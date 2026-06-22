# Lock Window Position Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a checkbox to lock the XianTuEnhanced window position, When locked, the window cannot be dragged and always opens at the locked position.

**Architecture:** Add runtime state fields to BlueTuUIData for lock status and position. Add checkbox UI to XianTuEnhancedWindow with handlers to toggle drag functionality on the window's UIDragDrop component.

**Tech Stack:** C#, Unity UI, BepInEx, DSP Modding

---

## Task 1: Add State Fields to BlueTuUIData

**Files:**
- Modify: `XianTuEnhanced/BlueTuUIData.cs`

**Changes:**
Add two properties after line 122 (after `_repeatCount`):

```csharp
private bool _lockWindowPosition;
private Vector2? _lockedPosition;

public bool LockWindowPosition
{
    get => _lockWindowPosition;
    set => _lockWindowPosition = value;
}

public Vector2? LockedPosition
{
    get => _lockedPosition;
    set => _lockedPosition = value;
}
```

**Testing:**
- Build the project to verify no compilation errors

---

## Task 2: Add Checkbox and Logic to XianTuEnhancedWindow

**Files:**
- Modify: `XianTuEnhanced/XianTuEnhancedWindow.cs`

**Changes:**

1. Add field after line 13 (after `_enableCheckBox`):
```csharp
private MyCheckBox _lockPositionCheckBox;
```

2. In `_OnInit()`, after the Enable checkbox section (around line 55), add the lock position checkbox:
```csharp
// Lock Position checkbox
_lockPositionCheckBox = MyCheckBox.CreateCheckBox(LabelX, y, parent, _data.LockWindowPosition, "固定位置", 15);
_lockPositionCheckBox.OnChecked += OnLockPositionChanged;
MaxY = Mathf.Max(MaxY, y + _lockPositionCheckBox.Height);
y += RowHeight;
```

3. Add the handler method at the end of the class (before `RefreshInputFields`):
```csharp
private void OnLockPositionChanged()
{
    _data.LockWindowPosition = _lockPositionCheckBox.Checked;
    if (_data.LockWindowPosition)
    {
        _data.LockedPosition = GetComponent<RectTransform>().anchoredPosition;
    }
    SetDragEnabled(!_data.LockWindowPosition);
}

private void SetDragEnabled(bool enabled)
{
    var dragDrop = transform.Find("panel-bg")?.GetComponent<UIDragDrop>();
    if (dragDrop != null)
    {
        dragDrop.enabled = enabled;
    }
}
```

4. In `_OnOpen()`, add position restore and drag state after line 129 (after `RefreshInputFields()`):
```csharp
// Restore locked position if set
if (_data.LockedPosition.HasValue)
{
    GetComponent<RectTransform>().anchoredPosition = _data.LockedPosition.Value;
}
SetDragEnabled(!_data.LockWindowPosition);
```

5. Add using statement if needed at top of file:
```csharp
using UnityEngine.UI;
```

**Testing:**
- Build and run the game
- Press F2 to open window
- Check "固定位置" checkbox - window should not be draggable
- Uncheck - window should be draggable again
- Close and reopen window - should return to locked position
- Restart game - settings should reset

---

## Task 3: Update RefreshInputFields for Checkbox State

**Files:**
- Modify: `XianTuEnhanced/XianTuEnhancedWindow.cs`

**Changes:**
In `RefreshInputFields()` method, add after line 141 (after `_enableCheckBox.Checked`):
```csharp
_lockPositionCheckBox.Checked = _data.LockWindowPosition;
```

**Testing:**
- Build and test that checkbox state is correctly restored when window is reopened
