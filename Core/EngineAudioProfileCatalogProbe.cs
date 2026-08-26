using System.Text.Json;
using RType.Audio;

namespace RType.Core;

internal static class EngineAudioProfileCatalogProbe
{
    private const string CatalogPath = "Data/Audio/engine_audio_profile_catalog.json";
    private const string EngineAudioDspPath = "Data/Parts/Engine/engine_audio_dsp.json";

    public static void Run()
    {
        string resolvedCatalogPath = ResolveDataPath(CatalogPath);
        using FileStream stream = File.OpenRead(resolvedCatalogPath);
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
        JsonElement root = document.RootElement;
        JsonElement profiles = Require(root, "profiles");
        if (profiles.ValueKind != JsonValueKind.Array || profiles.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Engine audio profile catalog probe failed: no profiles were declared.");
        }

        int exactProfiles = 0;
        int fallbackFamilies = 0;
        int sampleCount = 0;
        int missingOptionalSources = 0;
        Dictionary<string, EngineAudioProfileCatalogEntry> catalogByPath = new(StringComparer.OrdinalIgnoreCase);

        Console.WriteLine("Engine audio profile catalog probe");
        foreach (JsonElement entry in profiles.EnumerateArray())
        {
            string id = ReadString(entry, string.Empty, "id");
            string path = ReadString(entry, string.Empty, "path");
            string sourceEngineId = ReadString(entry, string.Empty, "sourceEngineId");
            string sourceFamily = ReadString(entry, string.Empty, "sourceEngineFamily");
            string coverage = ReadString(entry, string.Empty, "coverage");
            string generationMethod = ReadString(entry, string.Empty, "generationMethod");
            string generatedSampleSetPath = ReadString(entry, string.Empty, "generatedSampleSetPath");
            string sourceRecordingPath = ReadString(entry, string.Empty, "sourceRecordingPath");
            bool sourceRecordingRequired = ReadBoolean(entry, false, "sourceRecordingRequired");
            string[] requiredRoles = ReadStringArray(entry, "requiredSampleRoles");
            string[] fallbackFamilyIds = ReadStringArray(entry, "fallbackAllowedForFamilies");

            RequireCondition(!string.IsNullOrWhiteSpace(id), "profile entry missing id");
            RequireCondition(!string.IsNullOrWhiteSpace(path), $"{id} missing profile path");
            RequireCondition(!string.IsNullOrWhiteSpace(sourceEngineId), $"{id} missing sourceEngineId");
            RequireCondition(!string.IsNullOrWhiteSpace(sourceFamily), $"{id} missing sourceEngineFamily");
            RequireCondition(!string.IsNullOrWhiteSpace(generationMethod), $"{id} missing generationMethod");
            RequireCondition(!string.IsNullOrWhiteSpace(generatedSampleSetPath), $"{id} missing generatedSampleSetPath");
            RequireCondition(Directory.Exists(ResolveDirectoryPath(generatedSampleSetPath)), $"{id} generated sample set path missing: {generatedSampleSetPath}");
            if (!string.IsNullOrWhiteSpace(sourceRecordingPath) && !CanResolveDataPath(sourceRecordingPath))
            {
                if (sourceRecordingRequired)
                {
                    throw new InvalidOperationException($"Engine audio profile catalog probe failed: {id} required source recording path missing: {sourceRecordingPath}.");
                }

                missingOptionalSources++;
            }

            JsonElement profile = LoadProfile(path);
            string profileId = ReadString(profile, string.Empty, "id");
            string profileEngineId = ReadString(profile, string.Empty, "engineId");
            string profileFamily = NormalizeAudioFamily(ReadString(profile, string.Empty, "engineFamily"));
            string profileGenerationMethod = ReadString(profile, generationMethod, "generationMethod");
            JsonElement samples = Require(profile, "samples");

            RequireCondition(profileId.Equals(id, StringComparison.OrdinalIgnoreCase), $"{id} catalog id does not match profile id {profileId}");
            RequireCondition(profileEngineId.Equals(sourceEngineId, StringComparison.OrdinalIgnoreCase), $"{id} source engine does not match profile engine {profileEngineId}");
            RequireCondition(profileFamily.Equals(sourceFamily, StringComparison.OrdinalIgnoreCase), $"{id} source family does not match profile family {profileFamily}");
            RequireCondition(profileGenerationMethod.Equals(generationMethod, StringComparison.OrdinalIgnoreCase), $"{id} generation method does not match profile");

            HashSet<string> roles = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonElement sample in samples.EnumerateArray())
            {
                string role = ReadString(sample, string.Empty, "role");
                string samplePath = ReadString(sample, string.Empty, "path");
                float rpm = ReadSingle(sample, 0f, "rpm");
                RequireCondition(!string.IsNullOrWhiteSpace(role), $"{id} has sample without role");
                RequireCondition(!string.IsNullOrWhiteSpace(samplePath), $"{id} {role} sample missing path");
                RequireCondition(rpm > 0f, $"{id} {role} sample missing rpm");
                WavLoopSource source = WavLoopSource.Load(ResolveDataPath(samplePath));
                RequireCondition(source.FrameCount > 0, $"{id} {role} sample has no frames");
                roles.Add(role);
                sampleCount++;
            }

            foreach (string role in requiredRoles)
            {
                RequireCondition(roles.Contains(role), $"{id} missing required sample role {role}");
            }

            if (coverage.Equals("exact", StringComparison.OrdinalIgnoreCase))
            {
                exactProfiles++;
            }

            catalogByPath[NormalizeCatalogPath(path)] = new EngineAudioProfileCatalogEntry(
                id,
                NormalizeCatalogPath(path),
                sourceEngineId,
                sourceFamily,
                generationMethod,
                generatedSampleSetPath,
                fallbackFamilyIds);

            fallbackFamilies += fallbackFamilyIds.Length;
            Console.WriteLine($"  {id}: {sourceEngineId}/{sourceFamily}, {coverage}, samples {samples.GetArrayLength()}, fallbacks {fallbackFamilyIds.Length}");
        }

        int dspProfileReferences = ValidateEngineAudioDspReferences(catalogByPath);

        Console.WriteLine($"  result: PASS ({profiles.GetArrayLength()} profiles, {exactProfiles} exact, {fallbackFamilies} fallback families, {sampleCount} samples, {dspProfileReferences} DSP profile refs, {missingOptionalSources} missing optional source recordings)");
    }

    private static int ValidateEngineAudioDspReferences(IReadOnlyDictionary<string, EngineAudioProfileCatalogEntry> catalogByPath)
    {
        using FileStream stream = File.OpenRead(ResolveDataPath(EngineAudioDspPath));
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
        JsonElement root = document.RootElement;
        JsonElement parts = Require(root, "parts");
        int references = 0;

        foreach (JsonElement part in parts.EnumerateArray())
        {
            string dspId = ReadString(part, string.Empty, "id");
            if (!TryGet(part, out JsonElement audio, "modifies", "audio"))
            {
                continue;
            }

            string profilePath = ReadString(audio, string.Empty, "engineAudioProfilePath");
            if (string.IsNullOrWhiteSpace(profilePath))
            {
                continue;
            }

            string normalizedProfilePath = NormalizeCatalogPath(profilePath);
            if (!catalogByPath.TryGetValue(normalizedProfilePath, out EngineAudioProfileCatalogEntry? profile))
            {
                throw new InvalidOperationException(
                    $"Engine audio profile catalog probe failed: DSP {dspId} references profile path {profilePath}, but it is not registered in {CatalogPath}.");
            }

            string profileEngineId = ReadString(audio, string.Empty, "profileEngineId");
            string profileEngineFamily = ReadString(audio, string.Empty, "profileEngineFamily");
            string generationMethod = ReadString(audio, string.Empty, "generationMethod");
            string generatedSampleSetPath = ReadString(audio, string.Empty, "generatedSampleSetPath");
            bool fallbackAllowed = ReadBoolean(audio, false, "fallbackAllowed");

            RequireCondition(profile.SourceEngineId.Equals(profileEngineId, StringComparison.OrdinalIgnoreCase),
                $"DSP {dspId} profileEngineId {profileEngineId} does not match catalog source {profile.SourceEngineId}");
            RequireCondition(profile.SourceEngineFamily.Equals(profileEngineFamily, StringComparison.OrdinalIgnoreCase),
                $"DSP {dspId} profileEngineFamily {profileEngineFamily} does not match catalog source {profile.SourceEngineFamily}");
            RequireCondition(profile.GenerationMethod.Equals(generationMethod, StringComparison.OrdinalIgnoreCase),
                $"DSP {dspId} generationMethod {generationMethod} does not match catalog method {profile.GenerationMethod}");
            RequireCondition(NormalizeCatalogPath(profile.GeneratedSampleSetPath).Equals(NormalizeCatalogPath(generatedSampleSetPath), StringComparison.OrdinalIgnoreCase),
                $"DSP {dspId} generatedSampleSetPath {generatedSampleSetPath} does not match catalog path {profile.GeneratedSampleSetPath}");

            if (fallbackAllowed)
            {
                string[] compatibility = ReadStringArray(part, "compatibility");
                foreach (string family in compatibility)
                {
                    RequireCondition(profile.FallbackAllowedForFamilies.Any(candidate => candidate.Equals(family, StringComparison.OrdinalIgnoreCase)),
                        $"DSP {dspId} allows fallback for {family}, but profile {profile.Id} does not list that family.");
                }
            }

            references++;
            Console.WriteLine($"  dsp {dspId}: profile {profile.Id}, source {profile.SourceEngineId}/{profile.SourceEngineFamily}, fallback {fallbackAllowed}");
        }

        return references;
    }

    private static JsonElement LoadProfile(string path)
    {
        using FileStream stream = File.OpenRead(ResolveDataPath(path));
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
        return document.RootElement.Clone();
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

    private static float ReadSingle(JsonElement root, float fallback, params string[] path)
    {
        return TryGet(root, out JsonElement value, path) && value.TryGetSingle(out float result)
            ? result
            : fallback;
    }

    private static bool ReadBoolean(JsonElement root, bool fallback, params string[] path)
    {
        return TryGet(root, out JsonElement value, path) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;
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

    private static string NormalizeAudioFamily(string family)
    {
        return family.EndsWith("_vtec", StringComparison.OrdinalIgnoreCase)
            ? family[..^"_vtec".Length]
            : family;
    }

    private static string NormalizeCatalogPath(string path)
    {
        return path.Replace('\\', '/');
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

        throw new FileNotFoundException($"Data or asset file was not found: {path}", path);
    }

    private static bool CanResolveDataPath(string path)
    {
        try
        {
            _ = ResolveDataPath(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string ResolveDirectoryPath(string path)
    {
        if (Path.IsPathRooted(path) && Directory.Exists(path))
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
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return candidates[0];
    }

    private static void RequireCondition(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Engine audio profile catalog probe failed: {message}.");
        }
    }

    private sealed record EngineAudioProfileCatalogEntry(
        string Id,
        string Path,
        string SourceEngineId,
        string SourceEngineFamily,
        string GenerationMethod,
        string GeneratedSampleSetPath,
        IReadOnlyList<string> FallbackAllowedForFamilies);
}
