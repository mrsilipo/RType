using RType.Rendering;
using RType.World;

namespace RType.Core;

internal static class SceneryInventoryReporter
{
    private sealed record Family(string Label, string[] Needles, string[] Exclusions);

    private static readonly Family[] Families =
    [
        new("road/asphalt", ["asphalt loop", "track_asphalt", "road_asphalt", "asphalt"], []),
        new("grass/field", ["grass", "field", "pasture", "meadow", "crop", "harvest"], ["farmhouse", "farm barn", "farm brick", "farm dark", "farm pale", "farm front"]),
        new("horizon/backdrop", ["backdrop"], []),
        new("trees/hedges", ["tree", "hedge", "scrub", "copse", "forest", "understory"], []),
        new("start/race furniture", ["start finish", "start spectator", "start paddock", "gantry", "grid", "race control"], []),
        new("corner safety furniture", ["chevron", "brake marker", "marshal", "tire stack", "barrier", "wall"], []),
        new("Salisbury/Wiltshire flavour", ["Salisbury", "Wiltshire", "chalk", "flint", "farm", "standing stone", "stream"], []),
        new("airfield flavour", ["airfield", "hangar", "windsock", "aircraft", "apron"], []),
        new("rail/underpass", ["railway", "underpass", "sleeper", "rail"], ["guard rail", "shadow rail", "back rail", "open gate rail"])
    ];

    public static void Report(TrackScene? track, SceneRenderer? sceneRenderer)
    {
        if (track is null)
        {
            Console.WriteLine("Scenery inventory: no track scene loaded.");
            return;
        }

        IReadOnlyList<StaticMesh> backdropMeshes = track.DrawProceduralBackdrop
            ? sceneRenderer?.BackdropMeshes ?? []
            : [];
        List<StaticMesh> meshes = track.Meshes.Concat(backdropMeshes).ToList();
        Console.WriteLine("Scenery inventory: High Speed Circuit runtime scene");
        Console.WriteLine($"  track: {track.Definition.DisplayName} ({track.Definition.Id})");
        Console.WriteLine($"  visual source: {track.VisualSource}");
        Console.WriteLine($"  track meshes: {track.Meshes.Count}");
        Console.WriteLine($"  backdrop meshes: {backdropMeshes.Count}");
        Console.WriteLine($"  total scenery meshes: {meshes.Count}");
        Console.WriteLine($"  transparent meshes: {meshes.Count(mesh => mesh.IsTransparent)}");
        foreach (StaticMesh mesh in meshes.Where(mesh => mesh.IsTransparent).OrderBy(mesh => mesh.Name, StringComparer.OrdinalIgnoreCase).Take(16))
        {
            Console.WriteLine($"    transparent - {mesh.Name}");
        }

        Console.WriteLine($"  track length: {track.LengthMeters:0.0}m");

        foreach (Family family in Families)
        {
            List<string> matches = meshes
                .Select(mesh => mesh.Name)
                .Where(name => ContainsAny(name, family.Needles) && !ContainsAny(name, family.Exclusions))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            Console.WriteLine($"  {family.Label}: {matches.Count}");
            foreach (string sample in matches.Take(5))
            {
                Console.WriteLine($"    - {sample}");
            }
        }
    }

    private static bool ContainsAny(string value, string[] needles)
    {
        foreach (string needle in needles)
        {
            if (value.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
