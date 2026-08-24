using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RType.Rendering;

public sealed class VehicleRenderEffect : IDisposable
{
    private const string DefaultOpaqueEffectPath = "Assets/Shaders/VehicleOpaque.mgfxo";
    private const string DefaultTransparentEffectPath = "Assets/Shaders/VehicleTransparent.mgfxo";

    private readonly GraphicsDevice _graphicsDevice;
    private readonly EffectState? _opaque;
    private readonly EffectState? _transparent;

    private Matrix _viewProjection;
    private Vector3 _cameraPosition;

    private VehicleRenderEffect(GraphicsDevice graphicsDevice, EffectState? opaque, EffectState? transparent)
    {
        _graphicsDevice = graphicsDevice;
        _opaque = opaque;
        _transparent = transparent;
    }

    public bool CanDrawOpaque => _opaque is not null;

    public bool CanDrawTransparent => _transparent is not null;

    public static VehicleRenderEffect? TryCreate(GraphicsDevice graphicsDevice)
    {
        EffectState? opaque = TryLoadEffect(graphicsDevice, DefaultOpaqueEffectPath);
        EffectState? transparent = TryLoadEffect(graphicsDevice, DefaultTransparentEffectPath);
        return opaque is null && transparent is null
            ? null
            : new VehicleRenderEffect(graphicsDevice, opaque, transparent);
    }

    public void ConfigureFrame(Matrix view, Matrix projection, Vector3 cameraPosition)
    {
        _viewProjection = view * projection;
        _cameraPosition = cameraPosition;
        ConfigureFrame(_opaque);
        ConfigureFrame(_transparent);
    }

    public void DrawOpaqueMesh(StaticMesh mesh, Matrix world)
    {
        if (_opaque is null)
        {
            return;
        }

        VehicleMaterial material = ResolveMaterial(mesh);
        ApplyCommonMeshState(_opaque, mesh, world, material);
        _opaque.Metallic?.SetValue(material.Metallic);
        mesh.Draw(_graphicsDevice, _opaque.Effect);
    }

    public void DrawTransparentMesh(StaticMesh mesh, Matrix world)
    {
        if (_transparent is null)
        {
            return;
        }

        VehicleMaterial material = ResolveMaterial(mesh);
        ApplyCommonMeshState(_transparent, mesh, world, material);
        _transparent.Opacity?.SetValue(material.Opacity);
        _transparent.LensDetailStrength?.SetValue(CalculateLensDetailStrength(material.Category));
        mesh.Draw(_graphicsDevice, _transparent.Effect);
    }

    public void Dispose()
    {
        _opaque?.Effect.Dispose();
        _transparent?.Effect.Dispose();
    }

    private void ConfigureFrame(EffectState? state)
    {
        if (state is null)
        {
            return;
        }

        state.CameraPosition.SetValue(_cameraPosition);
        state.AmbientLightColor?.SetValue(new Vector3(0.25f, 0.26f, 0.27f));
        state.LightDirection0?.SetValue(Vector3.Normalize(new Vector3(-0.52f, -1.0f, -0.30f)));
        state.LightColor0?.SetValue(new Vector3(0.86f, 0.88f, 0.90f));
        state.LightSpecularColor0?.SetValue(new Vector3(0.88f, 0.92f, 0.96f));
        state.LightDirection1?.SetValue(Vector3.Normalize(new Vector3(0.22f, -0.38f, 0.92f)));
        state.LightColor1?.SetValue(new Vector3(0.18f, 0.20f, 0.23f));
        state.LightSpecularColor1?.SetValue(new Vector3(0.54f, 0.60f, 0.68f));
        state.FogColor?.SetValue(SceneRenderer.FogColor.ToVector3());
        state.FogStart?.SetValue(78f);
        state.FogEnd?.SetValue(280f);
    }

    private void ApplyCommonMeshState(EffectState state, StaticMesh mesh, Matrix world, VehicleMaterial material)
    {
        state.World.SetValue(world);
        state.WorldViewProjection.SetValue(world * _viewProjection);
        state.Texture.SetValue(mesh.Texture);
        state.BaseColor.SetValue(material.BaseColor);
        state.Roughness.SetValue(material.Roughness);
        state.SpecularStrength.SetValue(material.SpecularStrength);
        state.ReflectionStrength.SetValue(material.ReflectionStrength);
        state.FresnelStrength.SetValue(material.FresnelStrength);
        state.EmissiveColor.SetValue(material.EmissiveColor);
        state.EmissiveStrength.SetValue(material.EmissiveStrength);
    }

    private static VehicleMaterial ResolveMaterial(StaticMesh mesh)
    {
        return mesh.VehicleMaterial ?? VehicleMaterial.FromBasicEffect(
            mesh.DiffuseColor,
            mesh.Alpha,
            mesh.SpecularColor,
            mesh.SpecularPower,
            mesh.EmissiveColor);
    }

    private static float CalculateLensDetailStrength(VehicleMaterialCategory category)
    {
        return category switch
        {
            VehicleMaterialCategory.TaillightLens => 0.88f,
            VehicleMaterialCategory.ClearTailLens => 0.72f,
            VehicleMaterialCategory.HeadlightLens => 0.16f,
            _ => 0f
        };
    }

    private static EffectState? TryLoadEffect(GraphicsDevice graphicsDevice, string relativePath)
    {
        string path = ResolveExistingEffectPath(relativePath);
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Vehicle shader effect '{path}' was not found. Falling back for that pass.");
            return null;
        }

        try
        {
            return new EffectState(new Effect(graphicsDevice, File.ReadAllBytes(path)));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            Console.Error.WriteLine($"Could not load vehicle shader effect '{path}': {exception.Message}");
            return null;
        }
    }

    private static string ResolveExistingEffectPath(string relativePath)
    {
        foreach (string path in GetCandidateEffectPaths(relativePath))
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return Path.Combine(AppContext.BaseDirectory, relativePath);
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

    private sealed class EffectState
    {
        public EffectState(Effect effect)
        {
            Effect = effect;
            World = RequiredParameter("World");
            WorldViewProjection = RequiredParameter("WorldViewProjection");
            CameraPosition = RequiredParameter("CameraPosition");
            BaseColor = RequiredParameter("BaseColor");
            Roughness = RequiredParameter("Roughness");
            SpecularStrength = RequiredParameter("SpecularStrength");
            ReflectionStrength = RequiredParameter("ReflectionStrength");
            FresnelStrength = RequiredParameter("FresnelStrength");
            EmissiveColor = RequiredParameter("EmissiveColor");
            EmissiveStrength = RequiredParameter("EmissiveStrength");
            Texture = RequiredParameter("Texture0");
            Metallic = Effect.Parameters["Metallic"];
            Opacity = Effect.Parameters["Opacity"];
            LensDetailStrength = Effect.Parameters["LensDetailStrength"];
            AmbientLightColor = Effect.Parameters["AmbientLightColor"];
            LightDirection0 = Effect.Parameters["LightDirection0"];
            LightColor0 = Effect.Parameters["LightColor0"];
            LightSpecularColor0 = Effect.Parameters["LightSpecularColor0"];
            LightDirection1 = Effect.Parameters["LightDirection1"];
            LightColor1 = Effect.Parameters["LightColor1"];
            LightSpecularColor1 = Effect.Parameters["LightSpecularColor1"];
            FogColor = Effect.Parameters["FogColor"];
            FogStart = Effect.Parameters["FogStart"];
            FogEnd = Effect.Parameters["FogEnd"];
        }

        public Effect Effect { get; }

        public EffectParameter World { get; }

        public EffectParameter WorldViewProjection { get; }

        public EffectParameter CameraPosition { get; }

        public EffectParameter BaseColor { get; }

        public EffectParameter Roughness { get; }

        public EffectParameter SpecularStrength { get; }

        public EffectParameter ReflectionStrength { get; }

        public EffectParameter FresnelStrength { get; }

        public EffectParameter EmissiveColor { get; }

        public EffectParameter EmissiveStrength { get; }

        public EffectParameter Texture { get; }

        public EffectParameter? Metallic { get; }

        public EffectParameter? Opacity { get; }

        public EffectParameter? LensDetailStrength { get; }

        public EffectParameter? AmbientLightColor { get; }

        public EffectParameter? LightDirection0 { get; }

        public EffectParameter? LightColor0 { get; }

        public EffectParameter? LightSpecularColor0 { get; }

        public EffectParameter? LightDirection1 { get; }

        public EffectParameter? LightColor1 { get; }

        public EffectParameter? LightSpecularColor1 { get; }

        public EffectParameter? FogColor { get; }

        public EffectParameter? FogStart { get; }

        public EffectParameter? FogEnd { get; }

        private EffectParameter RequiredParameter(string name)
        {
            return Effect.Parameters[name] ?? throw new InvalidOperationException($"Vehicle shader missing '{name}' parameter.");
        }
    }
}
