using Microsoft.Xna.Framework;

namespace RType.World;

public readonly record struct TrackSurfaceContact(
    Vector3 Position,
    Vector3 Normal,
    string SourceName,
    int TriangleIndex,
    int CandidateTriangleCount);

public readonly record struct AuthoredDriveableTriangle(
    Vector3 A,
    Vector3 B,
    Vector3 C,
    Vector3 Normal,
    string SourceName,
    int TriangleIndex);
