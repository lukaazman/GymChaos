"""Tileable cloth tiles for the lofted garments (no Blender needed).

    python Tools/build_player_fabrics.py

PlayerTopShell lofts the oversized tee and the sweatpants from its own rings
and gives them their own UVs at one tile = 12 cm, so the cloth is textured by
a repeating tile instead of the scan's painted atlas (whose islands have mixed
texel density and made every tiled detail vanish).

Every map is neutral grey at FABRIC_LEVEL so the runtime tint
(colour / FABRIC_LEVEL) keeps the chosen shirt colour, like the bottoms.

- jersey_heavy.png   heavyweight cotton jersey: fine wales, heather, slubs
- fleece_brushed.png brushed sweat fleece: soft mottle, short fibre noise
- rib_2x1.png        collar/cuff/waist rib
- track_knit.png     track-pant double knit: dense wales with a soft sheen
- rib_waistband.png  deeper, wider rib for the elastic waistband
"""

from pathlib import Path
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'GymChaos/Assets/Resources/Player/Outfits'
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


def save(name, shade):
    rgb = np.uint8(np.clip(np.repeat(shade[..., None], 3, 2), 0, 1) * 255)
    Image.fromarray(rgb).save(OUT / name)
    print('FABRIC_OK', name, rgb.shape[1], 'texels')


def jersey(size=1024):
    x = np.arange(size)[None, :].repeat(size, 0)
    y = np.arange(size)[:, None].repeat(size, 1)
    # Knit wales run along the garment (vertical in the tile).
    wales = np.sin(x * 2 * np.pi * 128 / size) * 0.5 + 0.5
    courses = np.sin(y * 2 * np.pi * 96 / size + 0.7 * wales) * 0.5 + 0.5
    heather = periodic_noise(size, 5.0, 41)
    fine = periodic_noise(size, 0.7, 42)
    slubs = np.clip(periodic_noise(size, 2.0, 43) * np.array(periodic_noise(size, 16, 44) > 1.15, np.float32), -2, 2)
    # A gentle vertical fall of light, so a flat colour still reads as cloth.
    fall = 0.012 * np.sin(x * 2 * np.pi * 2 / size)
    shade = FABRIC_LEVEL * (1 + 0.030 * (wales - 0.5) + 0.020 * (courses - 0.5)
                            + 0.022 * heather + 0.012 * fine + 0.020 * slubs + fall)
    save('jersey_heavy.png', shade)


def fleece(size=1024):
    x = np.arange(size)[None, :].repeat(size, 0)
    mottle = periodic_noise(size, 9.0, 51)
    fibre = periodic_noise(size, 1.1, 52)
    pebble = periodic_noise(size, 2.6, 53)
    # Brushed pile shows short random highlights, no knit wales.
    pile = np.clip(periodic_noise(size, 0.5, 54), -1.5, 1.5)
    fall = 0.010 * np.sin(x * 2 * np.pi * 3 / size)
    shade = FABRIC_LEVEL * (1 + 0.016 * mottle + 0.012 * fibre + 0.010 * pebble + 0.018 * pile + fall)
    save('fleece_brushed.png', shade)


def track_knit(size=1024):
    """Double knit of track pants: dense fine wales, tight courses and the
    faint satin sheen that catches light down a panel, on top of a soft
    heather. Brushed fleece read as pyjama fabric; this is thin and slick."""
    x = np.arange(size)[None, :].repeat(size, 0)
    y = np.arange(size)[:, None].repeat(size, 1)
    # Coarser than life: a 0.6 mm wale is one pixel at arm's length and mips
    # away to flat grey, so the tile carries 1.1 mm wales that still read.
    wales = np.sin(x * 2 * np.pi * 112 / size) * 0.5 + 0.5
    courses = np.sin(y * 2 * np.pi * 96 / size + 0.35 * wales) * 0.5 + 0.5
    # Interlock: every second wale runs a little brighter, which is what makes
    # the cloth shimmer as the leg turns instead of looking painted on.
    panel = np.sin(x * 2 * np.pi * 5 / size) * 0.5 + 0.5
    heather = periodic_noise(size, 6.0, 71)
    fine = periodic_noise(size, 0.8, 72)
    shade = FABRIC_LEVEL * (1 + 0.070 * (wales - 0.5) + 0.050 * (courses - 0.5)
                            + 0.055 * (panel - 0.5) + 0.014 * heather + 0.012 * fine)
    save('track_knit.png', shade)


def rib_waistband(size=512, ribs=40):
    """2x2 rib at waistband scale: fewer, deeper columns than the cuff rib, so
    the band reads as elastic rather than as another piece of the trouser."""
    x = np.arange(size)[None, :].repeat(size, 0)
    y = np.arange(size)[:, None].repeat(size, 1)
    phase = x * 2 * np.pi * ribs / size
    profile = 0.5 + 0.5 * np.cos(phase)
    stitch = np.sin(y * 2 * np.pi * 96 / size) * 0.5 + 0.5
    heather = periodic_noise(size, 3.5, 81)
    shade = FABRIC_LEVEL * (1 + 0.120 * (profile - 0.5) + 0.016 * (stitch - 0.5)
                            + 0.012 * heather + 0.012 * (profile - 0.5) * (stitch - 0.5))
    save('rib_waistband.png', shade)


def rib(size=512, ribs=64):
    x = np.arange(size)[None, :].repeat(size, 0)
    y = np.arange(size)[:, None].repeat(size, 1)
    phase = x * 2 * np.pi * ribs / size
    # 2x1 rib: two knit columns raised, one valley.
    profile = 0.5 + 0.5 * np.cos(phase)
    stitch = np.sin(y * 2 * np.pi * 128 / size) * 0.5 + 0.5
    heather = periodic_noise(size, 3.0, 61)
    shade = FABRIC_LEVEL * (1 + 0.075 * (profile - 0.5) + 0.012 * (stitch - 0.5)
                            + 0.012 * heather + 0.010 * (profile - 0.5) * (stitch - 0.5))
    save('rib_2x1.png', shade)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    jersey()
    fleece()
    rib()
    track_knit()
    rib_waistband()
    print('FABRICS_OK level=%.2f' % FABRIC_LEVEL)


if __name__ == '__main__':
    main()
