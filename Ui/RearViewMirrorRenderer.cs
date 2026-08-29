using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RType.Camera;
using RType.Rendering;
using RType.Vehicle;
using RType.World;

namespace RType.Ui;

public sealed class RearViewMirrorRenderer : IDisposable
{
    public const int MirrorWidth = 539;
    public const int MirrorHeight = 152;
    public const int CornerRadius = 23;
    public const int TopMargin = 20;

    private const string SurroundPath = "Assets/Menus/Backgrounds/Racing/RacingPauseMenu_ReverseMirrorSurround.png";

    private readonly GraphicsDevice _graphicsDevice;
    private readonly RenderTarget2D _mirrorTarget;
    private readonly Texture2D _surround;
    private readonly Texture2D _pixel;

    public RearViewMirrorRenderer(GraphicsDevice graphicsDevice)
    {
        _graphicsDevice = graphicsDevice;
        _mirrorTarget = new RenderTarget2D(
            graphicsDevice,
            MirrorWidth,
            MirrorHeight,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24);
        _surround = LoadTexture(graphicsDevice, SurroundPath);
        _pixel = new Texture2D(graphicsDevice, 1, 1);
        _pixel.SetData([Color.White]);
    }

    public Rectangle MirrorBounds => new((RacingGameWidth - MirrorWidth) / 2, TopMargin, MirrorWidth, MirrorHeight);

    private static int RacingGameWidth => 1920;

    public void Render(SceneRenderer sceneRenderer, TrackScene track, VehicleState vehicle)
    {
        _graphicsDevice.SetRenderTarget(_mirrorTarget);
        _graphicsDevice.Viewport = new Viewport(0, 0, MirrorWidth, MirrorHeight);
        _graphicsDevice.Clear(SceneRenderer.FogColor);

        SceneCamera rearCamera = ChaseCamera.CreateRearViewMirrorCamera(vehicle, MirrorWidth / (float)MirrorHeight);
        sceneRenderer.Draw(track, vehicle, rearCamera, drawVehicle: false);
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        Rectangle mirror = MirrorBounds;
        DrawCornerOcclusion(spriteBatch, mirror);
        spriteBatch.Draw(
            _mirrorTarget,
            mirror,
            null,
            Color.White,
            0f,
            Vector2.Zero,
            SpriteEffects.FlipHorizontally,
            0f);

        DrawCornerOcclusion(spriteBatch, mirror);

        Rectangle surround = new(
            mirror.X - (_surround.Width - mirror.Width) / 2,
            mirror.Y - (_surround.Height - mirror.Height) / 2,
            _surround.Width,
            _surround.Height);
        spriteBatch.Draw(_surround, surround, Color.White);
    }

    public void Dispose()
    {
        _pixel.Dispose();
        _surround.Dispose();
        _mirrorTarget.Dispose();
    }

    private void DrawCornerOcclusion(SpriteBatch spriteBatch, Rectangle bounds)
    {
        Color cover = new(8, 9, 10, 240);
        int radius = CornerRadius;
        DrawCorner(spriteBatch, bounds.X, bounds.Y, radius, top: true, left: true, cover);
        DrawCorner(spriteBatch, bounds.Right - radius, bounds.Y, radius, top: true, left: false, cover);
        DrawCorner(spriteBatch, bounds.X, bounds.Bottom - radius, radius, top: false, left: true, cover);
        DrawCorner(spriteBatch, bounds.Right - radius, bounds.Bottom - radius, radius, top: false, left: false, cover);
    }

    private void DrawCorner(SpriteBatch spriteBatch, int x, int y, int radius, bool top, bool left, Color color)
    {
        for (int row = 0; row < radius; row++)
        {
            float dy = top ? radius - row : row + 1;
            float inside = MathF.Sqrt(MathF.Max(0f, radius * radius - dy * dy));
            int fill = Math.Clamp(radius - (int)MathF.Ceiling(inside), 0, radius);
            if (fill <= 0)
            {
                continue;
            }

            int drawX = left ? x : x + radius - fill;
            spriteBatch.Draw(_pixel, new Rectangle(drawX, y + row, fill, 1), color);
        }
    }

    private static Texture2D LoadTexture(GraphicsDevice graphicsDevice, string path)
    {
        string resolved = Path.Combine(AppContext.BaseDirectory, path);
        if (!File.Exists(resolved))
        {
            resolved = path;
        }

        using FileStream stream = File.OpenRead(resolved);
        return Texture2D.FromStream(graphicsDevice, stream);
    }
}
