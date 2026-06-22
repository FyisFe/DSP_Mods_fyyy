using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DashboardOverhaul;

public class PageTabBar
{
    public UIDashboard Dashboard { get; private set; }
    private RectTransform _root;
    private Font _font;
    private readonly List<PageTab> _tabs = new();

    private const int kTabHeight = 26;
    private const int kTabMinWidth = 64;

    public void Build(UIDashboard dashboard)
    {
        Dashboard = dashboard;
        _font = dashboard.emptyTip != null ? dashboard.emptyTip.font : null;

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
        _tabs.Clear();
        Dashboard = null;
    }

    public void Refresh()
    {
        if (_root == null || Dashboard == null) return;
        foreach (var t in _tabs) if (t != null) Object.Destroy(t.gameObject);
        _tabs.Clear();

        var charts = Traverse.Create(Dashboard).Field<CustomCharts>("charts").Value;
        var pages = charts.dashboardLayout.pages;
        int current = charts.currentView.pageIndex;
        for (int i = 1; i < DashboardLayout.MAX_PAGE_COUNT; i++)
        {
            if (pages[i] == null) continue;
            string label = string.IsNullOrEmpty(pages[i].name) ? i.ToString() : pages[i].name;
            _tabs.Add(CreateTab(i, label, i == current));
        }
        // Task 4 会在此后追加 "+" 按钮
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

    // Stubs for future tasks (Task 4+)
    public void OpenContextMenu(PageTab tab) { }
    public void BeginRename(PageTab tab) { }
}
