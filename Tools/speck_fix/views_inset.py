import bpy, sys, math, mathutils, os
a=sys.argv[sys.argv.index('--')+1:]
src,out=a[0],a[1]
views=a[2] if len(a)>2 else "front:0:0,left:90:0,back:180:0,right:270:0"
zoom=float(a[3]) if len(a)>3 else 1.0
focus=[float(v) for v in a[4].split(',')] if len(a)>4 else None
bpy.ops.wm.read_factory_settings(use_empty=True)
if src.lower().endswith('.fbx'): bpy.ops.import_scene.fbx(filepath=src)
else: bpy.ops.import_scene.gltf(filepath=src)
obs=[o for o in bpy.data.objects if o.type=='MESH']
if os.environ.get('UV_INSET'):
    import numpy as np
    tex_px=int(os.environ.get('UV_INSET'))
    for o in obs:
        m=o.data
        if not m.uv_layers: continue
        nl=len(m.loops); uv=np.empty(nl*2,np.float32); m.uv_layers.active.data.foreach_get('uv',uv); uv=uv.reshape(-1,2)
        lv=np.empty(nl,np.int32); m.loops.foreach_get('vertex_index',lv)
        # Unity splits vertices per (position, uv); emulate: key = vertex + uv
        key=np.round(np.c_[lv, uv*1e6]).astype(np.int64); _,uid=np.unique(key,axis=0,return_inverse=True); uid=uid.ravel()
        m.calc_loop_triangles(); tl=np.empty(len(m.loop_triangles)*3,np.int32); m.loop_triangles.foreach_get('loops',tl); tl=tl.reshape(-1,3)
        t=uid[tl]; nu=uid.max()+1; U=np.zeros((nu,2)); U[uid]=uv
        e=np.sort(np.concatenate([t[:,[0,1]],t[:,[1,2]],t[:,[2,0]]]),1); u,c=np.unique(e,axis=0,return_counts=True)
        border=np.zeros(nu,bool); border[u[c==1].ravel()]=True
        cen=U[t].mean(1); pull=np.zeros((nu,2)); hits=np.zeros(nu)
        for k in range(3): np.add.at(pull,t[:,k],cen-U[t[:,k]]); np.add.at(hits,t[:,k],1)
        pull/=np.maximum(hits,1)[:,None]; L=np.linalg.norm(pull,axis=1); step=np.minimum(float(os.environ.get('INSET_TEXELS','1.0'))/tex_px,L*0.3)
        mv=np.where(L[:,None]>1e-12,pull/np.maximum(L,1e-12)[:,None]*step[:,None],0)
        U2=U.copy(); U2[border]+=mv[border]
        m.uv_layers.active.data.foreach_set('uv',U2[uid].astype(np.float32).ravel())
        print('INSET',o.name,int(border.sum()))
        if os.environ.get('SLIVER_FIX'):
            img=bpy.data.images.load(os.environ['TEX_OVERRIDE']); W,H=img.size
            pxa=np.empty(W*H*4,np.float32); img.pixels.foreach_get(pxa); pxa=pxa.reshape(H,W,4)[...,:3]*255
            co=np.empty(len(m.vertices)*3,np.float32); m.vertices.foreach_get('co',co); co=co.reshape(-1,3)
            tv=lv[tl]
            q=np.round(co/ (np.ptp(co,0).max()*1e-6)).astype(np.int64); _,wid=np.unique(q,axis=0,return_inverse=True); wid=wid.ravel()
            wt=wid[tv]; nt=len(wt)
            e=np.concatenate([wt[:,[0,1]],wt[:,[1,2]],wt[:,[2,0]]]); e=np.sort(e,1); tri=np.tile(np.arange(nt),3)
            key=e[:,0].astype(np.int64)*(1<<32)+e[:,1]; o=np.argsort(key,kind='stable'); key=key[o]; tri=tri[o]
            same=np.nonzero(key[1:]==key[:-1])[0]
            a_=tri[same]; b_=tri[same+1]
            U3=U2[uid][tl]; cen=U3.mean(1)
            xs=np.clip((cen[:,0]*W).astype(int),0,W-1); ys=np.clip((cen[:,1]*H).astype(int),0,H-1)
            lum=pxa[ys,xs].mean(1)
            nbmax=np.full(nt,-1.0); nbmin=np.full(nt,1e9); nbarg=np.full(nt,-1); deg=np.zeros(nt,int); nlight=np.zeros(nt,int)
            for x_,y_ in ((a_,b_),(b_,a_)):
                np.maximum.at(nbmax,x_,lum[y_]); np.add.at(deg,x_,1); np.add.at(nlight,x_,(lum[y_]>=50).astype(int))
                for i in range(len(x_)):
                    if lum[y_[i]]<nbmin[x_[i]]: nbmin[x_[i]]=lum[y_[i]]; nbarg[x_[i]]=y_[i]
            LT=float(os.environ.get('SLIVER_LUM','60')); DT=float(os.environ.get('SLIVER_DIFF','40')); uvarea=np.abs((U3[:,1,0]-U3[:,0,0])*(U3[:,2,1]-U3[:,0,1])-(U3[:,2,0]-U3[:,0,0])*(U3[:,1,1]-U3[:,0,1]))*0.5*W*H
            base=(deg>=2)&(nlight<=1)&(deg-nlight>=2)
            sel=base&(((lum>=60)&(lum-nbmin>=40))|((uvarea<0.5)&(lum>=40)&(lum-nbmin>=22)))
            loops=tl[sel]
            uvflat=U2[uid].copy()
            for t_,lp in zip(np.nonzero(sel)[0],loops):
                uvflat[lp]=cen[nbarg[t_]]
            m.uv_layers.active.data.foreach_set('uv',uvflat.astype(np.float32).ravel())
            print('SLIVER',int(sel.sum()))
pts=[o.matrix_world@mathutils.Vector(c) for o in obs for c in o.bound_box]
mn=mathutils.Vector([min(p[i] for p in pts) for i in range(3)]); mx=mathutils.Vector([max(p[i] for p in pts) for i in range(3)])
ctr=(mn+mx)/2; size=max(mx-mn)
if focus: ctr=mn+mathutils.Vector(focus)*(mx-mn)
print("BOUNDS",tuple(mn),tuple(mx))
sc=bpy.context.scene; sc.render.engine='BLENDER_EEVEE_NEXT'; res=int(os.environ.get('RES','640')); sc.render.resolution_x=res; sc.render.resolution_y=res
sc.view_settings.view_transform='Standard'
w=bpy.data.worlds.new("w"); sc.world=w; w.use_nodes=True; w.node_tree.nodes['Background'].inputs[0].default_value=(0.35,0.4,0.35,1); w.node_tree.nodes['Background'].inputs[1].default_value=1.0
import os
tex=os.environ.get('TEX_OVERRIDE')
for m in bpy.data.materials:
    m.use_backface_culling=False
    if tex and m.node_tree:
        img=bpy.data.images.load(tex)
        nt=m.node_tree
        bs=[n for n in nt.nodes if n.type=='BSDF_PRINCIPLED']
        ti=[n for n in nt.nodes if n.type=='TEX_IMAGE']
        t=ti[0] if ti else nt.nodes.new('ShaderNodeTexImage')
        t.image=img
        if bs and not bs[0].inputs['Base Color'].is_linked: nt.links.new(t.outputs['Color'],bs[0].inputs['Base Color'])
        if bs and not ti: nt.links.new(t.outputs['Color'],bs[0].inputs['Base Color'])
    # unlit-like look as in game: emission from base color
    if m.node_tree:
        nt=m.node_tree; bs=[n for n in nt.nodes if n.type=='BSDF_PRINCIPLED']
        if bs and bs[0].inputs['Base Color'].is_linked:
            src_sock=bs[0].inputs['Base Color'].links[0].from_socket
            em=nt.nodes.new('ShaderNodeEmission'); nt.links.new(src_sock,em.inputs['Color'])
            mo=[n for n in nt.nodes if n.type=="OUTPUT_MATERIAL"][0]; nt.links.new(em.outputs[0],mo.inputs['Surface'])
mips=int(os.environ.get('MIPSIM','1'))
if mips>1:
    for im in bpy.data.images:
        if im.size[0]>8: im.scale(max(1,im.size[0]//mips),max(1,im.size[1]//mips))
cam=bpy.data.objects.new("cam",bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera=cam
cam.data.type='ORTHO'; cam.data.ortho_scale=size*1.05/zoom; cam.data.clip_end=1000
for v in views.split(','):
    name,yaw,pitch=v.split(':'); a_=math.radians(float(yaw)); p=math.radians(float(pitch))
    d=mathutils.Vector((math.sin(a_)*math.cos(p),-math.cos(a_)*math.cos(p),math.sin(p)))
    cam.location=ctr+d*size*3
    cam.rotation_euler=(-d).to_track_quat('-Z','Y').to_euler()
    sc.render.filepath=f"{out}_{name}.png"; bpy.ops.render.render(write_still=True)
