using Microsoft.Xna.Framework;

namespace RType.World;

public interface ITrackSurfaceSampler
{
    SurfaceSample Sample(Vector3 position);

    float GetElevation(Vector2 position)
    {
        return 0f;
    }

    bool TryGetBoundaryHit(Vector2 position, float radiusMeters, out TrackBoundaryHit hit)
    {
        hit = default;
        return false;
    }
}
