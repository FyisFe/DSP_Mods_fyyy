# BlueprintSearch

Adds a search bar to the blueprint browser window.

为蓝图库窗口添加搜索栏。

![demo](https://raw.githubusercontent.com/FyisFe/DSP_Mods_fyyy/3a7147f7f2c7f8d5e1463fc50c53469d8c625255/BlueprintSearch/demo.png)

## Features / 功能

- Search recursively across all folders / 跨所有文件夹递归搜索
- Right-click on a result to jump to its containing folder / 右键点击搜索结果跳转到其所在文件夹
- Left-click on the result to use / 左键点击结果即可使用
- Progressive rendering: all matches are shown, streamed in batches across frames so typing stays snappy even on large libraries / 分帧渐进渲染：显示全部匹配结果，分批写入以保证大库下打字依然流畅

## Configuration / 配置

Config file: `BepInEx/config/org.fyyy.blueprintsearch.cfg`

配置文件：`BepInEx/config/org.fyyy.blueprintsearch.cfg`

- `Enabled` (default `true`) — enable/disable the search bar at runtime / 运行时启用或禁用搜索栏
- `MaxResults` (default `0`) — hard cap on total search results. `0` = unlimited. Results are rendered progressively across frames, so this cap is only needed for libraries so large that even streamed rendering is undesirable / 搜索结果的硬上限。`0` 表示不限。结果会分帧逐步渲染，所以通常不需要设上限；仅当蓝图库巨大、连流式渲染也觉得累赘时才设正数
- `DebounceMs` (default `120`) — milliseconds to wait after the last keystroke before recomputing results / 最后一次按键后等待多少毫秒才重算结果

## Install / 安装

Install via R2Modman or extract the DLL to `BepInEx/plugins/`.

通过R2Modman安装，或将DLL解压到 `BepInEx/plugins/`。

## Requirements / 依赖

- BepInEx 5.x
