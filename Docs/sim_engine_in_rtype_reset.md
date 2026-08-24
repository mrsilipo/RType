# Sim Engine in RType Reset

This is the agreed direction for the engine simulator work after rejecting the native-wrapper approach.

## Goal

RType needs its own C# engine simulation that drives both vehicle behavior and engine audio.

The simulator should eventually own:

- RPM, crank phase, limiter behavior, and RPM gauge output
- torque production and engine braking
- clutch, gear, flywheel, and driveline coupling
- combustion/exhaust pulse generation for procedural sound
- VTEC/cam profile changes
- configurable intake, exhaust, displacement, compression, flywheel, clutch, turbo, and ECU behavior
- data-driven engine profiles and upgrade parts

## Non-Goals

- Do not run Engine Sim as a native DLL or sidecar process.
- Do not vendor Engine Sim source into the runtime project.
- Do not maintain a parallel simulator where audio and physics disagree.
- Do not keep tuning the current managed sound approximation as the final architecture.

## How Engine Sim Should Be Used

Engine Sim is a source reference only. We study it to understand the architecture and math, then implement RType-owned C# systems.

Important source ideas to study and reproduce in our own design:

- `Simulator::startFrame`, `simulateStep`, `endFrame`
- crankshaft, piston, connecting rod, and inertia modeling
- combustion chamber ignition and pressure changes
- intake and exhaust gas flow
- valvetrain and VTEC cam switching
- transmission and clutch coupling
- synthesizer input from exhaust-system pressure pulses
- DSP chain: jitter, DC filtering, derivative/direct-flow mix, convolution, anti-aliasing, leveling

## Current Legacy Runtime

These files are still active so the game has working audio/physics while the replacement is built:

- `Audio/EngineSimulatorSound.cs`
- `Audio/EngineSimulatorSampleSynth.cs`
- `Audio/EngineSimGasFlowModel.cs`
- `Audio/EngineSimDspProcessor.cs`
- `Vehicle/EngineSimPowerUnit.cs`

Treat these as legacy scaffolding. They can provide clues, but the replacement should be organized around a clean RType engine core instead of more patches to the current approximation.

## Proposed New Structure

Build the new system in clear layers:

- `Simulation/Engines/Profiles`
  - immutable engine and part definitions
  - B16B, B18C, future K20, C-series, turbo variants
- `Simulation/Engines/Core`
  - crankshaft, cylinders, pistons, rods, combustion chambers, intake, exhaust, valvetrain
- `Simulation/Engines/Powertrain`
  - clutch, flywheel, gearbox, differential handoff, engine braking
- `Audio/EngineSynthesis`
  - consumes engine pulse state from the core and renders audio
- `Data/VehicleBuilds` and `Data/RTypeEngineProfiles`
  - selected vehicle builds, engine catalogs, tune data, and resolved simulator profiles

## First Implementation Chunk

Start small and source-guided:

1. Create an `RTypeEngineProfile` model for engine geometry, firing order, compression, redline, idle, cams, intake, exhaust, and DSP/sound parameters.
2. Create an `RTypeEngineRuntime` that advances crank phase, ignition events, simple combustion pressure, torque, and exhaust pulse output.
3. Replace audio pulse generation with `RTypeEngineRuntime` output, but keep the existing audio stream/buffer management.
4. Once sound is stable, route vehicle torque and RPM gauge state from the same runtime.
5. Retire the old `EngineSim*` approximation classes once the new runtime covers audio and power delivery.

## Verification

Every chunk should prove:

- the game still builds
- audio is not fragmented
- RPM gauge follows the same crank state as the sound
- limiter and VTEC are visible in both sound and vehicle behavior
- diagnostics identify whether the legacy path or new RType engine path is active
