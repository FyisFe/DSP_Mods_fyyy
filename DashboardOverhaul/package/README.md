# DashboardOverhaul

为游戏内**仪表盘**提供一系列优化与改进。零额外依赖、不改存档格式、始终生效。
A series of enhancements for the in-game **Dashboard**. No extra mod dependencies (BepInEx only), no save-format change, always on.

## 功能 / Features

- 顶部标签栏，最多 9 个页面 / Top tab bar, up to 9 pages
- 切换 / 新建 / 删除 / 重命名 页面 / Switch · add · delete · rename pages
- 把单个图表移动到其它页面 / Move an individual chart to another page
- 页面与页名随存档保存 / Pages & names persist in your save
- 重命名 / 删除 单个图表的统计项（图表右键菜单，或双击标题重命名）/ Rename · delete a chart's statistic (chart right-click menu, or double-click the title to rename)
- 拖动页面标签重新排序 / Drag page tabs to reorder pages
- 独立顶部工具栏，标签随窗口宽度自适应；长页名悬停可查看全文 / Separate header with tabs that adapt to the window width; hover to read full page names
- 空白页提供添加图表和复制其他页面的入口 / Empty pages offer actions to add charts or copy another page
- 移动图表后可直接前往目标页；目标页没有空位时保留原图表 / Jump to a moved chart; a full destination leaves the original chart untouched

## 使用 / How to use

- 像往常一样打开仪表盘，标签栏出现在顶部 / Open the Dashboard as usual — the tab bar is at the top
- 使用原生侧栏箭头打开或收起侧栏；背景与网格保持固定，仅图表层平移避让，图表和侧栏菜单位于标签栏下方 / Use the native arrow to toggle the sidebar; the background and grid stay fixed while the chart layer pans clear of the handle; charts and sidebar menus sit below the tabs
- **切换页面**：点击标签 / **Switch**: click a tab
- **新建页面**：点击 `+`，新页追加到末尾；达到 9 页后按钮禁用并显示原因 / **Add**: click `+` to append a page; disabled with an explanation at 9 pages
- **重命名**：双击标签，或 右键标签 → 重命名 / **Rename**: double-click a tab, or right-click → Rename
- **删除页面**：右键标签 → 删除页面（确认框显示页名和图表数量，统计项保留在侧栏；最后一页不能删除）/ **Delete**: right-click a tab → Delete page (confirms the name and chart count; statistics remain in the sidebar; the last page cannot be deleted)
- **重新排序页面**：左右拖动标签（未命名页面的序号会随位置更新）/ **Reorder pages**: drag a tab left or right (an unnamed page's number follows its position)
- **移动图表**：右键图表 → 移动到页面 → 选目标页；提示中的“前往”可切页并高亮图表。无可用空位时保留在原页 / **Move a chart**: right-click → Move to page → choose a page. Use “Go to page” in the notice to highlight it. If no space is available, the chart stays on its original page
- **重命名统计项**：右键图表 → 重命名统计项，或双击标题；会提示影响的图表数量，同一统计项在所有页面的标题、提示和侧栏名称同步更新 / **Rename a statistic**: use the chart menu or double-click its title. The affected chart count is shown; titles, tooltips and the sidebar name update together
- **移除此图表**：只移除当前图表，保留统计项及其他图表 / **Remove this chart**: removes only this chart, keeping its statistic and other charts
- **删除统计项及其全部图表**：确认框显示统计项名称和影响数量；包括其他页面及监控视图中的关联图表 / **Delete statistic and all its charts**: confirms the statistic name and affected count, including other pages and watch views
- **编辑名称**：Enter 提交，Esc 取消；失焦或切页提交，关闭仪表盘或回收目标图表时取消未完成的编辑 / **Edit names**: Enter commits, Esc cancels; focus loss or switching pages commits; closing the dashboard or recycling the target chart cancels an unfinished edit
- **空白页**：“添加图表”打开已有统计项侧栏；尚无统计项时可打开统计窗口。“复制其他页面”复制其布局和显示设置，统计项仍与原页共享 / **Empty pages**: open the sidebar to add existing statistics, or open the statistics window when none exist. “Copy another page” copies its layout and display settings while sharing its statistics

## 界面与操作演示 / Screenshots & demos

### 空白页 / Empty page

顶部标签整理不同主题的页面；空白页提供打开统计窗口、添加图表和复制其他页面的入口。背景与网格保持固定，侧栏仍使用原生箭头开关。
Organize pages by topic with the top tabs. Empty-page actions open statistics, add charts or copy another page. The background and grid stay fixed, and the sidebar keeps its native arrow toggle.

![空白页与顶部标签 / Empty page and top tabs](https://raw.githubusercontent.com/FyisFe/DSP_Mods_fyyy/master/DashboardOverhaul/screenshots/dashboard-empty.png)

### 页面管理 / Page management

悬停查看完整页名和操作提示；单击切换、双击重命名、右键管理、拖动排序。
Hover for the full page name and controls: click to switch, double-click to rename, right-click to manage, or drag to reorder.

![页签悬停提示 / Page tab tooltip](https://raw.githubusercontent.com/FyisFe/DSP_Mods_fyyy/master/DashboardOverhaul/screenshots/tab-tooltip.png)

拖动页签调整顺序，再通过右键菜单重命名：
Reorder a page by dragging its tab, then rename it from the context menu:

![页签排序与重命名 / Reorder and rename page tabs](https://raw.githubusercontent.com/FyisFe/DSP_Mods_fyyy/master/DashboardOverhaul/screenshots/page-management.gif)

### 添加与整理图表 / Add and organize charts

从空白页打开统计窗口并添加图表，再通过图表右键菜单移动到其他页面。移动后的提示提供“前往”入口，方便继续整理目标页。
Open statistics from an empty page, add a chart, and move it to another page from its context menu. The move notice offers a shortcut to the destination page.

![添加图表与跨页移动 / Add a chart and move it between pages](https://raw.githubusercontent.com/FyisFe/DSP_Mods_fyyy/master/DashboardOverhaul/screenshots/chart-workflow.gif)
