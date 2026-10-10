using HarmonyLib;
using UnityEngine;

namespace DashboardOverhaul;

/// <summary>
/// Shift+click on the statistics window's "function menu" button adds that statistic to the dashboard
/// and opens the dashboard, replacing the "open the menu, then choose the add entry" two-step. The
/// button's tooltip gains a line describing the shortcut.
/// </summary>
public static class StatPlanShortcutPatch
{
    private const string kMenuTextKey = "弹出式菜单提示文字";

    [HarmonyPrefix]
    [HarmonyPatch(typeof(UIGenericMenuButton), "OnButtonClick")]
    static bool OnButtonClick_Prefix(UIGenericMenuButton __instance)
    {
        if (!Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift)) return true; // plain click keeps the menu
        int index = AddStatPlanIndex(__instance);
        if (index < 0) return true; // no statistic to add here

        __instance.CreateStatPlan(index);
        OpenDashboard(__instance, index);
        return false; // do not open the function menu
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIGenericMenuButton), "Update")]
    static void Update_Postfix(UIGenericMenuButton __instance)
    {
        if (AddStatPlanIndex(__instance) < 0) return;
        var button = __instance.button;
        if (button == null || string.IsNullOrEmpty(button.tips.tipText)) return; // no tip while the menu is open
        // The tip text is translated as a whole, so translate the game's line here.
        button.tips.tipText = button.tips.tipText.Translate() + "\n" +
            Loc.L("按住Shift点击快捷创建统计项并打开仪表盘",
                "Shift+click to create this statistic and open the dashboard");
    }

    /// <summary>Index of the button's "add to dashboard" menu entry, or -1 if it has none.</summary>
    private static int AddStatPlanIndex(UIGenericMenuButton button)
    {
        var menuDatas = button != null ? button.menuDatas : null;
        if (menuDatas == null) return -1;
        for (int i = 0; i < menuDatas.Count; i++)
            if (menuDatas[i].menuFunctionType == UIGenericMenuButton.EMenuFunctionType.AddStatPlan) return i;
        return -1;
    }

    /// <summary>Open the dashboard (or refresh it when already open) and highlight the added statistic.</summary>
    private static void OpenDashboard(UIGenericMenuButton button, int index)
    {
        var data = GameMain.data;
        if (data == null) return;
        var uiGame = UIRoot.instance.uiGame;
        var dashboard = uiGame.dashboard;
        if (dashboard == null) return;

        if (!dashboard.active)
        {
            uiGame.On_Shift_P_Switch();
        }
        else
        {
            dashboard.DetermineCharts();
            dashboard.statboard.DetermineEntryVisible();
        }

        var charts = data.statistics.charts;
        var menu = button.menuDatas[index];
        var filters = new[] { menu.param1, menu.param2, menu.param3 };
        var statPlan = charts.CreateOrFindStatPlan((EStatPlanType)menu.param0, filters, out _);
        if (statPlan != null) dashboard.HighlightCharts(statPlan.id);
    }
}
