import argparse
import sys
from pathlib import Path

import bpy


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--texture-output")
    script_args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    args = parser.parse_args(script_args)

    source = Path(args.input).resolve()
    target = Path(args.output).resolve()
    target.parent.mkdir(parents=True, exist_ok=True)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(source))
    if args.texture_output:
        texture_target = Path(args.texture_output).resolve()
        texture_target.parent.mkdir(parents=True, exist_ok=True)
        images = [image for image in bpy.data.images if image.name != "Render Result"]
        if images:
            image = max(images, key=lambda item: item.size[0] * item.size[1])
            image.filepath_raw = str(texture_target)
            image.file_format = "PNG"
            image.save()
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=str(target),
        use_selection=True,
        apply_unit_scale=True,
        bake_space_transform=False,
        object_types={"MESH", "EMPTY"},
        path_mode="COPY",
        embed_textures=True,
        add_leaf_bones=False,
    )


if __name__ == "__main__":
    main()
