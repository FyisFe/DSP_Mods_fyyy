# DarkFogSterilizer / 黑雾星系绝育

对星图中指定的一个恒星系执行一次清理，按游戏为该星系计算的巢穴上限，留下满额的绝育中枢核心。核心未完工、有物质、零能量，依靠原版规则维持停工，不需要持续拦截黑雾逻辑。

需要 BepInEx 5，限单人游戏；与 Nebula 联机插件互斥。当前编译、机制验证使用安装的游戏程序集 MVID `ee6dc40f-a6a2-4b39-b220-81c6de923db6`。

## 使用

1. 把 `bin/Release/DarkFogSterilizer.dll` 放入游戏或模组配置的 `BepInEx/plugins/DarkFogSterilizer/`，重启游戏。
2. 打开星图，选中目标恒星、行星或巢穴；也可以进入目标恒星/行星的近景。
3. 点击星图**上方中央的“绝育星系”按钮**，或按 **Ctrl+Alt+X**。游戏原生确认弹窗会显示星系名称、ID、清理范围和最终核心数量。
4. 点击“确认绝育”执行；“取消”或 Escape 不作修改。

快捷键可在 `BepInEx/config/org.fyyy.darkfogsterilizer.cfg` 中修改。操作不自动保存、不另存备份。执行后由玩家决定是否保存；需要恢复时读取操作前的存档。

## 清理范围

- 目标星系所有已生成行星工厂中的黑雾基地、建筑和部队。
- 目标星系巢穴的全部太空建筑、部队、中继站和火种，包括它们已发往外部的火种。
- 其他星系已派往目标星系的在途火种。
- 未访问行星的虚拟基地随所属中继站一起清除，无需加载行星。

之后使用原生 API 重建 `min(8, max(0, star.maxHiveCount))` 个巢穴，各保留一个绝育核心。原有巢穴等级由新建巢穴规则重新初始化。玩家建筑、矿物、已存在的地表坑洞和废墟不属于清理目标；其他星系只移除上述在途火种，并让其余火种刷新目标缓存。

## 原版机制

绝育对象必须是**未完工**的中枢核心，不是把已完工巢穴的能量简单归零。

设核心建造进度为 `sp`、总进度为 `spMax`，每步所需物质和能量为 `spMatter`、`spEnergy`。目标状态为：

```text
realized = true，isEmpty = false，核心实体存在
sp = 0 < spMax，state = 0
matter = spMatter > 0，energy = 0 < spEnergy
无其他建筑、部队、中继站、火种或在途补给
```

`EnemyBuilderComponent.LogicTick` 在未完工时必须同时满足物质、能量门槛才能推进。发电逻辑位于已完工分支，因此该核心不会自己恢复能量。

`DFTinderComponent.GenerateSortedStarIndices` 和 `PrepareDispatchLogic` 把没有在途火种的“已实体化、非空、未完工、物质不足”巢穴视为救援目标，也会救援死亡巢穴。上述核心仍存活且物质达到门槛，因而不需要救援。填满星系容量后，也没有可供殖民的空位。

地面中继站的运货船会通过 `DFRelayComponent.CarrierSailLogic` / `CarrierSailLogicVirtual` 请求重建失去的核心，并向存在的核心输送物质。已出发的火种则会在抵达时向核心注入能量和物质，因此仍须消除在途火种。模组直接构造最终状态，无需等待这一过程。

当前版本的每个火种还缓存星系权重和空轨道数量。操作结束时清除现有火种的这三项派遣缓存，让它们下次使用当前状态重新计算，避免旧的空位信息导致额外殖民。

数量采用原生 `StarData.maxHiveCount`，与实际火种派遣一致，不使用初始巢穴数量。`StarGen` 会结合最大密度、星系类型和种子取整计算这个值；黑洞和中子星使用两倍密度，物理轨道上限为 8。

## 实现与持久化

确认按钮把操作排到下一次 Unity `Update`，在完整模拟帧之间执行；执行前重新校验当前存档及星系容量。地面通过原生移除 API 先移除子建筑/部队，再移除基地；太空先移除敌人及来袭火种，让原生引用清理完成后再移除旧巢穴。新巢穴在空状态下实体化，仅创建核心。随后刷新星图和黑雾监视器。

没有 Harmony 补丁、额外存档字段或持续扫描。改变使用游戏原有存档字段，保存后可移除插件。玩家再次摧毁绝育核心、其他模组向它注入能量或改变巢穴容量，都可能使原版逻辑重新允许扩张。

## 构建与验证

在仓库根目录运行：

```powershell
dotnet build DarkFogSterilizer/DarkFogSterilizer.csproj -c Release
dotnet run --project DarkFogSterilizer/tests/Checks.csproj -c Release -- '<game-managed-dir>'
```

可通过 MSBuild 属性 `GameManagedDir`、`BepInExDir` 指定本地依赖路径。检查程序直接调用游戏 DLL 的建造、序列化和火种目标计算方法，覆盖：零能量长期停工、物质门槛边界、原生建造组件序列化往返、未满/满额派遣差异、8 轨道上限、在途火种与其他星系的清理边界，以及缓存刷新。

离线检查不包含 Unity 场景中的完整清理、渲染、整份存档读写及其他模组兼容性。
