using Microsoft.Xna.Framework;

namespace RType.World;

public sealed class AuthoredTrackSurfaceSampler
{
    private const float MinimumGroundNormalY = 0.18f;
    private const float Epsilon = 0.00001f;

    private readonly AuthoredDriveableTriangle[] _triangles;
    private readonly Dictionary<(int X, int Z), List<int>> _cells = [];
    private readonly float _cellSizeMeters;
    private readonly float _minX;
    private readonly float _minZ;
    private readonly BoundingBox _bounds;

    public AuthoredTrackSurfaceSampler(IReadOnlyList<AuthoredDriveableTriangle> triangles, float cellSizeMeters = 8f)
    {
        _triangles = triangles
            .Where(IsUsableTriangle)
            .ToArray();
        _cellSizeMeters = MathF.Max(1f, cellSizeMeters);

        if (_triangles.Length == 0)
        {
            _bounds = new BoundingBox(Vector3.Zero, Vector3.Zero);
            return;
        }

        Vector3 min = _triangles[0].A;
        Vector3 max = _triangles[0].A;
        foreach (AuthoredDriveableTriangle triangle in _triangles)
        {
            min = Vector3.Min(min, Vector3.Min(triangle.A, Vector3.Min(triangle.B, triangle.C)));
            max = Vector3.Max(max, Vector3.Max(triangle.A, Vector3.Max(triangle.B, triangle.C)));
        }

        _bounds = new BoundingBox(min, max);
        _minX = min.X;
        _minZ = min.Z;
        BuildGrid();
        Stats = BuildStats();
    }

    public AuthoredTrackSurfaceStats Stats { get; private set; }

    public bool HasDriveableSurface => _triangles.Length > 0;

    public IReadOnlyList<AuthoredDriveableTriangle> Triangles => _triangles;

    public bool TryGetContact(Vector3 queryPosition, float downwardRangeMeters, out TrackSurfaceContact contact)
    {
        contact = default;
        if (_triangles.Length == 0)
        {
            return false;
        }

        float maxDrop = float.IsFinite(downwardRangeMeters)
            ? MathF.Max(0f, downwardRangeMeters)
            : float.PositiveInfinity;
        float topY = float.IsFinite(queryPosition.Y) ? queryPosition.Y : float.PositiveInfinity;
        float bottomY = float.IsFinite(topY) ? topY - maxDrop : float.NegativeInfinity;
        (int cellX, int cellZ) = ToCell(queryPosition.X, queryPosition.Z);
        if (!_cells.TryGetValue((cellX, cellZ), out List<int>? candidates))
        {
            return false;
        }

        float bestDrop = float.PositiveInfinity;
        TrackSurfaceContact best = default;
        foreach (int triangleIndex in candidates)
        {
            AuthoredDriveableTriangle triangle = _triangles[triangleIndex];
            if (triangle.Normal.Y < MinimumGroundNormalY)
            {
                continue;
            }

            if (!TrySampleTriangleY(queryPosition.X, queryPosition.Z, triangle, out float y))
            {
                continue;
            }

            if (float.IsFinite(topY))
            {
                if (y > topY + 0.02f || y < bottomY)
                {
                    continue;
                }
            }

            float drop = float.IsFinite(topY) ? topY - y : MathF.Abs(y);
            if (drop < bestDrop)
            {
                bestDrop = drop;
                best = new TrackSurfaceContact(
                    new Vector3(queryPosition.X, y, queryPosition.Z),
                    triangle.Normal,
                    triangle.SourceName,
                    triangle.TriangleIndex,
                    candidates.Count);
            }
        }

        if (float.IsFinite(bestDrop))
        {
            contact = best;
            return true;
        }

        return false;
    }

    public bool TryGetContact(Vector2 position, out TrackSurfaceContact contact)
    {
        return TryGetContact(new Vector3(position.X, float.PositiveInfinity, position.Y), float.PositiveInfinity, out contact);
    }

    public bool TryGetContactRay(Vector3 origin, Vector3 direction, float maxDistanceMeters, out TrackSurfaceContact contact)
    {
        contact = default;
        if (_triangles.Length == 0 ||
            direction.LengthSquared() <= Epsilon ||
            !float.IsFinite(maxDistanceMeters) ||
            maxDistanceMeters <= 0f)
        {
            return false;
        }

        Vector3 rayDirection = Vector3.Normalize(direction);
        float maxDistance = MathF.Max(0f, maxDistanceMeters);
        HashSet<int> candidateIndices = CollectRayCandidates(origin, rayDirection, maxDistance);
        if (candidateIndices.Count == 0)
        {
            return false;
        }

        float bestDistance = float.PositiveInfinity;
        TrackSurfaceContact best = default;
        foreach (int triangleIndex in candidateIndices)
        {
            AuthoredDriveableTriangle triangle = _triangles[triangleIndex];
            if (triangle.Normal.Y < MinimumGroundNormalY)
            {
                continue;
            }

            if (!TryIntersectRayTriangle(origin, rayDirection, triangle, out float distance))
            {
                continue;
            }

            if (distance < -0.001f || distance > maxDistance + 0.001f)
            {
                continue;
            }

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = new TrackSurfaceContact(
                    origin + rayDirection * distance,
                    triangle.Normal,
                    triangle.SourceName,
                    triangle.TriangleIndex,
                    candidateIndices.Count);
            }
        }

        if (float.IsFinite(bestDistance))
        {
            contact = best;
            return true;
        }

        return false;
    }

    private void BuildGrid()
    {
        for (int i = 0; i < _triangles.Length; i++)
        {
            AuthoredDriveableTriangle triangle = _triangles[i];
            float minX = MathF.Min(triangle.A.X, MathF.Min(triangle.B.X, triangle.C.X));
            float maxX = MathF.Max(triangle.A.X, MathF.Max(triangle.B.X, triangle.C.X));
            float minZ = MathF.Min(triangle.A.Z, MathF.Min(triangle.B.Z, triangle.C.Z));
            float maxZ = MathF.Max(triangle.A.Z, MathF.Max(triangle.B.Z, triangle.C.Z));
            (int minCellX, int minCellZ) = ToCell(minX, minZ);
            (int maxCellX, int maxCellZ) = ToCell(maxX, maxZ);

            for (int x = minCellX; x <= maxCellX; x++)
            {
                for (int z = minCellZ; z <= maxCellZ; z++)
                {
                    if (!_cells.TryGetValue((x, z), out List<int>? cell))
                    {
                        cell = [];
                        _cells[(x, z)] = cell;
                    }

                    cell.Add(i);
                }
            }
        }
    }

    private AuthoredTrackSurfaceStats BuildStats()
    {
        int occupied = _cells.Count;
        int totalRefs = _cells.Values.Sum(cell => cell.Count);
        int worst = occupied > 0 ? _cells.Values.Max(cell => cell.Count) : 0;
        float average = occupied > 0 ? totalRefs / (float)occupied : 0f;
        int gridWidth = occupied > 0
            ? _cells.Keys.Max(cell => cell.X) - _cells.Keys.Min(cell => cell.X) + 1
            : 0;
        int gridDepth = occupied > 0
            ? _cells.Keys.Max(cell => cell.Z) - _cells.Keys.Min(cell => cell.Z) + 1
            : 0;
        return new AuthoredTrackSurfaceStats(
            _triangles.Select(triangle => triangle.SourceName).Distinct(StringComparer.Ordinal).Count(),
            _triangles.Length,
            _cellSizeMeters,
            gridWidth,
            gridDepth,
            occupied,
            average,
            worst,
            _bounds);
    }

    private (int X, int Z) ToCell(float x, float z)
    {
        return (
            (int)MathF.Floor((x - _minX) / _cellSizeMeters),
            (int)MathF.Floor((z - _minZ) / _cellSizeMeters));
    }

    private HashSet<int> CollectRayCandidates(Vector3 origin, Vector3 direction, float maxDistance)
    {
        HashSet<int> candidates = [];
        Vector3 end = origin + direction * maxDistance;
        float minX = MathF.Min(origin.X, end.X);
        float maxX = MathF.Max(origin.X, end.X);
        float minZ = MathF.Min(origin.Z, end.Z);
        float maxZ = MathF.Max(origin.Z, end.Z);
        (int minCellX, int minCellZ) = ToCell(minX, minZ);
        (int maxCellX, int maxCellZ) = ToCell(maxX, maxZ);

        for (int x = minCellX; x <= maxCellX; x++)
        {
            for (int z = minCellZ; z <= maxCellZ; z++)
            {
                if (!_cells.TryGetValue((x, z), out List<int>? cell))
                {
                    continue;
                }

                foreach (int index in cell)
                {
                    candidates.Add(index);
                }
            }
        }

        return candidates;
    }

    private static bool IsUsableTriangle(AuthoredDriveableTriangle triangle)
    {
        return Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A).LengthSquared() > Epsilon &&
            triangle.Normal.LengthSquared() > Epsilon;
    }

    private static bool TrySampleTriangleY(float x, float z, AuthoredDriveableTriangle triangle, out float y)
    {
        Vector2 p = new(x, z);
        Vector2 a = new(triangle.A.X, triangle.A.Z);
        Vector2 b = new(triangle.B.X, triangle.B.Z);
        Vector2 c = new(triangle.C.X, triangle.C.Z);
        Vector2 v0 = b - a;
        Vector2 v1 = c - a;
        Vector2 v2 = p - a;
        float d00 = Vector2.Dot(v0, v0);
        float d01 = Vector2.Dot(v0, v1);
        float d11 = Vector2.Dot(v1, v1);
        float d20 = Vector2.Dot(v2, v0);
        float d21 = Vector2.Dot(v2, v1);
        float denominator = d00 * d11 - d01 * d01;
        if (MathF.Abs(denominator) <= Epsilon)
        {
            y = 0f;
            return false;
        }

        float v = (d11 * d20 - d01 * d21) / denominator;
        float w = (d00 * d21 - d01 * d20) / denominator;
        float u = 1f - v - w;
        const float tolerance = -0.0002f;
        if (u < tolerance || v < tolerance || w < tolerance)
        {
            y = 0f;
            return false;
        }

        y = u * triangle.A.Y + v * triangle.B.Y + w * triangle.C.Y;
        return float.IsFinite(y);
    }

    private static bool TryIntersectRayTriangle(
        Vector3 origin,
        Vector3 direction,
        AuthoredDriveableTriangle triangle,
        out float distance)
    {
        Vector3 edge1 = triangle.B - triangle.A;
        Vector3 edge2 = triangle.C - triangle.A;
        Vector3 p = Vector3.Cross(direction, edge2);
        float determinant = Vector3.Dot(edge1, p);
        if (MathF.Abs(determinant) <= Epsilon)
        {
            distance = 0f;
            return false;
        }

        float inverseDeterminant = 1f / determinant;
        Vector3 t = origin - triangle.A;
        float u = Vector3.Dot(t, p) * inverseDeterminant;
        if (u < -0.0002f || u > 1.0002f)
        {
            distance = 0f;
            return false;
        }

        Vector3 q = Vector3.Cross(t, edge1);
        float v = Vector3.Dot(direction, q) * inverseDeterminant;
        if (v < -0.0002f || u + v > 1.0002f)
        {
            distance = 0f;
            return false;
        }

        distance = Vector3.Dot(edge2, q) * inverseDeterminant;
        return float.IsFinite(distance);
    }
}

public readonly record struct AuthoredTrackSurfaceStats(
    int DriveableNodeCount,
    int TriangleCount,
    float CellSizeMeters,
    int GridWidth,
    int GridDepth,
    int OccupiedCellCount,
    float AverageTrianglesPerOccupiedCell,
    int WorstCaseTrianglesInCell,
    BoundingBox Bounds);
