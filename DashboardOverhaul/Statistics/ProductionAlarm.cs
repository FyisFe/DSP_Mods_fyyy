namespace DashboardOverhaul;

/// <summary>
/// Colours production/reference charts on the current page: below 99% red, below 99.9% yellow, otherwise black.
/// </summary>
public static class ProductionAlarm
{
    private const double kAlarmRatio = 0.99;
    private const double kWarnRatio = 0.999;
    private enum Status { OK, Warning, Alarm }

    private static Status Classify(double actual, double reference)
    {
        double ratio = reference > 0.0 ? actual / reference : 1.0;
        return ratio < kAlarmRatio ? Status.Alarm : ratio < kWarnRatio ? Status.Warning : Status.OK;
    }

    public static void Run(UIDashboard dashboard)
    {
        var charts = dashboard.charts;
        if (!PageOps.IsValidViewPage(charts)) return;
        var page = charts.dashboardLayout.pages[charts.currentView.pageIndex];

        int yellow = ChartStyles.YellowBackgroundIndex;
        int red = ChartStyles.RedBackgroundIndex;
        int black = ChartStyles.BlackBackgroundIndex;
        if (yellow < 0 || red < 0) return; // custom styles are unavailable

        int alarmCount = 0, warnCount = 0, okCount = 0;
        foreach (var data in page.chartDatas)
        {
            if (data.displayTypeParams[0] != ProductionSpeedLayout.kProductionLayout) continue;
            if (!(charts.statPlans[data.statPlanId] is AstroItemProductionStatPlan plan)) continue;
            if (!ProductionSpeedLayout.SupportsLayout(data.presetIndex, plan.timeLevel, data.displayTypeParams[0])) continue;

            double divisor = ProductionSpeedLayout.PeriodDivisor(plan.timeLevel);
            plan.CalculateProductionAndConsumption(out long production, out _);
            plan.GetItemsCyclicRefSpeed(out float reference, out _);
            var status = Classify(System.Math.Abs((double)production) / divisor, reference);

            int background = status == Status.Alarm ? red : status == Status.Warning ? yellow : black;
            if (status == Status.Alarm) alarmCount++;
            else if (status == Status.Warning) warnCount++;
            else okCount++;

            if (!ChartStyles.Apply(data, EChartStyleType.Background, background)) continue;
            ChartStyles.Repaint(ChartStyles.FindChart(dashboard, data));
        }

        int checkedCount = alarmCount + warnCount + okCount;
        if (checkedCount == 0)
        {
            UIRealtimeTip.Popup(Loc.L("当前页没有【生产与参考速率】统计项",
                "No production + reference statistics on this page"));
            return;
        }

        UIRealtimeTip.Popup(string.Format(
            Loc.L("产量告警：红 {0}、黄 {1}、达标 {2}", "Production alarm: {0} red, {1} yellow, {2} OK"),
            alarmCount, warnCount, okCount));
    }
}
