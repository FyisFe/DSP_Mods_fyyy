using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DashboardOverhaul;

public class PageTabBar
{
    public UIDashboard Dashboard { get; private set; }
    private RectTransform _content;
    private RectTransform _header;
    private RectTransform _root;
    private Vector2 _statboardTopOffset;
    private Font _font;
    private readonly List<PageTab> _tabs = new();
    private InputField _renameInput;
    private DashboardPage _renamingPage;
    private RectTransform _addButton;
    private PageTab _draggingTab;
    private RectTransform _placeholder;
    private float _dragFixedY;
    private float _dragZ;
    private int _dragInsertIndex = -1; // last placeholder insert index; skips redundant reflow when unchanged
    private HorizontalLayoutGroup _layout;                    // cached at Build; its spacing/padding are read every drag-move frame
    private readonly Vector3[] _dragCorners = new Vector3[4]; // reused per-move scratch (avoids per-frame Vector3[4] allocations)
    private readonly List<Transform> _reflowOrdered = new();  // reused per-move scratch for the sibling reorder
    private float _tabWidth = -1f;
    private RectTransform _emptyPanel;
    private Text _emptyText;
    private Button _emptyAdd;
    private Button _emptyCopy;
    private int _emptyState = -1;
    private RectTransform _notice;
    private Text _noticeText;
    private DashboardPage _movedPage;
    private ChartData _movedChart;
    private float _noticeUntil;

    private const int kTabHeight = 28;
    private const int kTabMinWidth = 64;
    private const float kTabMaxWidth = 160f;
    private const float kDragChipMaxWidth = 120f; // while dragging, the lifted tab shrinks to this cap so a long title doesn't cover the row
    private const float kTabHPadding = 20f; // sum of 10px left + 10px right text padding
    private const float kHeaderHeight = 44f;
    private const float kChartLeftMargin = 32f; // Keep chart resize handles clear of the native handle when the sidebar is closed.
    private const float kBaseLeftMargin = 4f;
    private const float kTopOffset = -4f;

    public void Build(UIDashboard dashboard)
    {
        Dashboard = dashboard;
        _font = dashboard.emptyTip != null ? dashboard.emptyTip.font : null;
        if (_font == null) DashboardOverhaulPlugin.Logger.LogWarning("[DashboardOverhaul] emptyTip/font is null; tab labels may be invisible.");
        // Vanilla toggles this GameObject in _OnUpdate; hide its Text without a per-frame active-state flip.
        if (dashboard.emptyTip != null) dashboard.emptyTip.enabled = false;

        // Keep the grid and charts aligned; the native sidebar overlays them.
        _content = (RectTransform)new GameObject("DO_Content", typeof(RectTransform)).transform;
        _content.SetParent(dashboard.rectTrans, false);
        _content.SetAsFirstSibling();
        _content.anchorMin = Vector2.zero;
        _content.anchorMax = Vector2.one;
        _content.offsetMin = new Vector2(kChartLeftMargin, 0f);
        _content.offsetMax = new Vector2(0f, -kHeaderHeight);

        // Cover the fixed left margin under the native handle.
        var panelColor = new Color(0.04f, 0.12f, 0.19f, 1f);
        var gutter = (RectTransform)new GameObject("DO_SidebarGutter", typeof(RectTransform), typeof(Image)).transform;
        gutter.SetParent(_content, false);
        gutter.anchorMin = Vector2.zero;
        gutter.anchorMax = new Vector2(0f, 1f);
        gutter.pivot = new Vector2(1f, 0.5f);
        gutter.anchoredPosition = Vector2.zero;
        gutter.sizeDelta = new Vector2(kChartLeftMargin, 0f);
        gutter.GetComponent<Image>().color = panelColor;

        Reparent(dashboard.gridRawImage.rectTransform, _content);
        Reparent(dashboard.chartContentRt, _content);
        var statboard = (RectTransform)dashboard.statboard.transform;
        _statboardTopOffset = statboard.offsetMax;
        statboard.offsetMax = _statboardTopOffset - new Vector2(0f, kHeaderHeight);

        _header = (RectTransform)new GameObject("DO_Header", typeof(RectTransform), typeof(Image)).transform;
        _header.SetParent(dashboard.rectTrans, false);
        _header.anchorMin = new Vector2(0f, 1f);
        _header.anchorMax = Vector2.one;
        _header.pivot = new Vector2(0f, 1f);
        _header.sizeDelta = new Vector2(0f, kHeaderHeight);
        _header.anchoredPosition = Vector2.zero;
        // Cover off-screen charts without changing their saved grid positions; block click-through.
        _header.GetComponent<Image>().color = panelColor;
        dashboard.tipsParent.SetAsLastSibling();

        var go = new GameObject("DO_PageTabBar", typeof(RectTransform));
        _root = (RectTransform)go.transform;
        _root.SetParent(_header, false);
        // top horizontal row, anchored top-left
        _root.anchorMin = new Vector2(0f, 1f);
        _root.anchorMax = new Vector2(0f, 1f);
        _root.pivot = new Vector2(0f, 1f);
        _root.anchoredPosition = new Vector2(kBaseLeftMargin, kTopOffset);
        _root.sizeDelta = new Vector2(0f, kTabHeight);

        var layout = go.AddComponent<HorizontalLayoutGroup>();
        _layout = layout;
        layout.spacing = 4f;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        var fitter = go.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        BuildEmptyPanel();
    }

    private static void Reparent(RectTransform child, RectTransform parent)
    {
        var position = child.anchoredPosition;
        child.SetParent(parent, false);
        child.anchoredPosition = position;
    }

    public void Free()
    {
        CancelRename();
        if (_renameInput != null) Object.Destroy(_renameInput.gameObject);
        if (_emptyPanel != null) Object.Destroy(_emptyPanel.gameObject);
        if (_notice != null) Object.Destroy(_notice.gameObject);
        if (_header != null) Object.Destroy(_header.gameObject);
        if (_content != null)
        {
            Reparent(Dashboard.gridRawImage.rectTransform, Dashboard.rectTrans);
            Reparent(Dashboard.chartContentRt, Dashboard.rectTrans);
            Object.Destroy(_content.gameObject);
        }
        ((RectTransform)Dashboard.statboard.transform).offsetMax = _statboardTopOffset;
        _content = null;
        _header = null;
        _root = null;
        _renameInput = null;
        _addButton = null;
        _placeholder = null;
        _draggingTab = null;
        _layout = null;
        _tabs.Clear();
        Dashboard = null;
    }

    public void UpdateLayout()
    {
        if (_root == null || Dashboard == null) return;
        float width = ComputePerTabMax();
        if (_draggingTab == null && !Mathf.Approximately(width, _tabWidth)) ResizeTabs(width);
        if (_renamingPage != null) PositionRenameInput();
        UpdateEmptyPanel();
        if (_notice != null && _notice.gameObject.activeSelf)
        {
            int slot = System.Array.IndexOf(Dashboard.charts.dashboardLayout.pages, _movedPage);
            if (Time.unscaledTime >= _noticeUntil || slot < 1 || !_movedPage.chartDatas.Contains(_movedChart))
                _notice.gameObject.SetActive(false);
            else
                _noticeText.text = Loc.L("已移动到：", "Moved to: ") + PageOps.PageName(_movedPage, slot);
        }
    }

    public void Refresh()
    {
        if (_root == null || Dashboard == null) return;
        CleanupDrag(); // a rebuild cancels any in-progress drag; the lifted tab + placeholder are destroyed in the sweep below
        for (int c = _root.childCount - 1; c >= 0; c--)
        {
            var child = _root.GetChild(c);
            child.gameObject.SetActive(false);
            child.SetParent(null, false); // Destroy is deferred; exclude old tabs from this frame's layout.
            Object.Destroy(child.gameObject);
        }
        _tabs.Clear();

        var charts = Dashboard.charts;
        if (charts == null) return;
        var pages = charts.dashboardLayout.pages;
        int current = charts.currentView.pageIndex;
        float tabMax = ComputePerTabMax();
        for (int i = 1; i < DashboardLayout.MAX_PAGE_COUNT; i++)
        {
            if (pages[i] == null) continue;
            string label = PageOps.PageName(pages[i], i);
            _tabs.Add(CreateTab(i, label, i == current, tabMax));
        }
        CreateAddButton();
        ResizeTabs(tabMax);
        UpdateLayout();
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
        text.supportRichText = false;
        // in the slim bar the font line height can exceed the text box -> default clipping blanks it; Overflow keeps it always rendered
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        var tab = go.AddComponent<PageTab>();
        tab.Label = text;
        tab.Background = bg;
        tab.Setup(this, slot, label, current);
        DashboardUi.Tip(go, label, Loc.L("单击切换 · 双击重命名 · 右键管理 · 拖动排序",
            "Click to switch · Double-click to rename · Right-click to manage · Drag to reorder"));
        FitTabWidth(text, le, label, maxWidth);
        return tab;
    }

    /// <summary>Fit the label to the available width; keep vertical overflow for the game font's line height.</summary>
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
        le.minWidth = Mathf.Min(kTabMinWidth, maxWidth);
        le.preferredWidth = Mathf.Clamp(text.preferredWidth + kTabHPadding, le.minWidth, maxWidth);
    }

    /// <summary>Share the live viewport between active tabs, reserving the add button and gaps.</summary>
    private float ComputePerTabMax()
    {
        if (Dashboard == null || Dashboard.rectTrans == null) return kTabMaxWidth;
        int count = Dashboard.charts == null ? 1 : Mathf.Max(1, PageOps.ActivePageCount(Dashboard.charts));
        return ComputePerTabMax(Dashboard.rectTrans.rect.width, count);
    }

    private static float ComputePerTabMax(float dashW, int count)
    {
        if (dashW <= 1f) return kTabMaxWidth; // not laid out yet
        float available = dashW - kBaseLeftMargin - 16f - kTabHeight - count * 4f;
        return Mathf.Clamp(available / count, 32f, 240f);
    }

    private void ResizeTabs(float width)
    {
        _tabWidth = width;
        foreach (var tab in _tabs)
        {
            tab.FullName = PageOps.PageName(Dashboard.charts.dashboardLayout.pages[tab.Slot], tab.Slot);
            FitTabWidth(tab.Label, tab.GetComponent<LayoutElement>(), tab.FullName, width);
            DashboardUi.Tip(tab.gameObject, tab.FullName,
                Loc.L("单击切换 · 双击重命名 · 右键管理 · 拖动排序",
                    "Click to switch · Double-click to rename · Right-click to manage · Drag to reorder"));
        }
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
        FinishRename();
        Dashboard.CloseChartPopupMenu();
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
        DashboardUi.StyleButton(btn, bg.color);
        bg.color = Color.white;
        btn.interactable = PageOps.ActivePageCount(Dashboard.charts) < DashboardLayout.MAX_PAGE_COUNT - 1;
        DashboardUi.Tip(go, Loc.L("新建页面", "New page"), btn.interactable
            ? Loc.L("在末尾添加一个空白页面", "Append an empty page")
            : Loc.L("已达 9 页上限", "Maximum of 9 pages reached"));
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
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);

        var bg = go.AddComponent<Image>();
        bg.color = Color.black;

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
        FinishRename();
        ChartRename.Cancel();
        var input = EnsureRenameInput();
        var page = Dashboard.charts.dashboardLayout.pages[tab.Slot];
        _renamingPage = page;
        input.gameObject.SetActive(true);
        PositionRenameInput();
        input.text = page != null ? (page.name ?? string.Empty) : string.Empty;
        input.Select();
        input.ActivateInputField();
        input.transform.SetAsLastSibling();
    }

    private static Rect RenameInputRect(Rect tab, Rect header)
    {
        float width = Mathf.Min(Mathf.Max(120f, tab.width), header.width - 8f);
        float x = Mathf.Clamp(tab.xMin, header.xMin + 4f, header.xMax - width - 4f);
        float y = Mathf.Clamp(tab.yMax - kTabHeight, header.yMin + 4f, header.yMax - kTabHeight - 4f);
        return new Rect(x, y, width, kTabHeight);
    }

    private void PositionRenameInput()
    {
        foreach (var tab in _tabs)
            if (Dashboard.charts.dashboardLayout.pages[tab.Slot] == _renamingPage)
            {
                var inputRt = (RectTransform)_renameInput.transform;
                var tabRt = (RectTransform)tab.transform;
                tabRt.GetWorldCorners(_dragCorners);
                Vector2 bottomLeft = _header.InverseTransformPoint(_dragCorners[0]);
                Vector2 topRight = _header.InverseTransformPoint(_dragCorners[2]);
                var rect = RenameInputRect(Rect.MinMaxRect(bottomLeft.x, bottomLeft.y, topRight.x, topRight.y), _header.rect);
                inputRt.sizeDelta = rect.size;
                inputRt.anchoredPosition = new Vector2(rect.xMin - _header.rect.xMin, rect.yMax - _header.rect.yMax);
                return;
            }
    }

    private void CommitRename(string value)
    {
        var page = _renamingPage;
        bool canceled = _renameInput != null && _renameInput.wasCanceled;
        CancelRename();
        if (page == null || canceled || Dashboard?.charts == null) return;
        if (System.Array.IndexOf(Dashboard.charts.dashboardLayout.pages, page) < 1) return;
        PageOps.RenamePage(page, value);
        ResizeTabs(ComputePerTabMax()); // Keep the clicked tab alive when editing loses focus.
    }

    public void FinishRename()
    {
        if (_renamingPage != null) _renameInput.DeactivateInputField();
    }

    public void CancelRename()
    {
        _renamingPage = null;
        DashboardUi.HideInput(_renameInput);
    }

    public void Close()
    {
        CancelRename();
        ChartRename.Cancel();
        if (_notice != null) _notice.gameObject.SetActive(false);
        _movedPage = null;
        _movedChart = null;
    }

    public void OpenContextMenu(PageTab tab)
    {
        var tabRt = (RectTransform)tab.transform;
        var menu = Dashboard.OpenChartPopupMenu(new Vector2(0f, -kTabHeight), tabRt);
        // Keep the tab menu under the header so the native sidebar cannot cover it.

        var rename = menu.AddMenuButton(Loc.L("重命名", "Rename"));
        rename.onMenuButtonClick += _ => { Dashboard.CloseChartPopupMenu(); BeginRename(tab); };
        rename.SetState(true);

        bool canDelete = PageOps.CanDelete(Dashboard.charts);
        var del = menu.AddMenuButton(canDelete ? Loc.L("删除页面", "Delete page")
            : Loc.L("删除页面（至少保留一页）", "Delete page (keep at least one)"));
        del.onMenuButtonClick += _ => { Dashboard.CloseChartPopupMenu(); DeletePage(tab); };
        del.SetState(true);
        del.m_Button.interactable = canDelete;

        menu.SetState(true);
        Dashboard.input_lock = true;
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
                string.Format(Loc.L("删除页面“{0}”及其中 {1} 个图表？统计项仍保留在侧栏。",
                    "Delete page “{0}” and its {1} charts? Statistics remain in the sidebar."),
                    PageOps.PageName(page, slot), page.chartDatas.Count),
                Loc.L("取消", "Cancel"), Loc.L("确定", "OK"), 1,
                (UIMessageBox.Response)null, new UIMessageBox.Response(() => DoDeletePage(charts, page)));
        else
            DoDeletePage(charts, page);
    }

    private void DoDeletePage(CustomCharts charts, DashboardPage page)
    {
        if (Dashboard == null || Dashboard.charts != charts) return;
        int slot = System.Array.IndexOf(charts.dashboardLayout.pages, page);
        if (page == null || slot < 1) return;
        if (!PageOps.CanDelete(charts)) return;
        CancelRename();
        ChartRename.Cancel();
        int target = PageOps.PickPageAfterDelete(charts.dashboardLayout, slot);
        bool deletingCurrent = charts.currentView.pageIndex == slot;
        // UI listeners need the original statPlanId while being unregistered.
        if (deletingCurrent && target > 0)
            Dashboard.SetViewPage(target);
        if (!PageOps.RemovePage(charts, slot)) return;
        Refresh();
    }

    /// <summary>Begin reordering: lift the dragged tab out of the layout, hold its gap with a
    /// placeholder of the same (compact chip) width, and raise it above its siblings so it can follow
    /// the cursor. No-op with fewer than two pages, or while another drag is already active.</summary>
    public void BeginDrag(PageTab tab, PointerEventData eventData)
    {
        if (_root == null || Dashboard == null || tab == null) return;
        if (_draggingTab != null) return; // one drag at a time; a second pointer would otherwise orphan the first lifted tab
        if (_tabs.Count < 2) return;      // nothing to reorder

        // A drag invalidates any in-progress rename (its slot is about to be reassigned).
        CancelRename();

        _draggingTab = tab;
        _dragInsertIndex = -1; // force the first reflow frame to apply
        var rt = (RectTransform)tab.transform;
        var le = tab.GetComponent<LayoutElement>();

        // Capture the tab's current center world position so lifting it doesn't visually jump.
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);                 // 0=BL 1=TL 2=TR 3=BR
        Vector3 center = (corners[0] + corners[2]) * 0.5f;
        _dragFixedY = center.y;
        _dragZ = rt.position.z;

        // Shrink the dragged tab to a compact, ellipsized chip so a long title doesn't overlap the
        // rest of the row while it floats under the cursor. Only affects drag visuals -- the chip is
        // destroyed on drop, where Refresh rebuilds every tab at its full width.
        float width;
        if (tab.Label != null && le != null)
        {
            FitTabWidth(tab.Label, le, tab.FullName, Mathf.Min(_tabWidth, kDragChipMaxWidth));
            width = le.preferredWidth;
        }
        else
        {
            width = rt.rect.width;
        }

        // Compact-width placeholder marks the drop gap; the other tabs slide around it.
        _placeholder = CreatePlaceholder(width);
        _placeholder.SetSiblingIndex(rt.GetSiblingIndex());

        // Lift out of layout control; center pivot so it tracks the cursor by its middle, and apply
        // the compact width directly (ignoreLayout means the LayoutElement no longer drives size).
        if (le != null) le.ignoreLayout = true;
        if (tab.Background != null) tab.Background.raycastTarget = false;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
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
        _root.GetWorldCorners(_dragCorners);
        float halfWidth = rt.rect.width * _root.lossyScale.x * 0.5f;
        float x = Mathf.Clamp(world.x, _dragCorners[0].x + halfWidth, _dragCorners[2].x - halfWidth);
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

        if (!PageOps.ReorderPages(Dashboard.charts, newOrder))
            DashboardOverhaulPlugin.Logger.LogWarning(
                "[DashboardOverhaul] Reorder rejected: built order was not a permutation of the current pages; drag discarded.");
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
        if (_placeholder == null || _root == null) return;

        // Layout metrics for reconstructing resting (placeholder-independent) positions. The layout
        // group is cached at Build; its spacing/padding never change after that.
        float scale = _root.lossyScale.x;
        float spacing = _layout != null ? _layout.spacing : 0f;
        float padLeft = _layout != null ? _layout.padding.left : 0f;
        _root.GetWorldCorners(_dragCorners);
        float barLeft = _dragCorners[0].x;

        // One pass: collect the non-dragged tabs in sibling order while packing them left-to-right at
        // their RESTING positions (as if the placeholder weren't there) and counting how many resting
        // centers sit left of the cursor. Using resting (not live) centers means moving the placeholder
        // never shifts the decision -> the target slot stays stable and predictable even when a
        // long-title tab makes the placeholder very wide.
        _reflowOrdered.Clear();
        int target = 0;
        float x = barLeft + padLeft * scale; // resting left edge of the first tab (world)
        for (int i = 0; i < _root.childCount; i++)
        {
            var child = _root.GetChild(i);
            var pt = child.GetComponent<PageTab>();
            if (pt == null || pt == _draggingTab) continue;
            _reflowOrdered.Add(child);
            ((RectTransform)child).GetWorldCorners(_dragCorners);
            float w = _dragCorners[2].x - _dragCorners[0].x; // stable world width (placeholder shifts position, not width)
            if (x + w * 0.5f < cursorWorldX) target++;
            x += w + spacing * scale;
        }
        if (target > _reflowOrdered.Count) target = _reflowOrdered.Count;
        if (target == _dragInsertIndex) return; // placeholder already here; nothing to reflow
        _dragInsertIndex = target;

        // Apply the new child order: real tabs with the placeholder at the new index, + button last,
        // dragged tab on top.
        _reflowOrdered.Insert(target, _placeholder);
        if (_addButton != null) _reflowOrdered.Add(_addButton);
        _reflowOrdered.Add(_draggingTab.transform);
        for (int i = 0; i < _reflowOrdered.Count; i++) _reflowOrdered[i].SetSiblingIndex(i);
    }

    private void CleanupDrag()
    {
        _placeholder = null;   // destroyed by Refresh's child sweep
        _draggingTab = null;
        _dragInsertIndex = -1;
    }

    private Button CreateAction(Transform parent, string label, float x, float width, UnityEngine.Events.UnityAction action)
    {
        var go = new GameObject("DO_Action", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(x, 0f);
        rt.sizeDelta = new Vector2(width, 30f);
        var bg = go.AddComponent<Image>();
        var button = go.AddComponent<Button>();
        button.targetGraphic = bg;
        DashboardUi.StyleButton(button, new Color(0.13f, 0.3f, 0.4f, 0.95f));
        DashboardUi.Text(rt, _font, label);
        button.onClick.AddListener(action);
        return button;
    }

    private void BuildEmptyPanel()
    {
        var go = new GameObject("DO_EmptyPage", typeof(RectTransform));
        _emptyPanel = (RectTransform)go.transform;
        _emptyPanel.SetParent(Dashboard.chartContentRt, false);
        _emptyPanel.sizeDelta = new Vector2(460f, 112f);
        _emptyText = DashboardUi.Text(_emptyPanel, _font, "", TextAnchor.UpperCenter);
        _emptyAdd = CreateAction(_emptyPanel, Loc.L("添加图表", "Add charts"), -100f, 184f, () =>
        {
            if (Dashboard.charts.statPlans.count == 0)
            {
                UIRoot.instance.uiGame.OpenProductionWindow();
                UIRealtimeTip.Popup(Loc.L("在统计窗口中选择需要监控的统计项", "Choose a statistic to monitor in the statistics window"));
            }
            else if (!Dashboard.showSidebar) Dashboard.OnSidebarBtnClick(0);
        });
        _emptyCopy = CreateAction(_emptyPanel, Loc.L("复制其他页面", "Copy another page"), 100f, 184f, OpenCopyMenu);
        go.SetActive(false);
    }

    private void UpdateEmptyPanel()
    {
        if (_emptyPanel == null || !PageOps.IsValidViewPage(Dashboard.charts)) return;
        var charts = Dashboard.charts;
        var current = charts.dashboardLayout.pages[charts.currentView.pageIndex];
        bool empty = current.chartDatas.Count == 0;
        if (_emptyPanel.gameObject.activeSelf != empty) _emptyPanel.gameObject.SetActive(empty);
        if (!empty) return;
        bool hasStats = charts.statPlans.count > 0;
        bool canCopy = false;
        foreach (var page in charts.dashboardLayout.pages)
            if (page != null && page != current && page.chartDatas.Count > 0) { canCopy = true; break; }
        int state = (hasStats ? 1 : 0) | (canCopy ? 2 : 0) | (Localization.isZHCN ? 4 : 0);
        if (state == _emptyState) return;
        _emptyState = state;
        _emptyText.text = Loc.L("当前页暂无图表\n", "This page has no charts\n") + (hasStats
            ? Loc.L("打开侧栏，点击统计项旁的添加按钮。", "Open the sidebar and use a statistic's add button.")
            : Loc.L("先在统计窗口中添加需要监控的统计项。", "Start by adding a statistic from the statistics window."));
        _emptyAdd.GetComponentInChildren<Text>().text = hasStats ? Loc.L("添加图表", "Add charts")
            : Loc.L("打开统计窗口", "Open statistics");
        _emptyCopy.interactable = canCopy;
        DashboardUi.Tip(_emptyCopy.gameObject, Loc.L("复制其他页面", "Copy another page"), canCopy
            ? Loc.L("复制布局和显示设置；统计项与原页面共享，重命名会同步生效。",
                "Copy layout and display settings. Statistics are shared; renaming affects both pages.")
            : Loc.L("没有其他包含图表的页面", "No other page contains charts"));
    }

    private void OpenCopyMenu()
    {
        var charts = Dashboard.charts;
        var target = charts.dashboardLayout.pages[charts.currentView.pageIndex];
        var menu = Dashboard.OpenChartPopupMenu(new Vector2(0f, 30f), (RectTransform)_emptyCopy.transform);
        menu.m_RectTrans.SetParent(Dashboard.chartContentRt, true);
        for (int i = 1; i < DashboardLayout.MAX_PAGE_COUNT; i++)
        {
            var source = charts.dashboardLayout.pages[i];
            if (source == null || source == target || source.chartDatas.Count == 0) continue;
            var item = menu.AddMenuButton($"{PageOps.PageName(source, i)} ({source.chartDatas.Count})");
            item.onMenuButtonClick += _ =>
            {
                if (Dashboard == null || Dashboard.charts != charts ||
                    System.Array.IndexOf(charts.dashboardLayout.pages, target) < 1) return;
                Dashboard.CloseChartPopupMenu();
                if (!PageOps.CopyIntoEmptyPage(source, target)) return;
                Dashboard.DetermineCharts();
                UpdateEmptyPanel();
                UIRealtimeTip.Popup(Loc.L("已复制图表，统计项与原页共享。", "Charts copied. Statistics are shared with the source page."));
            };
            item.SetState(true);
        }
        menu.SetState(true);
        Dashboard.input_lock = true;
    }

    public void ShowMoved(DashboardPage page, ChartData chart)
    {
        if (_notice == null)
        {
            var go = new GameObject("DO_MoveNotice", typeof(RectTransform));
            _notice = (RectTransform)go.transform;
            _notice.SetParent(Dashboard.rectTrans, false);
            _notice.anchorMin = _notice.anchorMax = new Vector2(0.5f, 0f);
            _notice.pivot = new Vector2(0.5f, 0f);
            _notice.anchoredPosition = new Vector2(0f, 36f);
            _notice.sizeDelta = new Vector2(440f, 96f);
            go.AddComponent<Image>().color = new Color(0.02f, 0.08f, 0.14f, 0.96f);
            _noticeText = DashboardUi.Text(_notice, _font, "", TextAnchor.UpperCenter);
            _noticeText.rectTransform.offsetMin = new Vector2(8f, 36f);
            _noticeText.rectTransform.offsetMax = new Vector2(-8f, -8f);
            CreateAction(_notice, Loc.L("前往", "Go to page"), -50f, 90f, () =>
            {
                int slot = System.Array.IndexOf(Dashboard.charts.dashboardLayout.pages, _movedPage);
                if (slot > 0)
                {
                    SwitchTo(slot);
                    Dashboard.HighlightChart(_movedChart);
                }
                _notice.gameObject.SetActive(false);
            });
            CreateAction(_notice, Loc.L("关闭", "Dismiss"), 50f, 90f, () => _notice.gameObject.SetActive(false));
        }
        _movedPage = page;
        _movedChart = chart;
        _noticeUntil = Time.unscaledTime + 10f;
        _notice.gameObject.SetActive(true);
        _notice.SetAsLastSibling();
        UpdateLayout();
    }
}
