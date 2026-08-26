using System.Text.Json;
using System.Text.Json.Nodes;

namespace RType.Data;

internal static class EngineModPathResolver
{
    private const string EngineCatalogIndexPath = "Data/Parts/Engine/part_catalog_index.json";
    private const string EngineTuneIndexPath = "Data/Tunes/Engine/engine_tunes.json";
    private const string EngineFuelCatalogPath = "Data/Tunes/Engine/fuels.json";

    public static EngineModPathReport BuildReport(string vehicleBuildPath)
    {
        JsonObject engine = ReadEngineNode(vehicleBuildPath);
        ResolvedEngineAssembly current = ResolveEngine(engine);
        EngineCatalogBrowser catalogs = EngineCatalogBrowser.Load(EngineCatalogIndexPath, EngineTuneIndexPath, EngineFuelCatalogPath);

        List<EngineModOption> options = [];
        foreach (CatalogItem item in catalogs.Items)
        {
            if (GarageModSlotMap.EngineCatalogSlotToInstalledSlot.TryGetValue(item.CatalogSlot, out string? installedSlot))
            {
                if (!IsCompatible(item.Element, current.Family))
                {
                    continue;
                }

                options.Add(EvaluateOption(
                    item.Id,
                    item.DisplayName,
                    installedSlot,
                    item.CatalogSlot,
                    item.Tier,
                    item.Category,
                    engine,
                    candidate => SetInstalledPart(candidate, installedSlot, item.Id)));
            }
            else if (item.CatalogSlot.Equals("engineTune", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsCompatible(item.Element, current.Family))
                {
                    continue;
                }

                options.Add(EvaluateOption(
                    item.Id,
                    item.DisplayName,
                    "tuneId",
                    item.CatalogSlot,
                    item.Tier,
                    item.Category,
                    engine,
                    candidate => candidate["tuneId"] = item.Id));
            }
            else if (item.CatalogSlot.Equals("fuel", StringComparison.OrdinalIgnoreCase))
            {
                options.Add(EvaluateOption(
                    item.Id,
                    item.DisplayName,
                    "fuel",
                    item.CatalogSlot,
                    item.Tier,
                    item.Category,
                    engine,
                    candidate => SetSelectedFuel(candidate, item.Id)));
            }
            else if (item.CatalogSlot.Equals("engineCombination", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsCompatible(item.Element, current.Family))
                {
                    continue;
                }

                options.Add(EvaluateOption(
                    item.Id,
                    item.DisplayName,
                    "engineCombination",
                    item.CatalogSlot,
                    item.Tier,
                    item.Category,
                    engine,
                    candidate =>
                    {
                        candidate["engineId"] = ReadString(item.Element, ReadString(candidate, string.Empty, "engineId"), "baseEngineId");
                        candidate["blockId"] = ReadString(item.Element, string.Empty, "blockId");
                        candidate["headId"] = ReadString(item.Element, string.Empty, "headId");
                        candidate["combinationId"] = item.Id;
                    }));
            }
        }

        return new EngineModPathReport(current, [.. options.OrderBy(option => option.Slot).ThenBy(option => option.Id, StringComparer.OrdinalIgnoreCase)]);
    }

    private static EngineModOption EvaluateOption(
        string id,
        string displayName,
        string slot,
        string catalogSlot,
        string tier,
        string category,
        JsonObject sourceEngine,
        Action<JsonObject> apply)
    {
        string currentId = ReadCurrentSelection(sourceEngine, slot);
        JsonObject candidate = CloneObject(sourceEngine);
        apply(candidate);
        ResolvedEngineAssembly resolved = ResolveEngine(candidate);
        string[] warningCodes = [.. resolved.Validation
            .Where(message => message.Severity == EngineAssemblyValidationSeverity.Warning)
            .Select(message => message.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)];
        string[] infoCodes = [.. resolved.Validation
            .Where(message => message.Severity == EngineAssemblyValidationSeverity.Info)
            .Select(message => message.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)];

        return new EngineModOption(
            id,
            displayName,
            slot,
            catalogSlot,
            tier,
            category,
            currentId.Equals(id, StringComparison.OrdinalIgnoreCase),
            warningCodes.Length == 0,
            warningCodes,
            infoCodes,
            resolved.DisplacementCc,
            resolved.CompressionRatio,
            resolved.LimiterHardCutRpm,
            resolved.TorqueCurve.Length == 0 ? 0f : resolved.TorqueCurve.Max(point => point.TorqueNm));
    }

    private static string ReadCurrentSelection(JsonObject engine, string slot)
    {
        if (slot.Equals("tuneId", StringComparison.OrdinalIgnoreCase))
        {
            return ReadString(engine, string.Empty, "tuneId");
        }

        if (slot.Equals("fuel", StringComparison.OrdinalIgnoreCase))
        {
            return ReadString(engine, string.Empty, "fuel", "selected");
        }

        if (slot.Equals("engineCombination", StringComparison.OrdinalIgnoreCase))
        {
            return ReadString(engine, string.Empty, "combinationId");
        }

        return ReadString(engine, string.Empty, "installedParts", slot);
    }

    private static JsonObject ReadEngineNode(string vehicleBuildPath)
    {
        string resolvedPath = ResolveDataPath(vehicleBuildPath);
        JsonNode? root = JsonNode.Parse(File.ReadAllText(resolvedPath));
        if (root is not JsonObject rootObject ||
            rootObject["assembly"] is not JsonObject assembly ||
            assembly["engine"] is not JsonObject engine)
        {
            throw new InvalidDataException($"Vehicle build does not contain assembly.engine: {vehicleBuildPath}");
        }

        return CloneObject(engine);
    }

    private static ResolvedEngineAssembly ResolveEngine(JsonObject engine)
    {
        using JsonDocument document = JsonDocument.Parse(engine.ToJsonString(), new JsonDocumentOptions { AllowTrailingCommas = true });
        return EngineAssemblyResolver.Resolve(document.RootElement);
    }

    private static void SetInstalledPart(JsonObject engine, string slot, string partId)
    {
        if (engine["installedParts"] is not JsonObject installedParts)
        {
            installedParts = [];
            engine["installedParts"] = installedParts;
        }

        installedParts[slot] = partId;
    }

    private static void SetSelectedFuel(JsonObject engine, string fuelId)
    {
        if (engine["fuel"] is not JsonObject fuel)
        {
            fuel = [];
            engine["fuel"] = fuel;
        }

        fuel["selected"] = fuelId;
    }

    private static bool IsCompatible(JsonElement item, string family)
    {
        if (!TryGet(item, out JsonElement compatibility, "compatibility"))
        {
            string itemFamily = ReadString(item, string.Empty, "family");
            return string.IsNullOrWhiteSpace(itemFamily) ||
                itemFamily.Equals(family, StringComparison.OrdinalIgnoreCase);
        }

        if (compatibility.ValueKind == JsonValueKind.String)
        {
            return compatibility.GetString()?.Equals(family, StringComparison.OrdinalIgnoreCase) == true;
        }

        return compatibility.ValueKind != JsonValueKind.Array ||
            compatibility.EnumerateArray().Any(value =>
                value.ValueKind == JsonValueKind.String &&
                value.GetString()?.Equals(family, StringComparison.OrdinalIgnoreCase) == true);
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

        return node.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : fallback;
    }

    private static string ReadString(JsonElement root, string fallback, params string[] path)
    {
        return TryGet(root, out JsonElement value, path) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
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

    private sealed class EngineCatalogBrowser
    {
        private readonly List<CatalogItem> _items = [];

        public IEnumerable<CatalogItem> Items => _items;

        public static EngineCatalogBrowser Load(params string[] paths)
        {
            EngineCatalogBrowser browser = new();
            foreach (string path in paths)
            {
                browser.LoadPath(path);
            }

            return browser;
        }

        private void LoadPath(string path)
        {
            string resolvedPath = ResolveDataPath(path);
            using FileStream stream = File.OpenRead(resolvedPath);
            using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("catalogs", out JsonElement catalogs))
            {
                foreach (JsonElement catalog in catalogs.EnumerateArray())
                {
                    LoadCatalog(
                        ReadString(catalog, string.Empty, "path"),
                        ReadString(catalog, string.Empty, "slot"));
                }

                return;
            }

            LoadCatalog(path, ReadString(root, string.Empty, "slot"));
        }

        private void LoadCatalog(string path, string fallbackSlot)
        {
            string resolvedPath = ResolveDataPath(path);
            using FileStream stream = File.OpenRead(resolvedPath);
            using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
            JsonElement root = document.RootElement;
            string catalogSlot = ReadString(root, fallbackSlot, "slot");
            JsonElement array = default;
            string inferredSlot = catalogSlot;
            if (root.TryGetProperty("parts", out JsonElement parts))
            {
                array = parts;
            }
            else if (root.TryGetProperty("engines", out JsonElement engines))
            {
                array = engines;
                inferredSlot = string.IsNullOrWhiteSpace(inferredSlot) ? "engine" : inferredSlot;
            }
            else if (root.TryGetProperty("blocks", out JsonElement blocks))
            {
                array = blocks;
                inferredSlot = string.IsNullOrWhiteSpace(inferredSlot) ? "engineBlock" : inferredSlot;
            }
            else if (root.TryGetProperty("heads", out JsonElement heads))
            {
                array = heads;
                inferredSlot = string.IsNullOrWhiteSpace(inferredSlot) ? "engineHead" : inferredSlot;
            }
            else if (root.TryGetProperty("combinations", out JsonElement combinations))
            {
                array = combinations;
                inferredSlot = string.IsNullOrWhiteSpace(inferredSlot) ? "engineCombination" : inferredSlot;
            }
            else if (root.TryGetProperty("tunes", out JsonElement tunes))
            {
                array = tunes;
                inferredSlot = string.IsNullOrWhiteSpace(inferredSlot) ? "engineTune" : inferredSlot;
            }
            else if (root.TryGetProperty("fuels", out JsonElement fuels))
            {
                array = fuels;
                inferredSlot = string.IsNullOrWhiteSpace(inferredSlot) ? "fuel" : inferredSlot;
            }

            if (array.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (JsonElement item in array.EnumerateArray())
            {
                string id = ReadString(item, string.Empty, "id");
                if (!string.IsNullOrWhiteSpace(id))
                {
                    _items.Add(new CatalogItem(
                        id,
                        ReadString(item, id, "displayName"),
                        inferredSlot,
                        ReadString(item, string.Empty, "tier"),
                        ReadString(item, string.Empty, "category"),
                        item.Clone()));
                }
            }
        }
    }

    private sealed record CatalogItem(
        string Id,
        string DisplayName,
        string CatalogSlot,
        string Tier,
        string Category,
        JsonElement Element);
}

internal sealed record EngineModPathReport(
    ResolvedEngineAssembly CurrentEngine,
    IReadOnlyList<EngineModOption> Options)
{
    public IEnumerable<EngineModOption> Selectable => Options.Where(option => option.Selectable);
    public IEnumerable<EngineModOption> Rejected => Options.Where(option => !option.Selectable);
    public IEnumerable<EngineModOption> Ready => Options.Where(option => option.Status == EngineModOptionStatus.Ready);
    public IEnumerable<EngineModOption> Advisory => Options.Where(option => option.Status == EngineModOptionStatus.Advisory);
    public IEnumerable<EngineModOption> Blocked => Options.Where(option => option.Status == EngineModOptionStatus.Blocked);
    public IEnumerable<EngineModOption> Installed => Options.Where(option => option.IsInstalled);

    public IReadOnlyList<EngineModPathSlotGroup> Groups =>
    [
        .. Options
            .GroupBy(option => option.Slot, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new EngineModPathSlotGroup(
                group.Key,
                [.. group.Where(option => option.IsInstalled).OrderBy(option => option.Id, StringComparer.OrdinalIgnoreCase)],
                [.. group.Where(option => option.Status == EngineModOptionStatus.Ready).OrderBy(option => option.Id, StringComparer.OrdinalIgnoreCase)],
                [.. group.Where(option => option.Status == EngineModOptionStatus.Advisory).OrderBy(option => option.Id, StringComparer.OrdinalIgnoreCase)],
                [.. group.Where(option => option.Status == EngineModOptionStatus.Blocked).OrderBy(option => option.Id, StringComparer.OrdinalIgnoreCase)]))
    ];
}

internal sealed record EngineModOption(
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
    float DisplacementCc,
    float CompressionRatio,
    float LimiterHardCutRpm,
    float PeakTorqueNm)
{
    public EngineModOptionStatus Status => !Selectable
        ? EngineModOptionStatus.Blocked
        : InfoCodes.Count == 0
            ? EngineModOptionStatus.Ready
            : EngineModOptionStatus.Advisory;
}

internal sealed record EngineModPathSlotGroup(
    string Slot,
    IReadOnlyList<EngineModOption> Installed,
    IReadOnlyList<EngineModOption> Ready,
    IReadOnlyList<EngineModOption> Advisory,
    IReadOnlyList<EngineModOption> Blocked);

internal enum EngineModOptionStatus
{
    Ready,
    Advisory,
    Blocked
}
