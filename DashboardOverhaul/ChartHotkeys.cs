using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DashboardOverhaul;

/// <summary>
/// Dashboard chart hotkeys: the size switch ported from StatsUITweaks' TabSizeSwitch feature, plus a
/// quick rename for production statistics. Hovering a chart and pressing
/// <see cref="ModConfig.SwitchSizeKey"/> cycles the chart through the sizes the dashboard offers;
/// hovering a production chart and pressing LeftCtrl plus <see cref="ModConfig.QuickRenameKey"/> steps
/// the statistic's name through the generated names of
/// <see cref="RenameName"/> and back to the game's default name.
/// Both keys are configurable, and neither acts while a text field has focus.
/// </summary>
public static class ChartHotkeys
{
    /// <summary>Rename states the quick rename cycles through: three generated names and the game's
    /// default name (an empty custom name).</summary>
    private const int kRenameStepCount = 4;

    /// <summary>Last step applied per statistic id, so a name that repeats between steps (a scope
    /// without a planet makes two steps produce the same text) still advances predictably.</summary>
    private static readonly Dictionary<int, int> s_renameStep = new();

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIChart), "_OnUpdate")]
    static void OnUpdate_Postfix(UIChart __instance)
    {
        if (__instance == null) return;
        SyncHover(__instance);
        if (!__instance.mouseInChart) return;
        if (IsTextEditing()) return; // a rename input owns the keyboard
        if (IsAltDown()) return;     // Alt+Tab and friends belong to the OS

        if (IsQuickRenameDown())
        {
            QuickRename(__instance);
            return;
        }
        if (IsSwitchSizeDown())
        {
            SwitchSize(__instance);
            return;
        }
        if (IsCycleBackgroundDown())
        {
            CycleStyle(__instance, EChartStyleType.Background);
            return;
        }
        if (IsCycleBorderDown())
        {
            CycleStyle(__instance, EChartStyleType.Border);
            return;
        }
        if (IsResetBorderDown()) ResetBorder(__instance);
    }

    /// <summary>Handles the page-wide style keys while the cursor is over the page-settings gear.</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIDashboard), "_OnUpdate")]
    static void DashboardUpdate_Postfix(UIDashboard __instance)
    {
        if (__instance == null) return;

        var bar = UIDashboardPatch.Bar;
        if (bar != null && bar.PointerOverGear && !IsTextEditing() && !IsAltDown())
        {
            // Over the gear, the style keys act on every statistic of the current page.
            if (IsQuickRenameDown())
            {
                RenameCurrentPage(__instance);
                return;
            }
            if (IsCycleBackgroundDown())
            {
                PageGearMenu.CyclePageStyle(__instance, EChartStyleType.Background);
                return;
            }
            if (IsCycleBorderDown())
            {
                PageGearMenu.CyclePageStyle(__instance, EChartStyleType.Border);
                return;
            }
            if (IsResetBorderDown())
            {
                PageGearMenu.ResetPageBorder(__instance);
                return;
            }
        }
    }

    private static bool IsSwitchSizeDown()
    {
        var key = ModConfig.SwitchSizeKey;
        return key != null && key.Value != KeyCode.None && !IsCtrlDown() && Input.GetKeyDown(key.Value);
    }

    private static bool IsQuickRenameDown()
    {
        var key = ModConfig.QuickRenameKey;
        return key != null && key.Value != KeyCode.None && Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(key.Value);
    }

    private static bool IsCycleBackgroundDown()
    {
        var key = ModConfig.CycleBackgroundKey;
        return key != null && key.Value != KeyCode.None && !IsCtrlDown() && Input.GetKeyDown(key.Value);
    }

    private static bool IsCycleBorderDown()
    {
        var key = ModConfig.CycleBorderKey;
        return key != null && key.Value != KeyCode.None && !IsCtrlDown() && Input.GetKeyDown(key.Value);
    }

    private static bool IsResetBorderDown()
    {
        var key = ModConfig.ResetBorderKey;
        return key != null && key.Value != KeyCode.None && !IsCtrlDown() && Input.GetKeyDown(key.Value);
    }

    /// <summary>Step the hovered chart through the styles of one kind, in the order the style menu lists
    /// them (default first, then the table order). For a locked chart the border "default" is its blue
    /// border, so the cycle never lands on "no border".</summary>
    private static void CycleStyle(UIChart chart, EChartStyleType type)
    {
        var data = chart != null ? chart.chartData : null;
        var styles = ChartPresetsDB.GetChartStyles();
        if (data == null || styles == null) return;

        var options = ChartStyleExtras.StyleOptions(type);
        if (options.Count < 2) return;
        int current = type == EChartStyleType.Background ? data.backgroundStyleIndex : data.borderStyleIndex;
        int at = options.IndexOf(current);
        int next = options[(at < 0 ? 0 : at + 1) % options.Count];
        if (type == EChartStyleType.Border) next = ChartLockPersist.ResolveBorder(data, next);

        if (type == EChartStyleType.Background) data.backgroundStyleIndex = next;
        else data.borderStyleIndex = next;

        // A plain Repaint would keep the previous style's colour; rebuilding clears it.
        ChartStyleExtras.RebuildCharts(chart);
    }

    /// <summary>Reset the hovered chart's border to its default: blue while locked, no border otherwise.</summary>
    private static void ResetBorder(UIChart chart)
    {
        var data = chart != null ? chart.chartData : null;
        if (data == null) return;
        int index = ChartLockPersist.ResolveBorder(data, 0);
        if (data.borderStyleIndex == index) return;
        data.borderStyleIndex = index;
        ChartStyleExtras.RebuildCharts(chart);
    }

    private static bool IsCtrlDown() => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

    /// <summary>
    /// Keep the chart's hover flag in step with the pointer. The game only sets <c>mouseInChart</c> from
    /// pointer-enter events, so a chart that was rebuilt while the cursor sat on it (style switching
    /// rebuilds the page) never learns the cursor is already there: its hotkeys and corner buttons stay
    /// dead until the cursor is moved away and back. The dashboard tracks the chart nearest to the cursor
    /// every frame, and a rectangle test confirms the cursor really is inside, which gives the same answer
    /// the enter event would have.
    /// </summary>
    private static void SyncHover(UIChart chart)
    {
        var dashboard = chart.uiDashboard;
        bool inside = PointerInside(chart) && dashboard != null && dashboard.mouseNearestChart == chart;
        if (chart.mouseInChart != inside) chart.mouseInChart = inside;
    }

    private static bool PointerInside(UIChart chart)
    {
        if (chart == null || chart.rectTrans == null) return false;
        var root = UIRoot.instance;
        var camera = root != null && root.overlayCanvas != null ? root.overlayCanvas.worldCamera : null;
        return RectTransformUtility.RectangleContainsScreenPoint(chart.rectTrans, Input.mousePosition, camera);
    }
    private static bool IsAltDown() => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

    private static bool IsTextEditing()
    {
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        return selected != null && selected.GetComponent<InputField>() != null;
    }

    /// <summary>Advance the chart to the next dashboard size, wrapping around. The presets array is
    /// shared with the watch view, so only presets the dashboard size menu offers are used.</summary>
    private static void SwitchSize(UIChart chart)
    {
        if (chart.charts == null || chart.chartData == null) return;
        var presets = ChartPresetsDB.GetPresetsArray(chart.statPlanType);
        if (presets == null || presets.Length < 2) return;

        for (int step = 1; step < presets.Length; step++)
        {
            int next = (chart.chartData.presetIndex + step) % presets.Length;
            if (presets[next].usage != ChartPresets.EUsage.Dashboard) continue;
            chart.OnSizeMenuButtonClick(next);
            return;
        }
    }

    /// <summary>Step a production statistic's name through the generated names and back to the game's
    /// default name. Renaming edits the shared StatPlan, so every chart of that statistic follows
    /// (like the sidebar rename).</summary>
    private static void QuickRename(UIChart chart)
    {
        var plan = ResolveStatPlan(chart);
        if (plan == null) return;
        if (!(plan is AstroItemProductionStatPlan production))
        {
            return;
        }

        int step = ResolveRenameStep(production);
        int next = (step + 1) % kRenameStepCount;
        string name = RenameName(production, next);
        s_renameStep[production.id] = next;
        plan.Rename(ref name);
        UIRealtimeTip.Popup(name.Length == 0
            ? Loc.L("已恢复默认名称", "Restored the default name")
            : string.Format(Loc.L("统计项已命名为：{0}", "Statistic named: {0}"), name));
    }

    /// <summary>Which rename step the statistic is on: remembered per statistic, and re-derived from
    /// the current name so a name edited elsewhere (sidebar or double-click) still starts the cycle.</summary>
    private static int ResolveRenameStep(AstroItemProductionStatPlan plan)
    {
        string current = plan.name ?? string.Empty;
        if (s_renameStep.TryGetValue(plan.id, out int step) && current == RenameName(plan, step)) return step;
        for (int i = 0; i < kRenameStepCount - 1; i++)
            if (current == RenameName(plan, i)) return i;
        return kRenameStepCount - 1; // default name: the next press applies the first generated name
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
        if (gameData == null || gameData.galaxy == null || filter == -1) return null;
        if (filter == 0)
            return gameData.localPlanet != null ? gameData.localPlanet.displayName : null;
        if (filter % 100 == 0) return null;
        var planet = gameData.galaxy.PlanetById(filter);
        return planet != null ? planet.displayName : null;
    }

    /// <summary>The watched star system, or the parent star of the watched planet.</summary>
    private static string StarName(AstroItemProductionStatPlan plan)
    {
        var gameData = plan.gameData;
        int filter = plan.astroFilter;
        if (gameData == null || gameData.galaxy == null || filter == -1) return null;
        if (filter == 0)
            return gameData.localStar != null ? gameData.localStar.displayName : null;
        if (filter % 100 == 0)
        {
            var star = gameData.galaxy.StarById(filter / 100);
            return star != null ? star.displayName : null;
        }
        var planet = gameData.galaxy.PlanetById(filter);
        return planet != null && planet.star != null ? planet.star.displayName : null;
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

    /// <summary>Resolve the StatPlan bound to a chart, guarding a null pool/buffer and a stale
    /// out-of-range statPlanId (e.g. during teardown).</summary>
    private static StatPlan ResolveStatPlan(UIChart chart)
    {
        var pool = chart != null && chart.charts != null ? chart.charts.statPlans : null;
        if (pool == null || pool.buffer == null || chart.chartData == null) return null;
        int id = chart.chartData.statPlanId;
        if (id < 0 || id >= pool.buffer.Length) return null;
        return pool.buffer[id];
    }

    /// <summary>The gear-hover variant of the rename key: every production statistic of the current page
    /// advances to the same rename step at once.</summary>
    private static void RenameCurrentPage(UIDashboard dashboard)
    {
        var charts = dashboard != null ? dashboard.charts : null;
        var pages = charts != null && charts.dashboardLayout != null ? charts.dashboardLayout.pages : null;
        if (pages == null || !PageOps.IsValidViewPage(charts)) return;
        var page = pages[charts.currentView.pageIndex];
        if (page == null || page.chartDatas == null) return;

        var pool = charts.statPlans != null ? charts.statPlans.buffer : null;
        if (pool == null) return;

        var plans = new List<AstroItemProductionStatPlan>();
        foreach (var data in page.chartDatas)
        {
            if (data == null) continue;
            int id = data.statPlanId;
            if (id < 0 || id >= pool.Length) continue;
            if (pool[id] is AstroItemProductionStatPlan plan && !plans.Contains(plan)) plans.Add(plan);
        }
        if (plans.Count == 0)
        {
            return;
        }

        int step = ResolveRenameStep(plans[0]);
        int next = (step + 1) % kRenameStepCount;
        foreach (var plan in plans)
        {
            string name = RenameName(plan, next);
            s_renameStep[plan.id] = next;
            plan.Rename(ref name);
        }
        UIRealtimeTip.Popup(string.Format(Loc.L("当前页已命名 {0} 个统计项", "Renamed {0} statistics on this page"),
            plans.Count));
    }
}
