"""Player bottoms (Shorts, Pants): shell masks and the shared fabric texture.

    blender -b --factory-startup -P Tools/export_player_uv_positions.py
    python Tools/build_player_bottoms.py

Same atlas pipeline as build_player_tops.py (rest-pose position per texel):

- Shorts: athletic shorts over the scan's own shorts, waistband to just
  above the knee, a few mm off the skin.
- Pants: wide straight-leg track pants. PlayerTopShell lofts them from the
  body inside this mask (straight leg tubes, a seat yoke); the mask only
  marks the region to measure.

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
ANKLE = 0.055
# Straight leg: tube round a vertical axis per leg (x = +-LEG_AXIS_X,
# y = LEG_AXIS_Y), radius from HEM_RADIUS at the hem to THIGH_RADIUS at the
# crotch, always clear of the calf/knee (max 0.052) by TUBE_CLEARANCE.
LEG_AXIS_X = 0.088
LEG_AXIS_Y = 0.02
HEM_RADIUS = 0.062
THIGH_RADIUS = 0.077
CROTCH = 0.33
TUBE_CLEARANCE = 0.006
# Fabric base the runtime tint multiplies (PlayerTopShell.BottomsFabricLevel).
FABRIC_LEVEL = 0.8


def periodic_noise(size, sigma, seed):
    """Gaussian-blurred noise that tiles: blurred in the frequency domain."""
    rng = np.random.default_rng(seed)
    noise = rng.normal(0, 1, (size, size))
    fy = np.fft.fftfreq(size)[:, None]
    fx = np.fft.fftfreq(size)[None, :]
    kernel = np.exp(-2 * (np.pi * sigma) ** 2 * (fx ** 2 + fy ** 2))
    out = np.real(np.fft.ifft2(np.fft.fft2(noise) * kernel))
    return (out / max(out.std(), 1e-6)).astype(np.float32)


def write_pants_fabric(size=512):
    """Seamless cotton tricot tile for the lofted pants (PlayerTopShell
    FabricTile = one tile): fine vertical knit wales, a soft heather and a few
    slubs, around FABRIC_LEVEL so the runtime tint keeps the chosen colour."""
    x = np.arange(size)[None, :].repeat(size, 0)
    wales = np.sin(x * 2 * np.pi * 96 / size) * 0.5 + 0.5
    heather = periodic_noise(size, 6, 31)
    fine = periodic_noise(size, 0.8, 32)
    slubs = np.clip(periodic_noise(size, 2.5, 33) * np.array(periodic_noise(size, 18, 34) > 1.2, np.float32), -2, 2)
    shade = FABRIC_LEVEL * (1 + 0.035 * (wales - 0.5) + 0.02 * heather + 0.015 * fine + 0.02 * slubs)
    Image.fromarray(np.uint8(np.clip(np.repeat(shade[..., None], 3, 2), 0, 1) * 255)).save(OUT / 'pants_fabric.png')


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

    # Neutral track-pants tricot: smooth, faint heather, long straight fall,
    # a waistband and a plain hem a touch darker (the sheen is the material's).
    rng = np.random.default_rng(23)
    heather = cv2.GaussianBlur(rng.normal(0, 1, z.shape).astype(np.float32), (0, 0), 3.0)
    heather /= max(heather.std(), 1e-6)
    fine = cv2.GaussianBlur(rng.normal(0, 1, z.shape).astype(np.float32), (0, 0), 1.0)
    fine /= max(fine.std(), 1e-6)
    around = np.arctan2(y - LEG_AXIS_Y, ax - LEG_AXIS_X)
    # Tricot falls straight: faint long folds, and a soft break over the shoe.
    fall = 0.015 * np.sin(around * 5 + 0.4 * np.sin(z * 14)) * tops.smoothstep(CROTCH + 0.02, CROTCH - 0.04, z)
    fall += 0.035 * np.sin(z * 260 + around * 2) * tops.smoothstep(ANKLE + 0.07, ANKLE + 0.01, z)
    seat = 0.02 * np.sin(x * 70 + 2.0 * np.sin(z * 22)) * tops.smoothstep(CROTCH - 0.02, CROTCH + 0.04, z)
    folds = 1 + fall + seat
    waistband = 1 - 0.07 * tops.smoothstep(WAIST - 0.03, WAIST - 0.022, z)
    hem = 1 - 0.06 * tops.smoothstep(ANKLE + 0.012, ANKLE + 0.004, z)
    shade = np.clip(FABRIC_LEVEL * (1 + 0.012 * heather + 0.006 * fine) * folds * waistband * hem, 0, 1)

    painted = shorts_paint | pants_paint
    out = src.copy()
    out[painted] = np.repeat(shade[painted][:, None], 3, 1)
    # Gutters next to painted texels take the fabric too, so bilinear and
    # mipmaps never pull the scan's black shorts into a light colour.
    grown = cv2.dilate(painted.astype(np.uint8), np.ones((3, 3), np.uint8), iterations=6) > 0
    out[grown & ~valid] = FABRIC_LEVEL
    Image.fromarray(np.uint8(np.clip(out, 0, 1) * 255)).save(OUT / 'bottoms_fabric.png')

    write_pants_fabric()

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
    # Straight leg: push every leg texel out to its tube, so the silhouette
    # is two straight lines from hip to hem whatever the muscles do; above
    # the crotch a relaxed seat and a snug waistband.
    tube_radius = HEM_RADIUS + (THIGH_RADIUS - HEM_RADIUS) * tops.smoothstep(ANKLE, CROTCH, z)
    leg_distance = np.hypot(ax - LEG_AXIS_X, y - LEG_AXIS_Y)
    leg = np.clip(tube_radius - leg_distance, TUBE_CLEARANCE, INFLATE_RANGE * 0.98)
    hips = 0.004 + 0.012 * tops.smoothstep(0.0, 0.03, WAIST - z)
    blend = tops.smoothstep(CROTCH - 0.03, CROTCH + 0.04, z)
    pants_inflate = leg * (1 - blend) + hips * blend
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
