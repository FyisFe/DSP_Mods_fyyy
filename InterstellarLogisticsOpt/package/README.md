# InterstellarLogisticsOpt

Flattens the CPU spikes caused by **interstellar (stellar) logistics** when idling
with many logistics towers.

## What it does

Two optimizations, both active whenever the mod is enabled:

### 1. Phase dispersion (spike flattening)

Vanilla schedules every stellar logistics tower *in phase*: all towers of a given
priority run on the same tick (`t%10`, `t%30`, `t%60`), producing a large spike
every 60 ticks and near-idle ticks otherwise. This mod spreads the same work evenly
across ticks by offsetting each tower by its array slot, **without changing how
often any tower is scheduled** (still every 10 / 30 / 60 ticks). Peak per-tick
scheduler load drops by roughly 96% with no change to logistics throughput.

Note: this **redistributes** load to remove stutter — it does not reduce total CPU.
To also reduce total CPU here, see `AmortizeFactor` below.

### 2. Dispatch early-exit (total CPU reduction)

Vanilla checks "no idle ship / not enough energy" *inside* its per-pair dispatch
loop, so a tower that cannot send anything still scans its entire supply/demand
pair ring every scheduling tick before finding nothing to do. This mod hoists that
check to the method entry: such towers are skipped in O(1). In an idle steady-state
base many towers qualify, so this **reduces total CPU**, not just spikes.

Only interstellar logistics is affected. Local (planetary) logistics is untouched.

## Configuration

Two settings, adjustable live from the in-game **UXAssist** config panel
(its tab is labelled *星际物流调度优化 / InterstellarLogisticsOpt*) or in
`BepInEx/config/org.fyyy.interstellarlogisticsopt.cfg`, section `[General]`:

- `Enabled` (default `true`) — master switch for both optimizations above. Set to `false` to run the vanilla scheduler.
- `AmortizeFactor` (default `1`, range `1`–`30`) — multiply each priority's scheduling interval by this factor ("simulated frames") to cut total CPU. `1` = off (vanilla 10/30/60-tick cadence). E.g. `5` schedules each tower every 50/150/300 ticks instead, reducing per-tick work to a fifth while keeping it evenly spread across ticks (no spikes). This trades logistics responsiveness for CPU — higher factors mean slower reaction to supply/demand changes. Requires `Enabled = true`.

Both controls take effect immediately, no save reload needed.

Requires [UXAssist](https://thunderstore.io/c/dyson-sphere-program/p/soarqin/UXAssist/).

## Note on determinism

Because towers competing for the same delivery are now resolved across different
ticks instead of all on one tick, the exact "which supplier wins this order"
sequence can differ from vanilla. The dispatch early-exit likewise skips a tower's
priority-lock update and pair-cursor advance on ticks where it has nothing to send.
Steady-state throughput is unaffected (cargo still flows, arguably more fairly), but
per-frame behavior is **not** bit-identical to vanilla. If you play multiplayer or
rely on replay determinism, evaluate before using.
