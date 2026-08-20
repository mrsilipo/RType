using Microsoft.Xna.Framework.Audio;

namespace RetroRacer.Audio;

public sealed class MenuSoundSystem : IDisposable
{
    private const float DefaultVolume = 0.8f;

    private readonly SoundEffect? _click;
    private readonly SoundEffect? _cancel;
    private readonly SoundEffect? _confirm;
    private readonly SoundEffect? _decision;
    private readonly SoundEffect? _notAllowed;
    private readonly SoundEffect? _purchase;

    public MenuSoundSystem()
    {
        _click = LoadOptional("Assets/Sounds/Menu/menu_click.wav");
        _cancel = LoadOptional("Assets/Sounds/Menu/menu_cancel.wav");
        _confirm = LoadOptional("Assets/Sounds/Menu/menu_confirm.wav");
        _decision = LoadOptional("Assets/Sounds/Menu/menu_decision.wav");
        _notAllowed = LoadOptional("Assets/Sounds/Menu/menu_notallowed.wav");
        _purchase = LoadOptional("Assets/Sounds/Menu/menu_purchase.wav");
    }

    public void PlayClick()
    {
        Play(_click);
    }

    public void PlayCancel()
    {
        Play(_cancel);
    }

    public void PlayConfirm()
    {
        Play(_confirm);
    }

    public void PlayDecision()
    {
        Play(_decision);
    }

    public void PlayNotAllowed()
    {
        Play(_notAllowed);
    }

    public void PlayPurchase()
    {
        Play(_purchase);
    }

    public void Dispose()
    {
        _purchase?.Dispose();
        _notAllowed?.Dispose();
        _decision?.Dispose();
        _confirm?.Dispose();
        _cancel?.Dispose();
        _click?.Dispose();
    }

    private static void Play(SoundEffect? sound)
    {
        sound?.Play(DefaultVolume, 0f, 0f);
    }

    private static SoundEffect? LoadOptional(string relativePath)
    {
        foreach (string path in GetCandidateAssetPaths(relativePath))
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using FileStream stream = File.OpenRead(path);
                return SoundEffect.FromStream(stream);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or NotSupportedException)
            {
                Console.Error.WriteLine($"Could not load menu sound '{path}': {exception.Message}");
            }
        }

        Console.Error.WriteLine($"Menu sound asset was not found: {relativePath}");
        return null;
    }

    private static IEnumerable<string> GetCandidateAssetPaths(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            yield return relativePath;
            yield break;
        }

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        string currentDirectoryPath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, relativePath));
        string outputDirectoryPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relativePath));

        if (seen.Add(currentDirectoryPath))
        {
            yield return currentDirectoryPath;
        }

        if (seen.Add(outputDirectoryPath))
        {
            yield return outputDirectoryPath;
        }
    }
}
