using Microsoft.Xna.Framework;
using RType.World;

namespace RType.Vehicle;

public sealed class ClassicFourWheelVehicleSimulator : IVehicleSimulator
{
    private const float Gravity = 9.81f;
    private const float RpmToOmega = MathF.Tau / 60f;
    private const float OmegaToRpm = 60f / MathF.Tau;
    private const float RearYawMomentScale = 1.0f;
    private const float RestSleepSpeedMetersPerSecond = 0.08f;
    private const float RestSleepYawRateRadiansPerSecond = 0.015f;
    public const float BodySlipDampingStartDegrees = 3.8f;
    public const float BodySlipDampingEndDegrees = 11.8f;
    public const float BodySlipDampingRateCeiling = 3.40f;
    public const float BodySlipSettleStartDegrees = 6.0f;
    public const float BodySlipSettleEndDegrees = 13.0f;
    public const float BodySlipSettleRateCeiling = 1.00f;
    public const float RearSlipSettleStartDegrees = 7.5f;
    public const float RearSlipSettleEndDegrees = 14.0f;
    public const float RearSlipSettleRateCeiling = 1.60f;

    private readonly ITrackSurfaceSampler _surfaceSampler;
    private readonly VehicleSimulationParameters _parameters;
    private readonly SimulationEngineParameters _engineParameters;
    private VehicleInput _pendingInput;
    private float _fixedTickAccumulatorSeconds;
    private bool _manualTransmission;
    private float _currentSteerRadians;
    private float _currentSteerCommand;
    private float _steerHoldSeconds;
    private float _steeringTransientBoostSecondsRemaining;
    private float _previousForwardSpeed;
    private float _previousLateralSpeed;
    private float _previousLongitudinalAcceleration;
    private float _previousLateralAcceleration;
    private float _targetLongitudinalLoadTransferN;
    private float _actualLongitudinalLoadTransferN;
    private float _longitudinalLoadTransferVelocityNPerSecond;
    private float _targetFrontLateralLoadTransferN;
    private float _actualFrontLateralLoadTransferN;
    private float _frontLateralLoadTransferVelocityNPerSecond;
    private float _targetRearLateralLoadTransferN;
    private float _actualRearLateralLoadTransferN;
    private float _rearLateralLoadTransferVelocityNPerSecond;
    private float _engineCrankPhaseDegrees;
    private float _revLimiterCutTimerSeconds;
    private float _revLimiterRestoreTimerSeconds;
    private float _launchClutchCouplingSeconds;
    private int _revLimiterPulseIndex;
    private float _visualBodyPitchRadians;
    private float _visualBodyRollRadians;
    private float _rearSlipSettleSeconds;
    private float _frontLeftBrakePressureRatio = 1f;
    private float _frontRightBrakePressureRatio = 1f;
    private float _rearLeftBrakePressureRatio = 1f;
    private float _rearRightBrakePressureRatio = 1f;
    private float _frontLeftRelaxedLateralForceN;
    private float _frontRightRelaxedLateralForceN;
    private float _rearLeftRelaxedLateralForceN;
    private float _rearRightRelaxedLateralForceN;
    private float _frontLeftRelaxedLateralSlipRadians;
    private float _frontRightRelaxedLateralSlipRadians;
    private float _rearLeftRelaxedLateralSlipRadians;
    private float _rearRightRelaxedLateralSlipRadians;
    private float _previousYawRecoveryBetaRadians;
    private bool _hasYawRecoveryBetaSample;
    private SuspensionCornerState _frontLeftSuspension;
    private SuspensionCornerState _frontRightSuspension;
    private SuspensionCornerState _rearLeftSuspension;
    private SuspensionCornerState _rearRightSuspension;

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
        InitializeStaticSuspensionPresentation();
    }

    public VehicleState State { get; }

    public VehicleSimulationParameters Parameters => _parameters;

    public bool DisableYawRecoveryForProbe { get; set; }

    public bool UseLegacyYawRecoveryForProbe { get; set; }

    public bool DisableBrakePressureRegulatorForProbe { get; set; }

    public bool UseStaticWheelLoadsForProbe { get; set; }

    public bool UseStaticWeightLateralTransferSplitForProbe { get; set; }

    public float TyreLoadSensitivityOverrideForProbe { get; set; } = float.NaN;

    public float TyreRelaxationLengthOverrideForProbe { get; set; } = float.NaN;

    public float FrozenSteeringAngleDegreesForProbe { get; set; } = float.NaN;

    public float LowSpeedSteeringReturnRateMultiplierForProbe { get; set; } = float.NaN;

    public float FrontDriveSideSuppressionEndSpeedMetersPerSecondForProbe { get; set; } = float.NaN;

    public float LateralRelaxationSpeedFloorOverrideForProbe { get; set; } = float.NaN;

    public bool SmoothLateralRelaxationSpeedFloorForProbe { get; set; }

    public ClassicLowSpeedForceDiagnosticOptions LowSpeedForceDiagnosticOptionsForProbe { get; set; } =
        ClassicLowSpeedForceDiagnosticOptions.Default;

    public ClassicFourWheelAssistOptions AssistOptions { get; set; } = ClassicFourWheelAssistOptions.Default;

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
        _launchClutchCouplingSeconds = 0f;
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
        if (State.Gear != 0 && throttle > 0.05f)
        {
            _launchClutchCouplingSeconds = MathF.Min(1f, _launchClutchCouplingSeconds + dt);
        }
        else
        {
            _launchClutchCouplingSeconds = 0f;
        }

        UpdateSteering(input.Steer, speed, dt);

        ClassicBicycleParameters classic = _engineParameters.ClassicFourWheel;
        ClassicFourWheelTyres tyres = ResolveClassicTyres(_parameters, classic);
        if (float.IsFinite(TyreLoadSensitivityOverrideForProbe))
        {
            tyres = new ClassicFourWheelTyres(
                CopyTyreWithLoadSensitivity(tyres.Front, TyreLoadSensitivityOverrideForProbe),
                CopyTyreWithLoadSensitivity(tyres.Rear, TyreLoadSensitivityOverrideForProbe));
        }
        float mass = MathF.Max(1f, _parameters.MassKg);
        VehicleAxleGeometry geometry = VehicleAxleGeometry.FromParameters(_parameters);
        float wheelbase = geometry.WheelbaseMeters;
        float frontTrack = geometry.FrontTrackMeters;
        float rearTrack = geometry.RearTrackMeters;
        float frontBias = MathHelper.Clamp(_parameters.FrontWeightDistribution, 0.05f, 0.95f);
        float frontDistance = geometry.CgToFrontAxleMeters;
        float rearDistance = geometry.CgToRearAxleMeters;
        UpdateChassisLoadTransferState(mass, frontBias, wheelbase, frontTrack, rearTrack, dt);
        UpdatePerCornerSuspensionState(mass, frontBias, dt);

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
        float brakingSteeringBlend = CalculateBrakingSteeringBlend(brake, _currentSteerCommand);
        ClassicBicycleGripBudgetParameters gripBudget = classic.GripBudget;
        frontServiceBrakeForce *= MathHelper.Lerp(
            1f,
            MathHelper.Clamp(gripBudget.BrakingSteeringFrontBrakeMultiplier, 0.1f, 1f),
            brakingSteeringBlend);
        rearServiceBrakeForce *= MathHelper.Lerp(
            1f,
            MathHelper.Clamp(gripBudget.BrakingSteeringRearBrakeMultiplier, 0.05f, 1f),
            handbrake > 0.01f ? 0f : brakingSteeringBlend);
        float brakingSteeringLateralPriority = CalculateBrakingSteeringLateralPriority(brakingSteeringBlend);

        WheelForces fl = SolveWheel(
            new WheelInput("FL", -frontTrack * 0.5f, frontDistance, _currentSteerRadians, true, true),
            (frontDriveForce + frontEngineBrakeForce) * 0.5f,
            frontServiceBrakeForce * 0.5f,
            tyres.Front,
            ref _frontLeftRelaxedLateralForceN,
            ref _frontLeftRelaxedLateralSlipRadians,
            mass,
            frontBias,
            wheelbase,
            frontTrack,
            forwardSpeed,
            lateralSpeed,
            dt,
            brakingSteeringLateralPriority);
        WheelForces fr = SolveWheel(
            new WheelInput("FR", frontTrack * 0.5f, frontDistance, _currentSteerRadians, true, false),
            (frontDriveForce + frontEngineBrakeForce) * 0.5f,
            frontServiceBrakeForce * 0.5f,
            tyres.Front,
            ref _frontRightRelaxedLateralForceN,
            ref _frontRightRelaxedLateralSlipRadians,
            mass,
            frontBias,
            wheelbase,
            frontTrack,
            forwardSpeed,
            lateralSpeed,
            dt,
            brakingSteeringLateralPriority);
        WheelForces rl = SolveWheel(
            new WheelInput("RL", -rearTrack * 0.5f, -rearDistance, 0f, false, true),
            (rearDriveForce + rearEngineBrakeForce + rearHandbrakeForce) * 0.5f,
            rearServiceBrakeForce * 0.5f,
            tyres.Rear,
            ref _rearLeftRelaxedLateralForceN,
            ref _rearLeftRelaxedLateralSlipRadians,
            mass,
            frontBias,
            wheelbase,
            rearTrack,
            forwardSpeed,
            lateralSpeed,
            dt,
            handbrake > 0.01f ? 0f : brakingSteeringLateralPriority);
        WheelForces rr = SolveWheel(
            new WheelInput("RR", rearTrack * 0.5f, -rearDistance, 0f, false, false),
            (rearDriveForce + rearEngineBrakeForce + rearHandbrakeForce) * 0.5f,
            rearServiceBrakeForce * 0.5f,
            tyres.Rear,
            ref _rearRightRelaxedLateralForceN,
            ref _rearRightRelaxedLateralSlipRadians,
            mass,
            frontBias,
            wheelbase,
            rearTrack,
            forwardSpeed,
            lateralSpeed,
            dt,
            handbrake > 0.01f ? 0f : brakingSteeringLateralPriority);

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
        ClassicFourWheelAssistOptions assistOptions = AssistOptions ?? ClassicFourWheelAssistOptions.Default;
        float lateralVelocityDampingForce = assistOptions.LateralVelocityDampingEnabled
            ? lateralSpeed * mass * MathF.Max(0f, classic.Yaw.LateralVelocityDamping)
            : 0f;
        float bodySlipDampingForce = assistOptions.BodySlipDampingEnabled
            ? CalculateBodySlipDampingForce(
                forwardSpeed,
                lateralSpeed,
                mass,
                _currentSteerRadians,
                averageFrontSlipDegrees,
                averageRearSlipDegrees,
                dt)
            : 0f;
        float lateralCleanupForce = lateralVelocityDampingForce + bodySlipDampingForce;
        float corneringCleanupSpeedRetentionForce = assistOptions.SpeedRetentionEnabled
            ? CalculateCorneringCleanupSpeedRetentionForce(
                forwardSpeed,
                lateralSpeed,
                input.Steer,
                throttle,
                brake,
                lateralCleanupForce,
                mass)
            : 0f;
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
        LimitForwardGearSpeed();

        float frontYawTorque =
            fl.LocalForwardMeters * fl.LocalForceRightN - fl.LocalRightMeters * fl.LocalForceForwardN +
            fr.LocalForwardMeters * fr.LocalForceRightN - fr.LocalRightMeters * fr.LocalForceForwardN;
        float rearYawTorque = (
            rl.LocalForwardMeters * rl.LocalForceRightN - rl.LocalRightMeters * rl.LocalForceForwardN +
            rr.LocalForwardMeters * rr.LocalForceRightN - rr.LocalRightMeters * rr.LocalForceForwardN) *
            RearYawMomentScale;
        float yawTorque = frontYawTorque + rearYawTorque;
        float yawInertia = MathF.Max(1f, _parameters.YawInertiaKgM2 * MathF.Max(0.1f, classic.Yaw.InertiaScale));
        float frontYawAcceleration = frontYawTorque / yawInertia;
        float rearYawAcceleration = rearYawTorque / yawInertia;
        float naturalYawAcceleration = yawTorque / yawInertia;
        float yawDampingAcceleration = -State.YawRateRadiansPerSecond * MathF.Max(0f, classic.Yaw.Damping);
        float yawRecoveryAcceleration = DisableYawRecoveryForProbe || !assistOptions.YawRecoveryEnabled
            ? ResetYawRecoveryTelemetry()
            : CalculateYawRecoveryAcceleration(speed, wheelbase, classic, dt);
        float rearFollowForceDeficit = 0f;
        float rearFollowAcceleration = assistOptions.RearFollowEnabled
            ? CalculateRearFollowAcceleration(
                forwardSpeed,
                lateralSpeed,
                rearDistance,
                yawInertia,
                rl.LocalForceRightN + rr.LocalForceRightN,
                rl.GripBudgetN + rr.GripBudgetN,
                tyres.Rear,
                classic,
                out rearFollowForceDeficit)
            : 0f;
        float yawAcceleration =
            naturalYawAcceleration +
            yawDampingAcceleration +
            yawRecoveryAcceleration +
            rearFollowAcceleration;
        State.YawRateRadiansPerSecond += yawAcceleration * dt;
        ApplyLowSpeedRollingContactConstraint(wheelbase, yawInertia, dt);
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

        ApplyRestSleep(input, throttle, brake, handbrake);
        AdvanceEnginePresentation(throttle, forwardSpeed, dt);
        _previousForwardSpeed = forwardSpeed;
        _previousLateralSpeed = lateralSpeed;
        _previousLongitudinalAcceleration = localLongitudinalAcceleration;
        _previousLateralAcceleration = localLateralAcceleration;
    }

    private WheelForces SolveWheel(
        WheelInput wheel,
        float requestedNonServiceLongitudinalForce,
        float requestedServiceBrakeForce,
        ClassicBicycleTyreParameters tyre,
        ref float relaxedLateralForce,
        ref float relaxedLateralSlipRadians,
        float mass,
        float frontBias,
        float wheelbase,
        float axleTrack,
        float chassisForwardSpeed,
        float chassisLateralSpeed,
        float dt,
        float brakingSteeringLateralPriority)
    {
        float load = CalculateWheelLoad(wheel, mass, frontBias, wheelbase, axleTrack);
        Vector3 worldPosition = State.Position + State.Right * wheel.LocalRightMeters + State.Forward * wheel.LocalForwardMeters;
        SurfaceSample surface = _surfaceSampler.Sample(worldPosition);
        float surfaceMu = MathF.Max(0.05f, surface.StaticFrictionCoefficient);
        float maxForce = CalculateClassicTyreGripLimit(load, surfaceMu, tyre);

        WheelKinematicsSample kinematics = WheelKinematics.Calculate(
            wheel.LocalRightMeters,
            wheel.LocalForwardMeters,
            wheel.SteerRadians,
            chassisForwardSpeed,
            chassisLateralSpeed,
            State.YawRateRadiansPerSecond,
            _engineParameters.ClassicFourWheel.LowSpeed.SlipSpeedFloorMetersPerSecond);
        float slipRadians = kinematics.SlipRadians;
        ClassicLowSpeedForceDiagnosticOptions lowSpeedDiagnostics = LowSpeedForceDiagnosticOptionsForProbe;
        float forceSlipRadians = slipRadians;
        bool useContactPatchSlipRelaxation = lowSpeedDiagnostics.UseContactPatchSlipRelaxation;
        float slipRelaxationTimeSeconds = 0f;
        if (useContactPatchSlipRelaxation)
        {
            forceSlipRadians = StepRelaxedLateralSlip(
                ref relaxedLateralSlipRadians,
                slipRadians,
                tyre.RelaxationLengthMeters,
                kinematics.LocalForwardSpeedMetersPerSecond,
                float.IsFinite(LateralRelaxationSpeedFloorOverrideForProbe)
                    ? LateralRelaxationSpeedFloorOverrideForProbe
                    : 3.0f,
                SmoothLateralRelaxationSpeedFloorForProbe,
                dt,
                out slipRelaxationTimeSeconds);
        }
        else if (lowSpeedDiagnostics.LimitLowSpeedSlipRate ||
            _engineParameters.ClassicFourWheel.LowSpeed.SlipRateLimitEnabled)
        {
            bool diagnosticOverride = lowSpeedDiagnostics.LimitLowSpeedSlipRate;
            ClassicBicycleLowSpeedParameters lowSpeed = _engineParameters.ClassicFourWheel.LowSpeed;
            forceSlipRadians = StepRateLimitedLateralSlip(
                ref relaxedLateralSlipRadians,
                slipRadians,
                kinematics.LocalForwardSpeedMetersPerSecond,
                diagnosticOverride
                    ? lowSpeedDiagnostics.MaxLowSpeedSlipRateDegreesPerSecond
                    : lowSpeed.MaxSlipRateDegreesPerSecond,
                diagnosticOverride
                    ? lowSpeedDiagnostics.SlipRateLimitFadeStartMetersPerSecond
                    : lowSpeed.SlipRateLimitFadeStartMetersPerSecond,
                diagnosticOverride
                    ? lowSpeedDiagnostics.SlipRateLimitFadeEndMetersPerSecond
                    : lowSpeed.SlipRateLimitFadeEndMetersPerSecond,
                dt);
        }
        else
        {
            relaxedLateralSlipRadians = slipRadians;
        }

        float lowSpeedLateralForceScale = CalculateLowSpeedLateralForceScale(
            kinematics.LocalForwardSpeedMetersPerSecond,
            kinematics.LocalLateralSpeedMetersPerSecond,
            wheel.IsFront,
            _engineParameters.ClassicFourWheel.LowSpeed);
        float diagnosticSpeedFade = CalculateLowSpeedDiagnosticFade(
            kinematics.LocalForwardSpeedMetersPerSecond,
            kinematics.LocalLateralSpeedMetersPerSecond,
            lowSpeedDiagnostics.WalkingSpeedMetersPerSecond);
        float frontSlipMultiplier = wheel.IsFront
            ? MathHelper.Lerp(1f, lowSpeedDiagnostics.FrontSlipLateralMultiplier, diagnosticSpeedFade)
            : 1f;
        float rearResistanceMultiplier = !wheel.IsFront
            ? MathHelper.Lerp(1f, lowSpeedDiagnostics.RearLateralResistanceMultiplier, diagnosticSpeedFade)
            : 1f;
        float slipDerivedLateralForce = CalculateDiagnosticTyreLateralForce(forceSlipRadians, maxForce, tyre) *
            lowSpeedLateralForceScale *
            frontSlipMultiplier *
            rearResistanceMultiplier;
        float requestedLateralForce = ApplyLowSpeedRollingConstraint(
            slipDerivedLateralForce,
            wheel,
            kinematics.LocalForwardSpeedMetersPerSecond,
            kinematics.WheelRightSpeedMetersPerSecond,
            maxForce,
            mass,
            MathF.Max(1f, _parameters.YawInertiaKgM2 * MathF.Max(0.1f, _engineParameters.ClassicFourWheel.Yaw.InertiaScale)),
            dt,
            _engineParameters.ClassicFourWheel.LowSpeed,
            out float rollingConstraintForce,
            out float rollingConstraintBlend);
        if (lowSpeedDiagnostics.SlipDerivedOnlyBelowTransition)
        {
            requestedLateralForce = MathHelper.Lerp(requestedLateralForce, slipDerivedLateralForce, rollingConstraintBlend);
        }

        float relaxationLengthOverride = float.IsFinite(TyreRelaxationLengthOverrideForProbe)
            ? MathHelper.Clamp(TyreRelaxationLengthOverrideForProbe, 0f, 2.5f)
            : tyre.RelaxationLengthMeters;
        float dynamicEndSpeed = MathF.Max(
            _engineParameters.ClassicFourWheel.LowSpeed.RollingDominantEndMetersPerSecond + 0.25f,
            _engineParameters.ClassicFourWheel.LowSpeed.DynamicBlendEndMetersPerSecond);
        if (lowSpeedDiagnostics.BypassLateralRelaxationBelowTransition &&
            MathF.Abs(kinematics.LocalForwardSpeedMetersPerSecond) < dynamicEndSpeed)
        {
            relaxationLengthOverride = 0f;
        }
        if (useContactPatchSlipRelaxation)
        {
            relaxationLengthOverride = 0f;
        }

        float relaxationTimeSeconds;
        float relaxationLengthMeters;
        float relaxedRequestedLateralForce = StepRelaxedLateralForce(
            ref relaxedLateralForce,
            requestedLateralForce,
            relaxationLengthOverride,
            kinematics.LocalForwardSpeedMetersPerSecond,
            float.IsFinite(LateralRelaxationSpeedFloorOverrideForProbe)
                ? LateralRelaxationSpeedFloorOverrideForProbe
                : 3.0f,
            SmoothLateralRelaxationSpeedFloorForProbe,
            lowSpeedDiagnostics.UnwindLateralForceBeforeSignChange,
            dt,
            out relaxationTimeSeconds,
            out relaxationLengthMeters);
        if (useContactPatchSlipRelaxation)
        {
            relaxationTimeSeconds = slipRelaxationTimeSeconds;
        }
        bool brakePressureRegulatorActive = false;
        float brakePressureRatio = ApplyBrakePressureRegulator(
            wheel,
            requestedNonServiceLongitudinalForce,
            requestedServiceBrakeForce,
            relaxedRequestedLateralForce,
            maxForce,
            _engineParameters.ClassicFourWheel.GripBudget.CombinedGripExponent,
            kinematics.LocalForwardSpeedMetersPerSecond,
            dt,
            out brakePressureRegulatorActive);
        float requestedLongitudinalForce = requestedNonServiceLongitudinalForce + requestedServiceBrakeForce * brakePressureRatio;

        float longitudinal = requestedLongitudinalForce;
        float lateral = relaxedRequestedLateralForce;
        float gripUsage = ClampCombinedForce(
            ref longitudinal,
            ref lateral,
            maxForce,
            _engineParameters.ClassicFourWheel.GripBudget.CombinedGripExponent,
            brakingSteeringLateralPriority);

        float sin = MathF.Sin(wheel.SteerRadians);
        float cos = MathF.Cos(wheel.SteerRadians);
        float driveSideFade = diagnosticSpeedFade;
        if (float.IsFinite(FrontDriveSideSuppressionEndSpeedMetersPerSecondForProbe))
        {
            float endSpeed = MathF.Max(0.25f, FrontDriveSideSuppressionEndSpeedMetersPerSecondForProbe);
            float localSpeed = MathF.Sqrt(
                kinematics.LocalForwardSpeedMetersPerSecond * kinematics.LocalForwardSpeedMetersPerSecond +
                kinematics.LocalLateralSpeedMetersPerSecond * kinematics.LocalLateralSpeedMetersPerSecond);
            driveSideFade = 1f - SmoothStep01(localSpeed / endSpeed);
        }
        float driveSideMultiplier = wheel.IsFront
            ? MathHelper.Lerp(1f, lowSpeedDiagnostics.FrontDriveSideMultiplier, driveSideFade)
            : 1f;
        float localForceRight = longitudinal * sin * driveSideMultiplier + lateral * cos;
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
            kinematics.LocalForwardSpeedMetersPerSecond,
            kinematics.LocalLateralSpeedMetersPerSecond,
            kinematics.YawLateralContributionMetersPerSecond,
            requestedLongitudinalForce,
            slipDerivedLateralForce,
            rollingConstraintForce,
            rollingConstraintBlend,
            requestedLateralForce,
            relaxedRequestedLateralForce,
            forceSlipRadians,
            requestedLateralForce - relaxedRequestedLateralForce,
            relaxationTimeSeconds,
            relaxationLengthMeters,
            lowSpeedLateralForceScale,
            longitudinal,
            lateral,
            localForceRight,
            localForceForward,
            gripUsage,
            brakePressureRatio,
            brakePressureRegulatorActive);
    }

    private float CalculateWheelLoad(WheelInput wheel, float mass, float frontBias, float wheelbase, float axleTrack)
    {
        float staticFrontLoad = mass * Gravity * frontBias;
        float staticRearLoad = mass * Gravity * (1f - frontBias);
        if (UseStaticWheelLoadsForProbe)
        {
            float staticAxleLoad = wheel.IsFront ? staticFrontLoad : staticRearLoad;
            return MathHelper.Clamp(staticAxleLoad * 0.5f, 50f, mass * Gravity);
        }

        if (_engineParameters.ClassicFourWheel.ChassisLoadTransfer.Enabled)
        {
            return wheel.Name switch
            {
                "FL" => MathHelper.Clamp(_frontLeftSuspension.NormalLoadN, 50f, mass * Gravity),
                "FR" => MathHelper.Clamp(_frontRightSuspension.NormalLoadN, 50f, mass * Gravity),
                "RL" => MathHelper.Clamp(_rearLeftSuspension.NormalLoadN, 50f, mass * Gravity),
                "RR" => MathHelper.Clamp(_rearRightSuspension.NormalLoadN, 50f, mass * Gravity),
                _ => 50f
            };
        }

        float frontAxleLoad = staticFrontLoad + _actualLongitudinalLoadTransferN;
        float rearAxleLoad = staticRearLoad - _actualLongitudinalLoadTransferN;
        float axleLoad = wheel.IsFront ? frontAxleLoad : rearAxleLoad;
        float lateralTransfer = wheel.IsFront
            ? _actualFrontLateralLoadTransferN
            : _actualRearLateralLoadTransferN;
        float load = axleLoad * 0.5f + MathF.Sign(wheel.LocalRightMeters) * lateralTransfer * 0.5f;
        return MathHelper.Clamp(load, 50f, mass * Gravity);
    }

    private void UpdateChassisLoadTransferState(
        float mass,
        float frontBias,
        float wheelbase,
        float frontTrack,
        float rearTrack,
        float dt)
    {
        float cgHeight = MathHelper.Clamp(_parameters.CenterOfGravityHeightMeters, 0.05f, 1.5f);
        _targetLongitudinalLoadTransferN = -mass *
            _previousLongitudinalAcceleration *
            cgHeight /
            MathF.Max(0.1f, wheelbase);
        float frontRollStiffness = CalculateAxleRollStiffness(
            _parameters.FrontSpringRateNPerM,
            frontTrack,
            _parameters.FrontAntiRollBarRateNmPerRad);
        float rearRollStiffness = CalculateAxleRollStiffness(
            _parameters.RearSpringRateNPerM,
            rearTrack,
            _parameters.RearAntiRollBarRateNmPerRad);
        float totalRollStiffness = frontRollStiffness + rearRollStiffness;
        float frontTransferShare = totalRollStiffness > 0.001f
            ? frontRollStiffness / totalRollStiffness
            : frontBias;
        frontTransferShare = UseStaticWeightLateralTransferSplitForProbe
            ? frontBias
            : MathHelper.Clamp(frontTransferShare, 0.05f, 0.95f);
        float rearTransferShare = 1f - frontTransferShare;

        float totalLateralTransferMoment = -mass * _previousLateralAcceleration * cgHeight;
        _targetFrontLateralLoadTransferN =
            totalLateralTransferMoment *
            frontTransferShare /
            MathF.Max(0.1f, frontTrack);
        _targetRearLateralLoadTransferN =
            totalLateralTransferMoment *
            rearTransferShare /
            MathF.Max(0.1f, rearTrack);

        State.ClassicFrontRollStiffnessNmPerRad = frontRollStiffness;
        State.ClassicRearRollStiffnessNmPerRad = rearRollStiffness;
        State.ClassicFrontRollStiffnessShare = frontTransferShare;
        State.ClassicRearRollStiffnessShare = rearTransferShare;
        State.ClassicStaticWeightFrontLateralTransferShare = frontBias;

        ClassicChassisLoadTransferParameters transfer = _engineParameters.ClassicFourWheel.ChassisLoadTransfer;
        if (!transfer.Enabled)
        {
            _actualLongitudinalLoadTransferN = _targetLongitudinalLoadTransferN;
            _actualFrontLateralLoadTransferN = _targetFrontLateralLoadTransferN;
            _actualRearLateralLoadTransferN = _targetRearLateralLoadTransferN;
            _longitudinalLoadTransferVelocityNPerSecond = 0f;
            _frontLateralLoadTransferVelocityNPerSecond = 0f;
            _rearLateralLoadTransferVelocityNPerSecond = 0f;
        }
        else
        {
            StepSecondOrder(
                ref _actualLongitudinalLoadTransferN,
                ref _longitudinalLoadTransferVelocityNPerSecond,
                _targetLongitudinalLoadTransferN,
                transfer.LongitudinalNaturalFrequencyHz,
                transfer.LongitudinalDampingRatio,
                dt);
            StepSecondOrder(
                ref _actualFrontLateralLoadTransferN,
                ref _frontLateralLoadTransferVelocityNPerSecond,
                _targetFrontLateralLoadTransferN,
                transfer.LateralNaturalFrequencyHz,
                transfer.LateralDampingRatio,
                dt);
            StepSecondOrder(
                ref _actualRearLateralLoadTransferN,
                ref _rearLateralLoadTransferVelocityNPerSecond,
                _targetRearLateralLoadTransferN,
                transfer.LateralNaturalFrequencyHz,
                transfer.LateralDampingRatio,
                dt);
        }

        float staticFrontLoad = mass * Gravity * frontBias;
        float staticRearLoad = mass * Gravity * (1f - frontBias);
        float maximumForwardTransfer = staticRearLoad - 100f;
        float maximumRearwardTransfer = staticFrontLoad - 100f;
        _actualLongitudinalLoadTransferN = MathHelper.Clamp(
            _actualLongitudinalLoadTransferN,
            -maximumRearwardTransfer,
            maximumForwardTransfer);
        _targetLongitudinalLoadTransferN = MathHelper.Clamp(
            _targetLongitudinalLoadTransferN,
            -maximumRearwardTransfer,
            maximumForwardTransfer);

        State.ClassicTargetLongitudinalLoadTransferN = _targetLongitudinalLoadTransferN;
        State.ClassicActualLongitudinalLoadTransferN = _actualLongitudinalLoadTransferN;
        State.ClassicLongitudinalLoadTransferVelocityNPerSecond = _longitudinalLoadTransferVelocityNPerSecond;
        State.ClassicTargetFrontLateralLoadTransferN = _targetFrontLateralLoadTransferN;
        State.ClassicActualFrontLateralLoadTransferN = _actualFrontLateralLoadTransferN;
        State.ClassicFrontLateralLoadTransferVelocityNPerSecond = _frontLateralLoadTransferVelocityNPerSecond;
        State.ClassicTargetRearLateralLoadTransferN = _targetRearLateralLoadTransferN;
        State.ClassicActualRearLateralLoadTransferN = _actualRearLateralLoadTransferN;
        State.ClassicRearLateralLoadTransferVelocityNPerSecond = _rearLateralLoadTransferVelocityNPerSecond;
    }

    private void UpdatePerCornerSuspensionState(float mass, float frontBias, float dt)
    {
        float staticFrontCornerLoad = mass * Gravity * frontBias * 0.5f;
        float staticRearCornerLoad = mass * Gravity * (1f - frontBias) * 0.5f;

        if (UseStaticWheelLoadsForProbe)
        {
            _frontLeftSuspension = SuspensionCornerState.Static(staticFrontCornerLoad);
            _frontRightSuspension = SuspensionCornerState.Static(staticFrontCornerLoad);
            _rearLeftSuspension = SuspensionCornerState.Static(staticRearCornerLoad);
            _rearRightSuspension = SuspensionCornerState.Static(staticRearCornerLoad);
            PublishSuspensionState();
            return;
        }

        float frontAxleLoad = staticFrontCornerLoad * 2f + _actualLongitudinalLoadTransferN;
        float rearAxleLoad = staticRearCornerLoad * 2f - _actualLongitudinalLoadTransferN;
        float flTarget = frontAxleLoad * 0.5f - _actualFrontLateralLoadTransferN * 0.5f;
        float frTarget = frontAxleLoad * 0.5f + _actualFrontLateralLoadTransferN * 0.5f;
        float rlTarget = rearAxleLoad * 0.5f - _actualRearLateralLoadTransferN * 0.5f;
        float rrTarget = rearAxleLoad * 0.5f + _actualRearLateralLoadTransferN * 0.5f;

        if (!_engineParameters.ClassicFourWheel.ChassisLoadTransfer.Enabled)
        {
            _frontLeftSuspension = SuspensionCornerState.Static(flTarget);
            _frontRightSuspension = SuspensionCornerState.Static(frTarget);
            _rearLeftSuspension = SuspensionCornerState.Static(rlTarget);
            _rearRightSuspension = SuspensionCornerState.Static(rrTarget);
            PublishSuspensionState();
            return;
        }

        StepSuspensionCorner(
            ref _frontLeftSuspension,
            flTarget,
            staticFrontCornerLoad,
            _parameters.FrontSpringRateNPerM,
            _parameters.FrontBumpDampingNsPerM,
            _parameters.FrontReboundDampingNsPerM,
            _parameters.FrontSuspensionGeometry,
            dt);
        StepSuspensionCorner(
            ref _frontRightSuspension,
            frTarget,
            staticFrontCornerLoad,
            _parameters.FrontSpringRateNPerM,
            _parameters.FrontBumpDampingNsPerM,
            _parameters.FrontReboundDampingNsPerM,
            _parameters.FrontSuspensionGeometry,
            dt);
        StepSuspensionCorner(
            ref _rearLeftSuspension,
            rlTarget,
            staticRearCornerLoad,
            _parameters.RearSpringRateNPerM,
            _parameters.RearBumpDampingNsPerM,
            _parameters.RearReboundDampingNsPerM,
            _parameters.RearSuspensionGeometry,
            dt);
        StepSuspensionCorner(
            ref _rearRightSuspension,
            rrTarget,
            staticRearCornerLoad,
            _parameters.RearSpringRateNPerM,
            _parameters.RearBumpDampingNsPerM,
            _parameters.RearReboundDampingNsPerM,
            _parameters.RearSuspensionGeometry,
            dt);

        PreserveTotalSuspensionLoad(mass * Gravity);
        PublishSuspensionState();
    }

    private static void StepSuspensionCorner(
        ref SuspensionCornerState state,
        float targetLoadN,
        float staticLoadN,
        float springRateNPerM,
        float bumpDampingNsPerM,
        float reboundDampingNsPerM,
        SuspensionGeometryParameters geometry,
        float dt)
    {
        float safeDt = MathHelper.Clamp(dt, 0f, 1f / 20f);
        float springRate = MathF.Max(1000f, springRateNPerM);
        float cornerMass = MathF.Max(20f, staticLoadN / Gravity);
        float targetTravel = MathHelper.Clamp(
            (targetLoadN - staticLoadN) / springRate,
            -MathF.Max(0.01f, geometry.MaxDroopMeters),
            MathF.Max(0.01f, geometry.MaxCompressionMeters));
        float damping = state.VelocityMetersPerSecond >= 0f
            ? MathF.Max(0f, bumpDampingNsPerM)
            : MathF.Max(0f, reboundDampingNsPerM);
        float springForceError = springRate * (targetTravel - state.TravelMeters);
        float acceleration = (springForceError - damping * state.VelocityMetersPerSecond) / cornerMass;

        state.VelocityMetersPerSecond += acceleration * safeDt;
        state.TravelMeters += state.VelocityMetersPerSecond * safeDt;
        state.TravelMeters = MathHelper.Clamp(
            state.TravelMeters,
            -MathF.Max(0.01f, geometry.MaxDroopMeters),
            MathF.Max(0.01f, geometry.MaxCompressionMeters));
        if ((state.TravelMeters <= -MathF.Max(0.01f, geometry.MaxDroopMeters) && state.VelocityMetersPerSecond < 0f) ||
            (state.TravelMeters >= MathF.Max(0.01f, geometry.MaxCompressionMeters) && state.VelocityMetersPerSecond > 0f))
        {
            state.VelocityMetersPerSecond = 0f;
        }

        float springForce = springRate * state.TravelMeters;
        float damperForce = damping * state.VelocityMetersPerSecond;
        state.TargetLoadN = targetLoadN;
        state.SpringForceN = springForce;
        state.DamperForceN = damperForce;
        state.NormalLoadN = MathHelper.Clamp(staticLoadN + springForce + damperForce, 25f, staticLoadN * 3.0f);
    }

    private void PreserveTotalSuspensionLoad(float targetTotalLoadN)
    {
        float total =
            _frontLeftSuspension.NormalLoadN +
            _frontRightSuspension.NormalLoadN +
            _rearLeftSuspension.NormalLoadN +
            _rearRightSuspension.NormalLoadN;
        if (total <= 1f)
        {
            return;
        }

        float scale = targetTotalLoadN / total;
        _frontLeftSuspension.NormalLoadN *= scale;
        _frontRightSuspension.NormalLoadN *= scale;
        _rearLeftSuspension.NormalLoadN *= scale;
        _rearRightSuspension.NormalLoadN *= scale;
    }

    private void PublishSuspensionState()
    {
        State.FrontLeftSuspensionTravelMeters = _frontLeftSuspension.TravelMeters;
        State.FrontRightSuspensionTravelMeters = _frontRightSuspension.TravelMeters;
        State.RearLeftSuspensionTravelMeters = _rearLeftSuspension.TravelMeters;
        State.RearRightSuspensionTravelMeters = _rearRightSuspension.TravelMeters;
        State.FrontLeftSuspensionVelocityMetersPerSecond = _frontLeftSuspension.VelocityMetersPerSecond;
        State.FrontRightSuspensionVelocityMetersPerSecond = _frontRightSuspension.VelocityMetersPerSecond;
        State.RearLeftSuspensionVelocityMetersPerSecond = _rearLeftSuspension.VelocityMetersPerSecond;
        State.RearRightSuspensionVelocityMetersPerSecond = _rearRightSuspension.VelocityMetersPerSecond;
        State.FrontLeftSuspensionSpringForceN = _frontLeftSuspension.SpringForceN;
        State.FrontRightSuspensionSpringForceN = _frontRightSuspension.SpringForceN;
        State.RearLeftSuspensionSpringForceN = _rearLeftSuspension.SpringForceN;
        State.RearRightSuspensionSpringForceN = _rearRightSuspension.SpringForceN;
        State.FrontLeftSuspensionDamperForceN = _frontLeftSuspension.DamperForceN;
        State.FrontRightSuspensionDamperForceN = _frontRightSuspension.DamperForceN;
        State.RearLeftSuspensionDamperForceN = _rearLeftSuspension.DamperForceN;
        State.RearRightSuspensionDamperForceN = _rearRightSuspension.DamperForceN;
        State.FrontLeftSuspensionTargetLoadN = _frontLeftSuspension.TargetLoadN;
        State.FrontRightSuspensionTargetLoadN = _frontRightSuspension.TargetLoadN;
        State.RearLeftSuspensionTargetLoadN = _rearLeftSuspension.TargetLoadN;
        State.RearRightSuspensionTargetLoadN = _rearRightSuspension.TargetLoadN;
        State.FrontLeftSuspensionNormalLoadN = _frontLeftSuspension.NormalLoadN;
        State.FrontRightSuspensionNormalLoadN = _frontRightSuspension.NormalLoadN;
        State.RearLeftSuspensionNormalLoadN = _rearLeftSuspension.NormalLoadN;
        State.RearRightSuspensionNormalLoadN = _rearRightSuspension.NormalLoadN;
    }

    private void ApplyRestSleep(VehicleInput input, float throttle, float brake, float handbrake)
    {
        if (throttle > 0.001f ||
            input.Reverse > 0.001f ||
            State.SpeedMetersPerSecond > RestSleepSpeedMetersPerSecond ||
            MathF.Abs(State.YawRateRadiansPerSecond) > RestSleepYawRateRadiansPerSecond)
        {
            return;
        }

        State.Velocity = Vector2.Zero;
        State.YawRateRadiansPerSecond = 0f;
        State.SignedForwardSpeed = 0f;
        State.LateralSpeed = 0f;
        State.DisplayedSpeedMetersPerSecond = 0f;
        _previousForwardSpeed = 0f;
        _previousLateralSpeed = 0f;
        _frontLeftRelaxedLateralForceN = 0f;
        _frontRightRelaxedLateralForceN = 0f;
        _rearLeftRelaxedLateralForceN = 0f;
        _rearRightRelaxedLateralForceN = 0f;
        _frontLeftRelaxedLateralSlipRadians = 0f;
        _frontRightRelaxedLateralSlipRadians = 0f;
        _rearLeftRelaxedLateralSlipRadians = 0f;
        _rearRightRelaxedLateralSlipRadians = 0f;
        State.FrontLeftRelaxedLateralForceN = 0f;
        State.FrontRightRelaxedLateralForceN = 0f;
        State.RearLeftRelaxedLateralForceN = 0f;
        State.RearRightRelaxedLateralForceN = 0f;
        State.FrontLeftRelaxedLateralSlip = 0f;
        State.FrontRightRelaxedLateralSlip = 0f;
        State.RearLeftRelaxedLateralSlip = 0f;
        State.RearRightRelaxedLateralSlip = 0f;
        State.PeakRelaxedLateralSlip = 0f;
    }

    private void InitializeStaticSuspensionPresentation()
    {
        float frontBias = MathHelper.Clamp(_parameters.FrontWeightDistribution, 0.05f, 0.95f);
        float staticFrontCornerLoad = _parameters.MassKg * Gravity * frontBias * 0.5f;
        float staticRearCornerLoad = _parameters.MassKg * Gravity * (1f - frontBias) * 0.5f;

        _frontLeftSuspension = SuspensionCornerState.Static(staticFrontCornerLoad);
        _frontRightSuspension = SuspensionCornerState.Static(staticFrontCornerLoad);
        _rearLeftSuspension = SuspensionCornerState.Static(staticRearCornerLoad);
        _rearRightSuspension = SuspensionCornerState.Static(staticRearCornerLoad);
        PublishSuspensionState();

        State.FrontLeftVisualSuspensionCompressionMeters = CalculateVisualCompression(staticFrontCornerLoad, staticFrontCornerLoad, 0f);
        State.FrontRightVisualSuspensionCompressionMeters = CalculateVisualCompression(staticFrontCornerLoad, staticFrontCornerLoad, 0f);
        State.RearLeftVisualSuspensionCompressionMeters = CalculateVisualCompression(staticRearCornerLoad, staticRearCornerLoad, 0f);
        State.RearRightVisualSuspensionCompressionMeters = CalculateVisualCompression(staticRearCornerLoad, staticRearCornerLoad, 0f);
        UpdateGroundContactPresentation();
    }

    private static void StepSecondOrder(
        ref float value,
        ref float velocity,
        float target,
        float naturalFrequencyHz,
        float dampingRatio,
        float dt)
    {
        float safeDt = MathHelper.Clamp(dt, 0f, 1f / 20f);
        float omega = MathF.Tau * MathHelper.Clamp(naturalFrequencyHz, 0.1f, 20f);
        float damping = MathHelper.Clamp(dampingRatio, 0f, 4f);
        float acceleration = omega * omega * (target - value) - 2f * damping * omega * velocity;
        velocity += acceleration * safeDt;
        value += velocity * safeDt;
    }

    private static float CalculateAxleRollStiffness(float springRateNPerM, float trackMeters, float antiRollBarRateNmPerRad)
    {
        float safeTrack = MathF.Max(0.4f, trackMeters);
        return MathF.Max(0f, springRateNPerM) * safeTrack * safeTrack * 0.5f +
            MathF.Max(0f, antiRollBarRateNmPerRad);
    }

    private float ApplyBrakePressureRegulator(
        WheelInput wheel,
        float requestedNonServiceLongitudinalForce,
        float requestedServiceBrakeForce,
        float requestedLateralForce,
        float maxForce,
        float exponent,
        float wheelForwardSpeed,
        float dt,
        out bool active)
    {
        float pressure = GetBrakePressureRatio(wheel);
        active = false;
        if (DisableBrakePressureRegulatorForProbe)
        {
            SetBrakePressureRatio(wheel, 1f, false);
            return 1f;
        }

        if (MathF.Abs(requestedServiceBrakeForce) <= 0.1f ||
            MathF.Abs(wheelForwardSpeed) < _engineParameters.ClassicFourWheel.GripBudget.BrakePressureMinimumSpeedMetersPerSecond)
        {
            pressure = Approach(
                pressure,
                1f,
                MathF.Max(0.1f, _engineParameters.ClassicFourWheel.GripBudget.BrakePressureApplyRatePerSecond) * dt);
            SetBrakePressureRatio(wheel, pressure, false);
            return pressure;
        }

        ClassicBicycleGripBudgetParameters gripBudget = _engineParameters.ClassicFourWheel.GripBudget;
        float targetUsage = wheel.IsFront
            ? gripBudget.BrakePressureFrontTargetGripUsage
            : gripBudget.BrakePressureRearTargetGripUsage;
        targetUsage = MathHelper.Clamp(targetUsage, 0.35f, 0.99f);
        float usage = CalculateCombinedGripUsage(
            requestedNonServiceLongitudinalForce + requestedServiceBrakeForce * pressure,
            requestedLateralForce,
            maxForce,
            exponent);

        if (usage > targetUsage)
        {
            pressure -= MathF.Max(0.1f, gripBudget.BrakePressureReleaseRatePerSecond) * dt;
            active = true;
        }
        else
        {
            pressure += MathF.Max(0.1f, gripBudget.BrakePressureApplyRatePerSecond) * dt;
        }

        pressure = MathHelper.Clamp(
            pressure,
            MathHelper.Clamp(gripBudget.BrakePressureMinimumRatio, 0f, 1f),
            1f);
        SetBrakePressureRatio(wheel, pressure, active);
        return pressure;
    }

    private static float StepRelaxedLateralForce(
        ref float actualForceN,
        float targetForceN,
        float configuredRelaxationLengthMeters,
        float localForwardSpeedMetersPerSecond,
        float relaxationSpeedFloorMetersPerSecond,
        bool smoothRelaxationSpeedFloor,
        bool unwindBeforeSignChange,
        float dt,
        out float relaxationTimeSeconds,
        out float relaxationLengthMeters)
    {
        relaxationLengthMeters = MathF.Max(0f, configuredRelaxationLengthMeters);
        if (relaxationLengthMeters <= 0.001f || dt <= 0f)
        {
            actualForceN = targetForceN;
            relaxationTimeSeconds = 0f;
            return actualForceN;
        }

        float absoluteForwardSpeed = MathF.Abs(localForwardSpeedMetersPerSecond);
        float relaxationSpeedFloor = MathF.Max(0.1f, relaxationSpeedFloorMetersPerSecond);
        float relaxationSpeed = smoothRelaxationSpeedFloor
            ? MathF.Sqrt(absoluteForwardSpeed * absoluteForwardSpeed + relaxationSpeedFloor * relaxationSpeedFloor)
            : MathF.Max(relaxationSpeedFloor, absoluteForwardSpeed);
        relaxationTimeSeconds = relaxationLengthMeters / relaxationSpeed;
        float blend = 1f - MathF.Exp(-dt / MathF.Max(0.001f, relaxationTimeSeconds));
        float effectiveTargetForceN = targetForceN;
        if (unwindBeforeSignChange &&
            MathF.Abs(actualForceN) > 1f &&
            MathF.Abs(targetForceN) > 1f &&
            MathF.Sign(actualForceN) != MathF.Sign(targetForceN))
        {
            effectiveTargetForceN = 0f;
        }

        actualForceN += (effectiveTargetForceN - actualForceN) * MathHelper.Clamp(blend, 0f, 1f);

        if (!float.IsFinite(actualForceN))
        {
            actualForceN = targetForceN;
        }

        return actualForceN;
    }

    private static float StepRelaxedLateralSlip(
        ref float actualSlipRadians,
        float targetSlipRadians,
        float configuredRelaxationLengthMeters,
        float localForwardSpeedMetersPerSecond,
        float relaxationSpeedFloorMetersPerSecond,
        bool smoothRelaxationSpeedFloor,
        float dt,
        out float relaxationTimeSeconds)
    {
        float relaxationLengthMeters = MathF.Max(0f, configuredRelaxationLengthMeters);
        if (relaxationLengthMeters <= 0.001f || dt <= 0f)
        {
            actualSlipRadians = targetSlipRadians;
            relaxationTimeSeconds = 0f;
            return actualSlipRadians;
        }

        float absoluteForwardSpeed = MathF.Abs(localForwardSpeedMetersPerSecond);
        float relaxationSpeedFloor = MathF.Max(0.1f, relaxationSpeedFloorMetersPerSecond);
        float relaxationSpeed = smoothRelaxationSpeedFloor
            ? MathF.Sqrt(absoluteForwardSpeed * absoluteForwardSpeed + relaxationSpeedFloor * relaxationSpeedFloor)
            : MathF.Max(relaxationSpeedFloor, absoluteForwardSpeed);
        relaxationTimeSeconds = relaxationLengthMeters / relaxationSpeed;
        float blend = 1f - MathF.Exp(-dt / MathF.Max(0.001f, relaxationTimeSeconds));
        actualSlipRadians += (targetSlipRadians - actualSlipRadians) * MathHelper.Clamp(blend, 0f, 1f);

        if (!float.IsFinite(actualSlipRadians))
        {
            actualSlipRadians = targetSlipRadians;
        }

        return actualSlipRadians;
    }

    private static float StepRateLimitedLateralSlip(
        ref float actualSlipRadians,
        float targetSlipRadians,
        float localForwardSpeedMetersPerSecond,
        float maxLowSpeedSlipRateDegreesPerSecond,
        float fadeStartMetersPerSecond,
        float fadeEndMetersPerSecond,
        float dt)
    {
        if (dt <= 0f)
        {
            actualSlipRadians = targetSlipRadians;
            return actualSlipRadians;
        }

        float speed = MathF.Abs(localForwardSpeedMetersPerSecond);
        float start = MathF.Max(0f, fadeStartMetersPerSecond);
        float end = MathF.Max(start + 0.1f, fadeEndMetersPerSecond);
        float limitWeight = 1f - SmoothStep01((speed - start) / (end - start));
        if (limitWeight <= 0f)
        {
            actualSlipRadians = targetSlipRadians;
            return actualSlipRadians;
        }

        float maxRate = MathHelper.ToRadians(MathF.Max(1f, maxLowSpeedSlipRateDegreesPerSecond));
        float unrestrictedDelta = targetSlipRadians - actualSlipRadians;
        float maximumStep = maxRate * dt;
        float limitedTarget = actualSlipRadians + MathHelper.Clamp(unrestrictedDelta, -maximumStep, maximumStep);
        actualSlipRadians = MathHelper.Lerp(targetSlipRadians, limitedTarget, limitWeight);

        if (!float.IsFinite(actualSlipRadians))
        {
            actualSlipRadians = targetSlipRadians;
        }

        return actualSlipRadians;
    }

    private static float CalculateLowSpeedLateralForceScale(
        float localForwardSpeedMetersPerSecond,
        float localLateralSpeedMetersPerSecond,
        bool isFrontWheel,
        ClassicBicycleLowSpeedParameters lowSpeed)
    {
        _ = localLateralSpeedMetersPerSecond;
        float speed = MathF.Abs(localForwardSpeedMetersPerSecond);
        float rollingEnd = MathF.Max(0.25f, lowSpeed.RollingDominantEndMetersPerSecond);
        float dynamicEnd = MathF.Max(rollingEnd + 0.25f, lowSpeed.DynamicBlendEndMetersPerSecond);
        float rollingMaximum = isFrontWheel
            ? MathHelper.Clamp(lowSpeed.RollingDominantMaximumLateralScale, 0f, 0.6f)
            : MathHelper.Clamp(lowSpeed.RollingDominantRearLateralScale, 0f, 1f);
        float rollingBuild = SmoothStep01(speed / rollingEnd) * rollingMaximum;
        float dynamicBlend = SmoothStep01((speed - rollingEnd) / (dynamicEnd - rollingEnd));
        return MathHelper.Lerp(rollingBuild, 1f, dynamicBlend);
    }

    private static float ApplyLowSpeedRollingConstraint(
        float slipDerivedLateralForceN,
        WheelInput wheel,
        float localForwardSpeedMetersPerSecond,
        float wheelRightSpeedMetersPerSecond,
        float gripBudgetN,
        float mass,
        float yawInertia,
        float dt,
        ClassicBicycleLowSpeedParameters lowSpeed,
        out float rollingConstraintForceN,
        out float rollingWeight)
    {
        float rollingEnd = MathF.Max(0.25f, lowSpeed.RollingDominantEndMetersPerSecond);
        float dynamicEnd = MathF.Max(rollingEnd + 0.25f, lowSpeed.DynamicBlendEndMetersPerSecond);
        float rollingSpeed = MathF.Abs(localForwardSpeedMetersPerSecond);
        rollingWeight = 1f - SmoothStep01((rollingSpeed - rollingEnd) / (dynamicEnd - rollingEnd));
        rollingConstraintForceN = 0f;
        if (rollingWeight <= 0f || MathF.Abs(wheelRightSpeedMetersPerSecond) <= 0.001f)
        {
            return slipDerivedLateralForceN;
        }

        float maximumConstraintForce = MathF.Max(0f, gripBudgetN) *
            MathHelper.Clamp(lowSpeed.RollingConstraintGripFraction, 0f, 1f) *
            rollingWeight;
        float lateralSpeedScale = MathF.Max(0.05f, lowSpeed.RollingConstraintLateralSpeedMetersPerSecond);
        float normalizedLateralSpeed = MathHelper.Clamp(
            wheelRightSpeedMetersPerSecond / lateralSpeedScale,
            -4f,
            4f);
        rollingConstraintForceN = -MathF.Tanh(normalizedLateralSpeed) * maximumConstraintForce;

        return MathHelper.Lerp(
            slipDerivedLateralForceN,
            rollingConstraintForceN,
            rollingWeight);
    }

    private static float CalculateLowSpeedDiagnosticFade(
        float localForwardSpeedMetersPerSecond,
        float localLateralSpeedMetersPerSecond,
        float walkingSpeedMetersPerSecond)
    {
        float speed = MathF.Sqrt(
            localForwardSpeedMetersPerSecond * localForwardSpeedMetersPerSecond +
            localLateralSpeedMetersPerSecond * localLateralSpeedMetersPerSecond);
        float t = MathHelper.Clamp(speed / MathF.Max(0.5f, walkingSpeedMetersPerSecond), 0f, 1f);
        return 1f - t * t * (3f - 2f * t);
    }

    private void ApplyLowSpeedKinematicYawBlend(float forwardSpeedMetersPerSecond, float wheelbaseMeters, float dt)
    {
        ClassicLowSpeedForceDiagnosticOptions options = LowSpeedForceDiagnosticOptionsForProbe;
        float configuredBlend = MathHelper.Clamp(_engineParameters.ClassicFourWheel.LowSpeed.KinematicYawBlend, 0f, 1f);
        float diagnosticBlend = MathHelper.Clamp(options.KinematicYawBlend, 0f, 1f);
        float kinematicYawBlend = MathF.Max(configuredBlend, diagnosticBlend);
        if (kinematicYawBlend <= 0f || MathF.Abs(_currentSteerRadians) <= 0.0001f)
        {
            return;
        }

        float speed = MathF.Abs(forwardSpeedMetersPerSecond);
        float blendEndSpeed = MathF.Max(
            _engineParameters.ClassicFourWheel.LowSpeed.KinematicBlendEndSpeedMetersPerSecond,
            options.KinematicBlendEndSpeedMetersPerSecond);
        float t = MathHelper.Clamp(speed / MathF.Max(0.5f, blendEndSpeed), 0f, 1f);
        float lowSpeedWeight = 1f - t * t * (3f - 2f * t);
        float blend = MathHelper.Clamp(kinematicYawBlend * lowSpeedWeight, 0f, 1f);
        if (blend <= 0f)
        {
            return;
        }

        float expectedYawRate = forwardSpeedMetersPerSecond *
            MathF.Tan(_currentSteerRadians) /
            MathF.Max(0.25f, wheelbaseMeters);
        float guidedYawRate = MathHelper.Lerp(
            State.YawRateRadiansPerSecond,
            expectedYawRate,
            MathHelper.Clamp(blend, 0f, 1f));
        float accelerationLimit = MathHelper.ToRadians(MathF.Max(
            5f,
            _engineParameters.ClassicFourWheel.LowSpeed.KinematicYawAccelerationLimitDegreesPerSecondSquared));
        float maximumYawRateChange = accelerationLimit * MathHelper.Clamp(dt, 0f, 1f / 20f);
        State.YawRateRadiansPerSecond += MathHelper.Clamp(
            guidedYawRate - State.YawRateRadiansPerSecond,
            -maximumYawRateChange,
            maximumYawRateChange);
    }

    private void ApplyLowSpeedRollingContactConstraint(float wheelbaseMeters, float yawInertia, float dt)
    {
        State.FrontLeftLowSpeedRollingContactForceN = 0f;
        State.FrontRightLowSpeedRollingContactForceN = 0f;
        State.RearLeftLowSpeedRollingContactForceN = 0f;
        State.RearRightLowSpeedRollingContactForceN = 0f;
        State.FrontLeftLowSpeedRollingContactYawMomentNm = 0f;
        State.FrontRightLowSpeedRollingContactYawMomentNm = 0f;
        State.RearLeftLowSpeedRollingContactYawMomentNm = 0f;
        State.RearRightLowSpeedRollingContactYawMomentNm = 0f;

        ClassicBicycleLowSpeedParameters lowSpeed = _engineParameters.ClassicFourWheel.LowSpeed;
        float dynamicEnd = MathF.Max(
            lowSpeed.RollingDominantEndMetersPerSecond + 0.25f,
            lowSpeed.DynamicBlendEndMetersPerSecond);
        Vector2 forward = new(State.Forward.X, State.Forward.Z);
        Vector2 right = new(State.Right.X, State.Right.Z);
        float signedForwardSpeed = Vector2.Dot(State.Velocity, forward);
        float lateralSpeed = Vector2.Dot(State.Velocity, right);
        float speed = State.Velocity.Length();
        float lowSpeedWeight = 1f - SmoothStep01(speed / MathF.Max(0.5f, dynamicEnd));
        if (lowSpeedWeight <= 0f || !LowSpeedForceDiagnosticOptionsForProbe.EnablePostForceRollingContactConstraint)
        {
            return;
        }

        float frontBias = MathHelper.Clamp(_parameters.FrontWeightDistribution, 0.05f, 0.95f);
        float rearLocalForwardMeters = -wheelbaseMeters * frontBias;
        float frontLocalForwardMeters = wheelbaseMeters * (1f - frontBias);
        float halfFrontTrack = MathF.Max(0.2f, _parameters.FrontTrackMeters * 0.5f);
        float halfRearTrack = MathF.Max(0.2f, _parameters.RearTrackMeters * 0.5f);
        float constraintGripFraction = MathHelper.Clamp(lowSpeed.RollingConstraintGripFraction, 0f, 1f);
        float mass = MathF.Max(1f, _parameters.MassKg);
        float effectiveYawInertia = MathF.Max(1f, yawInertia);
        float impulseScale = MathHelper.Clamp(0.92f * lowSpeedWeight, 0f, 1f);
        float staticFrontLoad = mass * Gravity * MathHelper.Clamp(_parameters.FrontWeightDistribution, 0.05f, 0.95f) * 0.5f;
        float staticRearLoad = mass * Gravity * (1f - MathHelper.Clamp(_parameters.FrontWeightDistribution, 0.05f, 0.95f)) * 0.5f;
        float yawRate = State.YawRateRadiansPerSecond;
        float flContactForce = 0f;
        float frContactForce = 0f;
        float rlContactForce = 0f;
        float rrContactForce = 0f;
        float flContactYawMoment = 0f;
        float frContactYawMoment = 0f;
        float rlContactYawMoment = 0f;
        float rrContactYawMoment = 0f;

        for (int iteration = 0; iteration < 4; iteration++)
        {
            float flImpulse = ApplyWheelRollingConstraintImpulse(
                ref signedForwardSpeed,
                ref lateralSpeed,
                ref yawRate,
                -halfFrontTrack,
                frontLocalForwardMeters,
                _currentSteerRadians,
                staticFrontLoad,
                mass,
                effectiveYawInertia,
                constraintGripFraction,
                impulseScale,
                dt);
            AddRollingContactTelemetry(
                ref flContactForce,
                ref flContactYawMoment,
                flImpulse,
                -halfFrontTrack,
                frontLocalForwardMeters,
                _currentSteerRadians,
                dt);
            float frImpulse = ApplyWheelRollingConstraintImpulse(
                ref signedForwardSpeed,
                ref lateralSpeed,
                ref yawRate,
                halfFrontTrack,
                frontLocalForwardMeters,
                _currentSteerRadians,
                staticFrontLoad,
                mass,
                effectiveYawInertia,
                constraintGripFraction,
                impulseScale,
                dt);
            AddRollingContactTelemetry(
                ref frContactForce,
                ref frContactYawMoment,
                frImpulse,
                halfFrontTrack,
                frontLocalForwardMeters,
                _currentSteerRadians,
                dt);
            float rlImpulse = ApplyWheelRollingConstraintImpulse(
                ref signedForwardSpeed,
                ref lateralSpeed,
                ref yawRate,
                -halfRearTrack,
                rearLocalForwardMeters,
                0f,
                staticRearLoad,
                mass,
                effectiveYawInertia,
                constraintGripFraction,
                impulseScale,
                dt);
            AddRollingContactTelemetry(
                ref rlContactForce,
                ref rlContactYawMoment,
                rlImpulse,
                -halfRearTrack,
                rearLocalForwardMeters,
                0f,
                dt);
            float rrImpulse = ApplyWheelRollingConstraintImpulse(
                ref signedForwardSpeed,
                ref lateralSpeed,
                ref yawRate,
                halfRearTrack,
                rearLocalForwardMeters,
                0f,
                staticRearLoad,
                mass,
                effectiveYawInertia,
                constraintGripFraction,
                impulseScale,
                dt);
            AddRollingContactTelemetry(
                ref rrContactForce,
                ref rrContactYawMoment,
                rrImpulse,
                halfRearTrack,
                rearLocalForwardMeters,
                0f,
                dt);
        }

        State.FrontLeftLowSpeedRollingContactForceN = flContactForce;
        State.FrontRightLowSpeedRollingContactForceN = frContactForce;
        State.RearLeftLowSpeedRollingContactForceN = rlContactForce;
        State.RearRightLowSpeedRollingContactForceN = rrContactForce;
        State.FrontLeftLowSpeedRollingContactYawMomentNm = flContactYawMoment;
        State.FrontRightLowSpeedRollingContactYawMomentNm = frContactYawMoment;
        State.RearLeftLowSpeedRollingContactYawMomentNm = rlContactYawMoment;
        State.RearRightLowSpeedRollingContactYawMomentNm = rrContactYawMoment;
        State.YawRateRadiansPerSecond = yawRate;
        State.Velocity = forward * signedForwardSpeed + right * lateralSpeed;
    }

    private static float ApplyWheelRollingConstraintImpulse(
        ref float bodyForwardSpeed,
        ref float bodyLateralSpeed,
        ref float yawRate,
        float localRightMeters,
        float localForwardMeters,
        float steerRadians,
        float normalLoadN,
        float mass,
        float yawInertia,
        float gripFraction,
        float impulseScale,
        float dt)
    {
        float sin = MathF.Sin(steerRadians);
        float cos = MathF.Cos(steerRadians);
        float localForwardSpeed = bodyForwardSpeed - yawRate * localRightMeters;
        float localLateralSpeed = bodyLateralSpeed + yawRate * localForwardMeters;
        float wheelRightSpeed = localLateralSpeed * cos - localForwardSpeed * sin;
        if (MathF.Abs(wheelRightSpeed) <= 0.0001f)
        {
            return 0f;
        }

        float momentArm = localForwardMeters * cos + localRightMeters * sin;
        float inverseEffectiveMass = 1f / mass + momentArm * momentArm / yawInertia;
        float impulse = -wheelRightSpeed / MathF.Max(0.0001f, inverseEffectiveMass);
        float maximumImpulse = MathF.Max(0f, normalLoadN) * MathF.Max(0f, gripFraction) * MathF.Max(0f, dt);
        impulse = MathHelper.Clamp(impulse, -maximumImpulse, maximumImpulse) * impulseScale;

        bodyForwardSpeed += impulse * -sin / mass;
        bodyLateralSpeed += impulse * cos / mass;
        yawRate += impulse * momentArm / yawInertia;
        return impulse;
    }

    private static void AddRollingContactTelemetry(
        ref float forceN,
        ref float yawMomentNm,
        float impulseNs,
        float localRightMeters,
        float localForwardMeters,
        float steerRadians,
        float dt)
    {
        if (dt <= 0f || MathF.Abs(impulseNs) <= 0f)
        {
            return;
        }

        float equivalentForceN = impulseNs / dt;
        float sin = MathF.Sin(steerRadians);
        float cos = MathF.Cos(steerRadians);
        float momentArm = localForwardMeters * cos + localRightMeters * sin;
        forceN += equivalentForceN;
        yawMomentNm += equivalentForceN * momentArm;
    }

    private float GetBrakePressureRatio(WheelInput wheel)
    {
        return wheel.Name switch
        {
            "FL" => _frontLeftBrakePressureRatio,
            "FR" => _frontRightBrakePressureRatio,
            "RL" => _rearLeftBrakePressureRatio,
            "RR" => _rearRightBrakePressureRatio,
            _ => 1f
        };
    }

    private void SetBrakePressureRatio(WheelInput wheel, float pressureRatio, bool active)
    {
        switch (wheel.Name)
        {
            case "FL":
                _frontLeftBrakePressureRatio = pressureRatio;
                State.FrontLeftBrakePressureRatio = pressureRatio;
                State.FrontLeftBrakePressureRegulatorActive = active;
                break;
            case "FR":
                _frontRightBrakePressureRatio = pressureRatio;
                State.FrontRightBrakePressureRatio = pressureRatio;
                State.FrontRightBrakePressureRegulatorActive = active;
                break;
            case "RL":
                _rearLeftBrakePressureRatio = pressureRatio;
                State.RearLeftBrakePressureRatio = pressureRatio;
                State.RearLeftBrakePressureRegulatorActive = active;
                break;
            case "RR":
                _rearRightBrakePressureRatio = pressureRatio;
                State.RearRightBrakePressureRatio = pressureRatio;
                State.RearRightBrakePressureRegulatorActive = active;
                break;
        }

        State.AbsActive =
            State.FrontLeftBrakePressureRegulatorActive ||
            State.FrontRightBrakePressureRegulatorActive ||
            State.RearLeftBrakePressureRegulatorActive ||
            State.RearRightBrakePressureRegulatorActive;
    }

    private float ResetYawRecoveryTelemetry()
    {
        _hasYawRecoveryBetaSample = false;
        State.ClassicYawRecoveryDesiredYawRateDegreesPerSecond = 0f;
        State.ClassicYawRecoveryYawErrorDegreesPerSecond = 0f;
        State.ClassicYawRecoveryActivation = 0f;
        State.ClassicYawRecoveryBetaGate = 0f;
        State.ClassicYawRecoveryBetaDotGate = 0f;
        State.ClassicYawRecoveryYawExcessGate = 0f;
        State.ClassicYawRecoveryRearSlipGate = 0f;
        State.ClassicYawRecoveryDriverIntentFactor = 1f;
        return 0f;
    }

    private float CalculateYawRecoveryAcceleration(float speedMetersPerSecond, float wheelbase, ClassicBicycleParameters classic, float dt)
    {
        if (speedMetersPerSecond <= 0.5f)
        {
            return ResetYawRecoveryTelemetry();
        }

        float steerRadians = MathHelper.Clamp(_currentSteerRadians, MathHelper.ToRadians(-32f), MathHelper.ToRadians(32f));

        if (UseLegacyYawRecoveryForProbe)
        {
            float legacyDesiredYawRate = -speedMetersPerSecond / MathF.Max(0.1f, wheelbase) * MathF.Tan(steerRadians) * 0.34f;
            float legacySpeedGate = SmoothStep01((speedMetersPerSecond - 2f) / 10f);
            float steeringReleaseGate = 1f - SmoothStep01(MathF.Abs(steerRadians) / MathHelper.ToRadians(7.5f));
            float overRotation = MathF.Abs(State.YawRateRadiansPerSecond) - MathF.Abs(legacyDesiredYawRate);
            float overRotationGate = SmoothStep01(overRotation / MathHelper.ToRadians(28f));
            float responseShape = MathHelper.Lerp(0.85f, 1.55f, overRotationGate);
            float releaseAssist = MathHelper.Lerp(0f, 0.35f, steeringReleaseGate);
            float legacyResponse = MathF.Max(0f, classic.Yaw.Damping) * (responseShape + releaseAssist);
            float acceleration = (legacyDesiredYawRate - State.YawRateRadiansPerSecond) * legacyResponse * legacySpeedGate;

            State.ClassicYawRecoveryDesiredYawRateDegreesPerSecond = MathHelper.ToDegrees(legacyDesiredYawRate);
            State.ClassicYawRecoveryYawErrorDegreesPerSecond = MathHelper.ToDegrees(legacyDesiredYawRate - State.YawRateRadiansPerSecond);
            State.ClassicYawRecoveryActivation = legacySpeedGate;
            State.ClassicYawRecoveryBetaGate = 0f;
            State.ClassicYawRecoveryBetaDotGate = 0f;
            State.ClassicYawRecoveryYawExcessGate = overRotationGate;
            State.ClassicYawRecoveryRearSlipGate = 0f;
            State.ClassicYawRecoveryDriverIntentFactor = 1f;
            return acceleration;
        }

        Vector2 forward = new(State.Forward.X, State.Forward.Z);
        Vector2 right = new(State.Right.X, State.Right.Z);
        float forwardSpeed = Vector2.Dot(State.Velocity, forward);
        float lateralSpeed = Vector2.Dot(State.Velocity, right);
        float betaRadians = MathF.Atan2(lateralSpeed, MathF.Max(2f, MathF.Abs(forwardSpeed)));
        float betaDotRadians = _hasYawRecoveryBetaSample && dt > 0f
            ? (betaRadians - _previousYawRecoveryBetaRadians) / dt
            : 0f;
        _previousYawRecoveryBetaRadians = betaRadians;
        _hasYawRecoveryBetaSample = true;

        float desiredYawRate = speedMetersPerSecond / MathF.Max(0.1f, wheelbase) * MathF.Tan(steerRadians);
        float speedGate = SmoothStep01((speedMetersPerSecond - 2f) / 10f);
        float absBetaDegrees = MathF.Abs(MathHelper.ToDegrees(betaRadians));
        float betaGate = SmoothStep01((absBetaDegrees - 3f) / 3f);
        float betaLargeGate = SmoothStep01((absBetaDegrees - 6f) / 4f);

        float betaDotDegrees = MathHelper.ToDegrees(betaDotRadians);
        bool betaDiverging = MathF.Abs(betaRadians) > MathHelper.ToRadians(2f) &&
            MathF.Sign(betaRadians) == MathF.Sign(betaDotRadians);
        float betaDotGate = betaDiverging
            ? SmoothStep01((MathF.Abs(betaDotDegrees) - 8f) / 22f)
            : 0f;

        float absDesiredYawRate = MathF.Abs(desiredYawRate);
        float yawLimit = absDesiredYawRate * 1.35f + MathHelper.ToRadians(8f);
        float yawExcess = MathF.Abs(State.YawRateRadiansPerSecond) - yawLimit;
        float yawExcessGate = SmoothStep01(yawExcess / MathHelper.ToRadians(28f));

        float rearSlipDegrees = (
            MathF.Abs(State.RearLeftSlipAngleDegrees) +
            MathF.Abs(State.RearRightSlipAngleDegrees)) * 0.5f;
        float rearSlipGate = SmoothStep01((rearSlipDegrees - 8f) / 6f);

        float runawayGate = MathF.Max(betaDotGate, MathF.Max(yawExcessGate, rearSlipGate * 0.55f));
        float activation = speedGate * MathF.Max(
            betaGate * MathHelper.Lerp(0.35f, 1f, runawayGate),
            betaLargeGate * 0.60f);

        float driverIntentFactor = 1f;
        float absSteer = MathF.Abs(steerRadians);
        float absYawRate = MathF.Abs(State.YawRateRadiansPerSecond);
        if (absSteer > MathHelper.ToRadians(1.5f) && absYawRate > MathHelper.ToRadians(3f))
        {
            bool counterSteering = MathF.Sign(steerRadians) == -MathF.Sign(State.YawRateRadiansPerSecond);
            bool addingRotation = MathF.Sign(steerRadians) == MathF.Sign(State.YawRateRadiansPerSecond) &&
                (betaDiverging || yawExcessGate > 0.05f);

            if (counterSteering)
            {
                driverIntentFactor = MathHelper.Lerp(1f, 0.22f, SmoothStep01(absSteer / MathHelper.ToRadians(10f)));
            }
            else if (addingRotation)
            {
                driverIntentFactor = 1.15f;
            }
        }

        float yawError = desiredYawRate - State.YawRateRadiansPerSecond;
        float response = MathF.Max(0f, classic.Yaw.Damping) * 2.35f;
        float recoveryCommand = -State.YawRateRadiansPerSecond;
        float recoveryAcceleration = recoveryCommand * response * activation * driverIntentFactor;

        State.ClassicYawRecoveryDesiredYawRateDegreesPerSecond = MathHelper.ToDegrees(desiredYawRate);
        State.ClassicYawRecoveryYawErrorDegreesPerSecond = MathHelper.ToDegrees(yawError);
        State.ClassicYawRecoveryActivation = activation;
        State.ClassicYawRecoveryBetaGate = betaGate;
        State.ClassicYawRecoveryBetaDotGate = betaDotGate;
        State.ClassicYawRecoveryYawExcessGate = yawExcessGate;
        State.ClassicYawRecoveryRearSlipGate = rearSlipGate;
        State.ClassicYawRecoveryDriverIntentFactor = driverIntentFactor;
        return recoveryAcceleration;
    }

    private float CalculateRearFollowAcceleration(
        float forwardSpeed,
        float lateralSpeed,
        float rearDistance,
        float yawInertia,
        float actualRearLateralForceN,
        float rearGripBudgetN,
        ClassicBicycleTyreParameters rearTyres,
        ClassicBicycleParameters classic)
    {
        return CalculateRearFollowAcceleration(
            forwardSpeed,
            lateralSpeed,
            rearDistance,
            yawInertia,
            actualRearLateralForceN,
            rearGripBudgetN,
            rearTyres,
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
        ClassicBicycleTyreParameters rearTyres,
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
        float slipDenominator = WheelKinematics.EffectiveSlipSpeed(forwardSpeed, classic.LowSpeed.SlipSpeedFloorMetersPerSecond);
        float rearSlipRadians = -MathF.Atan2(rearAxleLateralSpeed, slipDenominator);
        float absRearSlipDegrees = MathF.Abs(MathHelper.ToDegrees(rearSlipRadians));
        float slipGate = SmoothStep01((absRearSlipDegrees - rearTyres.PeakSlipAngleDegrees) /
            MathF.Max(0.1f, rearTyres.FalloffSlipAngleDegrees - rearTyres.PeakSlipAngleDegrees));
        if (slipGate <= 0f)
        {
            return 0f;
        }

        float maxForce = MathF.Max(1f, rearGripBudgetN);
        float expectedTrackingForce = CalculateDiagnosticTyreLateralForce(rearSlipRadians, maxForce, rearTyres);
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
        float slipGate = CalculateBodySlipDampingGate(bodySlipDegrees);
        float speedGate = SmoothStep01((speed - 6f) / 18f);
        float dampingRate = BodySlipDampingRateCeiling * slipGate * speedGate;
        float centeredRackGate = 1f - SmoothStep01(MathF.Abs(MathHelper.ToDegrees(steerRadians)) / 6f);
        float settleGate = CalculateBodySlipSettleGate(bodySlipDegrees) * centeredRackGate * speedGate;
        dampingRate += BodySlipSettleRateCeiling * settleGate;
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
            CalculateRearSlipSettleBodyGate(bodySlipDegrees) *
            highSteerGate *
            speedGate;
        dampingRate += RearSlipSettleRateCeiling * rearSlipSettleGate;
        return lateralSpeed * mass * dampingRate;
    }

    public static float CalculateBodySlipDampingGate(float bodySlipDegrees)
    {
        return SmoothStep01((bodySlipDegrees - BodySlipDampingStartDegrees) /
            MathF.Max(0.1f, BodySlipDampingEndDegrees - BodySlipDampingStartDegrees));
    }

    public static float CalculateBodySlipSettleGate(float bodySlipDegrees)
    {
        return SmoothStep01((bodySlipDegrees - BodySlipSettleStartDegrees) /
            MathF.Max(0.1f, BodySlipSettleEndDegrees - BodySlipSettleStartDegrees));
    }

    public static float CalculateRearSlipSettleBodyGate(float bodySlipDegrees)
    {
        return SmoothStep01((bodySlipDegrees - RearSlipSettleStartDegrees) /
            MathF.Max(0.1f, RearSlipSettleEndDegrees - RearSlipSettleStartDegrees));
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
        float transferLongitudinalAcceleration = CalculateEquivalentLongitudinalTransferAcceleration(
            _parameters.MassKg,
            _parameters.WheelbaseMeters);
        float transferLateralAcceleration = CalculateEquivalentLateralTransferAcceleration(
            _parameters.MassKg,
            MathHelper.Clamp(_parameters.FrontWeightDistribution, 0.05f, 0.95f),
            VehicleAxleGeometry.FromParameters(_parameters).FrontTrackMeters,
            VehicleAxleGeometry.FromParameters(_parameters).RearTrackMeters);
        float targetRoll = -MathHelper.Clamp(transferLateralAcceleration / Gravity, -1.15f, 1.15f) * 0.022f;
        float targetPitch = -MathHelper.Clamp(transferLongitudinalAcceleration / Gravity, -1.25f, 1.25f) * 0.020f;
        float blend = MathHelper.Clamp(1f - MathF.Exp(-13f * clampedDt), 0f, 1f);
        _visualBodyRollRadians = MathHelper.Lerp(_visualBodyRollRadians, targetRoll, blend);
        _visualBodyPitchRadians = MathHelper.Lerp(_visualBodyPitchRadians, targetPitch, blend);

        State.GroundRollRadians = 0f;
        State.GroundPitchRadians = 0f;
        State.BodyRollRadians = MathHelper.Clamp(_visualBodyRollRadians, MathHelper.ToRadians(-1.45f), MathHelper.ToRadians(1.45f));
        State.BodyPitchRadians = MathHelper.Clamp(_visualBodyPitchRadians, MathHelper.ToRadians(-1.45f), MathHelper.ToRadians(1.45f));

        float staticFrontCornerLoad = _parameters.MassKg * Gravity * MathHelper.Clamp(_parameters.FrontWeightDistribution, 0.05f, 0.95f) * 0.5f;
        float staticRearCornerLoad = _parameters.MassKg * Gravity * (1f - MathHelper.Clamp(_parameters.FrontWeightDistribution, 0.05f, 0.95f)) * 0.5f;
        State.FrontLeftVisualSuspensionCompressionMeters = CalculateVisualCompression(frontLeftLoadN, staticFrontCornerLoad, State.FrontLeftSuspensionTravelMeters);
        State.FrontRightVisualSuspensionCompressionMeters = CalculateVisualCompression(frontRightLoadN, staticFrontCornerLoad, State.FrontRightSuspensionTravelMeters);
        State.RearLeftVisualSuspensionCompressionMeters = CalculateVisualCompression(rearLeftLoadN, staticRearCornerLoad, State.RearLeftSuspensionTravelMeters);
        State.RearRightVisualSuspensionCompressionMeters = CalculateVisualCompression(rearRightLoadN, staticRearCornerLoad, State.RearRightSuspensionTravelMeters);
        UpdateGroundContactPresentation();
    }

    private void UpdateGroundContactPresentation()
    {
        VehicleAxleGeometry geometry = VehicleAxleGeometry.FromParameters(_parameters);
        Vector2 center = new(State.Position.X, State.Position.Z);
        Vector2 forward = new(State.Forward.X, State.Forward.Z);
        Vector2 right = new(State.Right.X, State.Right.Z);
        if (forward.LengthSquared() <= 0.0001f || right.LengthSquared() <= 0.0001f)
        {
            float headingSin = MathF.Sin(State.HeadingRadians);
            float headingCos = MathF.Cos(State.HeadingRadians);
            forward = new Vector2(headingSin, headingCos);
            right = new Vector2(headingCos, -headingSin);
        }

        float frontHalfTrack = geometry.FrontTrackMeters * 0.5f;
        float rearHalfTrack = geometry.RearTrackMeters * 0.5f;
        float flGround = _surfaceSampler.GetElevation(center - right * frontHalfTrack + forward * geometry.CgToFrontAxleMeters);
        float frGround = _surfaceSampler.GetElevation(center + right * frontHalfTrack + forward * geometry.CgToFrontAxleMeters);
        float rlGround = _surfaceSampler.GetElevation(center - right * rearHalfTrack - forward * geometry.CgToRearAxleMeters);
        float rrGround = _surfaceSampler.GetElevation(center + right * rearHalfTrack - forward * geometry.CgToRearAxleMeters);

        float flSupport = flGround - State.FrontLeftVisualSuspensionCompressionMeters;
        float frSupport = frGround - State.FrontRightVisualSuspensionCompressionMeters;
        float rlSupport = rlGround - State.RearLeftVisualSuspensionCompressionMeters;
        float rrSupport = rrGround - State.RearRightVisualSuspensionCompressionMeters;
        float groundCenterHeight = Average(flGround, frGround, rlGround, rrGround);
        float supportCenterHeight = Average(flSupport, frSupport, rlSupport, rrSupport);
        float groundFrontHeight = Average(flGround, frGround);
        float groundRearHeight = Average(rlGround, rrGround);
        float supportFrontHeight = Average(flSupport, frSupport);
        float supportRearHeight = Average(rlSupport, rrSupport);
        float frontRollWeight = MathHelper.Clamp(_parameters.FrontWeightDistribution, 0.05f, 0.95f);
        float groundPitch = -MathF.Atan2(groundFrontHeight - groundRearHeight, geometry.WheelbaseMeters);
        float supportPitch = -MathF.Atan2(supportFrontHeight - supportRearHeight, geometry.WheelbaseMeters);
        float groundRoll = MathHelper.Lerp(
            MathF.Atan2(rlGround - rrGround, MathF.Max(0.1f, geometry.RearTrackMeters)),
            MathF.Atan2(flGround - frGround, MathF.Max(0.1f, geometry.FrontTrackMeters)),
            frontRollWeight);
        float supportRoll = MathHelper.Lerp(
            MathF.Atan2(rlSupport - rrSupport, MathF.Max(0.1f, geometry.RearTrackMeters)),
            MathF.Atan2(flSupport - frSupport, MathF.Max(0.1f, geometry.FrontTrackMeters)),
            frontRollWeight);

        State.Position = new Vector3(State.Position.X, supportCenterHeight, State.Position.Z);
        State.WheelContactCenterHeightMeters = groundCenterHeight;
        State.GroundPitchRadians = MathHelper.Clamp(groundPitch, -0.18f, 0.18f);
        State.GroundRollRadians = MathHelper.Clamp(groundRoll, -0.14f, 0.14f);
        State.BodyPitchRadians = State.GroundPitchRadians + MathHelper.Clamp(
            State.BodyPitchRadians + supportPitch - State.GroundPitchRadians,
            MathHelper.ToRadians(-1.45f),
            MathHelper.ToRadians(1.45f));
        State.BodyRollRadians = State.GroundRollRadians + MathHelper.Clamp(
            State.BodyRollRadians + supportRoll - State.GroundRollRadians,
            MathHelper.ToRadians(-1.45f),
            MathHelper.ToRadians(1.45f));
        State.FrontLeftSupportHeightMeters = flSupport;
        State.FrontRightSupportHeightMeters = frSupport;
        State.RearLeftSupportHeightMeters = rlSupport;
        State.RearRightSupportHeightMeters = rrSupport;
    }

    private static float CalculateVisualCompression(float loadN, float staticLoadN, float suspensionTravelMeters)
    {
        float loadDelta = (loadN - MathF.Max(1f, staticLoadN)) / MathF.Max(1f, staticLoadN);
        return MathHelper.Clamp(0.045f + loadDelta * 0.012f + suspensionTravelMeters * 0.55f, 0.005f, 0.095f);
    }

    private float CalculateEquivalentLongitudinalTransferAcceleration(float mass, float wheelbase)
    {
        float cgHeight = MathHelper.Clamp(_parameters.CenterOfGravityHeightMeters, 0.05f, 1.5f);
        return -_actualLongitudinalLoadTransferN *
            MathF.Max(0.1f, wheelbase) /
            (MathF.Max(1f, mass) * cgHeight);
    }

    private float CalculateEquivalentLateralTransferAcceleration(float mass, float frontBias, float frontTrack, float rearTrack)
    {
        float cgHeight = MathHelper.Clamp(_parameters.CenterOfGravityHeightMeters, 0.05f, 1.5f);
        float denominator = MathF.Max(1f, mass) * cgHeight * (
            frontBias / MathF.Max(0.1f, frontTrack) +
            (1f - frontBias) / MathF.Max(0.1f, rearTrack));
        return (_actualFrontLateralLoadTransferN + _actualRearLateralLoadTransferN) / denominator;
    }

    private static float Average(float a, float b)
    {
        return (a + b) * 0.5f;
    }

    private static float Average(float a, float b, float c, float d)
    {
        return (a + b + c + d) * 0.25f;
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
        State.FrontLeftLocalForwardSpeedMetersPerSecond = fl.LocalForwardSpeedMetersPerSecond;
        State.FrontRightLocalForwardSpeedMetersPerSecond = fr.LocalForwardSpeedMetersPerSecond;
        State.RearLeftLocalForwardSpeedMetersPerSecond = rl.LocalForwardSpeedMetersPerSecond;
        State.RearRightLocalForwardSpeedMetersPerSecond = rr.LocalForwardSpeedMetersPerSecond;
        State.FrontLeftLocalLateralSpeedMetersPerSecond = fl.LocalLateralSpeedMetersPerSecond;
        State.FrontRightLocalLateralSpeedMetersPerSecond = fr.LocalLateralSpeedMetersPerSecond;
        State.RearLeftLocalLateralSpeedMetersPerSecond = rl.LocalLateralSpeedMetersPerSecond;
        State.RearRightLocalLateralSpeedMetersPerSecond = rr.LocalLateralSpeedMetersPerSecond;
        State.FrontLeftYawLateralContributionMetersPerSecond = fl.YawLateralContributionMetersPerSecond;
        State.FrontRightYawLateralContributionMetersPerSecond = fr.YawLateralContributionMetersPerSecond;
        State.RearLeftYawLateralContributionMetersPerSecond = rl.YawLateralContributionMetersPerSecond;
        State.RearRightYawLateralContributionMetersPerSecond = rr.YawLateralContributionMetersPerSecond;
        State.FrontLeftRequestedLongitudinalForceN = fl.RequestedLongitudinalForceN;
        State.FrontRightRequestedLongitudinalForceN = fr.RequestedLongitudinalForceN;
        State.RearLeftRequestedLongitudinalForceN = rl.RequestedLongitudinalForceN;
        State.RearRightRequestedLongitudinalForceN = rr.RequestedLongitudinalForceN;
        State.FrontLeftRequestedLateralForceN = fl.RequestedLateralForceN;
        State.FrontRightRequestedLateralForceN = fr.RequestedLateralForceN;
        State.RearLeftRequestedLateralForceN = rl.RequestedLateralForceN;
        State.RearRightRequestedLateralForceN = rr.RequestedLateralForceN;
        State.FrontLeftLowSpeedSlipLateralForceN = fl.LowSpeedSlipLateralForceN;
        State.FrontRightLowSpeedSlipLateralForceN = fr.LowSpeedSlipLateralForceN;
        State.RearLeftLowSpeedSlipLateralForceN = rl.LowSpeedSlipLateralForceN;
        State.RearRightLowSpeedSlipLateralForceN = rr.LowSpeedSlipLateralForceN;
        State.FrontLeftLowSpeedRollingConstraintForceN = fl.LowSpeedRollingConstraintForceN;
        State.FrontRightLowSpeedRollingConstraintForceN = fr.LowSpeedRollingConstraintForceN;
        State.RearLeftLowSpeedRollingConstraintForceN = rl.LowSpeedRollingConstraintForceN;
        State.RearRightLowSpeedRollingConstraintForceN = rr.LowSpeedRollingConstraintForceN;
        State.FrontLeftLowSpeedRollingBlend = fl.LowSpeedRollingBlend;
        State.FrontRightLowSpeedRollingBlend = fr.LowSpeedRollingBlend;
        State.RearLeftLowSpeedRollingBlend = rl.LowSpeedRollingBlend;
        State.RearRightLowSpeedRollingBlend = rr.LowSpeedRollingBlend;
        State.FrontLeftLowSpeedFinalLateralForceN = fl.WheelLateralForceN;
        State.FrontRightLowSpeedFinalLateralForceN = fr.WheelLateralForceN;
        State.RearLeftLowSpeedFinalLateralForceN = rl.WheelLateralForceN;
        State.RearRightLowSpeedFinalLateralForceN = rr.WheelLateralForceN;
        State.FrontLeftRelaxedLateralForceN = fl.RelaxedRequestedLateralForceN;
        State.FrontRightRelaxedLateralForceN = fr.RelaxedRequestedLateralForceN;
        State.RearLeftRelaxedLateralForceN = rl.RelaxedRequestedLateralForceN;
        State.RearRightRelaxedLateralForceN = rr.RelaxedRequestedLateralForceN;
        State.FrontLeftRelaxedLateralSlip = fl.RelaxedLateralSlipRadians;
        State.FrontRightRelaxedLateralSlip = fr.RelaxedLateralSlipRadians;
        State.RearLeftRelaxedLateralSlip = rl.RelaxedLateralSlipRadians;
        State.RearRightRelaxedLateralSlip = rr.RelaxedLateralSlipRadians;
        State.PeakRelaxedLateralSlip = MathF.Max(
            MathF.Max(MathF.Abs(fl.RelaxedLateralSlipRadians), MathF.Abs(fr.RelaxedLateralSlipRadians)),
            MathF.Max(MathF.Abs(rl.RelaxedLateralSlipRadians), MathF.Abs(rr.RelaxedLateralSlipRadians)));
        State.FrontLeftLateralRelaxationDeltaN = fl.LateralRelaxationDeltaN;
        State.FrontRightLateralRelaxationDeltaN = fr.LateralRelaxationDeltaN;
        State.RearLeftLateralRelaxationDeltaN = rl.LateralRelaxationDeltaN;
        State.RearRightLateralRelaxationDeltaN = rr.LateralRelaxationDeltaN;
        State.FrontLeftLateralRelaxationTimeSeconds = fl.LateralRelaxationTimeSeconds;
        State.FrontRightLateralRelaxationTimeSeconds = fr.LateralRelaxationTimeSeconds;
        State.RearLeftLateralRelaxationTimeSeconds = rl.LateralRelaxationTimeSeconds;
        State.RearRightLateralRelaxationTimeSeconds = rr.LateralRelaxationTimeSeconds;
        State.FrontLeftLateralRelaxationLengthMeters = fl.LateralRelaxationLengthMeters;
        State.FrontRightLateralRelaxationLengthMeters = fr.LateralRelaxationLengthMeters;
        State.RearLeftLateralRelaxationLengthMeters = rl.LateralRelaxationLengthMeters;
        State.RearRightLateralRelaxationLengthMeters = rr.LateralRelaxationLengthMeters;
        State.FrontLeftLowSpeedLateralForceScale = fl.LowSpeedLateralForceScale;
        State.FrontRightLowSpeedLateralForceScale = fr.LowSpeedLateralForceScale;
        State.RearLeftLowSpeedLateralForceScale = rl.LowSpeedLateralForceScale;
        State.RearRightLowSpeedLateralForceScale = rr.LowSpeedLateralForceScale;
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
        State.FrontLeftBrakePressureRatio = fl.BrakePressureRatio;
        State.FrontRightBrakePressureRatio = fr.BrakePressureRatio;
        State.RearLeftBrakePressureRatio = rl.BrakePressureRatio;
        State.RearRightBrakePressureRatio = rr.BrakePressureRatio;
        State.FrontLeftBrakePressureRegulatorActive = fl.BrakePressureRegulatorActive;
        State.FrontRightBrakePressureRegulatorActive = fr.BrakePressureRegulatorActive;
        State.RearLeftBrakePressureRegulatorActive = rl.BrakePressureRegulatorActive;
        State.RearRightBrakePressureRegulatorActive = rr.BrakePressureRegulatorActive;
        State.AbsActive =
            fl.BrakePressureRegulatorActive ||
            fr.BrakePressureRegulatorActive ||
            rl.BrakePressureRegulatorActive ||
            rr.BrakePressureRegulatorActive;
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
        ClassicBicycleSteeringParameters steering = _engineParameters.ClassicFourWheel.Steering;
        float speedKmh = speedMetersPerSecond * 3.6f;
        float legacyControlMaxAngleDegrees = CalculateMaxSteerAngleDegrees(speedKmh);
        float targetCommand = MathHelper.Clamp(steerInput, -1f, 1f);
        float commandRate = CalculateSteeringCommandRate(targetCommand, legacyControlMaxAngleDegrees, speedMetersPerSecond, dt);
        _currentSteerCommand = Approach(_currentSteerCommand, targetCommand, MathF.Max(0.01f, commandRate) * dt);

        SteeringEnvelope envelope = CalculatePhysicalSteeringEnvelope(speedKmh);
        float currentDegrees = CalculatePhysicalSteeringAngleDegrees(_currentSteerCommand, envelope, speedKmh);
        if (float.IsFinite(FrozenSteeringAngleDegreesForProbe))
        {
            currentDegrees = MathHelper.Clamp(
                FrozenSteeringAngleDegreesForProbe,
                -MathF.Abs(legacyControlMaxAngleDegrees),
                MathF.Abs(legacyControlMaxAngleDegrees));
        }
        else
        {
            currentDegrees = ApplyLowSpeedRoadWheelRateCap(currentDegrees, targetCommand, speedMetersPerSecond, dt);
        }

        _currentSteerRadians = MathHelper.ToRadians(currentDegrees);
        State.SteeringSpeedMatchedMaxAngleDegrees = envelope.OverdriveAngleDegrees;
        State.SteeringNormalizedCommand = _currentSteerCommand;
        State.SteeringLegacyControlMaxAngleDegrees = legacyControlMaxAngleDegrees;
        State.SteeringPhysicalNormalAngleDegrees = envelope.NormalAngleDegrees;
        State.SteeringPhysicalOverdriveAngleDegrees = envelope.OverdriveAngleDegrees;
        State.SteeringTransientBoostAngleDegrees = envelope.TransientBoostAngleDegrees;
        State.SteeringDigitalHoldSeconds = _steerHoldSeconds;
        State.SteeringCommandRatePerSecond = commandRate;
        State.FrontLeftSteerAngleDegrees = currentDegrees;
        State.FrontRightSteerAngleDegrees = currentDegrees;
    }

    private float CalculateSteeringCommandRate(
        float targetCommand,
        float legacyControlMaxAngleDegrees,
        float speedMetersPerSecond,
        float dt)
    {
        ClassicBicycleSteeringParameters steering = _engineParameters.ClassicFourWheel.Steering;
        float targetMagnitude = MathF.Abs(targetCommand);
        float currentMagnitude = MathF.Abs(_currentSteerCommand);
        bool hasInput = targetMagnitude > 0.01f;
        bool oppositeInput = hasInput &&
            MathF.Abs(_currentSteerCommand) > 0.01f &&
            MathF.Sign(targetCommand) != MathF.Sign(_currentSteerCommand);
        bool returningTowardCenter = targetMagnitude < currentMagnitude && !oppositeInput;
        bool startingInput = hasInput && _steerHoldSeconds <= 0f;

        if (!hasInput || oppositeInput)
        {
            _steerHoldSeconds = 0f;
        }
        else
        {
            _steerHoldSeconds += dt;
        }

        if (startingInput || oppositeInput)
        {
            _steeringTransientBoostSecondsRemaining = MathF.Max(
                _steeringTransientBoostSecondsRemaining,
                MathF.Max(0f, steering.TransientBoostSeconds));
        }
        _steeringTransientBoostSecondsRemaining = MathF.Max(0f, _steeringTransientBoostSecondsRemaining - dt);

        if (returningTowardCenter)
        {
            float configuredReleaseRate = MathF.Max(0.05f, steering.DigitalReleaseCommandRatePerSecond);
            float legacyReturnRate = CalculateGracefulSteeringReturnRate(
                _currentSteerCommand * legacyControlMaxAngleDegrees,
                legacyControlMaxAngleDegrees) / MathF.Max(1f, legacyControlMaxAngleDegrees);
            float returnRate = MathF.Max(configuredReleaseRate, legacyReturnRate);
            if (float.IsFinite(LowSpeedSteeringReturnRateMultiplierForProbe))
            {
                float lowSpeedWeight = 1f - SmoothStep01((MathF.Abs(speedMetersPerSecond) - 3.0f) / 2.5f);
                float multiplier = MathHelper.Lerp(
                    1f,
                    MathHelper.Clamp(LowSpeedSteeringReturnRateMultiplierForProbe, 0.05f, 1.5f),
                    lowSpeedWeight);
                returnRate *= multiplier;
            }

            return returnRate;
        }

        float baseRate;
        if (targetMagnitude >= 0.95f)
        {
            float holdBlend = SmoothStep01(_steerHoldSeconds / MathF.Max(0.05f, steering.DigitalRiseAccelerationSeconds));
            float initialRate = MathF.Max(0.05f, steering.DigitalInitialCommandRatePerSecond);
            float sustainedRate = MathF.Max(initialRate, steering.DigitalSustainedCommandRatePerSecond);
            baseRate = MathHelper.Lerp(initialRate, sustainedRate, holdBlend);
        }
        else
        {
            baseRate = MathF.Max(1f, steering.SteerSpeedDegreesPerSecond) / MathF.Max(1f, legacyControlMaxAngleDegrees);
        }

        return oppositeInput
            ? baseRate * MathF.Max(1f, steering.DigitalCounterSteerRateMultiplier)
            : baseRate;
    }

    private float ApplyLowSpeedRoadWheelRateCap(
        float targetDegrees,
        float targetCommand,
        float speedMetersPerSecond,
        float dt)
    {
        if (dt <= 0f)
        {
            return targetDegrees;
        }

        float currentDegrees = MathHelper.ToDegrees(_currentSteerRadians);
        bool hasInput = MathF.Abs(targetCommand) > 0.01f;
        bool currentWheelIsSteered = MathF.Abs(currentDegrees) > 0.05f;
        bool targetWheelIsSteered = MathF.Abs(targetDegrees) > 0.05f;
        bool oppositeInput = hasInput &&
            currentWheelIsSteered &&
            MathF.Sign(targetCommand) != MathF.Sign(currentDegrees);
        bool crossingCenter = currentWheelIsSteered &&
            targetWheelIsSteered &&
            MathF.Sign(targetDegrees) != MathF.Sign(currentDegrees);
        bool returningTowardCenter =
            !oppositeInput &&
            currentWheelIsSteered &&
            MathF.Abs(targetDegrees) < MathF.Abs(currentDegrees) - 0.05f;
        if (!oppositeInput && !crossingCenter && !returningTowardCenter)
        {
            return targetDegrees;
        }

        ClassicBicycleSteeringParameters steering = _engineParameters.ClassicFourWheel.Steering;
        float speed = MathF.Abs(speedMetersPerSecond);
        float lowSpeedStart = MathF.Max(0.5f, _engineParameters.ClassicFourWheel.LowSpeed.RollingDominantEndMetersPerSecond);
        float lowSpeedEnd = MathF.Max(lowSpeedStart + 4.0f, _engineParameters.ClassicFourWheel.LowSpeed.DynamicBlendEndMetersPerSecond + 4.5f);
        float lowSpeedWeight = 1f - SmoothStep01((speed - lowSpeedStart) / (lowSpeedEnd - lowSpeedStart));
        if (lowSpeedWeight <= 0f)
        {
            return targetDegrees;
        }

        float returnRate = MathF.Max(1f, steering.ReturnSpeedDegreesPerSecond);
        float steerRate = MathF.Max(1f, steering.SteerSpeedDegreesPerSecond);
        float highSpeedRate = oppositeInput || crossingCenter
            ? steerRate * 1.10f
            : returnRate;
        float lowSpeedRate = oppositeInput
            ? steerRate * 0.65f
            : returnRate * 0.55f;
        float maximumRate = MathHelper.Lerp(highSpeedRate, lowSpeedRate, lowSpeedWeight);
        return Approach(currentDegrees, targetDegrees, maximumRate * dt);
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
        float speedBlend = MathHelper.Clamp(speedKmh / 400f, 0f, 1f);
        return MathHelper.Lerp(steering.ZeroKmhAngleDegrees, steering.TwoHundredKmhAngleDegrees, speedBlend);
    }

    private SteeringEnvelope CalculatePhysicalSteeringEnvelope(float speedKmh)
    {
        ClassicBicycleSteeringParameters steering = _engineParameters.ClassicFourWheel.Steering;
        float legacyControlMaxAngleDegrees = CalculateMaxSteerAngleDegrees(speedKmh);
        float blend = SmoothStep01((speedKmh - steering.PhysicalEnvelopeBlendStartKmh) /
            MathF.Max(1f, steering.PhysicalEnvelopeFullKmh - steering.PhysicalEnvelopeBlendStartKmh));
        float normalCommand = MathHelper.Clamp(steering.NormalCommand, 0.1f, 0.98f);
        float normalG = MathHelper.Clamp(steering.NormalLateralAccelerationG, 0.05f, 2.5f);
        float overdriveG = MathF.Max(normalG, MathHelper.Clamp(steering.OverdriveLateralAccelerationG, 0.05f, 3.0f));
        float peakSlipDegrees = MathHelper.ToDegrees(MathF.Max(0.01f, _parameters.FrontTyres.LateralPeakSlipAngleRadians));
        float normalSlipAllowance = peakSlipDegrees * MathHelper.Clamp(steering.NormalPeakSlipFraction, 0f, 1f);
        float overdriveSlipAllowance = peakSlipDegrees * MathHelper.Clamp(steering.OverdrivePeakSlipFraction, steering.NormalPeakSlipFraction, 1.25f);
        float transientBoost = CalculateTransientSteeringBoostDegrees(peakSlipDegrees, steering, speedKmh);
        float normalAngle = MathF.Max(
            MathF.Max(0f, steering.MinimumHighSpeedAngleDegrees),
            CalculateLateralGSteerAngleDegrees(speedKmh, normalG) + normalSlipAllowance);
        float overdriveAngle = MathF.Max(
            normalAngle,
            CalculateLateralGSteerAngleDegrees(speedKmh, overdriveG) + overdriveSlipAllowance);

        return new SteeringEnvelope(
            MathHelper.Lerp(legacyControlMaxAngleDegrees * normalCommand, normalAngle, blend),
            MathHelper.Lerp(legacyControlMaxAngleDegrees, overdriveAngle, blend),
            transientBoost * blend);
    }

    private float CalculateTransientSteeringBoostDegrees(
        float peakSlipDegrees,
        ClassicBicycleSteeringParameters steering,
        float speedKmh)
    {
        if (_steeringTransientBoostSecondsRemaining <= 0f || MathF.Abs(_currentSteerCommand) <= 0.01f)
        {
            return 0f;
        }

        float speedGate = SmoothStep01((speedKmh - steering.PhysicalEnvelopeBlendStartKmh) /
            MathF.Max(1f, steering.PhysicalEnvelopeFullKmh - steering.PhysicalEnvelopeBlendStartKmh));
        float commandGate = SmoothStep01((MathF.Abs(_currentSteerCommand) - 0.08f) / 0.42f);
        float timeGate = MathHelper.Clamp(
            _steeringTransientBoostSecondsRemaining / MathF.Max(0.05f, steering.TransientBoostSeconds),
            0f,
            1f);
        return peakSlipDegrees *
            MathHelper.Clamp(steering.TransientPeakSlipFraction, 0f, 0.75f) *
            speedGate *
            commandGate *
            timeGate;
    }

    private float CalculatePhysicalSteeringAngleDegrees(float command, SteeringEnvelope envelope, float speedKmh)
    {
        float sign = MathF.Sign(command);
        float magnitude = MathF.Abs(command);
        if (sign == 0f || magnitude <= 0f)
        {
            return 0f;
        }

        float normalCommand = MathHelper.Clamp(_engineParameters.ClassicFourWheel.Steering.NormalCommand, 0.1f, 0.98f);
        float angleMagnitude = magnitude <= normalCommand
            ? envelope.NormalAngleDegrees * (magnitude / normalCommand)
            : MathHelper.Lerp(
                envelope.NormalAngleDegrees,
                envelope.OverdriveAngleDegrees,
                SmoothStep01((magnitude - normalCommand) / (1f - normalCommand)));
        float boostGate = SmoothStep01((magnitude - 0.05f) / 0.45f);
        angleMagnitude += envelope.TransientBoostAngleDegrees * boostGate;
        return sign * angleMagnitude;
    }

    private float CalculateLateralGSteerAngleDegrees(float speedKmh, float lateralG)
    {
        if (speedKmh <= 0.1f)
        {
            return CalculateMaxSteerAngleDegrees(speedKmh);
        }

        float speed = speedKmh / 3.6f;
        float lateralAcceleration = lateralG * Gravity;
        VehicleAxleGeometry geometry = VehicleAxleGeometry.FromParameters(_parameters);
        return MathHelper.ToDegrees(MathF.Atan(geometry.WheelbaseMeters * lateralAcceleration / MathF.Max(0.01f, speed * speed)));
    }

    public static float CalculateDiagnosticTyreLateralForce(float slipRadians, float maxForceN, ClassicBicycleTyreParameters tyres)
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

    public static float CalculateClassicTyreGripLimit(
        float normalLoadN,
        float surfaceMu,
        ClassicBicycleTyreParameters tyres)
    {
        float referenceLoad = tyres.ReferenceLoadN > 0f
            ? tyres.ReferenceLoadN
            : normalLoadN;
        float loadSensitivity = MathHelper.Clamp(tyres.LoadSensitivity, 0f, 0.35f);
        float loadScale = loadSensitivity > 0f
            ? MathF.Pow(referenceLoad / MathF.Max(150f, normalLoadN), loadSensitivity)
            : 1f;
        loadScale = MathHelper.Clamp(loadScale, 0.72f, 1.18f);
        return MathF.Max(1f, normalLoadN * MathF.Max(0.01f, tyres.MaxGrip) * MathF.Max(0.05f, surfaceMu) * loadScale);
    }

    public static ClassicBicycleTyreParameters CopyTyreWithLoadSensitivity(
        ClassicBicycleTyreParameters source,
        float loadSensitivity)
    {
        return new ClassicBicycleTyreParameters
        {
            CorneringStiffness = source.CorneringStiffness,
            PeakSlipAngleDegrees = source.PeakSlipAngleDegrees,
            FalloffSlipAngleDegrees = source.FalloffSlipAngleDegrees,
            MaxGrip = source.MaxGrip,
            SlidingGrip = source.SlidingGrip,
            LoadSensitivity = MathHelper.Clamp(loadSensitivity, 0f, 0.35f),
            ReferenceLoadN = source.ReferenceLoadN,
            RelaxationLengthMeters = source.RelaxationLengthMeters
        };
    }

    private float CalculateDriveForce(float throttle, float forwardSpeed)
    {
        float gearRatio = GetCurrentGearRatio();
        State.RevLimiterWheelImpliedRpm = gearRatio > 0f && State.Gear != 0
            ? CalculateRoadRpm(forwardSpeed, gearRatio)
            : 0f;
        State.RevLimiterTorqueProducingRpm = State.Rpm;
        State.RevLimiterEngineTorqueNm = 0f;
        State.RevLimiterDeliveredWheelForceN = 0f;

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

        float roadRpm = CalculateRoadRpm(forwardSpeed, gearRatio);
        float rpmForTorque = State.Gear > 0 && MathF.Abs(forwardSpeed) > 4f
            ? MathF.Max(_parameters.IdleRpm, roadRpm)
            : State.Rpm;
        State.RevLimiterWheelImpliedRpm = roadRpm;
        State.RevLimiterTorqueProducingRpm = rpmForTorque;
        float requestedTorque = _parameters.TorqueAtRpm(rpmForTorque) * throttle;
        float torqueMultiplier = MathHelper.Clamp(State.LimiterTorqueMultiplier, 0f, 1f);
        float fuelCutDragTorque = State.RevLimiterActive
            ? _parameters.EngineBrakeTorqueAtRpm(rpmForTorque) * (1f - torqueMultiplier)
            : 0f;
        float deliveredTorque = requestedTorque * torqueMultiplier - fuelCutDragTorque;
        State.RevLimiterEngineTorqueNm = deliveredTorque;
        float wheelTorque = deliveredTorque * gearRatio * _parameters.FinalDriveRatio * _parameters.DrivetrainEfficiency;
        float force = wheelTorque / MathF.Max(0.05f, _parameters.WheelRadiusMeters);
        float signedForce = State.Gear < 0 ? -force : force;
        State.RevLimiterDeliveredWheelForceN = signedForce;
        return signedForce;
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

    private float CalculateBrakingSteeringBlend(float brake, float steerCommand)
    {
        ClassicBicycleGripBudgetParameters gripBudget = _engineParameters.ClassicFourWheel.GripBudget;
        float steerGate = SmoothStep01((MathF.Abs(steerCommand) - gripBudget.BrakingSteeringPrioritySteerStart) /
            MathF.Max(0.01f, gripBudget.BrakingSteeringPrioritySteerEnd - gripBudget.BrakingSteeringPrioritySteerStart));
        float brakeGate = SmoothStep01((brake - gripBudget.BrakingSteeringPriorityBrakeStart) /
            MathF.Max(0.01f, gripBudget.BrakingSteeringPriorityBrakeEnd - gripBudget.BrakingSteeringPriorityBrakeStart));

        return steerGate * brakeGate;
    }

    private float CalculateBrakingSteeringLateralPriority(float brakingSteeringBlend)
    {
        ClassicBicycleGripBudgetParameters gripBudget = _engineParameters.ClassicFourWheel.GripBudget;
        return MathHelper.Clamp(gripBudget.BrakingSteeringLateralPriority, 0f, 0.85f) *
            MathHelper.Clamp(brakingSteeringBlend, 0f, 1f);
    }

    private static float ClampCombinedForce(
        ref float longitudinal,
        ref float lateral,
        float maxForce,
        float exponent,
        float brakingSteeringLateralPriority = 0f)
    {
        maxForce = MathF.Max(1f, maxForce);
        exponent = MathHelper.Clamp(exponent, 1.2f, 4f);
        float demand = CalculateCombinedGripDemand(longitudinal, lateral, maxForce, exponent);
        if (demand <= 1f)
        {
            return demand;
        }

        float scale = MathF.Pow(demand, -1f / exponent);
        float lateralPriority = MathHelper.Clamp(brakingSteeringLateralPriority, 0f, 0.85f);
        if (lateralPriority > 0f && longitudinal < 0f && MathF.Abs(lateral) > 0.01f)
        {
            float scaledLateral = lateral * scale;
            float prioritizedLateral = MathHelper.Lerp(scaledLateral, lateral, lateralPriority);
            float lateralRatio = MathHelper.Clamp(MathF.Abs(prioritizedLateral) / maxForce, 0f, 1f);
            float remainingLongitudinalRatio = MathF.Pow(
                MathF.Max(0f, 1f - MathF.Pow(lateralRatio, exponent)),
                1f / exponent);

            lateral = MathF.Sign(lateral) * lateralRatio * maxForce;
            longitudinal = MathF.Sign(longitudinal) *
                MathF.Min(MathF.Abs(longitudinal), remainingLongitudinalRatio * maxForce);
            return 1f;
        }

        longitudinal *= scale;
        lateral *= scale;
        return 1f;
    }

    private static float CalculateCombinedGripUsage(float longitudinal, float lateral, float maxForce, float exponent)
    {
        float demand = CalculateCombinedGripDemand(longitudinal, lateral, maxForce, exponent);
        return MathF.Pow(MathF.Max(0f, demand), 1f / MathHelper.Clamp(exponent, 1.2f, 4f));
    }

    private static float CalculateCombinedGripDemand(float longitudinal, float lateral, float maxForce, float exponent)
    {
        maxForce = MathF.Max(1f, maxForce);
        exponent = MathHelper.Clamp(exponent, 1.2f, 4f);
        return
            MathF.Pow(MathF.Abs(longitudinal / maxForce), exponent) +
            MathF.Pow(MathF.Abs(lateral / maxForce), exponent);
    }

    private void AdvanceEnginePresentation(float throttle, float forwardSpeed, float dt)
    {
        State.PreviousPhysicsRpm = State.Rpm;
        float gearRatio = GetCurrentGearRatio();
        float roadRpm = gearRatio > 0f && State.Gear != 0
            ? CalculateRoadRpm(forwardSpeed, gearRatio)
            : 0f;
        State.RevLimiterWheelImpliedRpm = roadRpm;
        float speedClutchT = SmoothStep01(MathF.Abs(forwardSpeed) / 4f);
        float launchClutchT = State.Gear != 0 && throttle > 0.05f
            ? SmoothStep01(_launchClutchCouplingSeconds / 0.42f)
            : 0f;
        float lowSpeedClutchT = MathF.Max(speedClutchT, launchClutchT);
        float freeRevTarget = _parameters.IdleRpm + throttle * (_parameters.LimiterHardCutRpm - _parameters.IdleRpm);
        float targetRpm = State.Gear == 0
            ? freeRevTarget
            : MathHelper.Lerp(MathF.Max(_parameters.IdleRpm, freeRevTarget), MathF.Max(_parameters.IdleRpm, roadRpm), lowSpeedClutchT);

        if (State.Gear > 0 && MathF.Abs(forwardSpeed) > 4f)
        {
            targetRpm = MathF.Max(_parameters.IdleRpm, roadRpm);
        }

        if (State.RevLimiterActive && MathF.Abs(forwardSpeed) <= 4f)
        {
            targetRpm = MathF.Min(
                targetRpm,
                MathF.Max(_parameters.IdleRpm, _parameters.RevLimiterResumeRpm - MathF.Max(40f, _parameters.RevLimiterBounceRpm * 0.45f)));
        }

        bool inDrivenGearAtSpeed = State.Gear > 0 && MathF.Abs(forwardSpeed) > 4f;
        bool limiterCycleActive = State.RevLimiterActive || _revLimiterRestoreTimerSeconds > 0f;
        bool limiterComplianceActive = limiterCycleActive ||
            (inDrivenGearAtSpeed &&
             throttle > 0.05f &&
             roadRpm >= _parameters.RevLimiterResumeRpm &&
             State.Rpm < roadRpm - 5f);
        if (inDrivenGearAtSpeed && !limiterComplianceActive)
        {
            State.Rpm = targetRpm;
        }
        else if (inDrivenGearAtSpeed)
        {
            State.Rpm = Approach(State.Rpm, targetRpm, MathF.Max(100f, _parameters.MaxFreeRevRiseRpmPerSecond) * dt);
        }
        else
        {
            float rate = targetRpm > State.Rpm ? _parameters.MaxFreeRevRiseRpmPerSecond : _parameters.MaxFreeRevFallRpmPerSecond;
            State.Rpm = Approach(State.Rpm, targetRpm, MathF.Max(100f, rate) * dt);
        }

        if (State.Rpm > _parameters.LimiterHardCutRpm)
        {
            State.Rpm = _parameters.LimiterHardCutRpm;
        }

        UpdateRevLimiterState(throttle, State.Rpm, dt);
        ApplyLimiterEngineRpmCompliance(inDrivenGearAtSpeed, throttle, roadRpm, dt);

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
        State.EnginePowerUnitFuelCutBlend = State.RevLimiterActive ? 1f : 0f;
        State.EnginePowerUnitTransmissionRpm = roadRpm;
        State.EnginePowerUnitEngineDriveTorqueNm = State.RevLimiterEngineTorqueNm;
        State.EnginePowerUnitRawTorqueNm = _parameters.TorqueAtRpm(State.RevLimiterTorqueProducingRpm) * throttle;
        State.EnginePowerUnitDriveTorqueNm = State.RevLimiterDeliveredWheelForceN * _parameters.WheelRadiusMeters;
        State.RTypeEngineRpm = State.Rpm;
        State.RTypeEngineThrottle = throttle;
        State.RTypeEngineLimiterCut = State.RevLimiterActive;
        State.RTypeEngineRevLimitTimerSeconds = State.RevLimiterCutTimerSeconds;
    }

    private void ApplyLimiterEngineRpmCompliance(bool inDrivenGearAtSpeed, float throttle, float roadRpm, float dt)
    {
        if (!inDrivenGearAtSpeed || throttle <= 0.05f)
        {
            return;
        }

        float cutRpm = MathF.Max(_parameters.IdleRpm + 100f, _parameters.LimiterHardCutRpm);
        float bounceDepth = RevLimiterPresentationRules.CalculateBounceDepthRpm(cutRpm);
        float minimumBounceRpm = MathF.Max(_parameters.IdleRpm, cutRpm - bounceDepth * 0.64f);
        float targetRpm;
        float responseRpmPerSecond;

        if (State.RevLimiterActive)
        {
            float cutProgress = 1f - MathHelper.Clamp(
                State.RevLimiterCutTimerSeconds / MathF.Max(dt, _parameters.RevLimiterFuelCutSeconds),
                0f,
                1f);
            float dipShape = cutProgress * cutProgress * (3f - 2f * cutProgress);
            targetRpm = MathHelper.Lerp(cutRpm - bounceDepth * 0.18f, minimumBounceRpm, dipShape);
            responseRpmPerSecond = 18000f;
        }
        else if (State.RevLimiterRestoreTimerSeconds > 0f)
        {
            float restoreProgress = 1f - MathHelper.Clamp(
                State.RevLimiterRestoreTimerSeconds / MathF.Max(dt, _parameters.RevLimiterRestoreSeconds),
                0f,
                1f);
            float chargeShape = 1f - MathF.Pow(1f - restoreProgress, 2.4f);
            targetRpm = MathHelper.Lerp(minimumBounceRpm, MathF.Min(cutRpm, MathF.Max(roadRpm, _parameters.RevLimiterResumeRpm)), chargeShape);
            responseRpmPerSecond = 17000f;
        }
        else
        {
            return;
        }

        State.Rpm = Approach(State.Rpm, MathHelper.Clamp(targetRpm, minimumBounceRpm, cutRpm), responseRpmPerSecond * dt);
    }

    private void UpdateRevLimiterState(float throttle, float rpm, float dt)
    {
        float cutRpm = MathF.Max(_parameters.IdleRpm + 100f, _parameters.LimiterHardCutRpm);
        float resumeRpm = MathHelper.Clamp(
            _parameters.RevLimiterResumeRpm,
            _parameters.IdleRpm,
            cutRpm - 10f);
        float cutSeconds = MathF.Max(dt, _parameters.RevLimiterFuelCutSeconds);
        float restoreSeconds = MathF.Max(0f, _parameters.RevLimiterRestoreSeconds);
        float cutMultiplier = MathHelper.Clamp(_parameters.RevLimiterCutTorqueMultiplier, 0f, 1f);
        bool throttleDemand = throttle > 0.05f;

        if (!throttleDemand)
        {
            _revLimiterCutTimerSeconds = 0f;
            _revLimiterRestoreTimerSeconds = 0f;
            State.RevLimiterActive = false;
            State.LimiterTorqueMultiplier = 1f;
            State.RevLimiterBounceIntensity = 0f;
            State.RevLimiterBouncePhase = 0f;
            PublishRevLimiterTimers();
            return;
        }

        if (!State.RevLimiterActive && rpm >= cutRpm - 1f)
        {
            BeginRevLimiterCut(cutSeconds);
        }

        if (State.RevLimiterActive)
        {
            _revLimiterCutTimerSeconds = MathF.Max(0f, _revLimiterCutTimerSeconds - dt);
            bool minimumCutElapsed = _revLimiterCutTimerSeconds <= 0f;
            if (minimumCutElapsed)
            {
                State.RevLimiterActive = false;
                _revLimiterRestoreTimerSeconds = restoreSeconds;
            }
        }
        else if (_revLimiterRestoreTimerSeconds > 0f)
        {
            _revLimiterRestoreTimerSeconds = MathF.Max(0f, _revLimiterRestoreTimerSeconds - dt);
            bool restoreComplete = _revLimiterRestoreTimerSeconds <= 0f;
            bool reachedLimiterAgain = rpm >= cutRpm - 1f;
            if (restoreComplete && reachedLimiterAgain)
            {
                BeginRevLimiterCut(cutSeconds);
            }
        }

        if (State.RevLimiterActive)
        {
            State.LimiterTorqueMultiplier = cutMultiplier;
            State.RevLimiterBounceIntensity = 1f;
            State.RevLimiterBouncePhase = RevLimiterPresentationRules.AdvanceBouncePhase(
                State.RevLimiterBouncePhase,
                cutRpm,
                dt);
        }
        else if (_revLimiterRestoreTimerSeconds > 0f && restoreSeconds > 0f)
        {
            float restoreT = 1f - MathHelper.Clamp(_revLimiterRestoreTimerSeconds / restoreSeconds, 0f, 1f);
            float shapedRestoreT = restoreT * restoreT * (3f - 2f * restoreT);
            State.LimiterTorqueMultiplier = MathHelper.Lerp(cutMultiplier, 1f, shapedRestoreT);
            State.RevLimiterBounceIntensity = MathHelper.Clamp(1f - restoreT, 0f, 1f);
            State.RevLimiterBouncePhase = RevLimiterPresentationRules.AdvanceBouncePhase(
                State.RevLimiterBouncePhase,
                cutRpm,
                dt);
        }
        else
        {
            State.LimiterTorqueMultiplier = 1f;
            State.RevLimiterBounceIntensity = 0f;
            State.RevLimiterBouncePhase = 0f;
        }

        PublishRevLimiterTimers();
    }

    private void PublishRevLimiterTimers()
    {
        State.RevLimiterCutTimerSeconds = _revLimiterCutTimerSeconds;
        State.RevLimiterRestoreTimerSeconds = _revLimiterRestoreTimerSeconds;
    }

    private void BeginRevLimiterCut(float baseCutSeconds)
    {
        _revLimiterPulseIndex++;
        State.RevLimiterActive = true;
        _revLimiterCutTimerSeconds = MathF.Max(1f / 120f, baseCutSeconds);
        _revLimiterRestoreTimerSeconds = 0f;
    }

    private float CalculateRoadRpm(float forwardSpeed, float gearRatio)
    {
        return MathF.Abs(forwardSpeed) / MathF.Max(0.05f, _parameters.WheelRadiusMeters) *
            gearRatio *
            _parameters.FinalDriveRatio *
            OmegaToRpm;
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

    private void LimitForwardGearSpeed()
    {
        if (State.Gear <= 0)
        {
            return;
        }

        float gearRatio = GetCurrentGearRatio();
        if (gearRatio <= 0f)
        {
            return;
        }

        float maximumForwardSpeed = _parameters.LimiterHardCutRpm /
            MathF.Max(0.001f, gearRatio * _parameters.FinalDriveRatio) /
            60f *
            MathF.Tau *
            MathF.Max(0.05f, _parameters.WheelRadiusMeters);
        Vector2 forward = new(State.Forward.X, State.Forward.Z);
        Vector2 right = new(State.Right.X, State.Right.Z);
        float forwardSpeed = Vector2.Dot(State.Velocity, forward);
        if (forwardSpeed <= maximumForwardSpeed)
        {
            return;
        }

        float lateralSpeed = Vector2.Dot(State.Velocity, right);
        State.Velocity = forward * maximumForwardSpeed + right * lateralSpeed;
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

    public static ClassicFourWheelTyres ResolveClassicTyres(
        VehicleSimulationParameters parameters,
        ClassicBicycleParameters classicFallback)
    {
        return new ClassicFourWheelTyres(
            ConvertResolvedTyre(
                parameters.FrontTyres,
                classicFallback.FrontTyres,
                parameters.MassKg * Gravity * MathHelper.Clamp(parameters.FrontWeightDistribution, 0.05f, 0.95f) * 0.5f),
            ConvertResolvedTyre(
                parameters.RearTyres,
                classicFallback.RearTyres,
                parameters.MassKg * Gravity * (1f - MathHelper.Clamp(parameters.FrontWeightDistribution, 0.05f, 0.95f)) * 0.5f));
    }

    private static ClassicBicycleTyreParameters ConvertResolvedTyre(
        TyreAxleParameters resolvedTyre,
        ClassicBicycleTyreParameters fallback,
        float referenceLoadN)
    {
        if (!float.IsFinite(resolvedTyre.CorneringStiffnessNPerRad) ||
            resolvedTyre.CorneringStiffnessNPerRad <= 0f)
        {
            return fallback;
        }

        float peakSlipDegrees = MathHelper.ToDegrees(resolvedTyre.LateralPeakSlipAngleRadians);
        float slideSlipDegrees = MathHelper.ToDegrees(resolvedTyre.LateralSlideSlipAngleRadians);
        return new ClassicBicycleTyreParameters
        {
            CorneringStiffness = MathHelper.Clamp(resolvedTyre.CorneringStiffnessNPerRad / 8500f, 0.45f, 18f),
            PeakSlipAngleDegrees = float.IsFinite(peakSlipDegrees) && peakSlipDegrees > 0f
                ? peakSlipDegrees
                : fallback.PeakSlipAngleDegrees,
            FalloffSlipAngleDegrees = float.IsFinite(slideSlipDegrees) && slideSlipDegrees > peakSlipDegrees
                ? slideSlipDegrees
                : fallback.FalloffSlipAngleDegrees,
            MaxGrip = float.IsFinite(resolvedTyre.PeakFriction) && resolvedTyre.PeakFriction > 0f
                ? resolvedTyre.PeakFriction
                : fallback.MaxGrip,
            SlidingGrip = float.IsFinite(resolvedTyre.SlidingLateralFrictionMultiplier) && resolvedTyre.SlidingLateralFrictionMultiplier > 0f
                ? resolvedTyre.SlidingLateralFrictionMultiplier
                : fallback.SlidingGrip,
            LoadSensitivity = float.IsFinite(resolvedTyre.LoadSensitivity)
                ? MathHelper.Clamp(resolvedTyre.LoadSensitivity, 0f, 0.35f)
                : MathHelper.Clamp(fallback.LoadSensitivity, 0f, 0.35f),
            ReferenceLoadN = MathF.Max(150f, referenceLoadN),
            RelaxationLengthMeters = float.IsFinite(resolvedTyre.RelaxationLengthMeters) && resolvedTyre.RelaxationLengthMeters > 0f
                ? resolvedTyre.RelaxationLengthMeters
                : MathF.Max(0f, fallback.RelaxationLengthMeters)
        };
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

    private readonly record struct SteeringEnvelope(
        float NormalAngleDegrees,
        float OverdriveAngleDegrees,
        float TransientBoostAngleDegrees);

    private struct SuspensionCornerState
    {
        public float TravelMeters;
        public float VelocityMetersPerSecond;
        public float SpringForceN;
        public float DamperForceN;
        public float TargetLoadN;
        public float NormalLoadN;

        public static SuspensionCornerState Static(float loadN)
        {
            return new SuspensionCornerState
            {
                TargetLoadN = loadN,
                NormalLoadN = loadN
            };
        }
    }

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
        float LocalForwardSpeedMetersPerSecond,
        float LocalLateralSpeedMetersPerSecond,
        float YawLateralContributionMetersPerSecond,
        float RequestedLongitudinalForceN,
        float LowSpeedSlipLateralForceN,
        float LowSpeedRollingConstraintForceN,
        float LowSpeedRollingBlend,
        float RequestedLateralForceN,
        float RelaxedRequestedLateralForceN,
        float RelaxedLateralSlipRadians,
        float LateralRelaxationDeltaN,
        float LateralRelaxationTimeSeconds,
        float LateralRelaxationLengthMeters,
        float LowSpeedLateralForceScale,
        float LongitudinalForceN,
        float WheelLateralForceN,
        float LocalForceRightN,
        float LocalForceForwardN,
        float GripUsage,
        float BrakePressureRatio,
        bool BrakePressureRegulatorActive);
}

public sealed class ClassicLowSpeedForceDiagnosticOptions
{
    public static ClassicLowSpeedForceDiagnosticOptions Default { get; } = new();

    public float WalkingSpeedMetersPerSecond { get; init; } = 3.0f;

    public float FrontSlipLateralMultiplier { get; init; } = 1.0f;

    public float FrontDriveSideMultiplier { get; init; } = 1.0f;

    public float RearLateralResistanceMultiplier { get; init; } = 1.0f;

    public float KinematicYawBlend { get; init; }

    public float KinematicBlendEndSpeedMetersPerSecond { get; init; } = 2.5f;

    public bool BypassLateralRelaxationBelowTransition { get; init; }

    public bool RollingConstraintOnlyBelowTransition { get; init; }

    public bool SlipDerivedOnlyBelowTransition { get; init; }

    public bool UnwindLateralForceBeforeSignChange { get; init; }

    public bool UseContactPatchSlipRelaxation { get; init; }

    public bool LimitLowSpeedSlipRate { get; init; }

    public float MaxLowSpeedSlipRateDegreesPerSecond { get; init; } = 180f;

    public float SlipRateLimitFadeStartMetersPerSecond { get; init; } = 2.0f;

    public float SlipRateLimitFadeEndMetersPerSecond { get; init; } = 5.0f;

    public bool EnablePostForceRollingContactConstraint { get; init; }

    public bool DisablePostForceRollingContactConstraint { get; init; }
}
