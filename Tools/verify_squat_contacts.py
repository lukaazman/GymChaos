"""Check every exported squat frame independently of the contact baker."""
import hashlib
import json
import sys
from pathlib import Path

import bpy

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "Tools"))
from export_authored_character_fbx import assign_action_slot

report = {}
for name in ("arnold", "cbum", "goku", "jaycutler", "ronnie", "davie", "zyzz"):
    path = ROOT / f"GymChaos/Assets/Resources/Characters/Enemies/{name}_authored.fbx"
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=True, automatic_bone_orientation=False)
    rig = next(obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
    action = next(a for a in bpy.data.actions if a.name.lower().endswith("|squat"))
    assign_action_slot(rig, action)
    scene = bpy.context.scene
    scene.frame_set(0)
    scene.frame_set(1)
    bpy.context.view_layer.update()
    def position(bone):
        return rig.matrix_world @ rig.pose.bones[bone].matrix.translation
    anchors = {side: position(f"DEF-foot.{side}") for side in ("L", "R")}
    lengths = {}
    for side in ("L", "R"):
        for start, end in (("thigh", "shin"), ("shin", "foot")):
            pair = (f"DEF-{start}.{side}", f"DEF-{end}.{side}")
            lengths[pair] = (position(pair[1]) - position(pair[0])).length
    leg = sum(lengths.values()) / 2
    first_hip = position("DEF-spine").z
    max_drift = max_length_error = max_scale_error = hip_drop = 0.0
    for frame in range(1, 70):
        scene.frame_set(frame)
        bpy.context.view_layer.update()
        for side, anchor in anchors.items():
            max_drift = max(max_drift, (position(f"DEF-foot.{side}") - anchor).length)
        for pair, length in lengths.items():
            max_length_error = max(max_length_error,
                abs((position(pair[1]) - position(pair[0])).length / length - 1))
        for bone in rig.pose.bones:
            max_scale_error = max(max_scale_error, max(abs(v - 1) for v in bone.scale))
        hip_drop = max(hip_drop, first_hip - position("DEF-spine").z)
    result = dict(frames=69, foot_drift=max_drift, foot_drift_leg_ratio=max_drift / leg,
                  length_error=max_length_error, scale_error=max_scale_error,
                  hip_drop_leg_ratio=hip_drop / leg,
                  sha256=hashlib.sha256(path.read_bytes()).hexdigest())
    result["pass"] = max_drift / leg < 0.002 and max_length_error < 0.002 and max_scale_error < 0.002 and hip_drop / leg > 0.15
    report[name] = result
    print("SQUAT_CONTACT_RESULT", name, json.dumps(result), flush=True)
output = ROOT / ".unlazy/gymchaos-legacy-goal-20260924/squat-rebuild-contact-report.json"
output.write_text(json.dumps(report, indent=2), encoding="utf-8")
if not all(item["pass"] for item in report.values()):
    raise RuntimeError("Exported squat contact contract failed; see report")
