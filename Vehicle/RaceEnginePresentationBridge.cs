using Microsoft.Xna.Framework;

namespace RType.Vehicle;

public static class RaceEnginePresentationBridge
{
    public static void ApplyAudioState(
        VehicleState state,
        VehicleSimulationParameters parameters,
        float deltaSeconds,
        float? crankPhaseDegrees = null)
    {
        float rpm = MathF.Max(300f, state.Rpm);
        float redline = MathF.Max(450f, parameters.RedlineRpm);
        float dt = MathHelper.Clamp(deltaSeconds, 0f, 1f / 20f);
        float throttle = MathHelper.Clamp(MathF.Max(state.Throttle, state.EffectiveThrottle), 0f, 1f);
        float vtec = parameters.VtecEnabled
            ? SmoothStep(
                parameters.VtecActivationRpm,
                parameters.VtecActivationRpm + MathF.Max(1f, parameters.VtecTransitionWidthRpm),
                rpm)
            : 0f;
        bool limiter = state.RevLimiterActive || rpm >= redline - 1f;

        state.RedlineRpm = redline;
        state.EnginePowerUnitActive = true;
        state.EnginePowerUnitCrankRpm = rpm;
        state.EnginePowerUnitVtecBlend = vtec;
        state.EnginePowerUnitVtecKickIntensity = MathHelper.Clamp(MathF.Max(state.ShiftKickIntensity, state.PowertrainShockIntensity) * vtec, 0f, 1f);
        state.EnginePowerUnitLoad = MathHelper.Clamp(throttle * 0.72f + SmoothStep(2600f, redline, rpm) * 0.18f + state.Brake * 0.10f, 0f, 1f);
        state.EnginePowerUnitFuelCutBlend = limiter ? 1f : 0f;
        state.EnginePowerUnitCrankPhaseDegrees = crankPhaseDegrees ??
            (state.EnginePowerUnitCrankPhaseDegrees + rpm / 60f * 360f * dt) % 720f;
        state.EnginePowerUnitTransmissionRpm = MathF.Abs(state.SpeedMetersPerSecond) / MathF.Max(0.001f, parameters.WheelRadiusMeters) * 60f / MathHelper.TwoPi;
        state.EnginePowerUnitAfterfireBlend = (1f - throttle) * SmoothStep(4200f, redline, rpm);
        state.EnginePowerUnitEngineDriveTorqueNm = parameters.TorqueAtRpm(rpm) * throttle * state.LimiterTorqueMultiplier;
        state.EnginePowerUnitCrankFrictionTorqueNm = parameters.EngineBrakeTorqueAtRpm(rpm) * (1f - throttle);
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float t = MathHelper.Clamp((value - edge0) / MathF.Max(0.001f, edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
