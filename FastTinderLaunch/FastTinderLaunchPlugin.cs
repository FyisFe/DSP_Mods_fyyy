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
        _harmony = Harmony.CreateAndPatchAll(typeof(TinderPatches));
        Logger.LogInfo("FastTinderLaunch loaded.");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }

    static class TinderPatches
    {
        // ====================================================================
        // Patch 1: PrepareDispatchLogic — 发射概率恢复为100%
        //
        // New code added:
        //   if (hive.ticks % 1800 != num15 % 1800
        //       || hive.trand.NextDouble() > Pow(...) * (1.0/16.0))
        //     return;
        //
        // Fix: 1800 → 1 (tick check always passes), 0.0625 → 1E+300 (prob always passes)
        // ====================================================================
        [HarmonyTranspiler]
        [HarmonyPatch(typeof(DFTinderComponent), nameof(DFTinderComponent.PrepareDispatchLogic))]
        static IEnumerable<CodeInstruction> PrepareDispatchLogic_Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            int patchCount = 0;

            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldc_I4 && codes[i].operand is int intVal && intVal == 1800)
                {
                    codes[i].operand = 1;
                    patchCount++;
                }
                else if (IsDouble(codes[i], 0.0625))
                {
                    codes[i].operand = 1E+300;
                    patchCount++;
                }
            }

            Logger.LogInfo($"PrepareDispatchLogic: {patchCount} patches (expected 3).");
            return codes;
        }

        // ====================================================================
        // Patch 2: LogicTick — 建造触发和推进概率恢复
        //
        // 推进概率: NextDouble() < 0.75 * Pow(0.92^..., N) * g → always true
        //   Fix: second 0.75 → 1E+300 (first 0.75 is maxMatter*0.75 threshold, keep it)
        //
        // 触发相位: hiveAstroId % 1000000 * 127 → * 0 (removes phase offset)
        //   Fix: 127 (sbyte.MaxValue) → 0
        //
        // 触发衰减: Pow(0.97, existingTinders) → Pow(1.0, N) = 1.0
        //   Fix: 0.97 → 1.0
        // ====================================================================
        [HarmonyTranspiler]
        [HarmonyPatch(typeof(DFSCoreComponent), nameof(DFSCoreComponent.LogicTick))]
        static IEnumerable<CodeInstruction> LogicTick_Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            int patchCount = 0;
            int count075 = 0;

            for (int i = 0; i < codes.Count; i++)
            {
                // Replace SECOND 0.75 only (first is resource threshold)
                if (IsDouble(codes[i], 0.75))
                {
                    count075++;
                    if (count075 == 2)
                    {
                        codes[i].operand = 1E+300;
                        patchCount++;
                    }
                }
                // Replace 127 (sbyte.MaxValue) → 0 to remove phase offset
                else if (codes[i].opcode == OpCodes.Ldc_I4_S && codes[i].operand is sbyte sb && sb == 127)
                {
                    codes[i].operand = (sbyte)0;
                    patchCount++;
                }
                // Replace 0.97 → 1.0 to remove trigger decay
                else if (IsDouble(codes[i], 0.97))
                {
                    codes[i].operand = 1.0;
                    patchCount++;
                }
            }

            Logger.LogInfo($"LogicTick: {patchCount} patches (expected 3).");
            return codes;
        }

        // ====================================================================
        // Patch 3: LogicTickVirtual — 虚拟Hive建造概率恢复 (same changes)
        // ====================================================================
        [HarmonyTranspiler]
        [HarmonyPatch(typeof(DFSCoreComponent), nameof(DFSCoreComponent.LogicTickVirtual))]
        static IEnumerable<CodeInstruction> LogicTickVirtual_Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            int patchCount = 0;

            for (int i = 0; i < codes.Count; i++)
            {
                // 0.75 → 1E+300 (only one occurrence in this method)
                if (IsDouble(codes[i], 0.75))
                {
                    codes[i].operand = 1E+300;
                    patchCount++;
                }
                // 127 → 0 (phase offset)
                else if (codes[i].opcode == OpCodes.Ldc_I4_S && codes[i].operand is sbyte sb && sb == 127)
                {
                    codes[i].operand = (sbyte)0;
                    patchCount++;
                }
                // 0.97 → 1.0 (trigger decay)
                else if (IsDouble(codes[i], 0.97))
                {
                    codes[i].operand = 1.0;
                    patchCount++;
                }
            }

            Logger.LogInfo($"LogicTickVirtual: {patchCount} patches (expected 3).");
            return codes;
        }

        // ====================================================================
        // Patch 4: GetTargetTinderYieldRatio → always 1.0
        //
        // This function was added in new version to reduce trigger probability
        // based on global hive coverage. Old version had no such dampening.
        // ====================================================================
        [HarmonyPrefix]
        [HarmonyPatch(typeof(DFSCoreComponent), "GetTargetTinderYieldRatio")]
        static bool GetTargetTinderYieldRatio_Prefix(ref double __result)
        {
            __result = 1.0;
            return false;
        }

        static bool IsDouble(CodeInstruction code, double value) =>
            code.opcode == OpCodes.Ldc_R8 && code.operand is double d && Math.Abs(d - value) < 1e-10;
    }
}
