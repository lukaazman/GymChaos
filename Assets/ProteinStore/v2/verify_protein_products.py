"""Checks the remodelled shop products against their brief.

    "C:/Program Files/Blender Foundation/Blender 4.5/blender.exe" -b --factory-startup \
        -P Assets/ProteinStore/v2/verify_protein_products.py

Imports the exported GLBs and the label atlas and checks:
  pouch_isolate  not a box (rounded, tapers to a seal), side art on the sides,
                 front text kept clear of the label edges
  bar_choc       flow-wrap with crimped end fins, wrapper art on its face only
  can_lime       silver base and lid (no label on them), label on the body
Prints PROTEIN_PRODUCTS_VERIFY_OK or exits 1 with the failures.
"""
import json
import sys
from pathlib import Path

import bpy
from mathutils import Vector

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
GLB = REPO / "GymChaos" / "Assets" / "StreamingAssets" / "BodyBuilders" / "protein_products"
LAYOUT = json.loads((HERE / "textures" / "atlas_layout.json").read_text())
ATLAS = LAYOUT["atlas_size"]
failures = []


def check(condition, message):
    if not condition:
        failures.append(message)


def tile_rect(name):
    x, y, w, h = LAYOUT["products"][name]
    return (x / ATLAS, 1 - (y + h) / ATLAS, (x + w) / ATLAS, 1 - y / ATLAS)


def in_tile(uv, name, pad=1e-4):
    u0, v0, u1, v1 = tile_rect(name)
    return u0 - pad <= uv[0] <= u1 + pad and v0 - pad <= uv[1] <= v1 + pad


def load(kind):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(GLB / f"{kind}.glb"))
    obj = next(o for o in bpy.data.objects if o.type == "MESH")
    mesh = obj.data
    uv = mesh.uv_layers.active.data
    faces = []
    for poly in mesh.polygons:
        uvs = [tuple(uv[i].uv) for i in poly.loop_indices]
        verts = [obj.matrix_world @ mesh.vertices[v].co for v in poly.vertices]
        faces.append((poly, verts, uvs, (obj.matrix_world.to_3x3() @ poly.normal).normalized()))
    return obj, faces


def face_tile(uvs, names):
    for name in names:
        if all(in_tile(uv, name) for uv in uvs):
            return name
    return None


# Whey isolate pouch ---------------------------------------------------------
obj, faces = load("pouch_isolate")
check(len(obj.data.vertices) > 60, f"isolate has only {len(obj.data.vertices)} vertices: still a box")
zs = [v.z for _, verts, _, _ in faces for v in verts]
z0, z1 = min(zs), max(zs)
bottom_depth = max(v.y for _, vs, _, _ in faces for v in vs if v.z < z0 + 0.1 * (z1 - z0)) - \
    min(v.y for _, vs, _, _ in faces for v in vs if v.z < z0 + 0.1 * (z1 - z0))
top_depth = max(v.y for _, vs, _, _ in faces for v in vs if v.z > z1 - 0.03 * (z1 - z0)) - \
    min(v.y for _, vs, _, _ in faces for v in vs if v.z > z1 - 0.03 * (z1 - z0))
check(top_depth < 0.3 * bottom_depth, f"isolate does not taper to a seal ({top_depth:.3f} vs {bottom_depth:.3f})")
tiles = [face_tile(uvs, ["isolate", "isolate_side"]) for _, _, uvs, _ in faces]
check(tiles.count("isolate_side") >= 4, "isolate sides do not use the side art")
check(tiles.count("isolate") >= 8, "isolate front/back do not use the label")
oblique = sum(1 for _, _, _, n in faces if 0.2 < abs(n.x) < 0.95 and abs(n.z) < 0.5)
check(oblique >= 8, f"isolate has no rounded corners ({oblique} corner faces)")

# Front label padding: the outer 6% of the isolate tile holds no text pixels.
img = bpy.data.images.load(str(HERE / "textures" / "products_atlas.png"))
px = list(img.pixels)
W = img.size[0]
tx, ty, tw, th = LAYOUT["products"]["isolate"]


def pixel(x, y):
    i = ((W - 1 - y) * W + x) * 4
    return px[i], px[i + 1], px[i + 2]


margin = int(tw * 0.06)
bright = 0
for y in range(ty, ty + int(th * 0.85)):
    for x in list(range(tx, tx + margin)) + list(range(tx + tw - margin, tx + tw)):
        r, g, b = pixel(x, y)
        if max(r, g, b) > 0.35:
            bright += 1
for y in range(ty, ty + margin):
    for x in range(tx, tx + tw):
        r, g, b = pixel(x, y)
        if max(r, g, b) > 0.35:
            bright += 1
check(bright == 0, f"isolate label has {bright} text pixels within 6% of its edges")

# Protein bar ------------------------------------------------------------------
obj, faces = load("bar_choc")
xs = [v.x for _, vs, _, _ in faces for v in vs]
x0, x1 = min(xs), max(xs)
dims = obj.dimensions
thick = min(dims.y, dims.z)
end = [v for _, vs, _, _ in faces for v in vs if abs(v.x - x0) < 1e-4]
end_thickness = min(max(v.y for v in end) - min(v.y for v in end), max(v.z for v in end) - min(v.z for v in end))
check(end_thickness < 0.2 * thick, f"bar ends are not crimped fins ({end_thickness:.4f} vs body {thick:.4f})")
label_faces = [f for f in faces if face_tile(f[2], ["bar_wrap_choc"])]
label_x = [v.x for _, vs, _, _ in label_faces for v in vs]
check(bool(label_faces) and max(label_x) - min(label_x) > 0.8 * (x1 - x0 - 0.016),
      "bar wrapper art does not cover the bar's face")
check(all(abs(n.y) > 0.5 for _, _, _, n in label_faces) or all(abs(n.z) > 0.5 for _, _, _, n in label_faces),
      "bar wrapper art spills onto the bar's edges")
check(len(obj.data.polygons) > 20, "bar is still a single box")

# Energy can -------------------------------------------------------------------
obj, faces = load("can_lime")
zs = [v.z for _, vs, _, _ in faces for v in vs]
z0, z1 = min(zs), max(zs)
caps = [f for f in faces if abs(f[3].z) > 0.9]
check(len(caps) >= 2, "can has no top/bottom")
check(all(face_tile(uvs, ["s_silver"]) for _, _, uvs, _ in caps), "can top/bottom are not silver")
body = [f for f in faces if face_tile(f[2], ["can_lime"])]
check(len(body) >= 8, "can body label is missing")
check(all(abs(n.z) < 0.3 for _, _, _, n in body), "can label covers the lid or base")
shoulder = [f for f in faces if 0.15 < f[3].z < 0.95]
check(len(shoulder) >= 8, "can has no tapered shoulder")

# Shop models are the shelf meshes ---------------------------------------------
# Every exported variant must match place_product's own mesh vertex for vertex
# (positions and UVs), the bar only turned upright.
import importlib.util
import math
from mathutils import Matrix

spec = importlib.util.spec_from_file_location("protein_store_v2", HERE / "create_protein_store_v2.py")
store = importlib.util.module_from_spec(spec)
spec.loader.exec_module(store)
spec = importlib.util.spec_from_file_location("export_products", HERE / "export_protein_products.py")
exporter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(exporter)


def signature(obj):
    mesh = obj.data
    uv = mesh.uv_layers.active.data
    keys = set()
    for poly in mesh.polygons:
        for li, vi in zip(poly.loop_indices, poly.vertices):
            co = obj.matrix_world @ mesh.vertices[vi].co
            keys.add((round(co.x, 4), round(co.y, 4), round(co.z, 4),
                      round(uv[li].uv[0], 3), round(uv[li].uv[1], 3)))
    return keys


matched = 0
for kind in exporter.SHOP_KINDS:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    exporter.store.make_materials()
    built = exporter.build_product(kind)
    expected = signature(built)
    bpy.ops.import_scene.gltf(filepath=str(GLB / f"{kind}.glb"))
    imported = next(o for o in bpy.data.objects if o.type == "MESH" and o is not built)
    actual = signature(imported)
    if expected != actual:
        failures.append(f"{kind}: shop model differs from the shelf mesh "
                        f"({len(expected ^ actual)} differing vertex/UV keys)")
    else:
        matched += 1
check(matched == len(exporter.SHOP_KINDS), f"only {matched}/{len(exporter.SHOP_KINDS)} shop models match the shelf")

if failures:
    print("PROTEIN_PRODUCTS_VERIFY_FAIL\n  " + "\n  ".join(failures))
    sys.exit(1)
print("PROTEIN_PRODUCTS_VERIFY_OK isolate=rounded+side-art+padded bar=fins+face-art can=silver-caps+shoulder "
      f"shelf_match={matched}/{len(exporter.SHOP_KINDS)}")
