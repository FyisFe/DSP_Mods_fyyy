using HarmonyLib;

namespace DashboardOverhaul;

/// <summary>
/// Harmony fixes for <see cref="DashboardLayout"/> (see each patch method for the specific behavior).
///
/// <b>Import_Prefix</b> fixes a vanilla load-time asymmetry so deleting page 1 actually sticks across
/// save/reload. DashboardLayout.Init() pre-creates pages[1] (Init → AddPage(1)), and the load order is
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

    /// <summary>
    /// Drops the vanilla auto-name every new page is given (= its creation-slot index). Page display
    /// order is derived from the slot, so an auto-named page shows a FROZEN number that goes stale
    /// after a drag-reorder and can collide with another page's live index (two "5"s). Blanking the
    /// name makes the tab fall back to the page's LIVE slot index, which always matches its position.
    /// Covers every auto-create path from one place: DashboardLayout.Init (the first page),
    /// UIDashboard.SetViewPage (auto-create on jumping to an empty slot), and the mod's + button
    /// (PageOps.AddPage). A page the player explicitly renames keeps that name. Empty name is a
    /// serialized first-class state (DashboardPage.Export/Import handle it). Not retroactive: pages
    /// already saved with an auto-name keep it until cleared.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(DashboardLayout), nameof(DashboardLayout.AddPage))]
    static void AddPage_Postfix(DashboardLayout __instance, int index)
    {
        if (__instance.pages != null && index >= 0 && index < DashboardLayout.MAX_PAGE_COUNT
            && __instance.pages[index] != null)
            __instance.pages[index].name = "";
    }
}
