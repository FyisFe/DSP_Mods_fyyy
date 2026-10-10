using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DashboardOverhaul;

/// <summary>
/// Helpers around the game's chart style list (<c>ChartPresetsDB.styles</c>), used by the page-level gear
/// menu and the Q/W style hotkeys:
/// <list type="bullet">
/// <item>light, 60%-transparent background colours (green, yellow, red) appended after the game's own
/// backgrounds, so the menu order is: default (blue), the game's black, green, yellow, red, and</item>
/// <item>black, green, yellow and red borders matching the weight of the game's blue one, plus</item>
/// <item>the index of that blue border, which locked charts switch to.</item>
/// </list>
/// Each new style is a copy of an existing style of the same kind, so its selectors and shape match the
/// game's own; only the colour declaration is replaced.
///
/// A saved chart stores its style indices, so a save made while these styles existed points at them. The
/// game's painter indexes the list without a bounds check, which would throw on such a save as soon as the
/// dashboard is reopened - hence <see cref="Repaint_Prefix"/>, which extends the list (and resets an index
/// that is still out of range) right before the painter reads it.
/// </summary>
public static class ChartStyleExtras
{
    /// <summary>Names of the added styles; they double as their own display text.</summary>
    public const string kGreenBackgroundKey = "绿色半透明";
    public const string kYellowBackgroundKey = "黄色半透明";
    public const string kRedBackgroundKey = "红色半透明";
    public const string kGreenBorderKey = "绿色边框";
    public const string kYellowBorderKey = "黄色边框";
    public const string kRedBorderKey = "红色边框";

    // Light backgrounds at 60% transparency (alpha 0.4), matching Material 200 tones.
    private static readonly Color kGreenBackground = new Color(0.65f, 0.84f, 0.66f, 0.4f);  // #A6D6A8
    private static readonly Color kYellowBackground = new Color(1.00f, 0.88f, 0.51f, 0.4f); // #FFE082
    private static readonly Color kRedBackground = new Color(0.94f, 0.60f, 0.60f, 0.4f);    // #EF9A9A

    // Opaque borders in the same weight as the game's blue, matching Material 500 tones.
    private static readonly Color kGreenBorder = new Color(0.30f, 0.69f, 0.31f, 1f);        // #4CAF50
    private static readonly Color kYellowBorder = new Color(1.00f, 0.76f, 0.03f, 1f);       // #FFC107
    private static readonly Color kRedBorder = new Color(0.96f, 0.26f, 0.21f, 1f);          // #F44336

    private static int s_blueBorderIndex = int.MinValue;

    /// <summary>Make sure the style list is extended and the chart's saved style indices are usable before
    /// the game's painter reads them (it indexes the list without checking the bounds).</summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(UIChartStylePainter), nameof(UIChartStylePainter.Repaint))]
    static void Repaint_Prefix(UIChartStylePainter __instance)
    {
        var chart = __instance != null ? __instance.parentUIChart : null;
        EnsureStyleIndices(chart != null ? chart.chartData : null);
    }

    /// <summary>
    /// Styles only declare what they change, so switching between them leaves the previous style behind: a
    /// translucent red stayed on the default background and on the game's black one, and elements a newly
    /// selected style does not declare kept the old tint. Re-applying the prefab's own default for this
    /// element before the selected styles clears those leftovers, while the selected style still wins for
    /// everything it does declare.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIChartStylePainter), nameof(UIChartStylePainter.Repaint))]
    static void Repaint_Postfix(UIChartStylePainter __instance)
    {
        var chart = __instance != null ? __instance.parentUIChart : null;
        var data = chart != null ? chart.chartData : null;
        if (data == null) return;
        var defaults = DefaultDeclarations(__instance, data);
        if (defaults == null || defaults.Length == 0) return;

        __instance.PaintWithDeclarations(defaults); // back to the prefab's own look first
        PaintSelected(__instance, data);            // then the active styles on top
    }

    /// <summary>The prefab's own look for this element: the painter's override when it has one, otherwise
    /// the game's "default" style entry (index 0), which the database keeps for exactly that purpose.</summary>
    private static ChartStyleDeclaration[] DefaultDeclarations(UIChartStylePainter painter, ChartData data)
    {
        var overlay = painter.overrideDefaultSelector;
        if (overlay != null && overlay.name == painter.selectorName &&
            overlay.declarations != null && overlay.declarations.Length > 0) return overlay.declarations;

        var styles = ChartPresetsDB.GetChartStyles();
        if (styles == null || styles.Length == 0 || styles[0] == null || styles[0].selectors == null) return null;
        foreach (var selector in styles[0].selectors)
            if (selector != null && selector.name == painter.selectorName && selector.declarations != null)
                return selector.declarations;
        return null;
    }

    private static void PaintSelected(UIChartStylePainter painter, ChartData data)
    {
        var styles = ChartPresetsDB.GetChartStyles();
        if (styles == null) return;
        Paint(painter, styles, data.genericStyleIndex);
        Paint(painter, styles, data.backgroundStyleIndex);
        Paint(painter, styles, data.borderStyleIndex);
    }

    private static void Paint(UIChartStylePainter painter, ChartStyle[] styles, int index)
    {
        if (index <= 0 || index >= styles.Length) return;
        var style = styles[index];
        if (style == null || style.selectors == null) return;
        foreach (var selector in style.selectors)
            if (selector != null && selector.name == painter.selectorName && selector.declarations != null)
                painter.PaintWithDeclarations(selector.declarations);
    }

    /// <summary>Extend the style list when needed, then reset any style index that is out of range.</summary>
    public static void EnsureStyleIndices(ChartData data)
    {        EnsureCustomStyles();
        var styles = ChartPresetsDB.GetChartStyles();
        if (data == null || styles == null || styles.Length == 0) return;
        data.genericStyleIndex = Clamp(data.genericStyleIndex, styles.Length, "generic");
        data.backgroundStyleIndex = Clamp(data.backgroundStyleIndex, styles.Length, "background");
        data.borderStyleIndex = Clamp(data.borderStyleIndex, styles.Length, "border");
    }

    private static int Clamp(int index, int length, string kind)
    {
        if (index <= 0) return 0;
        if (index < length) return index;
        return 0;
    }

    public static void EnsureCustomStyles()
    {
        var styles = ChartPresetsDB.GetChartStyles();
        if (styles == null || styles.Length == 0) return;

        // Order matters: the backgrounds are appended green, yellow, red, after the game's own ones.
        var wanted = new List<StyleSpec>
        {
            new StyleSpec(kGreenBackgroundKey, EChartStyleType.Background, kGreenBackground),
            new StyleSpec(kYellowBackgroundKey, EChartStyleType.Background, kYellowBackground),
            new StyleSpec(kRedBackgroundKey, EChartStyleType.Background, kRedBackground),
            new StyleSpec(kGreenBorderKey, EChartStyleType.Border, kGreenBorder),
            new StyleSpec(kYellowBorderKey, EChartStyleType.Border, kYellowBorder),
            new StyleSpec(kRedBorderKey, EChartStyleType.Border, kRedBorder),
        };

        var added = new List<ChartStyle>();
        foreach (var spec in wanted)
        {
            if (HasStyle(styles, spec.Name)) continue;
            var source = FindFirst(styles, spec.Type);
            if (source == null)
            {
                continue;
            }
            added.Add(MakeColored(source, spec));
        }
        if (added.Count == 0) return;

        var extended = new ChartStyle[styles.Length + added.Count];
        System.Array.Copy(styles, extended, styles.Length);
        for (int i = 0; i < added.Count; i++) extended[styles.Length + i] = added[i];
        ChartPresetsDB.styles = extended;
    }

    /// <summary>Style index locked charts use, or -1 when the game has no blue border style.</summary>
    public static int BlueBorderIndex    {
        get
        {
            if (s_blueBorderIndex != int.MinValue) return s_blueBorderIndex;
            var styles = ChartPresetsDB.GetChartStyles();
            s_blueBorderIndex = -1;
            if (styles != null)
                for (int i = 1; i < styles.Length; i++)
                {
                    var style = styles[i];
                    if (style == null || style.type != EChartStyleType.Border) continue;
                    string translated = style.name.Translate();
                    if (IsBlue(style.name) || IsBlue(translated)) { s_blueBorderIndex = i; break; }
                }
            return s_blueBorderIndex;
        }
    }

    /// <summary>Index of the added green background, used as the marker for page-wide batch edits,
    /// or -1 while the style list is not ready.</summary>
    public static int GreenBackgroundIndex => BackgroundIndexByName(kGreenBackgroundKey);

    /// <summary>Index of the added yellow background, or -1 while the style list is not ready.</summary>
    public static int YellowBackgroundIndex => BackgroundIndexByName(kYellowBackgroundKey);

    /// <summary>Index of the added red background, or -1 while the style list is not ready.</summary>
    public static int RedBackgroundIndex => BackgroundIndexByName(kRedBackgroundKey);

    /// <summary>The game's own black background, which statistics that meet their reference rate use,
    /// or -1 when it cannot be identified.</summary>
    public static int BlackBackgroundIndex
    {
        get
        {
            EnsureCustomStyles();
            var styles = ChartPresetsDB.GetChartStyles();
            if (styles == null) return -1;
            for (int i = 1; i < styles.Length; i++)
            {
                var style = styles[i];
                if (style == null || style.type != EChartStyleType.Background) continue;
                if (IsOwnBackground(style.name)) continue;
                if (IsBlack(style.name) || IsBlack(style.name.Translate())) return i;
            }
            return -1;
        }
    }

    private static int BackgroundIndexByName(string key)
    {
        EnsureCustomStyles();
        var styles = ChartPresetsDB.GetChartStyles();
        if (styles == null) return -1;
        for (int i = 1; i < styles.Length; i++)
            if (styles[i] != null && styles[i].name == key) return i;
        return -1;
    }

    private static bool IsOwnBackground(string name) =>
        name == kGreenBackgroundKey || name == kYellowBackgroundKey || name == kRedBackgroundKey;

    private static bool IsBlack(string text) =>
        !string.IsNullOrEmpty(text) && (text.Contains("黑") || text.ToLowerInvariant().Contains("black"));

    /// <summary>Every selectable index of a style kind, in menu order: default (0) then the table order.</summary>
    public static List<int> StyleOptions(EChartStyleType type)
    {
        EnsureCustomStyles();
        var options = new List<int> { 0 };
        var styles = ChartPresetsDB.GetChartStyles();
        if (styles == null) return options;
        for (int i = 1; i < styles.Length; i++)
            if (styles[i] != null && styles[i].type == type) options.Add(i);
        return options;
    }

    /// <summary>
    /// Rebuild the page's charts, exactly like the page-level gear menu does. Switching styles with a plain
    /// <c>Repaint</c> leaves colours behind: the painter only applies the declarations of the newly selected
    /// style, so every element that style does not declare keeps the tint of the style before it - which is
    /// what shows up as a leftover colour on the default and black backgrounds. A fresh <c>_OnInit</c>
    /// repaints every element from the prefab again, so the leftover cannot survive.
    /// </summary>
    public static void RebuildCharts(UIChart chart)
    {
        var dashboard = chart != null ? chart.uiDashboard : null;
        if (dashboard == null) return;
        dashboard.DetermineCharts();
    }

    /// <summary>The live chart instance showing this data, if it is on screen.</summary>
    public static UIChart FindChart(UIDashboard dashboard, ChartData data)
    {
        var pool = dashboard != null ? dashboard.chartPool : null;
        if (pool == null) return null;
        foreach (var chart in pool)
            if (chart != null && chart.inited && chart.chartData == data) return chart;
        return null;
    }

    /// <summary>
    /// The game's own style menu only repaints. Rebuilding next frame (after the click has been handled and
    /// the menu closed) is what stops the previous style's colour from staying on screen.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIChart), nameof(UIChart.OnStyleMenuButtonClick))]
    static void OnStyleMenuButtonClick_Postfix(UIChart __instance)
    {
        if (__instance == null) return;
        __instance.StartCoroutine(RebuildNextFrame(__instance));
    }

    private static IEnumerator RebuildNextFrame(UIChart chart)
    {
        yield return null; // let the popup menu close and the click finish
        if (chart == null) yield break;
        RebuildCharts(chart);
    }

    private struct StyleSpec
    {
        public readonly string Name;
        public readonly EChartStyleType Type;
        public readonly Color Color;

        public StyleSpec(string name, EChartStyleType type, Color color)
        {
            Name = name;
            Type = type;
            Color = color;
        }
    }

    private static bool IsBlue(string text) =>
        !string.IsNullOrEmpty(text) &&
        (text.Contains("蓝") || text.ToLowerInvariant().Contains("blue"));

    /// <summary>Copy a style of the same kind and tint its own selector with the given colour.</summary>
    private static ChartStyle MakeColored(ChartStyle source, StyleSpec spec)
    {
        var style = new ChartStyle(source) { name = spec.Name, type = spec.Type };
        int touched = 0;
        foreach (var selector in style.selectors)
        {
            if (selector == null || !IsTargetSelector(selector.name, spec.Type)) continue;
            selector.declarations = WithColor(selector.declarations, spec.Color);
            touched++;
        }
        if (touched == 0 && style.selectors.Length > 0)
        {
            // No selector is named like the target: tint the first one so the colour still applies.
            style.selectors[0].declarations = WithColor(style.selectors[0].declarations, spec.Color);
            touched = 1;
        }
        return style;
    }

    private static bool IsTargetSelector(string selectorName, EChartStyleType type)
    {
        if (string.IsNullOrEmpty(selectorName)) return false;
        string lower = selectorName.ToLowerInvariant();
        if (type == EChartStyleType.Border)
            return lower.Contains("border") || lower.Contains("frame");
        return lower.Contains("background") || lower.Contains("bg");
    }

    private static ChartStyleDeclaration[] WithColor(ChartStyleDeclaration[] declarations, Color color)
    {
        var result = new List<ChartStyleDeclaration>();
        bool replaced = false;
        if (declarations != null)
            foreach (var declaration in declarations)
            {
                if (declaration == null) continue;
                if (declaration.type == ChartStyleDeclaration.EType.Color && !replaced)
                {
                    result.Add(ColorDeclaration(color));
                    replaced = true;
                }
                else
                {
                    result.Add(declaration);
                }
            }
        if (!replaced) result.Add(ColorDeclaration(color));
        return result.ToArray();
    }

    private static ChartStyleDeclaration ColorDeclaration(Color color)
    {
        var declaration = new ChartStyleDeclaration(ChartStyleDeclaration.EType.Color);
        declaration.value = color; // Compound has an implicit conversion from Color
        return declaration;
    }

    private static bool HasStyle(ChartStyle[] styles, string name)
    {
        for (int i = 1; i < styles.Length; i++)
            if (styles[i] != null && styles[i].name == name) return true;
        return false;
    }

    private static ChartStyle FindFirst(ChartStyle[] styles, EChartStyleType type)
    {
        for (int i = 1; i < styles.Length; i++)
            if (styles[i] != null && styles[i].type == type) return styles[i];
        return null;
    }
}
