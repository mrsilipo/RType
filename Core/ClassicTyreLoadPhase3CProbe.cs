using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicTyreLoadPhase3CProbe
{
    private const float Gravity = 9.81f;
    private const float Dt = 1f / 120f;
    private const string EkReferenceVehiclePath = "Data/PurchaseCars/2000_Ek9_Stock.json";
    private const float PhysicalLoadFilterTimeSeconds = 0.05f;

    public static void Run()
    {
        VehicleSimulationParameters parameters = VehicleBuildDefinitionLoader.LoadSimulationParameters(EkReferenceVehiclePath);
        SimulationEngineParameters engine = new();
        Console.WriteLine("Classic tyre-load Phase 3C-C authority comparison probe");
        Console.WriteLine($"  Vehicle: {parameters.DisplayName} ({EkReferenceVehiclePath})");
        Console.WriteLine($"  Fixed timestep: {engine.Timing.FixedDeltaSeconds:0.######} s ({engine.Timing.FixedTickRateHz:0.#} Hz)");
        Console.WriteLine("  Default runtime tyre grip authority remains Tuned.");
        Console.WriteLine("  Physical load diagnostics assume effective wheel rate, motion ratio = 1.0.");
        Console.WriteLine("  Physical loads are not normalized back to mass*g.");
        Console.WriteLine($"  PhysicalFiltered tau: {PhysicalLoadFilterTimeSeconds:0.###} s.");
        Console.WriteLine($"  mass*g: {parameters.MassKg * Gravity:0.#} N");
        Console.WriteLine($"  front ARB: {parameters.FrontAntiRollBarRateNmPerRad:0.#} Nm/rad, rear ARB: {parameters.RearAntiRollBarRateNmPerRad:0.#} Nm/rad");

        RunSuite("Tuned", TyreLoadAuthorityMode.Tuned, PhysicalLoadFilterRecontactMode.SeedFromRaw, parameters, engine);
        RunSuite("PhysicalRaw", TyreLoadAuthorityMode.PhysicalRaw, PhysicalLoadFilterRecontactMode.SeedFromRaw, parameters, engine);
        RunSuite("PhysicalFiltered/recontact-zero", TyreLoadAuthorityMode.PhysicalFiltered, PhysicalLoadFilterRecontactMode.ResumeFromZero, parameters, engine);
        RunSuite("PhysicalFiltered/recontact-raw", TyreLoadAuthorityMode.PhysicalFiltered, PhysicalLoadFilterRecontactMode.SeedFromRaw, parameters, engine);
        RunSuite("Hybrid diagnostic", TyreLoadAuthorityMode.Hybrid, PhysicalLoadFilterRecontactMode.SeedFromRaw, parameters, engine);
    }

    private static void RunSuite(
        string suiteLabel,
        TyreLoadAuthorityMode mode,
        PhysicalLoadFilterRecontactMode recontactMode,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine)
    {
        Console.WriteLine();
        Console.WriteLine($"== {suiteLabel} ==");
        RunCase("static flat", new PlaneSampler(Vector3.Up, "Flat_Driveable"), parameters, engine, mode, recontactMode, new VehicleInput(0f, 0f, 0f), 0f, 2.0f);
        RunCase("straight acceleration", new PlaneSampler(Vector3.Up, "Flat_Driveable"), parameters, engine, mode, recontactMode, new VehicleInput(1f, 0f, 0f), 0f, 2.0f);
        RunCase("hard braking", new PlaneSampler(Vector3.Up, "Flat_Driveable"), parameters, engine, mode, recontactMode, new VehicleInput(0f, 1f, 0f), 30f, 1.0f);
        RunCase("steady left cornering", new PlaneSampler(Vector3.Up, "Flat_Driveable"), parameters, engine, mode, recontactMode, new VehicleInput(0.22f, 0f, 0.35f), 22f, 2.5f);
        RunCase("steady right cornering", new PlaneSampler(Vector3.Up, "Flat_Driveable"), parameters, engine, mode, recontactMode, new VehicleInput(0.22f, 0f, -0.35f), 22f, 2.5f);
        RunCase("braking + cornering", new PlaneSampler(Vector3.Up, "Flat_Driveable"), parameters, engine, mode, recontactMode, new VehicleInput(0f, 0.65f, 0.4f), 26f, 1.5f);
        RunCase("throttle + cornering", new PlaneSampler(Vector3.Up, "Flat_Driveable"), parameters, engine, mode, recontactMode, new VehicleInput(0.7f, 0f, 0.35f), 18f, 2.0f);
        RunCase("10 bank", new PlaneSampler(new Vector3(-MathF.Sin(MathHelper.ToRadians(10f)), MathF.Cos(MathHelper.ToRadians(10f)), 0f), "Bank10_Driveable"), parameters, engine, mode, recontactMode, new VehicleInput(0f, 0f, 0f), 22f, 1.5f);
        RunCase("10 incline", new PlaneSampler(NormalForSlope(MathHelper.ToRadians(10f), uphill: true), "Incline10_Driveable"), parameters, engine, mode, recontactMode, new VehicleInput(0f, 0f, 0f), 14f, 1.5f);
        RunCase("10 decline", new PlaneSampler(NormalForSlope(MathHelper.ToRadians(10f), uphill: false), "Decline10_Driveable"), parameters, engine, mode, recontactMode, new VehicleInput(0f, 0f, 0f), 14f, 1.5f);
        RunCase("crest", new CrestDipSampler(heightMeters: 0.16f, widthMeters: 9.0f, "Crest_Driveable"), parameters, engine, mode, recontactMode, new VehicleInput(0f, 0f, 0f), 18f, 1.4f, startZ: -9f);
        RunCase("dip", new CrestDipSampler(heightMeters: -0.16f, widthMeters: 9.0f, "Dip_Driveable"), parameters, engine, mode, recontactMode, new VehicleInput(0f, 0f, 0f), 18f, 1.4f, startZ: -9f);
        RunCase("one-wheel 50mm bump", new OneCornerHeightSampler(0.05f, "Bump50_Driveable"), parameters, engine, mode, recontactMode, new VehicleInput(0f, 0f, 0f), 0f, 1.0f);
        RunCase("one-wheel 100mm bump", new OneCornerHeightSampler(0.10f, "Bump100_Driveable"), parameters, engine, mode, recontactMode, new VehicleInput(0f, 0f, 0f), 0f, 1.0f);
        RunCase("one-wheel drop", new OneCornerHeightSampler(-0.22f, "Drop_Driveable"), parameters, engine, mode, recontactMode, new VehicleInput(0f, 0f, 0f), 0f, 1.0f);
        RunCase("contact loss", new NoContactSampler(), parameters, engine, mode, recontactMode, new VehicleInput(0f, 0f, 0f), 0f, 0.35f, startY: 1.0f);
        RunCase("controlled landing", new PlaneSampler(Vector3.Up, "Landing_Driveable"), parameters, engine, mode, recontactMode, new VehicleInput(0f, 0f, 0f), 0f, 0.75f, postInitializeYOffset: 0.45f, startVerticalVelocity: -1.25f);
    }

    private static void RunCase(
        string label,
        ITrackSurfaceSampler sampler,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        TyreLoadAuthorityMode mode,
        PhysicalLoadFilterRecontactMode recontactMode,
        VehicleInput input,
        float startSpeedMetersPerSecond,
        float seconds,
        float startY = 0f,
        float startZ = 0f,
        float postInitializeYOffset = 0f,
        float startVerticalVelocity = 0f)
    {
        ClassicFourWheelVehicleSimulator simulator = new(sampler, new Vector3(0f, startY, startZ), 0f, parameters, engine);
        simulator.TyreLoadAuthorityMode = mode;
        simulator.PhysicalLoadFilterRecontactMode = recontactMode;
        simulator.PhysicalLoadFilterTimeConstantSeconds = PhysicalLoadFilterTimeSeconds;
        simulator.InitializePhysicalChassisForProbe();
        if (MathF.Abs(postInitializeYOffset) > 0.0001f)
        {
            simulator.SetPhysicalChassisPositionForProbe(simulator.State.Position + Vector3.Up * postInitializeYOffset);
        }

        simulator.SetPhysicalChassisVelocityForProbe(new Vector3(0f, startVerticalVelocity, startSpeedMetersPerSecond));
        LoadMetrics peak = default;
        LoadMetrics peakInput = default;
        LoadMetrics peakFiltered = default;
        LoadMetrics minimum = default;
        bool hasMinimum = false;
        float peakRate = 0f;
        float previousSum = float.NaN;
        int ticks = Math.Max(1, (int)MathF.Round(seconds / Dt));
        for (int tick = 0; tick < ticks; tick++)
        {
            simulator.Update(input, Dt);
            LoadMetrics sample = CalculateMetrics(simulator.State);
            peak = LoadMetrics.Max(peak, sample);
            peakInput = LoadMetrics.MaxInput(peakInput, sample);
            peakFiltered = LoadMetrics.MaxFiltered(peakFiltered, sample);
            minimum = hasMinimum ? LoadMetrics.Min(minimum, sample) : sample;
            hasMinimum = true;
            if (float.IsFinite(previousSum))
            {
                peakRate = MathF.Max(peakRate, MathF.Abs(sample.SumProjectedRoadNormalLoadN - previousSum) / Dt);
            }

            previousSum = sample.SumProjectedRoadNormalLoadN;
        }

        LoadMetrics final = CalculateMetrics(simulator.State);
        Console.WriteLine(label + ":");
        Console.WriteLine($"  authority {simulator.State.TyreLoadAuthorityMode} recontact {simulator.State.PhysicalLoadFilterRecontactMode} tau {simulator.State.PhysicalLoadFilterTimeConstantSeconds:0.###}s");
        Console.WriteLine($"  speed {simulator.State.SpeedMetersPerSecond:0.###} m/s, yawRate {simulator.State.YawRateRadiansPerSecond:0.###} rad/s, y {simulator.State.Position.Y:0.###} m, pitch {MathHelper.ToDegrees(simulator.State.BodyPitchRadians):0.###} deg, roll {MathHelper.ToDegrees(simulator.State.BodyRollRadians):0.###} deg");
        Console.WriteLine($"  totals input {final.SumAuthorityInputLoadN:0.#} N, tuned {final.SumTunedNormalLoadN:0.#} N, projected {final.SumProjectedRoadNormalLoadN:0.#} N, filtered {final.SumFilteredProjectedLoadN:0.#} N, supportMag {final.SumSupportMagnitudeN:0.#} N, mass*g {parameters.MassKg * Gravity:0.#} N");
        Console.WriteLine($"  splits projected front/rear {final.FrontProjectedLoadN:0.#}/{final.RearProjectedLoadN:0.#} N, left/right {final.LeftProjectedLoadN:0.#}/{final.RightProjectedLoadN:0.#} N");
        Console.WriteLine($"  deltas projected-tuned FL/FR/RL/RR {final.Fl.ProjectedRoadNormalLoadN - final.Fl.TunedNormalLoadN:0.#}/{final.Fr.ProjectedRoadNormalLoadN - final.Fr.TunedNormalLoadN:0.#}/{final.Rl.ProjectedRoadNormalLoadN - final.Rl.TunedNormalLoadN:0.#}/{final.Rr.ProjectedRoadNormalLoadN - final.Rr.TunedNormalLoadN:0.#} N");
        Console.WriteLine($"  min projected {minimum.SumProjectedRoadNormalLoadN:0.#} N, peak projected {peak.SumProjectedRoadNormalLoadN:0.#} N, peak filtered {peakFiltered.SumFilteredProjectedLoadN:0.#} N, peak input {peakInput.SumAuthorityInputLoadN:0.#} N, peak load-rate {peakRate:0.#} N/s");
        Console.WriteLine($"  tyre moments pitch {simulator.State.TotalTyrePitchMomentNm:0.#} Nm, roll {simulator.State.TotalTyreRollMomentNm:0.#} Nm, pitchRate {simulator.State.BodyPitchRateRadiansPerSecond:0.###} rad/s, rollRate {simulator.State.BodyRollRateRadiansPerSecond:0.###} rad/s");
        Console.WriteLine($"  ARB axle sums FL+FR {final.Fl.ArbContributionN + final.Fr.ArbContributionN:0.###} N, RL+RR {final.Rl.ArbContributionN + final.Rr.ArbContributionN:0.###} N");
        PrintCorner("FL", final.Fl);
        PrintCorner("FR", final.Fr);
        PrintCorner("RL", final.Rl);
        PrintCorner("RR", final.Rr);
    }

    private static LoadMetrics CalculateMetrics(VehicleState state)
    {
        WheelLoadDiagnostic fl = CreateWheel("FL", state.FrontLeftShadowSuspension, state.FrontLeftTunedTyreLoadN, state.FrontLeftPhysicalRawTyreLoadN, state.FrontLeftPhysicalClampedTyreLoadN, state.FrontLeftPhysicalLoadClampLossN, state.FrontLeftPhysicalFilteredTyreLoadN, state.FrontLeftHybridRawTyreLoadN, state.FrontLeftHybridUsableTyreLoadN, state.FrontLeftTyreLoadAuthorityInputN, state.FrontLeftLongitudinalForceN, state.FrontLeftLateralForceN, state.FrontLeftTyrePitchMomentNm, state.FrontLeftTyreRollMomentNm);
        WheelLoadDiagnostic fr = CreateWheel("FR", state.FrontRightShadowSuspension, state.FrontRightTunedTyreLoadN, state.FrontRightPhysicalRawTyreLoadN, state.FrontRightPhysicalClampedTyreLoadN, state.FrontRightPhysicalLoadClampLossN, state.FrontRightPhysicalFilteredTyreLoadN, state.FrontRightHybridRawTyreLoadN, state.FrontRightHybridUsableTyreLoadN, state.FrontRightTyreLoadAuthorityInputN, state.FrontRightLongitudinalForceN, state.FrontRightLateralForceN, state.FrontRightTyrePitchMomentNm, state.FrontRightTyreRollMomentNm);
        WheelLoadDiagnostic rl = CreateWheel("RL", state.RearLeftShadowSuspension, state.RearLeftTunedTyreLoadN, state.RearLeftPhysicalRawTyreLoadN, state.RearLeftPhysicalClampedTyreLoadN, state.RearLeftPhysicalLoadClampLossN, state.RearLeftPhysicalFilteredTyreLoadN, state.RearLeftHybridRawTyreLoadN, state.RearLeftHybridUsableTyreLoadN, state.RearLeftTyreLoadAuthorityInputN, state.RearLeftLongitudinalForceN, state.RearLeftLateralForceN, state.RearLeftTyrePitchMomentNm, state.RearLeftTyreRollMomentNm);
        WheelLoadDiagnostic rr = CreateWheel("RR", state.RearRightShadowSuspension, state.RearRightTunedTyreLoadN, state.RearRightPhysicalRawTyreLoadN, state.RearRightPhysicalClampedTyreLoadN, state.RearRightPhysicalLoadClampLossN, state.RearRightPhysicalFilteredTyreLoadN, state.RearRightHybridRawTyreLoadN, state.RearRightHybridUsableTyreLoadN, state.RearRightTyreLoadAuthorityInputN, state.RearRightLongitudinalForceN, state.RearRightLateralForceN, state.RearRightTyrePitchMomentNm, state.RearRightTyreRollMomentNm);
        return new LoadMetrics(fl, fr, rl, rr);
    }

    private static WheelLoadDiagnostic CreateWheel(
        string name,
        ShadowSuspensionCornerState corner,
        float tunedLoadN,
        float rawPhysicalLoadN,
        float clampedPhysicalLoadN,
        float clampLossN,
        float filteredPhysicalLoadN,
        float hybridRawLoadN,
        float hybridUsableLoadN,
        float authorityInputLoadN,
        float tyreLongitudinalForceN,
        float tyreLateralForceN,
        float tyrePitchMomentNm,
        float tyreRollMomentNm)
    {
        Vector3 supportVector = Vector3.Zero;
        float projected = clampedPhysicalLoadN;
        if (corner.HasContact && corner.SuspensionAxisWorld.LengthSquared() > 0.000001f)
        {
            supportVector = -Vector3.Normalize(corner.SuspensionAxisWorld) * corner.SupportForceN;
        }

        return new WheelLoadDiagnostic(
            name,
            corner.HasContact,
            corner.CompressionMeters,
            tunedLoadN,
            corner.SupportForceN,
            supportVector,
            corner.ContactNormal,
            projected,
            filteredPhysicalLoadN,
            corner.ArbContributionN,
            projected,
            corner.StaticPreloadForceN,
            corner.DynamicSpringForceN,
            corner.DamperForceN,
            corner.BumpStopForceN,
            corner.SpringForceN,
            corner.SupportForceN,
            corner.UnclampedSupportForceN,
            corner.SupportClampErrorN,
            clampLossN,
            tyreLongitudinalForceN,
            tyreLateralForceN,
            tyrePitchMomentNm,
            tyreRollMomentNm,
            hybridRawLoadN,
            hybridUsableLoadN,
            rawPhysicalLoadN,
            authorityInputLoadN);
    }

    private static void PrintCorner(string label, WheelLoadDiagnostic wheel)
    {
        Console.WriteLine(
            $"  {label}: contact {(wheel.HasContact ? "1" : "0")} travel {wheel.DynamicTravelMeters:0.###} tuned {wheel.TunedNormalLoadN:0.#} " +
            $"input {wheel.AuthorityInputLoadN:0.#} raw {wheel.RawProjectedRoadNormalLoadN:0.#} projected {wheel.ProjectedRoadNormalLoadN:0.#} filtered {wheel.FilteredProjectedLoadN:0.#} " +
            $"arb {wheel.ArbContributionN:0.#} phys+arb {wheel.PhysicalWithArbLoadN:0.#} hybridRaw {wheel.HybridRawLoadN:0.#} hybridUsable {wheel.HybridUsableLoadN:0.#}");
        Console.WriteLine(
            $"      support {Format(wheel.SuspensionSupportVectorWorld)} normal {Format(wheel.RoadNormal)} preload {wheel.StaticPreloadForceN:0.#} " +
            $"dynamicSpring {wheel.DynamicSpringForceN:0.#} damper {wheel.DamperForceN:0.#} bumpStop {wheel.BumpStopForceN:0.#} " +
            $"unclamped {wheel.UnclampedSupportForceN:0.#} supportClampErr {wheel.SupportClampErrorN:0.#} loadClampLoss {wheel.ProjectedLoadRateNPerSecond:0.#} totalSupport {wheel.TotalSupportForceN:0.#}");
        Console.WriteLine(
            $"      tyreLong {wheel.TyreLongitudinalForceN:0.#} tyreLat {wheel.TyreLateralForceN:0.#} pitchMoment {wheel.TyrePitchMomentNm:0.#} rollMoment {wheel.TyreRollMomentNm:0.#}");
    }

    private static Vector3 NormalForSlope(float slopeRadians, bool uphill)
    {
        float sign = uphill ? -1f : 1f;
        return Vector3.Normalize(new Vector3(0f, MathF.Cos(slopeRadians), sign * MathF.Sin(slopeRadians)));
    }

    private static string Format(Vector3 value)
    {
        return $"({value.X:0.###},{value.Y:0.###},{value.Z:0.###})";
    }

    private readonly record struct LoadMetrics(
        WheelLoadDiagnostic Fl,
        WheelLoadDiagnostic Fr,
        WheelLoadDiagnostic Rl,
        WheelLoadDiagnostic Rr)
    {
        public float SumTunedNormalLoadN => Fl.TunedNormalLoadN + Fr.TunedNormalLoadN + Rl.TunedNormalLoadN + Rr.TunedNormalLoadN;
        public float SumAuthorityInputLoadN => Fl.AuthorityInputLoadN + Fr.AuthorityInputLoadN + Rl.AuthorityInputLoadN + Rr.AuthorityInputLoadN;
        public float SumProjectedRoadNormalLoadN => Fl.ProjectedRoadNormalLoadN + Fr.ProjectedRoadNormalLoadN + Rl.ProjectedRoadNormalLoadN + Rr.ProjectedRoadNormalLoadN;
        public float SumFilteredProjectedLoadN => Fl.FilteredProjectedLoadN + Fr.FilteredProjectedLoadN + Rl.FilteredProjectedLoadN + Rr.FilteredProjectedLoadN;
        public float SumSupportMagnitudeN => Fl.SuspensionSupportMagnitudeN + Fr.SuspensionSupportMagnitudeN + Rl.SuspensionSupportMagnitudeN + Rr.SuspensionSupportMagnitudeN;
        public float SumPhysicalWithArbN => Fl.PhysicalWithArbLoadN + Fr.PhysicalWithArbLoadN + Rl.PhysicalWithArbLoadN + Rr.PhysicalWithArbLoadN;
        public float FrontProjectedLoadN => Fl.ProjectedRoadNormalLoadN + Fr.ProjectedRoadNormalLoadN;
        public float RearProjectedLoadN => Rl.ProjectedRoadNormalLoadN + Rr.ProjectedRoadNormalLoadN;
        public float LeftProjectedLoadN => Fl.ProjectedRoadNormalLoadN + Rl.ProjectedRoadNormalLoadN;
        public float RightProjectedLoadN => Fr.ProjectedRoadNormalLoadN + Rr.ProjectedRoadNormalLoadN;

        public static LoadMetrics Max(LoadMetrics a, LoadMetrics b)
        {
            return a.SumProjectedRoadNormalLoadN >= b.SumProjectedRoadNormalLoadN ? a : b;
        }

        public static LoadMetrics MaxFiltered(LoadMetrics a, LoadMetrics b)
        {
            return a.SumFilteredProjectedLoadN >= b.SumFilteredProjectedLoadN ? a : b;
        }

        public static LoadMetrics MaxInput(LoadMetrics a, LoadMetrics b)
        {
            return a.SumAuthorityInputLoadN >= b.SumAuthorityInputLoadN ? a : b;
        }

        public static LoadMetrics Min(LoadMetrics a, LoadMetrics b)
        {
            return a.SumProjectedRoadNormalLoadN <= b.SumProjectedRoadNormalLoadN ? a : b;
        }
    }

    private readonly record struct WheelLoadDiagnostic(
        string Name,
        bool HasContact,
        float DynamicTravelMeters,
        float TunedNormalLoadN,
        float SuspensionSupportMagnitudeN,
        Vector3 SuspensionSupportVectorWorld,
        Vector3 RoadNormal,
        float ProjectedRoadNormalLoadN,
        float FilteredProjectedLoadN,
        float ArbContributionN,
        float PhysicalWithArbLoadN,
        float StaticPreloadForceN,
        float DynamicSpringForceN,
        float DamperForceN,
        float BumpStopForceN,
        float SpringTotalForceN,
        float TotalSupportForceN,
        float UnclampedSupportForceN,
        float SupportClampErrorN,
        float ProjectedLoadRateNPerSecond,
        float TyreLongitudinalForceN,
        float TyreLateralForceN,
        float TyrePitchMomentNm,
        float TyreRollMomentNm,
        float HybridRawLoadN,
        float HybridUsableLoadN,
        float RawProjectedRoadNormalLoadN,
        float AuthorityInputLoadN);

    private class PlaneSampler : ITrackSurfaceSampler
    {
        private readonly Vector3 _normal;
        private readonly string _sourceName;

        public PlaneSampler(Vector3 normal, string sourceName)
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

        public bool TryGetSurfaceContact(Vector3 queryPosition, float downwardRangeMeters, out TrackSurfaceContact contact)
        {
            return TryGetSurfaceContactRay(queryPosition, -Vector3.Up, downwardRangeMeters, out contact);
        }

        public virtual bool TryGetSurfaceContactRay(Vector3 origin, Vector3 direction, float maxDistanceMeters, out TrackSurfaceContact contact)
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

            contact = new TrackSurfaceContact(origin + rayDirection * distance, _normal, _sourceName, 0, 1);
            return true;
        }
    }

    private sealed class CrestDipSampler : PlaneSampler
    {
        private readonly float _heightMeters;
        private readonly float _widthMeters;
        private readonly string _sourceName;

        public CrestDipSampler(float heightMeters, float widthMeters, string sourceName)
            : base(Vector3.Up, sourceName)
        {
            _heightMeters = heightMeters;
            _widthMeters = MathF.Max(0.5f, widthMeters);
            _sourceName = sourceName;
        }

        public override bool TryGetSurfaceContactRay(Vector3 origin, Vector3 direction, float maxDistanceMeters, out TrackSurfaceContact contact)
        {
            Vector3 rayDirection = Vector3.Normalize(direction);
            if (MathF.Abs(rayDirection.Y) < 0.0001f)
            {
                contact = default;
                return false;
            }

            float x = origin.X;
            float z = origin.Z;
            float t = MathHelper.Clamp(z / _widthMeters, -1f, 1f);
            float y = _heightMeters * 0.5f * (1f + MathF.Cos(MathF.PI * t));
            float dyDz = -_heightMeters * 0.5f * MathF.PI / _widthMeters * MathF.Sin(MathF.PI * t);
            Vector3 normal = Vector3.Normalize(new Vector3(0f, 1f, -dyDz));
            float distance = (y - origin.Y) / rayDirection.Y;
            if (distance < -0.001f || distance > maxDistanceMeters + 0.001f)
            {
                contact = default;
                return false;
            }

            contact = new TrackSurfaceContact(origin + rayDirection * distance, normal, _sourceName, 0, 1);
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

            contact = new TrackSurfaceContact(origin + rayDirection * distance, Vector3.Up, _sourceName, 0, 1);
            return true;
        }

        public float GetElevation(Vector2 position)
        {
            return position.X < 0f && position.Y > 0f ? _frontLeftHeight : 0f;
        }
    }

    private sealed class NoContactSampler : ITrackSurfaceSampler
    {
        public bool HasAuthoredSurfaceContact => true;

        public SurfaceSample Sample(Vector3 position) => new("ROAD", 1f);

        public bool TryGetSurfaceContact(Vector3 queryPosition, float downwardRangeMeters, out TrackSurfaceContact contact)
        {
            contact = default;
            return false;
        }

        public bool TryGetSurfaceContactRay(Vector3 origin, Vector3 direction, float maxDistanceMeters, out TrackSurfaceContact contact)
        {
            contact = default;
            return false;
        }

        public float GetElevation(Vector2 position) => 0f;
    }
}
