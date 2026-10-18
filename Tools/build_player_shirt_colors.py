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
mask = cv2.dilate(mask, np.ones((3, 3), np.uint8)) > 0
f = im.astype(np.float32) / 255.
lum = f.mean(2)
# Cloth sits around 0.02-0.08: map it to a gentle 0.62-1.05 shading range.
shade = np.clip(0.62 + np.clip(lum / 0.08, 0, 1) * 0.43, 0.62, 1.05)
shade = cv2.GaussianBlur(shade.astype(np.float32), (3, 3), 0)
out = Path('GymChaos/Assets/Resources/Player/Outfits'); out.mkdir(exist_ok=True)
for name, color in dict(White=[.9, .92, .9], Red=[.72, .045, .025], Blue=[.035, .22, .78], Gold=[.92, .58, .08]).items():
    result = f.copy()
    result[mask] = (shade[:, :, None] * np.array(color))[mask]
    Image.fromarray(np.uint8(np.clip(result, 0, 1) * 255)).save(out / ('shirt_' + name.lower() + '.png'))
Image.fromarray(np.uint8(mask) * 255).save('.tools/shirt-mask.png')
print('RECOLORED_PIXELS', int(mask.sum()), 'total', w * h)
