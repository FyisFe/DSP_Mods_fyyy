# InterstellarLogisticsOpt

Flattens the CPU spikes caused by **interstellar (stellar) logistics** when idling
with many logistics towers.

## What it does

Vanilla schedules every stellar logistics tower *in phase*: all towers of a given
priority run on the same tick (`t%10`, `t%30`, `t%60`), producing a large spike
every 60 ticks and near-idle ticks otherwise. This mod spreads the same work evenly
across ticks by offsetting each tower by its array slot, **without changing how
often any tower is scheduled** (still every 10 / 30 / 60 ticks). Peak per-tick
scheduler load drops by roughly 96% with no change to logistics throughput.

Only interstellar logistics is affected. Local (planetary) logistics is untouched.

## Configuration

`BepInEx/config/org.fyyy.interstellarlogisticsopt.cfg`:

- `[General] Enabled` (default `true`) — set to `false` to run the vanilla scheduler.

## Note on determinism

Because towers competing for the same delivery are now resolved across different
ticks instead of all on one tick, the exact "which supplier wins this order"
sequence can differ from vanilla. Steady-state throughput is unaffected (cargo
still flows, arguably more fairly), but per-frame behavior is **not** bit-identical
to vanilla. If you play multiplayer or rely on replay determinism, evaluate before
using.
