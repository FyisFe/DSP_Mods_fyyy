using HarmonyLib;
using UnityEngine;

namespace DashboardOverhaul;

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

        foreach (var item in popupMenu.childButtons)
            if (item.onMenuButtonClick == (System.Action<int>)__instance.CloseAndRemoveChart)
            {
                item.ButtonText = Loc.L("移除此图表", "Remove this chart");
                item.SetState(true);
            }

        if (PageOps.ActivePageCount(charts) > 1)
        {
            var move = popupMenu.AddMenuButton(Loc.L("移动到页面", "Move to page"), -1, true);
            var child = __instance.CreateAndInitChildPopupMenu(move);
            for (int i = 1; i < DashboardLayout.MAX_PAGE_COUNT; i++)
            {
                var page = layout.pages[i];
                if (i == cur || page == null) continue;
                var item = child.AddMenuButton(PageOps.PageName(page, i));
                item.data = i;
                item.onMenuButtonClick += slot => MoveChartToPage(__instance, slot);
                item.SetState(true);
            }
            move.m_ChildMenu = child;
            move.SetState(true);
        }

        int count = PageOps.ChartCount(charts, __instance.chartData.statPlanId);
        var rename = popupMenu.AddMenuButton(string.Format(
            Loc.L("重命名统计项（影响 {0} 个图表）", "Rename statistic (affects {0} charts)"), count), -1, true);
        rename.onMenuButtonClick += _ =>
        {
            __instance.uiDashboard.CloseChartPopupMenu();
            ChartRename.Begin(__instance);
        };
        rename.SetState(true);

        var delete = popupMenu.AddMenuButton(Loc.L("删除统计项及其全部图表", "Delete statistic and all its charts"));
        delete.onMenuButtonClick += _ => ConfirmDelete(__instance);
        delete.SetState(true);
        popupMenu.SetState(true);
    }

    static void MoveChartToPage(UIChart chart, int targetSlot)
    {
        var charts = chart.charts;
        var dash = chart.uiDashboard;
        if (charts == null || dash == null || chart.chartData == null) return;
        var pages = charts.dashboardLayout.pages;
        var source = pages[charts.currentView.pageIndex];
        var target = pages[targetSlot];
        var data = chart.chartData;
        dash.CloseChartPopupMenu();
        if (source == null || target == null) return;
        ChartRename.CancelIfTargeting(chart);
        if (!PageOps.TryMoveChart(source, target, data, dash.CalculateMaxMiniGridCount()))
        {
            UIRealtimeTip.Popup(Loc.L("目标页面没有足够空位，图表保留在原页。",
                "No room on the destination page. The chart stays on its original page."));
            return;
        }
        dash.DetermineCharts();
        UIDashboardPatch.Bar?.ShowMoved(target, data);
    }

    static void ConfirmDelete(UIChart chart)
    {
        if (chart.chartData == null || chart.charts == null) return;
        var dash = chart.uiDashboard;
        var charts = chart.charts;
        int id = chart.chartData.statPlanId;
        var stat = charts.statPlans[id];
        dash.CloseChartPopupMenu();
        ChartRename.CancelIfTargeting(chart);
        UIMessageBox.Show(Loc.L("删除统计项", "Delete statistic"),
            string.Format(Loc.L("删除统计项“{0}”？\n将移除其全部 {1} 个图表（包括其他页面和监控视图），以及侧栏中的统计项。",
                "Delete statistic “{0}”?\nThis removes all {1} associated charts, including other pages and watch views, and the sidebar statistic."),
                stat.displayName, PageOps.ChartCount(charts, id)),
            "取消".Translate(), "确定".Translate(), 1, (UIMessageBox.Response)null,
            new UIMessageBox.Response(() =>
            {
                // A dialog must not act on another save or a recycled statistic id.
                if (dash == null || dash.charts != charts || charts.statPlans?.buffer == null ||
                    id >= charts.statPlans.buffer.Length || charts.statPlans[id] != stat) return;
                ChartRename.Cancel();
                dash.ResetChartPool();
                charts.RemoveStatPlan(id);
                dash.DetermineCharts();
                dash.statboard.DetermineEntryVisible();
            }));
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(UIDashboard), nameof(UIDashboard.PutChartIntoPool))]
    static void PutChartIntoPool_Prefix(UIChart chart) => ChartRename.CancelIfTargeting(chart);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(StatPlan), nameof(StatPlan.Rename))]
    static void Rename_Postfix(StatPlan __instance)
    {
        var dash = UIDashboardPatch.Bar?.Dashboard;
        if (dash == null || dash.charts != __instance.charts) return;
        foreach (var chart in dash.chartPool)
            if (chart.inited && chart.chartData.statPlanId == __instance.id)
            {
                chart.SetTipFormatString();
                chart.titleTip?.RefreshSimpleGeneralTipText();
            }
        foreach (var entry in dash.statboard.objectEntryPool)
            if (entry.statPlan == __instance) entry.nameInput.SetTextWithoutNotify(__instance.name ?? "");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIDashboard), nameof(UIDashboard.TakeChartFromPool))]
    static void TakeChartFromPool_Postfix(UIChart __result)
    {
        if (__result == null || __result.titleText == null) return;
        var titleGo = __result.titleText.gameObject;
        var trigger = titleGo.GetComponent<ChartTitleRenameTrigger>() ?? titleGo.AddComponent<ChartTitleRenameTrigger>();
        __result.titleText.raycastTarget = true;
        trigger.Owner = __result;
    }
}
