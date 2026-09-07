using Microsoft.Xna.Framework;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicContactFramePhase2Probe
{
    private const float Gravity = 9.81f;
    private const float Dt = 1f / 60f;

    public static void Run()
    {
        VehicleSimulationParameters parameters = new();
        SimulationEngineParameters engine = new();
        Console.WriteLine("Classic contact-frame Phase 2 probe");
        CompareFlat("flat acceleration", parameters, engine, new VehicleInput(1f, 0f, 0f), 0f, 2.0f);
        CompareFlat("flat braking", parameters, engine, new VehicleInput(0f, 1f, 0f), 30f, 1.0f);
        CompareFlat("flat steady cornering", parameters, engine, new VehicleInput(0.22f, 0f, 0.35f), 22f, 2.5f);
        ProbeSlope("10deg uphill coast", parameters, engine, MathHelper.ToRadians(10f), uphill: true);
        ProbeSlope("10deg downhill coast", parameters, engine, MathHelper.ToRadians(10f), uphill: false);
        ProbeBank("10deg constant bank", parameters, engine, MathHelper.ToRadians(10f));
        ProbeOneWheelMiss(parameters, engine);
    }

    private static void CompareFlat(
        string label,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        VehicleInput input,
        float startSpeedMetersPerSecond,
        float seconds)
    {
        ProbeSample legacy = RunCase(new LegacyFlatSampler(), parameters, engine, input, startSpeedMetersPerSecond, seconds);
        ProbeSample authored = RunCase(new PlaneContactSampler(Vector3.Up, "Flat_Driveable"), parameters, engine, input, startSpeedMetersPerSecond, seconds);
        Console.WriteLine($"{label}:");
        PrintComparison("  speed", legacy.SpeedMetersPerSecond, authored.SpeedMetersPerSecond, "m/s");
        PrintComparison("  long accel", legacy.LongitudinalAcceleration, authored.LongitudinalAcceleration, "m/s2");
        PrintComparison("  lateral accel", legacy.LateralAcceleration, authored.LateralAcceleration, "m/s2");
        PrintComparison("  yaw rate", MathHelper.ToDegrees(legacy.YawRateRadiansPerSecond), MathHelper.ToDegrees(authored.YawRateRadiansPerSecond), "deg/s");
        PrintComparison("  avg slip", legacy.AverageSlipAngleDegrees, authored.AverageSlipAngleDegrees, "deg");
        PrintComparison("  FL forward force", legacy.FrontLeftForwardForceN, authored.FrontLeftForwardForceN, "N");
        PrintComparison("  FR forward force", legacy.FrontRightForwardForceN, authored.FrontRightForwardForceN, "N");
        PrintComparison("  RL forward force", legacy.RearLeftForwardForceN, authored.RearLeftForwardForceN, "N");
        PrintComparison("  RR forward force", legacy.RearRightForwardForceN, authored.RearRightForwardForceN, "N");
        PrintComparison("  FL lateral force", legacy.FrontLeftLateralForceN, authored.FrontLeftLateralForceN, "N");
        PrintComparison("  FR lateral force", legacy.FrontRightLateralForceN, authored.FrontRightLateralForceN, "N");
        PrintComparison("  RL lateral force", legacy.RearLeftLateralForceN, authored.RearLeftLateralForceN, "N");
        PrintComparison("  RR lateral force", legacy.RearRightLateralForceN, authored.RearRightLateralForceN, "N");
    }

    private static void ProbeSlope(
        string label,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        float slopeRadians,
        bool uphill)
    {
        float sign = uphill ? -1f : 1f;
        Vector3 normal = Vector3.Normalize(new Vector3(0f, MathF.Cos(slopeRadians), sign * MathF.Sin(slopeRadians)));
        ProbeSample sample = RunCase(new PlaneContactSampler(normal, label + "_Driveable"), parameters, engine, new VehicleInput(0f, 0f, 0f), 0.5f, 0.25f);
        float expectedAlongSlope = (uphill ? -1f : 1f) * Gravity * MathF.Sin(slopeRadians);
        float expectedHorizontalForward = expectedAlongSlope * MathF.Cos(slopeRadians);
        Console.WriteLine($"{label}:");
        Console.WriteLine($"  expected along-slope gravity: {expectedAlongSlope:0.###} m/s2");
        Console.WriteLine($"  expected horizontal forward component: {expectedHorizontalForward:0.###} m/s2");
        Console.WriteLine($"  simulator longitudinal gravity: {sample.TrackLongitudinalGravityForceN / MathF.Max(1f, parameters.MassKg):0.###} m/s2");
        Console.WriteLine($"  simulator longitudinal accel: {sample.LongitudinalAcceleration:0.###} m/s2");
    }

    private static void ProbeBank(
        string label,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        float bankRadians)
    {
        Vector3 normal = Vector3.Normalize(new Vector3(-MathF.Sin(bankRadians), MathF.Cos(bankRadians), 0f));
        ProbeSample sample = RunCase(new PlaneContactSampler(normal, label + "_Driveable"), parameters, engine, new VehicleInput(0f, 0f, 0f), 22f, 0.25f);
        float expectedLateral = -Gravity * MathF.Sin(bankRadians) * MathF.Cos(bankRadians);
        Console.WriteLine($"{label}:");
        Console.WriteLine($"  expected horizontal lateral gravity component: {expectedLateral:0.###} m/s2");
        Console.WriteLine($"  simulator lateral gravity: {sample.TrackLateralGravityForceN / MathF.Max(1f, parameters.MassKg):0.###} m/s2");
        Console.WriteLine($"  simulator lateral accel: {sample.LateralAcceleration:0.###} m/s2");
    }

    private static void ProbeOneWheelMiss(VehicleSimulationParameters parameters, SimulationEngineParameters engine)
    {
        ProbeSample sample = RunCase(new OneWheelMissSampler(), parameters, engine, new VehicleInput(1f, 0f, 0f), 0f, Dt);
        Console.WriteLine("one-wheel contact miss:");
        Console.WriteLine($"  FL missed: {sample.FrontLeftMissed}, FL forward force {sample.FrontLeftForwardForceN:0.###} N, FL lateral force {sample.FrontLeftLateralForceN:0.###} N");
        Console.WriteLine($"  FR missed: {sample.FrontRightMissed}, RL missed: {sample.RearLeftMissed}, RR missed: {sample.RearRightMissed}");
    }

    private static ProbeSample RunCase(
        ITrackSurfaceSampler sampler,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        VehicleInput input,
        float startSpeedMetersPerSecond,
        float seconds)
    {
        ClassicFourWheelVehicleSimulator simulator = new(
            sampler,
            Vector3.Zero,
            0f,
            parameters,
            engine)
        {
            DisablePhysicalChassisPhase3BForProbe = true
        };
        simulator.State.Velocity = new Vector2(0f, startSpeedMetersPerSecond);
        int ticks = Math.Max(1, (int)MathF.Round(seconds / Dt));
        for (int i = 0; i < ticks; i++)
        {
            simulator.Update(input, Dt);
        }

        VehicleState state = simulator.State;
        return new ProbeSample(
            state.SpeedMetersPerSecond,
            state.LongitudinalAcceleration,
            state.LateralAcceleration,
            state.YawRateRadiansPerSecond,
            state.AverageSlipAngleDegrees,
            state.FrontLeftLongitudinalForceN,
            state.FrontRightLongitudinalForceN,
            state.RearLeftLongitudinalForceN,
            state.RearRightLongitudinalForceN,
            state.FrontLeftLateralForceN,
            state.FrontRightLateralForceN,
            state.RearLeftLateralForceN,
            state.RearRightLateralForceN,
            state.TrackLongitudinalGravityForceN,
            state.TrackLateralGravityForceN,
            state.FrontLeftContactMissed,
            state.FrontRightContactMissed,
            state.RearLeftContactMissed,
            state.RearRightContactMissed);
    }

    private static void PrintComparison(string label, float legacy, float authored, string unit)
    {
        Console.WriteLine($"{label}: legacy={legacy:0.###} authored={authored:0.###} diff={authored - legacy:+0.###;-0.###;0} {unit}");
    }

    private readonly record struct ProbeSample(
        float SpeedMetersPerSecond,
        float LongitudinalAcceleration,
        float LateralAcceleration,
        float YawRateRadiansPerSecond,
        float AverageSlipAngleDegrees,
        float FrontLeftForwardForceN,
        float FrontRightForwardForceN,
        float RearLeftForwardForceN,
        float RearRightForwardForceN,
        float FrontLeftLateralForceN,
        float FrontRightLateralForceN,
        float RearLeftLateralForceN,
        float RearRightLateralForceN,
        float TrackLongitudinalGravityForceN,
        float TrackLateralGravityForceN,
        bool FrontLeftMissed,
        bool FrontRightMissed,
        bool RearLeftMissed,
        bool RearRightMissed);

    private sealed class LegacyFlatSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position) => new("ROAD", 1f);
    }

    private class PlaneContactSampler : ITrackSurfaceSampler
    {
        private readonly Vector3 _normal;
        private readonly string _sourceName;

        public PlaneContactSampler(Vector3 normal, string sourceName)
        {
            _normal = Vector3.Normalize(normal);
            _sourceName = sourceName;
        }

        public bool HasAuthoredSurfaceContact => true;

        public SurfaceSample Sample(Vector3 position) => new("ROAD", 1f);

        public float GetElevation(Vector2 position)
        {
            return -(_normal.X * position.X + _normal.Z * position.Y) / MathF.Max(0.001f, _normal.Y);
        }

        public virtual bool TryGetSurfaceContact(Vector3 queryPosition, float downwardRangeMeters, out TrackSurfaceContact contact)
        {
            float y = GetElevation(new Vector2(queryPosition.X, queryPosition.Z));
            if (y > queryPosition.Y + 0.02f || y < queryPosition.Y - downwardRangeMeters)
            {
                contact = default;
                return false;
            }

            contact = new TrackSurfaceContact(
                new Vector3(queryPosition.X, y, queryPosition.Z),
                _normal,
                _sourceName,
                0,
                1);
            return true;
        }
    }

    private sealed class OneWheelMissSampler : PlaneContactSampler
    {
        public OneWheelMissSampler()
            : base(Vector3.Up, "OneWheelMiss_Driveable")
        {
        }

        public override bool TryGetSurfaceContact(Vector3 queryPosition, float downwardRangeMeters, out TrackSurfaceContact contact)
        {
            if (queryPosition.X < -0.1f && queryPosition.Z > 0f)
            {
                contact = default;
                return false;
            }

            return base.TryGetSurfaceContact(queryPosition, downwardRangeMeters, out contact);
        }
    }
}
