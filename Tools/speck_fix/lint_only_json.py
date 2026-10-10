import sys, numpy as np
from PIL import Image, ImageFilter
# A speck counts only if the whole 15x15 window around it, minus the speck,
# is dark cloth: no skin, badge, background or silhouette edge nearby.
import json
out=[]; outp=sys.argv[1]
for p in sys.argv[2:]:
    rgb=np.asarray(Image.open(p).convert('RGB')).astype(int); lum=rgb.mean(-1)
    bg=(np.abs(rgb-rgb[2,2]).sum(-1)<25)
    nonbody=np.asarray(Image.fromarray((bg*255).astype(np.uint8)).filter(ImageFilter.MaxFilter(21)))>0
    med=np.asarray(Image.fromarray(lum.astype(np.uint8)).filter(ImageFilter.MedianFilter(7))).astype(int)
    light=(lum>70).astype(np.uint8)*255
    # number of light pixels in a 15x15 window; a lone speck has very few
    cnt=np.asarray(Image.fromarray(light).filter(ImageFilter.BoxBlur(7))).astype(float)/255*225
    sp=(~nonbody)&(med<40)&(lum-med>20)&(cnt<12)
    ys,xs=np.nonzero(sp)
    out.append([p,[[int(x),int(y)] for x,y in zip(xs,ys)]])
json.dump(out,open(outp,'w')); print(sum(len(o[1]) for o in out))
