---
name: Lock Window Position Feature
description: Add checkbox to lock XianTuEnhanced window position
type: design
---

# Lock Window Position Feature

## Overview

Add a checkbox to the XianTuEnhanced window that allows users to lock the window position. When locked, the window cannot be dragged and will always open at the locked position.

## Requirements

1. Add a "Lock Position" checkbox below the existing "Enable" checkbox
2. When checked: record current window position, disable dragging
3. When unchecked: restore dragging functionality, keep the recorded position
4. When opening window: if position is locked, move window to that position
5. Settings are stored in memory only (reset on game restart)

## Implementation

### BlueTuUIData.cs

Add two runtime fields:

```csharp
public bool LockWindowPosition { get; set; }
public Vector2? LockedPosition { get; set; }
```

### XianTuEnhancedWindow.cs

1. Add checkbox field:
   ```csharp
   private MyCheckBox _lockPositionCheckBox;
   ```

2. In `_OnInit()`, add checkbox after the Enable checkbox:
   ```csharp
   _lockPositionCheckBox = MyCheckBox.CreateCheckBox(LabelX, y, parent, _data.LockWindowPosition, "固定位置", 15);
   _lockPositionCheckBox.OnChecked += OnLockPositionChanged;
   ```

3. Add handler method:
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
   ```

4. In `_OnOpen()`, restore position if locked:
   ```csharp
   if (_data.LockedPosition.HasValue)
   {
       GetComponent<RectTransform>().anchoredPosition = _data.LockedPosition.Value;
   }
   SetDragEnabled(!_data.LockWindowPosition);
   ```

5. Add drag control method:
   ```csharp
   private void SetDragEnabled(bool enabled)
   {
       var dragDrop = transform.Find("panel-bg")?.GetComponent<UIDragDrop>();
       if (dragDrop != null)
       {
           dragDrop.enabled = enabled;
       }
   }
   ```

### MyWindow.cs

No changes needed. The `UIDragDrop` component on `panel-bg` handles window dragging.

## UI Layout

```
[ ] 启用
[ ] 固定位置    <-- New checkbox
层数    [___]
层高    [___]
...
```

## Behavior

| Action | Result |
|--------|--------|
| Check "固定位置" | Record current position, disable drag |
| Uncheck "固定位置" | Enable drag, keep recorded position |
| Open window (locked) | Move to locked position, disable drag |
| Open window (unlocked) | Stay at last position, enable drag |
| Restart game | All settings reset (memory-only storage) |
