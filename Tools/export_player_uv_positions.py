"""Rest-pose position and normal of every player UV triangle, for build_player_tops.py.

    blender -b --factory-startup -P Tools/export_player_uv_positions.py

Writes .tools/player-uvpos.npz: uv (T,3,2), co (T,3,3) world rest positions
(T-pose, metres, Z up), nrm (T,3,3) smooth vertex normals.

With `-- --remake` it reads the remade player (Assets/BodyBuilders/remake/player.blend)
and writes .tools/player-uvpos-remake.npz, its positions warped piecewise onto
the scan's landmarks (hip, navel, chest, neck, head; shoulder, elbow, wrist),
so the garment designs in build_player_tops.py / build_player_bottoms.py fit it.
"""
import sys
import bpy
from pathlib import Path
import numpy as np

root = Path(__file__).resolve().parents[1]
remake = '--remake' in sys.argv
Z_BONES = ('DEF-thigh.L', 'DEF-spine.001', 'DEF-spine.003', 'DEF-spine.004', 'DEF-spine.005')
X_BONES = ('DEF-upper_arm.L', 'DEF-forearm.L', 'DEF-hand.L')


def landmarks(blend):
    bpy.ops.wm.open_mainfile(filepath=str(blend))
    rig = max((o for o in bpy.context.scene.objects if o.type == 'ARMATURE'),
              key=lambda o: sum(b.use_deform for b in o.data.bones))
    mesh = max((o for o in bpy.context.scene.objects if o.type == 'MESH'), key=lambda o: len(o.data.vertices))
    top = max((mesh.matrix_world @ v.co).z for v in mesh.data.vertices)
    head = lambda n: rig.matrix_world @ rig.data.bones[n].head_local  # noqa: E731
    # The nose tip (frontmost point of the head) pins the face: MPFB's head bone starts
    # above its chin, so a bone-only warp pulled the chin down into the collar.
    neck = head('DEF-spine.004').z
    face = [mesh.matrix_world @ v.co for v in mesh.data.vertices]
    nose = min((p for p in face if p.z > neck + 0.06 * top and abs(p.x) < 0.03 * top), key=lambda p: p.y).z
    z = sorted([head(n).z for n in Z_BONES[:-1]] + [nose])
    return [0.0] + z + [top], [0.0] + [abs(head(n).x) for n in X_BONES], top


if remake:
    scan_z, scan_x, scan_top = landmarks(root / 'Assets/BodyBuilders/Player/player_rig.blend')
    own_z, own_x, own_top = landmarks(root / 'Assets/BodyBuilders/remake/player.blend')
    print('LANDMARKS scan', [round(z / scan_top, 3) for z in scan_z], 'own', [round(z / own_top, 3) for z in own_z])
else:
    bpy.ops.wm.open_mainfile(filepath=str(root / 'Assets/BodyBuilders/Player/player_rig.blend'))
m = max((o for o in bpy.context.scene.objects if o.type == 'MESH'), key=lambda o: len(o.data.vertices))
me = m.data
me.calc_loop_triangles()
uvl = me.uv_layers.active.data
mw = m.matrix_world
nw = mw.to_3x3().inverted().transposed()
co = np.array([tuple(mw @ v.co) for v in me.vertices], np.float32)
vn = np.array([tuple((nw @ v.normal).normalized()) for v in me.vertices], np.float32)
if remake:  # onto the scan's landmarks: z per height band, x along the T-pose arm
    sz = scan_z[-1] / own_z[-1]
    co[:, 2] = np.interp(co[:, 2], own_z, scan_z)
    ax = np.abs(co[:, 0])
    co[:, 0] = np.sign(co[:, 0]) * np.interp(ax, own_x, scan_x, right=None)         + np.sign(co[:, 0]) * np.maximum(ax - own_x[-1], 0) * sz
    co[:, 1] *= sz
tris = me.loop_triangles
uv = np.array([[tuple(uvl[l].uv) for l in t.loops] for t in tris], np.float32)
vi = np.array([list(t.vertices) for t in tris], np.int64)
out = root / ('.tools/player-uvpos-remake.npz' if remake else '.tools/player-uvpos.npz')
extra = dict(own_z=own_z, scan_z=scan_z, own_x=own_x, scan_x=scan_x, own_top=own_top) if remake else {}
np.savez_compressed(out, uv=uv, co=co[vi], nrm=vn[vi], **extra)
print('UVPOS', m.name, 'tris', len(tris), 'verts', len(me.vertices),
      'bounds', co.min(0).round(3).tolist(), co.max(0).round(3).tolist())
