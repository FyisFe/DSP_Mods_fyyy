# Changelog / 更新日志

All notable changes to DashboardOverhaul are documented here.
本文件记录 DashboardOverhaul 的版本变更。

## [1.3.0] - 2026-09-22

- 顶部页签改为独立顶栏，按窗口宽度调整，并提供更大的点击区域、悬停/焦点状态和完整页名提示。/ The top tabs now use a separate responsive header with larger hit areas, hover/focus states and full-name tooltips.
- 新页追加到末尾；达到 9 页上限或仅剩一页时禁用相应操作并提示原因。/ New pages append at the end; actions disabled at the nine-page limit or on the last page explain why.
- 删除含图表的页面时，确认框显示页名、图表数量，以及统计项仍保留在侧栏。/ Deleting a page with charts shows its name, chart count and retained sidebar statistics in the confirmation.
- 图表菜单明确区分“移除此图表”和“删除统计项及其全部图表”；重命名与删除统计项时显示关联图表数量，并同步名称提示。/ The chart menu distinguishes removing one chart from deleting a statistic and all its charts; rename and delete show the affected count, and name tooltips update.
- 移动图表后可前往目标页并高亮图表；目标页无空位时原图表保持不变。/ After moving a chart, the notice can open its destination and highlight it; a full destination leaves the original chart unchanged.
- 空白页新增打开统计窗口、打开侧栏及复制其他非空页面的入口；复制布局和显示设置，统计项仍共享。/ Empty pages now offer shortcuts to statistics, the sidebar and copying a populated page; copies share statistics but keep independent display settings.
- 名称编辑新增 Esc 取消与切页前提交；图表回收时取消未提交的统计项名称编辑。/ Name editing now cancels on Esc and commits before a page switch; recycling a chart cancels its pending statistic rename.

## [1.2.0] - 2026-06-22

页面重新排序 / Page reordering.

### 新增 / Added

- 拖动页面标签即可重新排序，其它标签实时让位；新顺序随存档保存 / Drag a page tab to reorder pages — the other tabs slide aside in real time; the new order persists in your save
- 未命名页面在标签上显示其位置序号，重排后自动更新（新建页面默认无名）/ An unnamed page shows its position number on the tab and updates after a reorder (new pages start unnamed)

## [1.1.0] - 2026-06-22

图表统计项管理 / Chart statistic management.

### 新增 / Added

- 重命名单个图表的统计项，从图表右键菜单或双击标题 / Rename a chart's statistic from the chart right-click menu or by double-clicking its title
- 删除统计项，从图表右键菜单（会移除该统计项在所有页面的图表，有确认）/ Delete a statistic from the chart menu (removes that statistic's charts on all pages; confirms first)

## [1.0.0] - 2026-06-21

首个发布版 / Initial release.

### 新增 / Added

- 顶部标签栏，最多 9 个仪表盘页面 / Top tab bar, up to 9 Dashboard pages
- 切换 / 新建 / 删除 / 重命名 页面 / Switch · add · delete · rename pages
- 把单个图表移动到其它页面（图表右键菜单 → 移动到页面）/ Move an individual chart to another page (chart right-click → Move to page)
- 标签栏随侧边栏滑动避让 / Tab bar slides clear of the sidebar
- 中英双语；无额外依赖；不改存档格式；始终生效 / Bilingual (EN / 中文); no extra mod dependencies (BepInEx only); no save-format change; always on
