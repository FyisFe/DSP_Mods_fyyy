using UnityEngine;

namespace DashboardOverhaul;

/// <summary>
/// Lets Enter press the confirm button of one specific message box instead of the mouse: the
/// dashboard's "delete statistic" confirmation (<see cref="UIChartPatch"/> attaches it). The component
/// lives on that dialog only, so no other message box in the game is affected, and only the dialog in
/// front reacts while dialogs are stacked.
/// </summary>
public class ConfirmHotkey : MonoBehaviour
{
    public UIMessageBox Box;

    /// <summary>Make Enter confirm <paramref name="box"/> (its "确定" button is button 2).</summary>
    public static void Attach(UIMessageBox box)
    {
        if (box == null) return;
        var hotkey = box.gameObject.GetComponent<ConfirmHotkey>();
        if (hotkey == null) hotkey = box.gameObject.AddComponent<ConfirmHotkey>();
        hotkey.Box = box;
    }

    private void Update()
    {
        if (Box == null) return;
        if (UIMessageBox.TopMessage() != Box) return;
        if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter)) return;
        Box.OnButton2Click();
    }
}
