# 内置咕嘎模型

Icarus Model Replacement 随 Mod 提供这个模型，并默认启用。切换模型的方法见 Mod 的 README。

原作是 ReedMan 的 [Cute Gugugaga](https://sketchfab.com/3d-models/cute-gugugaga-741280967ece40e395a70070d8b31132)，按 [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) 使用。FyisFe 为 DSP 调整了尺寸、朝向、网格平滑、骨骼权重和材质，并添加行走、飞行动作配置。模型保留原造型、UV 和 4096×4096 贴图。分享或修改时请保留作者署名、原作及许可链接，并说明自己的修改。

## 模型文件

安装后，DLL 旁的 `model/` 包含 `model.json`、`mesh.bin.gz`、`texture.png` 和本说明。`model.json` 定义尺寸、朝向、材质、骨骼与动作，修改后重新读档或切换一次模型即可重新加载，无需编译 DLL。

## 制作与验证

制作文件位于仓库的 `GuguGaga/` 目录。`art/source.glb` 是原作 GLB，`art/GuguGaga.blend` 是已打包贴图的 Blender 5.2 工程。`art/model.py` 从原作生成适配工程。尺寸、材质和动作统一在 `model/model.json` 中编辑，`art/export.py` 保留这些设置，更新网格、贴图和骨骼数据，并生成预览。Mod 的 Release 构建将运行时资源打入同一个 ZIP。

`art/poses.py` 检查配置驱动的蒙皮姿态。制作脚本生成的预览 PNG 和 `art/model-stats.json` 不纳入版本管理，需要时可重新生成。预览使用 Blender 灯光，游戏内的受光效果需要另行检查。

模型没有专用的表情、采矿或战斗动画。特效位置和功能范围见 Mod 的 README；模型格式及通用导出方法见 `MODEL_FORMAT.md`。
