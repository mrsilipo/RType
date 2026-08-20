using Microsoft.Xna.Framework;

namespace RetroRacer.World;

public readonly record struct TrackProgress(
    float DistanceAlongTrackMeters,
    float NormalizedDistance,
    float SignedDistanceFromCenterMeters,
    float DistanceFromCenterMeters,
    Vector2 Forward,
    float ElevationMeters);

public interface ITrackProgressSampler
{
    float LengthMeters { get; }

    float RoadHalfWidthMeters { get; }

    IReadOnlyList<float> SectorMarkers => DefaultTrackSectorMarkers.Values;

    TrackProgress GetProgress(Vector3 position);
}

internal static class DefaultTrackSectorMarkers
{
    public static readonly float[] Values = [1f / 3f, 2f / 3f];
}
