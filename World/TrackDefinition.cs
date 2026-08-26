using Microsoft.Xna.Framework;

namespace RType.World;

public enum TrackLayout
{
    HighSpeedRing,
    LakesidePark,
    CustomSpline
}

public sealed record TrackDefinition(
    string Id,
    string DisplayName,
    TrackLayout Layout,
    float RoadHalfWidthMeters,
    float TerrainWidthMeters,
    float TerrainDepthMeters,
    int LengthMeters,
    int LongestStraightMeters,
    float ElevationDifferenceMeters,
    float SectorOneMarker = 1f / 3f,
    float SectorTwoMarker = 2f / 3f,
    TrackControlPoint[]? ControlPoints = null,
    TrackBankPoint[]? BankProfileDegrees = null,
    TrackSegmentShape[]? SegmentShapes = null,
    TrackWidthPoint[]? WidthPoints = null)
{
    public IReadOnlyList<float> SectorMarkers { get; } = [SectorOneMarker, SectorTwoMarker];
}

public readonly record struct TrackControlPoint(float X, float Z, float ElevationMeters);

public readonly record struct TrackBankPoint(float Progress, float BankDegrees);

public enum TrackSegmentShapeKind
{
    Curve,
    Straight
}

public readonly record struct TrackSegmentShape(int FromControlPoint, TrackSegmentShapeKind Shape);

public readonly record struct TrackWidthPoint(
    int ControlPoint,
    float LeftRoadWidthMeters,
    float RightRoadWidthMeters,
    float LeftGrassWidthMeters,
    float RightGrassWidthMeters,
    float LeftWallOffsetMeters,
    float RightWallOffsetMeters);

public readonly record struct TrackGeometryMetrics(
    float LengthMeters,
    float ElevationDifferenceMeters,
    float WidthMeters,
    float DepthMeters);

public readonly record struct TrackStartMetrics(
    Vector3 Position,
    Vector2 Tangent,
    float DistanceAlongTrackMeters);

public static class TrackCatalog
{
    public static readonly TrackDefinition HighSpeedRing = new(
        "high_speed_ring",
        "High Speed Ring",
        TrackLayout.HighSpeedRing,
        9.0f,
        1220f,
        760f,
        3100,
        794,
        0.0f);

    public static readonly TrackDefinition LakesidePark = new(
        "lakeside_park",
        "Lakeside Park",
        TrackLayout.LakesidePark,
        6.0f,
        1420f,
        820f,
        2410,
        600,
        31.0f,
        0.36f,
        0.74f);

    public static readonly TrackDefinition[] All =
    [
        HighSpeedRing,
        LakesidePark
    ];
}
