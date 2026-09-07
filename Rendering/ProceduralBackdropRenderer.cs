using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RType.Camera;

namespace RType.Rendering;

public sealed class ProceduralBackdropRenderer : IDisposable
{
    private readonly BasicEffect _effect;
    private readonly StaticMesh[] _meshes;

    public ProceduralBackdropRenderer(GraphicsDevice graphicsDevice, GeneratedTextures textures)
        : this(graphicsDevice, textures, BackdropSceneryProfile.HighSpeedCircuit)
    {
    }

    public ProceduralBackdropRenderer(GraphicsDevice graphicsDevice, GeneratedTextures textures, BackdropSceneryProfile profile)
    {
        _effect = new BasicEffect(graphicsDevice)
        {
            TextureEnabled = true,
            LightingEnabled = false,
            FogEnabled = false,
            VertexColorEnabled = false,
            World = Matrix.Identity
        };

        float downlandRadius = profile.DownlandRadius;
        float nearDownRadius = profile.NearDownRadius;
        float chalkFieldRadius = profile.ChalkFieldRadius;
        float hedgerowRadius = profile.HedgerowRadius;
        float nearTreeRadius = profile.NearTreeRadius;
        _meshes =
        [
            CreateTerrainBelt(graphicsDevice, textures.Mountain, "backdrop far Salisbury chalk downland", downlandRadius - 116f, downlandRadius, -8.5f, 4.5f, 14f, profile.FarDownlandTint, 0.2f, 6),
            CreateBrokenTerrainBelt(graphicsDevice, textures.Mountain, "backdrop broken blue-grey distant ridge caps", downlandRadius - 132f, downlandRadius - 26f, -7.7f, 5.2f, 16.5f, profile.RidgeCapTint, 0.9f, 3, 0.68f),
            CreateTerrainBelt(graphicsDevice, textures.DistantEarth, "backdrop soft Wiltshire ridge", downlandRadius - 146f, downlandRadius - 46f, -7.2f, 2.8f, 10f, profile.SoftRidgeTint, 1.6f, 5),
            CreateBrokenTerrainBelt(graphicsDevice, textures.DistantEarth, "backdrop distant valley and field seams", downlandRadius - 172f, downlandRadius - 92f, -6.0f, -1.4f, 3.8f, profile.ValleyTint, 2.2f, 2, 0.58f),
            CreateBrokenTerrainBelt(graphicsDevice, textures.White, "backdrop far chalk scarp highlights", downlandRadius - 164f, downlandRadius - 64f, -5.65f, -4.95f, -3.25f, profile.ChalkScarpTint, 3.9f, 1, 0.42f),
            CreateBrokenTerrainBelt(graphicsDevice, textures.DistantEarth, "backdrop blue green distant copse breaks", downlandRadius - 184f, downlandRadius - 104f, -4.95f, -2.80f, -1.15f, profile.DistantCopseTint, 4.7f, 2, 0.46f),
            CreateRing(graphicsDevice, textures.White, "backdrop warm horizon atmospheric veil", downlandRadius - 74f, -4.8f, 15.5f, profile.WarmVeilTint, 0.14f, 1f),
            CreateArcRing(graphicsDevice, textures.White, "backdrop sunlit warm horizon veil", downlandRadius - 82f, -4.35f, 14.2f, profile.SunHorizonAngleRadians, 2.15f, profile.SunVeilTint, 0.105f),
            CreateArcRing(graphicsDevice, textures.White, "backdrop cool opposite horizon veil", downlandRadius - 96f, -5.05f, 10.8f, profile.SunHorizonAngleRadians + MathF.PI, 2.35f, profile.CoolVeilTint, 0.060f),
            CreateRing(graphicsDevice, textures.White, "backdrop blue-grey valley distance veil", downlandRadius - 118f, -5.6f, 7.8f, profile.ValleyVeilTint, 0.10f, 1f),
            CreateTerrainBelt(graphicsDevice, textures.DistantEarth, "backdrop open chalk grass foothill", nearDownRadius - 104f, nearDownRadius, -5.5f, 0.8f, 6.6f, profile.FoothillTint, 2.8f, 5),
            CreateBrokenTerrainBelt(graphicsDevice, textures.DistantEarth, "backdrop broken pale chalk field terraces", chalkFieldRadius - 112f, chalkFieldRadius - 56f, -3.9f, -1.0f, 2.4f, profile.ChalkTerraceTint, 3.4f, 2, 0.54f),
            CreateBrokenTerrainBelt(graphicsDevice, textures.White, "backdrop thin chalk lane and dry field cuts", chalkFieldRadius - 118f, chalkFieldRadius - 36f, -2.88f, -2.65f, -1.95f, profile.ChalkCutTint, 8.4f, 1, 0.36f),
            CreateBrokenTerrainBelt(graphicsDevice, textures.DistantEarth, "backdrop muted pasture patchwork", chalkFieldRadius - 98f, chalkFieldRadius - 20f, -2.72f, -1.85f, -0.62f, profile.PastureTint, 9.8f, 2, 0.48f),
            CreateTerrainBelt(graphicsDevice, textures.DistantEarth, "backdrop pale chalk field band", chalkFieldRadius - 84f, chalkFieldRadius, -3.2f, -0.3f, 2.2f, profile.ChalkFieldTint, 4.1f, 4),
            CreateBrokenTerrainBelt(graphicsDevice, textures.DistantEarth, "backdrop broken distant hedgerow islands", hedgerowRadius - 54f, hedgerowRadius - 18f, -2.05f, -0.55f, 1.20f, profile.HedgerowIslandTint, 5.0f, 2, 0.50f),
            CreateBrokenTerrainBelt(graphicsDevice, textures.White, "backdrop pale river glints", hedgerowRadius - 62f, hedgerowRadius - 32f, -1.84f, -1.70f, -1.45f, profile.RiverGlintTint, 10.9f, 1, 0.30f),
            CreateTerrainBelt(graphicsDevice, textures.White, "backdrop dark hedgerow scrub mass", hedgerowRadius - 27f, hedgerowRadius, -1.7f, 0.0f, 1.7f, profile.ScrubMassTint, 5.2f, 2),
            CreateTerrainBelt(graphicsDevice, textures.White, "backdrop pale chalk track scar", hedgerowRadius - 36f, hedgerowRadius - 23f, -1.52f, -1.40f, -1.12f, profile.TrackScarTint, 7.3f, 1),
            CreateBrokenTerrainBelt(graphicsDevice, textures.White, "backdrop near uneven scrub bank fragments", nearTreeRadius - 38f, nearTreeRadius - 16f, -1.48f, 0.20f, 2.7f, profile.NearScrubTint, 6.6f, 2, 0.45f),
            CreateRing(graphicsDevice, textures.ReferenceTreeShrub, "backdrop low juniper scrub line", nearTreeRadius, -1.0f, 4.4f, profile.JuniperTint, 0.40f, 18f)
        ];
    }

    public IReadOnlyList<StaticMesh> Meshes => _meshes;

    public void Draw(GraphicsDevice graphicsDevice, SceneCamera camera)
    {
        _effect.View = camera.View;
        _effect.Projection = camera.Projection;

        graphicsDevice.BlendState = BlendState.NonPremultiplied;
        graphicsDevice.DepthStencilState = DepthStencilState.None;
        graphicsDevice.RasterizerState = RasterizerState.CullNone;
        graphicsDevice.SamplerStates[0] = SamplerState.PointWrap;

        Matrix world = Matrix.CreateTranslation(camera.Position.X, 0f, camera.Position.Z);
        foreach (StaticMesh mesh in _meshes)
        {
            mesh.Draw(graphicsDevice, _effect, world);
        }
    }

    public void Dispose()
    {
        foreach (StaticMesh mesh in _meshes)
        {
            mesh.Dispose();
        }

        _effect.Dispose();
    }

    private static StaticMesh CreateRing(
        GraphicsDevice graphicsDevice,
        Texture2D texture,
        string name,
        float radius,
        float baseY,
        float height,
        Vector3 tint,
        float alpha,
        float repeatX)
    {
        return MeshFactory.CreateVerticalRing(
            graphicsDevice,
            radius,
            baseY,
            height,
            96,
            texture,
            tint,
            name,
            alpha,
            repeatX);
    }

    private static StaticMesh CreateArcRing(
        GraphicsDevice graphicsDevice,
        Texture2D texture,
        string name,
        float radius,
        float baseY,
        float height,
        float centerAngle,
        float angularWidth,
        Vector3 tint,
        float alpha)
    {
        MeshBuilder builder = new();
        const int segments = 48;
        float start = centerAngle - angularWidth * 0.5f;

        for (int i = 0; i < segments; i++)
        {
            float t0 = i / (float)segments;
            float t1 = (i + 1) / (float)segments;
            float a0 = start + angularWidth * t0;
            float a1 = start + angularWidth * t1;
            Vector3 p0 = new(MathF.Sin(a0) * radius, baseY, MathF.Cos(a0) * radius);
            Vector3 p1 = new(MathF.Sin(a1) * radius, baseY, MathF.Cos(a1) * radius);
            Vector3 p2 = p1 + Vector3.Up * height;
            Vector3 p3 = p0 + Vector3.Up * height;
            Vector3 normal = Vector3.Normalize(new Vector3(
                MathF.Sin((a0 + a1) * 0.5f),
                0f,
                MathF.Cos((a0 + a1) * 0.5f)));

            builder.AddQuad(
                p1,
                p0,
                p3,
                p2,
                new Vector2(t1, 1f),
                new Vector2(t0, 1f),
                new Vector2(t0, 0f),
                new Vector2(t1, 0f),
                normal);
        }

        return builder.Build(graphicsDevice, name, texture, tint, alpha: alpha);
    }

    private static StaticMesh CreateTerrainRing(
        GraphicsDevice graphicsDevice,
        Texture2D texture,
        string name,
        float radius,
        float baseY,
        float minimumTopY,
        float maximumTopY,
        Vector3 tint,
        float phase)
    {
        return MeshFactory.CreateHorizonTerrainRing(
            graphicsDevice,
            radius,
            baseY,
            minimumTopY,
            maximumTopY,
            144,
            texture,
            tint,
            name,
            phase);
    }

    private static StaticMesh CreateTerrainBelt(
        GraphicsDevice graphicsDevice,
        Texture2D texture,
        string name,
        float innerRadius,
        float outerRadius,
        float innerY,
        float outerMinimumY,
        float outerMaximumY,
        Vector3 tint,
        float phase,
        int radialSegments)
    {
        return MeshFactory.CreateHorizonTerrainBelt(
            graphicsDevice,
            innerRadius,
            outerRadius,
            innerY,
            outerMinimumY,
            outerMaximumY,
            144,
            radialSegments,
            texture,
            tint,
            name,
            phase);
    }

    private static StaticMesh CreateBrokenTerrainBelt(
        GraphicsDevice graphicsDevice,
        Texture2D texture,
        string name,
        float innerRadius,
        float outerRadius,
        float innerY,
        float outerMinimumY,
        float outerMaximumY,
        Vector3 tint,
        float phase,
        int radialSegments,
        float density)
    {
        return MeshFactory.CreateBrokenHorizonTerrainBelt(
            graphicsDevice,
            innerRadius,
            outerRadius,
            innerY,
            outerMinimumY,
            outerMaximumY,
            144,
            radialSegments,
            texture,
            tint,
            name,
            phase,
            density);
    }

    public readonly record struct BackdropSceneryProfile(
        float DownlandRadius,
        float NearDownRadius,
        float ChalkFieldRadius,
        float HedgerowRadius,
        float NearTreeRadius,
        float SunHorizonAngleRadians,
        Vector3 FarDownlandTint,
        Vector3 RidgeCapTint,
        Vector3 SoftRidgeTint,
        Vector3 ValleyTint,
        Vector3 ChalkScarpTint,
        Vector3 DistantCopseTint,
        Vector3 WarmVeilTint,
        Vector3 SunVeilTint,
        Vector3 CoolVeilTint,
        Vector3 ValleyVeilTint,
        Vector3 FoothillTint,
        Vector3 ChalkTerraceTint,
        Vector3 ChalkCutTint,
        Vector3 PastureTint,
        Vector3 ChalkFieldTint,
        Vector3 HedgerowIslandTint,
        Vector3 RiverGlintTint,
        Vector3 ScrubMassTint,
        Vector3 TrackScarTint,
        Vector3 NearScrubTint,
        Vector3 JuniperTint)
    {
        public static BackdropSceneryProfile HighSpeedCircuit { get; } = new(
            390f,
            318f,
            286f,
            244f,
            220f,
            -1.88f,
            new Vector3(0.62f, 0.73f, 0.78f),
            new Vector3(0.52f, 0.64f, 0.72f),
            new Vector3(0.70f, 0.78f, 0.66f),
            new Vector3(0.52f, 0.64f, 0.48f),
            new Vector3(0.82f, 0.80f, 0.62f),
            new Vector3(0.34f, 0.48f, 0.43f),
            new Vector3(0.92f, 0.88f, 0.70f),
            new Vector3(0.96f, 0.88f, 0.62f),
            new Vector3(0.38f, 0.48f, 0.58f),
            new Vector3(0.47f, 0.58f, 0.62f),
            new Vector3(0.56f, 0.68f, 0.42f),
            new Vector3(0.93f, 0.88f, 0.62f),
            new Vector3(0.76f, 0.75f, 0.56f),
            new Vector3(0.44f, 0.58f, 0.36f),
            new Vector3(0.84f, 0.83f, 0.58f),
            new Vector3(0.25f, 0.38f, 0.24f),
            new Vector3(0.58f, 0.70f, 0.68f),
            new Vector3(0.18f, 0.27f, 0.18f),
            new Vector3(0.74f, 0.74f, 0.58f),
            new Vector3(0.26f, 0.40f, 0.22f),
            new Vector3(0.27f, 0.36f, 0.22f));
    }
}
