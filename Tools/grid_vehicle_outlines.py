"""Draw a labelled car-frame grid on Tools/measure_vehicle_outlines.py renders.

Usage:
  python Tools/grid_vehicle_outlines.py NAME VIEW HMIN HMAX VMIN VMAX
Writes Logs/agent/cabins/measure/<name>_<view>_crop.png: the region in car
units (h = horizontal axis, v = vertical axis of the view), upscaled 2x, with
0.01 unit lines and labels every 0.05 unit.
"""
import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parents[1] / "Logs/agent/cabins/measure"


def main():
    name, view = sys.argv[1], sys.argv[2]
    hmin, hmax, vmin, vmax = (float(v) for v in sys.argv[3:7])
    meta = json.loads((OUT / f"{name}.json").read_text())
    m = meta[view]
    res, span = meta["res"], meta["span"]

    def px(h):
        return (h - m["h_center"]) * m["h_sign"] / span * res + res / 2

    def py(v):
        return res / 2 - (v - m["v_center"]) * m["v_sign"] / span * res

    xs = sorted((px(hmin), px(hmax)))
    ys = sorted((py(vmin), py(vmax)))
    box = (int(xs[0]), int(ys[0]), int(xs[1]) + 1, int(ys[1]) + 1)
    scale = 2
    img = Image.open(OUT / f"{name}_{view}.png").convert("RGB").crop(box)
    img = img.resize((img.width * scale, img.height * scale), Image.LANCZOS)
    d = ImageDraw.Draw(img)
    step = 0.01
    for k in range(int(min(hmin, hmax) / step) - 1, int(max(hmin, hmax) / step) + 2):
        val = round(k * step, 3)
        x = (px(val) - box[0]) * scale
        major = k % 5 == 0
        if 0 <= x < img.width:
            d.line([(x, 0), (x, img.height)], fill=(255, 0, 0) if major else (0, 150, 255), width=1)
            if major:
                d.text((x + 2, 2), f"{val:+.2f}", fill=(255, 0, 0))
    for k in range(int(min(vmin, vmax) / step) - 1, int(max(vmin, vmax) / step) + 2):
        val = round(k * step, 3)
        y = (py(val) - box[1]) * scale
        major = k % 5 == 0
        if 0 <= y < img.height:
            d.line([(0, y), (img.width, y)], fill=(255, 0, 0) if major else (0, 150, 255), width=1)
            if major:
                d.text((2, y + 2), f"{val:+.2f}", fill=(255, 0, 0))
    out = OUT / f"{name}_{view}_crop.png"
    img.save(out)
    print(out)


if __name__ == "__main__":
    main()
