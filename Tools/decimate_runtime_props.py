"""Write decimated runtime copies (<name>_lod.glb) of the scanned prop GLBs.

Usage:
    blender -b -P Tools/decimate_runtime_props.py -- [name ...]

The scanned props, vehicles, streetlight and radio are ~140k-triangle scans.
They are small on screen, but every copy is drawn in the main view, in two
shadow cascades and in the mirror. This writes a <name>_lod.glb next to each
source in GymChaos/Assets/StreamingAssets/BodyBuilders. The source GLB is
never changed. Node names, hierarchy, origins (vehicle Wheel_* axles),
materials and textures are kept; only triangle density drops.

With --render it also writes before/after textured renders to
Logs/agent/lod/<name>.png for a visual check.
"""
import json
import math
import os
import struct
import sys

import bpy
from mathutils import Vector

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RUNTIME = os.path.join(ROOT, "GymChaos", "Assets", "StreamingAssets", "BodyBuilders")
RENDER_DIR = os.path.join(ROOT, "Logs", "agent", "lod")

# Target triangles for the whole file. Small objects keep full detail.
TARGETS = {
    "items/blue_ball": 10000,
    "items/red_ball": 10000,
    "items/foam_roller": 10000,
    "items/paper_towel": 10000,
    "items/step_platform": 10000,
    "items/yoga_roll_halfrolled": 12000,
    "items/yoga_roll_rolledout": 12000,
    "items/black_bag": 12000,
    "items/color_bag": 12000,
    "items/sink": 16000,
    "items/toilet": 16000,
    "items/wooden_bench": 16000,
    "outside/streetlight": 12000,
    "sound/radio": 18000,
    "vehicles/Arnold_Hummer": 36000,
    "vehicles/Davie_Bus": 40000,
    "vehicles/Policecar": 36000,
}
MIN_DECIMATE_TRIANGLES = 1500


def triangle_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def import_glb(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    # Welded import: the decimator then treats UV seams as attribute borders
    # instead of open island edges it may drag across the texture atlas.
    bpy.ops.import_scene.gltf(filepath=path, merge_vertices=True)
    return [o for o in bpy.context.scene.objects if o.type == "MESH"]


def decimate_file(name, render):
    source = os.path.join(RUNTIME, name + ".glb")
    target_path = os.path.join(RUNTIME, name + "_lod.glb")
    meshes = import_glb(source)
    before = sum(triangle_count(o) for o in meshes)
    if render:
        render_view(meshes, os.path.join(RENDER_DIR, name.replace("/", "_") + "_before.png"))
    ratio = min(1.0, TARGETS[name] / max(1, before))
    # Mesh data may be shared between objects; decimate each datablock once.
    done = set()
    for obj in meshes:
        if obj.data.name in done or triangle_count(obj) < MIN_DECIMATE_TRIANGLES or ratio >= 1.0:
            continue
        done.add(obj.data.name)
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        modifier = obj.modifiers.new("RuntimeDecimate", "DECIMATE")
        modifier.decimate_type = "COLLAPSE"
        modifier.ratio = ratio
        modifier.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    after = sum(triangle_count(o) for o in meshes)
    if render:
        render_view(meshes, os.path.join(RENDER_DIR, name.replace("/", "_") + "_after.png"))

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(
        filepath=target_path,
        export_format="GLB",
        export_yup=True,
        export_apply=True,
        export_normals=True,
        export_texcoords=True,
        export_materials="EXPORT",
        export_image_format="AUTO")
    restore_material_factors(source, target_path)
    print(f"PROP_LOD_OK name={name} objects={len(meshes)} trianglesBefore={before} "
          f"trianglesAfter={after} bytesBefore={os.path.getsize(source)} "
          f"bytesAfter={os.path.getsize(target_path)}")


def read_glb(path):
    with open(path, "rb") as handle:
        data = handle.read()
    json_length = struct.unpack_from("<I", data, 12)[0]
    document = json.loads(data[20:20 + json_length])
    return document, data[20 + json_length:]


def restore_material_factors(source, target):
    """Copy the source PBR factors and alpha mode into the exported LOD.

    Blender leaves out factors equal to the glTF defaults (metallic and
    roughness 1.0), but the game's parsers default a missing metallicFactor
    to 0, so the LOD would shade differently from the source.
    """
    source_json, _ = read_glb(source)
    target_json, rest = read_glb(target)
    source_materials = source_json.get("materials", [])
    target_materials = target_json.get("materials", [])
    if len(source_materials) != len(target_materials):
        print(f"PROP_LOD_MATERIAL_MISMATCH {target}")
        return
    for src, dst in zip(source_materials, target_materials):
        src_pbr = src.get("pbrMetallicRoughness", {})
        dst_pbr = dst.setdefault("pbrMetallicRoughness", {})
        for key in ("baseColorFactor", "metallicFactor", "roughnessFactor"):
            if key in src_pbr:
                dst_pbr[key] = src_pbr[key]
            else:
                dst_pbr.pop(key, None)
        for key in ("alphaMode", "alphaCutoff", "emissiveFactor", "doubleSided"):
            if key in src:
                dst[key] = src[key]
    encoded = json.dumps(target_json, separators=(",", ":")).encode("utf-8")
    encoded += b" " * ((4 - len(encoded) % 4) % 4)
    total = 12 + 8 + len(encoded) + len(rest)
    with open(target, "wb") as handle:
        handle.write(struct.pack("<4sII", b"glTF", 2, total))
        handle.write(struct.pack("<I4s", len(encoded), b"JSON"))
        handle.write(encoded)
        handle.write(rest)


def render_view(meshes, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    scene = bpy.context.scene
    lo = Vector((math.inf,) * 3)
    hi = Vector((-math.inf,) * 3)
    for obj in meshes:
        for corner in obj.bound_box:
            world = obj.matrix_world @ Vector(corner)
            lo = Vector(map(min, lo, world))
            hi = Vector(map(max, hi, world))
    center = (lo + hi) * 0.5
    radius = max((hi - lo).length * 0.5, 0.01)
    camera_data = bpy.data.cameras.get("LodCamera") or bpy.data.cameras.new("LodCamera")
    camera = bpy.data.objects.get("LodCamera") or bpy.data.objects.new("LodCamera", camera_data)
    if camera.name not in scene.collection.objects:
        scene.collection.objects.link(camera)
    direction = Vector((1.0, -1.4, 0.8)).normalized()
    camera.location = center + direction * radius * 2.6
    camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera_data.lens = 50
    camera_data.clip_end = radius * 20
    scene.camera = camera
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.render.resolution_x = 640
    scene.render.resolution_y = 480
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    render = "--render" in argv
    names = [a for a in argv if not a.startswith("--")] or list(TARGETS)
    for name in names:
        decimate_file(name, render)


main()
