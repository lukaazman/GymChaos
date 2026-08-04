"""Print compact material/image information from Blender equipment sources."""

from pathlib import Path
import sys

import bpy


def image_names(material):
    if material is None or not material.use_nodes or material.node_tree is None:
        return []
    names = []
    for node in material.node_tree.nodes:
        if node.type == "TEX_IMAGE" and node.image is not None:
            names.append(node.image.name)
    return sorted(set(names))


def main():
    args = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    if not args:
        raise SystemExit("Pass one or more .blend paths after --")

    for raw_path in args:
        path = Path(raw_path).resolve()
        bpy.ops.wm.open_mainfile(filepath=str(path))
        print(f"SOURCE {path.name} objects={len(bpy.data.objects)} materials={len(bpy.data.materials)} images={len(bpy.data.images)}")
        for material in sorted(bpy.data.materials, key=lambda item: item.name.lower()):
            color = tuple(round(value, 4) for value in material.diffuse_color)
            print(
                f"MATERIAL name={material.name!r} color={color} metallic={material.metallic:.4f} "
                f"roughness={material.roughness:.4f} images={image_names(material)}"
            )
        for obj in sorted((item for item in bpy.data.objects if item.type == "MESH"), key=lambda item: item.name.lower()):
            slots = [slot.material.name if slot.material is not None else "<none>" for slot in obj.material_slots]
            print(f"OBJECT name={obj.name!r} slots={slots}")


if __name__ == "__main__":
    main()
