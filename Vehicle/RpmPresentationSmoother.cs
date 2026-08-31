using Microsoft.Xna.Framework;

namespace RType.Vehicle;

public static class RpmPresentationSmoother
{
    public static void Update(VehicleState state, float dt)
    {
        float rawRpm = MathF.Max(300f, state.Rpm);
        float previousPhysicsRpm = MathF.Max(300f, state.PreviousPhysicsRpm);
        float limiterHardCutRpm = MathF.Max(450f, state.LimiterHardCutRpm);
        float physicsAlpha = MathHelper.Clamp(state.PhysicsTickAlpha, 0f, 1f);
        float physicsRpmDelta = rawRpm - previousPhysicsRpm;
        float projectedRpm = !state.IsShifting && physicsRpmDelta > 0f
            ? rawRpm + physicsRpmDelta * physicsAlpha
            : rawRpm;
        projectedRpm = MathHelper.Clamp(projectedRpm, 300f, MathF.Max(rawRpm, previousPhysicsRpm) + 700f);
        bool limiterPinned = (state.RevLimiterActive || state.MechanicalOverRevActive) && limiterHardCutRpm > 0f;
        if (limiterPinned)
        {
            projectedRpm = MathF.Min(projectedRpm, limiterHardCutRpm);
        }

        float vtecKick = MathHelper.Clamp(state.EnginePowerUnitVtecKickIntensity, 0f, 1f);
        bool vtecTransient = !limiterPinned &&
                             vtecKick > 0.02f &&
                             state.Gear > 0 &&
                             state.Throttle > 0.20f &&
                             state.Brake < 0.05f;

        if (state.DisplayedRpm <= 0f)
        {
            state.DisplayedRpm = projectedRpm;
            state.DisplayedRpmTarget = projectedRpm;
            state.DisplayedRpmVelocity = 0f;
            ClampLimiterDisplay(state, limiterPinned, limiterHardCutRpm);
            return;
        }

        float clampedDt = MathHelper.Clamp(dt, 0f, 1f / 20f);
        if (clampedDt <= 0f)
        {
            state.DisplayedRpmTarget = projectedRpm;
            state.DisplayedRpmVelocity = 0f;
            return;
        }

        if (state.DisplayedRpmTarget <= 0f)
        {
            state.DisplayedRpmTarget = projectedRpm;
        }

        bool pullingThroughGear = !state.IsShifting &&
                                  state.Gear > 0 &&
                                  state.Throttle > 0.20f &&
                                  state.Brake < 0.05f;
        bool finalShiftSettle = state.IsShifting &&
                                state.ShiftTimeRemainingSeconds <= MathF.Max(0.05f, clampedDt * 2f);
        bool largeRpmDrop = projectedRpm < state.DisplayedRpm - 450f;
        bool powertrainStress = state.MechanicalOverRevActive ||
                                state.PowertrainShockIntensity > 0.02f ||
                                state.RevLimiterBounceIntensity > 0.02f ||
                                vtecTransient;
        float targetResponseRate = state.IsShifting
            ? finalShiftSettle ? 72f : 38f
            : powertrainStress
                ? 56f
            : largeRpmDrop
                ? 34f
            : pullingThroughGear
                ? 30f
                : 18f;
        float targetBlend = MathHelper.Clamp(1f - MathF.Exp(-targetResponseRate * clampedDt), 0f, 1f);
        state.DisplayedRpmTarget = limiterPinned && state.RevLimiterBounceIntensity > 0.05f
            ? projectedRpm
            : MathHelper.Lerp(state.DisplayedRpmTarget, projectedRpm, targetBlend);

        float targetRpm = limiterPinned
            ? MathF.Min(state.DisplayedRpmTarget, limiterHardCutRpm)
            : state.DisplayedRpmTarget;
        if (limiterPinned && state.RevLimiterBounceIntensity > 0.05f)
        {
            state.DisplayedRpm = targetRpm;
            state.DisplayedRpmVelocity = 0f;
            ClampLimiterDisplay(state, limiterPinned, limiterHardCutRpm);
            return;
        }

        float delta = targetRpm - state.DisplayedRpm;
        float displayResponseRate;
        float maxRisePerSecond;
        float maxFallPerSecond;
        float accelerationLimit;
        if (state.IsShifting)
        {
            displayResponseRate = finalShiftSettle ? 64f : 36f;
            maxRisePerSecond = finalShiftSettle ? 30000f : 24000f;
            maxFallPerSecond = finalShiftSettle ? 30000f : 24000f;
            accelerationLimit = finalShiftSettle ? 220000f : 150000f;
        }
        else if (powertrainStress)
        {
            displayResponseRate = limiterPinned ? 82f : MathHelper.Lerp(42f, 58f, vtecKick);
            maxRisePerSecond = limiterPinned ? 52000f : MathHelper.Lerp(28000f, 36000f, vtecKick);
            maxFallPerSecond = limiterPinned ? 52000f : MathHelper.Lerp(22000f, 26000f, vtecKick);
            accelerationLimit = limiterPinned ? 520000f : MathHelper.Lerp(180000f, 260000f, vtecKick);
        }
        else if (largeRpmDrop)
        {
            displayResponseRate = 24f;
            maxRisePerSecond = 7200f;
            maxFallPerSecond = 16000f;
            accelerationLimit = 140000f;
        }
        else if (pullingThroughGear)
        {
            displayResponseRate = 18f;
            maxRisePerSecond = 9200f;
            maxFallPerSecond = 4200f;
            accelerationLimit = 90000f;
        }
        else
        {
            displayResponseRate = delta >= 0f ? 9f : 7f;
            maxRisePerSecond = 5600f;
            maxFallPerSecond = 5200f;
            accelerationLimit = 36000f;
        }

        float desiredVelocity = MathHelper.Clamp(
            delta * displayResponseRate,
            -maxFallPerSecond,
            maxRisePerSecond);
        state.DisplayedRpmVelocity = MoveTowards(
            state.DisplayedRpmVelocity,
            desiredVelocity,
            accelerationLimit * clampedDt);

        float nextRpm = state.DisplayedRpm + state.DisplayedRpmVelocity * clampedDt;
        if ((state.DisplayedRpmVelocity > 0f && nextRpm > targetRpm) ||
            (state.DisplayedRpmVelocity < 0f && nextRpm < targetRpm))
        {
            nextRpm = targetRpm;
            state.DisplayedRpmVelocity = 0f;
        }

        float stressMargin = powertrainStress ? 850f : 500f;
        float maximumDisplayedRpm = limiterPinned
            ? limiterHardCutRpm
            : MathF.Max(projectedRpm, targetRpm) + stressMargin;
        state.DisplayedRpm = MathHelper.Clamp(nextRpm, 300f, maximumDisplayedRpm);
        ClampLimiterDisplay(state, limiterPinned, limiterHardCutRpm);
    }

    private static float MoveTowards(float current, float target, float maximumDelta)
    {
        float delta = target - current;
        if (MathF.Abs(delta) <= maximumDelta)
        {
            return target;
        }

        return current + MathF.Sign(delta) * maximumDelta;
    }

    private static void ClampLimiterDisplay(VehicleState state, bool limiterPinned, float limiterHardCutRpm)
    {
        if (!limiterPinned)
        {
            return;
        }

        float bounceDepth = RevLimiterPresentationRules.CalculateBounceDepthRpm(limiterHardCutRpm);
        float minimumLimiterDisplayRpm = limiterHardCutRpm - bounceDepth * 0.62f;
        state.DisplayedRpm = MathHelper.Clamp(state.DisplayedRpm, minimumLimiterDisplayRpm, limiterHardCutRpm);
        state.DisplayedRpmTarget = MathHelper.Clamp(state.DisplayedRpmTarget, minimumLimiterDisplayRpm, limiterHardCutRpm);
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float t = MathHelper.Clamp((value - edge0) / MathF.Max(0.001f, edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
