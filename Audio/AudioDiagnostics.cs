namespace RType.Audio;

internal static class AudioDiagnostics
{
    private static readonly object Sync = new();
    private static bool _announced;
    private static readonly bool ConsoleEnabled =
        string.Equals(Environment.GetEnvironmentVariable("RTYPE_AUDIO_DIAGNOSTICS_CONSOLE"), "1", StringComparison.OrdinalIgnoreCase);
    private static readonly bool VerboseEnabled =
        string.Equals(Environment.GetEnvironmentVariable("RTYPE_AUDIO_DIAGNOSTICS_VERBOSE"), "1", StringComparison.OrdinalIgnoreCase);

    public static string LogFilePath { get; } = Path.Combine(AppContext.BaseDirectory, "Logs", "audio-diagnostics.log");

    public static bool Verbose => VerboseEnabled;

    public static void Log(string category, string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath) ?? AppContext.BaseDirectory);
            string line = $"{DateTimeOffset.Now:O} [{category}] {message}{Environment.NewLine}";
            lock (Sync)
            {
                if (!_announced)
                {
                    File.WriteAllText(LogFilePath, $"{DateTimeOffset.Now:O} [audio] diagnostics started{Environment.NewLine}");
                    _announced = true;
                }

                File.AppendAllText(LogFilePath, line);
            }

            if (ConsoleEnabled)
            {
                Console.WriteLine($"Audio diagnostic [{category}]: {message}");
            }
        }
        catch
        {
            // Diagnostics must never take down the game/audio path.
        }
    }

    public static double NowSeconds => Environment.TickCount64 / 1000.0;
}
