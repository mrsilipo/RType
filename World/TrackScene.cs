using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RType.Rendering;

namespace RType.World;

public sealed class TrackScene : ITrackSurfaceSampler, ITrackProgressSampler, IDisposable
{
    private const float WallOffsetFromRoadEdgeMeters = 1.45f;
    private const float WallCollisionShoulderMeters = 0.0f;
    private const float WallHeightMeters = 1.15f;
    private const float DefaultGrassWidthMeters = 26.0f;
    private const float CurbInnerOffsetFromRoadEdgeMeters = 0.20f;
    private const float CurbOuterOffsetFromRoadEdgeMeters = 1.10f;
    private const float CurbGrassBlendZoneMeters = 0.75f;
    private const float FieldTreeScale = 2.25f;
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
    private readonly AuthoredTrackVisualStats? _authoredVisualStats;
    private readonly AuthoredTrackSurfaceSampler? _authoredSurfaceSampler;

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
        TrackVisualSource visualSource,
        AuthoredTrackVisualStats? authoredVisualStats,
        AuthoredTrackSurfaceSampler? authoredSurfaceSampler,
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
        VisualSource = visualSource;
        _authoredVisualStats = authoredVisualStats;
        _authoredSurfaceSampler = authoredSurfaceSampler;
        StartPosition = startPosition;
        StartHeadingRadians = startHeadingRadians;
        IsReverse = isReverse;
    }

    public TrackDefinition Definition => _definition;

    public IReadOnlyList<StaticMesh> Meshes => _meshes;

    public TrackVisualSource VisualSource { get; }

    public AuthoredTrackVisualStats? AuthoredVisualStats => _authoredVisualStats;

    public AuthoredTrackSurfaceSampler? AuthoredSurfaceSampler => _authoredSurfaceSampler;

    public bool HasAuthoredSurfaceContact => _authoredSurfaceSampler?.HasDriveableSurface == true;

    public bool DrawProceduralBackdrop => VisualSource == TrackVisualSource.Generated;

    public Vector3 StartPosition { get; }

    public float StartHeadingRadians { get; }

    public Vector3 StartForward => new(MathF.Sin(StartHeadingRadians), 0f, MathF.Cos(StartHeadingRadians));

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

    public static TrackStartMetrics MeasureStart(TrackDefinition definition)
    {
        Vector3[] centerLine = BuildCenterLine(definition);
        float[] cumulativeDistances = CalculateCumulativeDistancesXZ(centerLine);
        int startIndex = GetStartIndex(definition.Layout, centerLine);
        return new TrackStartMetrics(
            centerLine[startIndex],
            GetTangent(centerLine, startIndex),
            cumulativeDistances[startIndex]);
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
        SurfaceLibrary surfaceLibrary,
        TrackVisualMode visualMode = TrackVisualMode.Auto)
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

        TrackVisualSource visualSource = ResolveVisualSource(definition, visualMode);
        AuthoredTrackVisualStats? authoredVisualStats = null;
        AuthoredTrackSurfaceSampler? authoredSurfaceSampler = null;
        List<StaticMesh> meshes;
        if (visualSource == TrackVisualSource.Authored)
        {
            string authoredVisualPath = GetAuthoredVisualPath(definition);
            AuthoredTrackVisualLoadResult authored = AuthoredTrackGltfLoader.Load(graphicsDevice, authoredVisualPath);
            meshes = authored.Meshes.ToList();
            authoredVisualStats = authored.Stats;
            authoredSurfaceSampler = new AuthoredTrackSurfaceSampler(authored.DriveableTriangles);
            if (authoredSurfaceSampler.HasDriveableSurface)
            {
                AuthoredTrackSurfaceStats surfaceStats = authoredSurfaceSampler.Stats;
                Console.WriteLine(
                    $"{definition.DisplayName} authored driveable surface: nodes {surfaceStats.DriveableNodeCount}, " +
                    $"triangles {surfaceStats.TriangleCount}, grid {surfaceStats.GridWidth}x{surfaceStats.GridDepth}, " +
                    $"cell {surfaceStats.CellSizeMeters:0.#}m, occupied cells {surfaceStats.OccupiedCellCount}, " +
                    $"avg candidates {surfaceStats.AverageTrianglesPerOccupiedCell:0.##}, worst {surfaceStats.WorstCaseTrianglesInCell}, " +
                    $"bounds min {FormatVector(surfaceStats.Bounds.Min)}, max {FormatVector(surfaceStats.Bounds.Max)}.");
            }
            else
            {
                Console.WriteLine(
                    $"{definition.DisplayName} authored driveable surface: no nodes ending exactly `_Driveable` were found. " +
                    "Vehicle ground contact will report misses and use the temporary procedural fallback until driveable meshes are authored.");
            }
        }
        else
        {
            Console.WriteLine($"{definition.DisplayName} visual source: Generated");
            meshes = BuildTrackMeshes(graphicsDevice, textures, definition, centerLine, bankRadians, widthSamples, roadHalfWidth);
        }

        Vector3 startPosition = new(start.X, start.Y, start.Z);
        float startHeadingRadians = MathF.Atan2(tangent.X, tangent.Y);
        if (visualSource == TrackVisualSource.Authored &&
            authoredVisualStats?.StartMarkers.Count > 0)
        {
            AuthoredTrackStartMarker authoredStart = authoredVisualStats.StartMarkers
                .FirstOrDefault(marker => marker.GridIndex == 6)
                ?? authoredVisualStats.StartMarkers.OrderBy(marker => marker.GridIndex).First();
            startPosition = authoredStart.Position;
            float authoredHeadingRadians = MathHelper.WrapAngle(authoredStart.HeadingRadians + MathF.PI);
            startHeadingRadians = reverse
                ? MathHelper.WrapAngle(authoredHeadingRadians + MathF.PI)
                : authoredHeadingRadians;
            Console.WriteLine(
                $"{definition.DisplayName} authored start marker: {authoredStart.Name} " +
                $"front-center at {FormatVector(startPosition)} heading {MathHelper.ToDegrees(startHeadingRadians):0.#}deg.");
        }

        ValidateVisualBounds(definition, visualSource, meshes, startPosition, authoredVisualStats);
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
            visualSource,
            authoredVisualStats,
            authoredSurfaceSampler,
            startPosition,
            startHeadingRadians,
            reverse);
    }

    public Vector3 ResolveVehicleStartPosition(float bodyLengthMeters)
    {
        if (VisualSource != TrackVisualSource.Authored)
        {
            return StartPosition;
        }

        float frontToCenterMeters = MathF.Max(0f, bodyLengthMeters) * 0.5f;
        return StartPosition - StartForward * frontToCenterMeters;
    }

    private static TrackVisualSource ResolveVisualSource(TrackDefinition definition, TrackVisualMode visualMode)
    {
        return visualMode switch
        {
            TrackVisualMode.Generated => TrackVisualSource.Generated,
            TrackVisualMode.Authored => TrackVisualSource.Authored,
            _ when IsAuthoredVisualTrack(definition) => TrackVisualSource.Authored,
            _ => TrackVisualSource.Generated
        };
    }

    private static bool IsAuthoredVisualTrack(TrackDefinition definition)
    {
        return definition.Id.Equals("high_speed_ring", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetAuthoredVisualPath(TrackDefinition definition)
    {
        if (!definition.Id.Equals("high_speed_ring", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"No authored visual package is configured for track `{definition.Id}`.");
        }

        return Path.Combine("Assets", "Tracks", "HighSpeedRing", "Runtime", "HighSpeedRing_Authored.glb");
    }

    private static void ValidateVisualBounds(
        TrackDefinition definition,
        TrackVisualSource visualSource,
        IReadOnlyList<StaticMesh> meshes,
        Vector3 startPosition,
        AuthoredTrackVisualStats? authoredVisualStats)
    {
        if (meshes.Count == 0)
        {
            throw new InvalidOperationException($"{definition.DisplayName} {visualSource} visual scene contains no meshes.");
        }

        BoundingBox bounds = authoredVisualStats?.Bounds ?? CalculateMeshBounds(meshes);
        bool startInsideXZ =
            startPosition.X >= bounds.Min.X - 25f &&
            startPosition.X <= bounds.Max.X + 25f &&
            startPosition.Z >= bounds.Min.Z - 25f &&
            startPosition.Z <= bounds.Max.Z + 25f;
        bool plausibleHeight = bounds.Max.Y - bounds.Min.Y < 250f && bounds.Max.Y > -25f && bounds.Min.Y < 75f;
        if (!startInsideXZ || !plausibleHeight)
        {
            throw new InvalidOperationException(
                $"{definition.DisplayName} {visualSource} visual bounds failed plausibility check. " +
                $"Start {FormatVector(startPosition)}, bounds min {FormatVector(bounds.Min)}, max {FormatVector(bounds.Max)}.");
        }

        Console.WriteLine(
            $"{definition.DisplayName} visual bounds validated: start {FormatVector(startPosition)}, " +
            $"bounds min {FormatVector(bounds.Min)}, max {FormatVector(bounds.Max)}.");
    }

    private static BoundingBox CalculateMeshBounds(IReadOnlyList<StaticMesh> meshes)
    {
        BoundingBox bounds = meshes[0].Bounds;
        for (int i = 1; i < meshes.Count; i++)
        {
            bounds = BoundingBox.CreateMerged(bounds, meshes[i].Bounds);
        }

        return bounds;
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.X:0.###}, {value.Y:0.###}, {value.Z:0.###})";
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

        List<StaticMesh> meshes =
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
        AddRaceCircuitFurniture(meshes, graphicsDevice, textures, definition, centerLine, offsets);
        AddRollingBackgroundTerrain(meshes, graphicsDevice, textures, terrainCenter, terrainWidth, terrainDepth);
        AddPatchworkFieldParcels(meshes, graphicsDevice, textures, terrainCenter, terrainWidth, terrainDepth);
        AddChalkDownlandFieldBands(meshes, graphicsDevice, textures, terrainCenter, terrainWidth, terrainDepth);
        AddMidFieldHedgerows(meshes, graphicsDevice, textures, terrainCenter, terrainWidth, terrainDepth);
        AddSalisburyPlainLandmarks(meshes, graphicsDevice, textures, terrainCenter, terrainWidth, terrainDepth);
        AddProceduralTreeClumps(meshes, graphicsDevice, textures, terrainCenter, terrainWidth, terrainDepth);
        return meshes;
    }

    private static void AddRaceCircuitFurniture(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        TrackDefinition definition,
        IReadOnlyList<Vector3> centerLine,
        TrackOffsetProfiles offsets)
    {
        AddCornerChevronSigns(meshes, graphicsDevice, textures, centerLine, offsets);
        AddCornerApproachFurniture(meshes, graphicsDevice, textures, centerLine, offsets);
        AddTireStackSafetyFurniture(meshes, graphicsDevice, textures, centerLine, offsets);
        AddStartLineGrandstands(meshes, graphicsDevice, textures, definition, centerLine, offsets);
        AddStartAreaAirfield(meshes, graphicsDevice, textures, definition, centerLine, offsets);
        AddTracksideRailwayAndUnderpass(meshes, graphicsDevice, textures, definition, centerLine, offsets);
    }

    private static void AddCornerChevronSigns(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        IReadOnlyList<Vector3> centerLine,
        TrackOffsetProfiles offsets)
    {
        int signIndex = 0;
        int step = Math.Max(1, centerLine.Count / 42);
        for (int i = 0; i < centerLine.Count; i += step)
        {
            int previousIndex = (i - step + centerLine.Count) % centerLine.Count;
            int nextIndex = (i + step) % centerLine.Count;
            Vector2 previous = ToXZ(centerLine[previousIndex]);
            Vector2 current = ToXZ(centerLine[i]);
            Vector2 next = ToXZ(centerLine[nextIndex]);
            Vector2 incoming = Vector2.Normalize(current - previous);
            Vector2 outgoing = Vector2.Normalize(next - current);
            float curvature = incoming.X * outgoing.Y - incoming.Y * outgoing.X;
            if (MathF.Abs(curvature) < 0.070f)
            {
                continue;
            }

            Vector2 left = GetTrackLeftNormal(centerLine, i);
            float sideOffset = curvature > 0f
                ? SampleOffsetValue(offsets.RightWall, i) - 1.45f
                : SampleOffsetValue(offsets.LeftWall, i) + 1.45f;
            Vector3 ground = OffsetTrackPoint(centerLine[i], left * sideOffset, 0.10f);
            Vector2 tangent = Vector2.Normalize(outgoing + incoming);
            float yaw = MathF.Atan2(tangent.Y, tangent.X);
            Vector3 signCenter = ground + Vector3.Up * 1.25f;
            meshes.Add(MeshFactory.CreateVerticalPlane(
                graphicsDevice,
                signCenter,
                3.4f,
                1.05f,
                yaw,
                textures.ChevronSign,
                Vector3.One,
                $"Japanese circuit chevron board {signIndex:00}",
                uvRepeatX: 1f,
                uvRepeatY: 1f));

            meshes.Add(MeshFactory.CreateBox(
                graphicsDevice,
                ground + Vector3.Up * 0.34f,
                new Vector3(4.2f, 0.68f, 0.48f),
                textures.White,
                new Vector3(0.48f, 0.50f, 0.49f),
                $"angular concrete chevron barrier {signIndex:00}"));
            signIndex++;
        }
    }

    private static void AddCornerApproachFurniture(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        IReadOnlyList<Vector3> centerLine,
        TrackOffsetProfiles offsets)
    {
        int markerIndex = 0;
        int step = Math.Max(1, centerLine.Count / 18);
        int preview = Math.Max(2, centerLine.Count / 74);
        for (int i = 0; i < centerLine.Count && markerIndex < 8; i += step)
        {
            int previousIndex = (i - preview + centerLine.Count) % centerLine.Count;
            int nextIndex = (i + preview) % centerLine.Count;
            Vector2 previous = ToXZ(centerLine[previousIndex]);
            Vector2 current = ToXZ(centerLine[i]);
            Vector2 next = ToXZ(centerLine[nextIndex]);
            Vector2 incoming = SafeNormalize(current - previous, Vector2.UnitX);
            Vector2 outgoing = SafeNormalize(next - current, incoming);
            float curvature = incoming.X * outgoing.Y - incoming.Y * outgoing.X;
            if (MathF.Abs(curvature) < 0.045f)
            {
                continue;
            }

            Vector2 left = GetTrackLeftNormal(centerLine, i);
            Vector2 forward = new(left.Y, -left.X);
            float wallOffset = curvature > 0f
                ? SampleOffsetValue(offsets.RightWall, i) - 4.2f
                : SampleOffsetValue(offsets.LeftWall, i) + 4.2f;
            float yaw = MathF.Atan2(left.Y, left.X);
            for (int board = 0; board < 3; board++)
            {
                Vector2 approachOffset = left * wallOffset - forward * (18f + board * 16f);
                Vector3 ground = OffsetTrackPoint(centerLine[i], approachOffset, 0.12f);
                meshes.Add(MeshFactory.CreateVerticalPlane(
                    graphicsDevice,
                    ground + Vector3.Up * 1.05f,
                    1.10f,
                    1.65f,
                    yaw,
                    textures.BrakeMarkerSign,
                    Vector3.One,
                    $"corner approach brake marker {markerIndex:00}-{board:00}",
                    uvRepeatX: 1f,
                    uvRepeatY: 1f));
                meshes.Add(MeshFactory.CreateBox(
                    graphicsDevice,
                    ground + Vector3.Up * 0.52f,
                    new Vector3(0.14f, 1.04f, 0.14f),
                    textures.White,
                    new Vector3(0.12f, 0.13f, 0.12f),
                    $"corner approach brake marker post {markerIndex:00}-{board:00}"));
            }

            if ((markerIndex & 1) == 0)
            {
                AddMarshalPost(meshes, graphicsDevice, textures, centerLine[i], left, wallOffset + MathF.CopySign(3.6f, wallOffset), markerIndex);
            }

            markerIndex++;
        }
    }

    private static void AddMarshalPost(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        Vector3 trackPoint,
        Vector2 left,
        float offset,
        int index)
    {
        Vector3 center = OffsetTrackPoint(trackPoint, left * offset, 0.18f);
        Vector3 timber = new(0.34f, 0.25f, 0.18f);
        Vector3 roof = new(0.09f, 0.095f, 0.09f);
        Vector3 hiVis = new(0.93f, 0.62f, 0.08f);
        Vector3 flagRed = new(0.78f, 0.04f, 0.04f);

        meshes.Add(MeshFactory.CreateBox(graphicsDevice, center + Vector3.Up * 0.72f, new Vector3(2.3f, 1.10f, 1.7f), textures.White, timber, $"corner marshal timber shelter {index:00}"));
        meshes.Add(MeshFactory.CreateBox(graphicsDevice, center + Vector3.Up * 1.38f, new Vector3(2.7f, 0.22f, 2.0f), textures.White, roof, $"corner marshal shelter roof {index:00}"));
        meshes.Add(MeshFactory.CreateBox(graphicsDevice, center + new Vector3(0.96f, 0.85f, -0.36f), new Vector3(0.22f, 0.72f, 0.16f), textures.White, hiVis, $"corner marshal figure {index:00}"));
        meshes.Add(MeshFactory.CreateVerticalPlane(
            graphicsDevice,
            center + new Vector3(0.06f, 1.55f, 1.16f),
            0.95f,
            0.62f,
            0f,
            textures.White,
            flagRed,
            $"corner marshal red flag {index:00}"));
    }

    private static void AddTireStackSafetyFurniture(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        IReadOnlyList<Vector3> centerLine,
        TrackOffsetProfiles offsets)
    {
        int stackIndex = 0;
        int step = Math.Max(1, centerLine.Count / 16);
        int preview = Math.Max(2, centerLine.Count / 64);
        for (int i = 0; i < centerLine.Count && stackIndex < 7; i += step)
        {
            int previousIndex = (i - preview + centerLine.Count) % centerLine.Count;
            int nextIndex = (i + preview) % centerLine.Count;
            Vector2 previous = ToXZ(centerLine[previousIndex]);
            Vector2 current = ToXZ(centerLine[i]);
            Vector2 next = ToXZ(centerLine[nextIndex]);
            Vector2 incoming = SafeNormalize(current - previous, Vector2.UnitX);
            Vector2 outgoing = SafeNormalize(next - current, incoming);
            float curvature = incoming.X * outgoing.Y - incoming.Y * outgoing.X;
            if (MathF.Abs(curvature) < 0.038f)
            {
                continue;
            }

            Vector2 left = GetTrackLeftNormal(centerLine, i);
            Vector2 forward = new(left.Y, -left.X);
            float offset = curvature > 0f
                ? SampleOffsetValue(offsets.RightWall, i) - 7.6f
                : SampleOffsetValue(offsets.LeftWall, i) + 7.6f;
            Vector3 anchor = OffsetTrackPoint(centerLine[i], left * offset - forward * 6.5f, 0.12f);
            AddTireStackBundle(meshes, graphicsDevice, textures, anchor, left, forward, stackIndex);
            AddRedWhiteBarrierBundle(meshes, graphicsDevice, textures, anchor + new Vector3(0f, 0f, 0f), left, forward, stackIndex);
            stackIndex++;
        }
    }

    private static void AddTireStackBundle(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        Vector3 anchor,
        Vector2 left,
        Vector2 forward,
        int index)
    {
        Vector3 tireTint = new(0.018f, 0.019f, 0.018f);
        Vector3 tireHighlight = new(0.045f, 0.047f, 0.044f);
        for (int column = 0; column < 3; column++)
        {
            for (int layer = 0; layer < 3; layer++)
            {
                Vector2 local = left * ((column - 1) * 0.72f) + forward * (column % 2 == 0 ? 0.0f : 0.42f);
                Vector3 center = OffsetTrackPoint(anchor, local, 0.30f + layer * 0.36f);
                Vector3 tint = layer == 2 ? tireHighlight : tireTint;
                meshes.Add(MeshFactory.CreateCylinderY(
                    graphicsDevice,
                    center,
                    0.42f,
                    0.28f,
                    12,
                    textures.Tire,
                    tint,
                    $"corner safety tire stack {index:00}-{column:00}-{layer:00}"));
            }
        }
    }

    private static void AddRedWhiteBarrierBundle(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        Vector3 anchor,
        Vector2 left,
        Vector2 forward,
        int index)
    {
        Vector3 red = new(0.70f, 0.05f, 0.045f);
        Vector3 white = new(0.86f, 0.84f, 0.74f);
        Vector3 baseCenter = OffsetTrackPoint(anchor, -forward * 1.35f, 0.36f);
        for (int block = 0; block < 4; block++)
        {
            Vector2 offset = left * ((block - 1.5f) * 1.18f);
            meshes.Add(MeshFactory.CreateBox(
                graphicsDevice,
                OffsetTrackPoint(baseCenter, offset, 0f),
                new Vector3(1.04f, 0.72f, 0.42f),
                textures.White,
                (block & 1) == 0 ? red : white,
                $"corner red white barrier bundle {index:00}-{block:00}"));
        }
    }

    private static void AddStartLineGrandstands(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        TrackDefinition definition,
        IReadOnlyList<Vector3> centerLine,
        TrackOffsetProfiles offsets)
    {
        int startIndex = GetStartIndex(definition.Layout, centerLine);
        Vector3 start = centerLine[startIndex];
        Vector2 left = GetTrackLeftNormal(centerLine, startIndex);
        Vector2 forward = new(left.Y, -left.X);
        AddStartLineRoadMarkings(meshes, graphicsDevice, textures, start, left, offsets, startIndex);
        AddGrandstand(meshes, graphicsDevice, textures, start, left, SampleOffsetValue(offsets.LeftWall, startIndex) + 16f, "left");
        AddGrandstand(meshes, graphicsDevice, textures, start, left, SampleOffsetValue(offsets.RightWall, startIndex) - 16f, "right");
        AddSpectatorTerrace(meshes, graphicsDevice, textures, start, left, forward, SampleOffsetValue(offsets.LeftWall, startIndex) + 24f, 28f, "left forward");
        AddSpectatorTerrace(meshes, graphicsDevice, textures, start, left, forward, SampleOffsetValue(offsets.LeftWall, startIndex) + 22f, -30f, "left rear");
        AddSpectatorTerrace(meshes, graphicsDevice, textures, start, left, forward, SampleOffsetValue(offsets.LeftWall, startIndex) + 38f, 58f, "left outer forward");
        AddSpectatorTerrace(meshes, graphicsDevice, textures, start, left, forward, SampleOffsetValue(offsets.RightWall, startIndex) - 23f, 24f, "right forward");
        AddSpectatorTerrace(meshes, graphicsDevice, textures, start, left, forward, SampleOffsetValue(offsets.RightWall, startIndex) - 23f, -30f, "right rear");
        AddSpectatorTerrace(meshes, graphicsDevice, textures, start, left, forward, SampleOffsetValue(offsets.RightWall, startIndex) - 39f, 56f, "right outer forward");
        AddStartFinishGantry(meshes, graphicsDevice, textures, start, left, offsets, startIndex);
        AddRaceControlHut(meshes, graphicsDevice, textures, start, left, SampleOffsetValue(offsets.RightWall, startIndex) - 23f);
        AddPaddockServiceArea(meshes, graphicsDevice, textures, start, left, SampleOffsetValue(offsets.RightWall, startIndex) - 34f, "right");
        AddPaddockServiceArea(meshes, graphicsDevice, textures, start, left, SampleOffsetValue(offsets.LeftWall, startIndex) + 30f, "left");
    }

    private static void AddStartLineRoadMarkings(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        Vector3 trackPoint,
        Vector2 left,
        TrackOffsetProfiles offsets,
        int startIndex)
    {
        Vector2 forward = new(left.Y, -left.X);
        float leftRoad = SampleOffsetValue(offsets.LeftRoadEdge, startIndex);
        float rightRoad = SampleOffsetValue(offsets.RightRoadEdge, startIndex);
        float roadWidth = MathF.Max(3f, leftRoad - rightRoad);
        Vector3 white = new(0.92f, 0.90f, 0.78f);
        Vector3 yellow = new(0.88f, 0.68f, 0.10f);
        Vector3 dark = new(0.06f, 0.065f, 0.06f);
        float tileWidth = roadWidth / 12f;
        float tileDepth = 0.72f;
        Vector3 lineCenter = trackPoint + Vector3.Up * 0.032f;

        for (int row = 0; row < 2; row++)
        {
            for (int col = 0; col < 12; col++)
            {
                Vector2 lateralOffset = left * (rightRoad + tileWidth * (col + 0.5f));
                Vector2 forwardOffset = forward * ((row - 0.5f) * tileDepth);
                Vector3 center = OffsetTrackPoint(lineCenter, lateralOffset + forwardOffset, 0f);
                meshes.Add(MeshFactory.CreateGroundRectangle(
                    graphicsDevice,
                    center,
                    left,
                    tileWidth * 0.94f,
                    forward,
                    tileDepth * 0.92f,
                    textures.White,
                    ((row + col) & 1) == 0 ? white : dark,
                    $"start finish checker tile {row:00}-{col:00}"));
            }
        }

        for (int slot = 0; slot < 8; slot++)
        {
            float rowOffset = 8f + slot * 7.2f;
            float side = slot % 2 == 0 ? -0.26f : 0.26f;
            Vector2 slotCenterOffset = forward * rowOffset + left * (side * roadWidth);
            Vector3 center = OffsetTrackPoint(lineCenter, slotCenterOffset, 0.003f);
            meshes.Add(MeshFactory.CreateGroundRectangle(
                graphicsDevice,
                center,
                left,
                roadWidth * 0.34f,
                forward,
                0.28f,
                textures.White,
                white,
                $"start grid box crossbar {slot:00}"));
            meshes.Add(MeshFactory.CreateGroundRectangle(
                graphicsDevice,
                OffsetTrackPoint(center, left * (-roadWidth * 0.17f), 0.002f),
                left,
                0.24f,
                forward,
                4.8f,
                textures.White,
                white,
                $"start grid box inner rail {slot:00}"));
            meshes.Add(MeshFactory.CreateGroundRectangle(
                graphicsDevice,
                OffsetTrackPoint(center, left * (roadWidth * 0.17f), 0.002f),
                left,
                0.24f,
                forward,
                4.8f,
                textures.White,
                white,
                $"start grid box outer rail {slot:00}"));
            meshes.Add(MeshFactory.CreateGroundRectangle(
                graphicsDevice,
                OffsetTrackPoint(center, forward * 2.65f, 0.002f),
                left,
                roadWidth * 0.20f,
                forward,
                0.22f,
                textures.White,
                yellow,
                $"start grid yellow nose marker {slot:00}"));
        }
    }

    private static void AddGrandstand(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        Vector3 trackPoint,
        Vector2 left,
        float offset,
        string side)
    {
        Vector3 baseCenter = OffsetTrackPoint(trackPoint, left * offset, 0.15f);
        Vector3 concrete = new(0.54f, 0.55f, 0.52f);
        Vector3 seatRed = new(0.64f, 0.10f, 0.09f);
        Vector3 seatWhite = new(0.86f, 0.84f, 0.76f);
        Vector3 seatBlue = new(0.08f, 0.18f, 0.42f);
        Vector3 crowdDark = new(0.12f, 0.11f, 0.09f);
        Vector3 crowdYellow = new(0.84f, 0.62f, 0.14f);
        Vector3 dark = new(0.12f, 0.13f, 0.13f);

        meshes.Add(MeshFactory.CreateBox(
            graphicsDevice,
            baseCenter + Vector3.Up * 0.32f,
            new Vector3(17.8f, 0.64f, 5.4f),
            textures.White,
            concrete,
            $"start finish {side} grandstand concrete base"));

        for (int row = 0; row < 7; row++)
        {
            Vector3 rowCenter = baseCenter + new Vector3(0f, 0.78f + row * 0.33f, -2.30f + row * 0.64f);
            Vector3 seatTint = row % 3 == 0 ? seatRed : row % 3 == 1 ? seatWhite : seatBlue;
            meshes.Add(MeshFactory.CreateBox(
                graphicsDevice,
                rowCenter,
                new Vector3(16.7f, 0.22f, 0.42f),
                textures.White,
                seatTint,
                $"start finish {side} grandstand seat row {row:00}"));

            if (row > 0)
            {
                for (int person = 0; person < 8; person++)
                {
                    float x = -7.25f + person * 2.05f + ((row + person) % 2) * 0.22f;
                    Vector3 crowdTint = (row + person) % 3 == 0 ? crowdYellow : crowdDark;
                    meshes.Add(MeshFactory.CreateBox(
                        graphicsDevice,
                        rowCenter + new Vector3(x, 0.28f, -0.02f),
                        new Vector3(0.42f, 0.42f, 0.28f),
                        textures.White,
                        crowdTint,
                        $"start finish {side} grandstand crowd block {row:00}-{person:00}"));
                }
            }
        }

        meshes.Add(MeshFactory.CreateBox(
            graphicsDevice,
            baseCenter + new Vector3(-9.25f, 1.28f, 0.0f),
            new Vector3(0.24f, 2.55f, 5.8f),
            textures.White,
            dark,
            $"start finish {side} grandstand left frame"));
        meshes.Add(MeshFactory.CreateBox(
            graphicsDevice,
            baseCenter + new Vector3(9.25f, 1.28f, 0.0f),
            new Vector3(0.24f, 2.55f, 5.8f),
            textures.White,
            dark,
            $"start finish {side} grandstand right frame"));
    }

    private static void AddSpectatorTerrace(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        Vector3 trackPoint,
        Vector2 left,
        Vector2 forward,
        float offset,
        float forwardOffset,
        string label)
    {
        Vector3 baseCenter = OffsetTrackPoint(trackPoint, left * offset + forward * forwardOffset, 0.13f);
        Vector3 concrete = new(0.48f, 0.49f, 0.46f);
        Vector3 rail = new(0.10f, 0.11f, 0.10f);
        Vector3 red = new(0.62f, 0.08f, 0.07f);
        Vector3 white = new(0.83f, 0.82f, 0.74f);
        Vector3 yellow = new(0.82f, 0.58f, 0.11f);

        meshes.Add(MeshFactory.CreateBox(
            graphicsDevice,
            baseCenter + Vector3.Up * 0.22f,
            new Vector3(11.0f, 0.44f, 3.2f),
            textures.White,
            concrete,
            $"start spectator terrace {label} base"));

        for (int row = 0; row < 4; row++)
        {
            Vector3 rowCenter = baseCenter + new Vector3(0f, 0.58f + row * 0.28f, -1.18f + row * 0.54f);
            meshes.Add(MeshFactory.CreateBox(
                graphicsDevice,
                rowCenter,
                new Vector3(10.2f, 0.18f, 0.34f),
                textures.White,
                row % 2 == 0 ? red : white,
                $"start spectator terrace {label} seat row {row:00}"));

            for (int person = 0; person < 5; person++)
            {
                float x = -4.0f + person * 2.0f;
                meshes.Add(MeshFactory.CreateBox(
                    graphicsDevice,
                    rowCenter + new Vector3(x, 0.23f, 0.0f),
                    new Vector3(0.34f, 0.34f, 0.24f),
                    textures.White,
                    (row + person) % 2 == 0 ? yellow : rail,
                    $"start spectator terrace {label} crowd block {row:00}-{person:00}"));
            }
        }

        meshes.Add(MeshFactory.CreateBox(
            graphicsDevice,
            baseCenter + new Vector3(0f, 1.24f, 1.52f),
            new Vector3(11.4f, 0.10f, 0.12f),
            textures.White,
            rail,
            $"start spectator terrace {label} back rail"));
    }

    private static void AddStartFinishGantry(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        Vector3 trackPoint,
        Vector2 left,
        TrackOffsetProfiles offsets,
        int startIndex)
    {
        float leftWall = SampleOffsetValue(offsets.LeftWall, startIndex);
        float rightWall = SampleOffsetValue(offsets.RightWall, startIndex);
        Vector3 leftBase = OffsetTrackPoint(trackPoint, left * (leftWall + 2.2f), 0.12f);
        Vector3 rightBase = OffsetTrackPoint(trackPoint, left * (rightWall - 2.2f), 0.12f);
        Vector3 gantryCenter = Vector3.Lerp(leftBase, rightBase, 0.5f);
        float span = Vector2.Distance(left * (leftWall + 2.2f), left * (rightWall - 2.2f));
        float boardYaw = MathF.Atan2(left.Y, left.X);

        Vector3 black = new(0.05f, 0.055f, 0.052f);
        Vector3 steel = new(0.48f, 0.50f, 0.48f);
        Vector3 lampRed = new(0.80f, 0.03f, 0.025f);
        Vector3 lampAmber = new(0.92f, 0.58f, 0.06f);
        Vector3 lampGreen = new(0.04f, 0.78f, 0.20f);

        meshes.Add(MeshFactory.CreateBox(graphicsDevice, leftBase + Vector3.Up * 3.55f, new Vector3(0.45f, 7.1f, 0.45f), textures.White, steel, "start finish gantry left upright"));
        meshes.Add(MeshFactory.CreateBox(graphicsDevice, rightBase + Vector3.Up * 3.55f, new Vector3(0.45f, 7.1f, 0.45f), textures.White, steel, "start finish gantry right upright"));
        meshes.Add(MeshFactory.CreateBox(graphicsDevice, gantryCenter + Vector3.Up * 7.10f, new Vector3(0.62f, 0.42f, span), textures.White, black, "start finish gantry top beam"));
        meshes.Add(MeshFactory.CreateVerticalPlane(
            graphicsDevice,
            gantryCenter + Vector3.Up * 6.82f,
            MathF.Max(8f, span - 2.6f),
            1.05f,
            boardYaw,
            textures.StartBoard,
            Vector3.One,
            "start finish high speed circuit board",
            uvRepeatX: 1f,
            uvRepeatY: 1f));

        for (int i = 0; i < 5; i++)
        {
            float offset = -2.4f + i * 1.2f;
            Vector3 tint = i switch
            {
                0 or 1 => lampRed,
                2 => lampAmber,
                _ => lampGreen
            };
            meshes.Add(MeshFactory.CreateBox(
                graphicsDevice,
                gantryCenter + new Vector3(0.40f, 6.10f, offset),
                new Vector3(0.16f, 0.30f, 0.30f),
                textures.White,
                tint,
                $"start finish signal lamp {i:00}"));
        }
    }

    private static void AddRaceControlHut(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        Vector3 trackPoint,
        Vector2 left,
        float offset)
    {
        Vector3 baseCenter = OffsetTrackPoint(trackPoint, left * offset, 0.18f) + new Vector3(10.5f, 0f, 0f);
        Vector3 concrete = new(0.50f, 0.51f, 0.48f);
        Vector3 dark = new(0.08f, 0.085f, 0.08f);
        Vector3 glass = new(0.14f, 0.22f, 0.22f);

        meshes.Add(MeshFactory.CreateBox(graphicsDevice, baseCenter + Vector3.Up * 1.25f, new Vector3(5.2f, 2.5f, 3.6f), textures.White, concrete, "start finish race control hut"));
        meshes.Add(MeshFactory.CreateBox(graphicsDevice, baseCenter + new Vector3(0f, 2.62f, 0f), new Vector3(5.9f, 0.34f, 4.1f), textures.White, dark, "start finish race control flat roof"));
        meshes.Add(MeshFactory.CreateBox(graphicsDevice, baseCenter + new Vector3(-2.64f, 1.55f, 0f), new Vector3(0.08f, 0.85f, 2.4f), textures.White, glass, "start finish race control glass strip"));
        meshes.Add(MeshFactory.CreateBox(graphicsDevice, baseCenter + new Vector3(0.2f, 0.12f, -2.3f), new Vector3(6.2f, 0.24f, 0.34f), textures.White, new Vector3(0.54f, 0.54f, 0.49f), "start finish race control service curb"));
    }

    private static void AddPaddockServiceArea(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        Vector3 trackPoint,
        Vector2 left,
        float offset,
        string side)
    {
        Vector2 forward = new(left.Y, -left.X);
        Vector3 origin = OffsetTrackPoint(trackPoint, left * offset - forward * 8f, 0.16f);
        float sideSign = MathF.Sign(offset);
        if (sideSign == 0f)
        {
            sideSign = 1f;
        }

        Vector3 concrete = new(0.46f, 0.47f, 0.44f);
        Vector3 dark = new(0.06f, 0.065f, 0.06f);
        Vector3 red = new(0.68f, 0.05f, 0.04f);
        Vector3 cream = new(0.83f, 0.80f, 0.66f);
        Vector3 blue = new(0.08f, 0.18f, 0.34f);
        Vector3 yellow = new(0.90f, 0.66f, 0.08f);

        for (int i = 0; i < 6; i++)
        {
            Vector3 bay = OffsetTrackPoint(origin, forward * (i * 5.6f), 0f);
            meshes.Add(MeshFactory.CreateBox(graphicsDevice, bay + Vector3.Up * 0.42f, new Vector3(0.38f, 0.84f, 3.2f), textures.White, concrete, $"start paddock {side} low pit wall block {i:00}"));
            meshes.Add(MeshFactory.CreateBox(graphicsDevice, bay + new Vector3(0f, 1.08f, 0f), new Vector3(0.26f, 0.44f, 2.2f), textures.White, dark, $"start paddock {side} timing screen frame {i:00}"));
            meshes.Add(MeshFactory.CreateBox(graphicsDevice, bay + new Vector3(-0.03f, 1.09f, 0f), new Vector3(0.08f, 0.26f, 1.68f), textures.White, i % 2 == 0 ? yellow : red, $"start paddock {side} timing screen panel {i:00}"));
        }

        for (int i = 0; i < 4; i++)
        {
            Vector2 local = forward * (5f + i * 8.6f) + left * (sideSign * 8.2f);
            Vector3 tent = OffsetTrackPoint(origin, local, 0f);
            Vector3 roofTint = i switch
            {
                0 => red,
                1 => cream,
                2 => blue,
                _ => new Vector3(0.18f, 0.40f, 0.22f)
            };
            meshes.Add(MeshFactory.CreateBox(graphicsDevice, tent + Vector3.Up * 0.96f, new Vector3(4.8f, 1.26f, 3.4f), textures.White, cream * 0.86f, $"start paddock {side} tent body {i:00}"));
            meshes.Add(MeshFactory.CreateBox(graphicsDevice, tent + Vector3.Up * 1.72f, new Vector3(5.4f, 0.42f, 4.0f), textures.White, roofTint, $"start paddock {side} canvas roof {i:00}"));
            meshes.Add(MeshFactory.CreateBox(graphicsDevice, tent + new Vector3(0f, 0.22f, -2.26f), new Vector3(5.8f, 0.16f, 0.26f), textures.White, dark, $"start paddock {side} tent shadow rail {i:00}"));
        }

        Vector3 truck = OffsetTrackPoint(origin, forward * 34f + left * (sideSign * 7.6f), 0f);
        meshes.Add(MeshFactory.CreateBox(graphicsDevice, truck + Vector3.Up * 0.72f, new Vector3(6.4f, 1.44f, 2.25f), textures.White, new Vector3(0.82f, 0.80f, 0.70f), $"start paddock {side} box truck cargo"));
        meshes.Add(MeshFactory.CreateBox(graphicsDevice, truck + new Vector3(3.75f, 0.55f, 0f), new Vector3(1.45f, 1.10f, 2.0f), textures.White, red, $"start paddock {side} box truck cab"));
        meshes.Add(MeshFactory.CreateBox(graphicsDevice, truck + new Vector3(-2.1f, 0.20f, -1.22f), new Vector3(0.92f, 0.40f, 0.16f), textures.White, dark, $"start paddock {side} box truck rear wheel shadow"));
        meshes.Add(MeshFactory.CreateBox(graphicsDevice, truck + new Vector3(3.45f, 0.20f, -1.22f), new Vector3(0.92f, 0.40f, 0.16f), textures.White, dark, $"start paddock {side} box truck front wheel shadow"));
    }

    private static void AddStartAreaAirfield(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        TrackDefinition definition,
        IReadOnlyList<Vector3> centerLine,
        TrackOffsetProfiles offsets)
    {
        int startIndex = GetStartIndex(definition.Layout, centerLine);
        Vector3 start = centerLine[startIndex];
        Vector2 left = GetTrackLeftNormal(centerLine, startIndex);
        Vector2 forward = new(left.Y, -left.X);
        float rightWall = SampleOffsetValue(offsets.RightWall, startIndex);
        Vector3 apronCenter = OffsetTrackPoint(start, left * (rightWall - 72f) + forward * 64f, 0.055f);
        Vector3 runwayTint = new(0.35f, 0.36f, 0.34f);
        Vector3 chalkEdge = new(0.74f, 0.72f, 0.55f);
        Vector3 hangarWall = new(0.56f, 0.58f, 0.54f);
        Vector3 hangarRoof = new(0.20f, 0.22f, 0.22f);
        Vector3 dark = new(0.055f, 0.060f, 0.056f);

        meshes.Add(MeshFactory.CreateGroundRectangle(
            graphicsDevice,
            apronCenter,
            left,
            28f,
            forward,
            62f,
            textures.Road,
            runwayTint,
            "start area old airfield apron",
            uvRepeatX: 2.5f,
            uvRepeatY: 4.5f));
        meshes.Add(MeshFactory.CreateGroundRectangle(
            graphicsDevice,
            OffsetTrackPoint(apronCenter, -left * 16.0f, 0.002f),
            left,
            2.0f,
            forward,
            66f,
            textures.White,
            chalkEdge,
            "start area chalk airfield apron edge"));

        for (int i = 0; i < 3; i++)
        {
            Vector3 hangar = OffsetTrackPoint(apronCenter, forward * (-21f + i * 18f) + left * 22f, 0.95f);
            meshes.Add(MeshFactory.CreateBox(
                graphicsDevice,
                hangar,
                new Vector3(9.4f, 1.9f, 6.2f),
                textures.White,
                hangarWall,
                $"start area airfield hangar body {i:00}"));
            meshes.Add(MeshFactory.CreateBox(
                graphicsDevice,
                hangar + Vector3.Up * 1.25f,
                new Vector3(10.4f, 0.54f, 7.2f),
                textures.White,
                hangarRoof,
                $"start area airfield hangar roof {i:00}"));
            meshes.Add(MeshFactory.CreateBox(
                graphicsDevice,
                hangar + new Vector3(-4.78f, -0.12f, 0f),
                new Vector3(0.14f, 1.30f, 4.8f),
                textures.White,
                dark,
                $"start area airfield hangar opening {i:00}"));
        }

        Vector3 hut = OffsetTrackPoint(apronCenter, -forward * 34f + left * 15f, 0.74f);
        meshes.Add(MeshFactory.CreateBox(
            graphicsDevice,
            hut,
            new Vector3(4.2f, 1.48f, 3.4f),
            textures.White,
            new Vector3(0.58f, 0.54f, 0.42f),
            "start area airfield control hut"));
        meshes.Add(MeshFactory.CreateBox(
            graphicsDevice,
            hut + Vector3.Up * 1.04f,
            new Vector3(4.8f, 0.36f, 4.0f),
            textures.White,
            hangarRoof * 0.95f,
            "start area airfield control hut roof"));
        meshes.Add(MeshFactory.CreateBox(
            graphicsDevice,
            hut + new Vector3(-2.12f, 0.18f, 0f),
            new Vector3(0.10f, 0.54f, 2.0f),
            textures.White,
            new Vector3(0.13f, 0.18f, 0.18f),
            "start area airfield control glass strip"));

        Vector3 windsockBase = OffsetTrackPoint(apronCenter, -forward * 18f - left * 16f, 0.0f);
        meshes.Add(MeshFactory.CreateBox(
            graphicsDevice,
            windsockBase + Vector3.Up * 2.0f,
            new Vector3(0.16f, 4.0f, 0.16f),
            textures.White,
            new Vector3(0.17f, 0.17f, 0.15f),
            "start area airfield windsock pole"));
        meshes.Add(MeshFactory.CreateVerticalPlane(
            graphicsDevice,
            windsockBase + Vector3.Up * 3.75f,
            2.1f,
            0.62f,
            MathF.Atan2(forward.Y, forward.X),
            textures.White,
            new Vector3(0.82f, 0.12f, 0.08f),
            "start area red windsock"));

        Vector3 plane = OffsetTrackPoint(apronCenter, forward * 19f - left * 5.2f, 0.42f);
        Vector3 planeBody = new(0.78f, 0.78f, 0.68f);
        Vector3 planeNose = new(0.10f, 0.16f, 0.22f);
        meshes.Add(MeshFactory.CreateBox(
            graphicsDevice,
            plane,
            new Vector3(5.6f, 0.52f, 0.82f),
            textures.White,
            planeBody,
            "start area parked light aircraft fuselage"));
        meshes.Add(MeshFactory.CreateBox(
            graphicsDevice,
            plane + new Vector3(-0.1f, 0.04f, 0f),
            new Vector3(0.70f, 0.12f, 7.6f),
            textures.White,
            planeBody * 0.94f,
            "start area parked light aircraft wing"));
        meshes.Add(MeshFactory.CreateBox(
            graphicsDevice,
            plane + new Vector3(2.65f, 0.04f, 0f),
            new Vector3(0.30f, 0.36f, 1.1f),
            textures.White,
            planeNose,
            "start area parked light aircraft nose"));
    }

    private static Vector2 GetTrackLeftNormal(IReadOnlyList<Vector3> centerLine, int index)
    {
        Vector2 previous = ToXZ(centerLine[(index - 1 + centerLine.Count) % centerLine.Count]);
        Vector2 next = ToXZ(centerLine[(index + 1) % centerLine.Count]);
        Vector2 tangent = next - previous;
        if (tangent.LengthSquared() <= 0.0001f)
        {
            return Vector2.UnitX;
        }

        tangent.Normalize();
        return new Vector2(-tangent.Y, tangent.X);
    }

    private static Vector2 SafeNormalize(Vector2 value, Vector2 fallback)
    {
        return value.LengthSquared() <= 0.0001f ? fallback : Vector2.Normalize(value);
    }

    private static Vector3 OffsetTrackPoint(Vector3 center, Vector2 offset, float yOffset)
    {
        return new Vector3(center.X + offset.X, center.Y + yOffset, center.Z + offset.Y);
    }

    private static float SampleOffsetValue(IReadOnlyList<float> offsets, int index)
    {
        return offsets.Count == 0 ? 0f : offsets[Math.Clamp(index, 0, offsets.Count - 1)];
    }

    private static void AddChalkDownlandFieldBands(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        Vector3 terrainCenter,
        float terrainWidth,
        float terrainDepth)
    {
        float halfWidth = terrainWidth * 0.5f;
        float halfDepth = terrainDepth * 0.5f;
        Vector3 paleChalkGrass = new(1.00f, 0.96f, 0.70f);
        Vector3 dryOlive = new(0.74f, 0.78f, 0.48f);
        float y = terrainCenter.Y + 0.052f;

        meshes.Add(MeshFactory.CreatePlane(
            graphicsDevice,
            terrainCenter + new Vector3(-halfWidth * 0.34f, y, -halfDepth * 0.30f),
            terrainWidth * 0.34f,
            terrainDepth * 0.055f,
            textures.Grass,
            6f,
            paleChalkGrass,
            "Salisbury chalk grass field band north west"));
        meshes.Add(MeshFactory.CreatePlane(
            graphicsDevice,
            terrainCenter + new Vector3(halfWidth * 0.30f, y, halfDepth * 0.32f),
            terrainWidth * 0.38f,
            terrainDepth * 0.050f,
            textures.Grass,
            6f,
            paleChalkGrass,
            "Salisbury chalk grass field band south east"));
        meshes.Add(MeshFactory.CreatePlane(
            graphicsDevice,
            terrainCenter + new Vector3(-halfWidth * 0.44f, y + 0.002f, halfDepth * 0.08f),
            terrainWidth * 0.13f,
            terrainDepth * 0.42f,
            textures.Grass,
            7f,
            dryOlive,
            "Wiltshire dry downland side field west"));
        meshes.Add(MeshFactory.CreatePlane(
            graphicsDevice,
            terrainCenter + new Vector3(halfWidth * 0.43f, y + 0.002f, -halfDepth * 0.04f),
            terrainWidth * 0.12f,
            terrainDepth * 0.38f,
            textures.Grass,
            7f,
            dryOlive,
            "Wiltshire dry downland side field east"));
    }

    private static void AddPatchworkFieldParcels(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        Vector3 terrainCenter,
        float terrainWidth,
        float terrainDepth)
    {
        float halfWidth = terrainWidth * 0.5f;
        float halfDepth = terrainDepth * 0.5f;
        AddFieldParcel(meshes, graphicsDevice, textures, terrainCenter, terrainWidth, terrainDepth, -0.54f, -0.40f, 0.22f, 0.14f, -8f, new Vector3(0.67f, 0.73f, 0.38f), "north west pale pasture parcel");
        AddFieldParcel(meshes, graphicsDevice, textures, terrainCenter, terrainWidth, terrainDepth, -0.10f, -0.46f, 0.36f, 0.13f, 4f, new Vector3(0.78f, 0.76f, 0.48f), "north dry chalk crop parcel");
        AddFieldParcel(meshes, graphicsDevice, textures, terrainCenter, terrainWidth, terrainDepth, 0.34f, -0.38f, 0.28f, 0.18f, 11f, new Vector3(0.45f, 0.59f, 0.31f), "north east muted meadow parcel");
        AddFieldParcel(meshes, graphicsDevice, textures, terrainCenter, terrainWidth, terrainDepth, -0.48f, 0.30f, 0.24f, 0.26f, 13f, new Vector3(0.52f, 0.66f, 0.36f), "south west rolling pasture parcel");
        AddFieldParcel(meshes, graphicsDevice, textures, terrainCenter, terrainWidth, terrainDepth, -0.06f, 0.43f, 0.33f, 0.14f, -5f, new Vector3(0.82f, 0.80f, 0.55f), "south pale harvest strip parcel");
        AddFieldParcel(meshes, graphicsDevice, textures, terrainCenter, terrainWidth, terrainDepth, 0.38f, 0.33f, 0.30f, 0.21f, -12f, new Vector3(0.39f, 0.54f, 0.29f), "south east deep field parcel");

        for (int i = 0; i < 9; i++)
        {
            float t = (i + 0.5f) / 9f;
            float x = MathHelper.Lerp(-halfWidth * 0.58f, halfWidth * 0.56f, t);
            float z = terrainCenter.Z + halfDepth * (i % 2 == 0 ? -0.52f : 0.52f);
            float y = SampleBackgroundTerrainY(terrainCenter, terrainWidth, terrainDepth, terrainCenter.X + x, z) + 0.084f;
            float angle = MathHelper.ToRadians(i % 2 == 0 ? -7f : 9f);
            Vector2 axisA = new(MathF.Cos(angle), MathF.Sin(angle));
            Vector2 axisB = new(-axisA.Y, axisA.X);
            meshes.Add(MeshFactory.CreateGroundRectangle(
                graphicsDevice,
                new Vector3(terrainCenter.X + x, y, z),
                axisA,
                terrainWidth * 0.055f,
                axisB,
                terrainDepth * 0.012f,
                textures.White,
                new Vector3(0.76f, 0.74f, 0.55f),
                $"patchwork far chalk field cut {i:00}"));
        }
    }

    private static void AddFieldParcel(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        Vector3 terrainCenter,
        float terrainWidth,
        float terrainDepth,
        float normalizedX,
        float normalizedZ,
        float normalizedWidth,
        float normalizedDepth,
        float rotationDegrees,
        Vector3 tint,
        string name)
    {
        Vector3 center = new(
            terrainCenter.X + terrainWidth * normalizedX,
            0f,
            terrainCenter.Z + terrainDepth * normalizedZ);
        center.Y = SampleBackgroundTerrainY(terrainCenter, terrainWidth, terrainDepth, center.X, center.Z) + 0.070f;
        float angle = MathHelper.ToRadians(rotationDegrees);
        Vector2 axisA = new(MathF.Cos(angle), MathF.Sin(angle));
        Vector2 axisB = new(-axisA.Y, axisA.X);
        meshes.Add(MeshFactory.CreateGroundRectangle(
            graphicsDevice,
            center,
            axisA,
            terrainWidth * normalizedWidth,
            axisB,
            terrainDepth * normalizedDepth,
            textures.Grass,
            tint,
            name,
            uvRepeatX: MathF.Max(1f, normalizedWidth * 8f),
            uvRepeatY: MathF.Max(1f, normalizedDepth * 8f)));
    }

    private static void AddMidFieldHedgerows(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        Vector3 terrainCenter,
        float terrainWidth,
        float terrainDepth)
    {
        float halfWidth = terrainWidth * 0.5f;
        float halfDepth = terrainDepth * 0.5f;
        AddHedgerowLine(
            meshes,
            graphicsDevice,
            textures,
            terrainCenter,
            terrainWidth,
            terrainDepth,
            new Vector3(terrainCenter.X - halfWidth * 0.53f, 0f, terrainCenter.Z - halfDepth * 0.28f),
            new Vector3(terrainCenter.X - halfWidth * 0.12f, 0f, terrainCenter.Z - halfDepth * 0.18f),
            14,
            "north west field boundary");
        AddHedgerowLine(
            meshes,
            graphicsDevice,
            textures,
            terrainCenter,
            terrainWidth,
            terrainDepth,
            new Vector3(terrainCenter.X + halfWidth * 0.11f, 0f, terrainCenter.Z - halfDepth * 0.12f),
            new Vector3(terrainCenter.X + halfWidth * 0.55f, 0f, terrainCenter.Z - halfDepth * 0.18f),
            16,
            "north east broken hedge");
        AddHedgerowLine(
            meshes,
            graphicsDevice,
            textures,
            terrainCenter,
            terrainWidth,
            terrainDepth,
            new Vector3(terrainCenter.X - halfWidth * 0.60f, 0f, terrainCenter.Z + halfDepth * 0.18f),
            new Vector3(terrainCenter.X - halfWidth * 0.22f, 0f, terrainCenter.Z + halfDepth * 0.34f),
            13,
            "south west diagonal hedge");
        AddHedgerowLine(
            meshes,
            graphicsDevice,
            textures,
            terrainCenter,
            terrainWidth,
            terrainDepth,
            new Vector3(terrainCenter.X + halfWidth * 0.22f, 0f, terrainCenter.Z + halfDepth * 0.24f),
            new Vector3(terrainCenter.X + halfWidth * 0.62f, 0f, terrainCenter.Z + halfDepth * 0.32f),
            15,
            "south east chalk boundary");
    }

    private static void AddHedgerowLine(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        Vector3 terrainCenter,
        float terrainWidth,
        float terrainDepth,
        Vector3 start,
        Vector3 end,
        int clumpCount,
        string label)
    {
        Vector3 direction = end - start;
        float yaw = MathF.Atan2(direction.Z, direction.X);
        Vector3 hedgeTintA = new(0.24f, 0.36f, 0.20f);
        Vector3 hedgeTintB = new(0.38f, 0.52f, 0.29f);
        Vector3 chalkFlint = new(0.52f, 0.51f, 0.43f);

        for (int i = 0; i < clumpCount; i++)
        {
            int seed = Hash(StableTextSeed(label), i * 97);
            float t = (i + 0.35f + ((seed % 1000) / 1000f - 0.5f) * 0.35f) / clumpCount;
            if (Hash(seed, i) % 7 == 0)
            {
                continue;
            }

            Vector3 basePosition = Vector3.Lerp(start, end, MathHelper.Clamp(t, 0f, 1f));
            float lateralJitter = ((Hash(seed, i * 31) % 1000) / 1000f - 0.5f) * 5.5f;
            Vector3 side = Vector3.Normalize(Vector3.Cross(Vector3.Up, direction.LengthSquared() <= 0.0001f ? Vector3.Forward : direction));
            Vector3 position = basePosition + side * lateralJitter;
            position.Y = SampleBackgroundTerrainY(terrainCenter, terrainWidth, terrainDepth, position.X, position.Z) + 0.04f;

            float width = 5.4f + (Hash(seed, 17) % 1000) / 1000f * 4.8f;
            float height = 3.0f + (Hash(seed, 29) % 1000) / 1000f * 3.2f;
            Texture2D texture = Hash(seed, 41) % 3 == 0 ? textures.ReferenceTreeBroad : textures.ReferenceTreeShrub;
            Vector3 tint = Vector3.Lerp(hedgeTintA, hedgeTintB, (Hash(seed, 53) % 1000) / 1000f);
            meshes.Add(MeshFactory.CreateCrossBillboard(
                graphicsDevice,
                position,
                width,
                height,
                texture,
                tint,
                $"mid-field {label} shrub clump {i:00}",
                alpha: 0.99f));

            if (i % 4 == 0)
            {
                meshes.Add(MeshFactory.CreateBox(
                    graphicsDevice,
                    position + new Vector3(0f, 0.18f, 0f),
                    new Vector3(width * 0.86f, 0.24f, 0.18f),
                    textures.White,
                    chalkFlint,
                    $"mid-field {label} low flint wall hint {i:00}"));
            }
        }

        Vector3 midpoint = Vector3.Lerp(start, end, 0.5f);
        midpoint.Y = SampleBackgroundTerrainY(terrainCenter, terrainWidth, terrainDepth, midpoint.X, midpoint.Z) + 0.055f;
        meshes.Add(MeshFactory.CreateVerticalPlane(
            graphicsDevice,
            midpoint,
            direction.Length() * 0.80f,
            0.90f,
            yaw,
            textures.ReferenceTreeShrub,
            new Vector3(0.20f, 0.30f, 0.16f),
            $"mid-field {label} dark hedge underlayer",
            alpha: 0.72f,
            uvRepeatX: 5f,
            uvRepeatY: 1f));
    }

    private static int StableTextSeed(string text)
    {
        int hash = 17;
        for (int i = 0; i < text.Length; i++)
        {
            hash = hash * 31 + text[i];
        }

        return hash;
    }

    private static void AddSalisburyPlainLandmarks(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        Vector3 terrainCenter,
        float terrainWidth,
        float terrainDepth)
    {
        AddDistantStoneCircle(meshes, graphicsDevice, textures, terrainCenter, terrainWidth, terrainDepth);
        AddFarmStoneWalls(meshes, graphicsDevice, textures, terrainCenter, terrainWidth, terrainDepth);
        AddFarmFenceLines(meshes, graphicsDevice, textures, terrainCenter, terrainWidth, terrainDepth);
        AddDistantFarmsteads(meshes, graphicsDevice, textures, terrainCenter, terrainWidth, terrainDepth);
        AddChalkStream(meshes, graphicsDevice, textures, terrainCenter, terrainWidth, terrainDepth);
    }

    private static void AddDistantStoneCircle(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        Vector3 terrainCenter,
        float terrainWidth,
        float terrainDepth)
    {
        Vector3 stone = new(0.58f, 0.56f, 0.48f);
        Vector3 center = terrainCenter + new Vector3(-terrainWidth * 0.30f, 0.42f, terrainDepth * 0.39f);
        float radius = 13f;
        for (int i = 0; i < 10; i++)
        {
            float angle = MathF.Tau * i / 10f;
            Vector3 position = center + new Vector3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius);
            float height = i % 3 == 0 ? 3.7f : 3.0f;
            meshes.Add(MeshFactory.CreateBox(
                graphicsDevice,
                position + Vector3.Up * (height * 0.5f),
                new Vector3(1.0f, height, 1.35f),
                textures.White,
                stone,
                $"distant Salisbury standing stone {i:00}"));

            if (i % 2 == 0)
            {
                Vector3 next = center + new Vector3(MathF.Cos(angle + MathF.Tau / 10f) * radius, 0f, MathF.Sin(angle + MathF.Tau / 10f) * radius);
                Vector3 lintelCenter = Vector3.Lerp(position, next, 0.5f) + Vector3.Up * (height + 0.45f);
                meshes.Add(MeshFactory.CreateBox(
                    graphicsDevice,
                    lintelCenter,
                    new Vector3(3.2f, 0.55f, 1.15f),
                    textures.White,
                    stone * 0.96f,
                    $"distant Salisbury lintel {i:00}"));
            }
        }
    }

    private static void AddFarmStoneWalls(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        Vector3 terrainCenter,
        float terrainWidth,
        float terrainDepth)
    {
        Vector3 wall = new(0.49f, 0.48f, 0.43f);
        float y = terrainCenter.Y + 0.30f;
        float halfWidth = terrainWidth * 0.5f;
        float halfDepth = terrainDepth * 0.5f;
        AddWall(meshes, graphicsDevice, textures, terrainCenter + new Vector3(-halfWidth * 0.34f, y, -halfDepth * 0.23f), terrainWidth * 0.28f, 0.38f, wall, "north west flint farm wall");
        AddWall(meshes, graphicsDevice, textures, terrainCenter + new Vector3(halfWidth * 0.29f, y, halfDepth * 0.25f), terrainWidth * 0.30f, 0.38f, wall, "south east flint farm wall");
        AddWall(meshes, graphicsDevice, textures, terrainCenter + new Vector3(-halfWidth * 0.47f, y, halfDepth * 0.05f), 0.42f, terrainDepth * 0.34f, wall * 0.92f, "west chalk field division wall");
        AddWall(meshes, graphicsDevice, textures, terrainCenter + new Vector3(halfWidth * 0.46f, y, -halfDepth * 0.08f), 0.42f, terrainDepth * 0.32f, wall * 0.92f, "east chalk field division wall");
    }

    private static void AddFarmFenceLines(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        Vector3 terrainCenter,
        float terrainWidth,
        float terrainDepth)
    {
        float halfWidth = terrainWidth * 0.5f;
        float halfDepth = terrainDepth * 0.5f;
        AddFenceLine(
            meshes,
            graphicsDevice,
            textures,
            terrainCenter,
            terrainWidth,
            terrainDepth,
            terrainCenter + new Vector3(-halfWidth * 0.22f, 0f, -halfDepth * 0.43f),
            terrainCenter + new Vector3(halfWidth * 0.28f, 0f, -halfDepth * 0.36f),
            18,
            "north chalk paddock fence");
        AddFenceLine(
            meshes,
            graphicsDevice,
            textures,
            terrainCenter,
            terrainWidth,
            terrainDepth,
            terrainCenter + new Vector3(-halfWidth * 0.58f, 0f, halfDepth * 0.42f),
            terrainCenter + new Vector3(-halfWidth * 0.08f, 0f, halfDepth * 0.50f),
            16,
            "south west open sheep fence");
        AddFenceLine(
            meshes,
            graphicsDevice,
            textures,
            terrainCenter,
            terrainWidth,
            terrainDepth,
            terrainCenter + new Vector3(halfWidth * 0.35f, 0f, halfDepth * 0.06f),
            terrainCenter + new Vector3(halfWidth * 0.50f, 0f, halfDepth * 0.42f),
            13,
            "east service field fence");
    }

    private static void AddFenceLine(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        Vector3 terrainCenter,
        float terrainWidth,
        float terrainDepth,
        Vector3 start,
        Vector3 end,
        int postCount,
        string label)
    {
        Vector3 direction = end - start;
        float length = direction.Length();
        if (length <= 0.001f)
        {
            return;
        }

        Vector3 tangent = direction / length;
        Vector3 postTint = new(0.34f, 0.28f, 0.18f);
        Vector3 railTint = new(0.40f, 0.34f, 0.23f);
        float postSpacing = length / Math.Max(1, postCount - 1);

        for (int i = 0; i < postCount; i++)
        {
            if (i is 6 or 7)
            {
                continue;
            }

            Vector3 position = Vector3.Lerp(start, end, i / MathF.Max(1f, postCount - 1f));
            position.Y = SampleBackgroundTerrainY(terrainCenter, terrainWidth, terrainDepth, position.X, position.Z) + 0.42f;
            meshes.Add(MeshFactory.CreateBox(
                graphicsDevice,
                position,
                new Vector3(0.18f, 0.84f, 0.18f),
                textures.White,
                postTint,
                $"field {label} timber post {i:00}"));
        }

        for (int i = 0; i < postCount - 1; i++)
        {
            if (i is 5 or 6 or 7)
            {
                continue;
            }

            Vector3 railCenter = Vector3.Lerp(start, end, (i + 0.5f) / MathF.Max(1f, postCount - 1f));
            railCenter.Y = SampleBackgroundTerrainY(terrainCenter, terrainWidth, terrainDepth, railCenter.X, railCenter.Z) + 0.66f;
            Vector3 size = new(
                MathF.Max(0.24f, MathF.Abs(tangent.X) * postSpacing + 0.16f),
                0.10f,
                MathF.Max(0.24f, MathF.Abs(tangent.Z) * postSpacing + 0.16f));
            meshes.Add(MeshFactory.CreateBox(
                graphicsDevice,
                railCenter,
                size,
                textures.White,
                railTint,
                $"field {label} upper rail {i:00}"));
            meshes.Add(MeshFactory.CreateBox(
                graphicsDevice,
                railCenter - Vector3.Up * 0.24f,
                size,
                textures.White,
                railTint * 0.92f,
                $"field {label} lower rail {i:00}"));
        }

        Vector3 gateCenter = Vector3.Lerp(start, end, 6.5f / MathF.Max(1f, postCount - 1f));
        gateCenter.Y = SampleBackgroundTerrainY(terrainCenter, terrainWidth, terrainDepth, gateCenter.X, gateCenter.Z) + 0.54f;
        meshes.Add(MeshFactory.CreateBox(
            graphicsDevice,
            gateCenter,
            new Vector3(MathF.Max(0.42f, MathF.Abs(tangent.X) * postSpacing * 1.65f), 0.16f, MathF.Max(0.42f, MathF.Abs(tangent.Z) * postSpacing * 1.65f)),
            textures.White,
            railTint * 1.12f,
            $"field {label} open gate rail"));
    }

    private static void AddDistantFarmsteads(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        Vector3 terrainCenter,
        float terrainWidth,
        float terrainDepth)
    {
        float halfWidth = terrainWidth * 0.5f;
        float halfDepth = terrainDepth * 0.5f;
        AddFarmsteadCluster(
            meshes,
            graphicsDevice,
            textures,
            terrainCenter,
            terrainWidth,
            terrainDepth,
            terrainCenter + new Vector3(-halfWidth * 0.42f, 0f, -halfDepth * 0.34f),
            "north west chalk farm");
        AddFarmsteadCluster(
            meshes,
            graphicsDevice,
            textures,
            terrainCenter,
            terrainWidth,
            terrainDepth,
            terrainCenter + new Vector3(halfWidth * 0.38f, 0f, halfDepth * 0.31f),
            "south east ridge farm");
        AddFarmsteadCluster(
            meshes,
            graphicsDevice,
            textures,
            terrainCenter,
            terrainWidth,
            terrainDepth,
            terrainCenter + new Vector3(halfWidth * 0.18f, 0f, -halfDepth * 0.47f),
            "north service farm");
    }

    private static void AddFarmsteadCluster(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        Vector3 terrainCenter,
        float terrainWidth,
        float terrainDepth,
        Vector3 basePosition,
        string label)
    {
        float groundY = SampleBackgroundTerrainY(terrainCenter, terrainWidth, terrainDepth, basePosition.X, basePosition.Z);
        Vector3 chalkWall = new(0.68f, 0.66f, 0.54f);
        Vector3 brick = new(0.44f, 0.31f, 0.24f);
        Vector3 roof = new(0.18f, 0.16f, 0.13f);
        Vector3 barn = new(0.34f, 0.28f, 0.19f);
        Vector3 darkDoor = new(0.08f, 0.07f, 0.055f);
        Vector3 silo = new(0.56f, 0.56f, 0.51f);

        Vector3 farmhouse = new(basePosition.X, groundY + 0.95f, basePosition.Z);
        meshes.Add(MeshFactory.CreateBox(
            graphicsDevice,
            farmhouse,
            new Vector3(4.4f, 1.9f, 3.2f),
            textures.White,
            chalkWall,
            $"distant {label} farmhouse"));
        meshes.Add(MeshFactory.CreateBox(
            graphicsDevice,
            farmhouse + new Vector3(0f, 1.15f, 0f),
            new Vector3(5.0f, 0.55f, 3.8f),
            textures.White,
            roof,
            $"distant {label} dark roof"));
        meshes.Add(MeshFactory.CreateBox(
            graphicsDevice,
            farmhouse + new Vector3(-1.3f, -0.22f, -1.63f),
            new Vector3(0.52f, 0.75f, 0.10f),
            textures.White,
            darkDoor,
            $"distant {label} front door"));

        Vector3 barnCenter = basePosition + new Vector3(5.8f, 0f, 2.0f);
        barnCenter.Y = SampleBackgroundTerrainY(terrainCenter, terrainWidth, terrainDepth, barnCenter.X, barnCenter.Z) + 0.86f;
        meshes.Add(MeshFactory.CreateBox(
            graphicsDevice,
            barnCenter,
            new Vector3(6.2f, 1.72f, 3.8f),
            textures.White,
            barn,
            $"distant {label} timber barn"));
        meshes.Add(MeshFactory.CreateBox(
            graphicsDevice,
            barnCenter + new Vector3(0f, 1.10f, 0f),
            new Vector3(6.8f, 0.48f, 4.4f),
            textures.White,
            roof * 1.12f,
            $"distant {label} barn roof"));
        meshes.Add(MeshFactory.CreateBox(
            graphicsDevice,
            barnCenter + new Vector3(0f, -0.24f, -1.96f),
            new Vector3(1.5f, 0.92f, 0.12f),
            textures.White,
            darkDoor,
            $"distant {label} barn opening"));

        Vector3 shedCenter = basePosition + new Vector3(-4.9f, 0f, 1.8f);
        shedCenter.Y = SampleBackgroundTerrainY(terrainCenter, terrainWidth, terrainDepth, shedCenter.X, shedCenter.Z) + 0.55f;
        meshes.Add(MeshFactory.CreateBox(
            graphicsDevice,
            shedCenter,
            new Vector3(3.2f, 1.1f, 2.5f),
            textures.White,
            brick,
            $"distant {label} brick outbuilding"));
        meshes.Add(MeshFactory.CreateBox(
            graphicsDevice,
            shedCenter + new Vector3(0f, 0.78f, 0f),
            new Vector3(3.7f, 0.36f, 2.9f),
            textures.White,
            roof * 0.9f,
            $"distant {label} outbuilding roof"));

        Vector3 siloCenter = basePosition + new Vector3(9.1f, 0f, -1.9f);
        siloCenter.Y = SampleBackgroundTerrainY(terrainCenter, terrainWidth, terrainDepth, siloCenter.X, siloCenter.Z) + 1.18f;
        meshes.Add(MeshFactory.CreateCylinderY(
            graphicsDevice,
            siloCenter,
            0.70f,
            2.35f,
            10,
            textures.White,
            silo,
            $"distant {label} pale silo"));
        meshes.Add(MeshFactory.CreateBox(
            graphicsDevice,
            siloCenter + new Vector3(0f, 1.34f, 0f),
            new Vector3(1.65f, 0.28f, 1.65f),
            textures.White,
            roof * 1.16f,
            $"distant {label} silo cap"));

        for (int i = 0; i < 5; i++)
        {
            float x = -5.4f + i * 2.1f;
            Vector3 sheep = basePosition + new Vector3(x, 0f, -4.6f - (i % 2) * 1.1f);
            sheep.Y = SampleBackgroundTerrainY(terrainCenter, terrainWidth, terrainDepth, sheep.X, sheep.Z) + 0.18f;
            meshes.Add(MeshFactory.CreateBox(
                graphicsDevice,
                sheep,
                new Vector3(0.55f, 0.36f, 0.40f),
                textures.White,
                new Vector3(0.82f, 0.80f, 0.68f),
                $"distant {label} sheep block {i:00}"));
        }
    }

    private static void AddTracksideRailwayAndUnderpass(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        TrackDefinition definition,
        IReadOnlyList<Vector3> centerLine,
        TrackOffsetProfiles offsets)
    {
        int startIndex = GetStartIndex(definition.Layout, centerLine);
        Vector3 start = centerLine[startIndex];
        Vector2 left = GetTrackLeftNormal(centerLine, startIndex);
        float railOffset = SampleOffsetValue(offsets.RightWall, startIndex) - 38f;
        Vector3 railwayCenter = OffsetTrackPoint(start, left * railOffset, 0.10f);

        Vector3 rail = new(0.18f, 0.17f, 0.15f);
        Vector3 sleeper = new(0.34f, 0.25f, 0.17f);
        float z = railwayCenter.Z;
        float y = railwayCenter.Y + 0.10f;
        float length = 92f;
        float centerX = railwayCenter.X + 18f;
        meshes.Add(MeshFactory.CreateBox(graphicsDevice, new Vector3(centerX, y + 0.05f, z - 0.72f), new Vector3(length, 0.10f, 0.10f), textures.White, rail, "far railway rail near"));
        meshes.Add(MeshFactory.CreateBox(graphicsDevice, new Vector3(centerX, y + 0.05f, z + 0.72f), new Vector3(length, 0.10f, 0.10f), textures.White, rail, "far railway rail far"));
        for (int i = 0; i < 22; i++)
        {
            float t = (i + 0.5f) / 22f - 0.5f;
            meshes.Add(MeshFactory.CreateBox(
                graphicsDevice,
                new Vector3(centerX + t * length, y, z),
                new Vector3(1.0f, 0.08f, 1.95f),
                textures.White,
                sleeper,
                $"far railway sleeper {i:00}"));
        }

        Vector3 brick = new(0.42f, 0.35f, 0.29f);
        Vector3 tunnelCenter = new(centerX + length * 0.34f, railwayCenter.Y + 1.15f, z);
        meshes.Add(MeshFactory.CreateBox(graphicsDevice, tunnelCenter + new Vector3(-3.2f, 0f, 0f), new Vector3(1.0f, 2.4f, 5.8f), textures.White, brick, "brick underpass left pier"));
        meshes.Add(MeshFactory.CreateBox(graphicsDevice, tunnelCenter + new Vector3(3.2f, 0f, 0f), new Vector3(1.0f, 2.4f, 5.8f), textures.White, brick, "brick underpass right pier"));
        meshes.Add(MeshFactory.CreateBox(graphicsDevice, tunnelCenter + new Vector3(0f, 1.35f, 0f), new Vector3(7.4f, 0.85f, 5.8f), textures.White, brick * 1.08f, "brick underpass lintel"));
        meshes.Add(MeshFactory.CreateBox(graphicsDevice, tunnelCenter + new Vector3(0f, -0.15f, 0f), new Vector3(5.1f, 1.45f, 5.2f), textures.White, new Vector3(0.035f, 0.033f, 0.030f), "brick underpass dark opening"));
    }

    private static void AddChalkStream(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        Vector3 terrainCenter,
        float terrainWidth,
        float terrainDepth)
    {
        Vector3 water = new(0.54f, 0.68f, 0.68f);
        Vector3 chalkBank = new(0.78f, 0.76f, 0.58f);
        float z = terrainCenter.Z + terrainDepth * 0.35f;
        float y = terrainCenter.Y + 0.065f;
        meshes.Add(MeshFactory.CreatePlane(graphicsDevice, terrainCenter + new Vector3(terrainWidth * 0.22f, y, z), terrainWidth * 0.34f, terrainDepth * 0.020f, textures.White, 1f, chalkBank, "pale chalk stream bank"));
        meshes.Add(MeshFactory.CreatePlane(graphicsDevice, terrainCenter + new Vector3(terrainWidth * 0.22f, y + 0.004f, z), terrainWidth * 0.31f, terrainDepth * 0.010f, textures.White, 1f, water, "clear chalk stream water"));
    }

    private static void AddWall(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        Vector3 center,
        float width,
        float depth,
        Vector3 tint,
        string name)
    {
        meshes.Add(MeshFactory.CreateBox(
            graphicsDevice,
            center,
            new Vector3(width, 0.42f, depth),
            textures.White,
            tint,
            name));
    }

    private static void AddRollingBackgroundTerrain(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        Vector3 terrainCenter,
        float terrainWidth,
        float terrainDepth)
    {
        float halfWidth = terrainWidth * 0.5f;
        float halfDepth = terrainDepth * 0.5f;
        Vector3 tint = new(0.93f, 0.91f, 0.70f);
        meshes.Add(MeshFactory.CreateRollingTerrainPatch(
            graphicsDevice,
            terrainCenter + new Vector3(0f, 0.035f, -halfDepth * 0.42f),
            terrainWidth * 0.92f,
            terrainDepth * 0.24f,
            1.20f,
            18,
            6,
            textures.Grass,
            10f,
            tint,
            "background rolling field north",
            0.3f));
        meshes.Add(MeshFactory.CreateRollingTerrainPatch(
            graphicsDevice,
            terrainCenter + new Vector3(0f, 0.030f, halfDepth * 0.42f),
            terrainWidth * 0.92f,
            terrainDepth * 0.24f,
            1.05f,
            18,
            6,
            textures.Grass,
            10f,
            tint,
            "background rolling field south",
            1.8f));
        meshes.Add(MeshFactory.CreateRollingTerrainPatch(
            graphicsDevice,
            terrainCenter + new Vector3(-halfWidth * 0.42f, 0.025f, 0f),
            terrainWidth * 0.24f,
            terrainDepth * 0.82f,
            0.85f,
            6,
            18,
            textures.Grass,
            10f,
            tint,
            "background rolling field west",
            3.1f));
        meshes.Add(MeshFactory.CreateRollingTerrainPatch(
            graphicsDevice,
            terrainCenter + new Vector3(halfWidth * 0.42f, 0.025f, 0f),
            terrainWidth * 0.24f,
            terrainDepth * 0.82f,
            0.95f,
            6,
            18,
            textures.Grass,
            10f,
            tint,
            "background rolling field east",
            4.2f));
    }

    private static void AddProceduralTreeClumps(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        Vector3 terrainCenter,
        float terrainWidth,
        float terrainDepth)
    {
        float halfWidth = terrainWidth * 0.5f;
        float halfDepth = terrainDepth * 0.5f;
        int count = 144;
        for (int i = 0; i < count; i++)
        {
            int seed = Hash(i * 17, 91);
            bool horizontalSide = i % 2 == 0;
            float side = (i / 2) % 2 == 0 ? -1f : 1f;
            float t = ((seed % 1000) + 0.5f) / 1000f;
            float jitter = ((Hash(i, seed) % 1000) / 1000f - 0.5f) * 18f;
            Vector3 position = horizontalSide
                ? new Vector3(terrainCenter.X + MathHelper.Lerp(-halfWidth * 0.82f, halfWidth * 0.82f, t), terrainCenter.Y + 0.02f, terrainCenter.Z + side * (halfDepth * 0.42f + 10f + jitter))
                : new Vector3(terrainCenter.X + side * (halfWidth * 0.42f + 10f + jitter), terrainCenter.Y + 0.02f, terrainCenter.Z + MathHelper.Lerp(-halfDepth * 0.82f, halfDepth * 0.82f, t));
            position.Y = SampleBackgroundTerrainY(terrainCenter, terrainWidth, terrainDepth, position.X, position.Z) + 0.02f;
            float size = (3.0f + (Hash(seed, i * 31) % 1000) / 1000f * 3.8f) * FieldTreeScale;
            Vector3 tint = Vector3.Lerp(new Vector3(0.58f, 0.70f, 0.42f), new Vector3(0.33f, 0.48f, 0.29f), (Hash(seed, i) % 1000) / 1000f);
            Texture2D texture = (Hash(i, seed + 7) % 6) switch
            {
                0 => textures.ReferenceTreePines,
                1 => textures.ReferenceTreePinesAlt,
                2 => textures.ReferenceTreeBroad,
                3 => textures.ReferenceTreeBroadAlt,
                4 => textures.ReferenceTreeShrub,
                _ => textures.ReferenceTreeShrubAlt
            };
            bool isPine = texture == textures.ReferenceTreePines || texture == textures.ReferenceTreePinesAlt;
            float widthMultiplier = isPine ? 0.72f : 0.92f;
            float heightMultiplier = isPine ? 1.45f : 1f;
            meshes.Add(MeshFactory.CreateCrossBillboard(
                graphicsDevice,
                position,
                size * widthMultiplier,
                size * heightMultiplier,
                texture,
                tint,
                $"procedural tree clump {i:00}",
                alpha: 0.99f));
        }

        int clusterCount = 26;
        int meshIndex = count;
        for (int cluster = 0; cluster < clusterCount; cluster++)
        {
            int clusterSeed = Hash(cluster * 43, 517);
            bool horizontalSide = cluster % 2 == 0;
            float side = (cluster / 2) % 2 == 0 ? -1f : 1f;
            float along = ((clusterSeed % 1000) + 0.5f) / 1000f;
            Vector3 clusterCenter = horizontalSide
                ? new Vector3(
                    terrainCenter.X + MathHelper.Lerp(-halfWidth * 0.78f, halfWidth * 0.78f, along),
                    terrainCenter.Y + 0.025f,
                    terrainCenter.Z + side * (halfDepth * 0.34f + 16f + (Hash(cluster, clusterSeed) % 1000 / 1000f) * 22f))
                : new Vector3(
                    terrainCenter.X + side * (halfWidth * 0.34f + 16f + (Hash(cluster, clusterSeed) % 1000 / 1000f) * 22f),
                    terrainCenter.Y + 0.025f,
                    terrainCenter.Z + MathHelper.Lerp(-halfDepth * 0.78f, halfDepth * 0.78f, along));

            int treesInCluster = 10 + Hash(clusterSeed, cluster * 9) % 10;
            float clusterRadius = 12f + (Hash(clusterSeed, 29) % 1000) / 1000f * 16f;
            Vector3 understoryCenter = clusterCenter;
            understoryCenter.Y = SampleBackgroundTerrainY(terrainCenter, terrainWidth, terrainDepth, understoryCenter.X, understoryCenter.Z) + 0.074f;
            Vector3 understoryTint = Vector3.Lerp(
                new Vector3(0.24f, 0.35f, 0.18f),
                new Vector3(0.14f, 0.23f, 0.13f),
                (Hash(clusterSeed, 811) % 1000) / 1000f);
            meshes.Add(MeshFactory.CreateGroundRectangle(
                graphicsDevice,
                understoryCenter,
                Vector2.UnitX,
                clusterRadius * 1.85f,
                Vector2.UnitY,
                clusterRadius * 1.28f,
                textures.Grass,
                understoryTint,
                $"procedural forest understory patch {cluster:00}",
                uvRepeatX: 2.8f,
                uvRepeatY: 2.0f));

            for (int j = 0; j < treesInCluster; j++)
            {
                int treeSeed = Hash(clusterSeed + j * 71, cluster * 31);
                float angle = MathF.Tau * ((treeSeed % 1000) / 1000f);
                float distance = clusterRadius * MathF.Sqrt((Hash(treeSeed, j * 13) % 1000) / 1000f);
                Vector3 position = clusterCenter + new Vector3(MathF.Cos(angle) * distance, 0f, MathF.Sin(angle) * distance);
                position.Y = SampleBackgroundTerrainY(terrainCenter, terrainWidth, terrainDepth, position.X, position.Z) + 0.02f;
                float size = (3.4f + (Hash(treeSeed, j * 47) % 1000) / 1000f * 4.2f) * FieldTreeScale;
                Texture2D texture = (Hash(j, treeSeed + 7) % 8) switch
                {
                    0 => textures.ReferenceTreePines,
                    1 => textures.ReferenceTreePinesAlt,
                    2 => textures.ReferenceTreeBroad,
                    3 => textures.ReferenceTreeBroadAlt,
                    4 => textures.ReferenceTreeBroad,
                    5 => textures.ReferenceTreeBroadAlt,
                    6 => textures.ReferenceTreeShrub,
                    _ => textures.ReferenceTreeShrubAlt
                };
                bool isPine = texture == textures.ReferenceTreePines || texture == textures.ReferenceTreePinesAlt;
                float widthMultiplier = isPine ? 0.70f : 0.98f;
                float heightMultiplier = isPine ? 1.55f : 1.05f;
                float tintAmount = (Hash(treeSeed, j) % 1000) / 1000f;
                Vector3 tint = Vector3.Lerp(new Vector3(0.43f, 0.60f, 0.32f), new Vector3(0.22f, 0.34f, 0.18f), tintAmount);
                meshes.Add(MeshFactory.CreateCrossBillboard(
                    graphicsDevice,
                    position,
                    size * widthMultiplier,
                    size * heightMultiplier,
                    texture,
                    tint,
                    $"procedural forest tree {meshIndex++:000}",
                    alpha: 0.99f));
            }
        }
    }

    private static float SampleBackgroundTerrainY(
        Vector3 terrainCenter,
        float terrainWidth,
        float terrainDepth,
        float x,
        float z)
    {
        float halfWidth = terrainWidth * 0.5f;
        float halfDepth = terrainDepth * 0.5f;
        float northSouth = SampleRollingPatchY(
            terrainCenter + new Vector3(0f, 0.035f, -halfDepth * 0.42f),
            terrainWidth * 0.92f,
            terrainDepth * 0.24f,
            1.20f,
            x,
            z,
            0.3f);
        northSouth = MathF.Max(
            northSouth,
            SampleRollingPatchY(
                terrainCenter + new Vector3(0f, 0.030f, halfDepth * 0.42f),
                terrainWidth * 0.92f,
                terrainDepth * 0.24f,
                1.05f,
                x,
                z,
                1.8f));
        float eastWest = SampleRollingPatchY(
            terrainCenter + new Vector3(-halfWidth * 0.42f, 0.025f, 0f),
            terrainWidth * 0.24f,
            terrainDepth * 0.82f,
            0.85f,
            x,
            z,
            3.1f);
        eastWest = MathF.Max(
            eastWest,
            SampleRollingPatchY(
                terrainCenter + new Vector3(halfWidth * 0.42f, 0.025f, 0f),
                terrainWidth * 0.24f,
                terrainDepth * 0.82f,
                0.95f,
                x,
                z,
                4.2f));

        return MathF.Max(terrainCenter.Y, MathF.Max(northSouth, eastWest));
    }

    private static float SampleRollingPatchY(
        Vector3 center,
        float width,
        float depth,
        float height,
        float x,
        float z,
        float phase)
    {
        float xT = (x - center.X) / MathF.Max(0.001f, width) + 0.5f;
        float zT = (z - center.Z) / MathF.Max(0.001f, depth) + 0.5f;
        if (xT is < 0f or > 1f || zT is < 0f or > 1f)
        {
            return float.NegativeInfinity;
        }

        float edgeFadeX = MathF.Sin(xT * MathF.PI);
        float edgeFadeZ = MathF.Sin(zT * MathF.PI);
        float edgeFade = MathHelper.Clamp(edgeFadeX * edgeFadeZ, 0f, 1f);
        float broad =
            MathF.Sin(xT * MathF.Tau * 1.15f + phase) * 0.48f +
            MathF.Sin(zT * MathF.Tau * 1.45f + phase * 0.71f) * 0.36f +
            MathF.Sin((xT + zT) * MathF.Tau * 0.82f + phase * 1.6f) * 0.22f;
        return center.Y + height * broad * edgeFade;
    }

    public SurfaceSample Sample(Vector3 position)
    {
        if (_authoredSurfaceSampler is not null &&
            _authoredSurfaceSampler.TryGetContact(position + Vector3.Up * 1.5f, 4.0f, out _))
        {
            return _surfaceLibrary.Road;
        }

        CenterLineProjection projection = ProjectToCenterLine(new Vector2(position.X, position.Z));
        TrackWidthSample width = GetProjectionWidthSample(projection);
        float roadWidth = width.RoadWidthForSignedDistance(projection.SignedDistance);
        if (projection.Distance <= roadWidth + CurbInnerOffsetFromRoadEdgeMeters)
        {
            return _surfaceLibrary.Road;
        }

        float curbOuter = roadWidth + CurbOuterOffsetFromRoadEdgeMeters;
        float curbGrassBlendStart = curbOuter - CurbGrassBlendZoneMeters;
        if (projection.Distance <= curbGrassBlendStart)
        {
            return _surfaceLibrary.Curb;
        }

        if (projection.Distance <= curbOuter)
        {
            float blend = (projection.Distance - curbGrassBlendStart) / MathF.Max(0.001f, CurbGrassBlendZoneMeters);
            return SurfaceSample.Blend("CURB_GRASS", _surfaceLibrary.Curb, _surfaceLibrary.Grass, blend);
        }

        return _surfaceLibrary.Grass;
    }

    public bool TryGetSurfaceContact(Vector3 queryPosition, float downwardRangeMeters, out TrackSurfaceContact contact)
    {
        if (_authoredSurfaceSampler is not null &&
            _authoredSurfaceSampler.TryGetContact(queryPosition, downwardRangeMeters, out contact))
        {
            return true;
        }

        contact = default;
        return false;
    }

    public bool TryGetSurfaceContactRay(Vector3 origin, Vector3 direction, float maxDistanceMeters, out TrackSurfaceContact contact)
    {
        if (_authoredSurfaceSampler is not null &&
            _authoredSurfaceSampler.TryGetContactRay(origin, direction, maxDistanceMeters, out contact))
        {
            return true;
        }

        contact = default;
        return false;
    }

    public bool TryGetSurfaceContact(Vector2 position, out TrackSurfaceContact contact)
    {
        if (_authoredSurfaceSampler is not null &&
            _authoredSurfaceSampler.TryGetContact(position, out contact))
        {
            return true;
        }

        contact = default;
        return false;
    }

    public float GetElevation(Vector2 position)
    {
        if (_authoredSurfaceSampler is not null &&
            _authoredSurfaceSampler.TryGetContact(position, out TrackSurfaceContact contact))
        {
            return contact.Position.Y;
        }

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
            -width.RightRoadWidthMeters - CurbOuterOffsetFromRoadEdgeMeters,
            width.LeftRoadWidthMeters + CurbOuterOffsetFromRoadEdgeMeters);
        float bankedElevation = projection.Elevation + bankedDistance * MathF.Tan(bankRadians);
        if (projection.Distance <= roadWidth + CurbOuterOffsetFromRoadEdgeMeters)
        {
            return bankedElevation;
        }

        float shoulderT = SmoothStep(roadWidth + CurbOuterOffsetFromRoadEdgeMeters, roadWidth + grassWidth, projection.Distance);
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
            TrackLayout.HighSpeedRing => BuildHighSpeedRingCenterLine(definition.LengthMeters),
            TrackLayout.LakesidePark => BuildLakesideParkCenterLine(definition.LengthMeters, definition.ElevationDifferenceMeters),
            TrackLayout.CustomSpline => BuildHighSpeedRingCenterLine(definition.LengthMeters),
            _ => BuildHighSpeedRingCenterLine(definition.LengthMeters)
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
            float leftCurbOuterOffset = leftRoadWidth + CurbOuterOffsetFromRoadEdgeMeters;
            float rightCurbOuterOffset = rightRoadWidth + CurbOuterOffsetFromRoadEdgeMeters;
            float leftGrassOuterOffset = leftRoadWidth + MathF.Max(CurbOuterOffsetFromRoadEdgeMeters, sample.LeftGrassWidthMeters);
            float rightGrassOuterOffset = rightRoadWidth + MathF.Max(CurbOuterOffsetFromRoadEdgeMeters, sample.RightGrassWidthMeters);
            leftRoad[i] = leftRoadWidth;
            rightRoad[i] = -rightRoadWidth;
            leftCurbInner[i] = leftRoadWidth + CurbInnerOffsetFromRoadEdgeMeters;
            leftCurbOuter[i] = leftCurbOuterOffset;
            rightCurbInner[i] = -rightRoadWidth - CurbInnerOffsetFromRoadEdgeMeters;
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

    private static Vector3[] BuildHighSpeedRingCenterLine(float targetLengthMeters)
    {
        const float sourcePathLengthSvgUnits = 3317.401749462f;
        const float defaultTargetLengthMeters = 3100.0f;
        const float defaultMetresPerSvgUnit = 0.934466258271f;
        const float finalSpacingMeters = 1.0f;
        float scale = targetLengthMeters > 0.001f
            ? targetLengthMeters / sourcePathLengthSvgUnits
            : defaultMetresPerSvgUnit;
        if (MathF.Abs((targetLengthMeters <= 0.001f ? defaultTargetLengthMeters : targetLengthMeters) - defaultTargetLengthMeters) < 0.01f)
        {
            scale = defaultMetresPerSvgUnit;
        }

        List<Vector2> dense = BuildHighSpeedRingDensePolyline(scale);
        float[] denseDistances = CalculateCumulativeDistances(dense);
        float loopLength = denseDistances[^1];
        float requestedLength = targetLengthMeters > 0.001f ? targetLengthMeters : defaultTargetLengthMeters;
        int sampleCount = Math.Max(16, (int)MathF.Round(requestedLength / finalSpacingMeters));
        float startDistance = FindDistanceAlongPolyline(dense, denseDistances, HighSpeedRingStartWorldXZ(scale));
        Vector3[] result = new Vector3[sampleCount];

        for (int i = 0; i < result.Length; i++)
        {
            float distanceFromStart = i / (float)result.Length * loopLength;
            Vector2 point = SamplePolylineAtDistance(dense, denseDistances, startDistance - distanceFromStart);
            result[i] = new Vector3(point.X, 0f, point.Y);
        }

        return result;
    }

    private static List<Vector2> BuildHighSpeedRingDensePolyline(float metresPerSvgUnit)
    {
        HighSpeedRingPathSegment[] segments = BuildHighSpeedRingSourceSegments();
        List<Vector2> points = [];
        foreach (HighSpeedRingPathSegment segment in segments)
        {
            int sampleCount = segment.Kind == HighSpeedRingPathSegmentKind.Line
                ? Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(segment.P0, segment.P3) / 4f))
                : 220;

            if (points.Count == 0)
            {
                points.Add(segment.P0 * metresPerSvgUnit);
            }

            for (int sample = 1; sample <= sampleCount; sample++)
            {
                float t = sample / (float)sampleCount;
                points.Add(EvaluateHighSpeedRingSegment(segment, t) * metresPerSvgUnit);
            }
        }

        if (points.Count > 1 && Vector2.DistanceSquared(points[0], points[^1]) < 0.0001f)
        {
            points.RemoveAt(points.Count - 1);
        }

        return points;
    }

    private static HighSpeedRingPathSegment[] BuildHighSpeedRingSourceSegments()
    {
        return
        [
            HighSpeedRingPathSegment.Line(new(1468.98f, 396.56f), new(1444.65f, 396.56f)),
            HighSpeedRingPathSegment.Cubic(new(1444.65f, 396.56f), new(1351.46f, 396.56f), new(1266.12f, 448.73f), new(1223.62f, 531.66f)),
            HighSpeedRingPathSegment.Line(new(1223.62f, 531.66f), new(1112.45f, 748.62f)),
            HighSpeedRingPathSegment.Cubic(new(1112.45f, 748.62f), new(1080.78f, 810.42f), new(988.71f, 796.58f), new(976.62f, 728.20f)),
            HighSpeedRingPathSegment.Line(new(976.62f, 728.20f), new(968.21f, 680.68f)),
            HighSpeedRingPathSegment.Cubic(new(968.21f, 680.68f), new(957.01f, 617.38f), new(876.57f, 596.60f), new(836.11f, 646.55f)),
            HighSpeedRingPathSegment.Line(new(836.11f, 646.55f), new(680.53f, 838.68f)),
            HighSpeedRingPathSegment.Cubic(new(680.53f, 838.68f), new(636.29f, 893.31f), new(549.68f, 880.52f), new(523.11f, 815.44f)),
            HighSpeedRingPathSegment.Line(new(523.11f, 815.44f), new(402.85f, 520.79f)),
            HighSpeedRingPathSegment.Cubic(new(402.85f, 520.79f), new(340.09f, 367.03f), new(453.20f, 198.69f), new(619.28f, 198.69f)),
            HighSpeedRingPathSegment.Line(new(619.28f, 198.69f), new(1468.98f, 198.69f)),
            HighSpeedRingPathSegment.Cubic(new(1468.98f, 198.69f), new(1523.62f, 198.69f), new(1567.92f, 242.98f), new(1567.92f, 297.63f)),
            HighSpeedRingPathSegment.Cubic(new(1567.92f, 297.63f), new(1567.91f, 352.27f), new(1523.62f, 396.56f), new(1468.98f, 396.56f))
        ];
    }

    private static Vector2 HighSpeedRingStartWorldXZ(float metresPerSvgUnit)
    {
        return new Vector2(1109.0f, 198.69f) * metresPerSvgUnit;
    }

    private static Vector2 EvaluateHighSpeedRingSegment(HighSpeedRingPathSegment segment, float t)
    {
        t = MathHelper.Clamp(t, 0f, 1f);
        if (segment.Kind == HighSpeedRingPathSegmentKind.Line)
        {
            return Vector2.Lerp(segment.P0, segment.P3, t);
        }

        float inverse = 1f - t;
        return inverse * inverse * inverse * segment.P0 +
               3f * inverse * inverse * t * segment.P1 +
               3f * inverse * t * t * segment.P2 +
               t * t * t * segment.P3;
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

    private static float FindDistanceAlongPolyline(IReadOnlyList<Vector2> points, IReadOnlyList<float> cumulativeDistances, Vector2 target)
    {
        float bestDistance = 0f;
        float bestError = float.MaxValue;
        for (int i = 0; i < points.Count; i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[(i + 1) % points.Count];
            SegmentProjection projection = ProjectPointToSegment(target, a, b);
            if (projection.Distance >= bestError)
            {
                continue;
            }

            bestError = projection.Distance;
            bestDistance = cumulativeDistances[i] + Vector2.Distance(a, b) * projection.T;
        }

        return bestDistance;
    }

    private static Vector2 SamplePolylineAtDistance(IReadOnlyList<Vector2> points, IReadOnlyList<float> cumulativeDistances, float distance)
    {
        if (points.Count == 0)
        {
            return Vector2.Zero;
        }

        float loopLength = cumulativeDistances[^1];
        if (loopLength <= 0.001f)
        {
            return points[0];
        }

        distance %= loopLength;
        if (distance < 0f)
        {
            distance += loopLength;
        }

        int segmentIndex = 0;
        for (int i = 0; i < points.Count; i++)
        {
            if (distance <= cumulativeDistances[i + 1])
            {
                segmentIndex = i;
                break;
            }
        }

        float segmentStart = cumulativeDistances[segmentIndex];
        float segmentEnd = cumulativeDistances[segmentIndex + 1];
        float t = MathHelper.Clamp((distance - segmentStart) / MathF.Max(0.001f, segmentEnd - segmentStart), 0f, 1f);
        return Vector2.Lerp(points[segmentIndex], points[(segmentIndex + 1) % points.Count], t);
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

        if (layout != TrackLayout.HighSpeedRing)
        {
            return centerLine.Count * 3 / 4;
        }

        Vector2 start = HighSpeedRingStartWorldXZ(0.934466258271f);
        int bestIndex = 0;
        float bestScore = float.MaxValue;
        for (int i = 0; i < centerLine.Count; i++)
        {
            Vector3 point = centerLine[i];
            float score = Vector2.DistanceSquared(new Vector2(point.X, point.Z), start);
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

    private static int Hash(int x, int y)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            return (h ^ (h >> 16)) & int.MaxValue;
        }
    }

    private readonly record struct SegmentProjection(float Distance, float T, Vector2 Point);

    private readonly record struct ProfilePoint(float Progress, float Value);

    private enum HighSpeedRingPathSegmentKind
    {
        Line,
        Cubic
    }

    private readonly record struct HighSpeedRingPathSegment(
        HighSpeedRingPathSegmentKind Kind,
        Vector2 P0,
        Vector2 P1,
        Vector2 P2,
        Vector2 P3)
    {
        public static HighSpeedRingPathSegment Line(Vector2 start, Vector2 end)
        {
            return new HighSpeedRingPathSegment(HighSpeedRingPathSegmentKind.Line, start, start, end, end);
        }

        public static HighSpeedRingPathSegment Cubic(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
        {
            return new HighSpeedRingPathSegment(HighSpeedRingPathSegmentKind.Cubic, p0, p1, p2, p3);
        }
    }

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
