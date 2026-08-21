using System.Text.Json;

namespace RetroRacer.Core;

public static class EngineSimProfileAudit
{
    private const string ProfileDirectory = "Data/EngineProfiles";

    public static void Run()
    {
        string directory = ResolveDirectory(ProfileDirectory);
        string[] files = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
        Console.WriteLine("Engine Sim profile audit");
        Console.WriteLine($"  directory: {directory}");

        if (files.Length == 0)
        {
            Console.WriteLine("  no profiles found");
            return;
        }

        int failures = 0;
        foreach (string file in files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
                JsonElement root = document.RootElement;
                string id = ReadString(root, "id");
                string displayName = ReadString(root, "displayName");
                if (!root.TryGetProperty("engineSimulator", out JsonElement engine))
                {
                    throw new InvalidDataException("missing engineSimulator object");
                }

                string mrPath = ReadString(engine, "mrScriptPath");
                string impulseResponsePath = ReadString(engine, "impulseResponsePath");
                string? resolvedMrPath = ResolveOptionalPath(mrPath);
                string? resolvedImpulsePath = ResolveOptionalPath(impulseResponsePath);
                List<string> errors = [];
                if (string.IsNullOrWhiteSpace(id))
                {
                    errors.Add("missing id");
                }

                if (resolvedMrPath is null)
                {
                    errors.Add($"missing MR asset '{mrPath}'");
                }

                if (resolvedImpulsePath is null)
                {
                    errors.Add($"missing impulse response '{impulseResponsePath}'");
                }

                string status = errors.Count == 0 ? "OK" : "FAIL";
                Console.WriteLine($"  [{status}] {Path.GetFileName(file)}: {displayName} ({id})");
                Console.WriteLine($"       MR {mrPath}");
                Console.WriteLine($"       IR {impulseResponsePath}");
                if (errors.Count > 0)
                {
                    failures++;
                    foreach (string error in errors)
                    {
                        Console.WriteLine($"       error: {error}");
                    }
                }
            }
            catch (Exception exception)
            {
                failures++;
                Console.WriteLine($"  [FAIL] {Path.GetFileName(file)}: {exception.Message}");
            }
        }

        Console.WriteLine($"  result: {files.Length - failures}/{files.Length} profiles valid");
        if (failures > 0)
        {
            throw new InvalidDataException($"{failures} Engine Sim profile(s) failed validation.");
        }
    }

    private static string ReadString(JsonElement parent, string propertyName)
    {
        return parent.TryGetProperty(propertyName, out JsonElement value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string ResolveDirectory(string path)
    {
        string[] candidates =
        [
            Path.Combine(Environment.CurrentDirectory, path),
            Path.Combine(AppContext.BaseDirectory, path)
        ];

        return candidates.FirstOrDefault(Directory.Exists)
            ?? throw new DirectoryNotFoundException($"Engine Sim profile directory was not found: {path}");
    }

    private static string? ResolveOptionalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (Path.IsPathRooted(path) && File.Exists(path))
        {
            return path;
        }

        string[] candidates =
        [
            Path.Combine(Environment.CurrentDirectory, path),
            Path.Combine(AppContext.BaseDirectory, path)
        ];

        return candidates.FirstOrDefault(File.Exists);
    }
}
