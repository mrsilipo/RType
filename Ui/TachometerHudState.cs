using RType.Vehicle;

namespace RType.Ui;

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
            vehicle.DisplayedSpeedMetersPerSecond > 0f
                ? vehicle.DisplayedSpeedMetersPerSecond
                : MathF.Abs(vehicle.SignedForwardSpeed),
            gear,
            vehicle.RevLimiterActive,
            vehicle.MechanicalOverRevActive);
    }
}
