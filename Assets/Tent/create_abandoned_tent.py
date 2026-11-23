"""Abandoned / homeless dome tent for GymChaos, low poly, real interior.

Run headless:
    blender -b --factory-startup -P Assets/Tent/create_abandoned_tent.py

Outputs:
- Assets/BodyBuilders/outside/abandoned_tent.glb (materials + textures embedded)
- Assets/Tent/abandoned_tent.blend
- Assets/Tent/textures/*.png (the generated textures, kept for reference)

Authored in metres, Z up, tent door on the -Y side (glTF +Z). Origin is the
centre of the floor at ground level. Shell is a thin solidified canvas with a
dark liner on the inside, torn holes are real holes, the door flaps are drawn
back, and the interior holds a foam mat, sleeping bag, blanket, bundled
clothes, backpack, bottles, cans and cardboard.
"""

import math
import os

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector, noise
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT_GLB = os.path.join(ROOT, "Assets", "BodyBuilders", "outside", "abandoned_tent.glb")
OUT_BLEND = os.path.join(HERE, "abandoned_tent.blend")
TEX_DIR = os.path.join(HERE, "textures")

CENTER = Vector((0.0, 0.0, 0.6))

# (z, half width x, half depth y) of the dome rings from ground to top.
LEVELS = [
    (0.00, 1.30, 1.15),
    (0.50, 1.16, 1.03),
    (1.00, 0.94, 0.83),
    (1.32, 0.72, 0.63),
    (1.50, 0.50, 0.44),
]
TOP = len(LEVELS) - 1
CAP_Z = 1.58
CORNERS = [(-1, -1), (1, -1), (1, 1), (-1, 1)]
OUT_N = [Vector((0, -1, 0)), Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((-1, 0, 0))]
SIDE_COLS = 6
FLAP_COLS = 4
GAP = [0.50, 0.40, 0.26, 0.09, 0.0]  # half width of the door opening per ring
FLAP_FOLD = [1.0, 1.0, 1.0, 0.7, 0.0]
SHELL_THICKNESS = 0.018

MAT_OLIVE, MAT_BLUE, MAT_BEIGE, MAT_LINER = 0, 1, 2, 3
SIDE_MATERIAL = {0: MAT_BEIGE, 1: MAT_BLUE, 2: MAT_OLIVE, 3: MAT_OLIVE}

# ---------------------------------------------------------------------------
# Procedural textures (numpy, tileable)
# ---------------------------------------------------------------------------


def vnoise(h, w, ch, cw, rng):
    grid = rng.random((ch, cw))
    ys = np.arange(h) / h * ch
    xs = np.arange(w) / w * cw
    y0 = np.floor(ys).astype(int)
    x0 = np.floor(xs).astype(int)
    fy = ys - y0
    fx = xs - x0
    fy = fy * fy * (3 - 2 * fy)
    fx = fx * fx * (3 - 2 * fx)
    y1 = (y0 + 1) % ch
    x1 = (x0 + 1) % cw
    a = grid[y0][:, x0]
    b = grid[y0][:, x1]
    c = grid[y1][:, x0]
    d = grid[y1][:, x1]
    top = a + (b - a) * fx[None, :]
    bot = c + (d - c) * fx[None, :]
    return top + (bot - top) * fy[:, None]


def fbm(size, cells, octaves, rng, aniso=(1, 1)):
    out = np.zeros((size, size))
    amp = 1.0
    total = 0.0
    for o in range(octaves):
        ch = max(1, int(cells * aniso[0] * 2 ** o))
        cw = max(1, int(cells * aniso[1] * 2 ** o))
        out += amp * vnoise(size, size, ch, cw, rng)
        total += amp
        amp *= 0.5
    return out / total


def stain_mask(size, rng, cells, lo, gain):
    return np.clip((fbm(size, cells, 3, rng) - lo) * gain, 0, 1)


def canvas_texture(base, seed, size=1024, dirt=1.0, mold=True):
    rng = np.random.default_rng(seed)
    base = np.array(base, dtype=float)
    low = fbm(size, 3, 4, rng)
    fine = fbm(size, 160, 2, rng)
    col = base[None, None, :] * (0.62 + 0.8 * low)[..., None]
    col *= (0.9 + 0.2 * fine)[..., None]
    period = np.sin(np.arange(size) * 2 * np.pi / 4)
    weave = 0.035 * period[None, :] * period[:, None]
    col += weave[..., None]
    blotch = stain_mask(size, rng, 5, 0.52, 4.0) * dirt
    col = col * (1 - 0.7 * blotch[..., None]) + np.array([0.20, 0.15, 0.09]) * 0.7 * blotch[..., None]
    streak = np.clip((fbm(size, 2, 3, rng, aniso=(1, 18)) - 0.60) * 4, 0, 1) * dirt * 0.7
    col *= (1 - 0.45 * streak)[..., None]
    if mold:
        m = np.clip((fbm(size, 22, 3, rng) - 0.6) * 8, 0, 1) * (low > 0.45)
        col = col * (1 - 0.3 * m[..., None]) + np.array([0.15, 0.25, 0.13]) * 0.3 * m[..., None]
    bleach = np.clip((low - 0.6) * 5, 0, 1)
    col += (0.85 - col) * 0.22 * bleach[..., None]
    # bottom hem mud: darker toward v=0 side tiles every metre, reads as dirt line
    return np.clip(col, 0, 1)


def liner_texture(seed, size=512):
    rng = np.random.default_rng(seed)
    low = fbm(size, 3, 4, rng)
    col = np.array([0.26, 0.27, 0.22])[None, None, :] * (0.7 + 0.6 * low)[..., None]
    m = np.clip((fbm(size, 18, 3, rng) - 0.58) * 7, 0, 1)
    col = col * (1 - 0.3 * m[..., None]) + np.array([0.12, 0.19, 0.10]) * 0.3 * m[..., None]
    streak = np.clip((fbm(size, 2, 3, rng, aniso=(1, 14)) - 0.6) * 4, 0, 1)
    col *= (1 - 0.25 * streak)[..., None]
    return np.clip(col, 0, 1)


def ground_texture(seed, size=512):
    rng = np.random.default_rng(seed)
    low = fbm(size, 4, 4, rng)
    col = np.array([0.20, 0.22, 0.22])[None, None, :] * (0.75 + 0.5 * low)[..., None]
    crease = np.abs(fbm(size, 6, 2, rng) - 0.5)
    col *= (0.85 + 0.6 * np.clip(crease * 3, 0, 0.5))[..., None]
    mud = stain_mask(size, rng, 5, 0.5, 4.0)
    col = col * (1 - 0.7 * mud[..., None]) + np.array([0.22, 0.16, 0.10]) * 0.7 * mud[..., None]
    return np.clip(col, 0, 1)


def mat_texture(seed, size=512):
    """Olive foam sleeping mat with ridge lines."""
    rng = np.random.default_rng(seed)
    low = fbm(size, 4, 3, rng)
    col = np.array([0.30, 0.34, 0.20])[None, None, :] * (0.7 + 0.5 * low)[..., None]
    ridge = (0.82 + 0.18 * np.sin(np.arange(size) * 2 * np.pi / 32))[:, None]
    col *= ridge[..., None]
    dirt = stain_mask(size, rng, 5, 0.5, 4.0)
    col = col * (1 - 0.6 * dirt[..., None]) + np.array([0.20, 0.15, 0.09]) * 0.6 * dirt[..., None]
    return np.clip(col, 0, 1)


def bag_texture(seed, size=512):
    """Faded navy sleeping bag with quilted stitch lines."""
    rng = np.random.default_rng(seed)
    low = fbm(size, 4, 3, rng)
    col = np.array([0.10, 0.14, 0.26])[None, None, :] * (0.7 + 0.7 * low)[..., None]
    stitch = (np.sin(np.arange(size) * 2 * np.pi / 64) < -0.96)
    col *= np.where(stitch[:, None], 0.55, 1.0)[..., None]
    dirt = stain_mask(size, rng, 6, 0.52, 4.0)
    col = col * (1 - 0.7 * dirt[..., None]) + np.array([0.19, 0.14, 0.09]) * 0.7 * dirt[..., None]
    return np.clip(col, 0, 1)


def blanket_texture(seed, size=512):
    """Worn red-grey plaid blanket."""
    rng = np.random.default_rng(seed)
    x = np.arange(size) / size
    stripes_x = 0.5 + 0.5 * np.sin(x * 2 * np.pi * 8)
    stripes_y = 0.5 + 0.5 * np.sin(x * 2 * np.pi * 8)
    plaid = 0.5 * stripes_x[None, :] + 0.5 * stripes_y[:, None]
    red = np.array([0.42, 0.10, 0.08])
    grey = np.array([0.30, 0.30, 0.28])
    col = red[None, None, :] * plaid[..., None] + grey[None, None, :] * (1 - plaid[..., None])
    col *= (0.75 + 0.5 * fbm(size, 5, 4, rng))[..., None]
    dirt = stain_mask(size, rng, 6, 0.5, 4.0)
    col = col * (1 - 0.6 * dirt[..., None]) + np.array([0.20, 0.15, 0.10]) * 0.6 * dirt[..., None]
    return np.clip(col, 0, 1)


def clothes_texture(seed, size=512):
    """Grey hoodie fabric."""
    rng = np.random.default_rng(seed)
    low = fbm(size, 5, 4, rng)
    fine = fbm(size, 120, 1, rng)
    col = np.array([0.30, 0.31, 0.32])[None, None, :] * (0.65 + 0.7 * low)[..., None] * (0.9 + 0.2 * fine)[..., None]
    dirt = stain_mask(size, rng, 6, 0.5, 4.0)
    col = col * (1 - 0.5 * dirt[..., None]) + np.array([0.20, 0.15, 0.10]) * 0.5 * dirt[..., None]
    return np.clip(col, 0, 1)


def pack_texture(seed, size=512):
    """Faded orange nylon backpack."""
    rng = np.random.default_rng(seed)
    low = fbm(size, 4, 4, rng)
    col = np.array([0.62, 0.30, 0.10])[None, None, :] * (0.6 + 0.7 * low)[..., None]
    period = np.sin(np.arange(size) * 2 * np.pi / 3)
    col += (0.03 * period[None, :] * period[:, None])[..., None]
    dirt = stain_mask(size, rng, 5, 0.48, 4.0)
    col = col * (1 - 0.65 * dirt[..., None]) + np.array([0.17, 0.12, 0.08]) * 0.65 * dirt[..., None]
    return np.clip(col, 0, 1)


def wood_texture(seed, size=512):
    rng = np.random.default_rng(seed)
    grain = fbm(size, 3, 4, rng, aniso=(1, 24))
    col = np.array([0.30, 0.19, 0.10])[None, None, :] * (0.55 + 0.9 * grain)[..., None]
    col *= (0.85 + 0.3 * fbm(size, 80, 1, rng))[..., None]
    return np.clip(col, 0, 1)


def rock_texture(seed, size=512):
    rng = np.random.default_rng(seed)
    low = fbm(size, 6, 5, rng)
    col = np.array([0.40, 0.40, 0.39])[None, None, :] * (0.55 + 0.9 * low)[..., None]
    lichen = np.clip((fbm(size, 14, 3, rng) - 0.62) * 8, 0, 1)
    col = col * (1 - 0.35 * lichen[..., None]) + np.array([0.30, 0.34, 0.18]) * 0.35 * lichen[..., None]
    return np.clip(col, 0, 1)


def metal_texture(seed, size=512):
    rng = np.random.default_rng(seed)
    low = fbm(size, 5, 4, rng)
    col = np.array([0.10, 0.10, 0.11])[None, None, :] * (0.7 + 0.8 * low)[..., None]
    rust = np.clip((fbm(size, 10, 4, rng) - 0.55) * 6, 0, 1)
    col = col * (1 - 0.7 * rust[..., None]) + np.array([0.33, 0.15, 0.06]) * 0.7 * rust[..., None]
    return np.clip(col, 0, 1)


def rope_texture(seed, size=256):
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:size, 0:size] / size
    twist = 0.5 + 0.5 * np.sin((xx + yy) * 2 * np.pi * 24)
    col = np.array([0.30, 0.22, 0.14])[None, None, :] * (0.55 + 0.7 * twist)[..., None]
    col *= (0.8 + 0.4 * fbm(size, 20, 2, rng))[..., None]
    return np.clip(col, 0, 1)


def tape_texture(seed, size=256):
    rng = np.random.default_rng(seed)
    low = fbm(size, 4, 3, rng)
    col = np.array([0.30, 0.30, 0.31])[None, None, :] * (0.7 + 0.6 * low)[..., None]
    fibre = (0.9 + 0.1 * np.sin(np.arange(size) * 2 * np.pi / 6))[None, :]
    col *= fibre[..., None]
    dirt = stain_mask(size, rng, 5, 0.5, 4.0)
    col = col * (1 - 0.5 * dirt[..., None]) + np.array([0.18, 0.14, 0.10]) * 0.5 * dirt[..., None]
    return np.clip(col, 0, 1)


def cardboard_texture(seed, size=512):
    rng = np.random.default_rng(seed)
    low = fbm(size, 4, 4, rng)
    col = np.array([0.52, 0.38, 0.22])[None, None, :] * (0.65 + 0.6 * low)[..., None]
    flute = (0.9 + 0.1 * np.sin(np.arange(size) * 2 * np.pi / 10))[None, :]
    col *= flute[..., None]
    wet = stain_mask(size, rng, 4, 0.5, 3.0)
    col = col * (1 - 0.55 * wet[..., None]) + np.array([0.22, 0.16, 0.10]) * 0.55 * wet[..., None]
    return np.clip(col, 0, 1)


def can_texture(seed, size=256):
    rng = np.random.default_rng(seed)
    yy = np.arange(size) / size
    band = (0.5 + 0.5 * np.sin(yy * 2 * np.pi * 3))[:, None]
    red = np.array([0.55, 0.08, 0.07])
    silver = np.array([0.55, 0.56, 0.58])
    col = red[None, None, :] * band[..., None] + silver[None, None, :] * (1 - band[..., None])
    col = np.broadcast_to(col, (size, size, 3)).copy()
    col *= (0.7 + 0.5 * fbm(size, 6, 3, rng))[..., None]
    rust = np.clip((fbm(size, 8, 3, rng) - 0.6) * 6, 0, 1)
    col = col * (1 - 0.5 * rust[..., None]) + np.array([0.30, 0.14, 0.06]) * 0.5 * rust[..., None]
    return np.clip(col, 0, 1)


def glass_texture(seed, size=256):
    rng = np.random.default_rng(seed)
    low = fbm(size, 4, 3, rng)
    col = np.array([0.08, 0.20, 0.09])[None, None, :] * (0.6 + 0.8 * low)[..., None]
    return np.clip(col, 0, 1)


TEXTURES = {
    "canvas_olive": (lambda: canvas_texture((0.36, 0.38, 0.27), 11, dirt=0.55), 1024, 0.95, 0.0),
    "canvas_blue": (lambda: canvas_texture((0.20, 0.30, 0.42), 12, dirt=0.6, mold=False), 1024, 0.95, 0.0),
    "canvas_beige": (lambda: canvas_texture((0.52, 0.46, 0.32), 13, dirt=0.8, mold=False), 1024, 0.95, 0.0),
    "liner": (lambda: liner_texture(14), 512, 1.0, 0.0),
    "groundsheet": (lambda: ground_texture(15), 512, 0.9, 0.0),
    "foam_mat": (lambda: mat_texture(16), 512, 0.95, 0.0),
    "sleeping_bag": (lambda: bag_texture(17), 512, 0.9, 0.0),
    "blanket": (lambda: blanket_texture(18), 512, 1.0, 0.0),
    "clothes": (lambda: clothes_texture(19), 512, 1.0, 0.0),
    "backpack": (lambda: pack_texture(20), 512, 0.85, 0.0),
    "wood": (lambda: wood_texture(21), 512, 0.9, 0.0),
    "rock": (lambda: rock_texture(22), 512, 0.95, 0.0),
    "pole_metal": (lambda: metal_texture(23), 512, 0.55, 0.6),
    "rope": (lambda: rope_texture(24), 256, 1.0, 0.0),
    "tape": (lambda: tape_texture(25), 256, 0.6, 0.0),
    "cardboard": (lambda: cardboard_texture(26), 512, 1.0, 0.0),
    "can": (lambda: can_texture(27), 256, 0.45, 0.6),
    "glass_bottle": (lambda: glass_texture(28), 256, 0.25, 0.0),
}


def make_image(name, arr):
    size = arr.shape[0]
    img = bpy.data.images.new(name, size, size, alpha=False)
    rgba = np.ones((size, size, 4), dtype=np.float32)
    rgba[..., :3] = arr
    img.pixels.foreach_set(rgba.ravel())
    os.makedirs(TEX_DIR, exist_ok=True)
    img.filepath_raw = os.path.join(TEX_DIR, name + ".png")
    img.file_format = "PNG"
    img.save()
    img.pack()
    return img


MATERIALS = {}


def build_materials():
    for name, (gen, _size, rough, metal) in TEXTURES.items():
        img = make_image(name, gen())
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        nodes = mat.node_tree.nodes
        links = mat.node_tree.links
        bsdf = nodes["Principled BSDF"]
        tex = nodes.new("ShaderNodeTexImage")
        tex.image = img
        links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        bsdf.inputs["Roughness"].default_value = rough
        bsdf.inputs["Metallic"].default_value = metal
        if name.startswith("canvas") or name == "liner":
            mat.use_backface_culling = False
        MATERIALS[name] = mat


# ---------------------------------------------------------------------------
# Mesh helpers
# ---------------------------------------------------------------------------


def uv_box(bm, scale=1.0):
    uv = bm.loops.layers.uv.verify()
    for face in bm.faces:
        n = face.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for loop in face.loops:
            p = loop.vert.co
            if ax == 0:
                u, v = p.y, p.z
            elif ax == 1:
                u, v = p.x, p.z
            else:
                u, v = p.x, p.y
            loop[uv].uv = (u / scale, v / scale)


def new_object(name, bm, mat_names, uv_scale=1.0):
    bm.normal_update()
    uv_box(bm, uv_scale)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in mat_names:
        me.materials.append(MATERIALS[m])
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def box(bm, center, size, mi, rot_z=0.0, taper=None):
    m = Matrix.Translation(center) @ Matrix.Rotation(rot_z, 4, "Z") @ Matrix.Diagonal((size[0], size[1], size[2], 1.0))
    res = bmesh.ops.create_cube(bm, size=1.0, matrix=m)
    if taper is not None:
        # taper: scale of the top vertices in x/y relative to the bottom
        zmax = max(v.co.z for v in res["verts"])
        for v in res["verts"]:
            if v.co.z > zmax - 1e-4:
                dx = v.co.x - center.x
                dy = v.co.y - center.y
                rel = Matrix.Rotation(-rot_z, 2)
                # taper in local frame is approximated in world axes (small angles)
                v.co.x = center.x + dx * taper
                v.co.y = center.y + dy * taper
    for v in res["verts"]:
        for f in v.link_faces:
            f.material_index = mi
    return res["verts"]


def blob(bm, center, radii, mi, rot_z=0.0, seed=0, lump=0.0, segments=(10, 6), flat_bottom=None):
    m = Matrix.Translation(center) @ Matrix.Rotation(rot_z, 4, "Z") @ Matrix.Diagonal((radii[0], radii[1], radii[2], 1.0))
    res = bmesh.ops.create_uvsphere(bm, u_segments=segments[0], v_segments=segments[1], radius=1.0, matrix=m)
    for v in res["verts"]:
        if lump:
            p = v.co * 2.1 + Vector((seed, seed * 0.7, seed * 1.3))
            v.co += Vector((noise.noise(p), noise.noise(p + Vector((7, 3, 1))), noise.noise(p + Vector((2, 9, 5))))) * lump
        if flat_bottom is not None and v.co.z < flat_bottom:
            v.co.z = flat_bottom
    for v in res["verts"]:
        for f in v.link_faces:
            f.material_index = mi
    return res["verts"]


def tube(bm, a, b, radius, mi, segments=6):
    a = Vector(a)
    b = Vector(b)
    d = b - a
    length = d.length
    if length < 1e-5:
        return
    rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix().to_4x4()
    m = Matrix.Translation((a + b) * 0.5) @ rot
    res = bmesh.ops.create_cone(
        bm, cap_ends=True, cap_tris=False, segments=segments, radius1=radius, radius2=radius, depth=length, matrix=m)
    for v in res["verts"]:
        for f in v.link_faces:
            f.material_index = mi


def polyline_tube(bm, points, radius, mi, segments=6):
    for a, b in zip(points[:-1], points[1:]):
        tube(bm, a, b, radius, mi, segments)


# ---------------------------------------------------------------------------
# Canvas shell
# ---------------------------------------------------------------------------


def corner(level, k):
    z, hx, hy = LEVELS[level]
    sx, sy = CORNERS[k]
    return Vector((sx * hx, sy * hy, z))


def surf(level, side, t):
    a = corner(level, side)
    b = corner(level, (side + 1) % 4)
    p = a + (b - a) * t
    w = 4.0 * t * (1.0 - t)
    p += OUT_N[side] * (-0.035 * w)
    if level > 0 and w > 0:
        q = p * 2.7
        j = Vector((
            noise.noise(q + Vector((5, 1, 3))),
            noise.noise(q + Vector((9, 7, 2))),
            noise.noise(q + Vector((1, 4, 8)))))
        p += Vector((j.x * 0.035, j.y * 0.035, j.z * 0.025)) * w
    return p


class Shell:
    def __init__(self):
        self.bm = bmesh.new()
        self.cache = {}

    def vert(self, p):
        key = (round(p.x, 5), round(p.y, 5), round(p.z, 5))
        if key not in self.cache:
            self.cache[key] = self.bm.verts.new(p)
        return self.cache[key]

    def tri(self, pa, pb, pc, mi):
        a, b, c = self.vert(pa), self.vert(pb), self.vert(pc)
        if a is b or b is c or a is c:
            return
        n = (pb - pa).cross(pc - pa)
        centroid = (pa + pb + pc) / 3.0
        if n.dot(centroid - CENTER) < 0:
            b, c = c, b
        try:
            f = self.bm.faces.new((a, b, c))
            f.material_index = mi
        except ValueError:
            pass

    def quad_grid(self, grid, mi):
        rows = len(grid)
        cols = len(grid[0])
        for j in range(rows - 1):
            for i in range(cols - 1):
                p00, p10 = grid[j][i], grid[j][i + 1]
                p01, p11 = grid[j + 1][i], grid[j + 1][i + 1]
                if (i + j) % 2 == 0:
                    self.tri(p00, p10, p11, mi)
                    self.tri(p00, p11, p01, mi)
                else:
                    self.tri(p00, p10, p01, mi)
                    self.tri(p10, p11, p01, mi)


def flap_grid(side_right):
    grid = []
    for level in range(LEVELS.__len__()):
        hx = LEVELS[level][1]
        tg = (hx - GAP[level]) / (2 * hx)
        row = []
        for i in range(FLAP_COLS + 1):
            if side_right:
                t = (1.0 - tg) + tg * i / FLAP_COLS
                amount = ((FLAP_COLS - i) / FLAP_COLS) ** 1.3
            else:
                t = tg * i / FLAP_COLS
                amount = (i / FLAP_COLS) ** 1.3
            p = surf(level, 0, t)
            fold = FLAP_FOLD[level] * amount
            if fold > 0:
                q = p * 3.3
                n = 0.55 + 0.45 * noise.noise(q + Vector((3, 5, 7)))
                p.y -= 0.24 * fold * n
                p.x += (0.20 if side_right else -0.20) * fold
                p.z += 0.05 * fold * noise.noise(q + Vector((8, 2, 6)))
            row.append(p)
        grid.append(row)
    return grid


def build_shell():
    sh = Shell()
    # side walls: right (1), back (2), left (3)
    for side in (1, 2, 3):
        grid = [[surf(l, side, i / SIDE_COLS) for i in range(SIDE_COLS + 1)] for l in range(len(LEVELS))]
        sh.quad_grid(grid, SIDE_MATERIAL[side])
    # front door flaps
    left = flap_grid(False)
    right = flap_grid(True)
    sh.quad_grid(left, MAT_BEIGE)
    sh.quad_grid(right, MAT_BEIGE)
    # roof cap fan over the top ring
    perimeter = []
    for p in right[TOP][::-1]:
        pass
    perimeter.extend(left[TOP])
    perimeter.extend(right[TOP][1:])  # front, left to right
    for side in (1, 2, 3):
        pts = [surf(TOP, side, i / SIDE_COLS) for i in range(1, SIDE_COLS + 1)]
        perimeter.extend(pts)
    # perimeter[0] is front-left corner; it closes back on itself via left side end
    cap = Vector((0.0, 0.0, CAP_Z))
    for a, b in zip(perimeter, perimeter[1:]):
        sh.tri(a, b, cap, MAT_OLIVE)
    sh.tri(perimeter[-1], perimeter[0], cap, MAT_OLIVE)
    return sh


def punch_holes(bm, holes):
    bm.faces.ensure_lookup_table()
    for pos, radius in holes:
        pos = Vector(pos)
        ranked = sorted(bm.faces, key=lambda f: (f.calc_center_median() - pos).length)
        doomed = [ranked[0]] + [f for f in ranked[1:5] if (f.calc_center_median() - pos).length < radius]
        bmesh.ops.delete(bm, geom=[f for f in doomed if f.is_valid], context="FACES")
        bm.faces.ensure_lookup_table()


HOLES = [
    ((0.30, 0.10, 1.57), 0.24),      # roof
    ((-0.85, -0.90, 0.90), 0.14),    # left flap, mid
    ((-1.00, -1.00, 0.38), 0.14),    # left flap, low
    ((1.00, 0.50, 0.80), 0.24),      # blue panel
    ((0.22, 0.80, 1.00), 0.21),      # back wall
    ((-1.00, 0.32, 0.90), 0.21),     # left wall
]


def shell_object():
    sh = build_shell()
    bm = sh.bm
    punch_holes(bm, HOLES)
    bvh = BVHTree.FromBMesh(bm)
    bm.normal_update()
    me = bpy.data.meshes.new("Tent_Canvas")
    bm.to_mesh(me)
    uv_bm = None
    for m in ("canvas_olive", "canvas_blue", "canvas_beige", "liner"):
        me.materials.append(MATERIALS[m])
    obj = bpy.data.objects.new("Tent_Canvas", me)
    bpy.context.scene.collection.objects.link(obj)
    mod = obj.modifiers.new("Shell", "SOLIDIFY")
    mod.thickness = SHELL_THICKNESS
    mod.offset = 0.0
    mod.use_even_offset = True
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.modifier_apply(modifier=mod.name)
    obj.select_set(False)
    # inner faces get the dark liner
    for poly in obj.data.polygons:
        d = poly.center - CENTER
        if d.length > 1e-6 and poly.normal.dot(d.normalized()) < -0.35:
            poly.material_index = MAT_LINER
    # UVs
    bm2 = bmesh.new()
    bm2.from_mesh(obj.data)
    bm2.normal_update()
    uv_box(bm2, 1.0)
    bm2.to_mesh(obj.data)
    bm2.free()
    bm.free()
    return obj, bvh


# ---------------------------------------------------------------------------
# Details
# ---------------------------------------------------------------------------


def snap_grid_patch(bvh, y0, y1, z0, z1, n=3):
    pts = []
    for j in range(n + 1):
        row = []
        for i in range(n + 1):
            y = y0 + (y1 - y0) * i / n
            z = z0 + (z1 - z0) * j / n
            hit = bvh.ray_cast(Vector((3.0, y, z)), Vector((-1.0, 0.0, 0.0)), 6.0)
            if hit[0] is None or hit[0].x < 0.5:
                return None
            row.append(hit[0] + hit[1] * 0.016)
        pts.append(row)
    return pts


def tape_object(bvh):
    bm = bmesh.new()
    rects = [
        (-0.30, -0.18, 0.26, 0.96),    # vertical strip
        (-0.62, 0.10, 0.54, 0.66),     # horizontal strip
        (-0.56, -0.38, 0.30, 0.46),    # small square patch
        (-0.12, 0.08, 0.72, 0.90),     # second patch
    ]
    for y0, y1, z0, z1 in rects:
        pts = snap_grid_patch(bvh, y0, y1, z0, z1)
        if pts is None:
            continue
        vs = [[bm.verts.new(p) for p in row] for row in pts]
        for j in range(len(vs) - 1):
            for i in range(len(vs[0]) - 1):
                try:
                    f = bm.faces.new((vs[j][i], vs[j][i + 1], vs[j + 1][i + 1], vs[j + 1][i]))
                    f.normal_update()
                except ValueError:
                    pass
    bm.normal_update()
    for f in bm.faces:
        if f.normal.x < 0:
            f.normal_flip()
    return new_object("Tent_Tape", bm, ["tape"], 0.5)


def poles_and_hubs():
    bm = bmesh.new()
    out = 0.045
    for k in range(4):
        pts = []
        for level in range(len(LEVELS)):
            p = corner(level, k)
            sx, sy = CORNERS[k]
            p += Vector((sx * out, sy * out, 0.0))
            pts.append(p)
        pts[0].z = 0.03
        polyline_tube(bm, pts, 0.016, 0, 6)
        # toe plate
        box(bm, Vector((pts[0].x, pts[0].y, 0.02)), (0.07, 0.07, 0.04), 0, math.radians(45))
        # corner hub knuckles
        for level in (2, TOP):
            box(bm, pts[level], (0.07, 0.07, 0.07), 0, math.radians(30))
    # ridge poles over the roof, hubs on front and back top centre
    front = surf(TOP, 0, 0.5) + Vector((0, -0.045, 0.02))
    back = surf(TOP, 2, 0.5) + Vector((0, 0.045, 0.02))
    right = surf(TOP, 1, 0.5) + Vector((0.045, 0, 0.02))
    left = surf(TOP, 3, 0.5) + Vector((-0.045, 0, 0.02))
    top = Vector((0, 0, CAP_Z + 0.03))
    for a, b in ((front, top), (top, back), (left, top), (top, right)):
        tube(bm, a, b, 0.014, 0)
    for hub in (front, back, left, right, top):
        box(bm, hub, (0.075, 0.075, 0.075), 0, math.radians(20))
    return new_object("Tent_Poles", bm, ["pole_metal"], 0.5)


def ropes_and_stakes():
    rope_bm = bmesh.new()
    stake_bm = bmesh.new()
    cfg = [(0, 0.0), (1, 0.0), (2, 0.0), (3, 0.0)]
    for k, _ in cfg:
        sx, sy = CORNERS[k]
        anchor = corner(2, k) + Vector((sx * 0.05, sy * 0.05, 0.02))
        lean = Vector((sx, sy, 0)).normalized()
        base = Vector((sx * (LEVELS[0][1] + 0.85), sy * (LEVELS[0][2] + 0.80), 0.0))
        # stake leans away from the tent
        rot = Matrix.Rotation(math.radians(18), 4, Vector((-lean.y, lean.x, 0)))
        m = Matrix.Translation(base + Vector((0, 0, 0.08))) @ rot @ Matrix.Diagonal((0.035, 0.035, 0.30, 1.0))
        res = bmesh.ops.create_cube(stake_bm, size=1.0, matrix=m)
        top = max(res["verts"], key=lambda v: v.co.z).co.copy()
        # notch near the head
        end = top - Vector((0, 0, 0.05)) + lean * 0.0
        mid = (anchor + end) * 0.5 + Vector((0, 0, -0.06))
        polyline_tube(rope_bm, [anchor, (anchor + mid) * 0.5 + Vector((0, 0, -0.02)), mid, (mid + end) * 0.5 + Vector((0, 0, -0.02)), end], 0.011, 0, 4)
        # loop of rope around the stake head
        for dz in (0.0, -0.025):
            c = end + Vector((0, 0, dz))
            box(rope_bm, c, (0.05, 0.05, 0.012), 0, math.atan2(lean.y, lean.x))
    ropes = new_object("Tent_Ropes", rope_bm, ["rope"], 0.4)
    stakes = new_object("Tent_Stakes", stake_bm, ["wood"], 0.4)
    return ropes, stakes


def rocks():
    bm = bmesh.new()
    spots = [
        (Vector((-1.50, -1.28, 0.0)), (0.22, 0.19, 0.14), 1),
        (Vector((1.52, -1.20, 0.0)), (0.20, 0.17, 0.12), 2),
        (Vector((-0.55, -1.42, 0.0)), (0.13, 0.11, 0.08), 3),
        (Vector((1.50, 1.24, 0.0)), (0.18, 0.16, 0.12), 4),
    ]
    for pos, radii, seed in spots:
        m = Matrix.Translation(pos + Vector((0, 0, radii[2] * 0.55))) @ Matrix.Rotation(seed * 1.3, 4, "Z") @ Matrix.Diagonal((radii[0], radii[1], radii[2], 1.0))
        res = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=1.0, matrix=m)
        for v in res["verts"]:
            q = v.co * 5.0 + Vector((seed, 2 * seed, 3 * seed))
            v.co += Vector((noise.noise(q), noise.noise(q + Vector((4, 1, 7))), noise.noise(q + Vector((6, 8, 2))))) * (radii[0] * 0.18)
            if v.co.z < 0.01:
                v.co.z = 0.01
        for v in res["verts"]:
            for f in v.link_faces:
                f.material_index = 0
    return new_object("Tent_Rocks", bm, ["rock"], 0.6)


def floor_object():
    bm = bmesh.new()
    hx, hy = LEVELS[0][1], LEVELS[0][2]
    box(bm, Vector((0, -0.1, 0.02)), (2 * hx - 0.04, 2 * hy + 0.2 - 0.04, 0.04), 0)
    return new_object("Tent_Floor", bm, ["groundsheet"], 1.2)


def bedding_object():
    bm = bmesh.new()
    # foam mat (0), sleeping bag (1), blanket (2), clothes (3)
    box(bm, Vector((-0.40, 0.05, 0.06)), (0.78, 1.95, 0.04), 0, math.radians(3))
    blob(bm, Vector((-0.40, 0.10, 0.18)), (0.30, 0.86, 0.13), 1, math.radians(3), seed=1, lump=0.03, segments=(10, 6), flat_bottom=0.08)
    blob(bm, Vector((-0.40, 0.68, 0.24)), (0.27, 0.30, 0.10), 1, math.radians(5), seed=2, lump=0.025, segments=(8, 5), flat_bottom=0.12)
    blob(bm, Vector((-0.34, -0.36, 0.27)), (0.30, 0.38, 0.09), 2, math.radians(-8), seed=3, lump=0.03, segments=(9, 5), flat_bottom=0.14)
    blob(bm, Vector((-0.42, 0.88, 0.22)), (0.20, 0.15, 0.09), 3, math.radians(10), seed=4, lump=0.02, segments=(8, 5), flat_bottom=0.08)
    blob(bm, Vector((0.55, -0.55, 0.12)), (0.22, 0.18, 0.07), 3, math.radians(40), seed=5, lump=0.03, segments=(8, 5), flat_bottom=0.04)
    return new_object("Tent_Bedding", bm, ["foam_mat", "sleeping_bag", "blanket", "clothes"], 0.8)


def interior_props_object():
    bm = bmesh.new()
    # backpack (0 backpack), cardboard (1), cans (2), bottles (3)
    box(bm, Vector((0.72, 0.68, 0.30)), (0.36, 0.22, 0.52), 0, math.radians(-15), taper=0.88)
    box(bm, Vector((0.62, 0.55, 0.20)), (0.26, 0.10, 0.28), 0, math.radians(-15))
    # cardboard box and flat sheet
    box(bm, Vector((0.78, 0.05, 0.19)), (0.42, 0.34, 0.30), 1, math.radians(12))
    box(bm, Vector((0.30, -1.40, 0.008)), (1.00, 0.80, 0.014), 1, math.radians(-6))
    # cans
    for pos, rot in ((Vector((0.14, -0.64, 0.075)), 0.0), (Vector((0.27, -0.72, 0.075)), 0.0)):
        tube(bm, pos - Vector((0, 0, 0.065)), pos + Vector((0, 0, 0.065)), 0.033, 2, 8)
    tube(bm, Vector((0.02, -0.80, 0.04)), Vector((0.14, -0.84, 0.04)), 0.033, 2, 8)
    tube(bm, Vector((0.10, -1.30, 0.034)), Vector((0.22, -1.34, 0.034)), 0.033, 2, 8)
    # bottles: body + neck
    for bx, by, ang in ((0.30, 0.55, 0.0), (0.08, 0.72, 0.0)):
        tube(bm, Vector((bx, by, 0.04)), Vector((bx, by, 0.20)), 0.034, 3, 8)
        tube(bm, Vector((bx, by, 0.20)), Vector((bx, by, 0.30)), 0.014, 3, 6)
    tube(bm, Vector((0.55, 0.30, 0.04)), Vector((0.73, 0.34, 0.04)), 0.034, 3, 8)
    return new_object("Tent_Props", bm, ["backpack", "cardboard", "can", "glass_bottle"], 0.6)


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    build_materials()
    canvas, bvh = shell_object()
    parts = [
        canvas,
        tape_object(bvh),
        poles_and_hubs(),
        floor_object(),
        bedding_object(),
        interior_props_object(),
        rocks(),
    ]
    ropes, stakes = ropes_and_stakes()
    parts += [ropes, stakes]

    root = bpy.data.objects.new("AbandonedTent", None)
    bpy.context.scene.collection.objects.link(root)
    for obj in parts:
        obj.parent = root

    tris = 0
    for obj in parts:
        for p in obj.data.polygons:
            tris += len(p.vertices) - 2
    print("TENT_TRIS", tris)

    os.makedirs(os.path.dirname(OUT_GLB), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND)
    bpy.ops.object.select_all(action="DESELECT")
    root.select_set(True)
    for obj in parts:
        obj.select_set(True)
    bpy.ops.export_scene.gltf(
        filepath=OUT_GLB,
        export_format="GLB",
        use_selection=True,
        export_apply=True,
        export_materials="EXPORT",
        export_image_format="AUTO",
        export_yup=True,
    )
    print("TENT_EXPORTED", OUT_GLB)


main()
