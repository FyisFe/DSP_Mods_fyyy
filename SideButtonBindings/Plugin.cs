using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace SideButtonBindings;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public sealed class Plugin : BaseUnityPlugin
{
    private Harmony _harmony;

    private void Awake()
    {
        _harmony = Harmony.CreateAndPatchAll(typeof(SideButtonPatch), PluginInfo.PLUGIN_GUID);
        Logger.LogInfo("Mouse side button bindings enabled.");
    }

    private void OnDestroy() => _harmony?.UnpatchSelf();
}

[HarmonyPatch(typeof(UIKeyEntry), "OverrideKey")]
internal static class SideButtonPatch
{
    internal static void Prefix(ref BuiltinKey ___builtinKey, int keyCode, out int __state)
    {
        __state = ___builtinKey.conflictGroup;
        if (keyCode >= (int)KeyCode.Mouse3 && keyCode <= (int)KeyCode.Mouse6)
        {
            // Device bits are excluded from conflictKeyGroup; actual key conflicts still apply.
            // Modified mouse buttons use the game's keyboard category.
            ___builtinKey.conflictGroup |= BuiltinKey.USE_MOUSE | BuiltinKey.USE_KEYBOARD;
        }
    }

    internal static void Finalizer(ref BuiltinKey ___builtinKey, int __state)
    {
        ___builtinKey.conflictGroup = __state;
    }
}
