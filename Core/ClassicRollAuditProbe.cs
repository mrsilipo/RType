using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicRollAuditProbe
{
    private const float Gravity = 9.81f;
    private const float Dt = 1f / 120f;
    private const string EkReferenceVehiclePath = "Data/PurchaseCars/2000_Ek9_Stock.json";

    public static void Run()
    {
        VehicleSimulationParameters parameters = VehicleBuildDefinitionLoader.LoadSimulationParameters(EkReferenceVehiclePath);
        SimulationEngineParameters engine = new();
        Console.WriteLine("Classic physical roll audit");
        Console.WriteLine($"  Vehicle: {parameters.DisplayName} ({EkReferenceVehiclePath})");
        Console.WriteLine($"  Position semantic: simulator ground/vehicle reference, not the raised body render pivot; renderer rotates body about BodyPivotHeightMeters={parameters.CenterOfGravityHeightMeters:0.###}m.");
        Console.WriteLine($"  Effective CG for physical moments: State.Position + chassisUp * CenterOfGravityHeightMeters ({parameters.CenterOfGravityHeightMeters:0.###}m).");
        float bodyHeight = MathHelper.Clamp(parameters.CenterOfGravityHeightMeters * 2f, 0.6f, 1.8f);
        float bodyWidth = MathF.Max(parameters.BodyWidthMeters, MathF.Max(parameters.FrontTrackMeters, parameters.RearTrackMeters));
        float rollInertia = MathF.Max(1f, parameters.MassKg) * (bodyWidth * bodyWidth + bodyHeight * bodyHeight) / 12f;
        Console.WriteLine($"  Roll inertia formula: mass * (width^2 + bodyHeight^2) / 12");
        Console.WriteLine($"  mass {parameters.MassKg:0.#}kg, width {bodyWidth:0.###}m, bodyHeight {bodyHeight:0.###}m, I_roll {rollInertia:0.#} kgm^2");
        Console.WriteLine($"  Physical pitch/roll clamp: +/-0.45 rad = {MathHelper.ToDegrees(0.45f):0.###} deg.");
        Console.WriteLine("  Convention: heading 0 forward is +Z, right is +X, up is +Y; physical pitch axis is chassis +X/right, roll axis is chassis +Z/forward.");
        PrintPitchReachAnalysis(parameters);

        RunTrace("steady left corner", parameters, engine, new PlaneSampler(Vector3.Up), new VehicleInput(0.22f, 0f, 0.35f), 22f, 0.35f, reportEverySeconds: 0.05f);
        RunTrace("steady right corner", parameters, engine, new PlaneSampler(Vector3.Up), new VehicleInput(0.22f, 0f, -0.35f), 22f, 0.35f, reportEverySeconds: 0.05f);
        RunTrace("one-wheel 50mm bump", parameters, engine, new OneCornerHeightSampler(0.05f), new VehicleInput(0f, 0f, 0f), 0f, 0.45f, reportEverySeconds: 0.05f);
        RunTrace("one-wheel 100mm bump", parameters, engine, new OneCornerHeightSampler(0.10f), new VehicleInput(0f, 0f, 0f), 0f, 0.45f, reportEverySeconds: 0.05f);
        RunTrace("one-wheel 50mm drop", parameters, engine, new OneCornerHeightSampler(-0.05f), new VehicleInput(0f, 0f, 0f), 0f, 0.45f, reportEverySeconds: 0.05f);
        RunFreeRollDecay("+5 deg free-roll decay ARB off", parameters, engine, MathHelper.ToRadians(5f), disableArb: true);
        RunFreeRollDecay("+5 deg free-roll decay ARB on", parameters, engine, MathHelper.ToRadians(5f), disableArb: false);
        RunFreeRollDecay("-5 deg free-roll decay ARB off", parameters, engine, MathHelper.ToRadians(-5f), disableArb: true);
        RunFreeRollDecay("-5 deg free-roll decay ARB on", parameters, engine, MathHelper.ToRadians(-5f), disableArb: false);
        RunFreePitchDecay("+5 deg free-pitch decay", parameters, engine, MathHelper.ToRadians(5f));
        RunFreePitchDecay("-5 deg free-pitch decay", parameters, engine, MathHelper.ToRadians(-5f));
        RunFreePitchDecay("+3 deg free-pitch decay", parameters, engine, MathHelper.ToRadians(3f));
        RunFreePitchDecay("-3 deg free-pitch decay", parameters, engine, MathHelper.ToRadians(-3f));
    }

    private static void RunFreeRollDecay(string label, VehicleSimulationParameters parameters, SimulationEngineParameters engine, float rollRadians, bool disableArb)
    {
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engine, new PlaneSampler(Vector3.Up), 0f);
        simulator.DisablePhysicalArbForProbe = disableArb;
        simulator.SetPhysicalChassisOrientationForProbe(0f, rollRadians);
        simulator.SetPhysicalChassisVelocityForProbe(Vector3.Zero);
        RunTrace(label, simulator, parameters, new VehicleInput(0f, 0f, 0f), 0.75f, 0.05f);
    }

    private static void RunFreePitchDecay(string label, VehicleSimulationParameters parameters, SimulationEngineParameters engine, float pitchRadians)
    {
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engine, new PlaneSampler(Vector3.Up), 0f);
        simulator.SetPhysicalChassisOrientationForProbe(pitchRadians, 0f);
        bool solved = simulator.SolvePhysicalChassisHeightForCurrentOrientationForProbe(out float residualMeters, out int contactCount);
        Console.WriteLine($"{label} fixed-pitch height solve: contacts {contactCount}/4, residual {residualMeters:0.####} m, requested pitch {MathHelper.ToDegrees(pitchRadians):0.###} deg.");
        simulator.SetPhysicalChassisVelocityForProbe(Vector3.Zero);
        RunTrace(label, simulator, parameters, new VehicleInput(0f, 0f, 0f), 0.75f, 0.05f);
    }

    private static void PrintPitchReachAnalysis(VehicleSimulationParameters parameters)
    {
        VehicleAxleGeometry geometry = VehicleAxleGeometry.FromParameters(parameters);
        float wheelbase = MathF.Max(0.1f, geometry.WheelbaseMeters);
        float requestedAxleDelta5 = MathF.Sin(MathHelper.ToRadians(5f)) * wheelbase;
        float frontBumpRearDroop = parameters.FrontSuspensionGeometry.MaxCompressionMeters + parameters.RearSuspensionGeometry.MaxDroopMeters;
        float rearBumpFrontDroop = parameters.RearSuspensionGeometry.MaxCompressionMeters + parameters.FrontSuspensionGeometry.MaxDroopMeters;
        float symmetricLimit = MathF.Min(frontBumpRearDroop, rearBumpFrontDroop);
        float maxSymmetricPitch = MathHelper.ToDegrees(MathF.Asin(MathHelper.Clamp(symmetricLimit / wheelbase, -1f, 1f)));
        float requestedAxleDelta3 = MathF.Sin(MathHelper.ToRadians(3f)) * wheelbase;
        Console.WriteLine(
            $"  Free-pitch reach: wheelbase {wheelbase:0.###}m, 5deg axle delta {requestedAxleDelta5:0.###}m, " +
            $"3deg axle delta {requestedAxleDelta3:0.###}m.");
        Console.WriteLine(
            $"  Travel envelope: front bump {parameters.FrontSuspensionGeometry.MaxCompressionMeters:0.###}m + rear droop {parameters.RearSuspensionGeometry.MaxDroopMeters:0.###}m = {frontBumpRearDroop:0.###}m; " +
            $"rear bump {parameters.RearSuspensionGeometry.MaxCompressionMeters:0.###}m + front droop {parameters.FrontSuspensionGeometry.MaxDroopMeters:0.###}m = {rearBumpFrontDroop:0.###}m.");
        Console.WriteLine($"  Max symmetric free-pitch before one axle exceeds bump/droop reach is about {maxSymmetricPitch:0.###} deg, so 3 deg is the valid decay gate.");
    }

    private static void RunTrace(
        string label,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        ITrackSurfaceSampler sampler,
        VehicleInput input,
        float startSpeedMetersPerSecond,
        float seconds,
        float reportEverySeconds)
    {
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engine, sampler, startSpeedMetersPerSecond);
        RunTrace(label, simulator, parameters, input, seconds, reportEverySeconds);
    }

    private static ClassicFourWheelVehicleSimulator CreateSimulator(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        ITrackSurfaceSampler sampler,
        float startSpeedMetersPerSecond)
    {
        ClassicFourWheelVehicleSimulator simulator = new(sampler, Vector3.Zero, 0f, parameters, engine);
        simulator.InitializePhysicalChassisForProbe();
        simulator.SetPhysicalChassisVelocityForProbe(new Vector3(0f, 0f, startSpeedMetersPerSecond));
        return simulator;
    }

    private static void RunTrace(
        string label,
        ClassicFourWheelVehicleSimulator simulator,
        VehicleSimulationParameters parameters,
        VehicleInput input,
        float seconds,
        float reportEverySeconds)
    {
        Console.WriteLine(label + ":");
        int ticks = Math.Max(1, (int)MathF.Round(seconds / Dt));
        int reportEveryTicks = Math.Max(1, (int)MathF.Round(reportEverySeconds / Dt));
        for (int tick = 0; tick <= ticks; tick++)
        {
            if (tick % reportEveryTicks == 0 || tick == ticks)
            {
                PrintSample(tick * Dt, simulator.State, parameters);
            }

            if (tick < ticks)
            {
                simulator.Update(input, Dt);
            }
        }
    }

    private static void PrintSample(float timeSeconds, VehicleState state, VehicleSimulationParameters parameters)
    {
        Vector3 cg = CalculateCg(state, parameters);
        float lateralAcceleration = state.LateralAcceleration;
        float totalLateralTyre = state.FrontLeftLateralForceN + state.FrontRightLateralForceN + state.RearLeftLateralForceN + state.RearRightLateralForceN;
        float analyticalRollMoment = totalLateralTyre * MathHelper.Clamp(parameters.CenterOfGravityHeightMeters, 0.05f, 1.5f);
        float rollInertia = CalculateRollInertia(parameters);
        float predictedAlpha = state.TotalTyreRollMomentNm / MathF.Max(1f, rollInertia);
        float totalRollTorque = state.PhysicalSuspensionRollAccelerationRadiansPerSecondSquared * rollInertia;
        float suspensionRollTorque = state.PhysicalSuspensionSpringRollTorqueNm + state.PhysicalSuspensionDamperRollTorqueNm +
            state.PhysicalSuspensionBumpStopRollTorqueNm + state.PhysicalSuspensionArbRollTorqueNm;
        float pitchInertia = CalculatePitchInertia(parameters);
        float totalPitchTorque = state.PhysicalSuspensionPitchAccelerationRadiansPerSecondSquared * pitchInertia;
        float suspensionPitchTorque = state.PhysicalSuspensionSpringPitchTorqueNm + state.PhysicalSuspensionDamperPitchTorqueNm +
            state.PhysicalSuspensionBumpStopPitchTorqueNm + state.PhysicalSuspensionArbPitchTorqueNm;
        float physicalTotal = Projected(state.FrontLeftShadowSuspension) + Projected(state.FrontRightShadowSuspension) +
            Projected(state.RearLeftShadowSuspension) + Projected(state.RearRightShadowSuspension);
        Console.WriteLine(
            $"  t {timeSeconds:0.###}s speed {state.SpeedMetersPerSecond:0.###} latAcc {lateralAcceleration:0.###} " +
            $"F_lat {totalLateralTyre:0.#} analyticalRoll {analyticalRollMoment:0.#} tyreRoll {state.TotalTyreRollMomentNm:0.#} " +
            $"suspRoll {suspensionRollTorque:0.#} totalRoll {totalRollTorque:0.#} rollAlpha tyre/actual {predictedAlpha:0.###}/{state.PhysicalSuspensionRollAccelerationRadiansPerSecondSquared:0.###} " +
            $"roll {MathHelper.ToDegrees(state.BodyRollRadians):0.###}deg rate {state.BodyRollRateRadiansPerSecond:0.###} " +
            $"pitch {MathHelper.ToDegrees(state.BodyPitchRadians):0.###}deg pitchRate {state.BodyPitchRateRadiansPerSecond:0.###} " +
            $"physLoad {physicalTotal:0.#}");
        Console.WriteLine(
            $"    torqueSplit pitch spring/damper/bump/arb/tyre/total {state.PhysicalSuspensionSpringPitchTorqueNm:0.#}/" +
            $"{state.PhysicalSuspensionDamperPitchTorqueNm:0.#}/{state.PhysicalSuspensionBumpStopPitchTorqueNm:0.#}/" +
            $"{state.PhysicalSuspensionArbPitchTorqueNm:0.#}/" +
            $"{state.TotalTyrePitchMomentNm:0.#}/{totalPitchTorque:0.#} roll spring/damper/bump/arb/tyre/total " +
            $"{state.PhysicalSuspensionSpringRollTorqueNm:0.#}/{state.PhysicalSuspensionDamperRollTorqueNm:0.#}/" +
            $"{state.PhysicalSuspensionBumpStopRollTorqueNm:0.#}/{state.PhysicalSuspensionArbRollTorqueNm:0.#}/" +
            $"{state.TotalTyreRollMomentNm:0.#}/{totalRollTorque:0.#}");
        PrintWheel("FL", state.FrontLeftShadowSuspension, state.FrontLeftLongitudinalForceN, state.FrontLeftLateralForceN, state.FrontLeftTyrePitchMomentNm, state.FrontLeftTyreRollMomentNm, cg);
        PrintWheel("FR", state.FrontRightShadowSuspension, state.FrontRightLongitudinalForceN, state.FrontRightLateralForceN, state.FrontRightTyrePitchMomentNm, state.FrontRightTyreRollMomentNm, cg);
        PrintWheel("RL", state.RearLeftShadowSuspension, state.RearLeftLongitudinalForceN, state.RearLeftLateralForceN, state.RearLeftTyrePitchMomentNm, state.RearLeftTyreRollMomentNm, cg);
        PrintWheel("RR", state.RearRightShadowSuspension, state.RearRightLongitudinalForceN, state.RearRightLateralForceN, state.RearRightTyrePitchMomentNm, state.RearRightTyreRollMomentNm, cg);
    }

    private static void PrintWheel(string label, ShadowSuspensionCornerState corner, float longForce, float latForce, float pitchMoment, float rollMoment, Vector3 cg)
    {
        Vector3 r = corner.HasContact ? corner.CalculatedTyreContactPoint - cg : Vector3.Zero;
        float projected = Projected(corner);
        Console.WriteLine(
            $"    {label} contact {(corner.HasContact ? 1 : 0)} r {Format(r)} leverY {-r.Y:0.###}m " +
            $"travel {corner.CompressionMeters:0.###} vel {corner.CompressionVelocityMetersPerSecond:0.###} " +
            $"spring {corner.SpringForceN:0.#} damper {corner.DamperForceN:0.#} bump {corner.BumpStopForceN:0.#} " +
            $"arb {corner.ArbContributionN:0.#} unclamped {corner.UnclampedSupportForceN:0.#} clampErr {corner.SupportClampErrorN:0.#} " +
            $"projLoad {projected:0.#} long {longForce:0.#} lat {latForce:0.#} " +
            $"pitchM {pitchMoment:0.#} rollM {rollMoment:0.#} limit B/D {(corner.BumpLimitHit ? 1 : 0)}/{(corner.DroopLimitHit ? 1 : 0)}");
        Console.WriteLine(
            $"      geom hardpoint {Format(corner.HardpointWorld)} axis {Format(corner.SuspensionAxisWorld)} " +
            $"contact {Format(corner.ContactPoint)} normal {Format(corner.ContactNormal)} wheelCenter {Format(corner.SolvedWheelCenter)} " +
            $"length {corner.SuspensionLengthMeters:0.###}/{corner.StaticReferenceLengthMeters:0.###}m " +
            $"bumpRemain {corner.BumpTravelRemainingMeters:0.###}m droopRemain {corner.DroopTravelRemainingMeters:0.###}m " +
            $"axisErr {corner.WheelCenterAxisErrorMeters:0.####}m planeErr {corner.TyrePlaneDistanceErrorMeters:0.####}m " +
            $"miss '{corner.MissReason}'");
    }

    private static Vector3 CalculateCg(VehicleState state, VehicleSimulationParameters parameters)
    {
        Matrix rotation = Matrix.CreateFromYawPitchRoll(state.HeadingRadians, state.BodyPitchRadians, state.BodyRollRadians);
        Vector3 up = Vector3.Normalize(Vector3.TransformNormal(Vector3.Up, rotation));
        return state.Position + up * MathHelper.Clamp(parameters.CenterOfGravityHeightMeters, 0.05f, 1.5f);
    }

    private static float CalculateRollInertia(VehicleSimulationParameters parameters)
    {
        float bodyHeight = MathHelper.Clamp(parameters.CenterOfGravityHeightMeters * 2f, 0.6f, 1.8f);
        float bodyWidth = MathF.Max(parameters.BodyWidthMeters, MathF.Max(parameters.FrontTrackMeters, parameters.RearTrackMeters));
        return MathF.Max(1f, parameters.MassKg) * (bodyWidth * bodyWidth + bodyHeight * bodyHeight) / 12f;
    }

    private static float CalculatePitchInertia(VehicleSimulationParameters parameters)
    {
        float bodyHeight = MathHelper.Clamp(parameters.CenterOfGravityHeightMeters * 2f, 0.6f, 1.8f);
        float bodyLength = MathF.Max(parameters.BodyLengthMeters, parameters.WheelbaseMeters);
        return MathF.Max(1f, parameters.MassKg) * (bodyLength * bodyLength + bodyHeight * bodyHeight) / 12f;
    }

    private static float Projected(ShadowSuspensionCornerState corner)
    {
        if (!corner.HasContact || corner.SuspensionAxisWorld.LengthSquared() <= 0.000001f || corner.ContactNormal.LengthSquared() <= 0.000001f)
        {
            return 0f;
        }

        Vector3 support = -Vector3.Normalize(corner.SuspensionAxisWorld) * corner.SupportForceN;
        return MathF.Max(0f, Vector3.Dot(support, Vector3.Normalize(corner.ContactNormal)));
    }

    private static string Format(Vector3 value)
    {
        return $"({value.X:0.###},{value.Y:0.###},{value.Z:0.###})";
    }

    private sealed class PlaneSampler : ITrackSurfaceSampler
    {
        private readonly Vector3 _normal;

        public PlaneSampler(Vector3 normal)
        {
            _normal = Vector3.Normalize(normal);
        }

        public bool HasAuthoredSurfaceContact => true;

        public SurfaceSample Sample(Vector3 position) => new("ROAD", 1f);

        public float GetElevation(Vector2 position) => -(_normal.X * position.X + _normal.Z * position.Y) / MathF.Max(0.001f, _normal.Y);

        public bool TryGetSurfaceContact(Vector3 queryPosition, float downwardRangeMeters, out TrackSurfaceContact contact)
        {
            return TryGetSurfaceContactRay(queryPosition, -Vector3.Up, downwardRangeMeters, out contact);
        }

        public bool TryGetSurfaceContactRay(Vector3 origin, Vector3 direction, float maxDistanceMeters, out TrackSurfaceContact contact)
        {
            Vector3 rayDirection = Vector3.Normalize(direction);
            float denominator = Vector3.Dot(rayDirection, _normal);
            if (MathF.Abs(denominator) < 0.0001f)
            {
                contact = default;
                return false;
            }

            float distance = Vector3.Dot(-origin, _normal) / denominator;
            if (distance < -0.001f || distance > maxDistanceMeters + 0.001f)
            {
                contact = default;
                return false;
            }

            contact = new TrackSurfaceContact(origin + rayDirection * distance, _normal, "Flat_Driveable", 0, 1);
            return true;
        }
    }

    private sealed class OneCornerHeightSampler : ITrackSurfaceSampler
    {
        private readonly float _frontLeftHeight;

        public OneCornerHeightSampler(float frontLeftHeight)
        {
            _frontLeftHeight = frontLeftHeight;
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
            float y = origin.X < 0f && origin.Z > 0f ? _frontLeftHeight : 0f;
            if (MathF.Abs(rayDirection.Y) < 0.0001f)
            {
                contact = default;
                return false;
            }

            float distance = (y - origin.Y) / rayDirection.Y;
            if (distance < -0.001f || distance > maxDistanceMeters + 0.001f)
            {
                contact = default;
                return false;
            }

            contact = new TrackSurfaceContact(origin + rayDirection * distance, Vector3.Up, "Bump_Driveable", 0, 1);
            return true;
        }

        public float GetElevation(Vector2 position) => position.X < 0f && position.Y > 0f ? _frontLeftHeight : 0f;
    }
}
