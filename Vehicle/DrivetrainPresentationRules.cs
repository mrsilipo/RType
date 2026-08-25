using Microsoft.Xna.Framework;

namespace RType.Vehicle;

public static class DrivetrainPresentationRules
{
    public static float CalculateSignedGearSpeedMetersPerSecond(
        VehicleSimulationParameters parameters,
        int gear,
        float rpm,
        float fallbackSignedSpeedMetersPerSecond)
    {
        float gearRatio = ResolveGearRatio(parameters, gear);
        if (gear == 0 || gearRatio <= 0f || parameters.FinalDriveRatio <= 0f || parameters.WheelRadiusMeters <= 0f)
        {
            return fallbackSignedSpeedMetersPerSecond;
        }

        float wheelRpm = MathF.Max(0f, rpm) / MathF.Max(0.001f, gearRatio * parameters.FinalDriveRatio);
        float sign = gear < 0 ? -1f : 1f;
        return sign * wheelRpm / 60f * MathHelper.TwoPi * parameters.WheelRadiusMeters;
    }

    public static float CalculateDisplayedSpeedMetersPerSecond(
        VehicleState state,
        VehicleSimulationParameters parameters)
    {
        return MathF.Abs(state.SignedForwardSpeed);
    }

    private static float ResolveGearRatio(VehicleSimulationParameters parameters, int gear)
    {
        if (gear < 0)
        {
            return parameters.ReverseGearRatio;
        }

        if (gear == 0 || parameters.ForwardGearRatios.Length == 0)
        {
            return 0f;
        }

        return parameters.ForwardGearRatios[
            Math.Clamp(gear, 1, parameters.ForwardGearRatios.Length) - 1];
    }
}
