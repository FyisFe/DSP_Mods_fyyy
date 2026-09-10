# Logistics capture results

## Release 1.2.0

The release retains 1.1.0's station-ID phase dispersion, plus the dispatch-state fixes and redundant-read optimization. It accepts cross-station priority differences. Lock aging and return loading remain native; there is no dispatch budget, shared clock or suspended scan. [The package README](package/README.md) owns the behavior and tradeoffs, and [CHANGELOG.md](package/CHANGELOG.md) describes the changes from 1.1.0.

The .NET Framework and game Mono checks pass 5,952 native/optimized dispatch-state comparisons and 24 branch scenarios. Scheduler checks use native route eligibility and dispatch arguments, then verify the GID phase contract for factors 1/2/5/30 over 1,800 ticks, exact 1/N call counts over complete cycles, live pool/configuration/time changes, unsupported-patch fallback, exception propagation and profiler accounting. These are offline checks; they do not establish save-level UPS, spike reduction or delivery throughput.

No game capture has yet tested this final combination. After DSPGAME exited, the release DLL listed below was installed into test1 and its SHA-256 verified against the build. The previous DLL (`8AD99E75DD668D29767CEC941811FDD6E988163A4FD6BB4135043A243CA75FB6`) and config were backed up beside their originals with suffix `.20260909191008.bak`. Settings remain `Enabled=true, AmortizeFactor=5`; LogisticsProfiler remains 0.2.2. A fresh comparison should follow [the capture procedure](../LogisticsProfiler/README.md#对照实验).

| Artifact | SHA-256 |
|---|---|
| Release 1.2.0 DLL | `67BDE49941D7A49C277E04C004C76E32A9EAFD8501E8FA0354D37AA6B422BDE9` |
| `package/InterstellarLogisticsOpt-1.2.0.zip` | `8C180E7D74040CBBFE3881B9F6A2C5069E6FC8AA601DC253F1C9E6F946825100` |

The release assembly is `1.2.0.0`, and the plugin and manifest versions are `1.2.0`. The package contains exactly the DLL, manifest, README, CHANGELOG and icon. The supplied screenshots remain unchanged; they show an earlier build and are not measurements of this final scheduler.

All sections below describe historical binaries identified by their version labels and hashes. Development labels, including an earlier build also named 1.2.0, are not current release identities. Their measurements must not be attributed to the final phase-dispersed release.

## Historical factor-5 comparison: 1.1.0 versus complete sweeps

| Metric | ILO 1.1.0 | Complete-sweep candidate (1.3.1) |
|---|---:|---:|
| Scheduler mean, ms/tick | 2.435457 | 0.699808 |
| Scheduler maximum, ms/call | 4.959900 | 161.906500 |
| Observed UPS | 7.885003 | 8.160370 |

The older capture is `20260909_223940_7611437`, using profiler 0.2.1. The newer capture is `20260910_004319_6959255`, using profiler 0.2.2 and development label 1.3.1; this complete-sweep scheduler has since been superseded. Both use factor 5, detailed 1/64 sampling, 180-second windows and SAHS period 1.

The newer capture has 71.3% lower average scheduler time and 3.5% higher observed UPS, but a substantially higher scheduler maximum. The complete ordered sweeps retain the priority protocol and can concentrate work into a single tick. Different starting stock/order states and profiler versions prevent treating these differences as a controlled version-to-version speedup. The observations support lower mean dispatch cost, not uniformly better frame times or proven delivery throughput. The README screenshots use a different SAHS setting and do not establish these version differences.

## Complete-sweep game captures: disabled versus factor 5

These captures use development ILO 1.3.1 (DLL SHA-256 `7BBB50F735DE6507F6FDE244F82AFC4ADFDA13AE66ADD1CE9FEB1FAF4C9A0D52`), LogisticsProfiler 0.2.2, DSP 0.10.34, detailed 1/64 sampling, SAHS period 1 and station cursor 12,358. Both complete normally after about 180 seconds, without pause, settings changes or diagnostic errors. Plugin versions and patch lists match. The only recorded configuration difference is `Enabled`; both retain factor 5, which is ignored while disabled. The game log contains no intervening reload, so these are successive windows of the same save, not replayed identical starting states.

| Capture | File prefix, UTC | Wall seconds | Tick begin | Tick end | Station/priority rows |
|---|---|---:|---:|---:|---:|
| Disabled | `20260910_003819_3801807` | 180.169979 | 275936067 | 275937493 | 31,913 |
| Factor 5 | `20260910_004319_6959255` | 180.138894 | 275938425 | 275939895 | 9,281 |

All 41,194 station/priority rows reconcile with the summaries: sample and branch counters, elapsed sums, maxima, candidate-visit partitions and successful launches agree. Scheduler samples equal tick deltas, and phase counts/time/maxima reconcile. Every recorded method error count is zero. Disabled mode executes the native scheduler on all 1,426 ticks. Factor 5 executes it on 294 of 1,470 ticks and skips the other 1,176, exactly matching the requested cadence. Phase groups retain 24/25 samples each, confirming that profiling still groups by actual simulation time.

| Metric | Disabled | Factor 5 |
|---|---:|---:|
| Scheduler mean, ms/tick | 6.422176 | 0.699808 |
| Scheduler maximum, ms/call | 261.342800 | 161.906500 |
| Observed UPS | 7.914748 | 8.160370 |
| Mean LateUpdate interval, ms | 255.197972 | 248.125195 |
| Maximum LateUpdate interval, ms | 626.155000 | 576.568600 |
| Sampled dispatch calls | 55,197 | 10,653 |
| Actual candidate visits within samples | 4,870,613 | 749,414 |
| Sampled new launches | 477 | 429 |
| Estimated remote-update time, ms/tick | 34.017459 | 32.291927 |

The observed scheduler mean falls 89.1% and its maximum falls 38.0%, while UPS rises 3.1%. These are within-run observations with changed stock/order states, not isolated causal estimates. The remaining 161.9 ms scheduler maximum is consistent with complete sweeps; this candidate did not split sweeps across ticks.

Sampled launches fall 10.1% in total, or 12.8% per simulation tick. Their P2/P3/P5 totals are 196/233/48 while disabled and 186/212/31 at factor 5. This does not establish an equal change in delivery throughput: launches are sampled, cargo amounts and return loading are not measured, and the starting states differ. Each capture covers only about 24 seconds of simulated time. P0 has no samples and every P1/P4 sample has an empty candidate interval, leaving live successful dispatch for those priorities unexercised in this save.

No ILO/profiler patch or runtime failure is recorded. The existing BepInEx 5.4.20 target versus 5.4.17 host warning remains, along with unrelated BuildToolOptCAPIcompat/BlueprintTweaks and DeliverySlotsTweaks/UXAssist startup compatibility messages. This capture supports runtime compatibility and coherent dispatch accounting for the tested profile; it does not prove every priority decision or long-term delivery outcome.

Source directory: `test1/BepInEx/LogisticsProfiler`. SHA-256 identities:

| Capture | Summary | Stations |
|---|---|---|
| Disabled | `A1BEE7A5F788B71216CFDD446FA56E253B19CD0B83A025E279360554ACA3BFDD` | `536B0B4CA386A35F2FB4FA1B7C6089EA38DB904F6AFE9BFD8D64F22C4A734553` |
| Factor 5 | `9C6A14ACFCB19685A6C9D524EABE8D541AE1300BEF5756A8D3EECFFA35120964` | `35EBA80C071EF99C060199184F74E9318D6C9C9F0B11467C8D5248EA88403623` |

## ILO 1.3.1: complete native sweeps

This development build has no dispatch budget, cross-tick scan cursor or return-cargo priority clamp. The coefficient still covers every priority, and native priority-lock aging follows the same logical clock. On each logical tick the mod supplies that clock to the original `GalacticTransport.GameTick`; the original method owns the complete ordered sweep. Waiting ticks skip dispatch and lock aging. Return-cargo selection remains native on every simulation tick. Current controls and limits are maintained in [the package README](package/README.md).

Validation:

- Release build passed with zero warnings/errors; the ZIP contains the DLL, manifest, README and icon at its root, with matching build/source bytes and version 1.3.1.
- .NET Framework and the game's Mono each passed 5,952 dispatch-state comparisons and 24 exact branch scenarios, plus native schedule/lock-clock comparisons for factors 1/2/5/30.
- Checks cover native return selection on dispatch and waiting ticks, factor-30 lock countdown, exception propagation, factor/time reset, unsupported-clock fallback, unload and profiler accounting. Profiler phase groups retain the actual simulation tick even though the native scheduler receives logical time.
- Full flight-method Harmony binding is checked on .NET Framework. Headless Mono executes the extracted native priority blocks; the subsequent game captures above verify live binding and accounting in test1.

Installed into test1 with DSPGAME stopped. The old DLL and config are backed up beside their originals with suffix `.20260909173151.bak`. The installed DLL matches the build. Obsolete `DispatchBudgetMs` and `DispatchEarlyExit` settings are removed from that profile; `Enabled=true` and `AmortizeFactor=5` are retained. LogisticsProfiler 0.2.2 is unchanged.

| Artifact | SHA-256 |
|---|---|
| InterstellarLogisticsOpt 1.3.1 DLL | `7BBB50F735DE6507F6FDE244F82AFC4ADFDA13AE66ADD1CE9FEB1FAF4C9A0D52` |
| `package/InterstellarLogisticsOpt-1.3.1.zip` | `169E8EB92A20ED76497BE10D777A53D767E9404247964D48B2E397A0AD546F4F` |

Complete sweeps can still produce scheduling spikes; this build does not promise spike removal. Long-term deliveries and return utilization remain unverified.

## ILO 1.3.0 game captures: budget 2 versus 0 ms

Two 180-second captures use ILO 1.3.0, LogisticsProfiler 0.2.2, factor 5, detailed 1/64 sampling and SAHS period 1. Budget 2 was tested first, then budget 0 in the same running save. Both finish normally, with no pause, settings change or diagnostic error. Plugin and patch lists match; the only recorded configuration difference is the budget. The installed ILO DLL still matches the 1.3.0 SHA-256 below.

| Capture | File prefix, UTC | Wall seconds | Tick begin | Tick end | Station/priority rows |
|---|---|---:|---:|---:|---:|
| Budget 2 ms | `20260910_001200_6987626` | 180.099762 | 275936208 | 275937704 | 7,014 |
| Budget 0 | `20260910_001549_2459743` | 180.003512 | 275938066 | 275939555 | 10,157 |

All 17,171 station/priority rows reconcile with the priority summaries: integer counters and elapsed totals agree, candidate visits partition into the recorded branches, and successful launch attempts reconcile with ship-count deltas. Scheduler samples equal tick increments, and its 60 phase groups reconcile with the total and maximum. All recorded method error counts are zero. The game log lists the priority-clock transpiler on `InternalTickRemote`, records both completed captures and reports no ILO/profiler patch or runtime failure. This establishes live binding and execution for the installed profile; it does not observe every priority-lock or return-loading decision. The log also warns that ILO targets BepInEx 5.4.20 while the profile runs 5.4.17. Unrelated BuildToolOptCAPIcompat/BlueprintTweaks and DeliverySlotsTweaks/UXAssist startup compatibility messages remain.

| Metric | Budget 0 | Budget 2 ms |
|---|---:|---:|
| Scheduler mean, ms/tick | 0.760534 | 0.720007 |
| Scheduler maximum, ms/call | 153.232200 | 2.277400 |
| Observed UPS | 8.272061 | 8.306507 |
| Mean LateUpdate interval, ms | 244.902735 | 243.377789 |
| Maximum LateUpdate interval, ms | 478.876100 | 469.006600 |
| Sampled dispatch calls | 11,637 | 7,596 |
| Actual candidate visits within samples | 933,821 | 630,176 |
| Sampled new launches | 536 | 421 |
| Estimated remote-update time, ms/tick | 32.841765 | 31.675183 |

The observed scheduler maximum falls 98.5%, while its mean falls 5.3% and UPS changes only 0.4%. Budget 0 concentrates most scheduling time in two `time % 60` groups (3 and 33), with maxima of 71.5671 and 153.2322 ms. With budget 2, all phase-group maxima are at most 2.2774 ms. This is direct evidence that station-level slicing spreads the concentrated scheduler work in this run. It is not a hard 2 ms guarantee or evidence that whole-game stutters are solved: the maximum LateUpdate interval remains about 469 ms. The budget-2 scheduler accounts for about 0.60% of capture wall time.

| Priority | Dispatch samples, budget 0 / 2 | Sampled launches, budget 0 / 2 |
|---|---:|---:|
| P0 | 0 / 0 | 0 / 0 |
| P1 | 5,866 / 3,620 | 0 / 0 |
| P2 | 1,913 / 1,164 | 226 / 182 |
| P3 | 1,915 / 1,252 | 254 / 194 |
| P4 | 936 / 810 | 0 / 0 |
| P5 | 1,007 / 750 | 56 / 45 |

The throughput tradeoff needs attention. Dispatch samples per simulation tick are 35.0% lower with budget 2; sampled launches are 21.5% lower in total, or 21.8% lower after tick normalization. These observations are consistent with the scheduler's shared clock pausing during sliced sweeps. They are not proof of an equal reduction in deliveries: calls and launches are sampled, the two windows cover different inventory/order states, and return cargo and delivered amounts are not measured. P1/P4 have empty candidate intervals in every sample, and P0 has no samples, so this save does not exercise successful dispatch for those priorities. Each capture spans only about 25 seconds of simulated time at 60 ticks per simulated second.

Budget 2 demonstrated spike reduction together with fewer sampled dispatches and launches; it did not establish acceptable sustained throughput. These captures document the slicing implementation retired in 1.3.1. The comparison procedure for the current release is maintained in [LogisticsProfiler/README.md](../LogisticsProfiler/README.md#对照实验).

Source directory: `test1/BepInEx/LogisticsProfiler`. SHA-256 identities:

| Capture | Summary | Stations |
|---|---|---|
| Budget 2 | `13681962DA92291E1300A1E11BB9218B076654BE71EA231D3F6E1173929EC485` | `A593DF7F229661E2616ADB7D7E2A764235640D857D4852A1463A0010B05A88BE` |
| Budget 0 | `A86D4BAEF88AFA3A2153411CB1B470F2FE643C5F97111F19389FD9D64E9395B8` | `1FC2B341F510B14158A530D2C578A33ACB3E2F83DB5F7E8F183B8D480CC19177` |

## ILO 1.3.0: all-route clock and frame budget

The coefficient now covers P1-P4 as well as P0/P5. At factor > 1, a logical dispatch tick visits all due priorities in native pass/station order. A shared clock controls both dispatch cadence and native priority-lock aging. Without slicing delays, factors 1/2/5/30 reproduce the corresponding time-scaled native schedule; factor 1 and disabled mode use the native scheduler directly. Lock counters retain their native byte values, avoiding `60 * factor` overflow.

`DispatchBudgetMs=2` is the default soft budget when factor > 1. Scanning resumes from the next station after a budget yield, without duplicating completed station calls or queuing more logical ticks. The clock and lock countdown pause until the due sweep finishes. Return-cargo selection can use the active priority or a higher priority; it cannot overtake an unfinished higher-priority departure sweep. Existing flight movement, unloading, order updates and warper replenishment remain native. Return ships do not wait for deferred lower-priority loading and may return empty; delivery and return utilization remain part of acceptance.

This addresses concentration of a whole station sweep into one frame. It does **not** establish a hard 2 ms limit: an indivisible station call, GC or OS scheduling may exceed the soft budget. Under sustained load, actual dispatch frequency falls below the nominal `1/factor`; there is one paused logical sweep rather than an accumulating backlog. Budget 0 disables slicing, providing a direct comparison of all-route amortization versus amortization plus slicing.

Validation passed on the installed game assembly:

- Release build: zero warnings/errors; ZIP root layout, manifest version and DLL/README identity verified.
- .NET Framework and game Mono: existing 5,952 real dispatch-state comparisons and 24 exact branch scenarios; scaled all-priority traces for factors 1/2/5/30; budgeted continuation without duplicates/missing calls; clock holds and return gates; propagated dispatch exceptions; factor/time reset and game-unload cleanup.
- Both runtimes execute the actual native return-priority selector and lock-aging IL blocks with the new guards. Tests cover each route mode, allowed-priority limits, suspended aging, the factor-30 case and the native zero-then-clear countdown boundary. Unsupported return-selection structure rejects both remote-method edits together.
- Full `InternalTickRemote` Harmony binding together with the profiler is checked on .NET Framework. Headless Mono lacks Unity's complete native flight environment and verifies the extracted native blocks. The game captures above additionally establish live binding and execution in test1; return utilization and sustained delivery acceptance remain open.

Artifacts:

| Artifact | SHA-256 |
|---|---|
| InterstellarLogisticsOpt 1.3.0 DLL | `8A3991373597791E783791FAD35B31F5CC71508FFD0C833A2F7D78B4AA6F6FAF` |
| `package/InterstellarLogisticsOpt-1.3.0.zip` | `41FE8393C76E653D28C0308468C75FE5AED5798047EF30B85576CDA430C32678` |

Installed into test1 with DSPGAME stopped; the installed DLL matches the build. LogisticsProfiler 0.2.2 is unchanged. The game captures above compare budgets 0 and 2 ms. Detailed observer overhead and unrelated game work can still affect maxima. The complete comparison procedure is maintained in [LogisticsProfiler/README.md](../LogisticsProfiler/README.md#对照实验).

## ILO 1.2.1: priority-preserving amortization

`AmortizeFactor` is available again with a narrower contract: P1-P4 keep the complete vanilla 10/30/60-tick sweeps and global station/priority order. Only ordinary P0/P5 station groups rotate across native 60-tick boundaries, once per station every `60 * factor` ticks. All due prioritized passes finish before ordinary dispatch. The lock lifetimes, required lock maintenance and redundant-read optimization remain unchanged. This preserves the native priority mechanism while trading ordinary dispatch responsiveness and potentially throughput for CPU; it does not promise identical delivery history or remove prioritized scheduling spikes.

Release build passed with zero warnings/errors. .NET Framework and the game's Mono passed the existing 5,952 dispatch-state comparisons and 24 branch scenarios, plus 1,800-tick scheduler trace comparisons for every route mode, disabled/enabled states and factors 1/2/5/30. Every prioritized call retains the vanilla tick, station order and dispatch arguments. Ordinary calls have the requested interval; empty/removed/mismatched pool slots are excluded, and profiler scheduler accounting remains valid. The ZIP layout, DLL/README identity and manifest version were verified. Microbenchmark timings from this run overlapped an active game process and are not used as new performance evidence.

| Artifact | SHA-256 |
|---|---|
| InterstellarLogisticsOpt 1.2.1 DLL | `AF2C2BE8BEA2622EB87227F4E0E58F78B190D0024CA13465A5304FB0109B78D6` |
| `package/InterstellarLogisticsOpt-1.2.1.zip` | `44A2CAD49E89B63CD04CA34B56BF9449E5AACDD45234EA382EA3FEF32A002EBA` |

Installed into test1 after DSPGAME exited; the installed DLL hash matches the build. The previous 1.2.0 DLL is backed up beside it as `InterstellarLogisticsOpt.dll.20260909162650.bak`. Existing settings remain `Enabled=true, AmortizeFactor=5`. LogisticsProfiler 0.2.2 is unchanged and its installed hash still matches the validated artifact. This version is superseded by 1.3.0; use the current comparison procedure above.

## ILO 1.2.0 enabled capture: 20260909_231601_5106788

One new capture was available: ILO 1.2.0 enabled, LogisticsProfiler 0.2.2, 180.011376 seconds, 1/64 detailed sampling, SAHS period 1. The legacy factor setting was not bound by 1.2.0 and did not affect this run. The capture finished by duration with no pause, configuration changes or diagnostic errors. All 31,746 station rows reconcile with priority totals; visited-branch partitions and launch attempts/results agree. Neither ILO nor the profiler logged a patch or runtime failure. The log also contains an unrelated DeliverySlotsTweaks/UXAssist compatibility warning; that plugin is outside this change.

The old disabled capture below used ILO 1.1.0 disabled and profiler 0.2.1. Both captures name the same save and have overlapping tick ranges (old 275936216-275937604, new 275936089-275937495), but this is not a same-version paired A/B test. No new 1.2.0 disabled capture was present.

| Metric | Earlier disabled | 1.2.0 enabled |
|---|---:|---:|
| Scheduler mean | 6.274536 ms/tick | 4.725796 ms/tick |
| Scheduler maximum | 239.4397 ms | 272.2795 ms |
| Observed UPS | 7.699842 | 7.810617 |
| P3 estimated time per tick | 3.769167 ms | 2.519147 ms |
| P5 estimated time per tick | 1.884168 ms | 1.624280 ms |
| Sampled dispatch launches | 452 | 459 |
| Planet 2704/P3 candidate visits | 322,967 | 339,325 |
| Planet 2704/P3 lock-function calls | 297,127 | 2,670 |

Scheduler mean is 24.7% lower and observed UPS 1.4% higher in this comparison. These are observations, not an isolated causal speedup. The hotspot's repeated lock calls fall from about 0.920 to 0.00787 per visit, consistent with redundant-refresh elimination. Its elapsed time per visit falls from about 60.1 to 28.2 ns. The maximum scheduler call did not improve; concentrated vanilla sweeps remain. No obvious diagnostic or launch-accounting anomaly is present, but this short capture does not establish long-term delivery/priority acceptance.

Source: `test1/BepInEx/LogisticsProfiler/20260909_231601_5106788_{metadata.txt,summary.tsv,stations.tsv}`. Summary SHA-256 `B57EC52134A519D18DFDDDDF10971A1A455A76082BB0B45BF62C9AA844D674A5D`; station data SHA-256 `C247F4BA80F53FA21ED56F49BBE46E26D459CE1F78AD6E7E08215FB39C2DA4CC`.

## ILO 1.2.0 implementation validation

ILO 1.2.0 restores vanilla scheduler cadence and global priority order. The whole-method ship/energy early return and `AmortizeFactor` are removed; old factor config entries are ignored. Required priority-lock writes and pair-cursor advancement remain native. UXAssist and config event subscriptions are removed on destruction.

The two own-supply/own-demand failure branches now bypass counterpart storage reads when `SetPriorityLock` cannot change state: priorities 0/5, an existing higher-priority lock, or an identical lock already at its full 10/30/60-tick lifetime. This targets the measured P5 failure paths and P3 lock maintenance without a cache. It relies on native main-thread dispatch before the `FactoryBeforeTick` barrier; the later `FactoryTransport` phase owns lock aging and return-cargo lock writes. It does not patch the latter, where workers can run concurrently. Source: `GameLogic.cs:192`, `EGameLogicTask.cs`, `GameThreadController.cs:67`, `WorkerThread.cs:119–143`, `StationComponent.cs:2444–2452, 2548–2559, 3256–3279`.

LogisticsProfiler 0.2.2 checks and instruments the original dispatch IL before ILO inserts its guards. Existing visit counters remain meaningful; `priority_lock_calls` counts only calls still executed. No output columns were added. BuildToolOpt's edit-time route/pair rebuilding remains excluded.

Installed into profile `test1` with DSPGAME stopped. Each old DLL is backed up beside it with suffix `.20260909161128.bak`; installed hashes match the built artifacts:

| Artifact | SHA-256 |
|---|---|
| InterstellarLogisticsOpt 1.2.0 | `4538F795CB4A1348438818A3A3FBB8A3923ED1E8A5CC642FE79A09EE03B73F3A` |
| LogisticsProfiler 0.2.2 | `B96408D4B40A713B39B518484B394FFBA4C21C539355A31C029BF82E385866F1` |
| `package/InterstellarLogisticsOpt-1.2.0.zip` | `CFDFD8ED0A4886D9F8ED06712B34D4FC8BA01E3886DF506A61FCFBA00B0A007B` |

The ZIP contains exactly the DLL, manifest, README and icon at its root; its DLL/README match the build/source and its manifest is 1.2.0. Capture settings remain F9, 180 seconds, 1/64, detailed mode.

Release builds passed with zero warnings/errors. The shared executable checks passed on .NET Framework and the game's embedded Mono, using the installed game assembly. Each runtime performed 5,952 byte-for-byte state comparisons across all six priorities, mixed supply/demand rings, stock/order and capacity boundaries, full/expired/higher-priority locks, no idle ships, low energy, successful/failed launches, enabled/disabled optimization, timing-only/detailed profiling, and both plugin load orders. The existing 24 exact branch-counter scenarios also passed. Unsupported dispatch structure is left unchanged. Full three-method profiler binding is checked on .NET Framework; the headless Mono host excludes Unity-native flight-update bindings.

The executable also measures synthetic rings of 1,024 identical failing candidates, 1,000 calls per trial, after 100 warm-up calls. Values below are medians of five trials on the game's Mono with profiler patches removed. The P3 peer starts full; its first refresh remains necessary and subsequent identical refreshes are redundant. These are deliberately isolated paths, not a replay of the captured save.

| Path | Vanilla | Patched, disabled | Enabled | Reduction vs vanilla |
|---|---:|---:|---:|---:|
| P3 own supply short, peer full | 53.212 ms | 53.196 ms | 20.526 ms | 61.4% |
| P5 own supply short | 36.553 ms | 37.404 ms | 20.446 ms | 44.1% |
| P5 own demand empty | 37.291 ms | 36.931 ms | 19.545 ms | 47.6% |

These reductions are **not save-level CPU/UPS gains**. P3's actual redundant-lock fraction was not measured by 0.2.1. The new version also restores work that 1.1.0 incorrectly skipped, so its performance cannot be inferred from the old enabled/factor-5 captures. No frame-spike removal is promised with vanilla cadence.

One subsequent enabled 1.2.0 capture is analyzed above; there is no same-version disabled pair or long-term delivery acceptance. The current release and its validation scope are described at the top of this document. Build/test commands are maintained in [LogisticsProfiler/README.md](../LogisticsProfiler/README.md#构建与离线检查).

## Detailed dispatch captures: LogisticsProfiler 0.2.1

The detailed captures identify inventory and priority checks as the dominant dispatch paths. Planet 2704's P3 hotspot repeatedly finds insufficient own supply; it performs no reverse search. The first small implementation target is P5's counterpart inventory reads after its own supply/demand check fails: their only game-state effect is a call to a priority-lock function that returns immediately for P5. Keep the candidate cursor and method bookkeeping, and verify state equivalence before measuring the change.

### Validity and comparison limits

Source directory: `C:\Users\Yi\AppData\Roaming\r2modmanPlus-local\DysonSphereProgram\profiles\test1\BepInEx\LogisticsProfiler`.

| Group | File prefix, UTC | Wall seconds | Tick begin | Tick end | Dispatch samples |
|---|---|---:|---:|---:|---:|
| Off | `20260909_222742_3184774` | 180.263 | 275936216 | 275937604 | 54,009 |
| Factor 1 | `20260909_223223_9447919` | 180.018 | 275938353 | 275939745 | 53,430 |
| Factor 5 | `20260909_223940_7611437` | 180.089 | 275941675 | 275943095 | 10,983 |

All three use schema 2, profiler 0.2.1, detailed probes enabled, 1/64 sampling, save `无模拟帧稳跑`, game 0.10.34, ILO 1.1.0 and SAHS 0.7.7 with `General.UpdatePeriod=1`. The station cursor is 12,358 and local planet is 0. All stop normally at `duration`, record no pause, and have unchanged start/end ILO/SAHS settings. Plugin versions and patch lists agree. The log confirms three completed sessions without profiler errors.

Game MVID: `ece4a40e-5e73-43f4-a9f8-4e74970b5942`; profiler MVID: `b55b88f0-bbda-42b4-8523-fa16a3f45a29`. Installed profiler SHA-256: `1EAB4D5FAF834999B0A44D09DB6DA11B368BEE2059C4895EA1BEC6EE48389803`.

Checks passed for all 72,528 station/priority rows: no method errors; counts, times, entry states and every detailed counter reconcile with priority summaries. Scheduler samples equal tick deltas and phase totals reconcile. Outer visits never exceed candidate counts. Every outer visit belongs to exactly one pre-flight rejection or trip-check path, and successful dispatch attempts reconcile with the ship-count delta. The new Mono-compatible instrumentation is producing coherent live data.

These are still successive game intervals: tick ranges advance, and the log contains no intervening load. Each group covers only 23.1–23.7 seconds of simulated time, or about 4.6–4.7 of the slowest factor-5 dispatch periods. There is one capture per setting and no uninstrumented control. Detailed probes add observer cost, so compare settings within this round; do not infer the profiler's overhead by subtracting the earlier 0.1.0 timings. No production or delivery-throughput record was collected.

### Scheduler and overall speed

| Metric | Off | Factor 1 | Factor 5 |
|---|---:|---:|---:|
| Observed UPS | 7.700 | 7.733 | 7.885 |
| Complete scheduler mean, ms/tick | 6.275 | 6.182 | 2.435 |
| Complete scheduler maximum, ms/call | 239.440 | 260.663 | 4.960 |
| Mean LateUpdate interval, ms | 259.745 | 261.655 | 256.903 |
| Maximum LateUpdate interval, ms | 540.366 | 612.771 | 428.533 |
| Estimated P3 elapsed time, ms/tick | 3.769 | 3.587 | 1.533 |
| Estimated P5 elapsed time, ms/tick | 1.884 | 2.109 | 0.754 |
| Estimated remote-update elapsed time, ms/tick | 34.890 | 34.787 | 34.668 |

Estimates use `sampled_ms * 64 / scheduler.samples`. Scheduler timing is complete; its dispatch children must not be added to it. Remote-update elapsed time sums overlapping worker calls and waiting, so it is not the main-thread critical path.

Factor 5 reduces the measured scheduler mean by 61.2% and maximum by 97.9%, while observed UPS increases 2.4%. Factor 1 reduces scheduler mean only 1.5%; its UPS difference is 0.4%. These overall differences are observations, not controlled causal estimates. Scheduler time accounts for 4.83%, 4.78% and 1.92% of elapsed wall time respectively, limiting how much further dispatch-only work can improve this workload's UPS.

P3 and P5 together account for 90.7%, 93.7% and 92.0% of sampled dispatch time. P1 and P4 have empty candidate intervals in every sample. Sampled new-launch totals are 452, 462 and 467; these are not delivered cargo, nor proof that factor 5 preserves throughput.

### Planet 2704, priority 3

| Metric | Off | Factor 1 | Factor 5 |
|---|---:|---:|---:|
| Samples | 332 | 344 | 86 |
| Actual outer candidate visits | 322,967 | 317,625 | 83,342 |
| Own supply below threshold | 322,436 | 317,026 | 83,314 |
| Share of visits below threshold | 99.836% | 99.811% | 99.966% |
| Counterpart demand exhausted | 505 | 567 | 20 |
| Trip calculations | 26 | 32 | 8 |
| Reverse candidate visits | 0 | 0 | 0 |
| Priority-lock calls | 297,127 | 296,739 | 66,714 |
| New launches | 12 | 16 | 5 |
| Share of all sampled dispatch time | 14.3% | 14.8% | 23.1% |

Every outer visit here is a supply-direction candidate. `own_supply_short` means physical stock, remote supply including reservations, or total supply including reservations fails the game's loading threshold; it does not identify which quantity failed or prove physical stock is zero. The mean nonempty entry interval is 1,082 candidates in each group. Range, collector and warper restrictions are absent in these samples. Some of the very few flight-eligible candidates lack trip energy, but those checks are not the repeated bulk path.

This separates the hotspot from the speculative distance-cache and reverse-index targets. It also rules out simply returning when the tower lacks supply: vanilla continues to inspect counterpart demand and may refresh that counterpart's priority lock. The lock-call counts are attempts, not distinct writes or distinct counterpart slots.

Planet 6301/P3 is also mostly supply-limited. Planet 1803/P3 is different: about 81–83% of outer visits find exhausted counterpart demand. A single global “no stock” heuristic would not address both, and neither finding justifies discarding priority maintenance.

### Priority 5: a narrower optimization target

| Metric | Off | Factor 1 | Factor 5 |
|---|---:|---:|---:|
| Actual outer visits | 2,945,275 | 1,933,493 | 368,365 |
| Own/peer priority-lock rejections | 2,722,789 | 949,194 | 181,604 |
| Lock-rejection share | 92.45% | 49.09% | 49.30% |
| Own demand exhausted | 113,338 | 933,285 | 178,555 |
| Own supply below threshold | 46,222 | 31,483 | 3,422 |
| Combined own supply/demand failure share | 5.42% | 49.90% | 49.40% |
| Priority-lock calls, all no-ops at P5 | 161,222 | 490,852 | 80,106 |

When its own supply is inadequate, vanilla reads the other station's demand only to decide whether to call `SetPriorityLock`. When its own demand is exhausted, it similarly reads the other's supply. `SetPriorityLock` returns immediately for priorities 0 and 5. A guard inside these two failure branches can therefore omit their counterpart lookup, storage lock and reads on P5 while retaining the original outer scan, cursor advancement and bookkeeping. P0 shares that source-level property but has no samples here. This is about eliminating specific redundant work, not skipping all P5 scheduling.

The proposed branch applies to about half of P5's visited candidates with ILO enabled. That is a candidate-count fraction, **not** a predicted 50% time saving. P5 still spends time checking valid priority locks and traversing the ring. Its changed branch distribution at factor 1 is consistent with the earlier finding that the ILO prefix drops higher-priority lock maintenance, but successive game states prevent attributing the whole shift to that bug. P5's estimated time actually increases at factor 1 despite fewer visits, so visit counts alone cannot predict CPU savings.

Source anchors: `GameCode-latest/StationComponent.cs:3039–3053, 3211–3234` for the two own-state failure branches and `3256–3280` for the lock function. The [correctness findings](RESEARCH.md#correctness-findings) remain unresolved by profiling.

### Remaining priorities

1. Implement and compare the P5 counterpart-read guard first, preserving cursor, locks, reservations and ship behavior in executable witnesses. It needs no persistent cache or route-rebuild changes. A dispatch transpiler must also be tested together with the profiler's instrumentation order and signature gate; the current profiler deliberately rejects an unknown rewritten body.
2. For P3, investigate reducing repeated storage reads and redundant lock refreshes while preserving required counterpart effects. The current counters do not record source slot reuse or whether a lock already has its requested value. A call-scoped snapshot or batch would need a demonstrated invariant and equivalence checks; do not assume a permanent cache is safe.
3. Defer distance caching for dispatch: only 0.386%, 0.070% and 0.293% of outer visits reach trip calculation. Reverse searches visit 128,310, 132,370 and 75,742 candidates, with only 1, 2 and 0 partner matches and no reverse-launch attempts. A negative partner lookup could help that path, but it does not address planet 2704/P3, and no branch timing establishes its share of CPU cost.
4. For a larger UPS gain, investigate remote updates and their critical-path contribution separately. Their aggregate workload remains much larger than dispatch; these captures do not separate flight, return cargo, render data or waits.

BuildToolOpt's edit-time route rebuilding remains excluded. The next dispatch candidate does not overlap it. This analysis changes reports only; no production optimization has been installed.

### Detailed-capture SHA-256

| Group | Summary TSV | Stations TSV |
|---|---|---|
| Off | `383df40f5b6dfbff44a852d9d3f4f6008588f9a79bb8af6ff5c6efd2ee9d5132` | `959f54a5af2f89357d70a0867268bc4d1f8f470849ff46f423ca4c8dd0934e5e` |
| Factor 1 | `5a330358ca8e9e5d08c544d3486cd5a6dfcd335817c6f4b46deb03762ed77853` | `65c4be15b28db7bd9abb79038e5f3e73929ee45429cd8ceacca423f631074f01` |
| Factor 5 | `ea522f6dbdd588029ac06351f014ac58aa3cdb2e528c3903a35c40d4adad7b9b` | `e307f39afea5e9233e36d3c43679fa7af247e3a0e5548ee088ab5ae1bb08a0e3` |

## Earlier 0.1.0 captures

Three sequential captures on 2026-09-09 show a large reduction in scheduler spikes at factor 5, but little change in overall simulation speed. Factor 1 has a small observed total benefit. The remaining sampled dispatch cost is concentrated in star-route and fallback passes; flight updates are also a larger aggregate workload than dispatch.

### Capture validity

Source directory: `C:\Users\Yi\AppData\Roaming\r2modmanPlus-local\DysonSphereProgram\profiles\test1\BepInEx\LogisticsProfiler`.

| Group | File prefix, UTC | ILO Enabled | Factor | Wall seconds | Game ticks |
|---|---|---|---:|---:|---:|
| Off | `20260909_213931_5483492` | false | 5, inactive | 60.156 | 470 |
| Factor 1 | `20260909_214046_5387186` | true | 1 | 60.001 | 472 |
| Factor 5 | `20260909_214201_5651892` | true | 5 | 60.088 | 484 |

All three groups used save `无模拟帧稳跑`, SAHS 0.7.7 with `General.UpdatePeriod=1`, game 0.10.34, and ILO 1.1.0. The station-pool cursor was 12,358; this is a high-water mark, not a live-station count. Each capture ended with `stop=duration`, no recorded pause, unchanged start/end ILO/SAHS settings, and no reported method or diagnostic errors. Plugin versions and the three target methods' patch lists agree across captures. The log confirms all three sessions completed.

Game MVID: `ece4a40e-5e73-43f4-a9f8-4e74970b5942`. Profiler MVID: `f8cdedc6-601a-4463-8a88-172bbf4f2b45`. The installed profiler DLL matches the locally built DLL, SHA-256 `750A9C2855D27B6E19CBEFCD3F806C92B9ECA895A9E4785AA6E97C3D8C7009DC`.

Checks passed: scheduler sample count equals game-tick delta; phase counts/times sum to scheduler totals; per-station counts/times and state counters sum to each priority's summary. All labels say `baseline`; the groups above are identified from their actual settings.

These were successive intervals, not independent reloads of the same initial state. Each 60 wall-clock seconds contains only about eight seconds of game simulation. Factor 5's 300-tick passes have fewer than two periods per capture. No uninstrumented control, full production/delivery record, or stable round-trip comparison was collected. The measurements locate work; they do not establish long-term throughput or the profiler's observer cost.

### Complete scheduler and frame measurements

| Metric | Off | Factor 1 | Factor 5 |
|---|---:|---:|---:|
| Observed UPS | 7.813 | 7.867 | 8.055 |
| Scheduler mean, ms per game tick | 5.697 | 5.553 | 1.744 |
| Scheduler maximum, ms per call | 250.943 | 238.765 | 5.477 |
| Mean LateUpdate interval, ms | 255.981 | 264.321 | 253.536 |
| Maximum LateUpdate interval, ms | 529.549 | 629.889 | 509.401 |

Factor 5 reduced scheduler mean by 69.4% and the observed maximum by 97.8% relative to Off. The observed UPS difference was only +3.1%; mean frame intervals were almost unchanged. Factor 1 reduced scheduler mean by 2.5%, with an observed UPS difference of +0.7%. One interval per setting cannot separate these small overall differences from normal variation.

Off and Factor 1 still concentrate nearly all scheduler time at phases 0 and 30 of the native 60-tick cycle. Their phase-0 means are 232.080 and 235.911 ms; phase-30 means are 126.597 and 115.916 ms. Factor 5 spreads work across ticks and removes those large scheduler spikes in this capture, while the overall frame stalls remain.

In the Off capture the scheduler's measured duration totals 4.45% of elapsed wall time; at Factor 5 it is 1.41%. Further work confined to this scheduler therefore has limited scope to transform overall UPS under the observed workload. This is an attribution within the instrumented run, not a prediction for another save or a different SAHS setting.

### Sampled dispatch attribution

Dispatch and remote updates use random 1/256 sampling. The following values estimate aggregate elapsed method time per game tick as `sampled_ms * 256 / scheduler.samples`.

| Method / priority | Off, ms/tick | Factor 1, ms/tick | Factor 5, ms/tick |
|---|---:|---:|---:|
| P1: station route | 0.047 | 0.052 | 0.017 |
| P2: planet route | 0.415 | 0.267 | 0.146 |
| P3: star route | 3.629 | 3.365 | 1.315 |
| P4: logistics group | 0.007 | 0.007 | 0.004 |
| P5: ordinary fallback | 1.514 | 1.658 | 0.459 |
| All sampled dispatch priorities | 5.611 | 5.349 | 1.941 |
| `InternalTickRemote` | 35.279 | 34.586 | 34.163 |

P0 has no samples in these captures and is omitted; this is not proof that no Ignore-mode station exists. The earlier per-station Ignore-mode launch ceiling is not a measured throughput bound for this save.

Do not add the dispatch estimate to scheduler time: dispatch is nested within the scheduler. Do not add worker-thread remote-update time to a frame's wall time: calls overlap across workers and include waiting. The Factor 5 dispatch estimate exceeds its fully measured scheduler mean by about 11%, illustrating the sampling error; use the complete scheduler measurement for its total cost.

P3 and P5 account for about 91–94% of sampled dispatch time across the groups. P1 and P4 have empty candidate intervals in every captured sample and together account for only about 1% of sampled dispatch time. Adding another empty-range fast path is consequently a low-priority target here.

Calls with no net new ship account for 98.7%, 99.3%, and 94.9% of sampled dispatch time respectively. This includes useful lock/cursor maintenance and calls blocked by valid rules, so it is not all removable work. No-idle dispatch time falls from an estimated 1.077 ms/tick Off to 0.010 ms/tick at Factor 1, yet the complete scheduler saves only 0.144 ms/tick. State evolution, changed priority behavior, and sampling variation are not separated by these captures.

### Concrete remaining hotspot

Planet ID **2704**, priority **3**, appears in every group:

| Group | Samples | Mean candidate interval length | Share of all sampled dispatch time | Sampled new launches |
|---|---:|---:|---:|---:|
| Off | 41 | 1,055.6 | 22.9% | 0 |
| Factor 1 | 30 | 1,045.9 | 18.5% | 0 |
| Factor 5 | 6 | 1,082.0 | 17.1% | 0 |

All these samples enter with an idle ship and more than 6 MJ, so ILO's existing early return cannot remove this hotspot. More than 6 MJ does not prove the tower can afford the trip. The remaining possibilities include inventory/reservations, priority locks, range/warper restrictions, trip energy, and reverse-pair search; the profiler does not measure those branch reasons or actual pair visits.

Treat the planet aggregate as a useful lead, not a stable ranking of individual towers. Most tower/priority rows have just one sample: 3,779/4,007 Off, 3,925/4,170 at Factor 1, and 958/965 at Factor 5. Planet 6301 also merits inspection, but its Factor 5 hotspot uses only seven samples.

Sampled new-launch totals are 28, 32, and 40. These small random counts and consecutive states cannot establish either increased or preserved cargo throughput. No delivery quantities were recorded.

### Targets identified before detailed capture

1. Preserve priority-lock and cursor semantics before broadening the existing early return. The source-level correctness findings remain applicable; the absence of game exceptions does not validate routing behavior.
2. For dispatch, investigate P3 on planet 2704 and P5's large candidate rings. A focused follow-up instrument should distinguish actual pair visits and rejection reasons on those passes. Do not assume the reverse lookup or distance calculation is the dominant branch from method-level timing alone.
3. For wider logistics gains, split `InternalTickRemote` into flight/return work and `ShipRenderersOnTick` only if further profiling is pursued. Its aggregate elapsed workload is consistently larger, while its parallel contribution to the critical path remains unknown.

BuildToolOpt's edit-time route rebuilding remains outside this scope. No production optimization or new profiling hooks were added while analyzing these captures.

### Summary-file SHA-256

| Group | Hash |
|---|---|
| Off | `c836b2dccf69dec258b720cbf36b474f83a4df9e8eb67fbcf79fbe3bdf2edad2` |
| Factor 1 | `edf391ce213cd56504461571b4bcaf90724cd78b4f8c2d587e950abc33775f0f` |
| Factor 5 | `d8eb3c2fa56be3da75548588ae1f8e16a30d60359c32ea9f771d752cdb359d96` |
