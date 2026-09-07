using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RType.Data;
using RType.Rendering;
using RType.World;

namespace RType.Core;

internal static class HighSpeedRingBlenderExporter
{
    private const string TrackPackageRoot = "Temp/TrackExport/HighSpeedRing/ProceduralSeed";
    private const string ExternalGltfName = "HighSpeedRing_TexturesExternal.gltf";
    private const string GlbName = "HighSpeedRing.glb";
    private const string ExternalBinName = "HighSpeedRing_TexturesExternal.bin";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static void Export(
        GraphicsDevice graphicsDevice,
        GeneratedTextures textures,
        TrackDefinitionFile sourceFile,
        TrackScene track,
        IReadOnlyList<StaticMesh> backdropMeshes)
    {
        string packageRoot = Path.GetFullPath(TrackPackageRoot);
        string editableDirectory = Path.Combine(packageRoot, "Editable");
        string textureDirectory = Path.Combine(packageRoot, "Textures");
        string sourceDirectory = Path.Combine(packageRoot, "Source");
        string documentationDirectory = Path.Combine(packageRoot, "Documentation");
        Directory.CreateDirectory(editableDirectory);
        Directory.CreateDirectory(textureDirectory);
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(documentationDirectory);
        CreateAssetCategoryFolders(packageRoot);
        CopySourceFiles(sourceFile.SourcePath, sourceDirectory);

        List<ExportMeshInput> inputs = BuildExportInputs(track.Meshes, backdropMeshes);
        Dictionary<Texture2D, TextureExportInfo> textureInfos = BuildTextureInfos(textures);
        List<TextureExportInfo> usedTextures = ExportUsedTextures(inputs, textureInfos, textureDirectory);
        ExportScene scene = BuildScene(inputs, usedTextures);

        string externalGltfPath = Path.Combine(editableDirectory, ExternalGltfName);
        string externalBinPath = Path.Combine(editableDirectory, ExternalBinName);
        WriteGltf(scene, externalGltfPath, externalBinPath, embedTextures: false);

        string glbPath = Path.Combine(editableDirectory, GlbName);
        WriteGlb(scene, glbPath);

        ExportManifest manifest = BuildManifest(sourceFile, track, backdropMeshes, scene, usedTextures, packageRoot);
        string manifestJsonPath = Path.Combine(documentationDirectory, "HighSpeedRing_ExportManifest.json");
        File.WriteAllText(manifestJsonPath, JsonSerializer.Serialize(manifest, JsonOptions));
        WriteMarkdownManifest(Path.Combine(documentationDirectory, "HighSpeedRing_ExportManifest.md"), manifest);

        ValidationReport validation = Validate(scene, externalGltfPath, externalBinPath, glbPath);
        manifest = manifest with { Validation = validation };
        File.WriteAllText(manifestJsonPath, JsonSerializer.Serialize(manifest, JsonOptions));
        WriteMarkdownManifest(Path.Combine(documentationDirectory, "HighSpeedRing_ExportManifest.md"), manifest);

        TryCreateBlenderReview(editableDirectory, externalGltfPath, manifestJsonPath, manifest);

        Console.WriteLine("High Speed Ring Blender export complete.");
        Console.WriteLine($"  External glTF: {externalGltfPath}");
        Console.WriteLine($"  GLB: {glbPath}");
        Console.WriteLine($"  Manifest: {manifestJsonPath}");
        Console.WriteLine($"  Runtime StaticMeshes: {manifest.RuntimeScene.StaticMeshCount}");
        Console.WriteLine($"  Export nodes: {manifest.ExportedScene.NodeCount}");
        Console.WriteLine($"  Unique glTF meshes: {manifest.ExportedScene.UniqueMeshCount}");
        Console.WriteLine($"  Materials: {manifest.ExportedScene.MaterialCount}");
        Console.WriteLine($"  Textures: {manifest.ExportedScene.TextureCount}");
    }

    private static List<ExportMeshInput> BuildExportInputs(
        IReadOnlyList<StaticMesh> trackMeshes,
        IReadOnlyList<StaticMesh> backdropMeshes)
    {
        List<ExportMeshInput> inputs = [];
        Dictionary<string, int> semanticCounts = new(StringComparer.OrdinalIgnoreCase);
        foreach (StaticMesh mesh in trackMeshes)
        {
            inputs.Add(CreateInput(mesh, isBackdrop: false, semanticCounts));
        }

        foreach (StaticMesh mesh in backdropMeshes)
        {
            inputs.Add(CreateInput(mesh, isBackdrop: true, semanticCounts));
        }

        return inputs;
    }

    private static ExportMeshInput CreateInput(
        StaticMesh mesh,
        bool isBackdrop,
        Dictionary<string, int> semanticCounts)
    {
        string category = ClassifyCategory(mesh.Name, isBackdrop);
        string semantic = ClassifySemanticAssetType(mesh.Name, category);
        string generatorType = ClassifyGeneratorType(mesh.Name, category);
        string stableBase = $"{category}|{semantic}|{mesh.Name}|{BoundsToken(mesh.Bounds)}";
        string stableHash = ShortHash(stableBase);
        int index = NextIndex(semanticCounts, $"{category}|{semantic}");
        string nodeName = $"{semantic}_{index:0000}";
        string exportId = $"high_speed_ring/{category}/{semantic}/{stableHash}";
        bool runtimeShadow = category == "RuntimeShadows";

        return new ExportMeshInput(
            mesh,
            nodeName,
            exportId,
            category,
            generatorType,
            semantic,
            index,
            isBackdrop,
            runtimeShadow);
    }

    private static ExportScene BuildScene(List<ExportMeshInput> inputs, List<TextureExportInfo> usedTextures)
    {
        ExportScene scene = new();
        Dictionary<Texture2D, int> textureIndices = new(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < usedTextures.Count; i++)
        {
            textureIndices[usedTextures[i].Texture] = i;
            scene.Textures.Add(usedTextures[i]);
        }

        Dictionary<string, int> materialLookup = new(StringComparer.Ordinal);
        Dictionary<string, int> meshLookup = new(StringComparer.Ordinal);

        foreach (ExportMeshInput input in inputs)
        {
            TextureExportInfo texture = textureIndices.TryGetValue(input.Mesh.Texture, out int textureIndex)
                ? scene.Textures[textureIndex]
                : scene.Textures.First(info => ReferenceEquals(info.Texture, input.Mesh.Texture));

            string materialKey = MaterialKey(input.Mesh, texture);
            if (!materialLookup.TryGetValue(materialKey, out int materialIndex))
            {
                materialIndex = scene.Materials.Count;
                materialLookup[materialKey] = materialIndex;
                scene.Materials.Add(CreateMaterial(input.Mesh, texture, materialIndex));
            }

            Vector3 origin = Center(input.Mesh.Bounds);
            MeshGeometry geometry = CreateGeometry(input.Mesh, origin, materialIndex);
            string meshKey = GeometryKey(geometry);
            if (!meshLookup.TryGetValue(meshKey, out int meshIndex))
            {
                meshIndex = scene.Meshes.Count;
                meshLookup[meshKey] = meshIndex;
                scene.Meshes.Add(geometry);
            }

            scene.Nodes.Add(new ExportNode(
                input.NodeName,
                input.ExportId,
                input.Mesh.Name,
                input.Category,
                input.GeneratorType,
                input.SemanticAssetType,
                input.InstanceIndex,
                input.IsBackdrop,
                input.IsRuntimeShadow,
                meshIndex,
                origin));
        }

        return scene;
    }

    private static MeshGeometry CreateGeometry(StaticMesh mesh, Vector3 origin, int materialIndex)
    {
        VertexPositionNormalTexture[] vertices = mesh.Vertices.ToArray();
        float[] positions = new float[vertices.Length * 3];
        float[] normals = new float[vertices.Length * 3];
        float[] uvs = new float[vertices.Length * 2];
        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 local = vertices[i].Position - origin;
            positions[i * 3] = local.X;
            positions[i * 3 + 1] = local.Y;
            positions[i * 3 + 2] = local.Z;
            normals[i * 3] = vertices[i].Normal.X;
            normals[i * 3 + 1] = vertices[i].Normal.Y;
            normals[i * 3 + 2] = vertices[i].Normal.Z;
            uvs[i * 2] = vertices[i].TextureCoordinate.X;
            uvs[i * 2 + 1] = vertices[i].TextureCoordinate.Y;
            min = Vector3.Min(min, local);
            max = Vector3.Max(max, local);
        }

        return new MeshGeometry(
            $"mesh_{ShortHash(mesh.Name + BoundsToken(mesh.Bounds) + materialIndex.ToString(CultureInfo.InvariantCulture))}",
            positions,
            normals,
            uvs,
            mesh.Indices.ToArray(),
            min,
            max,
            materialIndex);
    }

    private static ExportMaterial CreateMaterial(StaticMesh mesh, TextureExportInfo texture, int index)
    {
        bool alphaTexture = texture.HasAlpha;
        bool blend = mesh.Alpha < 0.995f || alphaTexture || texture.SemanticName.Contains("Shadow", StringComparison.OrdinalIgnoreCase);
        return new ExportMaterial(
            $"mat_{SanitizeIdentifier(texture.SemanticName)}_{index:000}",
            texture,
            mesh.DiffuseColor,
            mesh.Alpha,
            mesh.EmissiveColor,
            ApproximateRoughness(mesh.SpecularPower, mesh.SpecularColor),
            0f,
            blend ? "BLEND" : "OPAQUE",
            true);
    }

    private static Dictionary<Texture2D, TextureExportInfo> BuildTextureInfos(GeneratedTextures textures)
    {
        Dictionary<Texture2D, TextureExportInfo> infos = new(ReferenceEqualityComparer.Instance);
        AddTexture(infos, textures.Road, "Road_Asphalt", generated: true);
        AddTexture(infos, textures.Grass, "Grass_A", generated: true);
        AddTexture(infos, textures.Curb, "Kerb_RedWhite", generated: true);
        AddTexture(infos, textures.White, "White_Solid", generated: true);
        AddTexture(infos, textures.CarRed, "Vehicle_Red", generated: true);
        AddTexture(infos, textures.CarGlass, "Vehicle_Glass", generated: true);
        AddTexture(infos, textures.Tire, "Tyre_Rubber", generated: true);
        AddTexture(infos, textures.DistantEarth, "DistantEarth_Backdrop", generated: true);
        AddTexture(infos, textures.Mountain, "Mountain_Backdrop", generated: true);
        AddTexture(infos, textures.TreeClump, "Tree_Clump_Low", generated: true);
        AddTexture(infos, textures.TreeClumpRound, "Tree_Clump_Round", generated: true);
        AddTexture(infos, textures.TreeClumpTall, "Tree_Clump_Tall", generated: true);
        AddTexture(infos, textures.ReferenceTreeBroad, "Tree_Reference_Broad", generated: true, "Assets/Tracks/Textures/Reference Material Trees/000-bigtree2.png");
        AddTexture(infos, textures.ReferenceTreePines, "Tree_Reference_Pines", generated: true, "Assets/Tracks/Textures/Reference Material Trees/011-pines.png");
        AddTexture(infos, textures.ReferenceTreeShrub, "Tree_Reference_Shrub", generated: true, "Assets/Tracks/Textures/Reference Material Trees/012-ShrubBranch.png");
        AddTexture(infos, textures.ReferenceTreeBroadAlt, "Tree_Reference_Broad_MirroredCool", generated: true, "Assets/Tracks/Textures/Reference Material Trees/000-bigtree2.png");
        AddTexture(infos, textures.ReferenceTreePinesAlt, "Tree_Reference_Pines_WarmDense", generated: true, "Assets/Tracks/Textures/Reference Material Trees/011-pines.png");
        AddTexture(infos, textures.ReferenceTreeShrubAlt, "Tree_Reference_Shrub_DarkLow", generated: true, "Assets/Tracks/Textures/Reference Material Trees/012-ShrubBranch.png");
        AddTexture(infos, textures.ChevronSign, "Chevron_A", generated: true);
        AddTexture(infos, textures.StartBoard, "StartFinish_Board", generated: true);
        AddTexture(infos, textures.BrakeMarkerSign, "BrakeMarker_100", generated: true);
        AddTexture(infos, textures.TaillightRedLens, "Taillight_RedLens", generated: true);
        AddTexture(infos, textures.TaillightClearLens, "Taillight_ClearLens", generated: true);
        AddTexture(infos, textures.Shadow, "RuntimeShadow_Blob", generated: true);
        return infos;
    }

    private static void AddTexture(
        Dictionary<Texture2D, TextureExportInfo> infos,
        Texture2D texture,
        string semanticName,
        bool generated,
        string sourcePath = "")
    {
        infos[texture] = new TextureExportInfo(
            texture,
            semanticName,
            $"{semanticName}.png",
            $"../Textures/{semanticName}.png",
            sourcePath,
            generated,
            false,
            string.Empty,
            texture.Width,
            texture.Height);
    }

    private static List<TextureExportInfo> ExportUsedTextures(
        List<ExportMeshInput> inputs,
        Dictionary<Texture2D, TextureExportInfo> knownTextures,
        string textureDirectory)
    {
        List<TextureExportInfo> used = [];
        HashSet<Texture2D> seen = new(ReferenceEqualityComparer.Instance);
        foreach (StaticMesh mesh in inputs.Select(input => input.Mesh))
        {
            if (!seen.Add(mesh.Texture))
            {
                continue;
            }

            TextureExportInfo? known = null;
            TextureExportInfo info = knownTextures.TryGetValue(mesh.Texture, out known)
                ? known
                : new TextureExportInfo(
                    mesh.Texture,
                    $"Texture_{used.Count:000}",
                    $"Texture_{used.Count:000}.png",
                    $"../Textures/Texture_{used.Count:000}.png",
                    string.Empty,
                    true,
                    false,
                    string.Empty,
                    mesh.Texture.Width,
                    mesh.Texture.Height);

            string path = Path.Combine(textureDirectory, info.FileName);
            using (FileStream stream = File.Create(path))
            {
                mesh.Texture.SaveAsPng(stream, mesh.Texture.Width, mesh.Texture.Height);
            }

            bool hasAlpha = TextureHasAlpha(mesh.Texture);
            string hash = FileHash(path);
            used.Add(info with { HasAlpha = hasAlpha, Sha256 = hash });
        }

        return used;
    }

    private static bool TextureHasAlpha(Texture2D texture)
    {
        Color[] data = new Color[texture.Width * texture.Height];
        texture.GetData(data);
        return data.Any(color => color.A < 250);
    }

    private static void WriteGltf(ExportScene scene, string gltfPath, string binPath, bool embedTextures)
    {
        BinaryWriterBuffer buffer = new();
        GltfDocumentParts parts = BuildGltfParts(scene, buffer, embedTextures, gltfPath);
        File.WriteAllBytes(binPath, buffer.ToArray());
        Dictionary<string, object?> root = BuildGltfJson(scene, parts, embedTextures, Path.GetFileName(binPath), buffer.Length);
        File.WriteAllText(gltfPath, JsonSerializer.Serialize(root, JsonOptions));
    }

    private static void WriteGlb(ExportScene scene, string glbPath)
    {
        BinaryWriterBuffer buffer = new();
        GltfDocumentParts parts = BuildGltfParts(scene, buffer, embedTextures: true, glbPath);
        Dictionary<string, object?> root = BuildGltfJson(scene, parts, embedTextures: true, string.Empty, buffer.Length);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(root, JsonOptions));
        byte[] paddedJson = Pad(jsonBytes, 0x20);
        byte[] binBytes = Pad(buffer.ToArray(), 0x00);

        using FileStream stream = File.Create(glbPath);
        using BinaryWriter writer = new(stream);
        writer.Write(0x46546C67);
        writer.Write(2);
        writer.Write(12 + 8 + paddedJson.Length + 8 + binBytes.Length);
        writer.Write(paddedJson.Length);
        writer.Write(0x4E4F534A);
        writer.Write(paddedJson);
        writer.Write(binBytes.Length);
        writer.Write(0x004E4942);
        writer.Write(binBytes);
    }

    private static GltfDocumentParts BuildGltfParts(
        ExportScene scene,
        BinaryWriterBuffer buffer,
        bool embedTextures,
        string documentPath)
    {
        List<BufferViewInfo> bufferViews = [];
        List<AccessorInfo> accessors = [];
        foreach (MeshGeometry mesh in scene.Meshes)
        {
            int positionBufferView = AddFloatBufferView(buffer, bufferViews, mesh.Positions);
            int normalBufferView = AddFloatBufferView(buffer, bufferViews, mesh.Normals);
            int uvBufferView = AddFloatBufferView(buffer, bufferViews, mesh.Uvs);
            int indexBufferView = AddIntBufferView(buffer, bufferViews, mesh.Indices);

            mesh.PositionAccessorIndex = accessors.Count;
            accessors.Add(AccessorInfo.Float(positionBufferView, mesh.Positions.Length / 3, "VEC3", mesh.Min, mesh.Max));
            mesh.NormalAccessorIndex = accessors.Count;
            accessors.Add(AccessorInfo.Float(normalBufferView, mesh.Normals.Length / 3, "VEC3", null, null));
            mesh.UvAccessorIndex = accessors.Count;
            accessors.Add(AccessorInfo.Float(uvBufferView, mesh.Uvs.Length / 2, "VEC2", null, null));
            mesh.IndexAccessorIndex = accessors.Count;
            accessors.Add(AccessorInfo.UnsignedInt(indexBufferView, mesh.Indices.Length, "SCALAR"));
        }

        if (embedTextures)
        {
            foreach (TextureExportInfo texture in scene.Textures)
            {
                string texturePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(documentPath)!, texture.RelativeUri.Replace("../", "../").Replace('/', Path.DirectorySeparatorChar)));
                byte[] imageBytes = File.ReadAllBytes(texturePath);
                buffer.Align4();
                texture.BufferViewIndex = bufferViews.Count;
                int offset = buffer.Length;
                buffer.Write(imageBytes);
                bufferViews.Add(new BufferViewInfo(offset, imageBytes.Length, null));
            }
        }

        return new GltfDocumentParts(bufferViews, accessors);
    }

    private static int AddFloatBufferView(BinaryWriterBuffer buffer, List<BufferViewInfo> bufferViews, float[] values)
    {
        buffer.Align4();
        int offset = buffer.Length;
        foreach (float value in values)
        {
            buffer.Write(value);
        }

        bufferViews.Add(new BufferViewInfo(offset, buffer.Length - offset, 34962));
        return bufferViews.Count - 1;
    }

    private static int AddIntBufferView(BinaryWriterBuffer buffer, List<BufferViewInfo> bufferViews, int[] values)
    {
        buffer.Align4();
        int offset = buffer.Length;
        foreach (int value in values)
        {
            buffer.Write((uint)value);
        }

        bufferViews.Add(new BufferViewInfo(offset, buffer.Length - offset, 34963));
        return bufferViews.Count - 1;
    }

    private static Dictionary<string, object?> BuildGltfJson(
        ExportScene scene,
        GltfDocumentParts parts,
        bool embedTextures,
        string binFileName,
        int binaryLength)
    {
        List<Dictionary<string, object?>> nodes = [];
        int rootNodeIndex = 0;
        nodes.Add(new Dictionary<string, object?>
        {
            ["name"] = "HighSpeedRing",
            ["children"] = Enumerable.Range(1, scene.Nodes.Count).ToArray(),
            ["extras"] = new Dictionary<string, object?>
            {
                ["exportId"] = "high_speed_ring/root",
                ["category"] = "Root",
                ["generatorType"] = "TrackScene",
                ["semanticAssetType"] = "HighSpeedRing"
            }
        });

        foreach (ExportNode node in scene.Nodes)
        {
            nodes.Add(new Dictionary<string, object?>
            {
                ["name"] = node.Name,
                ["mesh"] = node.MeshIndex,
                ["translation"] = FloatArray(node.Translation.X, node.Translation.Y, node.Translation.Z),
                ["extras"] = new Dictionary<string, object?>
                {
                    ["exportId"] = node.ExportId,
                    ["sourceRuntimeName"] = node.SourceRuntimeName,
                    ["category"] = node.Category,
                    ["generatorType"] = node.GeneratorType,
                    ["semanticAssetType"] = node.SemanticAssetType,
                    ["instanceIndex"] = node.InstanceIndex,
                    ["proceduralGenerated"] = true,
                    ["sourceAsset"] = "",
                    ["isBackdrop"] = node.IsBackdrop,
                    ["isRuntimeShadow"] = node.IsRuntimeShadow
                }
            });
        }

        List<Dictionary<string, object?>> meshes = [];
        foreach (MeshGeometry mesh in scene.Meshes)
        {
            meshes.Add(new Dictionary<string, object?>
            {
                ["name"] = mesh.Name,
                ["primitives"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["attributes"] = new Dictionary<string, object?>
                        {
                            ["POSITION"] = mesh.PositionAccessorIndex,
                            ["NORMAL"] = mesh.NormalAccessorIndex,
                            ["TEXCOORD_0"] = mesh.UvAccessorIndex
                        },
                        ["indices"] = mesh.IndexAccessorIndex,
                        ["material"] = mesh.MaterialIndex,
                        ["mode"] = 4
                    }
                }
            });
        }

        List<Dictionary<string, object?>> materials = [];
        foreach (ExportMaterial material in scene.Materials)
        {
            Dictionary<string, object?> pbr = new()
            {
                ["baseColorFactor"] = FloatArray(material.BaseColor.X, material.BaseColor.Y, material.BaseColor.Z, material.Alpha),
                ["metallicFactor"] = material.Metallic,
                ["roughnessFactor"] = material.Roughness,
                ["baseColorTexture"] = new Dictionary<string, object?> { ["index"] = scene.Textures.IndexOf(material.Texture) }
            };
            Dictionary<string, object?> materialJson = new()
            {
                ["name"] = material.Name,
                ["pbrMetallicRoughness"] = pbr,
                ["alphaMode"] = material.AlphaMode,
                ["doubleSided"] = material.DoubleSided,
                ["extras"] = new Dictionary<string, object?>
                {
                    ["sourceTexture"] = material.Texture.SemanticName,
                    ["specularApproximation"] = "MonoGame BasicEffect specularPower/specularColor approximated to glTF roughness; metallic forced to 0."
                }
            };
            if (material.EmissiveColor.LengthSquared() > 0.0001f)
            {
                materialJson["emissiveFactor"] = FloatArray(material.EmissiveColor.X, material.EmissiveColor.Y, material.EmissiveColor.Z);
            }

            materials.Add(materialJson);
        }

        List<Dictionary<string, object?>> images = [];
        for (int i = 0; i < scene.Textures.Count; i++)
        {
            TextureExportInfo texture = scene.Textures[i];
            Dictionary<string, object?> image = new()
            {
                ["name"] = texture.SemanticName
            };
            if (embedTextures)
            {
                image["bufferView"] = texture.BufferViewIndex;
                image["mimeType"] = "image/png";
            }
            else
            {
                image["uri"] = texture.RelativeUri;
            }

            images.Add(image);
        }

        List<Dictionary<string, object?>> gltfTextures = [];
        for (int i = 0; i < scene.Textures.Count; i++)
        {
            gltfTextures.Add(new Dictionary<string, object?> { ["source"] = i });
        }

        return new Dictionary<string, object?>
        {
            ["asset"] = new Dictionary<string, object?>
            {
                ["version"] = "2.0",
                ["generator"] = "GranTurismo HighSpeedRingBlenderExporter"
            },
            ["scene"] = 0,
            ["scenes"] = new[]
            {
                new Dictionary<string, object?> { ["name"] = "HighSpeedRing", ["nodes"] = new[] { rootNodeIndex } }
            },
            ["nodes"] = nodes,
            ["meshes"] = meshes,
            ["materials"] = materials,
            ["images"] = images,
            ["textures"] = gltfTextures,
            ["samplers"] = new[] { new Dictionary<string, object?> { ["magFilter"] = 9728, ["minFilter"] = 9728, ["wrapS"] = 10497, ["wrapT"] = 10497 } },
            ["buffers"] = new[]
            {
                embedTextures
                    ? new Dictionary<string, object?> { ["byteLength"] = binaryLength }
                    : new Dictionary<string, object?> { ["uri"] = binFileName, ["byteLength"] = binaryLength }
            },
            ["bufferViews"] = parts.BufferViews.Select(view =>
            {
                Dictionary<string, object?> json = new()
                {
                    ["buffer"] = 0,
                    ["byteOffset"] = view.ByteOffset,
                    ["byteLength"] = view.ByteLength
                };
                if (view.Target.HasValue)
                {
                    json["target"] = view.Target.Value;
                }

                return json;
            }).ToArray(),
            ["accessors"] = parts.Accessors.Select(accessor =>
            {
                Dictionary<string, object?> json = new()
                {
                    ["bufferView"] = accessor.BufferView,
                    ["componentType"] = accessor.ComponentType,
                    ["count"] = accessor.Count,
                    ["type"] = accessor.Type
                };
                if (accessor.Min.HasValue && accessor.Max.HasValue)
                {
                    json["min"] = FloatArray(accessor.Min.Value.X, accessor.Min.Value.Y, accessor.Min.Value.Z);
                    json["max"] = FloatArray(accessor.Max.Value.X, accessor.Max.Value.Y, accessor.Max.Value.Z);
                }

                return json;
            }).ToArray()
        };
    }

    private static ExportManifest BuildManifest(
        TrackDefinitionFile sourceFile,
        TrackScene track,
        IReadOnlyList<StaticMesh> backdropMeshes,
        ExportScene scene,
        List<TextureExportInfo> usedTextures,
        string packageRoot)
    {
        IReadOnlyList<StaticMesh> allMeshes = track.Meshes.Concat(backdropMeshes).ToArray();
        int runtimeVertexCount = allMeshes.Sum(mesh => mesh.Vertices.Count);
        int runtimeTriangleCount = allMeshes.Sum(mesh => mesh.Indices.Count / 3);
        int reusedNodes = scene.Nodes.Count - scene.Meshes.Count;
        BoundingBox bounds = CalculateBounds(allMeshes);
        TrackStartMetrics start = TrackScene.MeasureStart(track.Definition);

        return new ExportManifest(
            DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
            "High Speed Ring generated TrackScene plus ProceduralBackdropRenderer",
            sourceFile.SourcePath,
            packageRoot,
            "glTF 2.0 external textures plus GLB embedded transport",
            "GranTurismo/MonoGame coordinates are exported unchanged: metres, Y-up, X/Z ground plane. glTF is Y-up and right-handed; triangle winding/normals are preserved from generated mesh data.",
            new RuntimeSceneStats(
                track.Meshes.Count,
                backdropMeshes.Count,
                allMeshes.Count,
                runtimeVertexCount,
                runtimeTriangleCount,
                allMeshes.Select(mesh => MaterialKey(mesh, usedTextures.First(texture => ReferenceEquals(texture.Texture, mesh.Texture)))).Distinct(StringComparer.Ordinal).Count(),
                usedTextures.Count,
                track.LengthMeters,
                ToManifestVector(bounds.Min),
                ToManifestVector(bounds.Max),
                ToManifestVector(start.Position)),
            new ExportedSceneStats(
                scene.Nodes.Count,
                scene.Meshes.Count,
                scene.Materials.Count,
                scene.Textures.Count,
                reusedNodes,
                0),
            scene.Materials.Select(material => new MaterialManifestEntry(
                material.Name,
                material.Texture.SemanticName,
                material.AlphaMode,
                material.DoubleSided,
                ToManifestVector(material.BaseColor),
                material.Alpha,
                ToManifestVector(material.EmissiveColor),
                material.Roughness,
                material.Metallic)).ToArray(),
            usedTextures.Select(texture => new TextureManifestEntry(
                texture.SemanticName,
                $"Textures/{texture.FileName}",
                texture.Width,
                texture.Height,
                texture.Generated,
                texture.SourcePath,
                texture.HasAlpha,
                texture.Sha256)).ToArray(),
            scene.Nodes.Select(node => new NodeManifestEntry(
                node.Name,
                node.ExportId,
                node.SourceRuntimeName,
                node.Category,
                node.GeneratorType,
                node.SemanticAssetType,
                node.InstanceIndex,
                node.MeshIndex,
                ToManifestVector(node.Translation),
                node.IsBackdrop,
                node.IsRuntimeShadow)).ToArray(),
            [
                "Procedural sky shader is not exported as glTF shader logic.",
                "MonoGame specular state is approximated to glTF roughness with metallic=0.",
                "Generated textures are baked to PNG for Blender editing.",
                "Most generated props are world-space meshes; exporter recovers translation-only mesh reuse where geometry/material data matches after bounds-centre normalisation.",
                "Backdrop meshes are exported as editable geometry but remain runtime visual backdrop elements, not canonical source models."
            ],
            new ValidationReport(false, []));
    }

    private static ValidationReport Validate(ExportScene scene, string externalGltfPath, string externalBinPath, string glbPath)
    {
        List<string> warnings = [];
        if (!File.Exists(externalGltfPath))
        {
            warnings.Add("External glTF file missing.");
        }

        if (!File.Exists(externalBinPath))
        {
            warnings.Add("External glTF binary buffer missing.");
        }

        if (!File.Exists(glbPath))
        {
            warnings.Add("GLB file missing.");
        }

        using (JsonDocument.Parse(File.ReadAllText(externalGltfPath))) { }

        foreach (ExportNode node in scene.Nodes)
        {
            if (!IsFinite(node.Translation))
            {
                warnings.Add($"Node '{node.Name}' has invalid translation.");
            }
        }

        foreach (MeshGeometry mesh in scene.Meshes)
        {
            if (mesh.Positions.Length == 0 || mesh.Indices.Length == 0)
            {
                warnings.Add($"Mesh '{mesh.Name}' has no geometry.");
            }

            if (mesh.Uvs.Length != mesh.Positions.Length / 3 * 2)
            {
                warnings.Add($"Mesh '{mesh.Name}' has an invalid UV channel length.");
            }

            if (mesh.Indices.Length % 3 != 0)
            {
                warnings.Add($"Mesh '{mesh.Name}' has a non-triangle index count.");
            }
        }

        foreach (TextureExportInfo texture in scene.Textures)
        {
            string texturePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(externalGltfPath)!, texture.RelativeUri.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(texturePath))
            {
                warnings.Add($"Texture '{texture.SemanticName}' missing at '{texture.RelativeUri}'.");
            }
        }

        return new ValidationReport(warnings.Count == 0, warnings.ToArray());
    }

    private static void TryCreateBlenderReview(
        string editableDirectory,
        string externalGltfPath,
        string manifestJsonPath,
        ExportManifest manifest)
    {
        string? blender = FindBlenderExecutable();
        if (blender is null)
        {
            Console.WriteLine("Blender executable not found; skipped HighSpeedRing_Review.blend creation.");
            return;
        }

        string reviewPath = Path.Combine(editableDirectory, "HighSpeedRing_Review.blend");
        string scriptPath = Path.Combine(Path.GetTempPath(), $"hsr_blender_review_{Guid.NewGuid():N}.py");
        File.WriteAllText(scriptPath, BuildBlenderReviewScript(externalGltfPath, manifestJsonPath, reviewPath));
        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = blender,
                Arguments = $"--background --python \"{scriptPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start Blender.");
            process.WaitForExit(120_000);
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            if (process.ExitCode == 0 && File.Exists(reviewPath))
            {
                Console.WriteLine($"Created Blender review file '{reviewPath}'.");
            }
            else
            {
                Console.WriteLine("Blender review creation did not complete successfully.");
                Console.WriteLine(output);
                Console.WriteLine(error);
            }
        }
        finally
        {
            try
            {
                File.Delete(scriptPath);
            }
            catch (IOException)
            {
            }
        }
    }

    private static string BuildBlenderReviewScript(string gltfPath, string manifestPath, string reviewPath)
    {
        string safeGltf = gltfPath.Replace("\\", "\\\\");
        string safeManifest = manifestPath.Replace("\\", "\\\\");
        string safeReview = reviewPath.Replace("\\", "\\\\");
        return $$"""
import bpy
import json
import os

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete()
bpy.ops.import_scene.gltf(filepath=r"{{safeGltf}}")

with open(r"{{safeManifest}}", "r", encoding="utf-8") as f:
    manifest = json.load(f)

nodes = {node["name"]: node for node in manifest.get("nodes", [])}
root_collection = bpy.data.collections.new("HighSpeedRing")
bpy.context.scene.collection.children.link(root_collection)
collections = {}

def get_collection(name):
    if name not in collections:
        coll = bpy.data.collections.new(name)
        root_collection.children.link(coll)
        collections[name] = coll
    return collections[name]

for obj in list(bpy.context.scene.objects):
    node = nodes.get(obj.name)
    if not node:
        continue
    for key in ["exportId", "sourceRuntimeName", "category", "generatorType", "semanticAssetType", "instanceIndex", "isBackdrop", "isRuntimeShadow"]:
        if key in node:
            obj[key] = node[key]
    coll = get_collection(node.get("category", "Misc"))
    for existing in obj.users_collection:
        existing.objects.unlink(obj)
    coll.objects.link(obj)

for image in bpy.data.images:
    if image.filepath:
        image.filepath = bpy.path.relpath(image.filepath, start=os.path.dirname(r"{{safeReview}}"))

bpy.ops.wm.save_as_mainfile(filepath=r"{{safeReview}}")
""";
    }

    private static string? FindBlenderExecutable()
    {
        string[] candidates =
        [
            @"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe",
            @"C:\Program Files\Blender Foundation\Blender 5.1\blender.exe",
            @"C:\Program Files\Blender Foundation\Blender 4.4\blender.exe",
            @"C:\Program Files\Blender Foundation\Blender 4.3\blender.exe",
            @"C:\Program Files\Blender Foundation\Blender\blender.exe"
        ];
        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void WriteMarkdownManifest(string path, ExportManifest manifest)
    {
        StringBuilder builder = new();
        builder.AppendLine("# High Speed Ring Export Manifest");
        builder.AppendLine();
        builder.AppendLine($"- Export date/build: `{manifest.ExportDate}`");
        builder.AppendLine($"- Source generated track: `{manifest.SourceGeneratedTrack}`");
        builder.AppendLine($"- Source definition: `{manifest.SourceDefinitionPath}`");
        builder.AppendLine($"- Export format: `{manifest.ExportFormat}`");
        builder.AppendLine($"- Coordinate system: {manifest.CoordinateSystem}");
        builder.AppendLine();
        builder.AppendLine("## Runtime Scene");
        builder.AppendLine();
        builder.AppendLine($"- StaticMesh count: `{manifest.RuntimeScene.StaticMeshCount}`");
        builder.AppendLine($"- Backdrop mesh count: `{manifest.RuntimeScene.BackdropMeshCount}`");
        builder.AppendLine($"- Total mesh count: `{manifest.RuntimeScene.TotalMeshCount}`");
        builder.AppendLine($"- Vertex count: `{manifest.RuntimeScene.VertexCount}`");
        builder.AppendLine($"- Triangle count: `{manifest.RuntimeScene.TriangleCount}`");
        builder.AppendLine($"- Unique materials/textures: `{manifest.RuntimeScene.UniqueMaterialCount}` / `{manifest.RuntimeScene.UniqueTextureCount}`");
        builder.AppendLine($"- Track length: `{manifest.RuntimeScene.TrackLengthMeters:0.###}m`");
        builder.AppendLine($"- Bounds min/max: `{VectorText(manifest.RuntimeScene.BoundsMin)}` / `{VectorText(manifest.RuntimeScene.BoundsMax)}`");
        builder.AppendLine($"- Start/finish validation point: `{VectorText(manifest.RuntimeScene.StartPosition)}`");
        builder.AppendLine();
        builder.AppendLine("## Exported Blender Package");
        builder.AppendLine();
        builder.AppendLine($"- Node count: `{manifest.ExportedScene.NodeCount}`");
        builder.AppendLine($"- Unique mesh count: `{manifest.ExportedScene.UniqueMeshCount}`");
        builder.AppendLine($"- Material count: `{manifest.ExportedScene.MaterialCount}`");
        builder.AppendLine($"- Texture count: `{manifest.ExportedScene.TextureCount}`");
        builder.AppendLine($"- Reused mesh-instance count: `{manifest.ExportedScene.ReusedMeshInstanceCount}`");
        builder.AppendLine($"- Merged export-only mesh count: `{manifest.ExportedScene.MergedExportOnlyMeshCount}`");
        builder.AppendLine();
        builder.AppendLine("## Texture Strategy");
        builder.AppendLine();
        builder.AppendLine("External glTF texture paths are relative to `Editable/` and point to `../Textures/*.png`. The GLB embeds PNG image bytes for transport. Canonical source textures are not mutated; generated runtime textures are baked as PNG.");
        builder.AppendLine();
        builder.AppendLine("## Material Conversion");
        builder.AppendLine();
        builder.AppendLine("MonoGame diffuse/base color, texture, alpha, normals, UVs and emissive values are preserved. BasicEffect specular power/color is approximated to glTF roughness, and metallic is set to 0 because the runtime material model is not metallic/roughness PBR.");
        builder.AppendLine();
        builder.AppendLine("## Unsupported Or Approximate Runtime Features");
        builder.AppendLine();
        foreach (string warning in manifest.Warnings)
        {
            builder.AppendLine($"- {warning}");
        }

        builder.AppendLine();
        builder.AppendLine("## Validation");
        builder.AppendLine();
        builder.AppendLine($"- Passed: `{manifest.Validation.Passed}`");
        foreach (string warning in manifest.Validation.Warnings)
        {
            builder.AppendLine($"- Warning: {warning}");
        }

        File.WriteAllText(path, builder.ToString());
    }

    private static void CopySourceFiles(string sourceDefinitionPath, string sourceDirectory)
    {
        if (File.Exists(sourceDefinitionPath))
        {
            File.Copy(sourceDefinitionPath, Path.Combine(sourceDirectory, Path.GetFileName(sourceDefinitionPath)), overwrite: true);
        }

        string sourceSpline = Path.Combine("Assets", "Tracks", "HighSpeedRing", "HighSpeedRing_Spline.svg");
        if (File.Exists(sourceSpline))
        {
            File.Copy(sourceSpline, Path.Combine(sourceDirectory, Path.GetFileName(sourceSpline)), overwrite: true);
        }

        string nestedSourceSpline = Path.Combine("Assets", "Tracks", "HighSpeedRing", "Spline", "HighSpeedRing_Spline.svg");
        if (File.Exists(nestedSourceSpline))
        {
            File.Copy(nestedSourceSpline, Path.Combine(sourceDirectory, Path.GetFileName(nestedSourceSpline)), overwrite: true);
        }
    }

    private static void CreateAssetCategoryFolders(string packageRoot)
    {
        foreach (string category in new[] { "Barriers", "Buildings", "Fences", "Props", "Signs", "Vegetation", "Lights", "Grandstands" })
        {
            Directory.CreateDirectory(Path.Combine(packageRoot, "Assets", category));
        }
    }

    private static string ClassifyCategory(string name, bool isBackdrop)
    {
        string n = name.ToLowerInvariant();
        if (isBackdrop || n.Contains("backdrop")) return "Backdrop";
        if (n.Contains("shadow")) return "RuntimeShadows";
        if (n.Contains("asphalt")) return "Asphalt";
        if (n.Contains("curb")) return "Curbs";
        if (n.Contains("grass") || n.Contains("field") || n.Contains("pasture") || n.Contains("meadow") || n.Contains("crop") || n.Contains("terrain") || n.Contains("chalk")) return "Terrain";
        if (n.Contains("tree")) return "Trees";
        if (n.Contains("hedge") || n.Contains("scrub") || n.Contains("copse") || n.Contains("forest") || n.Contains("understory")) return "Hedges";
        if (n.Contains("grandstand") || n.Contains("spectator") || n.Contains("seating")) return "Grandstands";
        if (n.Contains("building") || n.Contains("hut") || n.Contains("hangar") || n.Contains("barn") || n.Contains("farmhouse") || n.Contains("race control")) return "Buildings";
        if (n.Contains("barrier") || n.Contains("wall") || n.Contains("tire stack")) return "Barriers";
        if (n.Contains("fence") || n.Contains("gate")) return "Fences";
        if (n.Contains("sign") || n.Contains("marker") || n.Contains("chevron") || n.Contains("board")) return "Signs";
        if (n.Contains("light") || n.Contains("lamp")) return "Lights";
        if (n.Contains("airfield") || n.Contains("aircraft") || n.Contains("windsock") || n.Contains("apron")) return "Airfield";
        if (n.Contains("rail") || n.Contains("underpass") || n.Contains("sleeper")) return "RailUnderpass";
        if (n.Contains("start") || n.Contains("gantry") || n.Contains("grid") || n.Contains("paddock")) return "RaceFurniture";
        return "Misc";
    }

    private static string ClassifySemanticAssetType(string name, string category)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("asphalt")) return "Track_Asphalt";
        if (n.Contains("left curb")) return "Kerb_Left";
        if (n.Contains("right curb")) return "Kerb_Right";
        if (n.Contains("grass shoulder")) return "Grass_Shoulder";
        if (n.Contains("grass field")) return "Grass_Field";
        if (n.Contains("chevron")) return n.Contains("barrier") ? "Barrier_ChevronConcrete" : "Sign_Chevron";
        if (n.Contains("brake marker")) return n.Contains("post") ? "Post_BrakeMarker" : "BrakeMarker_100";
        if (n.Contains("tire stack")) return "TyreStack_A";
        if (n.Contains("grandstand")) return "Grandstand_A";
        if (n.Contains("gantry")) return "StartFinish_Gantry";
        if (n.Contains("tree")) return "Tree_Billboard_A";
        if (n.Contains("hedge") || n.Contains("scrub")) return "Vegetation_Hedge_A";
        if (n.Contains("fence")) return "Fence_A";
        if (n.Contains("railway") || n.Contains("sleeper")) return "Railway_A";
        if (n.Contains("underpass")) return "Underpass_A";
        if (n.Contains("shadow")) return "RuntimeShadow_Blob";
        if (n.Contains("backdrop")) return "Backdrop_A";
        return SanitizeIdentifier(category + "_" + name);
    }

    private static string ClassifyGeneratorType(string name, string category)
    {
        string n = name.ToLowerInvariant();
        if (category == "Asphalt" || category == "Curbs" || n.Contains("shoulder") || n.Contains("wall")) return "TrackSceneOffsetRibbon";
        if (category == "RuntimeShadows") return "RuntimeBillboardShadow";
        if (category is "Trees" or "Hedges" || n.Contains("billboard")) return "ProceduralBillboard";
        if (category == "Backdrop") return "ProceduralBackdropRenderer";
        if (n.Contains("box") || n.Contains("hut") || n.Contains("barrier") || n.Contains("wall") || n.Contains("rail")) return "MeshFactoryBox";
        if (n.Contains("tire stack")) return "MeshFactoryCylinder";
        return "MeshFactoryGenerated";
    }

    private static string MaterialKey(StaticMesh mesh, TextureExportInfo texture)
    {
        return string.Join("|",
            texture.SemanticName,
            Round(mesh.DiffuseColor.X),
            Round(mesh.DiffuseColor.Y),
            Round(mesh.DiffuseColor.Z),
            Round(mesh.Alpha),
            Round(mesh.EmissiveColor.X),
            Round(mesh.EmissiveColor.Y),
            Round(mesh.EmissiveColor.Z),
            Round(mesh.SpecularPower));
    }

    private static string GeometryKey(MeshGeometry geometry)
    {
        using SHA256 sha = SHA256.Create();
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write(geometry.MaterialIndex);
        foreach (float value in geometry.Positions.Concat(geometry.Normals).Concat(geometry.Uvs))
        {
            writer.Write(MathF.Round(value, 4));
        }

        foreach (int index in geometry.Indices)
        {
            writer.Write(index);
        }

        return Convert.ToHexString(sha.ComputeHash(stream.ToArray()));
    }

    private static float ApproximateRoughness(float specularPower, Vector3 specularColor)
    {
        if (specularColor.LengthSquared() <= 0.0001f)
        {
            return 0.82f;
        }

        return MathHelper.Clamp(1f - MathF.Log2(MathF.Max(1f, specularPower)) / 12f, 0.18f, 0.92f);
    }

    private static BoundingBox CalculateBounds(IReadOnlyList<StaticMesh> meshes)
    {
        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);
        foreach (StaticMesh mesh in meshes)
        {
            min = Vector3.Min(min, mesh.Bounds.Min);
            max = Vector3.Max(max, mesh.Bounds.Max);
        }

        return new BoundingBox(min, max);
    }

    private static Vector3 Center(BoundingBox bounds)
    {
        return (bounds.Min + bounds.Max) * 0.5f;
    }

    private static string BoundsToken(BoundingBox bounds)
    {
        Vector3 center = Center(bounds);
        Vector3 size = bounds.Max - bounds.Min;
        return $"{Round(center.X)},{Round(center.Y)},{Round(center.Z)}|{Round(size.X)},{Round(size.Y)},{Round(size.Z)}";
    }

    private static string Round(float value)
    {
        return MathF.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static int NextIndex(Dictionary<string, int> counts, string key)
    {
        counts.TryGetValue(key, out int count);
        count++;
        counts[key] = count;
        return count;
    }

    private static string ShortHash(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }

    private static string FileHash(string path)
    {
        using SHA256 sha = SHA256.Create();
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static string SanitizeIdentifier(string value)
    {
        StringBuilder builder = new();
        foreach (char c in value)
        {
            builder.Append(char.IsLetterOrDigit(c) ? c : '_');
        }

        while (builder.ToString().Contains("__", StringComparison.Ordinal))
        {
            builder.Replace("__", "_");
        }

        return builder.ToString().Trim('_');
    }

    private static float[] FloatArray(params float[] values)
    {
        return values.Select(value => MathF.Round(value, 6)).ToArray();
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    }

    private static string VectorText(Vector3 value)
    {
        return $"{value.X:0.###}, {value.Y:0.###}, {value.Z:0.###}";
    }

    private static string VectorText(ManifestVector3 value)
    {
        return $"{value.X:0.###}, {value.Y:0.###}, {value.Z:0.###}";
    }

    private static ManifestVector3 ToManifestVector(Vector3 value)
    {
        return new ManifestVector3(
            MathF.Round(value.X, 6),
            MathF.Round(value.Y, 6),
            MathF.Round(value.Z, 6));
    }

    private static byte[] Pad(byte[] source, byte pad)
    {
        int paddedLength = (source.Length + 3) & ~3;
        byte[] output = new byte[paddedLength];
        Array.Copy(source, output, source.Length);
        for (int i = source.Length; i < output.Length; i++)
        {
            output[i] = pad;
        }

        return output;
    }

    private sealed class BinaryWriterBuffer
    {
        private readonly MemoryStream _stream = new();
        private readonly BinaryWriter _writer;

        public BinaryWriterBuffer()
        {
            _writer = new BinaryWriter(_stream);
        }

        public int Length => (int)_stream.Length;

        public void Write(float value) => _writer.Write(value);

        public void Write(uint value) => _writer.Write(value);

        public void Write(byte[] value) => _writer.Write(value);

        public void Align4()
        {
            while ((_stream.Length & 3) != 0)
            {
                _writer.Write((byte)0);
            }
        }

        public byte[] ToArray() => _stream.ToArray();
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<Texture2D>
    {
        public static ReferenceEqualityComparer Instance { get; } = new();

        public bool Equals(Texture2D? x, Texture2D? y) => ReferenceEquals(x, y);

        public int GetHashCode(Texture2D obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    private sealed record ExportMeshInput(
        StaticMesh Mesh,
        string NodeName,
        string ExportId,
        string Category,
        string GeneratorType,
        string SemanticAssetType,
        int InstanceIndex,
        bool IsBackdrop,
        bool IsRuntimeShadow);

    private sealed class ExportScene
    {
        public List<MeshGeometry> Meshes { get; } = [];
        public List<ExportMaterial> Materials { get; } = [];
        public List<TextureExportInfo> Textures { get; } = [];
        public List<ExportNode> Nodes { get; } = [];
    }

    private sealed record MeshGeometry(
        string Name,
        float[] Positions,
        float[] Normals,
        float[] Uvs,
        int[] Indices,
        Vector3 Min,
        Vector3 Max,
        int MaterialIndex)
    {
        public int PositionAccessorIndex { get; set; }
        public int NormalAccessorIndex { get; set; }
        public int UvAccessorIndex { get; set; }
        public int IndexAccessorIndex { get; set; }
    }

    private sealed record ExportNode(
        string Name,
        string ExportId,
        string SourceRuntimeName,
        string Category,
        string GeneratorType,
        string SemanticAssetType,
        int InstanceIndex,
        bool IsBackdrop,
        bool IsRuntimeShadow,
        int MeshIndex,
        Vector3 Translation);

    private sealed record TextureExportInfo(
        Texture2D Texture,
        string SemanticName,
        string FileName,
        string RelativeUri,
        string SourcePath,
        bool Generated,
        bool HasAlpha,
        string Sha256,
        int Width,
        int Height)
    {
        public int BufferViewIndex { get; set; } = -1;
    }

    private sealed record ExportMaterial(
        string Name,
        TextureExportInfo Texture,
        Vector3 BaseColor,
        float Alpha,
        Vector3 EmissiveColor,
        float Roughness,
        float Metallic,
        string AlphaMode,
        bool DoubleSided);

    private sealed record GltfDocumentParts(List<BufferViewInfo> BufferViews, List<AccessorInfo> Accessors);

    private sealed record BufferViewInfo(int ByteOffset, int ByteLength, int? Target);

    private sealed record AccessorInfo(
        int BufferView,
        int ComponentType,
        int Count,
        string Type,
        Vector3? Min,
        Vector3? Max)
    {
        public static AccessorInfo Float(int bufferView, int count, string type, Vector3? min, Vector3? max)
        {
            return new AccessorInfo(bufferView, 5126, count, type, min, max);
        }

        public static AccessorInfo UnsignedInt(int bufferView, int count, string type)
        {
            return new AccessorInfo(bufferView, 5125, count, type, null, null);
        }
    }

    private sealed record ExportManifest(
        string ExportDate,
        string SourceGeneratedTrack,
        string SourceDefinitionPath,
        string PackageRoot,
        string ExportFormat,
        string CoordinateSystem,
        RuntimeSceneStats RuntimeScene,
        ExportedSceneStats ExportedScene,
        MaterialManifestEntry[] Materials,
        TextureManifestEntry[] Textures,
        NodeManifestEntry[] Nodes,
        string[] Warnings,
        ValidationReport Validation);

    private sealed record RuntimeSceneStats(
        int StaticMeshCount,
        int BackdropMeshCount,
        int TotalMeshCount,
        int VertexCount,
        int TriangleCount,
        int UniqueMaterialCount,
        int UniqueTextureCount,
        float TrackLengthMeters,
        ManifestVector3 BoundsMin,
        ManifestVector3 BoundsMax,
        ManifestVector3 StartPosition);

    private sealed record ExportedSceneStats(
        int NodeCount,
        int UniqueMeshCount,
        int MaterialCount,
        int TextureCount,
        int ReusedMeshInstanceCount,
        int MergedExportOnlyMeshCount);

    private sealed record MaterialManifestEntry(
        string Name,
        string Texture,
        string AlphaMode,
        bool DoubleSided,
        ManifestVector3 BaseColor,
        float Alpha,
        ManifestVector3 EmissiveColor,
        float Roughness,
        float Metallic);

    private sealed record TextureManifestEntry(
        string Name,
        string RelativePath,
        int Width,
        int Height,
        bool Generated,
        string SourcePath,
        bool HasAlpha,
        string Sha256);

    private sealed record NodeManifestEntry(
        string Name,
        string ExportId,
        string SourceRuntimeName,
        string Category,
        string GeneratorType,
        string SemanticAssetType,
        int InstanceIndex,
        int MeshIndex,
        ManifestVector3 Translation,
        bool IsBackdrop,
        bool IsRuntimeShadow);

    private sealed record ValidationReport(bool Passed, string[] Warnings);

    private readonly record struct ManifestVector3(float X, float Y, float Z);
}
