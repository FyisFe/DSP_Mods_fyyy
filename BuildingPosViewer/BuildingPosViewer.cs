using BepInEx;
using HarmonyLib;

namespace BuildingPosViewer;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class BuildingPosViewer : BaseUnityPlugin
{
    public new static readonly BepInEx.Logging.ManualLogSource Logger =
        BepInEx.Logging.Logger.CreateLogSource(PluginInfo.PLUGIN_NAME);

    private Harmony _harmony;

    private void Awake()
    {
        _harmony = Harmony.CreateAndPatchAll(typeof(Patches.WindowPatches));
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
        PosDisplayHelper.Cleanup();
    }
}
