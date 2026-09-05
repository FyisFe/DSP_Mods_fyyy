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
        public const string VERSION = "1.1.0";

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
        private static readonly AccessTools.FieldRef<PlanetFactory, int> EntityCapacity =
            AccessTools.FieldRefAccess<PlanetFactory, int>("entityCapacity");
        private static readonly AccessTools.FieldRef<PlanetFactory, int[]> EntityRecycle =
            AccessTools.FieldRefAccess<PlanetFactory, int[]>("entityRecycle");
        private static readonly AccessTools.FieldRef<PlanetFactory, int> EntityRecycleCursor =
            AccessTools.FieldRefAccess<PlanetFactory, int>("entityRecycleCursor");

        private static bool _errorLogged;
        private static volatile bool _loading;
        internal static long TrimmedPoints;
        private static long _trimmedEntities;
        private static int _trimmedFactories;

        [HarmonyPrefix, HarmonyPatch(typeof(GameSave), nameof(GameSave.LoadCurrentGame))]
        private static void LoadBegin()
        {
            TrimmedPoints = _trimmedEntities = 0;
            _trimmedFactories = 0;
            _errorLogged = false;
            _loading = true;
        }

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
                    int removed = __instance.buffer.Length - target;
                    __instance.SetCapacity(target);
                    TrimmedPoints += removed;
                }
            }
            catch (Exception e)
            {
                Warn(e);
            }
        }

        [HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.Import))]
        private static void TrimFactoryAfterImport(PlanetFactory __instance)
        {
            // Full-save import owns these arrays before simulation starts; remote imports may not.
            if (!_loading) return;
            try
            {
                int removed = TrimEntities(__instance);
                if (removed == 0) return;
                _trimmedEntities += removed;
                _trimmedFactories++;
            }
            catch (Exception e) { Warn(e); }
        }

        internal static int TrimEntities(PlanetFactory factory)
        {
            int capacity = EntityCapacity(factory);
            int cursor = factory.entityCursor;
            // Keep 12.5% growth room (at least vanilla's 1024-slot initial block).
            long targetLong = cursor + Math.Max(1024L, (cursor + 7L) / 8);
            // Copy a factory only when at least 12.5% of its allocation can be reclaimed.
            if ((capacity - targetLong) * 8 < capacity) return 0;
            int target = checked((int)targetLong);
            int recycled = EntityRecycleCursor(factory);
            int[] recycle = EntityRecycle(factory);
            if (cursor < 1 || cursor > capacity || recycled < 0 || recycled >= cursor ||
                factory.entityPool?.Length != capacity || factory.entityAnimPool?.Length != capacity ||
                factory.entitySignPool?.Length != capacity || factory.entityConnPool?.LongLength != capacity * 16L ||
                factory.entityMutexs?.Length != capacity || factory.entityNeeds?.Length != capacity || recycle?.Length != capacity)
                throw new InvalidOperationException("Unexpected entity pool layout; factory " + factory.planetId + " left unchanged.");

            // Prepare every replacement before publishing: allocation/copy failure leaves the factory intact.
            // SetEntityCapacity cannot shrink: it drops the recycle stack and copies old-capacity mutex refs.
            var entities = CopyPrefix(factory.entityPool, target, cursor);
            var animations = CopyPrefix(factory.entityAnimPool, target, cursor);
            var signs = CopyPrefix(factory.entitySignPool, target, cursor);
            var connections = CopyPrefix(factory.entityConnPool, checked(target * 16), checked(cursor * 16));
            var mutexes = CopyPrefix(factory.entityMutexs, target, cursor);
            var needs = CopyPrefix(factory.entityNeeds, target, cursor);
            var recycledIds = CopyPrefix(recycle, target, recycled);

            factory.entityPool = entities;
            factory.entityAnimPool = animations;
            factory.entitySignPool = signs;
            factory.entityConnPool = connections;
            factory.entityMutexs = mutexes;
            factory.entityNeeds = needs;
            EntityRecycle(factory) = recycledIds;
            EntityCapacity(factory) = target;
            return capacity - target;
        }

        private static T[] CopyPrefix<T>(T[] source, int length, int count)
        {
            var result = new T[length];
            Array.Copy(source, result, count);
            return result;
        }

        [HarmonyFinalizer, HarmonyPatch(typeof(GameSave), nameof(GameSave.LoadCurrentGame))]
        private static void ReportAfterLoad(bool __result, Exception __exception)
        {
            _loading = false;
            if (!__result || __exception != null) return;
            if (TrimmedPoints > 0)
                PoolTrimPlugin.Log.LogInfo(string.Format(
                    "PoolTrim: trimmed {0} cargo path points, ~{1:F2} GiB saved",
                    TrimmedPoints, TrimmedPoints * 29.0 / (1024.0 * 1024.0 * 1024.0)));
            if (_trimmedEntities > 0)
                PoolTrimPlugin.Log.LogInfo(string.Format(
                    "PoolTrim: trimmed {0} entity slots across {1} factories (including companion arrays)",
                    _trimmedEntities, _trimmedFactories));
        }

        private static void Warn(Exception e)
        {
            if (_errorLogged) return;
            _errorLogged = true;
            PoolTrimPlugin.Log.LogWarning("PoolTrim failed (further errors muted): " + e);
        }
    }
}
