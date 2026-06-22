using HarmonyLib;

namespace DashboardOverhaul;

/// <summary>
/// Fixes a vanilla load-time asymmetry so deleting page 1 actually sticks across save/reload.
///
/// DashboardLayout.Init() pre-creates pages[1] (Init → AddPage(1)), and the load order is
/// Init() THEN Import(). But DashboardLayout.Import() only ASSIGNS a slot when its per-slot
/// presence flag != 0 — it never nulls a slot whose flag == 0. So a page-1 the player deleted
/// (serialized by Export as "absent", flag 0) would resurrect on reload as the empty
/// Init-created page named "1". (Slots 2–9 are unaffected: Init only ever creates slot 1.)
///
/// Nulling pages[1] before the vanilla import loop lets a flag==0 read leave it null
/// (deletion sticks) while a flag==1 read still re-creates and imports it as normal.
/// </summary>
public static class DashboardLayoutPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(DashboardLayout), nameof(DashboardLayout.Import))]
    static void Import_Prefix(DashboardLayout __instance)
    {
        if (__instance.pages != null) __instance.pages[1] = null;
    }
}
