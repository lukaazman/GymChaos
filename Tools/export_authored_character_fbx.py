"""Export the authored Blender rigs with their own retargeted animation clips.

Each source .blend is opened independently.  The animation FBX files contain
Mixamo's source skeleton only; this script transfers the motion directly onto
the selected character's own deform-bone hierarchy and writes one FBX containing
that character's mesh, armature, and all clips.  No character shares an armature
or runtime retarget source with another character.

The script intentionally never saves a source .blend.  Run it with Blender:

    blender -b --python Tools/export_authored_character_fbx.py -- cbum
"""

from __future__ import annotations

import argparse
import math
import os
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector


PROJECT_ROOT = Path(__file__).resolve().parents[1]

ENEMY_CLIPS = (
    "walking",
    "running",
    "idle1",
    "idle2",
    "idle3",
    "punch_combo",
    "flying",
    "squat",
    "celebration1",
    "celebration2",
    "celebration3",
)

PLAYER_CLIPS = (
    "walking",
    "running",
    "idle1",
    "idle2",
    "idle3",
    "crouched_walking",
    "jumping",
    "punch_left",
    "punch_right",
    "throw_frisbee",
    "throw_object_hard",
)

PLAYER_ONLY_CLIPS = {
    "crouched_walking",
    "jumping",
    "punch_left",
    "punch_right",
    "throw_frisbee",
    "throw_object_hard",
}

CHARACTERS = {
    "arnold": {
        "blend": PROJECT_ROOT / "Assets/BodyBuilders/enemies/arnold_rig.blend",
        "clips": PROJECT_ROOT / "Assets/BodyBuilders/enemies/anims",
        "output": PROJECT_ROOT / "GymChaos/Assets/Resources/Characters/Enemies/arnold_authored.fbx",
        "clip_names": ENEMY_CLIPS,
    },
    "cbum": {
        "blend": PROJECT_ROOT / "Assets/BodyBuilders/enemies/cbum_rig.blend",
        "clips": PROJECT_ROOT / "Assets/BodyBuilders/enemies/anims",
        "output": PROJECT_ROOT / "GymChaos/Assets/Resources/Characters/Enemies/cbum_authored.fbx",
        "clip_names": ENEMY_CLIPS,
    },
    "goku": {
        "blend": PROJECT_ROOT / "Assets/BodyBuilders/enemies/goku_rig.blend",
        "clips": PROJECT_ROOT / "Assets/BodyBuilders/enemies/anims",
        "output": PROJECT_ROOT / "GymChaos/Assets/Resources/Characters/Enemies/goku_authored.fbx",
        "clip_names": ENEMY_CLIPS,
    },
    "jaycutler": {
        "blend": PROJECT_ROOT / "Assets/BodyBuilders/enemies/jaycutler_rig.blend",
        "clips": PROJECT_ROOT / "Assets/BodyBuilders/enemies/anims",
        "output": PROJECT_ROOT / "GymChaos/Assets/Resources/Characters/Enemies/jaycutler_authored.fbx",
        "clip_names": ENEMY_CLIPS,
    },
    "ronnie": {
        "blend": PROJECT_ROOT / "Assets/BodyBuilders/enemies/ronnie_rig.blend",
        "clips": PROJECT_ROOT / "Assets/BodyBuilders/enemies/anims",
        "output": PROJECT_ROOT / "GymChaos/Assets/Resources/Characters/Enemies/ronnie_authored.fbx",
        "clip_names": ENEMY_CLIPS,
    },
    "zyzz": {
        "blend": PROJECT_ROOT / "Assets/BodyBuilders/enemies/zyzz_rig.blend",
        "clips": PROJECT_ROOT / "Assets/BodyBuilders/enemies/anims",
        "output": PROJECT_ROOT / "GymChaos/Assets/Resources/Characters/Enemies/zyzz_authored.fbx",
        "clip_names": ENEMY_CLIPS,
    },
    "player": {
        "blend": PROJECT_ROOT / "Assets/BodyBuilders/Player/player_rig.blend",
        "clips": PROJECT_ROOT / "Assets/BodyBuilders/enemies/anims",
        "output": PROJECT_ROOT / "GymChaos/Assets/Resources/Player/player_authored.fbx",
        "texture_output": PROJECT_ROOT / "GymChaos/Assets/Resources/Characters/Textures/player_authored.png",
        "clip_names": PLAYER_CLIPS,
    },
}


def parse_args() -> argparse.Namespace:
    # Blender places its own arguments before the -- separator.  argparse is
    # still useful here because it keeps the script usable from a shell too.
    argv = sys.argv
    if "--" in argv:
        argv = argv[argv.index("--") + 1 :]
    else:
        argv = []
    parser = argparse.ArgumentParser()
    parser.add_argument("character", choices=[*sorted(CHARACTERS), "all"])
    return parser.parse_args(argv)


def validate_authored_source(character_name, target, mesh) -> None:
    deform_names = {bone.name for bone in target.data.bones if bone.use_deform}
    if len(deform_names) != 72:
        raise RuntimeError(
            f"{character_name}: expected 72 unique deform bones, got {len(deform_names)}"
        )
    modifiers = [
        modifier for modifier in mesh.modifiers
        if modifier.type == "ARMATURE" and modifier.object == target
    ]
    if len(modifiers) != 1:
        raise RuntimeError(
            f"{character_name}: expected one armature modifier targeting {target.name}"
        )

    group_names = {group.index: group.name for group in mesh.vertex_groups}
    invalid_groups = set()
    unweighted = []
    non_normalized = []
    for vertex in mesh.data.vertices:
        influences = [item for item in vertex.groups if item.weight > 1e-6]
        if not influences:
            unweighted.append(vertex.index)
            continue
        total = sum(item.weight for item in influences)
        if abs(total - 1.0) > 0.001:
            non_normalized.append((vertex.index, total))
        for influence in influences:
            name = group_names.get(influence.group, "")
            if name not in deform_names:
                invalid_groups.add(name or f"index:{influence.group}")
    if invalid_groups:
        raise RuntimeError(
            f"{character_name}: weights reference non-deform groups {sorted(invalid_groups)}"
        )
    print(
        "GYMCHAOS_AUTHORED_SOURCE_OK "
        f"character={character_name} bones={len(deform_names)} "
        f"vertices={len(mesh.data.vertices)} modifier={modifiers[0].name} "
        f"weightsToNormalize={len(non_normalized)} unweighted={len(unweighted)}"
    )


def point_segment_distance(point: Vector, start: Vector, end: Vector) -> float:
    segment = end - start
    if segment.length_squared <= 1e-12:
        return (point - start).length
    factor = max(0.0, min(1.0, (point - start).dot(segment) / segment.length_squared))
    return (point - (start + segment * factor)).length


def normalize_export_weights(
    mesh: bpy.types.Object,
    target: bpy.types.Object,
    deform_names,
) -> None:
    group_names = {group.index: group.name for group in mesh.vertex_groups}
    groups_by_index = {group.index: group for group in mesh.vertex_groups}
    normalized = 0
    rebound = 0
    for vertex in mesh.data.vertices:
        influences = [
            item for item in vertex.groups
            if item.weight > 1e-6 and group_names.get(item.group) in deform_names
        ]
        total = sum(item.weight for item in influences)
        if total <= 1e-8:
            world_point = mesh.matrix_world @ vertex.co
            nearest_bone = min(
                (bone for bone in target.data.bones if bone.name in deform_names),
                key=lambda bone: point_segment_distance(
                    world_point,
                    target.matrix_world @ bone.head_local,
                    target.matrix_world @ bone.tail_local,
                ),
            )
            group = mesh.vertex_groups.get(nearest_bone.name)
            if group is None:
                group = mesh.vertex_groups.new(name=nearest_bone.name)
                groups_by_index[group.index] = group
                group_names[group.index] = group.name
            group.add([vertex.index], 1.0, "REPLACE")
            rebound += 1
            continue
        if abs(total - 1.0) <= 0.000001:
            continue
        for influence in influences:
            groups_by_index[influence.group].add(
                [vertex.index], influence.weight / total, "REPLACE"
            )
        normalized += 1
    print(
        "GYMCHAOS_AUTHORED_WEIGHTS_NORMALIZED "
        f"mesh={mesh.name} vertices={normalized} rebound={rebound}"
    )


def normalize_name(name: str) -> str:
    return (
        name.replace("mixamorig:", "")
        .replace("mixamorig", "")
        .replace("_", "")
        .replace(" ", "")
        .lower()
    )


def find_armature() -> bpy.types.Object:
    exact = bpy.data.objects.get("rig")
    if exact is not None and exact.type == "ARMATURE":
        return exact
    candidates = [obj for obj in bpy.data.objects if obj.type == "ARMATURE"]
    if not candidates:
        raise RuntimeError("No armature object exists in the authored blend")
    return max(candidates, key=lambda obj: sum(1 for bone in obj.data.bones if bone.use_deform))


def find_mesh(target: bpy.types.Object) -> bpy.types.Object:
    candidates = []
    for obj in bpy.data.objects:
        if obj.type != "MESH":
            continue
        if any(mod.type == "ARMATURE" and mod.object == target for mod in obj.modifiers):
            candidates.append(obj)
    if not candidates:
        raise RuntimeError("No mesh with an armature modifier targeting the authored rig")
    originals = [obj for obj in candidates if "original" in obj.name.lower()]
    return max(originals or candidates, key=lambda obj: len(obj.data.vertices))


def keep_only_target_objects(target: bpy.types.Object, mesh: bpy.types.Object) -> None:
    for obj in list(bpy.data.objects):
        if obj not in (target, mesh):
            bpy.data.objects.remove(obj, do_unlink=True)
    target.hide_render = False
    target.hide_viewport = False
    mesh.hide_render = False
    mesh.hide_viewport = False


def bone_depth(bone: bpy.types.EditBone | bpy.types.Bone) -> int:
    depth = 0
    parent = bone.parent
    while parent is not None:
        depth += 1
        parent = parent.parent
    return depth


def source_pose_rotation(source: bpy.types.Object, pose_bone: bpy.types.PoseBone):
    return (source.matrix_world.to_3x3() @ pose_bone.matrix.to_3x3()).to_quaternion()


def source_rest_rotation(source: bpy.types.Object, bone: bpy.types.Bone):
    return (source.matrix_world.to_3x3() @ bone.matrix_local.to_3x3()).to_quaternion()


def target_rest_rotation(target: bpy.types.Object, bone: bpy.types.Bone):
    return (target.matrix_world.to_3x3() @ bone.matrix_local.to_3x3()).to_quaternion()


def target_object_rotation(target: bpy.types.Object, world_rotation):
    return target.matrix_world.to_3x3().inverted().to_quaternion() @ world_rotation


def source_pose_translation(source: bpy.types.Object, pose_bone):
    return source.matrix_world @ pose_bone.matrix.translation


def source_rest_translation(source: bpy.types.Object, bone: bpy.types.Bone):
    return source.matrix_world @ bone.matrix_local.translation


def rest_local_matrix(target: bpy.types.Object, bone: bpy.types.Bone) -> Matrix:
    if bone.parent is None:
        return bone.matrix_local.copy()
    return bone.parent.matrix_local.inverted() @ bone.matrix_local


def import_animation(path: Path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(
        filepath=str(path),
        use_anim=True,
        use_image_search=False,
        automatic_bone_orientation=False,
    )
    imported = [obj for obj in bpy.data.objects if obj not in before]
    source = next((obj for obj in imported if obj.type == "ARMATURE"), None)
    if source is None:
        raise RuntimeError(f"Animation FBX has no armature: {path}")
    action = source.animation_data.action if source.animation_data else None
    if action is None:
        raise RuntimeError(f"Animation FBX has no active action: {path}")
    # Blender 4.4+ stores the evaluated action in an explicit slot. Merely
    # assigning AnimationData.action leaves the imported Mixamo armature in
    # its bind pose while frame_set() runs, which bakes a rest/T-pose instead
    # of the downloaded motion into the authored character.
    assign_action_slot(source, action)
    return imported, source, action


def assign_action_slot(owner: bpy.types.Object, action: bpy.types.Action) -> None:
    owner.animation_data_create()
    owner.animation_data.action = action
    slots = getattr(action, "slots", None)
    if slots is not None and len(slots) > 0:
        owner.animation_data.action_slot = slots[0]


def reset_target_pose(target: bpy.types.Object, rest_pose) -> None:
    # The clean export rig is authored in its rest pose, so every pose basis
    # starts as identity. Reset the basis directly instead of assigning
    # object-space matrices: Blender can retain a previous action's evaluated
    # matrix when a new Action slot is assigned, which then contaminates the
    # first keyed frame of the next clip.
    for pose_bone in sorted(target.pose.bones, key=lambda item: bone_depth(item.bone)):
        pose_bone.matrix_basis = Matrix.Identity(4)


DIRECT_DEFORM_TO_MIXAMO = {
    # The generated Rigify armature has two unparented structural roots. Both
    # must receive the Mixamo Hips displacement or the torso and leg branches
    # will separate during crouches and squats.
    "root": ("mixamorig:Hips",),
    "MCH-torso.parent": ("mixamorig:Hips",),
    "torso": ("mixamorig:Hips",),
    "MCH-spine.001": ("mixamorig:Spine",),
    "MCH-spine": ("mixamorig:Spine",),
    "tweak_spine": ("mixamorig:Hips",),
    "ORG-spine": ("mixamorig:Hips",),
    "ORG-pelvis.L": ("mixamorig:Hips",),
    "ORG-pelvis.R": ("mixamorig:Hips",),
    "MCH-spine.002": ("mixamorig:Spine2",),
    "ORG-spine.003": ("mixamorig:Spine2",),
    "ORG-shoulder.L": ("mixamorig:LeftShoulder",),
    "ORG-shoulder.R": ("mixamorig:RightShoulder",),
    "ORG-palm.01.L": ("mixamorig:LeftHand",),
    "ORG-palm.02.L": ("mixamorig:LeftHand",),
    "ORG-palm.03.L": ("mixamorig:LeftHand",),
    "ORG-palm.04.L": ("mixamorig:LeftHand",),
    "ORG-palm.01.R": ("mixamorig:RightHand",),
    "ORG-palm.02.R": ("mixamorig:RightHand",),
    "ORG-palm.03.R": ("mixamorig:RightHand",),
    "ORG-palm.04.R": ("mixamorig:RightHand",),
    "ORG-breast.L": ("mixamorig:Spine2",),
    "ORG-breast.R": ("mixamorig:Spine2",),
    "DEF-spine": ("mixamorig:Hips",),
    "DEF-spine.001": ("mixamorig:Spine",),
    "DEF-spine.002": ("mixamorig:Spine1",),
    "DEF-spine.003": ("mixamorig:Spine2",),
    "DEF-spine.004": ("mixamorig:Neck",),
    "DEF-spine.005": ("mixamorig:Head",),
    "DEF-pelvis.L": ("mixamorig:Hips",),
    "DEF-pelvis.R": ("mixamorig:Hips",),
    "DEF-shoulder.L": ("mixamorig:LeftShoulder",),
    "DEF-upper_arm.L": ("mixamorig:LeftArm",),
    "DEF-upper_arm.L.001": ("mixamorig:LeftArm",),
    "DEF-forearm.L": ("mixamorig:LeftForeArm",),
    "DEF-forearm.L.001": ("mixamorig:LeftForeArm",),
    "DEF-hand.L": ("mixamorig:LeftHand",),
    "DEF-thigh.L": ("mixamorig:LeftUpLeg",),
    "DEF-thigh.L.001": ("mixamorig:LeftUpLeg",),
    "DEF-shin.L": ("mixamorig:LeftLeg",),
    "DEF-shin.L.001": ("mixamorig:LeftLeg",),
    "DEF-foot.L": ("mixamorig:LeftFoot",),
    "DEF-toe.L": ("mixamorig:LeftToeBase",),
    "DEF-shoulder.R": ("mixamorig:RightShoulder",),
    "DEF-upper_arm.R": ("mixamorig:RightArm",),
    "DEF-upper_arm.R.001": ("mixamorig:RightArm",),
    "DEF-forearm.R": ("mixamorig:RightForeArm",),
    "DEF-forearm.R.001": ("mixamorig:RightForeArm",),
    "DEF-hand.R": ("mixamorig:RightHand",),
    "DEF-thigh.R": ("mixamorig:RightUpLeg",),
    "DEF-thigh.R.001": ("mixamorig:RightUpLeg",),
    "DEF-shin.R": ("mixamorig:RightLeg",),
    "DEF-shin.R.001": ("mixamorig:RightLeg",),
    "DEF-foot.R": ("mixamorig:RightFoot",),
    "DEF-toe.R": ("mixamorig:RightToeBase",),
    "DEF-f_index.01.L": ("mixamorig:LeftHandIndex1",),
    "DEF-f_index.02.L": ("mixamorig:LeftHandIndex2",),
    "DEF-f_index.03.L": ("mixamorig:LeftHandIndex3",),
    "DEF-f_middle.01.L": ("mixamorig:LeftHandMiddle1",),
    "DEF-f_middle.02.L": ("mixamorig:LeftHandMiddle2",),
    "DEF-f_middle.03.L": ("mixamorig:LeftHandMiddle3",),
    "DEF-f_ring.01.L": ("mixamorig:LeftHandRing1",),
    "DEF-f_ring.02.L": ("mixamorig:LeftHandRing2",),
    "DEF-f_ring.03.L": ("mixamorig:LeftHandRing3",),
    "DEF-f_pinky.01.L": ("mixamorig:LeftHandPinky1",),
    "DEF-f_pinky.02.L": ("mixamorig:LeftHandPinky2",),
    "DEF-f_pinky.03.L": ("mixamorig:LeftHandPinky3",),
    "DEF-thumb.01.L": ("mixamorig:LeftHandThumb1",),
    "DEF-thumb.02.L": ("mixamorig:LeftHandThumb2",),
    "DEF-thumb.03.L": ("mixamorig:LeftHandThumb3",),
    "DEF-palm.01.L": ("mixamorig:LeftHand",),
    "DEF-palm.02.L": ("mixamorig:LeftHand",),
    "DEF-palm.03.L": ("mixamorig:LeftHand",),
    "DEF-palm.04.L": ("mixamorig:LeftHand",),
    "DEF-f_index.01.R": ("mixamorig:RightHandIndex1",),
    "DEF-f_index.02.R": ("mixamorig:RightHandIndex2",),
    "DEF-f_index.03.R": ("mixamorig:RightHandIndex3",),
    "DEF-f_middle.01.R": ("mixamorig:RightHandMiddle1",),
    "DEF-f_middle.02.R": ("mixamorig:RightHandMiddle2",),
    "DEF-f_middle.03.R": ("mixamorig:RightHandMiddle3",),
    "DEF-f_ring.01.R": ("mixamorig:RightHandRing1",),
    "DEF-f_ring.02.R": ("mixamorig:RightHandRing2",),
    "DEF-f_ring.03.R": ("mixamorig:RightHandRing3",),
    "DEF-f_pinky.01.R": ("mixamorig:RightHandPinky1",),
    "DEF-f_pinky.02.R": ("mixamorig:RightHandPinky2",),
    "DEF-f_pinky.03.R": ("mixamorig:RightHandPinky3",),
    "DEF-thumb.01.R": ("mixamorig:RightHandThumb1",),
    "DEF-thumb.02.R": ("mixamorig:RightHandThumb2",),
    "DEF-thumb.03.R": ("mixamorig:RightHandThumb3",),
    "DEF-palm.01.R": ("mixamorig:RightHand",),
    "DEF-palm.02.R": ("mixamorig:RightHand",),
    "DEF-palm.03.R": ("mixamorig:RightHand",),
    "DEF-palm.04.R": ("mixamorig:RightHand",),
    "DEF-breast.L": ("mixamorig:Spine2",),
    "DEF-breast.R": ("mixamorig:Spine2",),
}


# Rigify splits long limbs into two deform segments and adds palm/breast
# deformation helpers. They inherit the anatomical driver's motion from their
# parent in the clean export hierarchy; applying the full Mixamo delta again
# compounds rotations and tears the skinned mesh.
INHERITED_DEFORM_BONES = {
    "DEF-upper_arm.L.001", "DEF-upper_arm.R.001",
    "DEF-forearm.L.001", "DEF-forearm.R.001",
    "DEF-thigh.L.001", "DEF-thigh.R.001",
    "DEF-shin.L.001", "DEF-shin.R.001",
    "DEF-pelvis.L", "DEF-pelvis.R",
    "DEF-breast.L", "DEF-breast.R",
    *{f"DEF-palm.0{index}.{side}" for side in ("L", "R") for index in range(1, 5)},
}


def build_deform_map(source: bpy.types.Object, target: bpy.types.Object):
    source_bones = {normalize_name(bone.name): bone for bone in source.data.bones}
    pairs = []
    for target_name, source_names in DIRECT_DEFORM_TO_MIXAMO.items():
        target_bone = target.data.bones.get(target_name)
        if target_bone is None:
            continue
        source_bone = next(
            (
                source_bones.get(normalize_name(source_name))
                for source_name in source_names
                if source_bones.get(normalize_name(source_name)) is not None
            ),
            None,
        )
        if source_bone is not None:
            pairs.append((target_name, target_bone, source_bone))
    pairs.sort(key=lambda item: bone_depth(item[1]))
    expected = set()
    for bone in target.data.bones:
        if not bone.use_deform:
            continue
        current = bone
        while current is not None:
            expected.add(current.name)
            current = current.parent
    mapped = {target_name for target_name, _, _ in pairs}
    missing = sorted(expected - mapped)
    if missing:
        raise RuntimeError(
            "Authored target deform bones are not mapped: " + ", ".join(missing)
        )
    return pairs


def clean_parent_map(deform_names):
    """Return a stable anatomical hierarchy for the exported DEF bones.

    Rigify's generated ORG/MCH parents are control-graph implementation
    details.  Keeping them in the FBX makes a direct Mixamo bake evaluate
    different from the authored target pose after FBX serialization.  The
    mesh is already weighted to DEF bones, so the export rig can preserve the
    exact DEF rest matrices while using a normal gameplay skeleton.
    """
    parents = {name: None for name in deform_names}
    spine = ["DEF-spine"] + [f"DEF-spine.{index:03d}" for index in range(1, 6)]
    for index in range(1, len(spine)):
        parents[spine[index]] = spine[index - 1]

    for side in ("L", "R"):
        parents[f"DEF-pelvis.{side}"] = "DEF-spine"
        parents[f"DEF-thigh.{side}"] = "DEF-spine"
        parents[f"DEF-thigh.{side}.001"] = f"DEF-thigh.{side}"
        parents[f"DEF-shin.{side}"] = f"DEF-thigh.{side}.001"
        parents[f"DEF-shin.{side}.001"] = f"DEF-shin.{side}"
        parents[f"DEF-foot.{side}"] = f"DEF-shin.{side}.001"
        parents[f"DEF-toe.{side}"] = f"DEF-foot.{side}"

        parents[f"DEF-shoulder.{side}"] = "DEF-spine.003"
        parents[f"DEF-upper_arm.{side}"] = f"DEF-shoulder.{side}"
        parents[f"DEF-upper_arm.{side}.001"] = f"DEF-upper_arm.{side}"
        parents[f"DEF-forearm.{side}"] = f"DEF-upper_arm.{side}.001"
        parents[f"DEF-forearm.{side}.001"] = f"DEF-forearm.{side}"
        parents[f"DEF-hand.{side}"] = f"DEF-forearm.{side}.001"
        parents[f"DEF-breast.{side}"] = "DEF-spine.003"

        for prefix in ("f_index", "f_middle", "f_ring", "f_pinky"):
            parents[f"DEF-{prefix}.01.{side}"] = f"DEF-hand.{side}"
            parents[f"DEF-{prefix}.02.{side}"] = f"DEF-{prefix}.01.{side}"
            parents[f"DEF-{prefix}.03.{side}"] = f"DEF-{prefix}.02.{side}"

        for index in range(1, 5):
            parents[f"DEF-palm.0{index}.{side}"] = f"DEF-hand.{side}"
        for index in range(1, 4):
            parents[f"DEF-thumb.0{index}.{side}"] = (
                f"DEF-hand.{side}" if index == 1
                else f"DEF-thumb.0{index - 1}.{side}"
            )

    missing = sorted(name for name, parent in parents.items()
                     if name != "DEF-spine" and parent is None)
    if missing:
        raise RuntimeError(
            "Clean export hierarchy has unparented deform bones: " +
            ", ".join(missing)
        )
    return parents


def build_clean_export_rig(
    authored_target: bpy.types.Object,
    authored_mesh: bpy.types.Object,
):
    """Clone one authored character into a DEF-only, per-character FBX rig."""
    deform_bones = [bone for bone in authored_target.data.bones
                    if bone.use_deform]
    deform_names = [bone.name for bone in deform_bones]
    if len(deform_names) != 72:
        raise RuntimeError(
            f"Expected 72 authored deform bones, got {len(deform_names)}"
        )

    armature_data = bpy.data.armatures.new(
        authored_target.data.name + "_DEF_EXPORT"
    )
    export_target = bpy.data.objects.new("rig", armature_data)
    bpy.context.scene.collection.objects.link(export_target)
    export_target.matrix_world = authored_target.matrix_world.copy()

    for obj in list(bpy.context.view_layer.objects):
        if obj is not None:
            obj.select_set(False)
    export_target.select_set(True)
    bpy.context.view_layer.objects.active = export_target
    bpy.ops.object.mode_set(mode="EDIT")
    for authored_bone in deform_bones:
        edit_bone = armature_data.edit_bones.new(authored_bone.name)
        edit_bone.head = authored_bone.head_local
        edit_bone.tail = authored_bone.tail_local
        edit_bone.matrix = authored_bone.matrix_local.copy()
        edit_bone.use_deform = True

    parents = clean_parent_map(deform_names)
    for name, parent_name in parents.items():
        edit_bone = armature_data.edit_bones[name]
        edit_bone.parent = (
            armature_data.edit_bones[parent_name]
            if parent_name is not None else None
        )
        edit_bone.use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.view_layer.update()

    export_mesh = authored_mesh.copy()
    export_mesh.data = authored_mesh.data.copy()
    export_mesh.name = "geometry_ORIGINAL"
    bpy.context.scene.collection.objects.link(export_mesh)
    export_mesh.matrix_world = authored_mesh.matrix_world.copy()
    normalize_export_weights(export_mesh, export_target, set(deform_names))
    armature_modifier = next(
        (modifier for modifier in authored_mesh.modifiers
         if modifier.type == "ARMATURE"),
        None,
    )
    for modifier in list(export_mesh.modifiers):
        export_mesh.modifiers.remove(modifier)
    export_modifier = export_mesh.modifiers.new("Armature", "ARMATURE")
    export_modifier.object = export_target
    if armature_modifier is not None:
        export_modifier.use_deform_preserve_volume = \
            armature_modifier.use_deform_preserve_volume

    authored_target.hide_viewport = True
    authored_target.hide_render = True
    authored_mesh.hide_viewport = True
    authored_mesh.hide_render = True
    return export_target, export_mesh


def root_motion_scale(source: bpy.types.Object, target: bpy.types.Object) -> float:
    source_root = source.data.bones.get("mixamorig:Hips")
    source_foot = source.data.bones.get("mixamorig:LeftFoot")
    target_root = target.data.bones.get("DEF-spine")
    target_foot = target.data.bones.get("DEF-foot.L")
    if None in (source_root, source_foot, target_root, target_foot):
        return 1.0

    source_root_position = source_rest_translation(source, source_root)
    source_foot_position = source_rest_translation(source, source_foot)
    target_root_position = target.matrix_world @ target_root.head_local
    target_foot_position = target.matrix_world @ target_foot.head_local
    source_length = (source_foot_position - source_root_position).length
    target_length = (target_foot_position - target_root_position).length
    if source_length <= 0.001 or target_length <= 0.001:
        return 1.0
    return target_length / source_length


def retarget_deform_action(
    target: bpy.types.Object,
    source: bpy.types.Object,
    source_action: bpy.types.Action,
    action_name: str,
    rest_pose,
):
    """Retarget source motion directly onto this character's own DEF bones."""
    assign_action_slot(source, source_action)

    target.animation_data_clear()
    reset_target_pose(target, rest_pose)
    bpy.context.view_layer.update()
    action = bpy.data.actions.new(action_name)
    action.use_fake_user = True
    assign_action_slot(target, action)

    for pose_bone in target.pose.bones:
        pose_bone.rotation_mode = "QUATERNION"
        for constraint in pose_bone.constraints:
            constraint.mute = True

    pairs = build_deform_map(source, target)
    if len(pairs) < 14:
        raise RuntimeError(
            f"{action_name}: only {len(pairs)} authored deform bones matched Mixamo bones"
        )

    scene = bpy.context.scene
    source_start = int(round(source_action.frame_range[0]))
    source_end = int(round(source_action.frame_range[1]))
    if source_end < source_start:
        raise RuntimeError(f"{action_name}: invalid source frame range {source_action.frame_range}")
    frame_count = source_end - source_start + 1
    root_motion_targets = (
        {"DEF-spine"}
        if target.data.bones.get("root") is None
        else {"root", "MCH-torso.parent"}
    )
    root_scale = root_motion_scale(source, target)

    # Retargeting several imported FBX actions in one Blender session leaves
    # the dependency graph on the previous clip's final frame. Wake both the
    # newly assigned source action and the empty target action on a frame
    # before the clip so the first sampled pose cannot inherit stale state.
    scene.frame_set(source_start - 1)
    bpy.context.view_layer.update()

    for source_frame in range(source_start, source_end + 1):
        output_frame = source_frame - source_start + 1
        scene.frame_set(source_frame)
        bpy.context.view_layer.update()
        reset_target_pose(target, rest_pose)
        # The target action is active while keyframe_insert() records the
        # generated curves.  At the next source frame Blender can therefore
        # leave the target pose on the previous action interpolation until
        # the reset is evaluated.  Refresh immediately after the reset so
        # every source frame starts from this rig's own rest pose; otherwise
        # the last squat frame can inherit a different hand/forearm pose and
        # the full standing -> squat -> standing rep will not loop cleanly.
        bpy.context.view_layer.update()

        desired_pose_matrices = {}
        for target_name, target_bone, source_bone in pairs:
            target_pose_bone = target.pose.bones[target_name]
            source_pose_bone = source.pose.bones[source_bone.name]
            parent = target_pose_bone.parent
            parent_pose = (
                desired_pose_matrices[parent.name]
                if parent is not None else Matrix.Identity(4)
            )
            local_rest = rest_local_matrix(target, target_bone)

            if target_name in INHERITED_DEFORM_BONES:
                # These are Rigify's second twist/palm/helper segments. They
                # have no independent Mixamo driver, so their local transform
                # must remain exactly at rest and inherit the animated parent.
                desired_pose = parent_pose @ local_rest
            else:
                source_pose = source_pose_rotation(source, source_pose_bone)
                source_rest = source_rest_rotation(source, source_bone)
                delta = source_pose @ source_rest.inverted()
                desired_world = delta @ target_rest_rotation(target, target_bone)
                desired_object = target_object_rotation(target, desired_world)
                parent_rotation = parent_pose.to_3x3().to_quaternion()
                local_rotation = parent_rotation.inverted() @ desired_object
                local_translation = local_rest.translation.copy()
                if target_name in root_motion_targets:
                    source_delta_world = source_pose_translation(source, source_pose_bone) - \
                        source_rest_translation(source, source_bone)
                    source_delta_world *= root_scale
                    target_delta_object = target.matrix_world.to_3x3().inverted() @ source_delta_world
                    local_translation += target_delta_object
                desired_local = (
                    Matrix.Translation(local_translation)
                    @ local_rotation.to_matrix().to_4x4()
                )
                desired_pose = parent_pose @ desired_local

            desired_pose_matrices[target_name] = desired_pose
            if parent is None:
                matrix_basis = target_bone.convert_local_to_pose(
                    desired_pose,
                    target_bone.matrix_local,
                    invert=True,
                )
            else:
                matrix_basis = target_bone.convert_local_to_pose(
                    desired_pose,
                    target_bone.matrix_local,
                    parent_matrix=parent_pose,
                    parent_matrix_local=parent.bone.matrix_local,
                    invert=True,
                )
            target_pose_bone.matrix_basis = matrix_basis
            target_pose_bone.keyframe_insert(
                data_path="rotation_quaternion",
                frame=output_frame,
                group=action_name,
            )
            if target_name in root_motion_targets:
                target_pose_bone.keyframe_insert(
                    data_path="location",
                    frame=output_frame,
                    group=action_name,
                )

    action.frame_start = 1
    action.frame_end = frame_count
    action.use_fake_user = True
    return action, len(pairs), frame_count


def delete_imported_objects(imported) -> None:
    for obj in imported:
        if obj.name in bpy.data.objects:
            bpy.data.objects.remove(obj, do_unlink=True)


def add_nla_strips(target: bpy.types.Object, actions) -> None:
    target.animation_data_clear()
    animation_data = target.animation_data_create()
    for action in actions:
        track = animation_data.nla_tracks.new()
        track.name = action.name
        strip = track.strips.new(action.name, 1, action)
        strip.action_frame_start = action.frame_start
        strip.action_frame_end = action.frame_end
        strip.frame_start = 1
        strip.frame_end = action.frame_end - action.frame_start + 1
        strip.extrapolation = "NOTHING"


def select_export_objects(target: bpy.types.Object, mesh: bpy.types.Object) -> None:
    bpy.ops.object.select_all(action="DESELECT")
    target.select_set(True)
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = target


def export_fbx(output: Path, target: bpy.types.Object, mesh: bpy.types.Object) -> None:
    output.parent.mkdir(parents=True, exist_ok=True)
    select_export_objects(target, mesh)
    # The clean action is already keyed on the deform bones. Leave Rigify
    # constraints muted during the FBX serialization step so the exporter
    # cannot evaluate control/deform scale artifacts back into the clip.
    for pose_bone in target.pose.bones:
        for constraint in pose_bone.constraints:
            constraint.mute = True
    bpy.ops.export_scene.fbx(
        filepath=str(output),
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        add_leaf_bones=False,
        use_armature_deform_only=True,
        # Each authored clip is a separate Action datablock. Export all of
        # them directly instead of stacking overlapping NLA strips: an NLA
        # stack starting every strip at frame 1 can alter the first sampled
        # frame of later clips during FBX serialization.
        # Keep all-bones baking disabled. The generated DEF rig already has
        # explicit curves for every deform bone; Blender 4.5's force-key
        # path can re-evaluate the clean hierarchy through the source
        # control graph and collapse intermediate poses on re-import.
        bake_anim=True,
        bake_anim_use_all_bones=False,
        bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
        path_mode="STRIP",
        embed_textures=False,
    )


def export_authored_base_color(mesh: bpy.types.Object, output: Path) -> None:
    """Write the image actually connected to the source material Base Color."""
    material = mesh.active_material
    if material is None or not material.use_nodes or material.node_tree is None:
        raise RuntimeError(f"{mesh.name}: authored material has no node tree")

    image = None
    for node in material.node_tree.nodes:
        if node.type != "BSDF_PRINCIPLED":
            continue
        base_color = node.inputs.get("Base Color")
        if base_color is None:
            continue
        for link in base_color.links:
            source = link.from_node
            if source is not None and source.type == "TEX_IMAGE" and source.image is not None:
                image = source.image
                break
        if image is not None:
            break
    if image is None:
        raise RuntimeError(
            f"{mesh.name}: authored Base Color has no connected image texture"
        )

    output.parent.mkdir(parents=True, exist_ok=True)
    original_path = image.filepath_raw
    original_format = image.file_format
    try:
        image.filepath_raw = str(output)
        image.file_format = "PNG"
        image.save()
    finally:
        image.filepath_raw = original_path
        image.file_format = original_format
    print(
        f"GYMCHAOS_AUTHORED_TEXTURE_OK mesh={mesh.name} "
        f"image={image.name} output={output}"
    )


def run(character_name: str) -> None:
    config = CHARACTERS[character_name]
    blend = config["blend"]
    if not blend.exists():
        raise FileNotFoundError(blend)

    bpy.ops.wm.open_mainfile(filepath=str(blend))
    authored_target = find_armature()
    authored_mesh = find_mesh(authored_target)
    validate_authored_source(character_name, authored_target, authored_mesh)
    texture_output = config.get("texture_output")
    if texture_output is not None:
        export_authored_base_color(authored_mesh, texture_output)
    keep_only_target_objects(authored_target, authored_mesh)
    target, mesh = build_clean_export_rig(authored_target, authored_mesh)

    rest_pose = {
        pose_bone.name: pose_bone.matrix.copy() for pose_bone in target.pose.bones
    }
    actions = []
    report = []
    for clip_name in config["clip_names"]:
        if clip_name in PLAYER_ONLY_CLIPS:
            clip_path = config["clips"] / "player_only" / f"{clip_name}.fbx"
        else:
            clip_path = config["clips"] / f"{clip_name}.fbx"
        if not clip_path.exists():
            raise FileNotFoundError(clip_path)
        imported, source, source_action = import_animation(clip_path)
        try:
            action, matched, frames = retarget_deform_action(
                target, source, source_action, clip_name, rest_pose
            )
            actions.append(action)
            report.append(f"{clip_name}:{frames}f/{matched}bones")
        finally:
            delete_imported_objects(imported)

    target.animation_data_clear()
    if actions:
        # Keep the first authored action active while the exporter walks all
        # Action datablocks. This avoids the first exported frame being
        # evaluated against the final action in Blender 4.5.
        assign_action_slot(target, actions[0])
    # Force a real frame change before FBX's all-actions pass. Blender 4.5
    # assigns each Action and then samples its first frame; if the scene is
    # already on frame 1, the dependency graph can keep the previous action's
    # pose for that first sample only.
    bpy.context.scene.frame_set(0)
    bpy.context.view_layer.update()
    if not bpy.context.scene.frame_end > 0 or not actions:
        raise RuntimeError("Cannot configure FBX animation frame range without authored actions")
    bpy.context.scene.frame_start = 1
    bpy.context.scene.frame_end = max(
        int(round(action.frame_end)) for action in actions
    )
    export_fbx(config["output"], target, mesh)
    print(
        "GYMCHAOS_AUTHORED_EXPORT_OK "
        f"character={character_name} output={config['output']} "
        f"mesh={mesh.name}:{len(mesh.data.vertices)} "
        f"clips={'|'.join(report)}"
    )


if __name__ == "__main__":
    args = parse_args()
    if args.character == "all":
        for character_name in sorted(CHARACTERS):
            run(character_name)
        print(f"GYMCHAOS_AUTHORED_BATCH_OK characters={len(CHARACTERS)}")
    else:
        run(args.character)
