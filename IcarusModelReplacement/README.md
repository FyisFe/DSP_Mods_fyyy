# Icarus Model Replacement

![Cute Gugugaga in Dyson Sphere Program](https://raw.githubusercontent.com/FyisFe/DSP_Mods_fyyy/2d9f74c3bbcd8423be25a70fbca3d6c83c40dae8/IcarusModelReplacement/assets/gugugaga.png)

<details>
<summary>中文看我</summary>

将伊卡洛斯的外观替换为咕嘎。模型随 Mod 提供，默认启用，进入存档即可使用。也支持自定义模型，换模型不需要重新编译 DLL。

### 配置项

首次启动后会生成 `BepInEx/config/org.fyyy.icarusmodelreplacement.cfg`。只使用内置咕嘎时，无需修改配置。

`[Model]` 下的 `Directory` 决定使用哪个模型：

| 值 | 效果 |
|---|---|
| `builtin`（默认） | 使用内置咕嘎 |
| 模型文件夹路径 | 使用自定义模型；支持相对 `BepInEx/plugins/` 的路径或绝对路径 |
| 留空 | 恢复原版伊卡洛斯 |

编辑配置文件后重启游戏生效。使用配置管理器修改这一项，可以直接在游戏中切换。升级会保留已有配置。

### 功能与限制

角色跟随伊卡洛斯行走、悬浮和飞行，只替换外观，不修改碰撞、存档或战斗逻辑。机甲编辑器保留原有功能，喷焰、护盾和武器特效仍使用伊卡洛斯的骨骼位置。

模型加载失败时会记录日志并保留伊卡洛斯，切换模型或重新读档后重试。停用插件、清空选择或退出存档时会恢复原模型。请勿与旧版 `GuguGaga.dll` 同时启用。

内置模型没有专用的表情、采矿或战斗动画。自定义模型支持一个网格、一张 PNG 图集、一个材质和最多 256 根骨骼，每顶点最多四项权重。骨骼和动作由模型文件定义，制作方法见 [MODEL_FORMAT.md](MODEL_FORMAT.md)。

### 模型来源

咕嘎原作为 ReedMan 的 [Cute Gugugaga](https://sketchfab.com/3d-models/cute-gugugaga-741280967ece40e395a70070d8b31132)，采用 [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) 许可。
</details>

<details>
<summary>README</summary>

Replaces Icarus with Cute Gugugaga. The model is included and enabled by default when you load a save. Custom models are also supported, with no DLL rebuild needed to switch characters.

### Configuration

The first launch creates `BepInEx/config/org.fyyy.icarusmodelreplacement.cfg`. You can leave it unchanged to use the included Gugugaga.

`Directory` in the `[Model]` section selects the model:

| Value | Effect |
|---|---|
| `builtin` (default) | Use the included Gugugaga |
| Model folder path | Use a custom model, with a path relative to `BepInEx/plugins/` or an absolute path |
| Empty | Restore the original Icarus |

Restart the game after editing the file. Changes made through a configuration manager apply in game. Updates preserve your existing configuration.

### Features and limits

The character follows Icarus while walking, hovering and flying. The mod changes appearance without modifying collision, save data or combat logic. The mecha editor keeps its existing functions; thrusters, shields and weapon effects still use Icarus's bone positions.

If a model fails to load, the mod logs the error and keeps Icarus visible. Changing the selection or reloading the save retries loading. Disabling the plugin, clearing the selection or leaving the save restores the original model. Do not enable it alongside the old `GuguGaga.dll`.

The included model has no dedicated facial, mining or combat animations. Custom models support one mesh, one PNG atlas, one material and up to 256 bones, with at most four weights per vertex. Model files define the skeleton and motion. See [MODEL_FORMAT.md](MODEL_FORMAT.md) for the export format.

### Model credits

The original model is [Cute Gugugaga](https://sketchfab.com/3d-models/cute-gugugaga-741280967ece40e395a70070d8b31132) by ReedMan, licensed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).

</details>
