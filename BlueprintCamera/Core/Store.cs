using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace BlueprintCamera.Core;

[DataContract]
public sealed class Library
{
    [DataMember] public int Version = 1;
    [DataMember] public List<Shot> Shots = new List<Shot>();
    [DataMember] public List<Shot> Views = new List<Shot>();

    public void Validate()
    {
        if (Version != 1 || Shots == null || Views == null || Shots.Count > 128 || Views.Count > 256)
            throw new ArgumentException("摄影库版本或数量无效（最多 128 条路线、256 个收藏机位）。");
        foreach (var s in Shots) { if (s == null) throw new ArgumentException("空镜头。"); s.Validate(false); }
        foreach (var s in Views) { if (s == null || s.Frames == null || s.Frames.Count != 1) throw new ArgumentException("收藏机位无效。"); s.Validate(false); }
    }
}

public static class Store
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public static string Encode<T>(T value)
    {
        using (var stream = new MemoryStream())
        {
            new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    public static T Decode<T>(string json)
    {
        if (json == null || Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new ArgumentException("文件不得超过 2 MiB。");
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            return (T)new DataContractJsonSerializer(typeof(T), new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 100000 }).ReadObject(stream);
    }

    public static Library Load(string file)
    {
        if (!File.Exists(file)) return new Library();
        if (new FileInfo(file).Length > MaxBytes) throw new ArgumentException("摄影库文件过大。");
        var library = Decode<Library>(File.ReadAllText(file));
        if (library == null) throw new ArgumentException("摄影库为空。");
        library.Validate();
        return library;
    }

    public static void Save(string file, Library library)
    {
        library.Validate();
        string json = Encode(library);
        if (Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new ArgumentException("摄影库超过 2 MiB，请导出部分镜头后删除。");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
        string temp = file + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(false));
        if (File.Exists(file)) File.Replace(temp, file, null);
        else File.Move(temp, file);
    }
}
