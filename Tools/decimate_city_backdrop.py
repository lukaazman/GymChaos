"""Decimate the distant city backdrop GLB for runtime.

Usage:
    blender -b -P Tools/decimate_city_backdrop.py -- [ratio]

Reads the full-detail source Assets/city_dystopia_backdrop.glb and writes the
runtime copy GymChaos/Assets/StreamingAssets/BodyBuilders/outside/
city_dystopia_backdrop.glb. Every object, material and texture is kept; only
triangle density drops, because the city is only ever seen from far away.
"""
import os
import sys

import bpy

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCE = os.path.join(ROOT, "Assets", "city_dystopia_backdrop.glb")
TARGET = os.path.join(
    ROOT, "GymChaos", "Assets", "StreamingAssets", "BodyBuilders", "outside",
    "city_dystopia_backdrop.glb")
# Only material parts this dense are decimated; small emissive windows,
# reflection streaks and signs keep their exact shape.
MIN_TRIANGLES = 50000


def triangle_count(obj):
    mesh = obj.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh()
    count = sum(len(poly.vertices) - 2 for poly in mesh.polygons)
    obj.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh_clear()
    return count


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ratio = float(argv[0]) if argv else 0.12

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=SOURCE)
    originals = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    for obj in originals:
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.mesh.separate(type="MATERIAL")
        bpy.ops.object.mode_set(mode="OBJECT")
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    before = after = 0
    for obj in meshes:
        tris = triangle_count(obj)
        before += tris
        if tris >= MIN_TRIANGLES:
            modifier = obj.modifiers.new("RuntimeDecimate", "DECIMATE")
            modifier.decimate_type = "COLLAPSE"
            modifier.ratio = ratio
            modifier.use_collapse_triangulate = True
            bpy.context.view_layer.objects.active = obj
            bpy.ops.object.modifier_apply(modifier=modifier.name)
        after += triangle_count(obj)

    # Re-join the material parts so the runtime sees one mesh again.
    bpy.ops.object.select_all(action="DESELECT")
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.join()

    bpy.ops.export_scene.gltf(
        filepath=TARGET,
        export_format="GLB",
        export_image_format="AUTO",
        export_apply=True,
        export_yup=True)
    print(f"CITY_DECIMATE_OK objects={len(meshes)} ratio={ratio} "
          f"trianglesBefore={before} trianglesAfter={after} "
          f"bytes={os.path.getsize(TARGET)}")


main()
