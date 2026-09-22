using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using DashboardOverhaul;
using HarmonyLib;
using UnityEngine;

internal static class Checks
{
    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    private static DashboardPage Page() => new DashboardPage { chartDatas = new List<ChartData>() };
    private static ChartData Chart(int x, int y, int width, int height) => new ChartData
    {
        pos = new Vector2Int(x, y), size = new Vector2Int(width, height), statPlanId = 7,
        depth = 11, presetIndex = 2, genericStyleIndex = 3, backgroundStyleIndex = 4,
        borderStyleIndex = 5, displayTypeParams = new int[16]
    };

    private static void Main(string[] args)
    {
        Require(args.Length == 1, "Usage: Checks <game-managed-dir>");
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            string path = Path.Combine(args[0], new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        Run();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run()
    {
        // Unity UI methods cannot be JIT-patched outside the engine; check their target signatures.
        foreach (var type in new[] { typeof(UIDashboardPatch), typeof(UIChartPatch), typeof(DashboardLayoutPatch) })
            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic))
                foreach (HarmonyPatch patch in method.GetCustomAttributes(typeof(HarmonyPatch), false))
                    Require(AccessTools.DeclaredMethod(patch.info.declaringType, patch.info.methodName,
                        patch.info.argumentTypes) != null, "missing Harmony target: " + patch.info.methodName);
        CheckHeader();
        CheckRenameBounds();
        CheckLayout();
    }

    private static void CheckHeader()
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        var type = typeof(PageTabBar);
        float Constant(string name) => Convert.ToSingle(type.GetField(name, flags).GetRawConstantValue());
        float height = Constant("kHeaderHeight");
        float tabHeight = Constant("kTabHeight");
        float left = Constant("kBaseLeftMargin");
        Require(-Constant("kTopOffset") + tabHeight + 10 <= height,
            "header must clear the first chart's 10-pixel resize hit area");
        var contentLeft = type.GetMethod("ContentLeft", flags);
        foreach (float sidebarX in new[] { -380.00006f, -380f, -375f, -190f, -5f, 0f })
        {
            float chartLeft = (float)contentLeft.Invoke(null, new object[] { 380f, sidebarX });
            float handleRight = 380f + sidebarX + 20f;
            Require(chartLeft - 10f >= handleRight,
                "chart resize hit areas must clear the native handle throughout the sidebar animation");
            Require(Math.Abs(chartLeft - Math.Max(0, 380f + sidebarX) - Constant("kChartLeftMargin")) < 0.01f,
                "sidebar animation must translate the chart origin by the visible sidebar width");
        }

        var fit = type.GetMethod("ComputePerTabMax", flags, null, new[] { typeof(float), typeof(int) }, null);
        foreach (float screenWidth in new[] { 1280f, 1920f, 2560f, 3840f })
            foreach (float scale in new[] { 0.75f, 1f, 1.5f, 2f })
                for (int count = 1; count <= 9; count++)
                {
                    float width = screenWidth / scale;
                    float tabWidth = (float)fit.Invoke(null, new object[] { width, count });
                    Require(left + count * (tabWidth + 4) + tabHeight + 16 <= width + 0.01f,
                        "all tabs and the add button must fit the header at supported UI scales");
                }
        Console.WriteLine("Dashboard header geometry checks passed.");
    }

    private static void CheckRenameBounds()
    {
        var place = typeof(PageTabBar).GetMethod("RenameInputRect", BindingFlags.NonPublic | BindingFlags.Static);
        foreach (float width in new[] { 80f, 640f, 1920f })
        {
            var header = new Rect(-width * 0.5f, -44f, width, 44f);
            foreach (var tab in new[]
            {
                new Rect(header.xMin + 4, -32, 64, 28), // Reported leftmost narrow tab.
                new Rect(header.xMax - 68, -32, 64, 28),
                new Rect(header.xMin + 4, -32, 240, 28),
                new Rect(header.xMax - 16, -20, 64, 28) // Window shrinks while editing.
            })
            {
                var input = (Rect)place.Invoke(null, new object[] { tab, header });
                Require(input.xMin >= header.xMin + 4 && input.xMax <= header.xMax - 4 &&
                    input.yMin >= header.yMin + 4 && input.yMax <= header.yMax - 4,
                    "rename input must remain fully visible at both viewport edges");
                Require(input.width >= Math.Min(120, width - 8) && input.height == 28,
                    "rename input must keep its editing space within the available header");
                if (tab.xMin == header.xMin + 4)
                    Require(input.xMin == tab.xMin && input.yMax == tab.yMax,
                        "leftmost rename input must expand rightwards from the tab's top-left corner");
            }
        }
        Console.WriteLine("Page rename input bounds checks passed.");
    }

    private static void CheckLayout()
    {
        var charts = new CustomCharts { dashboardLayout = new DashboardLayout { pages = new DashboardPage[10] } };
        var pages = charts.dashboardLayout.pages;
        var first = pages[1] = Page();
        var last = pages[5] = Page();
        charts.currentView.pageIndex = 5;
        int added = PageOps.AddPage(charts);
        Require(added == 3 && pages[1] == first && pages[2] == last && pages[3] != null,
            "new page must append after sparse existing pages");
        Require(charts.currentView.pageIndex == 2, "compaction must preserve the viewed page");
        Require(!PageOps.ReorderPages(charts, new[] { first, first, pages[3] }), "reject duplicate reorder entries");
        Require(pages[2] == last, "rejected reorder must not mutate pages");
        for (int i = 0; i < 6; i++) Require(PageOps.AddPage(charts) > 0, "fill remaining pages");
        Require(PageOps.AddPage(charts) == -1 && PageOps.ActivePageCount(charts) == 9, "page limit");

        foreach (int invalid in new[] { -1, 0, 10, int.MaxValue })
        {
            charts.currentView.pageIndex = invalid;
            PageOps.EnsureViewPage(charts);
            Require(PageOps.IsValidViewPage(charts), "repair invalid view before dashboard opens");
        }
        Array.Clear(pages, 0, pages.Length);
        PageOps.EnsureViewPage(charts);
        Require(PageOps.IsValidViewPage(charts) && pages[1] != null, "recover an entirely empty layout");
        Require(!PageOps.RemovePage(charts, 1), "cannot delete the last page");

        var source = Page();
        var target = Page();
        var chart = Chart(0, 0, 2, 2);
        source.chartDatas.Add(chart);
        target.chartDatas.Add(Chart(0, 0, 2, 2));
        Require(!PageOps.TryMoveChart(source, target, chart, new Vector2Int(2, 2)), "reject a full destination");
        Require(source.chartDatas.Count == 1 && target.chartDatas.Count == 1 && chart.pos == Vector2Int.zero && chart.depth == 11,
            "failed move must leave both pages and all chart data unchanged");
        Require(PageOps.TryMoveChart(source, target, chart, new Vector2Int(4, 2)), "use a free edge-adjacent position");
        Require(chart.pos == new Vector2Int(2, 0) && chart.depth == 12 && source.chartDatas.Count == 0,
            "moved chart must not overlap and must retain its identity");
        Require(target.chartDatas[1] == chart && chart.statPlanId == 7 && chart.presetIndex == 2,
            "moving preserves statistic and display configuration");

        var copy = Page();
        chart.displayTypeParams[3] = 91;
        Require(PageOps.CopyIntoEmptyPage(target, copy), "copy into an empty page");
        var cloned = copy.chartDatas[1];
        Require(cloned != chart && cloned.statPlanId == chart.statPlanId && cloned.size == chart.size &&
            cloned.pos == chart.pos && cloned.depth == chart.depth && cloned.presetIndex == 2 &&
            cloned.genericStyleIndex == 3 && cloned.backgroundStyleIndex == 4 && cloned.borderStyleIndex == 5 &&
            cloned.displayTypeParams[3] == 91, "copy all display settings while sharing only the statistic");
        cloned.displayTypeParams[3] = 12;
        Require(chart.displayTypeParams[3] == 91, "copied display parameters must be independent");
        Require(!PageOps.CopyIntoEmptyPage(target, copy) && copy.chartDatas.Count == 2, "never overwrite a populated page");

        pages[1] = target;
        pages[2] = copy;
        charts.watchLayout = new WatchLayout { chartDatas = new List<ChartData> { Chart(0, 0, 1, 1) } };
        Require(PageOps.ChartCount(charts, 7) == 5, "impact count must include other pages and watch views");
        Console.WriteLine("Dashboard checks passed: target signatures, page order/limits/recovery, transactional moves, independent copies, impact counts.");
    }
}
