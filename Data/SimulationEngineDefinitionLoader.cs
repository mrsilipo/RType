using System.Text.Json;
using RType.Vehicle;

namespace RType.Data;

public static class SimulationEngineDefinitionLoader
{
    public static SimulationEngineParameters Load(string path)
    {
        string resolvedPath = ResolveDataPath(path);
        using FileStream stream = File.OpenRead(resolvedPath);
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = true
        });

        JsonElement root = document.RootElement;
        return new SimulationEngineParameters
        {
            HandlingModel = ReadValueString(root, "rtypeClassic", "handlingModel"),
            Timing = ReadTiming(root),
            VehicleSafety = ReadVehicleSafety(root),
            StabilityAssist = ReadStabilityAssist(root),
            DigitalThrottleAssist = ReadDigitalThrottleAssist(root),
            DigitalBrakeAssist = ReadDigitalBrakeAssist(root),
            BrakeThrottlePriority = ReadBrakeThrottlePriority(root),
            SteeringAssist = ReadSteeringAssist(root),
            TyreForce = ReadTyreForce(root),
            RpmResponse = ReadRpmResponse(root),
            ClassicBicycle = ReadClassicBicycle(root),
            ClassicFourWheel = ReadClassicFourWheel(root)
        };
    }

    private static PhysicsTimingParameters ReadTiming(JsonElement root)
    {
        PhysicsTimingParameters defaults = new();
        return new PhysicsTimingParameters
        {
            FixedTickRateHz = ReadValueSingle(root, defaults.FixedTickRateHz, "physics", "fixedTickRateHz"),
            MaximumFrameTimeSeconds = ReadValueSingle(root, defaults.MaximumFrameTimeSeconds, "physics", "maximumFrameTimeSeconds"),
            MaximumTicksPerUpdate = ReadValueInt(root, defaults.MaximumTicksPerUpdate, "physics", "maximumTicksPerUpdate")
        };
    }

    private static VehicleSafetyParameters ReadVehicleSafety(JsonElement root)
    {
        VehicleSafetyParameters defaults = new();
        return new VehicleSafetyParameters
        {
            MinimumSlipSpeedMetersPerSecond = ReadValueSingle(root, defaults.MinimumSlipSpeedMetersPerSecond, "vehicleSafety", "minimumSlipSpeedMetersPerSecond"),
            MaximumReverseSpeedMetersPerSecond = ReadValueSingle(root, defaults.MaximumReverseSpeedMetersPerSecond, "vehicleSafety", "maximumReverseSpeedMetersPerSecond"),
            MaximumForwardSpeedMetersPerSecond = ReadValueSingle(root, defaults.MaximumForwardSpeedMetersPerSecond, "vehicleSafety", "maximumForwardSpeedMetersPerSecond")
        };
    }

    private static StabilityAssistParameters ReadStabilityAssist(JsonElement root)
    {
        StabilityAssistParameters defaults = new();
        return new StabilityAssistParameters
        {
            MinimumSpeedMetersPerSecond = ReadValueSingle(root, defaults.MinimumSpeedMetersPerSecond, "stabilityAssist", "minimumSpeedMetersPerSecond"),
            MinimumLateralSpeedMetersPerSecond = ReadValueSingle(root, defaults.MinimumLateralSpeedMetersPerSecond, "stabilityAssist", "minimumLateralSpeedMetersPerSecond"),
            SpeedBlendStartMetersPerSecond = ReadValueSingle(root, defaults.SpeedBlendStartMetersPerSecond, "stabilityAssist", "speedBlendStartMetersPerSecond"),
            SpeedBlendEndMetersPerSecond = ReadValueSingle(root, defaults.SpeedBlendEndMetersPerSecond, "stabilityAssist", "speedBlendEndMetersPerSecond"),
            GripBlendStart = ReadValueSingle(root, defaults.GripBlendStart, "stabilityAssist", "gripBlendStart"),
            GripBlendEnd = ReadValueSingle(root, defaults.GripBlendEnd, "stabilityAssist", "gripBlendEnd"),
            ThrottleBlendStart = ReadValueSingle(root, defaults.ThrottleBlendStart, "stabilityAssist", "throttleBlendStart"),
            ThrottleBlendEnd = ReadValueSingle(root, defaults.ThrottleBlendEnd, "stabilityAssist", "throttleBlendEnd"),
            BrakeBlendStart = ReadValueSingle(root, defaults.BrakeBlendStart, "stabilityAssist", "brakeBlendStart"),
            BrakeBlendEnd = ReadValueSingle(root, defaults.BrakeBlendEnd, "stabilityAssist", "brakeBlendEnd"),
            LateralDampingMin = ReadValueSingle(root, defaults.LateralDampingMin, "stabilityAssist", "lateralDampingMin"),
            LateralDampingMax = ReadValueSingle(root, defaults.LateralDampingMax, "stabilityAssist", "lateralDampingMax"),
            LateralGripBoost = ReadValueSingle(root, defaults.LateralGripBoost, "stabilityAssist", "lateralGripBoost"),
            LateralThrottleBoost = ReadValueSingle(root, defaults.LateralThrottleBoost, "stabilityAssist", "lateralThrottleBoost"),
            LateralBrakeBoost = ReadValueSingle(root, defaults.LateralBrakeBoost, "stabilityAssist", "lateralBrakeBoost"),
            MaxLateralAccelerationMinG = ReadValueSingle(root, defaults.MaxLateralAccelerationMinG, "stabilityAssist", "maxLateralAccelerationMinG"),
            MaxLateralAccelerationMaxG = ReadValueSingle(root, defaults.MaxLateralAccelerationMaxG, "stabilityAssist", "maxLateralAccelerationMaxG"),
            YawDampingMin = ReadValueSingle(root, defaults.YawDampingMin, "stabilityAssist", "yawDampingMin"),
            YawDampingMax = ReadValueSingle(root, defaults.YawDampingMax, "stabilityAssist", "yawDampingMax"),
            YawGripBoost = ReadValueSingle(root, defaults.YawGripBoost, "stabilityAssist", "yawGripBoost"),
            YawRecoveryBoost = ReadValueSingle(root, defaults.YawRecoveryBoost, "stabilityAssist", "yawRecoveryBoost"),
            YawThrottleBoost = ReadValueSingle(root, defaults.YawThrottleBoost, "stabilityAssist", "yawThrottleBoost"),
            YawBrakeBoost = ReadValueSingle(root, defaults.YawBrakeBoost, "stabilityAssist", "yawBrakeBoost"),
            BodySlipStartDegrees = ReadValueSingle(root, defaults.BodySlipStartDegrees, "stabilityAssist", "bodySlipStartDegrees"),
            BodySlipEndDegrees = ReadValueSingle(root, defaults.BodySlipEndDegrees, "stabilityAssist", "bodySlipEndDegrees"),
            TyreSlipStartDegrees = ReadValueSingle(root, defaults.TyreSlipStartDegrees, "stabilityAssist", "tyreSlipStartDegrees"),
            TyreSlipEndDegrees = ReadValueSingle(root, defaults.TyreSlipEndDegrees, "stabilityAssist", "tyreSlipEndDegrees"),
            AssistGripStart = ReadValueSingle(root, defaults.AssistGripStart, "stabilityAssist", "assistGripStart"),
            AssistGripEnd = ReadValueSingle(root, defaults.AssistGripEnd, "stabilityAssist", "assistGripEnd"),
            BodyGripInfluenceMin = ReadValueSingle(root, defaults.BodyGripInfluenceMin, "stabilityAssist", "bodyGripInfluenceMin"),
            BodyGripInfluenceMax = ReadValueSingle(root, defaults.BodyGripInfluenceMax, "stabilityAssist", "bodyGripInfluenceMax"),
            TyreGripInfluenceMin = ReadValueSingle(root, defaults.TyreGripInfluenceMin, "stabilityAssist", "tyreGripInfluenceMin"),
            TyreGripInfluenceMax = ReadValueSingle(root, defaults.TyreGripInfluenceMax, "stabilityAssist", "tyreGripInfluenceMax"),
            CounterSteerInputStart = ReadValueSingle(root, defaults.CounterSteerInputStart, "stabilityAssist", "counterSteerInputStart"),
            CounterSteerInputEnd = ReadValueSingle(root, defaults.CounterSteerInputEnd, "stabilityAssist", "counterSteerInputEnd"),
            CounterSteerGripAllowance = ReadValueSingle(root, defaults.CounterSteerGripAllowance, "stabilityAssist", "counterSteerGripAllowance"),
            CounterSteerSlipRelaxationMultiplier = ReadValueSingle(root, defaults.CounterSteerSlipRelaxationMultiplier, "stabilityAssist", "counterSteerSlipRelaxationMultiplier"),
            CounterSteerSlidingFrictionRecovery = ReadValueSingle(root, defaults.CounterSteerSlidingFrictionRecovery, "stabilityAssist", "counterSteerSlidingFrictionRecovery"),
            NeutralRecoveryInputStart = ReadValueSingle(root, defaults.NeutralRecoveryInputStart, "stabilityAssist", "neutralRecoveryInputStart"),
            NeutralRecoveryInputEnd = ReadValueSingle(root, defaults.NeutralRecoveryInputEnd, "stabilityAssist", "neutralRecoveryInputEnd"),
            NeutralRecoveryMultiplier = ReadValueSingle(root, defaults.NeutralRecoveryMultiplier, "stabilityAssist", "neutralRecoveryMultiplier"),
            CommittedTurnInputStart = ReadValueSingle(root, defaults.CommittedTurnInputStart, "stabilityAssist", "committedTurnInputStart"),
            CommittedTurnInputEnd = ReadValueSingle(root, defaults.CommittedTurnInputEnd, "stabilityAssist", "committedTurnInputEnd"),
            CommittedTurnBrakeDampingMultiplier = ReadValueSingle(root, defaults.CommittedTurnBrakeDampingMultiplier, "stabilityAssist", "committedTurnBrakeDampingMultiplier"),
            CommittedTurnCoastDampingMultiplier = ReadValueSingle(root, defaults.CommittedTurnCoastDampingMultiplier, "stabilityAssist", "committedTurnCoastDampingMultiplier"),
            CommittedTurnCoastThrottleEnd = ReadValueSingle(root, defaults.CommittedTurnCoastThrottleEnd, "stabilityAssist", "committedTurnCoastThrottleEnd"),
            MinimumYawRateDegreesPerSecond = ReadValueSingle(root, defaults.MinimumYawRateDegreesPerSecond, "stabilityAssist", "minimumYawRateDegreesPerSecond")
        };
    }

    private static DigitalThrottleAssistParameters ReadDigitalThrottleAssist(JsonElement root)
    {
        DigitalThrottleAssistParameters defaults = new();
        return new DigitalThrottleAssistParameters
        {
            FullThrottleBelowSpeedMetersPerSecond = ReadValueSingle(root, defaults.FullThrottleBelowSpeedMetersPerSecond, "digitalThrottleAssist", "fullThrottleBelowSpeedMetersPerSecond"),
            SpeedBlendStartMetersPerSecond = ReadValueSingle(root, defaults.SpeedBlendStartMetersPerSecond, "digitalThrottleAssist", "speedBlendStartMetersPerSecond"),
            SpeedBlendEndMetersPerSecond = ReadValueSingle(root, defaults.SpeedBlendEndMetersPerSecond, "digitalThrottleAssist", "speedBlendEndMetersPerSecond"),
            SteeringBlendStart = ReadValueSingle(root, defaults.SteeringBlendStart, "digitalThrottleAssist", "steeringBlendStart"),
            SteeringBlendEnd = ReadValueSingle(root, defaults.SteeringBlendEnd, "digitalThrottleAssist", "steeringBlendEnd"),
            StraightLaunchBypassSpeedMetersPerSecond = ReadValueSingle(root, defaults.StraightLaunchBypassSpeedMetersPerSecond, "digitalThrottleAssist", "straightLaunchBypassSpeedMetersPerSecond"),
            GripUsageBlendStart = ReadValueSingle(root, defaults.GripUsageBlendStart, "digitalThrottleAssist", "gripUsageBlendStart"),
            GripUsageBlendEnd = ReadValueSingle(root, defaults.GripUsageBlendEnd, "digitalThrottleAssist", "gripUsageBlendEnd"),
            SlipRatioBlendStart = ReadValueSingle(root, defaults.SlipRatioBlendStart, "digitalThrottleAssist", "slipRatioBlendStart"),
            SlipRatioBlendEnd = ReadValueSingle(root, defaults.SlipRatioBlendEnd, "digitalThrottleAssist", "slipRatioBlendEnd"),
            CornerLimitLowSpeed = ReadValueSingle(root, defaults.CornerLimitLowSpeed, "digitalThrottleAssist", "cornerLimitLowSpeed"),
            CornerLimitHighSpeed = ReadValueSingle(root, defaults.CornerLimitHighSpeed, "digitalThrottleAssist", "cornerLimitHighSpeed"),
            TractionDemandGripScale = ReadValueSingle(root, defaults.TractionDemandGripScale, "digitalThrottleAssist", "tractionDemandGripScale"),
            TractionLimitFloor = ReadValueSingle(root, defaults.TractionLimitFloor, "digitalThrottleAssist", "tractionLimitFloor"),
            MinimumAssistLimit = ReadValueSingle(root, defaults.MinimumAssistLimit, "digitalThrottleAssist", "minimumAssistLimit")
        };
    }

    private static DigitalBrakeAssistParameters ReadDigitalBrakeAssist(JsonElement root)
    {
        DigitalBrakeAssistParameters defaults = new();
        return new DigitalBrakeAssistParameters
        {
            FullBrakeBelowSpeedMetersPerSecond = ReadValueSingle(root, defaults.FullBrakeBelowSpeedMetersPerSecond, "digitalBrakeAssist", "fullBrakeBelowSpeedMetersPerSecond"),
            SpeedBlendStartMetersPerSecond = ReadValueSingle(root, defaults.SpeedBlendStartMetersPerSecond, "digitalBrakeAssist", "speedBlendStartMetersPerSecond"),
            SpeedBlendEndMetersPerSecond = ReadValueSingle(root, defaults.SpeedBlendEndMetersPerSecond, "digitalBrakeAssist", "speedBlendEndMetersPerSecond"),
            SteeringBlendStart = ReadValueSingle(root, defaults.SteeringBlendStart, "digitalBrakeAssist", "steeringBlendStart"),
            SteeringBlendEnd = ReadValueSingle(root, defaults.SteeringBlendEnd, "digitalBrakeAssist", "steeringBlendEnd"),
            HighSpeedBrakeLimit = ReadValueSingle(root, defaults.HighSpeedBrakeLimit, "digitalBrakeAssist", "highSpeedBrakeLimit"),
            SteeringReductionLowSpeed = ReadValueSingle(root, defaults.SteeringReductionLowSpeed, "digitalBrakeAssist", "steeringReductionLowSpeed"),
            SteeringReductionHighSpeed = ReadValueSingle(root, defaults.SteeringReductionHighSpeed, "digitalBrakeAssist", "steeringReductionHighSpeed"),
            MinimumAssistLimit = ReadValueSingle(root, defaults.MinimumAssistLimit, "digitalBrakeAssist", "minimumAssistLimit"),
            MaximumAssistLimit = ReadValueSingle(root, defaults.MaximumAssistLimit, "digitalBrakeAssist", "maximumAssistLimit"),
            TrailBrakeFrontTorqueMultiplier = ReadValueSingle(root, defaults.TrailBrakeFrontTorqueMultiplier, "digitalBrakeAssist", "trailBrakeFrontTorqueMultiplier"),
            TrailBrakeRearTorqueMultiplier = ReadValueSingle(root, defaults.TrailBrakeRearTorqueMultiplier, "digitalBrakeAssist", "trailBrakeRearTorqueMultiplier"),
            AbsTargetSlipRatio = ReadValueSingle(root, defaults.AbsTargetSlipRatio, "digitalBrakeAssist", "absTargetSlipRatio"),
            AbsReleaseSlipRatio = ReadValueSingle(root, defaults.AbsReleaseSlipRatio, "digitalBrakeAssist", "absReleaseSlipRatio"),
            AbsApplyRatePerSecond = ReadValueSingle(root, defaults.AbsApplyRatePerSecond, "digitalBrakeAssist", "absApplyRatePerSecond"),
            AbsReleaseRatePerSecond = ReadValueSingle(root, defaults.AbsReleaseRatePerSecond, "digitalBrakeAssist", "absReleaseRatePerSecond"),
            AbsMinimumSpeedMetersPerSecond = ReadValueSingle(root, defaults.AbsMinimumSpeedMetersPerSecond, "digitalBrakeAssist", "absMinimumSpeedMetersPerSecond"),
            AbsMinimumPressureRatio = ReadValueSingle(root, defaults.AbsMinimumPressureRatio, "digitalBrakeAssist", "absMinimumPressureRatio")
        };
    }

    private static BrakeThrottlePriorityParameters ReadBrakeThrottlePriority(JsonElement root)
    {
        BrakeThrottlePriorityParameters defaults = new();
        return new BrakeThrottlePriorityParameters
        {
            BrakeBlendStart = ReadValueSingle(root, defaults.BrakeBlendStart, "brakeThrottlePriority", "brakeBlendStart"),
            BrakeBlendEnd = ReadValueSingle(root, defaults.BrakeBlendEnd, "brakeThrottlePriority", "brakeBlendEnd"),
            FullBrakeThrottleMultiplier = ReadValueSingle(root, defaults.FullBrakeThrottleMultiplier, "brakeThrottlePriority", "fullBrakeThrottleMultiplier")
        };
    }

    private static SteeringAssistParameters ReadSteeringAssist(JsonElement root)
    {
        SteeringAssistParameters defaults = new();
        return new SteeringAssistParameters
        {
            DirectRackInput = ReadValueBool(root, defaults.DirectRackInput, "steeringAssist", "directRackInput"),
            BrakeAngleBoostBrakeStart = ReadValueSingle(root, defaults.BrakeAngleBoostBrakeStart, "steeringAssist", "brakeAngleBoostBrakeStart"),
            BrakeAngleBoostBrakeEnd = ReadValueSingle(root, defaults.BrakeAngleBoostBrakeEnd, "steeringAssist", "brakeAngleBoostBrakeEnd"),
            BrakeAngleBoostSpeedStartMetersPerSecond = ReadValueSingle(root, defaults.BrakeAngleBoostSpeedStartMetersPerSecond, "steeringAssist", "brakeAngleBoostSpeedStartMetersPerSecond"),
            BrakeAngleBoostSpeedEndMetersPerSecond = ReadValueSingle(root, defaults.BrakeAngleBoostSpeedEndMetersPerSecond, "steeringAssist", "brakeAngleBoostSpeedEndMetersPerSecond"),
            BrakeAngleBoostMultiplier = ReadValueSingle(root, defaults.BrakeAngleBoostMultiplier, "steeringAssist", "brakeAngleBoostMultiplier"),
            SpeedMatchedSlipStartMetersPerSecond = ReadValueSingle(root, defaults.SpeedMatchedSlipStartMetersPerSecond, "steeringAssist", "speedMatchedSlipStartMetersPerSecond"),
            SpeedMatchedSlipEndMetersPerSecond = ReadValueSingle(root, defaults.SpeedMatchedSlipEndMetersPerSecond, "steeringAssist", "speedMatchedSlipEndMetersPerSecond"),
            LowSpeedSlipAllowanceMultiplier = ReadValueSingle(root, defaults.LowSpeedSlipAllowanceMultiplier, "steeringAssist", "lowSpeedSlipAllowanceMultiplier"),
            HighSpeedSlipAllowanceMultiplier = ReadValueSingle(root, defaults.HighSpeedSlipAllowanceMultiplier, "steeringAssist", "highSpeedSlipAllowanceMultiplier"),
            HighSpeedMinimumRoadWheelAngleDegrees = ReadValueSingle(root, defaults.HighSpeedMinimumRoadWheelAngleDegrees, "steeringAssist", "highSpeedMinimumRoadWheelAngleDegrees"),
            CommittedTurnInputStart = ReadValueSingle(root, defaults.CommittedTurnInputStart, "steeringAssist", "committedTurnInputStart"),
            CommittedTurnInputEnd = ReadValueSingle(root, defaults.CommittedTurnInputEnd, "steeringAssist", "committedTurnInputEnd"),
            CommittedTurnMinimumRoadWheelAngleDegrees = ReadValueSingle(root, defaults.CommittedTurnMinimumRoadWheelAngleDegrees, "steeringAssist", "committedTurnMinimumRoadWheelAngleDegrees"),
            GripReserveAngleBoost = ReadValueSingle(root, defaults.GripReserveAngleBoost, "steeringAssist", "gripReserveAngleBoost"),
            HighSpeedInputCurveExponent = ReadValueSingle(root, defaults.HighSpeedInputCurveExponent, "steeringAssist", "highSpeedInputCurveExponent"),
            DecelInputCurveExponent = ReadValueSingle(root, defaults.DecelInputCurveExponent, "steeringAssist", "decelInputCurveExponent"),
            DecelAuthorityThrottleEnd = ReadValueSingle(root, defaults.DecelAuthorityThrottleEnd, "steeringAssist", "decelAuthorityThrottleEnd"),
            DecelInputRateBoost = ReadValueSingle(root, defaults.DecelInputRateBoost, "steeringAssist", "decelInputRateBoost"),
            LateralForceForwardProjectionScale = ReadValueSingle(root, defaults.LateralForceForwardProjectionScale, "steeringAssist", "lateralForceForwardProjectionScale"),
            PoweredLateralForceForwardProjectionScale = ReadValueSingle(root, defaults.PoweredLateralForceForwardProjectionScale, "steeringAssist", "poweredLateralForceForwardProjectionScale"),
            LowSpeedPivotSpeedEndMetersPerSecond = ReadValueSingle(root, defaults.LowSpeedPivotSpeedEndMetersPerSecond, "steeringAssist", "lowSpeedPivotSpeedEndMetersPerSecond"),
            LowSpeedPivotSteerStart = ReadValueSingle(root, defaults.LowSpeedPivotSteerStart, "steeringAssist", "lowSpeedPivotSteerStart"),
            LowSpeedPivotRearLateralMultiplier = ReadValueSingle(root, defaults.LowSpeedPivotRearLateralMultiplier, "steeringAssist", "lowSpeedPivotRearLateralMultiplier"),
            LowSpeedPivotYawResponse = ReadValueSingle(root, defaults.LowSpeedPivotYawResponse, "steeringAssist", "lowSpeedPivotYawResponse"),
            LowSpeedPivotMaxYawRateDegreesPerSecond = ReadValueSingle(root, defaults.LowSpeedPivotMaxYawRateDegreesPerSecond, "steeringAssist", "lowSpeedPivotMaxYawRateDegreesPerSecond"),
            InputBrakeAuthorityStart = ReadValueSingle(root, defaults.InputBrakeAuthorityStart, "steeringAssist", "inputBrakeAuthorityStart"),
            InputBrakeAuthorityEnd = ReadValueSingle(root, defaults.InputBrakeAuthorityEnd, "steeringAssist", "inputBrakeAuthorityEnd"),
            InputBrakeAuthoritySpeedStartMetersPerSecond = ReadValueSingle(root, defaults.InputBrakeAuthoritySpeedStartMetersPerSecond, "steeringAssist", "inputBrakeAuthoritySpeedStartMetersPerSecond"),
            InputBrakeAuthoritySpeedEndMetersPerSecond = ReadValueSingle(root, defaults.InputBrakeAuthoritySpeedEndMetersPerSecond, "steeringAssist", "inputBrakeAuthoritySpeedEndMetersPerSecond"),
            BrakingInputMultiplierFloor = ReadValueSingle(root, defaults.BrakingInputMultiplierFloor, "steeringAssist", "brakingInputMultiplierFloor"),
            BrakingReturnMultiplierFloor = ReadValueSingle(root, defaults.BrakingReturnMultiplierFloor, "steeringAssist", "brakingReturnMultiplierFloor"),
            BrakingInputRateBoost = ReadValueSingle(root, defaults.BrakingInputRateBoost, "steeringAssist", "brakingInputRateBoost"),
            RecentBrakeBoostThreshold = ReadValueSingle(root, defaults.RecentBrakeBoostThreshold, "steeringAssist", "recentBrakeBoostThreshold"),
            RecentBrakeBoostSeconds = ReadValueSingle(root, defaults.RecentBrakeBoostSeconds, "steeringAssist", "recentBrakeBoostSeconds"),
            RecentBrakeAuthority = ReadValueSingle(root, defaults.RecentBrakeAuthority, "steeringAssist", "recentBrakeAuthority")
        };
    }

    private static TyreForceTuningParameters ReadTyreForce(JsonElement root)
    {
        TyreForceTuningParameters defaults = new();
        return new TyreForceTuningParameters
        {
            SlidingForceFloor = ReadValueSingle(root, defaults.SlidingForceFloor, "tyreForce", "slidingForceFloor"),
            ScrubDragLimitMultiplier = ReadValueSingle(root, defaults.ScrubDragLimitMultiplier, "tyreForce", "scrubDragLimitMultiplier"),
            LateralLongitudinalGripCoupling = ReadValueSingle(root, defaults.LateralLongitudinalGripCoupling, "tyreForce", "lateralLongitudinalGripCoupling"),
            CorneringSpeedRetention = ReadValueSingle(root, defaults.CorneringSpeedRetention, "tyreForce", "corneringSpeedRetention"),
            CorneringSpeedRetentionSteerStart = ReadValueSingle(root, defaults.CorneringSpeedRetentionSteerStart, "tyreForce", "corneringSpeedRetentionSteerStart"),
            CorneringSpeedRetentionSteerEnd = ReadValueSingle(root, defaults.CorneringSpeedRetentionSteerEnd, "tyreForce", "corneringSpeedRetentionSteerEnd"),
            ScrubRpmIsolationSlipStart = ReadValueSingle(root, defaults.ScrubRpmIsolationSlipStart, "tyreForce", "scrubRpmIsolationSlipStart"),
            ScrubRpmIsolationSlipEnd = ReadValueSingle(root, defaults.ScrubRpmIsolationSlipEnd, "tyreForce", "scrubRpmIsolationSlipEnd"),
            ScrubRpmIsolationMaximumSpeedDropMetersPerSecond = ReadValueSingle(root, defaults.ScrubRpmIsolationMaximumSpeedDropMetersPerSecond, "tyreForce", "scrubRpmIsolationMaximumSpeedDropMetersPerSecond")
        };
    }

    private static RpmResponseParameters ReadRpmResponse(JsonElement root)
    {
        RpmResponseParameters defaults = new();
        return new RpmResponseParameters
        {
            PoweredAntiDipWindowRpm = ReadValueSingle(root, defaults.PoweredAntiDipWindowRpm, "rpmResponse", "poweredAntiDipWindowRpm"),
            PoweredAntiDipFallRateRpmPerSecond = ReadValueSingle(root, defaults.PoweredAntiDipFallRateRpmPerSecond, "rpmResponse", "poweredAntiDipFallRateRpmPerSecond")
        };
    }

    private static ClassicBicycleParameters ReadClassicBicycle(JsonElement root)
    {
        return ReadClassicHandling(root, "classicBicycle", "classicBicycle");
    }

    private static ClassicBicycleParameters ReadClassicFourWheel(JsonElement root)
    {
        return ReadClassicHandling(root, "classicFourWheel", "classicBicycle");
    }

    private static ClassicBicycleParameters ReadClassicHandling(JsonElement root, string sectionName, string fallbackSectionName)
    {
        ClassicBicycleParameters defaults = new();
        return new ClassicBicycleParameters
        {
            Steering = new ClassicBicycleSteeringParameters
            {
                ZeroKmhAngleDegrees = ReadValueSingleWithFallback(root, defaults.Steering.ZeroKmhAngleDegrees, sectionName, fallbackSectionName, "steering", "zeroKmhAngleDegrees"),
                SixtyKmhAngleDegrees = ReadValueSingleWithFallback(root, defaults.Steering.SixtyKmhAngleDegrees, sectionName, fallbackSectionName, "steering", "sixtyKmhAngleDegrees"),
                OneTwentyKmhAngleDegrees = ReadValueSingleWithFallback(root, defaults.Steering.OneTwentyKmhAngleDegrees, sectionName, fallbackSectionName, "steering", "oneTwentyKmhAngleDegrees"),
                TwoHundredKmhAngleDegrees = ReadValueSingleWithFallback(root, defaults.Steering.TwoHundredKmhAngleDegrees, sectionName, fallbackSectionName, "steering", "twoHundredKmhAngleDegrees"),
                SteerSpeedDegreesPerSecond = ReadValueSingleWithFallback(root, defaults.Steering.SteerSpeedDegreesPerSecond, sectionName, fallbackSectionName, "steering", "steerSpeedDegreesPerSecond"),
                ReturnSpeedDegreesPerSecond = ReadValueSingleWithFallback(root, defaults.Steering.ReturnSpeedDegreesPerSecond, sectionName, fallbackSectionName, "steering", "returnSpeedDegreesPerSecond")
            },
            FrontTyres = ReadClassicTyres(root, defaults.FrontTyres, sectionName, fallbackSectionName, "frontTyres"),
            RearTyres = ReadClassicTyres(root, defaults.RearTyres, sectionName, fallbackSectionName, "rearTyres"),
            Yaw = new ClassicBicycleYawParameters
            {
                InertiaScale = ReadValueSingleWithFallback(root, defaults.Yaw.InertiaScale, sectionName, fallbackSectionName, "yaw", "inertiaScale"),
                Damping = ReadValueSingleWithFallback(root, defaults.Yaw.Damping, sectionName, fallbackSectionName, "yaw", "damping"),
                LateralVelocityDamping = ReadValueSingleWithFallback(root, defaults.Yaw.LateralVelocityDamping, sectionName, fallbackSectionName, "yaw", "lateralVelocityDamping")
            },
            GripBudget = new ClassicBicycleGripBudgetParameters
            {
                CombinedGripExponent = ReadValueSingleWithFallback(root, defaults.GripBudget.CombinedGripExponent, sectionName, fallbackSectionName, "gripBudget", "combinedGripExponent")
            },
            LowSpeed = new ClassicBicycleLowSpeedParameters
            {
                SlipSpeedFloorMetersPerSecond = ReadValueSingleWithFallback(root, defaults.LowSpeed.SlipSpeedFloorMetersPerSecond, sectionName, fallbackSectionName, "lowSpeed", "slipSpeedFloorMetersPerSecond")
            },
            Resistance = new ClassicBicycleResistanceParameters
            {
                RollingResistanceMultiplier = ReadValueSingleWithFallback(root, defaults.Resistance.RollingResistanceMultiplier, sectionName, fallbackSectionName, "resistance", "rollingResistanceMultiplier"),
                AeroDragMultiplier = ReadValueSingleWithFallback(root, defaults.Resistance.AeroDragMultiplier, sectionName, fallbackSectionName, "resistance", "aeroDragMultiplier")
            }
        };
    }

    private static ClassicBicycleTyreParameters ReadClassicBicycleTyres(
        JsonElement root,
        ClassicBicycleTyreParameters defaults,
        string axleName)
    {
        return ReadClassicTyres(root, defaults, "classicBicycle", "classicBicycle", axleName);
    }

    private static ClassicBicycleTyreParameters ReadClassicTyres(
        JsonElement root,
        ClassicBicycleTyreParameters defaults,
        string sectionName,
        string fallbackSectionName,
        string axleName)
    {
        return new ClassicBicycleTyreParameters
        {
            CorneringStiffness = ReadValueSingleWithFallback(root, defaults.CorneringStiffness, sectionName, fallbackSectionName, axleName, "corneringStiffness"),
            PeakSlipAngleDegrees = ReadValueSingleWithFallback(root, defaults.PeakSlipAngleDegrees, sectionName, fallbackSectionName, axleName, "peakSlipAngleDegrees"),
            FalloffSlipAngleDegrees = ReadValueSingleWithFallback(root, defaults.FalloffSlipAngleDegrees, sectionName, fallbackSectionName, axleName, "falloffSlipAngleDegrees"),
            MaxGrip = ReadValueSingleWithFallback(root, defaults.MaxGrip, sectionName, fallbackSectionName, axleName, "maxGrip"),
            SlidingGrip = ReadValueSingleWithFallback(root, defaults.SlidingGrip, sectionName, fallbackSectionName, axleName, "slidingGrip")
        };
    }

    private static float ReadValueSingleWithFallback(
        JsonElement root,
        float defaultValue,
        string sectionName,
        string fallbackSectionName,
        params string[] remainingPath)
    {
        string[] primaryPath = new string[remainingPath.Length + 1];
        primaryPath[0] = sectionName;
        Array.Copy(remainingPath, 0, primaryPath, 1, remainingPath.Length);
        float primary = ReadValueSingle(root, float.NaN, primaryPath);
        if (!float.IsNaN(primary))
        {
            return primary;
        }

        string[] fallbackPath = new string[remainingPath.Length + 1];
        fallbackPath[0] = fallbackSectionName;
        Array.Copy(remainingPath, 0, fallbackPath, 1, remainingPath.Length);
        return ReadValueSingle(root, defaultValue, fallbackPath);
    }

    private static string ResolveDataPath(string path)
    {
        if (Path.IsPathRooted(path) && File.Exists(path))
        {
            return path;
        }

        string[] candidates =
        [
            Path.Combine(Environment.CurrentDirectory, path),
            Path.Combine(AppContext.BaseDirectory, path)
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Simulation engine definition JSON was not found: {path}", path);
    }

    private static float ReadValueSingle(JsonElement root, float fallback, params string[] path)
    {
        if (!TryGet(root, out JsonElement element, path))
        {
            return fallback;
        }

        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("value", out JsonElement valueElement))
        {
            return ReadSingle(valueElement, fallback);
        }

        return ReadSingle(element, fallback);
    }

    private static int ReadValueInt(JsonElement root, int fallback, params string[] path)
    {
        if (!TryGet(root, out JsonElement element, path))
        {
            return fallback;
        }

        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("value", out JsonElement valueElement))
        {
            element = valueElement;
        }

        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int value)
            ? value
            : fallback;
    }

    private static bool ReadValueBool(JsonElement root, bool fallback, params string[] path)
    {
        if (!TryGet(root, out JsonElement element, path))
        {
            return fallback;
        }

        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("value", out JsonElement valueElement))
        {
            element = valueElement;
        }

        return element.ValueKind == JsonValueKind.True
            ? true
            : element.ValueKind == JsonValueKind.False
                ? false
                : fallback;
    }

    private static string ReadValueString(JsonElement root, string fallback, params string[] path)
    {
        if (!TryGet(root, out JsonElement element, path))
        {
            return fallback;
        }

        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("value", out JsonElement valueElement))
        {
            element = valueElement;
        }

        return element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? fallback
            : fallback;
    }

    private static float ReadSingle(JsonElement root, float fallback)
    {
        return root.ValueKind == JsonValueKind.Number && root.TryGetSingle(out float value)
            ? value
            : fallback;
    }

    private static bool TryGet(JsonElement root, out JsonElement element, params string[] path)
    {
        element = root;
        foreach (string segment in path)
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty(segment, out element))
            {
                return false;
            }
        }

        return true;
    }
}
