using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DashboardOverhaul;

/// <summary>A single page tab button. Left-click switches page; double-click renames; right-click
/// opens the menu; dragging reorders -- all handled by PageTabBar. Unity only promotes a press to a
/// drag past EventSystem.pixelDragThreshold, and a drag suppresses the click, so the click gestures
/// are unaffected.</summary>
public class PageTab : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public int Slot;
    private PageTabBar _bar;
    public Text Label;
    public Image Background;

    public void Setup(PageTabBar bar, int slot, string label, bool current)
    {
        _bar = bar;
        Slot = slot;
        if (Label != null) Label.text = label;
        SetCurrent(current);
    }

    public void SetCurrent(bool current)
    {
        if (Background == null) return;
        var c = _bar != null ? _bar.Dashboard.focusColor : Color.gray;
        Background.color = current ? c : new Color(c.r, c.g, c.b, 0.15f);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_bar == null) return;
        if (eventData.button == PointerEventData.InputButton.Right)
            _bar.OpenContextMenu(this);
        else if (eventData.clickCount >= 2)
            _bar.BeginRename(this);
        else
            _bar.SwitchTo(Slot);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (_bar != null) _bar.BeginDrag(this, eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_bar != null) _bar.Drag(this, eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (_bar != null) _bar.EndDrag(this, eventData);
    }
}
