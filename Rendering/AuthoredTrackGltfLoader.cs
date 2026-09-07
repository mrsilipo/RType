using System.Text;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RType.World;

namespace RType.Rendering;

public sealed record AuthoredTrackVisualStats(
    string Path,
    string CoordinateConversion,
    int NodeCount,
    int MeshDefinitionCount,
    int RuntimeMeshCount,
    int MaterialCount,
    int TextureCount,
    int ImageCount,
    int VertexCount,
    int TriangleCount,
    int DriveableNodeCount,
    int DriveableTriangleCount,
    BoundingBox Bounds,
    IReadOnlyList<AuthoredTrackStartMarker> StartMarkers,
    IReadOnlyList<string> UnsupportedFeatures);

public sealed record AuthoredTrackStartMarker(
    int GridIndex,
    string Name,
    Vector3 Position,
    float HeadingRadians);

public sealed record AuthoredTrackVisualLoadResult(
    IReadOnlyList<StaticMesh> Meshes,
    IReadOnlyList<AuthoredDriveableTriangle> DriveableTriangles,
    AuthoredTrackVisualStats Stats);

internal static class AuthoredTrackGltfLoader
{
    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunkType = 0x4E4F534A;
    private const uint BinaryChunkType = 0x004E4942;

    public static AuthoredTrackVisualLoadResult Load(GraphicsDevice graphicsDevice, string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Configured authored track visual GLB was not found: {fullPath}", fullPath);
        }

        ReadOnlyMemory<byte> binaryChunk = ReadGlb(fullPath, out JsonDocument document);
        using (document)
        {
            JsonElement root = document.RootElement;
            JsonElement nodes = RequiredArray(root, "nodes");
            JsonElement meshes = RequiredArray(root, "meshes");
            JsonElement accessors = RequiredArray(root, "accessors");
            JsonElement bufferViews = RequiredArray(root, "bufferViews");
            JsonElement materials = root.TryGetProperty("materials", out JsonElement materialArray)
                ? materialArray
                : default;
            JsonElement images = root.TryGetProperty("images", out JsonElement imageArray)
                ? imageArray
                : default;
            JsonElement textures = root.TryGetProperty("textures", out JsonElement textureArray)
                ? textureArray
                : default;

            List<string> unsupported = [];
            Texture2D whiteTexture = CreateSolidTexture(graphicsDevice, Color.White);
            List<Texture2D?> loadedImages = LoadImages(graphicsDevice, images, bufferViews, binaryChunk, fullPath, unsupported);
            GltfMaterial[] loadedMaterials = LoadMaterials(materials, textures, loadedImages, whiteTexture, unsupported);
            if (loadedMaterials.Length == 0)
            {
                loadedMaterials = [new GltfMaterial("Default", whiteTexture, Vector3.One, 1f, Vector3.Zero, 16f, false, false, false, TextureCoordinateMode.Uv0, TextureTransform.Identity)];
            }

            List<StaticMesh> runtimeMeshes = [];
            List<AuthoredDriveableTriangle> driveableTriangles = [];
            HashSet<string> driveableNodeNames = new(StringComparer.Ordinal);
            int vertexCount = 0;
            int triangleCount = 0;
            BoundingBox bounds = new(Vector3.Zero, Vector3.Zero);
            bool hasBounds = false;
            List<AuthoredTrackStartMarker> startMarkers = [];

            int[] sceneRoots = GetSceneRoots(root, nodes);
            foreach (int rootNode in sceneRoots)
            {
                VisitNode(rootNode, Matrix.Identity);
            }

            startMarkers = ResolveStartMarkerHeadings(startMarkers);

            AuthoredTrackVisualStats stats = new(
                fullPath,
                "Blender Z-up authoring -> Blender glTF exporter writes glTF 2.0 Y-up; runtime imports glTF coordinates directly as GranTurismo X/right, Y/up, Z/forward metres.",
                nodes.GetArrayLength(),
                meshes.GetArrayLength(),
                runtimeMeshes.Count,
                materialArray.ValueKind == JsonValueKind.Array ? materialArray.GetArrayLength() : 0,
                textureArray.ValueKind == JsonValueKind.Array ? textureArray.GetArrayLength() : 0,
                imageArray.ValueKind == JsonValueKind.Array ? imageArray.GetArrayLength() : 0,
                vertexCount,
                triangleCount,
                driveableNodeNames.Count,
                driveableTriangles.Count,
                bounds,
                startMarkers,
                unsupported.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray());

            Console.WriteLine("HighSpeedRing visual source: Authored");
            Console.WriteLine($"Loaded: {fullPath}");
            Console.WriteLine($"Nodes: {stats.NodeCount}, glTF meshes: {stats.MeshDefinitionCount}, runtime meshes: {stats.RuntimeMeshCount}");
            Console.WriteLine($"Triangles: {stats.TriangleCount}, vertices: {stats.VertexCount}");
            Console.WriteLine($"Driveable nodes: {stats.DriveableNodeCount}, driveable triangles: {stats.DriveableTriangleCount}");
            Console.WriteLine($"Materials: {stats.MaterialCount}, glTF texture slots: {stats.TextureCount}, embedded images: {stats.ImageCount}");
            Console.WriteLine($"Bounds: min {FormatVector(stats.Bounds.Min)}, max {FormatVector(stats.Bounds.Max)}");
            Console.WriteLine($"Coordinate conversion: {stats.CoordinateConversion}");
            if (stats.StartMarkers.Count > 0)
            {
                Console.WriteLine(
                    "Authored start markers: " +
                    string.Join(", ", stats.StartMarkers
                        .OrderBy(marker => marker.GridIndex)
                        .Select(marker => $"{marker.Name} {FormatVector(marker.Position)} heading {MathHelper.ToDegrees(marker.HeadingRadians):0.#}deg")));
            }

            if (stats.UnsupportedFeatures.Count > 0)
            {
                Console.WriteLine("Unsupported glTF features encountered:");
                foreach (string warning in stats.UnsupportedFeatures.Take(24))
                {
                    Console.WriteLine($"  - {warning}");
                }
            }

            return new AuthoredTrackVisualLoadResult(runtimeMeshes, driveableTriangles, stats);

            void VisitNode(int nodeIndex, Matrix parentWorld)
            {
                if ((uint)nodeIndex >= (uint)nodes.GetArrayLength())
                {
                    unsupported.Add($"Node index out of range: {nodeIndex}");
                    return;
                }

                JsonElement node = nodes[nodeIndex];
                Matrix local = ReadNodeTransform(node, unsupported);
                Matrix world = local * parentWorld;
                string nodeName = ReadString(node, "name", $"node_{nodeIndex:0000}");
                if (TryParseGridMarkerIndex(nodeName, out int gridIndex))
                {
                    startMarkers.Add(new AuthoredTrackStartMarker(
                        gridIndex,
                        nodeName,
                        new Vector3(world.M41, world.M42, world.M43),
                        0f));
                }

                if (node.TryGetProperty("mesh", out JsonElement meshElement) &&
                    meshElement.ValueKind == JsonValueKind.Number &&
                    meshElement.TryGetInt32(out int meshIndex))
                {
                    LoadMeshNode(nodeName, meshIndex, world);
                }

                if (node.TryGetProperty("children", out JsonElement children) &&
                    children.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement child in children.EnumerateArray())
                    {
                        if (child.TryGetInt32(out int childIndex))
                        {
                            VisitNode(childIndex, world);
                        }
                    }
                }
            }

            void LoadMeshNode(string nodeName, int meshIndex, Matrix world)
            {
                if ((uint)meshIndex >= (uint)meshes.GetArrayLength())
                {
                    unsupported.Add($"{nodeName}: mesh index out of range {meshIndex}");
                    return;
                }

                JsonElement mesh = meshes[meshIndex];
                if (!mesh.TryGetProperty("primitives", out JsonElement primitives) ||
                    primitives.ValueKind != JsonValueKind.Array)
                {
                    unsupported.Add($"{nodeName}: mesh has no primitives array");
                    return;
                }

                int primitiveIndex = 0;
                foreach (JsonElement primitive in primitives.EnumerateArray())
                {
                    if (ReadInt(primitive, "mode", 4) != 4)
                    {
                        unsupported.Add($"{nodeName}: non-triangle primitive mode {ReadInt(primitive, "mode", 4)} ignored");
                        primitiveIndex++;
                        continue;
                    }

                    if (!primitive.TryGetProperty("attributes", out JsonElement attributes) ||
                        !attributes.TryGetProperty("POSITION", out JsonElement positionAccessorElement) ||
                        !positionAccessorElement.TryGetInt32(out int positionAccessor))
                    {
                        unsupported.Add($"{nodeName}: primitive has no POSITION accessor");
                        primitiveIndex++;
                        continue;
                    }

                    int normalAccessor = TryReadInt(attributes, "NORMAL") ?? -1;
                    int texCoordAccessor = TryReadInt(attributes, "TEXCOORD_0") ?? -1;
                    if (attributes.TryGetProperty("TANGENT", out _))
                    {
                        unsupported.Add($"{nodeName}: tangent attribute present but runtime BasicEffect path does not use tangents");
                    }

                    Vector3[] positions = ReadVec3Accessor(accessors, bufferViews, binaryChunk, positionAccessor, unsupported, $"{nodeName}:POSITION");
                    Vector3[] normals = normalAccessor >= 0
                        ? ReadVec3Accessor(accessors, bufferViews, binaryChunk, normalAccessor, unsupported, $"{nodeName}:NORMAL")
                        : GenerateFallbackNormals(positions);
                    Vector2[] uvs = texCoordAccessor >= 0
                        ? ReadVec2Accessor(accessors, bufferViews, binaryChunk, texCoordAccessor, unsupported, $"{nodeName}:TEXCOORD_0")
                        : Enumerable.Repeat(Vector2.Zero, positions.Length).ToArray();
                    int[] indices = TryReadInt(primitive, "indices") is int indexAccessor
                        ? ReadIndexAccessor(accessors, bufferViews, binaryChunk, indexAccessor, unsupported, $"{nodeName}:indices")
                        : Enumerable.Range(0, positions.Length).ToArray();

                    int count = Math.Min(positions.Length, Math.Min(normals.Length, uvs.Length));
                    if (count == 0 || indices.Length < 3)
                    {
                        primitiveIndex++;
                        continue;
                    }

                    Matrix normalMatrix = Matrix.Transpose(Matrix.Invert(world));
                    int materialIndex = TryReadInt(primitive, "material") ?? 0;
                    GltfMaterial material = (uint)materialIndex < (uint)loadedMaterials.Length
                        ? loadedMaterials[materialIndex]
                        : loadedMaterials[0];

                    BoundingBox localBounds = CalculateBounds(positions, count);
                    VertexPositionNormalTexture[] vertices = new VertexPositionNormalTexture[count];
                    for (int i = 0; i < count; i++)
                    {
                        Vector3 position = Vector3.Transform(positions[i], world);
                        Vector3 normal = Vector3.TransformNormal(normals[i], normalMatrix);
                        if (normal.LengthSquared() <= 0.000001f)
                        {
                            normal = Vector3.Up;
                        }
                        else
                        {
                            normal.Normalize();
                        }

                        Vector2 sourceUv = material.TextureCoordinateMode == TextureCoordinateMode.Generated
                            ? CalculateGeneratedTextureCoordinate(positions[i], localBounds)
                            : uvs[i];
                        vertices[i] = new VertexPositionNormalTexture(position, normal, material.TransformUv(sourceUv));
                        ExpandBounds(position);
                    }

                    int[] safeIndices = indices.Where(index => index >= 0 && index < count).ToArray();
                    if (safeIndices.Length != indices.Length)
                    {
                        unsupported.Add($"{nodeName}: dropped out-of-range indices");
                    }

                    if (world.Determinant() < 0f)
                    {
                        for (int i = 0; i + 2 < safeIndices.Length; i += 3)
                        {
                            (safeIndices[i + 1], safeIndices[i + 2]) = (safeIndices[i + 2], safeIndices[i + 1]);
                        }
                    }

                    string meshName = primitives.GetArrayLength() == 1
                        ? nodeName
                        : $"{nodeName}_prim_{primitiveIndex:00}";
                    bool materialTransparent = material.Alpha < 0.995f ||
                        material.AlphaCutout ||
                        material.AlphaBlend ||
                        material.TextureHasAlpha;
                    runtimeMeshes.Add(new StaticMesh(
                        graphicsDevice,
                        meshName,
                        vertices,
                        safeIndices,
                        material.Texture,
                        material.BaseColor,
                        alpha: materialTransparent ? MathF.Min(material.Alpha, 0.994f) : 1f,
                        specularColor: Vector3.One * MathHelper.Clamp((1f - material.Roughness) * 0.35f, 0f, 1f),
                        specularPower: material.SpecularPower,
                        emissiveColor: material.EmissiveColor));

                    if (nodeName.EndsWith("_Driveable", StringComparison.Ordinal))
                    {
                        driveableNodeNames.Add(nodeName);
                        int localTriangleIndex = 0;
                        for (int i = 0; i + 2 < safeIndices.Length; i += 3)
                        {
                            Vector3 a = vertices[safeIndices[i]].Position;
                            Vector3 b = vertices[safeIndices[i + 1]].Position;
                            Vector3 c = vertices[safeIndices[i + 2]].Position;
                            Vector3 normal = Vector3.Cross(b - a, c - a);
                            if (normal.LengthSquared() <= 0.000001f)
                            {
                                continue;
                            }

                            normal.Normalize();
                            if (normal.Y < 0f)
                            {
                                normal = -normal;
                            }

                            driveableTriangles.Add(new AuthoredDriveableTriangle(
                                a,
                                b,
                                c,
                                normal,
                                nodeName,
                                localTriangleIndex));
                            localTriangleIndex++;
                        }
                    }

                    vertexCount += vertices.Length;
                    triangleCount += safeIndices.Length / 3;
                    primitiveIndex++;
                }
            }

            void ExpandBounds(Vector3 point)
            {
                if (!hasBounds)
                {
                    bounds = new BoundingBox(point, point);
                    hasBounds = true;
                    return;
                }

                bounds = new BoundingBox(Vector3.Min(bounds.Min, point), Vector3.Max(bounds.Max, point));
            }
        }
    }

    private static ReadOnlyMemory<byte> ReadGlb(string fullPath, out JsonDocument document)
    {
        byte[] bytes = File.ReadAllBytes(fullPath);
        if (bytes.Length < 20)
        {
            throw new InvalidDataException($"GLB file is too small: {fullPath}");
        }

        uint magic = BitConverter.ToUInt32(bytes, 0);
        uint version = BitConverter.ToUInt32(bytes, 4);
        uint declaredLength = BitConverter.ToUInt32(bytes, 8);
        if (magic != GlbMagic || version != 2 || declaredLength != bytes.Length)
        {
            throw new InvalidDataException($"Invalid GLB header for authored track: {fullPath}");
        }

        byte[]? json = null;
        byte[]? binary = null;
        int offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            int chunkLength = checked((int)BitConverter.ToUInt32(bytes, offset));
            uint chunkType = BitConverter.ToUInt32(bytes, offset + 4);
            offset += 8;
            if (offset + chunkLength > bytes.Length)
            {
                throw new InvalidDataException($"Invalid GLB chunk length in {fullPath}");
            }

            byte[] chunk = new byte[chunkLength];
            Array.Copy(bytes, offset, chunk, 0, chunkLength);
            if (chunkType == JsonChunkType)
            {
                json = chunk;
            }
            else if (chunkType == BinaryChunkType)
            {
                binary = chunk;
            }

            offset += chunkLength;
        }

        if (json is null || binary is null)
        {
            throw new InvalidDataException($"Authored GLB must contain JSON and BIN chunks: {fullPath}");
        }

        string jsonText = Encoding.UTF8.GetString(json).TrimEnd('\0', ' ', '\n', '\r', '\t');
        document = JsonDocument.Parse(jsonText);
        return binary;
    }

    private static List<Texture2D?> LoadImages(
        GraphicsDevice graphicsDevice,
        JsonElement images,
        JsonElement bufferViews,
        ReadOnlyMemory<byte> binaryChunk,
        string glbPath,
        List<string> unsupported)
    {
        List<Texture2D?> result = [];
        if (images.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        string baseDirectory = Path.GetDirectoryName(glbPath) ?? Directory.GetCurrentDirectory();
        foreach (JsonElement image in images.EnumerateArray())
        {
            try
            {
                if (image.TryGetProperty("bufferView", out JsonElement bufferViewElement) &&
                    bufferViewElement.TryGetInt32(out int bufferViewIndex))
                {
                    ArraySegment<byte> data = ReadBufferView(bufferViews, binaryChunk, bufferViewIndex);
                    using MemoryStream stream = new(data.Array!, data.Offset, data.Count, writable: false);
                    result.Add(Texture2D.FromStream(graphicsDevice, stream));
                }
                else if (image.TryGetProperty("uri", out JsonElement uriElement))
                {
                    string uri = uriElement.GetString() ?? string.Empty;
                    string imagePath = Path.GetFullPath(Path.Combine(baseDirectory, uri.Replace('/', Path.DirectorySeparatorChar)));
                    using FileStream stream = File.OpenRead(imagePath);
                    result.Add(Texture2D.FromStream(graphicsDevice, stream));
                }
                else
                {
                    unsupported.Add("Image without bufferView or URI ignored.");
                    result.Add(null);
                }
            }
            catch (Exception exception)
            {
                unsupported.Add($"Image load failed: {ReadString(image, "name", "unnamed")} ({exception.Message})");
                result.Add(null);
            }
        }

        return result;
    }

    private static GltfMaterial[] LoadMaterials(
        JsonElement materials,
        JsonElement textures,
        IReadOnlyList<Texture2D?> images,
        Texture2D fallbackTexture,
        List<string> unsupported)
    {
        if (materials.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<GltfMaterial> result = [];
        foreach (JsonElement material in materials.EnumerateArray())
        {
            string name = ReadString(material, "name", $"material_{result.Count:000}");
            Vector4 baseColor = Vector4.One;
            int? baseColorTexture = null;
            TextureCoordinateMode textureCoordinateMode = TextureCoordinateMode.Uv0;
            TextureTransform textureTransform = TextureTransform.Identity;
            float roughness = 0.72f;
            float metallic = 0f;

            if (material.TryGetProperty("pbrMetallicRoughness", out JsonElement pbr))
            {
                if (pbr.TryGetProperty("baseColorFactor", out JsonElement baseColorFactor))
                {
                    baseColor = ReadVector4(baseColorFactor, Vector4.One);
                }

                if (pbr.TryGetProperty("baseColorTexture", out JsonElement baseColorTextureInfo))
                {
                    baseColorTexture = TryReadInt(baseColorTextureInfo, "index");
                    int texCoord = ReadInt(baseColorTextureInfo, "texCoord", 0);
                    if (texCoord == -1)
                    {
                        textureCoordinateMode = TextureCoordinateMode.Generated;
                        unsupported.Add($"{name}: baseColorTexture texCoord -1 imported as Blender Generated coordinates from local mesh bounds");
                    }
                    else if (texCoord != 0)
                    {
                        unsupported.Add($"{name}: baseColorTexture texCoord {texCoord} requested, runtime imports TEXCOORD_0 only");
                    }

                    textureTransform = ReadTextureTransform(baseColorTextureInfo, name, unsupported);
                }

                roughness = ReadFloat(pbr, "roughnessFactor", roughness);
                metallic = ReadFloat(pbr, "metallicFactor", metallic);
            }

            if (material.TryGetProperty("normalTexture", out _))
            {
                unsupported.Add($"{name}: normalTexture present but BasicEffect track path does not use normal maps");
            }

            if (material.TryGetProperty("occlusionTexture", out _))
            {
                unsupported.Add($"{name}: occlusionTexture ignored");
            }

            Vector3 emissive = Vector3.Zero;
            if (material.TryGetProperty("emissiveFactor", out JsonElement emissiveFactor))
            {
                emissive = ReadVector3(emissiveFactor, Vector3.Zero);
            }

            Texture2D texture = fallbackTexture;
            if (baseColorTexture is int textureIndex &&
                textures.ValueKind == JsonValueKind.Array &&
                (uint)textureIndex < (uint)textures.GetArrayLength())
            {
                int? source = TryReadInt(textures[textureIndex], "source");
                if (source is int imageIndex &&
                    (uint)imageIndex < (uint)images.Count &&
                    images[imageIndex] is Texture2D loaded)
                {
                    texture = loaded;
                }
            }

            string alphaMode = ReadString(material, "alphaMode", "OPAQUE");
            bool alphaCutout = alphaMode.Equals("MASK", StringComparison.OrdinalIgnoreCase);
            bool alphaBlend = alphaMode.Equals("BLEND", StringComparison.OrdinalIgnoreCase);
            if (!alphaMode.Equals("OPAQUE", StringComparison.OrdinalIgnoreCase) &&
                !alphaMode.Equals("MASK", StringComparison.OrdinalIgnoreCase) &&
                !alphaMode.Equals("BLEND", StringComparison.OrdinalIgnoreCase))
            {
                unsupported.Add($"{name}: unsupported alphaMode {alphaMode}");
            }

            float specularPower = MathHelper.Lerp(8f, 128f, MathF.Pow(1f - MathHelper.Clamp(roughness, 0f, 1f), 2f));
            Vector3 color = new(baseColor.X, baseColor.Y, baseColor.Z);
            if (metallic > 0.001f)
            {
                unsupported.Add($"{name}: metallicFactor approximated by BasicEffect specular only");
            }

            result.Add(new GltfMaterial(
                name,
                texture,
                color,
                MathHelper.Clamp(baseColor.W, 0f, 1f),
                emissive,
                specularPower,
                alphaCutout,
                alphaBlend,
                TextureHasAlpha(texture),
                textureCoordinateMode,
                textureTransform));
        }

        return result.ToArray();
    }

    private static bool TextureHasAlpha(Texture2D texture)
    {
        if (texture.Format != SurfaceFormat.Color)
        {
            return false;
        }

        Color[] pixels = new Color[texture.Width * texture.Height];
        texture.GetData(pixels);
        for (int i = 0; i < pixels.Length; i++)
        {
            if (pixels[i].A < byte.MaxValue)
            {
                return true;
            }
        }

        return false;
    }

    private static TextureTransform ReadTextureTransform(JsonElement textureInfo, string materialName, List<string> unsupported)
    {
        if (!textureInfo.TryGetProperty("extensions", out JsonElement extensions) ||
            !extensions.TryGetProperty("KHR_texture_transform", out JsonElement transform))
        {
            return TextureTransform.Identity;
        }

        Vector2 offset = transform.TryGetProperty("offset", out JsonElement offsetElement)
            ? ReadVector2(offsetElement, Vector2.Zero)
            : Vector2.Zero;
        Vector2 scale = transform.TryGetProperty("scale", out JsonElement scaleElement)
            ? ReadVector2(scaleElement, Vector2.One)
            : Vector2.One;
        float rotation = transform.TryGetProperty("rotation", out JsonElement rotationElement) &&
            rotationElement.TryGetSingle(out float parsedRotation)
                ? parsedRotation
                : 0f;

        if (transform.TryGetProperty("texCoord", out JsonElement texCoordElement) &&
            texCoordElement.TryGetInt32(out int texCoord) &&
            texCoord != 0)
        {
            unsupported.Add($"{materialName}: KHR_texture_transform texCoord {texCoord} requested, runtime imports TEXCOORD_0 only");
        }

        Console.WriteLine(
            $"{materialName}: applying KHR_texture_transform offset=({offset.X:0.###},{offset.Y:0.###}) " +
            $"scale=({scale.X:0.###},{scale.Y:0.###}) rotation={rotation:0.###}");
        return new TextureTransform(offset, scale, rotation);
    }

    private static Vector3[] ReadVec3Accessor(
        JsonElement accessors,
        JsonElement bufferViews,
        ReadOnlyMemory<byte> binaryChunk,
        int accessorIndex,
        List<string> unsupported,
        string label)
    {
        JsonElement accessor = accessors[accessorIndex];
        ValidateAccessor(accessor, label, "VEC3", 5126, unsupported);
        ArraySegment<byte> data = ReadAccessorBuffer(accessor, bufferViews, binaryChunk, out int stride, out int offset);
        int count = ReadInt(accessor, "count", 0);
        Vector3[] result = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            int readOffset = data.Offset + offset + i * stride;
            result[i] = new Vector3(
                BitConverter.ToSingle(data.Array!, readOffset),
                BitConverter.ToSingle(data.Array!, readOffset + 4),
                BitConverter.ToSingle(data.Array!, readOffset + 8));
        }

        return result;
    }

    private static Vector2[] ReadVec2Accessor(
        JsonElement accessors,
        JsonElement bufferViews,
        ReadOnlyMemory<byte> binaryChunk,
        int accessorIndex,
        List<string> unsupported,
        string label)
    {
        JsonElement accessor = accessors[accessorIndex];
        ValidateAccessor(accessor, label, "VEC2", 5126, unsupported);
        ArraySegment<byte> data = ReadAccessorBuffer(accessor, bufferViews, binaryChunk, out int stride, out int offset);
        int count = ReadInt(accessor, "count", 0);
        Vector2[] result = new Vector2[count];
        for (int i = 0; i < count; i++)
        {
            int readOffset = data.Offset + offset + i * stride;
            result[i] = new Vector2(
                BitConverter.ToSingle(data.Array!, readOffset),
                BitConverter.ToSingle(data.Array!, readOffset + 4));
        }

        return result;
    }

    private static int[] ReadIndexAccessor(
        JsonElement accessors,
        JsonElement bufferViews,
        ReadOnlyMemory<byte> binaryChunk,
        int accessorIndex,
        List<string> unsupported,
        string label)
    {
        JsonElement accessor = accessors[accessorIndex];
        string type = ReadString(accessor, "type", "SCALAR");
        if (!type.Equals("SCALAR", StringComparison.Ordinal))
        {
            unsupported.Add($"{label}: expected SCALAR accessor, found {type}");
        }

        int componentType = ReadInt(accessor, "componentType", 5125);
        ArraySegment<byte> data = ReadAccessorBuffer(accessor, bufferViews, binaryChunk, out int stride, out int offset);
        int count = ReadInt(accessor, "count", 0);
        int[] result = new int[count];
        for (int i = 0; i < count; i++)
        {
            int readOffset = data.Offset + offset + i * stride;
            result[i] = componentType switch
            {
                5121 => data.Array![readOffset],
                5123 => BitConverter.ToUInt16(data.Array!, readOffset),
                5125 => checked((int)BitConverter.ToUInt32(data.Array!, readOffset)),
                _ => throw new InvalidDataException($"{label}: unsupported index componentType {componentType}")
            };
        }

        return result;
    }

    private static void ValidateAccessor(JsonElement accessor, string label, string expectedType, int expectedComponentType, List<string> unsupported)
    {
        string type = ReadString(accessor, "type", expectedType);
        int componentType = ReadInt(accessor, "componentType", expectedComponentType);
        if (!type.Equals(expectedType, StringComparison.Ordinal) || componentType != expectedComponentType)
        {
            unsupported.Add($"{label}: expected {expectedType}/FLOAT, found {type}/{componentType}");
        }

        if (accessor.TryGetProperty("sparse", out _))
        {
            unsupported.Add($"{label}: sparse accessor unsupported");
        }
    }

    private static ArraySegment<byte> ReadAccessorBuffer(
        JsonElement accessor,
        JsonElement bufferViews,
        ReadOnlyMemory<byte> binaryChunk,
        out int stride,
        out int accessorOffset)
    {
        int bufferViewIndex = ReadInt(accessor, "bufferView", -1);
        if (bufferViewIndex < 0)
        {
            throw new InvalidDataException("Accessor without bufferView is not supported for authored track visuals.");
        }

        JsonElement bufferView = bufferViews[bufferViewIndex];
        int componentSize = ComponentSize(ReadInt(accessor, "componentType", 5126));
        int components = ComponentCount(ReadString(accessor, "type", "SCALAR"));
        stride = ReadInt(bufferView, "byteStride", componentSize * components);
        accessorOffset = ReadInt(accessor, "byteOffset", 0);
        return ReadBufferView(bufferViews, binaryChunk, bufferViewIndex);
    }

    private static ArraySegment<byte> ReadBufferView(JsonElement bufferViews, ReadOnlyMemory<byte> binaryChunk, int bufferViewIndex)
    {
        JsonElement bufferView = bufferViews[bufferViewIndex];
        int offset = ReadInt(bufferView, "byteOffset", 0);
        int length = ReadInt(bufferView, "byteLength", 0);
        if (offset < 0 || length < 0 || offset + length > binaryChunk.Length)
        {
            throw new InvalidDataException($"BufferView {bufferViewIndex} is outside the GLB binary chunk.");
        }

        return new ArraySegment<byte>(binaryChunk.ToArray(), offset, length);
    }

    private static Matrix ReadNodeTransform(JsonElement node, List<string> unsupported)
    {
        if (node.TryGetProperty("matrix", out JsonElement matrixElement) &&
            matrixElement.ValueKind == JsonValueKind.Array &&
            matrixElement.GetArrayLength() == 16)
        {
            float[] m = matrixElement.EnumerateArray().Select(value => value.GetSingle()).ToArray();
            return new Matrix(
                m[0], m[1], m[2], m[3],
                m[4], m[5], m[6], m[7],
                m[8], m[9], m[10], m[11],
                m[12], m[13], m[14], m[15]);
        }

        Vector3 translation = node.TryGetProperty("translation", out JsonElement t)
            ? ReadVector3(t, Vector3.Zero)
            : Vector3.Zero;
        Quaternion rotation = node.TryGetProperty("rotation", out JsonElement r)
            ? ReadQuaternion(r)
            : Quaternion.Identity;
        Vector3 scale = node.TryGetProperty("scale", out JsonElement s)
            ? ReadVector3(s, Vector3.One)
            : Vector3.One;

        if (!float.IsFinite(translation.X) || !float.IsFinite(translation.Y) || !float.IsFinite(translation.Z) ||
            !float.IsFinite(scale.X) || !float.IsFinite(scale.Y) || !float.IsFinite(scale.Z))
        {
            unsupported.Add($"{ReadString(node, "name", "node")}: invalid transform values");
        }

        return Matrix.CreateScale(scale) * Matrix.CreateFromQuaternion(rotation) * Matrix.CreateTranslation(translation);
    }

    private static int[] GetSceneRoots(JsonElement root, JsonElement nodes)
    {
        if (root.TryGetProperty("scenes", out JsonElement scenes) &&
            scenes.ValueKind == JsonValueKind.Array &&
            scenes.GetArrayLength() > 0)
        {
            int sceneIndex = ReadInt(root, "scene", 0);
            if ((uint)sceneIndex < (uint)scenes.GetArrayLength() &&
                scenes[sceneIndex].TryGetProperty("nodes", out JsonElement sceneNodes) &&
                sceneNodes.ValueKind == JsonValueKind.Array)
            {
                return sceneNodes.EnumerateArray().Select(node => node.GetInt32()).ToArray();
            }
        }

        bool[] childNodes = new bool[nodes.GetArrayLength()];
        foreach (JsonElement node in nodes.EnumerateArray())
        {
            if (node.TryGetProperty("children", out JsonElement children) && children.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement child in children.EnumerateArray())
                {
                    if (child.TryGetInt32(out int childIndex) && (uint)childIndex < (uint)childNodes.Length)
                    {
                        childNodes[childIndex] = true;
                    }
                }
            }
        }

        return Enumerable.Range(0, nodes.GetArrayLength()).Where(index => !childNodes[index]).ToArray();
    }

    private static Vector3[] GenerateFallbackNormals(Vector3[] positions)
    {
        return Enumerable.Repeat(Vector3.Up, positions.Length).ToArray();
    }

    private static List<AuthoredTrackStartMarker> ResolveStartMarkerHeadings(List<AuthoredTrackStartMarker> markers)
    {
        if (markers.Count == 0)
        {
            return markers;
        }

        AuthoredTrackStartMarker[] sorted = markers
            .GroupBy(marker => marker.GridIndex)
            .Select(group => group.OrderBy(marker => marker.Name, StringComparer.OrdinalIgnoreCase).First())
            .OrderBy(marker => marker.GridIndex)
            .ToArray();
        List<AuthoredTrackStartMarker> resolved = new(sorted.Length);
        foreach (AuthoredTrackStartMarker marker in sorted)
        {
            Vector3 before = marker.Position;
            Vector3 after = marker.Position;
            bool foundBefore = false;
            bool foundAfter = false;

            foreach (AuthoredTrackStartMarker candidate in sorted)
            {
                if (candidate.GridIndex >= marker.GridIndex ||
                    candidate.GridIndex % 2 != marker.GridIndex % 2)
                {
                    continue;
                }

                before = candidate.Position;
                foundBefore = true;
            }

            foreach (AuthoredTrackStartMarker candidate in sorted)
            {
                if (candidate.GridIndex <= marker.GridIndex ||
                    candidate.GridIndex % 2 != marker.GridIndex % 2)
                {
                    continue;
                }

                after = candidate.Position;
                foundAfter = true;
                break;
            }

            if (foundBefore && !foundAfter)
            {
                after = marker.Position;
                foundAfter = true;
            }
            else if (!foundBefore && foundAfter)
            {
                before = marker.Position;
                foundBefore = true;
            }

            if (!foundBefore || !foundAfter)
            {
                AuthoredTrackStartMarker? previous = sorted.LastOrDefault(candidate => candidate.GridIndex < marker.GridIndex);
                AuthoredTrackStartMarker? next = sorted.FirstOrDefault(candidate => candidate.GridIndex > marker.GridIndex);
                if (previous is not null)
                {
                    before = previous.Position;
                    foundBefore = true;
                }

                if (next is not null)
                {
                    after = next.Position;
                    foundAfter = true;
                }
            }

            Vector2 direction = new(after.X - before.X, after.Z - before.Z);
            if ((!foundBefore && !foundAfter) || direction.LengthSquared() <= 0.0001f)
            {
                direction = Vector2.UnitY;
            }
            else
            {
                direction.Normalize();
            }

            resolved.Add(marker with { HeadingRadians = MathF.Atan2(direction.X, direction.Y) });
        }

        return resolved;
    }

    private static bool TryParseGridMarkerIndex(string name, out int index)
    {
        string normalized = name.Trim().ToLowerInvariant();
        string[] suffixes = ["st", "nd", "rd", "th"];
        foreach (string suffix in suffixes)
        {
            if (normalized.EndsWith(suffix, StringComparison.Ordinal))
            {
                normalized = normalized[..^suffix.Length];
                break;
            }
        }

        if (int.TryParse(normalized, out index) && index > 0)
        {
            return true;
        }

        index = 0;
        return false;
    }

    private static Texture2D CreateSolidTexture(GraphicsDevice graphicsDevice, Color color)
    {
        Texture2D texture = new(graphicsDevice, 1, 1, false, SurfaceFormat.Color);
        texture.SetData([color]);
        return texture;
    }

    private static JsonElement RequiredArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement element) || element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"Authored GLB is missing required `{propertyName}` array.");
        }

        return element;
    }

    private static int? TryReadInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) && property.TryGetInt32(out int value)
            ? value
            : null;
    }

    private static int ReadInt(JsonElement element, string propertyName, int fallback)
    {
        return TryReadInt(element, propertyName) ?? fallback;
    }

    private static float ReadFloat(JsonElement element, string propertyName, float fallback)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) && property.TryGetSingle(out float value)
            ? value
            : fallback;
    }

    private static string ReadString(JsonElement element, string propertyName, string fallback)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? fallback
            : fallback;
    }

    private static Vector3 ReadVector3(JsonElement element, Vector3 fallback)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() < 3)
        {
            return fallback;
        }

        return new Vector3(element[0].GetSingle(), element[1].GetSingle(), element[2].GetSingle());
    }

    private static Vector2 ReadVector2(JsonElement element, Vector2 fallback)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() < 2)
        {
            return fallback;
        }

        return new Vector2(element[0].GetSingle(), element[1].GetSingle());
    }

    private static Vector4 ReadVector4(JsonElement element, Vector4 fallback)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() < 4)
        {
            return fallback;
        }

        return new Vector4(element[0].GetSingle(), element[1].GetSingle(), element[2].GetSingle(), element[3].GetSingle());
    }

    private static Quaternion ReadQuaternion(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() < 4)
        {
            return Quaternion.Identity;
        }

        return new Quaternion(element[0].GetSingle(), element[1].GetSingle(), element[2].GetSingle(), element[3].GetSingle());
    }

    private static int ComponentSize(int componentType)
    {
        return componentType switch
        {
            5120 or 5121 => 1,
            5122 or 5123 => 2,
            5125 or 5126 => 4,
            _ => throw new InvalidDataException($"Unsupported accessor componentType {componentType}")
        };
    }

    private static int ComponentCount(string type)
    {
        return type switch
        {
            "SCALAR" => 1,
            "VEC2" => 2,
            "VEC3" => 3,
            "VEC4" => 4,
            "MAT4" => 16,
            _ => throw new InvalidDataException($"Unsupported accessor type {type}")
        };
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.X:0.###}, {value.Y:0.###}, {value.Z:0.###})";
    }

    private static BoundingBox CalculateBounds(Vector3[] positions, int count)
    {
        if (count <= 0)
        {
            return new BoundingBox(Vector3.Zero, Vector3.Zero);
        }

        Vector3 min = positions[0];
        Vector3 max = positions[0];
        for (int i = 1; i < count; i++)
        {
            min = Vector3.Min(min, positions[i]);
            max = Vector3.Max(max, positions[i]);
        }

        return new BoundingBox(min, max);
    }

    private static Vector2 CalculateGeneratedTextureCoordinate(Vector3 localPosition, BoundingBox localBounds)
    {
        Vector3 size = localBounds.Max - localBounds.Min;
        float u = size.X > 0.000001f ? (localPosition.X - localBounds.Min.X) / size.X : 0f;
        // Blender Generated image textures use the authored X/Y bounding-box plane.
        // Blender's glTF exporter converts authored Y into negative runtime Z.
        float v = size.Z > 0.000001f ? (localBounds.Max.Z - localPosition.Z) / size.Z : 0f;
        return new Vector2(u, v);
    }

    private sealed record GltfMaterial(
        string Name,
        Texture2D Texture,
        Vector3 BaseColor,
        float Alpha,
        Vector3 EmissiveColor,
        float SpecularPower,
        bool AlphaCutout,
        bool AlphaBlend,
        bool TextureHasAlpha,
        TextureCoordinateMode TextureCoordinateMode,
        TextureTransform TextureTransform)
    {
        public float Roughness => MathHelper.Clamp(1f - MathF.Sqrt(MathHelper.Clamp((SpecularPower - 8f) / 120f, 0f, 1f)), 0f, 1f);

        public Vector2 TransformUv(Vector2 uv)
        {
            return TextureTransform.Apply(uv);
        }
    }

    private readonly record struct TextureTransform(Vector2 Offset, Vector2 Scale, float Rotation)
    {
        public static TextureTransform Identity { get; } = new(Vector2.Zero, Vector2.One, 0f);

        public Vector2 Apply(Vector2 uv)
        {
            Vector2 scaled = new(uv.X * Scale.X, uv.Y * Scale.Y);
            if (MathF.Abs(Rotation) <= 0.000001f)
            {
                return scaled + Offset;
            }

            float cos = MathF.Cos(Rotation);
            float sin = MathF.Sin(Rotation);
            return new Vector2(
                cos * scaled.X - sin * scaled.Y + Offset.X,
                sin * scaled.X + cos * scaled.Y + Offset.Y);
        }
    }

    private enum TextureCoordinateMode
    {
        Uv0,
        Generated
    }
}
