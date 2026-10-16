using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DashboardOverhaul;

/// <summary>
/// Applies the configured title size relative to the native prefab and keeps enlarged titles visible.
/// </summary>
public static class ChartTitleStyle
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIChart), nameof(UIChart.TruncateStatPlanNameText), new System.Type[0])]
    static void TruncateStatPlanNameText_Postfix(UIChart __instance) => Apply(__instance);

    public static void Apply(UIChart chart)
    {
        var title = chart.titleText;
        int configured = ModConfig.TitleFontSize.Value;
        int size = configured > ModConfig.kAutoFontSize ? configured :
            chart.presets.uiPrefab.titleText.fontSize + ModConfig.kAutoFontSizeDelta;
        if (title.fontSize != size)
        {
            title.fontSize = size;
        }
        ApplyOverflow(title);
    }

    /// <summary>
    /// Vertical overflow prevents a larger font's line height from hiding the entire title.
    /// Horizontal overflow either shows the full name or the prefix that fits, according to configuration.
    /// </summary>
    private static void ApplyOverflow(Text title)
    {
        title.horizontalOverflow = HorizontalWrapMode.Overflow;
        title.verticalOverflow = VerticalWrapMode.Overflow;
        if (ModConfig.TitleSingleLine.Value) return;

        var rect = title.rectTransform.rect;
        TrimToBox(title, rect.width);
    }

    /// <summary>Shorten the title to the characters that fit its box.</summary>
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
