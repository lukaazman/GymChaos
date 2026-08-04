"""Create temporary T-pose variants for scans that Mixamo cannot autorig.

The source scans and the normal staging exports are never modified.  A few
scans are authored with hands on hips or a bent accessory arm, which makes
Mixamo reject otherwise valid marker placement.  This uploader-only variant
straightens the lateral arm geometry into a recognisable T-pose.  Once a
Mixamo skeleton has been created, the downloaded FBX remains the runtime
asset; the variant is kept under .tools and is not shipped.
"""

from pathlib import Path

import bpy
from mathutils import Vector


REPOSITORY_ROOT = Path("D:/GitHub/GymChaos")
SOURCE_ROOT = REPOSITORY_ROOT / "GymChaos/Assets/StreamingAssets/BodyBuilders"
OUTPUT_ROOT = REPOSITORY_ROOT / ".tools/character-staging"
VARIANTS = {"arnold", "cbum", "zyzz", "ronnie", "jay", "goku"}
ARM_PROFILES = {
    "arnold": ((-0.13, 0.72), (-0.27, 0.82), (-0.20, 0.91), (0.13, 0.72), (0.27, 0.82), (0.20, 0.91), 0.095),
    "cbum": ((-0.14, 0.70), (-0.23, 0.62), (-0.12, 0.52), (0.14, 0.70), (0.23, 0.62), (0.12, 0.52), 0.095),
    "ronnie": ((-0.13, 0.70), (-0.18, 0.57), (-0.14, 0.43), (0.13, 0.70), (0.18, 0.57), (0.14, 0.43), 0.095),
    "jay": ((-0.11, 0.70), (-0.14, 0.56), (-0.12, 0.42), (0.11, 0.70), (0.14, 0.56), (0.12, 0.42), 0.095),
    "goku": ((-0.11, 0.70), (-0.15, 0.56), (-0.12, 0.42), (0.11, 0.70), (0.15, 0.56), (0.12, 0.42), 0.095),
    "zyzz": ((-0.10, 0.70), (-0.14, 0.55), (-0.13, 0.40), (0.10, 0.70), (0.13, 0.58), (0.07, 0.43), 0.10),
}


def clear_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.materials, bpy.data.images):
        for datablock in list(datablocks):
            if datablock.users == 0:
                datablocks.remove(datablock)


def mesh_objects():
    return [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]


def segment_distance(point: Vector, start: Vector, end: Vector):
    segment = end - start
    length_squared = segment.length_squared
    if length_squared < 1e-10:
        return (point - start).length, 0.0
    factor = max(0.0, min(1.0, (point - start).dot(segment) / length_squared))
    return (point - (start + segment * factor)).length, factor


def model_point(center_x, center_y, min_z, height, normalized):
    return Vector((center_x + normalized[0] * height, center_y, min_z + normalized[1] * height))


def rotate_arm_vertices(objects, name, min_z, max_z, center_x, center_y):
    profile = ARM_PROFILES[name]
    height = max(0.001, max_z - min_z)
    arm_radius = profile[6] * height
    inner_radius = arm_radius * 1.2
    outer_radius = arm_radius * 2.55
    changed = 0

    chains = []
    for side, points in ((-1.0, profile[0:3]), (1.0, profile[3:6])):
        shoulder = model_point(center_x, center_y, min_z, height, points[0])
        elbow = model_point(center_x, center_y, min_z, height, points[1])
        hand = model_point(center_x, center_y, min_z, height, points[2])
        target_direction = Vector((side, 0.0, 0.0))
        upper_length = max(0.001, (elbow - shoulder).length)
        forearm_length = max(0.001, (hand - elbow).length)
        target_elbow = shoulder + target_direction * upper_length
        target_hand = target_elbow + target_direction * forearm_length
        upper_rotation = (elbow - shoulder).normalized().rotation_difference(target_direction)
        forearm_rotation = (hand - elbow).normalized().rotation_difference(target_direction)
        chains.append((
            side, shoulder, elbow, hand, target_elbow, target_hand,
            upper_rotation, forearm_rotation,
        ))

    for obj in objects:
        inverse = obj.matrix_world.inverted()
        for vertex in obj.data.vertices:
            world = obj.matrix_world @ vertex.co
            normalized_height = (world.z - min_z) / height
            if not 0.36 < normalized_height < 0.96:
                continue

            best = None
            for chain in chains:
                side, shoulder, elbow, hand, target_elbow, _, upper_rotation, forearm_rotation = chain
                # Keep the torso and opposite arm out of each chain's candidate set.
                if side * (world.x - center_x) < height * 0.035:
                    continue
                upper_distance, _ = segment_distance(world, shoulder, elbow)
                forearm_distance, _ = segment_distance(world, elbow, hand)
                distance = min(upper_distance, forearm_distance)
                if distance > outer_radius or (best is not None and distance >= best[0]):
                    continue

                upper_transformed = shoulder + upper_rotation @ (world - shoulder)
                forearm_transformed = target_elbow + forearm_rotation @ (world - elbow)
                denominator = max(1e-6, upper_distance + forearm_distance)
                forearm_weight = upper_distance / denominator
                transformed = upper_transformed.lerp(forearm_transformed, forearm_weight)
                best = (distance, transformed, shoulder)

            if best is None:
                continue

            _, transformed, shoulder = best
            # A narrow shoulder transition avoids tearing the torso seam while
            # retaining a rigid, length-preserving transform over the visible arm.
            shoulder_distance = (world - shoulder).length
            shoulder_blend = max(0.12, min(1.0, (shoulder_distance - arm_radius * 0.08) / (arm_radius * 0.8)))
            if best[0] <= inner_radius:
                distance_blend = 1.0
            else:
                falloff = max(0.0, min(1.0, (outer_radius - best[0]) / (outer_radius - inner_radius)))
                distance_blend = falloff * falloff * (3.0 - 2.0 * falloff)
            blend = shoulder_blend * distance_blend
            vertex.co = inverse @ world.lerp(transformed, blend)
            changed += 1
    return changed


def create_variant(source: Path, destination: Path) -> None:
    clear_scene()
    bpy.ops.import_scene.gltf(filepath=str(source))
    objects = mesh_objects()
    if not objects:
        raise RuntimeError(f"No mesh imported from {source}")

    world_vertices = [obj.matrix_world @ vertex.co for obj in objects for vertex in obj.data.vertices]
    min_z = min(vertex.z for vertex in world_vertices)
    max_z = max(vertex.z for vertex in world_vertices)
    height = max(0.001, max_z - min_z)
    center_x = (min(vertex.x for vertex in world_vertices) + max(vertex.x for vertex in world_vertices)) * 0.5
    center_y = sum(vertex.y for vertex in world_vertices) / len(world_vertices)
    changed = rotate_arm_vertices(objects, source.stem, min_z, max_z, center_x, center_y)

    for obj in bpy.context.scene.objects:
        obj.select_set(obj.type in {"MESH", "EMPTY"})
    bpy.context.view_layer.objects.active = objects[0]
    destination.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=str(destination),
        export_format="GLB",
        use_selection=True,
        export_materials="EXPORT",
        export_texcoords=True,
        export_normals=True,
        export_animations=False,
        export_image_format="AUTO",
    )
    print(f"CHARACTER_AUTORIG_VARIANT {source.name} changed_vertices={changed} -> {destination.name}")


def main() -> None:
    OUTPUT_ROOT.mkdir(parents=True, exist_ok=True)
    for name in sorted(VARIANTS):
        source = SOURCE_ROOT / f"{name}.glb"
        if not source.exists():
            raise FileNotFoundError(source)
        create_variant(source, OUTPUT_ROOT / f"{name}-tpose.glb")
    print(f"CHARACTER_AUTORIG_VARIANTS_OK count={len(VARIANTS)}")


if __name__ == "__main__":
    main()
