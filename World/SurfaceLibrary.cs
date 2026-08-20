namespace RetroRacer.World;

public sealed class SurfaceLibrary
{
    public SurfaceLibrary(SurfaceSample road, SurfaceSample curb, SurfaceSample grass)
    {
        Road = road;
        Curb = curb;
        Grass = grass;
    }

    public SurfaceSample Road { get; }

    public SurfaceSample Curb { get; }

    public SurfaceSample Grass { get; }
}
