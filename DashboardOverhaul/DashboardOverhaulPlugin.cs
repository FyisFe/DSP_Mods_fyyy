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
        _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
        _harmony.PatchAll(typeof(UIDashboardPatch));
        _harmony.PatchAll(typeof(UIChartPatch));
        _harmony.PatchAll(typeof(DashboardLayoutPatch));

        Logger.LogInfo("DashboardOverhaul loaded.");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
