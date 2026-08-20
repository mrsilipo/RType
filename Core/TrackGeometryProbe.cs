using RetroRacer.Data;
using RetroRacer.World;

namespace RetroRacer.Core;

public static class TrackGeometryProbe
{
    public static void Run()
    {
        foreach (TrackDefinition track in TrackDefinitionFileLoader.LoadCatalog(TrackDefinitionFileLoader.DefaultTrackDirectory, TrackCatalog.All))
        {
            TrackGeometryMetrics metrics = TrackScene.MeasureGeometry(track);
            Console.WriteLine(
                $"{track.DisplayName}: centerline {metrics.LengthMeters:0.0} m, " +
                $"elevation {metrics.ElevationDifferenceMeters:0.0} m, " +
                $"bounds {metrics.WidthMeters:0.0} x {metrics.DepthMeters:0.0} m, " +
                $"road {track.RoadHalfWidthMeters * 2f:0.0} m");
        }
    }
}
