"""protein.com supplement store, v2 (built from scratch).

    "C:/Program Files/Blender Foundation/Blender 4.5/blender.exe" -b --factory-startup \
        -P Assets/ProteinStore/v2/create_protein_store_v2.py [-- --no-render]

Run make_store_textures.py first. Outputs next to this script:
  protein_store_v2.blend, protein_store_v2.glb, layout_boxes.json, renders/*.png

Coordinates (metres): +X runs from the storefront (x = -DEPTH/2) to the back
wall, +Y is north, +Z is up, origin at the centre of the footprint on the
floor. In Unity the GLB is placed with a 180 degree yaw, so +X maps to world
east and +Y to world north.

Style: plain boxes and low-segment cylinders, no bevels or rounded shapes,
white walls, black fixtures, yellow brand accents (proteini.si colours).
Every product and fixture part is recorded in layout_boxes.json so
verify_protein_store_v2.py can prove nothing overlaps.

Object naming read by GymProteinStoreEnvironment.cs:
  Solid_*        gets a box collider (walls, counters, shelving, fridges)
  Fridge_*_Glass transparent fridge doors
  Products_*     merged product meshes (no collider)
"""
import json
import math
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector

HERE = Path(__file__).resolve().parent
TEX = HERE / "textures"
LAYOUT = json.loads((TEX / "atlas_layout.json").read_text())
ATLAS = LAYOUT["atlas_size"]
SIGN_ATLAS = LAYOUT["signs_size"]

# Footprint and shell -------------------------------------------------------
DEPTH = 23.4          # X, storefront to back wall
WIDTH = 20.1          # Y
WALL = 0.25
WALL_H = 4.4
CEILING = 4.0
PARAPET = 5.0
X0, X1 = -DEPTH / 2, DEPTH / 2
Y0, Y1 = -WIDTH / 2, WIDTH / 2
IX0, IX1 = X0 + WALL, X1 - WALL
IY0, IY1 = Y0 + WALL, Y1 - WALL
DOOR_Y = -2.76        # door centre; matched by Unity's path-wall opening
DOOR_W = 2.6
DOOR_H = 2.7

boxes = []            # (kind, name, min, max) for overlap verification


def record(kind, name, mn, mx):
    boxes.append({"kind": kind, "name": name, "min": list(mn), "max": list(mx)})


# Materials -------------------------------------------------------------------
def srgb(c):
    # GymChaos' RuntimeGlbSceneLoader passes glTF colour factors straight to
    # Unity's Material.color, which expects sRGB values. Store the sRGB
    # values themselves so the shop shows its real yellow/white/black in the
    # game (Blender previews look slightly brighter as a result).
    return tuple(v / 255.0 for v in c)


def material(name, colour=(255, 255, 255), rough=0.7, metal=0.0, image=None,
             emission=None, strength=1.0, alpha=1.0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    bsdf = nodes["Principled BSDF"]
    lin = srgb(colour)
    bsdf.inputs["Base Color"].default_value = (*lin, 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    if image is not None:
        tex = nodes.new("ShaderNodeTexImage")
        tex.image = bpy.data.images.load(str(image))
        mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    if emission is not None:
        bsdf.inputs["Emission Color"].default_value = (*srgb(emission), 1.0)
        bsdf.inputs["Emission Strength"].default_value = strength
    if alpha < 1.0:
        bsdf.inputs["Alpha"].default_value = alpha
        mat.blend_method = "BLEND"
        mat.surface_render_method = "BLENDED"
    return mat


M = {}


def make_materials():
    M["white"] = material("PS_Wall_White", (238, 238, 234), 0.85)
    M["black"] = material("PS_Fixture_Black", (22, 22, 22), 0.55)
    M["yellow"] = material("PS_Brand_Yellow", (255, 204, 0), 0.5)
    M["shelf"] = material("PS_Shelf_White", (244, 244, 242), 0.6)
    M["floor"] = material("PS_Floor_Tile", (214, 214, 210), 0.45, image=TEX / "floor_tile.png")
    M["ceiling"] = material("PS_Ceiling", (232, 232, 230), 0.9)
    M["light"] = material("PS_Light_Panel", (255, 255, 250), 0.4, emission=(255, 250, 238), strength=3.0)
    M["glass"] = material("PS_Storefront_Glass", (190, 214, 222), 0.05, 0.0, alpha=0.22)
    M["fridge_glass"] = material("PS_Fridge_Glass", (200, 224, 236), 0.05, 0.0, alpha=0.18)
    M["fridge_inner"] = material("PS_Fridge_Inner", (240, 244, 246), 0.5, emission=(232, 242, 250), strength=0.6)
    M["metal"] = material("PS_Metal", (170, 172, 176), 0.35, 0.8)
    M["mat"] = material("PS_Entrance_Mat", (40, 40, 42), 0.95)
    M["roof"] = material("PS_Roof", (70, 72, 76), 0.9)
    M["letters"] = material("PS_Sign_Letters", (255, 204, 0), 0.4, emission=(255, 204, 0), strength=1.5)
    M["screen"] = material("PS_Screen", (30, 60, 90), 0.2, emission=(70, 130, 190), strength=0.8)
    M["products"] = material("PS_Products", (255, 255, 255), 0.55, image=TEX / "products_atlas.png")
    M["signs"] = material("PS_Signs", (255, 255, 255), 0.6, image=TEX / "signs_atlas.png")


# Mesh accumulation -------------------------------------------------------------
class Builder:
    """Accumulates boxes/cylinders into one mesh with per-face material and UV."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")
        self.mats = []

    def mat_index(self, mat):
        if mat not in self.mats:
            self.mats.append(mat)
        return self.mats.index(mat)

    def face(self, verts, mat, uvs):
        vs = [self.bm.verts.new(v) for v in verts]
        f = self.bm.faces.new(vs)
        f.material_index = self.mat_index(mat)
        for loop, uv in zip(f.loops, uvs):
            loop[self.uv].uv = uv
        return f

    def box(self, mn, mx, mat, uv_rects=None, uv_scale=None):
        """uv_rects: dict side -> (u0, v0, u1, v1); sides: +x -x +y -y +z -z.
        uv_scale: planar world-space UV with this repeat size (floors)."""
        x0, y0, z0 = mn
        x1, y1, z1 = mx
        quads = {
            "+x": [(x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1)],
            "-x": [(x0, y1, z0), (x0, y0, z0), (x0, y0, z1), (x0, y1, z1)],
            "+y": [(x1, y1, z0), (x0, y1, z0), (x0, y1, z1), (x1, y1, z1)],
            "-y": [(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)],
            "+z": [(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)],
            "-z": [(x0, y1, z0), (x1, y1, z0), (x1, y0, z0), (x0, y0, z0)],
        }
        for side, quad in quads.items():
            if uv_scale is not None:
                uvs = [(v[0] / uv_scale, v[1] / uv_scale) for v in quad]
            elif uv_rects and side in uv_rects:
                u0, v0, u1, v1 = uv_rects[side]
                uvs = [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]
            elif uv_rects and "*" in uv_rects:
                u0, v0, u1, v1 = uv_rects["*"]
                uvs = [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]
            else:
                uvs = [(0, 0), (1, 0), (1, 1), (0, 1)]
            self.face(quad, mat, uvs)

    def cylinder(self, base, radius, height, mat, label=None, cap=None, segments=16, front=(1, 0)):
        """Vertical cylinder; label wraps the side with its centre facing `front`."""
        cx, cy, cz = base
        start = math.atan2(front[1], front[0]) + math.pi
        ring0, ring1 = [], []
        for i in range(segments + 1):
            a = start + 2 * math.pi * i / segments
            ring0.append((cx + radius * math.cos(a), cy + radius * math.sin(a), cz))
            ring1.append((cx + radius * math.cos(a), cy + radius * math.sin(a), cz + height))
        cap_uv = cap or (0, 0, 1, 1)
        mid = ((cap_uv[0] + cap_uv[2]) / 2, (cap_uv[1] + cap_uv[3]) / 2)
        quarter = segments // 4
        for i in range(segments):
            # The label covers the front half (centred on `front`); the back
            # half is the plain packaging colour, like a real wrap label.
            if label:
                # One full label on the front half and one on the back half,
                # so the brand reads from any side (shelf islands, promo
                # pyramids).
                u0, v0, u1, v1 = label
                half = segments // 2
                k = (i - quarter) % segments
                k = k if k < half else k - half
                ua = u0 + (u1 - u0) * k / half
                ub = u0 + (u1 - u0) * (k + 1) / half
                uvs = [(ua, v0), (ub, v0), (ub, v1), (ua, v1)]
            else:
                uvs = [mid] * 4
            self.face([ring0[i], ring0[i + 1], ring1[i + 1], ring1[i]], mat, uvs)
        self.face(list(reversed(ring0[:-1])), mat, [mid] * segments)
        self.face(ring1[:-1], mat, [mid] * segments)

    def build(self):
        if not self.bm.faces:
            self.bm.free()
            return None
        mesh = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(mesh)
        self.bm.free()
        for mat in self.mats:
            mesh.materials.append(mat)
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        return obj


def single_box(name, mn, mx, mat, kind="fixture", **kw):
    b = Builder(name)
    b.box(mn, mx, mat, **kw)
    record(kind, name, mn, mx)
    return b.build()


def tile_uv(name, inset=0.004):
    x, y, w, h = LAYOUT["products"][name]
    u0 = x / ATLAS + inset
    u1 = (x + w) / ATLAS - inset
    v1 = 1 - y / ATLAS - inset
    v0 = 1 - (y + h) / ATLAS + inset
    return (u0, v0, u1, v1)


def solid_uv(name):
    u0, v0, u1, v1 = tile_uv(name, 0.02)
    mu, mv = (u0 + u1) / 2, (v0 + v1) / 2
    return (mu, mv, mu + 0.0005, mv + 0.0005)


def sign_uv(name, inset=6):
    # Inset a few texels so filtering never pulls in the neighbouring tile
    # (it drew a thin yellow outline round the counter panel).
    x, y, w, h = LAYOUT["signs"][name]
    x, y, w, h = x + inset, y + inset, w - 2 * inset, h - 2 * inset
    return (x / SIGN_ATLAS, 1 - (y + h) / SIGN_ATLAS, (x + w) / SIGN_ATLAS, 1 - y / SIGN_ATLAS)


# Products ---------------------------------------------------------------------
# kind: (shape, width, depth, height, label tile, solid tile)
PRODUCTS = {
    "pouch_choc": ("box", 0.20, 0.10, 0.30, "whey_choc", "s_white"),
    "pouch_vanilla": ("box", 0.20, 0.10, 0.30, "whey_vanilla", "s_white"),
    "pouch_straw": ("box", 0.20, 0.10, 0.30, "whey_straw", "s_white"),
    "pouch_cookie": ("box", 0.20, 0.10, 0.30, "whey_cookie", "s_white"),
    "pouch_banana": ("box", 0.20, 0.10, 0.30, "whey_banana", "s_white"),
    "pouch_caramel": ("box", 0.20, 0.10, 0.30, "whey_caramel", "s_white"),
    "pouch_isolate": ("box", 0.20, 0.10, 0.30, "isolate", "s_black"),
    "pouch_rebel": ("box", 0.17, 0.08, 0.24, "rebel", "s_navy"),
    "tub_whey": ("cyl", 0.17, 0.17, 0.26, "tub_whey", "s_beige"),
    "tub_whey_choc": ("cyl", 0.17, 0.17, 0.26, "tub_whey_choc", "s_brown"),
    "tub_creatine": ("cyl", 0.12, 0.12, 0.15, "creatine", "s_yellow"),
    "tub_reload": ("cyl", 0.13, 0.13, 0.17, "creatine_red", "s_red"),
    "tub_bcaa": ("cyl", 0.12, 0.12, 0.15, "bcaa", "s_blue"),
    "tub_pre_green": ("cyl", 0.13, 0.13, 0.16, "pre_green", "s_black"),
    "tub_pre_pink": ("cyl", 0.13, 0.13, 0.16, "pre_pink", "s_black"),
    "tub_pre_blue": ("cyl", 0.13, 0.13, 0.16, "pre_blue", "s_black"),
    "barbox_coconut": ("box", 0.24, 0.13, 0.09, "bar_box_coconut", "s_lightblue"),
    "barbox_choc": ("box", 0.24, 0.13, 0.09, "bar_box_choc", "s_lightblue"),
    "barbox_peanut": ("box", 0.24, 0.13, 0.09, "bar_box_peanut", "s_lightblue"),
    "bar_coconut": ("box", 0.12, 0.04, 0.025, "bar_wrap_coconut", "s_coconut"),
    "bar_choc": ("box", 0.12, 0.04, 0.025, "bar_wrap_choc", "s_brown"),
    "magnesium": ("box", 0.12, 0.08, 0.15, "magnesium", "s_white"),
    "vit_d3": ("cyl", 0.07, 0.07, 0.11, "vit_d3", "s_white"),
    "omega3": ("cyl", 0.07, 0.07, 0.11, "omega3", "s_white"),
    "zinc": ("cyl", 0.07, 0.07, 0.11, "zinc", "s_white"),
    "can_lime": ("cyl", 0.066, 0.066, 0.168, "can_lime", "s_silver"),
    "can_orange": ("cyl", 0.066, 0.066, 0.168, "can_orange", "s_silver"),
    "can_purple": ("cyl", 0.066, 0.066, 0.168, "can_purple", "s_silver"),
    "can_red": ("cyl", 0.066, 0.066, 0.168, "can_red", "s_silver"),
    "rtd_choc": ("cyl", 0.07, 0.07, 0.20, "rtd_choc", "s_white"),
    "rtd_vanilla": ("cyl", 0.07, 0.07, 0.20, "rtd_vanilla", "s_white"),
    "pb_jar": ("cyl", 0.10, 0.10, 0.12, "pb_jar", "s_peanut"),
    "shaker": ("cyl", 0.09, 0.09, 0.22, "shaker", "s_black"),
}

product_builders = {}


def products_builder(category):
    if category not in product_builders:
        product_builders[category] = Builder("Products_" + category.replace(" ", "_").replace("&", "And"))
    return product_builders[category]


def place_product(category, kind, x, y, z, facing):
    """facing: one of +x -x +y -y (the label side). (x, y) is the footprint centre."""
    shape, w, d, h, label, solid = PRODUCTS[kind]
    b = products_builder(category)
    along_x = facing in ("+y", "-y")
    sx, sy = (w, d) if along_x else (d, w)
    mn = (x - sx / 2, y - sy / 2, z)
    mx = (x + sx / 2, y + sy / 2, z + h)
    if shape == "box":
        back = {"+x": "-x", "-x": "+x", "+y": "-y", "-y": "+y"}[facing]
        # Label on the front and the back face; narrow sides stay plain.
        b.box(mn, mx, M["products"],
              uv_rects={facing: tile_uv(label), back: tile_uv(label), "*": solid_uv(solid)})
    else:
        front = {"+x": (1, 0), "-x": (-1, 0), "+y": (0, 1), "-y": (0, -1)}[facing]
        # Low segment counts: 12 sides for tubs and jars, 8 for cans/bottles.
        b.cylinder((x, y, z), w / 2, h, M["products"], label=tile_uv(label), cap=solid_uv(solid),
                   front=front, segments=12 if w > 0.09 else 8)
    record("product", f"{category}:{kind}", mn, mx)


def fill_row(category, kinds, start, end, y_or_x, z, facing, gap=0.025, depth_rows=1, row_pitch=0.0):
    """Lay products side by side between start and end along the shelf.
    For facing +/-y the row runs along x at y=y_or_x; else along y at x."""
    along_x = facing in ("+y", "-y")
    cursor = start + gap
    i = 0
    while True:
        kind = kinds[i % len(kinds)]
        w = PRODUCTS[kind][1]
        if cursor + w > end - gap + 1e-6:
            break
        c = cursor + w / 2
        for r in range(depth_rows):
            offset = r * row_pitch
            if along_x:
                place_product(category, kind, c, y_or_x + offset, z, facing)
            else:
                place_product(category, kind, y_or_x + offset, c, z, facing)
        cursor += w + gap
        i += 1


# Fixtures -----------------------------------------------------------------------
SHELF_LEVELS = [0.12, 0.52, 0.92, 1.32, 1.72]
SHELF_T = 0.03
BAY = 1.2
UPRIGHT = 0.05


def wall_bay(name, origin, facing, category, rows, header=None):
    """Wall shelving bay 1.2 m wide, 0.45 deep, 2.1 tall.
    origin: (x, y) of the bay's back-left corner on the wall face; facing is
    the direction customers look from (+x: unit faces +x)."""
    b = Builder("Solid_" + name)
    depth = 0.45
    ox, oy = origin
    # Local frame: u runs along the wall, v points away from the wall.
    if facing == "+y":
        def P(u, v, z): return (ox + u, oy + v, z)
    elif facing == "-y":
        def P(u, v, z): return (ox + u, oy - v, z)
    elif facing == "+x":
        def P(u, v, z): return (ox + v, oy + u, z)
    else:
        def P(u, v, z): return (ox - v, oy + u, z)

    def part(u0, u1, v0, v1, z0, z1, mat, label):
        a = P(u0, v0, z0)
        c = P(u1, v1, z1)
        mn = tuple(min(p, q) for p, q in zip(a, c))
        mx = tuple(max(p, q) for p, q in zip(a, c))
        b.box(mn, mx, mat)
        record("fixture", f"{name}:{label}", mn, mx)

    part(0, BAY, 0, 0.02, 0, 2.1, M["shelf"], "back")
    part(0, UPRIGHT, 0.02, depth, 0, 2.07, M["black"], "upL")
    part(BAY - UPRIGHT, BAY, 0.02, depth, 0, 2.07, M["black"], "upR")
    part(UPRIGHT, BAY - UPRIGHT, 0.02, depth, 0, SHELF_LEVELS[0], M["black"], "plinth")
    for i, z in enumerate(SHELF_LEVELS[1:]):
        part(UPRIGHT, BAY - UPRIGHT, 0.02, depth - 0.03, z - SHELF_T, z, M["shelf"], f"shelf{i}")
        part(UPRIGHT, BAY - UPRIGHT, depth - 0.03, depth, z - SHELF_T - 0.03, z, M["yellow"], f"rail{i}")
    part(0, BAY, 0.02, depth, 2.1 - 0.03, 2.1, M["black"], "top")
    obj = b.build()

    # Products: one row per level, labels toward the aisle.
    v_row = 0.26
    for level, kinds in enumerate(rows):
        if level >= len(SHELF_LEVELS) - 0 or not kinds:
            continue
        z = SHELF_LEVELS[level] + 0.001
        if facing in ("+y", "-y"):
            y = oy + (v_row if facing == "+y" else -v_row)
            fill_row(category, kinds, ox + UPRIGHT, ox + BAY - UPRIGHT, y, z, facing)
        else:
            x = ox + (v_row if facing == "+x" else -v_row)
            fill_row(category, kinds, oy + UPRIGHT, oy + BAY - UPRIGHT, x, z, facing)
    return obj


def header_sign(name, text, centre, facing, width=2.4, height=0.3, z=2.25):
    """Yellow category board above shelving, mounted on the wall/backboard."""
    cx, cy = centre
    t = 0.03
    if facing in ("+y", "-y"):
        mn = (cx - width / 2, cy - t / 2, z)
        mx = (cx + width / 2, cy + t / 2, z + height)
    else:
        mn = (cx - t / 2, cy - width / 2, z)
        mx = (cx + t / 2, cy + width / 2, z + height)
    single_box("Sign_" + name, mn, mx, M["signs"], kind="sign",
               uv_rects={facing: sign_uv(text), "*": sign_uv(text)})


def gondola(name, x0, x1, yc, category, rows_north, rows_south, header):
    """Double-sided island unit along X, 1.0 deep, 1.6 tall, bays of 1.2 m."""
    b = Builder("Solid_" + name)
    half = 0.5
    top = 1.75
    levels = [0.12, 0.52, 0.92, 1.32]

    def part(mn, mx, mat, label):
        b.box(mn, mx, mat)
        record("fixture", f"{name}:{label}", mn, mx)

    part((x0, yc - 0.03, 0), (x1, yc + 0.03, top - 0.03), M["shelf"], "spine")
    part((x0, yc - half, 0), (x0 + UPRIGHT, yc - 0.03, top - 0.03), M["black"], "endS0")
    part((x0, yc + 0.03, 0), (x0 + UPRIGHT, yc + half, top - 0.03), M["black"], "endN0")
    part((x1 - UPRIGHT, yc - half, 0), (x1, yc - 0.03, top - 0.03), M["black"], "endS1")
    part((x1 - UPRIGHT, yc + 0.03, 0), (x1, yc + half, top - 0.03), M["black"], "endN1")
    part((x0 + UPRIGHT, yc - half, 0), (x1 - UPRIGHT, yc - 0.03, levels[0]), M["black"], "plinthS")
    part((x0 + UPRIGHT, yc + 0.03, 0), (x1 - UPRIGHT, yc + half, levels[0]), M["black"], "plinthN")
    for i, z in enumerate(levels[1:]):
        part((x0 + UPRIGHT, yc - half + 0.03, z - SHELF_T), (x1 - UPRIGHT, yc - 0.03, z), M["shelf"], f"shelfS{i}")
        part((x0 + UPRIGHT, yc - half, z - SHELF_T - 0.03), (x1 - UPRIGHT, yc - half + 0.03, z), M["yellow"], f"railS{i}")
        part((x0 + UPRIGHT, yc + 0.03, z - SHELF_T), (x1 - UPRIGHT, yc + half - 0.03, z), M["shelf"], f"shelfN{i}")
        part((x0 + UPRIGHT, yc + half - 0.03, z - SHELF_T - 0.03), (x1 - UPRIGHT, yc + half, z), M["yellow"], f"railN{i}")
    part((x0, yc - half, top - 0.03), (x1, yc + half, top), M["black"], "top")
    obj = b.build()
    # Header board standing on the top, centred.
    cx = (x0 + x1) / 2
    single_box("Sign_" + name + "_Header", (cx - 1.2, yc - 0.015, top), (cx + 1.2, yc + 0.015, top + 0.3),
               M["signs"], kind="sign", uv_rects={"+y": sign_uv(header), "-y": sign_uv(header), "*": sign_uv(header)})
    for side, rows in (("+y", rows_north), ("-y", rows_south)):
        y = yc + (0.26 if side == "+y" else -0.26)
        for level, kinds in enumerate(rows):
            if not kinds:
                continue
            fill_row(category, kinds, x0 + UPRIGHT, x1 - UPRIGHT, y, levels[level] + 0.001, side)
    return obj


def fridge(index, y0, x_face):
    """Glass-door drinks fridge against the back wall, 1.1 wide, 0.75 deep."""
    name = f"Fridge_{index}"
    y1 = y0 + 1.1
    x_back = IX1
    x_front = x_face
    b = Builder("Solid_" + name)

    def part(mn, mx, mat, label):
        b.box(mn, mx, mat)
        record("fixture", f"{name}:{label}", mn, mx)

    h = 2.15
    t = 0.05
    part((x_front + t, y0, 0), (x_back, y0 + t, h), M["black"], "sideS")
    part((x_front + t, y1 - t, 0), (x_back, y1, h), M["black"], "sideN")
    part((x_back - t, y0 + t, 0), (x_back, y1 - t, h), M["fridge_inner"], "back")
    part((x_front + t, y0 + t, 0), (x_back - t, y1 - t, 0.18), M["black"], "base")
    part((x_front + t, y0 + t, h - 0.25), (x_back - t, y1 - t, h), M["black"], "canopy")
    part((x_front, y0, 0), (x_front + t, y1, 0.18), M["black"], "kick")
    part((x_front, y0, h - 0.25), (x_front + t, y1, h), M["yellow"], "header")
    part((x_front, y0, 0.18), (x_front + t, y0 + 0.04, h - 0.25), M["black"], "frameS")
    part((x_front, y1 - 0.04, 0.18), (x_front + t, y1, h - 0.25), M["black"], "frameN")
    levels = [0.18, 0.55, 0.92, 1.29, 1.66]
    for i, z in enumerate(levels[1:]):
        part((x_front + t + 0.02, y0 + t, z - 0.02), (x_back - t, y1 - t, z), M["metal"], f"shelf{i}")
    b.build()
    # Door glass (transparent), a thin pane inside the frame.
    g = Builder(f"{name}_Glass")
    gmn = (x_front + 0.015, y0 + 0.04, 0.18)
    gmx = (x_front + 0.035, y1 - 0.04, h - 0.25)
    g.box(gmn, gmx, M["fridge_glass"])
    record("glass", f"{name}:glass", gmn, gmx)
    g.build()
    drinks = [["rtd_choc", "rtd_vanilla"], ["can_lime", "can_orange"], ["can_purple", "can_red"],
              ["can_lime", "can_purple"], ["rtd_vanilla", "rtd_choc"]]
    x_row = x_front + t + 0.12
    for level, z in enumerate(levels):
        kinds = drinks[(index + level) % len(drinks)]
        fill_row("Drinks", kinds, y0 + t, y1 - t, x_row, z + 0.001, "-x", gap=0.012, depth_rows=2, row_pitch=0.11)


# Shell ---------------------------------------------------------------------------
def build_shell():
    floor = Builder("Floor_Tiles")
    mn, mx = (IX0, IY0, 0.0), (IX1, IY1, 0.01)
    floor.box(mn, mx, M["floor"], uv_scale=1.2)
    floor.build()
    single_box("Ceiling", (IX0, IY0, CEILING), (IX1, IY1, CEILING + 0.02), M["ceiling"], kind="shell")
    single_box("Roof", (X0 + WALL, Y0 + WALL, WALL_H), (X1 - WALL, Y1 - WALL, WALL_H + 0.2), M["roof"], kind="shell")

    walls = Builder("Solid_Walls")

    def wall(mn, mx, label):
        walls.box(mn, mx, M["white"])
        record("shell", "wall:" + label, mn, mx)

    wall((X1 - WALL, Y0, 0), (X1, Y1, WALL_H), "back")
    wall((X0 + WALL, Y1 - WALL, 0), (X1 - WALL, Y1, WALL_H), "north")
    wall((X0 + WALL, Y0, 0), (X1 - WALL, Y0 + WALL, WALL_H), "south")
    # Parapet ring around the roof (the storefront carries its own).
    wall((X0 + WALL, Y0, WALL_H), (X1 - WALL, Y0 + WALL, PARAPET), "parapetS")
    wall((X0 + WALL, Y1 - WALL, WALL_H), (X1 - WALL, Y1, PARAPET), "parapetN")
    wall((X1 - WALL, Y0, WALL_H), (X1, Y1, PARAPET), "parapetB")
    walls.build()

    # Black base strip and yellow coping on the exterior.
    trim = Builder("Exterior_Trim")
    trim.box((X1, Y0, 0), (X1 + 0.02, Y1, 0.4), M["black"])
    trim.box((X0, Y1, 0), (X1 + 0.02, Y1 + 0.02, 0.4), M["black"])
    trim.box((X0, Y0 - 0.02, 0), (X1 + 0.02, Y0, 0.4), M["black"])
    trim.box((X0 - 0.02, Y0 - 0.02, PARAPET), (X1 + 0.02, Y1 + 0.02, PARAPET + 0.08), M["yellow"])
    trim.build()

    # Storefront: black frame, glass, door opening, black fascia with letters.
    front = Builder("Solid_Storefront")
    fx0, fx1 = X0, X0 + WALL
    door0, door1 = DOOR_Y - DOOR_W / 2, DOOR_Y + DOOR_W / 2

    def fpart(y0, y1, z0, z1, mat, label):
        front.box((fx0, y0, z0), (fx1, y1, z1), mat)
        record("shell", "front:" + label, (fx0, y0, z0), (fx1, y1, z1))

    fpart(Y0, Y0 + 0.6, 0, WALL_H, M["white"], "pierS")
    fpart(Y1 - 0.6, Y1, 0, WALL_H, M["white"], "pierN")
    fpart(Y0 + 0.6, Y1 - 0.6, 3.5, WALL_H, M["black"], "fascia")
    fpart(Y0, Y1, WALL_H, PARAPET, M["black"], "parapet")
    fpart(Y0 + 0.6, door0, 0, 0.35, M["black"], "kickS")
    fpart(door1, Y1 - 0.6, 0, 0.35, M["black"], "kickN")
    fpart(door0, door1, DOOR_H, 3.5, M["black"], "transom")
    # Mullions every ~1.8 m on both sides of the door, glass between them.
    mull = 0.08
    glass = Builder("Storefront_Glass")
    for (a, b_) in ((Y0 + 0.6, door0), (door1, Y1 - 0.6)):
        count = max(1, round((b_ - a) / 1.8))
        edges = [a + (b_ - a) * k / count for k in range(count + 1)]
        spans = []
        for k, yc in enumerate(edges):
            y0 = a if k == 0 else (b_ - mull if k == count else yc - mull / 2)
            y1 = a + mull if k == 0 else (b_ if k == count else yc + mull / 2)
            fpart(y0, y1, 0.35, 3.5, M["black"], f"mull{a:.1f}_{k}")
            spans.append((y0, y1))
        for (p0, p1), (q0, q1) in zip(spans[:-1], spans[1:]):
            gmn, gmx = (fx0 + 0.11, p1, 0.35), (fx0 + 0.13, q0, 3.5)
            glass.box(gmn, gmx, M["glass"])
            record("glass", f"pane{p1:.2f}", gmn, gmx)
    front.build()
    glass.build()
    # Sliding door leaves parked open behind the glass, beside the opening.
    leaves = Builder("Door_Leaves")
    leaves.box((fx1 + 0.02, door0 - 1.25, 0.02), (fx1 + 0.05, door0 + 0.05, DOOR_H - 0.12), M["glass"])
    leaves.box((fx1 + 0.02, door1 - 0.05, 0.02), (fx1 + 0.05, door1 + 1.25, DOOR_H - 0.12), M["glass"])
    leaves.box((fx1 + 0.02, door0 - 1.25, DOOR_H - 0.12), (fx1 + 0.06, door1 + 1.25, DOOR_H - 0.02), M["metal"])
    leaves.build()
    record("glass", "door_leafS", (fx1 + 0.02, door0 - 1.25, 0.02), (fx1 + 0.05, door0 + 0.05, DOOR_H - 0.12))
    record("glass", "door_leafN", (fx1 + 0.02, door1 - 0.05, 0.02), (fx1 + 0.05, door1 + 1.25, DOOR_H - 0.12))
    record("fixture", "door_track", (fx1 + 0.02, door0 - 1.25, DOOR_H - 0.12), (fx1 + 0.06, door1 + 1.25, DOOR_H - 0.02))
    # Door decal (opening hours) on the north glass beside the door.
    single_box("Sign_Door_Hours", (fx0 + 0.105, door1 + 0.3, 1.2), (fx0 + 0.11, door1 + 0.9, 1.5),
               M["signs"], kind="sign", uv_rects={"-x": sign_uv("door_hours"), "*": sign_uv("door_hours")})
    single_box("Entrance_Mat", (fx1, door0 + 0.1, 0.01), (fx1 + 1.4, door1 - 0.1, 0.025), M["mat"], kind="floor")

    # Interior wall accents: black skirting and a yellow band at 2.8 m.
    acc = Builder("Interior_Accents")
    for label, (mn, mx) in {
        "bandN": ((IX0, IY1 - 0.01, 2.75), (IX1 - 0.01, IY1, 2.9)),
        "bandS": ((IX0, IY0, 2.75), (IX1 - 0.01, IY0 + 0.01, 2.9)),
        "bandB": ((IX1 - 0.01, IY0, 2.75), (IX1, IY1, 2.9)),
    }.items():
        acc.box(mn, mx, M["yellow"])
        record("sign", "accent:" + label, mn, mx)
    acc.build()

    # Ceiling light panels.
    lights = Builder("Ceiling_Lights")
    for i in range(7):
        for j in range(6):
            cx = -9.0 + 3.0 * i
            cy = -7.5 + 3.0 * j
            lights.box((cx - 0.6, cy - 0.3, CEILING - 0.02), (cx + 0.6, cy + 0.3, CEILING), M["light"])
    lights.build()


def build_sign_letters():
    """3D "protein.com" on the black fascia, yellow, facing the street (-x)."""
    bpy.ops.object.text_add(location=(0, 0, 0))
    text = bpy.context.object
    text.data.body = "protein.com"
    text.data.font = bpy.data.fonts.load("C:/Windows/Fonts/ariblk.ttf")
    text.data.extrude = 0.04
    text.data.size = 0.78
    text.data.align_x = "CENTER"
    text.data.align_y = "CENTER"
    text.data.shear = 0.12
    bpy.ops.object.convert(target="MESH")
    obj = bpy.context.object
    obj.name = "Sign_Letters"
    obj.data.materials.clear()
    obj.data.materials.append(M["letters"])
    # Text is built in XY; stand it up facing -X on the fascia.
    obj.rotation_euler = (math.radians(90), 0, math.radians(-90))
    obj.location = (X0 - 0.041, 1.4, 3.97)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    mn = [min(v.co[i] for v in obj.data.vertices) for i in range(3)]
    mx = [max(v.co[i] for v in obj.data.vertices) for i in range(3)]
    record("sign", "letters", mn, mx)


# Store contents ---------------------------------------------------------------------
PROTEIN_ROWS = [
    ["tub_whey", "tub_whey_choc"],
    ["pouch_choc", "pouch_vanilla", "pouch_straw"],
    ["pouch_cookie", "pouch_banana", "pouch_caramel"],
    ["pouch_vanilla", "pouch_choc", "pouch_cookie"],
    ["tub_whey_choc", "tub_whey"],
]
ISOLATE_ROWS = [["pouch_isolate"], ["pouch_isolate"], ["pouch_isolate", "pouch_choc"], ["pouch_isolate"], ["tub_whey"]]
AMINO_ROWS = [["tub_bcaa"], ["tub_bcaa", "tub_creatine"], ["tub_bcaa"], ["tub_bcaa", "tub_reload"], ["tub_bcaa"]]
SNACK_ROWS = [["pb_jar"], ["pb_jar"], ["shaker"], ["shaker"], ["pb_jar"]]
PRE_ROWS = [["tub_pre_green", "tub_pre_pink", "tub_pre_blue"]] * 5
CREATINE_ROWS = [["tub_creatine"], ["tub_reload", "tub_creatine"], ["pouch_rebel"], ["tub_creatine", "tub_reload"], ["pouch_rebel"]]
VITAMIN_ROWS = [["magnesium"], ["vit_d3", "omega3", "zinc"], ["magnesium"], ["omega3", "vit_d3"], ["zinc", "vit_d3"]]
BARBOX_ROWS = [["barbox_coconut", "barbox_choc", "barbox_peanut"]] * 5


def build_contents():
    # North wall (facing -y): protein, isolate, amino acids, snacks.
    north_y = IY1
    plan = ([("Protein", PROTEIN_ROWS, "PROTEIN")] * 9 + [("Isolate", ISOLATE_ROWS, "ISOLATE")] * 3 +
            [("Amino", AMINO_ROWS, "AMINO ACIDS")] * 3 + [("Snacks", SNACK_ROWS, "SNACKS")] * 2)
    x = -9.6
    groups = {}
    for i, (cat, rows, header) in enumerate(plan):
        wall_bay(f"WallBay_N{i:02d}", (x, north_y), "-y", cat, rows)
        groups.setdefault(header, []).append(x)
        x += BAY
    for header, xs in groups.items():
        cx = (min(xs) + max(xs) + BAY) / 2
        header_sign(f"N_{header.replace(' ', '_')}", header, (cx, north_y - 0.015), "-y",
                    width=min(3.6, BAY * len(xs) - 0.2))

    # South wall east of the counter (facing +y): pre-workout, creatine.
    south_y = IY0
    plan = [("PreWorkout", PRE_ROWS, "PRE-WORKOUT")] * 6 + [("Creatine", CREATINE_ROWS, "CREATINE")] * 6
    x = -3.6
    groups = {}
    for i, (cat, rows, header) in enumerate(plan):
        wall_bay(f"WallBay_S{i:02d}", (x, south_y), "+y", cat, rows)
        groups.setdefault(header, []).append(x)
        x += BAY
    for header, xs in groups.items():
        cx = (min(xs) + max(xs) + BAY) / 2
        header_sign(f"S_{header.replace(' ', '_')}", header, (cx, south_y + 0.015), "+y", width=3.6)

    # Back wall (facing -x): bars bays north, vitamins bays south, fridges middle.
    back_x = IX1
    for i in range(4):
        y = 4.4 + i * BAY
        wall_bay(f"WallBay_BN{i}", (back_x, y), "-x", "Bars", BARBOX_ROWS)
    header_sign("B_BARS", "PROTEIN BARS", (back_x - 0.015, 4.4 + 2 * BAY), "-x", width=3.6)
    for i in range(4):
        y = -8.8 + i * BAY
        wall_bay(f"WallBay_BS{i}", (back_x, y), "-x", "Vitamins", VITAMIN_ROWS)
    header_sign("B_VITAMINS", "VITAMINS & MINERALS", (back_x - 0.015, -8.8 + 2 * BAY), "-x", width=3.6)
    for i in range(6):
        fridge(i, -3.3 + i * 1.1, IX1 - 0.75)
    header_sign("B_DRINKS", "COLD DRINKS", (IX1 - 0.015, 0.0), "-x", width=4.0, height=0.35, z=2.3)

    # Island gondolas.
    bars = [["barbox_coconut", "barbox_choc"], ["barbox_peanut", "barbox_coconut"],
            ["barbox_choc", "barbox_peanut"], ["barbox_coconut"]]
    vit = [["magnesium"], ["vit_d3", "omega3", "zinc"], ["magnesium", "zinc"], ["omega3"]]
    tubs = [["tub_whey"], ["tub_whey_choc"], ["tub_whey"], ["tub_whey_choc"]]
    crea = [["tub_creatine"], ["tub_reload"], ["tub_creatine", "tub_bcaa"], ["tub_reload"]]
    snacks = [["pb_jar"], ["shaker"], ["pb_jar", "shaker"], ["pb_jar"]]
    gondola("Gondola_1W", -3.0, 1.8, -3.4, "Bars", bars, bars, "PROTEIN BARS")
    gondola("Gondola_1E", 3.6, 8.4, -3.4, "Bars", bars, bars, "PROTEIN BARS")
    gondola("Gondola_2W", -3.0, 1.8, 0.6, "Vitamins", vit, vit, "VITAMINS & MINERALS")
    gondola("Gondola_2E", 3.6, 8.4, 0.6, "Snacks", snacks, snacks, "SNACKS")
    gondola("Gondola_3W", -3.0, 1.8, 4.8, "Protein", tubs, tubs, "PROTEIN")
    gondola("Gondola_3E", 3.6, 8.4, 4.8, "Creatine", crea, crea, "CREATINE")

    build_checkout()
    build_promo()
    build_posters()


def build_checkout():
    # Main counter facing customers (north), staff behind it (south).
    c = Builder("Solid_Counter")

    def part(mn, mx, mat, label, uv=None):
        c.box(mn, mx, mat, uv_rects=uv)
        record("fixture", "counter:" + label, mn, mx)

    # One L-shaped counter: the front run and the return towards the wall
    # share their outer edge and meet at a single seam (y = -7.05 on top,
    # -7.0 below); the return ends on the wall, beside the shelving.
    part((-9.4, -7.0, 0), (-4.65, -6.24, 1.0), M["black"], "body")
    part((-9.45, -7.05, 1.0), (-4.6, -6.18, 1.05), M["shelf"], "top")
    part((-5.45, IY0, 0), (-4.65, -7.0, 1.0), M["black"], "return")
    part((-5.45, IY0, 1.0), (-4.6, -7.05, 1.05), M["shelf"], "returnTop")
    part((-11.2, -7.0, 0), (-10.4, -6.24, 1.0), M["black"], "side")
    part((-11.25, -7.05, 1.0), (-10.35, -6.18, 1.05), M["shelf"], "sideTop")
    c.build()
    single_box("Sign_Counter_Panel", (-9.15, -6.24, 0.18), (-4.9, -6.22, 0.88), M["signs"], kind="sign",
               uv_rects={"+y": sign_uv("counter_panel"), "*": sign_uv("counter_panel")})
    # Staff gate between the side unit and the counter.
    single_box("Solid_Counter_Gate", (-10.4, -6.66, 0.05), (-9.4, -6.62, 0.95), M["black"])
    # Till: screen on a post, card terminal, bar impulse box.
    till = Builder("Counter_Till")
    till.box((-7.86, -6.86, 1.05), (-7.74, -6.74, 1.3), M["black"])
    record("fixture", "till:post", (-7.86, -6.86, 1.05), (-7.74, -6.74, 1.3))
    till.box((-8.1, -6.84, 1.3), (-7.5, -6.80, 1.66), M["black"])
    record("fixture", "till:screenback", (-8.1, -6.84, 1.3), (-7.5, -6.80, 1.66))
    till.box((-8.06, -6.80, 1.34), (-7.54, -6.79, 1.62), M["screen"])
    record("fixture", "till:screen", (-8.06, -6.80, 1.34), (-7.54, -6.79, 1.62))
    till.box((-7.1, -6.62, 1.05), (-6.96, -6.42, 1.1), M["black"])
    record("fixture", "till:terminal", (-7.1, -6.62, 1.05), (-6.96, -6.42, 1.1))
    till.build()
    for i, kind in enumerate(["bar_coconut", "bar_choc", "bar_coconut", "bar_choc"]):
        place_product("Checkout", kind, -6.0 + i * 0.0, -6.36 - i * 0.05, 1.051, "+y")
    # Behind the counter: wall bays with shakers and magnesium.
    for i in range(5):
        wall_bay(f"WallBay_C{i}", (IX0 + i * BAY, IY0), "+y", "Checkout",
                 [["magnesium"], ["shaker"], ["vit_d3", "omega3"], ["shaker"], ["magnesium"]])
    # Hanging checkout sign.
    single_box("Sign_Checkout", (-8.4, -6.64, 2.9), (-6.4, -6.6, 3.2), M["signs"], kind="sign",
               uv_rects={"+y": sign_uv("CHECKOUT"), "-y": sign_uv("CHECKOUT"), "*": sign_uv("CHECKOUT")})
    rods = Builder("Sign_Checkout_Rods")
    for x in (-8.2, -6.6):
        rods.box((x - 0.01, -6.63, 3.2), (x + 0.01, -6.61, CEILING), M["black"])
    rods.build()


def build_promo():
    """Two stepped display platforms by the window, north of the door."""
    sets = ((0.4, ["tub_whey", "tub_whey_choc"]), (4.6, ["pouch_choc", "pouch_straw", "pouch_vanilla"]))
    tiers = ((-10.6, -9.0, 0.32, 0.62), (-10.35, -9.25, 0.62, 0.92), (-10.1, -9.5, 0.92, 1.22))
    for i, (y0, kinds) in enumerate(sets):
        name = f"Solid_Promo_{i}"
        b = Builder(name)
        parts = [((-10.8, y0, 0.0), (-8.8, y0 + 2.0, 0.3), M["black"], "base"),
                 ((-10.8, y0, 0.3), (-8.8, y0 + 2.0, 0.32), M["yellow"], "top")]
        for t, (tx0, tx1, z0, z1) in enumerate(tiers):
            parts.append(((tx0, y0 + 0.25, z0), (tx1, y0 + 1.75, z1), M["shelf"], f"tier{t}"))
        for mn, mx, mat, label in parts:
            b.box(mn, mx, mat)
            record("fixture", f"{name}:{label}", mn, mx)
        b.build()
        # The pyramid steps up on both sides: stock every ledge, labels facing
        # out (window side -x, shop side +x); the top tier holds one row
        # facing each way.
        ledges = (
            (-10.6, -10.35, 0.62, "-x"), (-10.35, -10.1, 0.92, "-x"),
            (-9.25, -9.0, 0.62, "+x"), (-9.5, -9.25, 0.92, "+x"),
            (-10.1, -9.8, 1.22, "-x"), (-9.8, -9.5, 1.22, "+x"),
        )
        for lx0, lx1, z, facing in ledges:
            fill_row("Promo", kinds, y0 + 0.25, y0 + 1.75, (lx0 + lx1) / 2, z + 0.001, facing, gap=0.03)


def build_posters():
    posters = [("poster_whey", (IX0 + 2.0, IY1)), ("poster_creatine", (2.0, IY0)),
               ("poster_bar", (8.0, IY1))]
    for i, (key, (x, y)) in enumerate(posters):
        facing = "-y" if y > 0 else "+y"
        wall_y = IY1 if facing == "-y" else IY0
        mn = (x - 0.5, wall_y - 0.01 if facing == "-y" else wall_y, 2.95)
        mx = (x + 0.5, wall_y if facing == "-y" else wall_y + 0.01, 3.85)
        single_box(f"Sign_Poster_{i}", mn, mx, M["signs"], kind="sign",
                   uv_rects={facing: sign_uv(key), "*": sign_uv(key)})


# Output -----------------------------------------------------------------------------
def finish_products():
    for builder in list(product_builders.values()):
        builder.build()


def render(out_dir):
    scene = bpy.context.scene
    try:
        scene.render.engine = "BLENDER_EEVEE_NEXT"
    except TypeError:
        scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 800
    scene.world = bpy.data.worlds.new("World")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.62, 0.72, 1)
    scene.world.node_tree.nodes["Background"].inputs[1].default_value = 0.6
    sun = bpy.data.lights.new("Sun", "SUN")
    sun.energy = 3.0
    so = bpy.data.objects.new("Sun", sun)
    so.rotation_euler = (math.radians(50), 0, math.radians(-60))
    scene.collection.objects.link(so)
    for i in range(4):
        for j in range(3):
            l = bpy.data.lights.new(f"Area{i}{j}", "AREA")
            l.energy = 900
            l.size = 3.0
            lo = bpy.data.objects.new(f"Area{i}{j}", l)
            lo.location = (-8 + i * 5.5, -6 + j * 6, CEILING - 0.1)
            scene.collection.objects.link(lo)
    cam = bpy.data.cameras.new("Cam")
    co = bpy.data.objects.new("Cam", cam)
    scene.collection.objects.link(co)
    scene.camera = co
    views = {
        "exterior": ((X0 - 14, -9, 6), (X0, 0, 2.2), 40),
        "interior": ((IX0 + 0.6, DOOR_Y, 1.7), (6, 1.5, 1.2), 70),
        "counter": ((-4.2, -2.5, 1.8), (-8.0, -7.5, 1.0), 60),
        "aisle": ((9.6, -1.6, 1.7), (-2, 7.5, 1.0), 65),
        "top": ((0, 0, 30), (0, 0, 0), 55),
    }
    roof_objs = [o for o in bpy.data.objects if o.name in ("Roof", "Ceiling", "Ceiling_Lights")]
    for name, (eye, look, fov) in views.items():
        for o in roof_objs:
            o.hide_render = name == "top"
        co.location = eye
        co.rotation_euler = (Vector(look) - Vector(eye)).to_track_quat("-Z", "Y").to_euler()
        cam.angle = math.radians(fov)
        scene.render.filepath = str(out_dir / f"store_{name}.png")
        bpy.ops.render.render(write_still=True)
    for o in roof_objs:
        o.hide_render = False


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    bpy.ops.wm.read_factory_settings(use_empty=True)
    make_materials()
    build_shell()
    build_sign_letters()
    build_contents()
    finish_products()
    (HERE / "layout_boxes.json").write_text(json.dumps({
        "footprint": {"depth": DEPTH, "width": WIDTH, "door_y": DOOR_Y, "door_w": DOOR_W},
        "boxes": boxes,
    }))
    bpy.ops.wm.save_as_mainfile(filepath=str(HERE / "protein_store_v2.blend"))
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(
        filepath=str(HERE / "protein_store_v2.glb"),
        export_format="GLB",
        use_selection=True,
        export_yup=True,
        export_apply=True,
        export_lights=False,
        export_cameras=False,
    )
    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in bpy.data.objects if o.type == "MESH")
    print(f"PROTEIN_STORE_V2_BUILT objects={len([o for o in bpy.data.objects if o.type == 'MESH'])} "
          f"triangles={tris} boxes={len(boxes)}")
    if "--no-render" not in argv:
        out = HERE / "renders"
        out.mkdir(exist_ok=True)
        render(out)


if __name__ == "__main__":
    main()
