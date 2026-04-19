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
    internal static ConfigEntry<int> MaxResults;
    internal static ConfigEntry<int> DebounceMs;

    private Harmony _harmony;

    private void Awake()
    {
        ModEnabled = Config.Bind("General", "Enabled", true,
            "Enable search bar in blueprint browser / 在蓝图库窗口启用搜索栏");
        ModEnabled.SettingChanged += OnEnabledChanged;

        MaxResults = Config.Bind("General", "MaxResults", 256,
            "Maximum number of search results shown (UI responsiveness guard)");

        DebounceMs = Config.Bind("General", "DebounceMs", 120,
            "Milliseconds to wait after the last keystroke before recomputing results");

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
        SearchState.cacheDirty = true;
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
