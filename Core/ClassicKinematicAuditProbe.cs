using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicKinematicAuditProbe
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
        VehicleAxleGeometry geometry = VehicleAxleGeometry.FromParameters(parameters);
        ClassicFourWheelTyres tyres = ClassicFourWheelVehicleSimulator.ResolveClassicTyres(parameters, engineParameters.ClassicFourWheel);

        Console.WriteLine($"Classic kinematic audit probe: {parameters.DisplayName}, model={engineParameters.HandlingModel}");
        Console.WriteLine(
            $"  geometry wheelbase={geometry.WheelbaseMeters:0.000}m cgToFront={geometry.CgToFrontAxleMeters:0.000}m cgToRear={geometry.CgToRearAxleMeters:0.000}m rear/frontDistanceRatio={geometry.CgToRearAxleMeters / MathF.Max(0.001f, geometry.CgToFrontAxleMeters):0.00}");
        Console.WriteLine(
            $"  resolved tyres front stiffness={parameters.FrontTyres.CorneringStiffnessNPerRad:0}N/rad peak={MathHelper.ToDegrees(parameters.FrontTyres.LateralPeakSlipAngleRadians):0.0}deg grip={parameters.FrontTyres.PeakFriction:0.00} relax={parameters.FrontTyres.RelaxationLengthMeters:0.00}m");
        Console.WriteLine(
            $"  resolved tyres rear  stiffness={parameters.RearTyres.CorneringStiffnessNPerRad:0}N/rad peak={MathHelper.ToDegrees(parameters.RearTyres.LateralPeakSlipAngleRadians):0.0}deg grip={parameters.RearTyres.PeakFriction:0.00} relax={parameters.RearTyres.RelaxationLengthMeters:0.00}m");
        Console.WriteLine(
            $"  classic adapter front stiffness={tyres.Front.CorneringStiffness:0.00} peak={tyres.Front.PeakSlipAngleDegrees:0.0}deg falloff={tyres.Front.FalloffSlipAngleDegrees:0.0}deg grip={tyres.Front.MaxGrip:0.00}/{tyres.Front.SlidingGrip:0.00}");
        Console.WriteLine(
            $"  classic adapter rear  stiffness={tyres.Rear.CorneringStiffness:0.00} peak={tyres.Rear.PeakSlipAngleDegrees:0.0}deg falloff={tyres.Rear.FalloffSlipAngleDegrees:0.0}deg grip={tyres.Rear.MaxGrip:0.00}/{tyres.Rear.SlidingGrip:0.00}");

        float slipFloor = engineParameters.ClassicFourWheel.LowSpeed.SlipSpeedFloorMetersPerSecond;
        PrintRawCase("zero sanity", geometry, slipFloor, 150f, 0f, 0f, 0f);
        PrintRawCase("pure yaw", geometry, slipFloor, 150f, 0f, 24f, 0f);
        PrintRawCase("pure lateral", geometry, slipFloor, 150f, 2.5f, 0f, 0f);
        PrintRawCase("combined medium", geometry, slipFloor, 150f, 1.5f, 24f, 3.5f);
        PrintRawCase("combined hard", geometry, slipFloor, 150f, 3.0f, 38f, 6.0f);

        PrintSteadyReference("steady reference medium", parameters, geometry, 150f, 3.5f);
        PrintSteadyReference("steady reference hard", parameters, geometry, 150f, 6.0f);

        PrintDynamicCase(parameters, engineParameters, "sim 150 25% medium", 150f, 0.35f, 0.25f, 2.4f);
        PrintDynamicCase(parameters, engineParameters, "sim 150 25% hard", 150f, 0.65f, 0.25f, 2.4f);
        Console.WriteLine("Classic kinematic audit probe complete.");
    }

    private static void PrintRawCase(
        string label,
        VehicleAxleGeometry geometry,
        float slipFloor,
        float speedKmh,
        float lateralSpeedMetersPerSecond,
        float yawRateDegreesPerSecond,
        float steerDegrees)
    {
        float forwardSpeed = speedKmh / 3.6f;
        float yawRate = MathHelper.ToRadians(yawRateDegreesPerSecond);
        float steer = MathHelper.ToRadians(steerDegrees);
        WheelKinematicsSample fl = WheelKinematics.Calculate(-geometry.FrontTrackMeters * 0.5f, geometry.CgToFrontAxleMeters, steer, forwardSpeed, lateralSpeedMetersPerSecond, yawRate, slipFloor);
        WheelKinematicsSample fr = WheelKinematics.Calculate(geometry.FrontTrackMeters * 0.5f, geometry.CgToFrontAxleMeters, steer, forwardSpeed, lateralSpeedMetersPerSecond, yawRate, slipFloor);
        WheelKinematicsSample rl = WheelKinematics.Calculate(-geometry.RearTrackMeters * 0.5f, -geometry.CgToRearAxleMeters, 0f, forwardSpeed, lateralSpeedMetersPerSecond, yawRate, slipFloor);
        WheelKinematicsSample rr = WheelKinematics.Calculate(geometry.RearTrackMeters * 0.5f, -geometry.CgToRearAxleMeters, 0f, forwardSpeed, lateralSpeedMetersPerSecond, yawRate, slipFloor);

        PrintKinematicSet(label, forwardSpeed, lateralSpeedMetersPerSecond, yawRateDegreesPerSecond, steerDegrees, fl, fr, rl, rr);
    }

    private static void PrintDynamicCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        string label,
        float speedKmh,
        float steerInput,
        float throttle,
        float seconds)
    {
        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engineParameters);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = 4;
        simulator.State.Velocity = new Vector2(0f, speedKmh / 3.6f);

        int ticks = Math.Max(1, (int)MathF.Round(seconds / Dt));
        for (int i = 0; i < ticks; i++)
        {
            simulator.Update(new VehicleInput(throttle, 0f, steerInput), Dt);
        }

        VehicleAxleGeometry geometry = VehicleAxleGeometry.FromParameters(parameters);
        float slipFloor = engineParameters.ClassicFourWheel.LowSpeed.SlipSpeedFloorMetersPerSecond;
        float forwardSpeed = simulator.State.SignedForwardSpeed;
        float lateralSpeed = simulator.State.LateralSpeed;
        float steer = MathHelper.ToRadians((simulator.State.FrontLeftSteerAngleDegrees + simulator.State.FrontRightSteerAngleDegrees) * 0.5f);
        WheelKinematicsSample fl = WheelKinematics.Calculate(-geometry.FrontTrackMeters * 0.5f, geometry.CgToFrontAxleMeters, steer, forwardSpeed, lateralSpeed, simulator.State.YawRateRadiansPerSecond, slipFloor);
        WheelKinematicsSample fr = WheelKinematics.Calculate(geometry.FrontTrackMeters * 0.5f, geometry.CgToFrontAxleMeters, steer, forwardSpeed, lateralSpeed, simulator.State.YawRateRadiansPerSecond, slipFloor);
        WheelKinematicsSample rl = WheelKinematics.Calculate(-geometry.RearTrackMeters * 0.5f, -geometry.CgToRearAxleMeters, 0f, forwardSpeed, lateralSpeed, simulator.State.YawRateRadiansPerSecond, slipFloor);
        WheelKinematicsSample rr = WheelKinematics.Calculate(geometry.RearTrackMeters * 0.5f, -geometry.CgToRearAxleMeters, 0f, forwardSpeed, lateralSpeed, simulator.State.YawRateRadiansPerSecond, slipFloor);

        PrintKinematicSet(label, forwardSpeed, lateralSpeed, MathHelper.ToDegrees(simulator.State.YawRateRadiansPerSecond), MathHelper.ToDegrees(steer), fl, fr, rl, rr);
        Console.WriteLine(
            $"    sim speed={simulator.State.SpeedMetersPerSecond * 3.6f:0.0}km/h rpm={simulator.State.Rpm:0} latG={simulator.State.LateralAcceleration / 9.81f:0.00} body={simulator.State.ClassicBodySlipAngleDegrees:0.0}deg bodyDamp={simulator.State.ClassicBodySlipDampingForceN:0}N");
    }

    private static void PrintKinematicSet(
        string label,
        float forwardSpeed,
        float lateralSpeed,
        float yawRateDegreesPerSecond,
        float steerDegrees,
        WheelKinematicsSample fl,
        WheelKinematicsSample fr,
        WheelKinematicsSample rl,
        WheelKinematicsSample rr)
    {
        float frontSlip = (MathF.Abs(MathHelper.ToDegrees(fl.SlipRadians)) + MathF.Abs(MathHelper.ToDegrees(fr.SlipRadians))) * 0.5f;
        float rearSlip = (MathF.Abs(MathHelper.ToDegrees(rl.SlipRadians)) + MathF.Abs(MathHelper.ToDegrees(rr.SlipRadians))) * 0.5f;
        Console.WriteLine(
            $"  {label,-22} fwd={forwardSpeed:0.00}m/s lat={lateralSpeed:+0.00;-0.00;0.00}m/s yaw={yawRateDegreesPerSecond:+0.0;-0.0;0.0}deg/s steer={steerDegrees:+0.0;-0.0;0.0}deg slipF/R={frontSlip:0.00}/{rearSlip:0.00}deg gap={rearSlip - frontSlip:+0.00;-0.00;0.00}deg");
        PrintWheel("FL", fl);
        PrintWheel("FR", fr);
        PrintWheel("RL", rl);
        PrintWheel("RR", rr);
    }

    private static void PrintWheel(string name, WheelKinematicsSample sample)
    {
        Console.WriteLine(
            $"    {name} z={sample.LocalForwardMeters:+0.000;-0.000;0.000}m x={sample.LocalRightMeters:+0.000;-0.000;0.000}m localFwd={sample.LocalForwardSpeedMetersPerSecond:+0.00;-0.00;0.00} localLat={sample.LocalLateralSpeedMetersPerSecond:+0.00;-0.00;0.00} yawLat={sample.YawLateralContributionMetersPerSecond:+0.00;-0.00;0.00} slip={MathHelper.ToDegrees(sample.SlipRadians):+0.00;-0.00;0.00}deg");
    }

    private static void PrintSteadyReference(
        string label,
        VehicleSimulationParameters parameters,
        VehicleAxleGeometry geometry,
        float speedKmh,
        float steerDegrees)
    {
        float speed = speedKmh / 3.6f;
        float steer = MathHelper.ToRadians(steerDegrees);
        float mass = MathF.Max(1f, parameters.MassKg);
        float yawInertia = MathF.Max(1f, parameters.YawInertiaKgM2);
        float cf = MathF.Max(1f, parameters.FrontTyres.CorneringStiffnessNPerRad);
        float cr = MathF.Max(1f, parameters.RearTyres.CorneringStiffnessNPerRad);
        float a = geometry.CgToFrontAxleMeters;
        float b = geometry.CgToRearAxleMeters;

        float a11 = -cf - cr;
        float a12 = (-cf * a + cr * b) / speed - mass * speed;
        float b1 = -cf * steer;
        float a21 = -a * cf + b * cr;
        float a22 = -(a * a * cf + b * b * cr) / speed;
        float b2 = -a * cf * steer;
        float det = a11 * a22 - a12 * a21;

        if (MathF.Abs(det) <= 0.001f)
        {
            Console.WriteLine($"  {label,-22} unavailable: singular steady-state reference.");
            return;
        }

        float beta = (b1 * a22 - a12 * b2) / det;
        float yawRate = (a11 * b2 - b1 * a21) / det;
        float frontSlip = steer - beta - a * yawRate / speed;
        float rearSlip = -beta + b * yawRate / speed;
        Console.WriteLine(
            $"  {label,-22} speed={speedKmh:0}km/h steer={steerDegrees:0.0}deg beta={MathHelper.ToDegrees(beta):+0.00;-0.00;0.00}deg yaw={MathHelper.ToDegrees(yawRate):+0.0;-0.0;0.0}deg/s refSlipF/R={MathHelper.ToDegrees(frontSlip):+0.00;-0.00;0.00}/{MathHelper.ToDegrees(rearSlip):+0.00;-0.00;0.00}deg gap={MathF.Abs(MathHelper.ToDegrees(rearSlip)) - MathF.Abs(MathHelper.ToDegrees(frontSlip)):+0.00;-0.00;0.00}deg inertia={yawInertia:0}kgm2");
    }

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}

