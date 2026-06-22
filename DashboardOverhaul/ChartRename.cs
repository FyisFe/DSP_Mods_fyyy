using UnityEngine;
using UnityEngine.UI;

namespace DashboardOverhaul;

/// <summary>
/// Floating single-line input for renaming the StatPlan bound to a chart, opened from the chart
/// popup's "Rename" button or by double-clicking the chart title. Mirrors PageTabBar's inline
/// rename. One shared input per dashboard, cleared on teardown. Renaming edits StatPlan.name, so
/// every chart of that statistic shows the new name (matches the sidebar rename).
/// </summary>
public static class ChartRename
{
    private static InputField _input;
    private static UIChart _target;

    /// <summary>Open the rename input over <paramref name="chart"/>'s title, pre-filled with the
    /// current StatPlan name.</summary>
    public static void Begin(UIChart chart)
    {
        if (chart == null || chart.chartData == null || chart.charts == null) return;
        var dash = chart.uiDashboard;
        if (dash == null || dash.chartContentRt == null) return;
        var statPlan = chart.charts.statPlans[chart.chartData.statPlanId];
        if (statPlan == null) return;

        var input = EnsureInput(dash);
        _target = chart;

        var inputRt = (RectTransform)input.transform;
        var anchorRt = chart.titleText != null ? (RectTransform)chart.titleText.transform : chart.rectTrans;
        inputRt.position = anchorRt.position;
        inputRt.sizeDelta = new Vector2(Mathf.Max(140f, anchorRt.rect.width), 22f);

        input.gameObject.SetActive(true);
        input.text = statPlan.name ?? string.Empty;
        input.Select();
        input.ActivateInputField();
    }

    /// <summary>Cancel an in-progress rename if it targets <paramref name="chart"/> (e.g. the chart
    /// is about to be deleted).</summary>
    public static void CancelIfTargeting(UIChart chart)
    {
        if (_target == chart) Hide();
    }

    /// <summary>Destroy the shared input and drop references; call on dashboard teardown.</summary>
    public static void Free()
    {
        if (_input != null) Object.Destroy(_input.gameObject);
        _input = null;
        _target = null;
    }

    private static InputField EnsureInput(UIDashboard dash)
    {
        if (_input != null) return _input;

        var font = dash.emptyTip != null ? dash.emptyTip.font : null;

        var go = new GameObject("DO_ChartRenameInput", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(dash.chartContentRt, false);
        rt.sizeDelta = new Vector2(140f, 22f);

        var bg = go.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.85f);

        var textGo = new GameObject("Text", typeof(RectTransform));
        var trt = (RectTransform)textGo.transform;
        trt.SetParent(rt, false);
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(6f, 0f); trt.offsetMax = new Vector2(-6f, 0f);
        var text = textGo.AddComponent<Text>();
        text.font = font; text.fontSize = 14; text.alignment = TextAnchor.MiddleLeft;
        text.color = Color.white; text.supportRichText = false;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        var input = go.AddComponent<InputField>();
        input.textComponent = text;
        input.lineType = InputField.LineType.SingleLine;
        input.characterLimit = 64;
        input.onEndEdit.AddListener(Commit);
        go.SetActive(false);
        _input = input;
        return input;
    }

    private static void Commit(string value)
    {
        var chart = _target;
        Hide();
        if (chart == null || chart.chartData == null || chart.charts == null) return;
        var statPlan = chart.charts.statPlans[chart.chartData.statPlanId];
        if (statPlan == null) return;
        string newName = (value ?? string.Empty).Trim();
        statPlan.Rename(ref newName);                 // fires onNameChanged -> title repaints
        chart.TruncateStatPlanNameText();             // defensive title refresh
        var dash = chart.uiDashboard;
        if (dash != null && dash.statboard != null)
            dash.statboard.DetermineEntryVisible();    // refresh the sidebar list if present
    }

    private static void Hide()
    {
        if (_input != null) _input.gameObject.SetActive(false);
        _target = null;
    }
}
