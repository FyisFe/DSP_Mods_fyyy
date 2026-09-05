# PoolTrim

<details>
<summary>中文看我</summary>

PoolTrim 在读档时裁剪传送带路径和建筑数据中多余的预留空间，减少大型存档的内存占用。还可以开启 `ColdGeometry`，无损压缩传送带的坐标和朝向，进一步节省内存。此选项默认关闭。

### 900w糖存档实测

存档大小 **46.68 GB**，包含 **266 个已建厂星球**。下表对比同一存档在不同优化条件下的表现，内存统一使用十进制 GB：

| 指标 | 未启用 PoolTrim | 启用（仅容量裁剪） | 启用 ColdGeometry |
|---|---:|---:|---:|
| 读档时间（存档导入） | 233.54 秒 | **167.42 秒** | **226.88 秒** |
| 读档后进程提交内存 | 131.04 GB | **78.41 GB** | **67.67 GB** |
| 传送带路径数组容量 | 66.21 GB | **20.72 GB** | — |
| 实体及伴随数组容量 | 18.66 GB | **15.90 GB** | — |
| 路径点位利用率 | 31.3% | **100%** | — |

容量裁剪为传送带路径回收约 **45.49 GB**，为实体及伴随数组回收约 **2.76 GB**，合计减少约 **48.25 GB 数组容量**。启用 ColdGeometry 后，冷存的路径几何数据从约 **19.56 GB** 压缩到 **8.78 GB**，再节省约 **10.78 GB**。相比仅容量裁剪，进程提交内存减少约 **10.74 GB**，读档多花约 **59.45 秒**。

### 配置项

首次启动游戏后，会生成配置文件 `BepInEx\config\fyyy.dsp.pooltrim.cfg`。修改配置后需重启游戏。容量裁剪始终启用。

- ⚠️ RISK： **`ColdGeometry`** (Default: `false` / 默认关闭，位于 `[Performance]`)
    - **效果:** 完整读档时，无损压缩传送带的点位坐标与朝向。显示、建造或编辑需要这些数据时，再按路径还原。货物占位、路径连接和运输逻辑仍使用原版数据。
    - **代价:** 节省运行内存，但读档更慢。首次到访星球和保存时也需要解压。已还原、编辑或新建的路径会保持未压缩状态，直到重新读档，因此节省的内存可能逐渐减少。
    - **生效:** 将配置值改为 `true` 并重启游戏。

### 优化原理

#### 传送带路径：实际长度与预留容量

游戏会把连续的一段传送带合并成一条路径（`CargoPath`），沿路径划分点位来记录货物的位置。每个点位在三份数组中各占一项，合计 **29 字节**：

| 数组 | 内容 | 每个点位占用 |
|---|---|---:|
| `buffer` | 货物占位状态 | 1 字节 |
| `pointPos` | 点位坐标（Vector3） | 12 字节 |
| `pointRot` | 点位朝向（Quaternion） | 16 字节 |

**实际长度**是路径正在使用的点位数，**容量**是数组已经分配的槽位数。

1. 铺带延长路径时，游戏会扩大数组容量。
2. 拆带、切断路径后，路径变短，已有数组仍会保留原来的容量；复用旧路径对象时，也会沿用其预留空间。
3. 保存时，游戏只写入实际长度内的数据，同时把容量数值写进存档。
4. 读档时，游戏先按存档中的容量分配三份数组，再填入实际数据。

老工厂经过多次改造后，数组可能远大于实际需要。这个 900w 糖存档的路径总容量约 **22.83 亿点**，实际只用了 **7.14 亿点**，其余约 **15.69 亿点**都占着数组空间。

PoolTrim 在 `CargoPath.Import` 完成后，调用游戏自己的 `SetCapacity(max(1, 实际长度))`，把三份数组一起缩到实际使用范围。游戏将有效数据复制到较小的数组，旧数组随后由垃圾回收器回收。空路径至少保留一个槽位，让原版扩容逻辑能继续工作。

#### 实体及伴随数组：按同一编号一起缩容

每座建筑都有一个实体编号。建筑本身的数据，以及它的动画、状态标记、连接关系等，分别存放在多份数组中，使用同一编号索引。例如，编号为 1000 的建筑，会使用实体、动画和状态标记数组的第 1000 项。

这组数据包括实体、动画、状态标记、连接数组，以及互斥锁引用、需求引用和回收编号数组，合计每个容量槽位 **384 字节**。

PoolTrim 保留整段已用编号范围，一起裁剪这些数组末尾的空闲空间。拆除建筑后留在中间的空位，仍由原有回收列表管理；现有编号、连接数据和回收顺序保持原样。

裁剪时会按已用编号范围额外预留 **12.5%** 的空间，至少保留 **1024 个空槽**。只有能回收原容量的 **12.5%** 或更多时，才复制整组数组。这个存档有 172 个工厂满足条件，共回收约 **718.30 万个实体槽位**。


#### ColdGeometry: 压缩传送带点位数据

完整读档时，PoolTrim 对 `pointPos` 和 `pointRot` 中 float 的原始位模式做整数差分和字节分组，再用 Windows 自带的 XPRESS_HUFF 无损压缩。压缩结果是**保存在内存中的字节数组**，与对应路径关联。两份压缩结果都准备好后，才释放原来的几何数组。整个过程不量化坐标，也不重新采样，负零和 NaN 的位模式同样逐位保留。如果小路径节省的空间还不够抵消额外对象的开销，就保留原数组。

运输使用的 `buffer`、货物编号和连接不变。显示、建造、拆分或合并需要几何数据时，PoolTrim 先完整还原坐标与朝向，再交给游戏使用。普通运输不会触发还原，离开星球也不会再次压缩。只要路径还在使用，正常垃圾回收就不会丢弃它的压缩数据；路径释放后，这些数据才可以一并回收。

保存时，PoolTrim 只临时解压正在写出的坐标或朝向，按原版格式写入路径数据，**不把内存压缩格式写进存档**。它不增加外部数据文件，正常保存后可以移除插件再读档。

</details>

<details>
<summary>README</summary>

PoolTrim reduces memory use in large saves by trimming unused capacity in belt paths and building data during loading. You can also enable `ColdGeometry` to save more memory by losslessly compressing belt positions and rotations. This option is off by default.

### 9M white-science save results

The save file is **46.68 GB** and contains **266 planets with factories**. The table compares the same save with different optimization settings. All memory values use decimal GB.

| Metric | Without PoolTrim | Capacity trimming only | With ColdGeometry |
|---|---:|---:|---:|
| Load time (save import) | 233.54 s | **167.42 s** | **226.88 s** |
| Post-load process commit | 131.04 GB | **78.41 GB** | **67.67 GB** |
| Belt-path array capacity | 66.21 GB | **20.72 GB** | — |
| Entity and companion array capacity | 18.66 GB | **15.90 GB** | — |
| Path-point utilization | 31.3% | **100%** | — |

Capacity trimming reclaims about **45.49 GB** from belt paths and **2.76 GB** from entity and companion arrays, for a total of about **48.25 GB** in array capacity. With ColdGeometry, the geometry stored in compressed form shrinks from about **19.56 to 8.78 GB**, saving another **10.78 GB**. Compared with trimming alone, process commit drops by about **10.74 GB**, while loading takes about **59.45 s** longer.

### Configuration

Run the game once to generate `BepInEx\config\fyyy.dsp.pooltrim.cfg`. Restart the game after changing it. Capacity trimming is always enabled.

- ⚠️ RISK: **`ColdGeometry`** (Default: `false`, section `[Performance]`)
    - **Effect:** Losslessly compresses belt positions and rotations when loading a full save. Paths are restored as needed for rendering, construction or editing. Cargo occupancy, connections and transport still use vanilla data.
    - **Trade-off:** Uses less memory but takes longer to load. First visits to planets and saving also require decompression. Restored, edited and new paths stay uncompressed until the next load, so memory savings may decrease over time.
    - **Activation:** Set it to `true` and restart the game.

### How it works

#### Belt paths: used length and reserved capacity

The game groups a continuous run of belts into a path (`CargoPath`) and divides it into points to track cargo positions. Each point uses one entry in each of three arrays, totaling **29 bytes**:

| Array | Contents | Bytes per point |
|---|---|---:|
| `buffer` | Cargo occupancy | 1 byte |
| `pointPos` | Point position (Vector3) | 12 bytes |
| `pointRot` | Point orientation (Quaternion) | 16 bytes |

**Length** is the number of points the path uses. **Capacity** is the number of slots already allocated.

1. Extending a belt grows its array capacity.
2. Removing belts or cutting a path shortens it, while existing arrays retain their capacity. Reused path objects also retain their reserved space.
3. Saving writes only the data within the used length, along with the capacity value.
4. Loading allocates all three arrays at the saved capacity, then fills them with the actual data.

After repeated rebuilding, an old factory can have far more allocated space than it needs. This 9M save has capacity for about **2.283 billion points**, but uses only **714 million points**. About **1.569 billion points** remain unused but still take up array space.

After `CargoPath.Import` finishes, PoolTrim calls the game's own `SetCapacity(max(1, actual length))` to shrink all three arrays to the used range. The game copies valid data into smaller arrays, and the garbage collector reclaims the old ones. Empty paths keep at least one slot so the game's capacity growth logic can still work.

#### Entity and companion arrays: resizing by shared IDs

Each building has an entity ID. Its main data, animation, status signs and connections are stored in separate arrays indexed by that same ID. For example, building 1000 uses entry 1000 in the entity, animation and sign arrays.

This group includes entity, animation, sign and connection arrays, plus mutex references, demand references and recycled IDs. Together, they occupy **384 bytes per capacity slot**.

PoolTrim keeps the entire used ID range and trims the unused space at the end of each array. The existing recycle list still manages gaps left by demolished buildings. IDs, connection data and recycle order stay intact.

PoolTrim reserves an extra **12.5%** of the used ID range, with at least **1,024 spare slots**. It copies the group of arrays only if trimming can reclaim at least **12.5%** of the original capacity. In this save, 172 factories met that threshold, freeing about **7.183 million entity slots**.

#### ColdGeometry: compressing belt-path points

When loading a full save, PoolTrim applies integer deltas and byte grouping to the raw float bit patterns in `pointPos` and `pointRot`, then compresses them with Windows XPRESS_HUFF. The result is a pair of **byte arrays held in RAM**, associated with the path. PoolTrim releases the original geometry arrays only after both compressed results are ready. It does not quantize coordinates or resample points, and it preserves signed zero and NaN bit patterns exactly. Small paths keep their original arrays if compression would not save enough space to offset the extra objects.

Cargo buffers, IDs and connections stay unchanged. When rendering, construction, splitting or merging needs geometry, PoolTrim restores both arrays before passing them to the game. Normal transport does not trigger restoration, and leaving a planet does not compress the arrays again. Normal garbage collection keeps the compressed data as long as its path is in use. Once the path is released, its data can be collected too.

When saving, PoolTrim temporarily decompresses the positions or rotations being written. It writes the original path data, **not the in-memory compression format**. PoolTrim creates no extra data files, so you can remove it after a normal save and load that save again.

</details>
