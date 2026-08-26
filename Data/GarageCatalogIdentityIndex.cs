using System.Text.Json;

namespace RType.Data;

internal static class GarageCatalogIdentityIndex
{
    private const string VehicleCatalogIndexPath = "Data/Parts/part_catalog_index.json";
    private const string EngineCatalogIndexPath = "Data/Parts/Engine/part_catalog_index.json";
    private const string EngineTunesPath = "Data/Tunes/Engine/engine_tunes.json";
    private const string FuelsPath = "Data/Tunes/Engine/fuels.json";

    public static GarageCatalogIdentityReport Load()
    {
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        List<GarageCatalogIdentityWarning> warnings = [];

        AddCatalogIndexIds(ids, VehicleCatalogIndexPath, warnings);
        AddCatalogIndexIds(ids, EngineCatalogIndexPath, warnings);
        AddIdsFromJson(ids, EngineTunesPath, warnings);
        AddIdsFromJson(ids, FuelsPath, warnings);

        return new GarageCatalogIdentityReport(ids, warnings);
    }

    private static void AddCatalogIndexIds(HashSet<string> ids, string indexPath, List<GarageCatalogIdentityWarning> warnings)
    {
        try
        {
            using FileStream stream = File.OpenRead(ResolveDataPath(indexPath));
            using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
            if (!TryGet(document.RootElement, out JsonElement catalogs, "catalogs") ||
                catalogs.ValueKind != JsonValueKind.Array)
            {
                warnings.Add(new GarageCatalogIdentityWarning("catalog_index_missing_catalogs", $"Catalog index {indexPath} does not contain a catalogs array."));
                return;
            }

            foreach (JsonElement catalog in catalogs.EnumerateArray())
            {
                string path = ReadString(catalog, string.Empty, "path");
                if (!string.IsNullOrWhiteSpace(path))
                {
                    AddIdsFromJson(ids, path, warnings);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException)
        {
            warnings.Add(new GarageCatalogIdentityWarning("catalog_index_load_failed", $"Catalog index {indexPath} could not load: {exception.Message}"));
        }
    }

    private static void AddIdsFromJson(HashSet<string> ids, string path, List<GarageCatalogIdentityWarning> warnings)
    {
        try
        {
            using FileStream stream = File.OpenRead(ResolveDataPath(path));
            using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
            AddIdsRecursive(ids, document.RootElement);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException)
        {
            warnings.Add(new GarageCatalogIdentityWarning("catalog_load_failed", $"Catalog {path} could not load: {exception.Message}"));
        }
    }

    private static void AddIdsRecursive(HashSet<string> ids, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("id", out JsonElement id) &&
                id.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(id.GetString()))
            {
                ids.Add(id.GetString()!);
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                AddIdsRecursive(ids, property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                AddIdsRecursive(ids, item);
            }
        }
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

        throw new FileNotFoundException($"Catalog identity file was not found: {path}", path);
    }
}

internal sealed record GarageCatalogIdentityReport(
    IReadOnlySet<string> Ids,
    IReadOnlyList<GarageCatalogIdentityWarning> Warnings)
{
    public bool IsClean => Warnings.Count == 0;

    public bool Contains(string id)
    {
        return !string.IsNullOrWhiteSpace(id) && Ids.Contains(id);
    }
}

internal sealed record GarageCatalogIdentityWarning(string Code, string Message);
