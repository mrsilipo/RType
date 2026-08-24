using Microsoft.Xna.Framework;

namespace RType.Rendering;

public enum VehicleMaterialCategory
{
    Paint,
    Glass,
    TyreRubber,
    WheelPaintOrMetal,
    Brake,
    BlackPlastic,
    Trim,
    ExhaustMetal,
    HeadlightLens,
    TaillightLens,
    ClearTailLens,
    EmissiveLight,
    Interior
}

public readonly record struct VehicleMaterial(
    VehicleMaterialCategory Category,
    Vector3 BaseColor,
    float Metallic,
    float Roughness,
    float SpecularStrength,
    float ReflectionStrength,
    float FresnelStrength,
    float Opacity,
    Vector3 EmissiveColor,
    float EmissiveStrength)
{
    public static VehicleMaterial CreateDefault(VehicleMaterialCategory category, Vector3? baseColor = null)
    {
        Vector3 resolvedBaseColor = baseColor.HasValue
            ? ClampColor(baseColor.Value)
            : DefaultBaseColor(category);

        return category switch
        {
            VehicleMaterialCategory.Glass => Create(
                category, resolvedBaseColor, 0f, 0.08f, 0.92f, 0.64f, 0.88f, 0.50f, Vector3.Zero, 0f),
            VehicleMaterialCategory.TyreRubber => Create(
                category, resolvedBaseColor, 0f, 0.86f, 0.12f, 0.02f, 0.05f, 1f, Vector3.Zero, 0f),
            VehicleMaterialCategory.WheelPaintOrMetal => Create(
                category, resolvedBaseColor, 0.22f, 0.28f, 0.68f, 0.30f, 0.28f, 1f, Vector3.Zero, 0f),
            VehicleMaterialCategory.Brake => Create(
                category, resolvedBaseColor, 0.45f, 0.42f, 0.46f, 0.18f, 0.18f, 1f, Vector3.Zero, 0f),
            VehicleMaterialCategory.BlackPlastic => Create(
                category, resolvedBaseColor, 0f, 0.64f, 0.24f, 0.08f, 0.10f, 1f, Vector3.Zero, 0f),
            VehicleMaterialCategory.Trim => Create(
                category, resolvedBaseColor, 0.18f, 0.34f, 0.52f, 0.24f, 0.28f, 1f, Vector3.Zero, 0f),
            VehicleMaterialCategory.ExhaustMetal => Create(
                category, resolvedBaseColor, 1f, 0.30f, 0.82f, 0.40f, 0.24f, 1f, Vector3.Zero, 0f),
            VehicleMaterialCategory.HeadlightLens => Create(
                category, resolvedBaseColor, 0f, 0.025f, 1.0f, 0.72f, 0.96f, 0.34f, Vector3.Zero, 0f),
            VehicleMaterialCategory.TaillightLens => Create(
                category, resolvedBaseColor, 0f, 0.045f, 0.98f, 0.56f, 0.90f, 0.78f, new Vector3(1.0f, 0.035f, 0.018f), 0.10f),
            VehicleMaterialCategory.ClearTailLens => Create(
                category, resolvedBaseColor, 0f, 0.050f, 0.95f, 0.52f, 0.86f, 0.62f, Vector3.Zero, 0f),
            VehicleMaterialCategory.EmissiveLight => Create(
                category, resolvedBaseColor, 0f, 0.16f, 0.54f, 0.22f, 0.28f, 1f, resolvedBaseColor, 0.38f),
            VehicleMaterialCategory.Interior => Create(
                category, resolvedBaseColor, 0f, 0.74f, 0.18f, 0.03f, 0.06f, 1f, Vector3.Zero, 0f),
            _ => Create(
                category, resolvedBaseColor, 0f, 0.18f, 0.78f, 0.46f, 0.58f, 1f, Vector3.Zero, 0f)
        };
    }

    public static VehicleMaterial FromBasicEffect(
        Vector3 diffuseColor,
        float alpha,
        Vector3 specularColor,
        float specularPower,
        Vector3 emissiveColor)
    {
        float specularStrength = MathHelper.Clamp(MathF.Max(specularColor.X, MathF.Max(specularColor.Y, specularColor.Z)), 0f, 1f);
        float gloss = MathHelper.Clamp((specularPower - 8f) / 120f, 0f, 1f);
        float roughness = MathHelper.Clamp(1f - MathF.Sqrt(gloss), 0.08f, 0.90f);
        float emissiveStrength = MathHelper.Clamp(MathF.Max(emissiveColor.X, MathF.Max(emissiveColor.Y, emissiveColor.Z)), 0f, 1f);

        return Create(
            VehicleMaterialCategory.Paint,
            diffuseColor,
            0f,
            roughness,
            specularStrength,
            specularStrength * 0.45f,
            specularStrength * 0.45f,
            alpha,
            emissiveStrength > 0f ? emissiveColor / MathF.Max(emissiveStrength, 0.001f) : Vector3.Zero,
            emissiveStrength);
    }

    public Vector3 ToBasicEffectDiffuseColor()
    {
        float diffuseScale = Category switch
        {
            VehicleMaterialCategory.Glass => 0.62f,
            VehicleMaterialCategory.HeadlightLens => 0.38f,
            VehicleMaterialCategory.TaillightLens => 0.86f,
            VehicleMaterialCategory.ClearTailLens => 0.78f,
            VehicleMaterialCategory.EmissiveLight => 0.82f,
            _ => MathHelper.Lerp(1f, 0.76f, Metallic)
        };

        return ClampColor(BaseColor * diffuseScale);
    }

    public Vector3 ToBasicEffectSpecularColor()
    {
        Vector3 tint = Vector3.Lerp(Vector3.One, ClampColor(BaseColor), Metallic * 0.65f);
        float strength = SpecularStrength * (0.55f + ReflectionStrength * 0.70f + FresnelStrength * 0.20f);
        return ClampColor(tint * strength);
    }

    public float ToBasicEffectSpecularPower()
    {
        float gloss = 1f - MathHelper.Clamp(Roughness, 0f, 1f);
        return MathHelper.Lerp(8f, 128f, gloss * gloss);
    }

    public Vector3 ToBasicEffectEmissiveColor()
    {
        return ClampColor(EmissiveColor * EmissiveStrength);
    }

    public static Vector3 ClampColor(Vector3 color)
    {
        return new Vector3(
            MathHelper.Clamp(color.X, 0f, 1f),
            MathHelper.Clamp(color.Y, 0f, 1f),
            MathHelper.Clamp(color.Z, 0f, 1f));
    }

    private static VehicleMaterial Create(
        VehicleMaterialCategory category,
        Vector3 baseColor,
        float metallic,
        float roughness,
        float specularStrength,
        float reflectionStrength,
        float fresnelStrength,
        float opacity,
        Vector3 emissiveColor,
        float emissiveStrength)
    {
        return new VehicleMaterial(
            category,
            ClampColor(baseColor),
            MathHelper.Clamp(metallic, 0f, 1f),
            MathHelper.Clamp(roughness, 0.02f, 1f),
            MathHelper.Clamp(specularStrength, 0f, 1f),
            MathHelper.Clamp(reflectionStrength, 0f, 1f),
            MathHelper.Clamp(fresnelStrength, 0f, 1f),
            MathHelper.Clamp(opacity, 0f, 1f),
            ClampColor(emissiveColor),
            MathHelper.Clamp(emissiveStrength, 0f, 1f));
    }

    private static Vector3 DefaultBaseColor(VehicleMaterialCategory category)
    {
        return category switch
        {
            VehicleMaterialCategory.Glass => new Vector3(0.06f, 0.10f, 0.13f),
            VehicleMaterialCategory.TyreRubber => new Vector3(0.025f, 0.025f, 0.023f),
            VehicleMaterialCategory.WheelPaintOrMetal => new Vector3(0.88f, 0.86f, 0.78f),
            VehicleMaterialCategory.Brake => new Vector3(0.58f, 0.04f, 0.025f),
            VehicleMaterialCategory.BlackPlastic => new Vector3(0.018f, 0.018f, 0.020f),
            VehicleMaterialCategory.Trim => new Vector3(0.050f, 0.050f, 0.052f),
            VehicleMaterialCategory.ExhaustMetal => new Vector3(0.48f, 0.46f, 0.42f),
            VehicleMaterialCategory.HeadlightLens => new Vector3(0.88f, 0.96f, 1.0f),
            VehicleMaterialCategory.TaillightLens => new Vector3(0.82f, 0.008f, 0.006f),
            VehicleMaterialCategory.ClearTailLens => new Vector3(0.76f, 0.78f, 0.76f),
            VehicleMaterialCategory.EmissiveLight => new Vector3(1.0f, 0.86f, 0.52f),
            VehicleMaterialCategory.Interior => new Vector3(0.035f, 0.035f, 0.038f),
            _ => HondaPaintColors.ChampionshipWhite.BaseColor
        };
    }
}
