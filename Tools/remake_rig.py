"""The one Rigify rig every remade character uses.

Rigify human metarig, minus the face rig, with the two neck segments merged so
the deform set matches what the game code reads (DEF-spine.004 neck,
DEF-spine.005 head, 72 deform bones), in a T-pose to match the Mixamo clips'
rest pose.
"""

from __future__ import annotations

import addon_utils
import bpy
from mathutils import Matrix, Vector


TORSO_BONES = 4  # spine .. spine.003, then one neck (spine.004) and the head (spine.005)


def conform_metarig(metarig: bpy.types.Object) -> dict[str, str]:
    """Drop the face rig and bring the spine to 4 torso bones, 1 neck, 1 head.

    Stock Rigify has 4 torso bones and a 2-bone neck; MPFB's fitted metarig
    has 5 torso bones and a 1-bone neck. Returns old -> new names for every
    bone whose weights move (merged, renamed or removed face bones).
    """
    bpy.context.view_layer.objects.active = metarig
    if bpy.context.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    data = metarig.data.bones
    face = data["face"]
    face_names = [b.name for b in [face] + list(face.children_recursive)]
    # MPFB adds corrective helper bones (elbow, knee, pelvis) outside the contract.
    helpers = [b.name for b in data if "-helper" in b.name]

    chain = [data["spine"].name]
    while True:
        nxt = [c.name for c in data[chain[-1]].children if c.name.startswith("spine.")]
        if not nxt:
            break
        chain.append(nxt[0])
    neck_start = next(i for i, n in enumerate(chain)
                      if metarig.pose.bones[n].rigify_type == "spines.super_head")
    groups = [[n] for n in chain]  # each entry: names merged into its first bone

    def merge(index):  # fold groups[index + 1] into groups[index]
        groups[index] += groups.pop(index + 1)

    while neck_start > TORSO_BONES:
        merge(neck_start - 2)
        neck_start -= 1
    while len(groups) - neck_start > 2:
        merge(neck_start)

    keep_of = {n: g[0] for g in groups for n in g[1:]}
    # The helpers are constraint-driven by the limb they correct: give their
    # weights to that limb.
    for name in helpers:
        keep_of[name] = next(c.subtarget for c in metarig.pose.bones[name].constraints
                             if c.type in {"TRANSFORM", "COPY_TRANSFORMS"} and c.subtarget)
    # MPFB's breast parent is a deforming raw copy blending chest bones; the
    # breasts hang off the chest instead.
    for bone in data:
        if bone.name.startswith("MCH-"):
            helpers.append(bone.name)
            keep_of[bone.name] = chain[TORSO_BONES - 1]
    keep_of.update({n: chain[-1] for n in face_names})
    # Constraints that point at a removed bone follow its replacement; renames
    # below are carried into constraints by Blender itself.
    for pose_bone in metarig.pose.bones:
        for constraint in pose_bone.constraints:
            if getattr(constraint, "subtarget", "") in keep_of:
                constraint.subtarget = keep_of[constraint.subtarget]
            for target in getattr(constraint, "targets", ()):
                if target.subtarget in keep_of:
                    target.subtarget = keep_of[target.subtarget]

    # Renaming onto a just-deleted bone's name hands it the deleted pose
    # channel, so keep each kept chain bone's Rigify setup and restore it.
    def rigify_setup(name):
        pose_bone = metarig.pose.bones[name]
        params = pose_bone.rigify_parameters
        values = {k: getattr(params, k) for k in params.bl_rna.properties.keys()
                  if k != "rna_type" and not params.bl_rna.properties[k].is_readonly
                  and params.bl_rna.properties[k].type in {"BOOLEAN", "INT", "FLOAT", "STRING", "ENUM"}
                  and not getattr(params.bl_rna.properties[k], "is_array", False)}
        return pose_bone.rigify_type, values
    setups = [rigify_setup(g[0]) for g in groups]

    bpy.ops.object.mode_set(mode="EDIT")
    bones = metarig.data.edit_bones
    # The neck starts a new Rigify chain: it must stay unconnected to the torso.
    connected = [bones[g[0]].use_connect for g in groups]
    for name in face_names + helpers:
        bone = bones.get(name)
        if bone is None:
            continue
        for child in list(bone.children):
            if child.name not in face_names and child.name not in helpers:
                child.use_connect = False
                child.parent = bones.get(keep_of[name]) or bone.parent
        bones.remove(bone)
    for group in groups:
        keep = bones[group[0]]
        if len(group) > 1:
            keep.tail = bones[group[-1]].tail.copy()
            for name in group[1:]:
                for child in list(bones[name].children):
                    if child.name not in group:
                        child.use_connect = False
                        child.parent = keep
            for name in group[1:]:
                bones.remove(bones[name])
    for index, group in enumerate(groups):
        bones[group[0]].name = f"tmp_spine_{index}"
    final = ["spine" if i == 0 else f"spine.{i:03d}" for i in range(len(groups))]
    for index, name in enumerate(final):
        bone = bones[f"tmp_spine_{index}"]
        bone.name = name
        if index and connected[index]:
            bone.use_connect = False
            bone.head = bone.parent.tail.copy()
            bone.use_connect = True
        bone.use_deform = True
    bpy.ops.object.mode_set(mode="OBJECT")
    for name, (rigify_type, values) in zip(final, setups):
        pose_bone = metarig.pose.bones[name]
        pose_bone.rigify_type = rigify_type
        for key, value in values.items():
            setattr(pose_bone.rigify_parameters, key, value)
    # One twist segment per limb bone (DEF-thigh.L + DEF-thigh.L.001), as the
    # game rigs have; MPFB asks Rigify for more.
    for pose_bone in metarig.pose.bones:
        if pose_bone.rigify_type.startswith("limbs.") and hasattr(pose_bone.rigify_parameters, "segments"):
            pose_bone.rigify_parameters.segments = 2

    renamed = {n: final[i] for i, g in enumerate(groups) for n in g}
    mapping = dict(renamed)
    for name, keep in keep_of.items():
        mapping[name] = renamed.get(keep, keep)
    return mapping


def conform_groups(obj: bpy.types.Object, mapping: dict[str, str]) -> None:
    """Move metarig-named weights through conform_metarig's bone changes."""
    old = {g.index: g.name for g in obj.vertex_groups if g.name in mapping}
    if not old:
        return
    totals = {}
    for v in obj.data.vertices:
        for g in v.groups:
            if g.group in old and g.weight > 0.0:
                key = (mapping[old[g.group]], v.index)
                totals[key] = totals.get(key, 0.0) + g.weight
    for index in sorted(old, reverse=True):
        obj.vertex_groups.remove(obj.vertex_groups[old[index]])
    for (name, vertex), weight in totals.items():
        group = obj.vertex_groups.get(name) or obj.vertex_groups.new(name=name)
        group.add([vertex], weight, "REPLACE")


def arm_tpose_turns(armature: bpy.types.Object, use_edit_bones: bool) -> dict[str, Matrix]:
    """Armature-space turn per side that raises the upper arm to horizontal."""
    turns = {}
    for side in ("L", "R"):
        bone = (armature.data.edit_bones if use_edit_bones else armature.data.bones)[f"upper_arm.{side}"]
        head = bone.head if use_edit_bones else bone.head_local
        tail = bone.tail if use_edit_bones else bone.tail_local
        direction = (tail - head).normalized()
        flat = Vector((direction.x, direction.y, 0.0)).normalized()
        turns[side] = (Matrix.Translation(head) @ direction.rotation_difference(flat).to_matrix().to_4x4()
                       @ Matrix.Translation(-head))
    return turns


def build_metarig() -> bpy.types.Object:
    """Stock metarig conformed and turned to a T-pose in edit mode (no mesh bound yet)."""
    addon_utils.enable("rigify", default_set=True)
    bpy.ops.object.armature_human_metarig_add()
    metarig = bpy.context.active_object
    conform_metarig(metarig)
    bpy.ops.object.mode_set(mode="EDIT")
    bones = metarig.data.edit_bones
    for side, turn in arm_tpose_turns(metarig, use_edit_bones=True).items():
        upper = bones[f"upper_arm.{side}"]
        # Connected children share their head with the parent's tail, so
        # take every target first and write them after.
        targets = [(b, turn @ b.matrix, b.length) for b in [upper] + list(upper.children_recursive)]
        for bone, matrix, length in targets:
            bone.matrix = matrix
            bone.length = length
    bpy.ops.object.mode_set(mode="OBJECT")
    return metarig


def generate_rig(metarig: bpy.types.Object) -> bpy.types.Object:
    bpy.context.view_layer.objects.active = metarig
    metarig.select_set(True)
    if hasattr(metarig.data, "rigify_rig_basename"):
        metarig.data.rigify_rig_basename = "rig"
    bpy.ops.pose.rigify_generate()
    return bpy.data.objects["rig"]
