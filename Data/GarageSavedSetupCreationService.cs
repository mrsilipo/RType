using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace RType.Data;

internal static class GarageSavedSetupCreationService
{
    public static GarageSavedSetupCreationResult CreateFromOwnedVehicle(
        string profilePath,
        string ownedVehicleIdOrPath,
        string setupOutputDirectory,
        string displayName,
        bool makeActive = false)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Saved setup display name must be provided.", nameof(displayName));
        }

        GarageProfile profile = GarageProfileLoader.Load(profilePath);
        GarageOwnedVehicleReference vehicle = FindOwnedVehicle(profile, ownedVehicleIdOrPath);
        string vehiclePath = ResolveDataPath(vehicle.Path);
        JsonObject ownedVehicle = ReadObject(vehiclePath);
        ResolvedVehicleAssembly resolved = VehicleAssemblyResolver.Resolve(vehiclePath);
        ThrowIfWarnings(resolved);

        string setupId = NextSetupId(profile, vehicle.VehicleId);
        JsonObject setup = CreateSetupJson(profile.Id, vehicle.VehicleId, setupId, displayName, ownedVehicle);
        Directory.CreateDirectory(setupOutputDirectory);
        string setupPath = Path.Combine(setupOutputDirectory, $"{setupId}.json");
        if (File.Exists(setupPath))
        {
            throw new IOException($"Saved setup already exists: {setupPath}");
        }

        string tempRoot = Path.Combine(Path.GetTempPath(), "rtype-setup-create-validation", Guid.NewGuid().ToString("N"));
        string tempSetupPath = Path.Combine(tempRoot, $"{setupId}.json");
        Directory.CreateDirectory(tempRoot);

        try
        {
            File.WriteAllText(tempSetupPath, setup.ToJsonString(CreateJsonOptions()));
            GarageResolvedSetupVehicle validated = GarageSavedSetupResolver.ResolveWithSetupFile(profilePath, vehicle.VehicleId, tempSetupPath);
            ThrowIfWarnings(validated);
            File.WriteAllText(setupPath, setup.ToJsonString(CreateJsonOptions()));

            string resolvedProfilePath = ResolveDataPath(profilePath);
            JsonObject profileJson = ReadObject(resolvedProfilePath);
            JsonArray savedSetups = EnsureArray(profileJson, "savedSetups");
            if (makeActive)
            {
                ClearActiveSetupsForVehicle(savedSetups, vehicle.VehicleId);
            }

            savedSetups.Add(new JsonObject
            {
                ["setupId"] = setupId,
                ["vehicleId"] = vehicle.VehicleId,
                ["path"] = NormalizeDataPath(setupPath),
                ["displayName"] = displayName,
                ["active"] = makeActive
            });
            File.WriteAllText(resolvedProfilePath, profileJson.ToJsonString(CreateJsonOptions()));

            GarageProfile updatedProfile = GarageProfileLoader.Load(resolvedProfilePath);
            GarageSavedSetup created = GarageSavedSetupLoader.Load(setupPath);
            GarageRuntimeVehicleSelection runtime = GarageRuntimeVehicleResolver.Resolve(
                resolvedProfilePath,
                vehicle.VehicleId,
                makeActive ? setupId : "active");

            return new GarageSavedSetupCreationResult(
                resolvedProfilePath,
                setupPath,
                vehicle.VehicleId,
                setupId,
                displayName,
                makeActive,
                created,
                updatedProfile,
                runtime);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static JsonObject CreateSetupJson(
        string profileId,
        string vehicleId,
        string setupId,
        string displayName,
        JsonObject ownedVehicle)
    {
        JsonObject assembly = RequireObject(ownedVehicle, "assembly");
        JsonObject engine = RequireObject(assembly, "engine");
        JsonObject suspension = RequireObject(assembly, "suspension");
        JsonObject tuning = RequireObject(assembly, "tuning");

        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["id"] = setupId,
            ["displayName"] = displayName,
            ["ownerProfileId"] = profileId,
            ["vehicleId"] = vehicleId,
            ["setupType"] = "saved_setup",
            ["createdAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["overrides"] = new JsonObject
            {
                ["engine"] = new JsonObject
                {
                    ["tuneId"] = ReadString(engine, string.Empty, "tuneId"),
                    ["fuelSelected"] = ReadString(engine, string.Empty, "fuel", "selected")
                },
                ["suspension"] = new JsonObject
                {
                    ["alignment"] = ReadString(suspension, string.Empty, "alignment")
                },
                ["tuning"] = new JsonObject
                {
                    ["steering"] = ReadString(tuning, string.Empty, "steering"),
                    ["handling"] = ReadString(tuning, string.Empty, "handling")
                }
            },
            ["notes"] = new JsonArray
            {
                "Saved setup snapshot. It records tune-like selected setup ids only and does not install permanent hardware."
            }
        };
    }

    private static string NextSetupId(GarageProfile profile, string vehicleId)
    {
        int next = 1;
        string prefix = $"{vehicleId}_setup_";
        foreach (GarageSavedSetupReference setup in profile.SavedSetups
                     .Where(setup => setup.VehicleId.Equals(vehicleId, StringComparison.OrdinalIgnoreCase)))
        {
            string id = Path.GetFileNameWithoutExtension(setup.SetupId);
            if (id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(id[prefix.Length..], out int numeric))
            {
                next = Math.Max(next, numeric + 1);
            }
        }

        string candidate;
        do
        {
            candidate = $"{prefix}{next:000}";
            next++;
        }
        while (profile.SavedSetups.Any(setup => setup.SetupId.Equals(candidate, StringComparison.OrdinalIgnoreCase)));

        return candidate;
    }

    private static void ClearActiveSetupsForVehicle(JsonArray savedSetups, string vehicleId)
    {
        foreach (JsonNode? node in savedSetups)
        {
            if (node is JsonObject setup &&
                ReadString(setup, string.Empty, "vehicleId").Equals(vehicleId, StringComparison.OrdinalIgnoreCase))
            {
                setup["active"] = false;
            }
        }
    }

    private static void ThrowIfWarnings(ResolvedVehicleAssembly resolved)
    {
        VehicleAssemblyValidationMessage[] vehicleWarnings = [.. resolved.Validation
            .Where(message => message.Severity == VehicleAssemblyValidationSeverity.Warning)];
        EngineAssemblyValidationMessage[] engineWarnings = [.. resolved.Engine.Validation
            .Where(message => message.Severity == EngineAssemblyValidationSeverity.Warning)];

        if (vehicleWarnings.Length == 0 && engineWarnings.Length == 0)
        {
            return;
        }

        string vehicleMessages = string.Join("; ", vehicleWarnings.Select(message => message.Message));
        string engineMessages = string.Join("; ", engineWarnings.Select(message => message.Message));
        throw new InvalidOperationException($"Saved setup creation would produce validation warnings. Vehicle: {vehicleMessages}. Engine: {engineMessages}.");
    }

    private static void ThrowIfWarnings(GarageResolvedSetupVehicle validated)
    {
        ThrowIfWarnings(validated.Resolved);
    }

    private static GarageOwnedVehicleReference FindOwnedVehicle(GarageProfile profile, string ownedVehicleIdOrPath)
    {
        return profile.OwnedVehicles.FirstOrDefault(candidate =>
            candidate.VehicleId.Equals(ownedVehicleIdOrPath, StringComparison.OrdinalIgnoreCase) ||
            candidate.Path.Equals(ownedVehicleIdOrPath, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileNameWithoutExtension(candidate.Path).Equals(ownedVehicleIdOrPath, StringComparison.OrdinalIgnoreCase)) ??
            throw new InvalidOperationException($"Garage profile {profile.Id} does not own vehicle {ownedVehicleIdOrPath}.");
    }

    private static JsonArray EnsureArray(JsonObject root, string name)
    {
        if (root[name] is JsonArray existing)
        {
            return existing;
        }

        JsonArray created = [];
        root[name] = created;
        return created;
    }

    private static JsonObject RequireObject(JsonObject root, string name)
    {
        return root[name] as JsonObject ??
            throw new InvalidDataException($"JSON object is missing required object '{name}'.");
    }

    private static JsonObject ReadObject(string path)
    {
        JsonNode? node = JsonNode.Parse(File.ReadAllText(path));
        return node as JsonObject ?? throw new InvalidDataException($"JSON file is not an object: {path}");
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

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
    }

    private static string NormalizeDataPath(string path)
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

        throw new FileNotFoundException($"Data file was not found: {path}", path);
    }
}

internal sealed record GarageSavedSetupCreationResult(
    string ProfilePath,
    string SetupPath,
    string VehicleId,
    string SetupId,
    string DisplayName,
    bool Active,
    GarageSavedSetup Setup,
    GarageProfile UpdatedProfile,
    GarageRuntimeVehicleSelection Runtime);
