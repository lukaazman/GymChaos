"""Make triangle winding consistent so no face renders as a hole.

Image-to-3D meshes contain stray triangles wound the other way round. With
back-face culling (URP Lit, the runtime GLB loader) such a triangle is not
drawn and the background shows through as a fleck. The mesh is welded by
position, each connected region is walked over shared edges, and every
triangle whose winding disagrees with the majority of its region is flipped
(two index entries swapped). Positions, UVs and normals are untouched.

  python Tools/fix_face_winding.py <in.glb> <out.glb> [--check]
"""

import os
import sys
from collections import deque

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import clean_scan_textures as cst  # noqa: E402

DTYPES = {5121: np.uint8, 5123: np.uint16, 5125: np.uint32}


def orient(positions, tris):
    flat = positions[tris].reshape(-1, 3)
    extent = max(float(np.ptp(flat, 0).max()), 1e-9)
    q = np.round(positions / (extent * 2e-5)).astype(np.int64)
    _, weld = np.unique(q, axis=0, return_inverse=True)
    w = weld.ravel()[tris]
    n = len(tris)
    edges = {}
    for f in range(n):
        a, b, c = w[f]
        if a == b or b == c or a == c:
            continue
        for u, v in ((a, b), (b, c), (c, a)):
            edges.setdefault((min(u, v), max(u, v)), []).append((f, u < v))
    adj = [[] for _ in range(n)]
    for faces in edges.values():
        if len(faces) != 2:
            continue  # boundary or non-manifold edge: no constraint
        (f, d1), (g, d2) = faces
        # consistent winding traverses a shared edge in opposite directions
        same = d1 == d2
        adj[f].append((g, same))
        adj[g].append((f, same))
    flip = np.zeros(n, bool)
    seen = np.zeros(n, bool)
    for start in range(n):
        if seen[start]:
            continue
        comp, state = [], {start: False}
        seen[start] = True
        queue = deque([start])
        while queue:
            f = queue.popleft()
            comp.append(f)
            for g, same in adj[f]:
                want = state[f] ^ same
                if not seen[g]:
                    seen[g] = True
                    state[g] = want
                    queue.append(g)
        flipped = [f for f in comp if state[f]]
        # flip the minority so the region's dominant winding wins
        chosen = flipped if len(flipped) * 2 < len(comp) else [f for f in comp if not state[f]]
        flip[chosen] = True
    return flip


def main(src, dst, check=False):
    gltf, binary = cst.read_glb(src)
    binary = bytearray(binary)
    total = 0
    for mesh in gltf.get("meshes", []):
        for prim in mesh["primitives"]:
            if prim.get("mode", 4) != 4 or "indices" not in prim:
                continue
            pos = cst.accessor(gltf, bytes(binary), prim["attributes"]["POSITION"])
            acc = gltf["accessors"][prim["indices"]]
            view = gltf["bufferViews"][acc["bufferView"]]
            dtype = np.dtype(DTYPES[acc["componentType"]])
            start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
            idx = np.frombuffer(bytes(binary), dtype, count=acc["count"], offset=start).copy()
            tris = idx.reshape(-1, 3).astype(np.int64)
            flip = orient(pos, tris)
            total += int(flip.sum())
            if flip.any() and not check:
                t = idx.reshape(-1, 3)
                t[flip, 1], t[flip, 2] = t[flip, 2].copy(), t[flip, 1].copy()
                binary[start:start + idx.nbytes] = idx.tobytes()
    print(f"WINDING {os.path.basename(src)} flipped={total}")
    if not check:
        cst.write_glb(dst, gltf, bytes(binary))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], "--check" in sys.argv)
