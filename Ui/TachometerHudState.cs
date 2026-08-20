using RetroRacer.Vehicle;

namespace RetroRacer.Ui;

public readonly record struct TachometerHudState(
    float Rpm,
    float RedlineRpm,
    float SpeedMetersPerSecond,
    string GearValue,
    bool RevLimiterActive,
    bool MechanicalOverRevActive)
{
    public static TachometerHudState FromVehicle(VehicleState vehicle)
    {
        string gear = vehicle.Gear < 0 ? "R" : vehicle.Gear == 0 ? "N" : vehicle.Gear.ToString();
        return new TachometerHudState(
            vehicle.DisplayedRpm,
            vehicle.RedlineRpm,
            MathF.Abs(vehicle.SignedForwardSpeed),
            gear,
            vehicle.RevLimiterActive,
            vehicle.MechanicalOverRevActive);
    }
}
