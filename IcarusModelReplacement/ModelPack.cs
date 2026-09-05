using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace IcarusModelReplacement;

[DataContract]
internal sealed class PackInfo
{
    [DataMember(Name = "format", IsRequired = true)] public int Format { get; set; }
    [DataMember(Name = "name", IsRequired = true)] public string Name { get; set; }
    [DataMember(Name = "author", IsRequired = true)] public string Author { get; set; }
    [DataMember(Name = "license", IsRequired = true)] public string License { get; set; }
    [DataMember(Name = "source")] public string Source { get; set; }
    [DataMember(Name = "scale", IsRequired = true)] public float Scale { get; set; }
    [DataMember(Name = "offset")] public float[] Offset { get; set; }
    [DataMember(Name = "rotation")] public float[] Rotation { get; set; }
    [DataMember(Name = "boundsPadding")] public float BoundsPadding { get; set; }
    [DataMember(Name = "material")] public MaterialInfo Material { get; set; }
    [DataMember(Name = "bones", IsRequired = true)] public BoneInfo[] Bones { get; set; }
    [DataMember(Name = "motions")] public MotionInfo[] Motions { get; set; }
}

[DataContract]
internal sealed class BoneInfo
{
    [DataMember(Name = "name", IsRequired = true)] public string Name { get; set; }
    [DataMember(Name = "parent", IsRequired = true)] public int Parent { get; set; }
    [DataMember(Name = "position", IsRequired = true)] public float[] Position { get; set; }
}

[DataContract]
internal sealed class MaterialInfo
{
    [DataMember(Name = "smoothness")] public float Smoothness { get; set; }
    [DataMember(Name = "metallic")] public float Metallic { get; set; }
    [DataMember(Name = "emission")] public float Emission { get; set; }
    [DataMember(Name = "specularHighlights")] public bool SpecularHighlights { get; set; }
}

[DataContract]
internal sealed class MotionInfo
{
    [DataMember(Name = "target", IsRequired = true)] public string Target { get; set; }
    [DataMember(Name = "signal", IsRequired = true)] public string Signal { get; set; }
    [DataMember(Name = "position")] public float[] Position { get; set; }
    [DataMember(Name = "rotation")] public float[] Rotation { get; set; }
    public int Index;
    public Signal Kind;
}

internal struct Vertex
{
    public float X, Y, Z, Nx, Ny, Nz, U, V;
    public byte B0, B1, B2, B3;
    public float W0, W1, W2, W3;
}

internal sealed class ModelPack
{
    public PackInfo Info;
    public Vertex[] Vertices;
    public int[] Indices;
    public byte[] Texture;

    // Fixed asset names keep untrusted manifests from requesting unrelated files.
    public static ModelPack Load(string directory)
    {
        var pack = new ModelPack();
        using (var file = Open(directory, "model.json", 1024 * 1024))
            pack.Info = (PackInfo)new DataContractJsonSerializer(typeof(PackInfo),
                new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 20000 }).ReadObject(file);
        pack.ValidateInfo();
        using (var file = Open(directory, "mesh.bin.gz", 32 * 1024 * 1024))
        using (var gzip = new GZipStream(file, CompressionMode.Decompress))
        using (var reader = new BinaryReader(gzip))
            pack.ReadMesh(reader);
        using (var file = Open(directory, "texture.png", 64 * 1024 * 1024))
        using (var reader = new BinaryReader(file))
            pack.Texture = reader.ReadBytes((int)file.Length);
        pack.ValidateTexture();
        return pack;
    }

    private static FileStream Open(string directory, string name, long limit)
    {
        string path = Path.Combine(directory, name);
        Require((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0, "Asset must be a regular file: " + name);
        var stream = File.OpenRead(path);
        if (stream.Length > 0 && stream.Length <= limit) return stream;
        stream.Dispose();
        throw new InvalidDataException("Asset size limit exceeded or empty: " + name);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static void Range(float value, float min, float max, string name) =>
        Require(Finite(value) && value >= min && value <= max, "Invalid " + name);

    private static float[] Vector(float[] value, bool required = false)
    {
        if (value == null && !required) return new float[3];
        Require(value != null && value.Length == 3, "Expected a three-component vector");
        foreach (float component in value) Range(component, -1000, 1000, "vector component");
        return value;
    }

    private void ValidateInfo()
    {
        Require(Info != null && Info.Format == 1, "Unsupported model format");
        Require(!string.IsNullOrWhiteSpace(Info.Name) && !string.IsNullOrWhiteSpace(Info.Author)
            && !string.IsNullOrWhiteSpace(Info.License), "Model name, author and license are required");
        Range(Info.Scale, .001f, 100, "scale");
        Range(Info.BoundsPadding, 0, 100, "bounds padding");
        Info.Offset = Vector(Info.Offset);
        Info.Rotation = Vector(Info.Rotation);
        Info.Material = Info.Material ?? new MaterialInfo();
        Range(Info.Material.Smoothness, 0, 1, "smoothness");
        Range(Info.Material.Metallic, 0, 1, "metallic");
        Range(Info.Material.Emission, 0, 2, "emission");
        Require(Info.Bones != null && Info.Bones.Length >= 1 && Info.Bones.Length <= 256, "Bone count must be 1..256");
        var names = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < Info.Bones.Length; i++)
        {
            var bone = Info.Bones[i];
            Require(bone != null && !string.IsNullOrWhiteSpace(bone.Name) && bone.Name.Length <= 128
                && bone.Name != "$root" && !names.ContainsKey(bone.Name), "Invalid or duplicate bone name");
            Require(bone.Parent >= -1 && bone.Parent < i, "Bone parents must precede children");
            bone.Position = Vector(bone.Position, true);
            names.Add(bone.Name, i);
        }
        names.Add("$root", Info.Bones.Length);
        Info.Motions = Info.Motions ?? Array.Empty<MotionInfo>();
        Require(Info.Motions.Length <= 1024, "Too many motion bindings");
        foreach (var motion in Info.Motions)
        {
            Require(motion != null && motion.Target != null && names.TryGetValue(motion.Target, out motion.Index), "Unknown motion target");
            Require(Enum.TryParse(motion.Signal, out motion.Kind) && Enum.GetName(typeof(Signal), motion.Kind) == motion.Signal, "Unknown motion signal");
            motion.Position = Vector(motion.Position);
            motion.Rotation = Vector(motion.Rotation);
        }
    }

    private static float ReadFloat(BinaryReader reader)
    {
        float value = reader.ReadSingle();
        Require(Finite(value), "Non-finite mesh value");
        return value;
    }

    private void ReadMesh(BinaryReader reader)
    {
        Require(reader.ReadUInt32() == 0x444D5249 && reader.ReadInt32() == 1, "Unsupported mesh format");
        int count = reader.ReadInt32(), indices = reader.ReadInt32();
        // Validate before allocating; compressed files can claim enormous meshes.
        Require(count >= 3 && count <= 250000 && indices >= 3 && indices <= 1500000 && indices % 3 == 0, "Mesh size limit exceeded");
        Vertices = new Vertex[count];
        for (int i = 0; i < count; i++)
        {
            var v = new Vertex
            {
                X = ReadFloat(reader), Y = ReadFloat(reader), Z = ReadFloat(reader),
                Nx = ReadFloat(reader), Ny = ReadFloat(reader), Nz = ReadFloat(reader),
                U = ReadFloat(reader), V = ReadFloat(reader),
                B0 = reader.ReadByte(), B1 = reader.ReadByte(), B2 = reader.ReadByte(), B3 = reader.ReadByte(),
                W0 = ReadFloat(reader), W1 = ReadFloat(reader), W2 = ReadFloat(reader), W3 = ReadFloat(reader)
            };
            Require(Math.Abs(v.X) <= 1000 && Math.Abs(v.Y) <= 1000 && Math.Abs(v.Z) <= 1000, "Mesh coordinate out of range");
            Require(Math.Abs(v.Nx*v.Nx + v.Ny*v.Ny + v.Nz*v.Nz - 1) < .01f, "Mesh normals must be unit length");
            Require(v.U >= 0 && v.U <= 1 && v.V >= 0 && v.V <= 1, "Atlas UV out of range");
            Require(v.B0 < Info.Bones.Length && v.B1 < Info.Bones.Length && v.B2 < Info.Bones.Length && v.B3 < Info.Bones.Length, "Mesh bone index out of range");
            Require(v.W0 >= v.W1 && v.W1 >= v.W2 && v.W2 >= v.W3 && v.W3 >= 0
                && Math.Abs(v.W0 + v.W1 + v.W2 + v.W3 - 1) < .00001f, "Skin weights must be sorted and normalized");
            Vertices[i] = v;
        }
        Indices = new int[indices];
        for (int i = 0; i < indices; i++)
        {
            Indices[i] = reader.ReadInt32();
            Require(Indices[i] >= 0 && Indices[i] < count, "Triangle index out of range");
        }
        Require(reader.BaseStream.ReadByte() == -1, "Unexpected trailing mesh data");
    }

    private void ValidateTexture()
    {
        byte[] header = { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82 };
        Require(Texture.Length >= 33, "Truncated PNG");
        for (int i = 0; i < header.Length; i++) Require(Texture[i] == header[i], "Expected PNG with IHDR");
        uint width = BigEndian(16), height = BigEndian(20);
        Require(width > 0 && height > 0 && width <= 8192 && height <= 8192
            && (ulong)width * height <= 16777216, "Texture exceeds 16 megapixels");
    }

    private uint BigEndian(int at) => (uint)(Texture[at] << 24 | Texture[at+1] << 16 | Texture[at+2] << 8 | Texture[at+3]);
}
