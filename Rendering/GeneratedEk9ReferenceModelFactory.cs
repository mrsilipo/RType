using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RType.Rendering;

internal static class GeneratedEk9ReferenceModelFactory
{
    private const float Length = 4.185f;
    private const float Width = 1.695f;
    private const float Height = 1.360f;
    private const float Wheelbase = 2.620f;
    private const float Track = 1.480f;
    private const float FrontWeightDistribution = 0.620f;
    private const float TyreRadius = 0.298f;
    private const float TyreWidth = 0.195f;
    private const float GroundClearance = 0.135f;

    private static readonly float FrontAxleZ = Wheelbase * (1f - FrontWeightDistribution);
    private static readonly float RearAxleZ = -Wheelbase * FrontWeightDistribution;
    private static readonly float HalfTrack = Track * 0.5f;

    public static List<StaticMesh> Create(GraphicsDevice graphicsDevice, GeneratedTextures textures)
    {
        List<StaticMesh> meshes = [];
        AddBody(meshes, graphicsDevice, textures);
        AddDebugAxes(meshes, graphicsDevice, textures);
        AddWheel(meshes, graphicsDevice, textures, "WHEEL_FL", WheelCorner.FrontLeft, new Vector3(-HalfTrack, TyreRadius, FrontAxleZ));
        AddWheel(meshes, graphicsDevice, textures, "WHEEL_FR", WheelCorner.FrontRight, new Vector3(HalfTrack, TyreRadius, FrontAxleZ));
        AddWheel(meshes, graphicsDevice, textures, "WHEEL_RL", WheelCorner.RearLeft, new Vector3(-HalfTrack, TyreRadius, RearAxleZ));
        AddWheel(meshes, graphicsDevice, textures, "WHEEL_RR", WheelCorner.RearRight, new Vector3(HalfTrack, TyreRadius, RearAxleZ));
        return meshes;
    }

    private static void AddBody(List<StaticMesh> meshes, GraphicsDevice graphicsDevice, GeneratedTextures textures)
    {
        meshes.Add(Box(
            graphicsDevice,
            textures.White,
            "GeneratedEK9 BODY main shell",
            new Vector3(0f, GroundClearance + 0.30f, -0.12f),
            new Vector3(Width, 0.50f, Length * 0.86f),
            VehicleMaterial.CreateDefault(VehicleMaterialCategory.Paint, new Vector3(0.92f, 0.90f, 0.82f))));
        meshes.Add(Box(
            graphicsDevice,
            textures.White,
            "GeneratedEK9 HOOD",
            new Vector3(0f, GroundClearance + 0.55f, 1.05f),
            new Vector3(Width * 0.86f, 0.12f, 1.10f),
            VehicleMaterial.CreateDefault(VehicleMaterialCategory.Paint, new Vector3(0.92f, 0.90f, 0.82f))));
        meshes.Add(Box(
            graphicsDevice,
            textures.White,
            "GeneratedEK9 FRONT_BUMPER",
            new Vector3(0f, GroundClearance + 0.25f, 1.78f),
            new Vector3(Width * 0.94f, 0.34f, 0.34f),
            VehicleMaterial.CreateDefault(VehicleMaterialCategory.Paint, new Vector3(0.92f, 0.90f, 0.82f))));
        meshes.Add(Box(
            graphicsDevice,
            textures.White,
            "GeneratedEK9 REAR_BUMPER",
            new Vector3(0f, GroundClearance + 0.27f, -1.95f),
            new Vector3(Width * 0.94f, 0.36f, 0.28f),
            VehicleMaterial.CreateDefault(VehicleMaterialCategory.Paint, new Vector3(0.92f, 0.90f, 0.82f))));
        meshes.Add(Box(
            graphicsDevice,
            textures.White,
            "GeneratedEK9 ROOF",
            new Vector3(0f, GroundClearance + 1.03f, -0.40f),
            new Vector3(Width * 0.72f, 0.18f, 1.54f),
            VehicleMaterial.CreateDefault(VehicleMaterialCategory.Paint, new Vector3(0.92f, 0.90f, 0.82f))));
        meshes.Add(Box(
            graphicsDevice,
            textures.CarGlass,
            "GeneratedEK9 GLASS_FRONT",
            new Vector3(0f, GroundClearance + 0.83f, 0.40f),
            new Vector3(Width * 0.70f, 0.10f, 0.62f),
            VehicleMaterial.CreateDefault(VehicleMaterialCategory.Glass)));
        meshes.Add(Box(
            graphicsDevice,
            textures.CarGlass,
            "GeneratedEK9 GLASS_REAR",
            new Vector3(0f, GroundClearance + 0.78f, -1.26f),
            new Vector3(Width * 0.68f, 0.10f, 0.46f),
            VehicleMaterial.CreateDefault(VehicleMaterialCategory.Glass)));
        meshes.Add(Box(
            graphicsDevice,
            textures.CarGlass,
            "GeneratedEK9 GLASS_LEFT",
            new Vector3(-HalfTrack, GroundClearance + 0.78f, -0.42f),
            new Vector3(0.08f, 0.36f, 1.34f),
            VehicleMaterial.CreateDefault(VehicleMaterialCategory.Glass)));
        meshes.Add(Box(
            graphicsDevice,
            textures.CarGlass,
            "GeneratedEK9 GLASS_RIGHT",
            new Vector3(HalfTrack, GroundClearance + 0.78f, -0.42f),
            new Vector3(0.08f, 0.36f, 1.34f),
            VehicleMaterial.CreateDefault(VehicleMaterialCategory.Glass)));
        meshes.Add(Box(
            graphicsDevice,
            textures.White,
            "GeneratedEK9 LIGHT_FRONT_L",
            new Vector3(-0.42f, GroundClearance + 0.43f, 1.98f),
            new Vector3(0.44f, 0.14f, 0.08f),
            VehicleMaterial.CreateDefault(VehicleMaterialCategory.HeadlightLens)));
        meshes.Add(Box(
            graphicsDevice,
            textures.White,
            "GeneratedEK9 LIGHT_FRONT_R",
            new Vector3(0.42f, GroundClearance + 0.43f, 1.98f),
            new Vector3(0.44f, 0.14f, 0.08f),
            VehicleMaterial.CreateDefault(VehicleMaterialCategory.HeadlightLens)));
        meshes.Add(Box(
            graphicsDevice,
            textures.TaillightRedLens,
            "GeneratedEK9 LIGHT_REAR_L",
            new Vector3(-0.50f, GroundClearance + 0.48f, -2.07f),
            new Vector3(0.36f, 0.16f, 0.07f),
            VehicleMaterial.CreateDefault(VehicleMaterialCategory.TaillightLens)));
        meshes.Add(Box(
            graphicsDevice,
            textures.TaillightRedLens,
            "GeneratedEK9 LIGHT_REAR_R",
            new Vector3(0.50f, GroundClearance + 0.48f, -2.07f),
            new Vector3(0.36f, 0.16f, 0.07f),
            VehicleMaterial.CreateDefault(VehicleMaterialCategory.TaillightLens)));
        meshes.Add(Box(
            graphicsDevice,
            textures.White,
            "GeneratedEK9 REAR_WING",
            new Vector3(0f, GroundClearance + 1.10f, -1.83f),
            new Vector3(Width * 0.76f, 0.08f, 0.24f),
            VehicleMaterial.CreateDefault(VehicleMaterialCategory.BlackPlastic)));
        meshes.Add(Box(
            graphicsDevice,
            textures.White,
            "GeneratedEK9 INTERIOR_DARK",
            new Vector3(0f, GroundClearance + 0.62f, -0.40f),
            new Vector3(Width * 0.55f, 0.28f, 1.20f),
            VehicleMaterial.CreateDefault(VehicleMaterialCategory.Interior)));
    }

    private static void AddDebugAxes(List<StaticMesh> meshes, GraphicsDevice graphicsDevice, GeneratedTextures textures)
    {
        float y = GroundClearance + Height + 0.16f;
        meshes.Add(Box(
            graphicsDevice,
            textures.White,
            "GeneratedEK9 GAME_AXIS_RIGHT_PLUS_X",
            new Vector3(0.48f, y, 0f),
            new Vector3(0.96f, 0.035f, 0.035f),
            VehicleMaterial.CreateDefault(VehicleMaterialCategory.Paint, new Vector3(1f, 0.08f, 0.04f))));
        meshes.Add(Box(
            graphicsDevice,
            textures.White,
            "GeneratedEK9 GAME_AXIS_FORWARD_PLUS_Z",
            new Vector3(0f, y + 0.06f, 0.62f),
            new Vector3(0.035f, 0.035f, 1.24f),
            VehicleMaterial.CreateDefault(VehicleMaterialCategory.Paint, new Vector3(0.08f, 0.88f, 0.12f))));
        meshes.Add(Box(
            graphicsDevice,
            textures.White,
            "GeneratedEK9 GAME_AXIS_UP_PLUS_Y",
            new Vector3(0f, y + 0.34f, 0f),
            new Vector3(0.035f, 0.68f, 0.035f),
            VehicleMaterial.CreateDefault(VehicleMaterialCategory.Paint, new Vector3(0.08f, 0.22f, 1f))));
    }

    private static StaticMesh Box(
        GraphicsDevice graphicsDevice,
        Texture2D texture,
        string name,
        Vector3 center,
        Vector3 size,
        VehicleMaterial material)
    {
        MeshBuilder builder = new();
        builder.AddBox(center, size);
        return builder.Build(graphicsDevice, name, texture, material.ToBasicEffectDiffuseColor(), vehicleMaterial: material);
    }

    private static void AddWheel(
        List<StaticMesh> meshes,
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        string name,
        WheelCorner corner,
        Vector3 pivot)
    {
        MeshBuilder tyre = new();
        tyre.AddCylinderX(Vector3.Zero, TyreRadius, TyreWidth, 12);
        meshes.Add(tyre.Build(
            graphicsDevice,
            $"{name}_TYRE",
            textures.Tire,
            Vector3.One,
            isWheelMesh: true,
            vehicleMaterial: VehicleMaterial.CreateDefault(VehicleMaterialCategory.TyreRubber),
            wheelCorner: corner,
            localPivot: pivot));

        MeshBuilder rim = new();
        rim.AddCylinderX(Vector3.Zero, TyreRadius * 0.56f, TyreWidth * 1.06f, 12);
        meshes.Add(rim.Build(
            graphicsDevice,
            $"{name}_RIM",
            textures.White,
            Vector3.One,
            isWheelMesh: true,
            vehicleMaterial: VehicleMaterial.CreateDefault(VehicleMaterialCategory.WheelPaintOrMetal),
            wheelCorner: corner,
            localPivot: pivot));
    }
}
