using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DashboardOverhaul;

/// <summary>
/// Adds a "Move to page →" submenu to every chart's right-click popup, letting the
/// player move an individual chart from the current page to another existing page.
/// </summary>
public static class UIChartPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIChart), nameof(UIChart.SetPopupMenuButtons))]
    static void SetPopupMenuButtons_Postfix(UIChart __instance, UIPopupMenu popupMenu)
    {
        var charts = __instance.charts;
        if (charts == null || __instance.chartData == null) return;
        var layout = charts.dashboardLayout;
        int cur = charts.currentView.pageIndex;

        // "Move to page →" — only when another existing page is available as a target.
        var targets = new List<int>();
        for (int i = 1; i < DashboardLayout.MAX_PAGE_COUNT; i++)
            if (i != cur && layout.pages[i] != null) targets.Add(i);
        if (targets.Count > 0)
        {
            var moveBtn = popupMenu.AddMenuButton(Loc.L("移动到页面", "Move to page"), -1, true);
            var child = __instance.CreateAndInitChildPopupMenu(moveBtn);
            foreach (int slot in targets)
            {
                var page = layout.pages[slot];
                string name = string.IsNullOrEmpty(page.name) ? slot.ToString() : page.name;
                var b = child.AddMenuButton(name);
                b.data = slot;
                b.onMenuButtonClick += s => MoveChartToPage(__instance, s);
                b.SetState(true);
            }
            moveBtn.m_ChildMenu = child;
            moveBtn.SetState(true);
        }

        // Rename (edits the bound StatPlan's name; affects all charts of that statistic).
        var renameBtn = popupMenu.AddMenuButton(Loc.L("重命名", "Rename"), -1, true);
        renameBtn.onMenuButtonClick += _ =>
        {
            var dash = __instance.uiDashboard;
            if (dash != null) dash.CloseChartPopupMenu();
            ChartRename.Begin(__instance);
        };
        renameBtn.SetState(true);

        popupMenu.SetState(true);
    }

    static void MoveChartToPage(UIChart chart, int targetSlot)
    {
        var charts = chart.charts;
        if (charts == null) return;
        var layout = charts.dashboardLayout;
        int cur = charts.currentView.pageIndex;
        var curPage = layout.pages[cur];
        var targetPage = layout.pages[targetSlot];
        var cd = chart.chartData;
        if (curPage == null || targetPage == null || cd == null) return;
        if (!curPage.chartDatas.Remove(cd)) return; // chart not on current page; bail

        // Drop it on top of the target page's stack, preserving all its other properties.
        int maxDepth = int.MinValue;
        for (int k = 0; k < targetPage.chartDatas.Count; k++)
            if (targetPage.chartDatas[k].depth > maxDepth) maxDepth = targetPage.chartDatas[k].depth;
        cd.depth = targetPage.chartDatas.Count == 0 ? 0 : maxDepth + 1;

        // Keep the chart's position if it doesn't collide on the target page; otherwise drop it on
        // the first free grid slot so it isn't stacked exactly on top of an existing chart. Mirrors
        // the game's own AddChartWithAutoPosition collision-avoidance (grid bounds = maxGridCount*8),
        // minus its least-overlap fallback. NB: like the vanilla AddChart (resize) path, this does
        // NOT enforce the per-stat-type chartExistMaxCount cap — moving is a reorganization action.
        var dash = chart.uiDashboard;
        if (dash != null && Overlaps(targetPage, cd.pos, cd.size))
        {
            var boundMax = new Vector2Int(dash.maxGridCountX * 8, dash.maxGridCountY * 8);
            cd.pos = FindFreePosition(targetPage, cd.size, boundMax, cd.pos);
        }

        targetPage.chartDatas.Add(cd);

        if (dash != null)
        {
            dash.CloseChartPopupMenu();
            dash.DetermineCharts(); // re-render current page (chart now gone from it)
        }
    }

    /// <summary>True if a box at <paramref name="pos"/> of <paramref name="size"/> intersects any
    /// chart already on <paramref name="page"/> (same half-open-box test the game uses).</summary>
    static bool Overlaps(DashboardPage page, Vector2Int pos, Vector2Int size)
    {
        int maxX = pos.x + size.x, maxY = pos.y + size.y;
        var list = page.chartDatas;
        for (int i = 0; i < list.Count; i++)
        {
            var o = list[i];
            int oMaxX = o.pos.x + o.size.x, oMaxY = o.pos.y + o.size.y;
            if (maxX > o.pos.x && oMaxX > pos.x && maxY > o.pos.y && oMaxY > pos.y) return true;
        }
        return false;
    }

    /// <summary>First grid slot (row-major within [0,boundMax)) where a box of <paramref name="size"/>
    /// doesn't overlap any chart on <paramref name="page"/>; returns <paramref name="fallback"/> if none.</summary>
    static Vector2Int FindFreePosition(DashboardPage page, Vector2Int size, Vector2Int boundMax, Vector2Int fallback)
    {
        for (int y = 0; y + size.y <= boundMax.y; y++)
            for (int x = 0; x + size.x <= boundMax.x; x++)
            {
                var p = new Vector2Int(x, y);
                if (!Overlaps(page, p, size)) return p;
            }
        return fallback;
    }
}
