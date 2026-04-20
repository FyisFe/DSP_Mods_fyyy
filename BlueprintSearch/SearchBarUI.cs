using UnityEngine;
using UnityEngine.UI;

namespace BlueprintSearch;

/// <summary>
/// Builds and owns the search input + clear button. A single instance is created in the
/// UIBlueprintBrowser._OnCreate postfix. The bar is parented alongside the browser's
/// ScrollRect so it sits in the same left-column extent as the file grid.
/// </summary>
internal class SearchBarUI : MonoBehaviour
{
    private const float BarHeight = 24f;
    private const float ClearButtonWidth = 24f;
    private const float BarBottomGap = 4f; // gap between bar bottom and scroll-rect top

    internal UIBlueprintBrowser browser;
    internal InputField inputField;
    internal Button clearButton;

    internal static SearchBarUI Create(UIBlueprintBrowser browser)
    {
        var scrollRt = (RectTransform)browser.browserScroll.transform;

        var go = new GameObject("BlueprintSearchBar", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(scrollRt.parent, false);

        // Snapshot the scroll rect's top edge BEFORE we shrink it.
        float oldScrollTopInset = scrollRt.offsetMax.y;

        // Mirror the scroll rect's horizontal extent; anchor top only.
        rt.anchorMin = new Vector2(scrollRt.anchorMin.x, 1f);
        rt.anchorMax = new Vector2(scrollRt.anchorMax.x, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(scrollRt.offsetMin.x, oldScrollTopInset - BarHeight);
        rt.offsetMax = new Vector2(scrollRt.offsetMax.x, oldScrollTopInset);

        // Shrink the scroll rect from the top so the file grid starts below the bar.
        scrollRt.offsetMax = new Vector2(
            scrollRt.offsetMax.x,
            oldScrollTopInset - BarHeight - BarBottomGap);

        var ui = go.AddComponent<SearchBarUI>();
        ui.browser = browser;
        ui.BuildInputField(rt);
        ui.BuildClearButton(rt);
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
        if (SearchState.pendingRefresh)
        {
            float debounce = (BlueprintSearchPlugin.DebounceMs?.Value ?? 120) * 0.001f;
            if (Time.unscaledTime - SearchState.lastChangeTime >= debounce)
            {
                SearchState.pendingRefresh = false;
                SearchState.tokens = SearchFilter.Tokenize(SearchState.query);
                if (browser != null && browser.currentDirectoryInfo != null)
                    browser.SetCurrentDirectory(browser.currentDirectoryInfo.FullName);
            }
        }

        Patches.UIBlueprintBrowserPatches.ContinueStreaming();
    }
}
