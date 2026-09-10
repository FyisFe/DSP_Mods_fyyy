# InterstellarLogisticsOpt

<details>
<summary>中文看我</summary>

减少星际运输塔寻找供需配对、决定派船时的 CPU 开销，适合物流塔较多的大型存档。需要安装 [UXAssist](https://thunderstore.io/c/dyson-sphere-program/p/soarqin/UXAssist/)。

### 使用

在游戏内 UXAssist 的「星际物流优化」页调整，修改后无需重新读档。

| 设置 | 默认值 | 作用 |
|---|---:|---|
| `Enabled` | `true` | 开启优化；关闭后使用原版调度 |
| `AmortizeFactor` | `1` | 调度间隔系数，范围 1 至 30。1 保持原版频率，数值越大，检查供需的次数越少 |

系数作用于全部航线，包括塔对塔、行星、恒星和物流分组。设为 5 时，原本每 10、30、60 tick 执行的检查，改为每 50、150、300 tick 执行。这里的 tick 是模拟更新次数。

### 优化了什么

本塔供需不足时，游戏仍可能需要维护对端的优先级锁。mod 会省去没有作用的库存读取和重复锁刷新，保留必要的锁维护和配对游标更新。这项优化在系数 1 时也生效。

调大系数后，派船检查和优先级锁的倒计时一起放慢。每轮按原版优先级和塔的顺序完整扫描，返程取货、飞船移动、已有订单、卸货和翘曲器补充照常处理。

### 使用时留意

系数越大，派船响应越慢，也可能限制物流吞吐。例如在 60 UPS、系数 5 下，一座忽略优先航线的塔，每分钟最多通过普通调度新派出 12 艘船；其他塔送取货和返程装货另算。航程较短时，更容易受到这个限制。

每轮扫描一次执行完，仍可能出现调度尖峰。CPU 开销和 UPS 不会按系数等比例变化，调大系数后也要留意生产和实际送货量。

关闭优化或切回系数 1，会恢复原版调度节奏。两个大于 1 的系数之间切换时，当前等待间隔结束后使用新值；重新读档会重置调度时钟。降频会改变派船时机，配送过程不保证与原版逐次相同。

### 性能截图

两张图的 SampleAndHoldSim Ratio 均为 200，按关闭和开启优化的顺序展示，开启时系数为 5。

| 面板读数 | 关闭 | 开启，系数 5 |
|---|---:|---:|
| CPU 圆环 | 19.708 ms | 12.354 ms |
| 物流调度 | 8.414 ms | 0.066 ms |

截图记录的是当时的面板读数，具体效果随存档、系数和其他 mod 设置变化。

关闭：

![关闭优化](https://raw.githubusercontent.com/FyisFe/DSP_Mods_fyyy/master/InterstellarLogisticsOpt/prior.png)

开启，系数 5：

![开启优化，系数 5](https://raw.githubusercontent.com/FyisFe/DSP_Mods_fyyy/master/InterstellarLogisticsOpt/after.png)

</details>

<details>
<summary>README</summary>

Reduces the CPU time spent matching supply and demand and dispatching interstellar ships in large saves. Requires [UXAssist](https://thunderstore.io/c/dyson-sphere-program/p/soarqin/UXAssist/).

### Usage

Change settings in the InterstellarLogisticsOpt tab of UXAssist. You do not need to reload the save.

| Setting | Default | Effect |
|---|---:|---|
| `Enabled` | `true` | Enable optimization; turning it off restores native dispatch |
| `AmortizeFactor` | `1` | Dispatch interval multiplier, from 1 to 30. At 1, checks keep their native frequency. Higher values run them less often |

The factor applies to all routes, including station, planet, star and logistics-group priorities. Factor 5 changes checks that normally run every 10, 30 or 60 ticks to every 50, 150 or 300 ticks. These are simulation ticks.

### What it changes

A station with insufficient supply or demand may still need to maintain another station's priority locks. The mod skips inventory reads and repeated lock refreshes that would have no effect, while keeping required lock updates and pair-cursor advancement. This also works at factor 1.

Higher factors slow dispatch checks and priority-lock countdowns together. Each sweep runs in full, in native priority and station order. Return loading, ship movement, existing orders, unloading and warper replenishment keep their normal behavior.

### Things to consider

Higher factors delay dispatch and may limit throughput. At 60 UPS and factor 5, a station set to ignore priority routes can launch at most 12 new ships per minute through ordinary dispatch. Deliveries and pickups by other stations, and return loading, are separate. Short routes are more likely to reach this limit.

Complete sweeps can still cause scheduling spikes. CPU time and UPS do not scale directly with the factor, so check production and actual deliveries after increasing it.

Disabling the mod or setting factor 1 restores native scheduling. When switching between factors above 1, the new value applies after the current waiting interval. Loading a save resets the dispatch clock. Changed dispatch timing means delivery histories can differ from the original game.

### Performance screenshots

Both screenshots use a SampleAndHoldSim Ratio of 200. They show the mod disabled, then enabled at factor 5.

| Panel reading | Disabled | Enabled, factor 5 |
|---|---:|---:|
| CPU ring | 19.708 ms | 12.354 ms |
| Logistics scheduling | 8.414 ms | 0.066 ms |

These are readings at the time of each screenshot. Results depend on the save, factor and other mod settings.

Disabled:

![Optimization disabled](https://raw.githubusercontent.com/FyisFe/DSP_Mods_fyyy/master/InterstellarLogisticsOpt/prior.png)

Enabled, factor 5:

![Optimization enabled, factor 5](https://raw.githubusercontent.com/FyisFe/DSP_Mods_fyyy/master/InterstellarLogisticsOpt/after.png)

</details>
