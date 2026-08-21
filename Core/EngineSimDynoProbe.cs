using RetroRacer.Data;
using RetroRacer.Vehicle;

namespace RetroRacer.Core;

public static class EngineSimDynoProbe
{
    private const string VehiclePath = "Data/Vehicles/ek9_reference_2000.json";
    private const string B16ProfilePath = "Data/EngineProfiles/honda_b16b_ek9_engine_sim.json";
    private const string B18ProfilePath = "Data/EngineProfiles/honda_b18c5_vtec_engine_sim.json";

    public static void Run()
    {
        VehicleSimulationParameters b16 = VehicleDefinitionLoader.LoadSimulationParameters(VehiclePath, B16ProfilePath);
        VehicleSimulationParameters b18 = VehicleDefinitionLoader.LoadSimulationParameters(VehiclePath, B18ProfilePath);

        Console.WriteLine("Engine Sim static dyno probe");
        Console.WriteLine("  fixed RPM, full throttle, no clutch or driveline coupling");
        Console.WriteLine("  rpm | B16 torque/brake/raw/VTEC | B18 torque/brake/raw/VTEC");

        foreach (float rpm in new[] { 1500f, 2500f, 3000f, 4500f, 5800f, 6500f, 7000f, 7800f, 8500f })
        {
            EnginePowerUnitState b16State = Sample(b16, rpm);
            EnginePowerUnitState b18State = Sample(b18, rpm);
            Console.WriteLine(
                $"  {rpm,4:0} | {b16State.EngineDriveTorqueNm,6:0.0}/{b16State.EngineBrakeTorqueNm,6:0.0}/{b16State.RawPositiveTorqueNm,6:0.0}/{b16State.VtecBlend:0.00} | " +
                $"{b18State.EngineDriveTorqueNm,6:0.0}/{b18State.EngineBrakeTorqueNm,6:0.0}/{b18State.RawPositiveTorqueNm,6:0.0}/{b18State.VtecBlend:0.00}");
        }
    }

    private static EnginePowerUnitState Sample(VehicleSimulationParameters vehicle, float rpm)
    {
        EngineSimPowerUnit powerUnit = new(vehicle, ownDriveline: false);
        EnginePowerUnitState state = EnginePowerUnitState.Disabled;
        for (int i = 0; i < 60; i++)
        {
            state = powerUnit.Advance(new EnginePowerUnitRequest(
                rpm,
                1f,
                22f,
                0f,
                1f,
                0f,
                0f,
                3,
                1f,
                0f,
                vehicle.FinalDriveRatio,
                vehicle.WheelRadiusMeters,
                0f,
                EnginePowerUnitPhase.Driving,
                1f,
                0f,
                0f,
                1f / 60f));
        }

        return state;
    }
}
