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

    private Harmony _harmony;

    private void Awake()
    {
        ModEnabled = Config.Bind("General", "Enabled", true,
            "Phase-disperse interstellar logistics scheduling to flatten CPU spikes / 相位分散星际物流调度以削平卡顿");

        _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
        _harmony.PatchAll(typeof(GalacticTransportPatch));
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
}
