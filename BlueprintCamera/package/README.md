# BlueprintCamera / 蓝图摄影

打开即用的整颗星球匀速环绕，为 OBS 等外部录像软件播放净画面镜头。高级编辑支持固定机位、直线横移、推近／拉远、工厂局部环绕、球面巡航和多点曲线。

目标游戏版本：**Dyson Sphere Program 0.10.35.29145**。依赖 BepInEx 5。当前已完成构建、数学与播放测试和原版 DLL 接口核对；游戏内画面和其他模组兼容性尚待实测。

## 安装

将 `BlueprintCamera.dll` 放入所用配置的 `BepInEx/plugins/BlueprintCamera/` 后重启游戏。与 `starfi5h.CameraTools` 互斥，请在该配置中停用 CameraTools；两者同时控制主相机会冲突。

本模组只在当前已加载工厂的行星上工作。先关闭星图、截图工具等窗口，退出建造、拆除、拾取或物品操作模式。普通浏览时，鼠标指向地面、建筑或资源均可进入摄影。

## 绕整颗星球拍摄

1. 在行星上按 **左 Alt+F8**。相机会自动拉远，让整颗星球进入画面。
2. 用 **拉近 / 拉远** 按钮或画面上的**鼠标滚轮**调整距离，选择一圈 **30 / 60 / 120 秒**，点击 **① 预览环绕** 查看效果。
3. 启动 OBS 等外部录屏，再点 **② 开始拍摄**。倒数 3 秒后开始匀速环绕，界面、机甲和鼠标自动隐藏。
4. 按 **Esc** 停止拍摄并找回面板；再按一次退出摄影。

默认 60 秒一圈，保持距离、始终看向星球中心，无停顿连续环绕。可以切换左右方向。起点在进入摄影时相机所在的那一侧；环绕沿过星心的大圆进行，平面由起点视角决定。需要改起点时展开“调整环绕起点…”，移动后点击“使用当前位置”。

滚轮向下拉远、向上拉近；鼠标放在面板上时只滚动面板。调整距离会暂停预览并保留当前角度，继续预览或拍摄会沿用新距离。“使用当前位置”也会保留手动调整后的距离；只有重新进入摄影或点“自动构图”才会重新适配整颗星球。拍摄期间滚轮不改变环绕距离。

模组不生成视频文件，录制由外部软件完成。默认面板没有坐标、关键帧或路线文件设置；这些功能在“高级…”中。简单拍摄使用临时镜头，不占用摄影库；需要保留时，在高级编辑点击“保存此镜头”。

## 快捷键与高级编辑

| 默认快捷键 | 操作 |
|---|---|
| `左 Alt+F8` | 进入／退出摄影 |
| `左 Alt+F7` | 自由相机／锁定当前构图 |
| `左 Alt+F9` | 播放／暂停当前镜头 |
| `左 Alt+F10` | 回起点，倒计时并播放净画面镜头 |
| `Tab` | 收起／显示面板 |
| `Esc` | 拍摄或播放时暂停并恢复面板；再次按下退出摄影 |
| `Ctrl+Z` / `Ctrl+Y` | 撤销／重做当前路线编辑 |
| `WASD` / `QE` | 自由相机前后左右／上下移动 |
| 鼠标中键拖动 | 转向 |
| 滚轮 / `左 Alt+滚轮` | 推拉／调整 FOV |
| `左 Shift` / `左 Ctrl` | 加速／减速移动 |

快捷键和面板缩放可在 `BepInEx/config/org.fyyy.blueprintcamera.cfg` 中修改。在文本框内输入时不触发摄影快捷键；`Esc` 先结束文本输入。鼠标位于面板内时，自由相机不会响应移动。

## 高级：拍摄工厂局部半圈环绕

1. 进入摄影，点“高级…”→“摄影库 / 文件”→“新建镜头”。切换自由相机，移动到满意的斜俯视画面。
2. 在“路径机位”选中首机位，点击“用当前构图更新”。
3. 在“镜头”选择“局部环绕”和“注视目标”，用“目标取画面中心”选取建筑碰撞体或地面，也可以填写目标坐标。
4. 设置角度 `180`、移动时长 `20`、首尾停留各 `2` 秒，点击“应用参数”。半径与高差相对目标；环绕轴是目标所在地的竖直方向。目标升降按钮可调整注视高度。
5. 拖动进度查看构图，点击播放预览。启动外部录屏，再按 `Alt+F10`。

拍摄会隐藏 HUD、机甲、鼠标、面板和轨迹。倒计时数字结束后开始起点停留，随后移动，终点停留结束后恢复面板。窗口失去焦点会暂停镜头，因此应先启动外部录屏，再回到游戏开始拍摄。

## 路线与机位

- **固定**：位置保持在首机位。单个机位可用于固定画面录像；多个朝向关键帧可原地转向或变焦。
- **整颗星球**：围绕星心沿大圆运动；“注视目标”对准星心，不需要填写目标坐标。简单面板会自动构图，高级编辑保留手动机位的位置和视野。
- **直线**：逐段连接机位。转角处方向可能突变，需要连续转弯时选择曲线。
- **局部环绕**：从首机位出发，围绕目标竖直轴旋转。正负角度控制方向；位置使用首机位，其他机位的角度不会改变环绕路线。
- **球面**：机位之间沿球面短弧移动，径向高度线性插值。沿整圈巡航需添加中间机位。不会简单线性插值经纬度。
- **曲线**：平滑通过所有机位；曲线可能偏离机位间的直线，应预览路线。
- **推拉预设**：在“路径机位”填写移动距离，生成沿当前视线的两个机位；负值拉远。

点击路径机位会恢复该构图，可以修改名称、位置、FOV，或用当前自由相机构图更新。追加、删除、重排会重新平均分配时间；这些操作可撤销。收藏机位独立保存名称、构图和缩略图，可恢复或加入路线。

## 朝向、速度与播放

朝向可选关键帧插值、相对拍摄区域固定方向、相对地面保持朝向、注视目标、沿路线前看。注视与沿路前看采用连续的最小扭转朝向，避免垂直视角翻转；大范围移动时不保证屏幕始终朝北。相对地面模式以首机位的地面方向为参照。

“按路径距离匀速”根据实际路程分配移动时间，曲线采用自适应距离表近似。中间机位不会自动停车。关闭后，按机位的到达时间播放；原地停留或转向可用同位置机位和不同时间表达。首尾机位时间应为 `0` 和镜头总时长。缓动会改变到达时间，因此精确手动计时应将缓动设为 `0`。

“固定视野”使用首机位的 FOV，关闭后在机位之间插值。缓动设置为首尾各自的加减速时长，设为 `0` 即关闭。

播放按现实时间推进，与游戏 UPS、加速倍率分开。暂停镜头不暂停工厂；重复镜头也不会回放工厂历史。实时录像的掉帧仍取决于游戏和录屏性能。

支持一次、跳回重播、往返和无缝循环。无缝循环要求首尾位置、朝向、FOV 与移动方向吻合，并关闭首尾停留和缓动；完整 `360°` 环绕可用于无缝循环。反向播放保留原镜头的构图方向。

## 保存与分享

已加入摄影库的镜头及其编辑自动保存至 `BepInEx/config/BlueprintCamera/library.json`。临时镜头需要先点“保存此镜头”或“保存到摄影库”，否则退出后不保留。每条镜头绑定星系种子、行星 ID 和半径，防止在其他地点意外播放。同一星系和行星的其他存档可以复用，但不会检查建筑是否仍存在。

“摄影库 / 文件”支持新建、复制、删除、手动保存，以及剪贴板 JSON 导入导出。文件导出目录是同级 `exports/`；填写单个 `.json` 文件名，导出不会覆盖已有文件，导入会新增镜头。上限为 128 条路线、每条 128 个机位、256 个收藏机位，单文件最多 2 MiB。

摄影库采用临时文件后替换的方式保存；若读取失败，本次会话禁用库写入，保留原文件，仍可导出新镜头。缩略图存放在 `thumbnails/`，删除收藏不会自动清理图片。

## 范围与限制

- 只接管摄影相机、输入和显示，不修改游戏存档格式或工厂数据，也不会搬动机甲。进入摄影前已有的游戏行为仍会继续。
- 退出时恢复正常相机、原有 HUD 和机甲显示状态。切换存档、离开当前行星、打开暂停菜单或原生模态窗口时退出摄影。
- 路径预览会检查相对球面的高度，穿入球面的路线不能播放；这不包含地形和建筑碰撞检测，不会自动绕开遮挡。
- 目标取点使用游戏建筑碰撞边界和行星球面，不是逐三角形选取，也不包含地形高度；可编辑目标坐标校正。轨迹以青色显示，目标用橙色竖线标记。
- 不支持星际自由飞行、蓝图范围自动识别、跨行星路线迁移、光照控制、世界回放或视频编码。

---

## English

BlueprintCamera opens with a simple whole-planet orbit panel. Press left Alt+F8, adjust distance with the near/far buttons or mouse wheel over the viewport, choose 30/60/120 seconds per revolution, preview, then start your external recorder and click capture. The camera initially frames the planet and loops at constant speed while facing its center. Zoom pauses preview at its current angle; accepting a manual start and capturing retain the chosen distance. Auto-frame explicitly to reset framing. Capture ignores wheel zoom and hides the interface after showing a three-second countdown; Esc brings the panel back. Simple shots are temporary; use “高级…” and “保存此镜头” to keep one in the library.

Advanced editing provides free camera, locked views, named thumbnail bookmarks, linear/dolly/local-orbit/spherical/curved paths, independent aiming, FOV, distance-based motion, timed keys, easing, holds, reverse playback and loops. Its in-game interface is Chinese.

Install `BlueprintCamera.dll` in `BepInEx/plugins/BlueprintCamera/`. Requires BepInEx 5; disable CameraTools in the same profile. The build targets DSP **0.10.35.29145**. Build, offline math/playback tests and native DLL API checks are complete; actual game visuals and mod compatibility require in-game testing.

Use left `Alt+F8` to enter/exit, `Alt+F7` to toggle free/locked camera, `Alt+F9` to play/pause and `Alt+F10` for clean countdown playback. `Tab` toggles the panel. `Esc` pauses playback first, then exits. Free movement uses WASD/QE, middle-mouse rotation, wheel dolly, Alt+wheel FOV, Shift for faster movement and Ctrl for slower movement. Controls and UI scale are configurable in the BepInEx config file.

Capture mode hides the HUD, mecha, cursor and path guides, counts down, holds at the start, plays, then holds at the end. Start your external recorder before triggering capture. Losing focus pauses the shot. Playback follows real time independently of game simulation; it cannot repair recording frame drops or replay earlier factory states.

Paths and bookmarks persist in `BepInEx/config/BlueprintCamera/library.json`, bound to galaxy seed, planet ID and radius. JSON exports use the `exports/` subdirectory and never overwrite an existing file. Import creates a new shot. Invalid libraries remain untouched and disable saving for that session. Thumbnails use `thumbnails/`.

Spherical paths follow the short arc between keys and interpolate altitude. Curved routes use an adaptive distance table. Look-at/travel orientation uses continuous transport to avoid overhead flips. Seamless loops require matching endpoints, orientation, FOV and travel direction, with no easing or holds. Paths crossing the planet sphere are rejected for playback; terrain/building collision avoidance is not included. Exiting restores the camera and display state. Space cameras, world replay, lighting controls and video encoding are outside this version.
