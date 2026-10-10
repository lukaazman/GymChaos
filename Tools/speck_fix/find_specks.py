import sys, numpy as np, json
from PIL import Image, ImageFilter
out=[]
for p in sys.argv[2:]:
    rgb=np.asarray(Image.open(p).convert('RGB')).astype(int); bg=(np.abs(rgb-rgb[2,2]).sum(-1)<25)
    body=~(np.asarray(Image.fromarray((bg*255).astype(np.uint8)).filter(ImageFilter.MaxFilter(15)))>0)
    med=np.stack([np.asarray(Image.fromarray(rgb[...,c].astype(np.uint8)).filter(ImageFilter.MedianFilter(7))) for c in range(3)],-1).astype(int)
    lum=rgb.mean(-1); mlum=med.mean(-1)
    sp=body&(mlum<40)&((lum-mlum>20)|(np.linalg.norm(rgb-med,axis=-1)>40))
    ys,xs=np.nonzero(sp); out.append([p,[[int(x),int(y)] for x,y in zip(xs,ys)]])
json.dump(out,open(sys.argv[1],'w'))
print(sum(len(o[1]) for o in out))
