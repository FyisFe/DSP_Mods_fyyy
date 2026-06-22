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
    static void OnOpen_Postfix()
    {
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
