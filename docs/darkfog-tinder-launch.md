# Dark Fog 火种 (Tinder) 发射逻辑 — 完整解析与版本对比

> 旧代码: `DSP_Mods/GameCode/`
> 新代码: `DSP_Mods_fyyy/GameCode-latest/`

---

## 一、火种系统总览

火种 (Tinder) 是黑雾 Hive 向其他星系扩张的核心机制。一个完整的火种生命周期：

```
Hive 成长 → 核心 Core 建造火种 → 火种在 dock 待命充能
  → 选择目标星系 → 发射 → 星际飞行 → 到达目标 Hive → 资源/经验转移
```

### 关键源文件

| 文件 | 职责 |
|------|------|
| `DFSCoreComponent.cs` | Hive 核心，控制火种建造触发和进度推进 |
| `DFTinderComponent.cs` | 火种实体，控制充能、目标选择、发射、飞行、停靠 |
| `EnemyDFHiveSystem.cs` | Hive 主系统，执行 deferred 创建，管理 idle 列表 |

### 核心数据结构

```csharp
// DFSCoreComponent — Hive 核心
struct DFSCoreComponent {
    int buildTinderSp;                    // 当前建造进度
    int buildTinderSpMax;                 // 建造总工期
    int buildTinderSpMatter;              // 每 tick 物质消耗
    int buildTinderSpEnergy;              // 每 tick 能量消耗
    int buildTinderCount;                 // 已建造火种总数
    int buildTinderTriggerMinTick;        // 最早可触发 tick
    int buildTinderTriggerKeyTick;        // 概率检测间隔
    float buildTinderTriggerProbability;  // 基础触发概率
}

// DFTinderComponent — 火种实体
struct DFTinderComponent {
    int direction;   // 0=idle, 1=已发射
    int stage;       // -2=出发dock, -1=离开hive, 0=星际飞行, 1=到达hive, 2=目标dock
    float uSpeed;    // 当前飞行速度
    float param0;    // 动画参数
    int originHiveAstroId;     // 出发 hive
    int targetHiveAstroId;     // 目标 hive
    int[] sortedStarIndices;   // 目标星系排序缓存
    int[] starValues;          // 星系权重累积分布
    int[] starBlankOrbitCount; // 各星系空轨道数

    static int lastTotalExistingTinders;  // [新版] 全局已有火种数
    static int lastTotalSailingTinders;   // [新版] 全局在途火种数
    static int lastAliveHiveCount;        // [新版] 全局存活 hive 数
}
```

---

## 二、阶段 1 — 火种建造

入口: `DFSCoreComponent.LogicTick()` — 每个 key tick 由 `EnemyDFHiveSystem.KeyTickLogic()` 调用。

### 2.1 前置条件（新旧一致）

同时满足以下 4 个条件：

1. `hive.ticks >= buildTinderTriggerMinTick` — hive 存在时间超过阈值
2. `hive.idleTinderCount < hive.tinderDocks.Length` — dock 有空位
3. `builder.matter >= maxMatter * 0.75` — 物质储量 >= 75%
4. `builder.energy >= maxEnergy * 0.5` — 能量储量 >= 50%

### 2.2 子阶段 A：周期触发开始建造 (`buildTinderSp == 0`)

当未在建造时，周期性检测是否开始新的建造。

#### 旧版 (DFSCoreComponent.cs:134)

```csharp
// 每 buildTinderTriggerKeyTick 个 tick 检查一次，固定周期（所有 hive 同步）
if (hive.ticks % this.buildTinderTriggerKeyTick == 0
    && RandomTable.Integer(ref hive.rtseed, 10000)
       < this.buildTinderTriggerProbability * 10000.0 - 0.5)
```

- 随机源: `RandomTable.Integer`
- 概率: 固定值 `buildTinderTriggerProbability`，不受任何动态因子影响
- 所有 hive 在同一 tick 检测（`ticks % keyTick == 0`）

#### 新版 (DFSCoreComponent.cs:144-157)

```csharp
// 每 hive 有独立的检测相位偏移
int num2 = hive.hiveAstroId % 1000000 * 127;  // sbyte.MaxValue = 127
if (hive.ticks % this.buildTinderTriggerKeyTick == num2 % this.buildTinderTriggerKeyTick)
{
    double num3 = GetTargetTinderYieldRatio(hive.sector)  // [新增] 全局平衡因子
                * growthSpeedFactor                        // [新增] 游戏设置倍率
                * buildTinderTriggerProbability
                * Math.Pow(0.97, lastTotalExistingTinders); // [新增] 指数衰减
    if (hive.trand.NextDouble() < num3) { ... }
}
```

**变化:**

| 项目 | 旧版 | 新版 |
|------|------|------|
| 检测相位 | 所有 hive 同步 (`ticks % keyTick == 0`) | 各 hive 独立偏移 (`== num2 % keyTick`) |
| 随机源 | `RandomTable.Integer(10000)` | `hive.trand.NextDouble()` |
| 概率公式 | `buildTinderTriggerProbability` (固定) | `YieldRatio × growthSpeed × probability × 0.97^existingTinders` |
| 动态因子 | 无 | growthSpeedFactor, YieldRatio, existingTinders 指数衰减 |

### 2.3 子阶段 B：持续建造 (`buildTinderSp > 0`)

建造已开始后，每 tick 消耗资源推进进度。

#### 旧版 (DFSCoreComponent.cs:117-132)

```csharp
// 每 tick 确定性消耗，无概率判定
if (local.matter > buildTinderSpMatter && local.energy > buildTinderSpEnergy)
{
    local.matter -= buildTinderSpMatter;
    local.energy -= buildTinderSpEnergy;
    ++buildTinderSp;
}
if (buildTinderSp >= buildTinderSpMax) { /* 建造完成 */ }
```

- **每 tick 必定推进**（只要资源够）
- 建造时长 = `buildTinderSpMax` 个 tick（确定性）

#### 新版 (DFSCoreComponent.cs:124-142)

```csharp
if (local.matter > buildTinderSpMatter && local.energy > buildTinderSpEnergy)
{
    flag = true;
    // [新增] 概率判定是否实际消耗资源
    if (hive.trand.NextDouble() < 0.75
        * Math.Pow(
            Math.Pow(0.92, Math.Sqrt(1.0 / growthSpeedFactor)),
            lastTotalExistingTinders)
        * growthSpeedFactor)
    {
        local.matter -= buildTinderSpMatter;
        local.energy -= buildTinderSpEnergy;
        ++buildTinderSp;
    }
}
```

- **每 tick 有概率推进**，不再确定性
- 概率公式: `0.75 × 0.92^(sqrt(1/g) × N) × g`
  - `g` = growthSpeedFactor (0.1~10)
  - `N` = lastTotalExistingTinders（全局已有火种总数）
- 已有火种越多，单次建造推进概率越低（0.92 底数指数衰减）
- 实际建造时长变为随机变量，期望值 = `buildTinderSpMax / P(建造)`

**变化:**

| 项目 | 旧版 | 新版 |
|------|------|------|
| 每 tick 推进 | 确定性（100%） | 概率性（0.75 × 衰减） |
| 建造时长 | 固定 `buildTinderSpMax` tick | 随机，受已有火种数影响 |
| 衰减机制 | 无 | `0.92^(sqrt(1/g) × N)` 指数衰减 |

### 2.4 新增: `GetTargetTinderYieldRatio()` 全局平衡函数

旧版不存在此函数。新版 DFSCoreComponent.cs:210-235:

```csharp
public static double GetTargetTinderYieldRatio(SpaceSector sector)
{
    int starCount = sector.galaxy.starCount;
    int starsWithHives = 0;  // 有存活 hive 的星系数
    int aliveHives = 0;      // 存活 hive 总数
    // ... 遍历统计 ...
    double y = (0.5 - starsWithHives / starCount) * 2.0;
    double x = 5.0 / (aliveHives + 1.0);
    return Math.Pow(5.0, y) * Math.Pow(x, 0.8);
}
```

直觉理解:
- 当 hive 覆盖 0% 星系时: `5^1.0 × (5/1)^0.8 ≈ 5 × 3.62 ≈ 18.1` — 极高触发率
- 当 hive 覆盖 25% 星系时: `5^0.5 × ...` — 中等
- 当 hive 覆盖 50% 星系时: `5^0.0 × ... = 1 × ...` — 基准
- 当 hive 覆盖 75%+ 星系时: `5^-0.5 × ...` — 大幅降低
- 存活 hive 数越多，`(5/(N+1))^0.8` 越小

**效果: 黑雾覆盖越广，新火种触发概率越低，防止后期雪崩扩张。**

### 2.5 虚拟 Hive 建造 (`LogicTickVirtual`)

未实体化的 hive 使用简化逻辑，用 `savedWorkProgress` 代替 `buildTinderSp`。

#### 旧版 (DFSCoreComponent.cs:164-180)

```csharp
// 子阶段 B: 每 tick 确定性推进
if (savedWorkProgress > 0) {
    ++savedWorkProgress;  // 无概率判定
    ...
}
// 子阶段 A: 同样的固定概率
else if (ticks % keyTick == 0
    && RandomTable.Integer(10000) < probability * 10000 - 0.5) { ... }
```

#### 新版 (DFSCoreComponent.cs:186-207)

```csharp
// 子阶段 B: [新增] 概率判定
if (savedWorkProgress > 0) {
    if (hive.trand.NextDouble() >= 0.75
        * Math.Pow(Math.Pow(0.92, Math.Sqrt(1.0 / g)), lastTotalExistingTinders) * g)
        return;  // 概率失败则跳过
    ++savedWorkProgress;
    ...
}
// 子阶段 A: [新增] 相位偏移 + YieldRatio + 衰减
else {
    int num4 = hiveAstroId % 1000000 * 127;
    if (ticks % keyTick != num4 % keyTick) return;
    double p = YieldRatio * g * probability * 0.97^lastTotalExistingTinders;
    if (hive.trand.NextDouble() >= p) return;
    ...
}
```

**变化: 虚拟 hive 现在与实体 hive 使用完全相同的概率公式。** 旧版虚拟 hive 建造是确定性的（每 tick 必推进），新版加入了同样的指数衰减。

---

## 三、阶段 2 — 火种充能

入口: `DFTinderComponent.PrepareDispatchLogic()` — 火种建成后在 dock 待命（stage=-2, direction=0）。

充能逻辑新旧一致，无变化:

### 实体化 Hive
从 dock builder 转移资源:
- 物质: 每 tick 转移量 = `min(需求量, spMatter × 6, dock_builder剩余 - maxMatter × 60%)`
- 能量: 每 tick 转移量 = `min(需求量, spEnergy, dock_builder剩余 - maxEnergy × 50%)`

### 虚拟 Hive
直接以 `spMatter × 6` / `spEnergy` 速率自动填充（无来源限制）。

充能完成条件: `matter >= maxMatter && energy >= maxEnergy`

---

## 四、阶段 3 — 发射决策

火种充满后进入发射决策。

### 旧版 (DFTinderComponent.cs:149-200)

```csharp
// 充满即立刻执行目标选择，每 tick 都调用
this.GenerateSortedStarIndices(hive.sector);  // 每次都重新生成
int starValue = this.starValues[length - 1];
if (starValue == 0) {
    // 无可用目标，每 3600 tick 重试一次
    if (hive.ticks % 3600 != 0) return;
    this.GenerateSortedStarIndices(hive.sector);
} else {
    // 直接选择目标并发射
    ...
}
```

- **无发射概率门槛** — 充满即发
- **每 tick 都重新生成** `sortedStarIndices`（性能浪费）
- 无间隔限制

### 新版 (DFTinderComponent.cs:152-209)

```csharp
// [新增] 1800 tick 间隔 + 概率门槛
int num15 = hive.hiveAstroId % 1000000 * 443 + this.id * 37;
if (hive.ticks % 1800 != num15 % 1800) return;  // 每 1800 tick 检查一次

if (hive.trand.NextDouble() > Math.Pow(
        Math.Pow(0.7, Math.Sqrt(1.0 / Math.Max(0.1, growthSpeedFactor))),
        Math.Max(0, lastTotalSailingTinders))
    * (1.0 / 16.0))
    return;  // 概率判定失败

// 缓存 sortedStarIndices，只生成一次
if (this.starValues == null)
    this.GenerateSortedStarIndices(hive.sector);
```

**变化:**

| 项目 | 旧版 | 新版 |
|------|------|------|
| 检测间隔 | 每 tick | 每 1800 tick（各火种有独立相位） |
| 发射概率 | 100%（充满即发） | `0.7^(sqrt(1/g) × sailingTinders) × 1/16` |
| 衰减因子 | 无 | 在途火种越多，发射概率指数衰减（0.7 底数，比建造的 0.92 更激进） |
| 星系索引缓存 | 每次重新生成 | `starValues == null` 时才生成，缓存复用 |

**效果:**
- 基础发射概率 = 1/16 = 6.25%（每 1800 tick 才检查一次，约 30 秒/次）
- 1 个在途火种: ~4.4%（g=1 时）
- 5 个在途火种: ~1.0%
- 10 个在途火种: ~0.17%
- 大量火种同时飞行的场景被有效抑制

---

## 五、目标星系选择

### 5.1 星系权重计算 (`GenerateSortedStarIndices`)

#### 旧版 (DFTinderComponent.cs:587-590)

```csharp
double num8 = (double)(num7 + num5 * 2);       // 空轨道×1 + 可用hive×2
double num9 = 1.2 - safetyFactor * 0.4;        // 安全因子影响范围: [0.8, 1.2]
int num11 = (int)(num8 * distanceFactor * num9 * 100 + 0.5);
```

#### 新版 (DFTinderComponent.cs:596-599)

```csharp
double num8 = (double)(num7 + num5 * 4);       // 空轨道×1 + 可用hive×4 (权重翻倍)
double num9 = 1.2 - safetyFactor * 1.0;        // 安全因子影响范围: [0.2, 1.2] (影响大幅增加)
int num11 = (int)(num8 * distanceFactor * num9 * 100 + 0.5);
```

**变化:**

| 参数 | 旧版 | 新版 | 影响 |
|------|------|------|------|
| 可用 hive 权重 | `num5 × 2` | `num5 × 4` | 更偏好已有受损/空闲 hive 的星系（补给而非开拓） |
| 安全因子系数 | `safetyFactor × 0.4` | `safetyFactor × 1.0` | 安全星系权重降至 [0.2, 1.2]，高安全星系被大幅去优先 |

**效果:** 新版更激进地偏好低安全系数（=高危险度）的星系，且更倾向于加强已有薄弱 hive 而非开拓全新星系。

### 5.2 目标星系内 Hive 选择（新旧一致）

遍历目标星系的 hive 链表，选中"需要帮助"的 hive:
- 实体化 + 非空 + builder未满 + builder缺物质 + 无在途火种 → **选中**（该 hive 需要补给）
- 已死亡 + 无在途火种 → **选中**（该 hive 需要复活）
- 有空轨道时 60% 概率选中空轨道创建新 hive

### 5.3 字段重命名

| 旧版 | 新版 |
|------|------|
| `tindersInTransit` | `tindersArrivingInTransit` |

仅重命名，语义不变。

---

## 六、阶段 4 — 飞行 (`TinderSailLogic`)

飞行逻辑新旧完全一致，无代码变化。

### Stage -2: 出坞动画
- `param0` 从 0 递增到 1（每 tick +0.00166667，约 600 tick = 10秒）
- 位置固定在 dock

### Stage -1: 离开 Hive
- 加速度 0.0833/tick，向上飞行
- 距 dock > 1000 后切换到星际飞行
- 设置 astroId = 0（进入宇宙空间）

### Stage 0: 星际飞行
- 目标速度: `uSpeed × (distance / (uSpeed + 0.1) × 0.382) + 50`，上限 1200
- 加速 4.0/tick，减速 360/tick
- 行星引力规避: 检测附近天体，施加侧向力绕行
- 轨道跟随: 在无行星干扰时跟随 hive 轨道速度
- 接近目标星系 400,000 距离时广播 `ApproachingSeed` 警报
- 距目标 < 200 时进入停靠阶段

### Stage 1: 停靠接近
- 移动速度: `min(distance × 0.8 + 1.0, 50)`
- 旋转速度: 0.5 度/帧
- 距 < 0.5 且角度 < 0.5° 时完成
- 检查是否需要实体化目标 hive（同胞已实体化/星系有工厂）

### Stage 2: 停靠完成
- `param0` 从 1 递减到 0
- 完成后:
  - 创建 builder（如目标 hive 需要）
  - 转移全部物质和能量到目标 builder
  - 转移 1/5 经验差值: `(origin.exp_total - target.exp_total) / 5`
  - 递减 `tindersArrivingInTransit`
  - 重置 direction=0

---

## 七、变化总结

### 核心设计变化：从"固定速率"到"自适应平衡"

旧版的火种系统是一个固定概率的简单循环，没有任何负反馈机制。新版引入了三层指数衰减自平衡。

### 变量定义

| 变量 | 来源 | 含义 | 取值范围 |
|------|------|------|----------|
| `g` | `Mathf.Clamp(combatSettings.growthSpeedFactor, 0.1f, 10f)` | 游戏战斗设置中的黑雾成长速度倍率 | [0.1, 10] |
| `P_base` | `buildTinderTriggerProbability` | Hive 核心的基础火种触发概率（配置值） | 由 DFSCoreComponent 初始化 |
| `N` | `DFTinderComponent.lastTotalExistingTinders` | 全局已存在的火种总数（idle + 在途） | >= 0 |
| `S` | `DFTinderComponent.lastTotalSailingTinders` | 全局正在星际飞行中的火种数 | >= 0 |
| `H_alive` | `DFTinderComponent.lastAliveHiveCount` | 全局存活的 Hive 总数 | >= 0 |
| `H_star` | 遍历统计 | 拥有至少一个存活 Hive 的星系数 | [0, starCount] |
| `starCount` | `sector.galaxy.starCount` | 银河系中的星系总数 | 由地图种子决定 |

### 公式 1: 全局覆盖率平衡函数 `GetTargetTinderYieldRatio`

$$
\text{YieldRatio} = 5^{\;(0.5 \;-\; H_{star} \;/\; starCount) \;\times\; 2} \;\times\; \left(\frac{5}{H_{alive} + 1}\right)^{0.8}
$$

- 第一项 `5^((0.5 - 覆盖率) × 2)`: Hive 覆盖率越高，指数越小，触发概率越低
  - 覆盖率 0% → `5^1.0 = 5.0`
  - 覆盖率 25% → `5^0.5 ≈ 2.24`
  - 覆盖率 50% → `5^0.0 = 1.0`（基准线）
  - 覆盖率 75% → `5^-0.5 ≈ 0.45`
- 第二项 `(5/(H_alive+1))^0.8`: 存活 Hive 越多，值越小
  - 1 个 Hive → `(5/2)^0.8 ≈ 2.16`
  - 10 个 Hive → `(5/11)^0.8 ≈ 0.53`
  - 50 个 Hive → `(5/51)^0.8 ≈ 0.13`

### 公式 2: 建造触发概率（子阶段 A）

每 `buildTinderTriggerKeyTick` 个 tick 检测一次（各 Hive 有独立相位偏移），概率为：

$$
P_{trigger} = \text{YieldRatio} \;\times\; g \;\times\; P_{base} \;\times\; 0.97^{N}
$$

- `YieldRatio`: 全局覆盖率平衡因子（见公式 1）
- `g`: 游戏成长速度倍率
- `P_base`: 基础触发概率
- `0.97^N`: 已有火种数指数衰减（每多 1 个火种，概率 ×0.97）
  - N=10 → ×0.74
  - N=20 → ×0.54
  - N=50 → ×0.22

旧版: 固定 `P_base`，无任何动态因子。

### 公式 3: 建造推进概率（子阶段 B）

建造进行中，每 tick 判定是否推进一步，概率为：

$$
P_{build} = 0.75 \;\times\; 0.92^{\;\sqrt{1/g} \;\times\; N} \;\times\; g
$$

- `0.75`: 基础推进概率上限
- `0.92^(sqrt(1/g) × N)`: 指数衰减项
  - 底数 0.92，指数为 `sqrt(1/g) × N`
  - g=1, N=10 → `0.92^10 ≈ 0.43` → P ≈ 0.75 × 0.43 × 1 ≈ 0.32
  - g=1, N=30 → `0.92^30 ≈ 0.08` → P ≈ 0.75 × 0.08 × 1 ≈ 0.06
- `g`: 成长速度倍率，g 越大衰减越慢（`sqrt(1/g)` 变小），同时线性提升概率
- 期望建造时长 = `buildTinderSpMax / P_build` tick

旧版: 每 tick 确定性推进（P=100%），建造时长固定为 `buildTinderSpMax` tick。

### 公式 4: 发射概率（阶段 3）

每 1800 tick 检测一次（各火种有独立相位偏移），概率为：

$$
P_{launch} = \frac{1}{16} \;\times\; 0.7^{\;\sqrt{1 / \max(0.1,\; g)} \;\times\; \max(0,\; S)}
$$

- `1/16 = 0.0625`: 基础发射概率（6.25%）
- `0.7^(sqrt(1/g) × S)`: 在途火种数指数衰减
  - 底数 0.7（比建造的 0.92 更激进）
  - g=1, S=1 → `0.7^1 = 0.70` → P ≈ 4.4%
  - g=1, S=5 → `0.7^5 ≈ 0.17` → P ≈ 1.0%
  - g=1, S=10 → `0.7^10 ≈ 0.028` → P ≈ 0.17%

旧版: 充满即发（P=100%），每 tick 都检测，无任何限制。

### 三层自平衡机制总览

```
┌────────────────────────────────────────────────────────────────────────────┐
│                          新版自平衡机制                                     │
│                                                                            │
│  第 1 层 — 建造触发                                                        │
│     P_trigger = YieldRatio × g × P_base × 0.97^N                          │
│     抑制因素: 全局 Hive 覆盖率 ↑ / 已有火种数 N ↑ → 触发概率 ↓            │
│                                                                            │
│  第 2 层 — 建造推进                                                        │
│     P_build = 0.75 × 0.92^(√(1/g) × N) × g                               │
│     抑制因素: 已有火种数 N ↑ → 单次推进概率 ↓ → 建造耗时 ↑                │
│                                                                            │
│  第 3 层 — 发射决策                                                        │
│     P_launch = (1/16) × 0.7^(√(1/g) × S)                                  │
│     抑制因素: 在途火种数 S ↑ → 发射概率 ↓（衰减最激进）                   │
│                                                                            │
│  综合效果: 火种数量存在自然上限，三层负反馈叠加防止无限增长                 │
└────────────────────────────────────────────────────────────────────────────┘
```

### 逐项变化清单

| # | 模块 | 旧版 | 新版 | 影响 |
|---|------|------|------|------|
| 1 | 建造触发 | 固定概率 `probability` | `YieldRatio × g × probability × 0.97^N` | 全局自平衡 |
| 2 | 建造推进 | 确定性（每 tick 必推进） | 概率性 `0.75 × 0.92^(...) × g` | 建造速度受火种总量抑制 |
| 3 | 虚拟 hive 建造 | 确定性 | 与实体 hive 同公式 | 虚拟 hive 不再无限制建造 |
| 4 | 触发相位 | 所有 hive 同步 | 各 hive 独立相位偏移 | 避免同步爆发 |
| 5 | 发射间隔 | 每 tick | 每 1800 tick | 降低发射频率 |
| 6 | 发射概率 | 100%（充满即发） | `0.7^(...) × 1/16` 指数衰减 | 在途火种抑制新发射 |
| 7 | 星系索引缓存 | 每次重新生成 | 生成一次后缓存 | 性能优化 |
| 8 | 可用 hive 权重 | `×2` | `×4` | 更偏好加强已有 hive |
| 9 | 安全因子系数 | `×0.4` | `×1.0` | 高安全星系被大幅去优先 |
| 10 | 字段重命名 | `tindersInTransit` | `tindersArrivingInTransit` | 仅重命名 |
| 11 | 新增函数 | — | `GetTargetTinderYieldRatio()` | 全局覆盖率平衡 |
| 12 | 新增静态字段 | — | `lastTotalExistingTinders`, `lastTotalSailingTinders`, `lastAliveHiveCount` | 全局状态追踪 |
| 13 | 随机源 | `RandomTable.Integer` | 建造/发射改为 `hive.trand.NextDouble()` | 更平滑的概率分布 |
| 14 | growthSpeedFactor | 不存在 | `Clamp(combatSettings.growthSpeedFactor, 0.1, 10)` | 支持游戏难度设置 |

### 未变化部分
- 建造前置条件（物质75%/能量50%）
- 充能逻辑（资源转移）
- 飞行物理（速度/引力/绕行）
- 停靠和资源转移
- 目标 hive 选择条件（选哪个 hive）
- 60% 空轨道概率
- 警报广播距离 400,000
