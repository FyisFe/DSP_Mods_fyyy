---
name: Blueprint Search Bar
description: Add a search bar to the in-game blueprint browser window so players can locate blueprints anywhere in their library by typing part of the file or folder name
type: design
---

# Blueprint Search Bar

## Overview

Vanilla DSP's blueprint browser (`UIBlueprintBrowser`) lists only the files in the currently-open folder — there is no search. Players with large libraries organized into many nested folders have no way to jump to a blueprint without navigating the folder tree by hand. This mod adds a search bar under the browser's toolbar. When a query is active, the file grid is replaced with a flat list of matching blueprints drawn from the entire library, each labeled with its folder path. Clearing the query restores the normal per-folder view.

## Goals

1. Add a text input field to `UIBlueprintBrowser` that filters blueprints by substring match against each file's path relative to the blueprint root.
2. Search is recursive from the root (not the current folder), so a player can find a blueprint regardless of where they are in the tree.
3. Tokens are case-insensitive and space-separated; all tokens must match (logical AND). `/` is treated as a separator too, so `初期/电力` behaves like `初期 电力`.
4. While a query is active, results are a flat grid of files only (no folders), reusing the existing `UIBlueprintFileItem` tile layout. Left-click opens the blueprint inspector as usual.
5. Right-click on a result jumps to its containing folder and clears the query.
6. Zero behavior change when the input is empty — vanilla browser code runs unmodified.

## Non-Goals

- Matching blueprint descriptions or internal content (`shortDesc` inside each `.txt`). The cost of parsing every file header is high and the feature value is low. May revisit later.
- Fuzzy matching or ranking. Simple substring match is predictable and covers the observed naming patterns (`【2】初期电力`).
- Live external-change detection via `FileSystemWatcher`. Vanilla does not see external changes mid-session either; we stay consistent.
- A new browser window. We patch the existing one additively.
- Unit tests. The only pure-logic piece (`SearchFilter.Matches`) is small enough that an in-game manual pass is sufficient. Testing will be done by the user in-game.

## Architecture

### Module Layout

```
BlueprintSearch/
├── BlueprintSearch.csproj
├── BlueprintSearchPlugin.cs              ← BepInEx entry, config, Harmony apply
├── SearchState.cs                        ← query, tokens, cached path list, dirty flag
├── SearchBarUI.cs                        ← builds InputField row + clear button, owns references
├── SearchFilter.cs                       ← tokenization + AND-match
└── Patches/
    └── UIBlueprintBrowserPatches.cs
         ├── _OnCreate_Postfix            ← instantiate SearchBarUI once
         ├── _OnOpen_Postfix              ← clear query, rebuild path cache
         ├── _OnClose_Postfix             ← hide clear button, keep cache
         ├── SetCurrentDirectory_Postfix  ← if Active, wipe file grid & repopulate w/ results
         ├── UIBlueprintFileItem._OnRegEvent_Postfix  ← right-click handler
         ├── OnNewFileButtonClick_Postfix ← cache dirty
         ├── OnNewFolderButtonClick_Postfix ← cache dirty
         └── (delete hook — see Inspector delete notes below)
```

### Window Layout Change

The browser's `rectTrans` children today are (from top): toolbar + breadcrumb (`addrGroupTrans` area), then `contentTrans` (the file grid scroll area), then the right-side inspector panel.

We add one new row between the toolbar and the file grid:

```
┌─ rectTrans ────────────────────────────────────────────────┐
│ [cut][newFile][newFolder][upLevel]  Path Blueprint / …   [查看蓝图文件] │
│ ┌─ searchBarRow (new, 24px) ──────────────────────────┐   │
│ │ [ InputField: placeholder "搜索蓝图..."     ] [ × ] │   │
│ └──────────────────────────────────────────────────────┘   │
│ ┌─ contentTrans (shifted down 28px) ───────────────────┐   │
│ │  file grid                                           │   │
│ └──────────────────────────────────────────────────────┘   │
└────────────────────────────────────────────────────────────┘
```

The shift is applied once in the `_OnCreate` postfix by adjusting `contentTrans`'s anchored position and size delta. Done once — no runtime layout jitter.

## Components & Data

### `SearchState` (static; there is only one browser instance)

```csharp
internal static class SearchState
{
    internal static string query = "";
    internal static string[] tokens = Array.Empty<string>();
    internal static List<string> cachedRelativePaths; // lowercased, forward-slashed, relative to rootPath
    internal static bool cacheDirty = true;
    internal static float lastChangeTime;  // Time.unscaledTime when onValueChanged last fired
    internal static bool pendingRefresh;   // set by onValueChanged, cleared in Update after debounce

    internal static bool Active => tokens.Length > 0;
}
```

### `SearchBarUI`

- Instantiated once from `UIBlueprintBrowser._OnCreate` postfix, parented to `browser.rectTrans`.
- Contains a Unity `InputField` (placeholder text localized via `UXAssist.Common.I18N` to English / Simplified Chinese) and a "×" button next to it.
- `InputField.onValueChanged` sets `SearchState.query`, flags `pendingRefresh`, and stamps `lastChangeTime`. The 120ms debounce is applied from a small `MonoBehaviour` Update hook that also lives on this UI object.
- Hides or disables itself based on `BlueprintSearchPlugin.ModEnabled.Value`.

### `SearchFilter`

```csharp
internal static class SearchFilter
{
    private static readonly char[] Separators = { ' ', '\t', '/', '\\' };

    internal static string[] Tokenize(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<string>();
        return query.ToLowerInvariant().Split(Separators, StringSplitOptions.RemoveEmptyEntries);
    }

    internal static bool Matches(string pathLower, string[] tokens)
    {
        for (int i = 0; i < tokens.Length; i++)
            if (pathLower.IndexOf(tokens[i], StringComparison.Ordinal) < 0)
                return false;
        return true;
    }
}
```

Whitespace AND `/` AND `\` all split tokens, so pasted path fragments work.

### Path Cache

- Built by `RebuildCache(UIBlueprintBrowser browser)` which calls `Directory.EnumerateFiles(browser.rootPath, "*.txt", SearchOption.AllDirectories)`, then for each full path produces `relLower = fullPath.Substring(browser.rootPathLen).Replace('\\', '/').ToLowerInvariant()`. Results go into `SearchState.cachedRelativePaths`.
- The rebuild is synchronous and main-thread. Libraries with a few thousand files finish in under 50ms on a consumer SSD, well within a frame budget's worth of stutter that only happens on open or after a modify. No async/background-thread infrastructure is introduced.
- Guarded per-subtree: the enumerator is wrapped so an `UnauthorizedAccessException` or `IOException` on one directory is logged once and skipped. The rest of the library still enumerates.
- Alongside the lowercased relative path, we also keep the original (preserving case) so the file tile can display human-readable labels. The cache stores both as a parallel list or a small struct per entry.

### BepInEx Config

```csharp
Enabled = Config.Bind("General", "Enabled", true,
    "Enable search bar in blueprint browser / 在蓝图库窗口启用搜索栏");

MaxResults = Config.Bind("General", "MaxResults", 256,
    "Maximum number of results shown for a query (UI responsiveness guard)");

DebounceMs = Config.Bind("General", "DebounceMs", 120,
    "Milliseconds to wait after the last keystroke before recomputing results");
```

Config changes apply live: `Enabled` toggling re-patches Harmony (`FastTinderLaunch` pattern), hides/shows the bar, and resets search state.

## Data Flow

### Browser open (`_OnOpen` postfix)

1. Vanilla `_OnOpen` runs, calling `SetCurrentDirectory(openPath || rootPath)`. Our postfix on that call fires last.
2. Postfix clears `SearchState.query` and sets the `InputField` text to `""` with `SetTextWithoutNotify` so no spurious `onValueChanged` fires.
3. If `cacheDirty`, rebuild the path cache.

### Every keystroke

1. `InputField.onValueChanged(text)` → `SearchState.query = text`; `lastChangeTime = Time.unscaledTime`; `pendingRefresh = true`.
2. In `SearchBarUI.Update`: if `pendingRefresh && (now - lastChangeTime) * 1000 >= DebounceMs`, clear `pendingRefresh`, recompute `tokens = SearchFilter.Tokenize(query)`, then call `browser.SetCurrentDirectory(browser.currentDirectoryInfo.FullName)` to trigger a re-render.
3. Vanilla `SetCurrentDirectory` runs — builds the breadcrumb, fills the grid with the current folder's files.
4. Our `SetCurrentDirectory` postfix sees `SearchState.Active == true`, calls `RepopulateWithResults(browser)`:
   - `ClearFileItems()`
   - Iterate `SearchState.cachedRelativePaths`; for each matching path, pull the original-case version, get a file item via `GetOrCreateFileItem()`, call `fileItem._Init(browser.data)`, then `fileItem.SetItemLayout(index, isdir: false, fullPath: browser.rootPath + relOriginalCase, shortName: ComposeLabel(relOriginalCase))`, then `fileItem._Open()`.
   - Stop iterating at `MaxResults` matches.
   - `browser.emptyTipText.gameObject.SetActive(matches == 0)`, update `browser.contentTrans.sizeDelta.y` to `last y + padding` using the same formula as vanilla.
5. While `Active`, disable `cutButton`, `newFileButton`, `newFolderButton`, `upLevelButton` (set `interactable = false`) — their actions don't make sense against a flat search result. Restore on clear.

### `ComposeLabel(relOriginalCase)`

- If no `/` in the path: return the file name without `.txt`.
- Otherwise return `parentFolder / fileName-without-ext`, truncating the middle with `…` to fit the fixed `shortText` width. Using the middle-ellipsis rather than right-truncation keeps the file name visible (which is usually what the user is searching for).

### Right-click on a result (`UIBlueprintFileItem._OnRegEvent` postfix)

1. Attach a Unity `EventTrigger` (or a single `PointerClickHandler`) that checks `eventData.button == PointerEventData.InputButton.Right`.
2. If `SearchState.Active` and right-clicked: compute `containingFolder = Path.GetDirectoryName(fileItem.fullPath)`, set `InputField.text` to `""` via `SetTextWithoutNotify`, clear `SearchState.query` / `tokens`, re-enable the toolbar buttons, then call `browser.SetCurrentDirectory(containingFolder)`.
3. Left-click behavior is untouched; vanilla click handler still fires for left-click.

### New file / new folder / delete

- Postfix on `UIBlueprintBrowser.OnNewFileButtonClick` and `UIBlueprintBrowser.OnNewFolderButtonClick`: set `SearchState.cacheDirty = true`. These are gated by `!Active` because we disable the buttons while searching.
- Delete / rename invalidations (set `cacheDirty = true` via postfix):
  - `UIBlueprintInspector.OnDeleteClick` (calls `File.Delete` on the single blueprint)
  - `UIBlueprintInspector.OnSaveChangesClick` (can `File.Delete` the old file when a save-path change renames it)
  - `UIBlueprintBookInspector.DoDeleteBook` (calls `Directory.Delete` on a folder)

### Browser close (`_OnClose` postfix)

- `SearchState.query = ""`, `tokens = Array.Empty<string>()`.
- Keep `cachedRelativePaths` unless dirty — a reopen without library changes can reuse it.

### Mod disabled at runtime

- Harmony unpatches. `SearchBarUI` GameObject set inactive (don't destroy — re-enable should restore cleanly).
- If the browser is open, force `SearchState` reset and call `SetCurrentDirectory(currentDirectoryInfo.FullName)` so the view reverts to vanilla.

## Error Handling & Edge Cases

- **I/O errors during cache build**: per-subtree try/catch; log once; skip that subtree; the rest of the library is still searchable.
- **`rootPath` doesn't exist**: vanilla creates it on `_OnCreate`; if it's missing at cache-build time, cache is empty and search shows no results. No exception.
- **Right-click when search is inactive**: the handler no-ops. Vanilla left-click unchanged.
- **Very long path labels**: `ComposeLabel` middle-truncates to fit `shortText` width. `fullPath` is intact.
- **Zero matches**: vanilla `emptyTipText` toggles on. Reuse the existing localized string.
- **Query contains slashes / pasted path fragments**: tokenizer splits on `/` and `\` in addition to whitespace.
- **IME composition (Chinese, Japanese)**: Unity `InputField.onValueChanged` fires on commit, not per raw key. The 120ms debounce further smooths composition.
- **External file changes mid-session**: not detected. Consistent with vanilla, which only sees files that existed at folder-open time.
- **Simultaneous search and navigation**: while `Active`, navigation buttons (up-level, cut, new-file, new-folder) are disabled. Clearing the query re-enables them.

## Out of Scope / Future Work

- Description / internal-content search.
- Fuzzy matching / result ranking.
- `FileSystemWatcher` for external changes.
- Keyboard shortcut (e.g. Ctrl+F) to focus the search bar.
- Persistent search history.
- Search integration inside `UIBlueprintBookInspector` (book contents).
