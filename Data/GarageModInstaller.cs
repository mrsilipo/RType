using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace RType.Data;

internal static class GarageModInstaller
{
    public static GarageModInstallResult ApplyOptionToFile(
        string ownedVehiclePath,
        string slot,
        string optionId,
        bool allowAdvisory = true,
        bool allowBlocked = false)
    {
        if (string.IsNullOrWhiteSpace(slot))
        {
            throw new ArgumentException("A garage option slot must be provided.", nameof(slot));
        }

        if (string.IsNullOrWhiteSpace(optionId))
        {
            throw new ArgumentException("A garage option id must be provided.", nameof(optionId));
        }

        string resolvedPath = ResolveDataPath(ownedVehiclePath);
        VehicleModPathReport report = VehicleModPathResolver.BuildReport(resolvedPath);
        EnsureOwnedVehicle(report.CurrentVehicle);

        GarageModInstallCandidate candidate = FindCandidate(report, slot, optionId);
        if (candidate.Status == GarageModInstallStatus.Blocked && !allowBlocked)
        {
            throw new InvalidOperationException(
                $"Garage option {optionId} in slot {slot} is blocked: {string.Join(", ", candidate.WarningCodes)}.");
        }

        if (candidate.Status == GarageModInstallStatus.Advisory && !allowAdvisory)
        {
            throw new InvalidOperationException(
                $"Garage option {optionId} in slot {slot} has advisory messages: {string.Join(", ", candidate.InfoCodes)}.");
        }

        JsonObject vehicle = ReadObject(resolvedPath);
        if (candidate.Kind == GarageModInstallKind.Engine)
        {
            ApplyEngineOption(vehicle, slot, optionId);
        }
        else
        {
            ApplyVehicleOption(vehicle, slot, optionId);
        }

        File.WriteAllText(resolvedPath, vehicle.ToJsonString(CreateIndentedJsonOptions()));
        ResolvedVehicleAssembly after = VehicleAssemblyResolver.Resolve(resolvedPath);
        GarageModInstallReceipt receipt = CreateReceipt(
            resolvedPath,
            slot,
            optionId,
            candidate.Kind,
            candidate.Status,
            report.CurrentVehicle,
            after);

        return new GarageModInstallResult(
            resolvedPath,
            slot,
            optionId,
            candidate.Kind,
            candidate.Status,
            report.CurrentVehicle,
            after,
            candidate.WarningCodes,
            candidate.InfoCodes,
            receipt);
    }

    public static GarageModInstallResult ApplyProfileOwnedOption(
        string profilePath,
        string ownedVehicleIdOrPath,
        string slot,
        string optionId,
        bool allowAdvisory = true)
    {
        GarageInventoryModPathReport inventoryReport = GarageInventoryModPathResolver.BuildReport(profilePath, ownedVehicleIdOrPath);
        GarageInventoryModOption option = inventoryReport.Options.FirstOrDefault(candidate =>
            candidate.Slot.Equals(slot, StringComparison.OrdinalIgnoreCase) &&
            candidate.Id.Equals(optionId, StringComparison.OrdinalIgnoreCase)) ??
            throw new InvalidOperationException($"Garage option {optionId} was not found for slot {slot}.");

        if (option.Availability == GarageInventoryAvailability.Installed)
        {
            throw new InvalidOperationException($"Garage option {optionId} is already installed in slot {slot}.");
        }

        if (option.Availability == GarageInventoryAvailability.BlockedByBuild)
        {
            throw new InvalidOperationException(
                $"Garage option {optionId} in slot {slot} is blocked by the current build: {string.Join(", ", option.WarningCodes)}.");
        }

        if (option.Availability == GarageInventoryAvailability.Locked)
        {
            throw new InvalidOperationException($"Garage option {optionId} in slot {slot} is locked for profile {inventoryReport.Profile.Id}.");
        }

        if (option.Availability == GarageInventoryAvailability.NotOwned)
        {
            throw new InvalidOperationException($"Garage option {optionId} in slot {slot} is not owned by profile {inventoryReport.Profile.Id}.");
        }

        if (option.Availability == GarageInventoryAvailability.Purchasable)
        {
            throw new InvalidOperationException(
                $"Garage option {optionId} in slot {slot} is purchasable but not owned. Route through the shop transaction layer first.");
        }

        return ApplyOptionToFile(inventoryReport.Vehicle.Path, slot, optionId, allowAdvisory);
    }

    private static void EnsureOwnedVehicle(ResolvedVehicleAssembly assembly)
    {
        if (!assembly.Classification.Equals("owned_vehicle", StringComparison.OrdinalIgnoreCase) || !assembly.PlayerOwned)
        {
            throw new InvalidOperationException(
                $"Garage installs can only mutate owned vehicles. Build {assembly.BuildId} is role '{assembly.Classification}', playerOwned={assembly.PlayerOwned}.");
        }
    }

    private static GarageModInstallCandidate FindCandidate(VehicleModPathReport report, string slot, string optionId)
    {
        EngineModOption? engineOption = report.Engine.Options.FirstOrDefault(option =>
            option.Slot.Equals(slot, StringComparison.OrdinalIgnoreCase) &&
            option.Id.Equals(optionId, StringComparison.OrdinalIgnoreCase));
        VehicleModOption? vehicleOption = report.VehicleOptions.FirstOrDefault(option =>
            option.Slot.Equals(slot, StringComparison.OrdinalIgnoreCase) &&
            option.Id.Equals(optionId, StringComparison.OrdinalIgnoreCase));

        if (engineOption is not null && vehicleOption is not null)
        {
            throw new InvalidOperationException($"Garage option {optionId} in slot {slot} is ambiguous across engine and vehicle catalogs.");
        }

        if (engineOption is not null)
        {
            return new GarageModInstallCandidate(
                GarageModInstallKind.Engine,
                ConvertStatus(engineOption.Status),
                engineOption.WarningCodes,
                engineOption.InfoCodes);
        }

        if (vehicleOption is not null)
        {
            return new GarageModInstallCandidate(
                GarageModInstallKind.Vehicle,
                ConvertStatus(vehicleOption.Status),
                vehicleOption.WarningCodes,
                vehicleOption.InfoCodes);
        }

        throw new InvalidOperationException($"Garage option {optionId} was not found for slot {slot}.");
    }

    private static GarageModInstallStatus ConvertStatus(EngineModOptionStatus status) => status switch
    {
        EngineModOptionStatus.Ready => GarageModInstallStatus.Ready,
        EngineModOptionStatus.Advisory => GarageModInstallStatus.Advisory,
        EngineModOptionStatus.Blocked => GarageModInstallStatus.Blocked,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    private static GarageModInstallStatus ConvertStatus(VehicleModOptionStatus status) => status switch
    {
        VehicleModOptionStatus.Ready => GarageModInstallStatus.Ready,
        VehicleModOptionStatus.Advisory => GarageModInstallStatus.Advisory,
        VehicleModOptionStatus.Blocked => GarageModInstallStatus.Blocked,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    private static void ApplyEngineOption(JsonObject vehicle, string slot, string optionId)
    {
        JsonObject engine = EnsureObject(EnsureObject(vehicle, "assembly"), "engine");
        if (slot.Equals("tuneId", StringComparison.OrdinalIgnoreCase))
        {
            engine["tuneId"] = optionId;
            return;
        }

        if (slot.Equals("fuel", StringComparison.OrdinalIgnoreCase))
        {
            EnsureObject(engine, "fuel")["selected"] = optionId;
            return;
        }

        if (slot.Equals("engineCombination", StringComparison.OrdinalIgnoreCase))
        {
            JsonObject combination = RequireCatalogItem("Data/Parts/Engine/engine_combinations.json", "combinations", optionId);
            engine["engineId"] = ReadString(combination, ReadString(engine, string.Empty, "engineId"), "baseEngineId");
            engine["blockId"] = ReadString(combination, string.Empty, "blockId");
            engine["headId"] = ReadString(combination, string.Empty, "headId");
            engine["combinationId"] = optionId;
            return;
        }

        if (!GarageModSlotMap.EngineInstalledSlots.Contains(slot))
        {
            throw new InvalidOperationException($"Engine slot {slot} is not installable by the garage installer.");
        }

        EnsureObject(engine, "installedParts")[slot] = optionId;
    }

    private static void ApplyVehicleOption(JsonObject vehicle, string slot, string optionId)
    {
        if (slot.Equals("tyrePackage", StringComparison.OrdinalIgnoreCase))
        {
            JsonObject package = RequireCatalogItem("Data/Parts/Tyres/tyre_packages.json", "parts", optionId);
            JsonObject data = package["data"] as JsonObject ??
                throw new InvalidDataException($"Tyre package {optionId} does not contain a data object.");
            JsonObject tyres = EnsureObject(EnsureObject(vehicle, "assembly"), "tyres");

            foreach ((string key, JsonNode? value) in data)
            {
                if (value is JsonValue jsonValue && jsonValue.GetValueKind() == JsonValueKind.String)
                {
                    tyres[key] = jsonValue.GetValue<string>();
                }
            }

            return;
        }

        if (!GarageModSlotMap.VehicleSlotPaths.TryGetValue(slot, out string[]? path))
        {
            throw new InvalidOperationException($"Vehicle slot {slot} is not installable by the garage installer.");
        }

        JsonObject node = EnsureObject(vehicle, "assembly");
        for (int index = 0; index < path.Length - 1; index++)
        {
            node = EnsureObject(node, path[index]);
        }

        node[path[^1]] = optionId;
    }

    private static JsonObject RequireCatalogItem(string catalogPath, string collectionName, string itemId)
    {
        JsonObject catalog = ReadObject(ResolveDataPath(catalogPath));
        JsonArray items = catalog[collectionName] as JsonArray ??
            throw new InvalidDataException($"Catalog {catalogPath} does not contain an array named {collectionName}.");
        foreach (JsonNode? item in items)
        {
            if (item is JsonObject itemObject &&
                ReadString(itemObject, string.Empty, "id").Equals(itemId, StringComparison.OrdinalIgnoreCase))
            {
                return CloneObject(itemObject);
            }
        }

        throw new InvalidDataException($"Catalog item {itemId} was not found in {catalogPath}.");
    }

    private static JsonObject EnsureObject(JsonObject root, string name)
    {
        if (root[name] is JsonObject existing)
        {
            return existing;
        }

        JsonObject created = [];
        root[name] = created;
        return created;
    }

    private static JsonObject ReadObject(string path)
    {
        JsonNode? node = JsonNode.Parse(File.ReadAllText(path));
        return node as JsonObject ?? throw new InvalidDataException($"JSON file is not an object: {path}");
    }

    private static JsonObject CloneObject(JsonObject source)
    {
        return JsonNode.Parse(source.ToJsonString()) as JsonObject ??
            throw new InvalidDataException("Failed to clone JSON object.");
    }

    private static string ReadString(JsonObject root, string fallback, params string[] path)
    {
        JsonNode? node = root;
        foreach (string segment in path)
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

    private static JsonSerializerOptions CreateIndentedJsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
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

        throw new FileNotFoundException($"Data file was not found: {path}", path);
    }

    private sealed record GarageModInstallCandidate(
        GarageModInstallKind Kind,
        GarageModInstallStatus Status,
        IReadOnlyList<string> WarningCodes,
        IReadOnlyList<string> InfoCodes);

    private static GarageModInstallReceipt CreateReceipt(
        string vehiclePath,
        string slot,
        string optionId,
        GarageModInstallKind kind,
        GarageModInstallStatus status,
        ResolvedVehicleAssembly before,
        ResolvedVehicleAssembly after)
    {
        return new GarageModInstallReceipt(
            DateTimeOffset.UtcNow,
            vehiclePath,
            before.BuildId,
            before.OwnerProfileId,
            before.GarageSlot,
            before.PurchaseCarId,
            slot,
            optionId,
            kind,
            status,
            before.Engine.EngineId,
            after.Engine.EngineId,
            before.Engine.FuelId,
            after.Engine.FuelId,
            before.Engine.TuneId,
            after.Engine.TuneId,
            before.Engine.TorqueCurve.Length == 0 ? 0f : before.Engine.TorqueCurve.Max(point => point.TorqueNm),
            after.Engine.TorqueCurve.Length == 0 ? 0f : after.Engine.TorqueCurve.Max(point => point.TorqueNm),
            before.MassProperties.TotalMassKg,
            after.MassProperties.TotalMassKg,
            before.MassProperties.FrontWeightDistribution,
            after.MassProperties.FrontWeightDistribution);
    }
}

internal sealed record GarageModInstallResult(
    string VehiclePath,
    string Slot,
    string OptionId,
    GarageModInstallKind Kind,
    GarageModInstallStatus Status,
    ResolvedVehicleAssembly Before,
    ResolvedVehicleAssembly After,
    IReadOnlyList<string> WarningCodes,
    IReadOnlyList<string> InfoCodes,
    GarageModInstallReceipt Receipt);

internal sealed record GarageModInstallReceipt(
    DateTimeOffset InstalledAtUtc,
    string VehiclePath,
    string BuildId,
    string OwnerProfileId,
    int GarageSlot,
    string PurchaseCarId,
    string Slot,
    string OptionId,
    GarageModInstallKind Kind,
    GarageModInstallStatus Status,
    string BeforeEngineId,
    string AfterEngineId,
    string BeforeFuelId,
    string AfterFuelId,
    string BeforeTuneId,
    string AfterTuneId,
    float BeforePeakTorqueNm,
    float AfterPeakTorqueNm,
    float BeforeTotalMassKg,
    float AfterTotalMassKg,
    float BeforeFrontWeightDistribution,
    float AfterFrontWeightDistribution);

internal enum GarageModInstallKind
{
    Engine,
    Vehicle
}

internal enum GarageModInstallStatus
{
    Ready,
    Advisory,
    Blocked
}
