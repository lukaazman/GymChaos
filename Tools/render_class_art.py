"""Render the five class illustrations from the game's own class bodies.

Run headless:
    blender -b -P Tools/render_class_art.py            # all five
    blender -b -P Tools/render_class_art.py -- cardio  # one class

Each class body (shape key from player_classes.blend, exaggerated for the
illustration) is posed on the DEF skeleton, rendered in three Workbench passes
(flat albedo, studio shading, region ids) and composited into a limited-palette
ink illustration in the boot-menu style: paper-white body, black shadow shapes,
halftone midtones, red and navy clothing, ink outline with a paper cut-out rim.
The canvas is transparent, 4:5, with identical camera scale for every class.
No lettering is drawn; names stay real UI text.

Outputs:
    Assets/UI/Classes/<id>.png                         2048x2560 source
    GymChaos/Assets/Resources/Classes/Art/<id>.png     1024x1280 runtime
"""

from __future__ import annotations

import json
import math
import sys
import tempfile
from pathlib import Path

import bpy
import numpy as np
from mathutils import Matrix, Vector

TOOLS = Path(__file__).resolve().parent
PROJECT_ROOT = TOOLS.parent
sys.path.insert(0, str(TOOLS))

import build_player_classes as build  # noqa: E402
import export_authored_character_fbx as authored  # noqa: E402

SOURCE_DIR = PROJECT_ROOT / "Assets/UI/Classes"
RUNTIME_DIR = PROJECT_ROOT / "GymChaos/Assets/Resources/Classes/Art"
WIDTH, HEIGHT = 2048, 2560
# World units across the canvas height; shared so every class has the same scale.
CANVAS_HEIGHT = 1.24

INK = np.array([0.035, 0.035, 0.045])
PAPER = np.array([0.97, 0.97, 0.95])
ACCENT = np.array([0.93, 0.20, 0.09])
NAVY = np.array([0.03, 0.16, 0.36])
STEEL = np.array([0.22, 0.30, 0.45])
GOLD = np.array([1.0, 0.82, 0.35])

# How far past the gameplay body the illustration pushes each class shape.
EXAGGERATION = {"bodybuilding": 0.0, "calisthenics": 1.25, "cardio": 1.2, "powerlifting": 1.5, "strongman": 1.6}


def mirror(value):
    """Mirror a direction, or a (direction, hint) pair, across the body's midline."""
    if isinstance(value[0], (tuple, list)):
        return tuple((-item[0], item[1], item[2]) for item in value)
    return (-value[0], value[1], value[2])


def sides(name, left, right=None):
    """Value for the .L bone and its mirror (or an explicit one) for .R."""
    if right is None:
        right = mirror(left)
    return {f"{name}.L": left, f"{name}.R": right}


def limb(name, left, right=None):
    pose = {}
    for bone, direction in sides(name, left, right).items():
        side = bone[-1]
        stem = bone[:-2]
        pose[f"DEF-{stem}.{side}"] = direction
        pose[f"DEF-{stem}.{side}.001"] = direction
    return pose


def single(name, left, right=None):
    return {f"DEF-{bone}": direction for bone, direction in sides(name, left, right).items()}


FOOT_FLAT = (0.28, -0.8, -0.52)
# Cumulative finger curl in degrees for the three segments, from the hand
# direction toward the palm.
GRIP = (62.0, 132.0, 180.0)
FIST = (78.0, 152.0, 180.0)
LOOSE = (32.0, 72.0, 104.0)
# Sprinter's knife hand: fingers long and together, barely flexed.
FLAT = (4.0, 8.0, 10.0)
# Barbell classes wrap each finger segment around the bar instead of using
# fixed angles (see wrap_fingers).
WRAP = "wrap"
PARALLETTE_RADIUS = 0.016
# Thumb-side hints. In the rest T-pose the thumb points forward (-Y).
THUMB_IN_L = (-1.0, 0.0, 0.0)
THUMB_UP = (0.0, -0.25, 1.0)


def pose_bodybuilding():
    # Most muscular, hands clasped low in front: shoulders rolled forward and
    # up, elbows flared, chin down, quads set with one foot slightly ahead.
    pose = {"DEF-spine.001": (0, -0.12, 1), "DEF-spine.002": (0, -0.28, 1), "DEF-spine.003": (0, -0.38, 1),
            "DEF-spine.004": (0, -0.6, 0.8), "DEF-spine.005": (0, -0.5, 0.87)}
    pose.update(single("shoulder", (1, -0.62, 0.16)))
    pose.update(limb("upper_arm", (0.7, -0.36, -0.62)))
    pose.update(limb("forearm", ((-0.78, -0.42, -0.5), (0.0, -0.8, 0.6))))
    pose.update(single("hand", ((-0.95, -0.2, -0.24), (0.0, -0.8, 0.6))))
    pose.update(limb("thigh", (0.24, -0.1, -1), (-0.2, -0.24, -1)))
    pose.update(limb("shin", (0.02, 0.1, -1), (-0.02, 0.16, -1)))
    pose.update(single("foot", (0.42, -0.74, -0.52), (-0.2, -0.82, -0.52)))
    return pose


def pose_calisthenics():
    # Planche-lean push-up on parallettes, after the reference photo: chest
    # low and ahead of the hands, hips higher, straight legs rising backward
    # at ~40 degrees. Elbows bend to about 90 degrees and point back along the
    # ribs; forearms stand almost vertical over the bars; fingers wrap the bars
    # from the outside. The body's front faces the floor.
    down = (0.0, 0.0, -1.0)
    torso = ((0, -0.96, -0.28), down)
    legs = (0.0, 0.77, 0.64)
    pose = {"DEF-spine": ((0, -0.97, -0.22), down), "DEF-spine.001": torso, "DEF-spine.002": torso,
            "DEF-spine.003": ((0, -0.95, -0.3), down), "DEF-spine.004": ((0, -0.9, -0.42), down),
            "DEF-spine.005": ((0, -0.8, -0.6), (0.0, -0.45, -0.9))}
    pose.update(single("shoulder", (1, 0.05, -0.12)))
    # Upper arms run back and down from the shoulders toward the hips.
    pose.update(limb("upper_arm", ((0.32, 0.9, -0.3), (0.0, -0.3, -1.0))))
    # Forearms drop almost straight down to the bars, thumbs forward.
    pose.update(limb("forearm", ((0.02, -0.04, -1.0), (0.0, -1.0, 0.0))))
    # Fingers point outward over the bar so they can wrap it; palm down.
    pose.update(single("hand", ((0.88, -0.05, -0.47), (0.0, -1.0, 0.0))))
    pose.update(limb("thigh", ((0.03, legs[1], legs[2]), down)))
    pose.update(limb("shin", ((0.02, legs[1], legs[2]), down)))
    # Pointed feet continue the leg line; instep faces the floor.
    pose.update(single("foot", ((0.0, 0.72, 0.69), (0.0, 0.2, -1.0))))
    return pose


def pose_cardio():
    pose = {"DEF-spine": (0, -0.42, 0.9), "DEF-spine.001": (0, -0.6, 0.8), "DEF-spine.002": (0, -0.66, 0.75),
            "DEF-spine.003": (0, -0.7, 0.71), "DEF-spine.004": (0, -0.62, 0.78), "DEF-spine.005": (0, -0.3, 1)}
    pose.update(single("shoulder", (1, -0.3, -0.1), (-1, 0.25, 0.0)))
    # Left arm drives back with the elbow at a right angle; right arm punches
    # forward. Thumbs stay up, so neither palm turns outward.
    pose.update(limb("upper_arm", (0.24, -0.6, -0.76), (-0.3, 0.8, -0.52)))
    pose.update(limb("forearm", ((0.1, -0.86, 0.5), THUMB_UP), ((-0.18, -0.56, -0.8), THUMB_UP)))
    pose.update(single("hand", ((0.08, -0.9, 0.44), THUMB_UP), ((-0.16, -0.6, -0.78), THUMB_UP)))
    pose.update(limb("thigh", (0.1, 0.5, -0.86), (-0.1, -0.82, -0.56)))
    pose.update(limb("shin", (0.04, 0.84, -0.54), (0.0, 0.36, -0.93)))
    # The rear foot pushes off its toes: it points down and slightly forward
    # with the instep facing forward. Pointing it backward flipped the instep
    # to the rear and read as a foot twisted around the ankle.
    pose.update(single("foot", (0.03, -0.34, -0.94), (-0.28, -0.8, -0.52)))
    return pose


def pose_powerlifting():
    # Lockout: chest up, arms long, bar held overhand in front of the hips.
    pose = {"DEF-spine.001": (0, 0.03, 1), "DEF-spine.002": (0, 0.06, 1), "DEF-spine.003": (0, 0.06, 1),
            "DEF-spine.004": (0, -0.2, 1), "DEF-spine.005": (0, -0.08, 1)}
    pose.update(single("shoulder", (1, 0.1, -0.12)))
    pose.update(limb("upper_arm", (0.2, -0.2, -1)))
    pose.update(limb("forearm", ((0.0, -0.2, -1), THUMB_IN_L)))
    pose.update(single("hand", ((0.0, -0.12, -1), THUMB_IN_L)))
    pose.update(limb("thigh", (0.5, -0.16, -1)))
    pose.update(limb("shin", (-0.08, 0.08, -1)))
    pose.update(single("foot", (0.5, -0.72, -0.48)))
    return pose


def pose_strongman():
    # Mid-pull: the bar has just left the floor, torso about 45 degrees.
    pose = {"DEF-spine": (0, -0.42, 0.9), "DEF-spine.001": (0, -0.6, 0.8), "DEF-spine.002": (0, -0.7, 0.71),
            "DEF-spine.003": (0, -0.74, 0.67), "DEF-spine.004": (0, -0.6, 0.8), "DEF-spine.005": (0, -0.25, 0.97)}
    pose.update(single("shoulder", (1, -0.3, -0.35)))
    pose.update(limb("upper_arm", (0.14, 0.0, -1)))
    pose.update(limb("forearm", ((0.02, -0.06, -1), THUMB_IN_L)))
    pose.update(single("hand", ((0.0, -0.08, -1), THUMB_IN_L)))
    pose.update(limb("thigh", (0.4, -0.5, -0.77)))
    pose.update(limb("shin", (-0.08, 0.26, -0.96)))
    pose.update(single("foot", (0.4, -0.75, -0.52)))
    return pose


# Camera direction is from the subject toward the camera.
CLASSES = {
    "bodybuilding": {"pose": pose_bodybuilding, "camera": (-0.22, -1.0, 0.05), "prop": None, "curl": FIST,
                     "backdrop": "olympia"},
    # Low front camera: the body faces the floor, so a camera above it showed
    # the face at one end and calves and soles at the other, which read as a
    # lower body turned 180 degrees. From below, face, chest, shins and insteps
    # all face the viewer while the legs still rise diagonally.
    "calisthenics": {"pose": pose_calisthenics, "camera": (1.0, -0.38, -0.1), "prop": "parallettes",
                     "bare_torso": True, "curl": WRAP, "bar_radius": PARALLETTE_RADIUS, "backdrop": "rings"},
    "cardio": {"pose": pose_cardio, "camera": (-0.85, -0.62, 0.05), "prop": None, "curl": FLAT,
               "backdrop": "bolt"},
    "powerlifting": {"pose": pose_powerlifting, "camera": (-0.3, -1.0, 0.04), "prop": "barbell", "curl": WRAP,
                     "backdrop": "judges"},
    "strongman": {"pose": pose_strongman, "camera": (-0.42, -0.88, 0.2), "prop": "floor_barbell", "curl": WRAP,
                  "backdrop": "boulder"},
}

REST = {}


def capture_rest(rig):
    """Rest orientation of every bone in world space, the reference for twist."""
    bpy.context.view_layer.update()
    REST.clear()
    for pose_bone in rig.pose.bones:
        REST[pose_bone.name] = (rig.matrix_world @ pose_bone.matrix).to_3x3().copy()


def carried(rig, bone_name, rest_vector):
    """Where a rest-space vector attached to the bone points now."""
    current = (rig.matrix_world @ rig.pose.bones[bone_name].matrix).to_3x3()
    return (current @ REST[bone_name].inverted() @ Vector(rest_vector)).normalized()


def aim(rig, bone_name, value):
    """Point a bone along a direction, then fix its twist.

    A bare direction keeps the bone's rest "front" facing front (knees and toes
    forward, thumbs forward). A (direction, hint) pair turns that front side
    toward the hint instead, which is how grips and thumb-up fists are posed.
    """
    pose_bone = rig.pose.bones.get(bone_name)
    if pose_bone is None:
        raise RuntimeError("Pose bone missing: " + bone_name)
    if isinstance(value[0], (tuple, list)):
        direction, hint = Vector(value[0]).normalized(), Vector(value[1]).normalized()
    else:
        direction, hint = Vector(value).normalized(), None

    bpy.context.view_layer.update()
    world = rig.matrix_world @ pose_bone.matrix
    current = (world.to_3x3() @ Vector((0.0, 1.0, 0.0))).normalized()
    swing = current.rotation_difference(direction).to_matrix().to_4x4()
    origin = world.translation.copy()
    world = Matrix.Translation(origin) @ swing @ Matrix.Translation(-origin) @ world

    rest_axis = (REST[bone_name] @ Vector((0.0, 1.0, 0.0))).normalized()
    reference = Vector((0.0, -1.0, 0.0))
    if abs(rest_axis.dot(reference)) > 0.7:
        # Bones that already point forward (feet) are steadied by their top side.
        reference = Vector((0.0, 0.0, 1.0))
    target = hint if hint is not None else reference
    facing = (world.to_3x3() @ REST[bone_name].inverted() @ reference)
    facing = (facing - direction * facing.dot(direction))
    target = (target - direction * target.dot(direction))
    if facing.length > 1e-4 and target.length > 0.15:
        facing.normalize()
        target.normalize()
        angle = math.atan2(direction.dot(facing.cross(target)), facing.dot(target))
        twist = Matrix.Rotation(angle, 4, direction)
        world = Matrix.Translation(origin) @ twist @ Matrix.Translation(-origin) @ world
    pose_bone.matrix = rig.matrix_world.inverted() @ world
    bpy.context.view_layer.update()


def depth(bone):
    count = 0
    while bone.parent is not None:
        bone = bone.parent
        count += 1
    return count


def apply_pose(rig, pose):
    ordered = sorted(pose, key=lambda name: depth(rig.data.bones[name]))
    for name in ordered:
        aim(rig, name, pose[name])


def hand_frame(rig, side):
    """Wrist position plus finger, thumb-side and palm-normal directions."""
    name = f"DEF-hand.{side}"
    bpy.context.view_layer.update()
    world = rig.matrix_world @ rig.pose.bones[name].matrix
    fingers = (world.to_3x3() @ Vector((0.0, 1.0, 0.0))).normalized()
    thumb = carried(rig, name, (0.0, -1.0, 0.0))
    palm = carried(rig, name, (0.0, 0.0, -1.0))
    return world.translation.copy(), fingers, thumb, palm


def curl_fingers(rig, side, angles, radius=None):
    """Wrap the four fingers toward the palm; the thumb closes over them."""
    if angles == WRAP:
        wrap_fingers(rig, side, radius if radius is not None else BAR_RADIUS)
        return
    _, fingers, thumb, palm = hand_frame(rig, side)
    for finger in ("f_index", "f_middle", "f_ring", "f_pinky"):
        for segment, degrees in enumerate(angles, start=1):
            radians = math.radians(degrees)
            direction = fingers * math.cos(radians) + palm * math.sin(radians)
            aim(rig, f"DEF-{finger}.0{segment}.{side}", (tuple(direction), tuple(thumb)))
    if angles is not GRIP:
        # Only a grip wraps the thumb; fists keep it resting along the fingers.
        return
    strength = angles[0] / 78.0
    for segment, blend in ((1, 0.35), (2, 0.7), (3, 1.0)):
        direction = fingers * (0.75 - 0.35 * blend) + thumb * (0.55 - 0.45 * blend * strength) + \
            palm * (0.35 + 0.75 * blend * strength)
        aim(rig, f"DEF-thumb.0{segment}.{side}", tuple(direction.normalized()))


def grip_point(rig, side, radius):
    """Centre of a bar of the given radius lying across the closed palm."""
    wrist, fingers, _, palm = hand_frame(rig, side)
    return wrist + fingers * 0.082 + palm * (radius + 0.006)


# A real grip holds the bar in the finger roots on the palm side: just past
# the middle knuckle, one palm-half-thickness plus the bar radius off the bones.
GRIP_ALONG = 0.012
PALM_DEPTH = 0.014
FINGER_HALF = 0.0085
# Largest bend each finger joint may take relative to its parent segment.
JOINT_LIMITS = (95.0, 110.0, 90.0)


def grip_centre(rig, side, radius):
    _, fingers, _, palm = hand_frame(rig, side)
    knuckle = rig.matrix_world @ rig.pose.bones[f"DEF-f_middle.01.{side}"].head
    return knuckle + fingers * GRIP_ALONG + palm * (PALM_DEPTH + radius)


def wrap_fingers(rig, side, radius):
    """Wrap every finger segment around the bar like a string.

    Each segment turns toward the palm as far as it can without its bone line
    entering the bar (bar radius plus half a finger thickness), so the fingers
    run down the front of the bar, under it and up its back instead of closing
    into a fist above it with the bar under the knuckles.
    """
    centre = grip_centre(rig, side, radius)
    _, fingers, thumb, palm = hand_frame(rig, side)
    clearance = radius + FINGER_HALF
    for finger in ("f_index", "f_middle", "f_ring", "f_pinky"):
        previous = 0.0
        for segment, limit in enumerate(JOINT_LIMITS, start=1):
            name = f"DEF-{finger}.0{segment}.{side}"
            bpy.context.view_layer.update()
            bone = rig.pose.bones[name]
            head = rig.matrix_world @ bone.head
            length = ((rig.matrix_world @ bone.tail) - head).length
            offset = head - centre
            start = np.array([offset.dot(fingers), offset.dot(palm)])
            chosen = previous
            for step in range(0, int(limit) + 1):
                angle = math.radians(previous + step)
                direction = np.array([math.cos(angle), math.sin(angle)])
                along = min(max(-start.dot(direction), 0.0), length)
                if np.linalg.norm(start + direction * along) < clearance:
                    break
                chosen = previous + step
            previous = chosen
            radians = math.radians(chosen)
            aim(rig, name, (tuple(fingers * math.cos(radians) + palm * math.sin(radians)), tuple(thumb)))
    # The thumb closes around the bar from the other side, over the fingers.
    for segment, blend in ((1, 0.35), (2, 0.7), (3, 1.0)):
        direction = fingers * (0.75 - 0.35 * blend) + thumb * (0.55 - 0.45 * blend) + palm * (0.35 + 0.75 * blend)
        aim(rig, f"DEF-thumb.0{segment}.{side}", tuple(direction.normalized()))


def trunk_mask(mesh_object):
    """Vertices of torso, hips and legs: what a held bar must stay in front of."""
    names = {group.index: group.name for group in mesh_object.vertex_groups}
    wanted = ("DEF-spine", "DEF-pelvis", "DEF-thigh", "DEF-shin", "DEF-breast")
    mask = np.zeros(len(mesh_object.data.vertices), dtype=bool)
    for vertex in mesh_object.data.vertices:
        weight = sum(item.weight for item in vertex.groups
                     if names[item.group].startswith(wanted) and
                     names[item.group] not in ("DEF-spine.004", "DEF-spine.005"))
        mask[vertex.index] = weight > 0.5
    return mask


def evaluated_points(obj):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    coords = np.empty(len(mesh.vertices) * 3)
    mesh.vertices.foreach_get("co", coords)
    matrix = np.array(evaluated.matrix_world)
    world = coords.reshape(-1, 3) @ matrix[:3, :3].T + matrix[:3, 3]
    evaluated.to_mesh_clear()
    return world


def hold_bar_clear_of_body(rig, body, pose, radius, curl=None):
    """Swing the straight arms forward until the gripped bar clears the body.

    The bar stays in the hands; the arms move, not the bar, so the grip is
    always real. Returns the two grip points.
    """
    mask = trunk_mask(body)
    for step in range(16):
        grip = grip_centre if curl == WRAP else grip_point
        left = grip(rig, "L", radius)
        right = grip(rig, "R", radius)
        points = evaluated_points(body)[mask]
        low_x, high_x = min(left.x, right.x), max(left.x, right.x)
        height = (left.z + right.z) * 0.5
        depth_at = (left.y + right.y) * 0.5
        band = (np.abs(points[:, 2] - height) < radius + 0.012) & \
            (points[:, 0] > low_x - 0.02) & (points[:, 0] < high_x + 0.02)
        if not band.any() or points[band, 1].min() - radius - 0.006 >= depth_at:
            return left, right
        for name in list(pose):
            if name.startswith(("DEF-upper_arm", "DEF-forearm", "DEF-hand")):
                value = pose[name]
                paired = isinstance(value[0], (tuple, list))
                direction = Vector(value[0] if paired else value)
                direction.y -= 0.035
                pose[name] = (tuple(direction), value[1]) if paired else tuple(direction)
        apply_pose(rig, {name: value for name, value in pose.items()
                         if name.startswith(("DEF-upper_arm", "DEF-forearm", "DEF-hand"))})
        for side in ("L", "R"):
            curl_fingers(rig, side, curl or GRIP, radius)
    raise RuntimeError("Could not swing the arms far enough for the bar to clear the body")

def paint_regions(mesh_object):
    """Vertex colours: R torso, G hips and thighs, B head. Used as an id pass."""
    mesh = mesh_object.data
    attribute = mesh.color_attributes.get("region") or mesh.color_attributes.new("region", "FLOAT_COLOR", "POINT")
    names = {group.index: group.name for group in mesh_object.vertex_groups}
    torso = ("DEF-spine.001", "DEF-spine.002", "DEF-spine.003", "DEF-breast", "DEF-shoulder")
    hips = ("DEF-spine", "DEF-pelvis", "DEF-thigh")
    head = ("DEF-spine.004", "DEF-spine.005")
    colors = np.zeros((len(mesh.vertices), 4), dtype=np.float32)
    colors[:, 3] = 1.0
    for vertex in mesh.vertices:
        r = g = b = 0.0
        for item in vertex.groups:
            name = names[item.group]
            if name in head:
                b += item.weight
            elif name == "DEF-spine" or name.startswith(("DEF-pelvis", "DEF-thigh")):
                g += item.weight
            elif name.startswith(torso):
                r += item.weight
        colors[vertex.index, :3] = (min(r, 1.0), min(g, 1.0), min(b, 1.0))
    attribute.data.foreach_set("color", colors.reshape(-1))
    mesh.color_attributes.active_color = attribute
    mesh.color_attributes.render_color_index = list(mesh.color_attributes).index(attribute)


def make_prop(name, mesh_builder):
    mesh_builder()
    obj = bpy.context.active_object
    obj.name = name
    attribute = obj.data.color_attributes.new("region", "FLOAT_COLOR", "POINT")
    colors = np.ones((len(obj.data.vertices), 4), dtype=np.float32)
    attribute.data.foreach_set("color", colors.reshape(-1))
    obj.data.color_attributes.active_color = attribute
    return obj


def cylinder(start, end, radius, name):
    start, end = Vector(start), Vector(end)
    axis = end - start
    obj = make_prop(name, lambda: bpy.ops.mesh.primitive_cylinder_add(
        vertices=40, radius=radius, depth=axis.length, location=(start + end) * 0.5))
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = Vector((0.0, 0.0, 1.0)).rotation_difference(axis.normalized())
    return obj


def hand_points(rig):
    bpy.context.view_layer.update()
    return [(rig.matrix_world @ rig.pose.bones[f"DEF-hand.{side}"].matrix) @ Vector((0.0, 0.025, 0.0))
            for side in ("L", "R")]


BAR_RADIUS = 0.012


def build_props(kind, rig, floor, grips=None):
    props = []
    if kind in ("barbell", "floor_barbell"):
        # The bar runs exactly through both closed palms.
        left, right = grips
        centre = (left + right) * 0.5
        axis = (left - right).normalized()
        floor_pull = kind == "floor_barbell"
        half = 0.42 if floor_pull else 0.47
        first_plate = 0.30 if floor_pull else 0.32
        plate_radius = min(centre.z - floor, 0.22) if floor_pull else 0.17
        props.append(cylinder(centre - axis * half, centre + axis * half, BAR_RADIUS, "Bar"))
        count = 3 if kind == "barbell" else 4
        for side in (-1, 1):
            for index in range(count):
                start = centre + axis * side * (first_plate + index * 0.028)
                props.append(cylinder(start, start + axis * side * 0.022, plate_radius, "Plate"))
            props.append(cylinder(centre + axis * side * (first_plate - 0.02),
                                  centre + axis * side * (first_plate - 0.002), 0.03, "Collar"))
    elif kind == "parallettes":
        # One bar per hand running front to back through the closed grip, on
        # two legs with flat feet, like the reference parallettes.
        for grip in grips:
            props.append(cylinder((grip.x, grip.y - 0.19, grip.z), (grip.x, grip.y + 0.19, grip.z),
                                  PARALLETTE_RADIUS, "Rail"))
            for offset in (-0.16, 0.16):
                props.append(cylinder((grip.x, grip.y + offset, grip.z), (grip.x, grip.y + offset, floor + 0.015),
                                      0.016, "Leg"))
                props.append(cylinder((grip.x - 0.09, grip.y + offset, floor + 0.015),
                                      (grip.x + 0.09, grip.y + offset, floor + 0.015), 0.016, "Foot"))
    return props


def evaluated_bounds(objects):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    low = np.array([1e9, 1e9, 1e9])
    high = -low
    for obj in objects:
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        coords = np.empty(len(mesh.vertices) * 3)
        mesh.vertices.foreach_get("co", coords)
        matrix = np.array(evaluated.matrix_world)
        world = coords.reshape(-1, 3) @ matrix[:3, :3].T + matrix[:3, 3]
        low = np.minimum(low, world.min(axis=0))
        high = np.maximum(high, world.max(axis=0))
        evaluated.to_mesh_clear()
    return low, high


def setup_render(scene):
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.render.resolution_x = WIDTH
    scene.render.resolution_y = HEIGHT
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.image_settings.color_depth = "8"
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.display.render_aa = "8"
    scene.display.shading.show_object_outline = False
    scene.display.shading.show_shadows = False


def render_pass(scene, kind, path):
    shading = scene.display.shading
    if kind == "albedo":
        shading.light = "FLAT"
        shading.color_type = "TEXTURE"
        shading.show_cavity = False
    elif kind == "regions":
        shading.light = "FLAT"
        shading.color_type = "VERTEX"
        shading.show_cavity = False
    else:
        shading.light = "STUDIO"
        shading.studio_light = "Default"
        shading.color_type = "SINGLE"
        shading.single_color = (1.0, 1.0, 1.0)
        shading.show_specular_highlight = False
        shading.show_cavity = True
        shading.cavity_type = "BOTH"
        shading.cavity_ridge_factor = 0.0
        shading.cavity_valley_factor = 2.5
        shading.curvature_ridge_factor = 0.0
        shading.curvature_valley_factor = 2.0
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    image = bpy.data.images.load(str(path))
    pixels = np.empty(WIDTH * HEIGHT * 4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    bpy.data.images.remove(image)
    # Blender stores rows bottom-up; flip to image space.
    return pixels.reshape(HEIGHT, WIDTH, 4)[::-1].copy()


def dilate(mask, radius):
    """Disc-ish dilation from separable box passes (no SciPy in Blender)."""
    result = mask.copy()
    for axis in (0, 1):
        grown = result.copy()
        for shift in range(1, radius + 1):
            grown |= np.roll(result, shift, axis=axis) | np.roll(result, -shift, axis=axis)
        result = grown
    return result


def halftone(shape, period, fill):
    ys, xs = np.mgrid[0:shape[0], 0:shape[1]]
    # 45 degree screen, like the boot art's dumbbell shading.
    u = (xs + ys) / period
    v = (xs - ys) / period
    distance = np.hypot(u - np.round(u), v - np.round(v))
    return distance < fill


def polygon_mask(shape, polygon):
    """Even-odd fill of a polygon given in (x, y) canvas fractions."""
    height, width = shape
    polygon = np.asarray(polygon, dtype=float) * np.array([width, height])
    ys, xs = np.mgrid[0:height, 0:width]
    inside = np.zeros(shape, dtype=bool)
    count = len(polygon)
    for i in range(count):
        x1, y1 = polygon[i]
        x2, y2 = polygon[(i + 1) % count]
        crosses = ((y1 > ys) != (y2 > ys)) & (xs < (x2 - x1) * (ys - y1) / (y2 - y1 + 1e-9) + x1)
        inside ^= crosses
    return inside


def segment_distance(shape, centre, direction, half):
    """Distance, in canvas widths, from every pixel to a segment.

    Centre is in canvas widths as (x, y) with y down, so circles stay round on
    the 4:5 canvas.
    """
    height, width = shape
    ys, xs = np.mgrid[0:height, 0:width]
    px = xs / width - centre[0]
    py = ys / width - centre[1]
    dx, dy = direction
    along = np.clip(px * dx + py * dy, -half, half)
    return np.hypot(px - along * dx, py - along * dy), px * dx + py * dy


def bolt_mask(shape):
    """Flash arrow behind the sprinter: a lightning bolt pointing forward-down."""
    return polygon_mask(shape, [[0.93, 0.10], [0.50, 0.40], [0.66, 0.44], [0.30, 0.74], [0.44, 0.76],
                                [0.06, 0.97], [0.34, 0.62], [0.20, 0.60], [0.50, 0.30], [0.36, 0.28]])


def rings_mask(shape):
    """Gymnastic rings on straps behind the calisthenics athlete."""
    mask = np.zeros(shape, dtype=bool)
    aspect = shape[0] / shape[1]
    ring_y = 0.36 * aspect
    for x in (0.27, 0.73):
        distance, _ = segment_distance(shape, (x, ring_y), (1.0, 0.0), 0.0)
        mask |= (distance < 0.17) & (distance > 0.115)
        strap, _ = segment_distance(shape, (x, (0.03 * aspect + ring_y - 0.17) * 0.5), (0.0, 1.0),
                                    (ring_y - 0.17 - 0.03 * aspect) * 0.5)
        mask |= strap < 0.022
    return mask


def ellipse_distance(shape, centre, radius_x, radius_y):
    """Normalised elliptical radius (1 on the ellipse) per pixel; units are canvas widths."""
    height, width = shape
    ys, xs = np.mgrid[0:height, 0:width]
    return np.hypot((xs / width - centre[0]) / radius_x, (ys / width - centre[1]) / radius_y)


def olympia_layers(shape):
    """Bodybuilding: the tall "O" of the Olympia stage logo, ring only - a red
    oval band edged in paper, as on the competition banner."""
    aspect = shape[0] / shape[1]
    centre = (0.5, 0.47 * aspect)
    outer = ellipse_distance(shape, centre, 0.38, 0.48)
    band = (outer < 1.0) & (outer > 0.74)
    edge = ((outer < 1.07) & (outer >= 1.0)) | ((outer <= 0.74) & (outer > 0.68))
    return [(edge, "paper"), (band, "accent")]


def judges_lights_layers(shape):
    """Powerlifting: the three referee lights after a lift - one white, two red."""
    aspect = shape[0] / shape[1]
    layers = []
    for x, tone in ((0.19, "paper"), (0.5, "accent"), (0.81, "accent")):
        disc = ellipse_distance(shape, (x, 0.13 * aspect), 0.135, 0.135) < 1.0
        layers.append((disc, tone))
    return layers


def boulder_layers(shape):
    """Strongman: an atlas stone - a big rough-edged sphere, shaded on the
    lower right."""
    aspect = shape[0] / shape[1]
    height, width = shape
    centre = (0.5, 0.40 * aspect)
    ys, xs = np.mgrid[0:height, 0:width]
    angle = np.arctan2(ys / width - centre[1], xs / width - centre[0])
    # Irregular outline: a few low-frequency bumps, deterministic.
    wobble = 1.0 + 0.012 * np.sin(angle * 5 + 0.7) + 0.008 * np.sin(angle * 9 + 2.1) + 0.005 * np.sin(angle * 14)
    radius = ellipse_distance(shape, centre, 0.40, 0.40) / wobble
    stone = radius < 1.0
    shade = stone & (((xs / width - centre[0]) * 0.7 + (ys / width - centre[1]) * 0.7) > 0.12)
    return [(stone, "accent"), (shade, "shade")]


BACKDROPS = {"bolt": bolt_mask, "olympia": olympia_layers, "rings": rings_mask,
             "judges": judges_lights_layers, "boulder": boulder_layers}


def composite(albedo, shade, regions, bare_torso, backdrop=None):
    alpha = albedo[..., 3] > 0.5
    rgb = albedo[..., :3]
    luminance = rgb @ np.array([0.299, 0.587, 0.114])
    region = regions[..., :3]
    prop = alpha & (region.min(axis=2) > 0.85)
    body = alpha & ~prop
    head_region = body & (region[..., 2] > 0.5)
    yellow = head_region & (rgb[..., 0] > 0.45) & (rgb[..., 2] < rgb[..., 0] * 0.6)
    # The neck shares the head bones; only the yellow ball and its drawn face are "head".
    face = np.zeros_like(yellow)
    if yellow.any():
        ys, xs = np.nonzero(yellow)
        centre_y, centre_x = ys.mean(), xs.mean()
        radius = np.percentile(np.hypot(ys - centre_y, xs - centre_x), 97)
        grid_y, grid_x = np.mgrid[0:yellow.shape[0], 0:yellow.shape[1]]
        inside_ball = np.hypot(grid_y - centre_y, grid_x - centre_x) < radius * 0.97
        face = body & inside_ball & ~yellow & (luminance < 0.3)
    head = yellow | face
    dark = luminance < 0.13
    top = body & dark & (region[..., 0] > 0.35) & ~head
    shorts = body & dark & ~top & ~head & (region[..., 1] > 0.2)
    if bare_torso:
        top[:] = False

    base = np.zeros(albedo.shape[:2] + (3,))
    base[body] = PAPER
    base[top] = NAVY
    base[shorts] = ACCENT
    base[head] = GOLD
    base[prop] = STEEL

    light = shade[..., :3].mean(axis=2)
    reference = np.percentile(light[alpha], 92) if alpha.any() else 1.0
    light = light / max(reference, 1e-3)
    shadow = alpha & (light < 0.56)
    mid = alpha & (light < 0.8) & ~shadow
    dots = halftone(alpha.shape, 17.0, 0.34)

    color = base.copy()
    skin = body & ~top & ~shorts & ~head
    color[skin & shadow] = INK
    color[skin & mid & dots] = INK
    for mask, tone in ((top, NAVY), (shorts, ACCENT), (head, GOLD), (prop, STEEL)):
        color[mask & shadow] = tone * 0.34 + INK * 0.66
        color[mask & mid & dots] = tone * 0.34 + INK * 0.66
    color[face] = INK

    # Ink lines where regions meet and where the surface folds sharply.
    classes = (top * 1 + shorts * 2 + head * 3 + prop * 4 + skin * 5).astype(np.int16)
    edges = np.zeros(alpha.shape, dtype=bool)
    edges[:, 1:] |= classes[:, 1:] != classes[:, :-1]
    edges[1:, :] |= classes[1:, :] != classes[:-1, :]
    lines = dilate(edges & alpha, 3) | (alpha & (light < 0.3))
    color[lines & alpha] = INK

    outline = dilate(alpha, 9) & ~alpha
    rim = dilate(alpha, 24) & ~alpha & ~outline
    out = np.zeros(albedo.shape[:2] + (4,), dtype=np.float32)
    if backdrop:
        # Same treatment for every class sign: red fill, darker halftone,
        # ink outline, drawn behind the figure's paper rim.
        layers = BACKDROPS[backdrop](alpha.shape)
        if isinstance(layers, np.ndarray):
            layers = [(layers, "accent")]
        dots = halftone(alpha.shape, 17.0, 0.28)
        for mask, tone in layers:
            if tone == "shade":
                # Darker halftone over an already drawn layer (sphere shading).
                out[mask & dots, :3] = ACCENT * 0.45
                continue
            if tone == "paper_flat":
                out[mask, :3] = PAPER
                continue
            mask_outline = dilate(mask, 8) & ~mask
            if tone == "paper":
                out[mask, :3] = PAPER
                out[mask & dots, :3] = PAPER * 0.82
            else:
                out[mask, :3] = ACCENT
                out[mask & dots, :3] = ACCENT * 0.62
            out[mask, 3] = 1.0
            out[mask_outline, :3] = INK
            out[mask_outline, 3] = 1.0
    out[rim, :3] = PAPER
    out[rim, 3] = 1.0
    out[outline, :3] = INK
    out[outline, 3] = 1.0
    out[alpha, :3] = color[alpha]
    out[alpha, 3] = 1.0
    return out


def save_png(pixels, path):
    height, width = pixels.shape[:2]
    image = bpy.data.images.new("class_art", width=width, height=height, alpha=True)
    image.alpha_mode = "STRAIGHT"
    image.pixels.foreach_set(pixels[::-1].reshape(-1).astype(np.float32))
    image.filepath_raw = str(path)
    image.file_format = "PNG"
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save()
    bpy.data.images.remove(image)


def downsample(pixels):
    # Premultiplied 2x box filter keeps the rim free of dark fringes.
    premultiplied = pixels.copy()
    premultiplied[..., :3] *= premultiplied[..., 3:4]
    small = premultiplied.reshape(HEIGHT // 2, 2, WIDTH // 2, 2, 4).mean(axis=(1, 3))
    alpha = np.maximum(small[..., 3:4], 1e-6)
    small[..., :3] = np.where(small[..., 3:4] > 0, small[..., :3] / alpha, 0.0)
    return small


def render_class(class_id, catalog_entry, scratch):
    config = CLASSES[class_id]
    bpy.ops.wm.open_mainfile(filepath=str(build.CLASSES_BLEND))
    scene = bpy.context.scene
    rig = authored.find_armature()
    mesh_object = authored.find_mesh(rig)

    keys = mesh_object.data.shape_keys
    count = len(mesh_object.data.vertices)
    basis = np.empty(count * 3, dtype=np.float32)
    keys.key_blocks["Basis"].data.foreach_get("co", basis)
    coords = basis.copy()
    appearance = catalog_entry.get("appearance")
    if appearance:
        shaped = np.empty(count * 3, dtype=np.float32)
        keys.key_blocks[appearance].data.foreach_get("co", shaped)
        coords = basis + (shaped - basis) * EXAGGERATION[class_id]
    mesh_object.shape_key_clear()
    mesh_object.data.vertices.foreach_set("co", coords)
    mesh_object.data.update()

    authored.keep_only_target_objects(rig, mesh_object)
    pose_rig, body = authored.build_clean_export_rig(rig, mesh_object)
    for polygon in body.data.polygons:
        polygon.use_smooth = True
    paint_regions(body)
    capture_rest(pose_rig)
    pose = config["pose"]()
    apply_pose(pose_rig, pose)
    for side in ("L", "R"):
        curl_fingers(pose_rig, side, config["curl"], config.get("bar_radius"))

    low, high = evaluated_bounds([body])
    props = []
    if config["prop"] == "parallettes":
        grips = [grip_centre(pose_rig, side, PARALLETTE_RADIUS) for side in ("L", "R")]
        floor = min(grip.z for grip in grips) - 0.2
        props = build_props("parallettes", pose_rig, floor, grips)
    elif config["prop"] in ("barbell", "floor_barbell"):
        grips = hold_bar_clear_of_body(pose_rig, body, pose, BAR_RADIUS, config["curl"])
        low, high = evaluated_bounds([body])
        props = build_props(config["prop"], pose_rig, low[2], grips)
    low, high = evaluated_bounds([body] + props)

    direction = Vector(config["camera"]).normalized()
    centre = Vector(((low[0] + high[0]) * 0.5, (low[1] + high[1]) * 0.5, (low[2] + high[2]) * 0.5))
    camera_data = bpy.data.cameras.new("ArtCamera")
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = CANVAS_HEIGHT
    camera_data.sensor_fit = "VERTICAL"
    camera = bpy.data.objects.new("ArtCamera", camera_data)
    scene.collection.objects.link(camera)
    camera.location = centre + direction * 6.0
    camera.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
    scene.camera = camera
    pose_rig.hide_render = True

    setup_render(scene)
    albedo = render_pass(scene, "albedo", scratch / f"{class_id}_albedo.png")
    regions = render_pass(scene, "regions", scratch / f"{class_id}_regions.png")
    shade = render_pass(scene, "shade", scratch / f"{class_id}_shade.png")
    result = composite(albedo, shade, regions, config.get("bare_torso", False), config.get("backdrop"))

    covered = result[..., 3] > 0
    rows = np.where(covered.any(axis=1))[0]
    columns = np.where(covered.any(axis=0))[0]
    margin = min(rows[0], HEIGHT - 1 - rows[-1], columns[0], WIDTH - 1 - columns[-1])
    save_png(result, SOURCE_DIR / f"{class_id}.png")
    save_png(downsample(result), RUNTIME_DIR / f"{class_id}.png")
    print(f"GYMCHAOS_CLASS_ART_OK class={class_id} coverage={covered.mean():.3f} marginPx={int(margin)} "
          f"extent={columns[-1] - columns[0] + 1}x{rows[-1] - rows[0] + 1}")
    if margin < 12:
        raise RuntimeError(f"{class_id}: illustration touches the canvas edge (margin {margin}px)")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    catalog = {item["id"]: item for item in json.loads(build.CATALOG.read_text(encoding="utf-8"))["classes"]}
    selected = argv or list(CLASSES)
    scratch = Path(tempfile.mkdtemp(prefix="gymchaos_class_art_"))
    for class_id in selected:
        render_class(class_id, catalog[class_id], scratch)
    print(f"GYMCHAOS_CLASS_ART_BATCH_OK classes={len(selected)}")


if __name__ == "__main__":
    try:
        main()
    except Exception as exception:  # noqa: BLE001 - a crash must fail the run
        import traceback
        traceback.print_exc()
        print(f"GYMCHAOS_CLASS_ART_FAILED {exception}")
        sys.exit(1)
