"""Check a remade character .blend against the asset contract.

The contract lives in .unlazy/assets-202611/ART-CHARACTERS.md. Run headless
with the user preferences loaded (no --factory-startup, or MPFB and Rigify do
not load):

    blender -b -noaudio --python-exit-code 1 -P Tools/verify_remake_asset.py -- <file.blend>
    blender -b -noaudio --python-exit-code 1 -P Tools/verify_remake_asset.py -- <file.blend> --expect-fail
    blender -b -noaudio --python-exit-code 1 -P Tools/verify_remake_asset.py -- --self-test
    blender -b -noaudio --python-exit-code 1 -P Tools/verify_remake_asset.py -- --env-check

Prints REMAKE_ASSET_OK <name> or REMAKE_ASSET_FAIL <name>: <reasons> and exits
non-zero on failure.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

import addon_utils
import bpy
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import remake_rig  # noqa: E402

HEAD_BONE = "DEF-spine.005"
ANCHOR = "EyeAnchor"
LOD1_SUFFIX = "_LOD1"
MAX_INFLUENCES = 4
MAX_TEXTURE = 1024
# Style exception (Goku, anime drawn 1:1 to his reference): more triangles
# and a 2048 texture. Everyone else keeps the default contract.
EXCEPTIONS = {"goku"}
PROFILES = {"default": {"max_tris": 9000, "max_lod1_tris": 2500, "max_texture": 1024},
            "exception": {"max_tris": 15000, "max_lod1_tris": 3500, "max_texture": 2048}}


def canonical_deform_bones() -> set[str]:
    names = {"DEF-spine"} | {f"DEF-spine.00{i}" for i in range(1, 6)}
    per_side = ["pelvis", "thigh", "shin", "foot", "toe", "shoulder", "upper_arm",
                "forearm", "hand", "breast"]
    per_side += [f"palm.0{i}" for i in range(1, 5)]
    per_side += [f"{finger}.0{i}" for finger in ("f_index", "f_middle", "f_ring", "f_pinky", "thumb")
                 for i in range(1, 4)]
    for side in ("L", "R"):
        names |= {f"DEF-{bone}.{side}" for bone in per_side}
        # Rigify twist segments put the side before the index: DEF-thigh.L.001.
        names |= {f"DEF-{bone}.{side}.001" for bone in ("thigh", "shin", "upper_arm", "forearm")}
    return names


CANONICAL = canonical_deform_bones()
assert len(CANONICAL) == 72, len(CANONICAL)


def evaluated_points(obj):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    try:
        return [evaluated.matrix_world @ v.co for v in mesh.vertices]
    finally:
        evaluated.to_mesh_clear()


def surface_distance(obj, world_point) -> float:
    evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    local = evaluated.matrix_world.inverted() @ world_point
    hit, location, _, _ = evaluated.closest_point_on_mesh(local)
    return (evaluated.matrix_world @ location - world_point).length if hit else float("inf")


def check_scene(min_tris: int = 4000, max_tris: int = 9000, max_lod1_tris: int = 2500,
                max_texture: int = MAX_TEXTURE) -> list[str]:
    errors: list[str] = []
    rig = bpy.data.objects.get("rig")
    if rig is None or rig.type != "ARMATURE":
        return ["no armature object named 'rig'"]

    deform = {b.name for b in rig.data.bones if b.use_deform}
    if deform != CANONICAL:
        missing = sorted(CANONICAL - deform)
        extra = sorted(deform - CANONICAL)
        errors.append(f"DEF set differs: missing={missing[:6]} extra={extra[:6]}")

    meshes = [o for o in bpy.data.objects
              if o.type == "MESH" and o.parent == rig and not o.name.startswith("WGT-")
              and any(m.type == "ARMATURE" and m.object == rig for m in o.modifiers)]
    lod0 = [o for o in meshes if not o.name.endswith(LOD1_SUFFIX)]
    lod1 = [o for o in meshes if o.name.endswith(LOD1_SUFFIX)]
    if not lod0:
        return errors + ["no skinned LOD0 mesh parented to rig"]
    if not lod1:
        errors.append(f"no skinned LOD1 mesh (name ending {LOD1_SUFFIX})")

    def tris(objs):
        return sum(len(p.vertices) - 2 for o in objs for p in o.data.polygons)

    lod0_tris, lod1_tris = tris(lod0), tris(lod1)
    if not min_tris <= lod0_tris <= max_tris:
        errors.append(f"LOD0 tris {lod0_tris} outside {min_tris}-{max_tris}")
    if lod1 and lod1_tris > max_lod1_tris:
        errors.append(f"LOD1 tris {lod1_tris} above {max_lod1_tris}")

    materials = {slot.material for o in meshes for slot in o.material_slots if slot.material}
    if len(materials) != 1:
        errors.append(f"expected 1 material, found {len(materials)}")
    else:
        material = next(iter(materials))
        images = [n.image for n in (material.node_tree.nodes if material.node_tree else [])
                  if n.type == "TEX_IMAGE" and n.image]
        # A colour map, optionally with a normal map.
        if len(images) not in (1, 2):
            errors.append(f"material {material.name} needs 1 colour (+1 normal) texture, found {len(images)}")
        for image in images:
            if not 0 < max(image.size) <= max_texture:
                errors.append(f"texture {image.name} size {tuple(image.size)} not in 1..{max_texture}")

    for obj in meshes:
        group_names = {g.index: g.name for g in obj.vertex_groups}
        bad_influence = bad_sum = bad_bone = 0
        for v in obj.data.vertices:
            live = [(group_names[g.group], g.weight) for g in v.groups if g.weight > 1e-4]
            if any(name not in deform for name, _ in live):
                bad_bone += 1
            if not 1 <= len(live) <= MAX_INFLUENCES:
                bad_influence += 1
            if live and not 0.98 <= sum(w for _, w in live) <= 1.02:
                bad_sum += 1
        if bad_bone:
            errors.append(f"{obj.name}: {bad_bone} vertices weighted to non-deform groups")
        if bad_influence:
            errors.append(f"{obj.name}: {bad_influence} vertices outside 1-{MAX_INFLUENCES} influences")
        if bad_sum:
            errors.append(f"{obj.name}: {bad_sum} vertices with unnormalised weights")
        used = {i for p in obj.data.polygons for i in p.vertices}
        loose = len(obj.data.vertices) - len(used)
        degenerate = sum(1 for p in obj.data.polygons if p.area < 1e-9)
        if loose or degenerate:
            errors.append(f"{obj.name}: {loose} loose vertices, {degenerate} zero-area faces")

    points = [p for o in lod0 for p in evaluated_points(o)]
    low = min(p.z for p in points)
    height = max(p.z for p in points) - low
    if abs(low) > 0.02:
        errors.append(f"feet at z={low:.3f}, expected 0 +-0.02")
    if not 1.5 <= height <= 2.3:
        errors.append(f"height {height:.2f} m outside 1.5-2.3")

    for side in ("L", "R"):
        bone = rig.data.bones.get(f"DEF-upper_arm.{side}")
        if bone is None:
            continue
        direction = (rig.matrix_world.to_3x3() @ (bone.tail_local - bone.head_local)).normalized()
        if abs(direction.z) > 0.35 or abs(direction.x) < 0.85:
            errors.append(f"rest pose not a T-pose: upper_arm.{side} direction {tuple(round(c, 2) for c in direction)}")

    anchor = bpy.data.objects.get(ANCHOR)
    head = rig.data.bones.get(HEAD_BONE)
    if anchor is None:
        errors.append(f"no {ANCHOR} empty")
    elif anchor.parent != rig or anchor.parent_type != "BONE" or anchor.parent_bone != HEAD_BONE:
        errors.append(f"{ANCHOR} must be parented to bone {HEAD_BONE}")
    elif head is not None:
        at = anchor.matrix_world.translation
        head_start = rig.matrix_world @ head.head_local
        head_end = rig.matrix_world @ head.tail_local
        if not min(head_start.z, head_end.z) <= at.z <= max(head_start.z, head_end.z):
            errors.append(f"{ANCHOR} z={at.z:.3f} outside head bone {head_start.z:.3f}-{head_end.z:.3f}")
        if head_start.y - at.y < 0.03:
            errors.append(f"{ANCHOR} is not in front of the head (faces -Y)")
        nearest = min(surface_distance(o, at) for o in lod0)
        if nearest > 0.03:
            errors.append(f"{ANCHOR} is {nearest:.3f} m from the mesh, expected <= 0.03")
    return errors


# ---------------------------------------------------------------- self-test


def enable_rigify() -> None:
    addon_utils.enable("rigify", default_set=True)


def build_fixture(mutation: str | None) -> None:
    """A box-per-bone character that satisfies the contract, optionally broken."""
    bpy.ops.wm.read_homefile(use_empty=True)
    metarig = remake_rig.build_metarig()
    rig = remake_rig.generate_rig(metarig)
    metarig.hide_set(True)

    verts, faces, owner = [], [], []
    for bone in rig.data.bones:
        if not bone.use_deform:
            continue
        head, tail = bone.head_local, bone.tail_local
        half = max(bone.length * 0.25, 0.01)
        lo = Vector(tuple(min(head[i], tail[i]) - half for i in range(3)))
        lo.z = max(lo.z, 0.0)  # feet stand on Z = 0
        hi = Vector(tuple(max(head[i], tail[i]) + half for i in range(3)))
        base = len(verts)
        verts += [Vector((x, y, z)) for x in (lo.x, hi.x) for y in (lo.y, hi.y) for z in (lo.z, hi.z)]
        faces += [(base + a, base + b, base + c, base + d) for a, b, c, d in
                  ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3))]
        owner += [bone.name] * 8

    material = bpy.data.materials.new("Fixture")
    material.use_nodes = True
    texture = material.node_tree.nodes.new("ShaderNodeTexImage")
    texture.image = bpy.data.images.new("fixture", 64, 64)

    def skinned(name):
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata([tuple(v) for v in verts], [], faces)
        mesh.materials.append(material)
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        obj.parent = rig
        obj.modifiers.new("Armature", "ARMATURE").object = rig
        for index, bone_name in enumerate(owner):
            group = obj.vertex_groups.get(bone_name) or obj.vertex_groups.new(name=bone_name)
            group.add([index], 1.0, "REPLACE")
        return obj

    body = skinned("Body")
    if mutation != "no_lod1":
        skinned("Body" + LOD1_SUFFIX)

    head = rig.data.bones[HEAD_BONE]
    face = [v for v, name in zip(verts, owner) if name == HEAD_BONE]
    front_y = min(v.y for v in face)
    if mutation != "no_anchor":
        anchor = bpy.data.objects.new(ANCHOR, None)
        bpy.context.scene.collection.objects.link(anchor)
        anchor.parent = rig
        anchor.parent_type = "BONE"
        anchor.parent_bone = HEAD_BONE
        bpy.context.view_layer.update()
        anchor.matrix_world.translation = Vector(
            (head.head_local.x, front_y, (head.head_local.z + head.tail_local.z) * 0.5))

    if mutation == "five_influences":
        extra = [n for n in sorted(CANONICAL) if n != owner[0]][:4]
        for name in extra:
            body.vertex_groups[name].add([0], 0.2, "REPLACE")
        body.vertex_groups[owner[0]].add([0], 0.2, "REPLACE")
    if mutation == "renamed_bone":
        bpy.context.view_layer.objects.active = rig
        bpy.ops.object.mode_set(mode="EDIT")
        rig.data.edit_bones["DEF-toe.L"].name = "DEF-toe_renamed.L"
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.view_layer.update()


def self_test() -> int:
    cases = [(None, None), ("no_anchor", ANCHOR), ("five_influences", "influences"),
             ("renamed_bone", "DEF set"), ("no_lod1", "LOD1")]
    for mutation, expected in cases:
        build_fixture(mutation)
        errors = check_scene(min_tris=500)
        if mutation is None and errors:
            print(f"REMAKE_ASSET_SELFTEST_FAIL valid fixture rejected: {errors}")
            return 1
        if mutation is not None and not any(expected in e for e in errors):
            print(f"REMAKE_ASSET_SELFTEST_FAIL {mutation} not caught: {errors}")
            return 1
        print(f"SELFTEST case={mutation or 'valid'} errors={len(errors)}")
    print(f"REMAKE_ASSET_SELFTEST_OK cases={len(cases)}")
    return 0


def env_check() -> int:
    enable_rigify()
    mpfb = next((m for m in addon_utils.modules() if m.__name__.endswith(".mpfb")), None)
    if mpfb is None or not addon_utils.check(mpfb.__name__)[1]:
        print("REMAKE_ENV_FAIL MPFB extension not enabled (run without --factory-startup)")
        return 1
    import importlib
    human = importlib.import_module(mpfb.__name__ + ".services.humanservice").HumanService.create_human()
    rigify_ok = hasattr(bpy.ops.pose, "rigify_generate")
    if not rigify_ok or len(human.data.vertices) < 1000:
        print(f"REMAKE_ENV_FAIL rigify={rigify_ok} mpfb_vertices={len(human.data.vertices)}")
        return 1
    print(f"REMAKE_ENV_OK mpfb_vertices={len(human.data.vertices)} rigify=ok blender={bpy.app.version_string}")
    return 0


def main() -> int:
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("blend", nargs="?")
    parser.add_argument("--expect-fail", action="store_true")
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--env-check", action="store_true")
    args = parser.parse_args(argv)
    if args.env_check:
        return env_check()
    if args.self_test:
        return self_test()
    if not args.blend:
        parser.error("blend path required")
    bpy.ops.wm.open_mainfile(filepath=str(Path(args.blend).resolve()))
    name = Path(args.blend).stem
    profile = "exception" if name in EXCEPTIONS else "default"
    errors = check_scene(**PROFILES[profile])
    if args.expect_fail:
        if errors:
            print(f"REMAKE_ASSET_EXPECTED_FAIL {name} reasons={len(errors)}: " + "; ".join(errors))
            return 0
        print(f"REMAKE_ASSET_UNEXPECTED_PASS {name}")
        return 1
    if errors:
        print(f"REMAKE_ASSET_FAIL {name}: " + "; ".join(errors))
        return 1
    print(f"REMAKE_ASSET_OK {name} profile={profile}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
