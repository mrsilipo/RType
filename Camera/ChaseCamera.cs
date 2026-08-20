using Microsoft.Xna.Framework;
using RetroRacer.Vehicle;

namespace RetroRacer.Camera;

public sealed class ChaseCamera
{
    private const float Chase1Distance = 6.4f;
    private const float Chase1Height = 3.0f;
    private const float Chase1TargetDistance = 2.7f;
    private const float Chase2Distance = 13.2f;
    private const float Chase2Height = 5.2f;
    private const float Chase2TargetDistance = 5.6f;
    private const float PositionSmoothing = 7.5f;
    private const float TargetSmoothing = 10.0f;
    private float _smoothedPowertrainShock;
    private float _powertrainShockPhaseSeconds;
    private Vector3 _inCarHeadOffset;
    private Vector3 _inCarHeadVelocity;

    public ChaseCamera(float aspectRatio)
    {
        Projection = Matrix.CreatePerspectiveFieldOfView(
            MathHelper.ToRadians(62f),
            aspectRatio,
            0.1f,
            360f);
    }

    public Vector3 Position { get; private set; }

    public Vector3 Target { get; private set; }

    public Matrix View { get; private set; }

    public Matrix Projection { get; }

    public CameraMode Mode { get; private set; } = CameraMode.Chase1;

    public string ModeName => Mode switch
    {
        CameraMode.InCar => "IN CAR",
        CameraMode.Chase2 => "CHASE 2",
        _ => "CHASE 1"
    };

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

    public void CycleMode(VehicleState vehicle)
    {
        Mode = Mode switch
        {
            CameraMode.Chase1 => CameraMode.Chase2,
            CameraMode.Chase2 => CameraMode.InCar,
            _ => CameraMode.Chase1
        };
        Reset(vehicle);
    }

    public void Reset(VehicleState vehicle)
    {
        _smoothedPowertrainShock = 0f;
        _powertrainShockPhaseSeconds = 0f;
        _inCarHeadOffset = Vector3.Zero;
        _inCarHeadVelocity = Vector3.Zero;

        if (Mode == CameraMode.InCar)
        {
            ResetInCar(vehicle);
            return;
        }

        Vector3 forward = vehicle.Forward;
        (float distance, float height, float targetDistance) = GetChaseSettings();
        Position = vehicle.Position - forward * distance + Vector3.Up * height;
        Target = vehicle.Position + forward * targetDistance + Vector3.Up * 1.15f;
        View = Matrix.CreateLookAt(Position, Target, Vector3.Up);
    }

    public void Update(VehicleState vehicle, float dt, bool lookBehind)
    {
        if (Mode == CameraMode.InCar)
        {
            UpdateInCar(vehicle, dt, lookBehind);
            return;
        }

        Vector3 forward = lookBehind ? -vehicle.Forward : vehicle.Forward;
        Vector3 viewRight = lookBehind ? -vehicle.Right : vehicle.Right;
        (float distance, float height, float targetDistance) = GetChaseSettings();
        Vector3 desiredPosition = vehicle.Position - forward * distance + Vector3.Up * height;
        Vector3 desiredTarget = vehicle.Position + forward * targetDistance + Vector3.Up * 1.15f;

        float shock = UpdatePowertrainShock(vehicle, dt);
        float downshiftPull = CalculateDownshiftPull(vehicle);
        Vector3 jitter = CalculatePowertrainJitter(forward, viewRight, Vector3.Up, shock, Mode == CameraMode.Chase2 ? 1.15f : 1f);
        desiredPosition -= forward * downshiftPull * (Mode == CameraMode.Chase2 ? 1.20f : 0.82f);
        desiredTarget -= forward * downshiftPull * (Mode == CameraMode.Chase2 ? 0.36f : 0.24f);
        desiredPosition += jitter;
        desiredTarget += jitter * 0.42f;

        float positionBlend = 1f - MathF.Exp(-(PositionSmoothing + shock * 14f) * dt);
        float targetBlend = 1f - MathF.Exp(-(TargetSmoothing + shock * 18f) * dt);
        Position = Vector3.Lerp(Position, desiredPosition, positionBlend);
        Target = Vector3.Lerp(Target, desiredTarget, targetBlend);
        View = Matrix.CreateLookAt(Position, Target, Vector3.Up);
    }

    private (float Distance, float Height, float TargetDistance) GetChaseSettings()
    {
        return Mode == CameraMode.Chase2
            ? (Chase2Distance, Chase2Height, Chase2TargetDistance)
            : (Chase1Distance, Chase1Height, Chase1TargetDistance);
    }

    private void ResetInCar(VehicleState vehicle)
    {
        (Position, Target, _, _, Vector3 up) = CalculateInCarPose(vehicle, false);
        View = Matrix.CreateLookAt(Position, Target, up);
    }

    private void UpdateInCar(VehicleState vehicle, float dt, bool lookBehind)
    {
        (Vector3 desiredPosition, Vector3 desiredTarget, Vector3 bodyForward, Vector3 bodyRight, Vector3 bodyUp) =
            CalculateInCarPose(vehicle, lookBehind);
        Vector3 lookForward = lookBehind ? -bodyForward : bodyForward;
        Vector3 viewRight = lookBehind ? -bodyRight : bodyRight;
        float shock = UpdatePowertrainShock(vehicle, dt);
        float downshiftPull = CalculateDownshiftPull(vehicle);
        Vector3 headOffset = CalculateInCarHeadOffset(vehicle, bodyForward, bodyRight, bodyUp, shock, dt);
        Vector3 jitter = CalculatePowertrainJitter(lookForward, viewRight, bodyUp, shock, 0.52f);
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

    private float UpdatePowertrainShock(VehicleState vehicle, float dt)
    {
        float mechanicalShock = MathHelper.Clamp(
            MathF.Max(vehicle.PowertrainShockIntensity, vehicle.MechanicalOverRevSeverity * 0.35f),
            0f,
            1f);
        float limiterShake = vehicle.RevLimiterBounceIntensity * 0.16f;
        float targetShock = MathHelper.Clamp(MathF.Max(mechanicalShock, limiterShake), 0f, 1f);
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

    private float CalculateDownshiftPull(VehicleState vehicle)
    {
        return MathHelper.Clamp(
            MathF.Max(vehicle.PowertrainShockIntensity, vehicle.MechanicalOverRevSeverity * 0.25f),
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
}
