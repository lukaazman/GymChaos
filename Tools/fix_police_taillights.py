"""Turn the green smears in the police car tail lights red.

The bake painted parts of both tail lights green. The car has no green
anywhere else (black body, white doors, red/blue/white light bar), so every
clearly green texel becomes red with the same brightness.

  python Tools/fix_police_taillights.py png <in.png> <out.png>
  python Tools/fix_police_taillights.py glb <in.glb> <out.glb>
"""

import io
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import clean_scan_textures as cst  # noqa: E402


def recolour(rgb):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    green = (g > r + 35) & (g > b + 15)
    out = rgb.copy()
    level = np.maximum(g, r)
    out[green, 0] = np.clip(level[green] * 1.05, 0, 255)
    out[green, 1] = np.clip(r[green] * 0.35, 0, 255)
    out[green, 2] = np.clip(b[green] * 0.45, 0, 255)
    return out, int(green.sum())


def main(mode, src, dst):
    if mode == "png":
        img = Image.open(src)
        rgb, alpha = cst.split_alpha(img)
        out, n = recolour(rgb)
        out = np.clip(np.rint(out), 0, 255).astype(np.uint8)
        Image.fromarray(np.dstack([out, alpha]) if alpha is not None else out).save(dst, optimize=True)
    else:
        gltf, binary = cst.read_glb(src)
        image = gltf["images"][0]
        view = gltf["bufferViews"][image["bufferView"]]
        start = view.get("byteOffset", 0)
        rgb, alpha = cst.split_alpha(Image.open(io.BytesIO(binary[start:start + view["byteLength"]])))
        out, n = recolour(rgb)
        data, mime = cst.encode_like(image.get("mimeType", "image/png"), np.clip(np.rint(out), 0, 255).astype(np.uint8), alpha)
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
    print(f"POLICE_TAILLIGHTS green texels recoloured={n}")


if __name__ == "__main__":
    main(*sys.argv[1:4])
