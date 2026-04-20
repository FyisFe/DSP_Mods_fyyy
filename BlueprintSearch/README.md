# BlueprintSearch

Adds a search bar to the blueprint browser window. Type to filter blueprints across your entire library by path.

为蓝图库窗口添加搜索栏。输入关键词即可在整个蓝图库中按路径过滤蓝图。

## Features / 功能

- Search recursively across all folders, not just the current one / 跨所有文件夹递归搜索，不限于当前文件夹
- Case-insensitive, space-separated AND tokens — e.g. `初期 电力` matches any path containing both words / 不区分大小写，空格分隔的 AND token —— 例如 `初期 电力` 会匹配同时包含两个词的路径
- `/` and `\` also work as token separators, so pasted path fragments search naturally / `/` 和 `\` 同样作为 token 分隔符，粘贴路径片段可直接搜索
- Right-click on a result to jump to its containing folder / 右键点击搜索结果即跳转到其所在文件夹
- Toolbar buttons (cut/new-file/new-folder/up-level) are disabled while searching / 搜索时工具栏按钮（剪切/新建文件/新建文件夹/返回上级）自动禁用
- 120 ms typing debounce to avoid rebuilding the grid on every keystroke / 120 毫秒输入防抖，避免每次按键都重建网格
- Localized placeholder (English / Simplified Chinese) / 本地化占位符（英文 / 简体中文）

## Configuration / 配置

Config file: `BepInEx/config/org.fyyy.blueprintsearch.cfg`

配置文件：`BepInEx/config/org.fyyy.blueprintsearch.cfg`

- `Enabled` (default `true`) — enable/disable the search bar at runtime / 运行时启用或禁用搜索栏
- `MaxResults` (default `48`) — maximum number of matches shown. Lower = snappier typing, higher = more results per query / 搜索结果的最大显示数量。越小越流畅，越大结果越全
- `DebounceMs` (default `120`) — milliseconds to wait after the last keystroke before recomputing results / 最后一次按键后等待多少毫秒才重算结果

## Install / 安装

Install via R2Modman or extract the DLL to `BepInEx/plugins/`.

通过R2Modman安装，或将DLL解压到 `BepInEx/plugins/`。

## Requirements / 依赖

- BepInEx 5.x
