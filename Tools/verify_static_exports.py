"""Compare re-exported static FBX bounds and hierarchy contracts to the originals."""

from pathlib import Path

import bpy
from mathutils import Vector


ROOT = Path("D:/GitHub/GymChaos")
ORIGINAL = ROOT / "GymChaos/Assets/Assets"
REIMPORTED = ROOT / ".tools/static-reimport/Assets"
MARKERS = {"Cylinder.028", "Cylinder.029", "Cube.013", "Cylinder.024", "Circle"}


def imported_snapshot(path: Path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path))
    objects = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    points = [obj.matrix_world @ Vector(corner) for obj in objects for corner in obj.bound_box]
    minimum = Vector((min(point[i] for point in points) for i in range(3)))
    maximum = Vector((max(point[i] for point in points) for i in range(3)))
    images = [image for image in bpy.data.images if image.source != "VIEWER"]
    names = {obj.name for obj in bpy.context.scene.objects}
    return minimum, maximum, names, len(images)


def main() -> None:
    checked = 0
    worst_delta = 0.0
    for reimported in sorted(REIMPORTED.glob("*.fbx")):
        original = ORIGINAL / reimported.name
        if not original.is_file():
            raise RuntimeError(f"No original FBX for {reimported.name}")

        old_min, old_max, old_names, _ = imported_snapshot(original)
        new_min, new_max, new_names, image_count = imported_snapshot(reimported)
        old_size = old_max - old_min
        new_size = new_max - new_min
        delta = max(
            max(abs(new_min[i] - old_min[i]) for i in range(3)),
            max(abs(new_max[i] - old_max[i]) for i in range(3)),
        )
        relative = delta / max(0.001, max(old_size))
        worst_delta = max(worst_delta, relative)
        if relative > 0.01:
            raise RuntimeError(
                f"{reimported.name}: footprint changed by {relative:.3%}; "
                f"old={tuple(round(v, 4) for v in old_size)} "
                f"new={tuple(round(v, 4) for v in new_size)}"
            )

        if reimported.name.lower() == "latpulldown.fbx" and not MARKERS.issubset(new_names):
            raise RuntimeError(f"{reimported.name}: required station hierarchy marker missing")

        print(
            f"STATIC_BOUNDS_OK {reimported.name} "
            f"size={tuple(round(v, 4) for v in new_size)} "
            f"delta={relative:.3%} embedded_images={image_count}"
        )
        checked += 1

    print(f"STATIC_EXPORTS_OK checked={checked} worst_relative_delta={worst_delta:.3%}")


if __name__ == "__main__":
    main()
