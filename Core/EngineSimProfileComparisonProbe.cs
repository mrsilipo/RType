using RetroRacer.Data;
using RetroRacer.Vehicle;
using Microsoft.Xna.Framework;

namespace RetroRacer.Core;

public static class EngineSimProfileComparisonProbe
{
    private const string VehiclePath = "Data/Vehicles/ek9_reference_2000.json";
    private const string B16ProfilePath = "Data/EngineProfiles/honda_b16b_ek9_engine_sim.json";
    private const string B18ProfilePath = "Data/EngineProfiles/honda_b18c5_vtec_engine_sim.json";

    public static void Run()
    {
        VehicleSimulationParameters b16 = VehicleDefinitionLoader.LoadSimulationParameters(VehiclePath, B16ProfilePath);
        VehicleSimulationParameters b18 = VehicleDefinitionLoader.LoadSimulationParameters(VehiclePath, B18ProfilePath);

        Console.WriteLine("Engine Sim profile comparison");
        Console.WriteLine("  same EK9 chassis, separate engine profiles");
        Console.WriteLine($"  B16 torque profile: {string.Join(",", b16.Audio.EngineSimulatorProfileTorqueCurveNm.Select(value => value.ToString("0")))}");
        Console.WriteLine($"  B18 torque profile: {string.Join(",", b18.Audio.EngineSimulatorProfileTorqueCurveNm.Select(value => value.ToString("0")))}");
        Console.WriteLine("  rpm | B16 engine/final/raw/brake/VTEC | B18 engine/final/raw/brake/VTEC");

        foreach (float rpm in new[] { 2500f, 4500f, 5800f, 6500f, 7800f, 8500f })
        {
            EnginePowerUnitState b16State = Advance(b16, rpm, 1f, EnginePowerUnitPhase.Driving);
            EnginePowerUnitState b18State = Advance(b18, rpm, 1f, EnginePowerUnitPhase.Driving);
            Console.WriteLine(
                $"  {rpm,4:0} | {b16State.EngineDriveTorqueNm,6:0.0}/{b16State.DriveTorqueNm,6:0.0}/{b16State.RawPositiveTorqueNm,6:0.0}/{b16State.EngineBrakeTorqueNm,6:0.0}/{b16State.VtecBlend:0.00} | " +
                $"{b18State.EngineDriveTorqueNm,6:0.0}/{b18State.DriveTorqueNm,6:0.0}/{b18State.RawPositiveTorqueNm,6:0.0}/{b18State.EngineBrakeTorqueNm,6:0.0}/{b18State.VtecBlend:0.00}");
        }

        Console.WriteLine("  launch | peak drive torque / final crank rpm");
        EnginePowerUnitState b16Launch = RunLaunch(b16);
        EnginePowerUnitState b18Launch = RunLaunch(b18);
        Console.WriteLine($"  B16B   | {b16Launch.DriveTorqueNm:0.0} Nm / {b16Launch.CrankRpm:0} rpm");
        Console.WriteLine($"  B18C5  | {b18Launch.DriveTorqueNm:0.0} Nm / {b18Launch.CrankRpm:0} rpm");
    }

    private static EnginePowerUnitState Advance(
        VehicleSimulationParameters vehicle,
        float rpm,
        float throttle,
        EnginePowerUnitPhase phase)
    {
        EngineSimPowerUnit powerUnit = new(vehicle);
        float gearRatio = vehicle.ForwardGearRatios.Length >= 3 ? vehicle.ForwardGearRatios[2] : 1f;
        EnginePowerUnitState state = EnginePowerUnitState.Disabled;
        for (int i = 0; i < 30; i++)
        {
            state = powerUnit.Advance(new EnginePowerUnitRequest(
                rpm,
                throttle,
                22f,
                0f,
                1f,
                throttle <= 0.01f ? 0.65f : 0f,
                0f,
                3,
                gearRatio,
                0f,
                vehicle.FinalDriveRatio,
                vehicle.WheelRadiusMeters,
                1f,
                phase,
                1f,
                0f,
                0f,
                1f / 60f));
        }

        return state;
    }

    private static EnginePowerUnitState RunLaunch(VehicleSimulationParameters vehicle)
    {
        EngineSimPowerUnit powerUnit = new(vehicle);
        float gearRatio = vehicle.ForwardGearRatios.Length > 0 ? vehicle.ForwardGearRatios[0] : 1f;
        EnginePowerUnitState peak = EnginePowerUnitState.Disabled;
        for (int i = 0; i < 120; i++)
        {
            EnginePowerUnitState state = powerUnit.Advance(new EnginePowerUnitRequest(
                4200f,
                1f,
                i * 0.05f,
                0f,
                1f,
                0f,
                0f,
                1,
                gearRatio,
                0f,
                vehicle.FinalDriveRatio,
                vehicle.WheelRadiusMeters,
                MathHelper.Clamp(i / 90f, 0.15f, 1f),
                EnginePowerUnitPhase.Launch,
                i / 120f,
                0.05f,
                2200f,
                1f / 60f));
            if (state.DriveTorqueNm > peak.DriveTorqueNm)
            {
                peak = state;
            }
        }

        return peak;
    }
}
