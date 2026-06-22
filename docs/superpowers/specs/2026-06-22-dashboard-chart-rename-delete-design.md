# DashboardOverhaul — Chart Rename & Delete (design)

**Date:** 2026-06-22
**Mod:** DashboardOverhaul (DSP 仪表盘)
**Branch:** `dashboard-chart-rename-delete`
**Status:** Approved design. Next: implementation plan (writing-plans).
**Backlog source:** `docs/superpowers/research/2026-06-22-dashboard-improvements-research.md` (items #1 rename, and the delete clarification).

## Goal

Bring the sidebar's two per-statistic operations — **rename** and **delete** — onto the
dashboard chart itself, so a player can do both without opening the sidebar. Both operate on
the bound **statistic** (`StatPlan`), matching the sidebar's existing behavior.

## Decisions (locked)

- **Delete scope = the whole statistic.** Removes this widget *and every other copy of the
  same statistic on all pages* plus its sidebar entry — i.e. the sidebar trash button's
  behavior (`CustomCharts.RemoveStatPlan`), reachable from the chart. This is distinct from
  vanilla **关闭图表 / Close chart**, which removes only the one widget. Both remain available.
- **Rename scope = the statistic's name.** Edits `StatPlan.name` (same as the sidebar). If the
  statistic is charted on multiple pages, all those charts show the new name. No per-widget
  title (charts have no `name` field; per-widget would need a new persistence store — out of
  scope).
- **Rename triggers (two):** the chart popup "Rename" button **and** double-clicking the chart
  title.
- **Approach A:** extend the existing `UIChart.SetPopupMenuButtons` postfix (the same hook the
  shipped "Move to page" uses). No custom right-click menu, no in-place title editing of the
  vanilla title element.
- **Label:** delete is **删除统计项 / Delete statistic** (not plain "Delete") to avoid confusion
  with vanilla "Close chart", and to match the game's own wording (统计项).

## Relevant game mechanics (verified in `GameCode-latest/`)

- Popup hook: `UIChart.SetPopupMenuButtons` + 3 `public virtual` group methods
  (`UIChart.cs:227-263`). `UIPopupMenu.AddMenuButton` / `onMenuButtonClick` are public.
  The shipped `UIChartPatch.cs` already postfixes this method for "Move to page".
- Rename: `StatPlan.Rename(ref string)` (`StatPlan.cs:137`) sets `name` and fires
  `onNameChanged` only when the value changes; the live `UIChart` subscribes to it
  (`UIChart.cs:94`), so the title refreshes automatically. Empty name → `displayName` falls
  back to the default (`StatPlan.GetDefaultName`).
- Delete: `CustomCharts.RemoveStatPlan(id)` (`CustomCharts.cs:334`) removes the StatPlan from
  the pool and every `ChartData` referencing it across all pages and the watch layout (each via
  `RemoveChartAt` → `ChartData.Free`). Then `UIDashboard.DetermineCharts()` (`UIDashboard.cs:221`)
  rebuilds the current page: `ResetChartPool()` frees the now-orphaned widget, and the removed
  `ChartData` is gone so it's not re-instantiated — no dangling reference.
- Title element: `UIChart.titleText` (base-class `Text`, set in
  `UIChart.TruncateStatPlanNameText`, `UIChart.cs:835`). Present on most presets; may be absent
  on a compact preset.
- Confirm dialog (sidebar's): `UIMessageBox.Show("确认删除统计项标题".Translate(),
  "确认删除统计项提示".Translate(), "取消".Translate(), "确定".Translate(), 1, null, response)`
  (`UIStatPlanEntry.cs:285`). Reusing these existing keys gives free localization.
- Charts are pooled and re-`_Init`'d (`UIDashboard.TakeChartFromPool`, `UIDashboard.cs:246`),
  so any per-instance handler must read the chart's *live* `chartData` on each use.

## Components

### 1. `UIChartPatch.cs` (extend existing postfix)
After the current "Move to page" submenu, append two buttons:
- **重命名 / Rename** → `dashboard.CloseChartPopupMenu()`; `ChartRename.Begin(chart)`.
- **删除统计项 / Delete statistic** (separator above it) → confirm dialog → on OK, run the
  delete sequence.

Guard: only add the buttons when `chart.chartData` and `chart.charts` resolve (mirrors the
existing null guards in the file).

### 2. `ChartRename.cs` (new) — floating rename input
A small manager (one shared single-line `InputField`, lifecycle owned by the dashboard) that
mirrors the proven `PageTabBar` rename pattern (`EnsureRenameInput` / `BeginRename` /
`CommitRename`):
- `Begin(UIChart chart)`: resolve `statPlan = chart.charts.statPlans[chart.chartData.statPlanId]`;
  position the input over the chart's title (fallback: top-left of the chart rect); pre-fill with
  the current `name`; focus.
- On `onEndEdit`: `statPlan.Rename(ref newName)`; refresh the sidebar list if open
  (`statboard.DetermineEntryVisible()`); hide the input. (Title refresh is automatic via
  `onNameChanged`; we may also call `chart.TruncateStatPlanNameText()` defensively.)
- `Free()`/cancel: hide and clear the target; called when the dashboard tears down, or when the
  target chart is deleted.

### 3. `ChartTitleRenameTrigger.cs` (new) — title double-click
A tiny `MonoBehaviour` implementing **only** `IPointerClickHandler`, attached to each chart's
`titleText` GameObject via a `UIChart` lifecycle postfix (chosen so it fires for every chart
type; attached once per pooled instance, guarded against double-attach). It sets the title's
`raycastTarget = true` and, on `clickCount >= 2`, calls `ChartRename.Begin(ownerChart)` using the
chart's live `chartData`. Implementing only the click handler (not pointer-down) lets pointer-down
bubble to the chart's drag logic, so drag-move is preserved.

### 4. Delete sequence (in `UIChartPatch` or a thin helper)
```
int id = chart.chartData.statPlanId;
dashboard.CloseChartPopupMenu();
ChartRename.CancelIfTargeting(chart);   // drop an in-progress rename of this chart
charts.RemoveStatPlan(id);
dashboard.DetermineCharts();            // rebuild current page, frees orphan widget
if (sidebar open) dashboard.statboard.DetermineEntryVisible();
```
Wrapped by the confirm dialog (always shown — this chart guarantees the statistic exists).

## Data flow

Menu "Rename" / title double-click → `ChartRename.Begin` → input → `StatPlan.Rename` →
`onNameChanged` → title repaints (+ sidebar refresh).

Menu "Delete statistic" → confirm → `CustomCharts.RemoveStatPlan` → `DetermineCharts` (+ sidebar
refresh).

## i18n

- New menu labels via `Loc.L(zh,en)`: `重命名`/`Rename`, `删除统计项`/`Delete statistic`.
- Delete confirm reuses game keys via `.Translate()`: `确认删除统计项标题`, `确认删除统计项提示`,
  `取消`, `确定`. (Reading existing keys is safe; the known caveat is only about *registering*
  base-game keys via UXAssist I18N.)

## Persistence

None changed. `StatPlan.name` is already serialized; delete mutates already-serialized structures.
Existing saves load unchanged.

## Edge cases & risks

- **Drag vs. double-click on the title (primary risk):** the title handler implements only
  `IPointerClickHandler`, so pointer-down should still reach the chart's drag logic. Must be
  verified in-game (drag-from-title still works; double-click renames). Fallback if event routing
  misbehaves: the chart body remains draggable regardless, and the menu "Rename" is always
  available.
- `titleText == null` on a compact preset → skip the double-click attach for that chart; menu
  "Rename" still works.
- Rename/delete are allowed on **locked** charts (vanilla lock only blocks move/resize).
- Deleting a chart that has an open popup or active rename input → close/cancel those first.
- Empty name on rename → reverts to default name (matches sidebar).
- Page becomes empty after delete → vanilla `emptyTip` shows via `DetermineCharts`; tab bar
  unaffected.

## Out of scope

- Per-widget custom titles (would need a new persistence store).
- Renaming/deleting via the chart's signal-tag picker (sidebar-only complex path); plain text
  rename only.
- Any change to vanilla "关闭图表 / Close chart".

## Testing / verification

Automated UI tests aren't feasible for Harmony patches against the live game; the mod's
established workflow is build (Release) + manual in-game test. Logic here is thin. Manual
checklist:
1. Rename via popup button updates the chart title.
2. Rename via title double-click updates the title.
3. Dragging the chart by its title still works (drag-routing risk).
4. Rename reflects on all copies of the same statistic across pages, and in the sidebar.
5. Empty name reverts to the default name.
6. Delete a statistic with copies on multiple pages removes them all + the sidebar entry.
7. Delete the last chart on a page shows the empty tip.
8. Confirm dialog Cancel aborts; OK deletes.
9. No errors when `titleText` is absent on a preset.
