"""Audit source, neural-rigged, and final character geometry/material data."""

from __future__ import annotations

from pathlib import Path

import bpy


ROOT = Path("D:/GitHub/GymChaos")
SOURCE_ROOT = ROOT / "GymChaos/Assets/StreamingAssets/BodyBuilders"
MIA_ROOT = ROOT / ".tools/ComfyUI/output"
FINAL_ROOT = ROOT / ".tools/character-animated"
CHARACTERS = ("arnold", "cbum", "zyzz", "ronnie", "jay", "goku", "manwithsuit1")


def clear_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for collection in (
        bpy.data.meshes,
        bpy.data.armatures,
        bpy.data.materials,
        bpy.data.images,
        bpy.data.actions,
    ):
        for block in list(collection):
            collection.remove(block)


def import_asset(path: Path) -> None:
    if path.suffix.lower() == ".glb":
        bpy.ops.import_scene.gltf(filepath=str(path))
    else:
        bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False, use_image_search=True)


def audit(label: str, path: Path) -> None:
    clear_scene()
    import_asset(path)
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    vertices = sum(len(obj.data.vertices) for obj in meshes)
    edges = sum(len(obj.data.edges) for obj in meshes)
    polygons = sum(len(obj.data.polygons) for obj in meshes)
    loops = sum(len(obj.data.loops) for obj in meshes)
    slots = sum(len(obj.material_slots) for obj in meshes)
    texture_nodes = []
    for material in bpy.data.materials:
        if material.use_nodes and material.node_tree is not None:
            for node in material.node_tree.nodes:
                if node.type == "TEX_IMAGE" and node.image is not None:
                    texture_nodes.append(
                        f"{node.image.name}:packed={node.image.packed_file is not None}:"
                        f"size={node.image.size[0]}x{node.image.size[1]}:path={node.image.filepath}"
                    )
    print(
        "CHARACTER_ASSET_AUDIT "
        f"label={label} file={path.name} meshes={len(meshes)} vertices={vertices} "
        f"edges={edges} polygons={polygons} loops={loops} material_slots={slots} "
        f"materials={len(bpy.data.materials)} textures={len(texture_nodes)}"
    )
    for texture in texture_nodes:
        print(f"CHARACTER_ASSET_TEXTURE label={label} {texture}")


def main() -> None:
    for name in CHARACTERS:
        audit(f"{name}:source", SOURCE_ROOT / f"{name}.glb")
        audit(f"{name}:mia", MIA_ROOT / f"{name}_mia_authored_mia.fbx")
        audit(f"{name}:final", FINAL_ROOT / f"{name}_mixamo_rigged.fbx")
    print("CHARACTER_ASSET_AUDIT_OK")


if __name__ == "__main__":
    main()
