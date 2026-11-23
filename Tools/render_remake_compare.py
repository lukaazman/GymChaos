"""Render a remade character next to its reference image.

    blender -b -noaudio --python-exit-code 1 -P Tools/render_remake_compare.py -- davie

Opens Assets/BodyBuilders/remake/<name>.blend (LOD0 only), renders it from the
front and three-quarter view, and writes Logs/agent/remake/<name>_compare.png
with the reference image from remake/ in the first tile.
"""

from __future__ import annotations

import sys
from pathlib import Path

import bpy

sys.path.insert(0, str(Path(__file__).resolve().parent))
import render_remake_sheet as rs  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Logs/agent/remake"
# Reference files whose name differs from the character's.
REFERENCES = {"zyzz": "Zyzz", "jolly_dog": "lolipop_larry"}


def reference_for(name: str) -> Path:
    stem = REFERENCES.get(name, name)
    found = sorted((ROOT / "remake").glob(stem + ".*"))
    if not found:
        raise FileNotFoundError(f"no reference image remake/{stem}.*")
    return found[0]


def main() -> int:
    name = sys.argv[sys.argv.index("--") + 1]
    reference = reference_for(name)
    bpy.ops.wm.open_mainfile(filepath=str(ROOT / f"Assets/BodyBuilders/remake/{name}.blend"))
    lod1 = bpy.data.objects.get("Body_LOD1")
    if lod1:
        lod1.hide_render = True
    meshes = [o for o in bpy.data.objects if o.type == "MESH" and not o.hide_render]
    if "--toon" in sys.argv:  # anime characters: cel ramp and ink outline, as in the game
        rs.toon(meshes)
    rs.TILE_W, rs.TILE_H, rs.COLUMNS = 512, 768, 3
    scene, camera, lights = rs.setup_scene()
    OUT.mkdir(parents=True, exist_ok=True)

    ref = bpy.data.images.load(str(reference))
    w, h = ref.size
    scale = min(rs.TILE_W / w, rs.TILE_H / h)
    ref.scale(max(1, int(w * scale)), max(1, int(h * scale)))
    canvas = bpy.data.images.new("ref_tile", rs.TILE_W, rs.TILE_H, alpha=True)
    import numpy as np
    tile = np.full((rs.TILE_H, rs.TILE_W, 4), 0.16, dtype=np.float32)
    tile[..., 3] = 1.0
    rw, rh = ref.size
    pixels = np.empty(rw * rh * 4, dtype=np.float32)
    ref.pixels.foreach_get(pixels)
    x0, y0 = (rs.TILE_W - rw) // 2, (rs.TILE_H - rh) // 2
    tile[y0:y0 + rh, x0:x0 + rw] = pixels.reshape(rh, rw, 4)
    canvas.pixels.foreach_set(tile.ravel())
    ref_path = OUT / f"{name}_compare_ref.png"
    canvas.filepath_raw = str(ref_path)
    canvas.file_format = "PNG"
    canvas.save()

    tiles = [ref_path]
    for view, yaw in (("front", 0.0), ("tq", 35.0)):
        path = OUT / f"{name}_compare_{view}.png"
        rs.render_tile(scene, camera, lights, meshes, yaw, path)
        tiles.append(path)
    output = OUT / f"{name}_compare.png"
    rs.stitch(tiles, output)
    print(f"REMAKE_COMPARE_OK {name} reference={reference.name} path={output}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
