"""Build the park bench and park lamp used by GymOutdoorFoliage.

blender -b --factory-startup -P Tools/build_park_props.py -- [render_dir]

Outputs GymChaos/Assets/StreamingAssets/BodyBuilders/outside/foliage/
park_bench.glb and park_lamp.glb. Then run Tools/verify_park_props.py.

Bench frame (unscaled, Unity applies GymOutdoorFoliage.BenchScale): seat top
0.49 m, seat 1.7 m wide and 0.46 m deep, centred on the origin; the sitter
looks along Blender -Y (Unity +Z), the backrest is at +Y (Unity -Z), the same
frame as the old box bench and its collider. Lamp: origin at the base, globe
centre 3.72 m. Colour factors are sRGB because RuntimeGlbSceneLoader feeds
them straight into Material.color.
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
OUT_DIR = os.path.join(ROOT, "GymChaos", "Assets", "StreamingAssets", "BodyBuilders", "outside", "foliage")
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
RENDER_DIR = argv[0] if argv else None

SEAT_TOP = 0.49
SEAT_WIDTH = 1.7
SEAT_DEPTH = 0.46


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def material(name, color, roughness, metallic=0.0, emission=None, strength=0.0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    if emission:
        bsdf.inputs["Emission Color"].default_value = (*emission, 1.0)
        bsdf.inputs["Emission Strength"].default_value = strength
    return mat


def finish(obj, mat, bevel=0.0, segments=2):
    obj.data.materials.append(mat)
    if bevel > 0.0:
        mod = obj.modifiers.new("Bevel", "BEVEL")
        mod.width = bevel
        mod.segments = segments
        mod.limit_method = "ANGLE"
    for poly in obj.data.polygons:
        poly.use_smooth = False
    return obj


def beam(name, mat, p0, p1, width, depth, bevel=0.006):
    """Box from p0 to p1 (centre line), width along local X, depth along local Y."""
    p0, p1 = Vector(p0), Vector(p1)
    length = (p1 - p0).length
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(p0 + p1) * 0.5)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (width, depth, length)
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = Vector((0, 0, 1)).rotation_difference((p1 - p0).normalized())
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return finish(obj, mat, bevel)


def slat(name, mat, center, size, tilt=0.0, bevel=0.006):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=center)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = size
    obj.rotation_euler = (tilt, 0.0, 0.0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return finish(obj, mat, bevel)


def cylinder(name, mat, z0, z1, r0, r1, segments=16, bevel=0.0):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segments, radius1=r0, radius2=r1, depth=z1 - z0)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.location = (0.0, 0.0, (z0 + z1) * 0.5)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
    return finish(obj, mat, bevel)


def join_all(name):
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for obj in meshes:
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        for mod in list(obj.modifiers):
            bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.join()
    obj = bpy.context.active_object
    obj.name = name
    return obj


def build_bench():
    wood = material("Park bench wood", (0.36, 0.23, 0.13), 0.72)
    iron = material("Park cast iron", (0.05, 0.055, 0.055), 0.55, 0.6)
    half = SEAT_DEPTH * 0.5
    # Seat: five slats across the depth, tops at SEAT_TOP.
    slat_w, gap = 0.078, (SEAT_DEPTH - 5 * 0.078) / 4.0
    for i in range(5):
        y = -half + slat_w * 0.5 + i * (slat_w + gap)
        slat(f"Seat slat {i}", wood, (0, y, SEAT_TOP - 0.016), (SEAT_WIDTH, slat_w, 0.032))
    # Backrest: three slats on a 12 degree recline behind the seat.
    tilt = math.radians(12.0)
    for i in range(3):
        z = 0.6 + i * 0.105
        y = half - 0.01 + (z - 0.55) * math.tan(tilt)
        slat(f"Back slat {i}", wood, (0, y, z), (SEAT_WIDTH, 0.026, 0.085), tilt=-tilt)
    for side in (-1, 1):
        x = side * 0.74
        # Front leg, rear leg rising into the back support, seat rail, armrest.
        beam(f"Front leg {side}", iron, (x, -half + 0.04, 0.0), (x, -half + 0.05, SEAT_TOP - 0.03), 0.045, 0.05)
        beam(f"Rear leg {side}", iron, (x, half - 0.06, 0.0), (x, half - 0.02, SEAT_TOP - 0.03), 0.045, 0.05)
        beam(f"Back support {side}", iron, (x, half - 0.02, SEAT_TOP - 0.03),
             (x, half - 0.01 + 0.33 * math.tan(tilt), 0.86), 0.045, 0.045)
        beam(f"Seat rail {side}", iron, (x, -half + 0.01, SEAT_TOP - 0.05), (x, half - 0.01, SEAT_TOP - 0.05), 0.05, 0.04)
        beam(f"Foot bar {side}", iron, (x, -half + 0.05, 0.08), (x, half - 0.05, 0.08), 0.035, 0.03)
        beam(f"Arm post {side}", iron, (x + side * 0.02, -half + 0.05, SEAT_TOP - 0.03), (x + side * 0.02, -half + 0.06, 0.66), 0.035, 0.035)
        beam(f"Armrest {side}", iron, (x + side * 0.02, -half + 0.0, 0.66), (x + side * 0.02, half - 0.02, 0.69), 0.05, 0.035)
    return join_all("Park bench")


def build_lamp():
    iron = material("Park cast iron", (0.05, 0.055, 0.055), 0.55, 0.6)
    glow = material("Park lamp glow", (1.0, 0.9, 0.74), 0.4, 0.0, (1.0, 0.86, 0.62), 2.6)
    cylinder("Base plinth", iron, 0.0, 0.08, 0.2, 0.19, 8, 0.01)
    cylinder("Base", iron, 0.08, 0.42, 0.15, 0.085, 8, 0.008)
    cylinder("Pole", iron, 0.42, 3.42, 0.055, 0.042, 16)
    cylinder("Collar", iron, 3.42, 3.5, 0.075, 0.075, 16, 0.006)
    cylinder("Globe seat", iron, 3.5, 3.54, 0.09, 0.12, 16)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=20, ring_count=12, radius=0.2, location=(0, 0, 3.72))
    globe = bpy.context.active_object
    globe.name = "Globe"
    globe.data.materials.append(glow)
    for poly in globe.data.polygons:
        poly.use_smooth = True
    cylinder("Cap", iron, 3.9, 3.98, 0.1, 0.03, 16)
    return join_all("Park lamp")


def export(path):
    bpy.ops.export_scene.gltf(
        filepath=path, export_format="GLB", export_apply=True,
        export_yup=True, export_materials="EXPORT")
    print("PARK_PROP_EXPORTED", path)


def render(path, target, distance):
    scene = bpy.context.scene
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items]
    scene.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in engines else "BLENDER_EEVEE"
    scene.render.resolution_x = 1600
    scene.render.resolution_y = 600
    world = bpy.data.worlds.new("World")
    world.color = (0.2, 0.21, 0.22)
    scene.world = world
    bpy.ops.object.light_add(type="SUN", location=(0, 0, 5))
    sun = bpy.context.active_object
    sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(50), 0, math.radians(-35))
    # Three views side by side: front, three-quarter, side.
    for i, (yaw, label) in enumerate(((0, "front"), (40, "three-quarter"), (90, "side"))):
        angle = math.radians(yaw)
        cam_data = bpy.data.cameras.new(label)
        cam = bpy.data.objects.new(label, cam_data)
        scene.collection.objects.link(cam)
        cam.location = Vector((math.sin(angle) * distance, -math.cos(angle) * distance, target.z + distance * 0.25))
        cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
        cam_data.lens = 50
        scene.camera = cam
        scene.render.resolution_x = 600
        scene.render.filepath = path.replace(".png", f"-{label}.png")
        bpy.ops.render.render(write_still=True)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    reset()
    build_bench()
    export(os.path.join(OUT_DIR, "park_bench.glb"))
    if RENDER_DIR:
        render(os.path.join(RENDER_DIR, "park_bench.png"), Vector((0, 0, 0.45)), 2.6)
    reset()
    build_lamp()
    export(os.path.join(OUT_DIR, "park_lamp.glb"))
    if RENDER_DIR:
        render(os.path.join(RENDER_DIR, "park_lamp.png"), Vector((0, 0, 2.0)), 6.5)


main()
