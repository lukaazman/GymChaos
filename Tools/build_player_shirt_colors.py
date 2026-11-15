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

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_player_tops as tops  # noqa: E402

# Torso band of the rest mesh (hem 0.528, straps end near 0.81).
TANK_BOTTOM, TANK_TOP, TANK_HALF_WIDTH = 0.515, 0.815, 0.135
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
# Only the tank top itself takes the colour. The atlas scatters islands of
# the legs, arms and head between the shirt's, and the closing above grew
# patches of shirt colour onto them (white and red showed it most). A texel
# is shirt when the rest mesh puts it on the torso between the hem and the
# strap tops; gutter texels beside such a texel take the colour too so
# bilinear filtering and mips never pull the black back in.
data = np.load('.tools/player-uvpos.npz')
ids, pts = tops.rasterize(data['uv'], w)
valid = ids > 0
P, _ = tops.interpolate(ids, pts, data['co'], data['nrm'])
region = valid & (P[..., 2] > TANK_BOTTOM) & (P[..., 2] < TANK_TOP) & (np.abs(P[..., 0]) < TANK_HALF_WIDTH)
# The scan cuts the tank top into many small charts; dropping charts by size
# left whole triangles black on the back and sides. Dark, neutral texels on the
# torso band are the tank's black cloth (skin and its shadows are brown). The
# paint is decided per mesh triangle, not per texel: a texel rule put flakes of
# colour on skin triangles along the hem and armholes. A triangle is cloth when
# most of its texels are; cloth triangles are painted whole, skin ones never.
neutral = (f.max(2) - f.min(2)) < 0.05
candidate = region & (((lum < 0.15) & neutral) | mask)
triangles = int(ids.max()) + 1
total = np.bincount(ids[valid], minlength=triangles)
hits = np.bincount(ids[candidate], minlength=triangles)
cloth_triangle = (total > 0) & (hits * 2 >= total)
print('PAINT_OFF_TORSO', int((mask & valid & ~region).sum()), 'cloth_triangles', int(cloth_triangle.sum()))
mask = valid & cloth_triangle[ids] & ~((lum > 0.15) & ~neutral)
# The black trim along the hem and armholes can sit on the skin side of an edge
# triangle: grow two texels into dark, neutral cloth touching the paint, so the
# edge ends clean without a black line, and never as loose flakes on the skin.
for _ in range(2):
    mask = mask | ((cv2.dilate(mask.astype(np.uint8), np.ones((3, 3), np.uint8)) > 0) & valid & (lum < 0.15) & neutral)
# Black left between cloth triangles shows as dots on the light colours: UV
# cracks no triangle owns, yet filtering samples them. Fill the enclosed ones.
def enclosed(m):
    n, lab, st, _ = cv2.connectedComponentsWithStats((~m).astype(np.uint8), 4)
    edge = set(np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]])).tolist())
    return np.isin(lab, [i for i in range(1, n) if i not in edge]) & ~m
mask = mask | (enclosed(mask) & ~valid)
# Unused texels take the colour of the nearest painted one, so mips and
# bilinear filtering never pull the black gutter in. Only where the shirt is
# the nearest island: a neighbour's gutter keeps its own colour.
PAD = 4
cloth = mask & valid
dist_shirt, nearest = cv2.distanceTransformWithLabels((~cloth).astype(np.uint8), cv2.DIST_L2, 3, labelType=cv2.DIST_LABEL_PIXEL)
dist_other = cv2.distanceTransform((~(valid & ~mask)).astype(np.uint8), cv2.DIST_L2, 3)
pad = ~valid & (mask | ((dist_shirt <= PAD) & (dist_shirt < dist_other)))
src_y, src_x = np.nonzero(cloth)
from_y, from_x = src_y[nearest - 1], src_x[nearest - 1]
print('PAINT_PAD', int(pad.sum()))
# Cloth sits around 0.02-0.08: map it to a gentle 0.62-1.05 shading range.
shade = np.clip(0.62 + np.clip(lum / 0.08, 0, 1) * 0.43, 0.62, 1.05)
shade = cv2.GaussianBlur(shade.astype(np.float32), (3, 3), 0)
# The shorts' waistband (build_player_bottoms.WAIST) overlaps the hem: a soft
# contact shadow on the last 2 cm of the top separates the two garments.
WAIST = 0.545
shade = shade * np.where(valid, 0.8 + 0.2 * tops.smoothstep(WAIST, WAIST + 0.02, P[..., 2]), 1.0)
out = Path('GymChaos/Assets/Resources/Player/Outfits'); out.mkdir(exist_ok=True)
for name, color in dict(White=[.9, .92, .9], Red=[.72, .045, .025], Blue=[.035, .22, .78], Gold=[.92, .58, .08]).items():
    result = f.copy()
    result[mask] = (shade[:, :, None] * np.array(color))[mask]
    result[pad] = result[from_y, from_x][pad]
    Image.fromarray(np.uint8(np.clip(result, 0, 1) * 255)).save(out / ('shirt_' + name.lower() + '.png'))
Image.fromarray(np.uint8(mask) * 255).save('.tools/shirt-mask.png')
# Skin must not take the shirt colour: painted texels that are clearly skin
# (bright and brown) have to stay near zero.
print('PAINT_SKIN_LEAK', int(((lum > 0.15) & ~neutral & mask).sum()))
print('RECOLORED_PIXELS', int((mask | pad).sum()), 'total', w * h)
holes = int((enclosed(mask) & ~valid & (lum < 0.15)).sum())
print('PAINT_DARK_HOLES', holes)
assert holes == 0, 'dark holes left inside the shirt paint'
