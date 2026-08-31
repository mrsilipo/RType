using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicEquilibriumMatrixProbe
{
    private const float EntrySpeedKmh = 150f;
    private const float EntrySpeedMetersPerSecond = EntrySpeedKmh / 3.6f;
    private const float Dt = 1f / 120f;
    private const int Ticks = 360;
    private const int Gear = 4;

    private static readonly ClassicFourWheelAssistOptions CleanupOff = new()
    {
        BodySlipDampingEnabled = false,
        LateralVelocityDampingEnabled = false,
        RearFollowEnabled = false,
        YawRecoveryEnabled = false,
        SpeedRetentionEnabled = false
    };

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        VehicleAxleGeometry geometry = VehicleAxleGeometry.FromParameters(parameters);

        float currentInertia = engine.ClassicFourWheel.Yaw.InertiaScale;
        float currentDamping = engine.ClassicFourWheel.Yaw.Damping;
        float referenceScale = EstimateReferenceYawInertia(parameters) / MathF.Max(1f, parameters.YawInertiaKgM2);

        Console.WriteLine($"Classic equilibrium matrix probe: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine("  diagnostic-only: cleanup=off, throttle=0.25, gear=4, production values unchanged");
        Console.WriteLine(
            $"  current inertiaScale={currentInertia:F2}, damping={currentDamping:F2}; " +
            $"reference inertia scale ~= {referenceScale:F2} ({EstimateReferenceYawInertia(parameters):F0}/{parameters.YawInertiaKgM2:F0} kgm2)");
        Console.WriteLine(
            $"  reference balance inputs: wheelbase={geometry.WheelbaseMeters:F3}m cgF={geometry.CgToFrontAxleMeters:F3}m cgR={geometry.CgToRearAxleMeters:F3}m " +
            $"Cf={parameters.FrontTyres.CorneringStiffnessNPerRad:F0} Cr={parameters.RearTyres.CorneringStiffnessNPerRad:F0}");

        ProbeResult[] inertiaOnly =
        [
            RunConfig(parameters, engine, geometry, "inertia-current", currentInertia, currentDamping),
            RunConfig(parameters, engine, geometry, "inertia-2.00", 2.00f, currentDamping),
            RunConfig(parameters, engine, geometry, "inertia-ref", referenceScale, currentDamping),
            RunConfig(parameters, engine, geometry, "inertia-1.00-extreme", 1.00f, currentDamping)
        ];
        PrintGroup("inertia only, damping unchanged", inertiaOnly);

        ProbeResult[] dampingOnly =
        [
            RunConfig(parameters, engine, geometry, "damping-current", currentInertia, currentDamping),
            RunConfig(parameters, engine, geometry, "damping-75pct", currentInertia, currentDamping * 0.75f),
            RunConfig(parameters, engine, geometry, "damping-50pct", currentInertia, currentDamping * 0.50f),
            RunConfig(parameters, engine, geometry, "damping-25pct", currentInertia, currentDamping * 0.25f),
            RunConfig(parameters, engine, geometry, "damping-zero-extreme", currentInertia, 0f)
        ];
        PrintGroup("damping only, inertia unchanged", dampingOnly);

        ProbeResult bestInertia = PickBest(inertiaOnly);
        ProbeResult bestDamping = PickBest(dampingOnly);
        ProbeResult[] combined =
        [
            RunConfig(parameters, engine, geometry, "combo-best", bestInertia.InertiaScale, bestDamping.Damping),
            RunConfig(parameters, engine, geometry, "combo-ref-50pct", referenceScale, currentDamping * 0.50f),
            RunConfig(parameters, engine, geometry, "combo-2.00-50pct", 2.00f, currentDamping * 0.50f)
        ];
        PrintGroup("small combined check", combined);

        ProbeResult bestOverall = PickBest([.. inertiaOnly, .. dampingOnly, .. combined]);
        Console.WriteLine($"  best qualitative row: {bestOverall.Label} score={bestOverall.Score:F1} classification={ClassifyOverall(bestOverall)}");
        Console.WriteLine("Classic equilibrium matrix probe complete.");
    }

    private static ProbeResult RunConfig(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters source,
        VehicleAxleGeometry geometry,
        string label,
        float inertiaScale,
        float damping)
    {
        SimulationEngineParameters engine = CloneWithYaw(source, inertiaScale, damping);
        CaseResult medium = RunCase(parameters, engine, geometry, 0.35f);
        CaseResult hard = RunCase(parameters, engine, geometry, 0.65f);
        float score = Score(medium) + Score(hard);
        return new ProbeResult(label, inertiaScale, damping, medium, hard, score);
    }

    private static CaseResult RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        VehicleAxleGeometry geometry,
        float steerInput)
    {
        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engine)
        {
            AssistOptions = CleanupOff
        };
        simulator.SetManualTransmission(true);
        simulator.State.Gear = Gear;
        simulator.State.Velocity = new Vector2(0f, EntrySpeedMetersPerSecond);

        float startSpeed = simulator.State.SpeedMetersPerSecond * 3.6f;
        float yawInertia = MathF.Max(1f, parameters.YawInertiaKgM2 * MathF.Max(0.1f, engine.ClassicFourWheel.Yaw.InertiaScale));
        Sample previous = BuildSample(0f, simulator.State, parameters, engine, geometry, yawInertia, 0f, 0f);
        Sample? at1 = null;
        Sample? at3 = null;
        float maxBeta = 0f;
        float maxRearGrip = 0f;
        float? frontZero = null;
        float? rearSat = null;
        bool equilibriumLike = false;

        for (int i = 0; i < Ticks; i++)
        {
            simulator.Update(new VehicleInput(0.25f, 0f, steerInput), Dt);
            int tick = i + 1;
            Sample current = BuildSample(tick * Dt, simulator.State, parameters, engine, geometry, yawInertia, previous.BetaDegrees, previous.YawRateDegreesPerSecond);
            maxBeta = MathF.Max(maxBeta, MathF.Abs(current.BetaDegrees));
            maxRearGrip = MathF.Max(maxRearGrip, current.RearGripUsage);

            if (frontZero is null &&
                tick > 1 &&
                MathF.Sign(previous.FrontSlipDegrees) != 0f &&
                MathF.Sign(current.FrontSlipDegrees) != 0f &&
                MathF.Sign(previous.FrontSlipDegrees) != MathF.Sign(current.FrontSlipDegrees))
            {
                frontZero = current.TimeSeconds;
            }

            if (rearSat is null && current.RearGripUsage >= 0.98f)
            {
                rearSat = current.TimeSeconds;
            }

            if (!equilibriumLike &&
                current.TimeSeconds > 0.25f &&
                current.FrontSlipDegrees > 0.5f &&
                current.RearSlipDegrees > 0.5f &&
                current.RearGripUsage < 0.90f &&
                MathF.Abs(current.BetaDotDegreesPerSecond) <= 1f &&
                MathF.Abs(current.YawAccelerationDegreesPerSecondSquared) <= 5f)
            {
                equilibriumLike = true;
            }

            if (tick == 120)
            {
                at1 = current;
            }

            if (tick == 360)
            {
                at3 = current;
            }

            previous = current;
        }

        Sample one = at1 ?? previous;
        Sample three = at3 ?? previous;
        return new CaseResult(
            steerInput,
            frontZero,
            maxBeta,
            one.BetaDegrees,
            three.BetaDegrees,
            one.FrontSlipDegrees,
            three.FrontSlipDegrees,
            one.RearSlipDegrees,
            three.RearSlipDegrees,
            maxRearGrip,
            rearSat,
            one.YawRateDegreesPerSecond,
            one.ReferenceYawRateDegreesPerSecond,
            three.YawRateDegreesPerSecond,
            three.ReferenceYawRateDegreesPerSecond,
            MathF.Abs(three.BetaDotDegreesPerSecond) <= 1f,
            MathF.Abs(three.YawAccelerationDegreesPerSecondSquared) <= 5f,
            startSpeed - simulator.State.SpeedMetersPerSecond * 3.6f,
            equilibriumLike);
    }

    private static Sample BuildSample(
        float time,
        VehicleState state,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        VehicleAxleGeometry geometry,
        float yawInertia,
        float previousBetaDegrees,
        float previousYawRateDegrees)
    {
        float frontMoment =
            CalculateMoment(-geometry.FrontTrackMeters * 0.5f, geometry.CgToFrontAxleMeters, state.FrontLeftLongitudinalForceN, state.FrontLeftLateralForceN) +
            CalculateMoment(geometry.FrontTrackMeters * 0.5f, geometry.CgToFrontAxleMeters, state.FrontRightLongitudinalForceN, state.FrontRightLateralForceN);
        float rearMoment =
            CalculateMoment(-geometry.RearTrackMeters * 0.5f, -geometry.CgToRearAxleMeters, state.RearLeftLongitudinalForceN, state.RearLeftLateralForceN) +
            CalculateMoment(geometry.RearTrackMeters * 0.5f, -geometry.CgToRearAxleMeters, state.RearRightLongitudinalForceN, state.RearRightLateralForceN);
        float yawAcceleration = (frontMoment + rearMoment) / yawInertia -
            state.YawRateRadiansPerSecond * MathF.Max(0f, engine.ClassicFourWheel.Yaw.Damping);
        float yawRate = MathHelper.ToDegrees(state.YawRateRadiansPerSecond);
        ReferenceSnapshot reference = CalculateReference(parameters, geometry, state.SpeedMetersPerSecond, MathHelper.ToRadians(state.FrontLeftSteerAngleDegrees));

        return new Sample(
            time,
            state.ClassicBodySlipAngleDegrees,
            time <= 0f ? 0f : (state.ClassicBodySlipAngleDegrees - previousBetaDegrees) / Dt,
            yawRate,
            time <= 0f ? 0f : (yawRate - previousYawRateDegrees) / Dt,
            MathHelper.ToDegrees(yawAcceleration),
            (state.FrontLeftSlipAngleDegrees + state.FrontRightSlipAngleDegrees) * 0.5f,
            (state.RearLeftSlipAngleDegrees + state.RearRightSlipAngleDegrees) * 0.5f,
            MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage),
            reference.YawRateDegreesPerSecond);
    }

    private static void PrintGroup(string title, IReadOnlyList<ProbeResult> results)
    {
        Console.WriteLine($"  {title}");
        Console.WriteLine("    label inert damp case zeroF maxBeta beta1/3 slipF1/3 slipR1/3 rearMax sat yaw1/ref yaw3/ref settleB/Y speed healthy score");
        foreach (ProbeResult result in results)
        {
            PrintRow(result, "med", result.Medium);
            PrintRow(result, "hard", result.Hard);
        }
    }

    private static void PrintRow(ProbeResult result, string caseLabel, CaseResult c)
    {
        Console.WriteLine(
            $"    {result.Label,-18} {result.InertiaScale,4:F2} {result.Damping,4:F2} {caseLabel,-4} " +
            $"{FormatTime(c.FrontSlipZeroTime),5} {c.MaxBetaDegrees,7:F1} {c.BetaAtOneSecondDegrees,5:F1}/{c.BetaAtThreeSecondsDegrees,5:F1} " +
            $"{c.FrontSlipAtOneSecondDegrees,6:F1}/{c.FrontSlipAtThreeSecondsDegrees,5:F1} " +
            $"{c.RearSlipAtOneSecondDegrees,6:F1}/{c.RearSlipAtThreeSecondsDegrees,5:F1} " +
            $"{c.MaxRearGripUsage,6:F2} {FormatTime(c.RearSaturationTime),5} " +
            $"{c.YawRateAtOneSecondDegreesPerSecond,6:F1}/{c.ReferenceYawRateAtOneSecondDegreesPerSecond,5:F1} " +
            $"{c.YawRateAtThreeSecondsDegreesPerSecond,6:F1}/{c.ReferenceYawRateAtThreeSecondsDegreesPerSecond,5:F1} " +
            $"{(c.BetaSettled ? "Y" : "N")}/{(c.YawSettled ? "Y" : "N")} {c.SpeedLossKmh,5:F1} {(c.HealthyEquilibriumSeen ? "yes" : "no "),3} {Score(c),5:F1}");
    }

    private static string FormatTime(float? time)
    {
        return time is null ? "never" : time.Value.ToString("F2");
    }

    private static ProbeResult PickBest(IReadOnlyList<ProbeResult> results)
    {
        ProbeResult best = results[0];
        foreach (ProbeResult result in results)
        {
            if (result.Score > best.Score)
            {
                best = result;
            }
        }

        return best;
    }

    private static float Score(CaseResult c)
    {
        float score = 0f;
        if (c.HealthyEquilibriumSeen)
        {
            score += 50f;
        }

        if (c.FrontSlipZeroTime is null)
        {
            score += 20f;
        }
        else
        {
            score += MathHelper.Clamp(c.FrontSlipZeroTime.Value, 0f, 1f) * 8f;
        }

        if (c.RearSaturationTime is null)
        {
            score += 15f;
        }
        else
        {
            score += MathHelper.Clamp(c.RearSaturationTime.Value, 0f, 1f) * 5f;
        }

        if (c.FrontSlipAtThreeSecondsDegrees > 0.5f)
        {
            score += 10f;
        }

        if (c.RearSlipAtThreeSecondsDegrees > 0.5f)
        {
            score += 10f;
        }

        if (c.MaxRearGripUsage < 0.90f)
        {
            score += 10f;
        }

        if (c.BetaSettled)
        {
            score += 5f;
        }

        if (c.YawSettled)
        {
            score += 5f;
        }

        score -= MathF.Max(0f, c.MaxBetaDegrees - 12f) * 1.5f;
        return score;
    }

    private static string ClassifyOverall(ProbeResult best)
    {
        if (best.Medium.HealthyEquilibriumSeen && best.Hard.HealthyEquilibriumSeen)
        {
            return "yaw resistance alone can produce a healthy equilibrium candidate";
        }

        if (best.Medium.FrontSlipZeroTime is not null || best.Hard.FrontSlipZeroTime is not null)
        {
            return "yaw resistance changes do not prevent front-slip reversal; move next to front/rear lateral balance";
        }

        return "yaw resistance changes avoid reversal but still do not meet full healthy-equilibrium criteria";
    }

    private static float CalculateMoment(float right, float forward, float forwardForce, float rightForce)
    {
        return right * forwardForce - forward * rightForce;
    }

    private static float EstimateReferenceYawInertia(VehicleSimulationParameters parameters)
    {
        float length = MathF.Max(parameters.WheelbaseMeters * 1.45f, parameters.BodyLengthMeters);
        float width = MathF.Max(parameters.FrontTrackMeters, parameters.BodyWidthMeters);
        return parameters.MassKg * (length * length + width * width) / 12f;
    }

    private static ReferenceSnapshot CalculateReference(
        VehicleSimulationParameters parameters,
        VehicleAxleGeometry geometry,
        float speed,
        float steerRadians)
    {
        if (MathF.Abs(steerRadians) <= 0.0001f)
        {
            return new ReferenceSnapshot(0f);
        }

        float mass = MathF.Max(1f, parameters.MassKg);
        float cf = MathF.Max(1f, parameters.FrontTyres.CorneringStiffnessNPerRad);
        float cr = MathF.Max(1f, parameters.RearTyres.CorneringStiffnessNPerRad);
        float a = geometry.CgToFrontAxleMeters;
        float b = geometry.CgToRearAxleMeters;
        float safeSpeed = MathF.Max(0.1f, speed);

        float a11 = -cf - cr;
        float a12 = (-cf * a + cr * b) / safeSpeed - mass * safeSpeed;
        float b1 = -cf * steerRadians;
        float a21 = -a * cf + b * cr;
        float a22 = -(a * a * cf + b * b * cr) / safeSpeed;
        float b2 = -a * cf * steerRadians;
        float det = a11 * a22 - a12 * a21;
        if (MathF.Abs(det) <= 0.001f)
        {
            return new ReferenceSnapshot(0f);
        }

        float yawRate = (a11 * b2 - b1 * a21) / det;
        return new ReferenceSnapshot(MathHelper.ToDegrees(yawRate));
    }

    private static SimulationEngineParameters CloneWithYaw(
        SimulationEngineParameters source,
        float inertiaScale,
        float damping)
    {
        return new SimulationEngineParameters
        {
            HandlingModel = source.HandlingModel,
            Timing = source.Timing,
            VehicleSafety = source.VehicleSafety,
            StabilityAssist = source.StabilityAssist,
            DigitalThrottleAssist = source.DigitalThrottleAssist,
            DigitalBrakeAssist = source.DigitalBrakeAssist,
            BrakeThrottlePriority = source.BrakeThrottlePriority,
            SteeringAssist = source.SteeringAssist,
            TyreForce = source.TyreForce,
            RpmResponse = source.RpmResponse,
            ClassicBicycle = source.ClassicBicycle,
            ClassicFourWheel = new ClassicBicycleParameters
            {
                Steering = source.ClassicFourWheel.Steering,
                FrontTyres = source.ClassicFourWheel.FrontTyres,
                RearTyres = source.ClassicFourWheel.RearTyres,
                Yaw = new ClassicBicycleYawParameters
                {
                    InertiaScale = inertiaScale,
                    Damping = damping,
                    LateralVelocityDamping = source.ClassicFourWheel.Yaw.LateralVelocityDamping
                },
                GripBudget = source.ClassicFourWheel.GripBudget,
                ChassisLoadTransfer = source.ClassicFourWheel.ChassisLoadTransfer,
                LowSpeed = source.ClassicFourWheel.LowSpeed,
                Resistance = source.ClassicFourWheel.Resistance
            }
        };
    }

    private readonly record struct ProbeResult(
        string Label,
        float InertiaScale,
        float Damping,
        CaseResult Medium,
        CaseResult Hard,
        float Score);

    private readonly record struct CaseResult(
        float SteerInput,
        float? FrontSlipZeroTime,
        float MaxBetaDegrees,
        float BetaAtOneSecondDegrees,
        float BetaAtThreeSecondsDegrees,
        float FrontSlipAtOneSecondDegrees,
        float FrontSlipAtThreeSecondsDegrees,
        float RearSlipAtOneSecondDegrees,
        float RearSlipAtThreeSecondsDegrees,
        float MaxRearGripUsage,
        float? RearSaturationTime,
        float YawRateAtOneSecondDegreesPerSecond,
        float ReferenceYawRateAtOneSecondDegreesPerSecond,
        float YawRateAtThreeSecondsDegreesPerSecond,
        float ReferenceYawRateAtThreeSecondsDegreesPerSecond,
        bool BetaSettled,
        bool YawSettled,
        float SpeedLossKmh,
        bool HealthyEquilibriumSeen);

    private readonly record struct Sample(
        float TimeSeconds,
        float BetaDegrees,
        float BetaDotDegreesPerSecond,
        float YawRateDegreesPerSecond,
        float MeasuredYawAccelerationDegreesPerSecondSquared,
        float YawAccelerationDegreesPerSecondSquared,
        float FrontSlipDegrees,
        float RearSlipDegrees,
        float RearGripUsage,
        float ReferenceYawRateDegreesPerSecond);

    private readonly record struct ReferenceSnapshot(float YawRateDegreesPerSecond);

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
