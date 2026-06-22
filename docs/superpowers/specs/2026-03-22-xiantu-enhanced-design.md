# XianTu Enhanced Design Spec

## Overview

XianTuEnhanced is a DSP (Dyson Sphere Program) mod that replicates all features of the original XianTu blueprint manipulation mod, with a completely rebuilt UI following the UXAssist programmatic UI style. All UI text is in Chinese.

## Goals

- Same feature set as XianTu: layer duplication, scaling, bias/offset, rotation with pivot, repeat build, copy blueprint
- Programmatic UI using UXAssist's `MyWindow` pattern (no Unity asset bundles)
- Input fields only (no sliders, no drag controls)
- Self-contained: UXAssist UI classes copied into the project, no external dependency
- Single window toggled via F2 hotkey
- Chinese-only UI

## Architecture

### Three Layers

1. **UI Layer** (new) — `XianTuEnhancedWindow` extends `MyWindow`. Built programmatically using `AddText2()`, `AddInputField()`, `MyCheckBox.CreateCheckBox()`, `AddButton()`. No asset bundles, no panel stack, no slider/drag controls.

2. **Data Layer** (ported) — `BlueTuUIData` singleton with change notification events. Properties: Bias (Vector3), Scale (Vector3), Pivot (Vector3), LayerHeight (float), LayerNumber (int), Rotate (float), Enable (bool), RepeatCount (int). Action callbacks: OnBuildBtn, OnResetBtn, OnCopyBtn, OnRepeatBuildBtn.
   - **Note:** RepeatCount does not fire OnValueChange (it is only read at RepeatBuild button-click time, not used for live preview). This is intentional.
   - **Note:** Pivot Z is always 0 and not exposed in the UI; only X and Y are user-editable.

3. **Logic Layer** (ported) — `BlueTuController` handles blueprint manipulation. Algorithms: CtrlLayerNumber (duplicate layers), CtrlLayerHeight (adjust spacing), CtrlRotate (rotate around pivot), CtrlBiasData (translate), CtrlScale (scale around pivot), ResetBuildDuiDie (foundation auto-creation), Build/RepeatBuild execution.

### Dropped from Original XianTu

- Asset bundle loading system (ABLoader, ABLoad, ABEmbeddedAssetsLoad, ABFileLoad, ResourceLoad, ILoad)
- Panel stack system (PanelManager, BasePanel, XianTuBasePanel)
- UIManager / CanvasMonoEvent (replaced by MyWindowManager)
- UIValue<T> generic binding system (replaced by direct input field wiring)
- UITool (not needed with programmatic UI)
- Drag controls, sliders, scrollbars
- Singleton<T> generic class (not needed)
- BlueTuDatabase (replaced by direct embedded resource loading)

### Copied from UXAssist

- `MyWindow.cs` — window base class, MyWindowWithTabs, MyWindowManager (with Harmony lifecycle patches). Namespace: `UXAssist.UI` → `XianTuEnhanced.UI`.
- `MyCheckBox.cs` — checkbox component for Enable toggle. Namespace: `UXAssist.UI` → `XianTuEnhanced.UI`.
- `Util.cs` — RectTransform normalization helpers. Namespace: `UXAssist.UI` → `XianTuEnhanced.UI`.
- `PatchImpl.cs` — Harmony patch base class. Namespace: `UXAssist.Common` → `XianTuEnhanced.Common`.

### Ported from XianTu (with adaptation)

- `BlueTuUIData.cs` — namespace change only.
- `BlueTuController.cs` — significant adaptation required:
  - Namespace change
  - `UIManager.CanvasMonoEvent.onEnableEvent` removed; replaced with window `_OnOpen` callback
  - `BlueTuDatabase.Load("FoundationBlueTu")` replaced with direct embedded resource loading (see Foundation Loading section)
  - `using XianTu.UI` / `using ToolScripts` imports updated
- `BlueprintBuildingExtensions.cs` — ported extension methods (`IsBelt()`, `IsSlot()`), namespace change.
- `FoundationBlueTu.txt` — embedded as assembly resource (unchanged content).

## File Structure

```
XianTuEnhanced/
├── XianTuEnhanced.csproj
├── XianTuEnhancedPlugin.cs          # Main BepInEx plugin entry, F2 hotkey
├── XianTuEnhancedWindow.cs          # MyWindow subclass, builds all UI
├── BlueTuUIData.cs                  # Ported from XianTu (namespace change)
├── BlueTuController.cs              # Ported from XianTu (adapted, see above)
├── BlueprintBuildingExtensions.cs   # Ported extension methods
├── FoundationBlueTu.txt             # Embedded resource
├── UI/
│   ├── MyWindow.cs                  # Copied from UXAssist/UI (namespace change)
│   ├── MyCheckBox.cs                # Copied from UXAssist/UI (namespace change)
│   └── Util.cs                      # Copied from UXAssist/UI (namespace change)
├── Common/
│   └── PatchImpl.cs                 # Copied from UXAssist/Common (namespace change)
└── package/
    ├── manifest.json
    └── icon.png
```

## Window Layout

```
┌──────────────────────────────────────────┐
│ 仙图增强                              [X]│
├──────────────────────────────────────────┤
│ [✓] 启用                                 │
│                                          │
│ 层数    [    1    ]                       │
│ 层高    [   5.0   ]                       │
│                                          │
│ 缩放  X [  1.0  ] Y [  1.0  ] Z [  1.0 ]│
│ 偏移  X [  0.0  ] Y [  0.0  ] Z [  0.0 ]│
│ 中心点 X [  0.0  ] Y [  0.0  ]           │
│                                          │
│ 旋转    [   0.0   ]                      │
│ 重复次数 [    1    ]                      │
│                                          │
│ [建造] [重复建造] [复制] [重置]            │
└──────────────────────────────────────────┘
```

### UI Element Details

| Element | Type | Chinese Label |
|---------|------|---------------|
| Window title | Title bar | 仙图增强 |
| Enable | MyCheckBox (created via `MyCheckBox.CreateCheckBox` directly, not `AddCheckBox` which requires `ConfigEntry<bool>`) | 启用 |
| Layer Number | Text + InputField (int) | 层数 |
| Layer Height | Text + InputField (float) | 层高 |
| Scale X/Y/Z | Text + 3x InputField (float, narrower width ~60px each) | 缩放 |
| Bias X/Y/Z | Text + 3x InputField (float, narrower width ~60px each) | 偏移 |
| Pivot X/Y | Text + 2x InputField (float, narrower width ~80px each) | 中心点 |
| Rotation | Text + InputField (float) | 旋转 |
| Repeat Count | Text + InputField (int) | 重复次数 |
| Build | Button | 建造 |
| Repeat Build | Button | 重复建造 |
| Copy | Button | 复制 |
| Reset | Button | 重置 |

**Multi-field rows:** Scale, Bias, and Pivot rows place multiple input fields side-by-side. These use narrower widths (~60px for 3-field rows, ~80px for 2-field rows) instead of the default 210px. Each sub-field has a small X/Y/Z label prefix.

## Data Flow

### Startup

```
XianTuEnhancedPlugin.Start()           # Use Start(), not Awake() — UIRoot.instance is not ready in Awake()
  → MyWindowManager.InitBaseObjects()  # Clones game UI templates (requires UIRoot.instance)
  → MyWindowManager.Enable(true)       # Activates Harmony lifecycle patches for window management
  → MyWindowManager.CreateWindow<XianTuEnhancedWindow>("xiantu-enhanced", "仙图增强")
  → new BlueTuController()
```

### Cleanup

```
XianTuEnhancedPlugin.OnDestroy()
  → MyWindowManager.Enable(false)      # Deactivates lifecycle patches
```

### Hotkey Toggle (F2)

```
XianTuEnhancedPlugin.Update()
  → if F2 pressed AND NOT VFInput.inputing:   # Guard: don't toggle when typing in input fields
      window.Open() / window.Close()
```

### Input Field → Blueprint Update

```
InputField.onEndEdit → parse float/int
  → set BlueTuUIData property (Bias, Scale, etc.)
  → BlueTuUIData.OnValueChange fires
  → BlueTuController.OnUserChangeData()
  → CtrlBias/CtrlScale/CtrlRotate/CtrlLayerNumber/CtrlLayerHeight
  → _BuildTool_BluePrint_OnTick() updates preview
```

### Reset Flow

```
重置 button clicked
  → BlueTuUIData.OnResetBtn fires
  → BlueTuController.OnReset() detects active BuildTool_BlueprintPaste
  → BlueTuUIData.Reset() restores defaults
  → XianTuEnhancedWindow.RefreshInputFields() syncs UI from data
```

### Window Open

```
XianTuEnhancedWindow._OnOpen()
  → base._OnOpen() (auto-fit window size)
  → Detect active BuildTool_BlueprintPaste
  → Reset state if new blueprint detected
  → RefreshInputFields()
```

## Foundation Loading

The original XianTu uses `BlueTuDatabase.Load("FoundationBlueTu")` which goes through `Singleton<LoadManager>`. In XianTuEnhanced, this is replaced with direct embedded resource loading in `BlueTuController`:

```csharp
var assembly = Assembly.GetExecutingAssembly();
using var stream = assembly.GetManifestResourceStream("XianTuEnhanced.FoundationBlueTu.txt");
using var reader = new StreamReader(stream);
var blueprintString = reader.ReadToEnd();
var blueprintData = new BlueprintData();
blueprintData.FromBase64String(blueprintString);  // or appropriate DSP blueprint parsing method
_foundation = blueprintData.buildings[0];
```

The `FoundationBlueTu.txt` file is set as `EmbeddedResource` in the `.csproj`.

## Input Validation

- Integer fields (层数, 重复次数): `int.TryParse`, clamp to min 1
- Float fields (层高, 缩放, 偏移, 中心点, 旋转): `float.TryParse`, accept any value
- On parse failure: revert input field text to current data value

## Project Configuration

- Target Framework: .NET Framework 4.7.2
- BepInEx 5.x plugin
- Plugin GUID: `org.fyyy.xiantuenhanced`
- Assembly references: same as BuildingPosViewer (Assembly-CSharp.dll, UnityEngine.UI.dll, etc.)
- FoundationBlueTu.txt: embedded as assembly resource (`<EmbeddedResource>` in csproj)
- Post-build: Thunderstore ZIP packaging (same pattern as BuildingPosViewer)
- No custom Harmony patches needed — all lifecycle patching is handled by `MyWindowManager.Enable(true)` which registers patches for `UIRoot._OnOpen`, `UIRoot._OnUpdate`, `UIRoot._OnDestroy`, and `UIGame.ShutAllFunctionWindow`
