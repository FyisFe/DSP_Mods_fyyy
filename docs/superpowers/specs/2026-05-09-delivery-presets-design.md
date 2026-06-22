# DeliveryPresets — design

A DSP mod that lets the player save and reload the **left-side logistics column** (`UIPlayerDeliveryPanel`, the 3–5-wide grid backed by `Player.deliveryPackage`) as named JSON presets stored under `BepInEx/config/DeliveryPresets/`.

## Decided behaviour

- **Storage:** one JSON file per preset, in `BepInEx/config/DeliveryPresets/`.
- **UI entry point:** two buttons (`导出` / `导入`) injected into `UIPlayerDeliveryPanel`. They sit in a horizontal strip just *below* the visible grid (parented to the panel, anchored to its bottom edge, extending downward by ~26 px). The strip is hidden whenever the panel itself is hidden / the player has no slots unlocked.
- **Export click:** opens a popup containing
  - a text input for the new preset name,
  - a list of existing `.json` files in the folder (click to overwrite, with single confirmation),
  - a "保存" button, and
  - a "在文件夹中打开" link.
- **Import click:** opens a popup listing all `.json` files. Single click loads. Each row also has a small `×` to delete.
- **Import semantics — Option A (full replace):** every grid slot in `deliveryPackage.grids[0..99]` is reset to match the file. Slots present in the file get their `itemId / requireCount / recycleCount` set; slots not in the file get cleared (`Player.SetDeliveryItem(idx, 0)`).
- **Safety guard — Option iii:** before any destructive write, count slots whose current `count + ordered > 0` and whose item / settings would change. If any, show a confirmation dialog listing item names + counts. Cancel aborts the import.
- **Tech-derived state is NOT touched:** `unlocked`, `rowCount`, `colCount`, `stackSizeMultiplier` are derived from the player's tech tree on load and reset by the game itself; the mod never writes them. Items in slots beyond the player's current `colCount` simply remain inactive until they unlock those columns.
- **`enable` flag:** restored from the preset (the on/off 物流 toggle).

## JSON schema (v1)

```json
{
  "version": 1,
  "savedAt": "2026-05-09T14:32:11Z",
  "modVersion": "1.0.0",
  "enable": true,
  "slots": [
    {
      "gridIndex": 4,
      "itemId": 2001,
      "itemName": "传送带",
      "requireCount": 300,
      "recycleCount": 2147483647
    }
  ]
}
```

- `gridIndex` is the raw 0..99 index into `DeliveryPackage.grids` — stable across versions and not affected by the player's current colCount.
- `itemName` is informational only (for human-readable diff/edit). Imports key off `itemId`.
- `recycleCount` of `int.MaxValue` (`2147483647`) means "不回收".
- Unknown / removed `itemId` on import → skip slot with a log warning.

## Components

| File | Responsibility |
|---|---|
| `DeliveryPresets.csproj` | net472, BepInEx 5, refs Assembly-CSharp + UnityEngine.UI (mirror BlueprintSearch). |
| `DeliveryPresetsPlugin.cs` | BepInEx entry; harmony PatchAll; config bindings (none yet — config dir is fixed). |
| `PresetData.cs` | `[Serializable]` `PresetFile` and `PresetSlot`. Uses Unity `JsonUtility` so no external deps. |
| `PresetIO.cs` | List / Load / Save / Delete preset files. Sanitises file names. Reads from `Path.Combine(Paths.ConfigPath, "DeliveryPresets")`. |
| `PresetService.cs` | Pure logic: snapshot current `deliveryPackage` → `PresetFile`; apply `PresetFile` → `deliveryPackage`; compute "what will be lost" diff for the confirm dialog. |
| `Patches/UIPlayerDeliveryPanelPatches.cs` | Harmony postfix on `_OnCreate` and `_OnOpen` — instantiates the button bar once and (re)positions it. |
| `UI/PresetButtonBar.cs` | Two-button strip (`导出`, `导入`) below the grid. Style mimics the inventory tip / vanilla button look. |
| `UI/PresetPopup.cs` | Shared popup used by both buttons. Two modes: `Save` (input + existing-file list) and `Load` (file list with delete). Anchored to the right of the panel. |
| `UI/ConfirmDialog.cs` | Modal-ish confirmation: title, body text, `确认` / `取消`. |
| `package/manifest.json`, `README.md`, `CHANGELOG.md` | Thunderstore packaging (mirror BlueprintSearch). |

## Edge cases

- **Panel not unlocked yet:** export/import buttons hidden via `DetermineVisible` (the same check vanilla uses).
- **Item already exists in another slot when importing:** the game enforces uniqueness in `OnItemPickerReturn`. We replicate this by detecting duplicates in the preset (same `itemId` twice) and refusing to import with an error tip. For cross-slot uniqueness with the existing inventory, the full-replace clears all other slots first, so no duplicates can occur.
- **Save name collision:** clicking an existing file name in the save popup pre-fills the input *and* requires the user to click 保存 again to overwrite (no implicit overwrite from a single click).
- **File-system errors:** caught around all IO, surfaced as `UIRealtimeTip` popups; never throw out of Harmony.

## Out of scope (v1)

- Multi-select / merging multiple presets.
- Renaming presets in-place (delete + re-save instead).
- Cross-save sharing UI inside the game beyond the file folder.
- Multiplayer considerations (vanilla DSP is single-player).
