# LogisticsProfiler

手动、限时采样星际物流 CPU 开销。独立 BepInEx 插件，可在 InterstellarLogisticsOpt 开启、关闭或未安装时使用；不依赖 UXAssist，不修改物流数据和存档。

## 使用

1. 将 `bin/Release/LogisticsProfiler.dll` 放进所用 profile 的 `BepInEx/plugins/LogisticsProfiler/`。
2. 启动游戏并读档，等生产稳定后按 **F9**，屏幕会提示“物流采样已开始”。默认采集 **60 秒墙钟时间**，再次按 F9 提前结束。插件因错误停用时，F9 会提示查看日志。
3. 收集该 profile 的 `BepInEx/LogisticsProfiler/` 内同一 UTC 时间前缀的三个文件。日志会打印完整路径。

配置文件为 `BepInEx/config/org.fyyy.logisticsprofiler.cfg`：

| Capture 配置 | 默认 | 含义 |
|---|---:|---|
| `Key` | F9 | 开始/结束采集 |
| `Seconds` | 60 | 时长，10–600 秒；开始时固定 |
| `SampleEvery` | 256 | 随机抽取约 1/N 的调度及飞船更新调用，范围 1–4096 |
| `Label` | baseline | 写入 metadata 的实验标签 |
| `DispatchDetails` | true | 统计被抽中调度的实际分支次数；改变此项后须重启游戏 |

调度总入口每次计时。随机抽样避免固定每 N 次采样与塔编号/优先级周期重合。采集期间只有内存聚合，结束时写文件；异常会停止诊断，游戏原有异常仍然向上传播。正在运行的采样回调结束后才导出汇总。

采样范围是持续运行的调度和飞船更新。建拆塔、修改航线时的配对重建由 BuildToolOpt 负责，不在本插件采样范围内；覆盖边界见[调研报告](../InterstellarLogisticsOpt/RESEARCH.md#additional-optimization-opportunities)。

## 对照实验

每组从同一份原始存档重新读档，使用相同 SAHS/BuildToolOpt 配置、游戏速度、视角和预热时间，分别采集：

| 组 | InterstellarLogisticsOpt 设置 | 用途 |
|---|---|---|
| A | `Enabled=false` | 原版调度计算 |
| B | `Enabled=true, AmortizeFactor=1` | 保留原版节奏，省去冗余库存读取和锁刷新 |
| C | `Enabled=true, AmortizeFactor=5` | 全航线降频，完整执行每次到期扫描 |

ILO 的系数覆盖全部优先级，并同步减慢锁时钟；每个逻辑 tick 完整执行原版调度，返程取货保持原版搜索规则。采样中不要切换配置；metadata 只比较开始和结束的配置，检测不到中途改回原值。暂停、换存档或 `settings_changed=True` 的记录不适合作为稳定运行对照。普通停止应为 `duration` 或 `manual`。

先各采集一组，再根据波动决定是否重复。另看游戏生产/交通统计，持续覆盖若干船舶往返周期：60 秒采样不能证明物流吞吐长期不变，也不能用发船数代替实际送货量。

插桩本身有成本。未采集时仍有三个 Harmony 方法包装；开启 `DispatchDetails` 时，调度分支还会检查本次调用是否被抽中。只有抽中的调用分配计数数组并累加，不在候选循环中调用计时器、聚合锁或额外读取物流状态。首次使用应比较卸载插件、已安装但未采集、正在采集这三种状态；若明显变慢，增大 `SampleEvery`。关闭 `DispatchDetails` 并重启可移除分支插桩；消除包装成本需要移走 DLL 并重启。详细模式用于判断扫描原因，其耗时不能直接与 0.1.0 或关闭详细模式的记录比较。

## 输出

- `_metadata.txt`：游戏/插件版本、MVID、采样率、存档名、起始塔池高水位、实际墙钟秒数、tick 增量、近似暂停时间、ILO/SAHS 配置以及三个目标方法上的 Harmony 补丁所有者。
- `_summary.tsv`：调度、六个优先级的 `DetermineDispatch`、`InternalTickRemote`，以及 60 个 `time % 60` 调度分组和渲染帧间隔。
- `_stations.tsv`：按塔 GID、planetId、优先级聚合的调度样本，按采样耗时降序排列。只包含至少被抽中一次的塔。

时间单位为毫秒，UTF-8、英文小数点，`-1` 表示不适用或没有样本。

0.2.0 使用 `schema=2`，在两个 TSV 原有列后追加以下分支计数。三个输出文件及普通计时列的含义不变。metadata 的 `dispatch_details` 记录实际启动模式。

优先级编号沿用游戏：0 为 Ignore 模式的普通配对，1 为塔对塔航线，2 为行星航线，3 为恒星航线，4 为物流分组，5 为 Prioritize 模式的普通回退配对。

| 列 | 解释 |
|---|---|
| `samples` | 实际采到的调用数；不是抽样方法的全部调用数 |
| `sampled_ms` | 已采到调用的累计墙钟耗时 |
| `mean_ms`, `max_ms` | 样本均值/最大值；抽样最大值不保证捕获真实峰值 |
| `original_skipped` | Harmony 报告原方法未执行的样本数；可能是优化 mod 的替代实现正常接管 |
| `errors` | 最终存在异常的样本数 |
| `no_idle`, `low_energy` | 进入时无闲船、能量不超过 6 MJ 的样本数；两者可重叠 |
| `empty_pairs` | 当前优先级没有候选的调度样本数 |
| `candidate_pairs_sum` | 进入时该优先级候选区间长度的总和；**不是实际扫描/访问次数** |
| `working_ships_sum` | 进入时在途船数的总和，除以 samples 得到样本均值 |
| `launch_delta_sum` | 被采样 `DetermineDispatch` 前后 workShipCount 的净增量；原版正常调度为 0 或 1，不含送货量及返程取货 |
| `no_idle_ms`, `low_energy_ms`, `empty_pairs_ms` | 相应入口状态的采样耗时，可相互重叠，不能相加 |
| `no_launch_ms` | 在途船净增量 ≤ 0 的调度样本耗时；也包括被跳过/失败的调用 |

### 调度分支计数

计数来自被抽中的 `DetermineDispatch` 方法体，包括 ILO 1.2 优化后的方法体。未开启详细模式、非调度行或无样本时为 `-1`；原方法被其他 prefix 跳过时为 `0`。异常调用保留异常前的部分计数，分析时同时检查 `errors`。

| 列 | 解释 |
|---|---|
| `supply_visit`, `demand_visit` | 外层循环实际访问的候选数，分别为本塔供货/取货方向；两者之和就是外层访问总数 |
| `own_priority_lock`, `peer_priority_lock` | 因本塔/对端较高优先级锁而跳过的外层候选数 |
| `own_supply_short`, `peer_supply_short` | 供货侧库存或含订单的可供应量未超过游戏装载阈值 |
| `own_demand_empty`, `peer_demand_empty` | 需求侧含订单的剩余需求量不大于零 |
| `peer_missing` | 本塔供需检查通过，但对端塔池槽为空 |
| `trip_check` | 通过供需和锁检查，开始计算航程的外层候选数 |
| `range_block`, `collector_block`, `warper_block` | 实际命中航程、轨道采集器限制、必要翘曲器/翘曲能力限制的次数 |
| `ship_or_energy_block` | 实际命中无闲船或能量 ≤ 6 MJ 的次数；入口两项状态另见 `no_idle` / `low_energy` |
| `flight_eligible` | 通过上述飞行条件，开始计算航行能耗的次数；不代表能量足够或成功发船 |
| `trip_energy_short` | 外层尝试发船时，能量低于计算出的航行成本 |
| `supply_attempt`, `supply_failed` | 外层供货发船函数的调用次数及返回 false 次数 |
| `demand_attempt`, `demand_failed` | 取货发船函数的调用次数及返回 false 次数 |
| `reverse_search`, `reverse_visit`, `reverse_match` | 反向供货搜索的启动次数、实际访问候选数、匹配当前两塔的候选数 |
| `reverse_own_lock`, `reverse_peer_lock` | 反向候选因本塔/对端优先级锁而跳过 |
| `reverse_supply_short`, `reverse_demand_empty`, `reverse_energy_short` | 反向候选实际命中的供货不足、无需求、航行能量不足分支 |
| `reverse_attempt`, `reverse_failed` | 反向供货发船函数的调用次数及返回 false 次数 |
| `priority_lock_calls` | 所有路径调用 `SetPriorityLock` 的次数；函数可能不改锁，故不是写入次数 |

这些列是分支事件，**不是互斥的未发船原因或各分支耗时**。例如一个候选可能同时受航程和翘曲器限制；反向搜索失败后仍可能成功派出取货船。供需量不足时也可能继续维护对端优先级锁。`reverse_visit` 与外层访问数分开统计，避免把嵌套搜索误当成外层候选环长度。

详细插桩校验包含操作数、跳转目标和异常区域的目标方法签名；字段类型使用明确的完整名称，避免 .NET Framework 与 Unity Mono 的反射显示格式差异。目前支持本机 MVID `ECE4A40E-5E73-43F4-A9F8-4E74970B5942` 的原版方法。0.2.2 将探针放在 ILO 1.2 优化之前：先严格校验原版 IL 并插桩，再让 ILO 插入跳过冗余工作的分支，因此仍能观察候选访问及实际保留下来的锁函数调用。游戏更新或其他更早运行的 transpiler 改写方法导致签名不匹配时，本插件停止诊断，保留对方的方法体，不阻止对方安装补丁。如果在本插件启动时发现不匹配，还会撤销自己的全部补丁并打印 `Unsupported DetermineDispatch body`；采集期间发现则以 `diagnostic_error` 结束，并在 metadata 记录原因。可以关闭 `DispatchDetails` 后重启，使用普通计时模式。

针对[已发现的 planetId 2704 / P3 和 P5 热点](../InterstellarLogisticsOpt/PROFILING.md)，建议 `Seconds=180`、`SampleEvery=64`、`DispatchDetails=true`，分别从同一原始存档重新读档采集上述 A/B/C 分组。保持相同预热、视角与其他 mod 设置；开始采样后不改配置。现有配置文件中的 60/256 不会因更新 DLL 自动变成 180/64。比较候选访问和分支分布；生产吞吐仍需更长的游戏时间验证。

约 `sampled_ms × sample_every` 可估计该方法在采集区间内的累计调用耗时；样本少时误差可能很大。除以 scheduler 的 samples，可得到每次调度 tick 对应的近似累计耗时。调度总入口本身完整计时，不需要外推。

**不要相加嵌套计时**：scheduler 包含 dispatch 及其诊断开销；scheduler_phase 是同一批 scheduler 样本的重新分组。`remote_tick` 含飞船运动、装卸/返程配对、翘曲器补充、船舶渲染数据和优先级锁递减。工作线程的耗时之和包含并行重叠和等待，不能当成主线程帧耗时。`frame` 是 LateUpdate 间隔，包含等待、渲染、暂停和模拟；`observed_ups` 是 tick 增量/墙钟秒数。

建议先看：

1. A/B 的 scheduler 平均/最大值、P3/P5 耗时与 `priority_lock_calls`：冗余工作是否减少；B/C 的所有优先级调用量和耗时：全航线降频效果。完整扫描仍可能产生尖峰，不能仅凭平均耗时判断流畅度。
2. `supply_visit + demand_visit` 和 `launch_delta_sum`：调度工作量及发船是否可比。
3. `remote_tick`：剩余耗时是否主要来自飞船更新/返程取货。当前 ILO 只同步缩放锁倒计时，该行仍包含完整飞船更新；必须同时看较长时间的实际送货量。

## 构建与离线检查

使用本机游戏与 BepInEx 程序集；可通过 MSBuild 的 `GameManagedDir`、`BepInExDir` 覆盖默认路径。

```powershell
dotnet build LogisticsProfiler/LogisticsProfiler.csproj -c Release
dotnet run --project LogisticsProfiler/tests/Checks.csproj -c Release -- `
  'C:\Program Files (x86)\Applications\Steam\steamapps\common\Dyson Sphere Program\DSPGAME_Data\Managed' `
  'C:\Users\Yi\AppData\Roaming\r2modmanPlus-local\DysonSphereProgram\profiles\latest\BepInEx\core'
python LogisticsProfiler/tests/run_mono.py `
  'C:\Program Files (x86)\Applications\Steam\steamapps\common\Dyson Sphere Program\DSPGAME_Data\Managed' `
  'C:\Users\Yi\AppData\Roaming\r2modmanPlus-local\DysonSphereProgram\profiles\test1\BepInEx\core'
```

检查覆盖并发聚合、停止时未完成回调、64 位计数、输出格式、三个真实游戏方法的 Harmony 绑定、被跳过的原方法、异常传播和卸载。同时直接调用游戏的 `DetermineDispatch` 复现 ILO 提前退出所遗漏的优先级锁与游标变化。分支检查在真实游戏程序集上验证供需、锁、飞行条件、反向搜索和发船成功/失败的计数，并逐字节对照插桩前后的库存、订单、锁、游标、船舶、能量和交通计数；目标操作数变化必须拒绝插桩。

`run_mono.py` 在独立进程中加载游戏自带 Mono，执行同一套聚合、24 个调度分支场景、ILO 状态等价检查、调度时序检查和专项微基准，不连接或操作正在运行的游戏。无头进程不具备 Unity 原生调用，因此跳过飞船更新的绑定检查；完整的三个方法绑定仍由 .NET Framework 检查覆盖。两种运行时都必须接受同一个调度签名并拒绝实际操作数变化。

当前离线检查使用游戏程序集 MVID `ECE4A40E-5E73-43F4-A9F8-4E74970B5942`，覆盖全部优先级、库存与订单边界、完整及过期锁、无闲船/低电量、开关状态、独立运行和 profiler 两种加载顺序。调度检查对照系数 1/2/5/30 下的原版轨迹和锁倒计时，并检查原版返航选择、异常传播、因子切换、兼容回退、卸载及 profiler 的真实 tick 分组。两种运行时均执行从实际 `InternalTickRemote` 提取的返航选择和锁倒计时 IL；完整飞船方法的 Harmony 绑定由 .NET Framework 验证，无头 Mono 不具备完整 Unity 飞行环境。微基准不能外推为存档 UPS。已有实机采样及其版本、验证范围见[采样分析](../InterstellarLogisticsOpt/PROFILING.md)；长期物流吞吐仍需游戏验证。
