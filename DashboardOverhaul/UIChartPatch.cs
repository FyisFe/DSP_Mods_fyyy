using System.Collections.Generic;
using HarmonyLib;

namespace DashboardOverhaul;

/// <summary>
/// Adds a "Move to page →" submenu to every chart's right-click popup, letting the
/// player move an individual chart from the current page to another existing page.
/// </summary>
public static class UIChartPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIChart), nameof(UIChart.SetPopupMenuButtons))]
    static void SetPopupMenuButtons_Postfix(UIChart __instance, UIPopupMenu popupMenu)
    {
        var charts = __instance.charts;
        if (charts == null || __instance.chartData == null) return;
        var layout = charts.dashboardLayout;
        int cur = charts.currentView.pageIndex;

        // Collect other existing pages (the chart lives on the current page).
        var targets = new List<int>();
        for (int i = 1; i < DashboardLayout.MAX_PAGE_COUNT; i++)
            if (i != cur && layout.pages[i] != null) targets.Add(i);
        if (targets.Count == 0) return; // nowhere to move to

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
        popupMenu.SetState(true);
    }

    static void MoveChartToPage(UIChart chart, int targetSlot)
    {
        var charts = chart.charts;
        if (charts == null) return;
        var layout = charts.dashboardLayout;
        int cur = charts.currentView.pageIndex;
        var curPage = layout.pages[cur];
        var targetPage = layout.pages[targetSlot];
        var cd = chart.chartData;
        if (curPage == null || targetPage == null || cd == null) return;
        if (!curPage.chartDatas.Remove(cd)) return; // chart not on current page; bail

        // Drop it on top of the target page's stack, preserving all its other properties.
        int maxDepth = int.MinValue;
        for (int k = 0; k < targetPage.chartDatas.Count; k++)
            if (targetPage.chartDatas[k].depth > maxDepth) maxDepth = targetPage.chartDatas[k].depth;
        cd.depth = targetPage.chartDatas.Count == 0 ? 0 : maxDepth + 1;
        targetPage.chartDatas.Add(cd);

        if (chart.uiDashboard != null)
        {
            chart.uiDashboard.CloseChartPopupMenu();
            chart.uiDashboard.DetermineCharts(); // re-render current page (chart now gone from it)
        }
    }
}
