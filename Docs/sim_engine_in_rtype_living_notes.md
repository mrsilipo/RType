# Sim Engine in RType Living Notes

Last updated: 2026-08-25

This is the current bookmark for RType Honda Racing engine/audio work. Keep it focused on active architecture and real remaining gaps.

## Current Decision

The heavy real-time Sim Engine-style C# experiment is parked. It was too fragile and expensive for the live racing path, and it repeatedly caused choppy, noisy, delayed engine audio.

The active game direction is now a race-ready sample engine:

- use real or offline-generated engine loops
- pitch samples from their recorded source RPM
- use one dominant normal sample, one VTEC sample, and a quiet additive idle bed
- keep the sound driven by race state: RPM, redline, throttle, load, VTEC blend, limiter state, gear, overrun, and shift shock
- use the same sample engine in both the race track and Engine Room
- keep Engine Sim/Andre source only as reference/offline sample-generation guidance, not as runtime code

## Active EK9 Sound Recipe

Profile:

- `Data/Audio/EngineAudioProfiles/ek9_b16b_5zigen_reference.json`

Samples:

- `Assets/Sounds/Honda/idle_0900.wav`
- `Assets/Sounds/Honda/normal_3500.wav`
- `Assets/Sounds/Honda/vtec_6200.wav`

Current mix:

- `idle_0900.wav`: additive bed across the rev range at 10%, not doubled at idle, not used under limiter
- `normal_3500.wav`: main non-VTEC engine loop, 1:1 pitch at 3500 RPM
- `vtec_6200.wav`: main VTEC loop above the VTEC blend range
- limiter: no dedicated limiter loop; actual redline forces the VTEC/high-RPM sample and bounces pitch/needle by 8% of redline
- limiter engages from actual RPM/redline, not visual tach bounce
- limiter audio crossfades over the final 50 RPM before redline
- visual tach/readout bounce is presentation only

Current EK9 race values:

- rev limiter/redline: 8400 RPM from `Data/Vehicles/ek9_reference_2000.json`
- VTEC activation: 5800 RPM
- active build path: `Data/VehicleBuilds/ek9_showroom_stock.json`

## Active Runtime Code

Race and Engine Room engine audio:

- `Audio/RaceEngineSampleSound.cs`
- `Audio/RaceEngineAudioState.cs`
- `Audio/VehicleAudioSystem.cs`
- `Audio/EngineAudioFrame.cs`
- `Core/RTypeEngineRoomScreen.cs`
- `Core/RTypeEngineRoomGame.cs`

Race entry:

- `Core/RacingGame.cs`
- `Program.cs`

Vehicle/build data still used by race:

- `Data/VehicleBuildDefinitionLoader.cs`
- `Data/VehicleDefinitionLoader.cs`
- `Vehicle/VehicleAudioParameters.cs`
- `Vehicle/VehicleSimulationParameters.cs`

Important note: some data properties still contain old `EngineSimulator*` names. They are legacy schema names and should be migrated later, but they are not evidence that the old procedural audio runtime is active.

## Disconnected Legacy Code

These old experimental systems are excluded from the active `RType.csproj` build:

- `Audio/EngineSim*.cs`
- `Audio/RTypeEngine/**/*.cs`
- `Audio/RTypeEngineDspSettings.cs`
- old `Core/EngineSim*.cs` probes/tools
- old `Core/RType*` engine/procedural probes/tools
- `Vehicle/EngineSimPowerUnit.cs`
- `Vehicle/RTypeEnginePowerUnit.cs`

They are retained on disk as reference/history only. Do not reconnect them to the game without an explicit decision.

Also retained as reference/offline material:

- `Assets/Sounds/engine-sim-v0.1.14a`
- `ReferenceSource/engine-sim`
- `Data/Legacy`
- `Data/RTypeEngineProfiles`

## Current Validation Commands

Run these after engine audio or launch-path changes:

```powershell
dotnet build RType.csproj -c Release --no-restore
dotnet run --project RType.csproj -c Release --no-build -- --audio-probe
dotnet run --project RType.csproj -c Release --no-build -- --audio-diagnostics-smoke
dotnet run --project RType.csproj -c Release --no-build -- --rtype-engine-room --auto-exit-ms 1000
```

Current known-good validation from 2026-08-25:

- Release build passes
- `--audio-probe` reports the EK9 race sample profile and confirms full-loop direct PCM checks
- Engine Room launches and exits using the race sample engine

## Next Work

Short term:

- keep tuning the EK9 sample recipe from real recordings
- migrate remaining `EngineSimulator*` data names to neutral race-audio/drivetrain names
- add a focused race-audio probe that sweeps RPM and reports active sample weights at idle, 3500, VTEC, and limiter
- drive-test the sample recipe on track and adjust only volumes/crossfades/sample choices, not procedural engine code

Long term:

- build a clean RType-owned engine simulation later only if needed for physics/upgrade depth
- if that happens, start from a small torque/RPM/load model first and keep audio sample playback as the stable presentation layer
