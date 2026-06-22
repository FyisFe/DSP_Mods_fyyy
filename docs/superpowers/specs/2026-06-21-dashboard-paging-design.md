# DashboardOverhaul — 仪表盘分页功能 设计文档

- 日期：2026-06-21
- 状态：**已实现并通过游戏内验证**（v1.0.0，分支 `dashboard-overhaul` / PR #1）
- 适用版本：DSP（Assembly-CSharp，基于 GameCode-latest 反编译核对）

> 本文档描述 **已发布（as-shipped）** 的设计。逐步实现计划见同目录 `plans/2026-06-21-dashboard-paging.md`（该计划早于若干基于游戏内反馈的调整，以本文为准）。变更记录见文末。

## 1. 背景与问题

DSP 的仪表盘（Dashboard，`UIDashboard`）底层**已经具备完整的多页能力**，但前端从未做出来：

- `DashboardLayout.pages` 是定长数组 `new DashboardPage[10]`，`MAX_PAGE_COUNT = 10`，第 0 槽闲置 → 实际 **1..9 共 9 个可用页**。
- 切换页面的 `UIDashboard.SetViewPage(int)`、新建页面的 `DashboardLayout.AddPage(int)` 都已实现并可用；`DashboardViewState.pageIndex` 默认为 1。
- 整套布局（含每页 `DashboardPage.name` 与其图表）由 `CustomCharts.Export/Import` → `DashboardLayout.Export/Import` → `DashboardPage.Export/Import` **随存档序列化**。
- 但**没有任何已发布 UI 调用 `SetViewPage`**——经核对，唯一调用方是开发用测试类 `TestDashboard`。因此玩家在游戏里只能看到第 1 页。
- `DashboardLayout.RemovePage(int)` 是**空实现**（no-op），即原版无法删除页面。

**目标**：补上分页前端 = 顶部标签栏，支持 切换 / 新建 / 删除 / 重命名，并支持把单个图表在页面间移动。

## 2. 范围

**已实现**
- 顶部标签栏 UI（导航控件方案 A）。
- 页面操作：切换、新建、删除、重命名。
- **图表跨页移动**：在每个图表自身的右键菜单加"移动到页面 →"。
- 中英双语（自带 `Loc`，无外部依赖）；**始终生效，无开关**。

**不纳入（YAGNI）**
- 拖拽重排页面顺序。
- 提高页数上限（保持原版 9 页结构）。
- 仪表盘其它方面的改动（图表内容、统计项、样式系统、告警等）。

## 3. 关键设计决策

| 维度 | 决定 | 理由 |
|---|---|---|
| 导航控件 | 顶部标签栏（方案 A） | 可发现性最高、最符合"快速跳转 + 总览"的分页诉求 |
| 页数上限 | 保持原版 9 页结构 | 零存档格式改动、完全兼容；9 个标签横向排得下 |
| 实现路线 | Harmony 注入 + 代码构建标签栏（方案①） | 可控性最高、不依赖外部预制体 |
| 删除处理 | 留空槽、不移位（方案甲） | 图表只引用 `statPlanId` 不引用页号，置空 `pages[i]` 最安全、无索引错位；页号可不连续，但标签显示名字故玩家无感 |
| 图表迁移 | 单个图表右键"移动到页面"（patch `UIChart.SetPopupMenuButtons`） | 比整页移动更灵活；移动的是 `ChartData` 对象本身，保留样式/尺寸/预设 |
| 配置 | **无开关，始终生效** | 功能即核心价值，无需开关；也免去配置依赖 |
| 本地化 / 依赖 | 自带 `Loc.L(zh,en)`（基于 `Localization.isZHCN`），**不依赖 UXAssist** | 零外部 mod 依赖；避免覆写游戏全局字符串 |

## 4. 架构

- 独立 BepInEx 插件 `DashboardOverhaul`：BepInEx 5 + Harmony，`net472`，**无外部 mod 依赖**。
- 纯 **Harmony 注入**：挂在 `UIDashboard` 生命周期上驱动标签栏，并在 `UIChart` 的右键菜单上追加"移动到页面"。
- **不新增存档数据**：页面与页名复用 `GameData.statistics.charts.dashboardLayout`，跟随原版存档持久化。

## 5. 组件

| 组件 | 职责 | 依赖 |
|---|---|---|
| `DashboardOverhaulPlugin` | 插件入口：Harmony 引导（patch `UIDashboardPatch` + `UIChartPatch`） | BepInEx / Harmony |
| `Loc` | 自带双语助手 `L(zh,en)`（按 `Localization.isZHCN` 选语言） | 游戏 `Localization` |
| `PageOps`（纯逻辑，无 UI） | 对 `DashboardLayout` 的增/删/改/切规则判定 | 仅 `CustomCharts` / `DashboardLayout` |
| `PageTabBar`（控制器） | 创建并持有标签栏；重建标签；切页只更新高亮；跟随侧栏定位；重命名输入框；页面右键菜单 | `PageOps` |
| `PageTab`（单标签视图） | 一个标签按钮：页名 + 选中高亮；左键切换 / 双击重命名 / 右键弹菜单 | — |
| `UIDashboardPatch`（Harmony） | `UIDashboard` 生命周期：`_OnCreate` 建栏 / `_OnOpen` 刷新 / `_OnUpdate` 跟随侧栏 / `_OnDestroy` 清理 | 以上 |
| `UIChartPatch`（Harmony） | `UIChart.SetPopupMenuButtons` 后追加"移动到页面 →"子菜单，移动该图表的 `ChartData` 到目标页 | 以上 |

**隔离原则**：`PageOps` 不触碰任何 Unity UI，纯粹操作数据结构；UI 层只负责显示与转发。

`PageOps` 接口（实际签名）：

```
int  ActivePageCount(CustomCharts charts)
int  FirstFreeSlot(DashboardLayout layout)        // 1..9 最小空槽，满则 -1
int  AddPage(CustomCharts charts)                 // 占用最小空槽并返回槽号，满则 -1
bool CanDelete(CustomCharts charts)               // 现有非空页 > 1
int  PickPageAfterDelete(DashboardLayout layout, int deletedIndex) // 1..9 或 -1
bool RemovePage(CustomCharts charts, int index)   // 置空该槽并释放其图表
void RenamePage(DashboardPage page, string name)
// 切换直接复用 UIDashboard.SetViewPage(int)
```

## 6. 数据流

唯一数据源：`charts.dashboardLayout.pages[1..9]` + `charts.currentView.pageIndex`。

- **切换**：点标签 → `SetViewPage(i)`（内部重排图表）→ 仅更新标签高亮（**不重建**，否则会打断双击）。
- **新建**：点 `＋` → `PageOps.AddPage` 取最小空槽 → `SetViewPage` → 重建标签。槽满弹"已达页面上限"。
- **重命名**：双击标签 / 右键菜单"重命名" → 就地输入框 → 写 `page.name` → 重建标签。
- **删除**：右键菜单"删除" → 若该页有图表先 `UIMessageBox` 确认 → `PageOps.RemovePage` → 跳相邻页 → 重建标签。
- **图表跨页移动**：图表右键菜单"移动到页面 →"选目标页 → 把该 `ChartData` 从当前页 `chartDatas` 移到目标页（置顶 depth）→ `DetermineCharts` 重渲染当前页。

## 7. 持久化

- `DashboardPage.name` 与 `pages[]` 已由原版序列化，**重命名 / 增删页 / 跨页移动 自动随存档保存，零格式改动**。
- 副作用：用本 mod 建的多页存档若之后不带 mod 打开，数据仍在，但原版无切页 UI 只能看到第 1 页（非破坏性，装回即恢复）。

## 8. 边界与容错

- **至少保留 1 页**：仅剩一页时禁止删除（拒绝 + 提示"至少保留一页"）。
- **删除当前页**：自动跳到最近的非空页（先向小页号找，再向大页号找；`PickPageAfterDelete` 返回 1..9 或 -1）。
- **槽位已满（9 个全占）**：`＋` 弹"已达页面上限"。
- **页号域 1..9**，第 0 槽闲置。
- **新页默认名** = 槽位号；名字留空则标签回退显示槽号。
- **图表移动**：仅当存在其它页时菜单才出现；移动后图表从当前页消失、切到目标页可见。
- **重命名生命周期**：`onEndEdit` 在失焦时也会触发——`CommitRename`/`BeginRename` 均对 `Dashboard`/`charts`/已删除页加空值保护。

## 9. 外观

- 标签栏挂在面板顶部，复用游戏字体（`emptyTip.font`）与配色；选中页用 `focusColor` 高亮。
- **跟随侧栏**：`UpdateLayout`（由 `_OnUpdate` 每帧调用）按侧栏 `statboardTestRt` 的可见宽度右移标签栏，避免与展开的侧栏重叠。
- **新建按钮的"+"用两条白色 `Image` 拼出**——游戏 UI 字体不渲染 ASCII `+` 字形，故不用文本。
- 最多 9 个标签，横向排得下，无需滚动。

## 10. Harmony 挂点

- `UIDashboard._OnCreate`（postfix）→ 构建标签栏。
- `UIDashboard._OnOpen`（postfix）→ 重建标签（页列表 + 高亮）。
- `UIDashboard._OnUpdate`（postfix）→ `UpdateLayout` 跟随侧栏。
- `UIDashboard._OnDestroy`（postfix）→ 清理。
- `UIChart.SetPopupMenuButtons`（postfix）→ 追加"移动到页面 →"子菜单。
- 切换复用 `UIDashboard.SetViewPage(int)`；删除确认复用 `UIMessageBox.Show(...)`；右键菜单复用 `UIDashboard.OpenChartPopupMenu` + `UIPopupMenu`。

## 11. 测试策略（诚实说明）

- 经 Harmony 注入的 Unity UI，本仓库无单元测试框架，传统 TDD 不适用。
- `PageOps` 为纯逻辑：索引/当前页变化可离线推演并重点审查。
- 主验证 = **游戏内手动清单**：新建 / 切换 / 重命名 / 删除 / 图表跨页移动；存档→重载持久化；旧存档只显示 1 页；删当前页跳转正确；仅剩 1 页禁删；满 9 槽提示；删非空页确认框；侧栏展开不重叠；`+` 号可见。
- 本功能已按上述清单完成游戏内验证。

## 12. 配置与本地化

- **无配置开关**：始终生效（早期设计曾有 `Enabled` 开关，按反馈移除）。
- 中英双语经自带 `Loc.L(zh,en)`（`Localization.isZHCN`），不依赖 UXAssist，也不向游戏全局字符串表注册（避免覆写原版词条）。

## 13. 风险

- **游戏更新**改动 `UIDashboard` / `UIChart` 字段、生命周期或 `SetPopupMenuButtons` 虚方法 → UI 注入脆弱（与所有 UI mod 同类风险）。
- **与其它改仪表盘的 mod** 潜在冲突（标签栏注入、`RemovePage` 逻辑、图表右键菜单）。
- 标签栏位置 `kBaseLeftMargin` 为经验值，不同分辨率/缩放可能需微调。

## 变更记录 / Changelog

- **v1.0.0（2026-06-21）** 首个发布版。相对早期 v1 计划的调整（均源于游戏内验证）：
  - 新增「图表跨页移动」（`UIChartPatch`）。
  - 移除 UXAssist 依赖，改用自带 `Loc`（双语，零外部依赖）。
  - 移除 `Enabled` 配置开关，功能始终生效。
  - 标签栏跟随侧栏滑动，避免重叠（`_OnUpdate` + `UpdateLayout`）。
  - 切页改为只更新高亮（不再重建标签），修复双击重命名失效。
  - 新建按钮「+」改用 `Image` 条绘制（游戏字体无 `+` 字形）。
