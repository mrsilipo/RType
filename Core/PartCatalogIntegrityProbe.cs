using System.Text.Json;
using RType.Data;

namespace RType.Core;

internal static class PartCatalogIntegrityProbe
{
    private static readonly string[] CatalogIndexes =
    [
        "Data/Parts/part_catalog_index.json",
        "Data/Parts/Engine/part_catalog_index.json",
        "Data/Tunes/Chassis/chassis_tune_index.json"
    ];

    private static readonly string[] DirectCatalogs =
    [
        "Data/Tunes/Engine/engine_tunes.json",
        "Data/Tunes/Engine/fuels.json"
    ];

    private static readonly HashSet<string> NonInstallableEngineCatalogSlots = new(StringComparer.OrdinalIgnoreCase)
    {
        "engine",
        "engineBlock",
        "engineHead",
        "engineCombination"
    };

    private static readonly HashSet<string> RequiredEngineInstalledSlots = new(StringComparer.OrdinalIgnoreCase)
    {
        "blockUpgrade",
        "headUpgrade",
        "cams",
        "displacement",
        "portPolishing",
        "throttleBody",
        "intake",
        "intakeRunnerLength",
        "valveSprings",
        "headers",
        "exhaust",
        "flywheel",
        "clutch",
        "engineAudioDsp"
    };

    private static readonly HashSet<string> NonInstallableVehicleCatalogSlots = new(StringComparer.OrdinalIgnoreCase)
    {
        "bodyShell",
        "swapKit",
        "tyreModel"
    };

    private static readonly HashSet<string> RequiredVehicleInstallSlots = new(StringComparer.OrdinalIgnoreCase)
    {
        "gearbox",
        "finalDrive",
        "differential",
        "frontSuspension",
        "rearSuspension",
        "alignment",
        "frontBrakes",
        "rearBrakes",
        "brakeSystem",
        "frontWheels",
        "rearWheels",
        "frontTyres",
        "rearTyres",
        "aeroPackage",
        "tyrePackage"
    };

    public static void Run()
    {
        List<CatalogFile> catalogs = [];
        foreach (string indexPath in CatalogIndexes)
        {
            catalogs.AddRange(ReadCatalogIndex(indexPath));
        }

        catalogs.AddRange(DirectCatalogs.Select(path => new CatalogFile("direct", path, path)));

        Dictionary<string, CatalogItem> ids = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> catalogPaths = new(StringComparer.OrdinalIgnoreCase);
        List<string> inheritedIds = [];
        int itemCount = 0;

        Console.WriteLine("Part catalog integrity probe");
        foreach (CatalogFile catalog in catalogs.OrderBy(catalog => catalog.Path, StringComparer.OrdinalIgnoreCase))
        {
            ValidateActiveCatalogPath(catalog.Path);
            Require(catalogPaths.Add(NormalizePath(catalog.Path)), $"catalog path is indexed more than once: {catalog.Path}");

            using JsonDocument document = ReadDocument(catalog.Path);
            JsonElement root = document.RootElement;
            string rootSlot = ReadString(root, string.Empty, "slot");
            if (!string.IsNullOrWhiteSpace(rootSlot))
            {
                Require(rootSlot.Equals(catalog.Slot, StringComparison.OrdinalIgnoreCase) || catalog.Slot.Equals("direct", StringComparison.OrdinalIgnoreCase),
                    $"{catalog.Path} declares slot '{rootSlot}' but index slot is '{catalog.Slot}'");
            }

            CatalogItem[] items = [.. EnumerateCatalogItems(root, catalog.Path, catalog.Slot)];
            Require(items.Length > 0, $"{catalog.Path} does not contain id-bearing catalog items");

            foreach (CatalogItem item in items)
            {
                if (ids.TryGetValue(item.Id, out CatalogItem? existing))
                {
                    throw new InvalidOperationException($"Part catalog integrity probe failed: duplicate catalog id '{item.Id}' in {item.Path} and {existing.Path}.");
                }

                ids[item.Id] = item;
                itemCount++;
                if (!string.IsNullOrWhiteSpace(item.Inherits))
                {
                    inheritedIds.Add(item.Inherits);
                }
            }

            Console.WriteLine($"  {catalog.Slot}: {catalog.Path}, {items.Length} items");
        }

        foreach (string inheritedId in inheritedIds)
        {
            Require(ids.ContainsKey(inheritedId), $"inherited catalog id '{inheritedId}' is not present in active catalogs");
        }

        ValidateLegacyDirectoryIsEmpty("Data/RTypeEngineProfiles");
        ValidateLegacyDirectoryIsEmpty("Data/Setups");
        ValidateLegacyDirectoryIsEmpty("Data/Tyres");
        ValidateLegacyDirectoryIsEmpty("Data/Vehicles");
        ValidateRuntimeDirectoryIsEmpty("Data/Legacy");
        ValidateEngineSlotMap(catalogs);
        ValidateVehicleSlotMap(catalogs);
        ValidateTyrePackages(ids);
        ValidateVehicleBuildSlots(ids);

        Console.WriteLine($"  result: PASS ({catalogs.Count} catalogs, {itemCount} unique active ids, {inheritedIds.Count} inheritance links)");
    }

    private static IEnumerable<CatalogFile> ReadCatalogIndex(string indexPath)
    {
        ValidateActiveCatalogPath(indexPath);
        using JsonDocument document = ReadDocument(indexPath);
        JsonElement root = document.RootElement;
        Require(root.TryGetProperty("catalogs", out JsonElement catalogs) && catalogs.ValueKind == JsonValueKind.Array,
            $"{indexPath} does not contain a catalogs array");

        HashSet<string> slots = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement catalog in catalogs.EnumerateArray())
        {
            string slot = ReadString(catalog, string.Empty, "slot");
            string path = ReadString(catalog, string.Empty, "path");
            Require(!string.IsNullOrWhiteSpace(slot), $"{indexPath} has a catalog entry without slot");
            Require(!string.IsNullOrWhiteSpace(path), $"{indexPath} has a catalog entry without path");
            Require(slots.Add(slot), $"{indexPath} declares duplicate slot '{slot}'");
            ValidateActiveCatalogPath(path);
            yield return new CatalogFile(slot, path, indexPath);
        }
    }

    private static IEnumerable<CatalogItem> EnumerateCatalogItems(JsonElement root, string catalogPath, string slot)
    {
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array ||
                property.NameEquals("catalogs"))
            {
                continue;
            }

            foreach (JsonElement item in property.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("id", out JsonElement idElement) ||
                    idElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string id = idElement.GetString() ?? string.Empty;
                Require(!string.IsNullOrWhiteSpace(id), $"{catalogPath} contains a blank id");
                yield return new CatalogItem(
                    id,
                    catalogPath,
                    slot,
                    ReadString(item, string.Empty, "inherits"));
            }
        }
    }

    private static JsonDocument ReadDocument(string path)
    {
        string resolvedPath = ResolveDataPath(path);
        Require(File.Exists(resolvedPath), $"missing catalog file: {path}");
        return JsonDocument.Parse(File.ReadAllText(resolvedPath), new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });
    }

    private static void ValidateActiveCatalogPath(string path)
    {
        string normalized = NormalizePath(path);
        Require(!normalized.Contains("data/legacy/", StringComparison.OrdinalIgnoreCase), $"active catalog path points into legacy data: {path}");
        Require(!normalized.Contains("data/rtypeengineprofiles/", StringComparison.OrdinalIgnoreCase), $"active catalog path points into old RTypeEngineProfiles data: {path}");
    }

    private static string ResolveDataPath(string path)
    {
        return Path.IsPathRooted(path)
            ? path
            : Path.Combine(AppContext.BaseDirectory, path);
    }

    private static void ValidateLegacyDirectoryIsEmpty(string path)
    {
        foreach (string root in CandidateRoots())
        {
            string resolvedPath = Path.Combine(root, path);
            if (!Directory.Exists(resolvedPath))
            {
                continue;
            }

            Require(!Directory.EnumerateFiles(resolvedPath, "*", SearchOption.AllDirectories).Any(),
                $"legacy directory exists with live files: {resolvedPath}");
        }
    }

    private static void ValidateRuntimeDirectoryIsEmpty(string path)
    {
        string resolvedPath = Path.Combine(AppContext.BaseDirectory, path);
        if (!Directory.Exists(resolvedPath))
        {
            return;
        }

        Require(!Directory.EnumerateFiles(resolvedPath, "*", SearchOption.AllDirectories).Any(),
            $"runtime output contains legacy files: {resolvedPath}");
    }

    private static void ValidateEngineSlotMap(IEnumerable<CatalogFile> catalogs)
    {
        CatalogFile[] engineCatalogs = [.. catalogs.Where(catalog =>
            NormalizePath(catalog.Path).StartsWith("Data/Parts/Engine/", StringComparison.OrdinalIgnoreCase))];

        foreach (CatalogFile catalog in engineCatalogs)
        {
            if (NonInstallableEngineCatalogSlots.Contains(catalog.Slot))
            {
                continue;
            }

            Require(GarageModSlotMap.EngineCatalogSlotToInstalledSlot.ContainsKey(catalog.Slot),
                $"engine catalog slot '{catalog.Slot}' has no garage installed-slot mapping");
        }

        HashSet<string> mappedInstalledSlots = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string catalogSlot, string installedSlot) in GarageModSlotMap.EngineCatalogSlotToInstalledSlot)
        {
            Require(!string.IsNullOrWhiteSpace(catalogSlot), "engine catalog slot map contains a blank catalog slot");
            Require(!string.IsNullOrWhiteSpace(installedSlot), $"engine catalog slot '{catalogSlot}' maps to a blank installed slot");
            Require(engineCatalogs.Any(catalog => catalog.Slot.Equals(catalogSlot, StringComparison.OrdinalIgnoreCase)),
                $"engine catalog slot map references missing catalog slot '{catalogSlot}'");
            Require(mappedInstalledSlots.Add(installedSlot),
                $"engine installed slot '{installedSlot}' is mapped by more than one catalog slot");
        }

        foreach (string requiredSlot in RequiredEngineInstalledSlots)
        {
            Require(mappedInstalledSlots.Contains(requiredSlot),
                $"required engine installed slot '{requiredSlot}' is not covered by the catalog slot map");
            Require(GarageModSlotMap.EngineInstalledSlots.Contains(requiredSlot),
                $"required engine installed slot '{requiredSlot}' is missing from GarageModSlotMap.EngineInstalledSlots");
        }
    }

    private static void ValidateVehicleSlotMap(IEnumerable<CatalogFile> catalogs)
    {
        CatalogFile[] vehicleCatalogs = [.. catalogs.Where(catalog =>
            NormalizePath(catalog.SourceIndex).Equals("Data/Parts/part_catalog_index.json", StringComparison.OrdinalIgnoreCase))];

        foreach (CatalogFile catalog in vehicleCatalogs)
        {
            if (NonInstallableVehicleCatalogSlots.Contains(catalog.Slot))
            {
                continue;
            }

            Require(GarageModSlotMap.VehicleCatalogSlotTargets.ContainsKey(catalog.Slot),
                $"vehicle catalog slot '{catalog.Slot}' has no garage target-slot mapping");
        }

        HashSet<string> mappedTargetSlots = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string catalogSlot, string[] targetSlots) in GarageModSlotMap.VehicleCatalogSlotTargets)
        {
            Require(vehicleCatalogs.Any(catalog => catalog.Slot.Equals(catalogSlot, StringComparison.OrdinalIgnoreCase)),
                $"vehicle catalog slot target map references missing catalog slot '{catalogSlot}'");
            Require(targetSlots.Length > 0, $"vehicle catalog slot '{catalogSlot}' maps to no target slots");

            foreach (string targetSlot in targetSlots)
            {
                Require(!string.IsNullOrWhiteSpace(targetSlot), $"vehicle catalog slot '{catalogSlot}' maps to a blank target slot");
                Require(targetSlot.Equals("tyrePackage", StringComparison.OrdinalIgnoreCase) ||
                    GarageModSlotMap.VehicleSlotPaths.ContainsKey(targetSlot),
                    $"vehicle catalog slot '{catalogSlot}' maps to unknown target slot '{targetSlot}'");
                Require(mappedTargetSlots.Add(targetSlot),
                    $"vehicle target slot '{targetSlot}' is mapped by more than one catalog slot");
            }
        }

        foreach ((string targetSlot, string[] path) in GarageModSlotMap.VehicleSlotPaths)
        {
            Require(path.Length > 0, $"vehicle target slot '{targetSlot}' maps to an empty assembly path");
            Require(path.All(segment => !string.IsNullOrWhiteSpace(segment)),
                $"vehicle target slot '{targetSlot}' maps to a blank assembly path segment");
        }

        foreach (string requiredSlot in RequiredVehicleInstallSlots)
        {
            Require(mappedTargetSlots.Contains(requiredSlot) || GarageModSlotMap.VehicleSlotPaths.ContainsKey(requiredSlot),
                $"required vehicle install slot '{requiredSlot}' is not covered by the catalog slot target map");
        }
    }

    private static void ValidateTyrePackages(IReadOnlyDictionary<string, CatalogItem> ids)
    {
        using JsonDocument document = ReadDocument("Data/Parts/Tyres/tyre_packages.json");
        JsonElement root = document.RootElement;
        JsonElement[] packageItems = [.. EnumerateJsonCatalogItems(root)];
        Require(packageItems.Length > 0, "Data/Parts/Tyres/tyre_packages.json does not contain tyre package items");

        foreach (JsonElement item in packageItems)
        {
            string id = ReadString(item, string.Empty, "id");
            JsonElement data = RequireElement(item, "data");
            RequirePackageReference(ids, id, data, "frontCompound", "tyres");
            RequirePackageReference(ids, id, data, "rearCompound", "tyres");
            RequirePackageReference(ids, id, data, "frontModel", "tyreModel");
            RequirePackageReference(ids, id, data, "rearModel", "tyreModel");
        }
    }

    private static IEnumerable<JsonElement> EnumerateJsonCatalogItems(JsonElement root)
    {
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array ||
                property.NameEquals("catalogs"))
            {
                continue;
            }

            foreach (JsonElement item in property.Value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object &&
                    item.TryGetProperty("id", out JsonElement idElement) &&
                    idElement.ValueKind == JsonValueKind.String)
                {
                    yield return item;
                }
            }
        }
    }

    private static void RequirePackageReference(
        IReadOnlyDictionary<string, CatalogItem> ids,
        string packageId,
        JsonElement data,
        string propertyName,
        string expectedSlot)
    {
        string referencedId = ReadString(data, string.Empty, propertyName);
        Require(!string.IsNullOrWhiteSpace(referencedId), $"tyre package {packageId} is missing data.{propertyName}");
        if (!ids.TryGetValue(referencedId, out CatalogItem? item))
        {
            throw new InvalidOperationException($"Part catalog integrity probe failed: tyre package {packageId} references missing {propertyName} id '{referencedId}'.");
        }

        Require(item.Slot.Equals(expectedSlot, StringComparison.OrdinalIgnoreCase),
            $"tyre package {packageId} references {referencedId} for {propertyName}, but that id belongs to slot '{item.Slot}' instead of '{expectedSlot}'");
    }

    private static void ValidateVehicleBuildSlots(IReadOnlyDictionary<string, CatalogItem> ids)
    {
        string[] buildRoots =
        [
            "Data/PurchaseCars",
            "Data/Garage/OwnedVehicles"
        ];

        foreach (string root in buildRoots)
        {
            string resolvedRoot = Path.Combine(Environment.CurrentDirectory, root);
            if (!Directory.Exists(resolvedRoot))
            {
                continue;
            }

            foreach (string buildPath in Directory.GetFiles(resolvedRoot, "*.json", SearchOption.TopDirectoryOnly))
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(buildPath), new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });

                JsonElement assembly = RequireElement(document.RootElement, "assembly");
                string displayPath = Path.GetRelativePath(Environment.CurrentDirectory, buildPath).Replace('\\', '/');
                ValidateVehicleBuildSlot(ids, assembly, displayPath, "bodyShell", "chassis", "bodyShell");
                ValidateVehicleBuildSlot(ids, assembly, displayPath, "gearbox", "drivetrain", "gearbox");
                ValidateVehicleBuildSlot(ids, assembly, displayPath, "finalDrive", "drivetrain", "finalDrive");
                ValidateVehicleBuildSlot(ids, assembly, displayPath, "differential", "drivetrain", "differential");
                ValidateVehicleBuildSlot(ids, assembly, displayPath, "suspension", "suspension", "front");
                ValidateVehicleBuildSlot(ids, assembly, displayPath, "suspension", "suspension", "rear");
                ValidateVehicleBuildSlot(ids, assembly, displayPath, "alignment", "suspension", "alignment");
                ValidateVehicleBuildSlot(ids, assembly, displayPath, "brakes", "brakes", "front");
                ValidateVehicleBuildSlot(ids, assembly, displayPath, "brakes", "brakes", "rear");
                ValidateVehicleBuildSlot(ids, assembly, displayPath, "brakeSystem", "brakes", "system");
                ValidateVehicleBuildSlot(ids, assembly, displayPath, "wheels", "wheels", "front");
                ValidateVehicleBuildSlot(ids, assembly, displayPath, "wheels", "wheels", "rear");
                ValidateVehicleBuildSlot(ids, assembly, displayPath, "tyres", "tyres", "frontCompound");
                ValidateVehicleBuildSlot(ids, assembly, displayPath, "tyres", "tyres", "rearCompound");
                ValidateVehicleBuildSlot(ids, assembly, displayPath, "tyreModel", "tyres", "frontModel");
                ValidateVehicleBuildSlot(ids, assembly, displayPath, "tyreModel", "tyres", "rearModel");
                ValidateVehicleBuildSlot(ids, assembly, displayPath, "aeroPackage", "aero", "package");
                ValidateSwapKitSlots(ids, assembly, displayPath);
            }
        }
    }

    private static void ValidateVehicleBuildSlot(
        IReadOnlyDictionary<string, CatalogItem> ids,
        JsonElement assembly,
        string buildPath,
        string expectedSlot,
        params string[] path)
    {
        string partId = ReadString(assembly, string.Empty, path);
        if (string.IsNullOrWhiteSpace(partId))
        {
            return;
        }

        if (!ids.TryGetValue(partId, out CatalogItem? item))
        {
            throw new InvalidOperationException($"Part catalog integrity probe failed: {buildPath} assembly.{string.Join(".", path)} references missing catalog id '{partId}'.");
        }

        Require(item.Slot.Equals(expectedSlot, StringComparison.OrdinalIgnoreCase),
            $"{buildPath} assembly.{string.Join(".", path)} references {partId}, but that id belongs to slot '{item.Slot}' instead of '{expectedSlot}'");
    }

    private static void ValidateSwapKitSlots(
        IReadOnlyDictionary<string, CatalogItem> ids,
        JsonElement assembly,
        string buildPath)
    {
        if (!assembly.TryGetProperty("swapKits", out JsonElement swapKits) ||
            swapKits.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty property in swapKits.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string partId = property.Value.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(partId))
            {
                continue;
            }

            if (!ids.TryGetValue(partId, out CatalogItem? item))
            {
                throw new InvalidOperationException($"Part catalog integrity probe failed: {buildPath} assembly.swapKits.{property.Name} references missing catalog id '{partId}'.");
            }

            Require(item.Slot.Equals("swapKit", StringComparison.OrdinalIgnoreCase),
                $"{buildPath} assembly.swapKits.{property.Name} references {partId}, but that id belongs to slot '{item.Slot}' instead of 'swapKit'");
        }
    }

    private static IEnumerable<string> CandidateRoots()
    {
        yield return AppContext.BaseDirectory;

        string current = Directory.GetCurrentDirectory();
        if (!current.Equals(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
        {
            yield return current;
        }
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').Trim();
    }

    private static string ReadString(JsonElement root, string fallback, params string[] path)
    {
        JsonElement value = root;
        foreach (string segment in path)
        {
            if (value.ValueKind != JsonValueKind.Object ||
                !value.TryGetProperty(segment, out value))
            {
                return fallback;
            }
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    private static JsonElement RequireElement(JsonElement root, params string[] path)
    {
        JsonElement value = root;
        foreach (string segment in path)
        {
            if (value.ValueKind != JsonValueKind.Object ||
                !value.TryGetProperty(segment, out value))
            {
                throw new InvalidOperationException($"Part catalog integrity probe failed: missing JSON path '{string.Join(".", path)}'.");
            }
        }

        return value;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Part catalog integrity probe failed: {message}.");
        }
    }

    private sealed record CatalogFile(string Slot, string Path, string SourceIndex);

    private sealed record CatalogItem(string Id, string Path, string Slot, string Inherits);
}
