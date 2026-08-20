using RetroRacer.Data;
using RetroRacer.World;

namespace RetroRacer.Core;

public static class TrackEditorTool
{
    public static void Run()
    {
        TrackDefinitionFile[] files = TrackDefinitionFileLoader.LoadFiles(TrackDefinitionFileLoader.DefaultTrackDirectory);
        if (files.Length == 0)
        {
            Console.WriteLine($"No editable tracks found in {TrackDefinitionFileLoader.DefaultTrackDirectory}.");
            return;
        }

        Console.WriteLine("Editable tracks:");
        foreach (TrackDefinitionFile file in files.OrderBy(file => file.SortOrder).ThenBy(file => file.Definition.DisplayName))
        {
            TrackDefinition track = file.Definition;
            TrackGeometryMetrics metrics = TrackScene.MeasureGeometry(track);
            int segmentCount = track.SegmentShapes?.Length ?? track.ControlPoints?.Length ?? 0;
            int straightCount = track.SegmentShapes?.Count(segment => segment.Shape == TrackSegmentShapeKind.Straight) ?? 0;
            int widthCount = track.WidthPoints?.Length ?? 0;
            Console.WriteLine(
                $"{track.DisplayName} [{track.Id}] from {file.SourcePath}");
            Console.WriteLine(
                $"  layout {track.Layout}, control points {track.ControlPoints?.Length ?? 0}, " +
                $"bank points {track.BankProfileDegrees?.Length ?? 0}, segments {segmentCount}, straights {straightCount}, widths {widthCount}, " +
                $"sectors {string.Join(", ", track.SectorMarkers.Select(marker => marker.ToString("0.00")))}");
            Console.WriteLine(
                $"  centerline {metrics.LengthMeters:0.0} m, elevation {metrics.ElevationDifferenceMeters:0.0} m, " +
                $"bounds {metrics.WidthMeters:0.0} x {metrics.DepthMeters:0.0} m, road {track.RoadHalfWidthMeters * 2f:0.0} m");
        }

        TrackDefinition[] pickerTracks = TrackDefinitionFileLoader.LoadCatalog(TrackDefinitionFileLoader.DefaultTrackDirectory, TrackCatalog.All);
        Console.WriteLine();
        Console.WriteLine("Track picker order:");
        for (int i = 0; i < pickerTracks.Length; i++)
        {
            Console.WriteLine($"  {i + 1}. {pickerTracks[i].DisplayName} [{pickerTracks[i].Id}]");
        }
    }
}
