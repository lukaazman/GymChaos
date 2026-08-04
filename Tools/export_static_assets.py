"""Export the packed Blender gym assets as texture-bearing Unity FBX files.

The source .blend files use packed 2K images but the historical FBX exports
were geometry-only.  Unity scene instances already compensate for the
historical export footprint, so the 1.25 export scale is intentional and is
checked by the companion bounds verifier before the files are copied into the
Unity project.
"""

from pathlib import Path

import bpy


REPOSITORY_ROOT = Path("D:/GitHub/GymChaos")
SOURCE_ROOT = REPOSITORY_ROOT / "Assets"
UNITY_ASSET_ROOT = REPOSITORY_ROOT / "GymChaos/Assets/Assets"
OUTPUT_ROOT = REPOSITORY_ROOT / ".tools/static-reimport/Assets"
EXPORT_SCALE = 1.25


def export_blend(source: Path, destination: Path) -> None:
    bpy.ops.wm.open_mainfile(filepath=str(source))
    destination.parent.mkdir(parents=True, exist_ok=True)

    # FBX does not preserve Blender Curve datablocks reliably.  The lat
    # pulldown cable is intentionally named "Bezier Curve" and is discovered
    # by the runtime station code, so convert curves in memory before export;
    # the source .blend file is never saved back.
    curve_objects = [obj for obj in bpy.context.scene.objects if obj.type == "CURVE"]
    for curve in curve_objects:
        bpy.ops.object.select_all(action="DESELECT")
        curve.select_set(True)
        bpy.context.view_layer.objects.active = curve
        bpy.ops.object.convert(target="MESH")

    bpy.ops.export_scene.fbx(
        filepath=str(destination),
        use_selection=False,
        object_types={"MESH", "EMPTY"},
        global_scale=EXPORT_SCALE,
        apply_unit_scale=False,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=False,
        mesh_smooth_type="OFF",
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        primary_bone_axis="Y",
        secondary_bone_axis="X",
        use_armature_deform_only=False,
        use_custom_props=False,
        path_mode="COPY",
        embed_textures=True,
    )
    print(f"EXPORTED {source.name} -> {destination.name}")


def main() -> None:
    OUTPUT_ROOT.mkdir(parents=True, exist_ok=True)
    exported = 0
    skipped = 0
    for source in sorted(SOURCE_ROOT.glob("*.blend")):
        target_name = f"{source.stem}.fbx"
        if not (UNITY_ASSET_ROOT / target_name).is_file():
            skipped += 1
            print(f"SKIP {source.name}: no matching Unity FBX")
            continue
        export_blend(source, OUTPUT_ROOT / target_name)
        exported += 1

    print(f"STATIC_EXPORT_SUMMARY exported={exported} skipped={skipped} scale={EXPORT_SCALE}")


if __name__ == "__main__":
    main()
