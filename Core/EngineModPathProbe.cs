using RType.Data;

namespace RType.Core;

internal static class EngineModPathProbe
{
    public static void Run()
    {
        EngineModPathReport stockReport = EngineModPathResolver.BuildReport("Data/PurchaseCars/2000_Ek9_Stock.json");
        PrintReport("stock_ek9", stockReport);

        RequireSelectable(stockReport, "cam_set_street");
        RequireSelectable(stockReport, "cam_set_club_sport");
        RequireInfo(stockReport, "cam_set_club_sport", "part_tune_tier_recommended");
        RequireStatus(stockReport, "cam_set_club_sport", EngineModOptionStatus.Advisory);
        RequireRejected(stockReport, "displacement_pro_high_comp", "fuel_octane_insufficient");
        RequireStatus(stockReport, "displacement_pro_high_comp", EngineModOptionStatus.Blocked);
        RequireSelectable(stockReport, "fuel_e85");
        RequireInfo(stockReport, "fuel_e85", "fuel_retune_recommended");
        RequireStatus(stockReport, "fuel_e85", EngineModOptionStatus.Advisory);
        RequirePresent(stockReport, "combo_b18b_block_b16b_head_lsvtec");
        RequireSelectable(stockReport, "combo_b18b_block_b16b_head_lsvtec");
        RequireInfo(stockReport, "combo_b18b_block_b16b_head_lsvtec", "supported_engine_combination");
        RequireStatus(stockReport, "combo_b18b_block_b16b_head_lsvtec", EngineModOptionStatus.Advisory);
        RequireInstalled(stockReport, "fuel_98ron");
        RequireGroup(stockReport, "fuel", readyMinimum: 1, advisoryMinimum: 1, blockedMinimum: 0);
        RequireGroup(stockReport, "displacement", readyMinimum: 2, advisoryMinimum: 1, blockedMinimum: 1);

        EngineModPathReport modifiedReport = EngineModPathResolver.BuildReport("Data/Garage/OwnedVehicles/vehicle_0002_modified_ek9.json");
        PrintReport("modified_ek9", modifiedReport);

        RequireSelectable(modifiedReport, "cam_set_club_sport");
        RequireSelectable(modifiedReport, "displacement_pro_high_comp");
        RequireStatus(modifiedReport, "displacement_pro_high_comp", EngineModOptionStatus.Ready);
        RequireSelectable(modifiedReport, "fuel_e85");
        RequireInstalled(modifiedReport, "fuel_e85");
        RequireRejected(modifiedReport, "fuel_98ron", "fuel_octane_insufficient");
        RequireGroup(modifiedReport, "fuel", readyMinimum: 1, advisoryMinimum: 0, blockedMinimum: 1);

        Console.WriteLine("Engine mod path probe: PASS");
    }

    private static void PrintReport(string label, EngineModPathReport report)
    {
        Console.WriteLine($"{label}: {report.CurrentEngine.EngineCode}, {report.CurrentEngine.Family}, {report.CurrentEngine.DisplacementCc:0}cc, tune {report.CurrentEngine.TuneId}, fuel {report.CurrentEngine.FuelId}");
        Console.WriteLine($"  options: {report.Options.Count}, ready {report.Ready.Count()}, advisory {report.Advisory.Count()}, blocked {report.Blocked.Count()}, installed {report.Installed.Count()}");

        foreach (EngineModOption option in report.Options
            .Where(option => option.Id.Contains("club", StringComparison.OrdinalIgnoreCase) ||
                option.Id.Contains("high_comp", StringComparison.OrdinalIgnoreCase) ||
                option.CatalogSlot.Equals("fuel", StringComparison.OrdinalIgnoreCase) ||
                option.CatalogSlot.Equals("engineCombination", StringComparison.OrdinalIgnoreCase))
            .OrderBy(option => option.CatalogSlot, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.Id, StringComparer.OrdinalIgnoreCase))
        {
            string installed = option.IsInstalled ? ", installed" : string.Empty;
            Console.WriteLine($"  {option.Status}{installed}: {option.CatalogSlot}/{option.Id} -> tier {Blank(option.Tier)}, {option.DisplacementCc:0}cc, CR {option.CompressionRatio:0.0}, limiter {option.LimiterHardCutRpm:0}rpm, warnings [{string.Join(", ", option.WarningCodes)}], info [{string.Join(", ", option.InfoCodes)}]");
        }
    }

    private static string Blank(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(none)" : value;
    }

    private static void RequirePresent(EngineModPathReport report, string id)
    {
        if (!report.Options.Any(option => option.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Engine mod path probe failed: option {id} was not present.");
        }
    }

    private static void RequireSelectable(EngineModPathReport report, string id)
    {
        EngineModOption option = Find(report, id);
        if (!option.Selectable)
        {
            throw new InvalidOperationException($"Engine mod path probe failed: option {id} should be selectable but had warnings [{string.Join(", ", option.WarningCodes)}].");
        }
    }

    private static void RequireRejected(EngineModPathReport report, string id, string expectedWarningCode)
    {
        EngineModOption option = Find(report, id);
        if (option.Selectable)
        {
            throw new InvalidOperationException($"Engine mod path probe failed: option {id} should be rejected.");
        }

        if (!option.WarningCodes.Any(code => code.Equals(expectedWarningCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Engine mod path probe failed: option {id} did not include expected warning {expectedWarningCode}. Actual warnings [{string.Join(", ", option.WarningCodes)}].");
        }
    }

    private static void RequireInfo(EngineModPathReport report, string id, string expectedInfoCode)
    {
        EngineModOption option = Find(report, id);
        if (!option.InfoCodes.Any(code => code.Equals(expectedInfoCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Engine mod path probe failed: option {id} did not include expected info {expectedInfoCode}. Actual info [{string.Join(", ", option.InfoCodes)}].");
        }
    }

    private static void RequireStatus(EngineModPathReport report, string id, EngineModOptionStatus expectedStatus)
    {
        EngineModOption option = Find(report, id);
        if (option.Status != expectedStatus)
        {
            throw new InvalidOperationException($"Engine mod path probe failed: option {id} status was {option.Status}, expected {expectedStatus}.");
        }
    }

    private static void RequireInstalled(EngineModPathReport report, string id)
    {
        EngineModOption option = Find(report, id);
        if (!option.IsInstalled)
        {
            throw new InvalidOperationException($"Engine mod path probe failed: option {id} should be marked installed.");
        }
    }

    private static void RequireGroup(EngineModPathReport report, string slot, int readyMinimum, int advisoryMinimum, int blockedMinimum)
    {
        EngineModPathSlotGroup group = report.Groups.FirstOrDefault(group => group.Slot.Equals(slot, StringComparison.OrdinalIgnoreCase)) ??
            throw new InvalidOperationException($"Engine mod path probe failed: slot group {slot} was not found.");

        if (group.Ready.Count < readyMinimum ||
            group.Advisory.Count < advisoryMinimum ||
            group.Blocked.Count < blockedMinimum)
        {
            throw new InvalidOperationException(
                $"Engine mod path probe failed: group {slot} counts ready/advisory/blocked were {group.Ready.Count}/{group.Advisory.Count}/{group.Blocked.Count}, expected minimum {readyMinimum}/{advisoryMinimum}/{blockedMinimum}.");
        }
    }

    private static EngineModOption Find(EngineModPathReport report, string id)
    {
        return report.Options.FirstOrDefault(option => option.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ??
            throw new InvalidOperationException($"Engine mod path probe failed: option {id} was not found.");
    }
}
