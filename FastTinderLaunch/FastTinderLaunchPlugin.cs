using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx;
using HarmonyLib;

namespace FastTinderLaunch;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class FastTinderLaunchPlugin : BaseUnityPlugin
{
    public new static readonly BepInEx.Logging.ManualLogSource Logger =
        BepInEx.Logging.Logger.CreateLogSource(PluginInfo.PLUGIN_NAME);

    private Harmony _harmony;

    private void Awake()
    {
        _harmony = Harmony.CreateAndPatchAll(typeof(TinderDispatchPatch));
        Logger.LogInfo("FastTinderLaunch loaded - tinder launch probability restored to 100%.");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }

    /// <summary>
    /// Patches DFTinderComponent.PrepareDispatchLogic to remove the new probability gate
    /// on tinder launches, restoring the old "charge full = launch immediately" behavior.
    ///
    /// New version added (line 155 in decompiled code):
    ///   if (hive.ticks % 1800 != num15 % 1800
    ///       || hive.trand.NextDouble() > Math.Pow(..., sailingTinders) * (1.0/16.0))
    ///     return;
    ///
    /// Strategy: Replace the constants so the condition is always false:
    ///   - 1800 → 1  (x % 1 == 0 always, so tick check always passes)
    ///   - 0.0625 (= 1/16) → 1E+300  (NextDouble() is never > 1E+300, so prob check always passes)
    /// </summary>
    static class TinderDispatchPatch
    {
        [HarmonyTranspiler]
        [HarmonyPatch(typeof(DFTinderComponent), nameof(DFTinderComponent.PrepareDispatchLogic))]
        static IEnumerable<CodeInstruction> PrepareDispatchLogic_Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            int patchCount = 0;

            for (int i = 0; i < codes.Count; i++)
            {
                // Replace ldc.i4 1800 → ldc.i4 1
                if (codes[i].opcode == OpCodes.Ldc_I4 && codes[i].operand is int intVal && intVal == 1800)
                {
                    codes[i].operand = 1;
                    patchCount++;
                }
                // Replace ldc.r8 0.0625 (1.0/16.0) → ldc.r8 1E+300
                else if (codes[i].opcode == OpCodes.Ldc_R8 && codes[i].operand is double dblVal
                         && Math.Abs(dblVal - 0.0625) < 1e-10)
                {
                    codes[i].operand = 1E+300;
                    patchCount++;
                }
            }

            if (patchCount >= 3)
            {
                Logger.LogInfo($"PrepareDispatchLogic patched successfully ({patchCount} constants replaced).");
            }
            else
            {
                Logger.LogWarning(
                    $"PrepareDispatchLogic: expected >=3 patches but applied {patchCount}. " +
                    "Game code may have changed.");
            }

            return codes;
        }
    }
}
