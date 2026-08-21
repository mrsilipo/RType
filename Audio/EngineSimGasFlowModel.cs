using System.Globalization;
using System.Text.RegularExpressions;
using RetroRacer.Vehicle;

namespace RetroRacer.Audio;

internal sealed class EngineSimGasFlowModel
{
    private const double CycleDegrees = 720.0;
    private const double DegreesToRadians = Math.PI / 180.0;
    private const double AtmosphericPressure = 101325.0;
    private const double AmbientTemperature = 298.15;
    private const double ChamberWallTemperature = 363.15;
    private const double AirMolecularMass = 0.02897;
    private const double GasConstant = 8.31446261815324;
    private const double Inch = 0.0254;
    private const double Thou = Inch / 1000.0;
    private const double Cm2 = 0.0001;
    private const double Cc = 0.000001;
    private const double Liter = 0.001;
    private const double Psi = 6894.754789999999;
    private const double SpeedOfSoundMetersPerSecond = 343.0;
    private const double MinimumGasMoles = 1.0e-12;
    private const double MinimumGasVolume = 1.0e-9;
    private const double MaximumGasPressure = 20_000_000.0;
    private const double MaximumGasTemperature = 6000.0;
    private const int StateSamples = 720;

    private readonly VehicleAudioParameters _parameters;
    private readonly EngineSimGasFlowProfile _profile;
    private readonly int _sampleRate;
    private readonly int _fluidSimulationSteps;
    private readonly double _dt;
    private readonly double _boreMeters;
    private readonly double _strokeMeters;
    private readonly double _rodLengthMeters;
    private readonly double _crankRadiusMeters;
    private readonly double _boreAreaMeters2;
    private readonly Cylinder[] _cylinders;
    private readonly ExhaustCollector[] _exhausts;
    private readonly double[] _ignitionTimingRpm;
    private readonly double[] _ignitionTimingDegrees;
    private readonly GasSystem _intakeSystem = new();
    private readonly GasSystem _intakeAtmosphere = new();
    private readonly GasSystem _exhaustAtmosphere = new();
    private readonly SampledFunction _meanPistonSpeedToTurbulence = new(1.0);
    private readonly FuelModel _fuel;
    private readonly GasMix _intakeFuelAirMix;
    private readonly GasMix _intakeIdleFuelMix;
    private double _crankDegrees;
    private double _fuelCutPhase;
    private double _fuelCutBlend;
    private bool _cutIgnition;
    private double _lastIndicatedTorqueNm;
    private double _lastPositiveTorqueNm;
    private double _lastNegativeTorqueNm;
    private double _lastAfterfireBlend;
    private double _peakChamberPressurePa;
    private double _averageExhaustPressurePa;
    private double _averageIntakePressurePa;
    private double _afterfireEnergyJ;
    private uint _noiseState = 0x7654c321u;

    public EngineSimGasFlowModel(VehicleAudioParameters parameters, int sampleRate, int? fluidSimulationStepsOverride = null)
    {
        _parameters = parameters;
        _sampleRate = Math.Max(1, sampleRate);
        _fluidSimulationSteps = Math.Clamp(fluidSimulationStepsOverride ?? parameters.EngineSimulatorFluidSimulationSteps, 1, 16);
        _dt = 1.0 / _sampleRate;
        _profile = EngineSimGasFlowProfile.Load(parameters);
        _boreMeters = Math.Max(0.001, parameters.EngineSimulatorBoreMillimeters / 1000.0);
        _strokeMeters = Math.Max(0.001, parameters.EngineSimulatorStrokeMillimeters / 1000.0);
        _rodLengthMeters = Math.Max(0.001, parameters.EngineSimulatorRodLengthMillimeters / 1000.0);
        _crankRadiusMeters = _strokeMeters * 0.5;
        _boreAreaMeters2 = Math.PI * Math.Pow(_boreMeters * 0.5, 2.0);
        _fuel = new FuelModel(parameters.EngineSimulatorFuelBurningEfficiency, parameters.EngineSimulatorFuelTurbulence);
        double idealAfr = 0.8 * _fuel.MolecularAfr * 4.0;
        double pAir = idealAfr / (1.0 + idealAfr);
        _intakeFuelAirMix = new GasMix(1.0 - pAir, pAir * 0.75, pAir * 0.25);
        double idleAfr = 2.0;
        double pIdleAir = idleAfr / (1.0 + idleAfr);
        _intakeIdleFuelMix = new GasMix(1.0 - pIdleAir, pIdleAir * 0.75, pIdleAir * 0.25);
        _ignitionTimingRpm = parameters.EngineSimulatorIgnitionTimingRpm.Length > 0
            ? [.. parameters.EngineSimulatorIgnitionTimingRpm.Select(value => (double)value)]
            : [0.0, 1000.0, 2000.0, 3000.0, 4000.0];
        _ignitionTimingDegrees = parameters.EngineSimulatorIgnitionTimingDegrees.Length > 0
            ? [.. parameters.EngineSimulatorIgnitionTimingDegrees.Select(value => (double)value)]
            : [-25.0, -25.0, -30.0, -30.0, -30.0];

        for (int i = 0; i < 30; i++)
        {
            _meanPistonSpeedToTurbulence.AddSample(i, i * 0.5);
        }

        int cylinderCount = Math.Clamp(parameters.EngineSimulatorCylinderCount, 1, 16);
        int exhaustCount = Math.Max(
            1,
            Math.Max(
                parameters.EngineSimulatorExhaustVolumes.Length,
                parameters.EngineSimulatorCylinderExhaust.Length == 0 ? 1 : parameters.EngineSimulatorCylinderExhaust.Max() + 1));

        _exhausts = new ExhaustCollector[exhaustCount];
        float[] exhaustVolumes = parameters.EngineSimulatorExhaustVolumes.Length > 0
            ? parameters.EngineSimulatorExhaustVolumes
            : [1f];
        for (int i = 0; i < _exhausts.Length; i++)
        {
            double audioVolume = ReadCyclic(exhaustVolumes, i, 1f);
            _exhausts[i] = new ExhaustCollector(_profile, Math.Max(0.001, audioVolume));
        }

        _cylinders = new Cylinder[cylinderCount];
        double[] powerTdcByCylinder = BuildPowerTdcByCylinder(parameters, cylinderCount);
        for (int i = 0; i < _cylinders.Length; i++)
        {
            int exhaustIndex = Math.Clamp(ReadCyclic(parameters.EngineSimulatorCylinderExhaust, i, 0), 0, _exhausts.Length - 1);
            double attenuation = Clamp(ReadCyclic(parameters.EngineSimulatorCylinderAttenuation, i, 1f), 0.2, 1.8);
            double blowbyK = ReadCyclic(_profile.BlowbyK, i, GasSystem.K28InH2O(0.001));
            _cylinders[i] = new Cylinder(
                i,
                powerTdcByCylinder[i],
                attenuation,
                exhaustIndex,
                blowbyK,
                _profile,
                _exhausts[exhaustIndex],
                _sampleRate);
            _cylinders[i].IntakeValve = BuildValveProfile(powerTdcByCylinder[i], intake: true);
            _cylinders[i].ExhaustValve = BuildValveProfile(powerTdcByCylinder[i], intake: false);
        }

        Reset();
    }

    public int CylinderCount => _cylinders.Length;

    public int ExhaustChannelCount => _exhausts.Length;

    public int AudioChannelCount => _exhausts.Length + 1;

    public string EventRouteSummary => string.Join("/", OrderedByFiring().Select(cylinder => cylinder.ExhaustIndex));

    public string EventAttenuationSummary => string.Join("/", OrderedByFiring().Select(cylinder => cylinder.SoundAttenuation.ToString("0.00", CultureInfo.InvariantCulture)));

    public string ExhaustGainSummary => string.Join("/", _exhausts.Select(exhaust => exhaust.AudioVolume.ToString("0.00", CultureInfo.InvariantCulture)));

    public string CamSummary => $"{_parameters.EngineSimulatorLowIntakeDurationDegrees:0}/{_parameters.EngineSimulatorLowIntakeLiftMillimeters:0.0}->{_parameters.EngineSimulatorVtecIntakeDurationDegrees:0}/{_parameters.EngineSimulatorVtecIntakeLiftMillimeters:0.0}";

    public string FlowSummary => $"chamber {_profile.ChamberVolume / Cc:0.0}cc, intake {_profile.IntakeRunnerVolume / Cc:0.0}cc/{_profile.IntakeRunnerCrossSectionArea / (Inch * Inch):0.00}in2, exhaust {_profile.ExhaustRunnerVolume / Cc:0.0}cc/{_profile.ExhaustRunnerCrossSectionArea / (Inch * Inch):0.00}in2";

    public int FluidSimulationSteps => _fluidSimulationSteps;

    public float CrankPhaseDegrees => (float)_crankDegrees;

    public void SynchronizeCrankPhase(float phaseDegrees)
    {
        _crankDegrees = WrapCycleDegrees(phaseDegrees);
    }

    public EngineSimGasFlowPowerState LastPowerState => new(
        (float)_lastIndicatedTorqueNm,
        (float)_lastPositiveTorqueNm,
        (float)_lastNegativeTorqueNm,
        (float)_fuelCutBlend,
        CrankPhaseDegrees,
        (float)_lastAfterfireBlend);

    public EngineSimGasFlowDiagnostics LastDiagnostics => new(
        (float)_peakChamberPressurePa,
        (float)_averageExhaustPressurePa,
        (float)_averageIntakePressurePa,
        (float)_afterfireEnergyJ);

    public void Step(float rpm, float throttle, float load, float vtecBlend, float limiter, float overrun, float shock, Span<float> output)
    {
        output.Clear();

        double clampedRpm = Math.Max(450.0, rpm);
        double clampedThrottle = Clamp(throttle, 0.0, 1.0);
        double clampedLoad = Clamp(load, 0.0, 1.0);
        double clampedVtec = Clamp(vtecBlend, 0.0, 1.0);
        double clampedLimiter = Clamp(limiter, 0.0, 1.0);
        double clampedOverrun = Clamp(overrun, 0.0, 1.0);
        double clampedShock = Clamp(shock, 0.0, 1.0);
        double pressureScale = Math.Max(0.0, _parameters.EngineSimulatorDspPressureScale);

        UpdateFuelCut(clampedLimiter);
        for (int i = 0; i < _cylinders.Length; i++)
        {
            _cylinders[i].ResetTimestepFlow();
        }

        double crankDeltaDegrees = clampedRpm * 6.0 / _sampleRate;
        double subDt = _dt / _fluidSimulationSteps;
        double subCrankDeltaDegrees = crankDeltaDegrees / _fluidSimulationSteps;
        double effectiveThrottle = CalculateEffectiveEngineSimThrottle(clampedThrottle, clampedLoad, clampedOverrun, clampedShock);
        double torqueSum = 0.0;
        double positiveTorqueSum = 0.0;
        double negativeTorqueSum = 0.0;
        double afterfireSum = 0.0;
        double exhaustPressureSum = 0.0;
        double intakePressureSum = 0.0;
        _peakChamberPressurePa = 0.0;
        _afterfireEnergyJ = 0.0;

        for (int i = 0; i < _fluidSimulationSteps; i++)
        {
            double previousDegrees = _crankDegrees;
            _crankDegrees = WrapCycleDegrees(_crankDegrees + subCrankDeltaDegrees);

            UpdateCylinderVolumesAndValveFlow(clampedRpm, clampedVtec);
            IgniteCrossedCylinders(previousDegrees, _crankDegrees, subCrankDeltaDegrees, clampedRpm, clampedLimiter, clampedOverrun, clampedShock);
            ProcessExhaustCollectors(subDt);
            ProcessIntake(subDt, effectiveThrottle);
            ProcessCylinderFlow(subDt);
            afterfireSum += ProcessAfterfire(subDt, clampedLimiter, clampedOverrun, clampedShock);
            for (int cylinderIndex = 0; cylinderIndex < _cylinders.Length; cylinderIndex++)
            {
                Cylinder cylinder = _cylinders[cylinderIndex];
                _peakChamberPressurePa = Math.Max(_peakChamberPressurePa, cylinder.Chamber.Pressure);
                exhaustPressureSum += cylinder.ExhaustRunner.Pressure;
                intakePressureSum += cylinder.IntakeRunner.Pressure;
            }
            GasTorqueSample gasTorque = CalculateGasTorque();
            torqueSum += gasTorque.NetTorqueNm;
            positiveTorqueSum += gasTorque.PositiveTorqueNm;
            negativeTorqueSum += gasTorque.NegativeTorqueNm;
        }

        double inverseSteps = 1.0 / _fluidSimulationSteps;
        _lastIndicatedTorqueNm = torqueSum * inverseSteps;
        _lastPositiveTorqueNm = positiveTorqueSum * inverseSteps;
        _lastNegativeTorqueNm = negativeTorqueSum * inverseSteps;
        _lastAfterfireBlend = Clamp(afterfireSum * inverseSteps, 0.0, 1.0);
        _averageExhaustPressurePa = exhaustPressureSum * inverseSteps / _cylinders.Length;
        _averageIntakePressurePa = intakePressureSum * inverseSteps / _cylinders.Length;
        WriteSynthesizerInput(clampedRpm, clampedLoad, clampedLimiter, clampedOverrun, clampedShock, pressureScale, output);
    }

    public void Reset()
    {
        _crankDegrees = 0.0;
        _fuelCutPhase = 0.0;
        _fuelCutBlend = 0.0;
        _cutIgnition = false;
        _noiseState = 0x7654c321u;
        _lastAfterfireBlend = 0.0;
        _peakChamberPressurePa = AtmosphericPressure;
        _averageExhaustPressurePa = AtmosphericPressure;
        _averageIntakePressurePa = AtmosphericPressure;
        _afterfireEnergyJ = 0.0;

        _intakeSystem.Initialize(AtmosphericPressure, _profile.IntakePlenumVolume, AmbientTemperature);
        _intakeSystem.SetGeometry(
            Math.Sqrt(_profile.IntakePlenumCrossSectionArea),
            _profile.IntakePlenumVolume / Math.Max(1.0e-8, _profile.IntakePlenumCrossSectionArea),
            1.0,
            0.0);

        _intakeAtmosphere.Initialize(AtmosphericPressure, 1000.0, AmbientTemperature);
        _intakeAtmosphere.SetGeometry(100.0, 100.0, 1.0, 0.0);

        _exhaustAtmosphere.Initialize(AtmosphericPressure, 1000.0, AmbientTemperature);
        _exhaustAtmosphere.SetGeometry(10.0, 10.0, 1.0, 0.0);

        for (int i = 0; i < _exhausts.Length; i++)
        {
            _exhausts[i].Reset();
        }

        for (int i = 0; i < _cylinders.Length; i++)
        {
            InitializeCylinder(_cylinders[i]);
        }
    }

    private GasTorqueSample CalculateGasTorque()
    {
        double netTorque = 0.0;
        double positiveTorque = 0.0;
        double negativeTorque = 0.0;
        for (int i = 0; i < _cylinders.Length; i++)
        {
            Cylinder cylinder = _cylinders[i];
            double pressureDifferential = cylinder.Chamber.Pressure - AtmosphericPressure;
            double torque = pressureDifferential * _boreAreaMeters2 * cylinder.CurrentPistonTravelDerivative;
            netTorque += torque;
            if (torque >= 0.0)
            {
                positiveTorque += torque;
            }
            else
            {
                negativeTorque += torque;
            }
        }

        return new GasTorqueSample(netTorque, positiveTorque, negativeTorque);
    }

    private double ProcessAfterfire(double dt, double limiter, double overrun, double shock)
    {
        double drive = Clamp(
            overrun * 0.82 +
            limiter * _fuelCutBlend * 0.72 +
            shock * overrun * 0.18,
            0.0,
            1.0);
        if (drive <= 0.001)
        {
            return 0.0;
        }

        double blend = 0.0;
        for (int i = 0; i < _cylinders.Length; i++)
        {
            GasSystem exhaustRunner = _cylinders[i].ExhaustRunner;
            double availableFuel = exhaustRunner.N * exhaustRunner.Mix.PFuel;
            double availableOxygen = exhaustRunner.N * exhaustRunner.Mix.PO2;
            if (availableFuel <= MinimumGasMoles || availableOxygen <= MinimumGasMoles)
            {
                continue;
            }

            double reactantMoles = Math.Min(
                availableFuel / (2.0 / 27.0),
                availableOxygen / (25.0 / 27.0));
            reactantMoles = Math.Min(reactantMoles, exhaustRunner.N * dt * 18.0 * drive);
            double activeFuel = exhaustRunner.React(reactantMoles, exhaustRunner.Mix);
            if (activeFuel <= 0.0)
            {
                continue;
            }

            double afterfireEnergy = activeFuel * _fuel.EnergyDensity * 0.22;
            exhaustRunner.ChangeEnergy(afterfireEnergy);
            _afterfireEnergyJ += afterfireEnergy;
            double pressurePulse = Clamp(
                (exhaustRunner.Pressure - AtmosphericPressure) / 450000.0,
                0.0,
                1.0);
            blend = Math.Max(blend, Clamp(activeFuel / Math.Max(MinimumGasMoles, availableFuel) * 8.0, 0.0, 1.0) * pressurePulse);
        }

        return blend;
    }

    private void InitializeCylinder(Cylinder cylinder)
    {
        double volume = CalculateCylinderVolume(cylinder.PowerTdcDegrees);
        double intakeRunnerVolume = _profile.IntakeRunnerVolume + _profile.IntakeRunnerCrossSectionArea * _profile.IntakeRunnerLength;
        double intakeRunnerLength = intakeRunnerVolume / Math.Max(1.0e-8, _profile.IntakeRunnerCrossSectionArea);
        double exhaustRunnerVolume = _profile.ExhaustRunnerVolume + _profile.ExhaustRunnerCrossSectionArea * _profile.ExhaustPrimaryTubeLength;
        double exhaustRunnerLength = exhaustRunnerVolume / Math.Max(1.0e-8, _profile.ExhaustRunnerCrossSectionArea);

        cylinder.Chamber.Initialize(AtmosphericPressure, volume, AmbientTemperature);
        cylinder.Chamber.SetGeometry(Math.Sqrt(_boreAreaMeters2), volume / Math.Max(1.0e-8, _boreAreaMeters2), 1.0, 0.0);
        cylinder.IntakeRunner.Initialize(AtmosphericPressure, intakeRunnerVolume, AmbientTemperature);
        cylinder.IntakeRunner.SetGeometry(intakeRunnerLength, Math.Sqrt(_profile.IntakeRunnerCrossSectionArea), 1.0, 0.0);
        cylinder.ExhaustRunner.Initialize(AtmosphericPressure, exhaustRunnerVolume, AmbientTemperature);
        cylinder.ExhaustRunner.SetGeometry(exhaustRunnerLength, Math.Sqrt(_profile.ExhaustRunnerCrossSectionArea), 1.0, 0.0);
        cylinder.Reset(
            volume,
            _profile.ExhaustPrimaryTubeLength + cylinder.Exhaust.Length,
            _sampleRate,
            intakeRunnerLength);
    }

    private void UpdateFuelCut(double limiter)
    {
        double limiterHz = 1.0 / Math.Max(0.015, _parameters.EngineSimulatorLimiterDurationSeconds);
        _fuelCutPhase = Wrap01(_fuelCutPhase + limiterHz * _dt);
        _cutIgnition = limiter > 0.01 && _fuelCutPhase > Lerp(0.58, 0.42, limiter);
        double targetFuelCut = _cutIgnition ? limiter : 0.0;
        double response = _cutIgnition ? 0.16 : 0.07;
        _fuelCutBlend = Lerp(_fuelCutBlend, targetFuelCut, response);
        if (_fuelCutBlend <= 0.0001 && targetFuelCut <= 0.0001)
        {
            _fuelCutBlend = 0.0;
        }
    }

    private double CalculateEffectiveEngineSimThrottle(double throttle, double load, double overrun, double shock)
    {
        double openThrottle = Math.Pow(Clamp(throttle, 0.0, 1.0), Math.Max(0.1, _parameters.EngineSimulatorThrottleGamma));
        openThrottle *= Lerp(0.62, 1.0, load);
        openThrottle += load * 0.08 + shock * 0.04;
        openThrottle *= Lerp(1.0, 0.28, overrun);
        openThrottle = Clamp(openThrottle, 0.0, 1.0);

        return 1.0 - openThrottle;
    }

    private void UpdateCylinderVolumesAndValveFlow(double rpm, double vtecBlend)
    {
        double omega = rpm * 2.0 * Math.PI / 60.0;
        for (int i = 0; i < _cylinders.Length; i++)
        {
            Cylinder cylinder = _cylinders[i];
            double localDegrees = WrapCycleDegrees(_crankDegrees - cylinder.PowerTdcDegrees);
            CalculatePistonKinematicsWrapped(localDegrees, out double volume, out double pistonTravelDerivative);
            cylinder.Chamber.SetVolume(volume);
            cylinder.CurrentVolume = volume;
            cylinder.CurrentPistonTravelDerivative = pistonTravelDerivative;

            double cylinderHeight = volume / Math.Max(1.0e-8, _boreAreaMeters2);
            cylinder.Chamber.SetGeometry(Math.Sqrt(_boreAreaMeters2), cylinderHeight, 1.0, 0.0);

            double pistonSpeed = Math.Abs(pistonTravelDerivative * omega);
            int sampleIndex = (int)((localDegrees / CycleDegrees) * (StateSamples - 1) + 0.5);
            sampleIndex = Math.Clamp(sampleIndex, 0, StateSamples - 1);
            cylinder.RecordPistonSample(sampleIndex, pistonSpeed);

            double intakeLift = CalculateValveLift(
                cylinder.IntakeValve,
                vtecBlend);
            double exhaustLift = CalculateValveLift(
                cylinder.ExhaustValve,
                vtecBlend);
            cylinder.IntakeFlowRate = _profile.IntakePortFlow.SampleTriangle(intakeLift);
            cylinder.ExhaustFlowRate = _profile.ExhaustPortFlow.SampleTriangle(exhaustLift);
        }
    }

    private void IgniteCrossedCylinders(double previousDegrees, double currentDegrees, double deltaDegrees, double rpm, double limiter, double overrun, double shock)
    {
        double ignitionAdvanceDegrees = Math.Abs(EvaluateIgnitionTiming(rpm));
        for (int i = 0; i < _cylinders.Length; i++)
        {
            Cylinder cylinder = _cylinders[i];
            double sparkDegrees = WrapCycleDegrees(cylinder.PowerTdcDegrees - ignitionAdvanceDegrees);
            if (!CrossedAngle(previousDegrees, currentDegrees, sparkDegrees, deltaDegrees))
            {
                continue;
            }

            if (_cutIgnition)
            {
                continue;
            }

            Ignite(cylinder, limiter, overrun, shock);
        }
    }

    private void Ignite(Cylinder cylinder, double limiter, double overrun, double shock)
    {
        if (cylinder.Flame.Lit || cylinder.Chamber.Mix.PFuel <= 1.0e-12)
        {
            return;
        }

        double afr = cylinder.Chamber.Mix.PO2 / Math.Max(1.0e-12, cylinder.Chamber.Mix.PFuel);
        double equivalenceRatio = afr / _fuel.MolecularAfr;
        if (equivalenceRatio < 0.5 || equivalenceRatio > 1.9)
        {
            return;
        }

        double idealInert = cylinder.Chamber.Mix.PO2 / 0.7;
        double dilution = idealInert > 1.0e-12
            ? cylinder.Chamber.Mix.PInert / idealInert - 1.0
            : 0.0;
        double turbulence = _meanPistonSpeedToTurbulence.SampleTriangle(cylinder.CalculateMeanPistonSpeed());
        double mixingFactor = 1.0 - Clamp(turbulence / _fuel.MaxTurbulenceEffect, 0.0, 1.0) *
            Clamp(1.0 - dilution / _fuel.MaxDilutionEffect, 0.0, 1.0);
        double random = NextUnit();
        double randomEfficiency = _fuel.LowEfficiencyAttenuation *
            ((1.0 - _fuel.BurningEfficiencyRandomness) + _fuel.BurningEfficiencyRandomness * random);
        double efficiency = (mixingFactor * randomEfficiency + (1.0 - mixingFactor)) * _fuel.MaxBurningEfficiency;

        efficiency *= Lerp(1.0, 0.72, Clamp(limiter, 0.0, 1.0) * _fuelCutBlend);
        efficiency *= Lerp(1.0, 0.68, Clamp(overrun, 0.0, 1.0));
        efficiency *= Lerp(1.0, 1.12, Clamp(shock, 0.0, 1.0));

        cylinder.Flame = new FlameEvent
        {
            Lit = true,
            LastVolume = Math.Max(1.0e-9, cylinder.CurrentVolume),
            TravelX = 0.0,
            TravelY = 0.0,
            LitN = 0.0,
            TotalN = cylinder.Chamber.N,
            PercentageLit = 0.0,
            GlobalMix = cylinder.Chamber.Mix,
            Efficiency = Clamp(efficiency, 0.0, 1.2),
            FlameSpeed = Math.Max(
                0.01,
                _fuel.FlameSpeed(
                    turbulence,
                    afr,
                    Math.Max(100.0, cylinder.Chamber.Temperature),
                    Math.Max(1000.0, cylinder.Chamber.Pressure),
                    0.0,
                    160.0 * Psi))
        };
    }

    private void ProcessExhaustCollectors(double dt)
    {
        GasMix airMix = new(0.0, 1.0, 0.0);
        _exhaustAtmosphere.Reset(AtmosphericPressure, AmbientTemperature, airMix);

        for (int i = 0; i < _exhausts.Length; i++)
        {
            ExhaustCollector exhaust = _exhausts[i];
            GasSystem.Flow(new GasFlowParameters(
                exhaust.Profile.ExhaustOutletFlowRate,
                dt,
                1.0,
                0.0,
                exhaust.Profile.ExhaustCollectorCrossSectionArea,
                10.0,
                _exhaustAtmosphere,
                exhaust.System));
            exhaust.System.DissipateExcessVelocity();
            exhaust.System.UpdateVelocity(dt, exhaust.Profile.ExhaustVelocityDecay);
        }
    }

    private void ProcessIntake(double dt, double engineSimThrottle)
    {
        double throttlePlate = Clamp(_profile.IntakeIdleThrottlePlatePosition * engineSimThrottle, 0.0, 1.0);
        double flowAttenuation = Math.Cos(throttlePlate * Math.PI / 2.0);
        GasFlowParameters flowParams = new(
            flowAttenuation * _profile.IntakeFlowRate,
            dt,
            0.0,
            -1.0,
            10.0,
            _profile.IntakePlenumCrossSectionArea,
            _intakeAtmosphere,
            _intakeSystem);

        _intakeAtmosphere.Reset(AtmosphericPressure, AmbientTemperature, _intakeFuelAirMix);
        GasSystem.Flow(flowParams);

        _intakeAtmosphere.Reset(AtmosphericPressure, AmbientTemperature, _intakeIdleFuelMix);
        GasSystem.Flow(flowParams with { KFlow = _profile.IntakeIdleFlowRate });

        _intakeSystem.DissipateExcessVelocity();
        _intakeSystem.UpdateVelocity(dt, _profile.IntakeVelocityDecay);
    }

    private void ProcessCylinderFlow(double dt)
    {
        for (int i = 0; i < _cylinders.Length; i++)
        {
            Cylinder cylinder = _cylinders[i];
            double volume = Math.Max(1.0e-9, cylinder.CurrentVolume);
            double cylinderHeight = volume / Math.Max(1.0e-8, _boreAreaMeters2);
            double cylinderSurfaceArea = cylinderHeight * Math.PI * _boreMeters + _boreAreaMeters2 * 2.0;
            double temperatureDelta = ChamberWallTemperature - cylinder.Chamber.Temperature;
            cylinder.Chamber.ChangeEnergy(temperatureDelta * cylinderSurfaceArea * 100.0 * dt);
            cylinder.Chamber.FlowToEnvironment(cylinder.BlowbyK, dt, AtmosphericPressure, AmbientTemperature);

            GasSystem.Flow(new GasFlowParameters(
                _profile.IntakeRunnerFlowRate,
                dt,
                1.0,
                0.0,
                _profile.IntakePlenumCrossSectionArea,
                _profile.IntakeRunnerCrossSectionArea,
                _intakeSystem,
                cylinder.IntakeRunner));

            cylinder.IntakeRunner.DissipateExcessVelocity();

            double intakeFlow = GasSystem.Flow(new GasFlowParameters(
                cylinder.IntakeFlowRate,
                dt,
                1.0,
                0.0,
                _profile.IntakeRunnerCrossSectionArea,
                _boreAreaMeters2,
                cylinder.IntakeRunner,
                cylinder.Chamber));

            cylinder.IntakeRunner.DissipateExcessVelocity();
            cylinder.Chamber.DissipateExcessVelocity();

            double exhaustFlow = GasSystem.Flow(new GasFlowParameters(
                cylinder.ExhaustFlowRate,
                dt,
                1.0,
                0.0,
                _boreAreaMeters2,
                _profile.ExhaustRunnerCrossSectionArea,
                cylinder.Chamber,
                cylinder.ExhaustRunner));

            cylinder.Chamber.DissipateExcessVelocity();
            cylinder.ExhaustRunner.DissipateExcessVelocity();

            GasSystem.Flow(new GasFlowParameters(
                _profile.ExhaustPrimaryFlowRate,
                dt,
                1.0,
                0.0,
                _profile.ExhaustRunnerCrossSectionArea,
                _profile.ExhaustCollectorCrossSectionArea,
                cylinder.ExhaustRunner,
                cylinder.Exhaust.System));

            cylinder.IntakeRunner.UpdateVelocity(dt, _profile.IntakeVelocityDecay);
            cylinder.Chamber.UpdateVelocity(dt, 0.5);
            cylinder.ExhaustRunner.UpdateVelocity(dt, _profile.ExhaustVelocityDecay);

            if (Math.Abs(intakeFlow) > 1.0e-9 && cylinder.Flame.Lit)
            {
                FlameEvent flame = cylinder.Flame;
                flame.Lit = false;
                cylinder.Flame = flame;
            }

            cylinder.LastTimestepExhaustFlow += exhaustFlow;
            cylinder.LastTimestepIntakeFlow += intakeFlow;
            BurnFlame(cylinder, dt);
        }
    }

    private void BurnFlame(Cylinder cylinder, double dt)
    {
        FlameEvent flame = cylinder.Flame;
        if (!flame.Lit)
        {
            return;
        }

        double volume = Math.Max(1.0e-9, cylinder.CurrentVolume);
        double totalTravelX = _boreMeters * 0.5;
        double totalTravelY = volume / Math.Max(1.0e-8, _boreAreaMeters2);
        double expansion = volume / Math.Max(1.0e-9, flame.LastVolume);
        double lastTravelX = flame.TravelX;
        double lastTravelY = flame.TravelY * expansion;

        flame.TravelX = Math.Min(lastTravelX + dt * flame.FlameSpeed, totalTravelX);
        flame.TravelY = Math.Min(lastTravelY + dt * flame.FlameSpeed, totalTravelY);

        if (lastTravelX < flame.TravelX || lastTravelY < flame.TravelY)
        {
            double burnedVolume = flame.TravelX * flame.TravelX * Math.PI * flame.TravelY;
            double previousBurnedVolume = lastTravelX * lastTravelX * Math.PI * lastTravelY;
            double litVolume = Math.Max(0.0, burnedVolume - previousBurnedVolume);
            double n = Math.Min(cylinder.Chamber.N, (litVolume / volume) * cylinder.Chamber.N);
            double fuelBurned = cylinder.Chamber.React(n * flame.Efficiency, flame.GlobalMix);
            double massFuelBurned = fuelBurned * _fuel.MolecularMass;
            cylinder.Chamber.ChangeEnergy(massFuelBurned * _fuel.EnergyDensity);

            flame.LitN += n;
            flame.PercentageLit += litVolume / volume;
        }
        else
        {
            flame.Lit = false;
        }

        flame.LastVolume = volume;
        cylinder.Flame = flame;
    }

    private void WriteSynthesizerInput(double rpm, double load, double limiter, double overrun, double shock, double pressureScale, Span<float> output)
    {
        double attenuation = Math.Min(Math.Abs(rpm * 2.0 * Math.PI / 60.0), 40.0) / 40.0;
        double attenuation3 = attenuation * attenuation * attenuation;
        double drive = Lerp(0.82, 1.12, Clamp(load, 0.0, 1.0));
        drive *= Lerp(1.0, 1.08, Clamp(shock, 0.0, 1.0));
        drive *= Lerp(1.0, 0.82, Clamp(overrun, 0.0, 1.0));
        double limiterValveNoise = Clamp(limiter, 0.0, 1.0) * _fuelCutBlend * 0.04 * (0.35 + NextUnit() * 0.65);

        for (int i = 0; i < _cylinders.Length; i++)
        {
            Cylinder cylinder = _cylinders[i];
            ExhaustCollector exhaust = cylinder.Exhaust;
            double exhaustFlow = attenuation3 * 1600.0 *
                (cylinder.ExhaustRunner.Pressure - AtmosphericPressure +
                 0.1 * cylinder.ExhaustRunner.DynamicPressure(1.0, 0.0) +
                 0.1 * cylinder.ExhaustRunner.DynamicPressure(-1.0, 0.0));
            double delayed = cylinder.Delay.Process(
                exhaustFlow,
                Lerp(
                    0.10,
                    0.24,
                    Clamp(load, 0.0, 1.0)) +
                Clamp(overrun, 0.0, 1.0) * 0.04);
            double exhaustLength = Math.Max(0.01, _profile.ExhaustPrimaryTubeLength + exhaust.Length);
            double staged = cylinder.SoundAttenuation *
                (exhaust.AudioVolume * delayed / _cylinders.Length) *
                (1.0 / (exhaustLength * exhaustLength));
            staged += limiterValveNoise * pressureScale * 220.0 * cylinder.SoundAttenuation;

            int outputIndex = Math.Clamp(cylinder.ExhaustIndex, 0, output.Length - 1);
            output[outputIndex] += (float)(staged * pressureScale * drive);
        }

        int intakeOutputIndex = _exhausts.Length;
        if (intakeOutputIndex < output.Length)
        {
            double delayedRunnerPressure = 0.0;
            for (int i = 0; i < _cylinders.Length; i++)
            {
                Cylinder cylinder = _cylinders[i];
                delayedRunnerPressure += cylinder.IntakeDelay.Process(
                    cylinder.IntakeRunner.Pressure - AtmosphericPressure +
                    0.12 * cylinder.IntakeRunner.DynamicPressure(1.0, 0.0),
                    Lerp(0.08, 0.22, Clamp(load, 0.0, 1.0)));
            }

            double intakePressure = _intakeSystem.Pressure - AtmosphericPressure +
                                    0.18 * _intakeSystem.DynamicPressure(0.0, -1.0) +
                                    delayedRunnerPressure / Math.Max(1, _cylinders.Length) * 0.32;
            double intakeSignal = intakePressure * pressureScale *
                                  Lerp(0.16, 0.48, Clamp(load, 0.0, 1.0));
            output[intakeOutputIndex] += (float)intakeSignal;
        }
    }

    private double CalculateCylinderVolume(double localDegrees)
    {
        CalculatePistonKinematics(localDegrees, out double volume, out _);
        return volume;
    }

    private void CalculatePistonKinematics(double localDegrees, out double volume, out double travelDerivative)
    {
        CalculatePistonKinematicsWrapped(WrapCycleDegrees(localDegrees), out volume, out travelDerivative);
    }

    private void CalculatePistonKinematicsWrapped(double wrappedCycleDegrees, out double volume, out double travelDerivative)
    {
        double theta = (wrappedCycleDegrees >= 360.0 ? wrappedCycleDegrees - 360.0 : wrappedCycleDegrees) * DegreesToRadians;
        double sin = Math.Sin(theta);
        double cos = Math.Cos(theta);
        double crankSquared = _crankRadiusMeters * _crankRadiusMeters;
        double underRoot = Math.Max(1.0e-12, _rodLengthMeters * _rodLengthMeters - crankSquared * sin * sin);
        double root = Math.Sqrt(underRoot);
        double pistonTravel = _crankRadiusMeters * (1.0 - cos) + _rodLengthMeters - root;
        travelDerivative = _crankRadiusMeters * sin + (crankSquared * sin * cos) / root;
        volume = Math.Max(1.0e-9, _profile.ChamberVolume + _boreAreaMeters2 * pistonTravel);
    }

    private ValveProfile BuildValveProfile(double powerTdcDegrees, bool intake)
    {
        return new ValveProfile(
            intake
                ? WrapCycleDegrees(powerTdcDegrees + 360.0 + _parameters.EngineSimulatorLowIntakeCenterDegrees)
                : WrapCycleDegrees(powerTdcDegrees + 360.0 - _parameters.EngineSimulatorLowExhaustCenterDegrees),
            intake
                ? WrapCycleDegrees(powerTdcDegrees + 360.0 + _parameters.EngineSimulatorVtecIntakeCenterDegrees)
                : WrapCycleDegrees(powerTdcDegrees + 360.0 - _parameters.EngineSimulatorVtecExhaustCenterDegrees),
            Math.Max(1.0, intake
                ? _parameters.EngineSimulatorLowIntakeDurationDegrees
                : _parameters.EngineSimulatorLowExhaustDurationDegrees),
            Math.Max(1.0, intake
                ? _parameters.EngineSimulatorVtecIntakeDurationDegrees
                : _parameters.EngineSimulatorVtecExhaustDurationDegrees),
            (intake
                ? _parameters.EngineSimulatorLowIntakeLiftMillimeters
                : _parameters.EngineSimulatorLowExhaustLiftMillimeters) / 1000.0,
            (intake
                ? _parameters.EngineSimulatorVtecIntakeLiftMillimeters
                : _parameters.EngineSimulatorVtecExhaustLiftMillimeters) / 1000.0,
            Math.Max(0.1, _parameters.EngineSimulatorLowCamGamma),
            Math.Max(0.1, _parameters.EngineSimulatorVtecCamGamma));
    }

    private double CalculateValveLift(ValveProfile profile, double vtecBlend)
    {
        double lowOpen = CalculateCamLobeOpen(_crankDegrees, profile.LowCenterDegrees, profile.LowDurationDegrees, profile.LowGamma) * profile.LowLiftMeters;
        double vtecOpen = CalculateCamLobeOpen(_crankDegrees, profile.VtecCenterDegrees, profile.VtecDurationDegrees, profile.VtecGamma) * profile.VtecLiftMeters;
        return Lerp(lowOpen, vtecOpen, Clamp(vtecBlend, 0.0, 1.0));
    }

    private readonly record struct ValveProfile(
        double LowCenterDegrees,
        double VtecCenterDegrees,
        double LowDurationDegrees,
        double VtecDurationDegrees,
        double LowLiftMeters,
        double VtecLiftMeters,
        double LowGamma,
        double VtecGamma);

    private double EvaluateIgnitionTiming(double rpm)
    {
        int count = Math.Min(_ignitionTimingRpm.Length, _ignitionTimingDegrees.Length);
        if (count <= 0)
        {
            return -28.0;
        }

        if (count == 1 || rpm <= _ignitionTimingRpm[0])
        {
            return _ignitionTimingDegrees[0];
        }

        for (int i = 1; i < count; i++)
        {
            double upperRpm = _ignitionTimingRpm[i];
            if (rpm > upperRpm)
            {
                continue;
            }

            double lowerRpm = _ignitionTimingRpm[i - 1];
            double t = Clamp((rpm - lowerRpm) / Math.Max(1.0, upperRpm - lowerRpm), 0.0, 1.0);
            return Lerp(_ignitionTimingDegrees[i - 1], _ignitionTimingDegrees[i], t);
        }

        return _ignitionTimingDegrees[count - 1];
    }

    private IEnumerable<Cylinder> OrderedByFiring()
    {
        return _cylinders.OrderBy(cylinder => cylinder.PowerTdcDegrees);
    }

    private static double[] BuildPowerTdcByCylinder(VehicleAudioParameters parameters, int cylinderCount)
    {
        double[] result = new double[cylinderCount];
        int[] order = parameters.EngineSimulatorFiringOrder.Length > 0
            ? parameters.EngineSimulatorFiringOrder
            : [.. Enumerable.Range(1, cylinderCount)];
        double eventSpacingDegrees = CycleDegrees / cylinderCount;
        for (int eventIndex = 0; eventIndex < cylinderCount; eventIndex++)
        {
            int cylinder = order[eventIndex % order.Length];
            if (cylinder < 1 || cylinder > cylinderCount)
            {
                cylinder = eventIndex + 1;
            }

            result[cylinder - 1] = eventIndex * eventSpacingDegrees;
        }

        return result;
    }

    private static double CalculateCamLobeOpen(double crankDegrees, double centerDegrees, double durationDegrees, double gamma)
    {
        double distance = CycleDistanceDegrees(crankDegrees, centerDegrees);
        double halfDuration = Math.Max(0.5, durationDegrees * 0.5);
        if (distance >= halfDuration)
        {
            return 0.0;
        }

        double phase = distance / halfDuration;
        double harmonic = 0.5 + Math.Cos(phase * Math.PI) * 0.5;
        double clamped = Clamp(harmonic, 0.0, 1.0);
        if (Math.Abs(gamma - 1.0) < 1.0e-6)
        {
            return clamped;
        }

        if (Math.Abs(gamma - 0.5) < 1.0e-6)
        {
            return Math.Sqrt(clamped);
        }

        if (Math.Abs(gamma - 2.0) < 1.0e-6)
        {
            return clamped * clamped;
        }

        return Math.Pow(clamped, Clamp(gamma, 0.1, 3.0));
    }

    private static bool CrossedAngle(double previousDegrees, double currentDegrees, double targetDegrees, double deltaDegrees)
    {
        if (deltaDegrees >= CycleDegrees)
        {
            return true;
        }

        return currentDegrees >= previousDegrees
            ? targetDegrees > previousDegrees && targetDegrees <= currentDegrees
            : targetDegrees > previousDegrees || targetDegrees <= currentDegrees;
    }

    private static double CycleDistanceDegrees(double a, double b)
    {
        double delta = Math.Abs(WrapCycleDegrees(a - b));
        return Math.Min(delta, CycleDegrees - delta);
    }

    private static double WrapCycleDegrees(double value)
    {
        return value - CycleDegrees * Math.Floor(value / CycleDegrees);
    }

    private static double Wrap01(double value)
    {
        return value - Math.Floor(value);
    }

    private static double ReadCyclic(float[] values, int index, float fallback)
    {
        return values.Length == 0 ? fallback : values[Math.Abs(index) % values.Length];
    }

    private static int ReadCyclic(int[] values, int index, int fallback)
    {
        return values.Length == 0 ? fallback : values[Math.Abs(index) % values.Length];
    }

    private static double ReadCyclic(double[] values, int index, double fallback)
    {
        return values.Length == 0 ? fallback : values[Math.Abs(index) % values.Length];
    }

    private static double Lerp(double a, double b, double t)
    {
        return a + (b - a) * Clamp(t, 0.0, 1.0);
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Min(max, Math.Max(min, value));
    }

    private static double ClampFinite(double value, double fallback, double min, double max)
    {
        return double.IsFinite(value)
            ? Clamp(value, min, max)
            : Clamp(fallback, min, max);
    }

    private double NextUnit()
    {
        _noiseState = _noiseState * 1664525u + 1013904223u;
        return (_noiseState >> 8) / 16777215.0;
    }

    private readonly record struct GasTorqueSample(
        double NetTorqueNm,
        double PositiveTorqueNm,
        double NegativeTorqueNm);

    private sealed class Cylinder
    {
        public Cylinder(
            int index,
            double powerTdcDegrees,
            double soundAttenuation,
            int exhaustIndex,
            double blowbyK,
            EngineSimGasFlowProfile profile,
            ExhaustCollector exhaust,
            int sampleRate)
        {
            Index = index;
            PowerTdcDegrees = powerTdcDegrees;
            SoundAttenuation = soundAttenuation;
            ExhaustIndex = exhaustIndex;
            BlowbyK = blowbyK;
            Exhaust = exhaust;
            Delay = new PressureWaveGuide(Math.Max(0.01, profile.ExhaustPrimaryTubeLength + exhaust.Length) / SpeedOfSoundMetersPerSecond, sampleRate);
        }

        public int Index { get; }

        public double PowerTdcDegrees { get; }

        public double SoundAttenuation { get; }

        public int ExhaustIndex { get; }

        public double BlowbyK { get; }

        public ExhaustCollector Exhaust { get; }

        public GasSystem Chamber { get; } = new();

        public GasSystem IntakeRunner { get; } = new();

        public GasSystem ExhaustRunner { get; } = new();

        public PressureWaveGuide Delay { get; private set; } = new(0.0, 1);

        public PressureWaveGuide IntakeDelay { get; private set; } = new(0.0, 1);

        public double CurrentVolume { get; set; }

        public double CurrentPistonTravelDerivative { get; set; }

        public double IntakeFlowRate { get; set; }

        public double ExhaustFlowRate { get; set; }

        public double LastTimestepExhaustFlow { get; set; }

        public double LastTimestepIntakeFlow { get; set; }

        public double[] PistonSpeedHistory { get; } = new double[StateSamples];

        public ValveProfile IntakeValve { get; set; }

        public ValveProfile ExhaustValve { get; set; }

        public FlameEvent Flame { get; set; }

        private double PistonSpeedSum { get; set; }

        public void Reset(double volume, double delayLength, int sampleRate, double intakeDelayLength = 0.0)
        {
            CurrentVolume = volume;
            CurrentPistonTravelDerivative = 0.0;
            IntakeFlowRate = 0.0;
            ExhaustFlowRate = 0.0;
            LastTimestepExhaustFlow = 0.0;
            LastTimestepIntakeFlow = 0.0;
            Array.Clear(PistonSpeedHistory);
            PistonSpeedSum = 0.0;
            Flame = default;
            Delay = new PressureWaveGuide(delayLength / SpeedOfSoundMetersPerSecond, sampleRate);
            IntakeDelay = new PressureWaveGuide(intakeDelayLength / SpeedOfSoundMetersPerSecond, sampleRate);
        }

        public void ResetTimestepFlow()
        {
            LastTimestepExhaustFlow = 0.0;
            LastTimestepIntakeFlow = 0.0;
        }

        public void RecordPistonSample(int sampleIndex, double pistonSpeed)
        {
            sampleIndex = Math.Clamp(sampleIndex, 0, StateSamples - 1);
            double oldPistonSpeed = PistonSpeedHistory[sampleIndex];
            PistonSpeedHistory[sampleIndex] = pistonSpeed;
            PistonSpeedSum += pistonSpeed - oldPistonSpeed;
        }

        public double CalculateMeanPistonSpeed()
        {
            return PistonSpeedSum / PistonSpeedHistory.Length;
        }

    }

    private sealed class ExhaustCollector
    {
        public ExhaustCollector(EngineSimGasFlowProfile profile, double audioVolume)
        {
            Profile = profile;
            AudioVolume = audioVolume;
            Length = profile.ExhaustCollectorLength;
            Reset();
        }

        public EngineSimGasFlowProfile Profile { get; }

        public double AudioVolume { get; }

        public double Length { get; }

        public GasSystem System { get; } = new();

        public void Reset()
        {
            double volume = Profile.ExhaustCollectorCrossSectionArea * Math.Max(0.01, Profile.ExhaustCollectorLength);
            System.Initialize(AtmosphericPressure, volume, AmbientTemperature);
            System.SetGeometry(Profile.ExhaustCollectorLength, Math.Sqrt(Profile.ExhaustCollectorCrossSectionArea), 1.0, 0.0);
        }
    }

    private struct FlameEvent
    {
        public bool Lit { get; set; }

        public double LastVolume { get; set; }

        public double TravelX { get; set; }

        public double TravelY { get; set; }

        public double LitN { get; set; }

        public double TotalN { get; set; }

        public double PercentageLit { get; set; }

        public GasMix GlobalMix { get; set; }

        public double Efficiency { get; set; }

        public double FlameSpeed { get; set; }
    }

    private readonly record struct GasFlowParameters(
        double KFlow,
        double Dt,
        double DirectionX,
        double DirectionY,
        double CrossSectionArea0,
        double CrossSectionArea1,
        GasSystem System0,
        GasSystem System1);

    private readonly record struct GasMix(double PFuel, double PInert, double PO2);

    private sealed class GasSystem
    {
        private double _nMol;
        private double _kineticEnergy;
        private double _volume;
        private double _momentumX;
        private double _momentumY;
        private GasMix _mix = new(0.0, 1.0, 0.0);
        private int _degreesOfFreedom = 5;
        private double _chokedFlowLimit;
        private double _chokedFlowRateCached;
        private double _width;
        private double _height;
        private double _dx;
        private double _dy;

        public double N => _nMol;

        public double Volume => _volume;

        public GasMix Mix => _mix;

        public double Pressure
        {
            get
            {
                if (_volume <= MinimumGasVolume || !double.IsFinite(_kineticEnergy))
                {
                    return AtmosphericPressure;
                }

                double pressure = _kineticEnergy / (0.5 * _degreesOfFreedom * _volume);
                return ClampFinite(pressure, AtmosphericPressure, 0.0, MaximumGasPressure);
            }
        }

        public double Temperature
        {
            get
            {
                if (_nMol <= MinimumGasMoles || !double.IsFinite(_kineticEnergy))
                {
                    return AmbientTemperature;
                }

                double temperature = _kineticEnergy / (0.5 * _degreesOfFreedom * _nMol * GasConstant);
                return ClampFinite(temperature, AmbientTemperature, 1.0, MaximumGasTemperature);
            }
        }

        public double Mass => AirMolecularMass * _nMol;

        public double HeatCapacityRatio => 1.0 + 2.0 / _degreesOfFreedom;

        private double FuelMoles => _mix.PFuel * _nMol;

        private double InertMoles => _mix.PInert * _nMol;

        private double OxygenMoles => _mix.PO2 * _nMol;

        public void SetGeometry(double width, double height, double dx, double dy)
        {
            _width = Math.Max(1.0e-8, width);
            _height = Math.Max(1.0e-8, height);
            _dx = dx;
            _dy = dy;
        }

        public void Initialize(double pressure, double volume, double temperature, GasMix mix = default, int degreesOfFreedom = 5)
        {
            _degreesOfFreedom = degreesOfFreedom;
            _volume = Math.Max(MinimumGasVolume, volume);
            _nMol = Math.Max(MinimumGasMoles, pressure * _volume / (GasConstant * Math.Max(1.0, temperature)));
            _kineticEnergy = temperature * (0.5 * _degreesOfFreedom * _nMol * GasConstant);
            _mix = mix == default ? new GasMix(0.0, 1.0, 0.0) : NormalizeMix(mix);
            _momentumX = 0.0;
            _momentumY = 0.0;
            _chokedFlowLimit = ChokedFlowLimit(_degreesOfFreedom);
            _chokedFlowRateCached = ChokedFlowRate(_degreesOfFreedom);
            Sanitize();
        }

        public void Reset(double pressure, double temperature, GasMix mix = default)
        {
            _nMol = Math.Max(MinimumGasMoles, pressure * _volume / (GasConstant * Math.Max(1.0, temperature)));
            _kineticEnergy = temperature * (0.5 * _degreesOfFreedom * _nMol * GasConstant);
            _mix = mix == default ? new GasMix(0.0, 1.0, 0.0) : NormalizeMix(mix);
            _momentumX = 0.0;
            _momentumY = 0.0;
            Sanitize();
        }

        public void SetVolume(double volume)
        {
            ChangeVolume(volume - _volume);
        }

        public void ChangeVolume(double deltaVolume)
        {
            double nextVolume = Math.Max(MinimumGasVolume, _volume + deltaVolume);
            double actualDelta = nextVolume - _volume;
            double length = Math.Cbrt(Math.Max(1.0e-12, _volume + actualDelta));
            double surfaceArea = Math.Max(1.0e-12, length * length);
            double deltaLength = -actualDelta / surfaceArea;
            double work = deltaLength * Pressure * surfaceArea;
            _volume = nextVolume;
            _kineticEnergy = Math.Max(0.0, _kineticEnergy + work);
            Sanitize();
        }

        public void ChangeEnergy(double deltaEnergy)
        {
            _kineticEnergy = Math.Max(0.0, _kineticEnergy + deltaEnergy);
            Sanitize();
        }

        public double React(double n, GasMix mix)
        {
            double localFuel = mix.PFuel * n;
            double localOxygen = mix.PO2 * n;
            double systemFuel = FuelMoles;
            double systemOxygen = OxygenMoles;
            double systemInert = InertMoles;
            double systemN = _nMol;
            const double idealOxygenRatio = 25.0 / 2.0;
            const double idealFuelRatio = 2.0 / 25.0;
            const double outputInputRatio = (16.0 + 18.0) / (25.0 + 2.0);

            double idealFuelN = idealFuelRatio * localOxygen;
            double idealOxygenN = idealOxygenRatio * localFuel;
            double activeFuel = Math.Min(Math.Min(systemFuel, localFuel), idealFuelN);
            double activeOxygen = Math.Min(Math.Min(systemOxygen, localOxygen), idealOxygenN);
            if (activeFuel <= 0.0 || activeOxygen <= 0.0)
            {
                return 0.0;
            }

            double reactantsN = activeFuel + activeOxygen;
            double productsN = outputInputRatio * reactantsN;
            double dn = productsN - reactantsN;
            _nMol = Math.Max(0.0, _nMol + dn);

            double newSystemFuel = systemFuel - activeFuel;
            double newSystemOxygen = systemOxygen - activeOxygen;
            double newSystemInert = systemInert + productsN;
            double newSystemN = systemN + dn;
            if (newSystemN > 1.0e-12)
            {
                _mix = NormalizeMix(new GasMix(newSystemFuel / newSystemN, newSystemInert / newSystemN, newSystemOxygen / newSystemN));
            }
            else
            {
                _mix = new GasMix(0.0, 0.0, 0.0);
            }

            Sanitize();
            return activeFuel;
        }

        public double FlowToEnvironment(double kFlow, double dt, double environmentPressure, double environmentTemperature, GasMix mix = default)
        {
            double maxFlow = PressureEquilibriumMaxFlow(environmentPressure, environmentTemperature);
            double flow = dt * FlowRate(
                kFlow,
                Pressure,
                environmentPressure,
                Temperature,
                environmentTemperature,
                HeatCapacityRatio,
                _chokedFlowLimit,
                _chokedFlowRateCached);

            if (Math.Abs(flow) > Math.Abs(maxFlow))
            {
                flow = maxFlow;
            }

            if (flow < 0.0)
            {
                double bulkEnergy0 = BulkKineticEnergy();
                GainN(-flow, KineticEnergyPerMol(environmentTemperature, _degreesOfFreedom), mix == default ? new GasMix(0.0, 1.0, 0.0) : mix);
                double bulkEnergy1 = BulkKineticEnergy();
                _kineticEnergy = Math.Max(0.0, _kineticEnergy + bulkEnergy1 - bulkEnergy0);
            }
            else if (_nMol > 1.0e-12)
            {
                double startingN = _nMol;
                LoseN(Math.Min(flow, _nMol), KineticEnergyPerMol());
                _momentumX -= flow / startingN * _momentumX;
                _momentumY -= flow / startingN * _momentumY;
            }

            Sanitize();
            return flow;
        }

        public void DissipateExcessVelocity()
        {
            if (_nMol <= 1.0e-12 || _kineticEnergy <= 0.0)
            {
                return;
            }

            double mass = Mass;
            if (mass <= 1.0e-12)
            {
                return;
            }

            double invMass = 1.0 / mass;
            double velocityX = _momentumX * invMass;
            double velocityY = _momentumY * invMass;
            double velocitySquared = velocityX * velocityX + velocityY * velocityY;
            double density = mass / Math.Max(1.0e-12, _volume);
            double speedOfSoundSquared = Pressure * HeatCapacityRatio / Math.Max(1.0e-12, density);
            if (speedOfSoundSquared >= velocitySquared || velocitySquared <= 0.0)
            {
                return;
            }

            double k = Math.Sqrt(speedOfSoundSquared / velocitySquared);
            _momentumX *= k;
            _momentumY *= k;
            _kineticEnergy = Math.Max(0.0, _kineticEnergy + 0.5 * Mass * (velocitySquared - speedOfSoundSquared));
            Sanitize();
        }

        public void UpdateVelocity(double dt, double beta = 1.0)
        {
            if (_nMol <= 1.0e-12)
            {
                return;
            }

            double depth = _volume / Math.Max(1.0e-12, _width * _height);
            double momentumDeltaX = 0.0;
            double momentumDeltaY = 0.0;
            double p0 = DynamicPressure(_dx, _dy);
            double p1 = DynamicPressure(-_dx, -_dy);
            double p2 = DynamicPressure(_dy, _dx);
            double p3 = DynamicPressure(-_dy, -_dx);
            double pSurfaceArea0 = p0 * (_height * depth);
            double pSurfaceArea1 = p1 * (_height * depth);
            double pSurfaceArea2 = p2 * (_width * depth);
            double pSurfaceArea3 = p3 * (_width * depth);

            momentumDeltaX += pSurfaceArea0 * _dx;
            momentumDeltaY += pSurfaceArea0 * _dy;
            momentumDeltaX -= pSurfaceArea1 * _dx;
            momentumDeltaY -= pSurfaceArea1 * _dy;
            momentumDeltaX += pSurfaceArea2 * _dy;
            momentumDeltaY += pSurfaceArea2 * _dx;
            momentumDeltaX -= pSurfaceArea3 * _dy;
            momentumDeltaY -= pSurfaceArea3 * _dx;

            double mass = Mass;
            if (mass <= 1.0e-12)
            {
                return;
            }

            double invMass = 1.0 / mass;
            double velocity0X = _momentumX * invMass;
            double velocity0Y = _momentumY * invMass;
            _momentumX -= momentumDeltaX * dt * beta;
            _momentumY -= momentumDeltaY * dt * beta;
            double velocity1X = _momentumX * invMass;
            double velocity1Y = _momentumY * invMass;
            _kineticEnergy -= 0.5 * mass * (velocity1X * velocity1X - velocity0X * velocity0X);
            _kineticEnergy -= 0.5 * mass * (velocity1Y * velocity1Y - velocity0Y * velocity0Y);
            _kineticEnergy = Math.Max(0.0, _kineticEnergy);
            Sanitize();
        }

        public double DynamicPressure(double dx, double dy)
        {
            if (_nMol <= 1.0e-12 || _kineticEnergy <= 0.0)
            {
                return 0.0;
            }

            double mass = Mass;
            if (mass <= 1.0e-12)
            {
                return 0.0;
            }

            double velocity = (dx * _momentumX + dy * _momentumY) / mass;
            if (velocity <= 0.0)
            {
                return 0.0;
            }

            double hcr = HeatCapacityRatio;
            double staticPressure = Math.Max(0.0, Pressure);
            double density = ApproximateDensity();
            if (density <= 1.0e-12)
            {
                return 0.0;
            }

            double cSquared = staticPressure * hcr / density;
            if (cSquared <= 1.0e-12)
            {
                return 0.0;
            }

            double machSquared = velocity * velocity / cSquared;
            double x = 1.0 + (hcr - 1.0) * 0.5 * machSquared;
            double pressureRatio = _degreesOfFreedom == 5
                ? x * x * x * Math.Sqrt(Math.Max(0.0, x))
                : _degreesOfFreedom == 3
                    ? x * x * Math.Sqrt(Math.Max(0.0, x))
                    : Math.Sqrt(Math.Max(0.0, x));
            return staticPressure * (pressureRatio - 1.0);
        }

        public static double Flow(GasFlowParameters parameters)
        {
            if (parameters.KFlow <= 0.0)
            {
                return 0.0;
            }

            GasSystem system0 = parameters.System0;
            GasSystem system1 = parameters.System1;
            double p0 = system0.Pressure + system0.DynamicPressure(parameters.DirectionX, parameters.DirectionY);
            double p1 = system1.Pressure + system1.DynamicPressure(-parameters.DirectionX, -parameters.DirectionY);

            GasSystem source;
            GasSystem sink;
            double sourcePressure;
            double sinkPressure;
            double sourceCrossSection;
            double sinkCrossSection;
            double dx;
            double dy;
            double direction;

            if (p0 > p1)
            {
                dx = parameters.DirectionX;
                dy = parameters.DirectionY;
                source = system0;
                sink = system1;
                sourcePressure = p0;
                sinkPressure = p1;
                sourceCrossSection = parameters.CrossSectionArea0;
                sinkCrossSection = parameters.CrossSectionArea1;
                direction = 1.0;
            }
            else
            {
                dx = -parameters.DirectionX;
                dy = -parameters.DirectionY;
                source = system1;
                sink = system0;
                sourcePressure = p1;
                sinkPressure = p0;
                sourceCrossSection = parameters.CrossSectionArea1;
                sinkCrossSection = parameters.CrossSectionArea0;
                direction = -1.0;
            }

            if (source._nMol <= 1.0e-12)
            {
                return 0.0;
            }

            double flow = parameters.Dt * FlowRate(
                parameters.KFlow,
                sourcePressure,
                sinkPressure,
                source.Temperature,
                sink.Temperature,
                source.HeatCapacityRatio,
                source._chokedFlowLimit,
                source._chokedFlowRateCached);
            flow = Clamp(flow, 0.0, 0.9 * source._nMol);

            if (flow <= 0.0)
            {
                return 0.0;
            }

            double fraction = flow / source._nMol;
            double fractionVolume = fraction * source._volume;
            double fractionMass = fraction * source.Mass;
            double sourceBulkEnergy0 = source.BulkKineticEnergy();
            double sinkBulkEnergy0 = sink.BulkKineticEnergy();
            double energyPerMol = source.KineticEnergyPerMol();
            sink.GainN(flow, energyPerMol, source._mix);
            source.LoseN(flow, energyPerMol);

            double momentumDeltaX = source._momentumX * fraction;
            double momentumDeltaY = source._momentumY * fraction;
            source._momentumX -= momentumDeltaX;
            source._momentumY -= momentumDeltaY;
            sink._momentumX += momentumDeltaX;
            sink._momentumY += momentumDeltaY;

            double sourceBulkEnergy1 = source.BulkKineticEnergy();
            double sinkBulkEnergy1 = sink.BulkKineticEnergy();
            sink._kineticEnergy = Math.Max(0.0, sink._kineticEnergy - ((sourceBulkEnergy1 + sinkBulkEnergy1) - (sourceBulkEnergy0 + sinkBulkEnergy0)));

            double sourceMass = source.Mass;
            double sinkMass = sink.Mass;
            double sourceInitialMomentumX = source._momentumX;
            double sourceInitialMomentumY = source._momentumY;
            double sinkInitialMomentumX = sink._momentumX;
            double sinkInitialMomentumY = sink._momentumY;

            if (sinkCrossSection > 0.0)
            {
                double sinkFractionVelocity = Clamp((fractionVolume / sinkCrossSection) / Math.Max(1.0e-9, parameters.Dt), 0.0, sink.C());
                sink._momentumX += sinkFractionVelocity * dx * fractionMass;
                sink._momentumY += sinkFractionVelocity * dy * fractionMass;
            }

            if (sourceCrossSection > 0.0 && sourceMass > 1.0e-12)
            {
                double sourceFractionVelocity = Clamp((fractionVolume / sourceCrossSection) / Math.Max(1.0e-9, parameters.Dt), 0.0, source.C());
                source._momentumX += sourceFractionVelocity * dx * fractionMass;
                source._momentumY += sourceFractionVelocity * dy * fractionMass;
            }

            if (sourceMass > 1.0e-12)
            {
                double invSourceMass = 1.0 / sourceMass;
                double sourceVelocity0X = sourceInitialMomentumX * invSourceMass;
                double sourceVelocity0Y = sourceInitialMomentumY * invSourceMass;
                double sourceVelocity1X = source._momentumX * invSourceMass;
                double sourceVelocity1Y = source._momentumY * invSourceMass;
                source._kineticEnergy -= 0.5 * sourceMass * (sourceVelocity1X * sourceVelocity1X - sourceVelocity0X * sourceVelocity0X);
                source._kineticEnergy -= 0.5 * sourceMass * (sourceVelocity1Y * sourceVelocity1Y - sourceVelocity0Y * sourceVelocity0Y);
            }

            if (sinkMass > 1.0e-12)
            {
                double invSinkMass = 1.0 / sinkMass;
                double sinkVelocity0X = sinkInitialMomentumX * invSinkMass;
                double sinkVelocity0Y = sinkInitialMomentumY * invSinkMass;
                double sinkVelocity1X = sink._momentumX * invSinkMass;
                double sinkVelocity1Y = sink._momentumY * invSinkMass;
                sink._kineticEnergy -= 0.5 * sinkMass * (sinkVelocity1X * sinkVelocity1X - sinkVelocity0X * sinkVelocity0X);
                sink._kineticEnergy -= 0.5 * sinkMass * (sinkVelocity1Y * sinkVelocity1Y - sinkVelocity0Y * sinkVelocity0Y);
            }

            source._kineticEnergy = Math.Max(0.0, source._kineticEnergy);
            sink._kineticEnergy = Math.Max(0.0, sink._kineticEnergy);
            source.Sanitize();
            sink.Sanitize();
            return flow * direction;
        }

        public static double K28InH2O(double flowRateScfm)
        {
            return FlowConstant(
                flowRateScfm * 0.002641 * 453.59237 / 60.0,
                AtmosphericPressure,
                28.0 * 3386.3886666666713 * 0.0734824,
                AmbientTemperature,
                HeatCapacityRatioForDegrees(5));
        }

        public static double KCarb(double flowRateScfm)
        {
            return FlowConstant(
                flowRateScfm * 0.002641 * 453.59237 / 60.0,
                AtmosphericPressure,
                1.5 * 3386.3886666666713,
                AmbientTemperature,
                HeatCapacityRatioForDegrees(5));
        }

        private static double FlowConstant(double targetFlowRate, double pressure, double pressureDrop, double temperature, double hcr)
        {
            double p0 = pressure;
            double pTarget = pressure - pressureDrop;
            double pRatio = pTarget / p0;
            double chokedFlowLimit = Math.Pow(2.0 / (hcr + 1.0), hcr / (hcr - 1.0));
            double flowRate;
            if (pRatio <= chokedFlowLimit)
            {
                flowRate = Math.Sqrt(hcr) * Math.Pow(2.0 / (hcr + 1.0), (hcr + 1.0) / (2.0 * (hcr - 1.0)));
            }
            else
            {
                flowRate = (2.0 * hcr) / (hcr - 1.0);
                flowRate *= 1.0 - Math.Pow(pRatio, (hcr - 1.0) / hcr);
                flowRate = Math.Sqrt(Math.Max(0.0, flowRate));
                flowRate *= Math.Pow(pRatio, 1.0 / hcr);
            }

            flowRate *= p0 / Math.Sqrt(GasConstant * temperature);
            return targetFlowRate / Math.Max(1.0e-12, flowRate);
        }

        private static double FlowRate(double kFlow, double p0, double p1, double t0, double t1, double hcr, double chokedFlowLimit, double chokedFlowRateCached)
        {
            if (kFlow <= 0.0)
            {
                return 0.0;
            }

            double direction;
            double upstreamTemperature;
            double upstreamPressure;
            double targetPressure;
            if (p0 > p1)
            {
                direction = 1.0;
                upstreamTemperature = Math.Max(1.0, t0);
                upstreamPressure = Math.Max(1.0, p0);
                targetPressure = Math.Max(0.0, p1);
            }
            else
            {
                direction = -1.0;
                upstreamTemperature = Math.Max(1.0, t1);
                upstreamPressure = Math.Max(1.0, p1);
                targetPressure = Math.Max(0.0, p0);
            }

            double pRatio = Clamp(targetPressure / upstreamPressure, 0.0, 1.0);
            double flowRate;
            if (pRatio <= chokedFlowLimit)
            {
                flowRate = chokedFlowRateCached / Math.Sqrt(GasConstant * upstreamTemperature);
            }
            else
            {
                double s = Math.Pow(pRatio, 1.0 / hcr);
                flowRate = (2.0 * hcr) / (hcr - 1.0);
                flowRate *= s * (s - pRatio);
                flowRate = Math.Sqrt(Math.Max(flowRate, 0.0) / (GasConstant * upstreamTemperature));
            }

            return flowRate * direction * upstreamPressure * kFlow;
        }

        private double LoseN(double dn, double energyPerMol)
        {
            dn = Math.Min(Math.Max(0.0, dn), _nMol);
            _kineticEnergy = Math.Max(0.0, _kineticEnergy - energyPerMol * dn);
            _nMol = Math.Max(0.0, _nMol - dn);
            return dn;
        }

        private double GainN(double dn, double energyPerMol, GasMix mix)
        {
            dn = Math.Max(0.0, dn);
            double nextN = _nMol + dn;
            double currentN = _nMol;
            _kineticEnergy += dn * energyPerMol;
            _nMol = nextN;
            if (nextN > 1.0e-12)
            {
                _mix = NormalizeMix(new GasMix(
                    (_mix.PFuel * currentN + dn * mix.PFuel) / nextN,
                    (_mix.PInert * currentN + dn * mix.PInert) / nextN,
                    (_mix.PO2 * currentN + dn * mix.PO2) / nextN));
            }
            else
            {
                _mix = new GasMix(0.0, 0.0, 0.0);
            }

            return -dn;
        }

        private double PressureEquilibriumMaxFlow(double environmentPressure, double environmentTemperature)
        {
            if (Pressure > environmentPressure)
            {
                return -(environmentPressure * (0.5 * _degreesOfFreedom * _volume) - _kineticEnergy) / Math.Max(1.0e-12, KineticEnergyPerMol());
            }

            double environmentEnergyPerMol = 0.5 * environmentTemperature * GasConstant * _degreesOfFreedom;
            return -(environmentPressure * (0.5 * _degreesOfFreedom * _volume) - _kineticEnergy) / Math.Max(1.0e-12, environmentEnergyPerMol);
        }

        private double KineticEnergyPerMol()
        {
            return _nMol > MinimumGasMoles && double.IsFinite(_kineticEnergy)
                ? _kineticEnergy / _nMol
                : KineticEnergyPerMol(AmbientTemperature, _degreesOfFreedom);
        }

        private static double KineticEnergyPerMol(double temperature, int degreesOfFreedom)
        {
            return 0.5 * temperature * GasConstant * degreesOfFreedom;
        }

        private double BulkKineticEnergy()
        {
            double mass = Mass;
            if (mass <= 1.0e-12)
            {
                return 0.0;
            }

            double velocityX = _momentumX / mass;
            double velocityY = _momentumY / mass;
            double energy = 0.5 * mass * (velocityX * velocityX + velocityY * velocityY);
            return double.IsFinite(energy) ? energy : 0.0;
        }

        private double C()
        {
            if (_nMol <= 1.0e-12 || _kineticEnergy <= 0.0)
            {
                return 0.0;
            }

            double density = ApproximateDensity();
            if (density <= 1.0e-12)
            {
                return 0.0;
            }

            return Math.Sqrt(Math.Max(0.0, Pressure * HeatCapacityRatio / density));
        }

        private double ApproximateDensity()
        {
            if (_volume <= 1.0e-12 || _nMol <= MinimumGasMoles)
            {
                return 0.0;
            }

            double density = AirMolecularMass * _nMol / _volume;
            return double.IsFinite(density) ? density : 0.0;
        }

        private double VelocityX()
        {
            double mass = Mass;
            if (mass <= 1.0e-12 || !double.IsFinite(_momentumX))
            {
                return 0.0;
            }

            double velocity = _momentumX / mass;
            return double.IsFinite(velocity) ? velocity : 0.0;
        }

        private double VelocityY()
        {
            double mass = Mass;
            if (mass <= 1.0e-12 || !double.IsFinite(_momentumY))
            {
                return 0.0;
            }

            double velocity = _momentumY / mass;
            return double.IsFinite(velocity) ? velocity : 0.0;
        }

        private void Sanitize()
        {
            _volume = ClampFinite(_volume, MinimumGasVolume, MinimumGasVolume, 10_000.0);

            double fallbackMoles = AtmosphericPressure * _volume / (GasConstant * AmbientTemperature);
            if (!double.IsFinite(_nMol) || _nMol < MinimumGasMoles)
            {
                _nMol = Math.Max(MinimumGasMoles, fallbackMoles);
            }

            double fallbackEnergy = AmbientTemperature * (0.5 * _degreesOfFreedom * _nMol * GasConstant);
            double maximumEnergy = MaximumGasTemperature * (0.5 * _degreesOfFreedom * _nMol * GasConstant);
            if (!double.IsFinite(_kineticEnergy) || _kineticEnergy <= 0.0)
            {
                _kineticEnergy = fallbackEnergy;
            }
            else
            {
                _kineticEnergy = Clamp(_kineticEnergy, 0.0, maximumEnergy);
            }

            if (!double.IsFinite(_momentumX))
            {
                _momentumX = 0.0;
            }

            if (!double.IsFinite(_momentumY))
            {
                _momentumY = 0.0;
            }

            _mix = NormalizeMix(_mix);
        }

        private static double HeatCapacityRatioForDegrees(int degreesOfFreedom)
        {
            return 1.0 + 2.0 / degreesOfFreedom;
        }

        private static double ChokedFlowLimit(int degreesOfFreedom)
        {
            double hcr = HeatCapacityRatioForDegrees(degreesOfFreedom);
            return Math.Pow(2.0 / (hcr + 1.0), hcr / (hcr - 1.0));
        }

        private static double ChokedFlowRate(int degreesOfFreedom)
        {
            double hcr = HeatCapacityRatioForDegrees(degreesOfFreedom);
            return Math.Sqrt(hcr) * Math.Pow(2.0 / (hcr + 1.0), (hcr + 1.0) / (2.0 * (hcr - 1.0)));
        }

        private static GasMix NormalizeMix(GasMix mix)
        {
            double fuel = double.IsFinite(mix.PFuel) ? Math.Max(0.0, mix.PFuel) : 0.0;
            double inert = double.IsFinite(mix.PInert) ? Math.Max(0.0, mix.PInert) : 0.0;
            double oxygen = double.IsFinite(mix.PO2) ? Math.Max(0.0, mix.PO2) : 0.0;
            double sum = fuel + inert + oxygen;
            if (sum <= 1.0e-12)
            {
                return new GasMix(0.0, 1.0, 0.0);
            }

            return new GasMix(
                fuel / sum,
                inert / sum,
                oxygen / sum);
        }
    }

    private sealed class DelayFilter
    {
        private readonly double[] _history;
        private readonly int _latencySamples;
        private int _writeOffset;
        private int _size;

        public DelayFilter(double delaySeconds, int sampleRate)
        {
            _latencySamples = Math.Max(0, (int)Math.Round(delaySeconds * Math.Max(1, sampleRate)));
            _history = new double[Math.Max(1, _latencySamples + 32)];
        }

        public double Process(double sample)
        {
            _history[_writeOffset] = sample;
            _writeOffset = (_writeOffset + 1) % _history.Length;
            _size = Math.Min(_size + 1, _history.Length);

            if (_size <= _latencySamples)
            {
                return 0.0;
            }

            int readIndex = _writeOffset - _latencySamples - 1;
            if (readIndex < 0)
            {
                readIndex += _history.Length;
            }

            return _history[readIndex];
        }
    }

    private sealed class PressureWaveGuide
    {
        private readonly double[] _history;
        private readonly int _latencySamples;
        private int _writeOffset;
        private int _size;

        public PressureWaveGuide(double delaySeconds, int sampleRate)
        {
            _latencySamples = Math.Max(0, (int)Math.Round(delaySeconds * Math.Max(1, sampleRate)));
            _history = new double[Math.Max(1, _latencySamples + 32)];
        }

        public double Process(double sample, double reflectionGain)
        {
            double delayed = 0.0;
            if (_size > _latencySamples)
            {
                int readIndex = _writeOffset - _latencySamples - 1;
                if (readIndex < 0)
                {
                    readIndex += _history.Length;
                }

                delayed = _history[readIndex];
            }

            double boundedReflection = Math.Clamp(reflectionGain, 0.0, 0.28);
            _history[_writeOffset] = sample + delayed * boundedReflection;
            _writeOffset = (_writeOffset + 1) % _history.Length;
            _size = Math.Min(_size + 1, _history.Length);
            return delayed;
        }
    }

    private sealed class SampledFunction
    {
        private const int LookupSize = 512;
        private readonly List<(double X, double Y)> _samples = [];
        private readonly double _filterRadius;
        private double[]? _lookup;
        private double _lookupMin;
        private double _lookupMax;
        private double _lookupScale;

        public SampledFunction(double filterRadius)
        {
            _filterRadius = Math.Max(1.0e-9, filterRadius);
        }

        public void AddSample(double x, double y)
        {
            int index = _samples.FindIndex(sample => x < sample.X);
            if (index < 0)
            {
                _samples.Add((x, y));
            }
            else
            {
                _samples.Insert(index, (x, y));
            }

            _lookup = null;
        }

        public double SampleTriangle(double x)
        {
            if (_samples.Count == 0)
            {
                return 0.0;
            }

            if (x >= _samples[^1].X)
            {
                return _samples[^1].Y;
            }

            if (x <= _samples[0].X)
            {
                return _samples[0].Y;
            }

            if (_samples.Count >= 4)
            {
                EnsureLookup();
                if (_lookup is not null && x >= _lookupMin && x <= _lookupMax)
                {
                    double scaled = (x - _lookupMin) * _lookupScale;
                    int index = Math.Clamp((int)scaled, 0, LookupSize - 2);
                    double fraction = scaled - index;
                    return _lookup[index] + (_lookup[index + 1] - _lookup[index]) * fraction;
                }
            }

            return SampleTriangleSlow(x);
        }

        private double SampleTriangleSlow(double x)
        {
            int closest = ClosestSample(x);
            double sum = 0.0;
            double totalWeight = 0.0;
            for (int i = closest; i >= 0; i--)
            {
                if (_samples[i].X > x)
                {
                    continue;
                }

                if (Math.Abs(x - _samples[i].X) > _filterRadius)
                {
                    break;
                }

                double weight = Triangle(_samples[i].X - x);
                sum += weight * _samples[i].Y;
                totalWeight += weight;
            }

            for (int i = closest; i < _samples.Count; i++)
            {
                if (_samples[i].X <= x)
                {
                    continue;
                }

                if (Math.Abs(_samples[i].X - x) > _filterRadius)
                {
                    break;
                }

                double weight = Triangle(_samples[i].X - x);
                sum += weight * _samples[i].Y;
                totalWeight += weight;
            }

            return totalWeight > 1.0e-12 ? sum / totalWeight : 0.0;
        }

        private void EnsureLookup()
        {
            if (_lookup is not null)
            {
                return;
            }

            _lookupMin = _samples[0].X;
            _lookupMax = _samples[^1].X;
            double range = _lookupMax - _lookupMin;
            if (range <= 1.0e-12)
            {
                return;
            }

            double[] lookup = new double[LookupSize];
            double step = range / (LookupSize - 1);
            for (int i = 0; i < lookup.Length; i++)
            {
                lookup[i] = SampleTriangleSlow(_lookupMin + step * i);
            }

            _lookupScale = (LookupSize - 1) / range;
            _lookup = lookup;
        }

        private int ClosestSample(double x)
        {
            int left = 0;
            int right = _samples.Count - 1;
            if (x <= _samples[left].X)
            {
                return left;
            }

            if (x >= _samples[right].X)
            {
                return right;
            }

            while (left + 1 < right)
            {
                int middle = (left + right) / 2;
                if (x > _samples[middle].X)
                {
                    left = middle;
                }
                else if (x < _samples[middle].X)
                {
                    right = middle;
                }
                else
                {
                    return middle;
                }
            }

            return x - _samples[left].X < _samples[right].X - x ? left : right;
        }

        private double Triangle(double x)
        {
            return (_filterRadius - Math.Abs(x)) / _filterRadius;
        }
    }

    private sealed class FuelModel
    {
        private readonly SampledFunction _turbulenceToFlameSpeedRatio = new(5.0);

        public FuelModel(double maxBurningEfficiency, double maxTurbulenceEffect)
        {
            MolecularMass = 0.100;
            EnergyDensity = 48.1e6;
            MolecularAfr = 25.0 / 2.0;
            BurningEfficiencyRandomness = 0.5;
            LowEfficiencyAttenuation = 0.6;
            MaxBurningEfficiency = Clamp(maxBurningEfficiency, 0.05, 1.2);
            MaxTurbulenceEffect = Math.Max(0.1, maxTurbulenceEffect);
            MaxDilutionEffect = 10.0;

            _turbulenceToFlameSpeedRatio.AddSample(0.0, 3.0);
            for (int i = 5; i <= 45; i += 5)
            {
                _turbulenceToFlameSpeedRatio.AddSample(i, 1.5 * i);
            }
        }

        public double MolecularMass { get; }

        public double EnergyDensity { get; }

        public double MolecularAfr { get; }

        public double BurningEfficiencyRandomness { get; }

        public double LowEfficiencyAttenuation { get; }

        public double MaxBurningEfficiency { get; }

        public double MaxTurbulenceEffect { get; }

        public double MaxDilutionEffect { get; }

        public double FlameSpeed(double turbulence, double molecularAfr, double temperature, double pressure, double firingPressure, double motoringPressure)
        {
            double laminar = LaminarBurningVelocity(molecularAfr, temperature, pressure);
            double ratioInput = turbulence / Math.Max(1.0e-6, laminar);
            return _turbulenceToFlameSpeedRatio.SampleTriangle(ratioInput) * laminar;
        }

        private double LaminarBurningVelocity(double molecularAfr, double temperature, double pressure)
        {
            const double erM = 1.21;
            double bM = 30.5 * 0.01;
            double bEr = -54.9 * 0.01;
            double er = molecularAfr / MolecularAfr;
            double alpha = 2.4 - 0.271 * Math.Pow(er, 3.51);
            double beta = -0.357 + 0.14 * Math.Pow(er, 2.77);
            double baseSpeed = bM + bEr * (er - erM) * (er - erM);
            double temperatureRatio = Math.Max(0.1, temperature / 298.0);
            double pressureRatio = Math.Max(0.01, pressure / AtmosphericPressure);
            return Math.Max(0.01, baseSpeed * Math.Pow(temperatureRatio, alpha) * Math.Pow(pressureRatio, beta));
        }
    }

    private sealed record EngineSimGasFlowProfile(
        double ChamberVolume,
        double IntakeRunnerVolume,
        double IntakeRunnerCrossSectionArea,
        double ExhaustRunnerVolume,
        double ExhaustRunnerCrossSectionArea,
        SampledFunction IntakePortFlow,
        SampledFunction ExhaustPortFlow,
        double IntakePlenumVolume,
        double IntakePlenumCrossSectionArea,
        double IntakeFlowRate,
        double IntakeRunnerFlowRate,
        double IntakeIdleFlowRate,
        double IntakeIdleThrottlePlatePosition,
        double IntakeRunnerLength,
        double IntakeVelocityDecay,
        double ExhaustCollectorVolume,
        double ExhaustCollectorCrossSectionArea,
        double ExhaustCollectorLength,
        double ExhaustOutletFlowRate,
        double ExhaustPrimaryTubeLength,
        double ExhaustPrimaryFlowRate,
        double ExhaustVelocityDecay,
        double[] BlowbyK)
    {
        public static EngineSimGasFlowProfile Load(VehicleAudioParameters parameters)
        {
            string script = ReadOptional(parameters.EngineSimulatorMrScriptPath);
            string headBody = ExtractBlock(script, @"private\s+node\s+honda_vtec_head\s*\{", "generic_cylinder_head");
            string intakeBody = ExtractBlock(script, @"intake\s+intake\s*\(", "\n    \\)");
            string exhaustBody = ExtractBlock(script, @"exhaust_system_parameters\s+es_params\s*\(", "\n    \\)");

            SampledFunction intakePortFlow = ExtractFlowFunction(script, "intake_flow");
            SampledFunction exhaustPortFlow = ExtractFlowFunction(script, "exhaust_flow");
            if (IsEmpty(intakePortFlow))
            {
                intakePortFlow = BuildFallbackIntakeFlow();
            }

            if (IsEmpty(exhaustPortFlow))
            {
                exhaustPortFlow = BuildFallbackExhaustFlow();
            }

            double chamberVolume = ExtractUnitsExpression(headBody, @"chamber_volume:\s*(?<value>[^;\r\n,]+)", 41.6 * Cc);
            double intakeRunnerVolume = ExtractUnitsExpression(headBody, @"intake_runner_volume:\s*(?<value>[^;\r\n,]+)", 149.6 * Cc);
            double intakeRunnerArea = ExtractUnitsExpression(headBody, @"intake_runner_cross_section_area:\s*(?<value>[^;\r\n,]+)", 1.35 * Inch * 1.35 * Inch);
            double exhaustRunnerVolume = ExtractUnitsExpression(headBody, @"exhaust_runner_volume:\s*(?<value>[^;\r\n,]+)", 50.0 * Cc);
            double exhaustRunnerArea = ExtractUnitsExpression(headBody, @"exhaust_runner_cross_section_area:\s*(?<value>[^;\r\n,]+)", 1.25 * Inch * 1.25 * Inch);

            double intakePlenumVolume = ExtractUnitsExpression(intakeBody, @"plenum_volume:\s*(?<value>[^,\r\n]+)", Math.Max(0.1, parameters.EngineSimulatorIntakePlenumVolumeLiters) * Liter);
            double intakePlenumArea = ExtractUnitsExpression(intakeBody, @"plenum_cross_section_area:\s*(?<value>[^,\r\n]+)", 20.0 * Cm2);
            double intakeFlowRate = ExtractFlowConstant(intakeBody, @"intake_flow_rate:\s*k_carb\((?<value>[-+]?\d+(?:\.\d+)?)\)", 800.0, carb: true);
            double runnerFlowRate = ExtractFlowConstant(intakeBody, @"runner_flow_rate:\s*k_carb\((?<value>[-+]?\d+(?:\.\d+)?)\)", 250.0, carb: true);
            double idleFlowRate = ExtractFlowConstant(intakeBody, @"idle_flow_rate:\s*k_carb\((?<value>[-+]?\d+(?:\.\d+)?)\)", 0.0, carb: true);
            double idleThrottlePlate = ExtractSingle(intakeBody, @"idle_throttle_plate_position:\s*(?<value>[-+]?\d+(?:\.\d+)?)", 0.9989);
            double runnerLength = ExtractUnitsExpression(intakeBody, @"runner_length:\s*(?<value>[^,\r\n]+)", Math.Max(1.0, parameters.EngineSimulatorIntakeRunnerLengthInches) * Inch);
            double intakeVelocityDecay = ExtractSingle(intakeBody, @"velocity_decay:\s*(?<value>[-+]?\d+(?:\.\d+)?)", 0.5);

            double collectorVolume = ExtractUnitsExpression(exhaustBody, @"volume:\s*(?<value>[^,\r\n]+)", Math.Max(1.0, parameters.EngineSimulatorExhaustVolumeLiters) * Liter);
            double collectorArea = ExtractCollectorArea(exhaustBody);
            double collectorLength = collectorVolume / Math.Max(1.0e-8, collectorArea);
            double outletFlowRate = ExtractFlowConstant(exhaustBody, @"outlet_flow_rate:\s*k_carb\((?<value>[-+]?\d+(?:\.\d+)?)\)", 1000.0, carb: true);
            double primaryLength = ExtractUnitsExpression(exhaustBody, @"primary_tube_length:\s*(?<value>[^,\r\n]+)", Math.Max(1.0, parameters.EngineSimulatorExhaustPrimaryTubeLengthInches) * Inch);
            double primaryFlowRate = ExtractFlowConstant(exhaustBody, @"primary_flow_rate:\s*k_carb\((?<value>[-+]?\d+(?:\.\d+)?)\)", 200.0, carb: true);
            double exhaustVelocityDecay = ExtractSingle(exhaustBody, @"velocity_decay:\s*(?<value>[-+]?\d+(?:\.\d+)?)", 1.0);

            double[] blowby = ExtractBlowby(script);
            if (blowby.Length == 0)
            {
                blowby = [GasSystem.K28InH2O(0.001), GasSystem.K28InH2O(0.002)];
            }

            return new EngineSimGasFlowProfile(
                chamberVolume,
                intakeRunnerVolume,
                Math.Max(1.0e-8, intakeRunnerArea),
                exhaustRunnerVolume,
                Math.Max(1.0e-8, exhaustRunnerArea),
                intakePortFlow,
                exhaustPortFlow,
                Math.Max(1.0e-6, intakePlenumVolume),
                Math.Max(1.0e-8, intakePlenumArea),
                Math.Max(0.0, intakeFlowRate),
                Math.Max(0.0, runnerFlowRate),
                Math.Max(0.0, idleFlowRate),
                Clamp(idleThrottlePlate, 0.0, 1.0),
                Math.Max(0.01, runnerLength),
                Math.Max(0.0, intakeVelocityDecay),
                Math.Max(1.0e-6, collectorVolume),
                Math.Max(1.0e-8, collectorArea),
                Math.Max(0.01, collectorLength),
                Math.Max(0.0, outletFlowRate),
                Math.Max(0.01, primaryLength),
                Math.Max(0.0, primaryFlowRate),
                Math.Max(0.0, exhaustVelocityDecay),
                blowby);
        }

        private static string ReadOptional(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string[] candidates =
            [
                path,
                Path.Combine(Environment.CurrentDirectory, path),
                Path.Combine(AppContext.BaseDirectory, path)
            ];

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }
            }

            return string.Empty;
        }

        private static string ExtractBlock(string text, string startPattern, string endPattern)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            Match start = Regex.Match(text, startPattern, RegexOptions.CultureInvariant);
            if (!start.Success)
            {
                return string.Empty;
            }

            int startIndex = start.Index + start.Length;
            Match end = Regex.Match(text[startIndex..], endPattern, RegexOptions.CultureInvariant);
            int length = end.Success ? end.Index : text.Length - startIndex;
            return text.Substring(startIndex, length);
        }

        private static SampledFunction ExtractFlowFunction(string script, string name)
        {
            SampledFunction function = new(50.0 * Thou);
            if (string.IsNullOrWhiteSpace(script))
            {
                return function;
            }

            int start = script.IndexOf($"function {name}", StringComparison.Ordinal);
            if (start < 0)
            {
                return function;
            }

            int end = name == "intake_flow"
                ? script.IndexOf("function exhaust_flow", start + 1, StringComparison.Ordinal)
                : script.IndexOf("generic_cylinder_head", start + 1, StringComparison.Ordinal);
            if (end < 0)
            {
                end = script.Length;
            }

            string body = script[start..end];
            foreach (Match match in Regex.Matches(
                         body,
                         @"\.add_flow_sample\(\s*(?<lift>[-+]?\d+(?:\.\d+)?)\s*\*\s*lift_scale,\s*(?<flow>[-+]?\d+(?:\.\d+)?)\s*\*\s*flow_attenuation",
                         RegexOptions.CultureInvariant))
            {
                double liftThou = ParseDouble(match.Groups["lift"].Value, 0.0);
                double flowScfm = ParseDouble(match.Groups["flow"].Value, 0.0);
                function.AddSample(liftThou * Thou, GasSystem.K28InH2O(flowScfm));
            }

            return function;
        }

        private static bool IsEmpty(SampledFunction function)
        {
            return function.SampleTriangle(100.0 * Thou) == 0.0;
        }

        private static SampledFunction BuildFallbackIntakeFlow()
        {
            SampledFunction function = new(50.0 * Thou);
            double[] flow = [0.0, 50.0, 80.0, 125.0, 160.0, 190.0, 210.0, 225.0, 230.0, 250.0];
            for (int i = 0; i < flow.Length; i++)
            {
                function.AddSample(i * 50.0 * Thou, GasSystem.K28InH2O(flow[i]));
            }

            return function;
        }

        private static SampledFunction BuildFallbackExhaustFlow()
        {
            SampledFunction function = new(50.0 * Thou);
            double[] flow = [0.0, 50.0, 80.0, 110.0, 130.0, 150.0, 160.0, 170.0, 170.0, 170.0];
            for (int i = 0; i < flow.Length; i++)
            {
                function.AddSample(i * 50.0 * Thou, GasSystem.K28InH2O(flow[i]));
            }

            return function;
        }

        private static double ExtractUnitsExpression(string text, string pattern, double fallback)
        {
            string expression = ExtractRaw(text, pattern, string.Empty);
            return string.IsNullOrWhiteSpace(expression) ? fallback : EvaluateUnitsExpression(expression, fallback);
        }

        private static double ExtractCollectorArea(string text)
        {
            Match circle = Regex.Match(
                text,
                @"collector_cross_section_area:\s*circle_area\(\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.(?<unit>inch|mm|cm|m)",
                RegexOptions.CultureInvariant);
            if (circle.Success)
            {
                double diameter = ParseDouble(circle.Groups["value"].Value, 2.0) * UnitValue(circle.Groups["unit"].Value);
                return Math.PI * Math.Pow(diameter * 0.5, 2.0);
            }

            return Math.PI * Math.Pow(2.0 * Inch * 0.5, 2.0);
        }

        private static double ExtractFlowConstant(string text, string pattern, double fallbackScfm, bool carb)
        {
            double scfm = ExtractSingle(text, pattern, fallbackScfm);
            return carb ? GasSystem.KCarb(scfm) : GasSystem.K28InH2O(scfm);
        }

        private static double[] ExtractBlowby(string script)
        {
            if (string.IsNullOrWhiteSpace(script))
            {
                return [];
            }

            return
            [
                .. Regex.Matches(script, @"blowby:\s*k_28inH2O\((?<value>[-+]?\d+(?:\.\d+)?)\)", RegexOptions.CultureInvariant)
                    .Select(match => GasSystem.K28InH2O(ParseDouble(match.Groups["value"].Value, 0.001)))
            ];
        }

        private static string ExtractRaw(string text, string pattern, string fallback)
        {
            Match match = Regex.Match(text, pattern, RegexOptions.CultureInvariant);
            return match.Success ? match.Groups["value"].Value.Trim() : fallback;
        }

        private static double ExtractSingle(string text, string pattern, double fallback)
        {
            Match match = Regex.Match(text, pattern, RegexOptions.CultureInvariant);
            return match.Success ? ParseDouble(match.Groups["value"].Value, fallback) : fallback;
        }

        private static double EvaluateUnitsExpression(string expression, double fallback)
        {
            string sanitized = expression
                .Replace("units.", string.Empty, StringComparison.Ordinal)
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .TrimEnd(',', ';');
            if (sanitized.Contains('+', StringComparison.Ordinal) ||
                sanitized.Contains('-', StringComparison.Ordinal) ||
                sanitized.Contains('/', StringComparison.Ordinal) ||
                sanitized.Contains("circle_area", StringComparison.Ordinal))
            {
                return fallback;
            }

            double product = 1.0;
            bool found = false;
            foreach (string token in sanitized.Split('*', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                {
                    product *= number;
                    found = true;
                    continue;
                }

                double unit = UnitValue(token);
                if (unit > 0.0)
                {
                    product *= unit;
                    found = true;
                }
            }

            return found ? product : fallback;
        }

        private static double UnitValue(string unit)
        {
            return unit switch
            {
                "m" => 1.0,
                "cm" => 0.01,
                "mm" => 0.001,
                "inch" => Inch,
                "thou" => Thou,
                "m2" => 1.0,
                "cm2" => Cm2,
                "m3" => 1.0,
                "cc" => Cc,
                "mL" => Cc,
                "L" => Liter,
                _ => 0.0
            };
        }

        private static double ParseDouble(string value, double fallback)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                ? parsed
                : fallback;
        }
    }
}

internal readonly record struct EngineSimGasFlowPowerState(
    float IndicatedTorqueNm,
    float PositiveTorqueNm,
    float NegativeTorqueNm,
    float FuelCutBlend,
    float CrankPhaseDegrees,
    float AfterfireBlend);

internal readonly record struct EngineSimGasFlowDiagnostics(
    float PeakChamberPressurePa,
    float AverageExhaustPressurePa,
    float AverageIntakePressurePa,
    float AfterfireEnergyJ);
