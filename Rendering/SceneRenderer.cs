using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RType.Camera;
using RType.Vehicle;
using RType.World;

namespace RType.Rendering;

public sealed class SceneRenderer : IDisposable
{
    public static readonly Color FogColor = new(126, 184, 230);

    private static readonly Vector3[] TyreContactShadowOffsets =
    [
        new(-0.82f, 0f, 1.34f),
        new(0.82f, 0f, 1.34f),
        new(-0.82f, 0f, -1.40f),
        new(0.82f, 0f, -1.40f)
    ];

    private readonly GraphicsDevice _graphicsDevice;
    private readonly BasicEffect _effect;
    private readonly VehicleRenderEffect? _vehicleEffect;
    private readonly CarModel _carModel;
    private readonly StaticMesh _shadowQuad;

    public SceneRenderer(GraphicsDevice graphicsDevice, GeneratedTextures textures)
    {
        _graphicsDevice = graphicsDevice;
        _effect = new BasicEffect(graphicsDevice)
        {
            TextureEnabled = true,
            LightingEnabled = true,
            PreferPerPixelLighting = false,
            FogEnabled = true,
            FogColor = FogColor.ToVector3(),
            FogStart = 78f,
            FogEnd = 280f,
            SpecularColor = Vector3.Zero,
            AmbientLightColor = new Vector3(0.36f, 0.38f, 0.40f)
        };

        ConfigureTrackLighting();

        _vehicleEffect = VehicleRenderEffect.TryCreate(graphicsDevice);
        _carModel = CarModel.Create(graphicsDevice, textures);
        _shadowQuad = MeshFactory.CreateUnitGroundQuad(graphicsDevice, textures.Shadow, "blob shadow");
    }

    public void Draw(TrackScene track, VehicleState vehicle, ChaseCamera camera)
    {
        _effect.View = camera.View;
        _effect.Projection = camera.Projection;

        _graphicsDevice.RasterizerState = RasterizerState.CullNone;
        _graphicsDevice.BlendState = BlendState.Opaque;
        _graphicsDevice.DepthStencilState = DepthStencilState.Default;
        _graphicsDevice.SamplerStates[0] = SamplerState.PointWrap;

        ConfigureTrackLighting();
        ConfigureLitEffect();
        foreach (StaticMesh mesh in track.Meshes)
        {
            mesh.Draw(_graphicsDevice, _effect, Matrix.Identity);
        }

        Matrix bodyWorld = CreateBodyWorld(vehicle);
        Matrix wheelWorld = CreateWheelWorld(vehicle);

        if (camera.Mode != CameraMode.InCar)
        {
            DrawShadow(vehicle);

            _graphicsDevice.BlendState = BlendState.Opaque;
            _graphicsDevice.DepthStencilState = DepthStencilState.Default;
            _graphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;
            if (_vehicleEffect?.CanDrawOpaque == true)
            {
                _vehicleEffect.ConfigureFrame(camera.View, camera.Projection, camera.Position);
                DrawCarMeshes(_carModel.BodyMeshes, bodyWorld, drawTransparent: false, _vehicleEffect, drawTransparentWithShader: false);
                DrawCarMeshes(_carModel.WheelMeshes, wheelWorld, drawTransparent: false, _vehicleEffect, drawTransparentWithShader: false);
            }
            else
            {
                ConfigureCarLighting();
                ConfigureLitEffect();
                DrawCarMeshes(_carModel.BodyMeshes, bodyWorld, drawTransparent: false);
                DrawCarMeshes(_carModel.WheelMeshes, wheelWorld, drawTransparent: false);
            }

            _graphicsDevice.BlendState = BlendState.NonPremultiplied;
            _graphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
            _graphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;
            if (_vehicleEffect?.CanDrawTransparent == true)
            {
                _vehicleEffect.ConfigureFrame(camera.View, camera.Projection, camera.Position);
                DrawCarMeshes(_carModel.BodyMeshes, bodyWorld, drawTransparent: true, _vehicleEffect, drawTransparentWithShader: true);
                DrawCarMeshes(_carModel.WheelMeshes, wheelWorld, drawTransparent: true, _vehicleEffect, drawTransparentWithShader: true);
            }
            else
            {
                ConfigureCarLighting();
                ConfigureLitEffect();
                DrawCarMeshes(_carModel.BodyMeshes, bodyWorld, drawTransparent: true);
                DrawCarMeshes(_carModel.WheelMeshes, wheelWorld, drawTransparent: true);
            }
        }
    }

    public void Dispose()
    {
        _shadowQuad.Dispose();
        _carModel.Dispose();
        _vehicleEffect?.Dispose();
        _effect.Dispose();
    }

    private void ConfigureLitEffect()
    {
        _effect.TextureEnabled = true;
        _effect.LightingEnabled = true;
        _effect.FogEnabled = true;
        _effect.Alpha = 1f;
        _effect.DiffuseColor = Vector3.One;
        _effect.SpecularColor = Vector3.Zero;
        _effect.SpecularPower = 16f;
        _effect.EmissiveColor = Vector3.Zero;
    }

    private void ConfigureTrackLighting()
    {
        _effect.PreferPerPixelLighting = false;
        _effect.AmbientLightColor = new Vector3(0.36f, 0.38f, 0.40f);

        _effect.DirectionalLight0.Enabled = true;
        _effect.DirectionalLight0.Direction = Vector3.Normalize(new Vector3(-0.45f, -1.0f, -0.25f));
        _effect.DirectionalLight0.DiffuseColor = new Vector3(0.76f, 0.74f, 0.70f);
        _effect.DirectionalLight0.SpecularColor = new Vector3(0.46f, 0.48f, 0.50f);

        _effect.DirectionalLight1.Enabled = false;
        _effect.DirectionalLight2.Enabled = false;
    }

    private void ConfigureCarLighting()
    {
        _effect.PreferPerPixelLighting = false;
        _effect.AmbientLightColor = new Vector3(0.26f, 0.27f, 0.28f);

        _effect.DirectionalLight0.Enabled = true;
        _effect.DirectionalLight0.Direction = Vector3.Normalize(new Vector3(-0.52f, -1.0f, -0.30f));
        _effect.DirectionalLight0.DiffuseColor = new Vector3(0.90f, 0.92f, 0.94f);
        _effect.DirectionalLight0.SpecularColor = new Vector3(0.82f, 0.86f, 0.90f);

        _effect.DirectionalLight1.Enabled = true;
        _effect.DirectionalLight1.Direction = Vector3.Normalize(new Vector3(0.22f, -0.38f, 0.92f));
        _effect.DirectionalLight1.DiffuseColor = new Vector3(0.20f, 0.22f, 0.25f);
        _effect.DirectionalLight1.SpecularColor = new Vector3(0.52f, 0.58f, 0.64f);

        _effect.DirectionalLight2.Enabled = false;
    }

    private void DrawCarMeshes(IEnumerable<StaticMesh> meshes, Matrix world, bool drawTransparent)
    {
        foreach (StaticMesh mesh in meshes)
        {
            if (mesh.IsTransparent != drawTransparent)
            {
                continue;
            }

            mesh.Draw(_graphicsDevice, _effect, world);
        }
    }

    private static void DrawCarMeshes(
        IEnumerable<StaticMesh> meshes,
        Matrix world,
        bool drawTransparent,
        VehicleRenderEffect effect,
        bool drawTransparentWithShader)
    {
        foreach (StaticMesh mesh in meshes)
        {
            if (mesh.IsTransparent != drawTransparent)
            {
                continue;
            }

            if (drawTransparentWithShader)
            {
                effect.DrawTransparentMesh(mesh, world);
            }
            else
            {
                effect.DrawOpaqueMesh(mesh, world);
            }
        }
    }

    private static Matrix CreateBodyWorld(VehicleState vehicle)
    {
        float pivotHeight = MathHelper.Clamp(vehicle.BodyPivotHeightMeters, 0.25f, 1.10f);
        return Matrix.CreateTranslation(0f, -pivotHeight, 0f) *
               Matrix.CreateRotationX(vehicle.BodyPitchRadians) *
               Matrix.CreateRotationZ(vehicle.BodyRollRadians) *
               Matrix.CreateTranslation(0f, pivotHeight, 0f) *
               Matrix.CreateRotationY(vehicle.HeadingRadians) *
               Matrix.CreateTranslation(vehicle.Position);
    }

    private static Matrix CreateWheelWorld(VehicleState vehicle)
    {
        Vector3 wheelContactPosition = new(
            vehicle.Position.X,
            vehicle.WheelContactCenterHeightMeters,
            vehicle.Position.Z);
        return Matrix.CreateRotationX(vehicle.GroundPitchRadians) *
               Matrix.CreateRotationZ(vehicle.GroundRollRadians) *
               Matrix.CreateRotationY(vehicle.HeadingRadians) *
               Matrix.CreateTranslation(wheelContactPosition);
    }

    private void DrawShadow(VehicleState vehicle)
    {
        _graphicsDevice.BlendState = BlendState.AlphaBlend;
        _graphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
        _graphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;

        _effect.LightingEnabled = false;
        _effect.TextureEnabled = true;
        _effect.FogEnabled = true;
        _effect.DiffuseColor = Vector3.One;
        _effect.SpecularColor = Vector3.Zero;
        _effect.EmissiveColor = Vector3.Zero;

        DrawShadowQuad(vehicle, Vector3.Zero, 2.35f, 5.10f, 0.52f);
        foreach (Vector3 offset in TyreContactShadowOffsets)
        {
            DrawShadowQuad(vehicle, offset, 0.58f, 0.92f, 0.36f);
        }

        _effect.Alpha = 1f;
    }

    private void DrawShadowQuad(VehicleState vehicle, Vector3 localOffset, float width, float depth, float alpha)
    {
        Matrix rotation = Matrix.CreateRotationY(vehicle.HeadingRadians);
        Vector3 worldOffset = Vector3.Transform(localOffset, rotation);
        Matrix shadowWorld =
            Matrix.CreateScale(width, 1f, depth) *
            rotation *
            Matrix.CreateTranslation(
                vehicle.Position.X + worldOffset.X,
                vehicle.WheelContactCenterHeightMeters - 0.036f,
                vehicle.Position.Z + worldOffset.Z);

        _effect.Alpha = alpha;
        _shadowQuad.Draw(_graphicsDevice, _effect, shadowWorld);
    }

}
