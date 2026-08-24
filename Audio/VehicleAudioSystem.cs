using Microsoft.Xna.Framework;
using RType.Camera;
using RType.Vehicle;

namespace RType.Audio;

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
    private RaceEngineSampleSound? _raceEngineSound;
    private LoopingPitchedSound? _tyreSpinLoop;
    private LoopingPitchedSound? _tyreChirpLoop;
    private LoopingPitchedSound? _controlLossScreechLoop;
    private float _smoothedEngineRpm;
    private float _highRpmBlend;
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
    private RaceEngineAudioState _lastRaceEngineState;

    public void SetVehicle(VehicleAudioParameters parameters)
    {
        DisposeLoops();
        _parameters = parameters;
        _smoothedEngineRpm = 0f;
        _highRpmBlend = 0f;
        _smoothedTyreSpinIntensity = 0f;
        _tyreChirpEnvelope = 0f;
        _previousTyreChirpSource = 0f;
        _smoothedControlLossScreechIntensity = 0f;
        _previousEngineAudioThrottle = 0f;
        _throttleTransientEnvelope = 0f;
        _hasEngineAudioFrameHistory = false;
        _lastRaceEngineState = default;
        ResetSwayScreechHistory();
        _available = true;

        try
        {
            bool useRaceEngineSamples = parameters.EngineSampleVolume > 0.001f &&
                                        (parameters.EngineSamples.Length > 0 ||
                                         !string.IsNullOrWhiteSpace(parameters.EngineLoopPath) ||
                                         !string.IsNullOrWhiteSpace(parameters.HighRpmLoopPath));
            AudioDiagnostics.Log(
                "vehicle-audio",
                $"initializing vehicle audio, raceSamples={useRaceEngineSamples}, legacyProceduralEngine=False");

            _raceEngineSound = useRaceEngineSamples
                ? new RaceEngineSampleSound(parameters)
                : null;

            if (useRaceEngineSamples)
            {
                AudioDiagnostics.Log("engine-audio-mode", "Race sample engine runtime");
            }
            else
            {
                AudioDiagnostics.Log("engine-audio-mode", "Engine audio disabled; no race sample profile configured");
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
            float actualRpm = MathF.Max(300f, vehicle.Rpm);
            if (_smoothedEngineRpm <= 0f)
            {
                _smoothedEngineRpm = actualRpm;
            }
            else
            {
                float responseRate = vehicle.IsShifting
                    ? 26f
                    : vehicle.RevLimiterActive
                        ? 22f
                        : 18f;
                float rpmBlend = 1f - MathF.Exp(-responseRate * MathHelper.Clamp(dt, 0f, 1f / 20f));
                _smoothedEngineRpm = MathHelper.Lerp(_smoothedEngineRpm, actualRpm, MathHelper.Clamp(rpmBlend, 0f, 1f));
            }

            float pauseScale = paused ? 0f : 1f;
            float driveVolume = MathHelper.Clamp(_parameters.EngineVolume * pauseScale, 0f, 1f);
            float enginePowerUnitRpm = SelectEnginePowerUnitAudioRpm(vehicle, actualRpm);
            float targetHighBlend = CalculateTargetHighRpmBlend(
                vehicle,
                MathF.Max(300f, vehicle.EnginePowerUnitActive ? enginePowerUnitRpm : vehicle.Rpm));
            float highBlendRate = vehicle.IsShifting ? 72f : 42f;
            float highBlendStep = 1f - MathF.Exp(-highBlendRate * MathHelper.Clamp(dt, 0f, 1f / 20f));
            _highRpmBlend = MathHelper.Lerp(_highRpmBlend, targetHighBlend, MathHelper.Clamp(highBlendStep, 0f, 1f));

            float throttleTransient = UpdateEngineAudioThrottleTransient(vehicle, dt);
            EngineAudioFrame engineFrame = EngineAudioFrame.FromVehicleState(
                _parameters,
                vehicle,
                enginePowerUnitRpm,
                _highRpmBlend,
                driveVolume,
                cameraMode,
                paused,
                throttleTransient,
                dt);
            _raceEngineSound?.Update(engineFrame);
            MirrorRaceEngineState(vehicle);
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
        _raceEngineSound?.Stop();
        _tyreSpinLoop?.Stop();
        _tyreChirpLoop?.Stop();
        _controlLossScreechLoop?.Stop();
        _smoothedEngineRpm = 0f;
        _highRpmBlend = 0f;
        _smoothedTyreSpinIntensity = 0f;
        _tyreChirpEnvelope = 0f;
        _previousTyreChirpSource = 0f;
        _smoothedControlLossScreechIntensity = 0f;
        _previousEngineAudioThrottle = 0f;
        _throttleTransientEnvelope = 0f;
        _hasEngineAudioFrameHistory = false;
        ResetSwayScreechHistory();
    }

    public bool TryGetRaceEngineState(out RaceEngineAudioState state)
    {
        state = _lastRaceEngineState;
        return state.Active;
    }

    private void MirrorRaceEngineState(VehicleState vehicle)
    {
        if (_raceEngineSound is null)
        {
            vehicle.RTypeEngineActive = false;
            _lastRaceEngineState = default;
            return;
        }

        RaceEngineAudioState state = _raceEngineSound.State;
        _lastRaceEngineState = state;
        vehicle.RTypeEngineActive = state.Active;
        vehicle.RTypeEngineProfileId = state.ProfileId;
        vehicle.RTypeEngineRpm = state.Rpm;
        vehicle.RTypeEngineCrankPhaseDegrees = state.CrankPhaseDegrees;
        vehicle.RTypeEngineVtecBlend = state.VtecBlend;
        vehicle.RTypeEngineLimiterCut = state.LimiterCut;
        vehicle.RTypeEngineRevLimitTimerSeconds = state.RevLimitTimerSeconds;
        vehicle.RTypeEngineLastIgnitedCylinder = state.LastIgnitedCylinder;
        vehicle.RTypeEngineThrottle = state.LastThrottle;
        vehicle.RTypeEngineOutputPeak = state.LastOutputPeak;
        vehicle.RTypeEngineOutputRms = state.LastOutputRms;
    }

    private static float SelectEnginePowerUnitAudioRpm(VehicleState vehicle, float fallbackRpm)
    {
        float crankRpm = vehicle.EnginePowerUnitActive ? vehicle.EnginePowerUnitCrankRpm : 0f;
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

    private bool HasEngineAudio => _raceEngineSound is not null;

    private bool HasHighRpmAudio => _raceEngineSound is not null;

    private float CalculateTargetHighRpmBlend(VehicleState vehicle, float rpm)
    {
        if (!HasHighRpmAudio)
        {
            return 0f;
        }

        float activationGate = CalculateHighRpmActivationGate(vehicle);
        return activationGate * SmoothStep(
            _parameters.HighRpmBlendInRpm,
            _parameters.HighRpmBlendInRpm + MathF.Max(1f, _parameters.HighRpmBlendWidthRpm),
            rpm);
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

    private static WavLoopSource? LoadSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string resolvedPath = ResolveAssetPath(path);
        return WavLoopSource.Load(resolvedPath);
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

    private void DisposeLoops()
    {
        _raceEngineSound?.Dispose();

        _tyreSpinLoop?.Dispose();
        _tyreChirpLoop?.Dispose();
        _controlLossScreechLoop?.Dispose();
        _raceEngineSound = null;
        _tyreSpinLoop = null;
        _tyreChirpLoop = null;
        _controlLossScreechLoop = null;
    }
}
