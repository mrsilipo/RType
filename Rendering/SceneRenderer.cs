using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using RType.Camera;
using RType.Vehicle;
using RType.World;

namespace RType.Rendering;

public sealed class SceneRenderer : IDisposable
{
    public static readonly Color FogColor = new(126, 184, 230);
    public static readonly Vector3 AfternoonSunDirection = Vector3.Normalize(new Vector3(-0.56f, -1.18f, -0.18f));
    public static readonly Vector3 AfternoonSunDiffuseColor = new(1.12f, 1.04f, 0.90f);
    public static readonly Vector3 AfternoonSunSpecularColor = new(0.90f, 0.82f, 0.66f);
    public static readonly Vector3 AfternoonAmbientColor = new(0.42f, 0.44f, 0.46f);
    private static readonly VehicleMaterial MeshShadowMaterial = new(
        VehicleMaterialCategory.BlackPlastic,
        Vector3.Zero,
        0f,
        1f,
        0f,
        0f,
        0f,
        1f,
        Vector3.Zero,
        0f);
    private static readonly VehicleMaterial HardFootprintShadowMaterial = new(
        VehicleMaterialCategory.BlackPlastic,
        Vector3.Zero,
        0f,
        1f,
        0f,
        0f,
        0f,
        0.30f,
        Vector3.Zero,
        0f);
    private const int FoliageReferenceAlpha = 32;

    private readonly GraphicsDevice _graphicsDevice;
    private readonly BasicEffect _effect;
    private readonly AlphaTestEffect _foliageEffect;
    private readonly VehicleRenderEffect? _vehicleEffect;
    private readonly CarModel _carModel;
    private readonly StaticMesh _hardShadowQuad;
    private readonly ProceduralSkyRenderer _skyRenderer;
    private readonly ProceduralBackdropRenderer _backdropRenderer;

    public SceneRenderer(GraphicsDevice graphicsDevice, ContentManager content, GeneratedTextures textures)
    {
        _graphicsDevice = graphicsDevice;
        _effect = new BasicEffect(graphicsDevice)
        {
            TextureEnabled = true,
            LightingEnabled = true,
            PreferPerPixelLighting = true,
            FogEnabled = false,
            FogColor = FogColor.ToVector3(),
            FogStart = 78f,
            FogEnd = 280f,
            SpecularColor = Vector3.Zero,
            AmbientLightColor = AfternoonAmbientColor
        };

        ConfigureTrackLighting();
        _foliageEffect = new AlphaTestEffect(graphicsDevice)
        {
            VertexColorEnabled = false,
            AlphaFunction = CompareFunction.Greater,
            ReferenceAlpha = FoliageReferenceAlpha
        };

        _skyRenderer = new ProceduralSkyRenderer(graphicsDevice, content);
        _backdropRenderer = new ProceduralBackdropRenderer(graphicsDevice, textures);
        _vehicleEffect = null;
        _carModel = CarModel.Create(graphicsDevice, textures);
        _hardShadowQuad = MeshFactory.CreatePlane(
            graphicsDevice,
            Vector3.Zero,
            1f,
            1f,
            textures.White,
            1f,
            Vector3.One,
            "hard car footprint shadow");
    }

    public IReadOnlyList<StaticMesh> BackdropMeshes => _backdropRenderer.Meshes;

    public void Draw(TrackScene track, VehicleState vehicle, ChaseCamera camera)
    {
        Draw(track, vehicle, new SceneCamera(camera.View, camera.Projection, camera.Position), camera.Mode != CameraMode.InCar);
    }

    public void Draw(TrackScene track, VehicleState vehicle, ChaseCamera camera, bool drawContactDebug)
    {
        Draw(track, vehicle, new SceneCamera(camera.View, camera.Projection, camera.Position), camera.Mode != CameraMode.InCar, drawContactDebug);
    }

    public void Draw(TrackScene track, VehicleState vehicle, SceneCamera camera, bool drawVehicle)
    {
        Draw(track, vehicle, camera, drawVehicle, drawContactDebug: false);
    }

    public void Draw(TrackScene track, VehicleState vehicle, SceneCamera camera, bool drawVehicle, bool drawContactDebug)
    {
        _effect.View = camera.View;
        _effect.Projection = camera.Projection;

        _skyRenderer.Draw(camera, AfternoonSunDirection);
        if (track.DrawProceduralBackdrop)
        {
            _backdropRenderer.Draw(_graphicsDevice, camera);
        }

        _graphicsDevice.RasterizerState = RasterizerState.CullNone;
        _graphicsDevice.BlendState = BlendState.Opaque;
        _graphicsDevice.DepthStencilState = DepthStencilState.Default;
        _graphicsDevice.SamplerStates[0] = SamplerState.PointWrap;

        ConfigureTrackLighting();
        ConfigureLitEffect();
        foreach (StaticMesh mesh in track.Meshes)
        {
            if (mesh.IsTransparent)
            {
                continue;
            }

            mesh.Draw(_graphicsDevice, _effect, Matrix.Identity);
        }

        _graphicsDevice.BlendState = BlendState.Opaque;
        _graphicsDevice.DepthStencilState = DepthStencilState.Default;
        _graphicsDevice.SamplerStates[0] = SamplerState.PointWrap;
        ConfigureFoliageEffect(camera);
        foreach (StaticMesh mesh in track.Meshes)
        {
            if (!mesh.IsTransparent)
            {
                continue;
            }

            _foliageEffect.World = Matrix.Identity;
            _foliageEffect.Texture = mesh.Texture;
            _foliageEffect.DiffuseColor = mesh.DiffuseColor;
            mesh.Draw(_graphicsDevice, _foliageEffect);
        }

        Matrix bodyWorld = CreateBodyWorld(vehicle);
        Matrix wheelWorld = CreateWheelWorld(vehicle);

        if (drawVehicle)
        {
            DrawShadow(vehicle, bodyWorld, wheelWorld);

            _graphicsDevice.BlendState = BlendState.Opaque;
            _graphicsDevice.DepthStencilState = DepthStencilState.Default;
            _graphicsDevice.SamplerStates[0] = SamplerState.PointWrap;
            if (_vehicleEffect?.CanDrawOpaque == true)
            {
                _vehicleEffect.ConfigureFrame(camera.View, camera.Projection, camera.Position);
                DrawCarMeshes(_carModel.BodyMeshes, bodyWorld, vehicle, drawTransparent: false, _vehicleEffect, drawTransparentWithShader: false);
                DrawWheelMeshes(_carModel.WheelMeshes, wheelWorld, vehicle, drawTransparent: false, _vehicleEffect, drawTransparentWithShader: false);
            }
            else
            {
                ConfigureCarLighting();
                ConfigureLitEffect();
                DrawCarMeshes(_carModel.BodyMeshes, bodyWorld, vehicle, drawTransparent: false);
                DrawWheelMeshes(_carModel.WheelMeshes, wheelWorld, vehicle, drawTransparent: false);
            }

            _graphicsDevice.BlendState = BlendState.NonPremultiplied;
            _graphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
            _graphicsDevice.SamplerStates[0] = SamplerState.PointWrap;
            if (_vehicleEffect?.CanDrawTransparent == true)
            {
                _vehicleEffect.ConfigureFrame(camera.View, camera.Projection, camera.Position);
                DrawCarMeshes(_carModel.BodyMeshes, bodyWorld, vehicle, drawTransparent: true, _vehicleEffect, drawTransparentWithShader: true);
                DrawWheelMeshes(_carModel.WheelMeshes, wheelWorld, vehicle, drawTransparent: true, _vehicleEffect, drawTransparentWithShader: true);
            }
            else
            {
                ConfigureCarLighting();
                ConfigureLitEffect();
                DrawCarMeshes(_carModel.BodyMeshes, bodyWorld, vehicle, drawTransparent: true);
                DrawWheelMeshes(_carModel.WheelMeshes, wheelWorld, vehicle, drawTransparent: true);
            }
        }

        if (drawContactDebug)
        {
            DrawSurfaceContactDebug(vehicle);
        }
    }

    public void Dispose()
    {
        _backdropRenderer.Dispose();
        _skyRenderer.Dispose();
        _hardShadowQuad.Dispose();
        _carModel.Dispose();
        _vehicleEffect?.Dispose();
        _foliageEffect.Dispose();
        _effect.Dispose();
    }

    private void ConfigureLitEffect()
    {
        _effect.TextureEnabled = true;
        _effect.LightingEnabled = true;
        _effect.FogEnabled = false;
        _effect.Alpha = 1f;
        _effect.DiffuseColor = Vector3.One;
        _effect.SpecularColor = Vector3.Zero;
        _effect.SpecularPower = 16f;
        _effect.EmissiveColor = Vector3.Zero;
    }

    private void DrawSurfaceContactDebug(VehicleState vehicle)
    {
        Span<VertexPositionColor> lines = stackalloc VertexPositionColor[16];
        int count = 0;
        AddContactDebugLine(lines, ref count, vehicle.FrontLeftContactPoint, vehicle.FrontLeftContactNormal, vehicle.FrontLeftContactMissed, Color.Lime);
        AddContactDebugLine(lines, ref count, vehicle.FrontRightContactPoint, vehicle.FrontRightContactNormal, vehicle.FrontRightContactMissed, Color.Cyan);
        AddContactDebugLine(lines, ref count, vehicle.RearLeftContactPoint, vehicle.RearLeftContactNormal, vehicle.RearLeftContactMissed, Color.Yellow);
        AddContactDebugLine(lines, ref count, vehicle.RearRightContactPoint, vehicle.RearRightContactNormal, vehicle.RearRightContactMissed, Color.Magenta);
        if (count == 0)
        {
            return;
        }

        _graphicsDevice.BlendState = BlendState.Opaque;
        _graphicsDevice.DepthStencilState = DepthStencilState.None;
        _graphicsDevice.RasterizerState = RasterizerState.CullNone;
        _effect.TextureEnabled = false;
        _effect.LightingEnabled = false;
        _effect.VertexColorEnabled = true;
        foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _graphicsDevice.DrawUserPrimitives(
                PrimitiveType.LineList,
                lines[..count].ToArray(),
                0,
                count / 2);
        }

        _effect.VertexColorEnabled = false;
        ConfigureLitEffect();
    }

    private static void AddContactDebugLine(
        Span<VertexPositionColor> lines,
        ref int count,
        Vector3 contactPoint,
        Vector3 contactNormal,
        bool missed,
        Color hitColor)
    {
        if (!float.IsFinite(contactPoint.X) || !float.IsFinite(contactPoint.Y) || !float.IsFinite(contactPoint.Z))
        {
            return;
        }

        Color color = missed ? Color.Red : hitColor;
        Vector3 normal = contactNormal.LengthSquared() > 0.0001f ? Vector3.Normalize(contactNormal) : Vector3.Up;
        lines[count++] = new VertexPositionColor(contactPoint + Vector3.Up * 2.5f, color);
        lines[count++] = new VertexPositionColor(contactPoint, color);
        lines[count++] = new VertexPositionColor(contactPoint, Color.White);
        lines[count++] = new VertexPositionColor(contactPoint + normal * 1.4f, Color.White);
    }

    private void ConfigureTrackLighting()
    {
        _effect.PreferPerPixelLighting = true;
        _effect.AmbientLightColor = AfternoonAmbientColor;

        _effect.DirectionalLight0.Enabled = true;
        _effect.DirectionalLight0.Direction = AfternoonSunDirection;
        _effect.DirectionalLight0.DiffuseColor = AfternoonSunDiffuseColor;
        _effect.DirectionalLight0.SpecularColor = Vector3.Zero;

        _effect.DirectionalLight1.Enabled = false;
        _effect.DirectionalLight2.Enabled = false;
    }

    private void ConfigureFoliageEffect(SceneCamera camera)
    {
        _foliageEffect.View = camera.View;
        _foliageEffect.Projection = camera.Projection;
        _foliageEffect.AlphaFunction = CompareFunction.Greater;
        _foliageEffect.ReferenceAlpha = FoliageReferenceAlpha;
        _foliageEffect.DiffuseColor = Vector3.One;
    }

    private void ConfigureCarLighting()
    {
        _effect.PreferPerPixelLighting = true;
        _effect.AmbientLightColor = new Vector3(0.54f, 0.57f, 0.62f);

        _effect.DirectionalLight0.Enabled = true;
        _effect.DirectionalLight0.Direction = AfternoonSunDirection;
        _effect.DirectionalLight0.DiffuseColor = new Vector3(1.04f, 0.99f, 0.88f);
        _effect.DirectionalLight0.SpecularColor = new Vector3(0.24f, 0.24f, 0.22f);

        _effect.DirectionalLight1.Enabled = true;
        _effect.DirectionalLight1.Direction = Vector3.Normalize(new Vector3(0.18f, -0.42f, 0.34f));
        _effect.DirectionalLight1.DiffuseColor = new Vector3(0.30f, 0.38f, 0.52f);
        _effect.DirectionalLight1.SpecularColor = new Vector3(0.34f, 0.44f, 0.60f);

        _effect.DirectionalLight2.Enabled = true;
        _effect.DirectionalLight2.Direction = Vector3.Normalize(new Vector3(-0.34f, -0.22f, -0.56f));
        _effect.DirectionalLight2.DiffuseColor = new Vector3(0.12f, 0.14f, 0.17f);
        _effect.DirectionalLight2.SpecularColor = new Vector3(0.14f, 0.17f, 0.21f);
    }

    private void DrawCarMeshes(IEnumerable<StaticMesh> meshes, Matrix world, VehicleState vehicle, bool drawTransparent)
    {
        foreach (StaticMesh mesh in meshes)
        {
            if (mesh.IsTransparent != drawTransparent)
            {
                continue;
            }

            mesh.Draw(_graphicsDevice, _effect, world, ResolveRuntimeMaterial(mesh, vehicle));
        }
    }

    private void DrawWheelMeshes(IEnumerable<StaticMesh> meshes, Matrix world, VehicleState vehicle, bool drawTransparent)
    {
        foreach (StaticMesh mesh in meshes)
        {
            if (mesh.IsTransparent != drawTransparent)
            {
                continue;
            }

            mesh.Draw(_graphicsDevice, _effect, CreateWheelMeshWorld(mesh, world, vehicle), ResolveRuntimeMaterial(mesh, vehicle));
        }
    }

    private static void DrawCarMeshes(
        IEnumerable<StaticMesh> meshes,
        Matrix world,
        VehicleState vehicle,
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
                effect.DrawTransparentMesh(mesh, world, ResolveRuntimeMaterial(mesh, vehicle));
            }
            else
            {
                effect.DrawOpaqueMesh(mesh, world, ResolveRuntimeMaterial(mesh, vehicle));
            }
        }
    }

    private static void DrawWheelMeshes(
        IEnumerable<StaticMesh> meshes,
        Matrix world,
        VehicleState vehicle,
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

            Matrix wheelMeshWorld = CreateWheelMeshWorld(mesh, world, vehicle);
            VehicleMaterial? materialOverride = ResolveRuntimeMaterial(mesh, vehicle);
            if (drawTransparentWithShader)
            {
                effect.DrawTransparentMesh(mesh, wheelMeshWorld, materialOverride);
            }
            else
            {
                effect.DrawOpaqueMesh(mesh, wheelMeshWorld, materialOverride);
            }
        }
    }

    private static Matrix CreateWheelMeshWorld(StaticMesh mesh, Matrix wheelWorld, VehicleState vehicle)
    {
        if (mesh.WheelCorner == WheelCorner.None)
        {
            return wheelWorld;
        }

        float steerDegrees = mesh.WheelCorner switch
        {
            WheelCorner.FrontLeft => vehicle.FrontLeftSteerAngleDegrees,
            WheelCorner.FrontRight => vehicle.FrontRightSteerAngleDegrees,
            _ => 0f
        };

        float spinRadians = mesh.WheelCorner switch
        {
            WheelCorner.FrontLeft => vehicle.FrontLeftWheelVisualRotationRadians,
            WheelCorner.FrontRight => vehicle.FrontRightWheelVisualRotationRadians,
            WheelCorner.RearLeft => vehicle.RearLeftWheelVisualRotationRadians,
            WheelCorner.RearRight => vehicle.RearRightWheelVisualRotationRadians,
            _ => 0f
        };

        return Matrix.CreateRotationX(spinRadians) *
               Matrix.CreateRotationY(MathHelper.ToRadians(steerDegrees)) *
               Matrix.CreateTranslation(mesh.LocalPivot) *
               wheelWorld;
    }

    private static VehicleMaterial? ResolveRuntimeMaterial(StaticMesh mesh, VehicleState vehicle)
    {
        if (mesh.VehicleMaterial is not VehicleMaterial material)
        {
            return null;
        }

        string normalizedName = mesh.Name.Replace('_', ' ').Replace('-', ' ').ToLowerInvariant();
        if (material.Category == VehicleMaterialCategory.Paint)
        {
            return material with
            {
                BaseColor = new Vector3(0.94f, 0.91f, 0.78f),
                Roughness = 0.30f,
                SpecularStrength = MathF.Max(material.SpecularStrength, 0.48f),
                ReflectionStrength = MathF.Max(material.ReflectionStrength, 0.26f),
                FresnelStrength = MathF.Max(material.FresnelStrength, 0.24f),
                EmissiveColor = new Vector3(0.62f, 0.74f, 0.92f),
                EmissiveStrength = MathF.Max(material.EmissiveStrength, 0.045f)
            };
        }

        if (material.Category == VehicleMaterialCategory.Glass)
        {
            return material with
            {
                BaseColor = new Vector3(0.084f, 0.133f, 0.154f),
                Roughness = 0.16f,
                SpecularStrength = MathF.Max(material.SpecularStrength, 0.68f),
                ReflectionStrength = MathF.Max(material.ReflectionStrength, 0.30f),
                FresnelStrength = MathF.Max(material.FresnelStrength, 0.34f),
                EmissiveColor = new Vector3(0.13f, 0.25f, 0.38f),
                EmissiveStrength = MathF.Max(material.EmissiveStrength, 0.065f)
            };
        }

        if (material.Category == VehicleMaterialCategory.BlackPlastic)
        {
            bool isBaseChassis = normalizedName.Contains("basechassis", StringComparison.OrdinalIgnoreCase) ||
                normalizedName.Contains("base chassis", StringComparison.OrdinalIgnoreCase) ||
                normalizedName.Contains("darkgrey", StringComparison.OrdinalIgnoreCase) ||
                normalizedName.Contains("dark grey", StringComparison.OrdinalIgnoreCase);

            return isBaseChassis
                ? material with
                {
                    BaseColor = new Vector3(0.34f, 0.35f, 0.33f),
                    Roughness = 0.36f,
                    SpecularStrength = MathF.Max(material.SpecularStrength, 0.50f),
                    ReflectionStrength = MathF.Max(material.ReflectionStrength, 0.30f),
                    FresnelStrength = MathF.Max(material.FresnelStrength, 0.28f),
                    EmissiveColor = new Vector3(0.26f, 0.32f, 0.38f),
                    EmissiveStrength = MathF.Max(material.EmissiveStrength, 0.06f)
                }
                : material with
                {
                    BaseColor = new Vector3(0.055f, 0.065f, 0.078f),
                    Roughness = 0.12f,
                    SpecularStrength = MathF.Max(material.SpecularStrength, 0.96f),
                    ReflectionStrength = MathF.Max(material.ReflectionStrength, 0.66f),
                    FresnelStrength = MathF.Max(material.FresnelStrength, 0.60f),
                    EmissiveColor = new Vector3(0.28f, 0.42f, 0.58f),
                    EmissiveStrength = MathF.Max(material.EmissiveStrength, 0.11f)
                };
        }

        if (material.Category == VehicleMaterialCategory.TyreRubber)
        {
            return material with
            {
                BaseColor = new Vector3(0.022f, 0.023f, 0.024f),
                Roughness = 0.28f,
                SpecularStrength = 0.52f,
                ReflectionStrength = 0.30f,
                FresnelStrength = 0.42f,
                EmissiveColor = new Vector3(0.040f, 0.046f, 0.052f),
                EmissiveStrength = MathF.Max(material.EmissiveStrength, 0.032f)
            };
        }

        if (material.Category == VehicleMaterialCategory.WheelPaintOrMetal)
        {
            return material with
            {
                BaseColor = new Vector3(0.50f, 0.50f, 0.47f),
                Metallic = 0.05f,
                Roughness = 0.82f,
                SpecularStrength = 0.035f,
                ReflectionStrength = 0.020f,
                FresnelStrength = 0.030f,
                EmissiveColor = new Vector3(0.45f, 0.47f, 0.50f),
                EmissiveStrength = MathF.Max(material.EmissiveStrength, 0.42f)
            };
        }

        if (material.Category == VehicleMaterialCategory.TaillightLens ||
            normalizedName.Contains("tail", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Contains("brake", StringComparison.OrdinalIgnoreCase))
        {
            float brake = MathHelper.Clamp(vehicle.Brake, 0f, 1f);
            if (brake <= 0.02f)
            {
                return material;
            }

            return material with
            {
                BaseColor = Vector3.Lerp(material.BaseColor, new Vector3(1f, 0.02f, 0.01f), brake),
                EmissiveColor = new Vector3(1f, 0.02f, 0.01f),
                EmissiveStrength = MathHelper.Lerp(MathF.Max(material.EmissiveStrength, 0.10f), 1.0f, brake)
            };
        }

        if (material.Category == VehicleMaterialCategory.HeadlightLens)
        {
            return material with
            {
                EmissiveColor = new Vector3(1.0f, 0.90f, 0.62f),
                EmissiveStrength = MathF.Max(material.EmissiveStrength, 0.16f)
            };
        }

        return material;
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

    private void DrawShadow(VehicleState vehicle, Matrix bodyWorld, Matrix wheelWorld)
    {
        _graphicsDevice.BlendState = BlendState.AlphaBlend;
        _graphicsDevice.DepthStencilState = DepthStencilState.None;
        _graphicsDevice.SamplerStates[0] = SamplerState.PointClamp;

        _effect.LightingEnabled = false;
        _effect.TextureEnabled = false;
        _effect.FogEnabled = false;
        _effect.DiffuseColor = Vector3.One;
        _effect.SpecularColor = Vector3.Zero;
        _effect.EmissiveColor = Vector3.Zero;

        DrawHardFootprintShadow(vehicle);
        DrawProjectedMeshShadows(vehicle, bodyWorld, wheelWorld);

        _effect.Alpha = 1f;
        _effect.TextureEnabled = true;
        _effect.FogEnabled = false;
    }

    private void DrawProjectedMeshShadows(VehicleState vehicle, Matrix bodyWorld, Matrix wheelWorld)
    {
        Vector3 compactSunDirection = Vector3.Normalize(new Vector3(
            AfternoonSunDirection.X * 0.42f,
            -1f,
            AfternoonSunDirection.Z * 0.42f));
        Plane groundPlane = new(Vector3.Up, -vehicle.WheelContactCenterHeightMeters);
        Matrix projectAlongSun = Matrix.CreateShadow(compactSunDirection, groundPlane);
        Matrix liftAboveRoad = Matrix.CreateTranslation(0f, 0.020f, 0f);
        Matrix meshShadowProjection = projectAlongSun * liftAboveRoad;

        foreach (StaticMesh mesh in _carModel.BodyMeshes)
        {
            mesh.DrawUntextured(_graphicsDevice, _effect, bodyWorld * meshShadowProjection, MeshShadowMaterial);
        }

        foreach (StaticMesh mesh in _carModel.WheelMeshes)
        {
            Matrix wheelMeshWorld = CreateWheelMeshWorld(mesh, wheelWorld, vehicle);
            mesh.DrawUntextured(_graphicsDevice, _effect, wheelMeshWorld * meshShadowProjection, MeshShadowMaterial);
        }
    }

    private void DrawHardFootprintShadow(VehicleState vehicle)
    {
        Matrix rotation = Matrix.CreateRotationY(vehicle.HeadingRadians);
        Vector3 sunOffset = Vector3.Normalize(new Vector3(-AfternoonSunDirection.X, 0f, -AfternoonSunDirection.Z)) * 0.30f;
        Vector3 origin = new(
            vehicle.Position.X + sunOffset.X,
            vehicle.WheelContactCenterHeightMeters + 0.016f,
            vehicle.Position.Z + sunOffset.Z);

        DrawFootprintShadowPart(rotation, origin, 0f, -0.16f, 1.56f, 3.85f);
        DrawFootprintShadowPart(rotation, origin, 0f, 0.34f, 1.22f, 1.76f);
        DrawFootprintShadowPart(rotation, origin, 0f, -2.02f, 1.52f, 0.36f);
        DrawFootprintShadowPart(rotation, origin, -0.86f, 1.06f, 0.48f, 0.82f);
        DrawFootprintShadowPart(rotation, origin, 0.86f, 1.06f, 0.48f, 0.82f);
        DrawFootprintShadowPart(rotation, origin, -0.86f, -1.70f, 0.48f, 0.82f);
        DrawFootprintShadowPart(rotation, origin, 0.86f, -1.70f, 0.48f, 0.82f);
    }

    private void DrawFootprintShadowPart(Matrix rotation, Vector3 origin, float localRight, float localForward, float width, float depth)
    {
        Matrix shadowWorld =
            Matrix.CreateScale(width, 1f, depth) *
            Matrix.CreateTranslation(localRight, 0f, localForward) *
            rotation *
            Matrix.CreateTranslation(origin);

        _hardShadowQuad.DrawUntextured(_graphicsDevice, _effect, shadowWorld, HardFootprintShadowMaterial);
    }

}
