using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicSlipGapProbe
{
    private const float Dt = 1f / 120f;

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engineParameters = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        ClassicFourWheelTyres tyres = ClassicFourWheelVehicleSimulator.ResolveClassicTyres(parameters, engineParameters.ClassicFourWheel);

        Console.WriteLine($"Classic slip-gap probe: {parameters.DisplayName}, model={engineParameters.HandlingModel}");
        Console.WriteLine(
            $"  front tyre source=vehicle stiffness={tyres.Front.CorneringStiffness:0.00} peak={tyres.Front.PeakSlipAngleDegrees:0.0}deg falloff={tyres.Front.FalloffSlipAngleDegrees:0.0}deg grip={tyres.Front.MaxGrip:0.00}/{tyres.Front.SlidingGrip:0.00}");
        Console.WriteLine(
            $"  rear tyre  source=vehicle stiffness={tyres.Rear.CorneringStiffness:0.00} peak={tyres.Rear.PeakSlipAngleDegrees:0.0}deg falloff={tyres.Rear.FalloffSlipAngleDegrees:0.0}deg grip={tyres.Rear.MaxGrip:0.00}/{tyres.Rear.SlidingGrip:0.00}");

        SlipGapSample[] steady =
        [
            RunSteadyCase(parameters, engineParameters, tyres, 80f, 0.45f, 0f, 1.8f, "80 coast medium"),
            RunSteadyCase(parameters, engineParameters, tyres, 100f, 0.35f, 0f, 1.8f, "100 coast medium"),
            RunSteadyCase(parameters, engineParameters, tyres, 120f, 0.35f, 0f, 1.8f, "120 coast medium"),
            RunSteadyCase(parameters, engineParameters, tyres, 150f, 0.18f, 0f, 2.4f, "150 coast mild"),
            RunSteadyCase(parameters, engineParameters, tyres, 150f, 0.35f, 0f, 2.4f, "150 coast medium"),
            RunSteadyCase(parameters, engineParameters, tyres, 150f, 0.65f, 0f, 2.4f, "150 coast hard"),
            RunSteadyCase(parameters, engineParameters, tyres, 150f, 0.18f, 0.25f, 2.4f, "150 25% mild"),
            RunSteadyCase(parameters, engineParameters, tyres, 150f, 0.35f, 0.25f, 2.4f, "150 25% medium"),
            RunSteadyCase(parameters, engineParameters, tyres, 150f, 0.65f, 0.25f, 2.4f, "150 25% hard"),
            RunSteadyCase(parameters, engineParameters, tyres, 150f, 0.35f, 1f, 2.4f, "150 full medium"),
            RunSteadyCase(parameters, engineParameters, tyres, 150f, 0.65f, 1f, 2.4f, "150 full hard")
        ];

        foreach (SlipGapSample sample in steady)
        {
            PrintSample(sample);
        }

        RunCornerChainCase(parameters, engineParameters, tyres);
        PrintWarnings(steady);
        Console.WriteLine("Classic slip-gap probe complete.");
    }

    private static SlipGapSample RunSteadyCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        ClassicFourWheelTyres tyres,
        float speedKmh,
        float steer,
        float throttle,
        float seconds,
        string label)
    {
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engineParameters, speedKmh);
        int ticks = Math.Max(1, (int)MathF.Round(seconds / Dt));
        SlipGapAccumulator accumulator = new(tyres);
        float startSpeedKmh = simulator.State.SpeedMetersPerSecond * 3.6f;
        float startRpm = simulator.State.Rpm;

        for (int i = 0; i < ticks; i++)
        {
            simulator.Update(new VehicleInput(throttle, 0f, steer), Dt);
            accumulator.Add(simulator.State);
        }

        RequireFinite(simulator.State);
        return accumulator.ToSample(label, speedKmh, throttle, steer, startSpeedKmh, simulator.State.SpeedMetersPerSecond * 3.6f, startRpm, simulator.State.Rpm);
    }

    private static void RunCornerChainCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        ClassicFourWheelTyres tyres)
    {
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engineParameters, 130f);
        SlipGapSample[] samples =
        [
            RunPhase(simulator, tyres, "chain T1", 0.08f, 0f, 0.32f, 1.0f, 4),
            RunPhase(simulator, tyres, "chain T2", 0f, 0.32f, 0.95f, 1.0f, 3),
            RunPhase(simulator, tyres, "chain T2 exit", 0.25f, 0f, 0.15f, 0.55f, 3),
            RunPhase(simulator, tyres, "chain T3", 0f, 0.24f, -0.85f, 1.0f, 3),
            RunPhase(simulator, tyres, "chain T4", 0.12f, 0f, 0.70f, 1.0f, 3)
        ];

        foreach (SlipGapSample sample in samples)
        {
            PrintSample(sample);
        }
    }

    private static SlipGapSample RunPhase(
        ClassicFourWheelVehicleSimulator simulator,
        ClassicFourWheelTyres tyres,
        string label,
        float throttle,
        float brake,
        float steer,
        float seconds,
        int gear)
    {
        simulator.State.Gear = gear;
        int ticks = Math.Max(1, (int)MathF.Round(seconds / Dt));
        SlipGapAccumulator accumulator = new(tyres);
        float startSpeedKmh = simulator.State.SpeedMetersPerSecond * 3.6f;
        float startRpm = simulator.State.Rpm;

        for (int i = 0; i < ticks; i++)
        {
            simulator.Update(new VehicleInput(throttle, brake, steer), Dt);
            accumulator.Add(simulator.State);
        }

        RequireFinite(simulator.State);
        return accumulator.ToSample(label, startSpeedKmh, throttle, steer, startSpeedKmh, simulator.State.SpeedMetersPerSecond * 3.6f, startRpm, simulator.State.Rpm);
    }

    private static ClassicFourWheelVehicleSimulator CreateSimulator(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        float speedKmh)
    {
        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engineParameters);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = speedKmh >= 130f ? 4 : 3;
        simulator.State.Velocity = new Vector2(0f, speedKmh / 3.6f);
        return simulator;
    }

    private static void PrintSample(SlipGapSample sample)
    {
        Console.WriteLine(
            $"  {sample.Label,-16} steer={sample.SteerInput:0.00} thr={sample.Throttle:0.00} " +
            $"speed={sample.StartSpeedKmh:0}->{sample.EndSpeedKmh:0} drop={sample.SpeedDropKmh:+0.0;-0.0;0.0}km/h " +
            $"rpmDrop={sample.RpmDrop:+0;-0;0} lat={sample.AverageAbsLateralAcceleration / 9.81f:0.00}g " +
            $"slipF/R={sample.AverageFrontSlipDegrees:0.0}/{sample.AverageRearSlipDegrees:0.0}deg gap={sample.AverageRearMinusFrontSlipDegrees:+0.0;-0.0;0.0}deg " +
            $"peakUseF/R={sample.AverageFrontPeakUse:0.00}/{sample.AverageRearPeakUse:0.00} " +
            $"gripF/R={sample.AverageFrontGripUsage:0.00}/{sample.AverageRearGripUsage:0.00} limit={sample.LimitingAxle} " +
            $"body={sample.PeakBodySlipDegrees:0.0}deg yaw={sample.PeakYawRateDegreesPerSecond:0.0}deg/s " +
            $"bodyDamp={sample.AverageAbsBodyDampingForceN:0}N retain={sample.AverageSpeedRetentionForceN:0}N");
    }

    private static void PrintWarnings(IEnumerable<SlipGapSample> samples)
    {
        foreach (SlipGapSample sample in samples)
        {
            if (sample.Label.Contains("medium", StringComparison.OrdinalIgnoreCase) &&
                sample.AverageRearMinusFrontSlipDegrees > 1.5f)
            {
                Console.WriteLine($"  warning: {sample.Label} rear/front slip gap is high at {sample.AverageRearMinusFrontSlipDegrees:0.0}deg.");
            }

            if (sample.Label.Contains("medium", StringComparison.OrdinalIgnoreCase) &&
                sample.AverageFrontPeakUse < 0.35f)
            {
                Console.WriteLine($"  warning: {sample.Label} front peak use is low at {sample.AverageFrontPeakUse:0.00}.");
            }
        }
    }

    private static void RequireFinite(VehicleState state)
    {
        if (!float.IsFinite(state.Position.X) ||
            !float.IsFinite(state.Position.Z) ||
            !float.IsFinite(state.Velocity.X) ||
            !float.IsFinite(state.Velocity.Y) ||
            !float.IsFinite(state.HeadingRadians) ||
            !float.IsFinite(state.YawRateRadiansPerSecond))
        {
            throw new InvalidOperationException("Classic slip-gap probe failed: vehicle state became non-finite.");
        }
    }

    private sealed class SlipGapAccumulator
    {
        private readonly float _frontPeakSlipDegrees;
        private readonly float _rearPeakSlipDegrees;
        private int _samples;
        private float _sumFrontSlip;
        private float _sumRearSlip;
        private float _sumGap;
        private float _sumFrontPeakUse;
        private float _sumRearPeakUse;
        private float _sumFrontGripUsage;
        private float _sumRearGripUsage;
        private float _sumAbsLateralAcceleration;
        private float _sumAbsBodyDampingForce;
        private float _sumSpeedRetentionForce;
        private float _peakBodySlip;
        private float _peakYawRate;

        public SlipGapAccumulator(ClassicFourWheelTyres tyres)
        {
            _frontPeakSlipDegrees = MathF.Max(0.1f, tyres.Front.PeakSlipAngleDegrees);
            _rearPeakSlipDegrees = MathF.Max(0.1f, tyres.Rear.PeakSlipAngleDegrees);
        }

        public void Add(VehicleState state)
        {
            float frontSlip = (
                MathF.Abs(state.FrontLeftSlipAngleDegrees) +
                MathF.Abs(state.FrontRightSlipAngleDegrees)) * 0.5f;
            float rearSlip = (
                MathF.Abs(state.RearLeftSlipAngleDegrees) +
                MathF.Abs(state.RearRightSlipAngleDegrees)) * 0.5f;
            float frontGripUsage = MathF.Max(state.FrontLeftGripUsage, state.FrontRightGripUsage);
            float rearGripUsage = MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage);

            _samples++;
            _sumFrontSlip += frontSlip;
            _sumRearSlip += rearSlip;
            _sumGap += rearSlip - frontSlip;
            _sumFrontPeakUse += frontSlip / _frontPeakSlipDegrees;
            _sumRearPeakUse += rearSlip / _rearPeakSlipDegrees;
            _sumFrontGripUsage += frontGripUsage;
            _sumRearGripUsage += rearGripUsage;
            _sumAbsLateralAcceleration += MathF.Abs(state.LateralAcceleration);
            _sumAbsBodyDampingForce += MathF.Abs(state.ClassicBodySlipDampingForceN);
            _sumSpeedRetentionForce += state.ClassicCorneringCleanupSpeedRetentionForceN;
            _peakBodySlip = MathF.Max(_peakBodySlip, MathF.Abs(state.ClassicBodySlipAngleDegrees));
            _peakYawRate = MathF.Max(_peakYawRate, MathF.Abs(MathHelper.ToDegrees(state.YawRateRadiansPerSecond)));
        }

        public SlipGapSample ToSample(
            string label,
            float nominalSpeedKmh,
            float throttle,
            float steer,
            float startSpeedKmh,
            float endSpeedKmh,
            float startRpm,
            float endRpm)
        {
            float count = MathF.Max(1f, _samples);
            float frontGrip = _sumFrontGripUsage / count;
            float rearGrip = _sumRearGripUsage / count;
            string limitingAxle = frontGrip > rearGrip + 0.04f
                ? "front"
                : rearGrip > frontGrip + 0.04f ? "rear" : "shared";
            return new SlipGapSample(
                label,
                nominalSpeedKmh,
                throttle,
                steer,
                startSpeedKmh,
                endSpeedKmh,
                startSpeedKmh - endSpeedKmh,
                startRpm - endRpm,
                _sumFrontSlip / count,
                _sumRearSlip / count,
                _sumGap / count,
                _sumFrontPeakUse / count,
                _sumRearPeakUse / count,
                frontGrip,
                rearGrip,
                _sumAbsLateralAcceleration / count,
                _sumAbsBodyDampingForce / count,
                _sumSpeedRetentionForce / count,
                _peakBodySlip,
                _peakYawRate,
                limitingAxle);
        }
    }

    private readonly record struct SlipGapSample(
        string Label,
        float NominalSpeedKmh,
        float Throttle,
        float SteerInput,
        float StartSpeedKmh,
        float EndSpeedKmh,
        float SpeedDropKmh,
        float RpmDrop,
        float AverageFrontSlipDegrees,
        float AverageRearSlipDegrees,
        float AverageRearMinusFrontSlipDegrees,
        float AverageFrontPeakUse,
        float AverageRearPeakUse,
        float AverageFrontGripUsage,
        float AverageRearGripUsage,
        float AverageAbsLateralAcceleration,
        float AverageAbsBodyDampingForceN,
        float AverageSpeedRetentionForceN,
        float PeakBodySlipDegrees,
        float PeakYawRateDegreesPerSecond,
        string LimitingAxle);

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
