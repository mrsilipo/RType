using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RType.Ui;

namespace RType.Core;

public sealed class RTypeEngineRoomGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly int? _autoExitMilliseconds;
    private readonly GameLaunchOptions _launchOptions;
    private SpriteBatch? _spriteBatch;
    private RTypeEngineRoomScreen? _screen;
    private TimeSpan _elapsed;

    private RTypeEngineRoomGame(GameLaunchOptions launchOptions)
    {
        _launchOptions = launchOptions;
        _autoExitMilliseconds = launchOptions.AutoExitMilliseconds;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = UiLayout.Width,
            PreferredBackBufferHeight = UiLayout.Height,
            SynchronizeWithVerticalRetrace = true
        };

        Window.Title = "R Type Honda Racing - Race Engine Room";
        Window.AllowUserResizing = false;
        IsMouseVisible = false;
        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0);
    }

    public static RTypeEngineRoomGame CreateFromArgs(string[] args)
    {
        return new RTypeEngineRoomGame(GameLaunchOptions.FromArgs(args));
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _screen = new RTypeEngineRoomScreen(GraphicsDevice, _launchOptions);
    }

    protected override void UnloadContent()
    {
        _screen?.Dispose();
        _spriteBatch?.Dispose();
    }

    protected override void Update(GameTime gameTime)
    {
        _elapsed += gameTime.ElapsedGameTime;
        _screen?.Update(gameTime);

        if (_screen?.ExitRequested == true ||
            _autoExitMilliseconds is int autoExitMs &&
            _elapsed.TotalMilliseconds >= autoExitMs)
        {
            Exit();
            return;
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_spriteBatch is null || _screen is null)
        {
            return;
        }

        GraphicsDevice.Clear(Color.Black);
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        _screen.Draw(_spriteBatch);
        _spriteBatch.End();
        base.Draw(gameTime);
    }
}
