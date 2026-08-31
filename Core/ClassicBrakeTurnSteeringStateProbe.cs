using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicBrakeTurnSteeringStateProbe
{
    private const float Dt = 1f / 120f;
    private const int Gear = 4;
    private const float StartSpeedKmh = 150f;
    private const float PreBrakeSeconds = 0.50f;
    private const float TurnSeconds = 0.50f;
    private const float FreezeCaptureSeconds = 0.10f;
    private const float SteerCommand = 1.0f;

    private static readonly float[] CheckpointsSeconds = [0.05f, 0.10f, 0.25f, 0.50f];

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);

        Console.WriteLine($"Classic brake-turn steering-state probe: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine("  diagnostic-only: production steering, brake, tyre, yaw, cleanup, and load-transfer values unchanged");
        Console.WriteLine("  sequence: 150km/h -> 0.5s hard brake -> steer=1 with progressive brake release for 0.5s");
        Console.WriteLine("  release curve: brake linearly falls from 1.00 to 0.25 during the sampled window");

        ProbeResult normal = RunCase(parameters, engine, freezeCaptureSeconds: null);
        ProbeResult frozenEarly = RunCase(parameters, engine, freezeCaptureSeconds: 0.10f);
        ProbeResult frozenPeak = RunCase(parameters, engine, freezeCaptureSeconds: 0.25f);
        PrintResult(normal);
        PrintResult(frozenEarly);
        PrintResult(frozenPeak);
        PrintComparison(normal, frozenEarly, frozenPeak);

        Console.WriteLine("Classic brake-turn steering-state probe complete.");
    }

    private static ProbeResult RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        float? freezeCaptureSeconds)
    {
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engine);
        for (int i = 0; i < SecondsToTicks(PreBrakeSeconds); i++)
        {
            simulator.Update(new VehicleInput(0f, 1f, 0f, brakeAssistEnabled: true), Dt);
        }

        List<SteeringStateSample> samples = [];
        SteeringStateSample? frontZero = null;
        float previousFrontSlip = 0f;
        float frozenAngle = float.NaN;
        int checkpointIndex = 0;

        for (int i = 1; i <= SecondsToTicks(TurnSeconds); i++)
        {
            float elapsed = i * Dt;
            float brake = MathHelper.Lerp(1f, 0.25f, MathHelper.Clamp(elapsed / TurnSeconds, 0f, 1f));
            simulator.Update(new VehicleInput(0f, brake, SteerCommand, brakeAssistEnabled: true), Dt);
            if (freezeCaptureSeconds.HasValue &&
                !float.IsFinite(frozenAngle) &&
                elapsed + Dt * 0.5f >= freezeCaptureSeconds.Value)
            {
                frozenAngle = (simulator.State.FrontLeftSteerAngleDegrees + simulator.State.FrontRightSteerAngleDegrees) * 0.5f;
                simulator.FrozenSteeringAngleDegreesForProbe = frozenAngle;
            }

            SteeringStateSample sample = BuildSample(elapsed, brake, simulator.State);
            if (frontZero is null &&
                i > 1 &&
                MathF.Sign(previousFrontSlip) != 0f &&
                MathF.Sign(sample.FrontSlipDegrees) != 0f &&
                MathF.Sign(previousFrontSlip) != MathF.Sign(sample.FrontSlipDegrees))
            {
                frontZero = sample;
            }

            if (checkpointIndex < CheckpointsSeconds.Length &&
                elapsed + Dt * 0.5f >= CheckpointsSeconds[checkpointIndex])
            {
                samples.Add(sample with { TimeSeconds = CheckpointsSeconds[checkpointIndex] });
                checkpointIndex++;
            }

            previousFrontSlip = sample.FrontSlipDegrees;
        }

        return new ProbeResult(
            freezeCaptureSeconds.HasValue
                ? $"frozen angle after {freezeCaptureSeconds.Value:0.00}s turn-in ({frozenAngle:0.00}deg)"
                : "current speed-following steering",
            frontZero,
            samples);
    }

    private static ClassicFourWheelVehicleSimulator CreateSimulator(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine)
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
        return simulator;
    }

    private static SteeringStateSample BuildSample(float elapsed, float brake, VehicleState state)
    {
        float speedKmh = state.SpeedMetersPerSecond * 3.6f;
        float actualAngle = (state.FrontLeftSteerAngleDegrees + state.FrontRightSteerAngleDegrees) * 0.5f;
        float frontSlip = (state.FrontLeftSlipAngleDegrees + state.FrontRightSlipAngleDegrees) * 0.5f;
        float rearSlip = (state.RearLeftSlipAngleDegrees + state.RearRightSlipAngleDegrees) * 0.5f;
        float travelAngle = CalculateTravelAngleDegrees(state);
        float headingAngle = MathHelper.ToDegrees(MathF.Atan2(state.Forward.X, state.Forward.Z));
        float frontVelocityAngle = CalculateFrontLocalVelocityAngleDegrees(state);
        float requiredForPositiveOne = frontVelocityAngle + 1f;
        float requiredForPositiveTwo = frontVelocityAngle + 2f;

        return new SteeringStateSample(
            elapsed,
            brake,
            speedKmh,
            state.SteeringNormalizedCommand,
            state.SteeringPhysicalNormalAngleDegrees,
            state.SteeringPhysicalOverdriveAngleDegrees,
            state.SteeringTransientBoostAngleDegrees,
            actualAngle,
            headingAngle,
            travelAngle,
            state.ClassicBodySlipAngleDegrees,
            frontVelocityAngle,
            requiredForPositiveOne,
            requiredForPositiveTwo,
            frontSlip,
            rearSlip,
            MathHelper.ToDegrees(state.YawRateRadiansPerSecond),
            state.LateralAcceleration / 9.81f,
            state.LongitudinalAcceleration / 9.81f,
            MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage));
    }

    private static float CalculateTravelAngleDegrees(VehicleState state)
    {
        Vector2 velocity = state.Velocity;
        if (velocity.LengthSquared() <= 0.0001f)
        {
            return MathHelper.ToDegrees(MathF.Atan2(state.Forward.X, state.Forward.Z));
        }

        return MathHelper.ToDegrees(MathF.Atan2(velocity.X, velocity.Y));
    }

    private static float CalculateFrontLocalVelocityAngleDegrees(VehicleState state)
    {
        float frontForward = (state.FrontLeftLocalForwardSpeedMetersPerSecond + state.FrontRightLocalForwardSpeedMetersPerSecond) * 0.5f;
        float frontLateral = (state.FrontLeftLocalLateralSpeedMetersPerSecond + state.FrontRightLocalLateralSpeedMetersPerSecond) * 0.5f;
        return MathHelper.ToDegrees(MathF.Atan2(frontLateral, MathF.Max(2f, MathF.Abs(frontForward))));
    }

    private static void PrintResult(ProbeResult result)
    {
        Console.WriteLine();
        Console.WriteLine($"  case: {result.Label}");
        Console.WriteLine("    t brake speed cmd angle normal/over/boost heading travel beta frontVel need+1/+2 slipF/R yaw latG longG rearGrip");
        foreach (SteeringStateSample sample in result.Samples)
        {
            Console.WriteLine(
                $"    {sample.TimeSeconds,4:F2} {sample.Brake,5:F2} {sample.SpeedKmh,6:F1} {sample.NormalizedCommand,4:F2} " +
                $"{sample.ActualSteerAngleDegrees,5:F2} {sample.NormalEnvelopeDegrees,5:F2}/{sample.OverdriveEnvelopeDegrees,5:F2}/{sample.TransientBoostDegrees,5:F2} " +
                $"{sample.HeadingAngleDegrees,7:F2} {sample.TravelAngleDegrees,6:F2} {sample.BetaDegrees,5:F2} " +
                $"{sample.FrontLocalVelocityAngleDegrees,8:F2} {sample.RequiredAngleForOneDegreeFrontSlip,6:F2}/{sample.RequiredAngleForTwoDegreeFrontSlip,5:F2} " +
                $"{sample.FrontSlipDegrees,6:F2}/{sample.RearSlipDegrees,6:F2} {sample.YawRateDegreesPerSecond,6:F1} " +
                $"{sample.LateralG,5:F2} {sample.LongitudinalG,5:F2} {sample.RearGripUsage,5:F2}");
        }

        Console.WriteLine(
            result.FrontSlipZero is null
                ? "    front-slip zero crossing: none in first 0.50s"
                : $"    front-slip zero crossing: t={result.FrontSlipZero.Value.TimeSeconds:0.000}s angle={result.FrontSlipZero.Value.ActualSteerAngleDegrees:0.00}deg frontVel={result.FrontSlipZero.Value.FrontLocalVelocityAngleDegrees:0.00}deg beta={result.FrontSlipZero.Value.BetaDegrees:0.00}deg");
    }

    private static void PrintComparison(ProbeResult normal, ProbeResult frozenEarly, ProbeResult frozenPeak)
    {
        SteeringStateSample normalEnd = normal.Samples[^1];
        SteeringStateSample frozenEarlyEnd = frozenEarly.Samples[^1];
        SteeringStateSample frozenPeakEnd = frozenPeak.Samples[^1];
        Console.WriteLine();
        Console.WriteLine("  comparison:");
        Console.WriteLine(
            $"    front-slip zero normal={FormatZero(normal.FrontSlipZero)} frozen0.10={FormatZero(frozenEarly.FrontSlipZero)} frozen0.25={FormatZero(frozenPeak.FrontSlipZero)}");
        Console.WriteLine(
            $"    at 0.50s: angle normal/0.10/0.25={normalEnd.ActualSteerAngleDegrees:0.00}/{frozenEarlyEnd.ActualSteerAngleDegrees:0.00}/{frozenPeakEnd.ActualSteerAngleDegrees:0.00}deg " +
            $"frontSlip={normalEnd.FrontSlipDegrees:0.00}/{frozenEarlyEnd.FrontSlipDegrees:0.00}/{frozenPeakEnd.FrontSlipDegrees:0.00}deg " +
            $"beta={normalEnd.BetaDegrees:0.00}/{frozenEarlyEnd.BetaDegrees:0.00}/{frozenPeakEnd.BetaDegrees:0.00}deg " +
            $"yaw={normalEnd.YawRateDegreesPerSecond:0.0}/{frozenEarlyEnd.YawRateDegreesPerSecond:0.0}/{frozenPeakEnd.YawRateDegreesPerSecond:0.0}deg/s");
        Console.WriteLine($"    classification: {Classify(normal, frozenEarly, frozenPeak)}");
    }

    private static string Classify(ProbeResult normal, ProbeResult frozenEarly, ProbeResult frozenPeak)
    {
        SteeringStateSample normalStart = normal.Samples[0];
        SteeringStateSample normalEnd = normal.Samples[^1];
        bool envelopeShrinks = normalEnd.OverdriveEnvelopeDegrees < normalStart.OverdriveEnvelopeDegrees - 0.05f ||
            normalEnd.NormalEnvelopeDegrees < normalStart.NormalEnvelopeDegrees - 0.05f;
        bool angleShrinks = normalEnd.ActualSteerAngleDegrees < normal.Samples[2].ActualSteerAngleDegrees - 0.2f;
        bool peakFreezeHelps = DelaysOrPreventsZero(normal, frozenPeak);
        bool earlyFreezeHelpsByReducingState = DelaysOrPreventsZero(normal, frozenEarly) &&
            frozenEarly.Samples[^1].ActualSteerAngleDegrees < normalEnd.ActualSteerAngleDegrees - 0.2f;

        if (envelopeShrinks && peakFreezeHelps)
        {
            return "speed-following steering angle decay materially contributes to front-slip collapse";
        }

        if (!envelopeShrinks && angleShrinks && !peakFreezeHelps)
        {
            return "speed envelope is not shrinking; transient steering boost/early yaw-beta buildup is the stronger suspect";
        }

        if (earlyFreezeHelpsByReducingState)
        {
            return "less early steering delays collapse by reducing yaw/beta buildup, not by preserving authority";
        }

        if (normal.FrontSlipZero is not null)
        {
            return "front-slip collapse persists with frozen turn-in angle; body/yaw/travel evolution is upstream";
        }

        return "front slip remains healthy in both cases during the sampled window";
    }

    private static bool DelaysOrPreventsZero(ProbeResult normal, ProbeResult counterfactual)
    {
        if (normal.FrontSlipZero is null)
        {
            return false;
        }

        if (counterfactual.FrontSlipZero is null)
        {
            return true;
        }

        return counterfactual.FrontSlipZero.Value.TimeSeconds - normal.FrontSlipZero.Value.TimeSeconds >= 0.08f;
    }

    private static string FormatZero(SteeringStateSample? sample)
    {
        return sample is null ? "none" : $"{sample.Value.TimeSeconds:0.000}s";
    }

    private static int SecondsToTicks(float seconds)
    {
        return Math.Max(1, (int)MathF.Round(seconds / Dt));
    }

    private readonly record struct ProbeResult(
        string Label,
        SteeringStateSample? FrontSlipZero,
        IReadOnlyList<SteeringStateSample> Samples);

    private readonly record struct SteeringStateSample(
        float TimeSeconds,
        float Brake,
        float SpeedKmh,
        float NormalizedCommand,
        float NormalEnvelopeDegrees,
        float OverdriveEnvelopeDegrees,
        float TransientBoostDegrees,
        float ActualSteerAngleDegrees,
        float HeadingAngleDegrees,
        float TravelAngleDegrees,
        float BetaDegrees,
        float FrontLocalVelocityAngleDegrees,
        float RequiredAngleForOneDegreeFrontSlip,
        float RequiredAngleForTwoDegreeFrontSlip,
        float FrontSlipDegrees,
        float RearSlipDegrees,
        float YawRateDegreesPerSecond,
        float LateralG,
        float LongitudinalG,
        float RearGripUsage);

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
