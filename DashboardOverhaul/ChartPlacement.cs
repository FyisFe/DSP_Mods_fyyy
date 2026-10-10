using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DashboardOverhaul;

/// <summary>
/// Places a newly added statistic at the bottom-right of the current page instead of the game's centred
/// auto position (<see cref="ModConfig.PlaceNewChartsAtBottomRight"/>, on by default). This covers every
/// way a chart can be added - the sidebar's add button, the statistics window's function menu and this
/// mod's shift-click shortcut - because they all use the game's auto-position entry point.
///
/// The game still runs its own search first, so its "chart limit reached" message and its fallback when
/// the page is full are unchanged; only the resulting position is replaced when a free spot exists.
/// </summary>
public static class ChartPlacement
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(DashboardPage), nameof(DashboardPage.AddChartWithAutoPosition))]
    static void AddChartWithAutoPosition_Postfix(DashboardPage __instance, ChartData __result,
        Vector2Int gridBoundMin, Vector2Int gridBoundMax)
    {
        var config = ModConfig.PlaceNewChartsAtBottomRight;
        if (config == null || !config.Value) return;
        if (__result == null || __instance.chartDatas == null) return;

        Vector2Int? spot = FindBottomRightSpot(__instance.chartDatas, __result, gridBoundMin, gridBoundMax);
        if (spot.HasValue && spot.Value != __result.pos) __result.pos = spot.Value;
    }

    /// <summary>First free position scanning from the bottom-right corner: the bottom row fills from
    /// right to left, then the row above it, using each chart's own size.</summary>
    private static Vector2Int? FindBottomRightSpot(List<ChartData> charts, ChartData added,
        Vector2Int min, Vector2Int max)
    {
        Vector2Int size = added.size;
        for (int y = max.y - size.y; y >= min.y; y--)
            for (int x = max.x - size.x; x >= min.x; x--)
            {
                var pos = new Vector2Int(x, y);
                if (IsFree(charts, added, pos, size)) return pos;
            }
        return null;
    }

    private static bool IsFree(List<ChartData> charts, ChartData added, Vector2Int pos, Vector2Int size)
    {
        foreach (var other in charts)
        {
            if (other == null || other == added) continue;
            if (pos.x + size.x > other.pos.x && other.pos.x + other.size.x > pos.x &&
                pos.y + size.y > other.pos.y && other.pos.y + other.size.y > pos.y) return false;
        }
        return true;
    }
}
