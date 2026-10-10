import bpy, sys, math, mathutils, json
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform
a=sys.argv[sys.argv.index('--')+1:]
src,views_json,outp=a[0],a[1],a[2]; res=1000; zoom=2.0; focus=[0.5,0.5,0.6]
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.fbx(filepath=src)
obs=[o for o in bpy.data.objects if o.type=='MESH']
P=[o.matrix_world@mathutils.Vector(c) for o in obs for c in o.bound_box]
mn=mathutils.Vector([min(p[i] for p in P) for i in range(3)]); mx=mathutils.Vector([max(p[i] for p in P) for i in range(3)])
size=max(mx-mn); ctr=mn+mathutils.Vector(focus)*(mx-mn); ortho=size*1.05/zoom
dg=bpy.context.evaluated_depsgraph_get(); ob=obs[0]; me=ob.evaluated_get(dg).to_mesh(); bvh=BVHTree.FromObject(ob,dg); inv=ob.matrix_world.inverted(); uvl=me.uv_layers.active.data
out=[]
for path,pts in json.load(open(views_json)):
    yaw=180.0 if '_back' in path else 90.0 if '_left' in path else 270.0 if '_right' in path else 0.0
    an=math.radians(yaw); pp=math.radians(5); d=mathutils.Vector((math.sin(an)*math.cos(pp),-math.cos(an)*math.cos(pp),math.sin(pp)))
    camloc=ctr+d*size*3; rot=(-d).to_track_quat('-Z','Y'); right=rot@mathutils.Vector((1,0,0)); up=rot@mathutils.Vector((0,1,0))
    dr=(inv.to_3x3()@(-d)).normalized()
    for px,py in pts:
        for sx in range(-3,4):
            for sy in range(-3,4):
                fx=px+0.5+sx/3.5; fy=py+0.5+sy/3.5
                o=inv@(camloc+right*(fx/res-0.5)*ortho+up*(0.5-fy/res)*ortho)
                loc,n,fi,dist=bvh.ray_cast(o,dr)
                if fi is None: continue
                poly=me.polygons[fi]; vs=[me.vertices[me.loops[l].vertex_index].co for l in poly.loop_indices]; uvs=[uvl[l].uv for l in poly.loop_indices]
                uv=barycentric_transform(loc,vs[0],vs[1],vs[2],uvs[0].to_3d(),uvs[1].to_3d(),uvs[2].to_3d())
                out.append([uv[0],1-uv[1]])
json.dump(out,open(outp,'w')); print('SUB',len(out))
