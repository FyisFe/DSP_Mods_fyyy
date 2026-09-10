using HarmonyLib;

namespace InterstellarLogisticsOpt;

[HarmonyPatch(typeof(GalacticTransport), nameof(GalacticTransport.GameTick))]
internal static class DispatchScheduler
{
    internal static volatile int Factor = 1;

    [HarmonyPrefix]
    private static bool Prefix(GalacticTransport __instance, long time)
    {
        int factor = Factor;
        if (!DispatchOptimization.Enabled || factor <= 1 || !DispatchOptimization.SupportedGame || DispatchOptimization.Failure != null)
            return true;

        var data = __instance.gameData;
        var history = data.history;
        float sail = history.logisticShipSailSpeedModified;
        float warp = history.logisticShipWarpDrive ? history.logisticShipWarpSpeedModified : sail;
        var stations = __instance.stationPool;

        for (int pass = 1; pass <= 6; pass++)
        {
            int priority = pass % 6;
            int period = (pass == 1 ? 10 : pass == 2 || pass == 3 ? 30 : 60) * factor;
            // GID phases spread station visits, deliberately relaxing cross-station
            // priority order. Native lock lifetimes and return loading remain unchanged.
            int phase = (int)(time % period);
            for (int gid = phase + 1; gid < __instance.stationCursor; gid += period)
            {
                var station = stations[gid];
                if (station == null || station.id <= 0 || station.gid != gid) continue;
                var route = station.routePriority;
                bool eligible = priority == 0 ? route == ERemoteRoutePriority.Ignore :
                    route == ERemoteRoutePriority.Prioritize ||
                    priority <= 4 && (route == ERemoteRoutePriority.Only || route == ERemoteRoutePriority.Designated);
                if (eligible)
                    station.DetermineDispatch(sail, warp, history.logisticShipCarries, priority, stations,
                        data.statistics.production.factoryStatPool, data.factories, data.galaxy, data.statistics.traffic);
            }
        }
        return false;
    }
}
