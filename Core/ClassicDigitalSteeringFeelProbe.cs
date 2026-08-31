using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicDigitalSteeringFeelProbe
{
    private const float Dt = 1f / 120f;
    private const int Gear = 4;

    private static readonly float[] SpeedsKmh = [60f, 120f, 180f];
    private static readonly float[] Checkpoints = [0.08f, 0.20f, 0.35f, 0.70f, 1.20f, 1.60f];

    private static readonly TimingVariant[] TimingVariants =
    [
        new("fast", 3.4f, 4.8f, 0.22f, 6.0f, 2.2f),
        new("veryFast", 4.2f, 6.2f, 0.18f, 7.0f, 2.5f),
        new("aggressive", 5.2f, 7.8f, 0.14f, 8.0f, 3.0f)
    ];

    private static readonly Scenario[] Scenarios =
    [
        new("tap", t => t < 0.08f ? 1f : 0f),
        new("shortHold", t => t < 0.35f ? 1f : 0f),
        new("longHold", t => t < 1.20f ? 1f : 0f),
        new("release", t => t < 0.70f ? 1f : 0f),
        new("counter", t => t < 0.45f ? 1f : t < 0.90f ? -1f : 0f)
    ];

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

        Console.WriteLine($"Classic digital steering feel probe: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine("  cleanup=off; digital input shapes normalized command only, then physical envelope maps to road-wheel angle");
        Console.WriteLine("  profile speed scenario t raw norm angle hold rate envelopeN/O");

        foreach (TimingVariant variant in TimingVariants)
        {
            SimulationEngineParameters variantEngine = CloneWithTiming(engine, variant);
            foreach (float speedKmh in SpeedsKmh)
            {
                foreach (Scenario scenario in Scenarios)
                {
                    RunScenario(parameters, variantEngine, variant.Label, speedKmh, scenario);
                }
            }
        }

        Console.WriteLine("Classic digital steering feel probe complete.");
    }

    private static void RunScenario(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        string profile,
        float speedKmh,
        Scenario scenario)
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
        simulator.State.Velocity = new Vector2(0f, speedKmh / 3.6f);

        int maxTicks = (int)MathF.Ceiling(1.6f / Dt);
        int nextCheckpoint = 0;
        for (int i = 0; i <= maxTicks; i++)
        {
            float time = i * Dt;
            float raw = scenario.InputAt(time);
            simulator.Update(new VehicleInput(0.20f, 0f, raw), Dt);

            while (nextCheckpoint < Checkpoints.Length && time + Dt >= Checkpoints[nextCheckpoint])
            {
                PrintSample(profile, speedKmh, scenario.Label, Checkpoints[nextCheckpoint], raw, simulator.State);
                nextCheckpoint++;
            }
        }
    }

    private static void PrintSample(string profile, float speedKmh, string label, float time, float rawInput, VehicleState state)
    {
        float roadWheel = (state.FrontLeftSteerAngleDegrees + state.FrontRightSteerAngleDegrees) * 0.5f;
        Console.WriteLine(
            $"  {profile,-10} {speedKmh,5:F0} {label,-9} {time,4:F2} {rawInput,4:F1} " +
            $"{state.SteeringNormalizedCommand,6:F2} {roadWheel,6:F2} " +
            $"{state.SteeringDigitalHoldSeconds,5:F2} {state.SteeringCommandRatePerSecond,5:F2} " +
            $"{state.SteeringPhysicalNormalAngleDegrees,5:F2}/{state.SteeringPhysicalOverdriveAngleDegrees,5:F2}");
    }

    private static SimulationEngineParameters CloneWithTiming(SimulationEngineParameters source, TimingVariant variant)
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
                    OverdriveLateralAccelerationG = steering.OverdriveLateralAccelerationG,
                    NormalCommand = steering.NormalCommand,
                    MinimumHighSpeedAngleDegrees = steering.MinimumHighSpeedAngleDegrees,
                    NormalPeakSlipFraction = steering.NormalPeakSlipFraction,
                    OverdrivePeakSlipFraction = steering.OverdrivePeakSlipFraction,
                    TransientPeakSlipFraction = steering.TransientPeakSlipFraction,
                    TransientBoostSeconds = steering.TransientBoostSeconds,
                    DigitalInitialCommandRatePerSecond = variant.InitialRate,
                    DigitalSustainedCommandRatePerSecond = variant.SustainedRate,
                    DigitalRiseAccelerationSeconds = variant.RiseSeconds,
                    DigitalReleaseCommandRatePerSecond = variant.ReleaseRate,
                    DigitalCounterSteerRateMultiplier = variant.CounterMultiplier
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

    private readonly record struct Scenario(string Label, Func<float, float> InputAt);

    private readonly record struct TimingVariant(
        string Label,
        float InitialRate,
        float SustainedRate,
        float RiseSeconds,
        float ReleaseRate,
        float CounterMultiplier);

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
