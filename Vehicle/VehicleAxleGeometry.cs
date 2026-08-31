using Microsoft.Xna.Framework;

namespace RType.Vehicle;

public readonly record struct VehicleAxleGeometry(
    float WheelbaseMeters,
    float CgToFrontAxleMeters,
    float CgToRearAxleMeters,
    float FrontTrackMeters,
    float RearTrackMeters)
{
    public static VehicleAxleGeometry FromParameters(VehicleSimulationParameters parameters)
    {
        float wheelbase = MathF.Max(0.1f, parameters.WheelbaseMeters);
        float frontWeight = MathHelper.Clamp(parameters.FrontWeightDistribution, 0.05f, 0.95f);
        float cgToRear = wheelbase * frontWeight;
        float cgToFront = wheelbase - cgToRear;

        return new VehicleAxleGeometry(
            wheelbase,
            cgToFront,
            cgToRear,
            MathF.Max(0.1f, parameters.FrontTrackMeters),
            MathF.Max(0.1f, parameters.RearTrackMeters));
    }
}

