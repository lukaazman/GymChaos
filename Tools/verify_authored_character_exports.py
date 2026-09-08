"""Round-trip verification for the seven authored GymChaos character FBXs."""

from __future__ import annotations

import math
from pathlib import Path

import bpy
from mathutils import Vector


PROJECT = Path(__file__).resolve().parents[1]
ENEMY_CLIPS = (
    "walking", "running", "idle1", "idle2", "idle3", "punch_combo",
    "flying", "squat", "celebration1", "celebration2", "celebration3",
)
PLAYER_CLIPS = (
    "walking", "running", "idle1", "idle2", "idle3", "crouched_walking",
    "jumping", "punch_left", "punch_right", "throw_frisbee",
    "throw_object_hard",
)
EXPORTS = {
    **{
        name: (
            PROJECT / "GymChaos/Assets/Resources/Characters/Enemies" /
            f"{name}.fbx",
            ENEMY_CLIPS,
        )
        for name in ("arnold_authored", "cbum_authored", "goku_authored",
                     "jaycutler_authored", "ronnie_authored", "zyzz_authored")
    },
    "player_authored": (
        PROJECT / "GymChaos/Assets/Resources/Player/player_authored.fbx",
        PLAYER_CLIPS,
    ),
}

INHERITED_DEFORM_BONES = {
    "DEF-upper_arm.L.001", "DEF-upper_arm.R.001",
    "DEF-forearm.L.001", "DEF-forearm.R.001",
    "DEF-thigh.L.001", "DEF-thigh.R.001",
    "DEF-shin.L.001", "DEF-shin.R.001",
    "DEF-pelvis.L", "DEF-pelvis.R",
    "DEF-breast.L", "DEF-breast.R",
    *{f"DEF-palm.0{index}.{side}" for side in ("L", "R") for index in range(1, 5)},
}


def finite(values) -> bool:
    return all(math.isfinite(float(value)) for value in values)


def action_for(actions, clip_name):
    matches = [
        action for action in actions
        if action.name.lower() == clip_name.lower() or
        action.name.lower().endswith("|" + clip_name.lower())
    ]
    if len(matches) != 1:
        raise RuntimeError(
            f"{clip_name}: expected one imported action, got "
            f"{[action.name for action in matches]}"
        )
    return matches[0]


def assign_action(armature, action) -> None:
    armature.animation_data_create()
    armature.animation_data.action = action
    if getattr(action, "slots", None) and len(action.slots) > 0:
        armature.animation_data.action_slot = action.slots[0]


def evaluated_bounds(mesh):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = mesh.evaluated_get(depsgraph)
    data = evaluated.to_mesh()
    try:
        points = [evaluated.matrix_world @ vertex.co for vertex in data.vertices]
        if not points or not all(finite(point) for point in points):
            raise RuntimeError(f"{mesh.name}: missing or non-finite evaluated vertices")
        minimum = Vector(tuple(min(point[axis] for point in points) for axis in range(3)))
        maximum = Vector(tuple(max(point[axis] for point in points) for axis in range(3)))
        return minimum, maximum
    finally:
        evaluated.to_mesh_clear()


def validate_weights(character, mesh, armature) -> None:
    deform_names = {bone.name for bone in armature.data.bones if bone.use_deform}
    group_names = {group.index: group.name for group in mesh.vertex_groups}
    unweighted = 0
    invalid = set()
    non_normalized = 0
    for vertex in mesh.data.vertices:
        influences = [item for item in vertex.groups if item.weight > 1e-6]
        total = sum(item.weight for item in influences)
        if not influences:
            unweighted += 1
        elif abs(total - 1.0) > 0.001:
            non_normalized += 1
        invalid.update(
            group_names.get(item.group, f"index:{item.group}")
            for item in influences
            if group_names.get(item.group) not in deform_names
        )
    if unweighted or non_normalized or invalid:
        raise RuntimeError(
            f"{character}: invalid round-trip weights unweighted={unweighted} "
            f"nonNormalized={non_normalized} invalid={sorted(invalid)}"
        )


def validate_inherited_segments(character, clip_name, armature) -> None:
    """Ensure unmapped Rigify helper segments follow their animated parent."""
    for bone_name in INHERITED_DEFORM_BONES:
        pose_bone = armature.pose.bones.get(bone_name)
        if pose_bone is None or pose_bone.parent is None:
            raise RuntimeError(f"{character}: missing inherited segment {bone_name}")
        rest_local = pose_bone.parent.bone.matrix_local.inverted() @ pose_bone.bone.matrix_local
        pose_local = pose_bone.parent.matrix.inverted() @ pose_bone.matrix
        local_delta = rest_local.inverted() @ pose_local
        angle_degrees = math.degrees(local_delta.to_quaternion().angle)
        if angle_degrees > 0.5:
            raise RuntimeError(
                f"{character}/{clip_name}: {bone_name} counter-rotates its parent "
                f"by {angle_degrees:.2f} degrees"
            )


def verify(character, path, clip_names) -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(
        filepath=str(path), use_anim=True, use_image_search=False,
        automatic_bone_orientation=False
    )
    armatures = [obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"]
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if len(armatures) != 1 or len(meshes) != 1:
        raise RuntimeError(
            f"{character}: expected one armature/mesh, got "
            f"{len(armatures)}/{len(meshes)}"
        )
    armature, mesh = armatures[0], meshes[0]
    deform_names = {bone.name for bone in armature.data.bones if bone.use_deform}
    if len(deform_names) != 72:
        raise RuntimeError(f"{character}: expected 72 deform bones, got {len(deform_names)}")
    modifiers = [
        modifier for modifier in mesh.modifiers
        if modifier.type == "ARMATURE" and modifier.object == armature
    ]
    if len(modifiers) != 1:
        raise RuntimeError(f"{character}: mesh is not bound to its imported armature")
    validate_weights(character, mesh, armature)

    actions = list(bpy.data.actions)
    summaries = []
    for clip_name in clip_names:
        action = action_for(actions, clip_name)
        assign_action(armature, action)
        start = int(round(action.frame_range[0]))
        end = int(round(action.frame_range[1]))
        frames = (start, int(round((start + end) * 0.5)), end)
        heights = []
        for frame in frames:
            bpy.context.scene.frame_set(frame)
            bpy.context.view_layer.update()
            validate_inherited_segments(character, clip_name, armature)
            minimum, maximum = evaluated_bounds(mesh)
            size = maximum - minimum
            if min(size) <= 1e-5 or max(size) > 20.0:
                raise RuntimeError(
                    f"{character}/{clip_name}@{frame}: implausible bounds {tuple(size)}"
                )
            for pose_bone in armature.pose.bones:
                if not finite(pose_bone.scale) or any(
                    abs(value - 1.0) > 0.001 for value in pose_bone.scale
                ):
                    raise RuntimeError(
                        f"{character}/{clip_name}@{frame}: invalid scale on "
                        f"{pose_bone.name}: {tuple(pose_bone.scale)}"
                    )
            heights.append(size.z)
        if clip_name in {"walking", "running", "idle1", "idle2", "idle3", "squat"}:
            endpoint_drift = abs(heights[-1] - heights[0]) / max(heights[0], 1e-6)
            if endpoint_drift > 0.02:
                raise RuntimeError(
                    f"{character}/{clip_name}: endpoint height drift {endpoint_drift:.2%}"
                )
        summaries.append(
            f"{clip_name}:{'/'.join(f'{height:.3f}' for height in heights)}"
        )
    print(
        "GYMCHAOS_AUTHORED_ROUNDTRIP_OK "
        f"character={character} bones={len(deform_names)} "
        f"vertices={len(mesh.data.vertices)} clips={len(clip_names)} "
        f"heights={'|'.join(summaries)}"
    )


for character_name, (fbx_path, clips) in EXPORTS.items():
    if not fbx_path.exists():
        raise FileNotFoundError(fbx_path)
    verify(character_name, fbx_path, clips)

print(f"GYMCHAOS_AUTHORED_ROUNDTRIP_BATCH_OK characters={len(EXPORTS)}")
