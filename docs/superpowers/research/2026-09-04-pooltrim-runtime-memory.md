# PoolTrim：全程内存优化调研

日期：2026-09-04。主体记录源码与历史测量调研；当日追加的900万糖读档复测见文末。200万糖的完整快照及保存重读对照见[实测报告](2026-09-04-loadmem-200w-results.md)。

## 结论

最值得深化的主线是：**消除运行期路径容量滞留 → 货物 GPU 镜像按需分配 → 远端传送带几何无损冷存**。前两项适合小步交付，第三项有最大的已知 RAM 空间，但必须先验证压缩率、恢复开销和远端编辑契约。

PoolTrim 1.0.0 在读档时裁剪的数组会持续保持较小容量，因此已经降低运行期常驻内存；局限是后续拆建仍会重新积累容量。继续加定时 GC，不能解决仍被对象池引用的数据。

| 方向 | 可处理的数据规模 | 建议 |
|---|---|---|
| 释放废弃 CargoPath；对编辑后严重过大的活路径定向裁剪 | 每个闲置点 29 B；本次未测运行期累积量 | 第一批，修复增长来源 |
| 非本地工厂 Cargo GPU 镜像延迟创建、离开释放 | 历史样本总 GPU 逻辑分配 3.02 GiB，扣除本地部分 | 第一批，独立验证显存收益 |
| 非本地 CargoPath 几何无损冷存 | 历史样本几何原始总量 18.63 GiB，扣除热数据与压缩载荷后才是净节省 | 主要深挖方向 |
| 实体与少数大组件池尾部裁剪 | 历史报告实体尾部 slack 4.04 GiB；布局和数量需在新运行中重测 | 第二批，专用实现 |
| DysonShell Mesh 的原生 CPU 副本释放 | 逻辑载荷约 38 B/已生成顶点；1 亿顶点约 3.54 GiB | 小型候选，先确认渲染兼容性 |
| DysonSwarm 保存快照释放、低利用率容量整理 | 快照 32 B/容量；另有 GPU 和队列容量冗余 | 帆群档按测量优先 |

这些数值不是可相加的收益承诺。GPU 分配不等于进程提交；18.63 GiB 是几何原始总量，也不是压缩后的净节省。

## 依据与历史基线

本次核对了 `GameCode-latest` 与当前安装的 `Assembly-CSharp.dll`：

- 源码与 DLL MVID 都是 `ECE4A40E-5E73-43F4-A9F8-4E74970B5942`，通过 Mono.Cecil 读取 DLL 元数据核实。
- DLL SHA-256：`AE0BA95F75BD879A62AA4CE253B2AB78EAA4FB3C7C595F5E1FEE75EBE0E0EF85`。
- `GameConfig.cs:17` 版本为 0.10.34；现有 BepInEx 日志记录 Unity `2022.3.62.1451004`。
- 当前插件入口：`PoolTrim/Plugins/PoolTrim.cs:35`，仅有 Import 后裁剪及读档后汇总。

找到并重新读取了 2026-08-01 的 Day24 原始报告。它们仍是**历史运行**，不是今天用当前游戏和模组组合重新测量：

数据位于当前模组配置目录下的 `BepInEx/LoadMemProfiler/`。

- `load_Day24-结档_20260801_103418.tsv` 及对应 `_capacity.txt`：未裁剪基线。
- `load_Day24-结档_20260801_104724.tsv` 及对应 `_capacity.txt`：路径容量已裁剪。

| 指标 | 未裁剪历史样本 | 已裁剪历史样本 |
|---|---:|---:|
| 工厂数 | 266 | 266 |
| 路径点容量 | 2,283,073,600 | 714,469,350 |
| 路径实际点数 | 714,469,350 | 714,469,350 |
| 路径三数组逻辑载荷 | 61.66 GiB | 19.30 GiB |
| 读档结束提交 | 122.02 GiB | 76.11 GiB |
| 整个采样窗口提交峰值 | 122.44 GiB | 77.18 GiB |
| 最后一次采样提交 | 122.30 GiB | 77.09 GiB |
| 读档耗时 | 215.867 s | 177.445 s |

报告列名 `MB/GB` 实际按 1024 换算，这里统一写 MiB/GiB。README 的“76GB”近似对应读档结束；含后续采样的窗口峰值约为 77.18 GiB。

重新按 TSV 的同名 begin/end 配对汇总，已裁剪样本中 BeltAndCargo 阶段提交净增约35.05 GiB，Entity约19.18 GiB，DysonShell约4.93 GiB，Statistics约0.27 GiB，Transport约0.18 GiB。这是阶段内的进程提交变化，会混入GC和堆复用，**不是对象存活大小**；父子阶段也不能相加。它支持优先研究路径与实体，暂不重写统计历史和物流数据结构。当前默认存档目录未找到 Day24 原档，本轮没有取得该档几何压缩率或新的运行期基准。

容量报告还有边界：

- `used` 对实体、货物和组件记录的是 cursor，不等于扣除回收孔洞后的活对象数。
- 实体旧报告按 372 B/槽估算，没有计入 entityRecycle、entityNeeds 引用数组和锁对象。报告记录 EntityData=224 B、AssemblerComponent=104 B；本次安装 DLL 在 CoreCLR 下 IL sizeof 为 204 B、96 B。不同运行环境/程序集改写的原因未确认，不能把旧测量尺寸当当前 Mono 布局。
- `gc_live_MB` 调用的是 `GC.GetTotalMemory(false)`，本身不强制完整回收，不能当精确存活对象普查；`commit − mono_heap` 也不是严格的原生对象分类。

## 1. 从源头处理运行期路径滞留

### 已删除的路径仍占数组

`CargoTraffic.RemoveCargoPath:342` 删除货物、移除渲染、设置 id=0，随后调用 `CargoPath.Clear:641`；Clear 清内容，但保留 buffer、pointPos、pointRot 三份数组。路径对象仍挂在 pathPool 中，GC 无法释放这些容量。

`CargoTraffic.NewCargoPath:314` 优先复用回收槽的对象；`CargoPath.PathCopy:432` 只在容量不足时扩容。因此大路径被拆掉后，其容量既能留在空槽里，又能被后来的短路径继承。

最小候选是在删除流程完成后释放废弃槽中的 CargoPath 引用，保留原版回收 ID。原版 NewCargoPath 已处理 null 槽，`CargoTraffic.Export:171` 也把 null/无效路径写成空槽，无需引入另一套对象池。

收益是 `29 × 废弃路径容量` 加对象、列表和分块数组；代价是随后建带要重新分配。应先统计废弃路径容量分布；如果小路径高频重用的分配成本明显，只保留有界的小容量复用，而不是保留历史最大数组。

需要覆盖路径拆分、合并、闭环、输入输出引用和同批多次编辑。跨路径解绑由上层编辑流程完成，应保留完整的上层流程及 RemoveCargoPath 的货物回收，不能只清槽。必须释放 `pathPool[id]` 引用，不能仅 Free 后留下非 null 对象：Free 会清空数组和列表，而 NewCargoPath 仅对 null 槽重新构造，否则会复用已经失效的对象。

### 活路径变短后的容量

删除废弃对象不能回收仍然有效但已大幅缩短的路径。沿 `CargoTraffic.AlterBeltConnections` 的完整编辑边界，对本次变更的路径收集一次裁剪；保留适量增长余量，只有绝对浪费和比例都值得时才复制。

不在每次铺一格时精确缩到长度，也不每帧扫描全部工厂。两者都会把省内存变成持续分配和大数组复制。运行期必须在相关模拟任务停止写入的边界替换数组；CargoPath.Update还把buffer本身作为锁对象，给裁剪代码单独加锁不能保证同步。

`AlterBeltConnections` 会递归调用自身；一次外层编辑完成后再合并处理受影响路径，避免在中间态反复复制。长度已有公开属性 `CargoPath.pathLength:55`。缩容后的容量不能为0：`AddBuffer:328`、`PathConcat:407`、`PathCopy:444` 都以旧容量乘2增长，零容量会令循环无法推进。失效槽直接释放；保留可复用对象时则必须保留正容量。

调度审计还必须覆盖货物显示线程：`GameLogic.FactoryPresentCargo_Parallel:4558` 在工作线程中读取几何并写cargoPool。仅等传送带模拟阶段完成不足以证明可以换数组；应验证包括显示阶段在内的读写已结束，再处理本轮待裁剪路径。

### 读档直接分配正确长度

`CargoPath.Import:101-105` 先读 capacity 并分配，随后才读 bufferLength。当前 postfix 在导入后再复制一次。

可以通过局部 IL 改写，仍顺序消费 capacity/length 两个字段，将分配搬到 length 读取之后。其他序列化逻辑保持原样，不需要流 Seek 或复制整个 Import 实现。它减少读档分配、复制及垃圾峰值，**不会额外降低已裁剪后的稳态载荷**。需要校验 IL 匹配和其他 Import 补丁兼容性。

## 2. 货物 GPU 镜像按可见工厂驻留

`CargoContainer.Import:291` 为每颗已建厂星球创建 `capacity × 32 B` 的 ComputeBuffer。全源引用显示该缓冲用于 Draw，模拟和 Export 读 CPU cargoPool；唯一外部绘制入口是 `FactoryModel.DrawInstancedBatches:195`。

原版离星流程为 `GameData.LeavePlanet:268 → PlanetData.UnloadFactory:533 → PlanetFactory.UnloadDisplay:336`。它销毁路径渲染批次，但没有释放货物 GPU 镜像。

候选实现：构造/导入时不创建大镜像；`CargoContainer.Draw:238` 首次绘制或容量变化时创建；UnloadDisplay 时 Release 并清引用。不能调用 CargoContainer.Free，它还会清空货物模拟数据。Draw 当前直接读取 computeBuffer.count，必须一并支持尚未创建的状态。

历史货物容量 101,352,448，对应 3.0205 GiB GPU 逻辑缓冲总量。实际专用显存、共享显存和进程提交变化由驱动决定，需分别测量。A→B→A 往返时，本地货物应及时刷新，远端物流持续运转。

## 3. 远端传送带几何无损冷存

每个路径点的三数组中，buffer 占 1 B，位置/朝向占 28 B。历史有效点数对应：

- buffer：0.6654 GiB；
- pointPos + pointRot：18.6312 GiB，即三数组载荷的 96.55%。

`CargoPath.Update:2203` 的输送模拟不依赖几何；`GameLogic.FactoryPresentCargo:1036` 只对 localLoadedFactory 执行 PresentCargoPathsSync。这使几何成为最有价值的冷热分离对象。

但几何不是可以直接丢弃的纯渲染缓存：

- `CargoPath.Export:89-90` 原样保存位置和朝向。
- `CargoTraffic.GeneratePathGeometry:2467` 可能读取接入目标路径的现有位置/朝向，`GetBezierArc:2503` 的离散点数受距离舍入影响。
- `PlanetFactory.OnBeltBuilt:4628` 等编辑路径、分拣器吸附和 UI 查询仍使用这些数组。
- 远端战损也修改带子：`CombatStat:309 → PlanetFactory.KillEntityFinally:4042 → RemoveEntityWithComponents:1207 → CargoTraffic.RemoveBeltComponent:422 → AlterBeltConnections`。factoryLoaded 只控制部分视觉，不阻止远端拆除。

因此第一种可验证方案应是**保存原始字节的无损冷存**，在需要绘制、编辑、查询或导出时恢复所需路径及其依赖。不要承诺仅凭实体位置就能无损重建原来采样；闭环首尾9点、路径接头、拆分历史和几何精度都要保留。

净节省公式为 `28 × 冷点数 − 冷存载荷 − 索引/恢复缓存`。压缩比例尚未测量；18.63 GiB 仅为整个样本的原始几何总量。若只把 float 数组搬进等大的 byte 数组，不会有有效节省。

用于评估是否值得投入的条件算例：假设全部几何都可冷存，压缩后占原始大小50%时，载荷差额约9.32 GiB；占25%时约13.97 GiB。实际还需扣除本地热路径、索引和恢复缓冲，以上不是压缩率预测。第一轮对原始字节使用现成无损压缩即可；只有真实样本表明收益不足，再评估可逆的float字节重排/差分，不先引入自定义几何近似。

冷存不仅要拦截绘制和编辑：`CargoPath.Clear:641` 无条件访问三数组，`Free:660` 和 `Import:97` 都先调用它，`CargoTraffic.Free:110` 则逐槽调用Free。冷路径销毁时应直接丢弃冷存载荷并完成解绑，不能为退出游戏或换档先恢复全宇宙。`SetCapacity:145` 在buffer仍存在时也会拷贝几何数组，必须纳入恢复边界，不能简单把两个几何字段置null。

保存应逐路径/有界批次解压并写回原版格式，避免自动保存时恢复全宇宙几何。必须处理导出异常后的状态、远端战损、远程建造/联机入口和进出星球的恢复峰值。第一轮实验只需回答：真实路径数据能压多小、一个大工厂恢复多久、连续自动保存是否仍维持较低内存。

## 4. 实体及组件池定向尾部裁剪

历史实体容量 48,595,968、cursor 36,935,386，报告估算尾部 slack 4.04 GiB。当前源码还包含 entityNeeds 引用数组；按本次离线 CoreCLR 尺寸计算，实体家族基本数组为每槽364 B，旧数量套新布局约3.95 GiB。该换算只是量级参考，实际游戏 Mono 下要重测。

不能直接调用原版 SetCapacity 缩小：

- `PlanetFactory.SetEntityCapacity:409-443` 重建 entityRecycle 而不保留回收栈，entityMutexs 又按旧容量全长复制，缩容可能抛异常。
- `FactorySystem:456-463`、`PowerSystem:361-368` 的容量 setter 也依赖原版增长语义。
- 分拣器还有姿态伴随数组，不能只裁 inserterPool。

仅处理 cursor 之后空间，完整保留回收栈、ID和伴随数组，保留增长余量。内部回收孔洞需要另行审计尾部高水位；不能按 liveCount 直接截断，也不应为小收益重编号全部实体。

`FactorySystem.GameTick:709-711` 会缓存数组引用；工作线程还运行时替换数组会让更新写回旧数组。优先在读档完成的安全边界做，运行期等证明同步边界后再加入。先实现测得最大的少数池，不建立通用反射裁剪框架。

## 5. 戴森球、帆群与既有模组

### Mesh 的 CPU 重复副本

`DysonShell.GenerateModelObjects:1142-1162` 把托管 verts/UV/indices 写入 Unity Mesh，原始托管数据仍保留。Mesh 中的逻辑载荷为位置12 B、三套UV共24 B、16位索引2 B，约38 B/顶点。

Unity 的 [Mesh.UploadMeshData(true)](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Mesh.UploadMeshData.html) 可以释放 Mesh 的系统内存副本。这不会释放托管原始数据或 GPU 数据。1亿顶点对应约3.54 GiB逻辑载荷，实际值须用 Unity 原生内存测量。

原版绘制只使用 Mesh，不回读顶点；但 GenerateModelObjects 会对旧 mesh.Clear 后重写，必须处理已不可读 Mesh 的再次生成（必要时销毁重建），并检查 SphereOpt 等模组的 Mesh 读取。不能把一次 Upload 调用当作未经验证的通用补丁。

### 帆群快照与容量高水位

`DysonSwarm.Export:297`、`Import:364` 为 sailPoolForSave 分配32 B/容量的 CPU 数组，结束后保留；`RemoveSailsByOrbit:1304` 已存在用完置 null 的原生先例。可评估仅释放明显大的保存快照，换取下次保存重新分配；若保存频繁，需比较复用与释放的峰值、停顿及稳态收益。

`DysonSwarm.GameTick:874` 只在每216000 tick且活帆少于200000时执行常规整理，完全空群另行清理。可以在低利用率时复用原生整理流程回收高水位，但要核对到期/吸收队列和GPU搬运成本，不直接缩 SetSailCapacity。

太阳帆 swarmBuffer 是每tick更新的模拟主状态，Export 也从GPU回读，**不能因为不可见就释放**。

### 不重复实现现有壳延迟加载

本地 test1 当前安装的 LossyCompression 配置为 LazyLoad=true、ReduceRAM=false、CargoPath=false；这不能证明历史测量当时使用了相同配置。

上游已有壳按需生成及保存前恢复逻辑，还处理无几何时拆壳释放太阳帆。其 [LazyLoading.cs](https://github.com/starfi5h/DSP_Mod/blob/fdb6e3573d41f78b3443d0c6021211d48585c956/LossyCompression/src/LazyLoading.cs) 和 [功能/兼容说明](https://github.com/starfi5h/DSP_Mod/blob/fdb6e3573d41f78b3443d0c6021211d48585c956/LossyCompression/README.md) 应先作为对照；SphereOpt 会影响该延迟加载能力。CargoPath 有损存档压缩改变存档依赖，不能当作原版格式兼容的内存冷存直接复用。此调研未更改这些配置。

## 暂不优先的方向

- **强制GC、工作集清空。** 仍有引用的数组不会因此消失；工作集下降也不代表提交或数据量下降。Unity 2022.3 使用非紧缩GC，回收、堆容量和操作系统内存指标需区分，见 [Unity garbage collection](https://docs.unity3d.com/2022.3/Documentation/Manual/performance-incremental-garbage-collection.html)。
- **配方数据去重。** 当前 `RecipeProto:114-119` 已共享 RecipeExecuteData，Assembler/Lab 使用共享引用；served/incServed/needs/produced 是各机器独立库存，不能合并。
- **远端实体动画/标志直接卸载。** FactorySystem、PowerSystem 在模拟中仍读写，且部分状态参与保存，不是纯显示缓存。
- **实体连接压缩。** 固定64 B/容量槽，旧样本约2.90 GiB，有空间但改造面较大。先统计非零槽分布；皮带还有分拣器槽，14/15为叠层，不能假设仅四个连接；Dictionary开销可能抵消收益。
- **Cargo 结构压缩。** 每货物32 B中28 B用于位置/朝向，但更改结构牵涉大量直接数组访问和GPU步长。约2.64 GiB的旧样本原始几何载荷不值得优先于18.63 GiB路径几何；只替换数组类型不是兼容补丁。
- **统计历史压缩。** ProductStat每个已记录的工厂×物品有7200个int，约28.1 KiB；需要按真实记录数评估。每tick、UI和存档都有直接索引，不能简单缩数组或删历史。
- **地形缓存。** 离开恒星已有 StarData.Unload → PlanetData.Unload 原生卸载。当前源标准200精度主要地形数组约5 MiB/行星，量级远小于路径；只在实测出现异常保留时深入。
- **读档整文件缓冲。** 原版 GameSave 已使用FileStream，UnsafeIO使用固定8 MiB缓冲；不需要再造流式读档层。

## 下一步最小验证范围

LoadMemProfiler 已扩展运行期时间线和容量扫描，具体指标、开销控制及限制见 [LoadMemProfiler README](../../../LoadMemProfiler/README.md)。诊断与 PoolTrim 运行逻辑分离；下面是实测所需数据，其中实际GPU专用/共享显存和几何压缩样本仍需补充采集：

1. 在读档后、长时间生产后、批量拆建后、切星球后、自动保存后记录快照；运行期按事件/受控请求采集，不每帧扫全宇宙。
2. 路径分别统计有效长度、活路径slack、无效路径保留容量；实体/货物/组件分别记录capacity、cursor、recycle/live数量。
3. 增加CPU保存快照、shell顶点和Mesh可读性、Cargo GPU镜像容量计数，记录当前程序集/游戏/模组版本。
4. 同时记录进程提交/工作集、Mono heap/used、GPU专用/共享内存及逻辑buffer大小；比较UPS、p95/p99帧时间、保存与落地恢复耗时。

第一批原型分别验证废弃路径释放和Cargo GPU驻留，避免混在一起无法归因。之后用真实路径几何测压缩率，再决定冷存是否值得实现；实体池裁剪和Mesh副本释放可独立推进。

运行期收益需同时看常驻量、增长速度和停顿：重复同一段拆建/飞行/自动保存循环后，比较活数据与保留容量是否稳定，记录新增分配字节和复制耗时。原型只有在明显减少目标载荷、且没有不可接受的UPS或p99帧时间退化时才进入PoolTrim；减少数组大小不保证进程提交立即等量下降，也不保证CPU模拟更快。读档、普通生产、编辑、保存、落地恢复应分别计时。

正确性门槛围绕真实风险：货物/堆叠/增产点数守恒、拆建与闭环连接、远端战损、A→B→A货物显示、原版格式保存再读、连续自动保存无增长、戴森壳重建/拆除与太阳帆吸收过期。冷存恢复还需逐字节比较原始几何；不把离线源码审计写成游戏内验证通过。

## 900万糖档：PoolTrim 1.1.0读档复测

2026-09-04，用户重新读取900万糖的Day24结档。报告前缀为`load_Day24-结档_20260904_231421_8850577`，文件位于当前模组配置目录下的`BepInEx/LoadMemProfiler/`。metadata确认游戏0.10.34、LoadMemProfiler 0.3.1、PoolTrim 1.1.0；游戏及诊断插件MVID与当日200万糖测试相同。

读档在167.424秒成功返回，184.562秒开始运行，导入266个工厂。PoolTrim日志记录：

| 项目 | 裁剪数量 | 按本次布局折算的数组载荷 |
|---|---:|---:|
| 路径三数组 | 1,568,604,250个路径点 | 45,489,523,250 B，42.3654 GiB |
| 实体及伴随数组 | 172个工厂、7,182,996个槽位 | 2,758,270,464 B，2.5688 GiB |
| 合计 | — | 48,247,793,714 B，44.9343 GiB |

路径使用原版三数组每点29 B。实体布局使用本次记录的EntityData=224 B、AnimData=20 B、SignData=56 B，加每槽64 B连接、8 B mutex引用、8 B needs引用、4 B回收ID，合计384 B。实体值是成功裁剪槽位数乘运行时布局的折算，不是完整快照的前后汇总。

手动快照1在206.474秒开始，238.822秒记录取消，仅留下1,826行且没有total汇总；该文件不作为完整容量快照。运行开始后60–75秒（245.007–259.148秒）15个未暂停样本的中位数：提交73.0295 GiB，工作集50.8806 GiB，GC已用52.7527 GiB，Mono堆60.8342 GiB。

### 同日运行对比与README口径

原始存档文件为46,679,237,264 B，即43.4734 GiB（46.6792 GB）；文件头声明的长度、实际文件长度和以下两次导入记录的最终字节位置一致。

补查同日未启用PoolTrim的报告`load_Day24-结档_20260904_211052_5245415`：游戏及诊断插件MVID与本次相同。统一取运行开始后60–75秒、未暂停且在星际空间的样本，未启用组4个样本（344.863–358.367秒）的进程提交中位数为122.0365 GiB；启用组15个样本为73.0295 GiB。README将其表述为同一存档的两次运行对比，没有使用8月历史进程值与本次拼接。

两次运行从诊断会话开始到`load_end`分别为233.539秒和167.424秒，对应`GameSave.LoadCurrentGame`成功返回。README的“读档时间（存档导入）”统一使用这个边界；`runtime_begin`分别为284.850秒和184.562秒，是后续开始运行的时刻。

两组metadata另有SpraycoaterLevel10差异：未启用组加载0.1.4，本次未加载；运行设置也未被完整记录。因此这不是严格单变量基准，不据此宣称全部进程差值仅由PoolTrim造成，也不将单次读档耗时差异视为稳定的加速比。README以两次运行实测值展示读档时间和提交占用，数组裁剪另按容量计数计算。

该原始存档此前完整容量报告记录路径容量2,283,073,600点、有效714,469,350点，差值与本次裁剪日志严格一致；对应三数组61.6621→19.2966 GiB，利用率31.2942%→100%。原实体容量48,595,968槽减去本次成功裁剪的7,182,996槽，得到41,412,972槽；按本次384 B/槽的完整伴随数组口径，17.3793→14.8104 GiB。这些数组前后数值为原档容量与本次裁剪计数的折算，本次取消的快照不作为完整汇总。
