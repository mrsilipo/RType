using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using RType.Vehicle;

namespace RType.Data;

internal static class EngineAssemblyResolver
{
    private const string EngineCatalogIndexPath = "Data/Parts/Engine/part_catalog_index.json";
    private const string EngineTuneIndexPath = "Data/Tunes/Engine/engine_tunes.json";
    private const string EngineFuelCatalogPath = "Data/Tunes/Engine/fuels.json";

    public static ResolvedEngineAssembly Resolve(JsonElement engine)
    {
        CatalogLookup catalogs = CatalogLookup.Load(EngineCatalogIndexPath, EngineTuneIndexPath, EngineFuelCatalogPath);
        string engineId = ReadString(engine, string.Empty, "engineId");
        string blockId = ReadString(engine, string.Empty, "blockId");
        string headId = ReadString(engine, string.Empty, "headId");
        string tuneId = ReadString(engine, string.Empty, "tuneId");
        string fuelId = ReadFuelId(engine);
        JsonElement engineCatalog = catalogs.Require(engineId);
        JsonElement block = catalogs.Require(blockId);
        JsonElement head = catalogs.Require(headId);
        JsonElement tune = catalogs.Require(tuneId);
        JsonElement fuel = catalogs.Require(fuelId);
        string requestedCombinationId = ReadString(engine, string.Empty, "combinationId");
        JsonElement? combination = ResolveEngineCombination(catalogs, requestedCombinationId, blockId, headId);

        Dictionary<string, string> installedParts = ReadInstalledParts(engineCatalog, engine);
        List<EngineAssemblyValidationMessage> messages = [];
        string family = ReadString(engineCatalog, string.Empty, "family");
        string blockFamily = ReadString(block, string.Empty, "family");
        string headFamily = ReadString(head, string.Empty, "family");
        bool headVtec = ReadBoolean(head, false, "vtec");

        if (combination.HasValue)
        {
            messages.Add(Info("supported_engine_combination", $"Using authored engine combination {ReadString(combination.Value, string.Empty, "id")}: {ReadString(combination.Value, string.Empty, "displayName")}."));
            ValidateCombinationPair(combination.Value, blockId, headId, messages);
        }
        else if (IsNonFactoryEngineCombination(engineCatalog, blockId, headId))
        {
            messages.Add(Info("unapproved_engine_combination", $"Block {blockId} and head {headId} are not a factory pair or authored Frankenstein combination yet."));
        }

        if (!string.Equals(blockFamily, headFamily, StringComparison.OrdinalIgnoreCase))
        {
            messages.Add(Info("frankenstein_family_mix", $"Frankenstein engine: block family {blockFamily} with head family {headFamily}."));
        }

        if (!string.Equals(family, blockFamily, StringComparison.OrdinalIgnoreCase))
        {
            messages.Add(Warning("engine_block_family_mismatch", $"Engine {engineId} declares family {family}, but block {blockId} declares {blockFamily}."));
        }

        if (!string.Equals(ReadString(engineCatalog, string.Empty, "blockId"), blockId, StringComparison.OrdinalIgnoreCase))
        {
            messages.Add(Info("non_factory_block", $"Engine {engineId} factory block is {ReadString(engineCatalog, string.Empty, "blockId")}; build installs {blockId}."));
        }

        if (!string.Equals(ReadString(engineCatalog, string.Empty, "headId"), headId, StringComparison.OrdinalIgnoreCase))
        {
            messages.Add(Info("non_factory_head", $"Engine {engineId} factory head is {ReadString(engineCatalog, string.Empty, "headId")}; build installs {headId}."));
        }

        ValidateBlockHeadRules(block, head, messages);

        float baseDisplacementCc = FirstPositive(ReadSingle(engineCatalog, 0f, "displacementCc"), ReadSingle(block, 0f, "displacementCc"));
        EngineAssemblyDraft draft = new()
        {
            EngineId = engineId,
            EngineCombinationId = combination.HasValue ? ReadString(combination.Value, string.Empty, "id") : string.Empty,
            EngineCombinationDisplayName = combination.HasValue ? ReadString(combination.Value, string.Empty, "displayName") : string.Empty,
            EngineCode = ReadString(engineCatalog, string.Empty, "code"),
            DisplayName = ReadString(engineCatalog, engineId, "displayName"),
            Family = family,
            BlockId = blockId,
            BlockFamily = blockFamily,
            HeadId = headId,
            HeadFamily = headFamily,
            Valvetrain = ReadString(head, string.Empty, "valvetrain"),
            TuneId = tuneId,
            TuneTier = ReadString(tune, "stock", "tier"),
            FuelId = fuelId,
            FuelDisplayName = ReadString(fuel, fuelId, "displayName"),
            FuelOctaneRon = ReadSingle(fuel, 0f, "data", "octaneRon"),
            FuelEthanolContent = ReadSingle(fuel, 0f, "data", "ethanolContent"),
            FuelSafeCompressionRatio = ReadSingle(fuel, 0f, "data", "safeCompressionRatio"),
            FuelRequiresRetune = ReadBoolean(fuel, false, "data", "requiresRetune"),
            FuelBasePowerMultiplier = ReadSingle(fuel, 1f, "data", "basePowerMultiplier"),
            FuelHighCompressionPowerMultiplier = ReadSingle(fuel, 1f, "data", "highCompressionPowerMultiplier"),
            FuelHighCompressionStartsAt = ReadSingle(fuel, 12f, "data", "highCompressionStartsAt"),
            InstalledParts = installedParts,
            MassComponents = [],
            EstimatedAssemblyMassKg = ReadWeight(engineCatalog) + ReadWeight(block) + ReadWeight(head),
            BaseDisplacementCc = baseDisplacementCc,
            DisplacementCc = baseDisplacementCc,
            BoreMm = ReadSingle(block, 0f, "boreMm"),
            StrokeMm = ReadSingle(block, 0f, "strokeMm"),
            RodLengthMm = ReadSingle(block, 0f, "rodLengthMm"),
            BaseCompressionRatio = FirstPositive(ReadSingle(engineCatalog, 0f, "compressionRatio"), ReadSingle(block, 0f, "defaultCompressionRatio")),
            CompressionRatio = FirstPositive(ReadSingle(engineCatalog, 0f, "compressionRatio"), ReadSingle(block, 0f, "defaultCompressionRatio")),
            IdleRpm = ReadSingle(engineCatalog, 900f, "idleRpm"),
            PowerRedlineRpm = ReadSingle(engineCatalog, 8200f, "redlineRpm"),
            LimiterHardCutRpm = ReadSingle(engineCatalog, 8400f, "limiterRpm"),
            LimiterResumeRpm = MathF.Max(0f, ReadSingle(engineCatalog, 0f, "limiterResumeRpm")),
            MaxGaugeRpm = ReadSingle(engineCatalog, 0f, "maxGaugeRpm"),
            LimiterFuelCutSeconds = 0.34f,
            LimiterRestoreSeconds = 0.41f,
            LimiterCutTorqueMultiplier = 0f,
            RotationalInertiaKgM2 = ReadSingle(block, 0.22f, "rotationalInertiaKgM2"),
            BaseRotationalInertiaKgM2 = ReadSingle(block, 0.22f, "rotationalInertiaKgM2"),
            VtecEnabled = (ReadBoolean(engineCatalog, false, "vtec") ||
                (combination.HasValue && ReadBoolean(combination.Value, false, "vtec"))) && headVtec,
            VtecActivationRpm = ReadSingle(engineCatalog, 0f, "vtecActivationRpm"),
            VtecTransitionWidthRpm = 350f,
            LowCamFlowMultiplier = 1f,
            HighCamFlowMultiplier = 1.22f,
            IntakeFlowScale = 1f,
            ExhaustFlowScale = 1f,
            ThrottleGamma = 2f,
            ClutchCouplingRate = 13f,
            ClutchEngagementSharpness = 1f,
            ClutchSlipDamping = 1f,
            ClutchLowSpeedAssistStrength = 0.65f,
            ClutchBiteInputStartMultiplier = 0.35f,
            ClutchLaunchAssistExponent = 0.55f,
            ClutchLowSpeedThrottleGamma = 0.65f,
            ClutchLowSpeedThrottleAssist = 0.45f,
            ClutchLowSpeedTorqueAssistNm = 55f,
            ClutchRollingLockSpeedMetersPerSecond = 0.85f,
            ClutchRollingLockSlipRadiansPerSecond = 115f,
            TorqueCurve = ReadTorqueCurve(engineCatalog, "baselineTorqueCurveNm"),
            AuthoredResolvedTorqueCurve = [],
            EngineBrakeTorqueCurve = ReadTorqueCurve(engineCatalog, "baselineEngineBrakeTorqueCurveNm")
        };

        AddEngineMassComponent(ref draft, engineCatalog, "engine_definition", draft.EngineId);
        AddEngineMassComponent(ref draft, block, "block", blockId);
        AddEngineMassComponent(ref draft, head, "head", headId);

        if (draft.TorqueCurve.Length == 0)
        {
            messages.Add(Warning("engine_baseline_torque_curve_missing", $"Engine {engineId} does not define baselineTorqueCurveNm; runtime will need an authored curve before it is production-ready."));
        }

        if (draft.EngineBrakeTorqueCurve.Length == 0)
        {
            messages.Add(Info("engine_brake_curve_synthesized", $"Engine {engineId} does not define baselineEngineBrakeTorqueCurveNm; using synthesized closed-throttle drag curve."));
        }

        foreach ((string slot, string partId) in installedParts)
        {
            JsonElement part = catalogs.Require(partId);
            float partWeight = ReadWeight(part);
            draft.EstimatedAssemblyMassKg += partWeight;
            AddEngineMassComponent(ref draft, part, slot, partId, partWeight);
            ValidateInstalledPartCatalogSlot(catalogs, partId, slot, messages);
            if (!IsCompatible(part, family))
            {
                messages.Add(Warning("part_family_incompatible", $"Part {partId} in slot {slot} does not list compatibility with {family}."));
            }

            ValidatePartAgainstHead(part, partId, slot, headVtec, messages);
            ApplyPartModifiers(part, ref draft);
            ValidatePartRequirements(part, partId, slot, draft, installedParts, messages);
        }

        if (combination.HasValue)
        {
            ValidatePartRequirements(combination.Value, draft.EngineCombinationId, "engineCombination", draft, installedParts, messages);
            ValidateCombinationTuneRecommendation(combination.Value, draft.TuneId, messages);
            ApplyPartModifiers(combination.Value, ref draft);
        }

        if (!IsCompatible(tune, family))
        {
            messages.Add(Warning("tune_family_incompatible", $"Tune {tuneId} does not list compatibility with {family}."));
        }

        if (!IsIntendedForEngine(tune, engineId))
        {
            messages.Add(Info("tune_not_factory_intended", $"Tune {tuneId} is not listed as intended for {engineId}; treating as a deliberate custom/frankenstein calibration."));
        }

        ValidateTuneFuelIntent(tune, tuneId, fuelId, messages);

        ApplyPartModifiers(tune, ref draft);
        ResolveFuelEffects(ref draft, messages);

        if (draft.VtecEnabled && !headVtec)
        {
            messages.Add(Warning("vtec_requested_on_non_vtec_head", $"VTEC was requested by engine/tune/cams, but head {headId} is non-VTEC."));
            draft.VtecEnabled = false;
            draft.VtecActivationRpm = 0f;
        }

        if (draft.ValveSpringSafeContinuousRpm > 0f && draft.PowerRedlineRpm > draft.ValveSpringSafeContinuousRpm)
        {
            messages.Add(Warning("redline_over_valve_spring_safe_limit", $"Power redline {draft.PowerRedlineRpm:0} rpm exceeds valve spring safe continuous limit {draft.ValveSpringSafeContinuousRpm:0} rpm."));
        }

        if (draft.ValveSpringFloatStartRpm > 0f && draft.LimiterHardCutRpm > draft.ValveSpringFloatStartRpm)
        {
            messages.Add(Warning("limiter_over_valve_float_start", $"Limiter {draft.LimiterHardCutRpm:0} rpm exceeds valve float start {draft.ValveSpringFloatStartRpm:0} rpm."));
        }

        if (draft.LimiterResumeRpm <= 0f)
        {
            draft.LimiterResumeRpm = MathF.Max(draft.IdleRpm, draft.LimiterHardCutRpm - 175f);
        }

        EngineTorqueCompositionInput torqueCompositionInput = CreateTorqueCompositionInput(draft);
        EngineBrakeCompositionInput engineBrakeCompositionInput = CreateEngineBrakeCompositionInput(draft);
        TorqueCurvePoint[] composedTorqueCurve = EnginePowerComposer.ResolveDriveTorqueCurve(torqueCompositionInput);
        if (draft.AuthoredResolvedTorqueCurve.Length > 0)
        {
            draft.TorqueCurve = draft.AuthoredResolvedTorqueCurve;
            messages.Add(Info("authored_resolved_torque_curve", $"Tune {draft.TuneId} supplies a resolved torque curve for the complete engine build."));
        }
        else
        {
            draft.TorqueCurve = composedTorqueCurve;
        }

        draft.EngineBrakeTorqueCurve = EnginePowerComposer.ResolveEngineBrakeTorqueCurve(engineBrakeCompositionInput);
        draft.PowerComposition = EnginePowerComposer.ResolveCompositionTrace(
            torqueCompositionInput,
            engineBrakeCompositionInput,
            draft.TorqueCurve,
            draft.EngineBrakeTorqueCurve);
        return draft.ToResolved(messages);
    }

    private static void ResolveFuelEffects(ref EngineAssemblyDraft draft, List<EngineAssemblyValidationMessage> messages)
    {
        if (draft.FuelSafeCompressionRatio > 0f && draft.CompressionRatio > draft.FuelSafeCompressionRatio)
        {
            messages.Add(Warning("fuel_octane_insufficient", $"Fuel {draft.FuelId} safe compression is {draft.FuelSafeCompressionRatio:0.0}:1, but build compression is {draft.CompressionRatio:0.0}:1."));
        }

        if (draft.FuelRequiresRetune && !draft.TuneId.Contains("e85", StringComparison.OrdinalIgnoreCase))
        {
            messages.Add(Info("fuel_retune_recommended", $"Fuel {draft.FuelId} benefits from a matching calibration tune; current tune is {draft.TuneId}."));
        }

        draft.FuelEffectivePowerMultiplier = EnginePowerComposer.ResolveFuelEffectivePowerMultiplier(new EngineFuelCompositionInput(
            draft.CompressionRatio,
            draft.FuelBasePowerMultiplier,
            draft.FuelHighCompressionPowerMultiplier,
            draft.FuelHighCompressionStartsAt));
    }

    private static EngineTorqueCompositionInput CreateTorqueCompositionInput(EngineAssemblyDraft draft)
    {
        return new EngineTorqueCompositionInput(
            draft.TorqueCurve,
            draft.BaseDisplacementCc,
            draft.DisplacementCc,
            draft.BaseCompressionRatio,
            draft.CompressionRatio,
            draft.VtecEnabled,
            draft.VtecActivationRpm,
            draft.VtecTransitionWidthRpm,
            draft.LowCamFlowMultiplier,
            draft.HighCamFlowMultiplier,
            draft.IntakeFlowScale,
            draft.ExhaustFlowScale,
            draft.FuelEffectivePowerMultiplier);
    }

    private static EngineBrakeCompositionInput CreateEngineBrakeCompositionInput(EngineAssemblyDraft draft)
    {
        return new EngineBrakeCompositionInput(
            draft.EngineBrakeTorqueCurve,
            draft.BaseDisplacementCc,
            draft.DisplacementCc,
            draft.BaseCompressionRatio,
            draft.CompressionRatio,
            draft.BaseRotationalInertiaKgM2,
            draft.RotationalInertiaKgM2,
            draft.IdleRpm,
            draft.PowerRedlineRpm,
            draft.LimiterHardCutRpm);
    }

    private static void ApplyPartModifiers(JsonElement part, ref EngineAssemblyDraft draft)
    {
        if (TryGet(part, out JsonElement data, "data"))
        {
            draft.ClutchTorqueCapacityNm = ReadSingle(data, draft.ClutchTorqueCapacityNm, "torqueCapacityNm");
            draft.ClutchBitePoint = ReadSingle(data, draft.ClutchBitePoint, "bitePoint");
            draft.ClutchCouplingRate = ReadSingle(data, draft.ClutchCouplingRate, "couplingRate");
            draft.ClutchEngagementSharpness = ReadSingle(data, draft.ClutchEngagementSharpness, "engagementSharpness");
            draft.ClutchSlipDamping = ReadSingle(data, draft.ClutchSlipDamping, "slipDamping");
            draft.ClutchLowSpeedAssistStrength = ReadSingle(data, draft.ClutchLowSpeedAssistStrength, "lowSpeedAssistStrength");
            draft.ClutchBiteInputStartMultiplier = ReadSingle(data, draft.ClutchBiteInputStartMultiplier, "biteInputStartMultiplier");
            draft.ClutchLaunchAssistExponent = ReadSingle(data, draft.ClutchLaunchAssistExponent, "launchAssistExponent");
            draft.ClutchLowSpeedThrottleGamma = ReadSingle(data, draft.ClutchLowSpeedThrottleGamma, "lowSpeedThrottleGamma");
            draft.ClutchLowSpeedThrottleAssist = ReadSingle(data, draft.ClutchLowSpeedThrottleAssist, "lowSpeedThrottleAssist");
            draft.ClutchLowSpeedTorqueAssistNm = ReadSingle(data, draft.ClutchLowSpeedTorqueAssistNm, "lowSpeedTorqueAssistNm");
            draft.ClutchRollingLockSpeedMetersPerSecond = ReadSingle(data, draft.ClutchRollingLockSpeedMetersPerSecond, "rollingLockSpeedMetersPerSecond");
            draft.ClutchRollingLockSlipRadiansPerSecond = ReadSingle(data, draft.ClutchRollingLockSlipRadiansPerSecond, "rollingLockSlipRadiansPerSecond");
            draft.ValveSpringFloatStartRpm = ReadSingle(data, draft.ValveSpringFloatStartRpm, "floatStartRpm");
            draft.ValveSpringSafeContinuousRpm = ReadSingle(data, draft.ValveSpringSafeContinuousRpm, "safeContinuousRpm");
        }

        if (!TryGet(part, out JsonElement modifies, "modifies"))
        {
            return;
        }

        if (TryGet(modifies, out JsonElement engine, "engine"))
        {
            draft.DisplacementCc = ReadSingle(engine, draft.DisplacementCc, "displacementCc");
            draft.BoreMm = ReadSingle(engine, draft.BoreMm, "boreMm");
            draft.StrokeMm = ReadSingle(engine, draft.StrokeMm, "strokeMm");
            draft.CompressionRatio = ReadSingle(engine, draft.CompressionRatio, "compressionRatio");
            draft.RotationalInertiaKgM2 = ReadSingle(engine, draft.RotationalInertiaKgM2, "rotationalInertiaKgM2");
            draft.IdleRpm = ReadSingle(engine, draft.IdleRpm, "idleRpm");
            draft.PowerRedlineRpm = ReadSingle(engine, draft.PowerRedlineRpm, "redlineRpm");
            draft.LimiterHardCutRpm = ReadSingle(engine, draft.LimiterHardCutRpm, "limiterRpm");
            draft.LimiterResumeRpm = ReadSingle(engine, draft.LimiterResumeRpm, "limiterResumeRpm");
            draft.MaxGaugeRpm = ReadSingle(engine, draft.MaxGaugeRpm, "maxGaugeRpm");
            TorqueCurvePoint[] resolvedTorqueCurve = ReadTorqueCurve(engine, "resolvedTorqueCurveNm");
            if (resolvedTorqueCurve.Length > 0)
            {
                draft.AuthoredResolvedTorqueCurve = resolvedTorqueCurve;
            }

            draft.ThrottleGamma = ReadSingle(engine, draft.ThrottleGamma, "throttleGamma");
        }

        if (TryGet(modifies, out JsonElement limiter, "limiter"))
        {
            draft.LimiterFuelCutSeconds = ReadSingle(limiter, draft.LimiterFuelCutSeconds, "fuelCutSeconds");
            draft.LimiterRestoreSeconds = ReadSingle(limiter, draft.LimiterRestoreSeconds, "restoreSeconds");
            draft.LimiterCutTorqueMultiplier = ReadSingle(limiter, draft.LimiterCutTorqueMultiplier, "cutTorqueMultiplier");
        }

        if (TryGet(modifies, out JsonElement ignition, "ignition"))
        {
            draft.LimiterFuelCutSeconds = ReadSingle(ignition, draft.LimiterFuelCutSeconds, "limiterDurationSeconds");
        }

        if (TryGet(modifies, out JsonElement vtec, "vtec"))
        {
            draft.VtecEnabled = ReadBoolean(vtec, draft.VtecEnabled, "enabled");
            draft.VtecActivationRpm = ReadSingle(vtec, draft.VtecActivationRpm, "activationRpm");
            draft.VtecTransitionWidthRpm = ReadSingle(vtec, draft.VtecTransitionWidthRpm, "transitionWidthRpm");
        }

        if (TryGet(modifies, out JsonElement lowCam, "lowCam"))
        {
            draft.LowCamFlowMultiplier = ReadSingle(lowCam, draft.LowCamFlowMultiplier, "flowMultiplier");
        }

        if (TryGet(modifies, out JsonElement highCam, "highCam"))
        {
            draft.HighCamFlowMultiplier = ReadSingle(highCam, draft.HighCamFlowMultiplier, "flowMultiplier");
        }

        if (TryGet(modifies, out JsonElement head, "head"))
        {
            draft.IntakeFlowScale = ReadSingle(head, draft.IntakeFlowScale, "intakeFlowScale");
            draft.ExhaustFlowScale = ReadSingle(head, draft.ExhaustFlowScale, "exhaustFlowScale");
        }

        if (TryGet(modifies, out JsonElement audio, "audio"))
        {
            draft.EngineAudioDspId = ReadString(part, draft.EngineAudioDspId, "id");
            draft.EngineAudioDspDisplayName = ReadString(part, draft.EngineAudioDspDisplayName, "displayName");
            draft.EngineAudioProfilePath = ReadString(audio, draft.EngineAudioProfilePath, "engineAudioProfilePath");
            draft.EngineAudioProfileEngineId = ReadString(audio, draft.EngineAudioProfileEngineId, "profileEngineId");
            draft.EngineAudioProfileEngineFamily = ReadString(audio, draft.EngineAudioProfileEngineFamily, "profileEngineFamily");
            draft.EngineAudioFallbackAllowed = ReadBoolean(audio, draft.EngineAudioFallbackAllowed, "fallbackAllowed");
            draft.EngineAudioSourceRecordingPath = ReadString(audio, draft.EngineAudioSourceRecordingPath, "sourceRecordingPath");
            draft.EngineAudioGenerationMethod = ReadString(audio, draft.EngineAudioGenerationMethod, "generationMethod");
            draft.EngineAudioGeneratedSampleSetPath = ReadString(audio, draft.EngineAudioGeneratedSampleSetPath, "generatedSampleSetPath");
        }
    }

    private static Dictionary<string, string> ReadInstalledParts(JsonElement engineCatalog, JsonElement engine)
    {
        Dictionary<string, string> installedParts = new(StringComparer.OrdinalIgnoreCase);
        AddInstalledParts(installedParts, engineCatalog, "defaultInstalledParts");
        AddInstalledParts(installedParts, engine, "installedParts");
        return installedParts;
    }

    private static void AddInstalledParts(Dictionary<string, string> installedParts, JsonElement root, string propertyName)
    {
        if (!TryGet(root, out JsonElement parts, propertyName) ||
            parts.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty part in parts.EnumerateObject())
        {
            if (part.Value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(part.Value.GetString()))
            {
                installedParts[part.Name] = part.Value.GetString()!;
            }
        }
    }

    private static string ReadFuelId(JsonElement engine)
    {
        if (!TryGet(engine, out JsonElement fuel, "fuel"))
        {
            return "fuel_98ron";
        }

        return ReadString(fuel, ReadString(fuel, "fuel_98ron", "default"), "selected");
    }

    private static EngineAssemblyValidationMessage Info(string code, string message)
    {
        return new EngineAssemblyValidationMessage(EngineAssemblyValidationSeverity.Info, code, message);
    }

    private static EngineAssemblyValidationMessage Warning(string code, string message)
    {
        return new EngineAssemblyValidationMessage(EngineAssemblyValidationSeverity.Warning, code, message);
    }

    private static bool IsCompatible(JsonElement part, string family)
    {
        if (!TryGet(part, out JsonElement compatibility, "compatibility"))
        {
            string itemFamily = ReadString(part, string.Empty, "family");
            return string.IsNullOrWhiteSpace(itemFamily) ||
                itemFamily.Equals(family, StringComparison.OrdinalIgnoreCase);
        }

        if (compatibility.ValueKind == JsonValueKind.String)
        {
            return compatibility.GetString()?.Equals(family, StringComparison.OrdinalIgnoreCase) == true;
        }

        return compatibility.ValueKind != JsonValueKind.Array ||
            compatibility.EnumerateArray().Any(item =>
                item.ValueKind == JsonValueKind.String &&
                item.GetString()?.Equals(family, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static void ValidateBlockHeadRules(
        JsonElement block,
        JsonElement head,
        List<EngineAssemblyValidationMessage> messages)
    {
        string blockId = ReadString(block, string.Empty, "id");
        string headId = ReadString(head, string.Empty, "id");
        string blockFamily = ReadString(block, string.Empty, "family");
        string headFamily = ReadString(head, string.Empty, "family");
        float blockBoreMm = ReadSingle(block, 0f, "boreMm");

        string[] allowedHeadFamilies = ReadStringArray(block, "compatibilityRules", "allowedHeadFamilies");
        if (allowedHeadFamilies.Length > 0 &&
            !allowedHeadFamilies.Any(family => family.Equals(headFamily, StringComparison.OrdinalIgnoreCase)))
        {
            messages.Add(Warning("block_head_family_rule_mismatch", $"Block {blockId} does not list head family {headFamily} as an allowed head family."));
        }

        string[] allowedBlockFamilies = ReadStringArray(head, "compatibilityRules", "allowedBlockFamilies");
        if (allowedBlockFamilies.Length > 0 &&
            !allowedBlockFamilies.Any(family => family.Equals(blockFamily, StringComparison.OrdinalIgnoreCase)))
        {
            messages.Add(Warning("head_block_family_rule_mismatch", $"Head {headId} does not list block family {blockFamily} as an allowed block family."));
        }

        float minimumBlockBoreMm = ReadSingle(head, 0f, "compatibilityRules", "minimumBlockBoreMm");
        float maximumBlockBoreMm = ReadSingle(head, 0f, "compatibilityRules", "maximumBlockBoreMm");
        if (minimumBlockBoreMm > 0f && blockBoreMm > 0f && blockBoreMm < minimumBlockBoreMm)
        {
            messages.Add(Warning("head_block_bore_too_small", $"Head {headId} expects at least {minimumBlockBoreMm:0.0}mm bore, but block {blockId} is {blockBoreMm:0.0}mm."));
        }

        if (maximumBlockBoreMm > 0f && blockBoreMm > maximumBlockBoreMm)
        {
            messages.Add(Warning("head_block_bore_too_large", $"Head {headId} expects no more than {maximumBlockBoreMm:0.0}mm bore, but block {blockId} is {blockBoreMm:0.0}mm."));
        }
    }

    private static void ValidatePartAgainstHead(
        JsonElement part,
        string partId,
        string slot,
        bool headVtec,
        List<EngineAssemblyValidationMessage> messages)
    {
        if (ReadBoolean(part, false, "requirements", "vtecHead") && !headVtec)
        {
            messages.Add(Warning("part_requires_vtec_head", $"Part {partId} in slot {slot} requires a VTEC head, but the installed head is non-VTEC."));
        }

        if (TryGet(part, out JsonElement vtec, "modifies", "vtec") &&
            ReadBoolean(vtec, false, "enabled") &&
            !headVtec)
        {
            messages.Add(Warning("part_enables_vtec_on_non_vtec_head", $"Part {partId} in slot {slot} enables VTEC behavior, but the installed head is non-VTEC."));
        }
    }

    private static void ValidatePartRequirements(
        JsonElement part,
        string partId,
        string slot,
        EngineAssemblyDraft draft,
        IReadOnlyDictionary<string, string> installedParts,
        List<EngineAssemblyValidationMessage> messages)
    {
        if (!TryGet(part, out JsonElement requirements, "requirements") ||
            requirements.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        float minimumFuelOctaneRon = ReadSingle(requirements, 0f, "minimumFuelOctaneRon");
        if (minimumFuelOctaneRon > 0f && draft.FuelOctaneRon > 0f && draft.FuelOctaneRon < minimumFuelOctaneRon)
        {
            messages.Add(Warning("part_requires_higher_octane", $"Part {partId} in slot {slot} expects at least {minimumFuelOctaneRon:0} RON, but selected fuel {draft.FuelId} is {draft.FuelOctaneRon:0} RON."));
        }

        string minimumTuneTier = ReadString(requirements, string.Empty, "minimumTuneTier");
        if (!string.IsNullOrWhiteSpace(minimumTuneTier) &&
            CompareTier(draft.TuneTier, minimumTuneTier) < 0)
        {
            messages.Add(Info("part_tune_tier_recommended", $"Part {partId} in slot {slot} is best matched with {minimumTuneTier} tune or higher; current tune {draft.TuneId} is {draft.TuneTier}."));
        }

        string[] recommendedFuelIds = ReadStringArray(requirements, "recommendedFuelIds");
        if (recommendedFuelIds.Length > 0 &&
            !recommendedFuelIds.Any(id => id.Equals(draft.FuelId, StringComparison.OrdinalIgnoreCase)))
        {
            messages.Add(Info("part_fuel_recommended", $"Part {partId} in slot {slot} recommends fuel {string.Join("/", recommendedFuelIds)}; current fuel is {draft.FuelId}."));
        }

        if (TryGet(requirements, out JsonElement requiredPartSlots, "requiredPartSlots") &&
            requiredPartSlots.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty requiredSlot in requiredPartSlots.EnumerateObject())
            {
                string requiredPartId = requiredSlot.Value.ValueKind == JsonValueKind.String
                    ? requiredSlot.Value.GetString() ?? string.Empty
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(requiredPartId))
                {
                    continue;
                }

                if (!installedParts.TryGetValue(requiredSlot.Name, out string? installedPartId) ||
                    !installedPartId.Equals(requiredPartId, StringComparison.OrdinalIgnoreCase))
                {
                    messages.Add(Info("supporting_part_recommended", $"Part {partId} in slot {slot} recommends {requiredSlot.Name}={requiredPartId}; current value is {installedPartId ?? "none"}."));
                }
            }
        }
    }

    private static void ValidateInstalledPartCatalogSlot(
        CatalogLookup catalogs,
        string partId,
        string installedSlot,
        List<EngineAssemblyValidationMessage> messages)
    {
        if (!TryGetExpectedCatalogSlot(installedSlot, out string expectedCatalogSlot))
        {
            messages.Add(Warning("unknown_engine_installed_slot", $"Installed engine slot {installedSlot} is not mapped to a catalog slot."));
            return;
        }

        if (catalogs.TryGetSlot(partId, out string actualCatalogSlot) &&
            !actualCatalogSlot.Equals(expectedCatalogSlot, StringComparison.OrdinalIgnoreCase))
        {
            messages.Add(Warning("engine_part_slot_mismatch", $"Part {partId} is installed in slot {installedSlot}, which expects catalog slot {expectedCatalogSlot}, but the part belongs to catalog slot {actualCatalogSlot}."));
        }
    }

    private static bool TryGetExpectedCatalogSlot(string installedSlot, out string expectedCatalogSlot)
    {
        foreach ((string catalogSlot, string mappedInstalledSlot) in GarageModSlotMap.EngineCatalogSlotToInstalledSlot)
        {
            if (mappedInstalledSlot.Equals(installedSlot, StringComparison.OrdinalIgnoreCase))
            {
                expectedCatalogSlot = catalogSlot;
                return true;
            }
        }

        expectedCatalogSlot = string.Empty;
        return false;
    }

    private static void ValidateTuneFuelIntent(
        JsonElement tune,
        string tuneId,
        string fuelId,
        List<EngineAssemblyValidationMessage> messages)
    {
        string[] intendedFuelIds = ReadStringArray(tune, "intendedFuelIds");
        if (intendedFuelIds.Length > 0 &&
            !intendedFuelIds.Any(id => id.Equals(fuelId, StringComparison.OrdinalIgnoreCase)))
        {
            messages.Add(Info("tune_fuel_not_intended", $"Tune {tuneId} is intended for fuel {string.Join("/", intendedFuelIds)}, but selected fuel is {fuelId}."));
        }
    }

    private static JsonElement? ResolveEngineCombination(
        CatalogLookup catalogs,
        string requestedCombinationId,
        string blockId,
        string headId)
    {
        if (!string.IsNullOrWhiteSpace(requestedCombinationId))
        {
            return catalogs.Require(requestedCombinationId);
        }

        foreach (JsonElement item in catalogs.Items)
        {
            if (ReadString(item, string.Empty, "blockId").Equals(blockId, StringComparison.OrdinalIgnoreCase) &&
                ReadString(item, string.Empty, "headId").Equals(headId, StringComparison.OrdinalIgnoreCase) &&
                ReadString(item, string.Empty, "category").Equals("frankenstein", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(ReadString(item, string.Empty, "supportLevel")))
            {
                return item;
            }
        }

        return null;
    }

    private static bool IsNonFactoryEngineCombination(JsonElement engineCatalog, string blockId, string headId)
    {
        return !ReadString(engineCatalog, string.Empty, "blockId").Equals(blockId, StringComparison.OrdinalIgnoreCase) ||
            !ReadString(engineCatalog, string.Empty, "headId").Equals(headId, StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateCombinationTuneRecommendation(
        JsonElement combination,
        string tuneId,
        List<EngineAssemblyValidationMessage> messages)
    {
        string[] recommendedTuneIds = ReadStringArray(combination, "recommendedTuneIds");
        if (recommendedTuneIds.Length > 0 &&
            !recommendedTuneIds.Any(id => id.Equals(tuneId, StringComparison.OrdinalIgnoreCase)))
        {
            messages.Add(Info("engine_combination_tune_recommended", $"Engine combination {ReadString(combination, string.Empty, "id")} recommends tune {string.Join("/", recommendedTuneIds)}; current tune is {tuneId}."));
        }
    }

    private static void ValidateCombinationPair(
        JsonElement combination,
        string blockId,
        string headId,
        List<EngineAssemblyValidationMessage> messages)
    {
        string combinationBlockId = ReadString(combination, string.Empty, "blockId");
        string combinationHeadId = ReadString(combination, string.Empty, "headId");
        if (!combinationBlockId.Equals(blockId, StringComparison.OrdinalIgnoreCase) ||
            !combinationHeadId.Equals(headId, StringComparison.OrdinalIgnoreCase))
        {
            messages.Add(Warning("engine_combination_pair_mismatch", $"Requested combination {ReadString(combination, string.Empty, "id")} is for {combinationBlockId}/{combinationHeadId}, but build uses {blockId}/{headId}."));
        }
    }

    private static bool IsIntendedForEngine(JsonElement tune, string engineId)
    {
        if (!TryGet(tune, out JsonElement intendedEngines, "intendedEngines"))
        {
            return true;
        }

        return intendedEngines.ValueKind != JsonValueKind.Array ||
            intendedEngines.EnumerateArray().Any(item =>
                item.ValueKind == JsonValueKind.String &&
                item.GetString()?.Equals(engineId, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static int CompareTier(string actualTier, string requiredTier)
    {
        return TierRank(actualTier).CompareTo(TierRank(requiredTier));
    }

    private static int TierRank(string tier)
    {
        return tier.Trim().ToLowerInvariant() switch
        {
            "stock" => 0,
            "street" => 1,
            "clubsport" or "club_sport" => 2,
            "proracing" or "pro_racing" => 3,
            _ => 0
        };
    }

    private static float FirstPositive(float first, float second)
    {
        return first > 0f ? first : second;
    }

    private static float ReadWeight(JsonElement item)
    {
        return ReadSingle(item, 0f, "weightKg") +
            ReadSingle(item, 0f, "weightDeltaKg") +
            ReadSingle(item, 0f, "data", "weightKg") +
            ReadSingle(item, 0f, "data", "massKg");
    }

    private static void AddEngineMassComponent(
        ref EngineAssemblyDraft draft,
        JsonElement item,
        string role,
        string id,
        float? knownMassKg = null)
    {
        float massKg = knownMassKg ?? ReadWeight(item);
        if (MathF.Abs(massKg) <= 0.001f)
        {
            return;
        }

        (float x, float y, float z) = EngineLocalPosition(role);
        draft.MassComponents.Add(new ResolvedEngineMassComponent(id, role, massKg, x, y, z));
    }

    private static (float X, float Y, float Z) EngineLocalPosition(string role)
    {
        return role switch
        {
            "head" or "headUpgrade" or "cams" or "valveSprings" or "portPolishing" => (0f, 0.54f, 0.02f),
            "block" or "blockUpgrade" or "displacement" => (0f, 0.37f, 0f),
            "flywheel" or "clutch" => (-0.18f, 0.32f, -0.16f),
            "intake" or "intakeRunnerLength" or "throttleBody" => (0.22f, 0.48f, -0.12f),
            "headers" or "exhaust" => (-0.20f, 0.34f, 0.12f),
            _ => (0f, 0.42f, 0f)
        };
    }

    private static TorqueCurvePoint[] ReadTorqueCurve(JsonElement root, string propertyName)
    {
        if (!TryGet(root, out JsonElement array, propertyName) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<TorqueCurvePoint> points = [];
        foreach (JsonElement point in array.EnumerateArray())
        {
            float rpm = ReadSingle(point, 0f, "rpm");
            float torque = ReadSingle(point, 0f, "torqueNm");
            if (rpm > 0f)
            {
                points.Add(new TorqueCurvePoint(rpm, torque));
            }
        }

        return [.. points.OrderBy(point => point.Rpm)];
    }

    private static string[] ReadStringArray(JsonElement root, params string[] path)
    {
        if (!TryGet(root, out JsonElement array, path) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))];
    }

    private static JsonElement Require(JsonElement root, params string[] path)
    {
        return TryGet(root, out JsonElement value, path)
            ? value
            : throw new InvalidDataException($"Missing required JSON path '{string.Join(".", path)}'.");
    }

    private static string ReadString(JsonElement root, string fallback, params string[] path)
    {
        return TryGet(root, out JsonElement value, path) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    private static bool ReadBoolean(JsonElement root, bool fallback, params string[] path)
    {
        return TryGet(root, out JsonElement value, path) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;
    }

    private static float ReadSingle(JsonElement root, float fallback, params string[] path)
    {
        return TryGet(root, out JsonElement value, path) && value.TryGetSingle(out float result) ? result : fallback;
    }

    private static bool TryGet(JsonElement root, out JsonElement value, params string[] path)
    {
        value = root;
        foreach (string segment in path)
        {
            if (value.ValueKind != JsonValueKind.Object ||
                !value.TryGetProperty(segment, out value))
            {
                return false;
            }
        }

        return true;
    }

    private sealed class CatalogLookup
    {
        private readonly Dictionary<string, JsonElement> _items = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _slots = new(StringComparer.OrdinalIgnoreCase);

        public static CatalogLookup Load(params string[] catalogIndexPaths)
        {
            CatalogLookup lookup = new();
            foreach (string catalogIndexPath in catalogIndexPaths)
            {
                lookup.LoadIndex(catalogIndexPath);
            }

            lookup.ResolveInheritedItems();
            return lookup;
        }

        public JsonElement Require(string id)
        {
            return !string.IsNullOrWhiteSpace(id) && _items.TryGetValue(id, out JsonElement item)
                ? item
                : throw new InvalidDataException($"Catalog item was not found: {id}");
        }

        public bool TryGetSlot(string id, out string slot)
        {
            return _slots.TryGetValue(id, out slot!);
        }

        public IEnumerable<JsonElement> Items => _items.Values;

        private void LoadIndex(string catalogIndexPath)
        {
            string resolvedIndexPath = ResolveDataPath(catalogIndexPath);
            using FileStream stream = File.OpenRead(resolvedIndexPath);
            using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });

            if (document.RootElement.TryGetProperty("catalogs", out JsonElement catalogs))
            {
                foreach (JsonElement catalog in catalogs.EnumerateArray())
                {
                    LoadCatalog(
                        ReadString(catalog, string.Empty, "path"),
                        ReadString(catalog, string.Empty, "slot"));
                }
            }
            else
            {
                LoadCatalog(catalogIndexPath, string.Empty);
            }
        }

        private void LoadCatalog(string catalogPath, string slotHint)
        {
            string resolvedCatalogPath = ResolveDataPath(catalogPath);
            using FileStream stream = File.OpenRead(resolvedCatalogPath);
            using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
            JsonElement root = document.RootElement;
            string catalogSlot = ReadString(root, slotHint, "slot");
            JsonElement array = root.TryGetProperty("parts", out JsonElement parts) ? parts :
                root.TryGetProperty("engines", out JsonElement engines) ? engines :
                root.TryGetProperty("blocks", out JsonElement blocks) ? blocks :
                root.TryGetProperty("heads", out JsonElement heads) ? heads :
                root.TryGetProperty("combinations", out JsonElement combinations) ? combinations :
                root.TryGetProperty("tunes", out JsonElement tunes) ? tunes :
                root.TryGetProperty("fuels", out JsonElement fuels) ? fuels :
                default;

            if (array.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (JsonElement item in array.EnumerateArray())
            {
                string id = ReadString(item, string.Empty, "id");
                if (!string.IsNullOrWhiteSpace(id))
                {
                    _items[id] = item.Clone();
                    _slots[id] = catalogSlot;
                }
            }
        }

        private void ResolveInheritedItems()
        {
            foreach (string id in _items.Keys.ToArray())
            {
                _items[id] = ResolveInheritedItem(id, []);
            }
        }

        private JsonElement ResolveInheritedItem(string id, HashSet<string> stack)
        {
            if (!_items.TryGetValue(id, out JsonElement item))
            {
                throw new InvalidDataException($"Missing inherited catalog id '{id}'.");
            }

            string baseId = ReadString(item, string.Empty, "inherits");
            if (string.IsNullOrWhiteSpace(baseId))
            {
                return item;
            }

            if (!stack.Add(id))
            {
                throw new InvalidDataException($"Catalog inheritance cycle detected at '{id}'.");
            }

            JsonElement inherited = ResolveInheritedItem(baseId, stack);
            stack.Remove(id);

            JsonNode? baseNode = JsonNode.Parse(inherited.GetRawText());
            JsonNode? overrideNode = JsonNode.Parse(item.GetRawText());
            if (baseNode is not JsonObject baseObject || overrideNode is not JsonObject overrideObject)
            {
                return item;
            }

            DeepMerge(baseObject, overrideObject);
            using JsonDocument mergedDocument = JsonDocument.Parse(baseObject.ToJsonString());
            return mergedDocument.RootElement.Clone();
        }

        private static void DeepMerge(JsonObject target, JsonObject overlay)
        {
            foreach (KeyValuePair<string, JsonNode?> property in overlay)
            {
                if (target[property.Key] is JsonObject targetChild &&
                    property.Value is JsonObject overlayChild)
                {
                    DeepMerge(targetChild, overlayChild);
                }
                else
                {
                    target[property.Key] = property.Value?.DeepClone();
                }
            }
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

            throw new FileNotFoundException($"Data file was not found: {path}", path);
        }
    }

    private struct EngineAssemblyDraft
    {
        public string EngineId;
        public string EngineCombinationId;
        public string EngineCombinationDisplayName;
        public string EngineCode;
        public string DisplayName;
        public string Family;
        public string BlockId;
        public string BlockFamily;
        public string HeadId;
        public string HeadFamily;
        public string Valvetrain;
        public string TuneId;
        public string TuneTier;
        public string FuelId;
        public string FuelDisplayName;
        public float FuelOctaneRon;
        public float FuelEthanolContent;
        public float FuelSafeCompressionRatio;
        public bool FuelRequiresRetune;
        public float FuelBasePowerMultiplier;
        public float FuelHighCompressionPowerMultiplier;
        public float FuelHighCompressionStartsAt;
        public float FuelEffectivePowerMultiplier;
        public Dictionary<string, string> InstalledParts;
        public List<ResolvedEngineMassComponent> MassComponents;
        public float EstimatedAssemblyMassKg;
        public float BaseDisplacementCc;
        public float DisplacementCc;
        public float BoreMm;
        public float StrokeMm;
        public float RodLengthMm;
        public float BaseCompressionRatio;
        public float CompressionRatio;
        public float IdleRpm;
        public float PowerRedlineRpm;
        public float LimiterHardCutRpm;
        public float LimiterResumeRpm;
        public float MaxGaugeRpm;
        public float LimiterFuelCutSeconds;
        public float LimiterRestoreSeconds;
        public float LimiterCutTorqueMultiplier;
        public float BaseRotationalInertiaKgM2;
        public float RotationalInertiaKgM2;
        public bool VtecEnabled;
        public float VtecActivationRpm;
        public float VtecTransitionWidthRpm;
        public float LowCamFlowMultiplier;
        public float HighCamFlowMultiplier;
        public float IntakeFlowScale;
        public float ExhaustFlowScale;
        public float ThrottleGamma;
        public float ClutchTorqueCapacityNm;
        public float ClutchBitePoint;
        public float ClutchCouplingRate;
        public float ClutchEngagementSharpness;
        public float ClutchSlipDamping;
        public float ClutchLowSpeedAssistStrength;
        public float ClutchBiteInputStartMultiplier;
        public float ClutchLaunchAssistExponent;
        public float ClutchLowSpeedThrottleGamma;
        public float ClutchLowSpeedThrottleAssist;
        public float ClutchLowSpeedTorqueAssistNm;
        public float ClutchRollingLockSpeedMetersPerSecond;
        public float ClutchRollingLockSlipRadiansPerSecond;
        public float ValveSpringFloatStartRpm;
        public float ValveSpringSafeContinuousRpm;
        public string EngineAudioDspId;
        public string EngineAudioDspDisplayName;
        public string EngineAudioProfilePath;
        public string EngineAudioProfileEngineId;
        public string EngineAudioProfileEngineFamily;
        public bool EngineAudioFallbackAllowed;
        public string EngineAudioSourceRecordingPath;
        public string EngineAudioGenerationMethod;
        public string EngineAudioGeneratedSampleSetPath;
        public TorqueCurvePoint[] TorqueCurve;
        public TorqueCurvePoint[] AuthoredResolvedTorqueCurve;
        public TorqueCurvePoint[] EngineBrakeTorqueCurve;
        public EnginePowerCompositionTrace PowerComposition;

        public readonly ResolvedEngineAssembly ToResolved(IReadOnlyList<EngineAssemblyValidationMessage> validationMessages)
        {
            return new ResolvedEngineAssembly
            {
                EngineId = EngineId,
                EngineCombinationId = EngineCombinationId,
                EngineCombinationDisplayName = EngineCombinationDisplayName,
                EngineCode = EngineCode,
                DisplayName = DisplayName,
                Family = Family,
                BlockId = BlockId,
                BlockFamily = BlockFamily,
                HeadId = HeadId,
                HeadFamily = HeadFamily,
                Valvetrain = Valvetrain,
                TuneId = TuneId,
                TuneTier = TuneTier,
                FuelId = FuelId,
                FuelDisplayName = FuelDisplayName,
                FuelOctaneRon = FuelOctaneRon,
                FuelEthanolContent = FuelEthanolContent,
                FuelSafeCompressionRatio = FuelSafeCompressionRatio,
                FuelEffectivePowerMultiplier = FuelEffectivePowerMultiplier,
                FuelRequiresRetune = FuelRequiresRetune,
                MassComponents = MassComponents.ToArray(),
                InstalledParts = new ReadOnlyDictionary<string, string>(InstalledParts),
                EstimatedAssemblyMassKg = EstimatedAssemblyMassKg,
                DisplacementCc = DisplacementCc,
                BoreMm = BoreMm,
                StrokeMm = StrokeMm,
                RodLengthMm = RodLengthMm,
                CompressionRatio = CompressionRatio,
                IdleRpm = IdleRpm,
                PowerRedlineRpm = PowerRedlineRpm,
                LimiterHardCutRpm = LimiterHardCutRpm,
                LimiterResumeRpm = LimiterResumeRpm,
                MaxGaugeRpm = MaxGaugeRpm,
                LimiterFuelCutSeconds = LimiterFuelCutSeconds,
                LimiterRestoreSeconds = LimiterRestoreSeconds,
                LimiterCutTorqueMultiplier = LimiterCutTorqueMultiplier,
                RotationalInertiaKgM2 = RotationalInertiaKgM2,
                VtecEnabled = VtecEnabled,
                VtecActivationRpm = VtecActivationRpm,
                VtecTransitionWidthRpm = VtecTransitionWidthRpm,
                LowCamFlowMultiplier = LowCamFlowMultiplier,
                HighCamFlowMultiplier = HighCamFlowMultiplier,
                IntakeFlowScale = IntakeFlowScale,
                ExhaustFlowScale = ExhaustFlowScale,
                ThrottleGamma = ThrottleGamma,
                ClutchTorqueCapacityNm = ClutchTorqueCapacityNm,
                ClutchBitePoint = ClutchBitePoint,
                ClutchCouplingRate = ClutchCouplingRate,
                ClutchEngagementSharpness = ClutchEngagementSharpness,
                ClutchSlipDamping = ClutchSlipDamping,
                ClutchLowSpeedAssistStrength = ClutchLowSpeedAssistStrength,
                ClutchBiteInputStartMultiplier = ClutchBiteInputStartMultiplier,
                ClutchLaunchAssistExponent = ClutchLaunchAssistExponent,
                ClutchLowSpeedThrottleGamma = ClutchLowSpeedThrottleGamma,
                ClutchLowSpeedThrottleAssist = ClutchLowSpeedThrottleAssist,
                ClutchLowSpeedTorqueAssistNm = ClutchLowSpeedTorqueAssistNm,
                ClutchRollingLockSpeedMetersPerSecond = ClutchRollingLockSpeedMetersPerSecond,
                ClutchRollingLockSlipRadiansPerSecond = ClutchRollingLockSlipRadiansPerSecond,
                ValveSpringFloatStartRpm = ValveSpringFloatStartRpm,
                ValveSpringSafeContinuousRpm = ValveSpringSafeContinuousRpm,
                EngineAudioDspId = EngineAudioDspId,
                EngineAudioDspDisplayName = EngineAudioDspDisplayName,
                EngineAudioProfilePath = EngineAudioProfilePath,
                EngineAudioProfileEngineId = EngineAudioProfileEngineId,
                EngineAudioProfileEngineFamily = EngineAudioProfileEngineFamily,
                EngineAudioFallbackAllowed = EngineAudioFallbackAllowed,
                EngineAudioSourceRecordingPath = EngineAudioSourceRecordingPath,
                EngineAudioGenerationMethod = EngineAudioGenerationMethod,
                EngineAudioGeneratedSampleSetPath = EngineAudioGeneratedSampleSetPath,
                TorqueCurve = TorqueCurve,
                EngineBrakeTorqueCurve = EngineBrakeTorqueCurve,
                PowerComposition = PowerComposition,
                Validation = validationMessages.ToArray()
            };
        }
    }
}
