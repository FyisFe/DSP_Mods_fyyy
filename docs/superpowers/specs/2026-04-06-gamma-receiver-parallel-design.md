---
name: Gamma Receiver Parallel
description: Parallelize the compute portion of photon-mode gamma ray receiver tick to remove the single-planet serial bottleneck
type: design
---

# Gamma Receiver Parallel

## Overview

In vanilla DSP, photon-mode gamma ray receivers are ticked inside `PowerSystem.GameTick`'s main generator loop, which runs sequentially for each planet's power system. When a single planet has thousands of photon-mode receivers, that one planet's worker thread becomes the bottleneck while other worker threads sit idle. This mod moves the thread-safe compute portion of `GameTick_Gamma` into a new parallel pass that piggybacks on the existing `FactoryBeforePowerGameTick_Parallel` phase, leaving the non-thread-safe belt I/O, entity sign, and entity anim writes in the original sequential loop.

Expected gain for the target scenario (single planet, thousands of photon-mode receivers): near-linear scaling of the compute portion across DSP worker threads. Belt I/O remains serial (because `CargoPath.TryInsertItem` is not thread-safe), but it is a small fraction of the per-receiver cost.

## Goals

1. Parallelize the compute work of photon-mode gamma ray receivers (`catalystPoint`, `productCount`, `warmup`, register writes) across DSP's worker threads.
2. Behavior equivalent to vanilla at the "imperceptible" level:
   - Long-term product output, energy consumption, and catalyst usage are bit-identical.
   - Short-term observables (per-tick productRegister/consumeRegister values, `keyFrame` phase) may differ slightly in timing within a tick, but all events still happen within the same tick.
3. Zero thread oversubscription: reuse DSP worker threads only; no `Parallel.For`, no `ThreadPool.QueueUserWorkItem`, no new background threads.
4. Works in both single-threaded and multi-threaded game modes (mod is only beneficial in MT mode but must not break ST mode).
5. Does not change behavior for power-mode gamma receivers (`productId == 0`) — they remain on the original code path.

## Non-Goals

- Parallelizing belt I/O (`InsertInto` / `PickFrom`). `CargoPath` is not thread-safe and the complexity of safe parallel belt I/O is out of scope.
- Parallelizing other generator types (wind, PV, fuel, geothermal). Not the bottleneck.
- Parallelizing within a single factory if the total across all factories is small. The mod adds a fixed overhead (build work list, cursor dispatch); below a threshold it is a net loss. We add a configurable threshold to gate activation.
- Changing `PowerGeneratorComponent` memory layout or `genPool` structure. We patch, not refactor.

## Architecture

### Tick Flow (changed region)

```
┌── EGameLogicTask.FactoryBeforePower (1201) ──────────────────────────┐
│                                                                       │
│   Each DSP worker runs:  FactoryBeforePowerGameTick_Parallel          │
│     ├── defenseSystem.ParallelGameTickBeforePower                     │
│     ├── digitalSystem.ParallelGameTickBeforePower                     │
│     ├── _power_gen_gamma_parallel   ← vanilla, EnergyCap_Gamma_Req    │
│     └── [Postfix patch from this mod]                                 │
│           └── GammaPrecomputePhase(__instance, threadOrdinal)         │
│                 ├── [first worker only] BuildWorkList(time)           │
│                 │       scan all factories → flat (fidx, gidx) list   │
│                 │       of gamma && productId > 0 receivers           │
│                 │       (other workers wait on ManualResetEventSlim)  │
│                 └── work-stealing loop:                                │
│                       int start = Interlocked.Add(&cursor, chunk)-chunk│
│                       for each (fidx, gidx) in [start, start+chunk):  │
│                         GammaCompute.Precompute(...)                  │
│                         sideBuffer.precomputedTick[gidx] = time       │
│                                                                       │
│   (all workers exit Postfix → DSP scheduler barrier → next task)     │
└───────────────────────────────────────────────────────────────────────┘

┌── EGameLogicTask.FactoryPowerSystem (1301) ──────────────────────────┐
│                                                                       │
│   Each DSP worker runs:  FactoryPowerSystemGameTick_Parallel          │
│     └── factory.powerSystem.GameTick(time, isActive, true, tOrd)      │
│           ├── consumer/node/exchanger loops unchanged                 │
│           ├── generator capacity loop [TRANSPILED at line 1533]       │
│           │     if (local.gamma && precomputed) {                     │
│           │         num29 = 0; num22 += 0;                            │
│           │         currentGeneratorCapacities[subId] += 0;           │
│           │     } else original EnergyCap_Gamma(response) + adds      │
│           └── main generator loop [TRANSPILED at line 1726]           │
│                 if (local.gamma && precomputed) {                     │
│                     GammaIOOnly.Run(ref local, sb.keyFrame[gidx],     │
│                                     factory, anim, signs, isActive);  │
│                 } else original GameTick_Gamma(...)                   │
│                                                                       │
│   Note: EnergyCap_Gamma_Req is NOT called from GameTick. It is called │
│   exclusively from RequestDysonSpherePower / _power_gen_gamma_parallel│
│   during the BeforePower phase.                                       │
└───────────────────────────────────────────────────────────────────────┘
```

### Module Layout

```
RayReceiverOptimization/
├── RayReceiverOptimization.csproj
├── RayReceiverOptimizationPlugin.cs    ← BepInEx entry, Harmony apply, config
├── GammaSideBuffer.cs                ← per-PlanetFactory metadata (precomputedTick[], keyFrame[])
├── GammaSideBuffers.cs               ← static dictionary factory.index → GammaSideBuffer
├── GammaWorkList.cs                  ← flat (fidx, gidx) table + Interlocked cursor + build latch
├── GammaCompute.cs                   ← Precompute(ref PowerGeneratorComponent, ...)
├── GammaIOOnly.cs                    ← belt I/O + sign + anim portion of GameTick_Gamma
└── Patches/
    ├── FactoryBeforePowerPatches.cs  ← Postfix on both parallel and single versions
    └── PowerSystemPatches.cs         ← Transpiler on PowerSystem.GameTick
```

## Data Structures

### GammaSideBuffer (per PlanetFactory)

```csharp
internal sealed class GammaSideBuffer
{
    // Indexed by genPool index. Length >= powerSystem.genCursor.
    public long[] precomputedTick;    // = currentTick ⇒ this entry was precomputed this tick
    public byte[] keyFrame;           // 1 / 0 — precompute records, IO reads

    public void EnsureCapacity(int genCursor)
    {
        if (precomputedTick == null || precomputedTick.Length < genCursor)
        {
            int newLen = Math.Max(64, Math.Max(genCursor, (precomputedTick?.Length ?? 0) * 2));
            Array.Resize(ref precomputedTick, newLen);
            Array.Resize(ref keyFrame, newLen);
        }
    }
}
```

Rationale for `long[] precomputedTick` instead of `bool[]`: avoids a per-tick `Array.Clear`. A stale entry from tick N is naturally invalidated when the IO phase checks `precomputedTick[gi] == currentTick` on tick N+1.

### GammaSideBuffers (global registry)

```csharp
internal static class GammaSideBuffers
{
    static readonly Dictionary<int, GammaSideBuffer> _map = new();

    // Called on main thread only: during BuildWorkList (which runs single-threaded
    // inside the first-worker-to-enter latch) and from PlanetFactory lifecycle hooks.
    public static GammaSideBuffer GetOrCreate(PlanetFactory factory)
    {
        if (!_map.TryGetValue(factory.index, out var sb))
        {
            sb = new GammaSideBuffer();
            _map[factory.index] = sb;
        }
        return sb;
    }

    public static GammaSideBuffer Get(int factoryIndex)
        => _map.TryGetValue(factoryIndex, out var sb) ? sb : null;

    public static void Clear() => _map.Clear();  // called on game unload
}
```

Created once per factory during BuildWorkList (main-thread, single-writer). Never concurrently mutated during the parallel phase — only `precomputedTick[gi]` and `keyFrame[gi]` are written, and those are slot-level writes on pre-sized arrays.

### GammaWorkList (global, rebuilt each tick)

```csharp
internal static class GammaWorkList
{
    internal static int[] factoryIdx;   // capacity grows 2x as needed
    internal static int[] genIdx;
    internal static int count;
    internal static int cursor;         // Interlocked.Add
    internal static long builtForTick = -1;

    internal static readonly ManualResetEventSlim buildLatch = new(false);
    internal static int buildClaim;     // 0 → 1 via Interlocked.CompareExchange
}
```

## Parallel Compute Phase

### GammaCompute.Precompute

Executes the compute-only portion of `GameTick_Gamma` plus the mutating body of `EnergyCap_Gamma(response)`. Must be called exactly once per tick per (factory, genIdx) and only for receivers where `gen.gamma == true && gen.productId > 0`.

```csharp
internal static class GammaCompute
{
    internal static void Precompute(
        ref PowerGeneratorComponent gen,
        float response,
        bool useCata,
        int[] productRegister,
        int[] consumeRegister)
    {
        // === Mutating portion of EnergyCap_Gamma(response) ===
        if (gen.warmupSpeed > 0f && response < 0.25f)
            gen.warmupSpeed *= response * 4f;
        gen.capacityCurrentTick = (long)((double)gen.capacityCurrentTick * (double)response);
        // Note: vanilla EnergyCap_Gamma returns 0 for photon mode, but capacityCurrentTick
        // is still consumed below by the productCount accumulator. Do NOT zero it.

        // === Compute-only portion of GameTick_Gamma ===

        // Catalyst consumption
        if (gen.catalystPoint > 0)
        {
            int num1 = gen.catalystPoint / 3600;
            if (useCata)
            {
                int num2 = gen.catalystIncPoint / gen.catalystPoint;
                --gen.catalystPoint;
                gen.catalystIncPoint -= num2;
                if (!gen.incUsed) gen.incUsed = num2 > 0;
                if (gen.catalystIncPoint < 0 || gen.catalystPoint <= 0)
                    gen.catalystIncPoint = 0;
            }
            int num3 = gen.catalystPoint / 3600;
            int delta = num1 - num3;
            if (delta != 0)
                Interlocked.Add(ref consumeRegister[gen.catalystId], delta);
        }

        // productCount accumulation
        if (gen.productCount < 20f)
        {
            int pc1 = (int)gen.productCount;
            gen.productCount += (float)gen.capacityCurrentTick / (float)gen.productHeat;
            int pc2 = (int)gen.productCount;
            if (pc2 != pc1)
                Interlocked.Add(ref productRegister[gen.productId], pc2 - pc1);
            if (gen.productCount > 20f) gen.productCount = 20f;
        }

        // warmup update
        gen.warmup += gen.warmupSpeed;
        if (gen.warmup > 1f) gen.warmup = 1f;
        else if (gen.warmup < 0f) gen.warmup = 0f;

        // NOT done here (deferred to IO phase):
        //   - entitySignPool[entityId].iconId0 / iconType
        //   - belt I/O (InsertInto / PickFrom)
        //   - entity anim updates
        //   - entitySignPool[entityId].signType
    }
}
```

### Postfix Patch: FactoryBeforePowerGameTick_Parallel

```csharp
[HarmonyPostfix]
[HarmonyPatch(typeof(GameLogic), nameof(GameLogic.FactoryBeforePowerGameTick_Parallel))]
static void FactoryBeforePowerGameTick_Parallel_Postfix(
    GameLogic __instance, int threadOrdinal, int threadCount)
{
    if (!RayReceiverOptimizationPlugin.Enabled.Value) return;

    long time = __instance.timei;

    // First worker to arrive builds the work list; others wait on the latch.
    if (Volatile.Read(ref GammaWorkList.builtForTick) != time)
    {
        if (Interlocked.CompareExchange(ref GammaWorkList.buildClaim, 1, 0) == 0)
        {
            GammaWorkList.buildLatch.Reset();
            try
            {
                BuildWorkList(__instance, time);
                Volatile.Write(ref GammaWorkList.cursor, 0);
                Volatile.Write(ref GammaWorkList.builtForTick, time);
            }
            finally
            {
                GammaWorkList.buildLatch.Set();
                Interlocked.Exchange(ref GammaWorkList.buildClaim, 0);
            }
        }
        else
        {
            GammaWorkList.buildLatch.Wait();
        }
    }

    // Short-circuit below the threshold: tiny total work ⇒ overhead > benefit.
    int total = GammaWorkList.count;
    if (total < RayReceiverOptimizationPlugin.MinReceiversThreshold.Value) return;

    int chunk = RayReceiverOptimizationPlugin.ChunkSize.Value;

    // Work-stealing loop (Interlocked.Add based).
    while (true)
    {
        int start = Interlocked.Add(ref GammaWorkList.cursor, chunk) - chunk;
        if (start >= total) break;
        int end = start + chunk; if (end > total) end = total;

        for (int i = start; i < end; i++)
        {
            int fidx = GammaWorkList.factoryIdx[i];
            int gidx = GammaWorkList.genIdx[i];
            PlanetFactory factory = __instance.factories[fidx];
            PowerSystem ps = factory.powerSystem;
            ref PowerGeneratorComponent gen = ref ps.genPool[gidx];

            // Double-check (state should not change between BuildWorkList and here,
            // but guards against edge cases like entity removal mid-frame).
            if (gen.id != gidx || !gen.gamma || gen.productId <= 0) continue;

            DysonSphere ds = factory.dysonSphere;
            float response = ds != null ? ds.energyRespCoef : 0f;
            bool useCata = time % 10L == 0L;

            FactoryProductionStat stat =
                GameMain.statistics.production.factoryStatPool[factory.index];

            GammaCompute.Precompute(
                ref gen, response, useCata,
                stat.productRegister, stat.consumeRegister);

            GammaSideBuffer sb = GammaSideBuffers.Get(factory.index);
            sb.keyFrame[gidx] = (byte)(((gidx + (int)(time % 90L)) % 90 == 0) ? 1 : 0);
            sb.precomputedTick[gidx] = time;
        }
    }

    // All workers return → DSP scheduler barrier → FactoryPowerSystem phase begins.
}
```

### BuildWorkList

Single-threaded pass run by the first worker to claim the build. Also resizes side buffers for any factory that will be touched this tick, so the parallel phase can rely on them being pre-sized.

```csharp
static void BuildWorkList(GameLogic gl, long time)
{
    int[] factoryIdx = GammaWorkList.factoryIdx;
    int[] genIdx = GammaWorkList.genIdx;
    int cap = factoryIdx?.Length ?? 0;
    int cnt = 0;

    for (int fi = 0; fi < gl.factoryCount; fi++)
    {
        PlanetFactory factory = gl.factories[fi];
        if (factory?.powerSystem == null) continue;

        PowerSystem ps = factory.powerSystem;
        int genCursor = ps.genCursor;
        if (genCursor <= 1) continue;

        GammaSideBuffer sb = GammaSideBuffers.GetOrCreate(factory);
        sb.EnsureCapacity(genCursor);

        PowerGeneratorComponent[] pool = ps.genPool;
        for (int gi = 1; gi < genCursor; gi++)
        {
            ref PowerGeneratorComponent gen = ref pool[gi];
            if (gen.id != gi || !gen.gamma || gen.productId <= 0) continue;

            if (cnt >= cap)
            {
                cap = Math.Max(256, cap * 2);
                Array.Resize(ref factoryIdx, cap);
                Array.Resize(ref genIdx, cap);
            }
            factoryIdx[cnt] = fi;
            genIdx[cnt] = gi;
            cnt++;
        }
    }

    GammaWorkList.factoryIdx = factoryIdx;
    GammaWorkList.genIdx = genIdx;
    GammaWorkList.count = cnt;
}
```

## PowerSystem.GameTick Transpile

Two call sites inside `PowerSystem.GameTick` need to become "skip if precomputed":

### Capacity loop — `EnergyCap_Gamma(response)` call site (PowerSystem.cs:1533)

Original IL sequence around line 1533:
```
ldloca.s    local              ; ref PowerGeneratorComponent
ldloc.s     response           ; float
call        EnergyCap_Gamma    ; long
stloc.s     num29
```

Transpile: locate the `callvirt/call EnergyCap_Gamma` and replace with a call to a mod helper that checks the precomputed flag:

```csharp
internal static class PowerSystemPatchHelpers
{
    public static long EnergyCap_Gamma_Routed(
        ref PowerGeneratorComponent local, float response, PowerSystem self)
    {
        if (local.productId > 0)
        {
            GammaSideBuffer sb = GammaSideBuffers.Get(self.factory.index);
            if (sb != null
                && local.id < sb.precomputedTick.Length
                && sb.precomputedTick[local.id] == self.factory.gameData.gameTick)
            {
                // Already precomputed this tick; vanilla returns 0 for photon mode.
                return 0L;
            }
        }
        return local.EnergyCap_Gamma(response);
    }
}
```

The transpiler replaces the `call EnergyCap_Gamma` instruction with `call EnergyCap_Gamma_Routed`. The helper's first argument is still `ref PowerGeneratorComponent local`, the second is `float response`, and we add a third: `this` (the PowerSystem instance). The transpiler must emit `ldarg.0` to push `this` before the call.

**Tick value source:** We read the tick from `GameMain.gameTick` (confirmed `public static long gameTick` in `GameMain.cs:98`). Globally accessible, no extra patching needed.

### Main generator loop — `GameTick_Gamma(...)` call site (PowerSystem.cs:1726)

Transpile the `callvirt GameTick_Gamma` to a routing helper:

```csharp
internal static class PowerSystemPatchHelpers
{
    public static void GameTick_Gamma_Routed(
        ref PowerGeneratorComponent local,
        bool useIonLayer, bool useCata, bool keyFrameUnused,
        PlanetFactory factory, int[] productRegister, int[] consumeRegister,
        PowerSystem self)
    {
        if (local.productId > 0)
        {
            GammaSideBuffer sb = GammaSideBuffers.Get(self.factory.index);
            if (sb != null
                && local.id < sb.precomputedTick.Length
                && sb.precomputedTick[local.id] == self.factory.gameData.gameTick)
            {
                bool keyFrame = sb.keyFrame[local.id] != 0;
                GammaIOOnly.Run(
                    ref local, useIonLayer, keyFrame, factory,
                    productRegister, consumeRegister);
                return;
            }
        }
        local.GameTick_Gamma(useIonLayer, useCata, keyFrameUnused, factory,
                              productRegister, consumeRegister);
    }
}
```

Original call at 1726 passes the computed `keyFrame` local. We ignore that parameter in the precomputed branch (use the one stored in the side buffer to avoid any mismatch), and forward it in the fallback branch. The signatures otherwise match so the transpiler just swaps the call instruction and pushes an extra `this`.

### Idempotency note

Our Precompute updates `gen.warmup` (via `warmup += warmupSpeed`). In vanilla, this update happens inside `GameTick_Gamma` during the main generator loop. If any other code reads `warmup` between our Precompute and the main generator loop — and computes something based on it — we would introduce drift. Confirmed by code inspection that nothing in `PowerSystem.GameTick` reads `gen.warmup` for gamma receivers between our Precompute and the main generator loop (`EnergyCap_Gamma` only touches `warmupSpeed` and `capacityCurrentTick`, not `warmup`). And `EnergyCap_Gamma_Req`, which does read `warmup`, is not called from `GameTick` — only from `RequestDysonSpherePower` / `_power_gen_gamma_parallel` which run strictly before our Precompute. Therefore the warmup-write reordering is safe.

### GammaIOOnly.Run

Mirrors the second half of `GameTick_Gamma`: sign icon writes, early return on non-keyframe full buffer, belt I/O, catalyst pickup. All compute is already done.

```csharp
internal static class GammaIOOnly
{
    internal static void Run(
        ref PowerGeneratorComponent gen,
        bool useIon, bool keyFrame,
        PlanetFactory factory,
        int[] productRegister, int[] consumeRegister)
    {
        // Sign icon updates (vanilla lines 362-369, guarded by productId)
        if (gen.productId > 0 && gen.productCount < 20f)
        {
            factory.entitySignPool[gen.entityId].iconId0 = (uint)gen.productId;
            factory.entitySignPool[gen.entityId].iconType = 1U;
        }
        // productId == 0 branch intentionally omitted: this method is only called
        // for photon-mode precomputed entries. For productId == 0 the fallback path
        // calls the original GameTick_Gamma.

        // Early return (vanilla line 372)
        if (!keyFrame && gen.productCount >= 20f) return;

        bool flag1 = gen.productId > 0 && gen.productCount >= 1f;
        bool flag2 = keyFrame & useIon && gen.catalystPoint < 72000;
        if (!(flag1 | flag2)) return;

        // Belt I/O block copied verbatim from vanilla GameTick_Gamma lines 378-466.
        bool isOutput1, isOutput2;
        int otherObjId1, otherObjId2;
        factory.ReadObjectConn(gen.entityId, 0, out isOutput1, out otherObjId1, out int _);
        factory.ReadObjectConn(gen.entityId, 1, out isOutput2, out otherObjId2, out int _);
        // ... identical to vanilla, using `gen` instead of `this` ...
    }
}
```

(The belt I/O block is ~90 lines; it is copied verbatim from `PowerGeneratorComponent.GameTick_Gamma` with `this.` → `gen.`.)

## Single-Threaded Fallback

The single-threaded `FactoryBeforePowerGameTick` (`GameLogic.cs:817`) calls `factory.powerSystem.RequestDysonSpherePower()` (`PowerSystem.cs:1311`) per factory, which internally iterates gamma generators and calls `EnergyCap_Gamma_Req`. So in ST mode the vanilla req pass still runs, just serially. By the time our ST Postfix executes, `warmupSpeed` and `capacityCurrentTick` on every gamma receiver are valid.

However, we **disable the mod entirely in ST mode**. Reasons:
1. ST mode has one thread running the logic frame. There is no parallelism to exploit.
2. The overhead of BuildWorkList + routing helpers is pure loss in ST mode.
3. ST users do not hit the "single planet, thousands of receivers" bottleneck because their total CPU budget is bounded by one thread anyway; vanilla's sequential gamma loop is not a special case.

In ST mode the mod runs a "null" path: no Postfix on `FactoryBeforePowerGameTick`, no work list built, no precomputed flags set. The transpiled `PowerSystem.GameTick` routing helpers see `sb == null || precomputedTick[id] != GameMain.gameTick` and fall through to the vanilla code path.

We achieve this automatically by only patching `FactoryBeforePowerGameTick_Parallel` (the MT variant) with our Postfix. In ST mode this patched method is never invoked — the game calls `FactoryBeforePowerGameTick` (no suffix) instead. Therefore no explicit MT detection is required. The Postfix on `_Parallel` is our implicit MT gate.

## Side Buffer Lifecycle

- **Creation**: lazy in `GammaSideBuffers.GetOrCreate`, called only from `BuildWorkList` (single-threaded section).
- **Resize**: in `BuildWorkList` before the parallel phase. Never during the parallel phase.
- **Reset on game load / unload**: hook `GameData.OnActive` / `GameData.OnGameBegin` (or equivalent) to call `GammaSideBuffers.Clear()` and reset `GammaWorkList`.
- **Save/load**: no persistent state. All state is rebuilt at tick start.

## Thread Safety Analysis

| Data | Reader | Writer | Synchronization |
|---|---|---|---|
| `GammaWorkList.factoryIdx/genIdx/count` | all workers (parallel phase) | single builder worker | `ManualResetEventSlim buildLatch` + `Interlocked.CompareExchange(buildClaim)` |
| `GammaWorkList.cursor` | all workers | all workers | `Interlocked.Add` |
| `GammaWorkList.builtForTick` | all workers | single builder worker | `Volatile.Read` / `Volatile.Write` |
| `GammaSideBuffers._map` | parallel phase (read via `Get`) | single builder in `BuildWorkList` | no locks needed: all writes (GetOrCreate insertions) happen during the single-threaded build phase; parallel phase only reads |
| `GammaSideBuffer.precomputedTick[i]` | PowerSystem.GameTick transpile helpers | parallel phase, slot `i` | single-writer per slot: entry `(fidx, gidx)` is unique in the work list; only one worker touches slot `i` |
| `GammaSideBuffer.keyFrame[i]` | PowerSystem.GameTick transpile helpers | parallel phase, slot `i` | single-writer per slot |
| `ps.genPool[gidx]` fields (productCount, catalystPoint, warmup, warmupSpeed, capacityCurrentTick, ...) | parallel phase (write), Loop 2/3 (read/write) | parallel phase (write), IO phase (write via GameTick_Gamma_Routed) | single-writer per slot in parallel phase; single-writer in IO phase (still serial per factory); these two phases are separated by the DSP scheduler barrier |
| `FactoryProductionStat.productRegister[idx]` | parallel phase (multiple workers may hit same idx on same factory) | parallel phase, IO phase | `Interlocked.Add` in parallel phase, vanilla `lock` in IO phase (the two phases are barrier-separated so they never contend) |
| `FactoryProductionStat.consumeRegister[idx]` | same | same | `Interlocked.Add` |
| `factory.entitySignPool[entityId]` | IO phase only | IO phase only | not touched by mod in parallel phase; vanilla rules apply |
| `factory.entityAnimPool[entityId]` | IO phase only | IO phase only | same |

### Race-free invariants

1. **Unique slot per entry**: `BuildWorkList` produces a list where each `(fidx, gidx)` pair appears exactly once (because we iterate a linear range of genPool and each slot is visited once). Therefore the work-stealing loop dispatches each slot to exactly one worker.

2. **No overlap between parallel and IO phase**: the DSP scheduler provides a barrier between `FactoryBeforePower` and `FactoryPowerSystem`. Any write the parallel phase makes happens-before any read in the IO phase.

3. **Side buffer sizing**: all `EnsureCapacity` calls happen in `BuildWorkList` (single thread) before the parallel phase. The parallel phase only writes to already-sized arrays.

4. **Register writes are commutative**: `productRegister[id] += delta` is associative/commutative. `Interlocked.Add` gives the same sum as serial addition modulo ordering.

### Subtle points

- **`EnergyCap_Gamma` side effect on `warmupSpeed`**: vanilla multiplies `warmupSpeed *= response * 4f` when `response < 0.25`. We replicate this in `Precompute`. If Loop 2's fallback path (for non-precomputed gens, e.g. during the first tick after the mod loads) calls `EnergyCap_Gamma` on a gen whose `warmupSpeed` was already modified, it would double-apply. Guarded by the precomputed flag, which is exclusively set/read within one tick.

- **`EnergyCap_Gamma_Req` is NOT called from `PowerSystem.GameTick`**. Verified by grepping `PowerSystem.cs` for call sites. The only callers are `RequestDysonSpherePower` (ST mode, via `FactoryBeforePowerGameTick`) and `_power_gen_gamma_parallel` (MT mode, via `FactoryBeforePowerGameTick_Parallel`). Both run strictly before our Precompute pass and write `warmupSpeed` / `capacityCurrentTick` via plain assignment (verified idempotent). So our Precompute updating `warmup` does not disturb any subsequent req-sum computation.

- **Receiver add/remove mid-tick**: UI-triggered changes are processed on the main thread, outside logic ticks. Between BuildWorkList and the end of FactoryPowerSystem, no receiver is added or removed.

## Behavior Equivalence

### Bit-identical across many ticks (steady state)

- Total product output per item type
- Total catalyst consumed
- Total energy requested from dyson sphere
- `warmup`, `productCount`, `catalystPoint` of each receiver at end of tick

### May differ by ≤ 1 tick but converges

- `productRegister` / `consumeRegister` absolute values within a given tick (reads that happen between parallel phase end and IO phase start will observe the parallel phase's contribution "earlier" than vanilla — acceptable because statistics reads are bucketed at the `Statistics(4001)` task, well after both phases).

### Unchanged

- Belt insertion / pickup order (all serial in `PowerSystem.GameTick` as vanilla)
- `keyFrame` schedule (computed from `(gidx + time % 90) % 90 == 0`, exactly vanilla)
- `entitySignPool` / `entityAnimPool` (only written in IO phase, identical code paths)
- Power-mode gamma receivers (`productId == 0`) — fall through to vanilla via the router helpers

## Configuration

BepInEx config (`FastTinderLaunch` style):

```csharp
Enabled = Config.Bind("General", "Enabled", true,
    "Enable parallel precompute for photon-mode gamma ray receivers");

MinReceiversThreshold = Config.Bind("General", "MinReceiversThreshold", 100,
    "Below this total count, mod skips parallel dispatch (overhead > benefit for small bases)");

ChunkSize = Config.Bind("General", "ChunkSize", 64,
    "Number of receivers processed per Interlocked.Add work-steal chunk");
```

No UI window (no UXAssist integration) in the first version. Follow-up work can add one if needed.

## Testing Strategy

### Correctness

1. **Empty base**: load a new game, verify zero exceptions, verify mod reports `count = 0` and takes the short-circuit path.
2. **Small base (< threshold)**: a few gamma receivers; verify they short-circuit and the original vanilla path runs.
3. **Single receiver photon mode**: place one gamma lens with photon recipe; verify product output matches vanilla over 1000 ticks (use a save file with debugging logs).
4. **Multiple receivers, single planet**: 100+ receivers producing photons; compare `factoryProductionStat.productRegister` / `consumeRegister` against a vanilla reference save over the same ticks.
5. **Multiple planets, mixed**: several planets each with gamma receivers; verify cross-factory parallelism does not cause register corruption.
6. **Hot toggle**: flip `Enabled` at runtime; verify no desync and the transpiler fallback path works both when flag is on and off.
7. **Save/load**: save during active gamma production, reload; verify `productCount` / `catalystPoint` persist correctly (no mod-introduced state drift).

### Performance

1. Baseline vs mod-enabled on a 1000-receiver single-planet test save:
   - Measure `DPEntry.PowerGenerator` timer (via game's built-in DeepProfiler).
   - Expect > 2x improvement on 4+ core CPU.
2. Verify no regression on bases with no gamma receivers.
3. Verify `DPEntry.PowerGamma` (the existing parallel req pass) is unchanged.

### Stress

1. 5000 receivers on one planet, 500 on each of 20 other planets, run for 10000 ticks, verify no exceptions / deadlocks / stuck `buildLatch`.
2. Stress-test the first-worker-builds pattern by deliberately injecting random sleeps in `BuildWorkList` to ensure other workers wait correctly.

## Out of Scope / Future Work

- Parallelizing power-mode gamma receivers (not a meaningful bottleneck).
- Parallelizing belt I/O for receivers whose outputs target disjoint `CargoPath`s.
- Replacing `Dictionary<int, GammaSideBuffer>` with a packed struct array once we have measurements confirming lookup overhead matters.
- Integrating with UXAssist's `MyConfigWindow` for an in-game toggle.
- Adding a DeepProfiler sample (`DPEntry.PowerGammaCompute`) for the new parallel phase.
