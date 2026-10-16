using UnityEngine;
using UnityEngine.UI;

namespace DashboardOverhaul;

public class DashboardView
{
    public UIDashboard Dashboard { get; private set; }
    public PageTabBar Tabs { get; } = new();
    private RectTransform _content;
    private RectTransform _header;
    private Vector2 _statboardTopOffset;
    private Font _font;
    private RectTransform _emptyPanel;
    private Text _emptyText;
    private Button _emptyAdd;
    private Button _emptyCopy;
    private Button _gearButton;
    private Image _gearIcon;
    private Text _gearFallback;
    private int _emptyState = -1;
    private RectTransform _notice;
    private Text _noticeText;
    private DashboardPage _movedPage;
    private ChartData _movedChart;
    private float _noticeUntil;

    private const float kHeaderHeight = 44f;
    private const float kChartLeftMargin = 32f; // Keep chart resize handles clear of the native handle when the sidebar is closed.
    private const float kBaseLeftMargin = 4f;
    internal const float kTopOffset = -4f;
    private const float kStatsPanelButtonWidth = 96f; // right-aligned statistics button
    private const float kGearButtonSize = 28f;        // page-settings gear before the tabs
    private const float kGearButtonGap = 4f;

    internal const float TabLeft = kBaseLeftMargin + kGearButtonSize + kGearButtonGap;
    internal const float TabRight = kBaseLeftMargin + kStatsPanelButtonWidth;

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

        Tabs.Build(dashboard, _header, _font);
        BuildStatsPanelButton();
        BuildGearButton();
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
        Tabs.Free();
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
        Dashboard = null;
    }

    public void Close()
    {
        Tabs.CancelRename();
        ChartRename.Cancel();
        if (_notice != null) _notice.gameObject.SetActive(false);
        _movedPage = null;
        _movedChart = null;
    }

    public void Refresh()
    {
        Tabs.Refresh();
        UpdateLayout();
    }

    public void UpdateLayout()
    {
        Tabs.UpdateLayout();
        UpdateGearIcon();
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
        _emptyAdd = CreateAction(_emptyPanel, Loc.L("添加图表", "Add charts"), -100f, 184f, ShowAddChartUi);
        _emptyCopy = CreateAction(_emptyPanel, Loc.L("复制其他页面", "Copy another page"), 100f, 184f, OpenCopyMenu);
        go.SetActive(false);
    }

    /// <summary>On an empty page, open statistics to create the first statistic, otherwise open the sidebar.</summary>
    private void ShowAddChartUi()
    {
        if (Dashboard == null || Dashboard.charts == null) return;
        if (Dashboard.charts.statPlans.count == 0)
        {
            var uiGame = UIRoot.instance.uiGame;
            if (!uiGame.statWindow.active) DashboardKeyPatch.ToggleStatisticsPanel(uiGame);
            UIRealtimeTip.Popup(Loc.L("在统计窗口中选择需要监控的统计项", "Choose a statistic to monitor in the statistics window"));
        }
        else if (!Dashboard.showSidebar) Dashboard.OnSidebarBtnClick(0);
    }

    /// <summary>Right-aligned statistics button, sharing the P hotkey's window behavior.</summary>
    private void BuildStatsPanelButton()
    {
        var go = new GameObject("DO_StatsPanelBtn", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(_header, false);
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-kBaseLeftMargin, kTopOffset);
        rt.sizeDelta = new Vector2(kStatsPanelButtonWidth, PageTabBar.kTabHeight);

        var bg = go.AddComponent<Image>();
        bg.raycastTarget = true;

        var button = go.AddComponent<Button>();
        button.targetGraphic = bg;
        DashboardUi.StyleButton(button, new Color(0.13f, 0.3f, 0.4f, 0.95f));
        var label = DashboardUi.Text(rt, _font, Loc.L("分析面板", "Statistics"));
        label.alignment = TextAnchor.MiddleCenter;
        // The header row is short; without overflow the game font can wrap and clip the label.
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        DashboardUi.Tip(go, Loc.L("分析面板", "Statistics panel"),
            Loc.L("与按 P 相同：打开或关闭统计面板", "Same as pressing P: opens or closes the statistics panel"));
        button.onClick.AddListener(ToggleStatisticsPanel);
    }

    /// <summary>Gear button that opens the page-wide style/lock menu for the current page. It sits at the
    /// far left of the header, before the first page tab.</summary>
    private void BuildGearButton()
    {
        var go = new GameObject("DO_GearBtn", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(_header, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(kBaseLeftMargin, kTopOffset);
        rt.sizeDelta = new Vector2(kGearButtonSize, PageTabBar.kTabHeight);

        var bg = go.AddComponent<Image>();
        bg.raycastTarget = true;

        var button = go.AddComponent<Button>();
        button.targetGraphic = bg;
        DashboardUi.StyleButton(button, new Color(0.13f, 0.3f, 0.4f, 0.95f));

        var iconGo = new GameObject("Icon", typeof(RectTransform));
        var iconRt = (RectTransform)iconGo.transform;
        iconRt.SetParent(rt, false);
        iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.5f, 0.5f);
        iconRt.pivot = new Vector2(0.5f, 0.5f);
        iconRt.anchoredPosition = Vector2.zero;
        iconRt.sizeDelta = new Vector2(18f, 18f);
        // Use the native style-menu icon; the label covers resource initialization.
        _gearIcon = iconGo.AddComponent<Image>();
        _gearIcon.raycastTarget = false;
        _gearIcon.sprite = GearSprite();
        _gearFallback = DashboardUi.Text(rt, _font, Loc.L("样式", "Style"));
        if (_gearIcon.sprite != null) _gearFallback.gameObject.SetActive(false);

        DashboardUi.Tip(go, Loc.L("页面设置", "Page settings"),
            Loc.L("对当前页所有统计项切换样式或锁定", "Switch the style or lock for every statistic on this page"));
        button.onClick.AddListener(OpenGearMenu);
        _gearButton = button;
    }

    /// <summary>Fill the gear icon as soon as the game's menu icons are available.</summary>
    private void UpdateGearIcon()
    {
        if (_gearIcon == null || _gearIcon.sprite != null) return;
        var sprite = GearSprite();
        if (sprite == null) return;
        _gearIcon.sprite = sprite;
        if (_gearFallback != null) _gearFallback.gameObject.SetActive(false);
    }

    /// <summary>The icon the game's own style menu entry uses (index 3 of the generic menu icons).</summary>
    private static Sprite GearSprite()
    {
        var builtin = Configs.builtin;
        var sprites = builtin != null ? builtin.genericMenuButtonSprites : null;
        if (sprites == null || sprites.Length <= 3) return null;
        return sprites[3];
    }

    private void OpenGearMenu()
    {
        if (Dashboard == null || _gearButton == null) return;
        PageGearMenu.Open(Dashboard, (RectTransform)_gearButton.transform);
    }

    public bool IsGearTarget(Transform target) => _gearButton != null &&
        (target == _gearButton.transform || target.IsChildOf(_gearButton.transform));

    private void ToggleStatisticsPanel()
    {
        if (Dashboard == null) return;
        DashboardKeyPatch.ToggleStatisticsPanel(UIRoot.instance.uiGame);
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
                    Tabs.SwitchTo(slot);
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
