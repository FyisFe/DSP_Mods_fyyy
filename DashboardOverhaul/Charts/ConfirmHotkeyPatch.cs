using HarmonyLib;

namespace DashboardOverhaul;

/// <summary>
/// Extends the Enter confirmation to message boxes the mod does not create itself: the sidebar's
/// per-statistic delete confirmation ("是否确认删除统计项"), whose "确定" button is also button 2.
/// Matched by the game's own localization key, so no other dialog is affected.
/// </summary>
public static class ConfirmHotkeyPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIMessageBox), nameof(UIMessageBox.Show),
        new[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(int),
            typeof(UIMessageBox.Response), typeof(UIMessageBox.Response) })]
    static void Show_Postfix(string title, UIMessageBox __result)
    {
        if (__result == null || title != "确认删除统计项标题".Translate()) return;
        ConfirmHotkey.Attach(__result);
    }
}
