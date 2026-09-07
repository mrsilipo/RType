using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RType.Data;
using RType.Rendering;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

internal sealed class AuthoredSurfaceProbe : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private bool _ran;

    private AuthoredSurfaceProbe()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 64,
            PreferredBackBufferHeight = 64
        };
        IsMouseVisible = false;
    }

    public static void RunProbeGame()
    {
        using AuthoredSurfaceProbe game = new();
        game.Run();
    }

    protected override void Update(GameTime gameTime)
    {
        if (_ran)
        {
            Exit();
            return;
        }

        _ran = true;
        RunProbe();
        Exit();
    }

    private void RunProbe()
    {
        TrackDefinition definition = TrackDefinitionFileLoader
            .LoadCatalog(TrackDefinitionFileLoader.DefaultTrackDirectory, TrackCatalog.All)
            .First(track => track.Id.Equals("high_speed_ring", StringComparison.OrdinalIgnoreCase));
        using GeneratedTextures textures = GeneratedTextures.Create(GraphicsDevice);
        SurfaceLibrary surfaces = SurfaceLibraryLoader.Load(GameLaunchOptions.DefaultSurfaceDefinitionPath);
        using TrackScene track = TrackScene.Create(
            GraphicsDevice,
            textures,
            definition,
            reverse: false,
            surfaces,
            TrackVisualMode.Authored);

        Console.WriteLine("Authored driveable surface probe:");
        if (track.AuthoredSurfaceSampler is not AuthoredTrackSurfaceSampler sampler || !sampler.HasDriveableSurface)
        {
            Console.WriteLine("  FAIL: no authored `_Driveable` nodes were found in the High Speed Ring GLB.");
            return;
        }

        AuthoredTrackSurfaceStats stats = sampler.Stats;
        Console.WriteLine($"  driveable nodes: {stats.DriveableNodeCount}");
        Console.WriteLine($"  triangles: {stats.TriangleCount}");
        Console.WriteLine($"  grid: {stats.GridWidth}x{stats.GridDepth}, cell {stats.CellSizeMeters:0.#}m");
        Console.WriteLine($"  occupied cells: {stats.OccupiedCellCount}");
        Console.WriteLine($"  avg candidates/occupied cell: {stats.AverageTrianglesPerOccupiedCell:0.##}");
        Console.WriteLine($"  worst candidates in one cell: {stats.WorstCaseTrianglesInCell}");
        Console.WriteLine($"  elevation range: {stats.Bounds.Min.Y:0.###} to {stats.Bounds.Max.Y:0.###}");

        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(GameLaunchOptions.DefaultVehiclePath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(GameLaunchOptions.DefaultSimulationEngineDefinitionPath);
        VehicleAxleGeometry geometry = VehicleAxleGeometry.FromParameters(parameters);
        Vector3 vehicleStart = track.ResolveVehicleStartPosition(parameters.BodyLengthMeters);
        Vector2 poleCenter = new(track.StartPosition.X, track.StartPosition.Z);
        Vector2 center = new(vehicleStart.X, vehicleStart.Z);
        Vector2 forward = new(MathF.Sin(track.StartHeadingRadians), MathF.Cos(track.StartHeadingRadians));
        Vector2 right = new(MathF.Cos(track.StartHeadingRadians), -MathF.Sin(track.StartHeadingRadians));
        float frontHalfTrack = geometry.FrontTrackMeters * 0.5f;
        float rearHalfTrack = geometry.RearTrackMeters * 0.5f;
        float queryY = MathF.Max(track.StartPosition.Y, vehicleStart.Y) + 3.0f;

        Console.WriteLine(
            $"  pole marker front-center=({track.StartPosition.X:0.###}, {track.StartPosition.Y:0.###}, {track.StartPosition.Z:0.###}) " +
            $"vehicle center spawn=({vehicleStart.X:0.###}, {vehicleStart.Y:0.###}, {vehicleStart.Z:0.###}) " +
            $"heading={MathHelper.ToDegrees(track.StartHeadingRadians):0.##}deg bodyLength={parameters.BodyLengthMeters:0.###}m");
        Sample("pole-marker", sampler, new Vector3(poleCenter.X, queryY, poleCenter.Y));
        Sample("vehicle-center-spawn", sampler, new Vector3(center.X, queryY, center.Y));
        Sample("front-left", sampler, ToQuery(center - right * frontHalfTrack + forward * geometry.CgToFrontAxleMeters, queryY));
        Sample("front-right", sampler, ToQuery(center + right * frontHalfTrack + forward * geometry.CgToFrontAxleMeters, queryY));
        Sample("rear-left", sampler, ToQuery(center - right * rearHalfTrack - forward * geometry.CgToRearAxleMeters, queryY));
        Sample("rear-right", sampler, ToQuery(center + right * rearHalfTrack - forward * geometry.CgToRearAxleMeters, queryY));
        SamplePose("spawn-pose", sampler, center, forward, right, geometry, queryY);

        foreach ((string label, AuthoredDriveableTriangle triangle) in SelectRepresentativeTriangles(sampler))
        {
            Vector3 centroid = (triangle.A + triangle.B + triangle.C) / 3f;
            Sample(label, sampler, centroid + Vector3.Up * 3f);
            SamplePose(label + "-pose", sampler, new Vector2(centroid.X, centroid.Z), forward, right, geometry, centroid.Y + 3f);
        }

        Sample("intentional-miss", sampler, new Vector3(stats.Bounds.Max.X + 50f, stats.Bounds.Max.Y + 5f, stats.Bounds.Max.Z + 50f));
        RunSpawnDriveProbe(track, sampler, parameters, engine);
    }

    private static void RunSpawnDriveProbe(
        TrackScene track,
        AuthoredTrackSurfaceSampler sampler,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine)
    {
        Vector3 vehicleStart = track.ResolveVehicleStartPosition(parameters.BodyLengthMeters);
        ClassicFourWheelVehicleSimulator simulator = new(track, vehicleStart, track.StartHeadingRadians, parameters, engine);
        simulator.InitializePhysicalChassisForProbe();

        float dt = engine.Timing.FixedDeltaSeconds;
        int ticks = Math.Max(1, (int)MathF.Ceiling(4.0f / MathF.Max(0.001f, dt)));
        bool firstMissFound = false;
        float minY = simulator.State.Position.Y;
        float maxAbsPitch = 0f;
        float maxAbsRoll = 0f;
        int normalContactSamples = 0;
        int recoverySamples = 0;
        int missSamples = 0;
        int bumpStopSamples = 0;
        float maxRecoveredPenetration = 0f;
        for (int i = 0; i < ticks; i++)
        {
            simulator.Update(new VehicleInput(0.45f, 0f, 0f), dt);
            minY = MathF.Min(minY, simulator.State.Position.Y);
            maxAbsPitch = MathF.Max(maxAbsPitch, MathF.Abs(simulator.State.BodyPitchRadians));
            maxAbsRoll = MathF.Max(maxAbsRoll, MathF.Abs(simulator.State.BodyRollRadians));
            normalContactSamples += simulator.State.AuthoredSuspensionNormalContactCount;
            recoverySamples += simulator.State.AuthoredSuspensionAntiTunnelRecoveryCount;
            missSamples += simulator.State.AuthoredSuspensionMissCount;
            bumpStopSamples += simulator.State.AuthoredSuspensionBumpStopContactCount;
            maxRecoveredPenetration = MathF.Max(maxRecoveredPenetration, simulator.State.AuthoredSuspensionMaxRecoveredPenetrationMeters);

            if (!firstMissFound &&
                (!simulator.State.FrontLeftShadowSuspension.HasContact ||
                 !simulator.State.FrontRightShadowSuspension.HasContact ||
                 !simulator.State.RearLeftShadowSuspension.HasContact ||
                 !simulator.State.RearRightShadowSuspension.HasContact))
            {
                firstMissFound = true;
                Console.WriteLine(
                    $"  drive-probe FIRST MISS t={(i + 1) * dt:0.###}s " +
                    $"pos=({simulator.State.Position.X:0.###},{simulator.State.Position.Y:0.###},{simulator.State.Position.Z:0.###}) " +
                    $"speed={simulator.State.SpeedMetersPerSecond:0.###}m/s");
                PrintCorner("FL", simulator.State.FrontLeftShadowSuspension);
                PrintCorner("FR", simulator.State.FrontRightShadowSuspension);
                PrintCorner("RL", simulator.State.RearLeftShadowSuspension);
                PrintCorner("RR", simulator.State.RearRightShadowSuspension);
            }
        }

        Console.WriteLine(
            $"  drive-probe final t={ticks * dt:0.###}s " +
            $"pos=({simulator.State.Position.X:0.###},{simulator.State.Position.Y:0.###},{simulator.State.Position.Z:0.###}) " +
            $"speed={simulator.State.SpeedMetersPerSecond:0.###}m/s minY={minY:0.###} " +
            $"maxPitch={MathHelper.ToDegrees(maxAbsPitch):0.##}deg maxRoll={MathHelper.ToDegrees(maxAbsRoll):0.##}deg " +
            $"contacts={ContactSummary(simulator.State)} " +
            $"normalSamples={normalContactSamples} " +
            $"recoveredSamples={recoverySamples} " +
            $"missSamples={missSamples} " +
            $"bumpStopSamples={bumpStopSamples} " +
            $"maxRecovery={maxRecoveredPenetration:0.###}m");

        RunRecoveryCase(track, parameters, engine, "road beyond max droop", Vector3.Up * 2.0f);
        RunRecoveryCase(track, parameters, engine, "large start below road", Vector3.Down * 2.0f);
        RunRecoveryCase(track, parameters, engine, "small deliberate penetration", Vector3.Down * 0.45f);
        RunLongSoakDriveProbe(track, sampler, parameters, engine);
    }

    private static void RunLongSoakDriveProbe(
        TrackScene track,
        AuthoredTrackSurfaceSampler sampler,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine)
    {
        Vector3 vehicleStart = track.ResolveVehicleStartPosition(parameters.BodyLengthMeters);
        ClassicFourWheelVehicleSimulator simulator = new(track, vehicleStart, track.StartHeadingRadians, parameters, engine);
        simulator.InitializePhysicalChassisForProbe();

        float dt = engine.Timing.FixedDeltaSeconds;
        int ticks = Math.Max(1, (int)MathF.Ceiling(75.0f / MathF.Max(0.001f, dt)));
        ContactSoakStats stats = new();
        ShadowSuspensionCornerState previousFl = simulator.State.FrontLeftShadowSuspension;
        ShadowSuspensionCornerState previousFr = simulator.State.FrontRightShadowSuspension;
        ShadowSuspensionCornerState previousRl = simulator.State.RearLeftShadowSuspension;
        ShadowSuspensionCornerState previousRr = simulator.State.RearRightShadowSuspension;
        Vector3 previousPosition = simulator.State.Position;

        for (int i = 0; i < ticks; i++)
        {
            float time = i * dt;
            VehicleInput input = CreateSoakDriverInput(track, simulator.State, time);
            simulator.Update(input, dt);
            stats.RecordStep(
                time + dt,
                simulator.State,
                previousPosition,
                previousFl,
                previousFr,
                previousRl,
                previousRr);

            previousPosition = simulator.State.Position;
            previousFl = simulator.State.FrontLeftShadowSuspension;
            previousFr = simulator.State.FrontRightShadowSuspension;
            previousRl = simulator.State.RearLeftShadowSuspension;
            previousRr = simulator.State.RearRightShadowSuspension;
        }

        Console.WriteLine("  long-soak authored surface drive:");
        Console.WriteLine($"    duration={ticks * dt:0.###}s steps={ticks} wheelSamples={ticks * 4}");
        Console.WriteLine(
            $"    contacts normal={stats.NormalContacts} recovered={stats.AntiTunnelRecoveries} " +
            $"bumpStop={stats.BumpStopContacts} miss={stats.Misses}");
        Console.WriteLine(
            $"    longestMiss FL={stats.LongestMissFl} FR={stats.LongestMissFr} " +
            $"RL={stats.LongestMissRl} RR={stats.LongestMissRr}");
        Console.WriteLine(
            $"    maxRecovery={stats.MaxRecoveryPenetration:0.###}m maxCompression={stats.MaxCompression:0.###}m " +
            $"maxDroop={stats.MaxDroop:0.###}m");
        Console.WriteLine(
            $"    chassisY={stats.MinChassisY:0.###}..{stats.MaxChassisY:0.###} " +
            $"roadY={stats.MinRoadY:0.###}..{stats.MaxRoadY:0.###} " +
            $"maxPitch={MathHelper.ToDegrees(stats.MaxAbsPitch):0.##}deg " +
            $"maxRoll={MathHelper.ToDegrees(stats.MaxAbsRoll):0.##}deg " +
            $"maxVerticalVelocity={stats.MaxAbsVerticalVelocity:0.###}m/s");
        Console.WriteLine(
            $"    recoveryWheels={FormatSet(stats.RecoveryWheels)} missWheels={FormatSet(stats.MissWheels)} " +
            $"bumpStopWheels={FormatSet(stats.BumpStopWheels)}");
        if (!string.IsNullOrWhiteSpace(stats.FirstUnusualEvent))
        {
            Console.WriteLine("    first unusual event:");
            foreach (string line in stats.FirstUnusualEvent.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                Console.WriteLine("      " + line);
            }
        }
        else
        {
            Console.WriteLine("    first unusual event: none");
        }

        AuditFirstMissCoverage(sampler, stats);
    }

    private static VehicleInput CreateSoakDriverInput(TrackScene track, VehicleState state, float time)
    {
        TrackProgress progress = track.GetProgress(state.Position);
        Vector2 currentForward = new(state.Forward.X, state.Forward.Z);
        if (currentForward.LengthSquared() <= 0.0001f)
        {
            currentForward = Vector2.UnitY;
        }
        else
        {
            currentForward.Normalize();
        }

        Vector2 targetForward = progress.Forward.LengthSquared() > 0.0001f
            ? Vector2.Normalize(progress.Forward)
            : currentForward;
        if (Vector2.Dot(currentForward, targetForward) < 0f)
        {
            targetForward = -targetForward;
        }

        float headingError = MathF.Atan2(
            Cross(currentForward, targetForward),
            Vector2.Dot(currentForward, targetForward));
        float targetOffset = MathF.Sin(progress.NormalizedDistance * MathF.Tau * 5.0f + time * 0.35f) * 2.2f;
        float lateralError = progress.SignedDistanceFromCenterMeters - targetOffset;
        float steer = MathHelper.Clamp(headingError * 1.35f - lateralError * 0.045f, -1f, 1f);
        float targetSpeed = time switch
        {
            < 10f => 13f,
            < 25f => 24f,
            < 45f => 34f,
            _ => 42f
        };
        float speed = state.SpeedMetersPerSecond;
        float throttle = speed < targetSpeed ? 0.78f : 0.12f;
        float brake = speed > targetSpeed + 5f ? 0.18f : 0f;
        return new VehicleInput(throttle, brake, steer);
    }

    private static void AuditFirstMissCoverage(AuthoredTrackSurfaceSampler sampler, ContactSoakStats stats)
    {
        const float minX = 1020f;
        const float maxX = 1036f;
        const float minZ = 204f;
        const float maxZ = 220f;
        Vector2 failure = stats.FirstFailureWheelPosition;
        Vector2 previousContact = stats.FirstFailurePreviousContactPosition;
        IReadOnlyList<AuthoredDriveableTriangle> localTriangles = sampler.Triangles
            .Where(triangle => TriangleOverlapsRegion(triangle, minX, maxX, minZ, maxZ))
            .ToArray();

        Console.WriteLine("  first-miss local driveable coverage audit:");
        Console.WriteLine($"    region X={minX:0.###}..{maxX:0.###} Z={minZ:0.###}..{maxZ:0.###}");
        Console.WriteLine($"    local driveable triangles={localTriangles.Count}");
        foreach (IGrouping<string, AuthoredDriveableTriangle> group in localTriangles.GroupBy(triangle => triangle.SourceName).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            float groupMinX = group.Min(triangle => MathF.Min(triangle.A.X, MathF.Min(triangle.B.X, triangle.C.X)));
            float groupMaxX = group.Max(triangle => MathF.Max(triangle.A.X, MathF.Max(triangle.B.X, triangle.C.X)));
            float groupMinZ = group.Min(triangle => MathF.Min(triangle.A.Z, MathF.Min(triangle.B.Z, triangle.C.Z)));
            float groupMaxZ = group.Max(triangle => MathF.Max(triangle.A.Z, MathF.Max(triangle.B.Z, triangle.C.Z)));
            float groupMinY = group.Min(triangle => MathF.Min(triangle.A.Y, MathF.Min(triangle.B.Y, triangle.C.Y)));
            float groupMaxY = group.Max(triangle => MathF.Max(triangle.A.Y, MathF.Max(triangle.B.Y, triangle.C.Y)));
            Console.WriteLine(
                $"    source={group.Key} tris={group.Count()} " +
                $"boundsX={groupMinX:0.###}..{groupMaxX:0.###} boundsZ={groupMinZ:0.###}..{groupMaxZ:0.###} " +
                $"elev={groupMinY:0.###}..{groupMaxY:0.###}");
        }

        bool asphaltExists = localTriangles.Any(triangle => triangle.SourceName.Equals("Track_Asphalt_Driveable", StringComparison.Ordinal));
        bool terrainInnerExists = localTriangles.Any(triangle => triangle.SourceName.Equals("Terrain_Inner_Driveable", StringComparison.Ordinal));
        Console.WriteLine($"    Track_Asphalt_Driveable in region: {asphaltExists}");
        Console.WriteLine($"    Terrain_Inner_Driveable in region: {terrainInnerExists}");

        if (failure != Vector2.Zero)
        {
            FindNearestTriangle2D(sampler.Triangles, failure, out AuthoredDriveableTriangle nearest, out float nearestDistance);
            bool directHit = sampler.TryGetContact(new Vector3(failure.X, 5f, failure.Y), 16f, out TrackSurfaceContact directContact);
            Console.WriteLine(
                $"    failed wheel X/Z=({failure.X:0.###},{failure.Y:0.###}) directVerticalHit={directHit} " +
                (directHit
                    ? $"hitSource={directContact.SourceName} hitTri={directContact.TriangleIndex} hitY={directContact.Position.Y:0.###}"
                    : $"nearestSource={nearest.SourceName} nearestTri={nearest.TriangleIndex} nearestXzDistance={nearestDistance:0.###}m"));
        }

        ScanLocalCoverageGrid(sampler, minX, maxX, minZ, maxZ);
        string svgPath = WriteCoverageSvg(localTriangles, stats.PathSamples, failure, previousContact, minX, maxX, minZ, maxZ);
        Console.WriteLine($"    debugSvg={svgPath}");
    }

    private static void ScanLocalCoverageGrid(AuthoredTrackSurfaceSampler sampler, float minX, float maxX, float minZ, float maxZ)
    {
        const float step = 0.25f;
        int samples = 0;
        int misses = 0;
        int longestRun = 0;
        for (float z = minZ; z <= maxZ + 0.0001f; z += step)
        {
            int run = 0;
            for (float x = minX; x <= maxX + 0.0001f; x += step)
            {
                samples++;
                bool hit = sampler.TryGetContact(new Vector3(x, 5f, z), 16f, out _);
                if (!hit)
                {
                    misses++;
                    run++;
                    longestRun = Math.Max(longestRun, run);
                }
                else
                {
                    run = 0;
                }
            }
        }

        Console.WriteLine(
            $"    grid coverage samples={samples} misses={misses} " +
            $"missRatio={(samples > 0 ? misses / (float)samples : 0f):0.###} longestContiguousXMissRun={longestRun} cells at {step:0.##}m");
    }

    private static bool TriangleOverlapsRegion(AuthoredDriveableTriangle triangle, float minX, float maxX, float minZ, float maxZ)
    {
        float triMinX = MathF.Min(triangle.A.X, MathF.Min(triangle.B.X, triangle.C.X));
        float triMaxX = MathF.Max(triangle.A.X, MathF.Max(triangle.B.X, triangle.C.X));
        float triMinZ = MathF.Min(triangle.A.Z, MathF.Min(triangle.B.Z, triangle.C.Z));
        float triMaxZ = MathF.Max(triangle.A.Z, MathF.Max(triangle.B.Z, triangle.C.Z));
        return triMaxX >= minX && triMinX <= maxX && triMaxZ >= minZ && triMinZ <= maxZ;
    }

    private static void FindNearestTriangle2D(
        IReadOnlyList<AuthoredDriveableTriangle> triangles,
        Vector2 point,
        out AuthoredDriveableTriangle nearest,
        out float distance)
    {
        nearest = default;
        distance = float.PositiveInfinity;
        foreach (AuthoredDriveableTriangle triangle in triangles)
        {
            float candidate = DistancePointTriangle2D(point, triangle);
            if (candidate < distance)
            {
                distance = candidate;
                nearest = triangle;
            }
        }
    }

    private static float DistancePointTriangle2D(Vector2 point, AuthoredDriveableTriangle triangle)
    {
        Vector2 a = new(triangle.A.X, triangle.A.Z);
        Vector2 b = new(triangle.B.X, triangle.B.Z);
        Vector2 c = new(triangle.C.X, triangle.C.Z);
        if (PointInTriangle2D(point, a, b, c))
        {
            return 0f;
        }

        return MathF.Min(
            DistancePointSegment2D(point, a, b),
            MathF.Min(DistancePointSegment2D(point, b, c), DistancePointSegment2D(point, c, a)));
    }

    private static bool PointInTriangle2D(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross(p - b, a - b);
        float d2 = Cross(p - c, b - c);
        float d3 = Cross(p - a, c - a);
        bool hasNeg = d1 < -0.0001f || d2 < -0.0001f || d3 < -0.0001f;
        bool hasPos = d1 > 0.0001f || d2 > 0.0001f || d3 > 0.0001f;
        return !(hasNeg && hasPos);
    }

    private static float DistancePointSegment2D(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lengthSquared = ab.LengthSquared();
        if (lengthSquared <= 0.000001f)
        {
            return Vector2.Distance(point, a);
        }

        float t = MathHelper.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0f, 1f);
        return Vector2.Distance(point, a + ab * t);
    }

    private static string WriteCoverageSvg(
        IReadOnlyList<AuthoredDriveableTriangle> triangles,
        IReadOnlyList<PathSample> pathSamples,
        Vector2 failure,
        Vector2 previousContact,
        float minX,
        float maxX,
        float minZ,
        float maxZ)
    {
        string directory = Path.Combine("Temp", "TrackExport", "HighSpeedRing");
        Directory.CreateDirectory(directory);
        string path = Path.GetFullPath(Path.Combine(directory, "DriveableCoverage_FirstMiss.svg"));
        const int width = 960;
        const int height = 960;
        string Map(Vector2 p)
        {
            float x = (p.X - minX) / MathF.Max(0.001f, maxX - minX) * width;
            float y = height - (p.Y - minZ) / MathF.Max(0.001f, maxZ - minZ) * height;
            return $"{x:0.###},{y:0.###}";
        }

        string ColorFor(string source)
        {
            if (source.Equals("Track_Asphalt_Driveable", StringComparison.Ordinal)) return "#d43d2f";
            if (source.Equals("Terrain_Inner_Driveable", StringComparison.Ordinal)) return "#3d9f53";
            if (source.Equals("Terrain_Outer_Driveable", StringComparison.Ordinal)) return "#2d6cdf";
            return "#9b59b6";
        }

        using StreamWriter writer = new(path);
        writer.WriteLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\">");
        writer.WriteLine("<rect width=\"100%\" height=\"100%\" fill=\"#151515\"/>");
        foreach (AuthoredDriveableTriangle triangle in triangles)
        {
            string points = $"{Map(new Vector2(triangle.A.X, triangle.A.Z))} {Map(new Vector2(triangle.B.X, triangle.B.Z))} {Map(new Vector2(triangle.C.X, triangle.C.Z))}";
            writer.WriteLine($"<polygon points=\"{points}\" fill=\"{ColorFor(triangle.SourceName)}\" fill-opacity=\"0.26\" stroke=\"{ColorFor(triangle.SourceName)}\" stroke-width=\"1\"/>");
        }

        if (pathSamples.Count > 1)
        {
            string pathPoints = string.Join(" ", pathSamples.Select(sample => Map(sample.Center)));
            writer.WriteLine($"<polyline points=\"{pathPoints}\" fill=\"none\" stroke=\"#ffffff\" stroke-width=\"3\" stroke-opacity=\"0.85\"/>");
            foreach (PathSample sample in pathSamples.TakeLast(40))
            {
                writer.WriteLine($"<circle cx=\"{Map(sample.FrontLeft).Split(',')[0]}\" cy=\"{Map(sample.FrontLeft).Split(',')[1]}\" r=\"2.5\" fill=\"#00ffff\"/>");
            }
        }

        if (previousContact != Vector2.Zero)
        {
            string p = Map(previousContact);
            writer.WriteLine($"<circle cx=\"{p.Split(',')[0]}\" cy=\"{p.Split(',')[1]}\" r=\"8\" fill=\"#ffd500\"/>");
        }

        if (failure != Vector2.Zero)
        {
            string p = Map(failure);
            writer.WriteLine($"<circle cx=\"{p.Split(',')[0]}\" cy=\"{p.Split(',')[1]}\" r=\"9\" fill=\"none\" stroke=\"#ff00ff\" stroke-width=\"4\"/>");
        }

        writer.WriteLine("<text x=\"16\" y=\"28\" fill=\"#fff\" font-family=\"monospace\" font-size=\"18\">Driveable coverage: red asphalt, green inner terrain, blue outer terrain, magenta first miss</text>");
        writer.WriteLine("</svg>");
        return path;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.X * b.Y - a.Y * b.X;
    }

    private static string FormatSet(HashSet<string> values)
    {
        return values.Count == 0 ? "none" : string.Join(",", values.OrderBy(value => value, StringComparer.Ordinal));
    }

    private static void RunRecoveryCase(
        TrackScene track,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        string label,
        Vector3 offset)
    {
        Vector3 vehicleStart = track.ResolveVehicleStartPosition(parameters.BodyLengthMeters);
        ClassicFourWheelVehicleSimulator simulator = new(track, vehicleStart, track.StartHeadingRadians, parameters, engine);
        simulator.InitializePhysicalChassisForProbe();
        Vector3 baseline = simulator.State.Position;
        simulator.State.Position = baseline + offset;
        simulator.UpdateShadowSuspensionDiagnosticsForProbe(engine.Timing.FixedDeltaSeconds);

        Console.WriteLine(
            $"  recovery-case {label}: offset=({offset.X:0.###},{offset.Y:0.###},{offset.Z:0.###}) " +
            $"normal={simulator.State.AuthoredSuspensionNormalContactCount} " +
            $"recovered={simulator.State.AuthoredSuspensionAntiTunnelRecoveryCount} " +
            $"miss={simulator.State.AuthoredSuspensionMissCount} " +
            $"bumpStop={simulator.State.AuthoredSuspensionBumpStopContactCount} " +
            $"maxRecovery={simulator.State.AuthoredSuspensionMaxRecoveredPenetrationMeters:0.###}m");
        PrintCorner("FL", simulator.State.FrontLeftShadowSuspension);
        PrintCorner("FR", simulator.State.FrontRightShadowSuspension);
        PrintCorner("RL", simulator.State.RearLeftShadowSuspension);
        PrintCorner("RR", simulator.State.RearRightShadowSuspension);
    }

    private static string ContactSummary(VehicleState state)
    {
        return
            (state.FrontLeftShadowSuspension.HasContact ? "FL" : "--") + "/" +
            (state.FrontRightShadowSuspension.HasContact ? "FR" : "--") + "/" +
            (state.RearLeftShadowSuspension.HasContact ? "RL" : "--") + "/" +
            (state.RearRightShadowSuspension.HasContact ? "RR" : "--");
    }

    private static void PrintCorner(string label, ShadowSuspensionCornerState state)
    {
        Console.WriteLine(
            $"    {label}: contact={state.HasContact} state={state.ContactState} recovered={state.AntiTunnelRecovered} " +
            $"penetration={state.AntiTunnelPenetrationMeters:0.###}m reason={state.MissReason} " +
            $"source={state.SourceName} tri={state.TriangleIndex} " +
            $"hardpoint=({state.HardpointWorld.X:0.###},{state.HardpointWorld.Y:0.###},{state.HardpointWorld.Z:0.###}) " +
            $"contact=({state.ContactPoint.X:0.###},{state.ContactPoint.Y:0.###},{state.ContactPoint.Z:0.###}) " +
            $"travel={state.CompressionMeters:0.###} support={state.SupportForceN:0.#}N");
    }

    private static Vector3 ToQuery(Vector2 position, float y)
    {
        return new Vector3(position.X, y, position.Y);
    }

    private static IEnumerable<(string Label, AuthoredDriveableTriangle Triangle)> SelectRepresentativeTriangles(AuthoredTrackSurfaceSampler sampler)
    {
        AuthoredDriveableTriangle[] triangles = sampler.Triangles.ToArray();
        yield return ("flat-ish", triangles.OrderByDescending(triangle => triangle.Normal.Y).First());
        yield return ("banked/tilted", triangles.OrderBy(triangle => triangle.Normal.Y).First());
        yield return ("lowest", triangles.OrderBy(triangle => MathF.Min(triangle.A.Y, MathF.Min(triangle.B.Y, triangle.C.Y))).First());
        yield return ("highest", triangles.OrderByDescending(triangle => MathF.Max(triangle.A.Y, MathF.Max(triangle.B.Y, triangle.C.Y))).First());
    }

    private static void Sample(string label, AuthoredTrackSurfaceSampler sampler, Vector3 query)
    {
        const float range = 8.0f;
        if (sampler.TryGetContact(query, range, out TrackSurfaceContact contact))
        {
            Console.WriteLine(
                $"  {label}: query=({query.X:0.###}, {query.Z:0.###}) hitY={contact.Position.Y:0.###} " +
                $"normal=({contact.Normal.X:0.###}, {contact.Normal.Y:0.###}, {contact.Normal.Z:0.###}) " +
                $"source={contact.SourceName} triangle={contact.TriangleIndex} candidates={contact.CandidateTriangleCount}");
        }
        else
        {
            Console.WriteLine(
                $"  {label}: MISS query=({query.X:0.###}, {query.Y:0.###}, {query.Z:0.###}) range={range:0.#}m");
        }
    }

    private static void SamplePose(
        string label,
        AuthoredTrackSurfaceSampler sampler,
        Vector2 center,
        Vector2 forward,
        Vector2 right,
        VehicleAxleGeometry geometry,
        float queryY)
    {
        float frontHalfTrack = geometry.FrontTrackMeters * 0.5f;
        float rearHalfTrack = geometry.RearTrackMeters * 0.5f;
        if (!TrySampleY(sampler, center - right * frontHalfTrack + forward * geometry.CgToFrontAxleMeters, queryY, out float fl) ||
            !TrySampleY(sampler, center + right * frontHalfTrack + forward * geometry.CgToFrontAxleMeters, queryY, out float fr) ||
            !TrySampleY(sampler, center - right * rearHalfTrack - forward * geometry.CgToRearAxleMeters, queryY, out float rl) ||
            !TrySampleY(sampler, center + right * rearHalfTrack - forward * geometry.CgToRearAxleMeters, queryY, out float rr))
        {
            Console.WriteLine($"  {label}: MISS one or more wheel contacts");
            return;
        }

        float front = (fl + fr) * 0.5f;
        float rear = (rl + rr) * 0.5f;
        float pitch = -MathF.Atan2(front - rear, geometry.WheelbaseMeters);
        float roll = MathHelper.Lerp(
            MathF.Atan2(rl - rr, MathF.Max(0.1f, geometry.RearTrackMeters)),
            MathF.Atan2(fl - fr, MathF.Max(0.1f, geometry.FrontTrackMeters)),
            0.5f);
        Console.WriteLine(
            $"  {label}: wheelY FL={fl:0.###} FR={fr:0.###} RL={rl:0.###} RR={rr:0.###} " +
            $"pitch={MathHelper.ToDegrees(pitch):0.##}deg roll={MathHelper.ToDegrees(roll):0.##}deg");
    }

    private static bool TrySampleY(AuthoredTrackSurfaceSampler sampler, Vector2 position, float queryY, out float y)
    {
        if (sampler.TryGetContact(ToQuery(position, queryY), 8f, out TrackSurfaceContact contact))
        {
            y = contact.Position.Y;
            return true;
        }

        y = 0f;
        return false;
    }

    private sealed class ContactSoakStats
    {
        private int _currentMissFl;
        private int _currentMissFr;
        private int _currentMissRl;
        private int _currentMissRr;

        public int NormalContacts { get; private set; }

        public int AntiTunnelRecoveries { get; private set; }

        public int BumpStopContacts { get; private set; }

        public int Misses { get; private set; }

        public int LongestMissFl { get; private set; }

        public int LongestMissFr { get; private set; }

        public int LongestMissRl { get; private set; }

        public int LongestMissRr { get; private set; }

        public float MaxRecoveryPenetration { get; private set; }

        public float MaxCompression { get; private set; }

        public float MaxDroop { get; private set; }

        public float MinChassisY { get; private set; } = float.PositiveInfinity;

        public float MaxChassisY { get; private set; } = float.NegativeInfinity;

        public float MinRoadY { get; private set; } = float.PositiveInfinity;

        public float MaxRoadY { get; private set; } = float.NegativeInfinity;

        public float MaxAbsPitch { get; private set; }

        public float MaxAbsRoll { get; private set; }

        public float MaxAbsVerticalVelocity { get; private set; }

        public HashSet<string> RecoveryWheels { get; } = new(StringComparer.Ordinal);

        public HashSet<string> MissWheels { get; } = new(StringComparer.Ordinal);

        public HashSet<string> BumpStopWheels { get; } = new(StringComparer.Ordinal);

        public string FirstUnusualEvent { get; private set; } = string.Empty;

        public Vector2 FirstFailureWheelPosition { get; private set; }

        public Vector2 FirstFailurePreviousContactPosition { get; private set; }

        public List<PathSample> PathSamples { get; } = [];

        public void RecordStep(
            float timeSeconds,
            VehicleState state,
            Vector3 previousPosition,
            ShadowSuspensionCornerState previousFl,
            ShadowSuspensionCornerState previousFr,
            ShadowSuspensionCornerState previousRl,
            ShadowSuspensionCornerState previousRr)
        {
            PathSamples.Add(PathSample.From(timeSeconds, state));
            if (PathSamples.Count > 1440)
            {
                PathSamples.RemoveAt(0);
            }

            MinChassisY = MathF.Min(MinChassisY, state.Position.Y);
            MaxChassisY = MathF.Max(MaxChassisY, state.Position.Y);
            MaxAbsPitch = MathF.Max(MaxAbsPitch, MathF.Abs(state.BodyPitchRadians));
            MaxAbsRoll = MathF.Max(MaxAbsRoll, MathF.Abs(state.BodyRollRadians));
            MaxAbsVerticalVelocity = MathF.Max(MaxAbsVerticalVelocity, MathF.Abs(state.BodyVelocityWorldMetersPerSecond.Y));

            RecordCorner("FL", timeSeconds, state, previousPosition, previousFl, state.FrontLeftShadowSuspension, ref _currentMissFl, value => LongestMissFl = value, LongestMissFl);
            RecordCorner("FR", timeSeconds, state, previousPosition, previousFr, state.FrontRightShadowSuspension, ref _currentMissFr, value => LongestMissFr = value, LongestMissFr);
            RecordCorner("RL", timeSeconds, state, previousPosition, previousRl, state.RearLeftShadowSuspension, ref _currentMissRl, value => LongestMissRl = value, LongestMissRl);
            RecordCorner("RR", timeSeconds, state, previousPosition, previousRr, state.RearRightShadowSuspension, ref _currentMissRr, value => LongestMissRr = value, LongestMissRr);
        }

        private void RecordCorner(
            string wheel,
            float timeSeconds,
            VehicleState state,
            Vector3 previousPosition,
            ShadowSuspensionCornerState previous,
            ShadowSuspensionCornerState current,
            ref int currentMissRun,
            Action<int> setLongestMiss,
            int longestMiss)
        {
            if (current.HasContact)
            {
                currentMissRun = 0;
                if (current.AntiTunnelRecovered)
                {
                    AntiTunnelRecoveries++;
                    RecoveryWheels.Add(wheel);
                    MaxRecoveryPenetration = MathF.Max(MaxRecoveryPenetration, current.AntiTunnelPenetrationMeters);
                }
                else
                {
                    NormalContacts++;
                }

                if (current.BumpLimitHit)
                {
                    BumpStopContacts++;
                    BumpStopWheels.Add(wheel);
                }

                MinRoadY = MathF.Min(MinRoadY, current.CalculatedTyreContactPoint.Y);
                MaxRoadY = MathF.Max(MaxRoadY, current.CalculatedTyreContactPoint.Y);
                MaxCompression = MathF.Max(MaxCompression, current.CompressionMeters);
                MaxDroop = MathF.Max(MaxDroop, MathF.Max(0f, -current.CompressionMeters));
            }
            else
            {
                Misses++;
                MissWheels.Add(wheel);
                currentMissRun++;
                if (currentMissRun > longestMiss)
                {
                    setLongestMiss(currentMissRun);
                }
            }

            bool firstUnusual =
                string.IsNullOrWhiteSpace(FirstUnusualEvent) &&
                (current.AntiTunnelRecovered ||
                 !current.HasContact && previous.HasContact ||
                 current.BumpLimitHit && !previous.BumpLimitHit);
            if (!firstUnusual)
            {
                return;
            }

            FirstFailureWheelPosition = current.HasContact
                ? new Vector2(current.CalculatedTyreContactPoint.X, current.CalculatedTyreContactPoint.Z)
                : new Vector2(current.HardpointWorld.X, current.HardpointWorld.Z);
            FirstFailurePreviousContactPosition = new Vector2(previous.ContactPoint.X, previous.ContactPoint.Z);

            FirstUnusualEvent =
                $"wheel={wheel} t={timeSeconds:0.###}s speed={state.SpeedMetersPerSecond:0.###}m/s type={DescribeUnusual(current, previous)}\n" +
                $"prevChassis=({previousPosition.X:0.###},{previousPosition.Y:0.###},{previousPosition.Z:0.###}) " +
                $"currChassis=({state.Position.X:0.###},{state.Position.Y:0.###},{state.Position.Z:0.###})\n" +
                $"prevHardpoint=({previous.HardpointWorld.X:0.###},{previous.HardpointWorld.Y:0.###},{previous.HardpointWorld.Z:0.###}) " +
                $"currHardpoint=({current.HardpointWorld.X:0.###},{current.HardpointWorld.Y:0.###},{current.HardpointWorld.Z:0.###})\n" +
                $"axis=({current.SuspensionAxisWorld.X:0.###},{current.SuspensionAxisWorld.Y:0.###},{current.SuspensionAxisWorld.Z:0.###}) " +
                $"previousContact=({previous.ContactPoint.X:0.###},{previous.ContactPoint.Y:0.###},{previous.ContactPoint.Z:0.###}) " +
                $"prevNormal=({previous.ContactNormal.X:0.###},{previous.ContactNormal.Y:0.###},{previous.ContactNormal.Z:0.###}) " +
                $"prevSource={previous.SourceName} prevTri={previous.TriangleIndex}\n" +
                $"currentContact=({current.ContactPoint.X:0.###},{current.ContactPoint.Y:0.###},{current.ContactPoint.Z:0.###}) " +
                $"source={current.SourceName} tri={current.TriangleIndex} candidates={current.CandidateTriangleCount} " +
                $"state={current.ContactState} reason={current.MissReason}\n" +
                $"velocity=({state.BodyVelocityWorldMetersPerSecond.X:0.###},{state.BodyVelocityWorldMetersPerSecond.Y:0.###},{state.BodyVelocityWorldMetersPerSecond.Z:0.###}) " +
                $"pitch={MathHelper.ToDegrees(state.BodyPitchRadians):0.###}deg roll={MathHelper.ToDegrees(state.BodyRollRadians):0.###}deg " +
                $"pitchRate={MathHelper.ToDegrees(state.BodyPitchRateRadiansPerSecond):0.###}deg/s " +
                $"rollRate={MathHelper.ToDegrees(state.BodyRollRateRadiansPerSecond):0.###}deg/s " +
                $"penetration={current.AntiTunnelPenetrationMeters:0.###}m compression={current.CompressionMeters:0.###}m";
        }

        private static string DescribeUnusual(ShadowSuspensionCornerState current, ShadowSuspensionCornerState previous)
        {
            if (current.AntiTunnelRecovered)
            {
                return "AntiTunnelRecovered";
            }

            if (!current.HasContact && previous.HasContact)
            {
                return "MissAfterContact";
            }

            if (current.BumpLimitHit && !previous.BumpLimitHit)
            {
                return "BumpStop";
            }

            return current.ContactState;
        }
    }

    private readonly record struct PathSample(
        float TimeSeconds,
        Vector2 Center,
        Vector2 FrontLeft,
        Vector2 FrontRight,
        Vector2 RearLeft,
        Vector2 RearRight,
        string FrontLeftSource,
        string FrontRightSource,
        string RearLeftSource,
        string RearRightSource,
        float Steering,
        float HeadingRadians,
        float SignedDistanceFromCenterMeters)
    {
        public static PathSample From(float timeSeconds, VehicleState state)
        {
            return new PathSample(
                timeSeconds,
                new Vector2(state.Position.X, state.Position.Z),
                ToXz(state.FrontLeftShadowSuspension),
                ToXz(state.FrontRightShadowSuspension),
                ToXz(state.RearLeftShadowSuspension),
                ToXz(state.RearRightShadowSuspension),
                state.FrontLeftShadowSuspension.SourceName,
                state.FrontRightShadowSuspension.SourceName,
                state.RearLeftShadowSuspension.SourceName,
                state.RearRightShadowSuspension.SourceName,
                state.Steer,
                state.HeadingRadians,
                0f);
        }

        private static Vector2 ToXz(ShadowSuspensionCornerState state)
        {
            Vector3 point = state.HasContact ? state.CalculatedTyreContactPoint : state.HardpointWorld;
            return new Vector2(point.X, point.Z);
        }
    }
}
