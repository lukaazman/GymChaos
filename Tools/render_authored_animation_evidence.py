"""Render consistent per-character pose evidence from exported FBX files."""

from __future__ import annotations

import sys
from pathlib import Path

import bpy
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import export_authored_character_fbx as exporter


OUTPUT = exporter.PROJECT_ROOT / ".tools/authored_animation_evidence"
POSES = (
    ("idle1", 0.5),
    ("walking", 0.5),
    ("running", 0.5),
    ("squat", 0.0),
    ("squat", 0.5),
    ("squat", 1.0),
)
PLAYER_POSES = (
    ("idle1", 0.5),
    ("walking", 0.5),
    ("running", 0.5),
    ("crouched_walking", 0.0),
    ("crouched_walking", 0.5),
    ("crouched_walking", 1.0),
    ("jumping", 0.0),
    ("jumping", 0.5),
    ("jumping", 1.0),
    ("punch_left", 0.5),
    ("punch_right", 0.5),
    ("throw_frisbee", 0.5),
    ("throw_object_hard", 0.5),
)


def action_for(name):
    return next(
        action for action in bpy.data.actions
        if action.name.lower() == name.lower()
        or action.name.lower().endswith("|" + name.lower())
    )


def bounds_for(mesh):
    evaluated = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
    data = evaluated.to_mesh()
    try:
        points = [evaluated.matrix_world @ vertex.co for vertex in data.vertices]
        minimum = Vector(tuple(min(point[axis] for point in points) for axis in range(3)))
        maximum = Vector(tuple(max(point[axis] for point in points) for axis in range(3)))
        return minimum, maximum
    finally:
        evaluated.to_mesh_clear()


def render_character(character, config):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(
        filepath=str(config["output"]), use_anim=True,
        automatic_bone_orientation=False,
    )
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = 420
    scene.render.resolution_y = 560
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "JPEG"
    scene.render.image_settings.quality = 88
    if scene.world is None:
        scene.world = bpy.data.worlds.new("Evidence World")
    scene.world.color = (0.035, 0.045, 0.06)

    armature = next(obj for obj in scene.objects if obj.type == "ARMATURE")
    mesh = next(obj for obj in scene.objects if obj.type == "MESH")
    material = bpy.data.materials.new(character + " evidence")
    material.diffuse_color = (0.52, 0.24, 0.11, 1.0) if character != "goku" else (0.78, 0.32, 0.05, 1.0)
    mesh.data.materials.clear()
    mesh.data.materials.append(material)

    camera_data = bpy.data.cameras.new("Evidence Camera")
    camera = bpy.data.objects.new("Evidence Camera", camera_data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    camera_data.type = "ORTHO"

    key = bpy.data.lights.new("Key", "AREA")
    key.energy = 900
    key.shape = "DISK"
    key.size = 4.0
    key_object = bpy.data.objects.new("Key", key)
    scene.collection.objects.link(key_object)

    fill = bpy.data.lights.new("Fill", "AREA")
    fill.energy = 450
    fill.size = 5.0
    fill_object = bpy.data.objects.new("Fill", fill)
    scene.collection.objects.link(fill_object)

    poses = PLAYER_POSES if character == "player" else POSES
    for clip_name, normalized_time in poses:
        action = action_for(clip_name)
        exporter.assign_action_slot(armature, action)
        start, end = action.frame_range
        frame = int(round(start + (end - start) * normalized_time))
        scene.frame_set(frame)
        bpy.context.view_layer.update()
        minimum, maximum = bounds_for(mesh)
        center = (minimum + maximum) * 0.5
        size = maximum - minimum
        distance = max(size.x, size.z) * 3.2
        camera.location = center + Vector((0.0, -distance, size.z * 0.04))
        camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
        camera_data.ortho_scale = max(size.z * 1.18, size.x * 1.55)
        key_object.location = center + Vector((-2.0, -3.0, 3.0))
        key_object.rotation_euler = (center - key_object.location).to_track_quat("-Z", "Y").to_euler()
        fill_object.location = center + Vector((2.0, -1.0, 1.0))
        fill_object.rotation_euler = (center - fill_object.location).to_track_quat("-Z", "Y").to_euler()
        suffix = f"{clip_name}_{int(round(normalized_time * 100)):03d}"
        scene.render.filepath = str(OUTPUT / character / f"{suffix}.jpg")
        bpy.ops.render.render(write_still=True)
        if character == "player":
            review_views = {
                "back": Vector((0.0, distance, size.z * 0.04)),
                "left": Vector((-distance, 0.0, size.z * 0.04)),
                "right": Vector((distance, 0.0, size.z * 0.04)),
            }
            for view_name, offset in review_views.items():
                camera.location = center + offset
                camera.rotation_euler = (
                    center - camera.location).to_track_quat("-Z", "Y").to_euler()
                scene.render.filepath = str(
                    OUTPUT / character / f"{suffix}_{view_name}.jpg")
                bpy.ops.render.render(write_still=True)
    print(f"GYMCHAOS_AUTHORED_VISUAL_EVIDENCE_OK character={character} poses={len(poses)}")


OUTPUT.mkdir(parents=True, exist_ok=True)
requested = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
characters = requested or list(exporter.CHARACTERS)
for character_name in characters:
    character_config = exporter.CHARACTERS[character_name]
    render_character(character_name, character_config)
print(f"GYMCHAOS_AUTHORED_VISUAL_EVIDENCE_BATCH_OK characters={len(characters)}")
