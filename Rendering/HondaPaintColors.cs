using Microsoft.Xna.Framework;

namespace RType.Rendering;

public readonly record struct VehiclePaintColor(
    string Name,
    string PaintCode,
    string Hex,
    Color SrgbColor,
    bool HasMetallicOrPearl,
    Vector3 PearlTint)
{
    public Vector3 BaseColor => SrgbColor.ToVector3();
}

public static class HondaPaintColors
{
    public static readonly VehiclePaintColor ChampionshipWhite = new(
        "Championship White",
        "NH-0",
        "#F1F1EE",
        new Color(241, 241, 238),
        HasMetallicOrPearl: false,
        PearlTint: Vector3.Zero);

    public static readonly VehiclePaintColor SunlightYellow = new(
        "Sunlight Yellow",
        "Y-56",
        "#F7C215",
        new Color(247, 194, 21),
        HasMetallicOrPearl: false,
        PearlTint: Vector3.Zero);

    public static readonly VehiclePaintColor VogueSilverMetallic = new(
        "Vogue Silver Metallic",
        "NH-583M",
        "#AAA9AD",
        new Color(170, 169, 173),
        HasMetallicOrPearl: true,
        PearlTint: new Vector3(0.74f, 0.74f, 0.78f));

    public static readonly VehiclePaintColor FlamencoBlackPearl = new(
        "Flamenco Black Pearl",
        "NH-592P",
        "#1A1B1C",
        new Color(26, 27, 28),
        HasMetallicOrPearl: true,
        PearlTint: new Vector3(1.0f, 0.58f, 0.20f));

    public static IReadOnlyList<VehiclePaintColor> Ek9FactoryColors { get; } =
    [
        ChampionshipWhite,
        SunlightYellow,
        VogueSilverMetallic,
        FlamencoBlackPearl
    ];
}
