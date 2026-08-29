using Microsoft.Xna.Framework;

namespace RType.Vehicle;

public readonly record struct ShiftShockInput(
    int PreviousGear,
    int TargetGear,
    float Throttle,
    float CurrentRpm,
    float TargetRpm,
    float ForcedTargetRpm,
    float PreviousGearRatio,
    float TargetGearRatio,
    VehicleSimulationParameters Parameters);

public static class ShiftShockModel
{
    public static float Calculate(ShiftShockInput input)
    {
        if (input.PreviousGear <= 0 || input.TargetGear <= 0 || input.PreviousGear == input.TargetGear)
        {
            return 0f;
        }

        VehicleSimulationParameters parameters = input.Parameters;
        float throttleT = SmoothStep(0.30f, 0.94f, MathHelper.Clamp(input.Throttle, 0f, 1f));
        if (throttleT <= 0.001f)
        {
            return 0f;
        }

        float effectiveTargetRpm = MathF.Max(input.TargetRpm, input.ForcedTargetRpm);
        float rpmMismatch = MathF.Abs(effectiveTargetRpm - input.CurrentRpm);
        float rpmMismatchT = SmoothStep(480f, 2800f, rpmMismatch);
        float highRpmT = SmoothStep(parameters.PowerRedlineRpm * 0.50f, parameters.PowerRedlineRpm * 0.98f, input.CurrentRpm);
        float torqueT = SmoothStep(55f, 190f, parameters.TorqueAtRpm(input.CurrentRpm) * throttleT);
        float gearRatioJumpT = CalculateGearRatioJump(input.PreviousGearRatio, input.TargetGearRatio);
        float clutchFactor = CalculateClutchFactor(parameters);
        float flywheelFactor = CalculateFlywheelFactor(parameters.EngineRotationalInertiaKgM2);
        float gearboxFactor = CalculateGearboxFactor(parameters);
        float diffFactor = CalculateDifferentialFactor(parameters);
        float downshiftFactor = input.TargetGear < input.PreviousGear ? 0.72f : 1f;
        float overRevFactor = SmoothStep(
            parameters.LimiterHardCutRpm + parameters.DownshiftOverRevToleranceRpm,
            GetMechanicalOverRevLimit(parameters),
            input.ForcedTargetRpm);

        float baseShock =
            0.20f +
            rpmMismatchT * 0.34f +
            highRpmT * 0.14f +
            torqueT * 0.16f +
            gearRatioJumpT * 0.16f;
        float drivetrainFactor = clutchFactor * flywheelFactor * gearboxFactor * diffFactor * downshiftFactor;
        float overRevShock = overRevFactor * 0.24f;

        return MathHelper.Clamp(
            (baseShock * drivetrainFactor + overRevShock) * throttleT,
            0f,
            0.72f);
    }

    private static float CalculateGearRatioJump(float previousRatio, float targetRatio)
    {
        previousRatio = MathF.Abs(previousRatio);
        targetRatio = MathF.Abs(targetRatio);
        if (previousRatio <= 0.0001f || targetRatio <= 0.0001f)
        {
            return 0.45f;
        }

        float ratioChange = MathF.Abs(targetRatio - previousRatio) / MathF.Max(previousRatio, targetRatio);
        return MathHelper.Clamp(ratioChange / 0.42f, 0.35f, 1f);
    }

    private static float CalculateClutchFactor(VehicleSimulationParameters parameters)
    {
        float sharpness = MathHelper.Clamp(parameters.ClutchEngagementSharpness, 0.45f, 2.0f);
        float damping = MathHelper.Clamp(parameters.ClutchSlipDamping, 0.45f, 1.35f);
        float bite = MathHelper.Clamp(1.08f - parameters.ClutchEngagementPoint, 0.55f, 0.90f);
        float catalogKick = MathHelper.Clamp(parameters.ClutchShiftKickIntensity, 0.40f, 1.35f);
        return MathHelper.Clamp(catalogKick * sharpness * bite / MathF.Max(0.45f, damping), 0.48f, 1.55f);
    }

    private static float CalculateFlywheelFactor(float inertiaKgM2)
    {
        float inertia = MathHelper.Clamp(inertiaKgM2, 0.08f, 0.32f);
        return MathHelper.Clamp(0.95f + (0.18f - inertia) * 2.1f, 0.72f, 1.18f);
    }

    private static float CalculateGearboxFactor(VehicleSimulationParameters parameters)
    {
        float typeFactor = parameters.GearboxType.Equals("dogbox", StringComparison.OrdinalIgnoreCase)
            ? 1.14f
            : 0.84f;
        float shiftTime = MathHelper.Clamp(parameters.ManualShiftTimeSeconds, 0.10f, 0.34f);
        float speedFactor = MathHelper.Lerp(1.14f, 0.84f, SmoothStep(0.12f, 0.30f, shiftTime));
        return MathHelper.Clamp(typeFactor * speedFactor * parameters.GearboxShiftShockMultiplier, 0.50f, 1.70f);
    }

    private static float CalculateDifferentialFactor(VehicleSimulationParameters parameters)
    {
        float tbrFactor = MathHelper.Lerp(0.96f, 1.06f, SmoothStep(1.0f, 4.2f, parameters.DifferentialTorqueBiasRatio));
        float preloadFactor = MathHelper.Lerp(0.96f, 1.06f, SmoothStep(0f, 70f, parameters.DifferentialPreloadTorqueNm));
        return MathHelper.Clamp(tbrFactor * preloadFactor, 0.88f, 1.14f);
    }

    private static float GetMechanicalOverRevLimit(VehicleSimulationParameters parameters)
    {
        return parameters.DownshiftMechanicalOverRevLimitRpm > 0f
            ? parameters.DownshiftMechanicalOverRevLimitRpm
            : parameters.LimiterHardCutRpm + MathF.Max(900f, parameters.LimiterHardCutRpm * 0.22f);
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float t = MathHelper.Clamp((value - edge0) / MathF.Max(0.001f, edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
