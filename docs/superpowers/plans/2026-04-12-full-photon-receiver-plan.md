# FullPhotonReceiver Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a BepInEx mod that makes photon-mode gamma ray receivers always produce critical photons at full power, ignoring sun direction, Dyson Sphere coverage, and Dyson Sphere energy supply.

**Architecture:** Two Harmony Prefix patches on `PowerGeneratorComponent` struct methods — one forces `currentStrength = 1.0` and zeroes the energy request, the other prevents `response` from scaling down `capacityCurrentTick`. Both only activate for photon mode (`productId > 0`).

**Tech Stack:** C# / .NET Framework 4.7.2, BepInEx 5, Harmony 2, Unity 2022.3

**Spec:** `docs/superpowers/specs/2026-04-12-full-photon-receiver-design.md`

---

## File Structure

| File | Purpose |
|---|---|
| `FullPhotonReceiver/FullPhotonReceiver.csproj` | Project file — BepInEx NuGet refs, Assembly-CSharp reference |
| `FullPhotonReceiver/FullPhotonReceiverPlugin.cs` | Plugin entry point + two Harmony Prefix patches |
| `DSP_Mods_fyyy.sln` | Add new project entry |

---

### Task 1: Create project file

**Files:**
- Create: `FullPhotonReceiver/FullPhotonReceiver.csproj`

- [ ] **Step 1: Create the csproj**

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <AssemblyName>FullPhotonReceiver</AssemblyName>
    <BepInExPluginGuid>org.fyyy.fullphotonreceiver</BepInExPluginGuid>
    <Description>Photon-mode ray receivers always produce at full power / 光子模式射线接收站始终满功率生产</Description>
    <Version>1.0.0</Version>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <LangVersion>latest</LangVersion>
    <RestoreAdditionalProjectSources>https://nuget.bepinex.dev/v3/index.json</RestoreAdditionalProjectSources>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="BepInEx.Core" Version="5.*" />
    <PackageReference Include="BepInEx.PluginInfoProps" Version="1.*" />
    <PackageReference Include="UnityEngine.Modules" Version="2022.3.62" IncludeAssets="compile" />
  </ItemGroup>

  <ItemGroup>
    <Reference Include="Assembly-CSharp">
      <HintPath>..\..\DSP_Mods\AssemblyFromGame\Assembly-CSharp.dll</HintPath>
    </Reference>
  </ItemGroup>

  <ItemGroup Condition="'$(TargetFramework.TrimEnd(`0123456789`))' == 'net'">
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Commit**

```bash
git add FullPhotonReceiver/FullPhotonReceiver.csproj
git commit -m "feat(FullPhotonReceiver): add project file"
```

---

### Task 2: Write plugin with Harmony patches

**Files:**
- Create: `FullPhotonReceiver/FullPhotonReceiverPlugin.cs`

- [ ] **Step 1: Write the plugin file**

```csharp
using BepInEx;
using HarmonyLib;

namespace FullPhotonReceiver;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class FullPhotonReceiverPlugin : BaseUnityPlugin
{
    public new static readonly BepInEx.Logging.ManualLogSource Logger =
        BepInEx.Logging.Logger.CreateLogSource(PluginInfo.PLUGIN_NAME);

    private Harmony _harmony;

    private void Awake()
    {
        _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
        _harmony.PatchAll(typeof(GammaPatches));
        Logger.LogInfo("FullPhotonReceiver loaded.");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }

    static class GammaPatches
    {
        /// <summary>
        /// Patch 1: EnergyCap_Gamma_Req — force currentStrength = 1.0 for photon mode,
        /// request zero energy from the Dyson Sphere.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(PowerGeneratorComponent), nameof(PowerGeneratorComponent.EnergyCap_Gamma_Req))]
        static bool EnergyCap_Gamma_Req_Prefix(
            ref PowerGeneratorComponent __instance,
            ref long __result)
        {
            if (__instance.productId <= 0)
                return true;

            __instance.currentStrength = 1.0f;

            float accBonus = (float)Cargo.accTableMilli[__instance.catalystIncLevel];
            __instance.capacityCurrentTick = (long)(
                1.0
                * (1.0 + (double)__instance.warmup * 1.5)
                * (__instance.catalystPoint > 0 ? 2.0 * (1.0 + (double)accBonus) : 1.0)
                * 8.0
                * (double)__instance.genEnergyPerTick);

            // Constant positive warmupSpeed — warmup will steadily rise to 1.0
            __instance.warmupSpeed = (float)((1.0 - 0.75) * 4.0 * 1.3888889043300878e-05);

            // Request zero energy from the Dyson Sphere
            __result = 0L;
            return false;
        }

        /// <summary>
        /// Patch 2: EnergyCap_Gamma — skip response scaling for photon mode,
        /// keep capacityCurrentTick at full power.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(PowerGeneratorComponent), nameof(PowerGeneratorComponent.EnergyCap_Gamma))]
        static bool EnergyCap_Gamma_Prefix(
            ref PowerGeneratorComponent __instance,
            ref long __result)
        {
            if (__instance.productId <= 0)
                return true;

            // Do not scale capacityCurrentTick by response.
            // Photon mode returns 0 energy to the grid (same as vanilla).
            __result = 0L;
            return false;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add FullPhotonReceiver/FullPhotonReceiverPlugin.cs
git commit -m "feat(FullPhotonReceiver): implement full-power photon receiver patches"
```

---

### Task 3: Add project to solution

**Files:**
- Modify: `DSP_Mods_fyyy.sln`

- [ ] **Step 1: Add the FullPhotonReceiver project to the solution**

Use a new GUID for the project. Add the project entry after the existing projects, and add all 6 build configurations (Debug/Release x Any CPU/x64/x86).

```bash
dotnet sln DSP_Mods_fyyy.sln add FullPhotonReceiver/FullPhotonReceiver.csproj
```

- [ ] **Step 2: Commit**

```bash
git add DSP_Mods_fyyy.sln
git commit -m "chore: add FullPhotonReceiver to solution"
```

---

### Task 4: Build and verify

- [ ] **Step 1: Restore NuGet packages and build**

```bash
dotnet restore FullPhotonReceiver/FullPhotonReceiver.csproj
dotnet build FullPhotonReceiver/FullPhotonReceiver.csproj -c Release
```

Expected: Build succeeds with 0 errors. Output DLL at `FullPhotonReceiver/bin/Release/net472/FullPhotonReceiver.dll`.

- [ ] **Step 2: Verify the DLL contains both patches**

The build output should show no warnings about missing references. The DLL should contain:
- `FullPhotonReceiverPlugin` class
- `GammaPatches` nested class with two prefix methods

- [ ] **Step 3: In-game smoke test**

1. Copy `FullPhotonReceiver.dll` to `BepInEx/plugins/`
2. Launch DSP, load a save with gamma ray receivers in photon mode
3. Verify: receivers on the night side (no sun) still show full `currentStrength` and produce critical photons at max rate
4. Verify: receivers in power generation mode (`productId == 0`) behave normally — output varies with sun/sphere
5. Check BepInEx log for "FullPhotonReceiver loaded." message and no errors
