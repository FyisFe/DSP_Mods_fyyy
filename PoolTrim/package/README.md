# PoolTrim

<details>
<summary>中文看我</summary>

PoolTrim 在读档时清理传送带路径和建筑数据中多余的预留空间，降低大型存档的内存占用。可选的 `ColdGeometry` 进一步无损压缩路径几何，默认关闭。

### 900w糖存档实测

存档大小 **46.68 GB**，包含 **266 个已建厂星球**。同一存档在不同优化条件下的运行对比如下，内存统一使用十进制 GB：

| 指标 | 未启用 PoolTrim | 启用（仅容量裁剪） | 启用 ColdGeometry |
|---|---:|---:|---:|
| 读档时间（存档导入） | 233.54 秒 | **167.42 秒** | **226.88 秒** |
| 读档后进程提交内存 | 131.04 GB | **78.41 GB** | **67.67 GB** |
| 传送带路径数组容量 | 66.21 GB | **20.72 GB** | — |
| 实体及伴随数组容量 | 18.66 GB | **15.90 GB** | — |
| 路径点位利用率 | 31.3% | **100%** | — |

传送带路径回收约 **45.49 GB**，实体及伴随数组回收约 **2.76 GB**，合计减少约 **48.25 GB 数组容量**。启用 ColdGeometry 后，被冷存的路径几何载荷约 **19.56 → 8.78 GB**，进一步减少约 **10.78 GB**；相比仅容量裁剪，进程提交减少约 **10.74 GB**，读档增加约 **59.45 秒**。

### 配置项

启动一次游戏后生成 `BepInEx\config\fyyy.dsp.pooltrim.cfg`。修改后需重启游戏；容量裁剪始终启用。

- ⚠️ RISK： **`ColdGeometry`** (Default: `false` / 默认关闭，位于 `[Performance]`)
    - **效果:** 完整读档时，无损压缩传送带的点位坐标与朝向；显示、建造或编辑时按路径还原。货物占位、路径连接和运输逻辑继续使用原版数据。
    - **代价:** 减少运行内存，但增加读档耗时；首次到访星球和保存时也需要解压。已还原、编辑或新建的路径会保持展开，直到重新读档，内存收益可能逐渐减少。
    - **生效:** 将配置值改为 `true` 并重启游戏。

### 优化原理

#### 传送带路径：实际长度与预留容量

游戏会把连续的一段传送带合并成一条路径（`CargoPath`），沿路径划分点位来记录货物的位置。每个点位对应三份并行数组，合计 **29 字节**：

| 数组 | 内容 | 每个点位占用 |
|---|---|---:|
| `buffer` | 货物占位状态 | 1 字节 |
| `pointPos` | 点位坐标（Vector3） | 12 字节 |
| `pointRot` | 点位朝向（Quaternion） | 16 字节 |

这里有两个不同的长度：**实际长度**是路径正在使用的点位数，**容量**是数组已经分配的位置数。

1. 铺带延长路径时，游戏会扩大数组容量。
2. 拆带、切断路径后，路径变短，已有数组仍会保留原来的容量；复用旧路径对象时，也会沿用其预留空间。
3. 保存时，游戏只写入实际长度内的数据，同时把容量数值写进存档。
4. 读档时，游戏先按存档中的容量分配三份数组，再填入实际数据。

因此，老工厂经过多次改造后，数组可能远大于实际需要。这个900w糖存档的路径总容量约 **22.83 亿点**，实际只用了 **7.14 亿点**，其余约 **15.69 亿点**都占着数组空间。

PoolTrim 在 `CargoPath.Import` 完成后，调用游戏自己的 `SetCapacity(max(1, 实际长度))`，把三份数组一起缩到实际使用范围。有效数据复制到较小的数组，旧数组随后由垃圾回收器回收；空路径保留正容量以便原版继续扩容。

#### 实体及伴随数组：按同一编号一起缩容

每座建筑都有一个实体编号。建筑本身的数据，以及它的动画、状态标记、连接关系等，分别存放在多份数组中，使用同一编号索引。例如，编号为 1000 的建筑，会使用实体、动画和状态标记数组的第 1000 项。

这组数据包括实体、动画、状态标记、连接数组，以及互斥锁引用、需求引用和回收编号数组，合计每个容量槽位 **384 字节**。

PoolTrim 保留整段已用编号范围，同步裁剪这些数组后面的空闲尾部。中间已经拆除的建筑留下的空位，仍由原有回收列表管理；现有编号、连接数据和回收顺序保持原样。

实体数组裁剪时会额外预留已用编号范围 **12.5%** 的空间，至少保留 **1024 个空槽**；能够回收原容量至少 **12.5%** 时才执行整组复制。在这个存档中，172 个工厂满足条件，共回收约 **718.30 万个实体槽位**。


#### ColdGeometry: 压缩传送带点位数据

完整读档时，将 `pointPos` 和 `pointRot` 的 float 原始位模式做整数差分和字节分组，再使用 Windows 自带的 XPRESS_HUFF 无损压缩。压缩结果是**保存在内存中的字节数组**，与对应路径关联；两份压缩结果都准备完成后才释放原来的几何数组。坐标不量化、不重采样，负零和 NaN 位模式也逐位保留。收益不足以抵偿额外对象的小路径保持展开。

运输使用的 `buffer`、货物编号和连接不变。显示、建造、拆分或合并需要几何时，先完整还原坐标与朝向，再交回游戏使用。普通运输不会触发还原，离开星球也不会再次压缩。路径还在使用时，正常垃圾回收不会丢弃它的冷存数据；路径释放后才允许一起回收。

保存时只临时解压正在写出的坐标或朝向，仍写入原版路径数据，**不把内存压缩格式写进存档**。PoolTrim 不增加外部数据文件，正常保存后可以移除插件再读档。LossyCompression 自身的有损存档选项仍决定其存档格式；其他直接读取几何数组的模组需逐项核验。

冷存不是备份：当前会话不另留一份未压缩副本。崩溃或断电后可重读磁盘上最后一份完好的存档，未保存进度仍会丢失。若压缩数据被异常破坏，目前只检查解压是否成功及输出长度，没有另加内容校验，不能保证识别所有损坏。原版手动覆盖存档会先截断目标文件，保存中途报错可能留下不完整文件；因此不能保证覆盖唯一存档后仍可恢复。原版自动保存先写临时文件，成功后才轮换存档。

离线检查覆盖逐位还原、存活路径经过 GC 后仍能完整保存、冷存保存字节一致性、保存失败、并发首次还原，以及原版路径的运输、扩容、拆分和合并。游戏内已验证完整读档与内存表现；首次到访星球、拆建和保存重读仍需游戏内验证。

</details>

<details>
<summary>README</summary>

PoolTrim reduces memory use in large saves by trimming unused belt-path and building-data capacity during loading. Optional `ColdGeometry` further compresses path geometry losslessly and is off by default.

### 9M white-science save results

The save file is **46.68 GB** and contains **266 planets with factories**. Runs with different optimization settings compare as follows. All memory values use decimal GB.

| Metric | Without PoolTrim | Capacity trimming only | With ColdGeometry |
|---|---:|---:|---:|
| Load time (save import) | 233.54 s | **167.42 s** | **226.88 s** |
| Post-load process commit | 131.04 GB | **78.41 GB** | **67.67 GB** |
| Belt-path array capacity | 66.21 GB | **20.72 GB** | — |
| Entity and companion array capacity | 18.66 GB | **15.90 GB** | — |
| Path-point utilization | 31.3% | **100%** | — |

Belt paths reclaim about **45.49 GB**, and entity and companion arrays reclaim about **2.76 GB**, reducing total array capacity by approximately **48.25 GB**. With ColdGeometry, the geometry selected for cold storage shrinks from about **19.56 to 8.78 GB**, saving another **10.78 GB** of payload. Compared with trimming alone, process commit drops by about **10.74 GB**, while loading takes about **59.45 s** longer.

### Configuration

Run the game once to generate `BepInEx\config\fyyy.dsp.pooltrim.cfg`. Restart the game after changing it. Capacity trimming is always enabled.

- ⚠️ RISK: **`ColdGeometry`** (Default: `false`, section `[Performance]`)
    - **Effect:** Losslessly compresses belt-path positions and rotations during full-save loading, restoring geometry for display or editing. Cargo occupancy, connections and transport continue using vanilla data.
    - **Trade-off:** Less runtime memory, longer loading and decompression work when visiting planets or saving. Restored, edited and new paths remain expanded until the next load, so savings may decrease over time.
    - **Activation:** Set it to `true` and restart the game.

### How it works

#### Belt paths: used length and reserved capacity

The game groups a continuous run of belts into a path (`CargoPath`), divided into points that track cargo positions. Each point has entries in three parallel arrays, totaling **29 bytes**:

| Array | Contents | Bytes per point |
|---|---|---:|
| `buffer` | Cargo occupancy | 1 byte |
| `pointPos` | Point position (Vector3) | 12 bytes |
| `pointRot` | Point orientation (Quaternion) | 16 bytes |

Two different sizes matter: **length** is the number of points the path uses, while **capacity** is the number of slots already allocated.

1. Extending a belt grows its array capacity.
2. Removing belts or cutting a path shortens it, while existing arrays retain their capacity. Reused path objects also retain their reserved space.
3. Saving writes only the data within the used length, along with the capacity value.
4. Loading allocates all three arrays at the saved capacity, then fills them with the actual data.

After repeated rebuilding, an old factory can have far more allocated space than it uses. This 9M save has about **2.283 billion points of capacity**, but uses only **714 million points**, leaving approximately **1.569 billion points** of unused array space.

After `CargoPath.Import` finishes, PoolTrim calls the game's own `SetCapacity(max(1, actual length))` to shrink all three arrays to the used range. Valid data is copied into smaller arrays, and the garbage collector reclaims the old arrays. Empty paths retain positive capacity for vanilla growth.

#### Entity and companion arrays: resizing by shared IDs

Each building has an entity ID. Its main data, animation, status signs and connections are stored in separate arrays indexed by that same ID. For example, building 1000 uses entry 1000 in the entity, animation and sign arrays.

This group includes entity, animation, sign and connection arrays, plus mutex references, demand references and recycled IDs. Together, they occupy **384 bytes per capacity slot**.

PoolTrim preserves the entire used ID range and trims the unused tails of these arrays together. Holes left by demolished buildings remain managed by the existing recycle list; IDs, connection data and recycle order stay intact.

Entity trimming reserves an additional **12.5%** of the used ID range, with at least **1,024 spare slots**. The group is copied when trimming can reclaim at least **12.5%** of its original capacity. In this save, 172 factories met that threshold, reclaiming approximately **7.183 million entity slots**.

#### ColdGeometry: compressing belt-path points

During full-save loading, PoolTrim applies integer deltas and byte grouping to the raw float bit patterns in `pointPos` and `pointRot`, then compresses them with Windows XPRESS_HUFF. The result is a pair of **byte arrays held in RAM**, associated with the path. Original geometry arrays are released only after both compressed results are ready. No quantization or resampling occurs; signed zero and NaN bit patterns are preserved exactly. Paths with insufficient savings remain expanded.

Cargo buffers, IDs and connections stay unchanged. Display, construction, splitting and merging restore both geometry arrays before the game uses them. Normal transport does not restore geometry, and leaving a planet does not compress it again. Normal garbage collection retains cold data while its path remains in use; released paths can be collected with their data.

Saving temporarily decodes the positions or rotations being written and writes vanilla path data, **not the in-memory compression format**. PoolTrim adds no external save file and can be removed after a normal save. LossyCompression's own lossy options still control its save format; other mods that directly access geometry arrays require compatibility checks.

Cold storage is not a backup: no second uncompressed copy is retained in the current session. After a crash or power loss, the last intact disk save can be reloaded, but unsaved progress is lost. The codec checks decompression success and output length without an additional content checksum, so it cannot guarantee detection of every corruption. Vanilla manual saving truncates the target file before writing; a mid-save failure can leave it incomplete. Recovery is not guaranteed after overwriting the only save. Vanilla autosaving writes a temporary file first and rotates slots only after success.

Offline checks cover exact bit recovery, retention across GC while a path is alive, byte-identical cold saves, failed saves, concurrent first access, and vanilla transport, growth, splitting and merging. Full loading and memory use have been measured in-game; first visits, editing and save/reload still need in-game validation.

</details>
