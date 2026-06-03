using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UXAssist.Common;
using UXAssist.UI;

namespace InterstellarLogisticsOpt;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
[BepInDependency(UXAssist.PluginInfo.PLUGIN_GUID)]
public class InterstellarLogisticsOptPlugin : BaseUnityPlugin
{
    public new static readonly BepInEx.Logging.ManualLogSource Logger =
        BepInEx.Logging.Logger.CreateLogSource(PluginInfo.PLUGIN_NAME);

    public static ConfigEntry<bool> ModEnabled;
    public static ConfigEntry<int> AmortizeFactor;

    private Harmony _harmony;

    private void Awake()
    {
        ModEnabled = Config.Bind("General", "Enabled", true,
            "Phase-disperse interstellar logistics scheduling to flatten CPU spikes / 相位分散星际物流调度以削平卡顿");
        AmortizeFactor = Config.Bind("General", "AmortizeFactor", 1,
            new BepInEx.Configuration.ConfigDescription(
                "Multiply each priority's scheduling interval by this factor to reduce total CPU (\"simulated frames\"). 1 = off (vanilla 10/30/60-tick cadence). E.g. 5 means each tower is scheduled every 50/150/300 ticks instead. Trades logistics responsiveness for CPU; load stays evenly spread across ticks (no spikes). Requires Enabled = true. / 把各优先级的调度间隔乘以该系数以降低总开销（\"模拟帧\"）。1=关闭。例如 5 表示每塔改为每 50/150/300 tick 调度一次。用调度响应速度换 CPU，且负载仍逐帧平摊不产生尖峰。需 Enabled=true。",
                new BepInEx.Configuration.AcceptableValueRange<int>(1, 30)));

        _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
        _harmony.PatchAll(typeof(GalacticTransportPatch));
        _harmony.PatchAll(typeof(DispatchPatch));

        UIConfig.Init();
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
        /// Each station slot is processed once per `period` ticks (10/30/60), offset
        /// by `time % period` so the work is spread evenly across ticks instead of
        /// all towers firing in phase. The inner priorityIndex2 / routePriority
        /// dispatch branches are unchanged.
        ///
        /// AmortizeFactor multiplies the effective period ("simulated frames"): with
        /// factor C, each tower is scheduled every period*C ticks, cutting per-tick
        /// work to N/(period*C) while keeping it evenly spread (no chunk spikes).
        /// This is the spike-flat equivalent of BuildToolOpt's contiguous chunking,
        /// trading scheduling responsiveness for total CPU. Factor 1 = vanilla cadence.
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

            int factor = AmortizeFactor.Value;
            if (factor < 1) factor = 1;

            for (int priorityIndex1 = 1; priorityIndex1 < 7; ++priorityIndex1)
            {
                int priorityIndex2 = priorityIndex1 % 6;
                int period = ((priorityIndex1 == 1) ? 10
                           : (priorityIndex1 == 2 || priorityIndex1 == 3) ? 30
                           : 60) * factor;
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
            if (!ModEnabled.Value)
                return true; // run vanilla DetermineDispatch

            if (__instance.idleShipCount == 0 || __instance.energy <= 6000000L)
                return false; // nothing dispatchable — skip the whole pair-ring scan

            return true;
        }
    }

    static class UIConfig
    {
        public static void Init()
        {
            I18N.Add("InterstellarLogisticsOpt", "InterstellarLogisticsOpt", "星际物流调度优化");
            I18N.Add("Phase-disperse interstellar logistics scheduling",
                "Phase-disperse interstellar logistics scheduling",
                "相位分散星际物流调度以削平卡顿");
            I18N.Add("Amortize factor (simulated frames)",
                "Amortize factor (simulated frames)",
                "调度间隔系数（模拟帧）");
            I18N.Apply();

            MyConfigWindow.OnUICreated += CreateUI;
        }

        private static void CreateUI(MyConfigWindow wnd, RectTransform trans)
        {
            wnd.AddSplitter(trans, 10f);
            wnd.AddTabGroup(trans, "InterstellarLogisticsOpt", "tab-group-interstellarlogisticsopt");
            var tab = wnd.AddTab(trans, "InterstellarLogisticsOpt");

            const float x = 0f;
            var y = 10f;
            wnd.AddCheckBox(x, y, tab, ModEnabled, "Phase-disperse interstellar logistics scheduling");
            y += 36f;
            var txt = wnd.AddText2(x, y + 5f, tab, "Amortize factor (simulated frames)", 14);
            wnd.AddSlider(x + txt.preferredWidth + 10f, y, tab, AmortizeFactor,
                new MyWindow.RangeValueMapper<int>(1, 30), "0", 200f).WithSmallerHandle();
        }
    }
}
