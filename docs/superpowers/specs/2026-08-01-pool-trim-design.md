# 读档内存优化 —— 阶段 2：PoolTrim（传送带路径容量裁剪）

日期：2026-08-01。前置：`2026-08-01-load-memory-profiler-design.md`（阶段 1 诊断）。

## 诊断结论（LoadMemProfiler v0.2.0 实测）

45GB 存档（Day24-结档，266 工厂）实测：

- 读档峰值 ≈ 稳态 ≈ 122GB 提交；Mono 堆 109.7GB，GC 存活 ~105GB —— **没有读档临时开销，堆几乎全是活数据**；Boehm 过度扩张、渲染暂存两个假设均被否定。
- 内存大头：BeltAndCargo 文件 21GB → 内存 72GB（3.4x）；Entity 4.3GB → 19.3GB（4.5x）。
- 容量报告：**CargoPath 点位总容量 22.8 亿、实际使用 7.1 亿（31%）**，每点 29B（buffer 1B + pointPos 12B + pointRot 16B），**松弛 = 42.4GB**。这是唯一的超大浪费源（实体池松弛 4GB，其余 <1.5GB）。
- 松弛成因：路径被切割/合并/重建时保留原容量（`CargoPath.Import` 按存档记录的 capacity 分配，老图反复拆建累积）。

## 方案

新 mod `PoolTrim`：Harmony postfix 挂 `CargoPath.Import`，导入完成后若 `buffer.Length > bufferLength + margin` 则调用原版 `SetCapacity(bufferLength + margin)` 缩容。

安全性依据：

- `SetCapacity` 按 `min(newCap, capacity)` 拷贝，缩容语义正确；活数据全部在 `bufferLength` 之内；
- 后续铺带扩容走原版按需 `SetCapacity` 增长路径，行为与新建路径一致；
- `CargoPath.Import` 仅在读档时被调用（`CargoTraffic.Import`），主线程串行，无并发问题；
- 不修改存档格式，卸载 mod 无残留。

明确不做（v1）：

- 实体池缩容：`SetEntityCapacity` 缩容会丢失 `entityRecycle` 内容（不拷贝旧数组）且 mutex 拷贝按旧容量长度、缩容抛异常，需要自行重写缩容逻辑，4GB 收益暂不值得；
- `CargoContainer.cargoPool` 缩容（1.2GB）：货物池运行时高频增删，暂缓。

## 实现

- postfix 用 `AccessTools.FieldRefAccess<CargoPath,int>("bufferLength")` 读私有长度，`buffer.Length` 即容量（公有）；
- try/catch 包裹、只记一次错误日志，异常不影响读档；
- 配置：`Enabled`（默认 true）、`MarginPoints`（默认 0，预留点数）。

## 预期效果（Day24-结档）

路径数组 61.7GB → ~20.7GB，进程提交 122GB → **~81GB**；页面文件压力大幅下降，读档时间预计也随分页减少而缩短。trim 过程逐路径进行，中途产生的旧数组垃圾由 GC 随读档回收，峰值远低于原版 122GB。

## 验证

- 用 LoadMemProfiler 同时开启再读同一档，对比峰值/稳态提交与容量报告（path 使用率应 ≈100%）；
- 游戏内验证：读档后传送带正常运行、能正常铺设/拆除传送带、存档可正常保存并再次读取。
