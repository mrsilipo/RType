using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RType.Rendering;

public sealed class GeneratedTextures : IDisposable
{
    public const string LakesideRoadTexturePath = "Assets/Textures/Tracks/Lakeside/road.png";
    private const string ReferenceTreeBroadPath = "Assets/Tracks/Textures/Reference Material Trees/000-bigtree2.png";
    private const string ReferenceTreePinesPath = "Assets/Tracks/Textures/Reference Material Trees/011-pines.png";
    private const string ReferenceTreeShrubPath = "Assets/Tracks/Textures/Reference Material Trees/012-ShrubBranch.png";

    private GeneratedTextures(
        Texture2D road,
        Texture2D grass,
        Texture2D curb,
        Texture2D white,
        Texture2D carRed,
        Texture2D carGlass,
        Texture2D tire,
        Texture2D distantEarth,
        Texture2D mountain,
        Texture2D treeClump,
        Texture2D treeClumpRound,
        Texture2D treeClumpTall,
        Texture2D referenceTreeBroad,
        Texture2D referenceTreePines,
        Texture2D referenceTreeShrub,
        Texture2D referenceTreeBroadAlt,
        Texture2D referenceTreePinesAlt,
        Texture2D referenceTreeShrubAlt,
        Texture2D chevronSign,
        Texture2D startBoard,
        Texture2D brakeMarkerSign,
        Texture2D taillightRedLens,
        Texture2D taillightClearLens,
        Texture2D shadow)
    {
        Road = road;
        Grass = grass;
        Curb = curb;
        White = white;
        CarRed = carRed;
        CarGlass = carGlass;
        Tire = tire;
        DistantEarth = distantEarth;
        Mountain = mountain;
        TreeClump = treeClump;
        TreeClumpRound = treeClumpRound;
        TreeClumpTall = treeClumpTall;
        ReferenceTreeBroad = referenceTreeBroad;
        ReferenceTreePines = referenceTreePines;
        ReferenceTreeShrub = referenceTreeShrub;
        ReferenceTreeBroadAlt = referenceTreeBroadAlt;
        ReferenceTreePinesAlt = referenceTreePinesAlt;
        ReferenceTreeShrubAlt = referenceTreeShrubAlt;
        ChevronSign = chevronSign;
        StartBoard = startBoard;
        BrakeMarkerSign = brakeMarkerSign;
        TaillightRedLens = taillightRedLens;
        TaillightClearLens = taillightClearLens;
        Shadow = shadow;
    }

    public Texture2D Road { get; }

    public Texture2D Grass { get; }

    public Texture2D Curb { get; }

    public Texture2D White { get; }

    public Texture2D CarRed { get; }

    public Texture2D CarGlass { get; }

    public Texture2D Tire { get; }

    public Texture2D DistantEarth { get; }

    public Texture2D Mountain { get; }

    public Texture2D TreeClump { get; }

    public Texture2D TreeClumpRound { get; }

    public Texture2D TreeClumpTall { get; }

    public Texture2D ReferenceTreeBroad { get; }

    public Texture2D ReferenceTreePines { get; }

    public Texture2D ReferenceTreeShrub { get; }

    public Texture2D ReferenceTreeBroadAlt { get; }

    public Texture2D ReferenceTreePinesAlt { get; }

    public Texture2D ReferenceTreeShrubAlt { get; }

    public Texture2D ChevronSign { get; }

    public Texture2D StartBoard { get; }

    public Texture2D BrakeMarkerSign { get; }

    public Texture2D TaillightRedLens { get; }

    public Texture2D TaillightClearLens { get; }

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
            CreateDistantEarth(graphicsDevice),
            CreateMountain(graphicsDevice),
            CreateTreeClump(graphicsDevice, TreeShape.Low),
            CreateTreeClump(graphicsDevice, TreeShape.Round),
            CreateTreeClump(graphicsDevice, TreeShape.Tall),
            LoadTreeTextureOrCreate(graphicsDevice, ReferenceTreeBroadPath, static device => CreateTreeClump(device, TreeShape.Round)),
            LoadTreeTextureOrCreate(graphicsDevice, ReferenceTreePinesPath, static device => CreateTreeClump(device, TreeShape.Tall)),
            LoadTreeTextureOrCreate(graphicsDevice, ReferenceTreeShrubPath, static device => CreateTreeClump(device, TreeShape.Low)),
            LoadTreeTextureOrCreate(graphicsDevice, ReferenceTreeBroadPath, static device => CreateTreeClump(device, TreeShape.Round), TreeTextureVariant.MirroredCool),
            LoadTreeTextureOrCreate(graphicsDevice, ReferenceTreePinesPath, static device => CreateTreeClump(device, TreeShape.Tall), TreeTextureVariant.WarmDense),
            LoadTreeTextureOrCreate(graphicsDevice, ReferenceTreeShrubPath, static device => CreateTreeClump(device, TreeShape.Low), TreeTextureVariant.DarkLow),
            CreateChevronSign(graphicsDevice),
            CreateStartBoard(graphicsDevice),
            CreateBrakeMarkerSign(graphicsDevice),
            CreateTaillightLens(graphicsDevice, clearLens: false),
            CreateTaillightLens(graphicsDevice, clearLens: true),
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
        DistantEarth.Dispose();
        Mountain.Dispose();
        TreeClump.Dispose();
        TreeClumpRound.Dispose();
        TreeClumpTall.Dispose();
        ReferenceTreeBroad.Dispose();
        ReferenceTreePines.Dispose();
        ReferenceTreeShrub.Dispose();
        ReferenceTreeBroadAlt.Dispose();
        ReferenceTreePinesAlt.Dispose();
        ReferenceTreeShrubAlt.Dispose();
        ChevronSign.Dispose();
        StartBoard.Dispose();
        BrakeMarkerSign.Dispose();
        TaillightRedLens.Dispose();
        TaillightClearLens.Dispose();
        Shadow.Dispose();
    }

    private static Texture2D CreateRoad(GraphicsDevice graphicsDevice)
    {
        return CreateTexture(graphicsDevice, 512, 512, (x, y) =>
        {
            float laneT = x / 511f;
            float longitudinalT = y / 511f;
            int coarse = Hash(x / 32, y / 48) % 18;
            int medium = Hash(x / 9, y / 13) % 12;
            int fine = Hash(x * 3, y * 5) % 10;
            int laneGrain = Hash(x / 3, y / 17) % 7;
            int tarNoise = Hash(x / 2 + 41, y / 2 + 17) % 12;
            bool darkerAggregate = Hash(x / 7, y / 5) % 19 == 0;
            bool paleAggregate = Hash(x / 5 + 37, y / 4 + 11) % 23 == 0;
            bool faintLongitudinalStreak = Hash(x / 21, y / 91) % 8 == 0;
            bool repairedPatch = Hash(x / 44, y / 38) % 17 == 0;
            bool fineCrack = Hash(x / 3 + y / 19, y / 3 - x / 23) % 107 == 0;
            float wheelTrackA = MathF.Exp(-MathF.Pow((laneT - 0.34f) / 0.065f, 2f));
            float wheelTrackB = MathF.Exp(-MathF.Pow((laneT - 0.66f) / 0.065f, 2f));
            float rubberBand = MathHelper.Clamp((wheelTrackA + wheelTrackB) * 0.5f, 0f, 1f);
            float broadStain = MathF.Sin(longitudinalT * MathF.Tau * 2.4f + laneT * 5.5f) * 0.5f + 0.5f;

            int value = 42 + coarse / 2 + medium / 2 + fine / 3 + laneGrain / 4;
            value -= (int)(rubberBand * (7f + broadStain * 5f));
            value += repairedPatch ? 5 - tarNoise / 4 : 0;
            if (darkerAggregate)
            {
                value -= 9 + fine / 2;
            }

            if (paleAggregate)
            {
                value += 7;
            }

            if (faintLongitudinalStreak)
            {
                value += 4;
            }

            if (fineCrack)
            {
                value -= 9;
            }

            value = Math.Clamp(value, 26, 76);
            int warm = repairedPatch ? 1 : 0;
            return new Color(value + warm, value + warm, value + 2);
        });
    }

    private static Texture2D CreateGrass(GraphicsDevice graphicsDevice)
    {
        return CreateTexture(graphicsDevice, 64, 64, (x, y) =>
        {
            int n = Hash(x * 5, y * 7) % 42;
            int blade = Hash(x * 17 + 3, y * 11 + 19) % 24;
            bool clump = ((x / 2) + (y / 2) + Hash(x / 4, y / 4) % 3) % 2 == 0;
            bool darkBlade = Hash(x * 3 + 13, y * 5 + 29) % 6 == 0;
            bool straw = Hash(x + 11, y * 7) % 8 == 0;
            bool chalkFleck = Hash(x / 2 + 29, y / 3 + 71) % 31 == 0;
            bool shadowClump = Hash(x / 5, y / 5) % 9 == 0;
            int windLine = (x + y * 2 + Hash(x / 8, y / 8) % 5) % 13;
            if (chalkFleck)
            {
                return new Color(150 + n / 4, 151 + n / 5, 106 + n / 6);
            }

            if (straw)
            {
                return new Color(123 + n / 3, 130 + n / 4, 73 + n / 5);
            }

            if (darkBlade)
            {
                return new Color(36 + blade / 3, 68 + n / 3, 31 + blade / 4);
            }

            int lineBoost = windLine == 0 ? 12 : 0;
            int shadow = shadowClump ? 12 : 0;
            return clump
                ? new Color(65 + n / 3 + lineBoost / 3 - shadow / 3, 104 + n / 2 + blade / 5 + lineBoost - shadow, 46 + n / 5 - shadow / 4)
                : new Color(50 + n / 5 + lineBoost / 4 - shadow / 4, 82 + n / 2 + blade / 6 + lineBoost / 2 - shadow, 36 + n / 6 - shadow / 5);
        });
    }

    private static Texture2D CreateDistantEarth(GraphicsDevice graphicsDevice)
    {
        return CreateTexture(graphicsDevice, 96, 20, (x, y) =>
        {
            float t = y / 19f;
            int n = Hash(x / 2, y) % 18;
            bool field = ((x / 9) + (y / 4)) % 2 == 0;
            bool treeRow = y > 10 && Hash(x / 3, y / 2) % 5 == 0;
            Vector3 top = field ? new Vector3(0.58f, 0.66f, 0.48f) : new Vector3(0.46f, 0.56f, 0.46f);
            Vector3 bottom = treeRow ? new Vector3(0.18f, 0.29f, 0.24f) : new Vector3(0.25f, 0.38f, 0.30f);
            Vector3 color = Vector3.Lerp(top, bottom, t);
            color += new Vector3(n, n + 3, n - 2) / 255f;
            return new Color(color);
        });
    }

    private static Texture2D CreateMountain(GraphicsDevice graphicsDevice)
    {
        return CreateTexture(graphicsDevice, 96, 36, (x, y) =>
        {
            float ridge = 16f +
                MathF.Sin(x * 0.11f) * 5f +
                MathF.Sin(x * 0.29f + 1.7f) * 4f +
                MathF.Sin(x * 0.53f + 0.6f) * 1.8f +
                (Hash(x / 3, 4) % 4);
            bool mountain = y >= ridge;
            if (!mountain)
            {
                return new Color(220, 228, 220, 0);
            }

            int n = Hash(x, y) % 18;
            float depth = MathHelper.Clamp((y - ridge) / MathF.Max(1f, 35f - ridge), 0f, 1f);
            float contour = MathF.Sin(x * 0.31f + y * 0.22f) * 0.5f + MathF.Sin(x * 0.09f - y * 0.34f) * 0.5f;
            bool darkFold = contour < -0.35f && y > ridge + 5f;
            bool sunPatch = contour > 0.38f && y > ridge + 9f;
            Vector3 far = new(0.42f, 0.54f, 0.58f);
            Vector3 lowerSlope = sunPatch
                ? new Vector3(0.43f, 0.56f, 0.26f)
                : new Vector3(0.28f, 0.43f, 0.29f);
            if (darkFold)
            {
                lowerSlope = new Vector3(0.18f, 0.30f, 0.30f);
            }

            Vector3 color = Vector3.Lerp(far, lowerSlope, depth);
            color += new Vector3(n, n + 1, n - 3) / 255f;
            return new Color(color);
        });
    }

    private static Texture2D CreateTreeClump(GraphicsDevice graphicsDevice, TreeShape shape)
    {
        return CreateTexture(graphicsDevice, 32, 32, (x, y) =>
        {
            float nx = (x + 0.5f) / 32f * 2f - 1f;
            float ny = (y + 0.5f) / 32f * 2f - 1f;
            bool gap = x is < 2 or > 29 || (Hash(x / 5 + (int)shape * 19, 11) % 9 == 0 && y < 25);
            float heightScale = shape switch
            {
                TreeShape.Tall => 0.70f,
                TreeShape.Round => 1.12f,
                _ => 1.0f
            };
            float widthScale = shape switch
            {
                TreeShape.Tall => 1.36f,
                TreeShape.Round => 0.86f,
                _ => 1.0f
            };
            float canopyA = (nx + 0.30f) * (nx + 0.30f) / (0.55f / widthScale) + (ny + 0.18f) * (ny + 0.18f) / (0.62f * heightScale);
            float canopyB = (nx - 0.20f) * (nx - 0.20f) / (0.50f / widthScale) + (ny + 0.04f) * (ny + 0.04f) / (0.54f * heightScale);
            float canopyC = nx * nx / (0.82f / widthScale) + (ny + 0.36f) * (ny + 0.36f) / (0.34f * heightScale);
            bool leaf = canopyA < 1f || canopyB < 1f || canopyC < 1f;
            bool trunk = MathF.Abs(nx) < 0.11f && y > 19;
            if (gap || (!leaf && !trunk))
            {
                return new Color(0, 0, 0, 0);
            }

            int n = Hash(x * 5, y * 7) % 24;
            if (trunk && !leaf)
            {
                return new Color(79 + n / 4, 62 + n / 5, 40 + n / 8, 255);
            }

            return new Color(37 + n / 3, 87 + n, 39 + n / 4, 255);
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

    private static Texture2D CreateChevronSign(GraphicsDevice graphicsDevice)
    {
        return CreateTexture(graphicsDevice, 64, 32, (x, y) =>
        {
            bool border = x < 2 || x > 61 || y < 2 || y > 29;
            if (border)
            {
                return new Color(28, 28, 26);
            }

            int stripe = Math.DivRem(x + y * 2, 18, out int rem);
            bool red = rem < 9;
            if (((stripe + y / 16) & 1) == 1)
            {
                red = !red;
            }

            return red
                ? new Color(205, 24, 25)
                : new Color(238, 235, 218);
        });
    }

    private static Texture2D CreateStartBoard(GraphicsDevice graphicsDevice)
    {
        return CreateTexture(graphicsDevice, 128, 32, (x, y) =>
        {
            bool border = x < 2 || x > 125 || y < 2 || y > 29;
            if (border)
            {
                return new Color(16, 17, 16);
            }

            if (y < 7)
            {
                return ((x / 8) & 1) == 0 ? new Color(205, 24, 25) : new Color(238, 235, 218);
            }

            if (y > 24)
            {
                return ((x / 6) & 1) == 0 ? new Color(238, 235, 218) : new Color(28, 29, 28);
            }

            bool centerStripe = y is >= 13 and <= 17;
            bool timingBlock = x is >= 7 and <= 27 || x is >= 100 and <= 120;
            if (timingBlock)
            {
                int local = x < 64 ? x - 7 : x - 100;
                return ((local / 5 + y / 5) & 1) == 0 ? new Color(238, 235, 218) : new Color(25, 26, 24);
            }

            if (centerStripe)
            {
                return new Color(236, 212, 70);
            }

            bool redPanel = x is >= 35 and <= 92 && y is >= 9 and <= 22;
            if (redPanel)
            {
                return new Color(177, 22, 24);
            }

            return new Color(31, 34, 32);
        });
    }

    private static Texture2D CreateBrakeMarkerSign(GraphicsDevice graphicsDevice)
    {
        return CreateTexture(graphicsDevice, 32, 48, (x, y) =>
        {
            bool border = x < 2 || x > 29 || y < 2 || y > 45;
            if (border)
            {
                return new Color(26, 26, 24);
            }

            bool redStripe = x is >= 5 and <= 10 || x is >= 14 and <= 19 || x is >= 23 and <= 26;
            bool lowerRed = y > 36 && x > 5 && x < 27;
            bool topRed = y < 9 && x > 5 && x < 27;
            if (redStripe || lowerRed || topRed)
            {
                return new Color(192, 22, 24);
            }

            return new Color(235, 232, 211);
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

    private static Texture2D CreateTaillightLens(GraphicsDevice graphicsDevice, bool clearLens)
    {
        return CreateTexture(graphicsDevice, 64, 64, (x, y) =>
        {
            float horizontalRib = (y % 8) switch
            {
                0 or 1 => 1.22f,
                4 => 0.78f,
                _ => 1.0f
            };
            float verticalPrism = x % 10 is 0 or 1 ? 1.12f : 1.0f;
            float dot = ((x / 4) + (y / 4)) % 2 == 0 ? 1.06f : 0.94f;
            float edgeDarkening = MathHelper.Lerp(0.78f, 1f, MathF.Min(1f, MathF.Min(x, 63 - x) / 8f));
            float value = MathHelper.Clamp(horizontalRib * verticalPrism * dot * edgeDarkening, 0.50f, 1.0f);

            if (clearLens)
            {
                int channel = (int)(210f * value);
                return new Color(channel, channel, Math.Min(255, channel + 8), 255);
            }

            int red = (int)(245f * value);
            int green = (int)(42f * value);
            int blue = (int)(34f * value);
            return new Color(red, green, blue, 255);
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

    private static Texture2D LoadTreeTextureOrCreate(
        GraphicsDevice graphicsDevice,
        string relativePath,
        Func<GraphicsDevice, Texture2D> createFallback,
        TreeTextureVariant variant = TreeTextureVariant.Base)
    {
        Texture2D source = LoadTextureOrCreate(graphicsDevice, relativePath, createFallback);
        Texture2D stylized = StylizeTreeTexture(graphicsDevice, source, variant);
        source.Dispose();
        return stylized;
    }

    private static Texture2D StylizeTreeTexture(GraphicsDevice graphicsDevice, Texture2D source, TreeTextureVariant variant)
    {
        Color[] sourceData = new Color[source.Width * source.Height];
        source.GetData(sourceData);

        int width = Math.Clamp(source.Width / 2, 16, 96);
        int height = Math.Clamp(source.Height / 2, 16, 96);
        Color[] outputData = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                float v = (y + 0.5f) / height;
                if (variant is TreeTextureVariant.MirroredCool or TreeTextureVariant.DarkLow)
                {
                    u = 1f - u;
                }

                if (variant == TreeTextureVariant.WarmDense)
                {
                    v = MathHelper.Clamp(v * 0.96f + 0.02f, 0f, 1f);
                }

                int sourceX = Math.Clamp((int)(u * source.Width), 0, source.Width - 1);
                int sourceY = Math.Clamp((int)(v * source.Height), 0, source.Height - 1);
                Color sampled = sourceData[sourceY * source.Width + sourceX];
                outputData[y * width + x] = IncreaseTreeContrast(sampled, variant);
            }
        }

        Texture2D texture = new(graphicsDevice, width, height, false, SurfaceFormat.Color);
        texture.SetData(outputData);
        return texture;
    }

    private static Color IncreaseTreeContrast(Color color, TreeTextureVariant variant)
    {
        if (color.A < 18)
        {
            return new Color(0, 0, 0, 0);
        }

        Vector3 rgb = color.ToVector3();
        float luminance = Vector3.Dot(rgb, new Vector3(0.299f, 0.587f, 0.114f));
        Vector3 contrasted = new(luminance);
        float contrast = variant switch
        {
            TreeTextureVariant.MirroredCool => 1.62f,
            TreeTextureVariant.WarmDense => 1.54f,
            TreeTextureVariant.DarkLow => 1.72f,
            _ => 1.5f
        };
        contrasted += (rgb - contrasted) * contrast;
        Vector3 tint = variant switch
        {
            TreeTextureVariant.MirroredCool => new Vector3(0.88f, 0.99f, 1.05f),
            TreeTextureVariant.WarmDense => new Vector3(1.08f, 1.03f, 0.86f),
            TreeTextureVariant.DarkLow => new Vector3(0.82f, 0.92f, 0.78f),
            _ => Vector3.One
        };
        contrasted = Vector3.Clamp(contrasted * tint * 0.96f, Vector3.Zero, Vector3.One);
        byte alpha = color.A < 120 ? (byte)Math.Max(0, color.A - (variant == TreeTextureVariant.WarmDense ? 12 : 22)) : color.A;
        return new Color(contrasted.X, contrasted.Y, contrasted.Z, alpha / 255f);
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

    private enum TreeShape
    {
        Low,
        Round,
        Tall
    }

    private enum TreeTextureVariant
    {
        Base,
        MirroredCool,
        WarmDense,
        DarkLow
    }
}
