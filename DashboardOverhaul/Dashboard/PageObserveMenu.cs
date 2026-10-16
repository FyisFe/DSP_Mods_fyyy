using System;
using System.Collections.Generic;

namespace DashboardOverhaul;

/// <summary>Native display menus applied to green charts with the same option meanings and preset.</summary>
public static class PageObserveMenu
{
    public static void AddTo(UIDashboard dashboard, UIPopupMenu menu, DashboardPage page)
    {
        foreach (var group in Groups(dashboard, page))
        {
            var source = group.Charts[0];
            int before = menu.childButtons.Count;
            source.SetObserveGroupPopupMenuButtons(menu);
            for (int i = before; i < menu.childButtons.Count; i++)
            {
                var button = menu.childButtons[i];
                button.ButtonText += $" ({group.Label})";
                button.SetState(true);
                DashboardUi.Tip(button.gameObject, button.ButtonText,
                    string.Format(Loc.L("修改当前页中背景为绿色、属于此组的 {0} 个图表。",
                        "Changes the {0} green charts in this group on the current page."), group.Charts.Count));
                Bind(button, source, group.Charts, dashboard);
            }
        }
    }

    private static void Bind(UIPopupMenuButton button, UIChart source, List<UIChart> targets,
        UIDashboard dashboard)
    {
        var handler = button.onMenuButtonClick;
        if (handler == null)
        {
            if (button.m_ChildMenu != null)
                foreach (var child in button.m_ChildMenu.childButtons)
                    Bind(child, source, targets, dashboard);
            return;
        }

        var handlers = BindTargets(handler, source, targets);
        if (handlers == null)
        {
            // Other mods can add closures or multicast actions with a different target or scope.
            button.onMenuButtonClick = null;
            button.m_Button.interactable = false;
            DashboardUi.Tip(button.gameObject, button.ButtonText,
                Loc.L("此扩展选项不支持批量修改，请在单个图表中设置。",
                    "Set this extension option on an individual chart; batch editing is unavailable."));
            return;
        }

        // A representative's check mark does not describe a group with mixed current settings.
        if (targets.Count > 1)
        {
            button.ButtonIcon = null;
            button.SetState(true);
        }
        button.onMenuButtonClick = data =>
        {
            dashboard.CloseChartPopupMenu();
            foreach (var apply in handlers) apply(data);
        };
    }

    private static Action<int>[] BindTargets(Action<int> handler, UIChart source, List<UIChart> targets)
    {
        if (handler.GetInvocationList().Length != 1 || !ReferenceEquals(handler.Target, source) ||
            handler.Method.DeclaringType?.Assembly != typeof(UIChart).Assembly) return null;

        var result = new Action<int>[targets.Count];
        for (int i = 0; i < targets.Count; i++)
        {
            if (!handler.Method.DeclaringType.IsInstanceOfType(targets[i])) return null;
            result[i] = (Action<int>)Delegate.CreateDelegate(typeof(Action<int>), targets[i], handler.Method);
        }
        return result;
    }

    private static List<ObserveGroup> Groups(UIDashboard dashboard, DashboardPage page)
    {
        var result = new List<ObserveGroup>();
        int green = ChartStyles.GreenBackgroundIndex;
        if (green < 0) return result;

        foreach (var data in page.chartDatas)
        {
            if (data.backgroundStyleIndex != green) continue;
            var chart = ChartStyles.FindChart(dashboard, data);
            if (chart == null) continue;
            var plan = chart.charts.statPlans[data.statPlanId];
            var key = GroupKey(plan, data);
            ObserveGroup group = result.Find(candidate => candidate.Key == key);
            if (group == null)
            {
                group = new ObserveGroup { Key = key, Label = GroupLabel(plan, key) };
                result.Add(group);
            }
            group.Charts.Add(chart);
        }
        return result;
    }

    /// <summary>Only fields that change a native menu's options or their integer meanings belong here.</summary>
    internal static (EStatPlanType Type, int Preset, int Subject, bool Totals) GroupKey(StatPlan plan, ChartData data)
    {
        int subject = 0;
        bool totals = false;
        switch (plan)
        {
            case AstroItemProductionStatPlan production:
                totals = production.timeLevel == 5;
                break;
            case DysonSphereStatPlan dyson:
                // Sails use layouts 0..3; structure and cell points share layouts 0..1.
                subject = data.displayTypeParams[0] == 0 ? 0 : 1;
                totals = dyson.timeLevel == 5;
                break;
            case AstroTransportStatPlan transport:
                subject = transport.isInterstellar ? 1 : 0;
                break;
            case SingleStorageStatPlan storage:
                // Only grids and tanks expose the "current / current and above" options.
                subject = (int)storage.storageType;
                break;
        }
        return (plan.type, data.presetIndex, subject, totals);
    }

    private static string GroupLabel(StatPlan plan,
        (EStatPlanType Type, int Preset, int Subject, bool Totals) key)
    {
        string label = StatPlan.GetDefaultName(key.Type, key.Subject) + " " +
            ChartPresetsDB.GetPresetsArray(key.Type)[key.Preset].name.Translate();
        switch (plan)
        {
            case AstroItemProductionStatPlan _:
                return label + " · " + (key.Totals ? Loc.L("累计", "Total") : Loc.L("速率", "Rate"));
            case DysonSphereStatPlan _:
                return label + " · " + (key.Subject == 0 ? Loc.L("太阳帆", "Sails") : Loc.L("结构点与细胞", "Structure / cells")) +
                    " · " + (key.Totals ? Loc.L("累计", "Total") : Loc.L("速率", "Rate"));
            case SingleStorageStatPlan storage:
                if (storage.storageType == SingleStorageStatPlan.EStatPlanStorageType.Grid)
                    return label + " · " + Loc.L("仓库", "Storage");
                if (storage.storageType == SingleStorageStatPlan.EStatPlanStorageType.Tank)
                    return label + " · " + Loc.L("储液罐", "Tank");
                break;
        }
        return label;
    }

    private sealed class ObserveGroup
    {
        public readonly List<UIChart> Charts = new List<UIChart>();
        public (EStatPlanType Type, int Preset, int Subject, bool Totals) Key;
        public string Label;
    }
}
