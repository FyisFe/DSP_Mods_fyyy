# InterstellarLogisticsOpt

依赖 / Requires **[UXAssist](https://thunderstore.io/c/dyson-sphere-program/p/soarqin/UXAssist/)**

## 中文

优化**星际物流**（星际运输塔）的调度开销，适用于上千座大塔的高强度冲糖场景。

两项优化：

- **调度提前退出**（默认开启）：对没有空闲运输船或电量不足的塔，直接跳过整轮供需配对扫描，省下白扫一圈的无用开销。
- **调度平摊 `AmortizeFactor`**（默认 `1` = 关闭）：设为 `2` 及以上时，每座塔的调度频率降为原来的 `1/N`（如 `5` = 每 50/150/300 tick 调度一次），总开销降到 `1/N`，且逐帧平摊、不产生卡顿尖峰。代价是物流响应变慢。

在游戏内 **UXAssist** 设置面板的「星际物流优化」标签页里实时开关与调节，无需重载存档。

> 注：平摊会让派单顺序与原版略有差异（吞吐量不受影响）。联机或依赖回放一致性时请自行斟酌。

## English

Cuts the scheduling CPU cost of **interstellar logistics** (stellar logistics stations) for large bases — thousands of stations under heavy throughput.

Two optimizations:

- **Dispatch early-exit** (always on): a station with no idle ship or too little energy skips the whole supply/demand scan instead of walking it just to find nothing to send.
- **Amortization `AmortizeFactor`** (default `1` = off): set `2`+ to schedule each station `1/N` as often (e.g. `5` = every 50/150/300 ticks instead of 10/30/60), cutting total scheduler CPU to `1/N` with load spread evenly across ticks (no spikes). Trades logistics responsiveness for CPU.

Toggle and tune live from the **InterstellarLogisticsOpt** tab in the in-game UXAssist panel — no save reload needed.

> Note: amortization changes dispatch ordering slightly vs vanilla (throughput unaffected). Evaluate before relying on it for multiplayer / replay determinism.

## 性能对比 / Performance comparison

场景：约 400 万/min 宇宙矩阵存档，模拟帧 (sampleAndHoldSim) = 200。关闭 → 开启（`AmortizeFactor = 5`）。
Scenario: ~4M/min universe-matrix save, simulated frames (sampleAndHoldSim) = 200; mod off → on (`AmortizeFactor = 5`).

**CPU 19.016 ms → 13.172 ms（约 −31% / ~31% lower frame time）**，GPU 与内存基本不变 / GPU & memory unchanged.

关闭 / Off — 19.016 ms

![Off / 关闭](https://raw.githubusercontent.com/FyisFe/DSP_Mods_fyyy/master/InterstellarLogisticsOpt/prior.png)

开启 / On (`AmortizeFactor = 5`) — 13.172 ms

![On / 开启](https://raw.githubusercontent.com/FyisFe/DSP_Mods_fyyy/master/InterstellarLogisticsOpt/after.png)
