# DashboardOverhaul 仪表盘分页 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给 DSP 仪表盘补上原版未发布的多页前端——顶部标签栏，支持 切换/新建/删除/重命名 页面。

**Architecture:** 独立 BepInEx 插件，用 Harmony 把一条标签栏注入 `UIDashboard`，由纯逻辑层 `PageOps` 操作现成的 `CustomCharts.dashboardLayout`（原版 9 页结构），右键菜单复用游戏 `UIPopupMenu`。不新增存档数据。

**Tech Stack:** C# / net472 / BepInEx 5 / HarmonyX / Unity UGUI / UXAssist（I18N）。

## Global Constraints

- 目标框架 `net472`；BepInEx 5.*；依赖 UXAssist（`[BepInDependency(UXAssist.PluginInfo.PLUGIN_GUID)]`）。
- 插件标识：GUID `org.fyyy.dashboardoverhaul`，AssemblyName/Name `DashboardOverhaul`，Version `1.0.0`。
- **不改存档格式**：页面与页名一律用 `GameMain.data.statistics.charts.dashboardLayout`，跟随原版存档持久化。
- 页索引域 **1..9**（`DashboardLayout.MAX_PAGE_COUNT == 10`，第 0 槽闲置、永不使用）。
- **删除 = 把 `pages[i]` 置空，不移位**（页号可不连续）。
- 用户可见文案一律走 UXAssist `I18N.Add(key, en, zh)` + `I18N.Apply()`，**中英双语**。
- 配置：仅 `Enabled`（bool，默认 true）；`Enabled==false` 时所有 Harmony 逻辑直接放行原版、不建标签栏。
- **验证循环（本仓库无单元测试框架，注入式 Unity UI 不适用传统 TDD）**：每个任务结尾先 `dotnet build`（必须成功），里程碑任务再做游戏内手动核对；每任务一次 commit。
- 构建：`dotnet build "DashboardOverhaul/DashboardOverhaul.csproj" -c Debug`，产物 `DashboardOverhaul/bin/Debug/net472/DashboardOverhaul.dll`。
- 游戏与插件目录（按实际安装调整）：游戏根 `C:\Program Files (x86)\Applications\Steam\steamapps\common\Dyson Sphere Program`，插件目录 `<游戏根>\BepInEx\plugins\DashboardOverhaul\`。把上面产物 DLL 复制到该目录后启动游戏。
- commit 信息**不要**加 Claude co-author 或 Claude-Session 尾注（用户偏好）。

## File Structure

- `DashboardOverhaul/DashboardOverhaul.csproj` — 工程文件（镜像 InterstellarLogisticsOpt）。
- `DashboardOverhaul/DashboardOverhaulPlugin.cs` — 插件入口：配置、I18N、Harmony 引导、静态访问器。
- `DashboardOverhaul/PageOps.cs` — 纯逻辑：对 `DashboardLayout` 的增/删/改/切规则（无 Unity UI）。
- `DashboardOverhaul/UIDashboardPatch.cs` — Harmony 补丁：在 `UIDashboard` 生命周期挂点建栏/刷新/清理。
- `DashboardOverhaul/PageTabBar.cs` — 控制器：构建并持有标签栏 GameObject，重建标签、转发操作、重命名输入、右键菜单。
- `DashboardOverhaul/PageTab.cs` — 单标签视图（MonoBehaviour，处理左键/双击/右键）。
- `DashboardOverhaul/package/manifest.json`、`package/README.md`、`package/icon.png` — Thunderstore 打包资源。

---

### Task 1: 工程脚手架 + 可加载的空插件

**Files:**
- Create: `DashboardOverhaul/DashboardOverhaul.csproj`
- Create: `DashboardOverhaul/DashboardOverhaulPlugin.cs`

**Interfaces:**
- Produces: `DashboardOverhaulPlugin.ModEnabled` (`ConfigEntry<bool>`)、`DashboardOverhaulPlugin.Logger` (`ManualLogSource`)。

- [ ] **Step 1: 写工程文件**

`DashboardOverhaul/DashboardOverhaul.csproj`：

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <AssemblyName>DashboardOverhaul</AssemblyName>
    <BepInExPluginGuid>org.fyyy.dashboardoverhaul</BepInExPluginGuid>
    <Description>Completes the Dashboard's multi-page UI: switch/add/delete/rename pages / 补全仪表盘多页界面：切换/新建/删除/重命名</Description>
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
    <ProjectReference Include="..\..\DSP_Mods\UXAssist\UXAssist.csproj" />
  </ItemGroup>

  <ItemGroup Condition="'$(TargetFramework.TrimEnd(`0123456789`))' == 'net'">
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />
  </ItemGroup>

  <Target Name="PostBuild" AfterTargets="PostBuildEvent" Condition="'$(Configuration)' == 'Release'">
    <Exec Command="del /F /Q package\$(ProjectName)-$(Version).zip
powershell Compress-Archive -Force -DestinationPath 'package/$(ProjectName)-$(Version).zip' -Path &quot;$(TargetPath)&quot;, package/icon.png, package/manifest.json, package/README.md" />
  </Target>

</Project>
```

- [ ] **Step 2: 写最小插件**

`DashboardOverhaul/DashboardOverhaulPlugin.cs`：

```csharp
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace DashboardOverhaul;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
[BepInDependency(UXAssist.PluginInfo.PLUGIN_GUID)]
public class DashboardOverhaulPlugin : BaseUnityPlugin
{
    public new static readonly BepInEx.Logging.ManualLogSource Logger =
        BepInEx.Logging.Logger.CreateLogSource(PluginInfo.PLUGIN_NAME);

    public static ConfigEntry<bool> ModEnabled;

    private Harmony _harmony;

    private void Awake()
    {
        ModEnabled = Config.Bind("General", "Enabled", true,
            "Enable the Dashboard paging UI / 启用仪表盘分页界面");

        _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
        _harmony.PatchAll(typeof(UIDashboardPatch));

        Logger.LogInfo("DashboardOverhaul loaded.");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
```

- [ ] **Step 3: 写一个临时空补丁类让工程能编译**

`DashboardOverhaul/UIDashboardPatch.cs`（本任务先放空壳，Task 3 再填实现）：

```csharp
using HarmonyLib;

namespace DashboardOverhaul;

public static class UIDashboardPatch
{
    // 占位：本任务仅保证编译与加载；真正的补丁在 Task 3 加入。
}
```

- [ ] **Step 4: 构建，确认成功**

Run: `dotnet build "DashboardOverhaul/DashboardOverhaul.csproj" -c Debug`
Expected: `Build succeeded`，产出 `DashboardOverhaul/bin/Debug/net472/DashboardOverhaul.dll`。

- [ ] **Step 5: 游戏内确认加载（里程碑）**

把 DLL 复制到 `<游戏根>\BepInEx\plugins\DashboardOverhaul\`，启动游戏，查看 `<游戏根>\BepInEx\LogOutput.log`。
Expected: 出现 `DashboardOverhaul loaded.`，无异常。

- [ ] **Step 6: 提交**

```bash
git add DashboardOverhaul/DashboardOverhaul.csproj DashboardOverhaul/DashboardOverhaulPlugin.cs DashboardOverhaul/UIDashboardPatch.cs
git commit -m "feat(DashboardOverhaul): scaffold loadable plugin"
```

---

### Task 2: `PageOps` 纯逻辑层

**Files:**
- Create: `DashboardOverhaul/PageOps.cs`

**Interfaces:**
- Consumes: `CustomCharts`（`.dashboardLayout.pages`、`.currentView.pageIndex`）、`DashboardLayout`（`.pages`、`AddPage(int)`、`MAX_PAGE_COUNT`）、`DashboardPage`（`.name`、`.chartDatas`、`.Free()`、`.RemoveChartAt(int)`）。
- Produces:
  - `int PageOps.ActivePageCount(CustomCharts charts)`
  - `int PageOps.FirstFreeSlot(DashboardLayout layout)` // 返回 1..9 的最小空槽，满则 -1
  - `int PageOps.AddPage(CustomCharts charts)` // 占用最小空槽并返回槽号，满则 -1
  - `bool PageOps.CanDelete(CustomCharts charts)` // 现有非空页 > 1
  - `int PageOps.PickPageAfterDelete(DashboardLayout layout, int deletedIndex)` // 删后跳向的槽号；找不到返回 -1
  - `bool PageOps.RemovePage(CustomCharts charts, int index)` // 释放并置空该槽；返回是否成功
  - `void PageOps.RenamePage(DashboardPage page, string newName)` // 写 page.name（trim）

- [ ] **Step 1: 写 `PageOps`**

`DashboardOverhaul/PageOps.cs`：

```csharp
namespace DashboardOverhaul;

/// <summary>
/// 仪表盘分页的纯逻辑层：只操作数据结构，不触碰任何 Unity UI。
/// 页索引域 1..9（pages[0] 永不使用）。删除采用"置空槽、不移位"。
/// </summary>
public static class PageOps
{
    public static int ActivePageCount(CustomCharts charts)
    {
        int count = 0;
        var pages = charts.dashboardLayout.pages;
        for (int i = 1; i < DashboardLayout.MAX_PAGE_COUNT; i++)
            if (pages[i] != null) count++;
        return count;
    }

    public static int FirstFreeSlot(DashboardLayout layout)
    {
        var pages = layout.pages;
        for (int i = 1; i < DashboardLayout.MAX_PAGE_COUNT; i++)
            if (pages[i] == null) return i;
        return -1;
    }

    /// <summary>占用最小空槽并初始化一页；返回新页槽号，满则 -1。</summary>
    public static int AddPage(CustomCharts charts)
    {
        var layout = charts.dashboardLayout;
        int slot = FirstFreeSlot(layout);
        if (slot < 0) return -1;
        layout.AddPage(slot); // 原版 AddPage：new DashboardPage().Init()，name = slot.ToString()
        return slot;
    }

    public static bool CanDelete(CustomCharts charts) => ActivePageCount(charts) > 1;

    /// <summary>删 deletedIndex 后应跳向的槽：先向小页号找，再向大页号找；都没有返回 -1。</summary>
    public static int PickPageAfterDelete(DashboardLayout layout, int deletedIndex)
    {
        var pages = layout.pages;
        for (int i = deletedIndex - 1; i >= 1; i--)
            if (pages[i] != null) return i;
        for (int i = deletedIndex + 1; i < DashboardLayout.MAX_PAGE_COUNT; i++)
            if (pages[i] != null) return i;
        return -1;
    }

    /// <summary>释放该页所有图表并置空槽位。不负责切页（调用方处理 currentView 与刷新）。</summary>
    public static bool RemovePage(CustomCharts charts, int index)
    {
        if (index < 1 || index >= DashboardLayout.MAX_PAGE_COUNT) return false;
        var pages = charts.dashboardLayout.pages;
        var page = pages[index];
        if (page == null) return false;
        // 逐个释放图表（DashboardPage.Free 会清空 chartDatas）
        page.Free();
        pages[index] = null;
        return true;
    }

    public static void RenamePage(DashboardPage page, string newName)
    {
        if (page == null) return;
        page.name = (newName ?? string.Empty).Trim();
    }
}
```

- [ ] **Step 2: 构建，确认成功**

Run: `dotnet build "DashboardOverhaul/DashboardOverhaul.csproj" -c Debug`
Expected: `Build succeeded`。

- [ ] **Step 3: 逻辑自检（离线推演，无测试框架）**

逐条核对（在代码注释或 commit message 不必体现，仅实现者自检）：
- `FirstFreeSlot`：pages[1] 非空、pages[2] 空 → 返回 2；全满返回 -1。✓
- `AddPage`：满槽返回 -1（不调用 `layout.AddPage`）。✓
- `CanDelete`：仅 1 页时返回 false。✓
- `PickPageAfterDelete`：删 3（存在 1,2,4）→ 返回 2；删 1（存在 1,4）→ 返回 4。✓
- `RemovePage`：越界 / 空槽返回 false，不抛异常。✓

- [ ] **Step 4: 提交**

```bash
git add DashboardOverhaul/PageOps.cs
git commit -m "feat(DashboardOverhaul): add PageOps pure paging logic"
```

---

### Task 3: 标签栏渲染 + 切换（核心 UI 注入）

构建标签栏，渲染每个非空页一个标签、当前页高亮、左键点击切页。本任务后：开仪表盘可见 "1" 标签且高亮（新存档只有一页，切换在 Task 4 加 `+` 后才能完整演示）。

**Files:**
- Create: `DashboardOverhaul/PageTab.cs`
- Create: `DashboardOverhaul/PageTabBar.cs`
- Rewrite: `DashboardOverhaul/UIDashboardPatch.cs`

**Interfaces:**
- Consumes: `DashboardOverhaulPlugin.ModEnabled`、`PageOps.*`、`UIDashboard`（`.rectTrans`、`.emptyTip.font`、`.focusColor`、`.SetViewPage(int)`、`.charts`、`.active`）、`CustomCharts`（`.dashboardLayout.pages`、`.currentView.pageIndex`）、`DashboardPage`（`.name`）。
- Produces:
  - `PageTabBar`：`void Build(UIDashboard dashboard)`、`void Refresh()`、`void Free()`、`void SwitchTo(int slot)`、`UIDashboard Dashboard { get; }`。
  - `PageTab`：`void Setup(PageTabBar bar, int slot, string label, bool current)`，字段 `int Slot`。
  - `UIDashboardPatch`：静态 `PageTabBar Bar`。

- [ ] **Step 1: 写 `PageTab`（单标签视图）**

`DashboardOverhaul/PageTab.cs`：

```csharp
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
```

- [ ] **Step 2: 写 `PageTabBar`（控制器，本任务只含 Build/Refresh/SwitchTo/Free）**

`DashboardOverhaul/PageTabBar.cs`：

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DashboardOverhaul;

public class PageTabBar
{
    public UIDashboard Dashboard { get; private set; }
    private RectTransform _root;
    private Font _font;
    private readonly List<PageTab> _tabs = new();

    private const int kTabHeight = 26;
    private const int kTabMinWidth = 64;

    public void Build(UIDashboard dashboard)
    {
        Dashboard = dashboard;
        _font = dashboard.emptyTip != null ? dashboard.emptyTip.font : null;

        var go = new GameObject("DO_PageTabBar", typeof(RectTransform));
        _root = (RectTransform)go.transform;
        _root.SetParent(dashboard.rectTrans, false);
        // 顶部横排，左上锚点，自栏目顶部下移一点
        _root.anchorMin = new Vector2(0f, 1f);
        _root.anchorMax = new Vector2(0f, 1f);
        _root.pivot = new Vector2(0f, 1f);
        _root.anchoredPosition = new Vector2(40f, -8f); // 注：位置可能需游戏内微调
        _root.sizeDelta = new Vector2(0f, kTabHeight);

        var layout = go.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 4f;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        var fitter = go.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    public void Free()
    {
        if (_root != null) Object.Destroy(_root.gameObject);
        _root = null;
        _tabs.Clear();
        Dashboard = null;
    }

    public void Refresh()
    {
        if (_root == null || Dashboard == null) return;
        foreach (var t in _tabs) if (t != null) Object.Destroy(t.gameObject);
        _tabs.Clear();

        var charts = Dashboard.charts;
        var pages = charts.dashboardLayout.pages;
        int current = charts.currentView.pageIndex;
        for (int i = 1; i < DashboardLayout.MAX_PAGE_COUNT; i++)
        {
            if (pages[i] == null) continue;
            string label = string.IsNullOrEmpty(pages[i].name) ? i.ToString() : pages[i].name;
            _tabs.Add(CreateTab(i, label, i == current));
        }
        // Task 4 会在此后追加 "+" 按钮
    }

    private PageTab CreateTab(int slot, string label, bool current)
    {
        var go = new GameObject("DO_Tab_" + slot, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(_root, false);

        var bg = go.AddComponent<Image>();
        bg.raycastTarget = true;

        var le = go.AddComponent<LayoutElement>();
        le.minWidth = kTabMinWidth;
        le.preferredHeight = kTabHeight;

        var textGo = new GameObject("Text", typeof(RectTransform));
        var trt = (RectTransform)textGo.transform;
        trt.SetParent(rt, false);
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(10f, 0f); trt.offsetMax = new Vector2(-10f, 0f);
        var text = textGo.AddComponent<Text>();
        text.font = _font;
        text.fontSize = 14;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;

        var tab = go.AddComponent<PageTab>();
        tab.Label = text;
        tab.Background = bg;
        tab.Setup(this, slot, label, current);
        return tab;
    }

    public void SwitchTo(int slot)
    {
        if (Dashboard == null) return;
        Dashboard.SetViewPage(slot); // 原版方法：切页并重排图表
        Refresh();
    }
}
```

- [ ] **Step 3: 写 Harmony 补丁，挂上生命周期**

`DashboardOverhaul/UIDashboardPatch.cs`（整文件替换）：

```csharp
using HarmonyLib;

namespace DashboardOverhaul;

public static class UIDashboardPatch
{
    public static PageTabBar Bar;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIDashboard), "_OnCreate")]
    static void OnCreate_Postfix(UIDashboard __instance)
    {
        if (!DashboardOverhaulPlugin.ModEnabled.Value) return;
        Bar = new PageTabBar();
        Bar.Build(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIDashboard), "_OnOpen")]
    static void OnOpen_Postfix()
    {
        if (Bar != null) Bar.Refresh();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIDashboard), "_OnDestroy")]
    static void OnDestroy_Postfix()
    {
        if (Bar != null) { Bar.Free(); Bar = null; }
    }
}
```

- [ ] **Step 4: 构建，确认成功**

Run: `dotnet build "DashboardOverhaul/DashboardOverhaul.csproj" -c Debug`
Expected: `Build succeeded`。

- [ ] **Step 5: 游戏内确认（里程碑）**

复制 DLL → 启动 → 进存档 → 打开仪表盘。
Expected: 面板顶部出现一个 "1" 标签且高亮显示；无报错。若标签栏位置不合适或不可见，微调 `_root.anchoredPosition`（见 Step 2 注释）后重建并重试。

- [ ] **Step 6: 提交**

```bash
git add DashboardOverhaul/PageTab.cs DashboardOverhaul/PageTabBar.cs DashboardOverhaul/UIDashboardPatch.cs
git commit -m "feat(DashboardOverhaul): render page tab bar and switch pages"
```

---

### Task 4: 新建页面（`+` 按钮）

**Files:**
- Modify: `DashboardOverhaul/PageTabBar.cs`

**Interfaces:**
- Consumes: `PageOps.AddPage`、`UIRealtimeTip.Popup(string)`、`"已达页面上限".Translate()`（字符串本身的双语在 Task 7 注册）。
- Produces: `PageTabBar.AddNewPage()`；`Refresh()` 末尾追加 `+` 按钮。

- [ ] **Step 1: 在 `PageTabBar` 加入 `AddNewPage` 与 `+` 按钮创建**

在 `PageTabBar` 类中新增方法：

```csharp
public void AddNewPage()
{
    if (Dashboard == null) return;
    int slot = PageOps.AddPage(Dashboard.charts);
    if (slot < 0)
    {
        UIRealtimeTip.Popup("已达页面上限".Translate());
        return;
    }
    SwitchTo(slot); // 切到新页并 Refresh
}

private void CreateAddButton()
{
    var go = new GameObject("DO_AddBtn", typeof(RectTransform));
    var rt = (RectTransform)go.transform;
    rt.SetParent(_root, false);

    var bg = go.AddComponent<Image>();
    var c = Dashboard.focusColor;
    bg.color = new Color(c.r, c.g, c.b, 0.15f);

    var le = go.AddComponent<LayoutElement>();
    le.minWidth = kTabHeight; // 方形
    le.preferredHeight = kTabHeight;

    var textGo = new GameObject("Text", typeof(RectTransform));
    var trt = (RectTransform)textGo.transform;
    trt.SetParent(rt, false);
    trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
    trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
    var text = textGo.AddComponent<Text>();
    text.font = _font; text.fontSize = 18; text.alignment = TextAnchor.MiddleCenter;
    text.color = Color.white; text.text = "+"; text.raycastTarget = false;

    var btn = go.AddComponent<Button>();
    btn.targetGraphic = bg;
    btn.onClick.AddListener(AddNewPage);
}
```

- [ ] **Step 2: 在 `Refresh()` 末尾调用 `CreateAddButton()`**

把 `Refresh()` 里 `// Task 4 会在此后追加 "+" 按钮` 这一行替换为：

```csharp
        CreateAddButton();
```

（`+` 按钮是临时 GameObject，会在下次 `Refresh()` 开头随其它子物体被一并 `Destroy`——注意：当前 `Refresh()` 只 Destroy 了 `_tabs` 里的对象。需改为清理 `_root` 下所有子物体。）

把 `Refresh()` 开头的清理段：

```csharp
        foreach (var t in _tabs) if (t != null) Object.Destroy(t.gameObject);
        _tabs.Clear();
```

替换为：

```csharp
        for (int c = _root.childCount - 1; c >= 0; c--)
            Object.Destroy(_root.GetChild(c).gameObject);
        _tabs.Clear();
```

- [ ] **Step 3: 构建，确认成功**

Run: `dotnet build "DashboardOverhaul/DashboardOverhaul.csproj" -c Debug`
Expected: `Build succeeded`。

- [ ] **Step 4: 游戏内确认（里程碑）**

复制 DLL → 启动 → 打开仪表盘。
Expected: 看到 "1" 标签 + "+"。点 "+" → 出现 "2" 标签并切到第 2 页（画布清空）；点 "1" 切回。重复点 "+" 至 9 页后再点 → 弹出"已达页面上限"提示。

- [ ] **Step 5: 提交**

```bash
git add DashboardOverhaul/PageTabBar.cs
git commit -m "feat(DashboardOverhaul): add new pages via + button"
```

---

### Task 5: 重命名（双击标签 / 右键菜单 → 内联输入）

**Files:**
- Modify: `DashboardOverhaul/PageTabBar.cs`

**Interfaces:**
- Consumes: `PageOps.RenamePage`、`UIDashboard.OpenChartPopupMenu(Vector2, RectTransform)`、`UIDashboard.CloseChartPopupMenu()`、`UIPopupMenu.AddMenuButton(string, int, bool)` / `.SetState(true)`、`UIPopupMenuButton.onMenuButtonClick`、`UnityEngine.UI.InputField`。
- Produces: `PageTabBar.OpenContextMenu(PageTab)`、`PageTabBar.BeginRename(PageTab)`。

- [ ] **Step 1: 在 `PageTabBar` 加入一个复用的内联输入框**

在 `PageTabBar` 类加入字段与懒创建：

```csharp
private InputField _renameInput;
private int _renamingSlot = -1;

private InputField EnsureRenameInput()
{
    if (_renameInput != null) return _renameInput;
    var go = new GameObject("DO_RenameInput", typeof(RectTransform));
    var rt = (RectTransform)go.transform;
    rt.SetParent(_root.parent, false); // 挂在标签栏的父级，浮在标签之上
    rt.sizeDelta = new Vector2(120f, kTabHeight);

    var bg = go.AddComponent<Image>();
    bg.color = new Color(0f, 0f, 0f, 0.85f);

    var textGo = new GameObject("Text", typeof(RectTransform));
    var trt = (RectTransform)textGo.transform;
    trt.SetParent(rt, false);
    trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
    trt.offsetMin = new Vector2(6f, 0f); trt.offsetMax = new Vector2(-6f, 0f);
    var text = textGo.AddComponent<Text>();
    text.font = _font; text.fontSize = 14; text.alignment = TextAnchor.MiddleLeft;
    text.color = Color.white; text.supportRichText = false;

    var input = go.AddComponent<InputField>();
    input.textComponent = text;
    input.lineType = InputField.LineType.SingleLine;
    input.characterLimit = 24;
    input.onEndEdit.AddListener(CommitRename);
    go.SetActive(false);
    _renameInput = input;
    return input;
}

public void BeginRename(PageTab tab)
{
    var input = EnsureRenameInput();
    _renamingSlot = tab.Slot;
    var page = Dashboard.charts.dashboardLayout.pages[tab.Slot];
    input.gameObject.SetActive(true);
    // 定位到被改标签的位置
    var inputRt = (RectTransform)input.transform;
    var tabRt = (RectTransform)tab.transform;
    inputRt.position = tabRt.position;
    inputRt.sizeDelta = new Vector2(Mathf.Max(120f, tabRt.rect.width), kTabHeight);
    input.text = page != null ? (page.name ?? string.Empty) : string.Empty;
    input.Select();
    input.ActivateInputField();
}

private void CommitRename(string value)
{
    if (_renamingSlot < 0) return;
    var page = Dashboard.charts.dashboardLayout.pages[_renamingSlot];
    PageOps.RenamePage(page, value);
    _renamingSlot = -1;
    if (_renameInput != null) _renameInput.gameObject.SetActive(false);
    Refresh();
}
```

- [ ] **Step 2: 在 `PageTabBar` 加入右键菜单（本任务只有"重命名"，Task 6 追加"删除"）**

```csharp
public void OpenContextMenu(PageTab tab)
{
    var tabRt = (RectTransform)tab.transform;
    var menu = Dashboard.OpenChartPopupMenu(new Vector2(0f, -kTabHeight), tabRt);

    var rename = menu.AddMenuButton("重命名".Translate());
    rename.onMenuButtonClick += _ => { Dashboard.CloseChartPopupMenu(); BeginRename(tab); };
    rename.SetState(true);

    menu.SetState(true);
}
```

- [ ] **Step 3: 让 `Free()` 一并清理输入框引用**

把 `Free()` 改为：

```csharp
public void Free()
{
    if (_root != null) Object.Destroy(_root.gameObject);
    _root = null;
    _renameInput = null;
    _renamingSlot = -1;
    _tabs.Clear();
    Dashboard = null;
}
```

（说明：`_renameInput` 挂在 `_root.parent` 下，不随 `_root` 销毁；但它在 `EnsureRenameInput` 里会按需重建，且 `_OnDestroy` 时整个仪表盘面板被销毁，无悬挂泄漏。若日后单独复用面板，可在此显式 Destroy。）

- [ ] **Step 4: 构建，确认成功**

Run: `dotnet build "DashboardOverhaul/DashboardOverhaul.csproj" -c Debug`
Expected: `Build succeeded`。

- [ ] **Step 5: 游戏内确认（里程碑）**

复制 DLL → 启动 → 仪表盘。
Expected:
- 双击某标签 → 出现输入框，输入"电力"回车 → 标签显示"电力"。
- 右键标签 → 弹出菜单含"重命名"；点它同样进入改名。
- 存档→重载 → 页名仍为"电力"（验证持久化）。

- [ ] **Step 6: 提交**

```bash
git add DashboardOverhaul/PageTabBar.cs
git commit -m "feat(DashboardOverhaul): rename pages via double-click and context menu"
```

---

### Task 6: 删除页面（右键菜单 → 确认 → 跳转）

**Files:**
- Modify: `DashboardOverhaul/PageTabBar.cs`

**Interfaces:**
- Consumes: `PageOps.CanDelete`、`PageOps.PickPageAfterDelete`、`PageOps.RemovePage`、`UIMessageBox.Show(...)`、`UIRealtimeTip.Popup(string)`、`DashboardPage.chartDatas`。
- Produces: `PageTabBar.DeletePage(PageTab)`。

- [ ] **Step 1: 在 `PageTabBar` 加入删除逻辑**

```csharp
public void DeletePage(PageTab tab)
{
    var charts = Dashboard.charts;
    if (!PageOps.CanDelete(charts))
    {
        UIRealtimeTip.Popup("至少保留一页".Translate());
        return;
    }
    int slot = tab.Slot;
    var page = charts.dashboardLayout.pages[slot];
    bool hasCharts = page != null && page.chartDatas != null && page.chartDatas.Count > 0;
    if (hasCharts)
        UIMessageBox.Show("删除页面标题".Translate(), "删除页面提示".Translate(),
            "取消".Translate(), "确定".Translate(), 1, null, () => DoDeletePage(slot));
    else
        DoDeletePage(slot);
}

private void DoDeletePage(int slot)
{
    var charts = Dashboard.charts;
    int target = PageOps.PickPageAfterDelete(charts.dashboardLayout, slot);
    bool deletingCurrent = charts.currentView.pageIndex == slot;
    if (!PageOps.RemovePage(charts, slot)) return;
    if (deletingCurrent && target > 0)
        Dashboard.SetViewPage(target); // 跳到相邻页并重排图表
    Refresh();
}
```

- [ ] **Step 2: 在右键菜单追加"删除"**

把 `OpenContextMenu` 改为（在"重命名"之后加"删除"）：

```csharp
public void OpenContextMenu(PageTab tab)
{
    var tabRt = (RectTransform)tab.transform;
    var menu = Dashboard.OpenChartPopupMenu(new Vector2(0f, -kTabHeight), tabRt);

    var rename = menu.AddMenuButton("重命名".Translate());
    rename.onMenuButtonClick += _ => { Dashboard.CloseChartPopupMenu(); BeginRename(tab); };
    rename.SetState(true);

    var del = menu.AddMenuButton("删除".Translate());
    del.onMenuButtonClick += _ => { Dashboard.CloseChartPopupMenu(); DeletePage(tab); };
    del.SetState(true);

    menu.SetState(true);
}
```

- [ ] **Step 3: 构建，确认成功**

Run: `dotnet build "DashboardOverhaul/DashboardOverhaul.csproj" -c Debug`
Expected: `Build succeeded`。

- [ ] **Step 4: 游戏内确认（里程碑）**

复制 DLL → 启动 → 仪表盘。
Expected:
- 在某页放 1 个图表，右键该页标签 → "删除" → 弹确认框；确定后页消失、自动跳邻页、画布更新。
- 空页删除不弹确认、直接删。
- 删当前页时跳转到相邻页正确。
- 仅剩 1 页时点"删除" → 弹"至少保留一页"，不删。
- 删第 3 页后页号呈 1、2、4…（不连续，符合设计）。

- [ ] **Step 5: 提交**

```bash
git add DashboardOverhaul/PageTabBar.cs
git commit -m "feat(DashboardOverhaul): delete pages with confirm and auto-switch"
```

---

### Task 7: 本地化、`Enabled` 收尾、打包

**Files:**
- Modify: `DashboardOverhaul/DashboardOverhaulPlugin.cs`
- Create: `DashboardOverhaul/package/manifest.json`
- Create: `DashboardOverhaul/package/README.md`
- Create: `DashboardOverhaul/package/icon.png`（256×256 PNG，占位图即可）

**Interfaces:**
- Consumes: `UXAssist.Common.I18N.Add(key, en, zh)` / `I18N.Apply()`。

- [ ] **Step 1: 注册双语字符串**

在 `DashboardOverhaulPlugin.Awake()` 内、`new Harmony(...)` 之前加入：

```csharp
        UXAssist.Common.I18N.Add("已达页面上限", "Page limit reached", "已达页面上限");
        UXAssist.Common.I18N.Add("至少保留一页", "Keep at least one page", "至少保留一页");
        UXAssist.Common.I18N.Add("重命名", "Rename", "重命名");
        UXAssist.Common.I18N.Add("删除", "Delete", "删除");
        UXAssist.Common.I18N.Add("删除页面标题", "Delete page", "删除页面");
        UXAssist.Common.I18N.Add("删除页面提示", "Delete this page and its charts?", "确认删除该页及其图表？");
        UXAssist.Common.I18N.Add("取消", "Cancel", "取消");
        UXAssist.Common.I18N.Add("确定", "Confirm", "确定");
        UXAssist.Common.I18N.Apply();
```

（注："取消"/"确定"等原版可能已有翻译键；若与原版冲突或已存在，去掉重复项即可——以游戏内显示为准。）

- [ ] **Step 2: 写 `package/manifest.json`**

```json
{
  "name": "DashboardOverhaul",
  "version_number": "1.0.0",
  "website_url": "",
  "description": "Completes the Dashboard's multi-page UI: switch/add/delete/rename pages",
  "dependencies": []
}
```

- [ ] **Step 3: 写 `package/README.md`**

```markdown
# DashboardOverhaul

补全 DSP 仪表盘的多页界面：顶部标签栏，支持切换 / 新建 / 删除 / 重命名页面。
原版仪表盘底层已支持最多 9 页并随存档保存，但从未提供切页 UI——本 mod 补上这一前端。

Completes the in-game Dashboard's unshipped multi-page UI: a top tab bar to
switch / add / delete / rename pages (vanilla already stored up to 9 pages).

依赖 / Requires: UXAssist。
```

- [ ] **Step 4: 放入占位 `package/icon.png`**

放一张 256×256 PNG（可暂用任意占位图）。

- [ ] **Step 5: Debug 构建确认**

Run: `dotnet build "DashboardOverhaul/DashboardOverhaul.csproj" -c Debug`
Expected: `Build succeeded`。

- [ ] **Step 6: 游戏内回归（里程碑，跑完整清单）**

复制 DLL → 启动，逐条核对 spec §11 清单：新建/切换/重命名/删除生效；存档重载持久化；旧存档只 1 页；删当前页跳转正确；仅剩 1 页禁删；满 9 槽提示；删非空页确认。再把 `Enabled` 配成 false 重启 → 标签栏不出现、行为同原版。

- [ ] **Step 7: Release 构建（产出 Thunderstore zip）**

Run: `dotnet build "DashboardOverhaul/DashboardOverhaul.csproj" -c Release`
Expected: `Build succeeded`，生成 `DashboardOverhaul/package/DashboardOverhaul-1.0.0.zip`。

- [ ] **Step 8: 提交**

```bash
git add DashboardOverhaul/DashboardOverhaulPlugin.cs DashboardOverhaul/package/manifest.json DashboardOverhaul/package/README.md DashboardOverhaul/package/icon.png
git commit -m "feat(DashboardOverhaul): localization, Enabled gating, packaging"
```

---

## 实现者注意（UI 注入的现实）

- 标签栏的锚定/父级/位置（`PageTabBar.Build` 中 `_root` 的 `anchoredPosition`、parent 选 `dashboard.rectTrans`）是合理初值，但**很可能需要在游戏内微调**——以实际显示为准，这类调整属预期内迭代。
- 右键菜单 `OpenChartPopupMenu` 是仪表盘自带方法（原本服务于图表菜单），用于标签亦可；若定位偏移，参照 `UIChart.OnMenuButtonClick` 的用法调整传入坐标。
- `UIMessageBox.Show` 的回调签名以游戏内实际为准（参考 `UIStatPlanEntry.OnDelBtnClick` 的调用：`UIMessageBox.Show(title, text, cancel, confirm, type, onCancel, onConfirm)`，回调为 `UIMessageBox.Response` 委托）。若签名不匹配，按 `UIStatPlanEntry` 的真实写法对齐。
```
