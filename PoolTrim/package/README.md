# PoolTrim

## 中文

**大幅降低超大存档的内存占用**：读档时把每条传送带路径（CargoPath）的缓冲区容量裁剪到实际使用长度。无配置项，装上即生效；实测 45GB 存档读档内存 122GB → 76GB。

### 原理

游戏不逐格模拟传送带，而是把首尾相连、中间无分叉的一串带子合并成一条"路径"（`CargoPath`）整体处理（遇到分流器、汇入点就断开成新路径）。每条路径按**点位**存储数据，一格传送带对应约 30 个点位（点位是货物定位的最小单位），每个点位需要 3 份并行数组共 **29 字节**：

| 数组 | 内容 | 每点字节 |
|------|------|---------|
| `buffer` | 货物占位状态 | 1 |
| `pointPos` | 点位世界坐标 (Vector3) | 12 |
| `pointRot` | 点位朝向 (Quaternion) | 16 |

这些数组按路径的**容量**（capacity）分配，而实际数据只占**长度**（bufferLength）。容量只增不减：

- 铺带延长路径时容量按需扩张；
- **切断/拆除/合并传送带时，拆分出的路径保留原路径的大容量**——这是浪费的主要来源；
- 存档时会把这个虚高的容量数字原样写入存档（但数据本体只写长度部分）；
- 读档时 `CargoPath.Import` 先按存档里的容量数字全额分配三份数组，再只填入长度部分的数据。

于是一个反复改造过的老基地，容量与实际长度的比值会越滚越大。实测一个 45GB 的存档（266 颗已建设行星、2350 万格传送带合并为 123 万条路径）：**点位总容量 22.8 亿，实际使用仅 7.1 亿（31%）**，即 22.8 亿 × 29B 中有 **42GB 是纯浪费**——分配了、提交了物理内存/页面文件，但永远不会被读写。

### 本 mod 的做法

Harmony postfix 挂在 `CargoPath.Import` 上：每条路径导入完成后，若容量 > 长度，就调用**游戏原版的** `SetCapacity(长度)` 缩容一次。旧的大数组随读档过程被 GC 回收。

### 为什么安全

- `SetCapacity` 是原版自带的重分配函数（按新旧容量的较小值拷贝），所有活数据都在长度之内，缩容不丢任何东西；
- 之后再铺带需要扩容时，走的是原版按需扩容的老路，与新建路径行为完全一致；
- 只在读档时干预一次，不碰运行时逻辑、不改存档格式；卸载 mod 无任何残留；
- 附带的好处：裁剪后的容量会随下次存档写入，此后即使移除本 mod，该存档读档也不再膨胀。

### 实测（45GB 存档，64GB 内存机器）

| 指标 | 无 mod | 有 mod |
|------|--------|--------|
| 进程提交内存峰值 | 122GB | **76GB（−38%）** |
| 读档时间 | 216 秒 | **177 秒**（分页减少，反而更快）|
| 路径点位利用率 | 31% | 100% |

## English

**Slashes RAM usage of huge saves** by trimming each belt path (`CargoPath`) buffer down to its actually-used length at load time. No config, works out of the box; measured 122GB → 76GB load RAM on a 45GB save.

### How it works

The game doesn't simulate belts tile by tile — an unbranched run of connected belts is merged into one *path* (`CargoPath`), split at splitters and merge points. Each path stores per-point data in three parallel arrays, **29 bytes per point**: `buffer` (cargo occupancy, 1B), `pointPos` (Vector3, 12B), `pointRot` (Quaternion, 16B). One belt tile spans ~30 points (points are the finest unit of cargo positioning).

These arrays are allocated at the path's **capacity**, while real data only fills its **length**. Capacity never shrinks:

- extending a belt grows capacity on demand;
- **cutting/removing/merging belts leaves the split paths with the original path's large capacity** — the main source of waste;
- saving writes this inflated capacity number into the save file (though only the length's worth of data);
- loading (`CargoPath.Import`) allocates all three arrays at the saved capacity, then fills only the length.

So a long-lived, heavily-rebuilt base accumulates slack indefinitely. Measured on a 45GB save (266 built-up planets, 23.5M belt tiles merged into 1.23M paths): **2.28 billion points of capacity vs. 714 million actually used (31%)** — **42GB of committed memory that is never read or written**.

### What the mod does

A Harmony postfix on `CargoPath.Import`: after each path is imported, if capacity > length, call the **vanilla** `SetCapacity(length)` once. The old oversized arrays are garbage-collected as loading proceeds.

### Why it's safe

- `SetCapacity` is the game's own resize routine (copies `min(old, new)`); all live data lies within the length, so nothing is lost;
- later belt edits re-grow capacity through the exact same vanilla on-demand path as a fresh belt would;
- it intervenes once at load time only — no runtime logic touched, no save format changes, remove anytime with zero residue;
- bonus: the trimmed capacity is written into your next save, so that save loads lean even without the mod.

### Measured (45GB save, 64GB RAM machine)

| Metric | Without | With |
|--------|---------|------|
| Peak committed memory | 122GB | **76GB (−38%)** |
| Load time | 216s | **177s** (less paging — actually faster) |
| Path point utilization | 31% | 100% |
