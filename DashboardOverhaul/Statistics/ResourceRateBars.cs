using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace DashboardOverhaul;

/// <summary>
/// Adds oil-well and gas-planet counts in the resource chart's bar view, using the solid veins' scale.
/// </summary>
public static class ResourceRateBars
{
    private const int kHydrogenItem = 1120;
    private const int kDeuteriumItem = 1121;
    private static readonly ConditionalWeakTable<UIChartAstroResource, List<UIChartAstroResourceBarEntry>> s_bars = new();

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIChartAstroResource), "Refresh")]
    static void ChartRefresh_Postfix(UIChartAstroResource __instance)
    {
        if (__instance.presetIndex != 2 || !__instance.resourceGroupGo.activeSelf ||
            !__instance.barGroupGo.activeSelf) return;
        var prefab = __instance.barEntryPrefab;
        var rows = __instance.genericEntries;

        int maxCount = SolidMaxCount();
        var bars = s_bars.GetOrCreateValue(__instance);
        int used = 0;
        foreach (var row in rows)
        {
            if (!row.gameObject.activeSelf) continue;
            var stat = row.statData;
            if (!IsRateResource(stat)) continue;
            if (used == bars.Count) bars.Add(CreateBar(prefab));
            var bar = bars[used++];
            bar.index = row.index;
            bar.statData = stat;
            bar.maxCount = maxCount;
            bar.SetTrans();
            if (!bar.gameObject.activeSelf) bar.gameObject.SetActive(true);
        }
        for (int i = used; i < bars.Count; i++)
            bars[i].gameObject.SetActive(false);
    }

    /// <summary>The chart's own bar rows handle solid veins; the added ones handle oil and gases.</summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(UIChartAstroResourceBarEntry), "Refresh")]
    static bool BarEntryRefresh_Prefix(UIChartAstroResourceBarEntry __instance)
    {
        var stat = __instance.statData;
        if (!IsRateResource(stat)) return true; // the game's own solid vein rows

        __instance.barTrans.sizeDelta = new Vector2(__instance.get_width(stat.count, __instance.maxCount),
            __instance.barTrans.sizeDelta.y);
        __instance.countText.text = stat.count.ToString();
        __instance.barImage.color = BarColor(__instance.parentChart, stat);
        return false; // the game's version indexes its vein colours with an item id
    }

    private static bool IsRateResource(AstroResourceStatPlan.StatData stat) =>
        stat.type == AstroResourceStatPlan.EAstroResourceType.Oil ||
        stat.type == AstroResourceStatPlan.EAstroResourceType.Gas;

    /// <summary>The chart's own bar scale: the largest count among the solid vein rows.</summary>
    private static int SolidMaxCount()
    {
        int max = 0;
        foreach (var stat in AstroResourceStatPlan.statDatas)
            if (stat.type < AstroResourceStatPlan.EAstroResourceType.Oil && stat.count > max) max = stat.count;
        return max;
    }

    private static UIChartAstroResourceBarEntry CreateBar(UIChartAstroResourceBarEntry prefab)
    {
        var bar = Object.Instantiate(prefab, prefab.rectTrans.parent);
        bar.gameObject.name = "DO_ResourceRateBar";
        return bar;
    }

    /// <summary>
    /// Oil uses titanium's colour; hydrogen uses spiniform, deuterium silicon, and fire ice its own colour.
    /// Gas proto IDs identify items, while the chart's colour array is indexed by vein IDs.
    /// </summary>
    private static Color BarColor(UIChartAstroResource chart, AstroResourceStatPlan.StatData stat)
    {
        if (stat.type == AstroResourceStatPlan.EAstroResourceType.Oil)
            return chart.barColors[(int)EVeinType.Titanium];
        if (stat.protoId == kHydrogenItem)
            return chart.barColors[(int)EVeinType.Bamboo];
        if (stat.protoId == kDeuteriumItem)
            return chart.barColors[(int)EVeinType.Silicium];
        return chart.barColors[(int)EVeinType.Fireice];
    }
}
