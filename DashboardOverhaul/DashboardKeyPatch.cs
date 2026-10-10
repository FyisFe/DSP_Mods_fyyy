using HarmonyLib;

namespace DashboardOverhaul;

/// <summary>
/// Keeps the dashboard's keys apart. The game's P handler closes the dashboard when it is open (instead
/// of opening the statistics panel), so while the dashboard is open this opens the statistics panel and
/// leaves the dashboard alone; shift+P still toggles the dashboard through the game's own handler.
/// The dashboard header's button uses <see cref="ToggleStatisticsPanel"/> for the same behaviour.
/// </summary>
public static class DashboardKeyPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(UIGame), nameof(UIGame.On_P_Switch))]
    static bool On_P_Switch_Prefix(UIGame __instance)
    {
        if (__instance.dashboard == null || !__instance.dashboard.active) return true; // vanilla elsewhere
        ToggleStatisticsPanel(__instance);
        return false; // the dashboard stays open
    }

    /// <summary>Open the statistics panel, or close it again when it is already open.</summary>
    public static void ToggleStatisticsPanel(UIGame uiGame)
    {
        if (uiGame == null) return;
        bool open = uiGame.statWindow != null && uiGame.statWindow.active;
        if (open) uiGame.ShutAllFunctionWindow();
        else uiGame.OpenProductionWindow();
    }
}
