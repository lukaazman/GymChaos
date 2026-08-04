"""Retarget Mixamo motion onto each authored-pose scan rig and export one FBX.

The retarget copies the Mixamo bone-local animation deltas instead of forcing
the scan into the animation's T-pose.  That keeps each model's authored rest
pose intact while still using the downloaded Mixamo run and punch motion.
"""

from __future__ import annotations

from pathlib import Path
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector


REPOSITORY_ROOT = Path("D:/GitHub/GymChaos")
BASE_ROOT = REPOSITORY_ROOT / ".tools/character-rigs"
MIA_ROOT = REPOSITORY_ROOT / ".tools/ComfyUI/output"
OUTPUT_ROOT = REPOSITORY_ROOT / ".tools/character-animated"
ANIMATION_ROOT = REPOSITORY_ROOT / "GymChaos/Assets/Resources/Player/Animations"
ANIMATIONS = {
    "Run": ANIMATION_ROOT / "Run.fbx",
    "Punch": ANIMATION_ROOT / "Punch/Punch.fbx",
}
CHARACTERS = ("arnold", "cbum", "zyzz", "ronnie", "jay", "goku", "manwithsuit1")


def clear_scene():
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)


def primary_armature(objects=None):
    candidates = [obj for obj in (objects or bpy.context.scene.objects) if obj.type == "ARMATURE"]
    if not candidates:
        raise RuntimeError("No armature in scene")
    return max(candidates, key=lambda item: len(item.data.bones))


def action_frame_range(action):
    start, end = action.frame_range
    return int(round(start)), int(round(end))


def motion_strength(character_name: str, clip_name: str, bone_name: str) -> float:
    limited_scan = character_name in {"jay", "goku"}
    if clip_name == "Run":
        if "Leg" in bone_name or bone_name.endswith("Foot"):
            return 0.32 if character_name == "goku" else 0.82
        if "Arm" in bone_name or bone_name.endswith("Hand") or bone_name.endswith("Shoulder"):
            return 0.06 if limited_scan else 0.28
        if bone_name.endswith("Head") or bone_name.endswith("Neck"):
            return 0.12
        return 0.34
    if clip_name == "Punch":
        if "Left" in bone_name and (
            "Arm" in bone_name or bone_name.endswith("Hand") or bone_name.endswith("Shoulder")
        ):
            return 0.30 if limited_scan else 0.72
        if "Right" in bone_name and (
            "Arm" in bone_name or bone_name.endswith("Hand") or bone_name.endswith("Shoulder")
        ):
            return 0.06 if limited_scan else 0.24
        if "Spine" in bone_name or bone_name.endswith("Hips"):
            return 0.30
        return 0.08
    return 0.3


def remove_objects(objects):
    for obj in objects:
        if obj and obj.name in bpy.data.objects:
            bpy.data.objects.remove(obj, do_unlink=True)


def repair_unweighted_scan_vertices(mesh_objects, maximum_vertices=5000):
    repaired_vertices = 0
    for mesh in mesh_objects:
        world_vertices = [mesh.matrix_world @ vertex.co for vertex in mesh.data.vertices]
        minimum = Vector((
            min(point.x for point in world_vertices),
            min(point.y for point in world_vertices),
            min(point.z for point in world_vertices),
        ))
        maximum = Vector((
            max(point.x for point in world_vertices),
            max(point.y for point in world_vertices),
            max(point.z for point in world_vertices),
        ))
        center = (minimum + maximum) * 0.5
        height = max(0.001, maximum.z - minimum.z)

        def central_bone(point):
            normalized_height = (point.z - minimum.z) / height
            if normalized_height >= 0.79:
                return "mixamorig:Head"
            if normalized_height >= 0.69:
                return "mixamorig:Spine2"
            if normalized_height >= 0.62:
                return "mixamorig:Spine1"
            if normalized_height >= 0.54:
                return "mixamorig:Spine"
            if normalized_height >= 0.45:
                return "mixamorig:Hips"
            side = "Left" if point.x < center.x else "Right"
            if normalized_height < 0.10:
                return f"mixamorig:{side}Foot"
            if normalized_height < 0.29:
                return f"mixamorig:{side}Leg"
            return f"mixamorig:{side}UpLeg"

        adjacency = [[] for _ in mesh.data.vertices]
        for edge in mesh.data.edges:
            first, second = edge.vertices
            adjacency[first].append(second)
            adjacency[second].append(first)
        visited = [False] * len(adjacency)
        for start in range(len(adjacency)):
            if visited[start]:
                continue
            stack = [start]
            visited[start] = True
            members = []
            while stack:
                current = stack.pop()
                members.append(current)
                for neighbor in adjacency[current]:
                    if not visited[neighbor]:
                        visited[neighbor] = True
                        stack.append(neighbor)
            points = [world_vertices[index] for index in members]
            centroid = sum(points, Vector()) / len(points)
            totals = {}
            for vertex_index in members:
                for assignment in mesh.data.vertices[vertex_index].groups:
                    totals[assignment.group] = totals.get(assignment.group, 0.0) + assignment.weight
            if not totals:
                replacement = mesh.vertex_groups.get(central_bone(centroid))
                if replacement is not None:
                    replacement.add(members, 1.0, "REPLACE")
                    repaired_vertices += len(members)
                continue
            dominant_index = max(totals, key=totals.get)
            unweighted = [
                vertex_index for vertex_index in members
                if not any(assignment.weight > 1e-6 for assignment in mesh.data.vertices[vertex_index].groups)
            ]
            if unweighted:
                mesh.vertex_groups[dominant_index].add(unweighted, 1.0, "REPLACE")
                repaired_vertices += len(unweighted)
    print(
        f"CHARACTER_SCAN_WEIGHTS_OK repaired_unweighted={repaired_vertices}"
    )


def bake_clip(target, character_name: str, clip_name: str, source_path: Path):
    before_objects = set(bpy.context.scene.objects)
    before_actions = set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=str(source_path), use_anim=True)
    imported_objects = [obj for obj in bpy.context.scene.objects if obj not in before_objects]
    source = primary_armature(imported_objects)
    source_action = source.animation_data.action if source.animation_data else None
    if source_action is None:
        new_actions = [action for action in bpy.data.actions if action not in before_actions]
        if not new_actions:
            raise RuntimeError(f"No action imported from {source_path}")
        source_action = new_actions[0]
        source.animation_data_create()
        source.animation_data.action = source_action

    target.animation_data_create()
    target_action = bpy.data.actions.new(name=clip_name)
    target_action.use_fake_user = True
    target.animation_data.action = target_action
    start, end = action_frame_range(source_action)
    scene = bpy.context.scene
    scene.frame_start = start
    scene.frame_end = end
    target_bones = {bone.name: bone for bone in target.pose.bones}
    source_bones = {bone.name: bone for bone in source.pose.bones}
    shared = [bone.name for bone in target.data.bones if bone.name in source_bones]
    if len(shared) < 18:
        raise RuntimeError(f"{clip_name} shares only {len(shared)} bones with target")

    for frame in range(start, end + 1):
        scene.frame_set(frame)
        for bone_name in shared:
            source_bone = source_bones[bone_name]
            target_bone = target_bones[bone_name]
            source_rest_world = source.matrix_world @ source.data.bones[bone_name].matrix_local
            source_pose_world = source.matrix_world @ source_bone.matrix
            source_delta_world = (
                source_pose_world.to_quaternion().normalized()
                @ source_rest_world.to_quaternion().normalized().inverted()
            )
            source_delta_world = Quaternion((1.0, 0.0, 0.0, 0.0)).slerp(
                source_delta_world, motion_strength(character_name, clip_name, bone_name))
            target_rest_world = target.matrix_world @ target.data.bones[bone_name].matrix_local
            desired_world_rotation = (
                source_delta_world @ target_rest_world.to_quaternion().normalized()
            ).normalized()
            desired_armature_rotation = (
                target.matrix_world.to_quaternion().normalized().inverted()
                @ desired_world_rotation
            ).normalized()

            data_bone = target.data.bones[bone_name]
            if target_bone.parent is None:
                desired_head = data_bone.head_local.copy()
            else:
                parent_rest = target_bone.parent.bone.matrix_local
                relative_head = parent_rest.inverted() @ data_bone.head_local
                desired_head = target_bone.parent.matrix @ relative_head

            target_bone.rotation_mode = "QUATERNION"
            target_bone.matrix = Matrix.LocRotScale(
                desired_head, desired_armature_rotation, Vector((1.0, 1.0, 1.0)))
            target_bone.keyframe_insert("rotation_quaternion", frame=frame, group=bone_name)
            target_bone.keyframe_insert("location", frame=frame, group=bone_name)
            target_bone.keyframe_insert("scale", frame=frame, group=bone_name)

    target.animation_data.action = None
    remove_objects(imported_objects)
    for action in list(bpy.data.actions):
        if action is source_action or (action not in before_actions and action is not target_action):
            bpy.data.actions.remove(action)
    print(f"CHARACTER_MIXAMO_CLIP_OK clip={clip_name} frames={start}-{end} bones={len(shared)}")
    return target_action


def export_character(name: str):
    clear_scene()
    mia_path = MIA_ROOT / f"{name}_mia_authored_mia.fbx"
    base_path = mia_path if mia_path.exists() else BASE_ROOT / f"{name}_blender_rigged.fbx"
    bpy.ops.import_scene.fbx(filepath=str(base_path), use_anim=False)
    target = primary_armature()
    mesh_objects = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    repair_unweighted_scan_vertices(mesh_objects)
    actions = [bake_clip(target, name, clip_name, source_path) for clip_name, source_path in ANIMATIONS.items()]

    bpy.ops.object.select_all(action="DESELECT")
    target.select_set(True)
    for mesh in mesh_objects:
        mesh.select_set(True)
    bpy.context.view_layer.objects.active = target
    destination = OUTPUT_ROOT / f"{name}_mixamo_rigged.fbx"
    destination.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=str(destination),
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        add_leaf_bones=False,
        use_armature_deform_only=True,
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
        path_mode="COPY",
        embed_textures=True,
    )
    print(f"CHARACTER_MIXAMO_RIG_OK name={name} actions={','.join(action.name for action in actions)} output={destination}")


def requested_characters():
    if "--" not in sys.argv:
        return CHARACTERS
    requested = tuple(value.lower() for value in sys.argv[sys.argv.index("--") + 1 :])
    unknown = [value for value in requested if value not in CHARACTERS]
    if unknown:
        raise ValueError(f"Unknown characters: {unknown}")
    return requested or CHARACTERS


def main():
    names = requested_characters()
    for name in names:
        export_character(name)
    print(f"CHARACTER_MIXAMO_RIGS_OK count={len(names)}")


if __name__ == "__main__":
    main()
