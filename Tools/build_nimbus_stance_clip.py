"""Author Goku's flying-nimbus stance as a Mixamo clip (anims/nimbus_stance.fbx).

Reference: the Ichiban "Goku on the Flying Nimbus" figure. Wide stance with
both knees bent and both soles flat, feet spread along the cloud's length,
chest to the front, head turned over the right shoulder, right fist driven
out to that side at head height and the left fist chambered at the hip.

The pose is built on the Mixamo skeleton of anims/idle1.fbx with IK
constraints (feet, hands, knee and elbow poles), baked to a short held
action and exported as an armature-only FBX, so
Tools/export_authored_character_fbx.py retargets it like every other clip.
In the clip the character faces Mixamo forward (-Y), the cloud's travel
direction (the cloud flies broadside). Ankles stay on the clip's ground plane
(z = 0 under the soles); the game places the soles on the cloud.

Usage:
  blender -b --factory-startup -P Tools/build_nimbus_stance_clip.py
"""
import math
from pathlib import Path

import bpy
from mathutils import Euler, Matrix, Quaternion, Vector

ROOT = Path(__file__).resolve().parents[1]
ANIMS = ROOT / "Assets/BodyBuilders/enemies/anims"
SOURCE = ANIMS / "idle1.fbx"
OUTPUT = ANIMS / "nimbus_stance.fbx"
FRAMES = 30

# World metres on the Mixamo rest skeleton (1.82 m, hips 1.04 m, facing -Y,
# right side -X). The chest keeps facing forward (-Y, the cloud's travel
# direction: the cloud flies broadside); the feet spread across, along the
# cloud's length; the head and the right fist turn to the right.
HIPS_DROP = 0.24            # knee bend lowers the pelvis
HIPS_YAW = -12.0            # hips open a little toward the punching side
TORSO_LEAN = 6.0            # forward
ANKLE_Z = 0.087
TARGETS = {
    # bone: (IK target, pole target)
    "mixamorig:RightLeg": ((-0.37, -0.02, ANKLE_Z), (-0.75, -0.95, 0.55)),
    "mixamorig:LeftLeg": ((0.35, 0.05, ANKLE_Z), (0.75, -0.95, 0.55)),
    "mixamorig:RightForeArm": ((-0.60, -0.34, 1.42), (-0.30, 0.45, 0.80)),
    "mixamorig:LeftForeArm": ((0.24, -0.02, 0.88), (0.45, 0.55, 1.00)),
}
# Soles flat, toes turned out.
TOES = {
    "mixamorig:RightFoot": Vector((-0.6, -0.8, 0.0)),
    "mixamorig:LeftFoot": Vector((0.5, -0.85, 0.0)),
}
# Head over the right shoulder; the chest twists a little with it.
TWIST = {"mixamorig:Spine2": -10.0, "mixamorig:Neck": -28.0, "mixamorig:Head": -32.0}
# Fist: every finger joint flexed, thumb wrapped over.
FINGER_CURL = 85.0
THUMB_CURL = 35.0


def world_rotation_about_z(pose_bone, arm, degrees):
    world = arm.matrix_world @ pose_bone.matrix
    pivot = world.translation.copy()
    turn = Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(degrees), 4, "Z") @ Matrix.Translation(-pivot)
    pose_bone.matrix = arm.matrix_world.inverted() @ turn @ world


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(SOURCE), use_anim=True, use_image_search=False,
                             automatic_bone_orientation=False)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    for obj in list(bpy.data.objects):
        if obj is not arm:
            bpy.data.objects.remove(obj, do_unlink=True)
    arm.animation_data_clear()
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)
    scene = bpy.context.scene
    scene.frame_start, scene.frame_end = 1, FRAMES
    scene.frame_set(1)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="POSE")
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
        pb.location = (0, 0, 0)
        pb.rotation_quaternion = (1, 0, 0, 0)
    bpy.context.view_layer.update()

    # Pelvis: lower, turn the chest to +X, lean slightly into the chest.
    hips = arm.pose.bones["mixamorig:Hips"]
    world = arm.matrix_world @ hips.matrix
    rot = (Matrix.Rotation(math.radians(TORSO_LEAN), 4, "X") @
           Matrix.Rotation(math.radians(HIPS_YAW), 4, "Z"))
    new_world = Matrix.Translation(world.translation + Vector((0, 0, -HIPS_DROP))) @ rot @ \
        Matrix.Translation(-world.translation) @ world
    new_world.translation = world.translation + Vector((0, 0, -HIPS_DROP))
    hips.matrix = arm.matrix_world.inverted() @ new_world
    bpy.context.view_layer.update()
    for name, degrees in TWIST.items():
        world_rotation_about_z(arm.pose.bones[name], arm, degrees)
        bpy.context.view_layer.update()

    empties = []

    def empty(name, location):
        obj = bpy.data.objects.new(name, None)
        obj.location = location
        scene.collection.objects.link(obj)
        empties.append(obj)
        return obj

    for bone, (target, pole) in TARGETS.items():
        constraint = arm.pose.bones[bone].constraints.new("IK")
        constraint.target = empty(bone + "_ik", Vector(target))
        constraint.pole_target = empty(bone + "_pole", Vector(pole))
        constraint.chain_count = 2
        # The pole angle depends on the bone roll: take the one that puts the
        # knee / elbow nearest its pole target.
        best = None
        for degrees in range(-180, 180, 15):
            constraint.pole_angle = math.radians(degrees)
            bpy.context.view_layer.update()
            joint = arm.matrix_world @ arm.pose.bones[bone].head
            distance = (joint - Vector(pole)).length
            if best is None or distance < best[0]:
                best = (distance, degrees)
        constraint.pole_angle = math.radians(best[1])
        print(f"NIMBUS_POLE {bone} angle={best[1]}")
    bpy.context.view_layer.update()
    for bone, direction in TOES.items():
        pb = arm.pose.bones[bone]
        ankle = arm.matrix_world @ pb.head
        toe = ankle + direction.normalized() * 0.14
        toe.z = 0.0
        track = pb.constraints.new("DAMPED_TRACK")
        track.target = empty(bone + "_toe", toe)
        track.track_axis = "TRACK_Y"
    bpy.context.view_layer.update()

    # Bake the constrained pose to plain keys on frames 1 and FRAMES.
    bpy.ops.pose.select_all(action="SELECT")
    bpy.ops.nla.bake(frame_start=1, frame_end=FRAMES, step=FRAMES - 1, only_selected=True,
                     visual_keying=True, clear_constraints=True, use_current_action=False,
                     bake_types={"POSE"})
    action = arm.animation_data.action
    action.name = "nimbus_stance"
    for obj in empties:
        bpy.data.objects.remove(obj, do_unlink=True)

    # Fists: flex the finger joints on top of the baked pose.
    for frame in (1, FRAMES):
        scene.frame_set(frame)
        for pb in arm.pose.bones:
            name = pb.name
            if "Hand" not in name or name.endswith("Hand") or name.endswith("4"):
                continue
            curl = THUMB_CURL if "Thumb" in name else FINGER_CURL
            if "Thumb" in name:
                # Thumb wraps across the fingers.
                pb.rotation_quaternion = Quaternion((0, 0, 1), math.radians(-curl if "Left" in name else curl))
            else:
                pb.rotation_quaternion = Quaternion((1, 0, 0), math.radians(curl))
            pb.keyframe_insert("rotation_quaternion", frame=frame)
    bpy.ops.object.mode_set(mode="OBJECT")

    # Report where the soles and fists ended up (frame 1).
    scene.frame_set(1)
    bpy.context.view_layer.update()

    def head(name):
        return arm.matrix_world @ arm.pose.bones[name].head

    for name in ("mixamorig:LeftToeBase", "mixamorig:RightToeBase", "mixamorig:LeftFoot",
                 "mixamorig:RightFoot", "mixamorig:LeftHand", "mixamorig:RightHand",
                 "mixamorig:Hips", "mixamorig:Head"):
        p = head(name)
        print(f"NIMBUS_POSE {name} ({p.x:.3f}, {p.y:.3f}, {p.z:.3f})")

    # An armature-only FBX has no bind pose, so the importer would take the
    # held pose as the skeleton's rest and the retarget would see no motion.
    # A tiny mesh skinned to the hips makes the exporter write the bind pose.
    bpy.ops.mesh.primitive_cube_add(size=0.02)
    anchor = bpy.context.active_object
    anchor.name = "BindPoseAnchor"
    group = anchor.vertex_groups.new(name="mixamorig:Hips")
    group.add([v.index for v in anchor.data.vertices], 1.0, "REPLACE")
    anchor.parent = arm
    modifier = anchor.modifiers.new("Armature", "ARMATURE")
    modifier.object = arm
    for obj in bpy.data.objects:
        obj.select_set(obj is arm or obj is anchor)
    bpy.ops.export_scene.fbx(
        filepath=str(OUTPUT), use_selection=True, object_types={"ARMATURE", "MESH"},
        add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=False,
        bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
        bake_anim_simplify_factor=0.0, apply_unit_scale=True)
    print(f"GYMCHAOS_NIMBUS_STANCE_CLIP_OK output={OUTPUT} frames={FRAMES}")


if __name__ == "__main__":
    main()
