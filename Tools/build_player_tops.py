"""Player top textures (Oversized, Onyx) and the Oversized shell mask.

    blender -b --factory-startup -P Tools/export_player_uv_positions.py
    python Tools/build_player_tops.py

Every atlas texel of the player scan gets its rest-pose position and normal
from .tools/player-uvpos.npz (T-pose, height 1, front -Y, Z up). Garment
coverage and print are then designed in 3D, so they stay continuous across
the scan's fragmented UV islands:

- Oversized: torso from below the shorts' waistband to a crew neck, sleeves
  to just above the elbow, no front logo, "KING" arched across the upper
  back in a fixed grey whatever the shirt colour.
- Onyx: compression top to mid-biceps, always black with the chosen colour
  as accent: honeycomb knit panels (V panel on the chest, inverted triangle
  on the back), raglan seams and sleeve cuffs, no front logo.

Fabric keeps the scan's local folds/muscle shading (a high-pass of the
original luminance, per material so skin and cloth do not halo).

oversized_shell.bytes / onyx_shell.bytes (PNG) drive PlayerTopShell at
runtime: R = shell coverage, G = inflation (G * InflateRange of the mesh
height), B = painted, A = signed distance to the open edges.
"""
import sys
sys.path.insert(0, '.tools/video-deps')
from pathlib import Path
import json
import numpy as np
import cv2
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / 'GymChaos/Assets/Resources/Characters/Textures/player_authored.png'
OUT = ROOT / 'GymChaos/Assets/Resources/Player/Outfits'
DEBUG = ROOT / '.tools/tops'
INFLATE_RANGE = 0.04

COLORS = {
    'black': (0.035, 0.035, 0.04),
    'white': (0.9, 0.92, 0.9),
    'red': (0.72, 0.045, 0.025),
    'blue': (0.035, 0.22, 0.78),
    'gold': (0.92, 0.58, 0.08),
}
KING_GREY = np.array((0.36, 0.36, 0.38), np.float32)
ONYX_BASE = np.array((0.03, 0.03, 0.034), np.float32)
ONYX_ACCENT = {
    'black': (0.2, 0.2, 0.215),
    'white': (0.82, 0.84, 0.84),
    'red': (0.7, 0.05, 0.03),
    'blue': (0.05, 0.25, 0.8),
    'gold': (0.9, 0.58, 0.1),
}

# Body landmarks (height fractions / metres of the height-1 rest mesh).
TANK_HEM = 0.528
OVERSIZED_HEM = 0.495
NECK_HALF_WIDTH = 0.072
NECK_LINE = 0.801
NECK_LINE_FRONT = 0.781
# Crew neck: a cylinder round the neck (axis at x 0, y NECK_CENTER_Y) cut by
# a plane that sits lower at the throat than at the nape.
NECK_CENTER_Y = 0.01
NECK_RADIUS = 0.072
COLLAR_Z = 0.786
COLLAR_SLOPE = 0.17
# Onyx reaches past the tank top's hem so the tank never shows below it.
ONYX_HEM = 0.512
TORSO_HALF_WIDTH = 0.135
OVERSIZED_SLEEVE_END = 0.285
ONYX_SLEEVE_END = 0.245
PAINT_MARGIN = 0.01
ONYX_MARGIN = 0.006
ONYX_INFLATE = 0.0035
ONYX_INFLATE_EDGE = 0.001
# Mask A: edge distance, 128 = on the edge, +-127 = +-EDGE_RANGE.
EDGE_RANGE = 0.03


def rasterize(uv, size):
    """Triangle id (+1) per texel, grown 2 px over island edges."""
    ids = np.zeros((size, size), np.int32)
    pts = np.stack([uv[:, :, 0] * (size - 1), (1 - uv[:, :, 1]) * (size - 1)], -1)
    pix = np.round(pts).astype(np.int32)
    for k in range(len(uv)):
        cv2.fillConvexPoly(ids, pix[k], int(k + 1))
    for _ in range(2):
        empty = ids == 0
        if not empty.any():
            break
        grown = ids.copy()
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            shifted = np.roll(np.roll(ids, dy, 0), dx, 1)
            take = empty & (grown == 0) & (shifted > 0)
            grown[take] = shifted[take]
        ids = grown
    return ids, pts


def interpolate(ids, pts, co, nrm):
    h, w = ids.shape
    ys, xs = np.nonzero(ids)
    k = ids[ys, xs] - 1
    a, b, c = pts[k, 0], pts[k, 1], pts[k, 2]
    v0, v1 = b - a, c - a
    p = np.stack([xs, ys], -1).astype(np.float32) - a
    d00 = (v0 * v0).sum(1); d01 = (v0 * v1).sum(1); d11 = (v1 * v1).sum(1)
    d20 = (p * v0).sum(1); d21 = (p * v1).sum(1)
    den = d00 * d11 - d01 * d01
    den[np.abs(den) < 1e-9] = 1e-9
    bv = (d11 * d20 - d01 * d21) / den
    bw = (d00 * d21 - d01 * d20) / den
    bu = 1 - bv - bw
    bary = np.clip(np.stack([bu, bv, bw], -1), 0, 1)
    bary /= np.maximum(bary.sum(1, keepdims=True), 1e-6)
    P = np.zeros((h, w, 3), np.float32)
    N = np.zeros((h, w, 3), np.float32)
    P[ys, xs] = (co[k] * bary[:, :, None]).sum(1)
    n = (nrm[k] * bary[:, :, None]).sum(1)
    N[ys, xs] = n / np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-6)
    return P, N


def masked_blur(values, mask, sigma):
    m = mask.astype(np.float32)
    num = cv2.GaussianBlur(values * m, (0, 0), sigma)
    den = cv2.GaussianBlur(m, (0, 0), sigma)
    return num / np.maximum(den, 1e-4)


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def coverage(P, valid, hem, sleeve_end):
    x, z = P[..., 0], P[..., 2]
    ax = np.abs(x)
    # Crew neck: lower at the throat than at the nape.
    neck_line = np.where(P[..., 1] < 0, NECK_LINE_FRONT, NECK_LINE)
    neck = (ax < NECK_HALF_WIDTH) & (z > neck_line)
    head = z > 0.835
    return valid & (z >= hem) & (ax <= sleeve_end) & ~neck & ~head & (z < 0.83)


def segment_distance(px, pz, a, b):
    ax_, az = a; bx, bz = b
    vx, vz = bx - ax_, bz - az
    t = np.clip(((px - ax_) * vx + (pz - az) * vz) / (vx * vx + vz * vz), 0, 1)
    return np.hypot(px - (ax_ + t * vx), pz - (az + t * vz))


def polyline_distance(px, pz, points):
    d = np.full(px.shape, 1e9, np.float32)
    for a, b in zip(points[:-1], points[1:]):
        d = np.minimum(d, segment_distance(px, pz, a, b))
    return d


def honeycomb(u, v, cell):
    """0 inside a hex cell, rising to 1 on its edges (pointy-top hexagons)."""
    q = (np.sqrt(3) / 3 * u - v / 3) / cell
    r = (2 / 3 * v) / cell
    # Cube rounding.
    x, zc = q, r
    y = -x - zc
    rx, ry, rz = np.round(x), np.round(y), np.round(zc)
    dx, dy, dz = np.abs(rx - x), np.abs(ry - y), np.abs(rz - zc)
    fix_x = (dx > dy) & (dx > dz)
    fix_y = ~fix_x & (dy > dz)
    rx = np.where(fix_x, -ry - rz, rx)
    ry = np.where(fix_y, -rx - rz, ry)
    rz = np.where(~fix_x & ~fix_y, -rx - ry, rz)
    cu = cell * np.sqrt(3) * (rx + rz / 2)
    cv = cell * 1.5 * rz
    lu, lv = np.abs(u - cu), np.abs(v - cv)
    # Hex distance (0 centre, 1 at the edge).
    hd = np.maximum(lu * np.sqrt(3) / 2 + lv / 2, lv) / (cell * np.sqrt(3) / 2)
    return smoothstep(0.72, 0.95, hd)


def king_canvas():
    """Arched KING lettering on the back plane: returns (image, x0, z0, scale)."""
    scale = 4000.0
    x0, x1, z0, z1 = -0.2, 0.2, 0.6, 0.83
    w, h = int((x1 - x0) * scale), int((z1 - z0) * scale)
    canvas = Image.new('L', (w, h), 0)
    font = ImageFont.truetype('C:/Windows/Fonts/impact.ttf', 300)
    text = 'KING'
    radius = 0.42
    peak = 0.772
    letter_h = 0.064
    spacing = 0.054
    for i, ch in enumerate(text):
        offset = (i - (len(text) - 1) / 2) * spacing
        theta = offset / radius
        cx = radius * np.sin(theta)
        cz = peak - radius * (1 - np.cos(theta)) - letter_h / 2
        glyph = Image.new('L', (400, 400), 0)
        ImageDraw.Draw(glyph).text((200, 200), ch, fill=255, font=font, anchor='mm')
        bbox = glyph.getbbox()
        glyph = glyph.crop(bbox)
        gh = letter_h * scale
        gw = glyph.width * gh / glyph.height
        glyph = glyph.resize((max(1, int(gw)), max(1, int(gh))), Image.LANCZOS)
        glyph = glyph.rotate(-np.degrees(theta), resample=Image.BICUBIC, expand=True)
        px = (cx - x0) * scale - glyph.width / 2
        pz = (z1 - cz) * scale - glyph.height / 2
        canvas.paste(255, (int(px), int(pz)), glyph)
    return np.array(canvas, np.float32) / 255.0, x0, z1, scale


def sample_canvas(canvas, x0, ztop, scale, x, z):
    h, w = canvas.shape
    px = ((x - x0) * scale).astype(np.float32)
    pz = ((ztop - z) * scale).astype(np.float32)
    return cv2.remap(canvas, px, pz, cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT, borderValue=0)


def main():
    data = np.load(ROOT / '.tools/player-uvpos.npz')
    uv, co, nrm = data['uv'], data['co'], data['nrm']
    src = np.array(Image.open(SRC).convert('RGB'), np.float32) / 255.0
    size = src.shape[0]
    ids, pts = rasterize(uv, size)
    valid = ids > 0
    P, N = interpolate(ids, pts, co, nrm)
    x, y, z = P[..., 0], P[..., 1], P[..., 2]
    ny = N[..., 1]
    ax = np.abs(x)

    lum = src.mean(2)
    tank = np.array(Image.open(ROOT / '.tools/shirt-mask.png').convert('L')) > 127
    cloth = tank & (lum < 0.14)
    skin = valid & ~cloth
    detail = np.ones_like(lum)
    for region in (cloth, skin):
        base = masked_blur(lum, region, 10)
        detail[region] = np.clip(lum[region] / np.maximum(base[region], 1e-3), 0.7, 1.3)
    # The tank top's edge (cloth vs skin) must not show through the new tops:
    # fade the scan detail out toward that edge so both sides meet at 1.
    edge = (cv2.dilate(cloth.astype(np.uint8), np.ones((3, 3), np.uint8)) > 0) &         (cv2.dilate(skin.astype(np.uint8), np.ones((3, 3), np.uint8)) > 0) & valid
    dist = cv2.distanceTransform((~edge).astype(np.uint8), cv2.DIST_L2, 3)
    detail = 1 + (detail - 1) * smoothstep(2.0, 14.0, dist)
    # Cloth carries brighter weave noise than skin: match its contrast.
    detail = np.where(cloth, 1 + (detail - 1) * 0.6, detail)
    noise = cv2.GaussianBlur(np.random.default_rng(7).normal(0, 1, lum.shape).astype(np.float32), (0, 0), 1.2)
    weave = 1 + 0.035 * noise / max(noise.std(), 1e-6)

    def edge_field(hem, sleeve_end):
        # Signed distance (rest units) to the garment's open edges: hem,
        # sleeve ends and a round crew neck. Paint, shell coverage and the
        # shell's cut line (PlayerTopShell.ClipAtEdges) all come from it,
        # so edges are smooth curves and line up with each other.
        r = np.hypot(x, y - NECK_CENTER_Y)
        collar_z = COLLAR_Z + (y - NECK_CENTER_Y) * COLLAR_SLOPE
        neck = np.maximum(r - NECK_RADIUS, collar_z - z)
        field = np.minimum(np.minimum(z - hem, sleeve_end - ax), neck)
        return np.where(z > 0.845, -1.0, field)

    over_edge = edge_field(OVERSIZED_HEM, OVERSIZED_SLEEVE_END)
    onyx_edge = edge_field(ONYX_HEM, ONYX_SLEEVE_END)
    # Shell coverage reaches past the edge so the cut has both sides; paint
    # stops a margin inside it (the shell's edge strip is drawn plain).
    grown = valid & (over_edge > -0.012)
    # The body under a shell keeps the scan's own texture, so the shell's
    # texture can be painted right across its cut edge: no plain edge strip.
    over = valid & (over_edge >= -0.01)
    onyx_grown = valid & (onyx_edge > -0.012)
    onyx = valid & (onyx_edge >= -0.01)

    # The tank top's straps run up beside the neck, above both new
    # necklines: fill those leftover cloth texels with the surrounding skin.
    # Neck skin tone: inpainting across the atlas pulled in its black
    # background and left grey patches, so straps take this colour instead,
    # with the scan's own skin shading.
    neck_skin = skin & (z > 0.76) & (z < 0.83) & (np.hypot(x, y - 0.01) < 0.07)
    skin_tone = np.median(src[neck_skin], axis=0)

    def without_straps(painted):
        strap = cloth & (z > 0.7) & ~painted
        strap = cv2.dilate(strap.astype(np.uint8), np.ones((3, 3), np.uint8)) > 0
        strap &= valid
        out = src.copy()
        shade = np.clip(0.85 + 0.3 * (detail - 1), 0.75, 1.1)
        out[strap] = skin_tone[None, :] * shade[strap][:, None]
        return out
    over_src = without_straps(over)
    onyx_src = without_straps(onyx)
    # The body under the Oversized/Onyx shells keeps the scan's skin, but its
    # tank-top straps beside the neck would show through the collar.
    # Seen through the collar, cuffs or a fold, the body must not show the
    # tank top: all of its cloth becomes skin on this texture.
    under = src.copy()
    tank_cloth = cloth & valid & (z > TANK_HEM - 0.004)
    under[tank_cloth] = skin_tone[None, :] * np.clip(0.85 + 0.3 * (detail - 1), 0.75, 1.1)[tank_cloth][:, None]
    Image.fromarray(np.uint8(np.clip(under, 0, 1) * 255)).save(OUT / 'under_shell_body.png')

    # ---------------------------------------------------------- oversized
    king, kx0, kz1, kscale = king_canvas()
    back = (ny > 0.12) & (ax < 0.2) & (z > 0.6)
    # Seen from behind, screen-right is +x in this rest pose: sample mirrored.
    king_mask = np.where(back & over, sample_canvas(king, kx0, kz1, kscale, -x, z), 0)
    # Only the back panel: shoulder tops facing up/back must not catch the
    # letters' ends (they showed as grey streaks from the front).
    king_mask = np.clip(king_mask * smoothstep(0.35, 0.55, ny) * (z < 0.785), 0, 1)
    # Loose cloth: soft folds only, a touch darker at the hem and armpits.
    # Loose cloth stands off the body: no muscle or tank-top shading from
    # the scan, only a soft weave and broad folds.
    # Folds are a function of the rest position, so they run on across the
    # atlas seams: soft vertical drapes on the torso, rings on the sleeves.
    folds = 1 + 0.035 * np.sin(x * 140 + 2.5 * np.sin(z * 31)) + 0.025 * np.sin((x - y) * 90 - z * 17)         + 0.03 * np.where(ax > 0.15, np.sin(ax * 260), 0)
    drape = folds * (1 + (weave - 1) * 0.35)
    drape *= 1 - 0.08 * smoothstep(0.56, 0.5, z)
    # Hemmed edges: a slightly darker 8 mm turn-up along every open edge.
    hem_band = over & (over_edge < 0.008)
    drape = np.where(hem_band, drape * 0.86, drape)
    # Atlas gutters next to painted texels take the shirt colour too, so
    # mipmaps and bilinear filtering never pull in the old texels (grey
    # specks on light shirts).
    def gutter(painted, width=6):
        grown = cv2.dilate(painted.astype(np.uint8), np.ones((3, 3), np.uint8), iterations=width) > 0
        return grown & ~valid

    over_gutter = gutter(over)
    onyx_gutter = gutter(onyx)
    for name, color in COLORS.items():
        out = over_src.copy()
        out[over_gutter] = np.array(color, np.float32) * 0.92
        fabric = np.array(color, np.float32)[None, None, :] * (0.92 * drape)[..., None]
        text = KING_GREY[None, None, :] * (0.96 * folds)[..., None]
        shirt = fabric * (1 - king_mask[..., None]) + text * king_mask[..., None]
        out[over] = shirt[over]
        Image.fromarray(np.uint8(np.clip(out, 0, 1) * 255)).save(OUT / f'oversized_{name}.png')

    # ---------------------------------------------------------- onyx
    front = ny < -0.1
    backside = ny > 0.1
    torso = ax <= TORSO_HALF_WIDTH
    sleeve = ax > TORSO_HALF_WIDTH
    # Chest V panel (front) and inverted triangle (back).
    v_left = [(-0.118, 0.775), (-0.085, 0.69), (-0.04, 0.6), (0.0, 0.575)]
    v_right = [(-px, pz) for px, pz in v_left]
    d_v = np.minimum(polyline_distance(x, z, v_left), polyline_distance(x, z, v_right))
    edge_x = np.interp(z, [0.575, 0.6, 0.69, 0.775], [0.0, 0.04, 0.085, 0.118])
    inside_v = (ax < edge_x) & (z > 0.575) & (z < 0.785)
    tri_left = [(-0.112, 0.786), (0.0, 0.62)]
    d_tri = np.minimum(polyline_distance(x, z, tri_left), polyline_distance(x, z, [(0.112, 0.786), (0.0, 0.62)]))
    inside_tri = (ax < (z - 0.62) / (0.786 - 0.62) * 0.112) & (z > 0.62)
    # Raglan seams: neck base to underarm, front and back.
    rag = [(NECK_HALF_WIDTH - 0.004, 0.792), (0.1, 0.765), (0.128, 0.712)]
    d_rag = np.minimum(polyline_distance(ax, z, rag), 1e9)
    # Lower front: the panel lines carry on down the obliques.
    oblique = [(0.06, 0.6), (0.085, 0.53)]
    d_obl = polyline_distance(ax, z, oblique)
    hexes = honeycomb(x, z, 0.0055)
    line_w = 0.0022
    accent_line = np.zeros_like(lum)
    accent_line = np.maximum(accent_line, np.where(front & torso, smoothstep(line_w, line_w * 0.4, d_v), 0))
    accent_line = np.maximum(accent_line, np.where(front & torso, smoothstep(line_w, line_w * 0.4, d_obl), 0))
    accent_line = np.maximum(accent_line, np.where(backside & torso, smoothstep(line_w, line_w * 0.4, d_tri), 0))
    accent_line = np.maximum(accent_line, np.where(~sleeve | (ax < 0.14), smoothstep(line_w, line_w * 0.4, d_rag), 0))
    cuff = sleeve & (ax > ONYX_SLEEVE_END - ONYX_MARGIN - 0.011) & (ax < ONYX_SLEEVE_END - ONYX_MARGIN - 0.006)
    neck_r = np.hypot(x, y - NECK_CENTER_Y)
    neck_rib = (z > 0.74) & (neck_r < NECK_RADIUS + 0.03) & \
        (onyx_edge > ONYX_MARGIN + 0.004) & (onyx_edge < ONYX_MARGIN + 0.0085)
    cuff = cuff | neck_rib
    accent_line = np.maximum(accent_line, cuff.astype(np.float32))
    panel = ((front & torso & inside_v) | (backside & torso & inside_tri)).astype(np.float32)
    panel = cv2.GaussianBlur(panel, (0, 0), 0.6)
    knit = panel * (0.25 + 0.75 * hexes) * 0.55
    # No scan shading: it carried the tank top's neckline. The shell's own
    # geometry gives the muscle definition under the lights.
    tight = weave
    for name, accent in ONYX_ACCENT.items():
        out = onyx_src.copy()
        out[onyx_gutter] = ONYX_BASE
        acc = np.array(accent, np.float32)[None, None, :]
        base = ONYX_BASE[None, None, :] * np.ones_like(src)
        mix = np.clip(np.maximum(knit, accent_line * 0.9), 0, 1)[..., None]
        shirt = (base * (1 - mix) + acc * mix) * tight[..., None]
        out[onyx] = shirt[onyx]
        Image.fromarray(np.uint8(np.clip(out, 0, 1) * 255)).save(OUT / f'onyx_{name}.png')

    # ---------------------------------------------------------- shell
    inflate = 0.010 + 0.014 * smoothstep(0.74, 0.52, z)
    sleeve_inflate = 0.016 + 0.016 * smoothstep(0.15, 0.27, ax)
    inflate = np.where(ax > 0.12, np.maximum(inflate * (1 - smoothstep(0.12, 0.16, ax)),
                                             sleeve_inflate * smoothstep(0.11, 0.15, ax)), inflate)
    # Keep the collar close to the neck.
    inflate *= 1 - 0.7 * smoothstep(0.765, 0.792, z) * (ax < 0.1)
    def write_mask(name, cover, amount, painted, edge):
        shell = np.zeros((size, size, 4), np.uint8)
        shell[..., 0] = np.where(cover, 255, 0)
        shell[..., 1] = np.uint8(np.clip(amount / INFLATE_RANGE, 0, 1) * 255) * cover
        shell[..., 2] = np.where(cv2.erode(painted.astype(np.uint8), np.ones((3, 3), np.uint8)) > 0, 255, 0)
        shell[..., 3] = np.uint8(np.clip(128 + edge / EDGE_RANGE * 127, 0, 255))
        ok, png = cv2.imencode('.png', shell[..., [2, 1, 0, 3]])
        (OUT / name).write_bytes(png.tobytes())

    write_mask('oversized_shell.bytes', grown, inflate, over, over_edge)
    # Onyx hugs the skin at its edges (no step where it ends) and stands a
    # few mm off it inside, enough to cover the tank top's sculpted ridges.
    onyx_inflate = ONYX_INFLATE_EDGE + (ONYX_INFLATE - ONYX_INFLATE_EDGE) * smoothstep(0.0, 0.006, onyx_edge)
    write_mask('onyx_shell.bytes', onyx_grown, onyx_inflate, onyx, onyx_edge)

    DEBUG.mkdir(parents=True, exist_ok=True)
    Image.fromarray(np.uint8(over) * 255).save(DEBUG / 'oversized-mask.png')
    Image.fromarray(np.uint8(onyx) * 255).save(DEBUG / 'onyx-mask.png')
    Image.fromarray(np.uint8(np.clip(king_mask, 0, 1) * 255)).save(DEBUG / 'king-mask.png')
    Image.fromarray(np.uint8(king * 255)).save(DEBUG / 'king-canvas.png')
    report = {
        'oversizedTexels': int(over.sum()), 'onyxTexels': int(onyx.sum()),
        'kingTexels': int((king_mask > 0.5).sum()), 'tankTexels': int(tank.sum()),
        'restDiagonal': float(np.linalg.norm(co.reshape(-1, 3).max(0) - co.reshape(-1, 3).min(0))),
        'restHeight': float(co[..., 2].max() - co[..., 2].min()),
    }
    (DEBUG / 'report.json').write_text(json.dumps(report, indent=1))
    print('TOPS_OK', json.dumps(report))


if __name__ == '__main__':
    main()
