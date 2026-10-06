"""Builds the runtime outdoor foliage set for GymChaos.

Run headless:
    blender -b -P Tools/export_outdoor_foliage.py

Inputs: the authored bush1/bush2/tree1/tree2 GLBs in
GymChaos/Assets/StreamingAssets/BodyBuilders/outside/.

Outputs (GymChaos/Assets/StreamingAssets/BodyBuilders/outside/foliage/):
- bush1_lod.glb, bush2_lod.glb, tree1_lod.glb, tree2_lod.glb: decimated copies
  (the sources are ~90k vertices each, too heavy to scatter) with a
  non-metallic material so the albedo texture reads in URP.
- bush3_custom.glb: clustered shrub built here, textured with a darker,
  olive-shifted copy of the bush2 leaf texture.
- tree3_custom.glb: round-canopy tree built here, canopy textured with a
  lighter yellow-green copy of the bush1 leaf texture and a trunk in the
  tree1 bark tone.
"""

import math
import os
import random

import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Vector, noise

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
SOURCE_DIR = os.path.join(
    ROOT, "GymChaos", "Assets", "StreamingAssets", "BodyBuilders", "outside")
OUT_DIR = os.path.join(SOURCE_DIR, "foliage")
TEXTURE_SIZE = 512
LOD_TRIANGLES = {"bush1": 2500, "bush2": 2500, "tree1": 4000, "tree2": 4000}


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_glb(name):
    bpy.ops.import_scene.gltf(filepath=os.path.join(SOURCE_DIR, name + ".glb"))
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    # Bake parent transforms so the exported mesh is a single clean node.
    bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    for other in list(bpy.context.scene.objects):
        if other != obj:
            bpy.data.objects.remove(other, do_unlink=True)
    return obj


def triangle_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def decimate(obj, target):
    count = triangle_count(obj)
    if count <= target:
        return
    modifier = obj.modifiers.new("Decimate", "DECIMATE")
    modifier.ratio = max(0.01, target / count)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=modifier.name)


def base_image(material):
    for node in material.node_tree.nodes:
        if node.type == "TEX_IMAGE" and node.image is not None:
            for link in node.outputs["Color"].links:
                if link.to_socket.name == "Base Color":
                    return node.image
    return None


def downsized_copy(image, name, tint=None, saturation=1.0, gain=1.0):
    """Returns a TEXTURE_SIZE copy of image, optionally recoloured."""
    copy = image.copy()
    copy.name = name
    copy.scale(TEXTURE_SIZE, TEXTURE_SIZE)
    pixels = np.array(copy.pixels[:], dtype=np.float32).reshape(-1, 4)
    rgb = pixels[:, :3]
    if tint is not None or saturation != 1.0 or gain != 1.0:
        luma = rgb @ np.array([0.299, 0.587, 0.114], dtype=np.float32)
        rgb = luma[:, None] + (rgb - luma[:, None]) * saturation
        if tint is not None:
            rgb = rgb * np.array(tint, dtype=np.float32)
        rgb = np.clip(rgb * gain, 0.0, 1.0)
        pixels[:, :3] = rgb
    pixels[:, 3] = 1.0
    copy.pixels[:] = pixels.ravel()
    copy.pack()
    return copy


def flat_material(name, image, roughness=0.88):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    bsdf = nodes.get("Principled BSDF")
    bsdf.inputs["Metallic"].default_value = 0.0
    bsdf.inputs["Roughness"].default_value = roughness
    texture = nodes.new("ShaderNodeTexImage")
    texture.image = image
    links.new(texture.outputs["Color"], bsdf.inputs["Base Color"])
    return material


def export(obj, file_name):
    os.makedirs(OUT_DIR, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    path = os.path.join(OUT_DIR, file_name)
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format="GLB",
        use_selection=True,
        export_yup=True,
        export_apply=True,
        export_normals=True,
        export_texcoords=True,
        export_materials="EXPORT",
        export_image_format="AUTO",
    )
    print(f"FOLIAGE_EXPORT {file_name} tris={triangle_count(obj)} "
          f"dims={tuple(round(d, 3) for d in obj.dimensions)}")


def ground_object(obj):
    """Puts the lowest vertex on z=0 and centres the footprint on the origin."""
    coords = [obj.matrix_world @ v.co for v in obj.data.vertices]
    min_z = min(c.z for c in coords)
    cx = (min(c.x for c in coords) + max(c.x for c in coords)) * 0.5
    cy = (min(c.y for c in coords) + max(c.y for c in coords)) * 0.5
    for v in obj.data.vertices:
        v.co -= Vector((cx, cy, min_z))


def export_lod(name):
    reset_scene()
    obj = import_glb(name)
    decimate(obj, LOD_TRIANGLES[name])
    source = obj.data.materials[0]
    image = base_image(source)
    material = flat_material(name + "_leaf", downsized_copy(image, name + "_albedo"))
    obj.data.materials.clear()
    obj.data.materials.append(material)
    ground_object(obj)
    obj.name = name + "_lod"
    export(obj, name + "_lod.glb")
    return image


def blob(bm, center, radius, seed, squash=1.0, subdivisions=3):
    result = bmesh.ops.create_icosphere(
        bm, subdivisions=subdivisions, radius=radius)
    offset = Vector((seed * 3.17, seed * 1.31, seed * 2.03))
    for vert in result["verts"]:
        direction = vert.co.normalized()
        bump = noise.noise(direction * 2.3 + offset) * 0.22 + \
            noise.noise(direction * 5.1 + offset) * 0.08
        vert.co = direction * radius * (1.0 + bump)
        vert.co.z *= squash
        vert.co += center


def mesh_object(name, bm):
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    for poly in mesh.polygons:
        poly.use_smooth = True
    return obj


def spherical_uv(obj, scale=2.0):
    """Wraps the texture around the shape so leaf detail stays small."""
    mesh = obj.data
    uv_layer = mesh.uv_layers.new(name="UVMap")
    center = sum((v.co for v in mesh.vertices), Vector()) / max(1, len(mesh.vertices))
    for loop in mesh.loops:
        co = mesh.vertices[loop.vertex_index].co - center
        u = (math.atan2(co.y, co.x) / (2.0 * math.pi) + 0.5) * scale
        v = (co.z / max(0.001, obj.dimensions.z) + 0.5) * scale
        uv_layer.data[loop.index].uv = (u, v)


def build_custom_bush(source):
    reset_scene()
    leaf_image = source()
    random.seed(31)
    bm = bmesh.new()
    clusters = [
        (Vector((0.0, 0.0, 0.42)), 0.5),
        (Vector((0.42, 0.12, 0.34)), 0.38),
        (Vector((-0.38, 0.2, 0.32)), 0.36),
        (Vector((0.08, -0.4, 0.3)), 0.34),
        (Vector((-0.14, 0.38, 0.5)), 0.32),
    ]
    for index, (center, radius) in enumerate(clusters):
        blob(bm, center, radius, index + 1, squash=0.82)
    obj = mesh_object("bush3_custom", bm)
    spherical_uv(obj, scale=3.0)
    image = downsized_copy(
        leaf_image, "bush3_custom_albedo",
        tint=(0.86, 0.98, 0.62), saturation=1.15, gain=0.92)
    obj.data.materials.append(flat_material("bush3_custom_leaf", image))
    ground_object(obj)
    export(obj, "bush3_custom.glb")


def build_custom_tree(source):
    reset_scene()
    tree_image = source()
    bm = bmesh.new()
    bmesh.ops.create_cone(
        bm, cap_ends=True, segments=10, radius1=0.09, radius2=0.06, depth=1.2,
        matrix=Matrix.Translation((0, 0, 0.6)))
    trunk = mesh_object("tree3_trunk", bm)
    bark = bpy.data.images.new("tree3_custom_bark", 16, 16)
    bark.pixels[:] = [0.23, 0.14, 0.08, 1.0] * 256
    bark.pack()
    trunk.data.materials.append(flat_material("tree3_custom_bark", bark, roughness=0.95))
    trunk_uv = trunk.data.uv_layers.new(name="UVMap")
    for loop in trunk.data.loops:
        trunk_uv.data[loop.index].uv = (0.5, 0.5)

    bm = bmesh.new()
    canopy = [
        (Vector((0.0, 0.0, 1.55)), 0.62),
        (Vector((0.3, 0.12, 1.32)), 0.42),
        (Vector((-0.28, -0.1, 1.36)), 0.44),
        (Vector((0.05, 0.25, 1.9)), 0.38),
    ]
    for index, (center, radius) in enumerate(canopy):
        blob(bm, center, radius, index + 11, squash=0.9)
    crown = mesh_object("tree3_crown", bm)
    spherical_uv(crown, scale=2.5)
    image = downsized_copy(
        tree_image, "tree3_custom_albedo",
        tint=(1.08, 1.12, 0.72), saturation=1.05, gain=1.05)
    crown.data.materials.append(flat_material("tree3_custom_leaf", image))

    bpy.ops.object.select_all(action="DESELECT")
    trunk.select_set(True)
    crown.select_set(True)
    bpy.context.view_layer.objects.active = crown
    bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    obj.name = "tree3_custom"
    ground_object(obj)
    export(obj, "tree3_custom.glb")


def main():
    images = {}
    for name in ("bush1", "bush2", "tree1", "tree2"):
        image = export_lod(name)
        # read_factory_settings clears images; keep the pixels we need.
        images[name] = (list(image.size), np.array(image.pixels[:], dtype=np.float32))

    def restore(name):
        size, pixels = images[name]
        image = bpy.data.images.new(name + "_source", size[0], size[1])
        image.pixels[:] = pixels
        # Pack so copies keep these pixels instead of regenerating blank.
        image.pack()
        return image

    build_custom_bush(lambda: restore("bush2"))
    build_custom_tree(lambda: restore("bush1"))
    print("FOLIAGE_EXPORT_DONE")


main()
