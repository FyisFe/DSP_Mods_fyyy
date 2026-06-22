using System.Collections.Generic;

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

    /// <summary>Claims the lowest free slot and initializes a page; returns the new slot, or -1 if full.</summary>
    public static int AddPage(CustomCharts charts)
    {
        var layout = charts.dashboardLayout;
        int slot = FirstFreeSlot(layout);
        if (slot < 0) return -1;
        layout.AddPage(slot); // vanilla AddPage: new DashboardPage().Init(), name = slot.ToString()
        return slot;
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
    /// mismatch this is a no-op (defensive). Repoints currentView.pageIndex to wherever the
    /// previously-viewed page object lands, so the player stays on the same page. Slot index is the
    /// page's save key (DashboardLayout.Export/Import is slot-by-slot), so the new order persists on
    /// the next game save with no format change.
    /// </summary>
    public static void ReorderPages(CustomCharts charts, IReadOnlyList<DashboardPage> newOrder)
    {
        var pages = charts?.dashboardLayout?.pages;
        if (pages == null || newOrder == null) return;

        // Validate that newOrder is a permutation of the current non-null pages.
        int active = ActivePageCount(charts);
        var set = new HashSet<DashboardPage>();
        foreach (var p in newOrder)
        {
            if (p == null) return;
            set.Add(p);
        }
        if (set.Count != active) return;                 // duplicates or wrong count
        for (int i = 1; i < DashboardLayout.MAX_PAGE_COUNT; i++)
            if (pages[i] != null && !set.Contains(pages[i])) return; // a current page is missing

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
    }

    public static void RenamePage(DashboardPage page, string newName)
    {
        if (page == null) return;
        page.name = (newName ?? string.Empty).Trim();
    }
}
