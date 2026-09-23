using BepInEx;
using HarmonyLib;

namespace FullPhotonReceiver;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class FullPhotonReceiverPlugin : BaseUnityPlugin
{
    public new static readonly BepInEx.Logging.ManualLogSource Logger =
        BepInEx.Logging.Logger.CreateLogSource(PluginInfo.PLUGIN_NAME);

    private Harmony _harmony;

    private void Awake()
    {
        _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
        _harmony.PatchAll(typeof(GammaPatches));
        Logger.LogInfo("FullPhotonReceiver loaded.");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }

    static class GammaPatches
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(PowerGeneratorComponent), nameof(PowerGeneratorComponent.EnergyCap_Gamma_Req))]
        static bool EnergyCap_Gamma_Req_Prefix(
            ref PowerGeneratorComponent __instance,
            ref long __result)
        {
            __instance.currentStrength = 1.0f;

            // Keep lens types and proliferator bonuses owned by the game.
            __instance.capacityCurrentTick = __instance.MaxOutputCurrent_Gamma();
            __instance.warmupSpeed = 1f / 72000f;

            // Request zero energy from the Dyson Sphere
            __result = 0L;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(PowerGeneratorComponent), nameof(PowerGeneratorComponent.EnergyCap_Gamma))]
        static bool EnergyCap_Gamma_Prefix(
            ref PowerGeneratorComponent __instance,
            ref long __result)
        {
            // Preserve full capacity; only power mode supplies the grid.
            __result = __instance.productId == 0 ? __instance.capacityCurrentTick : 0L;
            return false;
        }
    }
}
