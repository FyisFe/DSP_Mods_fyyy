using UnityEngine;

namespace DashboardOverhaul;

/// <summary>
/// The gear menu's production alarm. It walks every statistic of the current page that uses the
/// "production + reference rate" layout, compares its production rate with the rate its recipes should
/// reach, and colours the background by the shortfall so a problem is visible at a glance:
/// below 99% red, below 99.9% yellow, and the game's own black background when the rate is met.
/// </summary>
public static class ProductionAlarm
{
    private const double kAlarmRatio = 0.99;
    private const double kWarnRatio = 0.999;

    public static void Run(UIDashboard dashboard)
    {
        var charts = dashboard != null ? dashboard.charts : null;
        var pages = charts != null && charts.dashboardLayout != null ? charts.dashboardLayout.pages : null;
        if (pages == null || !PageOps.IsValidViewPage(charts)) return;
        var page = pages[charts.currentView.pageIndex];
        var pool = charts.statPlans != null ? charts.statPlans.buffer : null;
        if (page == null || page.chartDatas == null || pool == null) return;

        int yellow = ChartStyleExtras.YellowBackgroundIndex;
        int red = ChartStyleExtras.RedBackgroundIndex;
        int black = ChartStyleExtras.BlackBackgroundIndex;
        if (yellow < 0 || red < 0)
        {
            return;
        }

        int alarmCount = 0, warnCount = 0, okCount = 0;
        foreach (var data in page.chartDatas)
        {
            if (data == null || data.displayTypeParams == null || data.displayTypeParams.Length == 0) continue;
            if (data.displayTypeParams[0] != ProductionSpeedLayout.kProductionLayout) continue;
            int id = data.statPlanId;
            if (id < 0 || id >= pool.Length) continue;
            if (!(pool[id] is AstroItemProductionStatPlan plan)) continue;

            double divisor = ProductionSpeedLayout.PeriodDivisor(plan.timeLevel);
            if (divisor <= 0.0) continue;

            plan.CalculateProductionAndConsumption(out long production, out _);
            plan.GetItemsCyclicRefSpeed(out float reference, out _);
            double actual = production / divisor;
            double ratio = reference > 0f ? actual / reference : 1.0;

            // When the game's own black background cannot be identified, fall back to the default look so
            // a previous alarm colour is at least cleared.
            int background = ratio < kAlarmRatio ? red : ratio < kWarnRatio ? yellow : (black >= 0 ? black : 0);
            if (background == red) alarmCount++;
            else if (background == yellow) warnCount++;
            else okCount++;

            if (background >= 0 && data.backgroundStyleIndex != background)
                data.backgroundStyleIndex = background;
        }

        int checkedCount = alarmCount + warnCount + okCount;
        if (checkedCount == 0)
        {
            UIRealtimeTip.Popup(Loc.L("当前页没有【生产与参考速率】统计项",
                "No production + reference statistics on this page"));
            return;
        }

        dashboard.DetermineCharts();
        UIRealtimeTip.Popup(string.Format(
            Loc.L("产量告警：红 {0}、黄 {1}、达标 {2}", "Production alarm: {0} red, {1} yellow, {2} OK"),
            alarmCount, warnCount, okCount));
    }
}
