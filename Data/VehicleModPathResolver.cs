using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace RType.Data;

internal static class VehicleModPathResolver
{
    private const string VehicleCatalogIndexPath = "Data/Parts/part_catalog_index.json";

    public static VehicleModPathReport BuildReport(string vehicleBuildPath)
    {
        ResolvedVehicleAssembly assembly = VehicleAssemblyResolver.Resolve(vehicleBuildPath);
        EngineModPathReport engine = EngineModPathResolver.BuildReport(vehicleBuildPath);
        VehicleCatalogBrowser catalogs = VehicleCatalogBrowser.Load(VehicleCatalogIndexPath);
        JsonObject sourceBuild = ReadBuild(vehicleBuildPath);

        VehicleAssemblyValidationMessage[] vehicleWarnings = [.. assembly.Validation
            .Where(message => message.Severity == VehicleAssemblyValidationSeverity.Warning)];
        EngineAssemblyValidationMessage[] engineWarnings = [.. assembly.Engine.Validation
            .Where(message => message.Severity == EngineAssemblyValidationSeverity.Warning)];
        VehicleAssemblyValidationMessage[] vehicleInfo = [.. assembly.Validation
            .Where(message => message.Severity == VehicleAssemblyValidationSeverity.Info)];
        EngineAssemblyValidationMessage[] engineInfo = [.. assembly.Engine.Validation
            .Where(message => message.Severity == EngineAssemblyValidationSeverity.Info)];
        VehicleModOption[] vehicleOptions = [.. BuildVehicleOptions(sourceBuild, assembly, catalogs)];

        return new VehicleModPathReport(
            assembly,
            engine,
            vehicleOptions,
            vehicleWarnings.Length == 0 && engineWarnings.Length == 0,
            vehicleWarnings,
            engineWarnings,
            vehicleInfo,
            engineInfo);
    }

    private static IEnumerable<VehicleModOption> BuildVehicleOptions(
        JsonObject sourceBuild,
        ResolvedVehicleAssembly current,
        VehicleCatalogBrowser catalogs)
    {
        foreach (VehicleCatalogItem item in catalogs.Items)
        {
            if (!GarageModSlotMap.VehicleCatalogSlotTargets.TryGetValue(item.CatalogSlot, out string[]? targetSlots))
            {
                continue;
            }

            foreach (string targetSlot in targetSlots)
            {
                if (targetSlot.Equals("tyrePackage", StringComparison.OrdinalIgnoreCase))
                {
                    if (IsSlotCompatible(item, current, targetSlot))
                    {
                        yield return EvaluateTyrePackageOption(sourceBuild, current, item);
                    }

                    continue;
                }

                if (!GarageModSlotMap.VehicleSlotPaths.TryGetValue(targetSlot, out string[]? path))
                {
                    continue;
                }

                if (!IsSlotCompatible(item, current, targetSlot))
                {
                    continue;
                }

                yield return EvaluateVehicleOption(sourceBuild, current, item, targetSlot, path);
            }
        }
    }

    private static VehicleModOption EvaluateTyrePackageOption(
        JsonObject sourceBuild,
        ResolvedVehicleAssembly currentAssembly,
        VehicleCatalogItem item)
    {
        string currentSignature = string.Join('|',
            ReadAssemblyString(sourceBuild, string.Empty, ["tyres", "frontCompound"]),
            ReadAssemblyString(sourceBuild, string.Empty, ["tyres", "rearCompound"]),
            ReadAssemblyString(sourceBuild, string.Empty, ["tyres", "frontModel"]),
            ReadAssemblyString(sourceBuild, string.Empty, ["tyres", "rearModel"]));
        string candidateSignature = string.Join('|',
            item.Data.TryGetValue("frontCompound", out string? frontCompound) ? frontCompound : string.Empty,
            item.Data.TryGetValue("rearCompound", out string? rearCompound) ? rearCompound : string.Empty,
            item.Data.TryGetValue("frontModel", out string? frontModel) ? frontModel : string.Empty,
            item.Data.TryGetValue("rearModel", out string? rearModel) ? rearModel : string.Empty);

        JsonObject candidate = CloneObject(sourceBuild);
        foreach ((string key, string value) in item.Data)
        {
            SetAssemblyString(candidate, value, ["tyres", key]);
        }

        ResolvedVehicleAssembly resolved = ResolveCandidate(candidate);
        (string[] warningCodes, string[] infoCodes) = BuildCandidateValidationCodes(currentAssembly, resolved);

        return new VehicleModOption(
            item.Id,
            item.DisplayName,
            "tyrePackage",
            item.CatalogSlot,
            item.Tier,
            item.Category,
            currentSignature.Equals(candidateSignature, StringComparison.OrdinalIgnoreCase),
            warningCodes.Length == 0,
            warningCodes,
            infoCodes,
            resolved.MassProperties.TotalMassKg,
            resolved.MassProperties.FrontWeightDistribution,
            resolved.MassProperties.CenterOfGravityHeightMeters,
            resolved.MassProperties.YawInertiaKgM2,
            resolved.RuntimeBuild.Drivetrain.GearboxId,
            resolved.RuntimeBuild.Drivetrain.FinalDriveId,
            resolved.RuntimeBuild.Drivetrain.DifferentialId);
    }

    private static VehicleModOption EvaluateVehicleOption(
        JsonObject sourceBuild,
        ResolvedVehicleAssembly currentAssembly,
        VehicleCatalogItem item,
        string slot,
        IReadOnlyList<string> assemblyPath)
    {
        string currentId = ReadAssemblyString(sourceBuild, string.Empty, assemblyPath);
        JsonObject candidate = CloneObject(sourceBuild);
        SetAssemblyString(candidate, item.Id, assemblyPath);

        ResolvedVehicleAssembly resolved = ResolveCandidate(candidate);
        (string[] warningCodes, string[] infoCodes) = BuildCandidateValidationCodes(currentAssembly, resolved);

        return new VehicleModOption(
            item.Id,
            item.DisplayName,
            slot,
            item.CatalogSlot,
            item.Tier,
            item.Category,
            currentId.Equals(item.Id, StringComparison.OrdinalIgnoreCase),
            warningCodes.Length == 0,
            warningCodes,
            infoCodes,
            resolved.MassProperties.TotalMassKg,
            resolved.MassProperties.FrontWeightDistribution,
            resolved.MassProperties.CenterOfGravityHeightMeters,
            resolved.MassProperties.YawInertiaKgM2,
            resolved.RuntimeBuild.Drivetrain.GearboxId,
            resolved.RuntimeBuild.Drivetrain.FinalDriveId,
            resolved.RuntimeBuild.Drivetrain.DifferentialId);
    }

    private static (string[] WarningCodes, string[] InfoCodes) BuildCandidateValidationCodes(
        ResolvedVehicleAssembly currentAssembly,
        ResolvedVehicleAssembly resolved)
    {
        HashSet<string> baselineWarningCodes = BuildValidationCodeSet(
            currentAssembly,
            VehicleAssemblyValidationSeverity.Warning,
            EngineAssemblyValidationSeverity.Warning);
        HashSet<string> baselineInfoCodes = BuildValidationCodeSet(
            currentAssembly,
            VehicleAssemblyValidationSeverity.Info,
            EngineAssemblyValidationSeverity.Info);
        string[] vehicleWarningCodes = [.. resolved.Validation
            .Where(message => message.Severity == VehicleAssemblyValidationSeverity.Warning)
            .Select(message => message.Code)
            .Where(code => !baselineWarningCodes.Contains(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)];
        string[] engineWarningCodes = [.. resolved.Engine.Validation
            .Where(message => message.Severity == EngineAssemblyValidationSeverity.Warning)
            .Select(message => $"engine:{message.Code}")
            .Where(code => !baselineWarningCodes.Contains(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)];
        string[] vehicleInfoCodes = [.. resolved.Validation
            .Where(message => message.Severity == VehicleAssemblyValidationSeverity.Info)
            .Select(message => message.Code)
            .Where(code => !baselineInfoCodes.Contains(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)];
        string[] engineInfoCodes = [.. resolved.Engine.Validation
            .Where(message => message.Severity == EngineAssemblyValidationSeverity.Info)
            .Select(message => $"engine:{message.Code}")
            .Where(code => !baselineInfoCodes.Contains(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)];

        string[] warningCodes = [.. vehicleWarningCodes.Concat(engineWarningCodes)];
        string[] infoCodes = [.. vehicleInfoCodes.Concat(engineInfoCodes)];

        return (warningCodes, infoCodes);
    }

    private static HashSet<string> BuildValidationCodeSet(
        ResolvedVehicleAssembly assembly,
        VehicleAssemblyValidationSeverity vehicleSeverity,
        EngineAssemblyValidationSeverity engineSeverity)
    {
        HashSet<string> codes = new(StringComparer.OrdinalIgnoreCase);
        foreach (VehicleAssemblyValidationMessage message in assembly.Validation.Where(message => message.Severity == vehicleSeverity))
        {
            codes.Add(message.Code);
        }

        foreach (EngineAssemblyValidationMessage message in assembly.Engine.Validation.Where(message => message.Severity == engineSeverity))
        {
            codes.Add($"engine:{message.Code}");
        }

        return codes;
    }

    private static bool IsSlotCompatible(VehicleCatalogItem item, ResolvedVehicleAssembly current, string targetSlot)
    {
        string requiredAxle = targetSlot.StartsWith("front", StringComparison.OrdinalIgnoreCase)
            ? "front"
            : targetSlot.StartsWith("rear", StringComparison.OrdinalIgnoreCase)
                ? "rear"
                : string.Empty;
        if (!string.IsNullOrWhiteSpace(requiredAxle) &&
            !string.IsNullOrWhiteSpace(item.Axle) &&
            !item.Axle.Equals(requiredAxle, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (item.Compatibility.Count == 0)
        {
            return true;
        }

        string chassisToken = current.ChassisCode.ToLowerInvariant();
        string drivetrainToken = current.DrivetrainLayout.Equals("FF", StringComparison.OrdinalIgnoreCase)
            ? "ff_transverse"
            : current.DrivetrainLayout.ToLowerInvariant();

        return item.Compatibility.Any(token =>
            token.Equals(chassisToken, StringComparison.OrdinalIgnoreCase) ||
            token.Equals(current.Engine.Family, StringComparison.OrdinalIgnoreCase) ||
            token.Equals(drivetrainToken, StringComparison.OrdinalIgnoreCase) ||
            token.Equals("15_inch", StringComparison.OrdinalIgnoreCase));
    }

    private static JsonObject ReadBuild(string vehicleBuildPath)
    {
        string resolvedPath = ResolveDataPath(vehicleBuildPath);
        JsonNode? root = JsonNode.Parse(File.ReadAllText(resolvedPath));
        return root as JsonObject ?? throw new InvalidDataException($"Vehicle build is not a JSON object: {vehicleBuildPath}");
    }

    private static ResolvedVehicleAssembly ResolveCandidate(JsonObject candidate)
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "rtype-vehicle-mod-path");
        Directory.CreateDirectory(tempDirectory);
        string tempPath = Path.Combine(tempDirectory, $"candidate-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tempPath, candidate.ToJsonString(CreateIndentedJsonOptions()));
            return VehicleAssemblyResolver.Resolve(tempPath);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static JsonSerializerOptions CreateIndentedJsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
    }

    private static JsonObject CloneObject(JsonObject source)
    {
        return JsonNode.Parse(source.ToJsonString()) as JsonObject ??
            throw new InvalidDataException("Failed to clone JSON object.");
    }

    private static string ReadAssemblyString(JsonObject root, string fallback, IReadOnlyList<string> assemblyPath)
    {
        JsonNode? node = root["assembly"];
        foreach (string segment in assemblyPath)
        {
            node = node is JsonObject current ? current[segment] : null;
            if (node is null)
            {
                return fallback;
            }
        }

        return node is JsonValue value && value.GetValueKind() == JsonValueKind.String
            ? value.GetValue<string>()
            : fallback;
    }

    private static void SetAssemblyString(JsonObject root, string value, IReadOnlyList<string> assemblyPath)
    {
        if (root["assembly"] is not JsonObject assembly)
        {
            assembly = [];
            root["assembly"] = assembly;
        }

        JsonObject current = assembly;
        for (int i = 0; i < assemblyPath.Count - 1; i++)
        {
            string segment = assemblyPath[i];
            if (current[segment] is not JsonObject next)
            {
                next = [];
                current[segment] = next;
            }

            current = next;
        }

        current[assemblyPath[^1]] = value;
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

    private sealed class VehicleCatalogBrowser
    {
        private readonly List<VehicleCatalogItem> _items = [];

        public IEnumerable<VehicleCatalogItem> Items => _items;

        public static VehicleCatalogBrowser Load(string path)
        {
            VehicleCatalogBrowser browser = new();
            browser.LoadPath(path);
            return browser;
        }

        private void LoadPath(string path)
        {
            string resolvedPath = ResolveDataPath(path);
            using FileStream stream = File.OpenRead(resolvedPath);
            using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("catalogs", out JsonElement catalogs))
            {
                return;
            }

            foreach (JsonElement catalog in catalogs.EnumerateArray())
            {
                LoadCatalog(
                    ReadString(catalog, string.Empty, "path"),
                    ReadString(catalog, string.Empty, "slot"));
            }
        }

        private void LoadCatalog(string path, string fallbackSlot)
        {
            string resolvedPath = ResolveDataPath(path);
            using FileStream stream = File.OpenRead(resolvedPath);
            using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
            JsonElement root = document.RootElement;
            string catalogSlot = ReadString(root, fallbackSlot, "slot");
            JsonElement array = root.TryGetProperty("parts", out JsonElement parts) ? parts : default;
            if (array.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (JsonElement item in array.EnumerateArray())
            {
                string id = ReadString(item, string.Empty, "id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                _items.Add(new VehicleCatalogItem(
                    id,
                    ReadString(item, id, "displayName"),
                    catalogSlot,
                    ReadString(item, string.Empty, "tier"),
                    ReadString(item, string.Empty, "category"),
                    ReadString(item, string.Empty, "axle"),
                    ReadStringArray(item, "compatibility"),
                    ReadStringMap(item, "data")));
            }
        }

        private static string ReadString(JsonElement root, string fallback, params string[] path)
        {
            return TryGet(root, out JsonElement value, path) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? fallback
                : fallback;
        }

        private static IReadOnlyList<string> ReadStringArray(JsonElement root, params string[] path)
        {
            if (!TryGet(root, out JsonElement value, path) || value.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return [.. value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? string.Empty)
                .Where(item => !string.IsNullOrWhiteSpace(item))];
        }

        private static IReadOnlyDictionary<string, string> ReadStringMap(JsonElement root, params string[] path)
        {
            if (!TryGet(root, out JsonElement value, path) || value.ValueKind != JsonValueKind.Object)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    result[property.Name] = property.Value.GetString() ?? string.Empty;
                }
            }

            return result;
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
    }

    private sealed record VehicleCatalogItem(
        string Id,
        string DisplayName,
        string CatalogSlot,
        string Tier,
        string Category,
        string Axle,
        IReadOnlyList<string> Compatibility,
        IReadOnlyDictionary<string, string> Data);
}

internal sealed record VehicleModPathReport(
    ResolvedVehicleAssembly CurrentVehicle,
    EngineModPathReport Engine,
    IReadOnlyList<VehicleModOption> VehicleOptions,
    bool CurrentBuildIsClean,
    IReadOnlyList<VehicleAssemblyValidationMessage> VehicleWarnings,
    IReadOnlyList<EngineAssemblyValidationMessage> EngineWarnings,
    IReadOnlyList<VehicleAssemblyValidationMessage> VehicleInfo,
    IReadOnlyList<EngineAssemblyValidationMessage> EngineInfo)
{
    public int ReadyEngineOptionCount => Engine.Ready.Count();
    public int AdvisoryEngineOptionCount => Engine.Advisory.Count();
    public int BlockedEngineOptionCount => Engine.Blocked.Count();
    public int InstalledEngineOptionCount => Engine.Installed.Count();
    public IEnumerable<VehicleModOption> ReadyVehicleOptions => VehicleOptions.Where(option => option.Status == VehicleModOptionStatus.Ready);
    public IEnumerable<VehicleModOption> AdvisoryVehicleOptions => VehicleOptions.Where(option => option.Status == VehicleModOptionStatus.Advisory);
    public IEnumerable<VehicleModOption> BlockedVehicleOptions => VehicleOptions.Where(option => option.Status == VehicleModOptionStatus.Blocked);
    public IEnumerable<VehicleModOption> InstalledVehicleOptions => VehicleOptions.Where(option => option.IsInstalled);

    public IReadOnlyList<VehicleModPathSlotGroup> VehicleGroups =>
    [
        .. VehicleOptions
            .GroupBy(option => option.Slot, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new VehicleModPathSlotGroup(
                group.Key,
                [.. group.Where(option => option.IsInstalled).OrderBy(option => option.Id, StringComparer.OrdinalIgnoreCase)],
                [.. group.Where(option => option.Status == VehicleModOptionStatus.Ready).OrderBy(option => option.Id, StringComparer.OrdinalIgnoreCase)],
                [.. group.Where(option => option.Status == VehicleModOptionStatus.Advisory).OrderBy(option => option.Id, StringComparer.OrdinalIgnoreCase)],
                [.. group.Where(option => option.Status == VehicleModOptionStatus.Blocked).OrderBy(option => option.Id, StringComparer.OrdinalIgnoreCase)]))
    ];
}

internal sealed record VehicleModOption(
    string Id,
    string DisplayName,
    string Slot,
    string CatalogSlot,
    string Tier,
    string Category,
    bool IsInstalled,
    bool Selectable,
    IReadOnlyList<string> WarningCodes,
    IReadOnlyList<string> InfoCodes,
    float TotalMassKg,
    float FrontWeightDistribution,
    float CenterOfGravityHeightMeters,
    float YawInertiaKgM2,
    string GearboxId,
    string FinalDriveId,
    string DifferentialId)
{
    public VehicleModOptionStatus Status => !Selectable
        ? VehicleModOptionStatus.Blocked
        : InfoCodes.Count == 0
            ? VehicleModOptionStatus.Ready
            : VehicleModOptionStatus.Advisory;
}

internal sealed record VehicleModPathSlotGroup(
    string Slot,
    IReadOnlyList<VehicleModOption> Installed,
    IReadOnlyList<VehicleModOption> Ready,
    IReadOnlyList<VehicleModOption> Advisory,
    IReadOnlyList<VehicleModOption> Blocked);

internal enum VehicleModOptionStatus
{
    Ready,
    Advisory,
    Blocked
}
