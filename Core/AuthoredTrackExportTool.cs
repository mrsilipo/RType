using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RType.Core;

internal static class AuthoredTrackExportTool
{
    private const string TrackName = "HighSpeedRing";
    private const string PackageRoot = "Assets/Tracks/HighSpeedRing";

    public static void RunFromArgs(string[] args)
    {
        string track = TrackName;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--export-authored-track", StringComparison.OrdinalIgnoreCase) &&
                i + 1 < args.Length)
            {
                track = args[i + 1];
                break;
            }
        }

        if (!Normalize(track).Equals(Normalize(TrackName), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("--export-authored-track currently supports HighSpeedRing.");
        }

        string blender = FindBlenderExecutable()
            ?? throw new FileNotFoundException("Blender executable was not found.");
        string root = Path.GetFullPath(PackageRoot);
        string editableDirectory = Path.Combine(root, "Editable");
        string runtimeDirectory = Path.Combine(root, "Runtime");
        string documentationDirectory = Path.Combine(root, "Documentation");
        string authoringPath = Path.Combine(editableDirectory, "HighSpeedRing_Authoring.blend");
        string authoredGlbPath = Path.Combine(runtimeDirectory, "HighSpeedRing_Authored.glb");
        string manifestJsonPath = Path.Combine(documentationDirectory, "HighSpeedRing_AuthoredManifest.json");
        string manifestMarkdownPath = Path.Combine(documentationDirectory, "HighSpeedRing_AuthoredManifest.md");
        string tempDirectory = Path.Combine(Path.GetTempPath(), "TrackExport", "HighSpeedRing", "BuildImport");
        string tempGlbPath = Path.Combine(tempDirectory, "HighSpeedRing_Authored.new.glb");
        string tempManifestJsonPath = Path.Combine(tempDirectory, "HighSpeedRing_AuthoredManifest.new.json");

        if (!File.Exists(authoringPath))
        {
            throw new FileNotFoundException("High Speed Ring authoring file was not found.", authoringPath);
        }

        Directory.CreateDirectory(runtimeDirectory);
        Directory.CreateDirectory(documentationDirectory);
        Directory.CreateDirectory(tempDirectory);
        TryDelete(tempGlbPath);
        TryDelete(tempManifestJsonPath);

        string scriptPath = Path.Combine(Path.GetTempPath(), $"hsr_authored_export_{Guid.NewGuid():N}.py");
        File.WriteAllText(scriptPath, BuildBlenderScript(authoringPath, tempGlbPath, authoredGlbPath, tempManifestJsonPath));
        try
        {
            RunBlender(blender, scriptPath);
        }
        finally
        {
            TryDelete(scriptPath);
        }

        if (!File.Exists(tempGlbPath))
        {
            throw new InvalidOperationException($"Authored GLB was not created: {tempGlbPath}");
        }

        AuthoredManifest manifest = JsonSerializer.Deserialize<AuthoredManifest>(
            File.ReadAllText(tempManifestJsonPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Authored manifest could not be read.");

        bool glbChanged = !FilesMatch(authoredGlbPath, tempGlbPath);
        if (glbChanged)
        {
            File.Copy(tempGlbPath, authoredGlbPath, overwrite: true);
            File.Copy(tempManifestJsonPath, manifestJsonPath, overwrite: true);
            WriteMarkdownManifest(manifestMarkdownPath, manifest);
        }
        else
        {
            Console.WriteLine("High Speed Ring authored GLB unchanged; persistent runtime package left untouched.");
        }

        Console.WriteLine("High Speed Ring authored export complete.");
        Console.WriteLine($"  Authoring blend: {authoringPath}");
        Console.WriteLine($"  Runtime GLB: {authoredGlbPath}");
        Console.WriteLine($"  Manifest: {manifestJsonPath}");
        Console.WriteLine($"  Objects: {manifest.ObjectCount}");
        Console.WriteLine($"  Visible objects: {manifest.VisibleObjectCount}");
        Console.WriteLine($"  Hidden objects omitted: {manifest.HiddenOmittedObjectCount}");
        Console.WriteLine($"  Meshes: {manifest.MeshCount}");
        Console.WriteLine($"  Vertices: {manifest.VertexCount}");
        Console.WriteLine($"  Triangles: {manifest.TriangleCount}");
        Console.WriteLine($"  Materials: {manifest.MaterialCount}");
        Console.WriteLine($"  Textures: {manifest.TextureCount}");
        Console.WriteLine($"  Missing textures: {manifest.MissingTextures.Length}");
        Console.WriteLine($"  Duplicate authored IDs: {manifest.DuplicateAuthoredIds.Length}");
        Console.WriteLine($"  Validation passed: {manifest.ValidationPassed}");
        Console.WriteLine($"  GLB changed: {glbChanged}");
    }

    private static void RunBlender(string blender, string scriptPath)
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
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Console.Write(output);
        if (!string.IsNullOrWhiteSpace(error))
        {
            Console.Error.Write(error);
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Blender authored export failed with exit code {process.ExitCode}.");
        }
    }

    private static string BuildBlenderScript(string authoringPath, string authoredGlbPath, string manifestRuntimeGlbPath, string manifestJsonPath)
    {
        string safeAuthoring = EscapePythonPath(authoringPath);
        string safeGlb = EscapePythonPath(authoredGlbPath);
        string safeManifestRuntimeGlb = EscapePythonPath(manifestRuntimeGlbPath);
        string safeManifest = EscapePythonPath(manifestJsonPath);
        return $$"""
import bpy
import json
import os
import uuid
import re
import math
import mathutils

authoring_path = r"{{safeAuthoring}}"
authored_glb_path = r"{{safeGlb}}"
manifest_runtime_glb_path = r"{{safeManifestRuntimeGlb}}"
manifest_json_path = r"{{safeManifest}}"
package_root = os.path.abspath(os.path.join(os.path.dirname(authoring_path), ".."))

bpy.ops.wm.open_mainfile(filepath=authoring_path)

warnings = []
objects = list(bpy.context.scene.objects)
ids_added = 0
for obj in objects:
    if obj.type in {"MESH", "CURVE", "SURFACE", "FONT", "META", "EMPTY", "LIGHT", "CAMERA"} and not obj.get("authoredId"):
        obj["authoredId"] = "hsr-authored-" + uuid.uuid4().hex
        ids_added += 1

if ids_added:
    bpy.ops.wm.save_as_mainfile(filepath=authoring_path)

renderable_types = {"MESH", "CURVE", "SURFACE", "FONT", "META"}
visible_objects = [obj for obj in objects if obj.visible_get()]
hidden_omitted_objects = [obj.name for obj in objects if not obj.visible_get()]
mesh_objects = [obj for obj in visible_objects if obj.type == "MESH"]
renderable_objects = [obj for obj in visible_objects if obj.type in renderable_types]
has_authored_track_asphalt = any(obj.type == "MESH" and obj.name.lower().startswith("track_asphalt") for obj in visible_objects)
replaced_visual_objects = []

def is_replaced_generated_visual(obj):
    if has_authored_track_asphalt and obj.name.lower() == "road":
        return True
    return False

export_renderable_objects = [obj for obj in renderable_objects if not is_replaced_generated_visual(obj)]
for obj in renderable_objects:
    if is_replaced_generated_visual(obj):
        replaced_visual_objects.append(obj.name)

source_mesh_datablock_count = len(bpy.data.meshes)
vertex_count = 0
triangle_count = 0
invalid_transforms = []
depsgraph = bpy.context.evaluated_depsgraph_get()
evaluated_modifier_objects = []
converted_curve_objects = []
omitted_empty_geometry = []
fallback_base_mesh_objects = []
manual_curve_array_objects = []

def bezier_point(p0, p1, p2, p3, t):
    u = 1.0 - t
    return (u * u * u * p0) + (3.0 * u * u * t * p1) + (3.0 * u * t * t * p2) + (t * t * t * p3)

def sample_curve_world(curve_obj, steps_per_segment=32):
    samples = []
    curve = curve_obj.data
    for spline in curve.splines:
        if spline.type == "BEZIER":
            points = list(spline.bezier_points)
            segment_count = len(points) if spline.use_cyclic_u else max(0, len(points) - 1)
            for index in range(segment_count):
                a = points[index]
                b = points[(index + 1) % len(points)]
                for step in range(steps_per_segment):
                    if samples and step == 0:
                        continue
                    t = step / float(steps_per_segment)
                    local = bezier_point(a.co, a.handle_right, b.handle_left, b.co, t)
                    samples.append(curve_obj.matrix_world @ local)
            if points:
                samples.append(curve_obj.matrix_world @ points[-1].co)
        elif spline.type == "POLY":
            for point in spline.points:
                samples.append(curve_obj.matrix_world @ mathutils.Vector((point.co.x, point.co.y, point.co.z)))
    return samples

def curve_length(samples):
    return sum((samples[i] - samples[i - 1]).length for i in range(1, len(samples)))

def point_and_tangent_at_distance(samples, distance):
    if len(samples) < 2:
        return None, None
    travelled = 0.0
    for index in range(1, len(samples)):
        start = samples[index - 1]
        end = samples[index]
        segment = end - start
        length = segment.length
        if length <= 0.000001:
            continue
        if travelled + length >= distance:
            factor = (distance - travelled) / length
            point = start.lerp(end, factor)
            tangent = segment.normalized()
            return point, tangent
        travelled += length
    tangent = (samples[-1] - samples[-2]).normalized()
    return samples[-1], tangent

def get_socket_value(modifier, socket_name, default_value):
    try:
        socket = getattr(modifier.properties.inputs, socket_name)
        if hasattr(socket, "value"):
            return socket.value
        if "value" in socket:
            return socket["value"]
    except Exception:
        pass
    return default_value

def make_curve_array_mesh(obj):
    array_modifier = None
    curve_modifier = None
    for modifier in obj.modifiers:
        if modifier.type == "NODES" and modifier.name.lower() == "array":
            array_modifier = modifier
        if modifier.type == "CURVE" and modifier.object is not None:
            curve_modifier = modifier
    if array_modifier is None or obj.type != "MESH" or len(obj.data.polygons) == 0:
        return None

    curve_obj = curve_modifier.object if curve_modifier is not None else None
    socket_curve_obj = get_socket_value(array_modifier, "Socket_27", None)
    if socket_curve_obj is not None:
        curve_obj = socket_curve_obj
    if curve_obj is None or curve_obj.type != "CURVE":
        return None

    samples = sample_curve_world(curve_obj)
    total_length = curve_length(samples)
    spacing = float(get_socket_value(array_modifier, "Socket_34", max(0.1, obj.dimensions.y)))
    if spacing <= 0.0001 or total_length <= 0.0001:
        return None
    exclude_first = bool(get_socket_value(array_modifier, "Socket_22", False))
    exclude_last = bool(get_socket_value(array_modifier, "Socket_46", False))

    distances = []
    d = spacing if exclude_first else 0.0
    end_distance = max(0.0, total_length - (spacing if exclude_last else 0.0))
    while d <= end_distance + 0.0001:
        distances.append(d)
        d += spacing
    if not distances:
        return None

    base_mesh = obj.data
    vertices = []
    faces = []
    material_indices = []
    up = mathutils.Vector((0.0, 0.0, 1.0))
    for distance in distances:
        point, tangent = point_and_tangent_at_distance(samples, distance)
        if point is None or tangent is None:
            continue
        tangent.z = 0.0
        if tangent.length <= 0.0001:
            continue
        tangent.normalize()
        x_axis = tangent.cross(up)
        if x_axis.length <= 0.0001:
            x_axis = mathutils.Vector((1.0, 0.0, 0.0))
        x_axis.normalize()
        y_axis = tangent
        z_axis = up
        transform = mathutils.Matrix((
            (x_axis.x, y_axis.x, z_axis.x, point.x),
            (x_axis.y, y_axis.y, z_axis.y, point.y),
            (x_axis.z, y_axis.z, z_axis.z, point.z),
            (0.0, 0.0, 0.0, 1.0),
        ))
        base_index = len(vertices)
        for vertex in base_mesh.vertices:
            vertices.append(transform @ vertex.co)
        for polygon in base_mesh.polygons:
            faces.append([base_index + vertex_index for vertex_index in polygon.vertices])
            material_indices.append(polygon.material_index)

    if not faces:
        return None
    mesh = bpy.data.meshes.new(obj.name + "_CurveArrayBake")
    mesh.from_pydata([vertex[:] for vertex in vertices], [], faces)
    mesh.update(calc_edges=True)
    for material in base_mesh.materials:
        mesh.materials.append(material)
    for polygon, material_index in zip(mesh.polygons, material_indices):
        polygon.material_index = material_index
    manual_curve_array_objects.append(obj.name)
    return mesh

for obj in export_renderable_objects:
    matrix_values = [v for row in obj.matrix_world for v in row]
    if any((not isinstance(v, float)) or v != v or abs(v) == float("inf") for v in matrix_values):
        invalid_transforms.append(obj.name)
    if obj.type == "MESH" and len(obj.modifiers) > 0:
        evaluated_modifier_objects.append(obj.name)
    if obj.type != "MESH":
        converted_curve_objects.append(obj.name)
    try:
        eval_obj = obj.evaluated_get(depsgraph)
        mesh = bpy.data.meshes.new_from_object(eval_obj, depsgraph=depsgraph, preserve_all_data_layers=True)
        if len(mesh.polygons) == 0:
            manual_mesh = make_curve_array_mesh(obj)
            if manual_mesh is not None:
                vertex_count += len(manual_mesh.vertices)
                triangle_count += sum(max(1, len(poly.vertices) - 2) for poly in manual_mesh.polygons)
                bpy.data.meshes.remove(manual_mesh)
            elif obj.type == "MESH" and len(obj.data.polygons) > 0:
                fallback_base_mesh_objects.append(obj.name)
                vertex_count += len(obj.data.vertices)
                triangle_count += sum(max(1, len(poly.vertices) - 2) for poly in obj.data.polygons)
            else:
                omitted_empty_geometry.append(obj.name)
        else:
            vertex_count += len(mesh.vertices)
            triangle_count += sum(max(1, len(poly.vertices) - 2) for poly in mesh.polygons)
        bpy.data.meshes.remove(mesh)
    except Exception as ex:
        omitted_empty_geometry.append(obj.name + ": " + str(ex))

exported_materials = set()
for obj in export_renderable_objects:
    data = getattr(obj, "data", None)
    if data is not None and hasattr(data, "materials"):
        for material in data.materials:
            if material is not None:
                exported_materials.add(material)

texture_paths = set()
missing_textures = []
unsupported_material_features = []
for material in exported_materials:
    if not material.use_nodes:
        continue
    for node in material.node_tree.nodes:
        if node.bl_idname == "ShaderNodeTexImage" and node.image:
            path = bpy.path.abspath(node.image.filepath)
            if path:
                texture_paths.add(os.path.relpath(path, package_root).replace(os.sep, "/"))
                if not os.path.exists(path):
                    missing_textures.append(node.image.filepath)
        if node.bl_idname not in {
            "ShaderNodeOutputMaterial",
            "ShaderNodeBsdfPrincipled",
            "ShaderNodeTexImage",
            "ShaderNodeNormalMap",
            "ShaderNodeBump",
            "ShaderNodeMapping",
            "ShaderNodeTexCoord",
            "ShaderNodeValue",
            "ShaderNodeRGB",
            "ShaderNodeMix",
            "ShaderNodeMixRGB",
            "ShaderNodeMath",
            "ShaderNodeInvert",
            "ShaderNodeSeparateColor",
            "ShaderNodeCombineColor",
        }:
            unsupported_material_features.append(material.name + ":" + node.bl_idname)

authored_ids = {}
duplicate_authored_ids = []
object_entries = []
export_renderable_object_names = set(obj.name for obj in export_renderable_objects)
for obj in objects:
    authored_id = obj.get("authoredId", "")
    if authored_id:
        if authored_id in authored_ids:
            duplicate_authored_ids.append(authored_id)
        authored_ids[authored_id] = obj.name
    object_entries.append({
        "name": obj.name,
        "type": obj.type,
        "visible": obj.visible_get(),
        "exported": obj.name in export_renderable_object_names,
        "authoredId": authored_id,
        "sourceExportId": obj.get("exportId", ""),
        "category": obj.get("category", ""),
        "collectionNames": [collection.name for collection in obj.users_collection],
        "location": [round(v, 6) for v in obj.location],
        "rotationEuler": [round(v, 6) for v in obj.rotation_euler],
        "scale": [round(v, 6) for v in obj.scale],
    })

collection_names = sorted({collection.name for collection in bpy.data.collections})

def clean_export_name(name):
    return re.sub(r"^__AuthoringOriginal_\d+_", "", name)

original_object_names = {}
for index, obj in enumerate(objects):
    original_object_names[obj] = obj.name
    obj.name = "__AuthoringOriginal_%04d_%s" % (index, obj.name)

export_collection = bpy.data.collections.new("HighSpeedRing_RuntimeExport_Evaluated")
bpy.context.scene.collection.children.link(export_collection)
export_objects = []
for obj in export_renderable_objects:
    try:
        original_name = original_object_names[obj]
        eval_obj = obj.evaluated_get(depsgraph)
        mesh = bpy.data.meshes.new_from_object(eval_obj, depsgraph=depsgraph, preserve_all_data_layers=True)
        manual_baked_world_space = False
        if len(mesh.polygons) == 0:
            manual_mesh = make_curve_array_mesh(obj)
            if manual_mesh is not None:
                bpy.data.meshes.remove(mesh)
                mesh = manual_mesh
                manual_baked_world_space = True
            elif obj.type == "MESH" and len(obj.data.polygons) > 0:
                fallback_base_mesh_objects.append(obj.name)
                bpy.data.meshes.remove(mesh)
                mesh = obj.data.copy()
                mesh.name = obj.data.name
            else:
                omitted_empty_geometry.append(original_name)
                bpy.data.meshes.remove(mesh)
                continue
        mesh.name = (obj.data.name if obj.data else original_name + "_Mesh")
        export_obj = bpy.data.objects.new(original_name, mesh)
        export_obj.matrix_world = mathutils.Matrix.Identity(4) if manual_baked_world_space else obj.matrix_world.copy()
        if len(mesh.materials) == 0:
            for material in obj.data.materials if hasattr(obj.data, "materials") else []:
                mesh.materials.append(material)
        for key in obj.keys():
            if key != "_RNA_UI":
                export_obj[key] = obj[key]
        export_obj["sourceObjectType"] = obj.type
        export_obj["sourceCollectionNames"] = [collection.name for collection in obj.users_collection]
        if manual_baked_world_space:
            export_obj["manualWorldSpaceBake"] = True
        export_collection.objects.link(export_obj)
        export_objects.append(export_obj)
    except Exception as ex:
        omitted_empty_geometry.append(obj.name + ": " + str(ex))

for obj in visible_objects:
    if obj.type == "LIGHT":
        original_name = original_object_names[obj]
        light_data = obj.data.copy() if obj.data else None
        light_obj = bpy.data.objects.new(original_name, light_data)
        light_obj.matrix_world = obj.matrix_world.copy()
        for key in obj.keys():
            if key != "_RNA_UI":
                light_obj[key] = obj[key]
        light_obj["sourceObjectType"] = obj.type
        light_obj["sourceCollectionNames"] = [collection.name for collection in obj.users_collection]
        export_collection.objects.link(light_obj)
        export_objects.append(light_obj)

os.makedirs(os.path.dirname(authored_glb_path), exist_ok=True)
for scene_obj in bpy.context.scene.objects:
    scene_obj.select_set(False)
for obj in export_objects:
    obj.select_set(True)
if export_objects:
    bpy.context.view_layer.objects.active = export_objects[0]
bpy.ops.export_scene.gltf(
    filepath=authored_glb_path,
    export_format="GLB",
    export_extras=True,
    export_cameras=False,
    export_lights=True,
    export_apply=False,
    use_selection=True,
)

glb_valid = False
glb_size = 0
if os.path.exists(authored_glb_path):
    glb_size = os.path.getsize(authored_glb_path)
    with open(authored_glb_path, "rb") as f:
        header = f.read(12)
    glb_valid = len(header) == 12 and header[0:4] == b"glTF" and int.from_bytes(header[4:8], "little") == 2

bounds_min = [0, 0, 0]
bounds_max = [0, 0, 0]
if export_renderable_objects:
    mins = []
    maxs = []
    for obj in export_renderable_objects:
        if obj.type == "MESH":
            corners = [obj.matrix_world @ __import__("mathutils").Vector(corner) for corner in obj.bound_box]
            mins.extend(corners)
            maxs.extend(corners)
    if mins:
        bounds_min = [round(min(v[i] for v in mins), 6) for i in range(3)]
        bounds_max = [round(max(v[i] for v in maxs), 6) for i in range(3)]

validation_passed = glb_valid and not missing_textures and not invalid_transforms and not duplicate_authored_ids
manifest = {
    "exportDate": __import__("datetime").datetime.now().astimezone().isoformat(),
    "authoringBlend": authoring_path,
    "runtimeGlb": manifest_runtime_glb_path,
    "objectCount": len(objects),
    "visibleObjectCount": len(visible_objects),
    "hiddenOmittedObjectCount": len(hidden_omitted_objects),
    "hiddenOmittedObjects": sorted(hidden_omitted_objects),
    "meshObjectCount": len(mesh_objects),
    "renderableObjectCount": len(renderable_objects),
    "exportedObjectCount": len(export_objects),
    "meshCount": source_mesh_datablock_count,
    "vertexCount": vertex_count,
    "triangleCount": triangle_count,
    "materialCount": len(exported_materials),
    "textureCount": len(texture_paths),
    "texturePaths": sorted(texture_paths),
    "collections": collection_names,
    "boundsMin": bounds_min,
    "boundsMax": bounds_max,
    "authoredIdsAdded": ids_added,
    "duplicateAuthoredIds": sorted(set(duplicate_authored_ids)),
    "invalidTransforms": invalid_transforms,
    "evaluatedModifierObjects": sorted(set(clean_export_name(name) for name in evaluated_modifier_objects)),
    "convertedCurveObjects": sorted(set(clean_export_name(name) for name in converted_curve_objects)),
    "omittedEmptyGeometry": sorted(set(clean_export_name(name) for name in omitted_empty_geometry)),
    "fallbackBaseMeshObjects": sorted(set(clean_export_name(name) for name in fallback_base_mesh_objects)),
    "manualCurveArrayObjects": sorted(set(clean_export_name(name) for name in manual_curve_array_objects)),
    "replacedVisualObjects": sorted(set(clean_export_name(name) for name in replaced_visual_objects)),
    "missingTextures": sorted(set(missing_textures)),
    "unsupportedMaterialFeatures": sorted(set(unsupported_material_features)),
    "glbValid": glb_valid,
    "glbSizeBytes": glb_size,
    "validationPassed": validation_passed,
    "objects": object_entries,
    "notes": [
        "Authored Blender scene is the visual source of truth.",
        "Procedural bootstrap object parity is not required.",
        "Deleted, moved, joined, split, replaced and newly authored objects are valid.",
        "Only objects visible in the saved Blender view layer are exported; hidden objects remain in the authoring blend but are omitted from the runtime GLB.",
        "GLB is exported from a temporary evaluated mesh scene with Blender glTF exporter extras enabled.",
        "Modifiers and curve/font/surface objects are baked only into the runtime GLB; the authoring blend remains editable.",
        "If a mesh object's evaluated modifier stack produces no faces, the exporter falls back to the base mesh and reports it.",
        "Distance-based curve array modifiers that evaluate empty in headless Blender are manually baked along their curve path.",
        "When Track_Asphalt* exists, the previous generated Road object is treated as replaced and is not exported to the runtime GLB.",
    ],
}
os.makedirs(os.path.dirname(manifest_json_path), exist_ok=True)
with open(manifest_json_path, "w", encoding="utf-8") as f:
    json.dump(manifest, f, indent=2)

print("AUTHORED_EXPORT objects=%d visibleObjects=%d hiddenOmitted=%d meshObjects=%d renderables=%d exportedObjects=%d meshes=%d vertices=%d triangles=%d materials=%d textures=%d idsAdded=%d modifiersBaked=%d curvesConverted=%d manualCurveArrays=%d baseMeshFallbacks=%d emptyOmitted=%d glbValid=%s validationPassed=%s" % (
    len(objects), len(visible_objects), len(hidden_omitted_objects), len(mesh_objects), len(renderable_objects), len(export_objects), source_mesh_datablock_count, vertex_count, triangle_count, len(exported_materials), len(texture_paths), ids_added, len(set(evaluated_modifier_objects)), len(set(converted_curve_objects)), len(set(manual_curve_array_objects)), len(set(fallback_base_mesh_objects)), len(set(omitted_empty_geometry)), glb_valid, validation_passed
))
""";
    }

    private static void WriteMarkdownManifest(string manifestMarkdownPath, AuthoredManifest manifest)
    {
        StringBuilder builder = new();
        builder.AppendLine("# High Speed Ring Authored Manifest");
        builder.AppendLine();
        builder.AppendLine($"- Export date: `{manifest.ExportDate}`");
        builder.AppendLine($"- Authoring blend: `{manifest.AuthoringBlend}`");
        builder.AppendLine($"- Runtime GLB: `{manifest.RuntimeGlb}`");
        builder.AppendLine($"- GLB valid: `{manifest.GlbValid}`");
        builder.AppendLine($"- GLB size bytes: `{manifest.GlbSizeBytes}`");
        builder.AppendLine($"- Validation passed: `{manifest.ValidationPassed}`");
        builder.AppendLine();
        builder.AppendLine("## Authored Scene");
        builder.AppendLine();
        builder.AppendLine($"- Object count: `{manifest.ObjectCount}`");
        builder.AppendLine($"- Visible object count: `{manifest.VisibleObjectCount}`");
        builder.AppendLine($"- Hidden objects omitted: `{manifest.HiddenOmittedObjectCount}`");
        builder.AppendLine($"- Mesh object count: `{manifest.MeshObjectCount}`");
        builder.AppendLine($"- Renderable object count: `{manifest.RenderableObjectCount}`");
        builder.AppendLine($"- Exported object count: `{manifest.ExportedObjectCount}`");
        builder.AppendLine($"- Mesh datablock count: `{manifest.MeshCount}`");
        builder.AppendLine($"- Vertex count: `{manifest.VertexCount}`");
        builder.AppendLine($"- Triangle count: `{manifest.TriangleCount}`");
        builder.AppendLine($"- Material count: `{manifest.MaterialCount}`");
        builder.AppendLine($"- Texture count: `{manifest.TextureCount}`");
        builder.AppendLine($"- Bounds min: `{FormatVector(manifest.BoundsMin)}`");
        builder.AppendLine($"- Bounds max: `{FormatVector(manifest.BoundsMax)}`");
        builder.AppendLine($"- Authored IDs added this export: `{manifest.AuthoredIdsAdded}`");
        builder.AppendLine();
        builder.AppendLine("## Textures");
        foreach (string texturePath in manifest.TexturePaths)
        {
            builder.AppendLine($"- `{texturePath}`");
        }

        builder.AppendLine();
        builder.AppendLine("## Collections");
        foreach (string collection in manifest.Collections)
        {
            builder.AppendLine($"- {collection}");
        }

        builder.AppendLine();
        builder.AppendLine("## Validation Notes");
        string[] missingTextures = manifest.MissingTextures ?? Array.Empty<string>();
        string[] invalidTransforms = manifest.InvalidTransforms ?? Array.Empty<string>();
        string[] duplicateAuthoredIds = manifest.DuplicateAuthoredIds ?? Array.Empty<string>();
        string[] unsupportedMaterialFeatures = manifest.UnsupportedMaterialFeatures ?? Array.Empty<string>();
        string[] evaluatedModifierObjects = manifest.EvaluatedModifierObjects ?? Array.Empty<string>();
        string[] convertedCurveObjects = manifest.ConvertedCurveObjects ?? Array.Empty<string>();
        string[] omittedEmptyGeometry = manifest.OmittedEmptyGeometry ?? Array.Empty<string>();
        string[] fallbackBaseMeshObjects = manifest.FallbackBaseMeshObjects ?? Array.Empty<string>();
        string[] manualCurveArrayObjects = manifest.ManualCurveArrayObjects ?? Array.Empty<string>();
        string[] replacedVisualObjects = manifest.ReplacedVisualObjects ?? Array.Empty<string>();
        string[] hiddenOmittedObjects = manifest.HiddenOmittedObjects ?? Array.Empty<string>();
        builder.AppendLine($"- Missing textures: `{missingTextures.Length}`");
        builder.AppendLine($"- Invalid transforms: `{invalidTransforms.Length}`");
        builder.AppendLine($"- Duplicate authored IDs: `{duplicateAuthoredIds.Length}`");
        builder.AppendLine($"- Unsupported/non-core material nodes observed: `{unsupportedMaterialFeatures.Length}`");
        builder.AppendLine($"- Modifier objects baked: `{evaluatedModifierObjects.Length}`");
        builder.AppendLine($"- Curve/font/surface objects converted: `{convertedCurveObjects.Length}`");
        builder.AppendLine($"- Manual curve arrays baked: `{manualCurveArrayObjects.Length}`");
        builder.AppendLine($"- Replaced generated visual objects skipped: `{replacedVisualObjects.Length}`");
        builder.AppendLine($"- Hidden Blender objects omitted: `{hiddenOmittedObjects.Length}`");
        builder.AppendLine($"- Base mesh fallbacks: `{fallbackBaseMeshObjects.Length}`");
        builder.AppendLine($"- Empty/unexportable geometry omitted: `{omittedEmptyGeometry.Length}`");
        foreach (string warning in missingTextures
            .Concat(invalidTransforms)
            .Concat(duplicateAuthoredIds)
            .Concat(evaluatedModifierObjects.Select(name => "Modifier baked: " + name))
            .Concat(convertedCurveObjects.Select(name => "Converted to mesh: " + name))
            .Concat(manualCurveArrayObjects.Select(name => "Manual curve array baked: " + name))
            .Concat(replacedVisualObjects.Select(name => "Replaced generated visual skipped: " + name))
            .Concat(hiddenOmittedObjects.Select(name => "Hidden object omitted: " + name))
            .Concat(fallbackBaseMeshObjects.Select(name => "Base mesh fallback: " + name))
            .Concat(omittedEmptyGeometry.Select(name => "Omitted empty geometry: " + name))
            .Take(80))
        {
            builder.AppendLine($"- Warning: {warning}");
        }

        builder.AppendLine();
        builder.AppendLine("## Workflow");
        builder.AppendLine();
        foreach (string note in manifest.Notes)
        {
            builder.AppendLine($"- {note}");
        }

        File.WriteAllText(manifestMarkdownPath, builder.ToString());
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
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string Normalize(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    private static string EscapePythonPath(string path)
    {
        return Path.GetFullPath(path).Replace("\\", "\\\\");
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    private static bool FilesMatch(string left, string right)
    {
        if (!File.Exists(left) || !File.Exists(right))
        {
            return false;
        }

        FileInfo leftInfo = new(left);
        FileInfo rightInfo = new(right);
        if (leftInfo.Length != rightInfo.Length)
        {
            return false;
        }

        using FileStream leftStream = File.OpenRead(left);
        using FileStream rightStream = File.OpenRead(right);
        byte[] leftHash = SHA256.HashData(leftStream);
        byte[] rightHash = SHA256.HashData(rightStream);
        return leftHash.SequenceEqual(rightHash);
    }

    private static string FormatVector(IReadOnlyList<float> values)
    {
        return values.Count >= 3 ? $"{values[0]:0.###}, {values[1]:0.###}, {values[2]:0.###}" : string.Empty;
    }

    private sealed record AuthoredManifest(
        string ExportDate,
        string AuthoringBlend,
        string RuntimeGlb,
        int ObjectCount,
        int VisibleObjectCount,
        int HiddenOmittedObjectCount,
        string[] HiddenOmittedObjects,
        int MeshObjectCount,
        int RenderableObjectCount,
        int ExportedObjectCount,
        int MeshCount,
        int VertexCount,
        int TriangleCount,
        int MaterialCount,
        int TextureCount,
        string[] TexturePaths,
        string[] Collections,
        float[] BoundsMin,
        float[] BoundsMax,
        int AuthoredIdsAdded,
        string[] DuplicateAuthoredIds,
        string[] InvalidTransforms,
        string[] EvaluatedModifierObjects,
        string[] ConvertedCurveObjects,
        string[] OmittedEmptyGeometry,
        string[] FallbackBaseMeshObjects,
        string[] ManualCurveArrayObjects,
        string[] ReplacedVisualObjects,
        string[] MissingTextures,
        string[] UnsupportedMaterialFeatures,
        bool GlbValid,
        long GlbSizeBytes,
        bool ValidationPassed,
        AuthoredObjectEntry[] Objects,
        string[] Notes);

    private sealed record AuthoredObjectEntry(
        string Name,
        string Type,
        string AuthoredId,
        string SourceExportId,
        string Category,
        string[] CollectionNames,
        float[] Location,
        float[] RotationEuler,
        float[] Scale);
}
