using HarmonyLib;

namespace DashboardOverhaul;

public static class UIDashboardPatch
{
    public static PageTabBar Bar;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIDashboard), "_OnCreate")]
    static void OnCreate_Postfix(UIDashboard __instance)
    {
        Bar = new PageTabBar();
        Bar.Build(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIDashboard), "_OnOpen")]
    static void OnOpen_Postfix(UIDashboard __instance)
    {
        // 防御：若存档里的 currentView.pageIndex 指向已删除/越界的页槽（外部改档、
        // 旧版本或未来回归都可能造成），原版 CustomCharts.PrepareTick 会无判空地
        // 解引用 pages[pageIndex]，且它运行在模拟循环里、不在 UI 的 try/catch 内 ——
        // 这会硬崩游戏。开窗时把无效的当前页指回首个有效页即可彻底规避。
        if (__instance != null && !PageOps.IsValidViewPage(__instance.charts))
        {
            int target = PageOps.FirstActiveSlot(__instance.charts?.dashboardLayout);
            if (target > 0) __instance.SetViewPage(target);
        }
        if (Bar != null) Bar.Refresh();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIDashboard), "_OnUpdate")]
    static void OnUpdate_Postfix()
    {
        if (Bar != null) Bar.UpdateLayout();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIDashboard), "_OnDestroy")]
    static void OnDestroy_Postfix()
    {
        if (Bar != null) { Bar.Free(); Bar = null; }
    }
}
