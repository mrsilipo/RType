using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RType.Rendering;

public sealed class CarModel : IDisposable
{
    private const string DefaultModelPath = "Assets/Models/Ek9/ek9.FBX";

    private readonly List<StaticMesh> _meshes;
    private readonly List<StaticMesh> _bodyMeshes;
    private readonly List<StaticMesh> _wheelMeshes;

    private CarModel(List<StaticMesh> meshes)
    {
        _meshes = meshes;
        _wheelMeshes = [.. meshes.Where(mesh => mesh.IsWheelMesh)];
        _bodyMeshes = [.. meshes.Where(mesh => !mesh.IsWheelMesh)];
        if (_bodyMeshes.Count == 0)
        {
            _bodyMeshes = [.. meshes];
        }
    }

    public IReadOnlyList<StaticMesh> Meshes => _meshes;

    public IReadOnlyList<StaticMesh> BodyMeshes => _bodyMeshes;

    public IReadOnlyList<StaticMesh> WheelMeshes => _wheelMeshes;

    public static CarModel Create(GraphicsDevice graphicsDevice, GeneratedTextures textures)
    {
        if (Directory.Exists(ResolveExistingModelDirectory("Assets/Models/GeneratedEK9")))
        {
            Console.WriteLine("Using generated EK9 reference model.");
            return new CarModel(GeneratedEk9ReferenceModelFactory.Create(graphicsDevice, textures));
        }

        string path = ResolveExistingModelPath(DefaultModelPath);
        List<StaticMesh>? importedMeshes = FbxCarModelLoader.TryLoad(graphicsDevice, path, textures);
        return new CarModel(importedMeshes ?? CreatePlaceholderMeshes(graphicsDevice, textures));
    }

    public void Dispose()
    {
        foreach (StaticMesh mesh in _meshes)
        {
            mesh.Dispose();
        }
    }

    private static List<StaticMesh> CreatePlaceholderMeshes(GraphicsDevice graphicsDevice, GeneratedTextures textures)
    {
        return
        [
            MeshFactory.CreateBox(
                graphicsDevice,
                new Vector3(0f, 0.48f, 0f),
                new Vector3(1.70f, 0.56f, 4.25f),
                textures.CarRed,
                Vector3.One,
                "placeholder body",
                VehicleMaterial.CreateDefault(VehicleMaterialCategory.Paint, new Vector3(0.82f, 0.035f, 0.025f))),
            MeshFactory.CreateBox(
                graphicsDevice,
                new Vector3(0f, 0.91f, -0.34f),
                new Vector3(1.14f, 0.56f, 1.32f),
                textures.CarGlass,
                new Vector3(0.78f, 0.9f, 1.0f),
                "placeholder cabin",
                VehicleMaterial.CreateDefault(VehicleMaterialCategory.Glass)),
            MeshFactory.CreateBox(
                graphicsDevice,
                new Vector3(0f, 0.57f, 2.22f),
                new Vector3(1.38f, 0.24f, 0.22f),
                textures.White,
                new Vector3(1.0f, 0.92f, 0.70f),
                "placeholder headlights",
                VehicleMaterial.CreateDefault(VehicleMaterialCategory.HeadlightLens)),
            MeshFactory.CreateBox(
                graphicsDevice,
                new Vector3(0f, 0.62f, -2.22f),
                new Vector3(1.48f, 0.26f, 0.20f),
                textures.CarRed,
                new Vector3(0.65f, 0.12f, 0.12f),
                "placeholder rear panel",
                VehicleMaterial.CreateDefault(VehicleMaterialCategory.TaillightLens)),
            MeshFactory.CreateCarWheelSet(graphicsDevice, textures.Tire)
        ];
    }

    private static string ResolveExistingModelPath(string relativePath)
    {
        foreach (string path in GetCandidateModelPaths(relativePath))
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return Path.Combine(AppContext.BaseDirectory, relativePath);
    }

    private static string ResolveExistingModelDirectory(string relativePath)
    {
        foreach (string path in GetCandidateModelPaths(relativePath))
        {
            if (Directory.Exists(path))
            {
                return path;
            }
        }

        return Path.Combine(AppContext.BaseDirectory, relativePath);
    }

    private static IEnumerable<string> GetCandidateModelPaths(string relativePath)
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
}
