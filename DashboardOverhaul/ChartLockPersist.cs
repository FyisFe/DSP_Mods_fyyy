using HarmonyLib;

namespace DashboardOverhaul;

/// <summary>
/// Keeps a chart's lock across dashboard sessions.
///
/// The game keeps the lock only on the chart instance (<c>UIChart.chart_lock</c>, cleared in
/// <c>UIChart._OnInit</c>), so closing and reopening the dashboard - which recycles every chart through
/// the pool - silently unlocks it, and changing a chart's size rebuilds its data the same way.
///
/// The lock is therefore remembered in the chart's own saved display parameters, in a slot the game does
/// not use: every chart type only reads/writes indices 0 and 1 of <c>ChartData.displayTypeParams</c>,
/// while the array is exported with the chart. The lock thus survives closing the dashboard, resizing
/// the chart and reloading the save.
/// </summary>
public static class ChartLockPersist
{
    /// <summary>Spare slot in <c>ChartData.displayTypeParams</c> (the game uses 0 and 1 only).</summary>
    private const int kLockParamIndex = 15;

    /// <summary>Second spare slot: the border style a chart had before it was locked.</summary>
    private const int kBorderParamIndex = 14;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIDashboard), nameof(UIDashboard.TakeChartFromPool))]
    static void TakeChartFromPool_Postfix(UIChart __result)
    {
        if (__result == null || __result.chartData == null) return;
        bool locked = IsLocked(__result.chartData);
        if (__result.chart_lock == locked) return;
        __result.chart_lock = locked;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIChart), nameof(UIChart.OnLockButtonClick))]
    static void OnLockButtonClick_Postfix(UIChart __instance)
    {
        if (__instance == null || __instance.chartData == null) return;
        if (!SetLocked(__instance.chartData, __instance.chart_lock)) return;
        ApplyLockBorder(__instance);
    }

    /// <summary>Locked charts are shown with the blue border; unlocking restores the border they had.</summary>
    public static void ApplyLockBorder(UIChart chart)
    {
        if (chart == null || chart.chartData == null) return;
        int before = chart.chartData.borderStyleIndex;
        ApplyLockBorder(chart.chartData, chart.chart_lock);
        if (chart.chartData.borderStyleIndex == before) return;
        Repaint(chart);
    }

    /// <summary>Data-only variant, for charts of the page that are not instantiated right now.</summary>
    public static void ApplyLockBorder(ChartData data, bool locked)
    {
        var parameters = data != null ? data.displayTypeParams : null;
        if (parameters == null || parameters.Length <= kBorderParamIndex) return;
        int blue = ChartStyleExtras.BlueBorderIndex;
        if (blue < 0) return;

        if (locked)
        {
            if (data.borderStyleIndex == blue) return;
            parameters[kBorderParamIndex] = data.borderStyleIndex; // remember it for unlocking
            data.borderStyleIndex = blue;
        }
        else
        {
            if (data.borderStyleIndex != blue) return;
            int previous = parameters[kBorderParamIndex];
            data.borderStyleIndex = previous < 0 || previous == blue ? 0 : previous;
        }
    }

    /// <summary>Repaint a live chart after its style fields changed.</summary>
    public static void Repaint(UIChart chart)
    {
        var painters = chart != null ? chart.stylePainters : null;
        if (painters == null) return;
        foreach (var painter in painters)
            if (painter != null) painter.Repaint();
    }

    /// <summary>Border index to use for a chart. A locked chart treats the blue border as its default, so
    /// "no border" (0) maps to blue while it is locked and the colour cycle never lands on "none".</summary>
    public static int ResolveBorder(ChartData data, int index)
    {
        if (index != 0 || !IsLocked(data)) return index;
        int blue = ChartStyleExtras.BlueBorderIndex;
        return blue < 0 ? 0 : blue;
    }

    /// <summary>Changing a chart's size or style builds a new ChartData, so the flag is carried over.</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ChartData), nameof(ChartData.InheritedParameter))]
    static void InheritedParameter_Postfix(ChartData __instance, int[] prevParameter)
    {
        var parameters = __instance != null ? __instance.displayTypeParams : null;
        if (parameters == null || prevParameter == null) return;
        if (parameters.Length <= kLockParamIndex || prevParameter.Length <= kLockParamIndex) return;
        parameters[kLockParamIndex] = prevParameter[kLockParamIndex];
    }

    /// <summary>Whether this chart's saved data carries the locked flag.</summary>
    public static bool IsLocked(ChartData data)
    {
        var parameters = data != null ? data.displayTypeParams : null;
        return parameters != null && parameters.Length > kLockParamIndex && parameters[kLockParamIndex] != 0;
    }

    /// <summary>Write the locked flag into a chart's saved data (used by the page-wide lock, which then
    /// also updates the live chart instances so no rebuild is needed).</summary>
    public static bool SetLocked(ChartData data, bool locked)
    {
        var parameters = data != null ? data.displayTypeParams : null;
        if (parameters == null || parameters.Length <= kLockParamIndex) return false;
        parameters[kLockParamIndex] = locked ? 1 : 0;
        return true;
    }
}
