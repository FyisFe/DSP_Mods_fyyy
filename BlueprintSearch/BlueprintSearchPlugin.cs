using BepInEx;
using HarmonyLib;

namespace BlueprintSearch;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class BlueprintSearchPlugin : BaseUnityPlugin
{
    public new static readonly BepInEx.Logging.ManualLogSource Logger =
        BepInEx.Logging.Logger.CreateLogSource(PluginInfo.PLUGIN_NAME);

    private Harmony _harmony;

    private void Awake()
    {
        _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
        // Patches applied in later tasks.
        Logger.LogInfo("BlueprintSearch loaded.");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
