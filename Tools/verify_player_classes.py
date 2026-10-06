"""Measure and render the class body variants built by build_player_classes.py.

Run headless:
    blender -b -P Tools/verify_player_classes.py

Checks, per class: standing height and the head/hand/foot vertices against the
Bodybuilder, mid-segment cross-sectional area against the documented muscle
targets, and the exported FBX skeleton, vertex count, UVs and material slots
against the baseline FBX. Writes Logs/agent/classes-blender-*.png and exits
non-zero with GYMCHAOS_PLAYER_CLASSES_VERIFY_FAILED on any violation.
"""

from __future__ import annotations

import json
import math
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector

TOOLS = Path(__file__).resolve().parent
PROJECT_ROOT = TOOLS.parent
sys.path.insert(0, str(TOOLS))

import build_player_classes as build  # noqa: E402
import export_authored_character_fbx as authored  # noqa: E402

BASELINE_FBX = PROJECT_ROOT / "GymChaos/Assets/Resources/Player/player_authored.fbx"
RENDER_DIR = PROJECT_ROOT / "Logs/agent"
MEASURE_REPORT = PROJECT_ROOT / "Assets/BodyBuilders/Player/Classes/player_classes_measurements.json"
MUSCLE_TOLERANCE = 0.08
FIXED_BONES = ("DEF-spine.005", "DEF-hand", "DEF-f_", "DEF-thumb", "DEF-palm", "DEF-foot", "DEF-toe")


def hull_area(points):
    """Area of the 2D convex hull (monotone chain)."""
    pts = sorted(set(map(tuple, np.round(points, 6))))
    if len(pts) < 3:
        return 0.0

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])

    lower, upper = [], []
    for p in pts:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 0:
            lower.pop()
        lower.append(p)
    for p in reversed(pts):
        while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 0:
            upper.pop()
        upper.append(p)
    hull = lower[:-1] + upper[:-1]
    area = 0.0
    for i in range(len(hull)):
        x1, y1 = hull[i]
        x2, y2 = hull[(i + 1) % len(hull)]
        area += x1 * y2 - x2 * y1
    return abs(area) * 0.5


def section_area(world, selection, start, end, at=0.5, half_width=0.05):
    axis = end - start
    length = np.linalg.norm(axis)
    direction = axis / length
    t = ((world - start) @ direction) / length
    inside = selection & (np.abs(t - at) < half_width)
    if inside.sum() < 12:
        return 0.0
    helper = np.array([0.0, 0.0, 1.0]) if abs(direction[2]) < 0.9 else np.array([1.0, 0.0, 0.0])
    u = np.cross(direction, helper)
    u /= np.linalg.norm(u)
    v = np.cross(direction, u)
    relative = world[inside] - start
    return hull_area(np.stack([relative @ u, relative @ v], axis=1))


def measure(world, base_selection, weights, bones):
    zero = np.zeros(len(world))
    result = {}
    for name, region, core, bone_names, axis_from, axis_to in build.segment_groups():
        if name.endswith(".R") or name.startswith("shoulder"):
            continue
        start = bones[axis_from[0]][0 if axis_from[1] == "head" else 1]
        end = bones[axis_to[0]][0 if axis_to[1] == "head" else 1]
        selection = base_selection[name]
        result[name.replace(".L", "")] = section_area(world, selection, start, end)
    return result


def selections(weights, count):
    zero = np.zeros(count)
    selected = {}
    for name, region, core, bone_names, axis_from, axis_to in build.segment_groups():
        group_weight = sum((weights.get(bone, zero) for bone in bone_names), zero)
        selected[name] = group_weight > 0.5
    return selected


def import_fbx(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False)
    created = [obj for obj in bpy.data.objects if obj not in before]
    armature = next(obj for obj in created if obj.type == "ARMATURE")
    mesh = next(obj for obj in created if obj.type == "MESH")
    bpy.context.view_layer.update()
    bones = {bone.name: (armature.matrix_world @ bone.head_local).copy() for bone in armature.data.bones}
    parents = {bone.name: bone.parent.name if bone.parent else None for bone in armature.data.bones}
    corners = [mesh.matrix_world @ Vector(corner) for corner in mesh.bound_box]
    info = {
        "bones": bones,
        "parents": parents,
        "vertices": len(mesh.data.vertices),
        "uv": len(mesh.data.uv_layers),
        "materials": len(mesh.material_slots),
        "groups": sorted(group.name for group in mesh.vertex_groups),
        "height": max(c.z for c in corners) - min(c.z for c in corners),
        "top": max(c.z for c in corners),
    }
    for obj in created:
        bpy.data.objects.remove(obj, do_unlink=True)
    return info


def render(mesh_object, rig, key_names, world_matrix):
    scene = bpy.context.scene
    rig.hide_render = True
    rig.hide_viewport = True
    copies = []
    spacing = 0.78
    names = ["Basis"] + key_names
    for index, key_name in enumerate(names):
        copy = mesh_object.copy()
        copy.data = mesh_object.data.copy()
        scene.collection.objects.link(copy)
        for modifier in list(copy.modifiers):
            copy.modifiers.remove(modifier)
        for block in copy.data.shape_keys.key_blocks:
            block.value = 1.0 if block.name == key_name else 0.0
        copy.parent = None
        copy.matrix_world = mesh_object.matrix_world.copy()
        copy.location.x += (index - (len(names) - 1) * 0.5) * spacing
        copies.append(copy)
    mesh_object.hide_render = True

    scene.render.engine = "BLENDER_WORKBENCH"
    shading = scene.display.shading
    shading.light = "STUDIO"
    # Neutral clay shading shows the body shape; the dark outfit texture hides it.
    shading.color_type = "SINGLE"
    shading.single_color = (0.82, 0.8, 0.78)
    shading.show_cavity = True
    shading.show_shadows = False
    scene.render.resolution_x = 2400
    scene.render.resolution_y = 1000
    scene.render.film_transparent = False
    scene.world = scene.world or bpy.data.worlds.new("World")
    scene.display.render_aa = "8"

    camera_data = bpy.data.cameras.new("ClassCamera")
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = spacing * len(names) + 0.35
    camera = bpy.data.objects.new("ClassCamera", camera_data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    views = {
        "front": (Vector((0.0, -4.0, 0.5)), (math.radians(90.0), 0.0, 0.0)),
        "back": (Vector((0.0, 4.0, 0.5)), (math.radians(90.0), 0.0, math.radians(180.0))),
    }
    for label, (location, rotation) in views.items():
        camera.location = location
        camera.rotation_euler = rotation
        scene.render.filepath = str(RENDER_DIR / f"classes-blender-{label}.png")
        bpy.ops.render.render(write_still=True)
    # Side view: bodies overlap along X, so turn each one instead of the camera.
    for copy in copies:
        copy.rotation_euler.z = math.radians(90.0)
    camera.location = views["front"][0]
    camera.rotation_euler = views["front"][1]
    scene.render.filepath = str(RENDER_DIR / "classes-blender-side.png")
    bpy.ops.render.render(write_still=True)
    for copy in copies:
        bpy.data.objects.remove(copy, do_unlink=True)
    bpy.data.objects.remove(camera, do_unlink=True)


def main():
    failures = []
    catalog = json.loads(build.CATALOG.read_text(encoding="utf-8"))["classes"]
    variants = [item for item in catalog if item.get("appearance")]
    bpy.ops.wm.open_mainfile(filepath=str(build.CLASSES_BLEND))
    rig = authored.find_armature()
    mesh_object = authored.find_mesh(rig)
    keys = mesh_object.data.shape_keys
    if keys is None:
        raise RuntimeError("player_classes.blend has no shape keys")

    local, world, world_matrix, weights, cluster, edges, bones = build.load_mesh_arrays(mesh_object, rig)
    count = len(world)
    selected = selections(weights, count)
    zero = np.zeros(count)
    fixed_weight = sum((column for name, column in weights.items() if name.startswith(FIXED_BONES)), zero)
    fixed = fixed_weight > 0.98
    base_height = world[:, 2].max() - world[:, 2].min()
    base_areas = measure(world, selected, weights, bones)
    axis_low = bones["DEF-spine"][0]
    axis_high = bones["DEF-spine.003"][1]
    trunk = selected["torso"] | selected["hips"]
    base_waist = section_area(world, trunk, axis_low, axis_high, at=0.36, half_width=0.04)

    # The source blend's Basis must still be the untouched Bodybuilder.
    bpy.ops.wm.open_mainfile(filepath=str(build.SOURCE_BLEND))
    source_mesh = authored.find_mesh(authored.find_armature())
    source = np.empty(len(source_mesh.data.vertices) * 3)
    source_mesh.data.vertices.foreach_get("co", source)
    if source_mesh.data.shape_keys is not None:
        failures.append("baseline blend gained shape keys")
    if len(source) != local.size or np.abs(source.reshape(-1, 3) - local).max() > 1e-6:
        failures.append("Basis differs from the baseline Bodybuilder mesh")
    bpy.ops.wm.open_mainfile(filepath=str(build.CLASSES_BLEND))
    rig = authored.find_armature()
    mesh_object = authored.find_mesh(rig)
    keys = mesh_object.data.shape_keys

    measurements = {"baselineHeight": base_height, "muscleTolerance": MUSCLE_TOLERANCE, "classes": {}}
    rotation = world_matrix[:3, :3]
    for definition in variants:
        block = keys.key_blocks.get(definition["appearance"])
        if block is None:
            failures.append(f"{definition['id']}: shape key {definition['appearance']} missing")
            continue
        data = np.empty(count * 3)
        block.data.foreach_get("co", data)
        shaped = data.reshape(count, 3) @ rotation.T + world_matrix[:3, 3]
        soft = float(definition["softTissue"])
        height = shaped[:, 2].max() - shaped[:, 2].min()
        fixed_move = float(np.linalg.norm(shaped[fixed] - world[fixed], axis=1).max())
        entry = {"height": height, "heightDelta": height - base_height,
                 "headHandFootMaxMove": fixed_move, "segments": {}}
        if abs(height - base_height) > 0.002 * base_height:
            failures.append(f"{definition['id']}: height changed by {height - base_height:+.4f}")
        if fixed_move > 0.004:
            failures.append(f"{definition['id']}: head/hand/foot vertices moved {fixed_move:.4f}")

        areas = measure(shaped, selected, weights, bones)
        for name, region, core, bone_names, axis_from, axis_to in build.segment_groups():
            key = name.replace(".L", "")
            if name.endswith(".R") or name.startswith("shoulder") or key == "hips":
                continue
            target = float(definition["upperBulk"] if region == "upper" else definition["lowerBulk"])
            ratio = areas[key] / base_areas[key]
            # Remove the uniform fat layer to recover the muscle-only section.
            lean_ratio = ratio / (1.0 + build.FAT_LAYER * soft) ** 2
            muscle = (lean_ratio - core) / (1.0 - core)
            entry["segments"][key] = {
                "areaRatio": ratio, "muscleTarget": target, "muscleMeasured": muscle,
                "deviation": muscle - target,
            }
            # The belly deliberately dominates the trunk of the soft classes.
            if not (key == "torso" and soft > 0.0) and abs(muscle - target) > MUSCLE_TOLERANCE:
                failures.append(
                    f"{definition['id']}: {key} muscle {muscle:.3f} vs target {target:.2f}")
        waist = section_area(shaped, trunk, axis_low, axis_high, at=0.36, half_width=0.04)
        entry["waistAreaRatio"] = waist / base_waist
        measurements["classes"][definition["id"]] = entry

    waist = {name: item["waistAreaRatio"] for name, item in measurements["classes"].items()}
    if not (waist.get("strongman", 0) > waist.get("powerlifting", 0) * 1.12 > 1.12):
        failures.append(f"soft-tissue ordering is wrong: {waist}")
    if not (waist.get("cardio", 9) < 1.0 and waist.get("calisthenics", 9) < 1.02):
        failures.append(f"lean classes did not slim the waist: {waist}")

    key_names = [definition["appearance"] for definition in variants]
    RENDER_DIR.mkdir(parents=True, exist_ok=True)
    render(mesh_object, rig, key_names, world_matrix)

    baseline = import_fbx(BASELINE_FBX)
    # Mesh extents are compared against the Basis sent through the identical
    # export path; the shipped baseline FBX stores its mesh in the first clip
    # pose, so only its skeleton, skin groups, UVs and counts are comparable.
    import tempfile
    reference_path = Path(tempfile.gettempdir()) / "gymchaos_player_basis_reference.fbx"
    original_output = build.OUTPUT_DIR
    build.OUTPUT_DIR = reference_path.parent
    try:
        exported, _ = build.export_variant(rig, mesh_object, "basis_reference", local, cluster)
        reference = import_fbx(exported)
        exported.unlink()
    finally:
        build.OUTPUT_DIR = original_output
    measurements["baselineFbxHeight"] = baseline["height"]
    measurements["basisExportHeight"] = reference["height"]
    for definition in variants:
        path = build.OUTPUT_DIR / f"player_{definition['id']}.fbx"
        info = import_fbx(path)
        if set(info["bones"]) != set(baseline["bones"]):
            failures.append(f"{definition['id']}: FBX bone names differ from baseline")
            continue
        worst = max((info["bones"][name] - baseline["bones"][name]).length for name in baseline["bones"])
        if worst > 1e-4:
            failures.append(f"{definition['id']}: FBX bone rest positions differ by {worst:.6f}")
        if info["parents"] != baseline["parents"]:
            failures.append(f"{definition['id']}: FBX bone hierarchy differs")
        if info["groups"] != baseline["groups"] or info["uv"] != baseline["uv"] or \
                info["materials"] != baseline["materials"]:
            failures.append(f"{definition['id']}: FBX skin groups, UVs or material slots differ")
        if info["vertices"] != baseline["vertices"]:
            failures.append(
                f"{definition['id']}: FBX vertex count {info['vertices']} vs {baseline['vertices']}")
        if abs(info["top"] - reference["top"]) > 0.002 * reference["height"] or \
                abs(info["height"] - reference["height"]) > 0.002 * reference["height"]:
            failures.append(
                f"{definition['id']}: FBX standing height differs top={info['top']:.4f} "
                f"referenceTop={reference['top']:.4f} height={info['height']:.4f} "
                f"referenceHeight={reference['height']:.4f}")
        measurements["classes"][definition["id"]]["fbxBoneMaxDelta"] = worst
        measurements["classes"][definition["id"]]["fbxVertices"] = info["vertices"]

    MEASURE_REPORT.write_text(json.dumps(measurements, indent=2), encoding="utf-8")
    for name, item in measurements["classes"].items():
        parts = " ".join(f"{segment}={values['muscleMeasured']:.2f}/{values['muscleTarget']:.2f}"
                         for segment, values in item["segments"].items())
        print(f"GYMCHAOS_PLAYER_CLASS_MEASURE class={name} heightDelta={item['heightDelta']:+.5f} "
              f"fixedMove={item['headHandFootMaxMove']:.5f} waist={item['waistAreaRatio']:.3f} {parts}")
    if failures:
        for failure in failures:
            print("GYMCHAOS_PLAYER_CLASSES_VERIFY_FAILED " + failure)
        sys.exit(1)
    print(f"GYMCHAOS_PLAYER_CLASSES_VERIFY_OK classes={len(variants)} tolerance={MUSCLE_TOLERANCE}")


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception as exception:  # noqa: BLE001 - a crash must be a failed gate
        import traceback
        traceback.print_exc()
        print(f"GYMCHAOS_PLAYER_CLASSES_VERIFY_FAILED {exception}")
        sys.exit(1)
