using HarmonyLib;

namespace DashboardOverhaul;

public static class UIDashboardPatch
{
    public static DashboardView View { get; private set; }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIDashboard), "_OnCreate")]
    static void OnCreate_Postfix(UIDashboard __instance)
    {
        View = new DashboardView();
        View.Build(__instance);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(UIDashboard), "_OnOpen")]
    static void OnOpen_Prefix(UIDashboard __instance) => PageOps.EnsureViewPage(__instance.charts);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIDashboard), "_OnOpen")]
    static void OnOpen_Postfix() => View?.Refresh();

    [HarmonyPrefix]
    [HarmonyPatch(typeof(UIDashboard), nameof(UIDashboard.SetViewPage))]
    static void SetViewPage_Prefix(UIDashboard __instance)
    {
        View?.Tabs.FinishRename();
        ChartRename.Finish();
        __instance.CloseChartPopupMenu();
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(UIDashboard), "_OnClose")]
    static void OnClose_Prefix() => View?.Close();

    [HarmonyPrefix]
    [HarmonyPatch(typeof(UIDashboard), "_OnFree")]
    static void OnFree_Prefix()
    {
        View?.Close();
        ProductionNames.Clear();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIDashboard), "_OnUpdate")]
    static void OnUpdate_Postfix()
    {
        if (View == null) return;
        View.UpdateLayout();
        ChartHotkeys.Update(View);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIDashboard), "_OnDestroy")]
    static void OnDestroy_Postfix()
    {
        View?.Free();
        View = null;
        ChartRename.Free();
        ProductionNames.Clear();
    }
}
