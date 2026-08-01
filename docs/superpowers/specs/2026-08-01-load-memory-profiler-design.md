# 读档内存优化 —— 阶段 1：LoadMemProfiler 诊断 mod 设计

日期：2026-08-01

## 背景与问题

- 45GB 大存档，机器物理内存 64GB。
- 读档时进程提交大小（commit size）峰值达 150-200GB，导致页面文件颠簸；读档完成后稳态 ≤80GB。
- 即：读档过程存在 70-120GB 的**临时**开销，来源未知。

代码调查结论（GameCode-latest 反编译源）：

- `GameSave.LoadCurrentGame` 直接从 `FileStream` 流式读取，无整档载入内存。
- 大数组（太阳帆池、实体池、动画池等）经 `UnsafeIO.ReadMassive` 用固定 8MB 静态缓冲直读进最终数组，IO 层无临时副本。
- 因此峰值来源无法从代码直接断定。主要假设：
  1. **Mono/Boehm GC 堆过度扩张**：短时间分配几十 GB 长寿命大数组时，Boehm 扩张策略过度预留且不归还 OS；
  2. 加载完成前后星球模型/戴森球渲染资源异步构建的原生暂存内存；
  3. 数百万小对象（如每实体一个 `Mutex` 托管对象）造成的堆碎片。

## 总体方案（已选定：方案 A，诊断先行）

两阶段。本设计只覆盖阶段 1：**诊断 mod `LoadMemProfiler`**，用真实大档跑出每阶段内存归因，再依据数据设计阶段 2 的针对性治理（候选工具箱见文末）。

成功标准（阶段 1）：读一次档产出一份 TSV，能回答——

- 哪个 `ESaveDataEntry` 阶段 / 哪个工厂 / 哪个戴森球导入时提交大小跳涨；
- 跳涨部分是托管堆（GC live / Mono heap）还是原生内存（commit − mono heap）；
- Boehm 堆浪费量（mono_gc_get_heap_size − GC live）随时间的曲线；
- 读档结束后 N 秒内（渲染资源构建期）内存爬坡曲线。

## 架构

```
LoadMemProfiler/
├── LoadMemProfiler.csproj      # net472 SDK 风格，沿用仓库惯例
└── Plugins/
    └── LoadMemProfiler.cs      # 插件入口 + Harmony patch + 采样/落盘
```

运行模型：平时无感。`GameSave.LoadCurrentGame` prefix 开启记录会话；读档中所有采样存内存 List；finalizer（异常也触发）结束会话、落盘 TSV、打日志摘要；随后协程继续采样 `PostLoadSeconds` 秒（默认 120s，每 1s 一次）追加写入，覆盖读档后异步构建期。

## Hook 点（Harmony）

| Hook | 类型 | 作用 |
|------|------|------|
| `GameSave.LoadCurrentGame(string)` | prefix / finalizer | 会话开始/结束（含异常），落盘，启动读档后采样协程 |
| `PerformanceMonitor.BeginStream/EndStream` | postfix | 捕获存档 `Stream` 引用（原版 `stream` 字段私有且受 `DataProfilerOn` 门控，postfix 直接拿参数不受影响） |
| `PerformanceMonitor.BeginData/EndData(ESaveDataEntry)` | postfix | 按游戏自带分段点采样（每工厂×每子条目，约 1-2 万行，内存开销可忽略） |
| `PlanetFactory.Import(...)` | postfix | 逐工厂一行：astroId、entityCursor，定位到具体星球 |
| `DysonSphere.Import(...)` | postfix | 逐戴森球一行：星系索引（太阳帆是嫌疑大户） |

所有 patch 体 try/catch 包裹：**采样失败绝不能影响读档**。

## 每个采样点的指标

1. `t`：会话内秒数（Stopwatch）
2. `file_MB`：存档流 `Position`（该阶段消耗的文件字节）
3. `gc_live_MB`：`GC.GetTotalMemory(false)`
4. `mono_heap_MB` / `mono_used_MB`：P/Invoke `mono-2.0-bdwgc` 的 `mono_gc_get_heap_size()` / `mono_gc_get_used_size()`；首次失败（DllNotFound/EntryPointNotFound）则永久降级为 -1，不报错
5. `commit_MB` / `ws_MB`：P/Invoke `psapi!GetProcessMemoryInfo`（`PROCESS_MEMORY_COUNTERS_EX.PrivateUsage` / `WorkingSetSize`），比 Mono 的 `Process` 属性可靠；失败降级 `Process.GetCurrentProcess()`

## 输出

- 路径：`BepInEx/LoadMemProfiler/load_<存档名>_<yyyyMMdd_HHmmss>.tsv`
- TSV 列：`t_s  event  detail  file_MB  gc_live_MB  mono_heap_MB  mono_used_MB  commit_MB  ws_MB`
- event 取值：`session_begin` / `data_begin:<entry>` / `data_end:<entry>` / `factory` / `dyson_sphere` / `session_end` / `postload`
- 读档结束时在 BepInEx 日志输出摘要：峰值提交、按提交增量排序的 Top 阶段。

## 配置（BepInEx config）

- `Enabled`（默认 true）
- `PostLoadSeconds`（默认 120）
- `PostLoadIntervalSeconds`（默认 1.0）

## 错误处理

- 每个 Harmony patch 体整体 try/catch，异常只记一次日志后静默；
- P/Invoke 逐项探测降级（写 -1），不影响其余指标；
- 落盘失败时把摘要打进 BepInEx 日志兜底。

## 测试

- 无现成测试基建（与仓库其他 mod 一致），以编译 + 实机验证为准；
- 实机验证清单：小档读档产出 TSV 且列齐全；大档读档不崩、不明显变慢；卸载 mod 后行为不变。

## 阶段 2 候选工具箱（依诊断数据取舍，另立设计）

- 每导入 N 个工厂受控 `GC.Collect`（限制垃圾累积，换读档时间，符合用户取舍）；
- Boehm 参数：`GC_FREE_SPACE_DIVISOR` 等（需在 Mono 初始化前生效，可能要 Doorstop/启动器层面设置，可行性待验证）；
- 对定位到的分配大户做针对性 Harmony patch（复用缓冲/延迟分配/分配后主动归还）；
- 读档完成后强制 `GC.Collect` + 尝试让 Boehm 归还 OS。
