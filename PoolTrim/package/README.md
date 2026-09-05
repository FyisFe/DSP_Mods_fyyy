# PoolTrim

<details>
<summary>中文看我</summary>

PoolTrim 在读档时清理传送带路径和建筑数据中多余的预留空间，降低大型存档的内存占用。

### 900w糖存档实测

存档大小 **46.68 GB**，包含 **266 个已建厂星球**。同一存档的两次运行对比如下：

| 指标 | 未启用 | 启用 |
|---|---:|---:|
| 读档时间（存档导入） | 233.54 秒 | **167.42 秒** |
| 读档后进程提交内存 | 131.04 GB | **78.41 GB** |
| 传送带路径数组容量 | 66.21 GB | **20.72 GB** |
| 实体及伴随数组容量 | 18.66 GB | **15.90 GB** |
| 路径点位利用率 | 31.3% | **100%** |

传送带路径回收约 **45.49 GB**，实体及伴随数组回收约 **2.76 GB**，合计减少约 **48.25 GB 数组容量**。

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

PoolTrim 在 `CargoPath.Import` 完成后，调用游戏自己的 `SetCapacity(实际长度)`，把三份数组一起缩到实际使用范围。有效数据复制到较小的数组，旧数组随后由垃圾回收器回收。

#### 实体及伴随数组：按同一编号一起缩容

每座建筑都有一个实体编号。建筑本身的数据，以及它的动画、状态标记、连接关系等，分别存放在多份数组中，使用同一编号索引。例如，编号为 1000 的建筑，会使用实体、动画和状态标记数组的第 1000 项。

这组数据包括实体、动画、状态标记、连接数组，以及互斥锁引用、需求引用和回收编号数组，合计每个容量槽位 **384 字节**。

PoolTrim 保留整段已用编号范围，同步裁剪这些数组后面的空闲尾部。中间已经拆除的建筑留下的空位，仍由原有回收列表管理；现有编号、连接数据和回收顺序保持原样。

实体数组裁剪时会额外预留已用编号范围 **12.5%** 的空间，至少保留 **1024 个空槽**；能够回收原容量至少 **12.5%** 时才执行整组复制。在这个存档中，172 个工厂满足条件，共回收约 **718.30 万个实体槽位**。

</details>

<details>
<summary>README</summary>

PoolTrim reduces memory use in large saves by trimming unused belt-path and building-data capacity during loading.

### 9M white-science save results

The save file is **46.68 GB** and contains **266 planets with factories**. Two runs of the same save compare as follows:

| Metric | Without PoolTrim | With PoolTrim |
|---|---:|---:|
| Load time (save import) | 233.54 s | **167.42 s** |
| Post-load process commit | 131.04 GB | **78.41 GB** |
| Belt-path array capacity | 66.21 GB | **20.72 GB** |
| Entity and companion array capacity | 18.66 GB | **15.90 GB** |
| Path-point utilization | 31.3% | **100%** |

Belt paths reclaim about **45.49 GB**, and entity and companion arrays reclaim about **2.76 GB**, reducing total array capacity by approximately **48.25 GB**.

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

After `CargoPath.Import` finishes, PoolTrim calls the game's own `SetCapacity(actual length)` to shrink all three arrays to the used range. Valid data is copied into smaller arrays, and the garbage collector reclaims the old arrays.

#### Entity and companion arrays: resizing by shared IDs

Each building has an entity ID. Its main data, animation, status signs and connections are stored in separate arrays indexed by that same ID. For example, building 1000 uses entry 1000 in the entity, animation and sign arrays.

This group includes entity, animation, sign and connection arrays, plus mutex references, demand references and recycled IDs. Together, they occupy **384 bytes per capacity slot**.

PoolTrim preserves the entire used ID range and trims the unused tails of these arrays together. Holes left by demolished buildings remain managed by the existing recycle list; IDs, connection data and recycle order stay intact.

Entity trimming reserves an additional **12.5%** of the used ID range, with at least **1,024 spare slots**. The group is copied when trimming can reclaim at least **12.5%** of its original capacity. In this save, 172 factories met that threshold, reclaiming approximately **7.183 million entity slots**.

</details>
