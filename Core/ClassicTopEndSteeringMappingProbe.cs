using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicTopEndSteeringMappingProbe
{
    private const float Dt = 1f / 120f;
    private const int Gear = 4;
    private const float StartSpeedKmh = 150f;
    private const float RunSeconds = 3.0f;

    private static readonly float[] Commands = [0.60f, 0.80f, 1.00f];

    private static readonly TopEndVariant[] Variants =
    [
        new("baseline", null, null, null, null, 1.00f),
        new("overSlip-022", null, 0.22f, null, null, 1.00f),
        new("overSlip-020", null, 0.20f, null, null, 1.00f),
        new("overSlip-018", null, 0.18f, null, null, 1.00f),
        new("overSlip-014", null, 0.14f, null, null, 1.00f),
        new("overSlip-012", null, 0.12f, null, null, 1.00f),
        new("transient-half", null, null, 0.16f, null, 1.00f),
        new("transient-off", null, null, 0.00f, null, 1.00f),
        new("overG-125", null, null, null, 1.25f, 1.00f),
        new("overG-115", null, null, null, 1.15f, 1.00f),
        new("slip012-trans016", null, 0.12f, 0.16f, null, 1.00f),
        new("topGain-075", null, null, null, null, 0.75f),
        new("topGain-060", null, null, null, null, 0.60f),
        new("slip012-gain075", null, 0.12f, null, null, 0.75f)
    ];

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters baseEngine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        VehicleAxleGeometry geometry = VehicleAxleGeometry.FromParameters(parameters);

        Console.WriteLine($"Classic top-end steering mapping probe: {parameters.DisplayName}, model={baseEngine.HandlingModel}");
        Console.WriteLine("  diagnostic-only: low/mid control retained, tyres/yaw/load/brake/cleanup unchanged");
        Console.WriteLine("  sequence: coast-turn, 150km/h, gear=4, compare commands 0.60/0.80/1.00 after 3.0s");
        Console.WriteLine("  normalized errors are relative to a steady-state reference using each run's actual final road-wheel angle.");
        Console.WriteLine("  score penalizes beta excess, rear-slip excess, front-slip deficit, yaw loss vs reference, and rear saturation.");
        Console.WriteLine();
        Console.WriteLine("  variant           overG overSlip trans topGain score  0.60 slipF/R beta yaw   0.80 slipF/R beta yaw   1.00 slipF/R beta yaw");

        List<VariantResult> results = [];
        foreach (TopEndVariant variant in Variants)
        {
            SimulationEngineParameters engine = CloneWithVariant(baseEngine, variant);
            List<CaseResult> cases = [];
            foreach (float command in Commands)
            {
                cases.Add(RunCase(parameters, engine, geometry, variant, command));
            }

            VariantResult result = new(variant, cases, Score(cases));
            results.Add(result);
            PrintVariantLine(result);
        }

        Console.WriteLine();
        Console.WriteLine("  detailed 0.80/1.00 cases:");
        Console.WriteLine("    variant cmd sent road n/o/slip/boost yaw/ref beta/ref slipF/ref slipR/ref rearGrip latG scoreBits");
        foreach (VariantResult result in results.OrderBy(r => r.Score).Take(6))
        {
            foreach (CaseResult c in result.Cases.Where(c => c.Command >= 0.80f))
            {
                Console.WriteLine(
                    $"    {result.Variant.Label,-16} {c.Command:F2} {c.SentCommand:F2} {c.RoadWheelAngleDegrees,5:F2} " +
                    $"{c.NormalAngleDegrees:F2}/{c.OverdriveAngleDegrees:F2}/{c.SlipAllowanceDegrees:F2}/{c.TransientBoostAngleDegrees:F2} " +
                    $"{c.NormalizedYawRateDegreesPerSecond,5:F1}/{c.ReferenceYawRateDegreesPerSecond,5:F1} " +
                    $"{c.NormalizedBetaDegrees,6:F2}/{c.ReferenceBetaDegrees,6:F2} " +
                    $"{c.NormalizedFrontSlipDegrees,6:F2}/{c.ReferenceFrontSlipDegrees,6:F2} " +
                    $"{c.NormalizedRearSlipDegrees,6:F2}/{c.ReferenceRearSlipDegrees,6:F2} " +
                    $"{c.RearGripUsage,5:F2} {c.LateralG,5:F2} {DescribeScoreBits(c)}");
            }
        }

        PrintClassification(results);
        Console.WriteLine("Classic top-end steering mapping probe complete.");
    }

    private static CaseResult RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        VehicleAxleGeometry geometry,
        TopEndVariant variant,
        float command)
    {
        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engine);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = Gear;
        simulator.State.Velocity = new Vector2(0f, StartSpeedKmh / 3.6f);

        float sentCommand = MapTopCommand(command, variant.TopCommandGain);
        for (int i = 0; i < SecondsToTicks(RunSeconds); i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, sentCommand, brakeAssistEnabled: true), Dt);
        }

        VehicleState state = simulator.State;
        float roadAngle = (state.FrontLeftSteerAngleDegrees + state.FrontRightSteerAngleDegrees) * 0.5f;
        ReferenceSnapshot reference = CalculateReference(
            parameters,
            geometry,
            state.SpeedMetersPerSecond,
            MathHelper.ToRadians(roadAngle));
        float roadSign = MathF.Sign(roadAngle);
        if (roadSign == 0f)
        {
            roadSign = MathF.Sign(command);
        }

        if (roadSign == 0f)
        {
            roadSign = 1f;
        }

        float simTurnMultiplier = -roadSign;
        float referenceTurnMultiplier = roadSign;
        float normalizedYaw = MathHelper.ToDegrees(state.YawRateRadiansPerSecond) * simTurnMultiplier;
        float normalizedBeta = state.ClassicBodySlipAngleDegrees * simTurnMultiplier;
        float normalizedFrontSlip = (state.FrontLeftSlipAngleDegrees + state.FrontRightSlipAngleDegrees) * 0.5f * simTurnMultiplier;
        float normalizedRearSlip = (state.RearLeftSlipAngleDegrees + state.RearRightSlipAngleDegrees) * 0.5f * simTurnMultiplier;

        return new CaseResult(
            variant.Label,
            command,
            sentCommand,
            state.SpeedMetersPerSecond * 3.6f,
            roadAngle,
            state.SteeringPhysicalNormalAngleDegrees,
            state.SteeringPhysicalOverdriveAngleDegrees,
            MathF.Max(0f, MathF.Abs(roadAngle) -
                state.SteeringPhysicalNormalAngleDegrees -
                state.SteeringTransientBoostAngleDegrees),
            state.SteeringTransientBoostAngleDegrees,
            normalizedYaw,
            reference.YawRateDegreesPerSecond * referenceTurnMultiplier,
            normalizedBeta,
            reference.BetaDegrees * referenceTurnMultiplier,
            normalizedFrontSlip,
            reference.FrontSlipDegrees * referenceTurnMultiplier,
            normalizedRearSlip,
            reference.RearSlipDegrees * referenceTurnMultiplier,
            MathF.Max(state.FrontLeftGripUsage, state.FrontRightGripUsage),
            MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage),
            state.LateralAcceleration / 9.81f);
    }

    private static float MapTopCommand(float command, float topCommandGain)
    {
        const float unchangedStart = 0.70f;
        float sign = MathF.Sign(command);
        float magnitude = MathF.Abs(command);
        if (magnitude <= unchangedStart)
        {
            return command;
        }

        return sign * (unchangedStart + (magnitude - unchangedStart) * MathHelper.Clamp(topCommandGain, 0.25f, 1f));
    }

    private static void PrintVariantLine(VariantResult result)
    {
        TopEndVariant v = result.Variant;
        string samples = string.Join("   ", result.Cases.Select(c =>
            $"{c.NormalizedFrontSlipDegrees:F2}/{c.NormalizedRearSlipDegrees:F2} {c.NormalizedBetaDegrees:F2} {c.NormalizedYawRateDegreesPerSecond:F1}"));
        Console.WriteLine(
            $"  {v.Label,-16} {FormatOptional(v.OverdriveGOverride),5} {FormatOptional(v.OverdriveSlipFractionOverride),8} " +
            $"{FormatOptional(v.TransientSlipFractionOverride),5} {v.TopCommandGain,7:F2} {result.Score,5:F1}  {samples}");
    }

    private static void PrintClassification(IReadOnlyList<VariantResult> results)
    {
        VariantResult baseline = results.First(r => r.Variant.Label == "baseline");
        VariantResult best = results.OrderBy(r => r.Score).First();
        Console.WriteLine();
        Console.WriteLine("  classification:");
        Console.WriteLine($"    baseline score={baseline.Score:F1}; best={best.Variant.Label} score={best.Score:F1}");

        CaseResult baselineFull = baseline.Cases.First(c => MathF.Abs(c.Command - 1f) < 0.01f);
        CaseResult bestFull = best.Cases.First(c => MathF.Abs(c.Command - 1f) < 0.01f);
        Console.WriteLine(
            $"    full-command change: yaw {baselineFull.NormalizedYawRateDegreesPerSecond:F1}->{bestFull.NormalizedYawRateDegreesPerSecond:F1} " +
            $"ref {baselineFull.ReferenceYawRateDegreesPerSecond:F1}->{bestFull.ReferenceYawRateDegreesPerSecond:F1}, " +
            $"beta {baselineFull.NormalizedBetaDegrees:F2}->{bestFull.NormalizedBetaDegrees:F2}, " +
            $"frontSlip {baselineFull.NormalizedFrontSlipDegrees:F2}->{bestFull.NormalizedFrontSlipDegrees:F2}, " +
            $"rearSlip {baselineFull.NormalizedRearSlipDegrees:F2}->{bestFull.NormalizedRearSlipDegrees:F2}");

        if (best.Variant.OverdriveSlipFractionOverride.HasValue &&
            best.Variant.TopCommandGain >= 0.99f &&
            !best.Variant.TransientSlipFractionOverride.HasValue &&
            !best.Variant.OverdriveGOverride.HasValue)
        {
            Console.WriteLine("    best isolated lever: overdrive slip allowance reduction");
        }
        else if (best.Variant.TransientSlipFractionOverride.HasValue &&
            best.Variant.TopCommandGain >= 0.99f &&
            !best.Variant.OverdriveSlipFractionOverride.HasValue &&
            !best.Variant.OverdriveGOverride.HasValue)
        {
            Console.WriteLine("    best isolated lever: transient boost reduction");
        }
        else if (best.Variant.OverdriveGOverride.HasValue &&
            best.Variant.TopCommandGain >= 0.99f &&
            !best.Variant.OverdriveSlipFractionOverride.HasValue &&
            !best.Variant.TransientSlipFractionOverride.HasValue)
        {
            Console.WriteLine("    best isolated lever: overdrive lateral-g target reduction");
        }
        else if (best.Variant.TopCommandGain < 0.99f &&
            !best.Variant.OverdriveSlipFractionOverride.HasValue &&
            !best.Variant.TransientSlipFractionOverride.HasValue &&
            !best.Variant.OverdriveGOverride.HasValue)
        {
            Console.WriteLine("    best isolated lever: high-command remap/compression");
        }
        else
        {
            Console.WriteLine("    best result is combined; inspect isolated variants before production tuning");
        }
    }

    private static float Score(IEnumerable<CaseResult> cases)
    {
        float score = 0f;
        foreach (CaseResult c in cases)
        {
            float frontDeficit = MathF.Max(0f, c.ReferenceFrontSlipDegrees - c.NormalizedFrontSlipDegrees);
            float rearExcess = MathF.Max(0f, c.NormalizedRearSlipDegrees - c.ReferenceRearSlipDegrees);
            float betaExcess = MathF.Max(0f, MathF.Abs(c.NormalizedBetaDegrees) - MathF.Abs(c.ReferenceBetaDegrees));
            float yawDeficit = MathF.Max(0f, c.ReferenceYawRateDegreesPerSecond * 0.90f - c.NormalizedYawRateDegreesPerSecond);
            float yawOvershoot = MathF.Max(0f, c.NormalizedYawRateDegreesPerSecond - c.ReferenceYawRateDegreesPerSecond * 1.20f);
            float commandWeight = c.Command <= 0.60f ? 0.35f : c.Command;

            score += commandWeight * (frontDeficit * 2.0f + rearExcess * 1.3f + betaExcess * 1.4f);
            score += commandWeight * (yawDeficit * 1.2f + yawOvershoot * 0.8f);
            score += MathF.Max(0f, c.RearGripUsage - 0.80f) * 20f;
            if (c.Command >= 0.80f && c.NormalizedYawRateDegreesPerSecond < c.ReferenceYawRateDegreesPerSecond * 0.75f)
            {
                score += 10f;
            }
        }

        return score;
    }

    private static string DescribeScoreBits(CaseResult c)
    {
        float frontDeficit = MathF.Max(0f, c.ReferenceFrontSlipDegrees - c.NormalizedFrontSlipDegrees);
        float rearExcess = MathF.Max(0f, c.NormalizedRearSlipDegrees - c.ReferenceRearSlipDegrees);
        float betaExcess = MathF.Max(0f, MathF.Abs(c.NormalizedBetaDegrees) - MathF.Abs(c.ReferenceBetaDegrees));
        float yawDeficit = MathF.Max(0f, c.ReferenceYawRateDegreesPerSecond * 0.90f - c.NormalizedYawRateDegreesPerSecond);
        return $"frontDef={frontDeficit:F2} rearEx={rearExcess:F2} betaEx={betaExcess:F2} yawDef={yawDeficit:F1}";
    }

    private static SimulationEngineParameters CloneWithVariant(SimulationEngineParameters source, TopEndVariant variant)
    {
        ClassicBicycleSteeringParameters steering = source.ClassicFourWheel.Steering;
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
                Steering = new ClassicBicycleSteeringParameters
                {
                    ZeroKmhAngleDegrees = steering.ZeroKmhAngleDegrees,
                    SixtyKmhAngleDegrees = steering.SixtyKmhAngleDegrees,
                    OneTwentyKmhAngleDegrees = steering.OneTwentyKmhAngleDegrees,
                    TwoHundredKmhAngleDegrees = steering.TwoHundredKmhAngleDegrees,
                    SteerSpeedDegreesPerSecond = steering.SteerSpeedDegreesPerSecond,
                    ReturnSpeedDegreesPerSecond = steering.ReturnSpeedDegreesPerSecond,
                    PhysicalEnvelopeBlendStartKmh = steering.PhysicalEnvelopeBlendStartKmh,
                    PhysicalEnvelopeFullKmh = steering.PhysicalEnvelopeFullKmh,
                    NormalLateralAccelerationG = steering.NormalLateralAccelerationG,
                    OverdriveLateralAccelerationG = variant.OverdriveGOverride ?? steering.OverdriveLateralAccelerationG,
                    NormalCommand = steering.NormalCommand,
                    MinimumHighSpeedAngleDegrees = steering.MinimumHighSpeedAngleDegrees,
                    NormalPeakSlipFraction = steering.NormalPeakSlipFraction,
                    OverdrivePeakSlipFraction = variant.OverdriveSlipFractionOverride ?? steering.OverdrivePeakSlipFraction,
                    TransientPeakSlipFraction = variant.TransientSlipFractionOverride ?? steering.TransientPeakSlipFraction,
                    TransientBoostSeconds = steering.TransientBoostSeconds,
                    DigitalInitialCommandRatePerSecond = steering.DigitalInitialCommandRatePerSecond,
                    DigitalSustainedCommandRatePerSecond = steering.DigitalSustainedCommandRatePerSecond,
                    DigitalRiseAccelerationSeconds = steering.DigitalRiseAccelerationSeconds,
                    DigitalReleaseCommandRatePerSecond = steering.DigitalReleaseCommandRatePerSecond,
                    DigitalCounterSteerRateMultiplier = steering.DigitalCounterSteerRateMultiplier
                },
                FrontTyres = source.ClassicFourWheel.FrontTyres,
                RearTyres = source.ClassicFourWheel.RearTyres,
                Yaw = source.ClassicFourWheel.Yaw,
                GripBudget = source.ClassicFourWheel.GripBudget,
                ChassisLoadTransfer = source.ClassicFourWheel.ChassisLoadTransfer,
                LowSpeed = source.ClassicFourWheel.LowSpeed,
                Resistance = source.ClassicFourWheel.Resistance
            }
        };
    }

    private static ReferenceSnapshot CalculateReference(
        VehicleSimulationParameters parameters,
        VehicleAxleGeometry geometry,
        float speed,
        float steerRadians)
    {
        if (MathF.Abs(steerRadians) <= 0.0001f)
        {
            return new ReferenceSnapshot(0f, 0f, 0f, 0f, false);
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
            return new ReferenceSnapshot(0f, 0f, 0f, 0f, false);
        }

        float beta = (b1 * a22 - a12 * b2) / det;
        float yawRate = (a11 * b2 - b1 * a21) / det;
        float frontSlip = steerRadians - beta - a * yawRate / safeSpeed;
        float rearSlip = -beta + b * yawRate / safeSpeed;
        return new ReferenceSnapshot(
            MathHelper.ToDegrees(yawRate),
            MathHelper.ToDegrees(beta),
            MathHelper.ToDegrees(frontSlip),
            MathHelper.ToDegrees(rearSlip),
            true);
    }

    private static string FormatOptional(float? value)
    {
        return value.HasValue ? value.Value.ToString("F2") : "base";
    }

    private static int SecondsToTicks(float seconds)
    {
        return Math.Max(1, (int)MathF.Round(seconds / Dt));
    }

    private readonly record struct TopEndVariant(
        string Label,
        float? NormalSlipFractionOverride,
        float? OverdriveSlipFractionOverride,
        float? TransientSlipFractionOverride,
        float? OverdriveGOverride,
        float TopCommandGain);

    private readonly record struct VariantResult(
        TopEndVariant Variant,
        IReadOnlyList<CaseResult> Cases,
        float Score);

    private readonly record struct CaseResult(
        string Variant,
        float Command,
        float SentCommand,
        float SpeedKmh,
        float RoadWheelAngleDegrees,
        float NormalAngleDegrees,
        float OverdriveAngleDegrees,
        float SlipAllowanceDegrees,
        float TransientBoostAngleDegrees,
        float NormalizedYawRateDegreesPerSecond,
        float ReferenceYawRateDegreesPerSecond,
        float NormalizedBetaDegrees,
        float ReferenceBetaDegrees,
        float NormalizedFrontSlipDegrees,
        float ReferenceFrontSlipDegrees,
        float NormalizedRearSlipDegrees,
        float ReferenceRearSlipDegrees,
        float FrontGripUsage,
        float RearGripUsage,
        float LateralG);

    private readonly record struct ReferenceSnapshot(
        float YawRateDegreesPerSecond,
        float BetaDegrees,
        float FrontSlipDegrees,
        float RearSlipDegrees,
        bool IsValid);

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
