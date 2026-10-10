"""Export a Unity-friendly city perimeter mesh from city_dystopia.blend.

The authored .blend remains the source of truth.  This derivative keeps the
final city, the explicit recessed/emissive window module, facade sheen and the
small warm/cool reflection streaks, while removing the large presentation
ground and wet reflection slab.  The mesh is evaluated in world space and
joined into one object with material slots so the game's lightweight GLB
loader can render the complete city instead of only the first glTF mesh.
"""

from __future__ import annotations

import bpy
import os
import math
from mathutils import Vector

SOURCE = os.path.abspath(bpy.data.filepath)
OUTPUT = os.path.join(os.path.dirname(SOURCE), "city_dystopia_backdrop.glb")
FACADE_TEXTURE_SIZE = 256

FACADE_PALETTES = {
    "MAT_Facade_Procedural_BlueBlack": {
        "base": (0.035, 0.105, 0.135),
        "panel": (0.055, 0.17, 0.19),
        "mortar": (0.012, 0.035, 0.045),
    },
    "MAT_Facade_Procedural_Charcoal": {
        "base": (0.055, 0.09, 0.105),
        "panel": (0.105, 0.16, 0.17),
        "mortar": (0.018, 0.028, 0.032),
    },
    "MAT_Facade_Procedural_GreenBlack": {
        "base": (0.035, 0.115, 0.085),
        "panel": (0.065, 0.19, 0.145),
        "mortar": (0.012, 0.04, 0.03),
    },
    "MAT_Facade_Procedural_WarmGray": {
        "base": (0.14, 0.115, 0.095),
        "panel": (0.26, 0.19, 0.14),
        "mortar": (0.035, 0.028, 0.024),
    },
}

def collection_names(obj):
    return {collection.name for collection in obj.users_collection}


def should_export(obj):
    if obj.type != "MESH" or obj.hide_render:
        return False

    names = collection_names(obj)
    if not ({"STAGE_03_FINAL_CITY", "STAGE_04_LIGHTING_AND_REFLECTIONS"} & names):
        return False

    # The presentation ground would occupy the protected gameplay area when
    # the asset is copied around the gym.  Keep only the actual city/reflection
    # detail; the scene itself still retains the full ground and wet surface.
    lower = obj.name.lower()
    if obj.name == "City_Ground_Mass":
        return False
    if obj.name == "Night_Reflection_Surface":
        return False
    # Ground reflection strips belong to the presentation slab, not the city
    # wall.  Excluding them lets the building rows move flush to a gym fence
    # without dragging ground detail through the protected gameplay area.
    if obj.name in ("Night_Reflection_Broken_Streaks", "Night_Reflection_Ripple_Lines"):
        return False
    if lower.startswith("night_warm_emitter") or lower.startswith("night_cool_haze"):
        return False
    return True


def facade_texture(material_name):
    """Build a deterministic, subtle panel/brick albedo for runtime GLB."""
    palette = FACADE_PALETTES.get(
        material_name,
        FACADE_PALETTES["MAT_Facade_Procedural_Charcoal"],
    )
    size = FACADE_TEXTURE_SIZE
    image = bpy.data.images.new(
        "Runtime Albedo " + material_name,
        width=size,
        height=size,
        alpha=False,
        float_buffer=False,
    )
    image.colorspace_settings.name = "sRGB"
    pixels = []
    for y in range(size):
        v = y / float(size - 1)
        row = int(v * 18.0)
        row_offset = 0.5 if row % 2 else 0.0
        for x in range(size):
            u = x / float(size - 1)
            brick_u = (u * 13.0 + row_offset) % 1.0
            brick_v = (v * 18.0) % 1.0
            seam = brick_u < 0.035 or brick_v < 0.045
            variation = 0.90 + 0.10 * (
                0.5 + 0.5 * math.sin(x * 0.17 + y * 0.071 + len(material_name))
            )
            if seam:
                color = palette["mortar"]
            else:
                panel_mix = 0.18 + 0.18 * (
                    0.5 + 0.5 * math.sin(u * 37.0 + row * 1.7)
                )
                color = tuple(
                    (palette["base"][index] * (1.0 - panel_mix) +
                     palette["panel"][index] * panel_mix) * variation
                    for index in range(3)
                )
            pixels.extend((color[0], color[1], color[2], 1.0))
    image.pixels = pixels
    image.pack()
    return image


def runtime_facade_material(source_material, image):
    """Make an export-only Principled material with an embedded albedo."""
    material = source_material.copy()
    material.name = source_material.name
    material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    shader = nodes.new("ShaderNodeBsdfPrincipled")
    texture = nodes.new("ShaderNodeTexImage")
    texture.image = image
    texture.extension = "REPEAT"
    texture.interpolation = "Linear"
    shader.inputs["Roughness"].default_value = 0.34
    shader.inputs["Metallic"].default_value = 0.18
    links.new(texture.outputs["Color"], shader.inputs["Base Color"])
    links.new(shader.outputs["BSDF"], output.inputs["Surface"])
    material.diffuse_color = (*FACADE_PALETTES[source_material.name]["base"], 1.0)
    material["runtime_export_texture"] = image.name
    material["runtime_export_note"] = "Baked from procedural Brick Texture facade workflow"
    return material


def runtime_material_for(source_material, material_cache, image_cache):
    if source_material is None:
        return None
    if not source_material.name.startswith("MAT_Facade_Procedural_"):
        return source_material
    if source_material.name not in material_cache:
        image = facade_texture(source_material.name)
        image_cache.append(image)
        material_cache[source_material.name] = runtime_facade_material(source_material, image)
    return material_cache[source_material.name]
def evaluated_world_mesh(obj, depsgraph):
    evaluated = obj.evaluated_get(depsgraph)
    mesh = bpy.data.meshes.new_from_object(
        evaluated,
        depsgraph=depsgraph,
        preserve_all_data_layers=True,
    )
    if mesh is None:
        return None

    # Make the exported object independent of the source collection/parents.
    # The transform is baked in world space so every perimeter copy has the
    # exact same city layout and no hidden parent offset can clip the gym.
    mesh.transform(obj.matrix_world)
    return mesh


def export_backdrop():
    depsgraph = bpy.context.evaluated_depsgraph_get()
    original_selection = list(bpy.context.selected_objects)
    original_active = bpy.context.view_layer.objects.active

    temp_collection = bpy.data.collections.new("CITY_DYSTOPIA_BACKDROP_EXPORT")
    bpy.context.scene.collection.children.link(temp_collection)
    temporary = []
    runtime_materials = {}
    runtime_images = []
    try:
        for source_obj in list(bpy.data.objects):
            if not should_export(source_obj):
                continue
            mesh = evaluated_world_mesh(source_obj, depsgraph)
            if mesh is None or len(mesh.vertices) == 0:
                if mesh is not None:
                    bpy.data.meshes.remove(mesh)
                continue
            temp_obj = bpy.data.objects.new(source_obj.name, mesh)
            temp_collection.objects.link(temp_obj)
            temp_obj.data.materials.clear()
            for material in source_obj.data.materials:
                temp_obj.data.materials.append(
                    runtime_material_for(material, runtime_materials, runtime_images)
                )
            temporary.append(temp_obj)

        if not temporary:
            raise RuntimeError("No final city meshes were found for backdrop export")

        bpy.ops.object.select_all(action="DESELECT")
        for obj in temporary:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = temporary[0]
        bpy.ops.object.join()
        combined = bpy.context.view_layer.objects.active
        combined.name = "City_Dystopia_Backdrop_Combined"

        bpy.ops.object.select_all(action="DESELECT")
        combined.select_set(True)
        bpy.context.view_layer.objects.active = combined
        try:
            bpy.ops.export_scene.gltf(
                filepath=OUTPUT,
                export_format="GLB",
                export_materials="EXPORT",
                export_cameras=False,
                export_lights=False,
                use_selection=True,
            )
        except TypeError:
            bpy.ops.export_scene.gltf(
                filepath=OUTPUT,
                export_format="GLB",
                use_selection=True,
            )

        bounds = [combined.matrix_world @ Vector(corner) for corner in combined.bound_box]
        min_corner = [min(point[index] for point in bounds) for index in range(3)]
        max_corner = [max(point[index] for point in bounds) for index in range(3)]
        print(
            "CITY_DYSTOPIA_BACKDROP_EXPORT_OK",
            {
                "source": SOURCE,
                "output": OUTPUT,
                "source_mesh_count": len(temporary),
                "vertex_count": len(combined.data.vertices),
                "polygon_count": len(combined.data.polygons),
                "material_count": len(combined.data.materials),
                "bounds_min": min_corner,
                "bounds_max": max_corner,
            },
        )
    finally:
        bpy.ops.object.select_all(action="DESELECT")
        for obj in original_selection:
            if obj is not None and obj.name in bpy.data.objects:
                obj.select_set(True)
        if original_active is not None and original_active.name in bpy.data.objects:
            bpy.context.view_layer.objects.active = original_active

        for obj in list(temp_collection.objects):
            bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.collections.remove(temp_collection)
        for material in runtime_materials.values():
            if material is not None and material.name in bpy.data.materials:
                bpy.data.materials.remove(material, do_unlink=True)
        for image in runtime_images:
            if image is not None and image.name in bpy.data.images:
                bpy.data.images.remove(image, do_unlink=True)


if __name__ == "__main__":
    export_backdrop()




