using BepInEx;
using HarmonyLib;

namespace DashboardOverhaul;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class DashboardOverhaulPlugin : BaseUnityPlugin
{
    public new static readonly BepInEx.Logging.ManualLogSource Logger =
        BepInEx.Logging.Logger.CreateLogSource(PluginInfo.PLUGIN_NAME);

    private Harmony _harmony;

    private void Awake()
    {
        ModConfig.Bind(Config);

        _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
        _harmony.PatchAll(typeof(UIDashboardPatch));
        _harmony.PatchAll(typeof(UIChartPatch));
        _harmony.PatchAll(typeof(DashboardLayoutPatch));
        _harmony.PatchAll(typeof(ChartHotkeys));
        _harmony.PatchAll(typeof(ChartTitleStyle));
        _harmony.PatchAll(typeof(ProductionSpeedLayout));
        _harmony.PatchAll(typeof(ConfirmHotkeyPatch));
        _harmony.PatchAll(typeof(DashboardKeyPatch));
        _harmony.PatchAll(typeof(StatPlanShortcutPatch));
        _harmony.PatchAll(typeof(ChartPlacement));
        _harmony.PatchAll(typeof(ResourceRateBars));
        _harmony.PatchAll(typeof(ChartLockPersist));
        _harmony.PatchAll(typeof(ChartStyleExtras));

        Logger.LogInfo("DashboardOverhaul loaded.");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
