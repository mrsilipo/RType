using RetroRacer.Core;

if (args.Any(arg => arg.Equals("--physics-smoke-test", StringComparison.OrdinalIgnoreCase)))
{
    PhysicsSmokeTest.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--handling-probe", StringComparison.OrdinalIgnoreCase)))
{
    HandlingProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--launch-probe", StringComparison.OrdinalIgnoreCase)))
{
    LaunchProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--shift-probe", StringComparison.OrdinalIgnoreCase)))
{
    ShiftProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--performance-probe", StringComparison.OrdinalIgnoreCase)))
{
    PerformanceProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--track-geometry-probe", StringComparison.OrdinalIgnoreCase)))
{
    TrackGeometryProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--track-editor", StringComparison.OrdinalIgnoreCase)))
{
    using var editor = TrackEditorGame.CreateFromArgs(args);
    editor.Run();
    return;
}

if (args.Any(arg => arg.Equals("--track-editor-probe", StringComparison.OrdinalIgnoreCase)))
{
    TrackEditorTool.Run();
    return;
}

if (args.Any(arg => arg.Equals("--audio-probe", StringComparison.OrdinalIgnoreCase)))
{
    AudioProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--engine-sim-probe", StringComparison.OrdinalIgnoreCase)))
{
    EngineSimProfileProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--engine-sim-power-probe", StringComparison.OrdinalIgnoreCase)))
{
    EngineSimPowerProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--engine-sim-fidelity-probe", StringComparison.OrdinalIgnoreCase)))
{
    EngineSimFidelityProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--engine-sim-stream-stress", StringComparison.OrdinalIgnoreCase)))
{
    EngineSimStreamStressProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--engine-sim-compare", StringComparison.OrdinalIgnoreCase)))
{
    int referenceIndex = Array.FindIndex(args, arg => arg.Equals("--reference-wav", StringComparison.OrdinalIgnoreCase));
    if (referenceIndex < 0 || referenceIndex + 1 >= args.Length)
    {
        Console.Error.WriteLine("Usage: --engine-sim-compare --reference-wav <path>");
        return;
    }

    EngineSimWaveformCompareTool.Run(GameLaunchOptions.FromArgs(args), args[referenceIndex + 1]);
    return;
}

if (args.Any(arg => arg.Equals("--render-engine-audio", StringComparison.OrdinalIgnoreCase)))
{
    EngineAudioRenderTool.Run();
    return;
}

if (args.Any(arg => arg.Equals("--audio-diagnostics-smoke", StringComparison.OrdinalIgnoreCase)))
{
    AudioDiagnosticsSmoke.Run(GameLaunchOptions.FromArgs(args));
    return;
}

using var game = new RacingGame(GameLaunchOptions.FromArgs(args));
game.Run();
