"""Correct the red cast on Jay Cutler's scanned skin.

The jay.png scan bake tinted all skin brick red (sunburn look) while the
other scans have a tan skin. Skin texels (red/orange hue, medium saturation)
are moved in Lab space towards the skin statistics of a reference scan
(arnold.png by default), keeping their own light/shade. White tank top,
shorts, shoes, hair and the headband are not skin-coloured and stay as they
are.

  python Tools/fix_jay_skin.py <jay.png> <reference.png> <out.png> [strength]
"""

import sys

import numpy as np
from PIL import Image


def srgb_to_lab(rgb):
    c = rgb / 255.0
    c = np.where(c > 0.04045, ((c + 0.055) / 1.055) ** 2.4, c / 12.92)
    m = np.array([[0.4124, 0.3576, 0.1805], [0.2126, 0.7152, 0.0722], [0.0193, 0.1192, 0.9505]])
    xyz = c @ m.T / np.array([0.95047, 1.0, 1.08883])
    f = np.where(xyz > 0.008856, np.cbrt(xyz), 7.787 * xyz + 16 / 116)
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], -1)


def lab_to_srgb(lab):
    fy = (lab[..., 0] + 16) / 116
    fx = fy + lab[..., 1] / 500
    fz = fy - lab[..., 2] / 200
    f = np.stack([fx, fy, fz], -1)
    xyz = np.where(f > 0.206893, f ** 3, (f - 16 / 116) / 7.787) * np.array([0.95047, 1.0, 1.08883])
    m = np.array([[3.2406, -1.5372, -0.4986], [-0.9689, 1.8758, 0.0415], [0.0557, -0.2040, 1.0570]])
    c = xyz @ m.T
    c = np.where(c > 0.0031308, 1.055 * np.clip(c, 0, None) ** (1 / 2.4) - 0.055, 12.92 * c)
    return np.clip(c * 255, 0, 255)


def skin_mask(lab):
    chroma = np.hypot(lab[..., 1], lab[..., 2])
    hue = np.degrees(np.arctan2(lab[..., 2], lab[..., 1]))
    return (lab[..., 0] > 18) & (lab[..., 0] < 85) & (chroma > 14) & (hue > 5) & (hue < 75)


def main(src, ref, dst, strength=0.8):
    img = Image.open(src)
    rgba = np.array(img.convert("RGBA")).astype(np.float64)
    lab = srgb_to_lab(rgba[..., :3])
    ref_lab = srgb_to_lab(np.array(Image.open(ref).convert("RGB")).astype(np.float64))
    mask = skin_mask(lab)
    ref_mask = skin_mask(ref_lab)
    src_mean, src_std = lab[mask].mean(0), lab[mask].std(0) + 1e-6
    ref_mean, ref_std = ref_lab[ref_mask].mean(0), ref_lab[ref_mask].std(0) + 1e-6
    moved = lab.copy()
    # chroma (a, b) follows the reference; lightness keeps its own shading
    for ch in (1, 2):
        moved[..., ch] = (lab[..., ch] - src_mean[ch]) / src_std[ch] * ref_std[ch] + ref_mean[ch]
    moved[..., 0] = lab[..., 0] + (ref_mean[0] - src_mean[0]) * 0.5
    # soft mask edge so skin borders do not band
    weight = mask.astype(np.float64) * strength
    out_lab = lab + (moved - lab) * weight[..., None]
    rgba[..., :3] = lab_to_srgb(out_lab)
    mode = "RGBA" if img.mode == "RGBA" else "RGB"
    Image.fromarray(np.rint(rgba).astype(np.uint8)[..., :4 if mode == "RGBA" else 3], mode).save(dst, optimize=True)
    print(f"JAY_SKIN texels={int(mask.sum())} src_ab=({src_mean[1]:.1f},{src_mean[2]:.1f}) "
          f"ref_ab=({ref_mean[1]:.1f},{ref_mean[2]:.1f}) strength={strength}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], sys.argv[3], float(sys.argv[4]) if len(sys.argv) > 4 else 0.8)
