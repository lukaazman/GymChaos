"""Verify externally rigged FBXs and render neutral-pose inspection images."""

from __future__ import annotations

from pathlib import Path
import sys

import bpy
from mathutils import Vector


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
RENDER_ROOT = REPOSITORY_ROOT / ".tools" / "character-renders"
CORE_BONES = {
    "hips", "spine", "head", "leftarm", "leftforearm", "lefthand",
    "rightarm", "rightforearm", "righthand", "leftupleg", "leftleg",
    "leftfoot", "rightupleg", "rightleg", "rightfoot",
}


def normalized_bone(name: str) -> str:
    return name.lower().replace("mixamorig", "").replace(":", "").replace("_", "").replace(" ", "")


def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def world_bounds(objects):
    points = []
    for obj in objects:
        for corner in obj.bound_box:
            points.append(obj.matrix_world @ Vector(corner))
    minimum = Vector((min(point.x for point in points), min(point.y for point in points), min(point.z for point in points)))
    maximum = Vector((max(point.x for point in points), max(point.y for point in points), max(point.z for point in points)))
    return minimum, maximum


def look_at(obj, target):
    obj.rotation_euler = (target - obj.location).to_track_quat("-Z", "Y").to_euler()


def render(path: Path, meshes):
    minimum, maximum = world_bounds(meshes)
    center = (minimum + maximum) * 0.5
    size = maximum - minimum
    height = max(0.1, size.z)

    camera_data = bpy.data.cameras.new("Inspection Camera")
    camera = bpy.data.objects.new("Inspection Camera", camera_data)
    bpy.context.scene.collection.objects.link(camera)
    camera.location = center + Vector((0.0, -height * 2.25, height * 0.05))
    camera.data.lens = 58
    look_at(camera, center + Vector((0.0, 0.0, height * 0.03)))
    bpy.context.scene.camera = camera

    for name, location, energy, size_value in (
        ("Key", center + Vector((-height, -height, height * 1.2)), 1300, height),
        ("Fill", center + Vector((height, -height * 0.4, height * 0.7)), 800, height * 0.8),
        ("Rim", center + Vector((0.0, height, height * 1.1)), 1000, height * 0.7),
    ):
        light_data = bpy.data.lights.new(name, "AREA")
        light_data.energy = energy
        light_data.shape = "DISK"
        light_data.size = size_value
        light = bpy.data.objects.new(name, light_data)
        bpy.context.scene.collection.objects.link(light)
        light.location = location
        look_at(light, center)

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.render.resolution_x = 640
    scene.render.resolution_y = 640
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    if scene.world is None:
        scene.world = bpy.data.worlds.new("Inspection World")
    scene.world.color = (0.035, 0.035, 0.045)
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


def verify(path: Path):
    clear_scene()
    if path.suffix.lower() in {".glb", ".gltf"}:
        bpy.ops.import_scene.gltf(filepath=str(path))
    else:
        bpy.ops.import_scene.fbx(filepath=str(path), use_image_search=True)
    armatures = [obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"]
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if not meshes:
        raise RuntimeError(f"{path.name}: expected one armature and at least one mesh")
    if not armatures:
        RENDER_ROOT.mkdir(parents=True, exist_ok=True)
        render_path = RENDER_ROOT / f"{path.stem}.png"
        render(render_path, meshes)
        print(f"CHARACTER_STAGING_RENDER file={path.name} render={render_path}")
        return
    if len(armatures) != 1:
        raise RuntimeError(f"{path.name}: expected exactly one armature")

    armature = armatures[0]
    raw_bones = [bone.name for bone in armature.data.bones]
    names = {normalized_bone(bone.name) for bone in armature.data.bones}
    missing = sorted(CORE_BONES - names)
    if missing:
        object_names = [(obj.name, obj.type, obj.parent.name if obj.parent else None) for obj in bpy.context.scene.objects]
        raise RuntimeError(
            f"{path.name}: missing core Mixamo bones {missing}; imported={raw_bones[:25]}; "
            f"objects={object_names[:60]}"
        )

    primary = max(meshes, key=lambda obj: len(obj.data.vertices))
    modifiers = [modifier for modifier in primary.modifiers if modifier.type == "ARMATURE"]
    if not modifiers or all(modifier.object != armature for modifier in modifiers):
        raise RuntimeError(f"{path.name}: primary mesh is not bound to its armature")
    if not primary.vertex_groups:
        raise RuntimeError(f"{path.name}: primary mesh has no skin weights")
    unweighted = sum(
        1 for vertex in primary.data.vertices
        if sum(assignment.weight for assignment in vertex.groups) <= 1e-6
    )
    if unweighted:
        raise RuntimeError(f"{path.name}: {unweighted} vertices have no skin weight")

    texture_nodes = []
    for material in bpy.data.materials:
        if material.use_nodes and material.node_tree is not None:
            texture_nodes.extend(
                node for node in material.node_tree.nodes
                if node.type == "TEX_IMAGE" and node.image is not None
            )
    if not texture_nodes:
        raise RuntimeError(f"{path.name}: no texture image is connected to an imported material")

    RENDER_ROOT.mkdir(parents=True, exist_ok=True)
    if armature.animation_data is not None:
        armature.animation_data.action = None
    bpy.context.view_layer.objects.active = armature
    bpy.ops.object.mode_set(mode="POSE")
    bpy.ops.pose.select_all(action="SELECT")
    bpy.ops.pose.transforms_clear()
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.scene.frame_set(0)
    render_path = RENDER_ROOT / f"{path.stem}-rest.png"
    render(render_path, meshes)
    clip_renders = []
    for action in list(bpy.data.actions):
        action_name = action.name.lower()
        label = "run" if "run" in action_name else "punch" if "punch" in action_name else None
        if label is None:
            continue
        armature.animation_data_create()
        armature.animation_data.action = action
        start, end = action.frame_range
        bpy.context.scene.frame_set(int(round((start + end) * 0.5)))
        clip_path = RENDER_ROOT / f"{path.stem}-{label}.png"
        render(clip_path, meshes)
        clip_renders.append(f"{label}:{clip_path}")
    if armature.animation_data is not None:
        armature.animation_data.action = None
    minimum, maximum = world_bounds(meshes)
    print(
        f"CHARACTER_RIG_OK file={path.name} bones={len(names)} vertices={len(primary.data.vertices)} "
        f"groups={len(primary.vertex_groups)} textures={len(texture_nodes)} height={(maximum - minimum).z:.4f} "
        f"render={render_path} clips={';'.join(clip_renders) or 'none'}"
    )


def main():
    paths = [Path(value).resolve() for value in sys.argv[sys.argv.index("--") + 1 :]] if "--" in sys.argv else []
    if not paths:
        raise SystemExit("Pass one or more FBX files after --")
    for path in paths:
        verify(path)
    print(f"CHARACTER_RIGS_OK count={len(paths)}")


if __name__ == "__main__":
    main()
