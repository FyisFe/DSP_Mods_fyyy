using HarmonyLib;

namespace DashboardOverhaul;

public static class ChartState
{
    // Native chart families use parameters 0/1; these slots keep the native save format intact.
    private const int SaveTagSlot = 9;
    private const int BackgroundSlot = 10;
    private const int BorderSlot = 11;
    private const int LayoutSlot = 12;
    private const int PreviousBorderSlot = 14;
    private const int LegacyLockSlot = 15;
    private const int SaveTag = 0x444F0001;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIChart), nameof(UIChart.OnLockButtonClick))]
    static void OnLockButtonClick_Postfix(UIChart __instance)
    {
        ApplyLockBorder(__instance.chartData, __instance.chartData.locked);
        ChartStyles.Repaint(__instance);
    }

    public static void SetLocked(ChartData data, bool locked)
    {
        if (data.locked == locked) return;
        data.locked = locked;
        ApplyLockBorder(data, locked);
    }

    private static void ApplyLockBorder(ChartData data, bool locked)
    {
        int blue = ChartStyles.BlueBorderIndex;
        if (blue <= 0) return;
        if (locked)
        {
            data.displayTypeParams[PreviousBorderSlot] = ChartStyles.EncodeBorder(data.borderStyleIndex);
            data.borderStyleIndex = blue;
        }
        else if (data.borderStyleIndex == blue)
            data.borderStyleIndex = ChartStyles.DecodeBorder(data.displayTypeParams[PreviousBorderSlot]);
    }

    public static int ResolveBorder(ChartData data, int index) =>
        index == 0 && data.locked && ChartStyles.BlueBorderIndex > 0 ? ChartStyles.BlueBorderIndex : index;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(UIChart), nameof(UIChart.OnStyleMenuButtonClick))]
    static void OnStyleMenuButtonClick_Prefix(UIChart __instance, ref int styleIndex)
    {
        if (styleIndex == -(int)EChartStyleType.Border && __instance.chartData.locked && ChartStyles.BlueBorderIndex > 0)
            styleIndex = ChartStyles.BlueBorderIndex;
    }

    // Native size replacement only transfers selected display parameters. Transfer the native lock
    // and our prior-border state while both ChartData objects still exist, before RemoveChart frees one.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(UIChart), nameof(UIChart.OnSizeMenuButtonClick))]
    static bool OnSizeMenuButtonClick_Prefix(UIChart __instance, int presetIndex)
    {
        var previous = __instance.chartData;
        if (previous.presetIndex == presetIndex) return false;
        var dashboard = __instance.uiDashboard;
        var charts = __instance.charts;
        var page = charts.dashboardLayout.pages[charts.currentView.pageIndex];
        var plan = charts.statPlans[previous.statPlanId];
        var next = page.AddChart(plan, ref previous.pos, presetIndex);
        Inherit(previous, next, plan.type);
        dashboard.PutChartIntoPool(__instance);
        page.RemoveChart(previous);
        dashboard.DetermineCharts();
        return false;
    }

    internal static void Inherit(ChartData previous, ChartData next, EStatPlanType type)
    {
        next.genericStyleIndex = previous.genericStyleIndex;
        next.backgroundStyleIndex = previous.backgroundStyleIndex;
        next.borderStyleIndex = previous.borderStyleIndex;
        next.locked = previous.locked;
        next.InheritedParameter(previous.presetIndex, previous.displayTypeParams, type);
        next.displayTypeParams[PreviousBorderSlot] = previous.displayTypeParams[PreviousBorderSlot];
    }

    private sealed class ExportState
    {
        public int Background, Border;
        public int[] Parameters;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ChartData), nameof(ChartData.Export))]
    static void Export_Prefix(ChartData __instance, out ExportState __state)
    {
        int background = ChartStyles.CustomId(__instance.backgroundStyleIndex);
        int border = ChartStyles.CustomId(__instance.borderStyleIndex);
        int layout = __instance.displayTypeParams[0];
        var plans = GameMain.data?.statistics?.charts?.statPlans;
        if (!ProductionSpeedLayout.IsCustomLayout(layout) || !(plans?[__instance.statPlanId] is AstroItemProductionStatPlan)) layout = 0;
        __state = null;
        if (background == 0 && border == 0 && layout == 0) return;

        __state = new ExportState
        {
            Background = __instance.backgroundStyleIndex, Border = __instance.borderStyleIndex,
            Parameters = __instance.displayTypeParams,
        };
        var saved = (int[])__state.Parameters.Clone();
        saved[SaveTagSlot] = SaveTag;
        saved[BackgroundSlot] = background;
        saved[BorderSlot] = border;
        saved[LayoutSlot] = layout;
        if (layout != 0) saved[0] = 0;
        __instance.displayTypeParams = saved;
        if (background != 0) __instance.backgroundStyleIndex = 0;
        if (border != 0) __instance.borderStyleIndex = 0;
    }

    // Saving must not alter the live chart, even if the underlying stream throws.
    [HarmonyFinalizer]
    [HarmonyPatch(typeof(ChartData), nameof(ChartData.Export))]
    static void Export_Finalizer(ChartData __instance, ExportState __state)
    {
        if (__state == null) return;
        __instance.backgroundStyleIndex = __state.Background;
        __instance.borderStyleIndex = __state.Border;
        __instance.displayTypeParams = __state.Parameters;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ChartData), nameof(ChartData.Import))]
    static void Import_Postfix(ChartData __instance)
    {
        var p = __instance.displayTypeParams;
        if (p[SaveTagSlot] == SaveTag)
        {
            if (__instance.backgroundStyleIndex == 0) __instance.backgroundStyleIndex = ChartStyles.FromId(p[BackgroundSlot], EChartStyleType.Background);
            if (__instance.borderStyleIndex == 0) __instance.borderStyleIndex = ChartStyles.FromId(p[BorderSlot], EChartStyleType.Border);
            if (p[0] == 0 && ProductionSpeedLayout.IsCustomLayout(p[LayoutSlot])) p[0] = p[LayoutSlot];
            p[SaveTagSlot] = p[BackgroundSlot] = p[BorderSlot] = p[LayoutSlot] = 0;
        }
        // Migrate legacy lock metadata once; native locked owns future changes.
        if (p[LegacyLockSlot] != 0) __instance.locked = true;
        p[LegacyLockSlot] = 0;
        p[PreviousBorderSlot] = ChartStyles.EncodeBorder(ChartStyles.DecodeBorder(p[PreviousBorderSlot]));
        __instance.genericStyleIndex = ChartStyles.ValidIndex(__instance.genericStyleIndex, EChartStyleType.Generic);
        __instance.backgroundStyleIndex = ChartStyles.ValidIndex(__instance.backgroundStyleIndex, EChartStyleType.Background);
        __instance.borderStyleIndex = ChartStyles.ValidIndex(__instance.borderStyleIndex, EChartStyleType.Border);
    }
}
