# Changelog / 更新日志

All notable changes to DashboardOverhaul are documented here.
本文件记录 DashboardOverhaul 的版本变更。

## [1.3.0] - 2026-09-22

- 独立顶部标签栏适配窗口宽度，扩大点击区域，提供悬停/焦点状态和完整页名提示，避免与图表标题和侧栏菜单重叠 / A separate responsive tab bar with larger hit areas, hover/focus states and full-name tooltips stays clear of chart titles and sidebar menus
- 保留原生侧栏开关，固定背景与网格，仅图表层平移避让，侧栏和面板之间不再露出游戏画面 / Preserve the native sidebar toggle; keep the background and grid stationary while charts pan clear of the handle, with no uncovered gap
- 页签重命名输入框始终位于顶部可见区域，编辑背景遮住原页名 / Keep the page rename input inside the visible header and hide the original label while editing
- 新页追加到末尾；达到页数上限或仅剩一页时禁用对应操作并说明原因 / Append new pages; explain disabled page-limit and last-page actions
- 区分移除单图表和删除统计项，改名/删除显示影响范围，名称提示同步更新 / Distinguish chart removal from statistic deletion; show rename/delete scope and synchronize name tooltips
- 跨页移动提供“前往”入口；无空位时保留原图表 / Move notices offer navigation; full destinations leave the original chart intact
- 空白页提供添加图表、打开统计窗口及复制其他页布局的入口 / Empty-page actions add charts, open statistics or copy another page's layout
- 统一名称编辑的提交/取消和焦点清理，修正删除当前页时的事件解绑顺序及无效页码恢复时机 / Consistent rename completion and focus cleanup; correct current-page deletion teardown and repair invalid page indices before opening

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
