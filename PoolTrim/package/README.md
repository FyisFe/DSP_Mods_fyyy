# PoolTrim

## 中文

**大幅降低超大存档的内存占用**：读档时把每条传送带路径（CargoPath）的缓冲区容量裁剪到实际使用长度。

老存档反复拆建/切割/合并传送带时，路径会保留历史容量并原样写入存档，读档时按容量全额分配（每个点位 29 字节：货物缓冲 1B + 位置 12B + 旋转 16B）。大型基地存档里这部分纯浪费可以非常可观。

实测（45GB 存档、266 个工厂、123 万条路径）：

- 路径点位容量 22.8 亿 → 实际使用 7.1 亿（31%），**纯浪费 42GB**
- 进程提交内存：**122GB → 76GB（−38%）**
- 读档时间：216 秒 → 177 秒（分页减少，反而更快）

安全性：

- 只调用游戏原版 `SetCapacity` 缩容，活数据完整保留；之后铺设/修改传送带走原版按需扩容路径；
- **不修改存档格式**，随时可以卸载，无残留。

配置（BepInEx config）：

- `Enabled`（默认 true）
- `MarginPoints`（默认 0）：每条路径额外预留的点位余量。

## English

**Slashes RAM usage of huge saves** by trimming each cargo path (belt path) buffer down to its actually-used length at load time.

When belts are repeatedly removed/cut/merged, paths keep their historically grown capacity, which gets written to the save and fully re-allocated on load (29 bytes per point: cargo buffer 1B + position 12B + rotation 16B). In megabase saves this pure waste can be enormous.

Measured on a 45GB save (266 factories, 1.23M paths):

- Path point capacity 2.28B vs. 714M actually used (31%) — **42GB pure waste**
- Process committed memory: **122GB → 76GB (−38%)**
- Load time: 216s → 177s (less paging, actually faster)

Safety:

- Shrinking uses the vanilla `SetCapacity` (live data fully preserved); later belt edits re-grow capacity on demand through the vanilla path;
- **No save format changes** — remove the mod anytime, no residue.

Config:

- `Enabled` (default true)
- `MarginPoints` (default 0): extra headroom points kept per path.
