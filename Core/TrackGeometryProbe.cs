using RType.Data;
using RType.World;

namespace RType.Core;

public static class TrackGeometryProbe
{
    public static void Run()
    {
        TrackDefinition[] tracks = TrackDefinitionFileLoader.LoadCatalog(TrackDefinitionFileLoader.DefaultTrackDirectory, TrackCatalog.All);
        if (tracks.Length == 0 || !string.Equals(tracks[0].Id, "high_speed_ring", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Track geometry probe failed: High Speed Ring is not the first/default catalog track.");
        }

        foreach (TrackDefinition track in tracks)
        {
            TrackGeometryMetrics metrics = TrackScene.MeasureGeometry(track);
            Console.WriteLine(
                $"{track.DisplayName}: centerline {metrics.LengthMeters:0.0} m, " +
                $"elevation {metrics.ElevationDifferenceMeters:0.0} m, " +
                $"bounds {metrics.WidthMeters:0.0} x {metrics.DepthMeters:0.0} m, " +
                $"road {track.RoadHalfWidthMeters * 2f:0.0} m");

            if (string.Equals(track.Id, "high_speed_ring", StringComparison.OrdinalIgnoreCase))
            {
                VerifyHighSpeedRing(track, metrics);
            }
        }
    }

    private static void VerifyHighSpeedRing(TrackDefinition track, TrackGeometryMetrics metrics)
    {
        const float metresPerSvgUnit = 0.934466258271f;
        TrackStartMetrics start = TrackScene.MeasureStart(track);
        RequireNear(metrics.LengthMeters, 3100f, 1.0f, "High Speed Ring length");
        RequireNear(track.RoadHalfWidthMeters * 2f, 18f, 0.01f, "High Speed Ring road width");
        RequireNear(metrics.ElevationDifferenceMeters, 0f, 0.01f, "High Speed Ring elevation");
        RequireNear(start.Position.X, 1109f * metresPerSvgUnit, 0.75f, "High Speed Ring start X");
        RequireNear(start.Position.Z, 198.69f * metresPerSvgUnit, 0.75f, "High Speed Ring start Z");
        RequireNear(start.Tangent.X, -1f, 0.02f, "High Speed Ring start tangent X");
        RequireNear(start.Tangent.Y, 0f, 0.02f, "High Speed Ring start tangent Z");
    }

    private static void RequireNear(float actual, float expected, float tolerance, string label)
    {
        if (MathF.Abs(actual - expected) > tolerance)
        {
            throw new InvalidOperationException($"{label} expected {expected:0.###}, got {actual:0.###}.");
        }
    }
}
