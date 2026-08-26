using Microsoft.Xna.Framework;
using RType.Camera;
using RType.Vehicle;

namespace RType.Audio;

internal readonly record struct EngineAudioFrame(
    float Rpm,
    float LimiterHardCutRpm,
    float Throttle,
    float ShapedThrottle,
    float Load,
    float VtecBlend,
    float VtecKickIntensity,
    float Limiter,
    bool HardLimiterActive,
    float LimiterBouncePhase,
    float Overrun,
    float Shock,
    float IntakeDrive,
    float ThrottleTransient,
    float DrivelineDrive,
    float DriveVolume,
    float PauseScale,
    CameraMode CameraMode,
    int Gear,
    float GearRatio,
    float FinalDriveRatio,
    float ClutchEngagement,
    bool IsShifting,
    float SpeedMetersPerSecond,
    float DrivenWheelSpeedMetersPerSecond,
    float VehicleSpeedMetersPerSecond,
    float EngineDriveTorqueNm,
    float DriveForceN,
    float EngineBrakeTorqueNm,
    float ClutchSlipRpm,
    float ClutchTorqueNm,
    float CrankFrictionTorqueNm,
    float TransmissionRpm,
    float Backfire,
    float CrankPhaseDegrees,
    float DeltaSeconds)
{
    public bool Audible => DriveVolume > 0.001f && PauseScale > 0f;

    public static EngineAudioFrame FromVehicleState(
        VehicleAudioParameters parameters,
        VehicleState vehicle,
        float rpm,
        float highRpmBlend,
        float driveVolume,
        CameraMode cameraMode,
        bool paused,
        float throttleTransient,
        float deltaSeconds)
    {
        float pauseScale = paused ? 0f : 1f;
        float limiterHardCutRpm = MathF.Max(450f, vehicle.LimiterHardCutRpm);
        float throttle = MathHelper.Clamp(MathF.Max(vehicle.Throttle, vehicle.EffectiveThrottle), 0f, 1f);
        float shapedThrottle = MathF.Pow(throttle, MathF.Max(0.1f, parameters.RaceAudioThrottleGamma));
        float overrun = CalculateOverrun(throttle, rpm, limiterHardCutRpm, vehicle.SpeedMetersPerSecond);
        float vtecKick = MathHelper.Clamp(vehicle.EnginePowerUnitVtecKickIntensity, 0f, 1f);
        float vtecBlend = MathHelper.Clamp(
            MathF.Max(highRpmBlend, vehicle.EnginePowerUnitVtecBlend) + vtecKick * 0.32f,
            0f,
            1f);
        float load = CalculateLoad(vehicle, shapedThrottle, overrun, vtecKick);
        float limiter = MathHelper.Clamp(
            MathF.Max(
                MathF.Max(vehicle.RevLimiterBounceIntensity, vehicle.EnginePowerUnitFuelCutBlend),
                vehicle.RevLimiterActive ? 0.55f : 0f),
            0f,
            1f);
        float shock = MathHelper.Clamp(
            MathF.Max(
                MathF.Max(vehicle.ShiftKickIntensity, vehicle.PowertrainShockIntensity),
                vtecKick * 0.42f),
            0f,
            1f);
        float clampedTransient = MathHelper.Clamp(throttleTransient, 0f, 1f);
        float backfire = vehicle.EnginePowerUnitActive
            ? MathHelper.Clamp(vehicle.EnginePowerUnitAfterfireBlend, 0f, 1f)
            : MathHelper.Clamp(
                overrun * MathF.Max(
                    MathF.Max(clampedTransient * 0.72f, limiter * 0.78f),
                    shock * 0.24f),
                0f,
                1f);
        float intakeDrive = CalculateIntakeDrive(shapedThrottle, load, vtecBlend, overrun, clampedTransient);
        float drivelineDrive = CalculateDrivelineDrive(vehicle, throttle);
        float gearRatio = ResolveGearRatio(parameters, vehicle.Gear);

        return new EngineAudioFrame(
            MathHelper.Clamp(rpm, 450f, limiterHardCutRpm * 1.12f),
            limiterHardCutRpm,
            throttle,
            shapedThrottle,
            load,
            vtecBlend,
            vtecKick,
            limiter,
            vehicle.RevLimiterActive,
            vehicle.RevLimiterBouncePhase - MathF.Floor(vehicle.RevLimiterBouncePhase),
            overrun,
            shock,
            intakeDrive,
            clampedTransient,
            drivelineDrive,
            MathHelper.Clamp(driveVolume, 0f, 1f),
            pauseScale,
            cameraMode,
            vehicle.Gear,
            gearRatio,
            parameters.RaceAudioFinalDriveRatio,
            vehicle.Gear == 0 ? 0f : 1f,
            vehicle.IsShifting,
            vehicle.SpeedMetersPerSecond,
            vehicle.SpeedMetersPerSecond,
            vehicle.SpeedMetersPerSecond,
            vehicle.EnginePowerUnitEngineDriveTorqueNm,
            vehicle.DriveForce,
            vehicle.EngineBrakeTorqueNm,
            vehicle.ClutchSlipRpm,
            vehicle.EnginePowerUnitClutchTorqueNm,
            vehicle.EnginePowerUnitCrankFrictionTorqueNm,
            vehicle.EnginePowerUnitTransmissionRpm,
            backfire,
            vehicle.EnginePowerUnitCrankPhaseDegrees,
            MathHelper.Clamp(deltaSeconds, 0f, 0.1f));
    }

    private static float ResolveGearRatio(VehicleAudioParameters parameters, int gear)
    {
        if (gear == 0)
        {
            return 0f;
        }

        if (gear < 0)
        {
            return 0f;
        }

        return parameters.RaceAudioGearRatios.Length == 0
            ? 0f
            : parameters.RaceAudioGearRatios[
                Math.Clamp(gear, 1, parameters.RaceAudioGearRatios.Length) - 1];
    }

    private static float CalculateLoad(VehicleState vehicle, float shapedThrottle, float overrun, float vtecKick)
    {
        float load = MathHelper.Clamp(
            0.14f + shapedThrottle * 0.82f + vehicle.ShiftKickIntensity * 0.16f + vtecKick * 0.12f - overrun * 0.10f,
            0f,
            1f);
        if (vehicle.EnginePowerUnitActive && vehicle.EnginePowerUnitLoad > 0f)
        {
            load = MathHelper.Lerp(load, MathHelper.Clamp(vehicle.EnginePowerUnitLoad, 0f, 1f), 0.72f);
        }

        return load;
    }

    private static float CalculateOverrun(float throttle, float rpm, float limiterHardCutRpm, float speedMetersPerSecond)
    {
        return (1f - SmoothStep(0.05f, 0.25f, throttle)) *
               SmoothStep(2600f, MathF.Max(3200f, limiterHardCutRpm), rpm) *
               SmoothStep(2f, 11f, speedMetersPerSecond);
    }

    private static float CalculateIntakeDrive(
        float shapedThrottle,
        float load,
        float vtecBlend,
        float overrun,
        float throttleTransient)
    {
        float loadedThrottle = MathF.Max(shapedThrottle, load * 0.62f);
        float highCamHarden = SmoothStep(0.18f, 0.92f, vtecBlend) * 0.18f;
        float snapHarden = throttleTransient * 0.24f;
        float liftOffMute = MathHelper.Lerp(1f, 0.42f, SmoothStep(0.08f, 0.88f, overrun));
        return MathHelper.Clamp((loadedThrottle + highCamHarden + snapHarden) * liftOffMute, 0f, 1f);
    }

    private static float CalculateDrivelineDrive(VehicleState vehicle, float throttle)
    {
        float clutchSlip = SmoothStep(180f, 2800f, MathF.Abs(vehicle.ClutchSlipRpm));
        float clutchLoad = SmoothStep(20f, 220f, MathF.Abs(vehicle.EnginePowerUnitClutchTorqueNm));
        float shiftShock = MathF.Max(vehicle.ShiftKickIntensity, vehicle.PowertrainShockIntensity);
        float gearSpeed = vehicle.Gear == 0 ? 0f : SmoothStep(8f, 48f, MathF.Abs(vehicle.SpeedMetersPerSecond));
        float throttleGate = SmoothStep(0.08f, 0.56f, throttle);

        return MathHelper.Clamp(
            MathF.Max(shiftShock * 0.70f, clutchSlip * clutchLoad * throttleGate) + gearSpeed * 0.12f,
            0f,
            1f);
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float t = MathHelper.Clamp((value - edge0) / MathF.Max(0.001f, edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
