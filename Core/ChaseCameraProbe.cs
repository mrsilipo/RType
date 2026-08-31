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
        ProbeFixedDiagnosticStaysWorldLocked();
        ProbeHighSpeedChaseDistance(CameraMode.Chase1);
        ProbeHighSpeedChaseDistance(CameraMode.Chase2);
        ProbeLowSpeedSlipLookAheadContinuity();
        ProbeLookBehindClassification();
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

    private static void ProbeFixedDiagnosticStaysWorldLocked()
    {
        VehicleState state = new()
        {
            Position = new Vector3(4f, 0f, 8f),
            HeadingRadians = 0.2f,
            Velocity = new Vector2(0f, 4f),
            Gear = 1,
            Rpm = 1800f,
            DisplayedRpm = 1800f
        };

        ChaseCamera camera = new(16f / 9f);
        camera.SetMode(CameraMode.FixedDiagnostic, state, reset: true);
        Vector3 lockedPosition = camera.Position;
        Vector3 lockedTarget = camera.Target;

        const float dt = 1f / 60f;
        for (int i = 0; i < 180; i++)
        {
            state.HeadingRadians += 0.02f;
            state.Position += new Vector3(0.03f, 0f, 0.08f);
            camera.Update(state, dt, lookBehind: i > 60 && i < 90);
        }

        float positionDelta = Vector3.Distance(camera.Position, lockedPosition);
        float targetDelta = Vector3.Distance(camera.Target, lockedTarget);
        if (positionDelta > 0.0005f || targetDelta > 0.0005f)
        {
            throw new InvalidOperationException(
                $"Chase camera probe failed: fixed diagnostic camera followed the car. Position delta {positionDelta:0.000000}, target delta {targetDelta:0.000000}.");
        }

        Console.WriteLine(
            $"FixedDiagnostic world lock: positionDelta={positionDelta:0.000000}, targetDelta={targetDelta:0.000000}");
    }

    private static void ProbeLowSpeedSlipLookAheadContinuity()
    {
        VehicleState state = new()
        {
            Position = Vector3.Zero,
            HeadingRadians = 0f,
            Velocity = new Vector2(0.85f, 2.25f),
            Gear = 1,
            Rpm = 1600f,
            DisplayedRpm = 1600f
        };

        ChaseCamera camera = new(16f / 9f);
        camera.SetMode(CameraMode.Chase1, state, reset: true);

        const float dt = 1f / 60f;
        float previousTargetSide = Vector3.Dot(camera.Target - state.Position, state.Right);
        float maximumTargetSideStep = 0f;

        for (int i = 0; i <= 72; i++)
        {
            float forwardSpeed = MathHelper.Lerp(2.25f, 4.50f, i / 72f);
            state.Velocity = new Vector2(0.85f, forwardSpeed);
            camera.Update(state, dt, lookBehind: false);

            float targetSide = Vector3.Dot(camera.Target - state.Position, state.Right);
            maximumTargetSideStep = MathF.Max(maximumTargetSideStep, MathF.Abs(targetSide - previousTargetSide));
            previousTargetSide = targetSide;
        }

        if (maximumTargetSideStep > 0.018f)
        {
            throw new InvalidOperationException(
                $"Chase camera probe failed: low-speed slip look-ahead has a target-side step of {maximumTargetSideStep:0.0000}m.");
        }

        Console.WriteLine(
            $"Chase1 low-speed slip look-ahead continuity: maxTargetSideStep={maximumTargetSideStep:0.0000}m");
    }

    private static void ProbeLookBehindClassification()
    {
        VehicleState state = new()
        {
            Position = Vector3.Zero,
            HeadingRadians = 0f,
            Velocity = new Vector2(0f, -3.2f),
            Gear = 1,
            Rpm = 1400f,
            DisplayedRpm = 1400f
        };

        ChaseCameraIntentDebug forwardGearSlide = ChaseCamera.GetIntentDebug(state, lookBehind: false);
        if (forwardGearSlide.Reversing || forwardGearSlide.Reason != "vehicle-forward")
        {
            throw new InvalidOperationException(
                $"Chase camera probe failed: forward gear negative signed speed triggered reverse camera. " +
                $"reason={forwardGearSlide.Reason}, signedForward={forwardGearSlide.SignedForwardSpeedMetersPerSecond:0.00}m/s.");
        }

        state.Gear = -1;
        ChaseCameraIntentDebug reverseGear = ChaseCamera.GetIntentDebug(state, lookBehind: false);
        if (reverseGear.Reason != "vehicle-forward")
        {
            throw new InvalidOperationException(
                $"Chase camera probe failed: reverse gear triggered camera flip. " +
                $"reason={reverseGear.Reason}, signedForward={reverseGear.SignedForwardSpeedMetersPerSecond:0.00}m/s.");
        }

        state.Gear = 1;
        ChaseCameraIntentDebug manualLookBehind = ChaseCamera.GetIntentDebug(state, lookBehind: true);
        if (!manualLookBehind.ManualLookBehind || manualLookBehind.Reason != "manual-look-behind")
        {
            throw new InvalidOperationException(
                $"Chase camera probe failed: manual look-behind did not override camera intent. reason={manualLookBehind.Reason}.");
        }

        Console.WriteLine(
            "look-behind classification: forward/reverse gear stay vehicle-forward; manual look-behind flips.");
    }
}
