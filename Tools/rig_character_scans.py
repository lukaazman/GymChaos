"""Bake model-specific Mixamo-compatible rigs into the character scan FBXs.

The scans are authored in different poses, so a shared T-pose autorigger tears
their geometry.  This tool places the skeleton directly through each authored
pose and writes the skin weights into the exported FBX.  Unity therefore only
loads a normal skinned model; it does not construct bones or classify body
parts at runtime.
"""

from __future__ import annotations

from dataclasses import dataclass
import heapq
from pathlib import Path
import sys

import bpy
from mathutils import Vector


REPOSITORY_ROOT = Path("D:/GitHub/GymChaos")
SOURCE_ROOT = REPOSITORY_ROOT / "GymChaos/Assets/StreamingAssets/BodyBuilders"
OUTPUT_ROOT = REPOSITORY_ROOT / ".tools/character-rigs"


@dataclass(frozen=True)
class RigProfile:
    left_shoulder: tuple[float, float]
    left_elbow: tuple[float, float]
    left_hand: tuple[float, float]
    right_shoulder: tuple[float, float]
    right_elbow: tuple[float, float]
    right_hand: tuple[float, float]
    left_hip: tuple[float, float]
    left_knee: tuple[float, float]
    left_foot: tuple[float, float]
    right_hip: tuple[float, float]
    right_knee: tuple[float, float]
    right_foot: tuple[float, float]
    neck_y: float
    head_half_width: float
    arm_radius: float
    eye_y: float
    left_arm_depth: float = 0.0
    right_arm_depth: float = 0.0
    left_hip_depth: float = 0.0
    left_knee_depth: float = 0.0
    left_foot_depth: float = 0.0
    right_hip_depth: float = 0.0
    right_knee_depth: float = 0.0
    right_foot_depth: float = 0.0


PROFILES = {
    "arnold": RigProfile(
        (-0.13, 0.72), (-0.27, 0.82), (-0.20, 0.91),
        (0.13, 0.72), (0.27, 0.82), (0.20, 0.91),
        (-0.055, 0.49), (-0.055, 0.25), (-0.055, 0.02),
        (0.055, 0.49), (0.055, 0.25), (0.055, 0.02),
        0.79, 0.09, 0.095, 0.88,
    ),
    "cbum": RigProfile(
        (-0.14, 0.70), (-0.23, 0.62), (-0.12, 0.52),
        (0.14, 0.70), (0.23, 0.62), (0.12, 0.52),
        (-0.065, 0.49), (-0.065, 0.25), (-0.075, 0.02),
        (0.065, 0.49), (0.065, 0.25), (0.075, 0.02),
        0.79, 0.085, 0.095, 0.88,
    ),
    "ronnie": RigProfile(
        (-0.13, 0.70), (-0.18, 0.57), (-0.14, 0.43),
        (0.13, 0.70), (0.18, 0.57), (0.14, 0.43),
        (-0.055, 0.49), (-0.055, 0.25), (-0.06, 0.02),
        (0.055, 0.49), (0.055, 0.25), (0.06, 0.02),
        0.79, 0.09, 0.095, 0.88,
    ),
    "manwithsuit1": RigProfile(
        (-0.105, 0.70), (-0.12, 0.57), (-0.09, 0.65),
        (0.105, 0.70), (0.12, 0.57), (0.09, 0.65),
        (-0.052, 0.49), (-0.052, 0.25), (-0.055, 0.02),
        (0.052, 0.49), (0.052, 0.25), (0.055, 0.02),
        0.79, 0.08, 0.085, 0.88, 0.12, 0.12,
    ),
    "jay": RigProfile(
        (-0.11, 0.70), (-0.14, 0.56), (-0.12, 0.42),
        (0.11, 0.70), (0.14, 0.56), (0.12, 0.42),
        (-0.05, 0.49), (-0.05, 0.25), (-0.04, 0.02),
        (0.05, 0.49), (0.05, 0.25), (0.04, 0.02),
        0.79, 0.085, 0.095, 0.88,
    ),
    "goku": RigProfile(
        (-0.11, 0.70), (-0.15, 0.56), (-0.12, 0.42),
        (0.11, 0.70), (0.15, 0.56), (0.12, 0.42),
        (-0.05, 0.49), (-0.05, 0.25), (-0.04, 0.02),
        (0.05, 0.49), (0.05, 0.25), (0.04, 0.02),
        0.79, 0.085, 0.095, 0.78,
    ),
    "zyzz": RigProfile(
        (-0.10, 0.70), (-0.14, 0.55), (-0.13, 0.40),
        (0.10, 0.70), (0.13, 0.58), (0.07, 0.43),
        (-0.045, 0.49), (-0.04, 0.25), (0.00, 0.02),
        (0.045, 0.49), (0.06, 0.25), (0.075, 0.02),
        0.79, 0.08, 0.10, 0.87,
        -0.085, 0.08, 0.0, -0.025, -0.08, 0.0, 0.08, 0.15,
    ),
}


def clear_scene() -> None:
    bpy.ops.object.mode_set(mode="OBJECT") if bpy.context.object and bpy.context.object.mode != "OBJECT" else None
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for collection in (bpy.data.armatures, bpy.data.meshes, bpy.data.materials, bpy.data.images):
        for block in list(collection):
            if block.users == 0:
                collection.remove(block)


def import_and_join(source: Path):
    bpy.ops.import_scene.gltf(filepath=str(source))
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if not meshes:
        raise RuntimeError(f"No mesh found in {source}")
    bpy.ops.object.select_all(action="DESELECT")
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = max(meshes, key=lambda item: len(item.data.vertices))
    if len(meshes) > 1:
        bpy.ops.object.join()
    mesh = bpy.context.view_layer.objects.active
    mesh.name = f"{source.stem}_Body"
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return mesh


def bounds_for(mesh):
    points = [mesh.matrix_world @ vertex.co for vertex in mesh.data.vertices]
    minimum = Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points)))
    maximum = Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points)))
    return minimum, maximum, (minimum + maximum) * 0.5, maximum.z - minimum.z


def profile_point(center: Vector, minimum: Vector, height: float, xy, depth=0.0):
    return Vector((center.x + xy[0] * height, center.y + depth * height, minimum.z + xy[1] * height))


def create_armature(name: str, profile: RigProfile, minimum: Vector, center: Vector, height: float):
    data = bpy.data.armatures.new(f"{name}_Armature")
    armature = bpy.data.objects.new(f"{name}_Rig", data)
    bpy.context.collection.objects.link(armature)
    bpy.context.view_layer.objects.active = armature
    armature.select_set(True)
    armature.show_in_front = True
    bpy.ops.object.mode_set(mode="EDIT")

    hips_center = Vector((center.x, center.y, minimum.z + height * 0.49))
    spine0 = Vector((center.x, center.y, minimum.z + height * 0.57))
    spine1 = Vector((center.x, center.y, minimum.z + height * 0.63))
    spine2 = Vector((center.x, center.y, minimum.z + height * 0.70))
    neck = Vector((center.x, center.y, minimum.z + height * 0.79))
    head = Vector((center.x, center.y, minimum.z + height * 0.86))
    head_top = Vector((center.x, center.y, minimum.z + height * 0.98))

    positions = {
        "mixamorig:Hips": (hips_center, spine0, None),
        "mixamorig:Spine": (spine0, spine1, "mixamorig:Hips"),
        "mixamorig:Spine1": (spine1, spine2, "mixamorig:Spine"),
        "mixamorig:Spine2": (spine2, neck, "mixamorig:Spine1"),
        "mixamorig:Neck": (neck, head, "mixamorig:Spine2"),
        "mixamorig:Head": (head, head_top, "mixamorig:Neck"),
    }

    for side, shoulder_xy, elbow_xy, hand_xy, depth in (
        ("Left", profile.left_shoulder, profile.left_elbow, profile.left_hand, profile.left_arm_depth),
        ("Right", profile.right_shoulder, profile.right_elbow, profile.right_hand, profile.right_arm_depth),
    ):
        shoulder = profile_point(center, minimum, height, shoulder_xy)
        elbow = profile_point(center, minimum, height, elbow_xy, depth * 0.55)
        hand = profile_point(center, minimum, height, hand_xy, depth)
        hand_tip = hand + (hand - elbow).normalized() * max(height * 0.055, (hand - elbow).length * 0.25)
        positions[f"mixamorig:{side}Shoulder"] = (spine2, shoulder, "mixamorig:Spine2")
        positions[f"mixamorig:{side}Arm"] = (shoulder, elbow, f"mixamorig:{side}Shoulder")
        positions[f"mixamorig:{side}ForeArm"] = (elbow, hand, f"mixamorig:{side}Arm")
        positions[f"mixamorig:{side}Hand"] = (hand, hand_tip, f"mixamorig:{side}ForeArm")

    for side, hip_xy, knee_xy, foot_xy, hip_depth, knee_depth, foot_depth in (
        ("Left", profile.left_hip, profile.left_knee, profile.left_foot,
         profile.left_hip_depth, profile.left_knee_depth, profile.left_foot_depth),
        ("Right", profile.right_hip, profile.right_knee, profile.right_foot,
         profile.right_hip_depth, profile.right_knee_depth, profile.right_foot_depth),
    ):
        hip = profile_point(center, minimum, height, hip_xy, hip_depth)
        knee = profile_point(center, minimum, height, knee_xy, knee_depth)
        ankle = profile_point(center, minimum, height, foot_xy, foot_depth)
        forward = Vector((0.0, -height * 0.075, height * 0.005))
        toe = ankle + forward
        toe_tip = toe + forward * 0.55
        positions[f"mixamorig:{side}UpLeg"] = (hip, knee, "mixamorig:Hips")
        positions[f"mixamorig:{side}Leg"] = (knee, ankle, f"mixamorig:{side}UpLeg")
        positions[f"mixamorig:{side}Foot"] = (ankle, toe, f"mixamorig:{side}Leg")
        positions[f"mixamorig:{side}ToeBase"] = (toe, toe_tip, f"mixamorig:{side}Foot")

    for bone_name, (head_position, tail_position, parent_name) in positions.items():
        bone = data.edit_bones.new(bone_name)
        bone.head = head_position
        bone.tail = tail_position if (tail_position - head_position).length > 1e-5 else head_position + Vector((0, 0, height * 0.02))
        bone.use_deform = True
        if parent_name:
            bone.parent = data.edit_bones[parent_name]
    bpy.ops.object.mode_set(mode="OBJECT")
    return armature


def segment_distance(point: Vector, start: Vector, end: Vector) -> float:
    delta = end - start
    if delta.length_squared < 1e-12:
        return (point - start).length
    factor = max(0.0, min(1.0, (point - start).dot(delta) / delta.length_squared))
    return (point - (start + delta * factor)).length


def normalize_weights(weights: dict[str, float]) -> dict[str, float]:
    total = sum(max(0.0, value) for value in weights.values())
    if total < 1e-8:
        return {"mixamorig:Hips": 1.0}
    return {name: max(0.0, value) / total for name, value in weights.items() if value > 1e-5}


def weight_coverage(mesh) -> float:
    if not mesh.data.vertices:
        return 0.0
    weighted = sum(1 for vertex in mesh.data.vertices if any(item.weight > 1e-6 for item in vertex.groups))
    return weighted / len(mesh.data.vertices)


def clear_skinning(mesh):
    mesh.parent = None
    for modifier in list(mesh.modifiers):
        if modifier.type in {"ARMATURE", "DATA_TRANSFER"}:
            mesh.modifiers.remove(modifier)
    mesh.vertex_groups.clear()


def parent_with_bone_heat(mesh, armature) -> float:
    bpy.ops.object.select_all(action="DESELECT")
    mesh.select_set(True)
    armature.select_set(True)
    bpy.context.view_layer.objects.active = armature
    try:
        bpy.ops.object.parent_set(type="ARMATURE_AUTO", keep_transform=True)
    except RuntimeError:
        return 0.0
    return weight_coverage(mesh)


def try_proxy_bone_heat(mesh, armature, name: str, height: float) -> bool:
    proxy = mesh.copy()
    proxy.data = mesh.data.copy()
    proxy.name = f"{name}_WeightProxy"
    bpy.context.collection.objects.link(proxy)
    clear_skinning(proxy)
    bpy.ops.object.select_all(action="DESELECT")
    proxy.select_set(True)
    bpy.context.view_layer.objects.active = proxy
    proxy.data.remesh_voxel_size = max(0.004, height / 135.0)
    proxy.data.remesh_voxel_adaptivity = 0.0
    try:
        bpy.ops.object.voxel_remesh()
        coverage = parent_with_bone_heat(proxy, armature)
        if coverage < 0.98:
            print(f"CHARACTER_PROXY_HEAT_FAILED name={name} coverage={coverage:.3f}")
            return False

        clear_skinning(mesh)
        for group in proxy.vertex_groups:
            mesh.vertex_groups.new(name=group.name)
        transfer = mesh.modifiers.new(name="Transfer bone heat", type="DATA_TRANSFER")
        transfer.object = proxy
        transfer.use_vert_data = True
        transfer.data_types_verts = {"VGROUP_WEIGHTS"}
        transfer.vert_mapping = "POLYINTERP_NEAREST"
        transfer.mix_mode = "REPLACE"
        bpy.ops.object.select_all(action="DESELECT")
        mesh.select_set(True)
        bpy.context.view_layer.objects.active = mesh
        bpy.ops.object.modifier_apply(modifier=transfer.name)
        mesh.parent = armature
        modifier = mesh.modifiers.new(name="Armature", type="ARMATURE")
        modifier.object = armature
        result = weight_coverage(mesh) >= 0.98
        print(f"CHARACTER_PROXY_HEAT_{'OK' if result else 'FAILED'} name={name} coverage={weight_coverage(mesh):.3f}")
        return result
    finally:
        if proxy.name in bpy.data.objects:
            bpy.data.objects.remove(proxy, do_unlink=True)


def assign_geodesic_weights(mesh, armature, name: str, center: Vector, height: float) -> bool:
    deform_names = (
        "mixamorig:Hips", "mixamorig:Spine", "mixamorig:Spine1", "mixamorig:Spine2", "mixamorig:Head",
        "mixamorig:LeftArm", "mixamorig:LeftForeArm", "mixamorig:LeftHand",
        "mixamorig:RightArm", "mixamorig:RightForeArm", "mixamorig:RightHand",
        "mixamorig:LeftUpLeg", "mixamorig:LeftLeg", "mixamorig:LeftFoot",
        "mixamorig:RightUpLeg", "mixamorig:RightLeg", "mixamorig:RightFoot",
    )
    vertices = [mesh.matrix_world @ vertex.co for vertex in mesh.data.vertices]
    minimum_z = min(value.z for value in vertices)
    adjacency = [[] for _ in vertices]
    for edge in mesh.data.edges:
        first, second = edge.vertices
        length = max(1e-6, (vertices[first] - vertices[second]).length)
        adjacency[first].append((second, length))
        adjacency[second].append((first, length))

    components = []
    component_index = [-1] * len(vertices)
    for start in range(len(vertices)):
        if component_index[start] >= 0:
            continue
        index = len(components)
        stack = [start]
        component_index[start] = index
        members = []
        while stack:
            current = stack.pop()
            members.append(current)
            for neighbor, _ in adjacency[current]:
                if component_index[neighbor] < 0:
                    component_index[neighbor] = index
                    stack.append(neighbor)
        components.append(members)

    landmarks = {}
    for bone_name in deform_names:
        bone = armature.data.bones[bone_name]
        factor = 0.18 if bone_name.endswith("Hand") else 0.45
        landmarks[bone_name] = armature.matrix_world @ bone.head_local.lerp(bone.tail_local, factor)

    def candidate_allowed(bone_name: str, point: Vector) -> bool:
        if "Left" in bone_name and point.x > center.x + height * 0.025:
            return False
        if "Right" in bone_name and point.x < center.x - height * 0.025:
            return False
        normalized_height = (point.z - minimum_z) / max(height, 1e-6)
        if bone_name.endswith("Head") and normalized_height < 0.76:
            return False
        if ("Leg" in bone_name or bone_name.endswith("Foot")) and normalized_height > 0.56:
            return False
        if ("Arm" in bone_name or bone_name.endswith("Hand")) and normalized_height < 0.38:
            return False
        return True

    seeds = {bone_name: [] for bone_name in deform_names}
    for bone_name, landmark in landmarks.items():
        nearest = sorted(
            ((vertices[index] - landmark).length_squared, index)
            for index in range(len(vertices)) if candidate_allowed(bone_name, vertices[index])
        )[:4]
        seeds[bone_name].extend(index for _, index in nearest)

    seeded_components = {component_index[index] for values in seeds.values() for index in values}
    for index, members in enumerate(components):
        if index in seeded_components:
            continue
        sample = min(members, key=lambda vertex_index: abs(vertices[vertex_index].x - center.x))
        centroid = sum((vertices[item] for item in members), Vector()) / len(members)
        nearest_bone = min(landmarks, key=lambda bone_name: (centroid - landmarks[bone_name]).length_squared)
        seed = min(members, key=lambda vertex_index: (vertices[vertex_index] - landmarks[nearest_bone]).length_squared)
        seeds[nearest_bone].append(seed)

    distances = {}
    for bone_name in deform_names:
        values = [float("inf")] * len(vertices)
        queue = []
        for seed in set(seeds[bone_name]):
            values[seed] = 0.0
            heapq.heappush(queue, (0.0, seed))
        while queue:
            distance, current = heapq.heappop(queue)
            if distance != values[current]:
                continue
            for neighbor, edge_length in adjacency[current]:
                candidate = distance + edge_length
                if candidate < values[neighbor]:
                    values[neighbor] = candidate
                    heapq.heappush(queue, (candidate, neighbor))
        distances[bone_name] = values

    clear_skinning(mesh)
    groups = {bone.name: mesh.vertex_groups.new(name=bone.name) for bone in armature.data.bones}
    blend_distance = height * 0.055
    for index in range(len(vertices)):
        nearest = sorted((distances[bone_name][index], bone_name) for bone_name in deform_names)[:2]
        if not nearest or nearest[0][0] == float("inf"):
            groups["mixamorig:Hips"].add([index], 1.0, "REPLACE")
            continue
        first_distance, first_name = nearest[0]
        second_distance, second_name = nearest[1]
        difference = max(0.0, second_distance - first_distance)
        second_weight = max(0.0, 0.5 * (1.0 - difference / max(1e-6, blend_distance)))
        first_weight = 1.0 - second_weight
        groups[first_name].add([index], first_weight, "REPLACE")
        if second_weight > 1e-4:
            groups[second_name].add([index], second_weight, "REPLACE")

    mesh.parent = armature
    modifier = mesh.modifiers.new(name="Armature", type="ARMATURE")
    modifier.object = armature
    coverage = weight_coverage(mesh)
    print(f"CHARACTER_GEODESIC_SKIN_OK name={name} components={len(components)} coverage={coverage:.3f}")
    return coverage >= 0.999


def chain_weights(point: Vector, start: Vector, joint: Vector, end: Vector, names: tuple[str, str, str]):
    first = segment_distance(point, start, joint)
    second = segment_distance(point, joint, end)
    total = max(1e-6, first + second)
    upper = second / total
    lower = 1.0 - upper
    forearm = end - joint
    projection = max(0.0, min(1.0, (point - joint).dot(forearm) / max(1e-8, forearm.length_squared)))
    hand = max(0.0, min(1.0, (projection - 0.62) / 0.25))
    return normalize_weights({names[0]: upper, names[1]: lower * (1.0 - hand), names[2]: lower * hand})


def assign_weights(mesh, armature, name: str, profile: RigProfile, minimum: Vector, center: Vector, height: float):
    coverage = parent_with_bone_heat(mesh, armature)
    if coverage >= 0.98:
        print(f"CHARACTER_BONE_HEAT_OK name={name} coverage={coverage:.3f}")
        return
    print(f"CHARACTER_BONE_HEAT_FALLBACK name={name} coverage={coverage:.3f}")
    clear_skinning(mesh)
    if assign_geodesic_weights(mesh, armature, name, center, height):
        return
    clear_skinning(mesh)
    if try_proxy_bone_heat(mesh, armature, name, height):
        return
    clear_skinning(mesh)
    groups = {bone.name: mesh.vertex_groups.new(name=bone.name) for bone in armature.data.bones}
    left_shoulder = profile_point(center, minimum, height, profile.left_shoulder)
    left_elbow = profile_point(center, minimum, height, profile.left_elbow, profile.left_arm_depth * 0.55)
    left_hand = profile_point(center, minimum, height, profile.left_hand, profile.left_arm_depth)
    right_shoulder = profile_point(center, minimum, height, profile.right_shoulder)
    right_elbow = profile_point(center, minimum, height, profile.right_elbow, profile.right_arm_depth * 0.55)
    right_hand = profile_point(center, minimum, height, profile.right_hand, profile.right_arm_depth)
    left_hip = profile_point(center, minimum, height, profile.left_hip, profile.left_hip_depth)
    left_knee = profile_point(center, minimum, height, profile.left_knee, profile.left_knee_depth)
    left_foot = profile_point(center, minimum, height, profile.left_foot, profile.left_foot_depth)
    right_hip = profile_point(center, minimum, height, profile.right_hip, profile.right_hip_depth)
    right_knee = profile_point(center, minimum, height, profile.right_knee, profile.right_knee_depth)
    right_foot = profile_point(center, minimum, height, profile.right_foot, profile.right_foot_depth)
    arm_radius = profile.arm_radius * height * (1.35 if name == "manwithsuit1" else 1.0)

    for vertex in mesh.data.vertices:
        point = mesh.matrix_world @ vertex.co
        normalized_height = (point.z - minimum.z) / max(height, 1e-6)
        left_arm_distance = min(segment_distance(point, left_shoulder, left_elbow), segment_distance(point, left_elbow, left_hand))
        right_arm_distance = min(segment_distance(point, right_shoulder, right_elbow), segment_distance(point, right_elbow, right_hand))
        use_left_arm = left_arm_distance < arm_radius and left_arm_distance <= right_arm_distance
        use_right_arm = right_arm_distance < arm_radius and right_arm_distance < left_arm_distance

        if normalized_height >= profile.neck_y and abs(point.x - center.x) <= profile.head_half_width * height:
            weights = {"mixamorig:Head": 1.0}
        elif use_left_arm:
            weights = chain_weights(point, left_shoulder, left_elbow, left_hand,
                                    ("mixamorig:LeftArm", "mixamorig:LeftForeArm", "mixamorig:LeftHand"))
        elif use_right_arm:
            weights = chain_weights(point, right_shoulder, right_elbow, right_hand,
                                    ("mixamorig:RightArm", "mixamorig:RightForeArm", "mixamorig:RightHand"))
        elif normalized_height < 0.51:
            left_distance = min(segment_distance(point, left_hip, left_knee), segment_distance(point, left_knee, left_foot))
            right_distance = min(segment_distance(point, right_hip, right_knee), segment_distance(point, right_knee, right_foot))
            side_total = max(1e-6, left_distance + right_distance)
            left_share = right_distance / side_total
            right_share = 1.0 - left_share
            left = chain_weights(point, left_hip, left_knee, left_foot,
                                 ("mixamorig:LeftUpLeg", "mixamorig:LeftLeg", "mixamorig:LeftFoot"))
            right = chain_weights(point, right_hip, right_knee, right_foot,
                                  ("mixamorig:RightUpLeg", "mixamorig:RightLeg", "mixamorig:RightFoot"))
            weights = normalize_weights({**{key: value * left_share for key, value in left.items()},
                                         **{key: value * right_share for key, value in right.items()}})
        elif normalized_height < 0.57:
            weights = {"mixamorig:Hips": 1.0}
        elif normalized_height < 0.64:
            blend = (normalized_height - 0.57) / 0.07
            weights = {"mixamorig:Spine": 1.0 - blend, "mixamorig:Spine1": blend}
        elif normalized_height < 0.72:
            blend = (normalized_height - 0.64) / 0.08
            weights = {"mixamorig:Spine1": 1.0 - blend, "mixamorig:Spine2": blend}
        else:
            weights = {"mixamorig:Spine2": 1.0}

        for bone_name, weight in weights.items():
            groups[bone_name].add([vertex.index], weight, "REPLACE")

    modifier = mesh.modifiers.new(name="Armature", type="ARMATURE")
    modifier.object = armature
    mesh.parent = armature


def export_fbx(mesh, armature, destination: Path):
    bpy.ops.object.select_all(action="DESELECT")
    mesh.select_set(True)
    armature.select_set(True)
    bpy.context.view_layer.objects.active = armature
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
        bake_anim=False,
        path_mode="COPY",
        embed_textures=True,
    )


def rig_character(name: str):
    clear_scene()
    source = SOURCE_ROOT / f"{name}.glb"
    if not source.exists():
        raise FileNotFoundError(source)
    mesh = import_and_join(source)
    minimum, _, center, height = bounds_for(mesh)
    armature = create_armature(name, PROFILES[name], minimum, center, height)
    assign_weights(mesh, armature, name, PROFILES[name], minimum, center, height)
    destination = OUTPUT_ROOT / f"{name}_blender_rigged.fbx"
    export_fbx(mesh, armature, destination)
    print(f"CHARACTER_BLENDER_RIG_OK name={name} vertices={len(mesh.data.vertices)} bones={len(armature.data.bones)} height={height:.4f} output={destination}")


def main():
    names = tuple(PROFILES)
    if "--" in sys.argv:
        requested = tuple(value.lower() for value in sys.argv[sys.argv.index("--") + 1 :])
        unknown = [value for value in requested if value not in PROFILES]
        if unknown:
            raise ValueError(f"Unknown characters: {unknown}")
        names = requested or names
    for name in names:
        rig_character(name)
    print(f"CHARACTER_BLENDER_RIGS_OK count={len(names)}")


if __name__ == "__main__":
    main()
