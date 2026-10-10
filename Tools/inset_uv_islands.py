"""Pull UV island borders slightly inward so they stop sampling neighbours.

In the scan atlases many islands touch islands of another colour (a navy
suit face next to the white shirt). Bilinear filtering at a border reads the
neighbour texel, which shows as a light fleck on dark cloth. Every vertex on
an island border is moved up to `--texels` texels towards the inside of its
own faces. Positions, normals and indices stay the same; the texture shifts by
less than a texel, which is not visible.

  python Tools/inset_uv_islands.py <in.glb> <out.glb> [--texels 0.6]
"""

import argparse
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import clean_scan_textures as cst  # noqa: E402


def inset(uv, tris, size, texels):
    """uv [n,2] in 0..1, tris [m,3] vertex indices (seams already split)."""
    edges = np.sort(np.concatenate([tris[:, [0, 1]], tris[:, [1, 2]], tris[:, [2, 0]]]), 1)
    uniq, counts = np.unique(edges, axis=0, return_counts=True)
    border = np.zeros(len(uv), bool)
    border[uniq[counts == 1].ravel()] = True
    centroid = uv[tris].mean(1)
    pull = np.zeros_like(uv)
    hits = np.zeros(len(uv))
    for k in range(3):
        v = tris[:, k]
        np.add.at(pull, v, centroid - uv[v])
        np.add.at(hits, v, 1)
    pull /= np.maximum(hits, 1)[:, None]
    length = np.linalg.norm(pull, axis=1)
    step = np.minimum(texels / size, length * 0.3)
    move = np.where(length[:, None] > 1e-12, pull / np.maximum(length, 1e-12)[:, None] * step[:, None], 0)
    out = uv.copy()
    out[border] += move[border]
    return out, int(border.sum())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("dst")
    ap.add_argument("--texels", type=float, default=0.6)
    a = ap.parse_args()
    gltf, binary = cst.read_glb(a.src)
    binary = bytearray(binary)
    moved = 0
    for mesh in gltf["meshes"]:
        for prim in mesh["primitives"]:
            if "TEXCOORD_0" not in prim["attributes"] or "indices" not in prim:
                continue
            acc = gltf["accessors"][prim["attributes"]["TEXCOORD_0"]]
            if acc["componentType"] != 5126:
                continue  # only float UVs are rewritten
            view = gltf["bufferViews"][acc["bufferView"]]
            stride = view.get("byteStride", 0) or 8
            start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
            uv = cst.accessor(gltf, bytes(binary), prim["attributes"]["TEXCOORD_0"])
            tris = cst.accessor(gltf, bytes(binary), prim["indices"]).astype(np.int64).reshape(-1, 3)
            size = 1024
            tex = gltf["materials"][prim.get("material", 0)].get("pbrMetallicRoughness", {}).get("baseColorTexture")
            if tex is not None:
                img = gltf["images"][gltf["textures"][tex["index"]]["source"]]
                bv = gltf["bufferViews"][img["bufferView"]]
                from PIL import Image
                import io
                o = bv.get("byteOffset", 0)
                size = max(Image.open(io.BytesIO(bytes(binary[o:o + bv["byteLength"]]))).size)
            new, n = inset(uv, tris, size, a.texels)
            moved += n
            for i in range(len(new)):
                binary[start + i * stride:start + i * stride + 8] = new[i].astype(np.float32).tobytes()
            if "min" in acc:
                acc["min"] = new.min(0).tolist()
                acc["max"] = new.max(0).tolist()
    cst.write_glb(a.dst, gltf, bytes(binary))
    print(f"UV_INSET {os.path.basename(a.src)} border_vertices={moved} texels={a.texels}")


if __name__ == "__main__":
    main()
