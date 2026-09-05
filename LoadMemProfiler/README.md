# LoadMemProfiler

DSP 读档和运行期内存诊断插件，版本 **0.3.1**。记录内存走势、操作事件和按工厂/恒星划分的容量数据，用于寻找 PoolTrim 的优化目标。只读取游戏数据，不裁剪数组、不强制 GC、不改变存档。

## 使用

1. 编译后，将 `bin/Release/LoadMemProfiler.dll` 放进所用 BepInEx profile 的 `plugins/LoadMemProfiler/`，替换旧 DLL，避免重复安装。
2. 启动游戏并读档。前120秒每秒采样，之后默认每5秒采样；**默认不自动扫描容量**，旧配置中的300秒周期也不会开启自动扫描。
3. **F8** 开始手动容量快照，再按一次取消。扫描完成后，BepInEx 日志会打印快照编号和文件位置。
4. 退出游戏后，收集该 profile 下 `BepInEx/LoadMemProfiler/` 内同一前缀的三个文件。

先读档并运行两分钟，不按F8，确认基础采样时游戏仍可正常运行。随后按一次F8测试扫描开销；若明显卡顿，再按F8取消。确认扫描开销可接受后，再采集持续生产、批量拆建、A→B→A往返和保存前后的快照。比较时使用同一个存档、模组配置和游戏速度；完整对照需关闭插件并重启游戏。

保存开始/结束/失败和位置变化会立即记录进程内存。拆建入口调用次数累计到运行期采样行。只有显式设置 `Runtime.AutomaticSnapshots=true`，才会在运行开始、周期到期、保存、位置变化和拆建后自动扫描：自动事件扫描在上次扫描结束后至少间隔30秒，纯拆建请求还需等待5秒没有新的拆建调用。F8可跳过这个间隔；已有扫描时F8取消，不排队重扫。保存进行中暂停容量扫描。

## 文件

每次读档创建独立文件组；新游戏或启用插件后的有效游戏也会创建运行期文件组。文件名时间为UTC。

| 文件 | 内容 |
|---|---|
| `load_<name>_<time>.tsv` / `runtime_<name>_<time>.tsv` | 时间线：读档分段、运行期采样、保存、位置变化、快照边界 |
| 同前缀 `_capacity.tsv` | 多次快照的工厂、恒星、全局和远端汇总数据 |
| 同前缀 `_metadata.txt` | 格式版本、游戏/Unity/CLR版本、游戏和插件MVID、已加载插件GUID/版本 |

格式版本为 **schema=3**；替代0.2的 `*_capacity.txt` 和MB列，不与旧TSV混用。字节列均为原始字节，换算GiB除以 `1024³`。TSV使用UTF-8、英文小数点；标签中的制表符和换行会替换为空格。

## 时间线指标

| 列 | 含义 |
|---|---|
| `t_s` / `event` / `detail` | 会话经过秒数、事件、上下文；运行期detail的add/remove_calls是累计入口调用次数，不代表当前建筑数量 |
| `file_bytes` | 读档流当前位置；非读档阶段为-1 |
| `gc_used_bytes` | `GC.GetTotalMemory(false)`，不强制回收，不等于精确存活对象大小 |
| `mono_heap_bytes` / `mono_used_bytes` | Mono堆容量/使用量 |
| `commit_bytes` / `working_set_bytes` | 进程私有提交/工作集 |
| `page_faults` | 进程累计缺页次数，包含软缺页；不能当作硬盘换页次数 |
| `gc0` / `gc1` / `gc2` | 各代累计GC次数；运行时不支持的代为-1 |
| `game_tick` / `local_planet` / `loaded_planet` / `local_star` / `paused` | 最近一次LateUpdate的游戏上下文；0表示不在相应星球/恒星，loaded_planet表示本地工厂已加载 |
| `frames` / `percentile_frames` | 本采样间隔帧数/实际参与分位数统计的帧数 |
| `frame_mean_ms` / `frame_p95_ms` / `frame_p99_ms` / `frame_max_ms` | LateUpdate之间的墙钟间隔；均值和最大值覆盖整个间隔，分位数最多保留最近4096帧，使用nearest-rank |
| `observed_ups` | 游戏tick增量/实际经过秒数；受暂停、游戏速度、保存等影响，不等于纯模拟CPU性能 |
| `observer_ms` | 本采样间隔内诊断LateUpdate的累计执行时间，包含扫描和写出；不包含读档/保存Harmony回调 |
| `unity_allocated_bytes` / `unity_reserved_bytes` / `unity_unused_reserved_bytes` | Unity分配器的已分配、已保留和空闲保留量，仅在runtime行读取 |

**-1表示不可用或不适用，不表示0。** Unity、Mono、进程指标有重叠，不能相加，也不能用两列相减推导精确对象分类。游戏卡住而未执行采样时，不会产生中间样本，所以短时峰值仍可能漏测。

## 容量指标

容量表列为 `snapshot, t_s, game_tick, scope, id, is_local, metric, value`。

- `factory` 的id是planetId，`star`的id是starId；`total`和`remote_total`分别汇总所有和非本地对象。`is_local`以读取该工厂/恒星时的所在地判定。
- 普通池记录 `slot_bytes, capacity, cursor, live, recycled, tail_slots, array_bytes, tail_array_bytes`。cursor是高水位，不等于live；tail只统计cursor之后的空间，回收孔洞单独记录。货物和太阳帆从0号槽开始，其余这些池通常保留0号槽。
- `slot_bytes`由实际游戏运行时的 `UnsafeUtility.SizeOf<T>()` 获取，只出现在具体工厂/恒星行；不把历史机器上测到的布局硬编码为当前布局。
- `path.active_slack_bytes`为有效路径的三数组尾部冗余；`path.inactive_bytes`为无效路径仍保留的数组及列表载荷。这两项互不重叠。
- `path.geometry_bytes`为位置/朝向数组实际载荷；包括活路径冗余和无效路径，所以不能再与它们相加。`path.auxiliary_bytes`包括chunks、临时chunks及路径关系列表的容量。`path.max_*`在汇总中取最大值，其余可用数量求和。
- 工厂覆盖实体/动画/标记/连接、部分实体引用数组、预建造、敌人、传送带及附属组件、货物、生产组件、电力组件、生产统计历史数组。
- 恒星覆盖太阳帆信息、弹道、保存快照、回收/过期/吸收队列、主要GPU缓冲，以及戴森壳几何数组、Mesh数量和可读性。
- `*.gpu_*bytes`是已创建ComputeBuffer的逻辑大小，不是驱动实测的专用/共享显存；不创建缓冲，不回读GPU数据。
- `shell.mesh_cpu_estimate_bytes`按可读Mesh的顶点数×38估算原版布局的逻辑载荷；不是Unity原生内存测量，修改Mesh布局的模组会影响估算。

这些是已覆盖数组的载荷统计，**不是完整堆普查**。不包含对象头、对齐、全部私有缓存、组件引用的库存数组和Mutex对象本体。`entity_mutex_refs.bytes`等只计引用数组，避免把共享引用重复算成实际对象；普通组件池的回收数组也未全部计入。

## 开销与生命周期

完整扫描按每帧 **1 ms软预算** 执行，每32个路径/生产统计槽、每8个壳槽及小组池指标之间检查耗时；达到预算后下一帧继续。不会逐点遍历或复制几何；小对象计数使用池的cursor/recycle计数。容量文件使用64 KiB缓冲，扫描完成时刷新，不逐帧刷新文件。游戏上下文仅写行时格式化，避免逐帧产生字符串。帧时间样本和汇总字典有界，时间线直接写文件，不在内存积累整段游戏历史。

**软预算不能抢占单次缺页、原生API调用或文件写入。** 当游戏占用超过可用物理内存时，遍历冷对象仍可能触发换页并拖慢模拟，因此容量扫描默认仅手动开启；不能把1 ms当作最坏帧耗时保证。

**快照是一个时间窗口，不是暂停游戏后的原子快照。** 同一工厂的长路径扫描也可能跨帧；分析应只使用有对应 `snapshot_end ... status=complete` 的编号，结合begin/end和各行时间判断窗口内是否发生变化。换档、退出或关闭插件会取消未完成扫描并释放持有的引用；部分数据不作为完整快照。

诊断文件IO或扫描失败会记录一次警告并停止本次进程中的诊断，重启游戏恢复；不修改Enabled配置、不阻断游戏保存。读档/保存返回false和抛异常都记录为失败，不生成成功标记。

文件会持续增长，适合定向采样；不自动删除既有报告。运行期开销仍需用目标大档测量，尤其观察 `observer_ms`、p99帧时间和扫描耗时。没有实际GPU专用/共享显存计数，也未采集原始几何压缩样本。

## 配置与验证

配置文件：`BepInEx/config/fyyy.dsp.loadmemprofiler.cfg`。

| 配置 | 默认值 |
|---|---:|
| `General.Enabled` | true |
| `General.PostLoadSeconds` | 120 |
| `General.PostLoadIntervalSeconds` | 1 |
| `Runtime.SampleIntervalSeconds` | 5 |
| `Runtime.AutomaticSnapshots` | false；关闭启动、周期和事件扫描，保留基础采样及F8 |
| `Runtime.SnapshotIntervalSeconds` | 300；仅AutomaticSnapshots=true时生效；0关闭周期扫描，正值最低30秒 |
| `Runtime.CaptureKey` | F8；开始/取消扫描 |

```powershell
dotnet build LoadMemProfiler/LoadMemProfiler.csproj -c Release
dotnet run --project LoadMemProfiler/tests/Checks.csproj -c Release
```

离线检查覆盖路径活/废弃载荷分离、64位计数、缺失几何、帧分位数的有界保留/重置、TSV格式，以及单批超时后让出执行并正确续扫。检查程序还接受三个可选位置参数：插件DLL路径、游戏Managed目录、BepInEx/core目录；提供时额外检查实际程序集的私有字段访问器和Harmony目标/参数，不执行游戏方法。此检查使用.NET Framework，避免旧Harmony与新版CoreCLR不兼容。

游戏内还需验证大档开销、F8、保存失败、连续换档、A→B→A、批量拆建及退出时文件关闭；离线检查不替代这些验证。
