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

    public void Build(UIDashboard dashboard)
    {
        Dashboard = dashboard;
        _font = dashboard.emptyTip != null ? dashboard.emptyTip.font : null;
        if (_font == null) DashboardOverhaulPlugin.Logger.LogWarning("[DashboardOverhaul] emptyTip/font is null; tab labels may be invisible.");

        var go = new GameObject("DO_PageTabBar", typeof(RectTransform));
        _root = (RectTransform)go.transform;
        _root.SetParent(dashboard.rectTrans, false);
        // 顶部横排，左上锚点，自栏目顶部下移一点
        _root.anchorMin = new Vector2(0f, 1f);
        _root.anchorMax = new Vector2(0f, 1f);
        _root.pivot = new Vector2(0f, 1f);
        _root.anchoredPosition = new Vector2(40f, -8f); // 注：位置可能需游戏内微调
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
        _tabs.Clear();
        Dashboard = null;
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
        Dashboard.SetViewPage(slot); // 原版方法：切页并重排图表
        Refresh();
    }

    public void AddNewPage()
    {
        if (Dashboard == null) return;
        int slot = PageOps.AddPage(Dashboard.charts);
        if (slot < 0)
        {
            UIRealtimeTip.Popup("已达页面上限".Translate());
            return;
        }
        SwitchTo(slot); // 切到新页并 Refresh
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

        var textGo = new GameObject("Text", typeof(RectTransform));
        var trt = (RectTransform)textGo.transform;
        trt.SetParent(rt, false);
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
        var text = textGo.AddComponent<Text>();
        text.font = _font; text.fontSize = 18; text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white; text.text = "+"; text.raycastTarget = false;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(AddNewPage);
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

        var rename = menu.AddMenuButton("重命名".Translate());
        rename.onMenuButtonClick += _ => { Dashboard.CloseChartPopupMenu(); BeginRename(tab); };
        rename.SetState(true);

        var del = menu.AddMenuButton("删除".Translate());
        del.onMenuButtonClick += _ => { Dashboard.CloseChartPopupMenu(); DeletePage(tab); };
        del.SetState(true);

        menu.SetState(true);
    }

    public void DeletePage(PageTab tab)
    {
        var charts = Dashboard.charts;
        if (!PageOps.CanDelete(charts))
        {
            UIRealtimeTip.Popup("至少保留一页".Translate());
            return;
        }
        int slot = tab.Slot;
        var page = charts.dashboardLayout.pages[slot];
        bool hasCharts = page != null && page.chartDatas != null && page.chartDatas.Count > 0;
        if (hasCharts)
            UIMessageBox.Show("删除页面标题".Translate(), "删除页面提示".Translate(),
                "取消".Translate(), "确定".Translate(), 1,
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
