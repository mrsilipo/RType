using Microsoft.Xna.Framework;

namespace RType.World;

public readonly record struct TrackBoundaryHit(
    Vector2 Point,
    Vector2 Normal,
    float PenetrationMeters,
    float ElevationMeters);
