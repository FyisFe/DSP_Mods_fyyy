using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UXAssist.Common;
using UXAssist.UI;

namespace InterstellarLogisticsOpt;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
[BepInDependency(UXAssist.PluginInfo.PLUGIN_GUID)]
public class InterstellarLogisticsOptPlugin : BaseUnityPlugin
{
    public static ConfigEntry<bool> ModEnabled;
    public static ConfigEntry<int> AmortizeFactor;
    private Harmony _harmony;

    private void Awake()
    {
        ModEnabled = Config.Bind("General", "Enabled", true,
            "Optimize interstellar dispatch / 优化星际物流派船");
        AmortizeFactor = Config.Bind("General", "AmortizeFactor", 1,
            new ConfigDescription(
                "All-route scheduling multiplier. 1 = vanilla cadence. At N > 1, stagger stations across ticks and run dispatch checks at 1/N frequency. Trades priority fidelity and responsiveness for lower CPU cost; may limit throughput. / 全航线调度间隔系数。1 为原版节奏；N > 1 时按塔错开调度，检查频率降为 1/N。以部分优先级准确性和响应速度换取更低 CPU 开销，可能限制吞吐。",
                new AcceptableValueRange<int>(1, 30)));
        ModEnabled.SettingChanged += UpdateSettings;
        AmortizeFactor.SettingChanged += UpdateSettings;
        UpdateSettings(null, EventArgs.Empty);
        _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
        _harmony.PatchAll(typeof(DispatchOptimization));
        _harmony.PatchAll(typeof(DispatchScheduler));
        if (DispatchOptimization.Failure != null) Logger.LogWarning(DispatchOptimization.Failure);

        I18N.Add("InterstellarLogisticsOpt", "InterstellarLogisticsOpt", "星际物流优化");
        I18N.Add("Optimize interstellar dispatch", "Optimize interstellar dispatch", "优化星际物流派船计算");
        I18N.Add("Dispatch interval multiplier", "Dispatch interval multiplier", "全航线调度间隔系数");
        I18N.Apply();
        MyConfigWindow.OnUICreated += CreateUI;
    }

    private static void UpdateSettings(object sender, EventArgs e)
    {
        DispatchOptimization.Enabled = ModEnabled.Value;
        DispatchScheduler.Factor = AmortizeFactor.Value;
    }

    private void OnDestroy()
    {
        MyConfigWindow.OnUICreated -= CreateUI;
        if (ModEnabled != null) ModEnabled.SettingChanged -= UpdateSettings;
        if (AmortizeFactor != null) AmortizeFactor.SettingChanged -= UpdateSettings;
        DispatchOptimization.Enabled = false;
        _harmony?.UnpatchSelf();
    }

    private static void CreateUI(MyConfigWindow wnd, RectTransform trans)
    {
        wnd.AddSplitter(trans, 10f);
        wnd.AddTabGroup(trans, "InterstellarLogisticsOpt", "tab-group-interstellarlogisticsopt");
        var tab = wnd.AddTab(trans, "InterstellarLogisticsOpt");
        wnd.AddCheckBox(0f, 10f, tab, ModEnabled, "Optimize interstellar dispatch");
        var text = wnd.AddText2(0f, 51f, tab, "Dispatch interval multiplier", 14);
        wnd.AddSlider(text.preferredWidth + 10f, 46f, tab, AmortizeFactor,
            new MyWindow.RangeValueMapper<int>(1, 30), "0", 200f).WithSmallerHandle();
    }
}
