using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

internal static partial class Checks
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Main(string[] args)
    {
        if (args.Length < 2 || args.Length > 3)
            throw new ArgumentException("Usage: Checks <game-managed-dir> <bepinex-core-dir> [geometry-samples.bin]");
        AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
        {
            string name = new AssemblyName(e.Name).Name + ".dll";
            foreach (string dir in args.Take(2))
            {
                string path = Path.Combine(dir, name);
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        };
        Run();
        Geometry(args.Length == 3 ? args[2] : null);
    }

    private static FieldInfo Field(string name) => typeof(PlanetFactory).GetField(name, Fields);
    private static int Capacity(PlanetFactory f) => (int)Field("entityCapacity").GetValue(f);
    private static int[] Recycle(PlanetFactory f) => (int[])Field("entityRecycle").GetValue(f);
    private static int Recycled(PlanetFactory f) => (int)Field("entityRecycleCursor").GetValue(f);

    private static PlanetFactory Factory(int capacity, int cursor)
    {
        var f = new PlanetFactory();
        typeof(PlanetFactory).GetProperty("planet").SetValue(f, new PlanetData { id = 101 });
        typeof(PlanetFactory).GetMethod("SetEntityCapacity", Fields).Invoke(f, new object[] { capacity });
        f.entityCursor = cursor;
        for (int i = 1; i < cursor; i++)
        {
            f.entityPool[i] = new EntityData { id = i, protoId = 2001, pos = new Vector3(i, 200, -i), rot = new Quaternion(0, 0, 0, 1) };
            f.entityAnimPool[i] = new AnimData { time = i + 0.25f, state = (uint)i, power = 0.75f };
            f.entitySignPool[i] = new SignData { iconId0 = (uint)i, count0 = i + 0.5f };
            f.entityMutexs[i] = new Mutex(i);
            f.entityNeeds[i] = new[] { i, i + 1 };
            for (int slot = 0; slot < 16; slot++) f.entityConnPool[i * 16 + slot] = i * 16 + slot;
        }
        return f;
    }

    private static Array[] Arrays(PlanetFactory f) => new Array[] {
        f.entityPool, f.entityAnimPool, f.entitySignPool, f.entityConnPool, f.entityMutexs, f.entityNeeds, Recycle(f)
    };

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run()
    {
        var patches = Assembly.Load("PoolTrim").GetType("PoolTrim.Patches", true);
        var trim = (Func<PlanetFactory, int>)Delegate.CreateDelegate(typeof(Func<PlanetFactory, int>),
            patches.GetMethod("TrimEntities", BindingFlags.Static | BindingFlags.NonPublic));
        var import = patches.GetMethod("TrimFactoryAfterImport", BindingFlags.Static | BindingFlags.NonPublic);
        var isolated = Factory(4096, 64);
        import.Invoke(null, new object[] { isolated });
        Require(Capacity(isolated) == 4096, "remote import outside a full load does not trim");
        patches.GetMethod("LoadBegin", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        import.Invoke(null, new object[] { isolated });
        Require(Capacity(isolated) == 1088, "full-load factory import applies trimming");
        patches.GetMethod("ReportAfterLoad", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { false, new IOException() });
        isolated = Factory(4096, 64);
        import.Invoke(null, new object[] { isolated });
        Require(Capacity(isolated) == 4096, "failed load releases the mutation gate");
        var f = Factory(4096, 64);
        foreach (int id in new[] { 3, 7 })
        {
            f.entityPool[id] = default;
            Array.Clear(f.entityConnPool, id * 16, 16);
        }
        Recycle(f)[0] = 3; Recycle(f)[1] = 7;
        Field("entityRecycleCursor").SetValue(f, 2);
        var before = Arrays(f);
        Require(trim(f) == 4096 - 1088, "trim retains the minimum construction reserve");
        var after = Arrays(f);
        for (int a = 0; a < after.Length; a++)
        {
            int count = a == 3 ? 64 * 16 : a == 6 ? 2 : 64;
            Require(after[a].Length == 1088 * (a == 3 ? 16 : 1), "all companions share capacity");
            for (int i = 0; i < count; i++)
                Require(Equals(before[a].GetValue(i), after[a].GetValue(i)), "prefix values and reference identity survive");
        }
        Require(f.entityCursor == 64 && f.entityCount == 61 && Recycled(f) == 2, "IDs, holes and recycle cursor survive");
        Require(trim(f) == 0 && Arrays(f).SequenceEqual(after), "repeat trim does not allocate or consume reserve");

        // A one-bucket spatial index supports the real game's add/remove methods without a Unity scene.
        typeof(HashSystem).GetProperty("bucketCount").SetValue(null, 1);
        typeof(HashSystem).GetProperty("cellSize").SetValue(null, 1f);
        HashSystem.bucketMap = new HashSystem.Cell[1];
        f.hashSystemStatic = new HashSystem(true) {
            hashPool = new int[8192], hashRecycle = new int[8192], bucketOffsets = new[] { 0, 8192 },
            bucketCursors = new int[1], bucketRecycleCursors = new int[1]
        };
        f.skillSystem = (SkillSystem)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SkillSystem));
        f.skillSystem.removedSkillTargets = new HashSet<SkillTarget>();
        var entity = new EntityData { pos = new Vector3(-270, -270, -270) };
        Require(f.AddEntityData(entity) == 7 && f.AddEntityData(entity) == 3, "vanilla add preserves recycle stack order");
        f.RemoveEntityWithComponents(7, false);
        Require(f.entityPool[7].id == 0 && Recycled(f) == 1 && f.AddEntityData(entity) == 7, "vanilla removal and ID reuse work after trim");
        int target = Capacity(f);
        while (f.entityCursor <= target) f.AddEntityData(entity);
        Require(Capacity(f) == target * 2 && f.entityPool[target].id == target, "vanilla growth works at the new boundary");
        Require(f.entityMutexs[1] == before[4].GetValue(1) && f.entityNeeds[1] == before[5].GetValue(1), "growth preserves companion references");
        Require(f.entityConnPool[16] == 16 && Recycled(f) == 0, "growth preserves connections with the recycle stack exhausted");

        var big = Factory(32768, 16384);
        Require(trim(big) == 32768 - 18432, "large factories retain proportional construction reserve");
        var dense = Factory(4096, 2600);
        before = Arrays(dense);
        Require(trim(dense) == 0 && Arrays(dense).SequenceEqual(before), "small savings skip copying the whole factory");
        var empty = Factory(4096, 1);
        Require(trim(empty) > 0 && empty.entityPool.Length >= 1024, "empty imported factory retains a positive base capacity");
        var invalid = Factory(4096, 64);
        invalid.entityNeeds = new int[10][];
        before = Arrays(invalid);
        bool rejected = false;
        try { trim(invalid); } catch (InvalidOperationException) { rejected = true; }
        Require(rejected && Capacity(invalid) == 4096 && Arrays(invalid).SequenceEqual(before), "incompatible pool layout leaves every original array intact");
        Console.WriteLine("PASS: load-only boundary, entity IDs, values/references, recycle order, vanilla add/remove/growth, headroom, no-op and invalid-layout preservation.");
        Console.WriteLine("Game MVID: " + typeof(PlanetFactory).Module.ModuleVersionId);
    }
}
