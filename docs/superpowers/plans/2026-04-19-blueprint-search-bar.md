# Blueprint Search Bar Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a search bar to `UIBlueprintBrowser` that filters blueprints recursively by path using case-insensitive AND tokens.

**Architecture:** New BepInEx/Harmony mod `BlueprintSearch` patching `UIBlueprintBrowser`. A postfix on `_OnCreate` builds an `InputField` row beneath the toolbar. A postfix on `SetCurrentDirectory` replaces the file grid contents with search hits when a query is active. A debounced driver on a `MonoBehaviour` ticks refreshes. Right-click on a result jumps to its containing folder.

**Tech Stack:** C# / .NET Framework 4.7.2, BepInEx 5.x, HarmonyLib, Unity UI (UnityEngine.UI.InputField, EventTrigger).

**Testing strategy:** No unit tests in this project. Each task ends with a `dotnet build` verification. The user manually tests in-game at natural checkpoints (noted per task). Do **not** mark a task complete until the build succeeds.

**Spec reference:** `docs/superpowers/specs/2026-04-19-blueprint-search-bar-design.md`

---

## File Structure

| Path | Responsibility |
|---|---|
| `BlueprintSearch/BlueprintSearch.csproj` | Project file (.NET 4.7.2, BepInEx, Harmony, Unity refs) |
| `BlueprintSearch/BlueprintSearchPlugin.cs` | BepInEx entry; config binds; Harmony apply / re-apply; localization helper |
| `BlueprintSearch/SearchFilter.cs` | Pure: `Tokenize`, `Matches` |
| `BlueprintSearch/SearchState.cs` | Static: `query`, `tokens`, `cachedEntries`, `cacheDirty`, `Active`; `RebuildCache` |
| `BlueprintSearch/SearchBarUI.cs` | MonoBehaviour; owns `InputField` + clear button; debounce driver in `Update` |
| `BlueprintSearch/Patches/UIBlueprintBrowserPatches.cs` | Lifecycle + `SetCurrentDirectory` postfixes; toolbar gating; cache-invalidation hooks |
| `BlueprintSearch/Patches/UIBlueprintFileItemPatches.cs` | Right-click handler attachment |
| `BlueprintSearch/package/manifest.json` | Thunderstore/BepInEx package manifest |
| `BlueprintSearch/package/icon.png` | Package icon (copied from another mod) |
| `DSP_Mods_fyyy.sln` | Add new project entry |

---

## Task 1: Scaffold project

**Files:**
- Create: `BlueprintSearch/BlueprintSearch.csproj`
- Create: `BlueprintSearch/BlueprintSearchPlugin.cs`
- Create: `BlueprintSearch/package/manifest.json`
- Create: `BlueprintSearch/package/icon.png` (copy from `FullPhotonReceiver/package/icon.png`)
- Modify: `DSP_Mods_fyyy.sln` (add project entry)

- [ ] **Step 1: Create csproj**

File `BlueprintSearch/BlueprintSearch.csproj`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <AssemblyName>BlueprintSearch</AssemblyName>
    <BepInExPluginGuid>org.fyyy.blueprintsearch</BepInExPluginGuid>
    <Description>Search bar for the blueprint browser / 蓝图库搜索栏</Description>
    <Version>1.0.0</Version>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <LangVersion>latest</LangVersion>
    <RestoreAdditionalProjectSources>https://nuget.bepinex.dev/v3/index.json</RestoreAdditionalProjectSources>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="BepInEx.Core" Version="5.*" />
    <PackageReference Include="BepInEx.PluginInfoProps" Version="1.*" />
    <PackageReference Include="UnityEngine.Modules" Version="2022.3.62" IncludeAssets="compile" />
  </ItemGroup>

  <ItemGroup>
    <Reference Include="Assembly-CSharp">
      <HintPath>..\..\DSP_Mods\AssemblyFromGame\Assembly-CSharp.dll</HintPath>
    </Reference>
  </ItemGroup>

  <ItemGroup Condition="'$(TargetFramework.TrimEnd(`0123456789`))' == 'net'">
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />
  </ItemGroup>

  <Target Name="PostBuild" AfterTargets="PostBuildEvent" Condition="'$(Configuration)' == 'Release'">
    <Exec Command="del /F /Q package\$(ProjectName)-$(Version).zip
powershell Compress-Archive -Force -DestinationPath 'package/$(ProjectName)-$(Version).zip' -Path &quot;$(TargetPath)&quot;, package/icon.png, package/manifest.json" />
  </Target>

</Project>
```

- [ ] **Step 2: Create minimal plugin entry**

File `BlueprintSearch/BlueprintSearchPlugin.cs`:

```csharp
using BepInEx;
using HarmonyLib;

namespace BlueprintSearch;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class BlueprintSearchPlugin : BaseUnityPlugin
{
    public new static readonly BepInEx.Logging.ManualLogSource Logger =
        BepInEx.Logging.Logger.CreateLogSource(PluginInfo.PLUGIN_NAME);

    private Harmony _harmony;

    private void Awake()
    {
        _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
        // Patches applied in later tasks.
        Logger.LogInfo("BlueprintSearch loaded.");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
```

- [ ] **Step 3: Create package manifest**

File `BlueprintSearch/package/manifest.json`:

```json
{
  "name": "BlueprintSearch",
  "version_number": "1.0.0",
  "website_url": "",
  "description": "Search bar for the blueprint browser / 蓝图库搜索栏",
  "dependencies": ["BepInEx-BepInExPack-5.4.21"]
}
```

- [ ] **Step 4: Copy icon**

```bash
cp FullPhotonReceiver/package/icon.png BlueprintSearch/package/icon.png
```

(If `FullPhotonReceiver/package/icon.png` is missing, copy from any sibling mod that has one.)

- [ ] **Step 5: Add project to solution**

Generate a fresh GUID for the project (e.g. using `uuidgen` or any generator), then insert these lines into `DSP_Mods_fyyy.sln`:

After the last existing `Project(...) / EndProject` pair (the `FullPhotonReceiver` entry), add:

```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "BlueprintSearch", "BlueprintSearch\BlueprintSearch.csproj", "{NEW-GUID-HERE}"
EndProject
```

Inside `GlobalSection(ProjectConfigurationPlatforms) = postSolution`, append:

```
		{NEW-GUID-HERE}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{NEW-GUID-HERE}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{NEW-GUID-HERE}.Debug|x64.ActiveCfg = Debug|Any CPU
		{NEW-GUID-HERE}.Debug|x64.Build.0 = Debug|Any CPU
		{NEW-GUID-HERE}.Debug|x86.ActiveCfg = Debug|Any CPU
		{NEW-GUID-HERE}.Debug|x86.Build.0 = Debug|Any CPU
		{NEW-GUID-HERE}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{NEW-GUID-HERE}.Release|Any CPU.Build.0 = Release|Any CPU
		{NEW-GUID-HERE}.Release|x64.ActiveCfg = Release|Any CPU
		{NEW-GUID-HERE}.Release|x64.Build.0 = Release|Any CPU
		{NEW-GUID-HERE}.Release|x86.ActiveCfg = Release|Any CPU
		{NEW-GUID-HERE}.Release|x86.Build.0 = Release|Any CPU
```

Replace `{NEW-GUID-HERE}` with the generated GUID (keep the curly braces).

- [ ] **Step 6: Build**

```bash
dotnet build BlueprintSearch/BlueprintSearch.csproj -c Release
```

Expected: `Build succeeded. 0 Warning(s), 0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add BlueprintSearch/ DSP_Mods_fyyy.sln
git commit -m "chore(BlueprintSearch): scaffold project"
```

---

## Task 2: SearchFilter (pure logic)

**Files:**
- Create: `BlueprintSearch/SearchFilter.cs`

- [ ] **Step 1: Write SearchFilter**

File `BlueprintSearch/SearchFilter.cs`:

```csharp
using System;

namespace BlueprintSearch;

internal static class SearchFilter
{
    private static readonly char[] Separators = { ' ', '\t', '/', '\\' };

    /// <summary>
    /// Split the query into lowercased, non-empty tokens using whitespace and path separators.
    /// </summary>
    internal static string[] Tokenize(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<string>();
        return query.ToLowerInvariant().Split(Separators, StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Returns true iff every token is a substring of pathLower (logical AND).
    /// Caller must ensure pathLower is already lowercased and tokens.Length > 0.
    /// </summary>
    internal static bool Matches(string pathLower, string[] tokens)
    {
        for (int i = 0; i < tokens.Length; i++)
        {
            if (pathLower.IndexOf(tokens[i], StringComparison.Ordinal) < 0)
                return false;
        }
        return true;
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build BlueprintSearch/BlueprintSearch.csproj -c Release
```

Expected: `Build succeeded. 0 Warning(s), 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add BlueprintSearch/SearchFilter.cs
git commit -m "feat(BlueprintSearch): add SearchFilter tokenizer and AND matcher"
```

---

## Task 3: SearchState + path cache

**Files:**
- Create: `BlueprintSearch/SearchState.cs`

- [ ] **Step 1: Write SearchState**

File `BlueprintSearch/SearchState.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;

namespace BlueprintSearch;

/// <summary>
/// Per-session state for the blueprint search feature. Single instance (one browser window).
/// </summary>
internal static class SearchState
{
    internal struct PathEntry
    {
        /// <summary>Relative path from rootPath, lower-cased, forward-slash separators. Match target.</summary>
        public string relLower;
        /// <summary>Relative path from rootPath, original case, forward-slash separators. Display + fullPath source.</summary>
        public string relOriginal;
    }

    internal static string query = "";
    internal static string[] tokens = Array.Empty<string>();
    internal static readonly List<PathEntry> cachedEntries = new();
    internal static bool cacheDirty = true;
    internal static float lastChangeTime;
    internal static bool pendingRefresh;

    internal static bool Active => tokens.Length > 0;

    /// <summary>
    /// Enumerate every .txt blueprint under rootPath. Per-subtree try/catch so one bad folder
    /// does not fail the whole cache. Main thread; typical libraries finish in &lt;50ms.
    /// </summary>
    internal static void RebuildCache(string rootPath, int rootPathLen,
        BepInEx.Logging.ManualLogSource logger)
    {
        cachedEntries.Clear();
        if (!Directory.Exists(rootPath))
        {
            cacheDirty = false;
            return;
        }
        EnumerateDirectory(rootPath, rootPathLen, logger);
        cacheDirty = false;
    }

    private static void EnumerateDirectory(string dirFull, int rootPathLen,
        BepInEx.Logging.ManualLogSource logger)
    {
        string[] files;
        string[] subDirs;
        try
        {
            files = Directory.GetFiles(dirFull, "*.txt", SearchOption.TopDirectoryOnly);
            subDirs = Directory.GetDirectories(dirFull, "*", SearchOption.TopDirectoryOnly);
        }
        catch (Exception e)
        {
            logger.LogWarning($"BlueprintSearch: skipping {dirFull}: {e.GetType().Name}: {e.Message}");
            return;
        }
        foreach (string f in files)
        {
            // f is an absolute path under rootPath. Slice off the rootPath prefix.
            if (f.Length <= rootPathLen) continue;
            string rel = f.Substring(rootPathLen).Replace('\\', '/');
            cachedEntries.Add(new PathEntry
            {
                relLower = rel.ToLowerInvariant(),
                relOriginal = rel,
            });
        }
        foreach (string sub in subDirs)
        {
            EnumerateDirectory(sub, rootPathLen, logger);
        }
    }

    internal static void ClearQuery()
    {
        query = "";
        tokens = Array.Empty<string>();
        pendingRefresh = false;
    }

    internal static void Reset()
    {
        ClearQuery();
        cachedEntries.Clear();
        cacheDirty = true;
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build BlueprintSearch/BlueprintSearch.csproj -c Release
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add BlueprintSearch/SearchState.cs
git commit -m "feat(BlueprintSearch): add SearchState and path-cache builder"
```

---

## Task 4: SearchBarUI (InputField + clear button + debounce)

**Files:**
- Create: `BlueprintSearch/SearchBarUI.cs`

This task builds the UI and wires value changes to `SearchState`, but refresh itself is a no-op until Task 6 (the `SetCurrentDirectory` postfix), so after this task the bar appears but nothing filters yet.

- [ ] **Step 1: Write SearchBarUI**

File `BlueprintSearch/SearchBarUI.cs`:

```csharp
using UnityEngine;
using UnityEngine.UI;

namespace BlueprintSearch;

/// <summary>
/// Builds and owns the search input + clear button. Parented to UIBlueprintBrowser.rectTrans.
/// A single instance is created in the UIBlueprintBrowser._OnCreate postfix.
/// </summary>
internal class SearchBarUI : MonoBehaviour
{
    private const float BarHeight = 24f;
    private const float BarMarginTop = 40f;   // below the toolbar row
    private const float BarMarginSides = 10f;
    private const float ClearButtonWidth = 24f;
    private const float ContentShift = BarHeight + 4f; // 28f, applied to contentTrans top

    internal UIBlueprintBrowser browser;
    internal InputField inputField;
    internal Button clearButton;

    /// <summary>
    /// Construct UI. Call once right after the browser's own _OnCreate has run.
    /// </summary>
    internal static SearchBarUI Create(UIBlueprintBrowser browser)
    {
        var go = new GameObject("BlueprintSearchBar", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(browser.rectTrans, false);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -BarMarginTop);
        rt.sizeDelta = new Vector2(-(BarMarginSides * 2f), BarHeight);

        var ui = go.AddComponent<SearchBarUI>();
        ui.browser = browser;
        ui.BuildInputField(rt);
        ui.BuildClearButton(rt);
        ui.ShiftContentTrans();
        ui.RefreshPlaceholder();
        return ui;
    }

    private void BuildInputField(RectTransform parent)
    {
        var inputGo = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(InputField));
        var inputRt = (RectTransform)inputGo.transform;
        inputRt.SetParent(parent, false);
        inputRt.anchorMin = new Vector2(0f, 0f);
        inputRt.anchorMax = new Vector2(1f, 1f);
        inputRt.pivot = new Vector2(0f, 0.5f);
        inputRt.offsetMin = new Vector2(0f, 0f);
        inputRt.offsetMax = new Vector2(-(ClearButtonWidth + 4f), 0f);

        var bg = inputGo.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.35f);

        // Text child
        var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
        var textRt = (RectTransform)textGo.transform;
        textRt.SetParent(inputRt, false);
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(6f, 2f);
        textRt.offsetMax = new Vector2(-6f, -2f);
        var text = textGo.GetComponent<Text>();
        text.supportRichText = false;
        text.color = Color.white;
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = 14;
        text.alignment = TextAnchor.MiddleLeft;

        // Placeholder child
        var phGo = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
        var phRt = (RectTransform)phGo.transform;
        phRt.SetParent(inputRt, false);
        phRt.anchorMin = Vector2.zero;
        phRt.anchorMax = Vector2.one;
        phRt.offsetMin = new Vector2(6f, 2f);
        phRt.offsetMax = new Vector2(-6f, -2f);
        var ph = phGo.GetComponent<Text>();
        ph.color = new Color(1f, 1f, 1f, 0.45f);
        ph.font = text.font;
        ph.fontSize = 14;
        ph.alignment = TextAnchor.MiddleLeft;
        ph.fontStyle = FontStyle.Italic;

        inputField = inputGo.GetComponent<InputField>();
        inputField.textComponent = text;
        inputField.placeholder = ph;
        inputField.lineType = InputField.LineType.SingleLine;
        inputField.onValueChanged.AddListener(OnValueChanged);
    }

    private void BuildClearButton(RectTransform parent)
    {
        var btnGo = new GameObject("Clear", typeof(RectTransform), typeof(Image), typeof(Button));
        var btnRt = (RectTransform)btnGo.transform;
        btnRt.SetParent(parent, false);
        btnRt.anchorMin = new Vector2(1f, 0f);
        btnRt.anchorMax = new Vector2(1f, 1f);
        btnRt.pivot = new Vector2(1f, 0.5f);
        btnRt.anchoredPosition = new Vector2(0f, 0f);
        btnRt.sizeDelta = new Vector2(ClearButtonWidth, 0f);

        btnGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
        var labelRt = (RectTransform)labelGo.transform;
        labelRt.SetParent(btnRt, false);
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;
        var label = labelGo.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        label.fontSize = 16;
        label.color = Color.white;
        label.alignment = TextAnchor.MiddleCenter;
        label.text = "×";

        clearButton = btnGo.GetComponent<Button>();
        clearButton.onClick.AddListener(OnClearClicked);
    }

    private void ShiftContentTrans()
    {
        // Shift the file grid down by ContentShift so it doesn't overlap the search row.
        var ct = browser.contentTrans;
        // contentTrans is already parented and anchored by vanilla. We move its top edge down.
        Vector2 offsetMax = ct.offsetMax;
        offsetMax.y -= ContentShift;
        ct.offsetMax = offsetMax;
    }

    internal void RefreshPlaceholder()
    {
        bool zh = Localization.CurrentLanguage != null && Localization.CurrentLanguage.lcId == Localization.LCID_ZHCN;
        var ph = (Text)inputField.placeholder;
        ph.text = zh ? "搜索蓝图..." : "Search blueprints...";
    }

    private void OnValueChanged(string text)
    {
        SearchState.query = text;
        SearchState.lastChangeTime = Time.unscaledTime;
        SearchState.pendingRefresh = true;
    }

    private void OnClearClicked()
    {
        inputField.SetTextWithoutNotify("");
        SearchState.ClearQuery();
        // Task 6 will trigger the browser refresh here. For now, just reset state.
        if (browser != null && browser.currentDirectoryInfo != null)
            browser.SetCurrentDirectory(browser.currentDirectoryInfo.FullName);
    }

    private void Update()
    {
        // Debounce driver implemented in Task 6. Left empty here so Task 4 builds cleanly.
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build BlueprintSearch/BlueprintSearch.csproj -c Release
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add BlueprintSearch/SearchBarUI.cs
git commit -m "feat(BlueprintSearch): add SearchBarUI with input field and clear button"
```

---

## Task 5: Browser lifecycle patches (_OnCreate, _OnOpen, _OnClose)

**Files:**
- Create: `BlueprintSearch/Patches/UIBlueprintBrowserPatches.cs`
- Modify: `BlueprintSearch/BlueprintSearchPlugin.cs` (apply patches)

After this task the search bar appears whenever the blueprint browser opens and the path cache is built, but search queries still don't filter results (that is Task 6).

- [ ] **Step 1: Create patches file**

File `BlueprintSearch/Patches/UIBlueprintBrowserPatches.cs`:

```csharp
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
```

- [ ] **Step 2: Apply patches from plugin Awake**

Modify `BlueprintSearch/BlueprintSearchPlugin.cs`. Replace its contents with:

```csharp
using BepInEx;
using BlueprintSearch.Patches;
using HarmonyLib;

namespace BlueprintSearch;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class BlueprintSearchPlugin : BaseUnityPlugin
{
    public new static readonly BepInEx.Logging.ManualLogSource Logger =
        BepInEx.Logging.Logger.CreateLogSource(PluginInfo.PLUGIN_NAME);

    private Harmony _harmony;

    private void Awake()
    {
        _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
        _harmony.PatchAll(typeof(UIBlueprintBrowserPatches));
        Logger.LogInfo("BlueprintSearch loaded.");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build BlueprintSearch/BlueprintSearch.csproj -c Release
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add BlueprintSearch/Patches/UIBlueprintBrowserPatches.cs BlueprintSearch/BlueprintSearchPlugin.cs
git commit -m "feat(BlueprintSearch): patch browser lifecycle and instantiate search bar"
```

**In-game check (optional):** install the mod, open the blueprint browser — a search row should appear beneath the toolbar with an empty input and an × button. Typing does nothing yet.

---

## Task 6: Render search results (SetCurrentDirectory postfix + debounce driver)

**Files:**
- Modify: `BlueprintSearch/Patches/UIBlueprintBrowserPatches.cs` (add postfix)
- Modify: `BlueprintSearch/SearchBarUI.cs` (flesh out `Update` debounce)

After this task the end-to-end search works: typing into the bar filters the grid after a 120ms debounce.

- [ ] **Step 1: Add SetCurrentDirectory postfix**

In `BlueprintSearch/Patches/UIBlueprintBrowserPatches.cs`, append this method to the class (still inside the `static class UIBlueprintBrowserPatches`):

```csharp
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIBlueprintBrowser), nameof(UIBlueprintBrowser.SetCurrentDirectory))]
    static void SetCurrentDirectory_Postfix(UIBlueprintBrowser __instance)
    {
        if (!SearchState.Active) return;
        RepopulateWithResults(__instance);
    }

    private const int MaxResults = 256;

    private static void RepopulateWithResults(UIBlueprintBrowser browser)
    {
        // Clear the file items that vanilla just populated for the current folder.
        foreach (var fi in browser.fileItems)
        {
            if (fi.inited) fi._Free();
        }

        int matches = 0;
        int y = 0;
        var entries = SearchState.cachedEntries;
        for (int i = 0; i < entries.Count && matches < MaxResults; i++)
        {
            if (!SearchFilter.Matches(entries[i].relLower, SearchState.tokens)) continue;

            string relOrig = entries[i].relOriginal;
            string fullPath = browser.rootPath + relOrig;
            string shortName = ComposeLabel(relOrig);

            var item = GetOrCreateFileItemViaReflection(browser);
            item._Init(browser.data);
            y = item.SetItemLayout(matches, false, fullPath, shortName);
            item._Open();
            matches++;
        }

        browser.emptyTipText.gameObject.SetActive(matches == 0);
        browser.contentTrans.sizeDelta = new UnityEngine.Vector2(
            browser.contentTrans.sizeDelta.x, (float)y);
    }

    // GetOrCreateFileItem is private in UIBlueprintBrowser. Use AccessTools to invoke it.
    private static readonly System.Reflection.MethodInfo GetOrCreateFileItemMI =
        AccessTools.Method(typeof(UIBlueprintBrowser), "GetOrCreateFileItem");

    private static UIBlueprintFileItem GetOrCreateFileItemViaReflection(UIBlueprintBrowser browser)
    {
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
        if (lastSlash < 0) return withoutExt;
        string fileName = withoutExt.Substring(lastSlash + 1);
        int prevSlash = withoutExt.LastIndexOf('/', lastSlash - 1);
        string parent = prevSlash < 0
            ? withoutExt.Substring(0, lastSlash)
            : withoutExt.Substring(prevSlash + 1, lastSlash - prevSlash - 1);
        return parent + " / " + fileName;
    }
```

- [ ] **Step 2: Implement debounce driver in SearchBarUI**

In `BlueprintSearch/SearchBarUI.cs`, replace the empty `Update` method with:

```csharp
    private const float DebounceSeconds = 0.120f;

    private void Update()
    {
        if (!SearchState.pendingRefresh) return;
        if (Time.unscaledTime - SearchState.lastChangeTime < DebounceSeconds) return;
        SearchState.pendingRefresh = false;
        SearchState.tokens = SearchFilter.Tokenize(SearchState.query);
        if (browser != null && browser.currentDirectoryInfo != null)
            browser.SetCurrentDirectory(browser.currentDirectoryInfo.FullName);
    }
```

- [ ] **Step 3: Build**

```bash
dotnet build BlueprintSearch/BlueprintSearch.csproj -c Release
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add BlueprintSearch/Patches/UIBlueprintBrowserPatches.cs BlueprintSearch/SearchBarUI.cs
git commit -m "feat(BlueprintSearch): render filtered results with debounced refresh"
```

**In-game check:** install the build; open the browser; type a folder name prefix and verify matching blueprints appear across the whole library after a brief pause. Clear the input — the normal folder view returns. Try AND tokens: `初期 电力`.

---

## Task 7: Right-click on a result jumps to its containing folder

**Files:**
- Create: `BlueprintSearch/Patches/UIBlueprintFileItemPatches.cs`
- Modify: `BlueprintSearch/BlueprintSearchPlugin.cs` (register second patch type)

- [ ] **Step 1: Create the file-item right-click patch**

File `BlueprintSearch/Patches/UIBlueprintFileItemPatches.cs`:

```csharp
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
```

- [ ] **Step 2: Register the patch type**

Modify `BlueprintSearch/BlueprintSearchPlugin.cs` — in `Awake`, after the existing `PatchAll(typeof(UIBlueprintBrowserPatches))`, add:

```csharp
        _harmony.PatchAll(typeof(UIBlueprintFileItemPatches));
```

So the `Awake` body looks like:

```csharp
    private void Awake()
    {
        _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
        _harmony.PatchAll(typeof(UIBlueprintBrowserPatches));
        _harmony.PatchAll(typeof(UIBlueprintFileItemPatches));
        Logger.LogInfo("BlueprintSearch loaded.");
    }
```

- [ ] **Step 3: Build**

```bash
dotnet build BlueprintSearch/BlueprintSearch.csproj -c Release
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add BlueprintSearch/Patches/UIBlueprintFileItemPatches.cs BlueprintSearch/BlueprintSearchPlugin.cs
git commit -m "feat(BlueprintSearch): right-click result jumps to containing folder"
```

**In-game check:** perform a search, right-click a result — browser should navigate to the file's folder with the search query cleared. Left-click should still open the inspector normally.

---

## Task 8: Toolbar button gating + cache invalidation

**Files:**
- Modify: `BlueprintSearch/Patches/UIBlueprintBrowserPatches.cs`

- [ ] **Step 1: Gate toolbar buttons while searching**

In `BlueprintSearch/Patches/UIBlueprintBrowserPatches.cs`, add a helper and call it from both lifecycle and `SetCurrentDirectory` postfixes.

Add this method to the class:

```csharp
    private static void UpdateToolbarInteractable(UIBlueprintBrowser browser)
    {
        bool interactable = !SearchState.Active;
        browser.cutButton.interactable = interactable;
        browser.newFileButton.interactable = interactable;
        browser.newFolderButton.interactable = interactable;
        browser.upLevelButton.interactable = interactable;
    }
```

Call it at the end of `OnOpen_Postfix`, at the end of `OnClose_Postfix` (force all interactable = true when closing), and at the end of both the no-op and populated paths of `SetCurrentDirectory_Postfix`.

Replace the existing methods with these fully-updated versions:

```csharp
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIBlueprintBrowser), "_OnOpen")]
    static void OnOpen_Postfix(UIBlueprintBrowser __instance)
    {
        if (searchBarUI != null && searchBarUI.inputField != null)
            searchBarUI.inputField.SetTextWithoutNotify("");
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
```

- [ ] **Step 2: Add cache invalidation hooks**

Append these patches to `UIBlueprintBrowserPatches`:

```csharp
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
```

- [ ] **Step 3: Build**

```bash
dotnet build BlueprintSearch/BlueprintSearch.csproj -c Release
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add BlueprintSearch/Patches/UIBlueprintBrowserPatches.cs
git commit -m "feat(BlueprintSearch): gate toolbar buttons and invalidate cache on file ops"
```

**In-game check:** with an active search, cut / new-file / new-folder / up-level buttons should appear greyed-out. Clear the search — they become active again. Create a blueprint, open search: the new blueprint appears in matches.

---

## Task 9: Config toggle, live enable/disable, finalize

**Files:**
- Modify: `BlueprintSearch/BlueprintSearchPlugin.cs` (bind config, handle changes)

- [ ] **Step 1: Bind config and wire runtime toggling**

Replace the entire contents of `BlueprintSearch/BlueprintSearchPlugin.cs` with:

```csharp
using System;
using BepInEx;
using BepInEx.Configuration;
using BlueprintSearch.Patches;
using HarmonyLib;

namespace BlueprintSearch;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class BlueprintSearchPlugin : BaseUnityPlugin
{
    public new static readonly BepInEx.Logging.ManualLogSource Logger =
        BepInEx.Logging.Logger.CreateLogSource(PluginInfo.PLUGIN_NAME);

    internal static ConfigEntry<bool> ModEnabled;

    private Harmony _harmony;

    private void Awake()
    {
        ModEnabled = Config.Bind("General", "Enabled", true,
            "Enable search bar in blueprint browser / 在蓝图库窗口启用搜索栏");
        ModEnabled.SettingChanged += OnEnabledChanged;

        _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
        ApplyPatches();

        Logger.LogInfo("BlueprintSearch loaded.");
    }

    private void OnDestroy()
    {
        if (ModEnabled != null) ModEnabled.SettingChanged -= OnEnabledChanged;
        _harmony?.UnpatchSelf();
    }

    private void ApplyPatches()
    {
        if (!ModEnabled.Value) return;
        _harmony.PatchAll(typeof(UIBlueprintBrowserPatches));
        _harmony.PatchAll(typeof(UIBlueprintFileItemPatches));
    }

    private void OnEnabledChanged(object sender, EventArgs e)
    {
        _harmony.UnpatchSelf();
        ApplyPatches();

        // If the browser is currently open, reset UI state and force a redraw.
        var ui = UIBlueprintBrowserPatches.searchBarUI;
        if (ui != null)
        {
            ui.gameObject.SetActive(ModEnabled.Value);
            if (!ModEnabled.Value)
            {
                SearchState.ClearQuery();
                if (ui.browser != null && ui.browser.currentDirectoryInfo != null)
                    ui.browser.SetCurrentDirectory(ui.browser.currentDirectoryInfo.FullName);
            }
        }

        Logger.LogInfo($"BlueprintSearch {(ModEnabled.Value ? "enabled" : "disabled")}.");
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build BlueprintSearch/BlueprintSearch.csproj -c Release
```

Expected: build succeeds, and `BlueprintSearch/package/BlueprintSearch-1.0.0.zip` is produced by the post-build target.

- [ ] **Step 3: Commit**

```bash
git add BlueprintSearch/BlueprintSearchPlugin.cs
git commit -m "feat(BlueprintSearch): add runtime enable toggle"
```

**In-game check (final acceptance):**
1. With `Enabled=true`, open browser, search works end-to-end (type → filter → clear → back to folder).
2. Toggle `Enabled=false` via BepInEx config manager while browser is open — the search bar hides and the folder view reappears unchanged.
3. Toggle back to true — the bar returns, typing works.
4. Close and reopen the browser several times — no leaked GameObjects (check scene hierarchy if you have a debugger; otherwise visually confirm no duplicate bars stack up).

---

## Spec Coverage Check

- [x] Recursive search from root — SearchState.RebuildCache (Task 3)
- [x] Match against relative path (file + folder names) — PathEntry.relLower (Task 3)
- [x] Case-insensitive AND tokens — SearchFilter.Tokenize + Matches (Task 2)
- [x] Token separators include `/` and `\` — SearchFilter.Separators (Task 2)
- [x] Search bar UI row below toolbar — SearchBarUI (Task 4)
- [x] File grid shift — ShiftContentTrans (Task 4)
- [x] 120ms debounce — SearchBarUI.Update (Task 6)
- [x] Result tile labels with parent folder + file — ComposeLabel (Task 6)
- [x] MaxResults cap — RepopulateWithResults (Task 6)
- [x] Empty-query restores vanilla view — Active guard in postfix (Task 6)
- [x] Right-click jump to containing folder — UIBlueprintFileItemPatches (Task 7)
- [x] Toolbar buttons disabled while active — UpdateToolbarInteractable (Task 8)
- [x] Cache invalidation on new/delete/rename — Task 8 postfixes
- [x] Runtime enable toggle — OnEnabledChanged (Task 9)
- [x] External file change not detected — consistent with vanilla (Non-Goal)
- [x] Placeholder text localized (zh/en) — RefreshPlaceholder (Task 4)
- [x] Empty-results text — reuses browser.emptyTipText (Task 6)
