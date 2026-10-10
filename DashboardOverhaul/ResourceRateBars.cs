using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace DashboardOverhaul;

/// <summary>
/// Gives the rate resources (crude oil and the gases) the same row as the solid veins: a bar with its
/// count printed after it - the number of oil wells for crude oil, the number of planets for hydrogen,
/// deuterium and the fire ice of ice giants.
///
/// The chart keeps that count in <c>AstroResourceStatPlan.StatData.count</c>, but only builds bar rows
/// for solid veins (<c>UIChartAstroResourceBarEntry</c>); its oil and gas rows are plain list rows that
/// show just the rate. This adds bar rows for them from the chart's own bar prefab, aligned with the
/// list rows, and draws them in the fire-ice vein colour.
///
/// Bar lengths are scaled against the largest count among those rows (the chart's own scale comes from
/// the solid veins and would leave every rate bar one pixel wide).
/// </summary>
public static class ResourceRateBars
{
    private static readonly ConditionalWeakTable<UIChartAstroResource, List<UIChartAstroResourceBarEntry>> s_bars = new();

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIChartAstroResource), "Refresh")]
    static void ChartRefresh_Postfix(UIChartAstroResource __instance)
    {
        if (__instance.presetIndex != 0 && __instance.presetIndex != 2) return; // list layouts only
        var prefab = __instance.barEntryPrefab;
        if (prefab == null || prefab.rectTrans == null) return;
        var rows = __instance.genericEntries;
        if (rows == null) return;

        // The list rows already carry the row index the bar has to line up with.
        var indexes = new List<int>();
        var stats = new List<AstroResourceStatPlan.StatData>();
        foreach (var row in rows)
        {
            if (row == null || !row.gameObject.activeSelf) continue;
            var stat = row.statData;
            if (!IsRateResource(stat)) continue;
            indexes.Add(row.index);
            stats.Add(stat);
        }

        // Bar lengths must be comparable with the solid vein bars above them, so they use the chart's own
        // reference: the largest count among the solid rows (AstroResourceStatPlan.statDatas is sorted by
        // type, and the game takes its maximum from the rows before Oil exactly like this).
        int maxCount = SolidMaxCount();

        var bars = s_bars.GetOrCreateValue(__instance);
        for (int i = indexes.Count; i < bars.Count; i++)
            if (bars[i] != null) bars[i].gameObject.SetActive(false);

        for (int i = 0; i < indexes.Count; i++)
        {
            while (bars.Count <= i) bars.Add(null);
            if (bars[i] == null) bars[i] = CreateBar(prefab, maxCount);
            var bar = bars[i];
            bar.index = indexes[i];
            bar.statData = stats[i];
            bar.maxCount = maxCount;
            bar.SetTrans();
            if (!bar.gameObject.activeSelf) bar.gameObject.SetActive(true);
        }
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
        var datas = AstroResourceStatPlan.statDatas;
        if (datas == null) return max;
        foreach (var stat in datas)
            if (stat.type < AstroResourceStatPlan.EAstroResourceType.Oil && stat.count > max) max = stat.count;
        return max;
    }

    private static UIChartAstroResourceBarEntry CreateBar(UIChartAstroResourceBarEntry prefab, int maxCount)
    {
        var bar = Object.Instantiate(prefab, prefab.rectTrans.parent);
        bar.gameObject.name = "DO_ResourceRateBar";
        return bar;
    }

    /// <summary>
    /// Bar colour per resource: crude oil uses the 钛石 (titanium) vein colour, hydrogen the 刺笋结晶
    /// (spiniform stalagmite crystal) colour, and deuterium and the other gases keep the colours they had
    /// (the 硅 vein colour and the fire-ice vein colour).
    /// The colour array is indexed by <c>VeinProto.ID</c>, so veins are resolved through the item they
    /// mine instead of guessing an index from the vein type enum.
    /// </summary>
    private static Color BarColor(UIChartAstroResource chart, AstroResourceStatPlan.StatData stat)
    {
        if (stat.type == AstroResourceStatPlan.EAstroResourceType.Oil)
            return VeinColor(chart, TitaniumVeinId);
        if (stat.type == AstroResourceStatPlan.EAstroResourceType.Gas && stat.protoId == HydrogenItemId)
            return VeinColor(chart, SpiniformVeinId);
        if (stat.type == AstroResourceStatPlan.EAstroResourceType.Gas && stat.protoId == DeuteriumItemId)
            return VeinColor(chart, (int)EVeinType.Silicium);
        return VeinColor(chart, (int)EVeinType.Fireice);
    }

    /// <summary>Colour the chart uses for a vein resource; the id is a <c>VeinProto.ID</c>.</summary>
    private static Color VeinColor(UIChartAstroResource chart, int veinId)
    {
        var colors = chart != null ? chart.barColors : null;
        if (colors != null && veinId >= 0 && veinId < colors.Length) return colors[veinId];
        return new Color(0.45f, 0.85f, 0.9f, 1f);
    }

    /// <summary>Item id of the prototype with this localisation key (the keys are language independent).</summary>
    private static int ItemId(string key)
    {
        var items = LDB.items != null ? LDB.items.dataArray : null;
        if (items == null) return 0;
        foreach (var item in items)
            if (item != null && item.Name == key) return item.ID;
        return 0;
    }

    /// <summary>Vein whose mining item is the item with this key; the colour array is indexed by that id.</summary>
    private static int VeinIdMining(string itemKey)
    {
        int itemId = ItemId(itemKey);
        var veins = LDB.veins != null ? LDB.veins.dataArray : null;
        if (itemId == 0 || veins == null) return 0;
        foreach (var vein in veins)
            if (vein != null && vein.MiningItem == itemId) return vein.ID;
        return 0;
    }

    private static int HydrogenItemId
    {
        get
        {
            if (s_hydrogenItemId == 0) s_hydrogenItemId = ItemId("氢");
            return s_hydrogenItemId;
        }
    }

    private static int DeuteriumItemId
    {
        get
        {
            if (s_deuteriumItemId == 0) s_deuteriumItemId = ItemId("重氢");
            return s_deuteriumItemId;
        }
    }

    private static int SpiniformVeinId
    {
        get
        {
            if (s_spiniformVeinId == 0) s_spiniformVeinId = VeinIdMining("刺笋结晶");
            return s_spiniformVeinId;
        }
    }

    private static int TitaniumVeinId
    {
        get
        {
            if (s_titaniumVeinId == 0) s_titaniumVeinId = VeinIdMining("钛石");
            return s_titaniumVeinId;
        }
    }

    private static int s_hydrogenItemId;
    private static int s_deuteriumItemId;
    private static int s_spiniformVeinId;
    private static int s_titaniumVeinId;
}
