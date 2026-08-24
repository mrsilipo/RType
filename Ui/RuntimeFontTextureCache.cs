using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DrawingFont = System.Drawing.Font;
using DrawingPointF = System.Drawing.PointF;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSizeF = System.Drawing.SizeF;
using DrawingStringFormat = System.Drawing.StringFormat;
using XnaColor = Microsoft.Xna.Framework.Color;

namespace RType.Ui;

public enum TachometerFontRole
{
    Orbitron,
    Dseg7ClassicBold
}

public sealed class RuntimeFontTextureCache : IDisposable
{
    private const int PaddingPixels = 4;

    private readonly GraphicsDevice _graphicsDevice;
    private readonly LoadedFont _orbitron;
    private readonly LoadedFont _dseg7ClassicBold;
    private readonly Dictionary<TextKey, CachedText> _cache = new();

    public RuntimeFontTextureCache(GraphicsDevice graphicsDevice, TachometerFontConfig fonts)
    {
        _graphicsDevice = graphicsDevice;
        _orbitron = new LoadedFont(ResolveAssetPath(fonts.OrbitronPath));
        _dseg7ClassicBold = new LoadedFont(ResolveAssetPath(fonts.Dseg7ClassicBoldPath));
    }

    public Vector2 Measure(TachometerFontRole role, string text, float size, int weight)
    {
        CachedText cached = GetOrCreate(role, text, size, weight);
        return new Vector2(cached.Width, cached.Height);
    }

    public void Draw(
        SpriteBatch spriteBatch,
        TachometerFontRole role,
        string text,
        Vector2 position,
        float size,
        int weight,
        XnaColor color)
    {
        CachedText cached = GetOrCreate(role, text, size, weight);
        spriteBatch.Draw(cached.Texture, position, color);
    }

    public void DrawCentered(
        SpriteBatch spriteBatch,
        TachometerFontRole role,
        string text,
        Vector2 center,
        float size,
        int weight,
        XnaColor color)
    {
        Vector2 measured = Measure(role, text, size, weight);
        Draw(spriteBatch, role, text, center - measured * 0.5f, size, weight, color);
    }

    public void Dispose()
    {
        foreach (CachedText cached in _cache.Values)
        {
            cached.Texture.Dispose();
        }

        _cache.Clear();
        _orbitron.Dispose();
        _dseg7ClassicBold.Dispose();
    }

    private CachedText GetOrCreate(TachometerFontRole role, string text, float size, int weight)
    {
        text = string.IsNullOrEmpty(text) ? " " : text;
        TextKey key = new(role, text, MathF.Round(size * 10f) / 10f, weight);
        if (_cache.TryGetValue(key, out CachedText? cached))
        {
            return cached;
        }

        cached = CreateTexture(key);
        _cache.Add(key, cached);
        return cached;
    }

    private CachedText CreateTexture(TextKey key)
    {
        LoadedFont loadedFont = key.Role == TachometerFontRole.Orbitron
            ? _orbitron
            : _dseg7ClassicBold;
        FontStyle style = key.Role == TachometerFontRole.Orbitron && key.Weight >= 650
            ? FontStyle.Bold
            : FontStyle.Regular;

        using DrawingFont font = loadedFont.CreateFont(key.Size, style);
        using DrawingStringFormat format = DrawingStringFormat.GenericTypographic;
        format.FormatFlags |= StringFormatFlags.NoClip;

        DrawingSizeF measured;
        using (Bitmap measureBitmap = new(1, 1))
        using (Graphics measureGraphics = Graphics.FromImage(measureBitmap))
        {
            ConfigureGraphics(measureGraphics);
            measured = measureGraphics.MeasureString(key.Text, font, int.MaxValue, format);
        }

        int width = Math.Max(1, (int)MathF.Ceiling(measured.Width) + PaddingPixels * 2 + 2);
        int height = Math.Max(1, (int)MathF.Ceiling(measured.Height) + PaddingPixels * 2 + 2);
        using Bitmap bitmap = new(width, height, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        using (Brush brush = new SolidBrush(System.Drawing.Color.White))
        {
            ConfigureGraphics(graphics);
            graphics.Clear(System.Drawing.Color.Transparent);
            graphics.DrawString(key.Text, font, brush, new DrawingPointF(PaddingPixels, PaddingPixels), format);
        }

        Texture2D texture = new(_graphicsDevice, width, height, false, SurfaceFormat.Color);
        texture.SetData(ConvertBitmapToXnaColors(bitmap));
        return new CachedText(texture, width, height);
    }

    private static void ConfigureGraphics(Graphics graphics)
    {
        graphics.PageUnit = GraphicsUnit.Pixel;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
    }

    private static XnaColor[] ConvertBitmapToXnaColors(Bitmap bitmap)
    {
        DrawingRectangle rectangle = new(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int byteCount = Math.Abs(data.Stride) * bitmap.Height;
            byte[] bytes = new byte[byteCount];
            Marshal.Copy(data.Scan0, bytes, 0, byteCount);

            XnaColor[] pixels = new XnaColor[bitmap.Width * bitmap.Height];
            for (int y = 0; y < bitmap.Height; y++)
            {
                int sourceRow = y * data.Stride;
                for (int x = 0; x < bitmap.Width; x++)
                {
                    int source = sourceRow + x * 4;
                    byte b = bytes[source];
                    byte g = bytes[source + 1];
                    byte r = bytes[source + 2];
                    byte a = bytes[source + 3];
                    pixels[y * bitmap.Width + x] = new XnaColor(
                        Premultiply(r, a),
                        Premultiply(g, a),
                        Premultiply(b, a),
                        a);
                }
            }

            return pixels;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static byte Premultiply(byte color, byte alpha)
    {
        return (byte)((color * alpha + 127) / 255);
    }

    private static string ResolveAssetPath(string relativePath)
    {
        string fromWorkingDirectory = Path.GetFullPath(relativePath);
        if (File.Exists(fromWorkingDirectory))
        {
            return fromWorkingDirectory;
        }

        string fromOutputDirectory = Path.Combine(AppContext.BaseDirectory, relativePath);
        if (File.Exists(fromOutputDirectory))
        {
            return fromOutputDirectory;
        }

        throw new FileNotFoundException($"Font file not found: {relativePath}", relativePath);
    }

    private readonly record struct TextKey(TachometerFontRole Role, string Text, float Size, int Weight);

    private sealed record CachedText(Texture2D Texture, int Width, int Height);

    private sealed class LoadedFont : IDisposable
    {
        private readonly PrivateFontCollection _collection = new();
        private readonly FontFamily _family;

        public LoadedFont(string path)
        {
            _collection.AddFontFile(path);
            _family = _collection.Families[0];
        }

        public DrawingFont CreateFont(float size, FontStyle style)
        {
            return new DrawingFont(_family, MathF.Max(1f, size), style, GraphicsUnit.Pixel);
        }

        public void Dispose()
        {
            _family.Dispose();
            _collection.Dispose();
        }
    }
}
