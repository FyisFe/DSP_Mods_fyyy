using System;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("DarkFogSterilizer.Checks")]

namespace DarkFogSterilizer;

internal static class Sterilizer
{
    internal static int Capacity(StarData star) => Math.Max(0, Math.Min(StarData.kMaxDFHiveOrbit, star.maxHiveCount));

    internal static bool TargetsStar(SpaceSector sector, ref EnemyData enemy, StarData star)
    {
        var origin = sector.GetHiveByAstroId(enemy.originAstroId);
        if (origin == null) return false;
        if (origin.starData == star) return true;
        return enemy.dfTinderId > 0 &&
            sector.GetHiveByAstroId(origin.tinders.buffer[enemy.dfTinderId].targetHiveAstroId)?.starData == star;
    }

    internal static void SetCore(ref EnemyBuilderComponent builder)
    {
        if (builder.spMax <= 0 || builder.spMatter <= 0 || builder.spEnergy <= 0)
            throw new InvalidOperationException("中枢核心的建造参数不支持绝育。");
        builder.sp = 0;
        builder.state = 0;
        builder.energy = 0;
        // Native tinder rescue requires matter < spMatter; unfinished cores cannot generate energy.
        builder.matter = builder.spMatter;
    }

    internal static void ClearTinderTargets(SpaceSector sector)
    {
        foreach (var first in sector.dfHives)
        {
            for (var hive = first; hive != null; hive = hive.nextSibling)
            {
                for (int i = 1; i < hive.tinders.cursor; i++)
                {
                    ref var tinder = ref hive.tinders.buffer[i];
                    if (tinder.id != i) continue;
                    // A cached blank orbit can otherwise bypass the current star capacity.
                    tinder.starValues = null;
                    tinder.sortedStarIndices = null;
                    tinder.starBlankOrbitCount = null;
                }
            }
        }
    }

    internal static void Apply(GameData data, StarData star)
    {
        var sector = data.spaceSector;
        if (!sector.isCombatMode || data.galaxy.StarById(star.id) != star)
            throw new InvalidOperationException("目标星系不属于当前战斗存档。");
        int capacity = Capacity(star);
        var coreDesc = LDB.enemies.Select(EnemyDFHiveSystem.PROTO_CORE)?.prefabDesc;
        if (capacity > 0 && (coreDesc == null || coreDesc.enemySpMax <= 0 || coreDesc.enemySpEnergy <= 0))
            throw new InvalidOperationException("当前中枢核心原型不支持零能量停工。");

        // Renderers subscribe to hive objects. Detach before the native RemoveHive frees them.
        if (sector.model.hiveRenderers != null)
            foreach (var renderer in sector.model.hiveRenderers)
                if (renderer?.hive?.starData == star) renderer.UnBindHive();

        foreach (var planet in star.planets)
        {
            var factory = planet.factory;
            if (factory == null) continue; // Unvisited bases live in relay components, removed below.
            var ground = factory.enemySystem;
            ground.ExecuteDeferredUnitFormation();
            ground.ExecuteDeferredEnemyChange();
            // Children still dereference their owner during removal; bases must be removed last.
            for (int i = factory.enemyCursor - 1; i > 0; i--)
                if (factory.enemyPool[i].id == i && factory.enemyPool[i].dfGBaseId == 0)
                    factory.RemoveEnemyFinal(i);
            ground.ExecuteDeferredEnemyChange();
            for (int i = ground.bases.cursor - 1; i > 0; i--)
                if (ground.bases.buffer[i]?.id == i) ground.RemoveBase(i);
            ground.NotifyBaseRemoved();
        }

        // Keep both endpoints alive until native tinder removal has decremented transit counts.
        for (int i = sector.enemyCursor - 1; i > 0; i--)
            if (sector.enemyPool[i].id == i && TargetsStar(sector, ref sector.enemyPool[i], star))
                sector.RemoveEnemyFinal(i);
        while (sector.dfHives[star.index] != null)
            sector.RemoveHive(sector.dfHives[star.index]);

        for (int i = 0; i < capacity; i++)
        {
            var hive = sector.TryCreateNewHive(star)
                ?? throw new InvalidOperationException("无法分配太空巢穴轨道。");
            // Realize the empty pattern, avoiding virtual growth and native relay generation.
            hive.Realize();
            int enemyId = sector.CreateEnemyFinal(hive, 1);
            if (enemyId <= 0) throw new InvalidOperationException("无法创建中枢核心。");
            ref var builder = ref hive.builders.buffer[sector.enemyPool[enemyId].builderId];
            SetCore(ref builder);
            builder.RefreshAnimation_Space(hive.pbuilders, ref sector.enemyAnimPool[enemyId]);
            hive.isEmpty = false;
        }
        ClearTinderTargets(sector);
    }
}
