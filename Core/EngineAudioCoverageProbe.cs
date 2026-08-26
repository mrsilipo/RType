using System.Text.Json;
using RType.Data;
using RType.Vehicle;

namespace RType.Core;

internal static class EngineAudioCoverageProbe
{
    private const string ProfileCatalogPath = "Data/Audio/engine_audio_profile_catalog.json";
    private const string GenerationTargetsPath = "Data/Audio/engine_audio_generation_targets.json";
    private const string EnginesPath = "Data/Parts/Engine/engines.json";
    private const string EngineCombinationsPath = "Data/Parts/Engine/engine_combinations.json";

    private static readonly string[] BuildRoots =
    [
        "Data/PurchaseCars",
        "Data/Garage/OwnedVehicles"
    ];

    public static void Run()
    {
        EngineAudioProfile[] profiles = LoadProfiles();
        Require(profiles.Length > 0, "no audio profiles were registered");
        AudioGenerationTargets generationTargets = LoadGenerationTargets();

        Console.WriteLine("Engine audio coverage probe");
        Console.WriteLine("  factory engines");
        CoverageSummary engineSummary = ReportFactoryEngineCoverage(profiles, generationTargets);

        Console.WriteLine("  authored combinations");
        CoverageSummary combinationSummary = ReportCombinationCoverage(profiles, generationTargets);

        Console.WriteLine("  assembled vehicles");
        VehicleCoverageSummary vehicleSummary = ReportVehicleCoverage(profiles, generationTargets);

        Require(engineSummary.Exact > 0, "no factory engine has exact audio profile coverage");
        Require(engineSummary.UntrackedGaps == 0, "one or more factory engine audio gaps are not tracked by generation targets");
        Require(combinationSummary.UntrackedGaps == 0, "one or more authored combination audio gaps are not tracked by generation targets");
        Require(vehicleSummary.MissingRegisteredProfile == 0, "one or more assembled vehicles resolved to an unregistered audio profile");
        Require(vehicleSummary.UnauthorizedFallback == 0, "one or more assembled vehicles use a fallback profile without explicit coverage");
        Require(vehicleSummary.MissingGenerationKey == 0, "one or more assembled vehicles resolved without a sample generation key");
        Require(vehicleSummary.UntrackedFallback == 0, "one or more assembled vehicle audio fallbacks are not tracked by generation targets");

        Console.WriteLine(
            $"  result: PASS (engines exact {engineSummary.Exact}, engine fallbacks {engineSummary.Fallback}, engine gaps {engineSummary.MissingExact}, combinations exact {combinationSummary.Exact}, combination fallbacks {combinationSummary.Fallback}, combination gaps {combinationSummary.MissingExact}, vehicles exact {vehicleSummary.Exact}, vehicle fallbacks {vehicleSummary.Fallback}, tracked gaps {engineSummary.TrackedGaps + combinationSummary.TrackedGaps + vehicleSummary.TrackedFallback})");
    }

    private static CoverageSummary ReportFactoryEngineCoverage(
        IReadOnlyList<EngineAudioProfile> profiles,
        AudioGenerationTargets generationTargets)
    {
        using FileStream stream = File.OpenRead(ResolveDataPath(EnginesPath));
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
        JsonElement engines = Require(document.RootElement, "engines");

        CoverageSummary summary = new();
        foreach (JsonElement engine in engines.EnumerateArray())
        {
            string id = ReadString(engine, string.Empty, "id");
            string code = ReadString(engine, string.Empty, "code");
            string family = ReadString(engine, string.Empty, "family");
            CoverageResult coverage = ResolveEngineCoverage(id, family, profiles);
            bool tracked = coverage.Kind == CoverageKind.Exact || generationTargets.FactoryEngineIds.Contains(id);
            summary.Add(coverage, tracked);

            Console.WriteLine($"    {id} ({code}/{family}): {coverage.Label}{FormatTracking(coverage, tracked)}");
        }

        return summary;
    }

    private static CoverageSummary ReportCombinationCoverage(
        IReadOnlyList<EngineAudioProfile> profiles,
        AudioGenerationTargets generationTargets)
    {
        using FileStream stream = File.OpenRead(ResolveDataPath(EngineCombinationsPath));
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
        JsonElement combinations = Require(document.RootElement, "combinations");

        CoverageSummary summary = new();
        foreach (JsonElement combination in combinations.EnumerateArray())
        {
            string id = ReadString(combination, string.Empty, "id");
            string family = ReadString(combination, string.Empty, "family");
            CoverageResult coverage = ResolveEngineCoverage(id, family, profiles);
            bool tracked = coverage.Kind == CoverageKind.Exact || generationTargets.CombinationIds.Contains(id);
            summary.Add(coverage, tracked);

            Console.WriteLine($"    {id} ({family}): {coverage.Label}{FormatTracking(coverage, tracked)}");
        }

        return summary;
    }

    private static VehicleCoverageSummary ReportVehicleCoverage(
        IReadOnlyList<EngineAudioProfile> profiles,
        AudioGenerationTargets generationTargets)
    {
        string[] buildPaths = [.. BuildRoots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.GetFiles(root, "*.json", SearchOption.TopDirectoryOnly))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];

        Require(buildPaths.Length > 0, "no purchase or owned vehicle JSON files were found");

        VehicleCoverageSummary summary = new();
        Dictionary<string, EngineAudioProfile> profilesByPath = profiles.ToDictionary(
            profile => NormalizePath(profile.Path),
            StringComparer.OrdinalIgnoreCase);

        foreach (string buildPath in buildPaths)
        {
            ResolvedVehicleAssembly assembly = VehicleAssemblyResolver.Resolve(buildPath);
            VehicleAudioParameters audio = VehicleRaceSampleAudioBuilder.Build(
                assembly.Engine,
                assembly.RuntimeBuild.Drivetrain,
                buildPath);

            if (string.IsNullOrWhiteSpace(audio.EngineAudioSampleGenerationKey))
            {
                summary.MissingGenerationKey++;
            }

            string profilePath = NormalizePath(assembly.Engine.EngineAudioProfilePath);
            if (!profilesByPath.TryGetValue(profilePath, out EngineAudioProfile? profile))
            {
                summary.MissingRegisteredProfile++;
                Console.WriteLine($"    {assembly.BuildId}: missing registered profile {assembly.Engine.EngineAudioProfilePath}");
                continue;
            }

            bool sourceEngineMatches = profile.SourceEngineId.Equals(assembly.Engine.EngineId, StringComparison.OrdinalIgnoreCase);
            bool exact = sourceEngineMatches && generationTargets.CoveredGenerationKeys.Contains(audio.EngineAudioSampleGenerationKey);
            bool fallbackAllowed = profile.FallbackAllowedForFamilies.Any(family =>
                family.Equals(assembly.Engine.Family, StringComparison.OrdinalIgnoreCase));
            bool trackedFallback = generationTargets.GenerationKeys.Contains(audio.EngineAudioSampleGenerationKey) ||
                (string.IsNullOrWhiteSpace(assembly.Engine.EngineCombinationId)
                    ? generationTargets.FactoryEngineIds.Contains(assembly.Engine.EngineId)
                    : generationTargets.CombinationIds.Contains(assembly.Engine.EngineCombinationId));

            if (exact)
            {
                summary.Exact++;
            }
            else if (assembly.Engine.EngineAudioFallbackAllowed && fallbackAllowed)
            {
                summary.Fallback++;
                if (trackedFallback)
                {
                    summary.TrackedFallback++;
                }
                else
                {
                    summary.UntrackedFallback++;
                }
            }
            else
            {
                summary.UnauthorizedFallback++;
            }

            string coverage = exact
                ? $"exact profile {profile.Id}"
                : fallbackAllowed
                    ? sourceEngineMatches
                        ? $"same-engine profile {profile.Id} from different generated key"
                        : $"fallback profile {profile.Id} from {profile.SourceEngineId}"
                    : $"unauthorized fallback profile {profile.Id} from {profile.SourceEngineId}";

            string tracking = exact ? string.Empty : trackedFallback ? ", target tracked" : ", target missing";
            Console.WriteLine($"    {assembly.BuildId}: {assembly.Engine.EngineCode}/{assembly.Engine.Family}, {coverage}{tracking}, key {audio.EngineAudioSampleGenerationKey}");
        }

        return summary;
    }

    private static CoverageResult ResolveEngineCoverage(
        string engineId,
        string family,
        IReadOnlyList<EngineAudioProfile> profiles)
    {
        EngineAudioProfile? exact = profiles.FirstOrDefault(profile =>
            profile.SourceEngineId.Equals(engineId, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return CoverageResult.Exact($"exact profile {exact.Id}");
        }

        EngineAudioProfile? fallback = profiles.FirstOrDefault(profile =>
            profile.FallbackAllowedForFamilies.Any(candidate => candidate.Equals(family, StringComparison.OrdinalIgnoreCase)));
        if (fallback is not null)
        {
            return CoverageResult.Fallback($"fallback via {fallback.Id} ({fallback.SourceEngineId})");
        }

        return CoverageResult.Missing("missing exact/fallback profile");
    }

    private static EngineAudioProfile[] LoadProfiles()
    {
        using FileStream stream = File.OpenRead(ResolveDataPath(ProfileCatalogPath));
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
        JsonElement profiles = Require(document.RootElement, "profiles");

        return [.. profiles.EnumerateArray().Select(profile => new EngineAudioProfile(
            ReadString(profile, string.Empty, "id"),
            NormalizePath(ReadString(profile, string.Empty, "path")),
            ReadString(profile, string.Empty, "sourceEngineId"),
            ReadString(profile, string.Empty, "sourceEngineFamily"),
            ReadStringArray(profile, "fallbackAllowedForFamilies")))];
    }

    private static AudioGenerationTargets LoadGenerationTargets()
    {
        using FileStream stream = File.OpenRead(ResolveDataPath(GenerationTargetsPath));
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
        JsonElement targets = Require(document.RootElement, "targets");
        HashSet<string> factoryEngineIds = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> combinationIds = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> generationKeys = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> coveredGenerationKeys = new(StringComparer.OrdinalIgnoreCase);

        foreach (JsonElement target in targets.EnumerateArray())
        {
            string targetType = ReadString(target, string.Empty, "targetType");
            string status = ReadString(target, string.Empty, "status");
            JsonElement engine = Require(target, "engine");
            string expectedGenerationKey = ReadString(target, string.Empty, "expectedGenerationKey");
            if (!string.IsNullOrWhiteSpace(expectedGenerationKey))
            {
                generationKeys.Add(expectedGenerationKey);
                if (status.Equals("covered_exact", StringComparison.OrdinalIgnoreCase))
                {
                    coveredGenerationKeys.Add(expectedGenerationKey);
                }
            }

            if (targetType.Equals("factory_engine", StringComparison.OrdinalIgnoreCase))
            {
                factoryEngineIds.Add(ReadString(engine, string.Empty, "engineId"));
            }
            else if (targetType.Equals("authored_combination", StringComparison.OrdinalIgnoreCase))
            {
                combinationIds.Add(ReadString(engine, string.Empty, "combinationId"));
            }
        }

        return new AudioGenerationTargets(factoryEngineIds, combinationIds, generationKeys, coveredGenerationKeys);
    }

    private static string FormatTracking(CoverageResult coverage, bool tracked)
    {
        return coverage.Kind == CoverageKind.Exact
            ? string.Empty
            : tracked ? " [target tracked]" : " [target missing]";
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

        throw new FileNotFoundException($"Data or asset file was not found: {path}", path);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Engine audio coverage probe failed: {message}.");
        }
    }

    private sealed record EngineAudioProfile(
        string Id,
        string Path,
        string SourceEngineId,
        string SourceEngineFamily,
        IReadOnlyList<string> FallbackAllowedForFamilies);

    private sealed record CoverageResult(CoverageKind Kind, string Label)
    {
        public static CoverageResult Exact(string label)
        {
            return new CoverageResult(CoverageKind.Exact, label);
        }

        public static CoverageResult Fallback(string label)
        {
            return new CoverageResult(CoverageKind.Fallback, label);
        }

        public static CoverageResult Missing(string label)
        {
            return new CoverageResult(CoverageKind.Missing, label);
        }
    }

    private enum CoverageKind
    {
        Exact,
        Fallback,
        Missing
    }

    private sealed class CoverageSummary
    {
        public int Exact { get; private set; }
        public int Fallback { get; private set; }
        public int MissingExact { get; private set; }
        public int TrackedGaps { get; private set; }
        public int UntrackedGaps { get; private set; }

        public void Add(CoverageResult result, bool tracked)
        {
            if (result.Kind == CoverageKind.Exact)
            {
                Exact++;
            }
            else if (result.Kind == CoverageKind.Fallback)
            {
                Fallback++;
                MissingExact++;
                if (tracked)
                {
                    TrackedGaps++;
                }
                else
                {
                    UntrackedGaps++;
                }
            }
            else
            {
                MissingExact++;
                if (tracked)
                {
                    TrackedGaps++;
                }
                else
                {
                    UntrackedGaps++;
                }
            }
        }
    }

    private sealed class VehicleCoverageSummary
    {
        public int Exact { get; set; }
        public int Fallback { get; set; }
        public int TrackedFallback { get; set; }
        public int UntrackedFallback { get; set; }
        public int MissingRegisteredProfile { get; set; }
        public int UnauthorizedFallback { get; set; }
        public int MissingGenerationKey { get; set; }
    }

    private sealed record AudioGenerationTargets(
        IReadOnlySet<string> FactoryEngineIds,
        IReadOnlySet<string> CombinationIds,
        IReadOnlySet<string> GenerationKeys,
        IReadOnlySet<string> CoveredGenerationKeys);
}
