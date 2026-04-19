using System.IO;
using HarmonyLib;
using UnityEngine.EventSystems;

namespace BlueprintSearch.Patches;

internal static class UIBlueprintFileItemPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIBlueprintFileItem), "_OnRegEvent")]
    static void OnRegEvent_Postfix(UIBlueprintFileItem __instance)
    {
        // Add an EventTrigger that forwards right-clicks to our handler.
        // We attach to the same button's GameObject used by left-click.
        var go = __instance.button.gameObject;
        var trigger = go.GetComponent<EventTrigger>();
        if (trigger == null) trigger = go.AddComponent<EventTrigger>();

        // Guard against attaching twice if _OnRegEvent runs multiple times per item.
        foreach (var e in trigger.triggers)
        {
            if (e.eventID == EventTriggerType.PointerClick) return;
        }

        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
        entry.callback.AddListener((data) =>
        {
            var ped = (PointerEventData)data;
            if (ped.button != PointerEventData.InputButton.Right) return;
            if (!SearchState.Active) return;
            if (__instance.isDirectory) return; // Search results are files only; nothing to do.

            string containing = Path.GetDirectoryName(__instance.fullPath);
            if (string.IsNullOrEmpty(containing)) return;

            var browser = UIBlueprintBrowserPatches.searchBarUI != null
                ? UIBlueprintBrowserPatches.searchBarUI.browser
                : null;
            if (browser == null) return;

            UIBlueprintBrowserPatches.searchBarUI.inputField.SetTextWithoutNotify("");
            SearchState.ClearQuery();
            browser.SetCurrentDirectory(containing);
        });
        trigger.triggers.Add(entry);
    }
}
