"""Checks the v2 protein.com store build.

    python Assets/ProteinStore/v2/verify_protein_store_v2.py

Reads layout_boxes.json written by create_protein_store_v2.py and the GLB:
  - no two recorded parts overlap (touching faces are fine, tolerance 1 mm);
  - every product stands inside the store and on something (shelf, plinth,
    tier, counter) instead of hanging in the air;
  - the GLB has the objects the Unity side relies on (Solid_*, fridge glass,
    the sign letters) and stays within a sensible triangle budget.
Prints PROTEIN_STORE_V2_VERIFY_OK or exits non-zero with the failures.
"""
import json
import struct
import sys
from collections import defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
TOL = 0.001


def overlap(a, b):
    depth = []
    for i in range(3):
        lo = max(a["min"][i], b["min"][i])
        hi = min(a["max"][i], b["max"][i])
        depth.append(hi - lo)
    return min(depth)


def main():
    data = json.loads((HERE / "layout_boxes.json").read_text())
    boxes = data["boxes"]
    fp = data["footprint"]
    failures = []

    # Spatial hash on 1 m cells keeps the pairwise test fast.
    cells = defaultdict(list)
    for index, box in enumerate(boxes):
        for cx in range(int(box["min"][0] // 1), int(box["max"][0] // 1) + 1):
            for cy in range(int(box["min"][1] // 1), int(box["max"][1] // 1) + 1):
                cells[(cx, cy)].append(index)
    checked = set()
    overlaps = []
    for members in cells.values():
        for i in range(len(members)):
            for j in range(i + 1, len(members)):
                a, b = members[i], members[j]
                key = (min(a, b), max(a, b))
                if key in checked:
                    continue
                checked.add(key)
                d = overlap(boxes[a], boxes[b])
                if d > TOL:
                    overlaps.append((d, boxes[a]["name"], boxes[b]["name"]))
    overlaps.sort(reverse=True)
    for d, a, b in overlaps[:25]:
        failures.append(f"overlap {d:.4f} m: {a} <> {b}")
    if len(overlaps) > 25:
        failures.append(f"... {len(overlaps) - 25} more overlaps")

    half_d, half_w = fp["depth"] / 2, fp["width"] / 2
    supports = [b for b in boxes if b["kind"] in ("fixture", "shell", "floor")]
    unsupported = 0
    products = [b for b in boxes if b["kind"] == "product"]
    for p in products:
        if not (-half_d < p["min"][0] and p["max"][0] < half_d and
                -half_w < p["min"][1] and p["max"][1] < half_w):
            failures.append(f"product outside store: {p['name']} {p['min']}")
        z = p["min"][2]
        if z < 0.02:
            continue
        held = False
        for s in supports:
            if abs(s["max"][2] - z) > 0.003:
                continue
            if (s["min"][0] - TOL <= (p["min"][0] + p["max"][0]) / 2 <= s["max"][0] + TOL and
                    s["min"][1] - TOL <= (p["min"][1] + p["max"][1]) / 2 <= s["max"][1] + TOL):
                held = True
                break
        if not held:
            unsupported += 1
            if unsupported <= 10:
                failures.append(f"product not standing on a surface: {p['name']} at z={z:.3f}")

    glb = (HERE / "protein_store_v2.glb").read_bytes()
    length = struct.unpack("<I", glb[12:16])[0]
    gltf = json.loads(glb[20:20 + length])
    names = [n.get("name", "") for n in gltf["nodes"]]
    solids = [n for n in names if n.startswith("Solid_")]
    glass = [n for n in names if "Fridge" in n and "Glass" in n]
    if len(solids) < 40:
        failures.append(f"too few Solid_ nodes: {len(solids)}")
    if len(glass) < 2:
        failures.append(f"fridge glass nodes missing: {glass}")
    if "Sign_Letters" not in names:
        failures.append("Sign_Letters missing")
    blend_materials = [m.get("name") for m in gltf.get("materials", []) if m.get("alphaMode") == "BLEND"]
    if not any("Fridge_Glass" in (m or "") for m in blend_materials):
        failures.append(f"fridge glass material is not alpha-blended: {blend_materials}")
    triangles = 0
    for mesh in gltf["meshes"]:
        for prim in mesh["primitives"]:
            triangles += gltf["accessors"][prim["indices"]]["count"] // 3
    if triangles > 400000:
        failures.append(f"triangle budget exceeded: {triangles}")

    print(f"boxes={len(boxes)} products={len(products)} overlaps={len(overlaps)} "
          f"unsupported={unsupported} solids={len(solids)} glass={len(glass)} triangles={triangles}")
    if failures:
        for f in failures:
            print("FAIL", f)
        print("PROTEIN_STORE_V2_VERIFY_FAILED")
        sys.exit(1)
    print("PROTEIN_STORE_V2_VERIFY_OK")


if __name__ == "__main__":
    main()
