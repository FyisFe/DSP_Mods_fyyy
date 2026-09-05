using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using HarmonyLib;
using Microsoft.Win32.SafeHandles;
using UnityEngine;

[assembly: InternalsVisibleTo("Checks")]
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace PoolTrim
{
    internal static class PathGeometry
    {
        private sealed class Packed
        {
            internal int Count;
            internal byte[] Positions, Rotations;
        }

        private static readonly ConditionalWeakTable<CargoPath, Packed> Cold = new ConditionalWeakTable<CargoPath, Packed>();
        private const int BlockPoints = 1024;
        [ThreadStatic] private static Workspace _workspace;
        private static long _paths, _rawBytes, _packedBytes, _encodeTicks;

        internal static void Initialize() => WorkspaceForThread();
        private static Workspace WorkspaceForThread() => _workspace ?? (_workspace = new Workspace());

        internal static void ResetTotals() => _paths = _rawBytes = _packedBytes = _encodeTicks = 0;

        internal static string Totals() => string.Format(
            "PoolTrim: cold-stored {0} paths during import; geometry payload {1:F3} -> {2:F3} GB; encode {3:F3} s (before on-demand restores, excludes object overhead)",
            _paths, _rawBytes / 1e9, _packedBytes / 1e9, _encodeTicks / (double)Stopwatch.Frequency);

        // ponytail: only full-save import owns the arrays exclusively. Restored paths stay hot
        // until reload; re-cooling needs a separate lifetime boundary for worker-held array refs.
        internal static void Store(CargoPath path)
        {
            int count = path.pathLength;
            if (count < 16 || path.pointPos == null) return;
            if (count > path.pointPos.Length || count > path.pointRot.Length || count > path.buffer.Length)
                throw new InvalidOperationException("Unexpected CargoPath geometry length.");
            long started = Stopwatch.GetTimestamp();
            var packed = new Packed {
                Count = count,
                Positions = Encode(path.pointPos, count),
                Rotations = Encode(path.pointRot, count)
            };
            Interlocked.Add(ref _encodeTicks, Stopwatch.GetTimestamp() - started);
            long raw = count * 28L;
            long bytes = packed.Positions.LongLength + packed.Rotations.LongLength;
            // Small/incompressible paths must also pay for the side table and extra objects.
            if (raw - bytes <= 128) return;
            Cold.Add(path, packed);
            path.pointPos = null;
            path.pointRot = null;
            Interlocked.Increment(ref _paths);
            Interlocked.Add(ref _rawBytes, raw);
            Interlocked.Add(ref _packedBytes, bytes);
        }

        internal static void Restore(CargoPath path)
        {
            if (Volatile.Read(ref path.pointPos) != null) return;
            lock (path)
            {
                if (!Cold.TryGetValue(path, out var packed)) return;
                var positions = Decode<Vector3>(packed.Positions, packed.Count, path.buffer.Length);
                var rotations = Decode<Quaternion>(packed.Rotations, packed.Count, path.buffer.Length);
                // Publish only after both decodes succeed; pointPos is the ready flag for readers.
                path.pointRot = rotations;
                Volatile.Write(ref path.pointPos, positions);
                Cold.Remove(path);
            }
        }

        internal static Vector3[] Positions(CargoPath path) { Restore(path); return path.pointPos; }
        internal static Quaternion[] Rotations(CargoPath path) { Restore(path); return path.pointRot; }

        // Export owns temporary arrays only. Saving neither warms the world nor changes its format.
        internal static Vector3[] ExportPositions(CargoPath path)
        {
            lock (path)
                return Cold.TryGetValue(path, out var packed)
                    ? Decode<Vector3>(packed.Positions, packed.Count, packed.Count) : path.pointPos;
        }

        internal static Quaternion[] ExportRotations(CargoPath path)
        {
            lock (path)
                return Cold.TryGetValue(path, out var packed)
                    ? Decode<Quaternion>(packed.Rotations, packed.Count, packed.Count) : path.pointRot;
        }

        internal static void Discard(CargoPath path)
        {
            if (!Cold.Remove(path)) return;
            // Free calls Clear before releasing its arrays; it needs non-null arrays, not geometry.
            path.pointPos = Array.Empty<Vector3>();
            path.pointRot = Array.Empty<Quaternion>();
        }

        // Subtract bit patterns, never floats: every NaN, signed zero and mantissa bit survives.
        // Unity's Mono DeflateStream ignores CompressionLevel.Fastest; use Windows XPRESS_HUFF.
        internal static unsafe byte[] Encode<T>(T[] values, int count) where T : unmanaged
        {
            if (count < 0 || count > values.Length) throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0) return Array.Empty<byte>();
            int width = sizeof(T) / 4;
            int length = checked(count * sizeof(T));
            var workspace = WorkspaceForThread();
            byte[] input = workspace.Input(length);
            fixed (T* source = values)
            {
                var words = (uint*)source;
                for (int first = 0; first < count; first += BlockPoints)
                {
                    int n = Math.Min(BlockPoints, count - first) * width;
                    int offset = first * width;
                    int start = first * sizeof(T);
                    for (int i = 0; i < n; i++)
                    {
                        int at = offset + i;
                        uint delta = unchecked(words[at] - (at < width ? 0 : words[at - width]));
                        input[start + i] = (byte)delta;
                        input[start + n + i] = (byte)(delta >> 8);
                        input[start + n * 2 + i] = (byte)(delta >> 16);
                        input[start + n * 3 + i] = (byte)(delta >> 24);
                    }
                }
            }
            byte[] output = workspace.Output(length);
            UIntPtr written;
            while (true)
            {
                fixed (byte* src = input)
                fixed (byte* dst = output)
                    if (Compress(workspace.Encoder, src, (UIntPtr)(uint)length, dst, (UIntPtr)(uint)output.Length, out written)) break;
                int error = Marshal.GetLastWin32Error();
                if (error != 122) throw new Win32Exception(error); // ERROR_INSUFFICIENT_BUFFER supplies the required bound.
                output = new byte[checked((int)written.ToUInt64())];
            }
            var result = new byte[checked((int)written.ToUInt64())];
            Buffer.BlockCopy(output, 0, result, 0, result.Length);
            return result;
        }

        internal static unsafe T[] Decode<T>(byte[] bytes, int count, int capacity) where T : unmanaged
        {
            if (count < 0 || count > capacity) throw new InvalidDataException("Invalid cold path geometry capacity.");
            var values = new T[capacity];
            if (count == 0)
            {
                if (bytes.Length != 0) throw new InvalidDataException("Unexpected empty path geometry.");
                return values;
            }
            int width = sizeof(T) / 4;
            int length = checked(count * sizeof(T));
            var workspace = WorkspaceForThread();
            byte[] input = workspace.Input(length);
            fixed (byte* src = bytes)
            fixed (byte* dst = input)
            {
                if (!Decompress(workspace.Decoder, src, (UIntPtr)(uint)bytes.Length, dst, (UIntPtr)(uint)length, out var written))
                    throw new InvalidDataException("Could not decode cold path geometry.", new Win32Exception(Marshal.GetLastWin32Error()));
                if (written.ToUInt64() != (ulong)length) throw new InvalidDataException("Unexpected cold path geometry length.");
            }
            fixed (T* target = values)
            {
                var words = (uint*)target;
                for (int first = 0; first < count; first += BlockPoints)
                {
                    int n = Math.Min(BlockPoints, count - first) * width;
                    int offset = first * width;
                    int start = first * sizeof(T);
                    for (int i = 0; i < n; i++)
                    {
                        uint delta = (uint)(input[start + i] | input[start + n + i] << 8 |
                            input[start + n * 2 + i] << 16 | input[start + n * 3 + i] << 24);
                        int at = offset + i;
                        words[at] = unchecked(delta + (at < width ? 0 : words[at - width]));
                    }
                }
            }
            return values;
        }

        private sealed class Workspace
        {
            internal readonly CodecHandle Encoder = new CodecHandle(false), Decoder = new CodecHandle(true);
            // Reuse ordinary path buffers; rare long paths must not enlarge thread-lifetime caches.
            private readonly byte[] _input = new byte[65536], _output = new byte[65536];
            internal byte[] Input(int length) => length <= _input.Length ? _input : new byte[length];
            internal byte[] Output(int length) => length <= _output.Length ? _output : new byte[length];
        }

        private sealed class CodecHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            private readonly bool _decode;
            internal CodecHandle(bool decode) : base(true)
            {
                _decode = decode;
                IntPtr value;
                bool created = decode ? CreateDecompressor(4, IntPtr.Zero, out value) : CreateCompressor(4, IntPtr.Zero, out value);
                if (!created) throw new Win32Exception(Marshal.GetLastWin32Error());
                SetHandle(value);
            }
            protected override bool ReleaseHandle() => _decode ? CloseDecompressor(handle) : CloseCompressor(handle);
        }

        // Buffer mode carries independent block framing. SIZE_T follows pointer width on Windows.
        [DllImport("cabinet.dll", SetLastError = true)]
        private static extern bool CreateCompressor(uint algorithm, IntPtr allocator, out IntPtr handle);
        [DllImport("cabinet.dll", SetLastError = true)]
        private static extern bool CreateDecompressor(uint algorithm, IntPtr allocator, out IntPtr handle);
        [DllImport("cabinet.dll", SetLastError = true)]
        private static extern bool CloseCompressor(IntPtr handle);
        [DllImport("cabinet.dll", SetLastError = true)]
        private static extern bool CloseDecompressor(IntPtr handle);
        [DllImport("cabinet.dll", SetLastError = true)]
        private static extern unsafe bool Compress(CodecHandle handle, byte* input, UIntPtr length, byte* output, UIntPtr capacity, out UIntPtr written);
        [DllImport("cabinet.dll", SetLastError = true)]
        private static extern unsafe bool Decompress(CodecHandle handle, byte* input, UIntPtr length, byte* output, UIntPtr capacity, out UIntPtr written);
    }

    internal static class GeometryPatches
    {
        internal static readonly FieldInfo PositionField = AccessTools.Field(typeof(CargoPath), nameof(CargoPath.pointPos));
        internal static readonly FieldInfo RotationField = AccessTools.Field(typeof(CargoPath), nameof(CargoPath.pointRot));

        internal static void Install(Harmony harmony)
        {
            foreach (var method in Readers())
                harmony.Patch(method, transpiler: Patch(nameof(ReadGeometry)));
            InstallPaths(harmony);
        }

        internal static void InstallPaths(Harmony harmony)
        {
            foreach (string name in new[] { "SetCapacity", "AddBuffer", "TruncBuffer", "PathClose", "PathOpen", "Clear", "PresentCargos" })
                harmony.Patch(AccessTools.Method(typeof(CargoPath), name), prefix: Patch(nameof(Restore)));
            harmony.Patch(AccessTools.Method(typeof(CargoPath), "PathConcat"), prefix: Patch(nameof(RestorePair)));
            harmony.Patch(AccessTools.Method(typeof(CargoPath), "PathCopy"), prefix: Patch(nameof(RestorePair)));
            harmony.Patch(AccessTools.Method(typeof(CargoPath), "Free"), prefix: Patch(nameof(Discard)));
            harmony.Patch(AccessTools.Method(typeof(CargoPath), "Export"), transpiler: Patch(nameof(ReadExport)));

            // LossyCompression can skip vanilla Export; its encoder also needs temporary decoded arrays.
            var lossy = AccessTools.Method("LossyCompression.CargoPathCompress:Encode");
            if (lossy != null) harmony.Patch(lossy, transpiler: Patch(nameof(ReadExport)));
        }

        private static HarmonyMethod Patch(string name) => new HarmonyMethod(typeof(GeometryPatches), name);

        internal static IEnumerable<MethodBase> Readers()
        {
            yield return AccessTools.Method(typeof(CargoPath), "DrawDebugLine");
            foreach (string name in new[] { "GeneratePathGeometry", "AlterBeltRenderer", "AlterPathRenderer", "SetBeltState" })
                yield return AccessTools.Method(typeof(CargoTraffic), name);
            yield return AccessTools.Method(typeof(PlanetFactory), "OnBeltBuilt");
            yield return AccessTools.Method(typeof(BuildTool_Click), "MatchInserter");
            yield return AccessTools.Method(typeof(BuildTool_BlueprintPaste), "MatchInserter");
            yield return AccessTools.Method(typeof(BuildTool_Addon), "SnapToBelt");
            yield return AccessTools.Method(typeof(BuildTool_Inserter), "DeterminePreviews");
            yield return AccessTools.Method(typeof(UIBeltWindow), "_OnUpdate");
        }

        private static void Restore(CargoPath __instance) => PathGeometry.Restore(__instance);
        private static void RestorePair(CargoPath __instance, CargoPath __0)
        {
            PathGeometry.Restore(__instance);
            PathGeometry.Restore(__0);
        }
        private static void Discard(CargoPath __instance) => PathGeometry.Discard(__instance);

        internal static IEnumerable<CodeInstruction> ReadGeometry(IEnumerable<CodeInstruction> instructions) => ReplaceReads(instructions, false);
        private static IEnumerable<CodeInstruction> ReadExport(IEnumerable<CodeInstruction> instructions) => ReplaceReads(instructions, true);

        private static IEnumerable<CodeInstruction> ReplaceReads(IEnumerable<CodeInstruction> instructions, bool export)
        {
            int replaced = 0;
            foreach (var instruction in instructions)
            {
                string name = instruction.LoadsField(PositionField) ? "Positions" :
                    instruction.LoadsField(RotationField) ? "Rotations" : null;
                if (name != null)
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(PathGeometry), (export ? "Export" : "") + name);
                    replaced++;
                }
                yield return instruction;
            }
            if (replaced == 0) throw new InvalidOperationException("CargoPath geometry reader changed; refusing to enable cold storage.");
        }
    }
}
