using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DashboardOverhaul;

public class PageTabBar
{
    public UIDashboard Dashboard { get; private set; }
    private RectTransform _root;
    private Font _font;
    private readonly List<PageTab> _tabs = new();
    private InputField _renameInput;
    private int _renamingSlot = -1;
    private RectTransform _addButton;
    private PageTab _draggingTab;
    private RectTransform _placeholder;
    private float _dragFixedY;
    private float _dragZ;
    private int _dragInsertIndex = -1; // last placeholder insert index; skips redundant reflow when unchanged

    private const int kTabHeight = 20;
    private const int kTabMinWidth = 64;
    private const float kTabMaxWidth = 160f;
    private const float kTabHPadding = 20f; // sum of 10px left + 10px right text padding
    private const float kBaseLeftMargin = 40f;
    private const float kTopOffset = -4f;

    public void Build(UIDashboard dashboard)
    {
        Dashboard = dashboard;
        _font = dashboard.emptyTip != null ? dashboard.emptyTip.font : null;
        if (_font == null) DashboardOverhaulPlugin.Logger.LogWarning("[DashboardOverhaul] emptyTip/font is null; tab labels may be invisible.");

        var go = new GameObject("DO_PageTabBar", typeof(RectTransform));
        _root = (RectTransform)go.transform;
        _root.SetParent(dashboard.rectTrans, false);
        // top horizontal row, anchored top-left
        _root.anchorMin = new Vector2(0f, 1f);
        _root.anchorMax = new Vector2(0f, 1f);
        _root.pivot = new Vector2(0f, 1f);
        _root.anchoredPosition = new Vector2(kBaseLeftMargin, kTopOffset);
        _root.sizeDelta = new Vector2(0f, kTabHeight);

        var layout = go.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 4f;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        var fitter = go.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    public void Free()
    {
        if (_root != null) Object.Destroy(_root.gameObject);
        _root = null;
        _renameInput = null;
        _renamingSlot = -1;
        _addButton = null;
        _placeholder = null;
        _draggingTab = null;
        _tabs.Clear();
        Dashboard = null;
    }

    /// <summary>Keep the tab bar clear of the sliding sidebar: offset its x by the
    /// sidebar's currently-visible width so the tabs slide along with it.</summary>
    public void UpdateLayout()
    {
        if (_root == null || Dashboard == null) return;
        float offset = 0f;
        var sidebar = Dashboard.statboardTestRt;
        if (sidebar != null)
            offset = Mathf.Max(0f, sidebar.rect.width + sidebar.anchoredPosition.x);
        _root.anchoredPosition = new Vector2(kBaseLeftMargin + offset, kTopOffset);
    }

    public void Refresh()
    {
        if (_root == null || Dashboard == null) return;
        for (int c = _root.childCount - 1; c >= 0; c--)
            Object.Destroy(_root.GetChild(c).gameObject);
        _tabs.Clear();

        var charts = Dashboard.charts;
        if (charts == null) return;
        var pages = charts.dashboardLayout.pages;
        int current = charts.currentView.pageIndex;
        float tabMax = ComputePerTabMax();
        for (int i = 1; i < DashboardLayout.MAX_PAGE_COUNT; i++)
        {
            if (pages[i] == null) continue;
            string label = string.IsNullOrEmpty(pages[i].name) ? i.ToString() : pages[i].name;
            _tabs.Add(CreateTab(i, label, i == current, tabMax));
        }
        CreateAddButton();
    }

    private PageTab CreateTab(int slot, string label, bool current, float maxWidth)
    {
        var go = new GameObject("DO_Tab_" + slot, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(_root, false);

        var bg = go.AddComponent<Image>();
        bg.raycastTarget = true;

        var le = go.AddComponent<LayoutElement>();
        le.minWidth = kTabMinWidth;
        le.preferredHeight = kTabHeight;

        var textGo = new GameObject("Text", typeof(RectTransform));
        var trt = (RectTransform)textGo.transform;
        trt.SetParent(rt, false);
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(10f, 0f); trt.offsetMax = new Vector2(-10f, 0f);
        var text = textGo.AddComponent<Text>();
        text.font = _font;
        text.fontSize = 14;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        // in the slim bar the font line height can exceed the text box -> default clipping blanks it; Overflow keeps it always rendered
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        var tab = go.AddComponent<PageTab>();
        tab.Label = text;
        tab.Background = bg;
        tab.Setup(this, slot, label, current);
        FitTabWidth(text, le, label, maxWidth);
        return tab;
    }

    /// <summary>Size the tab to its label width (clamped to [min,<paramref name="maxWidth"/>]); ellipsize
    /// the text past the cap so a long page name can't overflow onto the neighbouring tabs. Vertical
    /// Overflow is kept (it's what stops the slim bar from blanking the text); only horizontal spill
    /// is bounded. The cap is per-Refresh and shrinks with page count (see ComputePerTabMax).</summary>
    private static void FitTabWidth(Text text, LayoutElement le, string label, float maxWidth)
    {
        float maxText = maxWidth - kTabHPadding;
        text.text = label;
        if (text.preferredWidth > maxText)
        {
            string s = label;
            while (s.Length > 1)
            {
                s = s.Substring(0, s.Length - 1);
                text.text = s + "...";          // ASCII dots, in case the game font lacks an ellipsis glyph
                if (text.preferredWidth <= maxText) break;
            }
        }
        le.preferredWidth = Mathf.Clamp(text.preferredWidth + kTabHPadding, kTabMinWidth, maxWidth);
    }

    /// <summary>Per-tab width cap = (available width) / MAX_PAGE_COUNT. There are at most 9 page
    /// tabs, so the spare 1/10 share absorbs the + button and margins and the row always fits; on
    /// wide screens this is far larger than a fixed cap, so long names use the space instead of
    /// leaving the bar half-empty. Reads the live dashboard rect and the sidebar offset, so it
    /// adapts to resolution / aspect; falls back to a sane default if the width isn't laid out yet.</summary>
    private float ComputePerTabMax()
    {
        if (Dashboard == null || Dashboard.rectTrans == null) return kTabMaxWidth;
        float dashW = Dashboard.rectTrans.rect.width;
        if (dashW <= 1f) return kTabMaxWidth; // not laid out yet; next Refresh corrects it
        float sidebar = 0f;
        var sb = Dashboard.statboardTestRt;
        if (sb != null) sidebar = Mathf.Max(0f, sb.rect.width + sb.anchoredPosition.x);
        float per = (dashW - sidebar) / DashboardLayout.MAX_PAGE_COUNT;
        return Mathf.Max(kTabMinWidth, per);
    }

    public void SwitchTo(int slot)
    {
        if (Dashboard == null) return;
        Dashboard.SetViewPage(slot);  // vanilla method: switches page and re-lays out charts
        UpdateHighlights();           // only update highlights, don't rebuild tabs (rebuilding would interrupt double-click rename)
    }

    private void UpdateHighlights()
    {
        if (Dashboard == null || Dashboard.charts == null) return;
        int current = Dashboard.charts.currentView.pageIndex;
        foreach (var t in _tabs)
            if (t != null) t.SetCurrent(t.Slot == current);
    }

    public void AddNewPage()
    {
        if (Dashboard == null) return;
        int slot = PageOps.AddPage(Dashboard.charts);
        if (slot < 0)
        {
            UIRealtimeTip.Popup(Loc.L("已达页面上限", "Page limit reached"));
            return;
        }
        Dashboard.SetViewPage(slot);
        Refresh(); // page set changed, rebuild tabs
    }

    private void CreateAddButton()
    {
        var go = new GameObject("DO_AddBtn", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(_root, false);
        _addButton = rt;

        var bg = go.AddComponent<Image>();
        var c = Dashboard.focusColor;
        bg.color = new Color(c.r, c.g, c.b, 0.15f);

        var le = go.AddComponent<LayoutElement>();
        le.minWidth = kTabHeight; // square
        le.preferredHeight = kTabHeight;

        // build the "+" from two white Images, to avoid depending on whether the game font has a '+' glyph (some fonts don't render it)
        AddPlusBar(rt, 12f, 2f);
        AddPlusBar(rt, 2f, 12f);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(AddNewPage);
    }

    private static void AddPlusBar(RectTransform parent, float w, float h)
    {
        var go = new GameObject("Bar", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(w, h);
        var img = go.AddComponent<Image>();
        img.color = Color.white;
        img.raycastTarget = false;
    }

    private InputField EnsureRenameInput()
    {
        if (_renameInput != null) return _renameInput;
        var go = new GameObject("DO_RenameInput", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(_root.parent, false); // parent to the tab bar's parent, floating above the tabs
        rt.sizeDelta = new Vector2(120f, kTabHeight);

        var bg = go.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.85f);

        var textGo = new GameObject("Text", typeof(RectTransform));
        var trt = (RectTransform)textGo.transform;
        trt.SetParent(rt, false);
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(6f, 0f); trt.offsetMax = new Vector2(-6f, 0f);
        var text = textGo.AddComponent<Text>();
        text.font = _font; text.fontSize = 14; text.alignment = TextAnchor.MiddleLeft;
        text.color = Color.white; text.supportRichText = false;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        var input = go.AddComponent<InputField>();
        input.textComponent = text;
        input.lineType = InputField.LineType.SingleLine;
        input.characterLimit = 64;
        input.onEndEdit.AddListener(CommitRename);
        go.SetActive(false);
        _renameInput = input;
        return input;
    }

    public void BeginRename(PageTab tab)
    {
        if (_root == null || Dashboard == null || Dashboard.charts == null) return;
        var input = EnsureRenameInput();
        _renamingSlot = tab.Slot;
        var page = Dashboard.charts.dashboardLayout.pages[tab.Slot];
        input.gameObject.SetActive(true);
        // position over the tab being renamed
        var inputRt = (RectTransform)input.transform;
        var tabRt = (RectTransform)tab.transform;
        inputRt.position = tabRt.position;
        inputRt.sizeDelta = new Vector2(Mathf.Max(120f, tabRt.rect.width), kTabHeight);
        input.text = page != null ? (page.name ?? string.Empty) : string.Empty;
        input.Select();
        input.ActivateInputField();
    }

    private void CommitRename(string value)
    {
        if (_renamingSlot < 0 || Dashboard == null || Dashboard.charts == null) { _renamingSlot = -1; return; }
        var page = Dashboard.charts.dashboardLayout.pages[_renamingSlot];
        PageOps.RenamePage(page, value);
        _renamingSlot = -1;
        if (_renameInput != null) _renameInput.gameObject.SetActive(false);
        Refresh();
    }

    public void OpenContextMenu(PageTab tab)
    {
        var tabRt = (RectTransform)tab.transform;
        var menu = Dashboard.OpenChartPopupMenu(new Vector2(0f, -kTabHeight), tabRt);
        menu.m_RectTrans.SetParent(Dashboard.chartContentRt);

        var rename = menu.AddMenuButton(Loc.L("重命名", "Rename"));
        rename.onMenuButtonClick += _ => { Dashboard.CloseChartPopupMenu(); BeginRename(tab); };
        rename.SetState(true);

        var del = menu.AddMenuButton(Loc.L("删除", "Delete"));
        del.onMenuButtonClick += _ => { Dashboard.CloseChartPopupMenu(); DeletePage(tab); };
        del.SetState(true);

        menu.SetState(true);
    }

    public void DeletePage(PageTab tab)
    {
        var charts = Dashboard.charts;
        if (!PageOps.CanDelete(charts))
        {
            UIRealtimeTip.Popup(Loc.L("至少保留一页", "Keep at least one page"));
            return;
        }
        int slot = tab.Slot;
        var page = charts.dashboardLayout.pages[slot];
        bool hasCharts = page != null && page.chartDatas != null && page.chartDatas.Count > 0;
        if (hasCharts)
            UIMessageBox.Show(Loc.L("删除页面", "Delete page"),
                Loc.L("确认删除该页及其图表？", "Delete this page and its charts?"),
                Loc.L("取消", "Cancel"), Loc.L("确定", "OK"), 1,
                (UIMessageBox.Response)null, new UIMessageBox.Response(() => DoDeletePage(slot)));
        else
            DoDeletePage(slot);
    }

    private void DoDeletePage(int slot)
    {
        if (Dashboard == null || Dashboard.charts == null) return; // dialog callback may fire after teardown
        var charts = Dashboard.charts;
        int target = PageOps.PickPageAfterDelete(charts.dashboardLayout, slot);
        bool deletingCurrent = charts.currentView.pageIndex == slot;
        if (!PageOps.RemovePage(charts, slot)) return;
        if (_renamingSlot == slot) { _renamingSlot = -1; if (_renameInput != null) _renameInput.gameObject.SetActive(false); }
        if (deletingCurrent && target > 0)
            Dashboard.SetViewPage(target);
        Refresh();
    }

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
        _dragInsertIndex = -1; // force the first reflow frame to apply
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

        ReflowPlaceholder(x);
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

        // Insert index = how many non-dragged tab centers are left of the cursor. Counted inline
        // (no list alloc) so we can bail before the reorder when the index hasn't changed.
        int target = 0, realCount = 0;
        var c = new Vector3[4];
        for (int i = 0; i < _root.childCount; i++)
        {
            var child = _root.GetChild(i);
            var pt = child.GetComponent<PageTab>();
            if (pt == null || pt == _draggingTab) continue;
            realCount++;
            ((RectTransform)child).GetWorldCorners(c);
            float cx = (c[0].x + c[2].x) * 0.5f;
            if (cx < cursorWorldX) target++;
        }
        if (target > realCount) target = realCount;
        if (target == _dragInsertIndex) return; // placeholder already here; nothing to reflow
        _dragInsertIndex = target;

        // Rebuild child order: real tabs with the placeholder at the new index, + button last,
        // dragged tab on top.
        var ordered = new List<Transform>(realCount + 2);
        for (int i = 0; i < _root.childCount; i++)
        {
            var child = _root.GetChild(i);
            var pt = child.GetComponent<PageTab>();
            if (pt != null && pt != _draggingTab) ordered.Add(child);
        }
        ordered.Insert(target, _placeholder);
        if (_addButton != null) ordered.Add(_addButton);
        ordered.Add(_draggingTab.transform);
        for (int i = 0; i < ordered.Count; i++) ordered[i].SetSiblingIndex(i);
    }

    private void CleanupDrag()
    {
        _placeholder = null;   // destroyed by Refresh's child sweep
        _draggingTab = null;
        _dragInsertIndex = -1;
    }
}
