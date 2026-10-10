using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DashboardOverhaul;

/// <summary>
/// Configurable dashboard chart title font size. The configuration defaults to automatic: the game's
/// own title font size plus <see cref="ModConfig.kAutoFontSizeDelta"/> (2 points), so loading the mod
/// enlarges every chart title by two. A positive configured value is used as an absolute size.
/// The game's size is read from the chart prefab's own title text, tracked per chart type and size
/// because different sizes may ship different title sizes; the first chart of a type and size seen on
/// screen still carries it.
///
/// Long titles are handled by <see cref="ModConfig.TitleSingleLine"/> (on by default). The chart prefabs
/// size the title box for the game's short default abbreviations and ship <c>Wrap</c>/<c>Truncate</c>, so a
/// line taller than the box is dropped entirely - with an enlarged font (or a renamed statistic such as
/// "A1-1-宇宙矩阵-10分钟") the chart would appear to have no title at all. While the option is on the title
/// is kept on one overflowing line so the whole name is readable; with it off the title keeps the game's
/// own look, one line clipped at the box edge, which still shows as much of the name as the size has room
/// for (the vertical room is always kept so the enlarged font cannot make a title invisible).
/// </summary>
public static class ChartTitleStyle
{
    private static readonly Dictionary<(EStatPlanType, int), int> s_gameSizes = new();

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIChart), nameof(UIChart.TruncateStatPlanNameText), new System.Type[0])]
    static void TruncateStatPlanNameText_Postfix(UIChart __instance) => Apply(__instance);

    public static void Apply(UIChart chart)
    {
        var title = chart != null ? chart.titleText : null;
        if (title == null) return;

        int preset = chart.chartData != null ? chart.chartData.presetIndex : chart.presetIndex;
        var key = (chart.statPlanType, preset);
        if (!s_gameSizes.TryGetValue(key, out int gameSize))
        {
            gameSize = title.fontSize;
            s_gameSizes[key] = gameSize;
        }

        var config = ModConfig.TitleFontSize;
        int configured = config != null ? config.Value : ModConfig.kAutoFontSize;
        int size = configured > ModConfig.kAutoFontSize ? configured : gameSize + ModConfig.kAutoFontSizeDelta;
        if (title.fontSize != size)
        {
            title.fontSize = size;
        }
        ApplyOverflow(title, key);
    }

    /// <summary>
    /// Title overflow. The chart prefabs ship <c>Wrap</c>/<c>Truncate</c>, and a line taller than the box is
    /// dropped entirely: with the mod's enlarged title font (13pt needs a 21px line in the 18px box) that
    /// hides even the short default name, which the original 11pt font still fitted. The vertical room is
    /// therefore always kept, so a title is never invisible.
    /// <see cref="ModConfig.TitleSingleLine"/> (on by default) then decides how much of the name is shown:
    /// on = the whole name on one overflowing line, off = the game's own look, i.e. only the characters the
    /// box has room for (<see cref="TrimToBox"/>; this Unity version has no truncation wrap mode, so the
    /// text itself is shortened).
    /// </summary>
    private static void ApplyOverflow(Text title, (EStatPlanType, int) key)
    {
        var config = ModConfig.TitleSingleLine;
        bool singleLine = config == null || config.Value; // on by default
        const HorizontalWrapMode wantH = HorizontalWrapMode.Overflow;
        const VerticalWrapMode wantV = VerticalWrapMode.Overflow;

        title.horizontalOverflow = wantH;
        title.verticalOverflow = wantV;
        if (singleLine) return;

        var rect = title.rectTransform.rect;
        TrimToBox(title, rect.width);
    }

    /// <summary>Shorten the title to the characters that fit the box, reproducing what the game's own title
    /// box shows at this chart size (the box simply clips what does not fit).</summary>
    private static void TrimToBox(Text title, float available)
    {
        string full = title.text ?? string.Empty;
        if (available <= 1f || full.Length <= 1) return;
        if (title.preferredWidth <= available) return;

        for (int length = full.Length - 1; length > 0; length--)
        {
            title.text = full.Substring(0, length);
            if (title.preferredWidth <= available) return;
        }
        title.text = full.Substring(0, 1);
    }
}
