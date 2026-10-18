"""Shirt triangles of the player scan, for build_player_shirt_colors.py.

    blender -b --factory-startup -P Tools/extract_player_shirt_uv.py

A triangle is shirt when it sits on the torso band and its texture is dark
cloth. The tank top is charcoal (about 0.03-0.04) and the shorts below it
pure black (about 0.01) with the hem at ~53% of the body height, so the band
starts there: recolouring stops at the shirt and never reaches the shorts.
Only large connected patches count, so stray dark seam triangles stay.
"""
import bpy, json
from pathlib import Path
import numpy as np

root = Path(__file__).resolve().parents[1]
bpy.ops.wm.open_mainfile(filepath=str(root / 'Assets/BodyBuilders/Player/player_rig.blend'))
m = max((o for o in bpy.context.scene.objects if o.type == 'MESH'), key=lambda o: len(o.data.vertices))
img = bpy.data.images.load(str(root / 'GymChaos/Assets/Resources/Characters/Textures/player_authored.png'))
w, h = img.size
px = np.array(img.pixels[:]).reshape(h, w, 4)[:, :, :3]
zs = np.array([(m.matrix_world @ v.co).z for v in m.data.vertices])
vkey = [tuple(round(c, 4) for c in v.co) for v in m.data.vertices]
lo = zs.min(); height = zs.max() - lo
HEM, TOP = 0.53, 0.84
m.data.calc_loop_triangles(); uv = m.data.uv_layers.active.data
tri = []
verts = []
for t in m.data.loop_triangles:
    zf = (zs[list(t.vertices)].mean() - lo) / height
    if not HEM <= zf <= TOP:
        continue
    pts = [[*uv[l].uv] for l in t.loops]
    # Sample the corners and the centre: the whole triangle decides.
    samples = pts + [[sum(p[0] for p in pts) / 3, sum(p[1] for p in pts) / 3]]
    cols = np.array([px[min(h - 1, int(v * (h - 1))), min(w - 1, int(u * (w - 1)))] for u, v in samples])
    if np.median(cols.max(1)) < 0.3:
        tri.append(pts)
        # Join by position: the scan mesh is split along its UV seams.
        verts.append([vkey[v] for v in t.vertices])
# Keep the shirt itself: large connected patches on the mesh. Lone dark
# triangles on the arms or neck (atlas gaps at UV seams) stay as they are.
parent = {}
def find(a):
    while parent.setdefault(a, a) != a:
        parent[a] = parent[parent[a]]
        a = parent[a]
    return a
for vs in verts:
    for v in vs[1:]:
        parent[find(vs[0])] = find(v)
sizes = {}
for vs in verts:
    r = find(vs[0]); sizes[r] = sizes.get(r, 0) + 1
keep = [pts for pts, vs in zip(tri, verts) if sizes[find(vs[0])] >= 300]
(root / '.tools/shirt-uv.json').write_text(json.dumps(keep))
print('SHIRT_REGION', len(keep), 'of', len(tri), 'patches', sorted((n for n in sizes.values() if n >= 300), reverse=True))
