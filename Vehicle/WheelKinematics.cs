using Microsoft.Xna.Framework;

namespace RType.Vehicle;

public static class WheelKinematics
{
    public static WheelKinematicsSample Calculate(
        float localRightMeters,
        float localForwardMeters,
        float steerRadians,
        float chassisForwardSpeedMetersPerSecond,
        float chassisLateralSpeedMetersPerSecond,
        float yawRateRadiansPerSecond,
        float slipSpeedFloorMetersPerSecond)
    {
        float localForwardSpeed = chassisForwardSpeedMetersPerSecond - yawRateRadiansPerSecond * localRightMeters;
        float yawLateralContribution = yawRateRadiansPerSecond * localForwardMeters;
        float localLateralSpeed = chassisLateralSpeedMetersPerSecond + yawLateralContribution;
        float slipDenominator = EffectiveSlipSpeed(localForwardSpeed, slipSpeedFloorMetersPerSecond);
        float wheelRightSpeed = localLateralSpeed * MathF.Cos(steerRadians) -
            localForwardSpeed * MathF.Sin(steerRadians);
        float slipRadians = -MathF.Atan2(wheelRightSpeed, slipDenominator);

        return new WheelKinematicsSample(
            localRightMeters,
            localForwardMeters,
            steerRadians,
            localForwardSpeed,
            localLateralSpeed,
            yawLateralContribution,
            wheelRightSpeed,
            slipDenominator,
            slipRadians);
    }

    public static float EffectiveSlipSpeed(float signedForwardSpeed, float floor)
    {
        float safeFloor = MathF.Max(0.1f, floor);
        return MathF.Sqrt(signedForwardSpeed * signedForwardSpeed + safeFloor * safeFloor);
    }
}

public readonly record struct WheelKinematicsSample(
    float LocalRightMeters,
    float LocalForwardMeters,
    float SteerRadians,
    float LocalForwardSpeedMetersPerSecond,
    float LocalLateralSpeedMetersPerSecond,
    float YawLateralContributionMetersPerSecond,
    float WheelRightSpeedMetersPerSecond,
    float SlipDenominatorMetersPerSecond,
    float SlipRadians);
