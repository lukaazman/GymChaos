"""Remove scan-texture imperfections without touching geometry.

Photogrammetry / image-to-3D textures in this project share these defects:

* Many UV islands are single tiny triangles. The bake gave some of them the
  colour of a different body part, so a black shirt shows skin or grey dots,
  a navy suit shows black holes, and so on.
* Single texels inside an island have the wrong colour (lint, scratches).
* The gaps between islands hold colours of unrelated islands. Mipmaps and
  block compression average them into island borders.

The script rasterises the mesh UV triangles to get the face of every texel,
welds UV seams by position to get the 3D neighbours of every face, and
recolours faces whose colour disagrees with a consistent 3D neighbourhood.
It then removes strong single-texel outliers and rebuilds the gap padding
from the nearest island texel.

GLB files are edited at the byte level: only image buffer views change, so
meshes, skins, animations and materials stay identical.

Usage (system Python with numpy + Pillow):
  python Tools/clean_scan_textures.py glb <in.glb> <out.glb> [--protect-top 0.13]
  python Tools/clean_scan_textures.py png <uv.npz> <in.png> <out.png> [--protect-top 0.13]

`uv.npz` comes from Tools/dump_mesh_uvs.py (FBX/GLB through Blender).
`--protect-top` keeps the top fraction of the model height (face, eyes)
unchanged except for the gap padding.
"""

import argparse
import io
import json
import struct

import numpy as np
from PIL import Image, ImageDraw

GLB_MAGIC = 0x46546C67
COMPONENTS = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}
DTYPES = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}


# ---------------------------------------------------------------- GLB access

def read_glb(path):
    data = open(path, "rb").read()
    magic, _, length = struct.unpack_from("<III", data, 0)
    if magic != GLB_MAGIC:
        raise ValueError(f"{path} is not a GLB")
    offset = 12
    gltf, binary = None, b""
    while offset < length:
        chunk_len, chunk_type = struct.unpack_from("<II", data, offset)
        chunk = data[offset + 8: offset + 8 + chunk_len]
        if chunk_type == 0x4E4F534A:
            gltf = json.loads(chunk.decode("utf-8"))
        elif chunk_type == 0x004E4942:
            binary = chunk
        offset += 8 + chunk_len
    return gltf, binary


def write_glb(path, gltf, binary):
    js = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
    js += b" " * ((4 - len(js) % 4) % 4)
    binary += b"\0" * ((4 - len(binary) % 4) % 4)
    total = 12 + 8 + len(js) + 8 + len(binary)
    with open(path, "wb") as f:
        f.write(struct.pack("<III", GLB_MAGIC, 2, total))
        f.write(struct.pack("<II", len(js), 0x4E4F534A))
        f.write(js)
        f.write(struct.pack("<II", len(binary), 0x004E4942))
        f.write(binary)


def accessor(gltf, binary, index):
    acc = gltf["accessors"][index]
    view = gltf["bufferViews"][acc["bufferView"]]
    comps = COMPONENTS[acc["type"]]
    dtype = np.dtype(DTYPES[acc["componentType"]])
    start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    elem = comps * dtype.itemsize
    stride = view.get("byteStride", 0) or elem
    count = acc["count"]
    raw = np.frombuffer(binary, np.uint8, count=stride * (count - 1) + elem, offset=start)
    if stride == elem:
        out = raw.view(dtype).reshape(count, comps)
    else:
        rows = np.lib.stride_tricks.as_strided(raw, (count, elem), (stride, 1)).copy()
        out = rows.view(dtype).reshape(count, comps)
    out = out.astype(np.float64)
    if acc.get("normalized"):
        out /= np.iinfo(dtype).max
    return out


def glb_uv_groups(gltf, binary):
    """image index -> (uv triangles [n,3,2], heights [n,3] in 0..1, positions [n,3,3])."""
    prims = []
    ymin, ymax = np.inf, -np.inf
    for mesh in gltf.get("meshes", []):
        for prim in mesh["primitives"]:
            attrs = prim["attributes"]
            if prim.get("mode", 4) != 4 or "material" not in prim:
                continue
            tex = gltf["materials"][prim["material"]].get("pbrMetallicRoughness", {}).get("baseColorTexture")
            if tex is None:
                continue
            uv_key = f"TEXCOORD_{tex.get('texCoord', 0)}"
            img = gltf["textures"][tex["index"]].get("source")
            if img is None or uv_key not in attrs:
                continue
            pos = accessor(gltf, binary, attrs["POSITION"])
            uv = accessor(gltf, binary, attrs[uv_key])
            idx = accessor(gltf, binary, prim["indices"]).astype(np.int64).ravel() if "indices" in prim else np.arange(len(pos))
            tri = idx.reshape(-1, 3)
            ymin, ymax = min(ymin, pos[:, 1].min()), max(ymax, pos[:, 1].max())
            prims.append((img, uv[tri], pos[tri]))
    groups = {}
    for img, uvt, pt in prims:
        h = (pt[:, :, 1] - ymin) / max(ymax - ymin, 1e-9)
        if img in groups:
            g = groups[img]
            groups[img] = (np.concatenate([g[0], uvt]), np.concatenate([g[1], h]), np.concatenate([g[2], pt]))
        else:
            groups[img] = (uvt, h, pt)
    return groups


# ------------------------------------------------------------- image passes

def rasterise(uv_tris, width, height):
    """Per-texel face id (0 = gap). uv rows are top-down (glTF convention)."""
    ids = Image.new("I", (width, height), 0)
    draw = ImageDraw.Draw(ids)
    px = uv_tris[:, :, 0] * width
    py = uv_tris[:, :, 1] * height
    for i in range(len(uv_tris)):
        draw.polygon([(px[i, 0], py[i, 0]), (px[i, 1], py[i, 1]), (px[i, 2], py[i, 2])],
                     fill=i + 1, outline=i + 1)
    return np.array(ids, np.int64)


def shifted(a, dy, dx):
    out = a.copy()
    h, w = a.shape[:2]
    ys = slice(max(dy, 0), h + min(dy, 0)); yd = slice(max(-dy, 0), h + min(-dy, 0))
    xs = slice(max(dx, 0), w + min(dx, 0)); xd = slice(max(-dx, 0), w + min(-dx, 0))
    out[yd, xd] = a[ys, xs]
    return out


NEIGHBOURS = [(-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (-1, 1), (1, -1), (1, 1)]


def pad(rgb, mask, rings=24):
    """Fill gap texels from the nearest island texel, never mixing islands."""
    img = rgb.astype(np.float32).copy()
    known = mask.copy()
    for _ in range(rings):
        if known.all():
            break
        grown = np.zeros(known.shape, bool)
        for dy, dx in NEIGHBOURS:
            take = shifted(known, dy, dx) & ~known & ~grown
            if take.any():
                img[take] = shifted(img, dy, dx)[take]
                grown |= take
        if not grown.any():
            break
        known |= grown
    if not known.all():
        # push-pull for the remaining far gaps
        levels = [(img * known[..., None], known.astype(np.float32))]
        while min(levels[-1][1].shape) > 1:
            c, w = levels[-1]
            h2, w2 = (c.shape[0] + 1) // 2, (c.shape[1] + 1) // 2
            cp = np.zeros((h2 * 2, w2 * 2, 3), np.float32); wp = np.zeros((h2 * 2, w2 * 2), np.float32)
            cp[:c.shape[0], :c.shape[1]] = c; wp[:w.shape[0], :w.shape[1]] = w
            levels.append((cp.reshape(h2, 2, w2, 2, 3).sum((1, 3)), wp.reshape(h2, 2, w2, 2).sum((1, 3))))
        fill = levels[-1][0] / np.maximum(levels[-1][1], 1e-6)[..., None]
        for c, w in reversed(levels[:-1]):
            up = np.repeat(np.repeat(fill, 2, 0), 2, 1)[:c.shape[0], :c.shape[1]]
            fill = np.where((w > 0)[..., None], c / np.maximum(w, 1e-6)[..., None], up)
        img[~known] = fill[~known]
    return img


def face_neighbours(positions, max_per_vertex=16):
    """Vertex-ring neighbour faces [n, 3*M] (-1 padded); UV seams welded by position."""
    n = len(positions)
    flat = positions.reshape(-1, 3)
    extent = max(float(np.ptp(flat, 0).max()), 1e-9)
    q = np.round(flat / (extent * 2e-5)).astype(np.int64)
    _, vid = np.unique(q, axis=0, return_inverse=True)
    vid = vid.reshape(n, 3)
    nv = int(vid.max()) + 1
    order = np.argsort(vid.ravel(), kind="stable")
    v_sorted = vid.ravel()[order]
    f_sorted = order // 3
    starts = np.searchsorted(v_sorted, np.arange(nv))
    rank = np.arange(len(v_sorted)) - starts[v_sorted]
    keep = rank < max_per_vertex
    vf = np.full((nv, max_per_vertex), -1, np.int64)
    vf[v_sorted[keep], rank[keep]] = f_sorted[keep]
    nb = vf[vid].reshape(n, -1)
    nb[nb == np.arange(n)[:, None]] = -1
    return nb


def face_colours(work, ids, uv_tris):
    n = len(uv_tris)
    h, w = ids.shape
    flat = ids.ravel()
    sel = flat > 0
    fid = flat[sel] - 1
    cnt = np.bincount(fid, minlength=n).astype(np.float64)
    pix = work.reshape(-1, 3)[sel]
    col = np.stack([np.bincount(fid, weights=pix[:, c], minlength=n) for c in range(3)], 1)
    col /= np.maximum(cnt, 1)[:, None]
    empty = cnt == 0
    if empty.any():
        cu = uv_tris[empty].mean(1)
        x = np.clip((cu[:, 0] * w).astype(int), 0, w - 1)
        y = np.clip((cu[:, 1] * h).astype(int), 0, h - 1)
        col[empty] = work[y, x]
    return col


def fix_stray_faces(work, ids, uv_tris, nb, protect, threshold, scale=1, keep_detail=0.25,
                    settle=False, allowed=None):
    """Recolour faces whose colour disagrees with a consistent 3D neighbourhood.

    Detail on the face (eyes, print, camo) has a high neighbourhood spread and
    is kept. A dot on a flat shirt or suit has a low spread and is replaced.
    `scale` > 1 compares against iterated neighbourhood medians, which reach a
    few rings out and also catch streaks several faces wide.
    """
    col = face_colours(work, ids, uv_tris)
    valid = nb >= 0
    ref = col
    for _ in range(scale):
        ncol = np.where(valid[..., None], ref[np.maximum(nb, 0)], np.nan)
        med = np.nanmedian(ncol, axis=1)
        med = np.where(np.isnan(med), col, med)
        ref = med
    spread = np.nanmedian(np.linalg.norm(ncol - med[:, None], axis=-1), axis=1)
    dist = np.linalg.norm(col - med, axis=-1)
    # faces smaller than a texel own no texels: nothing to repaint, and at
    # that size they are not visible on screen either
    owns_texels = np.bincount(ids.ravel(), minlength=len(uv_tris) + 1)[1:] > 0
    stray = (valid.sum(1) >= 4) & ~protect & owns_texels & (spread < threshold * 0.45) & \
        (dist > np.maximum(threshold * 0.55, spread * 4.0))
    if allowed is not None:
        stray &= allowed
    if not stray.any():
        return work, 0
    if settle:
        # neighbouring strays can keep flipping each other; take the colour of
        # the non-stray neighbours only, so the result is final
        clean_nb = valid & ~stray[np.maximum(nb, 0)]
        ncol = np.where(clean_nb[..., None], col[np.maximum(nb, 0)], np.nan)
        settled = np.nanmedian(ncol, axis=1)
        med = np.where(np.isnan(settled), med, settled)
    face = np.maximum(ids - 1, 0)
    hit = (ids > 0) & stray[face]
    out = work.copy()
    detail = work - col[face].astype(np.float32)
    out[hit] = (med[face].astype(np.float32) + detail * keep_detail)[hit]
    return out, int(stray.sum())


def paint_slivers(work, ids, uv_tris, nb, protect):
    """Give faces that own no texel the colour of their 3D neighbours.

    A sliver face smaller than a texel samples whatever padding lies under
    its UV footprint, often a colour from an unrelated island (grey or blue
    flecks on a copper bullet tip). Only gap texels under such footprints are
    written, so no island texel changes.
    """
    h, w = ids.shape
    n = len(uv_tris)
    owned = np.bincount(ids.ravel(), minlength=n + 1)[1:] > 0
    targets = np.nonzero(~owned & ~protect)[0]
    if len(targets) == 0:
        return 0
    col = face_colours(work, ids, uv_tris)
    valid = (nb >= 0) & owned[np.maximum(nb, 0)]
    ncol = np.where(valid[..., None], col[np.maximum(nb, 0)], np.nan)
    with np.errstate(all="ignore"):
        med = np.nanmedian(ncol, axis=1)
    gap = ids == 0
    painted = 0
    for f in targets:
        if np.isnan(med[f]).any():
            continue
        xs = uv_tris[f, :, 0] * w
        ys = uv_tris[f, :, 1] * h
        x0, x1 = int(np.floor(xs.min())) - 1, int(np.ceil(xs.max())) + 1
        y0, y1 = int(np.floor(ys.min())) - 1, int(np.ceil(ys.max())) + 1
        x0, y0 = max(x0, 0), max(y0, 0)
        x1, y1 = min(x1, w - 1), min(y1, h - 1)
        if x1 < x0 or y1 < y0 or (x1 - x0) * (y1 - y0) > 64:
            continue
        region = gap[y0:y1 + 1, x0:x1 + 1]
        if region.any():
            work[y0:y1 + 1, x0:x1 + 1][region] = med[f]
            painted += 1
    return painted


LINT_RISE = 16.0


def remove_dark_lint(rgb, mask, protect_px, rise=16.0, dark=60.0):
    """Remove light lint texels on dark cloth (black shirts, navy suits).

    On dark fabric a single texel a little brighter than its 5x5 median reads
    as a white speck in game, although the jump is far below the general
    outlier threshold. Only texels whose neighbourhood is dark are touched.
    """
    luma_w = np.array([0.299, 0.587, 0.114], np.float32)
    total = 0
    for _ in range(4):
        med, _mad = median5(rgb)
        lum = rgb @ luma_w
        mlum = med @ luma_w
        lint = mask & ~protect_px & (mlum < dark) & (lum > mlum + rise)
        if not lint.any():
            break
        rgb = rgb.copy()
        rgb[lint] = med[lint]
        total += int(lint.sum())
    return rgb, total


def median5(img):
    stack = np.stack([shifted(img, dy, dx) for dy in range(-2, 3) for dx in range(-2, 3)], 0)
    med = np.median(stack, 0)
    mad = np.median(np.linalg.norm(stack - med[None], axis=-1), 0)
    return med, mad


def remove_texel_outliers(rgb, mask, protect_px, threshold):
    """Replace strong single-texel outliers inside islands with the 5x5 median."""
    med, mad = median5(rgb)
    diff = np.linalg.norm(rgb - med, axis=-1)
    limit = np.minimum(np.maximum(threshold * 0.6, mad * 6.0 + threshold * 0.2), threshold * 1.2)
    out = (diff > limit) & mask & ~protect_px
    fixed = rgb.copy()
    fixed[out] = med[out]
    return fixed, int(out.sum())


def clean_image(rgb, uv_tris, heights, positions, protect_top, threshold, passes=30, max_scale=1, keep_boxes=(), only_boxes=(),
                max_face_texels=0, dark_lint=False):
    h, w = rgb.shape[:2]
    ids = rasterise(uv_tris, w, h)
    mask = ids > 0
    protect = heights.mean(1) > 1 - protect_top if protect_top > 0 else np.zeros(len(uv_tris), bool)
    protect_px = protect[np.maximum(ids - 1, 0)] & mask
    nb = face_neighbours(positions)
    work = pad(rgb, mask)
    faces = 0
    # keep boxes (normalised model bounds) hold real thin details such as a
    # pocket square.
    centres = positions.mean(1)
    lo, hi = positions.reshape(-1, 3).min(0), positions.reshape(-1, 3).max(0)
    norm = (centres - lo) / np.maximum(hi - lo, 1e-9)
    kept = np.zeros(len(uv_tris), bool)
    for box in keep_boxes:
        b = np.asarray(box, float).reshape(3, 2)
        kept |= np.all((norm >= b[:, 0]) & (norm <= b[:, 1]), axis=1)
    # keep boxes hold real thin detail: never repaint them at any scale
    protect = protect | kept
    # only boxes: restrict all repairs to these regions (targeted touch-ups)
    if only_boxes:
        inside = np.zeros(len(uv_tris), bool)
        for box in only_boxes:
            b = np.asarray(box, float).reshape(3, 2)
            inside |= np.all((norm >= b[:, 0]) & (norm <= b[:, 1]), axis=1)
        protect = protect | ~inside
    # small hand-made textures: big faces carry real shading, repair dots only
    if max_face_texels > 0:
        owned = np.bincount(ids.ravel(), minlength=len(uv_tris) + 1)[1:]
        protect = protect | (owned > max_face_texels)
    protect_px = protect[np.maximum(ids - 1, 0)] & mask
    # single-texel outliers first, so the face passes see clean face colours
    work, texels = remove_texel_outliers(work, mask, protect_px, threshold)
    work = pad(np.where(mask[..., None], work, 0), mask)

    # Only faces that are outliers in the texture as baked are repaired.
    # Without this, every repaired face exposes its neighbours as new
    # outliers and thin real detail (prints, stitching) erodes pass by pass.
    def candidates(scale, guard, t):
        before = face_colours(work, ids, uv_tris)
        after = face_colours(fix_stray_faces(work, ids, uv_tris, nb, guard, t, scale, keep_detail=0.0)[0], ids, uv_tris)
        return np.linalg.norm(after - before, axis=1) > 1e-3

    allowed = np.zeros(len(uv_tris), bool)
    for scale in range(1, max_scale + 1):
        allowed |= candidates(scale, protect, threshold)
    allowed |= candidates(1, protect, 70.0)

    def converge(scale, guard, limit):
        fixed = 0
        for attempt in range(limit):
            # the first passes keep a little of the face's own shading; faces
            # that are still stray after that take the neighbourhood colour
            nonlocal work
            work, k = fix_stray_faces(work, ids, uv_tris, nb, guard, threshold, scale,
                                      keep_detail=0.25 if attempt < 3 else 0.0, allowed=allowed)
            fixed += k
            if k == 0:
                break
        return fixed

    def run_scales():
        fixed = 0
        for scale in range(1, max_scale + 1):
            fixed += converge(scale, protect, passes)
        # wider rings can leave new single-ring outliers at their edges
        return fixed + converge(1, protect, passes)

    faces += run_scales()
    # A defect two or three faces wide hides its own members (they raise each
    # other's neighbourhood spread). Once its first faces are repaired the
    # rest show up next to them; admit those, but only next to a repaired
    # face and only a few rings out, so real detail cannot erode.
    for _ in range(3):
        fresh = candidates(1, protect, threshold) & ~allowed
        near = np.zeros(len(uv_tris) + 1, bool)
        near[1:] = allowed
        touching = (near[np.where(nb >= 0, nb + 1, 0)]).any(1)
        fresh &= touching
        if not fresh.any():
            break
        allowed |= fresh
        faces += run_scales()
    # settle at the requested threshold and at the default one, so a strong
    # run is never worse than a default run on any face
    checks = sorted({threshold, 70.0})
    for _ in range(6):
        moved = 0
        for t in checks:
            work, k = fix_stray_faces(work, ids, uv_tris, nb, protect, t, 1, keep_detail=0.0, settle=True,
                                      allowed=allowed)
            moved += k
        faces += moved
        if moved == 0:
            break
    remaining = sum(fix_stray_faces(work, ids, uv_tris, nb, protect, t, 1)[1] for t in checks)
    emerged = remaining - sum(fix_stray_faces(work, ids, uv_tris, nb, protect, t, 1, allowed=allowed)[1]
                              for t in checks)
    work = pad(np.where(mask[..., None], work, 0), mask)
    lint = 0
    if dark_lint:
        work, lint = remove_dark_lint(work, mask, protect_px, rise=LINT_RISE)
        work = pad(np.where(mask[..., None], work, 0), mask)
    slivers = paint_slivers(work, ids, uv_tris, nb, protect)
    out = np.clip(np.rint(work), 0, 255).astype(np.uint8)
    stats = {"size": (w, h), "coverage": round(float(mask.mean()), 4), "faces_fixed": faces,
             "texels_fixed": texels, "faces_remaining": remaining, "of_which_not_in_bake": emerged,
             "slivers_painted": slivers, "dark_lint_texels": lint}
    return out, stats


# ------------------------------------------------------------------- modes

def encode_like(original_mime, rgb, alpha):
    buf = io.BytesIO()
    if original_mime == "image/jpeg" and alpha is None:
        Image.fromarray(rgb).save(buf, "JPEG", quality=95, subsampling=0)
        return buf.getvalue(), "image/jpeg"
    Image.fromarray(np.dstack([rgb, alpha]) if alpha is not None else rgb).save(buf, "PNG", optimize=True)
    return buf.getvalue(), "image/png"


def split_alpha(img):
    if img.mode in ("RGBA", "LA") or (img.mode == "P" and "transparency" in img.info):
        rgba = np.array(img.convert("RGBA"))
        a = rgba[..., 3]
        return rgba[..., :3].astype(np.float32), (a if a.min() < 255 else None)
    return np.array(img.convert("RGB")).astype(np.float32), None


def run_glb(src, dst, protect_top, threshold, max_scale=1, keep_boxes=(), only_boxes=(), max_face_texels=0, dark_lint=False):
    gltf, binary = read_glb(src)
    groups = glb_uv_groups(gltf, binary)
    if not groups:
        print(f"SKIP {src}: no textured primitives")
        return False
    replacements = {}
    for img_index, (uvt, hts, pts) in groups.items():
        image = gltf["images"][img_index]
        if "bufferView" not in image:
            continue
        view = gltf["bufferViews"][image["bufferView"]]
        start = view.get("byteOffset", 0)
        rgb, alpha = split_alpha(Image.open(io.BytesIO(binary[start:start + view["byteLength"]])))
        out, stats = clean_image(rgb, uvt, hts, pts, protect_top, threshold, max_scale=max_scale, keep_boxes=keep_boxes,
                                 only_boxes=only_boxes, max_face_texels=max_face_texels,
                                 dark_lint=dark_lint)
        # always PNG: JPEG re-compression would bring back faint blotches
        data, mime = encode_like("image/png", out, alpha)
        replacements[image["bufferView"]] = (data, mime, img_index)
        print(f"IMAGE {img_index} {stats} tris={len(uvt)}")
    if not replacements:
        return False
    # re-pack every buffer view of buffer 0 in its original order
    views = gltf["bufferViews"]
    new_bin = bytearray()
    for i in sorted(range(len(views)), key=lambda k: views[k].get("byteOffset", 0)):
        v = views[i]
        if v.get("buffer", 0) != 0:
            continue
        off = v.get("byteOffset", 0)
        chunk = replacements[i][0] if i in replacements else binary[off: off + v["byteLength"]]
        while len(new_bin) % 4:
            new_bin.append(0)
        v["byteOffset"] = len(new_bin)
        v["byteLength"] = len(chunk)
        new_bin += chunk
    for _, (_, mime, img_index) in replacements.items():
        gltf["images"][img_index]["mimeType"] = mime
    gltf["buffers"][0]["byteLength"] = len(new_bin)
    write_glb(dst, gltf, bytes(new_bin))
    return True


def run_png(npz, src, dst, protect_top, threshold, max_scale=1, keep_boxes=(), only_boxes=(), max_face_texels=0, dark_lint=False):
    d = np.load(npz)
    keys = sorted(k[2:] for k in d.files if k.startswith("uv"))
    uvt = np.concatenate([d["uv" + k] for k in keys]).astype(np.float64)
    pts = np.concatenate([d["co" + k][d["tri" + k]] for k in keys]).astype(np.float64)
    # Blender Z-up -> glTF Y-up (+Z forward), so --keep-box means the same in both modes
    pts = np.stack([pts[..., 0], pts[..., 2], -pts[..., 1]], -1)
    hts = pts[:, :, 1]
    hts = (hts - hts.min()) / max(hts.max() - hts.min(), 1e-9)
    uvt[:, :, 1] = 1 - uvt[:, :, 1]  # Blender uv (v up) -> image rows
    rgb, alpha = split_alpha(Image.open(src))
    out, stats = clean_image(rgb, uvt, hts, pts, protect_top, threshold, max_scale=max_scale, keep_boxes=keep_boxes,
                                 only_boxes=only_boxes, max_face_texels=max_face_texels,
                                 dark_lint=dark_lint)
    Image.fromarray(np.dstack([out, alpha]) if alpha is not None else out).save(dst, optimize=True)
    print(f"PNG {dst} {stats} tris={len(uvt)}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("mode", choices=["glb", "png"])
    ap.add_argument("paths", nargs="+")
    ap.add_argument("--protect-top", type=float, default=0.0)
    ap.add_argument("--threshold", type=float, default=70.0)
    ap.add_argument("--max-scale", type=int, default=1, help="neighbourhood rings for streak removal")
    ap.add_argument("--keep-box", action="append", default=[],
                    help="x0,x1,y0,y1,z0,z1 in normalised model bounds that are never repainted")
    ap.add_argument("--only-box", action="append", default=[],
                    help="x0,x1,y0,y1,z0,z1: repaint only inside these regions")
    ap.add_argument("--dark-lint", action="store_true", help="remove light lint texels on dark cloth")
    ap.add_argument("--lint-rise", type=float, default=16.0, help="--dark-lint: brightness jump that counts as lint")
    ap.add_argument("--max-face-texels", type=int, default=0,
                    help="repaint only faces owning at most this many texels (0 = no limit)")
    a = ap.parse_args()
    boxes = [[float(v) for v in b.split(',')] for b in a.keep_box]
    global LINT_RISE
    LINT_RISE = a.lint_rise
    only = [[float(v) for v in b.split(',')] for b in a.only_box]
    if a.mode == "glb":
        run_glb(a.paths[0], a.paths[1], a.protect_top, a.threshold, a.max_scale, boxes, only, a.max_face_texels, a.dark_lint)
    else:
        run_png(a.paths[0], a.paths[1], a.paths[2], a.protect_top, a.threshold, a.max_scale, boxes, only, a.max_face_texels, a.dark_lint)


if __name__ == "__main__":
    main()
