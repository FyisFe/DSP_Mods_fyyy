# DashboardOverhaul

为游戏内**仪表盘**提供最多 9 个可切换页面。页面、页名及顺序随存档保存。
Adds up to nine switchable Dashboard pages. Pages, names and order persist in the save.

## 使用 / How to use

- **页面**：单击顶部页签切换；双击页签或从右键菜单重命名；拖动页签排序。悬停可查看完整页名和操作提示。/ **Pages**: click a top tab to switch, double-click or choose Rename from its right-click menu, and drag to reorder. Hover for the full page name and controls.
- **新建页面**：点击 `+` 在末尾添加空白页；达到 9 页上限时按钮禁用并显示原因。/ **Add a page**: click `+` to append an empty page. At the nine-page limit, the button is disabled with an explanation.
- **删除页面**：右键页签选择“删除页面”。有图表时确认框显示页名及图表数量；统计项仍保留在侧栏。最后一页不能删除。/ **Delete a page**: choose Delete page from its right-click menu. If it has charts, a confirmation shows the page name and chart count; statistics remain in the sidebar. The last page cannot be deleted.
- **侧栏**：使用游戏原生箭头开合侧栏。侧栏覆盖图表，图表、背景和网格保持原位；页签位于图表及侧栏菜单上方。/ **Sidebar**: use the game's native arrow to toggle it. The sidebar overlays charts while the charts, background and grid stay in place; tabs remain above charts and sidebar menus.
- **空白页**：没有统计项时可打开统计窗口；已有统计项时可打开侧栏添加图表。有其他非空页面时，可复制其布局和显示设置；复制的图表与原页共用统计项。/ **Empty pages**: open the statistics window if no statistics exist, or open the sidebar to add charts from existing statistics. If another page has charts, copy its layout and display settings; the copied charts share their statistics with the source.
- **移动图表**：右键图表选择“移动到页面”和目标页；完成后的“前往”可切页并高亮该图表。目标页没有空位时，图表留在原页。/ **Move a chart**: choose Move to page and a destination from the chart menu. The resulting Go to page action switches pages and highlights the chart. If there is no room, the chart stays on its original page.
- **重命名统计项**：右键图表选择“重命名统计项”，或双击图表标题；菜单或提示显示关联图表数量。该统计项的图表标题、提示及侧栏名称同步更新。/ **Rename a statistic**: choose Rename statistic from the chart menu or double-click its title. The menu or notice shows the affected chart count; chart titles, tooltips and the sidebar name update together.
- **移除图表或删除统计项**：图表菜单中的“移除此图表”只移除当前图表；“删除统计项及其全部图表”会确认名称和影响数量，并移除其他页面、监控视图中的关联图表及侧栏统计项。/ **Remove a chart or delete a statistic**: Remove this chart affects only the current chart. Delete statistic and all its charts confirms the name and affected count, then removes associated charts on other pages and in watch views, along with the sidebar statistic.
- **编辑名称**：Enter 提交，Esc 取消；失焦或切页提交，关闭仪表盘或回收目标图表时取消未完成的编辑。/ **Edit names**: Enter commits and Esc cancels. Losing focus or switching pages commits; closing the dashboard or recycling the target chart cancels unfinished edits.

## 界面与操作演示 / Screenshots & demos

### 空白页 / Empty page

没有统计项时，空白页引导打开统计窗口；已有统计项时，可打开侧栏添加图表。有其他非空页面时，“复制其他页面”才可用。
With no statistics, the empty page opens the statistics window. When statistics exist, it opens the sidebar to add charts. Copy another page becomes available when another page has charts.

![空白页与顶部标签 / Empty page and top tabs](https://raw.githubusercontent.com/FyisFe/DSP_Mods_fyyy/d766eb82171c90ddf4128f290a3e8a4bb4a248bf/DashboardOverhaul/screenshots/dashboard-empty.png)

### 页面管理 / Page management

悬停页签可查看完整页名和操作提示。
Hover over a tab for its full name and controls.

![页签悬停提示 / Page tab tooltip](https://raw.githubusercontent.com/FyisFe/DSP_Mods_fyyy/d766eb82171c90ddf4128f290a3e8a4bb4a248bf/DashboardOverhaul/screenshots/tab-tooltip.png)

拖动页签调整顺序，再从右键菜单重命名。
Drag a tab to reorder the pages, then rename it from the right-click menu.

![页签排序与重命名 / Reorder and rename page tabs](https://raw.githubusercontent.com/FyisFe/DSP_Mods_fyyy/d766eb82171c90ddf4128f290a3e8a4bb4a248bf/DashboardOverhaul/screenshots/page-management.gif)

### 图表操作 / Chart actions

从空白页打开统计窗口、添加图表并移至另一页；移动提示可前往目标页。演示末尾显示删除统计项的确认。
Open statistics from an empty page, add a chart and move it to another page. The move notice offers navigation to the destination; the demo ends with a statistic-deletion confirmation.

![添加图表、跨页移动和删除统计项 / Add a chart, move it and delete its statistic](https://raw.githubusercontent.com/FyisFe/DSP_Mods_fyyy/d766eb82171c90ddf4128f290a3e8a4bb4a248bf/DashboardOverhaul/screenshots/chart-workflow.gif)
