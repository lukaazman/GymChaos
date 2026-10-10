import bpy, sys, math, mathutils, json
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform
a=sys.argv[sys.argv.index('--')+1:]
src,views_json,res,zoom,focus,outp=a[0],a[1],int(a[2]),float(a[3]),[float(v) for v in a[4].split(',')],a[5]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
obs=[o for o in bpy.data.objects if o.type=='MESH']
P=[o.matrix_world@mathutils.Vector(c) for o in obs for c in o.bound_box]
mn=mathutils.Vector([min(p[i] for p in P) for i in range(3)]); mx=mathutils.Vector([max(p[i] for p in P) for i in range(3)])
size=max(mx-mn); ctr=mn+mathutils.Vector(focus)*(mx-mn); ortho=size*1.05/zoom
dg=bpy.context.evaluated_depsgraph_get(); ob=obs[0]; me=ob.evaluated_get(dg).to_mesh(); bvh=BVHTree.FromObject(ob,dg); inv=ob.matrix_world.inverted(); uvl=me.uv_layers.active.data
res_uv=[]
for path,pts in json.load(open(views_json)):
    yaw=180.0 if 'back' in path else 0.0; pitch=5.0
    an=math.radians(yaw); pp=math.radians(pitch); d=mathutils.Vector((math.sin(an)*math.cos(pp),-math.cos(an)*math.cos(pp),math.sin(pp)))
    camloc=ctr+d*size*3; rot=(-d).to_track_quat('-Z','Y'); right=rot@mathutils.Vector((1,0,0)); up=rot@mathutils.Vector((0,1,0))
    for px,py in pts:
        o=camloc+right*((px+0.5)/res-0.5)*ortho+up*(0.5-(py+0.5)/res)*ortho
        loc,n,fi,dist=bvh.ray_cast(inv@o,(inv.to_3x3()@(-d)).normalized())
        if fi is None: continue
        poly=me.polygons[fi]; vs=[me.vertices[me.loops[l].vertex_index].co for l in poly.loop_indices]; uvs=[uvl[l].uv for l in poly.loop_indices]
        uv=barycentric_transform(loc,vs[0],vs[1],vs[2],uvs[0].to_3d(),uvs[1].to_3d(),uvs[2].to_3d())
        res_uv.append([uv[0],1-uv[1]])
json.dump(res_uv,open(outp,'w')); print('UVS',len(res_uv))
