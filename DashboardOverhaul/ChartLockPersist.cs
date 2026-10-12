using HarmonyLib;

namespace DashboardOverhaul;

/// <summary>
/// Keeps a chart's lock across dashboard sessions.
///
/// Historically the game kept the lock only on the chart instance (<c>UIChart.chart_lock</c>, cleared in
/// <c>UIChart._OnInit</c>), so closing and reopening the dashboard - which recycles every chart through
/// the pool - silently unlocked it, and changing a chart's size rebuilt its data the same way.
///
/// The game now stores the lock on the chart's saved data (<c>ChartData.locked</c>) and no longer clears
/// it in <c>UIChart._OnInit</c>, so the game itself persists the lock; the patch below only re-applies it
/// and the blue border to a recycled chart instance.
///
/// The legacy flag in the chart's own saved display parameters is still read, in a slot the game does not
/// use: every chart type only reads/writes indices 0 and 1 of <c>ChartData.displayTypeParams</c>, while
/// the array is exported with the chart. Lock states saved before the game stored them natively are thus
/// carried over once, and are written into <c>ChartData.locked</c> by the game from then on.
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
        // The game owns ChartData.locked now, so the legacy flag is only migrated in one direction: a lock
        // recorded by an older version of this mod is turned into the game's flag, never the other way
        // around (a chart locked in game may legitimately have no legacy flag).
        var data = __result.chartData;
        if (LegacyLocked(data) && !data.locked) data.locked = true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIChart), nameof(UIChart.OnLockButtonClick))]
    static void OnLockButtonClick_Postfix(UIChart __instance)
    {
        if (__instance == null || __instance.chartData == null) return;
        SetLocked(__instance.chartData, __instance.chartData.locked);
        ApplyLockBorder(__instance);
    }

    /// <summary>Locked charts are shown with the blue border; unlocking restores the border they had.</summary>
    public static void ApplyLockBorder(UIChart chart)
    {
        if (chart == null || chart.chartData == null) return;
        int before = chart.chartData.borderStyleIndex;
        ApplyLockBorder(chart.chartData, chart.chartData.locked);
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

    /// <summary>Changing a chart's size or style builds a new ChartData, and the game's
    /// <c>InheritedParameter</c> only copies <c>displayTypeParams[0]</c>, so the lock is carried over here:
    /// the legacy slot is copied and, when it recorded a lock, the game's own flag is set as well.</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ChartData), nameof(ChartData.InheritedParameter))]
    static void InheritedParameter_Postfix(ChartData __instance, int[] prevParameter, EStatPlanType statPlanType)
    {
        var parameters = __instance != null ? __instance.displayTypeParams : null;
        if (parameters == null || prevParameter == null) return;
        if (parameters.Length <= kLockParamIndex || prevParameter.Length <= kLockParamIndex) return;
        parameters[kLockParamIndex] = prevParameter[kLockParamIndex];
        if (parameters[kLockParamIndex] != 0) __instance.locked = true;
    }

    /// <summary>Whether a chart counts as locked: the game's own flag (used from DSP 0.10.35.29131 on) or
    /// the legacy slot written by older versions of this mod.</summary>
    public static bool IsLocked(ChartData data)
    {
        if (data != null && data.locked) return true;
        return LegacyLocked(data);
    }

    private static bool LegacyLocked(ChartData data)
    {
        var parameters = data != null ? data.displayTypeParams : null;
        return parameters != null && parameters.Length > kLockParamIndex && parameters[kLockParamIndex] != 0;
    }

    /// <summary>Write the lock into a chart's saved data: the game's own field, plus the legacy slot so old
    /// saves stay readable by older versions of this mod.</summary>
    public static void SetLocked(ChartData data, bool locked)
    {
        if (data == null) return;
        data.locked = locked;
        var parameters = data.displayTypeParams;
        if (parameters != null && parameters.Length > kLockParamIndex)
            parameters[kLockParamIndex] = locked ? 1 : 0;
    }
}
