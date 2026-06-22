using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace DashboardOverhaul;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class DashboardOverhaulPlugin : BaseUnityPlugin
{
    public new static readonly BepInEx.Logging.ManualLogSource Logger =
        BepInEx.Logging.Logger.CreateLogSource(PluginInfo.PLUGIN_NAME);

    public static ConfigEntry<bool> ModEnabled;

    private Harmony _harmony;

    private void Awake()
    {
        ModEnabled = Config.Bind("General", "Enabled", true,
            "Enable the Dashboard paging UI / 启用仪表盘分页界面");

        _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
        _harmony.PatchAll(typeof(UIDashboardPatch));
        _harmony.PatchAll(typeof(UIChartPatch));

        Logger.LogInfo("DashboardOverhaul loaded.");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
