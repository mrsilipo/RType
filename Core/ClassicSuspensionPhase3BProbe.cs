using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicSuspensionPhase3BProbe
{
    private const float Gravity = 9.81f;
    private const float Dt = 1f / 120f;
    private const string EkReferenceVehiclePath = "Data/PurchaseCars/2000_Ek9_Stock.json";

    public static void Run()
    {
        VehicleSimulationParameters parameters = VehicleBuildDefinitionLoader.LoadSimulationParameters(EkReferenceVehiclePath);
        SimulationEngineParameters engine = new();
        Console.WriteLine("Classic suspension Phase 3B physical chassis probe");
        Console.WriteLine($"  Vehicle: {parameters.DisplayName} ({EkReferenceVehiclePath})");
        Console.WriteLine($"  Fixed timestep: {engine.Timing.FixedDeltaSeconds:0.######} s ({engine.Timing.FixedTickRateHz:0.#} Hz)");
        Console.WriteLine("  Tyre equations and tuned tyre-load authority remain unchanged.");
        Console.WriteLine("  Physical authority: chassis Y, vertical velocity, pitch/rate, roll/rate.");
        Console.WriteLine($"  Pitch inertia estimate: {EstimatePitchInertia(parameters):0.#} kgm2");
        Console.WriteLine($"  Roll inertia estimate: {EstimateRollInertia(parameters):0.#} kgm2");

        CompareFlatRegression("flat acceleration", parameters, engine, new VehicleInput(1f, 0f, 0f), 0f, 2.0f);
        CompareFlatRegression("flat braking", parameters, engine, new VehicleInput(0f, 1f, 0f), 30f, 1.0f);
        CompareFlatRegression("flat steady cornering", parameters, engine, new VehicleInput(0.22f, 0f, 0.35f), 22f, 2.5f);

        VehicleInput neutral = new(0f, 0f, 0f);
        ProbePhysicalCase("static flat settle", new PlaneSampler(Vector3.Up, "Flat_Driveable"), parameters, engine, neutral, 0f, 3f);
        bool staticEquilibriumPassed = ProbeStaticEquilibriumSuite(parameters, engine);
        if (!staticEquilibriumPassed)
        {
            Console.WriteLine("Static equilibrium gate did not pass; skipping moving slope/settling tests.");
            return;
        }

        ProbePhysicalCase("10deg uphill coast", new PlaneSampler(NormalForSlope(MathHelper.ToRadians(10f), uphill: true), "Incline10_Driveable"), parameters, engine, neutral, 0.5f, 1.0f);
        ProbePhysicalCase("10deg downhill coast", new PlaneSampler(NormalForSlope(MathHelper.ToRadians(10f), uphill: false), "Downhill10_Driveable"), parameters, engine, neutral, 0.5f, 1.0f);
        ProbePhysicalCase("10deg constant bank", new PlaneSampler(new Vector3(-MathF.Sin(MathHelper.ToRadians(10f)), MathF.Cos(MathHelper.ToRadians(10f)), 0f), "Bank10_Driveable"), parameters, engine, new VehicleInput(0f, 0f, 0f), 22f, 1.0f);
        ProbePhysicalCase("one-wheel 50mm bump", new OneCornerHeightSampler(0.05f, "Bump50_Driveable"), parameters, engine, neutral, 0f, 1.0f);
        ProbePhysicalCase("one-wheel 100mm bump", new OneCornerHeightSampler(0.10f, "Bump100_Driveable"), parameters, engine, neutral, 0f, 1.0f);
        ProbePhysicalCase("one-wheel drop beyond droop", new OneCornerHeightSampler(-0.50f, "Drop_Driveable"), parameters, engine, neutral, 0f, 1.0f);
        ProbePhysicalCase("all wheels airborne", new NoContactSampler(), parameters, engine, neutral, 0f, 0.35f, startY: 1.0f);
        ProbePhysicalCase("controlled small landing", new PlaneSampler(Vector3.Up, "Landing_Driveable"), parameters, engine, neutral, 0f, 0.75f, startY: 0.45f);

        ProbeSlopeSettling("settled +10 incline isolated resistance", NormalForSlope(MathHelper.ToRadians(10f), uphill: true), parameters, engine, disableResistance: true);
        ProbeSlopeSettling("settled -10 decline isolated resistance", NormalForSlope(MathHelper.ToRadians(10f), uphill: false), parameters, engine, disableResistance: true);
        ProbeSlopeSettling("free-neutral +10 incline", NormalForSlope(MathHelper.ToRadians(10f), uphill: true), parameters, engine, disableResistance: true, freeNeutral: true);
        ProbeSlopeSettling("free-neutral -10 decline", NormalForSlope(MathHelper.ToRadians(10f), uphill: false), parameters, engine, disableResistance: true, freeNeutral: true);
        ProbeSlopeSettling("settled +10 incline normal resistance", NormalForSlope(MathHelper.ToRadians(10f), uphill: true), parameters, engine, disableResistance: false);
        ProbeSlopeSettling("settled -10 decline normal resistance", NormalForSlope(MathHelper.ToRadians(10f), uphill: false), parameters, engine, disableResistance: false);
    }

    private static bool ProbeStaticEquilibriumSuite(VehicleSimulationParameters parameters, SimulationEngineParameters engine)
    {
        float angle = MathHelper.ToRadians(10f);
        bool flat = ProbeStaticEquilibrium("static equilibrium flat 0", Vector3.Up, parameters, engine);
        bool uphill = ProbeStaticEquilibrium("static equilibrium uphill +10", NormalForSlope(angle, uphill: true), parameters, engine);
        bool downhill = ProbeStaticEquilibrium("static equilibrium downhill -10", NormalForSlope(angle, uphill: false), parameters, engine);
        bool bankRight = ProbeStaticEquilibrium("static equilibrium bank +10", new Vector3(-MathF.Sin(angle), MathF.Cos(angle), 0f), parameters, engine);
        bool bankLeft = ProbeStaticEquilibrium("static equilibrium bank -10", new Vector3(MathF.Sin(angle), MathF.Cos(angle), 0f), parameters, engine);
        return flat && uphill && downhill && bankRight && bankLeft;
    }

    private static bool ProbeStaticEquilibrium(
        string label,
        Vector3 normal,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine)
    {
        PlaneSampler sampler = new(normal, label.Replace(' ', '_') + "_Driveable");
        ClassicFourWheelVehicleSimulator simulator = new(sampler, Vector3.Zero, 0f, parameters, engine)
        {
            DisableLongitudinalResistanceForProbe = true
        };
        simulator.State.Velocity = Vector2.Zero;
        simulator.InitializePhysicalChassisForProbe();
        Vector3 lockedPosition = simulator.State.Position;
        Console.WriteLine(label + ":");
        Console.WriteLine("  planar X/Z position and velocity are locked for this static support diagnostic.");
        Console.WriteLine("  time y vy ay pitch pitchRate roll rollRate FLtravel FRtravel RLtravel RRtravel FLdamper FRdamper RLdamper RRdamper FLsupport FRsupport RLsupport RRsupport contacts limits");
        int totalTicks = (int)MathF.Round(4.0f / Dt);
        int[] checkpoints = [0, 30, 60, 120, 240, totalTicks];
        int checkpointIndex = 0;
        for (int tick = 0; tick <= totalTicks; tick++)
        {
            if (checkpointIndex < checkpoints.Length && tick == checkpoints[checkpointIndex])
            {
                PrintStaticEquilibriumSample(tick * Dt, simulator.State);
                checkpointIndex++;
            }

            if (tick < totalTicks)
            {
                simulator.Update(new VehicleInput(0f, 0f, 0f), Dt);
                simulator.State.Position = new Vector3(lockedPosition.X, simulator.State.Position.Y, lockedPosition.Z);
                simulator.SetPhysicalChassisVelocityForProbe(Vector3.Zero);
            }
        }

        VehicleState state = simulator.State;
        bool pass =
            MathF.Abs(state.BodyVerticalVelocityMetersPerSecond) < 0.03f &&
            MathF.Abs(MathHelper.ToDegrees(state.BodyPitchRateRadiansPerSecond)) < 1.0f &&
            MathF.Abs(MathHelper.ToDegrees(state.BodyRollRateRadiansPerSecond)) < 1.0f &&
            AllContacts(state) &&
            !AnyLimit(state);
        Console.WriteLine($"  strict equilibrium gate: {(pass ? "PASS" : "REVIEW")}");
        Console.WriteLine(
            $"  final support {Format(state.PhysicalSuspensionTotalSupportVectorN)} N, net {Format(state.PhysicalSuspensionNetVerticalDynamicsForceN)} N, " +
            $"track long {state.TrackLongitudinalGravityForceN / MathF.Max(1f, parameters.MassKg):0.###} m/s2, lateral {state.TrackLateralGravityForceN / MathF.Max(1f, parameters.MassKg):0.###} m/s2");
        return pass;
    }

    private static void PrintStaticEquilibriumSample(float time, VehicleState state)
    {
        ShadowSuspensionCornerState fl = state.FrontLeftShadowSuspension;
        ShadowSuspensionCornerState fr = state.FrontRightShadowSuspension;
        ShadowSuspensionCornerState rl = state.RearLeftShadowSuspension;
        ShadowSuspensionCornerState rr = state.RearRightShadowSuspension;
        string contacts = $"{(fl.HasContact ? "1" : "0")}{(fr.HasContact ? "1" : "0")}{(rl.HasContact ? "1" : "0")}{(rr.HasContact ? "1" : "0")}";
        string limits = $"{LimitText(fl)}{LimitText(fr)}{LimitText(rl)}{LimitText(rr)}";
        Console.WriteLine(
            $"  {time:0.###} {state.Position.Y:0.###} {state.BodyVerticalVelocityMetersPerSecond:0.###} " +
            $"{state.PhysicalSuspensionVerticalAccelerationMetersPerSecondSquared:0.###} {MathHelper.ToDegrees(state.BodyPitchRadians):0.###} " +
            $"{MathHelper.ToDegrees(state.BodyPitchRateRadiansPerSecond):0.###} {MathHelper.ToDegrees(state.BodyRollRadians):0.###} " +
            $"{MathHelper.ToDegrees(state.BodyRollRateRadiansPerSecond):0.###} {fl.CompressionMeters:0.###} {fr.CompressionMeters:0.###} " +
            $"{rl.CompressionMeters:0.###} {rr.CompressionMeters:0.###} {fl.DamperForceN:0.#} {fr.DamperForceN:0.#} " +
            $"{rl.DamperForceN:0.#} {rr.DamperForceN:0.#} {fl.SupportForceN:0.#} {fr.SupportForceN:0.#} " +
            $"{rl.SupportForceN:0.#} {rr.SupportForceN:0.#} {contacts} {limits}");
    }

    private static bool AllContacts(VehicleState state)
    {
        return state.FrontLeftShadowSuspension.HasContact &&
            state.FrontRightShadowSuspension.HasContact &&
            state.RearLeftShadowSuspension.HasContact &&
            state.RearRightShadowSuspension.HasContact;
    }

    private static bool AnyLimit(VehicleState state)
    {
        return IsLimit(state.FrontLeftShadowSuspension) ||
            IsLimit(state.FrontRightShadowSuspension) ||
            IsLimit(state.RearLeftShadowSuspension) ||
            IsLimit(state.RearRightShadowSuspension);
    }

    private static bool IsLimit(ShadowSuspensionCornerState corner)
    {
        return corner.BumpLimitHit || corner.DroopLimitHit;
    }

    private static string LimitText(ShadowSuspensionCornerState corner)
    {
        if (corner.BumpLimitHit)
        {
            return "B";
        }

        return corner.DroopLimitHit ? "D" : "-";
    }

    private static void CompareFlatRegression(
        string label,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        VehicleInput input,
        float startSpeedMetersPerSecond,
        float seconds)
    {
        ProbeSample phase2 = RunCase(new PlaneSampler(Vector3.Up, "Flat_Driveable"), parameters, engine, input, startSpeedMetersPerSecond, seconds, phase3b: false);
        ProbeSample phase3b = RunCase(new PlaneSampler(Vector3.Up, "Flat_Driveable"), parameters, engine, input, startSpeedMetersPerSecond, seconds, phase3b: true);
        Console.WriteLine(label + ":");
        PrintComparison("  speed", phase2.Speed, phase3b.Speed, "m/s");
        PrintComparison("  long accel", phase2.LongitudinalAcceleration, phase3b.LongitudinalAcceleration, "m/s2");
        PrintComparison("  lateral accel", phase2.LateralAcceleration, phase3b.LateralAcceleration, "m/s2");
        PrintComparison("  yaw rate", MathHelper.ToDegrees(phase2.YawRate), MathHelper.ToDegrees(phase3b.YawRate), "deg/s");
        PrintComparison("  avg slip", phase2.AverageSlipDegrees, phase3b.AverageSlipDegrees, "deg");
        PrintComparison("  FL force", phase2.FrontLeftForce, phase3b.FrontLeftForce, "N");
        PrintComparison("  FR force", phase2.FrontRightForce, phase3b.FrontRightForce, "N");
        PrintComparison("  RL force", phase2.RearLeftForce, phase3b.RearLeftForce, "N");
        PrintComparison("  RR force", phase2.RearRightForce, phase3b.RearRightForce, "N");
        float largestForceDelta = MathF.Max(
            MathF.Max(MathF.Abs(phase3b.FrontLeftForce - phase2.FrontLeftForce), MathF.Abs(phase3b.FrontRightForce - phase2.FrontRightForce)),
            MathF.Max(MathF.Abs(phase3b.RearLeftForce - phase2.RearLeftForce), MathF.Abs(phase3b.RearRightForce - phase2.RearRightForce)));
        if (MathF.Abs(phase3b.Speed - phase2.Speed) > 0.08f ||
            MathF.Abs(phase3b.YawRate - phase2.YawRate) > MathHelper.ToRadians(0.25f) ||
            largestForceDelta > 75f)
        {
            throw new InvalidOperationException($"Phase 3B flat regression failed for {label}.");
        }
    }

    private static void ProbePhysicalCase(
        string label,
        ITrackSurfaceSampler sampler,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        VehicleInput input,
        float startSpeedMetersPerSecond,
        float seconds,
        float startY = 0f)
    {
        ProbeSample sample = RunCase(sampler, parameters, engine, input, startSpeedMetersPerSecond, seconds, phase3b: true, startY);
        Vector3 net = sample.NetForce;
        Console.WriteLine(label + ":");
        Console.WriteLine($"  chassis y {sample.PositionY:0.###} m, vy {sample.VerticalVelocity:0.###} m/s, ay {sample.VerticalAcceleration:0.###} m/s2");
        Console.WriteLine($"  pitch {MathHelper.ToDegrees(sample.Pitch):0.###} deg, pitchRate {MathHelper.ToDegrees(sample.PitchRate):0.###} deg/s");
        Console.WriteLine($"  roll {MathHelper.ToDegrees(sample.Roll):0.###} deg, rollRate {MathHelper.ToDegrees(sample.RollRate):0.###} deg/s");
        Console.WriteLine($"  gravity {sample.GravityForce:0.#} N, support {Format(sample.SupportVector)} N, net {Format(net)} N");
        Console.WriteLine($"  X/Z acceleration from gravity+support: ({net.X / MathF.Max(1f, parameters.MassKg):0.###}, {net.Z / MathF.Max(1f, parameters.MassKg):0.###}) m/s2");
        Console.WriteLine($"  track gravity telemetry long {sample.TrackLongitudinalGravityForce / MathF.Max(1f, parameters.MassKg):0.###} m/s2, lateral {sample.TrackLateralGravityForce / MathF.Max(1f, parameters.MassKg):0.###} m/s2");
        PrintCorner("  FL", sample.FrontLeft);
        PrintCorner("  FR", sample.FrontRight);
        PrintCorner("  RL", sample.RearLeft);
        PrintCorner("  RR", sample.RearRight);
    }

    private static ProbeSample RunCase(
        ITrackSurfaceSampler sampler,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        VehicleInput input,
        float startSpeedMetersPerSecond,
        float seconds,
        bool phase3b,
        float startY = 0f)
    {
        ClassicFourWheelVehicleSimulator simulator = new(sampler, new Vector3(0f, startY, 0f), 0f, parameters, engine)
        {
            DisablePhysicalChassisPhase3BForProbe = !phase3b
        };
        simulator.State.Velocity = new Vector2(0f, startSpeedMetersPerSecond);
        simulator.InitializePhysicalChassisForProbe();
        simulator.SetPhysicalChassisVelocityForProbe(new Vector3(0f, 0f, startSpeedMetersPerSecond));
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
            state.FrontLeftLongitudinalForceN + state.FrontLeftLateralForceN,
            state.FrontRightLongitudinalForceN + state.FrontRightLateralForceN,
            state.RearLeftLongitudinalForceN + state.RearLeftLateralForceN,
            state.RearRightLongitudinalForceN + state.RearRightLateralForceN,
            state.Position.Y,
            state.BodyVerticalVelocityMetersPerSecond,
            state.PhysicalSuspensionVerticalAccelerationMetersPerSecondSquared,
            state.BodyVelocityWorldMetersPerSecond,
            state.BodyAccelerationWorldMetersPerSecondSquared,
            state.BodyPitchRadians,
            state.BodyPitchRateRadiansPerSecond,
            state.BodyRollRadians,
            state.BodyRollRateRadiansPerSecond,
            state.PhysicalSuspensionGravityForceN,
            state.PhysicalSuspensionTotalSupportVectorN,
            state.PhysicalSuspensionNetVerticalDynamicsForceN,
            state.TrackLongitudinalGravityForceN,
            state.TrackLateralGravityForceN,
            state.FrontLeftShadowSuspension,
            state.FrontRightShadowSuspension,
            state.RearLeftShadowSuspension,
            state.RearRightShadowSuspension);
    }

    private static void PrintCorner(string label, ShadowSuspensionCornerState corner)
    {
        if (!corner.HasContact)
        {
            Console.WriteLine($"{label}: MISS {corner.MissReason}, tuned {corner.TunedNormalLoadN:0.#} N");
            return;
        }

        Vector3 axisSupport = -Vector3.Normalize(corner.SuspensionAxisWorld) * corner.SupportForceN;
        Vector3 normalSupport = Vector3.Normalize(corner.ContactNormal) * corner.SupportForceN;
        float forceDiff = Vector3.Distance(axisSupport, normalSupport);
        Console.WriteLine(
            $"{label}: travel {corner.CompressionMeters:0.###} m, vel {corner.CompressionVelocityMetersPerSecond:0.###} m/s, " +
            $"spring {corner.SpringForceN:0.#} N, damper {corner.DamperForceN:0.#} N, support {corner.SupportForceN:0.#} N, tuned {corner.TunedNormalLoadN:0.#} N");
        Console.WriteLine(
            $"      normal {Format(corner.ContactNormal)}, axis {Format(corner.SuspensionAxisWorld)}, axisNormalAngle {corner.AxisNormalAngleDegrees:0.###} deg, " +
            $"axis-vs-normal force diff {forceDiff:0.#} N, bumpLimit {corner.BumpLimitHit}, droopLimit {corner.DroopLimitHit}");
    }

    private static void PrintComparison(string label, float phase2, float phase3b, string unit)
    {
        Console.WriteLine($"{label}: phase2={phase2:0.###} phase3b={phase3b:0.###} diff={phase3b - phase2:+0.###;-0.###;0} {unit}");
    }

    private static void ProbeSlopeSettling(
        string label,
        Vector3 planeNormal,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        bool disableResistance,
        bool freeNeutral = false)
    {
        float slopeDegrees = MathHelper.ToDegrees(MathF.Asin(MathHelper.Clamp(MathF.Abs(planeNormal.Z), -1f, 1f)));
        float expectedAlongMagnitude = Gravity * MathF.Sin(MathHelper.ToRadians(slopeDegrees));
        float expectedHorizontalMagnitude = expectedAlongMagnitude * MathF.Cos(MathHelper.ToRadians(slopeDegrees));
        PlaneSampler sampler = new(planeNormal, label.Replace(' ', '_') + "_Driveable");
        ClassicFourWheelVehicleSimulator simulator = new(sampler, Vector3.Zero, 0f, parameters, engine)
        {
            DisableLongitudinalResistanceForProbe = disableResistance,
            DisableSpeedLimiterForProbe = disableResistance,
            DisableEngineBrakeForProbe = freeNeutral,
            AssistOptions = freeNeutral
                ? new ClassicFourWheelAssistOptions
                {
                    BodySlipDampingEnabled = false,
                    LateralVelocityDampingEnabled = false,
                    RearFollowEnabled = false,
                    YawRecoveryEnabled = false,
                    SpeedRetentionEnabled = false
                }
                : ClassicFourWheelAssistOptions.Default
        };

        float pitch = planeNormal.Z < 0f ? -MathHelper.ToRadians(slopeDegrees) : MathHelper.ToRadians(slopeDegrees);
        simulator.State.BodyPitchRadians = pitch;
        simulator.State.GroundPitchRadians = pitch;
        simulator.State.BodyRollRadians = 0f;
        simulator.State.GroundRollRadians = 0f;
        simulator.State.Velocity = Vector2.Zero;
        simulator.InitializePhysicalChassisForProbe();
        int settleTicks = (int)MathF.Round(3.0f / Dt);
        for (int tick = 0; tick < settleTicks; tick++)
        {
            simulator.Update(new VehicleInput(0f, 0f, 0f), Dt);
        }

        Vector3 slopeTangent = Vector3.Forward - planeNormal * Vector3.Dot(Vector3.Forward, planeNormal);
        if (slopeTangent.LengthSquared() <= 0.000001f)
        {
            slopeTangent = Vector3.Forward;
        }

        slopeTangent.Normalize();
        simulator.SetPhysicalChassisVelocityForProbe(slopeTangent * 0.5f);

        Console.WriteLine(label + ":");
        Console.WriteLine($"  expected along-slope |g*sin({slopeDegrees:0.###})| = {expectedAlongMagnitude:0.###} m/s2");
        Console.WriteLine($"  expected horizontal X/Z |g*sin(theta)*cos(theta)| = {expectedHorizontalMagnitude:0.###} m/s2");
        Console.WriteLine($"  resistance disabled: {disableResistance}");
        Console.WriteLine($"  free-neutral diagnostic: {freeNeutral}");
        Console.WriteLine("  pre-settled at zero planar speed for 3.0 s before measurement.");
        Console.WriteLine("  time posY velWorld accelWorld velAlong velPlanar tangentError pitch pitchRate avgTravel avgDamper support net xzAccel alongAccel forceResidual");
        Console.WriteLine("       alongAccelParts gravity suspension tyreLong tyreLat drive engineBrake brake rolling aero cleanup");
        int totalTicks = (int)MathF.Round(12.0f / Dt);
        int[] checkpoints = [0, 30, 60, 120, 240, 480, 960, totalTicks];
        int checkpointIndex = 0;
        for (int tick = 0; tick <= totalTicks; tick++)
        {
            if (checkpointIndex < checkpoints.Length && tick == checkpoints[checkpointIndex])
            {
                PrintSlopeSettleSample(tick * Dt, simulator.State, parameters);
                checkpointIndex++;
            }

            if (tick < totalTicks)
            {
                simulator.Update(new VehicleInput(0f, 0f, 0f), Dt);
            }
        }

        PrintSlopeCorners(simulator.State);
    }

    private static void PrintSlopeSettleSample(float time, VehicleState state, VehicleSimulationParameters parameters)
    {
        Vector3 net = state.PhysicalSuspensionNetVerticalDynamicsForceN;
        Vector3 planeNormal = AverageContactNormal(state);
        Vector3 forward = Vector3.Normalize(new Vector3(state.Forward.X, 0f, state.Forward.Z));
        Vector3 slopeTangent = forward - planeNormal * Vector3.Dot(forward, planeNormal);
        if (slopeTangent.LengthSquared() <= 0.000001f)
        {
            slopeTangent = forward;
        }

        slopeTangent.Normalize();
        Vector3 velocityWorld = state.BodyVelocityWorldMetersPerSecond;
        Vector3 accelerationWorld = state.BodyAccelerationWorldMetersPerSecondSquared;
        float velocityAlongPlane = Vector3.Dot(velocityWorld, slopeTangent);
        float accelerationAlongPlane = Vector3.Dot(accelerationWorld, slopeTangent);
        float tangentVelocityError = Vector3.Dot(velocityWorld, planeNormal);
        float planarSpeed = MathF.Sqrt(velocityWorld.X * velocityWorld.X + velocityWorld.Z * velocityWorld.Z);
        float invMass = 1f / MathF.Max(1f, parameters.MassKg);
        float gravityAlong = Vector3.Dot(state.ForceGravityWorldN, slopeTangent) * invMass;
        float suspensionAlong = Vector3.Dot(state.ForceSuspensionSupportWorldN, slopeTangent) * invMass;
        float tyreLongAlong = Vector3.Dot(state.ForceTyreLongitudinalWorldN, slopeTangent) * invMass;
        float tyreLatAlong = Vector3.Dot(state.ForceTyreLateralWorldN, slopeTangent) * invMass;
        float driveAlong = Vector3.Dot(state.ForceDriveWorldN, slopeTangent) * invMass;
        float engineBrakeAlong = Vector3.Dot(state.ForceEngineBrakeWorldN, slopeTangent) * invMass;
        float brakeAlong = Vector3.Dot(state.ForceServiceBrakeWorldN, slopeTangent) * invMass;
        float rollingAlong = Vector3.Dot(state.ForceRollingResistanceWorldN, slopeTangent) * invMass;
        float aeroAlong = Vector3.Dot(state.ForceAeroDragWorldN, slopeTangent) * invMass;
        float cleanupAlong = Vector3.Dot(state.ForceCleanupAssistWorldN, slopeTangent) * invMass;
        float residual = state.ForceDecompositionResidualWorldN.Length();
        float avgTravel = Average(
            state.FrontLeftShadowSuspension.CompressionMeters,
            state.FrontRightShadowSuspension.CompressionMeters,
            state.RearLeftShadowSuspension.CompressionMeters,
            state.RearRightShadowSuspension.CompressionMeters);
        float avgDamper = Average(
            state.FrontLeftShadowSuspension.DamperForceN,
            state.FrontRightShadowSuspension.DamperForceN,
            state.RearLeftShadowSuspension.DamperForceN,
            state.RearRightShadowSuspension.DamperForceN);
        float maxAngle = Max(
            state.FrontLeftShadowSuspension.AxisNormalAngleDegrees,
            state.FrontRightShadowSuspension.AxisNormalAngleDegrees,
            state.RearLeftShadowSuspension.AxisNormalAngleDegrees,
            state.RearRightShadowSuspension.AxisNormalAngleDegrees);
        float maxForceDiff = Max(
            AxisNormalForceDiff(state.FrontLeftShadowSuspension),
            AxisNormalForceDiff(state.FrontRightShadowSuspension),
            AxisNormalForceDiff(state.RearLeftShadowSuspension),
            AxisNormalForceDiff(state.RearRightShadowSuspension));
        Console.WriteLine(
            $"  {time:0.###} {state.Position.Y:0.###} {Format(velocityWorld)} {Format(accelerationWorld)} " +
            $"{velocityAlongPlane:0.###} {planarSpeed:0.###} {tangentVelocityError:0.###} " +
            $"{MathHelper.ToDegrees(state.BodyPitchRadians):0.###} {MathHelper.ToDegrees(state.BodyPitchRateRadiansPerSecond):0.###} " +
            $"{avgTravel:0.###} {avgDamper:0.#} {Format(state.PhysicalSuspensionTotalSupportVectorN)} {Format(net)} " +
            $"({net.X / MathF.Max(1f, parameters.MassKg):0.###},{net.Z / MathF.Max(1f, parameters.MassKg):0.###}) " +
            $"{accelerationAlongPlane:0.###} {residual:0.###} N");
        Console.WriteLine(
            $"       parts {gravityAlong:0.###} {suspensionAlong:0.###} {tyreLongAlong:0.###} {tyreLatAlong:0.###} " +
            $"{driveAlong:0.###} {engineBrakeAlong:0.###} {brakeAlong:0.###} {rollingAlong:0.###} {aeroAlong:0.###} {cleanupAlong:0.###}; " +
            $"axisAngle {maxAngle:0.###} forceDiff {maxForceDiff:0.#} N");
    }

    private static Vector3 AverageContactNormal(VehicleState state)
    {
        Vector3 sum = Vector3.Zero;
        int count = 0;
        Add(state.FrontLeftShadowSuspension);
        Add(state.FrontRightShadowSuspension);
        Add(state.RearLeftShadowSuspension);
        Add(state.RearRightShadowSuspension);
        if (count == 0 || sum.LengthSquared() <= 0.000001f)
        {
            return Vector3.Up;
        }

        return Vector3.Normalize(sum);

        void Add(ShadowSuspensionCornerState corner)
        {
            if (!corner.HasContact || corner.ContactNormal.LengthSquared() <= 0.000001f)
            {
                return;
            }

            sum += Vector3.Normalize(corner.ContactNormal);
            count++;
        }
    }

    private static void PrintSlopeCorners(VehicleState state)
    {
        Console.WriteLine("  final corners:");
        PrintCorner("    FL", state.FrontLeftShadowSuspension);
        PrintCorner("    FR", state.FrontRightShadowSuspension);
        PrintCorner("    RL", state.RearLeftShadowSuspension);
        PrintCorner("    RR", state.RearRightShadowSuspension);
    }

    private static float AxisNormalForceDiff(ShadowSuspensionCornerState corner)
    {
        if (!corner.HasContact)
        {
            return 0f;
        }

        Vector3 axisSupport = -Vector3.Normalize(corner.SuspensionAxisWorld) * corner.SupportForceN;
        Vector3 normalSupport = Vector3.Normalize(corner.ContactNormal) * corner.SupportForceN;
        return Vector3.Distance(axisSupport, normalSupport);
    }

    private static float Max(float a, float b, float c, float d)
    {
        return MathF.Max(MathF.Max(a, b), MathF.Max(c, d));
    }

    private static float Average(float a, float b, float c, float d)
    {
        return (a + b + c + d) * 0.25f;
    }

    private static Vector3 NormalForSlope(float slopeRadians, bool uphill)
    {
        float sign = uphill ? -1f : 1f;
        return Vector3.Normalize(new Vector3(0f, MathF.Cos(slopeRadians), sign * MathF.Sin(slopeRadians)));
    }

    private static float EstimatePitchInertia(VehicleSimulationParameters parameters)
    {
        float bodyHeight = MathHelper.Clamp(parameters.CenterOfGravityHeightMeters * 2f, 0.6f, 1.8f);
        float length = MathF.Max(parameters.BodyLengthMeters, parameters.WheelbaseMeters);
        return MathF.Max(1f, parameters.MassKg) * (length * length + bodyHeight * bodyHeight) / 12f;
    }

    private static float EstimateRollInertia(VehicleSimulationParameters parameters)
    {
        float bodyHeight = MathHelper.Clamp(parameters.CenterOfGravityHeightMeters * 2f, 0.6f, 1.8f);
        float width = MathF.Max(parameters.BodyWidthMeters, MathF.Max(parameters.FrontTrackMeters, parameters.RearTrackMeters));
        return MathF.Max(1f, parameters.MassKg) * (width * width + bodyHeight * bodyHeight) / 12f;
    }

    private static string Format(Vector3 value)
    {
        return $"({value.X:0.###},{value.Y:0.###},{value.Z:0.###})";
    }

    private readonly record struct ProbeSample(
        float Speed,
        float LongitudinalAcceleration,
        float LateralAcceleration,
        float YawRate,
        float AverageSlipDegrees,
        float FrontLeftForce,
        float FrontRightForce,
        float RearLeftForce,
        float RearRightForce,
        float PositionY,
        float VerticalVelocity,
        float VerticalAcceleration,
        Vector3 WorldVelocity,
        Vector3 WorldAcceleration,
        float Pitch,
        float PitchRate,
        float Roll,
        float RollRate,
        float GravityForce,
        Vector3 SupportVector,
        Vector3 NetForce,
        float TrackLongitudinalGravityForce,
        float TrackLateralGravityForce,
        ShadowSuspensionCornerState FrontLeft,
        ShadowSuspensionCornerState FrontRight,
        ShadowSuspensionCornerState RearLeft,
        ShadowSuspensionCornerState RearRight);

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
    }
}
