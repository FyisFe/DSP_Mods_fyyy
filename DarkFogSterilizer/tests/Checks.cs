using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using DarkFogSterilizer;

internal static class Checks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

    private static void Main(string[] args)
    {
        Require(args.Length == 1, "Usage: Checks <game-managed-dir>");
        AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
        {
            string path = Path.Combine(args[0], new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        Run();
    }

    private static EnemyDFHiveSystem Hive(SpaceSector sector, StarData star, int orbit)
    {
        var hive = Empty<EnemyDFHiveSystem>();
        hive.starData = star;
        hive.hiveAstroId = 1000001 + star.index * 8 + orbit;
        hive.realized = true;
        hive.rootEnemyId = hive.hiveAstroId;
        hive.pbuilders = new GrowthPattern_DFSpace.Builder[2];
        hive.builders = new DataPool<EnemyBuilderComponent>();
        hive.builders.Reset();
        ref var builder = ref hive.builders.Add();
        builder.spMax = 100;
        builder.spMatter = 20;
        builder.spEnergy = 100;
        builder.genEnergy = 500; // Even a self-powered completed core must remain inert while unfinished.
        Sterilizer.SetCore(ref builder);
        hive.pbuilders[1].instBuilderId = builder.id;
        hive.tinders = new DataPool<DFTinderComponent>();
        hive.tinders.Reset();
        sector.dfHivesByAstro[hive.hiveAstroId - 1000000] = hive;
        return hive;
    }

    private static int Weight(ref DFTinderComponent tinder, int starIndex)
    {
        int index = Array.IndexOf(tinder.sortedStarIndices, starIndex);
        return tinder.starValues[index] - (index == 0 ? 0 : tinder.starValues[index - 1]);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run()
    {
        Console.WriteLine("Game assembly MVID: " + typeof(DFTinderComponent).Module.ModuleVersionId);
        var sector = Empty<SpaceSector>();
        sector.galaxy = new GalaxyData { stars = new[]
        {
            new StarData { id = 1, index = 0, maxHiveCount = 1 },
            new StarData { id = 2, index = 1, maxHiveCount = 2, uPosition = new VectorLF3(2400000, 0, 0) },
            new StarData { id = 3, index = 2, maxHiveCount = 1, uPosition = new VectorLF3(4800000, 0, 0) }
        }};
        sector.gameData = Empty<GameData>();
        sector.gameData.history = new GameHistoryData();
        sector.dfHives = new EnemyDFHiveSystem[3];
        sector.dfHivesByAstro = new EnemyDFHiveSystem[26];
        sector.maxHiveCount = 26;
        sector.astros = new AstroData[26];
        var origin = sector.dfHives[0] = Hive(sector, sector.galaxy.stars[0], 0);
        var target = sector.galaxy.stars[1];
        var first = sector.dfHives[1] = Hive(sector, target, 0);
        var second = first.nextSibling = Hive(sector, target, 1);
        var tinder = new DFTinderComponent { originHiveAstroId = origin.hiveAstroId, dock = new DFDock { builderIndex = 1 } };
        tinder.GenerateSortedStarIndices(sector);
        Require(Weight(ref tinder, 1) == 0 && tinder.starBlankOrbitCount[1] == 0, "full sterile star must have zero native dispatch weight");
        Require(Weight(ref tinder, 2) > 0, "unrelated empty star must remain colonizable");

        ref var core = ref first.builders.buffer[1];
        int matter = core.matter;
        for (int tick = 0; tick < 36000; tick++) core.LogicTick();
        Require(core.sp == 0 && core.state == 0 && core.energy == 0 && core.matter == matter, "native construction remains inert for 36000 ticks");
        using (var stream = new MemoryStream())
        {
            var writer = new BinaryWriter(stream);
            core.Export(writer);
            writer.Flush();
            stream.Position = 0;
            var restored = new EnemyBuilderComponent();
            restored.Import(new BinaryReader(stream));
            core = restored;
        }
        core.LogicTick();
        tinder.GenerateSortedStarIndices(sector);
        Require(core.energy == 0 && core.sp == 0 && Weight(ref tinder, 1) == 0, "native builder serialization preserves sterility");

        core.matter = core.spMatter - 1;
        tinder.GenerateSortedStarIndices(sector);
        Require(Weight(ref tinder, 1) > 0, "one unit below the matter threshold must attract rescue");
        Sterilizer.SetCore(ref core);
        first.nextSibling = null;
        tinder.GenerateSortedStarIndices(sector);
        Require(Weight(ref tinder, 1) > 0 && tinder.starBlankOrbitCount[1] == 1, "unfilled capacity must attract colonization");
        first.nextSibling = second;

        ref var incoming = ref origin.tinders.Add();
        incoming.originHiveAstroId = origin.hiveAstroId;
        incoming.targetHiveAstroId = first.hiveAstroId;
        incoming.starValues = tinder.starValues;
        incoming.sortedStarIndices = tinder.sortedStarIndices;
        incoming.starBlankOrbitCount = tinder.starBlankOrbitCount;
        var enemy = new EnemyData { id = 1, originAstroId = origin.hiveAstroId, dfTinderId = incoming.id };
        Require(Sterilizer.TargetsStar(sector, ref enemy, target), "incoming foreign tinder must be removed");
        incoming.targetHiveAstroId = origin.hiveAstroId;
        Require(!Sterilizer.TargetsStar(sector, ref enemy, target), "unrelated tinder must survive");
        enemy.dfTinderId = 0;
        Require(!Sterilizer.TargetsStar(sector, ref enemy, target), "unrelated structures and units must survive");
        enemy.originAstroId = first.hiveAstroId;
        enemy.astroId = 0;
        Require(Sterilizer.TargetsStar(sector, ref enemy, target), "owned enemies are removed even when outside the star");
        Sterilizer.ClearTinderTargets(sector);
        Require(incoming.starValues == null && incoming.sortedStarIndices == null && incoming.starBlankOrbitCount == null,
            "surviving tinders must discard stale blank-orbit caches");
        incoming.dock.builderIndex = 1;
        incoming.GenerateSortedStarIndices(sector);
        Require(Weight(ref incoming, 1) == 0, "recomputed native target cache excludes sterile star");

        foreach (int limit in new[] { 0, 1, 2, 3, 6, 8, 12 })
        {
            target.maxHiveCount = limit;
            Require(Sterilizer.Capacity(target) == Math.Min(8, limit), "native eight-orbit capacity boundary");
        }
        var invalid = new EnemyBuilderComponent { spMax = 1, spMatter = 1, spEnergy = 0 };
        bool rejected = false;
        try { Sterilizer.SetCore(ref invalid); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected, "a core that can build without energy is unsupported");
        Console.WriteLine("PASS: native construction, serialization, dispatch thresholds, capacity, scope and cache invalidation.");
    }
}
