# Chart Rename & Delete Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let players rename and delete a dashboard chart's statistic directly from the chart (popup button + title double-click), without opening the sidebar.

**Architecture:** Extend the existing `UIChart.SetPopupMenuButtons` Harmony postfix in `UIChartPatch` (the same hook the shipped "Move to page" uses) to add **Rename** and **Delete statistic** buttons. Rename opens a floating text input (`ChartRename`, mirroring `PageTabBar`'s inline-rename) that edits `StatPlan.name`; it's also triggered by a double-click handler (`ChartTitleRenameTrigger`) attached to each chart's title via a postfix on `UIDashboard.TakeChartFromPool`. Delete reuses `CustomCharts.RemoveStatPlan` behind the game's own confirm dialog. Both operate on the bound statistic, matching the sidebar.

**Tech Stack:** C# / .NET Framework 4.7.2, BepInEx 5 + HarmonyLib, Unity UI (UnityEngine.UI), references a **publicized** `Assembly-CSharp.dll`.

## Global Constraints

- Target framework `net472`; BepInEx 5 only — **no new mod dependencies**.
- Build references a PUBLICIZED `Assembly-CSharp.dll` at `..\..\DSP_Mods\AssemblyFromGame\` (relative to the csproj). A stock assembly will not compile.
- All new Harmony patch methods go in the already-registered `UIChartPatch` / `UIDashboardPatch` classes (registered via `_harmony.PatchAll(typeof(...))` in `DashboardOverhaulPlugin.Awake`); **no `Awake` change is needed** unless a brand-new patch class is introduced (this plan introduces none).
- UI strings: use `Loc.L(zh, en)` for new labels; reuse existing base-game keys via `"<key>".Translate()` for the delete confirm dialog (free localization). **Do NOT register base-game I18N keys** (overwrites them game-wide).
- No save-format change. `StatPlan.name` is already serialized; delete mutates already-serialized structures.
- Delete operates on the whole statistic (`RemoveStatPlan`) — distinct from vanilla "关闭图表 / Close chart" (single widget), which is left untouched.
- Rename edits `StatPlan.name`, so every chart of that statistic reflects the new name.

## Testing reality (read before starting)

This is a BepInEx/Harmony UI mod patched against the live game; there is **no automated test harness** and UI behavior cannot be unit-tested. Per the mod's established workflow, each task's verification is: **(1) it compiles** (`dotnet build`), and **(2) a manual in-game check** against an explicit checklist. The manual checks require the human to run DSP with the built DLL loaded (copy `DashboardOverhaul/bin/Debug/net472/DashboardOverhaul.dll` into the game's `BepInEx/plugins/` folder, or use the Release zip). Steps below mark which actions are automated vs. manual.

## File Structure

- `DashboardOverhaul/UIChartPatch.cs` — **modify.** Existing chart-popup postfix. Add Rename + Delete buttons; add the `TakeChartFromPool` postfix that attaches the title double-click handler; add delete helpers.
- `DashboardOverhaul/ChartRename.cs` — **create.** Static manager for the floating rename `InputField`; `Begin/CancelIfTargeting/Free`.
- `DashboardOverhaul/ChartTitleRenameTrigger.cs` — **create.** Tiny `MonoBehaviour` on the chart title; double-click → `ChartRename.Begin`.
- `DashboardOverhaul/UIDashboardPatch.cs` — **modify.** Call `ChartRename.Free()` on dashboard `_OnDestroy`.
- `DashboardOverhaul/package/README.md`, `package/CHANGELOG.md`, `DashboardOverhaul.csproj` — **modify** (Task 4): docs + version bump.

---

### Task 1: Rename a chart's statistic (manager + popup button)

**Files:**
- Create: `DashboardOverhaul/ChartRename.cs`
- Modify: `DashboardOverhaul/UIChartPatch.cs` (restructure the move-to-page block; add Rename button)
- Modify: `DashboardOverhaul/UIDashboardPatch.cs` (free the rename input on teardown)

**Interfaces:**
- Produces: `static class ChartRename` with `void Begin(UIChart chart)`, `void CancelIfTargeting(UIChart chart)`, `void Free()`.
- Consumes (from game, publicized): `UIChart.charts` (`CustomCharts`), `UIChart.chartData` (`ChartData`), `UIChart.uiDashboard` (`UIDashboard`), `UIChart.titleText` (`Text`), `UIChart.rectTrans` (`RectTransform`), `UIChart.TruncateStatPlanNameText()`; `CustomCharts.statPlans[int]` → `StatPlan`; `StatPlan.name` (get), `StatPlan.Rename(ref string)`; `UIDashboard.chartContentRt`, `UIDashboard.emptyTip` (`Text`), `UIDashboard.statboard` (`UIStatboard`), `UIDashboard.CloseChartPopupMenu()`; `UIStatboard.DetermineEntryVisible()`; `UIPopupMenu.AddMenuButton(string, int, bool)` → `UIPopupMenuButton` with `.onMenuButtonClick` (`Action<int>`) and `.SetState(bool)`.

- [ ] **Step 1: Create `ChartRename.cs`**

```csharp
using UnityEngine;
using UnityEngine.UI;

namespace DashboardOverhaul;

/// <summary>
/// Floating single-line input for renaming the StatPlan bound to a chart, opened from the chart
/// popup's "Rename" button or by double-clicking the chart title. Mirrors PageTabBar's inline
/// rename. One shared input per dashboard, cleared on teardown. Renaming edits StatPlan.name, so
/// every chart of that statistic shows the new name (matches the sidebar rename).
/// </summary>
public static class ChartRename
{
    private static InputField _input;
    private static UIChart _target;

    /// <summary>Open the rename input over <paramref name="chart"/>'s title, pre-filled with the
    /// current StatPlan name.</summary>
    public static void Begin(UIChart chart)
    {
        if (chart == null || chart.chartData == null || chart.charts == null) return;
        var dash = chart.uiDashboard;
        if (dash == null || dash.chartContentRt == null) return;
        var statPlan = chart.charts.statPlans[chart.chartData.statPlanId];
        if (statPlan == null) return;

        var input = EnsureInput(dash);
        _target = chart;

        var inputRt = (RectTransform)input.transform;
        var anchorRt = chart.titleText != null ? (RectTransform)chart.titleText.transform : chart.rectTrans;
        inputRt.position = anchorRt.position;
        inputRt.sizeDelta = new Vector2(Mathf.Max(140f, anchorRt.rect.width), 22f);

        input.gameObject.SetActive(true);
        input.text = statPlan.name ?? string.Empty;
        input.Select();
        input.ActivateInputField();
    }

    /// <summary>Cancel an in-progress rename if it targets <paramref name="chart"/> (e.g. the chart
    /// is about to be deleted).</summary>
    public static void CancelIfTargeting(UIChart chart)
    {
        if (_target == chart) Hide();
    }

    /// <summary>Destroy the shared input and drop references; call on dashboard teardown.</summary>
    public static void Free()
    {
        if (_input != null) Object.Destroy(_input.gameObject);
        _input = null;
        _target = null;
    }

    private static InputField EnsureInput(UIDashboard dash)
    {
        if (_input != null) return _input;

        var font = dash.emptyTip != null ? dash.emptyTip.font : null;

        var go = new GameObject("DO_ChartRenameInput", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(dash.chartContentRt, false);
        rt.sizeDelta = new Vector2(140f, 22f);

        var bg = go.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.85f);

        var textGo = new GameObject("Text", typeof(RectTransform));
        var trt = (RectTransform)textGo.transform;
        trt.SetParent(rt, false);
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(6f, 0f); trt.offsetMax = new Vector2(-6f, 0f);
        var text = textGo.AddComponent<Text>();
        text.font = font; text.fontSize = 14; text.alignment = TextAnchor.MiddleLeft;
        text.color = Color.white; text.supportRichText = false;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        var input = go.AddComponent<InputField>();
        input.textComponent = text;
        input.lineType = InputField.LineType.SingleLine;
        input.characterLimit = 64;
        input.onEndEdit.AddListener(Commit);
        go.SetActive(false);
        _input = input;
        return input;
    }

    private static void Commit(string value)
    {
        var chart = _target;
        Hide();
        if (chart == null || chart.chartData == null || chart.charts == null) return;
        var statPlan = chart.charts.statPlans[chart.chartData.statPlanId];
        if (statPlan == null) return;
        string newName = (value ?? string.Empty).Trim();
        statPlan.Rename(ref newName);                 // fires onNameChanged -> title repaints
        chart.TruncateStatPlanNameText();             // defensive title refresh
        var dash = chart.uiDashboard;
        if (dash != null && dash.statboard != null)
            dash.statboard.DetermineEntryVisible();    // refresh the sidebar list if present
    }

    private static void Hide()
    {
        if (_input != null) _input.gameObject.SetActive(false);
        _target = null;
    }
}
```

- [ ] **Step 2: Modify `UIChartPatch.cs` — restructure the move block and add the Rename button**

Replace the entire `SetPopupMenuButtons_Postfix` method body with the version below. The only changes vs. the current file: the `if (targets.Count == 0) return;` early-return becomes an `if (targets.Count > 0) { ... }` block so our buttons always appear, and a Rename button is appended before the final `SetState`.

```csharp
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIChart), nameof(UIChart.SetPopupMenuButtons))]
    static void SetPopupMenuButtons_Postfix(UIChart __instance, UIPopupMenu popupMenu)
    {
        var charts = __instance.charts;
        if (charts == null || __instance.chartData == null) return;
        var layout = charts.dashboardLayout;
        int cur = charts.currentView.pageIndex;

        // "Move to page →" — only when another existing page is available as a target.
        var targets = new List<int>();
        for (int i = 1; i < DashboardLayout.MAX_PAGE_COUNT; i++)
            if (i != cur && layout.pages[i] != null) targets.Add(i);
        if (targets.Count > 0)
        {
            var moveBtn = popupMenu.AddMenuButton(Loc.L("移动到页面", "Move to page"), -1, true);
            var child = __instance.CreateAndInitChildPopupMenu(moveBtn);
            foreach (int slot in targets)
            {
                var page = layout.pages[slot];
                string name = string.IsNullOrEmpty(page.name) ? slot.ToString() : page.name;
                var b = child.AddMenuButton(name);
                b.data = slot;
                b.onMenuButtonClick += s => MoveChartToPage(__instance, s);
                b.SetState(true);
            }
            moveBtn.m_ChildMenu = child;
            moveBtn.SetState(true);
        }

        // Rename (edits the bound StatPlan's name; affects all charts of that statistic).
        var renameBtn = popupMenu.AddMenuButton(Loc.L("重命名", "Rename"), -1, true);
        renameBtn.onMenuButtonClick += _ =>
        {
            var dash = __instance.uiDashboard;
            if (dash != null) dash.CloseChartPopupMenu();
            ChartRename.Begin(__instance);
        };
        renameBtn.SetState(true);

        popupMenu.SetState(true);
    }
```

- [ ] **Step 3: Modify `UIDashboardPatch.cs` — free the rename input on teardown**

Replace the existing `OnDestroy_Postfix` with:

```csharp
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIDashboard), "_OnDestroy")]
    static void OnDestroy_Postfix()
    {
        if (Bar != null) { Bar.Free(); Bar = null; }
        ChartRename.Free();
    }
```

- [ ] **Step 4: Build (automated)**

Run: `dotnet build "DashboardOverhaul/DashboardOverhaul.csproj" -c Debug`
Expected: `Build succeeded.` with 0 errors. (Warnings about LF/CRLF or existing analyzers are fine.)

- [ ] **Step 5: Manual in-game check (human)**

Load the built DLL in DSP, open the Dashboard, right-click a chart's gear menu:
1. A **重命名 / Rename** entry appears (below "Move to page" when other pages exist, otherwise below the vanilla items).
2. Clicking it closes the menu and shows a text box over the chart title pre-filled with the current name.
3. Typing a name and pressing Enter (or clicking away) updates the chart title immediately.
4. If the same statistic is charted on another page, that copy shows the new name too; the sidebar entry (if open) shows it.
5. Clearing the text reverts the title to the default name.

- [ ] **Step 6: Commit**

```bash
git add DashboardOverhaul/ChartRename.cs DashboardOverhaul/UIChartPatch.cs DashboardOverhaul/UIDashboardPatch.cs
git commit -m "feat(DashboardOverhaul): rename a chart's statistic from the chart popup"
```

---

### Task 2: Delete statistic from the chart popup

**Files:**
- Modify: `DashboardOverhaul/UIChartPatch.cs` (add Delete button + confirm/delete helpers)

**Interfaces:**
- Consumes: `ChartRename.CancelIfTargeting(UIChart)` (Task 1); `UIChart.uiDashboard`, `UIChart.chartData`, `UIChart.charts`; `ChartData.statPlanId` (int); `CustomCharts.RemoveStatPlan(int)`; `UIDashboard.CloseChartPopupMenu()`, `UIDashboard.DetermineCharts()`, `UIDashboard.statboard`; `UIStatboard.DetermineEntryVisible()`; `UIMessageBox.Show(string, string, string, string, int, UIMessageBox.Response, UIMessageBox.Response)` and `new UIMessageBox.Response(Action)`; `string.Translate()`.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Add the Delete button to `SetPopupMenuButtons_Postfix`**

Insert this block in `UIChartPatch.SetPopupMenuButtons_Postfix` **between the Rename button block and the final `popupMenu.SetState(true);`**:

```csharp
        // Delete statistic — removes this widget AND every copy on all pages + the sidebar entry
        // (distinct from vanilla "Close chart", which removes only this one widget).
        var deleteBtn = popupMenu.AddMenuButton(Loc.L("删除统计项", "Delete statistic"));
        deleteBtn.onMenuButtonClick += _ => ConfirmDelete(__instance);
        deleteBtn.SetState(true);
```

- [ ] **Step 2: Add the confirm + delete helper methods to `UIChartPatch`**

Add these two static methods inside the `UIChartPatch` class (e.g. after `SetPopupMenuButtons_Postfix`):

```csharp
    /// <summary>Closes the popup, cancels any rename on this chart, and shows the game's own
    /// "delete statistic" confirm dialog. Captures dashboard/charts/id up front so the deferred
    /// callback doesn't depend on the (possibly pooled) chart still holding its chartData.</summary>
    static void ConfirmDelete(UIChart chart)
    {
        if (chart == null || chart.chartData == null || chart.charts == null) return;
        var dash = chart.uiDashboard;
        var charts = chart.charts;
        int id = chart.chartData.statPlanId;
        if (dash != null) dash.CloseChartPopupMenu();
        ChartRename.CancelIfTargeting(chart);
        UIMessageBox.Show(
            "确认删除统计项标题".Translate(),
            "确认删除统计项提示".Translate(),
            "取消".Translate(), "确定".Translate(), 1,
            (UIMessageBox.Response)null,
            new UIMessageBox.Response(() => DoDelete(dash, charts, id)));
    }

    /// <summary>Removes the statistic and all its charts everywhere, then rebuilds the page and
    /// refreshes the sidebar.</summary>
    static void DoDelete(UIDashboard dash, CustomCharts charts, int id)
    {
        if (charts == null) return;
        charts.RemoveStatPlan(id);                 // pool + all pages + watch layout (frees ChartData)
        if (dash != null)
        {
            dash.DetermineCharts();                // rebuild current page; orphan widget auto-freed
            if (dash.statboard != null) dash.statboard.DetermineEntryVisible();
        }
    }
```

- [ ] **Step 3: Build (automated)**

Run: `dotnet build "DashboardOverhaul/DashboardOverhaul.csproj" -c Debug`
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 4: Manual in-game check (human)**

1. Right-click a chart → a **删除统计项 / Delete statistic** entry appears (last item).
2. Clicking it closes the menu and shows the game's standard confirm dialog (same wording as the sidebar's delete).
3. **Cancel** leaves everything unchanged.
4. **OK** removes the chart; if the same statistic was charted on other pages, those copies are gone too, and its sidebar entry disappears.
5. Deleting the last chart on a page shows the vanilla empty-dashboard tip; the page tab bar is unchanged.
6. No console errors; vanilla "Close chart" still works and only removes the single widget.

- [ ] **Step 5: Commit**

```bash
git add DashboardOverhaul/UIChartPatch.cs
git commit -m "feat(DashboardOverhaul): delete a statistic from the chart popup"
```

---

### Task 3: Rename via title double-click

**Files:**
- Create: `DashboardOverhaul/ChartTitleRenameTrigger.cs`
- Modify: `DashboardOverhaul/UIChartPatch.cs` (attach the trigger via a `TakeChartFromPool` postfix)

**Interfaces:**
- Produces: `class ChartTitleRenameTrigger : MonoBehaviour, IPointerClickHandler` with public field `UIChart Owner`.
- Consumes: `ChartRename.Begin(UIChart)` (Task 1); `UIChart.titleText` (`Text`, has `.gameObject` and `.raycastTarget`); `UIDashboard.TakeChartFromPool(ChartData)` returns `UIChart` (the postfix uses `__result`).

- [ ] **Step 1: Create `ChartTitleRenameTrigger.cs`**

```csharp
using UnityEngine;
using UnityEngine.EventSystems;

namespace DashboardOverhaul;

/// <summary>
/// Attached to a chart's title Text. Double-clicking the title opens the rename input. Implements
/// only IPointerClickHandler (not pointer-down), so pointer-down still bubbles to UIChart's drag
/// logic and dragging the chart keeps working. Reads the owner chart's live chartData at click time.
/// </summary>
public class ChartTitleRenameTrigger : MonoBehaviour, IPointerClickHandler
{
    public UIChart Owner;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (Owner == null) return;
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (eventData.clickCount >= 2)
            ChartRename.Begin(Owner);
    }
}
```

- [ ] **Step 2: Add the attach postfix to `UIChartPatch`**

Add this method inside `UIChartPatch`. Patching `UIDashboard.TakeChartFromPool` (rather than a `UIChart` lifecycle method) guarantees it fires for every chart type regardless of subclass overrides; the `GetComponent` guard makes re-attach on pooled reuse a no-op.

```csharp
    // Attach the title double-click rename trigger to every chart as it's taken from the pool.
    // TakeChartFromPool is the single chokepoint where any chart (all types) is shown.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIDashboard), nameof(UIDashboard.TakeChartFromPool))]
    static void TakeChartFromPool_Postfix(UIChart __result)
    {
        if (__result == null || __result.titleText == null) return;
        var titleGo = __result.titleText.gameObject;
        var trigger = titleGo.GetComponent<ChartTitleRenameTrigger>();
        if (trigger == null)
        {
            __result.titleText.raycastTarget = true;   // title must receive clicks
            trigger = titleGo.AddComponent<ChartTitleRenameTrigger>();
        }
        trigger.Owner = __result;
    }
```

- [ ] **Step 3: Build (automated)**

Run: `dotnet build "DashboardOverhaul/DashboardOverhaul.csproj" -c Debug`
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 4: Manual in-game check (human)**

1. **Double-click a chart's title** → the rename input opens, pre-filled, exactly like the menu Rename.
2. Renaming via double-click updates the title (and all copies of that statistic / the sidebar).
3. **Drag still works:** click-drag the chart by its title to move it — the chart moves, no rename is triggered by a single drag.
4. A single click on the title does nothing disruptive.
5. Charts with no visible title (compact presets) simply don't respond to title double-click; their menu Rename still works.
6. After moving a chart to another page and back, double-click still works (pooled reuse).

> If drag-from-title is broken by the title becoming a raycast target, fall back to dragging from the chart body (still works); record the issue for follow-up. Menu Rename is unaffected either way.

- [ ] **Step 5: Commit**

```bash
git add DashboardOverhaul/ChartTitleRenameTrigger.cs DashboardOverhaul/UIChartPatch.cs
git commit -m "feat(DashboardOverhaul): rename a chart by double-clicking its title"
```

---

### Task 4: Docs, version bump, release build

**Files:**
- Modify: `DashboardOverhaul/package/README.md`
- Modify: `DashboardOverhaul/package/CHANGELOG.md`
- Modify: `DashboardOverhaul/DashboardOverhaul.csproj` (`<Version>`)
- Modify: `DashboardOverhaul/package/manifest.json` (`version_number`, if present)

**Interfaces:** none.

- [ ] **Step 1: Add the new features to `package/README.md`**

Under the `## 功能 / Features` list, add:

```markdown
- 重命名 / 删除 单个图表的统计项（图表右键菜单，或双击标题重命名）/ Rename · delete a chart's statistic (chart right-click menu, or double-click the title to rename)
```

And under `## 使用 / How to use`, add:

```markdown
- **重命名图表**：右键图表 → 重命名，或双击图表标题 / **Rename a chart**: right-click → Rename, or double-click the chart title
- **删除统计项**：右键图表 → 删除统计项（会移除该统计项在所有页面的图表，有确认）/ **Delete statistic**: right-click → Delete statistic (removes that statistic's charts on every page; confirms first)
```

- [ ] **Step 2: Prepend a new entry to `package/CHANGELOG.md`**

Read the current top version, then add an entry above it for the next version (e.g. `## 1.2.0`) describing: rename a chart's statistic from the popup or by double-clicking the title; delete a statistic from the popup (whole statistic, with confirm). Match the file's existing heading style.

- [ ] **Step 3: Bump the version**

In `DashboardOverhaul/DashboardOverhaul.csproj`, change `<Version>` to the new version used in the changelog (e.g. `1.2.0`). If `package/manifest.json` has a `version_number`, set it to the same value.

- [ ] **Step 4: Release build (automated)**

Run: `dotnet build "DashboardOverhaul/DashboardOverhaul.csproj" -c Release`
Expected: `Build succeeded.`; the PostBuild step refreshes `package/DashboardOverhaul-<version>.zip`.

- [ ] **Step 5: Commit**

```bash
git add DashboardOverhaul/package/README.md DashboardOverhaul/package/CHANGELOG.md DashboardOverhaul/DashboardOverhaul.csproj DashboardOverhaul/package/manifest.json
git commit -m "release(DashboardOverhaul): chart rename & delete; docs + version bump"
```

---

## Self-Review

**Spec coverage:**
- Delete = whole statistic via `RemoveStatPlan` → Task 2. ✓
- Rename = `StatPlan.name` via `StatPlan.Rename` → Task 1. ✓
- Two rename triggers (popup button + title double-click) → Task 1 (button) + Task 3 (double-click). ✓
- Approach A (extend existing popup postfix) → Tasks 1–2. ✓
- Label "删除统计项 / Delete statistic" → Task 2. ✓
- Confirm dialog reusing game keys → Task 2. ✓
- i18n via `Loc.L` + `.Translate()` → Tasks 1–2. ✓
- No save-format change → no serialization touched. ✓
- Edge cases: drag vs double-click (Task 3 handler implements only `IPointerClickHandler`; manual check step 3); `titleText==null` skip (Task 3 guard + check step 5); cancel rename on delete (`CancelIfTargeting`, Task 2); deferred-callback safety (capture dash/charts/id, Task 2); pooled reuse (`GetComponent` guard, Task 3). ✓
- Testing checklist from spec distributed across Task 1/2/3 manual checks. ✓
- Out-of-scope items (per-widget titles, signal-tag picker, vanilla Close) not implemented. ✓

**Placeholder scan:** No TBD/TODO; all code shown in full. CHANGELOG/version values are intentionally chosen at execution time (Task 4 Steps 2–3) because they depend on the current changelog top — the action is fully specified.

**Type consistency:** `ChartRename.Begin/CancelIfTargeting/Free` defined in Task 1 and consumed identically in Tasks 2–3. `ChartTitleRenameTrigger.Owner` (field) set in Task 3's postfix and read in its `OnPointerClick`. `DoDelete(UIDashboard, CustomCharts, int)` and `ConfirmDelete(UIChart)` signatures consistent within Task 2. Game member names verified against `GameCode-latest/` (`titleText`, `charts`, `chartData`, `uiDashboard`, `rectTrans`, `chartContentRt`, `statboard`, `emptyTip`, `RemoveStatPlan`, `DetermineCharts`, `DetermineEntryVisible`, `Rename`, `TruncateStatPlanNameText`, `Translate`).
