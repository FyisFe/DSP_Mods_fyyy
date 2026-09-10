using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace LogisticsProfiler
{
    internal sealed class Totals
    {
        internal long Samples, Ticks, MaxTicks, Skipped, Errors;
        internal long NoIdle, LowEnergy, EmptyPairs, Pairs, WorkingShips, LaunchDelta;
        internal long NoIdleTicks, LowEnergyTicks, EmptyPairTicks, NoLaunchTicks;
        internal long[] Details;

        internal void Add(Sample sample, long elapsed, bool original, bool error, int launchDelta)
        {
            Samples++;
            Ticks += elapsed;
            MaxTicks = Math.Max(MaxTicks, elapsed);
            if (!original) Skipped++;
            if (error) Errors++;
            if (sample.NoIdle) { NoIdle++; NoIdleTicks += elapsed; }
            if (sample.LowEnergy) { LowEnergy++; LowEnergyTicks += elapsed; }
            if (sample.Pairs == 0) { EmptyPairs++; EmptyPairTicks += elapsed; }
            if (launchDelta <= 0) NoLaunchTicks += elapsed;
            Pairs += sample.Pairs;
            WorkingShips += sample.WorkingShips;
            LaunchDelta += launchDelta;
            if (sample.Details != null)
            {
                if (Details == null) Details = new long[DispatchDetails.Names.Length];
                for (int i = 0; i < Details.Length; i++) Details[i] += sample.Details[i];
            }
        }

        internal string Columns(bool station, bool dispatch)
        {
            double ms = 1000.0 / Stopwatch.Frequency;
            return Samples + "\t" + Number(Ticks * ms) + "\t" +
                Number(Samples == 0 ? -1 : Ticks * ms / Samples) + "\t" +
                Number(Samples == 0 ? -1 : MaxTicks * ms) + "\t" + Skipped + "\t" + Errors + "\t" +
                (station ? NoIdle : -1) + "\t" + (station ? LowEnergy : -1) + "\t" +
                (dispatch ? EmptyPairs : -1) + "\t" + (dispatch ? Pairs : -1) + "\t" +
                (station ? WorkingShips : -1) + "\t" + (dispatch ? LaunchDelta : -1) + "\t" +
                Number(station ? NoIdleTicks * ms : -1) + "\t" + Number(station ? LowEnergyTicks * ms : -1) + "\t" +
                Number(dispatch ? EmptyPairTicks * ms : -1) + "\t" + Number(dispatch ? NoLaunchTicks * ms : -1) + "\t" +
                string.Join("\t", DispatchDetails.Names.Select((name, i) => Details == null ? -1 : Details[i]));
        }

        internal static string Number(double value) => value.ToString("F6", CultureInfo.InvariantCulture);
        internal static string Cell(string value) => (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }

    internal struct Sample
    {
        internal Capture Capture;
        internal long Started;
        // 0: scheduler; 1..6: dispatch priority 0..5; 7: flight update.
        internal int Kind, Phase, Gid, Planet, Pairs, WorkingShips;
        internal bool NoIdle, LowEnergy;
        internal long[] Details, PreviousDetails;
    }

    internal sealed class Capture
    {
        internal readonly int SampleEvery;
        internal readonly Totals[] Methods = NewTotals(8), Phases = NewTotals(60);
        internal readonly Totals Frames = new Totals();
        internal readonly Dictionary<(int Gid, int Planet, int Priority), Totals> Stations =
            new Dictionary<(int, int, int), Totals>();
        private readonly object _gate = new object();
        private bool _stopped;
        private int _active;
        [ThreadStatic] private static uint _random;

        internal Capture(int sampleEvery) { SampleEvery = sampleEvery; }

        internal static bool Choose(int every)
        {
            if (_random == 0) _random = (uint)Thread.CurrentThread.ManagedThreadId * 747796405u + 2891336453u;
            // Random sampling avoids locking onto a station/priority's periodic call position.
            _random ^= _random << 13;
            _random ^= _random >> 17;
            _random ^= _random << 5;
            return _random % (uint)every == 0;
        }

        internal bool Begin(ref Sample sample)
        {
            lock (_gate)
            {
                if (_stopped) return false;
                _active++;
                sample.Capture = this;
                return true;
            }
        }

        internal void End(Sample sample, long elapsed, bool original, bool error, int launchDelta)
        {
            // ponytail: sampled callbacks share one lock; use per-thread buffers if observer cost dominates at lower sampling rates.
            lock (_gate)
            {
                try
                {
                    Methods[sample.Kind].Add(sample, elapsed, original, error, launchDelta);
                    if (sample.Kind == 0) Phases[sample.Phase].Add(sample, elapsed, original, error, 0);
                    if (sample.Kind >= 1 && sample.Kind <= 6)
                    {
                        var key = (sample.Gid, sample.Planet, sample.Kind - 1);
                        if (!Stations.TryGetValue(key, out var totals)) Stations.Add(key, totals = new Totals());
                        totals.Add(sample, elapsed, original, error, launchDelta);
                    }
                }
                finally { _active--; }
            }
        }

        internal bool Stop()
        {
            lock (_gate)
            {
                _stopped = true;
                return _active == 0;
            }
        }

        private static Totals[] NewTotals(int count)
        {
            var result = new Totals[count];
            for (int i = 0; i < count; i++) result[i] = new Totals();
            return result;
        }
    }
}
