using Microsoft.Xna.Framework;
using RType.World;

namespace RType.Vehicle;

public sealed class ClassicBicycleVehicleSimulator : IVehicleSimulator
{
    private const float Gravity = 9.81f;
    private const float RpmToOmega = MathF.Tau / 60f;
    private const float OmegaToRpm = 60f / MathF.Tau;

    private readonly ITrackSurfaceSampler _surfaceSampler;
    private readonly VehicleSimulationParameters _parameters;
    private readonly SimulationEngineParameters _engineParameters;
    private float _fixedTickAccumulatorSeconds;
    private VehicleInput _pendingInput;
    private bool _hasPendingInput;
    private bool _manualTransmission;
    private float _currentSteerRadians;
    private float _previousForwardSpeed;
    private float _previousLateralSpeed;
    private float _previousLongitudinalAccelerationForLoadTransfer;
    private float _engineCrankPhaseDegrees;

    public ClassicBicycleVehicleSimulator(
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
        _hasPendingInput = true;
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

        if (ticks == 0 && _hasPendingInput && cappedDt > 0f)
        {
            Step(_pendingInput, cappedDt);
            _pendingInput = ClearLatchedButtons(_pendingInput);
            _fixedTickAccumulatorSeconds = 0f;
        }

        State.PhysicsTickAlpha = fixedDt > 0f
            ? MathHelper.Clamp(_fixedTickAccumulatorSeconds / fixedDt, 0f, 1f)
            : 0f;
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

    private void Step(VehicleInput input, float dt)
    {
        Vector3 forward3 = State.Forward;
        Vector3 right3 = State.Right;
        Vector2 forward = new(forward3.X, forward3.Z);
        Vector2 right = new(right3.X, right3.Z);
        float forwardSpeed = Vector2.Dot(State.Velocity, forward);
        float lateralSpeed = Vector2.Dot(State.Velocity, right);
        float speed = State.Velocity.Length();

        UpdateGear(input, forwardSpeed);
        float throttle = State.Gear < 0 ? input.Reverse : input.Throttle;
        float brake = input.Brake;
        float handbrake = input.Handbrake;

        UpdateSteering(input.Steer, speed, dt);

        SurfaceSample surface = _surfaceSampler.Sample(State.Position);
        float surfaceMu = MathF.Max(0.05f, surface.StaticFrictionCoefficient);

        ClassicBicycleParameters classic = _engineParameters.ClassicBicycle;
        float mass = MathF.Max(1f, _parameters.MassKg);
        float wheelbase = MathF.Max(0.1f, _parameters.WheelbaseMeters);
        float frontBias = MathHelper.Clamp(_parameters.FrontWeightDistribution, 0.05f, 0.95f);
        float frontDistance = wheelbase * (1f - frontBias);
        float rearDistance = wheelbase * frontBias;
        float staticFrontLoad = mass * Gravity * frontBias;
        float staticRearLoad = mass * Gravity * (1f - frontBias);
        float loadTransfer = mass *
            _previousLongitudinalAccelerationForLoadTransfer *
            MathHelper.Clamp(_parameters.CenterOfGravityHeightMeters, 0.05f, 1.5f) /
            wheelbase;
        float minAxleLoad = (staticFrontLoad + staticRearLoad) * 0.05f;
        float frontLoad = MathHelper.Clamp(staticFrontLoad - loadTransfer, minAxleLoad, staticFrontLoad + staticRearLoad - minAxleLoad);
        float rearLoad = staticFrontLoad + staticRearLoad - frontLoad;
        float frontMaxForce = frontLoad * MathF.Max(0.01f, classic.FrontTyres.MaxGrip) * surfaceMu;
        float rearMaxForce = rearLoad * MathF.Max(0.01f, classic.RearTyres.MaxGrip) * surfaceMu;

        /*
         * Classic bicycle sign convention:
         * - Ground plane is X/Z. Heading 0 points along +Z.
         * - Positive steering input is player-right.
         * - Positive lateral velocity is vehicle-right.
         * - Positive slip angle means the tyre should generate rightward force.
         * - Positive lateral tyre force is vehicle-right.
         * - Negative yaw rate rotates the car to the player's right in the existing world/render convention.
         */
        float slipDenominator = EffectiveSlipSpeed(forwardSpeed, classic.LowSpeed.SlipSpeedFloorMetersPerSecond);
        float frontAxleLateralSpeed = lateralSpeed - State.YawRateRadiansPerSecond * frontDistance;
        float rearAxleLateralSpeed = lateralSpeed + State.YawRateRadiansPerSecond * rearDistance;
        float frontSlipRadians = _currentSteerRadians - MathF.Atan2(frontAxleLateralSpeed, slipDenominator);
        float rearSlipRadians = -MathF.Atan2(rearAxleLateralSpeed, slipDenominator);

        float requestedFrontLateral = CalculateTyreLateralForce(frontSlipRadians, frontMaxForce, classic.FrontTyres);
        float requestedRearLateral = CalculateTyreLateralForce(rearSlipRadians, rearMaxForce, classic.RearTyres);
        float requestedFrontLongitudinal = 0f;
        float requestedRearLongitudinal = 0f;

        float driveForce = CalculateDriveForce(throttle, forwardSpeed, dt);
        RouteDriveForce(driveForce, out float frontDriveForce, out float rearDriveForce);
        requestedFrontLongitudinal += frontDriveForce;
        requestedRearLongitudinal += rearDriveForce;

        float engineBrakeForce = CalculateEngineBrakeForce(throttle, forwardSpeed);
        RouteDriveForce(engineBrakeForce, out float frontEngineBrakeForce, out float rearEngineBrakeForce);
        requestedFrontLongitudinal += frontEngineBrakeForce;
        requestedRearLongitudinal += rearEngineBrakeForce;

        float brakeDirection = speed > 0.08f ? -MathF.Sign(forwardSpeed == 0f ? 1f : forwardSpeed) : 0f;
        float brakeForce = brake * MathF.Max(0f, _parameters.MaxBrakeForceN);
        float frontServiceBrakeForce = brakeDirection * brakeForce * MathHelper.Clamp(_parameters.BrakeBiasFront, 0f, 1f);
        float rearServiceBrakeForce = brakeDirection * brakeForce * (1f - MathHelper.Clamp(_parameters.BrakeBiasFront, 0f, 1f));
        float rearHandbrakeForce = brakeDirection * handbrake * MathF.Max(0f, _parameters.MaxBrakeForceN);
        requestedFrontLongitudinal += frontServiceBrakeForce;
        requestedRearLongitudinal += rearServiceBrakeForce;
        requestedRearLongitudinal += rearHandbrakeForce;

        float frontLongitudinal = requestedFrontLongitudinal;
        float frontLateral = requestedFrontLateral;
        float rearLongitudinal = requestedRearLongitudinal;
        float rearLateral = requestedRearLateral;
        float frontGripUsage = ClampCombinedForce(ref frontLongitudinal, ref frontLateral, frontMaxForce, classic.GripBudget.CombinedGripExponent);
        float rearGripUsage = ClampCombinedForce(ref rearLongitudinal, ref rearLateral, rearMaxForce, classic.GripBudget.CombinedGripExponent);

        float rollingResistance = _parameters.RollingResistanceCoefficient *
            classic.Resistance.RollingResistanceMultiplier *
            surface.RollingResistanceMultiplier *
            mass *
            Gravity *
            MathF.Sign(forwardSpeed) *
            SmoothStep01(MathF.Abs(forwardSpeed) / 1.0f);
        float aeroDrag = _parameters.AeroDragFactor *
            classic.Resistance.AeroDragMultiplier *
            forwardSpeed *
            MathF.Abs(forwardSpeed);
        float longitudinalForce = frontLongitudinal + rearLongitudinal - rollingResistance - aeroDrag;
        float lateralForce = frontLateral + rearLateral - lateralSpeed * mass * MathF.Max(0f, classic.Yaw.LateralVelocityDamping);
        float localLongitudinalAcceleration = longitudinalForce / mass;
        float localLateralAcceleration = lateralForce / mass;

        Vector2 acceleration = forward * localLongitudinalAcceleration + right * localLateralAcceleration;
        State.Velocity += acceleration * dt;
        LimitTopSpeed();

        float yawInertia = MathF.Max(1f, _parameters.YawInertiaKgM2 * MathF.Max(0.1f, classic.Yaw.InertiaScale));
        float yawTorque = -frontLateral * frontDistance + rearLateral * rearDistance;
        float yawAcceleration = yawTorque / yawInertia - State.YawRateRadiansPerSecond * MathF.Max(0f, classic.Yaw.Damping);
        State.YawRateRadiansPerSecond += yawAcceleration * dt;
        State.HeadingRadians = MathHelper.WrapAngle(State.HeadingRadians + State.YawRateRadiansPerSecond * dt);
        State.Position += new Vector3(State.Velocity.X, 0f, State.Velocity.Y) * dt;

        PublishState(
            input,
            throttle,
            brake,
            handbrake,
            surface,
            forwardSpeed,
            lateralSpeed,
            localLongitudinalAcceleration,
            localLateralAcceleration,
            staticFrontLoad,
            staticRearLoad,
            frontLoad,
            rearLoad,
            loadTransfer,
            frontMaxForce,
            rearMaxForce,
            frontSlipRadians,
            rearSlipRadians,
            driveForce,
            engineBrakeForce,
            frontServiceBrakeForce + rearServiceBrakeForce,
            rearHandbrakeForce,
            rollingResistance,
            aeroDrag,
            requestedFrontLongitudinal,
            requestedRearLongitudinal,
            frontLongitudinal,
            rearLongitudinal,
            frontLateral,
            rearLateral,
            frontGripUsage,
            rearGripUsage);

        AdvanceEnginePresentation(throttle, forwardSpeed, dt);
        _previousForwardSpeed = forwardSpeed;
        _previousLateralSpeed = lateralSpeed;
        _previousLongitudinalAccelerationForLoadTransfer = localLongitudinalAcceleration;
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
        float targetRadians = MathHelper.ToRadians(MathHelper.Clamp(steerInput, -1f, 1f) * maxAngleDegrees);
        float currentDegrees = MathHelper.ToDegrees(_currentSteerRadians);
        float targetDegrees = MathHelper.ToDegrees(targetRadians);
        float rate = MathF.Abs(targetDegrees) < MathF.Abs(currentDegrees)
            ? _engineParameters.ClassicBicycle.Steering.ReturnSpeedDegreesPerSecond
            : _engineParameters.ClassicBicycle.Steering.SteerSpeedDegreesPerSecond;
        currentDegrees = Approach(currentDegrees, targetDegrees, MathF.Max(1f, rate) * dt);
        _currentSteerRadians = MathHelper.ToRadians(currentDegrees);
        State.SteeringSpeedMatchedMaxAngleDegrees = maxAngleDegrees;
        State.FrontLeftSteerAngleDegrees = currentDegrees;
        State.FrontRightSteerAngleDegrees = currentDegrees;
    }

    private float CalculateMaxSteerAngleDegrees(float speedKmh)
    {
        ClassicBicycleSteeringParameters steering = _engineParameters.ClassicBicycle.Steering;
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

    private float CalculateTyreLateralForce(float slipRadians, float maxForceN, ClassicBicycleTyreParameters tyres)
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

    private float CalculateDriveForce(float throttle, float forwardSpeed, float dt)
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
        if (closedThrottle <= 0.001f)
        {
            return 0f;
        }

        float speedT = SmoothStep01(MathF.Abs(forwardSpeed) / 1.5f);
        if (speedT <= 0f)
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

    private float ClampCombinedForce(ref float longitudinal, ref float lateral, float maxForce, float exponent)
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

    private void PublishState(
        VehicleInput input,
        float throttle,
        float brake,
        float handbrake,
        SurfaceSample surface,
        float forwardSpeed,
        float lateralSpeed,
        float localLongitudinalAcceleration,
        float localLateralAcceleration,
        float staticFrontLoad,
        float staticRearLoad,
        float frontLoad,
        float rearLoad,
        float loadTransfer,
        float frontMaxForce,
        float rearMaxForce,
        float frontSlipRadians,
        float rearSlipRadians,
        float driveForceRequest,
        float engineBrakeForceRequest,
        float serviceBrakeForceRequest,
        float handbrakeForceRequest,
        float rollingResistance,
        float aeroDrag,
        float requestedFrontLongitudinal,
        float requestedRearLongitudinal,
        float frontLongitudinal,
        float rearLongitudinal,
        float frontLateral,
        float rearLateral,
        float frontGripUsage,
        float rearGripUsage)
    {
        Vector3 forward3 = State.Forward;
        Vector3 right3 = State.Right;
        Vector2 forward = new(forward3.X, forward3.Z);
        Vector2 right = new(right3.X, right3.Z);
        State.SignedForwardSpeed = Vector2.Dot(State.Velocity, forward);
        State.DisplayedSpeedMetersPerSecond = State.SpeedMetersPerSecond;
        State.LateralSpeed = Vector2.Dot(State.Velocity, right);
        State.LongitudinalAcceleration = localLongitudinalAcceleration;
        State.LateralAcceleration = (lateralSpeed - _previousLateralSpeed) / MathF.Max(0.0001f, _engineParameters.Timing.FixedDeltaSeconds);
        State.PhysicalLoadTransferLongitudinalAcceleration = localLongitudinalAcceleration;
        State.PhysicalLoadTransferLateralAcceleration = localLateralAcceleration;
        State.SurfaceName = surface.Name;
        State.SurfaceGrip = surface.Grip;
        State.Throttle = throttle;
        State.EffectiveThrottle = throttle;
        State.Brake = brake;
        State.Handbrake = handbrake;
        State.Steer = input.Steer;
        State.DriveForce = driveForceRequest;
        State.BrakeForce =
            MathF.Abs(engineBrakeForceRequest) +
            MathF.Abs(serviceBrakeForceRequest) +
            MathF.Abs(handbrakeForceRequest);
        State.FrontBrakeTorqueNm = brake * _parameters.MaxBrakeForceN * _parameters.BrakeBiasFront * _parameters.WheelRadiusMeters;
        State.RearBrakeTorqueNm = (brake * _parameters.MaxBrakeForceN * (1f - _parameters.BrakeBiasFront) +
            handbrake * _parameters.MaxBrakeForceN) * _parameters.WheelRadiusMeters;
        State.RearHandbrakeLockAmount = handbrake;
        State.RearHandbrakeSlideIntensity = handbrake * MathHelper.Clamp(MathF.Abs(State.SignedForwardSpeed) / 12f, 0f, 1f);
        State.RearHandbrakeScreechFactor = surface.HandbrakeScreechFactor;
        State.EngineBrakeTorqueNm = MathF.Abs(engineBrakeForceRequest) > 0.01f && State.Gear != 0
            ? _parameters.EngineBrakeTorqueAtRpm(State.Rpm) * (1f - MathHelper.Clamp(throttle, 0f, 1f))
            : 0f;

        State.FrontLeftLoadN = frontLoad * 0.5f;
        State.FrontRightLoadN = frontLoad * 0.5f;
        State.RearLeftLoadN = rearLoad * 0.5f;
        State.RearRightLoadN = rearLoad * 0.5f;
        State.FrontStaticAxleLoadN = staticFrontLoad;
        State.RearStaticAxleLoadN = staticRearLoad;
        State.LongitudinalLoadTransferN = loadTransfer;
        State.ClassicStaticFrontAxleLoadN = staticFrontLoad;
        State.ClassicStaticRearAxleLoadN = staticRearLoad;
        State.ClassicDynamicFrontAxleLoadN = frontLoad;
        State.ClassicDynamicRearAxleLoadN = rearLoad;
        State.ClassicLongitudinalLoadTransferN = loadTransfer;
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
        State.FrontLeftSurfaceGrip = surface.Grip;
        State.FrontRightSurfaceGrip = surface.Grip;
        State.RearLeftSurfaceGrip = surface.Grip;
        State.RearRightSurfaceGrip = surface.Grip;
        State.FrontLeftSurfaceMu = surface.StaticFrictionCoefficient;
        State.FrontRightSurfaceMu = surface.StaticFrictionCoefficient;
        State.RearLeftSurfaceMu = surface.StaticFrictionCoefficient;
        State.RearRightSurfaceMu = surface.StaticFrictionCoefficient;
        State.FrontLeftSurfaceName = surface.Name;
        State.FrontRightSurfaceName = surface.Name;
        State.RearLeftSurfaceName = surface.Name;
        State.RearRightSurfaceName = surface.Name;

        float frontSlipDegrees = MathHelper.ToDegrees(frontSlipRadians);
        float rearSlipDegrees = MathHelper.ToDegrees(rearSlipRadians);
        State.FrontLeftSlipAngleDegrees = frontSlipDegrees;
        State.FrontRightSlipAngleDegrees = frontSlipDegrees;
        State.RearLeftSlipAngleDegrees = rearSlipDegrees;
        State.RearRightSlipAngleDegrees = rearSlipDegrees;
        State.AverageSlipAngleDegrees = (MathF.Abs(frontSlipDegrees) + MathF.Abs(rearSlipDegrees)) * 0.5f;

        State.FrontLeftRequestedLongitudinalForceN = requestedFrontLongitudinal * 0.5f;
        State.FrontRightRequestedLongitudinalForceN = requestedFrontLongitudinal * 0.5f;
        State.RearLeftRequestedLongitudinalForceN = requestedRearLongitudinal * 0.5f;
        State.RearRightRequestedLongitudinalForceN = requestedRearLongitudinal * 0.5f;
        State.FrontLeftLongitudinalForceN = frontLongitudinal * 0.5f;
        State.FrontRightLongitudinalForceN = frontLongitudinal * 0.5f;
        State.RearLeftLongitudinalForceN = rearLongitudinal * 0.5f;
        State.RearRightLongitudinalForceN = rearLongitudinal * 0.5f;
        State.FrontLeftLateralForceN = frontLateral * 0.5f;
        State.FrontRightLateralForceN = frontLateral * 0.5f;
        State.RearLeftLateralForceN = rearLateral * 0.5f;
        State.RearRightLateralForceN = rearLateral * 0.5f;
        State.FrontLeftGripUsage = frontGripUsage;
        State.FrontRightGripUsage = frontGripUsage;
        State.RearLeftGripUsage = rearGripUsage;
        State.RearRightGripUsage = rearGripUsage;
        State.SteeringFrontGripReserve = 1f - frontGripUsage;

        State.FrontLeftFrictionEllipseGripBudgetN = frontMaxForce * 0.5f;
        State.FrontRightFrictionEllipseGripBudgetN = frontMaxForce * 0.5f;
        State.RearLeftFrictionEllipseGripBudgetN = rearMaxForce * 0.5f;
        State.RearRightFrictionEllipseGripBudgetN = rearMaxForce * 0.5f;
        State.FrontLeftFrictionEllipseGripUsage = frontGripUsage;
        State.FrontRightFrictionEllipseGripUsage = frontGripUsage;
        State.RearLeftFrictionEllipseGripUsage = rearGripUsage;
        State.RearRightFrictionEllipseGripUsage = rearGripUsage;
        State.PeakFrictionEllipseGripUsage = MathF.Max(frontGripUsage, rearGripUsage);

        State.FrontLeftDriveTorqueNm = frontLongitudinal * _parameters.WheelRadiusMeters * 0.5f;
        State.FrontRightDriveTorqueNm = frontLongitudinal * _parameters.WheelRadiusMeters * 0.5f;
        State.RearLeftDriveTorqueNm = rearLongitudinal * _parameters.WheelRadiusMeters * 0.5f;
        State.RearRightDriveTorqueNm = rearLongitudinal * _parameters.WheelRadiusMeters * 0.5f;
        State.FrontDifferentialManagedAxleTorqueNm = MathF.Abs(State.FrontLeftDriveTorqueNm + State.FrontRightDriveTorqueNm);
        State.RearDifferentialManagedAxleTorqueNm = MathF.Abs(State.RearLeftDriveTorqueNm + State.RearRightDriveTorqueNm);

        float frontSlipRatio = frontMaxForce > 1f ? requestedFrontLongitudinal / frontMaxForce : 0f;
        float rearSlipRatio = rearMaxForce > 1f ? requestedRearLongitudinal / rearMaxForce : 0f;
        State.FrontLeftSlipRatio = frontSlipRatio;
        State.FrontRightSlipRatio = frontSlipRatio;
        State.RearLeftSlipRatio = rearSlipRatio;
        State.RearRightSlipRatio = rearSlipRatio;
        State.AverageSlipRatio = (MathF.Abs(frontSlipRatio) + MathF.Abs(rearSlipRatio)) * 0.5f;
        State.PeakRawSlipRatio = MathF.Max(MathF.Abs(frontSlipRatio), MathF.Abs(rearSlipRatio));

        float wheelOmega = State.SignedForwardSpeed / MathF.Max(0.05f, _parameters.WheelRadiusMeters);
        State.FrontLeftWheelOmegaRadiansPerSecond = wheelOmega;
        State.FrontRightWheelOmegaRadiansPerSecond = wheelOmega;
        State.RearLeftWheelOmegaRadiansPerSecond = wheelOmega;
        State.RearRightWheelOmegaRadiansPerSecond = wheelOmega;
        State.BodyRollRadians = -MathHelper.Clamp(State.LateralAcceleration / Gravity, -1.2f, 1.2f) * 0.045f;
        State.BodyPitchRadians = -MathHelper.Clamp(State.LongitudinalAcceleration / Gravity, -1.2f, 1.2f) * 0.035f;
    }

    private void PublishStaticLoadState()
    {
        float frontLoad = _parameters.MassKg * Gravity * MathHelper.Clamp(_parameters.FrontWeightDistribution, 0.05f, 0.95f);
        float rearLoad = _parameters.MassKg * Gravity - frontLoad;
        State.FrontStaticAxleLoadN = frontLoad;
        State.RearStaticAxleLoadN = rearLoad;
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

    private static float EffectiveSlipSpeed(float signedForwardSpeed, float floor)
    {
        float magnitude = MathF.Sqrt(signedForwardSpeed * signedForwardSpeed + MathF.Max(0.1f, floor) * MathF.Max(0.1f, floor));
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
}
