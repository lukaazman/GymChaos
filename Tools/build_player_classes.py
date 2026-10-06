"""Build the four class body variants from the unchanged Bodybuilder.

Run headless:
    blender -b -P Tools/build_player_classes.py

The baseline blend and FBX are never written. Each class becomes a shape key
on a copy of the authored mesh (saved to Assets/BodyBuilders/Player/Classes/
player_classes.blend) and a mesh + DEF-skeleton FBX without animation under
GymChaos/Assets/Resources/Player/Classes. The skeleton, bind pose, weights,
UVs and material slot are copied verbatim, so every class shares the baseline
height, joint positions and animation clips.

How muscle size is measured
---------------------------
"Muscle size" is the muscle cross-sectional area of a body segment, taken
perpendicular to its bone at mid length. The total section is modelled as a
fixed core (bone, joint capsule, skin; for the torso also rib cage and
organs) plus muscle:

    area_total = core + muscle,   core = CORE_FRACTION * baseline area

A muscle target m therefore scales the section radius by
sqrt(core + m * (1 - core)). Limbs use core 0.15, the torso 0.55 and the hip
block 0.40. Soft tissue (fat) is added on top for Powerlifting and Strongman
and is not muscle; verify_player_classes.py reports both.
"""

from __future__ import annotations

import json
import math
import sys
from pathlib import Path

import bpy
import numpy as np

TOOLS = Path(__file__).resolve().parent
PROJECT_ROOT = TOOLS.parent
sys.path.insert(0, str(TOOLS))

import export_authored_character_fbx as authored  # noqa: E402

SOURCE_BLEND = PROJECT_ROOT / "Assets/BodyBuilders/Player/player_rig.blend"
CLASSES_BLEND = PROJECT_ROOT / "Assets/BodyBuilders/Player/Classes/player_classes.blend"
CATALOG = PROJECT_ROOT / "GymChaos/Assets/Resources/Classes/catalog.json"
OUTPUT_DIR = PROJECT_ROOT / "GymChaos/Assets/Resources/Player/Classes"
REPORT = PROJECT_ROOT / "Assets/BodyBuilders/Player/Classes/player_classes_report.json"

LIMB_CORE = 0.15
TORSO_CORE = 0.55
HIP_CORE = 0.40
# Share of the class delta that still applies right at a joint, so elbows,
# knees and the waist keep their neighbourhood while mid-segment takes it all.
JOINT_SHARE = 0.3
# Uniform fat layer per unit of soft tissue (radius fraction).
FAT_LAYER = 0.07
CALIBRATION_PASSES = 5


def segment_groups():
    """Body segments: (name, region, core fraction, bones, axis from, axis to)."""
    groups = [
        ("torso", "upper", TORSO_CORE,
         ["DEF-spine.001", "DEF-spine.002", "DEF-spine.003", "DEF-breast.L", "DEF-breast.R"],
         ("DEF-spine.001", "head"), ("DEF-spine.003", "tail")),
        ("hips", "hips", HIP_CORE,
         ["DEF-spine", "DEF-pelvis.L", "DEF-pelvis.R"],
         ("DEF-spine", "head"), ("DEF-spine", "tail")),
    ]
    for side in ("L", "R"):
        groups += [
            (f"shoulder.{side}", "upper", LIMB_CORE, [f"DEF-shoulder.{side}"],
             (f"DEF-shoulder.{side}", "head"), (f"DEF-shoulder.{side}", "tail")),
            (f"upper_arm.{side}", "upper", LIMB_CORE,
             [f"DEF-upper_arm.{side}", f"DEF-upper_arm.{side}.001"],
             (f"DEF-upper_arm.{side}", "head"), (f"DEF-upper_arm.{side}.001", "tail")),
            (f"forearm.{side}", "upper", LIMB_CORE,
             [f"DEF-forearm.{side}", f"DEF-forearm.{side}.001"],
             (f"DEF-forearm.{side}", "head"), (f"DEF-forearm.{side}.001", "tail")),
            (f"thigh.{side}", "lower", LIMB_CORE,
             [f"DEF-thigh.{side}", f"DEF-thigh.{side}.001"],
             (f"DEF-thigh.{side}", "head"), (f"DEF-thigh.{side}.001", "tail")),
            (f"shin.{side}", "lower", LIMB_CORE,
             [f"DEF-shin.{side}", f"DEF-shin.{side}.001"],
             (f"DEF-shin.{side}", "head"), (f"DEF-shin.{side}.001", "tail")),
        ]
    return groups


def radial_scale(muscle: float, core: float) -> float:
    return math.sqrt(core + muscle * (1.0 - core))


def load_mesh_arrays(mesh_object, rig):
    mesh = mesh_object.data
    count = len(mesh.vertices)
    local = np.empty(count * 3, dtype=np.float64)
    mesh.vertices.foreach_get("co", local)
    local = local.reshape(count, 3)
    world_matrix = np.array(mesh_object.matrix_world, dtype=np.float64)
    world = local @ world_matrix[:3, :3].T + world_matrix[:3, 3]

    group_names = {group.index: group.name for group in mesh_object.vertex_groups}
    weights = {}
    for vertex in mesh.vertices:
        total = sum(item.weight for item in vertex.groups if item.weight > 1e-6)
        if total <= 1e-8:
            continue
        for item in vertex.groups:
            if item.weight <= 1e-6:
                continue
            name = group_names[item.group]
            column = weights.get(name)
            if column is None:
                column = weights[name] = np.zeros(count, dtype=np.float64)
            column[vertex.index] = item.weight / total

    # Scan meshes duplicate vertices along UV seams. Every smoothing step works
    # on welded clusters so both sides of a seam move identically.
    keys = np.round(world / 1e-6).astype(np.int64)
    _, cluster = np.unique(keys, axis=0, return_inverse=True)
    cluster = cluster.reshape(-1)
    edges = np.empty(len(mesh.edges) * 2, dtype=np.int64)
    mesh.edges.foreach_get("vertices", edges)
    edges = cluster[edges.reshape(-1, 2)]
    edges = edges[edges[:, 0] != edges[:, 1]]

    rig_matrix = rig.matrix_world
    bones = {}
    for bone in rig.data.bones:
        if bone.use_deform:
            bones[bone.name] = (
                np.array(rig_matrix @ bone.head_local, dtype=np.float64),
                np.array(rig_matrix @ bone.tail_local, dtype=np.float64),
            )
    return local, world, world_matrix, weights, cluster, edges, bones


def cluster_smooth(values, cluster, edges, iterations, factor):
    """Laplacian smoothing of per-vertex values on the welded topology."""
    cluster_count = int(cluster.max()) + 1
    sizes = np.bincount(cluster, minlength=cluster_count).astype(np.float64)
    shape = (cluster_count,) + values.shape[1:]
    current = np.zeros(shape, dtype=np.float64)
    np.add.at(current, cluster, values)
    current /= sizes.reshape((-1,) + (1,) * (values.ndim - 1))
    degree = np.zeros(cluster_count, dtype=np.float64)
    np.add.at(degree, edges[:, 0], 1.0)
    np.add.at(degree, edges[:, 1], 1.0)
    degree = np.maximum(degree, 1.0).reshape((-1,) + (1,) * (values.ndim - 1))
    blend = factor if np.isscalar(factor) else None
    if blend is None:
        per_cluster = np.zeros(cluster_count, dtype=np.float64)
        np.add.at(per_cluster, cluster, factor)
        per_cluster /= sizes
        blend = per_cluster.reshape((-1,) + (1,) * (values.ndim - 1))
    for _ in range(iterations):
        total = np.zeros(shape, dtype=np.float64)
        np.add.at(total, edges[:, 0], current[edges[:, 1]])
        np.add.at(total, edges[:, 1], current[edges[:, 0]])
        current = current + (total / degree - current) * blend
    return current[cluster]


def class_positions(definition, world, weights, cluster, edges, bones, gains=None):
    upper = float(definition["upperBulk"])
    lower = float(definition["lowerBulk"])
    soft = float(definition["softTissue"])
    count = len(world)
    displacement = np.zeros((count, 3), dtype=np.float64)
    region_weight = np.zeros(count, dtype=np.float64)
    zero = np.zeros(count, dtype=np.float64)

    for name, region, core, bone_names, axis_from, axis_to in segment_groups():
        muscle = upper if region == "upper" else lower if region == "lower" else (upper + lower) * 0.5
        # A thin uniform fat layer sits over every soft segment.
        scale = radial_scale(muscle, core) * (1.0 + FAT_LAYER * soft)
        # Calibration gain: skin weights and blending dilute the raw scale, so
        # the delta is boosted until the measured mid-section hits its target.
        scale = 1.0 + (scale - 1.0) * (gains or {}).get(name, 1.0)
        start = bones[axis_from[0]][0 if axis_from[1] == "head" else 1]
        end = bones[axis_to[0]][0 if axis_to[1] == "head" else 1]
        axis = end - start
        length_squared = float(axis @ axis)
        t = np.clip(((world - start) @ axis) / length_squared, 0.0, 1.0)
        radial = world - (start + np.outer(t, axis))
        # Full change mid-segment, JOINT_SHARE of it at the joints.
        taper = JOINT_SHARE + (1.0 - JOINT_SHARE) * np.sin(np.pi * t) ** 0.7
        if name in ("torso", "hips"):
            # The trunk is one column: no taper where chest, waist and hips meet.
            taper = np.ones(count, dtype=np.float64)
        group_weight = sum((weights.get(bone, zero) for bone in bone_names), zero)
        displacement += radial * ((scale - 1.0) * taper * group_weight)[:, None]
        region_weight += group_weight

    # Belly and waist: soft tissue bulges outward from the spine, mostly forward
    # (the model faces -Y), peaking at navel height and fading to chest and hip.
    if soft > 0.0:
        spine_low = bones["DEF-spine"][0]
        spine_high = bones["DEF-spine.003"][1]
        trunk_weight = sum((weights.get(bone, zero) for bone in (
            "DEF-spine", "DEF-spine.001", "DEF-spine.002", "DEF-spine.003",
            "DEF-pelvis.L", "DEF-pelvis.R", "DEF-breast.L", "DEF-breast.R")), zero)
        height = (world[:, 2] - spine_low[2]) / (spine_high[2] - spine_low[2])
        navel = 0.36
        bell = np.exp(-((height - navel) / 0.27) ** 2)
        centre_xy = spine_low[:2] + np.outer(np.clip(height, 0.0, 1.0), (spine_high - spine_low)[:2])
        outward = world[:, :2] - centre_xy
        radius = np.linalg.norm(outward, axis=1)
        direction = outward / np.maximum(radius, 1e-6)[:, None]
        front = np.clip(-direction[:, 1], 0.0, 1.0)
        side = np.abs(direction[:, 0])
        shape = 0.22 + 0.62 * front ** 1.5 + 0.2 * side
        amount = soft * 0.62 * bell * shape * trunk_weight * radius
        displacement[:, :2] += direction * amount[:, None]
        # The belly hangs slightly: forward tissue drops a little.
        displacement[:, 2] -= soft * 0.018 * bell * front * trunk_weight

    # Blend every segment transition (armpit, hip crease, knee, elbow).
    displacement = cluster_smooth(displacement, cluster, edges, 10, 0.5)
    positions = world + displacement

    if soft > 0.0:
        # Body fat hides striations: relax the surface itself where the class
        # carries soft tissue, then restore the volume the relaxation removed.
        mask = np.clip(region_weight, 0.0, 1.0) * soft
        iterations = max(2, int(round(14 * soft)))
        smoothed = cluster_smooth(positions, cluster, edges, iterations, mask * 0.65)
        lost = positions - smoothed
        restored = cluster_smooth(lost, cluster, edges, 12, 0.5)
        positions = smoothed + restored
    return positions


def _hull_area(points):
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
    return abs(sum(hull[i][0] * hull[(i + 1) % len(hull)][1] - hull[(i + 1) % len(hull)][0] * hull[i][1]
                   for i in range(len(hull)))) * 0.5


def mid_section_area(points, selection, start, end):
    axis = end - start
    length = np.linalg.norm(axis)
    direction = axis / length
    t = ((points - start) @ direction) / length
    inside = selection & (np.abs(t - 0.5) < 0.05)
    if inside.sum() < 12:
        return 0.0
    helper = np.array([0.0, 0.0, 1.0]) if abs(direction[2]) < 0.9 else np.array([1.0, 0.0, 0.0])
    u = np.cross(direction, helper)
    u /= np.linalg.norm(u)
    v = np.cross(direction, u)
    relative = points[inside] - start
    return _hull_area(np.stack([relative @ u, relative @ v], axis=1))


def calibrated_positions(definition, world, weights, cluster, edges, bones):
    """Iterate the per-segment gain until the measured section matches the target."""
    upper = float(definition["upperBulk"])
    lower = float(definition["lowerBulk"])
    soft = float(definition["softTissue"])
    zero = np.zeros(len(world))
    gains = {}
    positions = class_positions(definition, world, weights, cluster, edges, bones, gains)
    for _ in range(CALIBRATION_PASSES):
        for name, region, core, bone_names, axis_from, axis_to in segment_groups():
            # Hips blend chest and legs; the soft classes' trunk is shaped by the belly.
            if region == "hips" or name.startswith("shoulder") or (name == "torso" and soft > 0.0):
                continue
            muscle = upper if region == "upper" else lower
            target = (core + muscle * (1.0 - core)) * (1.0 + FAT_LAYER * soft) ** 2
            if abs(target - 1.0) < 1e-6:
                continue
            start = bones[axis_from[0]][0 if axis_from[1] == "head" else 1]
            end = bones[axis_to[0]][0 if axis_to[1] == "head" else 1]
            selection = sum((weights.get(bone, zero) for bone in bone_names), zero) > 0.5
            base = mid_section_area(world, selection, start, end)
            if base <= 0.0:
                continue
            measured = mid_section_area(positions, selection, start, end) / base
            # Radius is the square root of area; move the gain by the radius error.
            wanted = math.sqrt(target) - 1.0
            got = math.sqrt(measured) - 1.0
            if abs(got) > 1e-4:
                gains[name] = float(np.clip(gains.get(name, 1.0) * wanted / got, 0.5, 2.5))
        positions = class_positions(definition, world, weights, cluster, edges, bones, gains)
    return positions, gains


def add_shape_key(mesh_object, name, positions_world, world_matrix):
    inverse = np.linalg.inv(world_matrix)
    local = positions_world @ inverse[:3, :3].T + inverse[:3, 3]
    key = mesh_object.shape_key_add(name=name, from_mix=False)
    key.data.foreach_set("co", local.astype(np.float32).reshape(-1))
    key.value = 0.0
    return local


def export_variant(rig, source_mesh, class_id, local_positions, cluster):
    variant = source_mesh.copy()
    variant.data = source_mesh.data.copy()
    variant.name = "geometry_original_" + class_id
    bpy.context.scene.collection.objects.link(variant)
    if variant.data.shape_keys is not None:
        variant.shape_key_clear()
    variant.data.vertices.foreach_set("co", local_positions.astype(np.float32).reshape(-1))
    variant.data.update()

    # Recompute shading normals from the new surface and share them across
    # seam duplicates; stale scan normals would keep the old muscle relief.
    count = len(variant.data.vertices)
    normals = np.empty(count * 3, dtype=np.float64)
    variant.data.vertices.foreach_get("normal", normals)
    normals = normals.reshape(count, 3)
    cluster_count = int(cluster.max()) + 1
    summed = np.zeros((cluster_count, 3), dtype=np.float64)
    np.add.at(summed, cluster, normals)
    summed /= np.maximum(np.linalg.norm(summed, axis=1), 1e-9)[:, None]
    variant.data.normals_split_custom_set_from_vertices(summed[cluster].tolist())
    variant.data.update()

    target, export_mesh = authored.build_clean_export_rig(rig, variant)
    output = OUTPUT_DIR / f"player_{class_id}.fbx"
    output.parent.mkdir(parents=True, exist_ok=True)
    authored.select_export_objects(target, export_mesh)
    bpy.ops.export_scene.fbx(
        filepath=str(output),
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        add_leaf_bones=False,
        use_armature_deform_only=True,
        bake_anim=False,
        path_mode="STRIP",
        embed_textures=False,
    )
    vertex_count = len(export_mesh.data.vertices)
    for obj in (export_mesh, target, variant):
        bpy.data.objects.remove(obj, do_unlink=True)
    rig.hide_viewport = False
    rig.hide_render = False
    source_mesh.hide_viewport = False
    source_mesh.hide_render = False
    return output, vertex_count


def main():
    catalog = json.loads(CATALOG.read_text(encoding="utf-8"))["classes"]
    variants = [item for item in catalog if item.get("appearance")]
    if len(variants) != 4:
        raise RuntimeError(f"Expected four class variants, catalog has {len(variants)}")

    bpy.ops.wm.open_mainfile(filepath=str(SOURCE_BLEND))
    rig = authored.find_armature()
    mesh_object = authored.find_mesh(rig)
    authored.validate_authored_source("player", rig, mesh_object)
    authored.keep_only_target_objects(rig, mesh_object)
    if mesh_object.data.shape_keys is not None:
        raise RuntimeError("Baseline player mesh already carries shape keys")

    local, world, world_matrix, weights, cluster, edges, bones = load_mesh_arrays(mesh_object, rig)
    mesh_object.shape_key_add(name="Basis", from_mix=False)
    built = {}
    calibration = {}
    for definition in variants:
        positions, gains = calibrated_positions(definition, world, weights, cluster, edges, bones)
        calibration[definition["id"]] = gains
        built[definition["id"]] = add_shape_key(
            mesh_object, definition["appearance"], positions, world_matrix)

    CLASSES_BLEND.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(CLASSES_BLEND), copy=True)

    report = {"source": str(SOURCE_BLEND.relative_to(PROJECT_ROOT)).replace("\\", "/"),
              "limbCore": LIMB_CORE, "torsoCore": TORSO_CORE, "hipCore": HIP_CORE,
              "jointShare": JOINT_SHARE, "classes": {}}
    for definition in variants:
        class_id = definition["id"]
        output, vertex_count = export_variant(rig, mesh_object, class_id, built[class_id], cluster)
        moved = np.linalg.norm(built[class_id] - local, axis=1)
        report["classes"][class_id] = {
            "fbx": str(output.relative_to(PROJECT_ROOT)).replace("\\", "/"),
            "vertices": vertex_count,
            "maxDisplacement": float(moved.max()),
            "upperBulk": definition["upperBulk"],
            "lowerBulk": definition["lowerBulk"],
            "softTissue": definition["softTissue"],
            "calibrationGains": calibration[class_id],
        }
        print(f"GYMCHAOS_PLAYER_CLASS_EXPORT_OK class={class_id} output={output} vertices={vertex_count}")
    REPORT.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(f"GYMCHAOS_PLAYER_CLASSES_BUILD_OK classes={len(variants)} blend={CLASSES_BLEND}")


if __name__ == "__main__":
    main()
