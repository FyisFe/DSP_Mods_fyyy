using System.Collections.Generic;
using UnityEngine;
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

    private const int kTabHeight = 26;
    private const int kTabMinWidth = 64;
    private const float kBaseLeftMargin = 40f;
    private const float kTopOffset = -8f;

    public void Build(UIDashboard dashboard)
    {
        Dashboard = dashboard;
        _font = dashboard.emptyTip != null ? dashboard.emptyTip.font : null;
        if (_font == null) DashboardOverhaulPlugin.Logger.LogWarning("[DashboardOverhaul] emptyTip/font is null; tab labels may be invisible.");

        var go = new GameObject("DO_PageTabBar", typeof(RectTransform));
        _root = (RectTransform)go.transform;
        _root.SetParent(dashboard.rectTrans, false);
        // 顶部横排，左上锚点
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

        // 预留顶部条带：把图表画布与网格整体下移，避免图表被标签栏盖住。
        // 同步平移二者（坐标换算用的是网格 rect 的尺寸，不受位移影响），保持对齐。
        float reserve = (-kTopOffset) + kTabHeight + 6f;
        ShiftDown(dashboard.chartContentRt, reserve);
        if (dashboard.gridRawImage != null &&
            !dashboard.gridRawImage.rectTransform.IsChildOf(dashboard.chartContentRt))
            ShiftDown(dashboard.gridRawImage.rectTransform, reserve);
    }

    private static void ShiftDown(RectTransform rt, float h)
    {
        if (rt != null)
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, rt.anchoredPosition.y - h);
    }

    public void Free()
    {
        if (_root != null) Object.Destroy(_root.gameObject);
        _root = null;
        _renameInput = null;
        _renamingSlot = -1;
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
        for (int i = 1; i < DashboardLayout.MAX_PAGE_COUNT; i++)
        {
            if (pages[i] == null) continue;
            string label = string.IsNullOrEmpty(pages[i].name) ? i.ToString() : pages[i].name;
            _tabs.Add(CreateTab(i, label, i == current));
        }
        CreateAddButton();
    }

    private PageTab CreateTab(int slot, string label, bool current)
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

        var tab = go.AddComponent<PageTab>();
        tab.Label = text;
        tab.Background = bg;
        tab.Setup(this, slot, label, current);
        return tab;
    }

    public void SwitchTo(int slot)
    {
        if (Dashboard == null) return;
        Dashboard.SetViewPage(slot);  // 原版方法：切页并重排图表
        UpdateHighlights();           // 只更新高亮，不重建标签（重建会打断双击重命名）
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
        Refresh(); // 页集合变化，需重建标签
    }

    private void CreateAddButton()
    {
        var go = new GameObject("DO_AddBtn", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(_root, false);

        var bg = go.AddComponent<Image>();
        var c = Dashboard.focusColor;
        bg.color = new Color(c.r, c.g, c.b, 0.15f);

        var le = go.AddComponent<LayoutElement>();
        le.minWidth = kTabHeight; // 方形
        le.preferredHeight = kTabHeight;

        // 用两条白色 Image 拼出 "+"，避免依赖游戏字体是否包含 '+' 字形（部分字体不渲染）
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
        rt.SetParent(_root.parent, false); // 挂在标签栏的父级，浮在标签之上
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

        var input = go.AddComponent<InputField>();
        input.textComponent = text;
        input.lineType = InputField.LineType.SingleLine;
        input.characterLimit = 24;
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
        // 定位到被改标签的位置
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
        var charts = Dashboard.charts;
        int target = PageOps.PickPageAfterDelete(charts.dashboardLayout, slot);
        bool deletingCurrent = charts.currentView.pageIndex == slot;
        if (!PageOps.RemovePage(charts, slot)) return;
        if (_renamingSlot == slot) { _renamingSlot = -1; if (_renameInput != null) _renameInput.gameObject.SetActive(false); }
        if (deletingCurrent && target > 0)
            Dashboard.SetViewPage(target);
        Refresh();
    }
}
