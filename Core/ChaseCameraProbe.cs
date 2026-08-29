using Microsoft.Xna.Framework;
using RType.Camera;
using RType.Vehicle;

namespace RType.Core;

public static class ChaseCameraProbe
{
    public static void Run()
    {
        ProbeStationaryChaseSettle(CameraMode.Chase1);
        ProbeStationaryChaseSettle(CameraMode.Chase2);
        ProbeHighSpeedChaseDistance(CameraMode.Chase1);
        ProbeHighSpeedChaseDistance(CameraMode.Chase2);
    }

    private static void ProbeStationaryChaseSettle(CameraMode mode)
    {
        VehicleState state = new()
        {
            Position = new Vector3(12f, 0f, 24f),
            HeadingRadians = 0.34f,
            Velocity = Vector2.Zero,
            Gear = 1,
            Rpm = 900f,
            DisplayedRpm = 900f,
            PowertrainShockIntensity = 1f,
            MechanicalOverRevSeverity = 1f,
            RevLimiterBounceIntensity = 1f,
            SurfaceRumbleLeft = 1f,
            SurfaceRumbleRight = 1f,
            GroundPitchRadians = 0.08f,
            GroundRollRadians = -0.06f
        };

        ChaseCamera camera = new(16f / 9f);
        camera.SetMode(mode, state, reset: true);

        Vector3 previousPosition = camera.Position;
        Vector3 previousTarget = camera.Target;
        float maximumPositionDelta = 0f;
        float maximumTargetDelta = 0f;
        const float dt = 1f / 60f;

        for (int i = 0; i < 240; i++)
        {
            camera.Update(state, dt, lookBehind: false);
            maximumPositionDelta = MathF.Max(maximumPositionDelta, Vector3.Distance(camera.Position, previousPosition));
            maximumTargetDelta = MathF.Max(maximumTargetDelta, Vector3.Distance(camera.Target, previousTarget));
            previousPosition = camera.Position;
            previousTarget = camera.Target;
        }

        if (maximumPositionDelta > 0.0005f || maximumTargetDelta > 0.0005f)
        {
            throw new InvalidOperationException(
                $"Chase camera probe failed: {mode} drifts at standstill. Position delta {maximumPositionDelta:0.000000}, target delta {maximumTargetDelta:0.000000}.");
        }

        Console.WriteLine($"{mode} stationary settle: positionDelta={maximumPositionDelta:0.000000}, targetDelta={maximumTargetDelta:0.000000}");
    }

    private static void ProbeHighSpeedChaseDistance(CameraMode mode)
    {
        VehicleState state = new()
        {
            Position = new Vector3(0f, 0f, 0f),
            HeadingRadians = 0f,
            Velocity = Vector2.Zero,
            Gear = 4,
            Rpm = 6200f,
            DisplayedRpm = 6200f
        };

        ChaseCamera camera = new(16f / 9f);
        camera.SetMode(mode, state, reset: true);

        float initialDistance = HorizontalDistance(camera.Position, state.Position);
        const float dt = 1f / 60f;
        Vector2 velocity = new(0f, 55f);
        state.Velocity = velocity;

        for (int i = 0; i < 360; i++)
        {
            state.Position += new Vector3(velocity.X * dt, 0f, velocity.Y * dt);
            camera.Update(state, dt, lookBehind: false);
        }

        float highSpeedDistance = HorizontalDistance(camera.Position, state.Position);
        float distanceDelta = MathF.Abs(highSpeedDistance - initialDistance);
        if (distanceDelta > 0.20f)
        {
            throw new InvalidOperationException(
                $"Chase camera probe failed: {mode} speed changes framing. Initial {initialDistance:0.000}, high-speed {highSpeedDistance:0.000}, delta {distanceDelta:0.000}.");
        }

        Console.WriteLine($"{mode} high-speed framing: initialDistance={initialDistance:0.000}, highSpeedDistance={highSpeedDistance:0.000}, delta={distanceDelta:0.000}");
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}
