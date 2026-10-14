# BlueprintCamera implementation and verification

User behavior, file locations and scope are owned by [the package README](package/README.md).

## Structure

- `Core/Shot.cs`: versioned shot/frame contracts and import/edit validation. Positions are planet-local, rotations are quaternions, key times are normalized.
- `Core/Path.cs`: immutable path snapshot. Linear, spherical and Hermite curves share sampling and distance lookup; orbit uses its own axis-angle geometry. Spherical interpolation uses `atan2(cross length, dot)` to retain very short arcs near float dot-product limits. Adaptive sampling includes key boundaries and controls midpoint position error. Distance lookup uses binary search. Look-at/travel frames are transported during construction, making seeks independent of earlier playback.
- `Core/PlanetOrbit.cs`: simple-shot construction and radial zoom. Automatic framing fits the sphere silhouette in the narrower viewport dimension with a 12% distance margin; accepting a manual view retains its position. Both aim at the planet center. Zoom changes radius without changing FOV or orbit plane, staying above the surface and inside the serialized position limit with a rounding margin. A planet orbit follows a great circle through the starting position, using the projected camera up vector as its axis. Circular routes use exact angular progress and analytic length; other routes retain distance lookup. `Route.PlanetOrbit` is appended to preserve existing serialized enum values; its focus is always the planet origin, independent of the local-target fields.
- `Core/Playback.cs`: real-time clock, countdown, holds, integrated trapezoidal speed profile and loop state. It does not read game simulation time. Seeking or pausing does not change the world.
- `Core/Store.cs`: bounded JSON input, schema validation and atomic file replacement. A failed initial load disables subsequent library writes for that process.
- `Plugin.cs`: current-planet session, Unity conversion, free positioning, Harmony integration, display restoration, path preview and bookmark thumbnails. A negative `Selected` denotes the temporary simple shot; edits target that shot without adding it to `Library.Shots`. Explicit save copies it into the library. Reentering photography creates a fresh temporary planet shot; existing library entries remain intact.
- `TargetPicker.cs`: explicit center-ray picking against DSP collider data and the planet sphere. It handles distant buildings whose Unity colliders are not instantiated. Scanning is confined to the user-triggered pick, not frame updates; building bounds are not mesh-accurate selection.
- `CameraWindow.cs`: compact planet-orbit panel by default, with advanced editing behind one button. Preview/capture actions sit outside the scroll area. Advanced mode exposes draft parameter fields, undo/redo, named views and file actions. User edits rebuild the path; frame updates only evaluate it.

## Native integration

The mod owns `GameCamera.FrameLogic` while photography is active, preserving native poser state. It sets the main camera pose and resets its projection matrix because native `PostLogic` installs a custom matrix. Ending the session restores the captured camera pose/matrix before native updates resume.

Input is suppressed through `VFInput.UpdateGameStates` and the player's input/interaction hooks. Raw Unity input drives the photo controls. HUD visibility uses the existing canvas and hide shader, mecha visibility uses `PlayerController.modelVisible`; the serialized `GameData.hidePlayerModel` and `disableController` properties are untouched. Native cursor finalization gets a postfix because it resets cursor visibility after UI updates.

The integration was inspected against the installed DSP 0.10.35.29145 assembly on 2026-10-14 (MVID `4bd0041b-46ae-442c-aba2-460c8e3630d8`). The matching decompilation retains the camera frame/projection, input-state and cursor/display contracts used by the patches. Recheck the installed binary when the game updates. Build path properties allow another installation to be used.

## Build and checks

```powershell
dotnet build BlueprintCamera/BlueprintCamera.csproj -c Release
dotnet run --project BlueprintCamera/tests/Checks.csproj -c Release -f net10.0
dotnet run --project BlueprintCamera/tests/Checks.csproj -c Release -f net472
dotnet run --project BlueprintCamera/tests/native/NativeChecks.csproj -c Release -p:SkipPackage=true -- '<game-managed-dir>' '<bepinex-core-dir>'
git diff --check
```

`GameManagedDir` and `BepInExDir` can be overridden with MSBuild `-p:` arguments. Building Release writes `package/BlueprintCamera-0.2.1.zip`. `package.ps1` checks the archive entry count and hashes, assembly/manifest version, and PNG dimensions. Build artifacts and ZIPs are ignored by Git. No game DLL is distributed.

Core checks exercise planet framing in landscape/portrait viewports across three radii and polar/equatorial starts, exact circular speed/distance/aim/closure, unequal segment lengths, curved distance speed, exact key passage, polar/seam/antipodal surface paths, overhead look-at continuity, quaternion sign equivalence, FOV, seek determinism, phase boundaries, real-time/FPS independence, reverse/loop playback, JSON validation and byte-preserving failed saves. They run the production core on both .NET 10 and .NET Framework. Native checks exercise photography entry across all eight native command types with pickup mode on/off and inspect the installed DLL's targeted methods and relevant fields/properties; they do not execute Unity rendering.

## Game acceptance

Offline checks cannot establish rendered smoothness, input ownership, resource cleanup or compatibility with other camera/UI mods. Verify in DSP before calling the release game-tested:

For simple zoom, check buttons and wheel while stationary and previewing: preview pauses without changing the current orbital angle. Confirm wheel input over the panel scrolls it, capture ignores wheel zoom, and undo restores distance. Accept a manually pulled-back start, change duration/direction, switch to advanced and back, then capture and save/reload: the chosen distance must remain. Explicit auto-frame and reentry should refit the planet. Core tests cover these distance/framing contracts, orientation and phase preservation, plus surface and serialization limits; input/UI behavior still needs the game.

1. Enter from normal gameplay with the mouse over terrain, a building and a resource; verify entry is blocked in build/dismantle/pickup modes. Confirm the simple panel and a fully framed planet at 720p/1080p and portrait aspect, then preview both directions and capture two uninterrupted circles. Check pole/loop transitions, external-recorder focus loss and Esc recovery. Confirm a populated library is unchanged until a temporary shot is explicitly saved.
2. Record a 20-second half orbit with two-second holds. Check target framing, FOV, cursor/HUD/mecha hiding, first and last frames, and repeated takes.
3. Preview and record a curve through unevenly spaced keys; seek backward/forward and compare the same timestamp.
4. Traverse a pole, the longitude seam and a top-down target. Check altitude and absence of abrupt rotation.
5. Exercise pause, reverse, all loop modes, focus loss, Escape, pre-hidden HUD/mecha, leaving the planet, death and save/menu transitions. Confirm restoration and no error log.
6. Open advanced editing, freely position, lock, move the mouse and use the wheel; confirm the locked picture stays fixed. Edit text and ensure keystrokes do not move the camera or mecha. Save views and a temporary shot, restart, load the same planet, import/export JSON and reject an incompatible planet. Confirm thumbnails and original files after invalid input; delete the last library shot and reenter successfully.
7. On a busy factory, compare matching save, simulation speed, viewpoint and plugin configuration. Check recording frame pacing; no performance gain is claimed by the offline tests.
