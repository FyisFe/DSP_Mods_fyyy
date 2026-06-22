# Page Drag-Reorder Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let players drag a dashboard page tab left/right to reorder pages, with the other tabs sliding aside in real time and the new order persisting across save/reload.

**Architecture:** Approach A from the spec — page display order is derived from array-slot order, so reordering rewrites which `DashboardPage` lives in which slot. Drag is implemented on the existing `PageTab`/`PageTabBar` UI: `PageTab` adds Unity drag-handler interfaces that forward to `PageTabBar`, which lifts the dragged tab out of the `HorizontalLayoutGroup`, holds its gap with an equal-width placeholder, slides the other tabs by moving the placeholder, and on drop commits the new order through a new pure `PageOps.ReorderPages` (compact into slots `1..N`, repoint `currentView.pageIndex` to the viewed page). No save-format change.

**Tech Stack:** C# / .NET Framework 4.7.2, BepInEx 5 + HarmonyLib, Unity UI (UnityEngine.UI / UnityEngine.EventSystems, Unity 2022.3), references a **publicized** `Assembly-CSharp.dll`.

## Global Constraints

- Target framework `net472`; BepInEx 5 only — **no new mod dependencies**.
- Build references a PUBLICIZED `Assembly-CSharp.dll` at `..\..\DSP_Mods\AssemblyFromGame\` (relative to the csproj). A stock assembly will not compile.
- **No save-format change.** Page order is encoded by slot index, which `DashboardLayout.Export`/`Import` already serialize slot-by-slot. Old saves load unchanged.
- Page index domain is `1..9` (`pages[0]` is never used); `DashboardLayout.MAX_PAGE_COUNT == 10`.
- The sim loop (`CustomCharts.PrepareTick`) dereferences `pages[currentView.pageIndex]` every tick with no null check, outside the UI try/catch — so any commit must leave `currentView.pageIndex` pointing at a valid, occupied slot. The whole reorder runs synchronously inside the Unity drop handler (same thread as the sim), so no inconsistent state is observable.
- UI strings: none added (drag-only; no new labels or dialogs). Keep using `Loc.L(zh, en)` only if any string is unexpectedly needed.
- Commit messages use the repo style `feat(DashboardOverhaul): …`; **do not** add a Claude co-author trailer.
- All new code lives in the existing `PageOps` / `PageTab` / `PageTabBar` classes; no new Harmony patch class, so no `DashboardOverhaulPlugin.Awake` change.

## Testing reality (read before starting)

This is a BepInEx/Harmony UI mod patched against the live game; there is **no automated test harness** and `PageOps` cannot be unit-tested without standing up a test project (out of scope per the spec). Per the mod's established workflow, each task's verification is: **(1) it compiles** (`dotnet build`), and **(2) a manual in-game check** against an explicit checklist. The manual checks require the human to run DSP with the built DLL loaded (copy `DashboardOverhaul/bin/Debug/net472/DashboardOverhaul.dll` into the game's `BepInEx/plugins/` folder, or use the Release zip). Steps below mark which actions are automated vs. manual.

## File Structure

- `DashboardOverhaul/PageOps.cs` — **modify.** Pure paging logic. Add `ReorderPages(CustomCharts, IReadOnlyList<DashboardPage>)`; add `using System.Collections.Generic;`.
- `DashboardOverhaul/PageTab.cs` — **modify.** Add `IBeginDragHandler/IDragHandler/IEndDragHandler`, each forwarding to `PageTabBar`. Keep `IPointerClickHandler` unchanged.
- `DashboardOverhaul/PageTabBar.cs` — **modify.** Store the `+` button reference; add drag state + `BeginDrag/Drag/EndDrag` + placeholder/reflow helpers; clear drag state in `Free()`.
- `DashboardOverhaul/package/README.md`, `package/CHANGELOG.md`, `package/manifest.json`, `DashboardOverhaul.csproj` — **modify** (Task 3): docs + version bump + release build.

---

### Task 1: `PageOps.ReorderPages` (pure slot-rewrite)

**Files:**
- Modify: `DashboardOverhaul/PageOps.cs`

**Interfaces:**
- Produces: `static void PageOps.ReorderPages(CustomCharts charts, IReadOnlyList<DashboardPage> newOrder)` — writes `newOrder` into slots `1..N`, nulls slots `N+1..9`, and sets `charts.currentView.pageIndex` to the slot where the previously-viewed page object landed (fallback `1`). No-op if `newOrder` is not exactly the current non-null page set.
- Consumes (from game, publicized): `CustomCharts.dashboardLayout` (`DashboardLayout`), `DashboardLayout.pages` (`DashboardPage[]`), `DashboardLayout.MAX_PAGE_COUNT` (int = 10), `CustomCharts.currentView` (`DashboardViewState`) with field `pageIndex` (int). Uses existing `PageOps.ActivePageCount(CustomCharts)`.

- [ ] **Step 1: Add `using System.Collections.Generic;` to the top of `PageOps.cs`**

The file currently starts with `namespace DashboardOverhaul;`. Change the top of the file to:

```csharp
using System.Collections.Generic;

namespace DashboardOverhaul;
```

- [ ] **Step 2: Add the `ReorderPages` method**

Add this method inside the `PageOps` class (e.g. directly after `RemovePage`):

```csharp
    /// <summary>
    /// Reorders pages to match <paramref name="newOrder"/> (the desired left-to-right display
    /// order), compacting them into slots 1..N and nulling the rest. <paramref name="newOrder"/>
    /// must contain exactly the current set of non-null pages (same count and members); on any
    /// mismatch this is a no-op (defensive). Repoints currentView.pageIndex to wherever the
    /// previously-viewed page object lands, so the player stays on the same page. Slot index is the
    /// page's save key (DashboardLayout.Export/Import is slot-by-slot), so the new order persists on
    /// the next game save with no format change.
    /// </summary>
    public static void ReorderPages(CustomCharts charts, IReadOnlyList<DashboardPage> newOrder)
    {
        var pages = charts?.dashboardLayout?.pages;
        if (pages == null || newOrder == null) return;

        // Validate that newOrder is a permutation of the current non-null pages.
        int active = ActivePageCount(charts);
        var set = new HashSet<DashboardPage>();
        foreach (var p in newOrder)
        {
            if (p == null) return;
            set.Add(p);
        }
        if (set.Count != active) return;                 // duplicates or wrong count
        for (int i = 1; i < DashboardLayout.MAX_PAGE_COUNT; i++)
            if (pages[i] != null && !set.Contains(pages[i])) return; // a current page is missing

        // Remember the page the player is viewing (by reference) so we can follow it.
        int cur = charts.currentView.pageIndex;
        DashboardPage viewed = (cur >= 1 && cur < DashboardLayout.MAX_PAGE_COUNT) ? pages[cur] : null;

        // Write the new order into slots 1..N; null the remainder.
        for (int i = 0; i < newOrder.Count; i++)
            pages[i + 1] = newOrder[i];
        for (int i = newOrder.Count + 1; i < DashboardLayout.MAX_PAGE_COUNT; i++)
            pages[i] = null;

        // Repoint the current view to the viewed page's new slot (fallback: first slot).
        int newCur = 1;
        if (viewed != null)
            for (int i = 1; i < DashboardLayout.MAX_PAGE_COUNT; i++)
                if (pages[i] == viewed) { newCur = i; break; }
        charts.currentView.pageIndex = newCur;
    }
```

- [ ] **Step 3: Build (automated)**

Run: `dotnet build "DashboardOverhaul/DashboardOverhaul.csproj" -c Debug`
Expected: `Build succeeded.` with 0 errors. (LF/CRLF warnings are fine.)

- [ ] **Step 4: Commit**

```bash
git add DashboardOverhaul/PageOps.cs
git commit -m "feat(DashboardOverhaul): add PageOps.ReorderPages slot-rewrite"
```

---

### Task 2: Drag-to-reorder on the tab bar

**Files:**
- Modify: `DashboardOverhaul/PageTab.cs` (add drag interfaces forwarding to the bar)
- Modify: `DashboardOverhaul/PageTabBar.cs` (add `+`-button field, drag state, `BeginDrag/Drag/EndDrag`, placeholder/reflow helpers, `Free()` cleanup, store `_addButton` in `CreateAddButton`)

**Interfaces:**
- Consumes: `PageOps.ReorderPages(CustomCharts, IReadOnlyList<DashboardPage>)` (Task 1); existing `PageTabBar.Refresh()`, `PageTabBar._tabs`, `PageTabBar._root`, `PageTabBar.Dashboard`, `PageTabBar._renamingSlot`, `PageTabBar._renameInput`, `PageTabBar.kTabHeight`; `PageTab.Slot` (int), `PageTab.Background` (`Image`); game `UIDashboard.charts` → `CustomCharts.dashboardLayout` → `DashboardPage[] pages`.
- Produces: `PageTabBar.BeginDrag(PageTab, PointerEventData)`, `PageTabBar.Drag(PageTab, PointerEventData)`, `PageTabBar.EndDrag(PageTab, PointerEventData)` (called by `PageTab`'s drag handlers).
- Unity APIs: `IBeginDragHandler/IDragHandler/IEndDragHandler` + `PointerEventData` (`UnityEngine.EventSystems`); `RectTransformUtility.ScreenPointToWorldPointInRectangle`; `RectTransform.GetWorldCorners`; `LayoutElement.ignoreLayout`.

- [ ] **Step 1: Replace `PageTab.cs` with the drag-enabled version**

Replace the entire contents of `DashboardOverhaul/PageTab.cs` with:

```csharp
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DashboardOverhaul;

/// <summary>A single page tab button. Left-click switches page; double-click renames; right-click
/// opens the menu; dragging reorders -- all handled by PageTabBar. Unity only promotes a press to a
/// drag past EventSystem.pixelDragThreshold, and a drag suppresses the click, so the click gestures
/// are unaffected.</summary>
public class PageTab : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public int Slot;
    private PageTabBar _bar;
    public Text Label;
    public Image Background;

    public void Setup(PageTabBar bar, int slot, string label, bool current)
    {
        _bar = bar;
        Slot = slot;
        if (Label != null) Label.text = label;
        SetCurrent(current);
    }

    public void SetCurrent(bool current)
    {
        if (Background == null) return;
        var c = _bar != null ? _bar.Dashboard.focusColor : Color.gray;
        Background.color = current ? c : new Color(c.r, c.g, c.b, 0.15f);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_bar == null) return;
        if (eventData.button == PointerEventData.InputButton.Right)
            _bar.OpenContextMenu(this);
        else if (eventData.clickCount >= 2)
            _bar.BeginRename(this);
        else
            _bar.SwitchTo(Slot);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (_bar != null) _bar.BeginDrag(this, eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_bar != null) _bar.Drag(this, eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (_bar != null) _bar.EndDrag(this, eventData);
    }
}
```

- [ ] **Step 2: Add drag-state fields to `PageTabBar`**

In `DashboardOverhaul/PageTabBar.cs`, add these fields next to the existing private fields (after `private int _renamingSlot = -1;`):

```csharp
    private RectTransform _addButton;
    private PageTab _draggingTab;
    private RectTransform _placeholder;
    private float _dragFixedY;
    private float _dragZ;
```

- [ ] **Step 3: Store the `+` button reference in `CreateAddButton`**

In `CreateAddButton`, immediately after the line `rt.SetParent(_root, false);`, add:

```csharp
        _addButton = rt;
```

- [ ] **Step 4: Clear drag state in `Free()`**

In `Free()`, add these three lines alongside the existing resets (e.g. after `_renamingSlot = -1;`):

```csharp
        _addButton = null;
        _placeholder = null;
        _draggingTab = null;
```

- [ ] **Step 5: Add the drag methods to `PageTabBar`**

Add these methods inside the `PageTabBar` class (e.g. after `DoDeletePage`). They need `using UnityEngine.EventSystems;` — add it to the top of `PageTabBar.cs` (the file already has `using System.Collections.Generic;`, `using UnityEngine;`, `using UnityEngine.UI;`).

```csharp
    /// <summary>Begin reordering: lift the dragged tab out of the layout, hold its gap with an
    /// equal-width placeholder, and raise it above its siblings so it can follow the cursor. No-op
    /// with fewer than two pages.</summary>
    public void BeginDrag(PageTab tab, PointerEventData eventData)
    {
        if (_root == null || Dashboard == null || tab == null) return;

        int tabCount = 0;
        foreach (var t in _tabs) if (t != null) tabCount++;
        if (tabCount < 2) return;   // nothing to reorder

        // A drag invalidates any in-progress rename (its slot is about to be reassigned).
        if (_renamingSlot >= 0)
        {
            _renamingSlot = -1;
            if (_renameInput != null) _renameInput.gameObject.SetActive(false);
        }

        _draggingTab = tab;
        var rt = (RectTransform)tab.transform;

        // Capture the tab's current center world position so lifting it doesn't visually jump.
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);                 // 0=BL 1=TL 2=TR 3=BR
        Vector3 center = (corners[0] + corners[2]) * 0.5f;
        _dragFixedY = center.y;
        _dragZ = rt.position.z;
        float width = rt.rect.width;

        // Equal-width placeholder at the tab's slot keeps the row width and other tabs in place.
        _placeholder = CreatePlaceholder(width);
        _placeholder.SetSiblingIndex(rt.GetSiblingIndex());

        // Lift out of layout control; center pivot so it tracks the cursor by its middle.
        var le = tab.GetComponent<LayoutElement>();
        if (le != null) le.ignoreLayout = true;
        if (tab.Background != null) tab.Background.raycastTarget = false;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.position = center;                         // keep visually in place at grab
        rt.SetAsLastSibling();                        // render on top
    }

    /// <summary>While dragging: move the lifted tab to the cursor's x (clamped to the bar) and slide
    /// the other tabs by repositioning the placeholder.</summary>
    public void Drag(PageTab tab, PointerEventData eventData)
    {
        if (_draggingTab != tab || _root == null) return;
        if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(
                _root, eventData.position, eventData.pressEventCamera, out Vector3 world))
            return;

        var rt = (RectTransform)tab.transform;
        var barCorners = new Vector3[4];
        _root.GetWorldCorners(barCorners);
        float x = Mathf.Clamp(world.x, barCorners[0].x, barCorners[2].x);
        rt.position = new Vector3(x, _dragFixedY, _dragZ);

        ReflowPlaceholder(world.x);
    }

    /// <summary>Drop: derive the new page order from the current child order (placeholder marks the
    /// dragged page's new position), commit it, and rebuild the tab bar.</summary>
    public void EndDrag(PageTab tab, PointerEventData eventData)
    {
        if (_draggingTab != tab) return;
        if (Dashboard == null || Dashboard.charts == null) { CleanupDrag(); Refresh(); return; }

        var layout = Dashboard.charts.dashboardLayout;
        var newOrder = new List<DashboardPage>();
        for (int i = 0; i < _root.childCount; i++)
        {
            var child = _root.GetChild(i);
            if (child == _placeholder)
            {
                var dragged = layout.pages[_draggingTab.Slot];
                if (dragged != null) newOrder.Add(dragged);
                continue;
            }
            var pt = child.GetComponent<PageTab>();
            if (pt == null || pt == _draggingTab) continue;   // skip the + button and the lifted tab
            var page = layout.pages[pt.Slot];
            if (page != null) newOrder.Add(page);
        }

        PageOps.ReorderPages(Dashboard.charts, newOrder);
        CleanupDrag();
        Refresh();   // rebuilds tabs from the new slots; destroys placeholder + lifted tab
    }

    private RectTransform CreatePlaceholder(float width)
    {
        var go = new GameObject("DO_DragPlaceholder", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(_root, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.minWidth = width;
        le.preferredHeight = kTabHeight;
        return rt;
    }

    /// <summary>Position the placeholder among the non-dragged tabs by cursor x, so the others slide
    /// aside to preview the drop. The + button stays last; the dragged tab stays on top.</summary>
    private void ReflowPlaceholder(float cursorWorldX)
    {
        if (_placeholder == null) return;

        var realTabs = new List<RectTransform>();
        for (int i = 0; i < _root.childCount; i++)
        {
            var child = _root.GetChild(i);
            var pt = child.GetComponent<PageTab>();
            if (pt != null && pt != _draggingTab) realTabs.Add((RectTransform)child);
        }

        int target = 0;
        var c = new Vector3[4];
        foreach (var r in realTabs)
        {
            r.GetWorldCorners(c);
            float cx = (c[0].x + c[2].x) * 0.5f;
            if (cx < cursorWorldX) target++;
        }
        if (target > realTabs.Count) target = realTabs.Count;

        var ordered = new List<Transform>(realTabs.Count + 2);
        foreach (var r in realTabs) ordered.Add(r);
        ordered.Insert(target, _placeholder);
        if (_addButton != null) ordered.Add(_addButton);
        ordered.Add((RectTransform)_draggingTab.transform);
        for (int i = 0; i < ordered.Count; i++) ordered[i].SetSiblingIndex(i);
    }

    private void CleanupDrag()
    {
        _placeholder = null;   // destroyed by Refresh's child sweep
        _draggingTab = null;
    }
```

- [ ] **Step 6: Build (automated)**

Run: `dotnet build "DashboardOverhaul/DashboardOverhaul.csproj" -c Debug`
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 7: Manual in-game check (human)**

Load the built DLL in DSP, open the Dashboard with at least 3 pages:
1. **Drag a tab left and right** — the dragged tab follows the cursor and the other tabs slide aside; dropping lands it in the previewed position.
2. **Single-tap** a tab still switches to that page.
3. **Double-click** a tab still opens the rename input; **right-click** still opens the Rename/Delete menu.
4. **Drag the page you're currently viewing** → after dropping you remain on that same page (it stays highlighted).
5. **Reorder with a gap:** delete a middle page first (leaving e.g. slots 1,3,5), then drag to reorder — tabs compact and reorder correctly with no blank gaps.
6. **Save → reload** → the new order persists.
7. With a **single page**, dragging does nothing and logs no errors.
8. Begin a drag **while a rename input is open** → the rename closes cleanly and the drag proceeds.
9. No console errors during or after any drag; the `+` button stays at the end throughout.

- [ ] **Step 8: Commit**

```bash
git add DashboardOverhaul/PageTab.cs DashboardOverhaul/PageTabBar.cs
git commit -m "feat(DashboardOverhaul): drag page tabs to reorder pages"
```

---

### Task 3: Docs, version bump, release build

**Files:**
- Modify: `DashboardOverhaul/package/README.md`
- Modify: `DashboardOverhaul/package/CHANGELOG.md`
- Modify: `DashboardOverhaul/package/manifest.json` (`version_number`)
- Modify: `DashboardOverhaul/DashboardOverhaul.csproj` (`<Version>`)

**Interfaces:** none.

- [ ] **Step 1: Add the feature to `package/README.md`**

Under `## 功能 / Features`, add a bullet:

```markdown
- 拖动页面标签重新排序 / Drag page tabs to reorder pages
```

Under `## 使用 / How to use`, add a bullet (after the Delete-page line):

```markdown
- **重新排序页面**：左右拖动标签 / **Reorder pages**: drag a tab left or right
```

- [ ] **Step 2: Prepend a new entry to `package/CHANGELOG.md`**

Insert this block immediately above the `## [1.1.0] - 2026-06-22` heading:

```markdown
## [1.2.0] - 2026-06-22

页面重新排序 / Page reordering.

### 新增 / Added

- 拖动页面标签即可重新排序，其它标签实时让位；新顺序随存档保存 / Drag a page tab to reorder pages — the other tabs slide aside in real time; the new order persists in your save

```

- [ ] **Step 3: Bump the version to 1.2.0**

In `DashboardOverhaul/DashboardOverhaul.csproj`, change `<Version>1.1.0</Version>` to:

```xml
    <Version>1.2.0</Version>
```

In `DashboardOverhaul/package/manifest.json`, change `"version_number": "1.1.0",` to:

```json
  "version_number": "1.2.0",
```

- [ ] **Step 4: Release build (automated)**

Run: `dotnet build "DashboardOverhaul/DashboardOverhaul.csproj" -c Release`
Expected: `Build succeeded.`; the PostBuild step writes `package/DashboardOverhaul-1.2.0.zip`.

- [ ] **Step 5: Commit**

```bash
git add DashboardOverhaul/package/README.md DashboardOverhaul/package/CHANGELOG.md DashboardOverhaul/package/manifest.json DashboardOverhaul/DashboardOverhaul.csproj DashboardOverhaul/package/DashboardOverhaul-1.2.0.zip
git commit -m "release(DashboardOverhaul): page drag-reorder; docs + version 1.2.0"
```

---

## Self-Review

**Spec coverage:**
- Approach A — reassign slots, compact to `1..N`, no separate order field → Task 1 (`ReorderPages`). ✓
- No save-format change (order rides on slot index) → Task 1; nothing in serialization touched. ✓
- Drag only; live reflow (tabs slide aside in real time); instant snap (no tween) → Task 2 (`BeginDrag/Drag/ReflowPlaceholder` move the placeholder via `SetSiblingIndex`, layout snaps). ✓
- Stay on the viewed page (`currentView.pageIndex` follows the page object) → Task 1 repoint logic + Task 2 manual check #4. ✓
- Click/double-click/right-click survive the new drag handlers → Task 2 Step 1 keeps `IPointerClickHandler`; manual checks #2–3. ✓
- `+` button not draggable (no handlers; stays last) → unchanged `CreateAddButton`; manual check #9. ✓
- Fewer than 2 pages → begin-drag no-op → Task 2 `BeginDrag` guard; manual check #7. ✓
- Drag while renaming closes the rename → Task 2 `BeginDrag` rename-cancel; manual check #8. ✓
- Gaps from prior deletion compacted → Task 1 writes `1..N` and nulls the rest; manual check #5. ✓
- Persists on reload → manual check #6. ✓
- No new i18n strings → confirmed (drag-only); README/CHANGELOG use `Loc`-free user docs only. ✓
- Out of scope (menu Move-left/right, tweened animation, `PageOps` test project, cross-window gestures) — not implemented. ✓

**Placeholder scan:** No TBD/TODO; all code shown in full. No "add error handling"/"handle edge cases" hand-waves — guards (`_draggingTab != tab`, null `_root`/`Dashboard`, `tabCount < 2`, permutation validation) are written out.

**Type consistency:** `PageOps.ReorderPages(CustomCharts, IReadOnlyList<DashboardPage>)` defined in Task 1 and called identically in Task 2's `EndDrag`. `PageTabBar.BeginDrag/Drag/EndDrag(PageTab, PointerEventData)` defined in Task 2 Step 5 and called from `PageTab`'s handlers in Task 2 Step 1. Fields `_addButton`/`_placeholder`/`_draggingTab`/`_dragFixedY`/`_dragZ` declared in Step 2 and used in Steps 3–5. `PageTab.Slot` (int) and `PageTab.Background` (`Image`) match the existing class. Game members verified against `GameCode-latest/`: `CustomCharts.dashboardLayout`, `CustomCharts.currentView.pageIndex`, `DashboardLayout.pages`, `DashboardLayout.MAX_PAGE_COUNT`, `UIDashboard.charts`, `UIDashboard.focusColor`.
