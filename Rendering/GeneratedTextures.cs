using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RetroRacer.Rendering;

public sealed class GeneratedTextures : IDisposable
{
    public const string LakesideRoadTexturePath = "Assets/Textures/Tracks/Lakeside/road.png";

    private GeneratedTextures(
        Texture2D road,
        Texture2D grass,
        Texture2D curb,
        Texture2D white,
        Texture2D carRed,
        Texture2D carGlass,
        Texture2D tire,
        Texture2D shadow)
    {
        Road = road;
        Grass = grass;
        Curb = curb;
        White = white;
        CarRed = carRed;
        CarGlass = carGlass;
        Tire = tire;
        Shadow = shadow;
    }

    public Texture2D Road { get; }

    public Texture2D Grass { get; }

    public Texture2D Curb { get; }

    public Texture2D White { get; }

    public Texture2D CarRed { get; }

    public Texture2D CarGlass { get; }

    public Texture2D Tire { get; }

    public Texture2D Shadow { get; }

    public static GeneratedTextures Create(GraphicsDevice graphicsDevice)
    {
        return new GeneratedTextures(
            LoadTextureOrCreate(graphicsDevice, LakesideRoadTexturePath, CreateRoad),
            CreateGrass(graphicsDevice),
            CreateCurb(graphicsDevice),
            CreateSolid(graphicsDevice, new Color(230, 230, 210)),
            CreateSolid(graphicsDevice, new Color(178, 42, 36)),
            CreateSolid(graphicsDevice, new Color(36, 58, 74)),
            CreateSolid(graphicsDevice, new Color(14, 14, 16)),
            CreateShadow(graphicsDevice));
    }

    public void Dispose()
    {
        Road.Dispose();
        Grass.Dispose();
        Curb.Dispose();
        White.Dispose();
        CarRed.Dispose();
        CarGlass.Dispose();
        Tire.Dispose();
        Shadow.Dispose();
    }

    private static Texture2D CreateRoad(GraphicsDevice graphicsDevice)
    {
        return CreateTexture(graphicsDevice, 16, 16, (x, y) =>
        {
            int n = Hash(x, y) % 20;
            int value = 45 + n;
            return new Color(value, value, value + 2);
        });
    }

    private static Texture2D CreateGrass(GraphicsDevice graphicsDevice)
    {
        return CreateTexture(graphicsDevice, 16, 16, (x, y) =>
        {
            int n = Hash(x * 3, y * 5) % 28;
            bool clump = ((x / 4) + (y / 3)) % 2 == 0;
            return clump
                ? new Color(35 + n / 2, 98 + n, 44 + n / 3)
                : new Color(24 + n / 3, 78 + n, 34 + n / 4);
        });
    }

    private static Texture2D CreateCurb(GraphicsDevice graphicsDevice)
    {
        return CreateTexture(graphicsDevice, 16, 16, (x, y) =>
        {
            bool red = ((y / 4) + (x / 8)) % 2 == 0;
            return red ? new Color(166, 28, 30) : new Color(230, 230, 215);
        });
    }

    private static Texture2D CreateShadow(GraphicsDevice graphicsDevice)
    {
        return CreateTexture(graphicsDevice, 32, 16, (x, y) =>
        {
            float nx = (x + 0.5f - 16f) / 16f;
            float ny = (y + 0.5f - 8f) / 8f;
            float distance = nx * nx + ny * ny;
            int alpha = distance < 1f ? (int)(135f * (1f - distance)) : 0;
            return new Color(0, 0, 0, alpha);
        });
    }

    private static Texture2D CreateSolid(GraphicsDevice graphicsDevice, Color color)
    {
        return CreateTexture(graphicsDevice, 2, 2, (_, _) => color);
    }

    private static Texture2D CreateTexture(GraphicsDevice graphicsDevice, int width, int height, Func<int, int, Color> colorAt)
    {
        Color[] data = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                data[y * width + x] = colorAt(x, y);
            }
        }

        Texture2D texture = new(graphicsDevice, width, height, false, SurfaceFormat.Color);
        texture.SetData(data);
        return texture;
    }

    private static Texture2D LoadTextureOrCreate(
        GraphicsDevice graphicsDevice,
        string relativePath,
        Func<GraphicsDevice, Texture2D> createFallback)
    {
        foreach (string path in GetCandidateTexturePaths(relativePath))
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using FileStream stream = File.OpenRead(path);
                return Texture2D.FromStream(graphicsDevice, stream);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or NotSupportedException)
            {
                Console.Error.WriteLine($"Could not load texture '{path}': {exception.Message}");
            }
        }

        return createFallback(graphicsDevice);
    }

    private static IEnumerable<string> GetCandidateTexturePaths(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            yield return relativePath;
            yield break;
        }

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        string currentDirectoryPath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, relativePath));
        string outputDirectoryPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relativePath));

        if (seen.Add(currentDirectoryPath))
        {
            yield return currentDirectoryPath;
        }

        if (seen.Add(outputDirectoryPath))
        {
            yield return outputDirectoryPath;
        }
    }

    private static int Hash(int x, int y)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            return (h ^ (h >> 16)) & 0x7fffffff;
        }
    }
}
