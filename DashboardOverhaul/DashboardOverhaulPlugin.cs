using System;
using BepInEx;
using HarmonyLib;

namespace DashboardOverhaul;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class DashboardOverhaulPlugin : BaseUnityPlugin
{
    public new static readonly BepInEx.Logging.ManualLogSource Logger =
        BepInEx.Logging.Logger.CreateLogSource(PluginInfo.PLUGIN_NAME);

    internal static readonly Type[] PatchTypes =
    {
        typeof(UIDashboardPatch),
        typeof(UIChartPatch),
        typeof(DashboardLayoutPatch),
        typeof(ChartTitleStyle),
        typeof(ProductionSpeedLayout),
        typeof(ConfirmHotkeyPatch),
        typeof(DashboardKeyPatch),
        typeof(StatPlanShortcutPatch),
        typeof(ChartPlacement),
        typeof(ResourceRateBars),
        typeof(ChartState),
        typeof(ChartStyles),
    };

    private Harmony _harmony;

    private void Awake()
    {
        ModConfig.Bind(Config);

        _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
        foreach (var type in PatchTypes) _harmony.PatchAll(type);
        ChartStyles.Initialize();

        Logger.LogInfo("DashboardOverhaul loaded.");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
