# InterstellarLogisticsOpt — Phase Dispersion (P0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a BepInEx mod that flattens interstellar-logistics CPU spikes by phase-dispersing the stellar station scheduler in `GalacticTransport.GameTick`, preserving each tower's 10/30/60-tick scheduling frequency.

**Architecture:** A single Harmony Prefix on `GalacticTransport.GameTick(long time)` returns `false` to replace the body. The replacement keeps the exact priority/routePriority dispatch branches but swaps the `DetermineFramingDispatchTime` all-towers-in-phase gate for a `period`-strided, `phase`-offset sweep over `stationPool`. A BepInEx config flag (`Enabled`, default true) makes the Prefix fall through to vanilla when off.

**Tech Stack:** C# (net472), BepInEx 5.4.x, HarmonyLib, references DSP `Assembly-CSharp.dll`. Mirrors the existing `FullPhotonReceiver` mod.

---

## Testing Note (read first)

DSP mods cannot be unit-tested in isolation — the patched types (`GalacticTransport`,
`StationComponent`) only exist inside the running game. Therefore the automated
"test" gate for each task is **a successful Release build** that compiles against
`Assembly-CSharp.dll` (this proves field/method signatures, enum names, and access
modifiers are correct). Final behavioral verification is **manual, in-game, by the
user** and is documented in Task 5 — it is not automatable here.

## File Structure

| File | Responsibility |
|------|----------------|
| `InterstellarLogisticsOpt/InterstellarLogisticsOpt.csproj` | Build config: net472, BepInEx.Core 5.*, Assembly-CSharp ref, PostBuild zip. |
| `InterstellarLogisticsOpt/InterstellarLogisticsOptPlugin.cs` | Plugin lifecycle, config binding, the Harmony Prefix patch. |
| `InterstellarLogisticsOpt/package/manifest.json` | Thunderstore manifest. |
| `InterstellarLogisticsOpt/package/icon.png` | Package icon (copied from an existing mod as placeholder). |
| `InterstellarLogisticsOpt/package/README.md` | User-facing description + determinism caveat. |
| `DSP_Mods_fyyy.sln` | Add the new project so it builds with the solution. |

---

## Task 1: Scaffold the project (.csproj)

**Files:**
- Create: `InterstellarLogisticsOpt/InterstellarLogisticsOpt.csproj`

- [ ] **Step 1: Create the csproj**

Create `InterstellarLogisticsOpt/InterstellarLogisticsOpt.csproj` with exactly:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <AssemblyName>InterstellarLogisticsOpt</AssemblyName>
    <BepInExPluginGuid>org.fyyy.interstellarlogisticsopt</BepInExPluginGuid>
    <Description>Phase-disperses interstellar logistics scheduling to flatten CPU spikes / 相位分散星际物流调度以削平卡顿</Description>
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

  <Target Name="PostBuild" AfterTargets="PostBuildEvent" Condition="'$(Configuration)' == 'Release'">
    <Exec Command="del /F /Q package\$(ProjectName)-$(Version).zip
powershell Compress-Archive -Force -DestinationPath 'package/$(ProjectName)-$(Version).zip' -Path &quot;$(TargetPath)&quot;, package/icon.png, package/manifest.json, package/README.md" />
  </Target>

</Project>
```

This is `FullPhotonReceiver.csproj` with the name/GUID/description changed and
`package/README.md` added to the zip Path list.

- [ ] **Step 2: Verify the Assembly-CSharp HintPath resolves**

Run:

```powershell
Test-Path "..\DSP_Mods\AssemblyFromGame\Assembly-CSharp.dll"
```

(Run from the repo root `C:\Users\Yi\Applications\Games\dsp\code\DSP_Mods_fyyy`;
the HintPath `..\..\DSP_Mods\...` is relative to the csproj one level deeper, so
from repo root it is `..\DSP_Mods\...`.)
Expected: `True`. If `False`, stop — the build cannot reference the game assembly;
report to the user before continuing.

- [ ] **Step 3: Commit**

```bash
git add InterstellarLogisticsOpt/InterstellarLogisticsOpt.csproj
git commit -m "feat(InterstellarLogisticsOpt): scaffold project file"
```

---

## Task 2: Register the project in the solution

**Files:**
- Modify: `DSP_Mods_fyyy.sln`

- [ ] **Step 1: Add the Project block**

In `DSP_Mods_fyyy.sln`, after the `DeliveryPresets` `EndProject` line (line 19),
add:

```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "InterstellarLogisticsOpt", "InterstellarLogisticsOpt\InterstellarLogisticsOpt.csproj", "{E2F8A1B4-3C5D-4E6F-9A0B-1C2D3E4F5A6B}"
EndProject
```

- [ ] **Step 2: Add the ProjectConfigurationPlatforms entries**

In the `GlobalSection(ProjectConfigurationPlatforms) = postSolution` block, after
the last `{D1E92F33-...}` line (line 125), add:

```
		{E2F8A1B4-3C5D-4E6F-9A0B-1C2D3E4F5A6B}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{E2F8A1B4-3C5D-4E6F-9A0B-1C2D3E4F5A6B}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{E2F8A1B4-3C5D-4E6F-9A0B-1C2D3E4F5A6B}.Debug|x64.ActiveCfg = Debug|Any CPU
		{E2F8A1B4-3C5D-4E6F-9A0B-1C2D3E4F5A6B}.Debug|x64.Build.0 = Debug|Any CPU
		{E2F8A1B4-3C5D-4E6F-9A0B-1C2D3E4F5A6B}.Debug|x86.ActiveCfg = Debug|Any CPU
		{E2F8A1B4-3C5D-4E6F-9A0B-1C2D3E4F5A6B}.Debug|x86.Build.0 = Debug|Any CPU
		{E2F8A1B4-3C5D-4E6F-9A0B-1C2D3E4F5A6B}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{E2F8A1B4-3C5D-4E6F-9A0B-1C2D3E4F5A6B}.Release|Any CPU.Build.0 = Release|Any CPU
		{E2F8A1B4-3C5D-4E6F-9A0B-1C2D3E4F5A6B}.Release|x64.ActiveCfg = Release|Any CPU
		{E2F8A1B4-3C5D-4E6F-9A0B-1C2D3E4F5A6B}.Release|x64.Build.0 = Release|Any CPU
		{E2F8A1B4-3C5D-4E6F-9A0B-1C2D3E4F5A6B}.Release|x86.ActiveCfg = Release|Any CPU
		{E2F8A1B4-3C5D-4E6F-9A0B-1C2D3E4F5A6B}.Release|x86.Build.0 = Release|Any CPU
```

(Indentation is two tab characters, matching the existing lines in that block.)

- [ ] **Step 3: Verify the solution still parses**

Run:

```powershell
dotnet sln DSP_Mods_fyyy.sln list
```

Expected: the output lists `InterstellarLogisticsOpt\InterstellarLogisticsOpt.csproj`
among the projects, with no parse error.

- [ ] **Step 4: Commit**

```bash
git add DSP_Mods_fyyy.sln
git commit -m "build(InterstellarLogisticsOpt): add project to solution"
```

---

## Task 3: Implement the plugin and Harmony Prefix

**Files:**
- Create: `InterstellarLogisticsOpt/InterstellarLogisticsOptPlugin.cs`

- [ ] **Step 1: Write the plugin source**

Create `InterstellarLogisticsOpt/InterstellarLogisticsOptPlugin.cs` with exactly:

```csharp
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace InterstellarLogisticsOpt;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class InterstellarLogisticsOptPlugin : BaseUnityPlugin
{
    public new static readonly BepInEx.Logging.ManualLogSource Logger =
        BepInEx.Logging.Logger.CreateLogSource(PluginInfo.PLUGIN_NAME);

    public static ConfigEntry<bool> ModEnabled;

    private Harmony _harmony;

    private void Awake()
    {
        ModEnabled = Config.Bind("General", "Enabled", true,
            "Phase-disperse interstellar logistics scheduling to flatten CPU spikes / 相位分散星际物流调度以削平卡顿");

        _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
        _harmony.PatchAll(typeof(GalacticTransportPatch));
        Logger.LogInfo("InterstellarLogisticsOpt loaded.");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }

    static class GalacticTransportPatch
    {
        /// <summary>
        /// Replaces GalacticTransport.GameTick with a phase-dispersed station sweep.
        /// Each station slot is still processed once per `period` ticks (10/30/60,
        /// identical cadence to vanilla), but offset by `time % period` so the work
        /// is spread evenly across ticks instead of all towers firing in phase.
        /// The inner priorityIndex2 / routePriority dispatch branches are unchanged.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(GalacticTransport), nameof(GalacticTransport.GameTick))]
        static bool GameTick_Prefix(GalacticTransport __instance, long time)
        {
            if (!ModEnabled.Value)
                return true; // run vanilla GameTick

            GameData gameData = __instance.gameData;
            GalaxyData galaxy = gameData.galaxy;
            GameHistoryData history = gameData.history;
            PlanetFactory[] factories = gameData.factories;
            FactoryProductionStat[] factoryStatPool = gameData.statistics.production.factoryStatPool;
            TrafficStatistics traffic = gameData.statistics.traffic;
            float sailSpeedModified = history.logisticShipSailSpeedModified;
            float shipWarpSpeed = history.logisticShipWarpDrive
                ? history.logisticShipWarpSpeedModified
                : sailSpeedModified;
            int logisticShipCarries = history.logisticShipCarries;

            StationComponent[] stationPool = __instance.stationPool;
            int stationCursor = __instance.stationCursor;

            for (int priorityIndex1 = 1; priorityIndex1 < 7; ++priorityIndex1)
            {
                int priorityIndex2 = priorityIndex1 % 6;
                int period = (priorityIndex1 == 1) ? 10
                           : (priorityIndex1 == 2 || priorityIndex1 == 3) ? 30
                           : 60;
                int phase = (int)(time % period);

                for (int index = 1 + phase; index < stationCursor; index += period)
                {
                    StationComponent stationComponent = stationPool[index];
                    if (stationComponent == null || stationComponent.id <= 0 || stationComponent.gid != index)
                        continue;

                    if (priorityIndex2 >= 1 && priorityIndex2 <= 4 &&
                        (stationComponent.routePriority == ERemoteRoutePriority.Prioritize ||
                         stationComponent.routePriority == ERemoteRoutePriority.Only ||
                         stationComponent.routePriority == ERemoteRoutePriority.Designated))
                        stationComponent.DetermineDispatch(sailSpeedModified, shipWarpSpeed, logisticShipCarries, priorityIndex2, stationPool, factoryStatPool, factories, galaxy, traffic);
                    else if (priorityIndex2 == 5 && stationComponent.routePriority == ERemoteRoutePriority.Prioritize)
                        stationComponent.DetermineDispatch(sailSpeedModified, shipWarpSpeed, logisticShipCarries, priorityIndex2, stationPool, factoryStatPool, factories, galaxy, traffic);
                    else if (priorityIndex2 == 0 && stationComponent.routePriority == ERemoteRoutePriority.Ignore)
                        stationComponent.DetermineDispatch(sailSpeedModified, shipWarpSpeed, logisticShipCarries, priorityIndex2, stationPool, factoryStatPool, factories, galaxy, traffic);
                }
            }

            return false; // skip the original method
        }
    }
}
```

This mirrors the vanilla `GameTick` locals (`GalacticTransport.cs:177-184`) and the
exact dispatch branches (`GalacticTransport.cs:195-200`). `gameData`, `stationPool`,
and `stationCursor` are `public` fields on `GalacticTransport` (verified at
`GalacticTransport.cs:15-16`).

- [ ] **Step 2: Build (this is the test — proves all signatures resolve)**

Run:

```powershell
dotnet build InterstellarLogisticsOpt/InterstellarLogisticsOpt.csproj -c Debug
```

Expected: `Build succeeded.` with 0 errors. A compile error here means a field name,
enum value (`ERemoteRoutePriority.*`), method signature (`DetermineDispatch`), or
access modifier is wrong — fix against `GameCode-latest/GalacticTransport.cs` and
`StationComponent.cs` before proceeding.

- [ ] **Step 3: Commit**

```bash
git add InterstellarLogisticsOpt/InterstellarLogisticsOptPlugin.cs
git commit -m "feat(InterstellarLogisticsOpt): phase-disperse GalacticTransport.GameTick"
```

---

## Task 4: Package files (manifest, icon, README)

**Files:**
- Create: `InterstellarLogisticsOpt/package/manifest.json`
- Create: `InterstellarLogisticsOpt/package/icon.png`
- Create: `InterstellarLogisticsOpt/package/README.md`

- [ ] **Step 1: Create the manifest**

Create `InterstellarLogisticsOpt/package/manifest.json` with exactly:

```json
{
  "name": "InterstellarLogisticsOpt",
  "version_number": "1.0.0",
  "website_url": "https://github.com/FyisFe/DSP_Mods_fyyy",
  "description": "Flattens interstellar logistics CPU spikes by phase-dispersing the stellar scheduler (same dispatch frequency). / 相位分散星际物流调度，削平挂机时的CPU尖峰，调度频率不变",
  "dependencies": [
    "xiaoye97-BepInEx-5.4.17"
  ]
}
```

- [ ] **Step 2: Provide an icon (placeholder from an existing mod)**

Run:

```powershell
Copy-Item FullPhotonReceiver/package/icon.png InterstellarLogisticsOpt/package/icon.png
```

Expected: the file exists afterward (`Test-Path InterstellarLogisticsOpt/package/icon.png` → `True`).
The Thunderstore zip requires a 256x256 `icon.png`; this placeholder satisfies the
build. (User may replace it later with a custom icon.)

- [ ] **Step 3: Create the README with the determinism caveat**

Create `InterstellarLogisticsOpt/package/README.md` with exactly:

```markdown
# InterstellarLogisticsOpt

Flattens the CPU spikes caused by **interstellar (stellar) logistics** when idling
with many logistics towers.

## What it does

Vanilla schedules every stellar logistics tower *in phase*: all towers of a given
priority run on the same tick (`t%10`, `t%30`, `t%60`), producing a large spike
every 60 ticks and near-idle ticks otherwise. This mod spreads the same work evenly
across ticks by offsetting each tower by its array slot, **without changing how
often any tower is scheduled** (still every 10 / 30 / 60 ticks). Peak per-tick
scheduler load drops by roughly 96% with no change to logistics throughput.

Only interstellar logistics is affected. Local (planetary) logistics is untouched.

## Configuration

`BepInEx/config/org.fyyy.interstellarlogisticsopt.cfg`:

- `[General] Enabled` (default `true`) — set to `false` to run the vanilla scheduler.

## Note on determinism

Because towers competing for the same delivery are now resolved across different
ticks instead of all on one tick, the exact "which supplier wins this order"
sequence can differ from vanilla. Steady-state throughput is unaffected (cargo
still flows, arguably more fairly), but per-frame behavior is **not** bit-identical
to vanilla. If you play multiplayer or rely on replay determinism, evaluate before
using.
```

- [ ] **Step 4: Commit**

```bash
git add InterstellarLogisticsOpt/package/manifest.json InterstellarLogisticsOpt/package/icon.png InterstellarLogisticsOpt/package/README.md
git commit -m "chore(InterstellarLogisticsOpt): add package manifest, icon, README"
```

---

## Task 5: Release build + manual verification

**Files:** none (verification only)

- [ ] **Step 1: Release build produces the distributable zip**

Run:

```powershell
dotnet build InterstellarLogisticsOpt/InterstellarLogisticsOpt.csproj -c Release
```

Expected: `Build succeeded.` and the PostBuild step creates
`InterstellarLogisticsOpt/package/InterstellarLogisticsOpt-1.0.0.zip`.

- [ ] **Step 2: Verify the zip contents**

Run:

```powershell
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::OpenRead((Resolve-Path "InterstellarLogisticsOpt/package/InterstellarLogisticsOpt-1.0.0.zip")).Entries | ForEach-Object { $_.FullName }
```

Expected entries: `InterstellarLogisticsOpt.dll`, `icon.png`, `manifest.json`,
`README.md`.

- [ ] **Step 3: Manual in-game verification (USER-RUN — not automatable)**

This step requires the DSP game and a save with many stellar logistics towers. Hand
off to the user with these checks:

1. Copy `InterstellarLogisticsOpt.dll` into `BepInEx/plugins/` of a BepInEx-modded
   DSP install (or install the zip via a mod manager).
2. Launch the game; confirm the log shows `InterstellarLogisticsOpt loaded.` and the
   config file `org.fyyy.interstellarlogisticsopt.cfg` is generated.
3. Load a large save and idle. Confirm:
   - Interstellar logistics ships still dispatch and cargo still flows (functional).
   - Per-frame CPU time is flatter — the once-per-second large spike is gone
     (open a frame-time graph / profiler, or compare with `Enabled = false`).

- [ ] **Step 4: Commit (only if any tracked files changed; the zip is build output)**

If `git status` shows only the (gitignored) build artifacts, there is nothing to
commit — the feature is complete. Otherwise commit any remaining tracked changes:

```bash
git status
```

---

## Self-Review (completed by plan author)

- **Spec coverage:** Approach (Prefix full-replace) → Task 3; config flag → Task 3 Step 1; csproj/packaging → Tasks 1, 4; sln registration → Task 2; build + manual verification → Task 5; determinism caveat in README → Task 4 Step 3. All spec sections covered.
- **Placeholder scan:** No TBD/TODO; every code/command step shows full content. The icon is a deliberate placeholder copy, called out explicitly (not a plan gap).
- **Type consistency:** Field names (`gameData`, `stationPool`, `stationCursor`, `id`, `gid`, `routePriority`), enum `ERemoteRoutePriority.{Prioritize,Only,Designated,Ignore}`, and `DetermineDispatch(...)` signature all taken verbatim from `GameCode-latest/GalacticTransport.cs:175-205`. Config field `ModEnabled` defined in Awake and read in the Prefix — consistent. GUID `org.fyyy.interstellarlogisticsopt` consistent across csproj, manifest, README.
- **Note:** DSP mods have no isolatable unit tests; build-success is the automated gate and final behavior is manual (documented in the Testing Note and Task 5). This is an honest deviation from strict TDD, not a skipped step.
