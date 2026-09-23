# FullPhotonReceiver Design Spec

## Contract

Ray receivers in both photon mode (`productId > 0`) and power mode (`productId == 0`) run at full capacity for their current warmup, lens and proliferator level, independent of sun direction and Dyson Sphere supply. Neither mode requests sphere energy.

- Photon mode produces photons and supplies no grid energy.
- Power mode provides full generating capacity; actual generation follows native grid demand.
- Warmup, lens consumption, photon buffers and belt I/O remain native. Warmup rises at `1f / 72000f` per tick, reaching its maximum after roughly 20 minutes from cold.
- Other generator types are unaffected. There are no configuration options.

## Runtime flow

Both `PowerSystem.RequestDysonSpherePower()` and `GameLogic._power_gen_gamma_parallel()` call `PowerGeneratorComponent.EnergyCap_Gamma_Req(...)` for gamma receivers, even without a Dyson Sphere. Its Harmony prefix skips the original and sets:

```csharp
currentStrength = 1.0f;
capacityCurrentTick = MaxOutputCurrent_Gamma();
warmupSpeed = 1f / 72000f;
__result = 0L;
```

`MaxOutputCurrent_Gamma()` owns lens availability, per-item catalyst ability, proliferator bonuses and the mode multiplier. The mod does not read `catalystIncLevel` directly or assume a fixed lens multiplier.

During the capacity phase, `PowerSystem.GameTick()` calls `EnergyCap_Gamma(response)`. Its prefix skips supply scaling, preserves capacity and warmup speed, and returns:

```csharp
__result = productId == 0 ? capacityCurrentTick : 0L;
```

The native power system includes that return value in grid capacity and allocates actual generation according to demand. `GameTick_Gamma(...)` continues to handle lens consumption, photon production, warmup and belt I/O. Mode changes use the current `productId` on each tick.

Both prefixes use `ref PowerGeneratorComponent __instance` because the receiver is a struct.

## Build and package

- BepInEx 5, Harmony, `net472`; GUID `org.fyyy.fullphotonreceiver`.
- Compile reference: `../../DSP_Mods/AssemblyFromGame/Assembly-CSharp.dll`.
- `FullPhotonReceiverPlugin.cs` owns the entry point and both patches.
- Release builds produce a ZIP containing the plugin DLL, manifest, icon and [package README](../../../FullPhotonReceiver/package/README.md).

## Verification

From the repository root:

```powershell
dotnet run --project FullPhotonReceiver/tests/Checks.csproj -c Release -- "C:/Program Files (x86)/Applications/Steam/steamapps/common/Dyson Sphere Program/DSPGAME_Data/Managed"
```

For another installation, also pass `-p:GameManagedDir="<managed directory>"` to `dotnet run`. The checks apply the Harmony patches to the original game DLL and compare both modes with native full-sun, full-supply calculations across warmup, lens abilities, queued lenses, proliferator levels, sun direction and sphere supply. They verify zero sphere demand and mode-specific grid capacity, without advancing warmup or consuming lenses during the capacity phase.

Offline checks do not establish in-game acceptance. In-game validation covers both modes at night and without sphere power, native warmup and lens consumption, and power delivery under grid load.
