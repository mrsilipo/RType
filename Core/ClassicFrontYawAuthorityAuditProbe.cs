using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicFrontYawAuthorityAuditProbe
{
    private const float Dt = 1f / 120f;
    private const int Gear = 4;
    private const float StartSpeedKmh = 150f;
    private const float RunSeconds = 0.5f;

    private static readonly float[] Commands = [0.60f, 0.80f, 1.00f];
    private static readonly float[] CheckpointsSeconds = [0.05f, 0.10f, 0.15f, 0.20f, 0.25f, 0.35f, 0.50f];

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        VehicleAxleGeometry geometry = VehicleAxleGeometry.FromParameters(parameters);

        Console.WriteLine($"Classic front yaw authority audit probe: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine("  diagnostic-only: production steering baseline, no production values changed");
        Console.WriteLine("  sequence: coast-turn, 150km/h, gear=4, first 0.5s");
        Console.WriteLine("  turn-normalized values: into the commanded turn is positive");
        Console.WriteLine("  control: command 0.60; compare command 0.80 and 1.00 to find the first diverging quantity");

        List<ProbeCase> cases = [];
        foreach (float command in Commands)
        {
            ProbeCase probeCase = RunCase(parameters, engine, geometry, command);
            cases.Add(probeCase);
            PrintCase(probeCase);
        }

        ProbeCase control = cases.First(c => MathF.Abs(c.Command - 0.60f) < 0.01f);
        foreach (ProbeCase probeCase in cases.Where(c => c.Command > 0.60f))
        {
            PrintDivergence(control, probeCase);
        }

        Console.WriteLine($"  root classification: {ClassifyRoot(cases)}");
        Console.WriteLine("Classic front yaw authority audit probe complete.");
    }

    private static ProbeCase RunCase(
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

        List<AuthoritySample> allSamples = [];
        List<AuthoritySample> checkpoints = [];
        int checkpointIndex = 0;
        float previousBeta = simulator.State.ClassicBodySlipAngleDegrees;
        float previousYaw = MathHelper.ToDegrees(simulator.State.YawRateRadiansPerSecond);

        for (int i = 1; i <= SecondsToTicks(RunSeconds); i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, command, brakeAssistEnabled: true), Dt);
            AuthoritySample sample = BuildSample(i * Dt, command, simulator.State, geometry, previousBeta, previousYaw);
            allSamples.Add(sample);

            if (checkpointIndex < CheckpointsSeconds.Length &&
                sample.TimeSeconds + Dt * 0.5f >= CheckpointsSeconds[checkpointIndex])
            {
                checkpoints.Add(sample with { TimeSeconds = CheckpointsSeconds[checkpointIndex] });
                checkpointIndex++;
            }

            previousBeta = sample.RawBetaDegrees;
            previousYaw = sample.RawYawRateDegreesPerSecond;
        }

        return new ProbeCase(command, allSamples, checkpoints, allSamples[^1]);
    }

    private static AuthoritySample BuildSample(
        float time,
        float command,
        VehicleState state,
        VehicleAxleGeometry geometry,
        float previousBetaDegrees,
        float previousYawRateDegreesPerSecond)
    {
        float roadAngle = (state.FrontLeftSteerAngleDegrees + state.FrontRightSteerAngleDegrees) * 0.5f;
        float turnMultiplier = -MathF.Sign(roadAngle == 0f ? command : roadAngle);
        if (turnMultiplier == 0f)
        {
            turnMultiplier = -1f;
        }

        float rawYawRate = MathHelper.ToDegrees(state.YawRateRadiansPerSecond);
        float measuredYawAcceleration = (rawYawRate - previousYawRateDegreesPerSecond) / Dt;
        float calculatedYawAcceleration =
            state.ClassicNaturalYawAccelerationDegreesPerSecondSquared +
            state.ClassicYawDampingAccelerationDegreesPerSecondSquared +
            state.ClassicYawRecoveryAccelerationDegreesPerSecondSquared +
            state.ClassicRearFollowAccelerationDegreesPerSecondSquared;
        float frontLateral = state.FrontLeftLateralForceN + state.FrontRightLateralForceN;
        float rearLateral = state.RearLeftLateralForceN + state.RearRightLateralForceN;
        float frontMoment =
            Moment(-geometry.FrontTrackMeters * 0.5f, geometry.CgToFrontAxleMeters, state.FrontLeftLongitudinalForceN, state.FrontLeftLateralForceN) +
            Moment(geometry.FrontTrackMeters * 0.5f, geometry.CgToFrontAxleMeters, state.FrontRightLongitudinalForceN, state.FrontRightLateralForceN);
        float rearMoment =
            Moment(-geometry.RearTrackMeters * 0.5f, -geometry.CgToRearAxleMeters, state.RearLeftLongitudinalForceN, state.RearLeftLateralForceN) +
            Moment(geometry.RearTrackMeters * 0.5f, -geometry.CgToRearAxleMeters, state.RearRightLongitudinalForceN, state.RearRightLateralForceN);
        float normalizedFrontLateral = frontLateral * turnMultiplier;
        float normalizedRearLateral = rearLateral * turnMultiplier;
        float normalizedFrontYaw = frontMoment * turnMultiplier;
        float normalizedRearYaw = rearMoment * turnMultiplier;
        float totalLatAbs = MathF.Abs(normalizedFrontLateral) + MathF.Abs(normalizedRearLateral);
        float totalYawAbs = MathF.Abs(normalizedFrontYaw) + MathF.Abs(normalizedRearYaw);

        return new AuthoritySample(
            time,
            command,
            state.SpeedMetersPerSecond * 3.6f,
            roadAngle,
            state.SteeringPhysicalNormalAngleDegrees,
            state.SteeringPhysicalOverdriveAngleDegrees,
            MathF.Max(0f, MathF.Abs(roadAngle) -
                state.SteeringPhysicalNormalAngleDegrees -
                state.SteeringTransientBoostAngleDegrees),
            state.SteeringTransientBoostAngleDegrees,
            state.ClassicBodySlipAngleDegrees,
            state.ClassicBodySlipAngleDegrees * turnMultiplier,
            (state.ClassicBodySlipAngleDegrees - previousBetaDegrees) / Dt * turnMultiplier,
            rawYawRate,
            rawYawRate * turnMultiplier,
            measuredYawAcceleration * turnMultiplier,
            calculatedYawAcceleration * turnMultiplier,
            (state.FrontLeftSlipAngleDegrees + state.FrontRightSlipAngleDegrees) * 0.5f * turnMultiplier,
            (state.RearLeftSlipAngleDegrees + state.RearRightSlipAngleDegrees) * 0.5f * turnMultiplier,
            normalizedFrontLateral,
            normalizedRearLateral,
            normalizedFrontYaw,
            normalizedRearYaw,
            (frontMoment + rearMoment) * turnMultiplier,
            totalLatAbs > 1f ? MathF.Abs(normalizedFrontLateral) / totalLatAbs : 0f,
            totalLatAbs > 1f ? MathF.Abs(normalizedRearLateral) / totalLatAbs : 0f,
            totalYawAbs > 1f ? MathF.Abs(normalizedFrontYaw) / totalYawAbs : 0f,
            totalYawAbs > 1f ? MathF.Abs(normalizedRearYaw) / totalYawAbs : 0f,
            state.FrontLeftLoadN + state.FrontRightLoadN,
            state.RearLeftLoadN + state.RearRightLoadN,
            MathF.Max(state.FrontLeftGripUsage, state.FrontRightGripUsage),
            MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage),
            state.LateralAcceleration / 9.81f);
    }

    private static void PrintCase(ProbeCase probeCase)
    {
        Console.WriteLine();
        Console.WriteLine($"  command {probeCase.Command:F2}");
        Console.WriteLine("    t angle n/o/slip/boost beta betaDot yaw yawAcc slipF/R latF/R latShareF/R yawM F/R/net yawShareF/R loadF/R gripF/R latG");
        foreach (AuthoritySample sample in probeCase.Checkpoints)
        {
            Console.WriteLine(
                $"    {sample.TimeSeconds,4:F2} {sample.RoadWheelAngleDegrees,5:F2} " +
                $"{sample.NormalAngleDegrees:F2}/{sample.OverdriveAngleDegrees:F2}/{sample.SlipAllowanceDegrees:F2}/{sample.TransientBoostAngleDegrees:F2} " +
                $"{sample.NormalizedBetaDegrees,6:F2} {sample.BetaDotDegreesPerSecond,7:F1} " +
                $"{sample.NormalizedYawRateDegreesPerSecond,6:F1} {sample.CalculatedYawAccelerationDegreesPerSecondSquared,7:F0} " +
                $"{sample.NormalizedFrontSlipDegrees,6:F2}/{sample.NormalizedRearSlipDegrees,6:F2} " +
                $"{sample.NormalizedFrontLateralForceN,7:F0}/{sample.NormalizedRearLateralForceN,7:F0} " +
                $"{sample.FrontLateralShare,4:P0}/{sample.RearLateralShare,4:P0} " +
                $"{sample.NormalizedFrontYawMomentNm,7:F0}/{sample.NormalizedRearYawMomentNm,7:F0}/{sample.NormalizedNetYawMomentNm,7:F0} " +
                $"{sample.FrontYawShare,4:P0}/{sample.RearYawShare,4:P0} " +
                $"{sample.FrontLoadN,6:F0}/{sample.RearLoadN,6:F0} " +
                $"{sample.FrontGripUsage,4:F2}/{sample.RearGripUsage,4:F2} {sample.LateralG,5:F2}");
        }
    }

    private static void PrintDivergence(ProbeCase control, ProbeCase candidate)
    {
        Divergence? first = null;
        int count = Math.Min(control.AllSamples.Count, candidate.AllSamples.Count);
        for (int i = 0; i < count; i++)
        {
            AuthoritySample a = control.AllSamples[i];
            AuthoritySample b = candidate.AllSamples[i];
            first ??= CheckDivergence(a, b);
        }

        Console.WriteLine();
        Console.WriteLine($"  divergence 0.60 -> {candidate.Command:F2}:");
        if (first is null)
        {
            Console.WriteLine("    no material divergence found in first 0.5s under current thresholds");
            return;
        }

        Divergence d = first.Value;
        Console.WriteLine(
            $"    first material divergence: t={d.TimeSeconds:F3}s quantity={d.Quantity} " +
            $"control={d.ControlValue:F2} candidate={d.CandidateValue:F2} delta={d.Delta:+0.00;-0.00;0.00}");
        Console.WriteLine($"    causal interpretation: {InterpretDivergence(d.Quantity)}");
    }

    private static Divergence? CheckDivergence(AuthoritySample control, AuthoritySample candidate)
    {
        (string Name, float Control, float Candidate, float Threshold)[] checks =
        [
            ("roadWheelAngle", control.RoadWheelAngleDegrees, candidate.RoadWheelAngleDegrees, 0.70f),
            ("frontSlip", control.NormalizedFrontSlipDegrees, candidate.NormalizedFrontSlipDegrees, 0.70f),
            ("rearSlip", control.NormalizedRearSlipDegrees, candidate.NormalizedRearSlipDegrees, 0.70f),
            ("frontYawMoment", control.NormalizedFrontYawMomentNm, candidate.NormalizedFrontYawMomentNm, 700f),
            ("rearYawMoment", control.NormalizedRearYawMomentNm, candidate.NormalizedRearYawMomentNm, 700f),
            ("betaDot", control.BetaDotDegreesPerSecond, candidate.BetaDotDegreesPerSecond, 3.5f),
            ("beta", control.NormalizedBetaDegrees, candidate.NormalizedBetaDegrees, 0.85f),
            ("frontLateralForce", control.NormalizedFrontLateralForceN, candidate.NormalizedFrontLateralForceN, 700f),
            ("rearLateralForce", control.NormalizedRearLateralForceN, candidate.NormalizedRearLateralForceN, 700f),
            ("rearYawShare", control.RearYawShare, candidate.RearYawShare, 0.18f),
            ("frontGripUsage", control.FrontGripUsage, candidate.FrontGripUsage, 0.08f),
            ("rearGripUsage", control.RearGripUsage, candidate.RearGripUsage, 0.08f)
        ];

        foreach ((string name, float controlValue, float candidateValue, float threshold) in checks)
        {
            float delta = candidateValue - controlValue;
            if (MathF.Abs(delta) >= threshold)
            {
                return new Divergence(candidate.TimeSeconds, name, controlValue, candidateValue, delta);
            }
        }

        return null;
    }

    private static string InterpretDivergence(string quantity)
    {
        return quantity switch
        {
            "roadWheelAngle" => "top-end mapping creates substantially more steering demand before tyre/chassis terms diverge",
            "frontSlip" => "front slip state diverges first; inspect whether it moves out of the useful range before force follows",
            "rearSlip" => "rear slip state diverges first; beta/yaw state may be putting the rear axle into the dominant role early",
            "frontYawMoment" => "front yaw authority changes before the rear dominates; inspect front force duration and slip-to-force response",
            "rearYawMoment" => "rear yaw contribution grows disproportionately before other states cross thresholds",
            "betaDot" or "beta" => "body attitude starts diverging before force shares clearly separate; beta growth may be starving front authority",
            "frontLateralForce" => "front lateral force changes first; compare against front slip to see whether the tyre is leaving useful range",
            "rearLateralForce" => "rear lateral force changes first; rear axle may be becoming path-defining too early",
            "rearYawShare" => "rear share becomes dominant before absolute force deltas are large",
            "frontGripUsage" => "front utilisation changes first; front tyre budget/combined state needs inspection",
            "rearGripUsage" => "rear utilisation changes first; rear grip budget is becoming influential early",
            _ => "unclassified divergence"
        };
    }

    private static string ClassifyRoot(IReadOnlyList<ProbeCase> cases)
    {
        ProbeCase control = cases.First(c => MathF.Abs(c.Command - 0.60f) < 0.01f);
        ProbeCase hard = cases.First(c => MathF.Abs(c.Command - 1.00f) < 0.01f);
        AuthoritySample cEnd = control.FinalSample;
        AuthoritySample hEnd = hard.FinalSample;

        if (hEnd.NormalizedFrontYawMomentNm < cEnd.NormalizedFrontYawMomentNm &&
            hEnd.RearYawShare > cEnd.RearYawShare + 0.15f)
        {
            return "front yaw authority is shorter-lived at high command while rear yaw share grows; next inspect why front force/moment collapses after initial bite";
        }

        if (hEnd.NormalizedBetaDegrees < cEnd.NormalizedBetaDegrees - 1.5f &&
            hEnd.NormalizedRearSlipDegrees > cEnd.NormalizedRearSlipDegrees + 1f)
        {
            return "beta/rear-slip growth is the dominant high-command difference; front may be losing authority because chassis attitude outruns it";
        }

        return "no single root from 0.5s audit; extend comparison into settled state or inspect tyre relaxation next";
    }

    private static float Moment(float right, float forward, float forwardForce, float rightForce)
    {
        return right * forwardForce - forward * rightForce;
    }

    private static int SecondsToTicks(float seconds)
    {
        return Math.Max(1, (int)MathF.Round(seconds / Dt));
    }

    private readonly record struct ProbeCase(
        float Command,
        IReadOnlyList<AuthoritySample> AllSamples,
        IReadOnlyList<AuthoritySample> Checkpoints,
        AuthoritySample FinalSample);

    private readonly record struct AuthoritySample(
        float TimeSeconds,
        float Command,
        float SpeedKmh,
        float RoadWheelAngleDegrees,
        float NormalAngleDegrees,
        float OverdriveAngleDegrees,
        float SlipAllowanceDegrees,
        float TransientBoostAngleDegrees,
        float RawBetaDegrees,
        float NormalizedBetaDegrees,
        float BetaDotDegreesPerSecond,
        float RawYawRateDegreesPerSecond,
        float NormalizedYawRateDegreesPerSecond,
        float MeasuredYawAccelerationDegreesPerSecondSquared,
        float CalculatedYawAccelerationDegreesPerSecondSquared,
        float NormalizedFrontSlipDegrees,
        float NormalizedRearSlipDegrees,
        float NormalizedFrontLateralForceN,
        float NormalizedRearLateralForceN,
        float NormalizedFrontYawMomentNm,
        float NormalizedRearYawMomentNm,
        float NormalizedNetYawMomentNm,
        float FrontLateralShare,
        float RearLateralShare,
        float FrontYawShare,
        float RearYawShare,
        float FrontLoadN,
        float RearLoadN,
        float FrontGripUsage,
        float RearGripUsage,
        float LateralG);

    private readonly record struct Divergence(
        float TimeSeconds,
        string Quantity,
        float ControlValue,
        float CandidateValue,
        float Delta);

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
