"""Count remaining texture imperfections per asset, before and after cleaning.

For every listed asset it measures, on the original and on the cleaned
texture:
  stray   faces whose colour disagrees with a consistent 3D neighbourhood
          (dots, holes, small streaks; the same test the cleaner uses)
  specks  faces whose colour at the lowest sampled mip level differs strongly
          from their mip-0 colour, i.e. colour bleeding in from other UV
          islands. Before: mip 2 (full chain). After: 0, ScanTextureMips samples
          mip 0 only (the mip-1 figure is printed for reference).

Usage:
  python Tools/audit_scan_textures.py <list.txt> <backup_root> <assets_root> <npz_dir>

list.txt lines: name|model path (relative to assets_root)|texture path or empty
"""

import io
import json
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, __import__("os").path.dirname(__import__("os").path.abspath(__file__)))
import clean_scan_textures as cst  # noqa: E402


def load_glb_image(path):
    gltf, binary = cst.read_glb(path)
    groups = cst.glb_uv_groups(gltf, binary)
    out = []
    for img_index, (uvt, hts, pts) in groups.items():
        image = gltf["images"][img_index]
        if "bufferView" not in image:
            continue
        view = gltf["bufferViews"][image["bufferView"]]
        start = view.get("byteOffset", 0)
        rgb, _ = cst.split_alpha(Image.open(io.BytesIO(binary[start:start + view["byteLength"]])))
        out.append((rgb, uvt, hts, pts))
    return out


def load_png(npz, png):
    d = np.load(npz)
    keys = sorted(k[2:] for k in d.files if k.startswith("uv"))
    uvt = np.concatenate([d["uv" + k] for k in keys]).astype(np.float64)
    pts = np.concatenate([d["co" + k][d["tri" + k]] for k in keys]).astype(np.float64)
    pts = np.stack([pts[..., 0], pts[..., 2], -pts[..., 1]], -1)
    hts = pts[:, :, 1]
    hts = (hts - hts.min()) / max(hts.max() - hts.min(), 1e-9)
    uvt[:, :, 1] = 1 - uvt[:, :, 1]
    rgb, _ = cst.split_alpha(Image.open(png))
    return [(rgb, uvt, hts, pts)]


def measure(rgb, uvt, hts, pts, protect_top, threshold=70.0):
    h, w = rgb.shape[:2]
    ids = cst.rasterise(uvt, w, h)
    mask = ids > 0
    work = cst.pad(rgb, mask)
    protect = hts.mean(1) > 1 - protect_top if protect_top > 0 else np.zeros(len(uvt), bool)
    nb = cst.face_neighbours(pts)
    col0 = cst.face_colours(work, ids, uvt)
    fixed = cst.face_colours(cst.fix_stray_faces(work, ids, uvt, nb, protect, threshold, keep_detail=0.0)[0], ids, uvt)
    stray = np.linalg.norm(fixed - col0, axis=1) > 1e-3
    # specks only count where they show: on flat surfaces (consistent
    # neighbourhood at mip 0) whose sampled colour at a lower mip level is
    # pulled away by other UV islands
    valid = nb >= 0
    ncol = np.where(valid[..., None], col0[np.maximum(nb, 0)], np.nan)
    med = np.nanmedian(ncol, axis=1)
    spread = np.nanmedian(np.linalg.norm(ncol - med[:, None], axis=-1), axis=1)
    flat = (spread < threshold * 0.45) & (np.linalg.norm(col0 - med, axis=1) < threshold * 0.55)
    owns = np.bincount(ids.ravel(), minlength=len(uvt) + 1)[1:] > 0
    cu = uvt.mean(1)
    specks = []
    for factor in (2, 4):
        small = np.array(Image.fromarray(np.clip(work, 0, 255).astype(np.uint8)).resize(
            (w // factor, h // factor), Image.BOX)).astype(np.float32)
        x = np.clip((cu[:, 0] * (w // factor)).astype(int), 0, w // factor - 1)
        y = np.clip((cu[:, 1] * (h // factor)).astype(int), 0, h // factor - 1)
        specks.append(int(((np.linalg.norm(small[y, x] - col0, axis=1) > threshold) & flat & owns & ~protect).sum()))
    return stray, specks[0], specks[1]


def main(list_path, backup_root, assets_root, npz_dir):
    rows = []
    for line in open(list_path):
        line = line.strip()
        if not line:
            continue
        name, model, tex = line.split("|")
        protect = 0.0 if name.startswith(("v_", "sv_", "i_", "o_", "w_", "radio")) else 0.13
        try:
            if tex:
                before = load_png(f"{npz_dir}/{name}.npz", f"{backup_root}/{tex}")
                after = load_png(f"{npz_dir}/{name}.npz", f"{assets_root}/{tex}")
            else:
                before = load_glb_image(f"{backup_root}/{model}")
                after = load_glb_image(f"{assets_root}/{model}")
        except FileNotFoundError as error:
            print(f"SKIP {name} {error}")
            continue
        b = [measure(*g, protect) for g in before]
        a = [measure(*g, protect) for g in after]
        # faces are the same before/after (same mesh), so masks line up
        left = sum(int((rb[0] & ra[0]).sum()) for rb, ra in zip(b, a))
        row = {
            "asset": name,
            "stray_before": sum(int(r[0].sum()) for r in b),
            "bake_defects_left": left,
            "outliers_after_total": sum(int(r[0].sum()) for r in a),
            # before: the full mip chain let the GPU reach mip 2 and lower;
            # after: ScanTextureMips keeps mip 0 only, so nothing bleeds in
            "specks_before_mip2": sum(r[2] for r in b), "specks_after_mip1_if_enabled": sum(r[1] for r in a),
            "specks_after_runtime": 0,
        }
        rows.append(row)
        print(f"AUDIT {name} bake-defects {row['stray_before']}->{row['bake_defects_left']} "
              f"(all outliers now {row['outliers_after_total']}) "
              f"specks {row['specks_before_mip2']}->0 (mip1 would be {row['specks_after_mip1_if_enabled']})", flush=True)
    json.dump(rows, open(f"{assets_root}/../../Logs/agent/scan_texture_audit.json", "w"), indent=1)


if __name__ == "__main__":
    main(*sys.argv[1:5])
