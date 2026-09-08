"""Compare baked authored FBX poses against their source Mixamo animation."""

from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy

sys.path.insert(0, str(Path(__file__).resolve().parent))
import export_authored_character_fbx as exporter


def action_for(clip_name):
    matches = [
        action for action in bpy.data.actions
        if action.name.lower() == clip_name.lower()
        or action.name.lower().endswith("|" + clip_name.lower())
    ]
    if len(matches) != 1:
        raise RuntimeError(f"{clip_name}: expected one target action, got {[a.name for a in matches]}")
    return matches[0]


def verify(character):
    config = exporter.CHARACTERS[character]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(
        filepath=str(config["output"]),
        use_anim=True,
        use_image_search=False,
        automatic_bone_orientation=False,
    )
    target = next(obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
    target_actions = {name: action_for(name) for name in config["clip_names"]}
    worst_error = 0.0
    worst_detail = "none"

    for clip_name in config["clip_names"]:
        clip_path = config["clips"] / (
            f"player_only/{clip_name}.fbx"
            if clip_name in exporter.PLAYER_ONLY_CLIPS
            else f"{clip_name}.fbx"
        )
        imported, source, source_action = exporter.import_animation(clip_path)
        target_action = target_actions[clip_name]
        exporter.assign_action_slot(target, target_action)
        source_start = int(round(source_action.frame_range[0]))
        source_end = int(round(source_action.frame_range[1]))
        if source_start != 1:
            raise RuntimeError(f"{clip_name}: unsupported source start {source_start}")
        frames = (source_start, int(round((source_start + source_end) * 0.5)), source_end)
        pairs = exporter.build_deform_map(source, target)
        for frame in frames:
            bpy.context.scene.frame_set(frame)
            bpy.context.view_layer.update()
            for target_name, target_bone, source_bone in pairs:
                if target_name in exporter.INHERITED_DEFORM_BONES:
                    continue
                source_pose = exporter.source_pose_rotation(source, source.pose.bones[source_bone.name])
                source_rest = exporter.source_rest_rotation(source, source_bone)
                expected = source_pose @ source_rest.inverted() @ exporter.target_rest_rotation(target, target_bone)
                actual = (
                    target.matrix_world.to_3x3()
                    @ target.pose.bones[target_name].matrix.to_3x3()
                ).to_quaternion()
                raw_error = math.degrees(actual.rotation_difference(expected).angle)
                error = min(raw_error, 360.0 - raw_error)
                if error > worst_error:
                    worst_error = error
                    worst_detail = f"{clip_name}@{frame}:{target_name}"
        exporter.delete_imported_objects(imported)

    if worst_error > 1.0:
        raise RuntimeError(
            f"{character}: baked pose differs from source retarget by {worst_error:.2f} degrees "
            f"at {worst_detail}"
        )
    print(
        f"GYMCHAOS_RETARGET_POSE_FIDELITY_OK character={character} "
        f"maxAngularError={worst_error:.3f} detail={worst_detail}"
    )


requested = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
characters = requested or list(exporter.CHARACTERS)
for character_name in characters:
    verify(character_name)
print(f"GYMCHAOS_RETARGET_POSE_FIDELITY_BATCH_OK characters={len(characters)}")
