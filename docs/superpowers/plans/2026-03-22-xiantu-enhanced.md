# XianTuEnhanced Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create the XianTuEnhanced mod — a DSP blueprint manipulation mod with UXAssist-style programmatic UI, Chinese-only text, input fields only.

**Architecture:** Three layers — UI (new MyWindow-based), Data (ported BlueTuUIData), Logic (ported BlueTuController). UXAssist UI classes copied in with namespace changes. No asset bundles, no sliders, no drag controls.

**Tech Stack:** C# / .NET 4.7.2, BepInEx 5.x, Unity 2022.3, Harmony 2.x

**Spec:** `docs/superpowers/specs/2026-03-22-xiantu-enhanced-design.md`

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `XianTuEnhanced/XianTuEnhanced.csproj` | Create | Project config, references, embedded resources |
| `XianTuEnhanced/Common/PatchImpl.cs` | Copy from UXAssist | Harmony patch base class |
| `XianTuEnhanced/UI/Util.cs` | Copy from UXAssist | RectTransform normalization helpers |
| `XianTuEnhanced/UI/MyCheckBox.cs` | Copy from UXAssist | Checkbox UI component |
| `XianTuEnhanced/UI/MyWindow.cs` | Copy from UXAssist | Window base, manager, lifecycle patches |
| `XianTuEnhanced/BlueTuUIData.cs` | Port from XianTu | Data model with change notification |
| `XianTuEnhanced/BlueprintBuildingExtensions.cs` | Port from XianTu | IsBelt/IsSlot extension methods |
| `XianTuEnhanced/FoundationBlueTu.txt` | Copy from XianTu | Embedded blueprint resource |
| `XianTuEnhanced/BlueTuController.cs` | Port+adapt from XianTu | Blueprint manipulation logic |
| `XianTuEnhanced/XianTuEnhancedWindow.cs` | Create new | MyWindow subclass, all UI elements |
| `XianTuEnhanced/XianTuEnhancedPlugin.cs` | Create new | BepInEx plugin entry, F2 hotkey |
| `XianTuEnhanced/package/manifest.json` | Create new | Thunderstore package manifest |
| `DSP_Mods_fyyy.sln` | Modify | Add XianTuEnhanced project reference |

---

### Task 1: Project Scaffolding

**Files:**
- Create: `XianTuEnhanced/XianTuEnhanced.csproj`
- Create: `XianTuEnhanced/FoundationBlueTu.txt`
- Modify: `DSP_Mods_fyyy.sln`

- [ ] **Step 1: Create the project directory**

```bash
mkdir -p XianTuEnhanced/UI XianTuEnhanced/Common XianTuEnhanced/package
```

- [ ] **Step 2: Create XianTuEnhanced.csproj**

Create `XianTuEnhanced/XianTuEnhanced.csproj`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <AssemblyName>XianTuEnhanced</AssemblyName>
    <BepInExPluginGuid>org.fyyy.xiantuenhanced</BepInExPluginGuid>
    <Description>DSP MOD - XianTuEnhanced</Description>
    <Version>1.0.0</Version>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <LangVersion>latest</LangVersion>
    <RestoreAdditionalProjectSources>https://nuget.bepinex.dev/v3/index.json</RestoreAdditionalProjectSources>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="BepInEx.Core" Version="5.*" />
    <PackageReference Include="BepInEx.PluginInfoProps" Version="1.*" />
    <PackageReference Include="UnityEngine.Modules" Version="2022.3.62" IncludeAssets="compile" />
  </ItemGroup>

  <ItemGroup>
    <Reference Include="Assembly-CSharp">
      <HintPath>..\..\DSP_Mods\AssemblyFromGame\Assembly-CSharp.dll</HintPath>
    </Reference>
    <Reference Include="UnityEngine.UI">
      <HintPath>..\..\DSP_Mods\AssemblyFromGame\UnityEngine.UI.dll</HintPath>
    </Reference>
  </ItemGroup>

  <ItemGroup>
    <EmbeddedResource Include="FoundationBlueTu.txt" />
  </ItemGroup>

  <ItemGroup Condition="'$(TargetFramework.TrimEnd(`0123456789`))' == 'net'">
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />
  </ItemGroup>

  <Target Name="PostBuild" AfterTargets="PostBuildEvent" Condition="'$(Configuration)' == 'Release'">
    <Exec Command="del /F /Q package\$(ProjectName)-$(Version).zip
powershell Compress-Archive -Force -DestinationPath 'package/$(ProjectName)-$(Version).zip' -Path &quot;$(TargetPath)&quot;, package/icon.png, package/manifest.json" />
  </Target>

</Project>
```

- [ ] **Step 3: Copy FoundationBlueTu.txt**

Copy from `../DSP_MODS_TO/XianTu/FoundationBlueTu.txt` to `XianTuEnhanced/FoundationBlueTu.txt`. Contents:

```
BLUEPRINT:0,10,0,0,0,0,0,0,637975354775775693,0.9.26.13034,OnBelt,"H4sIAAAAAAAAC2NkQAWMUAxh/2dgWABlMsKFEWoPSG5DZZs4g/BFdmWG/1CAZBwDAHYxJApsAAAA"DEB2D60B60B70FA664332C993B2453B2
```

- [ ] **Step 4: Add project to solution**

Add XianTuEnhanced project to `DSP_Mods_fyyy.sln`. Use `dotnet sln add`:

```bash
cd C:/Users/Yi/Applications/Games/dsp/code/DSP_Mods_fyyy
dotnet sln add XianTuEnhanced/XianTuEnhanced.csproj
```

- [ ] **Step 5: Commit**

```bash
git add XianTuEnhanced/XianTuEnhanced.csproj XianTuEnhanced/FoundationBlueTu.txt DSP_Mods_fyyy.sln
git commit -m "feat(XianTuEnhanced): scaffold project with csproj and embedded resource"
```

---

### Task 2: Copy UXAssist UI Infrastructure

**Files:**
- Create: `XianTuEnhanced/Common/PatchImpl.cs` (from `../../DSP_Mods/UXAssist/Common/PatchImpl.cs`)
- Create: `XianTuEnhanced/UI/Util.cs` (from `../../DSP_Mods/UXAssist/UI/Util.cs`)
- Create: `XianTuEnhanced/UI/MyCheckBox.cs` (from `../../DSP_Mods/UXAssist/UI/MyCheckBox.cs`)
- Create: `XianTuEnhanced/UI/MyWindow.cs` (from `../../DSP_Mods/UXAssist/UI/MyWindow.cs`)

- [ ] **Step 1: Create PatchImpl.cs**

Copy `../../DSP_Mods/UXAssist/Common/PatchImpl.cs` to `XianTuEnhanced/Common/PatchImpl.cs`.
Change namespace from `UXAssist.Common` to `XianTuEnhanced.Common`.

```csharp
using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace XianTuEnhanced.Common;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class PatchGuidAttribute(string guid) : Attribute
{
    public string Guid { get; } = guid;
}

public enum PatchCallbackFlag
{
    CallOnEnableBeforePatch,
    CallOnDisableAfterUnpatch,
}

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public class PatchSetCallbackFlagAttribute(PatchCallbackFlag flag) : Attribute
{
    public PatchCallbackFlag Flag { get; } = flag;
}

public class PatchImpl<T> where T : PatchImpl<T>, new()
{
    protected static T Instance { get; } = new();

    protected Harmony _patch;

    public static void Enable(bool enable)
    {
        var thisInstance = Instance;
        if (enable)
        {
            if (thisInstance._patch != null) return;
            var guid = typeof(T).GetCustomAttribute<PatchGuidAttribute>()?.Guid ?? $"PatchImpl.{typeof(T).FullName ?? typeof(T).ToString()}";
            var callOnEnableBefore = typeof(T).GetCustomAttributes<PatchSetCallbackFlagAttribute>().Any(n => n.Flag == PatchCallbackFlag.CallOnEnableBeforePatch);
            if (callOnEnableBefore) thisInstance.OnEnable();
            thisInstance._patch = Harmony.CreateAndPatchAll(typeof(T), guid);
            if (!callOnEnableBefore) thisInstance.OnEnable();
            return;
        }
        if (thisInstance._patch == null) return;
        var callOnDisableAfter = typeof(T).GetCustomAttributes<PatchSetCallbackFlagAttribute>().Any(n => n.Flag == PatchCallbackFlag.CallOnDisableAfterUnpatch);
        if (!callOnDisableAfter) thisInstance.OnDisable();
        thisInstance._patch.UnpatchSelf();
        thisInstance._patch = null;
        if (callOnDisableAfter) thisInstance.OnDisable();
    }

    public static Harmony GetHarmony() => Instance._patch;

    protected virtual void OnEnable() { }
    protected virtual void OnDisable() { }
}
```

- [ ] **Step 2: Create Util.cs**

Copy `../../DSP_Mods/UXAssist/UI/Util.cs` to `XianTuEnhanced/UI/Util.cs`.
Change namespace from `UXAssist.UI` to `XianTuEnhanced.UI`.

```csharp
using UnityEngine;

namespace XianTuEnhanced.UI;

public static class Util
{
    public static RectTransform NormalizeRectWithTopLeft(Component cmp, float left, float top, Transform parent = null)
    {
        if (cmp.transform is not RectTransform rect) return null;
        if (parent != null)
        {
            rect.SetParent(parent, false);
        }
        rect.anchorMax = new Vector2(0f, 1f);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition3D = new Vector3(left, -top, 0f);
        return rect;
    }

    public static RectTransform NormalizeRectWithTopRight(Component cmp, float right, float top, Transform parent = null)
    {
        if (cmp.transform is not RectTransform rect) return null;
        if (parent != null)
        {
            rect.SetParent(parent, false);
        }
        rect.anchorMax = new Vector2(1f, 1f);
        rect.anchorMin = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition3D = new Vector3(-right, -top, 0f);
        return rect;
    }

    public static RectTransform NormalizeRectWithBottomLeft(Component cmp, float left, float bottom, Transform parent = null)
    {
        if (cmp.transform is not RectTransform rect) return null;
        if (parent != null)
        {
            rect.SetParent(parent, false);
        }
        rect.anchorMax = new Vector2(0f, 0f);
        rect.anchorMin = new Vector2(0f, 0f);
        rect.pivot = new Vector2(0f, 0f);
        rect.anchoredPosition3D = new Vector3(left, bottom, 0f);
        return rect;
    }

    public static RectTransform NormalizeRectWithMargin(Component cmp, float top, float left, float bottom, float right, Transform parent = null)
    {
        if (cmp.transform is not RectTransform rect) return null;
        if (parent != null)
        {
            rect.SetParent(parent, false);
        }
        rect.anchoredPosition3D = Vector3.zero;
        rect.localScale = Vector3.one;
        rect.anchorMax = Vector2.one;
        rect.anchorMin = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMax = new Vector2(-right, -top);
        rect.offsetMin = new Vector2(left, bottom);
        return rect;
    }

    public static RectTransform NormalizeRectCenter(GameObject go, float width = 0, float height = 0)
    {
        if (go.transform is not RectTransform rect) return null;
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        if (width > 0 && height > 0)
        {
            rect.sizeDelta = new Vector2(width, height);
        }
        return rect;
    }
}
```

- [ ] **Step 3: Create MyCheckBox.cs**

Copy `../../DSP_Mods/UXAssist/UI/MyCheckBox.cs` to `XianTuEnhanced/UI/MyCheckBox.cs`.
Change namespace from `UXAssist.UI` to `XianTuEnhanced.UI`.

```csharp
using System;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace XianTuEnhanced.UI;

public class MyCheckBox : MonoBehaviour
{
    public RectTransform rectTrans;
    public UIButton uiButton;
    public Image boxImage;
    public Image checkImage;
    public Text labelText;
    public event Action OnChecked;
    private bool _checked;

    private static GameObject _baseObject;

    private static readonly Color BoxColor = new(1f, 1f, 1f, 100f / 255f);
    private static readonly Color CheckColor = new(1f, 1f, 1f, 1f);
    private static readonly Color TextColor = new(178f / 255f, 178f / 255f, 178f / 255f, 168f / 255f);

    public static void InitBaseObject()
    {
        if (_baseObject) return;
        var go = Instantiate(UIRoot.instance.uiGame.buildMenu.uxFacilityCheck.gameObject);
        go.name = "my-checkbox";
        go.SetActive(false);
        var comp = go.transform.Find("text");
        if (comp)
        {
            var txt = comp.GetComponent<Text>();
            if (txt) txt.text = "";
            var localizer = comp.GetComponent<Localizer>();
            if (localizer) DestroyImmediate(localizer);
        }
        _baseObject = go;
    }

    protected void OnDestroy()
    {
        if (_config != null) _config.SettingChanged -= _configChanged;
    }

    public static MyCheckBox CreateCheckBox(float x, float y, RectTransform parent, ConfigEntry<bool> config, string label = "", int fontSize = 15)
    {
        return CreateCheckBox(x, y, parent, config.Value, label, fontSize).WithConfigEntry(config);
    }

    public static MyCheckBox CreateCheckBox(float x, float y, RectTransform parent, bool check, string label = "", int fontSize = 15)
    {
        return CreateCheckBox(x, y, parent, fontSize).WithCheck(check).WithLabelText(label);
    }

    public static MyCheckBox CreateCheckBox(float x, float y, RectTransform parent, int fontSize = 15)
    {
        var go = Instantiate(_baseObject);
        go.name = "my-checkbox";
        go.SetActive(true);
        var cb = go.AddComponent<MyCheckBox>();
        var rect = Util.NormalizeRectWithTopLeft(cb, x, y, parent);

        cb.rectTrans = rect;
        cb.uiButton = go.GetComponent<UIButton>();
        cb.boxImage = go.transform.GetComponent<Image>();
        cb.checkImage = go.transform.Find("checked")?.GetComponent<Image>();
        Util.NormalizeRectWithTopLeft(cb.checkImage, 0f, 0f);

        var child = go.transform.Find("text");
        if (child != null)
        {
            cb.labelText = child.GetComponent<Text>();
            if (cb.labelText)
            {
                cb.labelText.text = "";
                cb.labelText.fontSize = fontSize;
                cb.UpdateLabelTextWidth();
            }
        }

        cb.uiButton.onClick += cb.OnClick;
        return cb;
    }

    private void UpdateLabelTextWidth()
    {
        if (labelText) labelText.rectTransform.sizeDelta = new Vector2(labelText.preferredWidth, labelText.rectTransform.sizeDelta.y);
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            _checked = value;
            checkImage.enabled = value;
        }
    }

    public void SetLabelText(string val)
    {
        if (labelText != null)
        {
            labelText.text = val.Translate();
            UpdateLabelTextWidth();
        }
    }

    public void SetEnable(bool on)
    {
        if (uiButton) uiButton.enabled = on;
        if (on)
        {
            if (boxImage) boxImage.color = BoxColor;
            if (checkImage) checkImage.color = CheckColor;
            if (labelText) labelText.color = TextColor;
        }
        else
        {
            if (boxImage) boxImage.color = BoxColor.RGBMultiplied(0.5f);
            if (checkImage) checkImage.color = CheckColor.RGBMultiplied(0.5f);
            if (labelText) labelText.color = TextColor.RGBMultiplied(0.5f);
        }
    }

    private EventHandler _configChanged;
    private Action _checkedChanged;
    private ConfigEntry<bool> _config;
    public void SetConfigEntry(ConfigEntry<bool> config)
    {
        if (_checkedChanged != null) OnChecked -= _checkedChanged;
        if (_configChanged != null) config.SettingChanged -= _configChanged;

        _config = config;
        _checkedChanged = () => config.Value = !config.Value;
        OnChecked += _checkedChanged;
        _configChanged = (_, _) => Checked = config.Value;
        config.SettingChanged += _configChanged;
    }

    public MyCheckBox WithLabelText(string val)
    {
        SetLabelText(val);
        return this;
    }

    public MyCheckBox WithCheck(bool check)
    {
        Checked = check;
        return this;
    }

    public MyCheckBox WithSmallerBox(float boxSize = 20f)
    {
        var oldWidth = rectTrans.sizeDelta.x;
        rectTrans.sizeDelta = new Vector2(boxSize, boxSize);
        checkImage.rectTransform.sizeDelta = new Vector2(boxSize, boxSize);
        labelText.rectTransform.sizeDelta = new Vector2(labelText.rectTransform.sizeDelta.x, boxSize);
        labelText.rectTransform.localPosition = new Vector3(labelText.rectTransform.localPosition.x + boxSize - oldWidth, labelText.rectTransform.localPosition.y, labelText.rectTransform.localPosition.z);
        return this;
    }

    public MyCheckBox WithEnable(bool on)
    {
        SetEnable(on);
        return this;
    }

    public MyCheckBox WithConfigEntry(ConfigEntry<bool> config)
    {
        SetConfigEntry(config);
        return this;
    }

    public void OnClick(int obj)
    {
        _checked = !_checked;
        checkImage.enabled = _checked;
        OnChecked?.Invoke();
    }

    public float Width => rectTrans.sizeDelta.x + labelText.rectTransform.sizeDelta.x;
    public float Height => Math.Max(rectTrans.sizeDelta.y, labelText.rectTransform.sizeDelta.y);
}
```

- [ ] **Step 4: Create MyWindow.cs**

Copy `../../DSP_Mods/UXAssist/UI/MyWindow.cs` to `XianTuEnhanced/UI/MyWindow.cs`.
Changes:
- Namespace: `UXAssist.UI` → `XianTuEnhanced.UI`
- Add `using XianTuEnhanced.Common;`
- Remove `using UXAssist.Common;`
- **Remove methods referencing UI components we don't copy** (these will cause compilation errors):
  - Remove `AddFlatButton` method (references `MyFlatButton`)
  - Remove `AddComboBox` method (references `MyComboBox`)
  - Remove `AddCornerComboBox` method (references `MyCornerComboBox`)
  - Remove all `AddSlider` overloads (references `MySlider`)
  - Remove all `AddSideSlider` overloads (references `MySideSlider`)
  - Remove the entire `#region Slider` ... `#endregion` block (ValueMapper classes + slider methods)
  - In `MyWindowManager.InitBaseObjects()`, keep only `MyWindow.InitBaseObject()` and `MyCheckBox.InitBaseObject()` — remove calls to `MyCheckButton.InitBaseObject()`, `MyComboBox.InitBaseObject()`, `MyCornerComboBox.InitBaseObject()`, `MyFlatButton.InitBaseObject()`
  - In `MyWindowWithTabs`, if it references `MyCheckButton`, remove those references
- Keep: `MyWindow`, `MyWindowWithTabs`, `MyWindowManager`, `AddText`, `AddText2`, `AddTipsButton`, `AddTipsButton2`, `AddButton` (both overloads), `AddCheckBox`, `AddInputField` (both overloads)

The full file is large (691 lines). Copy from `C:\Users\Yi\Applications\Games\dsp\code\DSP_Mods\UXAssist\UI\MyWindow.cs` and apply the namespace changes and removals listed above.

- [ ] **Step 5: Build to verify infrastructure compiles**

```bash
cd C:/Users/Yi/Applications/Games/dsp/code/DSP_Mods_fyyy
dotnet build XianTuEnhanced/XianTuEnhanced.csproj
```

Expected: Build succeeds (or succeeds with warnings only). If there are compilation errors from unused UXAssist features in MyWindow.cs (e.g. references to MyFlatButton, MyComboBox, MySideSlider, MyCornerComboBox, MyCheckButton), remove or comment out those methods since we don't need them.

- [ ] **Step 6: Commit**

```bash
git add XianTuEnhanced/Common/ XianTuEnhanced/UI/
git commit -m "feat(XianTuEnhanced): copy UXAssist UI infrastructure with namespace changes"
```

---

### Task 3: Port Data Layer

**Files:**
- Create: `XianTuEnhanced/BlueTuUIData.cs` (from `../../DSP_MODS_TO/XianTu/BlueTuUIData.cs`)
- Create: `XianTuEnhanced/BlueprintBuildingExtensions.cs` (from `../../DSP_MODS_TO/XianTu/ToolScripts/_BlueprintBuildingExpands.cs`)

- [ ] **Step 1: Create BlueTuUIData.cs**

Copy from `../../DSP_MODS_TO/XianTu/BlueTuUIData.cs`. Change namespace from `XianTu` to `XianTuEnhanced`.

```csharp
using System;
using System.Diagnostics;
using UnityEngine;

namespace XianTuEnhanced;

public class BlueTuUIData
{
    [field: DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public event Action OnValueChange;

    public static BlueTuUIData Instance { get; } = new();

    public Vector3 Bias
    {
        get => _bias;
        set
        {
            _bias = value;
            OnValueChange?.Invoke();
        }
    }

    public Vector3 Scale
    {
        get => _scale;
        set
        {
            _scale = value;
            OnValueChange?.Invoke();
        }
    }

    public Vector3 Pivot
    {
        get => _pivot;
        set
        {
            _pivot = value;
            OnValueChange?.Invoke();
        }
    }

    public float LayerHeight
    {
        get => _layerHeight;
        set
        {
            _layerHeight = value;
            OnValueChange?.Invoke();
        }
    }

    public float Rotate
    {
        get => _rotate;
        set
        {
            _rotate = value;
            OnValueChange?.Invoke();
        }
    }

    public int LayerNumber
    {
        get => _layerNumber;
        set
        {
            if (LayerNumber >= 1)
            {
                _layerNumber = value;
                OnValueChange?.Invoke();
            }
        }
    }

    public bool Enable
    {
        get => _enable;
        set
        {
            _enable = value;
            OnValueChange?.Invoke();
        }
    }

    // RepeatCount intentionally does NOT fire OnValueChange.
    // It is only read at RepeatBuild button-click time, not for live preview.
    public int RepeatCount
    {
        get => _repeatCount;
        set => _repeatCount = Math.Max(1, value);
    }

    public BlueTuUIData Clone()
    {
        var clone = (BlueTuUIData)MemberwiseClone();
        clone.OnValueChange = null;
        return clone;
    }

    public void Reset()
    {
        var defaults = new BlueTuUIData();
        _bias = defaults._bias;
        _scale = defaults._scale;
        _pivot = defaults._pivot;
        _layerHeight = defaults._layerHeight;
        _layerNumber = defaults._layerNumber;
        _rotate = defaults._rotate;
        _repeatCount = defaults._repeatCount;
        _enable = true;
    }

    private Vector3 _bias = new(0f, 0f, 0f);
    private Vector3 _scale = new(1f, 1f, 1f);
    private Vector3 _pivot = new(0f, 0f, 0f);
    private float _layerHeight = 5f;
    private int _layerNumber = 1;
    private float _rotate;
    private bool _enable = true;
    private int _repeatCount = 1;

    public Action OnBuildBtn;
    public Action OnResetBtn;
    public Action OnCopyBtn;
    public Action OnRepeatBuildBtn;
}
```

- [ ] **Step 2: Create BlueprintBuildingExtensions.cs**

Port from `../../DSP_MODS_TO/XianTu/ToolScripts/_BlueprintBuildingExpands.cs`. Change namespace to `XianTuEnhanced`.

```csharp
using System.Collections.Generic;

namespace XianTuEnhanced;

public static class BlueprintBuildingExtensions
{
    static BlueprintBuildingExtensions()
    {
        BeltProtoDict.Add(2001, null);
        BeltProtoDict.Add(2002, null);
        BeltProtoDict.Add(2003, null);
        SoltProtoDict.Add(2011, null);
        SoltProtoDict.Add(2012, null);
        SoltProtoDict.Add(2013, null);
        SoltProtoDict.Add(2014, null);
    }

    public static bool IsBelt(this BlueprintBuilding bb)
    {
        return BeltProtoDict.ContainsKey(bb.itemId);
    }

    public static bool IsSlot(this BlueprintBuilding bb)
    {
        return SoltProtoDict.ContainsKey(bb.itemId);
    }

    private static readonly Dictionary<int, ItemProto> BeltProtoDict = new();
    private static readonly Dictionary<int, ItemProto> SoltProtoDict = new();
}
```

- [ ] **Step 3: Build to verify**

```bash
dotnet build XianTuEnhanced/XianTuEnhanced.csproj
```

Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add XianTuEnhanced/BlueTuUIData.cs XianTuEnhanced/BlueprintBuildingExtensions.cs
git commit -m "feat(XianTuEnhanced): port data model and extension methods from XianTu"
```

---

### Task 4: Port and Adapt BlueTuController

**Files:**
- Create: `XianTuEnhanced/BlueTuController.cs` (adapted from `../../DSP_MODS_TO/XianTu/Scripts/DataController/BlueTuController.cs`)

Key adaptations from original:
1. Namespace: `XianTu.Scripts.DataController` → `XianTuEnhanced`
2. Remove `using ToolScripts;` and `using XianTu.UI;`
3. Replace `BlueTuDatabase.Load("FoundationBlueTu")` with embedded resource loading
4. Replace `UIManager.Instance.CanvasMonoEvent.onEnableEvent.AddListener(OnReset)` with a public `InitWithWindow` method
5. Add `public void OnWindowOpen()` method for window open callback

- [ ] **Step 1: Create BlueTuController.cs**

```csharp
using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace XianTuEnhanced;

internal class BlueTuController
{
    public BlueTuController()
    {
        _oldData = BlueTuUIData.Instance.Clone();
        _data = BlueTuUIData.Instance;
        _data.OnValueChange += OnUserChangeData;
        _data.OnBuildBtn += OnUserBuildDetermine;
        _data.OnCopyBtn += OnUserCopy;
        _data.OnResetBtn += OnReset;
        _data.OnRepeatBuildBtn += OnUserRepeatBuild;
        _foundation = LoadFoundationBlueprint();
    }

    private static BlueprintBuilding LoadFoundationBlueprint()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("XianTuEnhanced.FoundationBlueTu.txt");
        if (stream == null)
            throw new InvalidOperationException("Embedded resource XianTuEnhanced.FoundationBlueTu.txt not found");
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd().Trim();
        var blueprintData = BlueprintData.CreateNew(text);
        return blueprintData.buildings[0];
    }

    /// <summary>
    /// Called when the window opens. Replaces the original UIManager.CanvasMonoEvent.onEnableEvent binding.
    /// </summary>
    public void OnWindowOpen()
    {
        OnReset();
    }

    private void OnUserCopy()
    {
        _actionBuild.blueprintClipboard = _bPaste.blueprint;
        ResetBuildDuiDie();
        _bPaste.RefreshBlueprintUI();
    }

    private void OnReset()
    {
        Player = GameMain.mainPlayer;
        if (Player == null)
        {
            _data.Enable = false;
            return;
        }

        _playerController = Player.controller;
        _actionBuild = PlayerController.actionBuild;
        if (_actionBuild == null)
        {
            _data.Enable = false;
            return;
        }

        _activeTool = _actionBuild.activeTool;
        if (_activeTool is BuildTool_BlueprintPaste buildToolBlueprintPaste)
        {
            _bPaste = buildToolBlueprintPaste;
            var mouseRay = _actionBuild.activeTool.mouseRay;
            _defaultMouseRay = new Ray(mouseRay.origin, mouseRay.direction);
            if (buildToolBlueprintPaste.blueprint != _blueprint)
            {
                _templateBlueTu = buildToolBlueprintPaste.blueprint.Clone();
                _blueprint = buildToolBlueprintPaste.blueprint;
            }
            _oldData = new BlueTuUIData();
            _data.Reset();
            _data.Enable = true;
            return;
        }

        _data.Enable = false;
    }

    private Player Player { get; set; }

    private void OnUserBuildDetermine()
    {
        if (_actionBuild.activeTool is BuildTool_BlueprintPaste buildToolBlueprintPaste)
        {
            if (buildToolBlueprintPaste.CheckBuildConditionsPrestage())
            {
                ResetBuildDuiDie();
                Build(buildToolBlueprintPaste);
            }
        }
    }

    private void OnUserRepeatBuild()
    {
        if (_actionBuild.activeTool is not BuildTool_BlueprintPaste buildToolBlueprintPaste) return;

        var currentBias = _data.Bias;
        var appliedBias = currentBias;

        ResetBuildDuiDie();

        for (var i = 1; i <= _data.RepeatCount; i++)
        {
            var targetBias = currentBias * i;
            var delta = targetBias - appliedBias;
            CtrlBiasData(delta);
            appliedBias = targetBias;
            Build(buildToolBlueprintPaste);
        }

        var restoreDelta = currentBias - appliedBias;
        CtrlBiasData(restoreDelta);
        _BuildTool_BluePrint_OnTick();
    }

    private void ResetBuildDuiDie()
    {
        var buildToolBlueprintPaste = _bPaste;
        BlueprintBuilding blueprintBuilding = null;
        foreach (var building in buildToolBlueprintPaste.blueprint.buildings)
        {
            if (Math.Abs(building.localOffset_z) < 1.5f)
            {
                var itemProto = LDB.items.Select(building.itemId);
                Debug.Log($"基底查验:{itemProto.name}.{itemProto.ID}:{building.localOffset_x:2f}, {building.localOffset_y:2f}, {building.localOffset_z:2f}");
                blueprintBuilding = building;
                break;
            }
        }

        if (blueprintBuilding == null)
        {
            Debug.Log("没有基底");
        }
        if (blueprintBuilding == null)
        {
            var buildings = _bPaste.blueprint.buildings;
            var array = new BlueprintBuilding[buildings.Length + 1];
            _bPaste.blueprint.buildings = array;
            buildings.CopyTo(array, 0);
            blueprintBuilding = _foundation;
            blueprintBuilding.index = buildings.Length;
            blueprintBuilding.localOffset_z = -0.5f;
            array[buildings.Length] = blueprintBuilding;
        }

        foreach (var building in _bPaste.blueprint.buildings)
        {
            if (building.IsBelt()) continue;
            if (building.IsSlot()) continue;
            if (building == blueprintBuilding) continue;
            building.inputToSlot = 14;
            building.outputFromSlot = 15;
            building.inputFromSlot = 15;
            building.outputToSlot = 14;
            building.inputObj = blueprintBuilding;
            building.inputFromSlot = -1;
        }

        _bPaste.bpCursor = _bPaste.blueprint.buildings.Length;
        _bPaste.buildPreviews.Clear();
        _bPaste.ResetStates();
        _BuildTool_BluePrint_OnTick();
    }

    private void Build(BuildTool_BlueprintPaste bp)
    {
        if (bp.CheckBuildConditionsPrestage())
        {
            PlayerController.cmd.stage = 1;
            bp.GenerateBlueprintGratBoxes();
            bp.DeterminePreviewsPrestage(true, false);
            bp.ActiveColliders(_actionBuild.model);
            var buildCondition = bp.CheckBuildConditions();
            bp.DeterminePreviews();
            bp.result = buildCondition
                ? (bp.result & ~EBlueprintPasteResult.HasError)
                : (bp.result | EBlueprintPasteResult.HasError);
            bp.DeactiveColliders(_actionBuild.model);
            bp.CalculateReformData();
            if (buildCondition && bp.quickPaste && (bp.result & EBlueprintPasteResult.HasReform) == EBlueprintPasteResult.None)
            {
                bp.CreatePrebuilds();
                bp.ResetStates();
            }
        }
        bp.isDragging = false;
        bp.startGroundPosSnapped = bp.castGroundPosSnapped;
        bp.ErrorGridClustering();
        _BuildTool_BluePrint_OnTick();
    }

    private void OnUserChangeData()
    {
        if (!_data.Enable) return;
        if (PlayerController == null) return;
        if (_actionBuild == null) return;
        if (_bPaste == null) return;

        var biasDelta = _data.Bias - _oldData.Bias;
        var layerHeightDelta = _data.LayerHeight - _oldData.LayerHeight;
        var layerNumberDelta = _data.LayerNumber - _oldData.LayerNumber;
        var rotateDelta = _data.Rotate - _oldData.Rotate;

        CtrlLayerNumber(layerNumberDelta);
        CtrlLayerHeight(layerHeightDelta);
        CtrlRotate(rotateDelta);
        CtrlBiasData(biasDelta);
        CtrlScale();
        _BuildTool_BluePrint_OnTick();

        _oldData.Scale = _data.Scale;
        _oldData.Pivot = _data.Pivot;
        _oldData.LayerHeight = _data.LayerHeight;
        _oldData.LayerNumber = _data.LayerNumber;
        _oldData.Rotate = _data.Rotate;
        _oldData.Bias = _data.Bias;
    }

    private void CtrlLayerNumber(int bLayerNumber)
    {
        if (bLayerNumber == 0) return;

        var num = _data.LayerNumber * _templateBlueTu.buildings.Length;
        var buildings = _bPaste.blueprint.buildings;
        var array = new BlueprintBuilding[num];

        if (buildings.Length > num)
        {
            Array.Copy(buildings, array, num);
        }
        else
        {
            buildings.CopyTo(array, 0);
            var templateLen = _templateBlueTu.buildings.Length;
            for (var i = _bPaste.bpCursor; i < array.Length; i += templateLen)
            {
                _templateBlueTu.Clone().buildings.CopyTo(array, i);
            }
            for (var j = _bPaste.bpCursor; j < array.Length; j++)
            {
                array[j].localOffset_z = array[j - templateLen].localOffset_z + _data.LayerHeight;
                array[j].localOffset_z2 = array[j - templateLen].localOffset_z2 + _data.LayerHeight;
            }
            for (var k = _bPaste.bpCursor; k < array.Length; k++)
            {
                array[k].index = k;
            }
        }

        _bPaste.blueprint.buildings = array;
        _bPaste.bpCursor = _bPaste.blueprint.buildings.Length;
        _bPaste.buildPreviews.Clear();
        _bPaste.ResetStates();
    }

    private void CtrlLayerHeight(float bLayerHeight)
    {
        if (bLayerHeight == 0f) return;

        var templateLen = _templateBlueTu.buildings.Length;
        var layerIndex = 0;
        var buildings = _bPaste.blueprint.buildings;
        for (var i = 0; i < _bPaste.bpCursor; i++)
        {
            if (i == templateLen * (layerIndex + 1))
            {
                layerIndex++;
            }
            buildings[i].localOffset_z += bLayerHeight * layerIndex;
            buildings[i].localOffset_z2 += bLayerHeight * layerIndex;
        }
    }

    private void CtrlScale()
    {
        if (_oldData.Scale == _data.Scale) return;

        var buildings = _templateBlueTu.buildings;
        var scaleDelta = _data.Scale - _oldData.Scale;
        var pivot = _data.Pivot;
        var templateIndex = 0;
        for (var i = 0; i < _bPaste.bpCursor; i++)
        {
            if (templateIndex == buildings.Length) templateIndex = 0;

            var templateBuilding = buildings[templateIndex];
            var currentBuilding = _bPaste.blueprint.buildings[i];
            currentBuilding.localOffset_x += (templateBuilding.localOffset_x - pivot.x) * scaleDelta.x + pivot.x;
            currentBuilding.localOffset_y += (templateBuilding.localOffset_y - pivot.y) * scaleDelta.y + pivot.y;
            currentBuilding.localOffset_z += templateBuilding.localOffset_z * scaleDelta.z;
            currentBuilding.localOffset_x2 += (templateBuilding.localOffset_x2 - pivot.x) * scaleDelta.x + pivot.x;
            currentBuilding.localOffset_y2 += (templateBuilding.localOffset_y2 - pivot.y) * scaleDelta.y + pivot.y;
            currentBuilding.localOffset_z2 += templateBuilding.localOffset_z2 * scaleDelta.z;
            templateIndex++;
        }
    }

    private void CtrlRotate(float bRotate)
    {
        if (Math.Abs(bRotate) < 0.001f) return;

        if (_actionBuild.activeTool is BuildTool_BlueprintPaste)
        {
            var pivot = _data.Pivot;
            var buildings = _bPaste.blueprint.buildings;
            var quaternion = Quaternion.AngleAxis(bRotate, Vector3.forward);
            for (var i = 0; i < _bPaste.bpCursor; i++)
            {
                var building = buildings[i];
                var offset = new Vector3(building.localOffset_x - pivot.x, building.localOffset_y - pivot.y, 0f);
                offset = quaternion * offset;
                building.localOffset_x = offset.x + pivot.x;
                building.localOffset_y = offset.y + pivot.y;
                building.yaw -= bRotate;
                offset = new Vector3(building.localOffset_x2 - pivot.x, building.localOffset_y2 - pivot.y, 0f);
                offset = quaternion * offset;
                building.localOffset_x2 = offset.x + pivot.x;
                building.localOffset_y2 = offset.y + pivot.y;
                building.yaw2 -= bRotate;
            }
        }
    }

    private PlayerController PlayerController => _playerController;

    private void CtrlBiasData(Vector3 bBias)
    {
        if (bBias == Vector3.zero) return;

        if (_actionBuild.activeTool is BuildTool_BlueprintPaste buildToolBlueprintPaste)
        {
            for (var i = 0; i < buildToolBlueprintPaste.bpCursor; i++)
            {
                var building = buildToolBlueprintPaste.blueprint.buildings[i];
                building.localOffset_x += bBias.x;
                building.localOffset_x2 += bBias.x;
                building.localOffset_y += bBias.y;
                building.localOffset_y2 += bBias.y;
                building.localOffset_z += bBias.z;
                building.localOffset_z2 += bBias.z;
            }
        }
    }

    private void _BuildTool_BluePrint_OnTick()
    {
        VFInput.onGUI = false;
        _bPaste.mouseRay = _defaultMouseRay;
        if (_activeTool is BuildTool_BlueprintPaste buildToolBlueprintPaste)
        {
            buildToolBlueprintPaste.ClearErrorMessage();
            buildToolBlueprintPaste.UpdateRaycast();
            buildToolBlueprintPaste.CheckBuildConditionsPrestage();
            switch (PlayerController.cmd.stage)
            {
                case 0:
                    buildToolBlueprintPaste.OperatingPrestage();
                    break;
                case 1:
                    buildToolBlueprintPaste.Operating();
                    break;
            }
            buildToolBlueprintPaste.UpdatePreviewModels(_actionBuild.model);
            buildToolBlueprintPaste.TranslateErrorType(false);
        }
    }

    private readonly BlueTuUIData _data;
    private BlueTuUIData _oldData;
    private PlayerController _playerController;
    private Ray _defaultMouseRay;
    private BlueprintData _templateBlueTu;
    private PlayerAction_Build _actionBuild;
    private BuildTool _activeTool;
    private BuildTool_BlueprintPaste _bPaste;
    private readonly BlueprintBuilding _foundation;
    private BlueprintData _blueprint;
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build XianTuEnhanced/XianTuEnhanced.csproj
```

Expected: Build succeeds. The `BlueprintData.CreateNew()` and `FromBase64String()` methods are from the game's Assembly-CSharp. If `CreateNew()` doesn't exist, try `new BlueprintData()` directly — check the game API.

- [ ] **Step 3: Commit**

```bash
git add XianTuEnhanced/BlueTuController.cs
git commit -m "feat(XianTuEnhanced): port and adapt BlueTuController with embedded resource loading"
```

---

### Task 5: Create XianTuEnhancedWindow

**Files:**
- Create: `XianTuEnhanced/XianTuEnhancedWindow.cs`

This is the main new code — the programmatic UI window.

- [ ] **Step 1: Create XianTuEnhancedWindow.cs**

```csharp
using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using XianTuEnhanced.UI;

namespace XianTuEnhanced;

public class XianTuEnhancedWindow : MyWindow
{
    private readonly BlueTuUIData _data = BlueTuUIData.Instance;
    private MyCheckBox _enableCheckBox;

    // Single-value input fields
    private InputField _layerNumberInput;
    private InputField _layerHeightInput;
    private InputField _rotateInput;
    private InputField _repeatCountInput;

    // Multi-value input fields
    private InputField _scaleXInput;
    private InputField _scaleYInput;
    private InputField _scaleZInput;
    private InputField _biasXInput;
    private InputField _biasYInput;
    private InputField _biasZInput;
    private InputField _pivotXInput;
    private InputField _pivotYInput;

    // Layout constants
    private const float LabelX = 10f;
    private const float InputX = 80f;
    private const float RowHeight = 36f;
    private const float TripleInputWidth = 60f;
    private const float DoubleInputWidth = 80f;
    private const float SubLabelWidth = 16f;
    private const float TripleSpacing = 80f;  // space between each X/Y/Z group
    private const float DoubleSpacing = 100f; // space between each X/Y group
    private const float WindowWidth = 400f;   // fixed width (bypasses _maxX which is private)

    public override bool _OnInit()
    {
        if (!base._OnInit()) return false;

        var parent = GetComponent<RectTransform>();
        float y = 10f;

        // Enable checkbox
        _enableCheckBox = MyCheckBox.CreateCheckBox(LabelX, y, parent, _data.Enable, "启用", 15);
        _enableCheckBox.OnChecked += () => _data.Enable = _enableCheckBox.Checked;
        MaxY = Mathf.Max(MaxY, y + _enableCheckBox.Height);
        y += RowHeight;

        // Layer Number (int)
        AddText2(LabelX, y, parent, "层数");
        _layerNumberInput = AddInputField(InputX, y, parent, _data.LayerNumber.ToString(), 14, "layer-number-input",
            onEditEnd: val => { if (int.TryParse(val, out var v)) _data.LayerNumber = Mathf.Max(1, v); else RefreshInputFields(); });
        y += RowHeight;

        // Layer Height (float)
        AddText2(LabelX, y, parent, "层高");
        _layerHeightInput = AddInputField(InputX, y, parent, _data.LayerHeight.ToString(CultureInfo.InvariantCulture), 14, "layer-height-input",
            onEditEnd: val => { if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) _data.LayerHeight = v; else RefreshInputFields(); });
        y += RowHeight;

        // Scale X/Y/Z
        AddText2(LabelX, y, parent, "缩放");
        CreateTripleInputRow(InputX, y, parent, "scale",
            _data.Scale.x, _data.Scale.y, _data.Scale.z,
            out _scaleXInput, out _scaleYInput, out _scaleZInput,
            (vx, vy, vz) => _data.Scale = new Vector3(vx, vy, vz));
        y += RowHeight;

        // Bias X/Y/Z
        AddText2(LabelX, y, parent, "偏移");
        CreateTripleInputRow(InputX, y, parent, "bias",
            _data.Bias.x, _data.Bias.y, _data.Bias.z,
            out _biasXInput, out _biasYInput, out _biasZInput,
            (vx, vy, vz) => _data.Bias = new Vector3(vx, vy, vz));
        y += RowHeight;

        // Pivot X/Y (Pivot Z is always 0, not exposed)
        AddText2(LabelX, y, parent, "中心点");
        CreateDoubleInputRow(InputX, y, parent, "pivot",
            _data.Pivot.x, _data.Pivot.y,
            out _pivotXInput, out _pivotYInput,
            (vx, vy) => _data.Pivot = new Vector3(vx, vy, 0f));
        y += RowHeight;

        // Rotation (float)
        AddText2(LabelX, y, parent, "旋转");
        _rotateInput = AddInputField(InputX, y, parent, _data.Rotate.ToString(CultureInfo.InvariantCulture), 14, "rotate-input",
            onEditEnd: val => { if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) _data.Rotate = v; else RefreshInputFields(); });
        y += RowHeight;

        // Repeat Count (int)
        AddText2(LabelX, y, parent, "重复次数");
        _repeatCountInput = AddInputField(InputX, y, parent, _data.RepeatCount.ToString(), 14, "repeat-count-input",
            onEditEnd: val => { if (int.TryParse(val, out var v)) _data.RepeatCount = Mathf.Max(1, v); else RefreshInputFields(); });
        y += RowHeight + 5f;

        // Buttons row
        var btnWidth = 80f;
        var btnSpacing = 8f;
        var btnX = LabelX;
        AddButton(btnX, y, btnWidth, parent, "建造", 14, "btn-build", () => _data.OnBuildBtn?.Invoke());
        btnX += btnWidth + btnSpacing;
        AddButton(btnX, y, btnWidth, parent, "重复建造", 14, "btn-repeat-build", () => _data.OnRepeatBuildBtn?.Invoke());
        btnX += btnWidth + btnSpacing;
        AddButton(btnX, y, btnWidth, parent, "复制", 14, "btn-copy", () => _data.OnCopyBtn?.Invoke());
        btnX += btnWidth + btnSpacing;
        AddButton(btnX, y, btnWidth, parent, "重置", 14, "btn-reset", () =>
        {
            _data.OnResetBtn?.Invoke();
            RefreshInputFields();
        });

        return true;
    }

    public override void _OnOpen()
    {
        // Use fixed width since _maxX is private and multi-field rows don't track it.
        // MaxY is protected and tracked by AddText2/AddButton calls.
        var trans = GetComponent<RectTransform>();
        trans.sizeDelta = new Vector2(WindowWidth, MaxY + TitleHeight + Margin);
        RefreshInputFields();
    }

    public override bool IsWindowFunctional() => false; // Don't close on ShutAllFunctionWindow

    public void RefreshInputFields()
    {
        _enableCheckBox.Checked = _data.Enable;
        _layerNumberInput.text = _data.LayerNumber.ToString();
        _layerHeightInput.text = _data.LayerHeight.ToString(CultureInfo.InvariantCulture);
        _scaleXInput.text = _data.Scale.x.ToString(CultureInfo.InvariantCulture);
        _scaleYInput.text = _data.Scale.y.ToString(CultureInfo.InvariantCulture);
        _scaleZInput.text = _data.Scale.z.ToString(CultureInfo.InvariantCulture);
        _biasXInput.text = _data.Bias.x.ToString(CultureInfo.InvariantCulture);
        _biasYInput.text = _data.Bias.y.ToString(CultureInfo.InvariantCulture);
        _biasZInput.text = _data.Bias.z.ToString(CultureInfo.InvariantCulture);
        _pivotXInput.text = _data.Pivot.x.ToString(CultureInfo.InvariantCulture);
        _pivotYInput.text = _data.Pivot.y.ToString(CultureInfo.InvariantCulture);
        _rotateInput.text = _data.Rotate.ToString(CultureInfo.InvariantCulture);
        _repeatCountInput.text = _data.RepeatCount.ToString();
    }

    private void CreateTripleInputRow(float x, float y, RectTransform parent, string prefix,
        float valX, float valY, float valZ,
        out InputField inputX, out InputField inputY, out InputField inputZ,
        Action<float, float, float> onChanged)
    {
        var currentX = x;

        AddText(currentX, y, parent, "X", 12, $"{prefix}-label-x");
        currentX += SubLabelWidth;
        inputX = CreateNarrowInputField(currentX, y, TripleInputWidth, parent, valX.ToString(CultureInfo.InvariantCulture), 14, $"{prefix}-x-input");
        currentX += TripleSpacing;

        AddText(currentX, y, parent, "Y", 12, $"{prefix}-label-y");
        currentX += SubLabelWidth;
        inputY = CreateNarrowInputField(currentX, y, TripleInputWidth, parent, valY.ToString(CultureInfo.InvariantCulture), 14, $"{prefix}-y-input");
        currentX += TripleSpacing;

        AddText(currentX, y, parent, "Z", 12, $"{prefix}-label-z");
        currentX += SubLabelWidth;
        inputZ = CreateNarrowInputField(currentX, y, TripleInputWidth, parent, valZ.ToString(CultureInfo.InvariantCulture), 14, $"{prefix}-z-input");

        // Track MaxY for this row
        MaxY = Mathf.Max(MaxY, y + RowHeight);

        var ix = inputX;
        var iy = inputY;
        var iz = inputZ;

        void OnEdit(string _)
        {
            if (float.TryParse(ix.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var vx) &&
                float.TryParse(iy.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var vy) &&
                float.TryParse(iz.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var vz))
            {
                onChanged(vx, vy, vz);
            }
            else
            {
                RefreshInputFields();
            }
        }

        inputX.onEndEdit.AddListener(OnEdit);
        inputY.onEndEdit.AddListener(OnEdit);
        inputZ.onEndEdit.AddListener(OnEdit);
    }

    private void CreateDoubleInputRow(float x, float y, RectTransform parent, string prefix,
        float valX, float valY,
        out InputField inputX, out InputField inputY,
        Action<float, float> onChanged)
    {
        var currentX = x;

        AddText(currentX, y, parent, "X", 12, $"{prefix}-label-x");
        currentX += SubLabelWidth;
        inputX = CreateNarrowInputField(currentX, y, DoubleInputWidth, parent, valX.ToString(CultureInfo.InvariantCulture), 14, $"{prefix}-x-input");
        currentX += DoubleSpacing;

        AddText(currentX, y, parent, "Y", 12, $"{prefix}-label-y");
        currentX += SubLabelWidth;
        inputY = CreateNarrowInputField(currentX, y, DoubleInputWidth, parent, valY.ToString(CultureInfo.InvariantCulture), 14, $"{prefix}-y-input");

        MaxY = Mathf.Max(MaxY, y + RowHeight);

        var ix = inputX;
        var iy = inputY;

        void OnEdit(string _)
        {
            if (float.TryParse(ix.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var vx) &&
                float.TryParse(iy.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var vy))
            {
                onChanged(vx, vy);
            }
            else
            {
                RefreshInputFields();
            }
        }

        inputX.onEndEdit.AddListener(OnEdit);
        inputY.onEndEdit.AddListener(OnEdit);
    }

    /// <summary>
    /// Creates an input field with custom width. Does not track _maxX (we use fixed WindowWidth instead).
    /// </summary>
    private static InputField CreateNarrowInputField(float x, float y, float width, RectTransform parent, string text, int fontSize, string objName)
    {
        var stationWindow = UIRoot.instance.uiGame.stationWindow;
        var inputField = Instantiate(stationWindow.nameInput);
        inputField.gameObject.name = objName;
        Destroy(inputField.GetComponent<UIButton>());
        inputField.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f);
        var rect = Util.NormalizeRectWithTopLeft(inputField, x, y, parent);
        rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
        inputField.text = text;
        inputField.textComponent.fontSize = fontSize;
        inputField.onValueChanged.RemoveAllListeners();
        inputField.onEndEdit.RemoveAllListeners();
        return inputField;
    }
}
```

**Important implementation notes:**
- `_OnInit()` returns `bool` (matching `ManualBehaviour` base class), not `void`. Must return `true` on success.
- `_OnOpen()` uses a fixed `WindowWidth` instead of `AutoFitWindowSize()` because `_maxX` is private in the base class and multi-field rows (Scale/Bias/Pivot) use `CreateNarrowInputField` which doesn't track it. `MaxY` is protected and tracked normally.
- Single-value inputs (层数, 层高, 旋转, 重复次数) use the base class `AddInputField` which tracks `_maxX`/`MaxY`. Multi-value inputs use `CreateNarrowInputField` (static, no tracking).
- `IsWindowFunctional()` returns `false` so the window is NOT closed when `UIGame.ShutAllFunctionWindow` is called (close via X button or F2).
- `RefreshInputFields()` is public so the plugin can call it after controller reset.

- [ ] **Step 2: Build to verify**

```bash
dotnet build XianTuEnhanced/XianTuEnhanced.csproj
```

Expected: Build succeeds. Watch for:
- `AddInputField` signature conflicts with base class — the private overload may need renaming if it collides. The base class has `AddInputField(float x, float y, RectTransform parent, ...)` and `AddInputField(float x, float y, float width, RectTransform parent, ConfigEntry<string>, ...)`. Our private method has different params so it should be fine as a new overload.
- `_OnInit()` — verify this is the correct override. If it's `_Init(object data)` instead, change to `override void _Init(object data)` and call `base._Init(data)`.

- [ ] **Step 3: Commit**

```bash
git add XianTuEnhanced/XianTuEnhancedWindow.cs
git commit -m "feat(XianTuEnhanced): create programmatic UI window with all controls"
```

---

### Task 6: Create Plugin Entry Point

**Files:**
- Create: `XianTuEnhanced/XianTuEnhancedPlugin.cs`

- [ ] **Step 1: Create XianTuEnhancedPlugin.cs**

```csharp
using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using XianTuEnhanced.UI;

namespace XianTuEnhanced;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class XianTuEnhancedPlugin : BaseUnityPlugin
{
    private static ManualLogSource _logger;
    private XianTuEnhancedWindow _window;
    private BlueTuController _controller;

    private void Start()
    {
        _logger = Logger;

        MyWindowManager.InitBaseObjects();
        MyWindowManager.Enable(true);
        _window = MyWindowManager.CreateWindow<XianTuEnhancedWindow>("xiantu-enhanced", "仙图增强");
        _controller = new BlueTuController();

        _logger.LogInfo("XianTuEnhanced loaded.");
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F2) && !VFInput.inputing)
        {
            if (_window.active)
            {
                _window.Close();
            }
            else
            {
                _controller.OnWindowOpen();
                _window.Open();
            }
        }
    }

    private void OnDestroy()
    {
        MyWindowManager.Enable(false);
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build XianTuEnhanced/XianTuEnhanced.csproj
```

Expected: Build succeeds with no errors.

- [ ] **Step 3: Commit**

```bash
git add XianTuEnhanced/XianTuEnhancedPlugin.cs
git commit -m "feat(XianTuEnhanced): create plugin entry point with F2 hotkey toggle"
```

---

### Task 7: Full Build and Fix

- [ ] **Step 1: Clean build**

```bash
cd C:/Users/Yi/Applications/Games/dsp/code/DSP_Mods_fyyy
dotnet build XianTuEnhanced/XianTuEnhanced.csproj --no-incremental
```

- [ ] **Step 2: Fix any compilation errors**

Common issues to watch for:
1. **MyWindow.cs leftover references** — If Task 2 cleanup missed any references to removed component types, fix them here.
2. **`ManualBehaviour` API differences** — If the game version's `ManualBehaviour._OnInit()` has a different signature than expected, adapt accordingly. The plan assumes `public virtual bool _OnInit()`.
3. **`BlueprintData.CreateNew(string)`** — The plan uses `BlueprintData.CreateNew(text)`. If this overload doesn't exist in your game version, try `BlueprintData.CreateFromBPFile(text)` or check Assembly-CSharp for the correct method name.
4. **Missing `Translate()` extension** — The `.Translate()` calls in copied UXAssist code are a DSP game built-in string extension for localization. If it causes errors, it may need a `using` directive for the game's localization namespace.

- [ ] **Step 3: Rebuild after fixes**

```bash
dotnet build XianTuEnhanced/XianTuEnhanced.csproj --no-incremental
```

Expected: BUILD SUCCEEDED.

- [ ] **Step 4: Commit fixes if any**

```bash
git add XianTuEnhanced/
git commit -m "fix(XianTuEnhanced): fix compilation errors from build verification"
```

---

### Task 8: Thunderstore Packaging

**Files:**
- Create: `XianTuEnhanced/package/manifest.json`

- [ ] **Step 1: Create manifest.json**

```json
{
  "name": "XianTuEnhanced",
  "version_number": "1.0.0",
  "website_url": "",
  "description": "仙图增强 - 蓝图操作工具，支持层叠、缩放、偏移、旋转、重复建造",
  "dependencies": [
    "xiaoye97-BepInEx-5.4.17"
  ]
}
```

- [ ] **Step 2: Commit**

```bash
git add XianTuEnhanced/package/manifest.json
git commit -m "feat(XianTuEnhanced): add Thunderstore package manifest"
```

---

### Task 9: Final Verification

- [ ] **Step 1: Release build**

```bash
dotnet build XianTuEnhanced/XianTuEnhanced.csproj -c Release
```

Expected: Build succeeds and produces:
- `XianTuEnhanced/bin/Release/net472/XianTuEnhanced.dll`
- (ZIP packaging will fail without icon.png — that's fine for now, the DLL is the deliverable)

- [ ] **Step 2: Verify DLL output exists**

```bash
ls -la XianTuEnhanced/bin/Release/net472/XianTuEnhanced.dll
```

- [ ] **Step 3: Final commit**

```bash
git add -A XianTuEnhanced/
git commit -m "feat(XianTuEnhanced): complete initial implementation"
```
