using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using HarmonyLib;
using PoolTrim;
using UnityEngine;

internal static partial class Checks
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Geometry(string samples)
    {
        AuditReaders();
        var harmony = new Harmony("PoolTrim.Checks");
        // The only Unity-native boundary in path serialization is a raw memcpy. Substitute that
        // boundary so the real game's Import/Export bodies can run outside a Unity player.
        foreach (string name in new[] { "Import", "Export" })
            harmony.Patch(AccessTools.Method(typeof(CargoPath), name), transpiler: new HarmonyMethod(typeof(Checks), nameof(ManagedIO)));
        // Disabled mode must work with vanilla path methods and no geometry hooks installed.
        var patches = typeof(PoolTrimPlugin).Assembly.GetType("PoolTrim.Patches", true);
        var trimImport = patches.GetMethod("TrimAfterImport", BindingFlags.NonPublic | BindingFlags.Static);
        Require(!PoolTrimPlugin.ColdGeometry, "cold geometry starts disabled");
        var path = PathWithPoints(120);
        path.SetCapacity(4096);
        patches.GetMethod("LoadBegin", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        trimImport.Invoke(null, new object[] { path });
        Require(!IsCold(path) && path.buffer.Length == 120 && path.pointPos.Length == 120 && path.pointRot.Length == 120,
            "disabled cold storage still trims every path array without geometry hooks");
        Equal(path, Reload(Save(path)), "disabled cold storage retains vanilla save/reload");
        path.AddBuffer(5, 1, new Vector3[5], new Quaternion[5], Vector3.zero);
        Require(path.pathLength == 125, "disabled cold storage retains vanilla growth");
        patches.GetMethod("ReportAfterLoad", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { false, new IOException() });
        // Rendering methods require Unity's registered native calls even to JIT; check their IL
        // transformation, and detour/run path operations with the actual game assembly below.
        foreach (var method in GeometryPatches.Readers())
        {
            var code = GeometryPatches.ReadGeometry(PatchProcessor.GetOriginalInstructions(method)).ToList();
            Require(!code.Any(i => Equals(i.operand, GeometryPatches.PositionField) || Equals(i.operand, GeometryPatches.RotationField)),
                "every geometry field access is redirected in " + method);
        }
        GeometryPatches.InstallPaths(harmony);
        PoolTrimPlugin.ColdGeometry = true;
        GeometryBehavior();
        PoolTrimPlugin.ColdGeometry = false;
        if (samples != null) GeometrySamples(samples);
    }

    private static void AuditReaders()
    {
        var covered = new HashSet<MethodBase>(GeometryPatches.Readers());
        foreach (string name in new[] { "Import", "Export", "SetCapacity", "AddBuffer", "PathConcat", "PathCopy", "PathClose", "PathOpen", "Clear", "Free", "PresentCargos", "DrawDebugLine" })
            covered.Add(AccessTools.Method(typeof(CargoPath), name));
        int found = 0;
        var tokens = new HashSet<int>(covered.Select(m => m.MetadataToken));
        // Metadata inspection also covers Unity/Mono-only types that the desktop CLR cannot load.
        using (var module = Mono.Cecil.ModuleDefinition.ReadModule(typeof(CargoPath).Assembly.Location))
        foreach (var type in module.GetTypes())
        foreach (var method in type.Methods)
        {
            if (!method.HasBody) continue;
            bool touches = method.Body.Instructions.Any(i => i.Operand is Mono.Cecil.FieldReference f &&
                f.DeclaringType.FullName == "CargoPath" && (f.Name == "pointPos" || f.Name == "pointRot"));
            if (!touches) continue;
            found++;
            Require(tokens.Contains(method.MetadataToken.ToInt32()), "uncovered geometry access: " + type.Name + "." + method.Name);
        }
        Require(found == covered.Count, "every declared geometry patch still matches a game reader/writer");
        Console.WriteLine("PASS: complete game geometry field audit (" + found + " methods).");
    }

    private static IEnumerable<CodeInstruction> ManagedIO(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo m && m.DeclaringType == typeof(UnsafeIO) && m.IsGenericMethod)
                instruction.operand = AccessTools.Method(typeof(Checks), m.Name == "ReadMassive" ? nameof(ReadArray) : nameof(WriteArray))
                    .MakeGenericMethod(m.GetGenericArguments());
            yield return instruction;
        }
    }

    private static unsafe byte[] Bytes<T>(T[] values, int count) where T : unmanaged
    {
        var bytes = new byte[checked(count * sizeof(T))];
        fixed (T* source = values)
        fixed (byte* target = bytes)
            Buffer.MemoryCopy(source, target, bytes.Length, bytes.Length);
        return bytes;
    }

    private static void WriteArray<T>(Stream stream, T[] values, int count) where T : unmanaged
    {
        byte[] bytes = Bytes(values, count);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static unsafe void ReadArray<T>(Stream stream, T[] values, int count) where T : unmanaged
    {
        byte[] bytes = new BinaryReader(stream).ReadBytes(checked(count * sizeof(T)));
        Require(bytes.Length == count * sizeof(T), "fixture truncated");
        fixed (byte* source = bytes)
        fixed (T* target = values)
            Buffer.MemoryCopy(source, target, bytes.Length, bytes.Length);
    }

    private static CargoPath PathWithPoints(int count)
    {
        var cargo = (CargoContainer)FormatterServices.GetUninitializedObject(typeof(CargoContainer));
        cargo.cargoPool = new Cargo[1024];
        AccessTools.Field(typeof(CargoContainer), "poolCapacity").SetValue(cargo, 1024);
        AccessTools.Field(typeof(CargoContainer), "recycleIds").SetValue(cargo, new int[1024]);
        var path = new CargoPath(cargo) { id = 11 };
        var pos = Enumerable.Range(0, count).Select(i => new Vector3(i * 0.1f, 200f, 3f)).ToArray();
        var rot = Enumerable.Repeat(new Quaternion(0f, 0f, 0f, 1f), count).ToArray();
        path.AddBuffer(count, 1, pos, rot, Vector3.zero);
        path.belts.Add(123);
        return path;
    }

    private static byte[] Save(CargoPath path)
    {
        using (var stream = new MemoryStream())
        {
            path.Export(new BinaryWriter(stream));
            return stream.ToArray();
        }
    }

    private static CargoPath Reload(byte[] bytes)
    {
        var path = PathWithPoints(0);
        path.Import(new BinaryReader(new MemoryStream(bytes)));
        return path;
    }

    private static void Equal(CargoPath hot, CargoPath cold, string contract) => Require(Save(hot).SequenceEqual(Save(cold)), contract);
    private static bool IsCold(CargoPath path) => path.pointPos == null && path.pointRot == null;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static unsafe void GeometryBehavior()
    {
        var blocks = new List<(Quaternion[] Values, byte[] Packed)>();
        foreach (int count in new[] { 0, 1, 15, 16, 1023, 1024, 1025, 4096, 4097 })
        {
            var values = new Quaternion[count];
            var random = new System.Random(count);
            fixed (Quaternion* ptr = values)
            {
                uint* bits = (uint*)ptr;
                uint[] special = { 0, 0x80000000, 0x7f800000, 0xff800000, 0x7fc01234, 0xffcabcde, 0x00000001, 0x7fffffff };
                for (int i = 0; i < count * 4; i++)
                    bits[i] = i < special.Length ? special[i] : unchecked((uint)random.Next() << 1 | (uint)random.Next(2));
            }
            byte[] packed = PathGeometry.Encode(values, count);
            var restored = PathGeometry.Decode<Quaternion>(packed, count, count + 3);
            Require(Bytes(values, count).SequenceEqual(Bytes(restored, count)), "bit-exact codec across block boundaries and float edge cases");
            Require(Bytes(restored.Skip(count).ToArray(), 3).All(b => b == 0), "restored reserve is zeroed");
            blocks.Add((values, packed));
        }
        Parallel.For(0, 32, i => {
            var block = blocks[blocks.Count - 1 - i % blocks.Count];
            int count = block.Values.Length;
            var restored = PathGeometry.Decode<Quaternion>(block.Packed, count, count);
            byte[] packed = PathGeometry.Encode(restored, count);
            var again = PathGeometry.Decode<Quaternion>(packed, count, count);
            Require(Bytes(block.Values, count).SequenceEqual(Bytes(again, count)), "independent blocks decode out of order across threads and reused handles");
        });
        bool rejected = false;
        try { PathGeometry.Decode<Vector3>(Array.Empty<byte>(), 20, 20); } catch (InvalidDataException) { rejected = true; }
        Require(rejected, "truncated geometry is rejected");

        var original = PathWithPoints(2100);
        byte[] expected = Save(original);
        var path = Reload(expected);
        var buffer = path.buffer;
        var chunks = path.chunks;
        PathGeometry.Store(path);
        Require(IsCold(path) && path.buffer == buffer && path.chunks == chunks, "cold storage preserves simulation arrays and identity");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Require(Save(path).SequenceEqual(expected) && IsCold(path), "live path retains cold data across GC and exports byte-identically without warming");
        bool failed = false;
        try { path.Export(new BinaryWriter(new FailingStream())); } catch (IOException) { failed = true; }
        Require(failed && IsCold(path) && Save(path).SequenceEqual(expected), "failed export leaves cold data intact");
        Equal(original, Reload(Save(path)), "vanilla import reads cold exports");
        Parallel.For(0, 32, i => { if (i % 2 == 0) PathGeometry.Restore(path); else PathGeometry.ExportRotations(path); });
        Require(!IsCold(path) && Save(path).SequenceEqual(expected), "concurrent first access/export publishes complete exact geometry");

        foreach (Action<CargoPath> edit in new Action<CargoPath>[] {
            p => p.SetCapacity(p.buffer.Length + 20),
            p => p.AddBuffer(5, 1, new Vector3[5], new Quaternion[5], Vector3.zero),
            p => p.TruncBuffer(45),
            p => { p.PathClose(); p.PathOpen(13); },
            p => { p.Clear(); p.AddBuffer(5, 1, new Vector3[5], new Quaternion[5], Vector3.zero); }
        })
        {
            var hot = Reload(expected);
            var cold = Reload(expected);
            PathGeometry.Store(cold);
            edit(hot); edit(cold);
            Equal(hot, cold, "vanilla growth, extension, truncation, closed-loop rotation and reuse match hot paths");
        }
        foreach (bool copy in new[] { false, true })
        {
            var hot = Reload(expected); var cold = Reload(expected);
            var hotSource = Reload(expected); var coldSource = Reload(expected);
            PathGeometry.Store(cold); PathGeometry.Store(coldSource);
            if (copy) { hot.PathCopy(hotSource, 37); cold.PathCopy(coldSource, 37); }
            else { hot.PathConcat(hotSource); cold.PathConcat(coldSource); }
            Equal(hot, cold, "split/concat target matches hot geometry");
            Equal(hotSource, coldSource, "split/concat source matches hot geometry");
        }
        var hotLoop = PathWithPoints(120); hotLoop.PathClose();
        var coldLoop = PathWithPoints(120); coldLoop.PathClose(); PathGeometry.Store(coldLoop);
        Equal(hotLoop, coldLoop, "cold closed-loop export preserves its self connection");
        hotLoop.PathOpen(13); coldLoop.PathOpen(13);
        Equal(hotLoop, coldLoop, "opening an initially cold loop preserves point order and connections");
        path = PathWithPoints(120);
        var hotTransport = PathWithPoints(120);
        int cargoId = path.cargoContainer.AddCargo(1001, 1, 0);
        Require(path.TryInsertCargo(30, cargoId), "cargo fixture inserted");
        hotTransport.TryInsertCargo(30, hotTransport.cargoContainer.AddCargo(1001, 1, 0));
        PathGeometry.Store(path);
        for (int i = 0; i < 5; i++) { path.Update(); hotTransport.Update(); }
        Require(IsCold(path), "normal transport runs without restoring geometry");
        Equal(hotTransport, path, "hot and cold transport produce identical path state");
        path.PresentCargos();
        Require(!IsCold(path) && path.cargoContainer.cargoPool[cargoId].position.x > 0, "presentation restores cargo coordinates");
        path = Reload(expected); PathGeometry.Store(path); path.Free();
        Require(path.pointPos == null && path.pointRot == null && path.buffer == null, "free releases cold data without restoring");
        path = Reload(expected); PathGeometry.Store(path);
        path.Import(new BinaryReader(new MemoryStream(expected)));
        Require(Save(path).SequenceEqual(expected), "import replaces existing cold state");
        var patches = typeof(PoolTrimPlugin).Assembly.GetType("PoolTrim.Patches", true);
        var trimImport = patches.GetMethod("TrimAfterImport", BindingFlags.NonPublic | BindingFlags.Static);
        path = Reload(expected);
        trimImport.Invoke(null, new object[] { path });
        Require(!IsCold(path), "remote import outside full load stays hot");
        patches.GetMethod("LoadBegin", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        trimImport.Invoke(null, new object[] { path });
        Require(IsCold(path) && path.buffer.Length == path.pathLength, "full load trims then stores geometry cold");
        patches.GetMethod("ReportAfterLoad", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { false, new IOException() });
        path = PathWithPoints(0);
        trimImport.Invoke(null, new object[] { path });
        path.AddBuffer(5, 1, new Vector3[5], new Quaternion[5], Vector3.zero);
        Require(path.pathLength == 5, "empty trimmed path can regrow");
        Console.WriteLine("PASS: cold storage off/on, bit-exact codec, save/reload/failure, concurrent restoration, vanilla edits, transport, presentation and Free.");
    }

    private sealed class FailingStream : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (Length + count > 3000) throw new IOException("Deliberate save failure.");
            base.Write(buffer, offset, count);
        }
    }

    // Optional read-only save sample: repeated int32 point count, raw Vector3[], raw Quaternion[].
    private static void GeometrySamples(string filename)
    {
        int paths = 0;
        long raw = 0, packed = 0, encodeTicks = 0, decodeTicks = 0;
        var timer = new System.Diagnostics.Stopwatch();
        using (var reader = new BinaryReader(File.OpenRead(filename)))
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            int count = reader.ReadInt32();
            Require(count > 0 && count <= (reader.BaseStream.Length - reader.BaseStream.Position) / 28, "invalid sample length");
            var pos = new Vector3[count]; var rot = new Quaternion[count];
            ReadArray(reader.BaseStream, pos, count); ReadArray(reader.BaseStream, rot, count);
            timer.Restart();
            byte[] positions = PathGeometry.Encode(pos, count), rotations = PathGeometry.Encode(rot, count);
            encodeTicks += timer.ElapsedTicks;
            timer.Restart();
            var pos2 = PathGeometry.Decode<Vector3>(positions, count, count);
            var rot2 = PathGeometry.Decode<Quaternion>(rotations, count, count);
            decodeTicks += timer.ElapsedTicks;
            Require(Bytes(pos, count).SequenceEqual(Bytes(pos2, count)) && Bytes(rot, count).SequenceEqual(Bytes(rot2, count)), "real-save geometry roundtrip");
            long size = positions.LongLength + rotations.LongLength;
            raw += count * 28L;
            packed += count < 16 || count * 28L - size <= 128 ? count * 28L : size;
            paths++;
        }
        Console.WriteLine("PASS: real-save samples paths={0}, raw={1}, stored={2} ({3:F2}%), encode={4:F3}s, decode={5:F3}s; desktop CLR, payload only.",
            paths, raw, packed, packed * 100.0 / raw, encodeTicks / (double)System.Diagnostics.Stopwatch.Frequency, decodeTicks / (double)System.Diagnostics.Stopwatch.Frequency);
    }
}
