using RetroRacer.Data;
using RetroRacer.Vehicle;
using Microsoft.Xna.Framework;

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
        Console.WriteLine("  rpm | B16 torque/target/brake/raw/VTEC | B18 torque/target/brake/raw/VTEC");
        float b16MaxTorqueError = 0f;
        float b18MaxTorqueError = 0f;
        float b16MaxBrakeError = 0f;
        float b18MaxBrakeError = 0f;

        foreach (float rpm in new[] { 1500f, 2500f, 3000f, 4500f, 5800f, 6500f, 7000f, 7800f, 8500f })
        {
            EnginePowerUnitState b16State = Sample(b16, rpm);
            EnginePowerUnitState b18State = Sample(b18, rpm);
            float b16TargetTorque = ProfileValue(rpm, b16.Audio.EngineSimulatorProfileTorqueCurveRpm, b16.Audio.EngineSimulatorProfileTorqueCurveNm);
            float b18TargetTorque = ProfileValue(rpm, b18.Audio.EngineSimulatorProfileTorqueCurveRpm, b18.Audio.EngineSimulatorProfileTorqueCurveNm);
            float b16TargetBrake = ProfileValue(rpm, b16.Audio.EngineSimulatorProfileEngineBrakeCurveRpm, b16.Audio.EngineSimulatorProfileEngineBrakeCurveNm);
            float b18TargetBrake = ProfileValue(rpm, b18.Audio.EngineSimulatorProfileEngineBrakeCurveRpm, b18.Audio.EngineSimulatorProfileEngineBrakeCurveNm);
            b16MaxTorqueError = MathF.Max(b16MaxTorqueError, RelativeError(b16State.EngineDriveTorqueNm, b16TargetTorque));
            b18MaxTorqueError = MathF.Max(b18MaxTorqueError, RelativeError(b18State.EngineDriveTorqueNm, b18TargetTorque));
            b16MaxBrakeError = MathF.Max(b16MaxBrakeError, RelativeError(b16State.EngineBrakeTorqueNm, b16TargetBrake));
            b18MaxBrakeError = MathF.Max(b18MaxBrakeError, RelativeError(b18State.EngineBrakeTorqueNm, b18TargetBrake));
            Console.WriteLine(
                $"  {rpm,4:0} | {b16State.EngineDriveTorqueNm,6:0.0}/{b16TargetTorque,6:0.0}/{b16State.EngineBrakeTorqueNm,6:0.0}/{b16State.RawPositiveTorqueNm,6:0.0}/{b16State.VtecBlend:0.00} | " +
                $"{b18State.EngineDriveTorqueNm,6:0.0}/{b18TargetTorque,6:0.0}/{b18State.EngineBrakeTorqueNm,6:0.0}/{b18State.RawPositiveTorqueNm,6:0.0}/{b18State.VtecBlend:0.00}");
        }

        bool transientValid = ValidateTransientSignals(b16);
        bool passed = b16MaxTorqueError <= 0.25f &&
                      b18MaxTorqueError <= 0.25f &&
                      b16MaxBrakeError <= 0.30f &&
                      b18MaxBrakeError <= 0.30f &&
                      transientValid;
        Console.WriteLine($"  validation | {(passed ? "PASS" : "FAIL")} | B16 torque {b16MaxTorqueError:P1}, brake {b16MaxBrakeError:P1} | B18 torque {b18MaxTorqueError:P1}, brake {b18MaxBrakeError:P1} | transients {(transientValid ? "finite" : "invalid")}");
        if (!passed)
        {
            throw new InvalidDataException("Engine Sim dyno validation exceeded profile tolerances.");
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

    private static bool ValidateTransientSignals(VehicleSimulationParameters vehicle)
    {
        EngineSimPowerUnit powerUnit = new(vehicle, ownDriveline: false);
        EnginePowerUnitState limiter = powerUnit.Advance(new EnginePowerUnitRequest(
            6800f, 1f, 22f, 1f, 1f, 0f, 0f, 3, 1f, 0f, vehicle.FinalDriveRatio,
            vehicle.WheelRadiusMeters, 0f, EnginePowerUnitPhase.Driving, 1f, 0f, 0f, 1f / 60f));
        EnginePowerUnitState overrun = powerUnit.Advance(new EnginePowerUnitRequest(
            6200f, 0f, 22f, 0f, 1f, 1f, 0.2f, 3, 1f, 0f, vehicle.FinalDriveRatio,
            vehicle.WheelRadiusMeters, 0f, EnginePowerUnitPhase.Driving, 1f, 0f, 0f, 1f / 60f));
        return IsFinite(limiter) && IsFinite(overrun);
    }

    private static bool IsFinite(EnginePowerUnitState state)
    {
        return float.IsFinite(state.EngineDriveTorqueNm) &&
               float.IsFinite(state.EngineBrakeTorqueNm) &&
               float.IsFinite(state.RawIndicatedTorqueNm) &&
               float.IsFinite(state.RawPositiveTorqueNm) &&
               float.IsFinite(state.RawNegativeTorqueNm) &&
               float.IsFinite(state.AfterfireBlend);
    }

    private static float RelativeError(float value, float target)
    {
        return MathF.Abs(value - target) / MathF.Max(1f, MathF.Abs(target));
    }

    private static float ProfileValue(float rpm, float[] sampleRpm, float[] sampleValue)
    {
        int count = Math.Min(sampleRpm.Length, sampleValue.Length);
        if (count == 0)
        {
            return 0f;
        }

        if (rpm <= sampleRpm[0])
        {
            return sampleValue[0];
        }

        for (int i = 1; i < count; i++)
        {
            if (rpm <= sampleRpm[i])
            {
                float t = MathHelper.Clamp((rpm - sampleRpm[i - 1]) / MathF.Max(1f, sampleRpm[i] - sampleRpm[i - 1]), 0f, 1f);
                return MathHelper.Lerp(sampleValue[i - 1], sampleValue[i], t);
            }
        }

        return sampleValue[count - 1];
    }
}
