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
        /// <summary>
        /// Patch 1: EnergyCap_Gamma_Req — force currentStrength = 1.0 for photon mode,
        /// request zero energy from the Dyson Sphere.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(PowerGeneratorComponent), nameof(PowerGeneratorComponent.EnergyCap_Gamma_Req))]
        static bool EnergyCap_Gamma_Req_Prefix(
            ref PowerGeneratorComponent __instance,
            ref long __result)
        {
            if (__instance.productId <= 0)
                return true;

            __instance.currentStrength = 1.0f;

            // Vanilla formula with currentStrength replaced by 1.0:
            //   capacity = currentStrength * (1+warmup*1.5) * lensBonus * modeMultiplier * base
            float accBonus = (float)Cargo.accTableMilli[__instance.catalystIncLevel];
            __instance.capacityCurrentTick = (long)(
                (1.0 + (double)__instance.warmup * 1.5)
                * (__instance.catalystPoint > 0 ? 2.0 * (1.0 + (double)accBonus) : 1.0)
                * 8.0
                * (double)__instance.genEnergyPerTick);

            // Vanilla: warmupSpeed = (currentStrength - 0.75) * 4 / 72000
            // With currentStrength = 1.0 this is a constant positive value
            __instance.warmupSpeed = (float)((1.0 - 0.75) * 4.0 / 72000.0);

            // Request zero energy from the Dyson Sphere
            __result = 0L;
            return false;
        }

        /// <summary>
        /// Patch 2: EnergyCap_Gamma — skip response scaling for photon mode,
        /// keep capacityCurrentTick at full power.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(PowerGeneratorComponent), nameof(PowerGeneratorComponent.EnergyCap_Gamma))]
        static bool EnergyCap_Gamma_Prefix(
            ref PowerGeneratorComponent __instance,
            ref long __result)
        {
            if (__instance.productId <= 0)
                return true;

            // Do not scale capacityCurrentTick by response.
            // Photon mode returns 0 energy to the grid (same as vanilla).
            __result = 0L;
            return false;
        }
    }
}
