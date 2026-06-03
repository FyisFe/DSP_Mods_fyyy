# InterstellarLogisticsOpt

Reduces the CPU cost of **interstellar (stellar) logistics** when idling with many
logistics towers, via a dispatch early-exit (always on) and an optional scheduling
amortization knob.

## What it does

### 1. Scheduling amortization (`AmortizeFactor`, total CPU reduction)

Vanilla clusters all interstellar dispatch onto 1-in-10 ticks (`t%10`), with the
heaviest pass at `t%60` (once per second), leaving the other 9 of every 10 ticks
free. With `AmortizeFactor >= 2` this mod instead schedules each tower every
`period * factor` ticks (e.g. factor 5 → every 50/150/300 instead of 10/30/60) and
spreads the work evenly across ticks. That **reduces total scheduler CPU** to
`1/factor` while staying flat (no spikes), trading logistics responsiveness for CPU.

`AmortizeFactor = 1` (the default) runs the **vanilla scheduler unchanged**. Pure
phase-dispersion at factor 1 was removed: it does the same total work as vanilla but
taxes every tick instead of clustering, which on a tight per-tick frame budget causes
*more* frequent micro-stutter than vanilla's single absorbable per-second spike. The
real win is `AmortizeFactor >= 2`.

### 2. Dispatch early-exit (total CPU reduction)

Vanilla checks "no idle ship / not enough energy" *inside* its per-pair dispatch
loop, so a tower that cannot send anything still scans its entire supply/demand
pair ring every scheduling tick before finding nothing to do. This mod hoists that
check to the method entry: such towers are skipped in O(1). In an idle steady-state
base many towers qualify, so this **reduces total CPU**, not just spikes.

Only interstellar logistics is affected. Local (planetary) logistics is untouched.

## Configuration

Two settings, adjustable live from the in-game **UXAssist** config panel
(its tab is labelled *星际物流优化 / InterstellarLogisticsOpt*) or in
`BepInEx/config/org.fyyy.interstellarlogisticsopt.cfg`, section `[General]`:

- `Enabled` (default `true`) — master switch. Set to `false` to run fully vanilla.
- `AmortizeFactor` (default `1`, range `1`–`30`) — scheduling amortization. `1` = off: runs the **vanilla scheduler unchanged** (recommended baseline). `2`+ schedules each tower every `period * factor` ticks instead of 10/30/60 (e.g. `5` → every 50/150/300), cutting total scheduler CPU to `1/factor` while keeping load evenly spread (no spikes). Trades logistics responsiveness for CPU — higher factors mean slower reaction to supply/demand changes. Requires `Enabled = true`.

Both controls take effect immediately, no save reload needed.

Requires [UXAssist](https://thunderstore.io/c/dyson-sphere-program/p/soarqin/UXAssist/).

## Note on determinism

With `AmortizeFactor >= 2`, towers competing for the same delivery are resolved
across different ticks instead of all on one tick, so the exact "which supplier wins
this order" sequence can differ from vanilla. The dispatch early-exit likewise skips a
tower's priority-lock update and pair-cursor advance on ticks where it has nothing to
send. (At `AmortizeFactor = 1` the scheduler is vanilla, so only the early-exit caveat
applies.)
Steady-state throughput is unaffected (cargo still flows, arguably more fairly), but
per-frame behavior is **not** bit-identical to vanilla. If you play multiplayer or
rely on replay determinism, evaluate before using.
