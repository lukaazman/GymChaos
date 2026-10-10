import sys, json, numpy as np
sys.path.insert(0,'C:/Users/Luka/Documents/GitHub/GymChaos/Tools'); import clean_scan_textures as c
from PIL import Image
npz,png,uvjson=sys.argv[1:4]
d=np.load(npz); keys=sorted(k[2:] for k in d.files if k.startswith('uv'))
uvt=np.concatenate([d['uv'+k] for k in keys]).astype(float); uvt[:,:,1]=1-uvt[:,:,1]
rgb,alpha=c.split_alpha(Image.open(png)); h,w=rgb.shape[:2]; ids=c.rasterise(uvt,w,h)
work=rgb.copy(); n=0
for u,v in json.load(open(uvjson)):
    x,y=min(int(u*w),w-1),min(int(v*h),h-1)
    for yy in range(y-1,y+2):
        for xx in range(x-1,x+2):
            if not(0<=yy<h and 0<=xx<w) or work[yy,xx].mean()<45: continue
            win=work[max(yy-3,0):yy+4,max(xx-3,0):xx+4].reshape(-1,3)
            dark=win[win.mean(1)<45]
            # only where the light texel is a thin edge next to dark cloth
            if len(dark)<len(win)*0.5: continue
            work[yy,xx]=np.median(dark,0); n+=1
out=np.clip(np.rint(work),0,255).astype(np.uint8)
Image.fromarray(np.dstack([out,alpha]) if alpha is not None else out).save(png,optimize=True)
print('EDGEFIX texels',n)
