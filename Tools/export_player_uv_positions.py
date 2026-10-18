"""Rest-pose position and normal of every player UV triangle, for build_player_tops.py.

    blender -b --factory-startup -P Tools/export_player_uv_positions.py

Writes .tools/player-uvpos.npz: uv (T,3,2), co (T,3,3) world rest positions
(T-pose, metres, Z up), nrm (T,3,3) smooth vertex normals.
"""
import bpy
from pathlib import Path
import numpy as np

root = Path(__file__).resolve().parents[1]
bpy.ops.wm.open_mainfile(filepath=str(root / 'Assets/BodyBuilders/Player/player_rig.blend'))
m = max((o for o in bpy.context.scene.objects if o.type == 'MESH'), key=lambda o: len(o.data.vertices))
me = m.data
me.calc_loop_triangles()
uvl = me.uv_layers.active.data
mw = m.matrix_world
nw = mw.to_3x3().inverted().transposed()
co = np.array([tuple(mw @ v.co) for v in me.vertices], np.float32)
vn = np.array([tuple((nw @ v.normal).normalized()) for v in me.vertices], np.float32)
tris = me.loop_triangles
uv = np.array([[tuple(uvl[l].uv) for l in t.loops] for t in tris], np.float32)
vi = np.array([list(t.vertices) for t in tris], np.int64)
out = root / '.tools/player-uvpos.npz'
np.savez_compressed(out, uv=uv, co=co[vi], nrm=vn[vi])
print('UVPOS', m.name, 'tris', len(tris), 'verts', len(me.vertices),
      'bounds', co.min(0).round(3).tolist(), co.max(0).round(3).tolist())
