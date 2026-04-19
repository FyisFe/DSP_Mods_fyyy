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
        // ClearQuery sets Active=false, so the SetCurrentDirectory postfix is a no-op here
        // and vanilla restores the normal folder view.
        if (browser != null && browser.currentDirectoryInfo != null)
            browser.SetCurrentDirectory(browser.currentDirectoryInfo.FullName);
    }

    private void Update()
    {
        if (!SearchState.pendingRefresh) return;
        float debounce = (BlueprintSearchPlugin.DebounceMs?.Value ?? 120) * 0.001f;
        if (Time.unscaledTime - SearchState.lastChangeTime < debounce) return;
        SearchState.pendingRefresh = false;
        SearchState.tokens = SearchFilter.Tokenize(SearchState.query);
        if (browser != null && browser.currentDirectoryInfo != null)
            browser.SetCurrentDirectory(browser.currentDirectoryInfo.FullName);
    }
}
