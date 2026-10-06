"""Validate the canonical skinless squat FBX without changing it."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--runtime", required=False, type=Path)
    arguments = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return parser.parse_args(arguments)


SOURCE_RUNTIME_BONE_PAIRS = (
    ("mixamorig:Hips", "DEF-spine"),
    ("mixamorig:Spine", "DEF-spine.001"),
    ("mixamorig:Spine1", "DEF-spine.002"),
    ("mixamorig:Spine2", "DEF-spine.003"),
    ("mixamorig:Neck", "DEF-spine.004"),
    ("mixamorig:Head", "DEF-spine.005"),
    ("mixamorig:LeftShoulder", "DEF-shoulder.L"),
    ("mixamorig:LeftArm", "DEF-upper_arm.L"),
    ("mixamorig:LeftForeArm", "DEF-forearm.L"),
    ("mixamorig:LeftHand", "DEF-hand.L"),
    ("mixamorig:LeftUpLeg", "DEF-thigh.L"),
    ("mixamorig:LeftLeg", "DEF-shin.L"),
    ("mixamorig:LeftFoot", "DEF-foot.L"),
    ("mixamorig:RightShoulder", "DEF-shoulder.R"),
    ("mixamorig:RightArm", "DEF-upper_arm.R"),
    ("mixamorig:RightForeArm", "DEF-forearm.R"),
    ("mixamorig:RightHand", "DEF-hand.R"),
    ("mixamorig:RightUpLeg", "DEF-thigh.R"),
    ("mixamorig:RightLeg", "DEF-shin.R"),
    ("mixamorig:RightFoot", "DEF-foot.R"),
)


# Tools/squat_contact_bake.py re-solves the leg chains for ground contact,
# so legs may differ from the canonical clip; every other bone must match.
CONTACT_BAKED_RUNTIME_BONES = {
    "DEF-thigh.L", "DEF-shin.L", "DEF-foot.L",
    "DEF-thigh.R", "DEF-shin.R", "DEF-foot.R",
}
MAX_UPPER_BODY_DELTA_DEGREES = 0.25
MAX_CONTACT_BAKED_LEG_DELTA_DEGREES = 25.0


def import_animation_asset(path: Path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(
        filepath=str(path),
        use_anim=True,
        use_image_search=False,
        automatic_bone_orientation=False,
    )
    armatures = [obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"]
    if len(armatures) != 1:
        raise RuntimeError(
            f"{path}: expected one armature, got {len(armatures)}"
        )
    actions = list(bpy.data.actions)
    if not actions:
        raise RuntimeError(f"{path}: expected at least one animation action")
    armature = armatures[0]
    action = next(
        (
            candidate for candidate in actions
            if candidate.name.lower().endswith("|squat")
        ),
        actions[0],
    )
    armature.animation_data_create()
    armature.animation_data.action = action
    if getattr(action, "slots", None) and len(action.slots) > 0:
        armature.animation_data.action_slot = action.slots[0]
    return armature, action


def pose_rotation_delta(armature, bone_name):
    pose_bone = armature.pose.bones.get(bone_name)
    if pose_bone is None:
        raise RuntimeError(f"{armature.name}: missing pose bone {bone_name}")
    pose_world = armature.matrix_world @ pose_bone.matrix
    rest_world = armature.matrix_world @ pose_bone.bone.matrix_local
    return pose_world.to_quaternion() @ rest_world.to_quaternion().inverted()


def compare_runtime_animation(source: Path, runtime: Path) -> dict:
    source_armature, source_action = import_animation_asset(source)
    source_start = int(round(source_action.frame_range[0]))
    source_end = int(round(source_action.frame_range[1]))
    if (source_start, source_end) != (1, 69):
        raise RuntimeError(
            f"source squat action range is {source_start}-{source_end}, expected 1-69"
        )

    source_deltas = []
    for frame in range(source_start, source_end + 1):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        source_deltas.append(
            [
                pose_rotation_delta(source_armature, source_bone)
                for source_bone, _ in SOURCE_RUNTIME_BONE_PAIRS
            ]
        )

    runtime_armature, runtime_action = import_animation_asset(runtime)
    runtime_start = int(round(runtime_action.frame_range[0]))
    runtime_end = int(round(runtime_action.frame_range[1]))
    if (runtime_start, runtime_end) != (1, 69):
        raise RuntimeError(
            f"runtime squat action range is {runtime_start}-{runtime_end}, expected 1-69"
        )

    max_delta_degrees = 0.0
    max_leg_delta_degrees = 0.0
    worst_leg = "none"
    total_delta_degrees = 0.0
    comparison_count = 0
    worst = "none"
    for frame_index, frame in enumerate(range(runtime_start, runtime_end + 1)):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        for pair_index, (_, runtime_bone) in enumerate(SOURCE_RUNTIME_BONE_PAIRS):
            source_delta = source_deltas[frame_index][pair_index]
            runtime_delta = pose_rotation_delta(runtime_armature, runtime_bone)
            difference = source_delta.rotation_difference(runtime_delta)
            degrees = math.degrees(abs(float(difference.angle)))
            degrees = min(degrees, abs(360.0 - degrees))
            total_delta_degrees += degrees
            comparison_count += 1
            if runtime_bone in CONTACT_BAKED_RUNTIME_BONES:
                if degrees > max_leg_delta_degrees:
                    max_leg_delta_degrees = degrees
                    worst_leg = f"frame={frame} runtime={runtime_bone}"
                continue
            if degrees > max_delta_degrees:
                max_delta_degrees = degrees
                worst = f"frame={frame} source={SOURCE_RUNTIME_BONE_PAIRS[pair_index][0]} runtime={runtime_bone}"

    runtime_sha256 = hashlib.sha256(runtime.read_bytes()).hexdigest().upper()
    mean_delta_degrees = total_delta_degrees / max(comparison_count, 1)
    return {
        "runtime_path": str(runtime.resolve()),
        "runtime_sha256": runtime_sha256,
        "runtime_size_bytes": runtime.stat().st_size,
        "runtime_action": runtime_action.name,
        "runtime_frame_range": [runtime_start, runtime_end],
        "comparison_method": "Blender FBX round-trip world rotation delta",
        "matched_bone_pairs": len(SOURCE_RUNTIME_BONE_PAIRS),
        "compared_frames": source_end - source_start + 1,
        "comparison_count": comparison_count,
        # Upper body (non-leg bones) only; legs are reported separately.
        "max_rotation_delta_degrees": max_delta_degrees,
        "leg_max_rotation_delta_degrees": max_leg_delta_degrees,
        "worst_leg_rotation_delta": worst_leg,
        "mean_rotation_delta_degrees": mean_delta_degrees,
        "worst_rotation_delta": worst,
        "pass": max_delta_degrees <= MAX_UPPER_BODY_DELTA_DEGREES
        and max_leg_delta_degrees <= MAX_CONTACT_BAKED_LEG_DELTA_DEGREES,
    }


def finite_vector(values) -> bool:
    return all(math.isfinite(float(value)) for value in values)


def validate(source: Path) -> dict:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(
        filepath=str(source),
        use_anim=True,
        use_image_search=False,
        automatic_bone_orientation=False,
    )

    objects = list(bpy.context.scene.objects)
    armatures = [obj for obj in objects if obj.type == "ARMATURE"]
    actions = list(bpy.data.actions)
    frame_ranges = [
        [float(action.frame_range[0]), float(action.frame_range[1])]
        for action in actions
    ]
    action_scale_paths = []
    non_identity_scale_paths = []
    for action in actions:
        for fcurve in action.fcurves:
            if ".scale" not in fcurve.data_path:
                continue
            action_scale_paths.append(fcurve.data_path)
            if any(abs(float(point.co[1]) - 1.0) > 0.001
                   for point in fcurve.keyframe_points):
                non_identity_scale_paths.append(fcurve.data_path)
    sampled_frames = [1, 35, 69]
    max_pose_scale_delta = 0.0
    invalid_pose_scales = []
    if armatures:
        armature = armatures[0]
        for frame in sampled_frames:
            bpy.context.scene.frame_set(frame)
            bpy.context.view_layer.update()
            for pose_bone in armature.pose.bones:
                if not finite_vector(pose_bone.scale):
                    invalid_pose_scales.append(
                        f"{frame}:{pose_bone.name}:non_finite"
                    )
                    continue
                delta = max(abs(float(value) - 1.0) for value in pose_bone.scale)
                max_pose_scale_delta = max(max_pose_scale_delta, delta)
                if delta > 0.001:
                    invalid_pose_scales.append(
                        f"{frame}:{pose_bone.name}:{tuple(pose_bone.scale)}"
                    )

    object_scale = tuple(armatures[0].scale) if armatures else ()
    object_rotation = tuple(armatures[0].rotation_euler) if armatures else ()
    result = {
        "input": str(source.resolve()),
        "source_sha256": hashlib.sha256(source.read_bytes()).hexdigest().upper(),
        "source_size_bytes": source.stat().st_size,
        "object_names": [obj.name for obj in objects],
        "object_types": [obj.type for obj in objects],
        "armature_count": len(armatures),
        "mesh_count": sum(obj.type == "MESH" for obj in objects),
        "camera_count": sum(obj.type == "CAMERA" for obj in objects),
        "light_count": sum(obj.type == "LIGHT" for obj in objects),
        "action_count": len(actions),
        "action_names": [action.name for action in actions],
        "action_frame_ranges": frame_ranges,
        "sampled_frames": sampled_frames,
        "max_pose_scale_delta": max_pose_scale_delta,
        "invalid_pose_scales": invalid_pose_scales,
        "action_scale_paths": sorted(set(action_scale_paths)),
        "non_identity_scale_paths": sorted(set(non_identity_scale_paths)),
        "armature_object_scale": object_scale,
        "armature_object_rotation": object_rotation,
        "pass": (
            len(objects) == 1
            and len(armatures) == 1
            and len(actions) == 1
            and frame_ranges == [[1.0, 69.0]]
            and not any(obj.type == "MESH" for obj in objects)
            and not any(obj.type == "CAMERA" for obj in objects)
            and not any(obj.type == "LIGHT" for obj in objects)
            and not invalid_pose_scales
            and not non_identity_scale_paths
        ),
    }
    return result


def main() -> None:
    args = parse_args()
    result = validate(args.input.resolve())
    if args.runtime is not None:
        result["runtime_comparison"] = compare_runtime_animation(
            args.input.resolve(), args.runtime.resolve()
        )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    if not result["pass"] or (
        args.runtime is not None and
        not result["runtime_comparison"]["pass"]
    ):
        raise SystemExit(
            "GYMCHAOS_SQUAT_SOURCE_FAIL "
            + json.dumps(result, separators=(",", ":"))
        )
    print(
        "GYMCHAOS_SQUAT_SOURCE_OK "
        f"objects={len(result['object_names'])} armatures={result['armature_count']} "
        f"actions={result['action_count']} frame=1-69 "
        f"maxPoseScaleDelta={result['max_pose_scale_delta']:.6f}"
    )


if __name__ == "__main__":
    main()
