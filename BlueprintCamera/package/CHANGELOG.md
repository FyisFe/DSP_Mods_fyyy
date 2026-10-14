# Changelog

## 0.2.1

- 简单摄影新增拉近、拉远和自动构图按钮；鼠标位于游戏画面时可直接用滚轮调整距离。
- 距离调节暂停预览并保留当前环绕角度；确认手动起点、修改时长或开始拍摄时保留调整后的距离。
- 自动构图只在进入摄影或主动点击按钮时应用；拍摄期间忽略滚轮距离调节。

- Add near/far/auto-frame controls and viewport mouse-wheel zoom to simple photography.
- Pause preview at its current angle when zooming, and retain distance when accepting a manual start, changing duration or capturing.
- Auto-frame only on entry or explicit request; ignore wheel zoom during capture.

## 0.2.0

- 默认改为简单的整颗星球拍摄面板：选择一圈时长、预览、拍摄；关键帧、坐标与文件管理收进高级编辑。
- 新增自动构图的星球大圆环绕，保持距离、注视星心，按 30 / 60 / 120 秒一圈无停顿匀速循环，支持反向。
- 简单拍摄使用临时镜头，可在高级编辑保存到摄影库；预览和拍摄按钮固定显示。
- 圆形路线直接按角度匀速播放，避免距离表累计误差导致正反向进度偏移。

- Open with simple planet-orbit controls; move keyframes, coordinates and files into advanced editing.
- Automatically frame the planet and loop at constant distance and speed, with 30/60/120-second revolutions and either direction.
- Keep simple shots temporary until explicitly saved; keep preview and capture buttons visible.
- Use exact angular progress for circular routes to avoid distance-table accumulation drift.

## 0.1.1

- 修复鼠标指向地面、建筑或资源时，普通浏览被误判为操作模式，导致 Alt+F8 无法进入摄影的问题。
- 区分窗口阻挡和操作模式提示；拾取模式需先退出。

- Allow photography while hovering over terrain, buildings or resources during normal gameplay.
- Distinguish window and interaction-mode entry messages; require leaving pickup mode.

## 0.1.0

- 新增行星摄影模式、自由相机、构图锁定和带缩略图的机位收藏。
- 新增固定、直线、环绕、球面和曲线路线，以及推近／拉远预设。
- 新增距离匀速、手动机位时间、独立朝向、FOV、缓动、首尾停留、反向与循环播放。
- 新增时间轴预览、机位编辑与撤销重做、倒计时净画面播放、JSON 导入导出和摄影库自动保存。

- Add planet photography, free camera, locked composition and thumbnail bookmarks.
- Add stationary, linear, orbital, spherical and curved routes, plus a dolly preset.
- Add distance-based speed, timed keys, independent aiming, FOV, easing, holds, reverse and looping playback.
- Add scrubbing, key editing, undo/redo, clean countdown playback, JSON import/export and automatic library storage.
