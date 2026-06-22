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

    public static void RenamePage(DashboardPage page, string newName)
    {
        if (page == null) return;
        page.name = (newName ?? string.Empty).Trim();
    }
}
