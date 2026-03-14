using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BuildingPosViewer;

public static class PosDisplayHelper
{
    private static readonly Dictionary<Text, CopyButtons> ButtonMap = new();

    public static void Show(Text titleText, PlanetFactory factory, int entityId)
    {
        if (titleText == null || factory == null || entityId <= 0) return;

        // Calculate coordinates
        var pos = factory.entityPool[entityId].pos;
        var npos = pos.normalized;
        var planet = factory.planet;
        var segment = planet.aux?.activeGrid?.segment ?? 200;
        var latRadPerGrid = BlueprintUtils.GetLatitudeRadPerGrid(segment);
        var lonSegCnt = BlueprintUtils.GetLongitudeSegmentCount(npos, segment);
        var lonRadPerGrid = BlueprintUtils.GetLongitudeRadPerGrid(lonSegCnt, segment);
        var x = BlueprintUtils.GetLongitudeRad(npos) / lonRadPerGrid;
        var y = BlueprintUtils.GetLatitudeRad(npos) / latRadPerGrid;
        var z = (pos.magnitude - planet.realRadius - 0.2f) / 1.3333333f;

        if (!ButtonMap.TryGetValue(titleText, out var btns) || btns == null || btns.root == null)
        {
            btns = CreateButtons(titleText);
            ButtonMap[titleText] = btns;
        }

        btns.valX = Fmt(x);
        btns.valY = Fmt(y);
        btns.valZ = Fmt(z);
        btns.btnZ.gameObject.SetActive(z is not (< 0.001f and > -0.001f));
        btns.root.SetActive(true);

        // Position right after the title text content
        var rt = btns.root.GetComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(titleText.preferredWidth + 8, 0);
    }

    public static void Cleanup()
    {
        foreach (var kvp in ButtonMap)
            if (kvp.Value?.root != null)
                Object.Destroy(kvp.Value.root);
        ButtonMap.Clear();
    }

    private static string Fmt(float f) => f.ToString("F6").TrimEnd('0').TrimEnd('.');

    private static CopyButtons CreateButtons(Text titleText)
    {
        var titleRT = titleText.rectTransform;

        var root = new GameObject("CopyBtns");
        root.transform.SetParent(titleText.transform, false);
        var rootRT = root.AddComponent<RectTransform>();
        rootRT.anchorMin = new Vector2(0, 0.5f);
        rootRT.anchorMax = new Vector2(0, 0.5f);
        rootRT.pivot = new Vector2(0, 0.5f);
        rootRT.sizeDelta = new Vector2(80, 20);

        var hlg = root.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 3;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.padding = new RectOffset(0, 0, 0, 0);

        var btns = new CopyButtons { root = root };
        btns.btnX = MakeBtn(root.transform, titleText.font, "x", new Color(1f, 0.7f, 0.7f), btns, 0);
        btns.btnY = MakeBtn(root.transform, titleText.font, "y", new Color(0.7f, 1f, 0.7f), btns, 1);
        btns.btnZ = MakeBtn(root.transform, titleText.font, "z", new Color(0.7f, 0.7f, 1f), btns, 2);

        return btns;
    }

    private static Button MakeBtn(Transform parent, Font font, string label, Color color, CopyButtons btns, int axis)
    {
        var go = new GameObject($"Btn_{label}");
        go.transform.SetParent(parent, false);

        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.3f, 0.3f, 0.3f, 0.8f);

        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = 20;
        le.preferredHeight = 18;

        var textGO = new GameObject("Label");
        textGO.transform.SetParent(go.transform, false);
        var text = textGO.AddComponent<Text>();
        text.font = font;
        text.text = label;
        text.fontSize = 12;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        var textRT = text.rectTransform;
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = bg;
        var c = btn.colors;
        c.normalColor = new Color(0.3f, 0.3f, 0.3f, 0.8f);
        c.highlightedColor = new Color(0.5f, 0.5f, 0.5f, 0.9f);
        c.pressedColor = new Color(0.6f, 0.6f, 0.6f, 1f);
        btn.colors = c;
        btn.onClick.AddListener(() =>
        {
            var val = axis switch { 0 => btns.valX, 1 => btns.valY, _ => btns.valZ };
            GUIUtility.systemCopyBuffer = val;
        });

        return btn;
    }

    private class CopyButtons
    {
        public GameObject root;
        public Button btnX, btnY, btnZ;
        public string valX, valY, valZ;
    }
}
