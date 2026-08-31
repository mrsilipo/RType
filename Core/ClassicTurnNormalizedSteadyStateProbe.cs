using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicTurnNormalizedSteadyStateProbe
{
    private const float Dt = 1f / 120f;
    private const int Gear = 4;
    private const float StartSpeedKmh = 150f;
    private const float RunSeconds = 3.0f;

    private static readonly float[] Commands = [0.20f, 0.40f, 0.60f, 0.80f, 1.00f];

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        VehicleAxleGeometry geometry = VehicleAxleGeometry.FromParameters(parameters);

        Console.WriteLine($"Classic turn-normalized steady-state probe: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine("  diagnostic-only: production tyres, steering, yaw, brake, cleanup, and load-transfer values unchanged");
        Console.WriteLine("  sequence: coast-turn, 150km/h, gear=4, fixed normalized steering commands, compare final 3.0s state");
        Console.WriteLine("  sign convention:");
        Console.WriteLine("    raw simulator: positive steering command/road angle currently produces negative yaw for this turn direction");
        Console.WriteLine("    reference: positive road angle produces positive yaw and positive slip in the textbook equations");
        Console.WriteLine("    normalized: values into the intended turn are positive; sim values are multiplied by -sign(road angle), reference values by +sign(road angle)");
        Console.WriteLine("    slip: positive normalized slip means the axle is producing restoring/turning force into the requested corner");
        Console.WriteLine("    lateral force/yaw: signs are reported raw and normalized; magnitude/balance is judged after normalization");
        Console.WriteLine(
            $"  reference inputs: wheelbase={geometry.WheelbaseMeters:F3}m cgFront={geometry.CgToFrontAxleMeters:F3}m " +
            $"cgRear={geometry.CgToRearAxleMeters:F3}m Cf={parameters.FrontTyres.CorneringStiffnessNPerRad:F0}N/rad " +
            $"Cr={parameters.RearTyres.CorneringStiffnessNPerRad:F0}N/rad");

        List<ProbeResult> results = [];
        foreach (float command in Commands)
        {
            ProbeResult result = RunCase(parameters, engine, geometry, command);
            results.Add(result);
            PrintCase(result);
        }

        PrintSummary(results);
        Console.WriteLine("Classic turn-normalized steady-state probe complete.");
    }

    private static ProbeResult RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        VehicleAxleGeometry geometry,
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

        float previousBeta = simulator.State.ClassicBodySlipAngleDegrees;
        float previousYaw = MathHelper.ToDegrees(simulator.State.YawRateRadiansPerSecond);
        Sample final = default;
        for (int i = 1; i <= SecondsToTicks(RunSeconds); i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, command, brakeAssistEnabled: true), Dt);
            final = BuildSample(i * Dt, command, simulator.State, geometry, previousBeta, previousYaw);
            previousBeta = final.BetaDegrees;
            previousYaw = final.YawRateDegreesPerSecond;
        }

        float roadAngleRadians = MathHelper.ToRadians(final.RoadWheelAngleDegrees);
        ReferenceSnapshot reference = CalculateReference(
            parameters,
            geometry,
            final.SpeedKmh / 3.6f,
            roadAngleRadians);

        float roadSign = MathF.Sign(final.RoadWheelAngleDegrees);
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
        NormalizedSample normalizedSim = new(
            final.YawRateDegreesPerSecond * simTurnMultiplier,
            final.BetaDegrees * simTurnMultiplier,
            final.FrontSlipDegrees * simTurnMultiplier,
            final.RearSlipDegrees * simTurnMultiplier,
            final.FrontLateralForceN * simTurnMultiplier,
            final.RearLateralForceN * simTurnMultiplier,
            final.FrontYawMomentNm * simTurnMultiplier,
            final.RearYawMomentNm * simTurnMultiplier);
        NormalizedSample normalizedReference = new(
            reference.YawRateDegreesPerSecond * referenceTurnMultiplier,
            reference.BetaDegrees * referenceTurnMultiplier,
            reference.FrontSlipDegrees * referenceTurnMultiplier,
            reference.RearSlipDegrees * referenceTurnMultiplier,
            0f,
            0f,
            0f,
            0f);

        return new ProbeResult(
            command,
            final,
            reference,
            normalizedSim,
            normalizedReference,
            Classify(final, reference, normalizedSim, normalizedReference));
    }

    private static Sample BuildSample(
        float time,
        float command,
        VehicleState state,
        VehicleAxleGeometry geometry,
        float previousBetaDegrees,
        float previousYawRateDegreesPerSecond)
    {
        float frontLateral = state.FrontLeftLateralForceN + state.FrontRightLateralForceN;
        float rearLateral = state.RearLeftLateralForceN + state.RearRightLateralForceN;
        float frontMoment =
            Moment(-geometry.FrontTrackMeters * 0.5f, geometry.CgToFrontAxleMeters, state.FrontLeftLongitudinalForceN, state.FrontLeftLateralForceN) +
            Moment(geometry.FrontTrackMeters * 0.5f, geometry.CgToFrontAxleMeters, state.FrontRightLongitudinalForceN, state.FrontRightLateralForceN);
        float rearMoment =
            Moment(-geometry.RearTrackMeters * 0.5f, -geometry.CgToRearAxleMeters, state.RearLeftLongitudinalForceN, state.RearLeftLateralForceN) +
            Moment(geometry.RearTrackMeters * 0.5f, -geometry.CgToRearAxleMeters, state.RearRightLongitudinalForceN, state.RearRightLateralForceN);
        float yawRate = MathHelper.ToDegrees(state.YawRateRadiansPerSecond);
        float betaDot = (state.ClassicBodySlipAngleDegrees - previousBetaDegrees) / Dt;
        float yawAcceleration = (yawRate - previousYawRateDegreesPerSecond) / Dt;

        return new Sample(
            time,
            command,
            state.SteeringNormalizedCommand,
            state.SpeedMetersPerSecond * 3.6f,
            (state.FrontLeftSteerAngleDegrees + state.FrontRightSteerAngleDegrees) * 0.5f,
            state.SteeringPhysicalNormalAngleDegrees,
            state.SteeringPhysicalOverdriveAngleDegrees,
            MathF.Max(0f, MathF.Abs((state.FrontLeftSteerAngleDegrees + state.FrontRightSteerAngleDegrees) * 0.5f) -
                state.SteeringPhysicalNormalAngleDegrees -
                state.SteeringTransientBoostAngleDegrees),
            state.SteeringTransientBoostAngleDegrees,
            state.ClassicBodySlipAngleDegrees,
            betaDot,
            yawRate,
            yawAcceleration,
            state.ClassicNaturalYawAccelerationDegreesPerSecondSquared +
                state.ClassicYawDampingAccelerationDegreesPerSecondSquared +
                state.ClassicYawRecoveryAccelerationDegreesPerSecondSquared +
                state.ClassicRearFollowAccelerationDegreesPerSecondSquared,
            (state.FrontLeftSlipAngleDegrees + state.FrontRightSlipAngleDegrees) * 0.5f,
            (state.RearLeftSlipAngleDegrees + state.RearRightSlipAngleDegrees) * 0.5f,
            frontLateral,
            rearLateral,
            frontMoment,
            rearMoment,
            frontMoment + rearMoment,
            MathF.Max(state.FrontLeftGripUsage, state.FrontRightGripUsage),
            MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage),
            state.LateralAcceleration / 9.81f);
    }

    private static void PrintCase(ProbeResult result)
    {
        Sample s = result.Sim;
        ReferenceSnapshot r = result.Reference;
        NormalizedSample n = result.NormalizedSim;
        NormalizedSample nr = result.NormalizedReference;

        Console.WriteLine();
        Console.WriteLine($"  command {result.Command:F2}");
        Console.WriteLine(
            $"    final raw sim: speed={s.SpeedKmh:F1}km/h road={s.RoadWheelAngleDegrees:F2}deg " +
            $"normal/over/slip/boost={s.NormalAngleDegrees:F2}/{s.OverdriveAngleDegrees:F2}/{s.SlipAllowanceDegrees:F2}/{s.TransientBoostAngleDegrees:F2} " +
            $"yaw={s.YawRateDegreesPerSecond:F1}deg/s beta={s.BetaDegrees:F2}deg betaDot={s.BetaDotDegreesPerSecond:F2}deg/s " +
            $"yawAcc={s.CalculatedYawAccelerationDegreesPerSecondSquared:F1}deg/s2 slipF/R={s.FrontSlipDegrees:F2}/{s.RearSlipDegrees:F2}deg " +
            $"latF/R={s.FrontLateralForceN:F0}/{s.RearLateralForceN:F0}N yawM F/R/net={s.FrontYawMomentNm:F0}/{s.RearYawMomentNm:F0}/{s.NetYawMomentNm:F0}Nm " +
            $"gripF/R={s.FrontGripUsage:F2}/{s.RearGripUsage:F2} latG={s.LateralG:F2}");
        Console.WriteLine(
            $"    raw reference: valid={r.IsValid} yaw={r.YawRateDegreesPerSecond:F1}deg/s beta={r.BetaDegrees:F2}deg " +
            $"slipF/R={r.FrontSlipDegrees:F2}/{r.RearSlipDegrees:F2}deg");
        Console.WriteLine(
            $"    normalized sim: yaw={n.YawRateDegreesPerSecond:F1} beta={n.BetaDegrees:F2} " +
            $"slipF/R={n.FrontSlipDegrees:F2}/{n.RearSlipDegrees:F2} latF/R={n.FrontLateralForceN:F0}/{n.RearLateralForceN:F0} " +
            $"yawM F/R={n.FrontYawMomentNm:F0}/{n.RearYawMomentNm:F0}");
        Console.WriteLine(
            $"    normalized ref: yaw={nr.YawRateDegreesPerSecond:F1} beta={nr.BetaDegrees:F2} " +
            $"slipF/R={nr.FrontSlipDegrees:F2}/{nr.RearSlipDegrees:F2}");
        Console.WriteLine(
            $"    errors: yaw={n.YawRateDegreesPerSecond - nr.YawRateDegreesPerSecond:+0.0;-0.0;0.0}deg/s " +
            $"beta={n.BetaDegrees - nr.BetaDegrees:+0.00;-0.00;0.00}deg " +
            $"frontSlip={n.FrontSlipDegrees - nr.FrontSlipDegrees:+0.00;-0.00;0.00}deg " +
            $"rearSlip={n.RearSlipDegrees - nr.RearSlipDegrees:+0.00;-0.00;0.00}deg");
        Console.WriteLine($"    classification: {result.Classification}");
    }

    private static void PrintSummary(IReadOnlyList<ProbeResult> results)
    {
        Console.WriteLine();
        Console.WriteLine("  summary:");
        Console.WriteLine("    cmd angle normSlipF/R refSlipF/R normBeta/refBeta normYaw/refYaw rearGrip class");
        foreach (ProbeResult result in results)
        {
            Console.WriteLine(
                $"    {result.Command,4:F2} {result.Sim.RoadWheelAngleDegrees,5:F2} " +
                $"{result.NormalizedSim.FrontSlipDegrees,6:F2}/{result.NormalizedSim.RearSlipDegrees,6:F2} " +
                $"{result.NormalizedReference.FrontSlipDegrees,6:F2}/{result.NormalizedReference.RearSlipDegrees,6:F2} " +
                $"{result.NormalizedSim.BetaDegrees,7:F2}/{result.NormalizedReference.BetaDegrees,7:F2} " +
                $"{result.NormalizedSim.YawRateDegreesPerSecond,7:F1}/{result.NormalizedReference.YawRateDegreesPerSecond,7:F1} " +
                $"{result.Sim.RearGripUsage,7:F2} {ShortClassify(result)}");
        }

        Console.WriteLine($"    root classification: {ClassifyRoot(results)}");
    }

    private static string Classify(
        Sample sim,
        ReferenceSnapshot reference,
        NormalizedSample normalizedSim,
        NormalizedSample normalizedReference)
    {
        if (!reference.IsValid)
        {
            return "reference invalid for this state";
        }

        bool signsHealthy =
            normalizedSim.FrontSlipDegrees > 0f &&
            normalizedSim.RearSlipDegrees > 0f &&
            normalizedSim.YawRateDegreesPerSecond > 0f;
        float frontError = MathF.Abs(normalizedSim.FrontSlipDegrees - normalizedReference.FrontSlipDegrees);
        float rearError = MathF.Abs(normalizedSim.RearSlipDegrees - normalizedReference.RearSlipDegrees);
        float yawError = MathF.Abs(normalizedSim.YawRateDegreesPerSecond - normalizedReference.YawRateDegreesPerSecond);
        float betaError = MathF.Abs(normalizedSim.BetaDegrees - normalizedReference.BetaDegrees);

        if (signsHealthy &&
            frontError <= 1.5f &&
            rearError <= 1.75f &&
            yawError <= 5f &&
            betaError <= 2f &&
            sim.RearGripUsage < 0.90f)
        {
            return "mostly sign-convention: normalized signs and magnitudes are close to reference";
        }

        if (signsHealthy)
        {
            return "sign normalizes correctly, but magnitudes/balance still differ from reference";
        }

        if (normalizedSim.FrontSlipDegrees <= 0f || normalizedSim.RearSlipDegrees <= 0f)
        {
            return "turn-normalized slip is still opposite target on at least one axle";
        }

        return "mixed sign/equilibrium mismatch";
    }

    private static string ShortClassify(ProbeResult result)
    {
        if (result.Classification.StartsWith("mostly sign", StringComparison.Ordinal))
        {
            return "sign-only";
        }

        if (result.Classification.StartsWith("sign normalizes", StringComparison.Ordinal))
        {
            return "mag/balance";
        }

        if (result.Classification.StartsWith("turn-normalized", StringComparison.Ordinal))
        {
            return "sign+state";
        }

        return "mixed";
    }

    private static string ClassifyRoot(IReadOnlyList<ProbeResult> results)
    {
        int signOnly = results.Count(r => ShortClassify(r) == "sign-only");
        int magnitude = results.Count(r => ShortClassify(r) == "mag/balance");
        int signState = results.Count(r => ShortClassify(r) == "sign+state");

        if (signOnly == results.Count)
        {
            return "mostly sign-convention misunderstanding; revise earlier diagnostic targets before changing physics";
        }

        if (signState > 0)
        {
            return "both sign and equilibrium are wrong for at least one command; continue base cornering investigation";
        }

        if (magnitude > 0)
        {
            return "sign convention is fine after normalization, but slip/yaw/beta magnitudes and front/rear balance are wrong";
        }

        return "mixed result; inspect per-command classification before tuning";
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

    private static float Moment(float right, float forward, float forwardForce, float rightForce)
    {
        return right * forwardForce - forward * rightForce;
    }

    private static int SecondsToTicks(float seconds)
    {
        return Math.Max(1, (int)MathF.Round(seconds / Dt));
    }

    private readonly record struct ProbeResult(
        float Command,
        Sample Sim,
        ReferenceSnapshot Reference,
        NormalizedSample NormalizedSim,
        NormalizedSample NormalizedReference,
        string Classification);

    private readonly record struct Sample(
        float TimeSeconds,
        float Command,
        float NormalizedCommand,
        float SpeedKmh,
        float RoadWheelAngleDegrees,
        float NormalAngleDegrees,
        float OverdriveAngleDegrees,
        float SlipAllowanceDegrees,
        float TransientBoostAngleDegrees,
        float BetaDegrees,
        float BetaDotDegreesPerSecond,
        float YawRateDegreesPerSecond,
        float MeasuredYawAccelerationDegreesPerSecondSquared,
        float CalculatedYawAccelerationDegreesPerSecondSquared,
        float FrontSlipDegrees,
        float RearSlipDegrees,
        float FrontLateralForceN,
        float RearLateralForceN,
        float FrontYawMomentNm,
        float RearYawMomentNm,
        float NetYawMomentNm,
        float FrontGripUsage,
        float RearGripUsage,
        float LateralG);

    private readonly record struct ReferenceSnapshot(
        float YawRateDegreesPerSecond,
        float BetaDegrees,
        float FrontSlipDegrees,
        float RearSlipDegrees,
        bool IsValid);

    private readonly record struct NormalizedSample(
        float YawRateDegreesPerSecond,
        float BetaDegrees,
        float FrontSlipDegrees,
        float RearSlipDegrees,
        float FrontLateralForceN,
        float RearLateralForceN,
        float FrontYawMomentNm,
        float RearYawMomentNm);

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
