# InterstellarLogisticsOpt review

Reviewed 2026-09-09 against repository commit `d5fff59ee5d554d821333d37ebb211da86ea7b13`, ILO 1.1.0, and the installed game assembly MVID `ECE4A40E-5E73-43F4-A9F8-4E74970B5942`. The local `GameCode-latest` decompilation has the same MVID. Plugin source SHA-256: `1BB340936013C58B2C99D1E364B9407AF0A5172DD98DA07F83E2BAE6C421363E`.

This source review includes executable offline witnesses. Subsequent [game capture results](PROFILING.md) identify measured hotspots and comparison limits; they do not establish routing or throughput acceptance. These findings describe ILO 1.1.0. The current implementation retains required dispatch side effects and amortizes all priorities on a shared dispatch/lock clock, executing the complete native scheduler on each logical tick. UI/config events are unsubscribed on destruction. Current behavior and supported contracts are documented in [the package README](package/README.md); implementation validation is recorded in [PROFILING.md](PROFILING.md). The diagnostic implementation and run instructions are in [LogisticsProfiler](../LogisticsProfiler/README.md).

## Behavior changes and risks in 1.1.0

### P1: the dispatch early return drops priority-lock updates

Location: `InterstellarLogisticsOptPlugin.cs:138–146`, `DispatchPatch.DetermineDispatch_Prefix`.

`idleShipCount == 0 || energy <= 6000000` proves this station cannot launch a ship. It does not prove that `DetermineDispatch` has no other effects. In the game method, depleted suppliers and satisfied demand stations still cause `SetPriorityLock` calls on their counterpart's storage slot. These paths occur independently of the later launch-eligibility check. The method also advances `remotePairProcesses` after a full unsuccessful scan.

The offline check directly executes the installed game's `DetermineDispatch`: a shipless demand station with two empty suppliers writes a priority-1 lock with `lockTick=10` to both suppliers and advances its cursor from 0 to 1. Repeating with one idle ship and exactly 6 MJ also writes the counterpart lock. ILO's entry return omits both effects, including at `AmortizeFactor=1`.

These locks are read by other stations' dispatch and by returning ships' cargo selection. Therefore the comment calling this harmless in an idle steady state is too strong: dispatch priority and partner rotation can change as inventories or energy recover. The witness proves differing state; it does not establish a particular save's starvation or throughput loss.

Smallest correctness-first change: retain vanilla execution for lock-producing priority passes 1–4. Priorities 0 and 5 do not write locks because `SetPriorityLock` returns immediately there, although skipping them still changes cursor rotation. If exact vanilla behavior is required, preserve that rotation and the relevant method bookkeeping too, or optimize within the scan while retaining its side effects. Measure the reduced fast path before expanding it.

Source: local `StationComponent.cs:2939–2955, 3036–3050, 3210–3246, 3256–3279`; returning cargo selection at `2353–2452`.

### P1: dispersed, slower passes do not preserve the priority-lock schedule

Location: `InterstellarLogisticsOptPlugin.cs:88–110`.

For a station GID g, the replacement runs a priority pass when `time % (basePeriod * factor) == (g - 1) % (basePeriod * factor)`. Vanilla instead runs each due priority over the entire station pool before advancing to the next priority. GID-based dispersion removes that global ordering, and factor 2+ additionally lengthens the time between a station's passes.

`SetPriorityLock` still stores 10, 30 or 60; `InternalTickRemote` decrements the lock every update. A priority-1 pass scheduled every 50 ticks at factor 5 cannot rely on its own 10-tick lock lasting until its next pass. A different station's lower-priority pass can therefore run after a relevant lock has expired and before the next high-priority sweep. Other lock writes can shorten or eliminate the gap in a particular save, so this is a schedule incompatibility rather than a claim that every route fails.

The current implementation amortizes all due passes in native global priority/station order and pauses native lock aging between logical ticks. Return loading keeps native selection rules. This trades responsiveness, timing and potentially throughput while coordinating the priority protocol. A fully dispersed priority scheduler needs an explicit replacement for the cross-station priority protocol; simply multiplying lock lengths is insufficient to restore ordering, and `StationPriorityLock.lockTick` is a serialized byte. `60 * 5 = 300` already exceeds it.

Source: local `GalacticTransport.cs:175–204`, `StationComponent.cs:2548–2559, 3249–3279`, `StationPriorityLock.cs`.

### P2: amortization can cap new ship launches, beyond adding latency

Location: `InterstellarLogisticsOptPlugin.cs:91–97` and `package/README.md`.

Each successful `DetermineDispatch` invocation launches at most one ship before leaving its pair loop. An Ignore-mode station is called only at priority 0, once every `60 * factor` ticks. At 60 UPS its maximum rate of new launches by this scheduler is consequently:

| Factor | Dispatch interval | New launches per minute |
|---:|---:|---:|
| 1 | 1 s | 60 |
| 2 | 2 s | 30 |
| 5 | 5 s | 12 |
| 30 | 30 s | 2 |

This is a bound on that station's new launches, not all cargo moved through it: other stations can fetch/deliver, and returning ships can load cargo. Short routes and recovered stock can become dispatch-limited even when ships and energy are available. Long voyages with all ships already occupied may be largely unaffected.

The current README distinguishes delayed response from potential steady-state throughput reduction; the bound still applies to amortized ordinary calls. Validate production and deliveries across several round trips, not just a CPU screenshot.

Source: local `StationComponent.cs:3021–3031, 3161–3168, 3194–3206`.

## Superseded 1.1.0 claims and cleanup

- The implementation reduces a station's scheduled visits to 1/factor. It does not guarantee CPU time of exactly 1/factor: pair counts, available stock, early exits and successful dispatches determine the cost of each call. Flight updates and pair rebuilding are not amortized.
- Equal numbers of GIDs per tick do not guarantee equal work. A station with a large pair ring remains an indivisible expensive call, so “no spikes” is not a supported guarantee.
- Factor 1 retains vanilla `GalacticTransport.GameTick`, but the separate dispatch prefix remains active. Use `Enabled=false` as the baseline for both changes.
- `OnDestroy` unpatches Harmony but does not unsubscribe `MyConfigWindow.OnUICreated`. Pair that subscription with removal if plugin reinitialization/unloading is supported; this is lifecycle hygiene, not the main performance issue.
- The three identical `DetermineDispatch` call sites can share one eligibility condition. The lengthy patch-history comments can be replaced with the actual cadence and semantic tradeoffs. Neither cleanup merits a claimed speedup.

## Additional optimization opportunities

The [detailed 0.2.1 captures](PROFILING.md#detailed-dispatch-captures-logisticsprofiler-021) identified P5's redundant counterpart reads; ILO 1.2 implements their elimination. Planet 2704/P3 spends its visits on insufficient own supply plus counterpart lock maintenance. ILO 1.2 also avoids this maintenance when the target lock is already fully refreshed or cannot be replaced by the current priority; required refreshes remain native. Dispatch distance caching and reverse indexing do not address that hotspot. The table below describes the broader source-level opportunities, not a measured ranking.

Edit-time route and pair rebuilding belongs to BuildToolOpt and is excluded from this mod's optimization scope. The local BuildToolOpt 1.1.8 source at `../DSP_Mod/BuildToolOpt` (commit `fdb6e3573d41f78b3443d0c6021211d48585c956`) implements targeted `RefreshTraffic(keyStationGId)` for affected stations and their partners, routes station-to-station and astro/item changes through targeted refreshes, and repairs ship orders after station removal. This covers the proposed rebuild target; no duplicate rebuild optimization or profiler hook is included here.

That feature requires `BuildTool.EnableStationBuildOptimize=true` at startup; its default is false, and Nebula compatibility disables it. `RefreshTraffic(0)` still falls back to vanilla, and matching for a changed station still scans other stations rather than using a global item index. Those remaining edit-time costs stay within BuildToolOpt's scope. Its `UpdateShipStatus` repairs orders after edits; it does not optimize normal per-tick dispatch, flight or return-cargo selection.

BuildToolOpt source: `src/Optimization/GalacticTransport_Patch.cs:11–118, 120–210, 234–268, 320–455`; activation in `src/Plugin.cs:43, 95–98` and `src/Compatibility.cs:19–30`.

| Target | Evidence and possible change | Measurement and constraints |
|---|---|---|
| Priority-preserving dispatch fast path | Move launch-only work out of paths that cannot launch, retaining counterpart locks and cursor behavior. Remove repeated work only after establishing which state is invariant for this call. | Compare baseline no-idle/low-energy time against total dispatch time. A cheap early return already exists for an empty priority range; duplicating it is not a major optimization. |
| Reverse-direction pair lookup | A demand dispatch can scan its ring again looking for a supply shipment to the same partner. A partner index could reduce that search on dense networks. Returning ships also scan for a matching partner. | Preserve the first eligible match in the rotating ring, slot selection, locks, reservations and ordering. Rebuild the index with pair changes and station-ID reuse. The demand path leaves the outer loop after its eligible reverse search, so the nested loop does **not** by itself imply O(P²) work per call. |
| Flight and parked-ship updates | `InternalTickRemote` still runs every tick, including return cargo selection and `ShipRenderersOnTick`. The latter scans ship slots and updates idle ship poses even when no ship is flying. | Measure remote updates first, then separate rendering from flight/return work if material. Do not skip the whole remote method for `workShipCount=0`: it refills warpers and ages priority locks. The rendering helper also reconciles ship indices/counts; visual culling cannot discard those responsibilities. |
| Repeated planet-pair calculations | Many station pairs can share the same two planets. A per-tick planet-pair distance calculation may avoid repeated vector norms. | Planet positions change; an unbounded permanent station-pair cache adds stale data and memory cost. Distances feed range, warp and energy decisions, so preserve threshold semantics. Profile the arithmetic before adding a cache. |

Dispatch-only optimizations are implemented; flight/return lookup and distance caches remain deferred because the captures do not establish a sufficiently valuable target. Do not parallelize `DetermineDispatch`: different stations mutate the same storage/order state, energy and priority locks. Existing individual storage locks do not make the whole dispatch transaction safe for concurrent scheduling.

Local source anchors: `StationComponent.cs:1155–1230` (render data and ship bookkeeping), `1714–1750` (warper refill), `2353–2452` (return lookup), `3111–3206` (reverse search).

## SampleAndHoldSim and diagnostic scope

The local SAHS 0.7.7 transport patch runs `PlanetTransport.GameTick` for active and idle factories every game tick. This agrees with the [upstream StationLogic implementation](https://github.com/starfi5h/DSP_Mod/blob/master/SampleAndHoldSim/src/Logic/StationLogic.cs). Its update period is not the multiplier for the native priority-lock countdown. ILO's factor and SAHS's simulated-frame setting are separate controls.

The game's `GameLogic.GalacticTransportGameTick` already has a `DPEntry.Transport` sample with detail 99. Its built-in profiler can provide a broad starting point. [LogisticsProfiler](../LogisticsProfiler/README.md) adds entry-state, per-priority, per-station and skipped-original observations. Its optional detailed mode counts actual dispatch branches, including candidate visits and reverse search, with a checked method-body signature. It does not replace logistics logic.

Instrumentation follows Harmony's documented [state and original-execution injections](https://harmony.pardeike.net/articles/patching-injections.html). The installed HarmonyX was tested directly because current documentation is not proof of compatibility with an older runtime. Removal must update all of this profiler's patches on a target together: removing the last prefix while a `__runOriginal` finalizer remains caused an IL compile failure in the installed runtime.

Offline validation passed for concurrent aggregation, stop/drain, formatting, all three installed-game patch bindings, observation across a false prefix, original exception propagation, and complete patch removal. Release build passed with zero warnings/errors. Three completed game captures validate the F9 capture/output path; their measurements and remaining acceptance limits are documented in [capture results](PROFILING.md). Current capture comparisons and their control limitations are recorded in the profiling report.
