using UnityEngine;
using UnityEngine.EventSystems;

namespace DashboardOverhaul;

/// <summary>
/// Attached to a chart's title Text. Double-clicking the title opens the rename input. Implements
/// only IPointerClickHandler (not pointer-down), so pointer-down still bubbles to UIChart's drag
/// logic and dragging the chart keeps working. Reads the owner chart's live chartData at click time.
/// </summary>
public class ChartTitleRenameTrigger : MonoBehaviour, IPointerClickHandler
{
    public UIChart Owner;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (Owner == null) return;
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (eventData.clickCount >= 2)
            ChartRename.Begin(Owner);
    }
}
