using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UXAssist.Common;

namespace DashboardOverhaul;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
[BepInDependency(UXAssist.PluginInfo.PLUGIN_GUID)]
public class DashboardOverhaulPlugin : BaseUnityPlugin
{
    public new static readonly BepInEx.Logging.ManualLogSource Logger =
        BepInEx.Logging.Logger.CreateLogSource(PluginInfo.PLUGIN_NAME);

    public static ConfigEntry<bool> ModEnabled;

    private Harmony _harmony;

    private void Awake()
    {
        ModEnabled = Config.Bind("General", "Enabled", true,
            "Enable the Dashboard paging UI / 启用仪表盘分页界面");

        I18N.Add("已达页面上限", "Page limit reached", "已达页面上限");
        I18N.Add("至少保留一页", "Keep at least one page", "至少保留一页");
        I18N.Add("重命名", "Rename", "重命名");
        I18N.Add("删除", "Delete", "删除");
        I18N.Add("删除页面标题", "Delete page", "删除页面");
        I18N.Add("删除页面提示", "Delete this page and its charts?", "确认删除该页及其图表？");
        I18N.Add("取消", "Cancel", "取消");
        I18N.Add("确定", "Confirm", "确定");
        I18N.Apply();

        _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
        _harmony.PatchAll(typeof(UIDashboardPatch));

        Logger.LogInfo("DashboardOverhaul loaded.");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
