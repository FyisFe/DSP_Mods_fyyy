using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using DashboardOverhaul;
using HarmonyLib;
using static Checks;

internal static class ProductionChecks
{
    internal static void Run()
    {
        CheckLayouts();
        CheckAlarm();
        CheckNumberFormats();
        Console.WriteLine("Production checks passed: layout eligibility/recovery, rate periods, alarm boundaries and native number formatting.");
    }

    private static void CheckLayouts()
    {
        foreach (int preset in new[] { 0, 1 })
        {
            foreach (int level in new[] { 0, 4 })
            {
                Require(ProductionSpeedLayout.SupportsLayout(preset, level, 6), "production/reference layout is available");
                Require(ProductionSpeedLayout.SupportsLayout(preset, level, 7), "consumption/reference layout is available");
                Require(ProductionSpeedLayout.SupportsLayout(preset, level, 5), "StatsUITweaks layout remains readable");
            }
            foreach (int layout in new[] { 5, 6, 7, 8 })
                Require(!ProductionSpeedLayout.SupportsLayout(preset, 5, layout), "totals cannot select a rate/reference layout");
            Require(ProductionSpeedLayout.SupportsLayout(preset, 5, 0), "totals retain the native layout");
        }
        Require(ProductionSpeedLayout.SupportsLayout(1, 0, 8) && !ProductionSpeedLayout.SupportsLayout(0, 0, 8),
            "the combined layout needs the 2x1 preset");
        Require(!ProductionSpeedLayout.SupportsLayout(0, 0, 3) && ProductionSpeedLayout.SupportsLayout(1, 0, 3),
            "the storage layout needs the 2x1 preset");
        Require(!ProductionSpeedLayout.SupportsLayout(2, 0, 6), "larger presets retain their native layout");

        var normalize = (Func<int, int, int, int>)Delegate.CreateDelegate(typeof(Func<int, int, int, int>),
            AccessTools.Method(typeof(ProductionSpeedLayout), "NormalizeLayout"));
        Require(normalize(0, 0, 5) == 6 && normalize(1, 0, 5) == 8, "StatsUITweaks aliases keep their preset-specific meaning");
        foreach (int layout in new[] { 5, 6, 7, 8 })
            Require(normalize(1, 5, layout) == 0, "saved rate layouts recover when attached to totals");
        Require(normalize(0, 0, 8) == 0 && normalize(2, 0, 6) == 0, "unsupported saved sizes recover to a native layout");
        Require(normalize(1, 0, 4) == 4 && normalize(1, 0, 9) == 9, "normalization preserves unrelated layouts");

        Require(ProductionSpeedLayout.PeriodDivisor(0) == 1 && ProductionSpeedLayout.PeriodDivisor(1) == 10 &&
            ProductionSpeedLayout.PeriodDivisor(2) == 60 && ProductionSpeedLayout.PeriodDivisor(3) == 10 * 60 &&
            ProductionSpeedLayout.PeriodDivisor(4) == 100 * 60, "statistic periods convert counts to per-minute rates");
        Require(ProductionSpeedLayout.PeriodDivisor(-1) == 0 && ProductionSpeedLayout.PeriodDivisor(5) == 0,
            "unknown periods and totals do not supply a rate divisor");
    }

    private static void CheckAlarm()
    {
        var classify = AccessTools.Method(typeof(ProductionAlarm), "Classify");
        string Status(double actual, double reference) => classify.Invoke(null, new object[] { actual, reference }).ToString();
        Require(Status(989.9, 1000) == "Alarm", "rates below 99 percent are red");
        Require(Status(990, 1000) == "Warning" && Status(998.9, 1000) == "Warning", "99 percent through below 99.9 percent are yellow");
        Require(Status(999, 1000) == "OK" && Status(1001, 1000) == "OK", "99.9 percent and higher meet the alarm threshold");
        Require(Status(0, 0) == "OK", "a zero reference has no production shortfall");
    }

    private static void CheckNumberFormats()
    {
        // This native formatter only reads its StringBuilder. No Unity object or UI lifecycle runs here.
        var chart = (UIChartAstroItemProduction)FormatterServices.GetUninitializedObject(typeof(UIChartAstroItemProduction));
        AccessTools.Field(typeof(UIChartAstroItemProduction), "sb1").SetValue(chart, new StringBuilder("         ", 12));
        MethodInfo native = AccessTools.Method(typeof(UIChartAstroItemProduction), nameof(UIChartAstroItemProduction.ToLevelString));
        MethodInfo abbreviated = AccessTools.Method(typeof(ProductionSpeedLayout), "AbbreviatedNumber");
        var harmony = new Harmony("org.fyyy.dashboardoverhaul.tests.production");
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var samples = new List<(double value, int level, string vanilla)>();
            foreach (int level in new[] { 0, 1, 2 })
                foreach (double value in new[] { 0.0, 0.125, 99.95, 999.95, 9999.9, 10000.0, 999999.0, 1000000.6 })
                    samples.Add((value, level, (string)native.Invoke(chart, new object[] { value, level })));

            harmony.CreateReversePatcher(native, new HarmonyMethod(abbreviated)).Patch(HarmonyReversePatchType.Original);
            harmony.Patch(native, prefix: new HarmonyMethod(AccessTools.Method(typeof(ProductionSpeedLayout), "ToLevelString_Prefix")));
            foreach (var sample in samples)
            {
                string combined = (string)abbreviated.Invoke(null, new object[] { chart, sample.value, sample.level });
                Require(combined == sample.vanilla, "combined values preserve native abbreviations and period precision");
                if (sample.value < 10000.0 || sample.value >= 1000000.0)
                    Require((string)native.Invoke(chart, new object[] { sample.value, sample.level }) == sample.vanilla,
                        "ordinary values retain native formatting outside the full-thousands range");
            }
            Require((string)native.Invoke(chart, new object[] { 10000.0, 0 }) == "10,000" &&
                (string)native.Invoke(chart, new object[] { 999999.0, 2 }) == "999,999", "ordinary charts print thousands in full");
        }
        finally
        {
            harmony.UnpatchSelf();
            CultureInfo.CurrentCulture = previousCulture;
        }
    }
}
