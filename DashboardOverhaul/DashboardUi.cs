using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace DashboardOverhaul;

internal static class DashboardUi
{
    public static Text Text(Transform parent, Font font, string value, TextAnchor alignment = TextAnchor.MiddleCenter)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(8f, 0f);
        rt.offsetMax = new Vector2(-8f, 0f);
        var text = go.AddComponent<Text>();
        text.font = font;
        text.fontSize = 14;
        text.color = Color.white;
        text.alignment = alignment;
        text.supportRichText = false;
        text.raycastTarget = false;
        text.text = value;
        return text;
    }

    public static void StyleButton(Button button, Color color)
    {
        var colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = Color.Lerp(color, Color.white, 0.3f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = Color.Lerp(color, Color.white, 0.5f);
        colors.disabledColor = new Color(color.r, color.g, color.b, 0.08f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;
    }

    public static void Tip(GameObject go, string title, string description)
    {
        var tip = go.GetComponent<UIDashboardTip>() ?? go.AddComponent<UIDashboardTip>();
        tip.settings = new UIDashboardTip.Settings
        {
            tipType = UIDashboardTip.TipType.SimpleGeneral,
            tipTitle = title, tipText = description,
            delay = 0.4f, corner = 3, width = 300
        };
        tip.RefreshSimpleGeneralTipText();
    }

    public static void HideInput(InputField input)
    {
        if (input == null) return;
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == input.gameObject)
            EventSystem.current.SetSelectedGameObject(null);
        input.gameObject.SetActive(false);
    }
}
