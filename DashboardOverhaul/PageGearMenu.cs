using UnityEngine;

namespace DashboardOverhaul;

/// <summary>
/// The dashboard header's page-level gear menu: the same style choices a single chart offers (generic,
/// background and border styles) applied to <b>every</b> statistic on the current page, plus a page-wide
/// lock/unlock. The menu is built from the game's own popup menu prefab, so it looks and behaves like the
/// gear in a chart's corner.
/// </summary>
public static class PageGearMenu
{
    public static void Open(UIDashboard dashboard, RectTransform anchor)
    {
        var charts = dashboard != null ? dashboard.charts : null;
        if (charts == null || anchor == null) return;
        var page = CurrentPage(charts);
        if (page == null || page.chartDatas == null || page.chartDatas.Count == 0) return;

        dashboard.CloseChartPopupMenu();
        ChartStyleExtras.EnsureCustomStyles(); // the translucent background styles are offered here
        var menu = dashboard.OpenChartPopupMenu(new Vector2(0f, -anchor.rect.height), anchor);
        menu.m_RectTrans.SetParent(dashboard.chartContentRt, true);

        // Sprite index 3 is the icon the game's own style entry uses.
        var style = menu.AddMenuButton("切换图表样式".Translate(), 3);
        style.m_ChildMenu = CreateStyleTypesMenu(dashboard, menu, style, page);
        style.SetState(true);

        // The game's own "监控设置" options, applied to the page's green charts of one kind and size.
        PageObserveMenu.AddTo(dashboard, menu, page);

        var alarm = menu.AddMenuButton(Loc.L("产量告警", "Production alarm"), -1, true);
        alarm.onMenuButtonClick += _ =>
        {
            dashboard.CloseChartPopupMenu();
            ProductionAlarm.Run(dashboard);
        };
        alarm.SetState(true);
        DashboardUi.Tip(alarm.gameObject, Loc.L("产量告警", "Production alarm"),
            Loc.L("按参考速率判定当前页【生产与参考速率】统计项：低于 99% 标红、低于 99.9% 标黄、达标显示黑",
                "Checks every production + reference statistic of this page against its reference rate: " +
                "red below 99%, yellow below 99.9%, black when met"));

        bool allLocked = AllLocked(page);
        var lockItem = menu.AddMenuButton(allLocked
            ? Loc.L("解锁当前页统计项", "Unlock this page")
            : Loc.L("锁定当前页统计项", "Lock this page"), -1, true);
        lockItem.onMenuButtonClick += _ =>
        {
            dashboard.CloseChartPopupMenu();
            SetLocked(dashboard, page, !allLocked);
        };
        lockItem.SetState(true);

        menu.SetState(true);
        dashboard.input_lock = true;
    }

    /// <summary>One entry per style kind, each opening the list of styles of that kind (the game's own
    /// structure and wording).</summary>
    private static UIPopupMenu CreateStyleTypesMenu(UIDashboard dashboard, UIPopupMenu parent,
        UIPopupMenuButton parentBtn, DashboardPage page)
    {
        var menu = CreateChildMenu(dashboard, parent, parentBtn);
        foreach (EChartStyleType type in ChartPresetsDB.GetChartStyleTypeEnumArray())
        {
            if (type == EChartStyleType.None) continue;
            if (type == EChartStyleType.Generic && !HasGenericStyles()) continue; // as the game does
            var item = menu.AddMenuButton("修改样式类型前缀".Translate() + ChartStyle.StyleTypeToString(type));
            item.m_ChildMenu = CreateStylesMenu(dashboard, menu, item, page, type);
            item.SetState(true);
        }
        menu.SetState(true);
        return menu;
    }

    private static UIPopupMenu CreateStylesMenu(UIDashboard dashboard, UIPopupMenu parent,
        UIPopupMenuButton parentBtn, DashboardPage page, EChartStyleType type)
    {
        var menu = CreateChildMenu(dashboard, parent, parentBtn);
        var styles = ChartPresetsDB.GetChartStyles();
        if (styles == null || styles.Length == 0) return menu;

        int current = SharedStyleIndex(page, type);
        var fallback = menu.AddMenuButton("默认".Translate(), current == 0 ? 1 : -1);
        fallback.onMenuButtonClick += _ =>
        {
            dashboard.CloseChartPopupMenu();
            ApplyStyle(dashboard, page, type, 0);
        };
        fallback.SetState(true);

        for (int i = 1; i < styles.Length; i++)
        {
            if (styles[i].type != type) continue;
            int index = i;
            var item = menu.AddMenuButton(styles[i].name.Translate(), current == index ? 1 : -1);
            item.onMenuButtonClick += _ =>
            {
                dashboard.CloseChartPopupMenu();
                ApplyStyle(dashboard, page, type, index);
            };
            item.SetState(true);
        }
        menu.SetState(true);
        return menu;
    }

    /// <summary>Create a submenu next to its button, using the same layout logic as the chart menus.</summary>
    public static UIPopupMenu CreateChildMenu(UIDashboard dashboard, UIPopupMenu parent, UIPopupMenuButton parentBtn)
    {
        var menu = Object.Instantiate(dashboard.menuPrefab, parentBtn.transform);
        menu.m_RectTrans.anchoredPosition =
            new Vector2(parentBtn.m_RectTransform.rect.width, parent.Padding.top);
        menu.gameObject.SetActive(true);

        // Measure against the dashboard to flip the submenu when it would leave the screen.
        menu.m_RectTrans.SetParent(dashboard.chartContentRt, true);
        float width = Mathf.RoundToInt(dashboard.chartContentRt.rect.width);
        float left = menu.m_RectTrans.anchorMin.x * width + menu.m_RectTrans.anchoredPosition.x;
        var rect = menu.m_RectTrans.rect;
        rect.x += left;
        float shift = 0f;
        if (rect.xMin < 0f) shift -= rect.xMin;
        if (rect.xMax > width) shift -= rect.xMax - width;
        menu.m_RectTrans.SetParent(parentBtn.m_RectTransform);
        if (Mathf.Abs(shift) > 0f)
            menu.m_RectTrans.anchoredPosition = new Vector2(-menu.m_RectTrans.rect.width, parent.Padding.top);
        return menu;
    }

    /// <summary>Apply a style index to every chart of the page, then rebuild so the painters repaint.
    /// A locked chart keeps the blue border as its default, so "no border" maps to blue for it.</summary>
    private static void ApplyStyle(UIDashboard dashboard, DashboardPage page, EChartStyleType type, int index)
    {
        foreach (var data in page.chartDatas)
        {
            switch (type)
            {
                case EChartStyleType.Generic: data.genericStyleIndex = index; break;
                case EChartStyleType.Background: data.backgroundStyleIndex = index; break;
                case EChartStyleType.Border: data.borderStyleIndex = ChartLockPersist.ResolveBorder(data, index); break;
            }
        }
        dashboard.DetermineCharts();
    }

    /// <summary>Advance the whole page to the next style of one kind (the style hotkeys do this while the
    /// cursor is over the gear). The first chart of the page decides where the page currently is.</summary>
    public static void CyclePageStyle(UIDashboard dashboard, EChartStyleType type)
    {
        var charts = dashboard != null ? dashboard.charts : null;
        var page = charts != null ? CurrentPage(charts) : null;
        if (page == null || page.chartDatas.Count == 0) return;

        var options = ChartStyleExtras.StyleOptions(type);
        if (options.Count < 2) return;
        int current = StyleIndex(page.chartDatas[0], type);
        int at = options.IndexOf(current);
        int next = options[(at < 0 ? 0 : at + 1) % options.Count];
        ApplyStyle(dashboard, page, type, next);
    }

    /// <summary>Reset every border of the page to its default colour: blue while a chart is locked, no
    /// border otherwise.</summary>
    public static void ResetPageBorder(UIDashboard dashboard)
    {
        var charts = dashboard != null ? dashboard.charts : null;
        var page = charts != null ? CurrentPage(charts) : null;
        if (page == null || page.chartDatas.Count == 0) return;
        ApplyStyle(dashboard, page, EChartStyleType.Border, 0);
    }

    /// <summary>Lock or unlock the whole page, updating the saved flag and the live charts at once. Locked
    /// charts also switch to the blue border (restored when unlocked).</summary>
    private static void SetLocked(UIDashboard dashboard, DashboardPage page, bool locked)
    {
        foreach (var data in page.chartDatas)
        {
            ChartLockPersist.SetLocked(data, locked); // writes the game's flag and the legacy slot
            ChartLockPersist.ApplyLockBorder(data, locked); // covers charts that are off screen
        }

        var pool = dashboard.chartPool;
        if (pool != null)
            foreach (var chart in pool)
                if (chart != null && chart.inited && chart.chartData != null &&
                    page.chartDatas.Contains(chart.chartData))
                    ChartLockPersist.Repaint(chart);
    }

    private static bool HasGenericStyles()
    {
        var styles = ChartPresetsDB.GetChartStyles();
        if (styles == null) return false;
        for (int i = 1; i < styles.Length; i++)
            if (styles[i].type == EChartStyleType.Generic) return true;
        return false;
    }

    private static bool AllLocked(DashboardPage page)
    {
        if (page.chartDatas.Count == 0) return false;
        foreach (var data in page.chartDatas)
            if (!ChartLockPersist.IsLocked(data)) return false;
        return true;
    }

    /// <summary>The style index shared by every chart of the page, or -1 when they differ.</summary>
    private static int SharedStyleIndex(DashboardPage page, EChartStyleType type)
    {
        if (page.chartDatas.Count == 0) return 0;
        int first = StyleIndex(page.chartDatas[0], type);
        foreach (var data in page.chartDatas)
            if (StyleIndex(data, type) != first) return -1;
        return first;
    }

    private static int StyleIndex(ChartData data, EChartStyleType type)
    {
        switch (type)
        {
            case EChartStyleType.Generic: return data.genericStyleIndex;
            case EChartStyleType.Background: return data.backgroundStyleIndex;
            case EChartStyleType.Border: return data.borderStyleIndex;
            default: return 0;
        }
    }

    private static DashboardPage CurrentPage(CustomCharts charts)
    {
        var pages = charts.dashboardLayout != null ? charts.dashboardLayout.pages : null;
        if (pages == null || !PageOps.IsValidViewPage(charts)) return null;
        return pages[charts.currentView.pageIndex];
    }
}
