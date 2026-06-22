using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DashboardOverhaul;

/// <summary>一个页面标签按钮。左键切页 / 双击重命名 / 右键弹菜单 由 PageTabBar 处理。</summary>
public class PageTab : MonoBehaviour, IPointerClickHandler
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
}
