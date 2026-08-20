using Microsoft.Xna.Framework;
using RetroRacer.Camera;
using RetroRacer.Vehicle;

namespace RetroRacer.Audio;

public sealed class VehicleAudioSystem : IDisposable
{
    private const string TyreSpinLoopPath = "Assets/Sounds/Generic/TyreScreech_001.wav";
    private const string ControlLossScreechLoopPath = "Assets/Sounds/Generic/TyreScreech_002.wav";
    private const float TyreSpinLoopStartRatio = 0f;
    private const float TyreSpinLoopEndRatio = 1f;
    private const float TyreChirpLoopStartRatio = 0.02f;
    private const float TyreChirpLoopEndRatio = 0.22f;
    private const float ControlLossScreechLoopStartRatio = 0.18f;
    private const float ControlLossScreechLoopEndRatio = 0.86f;

    private VehicleAudioParameters _parameters = new();
    private LoopingPitchedSound? _engineLoop;
    private LoopingPitchedSound? _highRpmLoop;
    private EngineSampleLoop[] _engineSampleLoops = [];
    private EngineSampleLoop[] _normalEngineSampleLoops = [];
    private EngineSampleLoop[] _highRpmEngineSampleLoops = [];
    private EngineSampleLoop? _limiterEngineSampleLoop;
    private EngineSimulatorSound? _engineSimulatorSound;
    private LoopingPitchedSound? _tyreSpinLoop;
    private LoopingPitchedSound? _tyreChirpLoop;
    private LoopingPitchedSound? _controlLossScreechLoop;
    private float _smoothedEngineRpm;
    private float _highRpmBlend;
    private bool _highRpmAudioLatched;
    private float _highRpmAudioReleaseSeconds;
    private float _highRpmLoudnessTrim = 1f;
    private float _smoothedLimiterSampleIntensity;
    private float _smoothedTyreSpinIntensity;
    private float _tyreChirpEnvelope;
    private float _previousTyreChirpSource;
    private float _smoothedControlLossScreechIntensity;
    private float _previousSwaySteerInput;
    private float _previousSwayLateralAcceleration;
    private float _previousSwayBodyRollRadians;
    private bool _hasSwayScreechHistory;
    private float _previousEngineAudioThrottle;
    private float _throttleTransientEnvelope;
    private bool _hasEngineAudioFrameHistory;
    private bool _available = true;

    public void SetVehicle(VehicleAudioParameters parameters)
    {
        DisposeLoops();
        _parameters = parameters;
        _smoothedEngineRpm = 0f;
        _highRpmBlend = 0f;
        _highRpmAudioLatched = false;
        _highRpmAudioReleaseSeconds = 0f;
        _highRpmLoudnessTrim = 1f;
        _smoothedLimiterSampleIntensity = 0f;
        _smoothedTyreSpinIntensity = 0f;
        _tyreChirpEnvelope = 0f;
        _previousTyreChirpSource = 0f;
        _smoothedControlLossScreechIntensity = 0f;
        _previousEngineAudioThrottle = 0f;
        _throttleTransientEnvelope = 0f;
        _hasEngineAudioFrameHistory = false;
        ResetSwayScreechHistory();
        _available = true;

        try
        {
            bool useEngineSimulatorOnly = parameters.EngineSimulatorEnabled && parameters.EngineSimulatorVolume > 0.001f;
            AudioDiagnostics.Log(
                "vehicle-audio",
                $"initializing vehicle audio, engineSimOnly={useEngineSimulatorOnly}, sampleBank={parameters.EngineSamples.Length}");

            _engineSimulatorSound = parameters.EngineSimulatorEnabled
                ? new EngineSimulatorSound(parameters)
                : null;

            if (useEngineSimulatorOnly)
            {
                AudioDiagnostics.Log("engine-audio-mode", "Engine Sim only; legacy engine sample loops disabled");
            }
            else if (parameters.EngineSamples.Length > 0)
            {
                LoadEngineSampleBank(parameters.EngineSamples);
            }
            else
            {
                WavLoopSource? engineSource = LoadSource(parameters.EngineLoopPath);
                WavLoopSource? highRpmSource = LoadSource(parameters.HighRpmLoopPath);
                _engineLoop = engineSource is null ? null : new LoopingPitchedSound(engineSource, $"engine {parameters.EngineLoopPath}");
                _highRpmLoop = highRpmSource is null ? null : new LoopingPitchedSound(highRpmSource, $"high-rpm {parameters.HighRpmLoopPath}");
                _highRpmLoudnessTrim = CalculateHighRpmLoudnessTrim(engineSource, highRpmSource);
            }

            _tyreSpinLoop = TryLoadSlicedLoop(TyreSpinLoopPath, TyreSpinLoopStartRatio, TyreSpinLoopEndRatio, "tyre-spin sustain");
            _tyreChirpLoop = TryLoadSlicedLoop(TyreSpinLoopPath, TyreChirpLoopStartRatio, TyreChirpLoopEndRatio, "tyre-spin chirp");
            _controlLossScreechLoop = TryLoadSlicedLoop(ControlLossScreechLoopPath, ControlLossScreechLoopStartRatio, ControlLossScreechLoopEndRatio, "control-loss screech");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException)
        {
            AudioDiagnostics.Log("vehicle-audio-error", exception.ToString());
            Console.WriteLine($"Vehicle audio disabled: {exception.Message}");
            DisposeLoops();
            _available = false;
        }
    }

    public void Update(VehicleState vehicle, CameraMode cameraMode, bool active, bool paused, float dt)
    {
        if (!_available || !HasEngineAudio || !active)
        {
            Stop();
            return;
        }

        try
        {
            float rpm = MathF.Max(300f, vehicle.DisplayedRpm);
            if (_smoothedEngineRpm <= 0f)
            {
                _smoothedEngineRpm = rpm;
            }
            else
            {
                float responseRate = vehicle.IsShifting
                    ? 26f
                    : vehicle.RevLimiterActive
                        ? 22f
                        : 18f;
                float rpmBlend = 1f - MathF.Exp(-responseRate * MathHelper.Clamp(dt, 0f, 1f / 20f));
                _smoothedEngineRpm = MathHelper.Lerp(_smoothedEngineRpm, rpm, MathHelper.Clamp(rpmBlend, 0f, 1f));
            }

            rpm = _smoothedEngineRpm;
            float pauseScale = paused ? 0f : 1f;
            float driveVolume = MathHelper.Clamp(_parameters.EngineVolume * pauseScale, 0f, 1f);
            float sampleDriveVolume = driveVolume * MathHelper.Clamp(_parameters.EngineSampleVolume, 0f, 1f);
            float engineSimulatorRpm = SelectEngineSimulatorAudioRpm(vehicle, rpm);
            float targetHighBlend = CalculateTargetHighRpmBlend(
                vehicle,
                MathF.Max(300f, vehicle.EngineSimulatorPowerActive ? engineSimulatorRpm : vehicle.Rpm),
                dt);
            float highBlendRate = _engineSampleLoops.Length > 0
                ? (vehicle.IsShifting ? 95f : 72f)
                : (vehicle.IsShifting ? 18f : 10f);
            float highBlendStep = 1f - MathF.Exp(-highBlendRate * MathHelper.Clamp(dt, 0f, 1f / 20f));
            _highRpmBlend = MathHelper.Lerp(_highRpmBlend, targetHighBlend, MathHelper.Clamp(highBlendStep, 0f, 1f));
            float normalGain = 1f - _highRpmBlend;
            float highGain = _highRpmBlend;

            if (_engineSampleLoops.Length > 0)
            {
                UpdateEngineSampleBank(vehicle, rpm, sampleDriveVolume, dt);
            }
            else
            {
                float playbackRatio = CalculateEnginePlaybackRatio(rpm);
                _engineLoop?.Update(playbackRatio, sampleDriveVolume * normalGain);
                _highRpmLoop?.Update(playbackRatio, sampleDriveVolume * highGain * _highRpmLoudnessTrim);
            }

            float throttleTransient = UpdateEngineAudioThrottleTransient(vehicle, dt);
            EngineAudioFrame engineFrame = EngineAudioFrame.FromVehicleState(
                _parameters,
                vehicle,
                engineSimulatorRpm,
                _highRpmBlend,
                driveVolume,
                cameraMode,
                paused,
                throttleTransient);
            _engineSimulatorSound?.Update(engineFrame);
            UpdateTyreScreechLoops(vehicle, pauseScale, dt);
        }
        catch (InvalidOperationException exception)
        {
            AudioDiagnostics.Log("vehicle-audio-error", exception.ToString());
            Console.WriteLine($"Vehicle audio disabled: {exception.Message}");
            Stop();
            _available = false;
        }
    }

    public void Stop()
    {
        _engineLoop?.Stop();
        _highRpmLoop?.Stop();
        _engineSimulatorSound?.Stop();
        StopEngineSampleLoops();
        _tyreSpinLoop?.Stop();
        _tyreChirpLoop?.Stop();
        _controlLossScreechLoop?.Stop();
        _smoothedEngineRpm = 0f;
        _highRpmBlend = 0f;
        _highRpmAudioLatched = false;
        _highRpmAudioReleaseSeconds = 0f;
        _highRpmLoudnessTrim = 1f;
        _smoothedLimiterSampleIntensity = 0f;
        _smoothedTyreSpinIntensity = 0f;
        _tyreChirpEnvelope = 0f;
        _previousTyreChirpSource = 0f;
        _smoothedControlLossScreechIntensity = 0f;
        _previousEngineAudioThrottle = 0f;
        _throttleTransientEnvelope = 0f;
        _hasEngineAudioFrameHistory = false;
        ResetSwayScreechHistory();
    }

    private void StopEngineSampleLoops()
    {
        foreach (EngineSampleLoop sample in _engineSampleLoops)
        {
            sample.SmoothedVolume = 0f;
            sample.Loop.Stop();
        }
    }

    private static float SelectEngineSimulatorAudioRpm(VehicleState vehicle, float fallbackRpm)
    {
        float crankRpm = vehicle.EngineSimulatorPowerActive ? vehicle.EngineSimulatorCrankRpm : 0f;
        return crankRpm > 450f ? crankRpm : fallbackRpm;
    }

    private float UpdateEngineAudioThrottleTransient(VehicleState vehicle, float dt)
    {
        float clampedDt = MathHelper.Clamp(dt, 0.001f, 1f / 20f);
        float throttle = MathHelper.Clamp(MathF.Max(vehicle.Throttle, vehicle.EffectiveThrottle), 0f, 1f);
        float throttleRisePerSecond = _hasEngineAudioFrameHistory
            ? MathF.Max(0f, throttle - _previousEngineAudioThrottle) / clampedDt
            : 0f;
        float throttleSnap = SmoothStep(1.4f, 7.8f, throttleRisePerSecond) *
                             SmoothStep(0.04f, 0.42f, throttle);
        float heldThrottleShiftKick = SmoothStep(0.62f, 0.96f, throttle) *
                                      MathF.Max(vehicle.ShiftKickIntensity, vehicle.PowertrainShockIntensity) *
                                      0.72f;
        float limiterPulse = vehicle.RevLimiterActive
            ? SmoothStep(0.08f, 0.80f, vehicle.RevLimiterBounceIntensity) * 0.30f
            : 0f;

        _throttleTransientEnvelope = MathF.Max(
            _throttleTransientEnvelope * MathF.Exp(-18f * clampedDt),
            MathHelper.Clamp(MathF.Max(throttleSnap, MathF.Max(heldThrottleShiftKick, limiterPulse)), 0f, 1f));
        _previousEngineAudioThrottle = throttle;
        _hasEngineAudioFrameHistory = true;

        if (_throttleTransientEnvelope <= 0.0005f)
        {
            _throttleTransientEnvelope = 0f;
        }

        return _throttleTransientEnvelope;
    }

    public void Dispose()
    {
        DisposeLoops();
    }

    private bool HasEngineAudio => _engineLoop is not null ||
                                   _engineSimulatorSound is not null ||
                                   _normalEngineSampleLoops.Length > 0 ||
                                   _highRpmEngineSampleLoops.Length > 0;

    private bool HasHighRpmAudio => _highRpmLoop is not null ||
                                    _highRpmEngineSampleLoops.Length > 0 ||
                                    _engineSimulatorSound is not null;

    private float CalculateTargetHighRpmBlend(VehicleState vehicle, float rpm, float dt)
    {
        if (!HasHighRpmAudio)
        {
            _highRpmAudioReleaseSeconds = 0f;
            return 0f;
        }

        float activationGate = CalculateHighRpmActivationGate(vehicle);
        if (_engineSampleLoops.Length == 0)
        {
            return activationGate * SmoothStep(
                _parameters.HighRpmBlendInRpm,
                _parameters.HighRpmBlendInRpm + MathF.Max(1f, _parameters.HighRpmBlendWidthRpm),
                rpm);
        }

        float entryRpm = _parameters.HighRpmBlendInRpm;
        float releaseGapRpm = MathHelper.Clamp(
            MathF.Max(420f, _parameters.HighRpmBlendWidthRpm * 3f),
            420f,
            650f);
        float exitRpm = entryRpm - releaseGapRpm;
        if (_highRpmAudioLatched)
        {
            if (activationGate <= 0.2f || rpm <= exitRpm)
            {
                _highRpmAudioReleaseSeconds += MathHelper.Clamp(dt, 0f, 1f / 20f);
                if (_highRpmAudioReleaseSeconds >= 0.08f)
                {
                    _highRpmAudioLatched = false;
                    _highRpmAudioReleaseSeconds = 0f;
                    AudioDiagnostics.Log("vtec-audio-latch", $"released at {rpm:0} rpm, exit {exitRpm:0}, gate {activationGate:0.00}");
                }
            }
            else
            {
                _highRpmAudioReleaseSeconds = 0f;
            }
        }
        else if (activationGate > 0.92f && rpm >= entryRpm)
        {
            _highRpmAudioLatched = true;
            _highRpmAudioReleaseSeconds = 0f;
            AudioDiagnostics.Log("vtec-audio-latch", $"engaged at {rpm:0} rpm, entry {entryRpm:0}, exit {exitRpm:0}, gate {activationGate:0.00}");
        }

        return _highRpmAudioLatched ? activationGate : 0f;
    }

    private float CalculateHighRpmActivationGate(VehicleState vehicle)
    {
        float throttle = MathF.Max(vehicle.Throttle, vehicle.EffectiveThrottle);
        float throttleGate = _parameters.HighRpmMinimumThrottle <= 0f
            ? 1f
            : SmoothStep(_parameters.HighRpmMinimumThrottle * 0.72f, _parameters.HighRpmMinimumThrottle, throttle);
        float speed = MathF.Abs(vehicle.SpeedMetersPerSecond);
        float speedGate = _parameters.HighRpmMinimumSpeedMetersPerSecond <= 0f
            ? 1f
            : SmoothStep(_parameters.HighRpmMinimumSpeedMetersPerSecond * 0.72f, _parameters.HighRpmMinimumSpeedMetersPerSecond, speed);

        return MathHelper.Clamp(throttleGate * speedGate, 0f, 1f);
    }

    private void UpdateEngineSampleBank(VehicleState vehicle, float rpm, float driveVolume, float dt)
    {
        bool hasNormalBank = _normalEngineSampleLoops.Length > 0;
        bool hasHighRpmBank = _highRpmEngineSampleLoops.Length > 0;
        float normalBankScale = hasNormalBank && hasHighRpmBank ? 1f - _highRpmBlend : hasNormalBank ? 1f : 0f;
        float highRpmBankScale = hasNormalBank && hasHighRpmBank ? _highRpmBlend : hasHighRpmBank ? 1f : 0f;
        float limiterTargetIntensity = CalculateLimiterSampleIntensity(vehicle);
        float limiterIntensity = UpdateLimiterSampleEnvelope(limiterTargetIntensity, driveVolume, dt);
        float limiterDuck = 1f - limiterIntensity * 0.42f;

        UpdateEngineSampleBank(_normalEngineSampleLoops, rpm, driveVolume * normalBankScale * limiterDuck, dt);
        UpdateEngineSampleBank(_highRpmEngineSampleLoops, rpm, driveVolume * highRpmBankScale * limiterDuck, dt);
        UpdateLimiterSample(vehicle, rpm, driveVolume, limiterTargetIntensity, limiterIntensity, dt);
    }

    private void UpdateEngineSampleBank(EngineSampleLoop[] bank, float rpm, float bankVolume, float dt)
    {
        foreach (EngineSampleLoop sample in bank)
        {
            float weight = CalculateBankSampleWeight(bank, sample, rpm);
            UpdateEngineSampleLoop(sample, rpm, bankVolume * weight * sample.LoudnessTrim, dt);
        }
    }

    private void UpdateLimiterSample(
        VehicleState vehicle,
        float rpm,
        float driveVolume,
        float limiterTargetIntensity,
        float limiterIntensity,
        float dt)
    {
        if (_limiterEngineSampleLoop is null)
        {
            return;
        }

        if (driveVolume <= 0.006f)
        {
            _smoothedLimiterSampleIntensity = 0f;
            _limiterEngineSampleLoop.Loop.Stop();
            return;
        }

        float limiterRpm = MathF.Max(rpm, vehicle.RedlineRpm > 0f ? vehicle.RedlineRpm * 0.98f : rpm);
        float audibleIntensity = limiterIntensity <= 0.0005f && limiterTargetIntensity <= 0.0005f
            ? 0f
            : limiterIntensity;
        UpdateEngineSampleLoop(
            _limiterEngineSampleLoop,
            limiterRpm,
            driveVolume * audibleIntensity * _limiterEngineSampleLoop.LoudnessTrim,
            dt);
    }

    private float UpdateLimiterSampleEnvelope(float targetIntensity, float driveVolume, float dt)
    {
        if (driveVolume <= 0.006f)
        {
            _smoothedLimiterSampleIntensity = 0f;
            return 0f;
        }

        float clampedDt = MathHelper.Clamp(dt, 0f, 1f / 20f);
        float responseRate = targetIntensity > _smoothedLimiterSampleIntensity ? 58f : 3.4f;
        float blend = 1f - MathF.Exp(-responseRate * clampedDt);
        _smoothedLimiterSampleIntensity = MathHelper.Lerp(
            _smoothedLimiterSampleIntensity,
            MathHelper.Clamp(targetIntensity, 0f, 1f),
            MathHelper.Clamp(blend, 0f, 1f));
        return _smoothedLimiterSampleIntensity;
    }

    private float CalculateBankSampleWeight(EngineSampleLoop[] bank, EngineSampleLoop sample, float rpm)
    {
        if (bank.Length == 0)
        {
            return 0f;
        }

        if (bank.Length == 1)
        {
            return ReferenceEquals(sample, bank[0]) ? 1f : 0f;
        }

        int sampleIndex = Array.IndexOf(bank, sample);
        if (sampleIndex < 0)
        {
            return 0f;
        }

        float weight = 1f;
        if (sampleIndex > 0)
        {
            EngineSampleLoop previous = bank[sampleIndex - 1];
            float boundaryRpm = CalculateSampleBoundaryRpm(previous, sample);
            float halfWidth = CalculateSampleCrossfadeHalfWidth(previous, sample);
            weight *= SmoothStep(boundaryRpm - halfWidth, boundaryRpm + halfWidth, rpm);
        }

        if (sampleIndex < bank.Length - 1)
        {
            EngineSampleLoop next = bank[sampleIndex + 1];
            float boundaryRpm = CalculateSampleBoundaryRpm(sample, next);
            float halfWidth = CalculateSampleCrossfadeHalfWidth(sample, next);
            weight *= 1f - SmoothStep(boundaryRpm - halfWidth, boundaryRpm + halfWidth, rpm);
        }

        return MathHelper.Clamp(weight, 0f, 1f);
    }

    private void UpdateEngineSampleLoop(EngineSampleLoop sample, float rpm, float targetVolume, float dt)
    {
        float volume = SmoothEngineSampleVolume(sample, targetVolume, dt);
        sample.Loop.Update(CalculateEnginePlaybackRatio(rpm, sample.Rpm), volume);
    }

    private static float SmoothEngineSampleVolume(EngineSampleLoop sample, float targetVolume, float dt)
    {
        float clampedTarget = MathHelper.Clamp(targetVolume, 0f, 1f);
        float clampedDt = MathHelper.Clamp(dt, 0f, 1f / 20f);
        LogTargetDropIfNeeded(sample, clampedTarget);
        float attackRate = sample.Limiter ? 72f : 42f;
        float releaseRate = sample.Limiter ? 24f : 24f;
        float responseRate = clampedTarget > sample.SmoothedVolume ? attackRate : releaseRate;
        float blend = 1f - MathF.Exp(-responseRate * clampedDt);
        sample.SmoothedVolume = MathHelper.Lerp(
            sample.SmoothedVolume,
            clampedTarget,
            MathHelper.Clamp(blend, 0f, 1f));
        if (sample.SmoothedVolume <= 0.0001f && clampedTarget <= 0.0001f)
        {
            sample.SmoothedVolume = 0f;
        }

        return sample.SmoothedVolume;
    }

    private static void LogTargetDropIfNeeded(EngineSampleLoop sample, float targetVolume)
    {
        if (sample.PreviousTargetVolume <= 0.08f || targetVolume > 0.003f)
        {
            sample.PreviousTargetVolume = targetVolume;
            return;
        }

        double now = AudioDiagnostics.NowSeconds;
        if (now - sample.LastTargetDropLogSeconds >= 0.35)
        {
            sample.LastTargetDropLogSeconds = now;
            AudioDiagnostics.Log(
                "sample-target-drop",
                $"{sample.Label}: target {sample.PreviousTargetVolume:0.000}->0.000, smoothed {sample.SmoothedVolume:0.000}");
        }

        sample.PreviousTargetVolume = targetVolume;
    }

    private float CalculateSampleCrossfadeHalfWidth(EngineSampleLoop lower, EngineSampleLoop upper)
    {
        float sampleGap = MathF.Abs(upper.Rpm - lower.Rpm);
        float configuredWidth = MathF.Max(8f, _parameters.EngineSampleCrossfadeWidthRpm);
        return MathF.Min(configuredWidth, sampleGap * 0.45f) * 0.5f;
    }

    private float CalculateSampleBoundaryRpm(EngineSampleLoop lower, EngineSampleLoop upper)
    {
        if (lower.Rpm <= 1200f && upper.Rpm >= 1800f)
        {
            return MathHelper.Clamp(_parameters.EngineIdleBlendOutRpm, lower.Rpm + 80f, upper.Rpm - 80f);
        }

        return (lower.Rpm + upper.Rpm) * 0.5f;
    }

    private static float CalculateLimiterSampleIntensity(VehicleState vehicle)
    {
        float source = MathF.Max(
            vehicle.RevLimiterBounceIntensity,
            vehicle.RevLimiterActive ? 0.45f : 0f);
        return SmoothStep(0.04f, 0.85f, source);
    }

    private void UpdateTyreScreechLoops(VehicleState vehicle, float pauseScale, float dt)
    {
        if (_tyreSpinLoop is null && _tyreChirpLoop is null && _controlLossScreechLoop is null)
        {
            return;
        }

        float surfaceGate = CalculateTyreScreechSurfaceGate(vehicle);
        if (surfaceGate <= 0.001f)
        {
            StoreSwayScreechHistory(vehicle);
            UpdateTyreSpinLoop(0f, 0.15f, 0f, vehicle.SpeedMetersPerSecond, dt);
            UpdateTyreChirpLoop(0f, 0f, vehicle.SpeedMetersPerSecond, pauseScale, dt);
            UpdateControlLossScreechLoop(0f, 0.15f, vehicle.SpeedMetersPerSecond, dt);
            return;
        }

        float brakeSpeedGate = SmoothStep(0.35f, 1.25f, vehicle.SpeedMetersPerSecond);
        float rollingBrakeGate = MathHelper.Lerp(0.35f, 1f, SmoothStep(1.0f, 9.0f, vehicle.SpeedMetersPerSecond));
        float brakeGate = SmoothStep(0.08f, 0.85f, vehicle.Brake);
        float hardBrakeGate = SmoothStep(0.62f, 0.96f, vehicle.Brake);
        float lockedWheels = MathHelper.Clamp(vehicle.LockedWheelCount / 4f, 0f, 1f);
        float slipRatio = SmoothStep(0.24f, 0.90f, vehicle.AverageSlipRatio);
        float driveSlipRatio = CalculateDriveSlipRatio(vehicle);
        float drivenSlipMagnitude = CalculateDrivenSlipMagnitude(vehicle);
        float wheelSpinSlip = SmoothStep(0.08f, 0.56f, driveSlipRatio);
        float drivenGripUsage = CalculateDrivenGripUsage(vehicle);
        float tractionChirpGate = CalculateTractionChirpGate(driveSlipRatio, drivenSlipMagnitude, drivenGripUsage);
        float slipAngle = SmoothStep(12f, 34f, vehicle.AverageSlipAngleDegrees);
        float peakGripUsage = MathF.Max(
            MathF.Max(vehicle.FrontLeftGripUsage, vehicle.FrontRightGripUsage),
            MathF.Max(vehicle.RearLeftGripUsage, vehicle.RearRightGripUsage));
        float lateralGripGate = SmoothStep(0.88f, 1.10f, peakGripUsage);
        float hardTurnSpeedGate = SmoothStep(5f, 18f, vehicle.SpeedMetersPerSecond);
        float swayScreechIntensity = CalculateSwayScreechIntensity(vehicle, lateralGripGate, pauseScale, dt);
        float driveDirection = vehicle.Gear < 0 ? -1f : 1f;
        float driveForceGate = SmoothStep(220f, 1850f, MathF.Max(0f, vehicle.DriveForce * driveDirection));
        float throttleGate = SmoothStep(0.14f, 0.72f, MathF.Max(vehicle.Throttle, vehicle.EffectiveThrottle));
        float brakeSuppression = 1f - SmoothStep(0.05f, 0.30f, vehicle.Brake);
        float spinSpeedGate = MathHelper.Lerp(0.72f, 1f, SmoothStep(0.2f, 9.0f, vehicle.SpeedMetersPerSecond));

        float brakeLockIntensity = MathF.Max(lockedWheels, slipRatio * 0.85f) * brakeGate;
        float hardBrakeIntensity = hardBrakeGate * rollingBrakeGate * 0.38f;
        float hardTurnIntensity = slipAngle * lateralGripGate * hardTurnSpeedGate;
        float rawWheelSpinIntensity = wheelSpinSlip * throttleGate * driveForceGate * brakeSuppression * spinSpeedGate;
        float wheelSpinIntensity = rawWheelSpinIntensity * surfaceGate;
        float wheelSpinChirpSource = ShouldSuppressCurrentShiftWheelSpinChirp(vehicle)
            ? 0f
            : rawWheelSpinIntensity * tractionChirpGate;
        float shiftChirpSource = CalculateShiftChirpSource(
            vehicle,
            throttleGate,
            brakeSuppression,
            spinSpeedGate,
            driveSlipRatio,
            drivenSlipMagnitude,
            drivenGripUsage);
        float tyreChirpSource = MathF.Max(
            wheelSpinChirpSource,
            shiftChirpSource) * surfaceGate;
        float controlLossScreechIntensity = MathHelper.Clamp(
            MathF.Max(MathF.Max(MathF.Max(brakeLockIntensity, hardBrakeIntensity), hardTurnIntensity), swayScreechIntensity) * brakeSpeedGate * pauseScale * surfaceGate,
            0f,
            1f);
        float tyreSpinIntensity = MathHelper.Clamp(wheelSpinIntensity * pauseScale, 0f, 1f);
        float roughness = MathHelper.Clamp(lockedWheels * 0.65f + MathF.Max(slipRatio, wheelSpinSlip) * 0.25f + slipAngle * 0.35f, 0.15f, 1f);
        UpdateTyreSpinLoop(tyreSpinIntensity, roughness, driveSlipRatio, vehicle.SpeedMetersPerSecond, dt);
        UpdateTyreChirpLoop(tyreChirpSource, driveSlipRatio, vehicle.SpeedMetersPerSecond, pauseScale, dt);
        UpdateControlLossScreechLoop(controlLossScreechIntensity, roughness, vehicle.SpeedMetersPerSecond, dt);
    }

    private static float CalculateTyreScreechSurfaceGate(VehicleState vehicle)
    {
        int grassWheels = 0;
        if (IsGrassSurface(vehicle.FrontLeftSurfaceName)) grassWheels++;
        if (IsGrassSurface(vehicle.FrontRightSurfaceName)) grassWheels++;
        if (IsGrassSurface(vehicle.RearLeftSurfaceName)) grassWheels++;
        if (IsGrassSurface(vehicle.RearRightSurfaceName)) grassWheels++;

        return grassWheels switch
        {
            0 => 1f,
            1 => 0.25f,
            _ => 0f
        };
    }

    private static float CalculateShiftChirpSource(
        VehicleState vehicle,
        float throttleGate,
        float brakeSuppression,
        float spinSpeedGate,
        float driveSlipRatio,
        float drivenSlipMagnitude,
        float drivenGripUsage)
    {
        float driveTractionGate = CalculateTractionChirpGate(driveSlipRatio, drivenSlipMagnitude, drivenGripUsage);
        bool firstToSecondUpshift = vehicle.LastCompletedShiftFromGear == 1 &&
                                    vehicle.LastCompletedShiftToGear == 2;
        float oneTwoChirp = firstToSecondUpshift
            ? vehicle.ShiftKickIntensity * throttleGate * brakeSuppression * spinSpeedGate * driveTractionGate
            : 0f;

        bool harshDownshift = vehicle.LastCompletedShiftFromGear > 1 &&
                              vehicle.LastCompletedShiftToGear > 0 &&
                              vehicle.LastCompletedShiftToGear < vehicle.LastCompletedShiftFromGear;
        float downshiftShock = MathF.Max(
            vehicle.MechanicalOverRevSeverity,
            vehicle.PowertrainShockIntensity * 0.72f);
        float downshiftTractionGate = MathF.Max(
            SmoothStep(0.08f, 0.24f, drivenSlipMagnitude),
            SmoothStep(1.04f, 1.26f, drivenGripUsage));
        float harshDownshiftChirp = harshDownshift
            ? SmoothStep(0.30f, 0.78f, downshiftShock) *
              downshiftTractionGate *
              SmoothStep(5f, 16f, vehicle.SpeedMetersPerSecond) *
              MathHelper.Lerp(0.64f, 1f, brakeSuppression)
            : 0f;

        return MathHelper.Clamp(MathF.Max(oneTwoChirp, harshDownshiftChirp), 0f, 1f);
    }

    private static bool ShouldSuppressCurrentShiftWheelSpinChirp(VehicleState vehicle)
    {
        bool upshift = vehicle.LastCompletedShiftFromGear > 0 &&
                       vehicle.LastCompletedShiftToGear > vehicle.LastCompletedShiftFromGear;
        bool firstToSecondUpshift = vehicle.LastCompletedShiftFromGear == 1 &&
                                    vehicle.LastCompletedShiftToGear == 2;
        return upshift && !firstToSecondUpshift && vehicle.ShiftKickIntensity > 0.02f;
    }

    private static bool IsGrassSurface(string surfaceName)
    {
        return !string.IsNullOrWhiteSpace(surfaceName) &&
               surfaceName.Contains("GRASS", StringComparison.OrdinalIgnoreCase);
    }

    private static float CalculateTractionChirpGate(float driveSlipRatio, float drivenSlipMagnitude, float drivenGripUsage)
    {
        float slipGate = MathF.Max(
            SmoothStep(0.10f, 0.30f, driveSlipRatio),
            SmoothStep(0.12f, 0.34f, drivenSlipMagnitude));
        float gripGate = SmoothStep(1.02f, 1.24f, drivenGripUsage);
        return MathHelper.Clamp(MathF.Max(slipGate, gripGate * 0.82f), 0f, 1f);
    }

    private void UpdateTyreSpinLoop(float targetIntensity, float roughness, float driveSlipRatio, float speedMetersPerSecond, float dt)
    {
        if (_tyreSpinLoop is null)
        {
            _smoothedTyreSpinIntensity = 0f;
            return;
        }

        float clampedDt = MathHelper.Clamp(dt, 0f, 1f / 20f);
        float responseRate = targetIntensity > _smoothedTyreSpinIntensity ? 24f : 11f;
        float blend = 1f - MathF.Exp(-responseRate * clampedDt);
        _smoothedTyreSpinIntensity = MathHelper.Lerp(
            _smoothedTyreSpinIntensity,
            MathHelper.Clamp(targetIntensity, 0f, 1f),
            MathHelper.Clamp(blend, 0f, 1f));

        if (_smoothedTyreSpinIntensity <= 0.006f && targetIntensity <= 0.006f)
        {
            _smoothedTyreSpinIntensity = 0f;
            _tyreSpinLoop.Stop();
            return;
        }

        float audibleIntensity = SmoothStep(0.04f, 0.88f, _smoothedTyreSpinIntensity);
        float slipPitch = MathHelper.Lerp(0.88f, 1.23f, SmoothStep(0.08f, 0.92f, driveSlipRatio));
        float speedPitch = MathHelper.Lerp(0.94f, 1.08f, SmoothStep(0.4f, 24f, speedMetersPerSecond));
        float roughnessPitch = MathHelper.Lerp(0.95f, 1.08f, roughness);
        float volume = audibleIntensity;
        _tyreSpinLoop.Update(slipPitch * speedPitch * roughnessPitch, volume);
    }

    private void UpdateTyreChirpLoop(float chirpSource, float driveSlipRatio, float speedMetersPerSecond, float pauseScale, float dt)
    {
        if (_tyreChirpLoop is null)
        {
            _tyreChirpEnvelope = 0f;
            _previousTyreChirpSource = chirpSource;
            return;
        }

        float clampedDt = MathHelper.Clamp(dt, 0f, 1f / 20f);
        if (pauseScale <= 0f)
        {
            _tyreChirpEnvelope = 0f;
            _previousTyreChirpSource = 0f;
            _tyreChirpLoop.Stop();
            return;
        }

        float rise = MathF.Max(0f, chirpSource - _previousTyreChirpSource);
        float slipGate = MathF.Max(SmoothStep(0.05f, 0.25f, driveSlipRatio), SmoothStep(0.24f, 0.60f, chirpSource));
        float trigger = SmoothStep(0.08f, 0.34f, rise) * slipGate * pauseScale;
        _tyreChirpEnvelope = MathF.Max(_tyreChirpEnvelope, trigger);
        _tyreChirpEnvelope *= MathF.Exp(-12.5f * clampedDt);
        _previousTyreChirpSource = chirpSource;

        if (_tyreChirpEnvelope <= 0.006f)
        {
            _tyreChirpEnvelope = 0f;
            _tyreChirpLoop.Stop();
            return;
        }

        float slipPitch = MathHelper.Lerp(1.02f, 1.30f, SmoothStep(0.05f, 0.75f, driveSlipRatio));
        float speedPitch = MathHelper.Lerp(0.96f, 1.08f, SmoothStep(0.5f, 18f, speedMetersPerSecond));
        _tyreChirpLoop.Update(slipPitch * speedPitch, _tyreChirpEnvelope);
    }

    private void UpdateControlLossScreechLoop(float targetIntensity, float roughness, float speedMetersPerSecond, float dt)
    {
        if (_controlLossScreechLoop is null)
        {
            _smoothedControlLossScreechIntensity = 0f;
            return;
        }

        float clampedDt = MathHelper.Clamp(dt, 0f, 1f / 20f);
        float responseRate = targetIntensity > _smoothedControlLossScreechIntensity ? 18f : 9f;
        float blend = 1f - MathF.Exp(-responseRate * clampedDt);
        _smoothedControlLossScreechIntensity = MathHelper.Lerp(
            _smoothedControlLossScreechIntensity,
            MathHelper.Clamp(targetIntensity, 0f, 1f),
            MathHelper.Clamp(blend, 0f, 1f));

        if (_smoothedControlLossScreechIntensity <= 0.006f && targetIntensity <= 0.006f)
        {
            _smoothedControlLossScreechIntensity = 0f;
            _controlLossScreechLoop.Stop();
            return;
        }

        float audibleIntensity = SmoothStep(0.05f, 0.92f, _smoothedControlLossScreechIntensity);
        float speedPitch = MathHelper.Lerp(0.96f, 1.07f, SmoothStep(6f, 42f, speedMetersPerSecond));
        float roughnessPitch = MathHelper.Lerp(0.88f, 1.16f, roughness);
        float volume = audibleIntensity;
        _controlLossScreechLoop.Update(speedPitch * roughnessPitch, volume);
    }

    private float CalculateSwayScreechIntensity(VehicleState vehicle, float lateralGripGate, float pauseScale, float dt)
    {
        if (pauseScale <= 0f)
        {
            StoreSwayScreechHistory(vehicle);
            return 0f;
        }

        float clampedDt = MathHelper.Clamp(dt, 0.001f, 1f / 20f);
        if (!_hasSwayScreechHistory)
        {
            StoreSwayScreechHistory(vehicle);
            return 0f;
        }

        float speedGate = SmoothStep(10f, 28f, vehicle.SpeedMetersPerSecond);
        float steerMagnitude = MathF.Abs(vehicle.Steer);
        float steerActivity = SmoothStep(0.18f, 0.72f, steerMagnitude);
        float steerRate = MathF.Abs(vehicle.Steer - _previousSwaySteerInput) / clampedDt;
        float steerRateGate = SmoothStep(1.6f, 6.5f, steerRate);
        bool steeringReversed = vehicle.Steer * _previousSwaySteerInput < -0.025f;
        float reversalGate = steeringReversed
            ? SmoothStep(0.08f, 0.55f, MathF.Min(steerMagnitude, MathF.Abs(_previousSwaySteerInput)))
            : 0f;

        float lateralG = MathF.Abs(vehicle.LateralAcceleration) / 9.81f;
        float lateralLoadGate = SmoothStep(0.22f, 0.82f, lateralG);
        float lateralJerkG = MathF.Abs(vehicle.LateralAcceleration - _previousSwayLateralAcceleration) / clampedDt / 9.81f;
        float lateralJerkGate = SmoothStep(0.7f, 3.4f, lateralJerkG);
        bool lateralLoadReversed = vehicle.LateralAcceleration * _previousSwayLateralAcceleration < 0f &&
                                   MathF.Max(MathF.Abs(vehicle.LateralAcceleration), MathF.Abs(_previousSwayLateralAcceleration)) > 1.2f;
        float lateralReversalGate = lateralLoadReversed
            ? SmoothStep(0.20f, 0.74f, lateralG)
            : 0f;

        float bodyRollRate = MathF.Abs(vehicle.BodyRollRadians - _previousSwayBodyRollRadians) / clampedDt;
        float bodyRollRateGate = SmoothStep(0.035f, 0.22f, bodyRollRate);
        float bodyRollLoadGate = SmoothStep(MathHelper.ToRadians(0.6f), MathHelper.ToRadians(3.2f), MathF.Abs(vehicle.BodyRollRadians));

        float softSwayScrub = steerActivity *
                              MathF.Max(lateralLoadGate * lateralGripGate * 0.28f, bodyRollLoadGate * 0.18f);
        float reversalScreech = MathF.Max(reversalGate, lateralReversalGate) *
                                MathF.Max(lateralLoadGate, lateralJerkGate) *
                                0.62f;
        float rollSwayScrub = steerActivity *
                              bodyRollRateGate *
                              MathF.Max(lateralLoadGate, bodyRollLoadGate) *
                              0.34f;
        float steeringRateScrub = steerRateGate *
                                  steerActivity *
                                  MathF.Max(lateralJerkGate, lateralLoadGate * 0.55f) *
                                  0.30f;

        StoreSwayScreechHistory(vehicle);
        return MathHelper.Clamp(
            MathF.Max(MathF.Max(softSwayScrub, reversalScreech), MathF.Max(rollSwayScrub, steeringRateScrub)) * speedGate,
            0f,
            0.72f);
    }

    private void StoreSwayScreechHistory(VehicleState vehicle)
    {
        _previousSwaySteerInput = vehicle.Steer;
        _previousSwayLateralAcceleration = vehicle.LateralAcceleration;
        _previousSwayBodyRollRadians = vehicle.BodyRollRadians;
        _hasSwayScreechHistory = true;
    }

    private void ResetSwayScreechHistory()
    {
        _previousSwaySteerInput = 0f;
        _previousSwayLateralAcceleration = 0f;
        _previousSwayBodyRollRadians = 0f;
        _hasSwayScreechHistory = false;
    }

    private static float CalculateDriveSlipRatio(VehicleState vehicle)
    {
        float driveDirection = vehicle.Gear < 0 ? -1f : 1f;
        float slipTotal = 0f;
        int slipCount = 0;

        AccumulateDriveSlip(vehicle.FrontLeftSlipRatio, vehicle.FrontLeftLongitudinalForceN);
        AccumulateDriveSlip(vehicle.FrontRightSlipRatio, vehicle.FrontRightLongitudinalForceN);
        AccumulateDriveSlip(vehicle.RearLeftSlipRatio, vehicle.RearLeftLongitudinalForceN);
        AccumulateDriveSlip(vehicle.RearRightSlipRatio, vehicle.RearRightLongitudinalForceN);

        if (slipCount > 0)
        {
            return slipTotal / slipCount;
        }

        return MathF.Max(0f, vehicle.AverageSlipRatio) * SmoothStep(0.15f, 0.72f, MathF.Max(vehicle.Throttle, vehicle.EffectiveThrottle));

        void AccumulateDriveSlip(float slipRatio, float longitudinalForceN)
        {
            float driveSlip = slipRatio * driveDirection;
            if (driveSlip <= 0f || longitudinalForceN * driveDirection <= 80f)
            {
                return;
            }

            slipTotal += driveSlip;
            slipCount++;
        }
    }

    private static float CalculateDrivenSlipMagnitude(VehicleState vehicle)
    {
        float slipTotal = 0f;
        int slipCount = 0;

        AccumulateSlip(vehicle.FrontLeftSlipRatio, vehicle.FrontLeftLongitudinalForceN);
        AccumulateSlip(vehicle.FrontRightSlipRatio, vehicle.FrontRightLongitudinalForceN);
        AccumulateSlip(vehicle.RearLeftSlipRatio, vehicle.RearLeftLongitudinalForceN);
        AccumulateSlip(vehicle.RearRightSlipRatio, vehicle.RearRightLongitudinalForceN);

        if (slipCount > 0)
        {
            return slipTotal / slipCount;
        }

        return MathF.Max(0f, vehicle.AverageSlipRatio) *
               SmoothStep(0.22f, 0.82f, MathF.Max(vehicle.Throttle, vehicle.EffectiveThrottle));

        void AccumulateSlip(float slipRatio, float longitudinalForceN)
        {
            if (MathF.Abs(longitudinalForceN) <= 80f)
            {
                return;
            }

            slipTotal += MathF.Abs(slipRatio);
            slipCount++;
        }
    }

    private static float CalculateDrivenGripUsage(VehicleState vehicle)
    {
        float gripUsage = 0f;
        bool hasDrivenForce = false;

        AccumulateGrip(vehicle.FrontLeftGripUsage, vehicle.FrontLeftLongitudinalForceN);
        AccumulateGrip(vehicle.FrontRightGripUsage, vehicle.FrontRightLongitudinalForceN);
        AccumulateGrip(vehicle.RearLeftGripUsage, vehicle.RearLeftLongitudinalForceN);
        AccumulateGrip(vehicle.RearRightGripUsage, vehicle.RearRightLongitudinalForceN);

        return hasDrivenForce
            ? gripUsage
            : MathF.Max(
                MathF.Max(vehicle.FrontLeftGripUsage, vehicle.FrontRightGripUsage),
                MathF.Max(vehicle.RearLeftGripUsage, vehicle.RearRightGripUsage));

        void AccumulateGrip(float wheelGripUsage, float longitudinalForceN)
        {
            if (MathF.Abs(longitudinalForceN) <= 80f)
            {
                return;
            }

            gripUsage = MathF.Max(gripUsage, wheelGripUsage);
            hasDrivenForce = true;
        }
    }

    private float CalculateEnginePlaybackRatio(float rpm)
    {
        return CalculateEnginePlaybackRatio(rpm, _parameters.BaseSampleRpm);
    }

    private float CalculateEnginePlaybackRatio(float rpm, float sampleRpm)
    {
        float baseRpm = MathF.Max(100f, sampleRpm);
        return MathHelper.Clamp(
            rpm / baseRpm,
            MathF.Max(0.05f, _parameters.MinimumPlaybackRatio),
            MathF.Max(_parameters.MinimumPlaybackRatio, _parameters.MaximumPlaybackRatio));
    }

    private static LoopingPitchedSound? LoadLoop(string path, string label = "")
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        WavLoopSource? source = LoadSource(path);
        return source is null ? null : new LoopingPitchedSound(source, string.IsNullOrWhiteSpace(label) ? path : label);
    }

    private void LoadEngineSampleBank(EngineAudioSampleParameters[] samples)
    {
        List<EngineSampleLoop> loadedSamples = [];
        try
        {
            foreach (EngineAudioSampleParameters sample in samples)
            {
                WavLoopSource? source = LoadSource(sample.Path);
                if (source is null)
                {
                    continue;
                }

                loadedSamples.Add(new EngineSampleLoop(sample, source, CalculateLoopRmsLevel(source)));
            }

            if (!loadedSamples.Any(sample => !sample.Limiter))
            {
                throw new InvalidDataException("Engine sample bank does not contain any regular engine samples.");
            }

            _engineSampleLoops = [.. loadedSamples.OrderBy(sample => sample.Limiter).ThenBy(sample => sample.Rpm)];
            _normalEngineSampleLoops = [.. _engineSampleLoops.Where(sample => !sample.HighRpm && !sample.Limiter).OrderBy(sample => sample.Rpm)];
            _highRpmEngineSampleLoops = [.. _engineSampleLoops.Where(sample => sample.HighRpm && !sample.Limiter).OrderBy(sample => sample.Rpm)];
            _limiterEngineSampleLoop = _engineSampleLoops.FirstOrDefault(sample => sample.Limiter);
            NormalizeEngineSampleLoudness();
            AudioDiagnostics.Log(
                "engine-sample-bank",
                $"loaded {_engineSampleLoops.Length} loops: {string.Join(", ", _engineSampleLoops.Select(sample => sample.Label))}");
        }
        catch
        {
            foreach (EngineSampleLoop sample in loadedSamples)
            {
                sample.Dispose();
            }

            _engineSampleLoops = [];
            _normalEngineSampleLoops = [];
            _highRpmEngineSampleLoops = [];
            _limiterEngineSampleLoop = null;
            throw;
        }
    }

    private void NormalizeEngineSampleLoudness()
    {
        float[] regularRmsLevels =
        [
            .. _engineSampleLoops
                .Where(sample => !sample.Limiter && sample.RmsLevel > 0.0001f)
                .Select(sample => sample.RmsLevel)
                .OrderBy(rms => rms)
        ];
        if (regularRmsLevels.Length == 0)
        {
            return;
        }

        float referenceRms = regularRmsLevels[regularRmsLevels.Length / 2];
        foreach (EngineSampleLoop sample in _engineSampleLoops)
        {
            if (sample.RmsLevel <= 0.0001f)
            {
                sample.LoudnessTrim = 1f;
                continue;
            }

            sample.LoudnessTrim = MathHelper.Clamp(referenceRms / sample.RmsLevel, 0.12f, 1f);
        }
    }

    private static WavLoopSource? LoadSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string resolvedPath = ResolveAssetPath(path);
        return WavLoopSource.Load(resolvedPath);
    }

    private static LoopingPitchedSound? TryLoadLoop(string path, string label = "")
    {
        try
        {
            return LoadLoop(path, label);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException)
        {
            AudioDiagnostics.Log("optional-audio-error", exception.ToString());
            Console.WriteLine($"Optional audio asset disabled: {exception.Message}");
            return null;
        }
    }

    private static LoopingPitchedSound? TryLoadSlicedLoop(string path, float startRatio, float endRatio, string label = "")
    {
        try
        {
            WavLoopSource? source = LoadSource(path);
            string resolvedLabel = string.IsNullOrWhiteSpace(label)
                ? $"{path} [{startRatio:0.00}-{endRatio:0.00}]"
                : $"{label}: {path} [{startRatio:0.00}-{endRatio:0.00}]";
            return source is null ? null : new LoopingPitchedSound(source.Slice(startRatio, endRatio), resolvedLabel);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException)
        {
            AudioDiagnostics.Log("optional-audio-error", exception.ToString());
            Console.WriteLine($"Optional audio asset disabled: {exception.Message}");
            return null;
        }
    }

    private static string ResolveAssetPath(string path)
    {
        if (Path.IsPathRooted(path) && File.Exists(path))
        {
            return path;
        }

        string[] candidates =
        [
            Path.Combine(Environment.CurrentDirectory, path),
            Path.Combine(AppContext.BaseDirectory, path)
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Audio asset was not found: {path}", path);
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float t = MathHelper.Clamp((value - edge0) / MathF.Max(0.001f, edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float CalculateHighRpmLoudnessTrim(WavLoopSource? engineSource, WavLoopSource? highRpmSource)
    {
        if (engineSource is null || highRpmSource is null)
        {
            return 1f;
        }

        float engineRms = CalculateLoopRmsLevel(engineSource);
        float highRpmRms = CalculateLoopRmsLevel(highRpmSource);
        if (engineRms <= 0.0001f || highRpmRms <= 0.0001f)
        {
            return 1f;
        }

        return MathHelper.Clamp(engineRms / highRpmRms, 0.15f, 1f);
    }

    private static float CalculateLoopRmsLevel(WavLoopSource source)
    {
        LoopWindow loopWindow = LoopWindowPlanner.Plan(source);
        int sampleCount = Math.Clamp(loopWindow.EndFrame, 1, source.FrameCount) * source.ChannelCount;
        double sumSquares = 0.0;
        for (int i = 0; i < sampleCount; i++)
        {
            float sample = source.Samples[i];
            sumSquares += sample * sample;
        }

        return (float)Math.Sqrt(sumSquares / sampleCount);
    }

    private sealed class EngineSampleLoop : IDisposable
    {
        public EngineSampleLoop(EngineAudioSampleParameters parameters, WavLoopSource source, float rmsLevel)
        {
            Label = $"{(parameters.Limiter ? "limiter" : parameters.HighRpm ? "high-rpm" : "engine")} {parameters.Path} @ {parameters.Rpm:0} rpm";
            Loop = new LoopingPitchedSound(source, Label);
            Rpm = MathF.Max(100f, parameters.Rpm);
            HighRpm = parameters.HighRpm;
            Limiter = parameters.Limiter;
            RmsLevel = rmsLevel;
        }

        public string Label { get; }

        public LoopingPitchedSound Loop { get; }

        public float Rpm { get; }

        public bool HighRpm { get; }

        public bool Limiter { get; }

        public float RmsLevel { get; }

        public float LoudnessTrim { get; set; } = 1f;

        public float SmoothedVolume { get; set; }

        public float PreviousTargetVolume { get; set; }

        public double LastTargetDropLogSeconds { get; set; } = -999.0;

        public void Dispose()
        {
            Loop.Dispose();
        }
    }

    private void DisposeLoops()
    {
        _engineLoop?.Dispose();
        _highRpmLoop?.Dispose();
        _engineSimulatorSound?.Dispose();
        foreach (EngineSampleLoop sample in _engineSampleLoops)
        {
            sample.Dispose();
        }

        _tyreSpinLoop?.Dispose();
        _tyreChirpLoop?.Dispose();
        _controlLossScreechLoop?.Dispose();
        _engineLoop = null;
        _highRpmLoop = null;
        _engineSimulatorSound = null;
        _engineSampleLoops = [];
        _normalEngineSampleLoops = [];
        _highRpmEngineSampleLoops = [];
        _limiterEngineSampleLoop = null;
        _tyreSpinLoop = null;
        _tyreChirpLoop = null;
        _controlLossScreechLoop = null;
    }
}
