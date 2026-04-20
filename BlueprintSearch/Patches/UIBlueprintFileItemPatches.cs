using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BlueprintSearch.Patches;

internal static class UIBlueprintFileItemPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIBlueprintFileItem), "_OnRegEvent")]
    static void OnRegEvent_Postfix(UIBlueprintFileItem __instance)
    {
        // Do NOT use UnityEngine.EventSystems.EventTrigger: it implements IScrollHandler and
        // the drag handlers unconditionally, so Unity's ExecuteHierarchy stops at the tile
        // button and the parent ScrollRect never receives mousewheel / drag-scroll events.
        // A click-only component preserves right-click without blocking scroll propagation.
        var go = __instance.button.gameObject;
        var handler = go.GetComponent<BlueprintItemRightClickHandler>();
        if (handler == null) handler = go.AddComponent<BlueprintItemRightClickHandler>();
        handler.item = __instance;
    }
}

internal class BlueprintItemRightClickHandler : MonoBehaviour, IPointerClickHandler
{
    internal UIBlueprintFileItem item;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Right) return;
        if (!SearchState.Active) return;
        if (item == null || item.isDirectory) return;

        string containing = Path.GetDirectoryName(item.fullPath);
        if (string.IsNullOrEmpty(containing)) return;

        var browser = UIBlueprintBrowserPatches.searchBarUI != null
            ? UIBlueprintBrowserPatches.searchBarUI.browser
            : null;
        if (browser == null) return;

        UIBlueprintBrowserPatches.searchBarUI.inputField.SetTextWithoutNotify("");
        SearchState.ClearQuery();
        browser.SetCurrentDirectory(containing);
    }
}
