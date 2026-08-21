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
- The live stream queue is now `512` frames per buffer, `3` pending target buffers, `2` startup buffers, and `4` ready buffers.

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
- ready queue cap: initially `12`, then reduced to `4` in the freshness pass

Current Debug validation:

- `--engine-sim-stream-stress`: no low-buffer events, no emergency recovery events, worst reported fill `8.80 ms`
- `--audio-diagnostics-smoke`: no low-buffer events
- `--performance-probe`: audio synth around `1.38x` realtime

## 2026-08-21 Engine Audio Freshness Pass

After submitted latency was reduced to `34.8 ms`, remaining delay was likely coming from pre-generated buffers and target smoothing rather than the MonoGame submitted queue. The worker had been allowed to prebuild `12` ready buffers, which kept underruns away but could leave up to another `139 ms` of stale generated engine audio waiting behind newer RPM/load targets.

The ready queue is now capped at `4` buffers, and the Engine Sim-only target path tracks RPM, VTEC, limiter, shock, throttle transient, load, and overrun more quickly. The submitted playback queue remains `3 x 512` frames, so the playable audio queue is still `34.8 ms`; the extra pre-generated cushion is now much smaller and should feel more attached to the visual tach and car motion.

Current Debug validation:

- `--engine-sim-stream-stress`: no low-buffer events, no emergency recovery events, worst reported fill `13.76 ms`, ready cap `4`
- `--audio-diagnostics-smoke`: no low-buffer events
- `--performance-probe`: audio synth around `1.51x` realtime

## 2026-08-21 Engine Audio Timing Telemetry

The stream diagnostics now timestamp each game-side synthesis target and each generated audio buffer. Health logs report:

- target age at fill start
- target age at submit
- ready-buffer age at submit
- estimated audible age including pending submitted buffers
- target update gap from the game/audio update path

This makes the remaining lag measurable without a live drive test. Current validation shows the `3 x 512` submitted queue and `4` ready-buffer cap stay stable, with estimated audible age around `55-61 ms` in the command-line stress/smoke probes and no low-buffer or emergency recovery events.

Current validation:

- `--engine-sim-stream-stress`: no low-buffer events, no emergency recovery events, worst estimated audible age `54.85 ms`, worst target age at submit `31.63 ms`
- `--audio-diagnostics-smoke`: no low-buffer events, worst estimated audible age `60.59 ms`
- `--performance-probe`: audio synth around `1.51x` realtime

## 2026-08-21 Legacy Engine Sample Cleanup

The runtime vehicle engine-audio path is now Engine Sim-only. `VehicleAudioSystem` no longer creates pitch-shifted engine loops, high-RPM loops, limiter sample loops, or multi-sample Honda banks. If Engine Sim is disabled, the vehicle engine path logs that there is no legacy fallback instead of silently returning to the old sample model.

The vehicle audio data contract and loader also no longer expose `engineLoop`, `highRpmLoop`, `engineSamples`, playback-ratio, or engine-sample crossfade fields. Existing vehicle JSON may still contain historical sample fields, but they are ignored by the loader and have no runtime effect.

Tyre screech and other non-engine audio remain sample-based.

Current validation:

- `--audio-diagnostics-smoke`: Engine Sim initialized as the only engine mode; no low-buffer events
- `--engine-sim-stream-stress`: no low-buffer events, no emergency recovery events, worst estimated audible age `77.72 ms`
- `--performance-probe`: audio synth around `1.55x` realtime
- `--physics-smoke-test`: passed

## 2026-08-21 Low-Latency Stream Freshness Pass

The Engine Sim stream now targets `2 x 512` submitted buffers instead of `3 x 512`, reducing the nominal submitted audio queue from `34.8 ms` to `23.2 ms`. Startup still waits for two buffers so playback does not begin empty.

Ready-buffer handling now prefers fresher targets. If an older generated buffer is waiting and a newer generated buffer is available, the older one is recycled instead of submitted. Generated buffers also record the newest synthesis target used during their internal 64-frame target refreshes, so timing telemetry reflects the actual target data used inside the buffer instead of only the target captured at fill start.

Current validation:

- `--engine-sim-stream-stress`: no low-buffer events, no emergency recovery events, worst estimated audible age `42.32 ms`, worst ready age at submit `9.39 ms`
- `--audio-diagnostics-smoke`: no low-buffer events, worst estimated audible age `43.12 ms`
- `--performance-probe`: audio synth around `1.60x` realtime
- `--physics-smoke-test`: passed

## 2026-08-21 Fluid Solver Runtime Pass

The managed gas-flow model has had its hot path tightened enough to run the Honda profile at `20000 Hz` with `2` fluid substeps in the live stream. The main savings came from removing redundant per-flow sanitization, short-circuiting zero-flow paths before pressure work, replacing generic exponent math in the dynamic-pressure path with direct formulas for the 5-DOF gas model, and avoiding avoidable wrapping/rounding in piston/chamber volume updates.

The Honda Engine Sim audio profile now requests `2` fluid substeps. The live stream cap was raised to `2`, while the submitted queue remains `2 x 512` frames. The ready queue is `4` buffers so the worker has enough cushion for the heavier solver, and stale-ready-buffer pruning keeps that cushion from becoming extra audible lag.

The performance probe now reports explicit fluid-step variants so future tuning can compare `1`, `2`, and `4` substeps directly.

Current validation:

- `--engine-sim-stream-stress`: no low-buffer events, no emergency recovery events, worst fill `12.75 ms`, worst estimated audible age `42.19 ms`
- `--audio-diagnostics-smoke`: no low-buffer events, worst fill `12.50 ms`, worst estimated audible age `42.58 ms`
- `--performance-probe`: default `2`-step audio synth around `1.14x` realtime; variant probe `1` step `2.06x`, `2` steps `1.15x`, `4` steps `0.61x`
- `--physics-smoke-test`: passed

## 2026-08-21 Hybrid Exhaust IR Pass

The Engine Sim DSP now supports a hybrid exhaust convolution path. The first `512` impulse-response taps still run as exact full-rate direct convolution, preserving the attack and early exhaust character. Any remaining tail is folded into a lower-rate convolution tail using 8-sample groups, which keeps the full exhaust response present without paying full direct-convolution cost for every late tap.

The Honda profile now requests `2048` impulse-response taps. The current local `mild_exhaust.wav` clips to `1893` useful taps, so runtime now uses the full useful local IR instead of the previous `512`-tap slice. Diagnostics report the active split, for example `hybrid convolution direct 512, tail 1381->173`.

Current validation was run in a clean temporary worktree because unrelated renderer work-in-progress currently breaks the main worktree build:

- clean worktree `dotnet build`: passed
- `--performance-probe`: default `2`-step audio synth around `1.07x` realtime; variant probe `1` step `1.93x`, `2` steps `1.12x`, `4` steps `0.59x`
- `--engine-sim-stream-stress`: no low-buffer events, no emergency recovery events, worst fill `12.84 ms`, worst estimated audible age `39.86 ms`
- `--audio-diagnostics-smoke`: no low-buffer events, worst fill `15.05 ms`, worst estimated audible age `43.14 ms`

## 2026-08-21 Live Stream Quality Fallback

Two live gameplay runs with the `20000 Hz / 2` fluid-step profile plus the full hybrid exhaust IR showed the command-line probes were too optimistic under real render/gameplay load. The live logs showed repeated starvation:

- latest run: repeated `engine-sim-buffer-low` and `engine-sim-stream-recovery`
- worst fill `83.71 ms` against an `11.6 ms` buffer
- worst estimated audible age `110.33 ms`

The live `EngineSimulatorSound` stream now caps realtime gas-flow audio to `1` fluid step again. The shared Honda profile still requests `2` fluid steps so offline/fidelity probes can continue measuring the richer profile, but gameplay chooses the safer setting to keep buffers ahead of MonoGame. The full hybrid exhaust IR remains active.

Expected live diagnostics now include `engine-sim-realtime-cap` with `fluid steps 2 -> 1 for live audio stream`.

## 2026-08-21 Stream Continuity Fix

A follow-up gameplay run confirmed the realtime fluid-step cap was active, but audio was still choppy and frame rate suffered:

- `engine-sim-synth` reported `sim 20000 Hz, fluid steps 1`
- the stream still logged repeated `engine-sim-buffer-low` and `engine-sim-stream-recovery`
- typical emergency fills were `26-32 ms`, with health samples up to roughly `57 ms`
- estimated audible age still floated around `60-90 ms`

The remaining issue was the live stream policy. It discarded already-generated buffers whenever the game target state was newer, even while the audio device was below its pending-buffer target. That kept the stream chasing fresh RPM/load data, but it wasted generated audio and forced emergency synthesis on the game thread. The stream now keeps valid generated buffers for continuity, starts with `3` submitted buffers, keeps a larger worker-side reserve, and only uses emergency game-thread synthesis when the audio device has no pending buffers left.

Validation after this change:

- `dotnet build RetroRacer.csproj`: passed
- `--engine-sim-stream-stress`: `0` low-buffer events, `0` emergency recovery events, worst fill `9.66 ms`
- `--audio-diagnostics-smoke`: no low-buffer or emergency recovery events, worst fill `7.77 ms`
- `--performance-probe`: live `20000 Hz / 1` fluid-step synth measured about `2.04x` realtime; profile `20000 Hz / 2` remains about `1.12x` realtime offline

## 2026-08-21 Live Latency Trim

The next live run felt much better and confirmed the starvation path was fixed:

- no `engine-sim-buffer-low` events
- no `engine-sim-stream-recovery` events
- emergency generation stayed at `0`

The remaining issue moved from starvation to latency. Stream health showed steady pending depth and stable fills, but the estimated audible age sat around `105 ms`, with ready buffers often around `77 ms` old at submit time. That is stable, but it can smear quick state changes such as shifts, limiter pulses, VTEC transition, and throttle snaps.

The stream now targets `2` steady pending buffers, keeps startup at `3`, reduces worker ready capacity to `3`, and recycles stale ready buffers older than `40 ms` only when at least one fresher reserve buffer remains. Diagnostics now include a `stale` count in `engine-sim-stream-health` so the next live run can show whether stale recycling is active without reintroducing starvation.

Validation after this change:

- `dotnet build RetroRacer.csproj`: passed
- `--engine-sim-stream-stress`: `0` low-buffer events, `0` emergency recovery events, worst fill `7.66 ms`, worst estimated audible age `48.30 ms`
- `--audio-diagnostics-smoke`: no low-buffer or emergency recovery events, worst fill `11.57 ms`, worst estimated audible age `50.99 ms`

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
- `Core/AudioProbe.cs`
- `Core/EngineSimProfileProbe.cs`
- `Core/EngineSimStreamStressProbe.cs`
- `Core/PerformanceProbe.cs`
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

   The MR specifies `simulation_frequency: 20000`. The managed C# synth now runs live gameplay audio at `20000 Hz` with `1` fluid substep for stability under render/gameplay load. The Honda profile still requests `2` substeps for offline/fidelity measurement, but the live stream caps it down.

2. Fluid simulation substeps

   Engine Sim defaults to more fluid substeps per engine timestep. Our port supports configurable fluid substeps and the EK9 audio profile targets `2`, but live gameplay currently caps to `1` after real runs showed repeated buffer starvation at `2`. `4` substeps remains below realtime in Debug and the native Engine Sim default of `8` is still not viable in the managed hot path.

3. Convolution length

   Engine Sim supports up to `10000` impulse-response taps. The local Honda `mild_exhaust.wav` clips to about `1893` useful frames, and the runtime now uses that full useful local tail through a hybrid convolution path. This is still an approximation for the late tail rather than sample-exact full-rate convolution for every tap.

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
2. Use the timing telemetry during real gameplay to confirm whether the remaining perceived lag matches the reported estimated audible age.
3. Continue optimizing the synth toward live `2+` fluid substeps at the Honda MR `20000` Hz rate; `2` is useful offline but not stable enough during real gameplay yet.
4. Add a native C++ Engine Sim bridge once CMake/build tooling is available, then feed game RPM/load from the real `PistonEngineSimulator`/constraint system instead of the managed approximation.
5. Drive-test the hybrid IR tail for tone and CPU stability during actual gameplay; fall back to `512` taps or a coarser tail only if live gameplay reports buffer pressure.
6. Revisit the idle jitter/noise scaling after stream underruns are gone.
7. Continue the physics integration by replacing remaining launch/clutch heuristics with data from the coupled crank, clutch, and wheel-speed solver.

### 2026-08-21 Live underrun cushion

The latest live run was mostly stable at roughly 49-60 ms estimated audible age, but it recorded one real low-buffer event at 8400 RPM. The stream was generating about 80 buffers per second while one synthesis pass reached 22 ms for an 11.6 ms audio buffer. The playback target remains two buffers; the ready-buffer capacity is increased from three to four so one slow synthesis pass has an additional non-audible scheduling cushion without increasing the normal playback latency.

The following run had no underruns, but was recycling approximately 43 stale ready buffers every three seconds. Those buffers had already advanced the synthesizer state without being played, which could fragment rapid RPM and load transitions. Ready buffers are now consumed sequentially so the generated waveform remains continuous.
