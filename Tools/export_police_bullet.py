"""Export the police bullet prop for runtime use.

Run headless:
    blender -b --factory-startup -P Tools/export_police_bullet.py

Source: Assets/BodyBuilders/items/bullet.glb (a 9x19 mm cartridge scan, about
100k vertices with a 1354 px texture). A bullet is a few centimetres on screen
and several can fly at once, so the runtime copy is decimated to a few hundred
triangles and its texture shrunk to 256 px. Orientation and proportions are
kept: the long axis is glTF +Y with the tip at +Y. The game scales it to size.

Output: GymChaos/Assets/StreamingAssets/BodyBuilders/items/bullet.glb
"""

from __future__ import annotations

import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

PROJECT_ROOT = Path(__file__).resolve().parent.parent
SOURCE = PROJECT_ROOT / "Assets/BodyBuilders/items/bullet.glb"
TARGET = PROJECT_ROOT / "GymChaos/Assets/StreamingAssets/BodyBuilders/items/bullet.glb"
TARGET_TRIANGLES = 900
TEXTURE_SIZE = 256


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(SOURCE))
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if len(meshes) != 1:
        raise RuntimeError(f"Expected one mesh in {SOURCE.name}, found {len(meshes)}")
    mesh = meshes[0]
    bpy.context.view_layer.objects.active = mesh
    mesh.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    source_triangles = sum(len(polygon.vertices) - 2 for polygon in mesh.data.polygons)
    modifier = mesh.modifiers.new("Decimate", "DECIMATE")
    modifier.decimate_type = "COLLAPSE"
    modifier.ratio = min(1.0, TARGET_TRIANGLES / max(source_triangles, 1))
    modifier.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    for polygon in mesh.data.polygons:
        polygon.use_smooth = True

    # Centre the cartridge on its bounds so the game can pivot it at the middle.
    corners = [Vector(corner) for corner in mesh.bound_box]
    centre = sum(corners, Vector()) / 8.0
    mesh.data.transform(Matrix.Translation(-centre))
    mesh.data.update()

    for image in bpy.data.images:
        if image.size[0] > TEXTURE_SIZE or image.size[1] > TEXTURE_SIZE:
            image.scale(TEXTURE_SIZE, TEXTURE_SIZE)

    triangles = sum(len(polygon.vertices) - 2 for polygon in mesh.data.polygons)
    low = [min(Vector(c)[i] for c in mesh.bound_box) for i in range(3)]
    high = [max(Vector(c)[i] for c in mesh.bound_box) for i in range(3)]
    # Tip check in Blender space (glTF +Y is Blender +Z): the narrower end is the tip.
    length = high[2] - low[2]
    def band_radius(z_low, z_high):
        radii = [(v.co.x ** 2 + v.co.y ** 2) ** 0.5 for v in mesh.data.vertices if z_low <= v.co.z <= z_high]
        return max(radii) if radii else 0.0
    tip_radius = band_radius(high[2] - length * 0.06, high[2])
    base_radius = band_radius(low[2], low[2] + length * 0.06)
    if not tip_radius < base_radius * 0.8:
        raise RuntimeError(f"Tip is not at +Z: tip={tip_radius:.3f} base={base_radius:.3f}")

    TARGET.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=str(TARGET), export_format="GLB", use_selection=False,
        export_yup=True, export_apply=True, export_animations=False,
        export_image_format="JPEG", export_image_quality=88)
    print(f"GYMCHAOS_POLICE_BULLET_EXPORT_OK triangles={source_triangles}->{triangles} "
          f"length={length:.3f} width={high[0] - low[0]:.3f} tipRadius={tip_radius:.3f} "
          f"baseRadius={base_radius:.3f} bytes={TARGET.stat().st_size}")


if __name__ == "__main__":
    try:
        main()
    except Exception as exception:  # noqa: BLE001 - a crash must fail the run
        import traceback
        traceback.print_exc()
        print(f"GYMCHAOS_POLICE_BULLET_EXPORT_FAILED {exception}")
        sys.exit(1)
