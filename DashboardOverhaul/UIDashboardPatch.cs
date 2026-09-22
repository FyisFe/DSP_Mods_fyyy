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

    [HarmonyPrefix]
    [HarmonyPatch(typeof(UIDashboard), "_OnOpen")]
    static void OnOpen_Prefix(UIDashboard __instance) => PageOps.EnsureViewPage(__instance.charts);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIDashboard), "_OnOpen")]
    static void OnOpen_Postfix() => Bar?.Refresh();

    [HarmonyPrefix]
    [HarmonyPatch(typeof(UIDashboard), nameof(UIDashboard.SetViewPage))]
    static void SetViewPage_Prefix(UIDashboard __instance)
    {
        Bar?.FinishRename();
        ChartRename.Finish();
        __instance.CloseChartPopupMenu();
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(UIDashboard), "_OnClose")]
    static void OnClose_Prefix() => Bar?.Close();

    [HarmonyPrefix]
    [HarmonyPatch(typeof(UIDashboard), "_OnFree")]
    static void OnFree_Prefix() => Bar?.Close();

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIDashboard), "_OnUpdate")]
    static void OnUpdate_Postfix() => Bar?.UpdateLayout();

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIDashboard), "_OnDestroy")]
    static void OnDestroy_Postfix()
    {
        Bar?.Free();
        Bar = null;
        ChartRename.Free();
    }
}
