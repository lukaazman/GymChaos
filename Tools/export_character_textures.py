"""Export each scan's original base-color texture into Unity Resources."""

from __future__ import annotations

from pathlib import Path

import bpy


ROOT = Path("D:/GitHub/GymChaos")
SOURCE_ROOT = ROOT / "GymChaos/Assets/StreamingAssets/BodyBuilders"
OUTPUT_ROOT = ROOT / "GymChaos/Assets/Resources/Characters/Textures"
CHARACTERS = ("arnold", "cbum", "zyzz", "ronnie", "jay", "goku", "manwithsuit1")


def clear_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for collection in (bpy.data.meshes, bpy.data.materials, bpy.data.images):
        for block in list(collection):
            collection.remove(block)


def find_base_color_image():
    for material in bpy.data.materials:
        if not material.use_nodes or material.node_tree is None:
            continue
        for node in material.node_tree.nodes:
            if node.type != "BSDF_PRINCIPLED":
                continue
            base_color = node.inputs.get("Base Color")
            if base_color is None or not base_color.is_linked:
                continue
            source = base_color.links[0].from_node
            if source.type == "TEX_IMAGE" and source.image is not None:
                return source.image
    raise RuntimeError("No image is connected to the Principled BSDF Base Color input")


def export_texture(name: str) -> None:
    clear_scene()
    bpy.ops.import_scene.gltf(filepath=str(SOURCE_ROOT / f"{name}.glb"))
    image = find_base_color_image()
    OUTPUT_ROOT.mkdir(parents=True, exist_ok=True)
    destination = OUTPUT_ROOT / f"{name}.png"
    image.filepath_raw = str(destination)
    image.file_format = "PNG"
    image.save()
    print(
        f"CHARACTER_TEXTURE_OK name={name} size={image.size[0]}x{image.size[1]} "
        f"output={destination}"
    )


def main() -> None:
    for name in CHARACTERS:
        export_texture(name)
    print(f"CHARACTER_TEXTURES_OK count={len(CHARACTERS)}")


if __name__ == "__main__":
    main()
