using HarmonyLib;

namespace BlueprintSearch.Patches;

internal static class UIBlueprintBrowserPatches
{
    internal static SearchBarUI searchBarUI;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIBlueprintBrowser), "_OnCreate")]
    static void OnCreate_Postfix(UIBlueprintBrowser __instance)
    {
        if (searchBarUI != null) return; // guard against double-create
        searchBarUI = SearchBarUI.Create(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIBlueprintBrowser), "_OnOpen")]
    static void OnOpen_Postfix(UIBlueprintBrowser __instance)
    {
        // Clear any stale query and restore the UI to empty without firing onValueChanged.
        if (searchBarUI != null && searchBarUI.inputField != null)
            searchBarUI.inputField.SetTextWithoutNotify("");
        SearchState.ClearQuery();

        if (SearchState.cacheDirty)
        {
            int rootLen = __instance.rootPath != null ? __instance.rootPath.Length : 0;
            SearchState.RebuildCache(__instance.rootPath, rootLen, BlueprintSearchPlugin.Logger);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIBlueprintBrowser), "_OnClose")]
    static void OnClose_Postfix()
    {
        SearchState.ClearQuery();
    }
}
