"""Bake fixed-length leg contact into each character's own exported skeleton."""
import math
import bpy
from mathutils import Vector


def bake_contacts(target, anchors, frame, action_name):
    bpy.context.view_layer.update()
    chains = []
    for side in ("L", "R"):
        thigh = target.pose.bones[f"DEF-thigh.{side}"]
        shin = target.pose.bones[f"DEF-shin.{side}"]
        foot = target.pose.bones[f"DEF-foot.{side}"]
        if side not in anchors:
            anchors[side] = (foot.matrix.translation.copy(),
                             foot.matrix.to_quaternion().copy())
        upper = (shin.bone.head_local - thigh.bone.head_local).length
        lower = (foot.bone.head_local - shin.bone.head_local).length
        chains.append((thigh, shin, foot, upper, lower, anchors[side]))

    # One pelvis translation makes both fixed-length chains reachable.
    root = target.pose.bones["DEF-spine"]
    correction = 0.0
    for thigh, shin, foot, upper, lower, (ankle, rotation) in chains:
        offset = thigh.matrix.translation - ankle
        horizontal_sq = offset.x ** 2 + offset.y ** 2
        reach = (upper + lower) * 0.999
        if horizontal_sq >= reach ** 2:
            raise RuntimeError(f"{target.name}: unreachable squat stance at frame {frame}")
        height = math.sqrt(reach ** 2 - horizontal_sq)
        correction = max(correction, offset.z - height)
    if correction > 0:
        matrix = root.matrix.copy()
        matrix.translation.z -= correction
        root.matrix = matrix
        bpy.context.view_layer.update()

    for thigh, shin, foot, upper, lower, (ankle, foot_rotation) in chains:
        hip = thigh.matrix.translation.copy()
        knee = shin.matrix.translation.copy()
        delta = ankle - hip
        distance = delta.length
        if distance < abs(upper - lower) + 1e-5 or distance >= upper + lower:
            raise RuntimeError(f"{target.name}: invalid squat reach at frame {frame}")
        axis = delta.normalized()
        bend = knee - hip
        bend -= axis * bend.dot(axis)
        if bend.length < 1e-5:
            bend = Vector((0, -1, 0))
            bend -= axis * bend.dot(axis)
        bend.normalize()
        along = (upper * upper - lower * lower + distance * distance) / (2 * distance)
        radius = math.sqrt(max(0, upper * upper - along * along))
        desired_knee = hip + axis * along + bend * radius

        def aim(bone, current, desired):
            matrix = bone.matrix.copy()
            rotation = current.normalized().rotation_difference(desired.normalized())
            result = (rotation @ matrix.to_quaternion()).to_matrix().to_4x4()
            result.translation = matrix.translation
            bone.matrix = result
            bpy.context.view_layer.update()

        aim(thigh, shin.matrix.translation - hip, desired_knee - hip)
        aim(shin, foot.matrix.translation - shin.matrix.translation,
            ankle - shin.matrix.translation)
        matrix = foot_rotation.to_matrix().to_4x4()
        matrix.translation = foot.matrix.translation
        foot.matrix = matrix
        bpy.context.view_layer.update()
        for bone in (thigh, shin, foot):
            bone.scale = (1, 1, 1)
            bone.keyframe_insert(data_path="rotation_quaternion", frame=frame, group=action_name)
    root.keyframe_insert(data_path="location", frame=frame, group=action_name)
