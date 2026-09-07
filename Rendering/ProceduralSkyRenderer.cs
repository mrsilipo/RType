using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using RType.Camera;

namespace RType.Rendering;

public sealed class ProceduralSkyRenderer : IDisposable
{
    private const int Width = 320;
    private const int Height = 180;
    private const string DefaultEffectPath = "Assets/Shaders/ProceduralAtmosphere.mgfxo";

    private readonly GraphicsDevice _graphicsDevice;
    private readonly BasicEffect _effect;
    private readonly Effect? _atmosphereEffect;
    private readonly Texture2D _texture;
    private readonly VertexPositionTexture[] _vertices;
    private readonly short[] _indices;
    private readonly Color[] _pixels;

    public ProceduralSkyRenderer(GraphicsDevice graphicsDevice, ContentManager content)
    {
        _graphicsDevice = graphicsDevice;
        _effect = new BasicEffect(graphicsDevice)
        {
            TextureEnabled = true,
            LightingEnabled = false,
            FogEnabled = false,
            VertexColorEnabled = false,
            World = Matrix.Identity,
            View = Matrix.Identity,
            Projection = Matrix.Identity
        };
        _atmosphereEffect = TryLoadEffect(graphicsDevice, content, DefaultEffectPath);
        _texture = new Texture2D(graphicsDevice, Width, Height, false, SurfaceFormat.Color);
        _pixels = new Color[Width * Height];
        _vertices =
        [
            new(new Vector3(-1f, 1f, 0f), new Vector2(0f, 0f)),
            new(new Vector3(1f, 1f, 0f), new Vector2(1f, 0f)),
            new(new Vector3(1f, -1f, 0f), new Vector2(1f, 1f)),
            new(new Vector3(-1f, -1f, 0f), new Vector2(0f, 1f))
        ];
        _indices = [0, 1, 2, 0, 2, 3];
        UpdateSkyTexture(SceneRenderer.AfternoonSunDirection, Matrix.Identity, Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(56f), 16f / 9f, 0.1f, 1000f));
    }

    public void Draw(SceneCamera camera, Vector3 sunDirection)
    {
        _graphicsDevice.BlendState = BlendState.Opaque;
        _graphicsDevice.DepthStencilState = DepthStencilState.None;
        _graphicsDevice.RasterizerState = RasterizerState.CullNone;
        _graphicsDevice.SamplerStates[0] = SamplerState.PointClamp;

        if (_atmosphereEffect is not null)
        {
            DrawShaderSky(camera, sunDirection);
            return;
        }

        UpdateSkyTexture(sunDirection, camera.View, camera.Projection);
        _effect.Texture = _texture;
        foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _graphicsDevice.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList,
                _vertices,
                0,
                _vertices.Length,
                _indices,
                0,
                2);
        }
    }

    public void Dispose()
    {
        _atmosphereEffect?.Dispose();
        _texture.Dispose();
        _effect.Dispose();
    }

    private void DrawShaderSky(SceneCamera camera, Vector3 sunDirection)
    {
        Matrix viewProjection = camera.View * camera.Projection;
        Matrix.Invert(ref viewProjection, out Matrix inverseViewProjection);
        _atmosphereEffect!.Parameters["InverseViewProjection"]?.SetValue(inverseViewProjection);
        _atmosphereEffect.Parameters["SunDirection"]?.SetValue(sunDirection);
        _atmosphereEffect.Parameters["Exposure"]?.SetValue(0.78f);
        _atmosphereEffect.Parameters["MieG"]?.SetValue(0.76f);

        foreach (EffectPass pass in _atmosphereEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _graphicsDevice.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList,
                _vertices,
                0,
                _vertices.Length,
                _indices,
                0,
                2);
        }
    }

    private void UpdateSkyTexture(Vector3 sunDirection, Matrix view, Matrix projection)
    {
        Matrix viewProjection = view * projection;
        Matrix.Invert(ref viewProjection, out Matrix inverseViewProjection);
        Vector3 sunToLight = Vector3.Normalize(-sunDirection);

        for (int y = 0; y < Height; y++)
        {
            float v = (y + 0.5f) / Height;
            for (int x = 0; x < Width; x++)
            {
                float u = (x + 0.5f) / Width;
                Vector3 ray = CalculateRayDirection(u, v, inverseViewProjection);
                Vector3 color = CalculateAtmosphere(ray, sunToLight, v);
                _pixels[y * Width + x] = ToColor(color);
            }
        }

        _texture.SetData(_pixels);
    }

    private static Vector3 CalculateRayDirection(float u, float v, Matrix inverseViewProjection)
    {
        Vector3 near = Vector3.Transform(new Vector3(u * 2f - 1f, 1f - v * 2f, 0f), inverseViewProjection);
        Vector3 far = Vector3.Transform(new Vector3(u * 2f - 1f, 1f - v * 2f, 1f), inverseViewProjection);
        return Vector3.Normalize(far - near);
    }

    private static Vector3 CalculateAtmosphere(Vector3 viewRay, Vector3 sunToLight, float screenY)
    {
        float horizon = SmoothStep(-0.20f, 0.28f, viewRay.Y);
        float zenith = MathHelper.Clamp(viewRay.Y * 0.5f + 0.5f, 0f, 1f);
        float mu = MathHelper.Clamp(Vector3.Dot(viewRay, sunToLight), -1f, 1f);

        float rayleighPhase = 0.75f * (1f + mu * mu);
        float miePhase = HenyeyGreenstein(mu, 0.76f);
        float airMass = 1f / MathF.Max(0.10f, viewRay.Y + 0.18f);
        float opticalDepth = MathHelper.Clamp(airMass, 0.15f, 9.5f);

        Vector3 betaRayleigh = new(0.22f, 0.54f, 1.42f);
        Vector3 betaMie = new(0.92f, 0.80f, 0.60f);
        Vector3 rayleigh = betaRayleigh * rayleighPhase * 1.02f * MathF.Exp(-opticalDepth * 0.070f);
        Vector3 mie = betaMie * miePhase * 0.032f * MathF.Exp(-opticalDepth * 0.032f);

        Vector3 highSky = new Vector3(0.020f, 0.145f, 0.66f) * (0.86f + zenith * 0.82f);
        Vector3 midSky = new Vector3(0.15f, 0.40f, 0.78f) * (0.72f + zenith * 0.18f);
        Vector3 horizonHaze = new Vector3(1.00f, 0.965f, 0.78f) * MathF.Pow(1f - horizon, 0.66f);
        Vector3 baseSky = Vector3.Lerp(horizonHaze, highSky, horizon);
        baseSky = Vector3.Lerp(baseSky, midSky, SmoothStep(0.16f, 0.70f, viewRay.Y) * 0.31f);

        float sunCore = MathF.Pow(MathHelper.Clamp(mu, 0f, 1f), 2400f) * 22f;
        float sunHalo = MathF.Pow(MathHelper.Clamp(mu, 0f, 1f), 58f) * 1.05f;
        float broadGlare = MathF.Pow(MathHelper.Clamp(mu, 0f, 1f), 9f) * 0.18f;
        float sunSideWarmth = MathF.Pow(MathHelper.Clamp(mu, 0f, 1f), 3.2f) * SmoothStep(-0.10f, 0.46f, viewRay.Y) * 0.18f;
        float shaftBand = MathF.Pow(MathHelper.Clamp(mu, 0f, 1f), 18f) *
            (0.65f + 0.35f * MathF.Sin((viewRay.X * 34f + viewRay.Y * 11f) * 1.7f));

        Vector3 sunColor = new(1.0f, 0.88f, 0.58f);
        float highNoise = WaveNoise(viewRay, 17.0f, 0.8f);
        float fineNoise = WaveNoise(viewRay, 39.0f, 2.4f);
        float cirrusMask = SmoothStep(0.32f, 0.74f, viewRay.Y) * (1f - SmoothStep(0.86f, 1f, viewRay.Y));
        float cirrusStreak = SmoothStep(0.38f, 0.76f, highNoise + fineNoise * 0.28f) * cirrusMask;
        cirrusStreak *= 0.055f;
        float lowAirBand = SmoothStep(-0.05f, 0.16f, viewRay.Y) * (1f - SmoothStep(0.18f, 0.44f, viewRay.Y));
        lowAirBand *= SmoothStep(0.18f, 0.74f, WaveNoise(viewRay, 9.0f, 4.1f) * 0.55f + 0.48f);
        float altitudeBand = 0.018f * MathF.Sin((viewRay.X * 2.2f + viewRay.Z * 1.4f) + viewRay.Y * 5.0f);
        float domeDepth = SmoothStep(0.30f, 0.92f, viewRay.Y);
        float horizonCoolBreakup = SmoothStep(-0.12f, 0.18f, viewRay.Y) * (1f - SmoothStep(0.14f, 0.46f, viewRay.Y));
        horizonCoolBreakup *= SmoothStep(0.18f, 0.80f, WaveNoise(viewRay, 5.2f, 7.1f) * 0.45f + 0.50f);
        Vector3 color = baseSky * (0.66f + altitudeBand) + rayleigh * 0.34f + mie + sunColor * (sunCore + sunHalo + broadGlare + shaftBand * 0.055f + sunSideWarmth);
        color += new Vector3(0.86f, 0.93f, 1.0f) * cirrusStreak;
        color = Vector3.Lerp(color, new Vector3(0.88f, 0.91f, 0.80f), lowAirBand * 0.12f);
        color = Vector3.Lerp(color, new Vector3(0.12f, 0.32f, 0.74f), domeDepth * 0.08f);
        color = Vector3.Lerp(color, new Vector3(0.70f, 0.78f, 0.78f), horizonCoolBreakup * 0.07f);

        float horizonWarmth = MathF.Pow(1f - horizon, 1.55f);
        color = Vector3.Lerp(color, new Vector3(1.00f, 0.955f, 0.78f), horizonWarmth * 0.36f);
        color += new Vector3(1.00f, 0.74f, 0.30f) * horizonWarmth * 0.10f;
        float screenHaze = SmoothStep(0.10f, 0.36f, screenY) * (1f - SmoothStep(0.45f, 0.82f, screenY));
        color = Vector3.Lerp(color, new Vector3(0.96f, 0.94f, 0.80f), screenHaze * 0.24f);
        color += new Vector3(0.98f, 0.75f, 0.32f) * screenHaze * 0.025f;

        float exposure = 0.78f;
        color = Vector3.One - new Vector3(
            MathF.Exp(-color.X * exposure),
            MathF.Exp(-color.Y * exposure),
            MathF.Exp(-color.Z * exposure));

        return Gamma(color);
    }

    private static float HenyeyGreenstein(float mu, float g)
    {
        float g2 = g * g;
        float denominator = MathF.Pow(MathF.Max(0.001f, 1f + g2 - 2f * g * mu), 1.5f);
        return (1f - g2) / denominator;
    }

    private static float WaveNoise(Vector3 ray, float scale, float phase)
    {
        float a = MathF.Sin(ray.X * scale + ray.Z * (scale * 0.57f) + ray.Y * (scale * 0.23f) + phase);
        float b = MathF.Sin(ray.X * (scale * -0.41f) + ray.Z * (scale * 0.83f) + ray.Y * (scale * 0.31f) + phase * 1.73f);
        float c = MathF.Sin(ray.X * (scale * 0.19f) + ray.Z * (scale * -1.11f) + ray.Y * (scale * 0.47f) + phase * 0.61f);
        return a * 0.52f + b * 0.33f + c * 0.15f;
    }

    private static Vector3 Gamma(Vector3 linear)
    {
        return new Vector3(
            MathF.Pow(MathHelper.Clamp(linear.X, 0f, 1f), 1f / 2.2f),
            MathF.Pow(MathHelper.Clamp(linear.Y, 0f, 1f), 1f / 2.2f),
            MathF.Pow(MathHelper.Clamp(linear.Z, 0f, 1f), 1f / 2.2f));
    }

    private static Color ToColor(Vector3 color)
    {
        return new Color(
            (byte)(MathHelper.Clamp(color.X, 0f, 1f) * 255f),
            (byte)(MathHelper.Clamp(color.Y, 0f, 1f) * 255f),
            (byte)(MathHelper.Clamp(color.Z, 0f, 1f) * 255f),
            byte.MaxValue);
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float t = MathHelper.Clamp((value - edge0) / MathF.Max(0.0001f, edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static Effect? TryLoadEffect(GraphicsDevice graphicsDevice, ContentManager content, string relativePath)
    {
        try
        {
            string oldRoot = content.RootDirectory;
            content.RootDirectory = "Assets/Shaders";
            try
            {
                Effect effect = content.Load<Effect>("ProceduralAtmosphere.mgfxo");
                Console.WriteLine("Using compiled procedural atmosphere shader.");
                return effect;
            }
            finally
            {
                content.RootDirectory = oldRoot;
            }
        }
        catch (Exception exception) when (exception is ContentLoadException or InvalidOperationException or IOException)
        {
            Console.Error.WriteLine($"Could not load compiled procedural atmosphere shader via ContentManager: {exception.Message}");
        }

        foreach (string path in GetCandidateEffectPaths(relativePath))
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                return new Effect(graphicsDevice, File.ReadAllBytes(path));
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
            {
                Console.Error.WriteLine($"Could not load procedural atmosphere effect '{path}': {exception.Message}");
            }
        }

        Console.Error.WriteLine("Procedural atmosphere shader was not found. Falling back to generated sky texture.");
        return null;
    }

    private static IEnumerable<string> GetCandidateEffectPaths(string relativePath)
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

}
