"""Bind difficult scans to the downloaded Mixamo skeleton outside Unity.

Mixamo's web auto-rigger rejects several authored scan poses even when the
markers are correct.  The successful Arnold Mixamo FBX provides the standard
Mixamo skeleton and the downloaded Run/Punch clips.  This script uses
Blender's automatic external skinning to bind the original scan mesh to that
same skeleton, keeping the scan geometry/materials while removing the
runtime body-part rigging workaround.

It writes only to .tools/character-staging.  The output is an intermediate
FBX and must be copied into Resources only after its mesh and bone count have
been inspected.
"""

from pathlib import Path

import bpy
from mathutils import Vector


REPOSITORY_ROOT = Path("D:/GitHub/GymChaos")
SKELETON_SOURCE = REPOSITORY_ROOT / "GymChaos/Assets/Resources/Characters/Enemies/Arnold.fbx"
STAGING_ROOT = REPOSITORY_ROOT / ".tools/character-staging"
TARGETS = ("cbum", "ronnie", "manwithsuit1", "zyzz")


def clear_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.materials, bpy.data.images, bpy.data.armatures):
        for datablock in list(datablocks):
            if datablock.users == 0:
                datablocks.remove(datablock)


def import_mixamo_armature():
    bpy.ops.import_scene.fbx(filepath=str(SKELETON_SOURCE))
    armature = next((obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"), None)
    if armature is None:
        raise RuntimeError("Arnold Mixamo FBX has no armature")

    for obj in list(bpy.context.scene.objects):
        if obj.type == "MESH":
            bpy.data.objects.remove(obj, do_unlink=True)
    armature.name = "MixamoRig"
    armature.data.name = "MixamoRig"
    return armature


def find_target_mesh():
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if not meshes:
        raise RuntimeError("Target staging FBX has no mesh")
    return max(meshes, key=lambda obj: len(obj.data.vertices))


def align_target(target, armature) -> None:
    # The staging exporter preserves the centered, one-metre scan convention;
    # align its origin with the Mixamo armature before automatic weights.
    target.location = armature.location
    target.rotation_euler = armature.rotation_euler
    target.scale = armature.scale
    bpy.context.view_layer.objects.active = target
    target.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)


def segment_distance(point: Vector, start: Vector, end: Vector) -> float:
    segment = end - start
    length_squared = segment.length_squared
    if length_squared < 0.000001:
        return (point - start).length
    factor = max(0.0, min(1.0, (point - start).dot(segment) / length_squared))
    return (point - (start + segment * factor)).length


def bind(target, armature) -> None:
    # Scan topology is intentionally disconnected in places.  Blender's heat
    # solver can then leave entire pieces without weights, so use the nearest
    # four real Mixamo bone segments and normalize the weights explicitly.
    # This is still an external armature skinning step; Unity never guesses a
    # shoulder/forearm/hand rig at runtime.
    world_vertices = [target.matrix_world @ vertex.co for vertex in target.data.vertices]
    bones = [bone for bone in armature.data.bones if bone.use_deform]
    if not bones:
        raise RuntimeError("Mixamo armature has no deform bones")

    for group in list(target.vertex_groups):
        target.vertex_groups.remove(group)
    groups = {bone.name: target.vertex_groups.new(name=bone.name) for bone in bones}
    segments = []
    for bone in bones:
        start = armature.matrix_world @ bone.head_local
        end = armature.matrix_world @ bone.tail_local
        segments.append((bone.name, start, end))

    for vertex_index, point in enumerate(world_vertices):
        nearest = sorted(
            ((segment_distance(point, start, end), name) for name, start, end in segments),
            key=lambda item: item[0],
        )[:4]
        inverse_sum = sum(1.0 / max(0.002, distance) for distance, _ in nearest)
        for distance, name in nearest:
            weight = (1.0 / max(0.002, distance)) / inverse_sum
            groups[name].add([vertex_index], weight, "REPLACE")

    # FBX export can discard a vertex group when a disconnected scan happens
    # not to land near that particular bone segment. Keep every Mixamo deform
    # bone represented with a negligible anchor weight so Unity imports the
    # complete skeleton and every clip can address the same hierarchy.
    for bone_index, bone in enumerate(bones):
        groups[bone.name].add([bone_index % len(world_vertices)], 0.0001, "ADD")

    target.parent = armature
    target.matrix_parent_inverse = armature.matrix_world.inverted()
    modifier = target.modifiers.get("MixamoRig Deform")
    if modifier is None:
        modifier = target.modifiers.new("MixamoRig Deform", "ARMATURE")
    modifier.object = armature


def export_target(name: str) -> None:
    clear_scene()
    armature = import_mixamo_armature()
    target_path = STAGING_ROOT / f"{name}.fbx"
    bpy.ops.import_scene.fbx(filepath=str(target_path))
    target = find_target_mesh()
    target.name = "geometry_0"
    align_target(target, armature)
    bind(target, armature)

    bpy.ops.object.select_all(action="DESELECT")
    target.select_set(True)
    armature.select_set(True)
    bpy.context.view_layer.objects.active = armature
    output = STAGING_ROOT / f"{name}-external.fbx"
    bpy.ops.export_scene.fbx(
        filepath=str(output),
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        global_scale=1.0,
        apply_unit_scale=False,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=False,
        mesh_smooth_type="OFF",
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        use_armature_deform_only=False,
        use_custom_props=False,
        path_mode="COPY",
        embed_textures=True,
    )
    bone_count = len(armature.data.bones)
    vertex_group_count = len(target.vertex_groups)
    print(f"CHARACTER_EXTERNAL_STAGED name={name} bones={bone_count} vertex_groups={vertex_group_count} -> {output.name}")


def main() -> None:
    for name in TARGETS:
        export_target(name)
    print(f"CHARACTER_EXTERNAL_STAGING_OK count={len(TARGETS)}")


if __name__ == "__main__":
    main()
