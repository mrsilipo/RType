using Microsoft.Xna.Framework;

namespace RetroRacer.World;

public readonly record struct TrackBoundaryHit(
    Vector2 Point,
    Vector2 Normal,
    float PenetrationMeters,
    float ElevationMeters);
