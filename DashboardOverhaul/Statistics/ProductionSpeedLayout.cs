using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DashboardOverhaul;

/// <summary>Actual/reference rates for the 1x1 and 2x1 production charts.</summary>
public static class ProductionSpeedLayout
{
    private const string kStatsUITweaks = "starfi5h.plugin.StatsUITweaks";
    private const int kStatsTweaksLayout = 5;
    public const int kProductionLayout = 6;
    private const int kConsumptionLayout = 7;
    private const int kCombinedLayout = 8;
    private static readonly Color kReferenceColor = new Color(0.72f, 0.74f, 0.76f, 1f);
    private const string kInlineUnit = "<color=#fff5><size=10>/min</size></color>";
    private static readonly double[] s_periodMinutes = { 1.0, 10.0, 60.0, 600.0, 6000.0 };

    /// <summary>Minutes in a statistic period; totals have no rate divisor.</summary>
    public static double PeriodDivisor(int timeLevel) =>
        timeLevel >= 0 && timeLevel < s_periodMinutes.Length ? s_periodMinutes[timeLevel] : 0.0;

    public static bool IsCustomLayout(int layout) => layout >= kProductionLayout && layout <= kCombinedLayout;

    /// <summary>Layouts offered by the production chart's menu, including StatsUITweaks' alias.</summary>
    public static bool SupportsLayout(int preset, int timeLevel, int layout)
    {
        if (preset != 0 && preset != 1) return false;
        if (layout >= 0 && layout <= 2) return true;
        if (layout == 3 || layout == 4) return preset == 1;
        if (PeriodDivisor(timeLevel) == 0.0) return false;
        return layout == kStatsTweaksLayout || layout == kProductionLayout || layout == kConsumptionLayout ||
            (layout == kCombinedLayout && preset == 1);
    }

    private static int NormalizeLayout(int preset, int timeLevel, int layout)
    {
        // Give the shared layout one renderer: StatsUITweaks' ShowInText postfix only handles ID 5.
        if (layout == kStatsTweaksLayout)
            layout = preset == 0 ? kProductionLayout : kCombinedLayout;
        return IsCustomLayout(layout) && !SupportsLayout(preset, timeLevel, layout) ? 0 : layout;
    }

    [HarmonyPostfix]
    [HarmonyAfter(kStatsUITweaks)]
    [HarmonyPatch(typeof(UIChartAstroItemProduction), nameof(UIChartAstroItemProduction.CreateLayoutMenu))]
    static void CreateLayoutMenu_Postfix(UIChartAstroItemProduction __instance, UIPopupMenu __result)
    {
        int preset = __instance.presetIndex;
        int level = __instance.statPlan.timeLevel;
        if (!SupportsLayout(preset, level, kProductionLayout)) return;
        __instance.chartData.displayTypeParams[0] = NormalizeLayout(preset, level, __instance.chartData.displayTypeParams[0]);

        AddLayout(__result, __instance, kProductionLayout, Loc.L("生产与参考速率", "Production & reference speed"));
        AddLayout(__result, __instance, kConsumptionLayout, Loc.L("消耗与参考速率", "Consumption & reference speed"));
        if (preset == 1)
            AddLayout(__result, __instance, kCombinedLayout, Loc.L("生产消耗与参考", "Production, consumption & reference"));
        __result.SetState(true);
    }

    private static void AddLayout(UIPopupMenu menu, UIChartAstroItemProduction chart, int layout, string label)
    {
        foreach (var button in menu.childButtons)
        {
            if (NormalizeLayout(chart.presetIndex, chart.statPlan.timeLevel, button.data) != layout) continue;
            button.data = layout;
            button.ButtonText = label;
            button.ButtonIcon = chart.chartData.displayTypeParams[0] == layout ? Configs.builtin.genericMenuButtonSprites[1] : null;
            button.SetState(true);
            return;
        }
        var entry = menu.AddMenuButton(label, chart.chartData.displayTypeParams[0] == layout ? 1 : -1, false);
        entry.data = layout;
        entry.onMenuButtonClick += chart.OnLayoutMenuButtonClick;
        entry.SetState(true);
    }

    /// <summary>Print thousands in full; keep the native small-rate precision and million-plus suffixes.</summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(UIChartAstroItemProduction), nameof(UIChartAstroItemProduction.ToLevelString))]
    static bool ToLevelString_Prefix(double value, ref string __result)
    {
        if (value < 10000.0 || value >= 1000000.0) return true;
        __result = ((long)value).ToString("#,##0");
        return false;
    }

    // The combined row needs the game's abbreviated format, without the chart-wide thousands override.
    [HarmonyReversePatch]
    [HarmonyPatch(typeof(UIChartAstroItemProduction), nameof(UIChartAstroItemProduction.ToLevelString))]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string AbbreviatedNumber(UIChartAstroItemProduction instance, double value, int level) =>
        throw new NotSupportedException("Harmony reverse patch has not been applied.");

    [HarmonyPrefix]
    [HarmonyBefore(kStatsUITweaks)]
    [HarmonyPatch(typeof(UIChartAstroItemProduction), nameof(UIChartAstroItemProduction.ShowInText))]
    static bool ShowInText_Prefix(UIChartAstroItemProduction __instance, int level)
    {
        int layout = NormalizeLayout(__instance.presetIndex, level, __instance.chartData.displayTypeParams[0]);
        __instance.chartData.displayTypeParams[0] = layout;
        if (!IsCustomLayout(layout))
        {
            // These tooltip components are also reused by the ordinary layouts.
            SetTipIdentity(__instance.productSpeedTip,
                (level == 5 ? "生产总量冒号" : "生产速率冒号").Translate(), __instance.productColor);
            SetTipIdentity(__instance.consumeSpeedTip,
                (level == 5 ? "消耗总量冒号" : "消耗速率冒号").Translate(), __instance.consumeColor);
            return true;
        }

        // ShowInText normally performs this factory traversal. Replacing that method for our layouts
        // avoids doing the same aggregation again after Refresh has already called it.
        var plan = __instance.statPlan;
        plan.CalculateProductionAndConsumption(out long production, out long consumption);
        plan.GetItemsCyclicRefSpeed(out float productReference, out float consumeReference);
        double divisor = PeriodDivisor(level);
        var rates = new Rates
        {
            Production = Math.Abs((double)production) / divisor,
            Consumption = Math.Abs((double)consumption) / divisor,
            ProductReference = productReference,
            ConsumeReference = consumeReference,
            ProductionColor = production > 0 ? __instance.productColor : __instance.zeroColor,
            ConsumptionColor = consumption > 0 ? __instance.consumeColor : __instance.zeroColor,
        };
        Draw(__instance, rates, layout, level);
        return false;
    }

    private struct Rates
    {
        public double Production, Consumption, ProductReference, ConsumeReference;
        public Color ProductionColor, ConsumptionColor;
    }

    private static void Draw(UIChartAstroItemProduction chart, Rates rates, int layout, int level)
    {
        bool combined = layout == kCombinedLayout;
        bool production = layout != kConsumptionLayout;
        double actual = production ? rates.Production : rates.Consumption;
        double reference = production ? rates.ProductReference : rates.ConsumeReference;
        Color actualColor = production ? rates.ProductionColor : rates.ConsumptionColor;
        string actualLabel = production ? Loc.L("生产速率", "Production") : Loc.L("消耗速率", "Consumption");
        string referenceLabel = Loc.L("参考速率", "Reference");

        chart.displayGo0.SetActive(true);
        chart.displayGo1.SetActive(false);
        if (combined)
        {
            SetText(chart.productText, Combine(chart, rates.Production, rates.ProductReference, level));
            SetText(chart.consumeText, Combine(chart, rates.Consumption, rates.ConsumeReference, level));
            chart.productText.color = rates.ProductionColor;
            chart.consumeText.color = rates.ConsumptionColor;
            SetTip(chart.productSpeedTip, Loc.L("生产/参考速率：", "Production / reference:"),
                TipRate(rates.Production) + " / " + TipRate(rates.ProductReference), rates.ProductionColor);
            SetTip(chart.consumeSpeedTip, Loc.L("消耗/参考速率：", "Consumption / reference:"),
                TipRate(rates.Consumption) + " / " + TipRate(rates.ConsumeReference), rates.ConsumptionColor);
        }
        else
        {
            string unit = chart.presetIndex == 0 ? kInlineUnit : string.Empty;
            SetText(chart.productText, chart.ToLevelString(actual, level).TrimStart() + unit);
            SetText(chart.consumeText, chart.ToLevelString(reference, level).TrimStart() + unit);
            chart.productText.color = actualColor;
            chart.consumeText.color = kReferenceColor;
            SetTip(chart.productSpeedTip, actualLabel + ":", TipRate(actual), actualColor);
            SetTip(chart.consumeSpeedTip, referenceLabel + ":", TipRate(reference), kReferenceColor);
        }

        if (chart.presetIndex == 0)
        {
            chart.itemGroup.anchoredPosition = new Vector2(0f, 11f);
            return;
        }
        chart.productText.fontSize = 18;
        chart.consumeText.fontSize = 18;
        chart.productUnitLabel.text = "/ min";
        chart.consumeUnitLabel.text = "/ min";
        chart.productLabel.text = combined ? Loc.L("生产/参考", "Prod/Ref") : actualLabel;
        chart.consumeLabel.text = combined ? Loc.L("消耗/参考", "Use/Ref") : referenceLabel;
        chart.productText.rectTransform.anchoredPosition = new Vector2(-26f, 0f);
        chart.consumeText.rectTransform.anchoredPosition = new Vector2(-26f, 0f);
    }

    private static string Combine(UIChartAstroItemProduction chart, double actual, double reference, int level) =>
        AbbreviatedNumber(chart, actual, level).TrimStart() + " <color=#fff5>/</color> <color=#B8BDC2>" +
        AbbreviatedNumber(chart, reference, level).TrimStart() + "</color>";

    private static string TipRate(double rate) => rate.ToString("#,##0.##") + " / min";

    private static void SetTipIdentity(UIProductSimpleTipTrigger tip, string label, Color color)
    {
        if (tip == null) return;
        tip.label = label;
        tip.tipTextColor = color;
    }

    private static void SetTip(UIProductSimpleTipTrigger tip, string label, string description, Color color)
    {
        if (tip == null) return;
        SetTipIdentity(tip, label, color);
        tip.description = description;
    }

    private static void SetText(Text text, string value)
    {
        text.supportRichText = true;
        text.text = value;
    }
}
