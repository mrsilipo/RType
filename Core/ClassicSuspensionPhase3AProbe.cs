using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicSuspensionPhase3AProbe
{
    private const float Gravity = 9.81f;
    private const float Dt = 1f / 60f;
    private const string EkReferenceVehiclePath = "Data/PurchaseCars/2000_Ek9_Stock.json";

    public static void Run()
    {
        VehicleSimulationParameters parameters = VehicleBuildDefinitionLoader.LoadSimulationParameters(EkReferenceVehiclePath);
        SimulationEngineParameters engine = new();
        Console.WriteLine("Classic suspension Phase 3A shadow probe");
        Console.WriteLine($"  Vehicle: {parameters.DisplayName} ({EkReferenceVehiclePath})");
        Console.WriteLine("  Mode: diagnostic shadow only. Chassis Y/pitch/roll, tyre loads, tyre forces, yaw, and Phase 2 gravity remain authoritative.");
        Console.WriteLine($"  Wheel radius: {parameters.WheelRadiusMeters:0.###} m");
        Console.WriteLine($"  Front ride height data: {parameters.FrontSuspensionGeometry.RideHeightMeters:0.###} m, rear {parameters.RearSuspensionGeometry.RideHeightMeters:0.###} m");
        Console.WriteLine("  Assumption: spring rates are treated as effective wheel rates with 1.0 motion ratio for this diagnostic pass.");

        ProbeCase("static flat ground", new PlaneSampler(Vector3.Up, 0f, "Flat_Driveable"), parameters, engine, Vector3.Zero, 0f, 0);
        ProbeCase("static 10deg incline", new PlaneSampler(NormalForSlope(MathHelper.ToRadians(10f)), 0f, "Incline10_Driveable"), parameters, engine, Vector3.Zero, 0f, 0, pitchRadians: -MathHelper.ToRadians(10f));
        ProbeCase("static 10deg bank", new PlaneSampler(new Vector3(MathF.Sin(MathHelper.ToRadians(10f)), MathF.Cos(MathHelper.ToRadians(10f)), 0f), 0f, "Bank10_Driveable"), parameters, engine, Vector3.Zero, 0f, 0, rollRadians: -MathHelper.ToRadians(10f));
        ProbeCase("one wheel 50mm bump", new OneCornerHeightSampler(0.05f, "Bump50_Driveable"), parameters, engine, Vector3.Zero, 0f, 0);
        ProbeCase("one wheel 100mm bump", new OneCornerHeightSampler(0.10f, "Bump100_Driveable"), parameters, engine, Vector3.Zero, 0f, 0);
        ProbeCase("one wheel maximum droop", new OneCornerHeightSampler(-parameters.FrontSuspensionGeometry.MaxDroopMeters, "DroopLimit_Driveable"), parameters, engine, Vector3.Zero, 0f, 0);
        ProbeCase("road outside suspension reach", new PlaneSampler(Vector3.Up, 0f, "TooLow_Driveable"), parameters, engine, new Vector3(0f, 2f, 0f), 0f, 0);
        ProbeCase("slow crest traversal", new CrestDipSampler(0.20f, crest: true), parameters, engine, new Vector3(0f, 0f, -2.4f), 1.2f, 90);
        ProbeCase("slow dip traversal", new CrestDipSampler(0.20f, crest: false), parameters, engine, new Vector3(0f, 0f, -2.4f), 1.2f, 90);
    }

    private static void ProbeCase(
        string label,
        ITrackSurfaceSampler sampler,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        Vector3 start,
        float speedMetersPerSecond,
        int steps,
        float pitchRadians = 0f,
        float rollRadians = 0f)
    {
        ClassicFourWheelVehicleSimulator simulator = new(sampler, start, 0f, parameters, engine);
        simulator.State.Position = start;
        simulator.State.BodyPitchRadians = pitchRadians;
        simulator.State.BodyRollRadians = rollRadians;
        simulator.State.GroundPitchRadians = pitchRadians;
        simulator.State.GroundRollRadians = rollRadians;
        simulator.State.WheelContactCenterHeightMeters = start.Y;
        simulator.State.Velocity = new Vector2(0f, speedMetersPerSecond);
        for (int i = 0; i < steps; i++)
        {
            simulator.State.Position += new Vector3(0f, 0f, speedMetersPerSecond * Dt);
        }

        simulator.UpdateShadowSuspensionDiagnosticsForProbe(0f);

        VehicleState state = simulator.State;
        ShadowSuspensionCornerState fl = state.FrontLeftShadowSuspension;
        ShadowSuspensionCornerState fr = state.FrontRightShadowSuspension;
        ShadowSuspensionCornerState rl = state.RearLeftShadowSuspension;
        ShadowSuspensionCornerState rr = state.RearRightShadowSuspension;
        float weight = parameters.MassKg * Gravity;
        float verticalSupport = VerticalSupport(fl) + VerticalSupport(fr) + VerticalSupport(rl) + VerticalSupport(rr);
        float frontSupport = VerticalSupport(fl) + VerticalSupport(fr);
        float rearSupport = VerticalSupport(rl) + VerticalSupport(rr);
        Console.WriteLine(label + ":");
        Console.WriteLine($"  vertical support: {verticalSupport:0.#} N / weight {weight:0.#} N, ratio {verticalSupport / MathF.Max(1f, weight):0.###}");
        Console.WriteLine($"  front/rear vertical split: {frontSupport:0.#} N / {rearSupport:0.#} N, configured front bias {parameters.FrontWeightDistribution:0.###}");
        PrintCorner("  FL", fl, ExpectedStaticLoad(parameters, front: true));
        PrintCorner("  FR", fr, ExpectedStaticLoad(parameters, front: true));
        PrintCorner("  RL", rl, ExpectedStaticLoad(parameters, front: false));
        PrintCorner("  RR", rr, ExpectedStaticLoad(parameters, front: false));
    }

    private static float VerticalSupport(ShadowSuspensionCornerState state)
    {
        return state.HasContact ? state.ContactNormal.Y * state.SupportForceN : 0f;
    }

    private static float ExpectedStaticLoad(VehicleSimulationParameters parameters, bool front)
    {
        float bias = MathHelper.Clamp(parameters.FrontWeightDistribution, 0.05f, 0.95f);
        return parameters.MassKg * Gravity * (front ? bias : 1f - bias) * 0.5f;
    }

    private static void PrintCorner(string label, ShadowSuspensionCornerState state, float expectedStaticLoad)
    {
        if (!state.HasContact)
        {
            Console.WriteLine($"{label}: MISS {state.MissReason}, hardpoint {Format(state.HardpointWorld)}, axis {Format(state.SuspensionAxisWorld)}, tunedLoad {state.TunedNormalLoadN:0.#} N");
            return;
        }

        Console.WriteLine(
            $"{label}: src {state.SourceName} tri {state.TriangleIndex} cand {state.CandidateTriangleCount}, " +
            $"hardpoint {Format(state.HardpointWorld)}, axis {Format(state.SuspensionAxisWorld)}, normal {Format(state.ContactNormal)}");
        Console.WriteLine(
            $"      wheelCentre {Format(state.SolvedWheelCenter)}, contact {Format(state.CalculatedTyreContactPoint)}, " +
            $"len {state.SuspensionLengthMeters:0.###} m, staticLen {state.StaticReferenceLengthMeters:0.###} m, travel {state.CompressionMeters:0.###} m, vel {state.CompressionVelocityMetersPerSecond:0.###} m/s");
        Console.WriteLine(
            $"      expectedStatic {expectedStaticLoad:0.#} N, spring {state.SpringForceN:0.#} N, damper {state.DamperForceN:0.#} N, " +
            $"support {state.SupportForceN:0.#} N, tunedLoad {state.TunedNormalLoadN:0.#} N, preload {state.StaticPreloadForceN:0.#} N, freeLen {state.EquivalentFreeLengthMeters:0.###} m");
        Console.WriteLine(
            $"      bumpRemain {state.BumpTravelRemainingMeters:0.###} m, droopRemain {state.DroopTravelRemainingMeters:0.###} m, " +
            $"axisErr {state.WheelCenterAxisErrorMeters:0.######} m, planeRadiusErr {state.TyrePlaneDistanceErrorMeters:0.######} m, " +
            $"axisNormalAngle {state.AxisNormalAngleDegrees:0.##} deg, bumpLimit {state.BumpLimitHit}, droopLimit {state.DroopLimitHit}");
    }

    private static Vector3 NormalForSlope(float slopeRadians)
    {
        return Vector3.Normalize(new Vector3(0f, MathF.Cos(slopeRadians), -MathF.Sin(slopeRadians)));
    }

    private static string Format(Vector3 value)
    {
        return $"({value.X:0.###},{value.Y:0.###},{value.Z:0.###})";
    }

    private sealed class PlaneSampler : ITrackSurfaceSampler
    {
        private readonly Vector3 _normal;
        private readonly float _offsetY;
        private readonly string _sourceName;

        public PlaneSampler(Vector3 normal, float offsetY, string sourceName)
        {
            _normal = Vector3.Normalize(normal);
            _offsetY = offsetY;
            _sourceName = sourceName;
        }

        public bool HasAuthoredSurfaceContact => true;

        public SurfaceSample Sample(Vector3 position) => new("ROAD", 1f);

        public float GetElevation(Vector2 position)
        {
            return _offsetY - (_normal.X * position.X + _normal.Z * position.Y) / MathF.Max(0.001f, _normal.Y);
        }

        public bool TryGetSurfaceContact(Vector3 queryPosition, float downwardRangeMeters, out TrackSurfaceContact contact)
        {
            float y = GetElevation(new Vector2(queryPosition.X, queryPosition.Z));
            if (y > queryPosition.Y + 0.02f || y < queryPosition.Y - downwardRangeMeters)
            {
                contact = default;
                return false;
            }

            contact = new TrackSurfaceContact(new Vector3(queryPosition.X, y, queryPosition.Z), _normal, _sourceName, 0, 1);
            return true;
        }

        public bool TryGetSurfaceContactRay(Vector3 origin, Vector3 direction, float maxDistanceMeters, out TrackSurfaceContact contact)
        {
            Vector3 rayDirection = Vector3.Normalize(direction);
            Vector3 planePoint = new(0f, _offsetY, 0f);
            float denominator = Vector3.Dot(rayDirection, _normal);
            if (MathF.Abs(denominator) < 0.0001f)
            {
                contact = default;
                return false;
            }

            float distance = Vector3.Dot(planePoint - origin, _normal) / denominator;
            if (distance < -0.001f || distance > maxDistanceMeters + 0.001f)
            {
                contact = default;
                return false;
            }

            contact = new TrackSurfaceContact(origin + rayDirection * distance, _normal, _sourceName, 0, 1);
            return true;
        }
    }

    private sealed class OneCornerHeightSampler : ITrackSurfaceSampler
    {
        private readonly float _frontLeftHeight;
        private readonly string _sourceName;

        public OneCornerHeightSampler(float frontLeftHeight, string sourceName)
        {
            _frontLeftHeight = frontLeftHeight;
            _sourceName = sourceName;
        }

        public bool HasAuthoredSurfaceContact => true;

        public SurfaceSample Sample(Vector3 position) => new("ROAD", 1f);

        public bool TryGetSurfaceContact(Vector3 queryPosition, float downwardRangeMeters, out TrackSurfaceContact contact)
        {
            return TryGetSurfaceContactRay(queryPosition, -Vector3.Up, downwardRangeMeters, out contact);
        }

        public bool TryGetSurfaceContactRay(Vector3 origin, Vector3 direction, float maxDistanceMeters, out TrackSurfaceContact contact)
        {
            float y = origin.X < 0f && origin.Z > 0f ? _frontLeftHeight : 0f;
            Vector3 normal = Vector3.Up;
            float denominator = Vector3.Dot(Vector3.Normalize(direction), normal);
            if (MathF.Abs(denominator) < 0.0001f)
            {
                contact = default;
                return false;
            }

            float distance = (y - origin.Y) / denominator;
            if (distance < -0.001f || distance > maxDistanceMeters + 0.001f)
            {
                contact = default;
                return false;
            }

            contact = new TrackSurfaceContact(origin + Vector3.Normalize(direction) * distance, normal, _sourceName, 0, 1);
            return true;
        }
    }

    private sealed class CrestDipSampler : ITrackSurfaceSampler
    {
        private readonly float _amplitude;
        private readonly bool _crest;

        public CrestDipSampler(float amplitude, bool crest)
        {
            _amplitude = amplitude;
            _crest = crest;
        }

        public bool HasAuthoredSurfaceContact => true;

        public SurfaceSample Sample(Vector3 position) => new("ROAD", 1f);

        public bool TryGetSurfaceContact(Vector3 queryPosition, float downwardRangeMeters, out TrackSurfaceContact contact)
        {
            return TryGetSurfaceContactRay(queryPosition, -Vector3.Up, downwardRangeMeters, out contact);
        }

        public bool TryGetSurfaceContactRay(Vector3 origin, Vector3 direction, float maxDistanceMeters, out TrackSurfaceContact contact)
        {
            Vector3 rayDirection = Vector3.Normalize(direction);
            if (MathF.Abs(rayDirection.Y) < 0.2f)
            {
                contact = default;
                return false;
            }

            float z = origin.Z;
            float shape = MathF.Exp(-(z * z) / 5.0f);
            float y = (_crest ? 1f : -1f) * _amplitude * shape;
            float distance = (y - origin.Y) / rayDirection.Y;
            if (distance < -0.001f || distance > maxDistanceMeters + 0.001f)
            {
                contact = default;
                return false;
            }

            float dydz = (_crest ? 1f : -1f) * _amplitude * shape * (-2f * z / 5.0f);
            Vector3 normal = Vector3.Normalize(new Vector3(0f, 1f, -dydz));
            contact = new TrackSurfaceContact(origin + rayDirection * distance, normal, _crest ? "Crest_Driveable" : "Dip_Driveable", 0, 1);
            return true;
        }
    }
}
