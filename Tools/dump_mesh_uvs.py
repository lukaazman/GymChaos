"""Dump world-space triangles and UVs of every mesh to .npz for clean_scan_textures.py.

blender -b --factory-startup -P Tools/dump_mesh_uvs.py -- <model.fbx|glb> <out.npz>
"""
import bpy, sys, numpy as np
a=sys.argv[sys.argv.index('--')+1:]
src,out=a[0],a[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
if src.lower().endswith('.fbx'): bpy.ops.import_scene.fbx(filepath=src)
else: bpy.ops.import_scene.gltf(filepath=src)
data={}
k=0
for o in bpy.data.objects:
    if o.type!='MESH': continue
    m=o.data
    if not m.uv_layers: print("NOUV",o.name); continue
    m.calc_loop_triangles()
    nt=len(m.loop_triangles)
    tl=np.empty(nt*3,np.int32); m.loop_triangles.foreach_get('loops',tl)
    tv=np.empty(nt*3,np.int32); m.loop_triangles.foreach_get('vertices',tv)
    tm=np.empty(nt,np.int32); m.loop_triangles.foreach_get('material_index',tm)
    uv=np.empty(len(m.loops)*2,np.float32); m.uv_layers.active.data.foreach_get('uv',uv); uv=uv.reshape(-1,2)
    co=np.empty(len(m.vertices)*3,np.float32); m.vertices.foreach_get('co',co); co=co.reshape(-1,3)
    M=np.array(o.matrix_world); co=(M[:3,:3]@co.T).T+M[:3,3]  # world space, Z up
    mats=[]
    for s in o.material_slots:
        img=None
        if s.material and s.material.node_tree:
            for n in s.material.node_tree.nodes:
                if n.type=='TEX_IMAGE' and n.image: img=n.image.name+"|"+(n.image.filepath or "packed")+"|"+str(tuple(n.image.size))
        mats.append(f"{s.material.name if s.material else None}::{img}")
    print("MESH",o.name,"tris",nt,"verts",len(co),"mats",mats)
    data[f"tri{k}"]=tv.reshape(-1,3); data[f"uv{k}"]=uv[tl].reshape(-1,3,2); data[f"co{k}"]=co; data[f"mat{k}"]=tm
    k+=1
np.savez_compressed(out,**data)
