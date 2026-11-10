"""Crown crest printed on the oversized tee's left chest.

    python Tools/build_crown_mark.py

PlayerTopShell lofts the tee with its own UVs and maps this crest onto a small
patch on the wearer's left pec as its own alpha-clipped submesh, so the print
keeps its colour on every shirt colour instead of following the fabric tint.
White on transparent, drawn at 4x and downsampled for a clean edge.
"""

from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'GymChaos/Assets/Resources/Player/Outfits'
SIZE = 512
SCALE = 4


def crown():
    """Three peaks, a band and a ball over each point, inside an 0.8 frame."""
    s = SIZE * SCALE
    art = Image.new('L', (s, s), 0)
    d = ImageDraw.Draw(art)

    def at(x, y):
        return (x * s, y * s)

    # Band: the crest's base, slightly wider than the peaks.
    d.rounded_rectangle([*at(0.09, 0.66), *at(0.91, 0.78)], radius=int(0.035 * s), fill=255)
    # Body: two outer peaks and a taller middle one.
    body = [at(0.10, 0.66), at(0.20, 0.30), at(0.32, 0.53), at(0.50, 0.18),
            at(0.68, 0.53), at(0.80, 0.30), at(0.90, 0.66)]
    d.polygon(body, fill=255)
    # Balls over the points.
    for x, y, r in ((0.20, 0.28, 0.045), (0.50, 0.15, 0.055), (0.80, 0.28, 0.045)):
        d.ellipse([*at(x - r, y - r), *at(x + r, y + r)], fill=255)
    return art.resize((SIZE, SIZE), Image.LANCZOS)


def main():
    alpha = np.array(crown(), np.uint8)
    # Trim the transparent margin so the print fills its patch on the cloth.
    ys, xs = np.nonzero(alpha > 8)
    box = (xs.min(), ys.min(), xs.max() + 1, ys.max() + 1)
    pad = 6
    box = (max(0, box[0] - pad), max(0, box[1] - pad),
           min(SIZE, box[2] + pad), min(SIZE, box[3] + pad))
    alpha = np.array(Image.fromarray(alpha).crop(box).resize((SIZE, SIZE), Image.LANCZOS), np.uint8)
    rgb = np.full((SIZE, SIZE, 3), 255, np.uint8)
    Image.fromarray(np.dstack([rgb, alpha]), 'RGBA').save(OUT / 'crown_mark.png')
    print('CROWN_MARK_OK', int((alpha > 127).sum()), 'texels of', SIZE * SIZE)


if __name__ == '__main__':
    main()
