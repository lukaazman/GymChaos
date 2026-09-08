import bpy,json
from pathlib import Path
root=Path(__file__).resolve().parents[1]
bpy.ops.wm.open_mainfile(filepath=str(root/'Assets/BodyBuilders/Player/player_rig.blend'))
m=max((o for o in bpy.context.scene.objects if o.type=='MESH'),key=lambda o:len(o.data.vertices))
zs=[(m.matrix_world@v.co).z for v in m.data.vertices];lo=min(zs);h=max(zs)-lo
m.data.calc_loop_triangles();uv=m.data.uv_layers.active.data
tri=[]
for t in m.data.loop_triangles:
 if all(lo+h*.47 < zs[v] < lo+h*.80 for v in t.vertices):
  tri.append([[*uv[l].uv] for l in t.loops])
(root/'.tools/shirt-uv.json').write_text(json.dumps(tri))
print('SHIRT_REGION',len(tri),lo,h)
