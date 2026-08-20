using RetroRacer.Data;
using RetroRacer.Vehicle;

namespace RetroRacer.Core;

public static class EngineSimPowerProbe
{
    private const string VehiclePath = "Data/Vehicles/ek9_reference_2000.json";

    public static void Run()
    {
        VehicleSimulationParameters vehicle = VehicleDefinitionLoader.LoadSimulationParameters(VehiclePath);
        Console.WriteLine("EK9 Engine Sim power probe");
        Console.WriteLine($"  enabled {vehicle.EngineSimulatorDrivesPhysics}, sim {vehicle.EngineSimulatorPhysicsSimulationFrequencyHz:0} Hz, fluid steps {vehicle.EngineSimulatorPhysicsFluidSimulationSteps}, torque scale/blend {vehicle.EngineSimulatorPhysicsTorqueScale:0.000}/{vehicle.EngineSimulatorPhysicsTorqueBlend:0.00}, brake scale/blend {vehicle.EngineSimulatorPhysicsEngineBrakeScale:0.000}/{vehicle.EngineSimulatorPhysicsEngineBrakeBlend:0.00}");
        Console.WriteLine($"  VTEC {vehicle.VtecActivationRpm:0} rpm over {vehicle.VtecTransitionWidthRpm:0} rpm, flow {vehicle.VtecLowCamFlowMultiplier:0.00}->{vehicle.VtecHighCamFlowMultiplier:0.00}");
        Console.WriteLine("  rpm | curve | engine | road | raw net | raw + | raw - | brake | vtec | kick");

        float[] rpmSamples = [1000f, 2000f, 3000f, 4000f, 5000f, 5800f, 6200f, 7000f, 7500f, 8200f];
        foreach (float rpm in rpmSamples)
        {
            EnginePowerUnitState drive = Sample(vehicle, rpm, throttle: 1f);
            EnginePowerUnitState brake = Sample(vehicle, rpm, throttle: 0f);
            Console.WriteLine(
                $"  {rpm,4:0} | {vehicle.TorqueAtRpm(rpm),5:0.0} | {drive.EngineDriveTorqueNm,6:0.0} | {drive.DriveTorqueNm,5:0.0} | {drive.RawIndicatedTorqueNm,7:0.0} | {drive.RawPositiveTorqueNm,5:0.0} | {drive.RawNegativeTorqueNm,5:0.0} | {brake.EngineBrakeTorqueNm,5:0.0} | {drive.VtecBlend:0.00} | {drive.VtecKickIntensity:0.00}");
        }

        EngineSimPowerTransitionSample transition = SampleVtecTransition(vehicle);
        Console.WriteLine($"  VTEC transition 5400->6400 rpm: peak kick {transition.PeakKick:0.00}, peak engine {transition.PeakEngineTorqueNm:0.0} Nm, peak road {transition.PeakRoadTorqueNm:0.0} Nm");
    }

    private static EnginePowerUnitState Sample(
        VehicleSimulationParameters vehicle,
        float rpm,
        float throttle)
    {
        EngineSimPowerUnit powerUnit = new(vehicle);
        EnginePowerUnitState state = EnginePowerUnitState.Disabled;
        EnginePowerStateAccumulator accumulator = new();
        const float dt = 1f / 60f;
        int gear = vehicle.ForwardGearRatios.Length >= 3 ? 3 : 1;
        float gearRatio = vehicle.ForwardGearRatios.Length > 0
            ? vehicle.ForwardGearRatios[Math.Clamp(gear - 1, 0, vehicle.ForwardGearRatios.Length - 1)]
            : 0f;
        float speedMetersPerSecond = CalculateRoadSpeedForTransmissionRpm(rpm, gearRatio, vehicle);
        float transmissionRpm = rpm;
        for (int i = 0; i < 240; i++)
        {
            state = powerUnit.Advance(new EnginePowerUnitRequest(
                rpm,
                throttle,
                speedMetersPerSecond,
                0f,
                1f,
                throttle <= 0.01f ? 1f : 0f,
                0f,
                gear,
                gearRatio,
                transmissionRpm,
                vehicle.FinalDriveRatio,
                vehicle.WheelRadiusMeters,
                1f,
                throttle <= 0.01f ? EnginePowerUnitPhase.EngineBraking : EnginePowerUnitPhase.Driving,
                1f,
                0f,
                rpm - transmissionRpm,
                dt));
            if (i >= 120)
            {
                accumulator.Add(state);
            }
        }

        return accumulator.Count > 0 ? accumulator.Average() : state;
    }

    private struct EnginePowerStateAccumulator
    {
        private float _driveTorqueNm;
        private float _engineBrakeTorqueNm;
        private float _engineDriveTorqueNm;
        private float _rawIndicatedTorqueNm;
        private float _rawPositiveTorqueNm;
        private float _rawNegativeTorqueNm;
        private float _vtecBlend;
        private float _vtecKickIntensity;
        private float _load;
        private float _crankRpm;
        private float _transmissionRpm;
        private float _clutchTorqueNm;
        private float _crankFrictionTorqueNm;
        private float _fuelCutBlend;

        public int Count { get; private set; }

        public void Add(EnginePowerUnitState state)
        {
            _driveTorqueNm += state.DriveTorqueNm;
            _engineBrakeTorqueNm += state.EngineBrakeTorqueNm;
            _engineDriveTorqueNm += state.EngineDriveTorqueNm;
            _rawIndicatedTorqueNm += state.RawIndicatedTorqueNm;
            _rawPositiveTorqueNm += state.RawPositiveTorqueNm;
            _rawNegativeTorqueNm += state.RawNegativeTorqueNm;
            _vtecBlend += state.VtecBlend;
            _vtecKickIntensity += state.VtecKickIntensity;
            _load += state.Load;
            _crankRpm += state.CrankRpm;
            _transmissionRpm += state.TransmissionRpm;
            _clutchTorqueNm += state.ClutchTorqueNm;
            _crankFrictionTorqueNm += state.CrankFrictionTorqueNm;
            _fuelCutBlend += state.FuelCutBlend;
            Count++;
        }

        public EnginePowerUnitState Average()
        {
            float inverseCount = 1f / Count;
            return new EnginePowerUnitState(
                true,
                true,
                true,
                _driveTorqueNm * inverseCount,
                _engineBrakeTorqueNm * inverseCount,
                _engineDriveTorqueNm * inverseCount,
                _rawIndicatedTorqueNm * inverseCount,
                _rawPositiveTorqueNm * inverseCount,
                _rawNegativeTorqueNm * inverseCount,
                _vtecBlend * inverseCount,
                _vtecKickIntensity * inverseCount,
                _load * inverseCount,
                _crankRpm * inverseCount,
                _transmissionRpm * inverseCount,
                _clutchTorqueNm * inverseCount,
                _crankFrictionTorqueNm * inverseCount,
                _fuelCutBlend * inverseCount);
        }
    }

    private static EngineSimPowerTransitionSample SampleVtecTransition(VehicleSimulationParameters vehicle)
    {
        EngineSimPowerUnit powerUnit = new(vehicle);
        const float dt = 1f / 60f;
        float peakKick = 0f;
        float peakEngineTorque = 0f;
        float peakRoadTorque = 0f;
        int gear = vehicle.ForwardGearRatios.Length >= 3 ? 3 : 1;
        float gearRatio = vehicle.ForwardGearRatios.Length >= 3
            ? vehicle.ForwardGearRatios[2]
            : vehicle.ForwardGearRatios.Length > 0
                ? vehicle.ForwardGearRatios[0]
                : 0f;
        for (int i = 0; i < 90; i++)
        {
            float t = i / 89f;
            float rpm = 5400f + t * 1000f;
            float speedMetersPerSecond = CalculateRoadSpeedForTransmissionRpm(rpm, gearRatio, vehicle);
            EnginePowerUnitState state = powerUnit.Advance(new EnginePowerUnitRequest(
                rpm,
                1f,
                speedMetersPerSecond,
                0f,
                1f,
                0f,
                0f,
                gear,
                gearRatio,
                rpm,
                vehicle.FinalDriveRatio,
                vehicle.WheelRadiusMeters,
                1f,
                EnginePowerUnitPhase.Driving,
                1f,
                0f,
                0f,
                dt));
            peakKick = MathF.Max(peakKick, state.VtecKickIntensity);
            peakEngineTorque = MathF.Max(peakEngineTorque, state.EngineDriveTorqueNm);
            peakRoadTorque = MathF.Max(peakRoadTorque, state.DriveTorqueNm);
        }

        return new EngineSimPowerTransitionSample(peakKick, peakEngineTorque, peakRoadTorque);
    }

    private readonly record struct EngineSimPowerTransitionSample(
        float PeakKick,
        float PeakEngineTorqueNm,
        float PeakRoadTorqueNm);

    private static float CalculateRoadSpeedForTransmissionRpm(
        float transmissionRpm,
        float gearRatio,
        VehicleSimulationParameters vehicle)
    {
        if (gearRatio <= 0f || vehicle.FinalDriveRatio <= 0f)
        {
            return 0f;
        }

        float wheelRpm = MathF.Abs(transmissionRpm) / (gearRatio * vehicle.FinalDriveRatio);
        return wheelRpm / 60f * MathF.Tau * MathF.Max(0.05f, vehicle.WheelRadiusMeters);
    }
}
