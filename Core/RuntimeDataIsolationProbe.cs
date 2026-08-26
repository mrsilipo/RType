using RType.Data;

namespace RType.Core;

internal static class RuntimeDataIsolationProbe
{
    private static readonly string[] ActiveSourceRoots =
    [
        "Audio",
        "Core",
        "Data",
        "Vehicle"
    ];

    private static readonly string[] RetiredSourceRoots =
    [
        "Data/RTypeEngineProfiles",
        "Data/Setups",
        "Data/Tyres",
        "Data/Vehicles"
    ];

    public static void Run()
    {
        Console.WriteLine("Runtime data isolation probe");
        ValidateActiveSourceDoesNotCallLegacyVehicleLoader();
        ValidateRetiredSourceRoots();
        ValidateLegacyOutputNotPackaged();
        ValidateKnownLegacyAliasesResolveToPurchaseCar();
        Console.WriteLine("  result: PASS");
    }

    private static void ValidateActiveSourceDoesNotCallLegacyVehicleLoader()
    {
        foreach (string sourcePath in EnumerateActiveSourceFiles())
        {
            string normalized = NormalizePath(sourcePath);
            if (normalized.EndsWith("Data/VehicleDefinitionLoader.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (normalized.EndsWith("Core/RuntimeDataIsolationProbe.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string text = File.ReadAllText(sourcePath);
            Require(!text.Contains("VehicleDefinitionLoader.", StringComparison.Ordinal),
                $"active source calls legacy VehicleDefinitionLoader directly: {DisplayPath(sourcePath)}");
        }

        Console.WriteLine("  active source: no direct VehicleDefinitionLoader calls");
    }

    private static IEnumerable<string> EnumerateActiveSourceFiles()
    {
        foreach (string root in ActiveSourceRoots)
        {
            string resolved = Path.Combine(Environment.CurrentDirectory, root);
            if (!Directory.Exists(resolved))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(resolved, "*.cs", SearchOption.AllDirectories))
            {
                yield return file;
            }
        }

        string program = Path.Combine(Environment.CurrentDirectory, "Program.cs");
        if (File.Exists(program))
        {
            yield return program;
        }
    }

    private static void ValidateRetiredSourceRoots()
    {
        foreach (string root in RetiredSourceRoots)
        {
            foreach (string candidateRoot in CandidateRoots())
            {
                string resolved = Path.Combine(candidateRoot, root);
                if (!Directory.Exists(resolved))
                {
                    continue;
                }

                Require(!Directory.EnumerateFiles(resolved, "*", SearchOption.AllDirectories).Any(),
                    $"retired active data root contains live files: {resolved}");
            }
        }

        Console.WriteLine("  retired active roots: no live files");
    }

    private static void ValidateLegacyOutputNotPackaged()
    {
        string legacyOutput = Path.Combine(AppContext.BaseDirectory, "Data", "Legacy");
        if (Directory.Exists(legacyOutput))
        {
            Require(!Directory.EnumerateFiles(legacyOutput, "*", SearchOption.AllDirectories).Any(),
                $"runtime output contains packaged legacy data: {legacyOutput}");
        }

        Console.WriteLine("  runtime output: no packaged Data/Legacy files");
    }

    private static void ValidateKnownLegacyAliasesResolveToPurchaseCar()
    {
        Require(
            VehiclePathMigration.ResolveLegacyBuildPath(VehiclePathMigration.LegacyStockEk9VehicleBuildPath)
                .Equals(VehiclePathMigration.StockEk9PurchaseCarPath, StringComparison.OrdinalIgnoreCase),
            "legacy EK9 build path does not resolve to stock purchase car");

        Require(
            VehiclePathMigration.ResolveLegacyRuntimeVehiclePath(VehiclePathMigration.LegacyStockEk9VehicleDefinitionPath)
                .Equals(VehiclePathMigration.StockEk9PurchaseCarPath, StringComparison.OrdinalIgnoreCase),
            "legacy EK9 vehicle path does not resolve to stock purchase car");

        Console.WriteLine("  legacy aliases: stock EK9 paths resolve to purchase car");
    }

    private static IEnumerable<string> CandidateRoots()
    {
        yield return Environment.CurrentDirectory;

        if (!Environment.CurrentDirectory.Equals(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
        {
            yield return AppContext.BaseDirectory;
        }
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static string DisplayPath(string path)
    {
        return Path.GetRelativePath(Environment.CurrentDirectory, path).Replace('\\', '/');
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Runtime data isolation probe failed: {message}.");
        }
    }
}
