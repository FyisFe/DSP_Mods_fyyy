namespace DashboardOverhaul;

/// <summary>
/// 仪表盘分页的纯逻辑层：只操作数据结构，不触碰任何 Unity UI。
/// 页索引域 1..9（pages[0] 永不使用）。删除采用"置空槽、不移位"。
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

    /// <summary>第一个非空槽（1..9），没有返回 -1。用于把无效的当前页指回有效页。</summary>
    public static int FirstActiveSlot(DashboardLayout layout)
    {
        var pages = layout?.pages;
        if (pages == null) return -1;
        for (int i = 1; i < DashboardLayout.MAX_PAGE_COUNT; i++)
            if (pages[i] != null) return i;
        return -1;
    }

    /// <summary>currentView.pageIndex 是否指向一个有效的非空页（1..9）。</summary>
    public static bool IsValidViewPage(CustomCharts charts)
    {
        var pages = charts?.dashboardLayout?.pages;
        if (pages == null) return false;
        int cur = charts.currentView.pageIndex;
        return cur >= 1 && cur < DashboardLayout.MAX_PAGE_COUNT && pages[cur] != null;
    }

    /// <summary>占用最小空槽并初始化一页；返回新页槽号，满则 -1。</summary>
    public static int AddPage(CustomCharts charts)
    {
        var layout = charts.dashboardLayout;
        int slot = FirstFreeSlot(layout);
        if (slot < 0) return -1;
        layout.AddPage(slot); // 原版 AddPage：new DashboardPage().Init()，name = slot.ToString()
        return slot;
    }

    public static bool CanDelete(CustomCharts charts) => ActivePageCount(charts) > 1;

    /// <summary>删 deletedIndex 后应跳向的槽：先向小页号找，再向大页号找；都没有返回 -1。</summary>
    public static int PickPageAfterDelete(DashboardLayout layout, int deletedIndex)
    {
        var pages = layout.pages;
        for (int i = deletedIndex - 1; i >= 1; i--)
            if (pages[i] != null) return i;
        for (int i = deletedIndex + 1; i < DashboardLayout.MAX_PAGE_COUNT; i++)
            if (pages[i] != null) return i;
        return -1;
    }

    /// <summary>释放该页所有图表并置空槽位。不负责切页（调用方处理 currentView 与刷新）。</summary>
    public static bool RemovePage(CustomCharts charts, int index)
    {
        if (index < 1 || index >= DashboardLayout.MAX_PAGE_COUNT) return false;
        var pages = charts.dashboardLayout.pages;
        var page = pages[index];
        if (page == null) return false;
        // 逐个释放图表（DashboardPage.Free 会清空 chartDatas）
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
