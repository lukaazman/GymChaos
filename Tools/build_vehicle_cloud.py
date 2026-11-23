"""Goku's remade flying cloud (reference remake/goku_vehicle.png).

Faceted yellow cloud of overlapping puffs with carved swirls on its sides and
a thin tail that curls up. No wheels, door or lamps. The cloud flies
broadside like the OG one: its long axis is X, the puffy body on +X and the
tail on -X; the game finds the body half from the top heights.

Usage: blender -b -noaudio --python-exit-code 1 -P Tools/build_vehicle_cloud.py -- [--no-render]
"""
import math
import sys
from pathlib import Path

import bmesh

sys.path.insert(0, str(Path(__file__).resolve().parent))
import vehicle_kit as vk  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

SPEC = {
    "id": "cloud",
    "reference": "goku_vehicle.png",
    "paint": "E3B04A",
    "paint_finish": (0.0, 0.7),
    "lamps": False,
    "ref_view": (0.15, -1.0, 0.25),
}
# Puffs: centre (x, y, z) and radii (rx, ry, rz).
PUFFS = [
    ((0.55, 0.0, 0.62), (0.62, 0.58, 0.52)),
    ((1.15, 0.05, 0.55), (0.5, 0.52, 0.46)),
    ((0.0, -0.05, 0.52), (0.5, 0.5, 0.44)),
    ((0.75, -0.25, 0.3), (0.55, 0.42, 0.28)),
    ((0.3, 0.3, 0.32), (0.5, 0.35, 0.26)),
]


def puff(centre, radii):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=2, radius=1.0)
    bm.transform(Matrix.Translation(Vector(centre)) @ Matrix.Diagonal((*radii, 1.0)))
    return bm


def swirl(centre, normal, radius, turns=1.6, width=0.035, steps=22):
    """A raised spiral strip lying on the puff surface (carved-swirl look)."""
    n = Vector(normal).normalized()
    u = n.orthogonal().normalized()
    v = n.cross(u)
    pts = []
    for i in range(steps + 1):
        t = i / steps
        a = 2 * math.pi * turns * t
        r = radius * (0.15 + 0.85 * t)
        pts.append(Vector(centre) + (u * math.cos(a) + v * math.sin(a)) * r)
    bm = bmesh.new()
    for p0, p1 in zip(pts, pts[1:]):
        part = vk.beam(p0, p1, width, width)
        mapping = {vert: bm.verts.new(vert.co) for vert in part.verts}
        for f in part.faces:
            bm.faces.new([mapping[x] for x in f.verts])
        part.free()
    return bm


def tail():
    """Tapered tail from the body toward -X, curling up at its tip."""
    bm = bmesh.new()
    rings = []
    steps, seg = 10, 6
    for i in range(steps + 1):
        t = i / steps
        c = Vector((-0.35 - 1.3 * t, 0.0, 0.5 + 0.75 * t ** 2.5))
        r = 0.26 * (1 - t) + 0.02
        rings.append([bm.verts.new(c + Vector((0, math.cos(2 * math.pi * k / seg) * r,
                                                math.sin(2 * math.pi * k / seg) * r * 0.7))) for k in range(seg)])
    for a, b in zip(rings, rings[1:]):
        for k in range(seg):
            bm.faces.new((a[k], a[(k + 1) % seg], b[(k + 1) % seg], b[k]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def build(k):
    body = k.part("Cloud_Body")
    for c, r in PUFFS:
        body.add(puff(c, r), k.paint)
    body.add(tail(), k.paint)
    groove = k.mat("Swirl", "C08F2C", 0.0, 0.75)
    for c, n, r in (((0.55, -0.6, 0.66), (0, -1, 0.1), 0.26), ((1.25, -0.53, 0.58), (0.2, -1, 0.1), 0.2),
                    ((0.75, -0.66, 0.3), (0, -1, -0.2), 0.14), ((0.55, 0.6, 0.66), (0, 1, 0.1), 0.26),
                    ((1.25, 0.58, 0.58), (0.2, 1, 0.1), 0.2)):
        body.add(swirl(c, n, r), groove)


if __name__ == "__main__":
    vk.run(SPEC, builder=build)
