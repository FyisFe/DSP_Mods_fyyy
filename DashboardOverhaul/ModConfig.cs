using BepInEx.Configuration;
using UnityEngine;

namespace DashboardOverhaul;

/// <summary>
/// BepInEx configuration for the dashboard chart hotkeys and the chart title font size.
/// Bound once from <see cref="DashboardOverhaulPlugin.Awake"/>.
/// </summary>
public static class ModConfig
{
    /// <summary>Config value that means "use the game's own title font size plus <see cref="kAutoFontSizeDelta"/>".</summary>
    public const int kAutoFontSize = 0;

    /// <summary>How many points the automatic title font size adds to the game's own size.</summary>
    public const int kAutoFontSizeDelta = 2;

    /// <summary>Hover a chart and press this key to cycle its size (ported from StatsUITweaks).</summary>
    public static ConfigEntry<KeyCode> SwitchSizeKey;

    /// <summary>Hover a production chart and press LeftCtrl plus this key to name the statistic.</summary>
    public static ConfigEntry<KeyCode> QuickRenameKey;

    /// <summary>Hover a chart and press this key to step through the background styles.</summary>
    public static ConfigEntry<KeyCode> CycleBackgroundKey;

    /// <summary>Hover a chart and press this key to step through the border styles.</summary>
    public static ConfigEntry<KeyCode> CycleBorderKey;

    /// <summary>Hover a chart and press this key to reset its border to the default (blue when locked).</summary>
    public static ConfigEntry<KeyCode> ResetBorderKey;

    /// <summary>Chart title font size; <see cref="kAutoFontSize"/> means automatic.</summary>
    public static ConfigEntry<int> TitleFontSize;

    /// <summary>Keep a long chart title on one line so it stays fully visible (on by default).</summary>
    public static ConfigEntry<bool> TitleSingleLine;

    /// <summary>Place a newly added chart at the bottom-right of the page instead of the middle.</summary>
    public static ConfigEntry<bool> PlaceNewChartsAtBottomRight;

    public static void Bind(ConfigFile config)
    {
        SwitchSizeKey = config.Bind("Hotkeys", "SwitchSizeKey", KeyCode.Tab,
            "Hover a chart and press this key to cycle through its sizes.\n鼠标悬停在图表上时，按此键切换图表尺寸");

        QuickRenameKey = config.Bind("Hotkeys", "QuickRenameKey", KeyCode.Tab,
            "Hover a production chart and press LeftCtrl + this key to name the statistic as [planet]-[item]-[period].\n" +
            "鼠标悬停在生产统计图表上时，按 左Ctrl + 此键把统计项命名为 [星球]-[产物]-[统计区间]");

        CycleBackgroundKey = config.Bind("Hotkeys", "CycleBackgroundKey", KeyCode.Q,
            "Hover a chart and press this key to step through the background styles.\n" +
            "鼠标悬停在图表上时，按此键轮换背景样式");

        CycleBorderKey = config.Bind("Hotkeys", "CycleBorderKey", KeyCode.W,
            "Hover a chart and press this key to step through the border styles.\n" +
            "鼠标悬停在图表上时，按此键轮换边框样式");

        ResetBorderKey = config.Bind("Hotkeys", "ResetBorderKey", KeyCode.BackQuote,
            "Hover a chart and press this key to reset its border to the default colour: blue while the chart is " +
            "locked, no border otherwise.\n" +
            "鼠标悬停在图表上时，按此键把边框恢复为默认色：锁定时为蓝色，未锁定时为无色");

        TitleFontSize = config.Bind("UI", "TitleFontSize", kAutoFontSize,
            $"Dashboard chart title font size. {kAutoFontSize} = automatic (the game's own size + {kAutoFontSizeDelta}).\n" +
            $"仪表盘图表标题字号。{kAutoFontSize} = 自动（游戏自带字号 + {kAutoFontSizeDelta}）");

        TitleSingleLine = config.Bind("UI", "TitleSingleLine", true,
            "Show a long chart title on one line so the whole name stays visible. The game's own wrapping would " +
            "hide a long name completely, so keep this on unless you prefer the original behaviour.\n" +
            "长名称标题单行完整显示。关闭后使用游戏原有的换行行为（长名称可能被完全隐藏）");

        PlaceNewChartsAtBottomRight = config.Bind("UI", "PlaceNewChartsAtBottomRight", true,
            "Place a newly added statistic at the bottom-right of the current page instead of the game's centred position.\n" +
            "新添加的统计项放在当前页右下角（关闭后使用游戏默认的居中放置）");
    }
}
