using RType.Core;

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

if (args.Any(arg => arg.Equals("--rtype-engine-room", StringComparison.OrdinalIgnoreCase)))
{
    using var engineRoom = RTypeEngineRoomGame.CreateFromArgs(args);
    engineRoom.Run();
    return;
}

if (args.Any(arg => arg.Equals("--audio-diagnostics-smoke", StringComparison.OrdinalIgnoreCase)))
{
    AudioDiagnosticsSmoke.Run(GameLaunchOptions.FromArgs(args));
    return;
}

using var game = new RacingGame(GameLaunchOptions.FromArgs(args));
game.Run();
