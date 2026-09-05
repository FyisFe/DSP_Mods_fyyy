using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace LoadMemProfiler
{
    [BepInPlugin(GUID, NAME, VERSION)]
    public class LoadMemProfilerPlugin : BaseUnityPlugin
    {
        public const string GUID = "fyyy.dsp.loadmemprofiler";
        public const string NAME = "LoadMemProfiler";
        public const string VERSION = "0.3.1";
        internal static ManualLogSource Log;
        private static LoadMemProfilerPlugin _instance;
        internal static ConfigEntry<bool> Enabled;
        internal static volatile bool Failed;
        private ConfigEntry<int> _postLoadSeconds, _snapshotInterval;
        private ConfigEntry<float> _postLoadInterval, _runtimeInterval;
        private ConfigEntry<bool> _automaticSnapshots;
        private ConfigEntry<KeyboardShortcut> _captureKey;
        internal static volatile ProfileSession Current;
        private ProfileSession _observed;
        private CapacitySnapshot _snapshot;
        private readonly FrameWindow _frames = new FrameWindow();
        private double _lastFrame, _lastSample, _runtimeStart, _nextSnapshot, _lastEdit;
        private long _lastTick;
        private long _buildCalls, _removeCalls;
        private int _planet, _loadedPlanet, _star;
        private CaptureReason _pending;
        private Harmony _harmony;

        private void Awake()
        {
            _instance = this;
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, "Record load and runtime memory diagnostics.");
            _postLoadSeconds = Config.Bind("General", "PostLoadSeconds", 120, new ConfigDescription(
                "Use the faster sampling interval for this many seconds after runtime starts.", new AcceptableValueRange<int>(0, 3600)));
            _postLoadInterval = Config.Bind("General", "PostLoadIntervalSeconds", 1f, new ConfigDescription(
                "Memory sample interval immediately after loading.", new AcceptableValueRange<float>(0.1f, 60f)));
            _runtimeInterval = Config.Bind("Runtime", "SampleIntervalSeconds", 5f, new ConfigDescription(
                "Runtime memory and frame-time sample interval.", new AcceptableValueRange<float>(1f, 60f)));
            _automaticSnapshots = Config.Bind("Runtime", "AutomaticSnapshots", false,
                "Enable start, periodic and event capacity scans. Keep disabled on memory-constrained saves; F8 still works.");
            _snapshotInterval = Config.Bind("Runtime", "SnapshotIntervalSeconds", 300, new ConfigDescription(
                "Automatic capacity scan interval (minimum 30 seconds); 0 disables periodic scans. Event/manual scans remain enabled.",
                new AcceptableValueRange<int>(0, 3600)));
            _captureKey = Config.Bind("Runtime", "CaptureKey", new KeyboardShortcut(KeyCode.F8), "Start or cancel a capacity snapshot.");
            _harmony = Harmony.CreateAndPatchAll(typeof(Patches), GUID);
            Log.LogInfo("LoadMemProfiler ready. Automatic snapshots: " + _automaticSnapshots.Value + "; start/cancel key: " + _captureKey.Value);
        }

        internal static ProfileSession StartSession(string kind, string name)
        {
            StopSession("session_replaced");
            if (!Enabled.Value || Failed) return null;
            try
            {
                var session = new ProfileSession(kind, name);
                Current = session;
                WriteMetadata(session);
                session.Record("session_begin", name, true);
                Log.LogInfo("LoadMemProfiler: " + session.FilePath);
                return session;
            }
            catch (Exception e)
            {
                Warn(e);
                StopSession("start_failed");
                return null;
            }
        }

        internal static void StopSession(string reason)
        {
            _instance?.CancelSnapshot();
            var session = Current;
            Current = null;
            if (session == null) return;
            session.Record(reason, "", true);
            session.Dispose();
        }

        internal static void Warn(Exception e)
        {
            if (Failed) return;
            Failed = true;
            Log.LogWarning("LoadMemProfiler stopped diagnostics until restart: " + e);
        }

        private void LateUpdate()
        {
            long started = Stopwatch.GetTimestamp();
            try
            {
                if (!Enabled.Value || Failed) { StopSession("disabled"); CancelSnapshot(); return; }
                if (_snapshot != null && !ReferenceEquals(_snapshot.Session, Current)) CancelSnapshot();
                if (Current != null && Current.Loading) return;
                if (!GameMain.isRunning || DSPGame.IsMenuDemo || GameMain.data == null) return;
                if (Current == null || (Current.Data != null && !ReferenceEquals(Current.Data, GameMain.data)))
                    StartSession("runtime", GameMain.data.gameName);
                var session = Current;
                if (session == null) return;
                double now = session.Seconds;
                if (!ReferenceEquals(_observed, session))
                {
                    CancelSnapshot();
                    _observed = session;
                    session.Data = GameMain.data;
                    _frames.Reset();
                    _lastFrame = _lastSample = _runtimeStart = now;
                    _lastTick = GameMain.gameTick;
                    _buildCalls = _removeCalls = 0;
                    _planet = _loadedPlanet = _star = -1;
                    _nextSnapshot = 0;
                    _lastEdit = -10;
                    _pending = CaptureReason.Start;
                    session.Record("runtime_begin", "", true);
                }
                else _frames.Add((now - _lastFrame) * 1000);
                _lastFrame = now;
                int planet = session.Data.localPlanet?.id ?? 0;
                int loaded = session.Data.localLoadedPlanetFactory?.planetId ?? 0;
                int star = session.Data.localStar?.id ?? 0;
                session.Tick = GameMain.gameTick;
                session.Planet = planet; session.LoadedPlanet = loaded; session.Star = star;
                session.Paused = GameMain.isPaused;
                if (planet != _planet || loaded != _loadedPlanet || star != _star)
                {
                    session.Record("location_changed", _planet + ":" + _loadedPlanet + ":" + _star + " -> " + planet + ":" + loaded + ":" + star, true);
                    _planet = planet; _loadedPlanet = loaded; _star = star;
                    _pending |= CaptureReason.Location;
                }
                long builds = Interlocked.Read(ref session.BuildCalls);
                long removes = Interlocked.Read(ref session.RemoveCalls);
                if (builds != _buildCalls || removes != _removeCalls)
                {
                    _buildCalls = builds; _removeCalls = removes;
                    _lastEdit = now;
                    _pending |= CaptureReason.Edits;
                }
                if (Interlocked.Exchange(ref session.SavesCompleted, 0) > 0) _pending |= CaptureReason.Save;
                if (_captureKey.Value.IsDown())
                {
                    if (_snapshot != null || (_pending & CaptureReason.Manual) != 0)
                    {
                        CancelSnapshot();
                        _pending = 0;
                        session.NextEventScan = now + 30;
                        _nextSnapshot = now + Math.Max(30, _snapshotInterval.Value);
                        Log.LogInfo("LoadMemProfiler capacity capture cancelled.");
                        return;
                    }
                    _pending |= CaptureReason.Manual;
                }
                double interval = now - _runtimeStart < _postLoadSeconds.Value ? _postLoadInterval.Value : _runtimeInterval.Value;
                if (now - _lastSample >= interval)
                {
                    session.Record("runtime", "add_calls=" + builds + " remove_calls=" + removes, true,
                        _frames.Columns((GameMain.gameTick - _lastTick) / (now - _lastSample)),
                        UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() + "\t" +
                        UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() + "\t" +
                        UnityEngine.Profiling.Profiler.GetTotalUnusedReservedMemoryLong());
                    _frames.Reset();
                    _lastSample = now;
                    _lastTick = GameMain.gameTick;
                }
                if (Failed) return;
                if (_snapshotInterval.Value > 0 && now >= _nextSnapshot) _pending |= CaptureReason.Periodic;
                if (!_automaticSnapshots.Value) _pending &= CaptureReason.Manual;
                if (Volatile.Read(ref session.Saving) > 0) return;
                if (_snapshot == null && _pending != 0 &&
                    ((_pending & (CaptureReason.Start | CaptureReason.Manual)) != 0 ||
                     (now >= session.NextEventScan && (now - _lastEdit >= 5 || (_pending & ~CaptureReason.Edits) != 0))))
                {
                    _snapshot = new CapacitySnapshot(session, _pending.ToString());
                    _pending = 0;
                    _nextSnapshot = double.PositiveInfinity;
                }
                if (_snapshot != null && !_snapshot.Step())
                {
                    _snapshot.Dispose();
                    _snapshot = null;
                    session.NextEventScan = session.Seconds + 30;
                    _nextSnapshot = session.Seconds + Math.Max(30, _snapshotInterval.Value);
                }
            }
            catch (Exception e)
            {
                // A failed diagnostic must never interrupt simulation or trigger a per-frame retry.
                Warn(e);
                CancelSnapshot();
                StopSession("diagnostic_error");
            }
            finally { _frames.ObserverMs += (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency; }
        }

        private void CancelSnapshot()
        {
            var snapshot = _snapshot;
            _snapshot = null;
            try { snapshot?.Dispose(); } catch (Exception e) { Warn(e); }
        }

        private void OnDestroy()
        {
            CancelSnapshot();
            StopSession("plugin_destroyed");
            _observed = null;
            _harmony?.UnpatchSelf();
            _instance = null;
        }

        private static void WriteMetadata(ProfileSession session)
        {
            var sb = new StringBuilder();
            sb.AppendLine("schema=3");
            sb.AppendLine("profiler=" + VERSION);
            sb.AppendLine("game=" + GameConfig.gameVersion);
            sb.AppendLine("unity=" + Application.unityVersion);
            sb.AppendLine("clr=" + Environment.Version + " pointer_bytes=" + IntPtr.Size + " gc_max_generation=" + GC.MaxGeneration);
            sb.AppendLine("game_mvid=" + typeof(GameMain).Module.ModuleVersionId);
            sb.AppendLine("profiler_mvid=" + typeof(LoadMemProfilerPlugin).Module.ModuleVersionId);
            foreach (var plugin in Chainloader.PluginInfos.Values)
                sb.AppendLine("plugin=" + Tsv.Cell(plugin.Metadata.GUID) + " " + plugin.Metadata.Version);
            File.WriteAllText(session.Stem + "_metadata.txt", sb.ToString());
        }
    }

    [Flags]
    internal enum CaptureReason { Start = 1, Periodic = 2, Location = 4, Edits = 8, Save = 16, Manual = 32 }

    internal struct Sample
    {
        public long GcUsed, MonoHeap, MonoUsed, Commit, WorkingSet, PageFaults;
        public int Gc0, Gc1, Gc2;
    }

    internal sealed class ProfileSession : IDisposable
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly object _gate = new object();
        private StreamWriter _writer;
        internal readonly string Stem;
        internal string FilePath => Stem + ".tsv";
        internal double Seconds => _clock.Elapsed.TotalSeconds;
        internal volatile bool Loading;
        internal volatile GameData Data;
        internal long Tick = -1;
        internal int Planet, LoadedPlanet, Star;
        internal bool Paused;
        internal Stream SaveStream;
        internal long BuildCalls, RemoveCalls;
        internal int Saving, SavesCompleted, SnapshotId;
        internal double NextEventScan;

        internal ProfileSession(string kind, string name)
        {
            Loading = kind == "load";
            string dir = Path.Combine(Paths.BepInExRootPath, "LoadMemProfiler");
            Directory.CreateDirectory(dir);
            name = Tsv.Cell(name);
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            if (name.Length > 80) name = name.Substring(0, 80);
            Stem = Path.Combine(dir, kind + "_" + name + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff", CultureInfo.InvariantCulture));
            _writer = new StreamWriter(new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false), 65536);
            _writer.WriteLine("t_s\tevent\tdetail\tfile_bytes\tgc_used_bytes\tmono_heap_bytes\tmono_used_bytes\tcommit_bytes\tworking_set_bytes\tpage_faults\tgc0\tgc1\tgc2\tgame_tick\tlocal_planet\tloaded_planet\tlocal_star\tpaused\tframes\tpercentile_frames\tframe_mean_ms\tframe_p95_ms\tframe_p99_ms\tframe_max_ms\tobserved_ups\tobserver_ms\tunity_allocated_bytes\tunity_reserved_bytes\tunity_unused_reserved_bytes");
        }

        internal void Record(string evt, string detail, bool flush = false, string frames = null, string unity = null)
        {
            lock (_gate)
            {
                if (_writer == null) return;
                try
                {
                    Sample s = MemMetrics.Capture();
                    long position = -1;
                    try { if (SaveStream != null) position = SaveStream.Position; } catch { }
                    _writer.WriteLine(Tsv.Number(Seconds) + "\t" + Tsv.Cell(evt) + "\t" + Tsv.Cell(detail) + "\t" + position + "\t" +
                        s.GcUsed + "\t" + s.MonoHeap + "\t" + s.MonoUsed + "\t" + s.Commit + "\t" + s.WorkingSet + "\t" +
                        s.PageFaults + "\t" + s.Gc0 + "\t" + s.Gc1 + "\t" + s.Gc2 + "\t" +
                        Tick + "\t" + Planet + "\t" + LoadedPlanet + "\t" + Star + "\t" + (Paused ? 1 : 0) + "\t" +
                        (frames ?? "-1\t-1\t-1\t-1\t-1\t-1\t-1\t-1") + "\t" + (unity ?? "-1\t-1\t-1"));
                    if (flush) _writer.Flush();
                }
                catch (Exception e)
                {
                    LoadMemProfilerPlugin.Warn(e);
                    Dispose();
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                var writer = _writer;
                _writer = null;
                SaveStream = null;
                Data = null;
                try { writer?.Dispose(); } catch (Exception e) { LoadMemProfilerPlugin.Warn(e); }
            }
        }
    }

    internal static class MemMetrics
    {
        [StructLayout(LayoutKind.Sequential, Size = 80)]
        private struct PROCESS_MEMORY_COUNTERS_EX
        {
            public uint cb;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize;
            public UIntPtr WorkingSetSize;
            public UIntPtr QuotaPeakPagedPoolUsage;
            public UIntPtr QuotaPagedPoolUsage;
            public UIntPtr QuotaPeakNonPagedPoolUsage;
            public UIntPtr QuotaNonPagedPoolUsage;
            public UIntPtr PagefileUsage;
            public UIntPtr PeakPagefileUsage;
            public UIntPtr PrivateUsage;
        }

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool GetProcessMemoryInfo(IntPtr hProcess,
            out PROCESS_MEMORY_COUNTERS_EX counters, uint size);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("mono-2.0-bdwgc")]
        private static extern long mono_gc_get_heap_size();

        [DllImport("mono-2.0-bdwgc")]
        private static extern long mono_gc_get_used_size();

        private static bool _psapiOk = true;
        private static bool _monoOk = true;

        public static Sample Capture()
        {
            var s = new Sample
            {
                GcUsed = -1,
                MonoHeap = -1,
                MonoUsed = -1,
                Commit = -1,
                WorkingSet = -1,
                PageFaults = -1, Gc0 = -1, Gc1 = -1, Gc2 = -1
            };

            try
            {
                s.GcUsed = GC.GetTotalMemory(false);
                s.Gc0 = GC.CollectionCount(0);
                s.Gc1 = GC.MaxGeneration >= 1 ? GC.CollectionCount(1) : -1;
                s.Gc2 = GC.MaxGeneration >= 2 ? GC.CollectionCount(2) : -1;
            }
            catch
            {
            }

            if (_monoOk)
            {
                try
                {
                    s.MonoHeap = mono_gc_get_heap_size();
                    s.MonoUsed = mono_gc_get_used_size();
                }
                catch (Exception)
                {
                    _monoOk = false;
                }
            }

            if (_psapiOk)
            {
                try
                {
                    var pmc = new PROCESS_MEMORY_COUNTERS_EX();
                    pmc.cb = (uint) Marshal.SizeOf(typeof(PROCESS_MEMORY_COUNTERS_EX));
                    if (GetProcessMemoryInfo(GetCurrentProcess(), out pmc, pmc.cb))
                    {
                        s.PageFaults = pmc.PageFaultCount;
                        s.Commit = (long) pmc.PrivateUsage.ToUInt64();
                        s.WorkingSet = (long) pmc.WorkingSetSize.ToUInt64();
                    }
                    else
                    {
                        _psapiOk = false;
                    }
                }
                catch (Exception)
                {
                    _psapiOk = false;
                }
            }

            if (!_psapiOk)
            {
                try
                {
                    using (var p = Process.GetCurrentProcess())
                    {
                        s.Commit = p.PrivateMemorySize64;
                        s.WorkingSet = p.WorkingSet64;
                    }
                }
                catch
                {
                }
            }

            return s;
        }
    }

    internal static class Patches
    {
        private static volatile ProfileSession _loading;

        [HarmonyPrefix, HarmonyPatch(typeof(GameSave), nameof(GameSave.LoadCurrentGame))]
        private static void LoadBegin(string saveName, out ProfileSession __state)
        {
            __state = LoadMemProfilerPlugin.StartSession("load", saveName);
            _loading = __state;
        }

        [HarmonyFinalizer, HarmonyPatch(typeof(GameSave), nameof(GameSave.LoadCurrentGame))]
        private static void LoadEnd(ProfileSession __state, bool __result, Exception __exception)
        {
            if (__state == null) return;
            if (ReferenceEquals(_loading, __state)) _loading = null;
            __state.SaveStream = null;
            __state.Loading = false;
            __state.Record(__result && __exception == null ? "load_end" : "load_failed", __exception?.GetType().Name ?? "", true);
            if ((!__result || __exception != null) && ReferenceEquals(LoadMemProfilerPlugin.Current, __state))
                LoadMemProfilerPlugin.StopSession("load_failed_end");
        }

        [HarmonyPostfix, HarmonyPatch(typeof(PerformanceMonitor), nameof(PerformanceMonitor.BeginStream))]
        private static void BeginStream(Stream str)
        {
            var session = _loading;
            if (session != null) session.SaveStream = str;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(PerformanceMonitor), nameof(PerformanceMonitor.EndStream))]
        private static void EndStream()
        {
            var session = _loading;
            if (session != null) session.SaveStream = null;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(PerformanceMonitor), nameof(PerformanceMonitor.BeginData))]
        private static void BeginData(ESaveDataEntry entry) => _loading?.Record("data_begin:" + entry, "");

        [HarmonyPostfix, HarmonyPatch(typeof(PerformanceMonitor), nameof(PerformanceMonitor.EndData))]
        private static void EndData(ESaveDataEntry entry) => _loading?.Record("data_end:" + entry, "");

        [HarmonyPostfix, HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.Import))]
        private static void FactoryImported(PlanetFactory __instance) => _loading?.Record("factory", "index=" + __instance.index + " planet=" + (__instance.planet?.id ?? 0));

        [HarmonyPostfix, HarmonyPatch(typeof(DysonSphere), nameof(DysonSphere.Import))]
        private static void SphereImported(DysonSphere __instance) => _loading?.Record("dyson_sphere", "star=" + (__instance.starData?.id ?? 0));

        internal sealed class SaveProbe { internal ProfileSession Session; internal long Start; }

        [HarmonyPrefix, HarmonyPatch(typeof(GameSave), nameof(GameSave.SaveCurrentGame))]
        private static void SaveBegin(string saveName, out SaveProbe __state)
        {
            var session = LoadMemProfilerPlugin.Current;
            __state = null;
            if (session == null || session.Loading) return;
            __state = new SaveProbe { Session = session, Start = Stopwatch.GetTimestamp() };
            Interlocked.Increment(ref session.Saving);
            session.Record("save_begin", saveName, true);
        }

        [HarmonyFinalizer, HarmonyPatch(typeof(GameSave), nameof(GameSave.SaveCurrentGame))]
        private static void SaveEnd(SaveProbe __state, bool __result, Exception __exception)
        {
            if (__state == null) return;
            var session = __state.Session;
            session.Record(__result && __exception == null ? "save_end" : "save_failed",
                "duration_ms=" + Tsv.Number((Stopwatch.GetTimestamp() - __state.Start) * 1000.0 / Stopwatch.Frequency) +
                " exception=" + (__exception?.GetType().Name ?? ""), true);
            Interlocked.Decrement(ref session.Saving);
            Interlocked.Increment(ref session.SavesCompleted);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.AddEntityData))]
        private static void EntityAdded(PlanetFactory __instance)
        {
            var session = LoadMemProfilerPlugin.Current;
            if (session?.Data != null && ReferenceEquals(session.Data, __instance.gameData)) Interlocked.Increment(ref session.BuildCalls);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.RemoveEntityWithComponents))]
        private static void EntityRemoved(PlanetFactory __instance)
        {
            var session = LoadMemProfilerPlugin.Current;
            if (session?.Data != null && ReferenceEquals(session.Data, __instance.gameData)) Interlocked.Increment(ref session.RemoveCalls);
        }

        // End saves the last-exit file before destroying game data; retain its save markers.
        [HarmonyFinalizer, HarmonyPatch(typeof(GameMain), nameof(GameMain.End))]
        private static void GameEnded() => LoadMemProfilerPlugin.StopSession("game_end");
    }
}
