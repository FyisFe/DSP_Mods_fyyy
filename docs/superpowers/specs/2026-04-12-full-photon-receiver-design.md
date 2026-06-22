# FullPhotonReceiver Design Spec

**Date:** 2026-04-12
**Goal:** 让光子模式的射线接收站（gamma ray receiver）始终满功率生产临界光子，无视太阳朝向、戴森球/云覆盖、戴森球供能不足等限制。

## Scope

- **Only** affects photon generation mode (`productId > 0`)
- Does **not** affect power generation mode (`productId == 0`)
- No configuration — mod loads and takes effect immediately

## Game Mechanics Summary

### Vanilla Flow

1. **`PowerSystem.RequestDysonSpherePower()`**
   - Calls `PowerGeneratorComponent.EnergyCap_Gamma_Req(sx, sy, sz, increase, eta)` for each gamma receiver
   - Computes `currentStrength` from sun direction dot product + Dyson ray increase + ion lens enhance
   - Computes `capacityCurrentTick` based on `currentStrength`, `warmup`, lens bonus, and mode multiplier
   - Returns energy required from the Dyson Sphere (`capacityCurrentTick / eta`)
   - Return value accumulates into `dysonSphere.energyReqCurrentTick`

2. **`PowerSystem.GameTick()`** — capacity phase
   - `response = dysonSphere.energyRespCoef` (supply/demand ratio, 0~1)
   - Calls `EnergyCap_Gamma(response)` → multiplies `capacityCurrentTick *= response`
   - For photon mode, returns 0 to the grid (receiver doesn't supply energy)

3. **`PowerSystem.GameTick()`** — production phase
   - Calls `GameTick_Gamma(...)` → `productCount += capacityCurrentTick / productHeat`

### Bottleneck Points

| Limitation | Where | Field |
|---|---|---|
| Sun direction / night side | `EnergyCap_Gamma_Req` | `currentStrength` (dot product) |
| No Dyson Sphere/Swarm | `EnergyCap_Gamma_Req` | `increase` param → `currentStrength` |
| Dyson Sphere underpowered | `EnergyCap_Gamma` | `response` scales down `capacityCurrentTick` |

## Patch Design

### Patch 1: `EnergyCap_Gamma_Req` — Harmony Prefix

**Target:** `PowerGeneratorComponent.EnergyCap_Gamma_Req(float, float, float, float, float)`

**Condition:** `__instance.productId > 0` (photon mode only)

**Behavior when active (return false, skip original):**

```
currentStrength = 1.0f
accBonus = Cargo.accTableMilli[catalystIncLevel]
capacityCurrentTick = (long)(1.0
    * (1.0 + warmup * 1.5)
    * (catalystPoint > 0 ? 2.0 * (1.0 + accBonus) : 1.0)
    * 8.0
    * genEnergyPerTick)
warmupSpeed = (1.0 - 0.75) * 4.0 * 1.3888889043300878e-05  // constant positive
__result = 0  // request zero energy from Dyson Sphere
```

**Behavior when inactive (`productId == 0`):** `return true` — original method runs unchanged.

**Note:** `PowerGeneratorComponent` is a struct. Must use `ref PowerGeneratorComponent __instance` in Harmony patch signature.

### Patch 2: `EnergyCap_Gamma` — Harmony Prefix

**Target:** `PowerGeneratorComponent.EnergyCap_Gamma(float)`

**Condition:** `__instance.productId > 0` (photon mode only)

**Behavior when active (return false, skip original):**

```
// Do NOT multiply capacityCurrentTick by response
// warmupSpeed adjustment is also skipped (warmupSpeed is always positive from Patch 1)
__result = 0  // photon mode returns 0 to grid (same as vanilla)
```

**Behavior when inactive (`productId == 0`):** `return true` — original method runs unchanged.

### Combined Effect

| Step | Vanilla | Modded (photon mode) |
|---|---|---|
| `currentStrength` | 0~1 (sun/sphere dependent) | Always 1.0 |
| Energy requested from sphere | `capacityCurrentTick / eta` | 0 (no request) |
| `response` scaling | `capacityCurrentTick *= response` | Skipped |
| `capacityCurrentTick` into `GameTick_Gamma` | Scaled by strength & response | Full power |
| Photon production | Variable | Always maximum rate |
| Impact on other receivers | Competes for sphere energy | No impact (doesn't request energy) |

## Project Structure

```
FullPhotonReceiver/
├── FullPhotonReceiver.csproj    # BepInEx 5 + Harmony, net472
└── FullPhotonReceiverPlugin.cs  # Plugin entry + inline patches
```

Follows FastTinderLaunch conventions:
- GUID: `org.fyyy.fullphotonreceiver`
- NuGet-based BepInEx references
- Assembly-CSharp via relative path `..\..\DSP_Mods\AssemblyFromGame\Assembly-CSharp.dll`
- No UXAssist dependency, no configuration

## Warmup Behavior

With `currentStrength` forced to 1.0:
- `warmupSpeed = (1.0 - 0.75) * 4 * 1.389e-5 ≈ 1.389e-5` (constant positive)
- Warmup increases every tick until reaching 1.0 (~20 min from cold start)
- Once at 1.0, output is true maximum: `1.0 * (1 + 1.5) * lensBonus * 8 * genEnergyPerTick`

## Edge Cases

- **No Dyson Sphere at all:** Works. Receiver doesn't need the sphere.
- **Receiver on night side:** Works. `currentStrength` is forced to 1.0.
- **No gravitational lens:** Works. Lens bonus is calculated from actual `catalystPoint` (unchanged).
- **productCount >= 20:** Vanilla cap still applies (output belt must drain products).
- **Power generation mode:** Completely unaffected — prefix returns true, original runs.
