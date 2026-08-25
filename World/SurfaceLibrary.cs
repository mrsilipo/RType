namespace RType.World;

public sealed class SurfaceLibrary
{
    public SurfaceLibrary(SurfaceSample road, SurfaceSample curb, SurfaceSample grass, SurfaceSample dirt)
    {
        Road = road;
        Curb = curb;
        Grass = grass;
        Dirt = dirt;
    }

    public SurfaceSample Road { get; }

    public SurfaceSample Curb { get; }

    public SurfaceSample Grass { get; }

    public SurfaceSample Dirt { get; }
}
