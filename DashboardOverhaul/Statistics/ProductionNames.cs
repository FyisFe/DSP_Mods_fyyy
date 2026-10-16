using System.Collections.Generic;

namespace DashboardOverhaul;

public static class ProductionNames
{
    /// <summary>Rename states the quick rename cycles through: three generated names and the game's
    /// default name (an empty custom name).</summary>
    private const int kRenameStepCount = 4;

    /// <summary>Last step applied per statistic, so a name that repeats between steps (a scope
    /// without a planet makes two steps produce the same text) still advances predictably.</summary>
    private static readonly Dictionary<AstroItemProductionStatPlan, int> s_renameStep = new();

    public static void Clear() => s_renameStep.Clear();

    /// <summary>Step a production statistic's name through the generated names and back to the game's
    /// default name. Renaming edits the shared StatPlan, so every chart of that statistic follows
    /// (like the sidebar rename).</summary>
    public static void Cycle(UIChart chart)
    {
        var plan = chart.charts.statPlans[chart.chartData.statPlanId];
        if (!(plan is AstroItemProductionStatPlan production)) return;

        int step = ResolveRenameStep(production);
        int next = (step + 1) % kRenameStepCount;
        string name = ApplyRenameStep(production, next);
        UIRealtimeTip.Popup(name.Length == 0
            ? Loc.L("已恢复默认名称", "Restored the default name")
            : string.Format(Loc.L("统计项已命名为：{0}", "Statistic named: {0}"), name));
    }

    /// <summary>Which rename step the statistic is on: remembered per statistic, and re-derived from
    /// the current name so a name edited elsewhere (sidebar or double-click) still starts the cycle.</summary>
    private static int ResolveRenameStep(AstroItemProductionStatPlan plan)
    {
        string current = plan.name ?? string.Empty;
        if (s_renameStep.TryGetValue(plan, out int step) && current == RenameName(plan, step)) return step;
        for (int i = 0; i < kRenameStepCount - 1; i++)
            if (current == RenameName(plan, i)) return i;
        return kRenameStepCount - 1; // default name: the next press applies the first generated name
    }

    private static string ApplyRenameStep(AstroItemProductionStatPlan plan, int step)
    {
        string name = RenameName(plan, step);
        s_renameStep[plan] = step;
        plan.Rename(ref name);
        return name;
    }

    /// <summary>The name of a rename step:
    /// 0 = planet-period ("A1-1-10分钟"), 1 = planet-period-item ("A1-1-10分钟-宇宙矩阵"),
    /// 2 = star-planet-period ("A1-A1-1-10分钟", the planet is dropped when the scope is a star system
    /// or the whole galaxy), 3 = the game's default name (an empty custom name).</summary>
    private static string RenameName(AstroItemProductionStatPlan plan, int step)
    {
        switch (step)
        {
            case 0: return Join(ScopeName(plan), PeriodName(plan.timeLevel));
            case 1: return Join(ScopeName(plan), PeriodName(plan.timeLevel), ItemName(plan.itemId));
            case 2: return Join(StarName(plan), PlanetName(plan), PeriodName(plan.timeLevel));
            default: return string.Empty;
        }
    }

    private static string Join(params string[] parts)
    {
        var kept = new List<string>(parts.Length);
        foreach (var part in parts)
            if (!string.IsNullOrEmpty(part)) kept.Add(part);
        return string.Join("-", kept);
    }

    /// <summary>Name of the scope the statistic watches: its planet, else its star system, else the
    /// whole galaxy.</summary>
    private static string ScopeName(AstroItemProductionStatPlan plan)
    {
        string planet = PlanetName(plan);
        if (!string.IsNullOrEmpty(planet)) return planet;
        string star = StarName(plan);
        if (!string.IsNullOrEmpty(star)) return star;
        return Loc.L("全星系", "All systems");
    }

    /// <summary>The watched planet, or null when the scope is a star system or the whole galaxy.</summary>
    private static string PlanetName(AstroItemProductionStatPlan plan)
    {
        var gameData = plan.gameData;
        int filter = plan.astroFilter;
        if (filter == -1) return null;
        if (filter == 0)
            return gameData.localPlanet != null ? gameData.localPlanet.displayName : null;
        if (filter % 100 == 0) return null;
        return gameData.galaxy.PlanetById(filter).displayName;
    }

    /// <summary>The watched star system, or the parent star of the watched planet.</summary>
    private static string StarName(AstroItemProductionStatPlan plan)
    {
        var gameData = plan.gameData;
        int filter = plan.astroFilter;
        if (filter == -1) return null;
        if (filter == 0)
            return gameData.localStar != null ? gameData.localStar.displayName : null;
        if (filter % 100 == 0)
        {
            var star = gameData.galaxy.StarById(filter / 100);
            return star != null ? star.displayName : null;
        }
        return gameData.galaxy.PlanetById(filter).star.displayName;
    }

    private static string ItemName(int itemId)
    {
        if (itemId <= 0) return null;
        var item = LDB.items.Select(itemId);
        return item != null ? item.name : null;
    }

    private static string PeriodName(int timeLevel)
    {
        switch (timeLevel)
        {
            case 0: return Loc.L("1分钟", "1min");
            case 1: return Loc.L("10分钟", "10min");
            case 2: return Loc.L("1小时", "1h");
            case 3: return Loc.L("10小时", "10h");
            case 4: return Loc.L("100小时", "100h");
            case 5: return Loc.L("总计", "Total");
            default: return Loc.L("未知", "Unknown");
        }
    }

    /// <summary>The gear-hover variant of the rename key: every production statistic of the current page
    /// advances to the same rename step at once.</summary>
    public static void CyclePage(UIDashboard dashboard)
    {
        var charts = dashboard.charts;
        var page = charts.dashboardLayout.pages[charts.currentView.pageIndex];
        var plans = new List<AstroItemProductionStatPlan>();
        foreach (var data in page.chartDatas)
            if (charts.statPlans[data.statPlanId] is AstroItemProductionStatPlan plan && !plans.Contains(plan))
                plans.Add(plan);
        if (plans.Count == 0) return;

        int step = ResolveRenameStep(plans[0]);
        int next = (step + 1) % kRenameStepCount;
        foreach (var plan in plans)
            ApplyRenameStep(plan, next);
        UIRealtimeTip.Popup(string.Format(Loc.L("当前页已命名 {0} 个统计项", "Renamed {0} statistics on this page"),
            plans.Count));
    }
}
