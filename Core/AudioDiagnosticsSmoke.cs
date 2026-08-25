using RType.Audio;
using RType.Camera;
using RType.Data;
using RType.Vehicle;
using Microsoft.Xna.Framework;

namespace RType.Core;

public static class AudioDiagnosticsSmoke
{
    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleDefinitionLoader.LoadSimulationParameters(options.VehicleDefinitionPath);
        using VehicleAudioSystem audio = new();
        audio.SetVehicle(parameters.Audio);

        VehicleState state = new()
        {
            RedlineRpm = parameters.RedlineRpm,
            Rpm = parameters.IdleRpm,
            DisplayedRpm = parameters.IdleRpm,
            Throttle = 1f,
            EffectiveThrottle = 1f,
            Velocity = new Vector2(0f, 20f),
            Gear = 1
        };

        Tick(audio, state, parameters.IdleRpm, limiter: false);
        Tick(audio, state, 3500f, limiter: false);
        Tick(audio, state, 6200f, limiter: false);
        Tick(audio, state, 8300f, limiter: true);
        Thread.Sleep(250);
        audio.Stop();

        Console.WriteLine($"Audio diagnostics log: {AudioDiagnostics.LogFilePath}");
    }

    private static void Tick(VehicleAudioSystem audio, VehicleState state, float rpm, bool limiter)
    {
        const float dt = 1f / 60f;
        for (int i = 0; i < 90; i++)
        {
            state.Rpm = rpm;
            state.RevLimiterActive = limiter;
            state.RevLimiterBounceIntensity = limiter ? 1f : 0f;
            state.RevLimiterBouncePhase = limiter
                ? RevLimiterPresentationRules.AdvanceBouncePhase(state.RevLimiterBouncePhase, state.RedlineRpm, dt)
                : 0f;
            RpmPresentationSmoother.Update(state, dt);
            audio.Update(state, CameraMode.Chase1, active: true, paused: false, dt);
            Thread.Sleep(16);
        }
    }
}
