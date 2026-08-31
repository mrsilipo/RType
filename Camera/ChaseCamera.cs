using Microsoft.Xna.Framework;
using RType.Vehicle;
using RType.World;

namespace RType.Camera;

public sealed class ChaseCamera
{
    private const float StationarySettleSpeedMetersPerSecond = 0.50f;
    private const float StationarySettleYawRateRadiansPerSecond = 0.04f;
    private const float PowertrainPresentationShakeScale = 0.50f;

    private static readonly ChaseCameraProfile Chase1Profile = new(
        Distance: 4.05f,
        Height: 2.05f,
        TargetDistance: 2.1f,
        TargetHeight: 1.05f,
        FovDegrees: 56f,
        PositionResponse: 15f,
        TargetResponse: 18f,
        YawResponse: 12f,
        SurfaceShake: 0.070f,
        PowertrainShake: 0.030f,
        TargetShakeIntensity: 0.72f,
        PositionShakeIntensity: 0.30f,
        DriftLookAheadFactor: 0.25f,
        TrackUpInfluence: 0.0f,
        MinimumGroundClearance: 0.62f,
        ImpactImpulseStrength: 0.060f);

    private static readonly ChaseCameraProfile Chase2Profile = new(
        Distance: 6.75f,
        Height: 2.55f,
        TargetDistance: 3.2f,
        TargetHeight: 1.12f,
        FovDegrees: 58f,
        PositionResponse: 15f,
        TargetResponse: 18f,
        YawResponse: 12f,
        SurfaceShake: 0.070f,
        PowertrainShake: 0.030f,
        TargetShakeIntensity: 0.72f,
        PositionShakeIntensity: 0.30f,
        DriftLookAheadFactor: 0.45f,
        TrackUpInfluence: 0.0f,
        MinimumGroundClearance: 0.62f,
        ImpactImpulseStrength: 0.060f);

    private readonly float _aspectRatio;
    private float _smoothedPowertrainShock;
    private float _powertrainShockPhaseSeconds;
    private float _surfaceShakePhaseSeconds;
    private float _impactImpulse;
    private float _previousSpeedMetersPerSecond;
    private Vector3 _smoothedChaseForward = Vector3.Forward;
    private Vector3 _smoothedChaseRight = Vector3.Right;
    private Vector3 _inCarHeadOffset;
    private Vector3 _inCarHeadVelocity;

    public ChaseCamera(float aspectRatio)
    {
        _aspectRatio = aspectRatio;
        Projection = Matrix.CreatePerspectiveFieldOfView(
            MathHelper.ToRadians(62f),
            aspectRatio,
            0.1f,
            360f);
    }

    public Vector3 Position { get; private set; }

    public Vector3 Target { get; private set; }

    public Matrix View { get; private set; }

    public Matrix Projection { get; private set; }

    public CameraMode Mode { get; private set; } = CameraMode.Chase1;

    public string ModeName => Mode switch
    {
        CameraMode.InCar => "IN CAR",
        CameraMode.FixedDiagnostic => "FIXED",
        CameraMode.Chase2 => "CHASE 2",
        _ => "CHASE 1"
    };

    public static ChaseCameraIntentDebug GetIntentDebug(VehicleState vehicle, bool lookBehind)
    {
        float signedForwardSpeed = Vector2.Dot(vehicle.Velocity, new Vector2(vehicle.Forward.X, vehicle.Forward.Z));
        Vector3 forward = vehicle.Forward;
        if (lookBehind)
        {
            forward = -forward;
        }

        string reason = lookBehind
            ? "manual-look-behind"
            : "vehicle-forward";
        return new ChaseCameraIntentDebug(
            signedForwardSpeed,
            vehicle.Gear,
            lookBehind,
            vehicle.Gear < 0,
            reason,
            GetSafeHorizontal(forward, Vector3.Forward));
    }

    public void SetMode(CameraMode mode, VehicleState vehicle, bool reset)
    {
        Mode = mode;
        if (reset)
        {
            Reset(vehicle);
        }
    }

    public void SetLookAt(Vector3 position, Vector3 target)
    {
        Position = position;
        Target = target;
        View = Matrix.CreateLookAt(Position, Target, Vector3.Up);
    }

    public (Vector3 Position, Vector3 Target) GetInCarPose(VehicleState vehicle, bool lookBehind = false)
    {
        (Vector3 position, Vector3 target, _, _, _) = CalculateInCarPose(vehicle, lookBehind);
        return (position, target);
    }

    public static SceneCamera CreateRearViewMirrorCamera(VehicleState vehicle, float aspectRatio)
    {
        (Vector3 position, Vector3 target, _, _, Vector3 up) = CalculateInCarPose(vehicle, lookBehind: true);
        Matrix view = Matrix.CreateLookAt(position, target, up);
        Matrix projection = Matrix.CreatePerspectiveFieldOfView(
            MathHelper.ToRadians(46f),
            aspectRatio,
            0.1f,
            260f);
        return new SceneCamera(view, projection, position);
    }

    public void CycleMode(VehicleState vehicle)
    {
        Mode = Mode switch
        {
            CameraMode.Chase1 => CameraMode.Chase2,
            CameraMode.Chase2 => CameraMode.FixedDiagnostic,
            CameraMode.FixedDiagnostic => CameraMode.InCar,
            _ => CameraMode.Chase1
        };
        Reset(vehicle);
    }

    public void Reset(VehicleState vehicle)
    {
        _smoothedPowertrainShock = 0f;
        _powertrainShockPhaseSeconds = 0f;
        _surfaceShakePhaseSeconds = 0f;
        _impactImpulse = 0f;
        _previousSpeedMetersPerSecond = vehicle.SpeedMetersPerSecond;
        _inCarHeadOffset = Vector3.Zero;
        _inCarHeadVelocity = Vector3.Zero;

        if (Mode == CameraMode.InCar)
        {
            ResetInCar(vehicle);
            return;
        }

        if (Mode == CameraMode.FixedDiagnostic)
        {
            ResetFixedDiagnostic(vehicle);
            return;
        }

        ChaseCameraProfile profile = GetChaseProfile();
        Vector3 forward = GetSafeHorizontal(vehicle.Forward, Vector3.Forward);
        _smoothedChaseForward = forward;
        _smoothedChaseRight = GetSafeHorizontal(vehicle.Right, Vector3.Right);
        Position = vehicle.Position - forward * profile.Distance + Vector3.Up * profile.Height;
        Target = vehicle.Position + forward * profile.TargetDistance + Vector3.Up * profile.TargetHeight;
        Projection = CreateProjection(profile.FovDegrees);
        View = Matrix.CreateLookAt(Position, Target, Vector3.Up);
    }

    public void Update(VehicleState vehicle, float dt, bool lookBehind, TrackScene? track = null)
    {
        if (Mode == CameraMode.InCar)
        {
            UpdateInCar(vehicle, dt, lookBehind);
            return;
        }

        if (Mode == CameraMode.FixedDiagnostic)
        {
            Projection = CreateProjection(54f);
            return;
        }

        ChaseCameraProfile profile = GetChaseProfile();
        float clampedDt = MathHelper.Clamp(dt, 0f, 1f / 20f);
        float speed = vehicle.SpeedMetersPerSecond;
        bool stationarySettled =
            speed < StationarySettleSpeedMetersPerSecond &&
            MathF.Abs(vehicle.YawRateRadiansPerSecond) < StationarySettleYawRateRadiansPerSecond &&
            !vehicle.CollisionActive;

        ChaseCameraIntentDebug intent = GetIntentDebug(vehicle, lookBehind);
        Vector3 baseForward = intent.Forward;
        Vector3 baseRight = lookBehind ? -vehicle.Right : vehicle.Right;
        baseRight = GetSafeHorizontal(baseRight, Vector3.Right);

        if (!stationarySettled || lookBehind)
        {
            float yawBlend = 1f - MathF.Exp(-profile.YawResponse * clampedDt);
            _smoothedChaseForward = SmoothDirection(_smoothedChaseForward, baseForward, yawBlend);
            _smoothedChaseRight = SmoothDirection(_smoothedChaseRight, baseRight, yawBlend);
        }

        float distance = profile.Distance;
        float height = profile.Height;
        height = MathF.Max(profile.MinimumGroundClearance, height);
        Vector3 cameraUp = CalculateChaseUp(vehicle, profile, speed);
        Vector3 desiredPosition = vehicle.Position - _smoothedChaseForward * distance + cameraUp * height;
        Vector3 desiredTarget = vehicle.Position + _smoothedChaseForward * profile.TargetDistance + cameraUp * profile.TargetHeight;

        float shock = stationarySettled ? ClearPowertrainShock() : UpdatePowertrainShock(vehicle, dt);
        float downshiftPull = stationarySettled ? 0f : CalculateDownshiftPull(vehicle);
        if (stationarySettled)
        {
            _impactImpulse = 0f;
            _previousSpeedMetersPerSecond = speed;
        }
        else
        {
            ApplyImpactImpulse(vehicle, profile, clampedDt);
        }

        desiredPosition -= _smoothedChaseForward * downshiftPull * (Mode == CameraMode.Chase2 ? 1.12f : 0.72f);
        desiredTarget -= _smoothedChaseForward * downshiftPull * (Mode == CameraMode.Chase2 ? 0.30f : 0.20f);
        desiredPosition += _smoothedChaseForward * _impactImpulse;

        if (!lookBehind)
        {
            float lateralSpeed = Vector2.Dot(vehicle.Velocity, new Vector2(vehicle.Right.X, vehicle.Right.Z));
            float forwardSpeed = Vector2.Dot(vehicle.Velocity, new Vector2(vehicle.Forward.X, vehicle.Forward.Z));
            float slipAngle = MathHelper.Clamp(MathF.Atan2(lateralSpeed, MathF.Max(1f, MathF.Abs(forwardSpeed))), -0.70f, 0.70f);
            float slipLookAheadFade = SmoothStep(2.25f, 4.50f, speed);
            desiredTarget += _smoothedChaseRight * slipAngle * profile.DriftLookAheadFactor * slipLookAheadFade;
        }

        desiredPosition = ApplyGroundClearance(desiredPosition, track, profile);
        Projection = CreateProjection(profile.FovDegrees);

        float positionBlend = 1f - MathF.Exp(-(profile.PositionResponse + shock * 10f) * clampedDt);
        float targetBlend = 1f - MathF.Exp(-(profile.TargetResponse + shock * 12f) * clampedDt);
        Vector3 planarVelocity = new(vehicle.Velocity.X, 0f, vehicle.Velocity.Y);
        desiredPosition += planarVelocity * CalculateSmoothingFeedForward(positionBlend, clampedDt);
        desiredTarget += planarVelocity * CalculateSmoothingFeedForward(targetBlend, clampedDt);
        Position = Vector3.Lerp(Position, desiredPosition, positionBlend);
        Target = Vector3.Lerp(Target, desiredTarget, targetBlend);

        (Vector3 positionShake, Vector3 targetShake) = CalculateAsymmetricShake(vehicle, profile, shock, clampedDt, lookBehind);
        View = Matrix.CreateLookAt(Position + positionShake, Target + targetShake, cameraUp);
    }

    private static float CalculateSmoothingFeedForward(float blend, float dt)
    {
        if (dt <= 0f || blend <= 0.0001f)
        {
            return 0f;
        }

        return MathF.Max(0f, dt / blend - dt);
    }

    private void ResetInCar(VehicleState vehicle)
    {
        (Position, Target, _, _, Vector3 up) = CalculateInCarPose(vehicle, false);
        View = Matrix.CreateLookAt(Position, Target, up);
    }

    private void ResetFixedDiagnostic(VehicleState vehicle)
    {
        Vector3 anchor = vehicle.Position;
        Position = anchor + new Vector3(0f, 8.5f, -10.5f);
        Target = anchor + new Vector3(0f, 0.35f, 0f);
        Projection = CreateProjection(54f);
        View = Matrix.CreateLookAt(Position, Target, Vector3.Up);
    }

    private void UpdateInCar(VehicleState vehicle, float dt, bool lookBehind)
    {
        (Vector3 desiredPosition, Vector3 desiredTarget, Vector3 bodyForward, Vector3 bodyRight, Vector3 bodyUp) =
            CalculateInCarPose(vehicle, lookBehind);
        Vector3 lookForward = lookBehind ? -bodyForward : bodyForward;
        Vector3 viewRight = lookBehind ? -bodyRight : bodyRight;
        float shock = UpdatePowertrainShock(vehicle, dt);
        float downshiftPull = CalculateDownshiftPull(vehicle);
        Vector3 headOffset = CalculateInCarHeadOffset(vehicle, bodyForward, bodyRight, bodyUp, shock * PowertrainPresentationShakeScale, dt);
        Vector3 jitter = CalculatePowertrainJitter(lookForward, viewRight, bodyUp, shock, 0.26f);
        desiredPosition += headOffset + bodyForward * downshiftPull * 0.10f - bodyUp * downshiftPull * 0.018f;
        desiredPosition += jitter;
        desiredTarget += headOffset * 0.38f + bodyForward * downshiftPull * 0.05f - bodyUp * downshiftPull * 0.012f;
        desiredTarget += jitter * 0.82f;

        float positionBlend = 1f - MathF.Exp(-(42f + shock * 22f) * dt);
        float targetBlend = 1f - MathF.Exp(-(54f + shock * 24f) * dt);
        Position = Vector3.Lerp(Position, desiredPosition, positionBlend);
        Target = Vector3.Lerp(Target, desiredTarget, targetBlend);
        View = Matrix.CreateLookAt(Position, Target, bodyUp);
    }

    private ChaseCameraProfile GetChaseProfile()
    {
        return Mode == CameraMode.Chase2 ? Chase2Profile : Chase1Profile;
    }

    private Matrix CreateProjection(float fovDegrees)
    {
        return Matrix.CreatePerspectiveFieldOfView(
            MathHelper.ToRadians(MathHelper.Clamp(fovDegrees, 42f, 74f)),
            _aspectRatio,
            0.1f,
            360f);
    }

    private static Vector3 SmoothDirection(Vector3 current, Vector3 target, float blend)
    {
        current = GetSafeHorizontal(current, target);
        target = GetSafeHorizontal(target, current);
        Vector3 result = Vector3.Lerp(current, target, MathHelper.Clamp(blend, 0f, 1f));
        return GetSafeHorizontal(result, target);
    }

    private static Vector3 CalculateChaseUp(VehicleState vehicle, ChaseCameraProfile profile, float speedMetersPerSecond)
    {
        Matrix ground = Matrix.CreateRotationX(vehicle.GroundPitchRadians) * Matrix.CreateRotationZ(vehicle.GroundRollRadians);
        Vector3 trackUp = Vector3.Normalize(Vector3.TransformNormal(Vector3.Up, ground));
        float lowSpeedTrackUpFade = SmoothStep(1.0f, 4.0f, speedMetersPerSecond);
        float influence = MathHelper.Clamp(profile.TrackUpInfluence * lowSpeedTrackUpFade, 0f, 1f);
        Vector3 up = Vector3.Lerp(Vector3.Up, trackUp, influence);
        return up.LengthSquared() <= 0.0001f ? Vector3.Up : Vector3.Normalize(up);
    }

    private static Vector3 ApplyGroundClearance(Vector3 desiredPosition, TrackScene? track, ChaseCameraProfile profile)
    {
        if (track is null)
        {
            return desiredPosition;
        }

        float groundY = track.GetElevation(new Vector2(desiredPosition.X, desiredPosition.Z));
        float minimumY = groundY + profile.MinimumGroundClearance;
        if (desiredPosition.Y < minimumY)
        {
            desiredPosition.Y = minimumY;
        }

        return desiredPosition;
    }

    private void ApplyImpactImpulse(VehicleState vehicle, ChaseCameraProfile profile, float dt)
    {
        float speed = vehicle.SpeedMetersPerSecond;
        float speedDrop = _previousSpeedMetersPerSecond - speed;
        _previousSpeedMetersPerSecond = speed;
        if ((vehicle.CollisionActive && speed > 1.2f) || speedDrop > 4.8f)
        {
            float severity = MathHelper.Clamp(MathF.Max(vehicle.CrashSeverity, speedDrop / 16f), 0f, 1f);
            _impactImpulse = MathF.Max(_impactImpulse, severity * profile.ImpactImpulseStrength * 4.0f);
        }

        float decay = 1f - MathF.Exp(-14f * dt);
        _impactImpulse = MathHelper.Lerp(_impactImpulse, 0f, decay);
        if (_impactImpulse <= 0.001f)
        {
            _impactImpulse = 0f;
        }
    }

    private (Vector3 PositionShake, Vector3 TargetShake) CalculateAsymmetricShake(
        VehicleState vehicle,
        ChaseCameraProfile profile,
        float shock,
        float dt,
        bool lookBehind)
    {
        float surface = MathHelper.Clamp(MathF.Max(vehicle.SurfaceRumbleLeft, vehicle.SurfaceRumbleRight), 0f, 1f);
        float stopFade = SmoothStep(0.65f, 4.0f, vehicle.SpeedMetersPerSecond);
        float intensity = MathHelper.Clamp((surface * profile.SurfaceShake + shock * profile.PowertrainShake) * stopFade, 0f, 0.18f);
        if (lookBehind)
        {
            intensity *= 0.35f;
        }

        if (intensity <= 0.0001f)
        {
            return (Vector3.Zero, Vector3.Zero);
        }

        float speedFactor = MathHelper.Clamp(vehicle.SpeedMetersPerSecond / 42f, 0f, 1.25f);
        _surfaceShakePhaseSeconds += dt * MathHelper.Lerp(18f, 44f, speedFactor);
        if (_surfaceShakePhaseSeconds > 1000f)
        {
            _surfaceShakePhaseSeconds -= MathF.Floor(_surfaceShakePhaseSeconds);
        }

        float phase = _surfaceShakePhaseSeconds * MathF.Tau;
        Vector3 positionShake =
            _smoothedChaseRight * MathF.Sin(phase * 1.13f + 0.45f) * intensity * profile.PositionShakeIntensity +
            Vector3.Up * MathF.Sin(phase * 2.91f + 1.40f) * intensity * profile.PositionShakeIntensity * 0.72f;
        Vector3 targetShake =
            _smoothedChaseRight * MathF.Sin(phase * 1.77f) * intensity * profile.TargetShakeIntensity +
            Vector3.Up * MathF.Sin(phase * 3.37f + 0.80f) * intensity * profile.TargetShakeIntensity * 0.52f;
        return (positionShake, targetShake);
    }

    private float UpdatePowertrainShock(VehicleState vehicle, float dt)
    {
        float speedFade = SmoothStep(0.35f, 2.25f, vehicle.SpeedMetersPerSecond);
        float mechanicalShock = MathHelper.Clamp(
            MathF.Max(vehicle.PowertrainShockIntensity, vehicle.MechanicalOverRevSeverity * 0.175f),
            0f,
            1f);
        float limiterShake = vehicle.RevLimiterBounceIntensity * 0.08f * speedFade;
        float targetShock = MathHelper.Clamp(MathF.Max(mechanicalShock, limiterShake), 0f, 1f) * speedFade;
        float clampedDt = MathHelper.Clamp(dt, 0f, 1f / 20f);
        float responseRate = targetShock > _smoothedPowertrainShock ? 34f : 8f;
        float blend = MathHelper.Clamp(1f - MathF.Exp(-responseRate * clampedDt), 0f, 1f);
        _smoothedPowertrainShock = MathHelper.Lerp(_smoothedPowertrainShock, targetShock, blend);

        if (_smoothedPowertrainShock <= 0.001f)
        {
            _smoothedPowertrainShock = 0f;
        }
        else
        {
            _powertrainShockPhaseSeconds += clampedDt * MathHelper.Lerp(18f, 36f, _smoothedPowertrainShock);
            if (_powertrainShockPhaseSeconds > 1000f)
            {
                _powertrainShockPhaseSeconds -= MathF.Floor(_powertrainShockPhaseSeconds);
            }
        }

        return _smoothedPowertrainShock;
    }

    private float ClearPowertrainShock()
    {
        _smoothedPowertrainShock = 0f;
        _powertrainShockPhaseSeconds = 0f;
        return 0f;
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float t = MathHelper.Clamp((value - edge0) / MathF.Max(0.001f, edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private float CalculateDownshiftPull(VehicleState vehicle)
    {
        float speedFade = SmoothStep(0.35f, 2.25f, vehicle.SpeedMetersPerSecond);
        return MathHelper.Clamp(
            MathF.Max(vehicle.PowertrainShockIntensity, vehicle.MechanicalOverRevSeverity * 0.25f) * speedFade * 0.25f,
            0f,
            1f);
    }

    private Vector3 CalculatePowertrainJitter(Vector3 forward, Vector3 right, Vector3 up, float intensity, float scale)
    {
        if (intensity <= 0.001f)
        {
            return Vector3.Zero;
        }

        float phase = _powertrainShockPhaseSeconds * MathF.Tau;
        float lateral = MathF.Sin(phase * 1.31f) * 0.070f * intensity * scale;
        float vertical = MathF.Sin(phase * 1.83f + 0.70f) * 0.052f * intensity * scale;
        float longitudinal = MathF.Sin(phase * 1.07f + 2.10f) * 0.040f * intensity * scale;
        return right * lateral + up * vertical - forward * longitudinal;
    }

    private Vector3 CalculateInCarHeadOffset(
        VehicleState vehicle,
        Vector3 bodyForward,
        Vector3 bodyRight,
        Vector3 bodyUp,
        float shock,
        float dt)
    {
        float clampedDt = MathHelper.Clamp(dt, 0f, 1f / 20f);
        if (clampedDt <= 0f)
        {
            return _inCarHeadOffset;
        }

        float longitudinalG = MathHelper.Clamp(vehicle.LongitudinalAcceleration / 9.81f, -1.4f, 1.4f);
        float lateralG = MathHelper.Clamp(vehicle.LateralAcceleration / 9.81f, -1.4f, 1.4f);
        Vector3 targetOffset =
            bodyForward * (-longitudinalG * 0.050f) +
            bodyRight * (-lateralG * 0.038f) +
            bodyUp * (-MathF.Abs(lateralG) * 0.010f - MathF.Abs(longitudinalG) * 0.006f);

        targetOffset += bodyForward * shock * 0.030f - bodyUp * shock * 0.012f;
        float spring = 92f;
        float damping = 18f;
        Vector3 acceleration = (targetOffset - _inCarHeadOffset) * spring - _inCarHeadVelocity * damping;
        _inCarHeadVelocity += acceleration * clampedDt;
        _inCarHeadOffset += _inCarHeadVelocity * clampedDt;

        float maximumOffset = 0.18f;
        if (_inCarHeadOffset.LengthSquared() > maximumOffset * maximumOffset)
        {
            _inCarHeadOffset.Normalize();
            _inCarHeadOffset *= maximumOffset;
            _inCarHeadVelocity *= 0.45f;
        }

        return _inCarHeadOffset;
    }

    private static (Vector3 Position, Vector3 Target, Vector3 Forward, Vector3 Right, Vector3 Up) CalculateInCarPose(VehicleState vehicle, bool lookBehind)
    {
        Matrix bodyWorld = CreateBodyWorld(vehicle);
        Vector3 localPosition = new(0f, 1.15f, 0.32f);
        Vector3 localLook = lookBehind
            ? new Vector3(0f, -0.02f, -18f)
            : new Vector3(0f, -0.04f, 24f);
        Vector3 position = Vector3.Transform(localPosition, bodyWorld);
        Vector3 target = Vector3.Transform(localPosition + localLook, bodyWorld);
        Vector3 forward = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, bodyWorld));
        Vector3 right = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, bodyWorld));
        Vector3 up = Vector3.Normalize(Vector3.TransformNormal(Vector3.Up, bodyWorld));
        return (position, target, forward, right, up);
    }

    private static Matrix CreateBodyWorld(VehicleState vehicle)
    {
        float pivotHeight = MathHelper.Clamp(vehicle.BodyPivotHeightMeters, 0.25f, 1.10f);
        return Matrix.CreateTranslation(0f, -pivotHeight, 0f) *
               Matrix.CreateRotationX(vehicle.BodyPitchRadians) *
               Matrix.CreateRotationZ(vehicle.BodyRollRadians) *
               Matrix.CreateTranslation(0f, pivotHeight, 0f) *
               Matrix.CreateRotationY(vehicle.HeadingRadians) *
               Matrix.CreateTranslation(vehicle.Position);
    }

    private static Vector3 GetSafeHorizontal(Vector3 value, Vector3 fallback)
    {
        value.Y = 0f;
        if (value.LengthSquared() <= 0.0001f)
        {
            fallback.Y = 0f;
            return fallback.LengthSquared() <= 0.0001f ? Vector3.Forward : Vector3.Normalize(fallback);
        }

        return Vector3.Normalize(value);
    }

    private readonly record struct ChaseCameraProfile(
        float Distance,
        float Height,
        float TargetDistance,
        float TargetHeight,
        float FovDegrees,
        float PositionResponse,
        float TargetResponse,
        float YawResponse,
        float SurfaceShake,
        float PowertrainShake,
        float TargetShakeIntensity,
        float PositionShakeIntensity,
        float DriftLookAheadFactor,
        float TrackUpInfluence,
        float MinimumGroundClearance,
        float ImpactImpulseStrength);
}

public readonly record struct ChaseCameraIntentDebug(
    float SignedForwardSpeedMetersPerSecond,
    int Gear,
    bool ManualLookBehind,
    bool Reversing,
    string Reason,
    Vector3 Forward);
