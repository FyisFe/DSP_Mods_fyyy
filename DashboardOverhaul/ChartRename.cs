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
        if (chart == null) return; // chartData/charts null are covered by the ResolveStatPlan null-return below
        var dash = chart.uiDashboard;
        if (dash == null || dash.chartContentRt == null) return;
        var statPlan = ResolveStatPlan(chart);
        if (statPlan == null) return;

        var input = EnsureInput(dash);
        _target = chart;

        // Overlay the chart title's actual on-screen rectangle: pivot top-left, placed at the
        // title's top-left world corner, sized to the title's width. (Setting position to the
        // title's pivot — its centre — with a centre-pivot, fixed-width box misplaced it, since
        // the title is a wide, centre-pivoted element.)
        var inputRt = (RectTransform)input.transform;
        var titleRt = chart.titleText != null ? chart.titleText.rectTransform : chart.rectTrans;
        var parent = inputRt.parent as RectTransform;
        var corners = new Vector3[4];
        titleRt.GetWorldCorners(corners); // 0=BL, 1=TL, 2=TR, 3=BR
        float width = 140f, height = 22f;
        if (parent != null)
        {
            Vector3 tl = parent.InverseTransformPoint(corners[1]);
            Vector3 tr = parent.InverseTransformPoint(corners[2]);
            Vector3 bl = parent.InverseTransformPoint(corners[0]);
            width = Mathf.Max(140f, Mathf.Abs(tr.x - tl.x));
            height = Mathf.Max(20f, Mathf.Abs(tl.y - bl.y));
        }
        inputRt.anchorMin = inputRt.anchorMax = new Vector2(0f, 1f);
        inputRt.pivot = new Vector2(0f, 1f);
        inputRt.sizeDelta = new Vector2(width, height);
        inputRt.position = corners[1];

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

    /// <summary>Safely resolve the StatPlan bound to a chart, guarding a null pool/buffer and an
    /// out-of-range statPlanId (e.g. a stale id during teardown). Returns null if unavailable.</summary>
    private static StatPlan ResolveStatPlan(UIChart chart)
    {
        var pool = chart != null && chart.charts != null ? chart.charts.statPlans : null;
        if (pool == null || pool.buffer == null || chart.chartData == null) return null;
        int id = chart.chartData.statPlanId;
        if (id < 0 || id >= pool.buffer.Length) return null;
        return pool.buffer[id];
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
        var statPlan = ResolveStatPlan(chart);
        if (statPlan == null) return;
        string newName = (value ?? string.Empty).Trim();
        statPlan.Rename(ref newName);                 // fires onNameChanged -> chart title repaints
        chart.TruncateStatPlanNameText();             // defensive title refresh
        RefreshSidebarName(chart.uiDashboard, statPlan); // sync the sidebar entry's displayed name
    }

    /// <summary>Update the sidebar entry's displayed name after a rename. DetermineEntryVisible does
    /// NOT do this for an already-visible entry: ResetTarget early-returns on an unchanged id and
    /// _Open() no-ops when the entry is already open, so the entry's nameInput keeps its old text.
    /// We set it directly, mirroring UIStatPlanEntry._OnOpen (nameInput.text = name, or null -> the
    /// "#id default-name" placeholder shows).</summary>
    private static void RefreshSidebarName(UIDashboard dash, StatPlan statPlan)
    {
        var statboard = dash != null ? dash.statboard : null;
        if (statboard == null || statboard.objectEntryPool == null || statPlan == null) return;
        var pool = statboard.objectEntryPool;
        for (int i = 0; i < pool.Count; i++)
        {
            var e = pool[i];
            if (e != null && e.statPlan != null && e.statPlan.id == statPlan.id && e.nameInput != null)
                e.nameInput.text = string.IsNullOrEmpty(statPlan.name) ? null : statPlan.name;
        }
    }

    private static void Hide()
    {
        // Clear the target before deactivating: deactivating a focused InputField fires onEndEdit
        // synchronously, so a re-entrant Commit must see a null target — otherwise a cancel (e.g.
        // CancelIfTargeting just before a delete) would implicitly commit the pending text. Mirrors
        // PageTabBar, which clears its rename guard before SetActive(false).
        _target = null;
        if (_input != null) _input.gameObject.SetActive(false);
    }
}
