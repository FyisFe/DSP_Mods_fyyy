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
        // Defense-in-depth: if a save's currentView.pageIndex points at a deleted/
        // out-of-range slot (possible via external save edits, an older mod version,
        // or a future regression), vanilla CustomCharts.PrepareTick dereferences
        // pages[pageIndex] with no null check, and it runs in the sim loop outside
        // the UI try/catch -- a hard crash. Repointing an invalid current page to the
        // first active page on open avoids it entirely.
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
        ChartRename.Free();
    }
}
