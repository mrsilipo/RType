using Microsoft.Xna.Framework;
using RType.World;

namespace RType.Vehicle;

public sealed class SimpleVehicleSimulator : IVehicleSimulator
{
    private const float Gravity = 9.81f;

    private readonly ITrackSurfaceSampler _surfaceSampler;
    private readonly VehicleSimulationParameters _parameters;
    private readonly SimulationEngineParameters _engineParameters;
    private readonly IEnginePowerUnit _enginePowerUnit;
    private readonly WheelRuntimeState[] _wheels;
    private float _fixedTickAccumulatorSeconds;
    private VehicleInput _pendingFixedTickInput;
    private bool _hasPendingFixedTickInput;
    private bool _manualTransmission;
    private int _pendingGear;
    private int _pendingShiftFromGear;
    private float _shiftTimerSeconds;
    private float _shiftDurationSeconds;
    private float _shiftStartRpm;
    private float _shiftTargetRpm;
    private float _shiftRpmCeiling;
    private float _pendingShiftKickSeverity;
    private float _shiftKickSeconds;
    private float _shiftKickDurationSeconds;
    private float _shiftKickSeverity;
    private float _enginePowerShiftHandoffSmoothSeconds;
    private float _pendingDownshiftOverRevRpm;
    private float _pendingDownshiftOverRevSeverity;
    private float _downshiftOverRevBrakeSeconds;
    private float _downshiftOverRevBrakeDurationSeconds;
    private float _downshiftOverRevBrakeSeverity;
    private bool _revLimiterCutting;
    private float _revLimiterPhaseSeconds;
    private float _revLimiterChatterPhaseSeconds;
    private float _idleCrankPhaseDegrees;
    private float _filteredSteerInput;
    private float _filteredBrakeInput;
    private float _dynamicBodyPitchRadians;
    private float _dynamicBodyRollRadians;
    private readonly float[] _visualSuspensionCompressionMeters = new float[4];
    private readonly float[] _visualSuspensionVelocityMetersPerSecond = new float[4];
    private bool _digitalBrakeAssistActive;
    private float _recentBrakeSteeringBoostSeconds;
    private float _launchClutchEngagement;
    private float _launchClutchTimerSeconds;
    private bool _preRevLaunchActive;

    public SimpleVehicleSimulator(
        ITrackSurfaceSampler surfaceSampler,
        Vector3 startPosition,
        float startHeadingRadians,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters? engineParameters = null)
        : this(surfaceSampler, startPosition, startHeadingRadians, parameters, engineParameters, null)
    {
    }

    internal SimpleVehicleSimulator(
        ITrackSurfaceSampler surfaceSampler,
        Vector3 startPosition,
        float startHeadingRadians,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters? engineParameters,
        IEnginePowerUnit? enginePowerUnit = null)
    {
        _surfaceSampler = surfaceSampler;
        _parameters = parameters;
        _engineParameters = engineParameters ?? new SimulationEngineParameters();
        _enginePowerUnit = enginePowerUnit ?? EnginePowerUnitFactory.Create(parameters);
        _wheels = CreateWheels(parameters);
        State = new VehicleState
        {
            VehicleName = _parameters.DisplayName,
            RedlineRpm = _parameters.RedlineRpm,
            Position = startPosition,
            HeadingRadians = startHeadingRadians,
            Gear = 1,
            Rpm = _parameters.IdleRpm,
            PreviousPhysicsRpm = _parameters.IdleRpm,
            DisplayedRpm = _parameters.IdleRpm,
            DisplayedRpmTarget = _parameters.IdleRpm,
            WheelContactCenterHeightMeters = startPosition.Y,
            BodyPivotHeightMeters = MathHelper.Clamp(_parameters.CenterOfGravityHeightMeters, 0.34f, 0.78f)
        };
        State.FrontLeftSupportHeightMeters = startPosition.Y;
        State.FrontRightSupportHeightMeters = startPosition.Y;
        State.RearLeftSupportHeightMeters = startPosition.Y;
        State.RearRightSupportHeightMeters = startPosition.Y;
        InitializeStaticWheelState();
    }

    public VehicleState State { get; }

    public void SetManualTransmission(bool enabled)
    {
        _manualTransmission = enabled;
        State.IsManualTransmission = enabled;
    }

    public void ToggleTransmissionMode()
    {
        SetManualTransmission(!_manualTransmission);
    }

    public void Update(VehicleInput input, float dt)
    {
        AdvanceFixedTicks(input, dt, Step);
    }

    public void UpdateRaceStartHold(VehicleInput input, float dt)
    {
        AdvanceFixedTicks(input, dt, StepRaceStartHold);
    }

    private void AdvanceFixedTicks(VehicleInput input, float dt, Action<VehicleInput, float> tick)
    {
        PhysicsTimingParameters timing = _engineParameters.Timing;
        float fixedDelta = timing.FixedDeltaSeconds;
        float clampedDt = MathHelper.Clamp(dt, 0f, MathF.Max(fixedDelta, timing.MaximumFrameTimeSeconds));
        int frameWallContactCount = 0;
        bool frameCollisionActive = false;
        _pendingFixedTickInput = _hasPendingFixedTickInput
            ? MergePendingInput(_pendingFixedTickInput, input)
            : input;
        _hasPendingFixedTickInput = true;
        _fixedTickAccumulatorSeconds += clampedDt;

        if (MathF.Abs(clampedDt - fixedDelta) <= fixedDelta * 0.08f)
        {
            float previousRpm = State.Rpm;
            tick(_pendingFixedTickInput, fixedDelta);
            CaptureFrameCollisionState(ref frameWallContactCount, ref frameCollisionActive);
            State.PreviousPhysicsRpm = previousRpm;
            _fixedTickAccumulatorSeconds = 0f;
            _pendingFixedTickInput = ClearLatchedButtons(_pendingFixedTickInput);
            State.PhysicsTickAlpha = 0f;
            ApplyFrameCollisionState(frameWallContactCount, frameCollisionActive);
            return;
        }

        int completedTicks = 0;
        int maxTicks = Math.Max(1, timing.MaximumTicksPerUpdate);
        while (_fixedTickAccumulatorSeconds + 0.000001f >= fixedDelta && completedTicks < maxTicks)
        {
            float previousRpm = State.Rpm;
            tick(_pendingFixedTickInput, fixedDelta);
            CaptureFrameCollisionState(ref frameWallContactCount, ref frameCollisionActive);
            State.PreviousPhysicsRpm = previousRpm;
            _fixedTickAccumulatorSeconds -= fixedDelta;
            _pendingFixedTickInput = ClearLatchedButtons(_pendingFixedTickInput);
            completedTicks++;
        }

        State.PhysicsTickAlpha = MathHelper.Clamp(_fixedTickAccumulatorSeconds / fixedDelta, 0f, 1f);
        if (completedTicks > 0)
        {
            ApplyFrameCollisionState(frameWallContactCount, frameCollisionActive);
        }
        else
        {
            State.WallContactCount = 0;
            State.CollisionActive = State.CrashFlashSeconds > 0f;
        }
    }

    private void CaptureFrameCollisionState(ref int wallContactCount, ref bool collisionActive)
    {
        wallContactCount = Math.Max(wallContactCount, State.WallContactCount);
        collisionActive |= State.CollisionActive;
    }

    private void ApplyFrameCollisionState(int wallContactCount, bool collisionActive)
    {
        State.WallContactCount = Math.Max(State.WallContactCount, wallContactCount);
        State.CollisionActive |= collisionActive;
    }

    private static VehicleInput MergePendingInput(VehicleInput pending, VehicleInput latest)
    {
        return new VehicleInput(
            latest.Throttle,
            latest.Brake,
            latest.Steer,
            latest.Handbrake,
            latest.Reverse,
            pending.ShiftUpRequested || latest.ShiftUpRequested,
            pending.ShiftDownRequested || latest.ShiftDownRequested,
            latest.BrakeAssistEnabled,
            latest.ThrottleAssistEnabled);
    }

    private static VehicleInput ClearLatchedButtons(VehicleInput input)
    {
        return new VehicleInput(
            input.Throttle,
            input.Brake,
            input.Steer,
            input.Handbrake,
            input.Reverse,
            brakeAssistEnabled: input.BrakeAssistEnabled,
            throttleAssistEnabled: input.ThrottleAssistEnabled);
    }

    private void InitializeStaticWheelState()
    {
        Vector2 forward = GetForward();
        Vector2 right = GetRight();
        float[] normalLoads = CalculateNormalLoads(0f);
        SteeringAngles steeringAngles = CalculateSteeringAngles(0f, 0f, 0f);
        for (int i = 0; i < _wheels.Length; i++)
        {
            WheelRuntimeState wheel = _wheels[i];
            wheel.NormalLoadN = normalLoads[i];
            float commandedSteerAngle = wheel.Corner switch
            {
                WheelCorner.FrontLeft => steeringAngles.FrontLeft,
                WheelCorner.FrontRight => steeringAngles.FrontRight,
                _ => 0f
            };
            WheelAlignment alignment = CalculateWheelAlignment(wheel, commandedSteerAngle, normalLoads[i]);
            wheel.SteerAngleRadians = commandedSteerAngle + alignment.ToeRadians;
            wheel.EffectiveCamberRadians = alignment.CamberRadians;
            wheel.EffectiveToeRadians = alignment.ToeRadians;
            wheel.SuspensionCompressionMeters = alignment.CompressionMeters;
        }

        State.FrontLeftLoadN = normalLoads[(int)WheelCorner.FrontLeft];
        State.FrontRightLoadN = normalLoads[(int)WheelCorner.FrontRight];
        State.RearLeftLoadN = normalLoads[(int)WheelCorner.RearLeft];
        State.RearRightLoadN = normalLoads[(int)WheelCorner.RearRight];
        State.FrontLeftSteerAngleDegrees = MathHelper.ToDegrees(GetWheel(WheelCorner.FrontLeft).SteerAngleRadians);
        State.FrontRightSteerAngleDegrees = MathHelper.ToDegrees(GetWheel(WheelCorner.FrontRight).SteerAngleRadians);
        UpdateGroundContactPose(forward, right, 0f);
    }

    private void Step(VehicleInput input, float dt)
    {
        State.CrashFlashSeconds = MathF.Max(0f, State.CrashFlashSeconds - dt);

        Vector2 forward = GetForward();
        Vector2 right = GetRight();
        float forwardSpeed = Vector2.Dot(State.Velocity, forward);
        float lateralSpeed = Vector2.Dot(State.Velocity, right);

        UpdateShiftTimer(dt);
        UpdateGear(input, forwardSpeed);

        float throttle = State.Gear < 0 ? input.Reverse : input.Throttle;
        float driveThrottle = throttle;
        float steeringBrakeAuthority = CalculateSteeringBrakeAuthority(input.Brake, _filteredBrakeInput);
        UpdateSteeringInput(input.Steer, MathF.Abs(forwardSpeed), steeringBrakeAuthority, dt);
        if (input.ThrottleAssistEnabled && State.Gear > 0)
        {
            driveThrottle = ApplyDigitalThrottleAssist(throttle, forwardSpeed);
        }

        UpdateBrakeInput(input.Brake, dt);
        float brake = _filteredBrakeInput;
        brake = input.BrakeAssistEnabled
            ? ApplyDigitalBrakeAssist(brake, forwardSpeed)
            : brake;
        _digitalBrakeAssistActive = input.BrakeAssistEnabled && brake > 0.01f;
        UpdateRecentBrakeSteeringBoost(input.Brake, brake, dt);
        driveThrottle = ApplyBrakeThrottlePriority(driveThrottle, MathF.Max(brake, input.Brake));
        float[] normalLoads = CalculateNormalLoads(forwardSpeed);
        bool launchClutchActive = ShouldUseLaunchClutch(driveThrottle, forwardSpeed);
        float totalDriveTorque;
        if (launchClutchActive)
        {
            totalDriveTorque = CalculateLaunchClutchDriveTorque(driveThrottle, forwardSpeed, dt);
            UpdateRevLimiter(throttle, dt);
        }
        else
        {
            if (_launchClutchTimerSeconds > 0f && driveThrottle > 0.55f && State.Gear > 0)
            {
                ApplyLaunchClutchHandoffWheelSpin(driveThrottle);
            }

            _launchClutchEngagement = 0f;
            _launchClutchTimerSeconds = 0f;
            _preRevLaunchActive = false;
            UpdateRpm(input, forwardSpeed, dt);
            UpdateRevLimiter(throttle, dt);
            totalDriveTorque = CalculateTotalDriveTorque(driveThrottle, forwardSpeed, dt);
        }

        float[] driveTorques = DistributeDriveTorque(totalDriveTorque, normalLoads);
        SteeringAngles steeringAngles = CalculateSteeringAngles(
            _filteredSteerInput,
            MathF.Abs(forwardSpeed),
            CalculateSteeringBrakeAuthority(brake, input.Brake));
        float[] brakeTorques = CalculateBrakeTorques(brake, input.Handbrake, MathF.Abs(forwardSpeed), MathF.Abs(_filteredSteerInput));

        float totalForceX = 0f;
        float totalForceZ = 0f;
        float yawTorque = 0f;
        float slipRatioTotal = 0f;
        float slipAngleTotal = 0f;
        float gripUsageTotal = 0f;
        float drivenLongitudinalForce = 0f;
        float brakeLongitudinalForce = 0f;
        bool absActive = false;
        int lockedWheelCount = 0;
        string weakestSurface = "ROAD";
        float weakestGrip = 1f;
        float counterSteerRecoveryT = CalculateCounterSteerRecoveryT(_filteredSteerInput, forwardSpeed, lateralSpeed);

        for (int i = 0; i < _wheels.Length; i++)
        {
            WheelRuntimeState wheel = _wheels[i];
            wheel.NormalLoadN = normalLoads[i];
            float commandedSteerAngle = wheel.Corner switch
            {
                WheelCorner.FrontLeft => steeringAngles.FrontLeft,
                WheelCorner.FrontRight => steeringAngles.FrontRight,
                _ => 0f
            };
            WheelAlignment alignment = CalculateWheelAlignment(wheel, commandedSteerAngle, normalLoads[i]);
            wheel.SteerAngleRadians = commandedSteerAngle + alignment.ToeRadians;
            wheel.EffectiveCamberRadians = alignment.CamberRadians;
            wheel.EffectiveToeRadians = alignment.ToeRadians;
            wheel.SuspensionCompressionMeters = alignment.CompressionMeters;

            WheelForceResult force = CalculateWheelForce(
                wheel,
                driveTorques[i],
                brakeTorques[i],
                forward,
                right,
                forwardSpeed,
                lateralSpeed,
                counterSteerRecoveryT,
                dt);
            totalForceX += force.BodyForceX;
            totalForceZ += force.BodyForceZ;
            yawTorque += wheel.LocalZ * force.BodyForceX - wheel.LocalX * force.BodyForceZ;
            slipRatioTotal += MathF.Abs(force.SlipRatio);
            slipAngleTotal += MathF.Abs(force.SlipAngleRadians);
            gripUsageTotal += force.GripUsage;

            if (MathF.Abs(driveTorques[i]) > 0.01f)
            {
                drivenLongitudinalForce += MathF.Abs(force.LongitudinalForceN);
            }

            if (brakeTorques[i] > 0.01f)
            {
                brakeLongitudinalForce += MathF.Abs(force.LongitudinalForceN);
            }

            absActive |= wheel.AbsActive;
            if (wheel.IsLocked)
            {
                lockedWheelCount++;
            }

            if (force.SurfaceGrip < weakestGrip)
            {
                weakestGrip = force.SurfaceGrip;
                weakestSurface = force.SurfaceName;
            }
        }

        totalForceZ += CalculateAeroDrag(forwardSpeed);
        AddGradeForces(forward, ref totalForceZ);

        float averageSlipAngle = slipAngleTotal / _wheels.Length;
        float averageGripUsage = gripUsageTotal / _wheels.Length;
        ArcadeHandlingParameters arcade = _parameters.ArcadeHandling;
        bool passiveSlideRecoveryNeeded =
            MathF.Abs(lateralSpeed) > arcade.PassiveSlideRecoveryLateralSpeedMetersPerSecond ||
            MathF.Abs(State.YawRateRadiansPerSecond) > MathHelper.ToRadians(arcade.PassiveSlideRecoveryYawRateDegreesPerSecond);
        bool stabilityAssistAllowed = State.WallContactCount == 0 &&
                                      (MathF.Abs(_filteredSteerInput) > 0.05f ||
                                       driveThrottle > 0.05f ||
                                       brake > 0.05f ||
                                       input.Handbrake > 0.05f ||
                                       passiveSlideRecoveryNeeded);
        Vector2 worldAcceleration = (right * totalForceX + forward * totalForceZ) / _parameters.MassKg;
        if (stabilityAssistAllowed)
        {
            worldAcceleration += CalculateStabilityAssistAcceleration(
                right,
                forwardSpeed,
                lateralSpeed,
                averageSlipAngle,
                averageGripUsage,
                _filteredSteerInput,
                driveThrottle,
                brake);
        }

        State.Velocity += worldAcceleration * dt;

        float yawAcceleration = yawTorque / MathF.Max(1f, _parameters.YawInertiaKgM2);
        State.YawRateRadiansPerSecond += yawAcceleration * dt;
        float yawDampingRate = CalculateYawDampingRate(forwardSpeed, lateralSpeed, averageSlipAngle, averageGripUsage);
        if (stabilityAssistAllowed)
        {
            yawDampingRate += CalculateStabilityYawDampingRate(
                forwardSpeed,
                lateralSpeed,
                averageSlipAngle,
                averageGripUsage,
                _filteredSteerInput,
                driveThrottle,
                brake);
        }

        State.YawRateRadiansPerSecond *= MathF.Exp(-yawDampingRate * dt);
        State.HeadingRadians = MathHelper.WrapAngle(State.HeadingRadians + State.YawRateRadiansPerSecond * dt);

        if (input.Throttle <= 0.01f &&
            input.Brake <= 0.01f &&
            input.Handbrake <= 0.01f &&
            input.Reverse <= 0.01f &&
            State.Velocity.LengthSquared() < 0.02f)
        {
            State.Velocity = Vector2.Zero;
            State.YawRateRadiansPerSecond = 0f;
        }

        State.Position += new Vector3(State.Velocity.X, 0f, State.Velocity.Y) * dt;
        WallCollisionResult wallCollision = ResolveTrackCollisions(dt);
        UpdateGroundContactPose(forward, right, dt);

        forward = GetForward();
        right = GetRight();
        State.SignedForwardSpeed = Vector2.Dot(State.Velocity, forward);
        State.LateralSpeed = Vector2.Dot(State.Velocity, right);
        State.LongitudinalAcceleration = Vector2.Dot(worldAcceleration, forward);
        State.LateralAcceleration = Vector2.Dot(worldAcceleration, right);
        State.SurfaceGrip = weakestGrip;
        State.SurfaceName = weakestSurface;
        State.Throttle = throttle;
        State.EffectiveThrottle = driveThrottle;
        State.Brake = brake;
        State.Handbrake = input.Handbrake;
        State.Steer = _filteredSteerInput;
        State.CounterSteerRecoveryIntensity = counterSteerRecoveryT;
        State.FrontLeftSteerAngleDegrees = MathHelper.ToDegrees(GetWheel(WheelCorner.FrontLeft).SteerAngleRadians);
        State.FrontRightSteerAngleDegrees = MathHelper.ToDegrees(GetWheel(WheelCorner.FrontRight).SteerAngleRadians);
        State.IsShifting = _shiftTimerSeconds > 0f;
        State.ShiftTimeRemainingSeconds = _shiftTimerSeconds;
        State.EngineBrakeTorqueNm = DoesTorqueOpposeTravel(totalDriveTorque, forwardSpeed)
            ? MathF.Abs(totalDriveTorque)
            : 0f;
        ApplyIdleCrankCycleBounce(input, dt);
        PublishEnginePowerState();
        State.FrontBrakeTorqueNm = brakeTorques[(int)WheelCorner.FrontLeft] + brakeTorques[(int)WheelCorner.FrontRight];
        State.RearBrakeTorqueNm = brakeTorques[(int)WheelCorner.RearLeft] + brakeTorques[(int)WheelCorner.RearRight];
        State.AbsActive = absActive;
        State.LockedWheelCount = lockedWheelCount;
        State.DriveForce = drivenLongitudinalForce;
        State.BrakeForce = brakeLongitudinalForce;
        State.AverageSlipRatio = slipRatioTotal / _wheels.Length;
        State.AverageSlipAngleDegrees = MathHelper.ToDegrees(averageSlipAngle);
        State.FrontLeftLoadN = normalLoads[(int)WheelCorner.FrontLeft];
        State.FrontRightLoadN = normalLoads[(int)WheelCorner.FrontRight];
        State.RearLeftLoadN = normalLoads[(int)WheelCorner.RearLeft];
        State.RearRightLoadN = normalLoads[(int)WheelCorner.RearRight];
        State.FrontLeftGripUsage = GetWheel(WheelCorner.FrontLeft).GripUsage;
        State.FrontRightGripUsage = GetWheel(WheelCorner.FrontRight).GripUsage;
        State.RearLeftGripUsage = GetWheel(WheelCorner.RearLeft).GripUsage;
        State.RearRightGripUsage = GetWheel(WheelCorner.RearRight).GripUsage;
        State.FrontLeftSlipRatio = GetWheel(WheelCorner.FrontLeft).SlipRatio;
        State.FrontRightSlipRatio = GetWheel(WheelCorner.FrontRight).SlipRatio;
        State.RearLeftSlipRatio = GetWheel(WheelCorner.RearLeft).SlipRatio;
        State.RearRightSlipRatio = GetWheel(WheelCorner.RearRight).SlipRatio;
        State.FrontLeftSlipAngleDegrees = MathHelper.ToDegrees(GetWheel(WheelCorner.FrontLeft).SlipAngleRadians);
        State.FrontRightSlipAngleDegrees = MathHelper.ToDegrees(GetWheel(WheelCorner.FrontRight).SlipAngleRadians);
        State.RearLeftSlipAngleDegrees = MathHelper.ToDegrees(GetWheel(WheelCorner.RearLeft).SlipAngleRadians);
        State.RearRightSlipAngleDegrees = MathHelper.ToDegrees(GetWheel(WheelCorner.RearRight).SlipAngleRadians);
        State.FrontLeftLongitudinalForceN = GetWheel(WheelCorner.FrontLeft).LongitudinalForceN;
        State.FrontRightLongitudinalForceN = GetWheel(WheelCorner.FrontRight).LongitudinalForceN;
        State.RearLeftLongitudinalForceN = GetWheel(WheelCorner.RearLeft).LongitudinalForceN;
        State.RearRightLongitudinalForceN = GetWheel(WheelCorner.RearRight).LongitudinalForceN;
        State.FrontLeftLateralForceN = GetWheel(WheelCorner.FrontLeft).LateralForceN;
        State.FrontRightLateralForceN = GetWheel(WheelCorner.FrontRight).LateralForceN;
        State.RearLeftLateralForceN = GetWheel(WheelCorner.RearLeft).LateralForceN;
        State.RearRightLateralForceN = GetWheel(WheelCorner.RearRight).LateralForceN;
        State.FrontLeftSurfaceGrip = GetWheel(WheelCorner.FrontLeft).SurfaceGrip;
        State.FrontRightSurfaceGrip = GetWheel(WheelCorner.FrontRight).SurfaceGrip;
        State.RearLeftSurfaceGrip = GetWheel(WheelCorner.RearLeft).SurfaceGrip;
        State.RearRightSurfaceGrip = GetWheel(WheelCorner.RearRight).SurfaceGrip;
        State.FrontLeftSurfaceName = GetWheel(WheelCorner.FrontLeft).SurfaceName;
        State.FrontRightSurfaceName = GetWheel(WheelCorner.FrontRight).SurfaceName;
        State.RearLeftSurfaceName = GetWheel(WheelCorner.RearLeft).SurfaceName;
        State.RearRightSurfaceName = GetWheel(WheelCorner.RearRight).SurfaceName;
        State.FrontLeftCamberDegrees = MathHelper.ToDegrees(GetWheel(WheelCorner.FrontLeft).EffectiveCamberRadians);
        State.FrontRightCamberDegrees = MathHelper.ToDegrees(GetWheel(WheelCorner.FrontRight).EffectiveCamberRadians);
        State.RearLeftCamberDegrees = MathHelper.ToDegrees(GetWheel(WheelCorner.RearLeft).EffectiveCamberRadians);
        State.RearRightCamberDegrees = MathHelper.ToDegrees(GetWheel(WheelCorner.RearRight).EffectiveCamberRadians);
        State.FrontLeftToeDegrees = MathHelper.ToDegrees(GetWheel(WheelCorner.FrontLeft).EffectiveToeRadians);
        State.FrontRightToeDegrees = MathHelper.ToDegrees(GetWheel(WheelCorner.FrontRight).EffectiveToeRadians);
        State.RearLeftToeDegrees = MathHelper.ToDegrees(GetWheel(WheelCorner.RearLeft).EffectiveToeRadians);
        State.RearRightToeDegrees = MathHelper.ToDegrees(GetWheel(WheelCorner.RearRight).EffectiveToeRadians);
        State.WallContactCount = wallCollision.ContactCount;
        if (wallCollision.ImpactSpeedKph > 0.5f)
        {
            State.LastImpactSpeedKph = wallCollision.ImpactSpeedKph;
            State.CrashSeverity = MathHelper.Clamp(wallCollision.ImpactSpeedKph / 85f, 0.05f, 1f);
            State.CrashFlashSeconds = MathF.Max(State.CrashFlashSeconds, MathHelper.Clamp(wallCollision.ImpactSpeedKph / 90f, 0.08f, 0.65f));
        }
        else if (State.CrashFlashSeconds <= 0f)
        {
            State.CrashSeverity = 0f;
        }

        State.CollisionActive = wallCollision.ContactCount > 0 || State.CrashFlashSeconds > 0f;
        UpdateShiftKickTimer(dt);
        UpdateDownshiftOverRevShockTimer(dt);

    }

    private void StepRaceStartHold(VehicleInput input, float dt)
    {
        State.CrashFlashSeconds = MathF.Max(0f, State.CrashFlashSeconds - dt);
        UpdateShiftTimer(dt);
        UpdateGear(input, 0f);

        float throttle = State.Gear < 0 ? input.Reverse : input.Throttle;
        UpdateSteeringInput(input.Steer, 0f, input.Brake, dt);
        UpdateBrakeInput(input.Brake, dt);
        UpdateHeldLaunchRpm(throttle, dt);
        UpdateRevLimiter(throttle, dt);

        State.Velocity = Vector2.Zero;
        State.YawRateRadiansPerSecond = 0f;
        _pendingShiftKickSeverity = 0f;
        _shiftKickSeconds = 0f;
        _shiftKickDurationSeconds = 0f;
        _shiftKickSeverity = 0f;
        _enginePowerShiftHandoffSmoothSeconds = 0f;
        _launchClutchEngagement = 0f;
        _launchClutchTimerSeconds = 0f;
        _preRevLaunchActive = false;
        State.SignedForwardSpeed = 0f;
        State.LateralSpeed = 0f;
        State.LongitudinalAcceleration = 0f;
        State.LateralAcceleration = 0f;
        State.Throttle = throttle;
        State.EffectiveThrottle = 0f;
        State.Brake = _filteredBrakeInput;
        State.Handbrake = input.Handbrake;
        State.Steer = _filteredSteerInput;
        State.DriveForce = 0f;
        State.BrakeForce = 0f;
        State.EngineBrakeTorqueNm = 0f;
        ApplyIdleCrankCycleBounce(input, dt);
        PublishEnginePowerState();
        State.MechanicalOverRevActive = false;
        State.MechanicalOverRevRpm = 0f;
        State.MechanicalOverRevSeverity = 0f;
        State.ShiftKickIntensity = 0f;
        State.PowertrainShockIntensity = 0f;
        State.CounterSteerRecoveryIntensity = 0f;
        State.AbsActive = false;
        State.LockedWheelCount = 0;
        State.IsShifting = _shiftTimerSeconds > 0f;
        State.ShiftTimeRemainingSeconds = _shiftTimerSeconds;

        foreach (WheelRuntimeState wheel in _wheels)
        {
            wheel.AngularVelocityRadiansPerSecond = 0f;
            wheel.SlipRatio = 0f;
            wheel.SlipAngleRadians = 0f;
            wheel.RelaxedSlipAngleRadians = 0f;
            wheel.GripUsage = 0f;
            wheel.LongitudinalForceN = 0f;
            wheel.LateralForceN = 0f;
            wheel.IsLocked = false;
            wheel.AbsActive = false;
            wheel.AbsPressureRatio = 1f;
        }

        Vector2 forward = GetForward();
        Vector2 right = GetRight();
        float[] normalLoads = CalculateNormalLoads(0f);
        SteeringAngles steeringAngles = CalculateSteeringAngles(_filteredSteerInput, 0f, 0f);
        for (int i = 0; i < _wheels.Length; i++)
        {
            WheelRuntimeState wheel = _wheels[i];
            wheel.NormalLoadN = normalLoads[i];
            float commandedSteerAngle = wheel.Corner switch
            {
                WheelCorner.FrontLeft => steeringAngles.FrontLeft,
                WheelCorner.FrontRight => steeringAngles.FrontRight,
                _ => 0f
            };
            WheelAlignment alignment = CalculateWheelAlignment(wheel, commandedSteerAngle, normalLoads[i]);
            wheel.SteerAngleRadians = commandedSteerAngle + alignment.ToeRadians;
            wheel.EffectiveCamberRadians = alignment.CamberRadians;
            wheel.EffectiveToeRadians = alignment.ToeRadians;
            wheel.SuspensionCompressionMeters = alignment.CompressionMeters;
        }

        State.FrontLeftLoadN = normalLoads[(int)WheelCorner.FrontLeft];
        State.FrontRightLoadN = normalLoads[(int)WheelCorner.FrontRight];
        State.RearLeftLoadN = normalLoads[(int)WheelCorner.RearLeft];
        State.RearRightLoadN = normalLoads[(int)WheelCorner.RearRight];
        State.FrontLeftSteerAngleDegrees = MathHelper.ToDegrees(GetWheel(WheelCorner.FrontLeft).SteerAngleRadians);
        State.FrontRightSteerAngleDegrees = MathHelper.ToDegrees(GetWheel(WheelCorner.FrontRight).SteerAngleRadians);
        UpdateGroundContactPose(forward, right, dt);
    }

    private WheelForceResult CalculateWheelForce(
        WheelRuntimeState wheel,
        float driveTorqueNm,
        float brakeTorqueNm,
        Vector2 forward,
        Vector2 right,
        float forwardSpeed,
        float lateralSpeed,
        float counterSteerRecoveryT,
        float dt)
    {
        Vector3 contactPosition = State.Position +
                                  new Vector3(right.X, 0f, right.Y) * wheel.LocalX +
                                  new Vector3(forward.X, 0f, forward.Y) * wheel.LocalZ;
        SurfaceSample surface = _surfaceSampler.Sample(contactPosition);

        float localVelocityX = lateralSpeed + State.YawRateRadiansPerSecond * wheel.LocalZ;
        float localVelocityZ = forwardSpeed - State.YawRateRadiansPerSecond * wheel.LocalX;

        float sinSteer = MathF.Sin(wheel.SteerAngleRadians);
        float cosSteer = MathF.Cos(wheel.SteerAngleRadians);
        float wheelLongitudinalVelocity = localVelocityX * sinSteer + localVelocityZ * cosSteer;
        float wheelLateralVelocity = localVelocityX * cosSteer - localVelocityZ * sinSteer;
        float radius = wheel.Tyres.LoadedRadiusMeters;

        RecoverFreeRollingWheelSpeed(
            wheel,
            driveTorqueNm,
            brakeTorqueNm,
            wheelLongitudinalVelocity,
            radius,
            _engineParameters.VehicleSafety.MinimumSlipSpeedMetersPerSecond,
            dt);
        float slipRatio = CalculateSlipRatio(
            wheel,
            wheelLongitudinalVelocity,
            radius,
            _engineParameters.VehicleSafety.MinimumSlipSpeedMetersPerSecond);
        float slipAngle = MathHelper.Clamp(
            MathF.Atan2(wheelLateralVelocity, MathF.Max(1.5f, MathF.Abs(wheelLongitudinalVelocity))),
            -0.75f,
            0.75f);

        float gripLimit = CalculateGripLimit(wheel, surface.Grip);
        float wheelBrakeSign = SignWithFallback(wheel.AngularVelocityRadiansPerSecond, wheelLongitudinalVelocity);
        float effectiveBrakeTorqueNm = ApplyAbs(wheel, brakeTorqueNm, slipRatio, wheelLongitudinalVelocity, dt);
        float wheelRecoveryT = IsFrontWheel(wheel.Corner)
            ? counterSteerRecoveryT
            : counterSteerRecoveryT * 0.55f;
        float tyreLongitudinalForce = CalculateLongitudinalTyreForce(wheel.Tyres, slipRatio, gripLimit);
        float effectiveSlipAngle = UpdateRelaxedSlipAngle(wheel, slipAngle, wheelLongitudinalVelocity, wheelRecoveryT, dt);
        float tyreLateralForce = CalculateLateralTyreForce(wheel.Tyres, effectiveSlipAngle, gripLimit, wheelRecoveryT);
        tyreLateralForce += CalculateCamberThrust(wheel, gripLimit);
        ApplyBrakeForcePriority(wheel, gripLimit, effectiveBrakeTorqueNm, wheelLongitudinalVelocity, ref tyreLongitudinalForce);
        float combinedGripLimit = CalculateGtStyleCombinedGripLimit(
            wheel,
            gripLimit,
            driveTorqueNm,
            effectiveBrakeTorqueNm,
            effectiveSlipAngle,
            wheelRecoveryT);
        float gripUsage = ApplyCombinedGripLimit(
            _parameters.ArcadeHandling,
            _engineParameters.StabilityAssist,
            wheel.Tyres,
            wheelLongitudinalVelocity,
            effectiveBrakeTorqueNm,
            wheelRecoveryT,
            combinedGripLimit,
            ref tyreLongitudinalForce,
            ref tyreLateralForce);

        float passiveLongitudinalForce = CalculatePassiveSurfaceForce(
            wheelLongitudinalVelocity,
            wheel.Tyres.RollingResistanceCoefficient * surface.RollingResistanceMultiplier * wheel.NormalLoadN,
            surface.LongitudinalDragCoefficient);
        float passiveLateralForce = CalculatePassiveSurfaceForce(
            wheelLateralVelocity,
            0f,
            surface.LateralDragCoefficient);
        float scrubLongitudinalForce = CalculateTyreScrubForce(
            tyreLateralForce,
            wheelLateralVelocity,
            wheelLongitudinalVelocity,
            wheel.Tyres.LateralScrubDragCoefficient);

        float wheelSurfaceSpeed = wheel.AngularVelocityRadiansPerSecond * radius;
        float wheelSpinDragTorque = CalculateWheelSpinDragTorque(
            wheelSurfaceSpeed - wheelLongitudinalVelocity,
            surface.WheelSpinDragCoefficient,
            radius);
        float wheelTorque =
            driveTorqueNm -
            wheelBrakeSign * effectiveBrakeTorqueNm -
            tyreLongitudinalForce * radius -
            wheelSpinDragTorque;
        float previousAngularVelocity = wheel.AngularVelocityRadiansPerSecond;
        wheel.AngularVelocityRadiansPerSecond += wheelTorque / MathF.Max(0.1f, CalculateEffectiveWheelInertia(wheel)) * dt;
        SettleFreeRollingWheelSpeed(
            wheel,
            previousAngularVelocity,
            driveTorqueNm,
            effectiveBrakeTorqueNm,
            wheelLongitudinalVelocity,
            radius,
            dt);
        SettleEngineBrakingWheelSpeed(
            wheel,
            driveTorqueNm,
            effectiveBrakeTorqueNm,
            wheelLongitudinalVelocity,
            radius,
            _engineParameters.VehicleSafety.MinimumSlipSpeedMetersPerSecond,
            dt);

        if (effectiveBrakeTorqueNm > 0.1f &&
            MathF.Abs(driveTorqueNm) < 0.1f &&
            MathF.Abs(wheelLongitudinalVelocity) > 0.5f &&
            MathF.Sign(previousAngularVelocity) != MathF.Sign(wheel.AngularVelocityRadiansPerSecond))
        {
            wheel.AngularVelocityRadiansPerSecond = 0f;
        }

        if (MathF.Abs(wheelLongitudinalVelocity) < 0.2f && MathF.Abs(driveTorqueNm) < 0.1f && effectiveBrakeTorqueNm > 1f)
        {
            wheel.AngularVelocityRadiansPerSecond = 0f;
        }

        bool brakeOnlyOrEngineBraking = effectiveBrakeTorqueNm > 0.1f &&
                                        driveTorqueNm * wheelLongitudinalVelocity <= 0.1f &&
                                        MathF.Abs(wheelLongitudinalVelocity) > 0.5f &&
                                        radius > 0.01f;
        if (brakeOnlyOrEngineBraking)
        {
            float rollingAngularVelocity = wheelLongitudinalVelocity / radius;
            if (wheel.AngularVelocityRadiansPerSecond * rollingAngularVelocity > 0f &&
                MathF.Abs(wheel.AngularVelocityRadiansPerSecond) > MathF.Abs(rollingAngularVelocity))
            {
                wheel.AngularVelocityRadiansPerSecond = rollingAngularVelocity;
            }
        }

        if (MathF.Abs(wheelLongitudinalVelocity) < 0.15f &&
            MathF.Abs(driveTorqueNm) < 0.1f &&
            brakeTorqueNm < 0.1f)
        {
            wheel.AngularVelocityRadiansPerSecond = MathHelper.Lerp(wheel.AngularVelocityRadiansPerSecond, 0f, 0.08f);
        }

        float reportedSlipRatio = CalculateSlipRatio(
            wheel,
            wheelLongitudinalVelocity,
            radius,
            _engineParameters.VehicleSafety.MinimumSlipSpeedMetersPerSecond);
        wheel.SlipRatio = reportedSlipRatio;
        wheel.SlipAngleRadians = effectiveSlipAngle;
        wheel.GripUsage = MathHelper.Clamp(gripUsage, 0f, 1.5f);
        wheel.IsLocked = effectiveBrakeTorqueNm > 1f &&
                         MathF.Abs(wheelLongitudinalVelocity) > _parameters.Brakes.Abs.MinimumSpeedMetersPerSecond &&
                         MathF.Abs(wheel.AngularVelocityRadiansPerSecond * radius) < MathF.Abs(wheelLongitudinalVelocity) * 0.12f;

        float totalLongitudinalForce = tyreLongitudinalForce + passiveLongitudinalForce + scrubLongitudinalForce;
        float totalLateralForce = tyreLateralForce + passiveLateralForce;
        wheel.LongitudinalForceN = totalLongitudinalForce;
        wheel.LateralForceN = totalLateralForce;
        wheel.SurfaceGrip = surface.Grip;
        wheel.SurfaceName = surface.Name;
        float bodyForceX = totalLongitudinalForce * sinSteer + totalLateralForce * cosSteer;
        float bodyForceZ = totalLongitudinalForce * cosSteer - totalLateralForce * sinSteer;

        return new WheelForceResult(
            bodyForceX,
            bodyForceZ,
            totalLongitudinalForce,
            totalLateralForce,
            reportedSlipRatio,
            effectiveSlipAngle,
            wheel.GripUsage,
            surface.Grip,
            surface.Name);
    }

    private static float CalculateSlipRatio(
        WheelRuntimeState wheel,
        float wheelLongitudinalVelocity,
        float radius,
        float minimumSlipSpeedMetersPerSecond)
    {
        return MathHelper.Clamp(
            (wheel.AngularVelocityRadiansPerSecond * radius - wheelLongitudinalVelocity) /
            MathF.Max(minimumSlipSpeedMetersPerSecond, MathF.Abs(wheelLongitudinalVelocity)),
            -3f,
            3f);
    }

    private static void RecoverFreeRollingWheelSpeed(
        WheelRuntimeState wheel,
        float driveTorqueNm,
        float brakeTorqueNm,
        float wheelLongitudinalVelocity,
        float radius,
        float minimumSlipSpeedMetersPerSecond,
        float dt)
    {
        if (MathF.Abs(driveTorqueNm) > 0.1f ||
            brakeTorqueNm > 0.1f ||
            MathF.Abs(wheelLongitudinalVelocity) < 1.0f ||
            radius <= 0.01f)
        {
            return;
        }

        float rollingAngularVelocity = wheelLongitudinalVelocity / radius;
        if (wheel.AngularVelocityRadiansPerSecond * rollingAngularVelocity < 0f)
        {
            return;
        }

        float slipRatio = CalculateSlipRatio(
            wheel,
            wheelLongitudinalVelocity,
            radius,
            minimumSlipSpeedMetersPerSecond);
        float absSlipRatio = MathF.Abs(slipRatio);
        if (absSlipRatio < 0.025f)
        {
            return;
        }

        float recoveryT = SmoothStep(0.035f, 0.22f, absSlipRatio);
        float recoveryRate = MathHelper.Lerp(12f, 42f, recoveryT);
        float blend = 1f - MathF.Exp(-recoveryRate * dt);
        wheel.AngularVelocityRadiansPerSecond = MathHelper.Lerp(
            wheel.AngularVelocityRadiansPerSecond,
            rollingAngularVelocity,
            MathHelper.Clamp(blend, 0f, 1f));
    }

    private static void SettleFreeRollingWheelSpeed(
        WheelRuntimeState wheel,
        float previousAngularVelocity,
        float driveTorqueNm,
        float brakeTorqueNm,
        float wheelLongitudinalVelocity,
        float radius,
        float dt)
    {
        if (MathF.Abs(driveTorqueNm) > 0.1f ||
            brakeTorqueNm > 0.1f ||
            MathF.Abs(wheelLongitudinalVelocity) < 1.0f ||
            radius <= 0.01f)
        {
            return;
        }

        float rollingAngularVelocity = wheelLongitudinalVelocity / radius;
        if (wheel.AngularVelocityRadiansPerSecond * rollingAngularVelocity < 0f)
        {
            return;
        }

        float previousSurfaceSpeedError = previousAngularVelocity * radius - wheelLongitudinalVelocity;
        float currentSurfaceSpeedError = wheel.AngularVelocityRadiansPerSecond * radius - wheelLongitudinalVelocity;
        if (previousSurfaceSpeedError * currentSurfaceSpeedError < 0f ||
            MathF.Abs(currentSurfaceSpeedError) < 0.18f)
        {
            wheel.AngularVelocityRadiansPerSecond = rollingAngularVelocity;
            return;
        }

        float settleBlend = 1f - MathF.Exp(-MathHelper.Clamp(dt, 0f, 1f / 20f) * 72f);
        wheel.AngularVelocityRadiansPerSecond = MathHelper.Lerp(
            wheel.AngularVelocityRadiansPerSecond,
            rollingAngularVelocity,
            MathHelper.Clamp(settleBlend, 0f, 1f));
    }

    private static void SettleEngineBrakingWheelSpeed(
        WheelRuntimeState wheel,
        float driveTorqueNm,
        float brakeTorqueNm,
        float wheelLongitudinalVelocity,
        float radius,
        float minimumSlipSpeedMetersPerSecond,
        float dt)
    {
        if (brakeTorqueNm > 0.1f ||
            MathF.Abs(driveTorqueNm) <= 0.1f ||
            driveTorqueNm * wheelLongitudinalVelocity >= 0f ||
            MathF.Abs(wheelLongitudinalVelocity) < 1.0f ||
            radius <= 0.01f)
        {
            return;
        }

        float targetLongitudinalForce = driveTorqueNm / radius;
        float targetSlipRatio = targetLongitudinalForce / MathF.Max(1f, wheel.Tyres.LongitudinalStiffnessN);
        float peakSlip = MathF.Max(0.01f, wheel.Tyres.LongitudinalPeakSlipRatio);
        targetSlipRatio = MathHelper.Clamp(targetSlipRatio, -peakSlip * 0.55f, peakSlip * 0.55f);

        float slipSpeedReference = MathF.Max(minimumSlipSpeedMetersPerSecond, MathF.Abs(wheelLongitudinalVelocity));
        float targetSurfaceSpeed = wheelLongitudinalVelocity + targetSlipRatio * slipSpeedReference;
        float targetAngularVelocity = targetSurfaceSpeed / radius;
        if (targetAngularVelocity * wheelLongitudinalVelocity < 0f)
        {
            return;
        }

        float currentSlipRatio = CalculateSlipRatio(
            wheel,
            wheelLongitudinalVelocity,
            radius,
            minimumSlipSpeedMetersPerSecond);
        float currentError = currentSlipRatio - targetSlipRatio;
        if (MathF.Abs(currentError) < 0.006f)
        {
            wheel.AngularVelocityRadiansPerSecond = targetAngularVelocity;
            return;
        }

        if (MathF.Abs(currentSlipRatio) <= MathF.Abs(targetSlipRatio) + 0.012f)
        {
            return;
        }

        float correctionT = SmoothStep(0.018f, 0.11f, MathF.Abs(currentError));
        float settleRate = MathHelper.Lerp(18f, 76f, correctionT);
        float settleBlend = 1f - MathF.Exp(-MathHelper.Clamp(dt, 0f, 1f / 20f) * settleRate);
        wheel.AngularVelocityRadiansPerSecond = MathHelper.Lerp(
            wheel.AngularVelocityRadiansPerSecond,
            targetAngularVelocity,
            MathHelper.Clamp(settleBlend, 0f, 1f));
    }

    private float CalculateGripLimit(WheelRuntimeState wheel, float surfaceGrip)
    {
        float referenceLoad = _parameters.MassKg * Gravity * 0.25f;
        float loadSensitivity = MathHelper.Clamp(wheel.Tyres.LoadSensitivity, 0f, 0.35f);
        float loadScale = MathF.Pow(referenceLoad / MathF.Max(150f, wheel.NormalLoadN), loadSensitivity);
        loadScale = MathHelper.Clamp(loadScale, 0.72f, 1.18f);
        return wheel.Tyres.PeakFriction * CalculateCamberGripMultiplier(wheel) * loadScale * surfaceGrip * MathF.Max(0f, wheel.NormalLoadN);
    }

    private float CalculateGtStyleCombinedGripLimit(
        WheelRuntimeState wheel,
        float gripLimit,
        float driveTorqueNm,
        float brakeTorqueNm,
        float slipAngleRadians,
        float counterSteerRecoveryT)
    {
        float lateralDemand = SmoothStep(0.05f, 0.20f, MathF.Abs(slipAngleRadians));
        ArcadeHandlingParameters arcade = _parameters.ArcadeHandling;
        float poweredFrontAllowance = IsFrontWheel(wheel.Corner) &&
                                      _parameters.DrivenWheels.IsDriven(wheel.Corner) &&
                                      driveTorqueNm > 0.1f &&
                                      brakeTorqueNm <= 0.1f
            ? arcade.DrivenGripAllowance
            : arcade.GenericGripAllowance;
        float brakingAllowance = brakeTorqueNm > 0.1f
            ? arcade.BrakingGripAllowance
            : 0f;
        float counterSteerAllowance = MathHelper.Clamp(counterSteerRecoveryT, 0f, 1f) *
                                      MathF.Max(0f, _engineParameters.StabilityAssist.CounterSteerGripAllowance);
        return gripLimit * (1f + lateralDemand * (MathF.Max(poweredFrontAllowance, brakingAllowance) + counterSteerAllowance));
    }

    private static void ApplyBrakeForcePriority(
        WheelRuntimeState wheel,
        float gripLimit,
        float effectiveBrakeTorqueNm,
        float wheelLongitudinalVelocity,
        ref float tyreLongitudinalForce)
    {
        if (effectiveBrakeTorqueNm <= 0.1f ||
            gripLimit <= 1f ||
            MathF.Abs(wheelLongitudinalVelocity) < 0.5f)
        {
            return;
        }

        float brakeDirection = -MathF.Sign(wheelLongitudinalVelocity);
        float currentBrakeForce = MathF.Max(0f, tyreLongitudinalForce * brakeDirection);
        float requestedBrakeForce = effectiveBrakeTorqueNm / MathF.Max(0.05f, wheel.Tyres.LoadedRadiusMeters);
        float demandT = SmoothStep(gripLimit * 0.10f, gripLimit * 0.90f, requestedBrakeForce);
        float reservedShare = MathHelper.Lerp(0.22f, 0.58f, demandT);
        float targetBrakeForce = MathF.Min(requestedBrakeForce, gripLimit * reservedShare);
        if (currentBrakeForce >= targetBrakeForce)
        {
            return;
        }

        float blend = MathHelper.Lerp(0.28f, 0.54f, demandT);
        tyreLongitudinalForce = brakeDirection * MathHelper.Lerp(currentBrakeForce, targetBrakeForce, blend);
    }

    private static float ApplyCombinedGripLimit(
        ArcadeHandlingParameters arcade,
        StabilityAssistParameters stability,
        TyreAxleParameters tyres,
        float wheelLongitudinalVelocity,
        float effectiveBrakeTorqueNm,
        float counterSteerRecoveryT,
        float combinedGripLimit,
        ref float tyreLongitudinalForce,
        ref float tyreLateralForce)
    {
        float combinedForce = MathF.Sqrt(tyreLongitudinalForce * tyreLongitudinalForce + tyreLateralForce * tyreLateralForce);
        float gripUsage = combinedGripLimit > 1f ? combinedForce / combinedGripLimit : 0f;
        if (combinedForce <= combinedGripLimit || combinedForce <= 0.0001f)
        {
            return gripUsage;
        }

        float absoluteLongitudinalForce = MathF.Abs(tyreLongitudinalForce);
        float absoluteLateralForce = MathF.Abs(tyreLateralForce);
        float lateralDemandShare = absoluteLateralForce /
                                   MathF.Max(0.0001f, absoluteLongitudinalForce + absoluteLateralForce);
        float slidingMultiplier = MathHelper.Lerp(
            MathHelper.Clamp(tyres.SlidingFrictionMultiplier, 0.25f, 1f),
            MathHelper.Clamp(tyres.SlidingLateralFrictionMultiplier, 0.25f, 1f),
            MathF.Sqrt(lateralDemandShare));
        if (effectiveBrakeTorqueNm > 0.1f &&
            MathF.Abs(wheelLongitudinalVelocity) > 0.5f &&
            tyreLongitudinalForce * wheelLongitudinalVelocity < 0f)
        {
            slidingMultiplier = MathF.Max(slidingMultiplier, arcade.BrakingSlidingFrictionFloor);
        }

        float slidingRecovery = MathHelper.Clamp(counterSteerRecoveryT, 0f, 1f) *
                                MathHelper.Clamp(stability.CounterSteerSlidingFrictionRecovery, 0f, 0.65f);
        slidingMultiplier = MathHelper.Lerp(slidingMultiplier, 1f, slidingRecovery);

        float slidingGripLimit = combinedGripLimit * slidingMultiplier;
        float scale = slidingGripLimit / combinedForce;
        tyreLongitudinalForce *= scale;
        tyreLateralForce *= scale;
        return gripUsage;
    }

    private static float CalculateCamberGripMultiplier(WheelRuntimeState wheel)
    {
        float camberErrorDegrees = MathF.Abs(MathHelper.ToDegrees(wheel.EffectiveCamberRadians - wheel.Tyres.IdealCamberRadians));
        float multiplier = 1f - camberErrorDegrees * MathF.Max(0f, wheel.Tyres.CamberGripLossPerDegree);
        return MathHelper.Clamp(multiplier, MathHelper.Clamp(wheel.Tyres.MinimumCamberGripMultiplier, 0.5f, 1f), 1.04f);
    }

    private static float CalculateCamberThrust(WheelRuntimeState wheel, float gripLimit)
    {
        if (wheel.Tyres.CamberThrustStiffnessNPerRad <= 0f || MathF.Abs(wheel.EffectiveCamberRadians) < 0.0001f)
        {
            return 0f;
        }

        float sideSign = MathF.Sign(wheel.LocalX);
        if (sideSign == 0f)
        {
            return 0f;
        }

        float loadScale = wheel.NormalLoadN / MathF.Max(1f, wheel.NormalLoadN + 1200f);
        float thrust = sideSign * wheel.EffectiveCamberRadians * wheel.Tyres.CamberThrustStiffnessNPerRad * loadScale;
        return MathHelper.Clamp(thrust, -gripLimit * 0.18f, gripLimit * 0.18f);
    }

    private WheelAlignment CalculateWheelAlignment(WheelRuntimeState wheel, float commandedSteerAngle, float normalLoadN)
    {
        SuspensionGeometryParameters geometry = IsFrontWheel(wheel.Corner)
            ? _parameters.FrontSuspensionGeometry
            : _parameters.RearSuspensionGeometry;

        float compression = EstimateSuspensionCompression(wheel.Corner, normalLoadN, geometry);
        float sideSign = MathF.Sign(wheel.LocalX);
        float bodyRollCamber = -_dynamicBodyRollRadians * sideSign * geometry.BodyRollCamberMultiplier;
        float casterCamber = IsFrontWheel(wheel.Corner)
            ? -commandedSteerAngle * geometry.CasterRadians * geometry.CasterCamberGain * sideSign
            : 0f;
        float camber = geometry.StaticCamberRadians +
                       geometry.CamberGainRadiansPerMeter * compression +
                       bodyRollCamber +
                       casterCamber;

        float toeIn = geometry.StaticToeRadians + geometry.ToeGainRadiansPerMeter * compression;
        float toe = sideSign < 0f ? -toeIn : toeIn;
        return new WheelAlignment(camber, toe, compression);
    }

    private float EstimateSuspensionCompression(WheelCorner corner, float normalLoadN, SuspensionGeometryParameters geometry)
    {
        float springRate = IsFrontWheel(corner)
            ? _parameters.FrontSpringRateNPerM
            : _parameters.RearSpringRateNPerM;
        float staticLoad = GetStaticWheelLoad(corner);
        float compression = (normalLoadN - staticLoad) / MathF.Max(1f, springRate);
        return MathHelper.Clamp(compression, -geometry.MaxDroopMeters, geometry.MaxCompressionMeters);
    }

    private float GetStaticWheelLoad(WheelCorner corner)
    {
        float totalWeight = _parameters.MassKg * Gravity;
        float frontWeight = totalWeight * MathHelper.Clamp(_parameters.FrontWeightDistribution, 0.1f, 0.9f);
        return IsFrontWheel(corner)
            ? frontWeight * 0.5f
            : (totalWeight - frontWeight) * 0.5f;
    }

    private static bool IsFrontWheel(WheelCorner corner)
    {
        return corner is WheelCorner.FrontLeft or WheelCorner.FrontRight;
    }

    private static WheelCorner GetOppositeWheelCorner(WheelCorner corner)
    {
        return corner switch
        {
            WheelCorner.FrontLeft => WheelCorner.FrontRight,
            WheelCorner.FrontRight => WheelCorner.FrontLeft,
            WheelCorner.RearLeft => WheelCorner.RearRight,
            WheelCorner.RearRight => WheelCorner.RearLeft,
            _ => corner
        };
    }

    private static float CalculateLongitudinalTyreForce(TyreAxleParameters tyres, float slipRatio, float gripLimit)
    {
        if (MathF.Abs(slipRatio) <= 0.0001f || gripLimit <= 0f)
        {
            return 0f;
        }

        float absSlip = MathF.Abs(slipRatio);
        float peakSlip = MathF.Max(0.01f, tyres.LongitudinalPeakSlipRatio);
        float slideSlip = MathF.Max(peakSlip + 0.01f, tyres.LongitudinalSlideSlipRatio);
        float slidingGrip = MathHelper.Clamp(tyres.SlidingFrictionMultiplier, 0.25f, 1f);

        float curveScale;
        if (absSlip <= peakSlip)
        {
            float t = absSlip / peakSlip;
            float riseShape = MathF.Max(0.01f, tyres.LongitudinalForceRiseShape);
            curveScale = (1f - MathF.Exp(-riseShape * t)) / (1f - MathF.Exp(-riseShape));
        }
        else if (absSlip <= slideSlip)
        {
            float t = (absSlip - peakSlip) / (slideSlip - peakSlip);
            curveScale = MathHelper.Lerp(1f, slidingGrip, t);
        }
        else
        {
            curveScale = slidingGrip;
        }

        float linearForce = MathF.Abs(tyres.LongitudinalStiffnessN * slipRatio);
        float curveForce = gripLimit * curveScale;
        return MathF.Sign(slipRatio) * MathF.Min(linearForce, curveForce);
    }

    private float CalculateLateralTyreForce(
        TyreAxleParameters tyres,
        float slipAngleRadians,
        float gripLimit,
        float counterSteerRecoveryT)
    {
        if (MathF.Abs(slipAngleRadians) <= 0.0001f || gripLimit <= 0f)
        {
            return 0f;
        }

        float absSlip = MathF.Abs(slipAngleRadians);
        float peakSlip = MathF.Max(0.01f, tyres.LateralPeakSlipAngleRadians);
        float slideSlip = MathF.Max(peakSlip + 0.01f, tyres.LateralSlideSlipAngleRadians);
        float slidingGrip = MathHelper.Clamp(tyres.SlidingLateralFrictionMultiplier, 0.25f, 1f);
        float slidingRecovery = MathHelper.Clamp(counterSteerRecoveryT, 0f, 1f) *
                                MathHelper.Clamp(_engineParameters.StabilityAssist.CounterSteerSlidingFrictionRecovery, 0f, 0.65f);
        slidingGrip = MathHelper.Lerp(slidingGrip, 1f, slidingRecovery);

        float curveScale;
        if (absSlip <= peakSlip)
        {
            float t = absSlip / peakSlip;
            float riseShape = MathF.Max(0.01f, tyres.LateralForceRiseShape);
            curveScale = (1f - MathF.Exp(-riseShape * t)) / (1f - MathF.Exp(-riseShape));
        }
        else if (absSlip <= slideSlip)
        {
            float t = (absSlip - peakSlip) / (slideSlip - peakSlip);
            curveScale = MathHelper.Lerp(1f, slidingGrip, t);
        }
        else
        {
            curveScale = slidingGrip;
        }

        float linearForce = MathF.Abs(tyres.CorneringStiffnessNPerRad * slipAngleRadians);
        float curveForce = gripLimit * curveScale;
        return -MathF.Sign(slipAngleRadians) * MathF.Min(linearForce, curveForce);
    }

    private float UpdateRelaxedSlipAngle(
        WheelRuntimeState wheel,
        float targetSlipAngle,
        float wheelLongitudinalVelocity,
        float counterSteerRecoveryT,
        float dt)
    {
        float relaxationLength = MathF.Max(0.01f, wheel.Tyres.RelaxationLengthMeters);
        float relaxationMultiplier = MathHelper.Lerp(
            1f,
            MathHelper.Clamp(_engineParameters.StabilityAssist.CounterSteerSlipRelaxationMultiplier, 0.25f, 1f),
            MathHelper.Clamp(counterSteerRecoveryT, 0f, 1f));
        relaxationLength *= relaxationMultiplier;
        float relaxationSpeed = MathF.Max(1f, MathF.Abs(wheelLongitudinalVelocity));
        float blend = 1f - MathF.Exp(-(relaxationSpeed / relaxationLength) * dt);
        wheel.RelaxedSlipAngleRadians = MathHelper.Lerp(wheel.RelaxedSlipAngleRadians, targetSlipAngle, MathHelper.Clamp(blend, 0f, 1f));
        return wheel.RelaxedSlipAngleRadians;
    }

    private float CalculateYawDampingRate(
        float forwardSpeed,
        float lateralSpeed,
        float averageSlipAngleRadians,
        float averageGripUsage)
    {
        float speed = MathF.Sqrt(forwardSpeed * forwardSpeed + lateralSpeed * lateralSpeed);
        float speedT = MathHelper.Clamp((speed - 8f) / 32f, 0f, 1f);
        float peakSlipAngle = (_parameters.FrontTyres.LateralPeakSlipAngleRadians +
                               _parameters.RearTyres.LateralPeakSlipAngleRadians) * 0.5f;
        float bodySlipAngle = MathF.Abs(MathF.Atan2(lateralSpeed, MathF.Max(2f, MathF.Abs(forwardSpeed))));
        float slipT = MathHelper.Clamp(
            (MathF.Max(MathF.Abs(averageSlipAngleRadians), bodySlipAngle) - peakSlipAngle) /
            MathF.Max(0.05f, 0.55f - peakSlipAngle),
            0f,
            1f);
        float gripT = MathHelper.Clamp((averageGripUsage - 0.55f) / 0.45f, 0f, 1f);
        float response = MathHelper.Clamp(_parameters.LateralGripResponse / 8.5f, 0.55f, 1.45f);

        return (0.18f + speedT * 0.14f + slipT * gripT * 2.30f) * response;
    }

    private Vector2 CalculateStabilityAssistAcceleration(
        Vector2 right,
        float forwardSpeed,
        float lateralSpeed,
        float averageSlipAngleRadians,
        float averageGripUsage,
        float steerInput,
        float driveThrottle,
        float brake)
    {
        StabilityAssistParameters stability = _engineParameters.StabilityAssist;
        float speed = MathF.Sqrt(forwardSpeed * forwardSpeed + lateralSpeed * lateralSpeed);
        if (speed < stability.MinimumSpeedMetersPerSecond ||
            MathF.Abs(lateralSpeed) < stability.MinimumLateralSpeedMetersPerSecond)
        {
            return Vector2.Zero;
        }

        float assistT = CalculateStabilityAssistT(forwardSpeed, lateralSpeed, averageSlipAngleRadians, averageGripUsage);
        if (assistT <= 0.001f)
        {
            return Vector2.Zero;
        }

        float speedT = SmoothStep(stability.SpeedBlendStartMetersPerSecond, stability.SpeedBlendEndMetersPerSecond, speed);
        float gripT = SmoothStep(stability.GripBlendStart, stability.GripBlendEnd, averageGripUsage);
        float throttleT = SmoothStep(stability.ThrottleBlendStart, stability.ThrottleBlendEnd, driveThrottle);
        float brakeT = SmoothStep(stability.BrakeBlendStart, stability.BrakeBlendEnd, CalculateCommittedTurnBrakeDampingInput(brake, steerInput));
        float counterSteerT = CalculateCounterSteerRecoveryT(steerInput, forwardSpeed, lateralSpeed);
        float recoveryBoost = counterSteerT * MathHelper.Clamp(stability.CounterSteerSlidingFrictionRecovery, 0f, 0.65f);
        float dampingRate = MathHelper.Lerp(stability.LateralDampingMin, stability.LateralDampingMax, assistT) * speedT;
        dampingRate *= 1f +
                       gripT * stability.LateralGripBoost +
                       throttleT * stability.LateralThrottleBoost +
                       brakeT * stability.LateralBrakeBoost;
        dampingRate *= 1f + recoveryBoost * 1.35f;

        float maxAcceleration = Gravity *
                                MathHelper.Lerp(stability.MaxLateralAccelerationMinG, stability.MaxLateralAccelerationMaxG, assistT) *
                                speedT;
        maxAcceleration *= 1f + recoveryBoost * 0.55f;
        float lateralAcceleration = MathHelper.Clamp(-lateralSpeed * dampingRate, -maxAcceleration, maxAcceleration);
        return right * lateralAcceleration;
    }

    private float CalculateStabilityYawDampingRate(
        float forwardSpeed,
        float lateralSpeed,
        float averageSlipAngleRadians,
        float averageGripUsage,
        float steerInput,
        float driveThrottle,
        float brake)
    {
        StabilityAssistParameters stability = _engineParameters.StabilityAssist;
        float speed = MathF.Sqrt(forwardSpeed * forwardSpeed + lateralSpeed * lateralSpeed);
        if (speed < stability.MinimumSpeedMetersPerSecond)
        {
            return 0f;
        }

        float assistT = CalculateStabilityAssistT(forwardSpeed, lateralSpeed, averageSlipAngleRadians, averageGripUsage);
        if (assistT <= 0.001f)
        {
            return 0f;
        }

        float speedT = SmoothStep(stability.SpeedBlendStartMetersPerSecond, stability.SpeedBlendEndMetersPerSecond, speed);
        float gripT = SmoothStep(stability.GripBlendStart, stability.GripBlendEnd, averageGripUsage);
        float throttleT = SmoothStep(stability.ThrottleBlendStart, stability.ThrottleBlendEnd, driveThrottle);
        float brakeT = SmoothStep(stability.BrakeBlendStart, stability.BrakeBlendEnd, CalculateCommittedTurnBrakeDampingInput(brake, steerInput));
        float counterSteerT = CalculateCounterSteerRecoveryT(steerInput, forwardSpeed, lateralSpeed);
        float neutralRecoveryT = 1f - SmoothStep(stability.NeutralRecoveryInputStart, stability.NeutralRecoveryInputEnd, MathF.Abs(steerInput));
        float recoveryT = MathF.Max(counterSteerT, neutralRecoveryT * stability.NeutralRecoveryMultiplier);

        float dampingRate = MathHelper.Lerp(stability.YawDampingMin, stability.YawDampingMax, assistT) * speedT;
        dampingRate *= 1f +
                       gripT * stability.YawGripBoost +
                       recoveryT * stability.YawRecoveryBoost +
                       throttleT * stability.YawThrottleBoost +
                       brakeT * stability.YawBrakeBoost;
        if (MathF.Abs(steerInput) >= 0.05f &&
            MathF.Abs(State.YawRateRadiansPerSecond) >= MathHelper.ToRadians(stability.MinimumYawRateDegreesPerSecond))
        {
            float requestedYawDirection = -MathF.Sign(steerInput);
            float currentYawDirection = MathF.Sign(State.YawRateRadiansPerSecond);
            if (requestedYawDirection * currentYawDirection > 0f)
            {
                float committedTurnT = SmoothStep(stability.CommittedTurnInputStart, stability.CommittedTurnInputEnd, MathF.Abs(steerInput));
                dampingRate *= MathHelper.Lerp(1f, stability.CommittedTurnBrakeDampingMultiplier, committedTurnT * brakeT);
            }
        }

        return dampingRate;
    }

    private static float CalculateCommittedTurnBrakeDampingInput(float brake, float steerInput)
    {
        if (brake <= 0.001f)
        {
            return 0f;
        }

        float committedSteerT = SmoothStep(0.18f, 0.72f, MathF.Abs(steerInput));
        return brake * MathHelper.Lerp(1f, 0.58f, committedSteerT);
    }

    private float CalculateStabilityAssistT(
        float forwardSpeed,
        float lateralSpeed,
        float averageSlipAngleRadians,
        float averageGripUsage)
    {
        StabilityAssistParameters stability = _engineParameters.StabilityAssist;
        float bodySlipAngle = MathF.Abs(MathF.Atan2(lateralSpeed, MathF.Max(2f, MathF.Abs(forwardSpeed))));
        float bodySlipT = SmoothStep(
            MathHelper.ToRadians(stability.BodySlipStartDegrees),
            MathHelper.ToRadians(stability.BodySlipEndDegrees),
            bodySlipAngle);
        float tyreSlipT = SmoothStep(
            MathHelper.ToRadians(stability.TyreSlipStartDegrees),
            MathHelper.ToRadians(stability.TyreSlipEndDegrees),
            MathF.Abs(averageSlipAngleRadians));
        float gripT = SmoothStep(stability.AssistGripStart, stability.AssistGripEnd, averageGripUsage);
        float bodyAssistT = bodySlipT * MathHelper.Lerp(stability.BodyGripInfluenceMin, stability.BodyGripInfluenceMax, gripT);
        float tyreAssistT = tyreSlipT * MathHelper.Lerp(stability.TyreGripInfluenceMin, stability.TyreGripInfluenceMax, gripT);
        return MathHelper.Clamp(MathF.Max(bodyAssistT, tyreAssistT), 0f, 1f);
    }

    private float CalculateCounterSteerRecoveryT(float steerInput, float forwardSpeed, float lateralSpeed)
    {
        StabilityAssistParameters stability = _engineParameters.StabilityAssist;
        float absSteer = MathF.Abs(steerInput);
        float speed = MathF.Sqrt(forwardSpeed * forwardSpeed + lateralSpeed * lateralSpeed);
        if (absSteer < 0.05f || speed < stability.MinimumSpeedMetersPerSecond)
        {
            return 0f;
        }

        float correctionT = 0f;
        if (MathF.Abs(State.YawRateRadiansPerSecond) >= MathHelper.ToRadians(stability.MinimumYawRateDegreesPerSecond))
        {
            float requestedYawDirection = -MathF.Sign(steerInput);
            float currentYawDirection = MathF.Sign(State.YawRateRadiansPerSecond);
            if (requestedYawDirection * currentYawDirection < 0f)
            {
                correctionT = 1f;
            }
        }

        if (MathF.Abs(lateralSpeed) >= stability.MinimumLateralSpeedMetersPerSecond &&
            steerInput * lateralSpeed < 0f)
        {
            float bodySlipAngle = MathF.Abs(MathF.Atan2(lateralSpeed, MathF.Max(2f, MathF.Abs(forwardSpeed))));
            correctionT = MathF.Max(
                correctionT,
                SmoothStep(
                    MathHelper.ToRadians(stability.BodySlipStartDegrees),
                    MathHelper.ToRadians(stability.BodySlipEndDegrees),
                    bodySlipAngle));
        }

        return MathHelper.Clamp(
            correctionT * SmoothStep(stability.CounterSteerInputStart, stability.CounterSteerInputEnd, absSteer),
            0f,
            1f);
    }

    private static float CalculateTyreScrubForce(
        float lateralForce,
        float lateralVelocity,
        float longitudinalVelocity,
        float scrubCoefficient)
    {
        if (scrubCoefficient <= 0f || MathF.Abs(longitudinalVelocity) < 0.05f)
        {
            return 0f;
        }

        float scrubPower = MathF.Abs(lateralForce * lateralVelocity) * scrubCoefficient;
        float scrubForce = scrubPower / MathF.Max(2f, MathF.Abs(longitudinalVelocity));
        return -MathF.Sign(longitudinalVelocity) * scrubForce;
    }

    private float[] CalculateNormalLoads(float forwardSpeed)
    {
        float totalWeight = _parameters.MassKg * Gravity;
        float frontStatic = totalWeight * MathHelper.Clamp(_parameters.FrontWeightDistribution, 0.1f, 0.9f);
        float rearStatic = totalWeight - frontStatic;
        float speedSquared = forwardSpeed * forwardSpeed;

        float frontAeroLoad = -_parameters.FrontLiftFactor * speedSquared;
        float rearAeroLoad = -_parameters.RearLiftFactor * speedSquared;
        float longitudinalTransfer = _parameters.MassKg * State.LongitudinalAcceleration * _parameters.CenterOfGravityHeightMeters / _parameters.WheelbaseMeters;

        float frontAxleLoad = MathF.Max(80f, frontStatic - longitudinalTransfer + frontAeroLoad);
        float rearAxleLoad = MathF.Max(80f, rearStatic + longitudinalTransfer + rearAeroLoad);

        float frontRollStiffness = _parameters.FrontSpringRateNPerM * _parameters.FrontTrackMeters * _parameters.FrontTrackMeters * 0.5f +
                                   _parameters.FrontAntiRollBarRateNmPerRad;
        float rearRollStiffness = _parameters.RearSpringRateNPerM * _parameters.RearTrackMeters * _parameters.RearTrackMeters * 0.5f +
                                  _parameters.RearAntiRollBarRateNmPerRad;
        float frontRollShare = frontRollStiffness / MathF.Max(1f, frontRollStiffness + rearRollStiffness);

        ArcadeHandlingParameters arcade = _parameters.ArcadeHandling;
        float pseudoLateralAcceleration = _filteredSteerInput *
                                          forwardSpeed *
                                          MathF.Abs(forwardSpeed) *
                                          arcade.PseudoLateralTransferScale;
        float pseudoTransferT = arcade.PseudoLateralTransferBlend * SmoothStep(4f, 18f, MathF.Abs(forwardSpeed));
        float lateralTransferAcceleration = MathHelper.Lerp(State.LateralAcceleration, pseudoLateralAcceleration, pseudoTransferT);
        float totalLateralTransferMoment = _parameters.MassKg * lateralTransferAcceleration * _parameters.CenterOfGravityHeightMeters;
        float frontLateralTransfer = totalLateralTransferMoment * frontRollShare / MathF.Max(0.4f, _parameters.FrontTrackMeters);
        float rearLateralTransfer = totalLateralTransferMoment * (1f - frontRollShare) / MathF.Max(0.4f, _parameters.RearTrackMeters);

        return
        [
            MathF.Max(20f, frontAxleLoad * 0.5f + frontLateralTransfer * 0.5f),
            MathF.Max(20f, frontAxleLoad * 0.5f - frontLateralTransfer * 0.5f),
            MathF.Max(20f, rearAxleLoad * 0.5f + rearLateralTransfer * 0.5f),
            MathF.Max(20f, rearAxleLoad * 0.5f - rearLateralTransfer * 0.5f)
        ];
    }

    private float[] DistributeDriveTorque(float totalDriveTorque, float[] normalLoads)
    {
        float[] torques = new float[4];
        if (MathF.Abs(totalDriveTorque) < 0.001f || _parameters.DrivenWheels.Count == 0)
        {
            return torques;
        }

        DistributeAxleTorque(WheelCorner.FrontLeft, WheelCorner.FrontRight);
        DistributeAxleTorque(WheelCorner.RearLeft, WheelCorner.RearRight);
        DistributeSingleWheelTorque(WheelCorner.FrontLeft);
        DistributeSingleWheelTorque(WheelCorner.FrontRight);
        DistributeSingleWheelTorque(WheelCorner.RearLeft);
        DistributeSingleWheelTorque(WheelCorner.RearRight);
        return torques;

        void DistributeAxleTorque(WheelCorner left, WheelCorner right)
        {
            bool leftDriven = _parameters.DrivenWheels.IsDriven(left);
            bool rightDriven = _parameters.DrivenWheels.IsDriven(right);
            if (!leftDriven || !rightDriven)
            {
                return;
            }

            float axleTorque = totalDriveTorque * 2f / _parameters.DrivenWheels.Count;
            float loadLeft = normalLoads[(int)left];
            float loadRight = normalLoads[(int)right];
            float leftShare = loadLeft / MathF.Max(1f, loadLeft + loadRight);
            float torqueBias = MathF.Max(1f, _parameters.DifferentialTorqueBiasRatio);
            float minShare = 1f / (1f + torqueBias);
            float maxShare = torqueBias / (1f + torqueBias);
            if (torqueBias <= 1.01f)
            {
                leftShare = 0.5f;
            }
            else
            {
                leftShare += CalculateLimitedSlipShareCorrection(left, right, totalDriveTorque);
                leftShare = MathHelper.Clamp(leftShare, minShare, maxShare);
            }

            torques[(int)left] = axleTorque * leftShare;
            torques[(int)right] = axleTorque * (1f - leftShare);
        }

        void DistributeSingleWheelTorque(WheelCorner corner)
        {
            int index = (int)corner;
            if (!_parameters.DrivenWheels.IsDriven(corner) || MathF.Abs(torques[index]) > 0.001f)
            {
                return;
            }

            torques[index] = totalDriveTorque / _parameters.DrivenWheels.Count;
        }
    }

    private float CalculateLimitedSlipShareCorrection(WheelCorner left, WheelCorner right, float totalDriveTorque)
    {
        if (MathF.Abs(totalDriveTorque) < 0.001f)
        {
            return 0f;
        }

        WheelRuntimeState leftWheel = GetWheel(left);
        WheelRuntimeState rightWheel = GetWheel(right);
        float torqueSign = MathF.Sign(totalDriveTorque);
        float leftDriveSlip = MathF.Max(0f, leftWheel.SlipRatio * torqueSign);
        float rightDriveSlip = MathF.Max(0f, rightWheel.SlipRatio * torqueSign);
        float slipCorrection = MathHelper.Clamp((rightDriveSlip - leftDriveSlip) * 0.28f, -0.18f, 0.18f);

        float leftWheelSpeed = MathF.Abs(leftWheel.AngularVelocityRadiansPerSecond);
        float rightWheelSpeed = MathF.Abs(rightWheel.AngularVelocityRadiansPerSecond);
        float averageWheelSpeed = MathF.Max(8f, (leftWheelSpeed + rightWheelSpeed) * 0.5f);
        float speedCorrection = MathHelper.Clamp((rightWheelSpeed - leftWheelSpeed) / averageWheelSpeed * 0.16f, -0.10f, 0.10f);

        float steeringLoadCorrection = 0f;
        if (left is WheelCorner.FrontLeft &&
            right is WheelCorner.FrontRight &&
            MathF.Abs(_filteredSteerInput) > 0.04f &&
            totalDriveTorque > 0f)
        {
            steeringLoadCorrection = MathHelper.Clamp(_filteredSteerInput * 0.045f, -0.045f, 0.045f);
        }

        return slipCorrection + speedCorrection + steeringLoadCorrection;
    }

    private float ApplyDigitalThrottleAssist(float requestedThrottle, float forwardSpeed)
    {
        if (requestedThrottle <= 0.001f)
        {
            return 0f;
        }

        DigitalThrottleAssistParameters assist = _engineParameters.DigitalThrottleAssist;
        float speed = MathF.Abs(forwardSpeed);
        if (speed < assist.FullThrottleBelowSpeedMetersPerSecond)
        {
            return requestedThrottle;
        }

        float drivenGripUsage = 0f;
        float drivenSlipRatio = 0f;
        int drivenWheelCount = 0;
        foreach (WheelRuntimeState wheel in _wheels)
        {
            if (!_parameters.DrivenWheels.IsDriven(wheel.Corner))
            {
                continue;
            }

            drivenGripUsage = MathF.Max(drivenGripUsage, wheel.GripUsage);
            drivenSlipRatio = MathF.Max(drivenSlipRatio, MathF.Max(0f, wheel.SlipRatio));
            drivenWheelCount++;
        }

        if (drivenWheelCount == 0)
        {
            return requestedThrottle;
        }

        float speedT = SmoothStep(assist.SpeedBlendStartMetersPerSecond, assist.SpeedBlendEndMetersPerSecond, speed);
        float steerT = SmoothStep(assist.SteeringBlendStart, assist.SteeringBlendEnd, MathF.Abs(_filteredSteerInput));
        if (steerT <= 0.001f && speed < assist.StraightLaunchBypassSpeedMetersPerSecond)
        {
            return requestedThrottle;
        }

        float gripT = SmoothStep(assist.GripUsageBlendStart, assist.GripUsageBlendEnd, drivenGripUsage);
        float slipT = SmoothStep(assist.SlipRatioBlendStart, assist.SlipRatioBlendEnd, drivenSlipRatio);
        float cornerLimit = MathHelper.Lerp(
            1f,
            MathHelper.Lerp(assist.CornerLimitLowSpeed, assist.CornerLimitHighSpeed, speedT),
            steerT);
        float tractionDemand = MathF.Max(slipT, gripT * assist.TractionDemandGripScale);
        float tractionLimit = MathHelper.Lerp(1f, assist.TractionLimitFloor, tractionDemand);
        float assistLimit = MathHelper.Clamp(MathF.Min(cornerLimit, tractionLimit), assist.MinimumAssistLimit, 1f);
        return MathF.Min(requestedThrottle, assistLimit);
    }

    private float ApplyDigitalBrakeAssist(float requestedBrake, float forwardSpeed)
    {
        if (requestedBrake <= 0.001f)
        {
            return 0f;
        }

        DigitalBrakeAssistParameters assist = _engineParameters.DigitalBrakeAssist;
        float speed = MathF.Abs(forwardSpeed);
        if (speed < assist.FullBrakeBelowSpeedMetersPerSecond)
        {
            return requestedBrake;
        }

        float speedT = SmoothStep(assist.SpeedBlendStartMetersPerSecond, assist.SpeedBlendEndMetersPerSecond, speed);
        float steerT = SmoothStep(assist.SteeringBlendStart, assist.SteeringBlendEnd, MathF.Abs(_filteredSteerInput));
        float baselineLimit = MathHelper.Lerp(1.0f, assist.HighSpeedBrakeLimit, speedT);
        float assistedLimit = baselineLimit -
                              steerT * MathHelper.Lerp(assist.SteeringReductionLowSpeed, assist.SteeringReductionHighSpeed, speedT);
        assistedLimit = MathHelper.Clamp(assistedLimit, assist.MinimumAssistLimit, assist.MaximumAssistLimit);
        return MathF.Min(requestedBrake, assistedLimit);
    }

    private float ApplyBrakeThrottlePriority(float requestedThrottle, float brake)
    {
        if (requestedThrottle <= 0.001f || brake <= 0.02f)
        {
            return requestedThrottle;
        }

        BrakeThrottlePriorityParameters priority = _engineParameters.BrakeThrottlePriority;
        float brakeT = SmoothStep(priority.BrakeBlendStart, priority.BrakeBlendEnd, brake);
        return requestedThrottle * MathHelper.Lerp(1f, priority.FullBrakeThrottleMultiplier, brakeT);
    }

    private float[] CalculateBrakeTorques(float brake, float handbrake, float speed, float steer)
    {
        if (brake <= 0.01f && handbrake <= 0.01f)
        {
            return [0f, 0f, 0f, 0f];
        }

        BrakeSystemParameters brakes = _parameters.Brakes;
        float linePressure = MathF.Max(0f, brake) * brakes.MaxLinePressurePa;
        float rawFrontTorquePerWheel = brakes.Front.TorqueAtPressure(linePressure);
        float rawRearTorquePerWheel = brakes.Rear.TorqueAtPressure(linePressure);
        (float frontTorquePerWheel, float rearTorquePerWheel) = ApplyBrakeBiasLimit(
            rawFrontTorquePerWheel,
            rawRearTorquePerWheel,
            brakes.BrakeBiasFront);
        float handbrakeRearTorquePerWheel = brakes.HandbrakeRearTorqueNm * MathF.Max(0f, handbrake);

        if (_digitalBrakeAssistActive && brake > 0.01f)
        {
            DigitalBrakeAssistParameters assist = _engineParameters.DigitalBrakeAssist;
            float speedT = SmoothStep(assist.SpeedBlendStartMetersPerSecond, assist.SpeedBlendEndMetersPerSecond, speed);
            float steerT = SmoothStep(assist.SteeringBlendStart, assist.SteeringBlendEnd, steer);
            float trailBrakeT = steerT * speedT;
            frontTorquePerWheel *= MathHelper.Lerp(1f, assist.TrailBrakeFrontTorqueMultiplier, trailBrakeT);
            rearTorquePerWheel *= MathHelper.Lerp(1f, assist.TrailBrakeRearTorqueMultiplier, trailBrakeT);
        }

        return
        [
            frontTorquePerWheel,
            frontTorquePerWheel,
            rearTorquePerWheel + handbrakeRearTorquePerWheel,
            rearTorquePerWheel + handbrakeRearTorquePerWheel
        ];
    }

    private static (float FrontTorquePerWheel, float RearTorquePerWheel) ApplyBrakeBiasLimit(
        float rawFrontTorquePerWheel,
        float rawRearTorquePerWheel,
        float brakeBiasFront)
    {
        float frontBias = MathHelper.Clamp(brakeBiasFront, 0.35f, 0.9f);
        float frontTotal = rawFrontTorquePerWheel * 2f;
        float rearTotal = rawRearTorquePerWheel * 2f;
        float total = frontTotal + rearTotal;
        if (total <= 0.001f)
        {
            return (0f, 0f);
        }

        float currentFrontShare = frontTotal / total;
        if (currentFrontShare < frontBias)
        {
            float targetRearTotal = frontTotal * (1f - frontBias) / frontBias;
            rearTotal = MathF.Min(rearTotal, targetRearTotal);
        }
        else if (currentFrontShare > frontBias)
        {
            float targetFrontTotal = rearTotal * frontBias / (1f - frontBias);
            frontTotal = MathF.Min(frontTotal, targetFrontTotal);
        }

        return (frontTotal * 0.5f, rearTotal * 0.5f);
    }

    private float ApplyAbs(
        WheelRuntimeState wheel,
        float requestedBrakeTorqueNm,
        float slipRatio,
        float wheelLongitudinalVelocity,
        float dt)
    {
        AbsParameters abs = _parameters.Brakes.Abs;
        if (requestedBrakeTorqueNm <= 0.1f)
        {
            wheel.AbsPressureRatio = 1f;
            wheel.AbsActive = false;
            return 0f;
        }

        bool enabled = abs.Enabled;
        float targetSlipRatio = abs.TargetSlipRatio;
        float releaseSlipRatio = abs.ReleaseSlipRatio;
        float applyRate = abs.ApplyRatePerSecond;
        float releaseRate = abs.ReleaseRatePerSecond;
        float minimumSpeed = abs.MinimumSpeedMetersPerSecond;
        float minimumPressureRatio = abs.MinimumPressureRatio;

        if (_digitalBrakeAssistActive)
        {
            enabled = true;
            DigitalBrakeAssistParameters assist = _engineParameters.DigitalBrakeAssist;
            targetSlipRatio = assist.AbsTargetSlipRatio;
            releaseSlipRatio = assist.AbsReleaseSlipRatio;
            applyRate = assist.AbsApplyRatePerSecond;
            releaseRate = assist.AbsReleaseRatePerSecond;
            minimumSpeed = assist.AbsMinimumSpeedMetersPerSecond;
            minimumPressureRatio = assist.AbsMinimumPressureRatio;
        }

        if (!enabled || MathF.Abs(wheelLongitudinalVelocity) < minimumSpeed)
        {
            wheel.AbsPressureRatio = 1f;
            wheel.AbsActive = false;
            return requestedBrakeTorqueNm;
        }

        if (slipRatio < releaseSlipRatio)
        {
            wheel.AbsPressureRatio -= releaseRate * dt;
            wheel.AbsActive = true;
        }
        else if (slipRatio < targetSlipRatio)
        {
            wheel.AbsPressureRatio -= releaseRate * 0.45f * dt;
            wheel.AbsActive = true;
        }
        else
        {
            wheel.AbsPressureRatio += applyRate * dt;
            wheel.AbsActive = false;
        }

        wheel.AbsPressureRatio = MathHelper.Clamp(
            wheel.AbsPressureRatio,
            MathHelper.Clamp(minimumPressureRatio, 0f, 1f),
            1f);
        return requestedBrakeTorqueNm * wheel.AbsPressureRatio;
    }

    private SteeringAngles CalculateSteeringAngles(float steerInput, float speedMetersPerSecond, float brake)
    {
        float steeringWheelHalfLockRadians = MathHelper.ToRadians(_parameters.SteeringWheelLockDegrees * 0.5f);
        float ratioLimitedRoadAngle = steeringWheelHalfLockRadians / MathF.Max(1f, _parameters.SteeringRatio);
        float mechanicalMaxAngle = MathF.Min(_parameters.MaxSteerAngleRadians, ratioLimitedRoadAngle);
        float assistT = CalculateSteeringSpeedT(speedMetersPerSecond);
        float lockMultiplier = MathHelper.Lerp(1f, MathHelper.Clamp(_parameters.SteeringHighSpeedLockMultiplier, 0.1f, 1f), assistT);
        float assistedMaxAngle = mechanicalMaxAngle * lockMultiplier;
        float speedMatchedMaxAngle = CalculateSpeedMatchedSteeringAngle(mechanicalMaxAngle, speedMetersPerSecond);
        float inputFilteredMaxAngle = MathF.Min(assistedMaxAngle, speedMatchedMaxAngle);
        SteeringAssistParameters steeringAssist = _engineParameters.SteeringAssist;
        float brakeAuthorityT =
            SmoothStep(steeringAssist.BrakeAngleBoostBrakeStart, steeringAssist.BrakeAngleBoostBrakeEnd, brake) *
            SmoothStep(
                steeringAssist.BrakeAngleBoostSpeedStartMetersPerSecond,
                steeringAssist.BrakeAngleBoostSpeedEndMetersPerSecond,
                speedMetersPerSecond);
        inputFilteredMaxAngle = MathF.Min(
            mechanicalMaxAngle,
            inputFilteredMaxAngle * MathHelper.Lerp(1f, steeringAssist.BrakeAngleBoostMultiplier, brakeAuthorityT));
        // With the current MonoGame camera/world convention, a visual right turn is negative yaw.
        float baseAngle = -steerInput * inputFilteredMaxAngle;
        if (MathF.Abs(baseAngle) < 0.0001f)
        {
            return new SteeringAngles(0f, 0f);
        }

        float sign = MathF.Sign(baseAngle);
        float radius = MathF.Max(_parameters.FrontTrackMeters * 0.65f, _parameters.WheelbaseMeters / MathF.Tan(MathF.Abs(baseAngle)));
        float inner = MathF.Atan(_parameters.WheelbaseMeters / MathF.Max(0.1f, radius - _parameters.FrontTrackMeters * 0.5f)) * sign;
        float outer = MathF.Atan(_parameters.WheelbaseMeters / (radius + _parameters.FrontTrackMeters * 0.5f)) * sign;
        float ackermannBlend = MathHelper.Clamp(_parameters.AckermannPercent / 100f, 0f, 1f);

        return sign > 0f
            ? new SteeringAngles(MathHelper.Lerp(baseAngle, outer, ackermannBlend), MathHelper.Lerp(baseAngle, inner, ackermannBlend))
            : new SteeringAngles(MathHelper.Lerp(baseAngle, inner, ackermannBlend), MathHelper.Lerp(baseAngle, outer, ackermannBlend));
    }

    private float CalculateSpeedMatchedSteeringAngle(float mechanicalMaxAngle, float speedMetersPerSecond)
    {
        float referenceSpeed = MathF.Max(2.0f, _parameters.SteeringLowSpeedReferenceMetersPerSecond);
        float speed = MathF.Max(referenceSpeed, speedMetersPerSecond);
        float targetLateralAcceleration = MathF.Max(0.1f, _parameters.SteeringTargetLateralAccelerationG) * Gravity;
        float curvatureAngle = MathF.Atan(_parameters.WheelbaseMeters * targetLateralAcceleration / MathF.Max(1f, speed * speed));
        SteeringAssistParameters steeringAssist = _engineParameters.SteeringAssist;
        float highSpeedSlipT = SmoothStep(
            steeringAssist.SpeedMatchedSlipStartMetersPerSecond,
            steeringAssist.SpeedMatchedSlipEndMetersPerSecond,
            speed);
        float slipAllowance = _parameters.FrontTyres.LateralPeakSlipAngleRadians *
                              MathHelper.Clamp(_parameters.SteeringPeakSlipAngleFraction, 0f, 1.2f) *
                              MathHelper.Lerp(
                                  steeringAssist.LowSpeedSlipAllowanceMultiplier,
                                  steeringAssist.HighSpeedSlipAllowanceMultiplier,
                                  highSpeedSlipT);
        float lowSpeedMinimumAngle = MathHelper.Clamp(_parameters.SteeringMinimumHighSpeedAngleRadians, 0.01f, mechanicalMaxAngle);
        float highSpeedMinimumAngle = MathHelper.Clamp(
            MathHelper.ToRadians(steeringAssist.HighSpeedMinimumRoadWheelAngleDegrees),
            0.01f,
            mechanicalMaxAngle);
        float minimumAngle = MathHelper.Lerp(lowSpeedMinimumAngle, highSpeedMinimumAngle, highSpeedSlipT);
        return MathHelper.Clamp(curvatureAngle + slipAllowance, minimumAngle, mechanicalMaxAngle);
    }

    private float CalculateSteeringSpeedT(float speedMetersPerSecond)
    {
        float assistRange = MathF.Max(0.1f, _parameters.SteeringReducedLockSpeedMetersPerSecond - _parameters.SteeringFullLockSpeedMetersPerSecond);
        return MathHelper.Clamp((speedMetersPerSecond - _parameters.SteeringFullLockSpeedMetersPerSecond) / assistRange, 0f, 1f);
    }

    private void UpdateSteeringInput(float targetInput, float speedMetersPerSecond, float brake, float dt)
    {
        float delta = targetInput - _filteredSteerInput;
        if (MathF.Abs(delta) <= 0.0001f)
        {
            return;
        }

        bool currentInputHasDirection = MathF.Abs(_filteredSteerInput) > 0.0001f;
        bool returningToCenter = currentInputHasDirection &&
                                 (MathF.Abs(targetInput) < MathF.Abs(_filteredSteerInput) ||
                                  MathF.Sign(targetInput) != MathF.Sign(_filteredSteerInput));
        float baseRate = returningToCenter
            ? _parameters.SteeringReturnRatePerSecond
            : _parameters.SteeringInputRatePerSecond;
        float highSpeedMultiplier = returningToCenter
            ? _parameters.SteeringHighSpeedReturnRateMultiplier
            : _parameters.SteeringHighSpeedInputRateMultiplier;
        SteeringAssistParameters steeringAssist = _engineParameters.SteeringAssist;
        float brakeAuthorityT =
            SmoothStep(steeringAssist.InputBrakeAuthorityStart, steeringAssist.InputBrakeAuthorityEnd, brake) *
            SmoothStep(
                steeringAssist.InputBrakeAuthoritySpeedStartMetersPerSecond,
                steeringAssist.InputBrakeAuthoritySpeedEndMetersPerSecond,
                speedMetersPerSecond);
        float brakingMultiplierFloor = returningToCenter
            ? steeringAssist.BrakingReturnMultiplierFloor
            : steeringAssist.BrakingInputMultiplierFloor;
        highSpeedMultiplier = MathHelper.Lerp(
            highSpeedMultiplier,
            MathF.Max(highSpeedMultiplier, brakingMultiplierFloor),
            brakeAuthorityT);
        float rate = baseRate * MathHelper.Lerp(
            1f,
            MathHelper.Clamp(highSpeedMultiplier, 0.05f, 1.15f),
            CalculateSteeringSpeedT(speedMetersPerSecond));
        rate *= MathHelper.Lerp(1f, steeringAssist.BrakingInputRateBoost, brakeAuthorityT);
        float maxStep = MathF.Max(0f, rate) * dt;
        _filteredSteerInput += MathHelper.Clamp(delta, -maxStep, maxStep);
    }

    private float CalculateSteeringBrakeAuthority(float requestedBrake, float appliedBrake)
    {
        float authority = MathF.Max(requestedBrake, appliedBrake);
        if (_recentBrakeSteeringBoostSeconds <= 0f)
        {
            return authority;
        }

        SteeringAssistParameters steeringAssist = _engineParameters.SteeringAssist;
        float recentBrakeT = MathHelper.Clamp(
            _recentBrakeSteeringBoostSeconds / MathF.Max(0.001f, steeringAssist.RecentBrakeBoostSeconds),
            0f,
            1f);
        return MathF.Max(authority, steeringAssist.RecentBrakeAuthority * recentBrakeT);
    }

    private void UpdateRecentBrakeSteeringBoost(float requestedBrake, float appliedBrake, float dt)
    {
        SteeringAssistParameters steeringAssist = _engineParameters.SteeringAssist;
        if (MathF.Max(requestedBrake, appliedBrake) > steeringAssist.RecentBrakeBoostThreshold)
        {
            _recentBrakeSteeringBoostSeconds = steeringAssist.RecentBrakeBoostSeconds;
            return;
        }

        _recentBrakeSteeringBoostSeconds = MathF.Max(0f, _recentBrakeSteeringBoostSeconds - dt);
    }

    private void UpdateBrakeInput(float targetInput, float dt)
    {
        targetInput = MathHelper.Clamp(targetInput, 0f, 1f);
        float delta = targetInput - _filteredBrakeInput;
        if (MathF.Abs(delta) <= 0.0001f)
        {
            return;
        }

        float rate = delta > 0f
            ? _parameters.Brakes.PressureRiseRatePerSecond
            : _parameters.Brakes.PressureReleaseRatePerSecond;
        if (rate <= 0f)
        {
            _filteredBrakeInput = targetInput;
            return;
        }

        float maxStep = rate * dt;
        _filteredBrakeInput += MathHelper.Clamp(delta, -maxStep, maxStep);
        if (targetInput <= 0.001f && _filteredBrakeInput <= 0.001f)
        {
            _filteredBrakeInput = 0f;
        }
    }

    private float CalculateDriveCrankTorque(float rpm, float throttle, float forwardSpeed, float dt)
    {
        EnginePowerUnitState enginePower = AdvanceEnginePower(rpm, throttle, forwardSpeed, dt);
        if (enginePower.Enabled)
        {
            ApplyEnginePowerCrankState(enginePower, dt: dt);
            return enginePower.DriveTorqueNm;
        }

        return _parameters.TorqueAtRpm(rpm) *
               MathHelper.Clamp(throttle, 0f, 1f) *
               State.LimiterTorqueMultiplier;
    }

    private EnginePowerUnitState AdvanceEnginePower(
        float rpm,
        float throttle,
        float forwardSpeed,
        float dt,
        float clutchEngagement = 1f,
        bool forceNeutral = false,
        float transmissionRpm = -1f)
    {
        if (!_enginePowerUnit.Enabled)
        {
            return EnginePowerUnitState.Disabled;
        }

        float limiter = MathHelper.Clamp(
            MathF.Max(State.RevLimiterBounceIntensity, State.RevLimiterActive ? 0.55f : 0f),
            0f,
            1f);
        float overrun = CalculateEnginePowerOverrun(throttle, rpm, forwardSpeed);
        float shock = MathHelper.Clamp(
            MathF.Max(State.ShiftKickIntensity, State.PowertrainShockIntensity),
            0f,
            1f);
        int gear = forceNeutral ? 0 : State.Gear;
        float gearRatio = forceNeutral ? 0f : GetGearRatio(gear);
        float requestTransmissionRpm = forceNeutral || gearRatio <= 0f
            ? 0f
            : transmissionRpm >= 0f
                ? transmissionRpm
                : CalculateDrivenTransmissionRpm(gearRatio);
        EnginePowerUnitPhase phase = CalculateEnginePowerUnitPhase(throttle, forwardSpeed, forceNeutral);
        float phaseProgress = CalculateEnginePowerUnitPhaseProgress(phase);
        float drivenSlipRatio = forceNeutral ? 0f : CalculateDrivenAverageDriveSlipRatio();
        float clutchSlipRpm = rpm - requestTransmissionRpm;
        return _enginePowerUnit.Advance(new EnginePowerUnitRequest(
            rpm,
            throttle,
            forwardSpeed,
            limiter,
            State.LimiterTorqueMultiplier,
            overrun,
            shock,
            gear,
            gearRatio,
            requestTransmissionRpm,
            _parameters.FinalDriveRatio,
            _parameters.WheelRadiusMeters,
            clutchEngagement,
            phase,
            phaseProgress,
            drivenSlipRatio,
            clutchSlipRpm,
            dt));
    }

    private EnginePowerUnitPhase CalculateEnginePowerUnitPhase(float throttle, float forwardSpeed, bool forceNeutral)
    {
        if (forceNeutral || State.Gear == 0)
        {
            return EnginePowerUnitPhase.NeutralHold;
        }

        if (_shiftTimerSeconds > 0f)
        {
            return EnginePowerUnitPhase.Shifting;
        }

        if (_launchClutchTimerSeconds > 0f)
        {
            return EnginePowerUnitPhase.Launch;
        }

        if (IsMechanicalOverRevForced(forwardSpeed) || throttle <= 0.01f)
        {
            return EnginePowerUnitPhase.EngineBraking;
        }

        return EnginePowerUnitPhase.Driving;
    }

    private float CalculateEnginePowerUnitPhaseProgress(EnginePowerUnitPhase phase)
    {
        return phase switch
        {
            EnginePowerUnitPhase.Launch => MathHelper.Clamp(
                _launchClutchTimerSeconds / (_preRevLaunchActive ? 2.85f : 2.05f),
                0f,
                1f),
            EnginePowerUnitPhase.Shifting => _shiftDurationSeconds > 0f
                ? 1f - MathHelper.Clamp(_shiftTimerSeconds / _shiftDurationSeconds, 0f, 1f)
                : 1f,
            _ => 1f
        };
    }

    private float CalculateEnginePowerOverrun(float throttle, float rpm, float forwardSpeed)
    {
        return (1f - SmoothStep(0.05f, 0.25f, throttle)) *
               SmoothStep(2600f, MathF.Max(3200f, _parameters.RedlineRpm), rpm) *
               SmoothStep(2f, 11f, MathF.Abs(forwardSpeed));
    }

    private void PublishEnginePowerState()
    {
        EnginePowerUnitState enginePower = _enginePowerUnit.State;
        bool simActive = enginePower.Enabled && enginePower.UsesEngineSimulator;
        State.EnginePowerUnitActive = simActive;
        State.EnginePowerUnitDriveTorqueNm = simActive ? enginePower.DriveTorqueNm : 0f;
        State.EnginePowerUnitEngineDriveTorqueNm = simActive ? enginePower.EngineDriveTorqueNm : 0f;
        State.EnginePowerUnitRawTorqueNm = simActive ? enginePower.RawIndicatedTorqueNm : 0f;
        State.EnginePowerUnitVtecBlend = simActive ? enginePower.VtecBlend : 0f;
        State.EnginePowerUnitVtecKickIntensity = simActive ? enginePower.VtecKickIntensity : 0f;
        State.EnginePowerUnitLoad = simActive ? enginePower.Load : 0f;
        State.EnginePowerUnitFuelCutBlend = simActive ? enginePower.FuelCutBlend : 0f;
        State.EnginePowerUnitCrankRpm = simActive ? enginePower.CrankRpm : 0f;
        State.EnginePowerUnitCrankPhaseDegrees = simActive ? enginePower.CrankPhaseDegrees : 0f;
        State.EnginePowerUnitAfterfireBlend = simActive ? enginePower.AfterfireBlend : 0f;
        State.EnginePowerUnitTransmissionRpm = simActive ? enginePower.TransmissionRpm : 0f;
        State.EnginePowerUnitClutchTorqueNm = simActive ? enginePower.ClutchTorqueNm : 0f;
        State.EnginePowerUnitCrankFrictionTorqueNm = simActive ? enginePower.CrankFrictionTorqueNm : 0f;
        State.EnginePowerUnitReferenceDriveTorqueNm = simActive ? enginePower.ReferenceDriveTorqueNm : 0f;
        State.EnginePowerUnitCalibratedDriveTorqueNm = simActive ? enginePower.CalibratedDriveTorqueNm : 0f;
        State.EnginePowerUnitGasAuthority = simActive ? enginePower.GasAuthority : 0f;
        State.EnginePowerUnitFullThrottleGasTorqueNm = simActive ? enginePower.FullThrottleGasTorqueNm : 0f;
    }

    private void ApplyEnginePowerCrankState(EnginePowerUnitState enginePower, bool allowMechanicalOverRev = false, float dt = 0f)
    {
        if (!enginePower.Enabled || !enginePower.OwnsDriveline || enginePower.CrankRpm <= 0f)
        {
            return;
        }

        float ceiling = MathF.Max(_parameters.RedlineRpm, _parameters.IdleRpm + 1000f);
        float targetRpm = MathHelper.Clamp(enginePower.CrankRpm, 650f, ceiling);
        if (dt > 0f &&
            _enginePowerShiftHandoffSmoothSeconds > 0f &&
            _launchClutchTimerSeconds <= 0f &&
            !State.MechanicalOverRevActive)
        {
            float maxDrop = 1600f * MathHelper.Clamp(dt, 0f, 0.05f);
            float maxRise = 12000f * MathHelper.Clamp(dt, 0f, 0.05f);
            targetRpm = State.Rpm + MathHelper.Clamp(targetRpm - State.Rpm, -maxDrop, maxRise);
        }

        State.Rpm = targetRpm;
        State.ClutchSlipRpm = State.Rpm - MathF.Max(0f, enginePower.TransmissionRpm);
    }

    private float CalculateTotalDriveTorque(float throttle, float forwardSpeed, float dt)
    {
        if (_shiftTimerSeconds > 0f)
        {
            AdvanceEnginePowerDuringShift(throttle, forwardSpeed, dt);
            return 0f;
        }

        if (IsMechanicalOverRevForced(forwardSpeed) || throttle <= 0.01f)
        {
            return CalculateEngineBrakingTorque(forwardSpeed, dt);
        }

        if (State.Gear < 0 && forwardSpeed < -_engineParameters.VehicleSafety.MaximumReverseSpeedMetersPerSecond)
        {
            return 0f;
        }

        if (State.Gear > 0 && forwardSpeed > _engineParameters.VehicleSafety.MaximumForwardSpeedMetersPerSecond)
        {
            return 0f;
        }

        float ratio = GetCurrentGearRatio();
        if (ratio <= 0.0001f)
        {
            return 0f;
        }

        float direction = State.Gear < 0 ? -1f : 1f;
        float crankTorque = MathF.Min(
            CalculateDriveCrankTorque(State.Rpm, throttle, forwardSpeed, dt),
            _parameters.ClutchTorqueCapacityNm);
        float shiftKickTorqueMultiplier = 1f + CalculateShiftKickEnvelope() * _shiftKickSeverity * 0.16f;
        crankTorque *= shiftKickTorqueMultiplier;
        return direction * crankTorque * ratio * _parameters.FinalDriveRatio * _parameters.DrivetrainEfficiency;
    }

    private void AdvanceEnginePowerDuringShift(float throttle, float forwardSpeed, float dt)
    {
        if (!_enginePowerUnit.Enabled)
        {
            return;
        }

        // Keep combustion, limiter, VTEC and engine power-unit telemetry continuous while the clutch is open.
        _ = AdvanceEnginePower(
            MathF.Max(500f, State.Rpm),
            throttle,
            forwardSpeed,
            dt,
            clutchEngagement: 0f);
    }

    private bool ShouldUseLaunchClutch(float throttle, float forwardSpeed)
    {
        if (_shiftTimerSeconds > 0f || State.Gear <= 0 || throttle <= 0.01f)
        {
            return false;
        }

        float ratio = GetCurrentGearRatio();
        if (ratio <= 0.0001f)
        {
            return false;
        }

        float speed = MathF.Abs(forwardSpeed);
        bool launchAlreadyStarted = _launchClutchTimerSeconds > 0f;
        float launchDurationLimit = _preRevLaunchActive ? 2.85f : 2.05f;
        float launchSpeedLimit = _preRevLaunchActive ? 18.5f : 13.2f;
        bool startingFromRest = speed < 1.2f;
        if (!launchAlreadyStarted && !startingFromRest)
        {
            return false;
        }

        if (launchAlreadyStarted &&
            (_launchClutchTimerSeconds > launchDurationLimit || speed > launchSpeedLimit))
        {
            return false;
        }

        float transmissionRpm = CalculateDrivenTransmissionRpm(ratio);
        float launchTargetRpm = MathHelper.Clamp(_parameters.LaunchSlipTargetRpm, _parameters.IdleRpm + 400f, _parameters.RedlineRpm);
        float launchPowerBandRpm = MathHelper.Clamp(
            MathF.Max(launchTargetRpm, _parameters.RedlineRpm * 0.76f),
            _parameters.IdleRpm + 1400f,
            _parameters.RedlineRpm - 350f);
        float drivenSlipRatio = CalculateDrivenAverageDriveSlipRatio();
        if ((launchAlreadyStarted || startingFromRest) &&
            throttle > 0.45f &&
            speed < 13.6f &&
            (_launchClutchTimerSeconds < 1.35f ||
             (launchAlreadyStarted &&
              _launchClutchTimerSeconds < launchDurationLimit &&
               transmissionRpm < launchPowerBandRpm * 0.95f &&
               drivenSlipRatio < 0.55f) ||
             (launchAlreadyStarted &&
              _launchClutchTimerSeconds < 1.75f &&
              drivenSlipRatio > 0.18f) ||
             (State.Rpm < launchTargetRpm * 0.72f &&
              transmissionRpm < launchTargetRpm * 0.52f &&
              _launchClutchTimerSeconds < 1.30f)))
        {
            return true;
        }

        if (throttle <= 0.45f)
        {
            return false;
        }

        float clutchSlipRpm = State.Rpm - transmissionRpm;
        if (clutchSlipRpm <= 160f)
        {
            return false;
        }

        return speed < launchSpeedLimit && _launchClutchTimerSeconds < launchDurationLimit;
    }

    private float CalculateLaunchClutchDriveTorque(float throttle, float forwardSpeed, float dt)
    {
        float ratio = GetCurrentGearRatio();
        if (ratio <= 0.0001f)
        {
            return 0f;
        }

        float rpm = MathF.Max(500f, State.Rpm);
        float transmissionRpm = CalculateDrivenTransmissionRpm(ratio);
        float clutchSlipRpm = rpm - transmissionRpm;
        float launchTargetRpm = MathHelper.Clamp(_parameters.LaunchSlipTargetRpm, _parameters.IdleRpm + 400f, _parameters.RedlineRpm);
        float launchPowerBandRpm = MathHelper.Clamp(
            MathF.Max(launchTargetRpm, _parameters.RedlineRpm * 0.76f),
            _parameters.IdleRpm + 1400f,
            _parameters.RedlineRpm - 350f);
        if (_launchClutchTimerSeconds <= 0.0001f)
        {
            _preRevLaunchActive = rpm > launchTargetRpm * 0.78f;
        }

        float launchSyncT = SmoothStep(launchTargetRpm * 0.78f, launchTargetRpm * 1.02f, transmissionRpm);
        float engagement = UpdateAutomaticLaunchClutchEngagement(throttle, forwardSpeed, rpm, transmissionRpm, dt);
        float lockCapacityT = SmoothStep(0.20f, 0.92f, _launchClutchTimerSeconds);
        float clutchCapacityNm = MathF.Max(0f, _parameters.ClutchTorqueCapacityNm) *
                                 engagement *
                                 MathHelper.Lerp(0.82f, 1.26f, lockCapacityT);
        float drivenSlipRatio = CalculateDrivenAverageDriveSlipRatio();
        float launchWheelSpinReliefT = SmoothStep(0.10f, 0.40f, drivenSlipRatio) *
                                       (1f - SmoothStep(3.0f, 9.0f, MathF.Abs(forwardSpeed)));
        clutchCapacityNm *= MathHelper.Lerp(1f, 0.42f, launchWheelSpinReliefT);

        float slipDirection;
        if (throttle > 0.05f && clutchSlipRpm <= 0f)
        {
            slipDirection = 0f;
        }
        else if (MathF.Abs(clutchSlipRpm) < 280f)
        {
            slipDirection = clutchSlipRpm / 280f;
        }
        else
        {
            slipDirection = MathF.Sign(clutchSlipRpm);
        }

        float solverClutchEngagement = engagement;
        if (_enginePowerUnit.OwnsDriveline && _preRevLaunchActive)
        {
            float rpmHealthT = SmoothStep(launchTargetRpm * 0.94f, launchTargetRpm * 1.08f, rpm);
            float transmissionHealthT = SmoothStep(launchTargetRpm * 0.55f, launchTargetRpm * 1.00f, transmissionRpm);
            float rpmOnlyAllowanceT = rpmHealthT * SmoothStep(launchTargetRpm * 0.35f, launchTargetRpm * 0.70f, transmissionRpm);
            float highRpmBiteT = SmoothStep(launchTargetRpm * 1.02f, _parameters.RedlineRpm * 0.94f, rpm);
            float timedBiteT = SmoothStep(0.32f, 0.95f, _launchClutchTimerSeconds);
            float antiBogReleaseT = MathF.Max(MathF.Max(highRpmBiteT, timedBiteT), MathF.Max(rpmOnlyAllowanceT, transmissionHealthT));
            solverClutchEngagement *= MathHelper.Lerp(0.50f, 1f, antiBogReleaseT);
        }

        EnginePowerUnitState enginePower = AdvanceEnginePower(
            rpm,
            throttle,
            forwardSpeed,
            dt,
            solverClutchEngagement,
            transmissionRpm: transmissionRpm);
        if (enginePower.Enabled && enginePower.OwnsDriveline)
        {
            ApplyEnginePowerCrankState(enginePower);
            if (_preRevLaunchActive &&
                throttle > 0.45f &&
                _launchClutchTimerSeconds < 2.85f &&
                transmissionRpm < launchPowerBandRpm * 1.02f)
            {
                float floorReleaseT = MathF.Max(
                    SmoothStep(0.60f, 1.55f, _launchClutchTimerSeconds) * 0.85f,
                    SmoothStep(launchTargetRpm * 0.34f, launchTargetRpm * 0.82f, transmissionRpm));
                float protectiveFloorRpm = launchTargetRpm * MathHelper.Lerp(0.90f, 0.66f, floorReleaseT);
                float roadCatchupFloorRpm = transmissionRpm + MathHelper.Lerp(2200f, 450f, floorReleaseT);
                float launchFloorRpm = MathHelper.Clamp(
                    MathF.Max(protectiveFloorRpm, roadCatchupFloorRpm),
                    launchTargetRpm * 0.92f,
                    launchPowerBandRpm);
                float launchFloorTextureT = throttle *
                                            SmoothStep(0.18f, 0.72f, _launchClutchTimerSeconds) *
                                            (1f - SmoothStep(1.55f, 2.45f, _launchClutchTimerSeconds)) *
                                            SmoothStep(0.10f, 0.65f, drivenSlipRatio);
                float launchFloorTextureRpm = (MathF.Sin(_launchClutchTimerSeconds * 54f) +
                                               MathF.Sin(_launchClutchTimerSeconds * 113f + 0.7f) * 0.45f) *
                                              55f *
                                              launchFloorTextureT;
                State.Rpm = MathF.Max(State.Rpm, launchFloorRpm + launchFloorTextureRpm);
                State.ClutchSlipRpm = State.Rpm - transmissionRpm;
            }

            float coupledLowSpeedT = 1f - SmoothStep(4.0f, 12.0f, MathF.Abs(forwardSpeed));
            float coupledSlipT = SmoothStep(0.22f, 0.95f, State.AverageSlipRatio);
            float coupledRpmDeficitT = 1f - SmoothStep(launchTargetRpm * 0.78f, launchTargetRpm * 1.02f, State.Rpm);
            float coupledLaunchShock = throttle *
                                       coupledLowSpeedT *
                                       MathF.Max(coupledSlipT, coupledRpmDeficitT * 0.38f) *
                                       SmoothStep(0.12f, 0.68f, _launchClutchEngagement) *
                                       0.30f;
            State.PowertrainShockIntensity = MathHelper.Clamp(MathF.Max(State.ShiftKickIntensity, coupledLaunchShock), 0f, 1f);
            if (_preRevLaunchActive &&
                throttle > 0.55f &&
                _launchClutchTimerSeconds > 0.24f &&
                _launchClutchTimerSeconds < 1.35f &&
                MathF.Abs(State.ClutchSlipRpm) > 1200f &&
                CalculateDrivenAverageDriveSlipRatio() < 0.52f)
            {
                ApplyLaunchClutchHandoffWheelSpin(throttle, 0.22f);
            }

            float coupledDirection = State.Gear < 0 ? -1f : 1f;
            return coupledDirection * enginePower.DriveTorqueNm * ratio * _parameters.FinalDriveRatio * _parameters.DrivetrainEfficiency;
        }

        float engineTorqueNm = enginePower.Enabled
            ? enginePower.DriveTorqueNm
            : _parameters.TorqueAtRpm(rpm) * throttle * State.LimiterTorqueMultiplier;
        float idleControlTorqueNm = rpm < _parameters.IdleRpm + 120f
            ? MathHelper.Clamp((_parameters.IdleRpm + 120f - rpm) * 0.08f, 0f, 34f)
            : 0f;
        float frictionTorqueNm = CalculateEngineInternalDragTorque(rpm, throttle, 0.82f);
        float clutchTorqueNm = clutchCapacityNm * MathHelper.Clamp(slipDirection, -1f, 1f);
        if (clutchTorqueNm > 0f)
        {
            float antiBogStartRpm = MathHelper.Clamp(launchTargetRpm * 0.82f, _parameters.IdleRpm + 900f, launchTargetRpm * 0.92f);
            float antiBogEndRpm = MathHelper.Clamp(MathF.Max(launchTargetRpm * 1.04f, launchPowerBandRpm), antiBogStartRpm + 1f, _parameters.RedlineRpm);
            float bogProtectionT = 1f - SmoothStep(antiBogStartRpm, antiBogEndRpm, rpm);
            float reserveTorqueNm = MathHelper.Lerp(18f, 2f, SmoothStep(antiBogStartRpm, antiBogEndRpm, rpm));
            float protectedTorqueNm = MathF.Min(
                clutchTorqueNm,
                MathF.Max(0f, engineTorqueNm + idleControlTorqueNm - frictionTorqueNm - reserveTorqueNm));
            clutchTorqueNm = MathHelper.Lerp(clutchTorqueNm, protectedTorqueNm, bogProtectionT * MathHelper.Lerp(0.72f, 1f, launchWheelSpinReliefT));

            float tyreBiteDemandT = (1f - SmoothStep(0.05f, 0.14f, drivenSlipRatio)) *
                                    SmoothStep(0.28f, 0.68f, _launchClutchTimerSeconds) *
                                    (1f - launchSyncT) *
                                    MathHelper.Clamp(throttle, 0f, 1f);
            float biteTorqueNm = MathF.Min(
                clutchCapacityNm,
                MathF.Max(clutchTorqueNm, engineTorqueNm * MathHelper.Lerp(0.72f, 0.92f, tyreBiteDemandT)));
            clutchTorqueNm = MathHelper.Lerp(clutchTorqueNm, biteTorqueNm, tyreBiteDemandT * 0.42f);
        }

        float netEngineTorqueNm = engineTorqueNm + idleControlTorqueNm - frictionTorqueNm - clutchTorqueNm;
        float launchHopT = throttle *
                           SmoothStep(0.12f, 0.42f, drivenSlipRatio) *
                           SmoothStep(900f, 4800f, MathF.Max(0f, clutchSlipRpm)) *
                           (1f - SmoothStep(1.55f, 2.20f, _launchClutchTimerSeconds));
        if (launchHopT > 0.001f)
        {
            float hopTorqueNm = (MathF.Sin(_launchClutchTimerSeconds * 78f) +
                                 MathF.Sin(_launchClutchTimerSeconds * 143f + 0.8f) * 0.55f) *
                                18f *
                                launchHopT;
            netEngineTorqueNm += hopTorqueNm;
        }

        float rpmDelta = netEngineTorqueNm / MathF.Max(0.05f, _parameters.EngineRotationalInertiaKgM2) * (60f / MathF.Tau) * dt;
        float maximumRpm = _parameters.RedlineRpm;
        float minimumRpm = 650f;
        if (throttle > 0.45f &&
            _launchClutchTimerSeconds > 0f &&
            _launchClutchTimerSeconds < 2.05f &&
            transmissionRpm < launchPowerBandRpm * 1.02f)
        {
            float speedReleaseT = SmoothStep(7.0f, 13.2f, MathF.Abs(forwardSpeed));
            float timeReleaseT = SmoothStep(1.35f, 2.05f, _launchClutchTimerSeconds);
            float releaseT = MathF.Max(timeReleaseT, speedReleaseT);
            float powerBandFloorRpm = launchPowerBandRpm * MathHelper.Lerp(1.02f, 0.96f, releaseT);
            float roadCoupledRpm = CalculateRoadSpeedRpmForGear(State.Gear, forwardSpeed, _parameters.RedlineRpm);
            float roadCatchupFloorRpm = MathF.Min(
                _parameters.RedlineRpm - 120f,
                roadCoupledRpm + MathHelper.Lerp(2800f, 900f, releaseT));
            minimumRpm = MathF.Max(minimumRpm, MathF.Max(powerBandFloorRpm, roadCatchupFloorRpm));
        }

        State.Rpm = MathHelper.Clamp(rpm + rpmDelta, minimumRpm, maximumRpm);
        State.ClutchSlipRpm = State.Rpm - transmissionRpm;
        float lowSpeedT = 1f - SmoothStep(4.0f, 12.0f, MathF.Abs(forwardSpeed));
        float slipT = SmoothStep(0.22f, 0.95f, State.AverageSlipRatio);
        float rpmDeficitT = 1f - SmoothStep(launchTargetRpm * 0.78f, launchTargetRpm * 1.02f, State.Rpm);
        float launchShock = throttle *
                            lowSpeedT *
                            MathF.Max(MathF.Max(slipT, launchHopT), rpmDeficitT * 0.45f) *
                            SmoothStep(0.12f, 0.68f, _launchClutchEngagement) *
                            0.30f;
        State.PowertrainShockIntensity = MathHelper.Clamp(MathF.Max(State.ShiftKickIntensity, launchShock), 0f, 1f);

        float direction = State.Gear < 0 ? -1f : 1f;
        return direction * clutchTorqueNm * ratio * _parameters.FinalDriveRatio * _parameters.DrivetrainEfficiency;
    }

    private float CalculateDrivenAverageDriveSlipRatio()
    {
        float slipTotal = 0f;
        int drivenCount = 0;
        float driveDirection = State.Gear < 0 ? -1f : 1f;
        foreach (WheelRuntimeState wheel in _wheels)
        {
            if (!_parameters.DrivenWheels.IsDriven(wheel.Corner))
            {
                continue;
            }

            slipTotal += MathF.Max(0f, wheel.SlipRatio * driveDirection);
            drivenCount++;
        }

        return drivenCount > 0
            ? slipTotal / drivenCount
            : MathF.Max(0f, State.AverageSlipRatio);
    }

    private void ApplyLaunchClutchHandoffWheelSpin(float throttle, float intensityScale = 1f)
    {
        float gearRatio = GetCurrentGearRatio();
        float drivelineRatio = MathF.Abs(gearRatio * _parameters.FinalDriveRatio);
        if (drivelineRatio <= 0.001f || State.Rpm <= _parameters.IdleRpm + 300f)
        {
            return;
        }

        float clutchSlipT = SmoothStep(500f, 2200f, MathF.Max(0f, State.ClutchSlipRpm));
        float handoffT = SmoothStep(0.22f, 0.62f, _launchClutchTimerSeconds);
        float impulseT = MathHelper.Clamp(throttle, 0f, 1f) *
                         MathHelper.Clamp(_launchClutchEngagement, 0f, 1f) *
                         clutchSlipT *
                         handoffT *
                         MathHelper.Clamp(intensityScale, 0f, 1f);
        if (impulseT <= 0.001f)
        {
            return;
        }

        float direction = State.Gear < 0 ? -1f : 1f;
        float engineMatchedWheelAngularVelocity = State.Rpm / drivelineRatio * (MathF.Tau / 60f);
        float targetAngularVelocity = engineMatchedWheelAngularVelocity * MathHelper.Lerp(0.60f, 0.78f, impulseT);
        foreach (WheelRuntimeState wheel in _wheels)
        {
            if (!_parameters.DrivenWheels.IsDriven(wheel.Corner))
            {
                continue;
            }

            float signedAngularVelocity = wheel.AngularVelocityRadiansPerSecond * direction;
            if (signedAngularVelocity < targetAngularVelocity)
            {
                float boostedAngularVelocity = MathHelper.Lerp(signedAngularVelocity, targetAngularVelocity, MathHelper.Lerp(0.08f, 0.32f, impulseT));
                wheel.AngularVelocityRadiansPerSecond = boostedAngularVelocity * direction;
            }
        }

        State.PowertrainShockIntensity = MathHelper.Clamp(
            MathF.Max(State.PowertrainShockIntensity, impulseT * 0.30f),
            0f,
            1f);
    }

    private float UpdateAutomaticLaunchClutchEngagement(
        float throttle,
        float forwardSpeed,
        float engineRpm,
        float transmissionRpm,
        float dt)
    {
        _launchClutchTimerSeconds += dt;
        float speed = MathF.Abs(forwardSpeed);
        float speedT = SmoothStep(0.25f, 5.0f, speed);
        float timeT = SmoothStep(0.04f, 0.42f, _launchClutchTimerSeconds);
        float launchTargetRpm = MathHelper.Clamp(_parameters.LaunchSlipTargetRpm, _parameters.IdleRpm + 400f, _parameters.RedlineRpm);
        float launchSyncT = SmoothStep(launchTargetRpm * 0.58f, launchTargetRpm * 0.90f, transmissionRpm);
        float timeLockT = SmoothStep(0.18f, 1.38f, _launchClutchTimerSeconds);
        float lockT = MathF.Max(launchSyncT, timeLockT);
        float rpmHealthT = SmoothStep(_parameters.IdleRpm + 800f, launchTargetRpm * 0.96f, engineRpm);
        float bitePoint = MathHelper.Clamp(_parameters.ClutchEngagementPoint, 0.25f, 0.85f);
        float initialBite = bitePoint * MathHelper.Lerp(1.06f, 1.30f, rpmHealthT);
        initialBite = MathHelper.Clamp(initialBite, 0.52f, 0.84f);
        float targetEngagement = MathHelper.Lerp(initialBite, 1f, lockT);
        if (_preRevLaunchActive && throttle > 0.75f && _launchClutchTimerSeconds < 0.85f)
        {
            float preRevDumpT = SmoothStep(launchTargetRpm * 1.03f, _parameters.RedlineRpm * 0.98f, engineRpm);
            targetEngagement = MathF.Max(targetEngagement, MathHelper.Lerp(0.82f, 0.96f, preRevDumpT));
        }

        float slipRpm = engineRpm - transmissionRpm;
        if (slipRpm < 420f && transmissionRpm >= launchTargetRpm * 0.74f)
        {
            float nearSyncT = 1f - SmoothStep(-120f, 420f, slipRpm);
            targetEngagement = MathHelper.Lerp(targetEngagement, 1f, nearSyncT);
        }

        float bogFloorRpm = MathHelper.Clamp(launchTargetRpm * 0.84f, _parameters.IdleRpm + 1100f, launchTargetRpm * 0.92f);
        if (engineRpm < bogFloorRpm && _launchClutchTimerSeconds < 1.05f)
        {
            float bogProtectionT = 1f - SmoothStep(launchTargetRpm * 0.72f, bogFloorRpm, engineRpm);
            targetEngagement *= MathHelper.Lerp(0.58f, 1f, 1f - bogProtectionT);
        }

        float drivenSlipRatio = CalculateDrivenAverageDriveSlipRatio();
        float wheelSpinReliefT = SmoothStep(0.18f, 0.72f, drivenSlipRatio) *
                                 (1f - SmoothStep(4.0f, 11.5f, speed));
        targetEngagement *= MathHelper.Lerp(1f, 0.66f, wheelSpinReliefT);

        targetEngagement *= MathHelper.Lerp(0.86f, 1f, MathHelper.Clamp(throttle, 0f, 1f));
        targetEngagement = MathHelper.Clamp(targetEngagement, 0.05f, 1f);

        if (_launchClutchEngagement <= 0.001f)
        {
            _launchClutchEngagement = targetEngagement;
            return _launchClutchEngagement;
        }

        float engageRate = targetEngagement > _launchClutchEngagement
            ? MathHelper.Lerp(12.0f, 24.0f, MathF.Max(speedT, timeT))
            : 18.0f;
        float maxStep = engageRate * dt;
        _launchClutchEngagement += MathHelper.Clamp(targetEngagement - _launchClutchEngagement, -maxStep, maxStep);
        return MathHelper.Clamp(_launchClutchEngagement, 0f, 1f);
    }

    private float CalculateDrivenTransmissionRpm(float gearRatio)
    {
        float drivenWheelSpeed = 0f;
        int drivenCount = 0;

        foreach (WheelRuntimeState wheel in _wheels)
        {
            if (!_parameters.DrivenWheels.IsDriven(wheel.Corner))
            {
                continue;
            }

            drivenWheelSpeed += MathF.Abs(wheel.AngularVelocityRadiansPerSecond);
            drivenCount++;
        }

        if (drivenCount == 0)
        {
            drivenWheelSpeed = State.SpeedMetersPerSecond / MathF.Max(0.1f, _parameters.WheelRadiusMeters);
            drivenCount = 1;
        }

        float wheelRpm = drivenWheelSpeed / drivenCount / MathF.Tau * 60f;
        return wheelRpm * gearRatio * _parameters.FinalDriveRatio;
    }

    private float CalculateEngineBrakingTorque(float forwardSpeed, float dt)
    {
        if (State.Gear == 0 || MathF.Abs(forwardSpeed) < 0.15f)
        {
            return 0f;
        }

        float gearRatio = GetCurrentGearRatio();
        if (gearRatio <= 0.0001f)
        {
            return 0f;
        }

        float coupledRpm = MathF.Max(
            State.Rpm,
            CalculateRoadSpeedRpmForGear(State.Gear, forwardSpeed, GetMechanicalOverRevCeiling()));
        EnginePowerUnitState enginePower = AdvanceEnginePower(coupledRpm, 0f, forwardSpeed, dt);
        ApplyEnginePowerCrankState(enginePower, true, dt);
        float crankBrakeTorque = enginePower.Enabled
            ? MathF.Max(_parameters.ClosedThrottleEngineBrakeTorqueNm, enginePower.EngineBrakeTorqueNm)
            : MathF.Max(
                _parameters.ClosedThrottleEngineBrakeTorqueNm,
                _parameters.EngineBrakeTorqueAtRpm(coupledRpm));
        float overRevSeverity = CalculateMechanicalOverRevSeverity(coupledRpm);
        if (overRevSeverity > 0f)
        {
            float brakeMultiplier = MathF.Max(1f, _parameters.DownshiftOverRevBrakeMultiplier);
            crankBrakeTorque *= MathHelper.Lerp(1f, brakeMultiplier, overRevSeverity);
        }

        float shockEnvelope = CalculateDownshiftOverRevShockEnvelope();
        if (shockEnvelope > 0f)
        {
            crankBrakeTorque += _parameters.ClutchTorqueCapacityNm *
                                0.42f *
                                _downshiftOverRevBrakeSeverity *
                                shockEnvelope;
        }

        float wheelTorque = crankBrakeTorque * gearRatio * _parameters.FinalDriveRatio * _parameters.DrivetrainEfficiency;
        return -SignWithFallback(forwardSpeed, State.Gear) * wheelTorque;
    }

    private bool IsMechanicalOverRevForced(float forwardSpeed)
    {
        if (State.Gear <= 0 || MathF.Abs(forwardSpeed) < 0.15f)
        {
            return false;
        }

        bool downshiftOverRevArmed =
            _pendingDownshiftOverRevSeverity > 0f ||
            _downshiftOverRevBrakeSeconds > 0f ||
            State.MechanicalOverRevActive;
        if (!downshiftOverRevArmed)
        {
            return false;
        }

        float coupledRpm = CalculateRoadSpeedRpmForGear(State.Gear, forwardSpeed, GetMechanicalOverRevCeiling());
        return coupledRpm > _parameters.RedlineRpm + 25f || _downshiftOverRevBrakeSeconds > 0f;
    }

    private float CalculateAeroDrag(float forwardSpeed)
    {
        if (MathF.Abs(forwardSpeed) < 0.05f)
        {
            return 0f;
        }

        return -MathF.Sign(forwardSpeed) * _parameters.AeroDragFactor * forwardSpeed * forwardSpeed;
    }

    private void AddGradeForces(Vector2 forward, ref float totalForceZ)
    {
        Vector2 position = new(State.Position.X, State.Position.Z);
        const float sampleDistance = 3.0f;
        float forwardSlope = SampleSlope(position, forward, sampleDistance);
        float weight = _parameters.MassKg * Gravity;

        totalForceZ -= weight * forwardSlope;
    }

    private float SampleSlope(Vector2 position, Vector2 direction, float sampleDistance)
    {
        if (direction.LengthSquared() <= 0.0001f)
        {
            return 0f;
        }

        Vector2 offset = Vector2.Normalize(direction) * sampleDistance;
        float front = _surfaceSampler.GetElevation(position + offset);
        float rear = _surfaceSampler.GetElevation(position - offset);
        return (front - rear) / (sampleDistance * 2f);
    }

    private void UpdateGear(VehicleInput input, float forwardSpeed)
    {
        if (_shiftTimerSeconds > 0f)
        {
            return;
        }

        bool reverseAllowed = forwardSpeed <= -0.05f || MathF.Abs(forwardSpeed) <= 0.08f;
        if (input.Reverse > 0.05f && input.Throttle <= 0.05f && reverseAllowed && State.Gear != -1)
        {
            RequestShift(-1, _parameters.AutomaticShiftTimeSeconds, forwardSpeed, input.Throttle);
            return;
        }

        if (State.Gear < 0 && input.Throttle > 0.05f)
        {
            RequestShift(1, _parameters.AutomaticShiftTimeSeconds, forwardSpeed, input.Throttle);
            return;
        }
        else if (State.Gear < 0 && input.Reverse <= 0.05f && forwardSpeed > -0.25f)
        {
            RequestShift(1, _parameters.AutomaticShiftTimeSeconds, forwardSpeed, input.Throttle);
        }

        if (_manualTransmission && State.Gear < 0)
        {
            if (input.ShiftUpRequested)
            {
                RequestShift(1, _parameters.ManualShiftTimeSeconds, forwardSpeed, input.Throttle);
            }

            return;
        }

        if (_manualTransmission && State.Gear == 0)
        {
            if (input.ShiftUpRequested)
            {
                RequestShift(1, _parameters.ManualShiftTimeSeconds, forwardSpeed, input.Throttle);
            }
            else if (input.ShiftDownRequested && reverseAllowed)
            {
                RequestShift(-1, _parameters.ManualShiftTimeSeconds, forwardSpeed, input.Throttle);
            }

            return;
        }

        if (State.Gear < 0 || State.Gear == 0)
        {
            return;
        }

        if (_manualTransmission)
        {
            if (input.ShiftUpRequested && State.Gear < _parameters.ForwardGearRatios.Length)
            {
                RequestShift(State.Gear + 1, _parameters.ManualShiftTimeSeconds, forwardSpeed, input.Throttle);
            }
            else if (input.ShiftDownRequested && State.Gear > 1)
            {
                RequestShift(State.Gear - 1, _parameters.ManualShiftTimeSeconds, forwardSpeed, input.Throttle);
            }
            else if (input.ShiftDownRequested && State.Gear == 1 && reverseAllowed)
            {
                RequestShift(-1, _parameters.ManualShiftTimeSeconds, forwardSpeed, input.Throttle);
            }

            return;
        }

        float absForwardSpeed = MathF.Abs(forwardSpeed);
        if (absForwardSpeed < 2.25f && State.Gear > 1)
        {
            RequestShift(1, _parameters.AutomaticShiftTimeSeconds, forwardSpeed, input.Throttle);
        }
        else if (absForwardSpeed >= _parameters.AutomaticMinimumUpshiftSpeedMetersPerSecond &&
                 State.Rpm > _parameters.UpshiftRpm &&
                 State.Gear < _parameters.ForwardGearRatios.Length)
        {
            RequestShift(State.Gear + 1, _parameters.AutomaticShiftTimeSeconds, forwardSpeed, input.Throttle);
        }
        else if (State.Rpm < _parameters.DownshiftRpm && State.Gear > 1)
        {
            int targetGear = State.Gear - 1;
            if (!WouldOverRev(targetGear, forwardSpeed))
            {
                RequestShift(targetGear, _parameters.AutomaticShiftTimeSeconds, forwardSpeed, input.Throttle);
            }
        }
    }

    private void UpdateShiftTimer(float dt)
    {
        if (_shiftTimerSeconds <= 0f)
        {
            _pendingGear = 0;
            _pendingShiftFromGear = 0;
            _shiftDurationSeconds = 0f;
            _shiftStartRpm = 0f;
            _shiftTargetRpm = 0f;
            _shiftRpmCeiling = 0f;
            _pendingShiftKickSeverity = 0f;
            _pendingDownshiftOverRevRpm = 0f;
            _pendingDownshiftOverRevSeverity = 0f;
            State.IsShifting = false;
            State.ShiftTimeRemainingSeconds = 0f;
            return;
        }

        _shiftTimerSeconds = MathF.Max(0f, _shiftTimerSeconds - dt);
        if (_shiftTimerSeconds <= 0f)
        {
            State.LastCompletedShiftFromGear = _pendingShiftFromGear;
            State.LastCompletedShiftToGear = _pendingGear;
            State.LastCompletedShiftKickSeverity = _pendingShiftKickSeverity;
            _pendingGear = 0;
            _pendingShiftFromGear = 0;
            _shiftDurationSeconds = 0f;
            _shiftStartRpm = 0f;
            _shiftTargetRpm = 0f;
            _shiftRpmCeiling = 0f;
            State.ClutchSlipRpm = 0f;
            EngagePendingShiftKick();
            EngagePendingDownshiftOverRevShock();
            _pendingShiftKickSeverity = 0f;
            _pendingDownshiftOverRevRpm = 0f;
            _pendingDownshiftOverRevSeverity = 0f;
        }

        State.IsShifting = _shiftTimerSeconds > 0f;
        State.ShiftTimeRemainingSeconds = _shiftTimerSeconds;
    }

    private void RequestShift(int targetGear, float shiftTimeSeconds, float forwardSpeed, float throttle)
    {
        if (targetGear == State.Gear || targetGear == _pendingGear)
        {
            return;
        }

        if (shiftTimeSeconds <= 0f)
        {
            State.LastCompletedShiftFromGear = State.Gear;
            State.LastCompletedShiftToGear = targetGear;
            State.LastCompletedShiftKickSeverity = 0f;
            State.Gear = targetGear;
            _pendingGear = 0;
            _pendingShiftFromGear = 0;
            _shiftTimerSeconds = 0f;
            _shiftDurationSeconds = 0f;
            _shiftStartRpm = 0f;
            _shiftTargetRpm = 0f;
            _shiftRpmCeiling = 0f;
            _pendingShiftKickSeverity = 0f;
            _pendingDownshiftOverRevRpm = 0f;
            _pendingDownshiftOverRevSeverity = 0f;
            State.IsShifting = false;
            State.ShiftTimeRemainingSeconds = 0f;
            return;
        }

        int previousGear = State.Gear;
        bool isForwardUpshift = previousGear > 0 && targetGear > previousGear;
        bool isForwardDownshift = previousGear > 1 && targetGear > 0 && targetGear < previousGear;
        float forcedTargetRpm = CalculateRoadSpeedRpmForGear(targetGear, forwardSpeed, GetMechanicalOverRevCeiling());
        float targetRpmCeiling = GetShiftRpmCeiling();
        float targetRpm = CalculateRoadSpeedRpmForGear(targetGear, forwardSpeed, targetRpmCeiling);
        if (isForwardUpshift)
        {
            targetRpm = MathF.Min(targetRpm, State.Rpm);
        }

        _pendingDownshiftOverRevRpm = 0f;
        _pendingDownshiftOverRevSeverity = 0f;
        if (isForwardDownshift)
        {
            float overRevSeverity = CalculateMechanicalOverRevSeverity(forcedTargetRpm);
            if (overRevSeverity > 0f)
            {
                _pendingDownshiftOverRevRpm = forcedTargetRpm;
                _pendingDownshiftOverRevSeverity = overRevSeverity;
                targetRpm = CalculateMechanicalLimiterBounceRpm(CalculateMechanicalLimiterContactIntensity(forcedTargetRpm));
            }
        }

        _pendingShiftKickSeverity = CalculateShiftKickSeverity(previousGear, targetGear, throttle, State.Rpm, targetRpm, forcedTargetRpm);
        _pendingGear = targetGear;
        _pendingShiftFromGear = previousGear;
        _shiftDurationSeconds = shiftTimeSeconds;
        _shiftStartRpm = State.Rpm;
        _shiftTargetRpm = targetRpm;
        _shiftRpmCeiling = isForwardUpshift
            ? MathF.Min(targetRpmCeiling, State.Rpm + 40f + _pendingShiftKickSeverity * 220f)
            : targetRpmCeiling;
        _shiftTimerSeconds = shiftTimeSeconds;
        State.Gear = targetGear;
        State.IsShifting = true;
        State.ShiftTimeRemainingSeconds = _shiftTimerSeconds;
    }

    private bool WouldOverRev(int targetGear, float forwardSpeed)
    {
        float ratio = GetGearRatio(targetGear);
        if (ratio <= 0f)
        {
            return false;
        }

        float predictedRpm = CalculateRoadSpeedRpmForGearUnclamped(targetGear, forwardSpeed);
        return predictedRpm > _parameters.RedlineRpm + _parameters.DownshiftOverRevToleranceRpm;
    }

    private float CalculateRoadSpeedRpmForGear(int gear, float forwardSpeed, float rpmCeiling)
    {
        float targetRpm = CalculateRoadSpeedRpmForGearUnclamped(gear, forwardSpeed);
        return MathHelper.Clamp(targetRpm, _parameters.IdleRpm, rpmCeiling);
    }

    private float CalculateRoadSpeedRpmForGearUnclamped(int gear, float forwardSpeed)
    {
        float ratio = GetGearRatio(gear);
        if (ratio <= 0f)
        {
            return _parameters.IdleRpm;
        }

        float wheelRpm = MathF.Abs(forwardSpeed) /
                         MathF.Max(0.05f, _parameters.WheelRadiusMeters) /
                         MathF.Tau *
                         60f;
        return wheelRpm * ratio * _parameters.FinalDriveRatio;
    }

    private void UpdateRpm(VehicleInput input, float forwardSpeed, float dt)
    {
        float ratio = GetCurrentGearRatio();
        float limiterCeiling = GetCurrentRpmCeiling();
        float pedal = MathF.Max(input.Throttle, input.Reverse);

        if (_shiftTimerSeconds > 0f && _shiftDurationSeconds > 0f && _shiftTargetRpm > 0f)
        {
            float progress = 1f - MathHelper.Clamp(_shiftTimerSeconds / _shiftDurationSeconds, 0f, 1f);
            float shiftBlend = progress * progress * (3f - 2f * progress);
            if (_pendingShiftKickSeverity > 0.001f)
            {
                shiftBlend = SmoothStep(0.22f, 0.96f, progress);
            }

            float shiftCeiling = _shiftRpmCeiling > 0f
                ? _shiftRpmCeiling
                : GetShiftRpmCeiling();
            float flareWindow = _pendingShiftKickSeverity > 0.001f
                ? SmoothStep(0.04f, 0.28f, progress) * (1f - SmoothStep(0.46f, 0.78f, progress))
                : 0f;
            float flareRpm = flareWindow * MathHelper.Lerp(70f, 220f, _pendingShiftKickSeverity) * _pendingShiftKickSeverity;
            State.Rpm = MathHelper.Clamp(
                MathHelper.Lerp(_shiftStartRpm, _shiftTargetRpm, shiftBlend) + flareRpm,
                _parameters.IdleRpm,
                shiftCeiling);
            State.ClutchSlipRpm = 0f;
            float forcedShiftRpm = _pendingDownshiftOverRevRpm > 0f
                ? _pendingDownshiftOverRevRpm
                : _shiftTargetRpm;
            UpdateMechanicalOverRevState(MathF.Max(State.Rpm, forcedShiftRpm));
            return;
        }

        if (ratio <= 0f)
        {
            if (UpdateNeutralRpmWithEnginePower(pedal, forwardSpeed, dt))
            {
                return;
            }

            float freeTargetRpm = _parameters.IdleRpm + pedal * MathF.Max(0f, limiterCeiling - _parameters.IdleRpm);
            float freeBlend = 1f - MathF.Exp(-_parameters.EngineFreeRevResponseRate * dt / MathF.Max(0.05f, _parameters.EngineRotationalInertiaKgM2));
            State.ClutchSlipRpm = 0f;
            State.Rpm = MathHelper.Lerp(State.Rpm, freeTargetRpm, MathHelper.Clamp(freeBlend, 0f, 1f));
            UpdateMechanicalOverRevState(State.Rpm);
            return;
        }

        if (UpdateEnginePowerOwnedDrivingRpm(input, forwardSpeed, ratio, limiterCeiling))
        {
            return;
        }

        float roadSpeedRpm = CalculateRoadSpeedRpmForGear(State.Gear, forwardSpeed, GetMechanicalOverRevCeiling());
        bool downshiftOverRevArmed =
            _pendingDownshiftOverRevSeverity > 0f ||
            _downshiftOverRevBrakeSeconds > 0f ||
            State.MechanicalOverRevActive;
        bool mechanicalLimiterContact = downshiftOverRevArmed && roadSpeedRpm > _parameters.RedlineRpm;
        float roadCoupledTargetRpm = CalculateRoadSpeedRpmForGear(State.Gear, forwardSpeed, limiterCeiling);
        float targetRpm = mechanicalLimiterContact
            ? CalculateMechanicalLimiterBounceRpm(CalculateMechanicalLimiterContactIntensity(roadSpeedRpm))
            : roadCoupledTargetRpm;

        if (!mechanicalLimiterContact &&
            _shiftTimerSeconds <= 0f &&
            State.Gear > 0 &&
            pedal > 0.20f &&
            input.Brake < 0.05f)
        {
            float drivenTransmissionRpm = MathHelper.Clamp(CalculateDrivenTransmissionRpm(ratio), _parameters.IdleRpm, limiterCeiling);
            if (State.Gear == 1 && State.SpeedMetersPerSecond < 18f)
            {
                float speed = MathF.Abs(forwardSpeed);
                float lowSpeedAllowanceT = 1f - SmoothStep(5.0f, 13.5f, speed);
                float spinAllowanceSlipT = SmoothStep(0.06f, 0.36f, CalculateDrivenAverageDriveSlipRatio());
                float launchSpinAllowanceRpm =
                    650f +
                    lowSpeedAllowanceT *
                    MathHelper.Lerp(1900f, 3400f, spinAllowanceSlipT);
                float launchSpinCeilingRpm = MathHelper.Clamp(
                    roadCoupledTargetRpm + launchSpinAllowanceRpm,
                    _parameters.IdleRpm,
                    limiterCeiling);
                drivenTransmissionRpm = MathF.Min(drivenTransmissionRpm, launchSpinCeilingRpm);
            }

            float drivenSlipT = SmoothStep(0.05f, 0.20f, CalculateDrivenAverageDriveSlipRatio());
            targetRpm = MathF.Max(targetRpm, MathHelper.Lerp(targetRpm, drivenTransmissionRpm, drivenSlipT));
        }

        if (_shiftTimerSeconds <= 0f && State.SpeedMetersPerSecond < 0.8f)
        {
            float launchTarget = MathHelper.Clamp(_parameters.LaunchSlipTargetRpm, _parameters.IdleRpm, limiterCeiling);
            targetRpm = MathHelper.Lerp(targetRpm, launchTarget, pedal * MathHelper.Clamp(_parameters.LaunchSlipBlend, 0f, 1f));
        }

        if (_shiftTimerSeconds <= 0f &&
            State.Gear > 0 &&
            pedal > 0.20f &&
            input.Brake < 0.05f &&
            targetRpm > State.Rpm - _engineParameters.RpmResponse.PoweredAntiDipWindowRpm)
        {
            float antiDipFallRate = MathF.Max(0f, _engineParameters.RpmResponse.PoweredAntiDipFallRateRpmPerSecond);
            if (State.Gear == 1 && MathF.Abs(forwardSpeed) < 18f && State.Rpm - targetRpm > 700f)
            {
                antiDipFallRate *= 18f;
            }

            float maximumDip = antiDipFallRate * dt;
            targetRpm = MathF.Max(targetRpm, State.Rpm - maximumDip);
        }

        State.ClutchSlipRpm = mechanicalLimiterContact
            ? roadSpeedRpm - State.Rpm
            : targetRpm - State.Rpm;
        float couplingRate = mechanicalLimiterContact
            ? MathHelper.Lerp(52f, 86f, CalculateMechanicalLimiterContactIntensity(roadSpeedRpm))
            : _parameters.ClutchCouplingRate;
        float rpmBlend = 1f - MathF.Exp(-couplingRate * dt);
        float newRpm = MathHelper.Lerp(State.Rpm, targetRpm, MathHelper.Clamp(rpmBlend, 0f, 1f));
        if (_shiftTimerSeconds > 0f && _shiftRpmCeiling > 0f)
        {
            newRpm = MathF.Min(newRpm, _shiftRpmCeiling);
        }

        State.Rpm = newRpm;
        float coupledRpm = mechanicalLimiterContact
            ? MathF.Max(roadSpeedRpm, newRpm)
            : MathF.Max(targetRpm, newRpm);
        UpdateMechanicalOverRevState(coupledRpm);
    }

    private bool UpdateEnginePowerOwnedDrivingRpm(
        VehicleInput input,
        float forwardSpeed,
        float gearRatio,
        float rpmCeiling)
    {
        if (!_enginePowerUnit.OwnsDriveline ||
            State.Gear <= 0 ||
            gearRatio <= 0.0001f ||
            _shiftTimerSeconds > 0f ||
            input.Brake >= 0.05f ||
            MathF.Abs(forwardSpeed) < 0.8f)
        {
            return false;
        }

        float pedal = MathF.Max(input.Throttle, input.Reverse);
        if (pedal <= 0.20f)
        {
            return false;
        }

        bool downshiftOverRevArmed =
            _pendingDownshiftOverRevSeverity > 0f ||
            _downshiftOverRevBrakeSeconds > 0f ||
            State.MechanicalOverRevActive;
        if (downshiftOverRevArmed)
        {
            return false;
        }

        EnginePowerUnitState enginePower = _enginePowerUnit.State;
        if (!enginePower.Enabled || !enginePower.OwnsDriveline || enginePower.CrankRpm <= 0f)
        {
            return false;
        }

        float crankRpm = MathHelper.Clamp(enginePower.CrankRpm, _parameters.IdleRpm, rpmCeiling);
        float transmissionRpm = enginePower.TransmissionRpm > 0f
            ? enginePower.TransmissionRpm
            : CalculateDrivenTransmissionRpm(gearRatio);
        State.Rpm = crankRpm;
        State.ClutchSlipRpm = crankRpm - MathF.Max(0f, transmissionRpm);
        float roadSpeedRpm = CalculateRoadSpeedRpmForGear(State.Gear, forwardSpeed, GetMechanicalOverRevCeiling());
        UpdateMechanicalOverRevState(MathF.Max(crankRpm, roadSpeedRpm));
        return true;
    }

    private bool UpdateNeutralRpmWithEnginePower(float throttle, float forwardSpeed, float dt)
    {
        if (!_enginePowerUnit.Enabled)
        {
            return false;
        }

        EnginePowerUnitState enginePower = AdvanceEnginePower(
            MathF.Max(400f, State.Rpm),
            throttle,
            forwardSpeed,
            dt,
            clutchEngagement: 0f,
            forceNeutral: true);
        if (!enginePower.Enabled || !enginePower.OwnsDriveline)
        {
            return false;
        }

        ApplyEnginePowerCrankState(enginePower, dt: dt);
        UpdateMechanicalOverRevState(State.Rpm);
        return true;
    }

    private void UpdateHeldLaunchRpm(float throttle, float dt)
    {
        throttle = MathHelper.Clamp(throttle, 0f, 1f);
        float rpm = MathF.Max(400f, State.Rpm);
        float inertia = MathF.Max(0.05f, _parameters.EngineRotationalInertiaKgM2);
        EnginePowerUnitState enginePower = AdvanceEnginePower(rpm, throttle, 0f, dt, 0f, true);
        if (enginePower.Enabled && enginePower.OwnsDriveline)
        {
            ApplyEnginePowerCrankState(enginePower);
            UpdateMechanicalOverRevState(State.Rpm);
            return;
        }

        float crankTorque = enginePower.Enabled
            ? enginePower.DriveTorqueNm
            : _parameters.TorqueAtRpm(rpm) * throttle * State.LimiterTorqueMultiplier;
        float frictionTorque = CalculateEngineInternalDragTorque(rpm, throttle, 0.92f);
        float idleControlTorque = throttle <= 0.02f
            ? MathHelper.Clamp((_parameters.IdleRpm - rpm) * 0.06f, 0f, 28f)
            : 0f;
        float netTorque = crankTorque + idleControlTorque - frictionTorque;
        float rpmDelta = netTorque / inertia * (60f / MathF.Tau) * dt;
        float maximumRpm = _parameters.RedlineRpm;
        float newRpm = MathHelper.Clamp(rpm + rpmDelta, 650f, maximumRpm);

        if (throttle <= 0.001f && newRpm < _parameters.IdleRpm)
        {
            newRpm = MathHelper.Lerp(newRpm, _parameters.IdleRpm, MathHelper.Clamp(1f - MathF.Exp(-8f * dt), 0f, 1f));
        }

        State.ClutchSlipRpm = newRpm - State.Rpm;
        State.Rpm = newRpm;
        UpdateMechanicalOverRevState(newRpm);
    }

    private float CalculateEngineInternalDragTorque(float rpm, float throttle, float closedThrottleMultiplier)
    {
        EnginePowerUnitState enginePower = _enginePowerUnit.State;
        if (enginePower.Enabled)
        {
            float simRpmT = SmoothStep(_parameters.IdleRpm, _parameters.RedlineRpm, rpm);
            float simClosedThrottleTorque = MathHelper.Lerp(
                14f,
                enginePower.EngineBrakeTorqueNm * closedThrottleMultiplier,
                simRpmT);
            float simPoweredTorque = MathHelper.Lerp(
                8f,
                enginePower.EngineBrakeTorqueNm * 0.28f,
                simRpmT);
            float simThrottleT = SmoothStep(0.04f, 0.35f, throttle);
            return MathHelper.Lerp(simClosedThrottleTorque, simPoweredTorque, simThrottleT);
        }

        float rpmT = SmoothStep(_parameters.IdleRpm, _parameters.RedlineRpm, rpm);
        float closedThrottleTorque = MathHelper.Lerp(
            14f,
            _parameters.EngineBrakeTorqueAtRpm(rpm) * closedThrottleMultiplier,
            rpmT);
        float poweredTorque = MathHelper.Lerp(
            8f,
            _parameters.EngineBrakeTorqueAtRpm(rpm) * 0.28f,
            rpmT);
        float throttleT = SmoothStep(0.04f, 0.35f, throttle);
        return MathHelper.Lerp(closedThrottleTorque, poweredTorque, throttleT);
    }

    private void UpdateRevLimiter(float throttle, float dt)
    {
        bool mechanicalLimiterContact = State.MechanicalOverRevActive && State.MechanicalOverRevRpm > 25f;
        if (throttle <= 0.05f || State.Rpm < _parameters.RevLimiterResumeRpm)
        {
            _revLimiterCutting = false;
            _revLimiterPhaseSeconds = 0f;
        }
        else if (_revLimiterCutting)
        {
            _revLimiterPhaseSeconds -= dt;
            if (_revLimiterPhaseSeconds <= 0f)
            {
                _revLimiterCutting = false;
                _revLimiterPhaseSeconds = _parameters.RevLimiterRestoreSeconds;
            }
        }
        else if (State.Rpm >= _parameters.RedlineRpm - MathF.Max(1f, _parameters.RevLimiterBounceRpm * 0.1f))
        {
            _revLimiterCutting = true;
            _revLimiterPhaseSeconds = _parameters.RevLimiterFuelCutSeconds;
        }
        else if (_revLimiterPhaseSeconds > 0f)
        {
            _revLimiterPhaseSeconds -= dt;
        }

        State.RevLimiterActive = _revLimiterCutting || mechanicalLimiterContact;
        State.LimiterTorqueMultiplier = _revLimiterCutting
            ? MathHelper.Clamp(_parameters.RevLimiterCutTorqueMultiplier, 0f, 1f)
            : 1f;
        UpdateRevLimiterBounceIntensity(throttle, dt);
    }

    private void UpdateRevLimiterBounceIntensity(float throttle, float dt)
    {
        float bounceRpm = MathF.Max(80f, _parameters.RevLimiterBounceRpm);
        bool throttleLimiterRegion = throttle > 0.05f &&
                                     State.Rpm >= _parameters.RedlineRpm - bounceRpm * 1.45f &&
                                     State.Rpm >= _parameters.RevLimiterResumeRpm;
        float mechanicalLimiterStress = State.MechanicalOverRevActive
            ? CalculateMechanicalLimiterContactIntensity(_parameters.RedlineRpm + State.MechanicalOverRevRpm)
            : 0f;
        if (!throttleLimiterRegion && mechanicalLimiterStress <= 0f)
        {
            State.RevLimiterBounceIntensity = 0f;
            State.RevLimiterBouncePhase = 0f;
            _revLimiterChatterPhaseSeconds = 0f;
            return;
        }

        float throttleProximity = throttleLimiterRegion
            ? SmoothStep(
                _parameters.RedlineRpm - bounceRpm * 1.45f,
                _parameters.RedlineRpm - bounceRpm * 0.08f,
                State.Rpm)
            : 0f;
        float proximity = MathF.Max(throttleProximity, mechanicalLimiterStress);
        float chatterHz = 1f / RevLimiterPresentationRules.CalculateBounceSeconds(_parameters.RedlineRpm);
        _revLimiterChatterPhaseSeconds += MathF.Max(0f, dt) * chatterHz;
        if (_revLimiterChatterPhaseSeconds > 1000f)
        {
            _revLimiterChatterPhaseSeconds -= MathF.Floor(_revLimiterChatterPhaseSeconds);
        }

        float cycle = _revLimiterChatterPhaseSeconds - MathF.Floor(_revLimiterChatterPhaseSeconds);
        State.RevLimiterBouncePhase = cycle;
        float cutPulse = SmoothStep(0.08f, 0.16f, cycle) *
                         (1f - SmoothStep(0.36f, 0.78f, cycle));
        float secondaryCutPulse = SmoothStep(0.54f, 0.60f, cycle) *
                                  (1f - SmoothStep(0.66f, 0.90f, cycle)) *
                                  0.38f;
        float impulse = MathHelper.Clamp(MathF.Max(cutPulse, secondaryCutPulse), 0f, 1f);
        float cutWeight = _revLimiterCutting || mechanicalLimiterStress > 0f ? 1f : 0.45f;
        State.RevLimiterBounceIntensity = MathHelper.Clamp(
            proximity * cutWeight * (0.38f + impulse * 0.62f),
            0f,
            1f);
    }

    private void ApplyIdleCrankCycleBounce(VehicleInput input, float dt)
    {
        float pedal = MathF.Max(input.Throttle, input.Reverse);
        bool idleEligible =
            pedal <= 0.015f &&
            input.Brake <= 0.015f &&
            input.Handbrake <= 0.015f &&
            MathF.Abs(State.SignedForwardSpeed) <= 0.35f &&
            !State.RevLimiterActive &&
            !State.MechanicalOverRevActive &&
            _shiftTimerSeconds <= 0f &&
            State.Rpm <= 1030f;
        if (!idleEligible)
        {
            return;
        }

        _idleCrankPhaseDegrees += MathF.Max(0f, State.Rpm) / 60f * 360f * MathF.Max(0f, dt);
        _idleCrankPhaseDegrees %= 720f;
        float targetIdleRpm = CalculateIdleCycleTargetRpm(_idleCrankPhaseDegrees);
        float blend = MathHelper.Clamp(1f - MathF.Exp(-10f * MathF.Max(0f, dt)), 0f, 1f);
        State.Rpm = MathHelper.Lerp(State.Rpm, targetIdleRpm, blend);
    }

    private static float CalculateIdleCycleTargetRpm(float crankPhaseDegrees)
    {
        float phase = crankPhaseDegrees % 720f;
        if (phase < 0f)
        {
            phase += 720f;
        }

        if (phase < 270f)
        {
            return MathHelper.Lerp(900f, 950f, SmoothStep(0f, 1f, phase / 270f));
        }

        if (phase < 540f)
        {
            return MathHelper.Lerp(950f, 900f, SmoothStep(0f, 1f, (phase - 270f) / 270f));
        }

        return 900f;
    }

    private float GetCurrentRpmCeiling()
    {
        return _revLimiterCutting
            ? MathF.Max(_parameters.IdleRpm, _parameters.RedlineRpm - _parameters.RevLimiterBounceRpm)
            : _parameters.RedlineRpm;
    }

    private float GetShiftRpmCeiling()
    {
        return _parameters.RedlineRpm;
    }

    private float GetMechanicalOverRevCeiling()
    {
        float configuredLimit = _parameters.DownshiftMechanicalOverRevLimitRpm;
        float fallbackLimit = _parameters.RedlineRpm + MathF.Max(900f, _parameters.RedlineRpm * 0.22f);
        float minimumLimit = _parameters.RedlineRpm + MathF.Max(300f, _parameters.DownshiftOverRevToleranceRpm);
        return MathF.Max(minimumLimit, configuredLimit > 0f ? configuredLimit : fallbackLimit);
    }

    private float CalculateMechanicalOverRevSeverity(float coupledRpm)
    {
        float startRpm = _parameters.RedlineRpm + MathF.Max(0f, _parameters.DownshiftOverRevToleranceRpm);
        float limitRpm = GetMechanicalOverRevCeiling();
        if (coupledRpm <= startRpm || limitRpm <= startRpm + 1f)
        {
            return 0f;
        }

        return SmoothStep(startRpm, limitRpm, coupledRpm);
    }

    private float CalculateMechanicalLimiterContactIntensity(float coupledRpm)
    {
        float excessRpm = coupledRpm - _parameters.RedlineRpm;
        if (excessRpm <= 25f)
        {
            return 0f;
        }

        float warningRange = MathF.Max(160f, _parameters.DownshiftOverRevToleranceRpm + 550f);
        return MathHelper.Clamp(
            0.35f + SmoothStep(
                _parameters.RedlineRpm + 25f,
                _parameters.RedlineRpm + warningRange,
                coupledRpm) * 0.65f,
            0f,
            1f);
    }

    private float CalculateMechanicalLimiterBounceRpm(float contactIntensity)
    {
        float cycle = _revLimiterChatterPhaseSeconds - MathF.Floor(_revLimiterChatterPhaseSeconds);
        float cutPulse = SmoothStep(0.08f, 0.16f, cycle) *
                         (1f - SmoothStep(0.36f, 0.78f, cycle));
        float secondaryCutPulse = SmoothStep(0.54f, 0.60f, cycle) *
                                  (1f - SmoothStep(0.66f, 0.90f, cycle)) *
                                  0.35f;
        float bounceDepthRpm = MathF.Max(150f, _parameters.RevLimiterBounceRpm * 1.45f);
        float dip = MathF.Max(cutPulse, secondaryCutPulse) * MathHelper.Clamp(contactIntensity, 0f, 1f);
        return MathHelper.Clamp(
            _parameters.RedlineRpm - bounceDepthRpm * dip,
            _parameters.RevLimiterResumeRpm,
            _parameters.RedlineRpm);
    }

    private float CalculateShiftKickSeverity(
        int previousGear,
        int targetGear,
        float throttle,
        float currentRpm,
        float targetRpm,
        float forcedTargetRpm)
    {
        if (previousGear <= 0 || targetGear <= 0 || previousGear == targetGear)
        {
            return 0f;
        }

        float throttleT = SmoothStep(0.34f, 0.92f, MathHelper.Clamp(throttle, 0f, 1f));
        if (throttleT <= 0.001f)
        {
            return 0f;
        }

        float rpmDelta = MathF.Abs(MathF.Max(targetRpm, forcedTargetRpm) - currentRpm);
        float rpmDeltaT = SmoothStep(650f, 2600f, rpmDelta);
        float highRpmT = SmoothStep(_parameters.RedlineRpm * 0.54f, _parameters.RedlineRpm * 0.96f, currentRpm);
        float gearStepT = MathHelper.Clamp(MathF.Abs(targetGear - previousGear) / 2f, 0.45f, 1f);
        bool upshift = targetGear > previousGear;
        float directionScale = upshift ? 1f : 0.72f;
        return MathHelper.Clamp(
            throttleT * directionScale * gearStepT * (0.28f + rpmDeltaT * 0.46f + highRpmT * 0.22f),
            0f,
            0.82f);
    }

    private void EngagePendingShiftKick()
    {
        if (_pendingShiftKickSeverity <= 0.001f)
        {
            return;
        }

        float duration = MathHelper.Lerp(0.105f, 0.185f, _pendingShiftKickSeverity);
        _shiftKickDurationSeconds = duration;
        _shiftKickSeconds = MathF.Max(_shiftKickSeconds, duration);
        _shiftKickSeverity = MathF.Max(_shiftKickSeverity, _pendingShiftKickSeverity);
        _enginePowerShiftHandoffSmoothSeconds = MathF.Max(_enginePowerShiftHandoffSmoothSeconds, duration + 0.24f);
        State.ShiftKickIntensity = MathF.Max(State.ShiftKickIntensity, _shiftKickSeverity);
    }

    private float CalculateShiftKickEnvelope()
    {
        if (_shiftKickSeconds <= 0f || _shiftKickDurationSeconds <= 0f)
        {
            return 0f;
        }

        float t = MathHelper.Clamp(_shiftKickSeconds / _shiftKickDurationSeconds, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private void UpdateShiftKickTimer(float dt)
    {
        _enginePowerShiftHandoffSmoothSeconds = MathF.Max(0f, _enginePowerShiftHandoffSmoothSeconds - MathF.Max(0f, dt));
        if (_shiftKickSeconds <= 0f)
        {
            State.ShiftKickIntensity = 0f;
            return;
        }

        _shiftKickSeconds = MathF.Max(0f, _shiftKickSeconds - MathF.Max(0f, dt));
        if (_shiftKickSeconds <= 0f)
        {
            _shiftKickDurationSeconds = 0f;
            _shiftKickSeverity = 0f;
            State.ShiftKickIntensity = 0f;
        }
    }

    private void EngagePendingDownshiftOverRevShock()
    {
        if (_pendingDownshiftOverRevSeverity <= 0f)
        {
            return;
        }

        float duration = MathF.Max(0.05f, _parameters.DownshiftOverRevShockSeconds);
        _downshiftOverRevBrakeDurationSeconds = duration;
        _downshiftOverRevBrakeSeconds = MathF.Max(_downshiftOverRevBrakeSeconds, duration);
        _downshiftOverRevBrakeSeverity = MathF.Max(_downshiftOverRevBrakeSeverity, _pendingDownshiftOverRevSeverity);
        State.MechanicalOverRevActive = true;
        State.MechanicalOverRevRpm = MathF.Max(State.MechanicalOverRevRpm, _pendingDownshiftOverRevRpm - _parameters.RedlineRpm);
        State.MechanicalOverRevSeverity = MathF.Max(State.MechanicalOverRevSeverity, _pendingDownshiftOverRevSeverity);
    }

    private float CalculateDownshiftOverRevShockEnvelope()
    {
        if (_downshiftOverRevBrakeSeconds <= 0f || _downshiftOverRevBrakeDurationSeconds <= 0f)
        {
            return 0f;
        }

        float t = MathHelper.Clamp(_downshiftOverRevBrakeSeconds / _downshiftOverRevBrakeDurationSeconds, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private void UpdateDownshiftOverRevShockTimer(float dt)
    {
        if (_downshiftOverRevBrakeSeconds <= 0f)
        {
            return;
        }

        _downshiftOverRevBrakeSeconds = MathF.Max(0f, _downshiftOverRevBrakeSeconds - MathF.Max(0f, dt));
        if (_downshiftOverRevBrakeSeconds <= 0f)
        {
            _downshiftOverRevBrakeDurationSeconds = 0f;
            _downshiftOverRevBrakeSeverity = 0f;
        }
    }

    private void UpdateMechanicalOverRevState(float coupledRpm)
    {
        float overRevRpm = MathF.Max(0f, coupledRpm - _parameters.RedlineRpm);
        float mechanicalSeverity = CalculateMechanicalOverRevSeverity(coupledRpm);
        float shockSeverity = CalculateDownshiftOverRevShockEnvelope() * _downshiftOverRevBrakeSeverity;
        float shiftKickSeverity = CalculateShiftKickEnvelope() * _shiftKickSeverity;
        float severity = MathF.Max(mechanicalSeverity, shockSeverity);
        State.MechanicalOverRevRpm = overRevRpm;
        State.MechanicalOverRevSeverity = severity;
        State.ShiftKickIntensity = shiftKickSeverity;
        State.PowertrainShockIntensity = MathHelper.Clamp(
            MathF.Max(shiftKickSeverity, shockSeverity + mechanicalSeverity * 0.38f),
            0f,
            1f);
        State.MechanicalOverRevActive = overRevRpm > 25f || severity > 0.01f;
    }

    private float GetCurrentGearRatio()
    {
        return GetGearRatio(State.Gear);
    }

    private float GetGearRatio(int gear)
    {
        if (gear == 0)
        {
            return 0f;
        }

        return gear < 0
            ? _parameters.ReverseGearRatio
            : _parameters.ForwardGearRatios[Math.Clamp(gear, 1, _parameters.ForwardGearRatios.Length) - 1];
    }

    private void UpdateGroundContactPose(Vector2 forward, Vector2 right, float dt)
    {
        ArcadeHandlingParameters arcade = _parameters.ArcadeHandling;
        Vector2 center = new(State.Position.X, State.Position.Z);
        Span<float> groundHeights = stackalloc float[4];
        Span<float> supportHeights = stackalloc float[4];

        foreach (WheelRuntimeState wheel in _wheels)
        {
            Vector2 wheelPosition = center + right * wheel.LocalX + forward * wheel.LocalZ;
            groundHeights[(int)wheel.Corner] = _surfaceSampler.GetElevation(wheelPosition);
        }

        UpdateVisualSuspension(dt);
        for (int i = 0; i < _wheels.Length; i++)
        {
            supportHeights[i] = groundHeights[i] - _visualSuspensionCompressionMeters[i];
        }

        float groundFrontHeight = Average(groundHeights[(int)WheelCorner.FrontLeft], groundHeights[(int)WheelCorner.FrontRight]);
        float groundRearHeight = Average(groundHeights[(int)WheelCorner.RearLeft], groundHeights[(int)WheelCorner.RearRight]);
        float supportFrontHeight = Average(supportHeights[(int)WheelCorner.FrontLeft], supportHeights[(int)WheelCorner.FrontRight]);
        float supportRearHeight = Average(supportHeights[(int)WheelCorner.RearLeft], supportHeights[(int)WheelCorner.RearRight]);
        float supportCenterHeight = Average(
            supportHeights[(int)WheelCorner.FrontLeft],
            supportHeights[(int)WheelCorner.FrontRight],
            supportHeights[(int)WheelCorner.RearLeft],
            supportHeights[(int)WheelCorner.RearRight]);
        float groundCenterHeight = Average(
            groundHeights[(int)WheelCorner.FrontLeft],
            groundHeights[(int)WheelCorner.FrontRight],
            groundHeights[(int)WheelCorner.RearLeft],
            groundHeights[(int)WheelCorner.RearRight]);
        float wheelbase = MathF.Max(0.1f, _parameters.WheelbaseMeters);
        float frontRollWeight = CalculateFrontRollWeight();
        float groundPitch = -MathF.Atan2(groundFrontHeight - groundRearHeight, wheelbase);
        float groundRoll = MathHelper.Lerp(CalculateAxleRoll(groundHeights, false), CalculateAxleRoll(groundHeights, true), frontRollWeight);
        float bodyPitch = -MathF.Atan2(supportFrontHeight - supportRearHeight, wheelbase);
        float bodyRoll = MathHelper.Lerp(CalculateAxleRoll(supportHeights, false), CalculateAxleRoll(supportHeights, true), frontRollWeight);

        State.Position = new Vector3(State.Position.X, supportCenterHeight, State.Position.Z);
        State.WheelContactCenterHeightMeters = groundCenterHeight;
        State.GroundPitchRadians = MathHelper.Clamp(groundPitch, -0.18f, 0.18f);
        State.GroundRollRadians = MathHelper.Clamp(groundRoll, -0.14f, 0.14f);
        float physicsBodyPitch = MathHelper.Clamp(bodyPitch, -0.18f, 0.18f);
        float physicsBodyRoll = MathHelper.Clamp(bodyRoll, -0.14f, 0.14f);
        State.BodyPitchRadians = ScalePresentationBodyAngle(
            physicsBodyPitch,
            State.GroundPitchRadians,
            arcade.VisualBodyPitchScale,
            arcade.VisualBodyPitchLimitRadians);
        State.BodyRollRadians = ScalePresentationBodyAngle(
            physicsBodyRoll,
            State.GroundRollRadians,
            arcade.VisualBodyRollScale,
            arcade.VisualBodyRollLimitRadians);
        State.FrontLeftVisualSuspensionCompressionMeters = _visualSuspensionCompressionMeters[(int)WheelCorner.FrontLeft];
        State.FrontRightVisualSuspensionCompressionMeters = _visualSuspensionCompressionMeters[(int)WheelCorner.FrontRight];
        State.RearLeftVisualSuspensionCompressionMeters = _visualSuspensionCompressionMeters[(int)WheelCorner.RearLeft];
        State.RearRightVisualSuspensionCompressionMeters = _visualSuspensionCompressionMeters[(int)WheelCorner.RearRight];
        State.FrontLeftSupportHeightMeters = supportHeights[(int)WheelCorner.FrontLeft];
        State.FrontRightSupportHeightMeters = supportHeights[(int)WheelCorner.FrontRight];
        State.RearLeftSupportHeightMeters = supportHeights[(int)WheelCorner.RearLeft];
        State.RearRightSupportHeightMeters = supportHeights[(int)WheelCorner.RearRight];

        _dynamicBodyPitchRadians = physicsBodyPitch - State.GroundPitchRadians;
        _dynamicBodyRollRadians = physicsBodyRoll - State.GroundRollRadians;
    }

    private static float ScalePresentationBodyAngle(float physicsBodyAngle, float groundAngle, float scale, float limitRadians)
    {
        float limit = MathF.Max(0.01f, MathF.Abs(limitRadians));
        float suspensionDelta = (physicsBodyAngle - groundAngle) * MathHelper.Clamp(scale, 0f, 1.25f);
        return groundAngle + MathHelper.Clamp(suspensionDelta, -limit, limit);
    }

    private void UpdateVisualSuspension(float dt)
    {
        ArcadeHandlingParameters arcade = _parameters.ArcadeHandling;
        for (int i = 0; i < _wheels.Length; i++)
        {
            WheelRuntimeState wheel = _wheels[i];
            SuspensionGeometryParameters geometry = GetSuspensionGeometry(wheel.Corner);
            float axleVisualScale = IsFrontWheel(wheel.Corner)
                ? arcade.FrontVisualSuspensionMultiplier
                : arcade.RearVisualSuspensionMultiplier;
            WheelRuntimeState oppositeWheel = GetWheel(GetOppositeWheelCorner(wheel.Corner));
            float axleAverageCompression = Average(wheel.SuspensionCompressionMeters, oppositeWheel.SuspensionCompressionMeters);
            float axleAverageLoad = Average(wheel.NormalLoadN, oppositeWheel.NormalLoadN);
            float loadTransferRatio = (wheel.NormalLoadN - axleAverageLoad) / MathF.Max(1f, GetStaticWheelLoad(wheel.Corner));
            float heavePitchCompression = axleAverageCompression *
                                          arcade.VisualSuspensionMotionScale *
                                          arcade.VisualSuspensionHeavePitchScale *
                                          axleVisualScale;
            float rollCompression = loadTransferRatio *
                                    arcade.VisualSuspensionLoadTransferMeters *
                                    axleVisualScale;
            float targetCompression = MathHelper.Clamp(
                heavePitchCompression + rollCompression,
                -geometry.MaxDroopMeters,
                geometry.MaxCompressionMeters);
            float displacement = _visualSuspensionCompressionMeters[i];
            float velocity = _visualSuspensionVelocityMetersPerSecond[i];
            float acceleration = (targetCompression - displacement) * arcade.VisualSuspensionSpringRate -
                                 velocity * arcade.VisualSuspensionDampingRate;

            velocity += acceleration * dt;
            displacement += velocity * dt;
            displacement = MathHelper.Clamp(displacement, -geometry.MaxDroopMeters, geometry.MaxCompressionMeters);
            if ((displacement <= -geometry.MaxDroopMeters && velocity < 0f) ||
                (displacement >= geometry.MaxCompressionMeters && velocity > 0f))
            {
                velocity = 0f;
            }

            _visualSuspensionCompressionMeters[i] = displacement;
            _visualSuspensionVelocityMetersPerSecond[i] = velocity;
        }
    }

    private float CalculateFrontRollWeight()
    {
        float frontRollStiffness = _parameters.FrontSpringRateNPerM * _parameters.FrontTrackMeters * _parameters.FrontTrackMeters * 0.5f +
                                   _parameters.FrontAntiRollBarRateNmPerRad;
        float rearRollStiffness = _parameters.RearSpringRateNPerM * _parameters.RearTrackMeters * _parameters.RearTrackMeters * 0.5f +
                                  _parameters.RearAntiRollBarRateNmPerRad;
        return MathHelper.Clamp(frontRollStiffness / MathF.Max(1f, frontRollStiffness + rearRollStiffness), 0.35f, 0.65f);
    }

    private SuspensionGeometryParameters GetSuspensionGeometry(WheelCorner corner)
    {
        return IsFrontWheel(corner)
            ? _parameters.FrontSuspensionGeometry
            : _parameters.RearSuspensionGeometry;
    }

    private float CalculateAxleRoll(ReadOnlySpan<float> heights, bool front)
    {
        WheelCorner left = front ? WheelCorner.FrontLeft : WheelCorner.RearLeft;
        WheelCorner right = front ? WheelCorner.FrontRight : WheelCorner.RearRight;
        float track = front ? _parameters.FrontTrackMeters : _parameters.RearTrackMeters;
        return MathF.Atan2(heights[(int)right] - heights[(int)left], MathF.Max(0.1f, track));
    }

    private static float Average(float a, float b)
    {
        return (a + b) * 0.5f;
    }

    private static float Average(float a, float b, float c, float d)
    {
        return (a + b + c + d) * 0.25f;
    }

    private static float CalculatePassiveSurfaceForce(float localVelocity, float rollingForceMagnitude, float velocitySquaredDragCoefficient)
    {
        if (MathF.Abs(localVelocity) < 0.05f)
        {
            return 0f;
        }

        float sign = MathF.Sign(localVelocity);
        float plowingForce = velocitySquaredDragCoefficient * localVelocity * localVelocity;
        return -sign * (rollingForceMagnitude + plowingForce);
    }

    private static float CalculateWheelSpinDragTorque(float slipVelocity, float coefficient, float wheelRadius)
    {
        if (coefficient <= 0f || MathF.Abs(slipVelocity) < 0.05f)
        {
            return 0f;
        }

        return coefficient * slipVelocity * MathF.Abs(slipVelocity) * wheelRadius;
    }

    private float CalculateEffectiveWheelInertia(WheelRuntimeState wheel)
    {
        float inertia = _parameters.WheelInertiaKgM2;
        if (!_parameters.DrivenWheels.IsDriven(wheel.Corner) ||
            State.Gear == 0 ||
            _shiftTimerSeconds > 0f ||
            _parameters.DrivenWheels.Count == 0)
        {
            return inertia;
        }

        float ratio = GetCurrentGearRatio();
        if (ratio <= 0f)
        {
            return inertia;
        }

        float totalRatio = ratio * _parameters.FinalDriveRatio;
        float reflectedEngineInertia = _parameters.EngineRotationalInertiaKgM2 * totalRatio * totalRatio / _parameters.DrivenWheels.Count;
        return inertia + reflectedEngineInertia;
    }

    private WallCollisionResult ResolveTrackCollisions(float dt)
    {
        Vector2 center = new(State.Position.X, State.Position.Z);
        Vector2 forward = GetForward();
        Vector2 right = GetRight();
        float halfLength = MathF.Max(1.0f, _parameters.BodyLengthMeters * 0.5f);
        float halfWidth = MathF.Max(0.4f, _parameters.BodyWidthMeters * 0.5f);
        float pointRadius = CalculateWallCollisionPointRadius(halfWidth, halfLength);
        float collisionHalfWidth = MathF.Max(0f, halfWidth - pointRadius);
        float collisionHalfLength = MathF.Max(0f, halfLength - pointRadius);
        int contactCount = 0;
        float maxImpactSpeed = 0f;

        for (int iteration = 0; iteration < 3; iteration++)
        {
            WallContactManifold manifold = BuildWallContactManifold(
                collisionHalfWidth,
                collisionHalfLength,
                pointRadius,
                forward,
                right,
                center);
            if (manifold.ContactCount == 0)
            {
                break;
            }

            contactCount = Math.Max(contactCount, manifold.ContactCount);
            maxImpactSpeed = MathF.Max(maxImpactSpeed, ResolveWallContactManifold(manifold, ref center, dt));
        }

        State.Position = new Vector3(center.X, State.Position.Y, center.Y);
        return new WallCollisionResult(contactCount, maxImpactSpeed * 3.6f);
    }

    private WallContactManifold BuildWallContactManifold(
        float collisionHalfWidth,
        float collisionHalfLength,
        float pointRadius,
        Vector2 forward,
        Vector2 right,
        Vector2 center)
    {
        int contactCount = 0;
        float maximumPenetration = 0f;
        float totalPenetration = 0f;
        float weightedTotal = 0f;
        Vector2 normalSum = Vector2.Zero;
        Vector2 contactPointSum = Vector2.Zero;
        Vector2 deepestNormal = Vector2.UnitX;
        Vector2 deepestContactPoint = center;

        Accumulate(-collisionHalfWidth, collisionHalfLength);
        Accumulate(collisionHalfWidth, collisionHalfLength);
        Accumulate(-collisionHalfWidth, -collisionHalfLength);
        Accumulate(collisionHalfWidth, -collisionHalfLength);
        Accumulate(-collisionHalfWidth, 0f);
        Accumulate(collisionHalfWidth, 0f);
        Accumulate(0f, collisionHalfLength);
        Accumulate(0f, -collisionHalfLength);

        if (contactCount == 0)
        {
            return default;
        }

        Vector2 normal = NormalizeOrFallback(normalSum, deepestNormal);
        Vector2 contactPoint = weightedTotal > 0.0001f
            ? contactPointSum / weightedTotal
            : deepestContactPoint;
        return new WallContactManifold(
            contactCount,
            normal,
            contactPoint,
            maximumPenetration,
            totalPenetration / contactCount);

        void Accumulate(float localX, float localZ)
        {
            Vector2 contactPoint = center + right * localX + forward * localZ;
            if (!_surfaceSampler.TryGetBoundaryHit(contactPoint, pointRadius, out TrackBoundaryHit hit) ||
                hit.PenetrationMeters <= 0f)
            {
                return;
            }

            Vector2 normal = NormalizeOrFallback(hit.Normal, Vector2.UnitX);
            float impactSpeed = MathF.Max(0f, -Vector2.Dot(State.Velocity, normal));
            float weight = 0.08f + hit.PenetrationMeters * 2.5f + impactSpeed * 0.035f;
            Vector2 correctedContactPoint = contactPoint + normal * hit.PenetrationMeters;
            normalSum += normal * weight;
            contactPointSum += correctedContactPoint * weight;
            weightedTotal += weight;
            contactCount++;
            totalPenetration += hit.PenetrationMeters;
            if (hit.PenetrationMeters > maximumPenetration)
            {
                maximumPenetration = hit.PenetrationMeters;
                deepestNormal = normal;
                deepestContactPoint = correctedContactPoint;
            }
        }
    }

    private float ResolveWallContactManifold(WallContactManifold manifold, ref Vector2 center, float dt)
    {
        Vector2 normal = NormalizeOrFallback(manifold.Normal, Vector2.UnitX);
        center += normal * (manifold.MaxPenetrationMeters + 0.004f);

        float normalSpeed = Vector2.Dot(State.Velocity, normal);
        float impactSpeed = normalSpeed < 0f ? -normalSpeed : 0f;
        Vector2 tangent = new(normal.Y, -normal.X);
        float tangentSpeed = Vector2.Dot(State.Velocity, tangent);
        float contactSpeed = impactSpeed + MathF.Abs(tangentSpeed);
        float impactShare = contactSpeed > 0.05f
            ? MathHelper.Clamp(impactSpeed / contactSpeed, 0f, 1f)
            : 0f;
        float directHitT = impactShare * impactShare;

        ArcadeHandlingParameters arcade = _parameters.ArcadeHandling;
        float directOverrideT = SmoothStep(
            arcade.WallDirectImpactBlendStart,
            arcade.WallDirectImpactBlendEnd,
            directHitT);
        float restitution = MathHelper.Clamp(_parameters.WallCollisionRestitution, 0f, 0.45f);
        float directImpactRebound = MathHelper.Clamp(arcade.WallImpactVelocityMultiplier, 0.10f, 0.70f) * 0.56f;
        float reboundMultiplier = MathHelper.Lerp(
            restitution * MathHelper.Lerp(0.25f, 1f, directHitT),
            directImpactRebound,
            directOverrideT);

        float scrapeFriction = MathHelper.Clamp(_parameters.WallScrapeFriction, 0f, 0.30f);
        float impactFriction = MathHelper.Clamp(_parameters.WallImpactFriction, 0f, 0.85f);
        float scrapeLoss = 1f - MathF.Exp(-scrapeFriction * 2.8f * MathF.Max(0f, dt));
        float impactLoss = impactFriction * directHitT * MathHelper.Lerp(0.16f, 0.92f, directHitT);
        float multiContactLoss = MathHelper.Clamp((manifold.ContactCount - 1) * 0.012f, 0f, 0.08f) * directHitT;
        float friction = MathHelper.Clamp(scrapeLoss + impactLoss + multiContactLoss, 0f, 0.68f);
        float newTangentSpeed = tangentSpeed * (1f - friction);

        float separationT = SmoothStep(0.006f, 0.16f, manifold.MaxPenetrationMeters);
        float minimumSeparationSpeed = MathHelper.Lerp(0.16f, 0.72f, separationT) *
                                       MathHelper.Lerp(1f, 0.45f, directHitT);
        float newNormalSpeed = MathF.Max(normalSpeed, minimumSeparationSpeed);
        if (impactSpeed > 0.05f)
        {
            newNormalSpeed = MathF.Max(newNormalSpeed, impactSpeed * reboundMultiplier);
        }

        State.Velocity = tangent * newTangentSpeed + normal * newNormalSpeed;

        if (impactSpeed > 0.25f)
        {
            Vector2 lever = manifold.ContactPoint - center;
            float normalImpulseMagnitude = _parameters.MassKg * (impactSpeed + MathF.Max(0f, newNormalSpeed - normalSpeed));
            Vector2 impulse = normal * normalImpulseMagnitude;
            float torqueImpulse = lever.Y * impulse.X - lever.X * impulse.Y;
            float yawDelta = torqueImpulse / MathF.Max(1f, _parameters.YawInertiaKgM2);
            float yawContactT = SmoothStep(0.04f, 0.20f, impactShare);
            State.YawRateRadiansPerSecond += yawDelta * MathHelper.Clamp(_parameters.WallYawImpulseScale, 0f, 1.25f) * yawContactT;
        }

        float noseIntoWallT = MathF.Max(0f, Vector2.Dot(GetForward(), -normal));
        float slideT = SmoothStep(4f, 20f, MathF.Abs(tangentSpeed));
        if (noseIntoWallT > 0.01f && slideT > 0.001f && MathF.Abs(tangentSpeed) > 0.5f)
        {
            float desiredYawSign = -MathF.Sign(tangentSpeed);
            State.YawRateRadiansPerSecond += desiredYawSign *
                                             noseIntoWallT *
                                             slideT *
                                             MathHelper.Lerp(0.025f, 0.12f, SmoothStep(0.04f, 0.34f, impactShare));
        }

        State.YawRateRadiansPerSecond = MathHelper.Clamp(State.YawRateRadiansPerSecond, -4.5f, 4.5f);
        return impactSpeed;
    }

    private float CalculateWallCollisionPointRadius(float halfWidth, float halfLength)
    {
        float maximumRadius = MathF.Min(0.35f, MathF.Min(halfWidth, halfLength) * 0.45f);
        return MathHelper.Clamp(_parameters.WallCollisionPointRadiusMeters, 0f, maximumRadius);
    }

    private WheelRuntimeState GetWheel(WheelCorner corner)
    {
        return _wheels[(int)corner];
    }

    private Vector2 GetForward()
    {
        return new Vector2(MathF.Sin(State.HeadingRadians), MathF.Cos(State.HeadingRadians));
    }

    private Vector2 GetRight()
    {
        return new Vector2(MathF.Cos(State.HeadingRadians), -MathF.Sin(State.HeadingRadians));
    }

    private static float SignWithFallback(float primary, float fallback)
    {
        if (MathF.Abs(primary) > 0.05f)
        {
            return MathF.Sign(primary);
        }

        if (MathF.Abs(fallback) > 0.05f)
        {
            return MathF.Sign(fallback);
        }

        return 1f;
    }

    private static bool DoesTorqueOpposeTravel(float torque, float forwardSpeed)
    {
        return MathF.Abs(torque) > 0.01f &&
               torque * SignWithFallback(forwardSpeed, torque) < -0.01f;
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float t = MathHelper.Clamp((value - edge0) / MathF.Max(0.001f, edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static Vector2 NormalizeOrFallback(Vector2 value, Vector2 fallback)
    {
        return value.LengthSquared() > 0.000001f
            ? Vector2.Normalize(value)
            : fallback;
    }

    private static WheelRuntimeState[] CreateWheels(VehicleSimulationParameters parameters)
    {
        float distanceToRearAxle = MathHelper.Clamp(parameters.FrontWeightDistribution, 0.1f, 0.9f) * parameters.WheelbaseMeters;
        float distanceToFrontAxle = parameters.WheelbaseMeters - distanceToRearAxle;

        return
        [
            new WheelRuntimeState(WheelCorner.FrontLeft, -parameters.FrontTrackMeters * 0.5f, distanceToFrontAxle, parameters.FrontTyres),
            new WheelRuntimeState(WheelCorner.FrontRight, parameters.FrontTrackMeters * 0.5f, distanceToFrontAxle, parameters.FrontTyres),
            new WheelRuntimeState(WheelCorner.RearLeft, -parameters.RearTrackMeters * 0.5f, -distanceToRearAxle, parameters.RearTyres),
            new WheelRuntimeState(WheelCorner.RearRight, parameters.RearTrackMeters * 0.5f, -distanceToRearAxle, parameters.RearTyres)
        ];
    }

    private readonly record struct SteeringAngles(float FrontLeft, float FrontRight);

    private readonly record struct WheelAlignment(float CamberRadians, float ToeRadians, float CompressionMeters);

    private readonly record struct WheelForceResult(
        float BodyForceX,
        float BodyForceZ,
        float LongitudinalForceN,
        float LateralForceN,
        float SlipRatio,
        float SlipAngleRadians,
        float GripUsage,
        float SurfaceGrip,
        string SurfaceName);

    private readonly record struct WallCollisionResult(int ContactCount, float ImpactSpeedKph);

    private readonly record struct WallContactManifold(
        int ContactCount,
        Vector2 Normal,
        Vector2 ContactPoint,
        float MaxPenetrationMeters,
        float AveragePenetrationMeters);

    private sealed class WheelRuntimeState
    {
        public WheelRuntimeState(WheelCorner corner, float localX, float localZ, TyreAxleParameters tyres)
        {
            Corner = corner;
            LocalX = localX;
            LocalZ = localZ;
            Tyres = tyres;
        }

        public WheelCorner Corner { get; }

        public float LocalX { get; }

        public float LocalZ { get; }

        public TyreAxleParameters Tyres { get; }

        public float AngularVelocityRadiansPerSecond { get; set; }

        public float SteerAngleRadians { get; set; }

        public float NormalLoadN { get; set; }

        public float SlipRatio { get; set; }

        public float SlipAngleRadians { get; set; }

        public float RelaxedSlipAngleRadians { get; set; }

        public float EffectiveCamberRadians { get; set; }

        public float EffectiveToeRadians { get; set; }

        public float SuspensionCompressionMeters { get; set; }

        public float GripUsage { get; set; }

        public float LongitudinalForceN { get; set; }

        public float LateralForceN { get; set; }

        public float SurfaceGrip { get; set; } = 1f;

        public string SurfaceName { get; set; } = "ROAD";

        public float AbsPressureRatio { get; set; } = 1f;

        public bool AbsActive { get; set; }

        public bool IsLocked { get; set; }
    }
}
