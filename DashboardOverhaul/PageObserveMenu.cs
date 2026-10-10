using System.Collections.Generic;
using UnityEngine;

namespace DashboardOverhaul;

/// <summary>
/// The page-level "监控设置" (display options) entry of the gear menu.
///
/// It reuses the game's own menu builder (<see cref="UIChart.SetObserveGroupPopupMenuButtons"/>) for a
/// chart of the target kind, so every chart type offers exactly the options vanilla offers it - no option
/// list has to be duplicated here. The option handlers are then re-bound: instead of writing the choice
/// into the one chart the menu was built from, the game's handler is replayed once per chart of the group.
///
/// A group is the set of statistics on the current page that use the added green background and share the
/// same kind and size (the size matters because the options depend on it), which is how the green
/// background doubles as the selection marker for batch edits.
/// </summary>
public static class PageObserveMenu
{
    public static void AddTo(UIDashboard dashboard, UIPopupMenu menu, DashboardPage page)
    {
        var groups = Groups(dashboard, page);
        if (groups.Count == 0 || menu == null) return;

        foreach (var group in groups)
        {
            var chart = group.Representative;
            if (chart == null) continue;

            int before = menu.childButtons.Count;
            chart.SetObserveGroupPopupMenuButtons(menu);
            if (menu.childButtons.Count <= before) continue; // this kind/size has no observe settings

            for (int i = before; i < menu.childButtons.Count; i++)
            {
                var button = menu.childButtons[i];
                if (button == null) continue;
                if (groups.Count > 1)
                {
                    button.ButtonText = $"{button.ButtonText} ({group.SizeLabel})";
                    button.SetState(true);
                }
                ExplainPanelSwitch(button);
                Rebind(button, chart, group.Datas, dashboard);
            }
        }
    }

    /// <summary>Attach the scope hint to the "switch panel" entry of a group, so it is clear that the
    /// choice only reaches the statistics that share this group's background, kind and size.</summary>
    private static void ExplainPanelSwitch(UIPopupMenuButton groupButton)
    {
        var child = groupButton != null ? groupButton.m_ChildMenu : null;
        if (child == null || child.childButtons == null || child.childButtons.Count == 0) return;
        var panelButton = child.childButtons[0];
        if (panelButton == null) return;
        DashboardUi.Tip(panelButton.gameObject, panelButton.ButtonText,
            Loc.L("仅对当前页中背景为绿色、且类型与尺寸相同的统计项生效",
                "Applies only to statistics on this page that use the green background and share the same kind and size"));
    }

    /// <summary>Replay the game's own option handler for every chart of the group.</summary>
    private static void Rebind(UIPopupMenuButton button, UIChart source, List<ChartData> targets, UIDashboard dashboard)
    {
        var handler = button.onMenuButtonClick;
        if (handler == null)
        {
            if (button.m_ChildMenu == null) return;
            foreach (var child in button.m_ChildMenu.childButtons)
                Rebind(child, source, targets, dashboard);
            return;
        }

        button.onMenuButtonClick = data =>
        {
            dashboard.CloseChartPopupMenu();
            var keep = source.chartData; // the game's handler writes into the chart's own data
            try
            {
                foreach (var target in targets)
                {
                    source.chartData = target;
                    handler(data);
                }
            }
            finally
            {
                source.chartData = keep;
            }
            ChartStyleExtras.RebuildCharts(source);
        };
    }

    private static List<ObserveGroup> Groups(UIDashboard dashboard, DashboardPage page)
    {
        var result = new List<ObserveGroup>();
        int green = ChartStyleExtras.GreenBackgroundIndex;
        if (green < 0 || page.chartDatas == null) return result;

        foreach (var data in page.chartDatas)
        {
            if (data == null || data.backgroundStyleIndex != green) continue;
            var chart = ChartStyleExtras.FindChart(dashboard, data);
            if (chart == null) continue; // not on screen, so the game cannot build its menu

            ObserveGroup group = null;
            foreach (var candidate in result)
                if (candidate.Type == (int)chart.statPlanType && candidate.Preset == chart.presetIndex)
                {
                    group = candidate;
                    break;
                }
            if (group == null)
            {
                group = new ObserveGroup
                {
                    Type = (int)chart.statPlanType,
                    Preset = chart.presetIndex,
                    SizeLabel = $"{data.size.x}x{data.size.y}",
                    Representative = chart,
                };
                result.Add(group);
            }
            group.Datas.Add(data);
        }
        return result;
    }

    private sealed class ObserveGroup
    {
        public readonly List<ChartData> Datas = new List<ChartData>();
        public UIChart Representative;
        public int Type;
        public int Preset;
        public string SizeLabel;
    }
}
