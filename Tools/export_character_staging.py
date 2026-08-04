"""Stage the mesh-only GLB scans as FBX files for external Mixamo rigging.

The runtime scans in StreamingAssets deliberately remain untouched until each
character has a real external skeleton.  This script creates upload-ready FBX
files with embedded textures, one file per source GLB, and never saves over the
source files.
"""

from pathlib import Path

import bpy


REPOSITORY_ROOT = Path("D:/GitHub/GymChaos")
SOURCE_ROOT = REPOSITORY_ROOT / "GymChaos/Assets/StreamingAssets/BodyBuilders"
OUTPUT_ROOT = REPOSITORY_ROOT / ".tools/character-staging"


def clear_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.materials, bpy.data.images):
        for datablock in list(datablocks):
            if datablock.users == 0:
                datablocks.remove(datablock)


def export_character(source: Path, destination: Path) -> None:
    clear_scene()
    bpy.ops.import_scene.gltf(filepath=str(source))

    # GLB imports can contain helper empties and cameras.  Keep the complete
    # mesh hierarchy and its materials, but avoid exporting non-character
    # scene helpers that confuse automatic-rigging uploaders.
    for obj in bpy.context.scene.objects:
        obj.select_set(obj.type in {"MESH", "EMPTY"})
    mesh_objects = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if not mesh_objects:
        raise RuntimeError(f"No mesh imported from {source}")

    bpy.context.view_layer.objects.active = mesh_objects[0]
    destination.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=str(destination),
        use_selection=True,
        object_types={"MESH", "EMPTY"},
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
    print(f"CHARACTER_STAGED {source.name} -> {destination.name}")


def main() -> None:
    OUTPUT_ROOT.mkdir(parents=True, exist_ok=True)
    staged = 0
    for source in sorted(SOURCE_ROOT.glob("*.glb")):
        export_character(source, OUTPUT_ROOT / f"{source.stem}.fbx")
        staged += 1
    print(f"CHARACTER_STAGING_SUMMARY staged={staged}")


if __name__ == "__main__":
    main()
