using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace InterstellarLogisticsOpt;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class InterstellarLogisticsOptPlugin : BaseUnityPlugin
{
    public new static readonly BepInEx.Logging.ManualLogSource Logger =
        BepInEx.Logging.Logger.CreateLogSource(PluginInfo.PLUGIN_NAME);

    public static ConfigEntry<bool> ModEnabled;
    public static ConfigEntry<bool> DispatchEarlyExitEnabled;

    private Harmony _harmony;

    private void Awake()
    {
        ModEnabled = Config.Bind("General", "Enabled", true,
            "Phase-disperse interstellar logistics scheduling to flatten CPU spikes / 相位分散星际物流调度以削平卡顿");
        DispatchEarlyExitEnabled = Config.Bind("General", "DispatchEarlyExit", true,
            "Skip the full pair-ring scan for stations that have no idle ship or insufficient energy (reduces total CPU, not just spikes) / 对无闲船或能量不足的塔直接跳过整轮 pair 扫描（减少总开销，不只是削峰）");

        _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
        _harmony.PatchAll(typeof(GalacticTransportPatch));
        _harmony.PatchAll(typeof(DispatchPatch));
        Logger.LogInfo("InterstellarLogisticsOpt loaded.");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }

    static class GalacticTransportPatch
    {
        /// <summary>
        /// Replaces GalacticTransport.GameTick with a phase-dispersed station sweep.
        /// Each station slot is still processed once per `period` ticks (10/30/60,
        /// identical cadence to vanilla), but offset by `time % period` so the work
        /// is spread evenly across ticks instead of all towers firing in phase.
        /// The inner priorityIndex2 / routePriority dispatch branches are unchanged.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(GalacticTransport), nameof(GalacticTransport.GameTick))]
        static bool GameTick_Prefix(GalacticTransport __instance, long time)
        {
            if (!ModEnabled.Value)
                return true; // run vanilla GameTick

            GameData gameData = __instance.gameData;
            GalaxyData galaxy = gameData.galaxy;
            GameHistoryData history = gameData.history;
            PlanetFactory[] factories = gameData.factories;
            FactoryProductionStat[] factoryStatPool = gameData.statistics.production.factoryStatPool;
            TrafficStatistics traffic = gameData.statistics.traffic;
            float sailSpeedModified = history.logisticShipSailSpeedModified;
            float shipWarpSpeed = history.logisticShipWarpDrive
                ? history.logisticShipWarpSpeedModified
                : sailSpeedModified;
            int logisticShipCarries = history.logisticShipCarries;

            StationComponent[] stationPool = __instance.stationPool;
            int stationCursor = __instance.stationCursor;

            for (int priorityIndex1 = 1; priorityIndex1 < 7; ++priorityIndex1)
            {
                int priorityIndex2 = priorityIndex1 % 6;
                int period = (priorityIndex1 == 1) ? 10
                           : (priorityIndex1 == 2 || priorityIndex1 == 3) ? 30
                           : 60;
                int phase = (int)(time % period);

                for (int index = 1 + phase; index < stationCursor; index += period)
                {
                    StationComponent stationComponent = stationPool[index];
                    if (stationComponent == null || stationComponent.id <= 0 || stationComponent.gid != index)
                        continue;

                    if (priorityIndex2 >= 1 && priorityIndex2 <= 4 &&
                        (stationComponent.routePriority == ERemoteRoutePriority.Prioritize ||
                         stationComponent.routePriority == ERemoteRoutePriority.Only ||
                         stationComponent.routePriority == ERemoteRoutePriority.Designated))
                        stationComponent.DetermineDispatch(sailSpeedModified, shipWarpSpeed, logisticShipCarries, priorityIndex2, stationPool, factoryStatPool, factories, galaxy, traffic);
                    else if (priorityIndex2 == 5 && stationComponent.routePriority == ERemoteRoutePriority.Prioritize)
                        stationComponent.DetermineDispatch(sailSpeedModified, shipWarpSpeed, logisticShipCarries, priorityIndex2, stationPool, factoryStatPool, factories, galaxy, traffic);
                    else if (priorityIndex2 == 0 && stationComponent.routePriority == ERemoteRoutePriority.Ignore)
                        stationComponent.DetermineDispatch(sailSpeedModified, shipWarpSpeed, logisticShipCarries, priorityIndex2, stationPool, factoryStatPool, factories, galaxy, traffic);
                }
            }

            return false; // skip the original method
        }
    }

    static class DispatchPatch
    {
        /// <summary>
        /// Early-exit for DetermineDispatch. Vanilla checks idleShipCount==0 / low
        /// energy *inside* the do-while pair loop (StationComponent.cs:3013-3014),
        /// so a station that cannot dispatch anything still scans its entire pair
        /// ring (lock(storage) + field reads + trip recompute per pair) before
        /// finding nothing to send. Hoisting the check to the method entry turns
        /// that O(pairs) idle scan into O(1).
        ///
        /// Unlike phase dispersion (which only redistributes load across ticks),
        /// this removes work outright, so it reduces total CPU — not just spikes.
        ///
        /// Semantics note: skipping the method also skips SetPriorityLock and the
        /// remotePairProcesses cursor advance for these stations. Harmless for idle
        /// steady state, but not bit-identical to vanilla (same caveat class as
        /// phase dispersion).
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(StationComponent), nameof(StationComponent.DetermineDispatch))]
        static bool DetermineDispatch_Prefix(StationComponent __instance)
        {
            if (!DispatchEarlyExitEnabled.Value)
                return true; // run vanilla DetermineDispatch

            if (__instance.idleShipCount == 0 || __instance.energy <= 6000000L)
                return false; // nothing dispatchable — skip the whole pair-ring scan

            return true;
        }
    }
}
