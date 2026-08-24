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
            Timing = ReadTiming(root),
            VehicleSafety = ReadVehicleSafety(root),
            StabilityAssist = ReadStabilityAssist(root),
            DigitalThrottleAssist = ReadDigitalThrottleAssist(root),
            DigitalBrakeAssist = ReadDigitalBrakeAssist(root),
            BrakeThrottlePriority = ReadBrakeThrottlePriority(root),
            SteeringAssist = ReadSteeringAssist(root),
            RpmResponse = ReadRpmResponse(root)
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

    private static RpmResponseParameters ReadRpmResponse(JsonElement root)
    {
        RpmResponseParameters defaults = new();
        return new RpmResponseParameters
        {
            PoweredAntiDipWindowRpm = ReadValueSingle(root, defaults.PoweredAntiDipWindowRpm, "rpmResponse", "poweredAntiDipWindowRpm"),
            PoweredAntiDipFallRateRpmPerSecond = ReadValueSingle(root, defaults.PoweredAntiDipFallRateRpmPerSecond, "rpmResponse", "poweredAntiDipFallRateRpmPerSecond")
        };
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
