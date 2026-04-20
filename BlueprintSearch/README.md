# BlueprintSearch

Adds a search bar to the blueprint browser window.

为蓝图库窗口添加搜索栏。

## Features / 功能

- Search recursively across all folders / 跨所有文件夹递归搜索
- Right-click on a result to jump to its containing folder / 右键点击搜索结果跳转到其所在文件夹
- Left-click on the result to use / 左键点击结果即可使用

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
