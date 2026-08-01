using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace PoolTrim
{
    [BepInPlugin(GUID, NAME, VERSION)]
    public class PoolTrimPlugin : BaseUnityPlugin
    {
        public const string GUID = "fyyy.dsp.pooltrim";
        public const string NAME = "PoolTrim";
        public const string VERSION = "1.0.0";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            Harmony.CreateAndPatchAll(typeof(Patches), GUID);
            Log.LogInfo("PoolTrim ready.");
        }
    }

    internal static class Patches
    {
        private static readonly AccessTools.FieldRef<CargoPath, int> BufferLength =
            AccessTools.FieldRefAccess<CargoPath, int>("bufferLength");

        private static bool _errorLogged;
        internal static long TrimmedPoints;

        // Saves record each path's grown capacity (splits/merges/rebuilds keep the old
        // size), so megabase saves allocate ~3x the points they actually use. Trim right
        // after import; later belt edits re-grow on demand via the vanilla SetCapacity path.
        [HarmonyPostfix, HarmonyPatch(typeof(CargoPath), nameof(CargoPath.Import))]
        private static void TrimAfterImport(CargoPath __instance)
        {
            try
            {
                int target = BufferLength(__instance);
                if (__instance.buffer != null && __instance.buffer.Length > target)
                {
                    TrimmedPoints += __instance.buffer.Length - target;
                    __instance.SetCapacity(target);
                }
            }
            catch (Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    PoolTrimPlugin.Log.LogWarning("PoolTrim failed (further errors muted): " + e);
                }
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(GameSave), nameof(GameSave.LoadCurrentGame))]
        private static void ReportAfterLoad()
        {
            if (TrimmedPoints <= 0)
                return;
            PoolTrimPlugin.Log.LogInfo(string.Format(
                "PoolTrim: trimmed {0} cargo path points, ~{1:F2} GB saved",
                TrimmedPoints, TrimmedPoints * 29.0 / (1024.0 * 1024.0 * 1024.0)));
            TrimmedPoints = 0;
        }
    }
}
