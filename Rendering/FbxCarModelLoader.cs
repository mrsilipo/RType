using System.IO.Compression;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RType.Rendering;

internal static class FbxCarModelLoader
{
    private const string MeshCacheExtension = ".rtmesh";
    private const string MeshCacheMagic = "RTRMESH";
    private const int MeshCacheVersion = 14;
    private const float TargetBodyLengthMeters = 4.185f;
    private const float TargetWheelbaseMeters = 2.620f;
    private const float TargetFrontWeightDistribution = 0.620f;
    private const float MinimumUsableExtentMeters = 0.001f;

    private static readonly HashSet<string> SupportedTextureExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp"
    };

    public static List<StaticMesh>? TryLoad(GraphicsDevice graphicsDevice, string path, GeneratedTextures textures)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            string cachePath = Path.ChangeExtension(path, MeshCacheExtension);
            bool loadedFromCache = TryLoadMeshCache(cachePath, path, out List<ImportedMesh>? importedMeshes);
            if (!loadedFromCache)
            {
                FbxDocument document = FbxDocument.Load(path);
                importedMeshes = BuildImportedMeshes(document);
            }

            if (importedMeshes.Count == 0)
            {
                return null;
            }

            MeshBounds authoredBounds = CalculateBounds(importedMeshes);
            if (!loadedFromCache)
            {
                NormalizeToVehicleOrigin(importedMeshes);
                TrySaveMeshCache(cachePath, importedMeshes);
            }

            MeshBounds fittedBounds = CalculateBounds(importedMeshes);
            int totalTriangles = importedMeshes.Sum(mesh => mesh.Indices.Length / 3);
            Console.WriteLine(
                $"Loaded FBX car model '{path}' mode={(loadedFromCache ? "cache" : "fbx")} " +
                $"meshes={importedMeshes.Count}, tris={totalTriangles}, " +
                $"authored={FormatBounds(authoredBounds)}, fitted={FormatBounds(fittedBounds)}");

            List<StaticMesh> meshes = new(importedMeshes.Count);
            string modelDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory;
            Dictionary<string, Texture2D> loadedTextures = new(StringComparer.OrdinalIgnoreCase);
            foreach (ImportedMesh mesh in importedMeshes)
            {
                (Texture2D texture, Vector3 diffuseColor) = ResolveMaterial(
                    graphicsDevice,
                    mesh,
                    textures,
                    modelDirectory,
                    loadedTextures);
                meshes.Add(new StaticMesh(
                    graphicsDevice,
                    mesh.Name,
                    mesh.Vertices,
                    mesh.Indices,
                    texture,
                    diffuseColor,
                    mesh.IsWheelMesh,
                    mesh.Alpha,
                    mesh.SpecularColor,
                    mesh.SpecularPower,
                    mesh.EmissiveColor,
                    mesh.VehicleMaterial));
            }

            return meshes;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
        {
            Console.Error.WriteLine($"Failed to load FBX car model '{path}': {ex.Message}");
            return null;
        }
    }

    private static List<ImportedMesh> BuildImportedMeshes(FbxDocument document)
    {
        FbxNode objects = document.RequiredRoot("Objects");
        Dictionary<long, FbxNode> geometryNodes = [];
        Dictionary<long, FbxModel> models = [];
        Dictionary<long, FbxMaterial> materials = [];
        Dictionary<long, FbxTexture> textures = [];
        Dictionary<long, FbxVideo> videos = [];

        foreach (FbxNode node in objects.Children)
        {
            if (node.Name == "Geometry" && node.Properties.Count >= 3 && IsMeshClass(node.Properties[2]))
            {
                geometryNodes[(long)node.Properties[0]] = node;
            }
            else if (node.Name == "Model" && node.Properties.Count >= 3)
            {
                FbxModel model = ReadModel(node, IsMeshClass(node.Properties[2]));
                models[model.Id] = model;
            }
            else if (node.Name == "Material" && node.Properties.Count >= 2)
            {
                FbxMaterial material = ReadMaterial(node);
                materials[material.Id] = material;
            }
            else if (node.Name == "Texture" && node.Properties.Count >= 2)
            {
                FbxTexture texture = ReadTexture(node);
                textures[texture.Id] = texture;
            }
            else if (node.Name == "Video" && node.Properties.Count >= 2)
            {
                FbxVideo video = ReadVideo(node);
                videos[video.Id] = video;
            }
        }

        List<FbxConnection> connections = ReadConnections(document);
        Dictionary<long, long> objectParents = ReadObjectParentConnections(connections);
        Dictionary<long, List<long>> modelMaterials = ReadModelMaterialConnections(connections, models, materials);
        Dictionary<long, FbxMaterial> resolvedMaterials = ResolveMaterialTextures(
            connections,
            materials,
            textures,
            videos);

        foreach ((long modelId, FbxModel model) in models.ToArray())
        {
            if (objectParents.TryGetValue(modelId, out long parentId) &&
                models.ContainsKey(parentId))
            {
                models[modelId] = model with { ParentId = parentId };
            }
        }

        Dictionary<long, Matrix> modelWorldTransforms = BuildModelWorldTransforms(models);
        List<ImportedMesh> meshes = [];
        foreach ((long geometryId, FbxNode geometryNode) in geometryNodes)
        {
            if (!objectParents.TryGetValue(geometryId, out long modelId))
            {
                continue;
            }

            if (!models.TryGetValue(modelId, out FbxModel? model) || !model.IsMesh)
            {
                continue;
            }

            Matrix modelWorld = modelWorldTransforms.TryGetValue(model.Id, out Matrix world)
                ? world
                : CreateLocalTransform(model);
            IReadOnlyList<long> materialIds = modelMaterials.TryGetValue(model.Id, out List<long>? ids)
                ? ids
                : [];

            foreach (ImportedMesh mesh in BuildMeshes(
                         geometryNode,
                         model,
                         modelWorld,
                         materialIds,
                         resolvedMaterials))
            {
                meshes.Add(mesh);
            }
        }

        return meshes;
    }

    private static List<FbxConnection> ReadConnections(FbxDocument document)
    {
        FbxNode? connections = document.Root("Connections");
        if (connections is null)
        {
            return [];
        }

        List<FbxConnection> parsedConnections = [];
        foreach (FbxNode connection in connections.Children)
        {
            if (connection.Name != "C" ||
                connection.Properties.Count < 3 ||
                connection.Properties[0] is not string relationship ||
                connection.Properties[1] is not long childId ||
                connection.Properties[2] is not long parentId)
            {
                continue;
            }

            string propertyName = connection.Properties.Count >= 4
                ? connection.Properties[3] as string ?? string.Empty
                : string.Empty;
            parsedConnections.Add(new FbxConnection(relationship, childId, parentId, propertyName));
        }

        return parsedConnections;
    }

    private static Dictionary<long, long> ReadObjectParentConnections(IEnumerable<FbxConnection> connections)
    {
        Dictionary<long, long> objectParents = [];
        foreach (FbxConnection connection in connections)
        {
            if (connection.Relationship.Equals("OO", StringComparison.Ordinal))
            {
                objectParents[connection.ChildId] = connection.ParentId;
            }
        }

        return objectParents;
    }

    private static Dictionary<long, List<long>> ReadModelMaterialConnections(
        IEnumerable<FbxConnection> connections,
        IReadOnlyDictionary<long, FbxModel> models,
        IReadOnlyDictionary<long, FbxMaterial> materials)
    {
        Dictionary<long, List<long>> modelMaterials = [];
        foreach (FbxConnection connection in connections)
        {
            if (!materials.ContainsKey(connection.ChildId) ||
                !models.ContainsKey(connection.ParentId))
            {
                continue;
            }

            if (!modelMaterials.TryGetValue(connection.ParentId, out List<long>? materialIds))
            {
                materialIds = [];
                modelMaterials[connection.ParentId] = materialIds;
            }

            materialIds.Add(connection.ChildId);
        }

        return modelMaterials;
    }

    private static Dictionary<long, FbxMaterial> ResolveMaterialTextures(
        IEnumerable<FbxConnection> connections,
        IReadOnlyDictionary<long, FbxMaterial> materials,
        IReadOnlyDictionary<long, FbxTexture> textures,
        IReadOnlyDictionary<long, FbxVideo> videos)
    {
        Dictionary<long, long> materialTextures = [];
        Dictionary<long, long> textureVideos = [];
        foreach (FbxConnection connection in connections)
        {
            if (textures.ContainsKey(connection.ChildId) &&
                materials.ContainsKey(connection.ParentId) &&
                (connection.PropertyName.Length == 0 ||
                 connection.PropertyName.Contains("Diffuse", StringComparison.OrdinalIgnoreCase)))
            {
                materialTextures[connection.ParentId] = connection.ChildId;
            }
            else if (videos.ContainsKey(connection.ChildId) && textures.ContainsKey(connection.ParentId))
            {
                textureVideos[connection.ParentId] = connection.ChildId;
            }
        }

        Dictionary<long, FbxMaterial> resolvedMaterials = [];
        foreach ((long materialId, FbxMaterial material) in materials)
        {
            string? texturePath = null;
            if (materialTextures.TryGetValue(materialId, out long textureId) &&
                textures.TryGetValue(textureId, out FbxTexture? texture))
            {
                texturePath = FirstUsablePath(texture.RelativeFileName, texture.FileName);
                if (texturePath is null &&
                    textureVideos.TryGetValue(textureId, out long videoId) &&
                    videos.TryGetValue(videoId, out FbxVideo? video))
                {
                    texturePath = FirstUsablePath(video.RelativeFileName, video.FileName);
                }
            }

            resolvedMaterials[materialId] = material with { TexturePath = texturePath };
        }

        return resolvedMaterials;
    }

    private static Dictionary<long, Matrix> BuildModelWorldTransforms(Dictionary<long, FbxModel> models)
    {
        Dictionary<long, Matrix> transforms = [];
        HashSet<long> visiting = [];
        foreach (long modelId in models.Keys)
        {
            transforms[modelId] = BuildModelWorldTransform(modelId, models, transforms, visiting);
        }

        return transforms;
    }

    private static Matrix BuildModelWorldTransform(
        long modelId,
        IReadOnlyDictionary<long, FbxModel> models,
        Dictionary<long, Matrix> transforms,
        HashSet<long> visiting)
    {
        if (transforms.TryGetValue(modelId, out Matrix existing))
        {
            return existing;
        }

        if (!models.TryGetValue(modelId, out FbxModel? model))
        {
            return Matrix.Identity;
        }

        Matrix local = CreateLocalTransform(model);
        if (model.ParentId is not long parentId ||
            !models.ContainsKey(parentId) ||
            !visiting.Add(modelId))
        {
            transforms[modelId] = local;
            return local;
        }

        Matrix parent = BuildModelWorldTransform(parentId, models, transforms, visiting);
        visiting.Remove(modelId);
        Matrix world = local * parent;
        transforms[modelId] = world;
        return world;
    }

    private static FbxModel ReadModel(FbxNode node, bool isMesh)
    {
        FbxModel model = new(
            (long)node.Properties[0],
            CleanFbxName((string)node.Properties[1]),
            isMesh,
            Vector3.Zero,
            Vector3.Zero,
            Vector3.One,
            null);

        FbxNode? properties = node.Child("Properties70");
        if (properties is null)
        {
            return model;
        }

        foreach (FbxNode property in properties.Children)
        {
            if (property.Name != "P" ||
                property.Properties.Count < 7 ||
                property.Properties[0] is not string propertyName)
            {
                continue;
            }

            Vector3 value = new(
                Convert.ToSingle(property.Properties[4]),
                Convert.ToSingle(property.Properties[5]),
                Convert.ToSingle(property.Properties[6]));

            model = propertyName switch
            {
                "Lcl Translation" => model with { Translation = value },
                "Lcl Rotation" => model with { RotationDegrees = value },
                "Lcl Scaling" => model with { Scaling = value },
                _ => model
            };
        }

        return model;
    }

    private static FbxMaterial ReadMaterial(FbxNode node)
    {
        long id = (long)node.Properties[0];
        string name = CleanFbxName((string)node.Properties[1]);
        Vector3 diffuseColor = Vector3.One;
        Vector3 specularColor = Vector3.Zero;
        Vector3 emissiveColor = Vector3.Zero;
        float diffuseFactor = 1f;
        float specularFactor = 1f;
        float emissiveFactor = 1f;
        float opacity = 1f;
        float specularPower = 16f;

        FbxNode? properties = node.Child("Properties70");
        if (properties is null)
        {
            return new FbxMaterial(
                id,
                name,
                diffuseColor,
                opacity,
                specularColor,
                specularPower,
                emissiveColor,
                null);
        }

        foreach (FbxNode property in properties.Children)
        {
            if (property.Name != "P" ||
                property.Properties.Count < 5 ||
                property.Properties[0] is not string propertyName)
            {
                continue;
            }

            switch (propertyName)
            {
                case "DiffuseColor" when property.Properties.Count >= 7:
                    diffuseColor = ReadVector3Property(property, 4);
                    break;
                case "DiffuseFactor":
                    diffuseFactor = Clamp01(Convert.ToSingle(property.Properties[4]));
                    break;
                case "SpecularColor" when property.Properties.Count >= 7:
                    specularColor = ReadVector3Property(property, 4);
                    break;
                case "SpecularFactor":
                    specularFactor = Clamp01(Convert.ToSingle(property.Properties[4]));
                    break;
                case "Shininess":
                case "ShininessExponent":
                    specularPower = MathHelper.Clamp(Convert.ToSingle(property.Properties[4]), 1f, 128f);
                    break;
                case "EmissiveColor" when property.Properties.Count >= 7:
                    emissiveColor = ReadVector3Property(property, 4);
                    break;
                case "EmissiveFactor":
                    emissiveFactor = Clamp01(Convert.ToSingle(property.Properties[4]));
                    break;
                case "Opacity":
                    opacity = Clamp01(Convert.ToSingle(property.Properties[4]));
                    break;
                case "TransparencyFactor":
                    opacity = Clamp01(1f - Convert.ToSingle(property.Properties[4]));
                    break;
            }
        }

        return new FbxMaterial(
            id,
            name,
            ClampColor(diffuseColor * diffuseFactor),
            opacity,
            ClampColor(specularColor * specularFactor),
            specularPower,
            ClampColor(emissiveColor * emissiveFactor),
            null);
    }

    private static FbxTexture ReadTexture(FbxNode node)
    {
        FbxTexture texture = new(
            (long)node.Properties[0],
            CleanFbxName((string)node.Properties[1]),
            null,
            null);

        return texture with
        {
            FileName = ReadNamedChildString(node, "FileName"),
            RelativeFileName = ReadNamedChildString(node, "RelativeFilename")
        };
    }

    private static FbxVideo ReadVideo(FbxNode node)
    {
        FbxVideo video = new(
            (long)node.Properties[0],
            CleanFbxName((string)node.Properties[1]),
            null,
            null);

        return video with
        {
            FileName = ReadNamedChildString(node, "FileName"),
            RelativeFileName = ReadNamedChildString(node, "RelativeFilename")
        };
    }

    private static List<ImportedMesh> BuildMeshes(
        FbxNode geometryNode,
        FbxModel model,
        Matrix modelWorld,
        IReadOnlyList<long> modelMaterialIds,
        IReadOnlyDictionary<long, FbxMaterial> materials)
    {
        double[]? sourceVertices = geometryNode.Child("Vertices")?.Properties.FirstOrDefault() as double[];
        int[]? polygonVertexIndices = geometryNode.Child("PolygonVertexIndex")?.Properties.FirstOrDefault() as int[];
        if (sourceVertices is null ||
            polygonVertexIndices is null ||
            sourceVertices.Length < 3 ||
            polygonVertexIndices.Length < 3)
        {
            return [];
        }

        Vector3[] transformedVertices = TransformVertices(sourceVertices, modelWorld);
        GeometryNormalLayer? normalLayer = ReadNormalLayer(geometryNode, modelWorld);
        GeometryUvLayer? uvLayer = ReadUvLayer(geometryNode);
        GeometryMaterialLayer? materialLayer = ReadMaterialLayer(geometryNode);
        string geometryName = geometryNode.Properties.Count >= 2
            ? CleanFbxName((string)geometryNode.Properties[1])
            : string.Empty;

        Dictionary<int, ImportedMeshAccumulator> accumulators = [];
        List<PolygonVertex> polygon = [];
        int polygonIndex = 0;
        int polygonVertexCursor = 0;

        foreach (int rawIndex in polygonVertexIndices)
        {
            int vertexIndex = rawIndex < 0 ? -rawIndex - 1 : rawIndex;
            polygon.Add(new PolygonVertex(vertexIndex, polygonVertexCursor));

            if (rawIndex >= 0)
            {
                polygonVertexCursor++;
                continue;
            }

            int materialSlot = materialLayer?.GetSlot(polygonIndex, polygon[0].PolygonVertexIndex) ?? 0;
            ImportedMeshAccumulator accumulator = GetMeshAccumulator(
                accumulators,
                materialSlot,
                model,
                geometryName,
                modelMaterialIds,
                materials);
            AddPolygonTriangles(
                polygon,
                transformedVertices,
                normalLayer,
                uvLayer,
                accumulator.Vertices,
                accumulator.Indices);
            polygon.Clear();
            polygonIndex++;
            polygonVertexCursor++;
        }

        List<ImportedMesh> meshes = [];
        foreach (ImportedMeshAccumulator accumulator in accumulators.Values)
        {
            if (accumulator.Vertices.Count == 0)
            {
                continue;
            }

            meshes.Add(new ImportedMesh(
                accumulator.Name,
                accumulator.Vertices.ToArray(),
                accumulator.Indices.ToArray(),
                accumulator.MaterialKind,
                accumulator.DiffuseColor,
                accumulator.Alpha,
                accumulator.SpecularColor,
                accumulator.SpecularPower,
                accumulator.EmissiveColor,
                accumulator.TexturePath,
                accumulator.VehicleMaterial,
                IsWheelComponentName(accumulator.Name)));
        }

        return meshes;
    }

    private static Matrix CreateLocalTransform(FbxModel model)
    {
        return Matrix.CreateScale(model.Scaling) *
            Matrix.CreateRotationX(MathHelper.ToRadians(model.RotationDegrees.X)) *
            Matrix.CreateRotationY(MathHelper.ToRadians(model.RotationDegrees.Y)) *
            Matrix.CreateRotationZ(MathHelper.ToRadians(model.RotationDegrees.Z)) *
            Matrix.CreateTranslation(model.Translation);
    }

    private static ImportedMeshAccumulator GetMeshAccumulator(
        Dictionary<int, ImportedMeshAccumulator> accumulators,
        int materialSlot,
        FbxModel model,
        string geometryName,
        IReadOnlyList<long> modelMaterialIds,
        IReadOnlyDictionary<long, FbxMaterial> materials)
    {
        if (accumulators.TryGetValue(materialSlot, out ImportedMeshAccumulator? accumulator))
        {
            return accumulator;
        }

        FbxMaterial? material = null;
        if (modelMaterialIds.Count > 0)
        {
            int materialIndex = Math.Clamp(materialSlot, 0, modelMaterialIds.Count - 1);
            materials.TryGetValue(modelMaterialIds[materialIndex], out material);
        }

        string materialName = material?.Name ?? string.Empty;
        string combinedName = CombineMaterialNames(model.Name, geometryName, materialName);
        VehicleMaterial vehicleMaterial = ResolveVehicleMaterial(
            combinedName,
            material?.DiffuseColor,
            material?.Opacity,
            material?.EmissiveColor,
            null);
        ImportedMaterialKind materialKind = ToImportedMaterialKind(vehicleMaterial.Category);
        string name = string.IsNullOrWhiteSpace(materialName)
            ? $"fbx {model.Name}"
            : $"fbx {model.Name} {materialName}";

        accumulator = new ImportedMeshAccumulator(
            name,
            materialKind,
            vehicleMaterial.ToBasicEffectDiffuseColor(),
            vehicleMaterial.Opacity,
            vehicleMaterial.ToBasicEffectSpecularColor(),
            vehicleMaterial.ToBasicEffectSpecularPower(),
            vehicleMaterial.ToBasicEffectEmissiveColor(),
            vehicleMaterial,
            material?.TexturePath);
        accumulators[materialSlot] = accumulator;
        return accumulator;
    }

    private static Vector3[] TransformVertices(double[] sourceVertices, Matrix modelWorld)
    {
        Vector3[] vertices = new Vector3[sourceVertices.Length / 3];
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 fbxPosition = new(
                (float)sourceVertices[i * 3],
                (float)sourceVertices[i * 3 + 1],
                (float)sourceVertices[i * 3 + 2]);

            fbxPosition = Vector3.Transform(fbxPosition, modelWorld);
            vertices[i] = MapFbxToGamePosition(fbxPosition);
        }

        return vertices;
    }

    private static Vector3 MapFbxToGamePosition(Vector3 fbxPosition)
    {
        // This Blender FBX arrives already posed with +Y up and +Z toward the
        // Civic's nose, which matches the game car space. Keep the handedness
        // intact so decals and plate text are not mirrored.
        return fbxPosition;
    }

    private static Vector3 MapFbxToGameVector(Vector3 fbxVector)
    {
        return fbxVector;
    }

    private static void AddPolygonTriangles(
        IReadOnlyList<PolygonVertex> polygon,
        IReadOnlyList<Vector3> sourceVertices,
        GeometryNormalLayer? normalLayer,
        GeometryUvLayer? uvLayer,
        List<VertexPositionNormalTexture> vertices,
        List<int> indices)
    {
        if (polygon.Count < 3)
        {
            return;
        }

        for (int i = 1; i < polygon.Count - 1; i++)
        {
            PolygonVertex polygonA = polygon[0];
            PolygonVertex polygonB = polygon[i];
            PolygonVertex polygonC = polygon[i + 1];
            Vector3 a = sourceVertices[polygonA.ControlPointIndex];
            Vector3 b = sourceVertices[polygonB.ControlPointIndex];
            Vector3 c = sourceVertices[polygonC.ControlPointIndex];
            Vector3 faceNormal = Vector3.Cross(b - a, c - a);
            if (faceNormal.LengthSquared() <= 0.000001f)
            {
                faceNormal = Vector3.Up;
            }
            else
            {
                faceNormal.Normalize();
            }

            Vector3 normalA = normalLayer?.GetNormal(polygonA, faceNormal) ?? faceNormal;
            Vector3 normalB = normalLayer?.GetNormal(polygonB, faceNormal) ?? faceNormal;
            Vector3 normalC = normalLayer?.GetNormal(polygonC, faceNormal) ?? faceNormal;
            Vector2 uvA = uvLayer?.GetUv(polygonA, a) ?? CreateFallbackUv(a);
            Vector2 uvB = uvLayer?.GetUv(polygonB, b) ?? CreateFallbackUv(b);
            Vector2 uvC = uvLayer?.GetUv(polygonC, c) ?? CreateFallbackUv(c);

            int start = vertices.Count;
            vertices.Add(new VertexPositionNormalTexture(a, normalA, uvA));
            vertices.Add(new VertexPositionNormalTexture(b, normalB, uvB));
            vertices.Add(new VertexPositionNormalTexture(c, normalC, uvC));
            indices.Add(start);
            indices.Add(start + 1);
            indices.Add(start + 2);
        }
    }

    private static Vector2 CreateFallbackUv(Vector3 position)
    {
        return new Vector2(position.X, position.Z);
    }

    private static GeometryNormalLayer? ReadNormalLayer(FbxNode geometryNode, Matrix modelWorld)
    {
        FbxNode? layer = geometryNode.Children.FirstOrDefault(child => child.Name == "LayerElementNormal");
        double[]? normals = layer?.Child("Normals")?.Properties.FirstOrDefault() as double[];
        if (layer is null || normals is null || normals.Length < 3)
        {
            return null;
        }

        Vector3[] transformedNormals = new Vector3[normals.Length / 3];
        for (int i = 0; i < transformedNormals.Length; i++)
        {
            Vector3 normal = new(
                (float)normals[i * 3],
                (float)normals[i * 3 + 1],
                (float)normals[i * 3 + 2]);
            normal = MapFbxToGameVector(Vector3.TransformNormal(normal, modelWorld));
            if (normal.LengthSquared() <= 0.000001f)
            {
                normal = Vector3.Up;
            }
            else
            {
                normal.Normalize();
            }

            transformedNormals[i] = normal;
        }

        return new GeometryNormalLayer(
            transformedNormals,
            layer.Child("NormalsIndex")?.Properties.FirstOrDefault() as int[],
            ReadNamedChildString(layer, "MappingInformationType") ?? "ByPolygonVertex",
            ReadNamedChildString(layer, "ReferenceInformationType") ?? "Direct");
    }

    private static GeometryUvLayer? ReadUvLayer(FbxNode geometryNode)
    {
        FbxNode? layer = geometryNode.Children.FirstOrDefault(child => child.Name == "LayerElementUV");
        double[]? uvValues = layer?.Child("UV")?.Properties.FirstOrDefault() as double[];
        if (layer is null || uvValues is null || uvValues.Length < 2)
        {
            return null;
        }

        Vector2[] uvs = new Vector2[uvValues.Length / 2];
        for (int i = 0; i < uvs.Length; i++)
        {
            uvs[i] = new Vector2((float)uvValues[i * 2], 1f - (float)uvValues[i * 2 + 1]);
        }

        return new GeometryUvLayer(
            uvs,
            layer.Child("UVIndex")?.Properties.FirstOrDefault() as int[],
            ReadNamedChildString(layer, "MappingInformationType") ?? "ByPolygonVertex",
            ReadNamedChildString(layer, "ReferenceInformationType") ?? "Direct");
    }

    private static GeometryMaterialLayer? ReadMaterialLayer(FbxNode geometryNode)
    {
        FbxNode? layer = geometryNode.Children.FirstOrDefault(child => child.Name == "LayerElementMaterial");
        int[]? materialSlots = layer?.Child("Materials")?.Properties.FirstOrDefault() as int[];
        if (layer is null || materialSlots is null || materialSlots.Length == 0)
        {
            return null;
        }

        return new GeometryMaterialLayer(
            materialSlots,
            ReadNamedChildString(layer, "MappingInformationType") ?? "AllSame",
            ReadNamedChildString(layer, "ReferenceInformationType") ?? "IndexToDirect");
    }

    private static void NormalizeToVehicleOrigin(IReadOnlyList<ImportedMesh> meshes)
    {
        MeshBounds bounds = CalculateBounds(meshes);
        float scale = CalculateFitScale(bounds);

        Vector3 offset = new(
            (bounds.Min.X + bounds.Max.X) * 0.5f,
            bounds.Min.Y,
            (bounds.Min.Z + bounds.Max.Z) * 0.5f);

        foreach (ImportedMesh mesh in meshes)
        {
            for (int i = 0; i < mesh.Vertices.Length; i++)
            {
                VertexPositionNormalTexture vertex = mesh.Vertices[i];
                vertex.Position = (vertex.Position - offset) * scale;
                mesh.Vertices[i] = vertex;
            }
        }

        AlignVisualAxlesToPhysicsOrigin(meshes);
    }

    private static void AlignVisualAxlesToPhysicsOrigin(IReadOnlyList<ImportedMesh> meshes)
    {
        if (!TryCalculateNamedWheelAxleCenterZ(meshes, frontAxle: true, out float frontAxleZ) ||
            !TryCalculateNamedWheelAxleCenterZ(meshes, frontAxle: false, out float rearAxleZ))
        {
            return;
        }

        float visualAxleMidpointZ = (frontAxleZ + rearAxleZ) * 0.5f;
        float targetFrontAxleZ = TargetWheelbaseMeters * (1f - TargetFrontWeightDistribution);
        float targetRearAxleZ = -TargetWheelbaseMeters * TargetFrontWeightDistribution;
        float targetAxleMidpointZ = (targetFrontAxleZ + targetRearAxleZ) * 0.5f;
        float correctionZ = targetAxleMidpointZ - visualAxleMidpointZ;
        if (MathF.Abs(correctionZ) <= 0.001f)
        {
            return;
        }

        Vector3 correction = new(0f, 0f, correctionZ);
        foreach (ImportedMesh mesh in meshes)
        {
            for (int i = 0; i < mesh.Vertices.Length; i++)
            {
                VertexPositionNormalTexture vertex = mesh.Vertices[i];
                vertex.Position += correction;
                mesh.Vertices[i] = vertex;
            }
        }
    }

    private static bool TryCalculateNamedWheelAxleCenterZ(
        IReadOnlyList<ImportedMesh> meshes,
        bool frontAxle,
        out float centerZ)
    {
        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;
        bool found = false;
        foreach (ImportedMesh mesh in meshes)
        {
            if (!IsNamedWheelForAxle(mesh.Name, frontAxle))
            {
                continue;
            }

            foreach (VertexPositionNormalTexture vertex in mesh.Vertices)
            {
                minZ = MathF.Min(minZ, vertex.Position.Z);
                maxZ = MathF.Max(maxZ, vertex.Position.Z);
                found = true;
            }
        }

        centerZ = found ? (minZ + maxZ) * 0.5f : 0f;
        return found;
    }

    private static bool IsNamedWheelForAxle(string name, bool frontAxle)
    {
        string normalized = NormalizeMaterialName(name);
        return frontAxle
            ? ContainsAny(normalized, "lf tyre", "fr tyre", "lf tire", "fr tire", "lf rim", "fr rim", "front tyre", "front tire", "front wheel")
            : ContainsAny(normalized, "lr tyre", "rr tyre", "lr tire", "rr tire", "lr rim", "rr rim", "rear tyre", "rear tire", "rear wheel");
    }

    private static float CalculateFitScale(MeshBounds bounds)
    {
        Vector3 size = bounds.Size;
        float lengthExtent = size.Z;
        if (lengthExtent <= MinimumUsableExtentMeters)
        {
            return 1f;
        }

        return TargetBodyLengthMeters / lengthExtent;
    }

    private static MeshBounds CalculateBounds(IReadOnlyList<ImportedMesh> meshes)
    {
        Vector3 min = new(float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity);
        foreach (ImportedMesh mesh in meshes)
        {
            foreach (VertexPositionNormalTexture vertex in mesh.Vertices)
            {
                min = Vector3.Min(min, vertex.Position);
                max = Vector3.Max(max, vertex.Position);
            }
        }

        return new MeshBounds(min, max);
    }

    private static string FormatBounds(MeshBounds bounds)
    {
        Vector3 size = bounds.Size;
        return $"{size.X:0.###}x{size.Y:0.###}x{size.Z:0.###}m";
    }

    private static string CombineMaterialNames(params string[] names)
    {
        return string.Join(' ', names);
    }

    private static VehicleMaterial ResolveVehicleMaterial(
        string combinedName,
        Vector3? exportedDiffuseColor,
        float? exportedOpacity,
        Vector3? exportedEmissiveColor,
        ImportedMaterialKind? legacyMaterialKind)
    {
        VehicleMaterialCategory category = ResolveVehicleMaterialCategory(combinedName, legacyMaterialKind);
        Vector3? baseColor = ResolveVehicleMaterialBaseColor(category, combinedName, exportedDiffuseColor);
        VehicleMaterial material = VehicleMaterial.CreateDefault(category, baseColor);

        if (exportedOpacity is float opacity && opacity < 0.995f)
        {
            material = material with { Opacity = MathHelper.Clamp(opacity, 0f, 1f) };
        }

        if (exportedEmissiveColor is Vector3 emissiveColor && emissiveColor.LengthSquared() > 0.0001f)
        {
            float strength = MathHelper.Clamp(
                MathF.Max(emissiveColor.X, MathF.Max(emissiveColor.Y, emissiveColor.Z)),
                material.EmissiveStrength,
                1f);
            material = material with
            {
                EmissiveColor = VehicleMaterial.ClampColor(emissiveColor / MathF.Max(strength, 0.001f)),
                EmissiveStrength = strength
            };
        }

        if (category == VehicleMaterialCategory.EmissiveLight)
        {
            material = ConfigureEmissiveLightMaterial(material, combinedName);
        }

        return material;
    }

    private static VehicleMaterialCategory ResolveVehicleMaterialCategory(
        string combinedName,
        ImportedMaterialKind? legacyMaterialKind)
    {
        string normalizedName = NormalizeMaterialName(combinedName);
        if (IsWheelTyreName(normalizedName))
        {
            return VehicleMaterialCategory.TyreRubber;
        }

        if (IsWheelRimName(normalizedName))
        {
            return VehicleMaterialCategory.WheelPaintOrMetal;
        }

        if (ContainsAny(normalizedName, "brembo", "brake", "caliper", "disc", "rotor"))
        {
            return VehicleMaterialCategory.Brake;
        }

        if (ContainsAny(normalizedName, "exhaust", "egz", "muffler", "tailpipe"))
        {
            return VehicleMaterialCategory.ExhaustMetal;
        }

        if (ContainsAny(normalizedName, "farkrom", "farref", "reflector", "chrome", "krom"))
        {
            return VehicleMaterialCategory.ExhaustMetal;
        }

        if (ContainsAny(normalizedName, "interior", "interiror", "seat", "dash", "steering"))
        {
            return VehicleMaterialCategory.Interior;
        }

        if (ContainsAny(normalizedName, "lightlenses geri", "reverse lens", "clear tail"))
        {
            return VehicleMaterialCategory.ClearTailLens;
        }

        if (ContainsAny(normalizedName, "tailstop", "taillight", "tail light", "rear light", "lightlenses tail"))
        {
            return VehicleMaterialCategory.TaillightLens;
        }

        if (ContainsAny(normalizedName, "lightlenses", "headlight lens", "headlight", "far", "sinyal", "indicator lens"))
        {
            return VehicleMaterialCategory.HeadlightLens;
        }

        if (ContainsAny(normalizedName, "lights 09"))
        {
            return VehicleMaterialCategory.BlackPlastic;
        }

        if (ContainsAny(normalizedName, "rearlights", "rear lights", "lights", "bulb", "emitter"))
        {
            return VehicleMaterialCategory.EmissiveLight;
        }

        if (ContainsAny(normalizedName, "windowtrim", "window trim", "trim", "kenar", "mirror", "plate", "arma"))
        {
            return VehicleMaterialCategory.Trim;
        }

        if (ContainsAny(normalizedName, "glass", "window", "windows", "windscreen", "windshield"))
        {
            return VehicleMaterialCategory.Glass;
        }

        if (ContainsAny(normalizedName, "shell inner", "inner", "black plastic", "plastic", "grille", "grill"))
        {
            return VehicleMaterialCategory.BlackPlastic;
        }

        return legacyMaterialKind switch
        {
            ImportedMaterialKind.Glass => VehicleMaterialCategory.Glass,
            ImportedMaterialKind.Tire => VehicleMaterialCategory.TyreRubber,
            ImportedMaterialKind.Light => VehicleMaterialCategory.EmissiveLight,
            ImportedMaterialKind.Interior => VehicleMaterialCategory.Interior,
            _ => VehicleMaterialCategory.Paint
        };
    }

    private static Vector3? ResolveVehicleMaterialBaseColor(
        VehicleMaterialCategory category,
        string combinedName,
        Vector3? exportedDiffuseColor)
    {
        string normalizedName = NormalizeMaterialName(combinedName);
        if (category == VehicleMaterialCategory.HeadlightLens &&
            ContainsAny(normalizedName, "sinyal", "indicator"))
        {
            return new Vector3(0.95f, 0.46f, 0.03f);
        }

        if (category == VehicleMaterialCategory.Paint &&
            IsOrangeBiasedYellowPaint(exportedDiffuseColor))
        {
            return HondaPaintColors.SunlightYellow.BaseColor;
        }

        if (category == VehicleMaterialCategory.ClearTailLens &&
            ContainsAny(normalizedName, "geri", "reverse"))
        {
            return VehicleMaterial.CreateDefault(category).BaseColor;
        }

        if (exportedDiffuseColor is not Vector3 exported || IsNearBlack(exported))
        {
            return null;
        }

        return category switch
        {
            VehicleMaterialCategory.TyreRubber => null,
            VehicleMaterialCategory.BlackPlastic => null,
            VehicleMaterialCategory.HeadlightLens => VehicleMaterial.CreateDefault(category).BaseColor,
            VehicleMaterialCategory.TaillightLens => VehicleMaterial.CreateDefault(category).BaseColor,
            VehicleMaterialCategory.ClearTailLens => VehicleMaterial.CreateDefault(category).BaseColor,
            VehicleMaterialCategory.ExhaustMetal when ContainsAny(normalizedName, "farkrom", "farref", "reflector", "chrome", "krom") => new Vector3(0.92f, 0.90f, 0.86f),
            VehicleMaterialCategory.Interior when ContainsAny(normalizedName, "seat") => exported,
            VehicleMaterialCategory.Interior => null,
            _ => exported
        };
    }

    private static VehicleMaterial ConfigureEmissiveLightMaterial(VehicleMaterial material, string combinedName)
    {
        string normalizedName = NormalizeMaterialName(combinedName);
        if (ContainsAny(normalizedName, "rear", "tail", "brake", "stop"))
        {
            return material with
            {
                BaseColor = new Vector3(0.78f, 0.03f, 0.02f),
                EmissiveColor = new Vector3(1.0f, 0.04f, 0.02f),
                EmissiveStrength = MathF.Max(material.EmissiveStrength, 0.34f)
            };
        }

        if (ContainsAny(normalizedName, "geri", "reverse"))
        {
            return material with
            {
                BaseColor = new Vector3(0.86f, 0.84f, 0.76f),
                EmissiveColor = new Vector3(0.92f, 0.88f, 0.72f),
                EmissiveStrength = MathF.Max(material.EmissiveStrength, 0.22f)
            };
        }

        return material with
        {
            BaseColor = new Vector3(1.0f, 0.88f, 0.58f),
            EmissiveColor = new Vector3(1.0f, 0.86f, 0.52f),
            EmissiveStrength = MathF.Max(material.EmissiveStrength, 0.30f)
        };
    }

    private static ImportedMaterialKind ToImportedMaterialKind(VehicleMaterialCategory category)
    {
        return category switch
        {
            VehicleMaterialCategory.Glass => ImportedMaterialKind.Glass,
            VehicleMaterialCategory.TyreRubber => ImportedMaterialKind.Tire,
            VehicleMaterialCategory.HeadlightLens => ImportedMaterialKind.Glass,
            VehicleMaterialCategory.TaillightLens => ImportedMaterialKind.Glass,
            VehicleMaterialCategory.ClearTailLens => ImportedMaterialKind.Glass,
            VehicleMaterialCategory.EmissiveLight => ImportedMaterialKind.Light,
            VehicleMaterialCategory.Interior => ImportedMaterialKind.Interior,
            _ => ImportedMaterialKind.Body
        };
    }

    private static bool IsWheelComponentName(string combinedName)
    {
        string normalizedName = NormalizeMaterialName(combinedName);
        return IsWheelRimName(normalizedName) || IsWheelTyreName(normalizedName);
    }

    private static bool IsWheelRimName(string normalizedName)
    {
        if (ContainsAny(normalizedName, "steering"))
        {
            return false;
        }

        return ContainsAny(
            normalizedName,
            "fr rim", "rf rim", "front right rim",
            "lf rim", "fl rim", "front left rim",
            "lr rim", "rl rim", "rear left rim",
            "rr rim", "rear right rim");
    }

    private static bool IsWheelTyreName(string normalizedName)
    {
        if (ContainsAny(normalizedName, "steering"))
        {
            return false;
        }

        return ContainsAny(
            normalizedName,
            "fr tyre", "fr tire", "rf tyre", "rf tire", "front right tyre", "front right tire",
            "lf tyre", "lf tire", "fl tyre", "fl tire", "front left tyre", "front left tire",
            "lr tyre", "lr tire", "rl tyre", "rl tire", "rear left tyre", "rear left tire",
            "rr tyre", "rr tire", "rear right tyre", "rear right tire");
    }

    private static string NormalizeMaterialName(string name)
    {
        return name
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Replace('.', ' ')
            .ToLowerInvariant();
    }

    private static bool IsNearBlack(Vector3 color)
    {
        return color.X * 0.2126f + color.Y * 0.7152f + color.Z * 0.0722f < 0.035f;
    }

    private static bool IsOrangeBiasedYellowPaint(Vector3? color)
    {
        if (color is not Vector3 value)
        {
            return false;
        }

        return value.X >= 0.62f &&
               value.Y >= 0.28f &&
               value.Y <= 0.66f &&
               value.Z <= 0.12f &&
               value.X - value.Y >= 0.24f;
    }

    private static (Texture2D Texture, Vector3 DiffuseColor) ResolveMaterial(
        GraphicsDevice graphicsDevice,
        ImportedMesh mesh,
        GeneratedTextures textures,
        string modelDirectory,
        Dictionary<string, Texture2D> loadedTextures)
    {
        if (!string.IsNullOrWhiteSpace(mesh.TexturePath) &&
            TryLoadTexture(graphicsDevice, modelDirectory, mesh.TexturePath, loadedTextures, out Texture2D? texture))
        {
            return (texture!, Vector3.One);
        }

        if (mesh.VehicleMaterial.Category == VehicleMaterialCategory.TaillightLens)
        {
            return (textures.TaillightRedLens, Vector3.One);
        }

        if (mesh.VehicleMaterial.Category == VehicleMaterialCategory.ClearTailLens)
        {
            return (textures.TaillightClearLens, Vector3.One);
        }

        return (textures.White, mesh.DiffuseColor);
    }

    private static bool TryLoadTexture(
        GraphicsDevice graphicsDevice,
        string modelDirectory,
        string texturePath,
        Dictionary<string, Texture2D> loadedTextures,
        out Texture2D? texture)
    {
        texture = null;
        string? resolvedPath = ResolveTexturePath(modelDirectory, texturePath);
        if (resolvedPath is null || !SupportedTextureExtensions.Contains(Path.GetExtension(resolvedPath)))
        {
            return false;
        }

        if (loadedTextures.TryGetValue(resolvedPath, out texture))
        {
            return true;
        }

        try
        {
            using FileStream stream = File.OpenRead(resolvedPath);
            texture = Texture2D.FromStream(graphicsDevice, stream, DefaultColorProcessors.PremultiplyAlpha);
            loadedTextures[resolvedPath] = texture;
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or NotSupportedException)
        {
            Console.Error.WriteLine($"Could not load FBX texture '{resolvedPath}': {exception.Message}");
            return false;
        }
    }

    private static string? ResolveTexturePath(string modelDirectory, string texturePath)
    {
        string cleanedPath = texturePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(cleanedPath) && File.Exists(cleanedPath))
        {
            return Path.GetFullPath(cleanedPath);
        }

        string candidate = Path.GetFullPath(Path.Combine(modelDirectory, cleanedPath));
        if (File.Exists(candidate))
        {
            return candidate;
        }

        candidate = Path.GetFullPath(Path.Combine(modelDirectory, Path.GetFileName(cleanedPath)));
        return File.Exists(candidate) ? candidate : null;
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        foreach (string needle in needles)
        {
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMeshClass(object value)
    {
        return value is string text && text.Equals("Mesh", StringComparison.Ordinal);
    }

    private static string CleanFbxName(string name)
    {
        int separator = name.IndexOf('\0', StringComparison.Ordinal);
        return separator >= 0 ? name[..separator] : name;
    }

    private static string? FirstUsablePath(params string?[] paths)
    {
        foreach (string? path in paths)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }
        }

        return null;
    }

    private static string? ReadNamedChildString(FbxNode node, string childName)
    {
        return node.Child(childName)?.Properties.FirstOrDefault() as string;
    }

    private static Vector3 ReadVector3Property(FbxNode property, int startIndex)
    {
        return new Vector3(
            Convert.ToSingle(property.Properties[startIndex]),
            Convert.ToSingle(property.Properties[startIndex + 1]),
            Convert.ToSingle(property.Properties[startIndex + 2]));
    }

    private static Vector3 ClampColor(Vector3 color)
    {
        return new Vector3(Clamp01(color.X), Clamp01(color.Y), Clamp01(color.Z));
    }

    private static float Clamp01(float value)
    {
        return MathHelper.Clamp(value, 0f, 1f);
    }

    private static bool TryLoadMeshCache(string cachePath, string sourcePath, out List<ImportedMesh> meshes)
    {
        meshes = [];
        if (!File.Exists(cachePath) ||
            File.GetLastWriteTimeUtc(cachePath) < File.GetLastWriteTimeUtc(sourcePath))
        {
            return false;
        }

        try
        {
            using BinaryReader reader = new(File.OpenRead(cachePath), Encoding.UTF8);
            if (reader.ReadString() != MeshCacheMagic ||
                reader.ReadInt32() != MeshCacheVersion)
            {
                return false;
            }

            int meshCount = reader.ReadInt32();
            for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
            {
                string name = reader.ReadString();
                bool isWheelMesh = reader.ReadBoolean();
                ImportedMaterialKind materialKind = (ImportedMaterialKind)reader.ReadInt32();
                Vector3 diffuseColor = ReadVector3(reader);
                float alpha = reader.ReadSingle();
                Vector3 specularColor = ReadVector3(reader);
                float specularPower = reader.ReadSingle();
                Vector3 emissiveColor = ReadVector3(reader);
                string? texturePath = reader.ReadBoolean() ? reader.ReadString() : null;
                int vertexCount = reader.ReadInt32();
                VertexPositionNormalTexture[] vertices = new VertexPositionNormalTexture[vertexCount];
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 position = ReadVector3(reader);
                    Vector3 normal = ReadVector3(reader);
                    Vector2 textureCoordinate = ReadVector2(reader);
                    vertices[i] = new VertexPositionNormalTexture(position, normal, textureCoordinate);
                }

                int indexCount = reader.ReadInt32();
                int[] indices = new int[indexCount];
                for (int i = 0; i < indices.Length; i++)
                {
                    indices[i] = reader.ReadInt32();
                }

                VehicleMaterial vehicleMaterial = ResolveVehicleMaterial(
                    name,
                    diffuseColor,
                    alpha,
                    emissiveColor,
                    legacyMaterialKind: materialKind);
                meshes.Add(new ImportedMesh(
                    name,
                    vertices,
                    indices,
                    materialKind,
                    diffuseColor,
                    alpha,
                    specularColor,
                    specularPower,
                    emissiveColor,
                    texturePath,
                    vehicleMaterial,
                    isWheelMesh || IsWheelComponentName(name)));
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or EndOfStreamException)
        {
            Console.Error.WriteLine($"Could not load mesh cache '{cachePath}': {exception.Message}");
            meshes = [];
            return false;
        }
    }

    private static void TrySaveMeshCache(string cachePath, IReadOnlyList<ImportedMesh> meshes)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath) ?? ".");
            using BinaryWriter writer = new(File.Create(cachePath), Encoding.UTF8);
            writer.Write(MeshCacheMagic);
            writer.Write(MeshCacheVersion);
            writer.Write(meshes.Count);
            foreach (ImportedMesh mesh in meshes)
            {
                writer.Write(mesh.Name);
                writer.Write(mesh.IsWheelMesh);
                writer.Write((int)mesh.MaterialKind);
                WriteVector3(writer, mesh.DiffuseColor);
                writer.Write(mesh.Alpha);
                WriteVector3(writer, mesh.SpecularColor);
                writer.Write(mesh.SpecularPower);
                WriteVector3(writer, mesh.EmissiveColor);
                writer.Write(mesh.TexturePath is not null);
                if (mesh.TexturePath is not null)
                {
                    writer.Write(mesh.TexturePath);
                }

                writer.Write(mesh.Vertices.Length);
                foreach (VertexPositionNormalTexture vertex in mesh.Vertices)
                {
                    WriteVector3(writer, vertex.Position);
                    WriteVector3(writer, vertex.Normal);
                    WriteVector2(writer, vertex.TextureCoordinate);
                }

                writer.Write(mesh.Indices.Length);
                foreach (int index in mesh.Indices)
                {
                    writer.Write(index);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Console.Error.WriteLine($"Could not save mesh cache '{cachePath}': {exception.Message}");
        }
    }

    private static Vector3 ReadVector3(BinaryReader reader)
    {
        return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }

    private static Vector2 ReadVector2(BinaryReader reader)
    {
        return new Vector2(reader.ReadSingle(), reader.ReadSingle());
    }

    private static void WriteVector3(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }

    private static void WriteVector2(BinaryWriter writer, Vector2 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
    }

    private sealed record FbxModel(
        long Id,
        string Name,
        bool IsMesh,
        Vector3 Translation,
        Vector3 RotationDegrees,
        Vector3 Scaling,
        long? ParentId);

    private sealed record FbxMaterial(
        long Id,
        string Name,
        Vector3 DiffuseColor,
        float Opacity,
        Vector3 SpecularColor,
        float SpecularPower,
        Vector3 EmissiveColor,
        string? TexturePath);

    private sealed record FbxTexture(
        long Id,
        string Name,
        string? FileName,
        string? RelativeFileName);

    private sealed record FbxVideo(
        long Id,
        string Name,
        string? FileName,
        string? RelativeFileName);

    private readonly record struct FbxConnection(
        string Relationship,
        long ChildId,
        long ParentId,
        string PropertyName);

    private sealed class ImportedMeshAccumulator
    {
        public ImportedMeshAccumulator(
            string name,
            ImportedMaterialKind materialKind,
            Vector3 diffuseColor,
            float alpha,
            Vector3 specularColor,
            float specularPower,
            Vector3 emissiveColor,
            VehicleMaterial vehicleMaterial,
            string? texturePath)
        {
            Name = name;
            MaterialKind = materialKind;
            DiffuseColor = diffuseColor;
            Alpha = alpha;
            SpecularColor = specularColor;
            SpecularPower = specularPower;
            EmissiveColor = emissiveColor;
            VehicleMaterial = vehicleMaterial;
            TexturePath = texturePath;
        }

        public string Name { get; }

        public ImportedMaterialKind MaterialKind { get; }

        public Vector3 DiffuseColor { get; }

        public float Alpha { get; }

        public Vector3 SpecularColor { get; }

        public float SpecularPower { get; }

        public Vector3 EmissiveColor { get; }

        public VehicleMaterial VehicleMaterial { get; }

        public string? TexturePath { get; }

        public List<VertexPositionNormalTexture> Vertices { get; } = [];

        public List<int> Indices { get; } = [];
    }

    private sealed record ImportedMesh(
        string Name,
        VertexPositionNormalTexture[] Vertices,
        int[] Indices,
        ImportedMaterialKind MaterialKind,
        Vector3 DiffuseColor,
        float Alpha,
        Vector3 SpecularColor,
        float SpecularPower,
        Vector3 EmissiveColor,
        string? TexturePath,
        VehicleMaterial VehicleMaterial,
        bool IsWheelMesh);

    private enum ImportedMaterialKind
    {
        Body,
        Glass,
        Tire,
        Light,
        Interior
    }

    private readonly record struct PolygonVertex(int ControlPointIndex, int PolygonVertexIndex);

    private sealed class GeometryNormalLayer
    {
        private readonly Vector3[] _normals;
        private readonly int[]? _indices;
        private readonly string _mapping;
        private readonly string _reference;

        public GeometryNormalLayer(Vector3[] normals, int[]? indices, string mapping, string reference)
        {
            _normals = normals;
            _indices = indices;
            _mapping = mapping;
            _reference = reference;
        }

        public Vector3 GetNormal(PolygonVertex vertex, Vector3 fallback)
        {
            int sourceIndex = _mapping switch
            {
                "ByVertice" => vertex.ControlPointIndex,
                "ByPolygonVertex" => vertex.PolygonVertexIndex,
                _ => vertex.PolygonVertexIndex
            };
            int index = ResolveReferenceIndex(sourceIndex, _indices, _reference);
            return index >= 0 && index < _normals.Length ? _normals[index] : fallback;
        }
    }

    private sealed class GeometryUvLayer
    {
        private readonly Vector2[] _uvs;
        private readonly int[]? _indices;
        private readonly string _mapping;
        private readonly string _reference;

        public GeometryUvLayer(Vector2[] uvs, int[]? indices, string mapping, string reference)
        {
            _uvs = uvs;
            _indices = indices;
            _mapping = mapping;
            _reference = reference;
        }

        public Vector2 GetUv(PolygonVertex vertex, Vector3 fallbackPosition)
        {
            int sourceIndex = _mapping switch
            {
                "ByVertice" => vertex.ControlPointIndex,
                "ByPolygonVertex" => vertex.PolygonVertexIndex,
                _ => vertex.PolygonVertexIndex
            };
            int index = ResolveReferenceIndex(sourceIndex, _indices, _reference);
            return index >= 0 && index < _uvs.Length ? _uvs[index] : CreateFallbackUv(fallbackPosition);
        }
    }

    private sealed class GeometryMaterialLayer
    {
        private readonly int[] _materialSlots;
        private readonly string _mapping;
        private readonly string _reference;

        public GeometryMaterialLayer(int[] materialSlots, string mapping, string reference)
        {
            _materialSlots = materialSlots;
            _mapping = mapping;
            _reference = reference;
        }

        public int GetSlot(int polygonIndex, int polygonVertexIndex)
        {
            int sourceIndex = _mapping switch
            {
                "AllSame" => 0,
                "ByPolygon" => polygonIndex,
                "ByPolygonVertex" => polygonVertexIndex,
                _ => 0
            };
            int index = ResolveReferenceIndex(sourceIndex, null, _reference);
            return index >= 0 && index < _materialSlots.Length
                ? Math.Max(0, _materialSlots[index])
                : 0;
        }
    }

    private static int ResolveReferenceIndex(int sourceIndex, int[]? indices, string reference)
    {
        if (reference.Equals("IndexToDirect", StringComparison.OrdinalIgnoreCase) && indices is not null)
        {
            return sourceIndex >= 0 && sourceIndex < indices.Length ? indices[sourceIndex] : -1;
        }

        return sourceIndex;
    }

    private readonly record struct MeshBounds(Vector3 Min, Vector3 Max)
    {
        public Vector3 Size => Max - Min;
    }

    private sealed class FbxDocument
    {
        private readonly List<FbxNode> _roots;

        private FbxDocument(List<FbxNode> roots)
        {
            _roots = roots;
        }

        public static FbxDocument Load(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            using BinaryReader reader = new(new MemoryStream(data), Encoding.UTF8);
            string header = Encoding.ASCII.GetString(reader.ReadBytes(23));
            if (header != "Kaydara FBX Binary  \0\u001a\0")
            {
                throw new InvalidDataException("Only binary FBX files are supported.");
            }

            int version = reader.ReadInt32();
            if (version >= 7500)
            {
                throw new NotSupportedException($"FBX version {version} uses 64-bit node offsets.");
            }

            List<FbxNode> roots = [];
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                FbxNode? node = ReadNode(reader);
                if (node is null)
                {
                    break;
                }

                roots.Add(node);
            }

            return new FbxDocument(roots);
        }

        public FbxNode RequiredRoot(string name)
        {
            return _roots.FirstOrDefault(root => root.Name == name)
                ?? throw new InvalidDataException($"FBX missing required '{name}' section.");
        }

        public FbxNode? Root(string name)
        {
            return _roots.FirstOrDefault(root => root.Name == name);
        }

        private static FbxNode? ReadNode(BinaryReader reader)
        {
            if (reader.BaseStream.Position + 13 > reader.BaseStream.Length)
            {
                return null;
            }

            uint endOffset = reader.ReadUInt32();
            uint propertyCount = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            byte nameLength = reader.ReadByte();

            if (endOffset == 0 && propertyCount == 0 && nameLength == 0)
            {
                return null;
            }

            string name = Encoding.ASCII.GetString(reader.ReadBytes(nameLength));
            FbxNode node = new(name);
            for (int i = 0; i < propertyCount; i++)
            {
                node.Properties.Add(ReadProperty(reader));
            }

            while (reader.BaseStream.Position < endOffset - 13)
            {
                FbxNode? child = ReadNode(reader);
                if (child is null)
                {
                    break;
                }

                node.Children.Add(child);
            }

            reader.BaseStream.Position = endOffset;
            return node;
        }

        private static object ReadProperty(BinaryReader reader)
        {
            char type = reader.ReadChar();
            return type switch
            {
                'Y' => reader.ReadInt16(),
                'C' => reader.ReadByte() != 0,
                'I' => reader.ReadInt32(),
                'F' => reader.ReadSingle(),
                'D' => reader.ReadDouble(),
                'L' => reader.ReadInt64(),
                'S' => Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32())),
                'R' => reader.ReadBytes(reader.ReadInt32()),
                'f' => ReadArray(reader, binaryReader => binaryReader.ReadSingle()),
                'd' => ReadArray(reader, binaryReader => binaryReader.ReadDouble()),
                'i' => ReadArray(reader, binaryReader => binaryReader.ReadInt32()),
                'l' => ReadArray(reader, binaryReader => binaryReader.ReadInt64()),
                'b' => ReadArray(reader, binaryReader => binaryReader.ReadByte() != 0),
                _ => throw new InvalidDataException($"Unsupported FBX property type '{type}'.")
            };
        }

        private static T[] ReadArray<T>(BinaryReader reader, Func<BinaryReader, T> readValue)
        {
            int count = reader.ReadInt32();
            int encoding = reader.ReadInt32();
            int byteCount = reader.ReadInt32();
            byte[] bytes = reader.ReadBytes(byteCount);

            using Stream stream = encoding switch
            {
                0 => new MemoryStream(bytes),
                1 => new ZLibStream(new MemoryStream(bytes), CompressionMode.Decompress),
                _ => throw new InvalidDataException($"Unsupported FBX array encoding '{encoding}'.")
            };

            using BinaryReader arrayReader = new(stream, Encoding.UTF8);
            T[] values = new T[count];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = readValue(arrayReader);
            }

            return values;
        }
    }

    private sealed class FbxNode
    {
        public FbxNode(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public List<object> Properties { get; } = [];

        public List<FbxNode> Children { get; } = [];

        public FbxNode? Child(string name)
        {
            return Children.FirstOrDefault(child => child.Name == name);
        }
    }
}
