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

    private static void UpdateToolbarInteractable(UIBlueprintBrowser browser)
    {
        bool interactable = !SearchState.Active;
        browser.cutButton.interactable = interactable;
        browser.newFileButton.interactable = interactable;
        browser.newFolderButton.interactable = interactable;
        browser.upLevelButton.interactable = interactable;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIBlueprintBrowser), "_OnOpen")]
    static void OnOpen_Postfix(UIBlueprintBrowser __instance)
    {
        if (searchBarUI != null && searchBarUI.inputField != null)
            searchBarUI.inputField.SetTextWithoutNotify("");
        if (searchBarUI != null) searchBarUI.RefreshPlaceholder();
        SearchState.ClearQuery();

        if (SearchState.cacheDirty)
        {
            int rootLen = __instance.rootPath != null ? __instance.rootPath.Length : 0;
            SearchState.RebuildCache(__instance.rootPath, rootLen, BlueprintSearchPlugin.Logger);
        }
        UpdateToolbarInteractable(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIBlueprintBrowser), "_OnClose")]
    static void OnClose_Postfix(UIBlueprintBrowser __instance)
    {
        CancelStreaming();
        SearchState.ClearQuery();
        // Restore interactable state in case the browser reopens with a different mod state.
        if (__instance != null) UpdateToolbarInteractable(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIBlueprintBrowser), nameof(UIBlueprintBrowser.SetCurrentDirectory))]
    static void SetCurrentDirectory_Postfix(UIBlueprintBrowser __instance)
    {
        UpdateToolbarInteractable(__instance);
        if (!SearchState.Active) return;
        RepopulateWithResults(__instance);
    }

    // Progressive rendering budget. Each opened file item is expensive (blueprint preview load),
    // so we cap work per frame and stream the rest via SearchBarUI.Update -> ContinueStreaming.
    private const int MatchesPerFrame = 16;
    private const int ScanPerFrame = 5000;

    private static UIBlueprintBrowser _streamBrowser;
    private static int _streamNextIndex;
    private static int _streamMatches;
    private static int _streamY;

    internal static void CancelStreaming()
    {
        _streamBrowser = null;
        _streamNextIndex = 0;
        _streamMatches = 0;
        _streamY = 0;
    }

    private static void RepopulateWithResults(UIBlueprintBrowser browser)
    {
        // Clear the file items that vanilla just populated for the current folder.
        foreach (var fi in browser.fileItems)
        {
            if (fi.inited) fi._Free();
        }

        _streamBrowser = browser;
        _streamNextIndex = 0;
        _streamMatches = 0;
        _streamY = 0;

        RunStreamBatch();
    }

    internal static void ContinueStreaming()
    {
        if (_streamBrowser == null) return;
        if (!SearchState.Active) { CancelStreaming(); return; }
        // Another keystroke is already pending — don't burn cycles on items the imminent
        // SetCurrentDirectory will throw away.
        if (SearchState.pendingRefresh) return;
        RunStreamBatch();
    }

    private static void RunStreamBatch()
    {
        var browser = _streamBrowser;
        var entries = SearchState.cachedEntries;
        int maxResults = BlueprintSearchPlugin.MaxResults?.Value ?? 0;
        int matchesAdded = 0;
        int scanned = 0;
        bool hardCapHit = false;

        while (matchesAdded < MatchesPerFrame
               && scanned < ScanPerFrame
               && _streamNextIndex < entries.Count)
        {
            if (maxResults > 0 && _streamMatches >= maxResults) { hardCapHit = true; break; }
            int i = _streamNextIndex++;
            scanned++;
            if (!SearchFilter.Matches(entries[i].relLower, SearchState.tokens)) continue;

            string relOrig = entries[i].relOriginal;
            string fullPath = browser.rootPath + relOrig;
            string shortName = ComposeLabel(relOrig);

            var item = GetOrCreateFileItemViaReflection(browser);
            if (item == null) { _streamNextIndex = entries.Count; break; }
            item._Init(browser.data);
            _streamY = item.SetItemLayout(_streamMatches, false, fullPath, shortName);
            item._Open();
            _streamMatches++;
            matchesAdded++;
        }

        if (matchesAdded > 0)
        {
            browser.contentTrans.sizeDelta = new UnityEngine.Vector2(
                browser.contentTrans.sizeDelta.x, (float)_streamY);
        }

        bool done = hardCapHit
                 || _streamNextIndex >= entries.Count
                 || (maxResults > 0 && _streamMatches >= maxResults);
        if (done)
        {
            browser.emptyTipText.gameObject.SetActive(_streamMatches == 0);
            CancelStreaming();
        }
        else
        {
            // Streaming still going: suppress the empty tip so highly selective queries
            // don't flicker "(empty)" for the first few frames before a match lands.
            browser.emptyTipText.gameObject.SetActive(false);
        }
    }

    // GetOrCreateFileItem is private in UIBlueprintBrowser. Use AccessTools to invoke it.
    private static readonly System.Reflection.MethodInfo GetOrCreateFileItemMI =
        AccessTools.Method(typeof(UIBlueprintBrowser), "GetOrCreateFileItem");

    private static bool _missingMIReported;

    private static UIBlueprintFileItem GetOrCreateFileItemViaReflection(UIBlueprintBrowser browser)
    {
        if (GetOrCreateFileItemMI == null)
        {
            if (!_missingMIReported)
            {
                _missingMIReported = true;
                BlueprintSearchPlugin.Logger.LogError(
                    "BlueprintSearch: UIBlueprintBrowser.GetOrCreateFileItem not found — search results disabled.");
            }
            return null;
        }
        return (UIBlueprintFileItem)GetOrCreateFileItemMI.Invoke(browser, null);
    }

    /// <summary>
    /// "parentFolder / fileName" without extension. If path is deeper than one folder,
    /// take the immediate parent only. Middle-ellipsis truncation happens downstream in the
    /// tile's shortText via Unity's auto-truncation; we just produce a readable full string.
    /// </summary>
    internal static string ComposeLabel(string relPath)
    {
        // relPath uses '/' and ends in ".txt"
        string withoutExt = relPath.EndsWith(".txt", System.StringComparison.OrdinalIgnoreCase)
            ? relPath.Substring(0, relPath.Length - 4)
            : relPath;
        int lastSlash = withoutExt.LastIndexOf('/');
        if (lastSlash <= 0) return withoutExt.Substring(lastSlash + 1);
        string fileName = withoutExt.Substring(lastSlash + 1);
        int prevSlash = withoutExt.LastIndexOf('/', lastSlash - 1);
        string parent = prevSlash < 0
            ? withoutExt.Substring(0, lastSlash)
            : withoutExt.Substring(prevSlash + 1, lastSlash - prevSlash - 1);
        return parent + " / " + fileName;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIBlueprintBrowser), "OnNewFileButtonClick")]
    static void OnNewFileButtonClick_Postfix()
    {
        SearchState.cacheDirty = true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIBlueprintBrowser), "OnNewFolderButtonClick")]
    static void OnNewFolderButtonClick_Postfix()
    {
        SearchState.cacheDirty = true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIBlueprintInspector), "OnDeleteClick")]
    static void InspectorOnDeleteClick_Postfix()
    {
        SearchState.cacheDirty = true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIBlueprintInspector), "OnSaveChangesClick")]
    static void InspectorOnSaveChangesClick_Postfix()
    {
        SearchState.cacheDirty = true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIBlueprintBookInspector), "DoDeleteBook")]
    static void BookInspectorDoDeleteBook_Postfix()
    {
        SearchState.cacheDirty = true;
    }
}
