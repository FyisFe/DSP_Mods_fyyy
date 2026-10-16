using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DashboardOverhaul;

/// <summary>
/// Dispatches chart and page hotkeys after the dashboard has updated its charts.
/// The topmost UI hit owns the action; text editing, modal dialogs, menus and dragging take precedence.
/// </summary>
public static class ChartHotkeys
{
    private static readonly List<RaycastResult> s_hits = new();

    private enum Command { None, Rename, Size, Background, Border, ResetBorder }

    public static void Update(DashboardView view)
    {
        var dashboard = view.Dashboard;
        var command = ReadCommand();
        if (command == Command.None || IsTextEditing() || UIMessageBox.activeCount > 0) return;
        if (dashboard.rootPopupMenu != null) return;
        if ((dashboard.anyChartDragging && Input.GetMouseButton(0)) || view.Tabs.IsDragging) return;

        var target = PointerTarget();
        if (target == null) return;
        if (view.IsGearTarget(target.transform))
        {
            command = ReadCommand(page: true);
            switch (command)
            {
                case Command.Rename: ProductionNames.CyclePage(dashboard); break;
                case Command.Background: PageGearMenu.CyclePageStyle(dashboard, EChartStyleType.Background); break;
                case Command.Border: PageGearMenu.CyclePageStyle(dashboard, EChartStyleType.Border); break;
                case Command.ResetBorder: PageGearMenu.ResetPageBorder(dashboard); break;
            }
            return;
        }

        var chart = target.GetComponentInParent<UIChart>();
        if (chart == null || !chart.active || chart.uiDashboard != dashboard || chart.chartData == null) return;
        switch (command)
        {
            case Command.Rename: ProductionNames.Cycle(chart); break;
            case Command.Size: SwitchSize(chart); break;
            case Command.Background: CycleStyle(chart, EChartStyleType.Background); break;
            case Command.Border: CycleStyle(chart, EChartStyleType.Border); break;
            case Command.ResetBorder: ResetBorder(chart); break;
        }
    }

    private static Command ReadCommand(bool page = false)
    {
        if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) return Command.None;
        if (Input.GetKey(KeyCode.LeftControl))
            return IsDown(ModConfig.QuickRenameKey.Value) ? Command.Rename : Command.None;
        if (Input.GetKey(KeyCode.RightControl)) return Command.None;
        if (!page && IsDown(ModConfig.SwitchSizeKey.Value)) return Command.Size;
        if (IsDown(ModConfig.CycleBackgroundKey.Value)) return Command.Background;
        if (IsDown(ModConfig.CycleBorderKey.Value)) return Command.Border;
        return IsDown(ModConfig.ResetBorderKey.Value) ? Command.ResetBorder : Command.None;
    }

    private static bool IsDown(KeyCode key) => key != KeyCode.None && Input.GetKeyDown(key);

    private static GameObject PointerTarget()
    {
        var events = EventSystem.current;
        if (events == null) return null;
        events.RaycastAll(new PointerEventData(events) { position = Input.mousePosition }, s_hits);
        var target = s_hits.Count > 0 ? s_hits[0].gameObject : null;
        s_hits.Clear();
        return target;
    }

    private static void CycleStyle(UIChart chart, EChartStyleType type)
    {
        int next = ChartStyles.NextIndex(ChartStyles.GetIndex(chart.chartData, type), type);
        if (ChartStyles.Apply(chart.chartData, type, next)) ChartStyles.Repaint(chart);
    }

    private static void ResetBorder(UIChart chart)
    {
        if (ChartStyles.Apply(chart.chartData, EChartStyleType.Border, 0)) ChartStyles.Repaint(chart);
    }

    private static bool IsTextEditing()
    {
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        return selected != null && selected.GetComponent<InputField>() != null;
    }

    /// <summary>Advance the chart to the next dashboard size, wrapping around. The presets array is
    /// shared with the watch view, so only presets the dashboard size menu offers are used.</summary>
    private static void SwitchSize(UIChart chart)
    {
        var presets = ChartPresetsDB.GetPresetsArray(chart.statPlanType);
        for (int step = 1; step < presets.Length; step++)
        {
            int next = (chart.chartData.presetIndex + step) % presets.Length;
            if (presets[next].usage != ChartPresets.EUsage.Dashboard) continue;
            chart.OnSizeMenuButtonClick(next);
            return;
        }
    }
}
