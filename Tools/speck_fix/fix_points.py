import sys, json, numpy as np
sys.path.insert(0,'C:/Users/Luka/Documents/GitHub/GymChaos/Tools')
import clean_scan_textures as c
from PIL import Image
npz,png,uvjson=sys.argv[1:4]
d=np.load(npz); keys=sorted(k[2:] for k in d.files if k.startswith('uv'))
uvt=np.concatenate([d['uv'+k] for k in keys]).astype(float); pts=np.concatenate([d['co'+k][d['tri'+k]] for k in keys]).astype(float)
uvt[:,:,1]=1-uvt[:,:,1]
rgb,alpha=c.split_alpha(Image.open(png)); h,w=rgb.shape[:2]
ids=c.rasterise(uvt,w,h); mask=ids>0; nb=c.face_neighbours(pts); col=c.face_colours(c.pad(rgb,mask),ids,uvt)
faces=set(); work=rgb.copy()
# faces within two rings of a light detail (badge, buckle, skin) are its border: never touched
light=col.mean(1)>110
near=light.copy()
for _ in range(2):
    near=near|(np.where(nb>=0,near[np.maximum(nb,0)],False)).any(1)
for u,v in json.load(open(uvjson)):
    x,y=int(u*w),int(v*h)
    for dy in (-1,0,1):
        for dx in (-1,0,1):
            yy,xx=min(max(y+dy,0),h-1),min(max(x+dx,0),w-1)
            if ids[yy,xx]>0: faces.add(int(ids[yy,xx])-1)
fixed=0
for f in faces:
    nn=nb[f][nb[f]>=0]
    if len(nn)==0: continue
    med=np.median(col[nn],0)
    # only lint inside dark cloth: every neighbour dark, so badge / gear /
    # skin borders (any light neighbour) are never touched
    if near[f] or col[nn].mean(1).max()>60: continue
    if (ids==f+1).sum()>40: continue
    if np.linalg.norm(col[f]-med)<12: 
        # face itself fine: the speck came from a texel at its edge; clean the probed texels
        pass
    sel=ids==f+1; work[sel]=med; fixed+=1
# gap texels stay as patched (re-padding would bring the neighbour colour back)
out=np.clip(np.rint(work),0,255).astype(np.uint8)
Image.fromarray(np.dstack([out,alpha]) if alpha is not None else out).save(png,optimize=True)
print('FIX faces',fixed,'of',len(faces))
