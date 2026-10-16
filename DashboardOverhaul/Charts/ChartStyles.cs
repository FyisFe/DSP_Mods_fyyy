using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DashboardOverhaul;

public static class ChartStyles
{
    // IDs are persisted independently of the native style table's length and order.
    private static readonly (string Name, EChartStyleType Type, Color Color)[] Colors =
    {
        // Light tints let the native blue surface and grid show through without washing out the figures.
        ("绿色半透明", EChartStyleType.Background, new Color32(74, 174, 147, 72)),
        ("黄色半透明", EChartStyleType.Background, new Color32(229, 184, 100, 72)),
        ("红色半透明", EChartStyleType.Background, new Color32(223, 134, 151, 72)),
        ("绿色边框", EChartStyleType.Border, new Color32(85, 167, 145, 255)),
        ("黄色边框", EChartStyleType.Border, new Color32(213, 169, 91, 255)),
        ("红色边框", EChartStyleType.Border, new Color32(202, 118, 128, 255)),
    };

    private static ChartStyle[] _registered;
    private static readonly int[] Indices = new int[Colors.Length];
    public static int BlueBorderIndex { get; private set; }
    public static int BlackBackgroundIndex { get; private set; }
    public static int GreenBackgroundIndex => Indices[0];
    public static int YellowBackgroundIndex => Indices[1];
    public static int RedBackgroundIndex => Indices[2];

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ChartPresetsDB), nameof(ChartPresetsDB.Load))]
    static void Load_Postfix() => Initialize();

    public static void Initialize()
    {
        var styles = ChartPresetsDB.GetChartStyles();
        if (styles == null || ReferenceEquals(styles, _registered)) return;

        // The native background is named "半透明" and the border is blue. Resolve by kind,
        // not translated display text; their exact selectors also delimit what we recolor.
        BlackBackgroundIndex = FindTemplate(styles, EChartStyleType.Background, "bg");
        BlueBorderIndex = FindTemplate(styles, EChartStyleType.Border, "border");
        if (BlackBackgroundIndex < 0 || BlueBorderIndex < 0)
        {
            for (int i = 0; i < Indices.Length; i++) Indices[i] = -1;
            DashboardOverhaulPlugin.Logger.LogWarning("Chart background/border templates are unavailable; custom colors disabled.");
            return;
        }

        var extended = new List<ChartStyle>(styles);
        for (int i = 0; i < Colors.Length; i++)
        {
            var spec = Colors[i];
            int existing = extended.FindIndex(style => style.name == spec.Name && style.type == spec.Type);
            if (existing >= 0) { Indices[i] = existing; continue; }
            int template = spec.Type == EChartStyleType.Background ? BlackBackgroundIndex : BlueBorderIndex;
            var style = new ChartStyle(styles[template]) { name = spec.Name };
            string selectorName = spec.Type == EChartStyleType.Background ? "bg" : "border";
            foreach (var selector in style.selectors)
                if (selector.name == selectorName)
                    foreach (var declaration in selector.declarations)
                        if (declaration.type == ChartStyleDeclaration.EType.Color) declaration.value = spec.Color;
            Indices[i] = extended.Count;
            extended.Add(style);
        }
        ChartPresetsDB.styles = _registered = extended.ToArray();
    }

    private static int FindTemplate(ChartStyle[] styles, EChartStyleType type, string selectorName)
    {
        for (int i = 1; i < styles.Length; i++)
            if (styles[i].type == type)
                foreach (var selector in styles[i].selectors)
                    if (selector.name == selectorName)
                        foreach (var declaration in selector.declarations)
                            if (declaration.type == ChartStyleDeclaration.EType.Color) return i;
        return -1;
    }

    public static int GetIndex(ChartData data, EChartStyleType type)
    {
        switch (type)
        {
            case EChartStyleType.Generic: return data.genericStyleIndex;
            case EChartStyleType.Background: return data.backgroundStyleIndex;
            case EChartStyleType.Border: return data.borderStyleIndex;
            default: return 0;
        }
    }

    // Menu order: default first, followed by styles of this kind in table order.
    public static int NextIndex(int current, EChartStyleType type)
    {
        var styles = ChartPresetsDB.GetChartStyles();
        if (current != 0 && ValidIndex(current, type) == 0) return 0;
        for (int i = current + 1; i < styles.Length; i++)
            if (styles[i].type == type) return i;
        return 0;
    }

    public static bool Apply(ChartData data, EChartStyleType type, int index)
    {
        if (type == EChartStyleType.Border) index = ChartState.ResolveBorder(data, index);
        if (GetIndex(data, type) == index) return false;
        switch (type)
        {
            case EChartStyleType.Generic: data.genericStyleIndex = index; break;
            case EChartStyleType.Background: data.backgroundStyleIndex = index; break;
            case EChartStyleType.Border: data.borderStyleIndex = index; break;
        }
        return true;
    }

    public static UIChart FindChart(UIDashboard dashboard, ChartData data)
    {
        foreach (var chart in dashboard.chartPool)
            if (chart.inited && chart.chartData == data) return chart;
        return null;
    }

    public static void Repaint(UIChart chart)
    {
        if (chart == null) return; // a page's data need not have a live chart
        // Native default/override declarations reset every property our copied styles alter.
        // Reusing this painter preserves its default -> background -> border ordering.
        foreach (var painter in chart.stylePainters) painter.Repaint();
    }

    internal static int CustomId(int index)
    {
        if (index <= 0) return 0;
        int at = Array.IndexOf(Indices, index);
        return at < 0 ? 0 : at + 1;
    }

    internal static int FromId(int id, EChartStyleType type) =>
        id > 0 && id <= Colors.Length && Colors[id - 1].Type == type && Indices[id - 1] > 0
            ? Indices[id - 1] : 0;

    internal static int EncodeBorder(int index)
    {
        int id = CustomId(index);
        return id == 0 ? index : -id;
    }

    internal static int DecodeBorder(int value) =>
        value < 0 ? FromId(-value, EChartStyleType.Border) : ValidIndex(value, EChartStyleType.Border);

    internal static int ValidIndex(int index, EChartStyleType type)
    {
        var styles = ChartPresetsDB.GetChartStyles();
        return index > 0 && index < styles.Length && styles[index].type == type ? index : 0;
    }
}
