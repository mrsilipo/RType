using Microsoft.Xna.Framework;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public sealed class RaceSession
{
    private const int SectorCount = 3;
    private const float WrongWayTrackSpeedThresholdMetersPerSecond = -3.0f;
    private const float WrongWayVehicleSpeedThresholdMetersPerSecond = 5.0f;
    private const float WrongWayProgressThreshold = -0.0015f;
    private const double WrongWayFlagSeconds = 0.65;

    private readonly ITrackProgressSampler _track;
    private readonly float[] _sectorMarkers;
    private TimeSpan _lapStartTime;
    private TimeSpan _sectorStartTime;
    private float _previousProgress;
    private bool _hasPreviousProgress;
    private int _nextGateIndex;
    private double _wrongWaySeconds;

    public RaceSession(ITrackProgressSampler track, int targetLaps)
    {
        _track = track ?? throw new ArgumentNullException(nameof(track));
        _sectorMarkers = NormalizeSectorMarkers(_track.SectorMarkers);
        State = new RaceSessionState(Math.Max(1, targetLaps));
    }

    public RaceSessionState State { get; }

    public void Update(VehicleState vehicle, TimeSpan elapsed)
    {
        if (State.Finished)
        {
            return;
        }

        TimeSpan clampedElapsed = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        State.RaceTime += clampedElapsed;
        State.CurrentLapTime = State.RaceTime - _lapStartTime;

        TrackProgress progress = _track.GetProgress(vehicle.Position);
        State.ProgressPercent = progress.NormalizedDistance * 100f;
        State.OffTrack = IsOffTrack(vehicle) || vehicle.WallContactCount > 0;
        if (State.OffTrack)
        {
            State.CurrentLapInvalid = true;
        }

        float normalizedDelta = 0f;
        if (_hasPreviousProgress)
        {
            normalizedDelta = CalculateSignedProgressDelta(_previousProgress, progress.NormalizedDistance);
        }

        UpdateWrongWay(vehicle, progress, normalizedDelta, clampedElapsed.TotalSeconds);
        if (State.WrongWay)
        {
            State.CurrentLapInvalid = true;
        }

        if (_hasPreviousProgress && normalizedDelta > 0f)
        {
            AdvanceTimingGates(_previousProgress, progress.NormalizedDistance);
        }

        _previousProgress = progress.NormalizedDistance;
        _hasPreviousProgress = true;
        State.CurrentLapTime = State.Finished
            ? State.LastLapTime ?? State.CurrentLapTime
            : State.RaceTime - _lapStartTime;
    }

    private void AdvanceTimingGates(float previousProgress, float currentProgress)
    {
        if (_nextGateIndex < _sectorMarkers.Length)
        {
            float marker = _sectorMarkers[_nextGateIndex];
            if (CrossedForward(previousProgress, currentProgress, marker))
            {
                CompleteSector(_nextGateIndex);
            }

            return;
        }

        if (CrossedForward(previousProgress, currentProgress, 0f))
        {
            CompleteLap();
        }
    }

    private void CompleteSector(int sectorIndex)
    {
        TimeSpan sectorTime = State.RaceTime - _sectorStartTime;
        State.CurrentSectorTimes[sectorIndex] = sectorTime;
        State.LastSectorIndex = sectorIndex + 1;
        State.LastSectorTime = sectorTime;

        if (!State.CurrentLapInvalid &&
            (State.BestSectorTimes[sectorIndex] is null || sectorTime < State.BestSectorTimes[sectorIndex]))
        {
            State.BestSectorTimes[sectorIndex] = sectorTime;
        }

        _sectorStartTime = State.RaceTime;
        _nextGateIndex++;
        State.CurrentSector = Math.Min(SectorCount, _nextGateIndex + 1);
    }

    private void CompleteLap()
    {
        TimeSpan finalSectorTime = State.RaceTime - _sectorStartTime;
        State.CurrentSectorTimes[2] = finalSectorTime;
        State.LastSectorIndex = 3;
        State.LastSectorTime = finalSectorTime;
        if (!State.CurrentLapInvalid &&
            (State.BestSectorTimes[2] is null || finalSectorTime < State.BestSectorTimes[2]))
        {
            State.BestSectorTimes[2] = finalSectorTime;
        }

        TimeSpan lapTime = State.RaceTime - _lapStartTime;
        State.CompletedLaps++;
        State.LastLapTime = lapTime;
        State.LastLapWasValid = !State.CurrentLapInvalid;

        if (!State.CurrentLapInvalid &&
            (State.BestLapTime is null || lapTime < State.BestLapTime))
        {
            State.BestLapTime = lapTime;
        }

        if (State.CompletedLaps >= State.TargetLaps)
        {
            State.CurrentLap = State.TargetLaps;
            State.Finished = true;
            return;
        }

        State.CurrentLap = State.CompletedLaps + 1;
        State.CurrentLapInvalid = false;
        State.CurrentSector = 1;
        Array.Clear(State.CurrentSectorTimes);

        _lapStartTime = State.RaceTime;
        _sectorStartTime = State.RaceTime;
        _nextGateIndex = 0;
    }

    private void UpdateWrongWay(VehicleState vehicle, TrackProgress progress, float normalizedDelta, double elapsedSeconds)
    {
        float trackSpeed = Vector2.Dot(vehicle.Velocity, progress.Forward);
        bool wrongWay =
            vehicle.SpeedMetersPerSecond > WrongWayVehicleSpeedThresholdMetersPerSecond &&
            (trackSpeed < WrongWayTrackSpeedThresholdMetersPerSecond ||
             normalizedDelta < WrongWayProgressThreshold);

        if (wrongWay)
        {
            _wrongWaySeconds += elapsedSeconds;
        }
        else
        {
            _wrongWaySeconds = Math.Max(0.0, _wrongWaySeconds - elapsedSeconds * 2.0);
        }

        State.WrongWay = _wrongWaySeconds >= WrongWayFlagSeconds;
    }

    private static bool IsOffTrack(VehicleState vehicle)
    {
        return !vehicle.SurfaceName.Equals("ROAD", StringComparison.OrdinalIgnoreCase) &&
               !vehicle.SurfaceName.Equals("CURB", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CrossedForward(float previousProgress, float currentProgress, float marker)
    {
        if (currentProgress >= previousProgress)
        {
            return marker > previousProgress && marker <= currentProgress;
        }

        return marker > previousProgress || marker <= currentProgress;
    }

    private static float CalculateSignedProgressDelta(float previousProgress, float currentProgress)
    {
        float delta = currentProgress - previousProgress;
        if (delta > 0.5f)
        {
            delta -= 1f;
        }
        else if (delta < -0.5f)
        {
            delta += 1f;
        }

        return delta;
    }

    private static float[] NormalizeSectorMarkers(IReadOnlyList<float> markers)
    {
        if (markers.Count < SectorCount - 1)
        {
            return [1f / 3f, 2f / 3f];
        }

        float first = MathHelper.Clamp(markers[0], 0.05f, 0.90f);
        float second = MathHelper.Clamp(markers[1], first + 0.05f, 0.95f);
        return [first, second];
    }
}

public sealed class RaceSessionState
{
    public RaceSessionState(int targetLaps)
    {
        TargetLaps = Math.Max(1, targetLaps);
    }

    public int TargetLaps { get; }

    public int CurrentLap { get; set; } = 1;

    public int CompletedLaps { get; set; }

    public int CurrentSector { get; set; } = 1;

    public int LastSectorIndex { get; set; }

    public TimeSpan RaceTime { get; set; }

    public TimeSpan CurrentLapTime { get; set; }

    public TimeSpan? LastLapTime { get; set; }

    public TimeSpan? BestLapTime { get; set; }

    public TimeSpan? LastSectorTime { get; set; }

    public TimeSpan?[] CurrentSectorTimes { get; } = new TimeSpan?[3];

    public TimeSpan?[] BestSectorTimes { get; } = new TimeSpan?[3];

    public bool LastLapWasValid { get; set; }

    public bool CurrentLapInvalid { get; set; }

    public bool WrongWay { get; set; }

    public bool OffTrack { get; set; }

    public bool Finished { get; set; }

    public float ProgressPercent { get; set; }
}
