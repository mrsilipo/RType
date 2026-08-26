using System.Text.Json;
using System.Text.Json.Serialization;
using RType.World;

namespace RType.Data;

public static class TrackDefinitionFileLoader
{
    public const string DefaultTrackDirectory = "Data/Tracks";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private static readonly JsonSerializerOptions SaveJsonOptions = new(JsonOptions)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static TrackDefinition[] LoadCatalog(string directory, IEnumerable<TrackDefinition> builtIns)
    {
        List<TrackDefinition> tracks = [];
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);

        foreach (TrackDefinitionFile file in LoadFiles(directory).OrderBy(file => file.SortOrder).ThenBy(file => file.Definition.DisplayName))
        {
            if (ids.Add(file.Definition.Id))
            {
                tracks.Add(file.Definition);
            }
        }

        foreach (TrackDefinition builtIn in builtIns)
        {
            if (ids.Add(builtIn.Id))
            {
                tracks.Add(builtIn);
            }
        }

        return tracks.ToArray();
    }

    public static TrackDefinitionFile[] LoadFiles(string directory)
    {
        string? resolvedDirectory = ResolveDirectoryPath(directory);
        if (resolvedDirectory is null)
        {
            return [];
        }

        List<TrackDefinitionFile> files = [];
        foreach (string path in Directory.EnumerateFiles(resolvedDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                files.Add(LoadFile(path));
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
            {
                Console.Error.WriteLine($"Skipping track definition '{path}': {exception.Message}");
            }
        }

        return files.ToArray();
    }

    public static TrackDefinitionFile LoadFile(string path)
    {
        string resolvedPath = ResolveFilePath(path);
        using FileStream stream = File.OpenRead(resolvedPath);
        TrackDefinitionFileDto dto = JsonSerializer.Deserialize<TrackDefinitionFileDto>(stream, JsonOptions)
                                     ?? throw new InvalidDataException("Track definition JSON was empty.");

        string id = RequireText(dto.Id, "id");
        string displayName = RequireText(dto.DisplayName, "displayName");
        float roadWidthMeters = PositiveOrDefault(dto.RoadWidthMeters, 12f);
        int lengthMeters = Math.Max(1, dto.LengthMeters);
        int longestStraightMeters = Math.Max(0, dto.LongestStraightMeters);
        float elevationDifferenceMeters = Math.Max(0f, dto.ElevationDifferenceMeters);
        float sectorOne = 1f / 3f;
        float sectorTwo = 2f / 3f;
        if (dto.SectorMarkers is { Length: >= 2 })
        {
            sectorOne = Math.Clamp(dto.SectorMarkers[0], 0.01f, 0.98f);
            sectorTwo = Math.Clamp(dto.SectorMarkers[1], sectorOne + 0.01f, 0.99f);
        }

        TrackControlPoint[]? controlPoints = ReadControlPoints(dto.ControlPoints);
        TrackBankPoint[]? bankProfile = ReadBankProfile(dto.BankProfile);
        TrackSegmentShape[]? segmentShapes = ReadSegmentShapes(dto.Segments, controlPoints?.Length ?? 0);
        TrackWidthPoint[]? widthPoints = ReadWidthPoints(dto.WidthPoints, controlPoints?.Length ?? 0);

        TrackDefinition definition = new(
            id,
            displayName,
            ParseLayout(dto.Layout),
            roadWidthMeters * 0.5f,
            PositiveOrDefault(dto.TerrainWidthMeters, 1000f),
            PositiveOrDefault(dto.TerrainDepthMeters, 700f),
            lengthMeters,
            longestStraightMeters,
            elevationDifferenceMeters,
            sectorOne,
            sectorTwo,
            controlPoints,
            bankProfile,
            segmentShapes,
            widthPoints);

        return new TrackDefinitionFile(definition, dto.SortOrder, resolvedPath);
    }

    public static void SaveFile(TrackDefinition definition, int sortOrder, string path)
    {
        string resolvedPath = ResolveWritableFilePath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(resolvedPath) ?? Environment.CurrentDirectory);

        TrackDefinitionFileDto dto = new()
        {
            Schema = "../Schemas/track_definition_v1.json",
            FormatVersion = 1,
            SortOrder = sortOrder,
            Id = definition.Id,
            DisplayName = definition.DisplayName,
            Layout = ToJsonLayout(definition.Layout),
            RoadWidthMeters = definition.RoadHalfWidthMeters * 2f,
            TerrainWidthMeters = definition.TerrainWidthMeters,
            TerrainDepthMeters = definition.TerrainDepthMeters,
            LengthMeters = definition.LengthMeters,
            LongestStraightMeters = definition.LongestStraightMeters,
            ElevationDifferenceMeters = definition.ElevationDifferenceMeters,
            SectorMarkers = definition.SectorMarkers.ToArray(),
            ControlPoints = definition.ControlPoints?.Select(point => new TrackControlPointDto
            {
                X = point.X,
                Z = point.Z,
                ElevationMeters = point.ElevationMeters
            }).ToArray(),
            BankProfile = definition.BankProfileDegrees?.Select(point => new TrackBankPointDto
            {
                Progress = point.Progress,
                BankDegrees = point.BankDegrees
            }).ToArray(),
            Segments = definition.SegmentShapes?.Select(segment => new TrackSegmentDto
            {
                FromControlPoint = segment.FromControlPoint,
                Shape = ToJsonSegmentShape(segment.Shape)
            }).ToArray(),
            WidthPoints = definition.WidthPoints?.Select(point => new TrackWidthPointDto
            {
                ControlPoint = point.ControlPoint,
                LeftRoadWidthMeters = point.LeftRoadWidthMeters,
                RightRoadWidthMeters = point.RightRoadWidthMeters,
                LeftGrassWidthMeters = point.LeftGrassWidthMeters,
                RightGrassWidthMeters = point.RightGrassWidthMeters,
                LeftWallOffsetMeters = point.LeftWallOffsetMeters,
                RightWallOffsetMeters = point.RightWallOffsetMeters
            }).ToArray()
        };

        using FileStream stream = File.Create(resolvedPath);
        JsonSerializer.Serialize(stream, dto, SaveJsonOptions);
    }

    private static TrackControlPoint[]? ReadControlPoints(TrackControlPointDto[]? points)
    {
        if (points is null || points.Length == 0)
        {
            return null;
        }

        if (points.Length < 4)
        {
            throw new InvalidDataException("A custom spline track needs at least four control points.");
        }

        TrackControlPoint[] controlPoints = new TrackControlPoint[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            controlPoints[i] = new TrackControlPoint(points[i].X, points[i].Z, points[i].ElevationMeters);
        }

        return controlPoints;
    }

    private static TrackBankPoint[]? ReadBankProfile(TrackBankPointDto[]? points)
    {
        if (points is null || points.Length == 0)
        {
            return null;
        }

        return points
            .Select(point => new TrackBankPoint(Math.Clamp(point.Progress, 0f, 1f), point.BankDegrees))
            .OrderBy(point => point.Progress)
            .ToArray();
    }

    private static TrackSegmentShape[]? ReadSegmentShapes(TrackSegmentDto[]? segments, int controlPointCount)
    {
        if (segments is null || segments.Length == 0 || controlPointCount <= 0)
        {
            return null;
        }

        TrackSegmentShape[] shapes = new TrackSegmentShape[controlPointCount];
        for (int i = 0; i < shapes.Length; i++)
        {
            shapes[i] = new TrackSegmentShape(i, TrackSegmentShapeKind.Curve);
        }

        foreach (TrackSegmentDto segment in segments)
        {
            if (segment.FromControlPoint < 0 || segment.FromControlPoint >= controlPointCount)
            {
                continue;
            }

            shapes[segment.FromControlPoint] = new TrackSegmentShape(
                segment.FromControlPoint,
                ParseSegmentShape(segment.Shape));
        }

        return shapes;
    }

    private static TrackWidthPoint[]? ReadWidthPoints(TrackWidthPointDto[]? points, int controlPointCount)
    {
        if (points is null || points.Length == 0 || controlPointCount <= 0)
        {
            return null;
        }

        TrackWidthPoint[] widths = new TrackWidthPoint[controlPointCount];
        for (int i = 0; i < widths.Length; i++)
        {
            widths[i] = new TrackWidthPoint(i, 0f, 0f, 0f, 0f, 0f, 0f);
        }

        foreach (TrackWidthPointDto point in points)
        {
            if (point.ControlPoint < 0 || point.ControlPoint >= controlPointCount)
            {
                continue;
            }

            widths[point.ControlPoint] = new TrackWidthPoint(
                point.ControlPoint,
                Math.Max(0f, point.LeftRoadWidthMeters > 0f ? point.LeftRoadWidthMeters : point.RoadWidthMeters * 0.5f),
                Math.Max(0f, point.RightRoadWidthMeters > 0f ? point.RightRoadWidthMeters : point.RoadWidthMeters * 0.5f),
                Math.Max(0f, point.LeftGrassWidthMeters > 0f ? point.LeftGrassWidthMeters : point.GrassWidthMeters),
                Math.Max(0f, point.RightGrassWidthMeters > 0f ? point.RightGrassWidthMeters : point.GrassWidthMeters),
                Math.Max(0f, point.LeftWallOffsetMeters > 0f ? point.LeftWallOffsetMeters : point.WallOffsetMeters),
                Math.Max(0f, point.RightWallOffsetMeters > 0f ? point.RightWallOffsetMeters : point.WallOffsetMeters));
        }

        return widths;
    }

    private static TrackLayout ParseLayout(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return TrackLayout.CustomSpline;
        }

        string normalized = value.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        if (string.Equals(normalized, "velocityloop", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "highspeedringinspired", StringComparison.OrdinalIgnoreCase))
        {
            return TrackLayout.HighSpeedRing;
        }

        foreach (TrackLayout layout in Enum.GetValues<TrackLayout>())
        {
            if (string.Equals(layout.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return layout;
            }
        }

        throw new InvalidDataException($"Unknown track layout '{value}'.");
    }

    private static TrackSegmentShapeKind ParseSegmentShape(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return TrackSegmentShapeKind.Curve;
        }

        string normalized = value.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        return normalized switch
        {
            string shape when string.Equals(shape, "straight", StringComparison.OrdinalIgnoreCase) => TrackSegmentShapeKind.Straight,
            string shape when string.Equals(shape, "line", StringComparison.OrdinalIgnoreCase) => TrackSegmentShapeKind.Straight,
            string shape when string.Equals(shape, "curve", StringComparison.OrdinalIgnoreCase) => TrackSegmentShapeKind.Curve,
            string shape when string.Equals(shape, "curved", StringComparison.OrdinalIgnoreCase) => TrackSegmentShapeKind.Curve,
            _ => throw new InvalidDataException($"Unknown track segment shape '{value}'.")
        };
    }

    private static string ToJsonLayout(TrackLayout layout)
    {
        return layout switch
        {
            TrackLayout.HighSpeedRing => "highSpeedRing",
            TrackLayout.LakesidePark => "lakesidePark",
            _ => "customSpline"
        };
    }

    private static string ToJsonSegmentShape(TrackSegmentShapeKind shape)
    {
        return shape == TrackSegmentShapeKind.Straight ? "straight" : "curve";
    }

    private static string RequireText(string? value, string propertyName)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new InvalidDataException($"Missing required track property '{propertyName}'.");
    }

    private static float PositiveOrDefault(float value, float fallback)
    {
        return value > 0.001f ? value : fallback;
    }

    private static string ResolveFilePath(string path)
    {
        if (Path.IsPathRooted(path) && File.Exists(path))
        {
            return path;
        }

        string[] candidates =
        [
            Path.Combine(Environment.CurrentDirectory, path),
            Path.Combine(AppContext.BaseDirectory, path)
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Track definition JSON was not found: {path}", path);
    }

    private static string? ResolveDirectoryPath(string path)
    {
        if (Path.IsPathRooted(path) && Directory.Exists(path))
        {
            return path;
        }

        string[] candidates =
        [
            Path.Combine(Environment.CurrentDirectory, path),
            Path.Combine(AppContext.BaseDirectory, path)
        ];

        return candidates.FirstOrDefault(Directory.Exists);
    }

    private static string ResolveWritableFilePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, path));
    }

    private sealed class TrackDefinitionFileDto
    {
        [JsonPropertyName("$schema")]
        public string? Schema { get; set; }

        public int FormatVersion { get; set; } = 1;

        public int SortOrder { get; set; } = 1000;

        public string? Id { get; set; }

        public string? DisplayName { get; set; }

        public string? Layout { get; set; }

        public float RoadWidthMeters { get; set; }

        public float TerrainWidthMeters { get; set; }

        public float TerrainDepthMeters { get; set; }

        public int LengthMeters { get; set; }

        public int LongestStraightMeters { get; set; }

        public float ElevationDifferenceMeters { get; set; }

        public float[]? SectorMarkers { get; set; }

        public TrackControlPointDto[]? ControlPoints { get; set; }

        public TrackBankPointDto[]? BankProfile { get; set; }

        public TrackSegmentDto[]? Segments { get; set; }

        public TrackWidthPointDto[]? WidthPoints { get; set; }
    }

    private sealed class TrackControlPointDto
    {
        public float X { get; set; }

        public float Z { get; set; }

        public float ElevationMeters { get; set; }
    }

    private sealed class TrackBankPointDto
    {
        public float Progress { get; set; }

        public float BankDegrees { get; set; }
    }

    private sealed class TrackSegmentDto
    {
        public int FromControlPoint { get; set; }

        public string? Shape { get; set; }
    }

    private sealed class TrackWidthPointDto
    {
        public int ControlPoint { get; set; }

        public float RoadWidthMeters { get; set; }

        public float LeftRoadWidthMeters { get; set; }

        public float RightRoadWidthMeters { get; set; }

        public float GrassWidthMeters { get; set; }

        public float LeftGrassWidthMeters { get; set; }

        public float RightGrassWidthMeters { get; set; }

        public float WallOffsetMeters { get; set; }

        public float LeftWallOffsetMeters { get; set; }

        public float RightWallOffsetMeters { get; set; }
    }
}

public readonly record struct TrackDefinitionFile(TrackDefinition Definition, int SortOrder, string SourcePath);
