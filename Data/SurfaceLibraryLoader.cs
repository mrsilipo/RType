using System.Text.Json;
using RType.World;

namespace RType.Data;

public static class SurfaceLibraryLoader
{
    public static SurfaceLibrary Load(string path)
    {
        string resolvedPath = ResolveDataPath(path);
        using FileStream stream = File.OpenRead(resolvedPath);
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = true
        });

        JsonElement surfaces = Require(document.RootElement, "surfaces");
        return new SurfaceLibrary(
            ReadSurface(surfaces, "road"),
            ReadSurface(surfaces, "curb"),
            ReadSurface(surfaces, "grass"),
            ReadOptionalSurface(surfaces, "dirt", ReadSurface(surfaces, "grass")));
    }

    private static SurfaceSample ReadSurface(JsonElement surfaces, string id)
    {
        JsonElement surface = Require(surfaces, id);
        return new SurfaceSample(
            ReadRequiredString(surface, "name"),
            ReadRequiredValueSingle(surface, "grip"),
            ReadRequiredValueSingle(surface, "rollingResistanceMultiplier"),
            ReadRequiredValueSingle(surface, "longitudinalDragCoefficient"),
            ReadRequiredValueSingle(surface, "lateralDragCoefficient"),
            ReadRequiredValueSingle(surface, "wheelSpinDragCoefficient"),
            ReadValueSingle(surface, 0f, "muStatic"),
            ReadValueSingle(surface, 0f, "muDynamic"),
            ReadValueSingle(surface, 0f, "optimalSlipRatio"),
            ReadValueSingle(surface, 0f, "displacementDragCoefficient"),
            ReadValueSingle(surface, 0f, "vibrationPrimaryFrequency"),
            ReadValueSingle(surface, 0f, "vibrationPrimaryAmplitude"),
            ReadValueSingle(surface, 0f, "vibrationSecondaryFrequency"),
            ReadValueSingle(surface, 0f, "vibrationSecondaryAmplitude"),
            ReadValueSingle(surface, 1f, "handbrakeScreechFactor"),
            ReadValueSingle(surface, 18f, "handbrakeWheelSpinRecoveryRate"));
    }

    private static SurfaceSample ReadOptionalSurface(JsonElement surfaces, string id, SurfaceSample fallback)
    {
        return surfaces.ValueKind == JsonValueKind.Object && surfaces.TryGetProperty(id, out _)
            ? ReadSurface(surfaces, id)
            : fallback;
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

        throw new FileNotFoundException($"Surface definition JSON was not found: {path}", path);
    }

    private static string ReadRequiredString(JsonElement root, string property)
    {
        JsonElement element = Require(root, property);
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() ?? string.Empty;
        }

        throw new InvalidDataException($"Expected '{property}' to be a string.");
    }

    private static float ReadRequiredValueSingle(JsonElement root, string property)
    {
        JsonElement element = Require(root, property);
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("value", out JsonElement valueElement))
        {
            element = valueElement;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetSingle(out float value))
        {
            return value;
        }

        throw new InvalidDataException($"Expected '{property}' to be a value object or number.");
    }

    private static float ReadValueSingle(JsonElement root, float fallback, string property)
    {
        if (!root.TryGetProperty(property, out JsonElement element))
        {
            return fallback;
        }

        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("value", out JsonElement valueElement))
        {
            element = valueElement;
        }

        return element.ValueKind == JsonValueKind.Number && element.TryGetSingle(out float value)
            ? value
            : fallback;
    }

    private static JsonElement Require(JsonElement root, string property)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out JsonElement element))
        {
            return element;
        }

        throw new InvalidDataException($"Missing required surface JSON property '{property}'.");
    }
}
