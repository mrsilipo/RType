using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RetroRacer.Rendering;

namespace RetroRacer.World;

public sealed class TrackScene : ITrackSurfaceSampler, ITrackProgressSampler, IDisposable
{
    private const float WallOffsetFromRoadEdgeMeters = 1.45f;
    private const float WallCollisionShoulderMeters = 0.0f;
    private const float WallHeightMeters = 1.15f;
    private const float DefaultGrassWidthMeters = 26.0f;
    private static readonly ProfilePoint[] LakesideBankProfileDegrees =
    [
        new(0.00f, 0.0f),
        new(0.12f, 0.0f),
        new(0.18f, 2.0f),
        new(0.27f, 4.0f),
        new(0.37f, 1.0f),
        new(0.49f, -2.0f),
        new(0.60f, 1.5f),
        new(0.70f, 2.0f),
        new(0.82f, 1.0f),
        new(0.92f, 3.0f),
        new(1.00f, 0.0f)
    ];

    private readonly TrackDefinition _definition;
    private readonly SurfaceLibrary _surfaceLibrary;
    private readonly Vector3[] _centerLine;
    private readonly float[] _bankRadians;
    private readonly TrackWidthSample[] _widthSamples;
    private readonly float[] _cumulativeDistances;
    private readonly float _loopLengthMeters;
    private readonly float _startDistanceMeters;
    private readonly float _roadHalfWidth;
    private readonly List<StaticMesh> _meshes;

    private TrackScene(
        TrackDefinition definition,
        SurfaceLibrary surfaceLibrary,
        Vector3[] centerLine,
        float[] bankRadians,
        TrackWidthSample[] widthSamples,
        float[] cumulativeDistances,
        float loopLengthMeters,
        float startDistanceMeters,
        float roadHalfWidth,
        List<StaticMesh> meshes,
        Vector3 startPosition,
        float startHeadingRadians,
        bool isReverse)
    {
        _definition = definition;
        _surfaceLibrary = surfaceLibrary;
        _centerLine = centerLine;
        _bankRadians = bankRadians;
        _widthSamples = widthSamples;
        _cumulativeDistances = cumulativeDistances;
        _loopLengthMeters = loopLengthMeters;
        _startDistanceMeters = startDistanceMeters;
        _roadHalfWidth = roadHalfWidth;
        _meshes = meshes;
        StartPosition = startPosition;
        StartHeadingRadians = startHeadingRadians;
        IsReverse = isReverse;
    }

    public TrackDefinition Definition => _definition;

    public IReadOnlyList<StaticMesh> Meshes => _meshes;

    public Vector3 StartPosition { get; }

    public float StartHeadingRadians { get; }

    public bool IsReverse { get; }

    public float LengthMeters => _loopLengthMeters;

    public float RoadHalfWidthMeters => _roadHalfWidth;

    public IReadOnlyList<float> SectorMarkers => _definition.SectorMarkers;

    public static TrackGeometryMetrics MeasureGeometry(TrackDefinition definition)
    {
        Vector3[] centerLine = BuildCenterLine(definition);
        float length = CalculateLoopLengthXZ(centerLine);
        float minX = centerLine.Min(point => point.X);
        float maxX = centerLine.Max(point => point.X);
        float minZ = centerLine.Min(point => point.Z);
        float maxZ = centerLine.Max(point => point.Z);
        float minY = centerLine.Min(point => point.Y);
        float maxY = centerLine.Max(point => point.Y);
        return new TrackGeometryMetrics(length, maxY - minY, maxX - minX, maxZ - minZ);
    }

    public static TrackScene Create(GraphicsDevice graphicsDevice, GeneratedTextures textures)
    {
        throw new InvalidOperationException("A surface library is required to create a track scene.");
    }

    public static TrackScene Create(
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        TrackDefinition definition,
        bool reverse,
        SurfaceLibrary surfaceLibrary)
    {
        Vector3[] centerLine = BuildCenterLine(definition);
        float[] bankRadians = BuildBankRadians(definition, centerLine);
        TrackWidthSample[] widthSamples = BuildWidthSamples(definition, centerLine);
        if (reverse)
        {
            Array.Reverse(centerLine);
            Array.Reverse(bankRadians);
            Array.Reverse(widthSamples);
        }

        int startIndex = GetStartIndex(definition.Layout, centerLine);
        Vector2 tangent = GetTangent(centerLine, startIndex);
        Vector3 start = centerLine[startIndex];
        float roadHalfWidth = widthSamples.Length == centerLine.Length
            ? MathF.Max(widthSamples[startIndex].LeftRoadWidthMeters, widthSamples[startIndex].RightRoadWidthMeters)
            : definition.RoadHalfWidthMeters;
        float[] cumulativeDistances = CalculateCumulativeDistancesXZ(centerLine);
        float loopLengthMeters = cumulativeDistances[^1];
        float startDistanceMeters = cumulativeDistances[startIndex];

        List<StaticMesh> meshes = BuildTrackMeshes(graphicsDevice, textures, definition, centerLine, bankRadians, widthSamples, roadHalfWidth);
        Vector3 startPosition = new(start.X, start.Y, start.Z);
        float startHeadingRadians = MathF.Atan2(tangent.X, tangent.Y);
        return new TrackScene(
            definition,
            surfaceLibrary,
            centerLine,
            bankRadians,
            widthSamples,
            cumulativeDistances,
            loopLengthMeters,
            startDistanceMeters,
            roadHalfWidth,
            meshes,
            startPosition,
            startHeadingRadians,
            reverse);
    }

    private static List<StaticMesh> BuildTrackMeshes(
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        TrackDefinition definition,
        Vector3[] centerLine,
        float[] bankRadians,
        TrackWidthSample[] widthSamples,
        float roadHalfWidth)
    {
        TrackOffsetProfiles offsets = BuildOffsetProfiles(widthSamples, roadHalfWidth);
        float minX = centerLine.Min(point => point.X);
        float maxX = centerLine.Max(point => point.X);
        float minZ = centerLine.Min(point => point.Z);
        float maxZ = centerLine.Max(point => point.Z);
        float minY = centerLine.Min(point => point.Y);
        float maxLeftOffset = MathF.Max(offsets.LeftWall.Max(), offsets.LeftGrassOuter.Max());
        float maxRightOffset = MathF.Max(-offsets.RightWall.Min(), -offsets.RightGrassOuter.Min());
        float terrainPadding = MathF.Max(maxLeftOffset, maxRightOffset) + 48f;
        float terrainWidth = MathF.Max(definition.TerrainWidthMeters, maxX - minX + terrainPadding * 2f);
        float terrainDepth = MathF.Max(definition.TerrainDepthMeters, maxZ - minZ + terrainPadding * 2f);
        Vector3 terrainCenter = new((minX + maxX) * 0.5f, minY - 0.075f, (minZ + maxZ) * 0.5f);
        Vector3 wallGrey = new(0.54f, 0.56f, 0.55f);

        return
        [
            MeshFactory.CreatePlane(graphicsDevice, terrainCenter, terrainWidth, terrainDepth, textures.Grass, 9f, Vector3.One, "track grass field"),
            MeshFactory.CreateBankedOffsetRibbon(graphicsDevice, centerLine, bankRadians, offsets.LeftGrassInner, offsets.LeftGrassOuter, -0.026f, textures.Grass, 8.5f, "left grass shoulder"),
            MeshFactory.CreateBankedOffsetRibbon(graphicsDevice, centerLine, bankRadians, offsets.RightGrassOuter, offsets.RightGrassInner, -0.026f, textures.Grass, 8.5f, "right grass shoulder"),
            MeshFactory.CreateBankedOffsetRibbon(graphicsDevice, centerLine, bankRadians, offsets.RightRoadEdge, offsets.LeftRoadEdge, 0.0f, textures.Road, 5.4f, "asphalt loop"),
            MeshFactory.CreateBankedOffsetRibbon(graphicsDevice, centerLine, bankRadians, offsets.LeftCurbInner, offsets.LeftCurbOuter, 0.018f, textures.Curb, 2.6f, "left curb"),
            MeshFactory.CreateBankedOffsetRibbon(graphicsDevice, centerLine, bankRadians, offsets.RightCurbOuter, offsets.RightCurbInner, 0.018f, textures.Curb, 2.6f, "right curb"),
            MeshFactory.CreateOffsetWall(graphicsDevice, centerLine, offsets.LeftWall, WallHeightMeters, 0.02f, textures.White, 6.0f, wallGrey, "left grey wall"),
            MeshFactory.CreateOffsetWall(graphicsDevice, centerLine, offsets.RightWall, WallHeightMeters, 0.02f, textures.White, 6.0f, wallGrey, "right grey wall")
        ];
    }

    public SurfaceSample Sample(Vector3 position)
    {
        CenterLineProjection projection = ProjectToCenterLine(new Vector2(position.X, position.Z));
        TrackWidthSample width = GetProjectionWidthSample(projection);
        float roadWidth = width.RoadWidthForSignedDistance(projection.SignedDistance);
        if (projection.Distance <= roadWidth)
        {
            return _surfaceLibrary.Road;
        }

        if (projection.Distance <= roadWidth + 1.3f)
        {
            return _surfaceLibrary.Curb;
        }

        return _surfaceLibrary.Grass;
    }

    public float GetElevation(Vector2 position)
    {
        CenterLineProjection projection = ProjectToCenterLine(position);
        TrackWidthSample width = GetProjectionWidthSample(projection);
        float roadWidth = width.RoadWidthForSignedDistance(projection.SignedDistance);
        float grassWidth = width.GrassWidthForSignedDistance(projection.SignedDistance);
        return projection.Distance <= roadWidth + grassWidth ? CalculateSurfaceElevation(projection) : 0f;
    }

    public TrackProgress GetProgress(Vector3 position)
    {
        CenterLineProjection projection = ProjectToCenterLine(new Vector2(position.X, position.Z));
        float distanceFromStart = projection.DistanceAlongTrackMeters - _startDistanceMeters;
        if (_loopLengthMeters > 0.001f)
        {
            distanceFromStart %= _loopLengthMeters;
            if (distanceFromStart < 0f)
            {
                distanceFromStart += _loopLengthMeters;
            }
        }
        else
        {
            distanceFromStart = 0f;
        }

        float normalized = _loopLengthMeters > 0.001f
            ? distanceFromStart / _loopLengthMeters
            : 0f;
        return new TrackProgress(
            distanceFromStart,
            normalized,
            projection.SignedDistance,
            projection.Distance,
            projection.Tangent,
            CalculateSurfaceElevation(projection));
    }

    public bool TryGetBoundaryHit(Vector2 position, float radiusMeters, out TrackBoundaryHit hit)
    {
        CenterLineProjection projection = ProjectToCenterLine(position);
        TrackWidthSample width = GetProjectionWidthSample(projection);
        float roadWidth = width.RoadWidthForSignedDistance(projection.SignedDistance);
        float wallOffsetFromRoad = width.WallOffsetForSignedDistance(projection.SignedDistance);
        float wallOffset = roadWidth + wallOffsetFromRoad;
        float limit = MathF.Max(roadWidth, wallOffset - MathF.Max(0f, radiusMeters) - WallCollisionShoulderMeters);
        float absSignedDistance = MathF.Abs(projection.SignedDistance);

        if (absSignedDistance <= limit)
        {
            hit = default;
            return false;
        }

        float sideSign = MathF.Sign(projection.SignedDistance);
        if (sideSign == 0f)
        {
            sideSign = 1f;
        }

        Vector2 inwardNormal = -sideSign * projection.LeftNormal;
        Vector2 wallPoint = projection.ClosestPoint + projection.LeftNormal * sideSign * wallOffset;
        hit = new TrackBoundaryHit(
            wallPoint,
            inwardNormal,
            absSignedDistance - limit,
            CalculateSurfaceElevation(projection));
        return true;
    }

    public void Dispose()
    {
        foreach (StaticMesh mesh in _meshes)
        {
            mesh.Dispose();
        }
    }

    private float CalculateSurfaceElevation(CenterLineProjection projection)
    {
        float bankRadians = GetProjectionBankRadians(projection);
        TrackWidthSample width = GetProjectionWidthSample(projection);
        float roadWidth = width.RoadWidthForSignedDistance(projection.SignedDistance);
        float grassWidth = width.GrassWidthForSignedDistance(projection.SignedDistance);
        if (MathF.Abs(bankRadians) <= 0.0001f)
        {
            return projection.Elevation;
        }

        float bankedDistance = MathHelper.Clamp(
            projection.SignedDistance,
            -width.RightRoadWidthMeters - 1.3f,
            width.LeftRoadWidthMeters + 1.3f);
        float bankedElevation = projection.Elevation + bankedDistance * MathF.Tan(bankRadians);
        if (projection.Distance <= roadWidth + 1.3f)
        {
            return bankedElevation;
        }

        float shoulderT = SmoothStep(roadWidth + 1.3f, roadWidth + grassWidth, projection.Distance);
        return MathHelper.Lerp(bankedElevation, projection.Elevation, shoulderT);
    }

    private float GetProjectionBankRadians(CenterLineProjection projection)
    {
        if (_bankRadians.Length != _centerLine.Length || _bankRadians.Length == 0)
        {
            return 0f;
        }

        int index = Math.Clamp(projection.SegmentIndex, 0, _bankRadians.Length - 1);
        int nextIndex = (index + 1) % _bankRadians.Length;
        return MathHelper.Lerp(_bankRadians[index], _bankRadians[nextIndex], MathHelper.Clamp(projection.SegmentT, 0f, 1f));
    }

    private TrackWidthSample GetProjectionWidthSample(CenterLineProjection projection)
    {
        if (_widthSamples.Length != _centerLine.Length || _widthSamples.Length == 0)
        {
            return TrackWidthSample.FromSymmetric(_roadHalfWidth * 2f, DefaultGrassWidthMeters, WallOffsetFromRoadEdgeMeters);
        }

        int index = Math.Clamp(projection.SegmentIndex, 0, _widthSamples.Length - 1);
        int nextIndex = (index + 1) % _widthSamples.Length;
        float t = MathHelper.Clamp(projection.SegmentT, 0f, 1f);
        TrackWidthSample a = _widthSamples[index];
        TrackWidthSample b = _widthSamples[nextIndex];
        return TrackWidthSample.Lerp(a, b, t);
    }

    private static Vector3[] BuildCenterLine(TrackDefinition definition)
    {
        if (definition.ControlPoints is { Length: >= 4 } controlPoints)
        {
            return BuildCustomSplineCenterLine(
                controlPoints,
                definition.SegmentShapes,
                definition.LengthMeters);
        }

        return definition.Layout switch
        {
            TrackLayout.LakesidePark => BuildLakesideParkCenterLine(definition.LengthMeters, definition.ElevationDifferenceMeters),
            TrackLayout.VelocityLoop => BuildVelocityLoopCenterLine(),
            TrackLayout.CustomSpline => BuildHighSpeedRingInspiredCenterLine(definition.LengthMeters, definition.ElevationDifferenceMeters),
            _ => BuildHighSpeedRingInspiredCenterLine(definition.LengthMeters, definition.ElevationDifferenceMeters)
        };
    }

    private static float[] BuildBankRadians(TrackDefinition definition, IReadOnlyList<Vector3> centerLine)
    {
        float[] bankRadians = new float[centerLine.Count];
        if (centerLine.Count == 0)
        {
            return bankRadians;
        }

        TrackBankPoint[]? customBankProfile = definition.BankProfileDegrees;
        if (customBankProfile is { Length: > 0 })
        {
            ProfilePoint[] profile = customBankProfile
                .OrderBy(point => point.Progress)
                .Select(point => new ProfilePoint(point.Progress, point.BankDegrees))
                .ToArray();
            float[] customDistances = CalculateCumulativeDistancesXZ(centerLine);
            float customLength = MathF.Max(0.001f, customDistances[^1]);
            for (int i = 0; i < centerLine.Count; i++)
            {
                float progress = customDistances[i] / customLength;
                bankRadians[i] = MathHelper.ToRadians(SampleLoopedProfile(profile, progress));
            }

            return bankRadians;
        }

        if (definition.Layout != TrackLayout.LakesidePark)
        {
            return bankRadians;
        }

        float[] distances = CalculateCumulativeDistancesXZ(centerLine);
        float length = MathF.Max(0.001f, distances[^1]);
        for (int i = 0; i < centerLine.Count; i++)
        {
            float progress = distances[i] / length;
            bankRadians[i] = MathHelper.ToRadians(SampleLoopedProfile(LakesideBankProfileDegrees, progress));
        }

        return bankRadians;
    }

    private static TrackWidthSample[] BuildWidthSamples(TrackDefinition definition, IReadOnlyList<Vector3> centerLine)
    {
        TrackWidthSample[] samples = new TrackWidthSample[centerLine.Count];
        if (samples.Length == 0)
        {
            return samples;
        }

        int controlPointCount = definition.ControlPoints?.Length ?? 0;
        TrackWidthPoint[] widthPoints = BuildControlWidthPoints(definition, controlPointCount);
        if (controlPointCount < 2 || widthPoints.Length == 0)
        {
            FillUniformWidthSamples(samples, definition.RoadHalfWidthMeters * 2f, DefaultGrassWidthMeters, WallOffsetFromRoadEdgeMeters);
            return samples;
        }

        for (int i = 0; i < samples.Length; i++)
        {
            float controlPosition = i / (float)samples.Length * controlPointCount;
            int a = Math.Clamp((int)MathF.Floor(controlPosition), 0, controlPointCount - 1);
            int b = (a + 1) % controlPointCount;
            float t = controlPosition - MathF.Floor(controlPosition);
            TrackWidthPoint widthA = widthPoints[a];
            TrackWidthPoint widthB = widthPoints[b];
            samples[i] = new TrackWidthSample(
                MathHelper.Lerp(widthA.LeftRoadWidthMeters, widthB.LeftRoadWidthMeters, t),
                MathHelper.Lerp(widthA.RightRoadWidthMeters, widthB.RightRoadWidthMeters, t),
                MathHelper.Lerp(widthA.LeftGrassWidthMeters, widthB.LeftGrassWidthMeters, t),
                MathHelper.Lerp(widthA.RightGrassWidthMeters, widthB.RightGrassWidthMeters, t),
                MathHelper.Lerp(widthA.LeftWallOffsetMeters, widthB.LeftWallOffsetMeters, t),
                MathHelper.Lerp(widthA.RightWallOffsetMeters, widthB.RightWallOffsetMeters, t));
        }

        return samples;
    }

    private static TrackWidthPoint[] BuildControlWidthPoints(TrackDefinition definition, int controlPointCount)
    {
        if (controlPointCount <= 0)
        {
            return [];
        }

        float defaultRoadWidth = Math.Clamp(definition.RoadHalfWidthMeters * 2f, 4f, 84f);
        TrackWidthPoint[] points = new TrackWidthPoint[controlPointCount];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = TrackWidthPointFromSymmetric(i, defaultRoadWidth, DefaultGrassWidthMeters, WallOffsetFromRoadEdgeMeters);
        }

        if (definition.WidthPoints is null)
        {
            return points;
        }

        foreach (TrackWidthPoint point in definition.WidthPoints)
        {
            if (point.ControlPoint < 0 || point.ControlPoint >= points.Length)
            {
                continue;
            }

            points[point.ControlPoint] = new TrackWidthPoint(
                point.ControlPoint,
                Math.Clamp(point.LeftRoadWidthMeters <= 0f ? defaultRoadWidth * 0.5f : point.LeftRoadWidthMeters, 2f, 42f),
                Math.Clamp(point.RightRoadWidthMeters <= 0f ? defaultRoadWidth * 0.5f : point.RightRoadWidthMeters, 2f, 42f),
                Math.Clamp(point.LeftGrassWidthMeters <= 0f ? DefaultGrassWidthMeters : point.LeftGrassWidthMeters, 0f, 90f),
                Math.Clamp(point.RightGrassWidthMeters <= 0f ? DefaultGrassWidthMeters : point.RightGrassWidthMeters, 0f, 90f),
                Math.Clamp(point.LeftWallOffsetMeters <= 0f ? WallOffsetFromRoadEdgeMeters : point.LeftWallOffsetMeters, 0.5f, 95f),
                Math.Clamp(point.RightWallOffsetMeters <= 0f ? WallOffsetFromRoadEdgeMeters : point.RightWallOffsetMeters, 0.5f, 95f));
        }

        return points;
    }

    private static TrackWidthPoint TrackWidthPointFromSymmetric(
        int controlPoint,
        float roadWidthMeters,
        float grassWidthMeters,
        float wallOffsetMeters)
    {
        float halfRoad = Math.Clamp(roadWidthMeters, 4f, 84f) * 0.5f;
        return new TrackWidthPoint(
            controlPoint,
            halfRoad,
            halfRoad,
            Math.Clamp(grassWidthMeters, 0f, 90f),
            Math.Clamp(grassWidthMeters, 0f, 90f),
            Math.Clamp(wallOffsetMeters, 0.5f, 95f),
            Math.Clamp(wallOffsetMeters, 0.5f, 95f));
    }

    private static void FillUniformWidthSamples(
        TrackWidthSample[] samples,
        float roadWidthMeters,
        float grassWidthMeters,
        float wallOffsetMeters)
    {
        Array.Fill(samples, TrackWidthSample.FromSymmetric(roadWidthMeters, grassWidthMeters, wallOffsetMeters));
    }

    private static TrackOffsetProfiles BuildOffsetProfiles(IReadOnlyList<TrackWidthSample> widthSamples, float fallbackRoadHalfWidth)
    {
        int count = widthSamples.Count;
        float[] leftRoad = new float[count];
        float[] rightRoad = new float[count];
        float[] leftCurbInner = new float[count];
        float[] leftCurbOuter = new float[count];
        float[] rightCurbInner = new float[count];
        float[] rightCurbOuter = new float[count];
        float[] leftGrassInner = new float[count];
        float[] leftGrassOuter = new float[count];
        float[] rightGrassInner = new float[count];
        float[] rightGrassOuter = new float[count];
        float[] leftWall = new float[count];
        float[] rightWall = new float[count];

        for (int i = 0; i < count; i++)
        {
            TrackWidthSample sample = widthSamples[i];
            float leftRoadWidth = sample.LeftRoadWidthMeters > 0f ? sample.LeftRoadWidthMeters : fallbackRoadHalfWidth;
            float rightRoadWidth = sample.RightRoadWidthMeters > 0f ? sample.RightRoadWidthMeters : fallbackRoadHalfWidth;
            float leftCurbOuterOffset = leftRoadWidth + 1.10f;
            float rightCurbOuterOffset = rightRoadWidth + 1.10f;
            float leftGrassOuterOffset = leftRoadWidth + MathF.Max(1.10f, sample.LeftGrassWidthMeters);
            float rightGrassOuterOffset = rightRoadWidth + MathF.Max(1.10f, sample.RightGrassWidthMeters);
            leftRoad[i] = leftRoadWidth;
            rightRoad[i] = -rightRoadWidth;
            leftCurbInner[i] = leftRoadWidth + 0.20f;
            leftCurbOuter[i] = leftCurbOuterOffset;
            rightCurbInner[i] = -rightRoadWidth - 0.20f;
            rightCurbOuter[i] = -rightCurbOuterOffset;
            leftGrassInner[i] = leftCurbOuterOffset;
            leftGrassOuter[i] = leftGrassOuterOffset;
            rightGrassInner[i] = -rightCurbOuterOffset;
            rightGrassOuter[i] = -rightGrassOuterOffset;
            leftWall[i] = leftRoadWidth + MathF.Max(0.5f, sample.LeftWallOffsetMeters);
            rightWall[i] = -rightRoadWidth - MathF.Max(0.5f, sample.RightWallOffsetMeters);
        }

        return new TrackOffsetProfiles(
            leftRoad,
            rightRoad,
            leftCurbInner,
            leftCurbOuter,
            rightCurbInner,
            rightCurbOuter,
            leftGrassInner,
            leftGrassOuter,
            rightGrassInner,
            rightGrassOuter,
            leftWall,
            rightWall);
    }

    private static Vector3[] BuildHighSpeedRingInspiredCenterLine(float targetLengthMeters, float elevationDifferenceMeters)
    {
        Vector2[] controls =
        [
            new(-535f, 330f),
            new(-260f, 330f),
            new(120f, 330f),
            new(550f, 330f),
            new(675f, 300f),
            new(755f, 220f),
            new(720f, 145f),
            new(585f, 125f),
            new(505f, 55f),
            new(430f, -120f),
            new(330f, -265f),
            new(215f, -282f),
            new(135f, -238f),
            new(110f, -145f),
            new(42f, -92f),
            new(-70f, -146f),
            new(-245f, -330f),
            new(-420f, -575f),
            new(-560f, -552f),
            new(-720f, -235f),
            new(-835f, 45f),
            new(-835f, 230f),
            new(-730f, 315f)
        ];

        Vector2[] points = BuildCatmullRomLoop(controls, 20);
        ScaleToTargetLength(points, targetLengthMeters);
        return AddElevation(points, elevationDifferenceMeters);
    }

    private static Vector3[] BuildVelocityLoopCenterLine()
    {
        Vector2[] controls =
        [
            new(-86f, -18f),
            new(-66f, -45f),
            new(-12f, -54f),
            new(58f, -48f),
            new(94f, -24f),
            new(92f, 14f),
            new(58f, 38f),
            new(-8f, 44f),
            new(-72f, 36f),
            new(-96f, 12f)
        ];

        Vector2[] points = BuildCatmullRomLoop(controls, 14);
        Vector3[] centerLine = new Vector3[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            centerLine[i] = new Vector3(points[i].X, 0f, points[i].Y);
        }

        return centerLine;
    }

    private static Vector3[] BuildLakesideParkCenterLine(float targetLengthMeters, float elevationDifferenceMeters)
    {
        Vector3[] controls =
        [
            new(245f, 17f, -174f),
            new(60f, 15f, -178f),
            new(-150f, 11f, -178f),
            new(-375f, 6f, -166f),
            new(-525f, 1f, -118f),
            new(-604f, 0f, -42f),
            new(-566f, 2f, 42f),
            new(-438f, 8f, 90f),
            new(-282f, 18f, 126f),
            new(-124f, 27f, 142f),
            new(36f, 22f, 124f),
            new(196f, 17f, 116f),
            new(360f, 14f, 142f),
            new(512f, 16f, 206f),
            new(635f, 20f, 252f),
            new(735f, 25f, 206f),
            new(784f, 29f, 82f),
            new(722f, 31f, -42f),
            new(586f, 30f, -116f),
            new(424f, 24f, -158f)
        ];

        for (int i = 0; i < controls.Length; i++)
        {
            controls[i] = new Vector3(controls[i].X, controls[i].Y, controls[i].Z * 1.35f);
        }

        Vector3[] points = BuildCatmullRomLoop(controls, 4);
        ScaleToTargetLengthXZ(points, targetLengthMeters);
        NormalizeElevationRange(points, elevationDifferenceMeters);
        return points;
    }

    private static Vector3[] BuildCustomSplineCenterLine(
        IReadOnlyList<TrackControlPoint> controls,
        IReadOnlyList<TrackSegmentShape>? segmentShapes,
        float targetLengthMeters)
    {
        Vector3[] controlVectors = new Vector3[controls.Count];
        for (int i = 0; i < controls.Count; i++)
        {
            TrackControlPoint control = controls[i];
            controlVectors[i] = new Vector3(control.X, control.ElevationMeters, control.Z);
        }

        int samplesPerSegment = Math.Clamp(
            (int)MathF.Ceiling(targetLengthMeters / MathF.Max(1f, controls.Count * 18f)),
            4,
            12);
        TrackSegmentShapeKind[] segmentShapeLookup = BuildSegmentShapeLookup(segmentShapes, controls.Count);
        Vector3[] points = BuildMixedSplineLoop(controlVectors, segmentShapeLookup, samplesPerSegment);
        ScaleToTargetLengthXZ(points, targetLengthMeters);
        return points;
    }

    private static TrackSegmentShapeKind[] BuildSegmentShapeLookup(IReadOnlyList<TrackSegmentShape>? segmentShapes, int controlPointCount)
    {
        TrackSegmentShapeKind[] lookup = new TrackSegmentShapeKind[controlPointCount];
        Array.Fill(lookup, TrackSegmentShapeKind.Curve);
        if (segmentShapes is null)
        {
            return lookup;
        }

        foreach (TrackSegmentShape segment in segmentShapes)
        {
            if (segment.FromControlPoint >= 0 && segment.FromControlPoint < lookup.Length)
            {
                lookup[segment.FromControlPoint] = segment.Shape;
            }
        }

        return lookup;
    }

    private static Vector3[] BuildMixedSplineLoop(
        IReadOnlyList<Vector3> controls,
        IReadOnlyList<TrackSegmentShapeKind> segmentShapes,
        int samplesPerSegment)
    {
        Vector3[] points = new Vector3[controls.Count * samplesPerSegment];
        int index = 0;

        for (int i = 0; i < controls.Count; i++)
        {
            Vector3 p0 = controls[(i - 1 + controls.Count) % controls.Count];
            Vector3 p1 = controls[i];
            Vector3 p2 = controls[(i + 1) % controls.Count];
            Vector3 p3 = controls[(i + 2) % controls.Count];
            bool straight = i < segmentShapes.Count && segmentShapes[i] == TrackSegmentShapeKind.Straight;

            for (int sample = 0; sample < samplesPerSegment; sample++)
            {
                float t = sample / (float)samplesPerSegment;
                points[index++] = straight
                    ? Vector3.Lerp(p1, p2, t)
                    : Vector3.CatmullRom(p0, p1, p2, p3, t);
            }
        }

        return points;
    }

    private static Vector2[] BuildCatmullRomLoop(IReadOnlyList<Vector2> controls, int samplesPerSegment)
    {
        Vector2[] points = new Vector2[controls.Count * samplesPerSegment];
        int index = 0;

        for (int i = 0; i < controls.Count; i++)
        {
            Vector2 p0 = controls[(i - 1 + controls.Count) % controls.Count];
            Vector2 p1 = controls[i];
            Vector2 p2 = controls[(i + 1) % controls.Count];
            Vector2 p3 = controls[(i + 2) % controls.Count];

            for (int sample = 0; sample < samplesPerSegment; sample++)
            {
                float t = sample / (float)samplesPerSegment;
                points[index++] = Vector2.CatmullRom(p0, p1, p2, p3, t);
            }
        }

        return points;
    }

    private static Vector3[] BuildCatmullRomLoop(IReadOnlyList<Vector3> controls, int samplesPerSegment)
    {
        Vector3[] points = new Vector3[controls.Count * samplesPerSegment];
        int index = 0;

        for (int i = 0; i < controls.Count; i++)
        {
            Vector3 p0 = controls[(i - 1 + controls.Count) % controls.Count];
            Vector3 p1 = controls[i];
            Vector3 p2 = controls[(i + 1) % controls.Count];
            Vector3 p3 = controls[(i + 2) % controls.Count];

            for (int sample = 0; sample < samplesPerSegment; sample++)
            {
                float t = sample / (float)samplesPerSegment;
                points[index++] = Vector3.CatmullRom(p0, p1, p2, p3, t);
            }
        }

        return points;
    }

    private static void ScaleToTargetLength(Vector2[] points, float targetLengthMeters)
    {
        float length = CalculateLoopLength(points);
        if (length <= 0.001f)
        {
            return;
        }

        float scale = targetLengthMeters / length;
        for (int i = 0; i < points.Length; i++)
        {
            points[i] *= scale;
        }
    }

    private static void ScaleToTargetLengthXZ(Vector3[] points, float targetLengthMeters)
    {
        float length = CalculateLoopLengthXZ(points);
        if (length <= 0.001f)
        {
            return;
        }

        float scale = targetLengthMeters / length;
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = new Vector3(points[i].X * scale, points[i].Y, points[i].Z * scale);
        }
    }

    private static void NormalizeElevationRange(Vector3[] points, float elevationDifferenceMeters)
    {
        if (points.Length == 0)
        {
            return;
        }

        float min = points.Min(point => point.Y);
        float max = points.Max(point => point.Y);
        float range = MathF.Max(0.001f, max - min);
        for (int i = 0; i < points.Length; i++)
        {
            float normalized = (points[i].Y - min) / range;
            points[i] = new Vector3(points[i].X, normalized * elevationDifferenceMeters, points[i].Z);
        }
    }

    private static Vector3[] AddElevation(Vector2[] points, float elevationDifferenceMeters)
    {
        float[] distances = CalculateCumulativeDistances(points);
        float totalLength = distances[^1];
        float[] rawElevations = new float[points.Length];
        float min = float.MaxValue;
        float max = float.MinValue;

        for (int i = 0; i < points.Length; i++)
        {
            float progress = totalLength <= 0.001f ? 0f : distances[i] / totalLength;
            float broadHill = MathF.Sin(progress * MathF.Tau - 0.8f);
            float secondaryRise = 0.35f * MathF.Sin(progress * MathF.Tau * 2.0f + 1.2f);
            float raw = broadHill + secondaryRise;
            rawElevations[i] = raw;
            min = MathF.Min(min, raw);
            max = MathF.Max(max, raw);
        }

        Vector3[] elevated = new Vector3[points.Length];
        float range = MathF.Max(0.001f, max - min);
        for (int i = 0; i < points.Length; i++)
        {
            float normalized = (rawElevations[i] - min) / range;
            elevated[i] = new Vector3(points[i].X, normalized * elevationDifferenceMeters, points[i].Y);
        }

        return elevated;
    }

    private static float[] CalculateCumulativeDistances(IReadOnlyList<Vector2> points)
    {
        float[] distances = new float[points.Count + 1];
        for (int i = 0; i < points.Count; i++)
        {
            distances[i + 1] = distances[i] + Vector2.Distance(points[i], points[(i + 1) % points.Count]);
        }

        return distances;
    }

    private static float[] CalculateCumulativeDistancesXZ(IReadOnlyList<Vector3> points)
    {
        float[] distances = new float[points.Count + 1];
        for (int i = 0; i < points.Count; i++)
        {
            distances[i + 1] = distances[i] + Vector2.Distance(ToXZ(points[i]), ToXZ(points[(i + 1) % points.Count]));
        }

        return distances;
    }

    private static float CalculateLoopLength(IReadOnlyList<Vector2> points)
    {
        float length = 0f;
        for (int i = 0; i < points.Count; i++)
        {
            length += Vector2.Distance(points[i], points[(i + 1) % points.Count]);
        }

        return length;
    }

    private static float CalculateLoopLengthXZ(IReadOnlyList<Vector3> points)
    {
        float length = 0f;
        for (int i = 0; i < points.Count; i++)
        {
            length += Vector2.Distance(ToXZ(points[i]), ToXZ(points[(i + 1) % points.Count]));
        }

        return length;
    }

    private float DistanceToCenterLine(Vector2 point)
    {
        return ProjectToCenterLine(point).Distance;
    }

    private CenterLineProjection ProjectToCenterLine(Vector2 point)
    {
        float best = float.MaxValue;
        CenterLineProjection bestProjection = new(float.MaxValue, 0f, 0f, 0f, point, Vector2.UnitX, Vector2.UnitY, 0, 0f);
        for (int i = 0; i < _centerLine.Length; i++)
        {
            Vector3 a3 = _centerLine[i];
            Vector3 b3 = _centerLine[(i + 1) % _centerLine.Length];
            Vector2 a = ToXZ(a3);
            Vector2 b = ToXZ(b3);
            SegmentProjection projection = ProjectPointToSegment(point, a, b);
            if (projection.Distance < best)
            {
                best = projection.Distance;
                Vector2 segment = b - a;
                float segmentLength = segment.Length();
                Vector2 tangent = segmentLength <= 0.0001f ? Vector2.UnitY : segment / segmentLength;
                Vector2 leftNormal = GetLeftNormal(a, b);
                float elevation = MathHelper.Lerp(a3.Y, b3.Y, projection.T);
                float signedDistance = Vector2.Dot(point - projection.Point, leftNormal);
                float distanceAlongTrack = _cumulativeDistances[i] + segmentLength * projection.T;
                bestProjection = new CenterLineProjection(
                    projection.Distance,
                    signedDistance,
                    distanceAlongTrack,
                    elevation,
                    projection.Point,
                    leftNormal,
                    tangent,
                    i,
                    projection.T);
            }
        }

        return bestProjection;
    }

    private static float DistancePointToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        return ProjectPointToSegment(point, a, b).Distance;
    }

    private static SegmentProjection ProjectPointToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lengthSquared = ab.LengthSquared();
        if (lengthSquared <= 0.0001f)
        {
            return new SegmentProjection(Vector2.Distance(point, a), 0f, a);
        }

        float t = MathHelper.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0f, 1f);
        Vector2 closest = a + ab * t;
        return new SegmentProjection(Vector2.Distance(point, closest), t, closest);
    }

    private static Vector2 GetTangent(Vector3[] points, int index)
    {
        Vector2 previous = ToXZ(points[(index - 1 + points.Length) % points.Length]);
        Vector2 next = ToXZ(points[(index + 1) % points.Length]);
        Vector2 tangent = next - previous;
        return tangent.LengthSquared() <= 0.0001f ? Vector2.UnitY : Vector2.Normalize(tangent);
    }

    private static Vector2 GetLeftNormal(Vector3[] points, int index)
    {
        Vector2 tangent = GetTangent(points, index);
        return new Vector2(-tangent.Y, tangent.X);
    }

    private static int GetStartIndex(TrackLayout layout, IReadOnlyList<Vector3> centerLine)
    {
        if (layout == TrackLayout.CustomSpline)
        {
            return 0;
        }

        if (layout == TrackLayout.LakesidePark)
        {
            float minZ = centerLine.Min(point => point.Z);
            int lakesideBestIndex = 0;
            float lakesideBestScore = float.MaxValue;
            for (int i = 0; i < centerLine.Count; i++)
            {
                Vector3 point = centerLine[i];
                float score = MathF.Abs(point.X - 150f) + MathF.Abs(point.Z - minZ) * 7f;
                if (score < lakesideBestScore)
                {
                    lakesideBestScore = score;
                    lakesideBestIndex = i;
                }
            }

            return lakesideBestIndex;
        }

        if (layout != TrackLayout.HighSpeedRingInspired)
        {
            return centerLine.Count * 3 / 4;
        }

        float topZ = centerLine.Max(point => point.Z);
        int bestIndex = 0;
        float bestScore = float.MaxValue;
        for (int i = 0; i < centerLine.Count; i++)
        {
            Vector3 point = centerLine[i];
            float score = MathF.Abs(point.X) + MathF.Abs(topZ - point.Z) * 8f;
            if (score < bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private static Vector2 ToXZ(Vector3 value)
    {
        return new Vector2(value.X, value.Z);
    }

    private static Vector2 GetLeftNormal(Vector2 a, Vector2 b)
    {
        Vector2 tangent = b - a;
        if (tangent.LengthSquared() <= 0.0001f)
        {
            return Vector2.UnitX;
        }

        tangent.Normalize();
        return new Vector2(-tangent.Y, tangent.X);
    }

    private static float SampleLoopedProfile(IReadOnlyList<ProfilePoint> profile, float progress)
    {
        if (profile.Count == 0)
        {
            return 0f;
        }

        progress = Wrap01(progress);
        for (int i = 0; i < profile.Count; i++)
        {
            ProfilePoint current = profile[i];
            ProfilePoint next = profile[(i + 1) % profile.Count];
            float start = current.Progress;
            float end = next.Progress;
            float valueProgress = progress;
            if (i == profile.Count - 1)
            {
                end += 1f;
                if (valueProgress < start)
                {
                    valueProgress += 1f;
                }
            }

            if (valueProgress < start || valueProgress > end)
            {
                continue;
            }

            float t = MathHelper.Clamp((valueProgress - start) / MathF.Max(0.001f, end - start), 0f, 1f);
            t = t * t * (3f - 2f * t);
            return MathHelper.Lerp(current.Value, next.Value, t);
        }

        return profile[^1].Value;
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float t = MathHelper.Clamp((value - edge0) / MathF.Max(0.001f, edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float Wrap01(float value)
    {
        value %= 1f;
        return value < 0f ? value + 1f : value;
    }

    private readonly record struct SegmentProjection(float Distance, float T, Vector2 Point);

    private readonly record struct ProfilePoint(float Progress, float Value);

    private readonly record struct TrackWidthSample(
        float LeftRoadWidthMeters,
        float RightRoadWidthMeters,
        float LeftGrassWidthMeters,
        float RightGrassWidthMeters,
        float LeftWallOffsetMeters,
        float RightWallOffsetMeters)
    {
        public static TrackWidthSample FromSymmetric(float roadWidthMeters, float grassWidthMeters, float wallOffsetMeters)
        {
            float halfRoad = Math.Clamp(roadWidthMeters, 4f, 84f) * 0.5f;
            float grass = Math.Clamp(grassWidthMeters, 0f, 90f);
            float wall = Math.Clamp(wallOffsetMeters, 0.5f, 95f);
            return new TrackWidthSample(halfRoad, halfRoad, grass, grass, wall, wall);
        }

        public static TrackWidthSample Lerp(TrackWidthSample a, TrackWidthSample b, float t)
        {
            return new TrackWidthSample(
                MathHelper.Lerp(a.LeftRoadWidthMeters, b.LeftRoadWidthMeters, t),
                MathHelper.Lerp(a.RightRoadWidthMeters, b.RightRoadWidthMeters, t),
                MathHelper.Lerp(a.LeftGrassWidthMeters, b.LeftGrassWidthMeters, t),
                MathHelper.Lerp(a.RightGrassWidthMeters, b.RightGrassWidthMeters, t),
                MathHelper.Lerp(a.LeftWallOffsetMeters, b.LeftWallOffsetMeters, t),
                MathHelper.Lerp(a.RightWallOffsetMeters, b.RightWallOffsetMeters, t));
        }

        public float RoadWidthForSignedDistance(float signedDistance)
        {
            return signedDistance >= 0f ? LeftRoadWidthMeters : RightRoadWidthMeters;
        }

        public float GrassWidthForSignedDistance(float signedDistance)
        {
            return signedDistance >= 0f ? LeftGrassWidthMeters : RightGrassWidthMeters;
        }

        public float WallOffsetForSignedDistance(float signedDistance)
        {
            return signedDistance >= 0f ? LeftWallOffsetMeters : RightWallOffsetMeters;
        }
    }

    private sealed record TrackOffsetProfiles(
        float[] LeftRoadEdge,
        float[] RightRoadEdge,
        float[] LeftCurbInner,
        float[] LeftCurbOuter,
        float[] RightCurbInner,
        float[] RightCurbOuter,
        float[] LeftGrassInner,
        float[] LeftGrassOuter,
        float[] RightGrassInner,
        float[] RightGrassOuter,
        float[] LeftWall,
        float[] RightWall);

    private readonly record struct CenterLineProjection(
        float Distance,
        float SignedDistance,
        float DistanceAlongTrackMeters,
        float Elevation,
        Vector2 ClosestPoint,
        Vector2 LeftNormal,
        Vector2 Tangent,
        int SegmentIndex,
        float SegmentT);
}
