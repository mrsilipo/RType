using Microsoft.Xna.Framework;
using RType.World;

namespace RType.Vehicle;

public sealed class SimpleVehicleSimulator : IVehicleSimulator
{
    private const float Gravity = 9.81f;
    private const float SurfaceLoadVibrationMinimumSpeedMetersPerSecond = 1.0f;

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
    private float _revLimiterChatterPhaseSeconds;
    private float _idleCrankPhaseDegrees;
    private float _filteredSteerInput;
    private float _filteredBrakeInput;
    private float _dynamicBodyPitchRadians;
    private float _dynamicBodyRollRadians;
    private float _loadTransferLongitudinalAcceleration;
    private float _loadTransferLateralAcceleration;
    private float _visualLoadTransferLateralAcceleration;
    private float _longitudinalLoadTransferN;
    private float _frontLateralLoadTransferN;
    private float _rearLateralLoadTransferN;
    private float _frontStaticAxleLoadN;
    private float _rearStaticAxleLoadN;
    private float _frontAeroLoadN;
    private float _rearAeroLoadN;
    private float _frontRollShare = 0.5f;
    private float _physicsTimeSeconds;
    private int _curbContactWheelCount;
    private int _surfaceVibrationContactWheelCount;
    private float _surfaceRumbleLeft;
    private float _surfaceRumbleRight;
    private readonly float[] _visualSuspensionCompressionMeters = new float[4];
    private readonly float[] _visualSuspensionVelocityMetersPerSecond = new float[4];
    private bool _digitalBrakeAssistActive;
    private float _recentBrakeSteeringBoostSeconds;
    private float _ffLsdCornerExitBite;
    private float _ffLsdInsideFrontMaxTorqueNm;
    private float _ffLsdOutsideFrontMaxTorqueNm;
    private float _ffLsdManagedFrontAxleTorqueNm;
    private float _ffLsdFrontLeftActualTorqueNm;
    private float _ffLsdFrontRightActualTorqueNm;
    private string _ffLsdLowGripAnchor = string.Empty;
    private float _frontDifferentialCornerExitBite;
    private AxleTorqueResult _frontDifferentialTorqueResult;
    private AxleTorqueResult _rearDifferentialTorqueResult;
    private readonly float[] _lastDriveTorquesNm = new float[4];
    private float _frontDriveTorqueSteerYawMomentNm;
    private float _steeringFrontGripReserve = 1f;
    private float _steeringCommittedTurnAuthority;
    private float _steeringSpeedMatchedMaxAngleRadians;
    private float _steeringForwardForceClampN;
    private float _rpmScrubIsolationIntensity;

    public VehicleSimulationParameters Parameters => _parameters;

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
            PowerRedlineRpm = _parameters.LimiterHardCutRpm,
            LimiterHardCutRpm = _parameters.LimiterHardCutRpm,
            LimiterResumeRpm = _parameters.RevLimiterResumeRpm,
            MaxGaugeRpm = _parameters.MaxGaugeRpm,
            Position = startPosition,
            HeadingRadians = startHeadingRadians,
            Gear = 1,
            Rpm = _parameters.IdleRpm,
            PreviousPhysicsRpm = _parameters.IdleRpm,
            EngineOmegaRadiansPerSecond = RpmToOmega(_parameters.IdleRpm),
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
        SteeringAngles steeringAngles = CalculateSteeringAngles(0f, 0f, 0f, 0f);
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
            wheel.ResetTyreRelaxation();
        }

        State.FrontLeftLoadN = normalLoads[(int)WheelCorner.FrontLeft];
        State.FrontRightLoadN = normalLoads[(int)WheelCorner.FrontRight];
        State.RearLeftLoadN = normalLoads[(int)WheelCorner.RearLeft];
        State.RearRightLoadN = normalLoads[(int)WheelCorner.RearRight];
        State.FrontLeftSteerAngleDegrees = MathHelper.ToDegrees(GetWheel(WheelCorner.FrontLeft).SteerAngleRadians);
        State.FrontRightSteerAngleDegrees = MathHelper.ToDegrees(GetWheel(WheelCorner.FrontRight).SteerAngleRadians);
        State.SteeringFrontGripReserve = _steeringFrontGripReserve;
        State.SteeringCommittedTurnAuthority = _steeringCommittedTurnAuthority;
        State.SteeringSpeedMatchedMaxAngleDegrees = MathHelper.ToDegrees(_steeringSpeedMatchedMaxAngleRadians);
        UpdateGroundContactPose(forward, right, 0f);
    }

    private void Step(VehicleInput input, float dt)
    {
        _physicsTimeSeconds += MathF.Max(0f, dt);
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
        UpdateSteeringInput(input.Steer, MathF.Abs(forwardSpeed), steeringBrakeAuthority, driveThrottle, dt);
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
        ApplySurfaceLoadVibration(normalLoads, forward, right, MathF.Abs(forwardSpeed));
        UpdateRevLimiter(throttle, forwardSpeed, dt);
        float totalDriveTorque = CalculateContinuousClutchDriveTorque(input, driveThrottle, forwardSpeed, dt);

        float[] driveTorques = DistributeDriveTorque(totalDriveTorque, normalLoads, driveThrottle);
        SteeringAngles steeringAngles = CalculateSteeringAngles(
            _filteredSteerInput,
            MathF.Abs(forwardSpeed),
            CalculateSteeringBrakeAuthority(brake, input.Brake),
            driveThrottle);
        float[] brakeTorques = CalculateBrakeTorques(brake, input.Handbrake, MathF.Abs(forwardSpeed), MathF.Abs(_filteredSteerInput));

        float totalForceX = 0f;
        float totalForceZ = 0f;
        float yawTorque = 0f;
        float slipRatioTotal = 0f;
        float slipAngleTotal = 0f;
        float gripUsageTotal = 0f;
        float drivenLongitudinalForce = 0f;
        float brakeLongitudinalForce = 0f;
        float rearHandbrakeLockAmount = 0f;
        float rearHandbrakeSlideIntensity = 0f;
        float rearHandbrakeScreechFactor = 0f;
        bool absActive = false;
        int lockedWheelCount = 0;
        string weakestSurface = "ROAD";
        float weakestGrip = 1f;
        float projectedForwardForceLimitN = 0f;
        _rpmScrubIsolationIntensity = 0f;
        bool directRackHandling = _engineParameters.SteeringAssist.DirectRackInput;
        float counterSteerRecoveryT = directRackHandling
            ? 0f
            : CalculateCounterSteerRecoveryT(_filteredSteerInput, forwardSpeed, lateralSpeed);

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
                input.Handbrake,
                forward,
                right,
                forwardSpeed,
                lateralSpeed,
                MathF.Abs(_filteredSteerInput),
                counterSteerRecoveryT,
                dt);
            if (wheel.RequestedLongitudinalForceN > 0f)
            {
                projectedForwardForceLimitN += wheel.RequestedLongitudinalForceN * MathF.Max(0f, MathF.Cos(wheel.SteerAngleRadians));
            }

            totalForceX += force.BodyForceX;
            totalForceZ += force.BodyForceZ;
            float wheelYawTorque = wheel.LocalZ * force.BodyForceX - wheel.LocalX * force.BodyForceZ;
            yawTorque += wheelYawTorque * CalculateSurfaceYawContributionScale(wheel);
            slipRatioTotal += MathF.Abs(force.SlipRatio);
            slipAngleTotal += MathF.Abs(force.SlipAngleRadians);
            gripUsageTotal += force.GripUsage;
            if (!IsFrontWheel(wheel.Corner))
            {
                rearHandbrakeLockAmount = MathF.Max(rearHandbrakeLockAmount, wheel.HandbrakeLockAmount);
                rearHandbrakeSlideIntensity = MathF.Max(rearHandbrakeSlideIntensity, wheel.HandbrakeSlideIntensity);
                rearHandbrakeScreechFactor = MathF.Max(rearHandbrakeScreechFactor, wheel.HandbrakeScreechFactor);
            }

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

        _steeringForwardForceClampN = 0f;
        if (driveThrottle > 0.05f &&
            MathF.Abs(_filteredSteerInput) > 0.05f &&
            projectedForwardForceLimitN > 0f &&
            totalForceZ > projectedForwardForceLimitN)
        {
            _steeringForwardForceClampN = totalForceZ - projectedForwardForceLimitN;
            totalForceZ = projectedForwardForceLimitN;
        }

        totalForceZ += CalculateAeroDrag(forwardSpeed);
        AddTrackGravityForces(forward, right, ref totalForceZ, ref totalForceX);
        _loadTransferLongitudinalAcceleration = totalForceZ / MathF.Max(1f, _parameters.MassKg);
        _loadTransferLateralAcceleration = totalForceX / MathF.Max(1f, _parameters.MassKg);

        float averageSlipAngle = slipAngleTotal / _wheels.Length;
        float averageGripUsage = gripUsageTotal / _wheels.Length;
        ArcadeHandlingParameters arcade = _parameters.ArcadeHandling;
        bool passiveSlideRecoveryNeeded =
            MathF.Abs(lateralSpeed) > arcade.PassiveSlideRecoveryLateralSpeedMetersPerSecond ||
            MathF.Abs(State.YawRateRadiansPerSecond) > MathHelper.ToRadians(arcade.PassiveSlideRecoveryYawRateDegreesPerSecond);
        bool stabilityAssistAllowed = !directRackHandling &&
                                      State.WallContactCount == 0 &&
                                      (MathF.Abs(_filteredSteerInput) > 0.05f ||
                                       driveThrottle > 0.05f ||
                                       brake > 0.05f ||
                                       input.Handbrake > 0.05f ||
                                       passiveSlideRecoveryNeeded);
        Vector2 worldAcceleration = (right * totalForceX + forward * totalForceZ) / _parameters.MassKg;
        worldAcceleration = ApplyCorneringSpeedRetention(
            worldAcceleration,
            MathF.Abs(_filteredSteerInput),
            driveThrottle,
            brake,
            input.Handbrake);
        if (stabilityAssistAllowed)
        {
            worldAcceleration += CalculateStabilityControlAcceleration(
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
        ApplyLowSpeedPivotYawResponse(
            (steeringAngles.FrontLeft + steeringAngles.FrontRight) * 0.5f,
            forwardSpeed,
            _filteredSteerInput,
            dt);
        float yawDampingRate = CalculateYawDampingRate(
            forwardSpeed,
            lateralSpeed,
            averageSlipAngle,
            averageGripUsage,
            _filteredSteerInput,
            driveThrottle,
            brake);
        if (stabilityAssistAllowed)
        {
            yawDampingRate += CalculateStabilityControlYawDampingRate(
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
        State.PhysicalLoadTransferLongitudinalAcceleration = _loadTransferLongitudinalAcceleration;
        State.PhysicalLoadTransferLateralAcceleration = _loadTransferLateralAcceleration;
        State.VisualLoadTransferLateralAcceleration = _visualLoadTransferLateralAcceleration;
        State.LongitudinalLoadTransferN = _longitudinalLoadTransferN;
        State.FrontLateralLoadTransferN = _frontLateralLoadTransferN;
        State.RearLateralLoadTransferN = _rearLateralLoadTransferN;
        State.FrontStaticAxleLoadN = _frontStaticAxleLoadN;
        State.RearStaticAxleLoadN = _rearStaticAxleLoadN;
        State.FrontAeroLoadN = _frontAeroLoadN;
        State.RearAeroLoadN = _rearAeroLoadN;
        State.FrontRollShare = _frontRollShare;
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
        State.SteeringFrontGripReserve = _steeringFrontGripReserve;
        State.SteeringCommittedTurnAuthority = _steeringCommittedTurnAuthority;
        State.SteeringSpeedMatchedMaxAngleDegrees = MathHelper.ToDegrees(_steeringSpeedMatchedMaxAngleRadians);
        State.SteeringForwardForceClampN = _steeringForwardForceClampN;
        State.IsShifting = _shiftTimerSeconds > 0f;
        State.ShiftTimeRemainingSeconds = _shiftTimerSeconds;
        State.EngineBrakeTorqueNm = DoesTorqueOpposeTravel(totalDriveTorque, forwardSpeed)
            ? MathF.Abs(totalDriveTorque)
            : 0f;
        ApplyIdleCrankCycleBounce(input, dt);
        PublishEnginePowerState();
        State.FrontBrakeTorqueNm = brakeTorques[(int)WheelCorner.FrontLeft] + brakeTorques[(int)WheelCorner.FrontRight];
        State.RearBrakeTorqueNm = brakeTorques[(int)WheelCorner.RearLeft] + brakeTorques[(int)WheelCorner.RearRight];
        State.RearHandbrakeLockAmount = rearHandbrakeLockAmount;
        State.RearHandbrakeSlideIntensity = rearHandbrakeSlideIntensity;
        State.RearHandbrakeScreechFactor = rearHandbrakeScreechFactor;
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
        State.FrontLeftRelaxedLongitudinalSlipRatio = GetWheel(WheelCorner.FrontLeft).RelaxedLongitudinalSlipRatio;
        State.FrontRightRelaxedLongitudinalSlipRatio = GetWheel(WheelCorner.FrontRight).RelaxedLongitudinalSlipRatio;
        State.RearLeftRelaxedLongitudinalSlipRatio = GetWheel(WheelCorner.RearLeft).RelaxedLongitudinalSlipRatio;
        State.RearRightRelaxedLongitudinalSlipRatio = GetWheel(WheelCorner.RearRight).RelaxedLongitudinalSlipRatio;
        State.FrontLeftRelaxedLateralSlip = GetWheel(WheelCorner.FrontLeft).RelaxedLateralSlip;
        State.FrontRightRelaxedLateralSlip = GetWheel(WheelCorner.FrontRight).RelaxedLateralSlip;
        State.RearLeftRelaxedLateralSlip = GetWheel(WheelCorner.RearLeft).RelaxedLateralSlip;
        State.RearRightRelaxedLateralSlip = GetWheel(WheelCorner.RearRight).RelaxedLateralSlip;
        State.PeakRawSlipRatio = CalculatePeakRawSlipRatio();
        State.PeakRelaxedLongitudinalSlipRatio = CalculatePeakRelaxedLongitudinalSlipRatio();
        State.PeakRelaxedLateralSlip = CalculatePeakRelaxedLateralSlip();
        State.FrontLeftWheelOmegaRadiansPerSecond = GetWheel(WheelCorner.FrontLeft).AngularVelocityRadiansPerSecond;
        State.FrontRightWheelOmegaRadiansPerSecond = GetWheel(WheelCorner.FrontRight).AngularVelocityRadiansPerSecond;
        State.RearLeftWheelOmegaRadiansPerSecond = GetWheel(WheelCorner.RearLeft).AngularVelocityRadiansPerSecond;
        State.RearRightWheelOmegaRadiansPerSecond = GetWheel(WheelCorner.RearRight).AngularVelocityRadiansPerSecond;
        PublishFrictionEllipseDiagnostics();
        State.FrontLeftSlipAngleDegrees = MathHelper.ToDegrees(GetWheel(WheelCorner.FrontLeft).SlipAngleRadians);
        State.FrontRightSlipAngleDegrees = MathHelper.ToDegrees(GetWheel(WheelCorner.FrontRight).SlipAngleRadians);
        State.RearLeftSlipAngleDegrees = MathHelper.ToDegrees(GetWheel(WheelCorner.RearLeft).SlipAngleRadians);
        State.RearRightSlipAngleDegrees = MathHelper.ToDegrees(GetWheel(WheelCorner.RearRight).SlipAngleRadians);
        State.FrontLeftLongitudinalForceN = GetWheel(WheelCorner.FrontLeft).LongitudinalForceN;
        State.FrontRightLongitudinalForceN = GetWheel(WheelCorner.FrontRight).LongitudinalForceN;
        State.RearLeftLongitudinalForceN = GetWheel(WheelCorner.RearLeft).LongitudinalForceN;
        State.RearRightLongitudinalForceN = GetWheel(WheelCorner.RearRight).LongitudinalForceN;
        State.FrontLeftRequestedLongitudinalForceN = GetWheel(WheelCorner.FrontLeft).RequestedLongitudinalForceN;
        State.FrontRightRequestedLongitudinalForceN = GetWheel(WheelCorner.FrontRight).RequestedLongitudinalForceN;
        State.RearLeftRequestedLongitudinalForceN = GetWheel(WheelCorner.RearLeft).RequestedLongitudinalForceN;
        State.RearRightRequestedLongitudinalForceN = GetWheel(WheelCorner.RearRight).RequestedLongitudinalForceN;
        State.FrontLeftTyreScrubForceN = GetWheel(WheelCorner.FrontLeft).TyreScrubForceN;
        State.FrontRightTyreScrubForceN = GetWheel(WheelCorner.FrontRight).TyreScrubForceN;
        State.RearLeftTyreScrubForceN = GetWheel(WheelCorner.RearLeft).TyreScrubForceN;
        State.RearRightTyreScrubForceN = GetWheel(WheelCorner.RearRight).TyreScrubForceN;
        State.FrontLeftSteeringProjectionForceN = GetWheel(WheelCorner.FrontLeft).SteeringProjectionForceN;
        State.FrontRightSteeringProjectionForceN = GetWheel(WheelCorner.FrontRight).SteeringProjectionForceN;
        State.RearLeftSteeringProjectionForceN = GetWheel(WheelCorner.RearLeft).SteeringProjectionForceN;
        State.RearRightSteeringProjectionForceN = GetWheel(WheelCorner.RearRight).SteeringProjectionForceN;
        State.PeakTyreScrubForceN = CalculatePeakTyreScrubForce();
        State.PeakSteeringProjectionForceN = CalculatePeakSteeringProjectionForce();
        State.RpmScrubIsolationIntensity = _rpmScrubIsolationIntensity;
        State.FfLsdCornerExitBite = _ffLsdCornerExitBite;
        State.FfLsdInsideFrontMaxTorqueNm = _ffLsdInsideFrontMaxTorqueNm;
        State.FfLsdOutsideFrontMaxTorqueNm = _ffLsdOutsideFrontMaxTorqueNm;
        State.FfLsdManagedFrontAxleTorqueNm = _ffLsdManagedFrontAxleTorqueNm;
        State.FfLsdFrontLeftActualTorqueNm = _ffLsdFrontLeftActualTorqueNm;
        State.FfLsdFrontRightActualTorqueNm = _ffLsdFrontRightActualTorqueNm;
        State.FfLsdLowGripAnchor = _ffLsdLowGripAnchor;
        State.FrontDriveTorqueSteerYawMomentNm = _frontDriveTorqueSteerYawMomentNm;
        State.FrontDifferentialCornerExitBite = _frontDifferentialCornerExitBite;
        State.FrontDifferentialManagedAxleTorqueNm = _frontDifferentialTorqueResult.ManagedAxleTorqueNm;
        State.FrontDifferentialLeftActualTorqueNm = _frontDifferentialTorqueResult.LeftWheelTorqueNm;
        State.FrontDifferentialRightActualTorqueNm = _frontDifferentialTorqueResult.RightWheelTorqueNm;
        State.FrontDifferentialLowGripAnchor = _frontDifferentialTorqueResult.LowGripAnchor;
        State.RearDifferentialManagedAxleTorqueNm = _rearDifferentialTorqueResult.ManagedAxleTorqueNm;
        State.RearDifferentialLeftActualTorqueNm = _rearDifferentialTorqueResult.LeftWheelTorqueNm;
        State.RearDifferentialRightActualTorqueNm = _rearDifferentialTorqueResult.RightWheelTorqueNm;
        State.RearDifferentialLowGripAnchor = _rearDifferentialTorqueResult.LowGripAnchor;
        State.FrontLeftDriveTorqueNm = _lastDriveTorquesNm[(int)WheelCorner.FrontLeft];
        State.FrontRightDriveTorqueNm = _lastDriveTorquesNm[(int)WheelCorner.FrontRight];
        State.RearLeftDriveTorqueNm = _lastDriveTorquesNm[(int)WheelCorner.RearLeft];
        State.RearRightDriveTorqueNm = _lastDriveTorquesNm[(int)WheelCorner.RearRight];
        State.FrontLeftLateralForceN = GetWheel(WheelCorner.FrontLeft).LateralForceN;
        State.FrontRightLateralForceN = GetWheel(WheelCorner.FrontRight).LateralForceN;
        State.RearLeftLateralForceN = GetWheel(WheelCorner.RearLeft).LateralForceN;
        State.RearRightLateralForceN = GetWheel(WheelCorner.RearRight).LateralForceN;
        State.FrontLeftSurfaceGrip = GetWheel(WheelCorner.FrontLeft).SurfaceGrip;
        State.FrontRightSurfaceGrip = GetWheel(WheelCorner.FrontRight).SurfaceGrip;
        State.RearLeftSurfaceGrip = GetWheel(WheelCorner.RearLeft).SurfaceGrip;
        State.RearRightSurfaceGrip = GetWheel(WheelCorner.RearRight).SurfaceGrip;
        State.FrontLeftSurfaceMu = GetWheel(WheelCorner.FrontLeft).ActiveSurfaceMu;
        State.FrontRightSurfaceMu = GetWheel(WheelCorner.FrontRight).ActiveSurfaceMu;
        State.RearLeftSurfaceMu = GetWheel(WheelCorner.RearLeft).ActiveSurfaceMu;
        State.RearRightSurfaceMu = GetWheel(WheelCorner.RearRight).ActiveSurfaceMu;
        State.FrontLeftDisplacementDragForceN = GetWheel(WheelCorner.FrontLeft).DisplacementDragForceN;
        State.FrontRightDisplacementDragForceN = GetWheel(WheelCorner.FrontRight).DisplacementDragForceN;
        State.RearLeftDisplacementDragForceN = GetWheel(WheelCorner.RearLeft).DisplacementDragForceN;
        State.RearRightDisplacementDragForceN = GetWheel(WheelCorner.RearRight).DisplacementDragForceN;
        State.FrontLeftCurbLoadMultiplier = GetWheel(WheelCorner.FrontLeft).CurbLoadMultiplier;
        State.FrontRightCurbLoadMultiplier = GetWheel(WheelCorner.FrontRight).CurbLoadMultiplier;
        State.RearLeftCurbLoadMultiplier = GetWheel(WheelCorner.RearLeft).CurbLoadMultiplier;
        State.RearRightCurbLoadMultiplier = GetWheel(WheelCorner.RearRight).CurbLoadMultiplier;
        State.CurbContactWheelCount = _curbContactWheelCount;
        State.FrontLeftSurfaceLoadMultiplier = GetWheel(WheelCorner.FrontLeft).SurfaceLoadMultiplier;
        State.FrontRightSurfaceLoadMultiplier = GetWheel(WheelCorner.FrontRight).SurfaceLoadMultiplier;
        State.RearLeftSurfaceLoadMultiplier = GetWheel(WheelCorner.RearLeft).SurfaceLoadMultiplier;
        State.RearRightSurfaceLoadMultiplier = GetWheel(WheelCorner.RearRight).SurfaceLoadMultiplier;
        State.SurfaceVibrationContactWheelCount = _surfaceVibrationContactWheelCount;
        State.SurfaceRumbleLeft = _surfaceRumbleLeft;
        State.SurfaceRumbleRight = _surfaceRumbleRight;
        State.FrontLeftSurfaceBlend = GetWheel(WheelCorner.FrontLeft).SurfaceBlendWeight;
        State.FrontRightSurfaceBlend = GetWheel(WheelCorner.FrontRight).SurfaceBlendWeight;
        State.RearLeftSurfaceBlend = GetWheel(WheelCorner.RearLeft).SurfaceBlendWeight;
        State.RearRightSurfaceBlend = GetWheel(WheelCorner.RearRight).SurfaceBlendWeight;
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
        FinalizeLimiterAndOverRevRecovery(forwardSpeed);
    }

    private void StepRaceStartHold(VehicleInput input, float dt)
    {
        State.CrashFlashSeconds = MathF.Max(0f, State.CrashFlashSeconds - dt);
        UpdateShiftTimer(dt);
        UpdateGear(input, 0f);

        float throttle = State.Gear < 0 ? input.Reverse : input.Throttle;
        UpdateSteeringInput(input.Steer, 0f, input.Brake, input.Throttle, dt);
        UpdateBrakeInput(input.Brake, dt);
        UpdateHeldLaunchRpm(throttle, dt);
        UpdateRevLimiter(throttle, 0f, dt);

        State.Velocity = Vector2.Zero;
        State.YawRateRadiansPerSecond = 0f;
        _pendingShiftKickSeverity = 0f;
        _shiftKickSeconds = 0f;
        _shiftKickDurationSeconds = 0f;
        _shiftKickSeverity = 0f;
        _enginePowerShiftHandoffSmoothSeconds = 0f;
        PublishClutchState(0f, State.Rpm, 0f, false, 0f);
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
        SteeringAngles steeringAngles = CalculateSteeringAngles(_filteredSteerInput, 0f, 0f, input.Throttle);
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
        float handbrakeInput,
        Vector2 forward,
        Vector2 right,
        float forwardSpeed,
        float lateralSpeed,
        float absSteerInput,
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
        UpdateTyreRelaxationState(wheel, wheelLongitudinalVelocity, wheelLateralVelocity, radius, dt);
        bool isRearWheel = !IsFrontWheel(wheel.Corner);
        float handbrakeRearTorqueNm = isRearWheel
            ? _parameters.Brakes.HandbrakeRearTorqueNm * MathHelper.Clamp(handbrakeInput, 0f, 1f)
            : 0f;
        float serviceBrakeTorqueNm = MathF.Max(0f, brakeTorqueNm - handbrakeRearTorqueNm);

        RecoverFreeRollingWheelSpeed(
            wheel,
            driveTorqueNm,
            brakeTorqueNm,
            wheelLongitudinalVelocity,
            radius,
            _engineParameters.VehicleSafety.MinimumSlipSpeedMetersPerSecond,
            dt);
        RecoverReleasedHandbrakeWheelSpeed(
            wheel,
            surface,
            handbrakeRearTorqueNm,
            serviceBrakeTorqueNm,
            wheelLongitudinalVelocity,
            radius,
            dt);
        float slipRatio = CalculateSlipRatio(
            wheel,
            wheelLongitudinalVelocity,
            radius,
            _engineParameters.VehicleSafety.MinimumSlipSpeedMetersPerSecond);
        float stableLongitudinalSlipRatio = SelectStableLongitudinalSlipRatio(wheel, slipRatio, wheelLongitudinalVelocity);
        float relaxedTotalSlip = MathF.Sqrt(
            wheel.RelaxedLongitudinalSlipRatio * wheel.RelaxedLongitudinalSlipRatio +
            wheel.RelaxedLateralSlip * wheel.RelaxedLateralSlip);
        float activeSurfaceMu = CalculateActiveSurfaceMu(surface, relaxedTotalSlip, wheelLongitudinalVelocity);
        float slipAngle = MathHelper.Clamp(
            MathF.Atan2(wheelLateralVelocity, MathF.Max(1.5f, MathF.Abs(wheelLongitudinalVelocity))),
            -0.75f,
            0.75f);

        float gripLimit = CalculateGripLimit(wheel, activeSurfaceMu);
        float ellipseShape = CalculateFrictionEllipseShape(surface);
        float wheelBrakeSign = SignWithFallback(wheel.AngularVelocityRadiansPerSecond, wheelLongitudinalVelocity);
        float effectiveServiceBrakeTorqueNm = ApplyAbs(wheel, serviceBrakeTorqueNm, stableLongitudinalSlipRatio, wheelLongitudinalVelocity, dt);
        float effectiveBrakeTorqueNm = effectiveServiceBrakeTorqueNm + handbrakeRearTorqueNm;
        float wheelRecoveryT = IsFrontWheel(wheel.Corner)
            ? counterSteerRecoveryT
            : counterSteerRecoveryT * 0.55f;
        float effectiveSlipAngle = UpdateRelaxedSlipAngle(wheel, slipAngle, wheelLongitudinalVelocity, wheelRecoveryT, dt);
        float requestedLongitudinalForce = CalculateRequestedLongitudinalTyreForce(
            driveTorqueNm,
            effectiveBrakeTorqueNm,
            wheelBrakeSign,
            radius);
        UnifiedTyreForceResult tyreForce = UpdateUnifiedTyreForce(
            wheel,
            gripLimit,
            requestedLongitudinalForce,
            ellipseShape,
            CalculateFrictionEllipseSlidingFloor(),
            CalculateLateralLongitudinalGripCoupling());
        float tyreLateralForce = tyreForce.LateralForceN;
        float tyreLongitudinalForce = tyreForce.LongitudinalForceN;
        if (requestedLongitudinalForce > 0f)
        {
            tyreLongitudinalForce = RemoveSteeringProjectionDriveBoost(
                tyreLongitudinalForce,
                tyreLateralForce,
                requestedLongitudinalForce,
                sinSteer,
                cosSteer);
        }
        tyreLongitudinalForce = PreventFreeRollingWheelPropulsion(
            tyreLongitudinalForce,
            driveTorqueNm,
            effectiveBrakeTorqueNm,
            wheelLongitudinalVelocity);
        float handbrakeLockAmount = CalculateRearHandbrakeLockAmount(isRearWheel, handbrakeInput, stableLongitudinalSlipRatio, wheelLongitudinalVelocity);
        float gripUsage = tyreForce.GripUsage;

        UpdateSurfaceDragScale(wheel, surface, dt);
        float rollingResistanceForce =
            wheel.Tyres.RollingResistanceCoefficient *
            MathHelper.Lerp(1f, surface.RollingResistanceMultiplier, wheel.SurfaceDragScale) *
            wheel.NormalLoadN;
        float displacementDragForce =
            wheel.NormalLoadN *
            MathF.Max(0f, surface.DisplacementDragCoefficient) *
            wheel.SurfaceDragScale;
        float passiveLongitudinalForce = CalculatePassiveSurfaceForce(
            wheelLongitudinalVelocity,
            rollingResistanceForce + displacementDragForce,
            surface.LongitudinalDragCoefficient * wheel.SurfaceDragScale);
        float passiveLateralForce = CalculatePassiveSurfaceForce(
            wheelLateralVelocity,
            0f,
            surface.LateralDragCoefficient * wheel.SurfaceDragScale);
        float scrubLongitudinalForce = CalculateTyreScrubForce(
            tyreLateralForce,
            wheelLateralVelocity,
            wheelLongitudinalVelocity,
            wheel.Tyres.LateralScrubDragCoefficient,
            CalculateMaximumTyreScrubDragForce(wheel, surface));
        wheel.TyreScrubForceN = scrubLongitudinalForce;

        float wheelSurfaceSpeed = wheel.AngularVelocityRadiansPerSecond * radius;
        float wheelSpinDragTorque = CalculateWheelSpinDragTorque(
            wheelSurfaceSpeed - wheelLongitudinalVelocity,
            surface.WheelSpinDragCoefficient * wheel.SurfaceDragScale,
            radius);
        float wheelTorque =
            driveTorqueNm -
            wheelBrakeSign * effectiveBrakeTorqueNm -
            tyreLongitudinalForce * radius -
            wheelSpinDragTorque;
        float previousAngularVelocity = wheel.AngularVelocityRadiansPerSecond;
        wheel.AngularVelocityRadiansPerSecond += wheelTorque / MathF.Max(0.1f, CalculateEffectiveWheelInertia(wheel)) * dt;
        if (ShouldSynchronizeTorqueBalancedDrivenWheel(wheel, driveTorqueNm, effectiveBrakeTorqueNm, requestedLongitudinalForce, tyreLongitudinalForce))
        {
            SynchronizeDrivenRollingWheelSpeed(
                wheel,
                driveTorqueNm,
                wheelLongitudinalVelocity,
                radius,
                dt);
        }
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

        SettleFreeWheelAtRest(
            wheel,
            driveTorqueNm,
            brakeTorqueNm,
            handbrakeInput,
            wheelLongitudinalVelocity,
            State.SpeedMetersPerSecond,
            dt);

        float reportedSlipRatio = CalculateSlipRatio(
            wheel,
            wheelLongitudinalVelocity,
            radius,
            _engineParameters.VehicleSafety.MinimumSlipSpeedMetersPerSecond);
        wheel.SlipRatio = reportedSlipRatio;
        wheel.SlipAngleRadians = effectiveSlipAngle;
        wheel.HandbrakeLockAmount = handbrakeLockAmount;
        wheel.HandbrakeSlideIntensity = CalculateRearHandbrakeSlideIntensity(surface, handbrakeLockAmount, wheelLongitudinalVelocity, wheelLateralVelocity);
        wheel.HandbrakeScreechFactor = surface.HandbrakeScreechFactor;
        wheel.GripUsage = MathHelper.Clamp(gripUsage, 0f, 1.5f);
        wheel.IsLocked = effectiveBrakeTorqueNm > 1f &&
                         MathF.Abs(wheelLongitudinalVelocity) > _parameters.Brakes.Abs.MinimumSpeedMetersPerSecond &&
                         MathF.Abs(wheel.AngularVelocityRadiansPerSecond * radius) < MathF.Abs(wheelLongitudinalVelocity) * 0.12f;

        float totalLongitudinalForce = tyreLongitudinalForce + passiveLongitudinalForce + scrubLongitudinalForce;
        float totalLateralForce = tyreLateralForce + passiveLateralForce;
        if (isRearWheel)
        {
            totalLateralForce = ApplyLowSpeedPivotRearLateralRelease(
                totalLateralForce,
                forwardSpeed,
                absSteerInput,
                _engineParameters.SteeringAssist);
        }
        wheel.LongitudinalForceN = totalLongitudinalForce;
        wheel.RequestedLongitudinalForceN = requestedLongitudinalForce;
        wheel.LateralForceN = totalLateralForce;
        wheel.SurfaceGrip = surface.Grip;
        wheel.StaticSurfaceMu = surface.StaticFrictionCoefficient;
        wheel.DynamicSurfaceMu = surface.DynamicFrictionCoefficient;
        wheel.OptimalSurfaceSlipRatio = surface.OptimalSlipRatio;
        wheel.ActiveSurfaceMu = activeSurfaceMu;
        wheel.DisplacementDragForceN = displacementDragForce;
        wheel.SurfaceBlendWeight = surface.BlendWeight;
        wheel.SurfaceName = surface.Name;
        float bodyForceX = totalLongitudinalForce * sinSteer + totalLateralForce * cosSteer;
        float lateralProjectionZ = -totalLateralForce * sinSteer;
        float lateralProjectionScale = requestedLongitudinalForce > 0f
            ? _engineParameters.SteeringAssist.PoweredLateralForceForwardProjectionScale
            : _engineParameters.SteeringAssist.LateralForceForwardProjectionScale;
        lateralProjectionZ = ScaleSteeringLateralProjectionDrag(
            lateralProjectionZ,
            forwardSpeed,
            lateralProjectionScale);
        wheel.SteeringProjectionForceN = lateralProjectionZ;
        float bodyForceZ = totalLongitudinalForce * cosSteer + lateralProjectionZ;
        if (requestedLongitudinalForce > 0f)
        {
            float maximumBodyForwardForce =
                requestedLongitudinalForce * cosSteer +
                MathF.Min(0f, passiveLongitudinalForce + scrubLongitudinalForce);
            bodyForceZ = MathF.Min(bodyForceZ, maximumBodyForwardForce);
        }

        return new WheelForceResult(
            bodyForceX,
            bodyForceZ,
            totalLongitudinalForce,
            totalLateralForce,
            reportedSlipRatio,
            effectiveSlipAngle,
            wheel.GripUsage,
            surface.Grip,
            activeSurfaceMu,
            wheel.DisplacementDragForceN,
            surface.Name);
    }

    private static float CalculateActiveSurfaceMu(SurfaceSample surface, float slipRatio, float wheelLongitudinalVelocity)
    {
        float staticMu = MathF.Max(0.01f, surface.StaticFrictionCoefficient);
        float dynamicMu = MathHelper.Clamp(surface.DynamicFrictionCoefficient, 0.01f, staticMu);
        float optimalSlip = MathF.Max(0.01f, surface.OptimalSlipRatio);
        float absSlip = MathF.Abs(slipRatio);
        if (absSlip <= optimalSlip)
        {
            return staticMu;
        }

        float slideT = MathHelper.Clamp((absSlip - optimalSlip) / 0.5f, 0f, 1f);
        float speedConfidence = SmoothStep(0.75f, 4.0f, MathF.Abs(wheelLongitudinalVelocity));
        return MathHelper.Lerp(staticMu, MathHelper.Lerp(staticMu, dynamicMu, slideT), speedConfidence);
    }

    private static float SelectStableLongitudinalSlipRatio(
        WheelRuntimeState wheel,
        float fallbackRawSlipRatio,
        float wheelLongitudinalVelocity)
    {
        float relaxed = wheel.RelaxedLongitudinalSlipRatio;
        if (float.IsNaN(relaxed) || float.IsInfinity(relaxed))
        {
            return fallbackRawSlipRatio;
        }

        float rawBlend = SmoothStep(1.4f, 5.5f, MathF.Abs(wheelLongitudinalVelocity));
        return MathHelper.Lerp(relaxed, fallbackRawSlipRatio, rawBlend);
    }

    private static void SettleFreeWheelAtRest(
        WheelRuntimeState wheel,
        float driveTorqueNm,
        float brakeTorqueNm,
        float handbrakeInput,
        float wheelLongitudinalVelocity,
        float vehicleSpeedMetersPerSecond,
        float dt)
    {
        if (MathF.Abs(driveTorqueNm) > 0.1f ||
            brakeTorqueNm > 0.1f ||
            handbrakeInput > 0.01f ||
            vehicleSpeedMetersPerSecond >= 0.50f ||
            MathF.Abs(wheelLongitudinalVelocity) >= 0.35f)
        {
            return;
        }

        if (vehicleSpeedMetersPerSecond < 0.05f && MathF.Abs(wheelLongitudinalVelocity) < 0.05f)
        {
            wheel.AngularVelocityRadiansPerSecond = 0f;
            wheel.ResetTyreRelaxation();
            return;
        }

        float settle = 1f - MathF.Exp(-MathHelper.Clamp(dt, 0f, 1f / 20f) * 42f);
        wheel.AngularVelocityRadiansPerSecond = MathHelper.Lerp(
            wheel.AngularVelocityRadiansPerSecond,
            0f,
            MathHelper.Clamp(settle, 0f, 1f));
        if (MathF.Abs(wheel.AngularVelocityRadiansPerSecond) < 0.03f)
        {
            wheel.AngularVelocityRadiansPerSecond = 0f;
        }

        if (MathF.Abs(wheel.AngularVelocityRadiansPerSecond) < 0.35f)
        {
            wheel.ResetTyreRelaxation();
        }
    }

    private static void UpdateSurfaceDragScale(WheelRuntimeState wheel, SurfaceSample surface, float dt)
    {
        float target = HasSurfaceDrag(surface) ? 1f : 0f;
        float rate = target > wheel.SurfaceDragScale ? 2.2f : 8.0f;
        float maxStep = MathF.Max(0f, dt) * rate;
        wheel.SurfaceDragScale += MathHelper.Clamp(target - wheel.SurfaceDragScale, -maxStep, maxStep);
        wheel.SurfaceDragScale = MathHelper.Clamp(wheel.SurfaceDragScale, 0f, 1f);
    }

    private static bool HasSurfaceDrag(SurfaceSample surface)
    {
        return surface.RollingResistanceMultiplier > 1.01f ||
               surface.LongitudinalDragCoefficient > 0.001f ||
               surface.LateralDragCoefficient > 0.001f ||
               surface.WheelSpinDragCoefficient > 0.001f ||
               surface.DisplacementDragCoefficient > 0.001f;
    }

    private static float CalculateSurfaceYawContributionScale(WheelRuntimeState wheel)
    {
        if (wheel.SurfaceName.Equals("CURB_GRASS", StringComparison.OrdinalIgnoreCase))
        {
            return MathHelper.Lerp(1f, 0.50f, wheel.SurfaceBlendWeight);
        }

        if (wheel.SurfaceName.Equals("GRASS", StringComparison.OrdinalIgnoreCase))
        {
            return 0.50f;
        }

        if (wheel.SurfaceName.Equals("DIRT", StringComparison.OrdinalIgnoreCase))
        {
            return 0.62f;
        }

        return 1f;
    }

    private static void UpdateTyreRelaxationState(
        WheelRuntimeState wheel,
        float wheelLongitudinalVelocity,
        float wheelLateralVelocity,
        float radius,
        float dt)
    {
        float safeLength = MathF.Max(0.01f, wheel.TyreRelaxationLengthMeters);
        float wheelSurfaceSpeed = wheel.AngularVelocityRadiansPerSecond * radius;
        float slipVelocityLong = wheelSurfaceSpeed - wheelLongitudinalVelocity;
        float slipVelocityLat = -wheelLateralVelocity;
        float forwardSpeedMagnitude = MathF.Abs(wheelLongitudinalVelocity);
        float contactSpeedMagnitude = MathF.Max(forwardSpeedMagnitude, MathF.Abs(wheelSurfaceSpeed));
        float clampedDt = MathHelper.Clamp(dt, 0f, 1f / 20f);
        float maxStableDamping = clampedDt > 0.0001f
            ? 1.5f / clampedDt
            : 1500f;
        float rollingDamping = MathF.Min(forwardSpeedMagnitude / safeLength, maxStableDamping);
        float staticCarcassDamping =
            MathHelper.Lerp(28f, 0f, SmoothStep(0.15f, 1.8f, contactSpeedMagnitude));

        float longDeflectionChange =
            slipVelocityLong -
            (rollingDamping + staticCarcassDamping) *
            wheel.LongitudinalTyreDeflectionMeters;
        float latDeflectionChange =
            slipVelocityLat -
            (rollingDamping + staticCarcassDamping) *
            wheel.LateralTyreDeflectionMeters;

        wheel.LongitudinalTyreDeflectionMeters += longDeflectionChange * clampedDt;
        wheel.LateralTyreDeflectionMeters += latDeflectionChange * clampedDt;

        float maxPhysicalStretch = MathF.Max(0.05f, radius * 1.5f);
        wheel.LongitudinalTyreDeflectionMeters = MathHelper.Clamp(
            wheel.LongitudinalTyreDeflectionMeters,
            -maxPhysicalStretch,
            maxPhysicalStretch);
        wheel.LateralTyreDeflectionMeters = MathHelper.Clamp(
            wheel.LateralTyreDeflectionMeters,
            -maxPhysicalStretch,
            maxPhysicalStretch);

        wheel.RelaxedLongitudinalSlipRatio = MathHelper.Clamp(
            wheel.LongitudinalTyreDeflectionMeters / safeLength,
            -4f,
            4f);
        wheel.RelaxedLateralSlip = MathHelper.Clamp(
            wheel.LateralTyreDeflectionMeters / safeLength,
            -4f,
            4f);
    }

    private float CalculatePeakRawSlipRatio()
    {
        float peak = 0f;
        for (int i = 0; i < _wheels.Length; i++)
        {
            peak = MathF.Max(peak, MathF.Abs(_wheels[i].SlipRatio));
        }

        return peak;
    }

    private float CalculatePeakRelaxedLongitudinalSlipRatio()
    {
        float peak = 0f;
        for (int i = 0; i < _wheels.Length; i++)
        {
            peak = MathF.Max(peak, MathF.Abs(_wheels[i].RelaxedLongitudinalSlipRatio));
        }

        return peak;
    }

    private float CalculatePeakRelaxedLateralSlip()
    {
        float peak = 0f;
        for (int i = 0; i < _wheels.Length; i++)
        {
            peak = MathF.Max(peak, MathF.Abs(_wheels[i].RelaxedLateralSlip));
        }

        return peak;
    }

    private float CalculatePeakTyreScrubForce()
    {
        float peak = 0f;
        for (int i = 0; i < _wheels.Length; i++)
        {
            peak = MathF.Max(peak, MathF.Abs(_wheels[i].TyreScrubForceN));
        }

        return peak;
    }

    private float CalculatePeakSteeringProjectionForce()
    {
        float peak = 0f;
        for (int i = 0; i < _wheels.Length; i++)
        {
            peak = MathF.Max(peak, MathF.Abs(_wheels[i].SteeringProjectionForceN));
        }

        return peak;
    }

    private static float CalculateFrictionEllipseShape(SurfaceSample surface)
    {
        return 1f;
    }

    private float CalculateFrictionEllipseSlidingFloor()
    {
        return MathHelper.Clamp(_engineParameters.TyreForce.SlidingForceFloor, 0f, 1f);
    }

    private float CalculateLateralLongitudinalGripCoupling()
    {
        return MathHelper.Clamp(_engineParameters.TyreForce.LateralLongitudinalGripCoupling, 0f, 1f);
    }

    private float CalculateMaximumTyreScrubDragForce(WheelRuntimeState wheel, SurfaceSample surface)
    {
        float dynamicMu = MathF.Max(0.01f, surface.DynamicFrictionCoefficient);
        float multiplier = MathHelper.Clamp(_engineParameters.TyreForce.ScrubDragLimitMultiplier, 0f, 2f);
        return MathF.Max(0f, wheel.NormalLoadN) *
               dynamicMu *
               MathF.Max(0.01f, wheel.Tyres.PeakFriction) *
               multiplier;
    }

    private static UnifiedTyreForceResult UpdateUnifiedTyreForce(
        WheelRuntimeState wheel,
        float gripLimitN,
        float requestedLongitudinalForceN,
        float tyreShape,
        float slidingCurveFloor,
        float lateralLongitudinalGripCoupling)
    {
        UnifiedTyreForceResult result = UnifiedTyreForceModel.CalculateFromRequest(
            new TyreForceRequest(
                gripLimitN,
                requestedLongitudinalForceN,
                wheel.RelaxedLongitudinalSlipRatio,
                wheel.RelaxedLateralSlip,
                MathF.Max(0.01f, wheel.Tyres.LongitudinalPeakSlipRatio),
                MathF.Max(0.01f, wheel.Tyres.LateralPeakSlipAngleRadians)),
            tyreShape,
            slidingCurveFloor,
            lateralLongitudinalGripCoupling);
        UnifiedTyreForceDiagnostics diagnostics = result.Diagnostics;
        wheel.FrictionEllipseTotalSlip = diagnostics.TotalSlip;
        wheel.FrictionEllipseGripBudgetN = diagnostics.GripBudgetN;
        wheel.FrictionEllipseLongitudinalShare = diagnostics.LongitudinalShare;
        wheel.FrictionEllipseLateralShare = diagnostics.LateralShare;
        wheel.FrictionEllipseLongitudinalForceN = diagnostics.LongitudinalForceN;
        wheel.FrictionEllipseLateralForceN = diagnostics.LateralForceN;
        wheel.FrictionEllipseTotalForceN = diagnostics.TotalForceN;
        wheel.FrictionEllipseGripUsage = result.GripUsage;
        return result;
    }

    private void PublishFrictionEllipseDiagnostics()
    {
        WheelRuntimeState fl = GetWheel(WheelCorner.FrontLeft);
        WheelRuntimeState fr = GetWheel(WheelCorner.FrontRight);
        WheelRuntimeState rl = GetWheel(WheelCorner.RearLeft);
        WheelRuntimeState rr = GetWheel(WheelCorner.RearRight);

        State.FrontLeftFrictionEllipseTotalSlip = fl.FrictionEllipseTotalSlip;
        State.FrontRightFrictionEllipseTotalSlip = fr.FrictionEllipseTotalSlip;
        State.RearLeftFrictionEllipseTotalSlip = rl.FrictionEllipseTotalSlip;
        State.RearRightFrictionEllipseTotalSlip = rr.FrictionEllipseTotalSlip;
        State.FrontLeftFrictionEllipseGripBudgetN = fl.FrictionEllipseGripBudgetN;
        State.FrontRightFrictionEllipseGripBudgetN = fr.FrictionEllipseGripBudgetN;
        State.RearLeftFrictionEllipseGripBudgetN = rl.FrictionEllipseGripBudgetN;
        State.RearRightFrictionEllipseGripBudgetN = rr.FrictionEllipseGripBudgetN;
        State.FrontLeftFrictionEllipseLongitudinalShare = fl.FrictionEllipseLongitudinalShare;
        State.FrontRightFrictionEllipseLongitudinalShare = fr.FrictionEllipseLongitudinalShare;
        State.RearLeftFrictionEllipseLongitudinalShare = rl.FrictionEllipseLongitudinalShare;
        State.RearRightFrictionEllipseLongitudinalShare = rr.FrictionEllipseLongitudinalShare;
        State.FrontLeftFrictionEllipseLateralShare = fl.FrictionEllipseLateralShare;
        State.FrontRightFrictionEllipseLateralShare = fr.FrictionEllipseLateralShare;
        State.RearLeftFrictionEllipseLateralShare = rl.FrictionEllipseLateralShare;
        State.RearRightFrictionEllipseLateralShare = rr.FrictionEllipseLateralShare;
        State.FrontLeftFrictionEllipseLongitudinalForceN = fl.FrictionEllipseLongitudinalForceN;
        State.FrontRightFrictionEllipseLongitudinalForceN = fr.FrictionEllipseLongitudinalForceN;
        State.RearLeftFrictionEllipseLongitudinalForceN = rl.FrictionEllipseLongitudinalForceN;
        State.RearRightFrictionEllipseLongitudinalForceN = rr.FrictionEllipseLongitudinalForceN;
        State.FrontLeftFrictionEllipseLateralForceN = fl.FrictionEllipseLateralForceN;
        State.FrontRightFrictionEllipseLateralForceN = fr.FrictionEllipseLateralForceN;
        State.RearLeftFrictionEllipseLateralForceN = rl.FrictionEllipseLateralForceN;
        State.RearRightFrictionEllipseLateralForceN = rr.FrictionEllipseLateralForceN;
        State.FrontLeftFrictionEllipseTotalForceN = fl.FrictionEllipseTotalForceN;
        State.FrontRightFrictionEllipseTotalForceN = fr.FrictionEllipseTotalForceN;
        State.RearLeftFrictionEllipseTotalForceN = rl.FrictionEllipseTotalForceN;
        State.RearRightFrictionEllipseTotalForceN = rr.FrictionEllipseTotalForceN;
        State.FrontLeftFrictionEllipseGripUsage = fl.FrictionEllipseGripUsage;
        State.FrontRightFrictionEllipseGripUsage = fr.FrictionEllipseGripUsage;
        State.RearLeftFrictionEllipseGripUsage = rl.FrictionEllipseGripUsage;
        State.RearRightFrictionEllipseGripUsage = rr.FrictionEllipseGripUsage;
        State.PeakFrictionEllipseTotalSlip = MathF.Max(
            MathF.Max(fl.FrictionEllipseTotalSlip, fr.FrictionEllipseTotalSlip),
            MathF.Max(rl.FrictionEllipseTotalSlip, rr.FrictionEllipseTotalSlip));
        State.PeakFrictionEllipseGripUsage = MathF.Max(
            MathF.Max(fl.FrictionEllipseGripUsage, fr.FrictionEllipseGripUsage),
            MathF.Max(rl.FrictionEllipseGripUsage, rr.FrictionEllipseGripUsage));
    }

    private static float CalculateRequestedLongitudinalTyreForce(
        float driveTorqueNm,
        float brakeTorqueNm,
        float wheelBrakeSign,
        float radius)
    {
        if (radius <= 0.01f)
        {
            return 0f;
        }

        return (driveTorqueNm - wheelBrakeSign * brakeTorqueNm) / radius;
    }

    private static float RemoveSteeringProjectionDriveBoost(
        float longitudinalForce,
        float lateralForce,
        float requestedLongitudinalForce,
        float sinSteer,
        float cosSteer)
    {
        if (requestedLongitudinalForce <= 0f || cosSteer <= 0.05f)
        {
            return longitudinalForce;
        }

        float currentBodyForwardForce = longitudinalForce * cosSteer - lateralForce * sinSteer;
        float requestedBodyForwardForce = requestedLongitudinalForce * cosSteer;
        if (currentBodyForwardForce <= requestedBodyForwardForce)
        {
            return longitudinalForce;
        }

        float excessForwardForce = currentBodyForwardForce - requestedBodyForwardForce;
        return longitudinalForce - excessForwardForce / cosSteer;
    }

    private static float PreventFreeRollingWheelPropulsion(
        float longitudinalForce,
        float driveTorqueNm,
        float brakeTorqueNm,
        float wheelLongitudinalVelocity)
    {
        if (MathF.Abs(driveTorqueNm) > 0.1f ||
            brakeTorqueNm > 0.1f ||
            MathF.Abs(wheelLongitudinalVelocity) < 0.05f ||
            longitudinalForce * wheelLongitudinalVelocity <= 0f)
        {
            return longitudinalForce;
        }

        return 0f;
    }

    private static bool ShouldSynchronizeTorqueBalancedDrivenWheel(
        WheelRuntimeState wheel,
        float driveTorqueNm,
        float brakeTorqueNm,
        float requestedLongitudinalForce,
        float actualLongitudinalForce)
    {
        if (driveTorqueNm <= 0.1f || brakeTorqueNm > 0.1f)
        {
            return false;
        }

        if (MathF.Abs(requestedLongitudinalForce) <= 0.001f)
        {
            return false;
        }

        return MathF.Abs(actualLongitudinalForce - requestedLongitudinalForce) <=
               MathF.Max(8f, MathF.Abs(requestedLongitudinalForce) * 0.03f);
    }

    private static void SynchronizeDrivenRollingWheelSpeed(
        WheelRuntimeState wheel,
        float driveTorqueNm,
        float wheelLongitudinalVelocity,
        float radius,
        float dt)
    {
        if (driveTorqueNm <= 0.1f ||
            radius <= 0.01f ||
            MathF.Abs(wheelLongitudinalVelocity) < 0.5f)
        {
            return;
        }

        float targetRollingOmega = wheelLongitudinalVelocity / radius;
        if (targetRollingOmega * wheel.AngularVelocityRadiansPerSecond < 0f)
        {
            return;
        }

        float currentSurfaceSpeed = wheel.AngularVelocityRadiansPerSecond * radius;
        float targetSurfaceSpeed = targetRollingOmega * radius;
        if (MathF.Abs(currentSurfaceSpeed) >= MathF.Abs(targetSurfaceSpeed))
        {
            return;
        }

        float catchUp = 1f - MathF.Exp(-MathF.Max(0f, dt) * 48f);
        wheel.AngularVelocityRadiansPerSecond = MathHelper.Lerp(
            wheel.AngularVelocityRadiansPerSecond,
            targetRollingOmega,
            MathHelper.Clamp(catchUp, 0f, 1f));
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

    private void RecoverReleasedHandbrakeWheelSpeed(
        WheelRuntimeState wheel,
        SurfaceSample surface,
        float handbrakeRearTorqueNm,
        float serviceBrakeTorqueNm,
        float wheelLongitudinalVelocity,
        float radius,
        float dt)
    {
        if (IsFrontWheel(wheel.Corner) ||
            handbrakeRearTorqueNm > 0.1f ||
            serviceBrakeTorqueNm > 0.1f ||
            radius <= 0.01f)
        {
            return;
        }

        float targetAngularVelocity = MathF.Abs(wheelLongitudinalVelocity) < 0.12f
            ? 0f
            : wheelLongitudinalVelocity / radius;
        float deltaOmega = targetAngularVelocity - wheel.AngularVelocityRadiansPerSecond;
        if (MathF.Abs(deltaOmega) < 0.05f)
        {
            return;
        }

        float wheelInertia = MathF.Max(0.1f, CalculateEffectiveWheelInertia(wheel));
        float rawRecoveryTorque =
            deltaOmega *
            MathF.Max(0f, surface.HandbrakeWheelSpinRecoveryRate) *
            wheelInertia;
        float maxFrictionTorque =
            MathF.Max(0f, wheel.NormalLoadN) *
            MathF.Max(0.01f, wheel.Tyres.PeakFriction * surface.DynamicFrictionCoefficient) *
            radius;
        float recoveryTorque = MathHelper.Clamp(rawRecoveryTorque, -maxFrictionTorque, maxFrictionTorque);
        wheel.AngularVelocityRadiansPerSecond += recoveryTorque / wheelInertia * MathF.Max(0f, dt);

        float correctedDeltaOmega = targetAngularVelocity - wheel.AngularVelocityRadiansPerSecond;
        if (deltaOmega * correctedDeltaOmega <= 0f || MathF.Abs(correctedDeltaOmega * radius) < 0.12f)
        {
            wheel.AngularVelocityRadiansPerSecond = targetAngularVelocity;
        }
    }

    private static float CalculateRearHandbrakeLockAmount(
        bool isRearWheel,
        float handbrakeInput,
        float slipRatio,
        float wheelLongitudinalVelocity)
    {
        if (!isRearWheel ||
            handbrakeInput <= 0.01f ||
            MathF.Abs(wheelLongitudinalVelocity) < 0.35f)
        {
            return 0f;
        }

        float brakingSlip = MathF.Max(0f, -slipRatio);
        return SmoothStep(0.15f, 0.75f, brakingSlip) * MathHelper.Clamp(handbrakeInput, 0f, 1f);
    }

    private static float CalculateRearHandbrakeSlideIntensity(
        SurfaceSample surface,
        float lockAmount,
        float wheelLongitudinalVelocity,
        float wheelLateralVelocity)
    {
        if (lockAmount <= 0.001f)
        {
            return 0f;
        }

        float slideSpeed = MathF.Sqrt(wheelLongitudinalVelocity * wheelLongitudinalVelocity +
                                      wheelLateralVelocity * wheelLateralVelocity);
        return MathHelper.Clamp(
            lockAmount *
            SmoothStep(2.0f, 15.0f, slideSpeed) *
            surface.HandbrakeScreechFactor,
            0f,
            1f);
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

    private bool IsDrivenWheel(WheelCorner corner)
    {
        bool front = IsFrontWheel(corner);
        return _parameters.DrivetrainLayout switch
        {
            DrivetrainLayout.FF => front,
            DrivetrainLayout.FR => !front,
            DrivetrainLayout.AWD => true,
            _ => front
        };
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
        float averageGripUsage,
        float steerInput,
        float driveThrottle,
        float brake)
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

        float dampingRate = (0.18f + speedT * 0.14f + slipT * gripT * 2.30f) * response;
        StabilityAssistParameters stability = _engineParameters.StabilityAssist;
        if (MathF.Abs(steerInput) >= 0.05f &&
            MathF.Abs(State.YawRateRadiansPerSecond) >= MathHelper.ToRadians(stability.MinimumYawRateDegreesPerSecond))
        {
            float requestedYawDirection = -MathF.Sign(steerInput);
            float currentYawDirection = MathF.Sign(State.YawRateRadiansPerSecond);
            if (requestedYawDirection * currentYawDirection > 0f)
            {
                float committedTurnT = SmoothStep(stability.CommittedTurnInputStart, stability.CommittedTurnInputEnd, MathF.Abs(steerInput));
                float brakeT = SmoothStep(stability.BrakeBlendStart, stability.BrakeBlendEnd, CalculateCommittedTurnBrakeDampingInput(brake, steerInput));
                float coastT = 1f - SmoothStep(0.01f, MathF.Max(0.02f, stability.CommittedTurnCoastThrottleEnd), driveThrottle);
                float dampingReleaseT = MathF.Max(brakeT, coastT) * committedTurnT;
                float dampingMultiplier = MathHelper.Lerp(
                    1f,
                    MathHelper.Clamp(stability.CommittedTurnCoastDampingMultiplier, 0f, 1f),
                    dampingReleaseT);
                dampingRate *= dampingMultiplier;
            }
        }

        return dampingRate;
    }

    private Vector2 ApplyCorneringSpeedRetention(
        Vector2 worldAcceleration,
        float absSteerInput,
        float driveThrottle,
        float brake,
        float handbrake)
    {
        TyreForceTuningParameters tyreForce = _engineParameters.TyreForce;
        float retention = MathHelper.Clamp(tyreForce.CorneringSpeedRetention, 0f, 0.95f);
        if (retention <= 0.001f ||
            !_engineParameters.SteeringAssist.DirectRackInput ||
            driveThrottle > 0.35f ||
            brake > 0.01f ||
            handbrake > 0.01f ||
            State.Velocity.LengthSquared() < 1f)
        {
            return worldAcceleration;
        }

        float steerT = SmoothStep(
            MathHelper.Clamp(tyreForce.CorneringSpeedRetentionSteerStart, 0f, 0.95f),
            MathHelper.Clamp(tyreForce.CorneringSpeedRetentionSteerEnd, tyreForce.CorneringSpeedRetentionSteerStart + 0.001f, 1f),
            absSteerInput);
        if (steerT <= 0.001f)
        {
            return worldAcceleration;
        }

        Vector2 velocityDirection = Vector2.Normalize(State.Velocity);
        float accelerationAlongVelocity = Vector2.Dot(worldAcceleration, velocityDirection);
        if (accelerationAlongVelocity >= 0f)
        {
            return worldAcceleration;
        }

        return worldAcceleration - velocityDirection * accelerationAlongVelocity * retention * steerT;
    }

    private Vector2 CalculateStabilityControlAcceleration(
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

    private float CalculateStabilityControlYawDampingRate(
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
                float coastT = 1f - SmoothStep(0.01f, MathF.Max(0.02f, stability.CommittedTurnCoastThrottleEnd), driveThrottle);
                float dampingReleaseT = committedTurnT * MathF.Max(brakeT, coastT);
                float dampingMultiplier = brakeT >= coastT
                    ? stability.CommittedTurnBrakeDampingMultiplier
                    : stability.CommittedTurnCoastDampingMultiplier;
                dampingRate *= MathHelper.Lerp(1f, MathHelper.Clamp(dampingMultiplier, 0f, 1f), dampingReleaseT);
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
        float scrubCoefficient,
        float maximumScrubDragForce)
    {
        if (scrubCoefficient <= 0f ||
            maximumScrubDragForce <= 0f ||
            MathF.Abs(longitudinalVelocity) < 0.05f)
        {
            return 0f;
        }

        float scrubPower = MathF.Abs(lateralForce * lateralVelocity) * scrubCoefficient;
        float scrubForce = scrubPower / MathF.Max(2f, MathF.Abs(longitudinalVelocity));
        scrubForce = MathF.Min(scrubForce, maximumScrubDragForce);
        return -MathF.Sign(longitudinalVelocity) * scrubForce;
    }

    private static float ScaleSteeringLateralProjectionDrag(
        float lateralProjectionZ,
        float forwardSpeed,
        float projectionDragScale)
    {
        float scale = MathHelper.Clamp(projectionDragScale, 0f, 1f);
        if (MathF.Abs(forwardSpeed) < 0.25f ||
            MathF.Sign(lateralProjectionZ) == MathF.Sign(forwardSpeed))
        {
            return lateralProjectionZ;
        }

        return lateralProjectionZ * scale;
    }

    private static float ApplyLowSpeedPivotRearLateralRelease(
        float rearLateralForce,
        float forwardSpeed,
        float absSteerInput,
        SteeringAssistParameters steeringAssist)
    {
        float speedT = 1f - SmoothStep(
            steeringAssist.LowSpeedPivotSpeedEndMetersPerSecond * 0.45f,
            MathF.Max(0.5f, steeringAssist.LowSpeedPivotSpeedEndMetersPerSecond),
            MathF.Abs(forwardSpeed));
        float steerT = SmoothStep(
            MathHelper.Clamp(steeringAssist.LowSpeedPivotSteerStart, 0f, 0.98f),
            1f,
            MathHelper.Clamp(absSteerInput, 0f, 1f));
        float releaseT = speedT * steerT;
        if (releaseT <= 0.001f)
        {
            return rearLateralForce;
        }

        float multiplier = MathHelper.Clamp(steeringAssist.LowSpeedPivotRearLateralMultiplier, 0.1f, 1f);
        return rearLateralForce * MathHelper.Lerp(1f, multiplier, releaseT);
    }

    private float[] CalculateNormalLoads(float forwardSpeed)
    {
        return CalculateNormalLoads(
            forwardSpeed,
            _loadTransferLongitudinalAcceleration,
            _loadTransferLateralAcceleration,
            0f,
            publishDiagnostics: true);
    }

    private float[] CalculateVisualNormalLoads(float forwardSpeed)
    {
        ArcadeHandlingParameters arcade = _parameters.ArcadeHandling;
        float pseudoLateralAcceleration = _filteredSteerInput *
                                          forwardSpeed *
                                          MathF.Abs(forwardSpeed) *
                                          arcade.PseudoLateralTransferScale;
        float pseudoTransferT = arcade.PseudoLateralTransferBlend * SmoothStep(4f, 18f, MathF.Abs(forwardSpeed));
        _visualLoadTransferLateralAcceleration = _loadTransferLateralAcceleration + pseudoLateralAcceleration * pseudoTransferT;
        return CalculateNormalLoads(
            forwardSpeed,
            _loadTransferLongitudinalAcceleration,
            _visualLoadTransferLateralAcceleration,
            0f,
            publishDiagnostics: false);
    }

    private float[] CalculateNormalLoads(
        float forwardSpeed,
        float longitudinalAcceleration,
        float lateralAcceleration,
        float minimumWheelLoadN,
        bool publishDiagnostics)
    {
        float totalWeight = _parameters.MassKg * Gravity;
        float frontStatic = totalWeight * MathHelper.Clamp(_parameters.FrontWeightDistribution, 0.1f, 0.9f);
        float rearStatic = totalWeight - frontStatic;
        float speedSquared = forwardSpeed * forwardSpeed;

        float frontAeroLoad = -_parameters.FrontLiftFactor * speedSquared;
        float rearAeroLoad = -_parameters.RearLiftFactor * speedSquared;
        float longitudinalTransfer = _parameters.MassKg *
                                     longitudinalAcceleration *
                                     _parameters.CenterOfGravityHeightMeters /
                                     MathF.Max(0.5f, _parameters.WheelbaseMeters);

        float frontAxleLoad = MathF.Max(80f, frontStatic - longitudinalTransfer + frontAeroLoad);
        float rearAxleLoad = MathF.Max(80f, rearStatic + longitudinalTransfer + rearAeroLoad);

        float frontRollStiffness = _parameters.FrontSpringRateNPerM * _parameters.FrontTrackMeters * _parameters.FrontTrackMeters * 0.5f +
                                   _parameters.FrontAntiRollBarRateNmPerRad;
        float rearRollStiffness = _parameters.RearSpringRateNPerM * _parameters.RearTrackMeters * _parameters.RearTrackMeters * 0.5f +
                                  _parameters.RearAntiRollBarRateNmPerRad;
        float totalRollStiffness = frontRollStiffness + rearRollStiffness;
        float frontRollShare = totalRollStiffness > 0.001f
            ? frontRollStiffness / totalRollStiffness
            : 0.5f;

        float totalLateralTransferMoment = _parameters.MassKg * lateralAcceleration * _parameters.CenterOfGravityHeightMeters;
        float frontLateralTransfer = totalLateralTransferMoment * frontRollShare / MathF.Max(0.4f, _parameters.FrontTrackMeters);
        float rearLateralTransfer = totalLateralTransferMoment * (1f - frontRollShare) / MathF.Max(0.4f, _parameters.RearTrackMeters);
        if (publishDiagnostics)
        {
            _longitudinalLoadTransferN = longitudinalTransfer;
            _frontLateralLoadTransferN = frontLateralTransfer;
            _rearLateralLoadTransferN = rearLateralTransfer;
            _frontStaticAxleLoadN = frontStatic;
            _rearStaticAxleLoadN = rearStatic;
            _frontAeroLoadN = frontAeroLoad;
            _rearAeroLoadN = rearAeroLoad;
            _frontRollShare = frontRollShare;
        }

        return
        [
            MathF.Max(minimumWheelLoadN, frontAxleLoad * 0.5f + frontLateralTransfer * 0.5f),
            MathF.Max(minimumWheelLoadN, frontAxleLoad * 0.5f - frontLateralTransfer * 0.5f),
            MathF.Max(minimumWheelLoadN, rearAxleLoad * 0.5f + rearLateralTransfer * 0.5f),
            MathF.Max(minimumWheelLoadN, rearAxleLoad * 0.5f - rearLateralTransfer * 0.5f)
        ];
    }

    private void ApplySurfaceLoadVibration(float[] normalLoads, Vector2 forward, Vector2 right, float speedMetersPerSecond)
    {
        _curbContactWheelCount = 0;
        _surfaceVibrationContactWheelCount = 0;
        for (int i = 0; i < _wheels.Length; i++)
        {
            _wheels[i].CurbLoadMultiplier = 1f;
            _wheels[i].SurfaceLoadMultiplier = 1f;
            _wheels[i].SurfaceBlendWeight = 0f;
        }
        _surfaceRumbleLeft = 0f;
        _surfaceRumbleRight = 0f;

        if (speedMetersPerSecond <= SurfaceLoadVibrationMinimumSpeedMetersPerSecond)
        {
            return;
        }

        Vector3 center = State.Position;
        Vector3 right3 = new(right.X, 0f, right.Y);
        Vector3 forward3 = new(forward.X, 0f, forward.Y);
        for (int i = 0; i < _wheels.Length; i++)
        {
            WheelRuntimeState wheel = _wheels[i];
            Vector3 contactPosition = center + right3 * wheel.LocalX + forward3 * wheel.LocalZ;
            SurfaceSample surface = _surfaceSampler.Sample(contactPosition);
            bool isCurb = surface.Name.Equals("CURB", StringComparison.OrdinalIgnoreCase);
            bool isCurbGrassBlend = surface.Name.Equals("CURB_GRASS", StringComparison.OrdinalIgnoreCase);
            if (isCurb)
            {
                _curbContactWheelCount++;
            }

            bool hasPrimaryVibration = surface.VibrationPrimaryFrequency > 0f && surface.VibrationPrimaryAmplitude > 0f;
            bool hasSecondaryVibration = surface.VibrationSecondaryFrequency > 0f && surface.VibrationSecondaryAmplitude > 0f;
            if (!hasPrimaryVibration && !hasSecondaryVibration)
            {
                continue;
            }

            float phaseOffset = GetSurfaceVibrationPhaseOffset(wheel);
            float timeSpaceTracker = _physicsTimeSeconds * speedMetersPerSecond + phaseOffset;
            float vibration = 0f;
            if (hasPrimaryVibration)
            {
                vibration += MathF.Sin(timeSpaceTracker * surface.VibrationPrimaryFrequency) *
                             surface.VibrationPrimaryAmplitude;
            }

            if (hasSecondaryVibration)
            {
                vibration += MathF.Cos(timeSpaceTracker * surface.VibrationSecondaryFrequency) *
                             surface.VibrationSecondaryAmplitude;
            }

            float multiplier = MathHelper.Clamp(1f - MathF.Abs(vibration), 0f, 1f);
            normalLoads[i] = MathF.Max(50f, normalLoads[i] * multiplier);
            wheel.SurfaceLoadMultiplier = multiplier;
            wheel.CurbLoadMultiplier = isCurb || isCurbGrassBlend ? multiplier : 1f;
            wheel.SurfaceBlendWeight = surface.BlendWeight;
            AccumulateSurfaceRumble(wheel, surface, MathF.Abs(vibration), speedMetersPerSecond);
            _surfaceVibrationContactWheelCount++;
        }
    }

    private void AccumulateSurfaceRumble(
        WheelRuntimeState wheel,
        SurfaceSample surface,
        float vibrationMagnitude,
        float speedMetersPerSecond)
    {
        if (vibrationMagnitude <= 0.001f)
        {
            return;
        }

        float speedFactor = SmoothStep(1.0f, 30.0f, speedMetersPerSecond);
        float axleFactor = IsFrontWheel(wheel.Corner) ? 1f : 0.55f;
        float surfaceBlend = surface.Name.Equals("CURB_GRASS", StringComparison.OrdinalIgnoreCase)
            ? surface.BlendWeight
            : surface.Name.Equals("GRASS", StringComparison.OrdinalIgnoreCase) ||
              surface.Name.Equals("DIRT", StringComparison.OrdinalIgnoreCase)
                ? 1f
                : 0f;
        float curbShare = surface.Name.Equals("CURB", StringComparison.OrdinalIgnoreCase)
            ? 1f
            : surface.Name.Equals("CURB_GRASS", StringComparison.OrdinalIgnoreCase)
                ? 1f - surfaceBlend
                : 0f;

        _surfaceRumbleRight = MathF.Max(
            _surfaceRumbleRight,
            vibrationMagnitude * speedFactor * axleFactor * curbShare * 1.80f);
        _surfaceRumbleLeft = MathF.Max(
            _surfaceRumbleLeft,
            vibrationMagnitude * speedFactor * axleFactor * surfaceBlend * 2.20f);
    }

    private static float GetSurfaceVibrationPhaseOffset(WheelRuntimeState wheel)
    {
        float phase = wheel.LocalX < 0f ? 0f : MathHelper.PiOver2;
        if (wheel.LocalZ < 0f)
        {
            phase += MathHelper.PiOver4;
        }

        return phase;
    }

    private float[] DistributeDriveTorque(float totalDriveTorque, float[] normalLoads, float driveThrottle)
    {
        float[] torques = new float[4];
        Array.Fill(_lastDriveTorquesNm, 0f);
        _ffLsdCornerExitBite = 0f;
        _ffLsdInsideFrontMaxTorqueNm = 0f;
        _ffLsdOutsideFrontMaxTorqueNm = 0f;
        _ffLsdManagedFrontAxleTorqueNm = 0f;
        _ffLsdFrontLeftActualTorqueNm = 0f;
        _ffLsdFrontRightActualTorqueNm = 0f;
        _ffLsdLowGripAnchor = string.Empty;
        _frontDifferentialCornerExitBite = 0f;
        _frontDifferentialTorqueResult = default;
        _rearDifferentialTorqueResult = default;
        _frontDriveTorqueSteerYawMomentNm = 0f;
        if (MathF.Abs(totalDriveTorque) < 0.001f || _parameters.DrivenWheels.Count == 0)
        {
            return torques;
        }

        float frontShare = CalculateFrontTorqueShare();
        float frontAxleTorque = totalDriveTorque * frontShare;
        float rearAxleTorque = totalDriveTorque * (1f - frontShare);

        _frontDifferentialTorqueResult = DistributeDrivenAxleTorque(
            WheelCorner.FrontLeft,
            WheelCorner.FrontRight,
            frontAxleTorque,
            _parameters.FrontDifferential,
            normalLoads,
            driveThrottle,
            isSteeredAxle: true,
            torques);
        _rearDifferentialTorqueResult = DistributeDrivenAxleTorque(
            WheelCorner.RearLeft,
            WheelCorner.RearRight,
            rearAxleTorque,
            _parameters.RearDifferential,
            normalLoads,
            driveThrottle,
            isSteeredAxle: false,
            torques);
        DistributeSingleWheelTorque(WheelCorner.FrontLeft);
        DistributeSingleWheelTorque(WheelCorner.FrontRight);
        DistributeSingleWheelTorque(WheelCorner.RearLeft);
        DistributeSingleWheelTorque(WheelCorner.RearRight);
        for (int i = 0; i < torques.Length; i++)
        {
            _lastDriveTorquesNm[i] = torques[i];
        }

        return torques;

        float CalculateFrontTorqueShare()
        {
            return _parameters.DrivetrainLayout switch
            {
                DrivetrainLayout.FF => 1f,
                DrivetrainLayout.FR => 0f,
                DrivetrainLayout.AWD => MathHelper.Clamp(_parameters.FrontTorqueShare, 0f, 1f),
                _ => _parameters.DrivenWheels.RearLeft || _parameters.DrivenWheels.RearRight ? 0f : 1f
            };
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

    private AxleTorqueResult DistributeDrivenAxleTorque(
        WheelCorner left,
        WheelCorner right,
        float axleTorque,
        DifferentialParameters differential,
        float[] normalLoads,
        float driveThrottle,
        bool isSteeredAxle,
        float[] torques)
    {
        if (!_parameters.DrivenWheels.IsDriven(left) ||
            !_parameters.DrivenWheels.IsDriven(right) ||
            MathF.Abs(axleTorque) <= 0.001f)
        {
            return default;
        }

        WheelRuntimeState leftWheel = GetWheel(left);
        WheelRuntimeState rightWheel = GetWheel(right);
        float leftMaxTorque = CalculateWheelTractiveTorqueCapacity(leftWheel, normalLoads[(int)left]);
        float rightMaxTorque = CalculateWheelTractiveTorqueCapacity(rightWheel, normalLoads[(int)right]);
        bool leftIsAnchor = leftMaxTorque <= rightMaxTorque;
        WheelCorner anchor = leftIsAnchor ? left : right;
        WheelCorner highGrip = leftIsAnchor ? right : left;
        WheelRuntimeState anchorWheel = leftIsAnchor ? leftWheel : rightWheel;
        WheelRuntimeState highGripWheel = leftIsAnchor ? rightWheel : leftWheel;
        float lowGripAnchorMaxTorque = leftIsAnchor ? leftMaxTorque : rightMaxTorque;
        float highGripWheelMaxTorque = leftIsAnchor ? rightMaxTorque : leftMaxTorque;
        float torqueBias = MathF.Max(1f, differential.TorqueBiasRatio);
        float preloadTorque = MathF.Max(0f, differential.PreloadTorqueNm);
        float highGripViaLsdMax = MathF.Min(highGripWheelMaxTorque, (lowGripAnchorMaxTorque + preloadTorque) * torqueBias);
        float managedAxleTorque = lowGripAnchorMaxTorque + highGripViaLsdMax;
        if (managedAxleTorque <= 1f)
        {
            float halfTorque = axleTorque * 0.5f;
            torques[(int)left] = halfTorque;
            torques[(int)right] = halfTorque;
            return new AxleTorqueResult(halfTorque, halfTorque, 0f, 0f, 0f, string.Empty);
        }

        float torqueSign = MathF.Sign(axleTorque);
        float requestedAxleTorque = MathF.Abs(axleTorque);
        float actualAxleTorque = MathF.Min(requestedAxleTorque, managedAxleTorque);
        float torqueScale = MathHelper.Clamp(actualAxleTorque / managedAxleTorque, 0f, 1f);
        float anchorActualTorque = lowGripAnchorMaxTorque * torqueScale * torqueSign;
        float highGripActualTorque = highGripViaLsdMax * torqueScale * torqueSign;

        torques[(int)anchor] = anchorActualTorque;
        torques[(int)highGrip] = highGripActualTorque;
        if (left is WheelCorner.FrontLeft && right is WheelCorner.FrontRight)
        {
            _ffLsdInsideFrontMaxTorqueNm = lowGripAnchorMaxTorque;
            _ffLsdOutsideFrontMaxTorqueNm = highGripViaLsdMax;
            _ffLsdManagedFrontAxleTorqueNm = managedAxleTorque;
            _ffLsdLowGripAnchor = anchor.ToString();
            _ffLsdFrontLeftActualTorqueNm = torques[(int)WheelCorner.FrontLeft];
            _ffLsdFrontRightActualTorqueNm = torques[(int)WheelCorner.FrontRight];
            _frontDifferentialCornerExitBite = isSteeredAxle
                ? CalculateDifferentialCornerExitBite(
                    anchorWheel,
                    highGripWheel,
                    requestedAxleTorque,
                    managedAxleTorque,
                    driveThrottle,
                    differential)
                : 0f;
            _ffLsdCornerExitBite = _frontDifferentialCornerExitBite;
            float leftDriveForce = _ffLsdFrontLeftActualTorqueNm / MathF.Max(0.05f, leftWheel.Tyres.LoadedRadiusMeters);
            float rightDriveForce = _ffLsdFrontRightActualTorqueNm / MathF.Max(0.05f, rightWheel.Tyres.LoadedRadiusMeters);
            _frontDriveTorqueSteerYawMomentNm = (leftDriveForce - rightDriveForce) * _parameters.FrontTrackMeters * 0.5f;
        }

        return new AxleTorqueResult(
            torques[(int)left],
            torques[(int)right],
            managedAxleTorque,
            lowGripAnchorMaxTorque,
            highGripViaLsdMax,
            anchor.ToString());
    }

    private static float CalculateWheelTractiveTorqueCapacity(WheelRuntimeState wheel, float normalLoadN)
    {
        float surfaceMu = wheel.ActiveSurfaceMu > 0f
            ? wheel.ActiveSurfaceMu
            : wheel.StaticSurfaceMu;
        float mu = MathF.Max(0f, wheel.Tyres.PeakFriction * surfaceMu);
        return MathF.Max(0f, normalLoadN * mu * MathF.Max(0.05f, wheel.Tyres.LoadedRadiusMeters));
    }

    private float CalculateDifferentialCornerExitBite(
        WheelRuntimeState insideWheel,
        WheelRuntimeState outsideWheel,
        float requestedAxleTorque,
        float managedAxleTorque,
        float driveThrottle,
        DifferentialParameters differential)
    {
        float throttleT = SmoothStep(0.18f, 0.85f, driveThrottle);
        float rpmT = SmoothStep(4500f, MathF.Max(5000f, _parameters.PowerRedlineRpm), State.Rpm);
        float vtecT = _parameters.VtecEnabled
            ? SmoothStep(_parameters.VtecActivationRpm - 300f, _parameters.VtecActivationRpm + 700f, State.Rpm)
            : 0f;
        float lsdT = MathHelper.Clamp((differential.TorqueBiasRatio - 1f) / 2.2f, 0f, 1f);
        float steerT = SmoothStep(0.10f, 0.70f, MathF.Abs(_filteredSteerInput));
        float torqueDemandT = SmoothStep(managedAxleTorque * 0.35f, managedAxleTorque, requestedAxleTorque);
        float insideDriveSlip = MathF.Max(0f, insideWheel.SlipRatio);
        float outsideDriveSlip = MathF.Max(0f, outsideWheel.SlipRatio);
        float insideSlipT = SmoothStep(0.03f, 0.16f, MathF.Max(insideDriveSlip, insideDriveSlip - outsideDriveSlip));

        return MathHelper.Clamp(
            throttleT *
            MathF.Max(rpmT, vtecT * 0.85f) *
            lsdT *
            steerT *
            MathHelper.Lerp(0.65f, 1.15f, insideSlipT) *
            MathHelper.Lerp(0.70f, 1f, torqueDemandT),
            0f,
            1f);
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

        return MathHelper.Clamp(requestedThrottle, 0f, 1f);
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

        if (brake > 0.01f)
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

        bool corneringBrakeControlActive =
            MathF.Abs(_filteredSteerInput) > _engineParameters.DigitalBrakeAssist.SteeringBlendStart &&
            MathF.Abs(wheelLongitudinalVelocity) > _engineParameters.DigitalBrakeAssist.AbsMinimumSpeedMetersPerSecond;
        if (_digitalBrakeAssistActive || corneringBrakeControlActive)
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

    private SteeringAngles CalculateSteeringAngles(float steerInput, float speedMetersPerSecond, float brake, float throttle)
    {
        float steeringWheelHalfLockRadians = MathHelper.ToRadians(_parameters.SteeringWheelLockDegrees * 0.5f);
        float ratioLimitedRoadAngle = steeringWheelHalfLockRadians / MathF.Max(1f, _parameters.SteeringRatio);
        float mechanicalMaxAngle = MathF.Min(_parameters.MaxSteerAngleRadians, ratioLimitedRoadAngle);
        SteeringAssistParameters steeringAssist = _engineParameters.SteeringAssist;
        _steeringFrontGripReserve = CalculatePreviousFrameFrontGripReserve();
        _steeringCommittedTurnAuthority = SmoothStep(
            steeringAssist.CommittedTurnInputStart,
            steeringAssist.CommittedTurnInputEnd,
            MathF.Abs(steerInput));
        float shapedSteerInput = steeringAssist.DirectRackInput
            ? steerInput
            : ApplyHighSpeedSteeringInputCurve(steerInput, speedMetersPerSecond, throttle, brake);
        float availableRoadAngle = CalculateSpeedMatchedSteeringAngle(mechanicalMaxAngle, speedMetersPerSecond, steerInput);
        // With the current MonoGame camera/world convention, a visual right turn is negative yaw.
        float baseAngle = -shapedSteerInput * availableRoadAngle;
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

    private float CalculateSpeedMatchedSteeringAngle(float mechanicalMaxAngle, float speedMetersPerSecond, float steerInput)
    {
        SteeringAssistParameters steeringAssist = _engineParameters.SteeringAssist;
        _steeringFrontGripReserve = CalculatePreviousFrameFrontGripReserve();
        _steeringCommittedTurnAuthority = SmoothStep(
            steeringAssist.CommittedTurnInputStart,
            steeringAssist.CommittedTurnInputEnd,
            MathF.Abs(steerInput));
        if (steeringAssist.DirectRackInput)
        {
            float speedT = SmoothStep(
                MathF.Max(0f, steeringAssist.SpeedMatchedSlipStartMetersPerSecond),
                MathF.Max(steeringAssist.SpeedMatchedSlipStartMetersPerSecond + 0.1f, steeringAssist.SpeedMatchedSlipEndMetersPerSecond),
                MathF.Abs(speedMetersPerSecond));
            float highSpeedAngle = MathF.Max(
                MathHelper.ToRadians(MathF.Max(0f, steeringAssist.HighSpeedMinimumRoadWheelAngleDegrees)),
                mechanicalMaxAngle * MathHelper.Clamp(steeringAssist.HighSpeedSlipAllowanceMultiplier, 0.05f, 1f));
            float availableAngle = MathHelper.Lerp(
                mechanicalMaxAngle,
                MathHelper.Clamp(highSpeedAngle, 0f, mechanicalMaxAngle),
                speedT);
            _steeringSpeedMatchedMaxAngleRadians = availableAngle;
            return availableAngle;
        }

        _steeringSpeedMatchedMaxAngleRadians = mechanicalMaxAngle;
        return mechanicalMaxAngle;
    }

    private float ApplyHighSpeedSteeringInputCurve(float steerInput, float speedMetersPerSecond, float throttle, float brake)
    {
        if (MathF.Abs(steerInput) <= 0.0001f)
        {
            return 0f;
        }

        SteeringAssistParameters steeringAssist = _engineParameters.SteeringAssist;
        float curveStartSpeed = MathF.Max(6f, steeringAssist.SpeedMatchedSlipStartMetersPerSecond * 0.55f);
        float curveEndSpeed = MathF.Max(curveStartSpeed + 4f, steeringAssist.SpeedMatchedSlipStartMetersPerSecond * 1.15f);
        float speedT = SmoothStep(
            curveStartSpeed,
            curveEndSpeed,
            MathF.Abs(speedMetersPerSecond));
        float exponent = MathHelper.Lerp(
            1f,
            MathHelper.Clamp(steeringAssist.HighSpeedInputCurveExponent, 0.35f, 3f),
            speedT);
        float committedTurnT = SmoothStep(
            steeringAssist.CommittedTurnInputStart,
            steeringAssist.CommittedTurnInputEnd,
            MathF.Abs(steerInput));
        float lowThrottleT = 1f - SmoothStep(0.01f, MathF.Max(0.02f, steeringAssist.DecelAuthorityThrottleEnd), throttle);
        float brakeT = SmoothStep(0.03f, 0.35f, brake);
        float decelTurnT = MathHelper.Clamp(committedTurnT * MathF.Max(lowThrottleT, brakeT) * speedT, 0f, 1f);
        exponent = MathHelper.Lerp(
            exponent,
            MathHelper.Clamp(steeringAssist.DecelInputCurveExponent, 0.35f, MathF.Max(0.35f, exponent)),
            decelTurnT);
        return MathF.Sign(steerInput) * MathF.Pow(MathF.Abs(steerInput), exponent);
    }

    private float CalculatePreviousFrameFrontGripReserve()
    {
        WheelRuntimeState frontLeft = GetWheel(WheelCorner.FrontLeft);
        WheelRuntimeState frontRight = GetWheel(WheelCorner.FrontRight);
        float peakFrontGripUsage = MathF.Max(frontLeft.GripUsage, frontRight.GripUsage);
        if (peakFrontGripUsage <= 0.001f)
        {
            return 1f;
        }

        return MathHelper.Clamp(1f - peakFrontGripUsage, 0f, 1f);
    }

    private void ApplyLowSpeedPivotYawResponse(
        float averageFrontSteerAngleRadians,
        float forwardSpeed,
        float steerInput,
        float dt)
    {
        SteeringAssistParameters steeringAssist = _engineParameters.SteeringAssist;
        float pivotSpeedEnd = MathF.Max(0.5f, steeringAssist.LowSpeedPivotSpeedEndMetersPerSecond);
        float speedT = 1f - SmoothStep(
            2f,
            pivotSpeedEnd * 1.75f,
            MathF.Abs(forwardSpeed));
        float steerT = SmoothStep(
            MathHelper.Clamp(steeringAssist.LowSpeedPivotSteerStart * 0.55f, 0f, 0.98f),
            1f,
            MathF.Abs(steerInput));
        float pivotT = speedT * steerT;
        if (pivotT <= 0.001f || MathF.Abs(averageFrontSteerAngleRadians) <= 0.001f)
        {
            return;
        }

        float targetYawRate = forwardSpeed *
                              MathF.Tan(averageFrontSteerAngleRadians) /
                              MathF.Max(0.8f, _parameters.WheelbaseMeters);
        float yawLimit = MathHelper.ToRadians(MathF.Max(20f, steeringAssist.LowSpeedPivotMaxYawRateDegreesPerSecond));
        targetYawRate = MathHelper.Clamp(targetYawRate, -yawLimit, yawLimit);
        float blend = 1f - MathF.Exp(-MathF.Max(0f, steeringAssist.LowSpeedPivotYawResponse) * pivotT * dt);
        State.YawRateRadiansPerSecond = MathHelper.Lerp(
            State.YawRateRadiansPerSecond,
            targetYawRate,
            MathHelper.Clamp(blend, 0f, 1f));
    }

    private float CalculateSteeringSpeedT(float speedMetersPerSecond)
    {
        float assistRange = MathF.Max(0.1f, _parameters.SteeringReducedLockSpeedMetersPerSecond - _parameters.SteeringFullLockSpeedMetersPerSecond);
        return MathHelper.Clamp((speedMetersPerSecond - _parameters.SteeringFullLockSpeedMetersPerSecond) / assistRange, 0f, 1f);
    }

    private void UpdateSteeringInput(float targetInput, float speedMetersPerSecond, float brake, float throttle, float dt)
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
        float brakeAuthorityT = 0f;
        float decelAuthorityT = 0f;
        float rate = baseRate;
        if (!steeringAssist.DirectRackInput)
        {
            brakeAuthorityT =
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
            float committedTurnT = SmoothStep(
                steeringAssist.CommittedTurnInputStart,
                steeringAssist.CommittedTurnInputEnd,
                MathF.Abs(targetInput));
            float lowThrottleT = 1f - SmoothStep(0.01f, MathF.Max(0.02f, steeringAssist.DecelAuthorityThrottleEnd), throttle);
            decelAuthorityT =
                committedTurnT *
                lowThrottleT *
                SmoothStep(
                    steeringAssist.InputBrakeAuthoritySpeedStartMetersPerSecond,
                    steeringAssist.InputBrakeAuthoritySpeedEndMetersPerSecond,
                    speedMetersPerSecond);
            highSpeedMultiplier = MathHelper.Lerp(
                highSpeedMultiplier,
                MathF.Max(highSpeedMultiplier, steeringAssist.BrakingInputMultiplierFloor),
                decelAuthorityT);
            rate *= MathHelper.Lerp(
                1f,
                MathHelper.Clamp(highSpeedMultiplier, 0.05f, 1.15f),
                CalculateSteeringSpeedT(speedMetersPerSecond));
            rate *= MathHelper.Lerp(1f, steeringAssist.BrakingInputRateBoost, brakeAuthorityT);
            rate *= MathHelper.Lerp(1f, steeringAssist.DecelInputRateBoost, decelAuthorityT);
        }
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
                : CalculateDrivenTransmissionRpm(gearRatio, forwardSpeed);
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

        if (State.Gear != 0 &&
            !State.ClutchIsLocked &&
            throttle > 0.01f &&
            MathF.Abs(forwardSpeed) < _parameters.ClutchLowSpeedThresholdMetersPerSecond * 1.25f)
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
            EnginePowerUnitPhase.Launch => MathHelper.Clamp(State.ClutchEngagement, 0f, 1f),
            EnginePowerUnitPhase.Shifting => _shiftDurationSeconds > 0f
                ? 1f - MathHelper.Clamp(_shiftTimerSeconds / _shiftDurationSeconds, 0f, 1f)
                : 1f,
            _ => 1f
        };
    }

    private float CalculateEnginePowerOverrun(float throttle, float rpm, float forwardSpeed)
    {
        return (1f - SmoothStep(0.05f, 0.25f, throttle)) *
               SmoothStep(2600f, MathF.Max(3200f, _parameters.PowerRedlineRpm), rpm) *
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

        float ceiling = MathF.Max(_parameters.LimiterHardCutRpm, _parameters.IdleRpm + 1000f);
        float targetRpm = MathHelper.Clamp(enginePower.CrankRpm, 650f, ceiling);
        if (dt > 0f &&
            _enginePowerShiftHandoffSmoothSeconds > 0f &&
            !State.MechanicalOverRevActive)
        {
            float maxDrop = 1600f * MathHelper.Clamp(dt, 0f, 0.05f);
            float maxRise = 12000f * MathHelper.Clamp(dt, 0f, 0.05f);
            targetRpm = State.Rpm + MathHelper.Clamp(targetRpm - State.Rpm, -maxDrop, maxRise);
        }

        State.Rpm = targetRpm;
        State.ClutchSlipRpm = State.Rpm - MathF.Max(0f, enginePower.TransmissionRpm);
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

    private float CalculateContinuousClutchDriveTorque(VehicleInput input, float throttle, float forwardSpeed, float dt)
    {
        EnsureEngineOmegaInitialized();

        if (_shiftTimerSeconds > 0f)
        {
            UpdateRpm(input, forwardSpeed, dt);
            SyncEngineOmegaFromRpm();
            AdvanceEnginePowerDuringShift(throttle, forwardSpeed, dt);
            PublishClutchState(0f, 0f, 0f, false, 0f);
            return 0f;
        }

        if (IsMechanicalOverRevForced(forwardSpeed) || throttle <= 0.01f)
        {
            float brakingTorque = CalculateEngineBrakingTorque(forwardSpeed, dt);
            float brakingGearRatio = GetCurrentGearRatio();
            float brakingGearboxInputRpm = brakingGearRatio > 0.0001f ? CalculateDrivenTransmissionRpm(brakingGearRatio, forwardSpeed) : 0f;
            bool clutchLocked = brakingGearRatio > 0f && MathF.Abs(forwardSpeed) > 0.8f;
            if (clutchLocked)
            {
                State.Rpm = MathHelper.Clamp(brakingGearboxInputRpm, _parameters.IdleRpm, GetCurrentRpmCeiling());
                SyncEngineOmegaFromRpm();
            }
            else
            {
                UpdateRpm(input, forwardSpeed, dt);
                SyncEngineOmegaFromRpm();
            }

            PublishClutchState(brakingGearboxInputRpm, State.Rpm - brakingGearboxInputRpm, MathF.Abs(brakingTorque), clutchLocked, clutchLocked ? 1f : 0f);
            return brakingTorque;
        }

        if (State.Gear < 0 && forwardSpeed < -_engineParameters.VehicleSafety.MaximumReverseSpeedMetersPerSecond)
        {
            PublishClutchState(0f, 0f, 0f, false, 0f);
            return 0f;
        }

        if (State.Gear > 0 && forwardSpeed > _engineParameters.VehicleSafety.MaximumForwardSpeedMetersPerSecond)
        {
            PublishClutchState(0f, 0f, 0f, false, 0f);
            return 0f;
        }

        float gearRatio = GetCurrentGearRatio();
        if (gearRatio <= 0.0001f)
        {
            IntegrateFreeRevEngineOmega(throttle, forwardSpeed, dt, clutchLoadTorqueNm: 0f);
            PublishClutchState(0f, 0f, 0f, false, 0f);
            return 0f;
        }

        float gearboxInputRpm = CalculateDrivenTransmissionRpm(gearRatio, forwardSpeed);
        if (MathF.Abs(forwardSpeed) > 12f)
        {
            gearboxInputRpm = CalculateRoadSpeedRpmForGear(State.Gear, forwardSpeed, GetCurrentRpmCeiling());
        }
        float gearboxInputOmega = RpmToOmega(gearboxInputRpm);
        float slipOmega = State.EngineOmegaRadiansPerSecond - gearboxInputOmega;
        float speed = MathF.Abs(forwardSpeed);
        float lowSpeedThreshold = MathF.Max(0.5f, _parameters.ClutchLowSpeedThresholdMetersPerSecond);
        float speedFactor = MathHelper.Clamp(speed / lowSpeedThreshold, 0f, 1f);
        float pedal = MathHelper.Clamp(throttle, 0f, 1f);
        float bitePoint = MathHelper.Clamp(_parameters.ClutchEngagementPoint, 0.05f, 0.95f);
        float biteStart = MathHelper.Clamp(
            bitePoint * MathHelper.Clamp(_parameters.ClutchBiteInputStartMultiplier, 0.05f, 0.95f),
            0.01f,
            bitePoint - 0.001f);
        float pedalBite = SmoothStep(biteStart, bitePoint, pedal);
        float launchAssist = MathF.Pow(pedal, MathHelper.Clamp(_parameters.ClutchLaunchAssistExponent, 0.25f, 1.5f)) *
                             MathHelper.Clamp(_parameters.ClutchLowSpeedAssistStrength, 0f, 1f);
        float clampIntent = MathF.Max(speedFactor, MathF.Max(pedalBite, launchAssist));
        float sharpness = MathF.Max(0.25f, _parameters.ClutchEngagementSharpness);
        float clutchEngagement = 1f - MathF.Pow(1f - MathHelper.Clamp(clampIntent, 0f, 1f), sharpness);
        float lowSpeedLaunchT = 1f - SmoothStep(lowSpeedThreshold * 0.45f, lowSpeedThreshold, speed);
        float launchThrottle = MathF.Pow(pedal, MathHelper.Clamp(_parameters.ClutchLowSpeedThrottleGamma, 0.35f, 1.25f));
        float engineThrottle = MathHelper.Lerp(pedal, MathHelper.Clamp(launchThrottle, pedal, 1f), lowSpeedLaunchT * (1f - speedFactor * 0.35f));
        float pullAwayIntent = SmoothStep(0.015f, 0.35f, pedal) * (1f - SmoothStep(lowSpeedThreshold * 0.55f, lowSpeedThreshold, speed));
        float lowSpeedThrottleAssist = MathHelper.Clamp(_parameters.ClutchLowSpeedThrottleAssist, 0f, 0.85f);
        engineThrottle = MathHelper.Clamp(
            engineThrottle + (1f - engineThrottle) * lowSpeedThrottleAssist * pullAwayIntent,
            pedal,
            1f);
        float activeClutchCapacityNm = MathF.Max(0f, _parameters.ClutchTorqueCapacityNm) * clutchEngagement;
        float absoluteSlipOmega = MathF.Abs(slipOmega);
        float lockSlip = MathF.Max(0.2f, _parameters.ClutchLockSlipRadiansPerSecond);
        float unlockSlip = MathF.Max(lockSlip + 0.1f, _parameters.ClutchUnlockSlipRadiansPerSecond);
        float rollingLockSpeed = MathF.Max(0.2f, _parameters.ClutchRollingLockSpeedMetersPerSecond);
        float rollingLockSlip = MathF.Max(lockSlip, _parameters.ClutchRollingLockSlipRadiansPerSecond);

        if (State.ClutchIsLocked)
        {
            if (absoluteSlipOmega > unlockSlip || speed < 0.65f)
            {
                State.ClutchIsLocked = false;
            }
        }
        else if ((absoluteSlipOmega < lockSlip && speed > 1.8f) ||
                 (pedal > 0.14f && speed > rollingLockSpeed && absoluteSlipOmega < rollingLockSlip))
        {
            State.ClutchIsLocked = true;
        }

        float direction = State.Gear < 0 ? -1f : 1f;
        float rpm = MathHelper.Clamp(OmegaToRpm(State.EngineOmegaRadiansPerSecond), _parameters.IdleRpm, GetCurrentRpmCeiling());
        EnginePowerUnitState enginePower = AdvanceEnginePower(
            rpm,
            engineThrottle,
            forwardSpeed,
            dt,
            clutchEngagement,
            transmissionRpm: gearboxInputRpm);
        float crankTorqueNm = enginePower.Enabled
            ? enginePower.DriveTorqueNm
            : _parameters.TorqueAtRpm(rpm) * engineThrottle * State.LimiterTorqueMultiplier;
        if (!State.RevLimiterActive && pullAwayIntent > 0f)
        {
            float idleAssistWindowRpm = MathHelper.Clamp(_parameters.IdleRpm + 1650f, _parameters.IdleRpm + 300f, _parameters.PowerRedlineRpm);
            float nearIdleT = 1f - SmoothStep(_parameters.IdleRpm + 80f, idleAssistWindowRpm, rpm);
            crankTorqueNm += MathF.Max(0f, _parameters.ClutchLowSpeedTorqueAssistNm) * pullAwayIntent * nearIdleT;
        }
        float shiftKickTorqueMultiplier = 1f + CalculateShiftKickEnvelope() * _shiftKickSeverity * 0.16f;
        crankTorqueNm *= shiftKickTorqueMultiplier;

        float clutchTorqueNm;
        float totalDriveTorque;
        if (State.ClutchIsLocked)
        {
            State.EngineOmegaRadiansPerSecond = gearboxInputOmega;
            State.Rpm = MathHelper.Clamp(gearboxInputRpm, _parameters.IdleRpm, GetCurrentRpmCeiling());
            clutchTorqueNm = MathF.Min(MathF.Abs(crankTorqueNm), activeClutchCapacityNm);
            totalDriveTorque = direction * crankTorqueNm * gearRatio * _parameters.FinalDriveRatio * _parameters.DrivetrainEfficiency;
        }
        else
        {
            float damping = MathF.Max(0.05f, _parameters.ClutchSlipDamping);
            float slipCapacityT = SmoothStep(lockSlip * 0.12f, lockSlip, absoluteSlipOmega * damping);
            clutchTorqueNm = activeClutchCapacityNm * slipCapacityT;
            float slipDirection = absoluteSlipOmega > 0.0001f
                ? MathF.Sign(slipOmega)
                : 1f;
            float idleOmegaTarget = RpmToOmega(_parameters.IdleRpm);
            float idleControlTorqueNm = 0f;
            if (State.EngineOmegaRadiansPerSecond < idleOmegaTarget)
            {
                float idleError = idleOmegaTarget - State.EngineOmegaRadiansPerSecond;
                idleControlTorqueNm = MathHelper.Clamp(
                    idleError * MathF.Max(0f, _parameters.IdleControlSensitivityNmPerRadPerSecond),
                    0f,
                    _parameters.ClutchTorqueCapacityNm * 0.55f);
            }

            float frictionTorqueNm = CalculateEngineInternalDragTorque(rpm, throttle, 0.82f);
            float netFlywheelTorqueNm = crankTorqueNm + idleControlTorqueNm - frictionTorqueNm - clutchTorqueNm * slipDirection;
            float engineOmegaDelta = netFlywheelTorqueNm / MathF.Max(0.05f, _parameters.EngineRotationalInertiaKgM2) * dt;
            State.EngineOmegaRadiansPerSecond += engineOmegaDelta;
            State.EngineOmegaRadiansPerSecond = MathHelper.Clamp(
                State.EngineOmegaRadiansPerSecond,
                idleOmegaTarget,
                RpmToOmega(_parameters.LimiterHardCutRpm));
            State.Rpm = OmegaToRpm(State.EngineOmegaRadiansPerSecond);
            totalDriveTorque = direction * clutchTorqueNm * gearRatio * _parameters.FinalDriveRatio * _parameters.DrivetrainEfficiency;
        }

        State.PowertrainShockIntensity = MathHelper.Clamp(
            MathF.Max(State.ShiftKickIntensity, MathHelper.Clamp(clutchTorqueNm / MathF.Max(1f, _parameters.ClutchTorqueCapacityNm), 0f, 1f) * SmoothStep(0.2f, 1f, MathF.Abs(State.ClutchSlipRpm) / 1800f) * 0.18f),
            0f,
            1f);
        PublishClutchState(gearboxInputRpm, State.Rpm - gearboxInputRpm, clutchTorqueNm, State.ClutchIsLocked, clutchEngagement);
        UpdateMechanicalOverRevState(MathF.Max(State.Rpm, CalculateRoadSpeedRpmForGear(State.Gear, forwardSpeed, GetMechanicalOverRevCeiling())));
        return totalDriveTorque;
    }

    private void EnsureEngineOmegaInitialized()
    {
        if (State.EngineOmegaRadiansPerSecond <= 0.0001f)
        {
            SyncEngineOmegaFromRpm();
        }
    }

    private void SyncEngineOmegaFromRpm()
    {
        State.EngineOmegaRadiansPerSecond = RpmToOmega(MathF.Max(_parameters.IdleRpm, State.Rpm));
    }

    private void PublishClutchState(float gearboxInputRpm, float clutchSlipRpm, float clutchTorqueNm, bool locked, float engagement)
    {
        State.GearboxInputOmegaRadiansPerSecond = RpmToOmega(MathF.Max(0f, gearboxInputRpm));
        State.ClutchSlipDeltaRadiansPerSecond = RpmToOmega(clutchSlipRpm);
        State.ClutchSlipRpm = clutchSlipRpm;
        State.ActiveClutchTorqueNm = MathF.Max(0f, clutchTorqueNm);
        State.ClutchIsLocked = locked;
        State.ClutchEngagement = MathHelper.Clamp(engagement, 0f, 1f);
    }

    private void IntegrateFreeRevEngineOmega(float throttle, float forwardSpeed, float dt, float clutchLoadTorqueNm)
    {
        EnsureEngineOmegaInitialized();

        float clampedDt = MathHelper.Clamp(dt, 0f, 1f / 20f);
        if (clampedDt <= 0f)
        {
            return;
        }

        float currentRpm = MathHelper.Clamp(
            OmegaToRpm(State.EngineOmegaRadiansPerSecond),
            _parameters.IdleRpm,
            GetCurrentRpmCeiling());
        throttle = MathHelper.Clamp(throttle, 0f, 1f);

        EnginePowerUnitState enginePower = AdvanceEnginePower(
            currentRpm,
            throttle,
            forwardSpeed,
            clampedDt,
            clutchEngagement: 0f,
            forceNeutral: true);

        float crankTorqueNm = enginePower.Enabled
            ? enginePower.DriveTorqueNm
            : _parameters.TorqueAtRpm(currentRpm) * throttle * State.LimiterTorqueMultiplier;
        if (State.RevLimiterActive)
        {
            crankTorqueNm = 0f;
        }

        float idleOmegaTarget = RpmToOmega(_parameters.IdleRpm);
        float idleControlTorqueNm = 0f;
        if (State.EngineOmegaRadiansPerSecond < idleOmegaTarget || (throttle <= 0.02f && currentRpm < _parameters.IdleRpm + 120f))
        {
            float idleError = idleOmegaTarget - State.EngineOmegaRadiansPerSecond;
            idleControlTorqueNm = MathHelper.Clamp(
                idleError * MathF.Max(0f, _parameters.IdleControlSensitivityNmPerRadPerSecond),
                0f,
                MathF.Max(20f, _parameters.ClutchTorqueCapacityNm * 0.40f));
        }

        float frictionTorqueNm = CalculateEngineInternalDragTorque(currentRpm, throttle, 0.92f);
        float netTorqueNm = crankTorqueNm + idleControlTorqueNm - frictionTorqueNm - MathF.Max(0f, clutchLoadTorqueNm);
        float inertia = MathF.Max(0.05f, _parameters.EngineRotationalInertiaKgM2);
        float rpmDelta = netTorqueNm / inertia * (60f / MathF.Tau) * clampedDt;
        float maxRise = MathF.Max(500f, _parameters.MaxFreeRevRiseRpmPerSecond) * clampedDt;
        float maxFall = MathF.Max(500f, _parameters.MaxFreeRevFallRpmPerSecond) * clampedDt;
        rpmDelta = MathHelper.Clamp(rpmDelta, -maxFall, maxRise);

        float nextRpm = MathHelper.Clamp(
            currentRpm + rpmDelta,
            _parameters.IdleRpm,
            _parameters.LimiterHardCutRpm);
        State.Rpm = nextRpm;
        State.EngineOmegaRadiansPerSecond = RpmToOmega(nextRpm);
        State.ClutchSlipRpm = 0f;
        UpdateMechanicalOverRevState(nextRpm);
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

    private float CalculateDrivenTransmissionRpm(float gearRatio, float forwardSpeed)
    {
        float drivenWheelSpeed = 0f;
        int drivenCount = 0;
        float rpmReferenceSpeed = CalculateRoadSpeedRpmReferenceSpeed(State.Gear, forwardSpeed);

        foreach (WheelRuntimeState wheel in _wheels)
        {
            if (!_parameters.DrivenWheels.IsDriven(wheel.Corner))
            {
                continue;
            }

            float rollingOmega = rpmReferenceSpeed / MathF.Max(0.1f, _parameters.WheelRadiusMeters);
            float measuredOmega = MathF.Abs(wheel.AngularVelocityRadiansPerSecond);
            if (MathF.Abs(forwardSpeed) > 12f)
            {
                float speedReference = MathF.Max(
                    _engineParameters.VehicleSafety.MinimumSlipSpeedMetersPerSecond,
                    rpmReferenceSpeed);
                float allowedSlipOmega =
                    MathF.Max(0.01f, wheel.Tyres.LongitudinalPeakSlipRatio) *
                    1.35f *
                    speedReference /
                    MathF.Max(0.1f, _parameters.WheelRadiusMeters);
                float lowerOmega = MathF.Max(0f, rollingOmega - allowedSlipOmega);
                float rpmScrubIsolationT = SmoothStep(
                    MathF.Max(0f, _engineParameters.TyreForce.ScrubRpmIsolationSlipStart),
                    MathF.Max(
                        _engineParameters.TyreForce.ScrubRpmIsolationSlipStart + 0.01f,
                        _engineParameters.TyreForce.ScrubRpmIsolationSlipEnd),
                    MathF.Abs(wheel.RelaxedLateralSlip));
                if (rpmScrubIsolationT > 0f)
                {
                    float maximumSpeedDrop = MathF.Max(0f, _engineParameters.TyreForce.ScrubRpmIsolationMaximumSpeedDropMetersPerSecond);
                    float isolatedLowerOmega =
                        MathF.Max(0f, rpmReferenceSpeed - maximumSpeedDrop) /
                        MathF.Max(0.1f, _parameters.WheelRadiusMeters);
                    lowerOmega = MathHelper.Lerp(
                        lowerOmega,
                        MathF.Max(lowerOmega, isolatedLowerOmega),
                        rpmScrubIsolationT);
                    _rpmScrubIsolationIntensity = MathF.Max(_rpmScrubIsolationIntensity, rpmScrubIsolationT);
                }

                measuredOmega = MathHelper.Clamp(
                    measuredOmega,
                    lowerOmega,
                    rollingOmega + allowedSlipOmega);
            }

            drivenWheelSpeed += measuredOmega;
            drivenCount++;
        }

        if (drivenCount == 0)
        {
            drivenWheelSpeed = rpmReferenceSpeed / MathF.Max(0.1f, _parameters.WheelRadiusMeters);
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

        float roadCoupledRpm = CalculateRoadSpeedRpmForGear(State.Gear, forwardSpeed, GetMechanicalOverRevCeiling());
        bool downshiftOverRevArmed =
            _pendingDownshiftOverRevSeverity > 0f ||
            _downshiftOverRevBrakeSeconds > 0f ||
            State.MechanicalOverRevActive;
        float coupledRpm = downshiftOverRevArmed
            ? MathF.Max(State.Rpm, roadCoupledRpm)
            : roadCoupledRpm;
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
        return coupledRpm > _parameters.LimiterHardCutRpm + 25f || _downshiftOverRevBrakeSeconds > 0f;
    }

    private float CalculateAeroDrag(float forwardSpeed)
    {
        if (MathF.Abs(forwardSpeed) < 0.05f)
        {
            return 0f;
        }

        return -MathF.Sign(forwardSpeed) * _parameters.AeroDragFactor * forwardSpeed * forwardSpeed;
    }

    private void AddTrackGravityForces(Vector2 forward, Vector2 right, ref float totalForceZ, ref float totalForceX)
    {
        Vector2 position = new(State.Position.X, State.Position.Z);
        const float sampleDistance = 3.0f;
        float forwardSlope = SampleSlope(position, forward, sampleDistance);
        float rightSlope = SampleSlope(position, right, sampleDistance);
        float trackPitchRadians = MathF.Atan(forwardSlope);
        float trackRollRadians = MathF.Atan(rightSlope);
        float weight = _parameters.MassKg * Gravity;
        float longitudinalGravityForce = -weight * MathF.Sin(trackPitchRadians);
        float lateralGravityForce = -weight * MathF.Sin(trackRollRadians);

        totalForceZ += longitudinalGravityForce;
        totalForceX += lateralGravityForce;
        State.TrackPitchRadians = trackPitchRadians;
        State.TrackRollRadians = trackRollRadians;
        State.TrackLongitudinalGravityForceN = longitudinalGravityForce;
        State.TrackLateralGravityForceN = lateralGravityForce;
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
            if (MathF.Abs(forwardSpeed) < 1.25f && State.Gear > 1 && input.Throttle > 0.08f)
            {
                RequestShift(1, _parameters.ManualShiftTimeSeconds, forwardSpeed, input.Throttle);
            }
            else if (MathF.Abs(forwardSpeed) < 0.35f && State.Gear > 1 && input.Throttle <= 0.08f)
            {
                RequestShift(1, _parameters.ManualShiftTimeSeconds, forwardSpeed, input.Throttle);
            }
            else if (input.ShiftUpRequested && State.Gear < _parameters.ForwardGearRatios.Length)
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
        return predictedRpm > _parameters.LimiterHardCutRpm + _parameters.DownshiftOverRevToleranceRpm;
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

        float rpmReferenceSpeed = CalculateRoadSpeedRpmReferenceSpeed(gear, forwardSpeed);
        float wheelRpm = rpmReferenceSpeed /
                         MathF.Max(0.05f, _parameters.WheelRadiusMeters) /
                         MathF.Tau *
                         60f;
        return wheelRpm * ratio * _parameters.FinalDriveRatio;
    }

    private float CalculateRoadSpeedRpmReferenceSpeed(int gear, float forwardSpeed)
    {
        float forwardMagnitude = MathF.Abs(forwardSpeed);
        if (gear <= 0)
        {
            return forwardMagnitude;
        }

        return MathF.Max(forwardMagnitude, State.SpeedMetersPerSecond);
    }

    private static float RpmToOmega(float rpm)
    {
        return rpm * (MathF.Tau / 60f);
    }

    private static float OmegaToRpm(float omegaRadiansPerSecond)
    {
        return omegaRadiansPerSecond * (60f / MathF.Tau);
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
        bool mechanicalLimiterContact = downshiftOverRevArmed && roadSpeedRpm > _parameters.LimiterHardCutRpm;
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
            float drivenTransmissionRpm = MathHelper.Clamp(CalculateDrivenTransmissionRpm(ratio, forwardSpeed), _parameters.IdleRpm, limiterCeiling);
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
            : CalculateDrivenTransmissionRpm(gearRatio, forwardSpeed);
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
            SyncEngineOmegaFromRpm();
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
        rpmDelta = MathHelper.Clamp(
            rpmDelta,
            -MathF.Max(500f, _parameters.MaxFreeRevFallRpmPerSecond) * MathHelper.Clamp(dt, 0f, 1f / 20f),
            MathF.Max(500f, _parameters.MaxFreeRevRiseRpmPerSecond) * MathHelper.Clamp(dt, 0f, 1f / 20f));
        float maximumRpm = _parameters.LimiterHardCutRpm;
        float newRpm = MathHelper.Clamp(rpm + rpmDelta, 650f, maximumRpm);

        if (throttle <= 0.001f && newRpm < _parameters.IdleRpm)
        {
            newRpm = MathHelper.Lerp(newRpm, _parameters.IdleRpm, MathHelper.Clamp(1f - MathF.Exp(-8f * dt), 0f, 1f));
        }

        State.ClutchSlipRpm = newRpm - State.Rpm;
        State.Rpm = newRpm;
        SyncEngineOmegaFromRpm();
        UpdateMechanicalOverRevState(newRpm);
    }

    private float CalculateEngineInternalDragTorque(float rpm, float throttle, float closedThrottleMultiplier)
    {
        EnginePowerUnitState enginePower = _enginePowerUnit.State;
        if (enginePower.Enabled)
        {
            float simRpmT = SmoothStep(_parameters.IdleRpm, _parameters.LimiterHardCutRpm, rpm);
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

        float rpmT = SmoothStep(_parameters.IdleRpm, _parameters.LimiterHardCutRpm, rpm);
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

    private void UpdateRevLimiter(float throttle, float forwardSpeed, float dt)
    {
        float absoluteForwardSpeed = MathF.Abs(forwardSpeed);
        float bounceRpm = MathF.Max(80f, _parameters.RevLimiterBounceRpm);
        bool stoppedBelowLimiter =
            absoluteForwardSpeed <= 0.35f &&
            State.Rpm <= _parameters.LimiterHardCutRpm - bounceRpm;
        if (stoppedBelowLimiter)
        {
            ClearRevLimiterPresentationState();
            return;
        }

        float roadCoupledRpm = State.Gear > 0
            ? CalculateRoadSpeedRpmForGearUnclamped(State.Gear, forwardSpeed)
            : _parameters.IdleRpm;
        bool roadSpeedLimiterContact =
            throttle > 0.05f &&
            State.Gear > 0 &&
            absoluteForwardSpeed > 0.45f &&
            roadCoupledRpm >= _parameters.LimiterHardCutRpm - 2f;
        bool mechanicalLimiterContact =
            absoluteForwardSpeed > 0.45f &&
            roadCoupledRpm > _parameters.LimiterHardCutRpm + 25f;
        if (throttle <= 0.05f)
        {
            _revLimiterCutting = false;
        }
        else if (roadSpeedLimiterContact || mechanicalLimiterContact)
        {
            _revLimiterCutting = true;
        }
        else if (State.Rpm <= _parameters.RevLimiterResumeRpm ||
                 State.Rpm < _parameters.LimiterHardCutRpm - 80f)
        {
            _revLimiterCutting = false;
        }
        else if (State.Rpm >= _parameters.LimiterHardCutRpm - 0.5f)
        {
            _revLimiterCutting = true;
        }

        State.RevLimiterActive = _revLimiterCutting ||
                                 roadSpeedLimiterContact ||
                                 mechanicalLimiterContact;
        State.LimiterTorqueMultiplier = State.RevLimiterActive
            ? 0f
            : 1f;
        UpdateRevLimiterBounceIntensity(throttle, absoluteForwardSpeed, dt);
    }

    private void UpdateRevLimiterBounceIntensity(float throttle, float absoluteForwardSpeed, float dt)
    {
        float bounceRpm = MathF.Max(80f, _parameters.RevLimiterBounceRpm);
        bool throttleLimiterRegion = throttle > 0.05f &&
                                     State.Rpm >= _parameters.LimiterHardCutRpm - bounceRpm * 1.45f &&
                                     State.Rpm >= _parameters.RevLimiterResumeRpm;
        float movingStressGate = SmoothStep(0.35f, 1.25f, absoluteForwardSpeed);
        bool mechanicalLimiterRegion =
            State.RevLimiterActive &&
            State.MechanicalOverRevActive &&
            State.Rpm >= _parameters.LimiterHardCutRpm - bounceRpm * 1.45f &&
            State.Rpm >= _parameters.RevLimiterResumeRpm;
        float mechanicalLimiterStress = mechanicalLimiterRegion
            ? CalculateMechanicalLimiterContactIntensity(_parameters.LimiterHardCutRpm + State.MechanicalOverRevRpm) * movingStressGate
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
                _parameters.LimiterHardCutRpm - bounceRpm * 1.45f,
                _parameters.LimiterHardCutRpm - bounceRpm * 0.08f,
                State.Rpm)
            : 0f;
        float proximity = MathF.Max(throttleProximity, mechanicalLimiterStress);
        _revLimiterChatterPhaseSeconds = RevLimiterPresentationRules.AdvanceBouncePhase(
            _revLimiterChatterPhaseSeconds,
            _parameters.LimiterHardCutRpm,
            dt);
        float cycle = _revLimiterChatterPhaseSeconds;
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

    private void ClearRevLimiterPresentationState()
    {
        _revLimiterCutting = false;
        _revLimiterChatterPhaseSeconds = 0f;
        State.RevLimiterActive = false;
        State.LimiterTorqueMultiplier = 1f;
        State.RevLimiterBounceIntensity = 0f;
        State.RevLimiterBouncePhase = 0f;
        State.MechanicalOverRevActive = false;
        State.MechanicalOverRevRpm = 0f;
        State.MechanicalOverRevSeverity = 0f;
        State.PowertrainShockIntensity = MathF.Max(0f, State.ShiftKickIntensity);
    }

    private void ClearPendingDownshiftOverRevState()
    {
        _pendingDownshiftOverRevRpm = 0f;
        _pendingDownshiftOverRevSeverity = 0f;
        _downshiftOverRevBrakeSeconds = 0f;
        _downshiftOverRevBrakeDurationSeconds = 0f;
        _downshiftOverRevBrakeSeverity = 0f;
    }

    private void FinalizeLimiterAndOverRevRecovery(float forwardSpeed)
    {
        bool hasRecoverableLimiterState =
            _revLimiterCutting ||
            State.RevLimiterActive ||
            State.RevLimiterBounceIntensity > 0.001f ||
            State.MechanicalOverRevActive ||
            State.MechanicalOverRevRpm > 0.1f ||
            State.MechanicalOverRevSeverity > 0.001f ||
            _pendingDownshiftOverRevSeverity > 0.001f ||
            _downshiftOverRevBrakeSeconds > 0.001f;
        if (!hasRecoverableLimiterState)
        {
            return;
        }

        float absoluteForwardSpeed = MathF.Abs(forwardSpeed);
        float bounceRpm = MathF.Max(80f, _parameters.RevLimiterBounceRpm);
        float roadCoupledRpm = State.Gear > 0
            ? CalculateRoadSpeedRpmForGearUnclamped(State.Gear, forwardSpeed)
            : _parameters.IdleRpm;
        bool roadStillForcesOverRev =
            State.Gear > 0 &&
            absoluteForwardSpeed > 0.45f &&
            roadCoupledRpm > _parameters.LimiterHardCutRpm + 25f;

        if (roadStillForcesOverRev)
        {
            return;
        }

        bool stationary = absoluteForwardSpeed <= 0.35f;
        bool drivetrainSettled = State.Gear <= 0 || absoluteForwardSpeed < 0.15f;
        bool rpmRecovered =
            State.Rpm <= _parameters.RevLimiterResumeRpm ||
            State.Rpm <= _parameters.LimiterHardCutRpm - bounceRpm;

        if (!stationary && !drivetrainSettled && !rpmRecovered)
        {
            return;
        }

        ClearRevLimiterPresentationState();
        ClearPendingDownshiftOverRevState();
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
        return _parameters.LimiterHardCutRpm;
    }

    private float GetShiftRpmCeiling()
    {
        return _parameters.LimiterHardCutRpm;
    }

    private float GetMechanicalOverRevCeiling()
    {
        float configuredLimit = _parameters.DownshiftMechanicalOverRevLimitRpm;
        float fallbackLimit = _parameters.LimiterHardCutRpm + MathF.Max(900f, _parameters.LimiterHardCutRpm * 0.22f);
        float minimumLimit = _parameters.LimiterHardCutRpm + MathF.Max(300f, _parameters.DownshiftOverRevToleranceRpm);
        return MathF.Max(minimumLimit, configuredLimit > 0f ? configuredLimit : fallbackLimit);
    }

    private float CalculateMechanicalOverRevSeverity(float coupledRpm)
    {
        float startRpm = _parameters.LimiterHardCutRpm + MathF.Max(0f, _parameters.DownshiftOverRevToleranceRpm);
        float limitRpm = GetMechanicalOverRevCeiling();
        if (coupledRpm <= startRpm || limitRpm <= startRpm + 1f)
        {
            return 0f;
        }

        return SmoothStep(startRpm, limitRpm, coupledRpm);
    }

    private float CalculateMechanicalLimiterContactIntensity(float coupledRpm)
    {
        float excessRpm = coupledRpm - _parameters.LimiterHardCutRpm;
        if (excessRpm <= 25f)
        {
            return 0f;
        }

        float warningRange = MathF.Max(160f, _parameters.DownshiftOverRevToleranceRpm + 550f);
        return MathHelper.Clamp(
            0.35f + SmoothStep(
                _parameters.LimiterHardCutRpm + 25f,
                _parameters.LimiterHardCutRpm + warningRange,
                coupledRpm) * 0.65f,
            0f,
            1f);
    }

    private float CalculateMechanicalLimiterBounceRpm(float contactIntensity)
    {
        return _parameters.LimiterHardCutRpm;
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

        return ShiftShockModel.Calculate(new ShiftShockInput(
            previousGear,
            targetGear,
            throttle,
            currentRpm,
            targetRpm,
            forcedTargetRpm,
            GetGearRatio(previousGear),
            GetGearRatio(targetGear),
            _parameters));
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
        State.MechanicalOverRevRpm = MathF.Max(State.MechanicalOverRevRpm, _pendingDownshiftOverRevRpm - _parameters.LimiterHardCutRpm);
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
        float overRevRpm = MathF.Max(0f, coupledRpm - _parameters.LimiterHardCutRpm);
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
        Span<float> physicalSupportHeights = stackalloc float[4];
        Span<float> supportHeights = stackalloc float[4];

        foreach (WheelRuntimeState wheel in _wheels)
        {
            Vector2 wheelPosition = center + right * wheel.LocalX + forward * wheel.LocalZ;
            groundHeights[(int)wheel.Corner] = _surfaceSampler.GetElevation(wheelPosition);
        }

        float[] visualNormalLoads = CalculateVisualNormalLoads(Vector2.Dot(State.Velocity, forward));
        UpdateVisualSuspension(dt, visualNormalLoads);
        for (int i = 0; i < _wheels.Length; i++)
        {
            physicalSupportHeights[i] = groundHeights[i] - _wheels[i].SuspensionCompressionMeters;
            supportHeights[i] = groundHeights[i] - _visualSuspensionCompressionMeters[i];
        }

        float groundFrontHeight = Average(groundHeights[(int)WheelCorner.FrontLeft], groundHeights[(int)WheelCorner.FrontRight]);
        float groundRearHeight = Average(groundHeights[(int)WheelCorner.RearLeft], groundHeights[(int)WheelCorner.RearRight]);
        float physicalFrontHeight = Average(physicalSupportHeights[(int)WheelCorner.FrontLeft], physicalSupportHeights[(int)WheelCorner.FrontRight]);
        float physicalRearHeight = Average(physicalSupportHeights[(int)WheelCorner.RearLeft], physicalSupportHeights[(int)WheelCorner.RearRight]);
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
        float physicalBodyPitch = -MathF.Atan2(physicalFrontHeight - physicalRearHeight, wheelbase);
        float physicalBodyRoll = MathHelper.Lerp(CalculateAxleRoll(physicalSupportHeights, false), CalculateAxleRoll(physicalSupportHeights, true), frontRollWeight);
        float visualBodyPitch = -MathF.Atan2(supportFrontHeight - supportRearHeight, wheelbase);
        float visualBodyRoll = MathHelper.Lerp(CalculateAxleRoll(supportHeights, false), CalculateAxleRoll(supportHeights, true), frontRollWeight);

        State.Position = new Vector3(State.Position.X, supportCenterHeight, State.Position.Z);
        State.WheelContactCenterHeightMeters = groundCenterHeight;
        State.GroundPitchRadians = MathHelper.Clamp(groundPitch, -0.18f, 0.18f);
        State.GroundRollRadians = MathHelper.Clamp(groundRoll, -0.14f, 0.14f);
        float physicsBodyPitch = MathHelper.Clamp(physicalBodyPitch, -0.18f, 0.18f);
        float physicsBodyRoll = MathHelper.Clamp(physicalBodyRoll, -0.14f, 0.14f);
        float presentationBodyPitch = MathHelper.Clamp(visualBodyPitch, -0.18f, 0.18f);
        float presentationBodyRoll = MathHelper.Clamp(visualBodyRoll, -0.14f, 0.14f);
        State.BodyPitchRadians = ScalePresentationBodyAngle(
            presentationBodyPitch,
            State.GroundPitchRadians,
            arcade.VisualBodyPitchScale,
            arcade.VisualBodyPitchLimitRadians);
        State.BodyRollRadians = ScalePresentationBodyAngle(
            presentationBodyRoll,
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

    private void UpdateVisualSuspension(float dt, float[] visualNormalLoads)
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
            float wheelVisualLoad = visualNormalLoads[i];
            float oppositeVisualLoad = visualNormalLoads[(int)oppositeWheel.Corner];
            float axleAverageLoad = Average(wheelVisualLoad, oppositeVisualLoad);
            float loadTransferRatio = (wheelVisualLoad - axleAverageLoad) / MathF.Max(1f, GetStaticWheelLoad(wheel.Corner));
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
        float ActiveSurfaceMu,
        float DisplacementDragForceN,
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

        public float LongitudinalTyreDeflectionMeters { get; set; }

        public float LateralTyreDeflectionMeters { get; set; }

        public float RelaxedLongitudinalSlipRatio { get; set; }

        public float RelaxedLateralSlip { get; set; }

        public float TyreRelaxationLengthMeters { get; set; } = 0.12f;

        public float FrictionEllipseTotalSlip { get; set; }

        public float FrictionEllipseGripBudgetN { get; set; }

        public float FrictionEllipseLongitudinalShare { get; set; }

        public float FrictionEllipseLateralShare { get; set; }

        public float FrictionEllipseLongitudinalForceN { get; set; }

        public float FrictionEllipseLateralForceN { get; set; }

        public float FrictionEllipseTotalForceN { get; set; }

        public float FrictionEllipseGripUsage { get; set; }

        public float SlipAngleRadians { get; set; }

        public float RelaxedSlipAngleRadians { get; set; }

        public float EffectiveCamberRadians { get; set; }

        public float EffectiveToeRadians { get; set; }

        public float SuspensionCompressionMeters { get; set; }

        public float GripUsage { get; set; }

        public float LongitudinalForceN { get; set; }

        public float RequestedLongitudinalForceN { get; set; }

        public float TyreScrubForceN { get; set; }

        public float SteeringProjectionForceN { get; set; }

        public float LateralForceN { get; set; }

        public float SurfaceGrip { get; set; } = 1f;

        public float StaticSurfaceMu { get; set; } = 1f;

        public float DynamicSurfaceMu { get; set; } = 0.78f;

        public float OptimalSurfaceSlipRatio { get; set; } = 0.10f;

        public float ActiveSurfaceMu { get; set; } = 1f;

        public float DisplacementDragForceN { get; set; }

        public float HandbrakeLockAmount { get; set; }

        public float HandbrakeSlideIntensity { get; set; }

        public float HandbrakeScreechFactor { get; set; } = 1f;

        public float CurbLoadMultiplier { get; set; } = 1f;

        public float SurfaceLoadMultiplier { get; set; } = 1f;

        public float SurfaceDragScale { get; set; }

        public float SurfaceBlendWeight { get; set; }

        public string SurfaceName { get; set; } = "ROAD";

        public float AbsPressureRatio { get; set; } = 1f;

        public bool AbsActive { get; set; }

        public bool IsLocked { get; set; }

        public void ResetTyreRelaxation()
        {
            LongitudinalTyreDeflectionMeters = 0f;
            LateralTyreDeflectionMeters = 0f;
            RelaxedLongitudinalSlipRatio = 0f;
            RelaxedLateralSlip = 0f;
            FrictionEllipseTotalSlip = 0f;
            FrictionEllipseGripBudgetN = 0f;
            FrictionEllipseLongitudinalShare = 0f;
            FrictionEllipseLateralShare = 0f;
            FrictionEllipseLongitudinalForceN = 0f;
            FrictionEllipseLateralForceN = 0f;
            FrictionEllipseTotalForceN = 0f;
            FrictionEllipseGripUsage = 0f;
        }
    }
}
