using System.Text.Json;

namespace RetroRacer.Input;

public static class ControlSchemeLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static ControlScheme Load(string path)
    {
        string resolvedPath = ResolveDataPath(path);
        using FileStream stream = File.OpenRead(resolvedPath);
        ControlScheme? scheme = JsonSerializer.Deserialize<ControlScheme>(stream, JsonOptions);

        if (scheme is null)
        {
            throw new InvalidOperationException($"Control scheme JSON could not be read: {path}");
        }

        if (scheme.SchemaVersion != 1)
        {
            throw new InvalidOperationException($"Unsupported control scheme version {scheme.SchemaVersion} in {path}.");
        }

        return scheme;
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

        throw new FileNotFoundException($"Control scheme JSON was not found: {path}", path);
    }
}
