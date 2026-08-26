using System.Text.Json;
using RType.Data;

namespace RType.Core;

internal static class EngineAudioGenerationTargetProbe
{
    private const string TargetsPath = "Data/Audio/engine_audio_generation_targets.json";
    private const string ProfileCatalogPath = "Data/Audio/engine_audio_profile_catalog.json";
    private const string EnginesPath = "Data/Parts/Engine/engines.json";
    private const string EngineCombinationsPath = "Data/Parts/Engine/engine_combinations.json";

    public static void Run()
    {
        Dictionary<string, ProfileCatalogEntry> profilesById = LoadProfileCatalog();
        using FileStream stream = File.OpenRead(ResolveDataPath(TargetsPath));
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
        JsonElement targets = Require(document.RootElement, "targets");

        Require(targets.ValueKind == JsonValueKind.Array && targets.GetArrayLength() > 0, "no generation targets were declared");

        HashSet<string> targetIds = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> targetEngineIds = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> targetCombinationIds = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> desiredProfileIds = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> desiredProfilePaths = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> coveredProfileIds = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> generationKeys = new(StringComparer.OrdinalIgnoreCase);
        int covered = 0;
        int needsGeneration = 0;

        Console.WriteLine("Engine audio generation target probe");
        foreach (JsonElement target in targets.EnumerateArray())
        {
            string id = ReadString(target, string.Empty, "id");
            string targetType = ReadString(target, string.Empty, "targetType");
            string status = ReadString(target, string.Empty, "status");
            string desiredProfileId = ReadString(target, string.Empty, "desiredProfileId");
            string targetProfilePath = NormalizePath(ReadString(target, string.Empty, "targetProfilePath"));
            string targetSampleSetPath = NormalizePath(ReadString(target, string.Empty, "targetSampleSetPath"));
            string expectedGenerationKey = ReadString(target, string.Empty, "expectedGenerationKey");
            string[] requiredRoles = ReadStringArray(target, "requiredSampleRoles");
            JsonElement engineRequest = Require(target, "engine");

            Require(!string.IsNullOrWhiteSpace(id), "target missing id");
            Require(targetIds.Add(id), $"duplicate target id {id}");
            Require(targetType is "factory_engine" or "authored_combination" or "engine_build", $"{id} has unsupported targetType {targetType}");
            Require(status is "covered_exact" or "needs_generation", $"{id} has unsupported status {status}");
            Require(!string.IsNullOrWhiteSpace(desiredProfileId), $"{id} missing desiredProfileId");
            Require(!string.IsNullOrWhiteSpace(targetProfilePath), $"{id} missing targetProfilePath");
            Require(!string.IsNullOrWhiteSpace(targetSampleSetPath), $"{id} missing targetSampleSetPath");
            Require(!string.IsNullOrWhiteSpace(expectedGenerationKey), $"{id} missing expectedGenerationKey");
            Require(desiredProfileIds.Add(desiredProfileId), $"{id} duplicates desired profile id {desiredProfileId}");
            Require(desiredProfilePaths.Add(targetProfilePath), $"{id} duplicates target profile path {targetProfilePath}");
            Require(requiredRoles.Contains("idle", StringComparer.OrdinalIgnoreCase), $"{id} missing idle sample role");
            Require(requiredRoles.Contains("normal", StringComparer.OrdinalIgnoreCase), $"{id} missing normal sample role");

            ResolvedEngineAssembly engine = EngineAssemblyResolver.Resolve(engineRequest);
            VehicleAudioParametersBridge audio = BuildAudioBridge(engine);
            Require(!string.IsNullOrWhiteSpace(audio.GenerationKey), $"{id} resolved without a generation key");
            Require(audio.GenerationKey.Equals(expectedGenerationKey, StringComparison.OrdinalIgnoreCase),
                $"{id} expectedGenerationKey does not match resolver output. Expected {expectedGenerationKey}, got {audio.GenerationKey}");
            Require(generationKeys.Add(audio.GenerationKey), $"{id} produced duplicate generation key {audio.GenerationKey}");

            EngineAssemblyValidationMessage[] warnings = [.. engine.Validation.Where(message => message.Severity == EngineAssemblyValidationSeverity.Warning)];
            if (warnings.Length > 0)
            {
                foreach (EngineAssemblyValidationMessage warning in warnings)
                {
                    Console.WriteLine($"  warning {id}: {warning.Code} - {warning.Message}");
                }

                throw new InvalidOperationException($"Engine audio generation target probe failed: {id} produced resolver warnings.");
            }

            if (engine.VtecEnabled)
            {
                Require(requiredRoles.Contains("vtec", StringComparer.OrdinalIgnoreCase), $"{id} resolves as VTEC but does not request a VTEC sample role");
            }
            else
            {
                Require(!requiredRoles.Contains("vtec", StringComparer.OrdinalIgnoreCase), $"{id} resolves as non-VTEC but requests a VTEC sample role");
            }

            string combinationId = engine.EngineCombinationId;
            if (targetType.Equals("factory_engine", StringComparison.OrdinalIgnoreCase))
            {
                Require(string.IsNullOrWhiteSpace(combinationId), $"{id} is a factory target but resolves as combination {combinationId}");
                targetEngineIds.Add(engine.EngineId);
            }
            else
            if (targetType.Equals("authored_combination", StringComparison.OrdinalIgnoreCase))
            {
                Require(!string.IsNullOrWhiteSpace(combinationId), $"{id} is a combination target but resolved without an authored combination id");
                targetCombinationIds.Add(combinationId);
            }
            else
            {
                Require(string.IsNullOrWhiteSpace(combinationId), $"{id} is an engine_build target but resolves as authored combination {combinationId}");
            }

            if (status.Equals("covered_exact", StringComparison.OrdinalIgnoreCase))
            {
                if (!profilesById.TryGetValue(desiredProfileId, out ProfileCatalogEntry? profile))
                {
                    throw new InvalidOperationException(
                        $"Engine audio generation target probe failed: {id} is covered_exact but desired profile {desiredProfileId} is not registered.");
                }

                Require(profile.Path.Equals(targetProfilePath, StringComparison.OrdinalIgnoreCase), $"{id} target profile path does not match registered profile path");
                Require(profile.SourceEngineId.Equals(engine.EngineId, StringComparison.OrdinalIgnoreCase), $"{id} covered profile source engine does not match resolved engine");
                coveredProfileIds.Add(desiredProfileId);
                covered++;
            }
            else
            {
                Require(!profilesById.ContainsKey(desiredProfileId), $"{id} is marked needs_generation but desired profile {desiredProfileId} is already registered");
                needsGeneration++;
            }

            Console.WriteLine($"  {id}: {engine.EngineCode}/{engine.Family}, {status}, key {audio.GenerationKey}");
        }

        ValidateFactoryCoverage(targetEngineIds);
        ValidateCombinationCoverage(targetCombinationIds);
        ValidateProfileOwnership(profilesById, coveredProfileIds);

        Console.WriteLine($"  result: PASS ({targets.GetArrayLength()} targets, {covered} covered exact, {needsGeneration} need generation, {generationKeys.Count} unique keys, {profilesById.Count} owned profiles)");
    }

    private static VehicleAudioParametersBridge BuildAudioBridge(ResolvedEngineAssembly engine)
    {
        ResolvedDrivetrainBuild drivetrain = new()
        {
            ForwardGearRatios = [3.23f, 2.105f, 1.458f, 1.107f, 0.848f],
            FinalDriveRatio = 4.4f
        };
        RType.Vehicle.VehicleAudioParameters audio = VehicleRaceSampleAudioBuilder.Build(
            engine,
            drivetrain,
            TargetsPath);
        return new VehicleAudioParametersBridge(audio.EngineAudioSampleGenerationKey);
    }

    private static void ValidateFactoryCoverage(IReadOnlySet<string> targetEngineIds)
    {
        using FileStream stream = File.OpenRead(ResolveDataPath(EnginesPath));
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
        foreach (JsonElement engine in Require(document.RootElement, "engines").EnumerateArray())
        {
            string id = ReadString(engine, string.Empty, "id");
            Require(targetEngineIds.Contains(id), $"factory engine {id} has no audio generation target");
        }
    }

    private static void ValidateCombinationCoverage(IReadOnlySet<string> targetCombinationIds)
    {
        using FileStream stream = File.OpenRead(ResolveDataPath(EngineCombinationsPath));
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
        foreach (JsonElement combination in Require(document.RootElement, "combinations").EnumerateArray())
        {
            string id = ReadString(combination, string.Empty, "id");
            Require(targetCombinationIds.Contains(id), $"authored combination {id} has no audio generation target");
        }
    }

    private static void ValidateProfileOwnership(
        IReadOnlyDictionary<string, ProfileCatalogEntry> profilesById,
        IReadOnlySet<string> coveredProfileIds)
    {
        foreach ((string profileId, ProfileCatalogEntry profile) in profilesById)
        {
            Require(coveredProfileIds.Contains(profileId),
                $"registered profile {profileId} ({profile.Path}) is not owned by a covered_exact generation target");
        }
    }

    private static Dictionary<string, ProfileCatalogEntry> LoadProfileCatalog()
    {
        using FileStream stream = File.OpenRead(ResolveDataPath(ProfileCatalogPath));
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
        Dictionary<string, ProfileCatalogEntry> profiles = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement profile in Require(document.RootElement, "profiles").EnumerateArray())
        {
            string id = ReadString(profile, string.Empty, "id");
            if (!string.IsNullOrWhiteSpace(id))
            {
                profiles[id] = new ProfileCatalogEntry(
                    NormalizePath(ReadString(profile, string.Empty, "path")),
                    ReadString(profile, string.Empty, "sourceEngineId"));
            }
        }

        return profiles;
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

    private static bool TryGet(JsonElement root, out JsonElement value, params string[] path)
    {
        value = root;
        foreach (string segment in path)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value))
            {
                value = default;
                return false;
            }
        }

        return true;
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

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Engine audio generation target probe failed: {message}.");
        }
    }

    private sealed record ProfileCatalogEntry(string Path, string SourceEngineId);

    private sealed record VehicleAudioParametersBridge(string GenerationKey);
}
