namespace RetroRacer.Vehicle;

public sealed class VehicleSimulationParameters
{
    public string Id { get; init; } = "prototype_default";

    public string DisplayName { get; init; } = "Prototype Car";

    public float MassKg { get; init; } = 1125f;

    public float WheelbaseMeters { get; init; } = 2.55f;

    public float FrontTrackMeters { get; init; } = 1.48f;

    public float RearTrackMeters { get; init; } = 1.48f;

    public float BodyLengthMeters { get; init; } = 4.2f;

    public float BodyWidthMeters { get; init; } = 1.7f;

    public float FrontWeightDistribution { get; init; } = 0.58f;

    public float CenterOfGravityHeightMeters { get; init; } = 0.48f;

    public float YawInertiaKgM2 { get; init; } = 1450f;

    public float WheelRadiusMeters { get; init; } = 0.33f;

    public float FinalDriveRatio { get; init; } = 3.85f;

    public float DrivetrainEfficiency { get; init; } = 0.82f;

    public float ClosedThrottleEngineBrakeTorqueNm { get; init; } = 32f;

    public float IdleRpm { get; init; } = 900f;

    public float RedlineRpm { get; init; } = 6800f;

    public float RevLimiterResumeRpm { get; init; } = 6620f;

    public float RevLimiterFuelCutSeconds { get; init; } = 0.08f;

    public float RevLimiterRestoreSeconds { get; init; } = 0.05f;

    public float RevLimiterCutTorqueMultiplier { get; init; } = 0.08f;

    public float RevLimiterBounceRpm { get; init; } = 140f;

    public float EngineRotationalInertiaKgM2 { get; init; } = 0.22f;

    public bool VtecEnabled { get; init; }

    public float VtecActivationRpm { get; init; } = 5800f;

    public float VtecTransitionWidthRpm { get; init; } = 650f;

    public float VtecLowCamFlowMultiplier { get; init; } = 1f;

    public float VtecHighCamFlowMultiplier { get; init; } = 1.08f;

    public bool EngineSimulatorDrivesPhysics { get; init; }

    public bool EngineSimulatorFullDriveline { get; init; }

    public float EngineSimulatorPhysicsSimulationFrequencyHz { get; init; } = 1000f;

    public int EngineSimulatorPhysicsFluidSimulationSteps { get; init; } = 2;

    public float EngineSimulatorPhysicsTorqueScale { get; init; } = 1f;

    public float EngineSimulatorPhysicsTorqueBlend { get; init; }

    public bool EngineSimulatorPhysicsUseReferenceTorqueCalibration { get; init; }

    public float EngineSimulatorPhysicsEngineBrakeScale { get; init; } = 1f;

    public float EngineSimulatorPhysicsEngineBrakeBlend { get; init; }

    public float EngineSimulatorPhysicsMaxTorqueNm { get; init; } = 220f;

    public float EngineSimulatorPhysicsMaxEngineBrakeTorqueNm { get; init; } = 120f;

    public float ClutchTorqueCapacityNm { get; init; } = 250f;

    public float ClutchEngagementPoint { get; init; } = 0.55f;

    public float ClutchCouplingRate { get; init; } = 10f;

    public float EngineFreeRevResponseRate { get; init; } = 7f;

    public float LaunchSlipTargetRpm { get; init; } = 3800f;

    public float LaunchSlipBlend { get; init; } = 0.3f;

    public float UpshiftRpm { get; init; } = 6250f;

    public float DownshiftRpm { get; init; } = 2350f;

    public float AutomaticMinimumUpshiftSpeedMetersPerSecond { get; init; } = 5f;

    public float ManualShiftTimeSeconds { get; init; } = 0.32f;

    public float AutomaticShiftTimeSeconds { get; init; } = 0.18f;

    public float DownshiftOverRevToleranceRpm { get; init; } = 250f;

    public float DownshiftMechanicalOverRevLimitRpm { get; init; }

    public float DownshiftOverRevBrakeMultiplier { get; init; } = 2.35f;

    public float DownshiftOverRevShockSeconds { get; init; } = 0.38f;

    public float[] ForwardGearRatios { get; init; } = [3.20f, 2.12f, 1.52f, 1.15f, 0.92f];

    public float ReverseGearRatio { get; init; } = 2.85f;

    public float MaxBrakeForceN { get; init; } = 11500f;

    public float BrakeBiasFront { get; init; } = 0.67f;

    public BrakeSystemParameters Brakes { get; init; } = new();

    public float AeroDragFactor { get; init; } = 0.43f;

    public float FrontLiftFactor { get; init; }

    public float RearLiftFactor { get; init; }

    public float RollingResistanceCoefficient { get; init; } = 0.013f;

    public float LateralGripResponse { get; init; } = 8.5f;

    public ArcadeHandlingParameters ArcadeHandling { get; init; } = new();

    public float SteeringRatio { get; init; } = 15f;

    public float SteeringWheelLockDegrees { get; init; } = 960f;

    public float SteeringInputRatePerSecond { get; init; } = 4.5f;

    public float SteeringReturnRatePerSecond { get; init; } = 7.0f;

    public float SteeringHighSpeedInputRateMultiplier { get; init; } = 0.35f;

    public float SteeringHighSpeedReturnRateMultiplier { get; init; } = 0.55f;

    public float SteeringFullLockSpeedMetersPerSecond { get; init; }

    public float SteeringReducedLockSpeedMetersPerSecond { get; init; } = 52.8f;

    public float SteeringHighSpeedLockMultiplier { get; init; } = 0.48f;

    public float SteeringTargetLateralAccelerationG { get; init; } = 0.88f;

    public float SteeringPeakSlipAngleFraction { get; init; } = 0.68f;

    public float SteeringLowSpeedReferenceMetersPerSecond { get; init; } = 6.0f;

    public float SteeringMinimumHighSpeedAngleRadians { get; init; } = 0.075f;

    public float MaxSteerAngleRadians { get; init; } = 0.55f;

    public float AckermannPercent { get; init; } = 100f;

    public float FrontSpringRateNPerM { get; init; } = 33000f;

    public float RearSpringRateNPerM { get; init; } = 25000f;

    public float FrontAntiRollBarRateNmPerRad { get; init; } = 15000f;

    public float RearAntiRollBarRateNmPerRad { get; init; } = 10500f;

    public SuspensionGeometryParameters FrontSuspensionGeometry { get; init; } = new();

    public SuspensionGeometryParameters RearSuspensionGeometry { get; init; } = new();

    public float DifferentialTorqueBiasRatio { get; init; } = 1f;

    public float WheelInertiaKgM2 { get; init; } = 0.85f;

    public DrivenWheelSet DrivenWheels { get; init; } = new(true, true, false, false);

    public TyreAxleParameters FrontTyres { get; init; } = new();

    public TyreAxleParameters RearTyres { get; init; } = new();

    public VehicleAudioParameters Audio { get; init; } = new();

    public float WallCollisionPointRadiusMeters { get; init; } = 0.08f;

    public float WallCollisionRestitution { get; init; } = 0.12f;

    public float WallImpactFriction { get; init; } = 0.24f;

    public float WallScrapeFriction { get; init; } = 0.045f;

    public float WallYawImpulseScale { get; init; } = 0.55f;

    public TorqueCurvePoint[] TorqueCurve { get; init; } =
    [
        new(1000f, 95f),
        new(2000f, 118f),
        new(3000f, 135f),
        new(4000f, 148f),
        new(5000f, 160f),
        new(6000f, 168f),
        new(7000f, 162f),
        new(7500f, 160f),
        new(8200f, 150f)
    ];

    public TorqueCurvePoint[] EngineBrakeTorqueCurve { get; init; } =
    [
        new(1000f, 12f),
        new(3000f, 24f),
        new(6000f, 42f),
        new(8000f, 56f)
    ];

    public float TorqueAtRpm(float rpm)
    {
        return TorqueFromCurve(TorqueCurve, rpm);
    }

    public float EngineBrakeTorqueAtRpm(float rpm)
    {
        return TorqueFromCurve(EngineBrakeTorqueCurve, rpm);
    }

    private static float TorqueFromCurve(TorqueCurvePoint[] curve, float rpm)
    {
        if (curve.Length == 0)
        {
            return 0f;
        }

        if (rpm <= curve[0].Rpm)
        {
            return curve[0].TorqueNm;
        }

        for (int i = 1; i < curve.Length; i++)
        {
            TorqueCurvePoint previous = curve[i - 1];
            TorqueCurvePoint next = curve[i];
            if (rpm <= next.Rpm)
            {
                float t = (rpm - previous.Rpm) / MathF.Max(1f, next.Rpm - previous.Rpm);
                return previous.TorqueNm + (next.TorqueNm - previous.TorqueNm) * t;
            }
        }

        return curve[^1].TorqueNm;
    }
}
