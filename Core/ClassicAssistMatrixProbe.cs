using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicAssistMatrixProbe
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

        Console.WriteLine($"Classic assist matrix probe: {parameters.DisplayName}, model={engineParameters.HandlingModel}");
        Console.WriteLine("  profiles: all-on, each assist disabled independently, all cleanup assists disabled");
        Console.WriteLine("  columns: speed/RPM drop, slip gap, body slip, yaw, lateral g, body damping, lateral damping estimate, retention, rear follow, yaw recovery");

        AssistProfile[] profiles =
        [
            new("all on", new ClassicFourWheelAssistOptions()),
            new("no body", new ClassicFourWheelAssistOptions { BodySlipDampingEnabled = false }),
            new("no lateral", new ClassicFourWheelAssistOptions { LateralVelocityDampingEnabled = false }),
            new("no rearFollow", new ClassicFourWheelAssistOptions { RearFollowEnabled = false }),
            new("no yawRec", new ClassicFourWheelAssistOptions { YawRecoveryEnabled = false }),
            new("no retention", new ClassicFourWheelAssistOptions { SpeedRetentionEnabled = false }),
            new("all cleanup off", new ClassicFourWheelAssistOptions
            {
                BodySlipDampingEnabled = false,
                LateralVelocityDampingEnabled = false,
                RearFollowEnabled = false,
                YawRecoveryEnabled = false,
                SpeedRetentionEnabled = false
            })
        ];

        RunSteadyTable("150 25% medium", parameters, engineParameters, profiles, 150f, 0.35f, 0.25f, 2.4f);
        RunSteadyTable("150 25% hard", parameters, engineParameters, profiles, 150f, 0.65f, 0.25f, 2.4f);
        RunFlickTable("flick 100", parameters, engineParameters, profiles, 100f);
        RunCornerChainTable(parameters, engineParameters, profiles);
        Console.WriteLine("Classic assist matrix probe complete.");
    }

    private static void RunSteadyTable(
        string label,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        IReadOnlyList<AssistProfile> profiles,
        float speedKmh,
        float steer,
        float throttle,
        float seconds)
    {
        Console.WriteLine($"  {label}");
        foreach (AssistProfile profile in profiles)
        {
            ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engineParameters, speedKmh, profile.Options);
            int ticks = Math.Max(1, (int)MathF.Round(seconds / Dt));
            MatrixAccumulator accumulator = new(parameters, engineParameters, profile.Options);
            float startSpeedKmh = simulator.State.SpeedMetersPerSecond * 3.6f;
            float startRpm = simulator.State.Rpm;

            for (int i = 0; i < ticks; i++)
            {
                simulator.Update(new VehicleInput(throttle, 0f, steer), Dt);
                accumulator.Add(simulator.State);
            }

            RequireFinite(simulator.State);
            PrintSample(profile.Name, accumulator.ToSample(startSpeedKmh, simulator.State.SpeedMetersPerSecond * 3.6f, startRpm, simulator.State.Rpm));
        }
    }

    private static void RunFlickTable(
        string label,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        IReadOnlyList<AssistProfile> profiles,
        float speedKmh)
    {
        Console.WriteLine($"  {label}");
        foreach (AssistProfile profile in profiles)
        {
            ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engineParameters, speedKmh, profile.Options);
            MatrixAccumulator accumulator = new(parameters, engineParameters, profile.Options);
            float startSpeedKmh = simulator.State.SpeedMetersPerSecond * 3.6f;
            float startRpm = simulator.State.Rpm;

            RunPhase(simulator, accumulator, 0.20f, 0f, 0.65f, 0.35f);
            RunPhase(simulator, accumulator, 0.20f, 0f, -0.65f, 0.35f);
            RunPhase(simulator, accumulator, 0.10f, 0f, 0f, 1.2f);

            RequireFinite(simulator.State);
            PrintSample(profile.Name, accumulator.ToSample(startSpeedKmh, simulator.State.SpeedMetersPerSecond * 3.6f, startRpm, simulator.State.Rpm));
        }
    }

    private static void RunCornerChainTable(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        IReadOnlyList<AssistProfile> profiles)
    {
        Console.WriteLine("  chain T1-T4");
        foreach (AssistProfile profile in profiles)
        {
            ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engineParameters, 130f, profile.Options);
            MatrixAccumulator accumulator = new(parameters, engineParameters, profile.Options);
            float startSpeedKmh = simulator.State.SpeedMetersPerSecond * 3.6f;
            float startRpm = simulator.State.Rpm;

            simulator.State.Gear = 4;
            RunPhase(simulator, accumulator, 0.08f, 0f, 0.32f, 1.0f);
            simulator.State.Gear = 3;
            RunPhase(simulator, accumulator, 0f, 0.32f, 0.95f, 1.0f);
            RunPhase(simulator, accumulator, 0.25f, 0f, 0.15f, 0.55f);
            RunPhase(simulator, accumulator, 0f, 0.24f, -0.85f, 1.0f);
            RunPhase(simulator, accumulator, 0.12f, 0f, 0.70f, 1.0f);

            RequireFinite(simulator.State);
            PrintSample(profile.Name, accumulator.ToSample(startSpeedKmh, simulator.State.SpeedMetersPerSecond * 3.6f, startRpm, simulator.State.Rpm));
        }
    }

    private static void RunPhase(
        ClassicFourWheelVehicleSimulator simulator,
        MatrixAccumulator accumulator,
        float throttle,
        float brake,
        float steer,
        float seconds)
    {
        int ticks = Math.Max(1, (int)MathF.Round(seconds / Dt));
        for (int i = 0; i < ticks; i++)
        {
            simulator.Update(new VehicleInput(throttle, brake, steer), Dt);
            accumulator.Add(simulator.State);
        }
    }

    private static ClassicFourWheelVehicleSimulator CreateSimulator(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        float speedKmh,
        ClassicFourWheelAssistOptions assistOptions)
    {
        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engineParameters)
        {
            AssistOptions = assistOptions
        };
        simulator.SetManualTransmission(true);
        simulator.State.Gear = speedKmh >= 130f ? 4 : 3;
        simulator.State.Velocity = new Vector2(0f, speedKmh / 3.6f);
        return simulator;
    }

    private static void PrintSample(string profile, MatrixSample sample)
    {
        Console.WriteLine(
            $"    {profile,-15} drop={sample.SpeedDropKmh,+5:0.0}km/h rpm={sample.RpmDrop,+6:0} " +
            $"slipF/R={sample.FrontSlipDegrees:0.0}/{sample.RearSlipDegrees:0.0} gap={sample.RearMinusFrontSlipDegrees,+5:0.0} " +
            $"body={sample.PeakBodySlipDegrees:0.0} yaw={sample.PeakYawRateDegreesPerSecond:0.0} lat={sample.AverageAbsLateralAcceleration / 9.81f:0.00}g " +
            $"bodyDamp={sample.AverageAbsBodyDampingForceN:0}N latDamp={sample.AverageAbsLateralDampingForceN:0}N retain={sample.AverageSpeedRetentionForceN:0}N " +
            $"rearFollow={sample.AverageAbsRearFollowAccelerationDegreesPerSecondSquared:0}d/s2 yawRec={sample.AverageAbsYawRecoveryAccelerationDegreesPerSecondSquared:0}d/s2");
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
            throw new InvalidOperationException("Classic assist matrix probe failed: vehicle state became non-finite.");
        }
    }

    private readonly record struct AssistProfile(string Name, ClassicFourWheelAssistOptions Options);

    private sealed class MatrixAccumulator
    {
        private readonly VehicleSimulationParameters _parameters;
        private readonly SimulationEngineParameters _engineParameters;
        private readonly ClassicFourWheelAssistOptions _assistOptions;
        private int _samples;
        private float _sumFrontSlip;
        private float _sumRearSlip;
        private float _sumGap;
        private float _sumAbsLateralAcceleration;
        private float _sumAbsBodyDampingForce;
        private float _sumAbsLateralDampingForce;
        private float _sumSpeedRetentionForce;
        private float _sumAbsRearFollowAcceleration;
        private float _sumAbsYawRecoveryAcceleration;
        private float _peakBodySlip;
        private float _peakYawRate;

        public MatrixAccumulator(
            VehicleSimulationParameters parameters,
            SimulationEngineParameters engineParameters,
            ClassicFourWheelAssistOptions assistOptions)
        {
            _parameters = parameters;
            _engineParameters = engineParameters;
            _assistOptions = assistOptions;
        }

        public void Add(VehicleState state)
        {
            float frontSlip = (
                MathF.Abs(state.FrontLeftSlipAngleDegrees) +
                MathF.Abs(state.FrontRightSlipAngleDegrees)) * 0.5f;
            float rearSlip = (
                MathF.Abs(state.RearLeftSlipAngleDegrees) +
                MathF.Abs(state.RearRightSlipAngleDegrees)) * 0.5f;
            float lateralDampingForce = _assistOptions.LateralVelocityDampingEnabled
                ? state.LateralSpeed * _parameters.MassKg * MathF.Max(0f, _engineParameters.ClassicFourWheel.Yaw.LateralVelocityDamping)
                : 0f;

            _samples++;
            _sumFrontSlip += frontSlip;
            _sumRearSlip += rearSlip;
            _sumGap += rearSlip - frontSlip;
            _sumAbsLateralAcceleration += MathF.Abs(state.LateralAcceleration);
            _sumAbsBodyDampingForce += MathF.Abs(state.ClassicBodySlipDampingForceN);
            _sumAbsLateralDampingForce += MathF.Abs(lateralDampingForce);
            _sumSpeedRetentionForce += state.ClassicCorneringCleanupSpeedRetentionForceN;
            _sumAbsRearFollowAcceleration += MathF.Abs(state.ClassicRearFollowAccelerationDegreesPerSecondSquared);
            _sumAbsYawRecoveryAcceleration += MathF.Abs(state.ClassicYawRecoveryAccelerationDegreesPerSecondSquared);
            _peakBodySlip = MathF.Max(_peakBodySlip, MathF.Abs(state.ClassicBodySlipAngleDegrees));
            _peakYawRate = MathF.Max(_peakYawRate, MathF.Abs(MathHelper.ToDegrees(state.YawRateRadiansPerSecond)));
        }

        public MatrixSample ToSample(float startSpeedKmh, float endSpeedKmh, float startRpm, float endRpm)
        {
            float count = MathF.Max(1f, _samples);
            return new MatrixSample(
                startSpeedKmh - endSpeedKmh,
                startRpm - endRpm,
                _sumFrontSlip / count,
                _sumRearSlip / count,
                _sumGap / count,
                _sumAbsLateralAcceleration / count,
                _sumAbsBodyDampingForce / count,
                _sumAbsLateralDampingForce / count,
                _sumSpeedRetentionForce / count,
                _sumAbsRearFollowAcceleration / count,
                _sumAbsYawRecoveryAcceleration / count,
                _peakBodySlip,
                _peakYawRate);
        }
    }

    private readonly record struct MatrixSample(
        float SpeedDropKmh,
        float RpmDrop,
        float FrontSlipDegrees,
        float RearSlipDegrees,
        float RearMinusFrontSlipDegrees,
        float AverageAbsLateralAcceleration,
        float AverageAbsBodyDampingForceN,
        float AverageAbsLateralDampingForceN,
        float AverageSpeedRetentionForceN,
        float AverageAbsRearFollowAccelerationDegreesPerSecondSquared,
        float AverageAbsYawRecoveryAccelerationDegreesPerSecondSquared,
        float PeakBodySlipDegrees,
        float PeakYawRateDegreesPerSecond);

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}

