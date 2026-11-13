"""Shirt colour textures for the player from .tools/shirt-uv.json.

    python Tools/build_player_shirt_colors.py

Every shirt triangle is painted whole (grown 1 px over UV seams), so no
pixel of the default black survives on the shirt; folds keep their shading
from the original luminance. Pixels outside the shirt are untouched.
"""
import sys, json
sys.path.insert(0, '.tools/video-deps')
import cv2, numpy as np
from PIL import Image
from pathlib import Path

p = Path('GymChaos/Assets/Resources/Characters/Textures/player_authored.png')
im = np.array(Image.open(p).convert('RGB')); h, w = im.shape[:2]
mask = np.zeros((h, w), np.uint8)
for tri in json.loads(Path('.tools/shirt-uv.json').read_text()):
    pts = np.array([[round(u * (w - 1)), round((1 - v) * (h - 1))] for u, v in tri], np.int32)
    cv2.fillConvexPoly(mask, pts, 255)
# One texel past the islands, as before.
mask = cv2.dilate(mask, np.ones((3, 3), np.uint8)) > 0
f = im.astype(np.float32) / 255.
lum = f.mean(2)
# The tank's trim and seam lines sit a texel or two outside its islands and
# read as bitten edges. Closing the mask fills those gaps without growing it
# out over the body, and the filled texels must be dark cloth as well: a blind
# grow painted the arms, the neck and the face with the shirt colour.
filled = cv2.morphologyEx(mask.astype(np.uint8) * 255, cv2.MORPH_CLOSE, np.ones((9, 9), np.uint8)) > 127
mask = mask | (filled & (lum < 0.12))
print('PAINT_MASK', int(mask.sum()), 'filled_texels', int((filled & ~mask).sum()))
# The UV set also carries a couple of hundred sliver islands of a few texels
# each, which map body triangles onto the shirt's area: painted, they show as
# specks of shirt colour on the arms and the face. Only real garment islands
# stay.
componentCount, labels, stats, _ = cv2.connectedComponentsWithStats(mask.astype(np.uint8), 8)
keepMask = np.zeros_like(mask)
slivers = 0
for i in range(1, componentCount):
    if stats[i, cv2.CC_STAT_AREA] < 64:
        slivers += 1
        continue
    keepMask |= labels == i
mask = keepMask
print('PAINT_DROPPED_SLIVERS', slivers, 'islands', componentCount - 1 - slivers)
# Cloth sits around 0.02-0.08: map it to a gentle 0.62-1.05 shading range.
shade = np.clip(0.62 + np.clip(lum / 0.08, 0, 1) * 0.43, 0.62, 1.05)
shade = cv2.GaussianBlur(shade.astype(np.float32), (3, 3), 0)
out = Path('GymChaos/Assets/Resources/Player/Outfits'); out.mkdir(exist_ok=True)
for name, color in dict(White=[.9, .92, .9], Red=[.72, .045, .025], Blue=[.035, .22, .78], Gold=[.92, .58, .08]).items():
    result = f.copy()
    result[mask] = (shade[:, :, None] * np.array(color))[mask]
    Image.fromarray(np.uint8(np.clip(result, 0, 1) * 255)).save(out / ('shirt_' + name.lower() + '.png'))
Image.fromarray(np.uint8(mask) * 255).save('.tools/shirt-mask.png')
# Skin next to a seam must not take the shirt colour: this is what the grown
# ring would look like unguarded, and it has to stay near zero.
print('PAINT_SKIN_LEAK', int(((lum > 0.15) & mask).sum()))
print('RECOLORED_PIXELS', int(mask.sum()), 'total', w * h)
