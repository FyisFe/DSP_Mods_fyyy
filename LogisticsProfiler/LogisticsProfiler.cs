using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace LogisticsProfiler
{
    [BepInPlugin(Guid, "LogisticsProfiler", "0.2.2")]
    [BepInDependency(OptGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class LogisticsProfilerPlugin : BaseUnityPlugin
    {
        internal const string Guid = "org.fyyy.logisticsprofiler";
        internal const string OptGuid = "org.fyyy.interstellarlogisticsopt";
        private ConfigEntry<KeyboardShortcut> _key;
        private ConfigEntry<int> _seconds, _sampleEvery;
        private ConfigEntry<string> _label;
        private Harmony _harmony;
        internal static volatile Capture Current;
        internal static volatile Exception Failure;
        private Capture _capture;
        private GameData _data;
        private long _started, _lastFrame, _startTick, _endTick, _ended;
        private string _stem, _settings, _reason;
        private double _pausedSeconds;
        private int _durationSeconds;

        private void Awake()
        {
            _key = Config.Bind("Capture", "Key", new KeyboardShortcut(KeyCode.F9), "Start/stop a finite logistics capture.");
            _seconds = Config.Bind("Capture", "Seconds", 60, new ConfigDescription("Wall-clock capture duration.", new AcceptableValueRange<int>(10, 600)));
            _sampleEvery = Config.Bind("Capture", "SampleEvery", 256, new ConfigDescription(
                "Randomly time 1/N dispatch and flight-update calls. Scheduler is always timed. Restart capture after changes.",
                new AcceptableValueRange<int>(1, 4096)));
            _label = Config.Bind("Capture", "Label", "baseline", "A/B label written into metadata.");
            DispatchDetails.Enabled = Config.Bind("Capture", "DispatchDetails", true,
                "Count actual dispatch branches in sampled calls. Adds observer overhead. Restart game after changes.").Value;
            _harmony = new Harmony(Guid);
            try
            {
                _harmony.PatchAll(typeof(LogisticsProfilerPlugin).Assembly);
                if (Failure != null) throw Failure;
            }
            catch (Exception e) { Unpatch(_harmony); Failure = e; Logger.LogError("LogisticsProfiler patching failed: " + e); return; }
            Logger.LogInfo("LogisticsProfiler: " + _key.Value + " starts/stops a capture; output: " + Path.Combine(Paths.BepInExRootPath, "LogisticsProfiler"));
        }

        private void LateUpdate()
        {
            try
            {
                if (_capture != null)
                {
                    long now = Stopwatch.GetTimestamp();
                    if (_reason == null)
                    {
                        _capture.Frames.Add(default, now - _lastFrame, true, false, 0);
                        if (GameMain.isPaused) _pausedSeconds += (now - _lastFrame) / (double)Stopwatch.Frequency;
                        _lastFrame = now;
                        _endTick = GameMain.gameTick;
                        if (Failure != null) Stop("diagnostic_error");
                        else if (!ReferenceEquals(_data, GameMain.data) || !GameMain.isRunning) Stop("game_changed");
                        else if (_key.Value.IsDown()) Stop("manual");
                        else if ((now - _started) / (double)Stopwatch.Frequency >= _durationSeconds) Stop("duration");
                    }
                    if (_reason != null && _capture.Stop()) Finish();
                }
                else if (GameMain.isRunning && !DSPGame.IsMenuDemo && GameMain.data != null && _key.Value.IsDown())
                {
                    if (Failure != null)
                    {
                        Logger.LogError("LogisticsProfiler unavailable until restart: " + Failure);
                        UIRealtimeTip.Popup("LogisticsProfiler 已停用，请查看 BepInEx/LogOutput.log 中的错误", false);
                    }
                    else StartCapture();
                }
            }
            catch (Exception e)
            {
                Current = null;
                _capture?.Stop();
                _capture = null;
                _data = null;
                Failure = e;
                Logger.LogError("LogisticsProfiler stopped until restart: " + e);
            }
        }

        private void StartCapture()
        {
            string dir = Path.Combine(Paths.BepInExRootPath, "LogisticsProfiler");
            Directory.CreateDirectory(dir);
            _stem = Path.Combine(dir, DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff"));
            _data = GameMain.data;
            _settings = Settings();
            _durationSeconds = _seconds.Value;
            var metadata = new StringBuilder();
            metadata.AppendLine("schema=2\nprofiler=0.2.2\nlabel=" + Totals.Cell(_label.Value));
            metadata.AppendLine("dispatch_details=" + DispatchDetails.Enabled);
            metadata.AppendLine("game=" + GameConfig.gameVersion + "\nunity=" + Application.unityVersion);
            metadata.AppendLine("game_mvid=" + typeof(GameMain).Module.ModuleVersionId);
            metadata.AppendLine("profiler_mvid=" + typeof(LogisticsProfilerPlugin).Module.ModuleVersionId);
            metadata.AppendLine("stopwatch_frequency=" + Stopwatch.Frequency + "\nsample_every=" + _sampleEvery.Value);
            metadata.AppendLine("requested_seconds=" + _durationSeconds);
            metadata.AppendLine("save=" + Totals.Cell(_data.gameName) + "\nstation_cursor=" + _data.galacticTransport.stationCursor);
            metadata.AppendLine("local_planet=" + (_data.localPlanet?.id ?? 0));
            foreach (var plugin in Chainloader.PluginInfos.Values.OrderBy(p => p.Metadata.GUID))
                metadata.AppendLine("plugin=" + plugin.Metadata.GUID + " " + plugin.Metadata.Version);
            metadata.AppendLine("settings_begin:\n" + _settings);
            foreach (var method in new[] {
                AccessTools.Method(typeof(GalacticTransport), "GameTick"), AccessTools.Method(typeof(StationComponent), "DetermineDispatch"),
                AccessTools.Method(typeof(StationComponent), "InternalTickRemote") })
            {
                var info = Harmony.GetPatchInfo(method);
                if (info == null) continue;
                foreach (var group in new[] { ("prefix", info.Prefixes), ("postfix", info.Postfixes), ("transpiler", info.Transpilers), ("finalizer", info.Finalizers) })
                    foreach (var patch in group.Item2)
                        metadata.AppendLine("patch=" + method.DeclaringType.Name + "." + method.Name + " " + group.Item1 + " " + patch.owner +
                            " priority=" + patch.priority + " method=" + patch.PatchMethod.DeclaringType.FullName + "." + patch.PatchMethod.Name);
            }
            File.WriteAllText(_stem + "_metadata.txt", metadata.ToString());
            _capture = new Capture(_sampleEvery.Value);
            _reason = null;
            _pausedSeconds = 0;
            _startTick = _endTick = GameMain.gameTick;
            _started = _lastFrame = Stopwatch.GetTimestamp();
            Current = _capture;
            Logger.LogInfo("LogisticsProfiler capture started: " + _stem);
            UIRealtimeTip.Popup("物流采样已开始（" + _durationSeconds + " 秒）", false);
        }

        private void Stop(string reason)
        {
            Current = null;
            _capture.Stop();
            _reason = reason;
            _ended = Stopwatch.GetTimestamp();
        }

        private void Finish()
        {
            string settings = Settings();
            double seconds = (_ended - _started) / (double)Stopwatch.Frequency;
            File.AppendAllText(_stem + "_metadata.txt", "stop=" + _reason + "\nseconds=" + Totals.Number(seconds) +
                "\ngame_tick_begin=" + _startTick + "\ngame_tick_end=" + _endTick +
                "\nobserved_ups=" + Totals.Number((_endTick - _startTick) / seconds) +
                "\npaused_seconds_approx=" + Totals.Number(_pausedSeconds) +
                "\nsettings_changed=" + (_settings != settings) + "\nsettings_end:\n" + settings +
                "\nerror=" + Totals.Cell(Failure?.ToString()) + "\n");
            string header = "method\tpriority\tphase\tgid\tplanet\tsample_every\tsamples\tsampled_ms\tmean_ms\tmax_ms\toriginal_skipped\terrors\tno_idle\tlow_energy\tempty_pairs\tcandidate_pairs_sum\tworking_ships_sum\tlaunch_delta_sum\tno_idle_ms\tlow_energy_ms\tempty_pairs_ms\tno_launch_ms\t" + string.Join("\t", DispatchDetails.Names);
            using (var writer = new StreamWriter(_stem + "_summary.tsv", false, new UTF8Encoding(false)))
            {
                writer.WriteLine(header);
                for (int i = 0; i < _capture.Methods.Length; i++)
                {
                    bool dispatch = i >= 1 && i <= 6;
                    string name = i == 0 ? "scheduler" : i == 7 ? "remote_tick" : "dispatch";
                    writer.WriteLine(name + "\t" + (dispatch ? i - 1 : -1) + "\t-1\t-1\t-1\t" +
                        (dispatch || i == 7 ? _capture.SampleEvery : 1) + "\t" + _capture.Methods[i].Columns(dispatch || i == 7, dispatch));
                }
                for (int i = 0; i < 60; i++)
                    writer.WriteLine("scheduler_phase\t-1\t" + i + "\t-1\t-1\t1\t" + _capture.Phases[i].Columns(false, false));
                writer.WriteLine("frame\t-1\t-1\t-1\t-1\t1\t" + _capture.Frames.Columns(false, false));
            }
            using (var writer = new StreamWriter(_stem + "_stations.tsv", false, new UTF8Encoding(false)))
            {
                writer.WriteLine(header);
                foreach (var pair in _capture.Stations.OrderByDescending(p => p.Value.Ticks))
                    writer.WriteLine("dispatch\t" + pair.Key.Priority + "\t-1\t" + pair.Key.Gid + "\t" + pair.Key.Planet + "\t" +
                        _capture.SampleEvery + "\t" + pair.Value.Columns(true, true));
            }
            Logger.LogInfo("LogisticsProfiler capture complete (" + _reason + "): " + _stem);
            _capture = null;
            _data = null;
        }

        private static string Settings()
        {
            var result = new StringBuilder();
            foreach (string guid in new[] { OptGuid, "starfi5h.plugin.SampleAndHoldSim" })
                if (Chainloader.PluginInfos.TryGetValue(guid, out var plugin))
                    foreach (var entry in plugin.Instance.Config.OrderBy(p => p.Key.Section).ThenBy(p => p.Key.Key))
                        result.AppendLine(guid + " " + entry.Key.Section + "." + entry.Key.Key + "=" + Totals.Cell(entry.Value.GetSerializedValue()));
            return result.ToString();
        }

        private void OnDestroy()
        {
            Current = null;
            if (_capture != null)
            {
                try { Stop("plugin_destroyed"); if (_capture.Stop()) Finish(); }
                catch (Exception e) { Logger.LogError("LogisticsProfiler final report failed: " + e); }
            }
            if (_harmony != null) Unpatch(_harmony);
        }

        internal static void Unpatch(Harmony harmony)
        {
            // HarmonyX 2.x cannot rebuild a __runOriginal finalizer after removing its last prefix alone.
            foreach (var method in harmony.GetPatchedMethods().ToArray())
                harmony.Unpatch(method, HarmonyPatchType.All, harmony.Id);
        }

        internal static void Begin(int kind, StationComponent station, int priority, long time, out Sample state)
        {
            state = default;
            var capture = Current;
            if (capture == null || Failure != null || (kind != 0 && !Capture.Choose(capture.SampleEvery))) return;
            var sample = new Sample { Kind = kind, Phase = (int)(time % 60), Pairs = -1 };
            if (station != null)
            {
                sample.Gid = station.gid;
                sample.Planet = station.planetId;
                sample.NoIdle = station.idleShipCount == 0;
                sample.LowEnergy = station.energy <= 6000000L;
                sample.WorkingShips = station.workShipCount;
                if (priority >= 0 && station.remotePairOffsets != null && station.remotePairOffsets.Length > priority + 1)
                    sample.Pairs = station.remotePairOffsets[priority + 1] - station.remotePairOffsets[priority];
                else if (priority >= 0) sample.Pairs = 0;
            }
            if (DispatchDetails.Enabled && kind >= 1 && kind <= 6) sample.Details = new long[DispatchDetails.Names.Length];
            if (!capture.Begin(ref sample)) return;
            sample.Started = Stopwatch.GetTimestamp();
            state = sample;
        }

        internal static void End(Sample state, bool original, Exception error, StationComponent station = null)
        {
            if (state.Capture == null) return;
            long elapsed = Stopwatch.GetTimestamp() - state.Started;
            try { state.Capture.End(state, elapsed, original, error != null, station == null ? 0 : station.workShipCount - state.WorkingShips); }
            catch (Exception e) { Failure = e; }
        }
    }

    [HarmonyPatch(typeof(GalacticTransport), nameof(GalacticTransport.GameTick))]
    internal static class SchedulerPatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyBefore(LogisticsProfilerPlugin.OptGuid)]
        internal static void Prefix(long time, out Sample __state) => LogisticsProfilerPlugin.Begin(0, null, -1, time, out __state);
        [HarmonyFinalizer, HarmonyPriority(Priority.Last)]
        internal static void Finalizer(Sample __state, bool __runOriginal, Exception __exception) => LogisticsProfilerPlugin.End(__state, __runOriginal, __exception);
    }

    [HarmonyPatch(typeof(StationComponent), nameof(StationComponent.DetermineDispatch))]
    internal static class DispatchPatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyBefore(LogisticsProfilerPlugin.OptGuid)]
        internal static void Prefix(StationComponent __instance, int priorityIndex, out Sample __state)
        {
            LogisticsProfilerPlugin.Begin(priorityIndex + 1, __instance, priorityIndex, 0, out __state);
            __state.PreviousDetails = DispatchDetails.Active;
            DispatchDetails.Active = __state.Details;
        }
        [HarmonyFinalizer, HarmonyPriority(Priority.Last)]
        internal static void Finalizer(StationComponent __instance, Sample __state, bool __runOriginal, Exception __exception)
        {
            DispatchDetails.Active = __state.PreviousDetails;
            LogisticsProfilerPlugin.End(__state, __runOriginal, __exception, __instance);
        }
        [HarmonyTranspiler, HarmonyPriority(Priority.Last), HarmonyBefore(LogisticsProfilerPlugin.OptGuid)]
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) =>
            DispatchDetails.Transpiler(instructions, generator);
    }

    [HarmonyPatch(typeof(StationComponent), nameof(StationComponent.InternalTickRemote))]
    internal static class RemotePatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        internal static void Prefix(StationComponent __instance, out Sample __state) => LogisticsProfilerPlugin.Begin(7, __instance, -1, 0, out __state);
        [HarmonyFinalizer, HarmonyPriority(Priority.Last)]
        internal static void Finalizer(Sample __state, bool __runOriginal, Exception __exception) => LogisticsProfilerPlugin.End(__state, __runOriginal, __exception);
    }
}
