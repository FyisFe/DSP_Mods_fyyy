using System.Collections.Generic;
using UnityEngine;

namespace DashboardOverhaul;

/// <summary>
/// Pure logic layer for dashboard paging: operates only on data structures, never touches Unity UI.
/// Page index domain is 1..9 (pages[0] is never used). Deletion nulls the slot in place (no shifting).
/// </summary>
public static class PageOps
{
    public static int ActivePageCount(CustomCharts charts)
    {
        int count = 0;
        var pages = charts.dashboardLayout.pages;
        for (int i = 1; i < DashboardLayout.MAX_PAGE_COUNT; i++)
            if (pages[i] != null) count++;
        return count;
    }

    public static int FirstFreeSlot(DashboardLayout layout)
    {
        var pages = layout.pages;
        for (int i = 1; i < DashboardLayout.MAX_PAGE_COUNT; i++)
            if (pages[i] == null) return i;
        return -1;
    }

    /// <summary>First non-null slot (1..9), or -1 if none. Used to repoint an invalid current page back to a valid one.</summary>
    public static int FirstActiveSlot(DashboardLayout layout)
    {
        var pages = layout?.pages;
        if (pages == null) return -1;
        for (int i = 1; i < DashboardLayout.MAX_PAGE_COUNT; i++)
            if (pages[i] != null) return i;
        return -1;
    }

    /// <summary>Whether currentView.pageIndex points at a valid non-null page (1..9).</summary>
    public static bool IsValidViewPage(CustomCharts charts)
    {
        var pages = charts?.dashboardLayout?.pages;
        if (pages == null) return false;
        int cur = charts.currentView.pageIndex;
        return cur >= 1 && cur < DashboardLayout.MAX_PAGE_COUNT && pages[cur] != null;
    }

    public static string PageName(DashboardPage page, int slot) =>
        string.IsNullOrEmpty(page.name) ? slot.ToString() : page.name;

    public static void EnsureViewPage(CustomCharts charts)
    {
        if (IsValidViewPage(charts)) return;
        int slot = FirstActiveSlot(charts.dashboardLayout);
        if (slot < 0)
        {
            slot = 1;
            charts.dashboardLayout.AddPage(slot);
        }
        charts.currentView.pageIndex = slot;
    }

    /// <summary>Append after existing pages, preserving their order and the viewed page.</summary>
    public static int AddPage(CustomCharts charts)
    {
        var layout = charts.dashboardLayout;
        int slot = FirstFreeSlot(layout);
        if (slot < 0) return -1;
        var order = new List<DashboardPage>();
        for (int i = 1; i < DashboardLayout.MAX_PAGE_COUNT; i++)
            if (layout.pages[i] != null) order.Add(layout.pages[i]);
        layout.AddPage(slot);
        order.Add(layout.pages[slot]);
        ReorderPages(charts, order);
        return order.Count;
    }

    public static bool CanDelete(CustomCharts charts) => ActivePageCount(charts) > 1;

    /// <summary>Slot to jump to after deleting deletedIndex: scan lower page numbers first, then higher; -1 if none.</summary>
    public static int PickPageAfterDelete(DashboardLayout layout, int deletedIndex)
    {
        var pages = layout.pages;
        for (int i = deletedIndex - 1; i >= 1; i--)
            if (pages[i] != null) return i;
        for (int i = deletedIndex + 1; i < DashboardLayout.MAX_PAGE_COUNT; i++)
            if (pages[i] != null) return i;
        return -1;
    }

    /// <summary>Frees all charts on the page and nulls the slot. Does not switch pages (caller handles currentView and refresh).</summary>
    public static bool RemovePage(CustomCharts charts, int index)
    {
        if (!CanDelete(charts)) return false;
        if (index < 1 || index >= DashboardLayout.MAX_PAGE_COUNT) return false;
        var pages = charts.dashboardLayout.pages;
        var page = pages[index];
        if (page == null) return false;
        // free charts one by one (DashboardPage.Free clears chartDatas)
        page.Free();
        pages[index] = null;
        return true;
    }

    /// <summary>
    /// Reorders pages to match <paramref name="newOrder"/> (the desired left-to-right display
    /// order), compacting them into slots 1..N and nulling the rest. <paramref name="newOrder"/>
    /// must contain exactly the current set of non-null pages (same count and members); on any
    /// mismatch this is a no-op and returns false (defensive). Repoints currentView.pageIndex to
    /// wherever the previously-viewed page object lands, so the player stays on the same page. Slot
    /// index is the page's save key (DashboardLayout.Export/Import is slot-by-slot), so the new order
    /// persists on the next game save with no format change.
    /// </summary>
    /// <returns>true if the reorder was applied; false if <paramref name="newOrder"/> was rejected.</returns>
    public static bool ReorderPages(CustomCharts charts, IReadOnlyList<DashboardPage> newOrder)
    {
        var pages = charts?.dashboardLayout?.pages;
        if (pages == null || newOrder == null) return false;

        // newOrder must be exactly the current non-null page set. Checking the count first bounds the
        // write to slots 1..N (active <= 9 < MAX_PAGE_COUNT, so no slot overflow) and rejects any
        // duplicate-plus-extra list that the set check below could otherwise let through.
        int active = ActivePageCount(charts);
        if (active == 0) return false;
        if (newOrder.Count != active) return false;

        // Validate that newOrder is a permutation of the current non-null pages.
        var set = new HashSet<DashboardPage>();
        foreach (var p in newOrder)
        {
            if (p == null) return false;
            set.Add(p);
        }
        if (set.Count != active) return false;           // duplicates
        for (int i = 1; i < DashboardLayout.MAX_PAGE_COUNT; i++)
            if (pages[i] != null && !set.Contains(pages[i])) return false; // a current page is missing

        // Remember the page the player is viewing (by reference) so we can follow it.
        int cur = charts.currentView.pageIndex;
        DashboardPage viewed = (cur >= 1 && cur < DashboardLayout.MAX_PAGE_COUNT) ? pages[cur] : null;

        // Write the new order into slots 1..N; null the remainder.
        for (int i = 0; i < newOrder.Count; i++)
            pages[i + 1] = newOrder[i];
        for (int i = newOrder.Count + 1; i < DashboardLayout.MAX_PAGE_COUNT; i++)
            pages[i] = null;

        // Repoint the current view to the viewed page's new slot (fallback: first slot).
        int newCur = 1;
        if (viewed != null)
            for (int i = 1; i < DashboardLayout.MAX_PAGE_COUNT; i++)
                if (pages[i] == viewed) { newCur = i; break; }
        charts.currentView.pageIndex = newCur;
        return true;
    }

    public static void RenamePage(DashboardPage page, string newName)
    {
        if (page == null) return;
        page.name = (newName ?? string.Empty).Trim();
    }

    public static int ChartCount(CustomCharts charts, int statPlanId)
    {
        int count = 0;
        foreach (var page in charts.dashboardLayout.pages)
            if (page != null)
                foreach (var chart in page.chartDatas)
                    if (chart.statPlanId == statPlanId) count++;
        if (charts.watchLayout?.chartDatas != null)
            foreach (var chart in charts.watchLayout.chartDatas)
                if (chart.statPlanId == statPlanId) count++;
        return count;
    }

    // Find space before mutating either page: a full destination must leave the source intact.
    public static bool TryMoveChart(DashboardPage source, DashboardPage target, ChartData chart, Vector2Int bounds)
    {
        if (source == target || !source.chartDatas.Contains(chart)) return false;
        Vector2Int pos = chart.pos;
        if (!Fits(target, pos, chart.size, bounds))
        {
            bool found = false;
            for (int y = 0; y + chart.size.y <= bounds.y && !found; y++)
                for (int x = 0; x + chart.size.x <= bounds.x; x++)
                    if (Fits(target, new Vector2Int(x, y), chart.size, bounds))
                    {
                        pos = new Vector2Int(x, y);
                        found = true;
                        break;
                    }
            if (!found) return false;
        }
        int depth = -1;
        foreach (var other in target.chartDatas) depth = System.Math.Max(depth, other.depth);
        source.chartDatas.Remove(chart);
        chart.pos = pos;
        chart.depth = depth + 1;
        target.chartDatas.Add(chart);
        return true;
    }

    private static bool Fits(DashboardPage page, Vector2Int pos, Vector2Int size, Vector2Int bounds)
    {
        if (pos.x < 0 || pos.y < 0 || pos.x + size.x > bounds.x || pos.y + size.y > bounds.y) return false;
        foreach (var other in page.chartDatas)
            if (pos.x + size.x > other.pos.x && other.pos.x + other.size.x > pos.x &&
                pos.y + size.y > other.pos.y && other.pos.y + other.size.y > pos.y) return false;
        return true;
    }

    public static bool CopyIntoEmptyPage(DashboardPage source, DashboardPage target)
    {
        if (source == target || target.chartDatas.Count != 0 || source.chartDatas.Count == 0) return false;
        foreach (var chart in source.chartDatas)
            target.chartDatas.Add(new ChartData
            {
                pos = chart.pos, size = chart.size, depth = chart.depth,
                statPlanId = chart.statPlanId, presetIndex = chart.presetIndex,
                genericStyleIndex = chart.genericStyleIndex,
                backgroundStyleIndex = chart.backgroundStyleIndex,
                borderStyleIndex = chart.borderStyleIndex,
                displayTypeParams = (int[])chart.displayTypeParams.Clone()
            });
        return true;
    }
}
