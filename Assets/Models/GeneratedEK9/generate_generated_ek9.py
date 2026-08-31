import math
import bpy

LENGTH = 4.185
WIDTH = 1.695
HEIGHT = 1.360
WHEELBASE = 2.620
TRACK = 1.480
FRONT_WEIGHT = 0.620
TYRE_RADIUS = 0.298
TYRE_WIDTH = 0.195
GROUND_CLEARANCE = 0.135

FRONT_Z = WHEELBASE * (1.0 - FRONT_WEIGHT)
REAR_Z = -WHEELBASE * FRONT_WEIGHT
HALF_TRACK = TRACK * 0.5


def b_loc(phys):
    return (phys[2], phys[0], phys[1])


def b_scale(phys):
    return (phys[2], phys[0], phys[1])


def mat(name, color):
    material = bpy.data.materials.new(name)
    material.diffuse_color = color
    return material


MATS = {
    "BodyPaint": mat("BodyPaint", (0.92, 0.90, 0.82, 1.0)),
    "Glass": mat("Glass", (0.06, 0.10, 0.13, 0.50)),
    "BlackPlastic": mat("BlackPlastic", (0.018, 0.018, 0.020, 1.0)),
    "TyreRubber": mat("TyreRubber", (0.025, 0.025, 0.023, 1.0)),
    "WheelMetal": mat("WheelMetal", (0.88, 0.86, 0.78, 1.0)),
    "LightsFront": mat("LightsFront", (0.88, 0.96, 1.00, 0.80)),
    "LightsRear": mat("LightsRear", (0.82, 0.01, 0.01, 0.90)),
    "InteriorDark": mat("InteriorDark", (0.035, 0.035, 0.038, 1.0)),
}


def clean_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()


def cube_phys(name, loc, scale, material):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=b_loc(loc))
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = b_scale(scale)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(material)
    return obj


def cube_blender(name, loc, scale, material):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(material)
    return obj


def wheel_phys(name, loc, steerable):
    root = bpy.data.objects.new(f"{name}_STEER" if steerable else f"{name}_ROOT", None)
    root.empty_display_type = "ARROWS"
    root.empty_display_size = 0.25
    root.location = b_loc(loc)
    bpy.context.collection.objects.link(root)

    roll = bpy.data.objects.new(f"{name}_ROLL", None)
    roll.empty_display_type = "SINGLE_ARROW"
    roll.empty_display_size = 0.22
    roll.parent = root
    bpy.context.collection.objects.link(roll)

    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=TYRE_RADIUS, depth=TYRE_WIDTH, location=b_loc(loc), rotation=(math.pi / 2.0, 0.0, 0.0))
    tyre = bpy.context.object
    tyre.name = f"TYRE_{name}"
    tyre.parent = roll
    tyre.matrix_parent_inverse = roll.matrix_world.inverted()
    tyre.data.materials.append(MATS["TyreRubber"])

    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=TYRE_RADIUS * 0.56, depth=TYRE_WIDTH * 1.06, location=b_loc(loc), rotation=(math.pi / 2.0, 0.0, 0.0))
    rim = bpy.context.object
    rim.name = f"WHEEL_{name}"
    rim.parent = roll
    rim.matrix_parent_inverse = roll.matrix_world.inverted()
    rim.data.materials.append(MATS["WheelMetal"])
    return root


def empty_phys(name, loc):
    obj = bpy.data.objects.new(name, None)
    obj.empty_display_type = "SPHERE"
    obj.empty_display_size = 0.12
    obj.location = b_loc(loc)
    bpy.context.collection.objects.link(obj)
    return obj


clean_scene()
root = bpy.data.objects.new("CAR_ROOT", None)
root.empty_display_type = "PLAIN_AXES"
bpy.context.collection.objects.link(root)

parts = [
    cube_phys("BODY_main_shell", (0, GROUND_CLEARANCE + 0.30, -0.12), (WIDTH, 0.50, LENGTH * 0.86), MATS["BodyPaint"]),
    cube_phys("HOOD", (0, GROUND_CLEARANCE + 0.55, 1.05), (WIDTH * 0.86, 0.12, 1.10), MATS["BodyPaint"]),
    cube_phys("FRONT_BUMPER", (0, GROUND_CLEARANCE + 0.25, 1.78), (WIDTH * 0.94, 0.34, 0.34), MATS["BodyPaint"]),
    cube_phys("REAR_BUMPER", (0, GROUND_CLEARANCE + 0.27, -1.95), (WIDTH * 0.94, 0.36, 0.28), MATS["BodyPaint"]),
    cube_phys("ROOF", (0, GROUND_CLEARANCE + 1.03, -0.40), (WIDTH * 0.72, 0.18, 1.54), MATS["BodyPaint"]),
    cube_phys("GLASS_FRONT", (0, GROUND_CLEARANCE + 0.83, 0.40), (WIDTH * 0.70, 0.10, 0.62), MATS["Glass"]),
    cube_phys("GLASS_REAR", (0, GROUND_CLEARANCE + 0.78, -1.26), (WIDTH * 0.68, 0.10, 0.46), MATS["Glass"]),
    cube_phys("GLASS_LEFT", (-HALF_TRACK, GROUND_CLEARANCE + 0.78, -0.42), (0.08, 0.36, 1.34), MATS["Glass"]),
    cube_phys("GLASS_RIGHT", (HALF_TRACK, GROUND_CLEARANCE + 0.78, -0.42), (0.08, 0.36, 1.34), MATS["Glass"]),
    cube_phys("LIGHT_FRONT_L", (-0.42, GROUND_CLEARANCE + 0.43, 1.98), (0.44, 0.14, 0.08), MATS["LightsFront"]),
    cube_phys("LIGHT_FRONT_R", (0.42, GROUND_CLEARANCE + 0.43, 1.98), (0.44, 0.14, 0.08), MATS["LightsFront"]),
    cube_phys("LIGHT_REAR_L", (-0.50, GROUND_CLEARANCE + 0.48, -2.07), (0.36, 0.16, 0.07), MATS["LightsRear"]),
    cube_phys("LIGHT_REAR_R", (0.50, GROUND_CLEARANCE + 0.48, -2.07), (0.36, 0.16, 0.07), MATS["LightsRear"]),
    cube_phys("REAR_WING", (0, GROUND_CLEARANCE + 1.10, -1.83), (WIDTH * 0.76, 0.08, 0.24), MATS["BlackPlastic"]),
    cube_phys("INTERIOR_DARK", (0, GROUND_CLEARANCE + 0.62, -0.40), (WIDTH * 0.55, 0.28, 1.20), MATS["InteriorDark"]),
]
for part in parts:
    part.parent = root

axis_height = HEIGHT + GROUND_CLEARANCE + 0.16
for axis in [
    cube_blender("BLENDER_AXIS_LONGITUDINAL_FRONT_PLUS_X", (0.62, 0.0, axis_height), (1.24, 0.035, 0.035), MATS["LightsRear"]),
    cube_blender("BLENDER_AXIS_LATERAL_RIGHT_PLUS_Y", (0.0, 0.48, axis_height + 0.06), (0.035, 0.96, 0.035), MATS["LightsFront"]),
    cube_blender("BLENDER_AXIS_VERTICAL_UP_PLUS_Z", (0.0, 0.0, axis_height + 0.34), (0.035, 0.035, 0.68), MATS["WheelMetal"]),
]:
    axis.parent = root

for name, loc, steerable in [
    ("FL", (-HALF_TRACK, TYRE_RADIUS, FRONT_Z), True),
    ("FR", (HALF_TRACK, TYRE_RADIUS, FRONT_Z), True),
    ("RL", (-HALF_TRACK, TYRE_RADIUS, REAR_Z), False),
    ("RR", (HALF_TRACK, TYRE_RADIUS, REAR_Z), False),
]:
    wheel_phys(name, loc, steerable).parent = root
    empty_phys(f"WHEEL_{name}_CENTER", loc).parent = root
    empty_phys(f"WHEEL_{name}_CONTACT", (loc[0], 0.0, loc[2])).parent = root

empty_phys("CG_REFERENCE", (0, 0, 0)).parent = root
empty_phys("FRONT_AXLE_CENTER", (0, TYRE_RADIUS, FRONT_Z)).parent = root
empty_phys("REAR_AXLE_CENTER", (0, TYRE_RADIUS, REAR_Z)).parent = root
empty_phys("VEHICLE_FORWARD_REFERENCE", (0, TYRE_RADIUS, FRONT_Z + 0.65)).parent = root

print("Generated EK9 Blender wheel centres:")
for name, loc in [
    ("FL", (-HALF_TRACK, TYRE_RADIUS, FRONT_Z)),
    ("FR", (HALF_TRACK, TYRE_RADIUS, FRONT_Z)),
    ("RL", (-HALF_TRACK, TYRE_RADIUS, REAR_Z)),
    ("RR", (HALF_TRACK, TYRE_RADIUS, REAR_Z)),
]:
    print(f"  {name}: Blender X/Y/Z = {b_loc(loc)}")

bpy.ops.wm.save_as_mainfile(filepath="generated_ek9_reference.blend")
bpy.ops.export_scene.fbx(filepath="generated_ek9_reference.fbx", apply_unit_scale=True, bake_space_transform=False)
