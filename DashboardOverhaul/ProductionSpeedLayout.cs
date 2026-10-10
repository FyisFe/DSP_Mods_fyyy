using System;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DashboardOverhaul;

/// <summary>
/// The production chart's "speed + theoretical speed" layouts for the 2x1 preset:
/// <list type="bullet">
/// <item>production + theoretical speed - the production row plus a theoretical row,</item>
/// <item>consumption + theoretical speed - the consumption row plus a theoretical row,</item>
/// <item>production, consumption + theoretical speed - both rows as "actual / theoretical".</item>
/// </list>
/// The two-row layouts print full values ("1,550,050"); the four-value layout keeps the game's K/M/G
/// abbreviation. Theoretical values are grey, production keeps the chart's blue, consumption its orange.
///
/// StatsUITweaks offers its own "生产消耗与理论" layout (value 5, abbreviations). That entry is reused
/// rather than duplicated - <see cref="Refresh_Postfix"/> renders value 5 the same way as this mod's own
/// combined layout - and this mod's own entry (value 8) is only added when StatsUITweaks is not loaded.
/// Values 6 and 7 are this mod's own.
/// </summary>
public static class ProductionSpeedLayout
{
    /// <summary>Combined layout as offered by StatsUITweaks (rendered like <see cref="kOwnCombinedLayout"/>).</summary>
    private const int kStatsTweaksCombinedLayout = 5;
    /// <summary>This mod's production + theoretical layout (also used by the production alarm).</summary>
    public const int kProductionLayout = 6;

    /// <summary>This mod's consumption + theoretical layout.</summary>
    private const int kConsumptionLayout = 7;

    /// <summary>This mod's combined layout, used when no StatsUITweaks entry exists.</summary>
    private const int kOwnCombinedLayout = 8;

    /// <summary>Theoretical values are grey; production and consumption keep the chart's own colors.</summary>
    private static readonly Color kTheoryColor = new Color(0.72f, 0.74f, 0.76f, 1f);
    private const string kTheoryRich = "<color=#B8BDC2>";

    /// <summary>Unit markup the 1x1 prefab uses inside its value texts.</summary>
    private const string kInlineUnit = "<color=#fff5><size=10>/min</size></color>";

    /// <summary>Production counts are divided by the statistic period before being shown per minute.</summary>
    private static readonly double[] s_levelDivisors = { 1.0, 10.0, 60.0, 600.0, 6000.0 };

    /// <summary>The per-minute divisor of a statistic period, shared with the production alarm; 0 when the
    /// period is unknown.</summary>
    public static double PeriodDivisor(int timeLevel) =>
        timeLevel >= 0 && timeLevel < s_levelDivisors.Length ? s_levelDivisors[timeLevel] : 0.0;

    private static bool StatsUITweaksLoaded =>
        Chainloader.PluginInfos != null && Chainloader.PluginInfos.ContainsKey("starfi5h.plugin.StatsUITweaks");

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIChartAstroItemProduction), nameof(UIChartAstroItemProduction.CreateLayoutMenu))]
    static void CreateLayoutMenu_Postfix(UIChartAstroItemProduction __instance, UIPopupMenu __result)
    {
        if (__result == null || __instance.chartData == null) return;
        // Offered for the 2x1 and 1x1 sizes; the larger sizes keep the game's own layout list.
        if (__instance.presetIndex != 0 && __instance.presetIndex != 1) return;
        if (__instance.statPlan == null || __instance.statPlan.timeLevel == 5) return; // totals have no speed

        AddLayout(__result, __instance, kProductionLayout,
            Loc.L("生产与参考速率", "Production & reference speed"));
        AddLayout(__result, __instance, kConsumptionLayout,
            Loc.L("消耗与参考速率", "Consumption & reference speed"));

        // The four-value layout needs the room of the 2x1 row.
        if (__instance.presetIndex != 1) return;
        string combinedLabel = Loc.L("生产消耗与参考", "Production, consumption & reference");
        if (StatsUITweaksLoaded)
            RenameStatsTweaksEntry(__result, combinedLabel); // its layout is drawn by this mod
        else if (!HasLayout(__result, kStatsTweaksCombinedLayout))
            AddLayout(__result, __instance, kOwnCombinedLayout, combinedLabel);
    }

    /// <summary>StatsUITweaks offers the combined layout with its own wording and abbreviations; this mod
    /// draws it (see <see cref="Refresh_Postfix"/>), so its label is aligned with the other layouts.</summary>
    private static void RenameStatsTweaksEntry(UIPopupMenu menu, string label)
    {
        foreach (var button in menu.childButtons)
            if (button.data == kStatsTweaksCombinedLayout && button.ButtonText != label)
            {
                button.ButtonText = label;
            }
    }

    private static bool HasLayout(UIPopupMenu menu, int layout)
    {
        foreach (var button in menu.childButtons)
            if (button.data == layout) return true;
        return false;
    }

    private static void AddLayout(UIPopupMenu menu, UIChartAstroItemProduction chart, int layout, string label)
    {
        if (HasLayout(menu, layout)) return;
        var entry = menu.AddMenuButton(label, chart.chartData.displayTypeParams[0] == layout ? 1 : -1, false);
        entry.data = layout;
        entry.onMenuButtonClick = (Action<int>)Delegate.Combine(entry.onMenuButtonClick,
            new Action<int>(chart.OnLayoutMenuButtonClick));
        entry.SetState(true);
        menu.SetState(true);
    }

    /// <summary>
    /// The chart's number format, overridden chart-wide so every dashboard size follows the requested
    /// convention: a full number below one million and the game's abbreviation only from one million up.
    /// "k" is not an abbreviation, so thousands are printed in full (the game's own writer uses "k" from
    /// 10,000 and the chart's 1x1 layouts showed "21.6 k" because of it).
    /// Below 10,000 the chart's own formatting is already exact and is left alone, so the decimals of a
    /// small rate are preserved.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(UIChartAstroItemProduction), nameof(UIChartAstroItemProduction.ToLevelString))]
    static bool ToLevelString_Prefix(double value, ref string __result)
    {
        if (value >= 1000000.0)
        {
            StringBuilderUtility.WriteKMG(s_numberBuffer, 8, (long)(value + 0.5), blank: true);
            __result = s_numberBuffer.ToString();
            return false;
        }
        if (value >= 10000.0)
        {
            __result = ((long)value).ToString("#,##0");
            return false;
        }
        return true; // below 10,000 the chart's own formatting is already the full value
    }

    /// <summary>The game's own number formatting (the chart's original ToLevelString), kept for the
    /// four-value layout - the one case that keeps the game's abbreviations.</summary>
    private static string AbbreviatedNumber(double value, int level)
    {
        if (value >= 10000.0 || level == 5 || level == 0)
        {
            long num = (long)value;
            StringBuilderUtility.WriteKMG(s_numberBuffer, 8, num, blank: true);
            if (num <= 0) return num.ToString();
            if (num < 10000) return s_numberBuffer.ToString().Substring(0, s_numberBuffer.Length - 1);
            return s_numberBuffer.ToString();
        }
        if (value >= 1000.0) return value.ToString("F0");
        if (value >= 100.0 && level != 1) return value.ToString("F1");
        if (value > 0.0) return level == 1 ? value.ToString("F1") : value.ToString("F2");
        return value.ToString();
    }

    private static readonly System.Text.StringBuilder s_numberBuffer = new System.Text.StringBuilder("         ", 12);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIChartAstroItemProduction), nameof(UIChartAstroItemProduction.Refresh))]
    static void Refresh_Postfix(UIChartAstroItemProduction __instance)
    {
        if (__instance.chartData == null) return;
        int layout = __instance.chartData.displayTypeParams[0];
        if (layout != kStatsTweaksCombinedLayout && layout != kProductionLayout &&
            layout != kConsumptionLayout && layout != kOwnCombinedLayout) return;
        var plan = __instance.statPlan;
        if (plan == null || plan.timeLevel < 0 || plan.timeLevel >= s_levelDivisors.Length) return;

        plan.CalculateProductionAndConsumption(out long production, out long consumption);
        plan.GetItemsCyclicRefSpeed(out float productRefSpeed, out float consumeRefSpeed);
        double divisor = s_levelDivisors[plan.timeLevel];
        var speed = new Speeds
        {
            Production = production / divisor,
            Consumption = consumption / divisor,
            ProductTheory = productRefSpeed,
            ConsumeTheory = consumeRefSpeed,
            ProductionColor = production > 0 ? __instance.productColor : __instance.zeroColor,
            ConsumptionColor = consumption > 0 ? __instance.consumeColor : __instance.zeroColor,
        };

        if (__instance.presetIndex == 1)
        {
            if (layout == kProductionLayout) DrawTheoryRow(__instance, speed, true, plan.timeLevel);
            else if (layout == kConsumptionLayout) DrawTheoryRow(__instance, speed, false, plan.timeLevel);
            else DrawCombined(__instance, speed, plan.timeLevel);
        }
        else if (__instance.presetIndex == 0)
        {
            // The 1x1 row has room for one actual value and the reference value: value 5 (StatsUITweaks'
            // own layout) means the same here, so it is drawn with the same code.
            DrawSingleLine(__instance, speed, layout != kConsumptionLayout, plan.timeLevel);
        }
        else
            return; // other sizes do not offer these layouts
    }

    private struct Speeds
    {
        public double Production, Consumption, ProductTheory, ConsumeTheory;
        public Color ProductionColor, ConsumptionColor;
    }

    /// <summary>2x1 with one actual row and the reference value on the second row. The numbers follow the
    /// chart-wide convention (<see cref="ToLevelString_Prefix"/>): full below one million, abbreviated
    /// above.</summary>
    private static void DrawTheoryRow(UIChartAstroItemProduction chart, Speeds speed, bool production, int level)
    {
        chart.displayGo0.SetActive(true);
        chart.displayGo1.SetActive(false);
        SetText(chart.productText, chart.ToLevelString(production ? speed.Production : speed.Consumption, level).TrimStart());
        SetText(chart.consumeText, chart.ToLevelString(production ? speed.ProductTheory : speed.ConsumeTheory, level).TrimStart());
        chart.productText.color = production ? speed.ProductionColor : speed.ConsumptionColor;
        chart.consumeText.color = kTheoryColor;
        chart.productText.fontSize = 18;
        chart.consumeText.fontSize = 18;
        chart.productUnitLabel.text = "/ min";
        chart.consumeUnitLabel.text = "/ min";
        chart.productLabel.text = production ? Loc.L("生产速率", "Production") : Loc.L("消耗速率", "Consumption");
        chart.consumeLabel.text = Loc.L("参考速率", "Reference");
        chart.productText.rectTransform.anchoredPosition = new Vector2(-26f, 0f);
        chart.consumeText.rectTransform.anchoredPosition = new Vector2(-26f, 0f);
    }

    /// <summary>2x1 with both rows as "actual / reference": the four-value layout is the one case that
    /// keeps the game's abbreviations (<see cref="AbbreviatedNumber"/>); the reference part is grey.</summary>
    private static void DrawCombined(UIChartAstroItemProduction chart, Speeds speed, int level)
    {
        chart.displayGo0.SetActive(true);
        chart.displayGo1.SetActive(false);
        SetText(chart.productText, Combine(AbbreviatedNumber(speed.Production, level).TrimStart(),
            AbbreviatedNumber(speed.ProductTheory, level).TrimStart()));
        SetText(chart.consumeText, Combine(AbbreviatedNumber(speed.Consumption, level).TrimStart(),
            AbbreviatedNumber(speed.ConsumeTheory, level).TrimStart()));
        chart.productText.color = speed.ProductionColor;
        chart.consumeText.color = speed.ConsumptionColor;
        chart.productText.fontSize = 18;
        chart.consumeText.fontSize = 18;
        chart.productUnitLabel.text = "/ min";
        chart.consumeUnitLabel.text = "/ min";
        chart.productLabel.text = Loc.L("生产/参考", "Prod/Ref");
        chart.consumeLabel.text = Loc.L("消耗/参考", "Use/Ref");
        chart.productText.rectTransform.anchoredPosition = new Vector2(-26f, 0f);
        chart.consumeText.rectTransform.anchoredPosition = new Vector2(-26f, 0f);
    }

    /// <summary>
    /// The 1x1 chart keeps its own look (the unit sits inside the value text); its numbers follow the same
    /// chart-wide convention as every other size.
    /// </summary>
    private static void DrawSingleLine(UIChartAstroItemProduction chart, Speeds speed, bool production, int level)
    {
        chart.displayGo0.SetActive(true);
        chart.displayGo1.SetActive(false);
        SetText(chart.productText,
            chart.ToLevelString(production ? speed.Production : speed.Consumption, level).TrimStart() + kInlineUnit);
        SetText(chart.consumeText,
            chart.ToLevelString(production ? speed.ProductTheory : speed.ConsumeTheory, level).TrimStart() + kInlineUnit);
        chart.productText.color = production ? speed.ProductionColor : speed.ConsumptionColor;
        chart.consumeText.color = kTheoryColor;
    }

    /// <summary>"actual / theoretical" with the theoretical part in grey rich text (the object's own
    /// color paints the actual part).</summary>
    private static string Combine(string actual, string theory) =>
        actual + " <color=#fff5>/</color> " + kTheoryRich + theory + "</color>";

    private static void SetText(Text text, string value)
    {
        if (text == null || text.text == value) return;
        text.supportRichText = true;
        text.text = value;
    }
}
