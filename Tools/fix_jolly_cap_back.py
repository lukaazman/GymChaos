"""Repaint the back of the jolly propeller cap from its front panels.

The image-to-3D bake only got the front of the cap right (red, yellow and
green panels, blue brim). The back is a blur of unrelated colours. The crown
is split by azimuth around the propeller stem: the baked front panels stay,
and the back continues the six-panel pattern with blue, red and yellow panels
in the front panel colours. The brim, propeller, stem and button keep their
texels.

Only the base-colour image changes; geometry and UVs stay as they are.

  python Tools/fix_jolly_cap_back.py <in.glb> <out.glb>
"""

import io
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import clean_scan_textures as cst  # noqa: E402


def main(src, dst):
    gltf, binary = cst.read_glb(src)
    groups = cst.glb_uv_groups(gltf, binary)
    (img_index, (uvt, _, pts)), = groups.items()
    image = gltf["images"][img_index]
    view = gltf["bufferViews"][image["bufferView"]]
    start = view.get("byteOffset", 0)
    rgb, alpha = cst.split_alpha(Image.open(io.BytesIO(binary[start:start + view["byteLength"]])))
    h, w = rgb.shape[:2]
    ids = cst.rasterise(uvt, w, h)
    mask = ids > 0
    work = cst.pad(rgb, mask)
    col = cst.face_colours(work, ids, uvt)

    allp = pts.reshape(-1, 3)
    lo, hi = allp.min(0), allp.max(0)
    height = hi[1] - lo[1]
    fc = pts.mean(1)
    yr = (fc[:, 1] - lo[1]) / height
    # crown axis = propeller stem (glTF: Y up, brim towards +Z)
    stem = (yr > 0.75) & (yr < 0.83)
    ax, az = fc[stem, 0].mean(), fc[stem, 2].mean()
    theta = np.degrees(np.arctan2(fc[:, 0] - ax, fc[:, 2] - az))
    brim = (fc[:, 2] > az + 0.45 * height) & (yr < 0.42)
    crown = (yr < 0.72) & ~brim
    # front panels as baked: red [-88,-2] (its outer edge is purple and is repainted), yellow [-2,40], green [40,85] degrees.
    # The back continues the six-panel cap: blue, red, yellow.
    def median_colour(lo_deg, hi_deg):
        sel = crown & (theta > lo_deg) & (theta < hi_deg) & (yr > 0.25) & (yr < 0.65)
        return np.median(col[sel], 0)
    red, yellow, green = median_colour(-60, -12), median_colour(5, 32), median_colour(48, 78)
    blue = np.median(col[brim], 0)
    panels = [(85, 153, blue), (153, 221, red), (221, 272, yellow), (272, 298, red)]
    wrapped = np.where(theta < 0, theta + 360, theta)
    back = crown & ((theta > 85) | (theta < -62))
    target = np.zeros((len(uvt) + 1, 3), np.float32)
    for lo_deg, hi_deg, colour in panels:
        sel = back & (wrapped >= lo_deg) & (wrapped < hi_deg)
        target[np.nonzero(sel)[0] + 1] = colour
    back_idx = np.nonzero(back)[0]
    is_back = np.zeros(len(uvt) + 1, bool)
    is_back[back_idx + 1] = True
    hit = is_back[ids]
    # keep a little of the original light/shade (luminance only) so panels are not flat
    luma = np.array([0.299, 0.587, 0.114], np.float32)
    own = (work - col[np.maximum(ids - 1, 0)].astype(np.float32)) @ luma
    work[hit] = target[ids][hit] + own[hit, None] * 0.35
    src_idx = np.nonzero(crown & ~back)[0]
    work = cst.pad(np.where(mask[..., None], work, 0), mask)
    out = np.clip(np.rint(work), 0, 255).astype(np.uint8)
    data, mime = cst.encode_like(image.get("mimeType", "image/png"), out, alpha)

    views = gltf["bufferViews"]
    new_bin = bytearray()
    for i in sorted(range(len(views)), key=lambda k: views[k].get("byteOffset", 0)):
        v = views[i]
        if v.get("buffer", 0) != 0:
            continue
        off = v.get("byteOffset", 0)
        chunk = data if i == image["bufferView"] else binary[off: off + v["byteLength"]]
        while len(new_bin) % 4:
            new_bin.append(0)
        v["byteOffset"] = len(new_bin)
        v["byteLength"] = len(chunk)
        new_bin += chunk
    image["mimeType"] = mime
    gltf["buffers"][0]["byteLength"] = len(new_bin)
    cst.write_glb(dst, gltf, bytes(new_bin))
    print(f"JOLLY_CAP back faces={len(back_idx)} front faces kept={len(src_idx)}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
