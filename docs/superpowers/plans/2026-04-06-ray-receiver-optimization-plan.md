# RayReceiverOptimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a BepInEx mod that parallelizes the compute portion of photon-mode gamma ray receivers across DSP's worker threads, removing the single-planet sequential bottleneck in `PowerSystem.GameTick`.

**Architecture:** Postfix-patch `GameLogic.FactoryBeforePowerGameTick_Parallel` to run a work-stealing parallel pass that pre-computes catalyst, productCount, warmup, and register writes for every photon-mode gamma receiver across all factories. Then transpile two call sites in `PowerSystem.GameTick` so the original sequential loop only does belt I/O, entity sign, and entity anim writes for receivers that were precomputed. Reuses DSP's worker threads via piggyback Postfix on the existing parallel phase; no new threads, no `Parallel.For`, no `ThreadPool` calls.

**Tech Stack:** C# (net472), BepInEx 5.x, HarmonyLib 2.x, DSP `Assembly-CSharp.dll` (game DLL referenced via HintPath).

**Reference spec:** `docs/superpowers/specs/2026-04-06-gamma-receiver-parallel-design.md` — read it before starting. Has full thread safety analysis and behavior equivalence guarantees.

**Pre-allocated project slot:** `DSP_Mods_fyyy.sln` already has a project entry for `RayReceiverOptimization` with GUID `{4AD80501-E1CD-47B0-9285-BFE82F15C9E6}` (line 8). The directory does not exist yet; this plan creates it.

**TDD note:** Game mods cannot be unit-tested in isolation (Harmony patches require a running game). The plan substitutes "build verification" for "test pass" at each step. The final task is a manual in-game smoke test.

---

## File Structure

```
RayReceiverOptimization/
├── RayReceiverOptimization.csproj            ← project file
├── RayReceiverOptimizationPlugin.cs          ← BepInEx entry, config, Harmony apply, lifecycle
├── GammaState.cs                             ← GammaSideBuffer, GammaSideBuffers, GammaWorkList
├── GammaCompute.cs                           ← Precompute (pure compute portion of GameTick_Gamma)
├── GammaIOOnly.cs                            ← Run (belt I/O + sign + anim portion)
├── PowerSystemPatchHelpers.cs                ← EnergyCap_Gamma_Routed, GameTick_Gamma_Routed
└── Patches/
    ├── FactoryBeforePowerPatches.cs          ← Postfix on _Parallel: BuildWorkList + work-stealing
    └── PowerSystemPatches.cs                 ← Transpilers on PowerSystem.GameTick
```

---

## Task 1: Create project skeleton and verify build

**Files:**
- Create: `RayReceiverOptimization/RayReceiverOptimization.csproj`
- Create: `RayReceiverOptimization/RayReceiverOptimizationPlugin.cs`

- [ ] **Step 1: Create the .csproj**

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <AssemblyName>RayReceiverOptimization</AssemblyName>
    <BepInExPluginGuid>org.fyyy.rayreceiveroptimization</BepInExPluginGuid>
    <Description>Parallelize photon-mode gamma ray receiver compute across DSP worker threads / 并行化光子模式射线接收站计算</Description>
    <Version>1.0.0</Version>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <LangVersion>latest</LangVersion>
    <RestoreAdditionalProjectSources>https://nuget.bepinex.dev/v3/index.json</RestoreAdditionalProjectSources>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="BepInEx.Core" Version="5.*" />
    <PackageReference Include="BepInEx.PluginInfoProps" Version="1.*" />
    <PackageReference Include="UnityEngine.Modules" Version="2022.3.62" IncludeAssets="compile" />
  </ItemGroup>

  <ItemGroup>
    <Reference Include="Assembly-CSharp">
      <HintPath>..\..\DSP_Mods\AssemblyFromGame\Assembly-CSharp.dll</HintPath>
    </Reference>
  </ItemGroup>

  <ItemGroup Condition="'$(TargetFramework.TrimEnd(`0123456789`))' == 'net'">
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />
  </ItemGroup>

</Project>
```

Note: this csproj is intentionally simpler than `FastTinderLaunch.csproj` — no UXAssist dependency, no UnityEngine.UI, no PostBuild zip. We can add those later if needed.

- [ ] **Step 2: Create the plugin entry file with minimal Awake/OnDestroy**

```csharp
using BepInEx;
using HarmonyLib;

namespace RayReceiverOptimization;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class RayReceiverOptimizationPlugin : BaseUnityPlugin
{
    public new static readonly BepInEx.Logging.ManualLogSource Logger =
        BepInEx.Logging.Logger.CreateLogSource(PluginInfo.PLUGIN_NAME);

    private Harmony _harmony;

    private void Awake()
    {
        _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
        // Patches added in later tasks
        Logger.LogInfo("RayReceiverOptimization loaded.");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
```

- [ ] **Step 3: Build the project**

Run from repo root:
```
dotnet build RayReceiverOptimization/RayReceiverOptimization.csproj -c Debug
```

Expected: `Build succeeded.` with 0 errors. Some warnings about missing icon/manifest are OK (we skip the PostBuild zip target).

If build fails because the `..\..\DSP_Mods\AssemblyFromGame\Assembly-CSharp.dll` HintPath does not exist, copy the game's `Assembly-CSharp.dll` from `DSPGAME_Data\Managed\` to a sibling repo `DSP_Mods\AssemblyFromGame\` (this is the convention used by `FastTinderLaunch.csproj` line 23).

- [ ] **Step 4: Commit**

```
git add RayReceiverOptimization/RayReceiverOptimization.csproj RayReceiverOptimization/RayReceiverOptimizationPlugin.cs
git commit -m "feat(RayReceiverOptimization): scaffold project with BepInEx plugin entry"
```

---

## Task 2: Add config entries

**Files:**
- Modify: `RayReceiverOptimization/RayReceiverOptimizationPlugin.cs`

- [ ] **Step 1: Add ConfigEntry fields and bindings to Awake**

Replace the body of the plugin class with:

```csharp
using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace RayReceiverOptimization;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class RayReceiverOptimizationPlugin : BaseUnityPlugin
{
    public new static readonly BepInEx.Logging.ManualLogSource Logger =
        BepInEx.Logging.Logger.CreateLogSource(PluginInfo.PLUGIN_NAME);

    public static ConfigEntry<bool> Enabled;
    public static ConfigEntry<int> MinReceiversThreshold;
    public static ConfigEntry<int> ChunkSize;

    private Harmony _harmony;

    private void Awake()
    {
        Enabled = Config.Bind("General", "Enabled", true,
            "Enable parallel precompute for photon-mode gamma ray receivers");
        MinReceiversThreshold = Config.Bind("General", "MinReceiversThreshold", 100,
            "Below this total count, mod skips parallel dispatch (overhead > benefit for small bases)");
        ChunkSize = Config.Bind("General", "ChunkSize", 64,
            "Number of receivers processed per Interlocked.Add work-steal chunk");

        _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
        // Patches added in later tasks
        Logger.LogInfo($"RayReceiverOptimization loaded. Enabled={Enabled.Value} MinThreshold={MinReceiversThreshold.Value} ChunkSize={ChunkSize.Value}");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
```

- [ ] **Step 2: Build**

```
dotnet build RayReceiverOptimization/RayReceiverOptimization.csproj -c Debug
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 3: Commit**

```
git add RayReceiverOptimization/RayReceiverOptimizationPlugin.cs
git commit -m "feat(RayReceiverOptimization): add config entries (enable, threshold, chunk)"
```

---

## Task 3: Create GammaState (side buffer + work list)

**Files:**
- Create: `RayReceiverOptimization/GammaState.cs`

- [ ] **Step 1: Create GammaState.cs with all three classes**

```csharp
using System;
using System.Collections.Generic;
using System.Threading;

namespace RayReceiverOptimization;

// Per-PlanetFactory side buffer. Owned by GammaSideBuffers; mutated only during the
// single-threaded BuildWorkList phase (Resize) and the parallel phase (slot writes).
internal sealed class GammaSideBuffer
{
    // Indexed by genPool index. precomputedTick[gi] == GameMain.gameTick means this
    // entry was precomputed this tick. Using long avoids per-tick Array.Clear.
    public long[] precomputedTick;
    public byte[] keyFrame;

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

// Global registry mapping factory.index → GammaSideBuffer.
// All inserts happen on the main thread inside BuildWorkList.
internal static class GammaSideBuffers
{
    private static readonly Dictionary<int, GammaSideBuffer> _map = new();

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

    public static void Clear() => _map.Clear();
}

// Flat (factoryIdx, genIdx) work list, rebuilt at the start of each tick by the
// first worker to enter the Postfix patch on FactoryBeforePowerGameTick_Parallel.
internal static class GammaWorkList
{
    internal static int[] factoryIdx;
    internal static int[] genIdx;
    internal static int count;
    internal static int cursor;
    internal static long builtForTick = -1;

    internal static readonly ManualResetEventSlim buildLatch = new(false);
    internal static int buildClaim;

    public static void Reset()
    {
        factoryIdx = null;
        genIdx = null;
        count = 0;
        cursor = 0;
        builtForTick = -1;
        buildLatch.Reset();
        buildClaim = 0;
    }
}
```

- [ ] **Step 2: Build**

```
dotnet build RayReceiverOptimization/RayReceiverOptimization.csproj -c Debug
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 3: Commit**

```
git add RayReceiverOptimization/GammaState.cs
git commit -m "feat(RayReceiverOptimization): add GammaSideBuffer, registry, and work list"
```

---

## Task 4: Implement GammaCompute.Precompute

**Files:**
- Create: `RayReceiverOptimization/GammaCompute.cs`

This task ports the compute-only portion of `PowerGeneratorComponent.GameTick_Gamma` (lines 336-371 of `GameCode-latest/PowerGeneratorComponent.cs`) plus the mutating body of `EnergyCap_Gamma(response)` (lines 313-319). It must NOT do belt I/O, sign pool icon writes, or entity anim updates.

- [ ] **Step 1: Create GammaCompute.cs**

```csharp
using System.Threading;

namespace RayReceiverOptimization;

internal static class GammaCompute
{
    // Pre-condition: gen.gamma == true && gen.productId > 0
    // Pre-condition: gen.warmupSpeed and gen.capacityCurrentTick already set by
    //                vanilla EnergyCap_Gamma_Req (called in BeforePower phase).
    // Post-condition: gen.warmup, gen.productCount, gen.catalystPoint, gen.warmupSpeed,
    //                 gen.capacityCurrentTick all updated. Belt state, sign pool, anim
    //                 pool NOT touched.
    internal static void Precompute(
        ref PowerGeneratorComponent gen,
        float response,
        bool useCata,
        int[] productRegister,
        int[] consumeRegister)
    {
        // === Mutating portion of EnergyCap_Gamma(response) ===
        // (Vanilla: PowerGeneratorComponent.cs:313-319)
        if (gen.warmupSpeed > 0f && response < 0.25f)
            gen.warmupSpeed *= response * 4f;
        gen.capacityCurrentTick = (long)((double)gen.capacityCurrentTick * (double)response);
        // Note: vanilla EnergyCap_Gamma returns 0 for photon mode (productId > 0),
        // but the side effect on capacityCurrentTick is what GameTick_Gamma actually
        // consumes for the productCount accumulator. Do NOT zero capacityCurrentTick.

        // === Compute-only portion of GameTick_Gamma ===
        // (Vanilla: PowerGeneratorComponent.cs:336-371)

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

        // NOT done here:
        //   - entitySignPool[entityId].iconId0 / iconType    (deferred to GammaIOOnly)
        //   - belt I/O (InsertInto / PickFrom)               (deferred to GammaIOOnly)
        //   - entity anim updates                            (deferred to PowerSystem.GameTick body)
        //   - entitySignPool[entityId].signType              (deferred to PowerSystem.GameTick body)
    }
}
```

- [ ] **Step 2: Build**

```
dotnet build RayReceiverOptimization/RayReceiverOptimization.csproj -c Debug
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 3: Commit**

```
git add RayReceiverOptimization/GammaCompute.cs
git commit -m "feat(RayReceiverOptimization): add GammaCompute.Precompute (compute-only portion of GameTick_Gamma)"
```

---

## Task 5: Implement GammaIOOnly.Run (belt I/O block)

**Files:**
- Create: `RayReceiverOptimization/GammaIOOnly.cs`

This task copies the belt I/O block from `PowerGeneratorComponent.GameTick_Gamma` (lines 362-466 of the vanilla source) verbatim, with two changes: (a) replace `this.` with `gen.`, (b) the sign-pool icon writes that come BEFORE the belt I/O are also brought into this method.

- [ ] **Step 1: Create GammaIOOnly.cs**

```csharp
namespace RayReceiverOptimization;

internal static class GammaIOOnly
{
    // Mirrors the second half of PowerGeneratorComponent.GameTick_Gamma:
    //   - sign-pool icon writes (vanilla lines 362-369)
    //   - early-return on full buffer (vanilla line 372)
    //   - belt I/O for output product and catalyst input (vanilla lines 374-466)
    //
    // Pre-condition: gen.productId > 0 (this method is only called for photon-mode receivers).
    //                Compute already done by GammaCompute.Precompute.
    internal static void Run(
        ref PowerGeneratorComponent gen,
        bool useIon,
        bool keyFrame,
        PlanetFactory factory,
        int[] productRegister,
        int[] consumeRegister)
    {
        // Sign-pool icon writes (vanilla lines 362-369)
        if (gen.productCount < 20f)
        {
            factory.entitySignPool[gen.entityId].iconId0 = (uint)gen.productId;
            factory.entitySignPool[gen.entityId].iconType = 1U;
        }
        // Note: vanilla also has a `if (productId == 0) { iconId0 = 0; iconType = 0; }`
        // branch but we are only called for productId > 0 — fallback handles the other case.

        // Early return on full buffer (vanilla line 372)
        if (!keyFrame && gen.productCount >= 20f) return;

        // === Belt I/O block — copied from vanilla GameTick_Gamma lines 374-466 ===
        bool flag1 = gen.productId > 0 && gen.productCount >= 1f;
        bool flag2 = keyFrame & useIon && gen.catalystPoint < 72000;
        if (!(flag1 | flag2)) return;

        bool isOutput1;
        int otherObjId1;
        factory.ReadObjectConn(gen.entityId, 0, out isOutput1, out otherObjId1, out int _);
        bool isOutput2;
        int otherObjId2;
        factory.ReadObjectConn(gen.entityId, 1, out isOutput2, out otherObjId2, out int _);

        bool flag3, flag4;
        if (otherObjId1 <= 0)
        {
            flag3 = false; flag4 = false; otherObjId1 = 0;
        }
        else
        {
            flag3 = isOutput1; flag4 = !isOutput1;
        }

        bool flag5, flag6;
        if (otherObjId2 <= 0)
        {
            flag5 = false; flag6 = false; otherObjId2 = 0;
        }
        else
        {
            flag5 = isOutput2; flag6 = !isOutput2;
        }

        byte remainInc = 0;
        if (flag1)
        {
            if (flag3 & flag5)
            {
                if (gen.fuelHeat == 0L)
                {
                    if (factory.InsertInto(otherObjId1, 0, gen.productId, (byte)1, (byte)0, out remainInc) == 1)
                    {
                        --gen.productCount;
                        gen.fuelHeat = 1L;
                    }
                    else if (factory.InsertInto(otherObjId2, 0, gen.productId, (byte)1, (byte)0, out remainInc) == 1)
                    {
                        --gen.productCount;
                        gen.fuelHeat = 0L;
                    }
                }
                else if (factory.InsertInto(otherObjId2, 0, gen.productId, (byte)1, (byte)0, out remainInc) == 1)
                {
                    --gen.productCount;
                    gen.fuelHeat = 0L;
                }
                else if (factory.InsertInto(otherObjId1, 0, gen.productId, (byte)1, (byte)0, out remainInc) == 1)
                {
                    --gen.productCount;
                    gen.fuelHeat = 1L;
                }
            }
            else if (flag3)
            {
                if (factory.InsertInto(otherObjId1, 0, gen.productId, (byte)1, (byte)0, out remainInc) == 1)
                {
                    --gen.productCount;
                    gen.fuelHeat = 1L;
                }
            }
            else if (flag5 && factory.InsertInto(otherObjId2, 0, gen.productId, (byte)1, (byte)0, out remainInc) == 1)
            {
                --gen.productCount;
                gen.fuelHeat = 0L;
            }
        }

        if (!flag2) return;

        byte stack;
        byte inc;
        if (flag4 && factory.PickFrom(otherObjId1, 0, gen.catalystId, (int[])null, out stack, out inc) == gen.catalystId)
        {
            gen.catalystPoint += 3600 * (int)stack;
            gen.catalystIncPoint += 3600 * (int)inc;
        }
        if (!flag6 || factory.PickFrom(otherObjId2, 0, gen.catalystId, (int[])null, out stack, out inc) != gen.catalystId)
            return;
        gen.catalystPoint += 3600 * (int)stack;
        gen.catalystIncPoint += 3600 * (int)inc;
    }
}
```

- [ ] **Step 2: Build**

```
dotnet build RayReceiverOptimization/RayReceiverOptimization.csproj -c Debug
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 3: Cross-check against vanilla**

Open `GameCode-latest/PowerGeneratorComponent.cs` lines 374-466 in a diff tool and compare with the belt I/O section above. Verify:
- All `this.X` references became `gen.X`
- Variable names (`flag1`..`flag6`, `otherObjId1`/`otherObjId2`, `remainInc`, `stack`, `inc`) match exactly
- No control flow lines were dropped
- The two `if (!flag2) return;` and final `if (!flag6 || ...) return;` are present

- [ ] **Step 4: Commit**

```
git add RayReceiverOptimization/GammaIOOnly.cs
git commit -m "feat(RayReceiverOptimization): add GammaIOOnly.Run (belt I/O + sign portion of GameTick_Gamma)"
```

---

## Task 6: Implement PowerSystemPatchHelpers (routing helpers)

**Files:**
- Create: `RayReceiverOptimization/PowerSystemPatchHelpers.cs`

These two static methods are the targets that the transpiler patches in Task 9 and Task 10 will redirect calls to. They check the precomputed flag and either skip / use the IO-only path, or fall through to the vanilla method.

- [ ] **Step 1: Create PowerSystemPatchHelpers.cs**

```csharp
namespace RayReceiverOptimization;

internal static class PowerSystemPatchHelpers
{
    // Replaces the call site at PowerSystem.cs:1533:
    //   num29 = local.EnergyCap_Gamma(response);
    // Vanilla returns 0 for photon mode (productId > 0). If we already precomputed
    // this receiver this tick, return 0 directly without re-applying the response
    // factor (which would double-scale capacityCurrentTick and warmupSpeed).
    public static long EnergyCap_Gamma_Routed(
        ref PowerGeneratorComponent local, float response, PowerSystem self)
    {
        if (local.productId > 0)
        {
            GammaSideBuffer sb = GammaSideBuffers.Get(self.factory.index);
            if (sb != null
                && local.id < sb.precomputedTick.Length
                && sb.precomputedTick[local.id] == GameMain.gameTick)
            {
                // Already precomputed: vanilla return value for photon mode is 0,
                // and capacityCurrentTick / warmupSpeed are already updated.
                return 0L;
            }
        }
        return local.EnergyCap_Gamma(response);
    }

    // Replaces the call site at PowerSystem.cs:1726:
    //   local.GameTick_Gamma(useIonLayer, useCata, keyFrame, factory,
    //                        productRegister, consumeRegister);
    // If the receiver was precomputed this tick, only run the belt I/O + sign portion.
    // Otherwise fall through to the vanilla method.
    public static void GameTick_Gamma_Routed(
        ref PowerGeneratorComponent local,
        bool useIonLayer, bool useCata, bool keyFrame,
        PlanetFactory factory, int[] productRegister, int[] consumeRegister,
        PowerSystem self)
    {
        if (local.productId > 0)
        {
            GammaSideBuffer sb = GammaSideBuffers.Get(self.factory.index);
            if (sb != null
                && local.id < sb.precomputedTick.Length
                && sb.precomputedTick[local.id] == GameMain.gameTick)
            {
                bool kf = sb.keyFrame[local.id] != 0;
                GammaIOOnly.Run(ref local, useIonLayer, kf, factory,
                                productRegister, consumeRegister);
                return;
            }
        }
        local.GameTick_Gamma(useIonLayer, useCata, keyFrame, factory,
                             productRegister, consumeRegister);
    }
}
```

- [ ] **Step 2: Build**

```
dotnet build RayReceiverOptimization/RayReceiverOptimization.csproj -c Debug
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 3: Commit**

```
git add RayReceiverOptimization/PowerSystemPatchHelpers.cs
git commit -m "feat(RayReceiverOptimization): add routing helpers for transpiled call sites"
```

---

## Task 7: Implement Postfix patch on FactoryBeforePowerGameTick_Parallel

**Files:**
- Create: `RayReceiverOptimization/Patches/FactoryBeforePowerPatches.cs`

This is the heart of the parallelization. The Postfix runs once per worker thread after `FactoryBeforePowerGameTick_Parallel` body completes (which includes the vanilla `_power_gen_gamma_parallel`). The first worker to arrive builds the work list; the others wait on a latch. Then all workers participate in a work-stealing loop that processes chunks of receivers.

- [ ] **Step 1: Create Patches/FactoryBeforePowerPatches.cs**

```csharp
using System;
using System.Threading;
using HarmonyLib;

namespace RayReceiverOptimization.Patches;

[HarmonyPatch(typeof(GameLogic))]
internal static class FactoryBeforePowerPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(GameLogic.FactoryBeforePowerGameTick_Parallel))]
    static void FactoryBeforePowerGameTick_Parallel_Postfix(
        GameLogic __instance, int threadOrdinal, int threadCount)
    {
        if (!RayReceiverOptimizationPlugin.Enabled.Value) return;

        long time = GameMain.gameTick;

        // === Step 1: First worker to arrive builds the work list ===
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
                catch (Exception e)
                {
                    RayReceiverOptimizationPlugin.Logger.LogError($"BuildWorkList failed: {e}");
                    GammaWorkList.count = 0;
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

        // === Step 2: Short-circuit below threshold ===
        int total = GammaWorkList.count;
        if (total < RayReceiverOptimizationPlugin.MinReceiversThreshold.Value) return;

        int chunk = RayReceiverOptimizationPlugin.ChunkSize.Value;
        if (chunk < 1) chunk = 1;

        // === Step 3: Work-stealing loop ===
        while (true)
        {
            int start = Interlocked.Add(ref GammaWorkList.cursor, chunk) - chunk;
            if (start >= total) break;
            int end = start + chunk;
            if (end > total) end = total;

            for (int i = start; i < end; i++)
            {
                int fidx = GammaWorkList.factoryIdx[i];
                int gidx = GammaWorkList.genIdx[i];
                PlanetFactory factory = __instance.factories[fidx];
                PowerSystem ps = factory.powerSystem;
                ref PowerGeneratorComponent gen = ref ps.genPool[gidx];

                // Double-check (state should not change between BuildWorkList and here)
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
    }

    // Single-threaded build pass. Called by the first worker to claim the build slot;
    // other workers wait on GammaWorkList.buildLatch. Pre-sizes side buffers for any
    // factory that has gamma generators, so the parallel phase can rely on them being
    // ready and never needs to call EnsureCapacity (which would be unsafe in parallel).
    static void BuildWorkList(GameLogic gl, long time)
    {
        int[] factoryIdxArr = GammaWorkList.factoryIdx;
        int[] genIdxArr = GammaWorkList.genIdx;
        int cap = factoryIdxArr?.Length ?? 0;
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
                    Array.Resize(ref factoryIdxArr, cap);
                    Array.Resize(ref genIdxArr, cap);
                }
                factoryIdxArr[cnt] = fi;
                genIdxArr[cnt] = gi;
                cnt++;
            }
        }

        GammaWorkList.factoryIdx = factoryIdxArr;
        GammaWorkList.genIdx = genIdxArr;
        GammaWorkList.count = cnt;
    }
}
```

- [ ] **Step 2: Wire the patch into Plugin.Awake**

Modify `RayReceiverOptimization/RayReceiverOptimizationPlugin.cs` Awake method, replacing the existing comment:

Find:
```csharp
        _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
        // Patches added in later tasks
        Logger.LogInfo($"RayReceiverOptimization loaded. Enabled={Enabled.Value} MinThreshold={MinReceiversThreshold.Value} ChunkSize={ChunkSize.Value}");
```

Replace with:
```csharp
        _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
        _harmony.PatchAll(typeof(Patches.FactoryBeforePowerPatches));
        Logger.LogInfo($"RayReceiverOptimization loaded. Enabled={Enabled.Value} MinThreshold={MinReceiversThreshold.Value} ChunkSize={ChunkSize.Value}");
```

- [ ] **Step 3: Build**

```
dotnet build RayReceiverOptimization/RayReceiverOptimization.csproj -c Debug
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 4: Commit**

```
git add RayReceiverOptimization/Patches/FactoryBeforePowerPatches.cs RayReceiverOptimization/RayReceiverOptimizationPlugin.cs
git commit -m "feat(RayReceiverOptimization): add Postfix patch with parallel work-stealing precompute"
```

---

## Task 8: Add transpiler patch for EnergyCap_Gamma call site

**Files:**
- Create: `RayReceiverOptimization/Patches/PowerSystemPatches.cs`

The transpiler walks `PowerSystem.GameTick`'s IL, finds the single instance call to `EnergyCap_Gamma`, inserts a `ldarg.0` (push `this` PowerSystem) immediately before it, and changes the call target to our static `EnergyCap_Gamma_Routed`.

- [ ] **Step 1: Create Patches/PowerSystemPatches.cs with the first transpiler**

```csharp
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace RayReceiverOptimization.Patches;

[HarmonyPatch(typeof(PowerSystem))]
internal static class PowerSystemPatches
{
    [HarmonyTranspiler]
    [HarmonyPatch(nameof(PowerSystem.GameTick))]
    static IEnumerable<CodeInstruction> GameTick_Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);

        var origEnergyCapGamma = AccessTools.Method(
            typeof(PowerGeneratorComponent),
            nameof(PowerGeneratorComponent.EnergyCap_Gamma));
        var routedEnergyCapGamma = AccessTools.Method(
            typeof(PowerSystemPatchHelpers),
            nameof(PowerSystemPatchHelpers.EnergyCap_Gamma_Routed));

        int patchCount1 = 0;

        for (int i = 0; i < codes.Count; i++)
        {
            // Match `call/callvirt EnergyCap_Gamma(float)` instance call
            if ((codes[i].opcode == OpCodes.Call || codes[i].opcode == OpCodes.Callvirt)
                && codes[i].operand is MethodInfo mi
                && mi == origEnergyCapGamma)
            {
                // Insert `ldarg.0` (push this PowerSystem) before the call
                codes.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
                i++; // skip past inserted instruction
                // Replace call target
                codes[i].opcode = OpCodes.Call;
                codes[i].operand = routedEnergyCapGamma;
                patchCount1++;
            }
        }

        if (patchCount1 != 1)
        {
            RayReceiverOptimizationPlugin.Logger.LogWarning(
                $"PowerSystem.GameTick transpiler: EnergyCap_Gamma replacements = {patchCount1} (expected 1)!");
        }
        else
        {
            RayReceiverOptimizationPlugin.Logger.LogInfo(
                "PowerSystem.GameTick transpiler: EnergyCap_Gamma → EnergyCap_Gamma_Routed (1 site)");
        }

        return codes;
    }
}
```

- [ ] **Step 2: Wire the patch into Plugin.Awake**

Modify `RayReceiverOptimization/RayReceiverOptimizationPlugin.cs`:

Find:
```csharp
        _harmony.PatchAll(typeof(Patches.FactoryBeforePowerPatches));
```

Replace with:
```csharp
        _harmony.PatchAll(typeof(Patches.FactoryBeforePowerPatches));
        _harmony.PatchAll(typeof(Patches.PowerSystemPatches));
```

- [ ] **Step 3: Build**

```
dotnet build RayReceiverOptimization/RayReceiverOptimization.csproj -c Debug
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 4: Commit**

```
git add RayReceiverOptimization/Patches/PowerSystemPatches.cs RayReceiverOptimization/RayReceiverOptimizationPlugin.cs
git commit -m "feat(RayReceiverOptimization): add transpiler for EnergyCap_Gamma call site"
```

---

## Task 9: Add transpiler patch for GameTick_Gamma call site

**Files:**
- Modify: `RayReceiverOptimization/Patches/PowerSystemPatches.cs`

Same approach as Task 8, but for the second call site (the heavy `GameTick_Gamma` call at vanilla `PowerSystem.cs:1726`). Both transpiler matches happen in the same pass over the IL.

- [ ] **Step 1: Extend the existing transpiler in PowerSystemPatches.cs**

Replace the entire body of `GameTick_Transpiler` with:

```csharp
    static IEnumerable<CodeInstruction> GameTick_Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);

        var origEnergyCapGamma = AccessTools.Method(
            typeof(PowerGeneratorComponent),
            nameof(PowerGeneratorComponent.EnergyCap_Gamma));
        var routedEnergyCapGamma = AccessTools.Method(
            typeof(PowerSystemPatchHelpers),
            nameof(PowerSystemPatchHelpers.EnergyCap_Gamma_Routed));

        var origGameTickGamma = AccessTools.Method(
            typeof(PowerGeneratorComponent),
            nameof(PowerGeneratorComponent.GameTick_Gamma));
        var routedGameTickGamma = AccessTools.Method(
            typeof(PowerSystemPatchHelpers),
            nameof(PowerSystemPatchHelpers.GameTick_Gamma_Routed));

        int patchCount1 = 0;
        int patchCount2 = 0;

        for (int i = 0; i < codes.Count; i++)
        {
            if ((codes[i].opcode == OpCodes.Call || codes[i].opcode == OpCodes.Callvirt)
                && codes[i].operand is MethodInfo mi)
            {
                if (mi == origEnergyCapGamma)
                {
                    codes.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
                    i++;
                    codes[i].opcode = OpCodes.Call;
                    codes[i].operand = routedEnergyCapGamma;
                    patchCount1++;
                }
                else if (mi == origGameTickGamma)
                {
                    codes.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
                    i++;
                    codes[i].opcode = OpCodes.Call;
                    codes[i].operand = routedGameTickGamma;
                    patchCount2++;
                }
            }
        }

        if (patchCount1 != 1 || patchCount2 != 1)
        {
            RayReceiverOptimizationPlugin.Logger.LogWarning(
                $"PowerSystem.GameTick transpiler: EnergyCap_Gamma={patchCount1}, GameTick_Gamma={patchCount2} (expected 1, 1)!");
        }
        else
        {
            RayReceiverOptimizationPlugin.Logger.LogInfo(
                "PowerSystem.GameTick transpiler: EnergyCap_Gamma + GameTick_Gamma routed (2 sites)");
        }

        return codes;
    }
```

- [ ] **Step 2: Build**

```
dotnet build RayReceiverOptimization/RayReceiverOptimization.csproj -c Debug
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 3: Commit**

```
git add RayReceiverOptimization/Patches/PowerSystemPatches.cs
git commit -m "feat(RayReceiverOptimization): add transpiler for GameTick_Gamma call site"
```

---

## Task 10: Add lifecycle reset hooks (game load / unload)

**Files:**
- Modify: `RayReceiverOptimization/Patches/FactoryBeforePowerPatches.cs` (or new file `LifecyclePatches.cs`)

State must be cleared when a new save loads, otherwise stale `GammaSideBuffer` entries from a previous game would point to invalid `factory.index` slots.

- [ ] **Step 1: Create Patches/LifecyclePatches.cs**

```csharp
using HarmonyLib;

namespace RayReceiverOptimization.Patches;

[HarmonyPatch]
internal static class LifecyclePatches
{
    // GameData.Destroy is called when leaving a save and going back to the main menu.
    // (Verified at GameCode-latest/GameData.cs:109; method body logs "GameData.Destroy()"
    // and frees all the major subsystems.)
    // We hook it to clear our static state so a subsequent save load doesn't see
    // stale factory.index → side buffer mappings.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameData), nameof(GameData.Destroy))]
    static void GameData_Destroy_Postfix()
    {
        GammaSideBuffers.Clear();
        GammaWorkList.Reset();
        RayReceiverOptimizationPlugin.Logger.LogInfo("Cleared gamma side buffers and work list (game data destroyed)");
    }
}
```

- [ ] **Step 2: Wire the patch into Plugin.Awake**

Modify `RayReceiverOptimization/RayReceiverOptimizationPlugin.cs`:

Find:
```csharp
        _harmony.PatchAll(typeof(Patches.FactoryBeforePowerPatches));
        _harmony.PatchAll(typeof(Patches.PowerSystemPatches));
```

Replace with:
```csharp
        _harmony.PatchAll(typeof(Patches.FactoryBeforePowerPatches));
        _harmony.PatchAll(typeof(Patches.PowerSystemPatches));
        _harmony.PatchAll(typeof(Patches.LifecyclePatches));
```

- [ ] **Step 3: Build**

```
dotnet build RayReceiverOptimization/RayReceiverOptimization.csproj -c Debug
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 4: Commit**

```
git add RayReceiverOptimization/Patches/LifecyclePatches.cs RayReceiverOptimization/RayReceiverOptimizationPlugin.cs
git commit -m "feat(RayReceiverOptimization): add game-data lifecycle reset hook"
```

---

## Task 11: Manual smoke test in game

**Files:** None (manual test).

- [ ] **Step 1: Build Release and copy DLL to BepInEx plugins folder**

```
dotnet build RayReceiverOptimization/RayReceiverOptimization.csproj -c Release
```

Copy the resulting DLL from `RayReceiverOptimization/bin/Release/net472/RayReceiverOptimization.dll` to your DSP install at:
```
<DSP install>/BepInEx/plugins/RayReceiverOptimization.dll
```

- [ ] **Step 2: Empty-base smoke test**

1. Launch DSP
2. Start a new game (or load an early save with no gamma receivers)
3. Open BepInEx console / log file
4. Verify these log messages appear at startup:
   - `RayReceiverOptimization loaded. Enabled=True ...`
   - `PowerSystem.GameTick transpiler: EnergyCap_Gamma + GameTick_Gamma routed (2 sites)`
5. Play for 30 seconds
6. Expected: no exceptions, no warnings about transpiler patch counts, game runs at normal FPS

- [ ] **Step 3: Single-receiver functional test**

1. Build a single gamma ray receiver
2. Equip a gravitational lens (catalyst)
3. Connect input belt with antimatter or critical photons (whichever the recipe needs in current version) — actually for photon mode you don't need an input, the lens is the catalyst
4. Connect output belt
5. Verify the receiver produces critical photons at the same rate as vanilla (eyeball: ~ 1 photon every few seconds at full warmup)
6. Verify catalyst depletes at the same rate
7. No exceptions in log

- [ ] **Step 4: Multiple-receiver stress test (if you have a save file ready)**

1. Load a save with 100+ photon-mode gamma receivers on one planet
2. Open the production statistics window for critical photons
3. Note the per-second production rate
4. Disable the mod (`Enabled = false` in `BepInEx/config/org.fyyy.rayreceiveroptimization.cfg`, or rename the DLL out of plugins folder)
5. Restart the game, load the same save, observe rate
6. Expected: rates match within ±1% (not exactly equal due to register write timing, but very close)

- [ ] **Step 5: Load/unload stability test**

1. Load a save
2. Save and exit to main menu
3. Load the same save again
4. Verify log shows `Cleared gamma side buffers and work list (game data destroyed)` between the two loads
5. Verify second load has no exceptions
6. Verify gamma receivers still produce photons after the reload

- [ ] **Step 6: If smoke test passes, commit no changes — task is verification only**

If anything fails: dig into the BepInEx log, identify the failing patch / transpiler / runtime exception, fix the code, rebuild, retest.

---

## Self-Review (after completing all tasks)

After implementing all tasks, run through this checklist:

1. **Spec coverage:** Map each section of `docs/superpowers/specs/2026-04-06-gamma-receiver-parallel-design.md` to the task that implements it:
   - "Goals" → all tasks collectively
   - "Module Layout" → Tasks 1-10 (one per file)
   - "Data Structures" → Task 3
   - "Parallel Compute Phase" → Tasks 4 (Precompute), 7 (Postfix dispatch + BuildWorkList)
   - "PowerSystem.GameTick Transpile" → Tasks 8, 9 (transpilers), 6 (helpers), 5 (IO body)
   - "Single-Threaded Fallback" → Task 7 (no patch on `_Parallel` means ST mode is implicitly bypassed)
   - "Side Buffer Lifecycle" → Tasks 3 (lazy creation in BuildWorkList), 10 (reset on game unload)
   - "Configuration" → Task 2

2. **Performance verification (deferred):** The spec promises ">2x improvement on 4+ core CPU" for 1000-receiver bases. This is NOT tested by Task 11 (which only verifies correctness). If you want quantitative perf data, take a `DPEntry.PowerGenerator` profiler dump from in-game (DSP has a built-in profiler accessible via `~` console).

3. **Known limitations to document in a future README:**
   - ST mode (`MultithreadingEnabled = false` in DSP options) is unaffected — mod is a no-op
   - Power-mode gamma receivers (`productId == 0`) follow the vanilla code path
   - Belt I/O is not parallelized (cannot be — `CargoPath.TryInsertItem` is not thread-safe)
