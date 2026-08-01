using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx;
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
        public const string VERSION = "0.1.0";

        internal static ManualLogSource Log;
        internal static LoadMemProfilerPlugin Instance;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> PostLoadSeconds;
        internal static ConfigEntry<float> PostLoadIntervalSeconds;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            Enabled = Config.Bind("General", "Enabled", true,
                "Record memory profile during game-save loading.");
            PostLoadSeconds = Config.Bind("General", "PostLoadSeconds", 120,
                "How many seconds to keep sampling after loading finishes (captures async model/render building).");
            PostLoadIntervalSeconds = Config.Bind("General", "PostLoadIntervalSeconds", 1.0f,
                "Sampling interval in seconds for the post-load phase.");

            Harmony.CreateAndPatchAll(typeof(Patches), GUID);
            Log.LogInfo("LoadMemProfiler ready.");
        }

        internal void StartPostLoadSampling(ProfileSession session)
        {
            StartCoroutine(PostLoadSampling(session));
        }

        private IEnumerator PostLoadSampling(ProfileSession session)
        {
            float interval = Mathf.Max(0.1f, PostLoadIntervalSeconds.Value);
            int total = Mathf.Max(0, PostLoadSeconds.Value);
            float elapsed = 0f;
            while (elapsed < total)
            {
                yield return new WaitForSecondsRealtime(interval);
                elapsed += interval;
                try
                {
                    session.RecordAndAppend("postload", "");
                }
                catch (Exception e)
                {
                    Log.LogWarning("post-load sampling stopped: " + e.Message);
                    yield break;
                }
            }
            Log.LogInfo("LoadMemProfiler: post-load sampling finished -> " + session.FilePath);
        }
    }

    internal struct Sample
    {
        public double T;
        public string Event;
        public string Detail;
        public long FileBytes;
        public long GcLive;
        public long MonoHeap;
        public long MonoUsed;
        public long Commit;
        public long WorkingSet;
    }

    internal class ProfileSession
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private readonly List<Sample> _rows = new List<Sample>(32768);
        private readonly string _saveName;
        internal Stream SaveStream;
        public string FilePath { get; private set; }

        public ProfileSession(string saveName)
        {
            _saveName = saveName;
            Record("session_begin", saveName);
        }

        public void Record(string evt, string detail)
        {
            Sample s = MemMetrics.Capture();
            s.T = _sw.Elapsed.TotalSeconds;
            s.Event = evt;
            s.Detail = detail;
            Stream stream = SaveStream;
            try
            {
                s.FileBytes = stream != null && stream.CanRead ? stream.Position : -1L;
            }
            catch
            {
                s.FileBytes = -1L;
            }
            _rows.Add(s);
        }

        public void Finish()
        {
            Record("session_end", _saveName);
            SaveStream = null;

            string dir = Path.Combine(Paths.BepInExRootPath, "LoadMemProfiler");
            Directory.CreateDirectory(dir);
            string safeName = _saveName;
            foreach (char c in Path.GetInvalidFileNameChars())
                safeName = safeName.Replace(c, '_');
            FilePath = Path.Combine(dir,
                string.Format("load_{0}_{1:yyyyMMdd_HHmmss}.tsv", safeName, DateTime.Now));

            var sb = new StringBuilder(_rows.Count * 96);
            sb.AppendLine("t_s\tevent\tdetail\tfile_MB\tgc_live_MB\tmono_heap_MB\tmono_used_MB\tcommit_MB\tws_MB");
            foreach (Sample s in _rows)
                AppendRow(sb, s);
            File.WriteAllText(FilePath, sb.ToString());

            LogSummary();
            LoadMemProfilerPlugin.Log.LogInfo("LoadMemProfiler: profile written -> " + FilePath);
        }

        // Post-load samples append directly so data survives a crash of the game afterwards.
        public void RecordAndAppend(string evt, string detail)
        {
            Record(evt, detail);
            if (FilePath == null)
                return;
            var sb = new StringBuilder(128);
            AppendRow(sb, _rows[_rows.Count - 1]);
            File.AppendAllText(FilePath, sb.ToString());
        }

        private static void AppendRow(StringBuilder sb, Sample s)
        {
            sb.Append(s.T.ToString("F3", CultureInfo.InvariantCulture)).Append('\t');
            sb.Append(s.Event).Append('\t');
            sb.Append(s.Detail).Append('\t');
            sb.Append(Mb(s.FileBytes)).Append('\t');
            sb.Append(Mb(s.GcLive)).Append('\t');
            sb.Append(Mb(s.MonoHeap)).Append('\t');
            sb.Append(Mb(s.MonoUsed)).Append('\t');
            sb.Append(Mb(s.Commit)).Append('\t');
            sb.Append(Mb(s.WorkingSet)).AppendLine();
        }

        private static string Mb(long bytes)
        {
            return bytes < 0 ? "-1" : (bytes / (1024.0 * 1024.0)).ToString("F1", CultureInfo.InvariantCulture);
        }

        private void LogSummary()
        {
            long peakCommit = 0;
            foreach (Sample s in _rows)
                if (s.Commit > peakCommit)
                    peakCommit = s.Commit;

            // Attribute commit growth to the interval ending at each sample, keep the largest jumps.
            var jumps = new List<KeyValuePair<long, string>>();
            for (int i = 1; i < _rows.Count; i++)
            {
                long delta = _rows[i].Commit - _rows[i - 1].Commit;
                if (_rows[i].Commit >= 0 && _rows[i - 1].Commit >= 0 && delta > 0)
                    jumps.Add(new KeyValuePair<long, string>(delta,
                        _rows[i].Event + (_rows[i].Detail.Length > 0 ? " " + _rows[i].Detail : "")));
            }
            jumps.Sort((a, b) => b.Key.CompareTo(a.Key));

            var log = LoadMemProfilerPlugin.Log;
            log.LogInfo(string.Format("LoadMemProfiler summary for '{0}': peak commit {1} MB, {2} samples",
                _saveName, Mb(peakCommit), _rows.Count));
            int n = Math.Min(8, jumps.Count);
            for (int i = 0; i < n; i++)
                log.LogInfo(string.Format("  commit +{0} MB at {1}", Mb(jumps[i].Key), jumps[i].Value));
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
                FileBytes = -1,
                GcLive = -1,
                MonoHeap = -1,
                MonoUsed = -1,
                Commit = -1,
                WorkingSet = -1
            };

            try
            {
                s.GcLive = GC.GetTotalMemory(false);
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
        internal static ProfileSession Session;
        private static bool _errorLogged;

        private static void Guarded(Action action)
        {
            if (Session == null)
                return;
            try
            {
                action();
            }
            catch (Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    LoadMemProfilerPlugin.Log.LogWarning("LoadMemProfiler sampling error (further errors muted): " + e);
                }
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(GameSave), nameof(GameSave.LoadCurrentGame))]
        private static void LoadBegin(string saveName)
        {
            if (!LoadMemProfilerPlugin.Enabled.Value)
                return;
            try
            {
                Session = new ProfileSession(saveName);
            }
            catch (Exception e)
            {
                Session = null;
                LoadMemProfilerPlugin.Log.LogWarning("LoadMemProfiler failed to start session: " + e);
            }
        }

        [HarmonyFinalizer, HarmonyPatch(typeof(GameSave), nameof(GameSave.LoadCurrentGame))]
        private static void LoadEnd(Exception __exception)
        {
            ProfileSession session = Session;
            Session = null;
            if (session == null)
                return;
            try
            {
                if (__exception != null)
                    session.Record("load_exception", __exception.GetType().Name);
                session.Finish();
                LoadMemProfilerPlugin.Instance.StartPostLoadSampling(session);
            }
            catch (Exception e)
            {
                LoadMemProfilerPlugin.Log.LogWarning("LoadMemProfiler failed to write profile: " + e);
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(PerformanceMonitor), nameof(PerformanceMonitor.BeginStream))]
        private static void BeginStream(Stream str)
        {
            Guarded(() => Session.SaveStream = str);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(PerformanceMonitor), nameof(PerformanceMonitor.EndStream))]
        private static void EndStream()
        {
            Guarded(() => Session.SaveStream = null);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(PerformanceMonitor), nameof(PerformanceMonitor.BeginData))]
        private static void BeginData(ESaveDataEntry entry)
        {
            Guarded(() => Session.Record("data_begin:" + entry, ""));
        }

        [HarmonyPostfix, HarmonyPatch(typeof(PerformanceMonitor), nameof(PerformanceMonitor.EndData))]
        private static void EndData(ESaveDataEntry entry)
        {
            Guarded(() => Session.Record("data_end:" + entry, ""));
        }

        [HarmonyPostfix, HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.Import))]
        private static void FactoryImported(PlanetFactory __instance)
        {
            Guarded(() => Session.Record("factory", string.Format("idx={0} astro={1} entities={2}",
                __instance.index,
                __instance.planet != null ? __instance.planet.astroId : 0,
                __instance.entityCursor)));
        }

        [HarmonyPostfix, HarmonyPatch(typeof(DysonSphere), nameof(DysonSphere.Import))]
        private static void DysonSphereImported(DysonSphere __instance)
        {
            Guarded(() => Session.Record("dyson_sphere",
                "star=" + (__instance.starData != null ? __instance.starData.index.ToString() : "?")));
        }
    }
}
