# 模型包格式 1

每个模型使用一个文件夹，包含 `model.json`、`mesh.bin.gz`、`texture.png` 和作者说明。内置咕嘎也使用这个格式。自定义模型可以单独分享，请保留署名、来源和许可证。文件名固定，JSON 不指定其他文件路径，也不包含脚本或可执行代码。

## Blender 导出

在 Blender 5.2 中完成模型后，用 `tools/export_model.py` 导出。工具接收一个网格和一个材质，Principled BSDF 的 Base Color 需直接连接 PNG 图集，UV 必须在 0..1 内。多个物件或材质需要先合并并烘焙为图集。模型放在世界原点附近，Z 向上、面朝 -Y，脚底位于 Z=0。

骨骼名称和树形层级由作者决定。导出使用静止姿态，每顶点最多保留四个骨骼权重；静态模型会自动获得一根 `Body` 骨骼。骨骼轴心按世界坐标导出，运行时的静止骨骼轴与模型坐标轴平行，因此动作旋转轴不沿 Blender 的骨骼 roll。

```powershell
blender --background MyModel.blend --python export_model.py -- --object Body --settings settings.json --output MyModel
```

用 `--scene 场景名` 指定场景。衣物需要双面显示时，加 `--double-sided`，工具会复制反向三角形与法线。也可以在 Blender Python 中调用 `export_model(obj, directory, settings, double_sided=False)`。导出不会修改原网格。

`settings.json` 使用下面的 JSON 字段。`format` 和 `bones` 由工具生成，可以省略，也可以直接用已有的 `model.json` 作为配置输入。完整例子见仓库的 `GuguGaga/model/model.json`。构建 `tests/Checks.csproj` 后，运行 `Checks.exe 模型目录` 可检查导出结果；不传参数时运行格式和动作契约检查。

## JSON

```json
{
  "format": 1,
  "name": "My Character",
  "author": "Author",
  "license": "License identifier or terms",
  "scale": 1,
  "offset": [0, 0, 0],
  "rotation": [0, 0, 0],
  "boundsPadding": 0.5,
  "material": {"smoothness": 0.2, "metallic": 0, "emission": 0, "specularHighlights": true},
  "bones": [{"name": "Body", "parent": -1, "position": [0, 0, 0]}],
  "motions": [{"target": "Body", "signal": "Step", "position": [0, 0.05, 0]}]
}
```

必填字段：`format`、`name`、`author`、`license`、`scale`、`bones`。`source` 可填写原作链接。其他字段可省略：向量默认零，材质数值默认零、镜面高光默认关闭，motions 默认空数组。

向量为三个有限浮点数，坐标采用 Unity 的 X 向右、Y 向上、Z 向前，由 Blender `(x,z,-y)` 转换。长度为模型单位，旋转为角度。`scale` 是模型统一缩放；`offset` 和 `rotation` 调整整个模型与伊卡洛斯根节点的相对位置和朝向。

`bones` 数组顺序就是网格骨骼索引。名称区分大小写、唯一且不超过 128 字符；`$root` 保留给整个模型。`parent=-1` 表示直接挂在模型根节点，其余父索引必须小于当前索引。`position` 为静止轴心的绝对模型坐标，运行时由父子轴心之差得到局部位置。

`motions` 每项包含必填的 `target`、`signal`，以及可选的 `position`、`rotation` 向量。目标为骨骼名称或 `$root`，向量乘以状态值后累加到该目标。同目标旋转先累加欧拉角，再按 Unity `Quaternion.Euler` 的 Z、X、Y 顺序构成四元数；骨骼动作作用于其父坐标系。根动作旋转在 `rotation` 定义的基础朝向后应用，根位移位于伊卡洛斯父坐标系。

| 信号 | 值 |
| --- | --- |
| `Constant` | 1，用于常驻姿态 |
| `Stride` | sin(原版行走相位) × 行走权重 × (1−腾空权重) |
| `Step` | abs(Stride) |
| `LeftStep` | max(0, Stride) |
| `RightStep` | max(0, −Stride) |
| `Air` | 原版悬浮、飞行、航行权重之和，限制到 0..1 |
| `Sail` | 原版宇宙航行权重，限制到 0..1 |

`boundsPadding` 给静止网格包围盒的每侧扩展指定距离，需覆盖骨骼动作产生的最大位移。根节点整体运动不需要加入该扩展。作者应检查行走两端、腾空和混合姿态，避免模型被错误裁剪。

材质使用同一张图集作为底色和可选的自发光贴图，`emission` 表示线性强度。Mod 会为 Standard 材质补入 DSP 环境色，模型作者仍需在游戏中检查受光效果。贴图按 sRGB 加载，使用 mipmap、三线性过滤和 Clamp。

## 二进制与限制

`mesh.bin.gz` 解压后采用小端序：

| 内容 | 类型 |
| --- | --- |
| 标识和版本 | 4 字节 `IRMD`，uint32 版本 1 |
| 顶点数和索引数 | 两个 uint32 |
| 每个顶点 | 8 个 float32：位置 XYZ、法线 XYZ、UV；4 个 uint8 骨骼索引；4 个 float32 权重，共 52 字节 |
| 三角形索引 | uint32 数组，每三个形成一个三角形 |

骨骼、材质、尺寸和动作只写在 JSON 中。顶点坐标和配置向量分量限制在 ±1000；scale 为 0.001..100，boundsPadding 为 0..100。权重按降序排列，总和为 1；法线长度为 1，未使用的权重及索引填零。文件结尾不允许附加数据。

加载器在分配网格前验证限制：1..256 根骨骼、最多 1024 个动作绑定、250,000 个顶点、1,500,000 个三角形索引。JSON 最大 1 MiB，压缩网格最大 32 MiB，PNG 最大 64 MiB，宽高分别不超过 8192 且总像素不超过 16,777,216。smoothness/metallic 为 0..1，emission 为 0..2。无效模型会被拒绝，保留伊卡洛斯。

这些上限用于限制资源占用和校验格式，实际帧率需要进游戏测量。格式 1 只支持图集材质和状态驱动的骨骼动作，不能直接读取 GLB、FBX 或 VRM，也不支持多材质、表情形态键或模型脚本。
