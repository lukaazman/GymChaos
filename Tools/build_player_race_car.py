"""Build the player's race buggy: a stylised, low-poly open-top kart.

Crash Team Racing style: an orange-red body with a hollow nose over the
footwell, flared fenders, a big rear deck, chrome roll bars and front bumper,
a cyan faceted windscreen, a dark seat with a blue quilted pattern, big
treaded rear tyres, a side exhaust, round headlights and a toothed grille.

The parts follow the visitor-car cabin pipeline (Tools/build_vehicle_cabins.py,
Tools/split_vehicle_wheels.py) so the game's GymVehicleCabin, driver seat and
wheel spinner work on it unchanged:

  * ``Wheel_PL/PR/NL/NR`` (P = +Y half, the rear; L = +X, the driver side),
    origin on the axle, custom props ``wheel_radius`` and ``wheel_axis``, one
    material each (a palette texture) so the GLB loader keeps one node;
  * ``Body__Shell`` is the outer tub the penetration probe tests against;
  * ``Door_Driver__Shell/Glass/Card/Trim__<hex>`` is the short hinged side
    door, origin on its front hinge;
  * ``Glass__Body`` is the windscreen (keeps its own cyan GLB glass);
  * ``Driver_Seat__*``, ``Steering_Wheel__Rim__*`` and ``Cabin__*`` parts take
    flat colours from their names in the game;
  * ``Marker__Seat/Roof/Wheel/Floor/Hinge`` are tiny triangles the game reads
    (Roof is the top of the roll bar);
  * everything is top level (the runtime GLB loader ignores hierarchy).

Frame: metres for a 1.8 m driver, nose at -Y, driver side +X, Z up; 3.4 m
long. The game fits it to 3.4 * VehicleScale * CarSizeBoost (4.76 m).

Usage:
  blender -b --factory-startup -P Tools/build_player_race_car.py -- [--no-render]
"""
import argparse
import math
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "GymChaos/Assets/StreamingAssets/BodyBuilders/vehicles/player_buggy.glb"
EVIDENCE = ROOT / "Logs/agent/race/player-car"

LENGTH = 3.4
# The game scales the buggy by GAME_LENGTH / LENGTH; GymVehicleCabin expects
# the authored steering rim to be 0.19 m in the game.
GAME_LENGTH = 3.4 * 1.25 * 1.12
RIM_RADIUS = 0.19 * LENGTH / GAME_LENGTH

# Tub and door (driver side +X).
TUB_HALF = 0.60
WALL = 0.07
TUB_FRONT, TUB_REAR = -0.62, 0.80
FLOOR_BOTTOM, FLOOR_TOP = 0.10, 0.16
WALL_TOP = 0.74
# Short, low door hinged at the dash: the climb clip swings the legs out
# well ahead of the hips, so the open leaf must stand far forward of the seat,
# and it must stay short so that closing it misses the driver standing beside
# the car. Behind it the cockpit side stays open down to the floor (a kart
# step-in) so the climbing body can slide back off the leaf.
DOOR_FRONT, DOOR_REAR = TUB_FRONT + 0.02, 0.15
OPENING_REAR = 0.70
# The door stands on the floor pan's edge: its bottom is the sill the seat
# code lifts the feet over, so it must not sit below the pan top.
DOOR_BOTTOM, DOOR_TOP = FLOOR_TOP + 0.01, 0.725
HINGE = Vector((TUB_HALF, DOOR_FRONT, 0.46))
# The climb clip (car_exit) starts from a tall seat and the seat code may only
# lift the body over it to carry the feet over the sill, never lower it. The
# seated hips are placed so the soles rest on the footrest (Marker__Floor):
# a raised footrest and a high cushion keep them above the clip's own seat.
SEAT_Y, CUSHION_TOP = 0.0, 0.76
FOOTREST_TOP = 0.43
HOOP_Y, HOOP_TOP = 0.78, 1.86
WHEEL_CENTER = Vector((0.0, SEAT_Y - 0.40, 1.06))
BAR_R = 0.034

FRONT_AXLE_Y, FRONT_R, FRONT_W, FRONT_X = -1.08, 0.30, 0.22, 0.74
REAR_AXLE_Y, REAR_R, REAR_W, REAR_X = 1.14, 0.40, 0.36, 0.80

PALETTE = {
    "body": "F05417", "chrome": "C9CED6", "dark": "1A1B1F", "glass": "54D8F0",
    "lens": "FFF4D6", "tail": "E01515", "teeth": "F2EEDF", "engine": "2A2C31",
}
SEAT_HEX, QUILT_HEX = "1D1F26", "2F6BFF"
CARD_HEX, TRIM_HEX = "1C1C1F", "C9CCD1"
DASH_HEX, FLOOR_HEX, LINER_HEX = "1B1D22", "131417", "1C1C1F"
RIM_HEX = "18191C"
# Wheel palette texture cells (u centres): tyre, chrome rim, body hub, dark.
WHEEL_CELLS = {"tyre": (0.06, 0.06, 0.065), "rim": (0.80, 0.82, 0.85),
               "hub": (0.94, 0.33, 0.09), "dark": (0.22, 0.23, 0.25)}


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--no-render", action="store_true")
    return parser.parse_args(argv)


def hex_rgb(hex_color):
    return tuple(int(hex_color[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def material(name, hex_color, metallic=0.0, roughness=0.6, alpha=1.0, emission=0.0):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    # The game's GLB loader reads colour factors as sRGB (Material.color).
    rgb = hex_rgb(hex_color)
    mat.diffuse_color = (*rgb, alpha)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Alpha"].default_value = alpha
    if emission > 0:
        bsdf.inputs["Emission Color"].default_value = (*rgb, 1.0)
        bsdf.inputs["Emission Strength"].default_value = emission
    if alpha < 1:
        mat.surface_render_method = "BLENDED"
    return mat


def palette_material():
    """One material for every wheel: a 4-cell palette texture."""
    name = "Wheel_Palette"
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    width = 64
    image = bpy.data.images.new("wheel_palette", width=width, height=16, alpha=False)
    pixels = []
    cells = list(WHEEL_CELLS.values())
    for _y in range(16):
        for x in range(width):
            r, g, b = cells[min(len(cells) - 1, x * len(cells) // width)]
            pixels += [r, g, b, 1.0]
    image.pixels = pixels
    image.pack()
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    bsdf = nodes.get("Principled BSDF")
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = image
    tex.interpolation = "Closest"
    mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Metallic"].default_value = 0.25
    bsdf.inputs["Roughness"].default_value = 0.5
    return mat


def cell_u(cell):
    keys = list(WHEEL_CELLS)
    return (keys.index(cell) + 0.5) / len(keys)


class Part:
    """A bmesh with one material slot per added primitive group."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")
        self.materials = []

    def slot(self, mat):
        if mat not in self.materials:
            self.materials.append(mat)
        return self.materials.index(mat)

    def add(self, src, mat=None, cell=None):
        """Merges bmesh `src` (freed) into this part."""
        index = self.slot(mat) if mat is not None else 0
        mapping = {v: self.bm.verts.new(v.co) for v in src.verts}
        for f in src.faces:
            try:
                face = self.bm.faces.new([mapping[v] for v in f.verts])
            except ValueError:
                continue
            face.material_index = index
            face.smooth = f.smooth
            if cell is not None:
                for loop in face.loops:
                    loop[self.uv].uv = (cell_u(cell), 0.5)
        src.free()

    def emit(self, origin=None, props=None):
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts, dist=1e-5)
        if origin is not None:
            self.bm.transform(Matrix.Translation(-origin))
        mesh = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(mesh)
        self.bm.free()
        parts = self.name.split("__")
        if not self.materials and len(parts) >= 3:
            self.materials.append(material("Cabin_" + parts[2], parts[2], 0.0, 0.5))
        for mat in self.materials:
            mesh.materials.append(mat)
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        if origin is not None:
            obj.location = origin
        for key, value in (props or {}).items():
            obj[key] = value
        return obj


# ---------------------------------------------------------------- primitives

def box(center, size, bevel=0.0, rotation=None):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bm.transform(Matrix.Diagonal((*size, 1.0)))
    if bevel > 0:
        # Absolute chamfer, at most 45 % of the thinnest side.
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=min(bevel, 0.45 * min(size)),
                        segments=1, profile=0.5, affect="EDGES", clamp_overlap=True)
    m = Matrix.Identity(4) if rotation is None else rotation.to_4x4()
    bm.transform(Matrix.Translation(Vector(center)) @ m)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def span_box(lo, hi, bevel=0.0):
    lo, hi = Vector(lo), Vector(hi)
    return box((lo + hi) * 0.5, tuple(hi - lo), bevel)


def cylinder(center, axis, radius, depth, segments=16, radius2=None, smooth=True):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segments, radius1=radius,
                          radius2=radius if radius2 is None else radius2, depth=depth)
    rot = Vector((0, 0, 1)).rotation_difference(Vector(axis).normalized()).to_matrix().to_4x4()
    bm.transform(Matrix.Translation(Vector(center)) @ rot)
    for f in bm.faces:
        f.smooth = smooth and len(f.verts) == 4
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def tube(points, radius, segments=10):
    """A bent bar along `points` (joints are spheres-free mitred rings)."""
    bm = bmesh.new()
    pts = [Vector(p) for p in points]
    rings = []
    for i, p in enumerate(pts):
        if i == 0:
            tangent = (pts[1] - p).normalized()
        elif i == len(pts) - 1:
            tangent = (p - pts[i - 1]).normalized()
        else:
            tangent = ((pts[i + 1] - p).normalized() + (p - pts[i - 1]).normalized()).normalized()
        u = tangent.orthogonal().normalized()
        v = tangent.cross(u).normalized()
        rings.append([bm.verts.new(p + (u * math.cos(2 * math.pi * k / segments) +
                                        v * math.sin(2 * math.pi * k / segments)) * radius)
                      for k in range(segments)])
    for a, b in zip(rings, rings[1:]):
        for k in range(segments):
            f = bm.faces.new((a[k], a[(k + 1) % segments], b[(k + 1) % segments], b[k]))
            f.smooth = True
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def rounded_section(half_w, z_bot, z_top, radius, per_corner=3, x_offset=0.0):
    """Closed loop (x, z) of a rounded rectangle, counter-clockwise from +X."""
    r = min(radius, half_w * 0.9, (z_top - z_bot) * 0.45)
    corners = [((half_w - r, z_top - r), 0.0), ((-half_w + r, z_top - r), 0.5 * math.pi),
               ((-half_w + r, z_bot + r), math.pi), ((half_w - r, z_bot + r), 1.5 * math.pi)]
    loop = []
    for (cx, cz), start in corners:
        for k in range(per_corner + 1):
            a = start + 0.5 * math.pi * k / per_corner
            loop.append((cx + r * math.cos(a) + x_offset, cz + r * math.sin(a)))
    return loop


def loft(sections, cap_first=True, cap_last=True):
    """Bridge equal-length (x, z) loops placed at their y into a skin."""
    bm = bmesh.new()
    rings = [[bm.verts.new((x, y, z)) for x, z in loop] for y, loop in sections]
    n = len(rings[0])
    for a, b in zip(rings, rings[1:]):
        for k in range(n):
            f = bm.faces.new((a[k], a[(k + 1) % n], b[(k + 1) % n], b[k]))
            f.smooth = True
    if cap_first:
        bm.faces.new(rings[0])
    if cap_last:
        bm.faces.new(list(reversed(rings[-1])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def arch(cx, cy, cz, r_in, r_out, x0, x1, a0, a1, segments=14):
    """Fender: an annulus sector in the y/z plane extruded from x0 to x1.
    Angle 0 is the front (-Y), pi/2 the top, pi the rear."""
    bm = bmesh.new()

    def ring(r, x):
        out = []
        for k in range(segments + 1):
            a = a0 + (a1 - a0) * k / segments
            out.append(bm.verts.new((x, cy - r * math.cos(a), cz + r * math.sin(a))))
        return out

    oi, oo, ii, io = ring(r_out, x0), ring(r_out, x1), ring(r_in, x0), ring(r_in, x1)
    for k in range(segments):
        for quad in ((oi[k], oo[k], oo[k + 1], oi[k + 1]),      # outer skin
                     (ii[k], ii[k + 1], io[k + 1], io[k]),      # inner skin
                     (oi[k], oi[k + 1], ii[k + 1], ii[k]),      # x0 side
                     (oo[k], io[k], io[k + 1], oo[k + 1])):     # x1 side
            f = bm.faces.new(quad)
            f.smooth = True
    for end in (0, segments):
        bm.faces.new((oi[end], ii[end], io[end], oo[end]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def torus(center, normal, major, minor, ring=28, tubes=8):
    bm = bmesh.new()
    n = Vector(normal).normalized()
    u = n.orthogonal().normalized()
    v = n.cross(u)
    rings = []
    for i in range(ring):
        t = 2 * math.pi * i / ring
        radial = u * math.cos(t) + v * math.sin(t)
        rings.append([bm.verts.new(Vector(center) + radial * (major + minor * math.cos(2 * math.pi * j / tubes)) +
                                   n * (minor * math.sin(2 * math.pi * j / tubes))) for j in range(tubes)])
    for i in range(ring):
        for j in range(tubes):
            f = bm.faces.new((rings[i][j], rings[(i + 1) % ring][j],
                              rings[(i + 1) % ring][(j + 1) % tubes], rings[i][(j + 1) % tubes]))
            f.smooth = True
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def flip(bm):
    bmesh.ops.reverse_faces(bm, faces=bm.faces)
    return bm


def marker(name, point):
    bm = bmesh.new()
    p = Vector(point)
    e = 0.0005
    bm.faces.new((bm.verts.new(p), bm.verts.new(p + Vector((e, 0, 0))), bm.verts.new(p + Vector((0, e, 0)))))
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    mesh.materials.append(material("Marker", "FF00FF"))
    return obj


# ------------------------------------------------------------------- parts

NOSE = [  # y, half width, bottom, top, corner radius
    # A raised cowl over the footwell opening: the seated toes reach well
    # under the hood, below the line from the driver's shoulders.
    (TUB_FRONT, TUB_HALF, FLOOR_BOTTOM, 0.90, 0.12),
    (-0.92, 0.58, 0.17, 0.80, 0.14),
    (-1.22, 0.50, 0.19, 0.65, 0.15),
    (-1.50, 0.42, 0.22, 0.57, 0.14),
    (-1.62, 0.34, 0.26, 0.51, 0.11),
]
DECK = [
    (TUB_REAR - 0.02, 0.55, 0.20, 0.86, 0.12),
    (1.10, 0.52, 0.24, 0.80, 0.15),
    (1.48, 0.46, 0.28, 0.70, 0.15),
    (1.64, 0.38, 0.32, 0.60, 0.11),
]


def hood_top(y):
    """Nose top height at `y` (linear between the loft sections)."""
    for (y0, _w0, _b0, t0, _r0), (y1, _w1, _b1, t1, _r1) in zip(NOSE, NOSE[1:]):
        if y1 <= y <= y0:
            return t0 + (t1 - t0) * (y - y0) / (y1 - y0)
    return NOSE[0][3]


def build_body(mats):
    body = Part("Body__Shell")
    orange = mats["body"]
    # Hollow nose over the footwell: open toward the cockpit.
    nose = [(y, rounded_section(w, b, t, r)) for y, w, b, t, r in NOSE]
    body.add(loft(nose, cap_first=False, cap_last=True), orange)
    # Tub: floor pan, passenger wall, driver wall either side of the door,
    # and the bulkhead behind the seat.
    body.add(span_box((-TUB_HALF, TUB_FRONT, FLOOR_BOTTOM), (TUB_HALF, TUB_REAR, FLOOR_TOP), 0.01), orange)
    body.add(span_box((-TUB_HALF, TUB_FRONT, FLOOR_BOTTOM), (-TUB_HALF + WALL, TUB_REAR, WALL_TOP), 0.02), orange)
    body.add(span_box((TUB_HALF - WALL, OPENING_REAR + 0.01, FLOOR_BOTTOM), (TUB_HALF, TUB_REAR, WALL_TOP), 0.02), orange)
    body.add(span_box((-TUB_HALF, TUB_REAR - 0.08, FLOOR_BOTTOM), (TUB_HALF, TUB_REAR, 0.88), 0.02), orange)
    # Rear deck over the engine.
    deck = [(y, rounded_section(w, b, t, r)) for y, w, b, t, r in DECK]
    body.add(loft(deck), orange)
    # Flared fenders and the struts/pods that carry them.
    for side in (1, -1):
        x_in = side * (FRONT_X - FRONT_W * 0.5 - 0.04)
        x_out = side * (FRONT_X + FRONT_W * 0.5 + 0.05)
        body.add(arch(0, FRONT_AXLE_Y, FRONT_R, FRONT_R + 0.05, FRONT_R + 0.11,
                      min(x_in, x_out), max(x_in, x_out), math.radians(8), math.radians(172)), orange)
        body.add(span_box((min(side * 0.44, side * 0.62), FRONT_AXLE_Y - 0.08, 0.60),
                          (max(side * 0.44, side * 0.62), FRONT_AXLE_Y + 0.08, 0.67), 0.02), orange)
        x_in = side * (REAR_X - REAR_W * 0.5 - 0.03)
        x_out = side * (REAR_X + REAR_W * 0.5 + 0.06)
        body.add(arch(0, REAR_AXLE_Y, 0.40, REAR_R + 0.05, REAR_R + 0.12,
                      min(x_in, x_out), max(x_in, x_out), math.radians(25), math.radians(160), 14), orange)
        body.add(span_box((min(side * 0.48, side * 0.60), REAR_AXLE_Y - 0.22, 0.46),
                          (max(side * 0.48, side * 0.60), REAR_AXLE_Y + 0.22, 0.66), 0.03), orange)
    return body.emit()


def build_chrome(mats):
    bars = Part("Roll_Bars")
    chrome = mats["chrome"]
    x, top = 0.50, HOOP_TOP - BAR_R
    bars.add(tube([(x, HOOP_Y, 0.82), (x, HOOP_Y, top - 0.16), (x - 0.10, HOOP_Y, top),
                   (-x + 0.10, HOOP_Y, top), (-x, HOOP_Y, top - 0.16), (-x, HOOP_Y, 0.82)], BAR_R, 12), chrome)
    for side in (1, -1):
        bars.add(tube([(side * 0.46, HOOP_Y + 0.01, top - 0.20), (side * 0.40, 1.30, 0.80)], BAR_R * 0.85, 10), chrome)
        # Front bumper mounts.
        bars.add(tube([(side * 0.30, -1.50, 0.33), (side * 0.36, -1.665, 0.32)], BAR_R * 0.7, 8), chrome)
    # Front bumper bar.
    bars.add(tube([(0.58, -1.50, 0.30), (0.52, -1.64, 0.32), (0.40, -1.666, 0.32), (-0.40, -1.666, 0.32),
                   (-0.52, -1.64, 0.32), (-0.58, -1.50, 0.30)], BAR_R, 12), chrome)
    # Door and nose trim rails, tail pipe of the side exhaust is separate.
    return bars.emit()


def build_exhaust(mats):
    part = Part("Exhaust")
    chrome = mats["chrome"]
    # Out of the passenger side of the deck, behind the rear tyre and fender.
    part.add(tube([(-0.30, 1.62, 0.40), (-0.60, 1.645, 0.40), (-0.84, 1.63, 0.44), (-0.94, 1.62, 0.52)],
                  0.045, 12), chrome)
    tip = Vector((-0.45, 0.0, 0.9)).normalized()
    part.add(cylinder(Vector((-0.94, 1.62, 0.52)) + tip * 0.08, tip, 0.05, 0.16, 14, radius2=0.068), chrome)
    part.add(cylinder(Vector((-0.94, 1.62, 0.52)) + tip * 0.161, tip, 0.056, 0.004, 14), mats["dark"])
    # Engine block and intake on the deck.
    eng = mats["engine"]
    part.add(span_box((-0.30, 0.92, 0.80), (0.30, 1.30, 0.92), 0.03), eng)
    for k in range(4):
        part.add(cylinder((-0.21 + 0.14 * k, 1.10, 0.97), (0, 0, 1), 0.045, 0.10, 10), chrome)
    return part.emit()


def build_face(mats):
    lights = Part("Headlights")
    for side in (1, -1):
        c = Vector((side * 0.27, -1.58, 0.47))
        lights.add(cylinder(c + Vector((0, 0.02, 0)), (0, -1, 0), 0.10, 0.07, 18), mats["chrome"])
        lights.add(cylinder(c + Vector((0, -0.02, 0)), (0, -1, 0), 0.08, 0.02, 18, smooth=False), mats["lens"])
    lights.emit()
    grille = Part("Grille")
    grille.add(span_box((-0.16, -1.635, 0.30), (0.16, -1.60, 0.43), 0.015), mats["dark"])
    for k in range(6):
        x = -0.125 + 0.05 * k
        grille.add(box((x, -1.64, 0.405), (0.034, 0.02, 0.05), 0.006), mats["teeth"])
        grille.add(box((x + 0.025 if k < 5 else x - 0.025, -1.64, 0.325), (0.03, 0.02, 0.04), 0.006), mats["teeth"])
    grille.emit()
    tail = Part("Tail_Lights")
    for side in (1, -1):
        tail.add(box((side * 0.25, 1.66, 0.50), (0.16, 0.08, 0.07), 0.015), mats["tail"])
    # Rear bumper strip: also the car's rear end at +1.70.
    tail.add(span_box((-0.34, 1.62, 0.34), (0.34, 1.70, 0.40), 0.015), mats["dark"])
    tail.emit()


def build_glass(mats):
    glass = Part("Glass__Body")
    cyan = mats["glass"]
    # On the hood ahead of the dash: the seated knees ride high, close behind it.
    base_y, top_z, lean = TUB_FRONT - 0.12, 1.24, math.radians(10)
    base_z = hood_top(base_y) - 0.02

    def facet(x0, x1, y0_off, y1_off):
        rise = top_z - base_z
        p = [Vector((x0, base_y + y0_off, base_z)), Vector((x1, base_y + y1_off, base_z)),
             Vector((x1, base_y + y1_off + rise * math.tan(lean), top_z)),
             Vector((x0, base_y + y0_off + rise * math.tan(lean), top_z))]
        bm = bmesh.new()
        n = (p[1] - p[0]).cross(p[3] - p[0]).normalized() * 0.006
        front = [bm.verts.new(q - n) for q in p]
        back = [bm.verts.new(q + n) for q in p]
        bm.faces.new(front)
        bm.faces.new(list(reversed(back)))
        for k in range(4):
            bm.faces.new((front[k], back[k], back[(k + 1) % 4], front[(k + 1) % 4]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        return bm

    glass.add(facet(-0.20, 0.20, 0.0, 0.0), cyan)
    glass.add(facet(0.20, 0.40, 0.0, 0.08), cyan)
    glass.add(facet(-0.40, -0.20, 0.08, 0.0), cyan)
    glass.emit()
    frame = Part("Windscreen_Frame")
    rise = top_z - base_z
    top_y = base_y + rise * math.tan(lean)
    frame.add(tube([(-0.40, top_y + 0.08, top_z), (-0.20, top_y, top_z), (0.20, top_y, top_z),
                    (0.40, top_y + 0.08, top_z)], 0.014, 8), mats["chrome"])
    frame.emit()


def build_door(mats):
    orange = mats["body"]
    shell = Part("Door_Driver__Shell")
    shell.add(span_box((TUB_HALF - WALL, DOOR_FRONT + 0.01, DOOR_BOTTOM),
                       (TUB_HALF, DOOR_REAR - 0.01, DOOR_TOP), 0.02), orange)
    shell.emit(origin=HINGE)
    card = Part(f"Door_Driver__Card__{CARD_HEX}")
    card.add(span_box((TUB_HALF - WALL - 0.025, DOOR_FRONT + 0.05, 0.27),
                      (TUB_HALF - WALL + 0.002, DOOR_REAR - 0.05, 0.66), 0.01))
    card.emit(origin=HINGE)
    trim = Part(f"Door_Driver__Trim__{TRIM_HEX}")
    trim.add(span_box((TUB_HALF - WALL - 0.005, DOOR_FRONT + 0.01, DOOR_TOP - 0.02),
                      (TUB_HALF + 0.01, DOOR_REAR - 0.01, DOOR_TOP + 0.012), 0.006))
    trim.emit(origin=HINGE)
    glass = Part("Door_Driver__Glass")
    p0 = Vector((TUB_HALF - 0.035, DOOR_FRONT + 0.03, DOOR_TOP))
    bm = box(p0 + Vector((0, 0.13, 0.07)), (0.012, 0.26, 0.14))
    bm.transform(Matrix.Translation(p0) @ Matrix.Rotation(math.radians(-14), 4, "X") @ Matrix.Translation(-p0))
    glass.add(bm, mats["glass"])
    glass.emit(origin=HINGE)


def build_interior(mats):
    # Seat: low racing bucket with a blue quilted cushion and backrest.
    seat_origin = Vector((0.0, SEAT_Y, CUSHION_TOP))
    seat = Part(f"Driver_Seat__Seat__{SEAT_HEX}")
    seat.add(span_box((-0.25, SEAT_Y - 0.22, CUSHION_TOP - 0.14), (0.25, SEAT_Y + 0.24, CUSHION_TOP), 0.04))
    # Pedestal down to the floor (it rises with the cushion in the game).
    seat.add(span_box((-0.17, SEAT_Y - 0.14, FLOOR_TOP), (0.17, SEAT_Y + 0.18, CUSHION_TOP - 0.13), 0.02))
    back_pivot = Vector((0, SEAT_Y + 0.24, CUSHION_TOP))
    tilt = Matrix.Translation(back_pivot) @ Matrix.Rotation(math.radians(-12), 4, "X") @ Matrix.Translation(-back_pivot)
    back = span_box((-0.25, SEAT_Y + 0.20, CUSHION_TOP - 0.04), (0.25, SEAT_Y + 0.31, CUSHION_TOP + 0.58), 0.04)
    back.transform(tilt)
    seat.add(back)
    for side in (1, -1):  # bolsters
        bolster = span_box((side * 0.22 - 0.035, SEAT_Y - 0.18, CUSHION_TOP - 0.02),
                           (side * 0.22 + 0.035, SEAT_Y + 0.22, CUSHION_TOP + 0.07), 0.03)
        seat.add(bolster)
    seat.emit(origin=seat_origin)
    quilt = Part(f"Driver_Seat__Accent__{QUILT_HEX}")
    diamond = Matrix.Rotation(math.radians(45), 4, "Z")
    for i in range(3):
        for j in range(3):
            c = Vector((-0.12 + 0.12 * i, SEAT_Y - 0.13 + 0.12 * j, CUSHION_TOP + 0.004))
            q = box((0, 0, 0), (0.07, 0.07, 0.012), 0.0, diamond.to_3x3())
            q.transform(Matrix.Translation(c))
            quilt.add(q)
    for i in range(3):
        for j in range(4):
            c = Vector((-0.12 + 0.12 * i, SEAT_Y + 0.195, CUSHION_TOP + 0.08 + 0.13 * j))
            q = box((0, 0, 0), (0.07, 0.012, 0.07), 0.0, Matrix.Rotation(math.radians(45), 3, "Y"))
            q.transform(tilt @ Matrix.Translation(c))
            quilt.add(q)
    quilt.emit(origin=seat_origin)

    # Steering wheel facing the driver, tilted 24 degrees up (GymVehicleCabin).
    wheel_center = WHEEL_CENTER.copy()
    tilt_n = math.radians(24)
    normal = Vector((0.0, math.cos(tilt_n), math.sin(tilt_n)))
    rim = Part(f"Steering_Wheel__Rim__{RIM_HEX}")
    rim.add(torus(wheel_center, normal, RIM_RADIUS, 0.016, 26, 8))
    rim.add(cylinder(wheel_center - normal * 0.01, normal, 0.035, 0.03, 12))
    for k in range(3):
        a = math.radians(90 + 120 * k)
        u = normal.orthogonal().normalized()
        v = normal.cross(u)
        d = (u * math.cos(a) + v * math.sin(a))
        rim.add(tube([wheel_center + d * 0.03, wheel_center + d * (RIM_RADIUS - 0.01)], 0.009, 6))
    rim.add(cylinder(wheel_center - normal * 0.17, normal, 0.018, 0.30, 8))
    rim.emit(origin=wheel_center)

    dash = Part(f"Cabin__Dash__{DASH_HEX}")
    dash.add(span_box((-0.52, TUB_FRONT - 0.02, 0.62), (0.52, TUB_FRONT + 0.10, 0.87), 0.02))
    # Column binnacle up to the steering column.
    column_end = WHEEL_CENTER - Vector((0.0, math.cos(math.radians(24)), math.sin(math.radians(24)))) * 0.30
    dash.add(span_box((-0.13, TUB_FRONT - 0.02, 0.60), (0.13, column_end.y + 0.04, column_end.z + 0.05), 0.03))
    dash.emit()
    gauges = Part("Cabin__Screen__0A0D11")
    for x in (-0.12, 0.12):
        gauges.add(cylinder((x, TUB_FRONT + 0.105, 0.76), (0, 1, 0), 0.045, 0.01, 14, smooth=False))
    gauges.emit()
    floor = Part(f"Cabin__Floor__{FLOOR_HEX}")
    floor.add(span_box((-TUB_HALF + WALL, TUB_FRONT, FLOOR_TOP - 0.004),
                       (TUB_HALF - WALL, TUB_REAR - 0.08, FLOOR_TOP + 0.006), 0.0))
    # Raised footrest in the nose, with a toe board up from the cockpit floor.
    floor.add(span_box((-0.40, -1.25, FLOOR_TOP), (0.40, TUB_FRONT + 0.06, FOOTREST_TOP), 0.02))
    toe = span_box((-0.40, TUB_FRONT + 0.02, FLOOR_TOP), (0.40, TUB_FRONT + 0.22, FLOOR_TOP + 0.03), 0.0)
    hinge = Vector((0, TUB_FRONT + 0.06, FOOTREST_TOP))
    toe.transform(Matrix.Translation(hinge) @ Matrix.Rotation(math.radians(-40), 4, "X") @ Matrix.Translation(-Vector((0, TUB_FRONT + 0.06, FLOOR_TOP + 0.03))))
    floor.add(toe)
    floor.emit()
    # Inside of the hollow nose, so the footwell is never see-through.
    liner = Part(f"Cabin__Liner__{LINER_HEX}")
    inset = [(y, rounded_section(w - 0.012, b + 0.012, t - 0.012, max(0.02, r - 0.012)))
             for y, w, b, t, r in NOSE]
    liner.add(flip(loft(inset, cap_first=False, cap_last=True)))
    liner.emit()


def build_wheel(name, cx, cy, cz, radius, width, side):
    """Tyre with chevron tread, chrome rim and orange hub; origin on the axle."""
    part = Part(name)
    pal = palette_material()
    center = Vector((cx, cy, cz))
    axis = Vector((1, 0, 0))
    lug = 0.028
    body_r = radius - lug
    rim_r = radius * 0.58
    # Tyre carcass: a revolved rounded profile (x across, r radial).
    profile = []
    half = width * 0.5
    for k in range(7):  # outer shoulder, round
        a = math.pi * k / 6
        profile.append((-half * math.cos(a), body_r - 0.05 + 0.05 * math.sin(a)))
    profile = [(-half, rim_r)] + profile + [(half, rim_r)]
    segments = 32
    bm = bmesh.new()
    rings = []
    for s in range(segments):
        t = 2 * math.pi * s / segments
        rings.append([bm.verts.new((x, math.cos(t) * r, math.sin(t) * r)) for x, r in profile])
    for s in range(segments):
        a, b = rings[s], rings[(s + 1) % segments]
        for k in range(len(profile) - 1):
            f = bm.faces.new((a[k], a[k + 1], b[k + 1], b[k]))
            f.smooth = True
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.transform(Matrix.Translation(center))
    part.add(bm, pal, "tyre")
    # Chevron lugs, staggered across the tread.
    lugs = 20
    for k in range(lugs):
        t = 2 * math.pi * (k + 0.5 * (k % 2)) / lugs
        for half_side in (-1, 1):
            x = half_side * width * 0.22
            yaw = Matrix.Rotation(math.radians(22 * half_side), 4, "Z")
            block = box((0, 0, 0), (width * 0.38, 0.075, lug * 2))
            block.transform(yaw)
            place = (Matrix.Translation(center) @ Matrix.Rotation(t, 4, "X") @
                     Matrix.Translation((x, 0, body_r)))
            block.transform(place)
            part.add(block, pal, "tyre")
    # Rim: dark barrel, chrome face with five spokes, orange hub cap.
    # Chrome face just proud of the dark barrel.
    outer_x = side * (width * 0.43 + 0.012)
    part.add(cylinder(center, axis, rim_r, width * 0.86, 20), pal, "dark")
    part.add(cylinder(center + Vector((outer_x - side * 0.01, 0, 0)), axis, rim_r * 0.96, 0.03, 20), pal, "rim")
    for k in range(5):
        t = 2 * math.pi * k / 5
        spoke = box((0, 0, 0), (0.03, 0.05, rim_r * 0.85), 0.008)
        spoke.transform(Matrix.Translation(center + Vector((outer_x + side * 0.012, 0, 0))) @
                        Matrix.Rotation(t, 4, "X") @ Matrix.Translation((0, 0, rim_r * 0.45)))
        part.add(spoke, pal, "rim")
    part.add(cylinder(center + Vector((outer_x + side * 0.025, 0, 0)), axis, rim_r * 0.30, 0.04, 14), pal, "hub")
    return part.emit(origin=center, props={"wheel_radius": radius, "wheel_axis": "X"})


def build_markers():
    marker("Marker__Seat", (0.0, SEAT_Y, CUSHION_TOP))
    marker("Marker__Roof", (0.0, SEAT_Y + 0.01, HOOP_TOP))
    marker("Marker__Wheel", tuple(WHEEL_CENTER))
    marker("Marker__Floor", (0.0, SEAT_Y, FOOTREST_TOP))
    marker("Marker__Hinge", tuple(HINGE))


def build():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    mats = {
        "body": material("Buggy_Body", PALETTE["body"], 0.15, 0.32),
        "chrome": material("Buggy_Chrome", PALETTE["chrome"], 0.9, 0.18),
        "dark": material("Buggy_Dark", PALETTE["dark"], 0.0, 0.6),
        "glass": material("Buggy_Windscreen", PALETTE["glass"], 0.0, 0.05, alpha=0.5),
        "lens": material("Buggy_Headlight", PALETTE["lens"], 0.0, 0.2, emission=3.0),
        "tail": material("Buggy_Taillight", PALETTE["tail"], 0.0, 0.3, emission=2.0),
        "teeth": material("Buggy_Teeth", PALETTE["teeth"], 0.0, 0.4),
        "engine": material("Buggy_Engine", PALETTE["engine"], 0.4, 0.45),
    }
    build_body(mats)
    build_chrome(mats)
    build_exhaust(mats)
    build_face(mats)
    build_glass(mats)
    build_door(mats)
    build_interior(mats)
    for side, letter in ((1, "L"), (-1, "R")):
        build_wheel(f"Wheel_N{letter}", side * FRONT_X, FRONT_AXLE_Y, FRONT_R, FRONT_R, FRONT_W, side)
        build_wheel(f"Wheel_P{letter}", side * REAR_X, REAR_AXLE_Y, REAR_R, REAR_R, REAR_W, side)
    build_markers()


def export():
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(filepath=str(OUTPUT), export_format="GLB", use_selection=True,
                              export_extras=True, export_yup=True, export_apply=True)
    print(f"GYMCHAOS_PLAYER_CAR_EXPORTED path={OUTPUT}")


def render():
    EVIDENCE.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    try:
        scene.render.engine = "BLENDER_EEVEE_NEXT"
    except TypeError:
        scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1100
    scene.render.resolution_y = 760
    scene.view_settings.view_transform = "Standard"
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.42, 0.46, 0.52, 1.0)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.9
    scene.world = world
    for o in bpy.data.objects:
        if o.name.startswith("Marker__"):
            o.hide_render = True
    ground = bpy.data.meshes.new("ground")
    gbm = bmesh.new()
    bmesh.ops.create_grid(gbm, x_segments=1, y_segments=1, size=12)
    gbm.to_mesh(ground)
    gbm.free()
    gobj = bpy.data.objects.new("ground", ground)
    ground.materials.append(material("Ground", "6B6E73", 0.0, 0.9))
    scene.collection.objects.link(gobj)
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(48), math.radians(8), math.radians(35))
    scene.collection.objects.link(sun)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.lens = 50
    door = [o for o in bpy.data.objects if o.name.startswith("Door_Driver")]
    target = Vector((0, 0, 0.6))
    views = {
        "front": (Vector((0.0, -7.5, 1.3)), False),
        "side": (Vector((8.0, 0.0, 1.1)), False),
        "top34_front": (Vector((5.2, -5.4, 4.2)), False),
        "top34_rear": (Vector((-5.0, 5.6, 4.0)), False),
        "top": (Vector((0.01, 0.0, 9.0)), False),
        "door_open": (Vector((6.0, 2.4, 2.6)), True),
    }
    for view, (pos, open_door) in views.items():
        for o in door:
            o.rotation_euler = (0, 0, math.radians(-78) if open_door else 0)
        cam.location = pos
        cam.rotation_euler = (target - pos).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = str(EVIDENCE / f"blender-{view}.png")
        bpy.ops.render.render(write_still=True)
    for o in door:
        o.rotation_euler = (0, 0, 0)


def main():
    args = parse_args()
    build()
    export()
    if not args.no_render:
        render()
    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons)
               for o in bpy.data.objects if o.type == "MESH" and not o.name.startswith(("ground", "Marker__")))
    print(f"GYMCHAOS_PLAYER_CAR_BUILT tris={tris} objects={len(bpy.data.objects)}")


if __name__ == "__main__":
    main()
