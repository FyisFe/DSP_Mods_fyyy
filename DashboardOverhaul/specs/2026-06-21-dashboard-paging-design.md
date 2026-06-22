# DashboardOverhaul — 仪表盘分页功能 设计文档（v1）

- 日期：2026-06-21
- 状态：已通过脑暴评审，待规格评审
- 适用版本：DSP（Assembly-CSharp，基于 GameCode-latest 反编译核对）

## 1. 背景与问题

DSP 的仪表盘（Dashboard，`UIDashboard`）底层**已经具备完整的多页能力**，但前端从未做出来：

- `DashboardLayout.pages` 是定长数组 `new DashboardPage[10]`，`MAX_PAGE_COUNT = 10`，第 0 槽闲置 → 实际 **1..9 共 9 个可用页**。
- 切换页面的 `UIDashboard.SetViewPage(int)`、新建页面的 `DashboardLayout.AddPage(int)` 都已实现并可用；`DashboardViewState.pageIndex` 默认为 1。
- 整套布局（含每页 `DashboardPage.name` 与其图表）由 `CustomCharts.Export/Import` → `DashboardLayout.Export/Import` → `DashboardPage.Export/Import` **随存档序列化**。
- 但**没有任何已发布 UI 调用 `SetViewPage`**——经核对，唯一调用方是开发用测试类 `TestDashboard`。因此玩家在游戏里只能看到第 1 页。
- `DashboardLayout.RemovePage(int)` 是**空实现**（no-op），即原版无法删除页面。

**目标**：补上分页前端 = 顶部标签栏，支持 切换 / 新建 / 删除 / 重命名 四种操作，并补全删除逻辑。

## 2. 范围

**纳入（v1）**
- 顶部标签栏 UI（导航控件方案 A）。
- 页面操作：切换、新建、删除、重命名。
- 中英双语；一个 `Enabled` 开关。

**不纳入（v1，YAGNI）**
- 拖拽重排页面顺序。
- 提高页数上限（保持原版 9 页结构）。
- 仪表盘其它方面的改动（图表、统计项、样式系统、告警等）。

## 3. 关键设计决策

| 维度 | 决定 | 理由 |
|---|---|---|
| 导航控件 | 顶部标签栏（方案 A） | 可发现性最高、最符合"快速跳转 + 总览"的分页诉求 |
| 页数上限 | 保持原版 9 页结构 | 零存档格式改动、完全兼容；9 个标签横向排得下 |
| 实现路线 | Harmony 注入 + 代码构建标签栏（方案①），尽量复用游戏原生按钮素材 | 可控性最高、不依赖外部预制体；复用素材以贴近原生外观 |
| 删除处理 | 留空槽、不移位（方案甲） | 图表只引用 `statPlanId` 不引用页号，置空 `pages[i]` 最安全、无索引错位风险；页号可不连续，但标签显示名字故玩家无感 |

## 4. 架构

- 新建独立 BepInEx 插件 `DashboardOverhaul`：BepInEx 5 + Harmony，`net472`，依赖 **UXAssist**（I18N + 后续配置 UI），沿用本仓库既有 mod 约定。
- 纯 **Harmony 注入**：挂在 `UIDashboard` 生命周期上，向面板顶部加入并驱动标签栏。
- **不新增存档数据**：页面与页名复用 `GameData.statistics.charts.dashboardLayout`，跟随原版存档持久化。

## 5. 组件

| 组件 | 职责 | 依赖 |
|---|---|---|
| `DashboardOverhaulPlugin` | 插件入口：Harmony 引导、`Enabled` 配置、I18N 注册 | BepInEx / Harmony / UXAssist |
| `PageOps`（纯逻辑，无 UI） | 对 `DashboardLayout` 的增/删/改/切操作与规则判定 | 仅 `CustomCharts` / `DashboardLayout` |
| `PageTabBar`（控制器） | 创建并持有标签栏 GameObject；按页面数据重建标签；转发用户操作到 `PageOps` | `PageOps` |
| `PageTab`（单标签视图） | 一个标签按钮：页名 + 选中高亮；左键切换 / 双击重命名 / 右键弹菜单 | — |
| `UIDashboardPatch`（Harmony） | 在 `UIDashboard` 生命周期挂点上 建栏 / 刷新 / 清理 | 以上 |

**隔离原则**：`PageOps` 不触碰任何 Unity UI，纯粹操作数据结构，因此"删除后索引如何变化、当前页如何跳转"这类易错逻辑可集中、可离线推演；UI 层只负责显示与转发。

`PageOps` 接口草案（最终签名实现时定）：

```
int  AddPage(CustomCharts charts)                 // 占用最小空槽 1..9；返回槽号，满则 -1
bool RemovePage(CustomCharts charts, int index)   // 置空该槽并释放其图表；返回是否成功
void RenamePage(DashboardPage page, string name)  // 写 page.name
bool CanDelete(CustomCharts charts)               // 现有非空页 > 1 才允许删
int  PickPageAfterDelete(CustomCharts charts, int deletedIndex)  // 删当前页后跳向的目标页
// 切换直接复用 UIDashboard.SetViewPage(int)
```

## 6. 数据流

唯一数据源：`charts.dashboardLayout.pages[1..9]` + `charts.currentView.pageIndex`。标签栏在面板打开时、以及任一操作后按数据重建。

- **切换**：点标签 → `SetViewPage(i)`（内部已重排图表）→ 刷新标签高亮。
- **新建**：点 `＋` → `PageOps.AddPage` 取最小空槽 → `DashboardLayout.AddPage(slot)` → 切到新页 → 刷新。槽满弹"已达页面上限"。
- **重命名**：双击标签 / 右键菜单 → 就地输入 → 写 `page.name` → 刷新。
- **删除**：右键菜单 → 若该页有图表先 `UIMessageBox` 确认（沿用 `UIStatPlanEntry` 删除确认风格）→ `PageOps.RemovePage(i)` → 跳相邻页 → 刷新。

## 7. 持久化

- `DashboardPage.name` 与 `pages[]` 已由原版序列化，**重命名与增删页自动随存档保存/读取，零格式改动**。
- 副作用：用本 mod 建的多页存档，若之后不带 mod 打开——数据仍在，但原版无切页 UI，只能看到第 1 页（非破坏性，装回 mod 即恢复）。

## 8. 边界与容错

- **至少保留 1 页**：仅剩一页时禁止删除（拒绝 + 提示）。
- **删除当前页**：自动跳到最近的非空页（优先前一页，否则后一页）。
- **槽位已满（9 个全占）**：`＋` 弹"已达页面上限"。
- **页号域 1..9**，第 0 槽保持闲置（与原版语义一致）。
- **新页默认名** = 槽位号（原版 `AddPage` 行为），可随时重命名；名字留空则标签回退显示槽号。
- **加载旧存档**：仅页 1 存在 → 标签栏显示 1 个标签 + `＋`。

## 9. 外观

- 标签栏挂在面板顶部，复用游戏自带按钮的 Sprite / 字体 / 配色；选中页高亮（采用面板既有 `focusColor` 风格）。
- 最多 9 个标签，横向直接排得下，**v1 无需滚动**。

## 10. Harmony 挂点（具体）

- `UIDashboard._OnCreate`（postfix）→ 构建标签栏 GameObject。
- `UIDashboard._OnOpen`（postfix）→ 刷新标签（页列表 + 当前页高亮）。
- `UIDashboard._OnFree` / `_OnDestroy`（postfix）→ 清理标签栏。
- 切换复用 `UIDashboard.SetViewPage(int)`；标签刷新由操作事件驱动，**无需** patch `_OnUpdate`。

## 11. 测试策略（诚实说明）

- 这是经 Harmony 注入的 Unity UI，本仓库无单元测试框架，传统 TDD 不适用。
- `PageOps` 为纯逻辑：增删改切的索引/当前页变化可离线推演并重点审查。
- 主验证 = **游戏内手动清单**：
  1. 新建页 / 切换页 / 重命名页 / 删除页 均生效；
  2. 存档 → 重载，页与页名持久化正确；
  3. 加载旧存档只显示 1 页；
  4. 删除当前页时跳转目标正确；
  5. 仅剩 1 页时禁止删除；
  6. 9 槽全满时 `＋` 给出"已达页面上限"提示；
  7. 删除非空页弹确认框。
- 必要时用 `run` / `verify` 技能拉起游戏实测。

## 12. 配置与本地化

- 一个 `Enabled` 开关（v1 不加多余配置）。
- 中英双语（UXAssist I18N），字符串清单：新建页面、重命名、删除、删除确认（标题 + 正文 + 取消 + 确定）、已达页面上限。

## 13. 风险

- **游戏更新**改动 `UIDashboard` 字段或生命周期 → UI 注入脆弱（与所有 UI mod 同类风险）。
- **与其它改仪表盘的 mod** 潜在冲突（标签栏注入、`RemovePage` 逻辑）。
- **UXAssist 依赖**：需保证版本可用。
