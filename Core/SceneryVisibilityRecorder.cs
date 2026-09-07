using Microsoft.Xna.Framework;
using RType.Camera;
using RType.Rendering;
using RType.World;

namespace RType.Core;

internal sealed class SceneryVisibilityRecorder : IDisposable
{
    private const int FrameStride = 10;

    private StreamWriter? _writer;
    private int _frameIndex;

    public void Update(string path, TrackScene? track, ChaseCamera? camera)
    {
        if (string.IsNullOrWhiteSpace(path) || track is null || camera is null)
        {
            return;
        }

        _frameIndex++;
        if (_frameIndex % FrameStride != 0)
        {
            return;
        }

        EnsureOpen(path);
        if (_writer is null)
        {
            return;
        }

        BoundingFrustum frustum = new(camera.View * camera.Projection);
        foreach (StaticMesh mesh in track.Meshes)
        {
            if (!mesh.IsTransparent)
            {
                continue;
            }

            BoundingSphere sphere = mesh.BoundingSphere;
            float distance = Vector3.Distance(camera.Position, sphere.Center);
            float projectedRadius = distance <= 0.001f ? 999f : sphere.Radius / distance;
            ContainmentType containment = frustum.Contains(sphere);
            _writer.WriteLine(
                string.Join(
                    ',',
                    _frameIndex.ToString(),
                    Escape(mesh.Name),
                    camera.Position.X.ToString("0.###"),
                    camera.Position.Y.ToString("0.###"),
                    camera.Position.Z.ToString("0.###"),
                    sphere.Center.X.ToString("0.###"),
                    sphere.Center.Y.ToString("0.###"),
                    sphere.Center.Z.ToString("0.###"),
                    sphere.Radius.ToString("0.###"),
                    distance.ToString("0.###"),
                    projectedRadius.ToString("0.######"),
                    containment.ToString()));
        }

        _writer.Flush();
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _writer = null;
    }

    private void EnsureOpen(string path)
    {
        if (_writer is not null)
        {
            return;
        }

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _writer = new StreamWriter(File.Create(fullPath));
        _writer.WriteLine("frame,mesh,cameraX,cameraY,cameraZ,centerX,centerY,centerZ,radius,distance,projectedRadius,frustum");
        Console.WriteLine($"Writing scenery visibility log '{fullPath}'.");
    }

    private static string Escape(string value)
    {
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
