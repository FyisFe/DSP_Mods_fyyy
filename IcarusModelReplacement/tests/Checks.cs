using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using IcarusModelReplacement;

static class Checks
{
    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static void Main(string[] args)
    {
        Require(args.Length == 2, "Pass a model directory and the built Mod DLL.");
        string modDirectory = Path.GetDirectoryName(Path.GetFullPath(args[1]));
        Require(File.Exists(args[1]), "Missing Mod DLL");
        var builtin = ModelPack.Load("builtin", Path.GetTempPath(), modDirectory);
        Require(builtin.Info.Name == "Cute Gugugaga", "Bundled Gugugaga must load beside the Mod DLL");
        Require(File.Exists(Path.Combine(modDirectory, "model", "README.md")), "Missing bundled model attribution");
        var real = ModelPack.Load(Path.GetFullPath(args[0]), Path.GetTempPath(), modDirectory);
        Console.WriteLine($"PASS: bundled Gugugaga and absolute model path '{real.Info.Name}', {real.Info.Bones.Length} bones, {real.Indices.Length/3:N0} triangles.");
        for (int i = 0; i <= 100; i++)
        {
            double phase = i * Math.PI / 50;
            var idle = Motion.Sample(phase, 0, 0, 0);
            var walk = Motion.Sample(phase, 1, 0, 0);
            var opposite = Motion.Sample(phase + Math.PI, 1, 0, 0);
            var repeat = Motion.Sample(phase + 2 * Math.PI, 1, 0, 0);
            var flight = Motion.Sample(phase, 1, 1, 1);
            Require(idle.Get(Signal.Constant) == 1 && idle.Get(Signal.Stride) == 0 && idle.Get(Signal.Step) == 0, "idle");
            Require(flight.Get(Signal.Stride) == 0 && flight.Get(Signal.Air) == 1 && flight.Get(Signal.Sail) == 1, "flight");
            Require(Math.Abs(walk.Get(Signal.LeftStep) - opposite.Get(Signal.RightStep)) < .00001f, "left/right symmetry");
            Require(walk.Get(Signal.LeftStep) * walk.Get(Signal.RightStep) == 0, "alternating feet");
            Require(Math.Abs(walk.Get(Signal.Stride) - repeat.Get(Signal.Stride)) < .00001f, "continuous loop");
        }
        var blend = Motion.Sample(Math.PI/2, 1, .5f, .25f);
        Require(blend.Get(Signal.Stride) == .5f && blend.Get(Signal.Air) == .5f && blend.Get(Signal.Sail) == .25f, "state blending");
        string directory = Path.Combine(Path.GetTempPath(), "icarus-model-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (int count in new[] { 1, 3, 256 })
            {
                var info = TinyInfo(count);
                WriteInfo(directory, info);
                WriteMesh(directory, count);
                File.WriteAllBytes(Path.Combine(directory, "texture.png"), Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j5ZkAAAAASUVORK5CYII="));
                var pack = ModelPack.Load(Path.GetFileName(directory), Path.GetDirectoryName(directory), modDirectory);
                Require(pack.Info.Bones.Length == count && pack.Info.Motions[0].Index == count - 1
                    && pack.Info.Motions[1].Index == count, "arbitrary rig and root target");
            }
            var baseline = TinyInfo(3);
            WriteInfo(directory, baseline);
            WriteMesh(directory, 3);
            BadInfo(directory, p => p.Format = 99);
            BadInfo(directory, p => p.Bones[0].Parent = 2);
            BadInfo(directory, p => p.Bones[1].Name = p.Bones[0].Name);
            BadInfo(directory, p => p.Scale = 0);
            BadInfo(directory, p => p.Offset = new[] { 1f, 2f });
            BadInfo(directory, p => p.Motions[0].Target = "missing");
            BadInfo(directory, p => p.Motions[0].Signal = "42");
            BadInfo(directory, p => p.Bones = null);
            WriteInfo(directory, baseline);
            byte[] original;
            using (var file = File.OpenRead(Path.Combine(directory, "mesh.bin.gz")))
            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            using (var bytes = new MemoryStream()) { gzip.CopyTo(bytes); original = bytes.ToArray(); }
            BadMesh(directory, original, bytes => Array.Copy(BitConverter.GetBytes(int.MaxValue), 0, bytes, 8, 4));
            BadMesh(directory, original, bytes => Array.Copy(BitConverter.GetBytes(float.NaN), 0, bytes, 16, 4));
            BadMesh(directory, original, bytes => bytes[16+32] = 3);
            BadMesh(directory, original, bytes => Array.Copy(BitConverter.GetBytes(.2f), 0, bytes, 16+36, 4));
            BadMesh(directory, original, bytes => Array.Copy(BitConverter.GetBytes(999), 0, bytes, 16+3*52, 4));
            WriteCompressed(directory, new byte[10]);
            Reject(() => ModelPack.Load(directory), "truncated mesh");
            var trailing = new byte[original.Length + 1];
            Array.Copy(original, trailing, original.Length);
            WriteCompressed(directory, trailing);
            Reject(() => ModelPack.Load(directory), "trailing mesh data");
            WriteCompressed(directory, original);
            string texture = Path.Combine(directory, "texture.png");
            var png = File.ReadAllBytes(texture);
            png[16] = 127;
            File.WriteAllBytes(texture, png);
            Reject(() => ModelPack.Load(directory), "oversized decoded texture");
            File.Delete(texture);
            Reject(() => ModelPack.Load(directory), "missing texture");
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine("PASS: motion signals, 1/3/256-bone packs, root bindings, malformed metadata, size limits, NaN, indices, weights, truncated/trailing data and missing assets.");
    }

    static PackInfo TinyInfo(int count)
    {
        var bones = new BoneInfo[count];
        for (int i = 0; i < count; i++) bones[i] = new BoneInfo { Name = "Joint" + i, Parent = i - 1, Position = new[] { 0f, i*.01f, 0f } };
        return new PackInfo
        {
            Format = 1, Name = "Independent rig", Author = "Test", License = "CC0", Scale = 1, Bones = bones,
            Motions = new[] {
                new MotionInfo { Target = "Joint" + (count - 1), Signal = "Stride", Rotation = new[] { 23f, 0, 0 } },
                new MotionInfo { Target = "$root", Signal = "Sail", Rotation = new[] { 0f, 17, 0 } }
            }
        };
    }

    static void WriteInfo(string directory, PackInfo info)
    {
        using (var file = File.Create(Path.Combine(directory, "model.json")))
            new DataContractJsonSerializer(typeof(PackInfo)).WriteObject(file, info);
    }

    static void WriteMesh(string directory, int bones)
    {
        using (var bytes = new MemoryStream())
        {
            using (var w = new BinaryWriter(bytes, System.Text.Encoding.UTF8, true))
            {
                w.Write(0x444D5249); w.Write(1); w.Write(3); w.Write(3);
                for (int i = 0; i < 3; i++)
                {
                    w.Write(i == 1 ? 1f : 0f); w.Write(i == 2 ? 1f : 0f); w.Write(0f);
                    w.Write(0f); w.Write(0f); w.Write(-1f); w.Write(0f); w.Write(0f);
                    w.Write((byte)(bones-1)); w.Write(new byte[3]);
                    w.Write(1f); w.Write(0f); w.Write(0f); w.Write(0f);
                }
                w.Write(0); w.Write(1); w.Write(2);
            }
            WriteCompressed(directory, bytes.ToArray());
        }
    }

    static void WriteCompressed(string directory, byte[] bytes)
    {
        using (var file = File.Create(Path.Combine(directory, "mesh.bin.gz")))
        using (var gzip = new GZipStream(file, CompressionMode.Compress)) gzip.Write(bytes, 0, bytes.Length);
    }

    static void BadInfo(string directory, Action<PackInfo> change)
    {
        var info = TinyInfo(3); change(info); WriteInfo(directory, info);
        Reject(() => ModelPack.Load(directory), "invalid manifest");
    }

    static void BadMesh(string directory, byte[] baseline, Action<byte[]> change)
    {
        var bytes = (byte[])baseline.Clone(); change(bytes); WriteCompressed(directory, bytes);
        Reject(() => ModelPack.Load(directory), "invalid mesh");
    }

    static void Reject(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        catch (IOException) { return; }
        catch (SerializationException) { return; }
        throw new Exception("Accepted " + message);
    }
}
