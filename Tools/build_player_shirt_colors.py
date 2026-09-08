import sys,json
sys.path.insert(0,'.tools/video-deps')
import cv2,numpy as np
from PIL import Image
from pathlib import Path
p=Path('GymChaos/Assets/Resources/Characters/Textures/player_authored.png');im=np.array(Image.open(p).convert('RGB'));h,w=im.shape[:2];mask=np.zeros((h,w),np.uint8)
for tri in json.loads(Path('.tools/shirt-uv.json').read_text()):
 pts=np.array([[round(u*(w-1)),round((1-v)*(h-1))] for u,v in tri],np.int32);cv2.fillConvexPoly(mask,pts,255)
f=im.astype(np.float32)/255.;cloth=(f[:,:,2]>.85*f[:,:,0])&(f.max(2)<.52);mask=(mask>0)&cloth
out=Path('GymChaos/Assets/Resources/Player/Outfits');out.mkdir(exist_ok=True)
for name,color in dict(White=[.9,.92,.9],Red=[.72,.045,.025],Blue=[.035,.22,.78],Gold=[.92,.58,.08]).items():
 result=f.copy();shade=np.clip(.55+f.mean(2)*2.3,.55,1.05);result[mask]=(shade[:,:,None]*np.array(color))[mask];Image.fromarray(np.uint8(np.clip(result,0,1)*255)).save(out/('shirt_'+name.lower()+'.png'))
Image.fromarray(np.uint8(mask)*255).save('.tools/shirt-mask.png');print('RECOLORED_PIXELS',mask.sum(), 'total',w*h)
