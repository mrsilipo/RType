# Engine Sim Audio Audit

Date: 2026-08-20

Scope: compare the in-game EK9 engine audio path with the bundled Engine Sim 0.1.14a Honda VTEC MR file and the public Engine Sim core implementation.

## 2026-08-21 Integration Pass

The live Engine Sim audio path now receives one explicit `EngineAudioFrame` per game update. That frame is built from the same vehicle state used by physics and carries RPM, redline, throttle, load, VTEC blend/kick, limiter, overrun, shock, clutch slip, engine braking, speed, gear, and camera context.

The synth target now also includes continuous layer drives for intake, throttle transient, and driveline texture. The DSP uses those as subtle tonal shaping over the generated gas-flow signal, not as sample triggers, so VTEC, throttle snaps, clutch kick, limiter, and overrun remain tied to the combustion waveform.

## 2026-08-21 Engine Profile Pipeline

Engine Sim audio data is no longer embedded directly in the EK9 vehicle definition. `Data/Vehicles/ek9_reference_2000.json` now points to `Data/EngineProfiles/honda_b18c5_vtec_engine_sim.json`, and the loader now has two clear data paths:

1. MR-owned engine structure such as cylinder count, firing order, cam geometry, timing, crank, clutch, and transmission data comes from the active MR script first, then falls back to vehicle/profile/default values if needed.
2. Runtime knobs such as simulation rate, fluid-step budget, DSP gain, overrun/shock/limiter gains, VTEC intensity, and mix volume merge as vehicle-specific override, shared engine profile default, then MR-derived value or code fallback.

The shared Honda profile owns the MR script path, impulse response path, 20 kHz audio simulation target, runtime fluid-step budget, DSP gains, overrun/shock/limiter gains, and VTEC intensity. The EK9 JSON keeps only the car-specific enabled state and mix volume for now.

Validation added to `--engine-sim-probe`:

- confirms the shared profile path resolves
- confirms the active MR script resolves
- confirms the active impulse response resolves
- prints the profile identity alongside the runtime EK9 comparison

## 2026-08-21 Stream Resilience And Hot Path Pass

The Engine Sim stream now warms the synth once before playback so the first real buffers do not pay JIT/filter setup cost during active audio. Once playback has started, the stream also has an emergency recovery path: if MonoGame reaches a critically low pending-buffer count and the background worker has no ready buffers, the update thread can generate up to two immediate buffers and submit them with the same de-click continuity path. Recovery events are logged separately from normal worker fills.

The gas-flow hot path also no longer records per-cylinder pressure history because the current flame-speed model does not consume firing-pressure history. Sampled flow curves now use a lazy lookup cache, keeping the same functional shape while avoiding repeated searches for valve flow and turbulence curves.

Current Debug validation:

- `20000 Hz / 1` fluid step remains the runtime-safe profile, around `1.5x` realtime in the performance probe.
- `20000 Hz / 2` fluid steps is improved but still below realtime in Debug, around `0.84x`.
- `16000 Hz / 2` is around realtime, but it violates the requested 20 kHz target, so it is not used as the gameplay profile.
- The new `--engine-sim-stream-stress` probe reported no low-buffer events and no emergency recovery events in validation.

## 2026-08-21 Choppy Runtime Fix

A live Debug run showed the engine audio was not using the Honda profile at all:

- `MR none`
- `conv taps 0`
- `pressure scale 2400`
- `fluid steps 8`
- repeated `engine-sim-buffer-low` events with pending buffers at `0` or `1`

That was the bad configuration causing the very choppy engine audio. The current Debug build now copies `Data/EngineProfiles/honda_b18c5_vtec_engine_sim.json` and confirms the runtime path uses the Honda MR, `512` impulse-response taps, `pressure scale 1`, and `fluid steps 1`.

Guardrails added:

- Engine Sim audio parameter defaults now match the realtime Honda gameplay profile instead of the expensive generic fallback.
- The loader falls back to the bundled Honda Engine Sim profile/MR only when Engine Sim is enabled and the vehicle data has no usable profile or MR path.
- The live audio stream caps realtime gas-flow audio to `1` fluid substep until the deeper gas-flow optimizer makes higher substep counts safe.
- The live stream queue is now `512` frames per buffer, `3` pending target buffers, `2` startup buffers, and `12` ready buffers.

Current Debug validation after the fix:

- `--engine-sim-probe`: Honda profile loaded, `20000 Hz`, `fluid steps 1`, `dsp scale/gain 1/0.62`
- `--audio-diagnostics-smoke`: no low-buffer events
- `--engine-sim-stream-stress`: no low-buffer events, no emergency recovery events, worst reported fill `17.87 ms`
- `--performance-probe`: audio synth around `1.57x` realtime

## 2026-08-21 Engine Audio Latency Pass

The stream was stable but felt late because the previous safety queue targeted `12` submitted `1024`-frame buffers, about `279 ms` of queued engine audio before hardware/output latency.

The live stream now uses smaller `512`-frame buffers and targets `3` submitted buffers:

- buffer duration: `11.6 ms`
- target submitted latency: `34.8 ms`
- startup buffers: `2`
- ready queue cap: `12`

Current Debug validation:

- `--engine-sim-stream-stress`: no low-buffer events, no emergency recovery events, worst reported fill `8.80 ms`
- `--audio-diagnostics-smoke`: no low-buffer events
- `--performance-probe`: audio synth around `1.38x` realtime

Changed files:

- `Audio/EngineAudioFrame.cs`
- `Audio/VehicleAudioSystem.cs`
- `Audio/EngineSimulatorSound.cs`
- `Audio/EngineSimulatorSampleSynth.cs`
- `Audio/EngineSimDspProcessor.cs`
- `Vehicle/VehicleState.cs`
- `Vehicle/SimpleVehicleSimulator.cs`
- `Vehicle/VehicleAudioParameters.cs`
- `Data/VehicleDefinitionLoader.cs`
- `Data/EngineProfiles/honda_b18c5_vtec_engine_sim.json`
- `Data/Vehicles/ek9_reference_2000.json`
- `Core/EngineSimProfileProbe.cs`
- `Core/EngineSimStreamStressProbe.cs`
- `Program.cs`
- `Audio/EngineSimGasFlowModel.cs`

## Immediate Finding

Runtime diagnostics confirmed actual audio starvation:

- `engine-sim-buffer-low` reached pending buffer counts of `1` and `0` while playing.
- That matches the small cuts/glitches heard in-game.
- The stream now uses a larger queue, starts with more buffers ready, gives the generator thread higher priority, and responds to MonoGame `BufferNeeded` events.

Changed file:

- `Audio/EngineSimulatorSound.cs`

## Current Match

The game is already using the important Honda MR data:

- `Assets/Sounds/EngineSim/HondaB18C5/assets/engines/honda_b18c5_vtec.mr`
- 4 cylinders
- firing order `1-3-4-2`
- two exhaust routes
- per-cylinder sound attenuation
- Honda B18C5 bore/stroke/rod/chamber values
- low cam and VTEC cam lift/duration/center values
- ignition timing curve
- `Assets/Sounds/EngineSim/HondaB18C5/es/sound-library/new/mild_exhaust.wav` impulse response
- Engine Sim style DSP stages: jitter, high-frequency derivative mix, air noise, DC removal, convolution, antialiasing, and leveling

## Missing Or Reduced Versus Engine Sim

1. Audio simulation rate

   The MR specifies `simulation_frequency: 20000`. The managed C# synth now runs the EK9 audio model at `20000 Hz` with `1` fluid substep for gameplay. That keeps the requested engine timestep while staying above realtime in Debug. Higher fluid settings are still expensive in Debug: `20000 Hz` with `2` fluid substeps measured below realtime, while Release builds have enough headroom for higher settings.

2. Fluid simulation substeps

   Engine Sim defaults to multiple fluid substeps per engine timestep. Our port supports configurable fluid substeps and imports the Engine Sim default of `8`, but EK9 gameplay currently overrides this to `1` because higher settings are not realtime in Debug.

3. Convolution length

   Engine Sim supports up to `10000` impulse-response taps. The local Honda `mild_exhaust.wav` clips to about `1893` useful frames, but the EK9 JSON currently uses `512` taps. This is a reasonable runtime compromise, but it is not the full exhaust response.

4. Stream architecture

   Engine Sim keeps large input and output ring buffers and actively manages synthesizer latency around a target. Our MonoGame stream uses `DynamicSoundEffectInstance`, so underrun prevention has to be handled through pending buffers and ready buffers.

5. Full mechanical solver

   Engine Sim audio is fed by its actual crankshaft, piston, combustion, intake, exhaust, clutch, transmission, and vehicle simulation. The game now imports the Honda MR crank, clutch, transmission, gear, vehicle mass, tire radius, differential, and rolling resistance values, and it runs a first managed coupled crank/clutch/transmission pass for the EK9. This is closer to the Engine Sim boundary, but it is still not the exact native C++ constraint solver.

6. Runtime DSP shaping

   Engine Sim applies configured jitter/noise directly. Our port scales some jitter/noise by operating point to reduce idle grain and now shapes intake/transient/driveline texture from the generated pressure signal. That helps with harshness and driving feedback, but it is a deliberate deviation from the reference.

7. MR parser coverage

   The current parser extracts the Honda script values we need, but it is not a full MR DSL interpreter. Future engines with different script structure may need a stronger parser.

## Recommended Next Work

1. Drive-test the new stream and confirm whether `engine-sim-buffer-low` stops appearing during real gameplay.
2. Add audio timing telemetry: generated buffers per second, queue depth, worst generation time, underrun count.
3. Optimize the synth enough to restore multiple fluid substeps at the Honda MR `20000` Hz rate without dropping below realtime.
4. Add a native C++ Engine Sim bridge once CMake/build tooling is available, then feed game RPM/load from the real `PistonEngineSimulator`/constraint system instead of the managed approximation.
5. Replace the direct convolution with a cheaper partitioned or hybrid convolution so the full `mild_exhaust.wav` tail can be used.
6. Revisit the idle jitter/noise scaling after stream underruns are gone.
7. Continue the physics integration by replacing remaining launch/clutch heuristics with data from the coupled crank, clutch, and wheel-speed solver.
