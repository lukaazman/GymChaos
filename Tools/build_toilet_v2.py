"""Build the bathroom toilet used by GymBackRoomBuilder.

blender -b --factory-startup -P Tools/build_toilet_v2.py -- <out.glb> [render.png]

The toilet keeps the old scan's frame so the Unity placement and collider
still fit: centred bounding box 0.56 x 0.83 x 1.0 m, tank at +Y (glTF -Z,
Unity -Z), bowl opening at -Y. The bowl is a real open basin with water, and
the seat and lid are separate nodes whose origins sit on their hinge line, so
Unity opens them with a rotation about the local X axis.

Material colours are authored as sRGB factors because RuntimeGlbSceneLoader
feeds glTF colour factors straight into Material.color.
"""
import math
import sys

import bmesh
import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else "toilet_v2.glb"
RENDER = argv[1] if len(argv) > 1 else None

SEGMENTS = 48
FLOOR = -0.5
RIM = -0.03            # top of the ceramic rim
BOWL_Y = -0.145        # bowl ellipse centre
HINGE_Y = 0.118
SEAT_T = 0.024
LID_T = 0.022


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def material(name, color, roughness, metallic=0.0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    return mat


reset()
CERAMIC = material("Toilet ceramic", (0.93, 0.93, 0.91), 0.18)
SEAT = material("Toilet seat", (0.96, 0.96, 0.95), 0.32)
WATER = material("Toilet water", (0.50, 0.66, 0.76), 0.05)
CHROME = material("Toilet chrome", (0.80, 0.81, 0.83), 0.15, 1.0)
SHADOW = material("Toilet trap", (0.30, 0.34, 0.36), 0.4)


def ellipse_ring(bm, rx, ry, z, cy=BOWL_Y, cx=0.0, back_flat=0.0):
    ring = []
    for i in range(SEGMENTS):
        a = 2.0 * math.pi * i / SEGMENTS
        x = cx + math.sin(a) * rx
        y = cy - math.cos(a) * ry
        if back_flat > 0.0 and y > cy + ry * (1.0 - back_flat):
            y = cy + ry * (1.0 - back_flat)
        ring.append(bm.verts.new((x, y, z)))
    return ring


def bridge(bm, a, b, flip=False):
    n = len(a)
    for i in range(n):
        j = (i + 1) % n
        quad = [a[i], a[j], b[j], b[i]]
        bm.faces.new(list(reversed(quad)) if flip else quad)


def cap(bm, ring, z, flip=False, cy=BOWL_Y):
    centre = bm.verts.new((0.0, cy, z))
    n = len(ring)
    for i in range(n):
        tri = [centre, ring[(i + 1) % n], ring[i]]
        bm.faces.new(list(reversed(tri)) if flip else tri)


def mesh_object(name, bm, mat, smooth=True):
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    for poly in mesh.polygons:
        poly.use_smooth = smooth
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    return obj


def build_bowl():
    bm = bmesh.new()
    # Outer shell from the rim down into the pedestal and the floor foot.
    outer = [
        (0.200, 0.270, RIM, BOWL_Y),
        (0.199, 0.268, RIM - 0.035, BOWL_Y),
        (0.188, 0.250, RIM - 0.11, BOWL_Y),
        (0.165, 0.212, RIM - 0.19, BOWL_Y + 0.01),
        (0.135, 0.170, RIM - 0.26, BOWL_Y + 0.03),
        (0.118, 0.150, RIM - 0.31, BOWL_Y + 0.05),
        (0.114, 0.150, FLOOR + 0.07, BOWL_Y + 0.07),
        (0.128, 0.170, FLOOR + 0.02, BOWL_Y + 0.07),
        (0.134, 0.178, FLOOR, BOWL_Y + 0.07),
    ]
    inner = [
        (0.174, 0.240, RIM, BOWL_Y + 0.004),
        (0.168, 0.230, RIM - 0.03, BOWL_Y + 0.006),
        (0.146, 0.196, RIM - 0.11, BOWL_Y + 0.012),
        (0.108, 0.142, RIM - 0.19, BOWL_Y + 0.02),
        (0.064, 0.082, RIM - 0.25, BOWL_Y + 0.03),
    ]
    outer_rings = [ellipse_ring(bm, rx, ry, z, cy) for rx, ry, z, cy in outer]
    inner_rings = [ellipse_ring(bm, rx, ry, z, cy) for rx, ry, z, cy in inner]
    for a, b in zip(outer_rings, outer_rings[1:]):
        bridge(bm, a, b)
    bridge(bm, inner_rings[0], outer_rings[0])  # flat rim top
    for a, b in zip(inner_rings, inner_rings[1:]):
        bridge(bm, b, a)
    cap(bm, outer_rings[-1], FLOOR, flip=True, cy=BOWL_Y + 0.07)
    cap(bm, inner_rings[-1], RIM - 0.27, cy=BOWL_Y + 0.03)
    bowl = mesh_object("Toilet bowl", bm, CERAMIC)

    # Water surface and the darker trap opening below it.
    bm = bmesh.new()
    ring = ellipse_ring(bm, 0.104, 0.137, RIM - 0.185, BOWL_Y + 0.02)
    cap(bm, ring, RIM - 0.185, cy=BOWL_Y + 0.02)
    water = mesh_object("Toilet water", bm, WATER)
    bm = bmesh.new()
    ring = ellipse_ring(bm, 0.045, 0.055, RIM - 0.184, BOWL_Y + 0.05)
    cap(bm, ring, RIM - 0.184, cy=BOWL_Y + 0.05)
    trap = mesh_object("Toilet trap", bm, SHADOW)
    return [bowl, water, trap]


def rounded_box(name, size, location, mat, bevel):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = size
    bpy.ops.object.transform_apply(scale=True)
    mod = obj.modifiers.new("Bevel", "BEVEL")
    mod.width = bevel
    mod.segments = 4
    mod.limit_method = "ANGLE"
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.ops.object.shade_smooth()
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    return obj


def build_tank():
    parts = []
    parts.append(rounded_box(
        "Toilet tank", (0.50, 0.20, 0.56), (0.0, 0.305, 0.16), CERAMIC, 0.03))
    parts.append(rounded_box(
        "Toilet tank lid", (0.53, 0.225, 0.05), (0.0, 0.302, 0.465), CERAMIC, 0.018))
    # Neck joining the bowl back to the tank foot.
    parts.append(rounded_box(
        "Toilet neck", (0.24, 0.17, 0.26), (0.0, 0.14, RIM - 0.1), CERAMIC, 0.035))
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=32, radius=0.035, depth=0.012, location=(0.0, 0.29, 0.496))
    button = bpy.context.active_object
    button.name = "Toilet flush"
    button.data.materials.append(CHROME)
    parts.append(button)
    return parts


def hinge_plate(name, outer_rx, outer_ry, inner_rx, inner_ry, thickness, hinge_z, mat):
    """Seat or lid with its origin on the hinge (back edge, top face)."""
    bm = bmesh.new()
    cy = BOWL_Y - HINGE_Y  # ellipse centre relative to the hinge
    top = 0.0
    bottom = -thickness
    o_top = ellipse_ring(bm, outer_rx, outer_ry, top, cy, back_flat=0.12)
    o_bot = ellipse_ring(bm, outer_rx * 0.985, outer_ry * 0.985, bottom, cy, back_flat=0.12)
    bridge(bm, o_bot, o_top)
    if inner_rx > 0.0:
        i_top = ellipse_ring(bm, inner_rx, inner_ry, top, cy + 0.012)
        i_bot = ellipse_ring(bm, inner_rx * 1.02, inner_ry * 1.02, bottom, cy + 0.012)
        bridge(bm, o_top, i_top)
        bridge(bm, i_top, i_bot)
        bridge(bm, i_bot, o_bot)
    else:
        cap(bm, o_top, top, cy=cy)
        cap(bm, o_bot, bottom, flip=True, cy=cy)
    obj = mesh_object(name, bm, mat)
    obj.location = (0.0, HINGE_Y, hinge_z)
    return obj


def build_seat_and_lid():
    seat = hinge_plate(
        "Toilet seat", 0.192, 0.262, 0.118, 0.165, SEAT_T, RIM + SEAT_T, SEAT)
    lid = hinge_plate(
        "Toilet lid", 0.196, 0.266, 0.0, 0.0, LID_T, RIM + SEAT_T + LID_T, SEAT)
    # Hinge posts.
    for side in (-1.0, 1.0):
        bpy.ops.mesh.primitive_cylinder_add(
            vertices=16, radius=0.016, depth=0.04,
            location=(side * 0.09, HINGE_Y + 0.012, RIM + 0.012))
        post = bpy.context.active_object
        post.name = "Toilet hinge post"
        post.data.materials.append(CHROME)
    # Exported closed (identity hinge); Unity opens them about local X.
    return seat, lid


def render(path):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in [
        e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items
    ] else "BLENDER_EEVEE"
    scene.render.resolution_x = 900
    scene.render.resolution_y = 900
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("World")
    world.color = (0.18, 0.19, 0.21)
    scene.world = world
    bpy.ops.object.light_add(type="AREA", location=(-1.2, -1.6, 2.2))
    light = bpy.context.active_object
    light.data.energy = 260
    light.data.size = 2.0
    light.rotation_euler = (math.radians(45), 0, math.radians(-35))
    bpy.ops.object.light_add(type="AREA", location=(1.5, -0.6, 1.0))
    fill = bpy.context.active_object
    fill.data.energy = 90
    fill.data.size = 2.0
    fill.rotation_euler = (math.radians(70), 0, math.radians(60))
    bpy.ops.object.camera_add(location=(-0.95, -1.35, 0.95))
    cam = bpy.context.active_object
    direction = Vector((0.0, 0.0, -0.05)) - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = 42
    scene.camera = cam
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    build_bowl()
    build_tank()
    build_seat_and_lid()
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    bpy.context.view_layer.update()
    for obj in bpy.data.objects:
        if obj.type != "MESH" or obj.name.startswith("Toilet lid"):
            continue
        for corner in obj.bound_box:
            p = obj.matrix_world @ Vector(corner)
            lo = Vector((min(lo[i], p[i]) for i in range(3)))
            hi = Vector((max(hi[i], p[i]) for i in range(3)))
    print("TOILET_V2_BOUNDS", tuple(round(v, 3) for v in lo), tuple(round(v, 3) for v in hi))
    bpy.ops.export_scene.gltf(
        filepath=OUT, export_format="GLB", export_apply=True,
        export_yup=True, export_materials="EXPORT")
    print("TOILET_V2_EXPORTED", OUT)
    if RENDER:
        # Preview the in-game rest state: lid up against the tank, seat down.
        bpy.data.objects["Toilet lid"].rotation_euler = (math.radians(-96.0), 0.0, 0.0)
        render(RENDER)
        bpy.data.objects["Toilet seat"].rotation_euler = (math.radians(-92.0), 0.0, 0.0)
        render(RENDER.replace(".png", "_seat_up.png"))


main()
