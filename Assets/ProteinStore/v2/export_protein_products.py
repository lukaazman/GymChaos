"""Single protein.com products for the counter shop's 3D inspect view.

    "C:/Program Files/Blender Foundation/Blender 4.5/blender.exe" -b --factory-startup \
        -P Assets/ProteinStore/v2/export_protein_products.py [-- --no-render]

Builds every product with create_protein_store_v2.place_product itself, at
the origin with its label facing Blender -Y, so the shop's inspect model is the
very mesh that stands on the shelves (same segments, UVs and atlas). Only a
flat bar is turned upright (a rigid rotation) so its wrapper faces the camera.

Outputs:
  GymChaos/Assets/StreamingAssets/BodyBuilders/protein_products/<kind>.glb
  Assets/ProteinStore/v2/renders/products/<kind>.png (unless --no-render)
"""
import importlib.util
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
OUT = REPO / "GymChaos" / "Assets" / "StreamingAssets" / "BodyBuilders" / "protein_products"

spec = importlib.util.spec_from_file_location("protein_store_v2", HERE / "create_protein_store_v2.py")
store = importlib.util.module_from_spec(spec)
spec.loader.exec_module(store)

# Every consumable variant on sale; ids must match GymProteinShopCatalog.cs.
SHOP_KINDS = [
    "tub_whey", "tub_whey_choc", "pouch_choc", "pouch_vanilla", "pouch_straw", "pouch_cookie",
    "pouch_banana", "pouch_caramel",
    "pouch_isolate",
    "tub_creatine", "tub_reload", "pouch_rebel",
    "tub_pre_green", "tub_pre_pink", "tub_pre_blue",
    "bar_choc", "bar_coconut", "bar_peanut", "bar_berry",
    "can_lime", "can_orange", "can_purple", "can_red", "rtd_choc", "rtd_vanilla",
    "pb_jar",
    "vit_d3", "omega3", "zinc", "magnesium",
    "tub_bcaa",
]


def build_product(kind):
    store.product_builders.clear()
    store.place_product("Shop", kind, 0.0, 0.0, 0.0, "-y")
    obj = store.product_builders["Shop"].build()
    obj.name = kind
    shape, w, d, h, label, solid = store.PRODUCTS[kind]
    if shape == "bar":
        # Rigid quarter turn about X: the wrapper top (+Z) faces -Y, upright.
        obj.data.transform(Matrix.Translation((0.0, 0.0, d / 2)) @ Matrix.Rotation(math.radians(90), 4, "X"))
    return obj


def export(obj, path):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.gltf(
        filepath=str(path),
        export_format="GLB",
        use_selection=True,
        export_yup=True,
        export_apply=True,
        export_lights=False,
        export_cameras=False,
    )


def render(objects, out_dir):
    scene = bpy.context.scene
    try:
        scene.render.engine = "BLENDER_EEVEE_NEXT"
    except TypeError:
        scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 512
    scene.render.resolution_y = 512
    scene.world = bpy.data.worlds.new("World")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.06, 0.08, 0.14, 1)
    key = bpy.data.lights.new("Key", "AREA")
    key.energy = 60
    key.size = 1.0
    ko = bpy.data.objects.new("Key", key)
    ko.location = (0.6, -0.9, 0.8)
    ko.rotation_euler = (Vector((0, 0, 0.1)) - ko.location).to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(ko)
    cam = bpy.data.cameras.new("Cam")
    co = bpy.data.objects.new("Cam", cam)
    scene.collection.objects.link(co)
    scene.camera = co
    cam.angle = math.radians(30)
    for obj in objects:
        for other in objects:
            other.hide_render = other is not obj
        dims = obj.dimensions
        radius = max(dims.x, dims.y, dims.z) * 0.75
        centre = Vector((0, 0, dims.z / 2))
        eye = centre + Vector((0.35, -1.0, 0.35)).normalized() * (radius / math.tan(cam.angle / 2))
        co.location = eye
        co.rotation_euler = (centre - eye).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = str(out_dir / f"{obj.name}.png")
        bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    bpy.ops.wm.read_factory_settings(use_empty=True)
    store.make_materials()
    OUT.mkdir(parents=True, exist_ok=True)
    objects = []
    for kind in SHOP_KINDS:
        obj = build_product(kind)
        export(obj, OUT / f"{kind}.glb")
        tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
        print(f"PROTEIN_PRODUCT_EXPORTED kind={kind} triangles={tris} "
              f"size={tuple(round(v, 3) for v in obj.dimensions)}")
        objects.append(obj)
    print(f"PROTEIN_PRODUCTS_EXPORTED count={len(objects)} out={OUT}")
    if "--no-render" not in argv:
        out_dir = HERE / "renders" / "products"
        out_dir.mkdir(parents=True, exist_ok=True)
        render(objects, out_dir)


if __name__ == "__main__":
    main()
