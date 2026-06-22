# Dashboard — Missing-Feature Research & Improvement Backlog

**Date:** 2026-06-22
**Mod:** DashboardOverhaul (DSP 仪表盘)
**Status:** Research only — no code written. Candidate backlog for v2+.
**Game source read:** `GameCode-latest/` (decompiled). All `file:line` refs are into that tree.

## Scope

The `dashboard-overhaul` branch is merged. This doc catalogs what the in-game Dashboard
*could* gain next, grounded in the actual game code, and filtered against what we already
ship. It exists to pick the next slice of work from — not a spec.

### Already shipped (do not re-propose)
- Page **tab bar**: switch / add / delete / rename pages (1..9, `pages[0]` unused).
- **Move a chart** to another page (right-click chart → "移动到页面" submenu).
- Pages + names persist in the save (null-slot delete; page-1 resurrect bug fixed via
  `DashboardLayout.Import` prefix).
- Self-contained `Loc.L(zh,en)`, no external mod deps.

## Two corrections to the original framing

- **"Delete a chart" already exists in vanilla.** Hover a chart → gear icon →
  **关闭图表 / Close Chart** (`UIChart.cs:260` → `CloseAndRemoveChart`, `UIChart.cs:145`).
  Not a gap. A *faster/more discoverable* delete (hover ✕, or "delete all charts of this
  statistic") would still be an improvement.
- **"Rename a chart" has no per-chart title in the data model.** `ChartData` has **no
  `name` field**; the title is derived live from the bound `StatPlan`
  (`UIChart.cs:835`, `statPlan.displayName/abbr`). Today rename is sidebar-only
  (`UIStatPlanEntry` name input → `StatPlan.Rename`, `UIStatPlanEntry.cs:347`) and it
  renames **every** chart bound to that statistic. So:
  - *Rename-from-popup* (easy) = edit the StatPlan name from the chart's gear menu — but
    it's shared across all charts of that statistic; warn the user.
  - *True per-widget custom title* (harder) = needs our own persistence store keyed by
    chart identity; vanilla saves cannot hold it.

## Key architecture facts that shape feasibility

- **Popup menu is the proven, low-risk extension point.** `UIChart.SetPopupMenuButtons`
  and its three `public virtual` group methods — `SetStyleGroupPopupMenuButtons` /
  `SetObserveGroupPopupMenuButtons` / `SetOtherGroupPopupMenuButtons` (`UIChart.cs:227-263`)
  — plus `UIStatPlanEntry.SetPopupMenuButtons` (`UIStatPlanEntry.cs:351`) are all Harmony
  postfix targets. `UIPopupMenu.AddMenuButton` + `onMenuButtonClick` are public. This is
  the same hook our existing "Move to page" uses (`DashboardOverhaul/UIChartPatch.cs`).
- **Time window is on the `StatPlan`, not the chart.** 6 levels
  (`0=1min,1=10min,2=1hr,3=10hr,4=100hr,5=total`, `UIChartAstroItemProduction.cs:518`).
  Two charts with different `timeLevel` are two different StatPlans. Changing a chart's time
  window = rebind it to a sibling StatPlan via `CustomCharts.GetOrFindStatPlan`
  (`CustomCharts.cs:98,217`). Same story for `astroFilter` (`UIChartSingleProducer.cs:327`).
- **Chart create primitives:** `DashboardPage.AddChartWithAutoPosition` (capped, BFS
  auto-place, `DashboardPage.cs:60`), `AddChart` (manual pos, **no cap**, `:35`),
  `ChartData.InheritedParameter` copies preset/style/displayTypeParams (`ChartData.cs:82`).
  `OnSizeMenuButtonClick` (`UIChart.cs:736`) is the in-game recipe for clone-then-swap.
- **Sidebar (statboard)** is one flat virtualized list of `UIStatPlanEntry` rows
  (`UIStatboard.cs:119-175`), opened only by a mouse arrow-toggle (`UIDashboard.cs:326`),
  no keybind, no search/filter/grouping (only `StatPlanPool.PinToTop`, `:193`).
- **Styling is data-driven but under-exposed.** Generic/Background/Border style slots
  (`EChartStyleType.cs`) can set color/font/sprite/material/padding via
  `ChartStyleDeclaration` (`ChartStyleDeclaration.cs:40`) — but the UI lists only the few
  prebuilt `BuiltinConfig.chartStyleConfigs` (`BuiltinConfig.cs:184`). Inject extra
  `ChartStyleConfig`s before `ChartPresetsDB.Load()` and they show up automatically.
- **`chartExistMaxCount` default = 4** copies of one statistic per page, enforced only in
  `AddChartWithAutoPosition` (`DashboardPage.cs:68`, `ChartPresets.cs:16`); `AddChart`
  bypasses it. Raise via Harmony on `ChartPresetsDB.GetChartExistMaxCount`.
- **Layout (de)serialization already exists** — `DashboardLayout`/`DashboardPage`/
  `ChartData` have binary `Export`/`Import` (`DashboardLayout.cs:49`, `DashboardPage.cs:221`,
  `ChartData.cs:37`). Reusable for clipboard/file share. `Import` is forward-compatible with
  >10 pages (`DashboardLayout.cs:78`).
- **Grid is window-bounded, no pan/zoom.** `maxGridCount = rect/116` (`UIDashboard.cs:185`);
  charts only clamped against being fully off-screen (`UIChart.cs:932`). No scroll canvas.
- **New chart *types* are impractical for a mod.** `ChartPresetsDB.Load()` reflects over
  Assembly-CSharp only (`ChartPresetsDB.cs:26`), needs an `EStatPlanType` enum entry, an
  in-assembly `StatPlan` subclass, and an authored `Resources/Dashboard/Presets/
  chart-presets-<type>` prefab. Line/bar/stacked is baked into per-type prefabs + shaders,
  not a config flag.

## Backlog (grouped by value/effort)

### Tier 1 — Quick wins (low effort; all via popup postfix)
| # | Feature | Current state | Hook | Difficulty |
|---|---------|---------------|------|------------|
| 1 | Rename from chart popup (edits StatPlan name; shared-title warning) | sidebar-only | `UIChart.SetPopupMenuButtons` + `StatPlan.Rename` | Low |
| 2 | Duplicate chart | none | `AddChartWithAutoPosition` + `InheritedParameter` | Low–Med |
| 3 | Raise `chartExistMaxCount` (default 4) | hard cap | postfix `ChartPresetsDB.GetChartExistMaxCount` | Low |
| 4 | Sidebar hotkey / pin-open | mouse-only toggle | `UIDashboard.OnSidebarBtnClick` | Low |
| 5 | Persist chart lock state | runtime-only, resets | external store keyed by chart | Low–Med |

### Tier 2 — High-value (medium effort)
| # | Feature | Why / hook |
|---|---------|-----------|
| 6 | Per-chart time-window selector (6 levels) | rebind to sibling StatPlan via `GetOrFindStatPlan`; today only in `UIStatisticsWindow` |
| 7 | Per-chart astro filter (all/planet/system) | same rebind mechanism; today read-only on dashboard |
| 8 | Sidebar search / type filter | flat virtualized list, no search; inject control into `UIStatboard` |
| 9 | Layout import/export (share dashboards) | reuse binary `Export`/`Import` → clipboard string |
| 10 | Page reordering | tabs exist but slot index = identity; drag / move-left-right |
| 11 | Chart-config favorites / templates | greenfield; serialize a configured `ChartData` bundle |
| 12 | Duplicate / clear page | page primitives exist; no such command today |
| 13 | Custom colors / styles | inject extra `ChartStyleConfig`; auto-appears in "切换图表样式" |

### Tier 3 — Ambitious (high effort / risk)
| # | Feature | Note |
|---|---------|------|
| 14 | Raise the 10-page cap | `Import` forward-compatible; many hard-coded `10` literals |
| 15 | Pan / zoom / scrollable canvas | grid window-bounded; large UI undertaking |
| 16 | Global settings (default page, global time range, refresh throttle) | none exist |
| 17 | Change a chart's data source in place | risky; preset/type must match; needs recreate path |
| 18 | New chart types / line↔bar↔stacked | not practical (enum + Assembly-CSharp + prefab + shaders) |

## Recommended next slice (v2)

A "chart context-menu" bundle mirroring the page work: **#1 Rename + #2 Duplicate +
#6 Per-chart time window**, with **#3 cap raise** as a freebie. All reuse the proven popup
hook, no save-format change, and cover the operations players reach for most. Then **#9
share/export** and **#8 sidebar search** as standalone follow-ups.

## Open questions for the v2 spec
- Rename: ship the easy shared-StatPlan rename, or invest in true per-widget titles
  (needs an external persistence store)?
- Duplicate: same page only, or offer "duplicate to page →" like the existing move?
- Time-window rebind: what to do if the target sibling StatPlan would orphan the old one
  (ref-count / cleanup semantics)?
