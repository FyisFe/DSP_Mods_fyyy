# InterstellarLogisticsOpt — Phase Dispersion (P0) Design

**Date:** 2026-06-02
**Status:** Approved (pending spec review)
**Scope:** Only the P0 相位分散 optimization from `docs/星际物流挂机性能.md` §5.

## Goal

Eliminate the uneven CPU spikes caused by interstellar (stellar/remote) logistics
scheduling. Currently every stellar station fires *in phase*: all ~N towers run on
the same ticks (`t%10`, `t%30`, `t%60`), producing a 6N-iteration spike every 60
ticks (once per second) and idle ticks the rest of the time. We spread the same
total work evenly across ticks **without changing each tower's scheduling
frequency**.

## Non-Goals

- No early-exit on `DetermineDispatch`, no `pair.trip` cache, no warper-slot cache,
  no active-tower list, no `Array.Copy`→swap, no event/dirty-queue (doc P1–P4).
- Local logistics (`PlanetTransport`) is untouched. We only patch the stellar
  scheduler `GalacticTransport.GameTick`.

## Root Cause (reference)

`GalacticTransport.GameTick` (`GalacticTransport.cs:185-204`) gates the entire
station sweep on `StationComponent.DetermineFramingDispatchTime(time, priorityIndex1)`,
which depends only on `time` — never on the station. So all towers of a given
priority are processed on the same tick.

## Approach

A single Harmony **Prefix** on `GalacticTransport.GameTick(long time)` that
reimplements the method body and returns `false` to skip the original.

The new body keeps the outer `priorityIndex1` 1→6 loop and the **exact** inner
`priorityIndex2` / `routePriority` dispatch branches. The only change: drop the
`DetermineFramingDispatchTime` gate and instead stride the station array by
`period`, offset by `phase = time % period`.

```csharp
[HarmonyPrefix]
[HarmonyPatch(typeof(GalacticTransport), nameof(GalacticTransport.GameTick))]
static bool GameTick_Prefix(GalacticTransport __instance, long time)
{
    if (!Enabled) return true; // run vanilla

    GameData gameData = __instance.gameData;        // mirror vanilla locals
    GalaxyData galaxy = gameData.galaxy;
    GameHistoryData history = gameData.history;
    PlanetFactory[] factories = gameData.factories;
    FactoryProductionStat[] factoryStatPool = gameData.statistics.production.factoryStatPool;
    TrafficStatistics traffic = gameData.statistics.traffic;
    float sailSpeedModified = history.logisticShipSailSpeedModified;
    float shipWarpSpeed = history.logisticShipWarpDrive
        ? history.logisticShipWarpSpeedModified : sailSpeedModified;
    int logisticShipCarries = history.logisticShipCarries;

    StationComponent[] stationPool = __instance.stationPool;
    int stationCursor = __instance.stationCursor;

    for (int priorityIndex1 = 1; priorityIndex1 < 7; ++priorityIndex1)
    {
        int priorityIndex2 = priorityIndex1 % 6;
        int period = (priorityIndex1 == 1) ? 10
                   : (priorityIndex1 == 2 || priorityIndex1 == 3) ? 30
                   : 60;
        int phase = (int)(time % period);

        for (int index = 1 + phase; index < stationCursor; index += period)
        {
            StationComponent sc = stationPool[index];
            if (sc == null || sc.id <= 0 || sc.gid != index) continue;

            if (priorityIndex2 >= 1 && priorityIndex2 <= 4 &&
                (sc.routePriority == ERemoteRoutePriority.Prioritize ||
                 sc.routePriority == ERemoteRoutePriority.Only ||
                 sc.routePriority == ERemoteRoutePriority.Designated))
                sc.DetermineDispatch(sailSpeedModified, shipWarpSpeed, logisticShipCarries, priorityIndex2, stationPool, factoryStatPool, factories, galaxy, traffic);
            else if (priorityIndex2 == 5 && sc.routePriority == ERemoteRoutePriority.Prioritize)
                sc.DetermineDispatch(sailSpeedModified, shipWarpSpeed, logisticShipCarries, priorityIndex2, stationPool, factoryStatPool, factories, galaxy, traffic);
            else if (priorityIndex2 == 0 && sc.routePriority == ERemoteRoutePriority.Ignore)
                sc.DetermineDispatch(sailSpeedModified, shipWarpSpeed, logisticShipCarries, priorityIndex2, stationPool, factoryStatPool, factories, galaxy, traffic);
        }
    }
    return false;
}
```

Note: `gameData` is read off `__instance`; the rest of the locals mirror the
vanilla method verbatim. `stationPool` and `stationCursor` are `public` fields on
`GalacticTransport` (verified), accessible directly.

## Why This Preserves Behavior

- **Frequency unchanged.** A station at array slot `index` is processed exactly when
  `index % period == time % period`, i.e. once every `period` ticks — identical
  cadence to vanilla's 10/30/60.
- **Per-tower lock timing holds.** `priorityLocks` decrement every tick; the lock
  value written equals `period`, which equals the tower's revisit interval
  (doc §2.4). So single-tower lock sequencing is preserved.
- **Peak shaved.** Per-tick outer load becomes `N/10 + 2·N/30 + 3·N/60 ≈ 0.217N`,
  flat, versus the vanilla 6N spike every 60 ticks (~96% peak reduction).

## Known Caveat (documented, accepted)

Cross-tower contention is no longer resolved in a single frame. In vanilla, multiple
suppliers competing for one demander resolve in gid order on the same tick; with
phase dispersion they land on different ticks, so "who wins this order" can differ.
Steady-state idle throughput is unaffected (cargo still flows, arguably more
fairly), but it is **not** frame-deterministic. Multiplayer / replay scenarios
should be evaluated by the user. This will be stated in the README.

## Config

BepInEx config, section `[General]`:

| Key | Type | Default | Effect |
|-----|------|---------|--------|
| `Enabled` | bool | `true` | When `false`, the Prefix returns `true` and vanilla `GameTick` runs unchanged. |

## Files

| File | Purpose |
|------|---------|
| `InterstellarLogisticsOpt/InterstellarLogisticsOpt.csproj` | net472, BepInEx.Core 5.*, Assembly-CSharp ref, PostBuild zip — mirrors `FullPhotonReceiver.csproj`. |
| `InterstellarLogisticsOpt/InterstellarLogisticsOptPlugin.cs` | Plugin class, config binding, the Harmony Prefix. |
| `InterstellarLogisticsOpt/package/manifest.json` | Thunderstore manifest (dep `xiaoye97-BepInEx-5.4.17`). |
| `InterstellarLogisticsOpt/package/icon.png` | Package icon (reuse/placeholder). |
| `InterstellarLogisticsOpt/package/README.md` | User-facing description + the determinism caveat. |

Plugin metadata: GUID `org.fyyy.interstellarlogisticsopt`, name `InterstellarLogisticsOpt`, version `1.0.0`.

## Testing / Verification

DSP mods require the game runtime; no isolated unit tests. Verification is:

1. **Build** the project in Release — must compile against `Assembly-CSharp.dll`.
2. **Manual in-game** (user-run): load a save with many stellar logistics towers;
   confirm (a) interstellar ships still dispatch and cargo flows, (b) per-frame CPU
   spikes flatten (frame-time graph / profiler). This is manual-only and called out
   as such.

## Future Extension

The name is intentionally broad. P1 (caches), P2 (active-tower list), P3
(swap-last), P4 (dirty queue) from the doc can be folded into this same mod later
behind their own config flags.
