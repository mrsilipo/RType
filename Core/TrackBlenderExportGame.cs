using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RType.Data;
using RType.Rendering;
using RType.World;

namespace RType.Core;

internal sealed class TrackBlenderExportGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly string _trackIdOrName;
    private bool _exported;

    private TrackBlenderExportGame(string trackIdOrName)
    {
        _trackIdOrName = trackIdOrName;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 64,
            PreferredBackBufferHeight = 64
        };
        IsMouseVisible = false;
    }

    public static void RunFromArgs(string[] args)
    {
        string trackIdOrName = "HighSpeedRing";
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--export-track-blender", StringComparison.OrdinalIgnoreCase) &&
                i + 1 < args.Length)
            {
                trackIdOrName = args[i + 1];
                break;
            }
        }

        using TrackBlenderExportGame game = new(trackIdOrName);
        game.Run();
    }

    protected override void Update(GameTime gameTime)
    {
        if (_exported)
        {
            Exit();
            return;
        }

        _exported = true;
        Export();
        Exit();
    }

    private void Export()
    {
        TrackDefinitionFile[] files = TrackDefinitionFileLoader.LoadFiles(TrackDefinitionFileLoader.DefaultTrackDirectory);
        TrackDefinitionFile sourceFile = files.FirstOrDefault(file => MatchesTrack(file.Definition, _trackIdOrName));
        if (sourceFile.Definition is null)
        {
            throw new InvalidOperationException($"Track '{_trackIdOrName}' was not found in {TrackDefinitionFileLoader.DefaultTrackDirectory}.");
        }

        if (sourceFile.Definition.Layout != TrackLayout.HighSpeedRing)
        {
            throw new InvalidOperationException("--export-track-blender currently supports the generated High Speed Ring layout only.");
        }

        using GeneratedTextures textures = GeneratedTextures.Create(GraphicsDevice);
        SurfaceLibrary surfaces = SurfaceLibraryLoader.Load(GameLaunchOptions.DefaultSurfaceDefinitionPath);
        using TrackScene track = TrackScene.Create(
            GraphicsDevice,
            textures,
            sourceFile.Definition,
            reverse: false,
            surfaces,
            TrackVisualMode.Generated);
        using ProceduralBackdropRenderer backdrop = new(GraphicsDevice, textures);

        HighSpeedRingBlenderExporter.Export(
            GraphicsDevice,
            textures,
            sourceFile,
            track,
            backdrop.Meshes);
    }

    private static bool MatchesTrack(TrackDefinition definition, string idOrName)
    {
        string normalized = Normalize(idOrName);
        return Normalize(definition.Id) == normalized ||
               Normalize(definition.DisplayName) == normalized ||
               Normalize(definition.Layout.ToString()) == normalized;
    }

    private static string Normalize(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }
}
