# DashboardOverhaul — Page Drag-Reorder (design)

**Date:** 2026-06-22
**Mod:** DashboardOverhaul (DSP 仪表盘)
**Branch:** `page-drag-reorder` (suggested)
**Status:** Approved design. Next: implementation plan (writing-plans).
**Backlog source:** `docs/superpowers/research/2026-06-22-dashboard-improvements-research.md` (item #10, "Page reordering").

## Goal

Let the player reorder dashboard page tabs by **dragging** a tab left/right, with the other
tabs sliding aside in real time to preview the new order. Dropping commits the new order, which
persists across save/reload. No new save data, no new menu entries.

## Decisions (locked)

- **Interaction = drag only.** Live reflow: the dragged tab follows the cursor and the other
  tabs slide aside as you cross their midpoints; drop commits the previewed order. No
  "Move left / Move right" context-menu entries (the right-click menu stays Rename / Delete).
- **Reflow is instant snap, not a tween.** Tabs re-pack via `HorizontalLayoutGroup` when the
  placeholder moves. Smooth animation is explicitly out of scope (optional later polish).
- **Approach A — reassign slots (no separate order field).** Page display order is already
  derived from array-slot order, so reorder = rewriting which `DashboardPage` object lives in
  which slot. On commit, the pages are compacted into slots `1..N` in the new order; trailing
  slots become null. This needs **zero save-format change** — vanilla `Export`/`Import` is
  slot-by-slot, so the new order persists on the next game save exactly like add/delete/rename
  already do. (Rejected alternative B: a separate persisted order list in an external store —
  more sync surface, for a benefit the user never sees.)
- **You stay on the page you were viewing.** `currentView.pageIndex` is repointed to wherever
  the currently-viewed page object lands after compaction.
- **Logic stays pure.** The slot-rewrite lives in `PageOps` (no Unity refs), so it can be unit-
  tested later; the Unity drag/visual layer is verified manually in-game.

## Relevant game mechanics (verified in `GameCode-latest/`)

- **Slot = identity = display order = save key.** `DashboardLayout.pages` is a `DashboardPage[10]`
  with `pages[0]` unused; pages live in `1..9` (`DashboardLayout.cs:12-19`). The tab bar renders
  non-null slots in ascending order (`PageTabBar.cs:83-88`). There is **no** separate order field.
- **Save format is slot-by-slot, order implicit.** `DashboardLayout.Export` writes a presence
  flag + page per slot `0..9` (`DashboardLayout.cs:49-63`); `Import` reads them back into the same
  slots (`:65-87`). Reassigning slots therefore re-persists the new order for free.
- **The sim loop dereferences the current slot every tick.** `CustomCharts.PrepareTick` reads
  `dashboardLayout.pages[currentView.pageIndex].chartDatas` with no null check
  (`CustomCharts.cs:67`), outside the UI try/catch. So the commit must leave
  `currentView.pageIndex` pointing at a valid, occupied slot. The whole reorder runs inside the
  Unity drop handler on the main thread (same thread as the sim), updating the array and
  `currentView.pageIndex` together before yielding — no inconsistent state is observable.
- **`SetViewPage` early-returns on an unchanged index** (`UIDashboard.cs:295`) and re-renders
  the grid; we don't use it on commit (content is unchanged) — we set `currentView.pageIndex`
  directly and only `Refresh()` the tab bar.
- **Default page name is frozen at creation.** `AddPage` sets `name = index.ToString()`
  (`DashboardLayout.cs:36-43`); the tab label is `page.name`, never the live slot
  (`PageTabBar.cs:86`). So compaction renumbering is invisible in the UI, and the pre-existing
  quirk (an unnamed page keeps its creation-number as its label) is unchanged by this feature.
- **Existing PageOps treats slots as reassignable storage.** Deletion already nulls a slot in
  place without shifting (`PageOps.RemovePage`, `PageOps.cs:69-79`), so gaps (e.g. slots 1,3,5)
  already occur and the reorder must handle them.
- **`+` button is a `Button`, not a `PageTab`** (`PageTabBar.cs:194-215`) — it has no drag
  handlers and is therefore not draggable.

## Interaction & mechanics

### Click vs. drag disambiguation
`PageTab` keeps `IPointerClickHandler` and adds `IBeginDragHandler` / `IDragHandler` /
`IEndDragHandler`. Unity's `EventSystem` only promotes a press to a drag once the pointer moves
past `EventSystem.pixelDragThreshold`; below that it's a click, and a drag suppresses the click.
So single-tap→switch, double-click→rename, right-click→menu all keep working unchanged.

### Live reflow via a placeholder (standard `HorizontalLayoutGroup` pattern)
- **Begin drag** — ignore if fewer than 2 pages. Close any open rename input first (its slot is
  about to be reassigned). Insert an empty **placeholder** `LayoutElement` (width = dragged tab's
  width) into the layout group at the dragged tab's current sibling index to hold the gap. Lift
  the dragged tab out of layout (`LayoutElement.ignoreLayout = true`), raise it above its siblings,
  and disable its raycasts so it doesn't block hit-testing.
- **During drag** — move the lifted tab to follow the cursor's x (clamped to the bar, y fixed).
  Compute the target insert index by comparing cursor-x to the midpoints of the *other* tabs;
  when it changes, move the placeholder to that sibling index. The layout group instantly
  re-packs the remaining tabs around the gap (the "slide aside" effect).
- **End drag** — read the final index from the placeholder, remove it, restore the dragged tab
  (`ignoreLayout = false`, re-enable raycasts), build the new ordered page list, commit via
  `PageOps.ReorderPages`, then `Refresh()` for a clean rebuild with correct slots + highlight.

## Components

### 1. `PageOps.cs` — new pure method `ReorderPages`
```
public static void ReorderPages(CustomCharts charts, IReadOnlyList<DashboardPage> newOrder)
```
- Guard: `charts`, `dashboardLayout`, `pages`, `newOrder` non-null; `newOrder` is exactly the
  current set of non-null pages (same count, same members). On mismatch, no-op (defensive).
- Capture the currently-viewed page object: `viewed = pages[currentView.pageIndex]` (may be null
  if the view index was invalid — then leave repointing to the first slot).
- Write `pages[i+1] = newOrder[i]` for `i in 0..N-1`; null `pages[N+1..9]`.
- Set `currentView.pageIndex` to the slot whose page `== viewed` (by reference); if `viewed` was
  null/not found, point at slot 1 (first compacted page).
- No Unity references.

### 2. `PageTab.cs` — add drag handlers
Implement `IBeginDragHandler` / `IDragHandler` / `IEndDragHandler`, each forwarding to the bar:
`_bar.BeginDrag(this, eventData)` / `Drag(this, eventData)` / `EndDrag(this, eventData)`.
Keep `IPointerClickHandler` exactly as-is.

### 3. `PageTabBar.cs` — drag orchestration
- New state fields: `_draggingTab` (PageTab), `_placeholder` (RectTransform/LayoutElement),
  `_dragInsertIndex` (current target index among tabs).
- `BeginDrag` / `Drag` / `EndDrag`: implement the placeholder + cursor-follow + midpoint-index
  logic above. `BeginDrag` closes any active rename (clear `_renamingSlot`, hide `_renameInput`).
- `EndDrag` builds the new ordered `List<DashboardPage>` from the tabs' current visual order
  (with the dragged page at `_dragInsertIndex`), calls `PageOps.ReorderPages`, then `Refresh()`.
- Reuse existing `_tabs`, `Refresh`, `UpdateHighlights`; no change to build/teardown lifecycle.

No changes to Harmony patches, save format, `currentView` plumbing beyond the single
`pageIndex` write, or the existing `DashboardLayoutPatch` (after compaction `pages[1]` is always
occupied when ≥1 page exists, fully compatible with that page-1 import fix).

## Data flow

Drag a tab → `PageTab` drag events → `PageTabBar.BeginDrag/Drag/EndDrag` (placeholder reflow) →
`PageOps.ReorderPages` (compact slots `1..N`, repoint `currentView.pageIndex`) →
`PageTabBar.Refresh()` (rebuild tabs, re-highlight current). New order persists on next game save.

## Persistence

None changed. Page order is encoded by slot index, which `DashboardLayout.Export`/`Import`
already serialize. Existing saves load unchanged; a reordered layout saves and reloads in its
new order with no migration.

## i18n

None. No new user-facing strings (drag-only; no menu labels, no dialogs).

## Edge cases & risks

- **Click/double-click/right-click must survive the new drag handlers (primary risk).** Relies on
  Unity's drag-threshold promoting only real drags; verify in-game that tap→switch,
  double-click→rename, and right-click→menu still work.
- Fewer than 2 pages → begin-drag is a no-op (nothing to reorder).
- Drag begun while a rename input is open → close the rename first (its slot is being reassigned).
- The `+` button is not draggable (no handlers).
- Dragging the currently-viewed page → you stay on it (repointed by reference); dragging a
  non-current page → current view unchanged.
- Pages with gaps from a prior delete (slots 1,3,5) → compacted to 1,2,3 in the new order.
- Reorder never empties a page or changes chart contents; grid is not re-rendered on commit.

## Out of scope

- "Move left / Move right" context-menu entries (drag only).
- Smooth/tweened reflow animation (instant snap only).
- A `PageOps` unit-test project (logic is kept pure so it can be added later if wanted).
- Cross-window or drag-to-create / drag-to-delete gestures.

## Testing / verification

No mod-side test project exists (every `Test*.cs` is the game's own decompiled code), and the
mod references game assemblies. Per this mod's established workflow: keep `ReorderPages` pure
(unit-testable later) and verify manually — build (Release) + in-game. Manual checklist:
1. Drag a tab left and right; other tabs slide aside; drop lands it in the previewed position.
2. Single-tap a tab still switches pages.
3. Double-click a tab still renames; right-click still opens the menu.
4. Drag the page you're currently viewing → you remain on that page after drop.
5. Reorder with gaps (delete a middle page first) → tabs compact and reorder correctly.
6. Save → reload → the new order persists.
7. With a single page, dragging does nothing (no errors).
8. Drag started while a rename input is open closes the rename cleanly.
