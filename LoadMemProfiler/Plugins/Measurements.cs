using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;

namespace LoadMemProfiler
{
    internal static class ScanSlice
    {
        internal static bool Step(IEnumerator scan)
        {
            long deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 1000;
            // Check after each small batch: paging/native calls can exceed this soft 1 ms budget.
            do { if (!scan.MoveNext()) return false; }
            while (Stopwatch.GetTimestamp() < deadline);
            return true;
        }
    }

    internal static class Tsv
    {
        internal static string Cell(string value) => (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        internal static string Number(double value) => value.ToString("F3", CultureInfo.InvariantCulture);
    }

    internal sealed class FrameWindow
    {
        // Percentiles retain at most the latest 4096 frames; counts/mean/max cover the whole interval.
        private readonly double[] _frames = new double[4096];
        private readonly double[] _sorted = new double[4096];
        private int _next;
        internal long Count;
        internal double SumMs, MaxMs, ObserverMs;

        internal void Add(double milliseconds)
        {
            _frames[_next] = milliseconds;
            _next = (_next + 1) % _frames.Length;
            Count++;
            SumMs += milliseconds;
            MaxMs = Math.Max(MaxMs, milliseconds);
        }

        internal string Columns(double ups)
        {
            int n = (int)Math.Min(Count, _frames.Length);
            Array.Copy(_frames, _sorted, n);
            Array.Sort(_sorted, 0, n);
            return Count + "\t" + n + "\t" + Tsv.Number(Count == 0 ? -1 : SumMs / Count) + "\t" +
                Tsv.Number(Percentile(n, 0.95)) + "\t" + Tsv.Number(Percentile(n, 0.99)) + "\t" +
                Tsv.Number(Count == 0 ? -1 : MaxMs) + "\t" + Tsv.Number(ups) + "\t" + Tsv.Number(ObserverMs);
        }

        private double Percentile(int n, double p) => n == 0 ? -1 : _sorted[(int)Math.Ceiling(n * p) - 1];

        internal void Reset()
        {
            Count = 0;
            _next = 0;
            SumMs = MaxMs = ObserverMs = 0;
        }
    }

    internal sealed class PathTotals
    {
        internal long Active, Inactive, Capacity, Used, BufferBytes, GeometryBytes, AuxiliaryBytes;
        internal long ActiveSlackBytes, InactiveBytes, MaxCapacity, MaxSlackBytes;

        internal void Add(bool active, int capacity, int length, int positions, int rotations, long auxiliaryBytes)
        {
            long geometry = positions * 12L + rotations * 16L;
            Capacity += capacity;
            BufferBytes += capacity;
            GeometryBytes += geometry;
            AuxiliaryBytes += auxiliaryBytes;
            MaxCapacity = Math.Max(MaxCapacity, capacity);
            if (active)
            {
                Active++;
                Used += length;
                long slack = Math.Max(0L, (long)capacity - length) +
                    Math.Max(0L, (long)positions - length) * 12 + Math.Max(0L, (long)rotations - length) * 16;
                ActiveSlackBytes += slack;
                MaxSlackBytes = Math.Max(MaxSlackBytes, slack);
            }
            else
            {
                Inactive++;
                InactiveBytes += capacity + geometry + auxiliaryBytes;
            }
        }
    }
}
