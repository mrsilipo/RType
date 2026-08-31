using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicForkliftInvariantProbe
{
    private const float Dt = 1f / 120f;
    private const float Throttle = 0.28f;
    private const float ReverseThrottle = 0.28f;
    private const float RunSeconds = 4.0f;

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);

        VehicleAxleGeometry geometry = VehicleAxleGeometry.FromParameters(parameters);
        Console.WriteLine($"Classic forklift invariant probe: {parameters.DisplayName}");
        Console.WriteLine("  game axes: +Z forward, +X right, +Y up.");
        Console.WriteLine("  positive yaw: nose rotates toward game +X/right.");
        Console.WriteLine("  input convention under test: steer +1 is player/right input; steer -1 is player/left input.");
        Console.WriteLine(
            $"  geometry: wheelbase={geometry.WheelbaseMeters:0.000}m cgToFront={geometry.CgToFrontAxleMeters:0.000}m cgToRear={geometry.CgToRearAxleMeters:0.000}m trackF/R={geometry.FrontTrackMeters:0.000}/{geometry.RearTrackMeters:0.000}m");
        Console.WriteLine("  expected front-steer invariant: yaw grows with speed, rear axle follows an arc, ICR is laterally near the rear axle at crawl speed.");
        Console.WriteLine("  columns: case t kmh steer road yaw yawKinPlayer yawKinInternal yawRatio bodyU/V beta frontStepR/F rearStepR/F rear/frontSide ICRright ICRfwd ICRzone frontLat/rearLat frontYaw/rearYaw");

        RunCase(parameters, engine, geometry, "fwd-right", reverse: false, steer: 1f, alternating: false);
        RunCase(parameters, engine, geometry, "fwd-left", reverse: false, steer: -1f, alternating: false);
        RunCase(parameters, engine, geometry, "rev-right", reverse: true, steer: 1f, alternating: false);
        RunCase(parameters, engine, geometry, "rev-left", reverse: true, steer: -1f, alternating: false);
        RunCase(parameters, engine, geometry, "fwd-altern", reverse: false, steer: 1f, alternating: true);
        RunCase(parameters, engine, geometry, "rev-altern", reverse: true, steer: 1f, alternating: true);

        Console.WriteLine("Classic forklift invariant probe complete.");
    }

    private static void RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        VehicleAxleGeometry geometry,
        string label,
        bool reverse,
        float steer,
        bool alternating)
    {
        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engine);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = reverse ? -1 : 1;
        simulator.State.Rpm = parameters.IdleRpm;

        AxleCenters previousCenters = AxleCenters.From(simulator.State, geometry);
        Sample previous = Sample.From(0f, steer, simulator.State, geometry, previousCenters, previousCenters);
        Sample worstForklift = previous;
        Sample worstJolt = previous;
        float worstForkliftScore = float.NegativeInfinity;
        float worstJoltScore = float.NegativeInfinity;

        List<Sample> samples = [];
        for (int tick = 1; tick <= SecondsToTicks(RunSeconds); tick++)
        {
            float time = tick * Dt;
            float steerInput = alternating
                ? (time < 1.20f ? steer : time < 1.85f ? -steer : time < 2.50f ? steer : -steer)
                : steer;
            VehicleInput input = reverse
                ? new VehicleInput(0f, 0f, steerInput, reverse: ReverseThrottle)
                : new VehicleInput(Throttle, 0f, steerInput);

            simulator.Update(input, Dt);

            AxleCenters centers = AxleCenters.From(simulator.State, geometry);
            Sample sample = Sample.From(time, steerInput, simulator.State, geometry, centers, previousCenters);
            sample = sample with
            {
                YawRateStepDegreesPerSecond = MathF.Abs(sample.YawRateDegreesPerSecond - previous.YawRateDegreesPerSecond),
                BetaStepDegrees = MathF.Abs(sample.BetaDegrees - previous.BetaDegrees),
                RearSideStepMeters = MathF.Abs(sample.RearStepRightMeters - previous.RearStepRightMeters),
                FrontSideStepMeters = MathF.Abs(sample.FrontStepRightMeters - previous.FrontStepRightMeters)
            };
            samples.Add(sample);

            if (sample.SpeedKmh >= 1f && sample.SpeedKmh <= 16f)
            {
                float frontSide = MathF.Abs(sample.FrontStepRightMeters);
                float rearSide = MathF.Abs(sample.RearStepRightMeters);
                float forkliftScore = rearSide / MathF.Max(0.0001f, frontSide) +
                    MathF.Abs(sample.IcrForwardMeters + geometry.CgToRearAxleMeters) * 0.35f +
                    MathF.Abs(sample.BetaDegrees) * 0.05f;
                if (forkliftScore > worstForkliftScore)
                {
                    worstForkliftScore = forkliftScore;
                    worstForklift = sample;
                }

                float joltScore = sample.YawRateStepDegreesPerSecond * 20f +
                    sample.BetaStepDegrees * 12f +
                    (sample.RearSideStepMeters + sample.FrontSideStepMeters) * 1000f;
                if (joltScore > worstJoltScore)
                {
                    worstJoltScore = joltScore;
                    worstJolt = sample;
                }
            }

            previous = sample;
            previousCenters = centers;
        }

        Sample near3 = NearestSpeed(samples, 3f);
        Sample near8 = NearestSpeed(samples, 8f);
        Sample near12 = NearestSpeed(samples, 12f);
        Console.WriteLine($"  {label}: milestones");
        PrintSample(near3);
        PrintSample(near8);
        PrintSample(near12);
        Console.WriteLine($"  {label}: worst forklift signature");
        PrintSample(worstForklift);
        Console.WriteLine($"  {label}: worst jolt signature");
        PrintSample(worstJolt);
        PrintWindow(samples, worstJolt.TimeSeconds);
        Console.WriteLine();
    }

    private static Sample NearestSpeed(IReadOnlyList<Sample> samples, float speedKmh)
    {
        if (samples.Count == 0)
        {
            return default;
        }

        Sample best = samples[0];
        float bestDelta = MathF.Abs(best.SpeedKmh - speedKmh);
        for (int i = 1; i < samples.Count; i++)
        {
            float delta = MathF.Abs(samples[i].SpeedKmh - speedKmh);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = samples[i];
            }
        }

        return best;
    }

    private static void PrintWindow(IReadOnlyList<Sample> samples, float centerTime)
    {
        Console.WriteLine("    jolt window:");
        for (int i = 0; i < samples.Count; i++)
        {
            if (MathF.Abs(samples[i].TimeSeconds - centerTime) <= 0.05f)
            {
                PrintSample(samples[i]);
            }
        }
    }

    private static void PrintSample(Sample sample)
    {
        Console.WriteLine(
            $"    {sample.Label,-9} {sample.TimeSeconds,5:F3} {sample.SpeedKmh,5:F2} {sample.SteerInput,5:F2} {sample.RoadWheelDegrees,5:F1} " +
            $"{sample.YawRateDegreesPerSecond,6:F1} {sample.PlayerExpectedYawRateDegreesPerSecond,6:F1} {sample.InternalExpectedYawRateDegreesPerSecond,6:F1} {sample.InternalYawRatio,6:F2} " +
            $"{sample.ForwardSpeed,6:F2}/{sample.LateralSpeed,6:F2} {sample.BetaDegrees,6:F1} " +
            $"{sample.FrontStepRightMeters * 1000f,7:F1}/{sample.FrontStepForwardMeters * 1000f,7:F1} " +
            $"{sample.RearStepRightMeters * 1000f,7:F1}/{sample.RearStepForwardMeters * 1000f,7:F1} " +
            $"{sample.RearToFrontSideRatio,7:F2} " +
            $"{FormatFinite(sample.IcrRightMeters),7} {FormatFinite(sample.IcrForwardMeters),7} {sample.IcrZone,-9} " +
            $"{sample.FrontLateralForceN,7:F0}/{sample.RearLateralForceN,7:F0} " +
            $"{sample.FrontYawAccelerationDegreesPerSecondSquared,7:F0}/{sample.RearYawAccelerationDegreesPerSecondSquared,7:F0}");
    }

    private static string FormatFinite(float value)
    {
        return float.IsFinite(value) ? value.ToString("0.00") : "inf";
    }

    private static int SecondsToTicks(float seconds)
    {
        return Math.Max(1, (int)MathF.Round(seconds / Dt));
    }

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }

    private readonly record struct AxleCenters(Vector2 Front, Vector2 Rear)
    {
        public static AxleCenters From(VehicleState state, VehicleAxleGeometry geometry)
        {
            Vector2 center = new(state.Position.X, state.Position.Z);
            Vector2 forward = new(state.Forward.X, state.Forward.Z);
            return new AxleCenters(
                center + forward * geometry.CgToFrontAxleMeters,
                center - forward * geometry.CgToRearAxleMeters);
        }
    }

    private readonly record struct Sample(
        string Label,
        float TimeSeconds,
        float SpeedKmh,
        float SteerInput,
        float RoadWheelDegrees,
        float YawRateDegreesPerSecond,
        float YawRateStepDegreesPerSecond,
        float PlayerExpectedYawRateDegreesPerSecond,
        float InternalExpectedYawRateDegreesPerSecond,
        float InternalYawRatio,
        float ForwardSpeed,
        float LateralSpeed,
        float BetaDegrees,
        float BetaStepDegrees,
        float FrontStepRightMeters,
        float FrontStepForwardMeters,
        float RearStepRightMeters,
        float RearStepForwardMeters,
        float RearSideStepMeters,
        float FrontSideStepMeters,
        float RearToFrontSideRatio,
        float IcrRightMeters,
        float IcrForwardMeters,
        string IcrZone,
        float FrontLateralForceN,
        float RearLateralForceN,
        float FrontYawAccelerationDegreesPerSecondSquared,
        float RearYawAccelerationDegreesPerSecondSquared)
    {
        public static Sample From(
            float timeSeconds,
            float steerInput,
            VehicleState state,
            VehicleAxleGeometry geometry,
            AxleCenters centers,
            AxleCenters previousCenters)
        {
            Vector2 forward = new(state.Forward.X, state.Forward.Z);
            Vector2 right = new(state.Right.X, state.Right.Z);
            Vector2 frontDelta = centers.Front - previousCenters.Front;
            Vector2 rearDelta = centers.Rear - previousCenters.Rear;
            float frontStepRight = Vector2.Dot(frontDelta, right);
            float frontStepForward = Vector2.Dot(frontDelta, forward);
            float rearStepRight = Vector2.Dot(rearDelta, right);
            float rearStepForward = Vector2.Dot(rearDelta, forward);
            float roadWheelDegrees = (state.FrontLeftSteerAngleDegrees + state.FrontRightSteerAngleDegrees) * 0.5f;
            float roadWheelRadians = MathHelper.ToRadians(roadWheelDegrees);
            float playerExpectedYaw = state.SignedForwardSpeed * MathF.Tan(roadWheelRadians) / MathF.Max(0.25f, geometry.WheelbaseMeters);
            float internalExpectedYaw = playerExpectedYaw;
            float internalYawRatio = MathF.Abs(internalExpectedYaw) > 0.0001f
                ? state.YawRateRadiansPerSecond / internalExpectedYaw
                : 0f;
            CalculateIcr(state, out float icrRight, out float icrForward);
            string icrZone = ClassifyIcr(icrForward, geometry);
            float frontSide = MathF.Abs(frontStepRight);
            float rearSide = MathF.Abs(rearStepRight);

            return new Sample(
                "",
                timeSeconds,
                state.SpeedMetersPerSecond * 3.6f,
                steerInput,
                roadWheelDegrees,
                MathHelper.ToDegrees(state.YawRateRadiansPerSecond),
                0f,
                MathHelper.ToDegrees(playerExpectedYaw),
                MathHelper.ToDegrees(internalExpectedYaw),
                internalYawRatio,
                state.SignedForwardSpeed,
                state.LateralSpeed,
                state.ClassicBodySlipAngleDegrees,
                0f,
                frontStepRight,
                frontStepForward,
                rearStepRight,
                rearStepForward,
                0f,
                0f,
                rearSide / MathF.Max(0.0001f, frontSide),
                icrRight,
                icrForward,
                icrZone,
                state.FrontLeftLateralForceN + state.FrontRightLateralForceN,
                state.RearLeftLateralForceN + state.RearRightLateralForceN,
                state.ClassicFrontYawAccelerationDegreesPerSecondSquared,
                state.ClassicRearYawAccelerationDegreesPerSecondSquared) with
            {
                Label = timeSeconds <= 0f ? "initial" : "sample"
            };
        }

        private static void CalculateIcr(VehicleState state, out float icrRightMeters, out float icrForwardMeters)
        {
            float yawRate = state.YawRateRadiansPerSecond;
            if (MathF.Abs(yawRate) < 0.0001f)
            {
                icrRightMeters = float.PositiveInfinity;
                icrForwardMeters = float.PositiveInfinity;
                return;
            }

            icrRightMeters = -state.SignedForwardSpeed / yawRate;
            icrForwardMeters = state.LateralSpeed / yawRate;
        }

        private static string ClassifyIcr(float icrForwardMeters, VehicleAxleGeometry geometry)
        {
            if (!float.IsFinite(icrForwardMeters))
            {
                return "none";
            }

            if (icrForwardMeters > geometry.CgToFrontAxleMeters)
            {
                return "aheadFront";
            }

            if (icrForwardMeters < -geometry.CgToRearAxleMeters)
            {
                return "behindRear";
            }

            float rearDelta = MathF.Abs(icrForwardMeters + geometry.CgToRearAxleMeters);
            float frontDelta = MathF.Abs(icrForwardMeters - geometry.CgToFrontAxleMeters);
            return rearDelta <= frontDelta ? "nearRear" : "nearFront";
        }
    }
}
