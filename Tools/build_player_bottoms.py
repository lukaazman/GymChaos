"""Player bottoms (Shorts, Pants): shell masks and the shared fabric texture.

    blender -b --factory-startup -P Tools/export_player_uv_positions.py
    python Tools/build_player_bottoms.py

Same atlas pipeline as build_player_tops.py (rest-pose position per texel):

- Shorts: athletic shorts over the scan's own shorts, waistband to just
  above the knee, a few mm off the skin.
- Pants: baggy sweatpants from the waistband to the ankle, loose over the
  thigh and knee and gathered into a ribbed cuff.

bottoms_fabric.png is neutral light-grey cotton for both; PlayerTopShell
tints it with the chosen bottoms colour at runtime, so one texture serves
every colour. shorts_shell.bytes / pants_shell.bytes use the tops' mask
layout (R coverage, G inflation, B painted, A signed edge distance).
"""
import sys
sys.path.insert(0, '.tools/video-deps')
from pathlib import Path
import json
import numpy as np
import cv2
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_player_tops as tops  # noqa: E402

ROOT = tops.ROOT
OUT = tops.OUT
DEBUG = ROOT / '.tools/bottoms'
INFLATE_RANGE = tops.INFLATE_RANGE
EDGE_RANGE = tops.EDGE_RANGE

# Rest-mesh height fractions (the scan's shorts run 0.335-0.52).
WAIST = 0.522
SHORTS_HEM = 0.33
ANKLE = 0.062
CUFF = 0.035
# Fabric base the runtime tint multiplies (PlayerTopShell.BottomsFabricLevel).
FABRIC_LEVEL = 0.8


def main():
    data = np.load(ROOT / '.tools/player-uvpos.npz')
    uv, co, nrm = data['uv'], data['co'], data['nrm']
    src = np.array(Image.open(tops.SRC).convert('RGB'), np.float32) / 255.0
    size = src.shape[0]
    ids, pts = tops.rasterize(uv, size)
    valid = ids > 0
    P, N = tops.interpolate(ids, pts, co, nrm)
    x, y, z = P[..., 0], P[..., 1], P[..., 2]
    ax = np.abs(x)
    # T-pose: arms are level at shoulder height, so below the waist only
    # the hips and legs are body; the hands never reach this band.
    legs = valid & (ax < 0.2)

    shorts_edge = np.where(legs, np.minimum(WAIST - z, z - SHORTS_HEM), -1.0)
    pants_edge = np.where(legs, np.minimum(WAIST - z, z - ANKLE), -1.0)
    shorts_cover = shorts_edge > -0.012
    pants_cover = pants_edge > -0.012
    shorts_paint = shorts_edge >= -0.02
    pants_paint = pants_edge >= -0.02

    # Neutral cotton: soft heather, vertical drape folds on the pants, knee
    # creases, a waistband and ribbed cuffs a touch darker.
    rng = np.random.default_rng(23)
    heather = cv2.GaussianBlur(rng.normal(0, 1, z.shape).astype(np.float32), (0, 0), 3.0)
    heather /= max(heather.std(), 1e-6)
    fine = cv2.GaussianBlur(rng.normal(0, 1, z.shape).astype(np.float32), (0, 0), 1.0)
    fine /= max(fine.std(), 1e-6)
    folds = 1 + 0.03 * np.sin(x * 120 + 3.0 * np.sin(z * 26)) + 0.02 * np.sin((x + y) * 75 - z * 14)
    knee = 0.04 * tops.smoothstep(0.03, 0.0, np.abs(z - 0.27)) * np.sin(z * 900 + x * 40)
    waistband = 1 - 0.07 * tops.smoothstep(WAIST - 0.03, WAIST - 0.022, z)
    rib = np.where(z < ANKLE + CUFF, 1 - 0.05 * (0.5 + 0.5 * np.sin(x * 1800 + y * 1800)), 1.0)
    cuff_band = 1 - 0.06 * tops.smoothstep(ANKLE + CUFF - 0.004, ANKLE + CUFF + 0.004, z) * (z < ANKLE + CUFF + 0.01)
    shade = FABRIC_LEVEL * (1 + 0.02 * heather + 0.015 * fine) * folds * (1 + knee) * waistband * rib
    shade = np.clip(shade * np.where(z < ANKLE + CUFF + 0.01, cuff_band, 1.0), 0, 1)

    painted = shorts_paint | pants_paint
    out = src.copy()
    out[painted] = np.repeat(shade[painted][:, None], 3, 1)
    # Gutters next to painted texels take the fabric too, so bilinear and
    # mipmaps never pull the scan's black shorts into a light colour.
    grown = cv2.dilate(painted.astype(np.uint8), np.ones((3, 3), np.uint8), iterations=6) > 0
    out[grown & ~valid] = FABRIC_LEVEL
    Image.fromarray(np.uint8(np.clip(out, 0, 1) * 255)).save(OUT / 'bottoms_fabric.png')

    def write_mask(name, cover, amount, paint, edge):
        shell = np.zeros((size, size, 4), np.uint8)
        shell[..., 0] = np.where(cover, 255, 0)
        shell[..., 1] = np.uint8(np.clip(amount / INFLATE_RANGE, 0, 1) * 255) * cover
        # Erode only along the garment's real edges: thin UV islands inside
        # it (the scan's old shorts hem lip) would otherwise lose their
        # paint and be cut out as hem strips, leaving slits in the cloth.
        eroded = cv2.erode(paint.astype(np.uint8), np.ones((3, 3), np.uint8)) > 0
        shell[..., 2] = np.where(paint & (eroded | (edge > 0.015)), 255, 0)
        shell[..., 3] = np.uint8(np.clip(128 + edge / EDGE_RANGE * 127, 0, 255))
        ok, png = cv2.imencode('.png', shell[..., [2, 1, 0, 3]])
        (OUT / name).write_bytes(png.tobytes())

    # Shorts: close at the waistband and hem, a little loose over the thigh.
    shorts_inflate = 0.002 + 0.006 * tops.smoothstep(0.0, 0.03, shorts_edge)
    write_mask('shorts_shell.bytes', shorts_cover, shorts_inflate, shorts_paint, shorts_edge)
    # Sweatpants: snug waist, baggy thigh to shin, gathered into the cuff.
    leg_room = 0.013 + 0.008 * tops.smoothstep(0.46, 0.22, z)
    cuff_taper = tops.smoothstep(ANKLE + 0.005, ANKLE + CUFF + 0.03, z)
    pants_inflate = 0.003 + (leg_room - 0.003) * tops.smoothstep(0.0, 0.025, WAIST - z) * cuff_taper
    pants_inflate = np.maximum(pants_inflate, 0.003)
    write_mask('pants_shell.bytes', pants_cover, pants_inflate, pants_paint, pants_edge)

    DEBUG.mkdir(parents=True, exist_ok=True)
    Image.fromarray(np.uint8(shorts_paint) * 255).save(DEBUG / 'shorts-mask.png')
    Image.fromarray(np.uint8(pants_paint) * 255).save(DEBUG / 'pants-mask.png')
    report = {'shortsTexels': int(shorts_paint.sum()), 'pantsTexels': int(pants_paint.sum()),
              'maxPantsInflate': float(pants_inflate[pants_cover].max())}
    assert report['pantsTexels'] > report['shortsTexels'] > 10000, report
    (DEBUG / 'report.json').write_text(json.dumps(report, indent=1))
    print('BOTTOMS_OK', json.dumps(report))


if __name__ == '__main__':
    main()
