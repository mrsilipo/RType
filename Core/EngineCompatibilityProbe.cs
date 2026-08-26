using System.Text.Json;
using RType.Data;

namespace RType.Core;

internal static class EngineCompatibilityProbe
{
    public static void Run()
    {
        Probe("invalid_d_block_b_head", """
        {
          "engineId": "engine_b16b",
          "blockId": "block_d16y8",
          "headId": "head_b16b_type_r",
          "tuneId": "tune_b16b_factory",
          "fuel": { "selected": "fuel_98ron" },
          "installedParts": {}
        }
        """, ["engine_block_family_mismatch", "block_head_family_rule_mismatch"]);

        Probe("invalid_vtec_cam_on_non_vtec_head", """
        {
          "engineId": "engine_b18a",
          "blockId": "block_b18a",
          "headId": "head_b18a_non_vtec",
          "tuneId": "tune_b18a_factory",
          "fuel": { "selected": "fuel_98ron" },
          "installedParts": {
            "cams": "cam_set_stock"
          }
        }
        """, ["part_requires_vtec_head", "part_enables_vtec_on_non_vtec_head"]);

        Probe("invalid_flywheel_in_cam_slot", """
        {
          "engineId": "engine_b16b",
          "blockId": "block_b16b",
          "headId": "head_b16b_type_r",
          "tuneId": "tune_b16b_factory",
          "fuel": { "selected": "fuel_98ron" },
          "installedParts": {
            "cams": "flywheel_stock"
          }
        }
        """, ["engine_part_slot_mismatch"]);

        Probe("high_compression_on_98ron_factory_tune", """
        {
          "engineId": "engine_b16b",
          "blockId": "block_b16b",
          "headId": "head_b16b_type_r",
          "tuneId": "tune_b16b_factory",
          "fuel": { "selected": "fuel_98ron" },
          "installedParts": {
            "displacement": "displacement_pro_high_comp"
          }
        }
        """, ["part_requires_higher_octane", "fuel_octane_insufficient", "part_tune_tier_recommended", "part_fuel_recommended"]);

        Probe("high_compression_e85_club_tune_supported", """
        {
          "engineId": "engine_b16b",
          "blockId": "block_b16b",
          "headId": "head_b16b_type_r",
          "tuneId": "tune_b16b_club_sport_e85",
          "fuel": { "selected": "fuel_e85" },
          "installedParts": {
            "displacement": "displacement_pro_high_comp",
            "cams": "cam_set_club_sport",
            "valveSprings": "valve_springs_club_sport"
          }
        }
        """, []);

        Probe("supported_k24_k20_frank", """
        {
          "engineId": "engine_k24a3",
          "blockId": "block_k24a3",
          "headId": "head_k20a_vtec",
          "tuneId": "tune_k24a3_factory",
          "fuel": { "selected": "fuel_98ron" },
          "installedParts": {}
        }
        """, ["supported_engine_combination"]);

        Probe("supported_b18b_b16b_lsvtec_frank", """
        {
          "engineId": "engine_b18b",
          "blockId": "block_b18b",
          "headId": "head_b16b_type_r",
          "tuneId": "tune_b16b_club_sport_e85",
          "fuel": { "selected": "fuel_e85" },
          "installedParts": {
            "valveSprings": "valve_springs_club_sport"
          }
        }
        """, ["supported_engine_combination", "tune_not_factory_intended"]);

        Probe("unapproved_b18a_b16b_frank", """
        {
          "engineId": "engine_b18a",
          "blockId": "block_b18a",
          "headId": "head_b16b_type_r",
          "tuneId": "tune_b18a_factory",
          "fuel": { "selected": "fuel_98ron" },
          "installedParts": {}
        }
        """, ["unapproved_engine_combination"]);
    }

    private static void Probe(string label, string engineJson, IReadOnlyList<string> expectedCodes)
    {
        using JsonDocument document = JsonDocument.Parse(engineJson, new JsonDocumentOptions { AllowTrailingCommas = true });
        ResolvedEngineAssembly assembly = EngineAssemblyResolver.Resolve(document.RootElement);
        Console.WriteLine($"{label}: {assembly.EngineCode} block {assembly.BlockId}, head {assembly.HeadId}");
        if (!string.IsNullOrWhiteSpace(assembly.EngineCombinationId))
        {
            Console.WriteLine($"  combination: {assembly.EngineCombinationId} ({assembly.EngineCombinationDisplayName})");
        }
        Console.WriteLine($"  family: engine {assembly.Family}, block {assembly.BlockFamily}, head {assembly.HeadFamily}");
        Console.WriteLine($"  vtec: {(assembly.VtecEnabled ? "yes" : "no")}, valvetrain {assembly.Valvetrain}");
        Console.WriteLine($"  validation count: {assembly.Validation.Count}");
        foreach (EngineAssemblyValidationMessage message in assembly.Validation)
        {
            Console.WriteLine($"  {message.Severity} {message.Code}: {message.Message}");
        }

        foreach (string expectedCode in expectedCodes)
        {
            if (!assembly.Validation.Any(message => message.Code.Equals(expectedCode, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Engine compatibility probe failed: {label} did not produce expected validation code {expectedCode}.");
            }
        }

        if (expectedCodes.Count == 0 && assembly.Validation.Any(message => message.Severity == EngineAssemblyValidationSeverity.Warning))
        {
            throw new InvalidOperationException($"Engine compatibility probe failed: {label} produced unexpected warning validation messages.");
        }
    }
}
