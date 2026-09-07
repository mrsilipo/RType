using Microsoft.Xna.Framework;

namespace RType.World;

public interface ITrackSurfaceSampler
{
    bool HasAuthoredSurfaceContact => false;

    SurfaceSample Sample(Vector3 position);

    float GetElevation(Vector2 position)
    {
        return 0f;
    }

    bool TryGetSurfaceContact(Vector2 position, out TrackSurfaceContact contact)
    {
        return TryGetSurfaceContact(new Vector3(position.X, float.PositiveInfinity, position.Y), float.PositiveInfinity, out contact);
    }

    bool TryGetSurfaceContact(Vector3 queryPosition, float downwardRangeMeters, out TrackSurfaceContact contact)
    {
        contact = default;
        return false;
    }

    bool TryGetSurfaceContactRay(Vector3 origin, Vector3 direction, float maxDistanceMeters, out TrackSurfaceContact contact)
    {
        contact = default;
        return false;
    }

    bool TryGetBoundaryHit(Vector2 position, float radiusMeters, out TrackBoundaryHit hit)
    {
        hit = default;
        return false;
    }
}
