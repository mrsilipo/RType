using Microsoft.Xna.Framework;
using RType.World;

namespace RType.Vehicle;

public sealed class ClassicFourWheelVehicleSimulator : IVehicleSimulator
{
    private const float Gravity = 9.81f;
    private const float RpmToOmega = MathF.Tau / 60f;
    private const float OmegaToRpm = 60f / MathF.Tau;
    private const float RearYawMomentScale = 1.0f;

    private readonly ITrackSurfaceSampler _surfaceSampler;
    private readonly VehicleSimulationParameters _parameters;
    private readonly SimulationEngineParameters _engineParameters;
    private VehicleInput _pendingInput;
    private float _fixedTickAccumulatorSeconds;
    private bool _manualTransmission;
    private float _currentSteerRadians;
    private float _previousForwardSpeed;
    private float _previousLateralSpeed;
    private float _previousLongitudinalAcceleration;
    private float _previousLateralAcceleration;
    private float _engineCrankPhaseDegrees;
    private float _visualBodyPitchRadians;
    private float _visualBodyRollRadians;
    private float _rearSlipSettleSeconds;

    public ClassicFourWheelVehicleSimulator(
        ITrackSurfaceSampler surfaceSampler,
        Vector3 startPosition,
        float startHeadingRadians,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters? engineParameters = null)
    {
        _surfaceSampler = surfaceSampler;
        _parameters = parameters;
        _engineParameters = engineParameters ?? new SimulationEngineParameters();
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
            DisplayedRpm = _parameters.IdleRpm,
            DisplayedRpmTarget = _parameters.IdleRpm,
            EngineOmegaRadiansPerSecond = _parameters.IdleRpm * RpmToOmega,
            WheelContactCenterHeightMeters = startPosition.Y,
            BodyPivotHeightMeters = MathHelper.Clamp(_parameters.CenterOfGravityHeightMeters, 0.34f, 0.78f)
        };

        PublishStaticLoadState();
    }

    public VehicleState State { get; }

    public VehicleSimulationParameters Parameters => _parameters;

    public bool DisableYawRecoveryForProbe { get; set; }

    public void SetManualTransmission(bool enabled)
    {
        _manualTransmission = enabled;
        State.IsManualTransmission = enabled;
    }

    public void ToggleTransmissionMode()
    {
        SetManualTransmission(!_manualTransmission);
    }

    public void UpdateRaceStartHold(VehicleInput input, float dt)
    {
        float safeDt = Math.Clamp(dt, 0f, _engineParameters.Timing.MaximumFrameTimeSeconds);
        UpdateGear(input, 0f);
        float throttle = State.Gear < 0 ? input.Reverse : input.Throttle;
        AdvanceEnginePresentation(throttle, 0f, safeDt);
        State.Throttle = throttle;
        State.EffectiveThrottle = throttle;
        State.Brake = input.Brake;
        State.Handbrake = input.Handbrake;
        State.Steer = input.Steer;
    }

    public void Update(VehicleInput input, float dt)
    {
        float cappedDt = Math.Clamp(dt, 0f, _engineParameters.Timing.MaximumFrameTimeSeconds);
        _pendingInput = input;
        _fixedTickAccumulatorSeconds += cappedDt;

        float fixedDt = _engineParameters.Timing.FixedDeltaSeconds;
        int ticks = 0;
        while (_fixedTickAccumulatorSeconds >= fixedDt &&
               ticks < Math.Max(1, _engineParameters.Timing.MaximumTicksPerUpdate))
        {
            Step(_pendingInput, fixedDt);
            _pendingInput = ClearLatchedButtons(_pendingInput);
            _fixedTickAccumulatorSeconds -= fixedDt;
            ticks++;
        }

        if (ticks == 0 && cappedDt > 0f)
        {
            Step(_pendingInput, cappedDt);
            _pendingInput = ClearLatchedButtons(_pendingInput);
            _fixedTickAccumulatorSeconds = 0f;
        }

        State.PhysicsTickAlpha = fixedDt > 0f
            ? MathHelper.Clamp(_fixedTickAccumulatorSeconds / fixedDt, 0f, 1f)
            : 0f;
    }

    private void Step(VehicleInput input, float dt)
    {
        Vector2 forward = new(State.Forward.X, State.Forward.Z);
        Vector2 right = new(State.Right.X, State.Right.Z);
        float forwardSpeed = Vector2.Dot(State.Velocity, forward);
        float lateralSpeed = Vector2.Dot(State.Velocity, right);
        float speed = State.Velocity.Length();

        UpdateGear(input, forwardSpeed);
        float throttle = State.Gear < 0 ? input.Reverse : input.Throttle;
        float brake = input.Brake;
        float handbrake = input.Handbrake;
        UpdateSteering(input.Steer, speed, dt);

        ClassicBicycleParameters classic = _engineParameters.ClassicFourWheel;
        float mass = MathF.Max(1f, _parameters.MassKg);
        float wheelbase = MathF.Max(0.1f, _parameters.WheelbaseMeters);
        float frontTrack = MathF.Max(0.1f, _parameters.FrontTrackMeters);
        float rearTrack = MathF.Max(0.1f, _parameters.RearTrackMeters);
        float frontBias = MathHelper.Clamp(_parameters.FrontWeightDistribution, 0.05f, 0.95f);
        float frontDistance = wheelbase * (1f - frontBias);
        float rearDistance = wheelbase * frontBias;

        float driveForce = CalculateDriveForce(throttle, forwardSpeed);
        RouteDriveForce(driveForce, out float frontDriveForce, out float rearDriveForce);
        float engineBrakeForce = CalculateEngineBrakeForce(throttle, forwardSpeed) *
            CalculateCorneringEngineBrakeScale(input.Steer, brake, forwardSpeed, lateralSpeed);
        RouteDriveForce(engineBrakeForce, out float frontEngineBrakeForce, out float rearEngineBrakeForce);

        float brakeDirection = speed > 0.08f ? -MathF.Sign(forwardSpeed == 0f ? 1f : forwardSpeed) : 0f;
        float brakeForce = brake * MathF.Max(0f, _parameters.MaxBrakeForceN);
        float brakeBiasFront = MathHelper.Clamp(_parameters.BrakeBiasFront, 0f, 1f);
        float frontServiceBrakeForce = brakeDirection * brakeForce * brakeBiasFront;
        float rearServiceBrakeForce = brakeDirection * brakeForce * (1f - brakeBiasFront);
        float rearHandbrakeForce = brakeDirection * handbrake * MathF.Max(0f, _parameters.MaxBrakeForceN);

        WheelForces fl = SolveWheel(
            new WheelInput("FL", -frontTrack * 0.5f, frontDistance, _currentSteerRadians, true, true),
            (frontDriveForce + frontEngineBrakeForce + frontServiceBrakeForce) * 0.5f,
            classic.FrontTyres,
            mass,
            frontBias,
            wheelbase,
            frontTrack,
            forwardSpeed,
            lateralSpeed);
        WheelForces fr = SolveWheel(
            new WheelInput("FR", frontTrack * 0.5f, frontDistance, _currentSteerRadians, true, false),
            (frontDriveForce + frontEngineBrakeForce + frontServiceBrakeForce) * 0.5f,
            classic.FrontTyres,
            mass,
            frontBias,
            wheelbase,
            frontTrack,
            forwardSpeed,
            lateralSpeed);
        WheelForces rl = SolveWheel(
            new WheelInput("RL", -rearTrack * 0.5f, -rearDistance, 0f, false, true),
            (rearDriveForce + rearEngineBrakeForce + rearServiceBrakeForce + rearHandbrakeForce) * 0.5f,
            classic.RearTyres,
            mass,
            frontBias,
            wheelbase,
            rearTrack,
            forwardSpeed,
            lateralSpeed);
        WheelForces rr = SolveWheel(
            new WheelInput("RR", rearTrack * 0.5f, -rearDistance, 0f, false, false),
            (rearDriveForce + rearEngineBrakeForce + rearServiceBrakeForce + rearHandbrakeForce) * 0.5f,
            classic.RearTyres,
            mass,
            frontBias,
            wheelbase,
            rearTrack,
            forwardSpeed,
            lateralSpeed);

        float rollingResistance = _parameters.RollingResistanceCoefficient *
            classic.Resistance.RollingResistanceMultiplier *
            AverageRollingResistance(fl, fr, rl, rr) *
            mass *
            Gravity *
            MathF.Sign(forwardSpeed) *
            SmoothStep01(MathF.Abs(forwardSpeed) / 1.0f);
        float aeroDrag = _parameters.AeroDragFactor *
            classic.Resistance.AeroDragMultiplier *
            forwardSpeed *
            MathF.Abs(forwardSpeed);

        float averageFrontSlipDegrees = (
            MathF.Abs(MathHelper.ToDegrees(fl.SlipRadians)) +
            MathF.Abs(MathHelper.ToDegrees(fr.SlipRadians))) * 0.5f;
        float averageRearSlipDegrees = (
            MathF.Abs(MathHelper.ToDegrees(rl.SlipRadians)) +
            MathF.Abs(MathHelper.ToDegrees(rr.SlipRadians))) * 0.5f;
        float lateralVelocityDampingForce = lateralSpeed * mass * MathF.Max(0f, classic.Yaw.LateralVelocityDamping);
        float bodySlipDampingForce = CalculateBodySlipDampingForce(
            forwardSpeed,
            lateralSpeed,
            mass,
            _currentSteerRadians,
            averageFrontSlipDegrees,
            averageRearSlipDegrees,
            dt);
        float lateralCleanupForce = lateralVelocityDampingForce + bodySlipDampingForce;
        float corneringCleanupSpeedRetentionForce = CalculateCorneringCleanupSpeedRetentionForce(
            forwardSpeed,
            lateralSpeed,
            input.Steer,
            throttle,
            brake,
            lateralCleanupForce,
            mass);
        float longitudinalForce = fl.LocalForceForwardN + fr.LocalForceForwardN + rl.LocalForceForwardN + rr.LocalForceForwardN +
            corneringCleanupSpeedRetentionForce -
            rollingResistance -
            aeroDrag;
        float lateralForce = fl.LocalForceRightN + fr.LocalForceRightN + rl.LocalForceRightN + rr.LocalForceRightN -
            lateralCleanupForce;
        float localLongitudinalAcceleration = longitudinalForce / mass;
        float localLateralAcceleration = lateralForce / mass;

        Vector2 acceleration = forward * localLongitudinalAcceleration + right * localLateralAcceleration;
        State.Velocity += acceleration * dt;
        LimitTopSpeed();

        float frontYawTorque =
            fl.LocalRightMeters * fl.LocalForceForwardN - fl.LocalForwardMeters * fl.LocalForceRightN +
            fr.LocalRightMeters * fr.LocalForceForwardN - fr.LocalForwardMeters * fr.LocalForceRightN;
        float rearYawTorque = (
            rl.LocalRightMeters * rl.LocalForceForwardN - rl.LocalForwardMeters * rl.LocalForceRightN +
            rr.LocalRightMeters * rr.LocalForceForwardN - rr.LocalForwardMeters * rr.LocalForceRightN) *
            RearYawMomentScale;
        float yawTorque = frontYawTorque + rearYawTorque;
        float yawInertia = MathF.Max(1f, _parameters.YawInertiaKgM2 * MathF.Max(0.1f, classic.Yaw.InertiaScale));
        float frontYawAcceleration = frontYawTorque / yawInertia;
        float rearYawAcceleration = rearYawTorque / yawInertia;
        float naturalYawAcceleration = yawTorque / yawInertia;
        float yawDampingAcceleration = -State.YawRateRadiansPerSecond * MathF.Max(0f, classic.Yaw.Damping);
        float yawRecoveryAcceleration = DisableYawRecoveryForProbe
            ? 0f
            : CalculateYawRecoveryAcceleration(speed, wheelbase, classic);
        float rearFollowAcceleration = CalculateRearFollowAcceleration(
            forwardSpeed,
            lateralSpeed,
            rearDistance,
            yawInertia,
            rl.LocalForceRightN + rr.LocalForceRightN,
            rl.GripBudgetN + rr.GripBudgetN,
            classic,
            out float rearFollowForceDeficit);
        float yawAcceleration =
            naturalYawAcceleration +
            yawDampingAcceleration +
            yawRecoveryAcceleration +
            rearFollowAcceleration;
        State.YawRateRadiansPerSecond += yawAcceleration * dt;
        State.HeadingRadians = MathHelper.WrapAngle(State.HeadingRadians + State.YawRateRadiansPerSecond * dt);
        State.Position += new Vector3(State.Velocity.X, 0f, State.Velocity.Y) * dt;

        UpdateBodyPresentation(dt, localLongitudinalAcceleration, localLateralAcceleration, fl.LoadN, fr.LoadN, rl.LoadN, rr.LoadN);

        PublishState(input, throttle, brake, handbrake, forwardSpeed, lateralSpeed, localLongitudinalAcceleration, localLateralAcceleration,
            driveForce, engineBrakeForce, frontServiceBrakeForce + rearServiceBrakeForce, rearHandbrakeForce, rollingResistance, aeroDrag,
            fl, fr, rl, rr,
            naturalYawAcceleration,
            frontYawAcceleration,
            rearYawAcceleration,
            yawDampingAcceleration,
            yawRecoveryAcceleration,
            rearFollowAcceleration,
            rearFollowForceDeficit,
            bodySlipDampingForce,
            corneringCleanupSpeedRetentionForce);

        AdvanceEnginePresentation(throttle, forwardSpeed, dt);
        _previousForwardSpeed = forwardSpeed;
        _previousLateralSpeed = lateralSpeed;
        _previousLongitudinalAcceleration = localLongitudinalAcceleration;
        _previousLateralAcceleration = localLateralAcceleration;
    }

    private WheelForces SolveWheel(
        WheelInput wheel,
        float requestedLongitudinalForce,
        ClassicBicycleTyreParameters tyre,
        float mass,
        float frontBias,
        float wheelbase,
        float axleTrack,
        float chassisForwardSpeed,
        float chassisLateralSpeed)
    {
        float load = CalculateWheelLoad(wheel, mass, frontBias, wheelbase, axleTrack);
        Vector3 worldPosition = State.Position + State.Right * wheel.LocalRightMeters + State.Forward * wheel.LocalForwardMeters;
        SurfaceSample surface = _surfaceSampler.Sample(worldPosition);
        float surfaceMu = MathF.Max(0.05f, surface.StaticFrictionCoefficient);
        float maxForce = MathF.Max(1f, load * MathF.Max(0.01f, tyre.MaxGrip) * surfaceMu);

        float localForwardSpeed = chassisForwardSpeed + State.YawRateRadiansPerSecond * wheel.LocalRightMeters;
        float localLateralSpeed = chassisLateralSpeed - State.YawRateRadiansPerSecond * wheel.LocalForwardMeters;
        float slipDenominator = EffectiveSlipSpeed(localForwardSpeed, _engineParameters.ClassicFourWheel.LowSpeed.SlipSpeedFloorMetersPerSecond);
        float slipRadians = wheel.SteerRadians - MathF.Atan2(localLateralSpeed, slipDenominator);
        float requestedLateralForce = CalculateTyreLateralForce(slipRadians, maxForce, tyre);

        float longitudinal = requestedLongitudinalForce;
        float lateral = requestedLateralForce;
        float gripUsage = ClampCombinedForce(ref longitudinal, ref lateral, maxForce, _engineParameters.ClassicFourWheel.GripBudget.CombinedGripExponent);

        float sin = MathF.Sin(wheel.SteerRadians);
        float cos = MathF.Cos(wheel.SteerRadians);
        float localForceRight = longitudinal * sin + lateral * cos;
        float localForceForward = longitudinal * cos - lateral * sin;

        return new WheelForces(
            wheel.Name,
            wheel.LocalRightMeters,
            wheel.LocalForwardMeters,
            wheel.IsFront,
            wheel.IsLeft,
            surface,
            load,
            maxForce,
            slipRadians,
            requestedLongitudinalForce,
            requestedLateralForce,
            longitudinal,
            lateral,
            localForceRight,
            localForceForward,
            gripUsage);
    }

    private float CalculateWheelLoad(WheelInput wheel, float mass, float frontBias, float wheelbase, float axleTrack)
    {
        float staticFrontLoad = mass * Gravity * frontBias;
        float staticRearLoad = mass * Gravity * (1f - frontBias);
        float longTransfer = mass *
            _previousLongitudinalAcceleration *
            MathHelper.Clamp(_parameters.CenterOfGravityHeightMeters, 0.05f, 1.5f) /
            MathF.Max(0.1f, wheelbase);
        float frontAxleLoad = staticFrontLoad - longTransfer;
        float rearAxleLoad = staticRearLoad + longTransfer;
        float axleLoad = wheel.IsFront ? frontAxleLoad : rearAxleLoad;

        float axleShare = wheel.IsFront ? frontBias : 1f - frontBias;
        float lateralTransfer = mass *
            _previousLateralAcceleration *
            MathHelper.Clamp(_parameters.CenterOfGravityHeightMeters, 0.05f, 1.5f) /
            MathF.Max(0.1f, axleTrack) *
            axleShare;
        float load = axleLoad * 0.5f + MathF.Sign(wheel.LocalRightMeters) * lateralTransfer * 0.5f;
        return MathHelper.Clamp(load, 50f, mass * Gravity);
    }

    private float CalculateYawRecoveryAcceleration(float speedMetersPerSecond, float wheelbase, ClassicBicycleParameters classic)
    {
        if (speedMetersPerSecond <= 0.5f)
        {
            return 0f;
        }

        float steerRadians = MathHelper.Clamp(_currentSteerRadians, MathHelper.ToRadians(-32f), MathHelper.ToRadians(32f));
        float desiredYawRate = -speedMetersPerSecond / MathF.Max(0.1f, wheelbase) * MathF.Tan(steerRadians) * 0.34f;
        float speedGate = SmoothStep01((speedMetersPerSecond - 2f) / 10f);
        float steeringReleaseGate = 1f - SmoothStep01(MathF.Abs(steerRadians) / MathHelper.ToRadians(7.5f));
        float overRotation = MathF.Abs(State.YawRateRadiansPerSecond) - MathF.Abs(desiredYawRate);
        float overRotationGate = SmoothStep01(overRotation / MathHelper.ToRadians(28f));
        float responseShape = MathHelper.Lerp(0.85f, 1.55f, overRotationGate);
        float releaseAssist = MathHelper.Lerp(0f, 0.35f, steeringReleaseGate);
        float response = MathF.Max(0f, classic.Yaw.Damping) * (responseShape + releaseAssist);
        return (desiredYawRate - State.YawRateRadiansPerSecond) * response * speedGate;
    }

    private float CalculateRearFollowAcceleration(
        float forwardSpeed,
        float lateralSpeed,
        float rearDistance,
        float yawInertia,
        float actualRearLateralForceN,
        float rearGripBudgetN,
        ClassicBicycleParameters classic)
    {
        return CalculateRearFollowAcceleration(
            forwardSpeed,
            lateralSpeed,
            rearDistance,
            yawInertia,
            actualRearLateralForceN,
            rearGripBudgetN,
            classic,
            out _);
    }

    private float CalculateRearFollowAcceleration(
        float forwardSpeed,
        float lateralSpeed,
        float rearDistance,
        float yawInertia,
        float actualRearLateralForceN,
        float rearGripBudgetN,
        ClassicBicycleParameters classic,
        out float forceDeficitN)
    {
        float speed = MathF.Sqrt(forwardSpeed * forwardSpeed + lateralSpeed * lateralSpeed);
        forceDeficitN = 0f;
        if (speed <= 2f)
        {
            return 0f;
        }

        float rearAxleLateralSpeed = lateralSpeed + State.YawRateRadiansPerSecond * rearDistance;
        float slipDenominator = EffectiveSlipSpeed(forwardSpeed, classic.LowSpeed.SlipSpeedFloorMetersPerSecond);
        float rearSlipRadians = -MathF.Atan2(rearAxleLateralSpeed, slipDenominator);
        float absRearSlipDegrees = MathF.Abs(MathHelper.ToDegrees(rearSlipRadians));
        float slipGate = SmoothStep01((absRearSlipDegrees - classic.RearTyres.PeakSlipAngleDegrees) /
            MathF.Max(0.1f, classic.RearTyres.FalloffSlipAngleDegrees - classic.RearTyres.PeakSlipAngleDegrees));
        if (slipGate <= 0f)
        {
            return 0f;
        }

        float maxForce = MathF.Max(1f, rearGripBudgetN);
        float expectedTrackingForce = CalculateTyreLateralForce(rearSlipRadians, maxForce, classic.RearTyres);
        float deficit = expectedTrackingForce - actualRearLateralForceN;
        if (MathF.Sign(deficit) != MathF.Sign(expectedTrackingForce))
        {
            return 0f;
        }

        float steeringReleaseGate = 1f - SmoothStep01(MathF.Abs(MathHelper.ToDegrees(_currentSteerRadians)) / 9f);
        float speedGate = SmoothStep01((speed - 5f) / 15f);
        float assistScale = MathHelper.Lerp(0.10f, 0.42f, steeringReleaseGate) * slipGate * speedGate;
        forceDeficitN = deficit * assistScale;
        return forceDeficitN * rearDistance / MathF.Max(1f, yawInertia);
    }

    private float CalculateBodySlipDampingForce(
        float forwardSpeed,
        float lateralSpeed,
        float mass,
        float steerRadians,
        float frontSlipDegrees,
        float rearSlipDegrees,
        float dt)
    {
        float speed = MathF.Sqrt(forwardSpeed * forwardSpeed + lateralSpeed * lateralSpeed);
        if (speed <= 4f || MathF.Abs(lateralSpeed) <= 0.05f)
        {
            return 0f;
        }

        float bodySlipDegrees = MathF.Abs(MathHelper.ToDegrees(MathF.Atan2(lateralSpeed, MathF.Max(2f, MathF.Abs(forwardSpeed)))));
        float slipGate = SmoothStep01((bodySlipDegrees - 1.2f) / 5.3f);
        float speedGate = SmoothStep01((speed - 6f) / 18f);
        float dampingRate = MathHelper.Lerp(0.0f, 3.40f, slipGate * speedGate);
        float centeredRackGate = 1f - SmoothStep01(MathF.Abs(MathHelper.ToDegrees(steerRadians)) / 6f);
        float settleGate = SmoothStep01((bodySlipDegrees - 3f) / 7f) * centeredRackGate * speedGate;
        dampingRate += MathHelper.Lerp(0f, 1.00f, settleGate);
        float rearSlipExcess = rearSlipDegrees - frontSlipDegrees;
        float highSteerGate = SmoothStep01((MathF.Abs(MathHelper.ToDegrees(steerRadians)) - 7.5f) / 3.5f);
        if (rearSlipExcess > 3.0f && bodySlipDegrees > 5.5f && highSteerGate > 0.05f)
        {
            _rearSlipSettleSeconds = MathF.Min(1.5f, _rearSlipSettleSeconds + dt);
        }
        else
        {
            _rearSlipSettleSeconds = MathF.Max(0f, _rearSlipSettleSeconds - dt * 3f);
        }

        float rearSlipSettleGate = SmoothStep01((_rearSlipSettleSeconds - 0.12f) / 0.40f) *
            SmoothStep01((rearSlipExcess - 3.0f) / 5.0f) *
            SmoothStep01((bodySlipDegrees - 5.5f) / 7f) *
            highSteerGate *
            speedGate;
        dampingRate += MathHelper.Lerp(0f, 1.60f, rearSlipSettleGate);
        return lateralSpeed * mass * dampingRate;
    }

    private static float CalculateCorneringCleanupSpeedRetentionForce(
        float forwardSpeed,
        float lateralSpeed,
        float steerInput,
        float throttle,
        float brake,
        float lateralCleanupForce,
        float mass)
    {
        float speed = MathF.Sqrt(forwardSpeed * forwardSpeed + lateralSpeed * lateralSpeed);
        if (speed <= 12f || MathF.Abs(forwardSpeed) <= 2f || MathF.Abs(lateralSpeed) <= 0.05f)
        {
            return 0f;
        }

        float steerGate = SmoothStep01((MathF.Abs(steerInput) - 0.25f) / 0.50f);
        float coastGate = 1f - SmoothStep01((throttle - 0.05f) / 0.55f);
        float brakeGate = 1f - SmoothStep01(brake / 0.16f);
        float speedGate = SmoothStep01((speed - 18f) / 18f);
        float gate = steerGate * coastGate * brakeGate * speedGate;
        if (gate <= 0f)
        {
            return 0f;
        }

        float cleanupSpeedLossForce = MathF.Abs(lateralCleanupForce) *
            MathF.Abs(lateralSpeed) /
            MathF.Max(2f, MathF.Abs(forwardSpeed));
        float retainedForce = cleanupSpeedLossForce * 0.45f * gate;
        float maximumRetentionForce = mass * 1.65f;
        return MathF.Sign(forwardSpeed) * MathF.Min(retainedForce, maximumRetentionForce);
    }

    private void UpdateBodyPresentation(
        float dt,
        float localLongitudinalAcceleration,
        float localLateralAcceleration,
        float frontLeftLoadN,
        float frontRightLoadN,
        float rearLeftLoadN,
        float rearRightLoadN)
    {
        float clampedDt = MathHelper.Clamp(dt, 0f, 1f / 20f);
        float targetRoll = -MathHelper.Clamp(localLateralAcceleration / Gravity, -1.15f, 1.15f) * 0.022f;
        float targetPitch = -MathHelper.Clamp(localLongitudinalAcceleration / Gravity, -1.25f, 1.25f) * 0.020f;
        float blend = MathHelper.Clamp(1f - MathF.Exp(-13f * clampedDt), 0f, 1f);
        _visualBodyRollRadians = MathHelper.Lerp(_visualBodyRollRadians, targetRoll, blend);
        _visualBodyPitchRadians = MathHelper.Lerp(_visualBodyPitchRadians, targetPitch, blend);

        State.GroundRollRadians = 0f;
        State.GroundPitchRadians = 0f;
        State.BodyRollRadians = MathHelper.Clamp(_visualBodyRollRadians, MathHelper.ToRadians(-1.45f), MathHelper.ToRadians(1.45f));
        State.BodyPitchRadians = MathHelper.Clamp(_visualBodyPitchRadians, MathHelper.ToRadians(-1.45f), MathHelper.ToRadians(1.45f));

        float staticFrontCornerLoad = _parameters.MassKg * Gravity * MathHelper.Clamp(_parameters.FrontWeightDistribution, 0.05f, 0.95f) * 0.5f;
        float staticRearCornerLoad = _parameters.MassKg * Gravity * (1f - MathHelper.Clamp(_parameters.FrontWeightDistribution, 0.05f, 0.95f)) * 0.5f;
        State.FrontLeftVisualSuspensionCompressionMeters = CalculateVisualCompression(frontLeftLoadN, staticFrontCornerLoad);
        State.FrontRightVisualSuspensionCompressionMeters = CalculateVisualCompression(frontRightLoadN, staticFrontCornerLoad);
        State.RearLeftVisualSuspensionCompressionMeters = CalculateVisualCompression(rearLeftLoadN, staticRearCornerLoad);
        State.RearRightVisualSuspensionCompressionMeters = CalculateVisualCompression(rearRightLoadN, staticRearCornerLoad);
    }

    private static float CalculateVisualCompression(float loadN, float staticLoadN)
    {
        float loadDelta = (loadN - MathF.Max(1f, staticLoadN)) / MathF.Max(1f, staticLoadN);
        return MathHelper.Clamp(0.045f + loadDelta * 0.020f, 0.015f, 0.080f);
    }

    private void PublishState(
        VehicleInput input,
        float throttle,
        float brake,
        float handbrake,
        float forwardSpeed,
        float lateralSpeed,
        float localLongitudinalAcceleration,
        float localLateralAcceleration,
        float driveForceRequest,
        float engineBrakeForceRequest,
        float serviceBrakeForceRequest,
        float handbrakeForceRequest,
        float rollingResistance,
        float aeroDrag,
        WheelForces fl,
        WheelForces fr,
        WheelForces rl,
        WheelForces rr,
        float naturalYawAcceleration,
        float frontYawAcceleration,
        float rearYawAcceleration,
        float yawDampingAcceleration,
        float yawRecoveryAcceleration,
        float rearFollowAcceleration,
        float rearFollowForceDeficit,
        float bodySlipDampingForce,
        float corneringCleanupSpeedRetentionForce)
    {
        Vector2 forward = new(State.Forward.X, State.Forward.Z);
        Vector2 right = new(State.Right.X, State.Right.Z);
        float frontLoad = fl.LoadN + fr.LoadN;
        float rearLoad = rl.LoadN + rr.LoadN;
        float staticFrontLoad = _parameters.MassKg * Gravity * MathHelper.Clamp(_parameters.FrontWeightDistribution, 0.05f, 0.95f);
        float staticRearLoad = _parameters.MassKg * Gravity - staticFrontLoad;
        float frontMaxForce = fl.GripBudgetN + fr.GripBudgetN;
        float rearMaxForce = rl.GripBudgetN + rr.GripBudgetN;
        float frontLongitudinal = fl.LongitudinalForceN + fr.LongitudinalForceN;
        float rearLongitudinal = rl.LongitudinalForceN + rr.LongitudinalForceN;
        float frontLateral = fl.WheelLateralForceN + fr.WheelLateralForceN;
        float rearLateral = rl.WheelLateralForceN + rr.WheelLateralForceN;

        State.SignedForwardSpeed = Vector2.Dot(State.Velocity, forward);
        State.DisplayedSpeedMetersPerSecond = State.SpeedMetersPerSecond;
        State.LateralSpeed = Vector2.Dot(State.Velocity, right);
        State.LongitudinalAcceleration = localLongitudinalAcceleration;
        State.LateralAcceleration = localLateralAcceleration;
        State.PhysicalLoadTransferLongitudinalAcceleration = localLongitudinalAcceleration;
        State.PhysicalLoadTransferLateralAcceleration = localLateralAcceleration;
        State.SurfaceName = fl.Surface.Name;
        State.SurfaceGrip = fl.Surface.Grip;
        State.Throttle = throttle;
        State.EffectiveThrottle = throttle;
        State.Brake = brake;
        State.Handbrake = handbrake;
        State.Steer = input.Steer;
        State.DriveForce = driveForceRequest;
        State.BrakeForce = MathF.Abs(engineBrakeForceRequest) + MathF.Abs(serviceBrakeForceRequest) + MathF.Abs(handbrakeForceRequest);
        State.FrontBrakeTorqueNm = brake * _parameters.MaxBrakeForceN * _parameters.BrakeBiasFront * _parameters.WheelRadiusMeters;
        State.RearBrakeTorqueNm = (brake * _parameters.MaxBrakeForceN * (1f - _parameters.BrakeBiasFront) +
            handbrake * _parameters.MaxBrakeForceN) * _parameters.WheelRadiusMeters;
        State.RearHandbrakeLockAmount = handbrake;
        State.RearHandbrakeSlideIntensity = handbrake * MathHelper.Clamp(MathF.Abs(State.SignedForwardSpeed) / 12f, 0f, 1f);
        State.RearHandbrakeScreechFactor = (rl.Surface.HandbrakeScreechFactor + rr.Surface.HandbrakeScreechFactor) * 0.5f;
        State.EngineBrakeTorqueNm = MathF.Abs(engineBrakeForceRequest) > 0.01f && State.Gear != 0
            ? _parameters.EngineBrakeTorqueAtRpm(State.Rpm) * (1f - MathHelper.Clamp(throttle, 0f, 1f))
            : 0f;

        State.FrontStaticAxleLoadN = staticFrontLoad;
        State.RearStaticAxleLoadN = staticRearLoad;
        State.LongitudinalLoadTransferN = frontLoad - staticFrontLoad;
        State.FrontLateralLoadTransferN = MathF.Abs(fl.LoadN - fr.LoadN);
        State.RearLateralLoadTransferN = MathF.Abs(rl.LoadN - rr.LoadN);
        State.ClassicStaticFrontAxleLoadN = staticFrontLoad;
        State.ClassicStaticRearAxleLoadN = staticRearLoad;
        State.ClassicDynamicFrontAxleLoadN = frontLoad;
        State.ClassicDynamicRearAxleLoadN = rearLoad;
        State.ClassicLongitudinalLoadTransferN = frontLoad - staticFrontLoad;
        State.ClassicDriveForceRequestN = driveForceRequest;
        State.ClassicEngineBrakeForceRequestN = engineBrakeForceRequest;
        State.ClassicServiceBrakeForceRequestN = serviceBrakeForceRequest;
        State.ClassicHandbrakeForceRequestN = handbrakeForceRequest;
        State.ClassicRollingResistanceForceN = rollingResistance;
        State.ClassicAeroDragForceN = aeroDrag;
        State.ClassicFrontLongitudinalGripUsage = frontMaxForce > 1f ? MathHelper.Clamp(MathF.Abs(frontLongitudinal) / frontMaxForce, 0f, 1f) : 0f;
        State.ClassicRearLongitudinalGripUsage = rearMaxForce > 1f ? MathHelper.Clamp(MathF.Abs(rearLongitudinal) / rearMaxForce, 0f, 1f) : 0f;
        State.ClassicFrontLateralGripUsage = frontMaxForce > 1f ? MathHelper.Clamp(MathF.Abs(frontLateral) / frontMaxForce, 0f, 1f) : 0f;
        State.ClassicRearLateralGripUsage = rearMaxForce > 1f ? MathHelper.Clamp(MathF.Abs(rearLateral) / rearMaxForce, 0f, 1f) : 0f;
        State.ClassicBodySlipAngleDegrees = MathHelper.ToDegrees(MathF.Atan2(lateralSpeed, MathF.Max(2f, MathF.Abs(forwardSpeed))));
        State.ClassicNaturalYawAccelerationDegreesPerSecondSquared = MathHelper.ToDegrees(naturalYawAcceleration);
        State.ClassicFrontYawAccelerationDegreesPerSecondSquared = MathHelper.ToDegrees(frontYawAcceleration);
        State.ClassicRearYawAccelerationDegreesPerSecondSquared = MathHelper.ToDegrees(rearYawAcceleration);
        State.ClassicYawDampingAccelerationDegreesPerSecondSquared = MathHelper.ToDegrees(yawDampingAcceleration);
        State.ClassicYawRecoveryAccelerationDegreesPerSecondSquared = MathHelper.ToDegrees(yawRecoveryAcceleration);
        State.ClassicRearFollowAccelerationDegreesPerSecondSquared = MathHelper.ToDegrees(rearFollowAcceleration);
        State.ClassicRearFollowForceDeficitN = rearFollowForceDeficit;
        State.ClassicBodySlipDampingForceN = bodySlipDampingForce;
        State.ClassicCorneringCleanupSpeedRetentionForceN = corneringCleanupSpeedRetentionForce;

        PublishWheelState(fl, fr, rl, rr);

        State.FrontDifferentialManagedAxleTorqueNm = MathF.Abs(State.FrontLeftDriveTorqueNm + State.FrontRightDriveTorqueNm);
        State.RearDifferentialManagedAxleTorqueNm = MathF.Abs(State.RearLeftDriveTorqueNm + State.RearRightDriveTorqueNm);
        State.AverageSlipAngleDegrees = (
            MathF.Abs(State.FrontLeftSlipAngleDegrees) +
            MathF.Abs(State.FrontRightSlipAngleDegrees) +
            MathF.Abs(State.RearLeftSlipAngleDegrees) +
            MathF.Abs(State.RearRightSlipAngleDegrees)) * 0.25f;
        State.AverageSlipRatio = (
            MathF.Abs(State.FrontLeftSlipRatio) +
            MathF.Abs(State.FrontRightSlipRatio) +
            MathF.Abs(State.RearLeftSlipRatio) +
            MathF.Abs(State.RearRightSlipRatio)) * 0.25f;
        State.PeakRawSlipRatio = MathF.Max(
            MathF.Max(MathF.Abs(State.FrontLeftSlipRatio), MathF.Abs(State.FrontRightSlipRatio)),
            MathF.Max(MathF.Abs(State.RearLeftSlipRatio), MathF.Abs(State.RearRightSlipRatio)));
        State.PeakFrictionEllipseGripUsage = MathF.Max(
            MathF.Max(fl.GripUsage, fr.GripUsage),
            MathF.Max(rl.GripUsage, rr.GripUsage));
        State.SteeringFrontGripReserve = 1f - MathF.Max(fl.GripUsage, fr.GripUsage);
    }

    private void PublishWheelState(WheelForces fl, WheelForces fr, WheelForces rl, WheelForces rr)
    {
        State.FrontLeftLoadN = fl.LoadN;
        State.FrontRightLoadN = fr.LoadN;
        State.RearLeftLoadN = rl.LoadN;
        State.RearRightLoadN = rr.LoadN;
        State.FrontLeftSlipAngleDegrees = MathHelper.ToDegrees(fl.SlipRadians);
        State.FrontRightSlipAngleDegrees = MathHelper.ToDegrees(fr.SlipRadians);
        State.RearLeftSlipAngleDegrees = MathHelper.ToDegrees(rl.SlipRadians);
        State.RearRightSlipAngleDegrees = MathHelper.ToDegrees(rr.SlipRadians);
        State.FrontLeftRequestedLongitudinalForceN = fl.RequestedLongitudinalForceN;
        State.FrontRightRequestedLongitudinalForceN = fr.RequestedLongitudinalForceN;
        State.RearLeftRequestedLongitudinalForceN = rl.RequestedLongitudinalForceN;
        State.RearRightRequestedLongitudinalForceN = rr.RequestedLongitudinalForceN;
        State.FrontLeftLongitudinalForceN = fl.LocalForceForwardN;
        State.FrontRightLongitudinalForceN = fr.LocalForceForwardN;
        State.RearLeftLongitudinalForceN = rl.LocalForceForwardN;
        State.RearRightLongitudinalForceN = rr.LocalForceForwardN;
        State.FrontLeftLateralForceN = fl.LocalForceRightN;
        State.FrontRightLateralForceN = fr.LocalForceRightN;
        State.RearLeftLateralForceN = rl.LocalForceRightN;
        State.RearRightLateralForceN = rr.LocalForceRightN;
        State.FrontLeftGripUsage = fl.GripUsage;
        State.FrontRightGripUsage = fr.GripUsage;
        State.RearLeftGripUsage = rl.GripUsage;
        State.RearRightGripUsage = rr.GripUsage;
        State.FrontLeftFrictionEllipseGripBudgetN = fl.GripBudgetN;
        State.FrontRightFrictionEllipseGripBudgetN = fr.GripBudgetN;
        State.RearLeftFrictionEllipseGripBudgetN = rl.GripBudgetN;
        State.RearRightFrictionEllipseGripBudgetN = rr.GripBudgetN;
        State.FrontLeftFrictionEllipseGripUsage = fl.GripUsage;
        State.FrontRightFrictionEllipseGripUsage = fr.GripUsage;
        State.RearLeftFrictionEllipseGripUsage = rl.GripUsage;
        State.RearRightFrictionEllipseGripUsage = rr.GripUsage;
        State.FrontLeftDriveTorqueNm = fl.LongitudinalForceN * _parameters.WheelRadiusMeters;
        State.FrontRightDriveTorqueNm = fr.LongitudinalForceN * _parameters.WheelRadiusMeters;
        State.RearLeftDriveTorqueNm = rl.LongitudinalForceN * _parameters.WheelRadiusMeters;
        State.RearRightDriveTorqueNm = rr.LongitudinalForceN * _parameters.WheelRadiusMeters;
        State.FrontLeftSlipRatio = fl.GripBudgetN > 1f ? fl.RequestedLongitudinalForceN / fl.GripBudgetN : 0f;
        State.FrontRightSlipRatio = fr.GripBudgetN > 1f ? fr.RequestedLongitudinalForceN / fr.GripBudgetN : 0f;
        State.RearLeftSlipRatio = rl.GripBudgetN > 1f ? rl.RequestedLongitudinalForceN / rl.GripBudgetN : 0f;
        State.RearRightSlipRatio = rr.GripBudgetN > 1f ? rr.RequestedLongitudinalForceN / rr.GripBudgetN : 0f;
        State.FrontLeftWheelOmegaRadiansPerSecond = State.SignedForwardSpeed / MathF.Max(0.05f, _parameters.WheelRadiusMeters);
        State.FrontRightWheelOmegaRadiansPerSecond = State.FrontLeftWheelOmegaRadiansPerSecond;
        State.RearLeftWheelOmegaRadiansPerSecond = State.FrontLeftWheelOmegaRadiansPerSecond;
        State.RearRightWheelOmegaRadiansPerSecond = State.FrontLeftWheelOmegaRadiansPerSecond;
        State.FrontLeftSurfaceGrip = fl.Surface.Grip;
        State.FrontRightSurfaceGrip = fr.Surface.Grip;
        State.RearLeftSurfaceGrip = rl.Surface.Grip;
        State.RearRightSurfaceGrip = rr.Surface.Grip;
        State.FrontLeftSurfaceMu = fl.Surface.StaticFrictionCoefficient;
        State.FrontRightSurfaceMu = fr.Surface.StaticFrictionCoefficient;
        State.RearLeftSurfaceMu = rl.Surface.StaticFrictionCoefficient;
        State.RearRightSurfaceMu = rr.Surface.StaticFrictionCoefficient;
        State.FrontLeftSurfaceName = fl.Surface.Name;
        State.FrontRightSurfaceName = fr.Surface.Name;
        State.RearLeftSurfaceName = rl.Surface.Name;
        State.RearRightSurfaceName = rr.Surface.Name;
    }

    private void UpdateGear(VehicleInput input, float forwardSpeed)
    {
        bool reverseAllowed = MathF.Abs(forwardSpeed) < 0.75f || forwardSpeed < -0.05f;
        if (input.Reverse > 0.05f && reverseAllowed)
        {
            State.Gear = -1;
            State.IsShifting = false;
            return;
        }

        if (State.Gear < 0 && input.Reverse <= 0.05f && forwardSpeed > -0.5f)
        {
            State.Gear = 1;
        }

        if (_manualTransmission)
        {
            if (input.ShiftUpRequested && State.Gear < _parameters.ForwardGearRatios.Length)
            {
                ShiftTo(Math.Max(1, State.Gear + 1));
            }
            else if (input.ShiftDownRequested && State.Gear > 1)
            {
                ShiftTo(State.Gear - 1);
            }

            return;
        }

        if (State.Gear <= 0)
        {
            return;
        }

        if (State.Rpm > _parameters.PowerRedlineRpm && State.Gear < _parameters.ForwardGearRatios.Length)
        {
            ShiftTo(State.Gear + 1);
        }
        else if (State.Rpm < _parameters.DownshiftRpm && State.Gear > 1)
        {
            ShiftTo(State.Gear - 1);
        }
    }

    private void ShiftTo(int targetGear)
    {
        if (targetGear == State.Gear)
        {
            return;
        }

        State.LastCompletedShiftFromGear = State.Gear;
        State.LastCompletedShiftToGear = targetGear;
        State.LastCompletedShiftKickSeverity = 0.12f;
        State.ShiftKickIntensity = 0.12f;
        State.PowertrainShockIntensity = 0.05f;
        State.Gear = targetGear;
        State.IsShifting = false;
        State.ShiftTimeRemainingSeconds = 0f;
    }

    private void UpdateSteering(float steerInput, float speedMetersPerSecond, float dt)
    {
        float maxAngleDegrees = CalculateMaxSteerAngleDegrees(speedMetersPerSecond * 3.6f);
        float targetDegrees = MathHelper.Clamp(steerInput, -1f, 1f) * maxAngleDegrees;
        float currentDegrees = MathHelper.ToDegrees(_currentSteerRadians);
        bool returningTowardCenter = MathF.Abs(targetDegrees) < MathF.Abs(currentDegrees);
        float rate = returningTowardCenter
            ? CalculateGracefulSteeringReturnRate(currentDegrees, maxAngleDegrees)
            : _engineParameters.ClassicFourWheel.Steering.SteerSpeedDegreesPerSecond;
        currentDegrees = Approach(currentDegrees, targetDegrees, MathF.Max(1f, rate) * dt);
        _currentSteerRadians = MathHelper.ToRadians(currentDegrees);
        State.SteeringSpeedMatchedMaxAngleDegrees = maxAngleDegrees;
        State.FrontLeftSteerAngleDegrees = currentDegrees;
        State.FrontRightSteerAngleDegrees = currentDegrees;
    }

    private float CalculateGracefulSteeringReturnRate(float currentDegrees, float maxAngleDegrees)
    {
        float configuredRate = MathF.Max(1f, _engineParameters.ClassicFourWheel.Steering.ReturnSpeedDegreesPerSecond);
        float normalizedAngle = MathF.Abs(currentDegrees) / MathF.Max(1f, maxAngleDegrees);
        float nearCenterBlend = SmoothStep01((normalizedAngle - 0.18f) / 0.55f);
        return configuredRate * MathHelper.Lerp(0.30f, 1.0f, nearCenterBlend);
    }

    private float CalculateMaxSteerAngleDegrees(float speedKmh)
    {
        ClassicBicycleSteeringParameters steering = _engineParameters.ClassicFourWheel.Steering;
        if (speedKmh <= 60f)
        {
            return MathHelper.Lerp(steering.ZeroKmhAngleDegrees, steering.SixtyKmhAngleDegrees, SmoothStep01(speedKmh / 60f));
        }

        if (speedKmh <= 120f)
        {
            return MathHelper.Lerp(steering.SixtyKmhAngleDegrees, steering.OneTwentyKmhAngleDegrees, SmoothStep01((speedKmh - 60f) / 60f));
        }

        return MathHelper.Lerp(steering.OneTwentyKmhAngleDegrees, steering.TwoHundredKmhAngleDegrees, SmoothStep01((speedKmh - 120f) / 80f));
    }

    private static float CalculateTyreLateralForce(float slipRadians, float maxForceN, ClassicBicycleTyreParameters tyres)
    {
        float sign = MathF.Sign(slipRadians);
        if (sign == 0f)
        {
            return 0f;
        }

        float absSlipDegrees = MathF.Abs(MathHelper.ToDegrees(slipRadians));
        float peakSlip = MathF.Max(0.1f, tyres.PeakSlipAngleDegrees);
        float falloffSlip = MathF.Max(peakSlip + 0.1f, tyres.FalloffSlipAngleDegrees);
        float slidingForce = maxForceN * MathHelper.Clamp(tyres.SlidingGrip, 0f, 1.2f);
        float forceMagnitude;

        if (absSlipDegrees <= peakSlip)
        {
            float t = absSlipDegrees / peakSlip;
            float stiffnessShape = MathHelper.Clamp(tyres.CorneringStiffness / 7.5f, 0.45f, 2.25f);
            forceMagnitude = maxForceN * (1f - MathF.Pow(1f - SmoothStep01(t), stiffnessShape));
        }
        else if (absSlipDegrees <= falloffSlip)
        {
            float t = SmoothStep01((absSlipDegrees - peakSlip) / (falloffSlip - peakSlip));
            forceMagnitude = MathHelper.Lerp(maxForceN, slidingForce, t);
        }
        else
        {
            forceMagnitude = slidingForce;
        }

        return sign * forceMagnitude;
    }

    private float CalculateDriveForce(float throttle, float forwardSpeed)
    {
        float gearRatio = GetCurrentGearRatio();
        if (gearRatio <= 0f || throttle <= 0f || State.Gear == 0)
        {
            return 0f;
        }

        if (State.Gear > 0 && forwardSpeed > _engineParameters.VehicleSafety.MaximumForwardSpeedMetersPerSecond)
        {
            return 0f;
        }

        if (State.Gear < 0 && forwardSpeed < -_engineParameters.VehicleSafety.MaximumReverseSpeedMetersPerSecond)
        {
            return 0f;
        }

        float torque = _parameters.TorqueAtRpm(State.Rpm) * throttle;
        float wheelTorque = torque * gearRatio * _parameters.FinalDriveRatio * _parameters.DrivetrainEfficiency;
        float force = wheelTorque / MathF.Max(0.05f, _parameters.WheelRadiusMeters);
        return State.Gear < 0 ? -force : force;
    }

    private float CalculateEngineBrakeForce(float throttle, float forwardSpeed)
    {
        float gearRatio = GetCurrentGearRatio();
        if (gearRatio <= 0f || State.Gear == 0)
        {
            return 0f;
        }

        float closedThrottle = 1f - MathHelper.Clamp(throttle, 0f, 1f);
        float speedT = SmoothStep01(MathF.Abs(forwardSpeed) / 1.5f);
        if (closedThrottle <= 0.001f || speedT <= 0f)
        {
            return 0f;
        }

        float engineBrakeTorque = _parameters.EngineBrakeTorqueAtRpm(State.Rpm) * closedThrottle * speedT;
        float wheelTorque = engineBrakeTorque * gearRatio * _parameters.FinalDriveRatio * _parameters.DrivetrainEfficiency;
        float forceMagnitude = wheelTorque / MathF.Max(0.05f, _parameters.WheelRadiusMeters);
        float travelSign = MathF.Abs(forwardSpeed) > 0.05f
            ? MathF.Sign(forwardSpeed)
            : State.Gear < 0 ? -1f : 1f;
        return -travelSign * forceMagnitude;
    }

    private static float CalculateCorneringEngineBrakeScale(
        float steerInput,
        float brake,
        float forwardSpeed,
        float lateralSpeed)
    {
        float speed = MathF.Sqrt(forwardSpeed * forwardSpeed + lateralSpeed * lateralSpeed);
        if (speed <= 5f)
        {
            return 1f;
        }

        float bodySlipDegrees = MathF.Abs(MathHelper.ToDegrees(MathF.Atan2(lateralSpeed, MathF.Max(2f, MathF.Abs(forwardSpeed)))));
        float steerGate = SmoothStep01((MathF.Abs(steerInput) - 0.35f) / 0.45f);
        float bodySlipGate = SmoothStep01((bodySlipDegrees - 5f) / 9f);
        float brakeGate = SmoothStep01(brake / 0.45f);
        float lowSpeedGate = 1f - SmoothStep01((speed - 28f) / 24f);
        float reliefGate = MathF.Max(bodySlipGate, brakeGate * 0.7f) * steerGate * lowSpeedGate;
        return MathHelper.Lerp(1f, 0.48f, reliefGate);
    }

    private void RouteDriveForce(float driveForce, out float frontDriveForce, out float rearDriveForce)
    {
        switch (_parameters.DrivetrainLayout)
        {
            case DrivetrainLayout.FR:
                frontDriveForce = 0f;
                rearDriveForce = driveForce;
                break;
            case DrivetrainLayout.AWD:
                float frontShare = MathHelper.Clamp(_parameters.FrontTorqueShare, 0f, 1f);
                frontDriveForce = driveForce * frontShare;
                rearDriveForce = driveForce * (1f - frontShare);
                break;
            case DrivetrainLayout.FF:
            default:
                frontDriveForce = driveForce;
                rearDriveForce = 0f;
                break;
        }
    }

    private static float ClampCombinedForce(ref float longitudinal, ref float lateral, float maxForce, float exponent)
    {
        maxForce = MathF.Max(1f, maxForce);
        exponent = MathHelper.Clamp(exponent, 1.2f, 4f);
        float demand =
            MathF.Pow(MathF.Abs(longitudinal / maxForce), exponent) +
            MathF.Pow(MathF.Abs(lateral / maxForce), exponent);
        if (demand <= 1f)
        {
            return demand;
        }

        float scale = MathF.Pow(demand, -1f / exponent);
        longitudinal *= scale;
        lateral *= scale;
        return 1f;
    }

    private void AdvanceEnginePresentation(float throttle, float forwardSpeed, float dt)
    {
        State.PreviousPhysicsRpm = State.Rpm;
        float gearRatio = GetCurrentGearRatio();
        float roadRpm = gearRatio > 0f && State.Gear != 0
            ? MathF.Abs(forwardSpeed) / MathF.Max(0.05f, _parameters.WheelRadiusMeters) *
              gearRatio *
              _parameters.FinalDriveRatio *
              OmegaToRpm
            : 0f;
        float lowSpeedClutchT = SmoothStep01(MathF.Abs(forwardSpeed) / 4f);
        float freeRevTarget = _parameters.IdleRpm + throttle * (_parameters.LimiterHardCutRpm - _parameters.IdleRpm);
        float targetRpm = State.Gear == 0
            ? freeRevTarget
            : MathHelper.Lerp(MathF.Max(_parameters.IdleRpm, freeRevTarget), MathF.Max(_parameters.IdleRpm, roadRpm), lowSpeedClutchT);

        if (State.Gear > 0 && MathF.Abs(forwardSpeed) > 4f)
        {
            targetRpm = MathF.Max(_parameters.IdleRpm, roadRpm);
        }

        float rate = targetRpm > State.Rpm ? _parameters.MaxFreeRevRiseRpmPerSecond : _parameters.MaxFreeRevFallRpmPerSecond;
        State.Rpm = Approach(State.Rpm, targetRpm, MathF.Max(100f, rate) * dt);
        if (State.Rpm >= _parameters.LimiterHardCutRpm - 1f)
        {
            State.RevLimiterActive = true;
            State.Rpm = _parameters.LimiterHardCutRpm;
        }
        else if (State.Rpm <= _parameters.RevLimiterResumeRpm || throttle < 0.05f)
        {
            State.RevLimiterActive = false;
        }

        State.EngineOmegaRadiansPerSecond = State.Rpm * RpmToOmega;
        State.GearboxInputOmegaRadiansPerSecond = roadRpm * RpmToOmega;
        State.ClutchSlipDeltaRadiansPerSecond = State.EngineOmegaRadiansPerSecond - State.GearboxInputOmegaRadiansPerSecond;
        State.ClutchIsLocked = lowSpeedClutchT > 0.95f;
        State.ClutchEngagement = lowSpeedClutchT;
        _engineCrankPhaseDegrees = (_engineCrankPhaseDegrees + State.Rpm * 6f * dt) % 720f;
        State.EnginePowerUnitActive = false;
        State.EnginePowerUnitCrankRpm = State.Rpm;
        State.EnginePowerUnitCrankPhaseDegrees = _engineCrankPhaseDegrees;
        State.EnginePowerUnitLoad = throttle;
        State.RTypeEngineRpm = State.Rpm;
        State.RTypeEngineThrottle = throttle;
        State.RTypeEngineLimiterCut = State.RevLimiterActive;
    }

    private void PublishStaticLoadState()
    {
        float frontLoad = _parameters.MassKg * Gravity * MathHelper.Clamp(_parameters.FrontWeightDistribution, 0.05f, 0.95f);
        float rearLoad = _parameters.MassKg * Gravity - frontLoad;
        State.FrontStaticAxleLoadN = frontLoad;
        State.RearStaticAxleLoadN = rearLoad;
        State.ClassicStaticFrontAxleLoadN = frontLoad;
        State.ClassicStaticRearAxleLoadN = rearLoad;
        State.ClassicDynamicFrontAxleLoadN = frontLoad;
        State.ClassicDynamicRearAxleLoadN = rearLoad;
        State.FrontLeftLoadN = frontLoad * 0.5f;
        State.FrontRightLoadN = frontLoad * 0.5f;
        State.RearLeftLoadN = rearLoad * 0.5f;
        State.RearRightLoadN = rearLoad * 0.5f;
    }

    private void LimitTopSpeed()
    {
        float maxForward = MathF.Max(1f, _engineParameters.VehicleSafety.MaximumForwardSpeedMetersPerSecond);
        if (State.Velocity.Length() <= maxForward * 1.25f)
        {
            return;
        }

        State.Velocity = Vector2.Normalize(State.Velocity) * maxForward * 1.25f;
    }

    private float GetCurrentGearRatio()
    {
        if (State.Gear == 0)
        {
            return 0f;
        }

        if (State.Gear < 0)
        {
            return MathF.Max(0f, _parameters.ReverseGearRatio);
        }

        if (_parameters.ForwardGearRatios.Length == 0)
        {
            return 0f;
        }

        return _parameters.ForwardGearRatios[Math.Clamp(State.Gear, 1, _parameters.ForwardGearRatios.Length) - 1];
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

    private static float AverageRollingResistance(WheelForces fl, WheelForces fr, WheelForces rl, WheelForces rr)
    {
        return (fl.Surface.RollingResistanceMultiplier + fr.Surface.RollingResistanceMultiplier +
            rl.Surface.RollingResistanceMultiplier + rr.Surface.RollingResistanceMultiplier) * 0.25f;
    }

    private static float EffectiveSlipSpeed(float signedForwardSpeed, float floor)
    {
        float safeFloor = MathF.Max(0.1f, floor);
        float magnitude = MathF.Sqrt(signedForwardSpeed * signedForwardSpeed + safeFloor * safeFloor);
        return signedForwardSpeed >= 0f ? magnitude : -magnitude;
    }

    private static float Approach(float current, float target, float maxDelta)
    {
        if (current < target)
        {
            return MathF.Min(current + maxDelta, target);
        }

        return current > target ? MathF.Max(current - maxDelta, target) : current;
    }

    private static float SmoothStep01(float value)
    {
        float t = MathHelper.Clamp(value, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private readonly record struct WheelInput(
        string Name,
        float LocalRightMeters,
        float LocalForwardMeters,
        float SteerRadians,
        bool IsFront,
        bool IsLeft);

    private readonly record struct WheelForces(
        string Name,
        float LocalRightMeters,
        float LocalForwardMeters,
        bool IsFront,
        bool IsLeft,
        SurfaceSample Surface,
        float LoadN,
        float GripBudgetN,
        float SlipRadians,
        float RequestedLongitudinalForceN,
        float RequestedLateralForceN,
        float LongitudinalForceN,
        float WheelLateralForceN,
        float LocalForceRightN,
        float LocalForceForwardN,
        float GripUsage);
}
