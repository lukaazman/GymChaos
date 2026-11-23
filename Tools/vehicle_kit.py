"""Shared builder for the remade low-poly vehicles (Remake style).

Each ``Tools/build_vehicle_<id>.py`` describes one car as a spec dict plus a
``details(k)`` callback and calls :func:`run`. The kit builds the parts the
game already reads (see Tools/build_player_race_car.py for the same
contract):

  * ``Body__Shell``: faceted body loft with wheel arches cut out and the cabin
    top open, solidified so the tub has an inside;
  * ``Door_Driver__Shell`` / ``Door_Driver__Glass``: driver door cut out of the
    body side, origin on its front hinge (driver side is +X);
  * ``Cabin__Glass``: windscreen, rear and side windows (the game makes every
    ``Glass`` role transparent);
  * ``Driver_Seat__*``, ``Steering_Wheel__Rim__*``, ``Cabin__*``: interior
    parts that take flat colours from their names;
  * ``Marker__Seat/Roof/Wheel/Floor/Hinge``;
  * ``Wheel_FL/FR/RL/RR``: one node each, origin on the axle;
  * lamp lenses on materials ``Lamp_Headlight``, ``Lamp_Taillight``,
    ``Lamp_Reverse`` (police: ``Lamp_StrobeRed``, ``Lamp_StrobeBlue``).

Frame: metres, nose at -Y, driver side +X, Z up, ground at z = 0. Everything
is top level (the runtime GLB loader ignores hierarchy). Output:
GymChaos/Assets/StreamingAssets/BodyBuilders/vehicles/remake/<id>.glb, plus a
render sheet next to the reference image in Logs/agent/remake/vehicles.
"""
import argparse
import math
import sys
from pathlib import Path

import bmesh
import bpy
import subprocess
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[1]
OUT_DIR = ROOT / "GymChaos/Assets/StreamingAssets/BodyBuilders/vehicles/remake"
EVIDENCE = ROOT / "Logs/agent/remake/vehicles"
REFS = ROOT / "remake"
# Visitor and police cars are fitted to this length in the game; the cabin
# code expects the authored steering rim to be 0.19 m there.
CAR_GAME_LENGTH = 4.0 * 1.25 * 1.12


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--no-render", action="store_true")
    return parser.parse_args(argv)


def hex_rgb(hex_color):
    return tuple(int(hex_color[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def material(name, hex_color, metallic=0.0, roughness=0.6, alpha=1.0):
    mat = bpy.data.materials.get(name)
    if mat is not None:
        return mat
    mat = bpy.data.materials.new(name)
    # The game's GLB loader reads colour factors as sRGB (Material.color).
    rgb = hex_rgb(hex_color)
    mat.diffuse_color = (*rgb, alpha)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Alpha"].default_value = alpha
    if alpha < 1:
        mat.surface_render_method = "BLENDED"
    return mat


# ---------------------------------------------------------------- primitives

def box(center, size, bevel=0.0, rotation=None):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bm.transform(Matrix.Diagonal((*size, 1.0)))
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=min(bevel, 0.45 * min(size)),
                        segments=1, profile=0.5, affect="EDGES", clamp_overlap=True)
    m = Matrix.Identity(4) if rotation is None else rotation.to_4x4()
    bm.transform(Matrix.Translation(Vector(center)) @ m)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def span_box(lo, hi, bevel=0.0):
    lo, hi = Vector(lo), Vector(hi)
    return box((lo + hi) * 0.5, tuple(hi - lo), bevel)


def beam(p0, p1, width, depth=None):
    """Square bar from p0 to p1 (pillars, racks, bars)."""
    p0, p1 = Vector(p0), Vector(p1)
    axis = p1 - p0
    rot = Vector((0, 0, 1)).rotation_difference(axis.normalized()).to_matrix()
    return box((p0 + p1) * 0.5, (width, depth or width, axis.length), 0.0, rot)


def cylinder(center, axis, radius, depth, segments=12, radius2=None):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segments, radius1=radius,
                          radius2=radius if radius2 is None else radius2, depth=depth)
    rot = Vector((0, 0, 1)).rotation_difference(Vector(axis).normalized()).to_matrix().to_4x4()
    bm.transform(Matrix.Translation(Vector(center)) @ rot)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def torus(center, normal, major, minor, ring=20, tubes=6):
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
            bm.faces.new((rings[i][j], rings[(i + 1) % ring][j],
                          rings[(i + 1) % ring][(j + 1) % tubes], rings[i][(j + 1) % tubes]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def pane(points, thickness=0.012):
    """A flat polygon given as world points, extruded both ways (glass, decals)."""
    pts = [Vector(p) for p in points]
    n = (pts[1] - pts[0]).cross(pts[-1] - pts[0]).normalized() * (thickness * 0.5)
    bm = bmesh.new()
    front = [bm.verts.new(p + n) for p in pts]
    back = [bm.verts.new(p - n) for p in pts]
    bm.faces.new(front)
    bm.faces.new(list(reversed(back)))
    k = len(pts)
    for i in range(k):
        bm.faces.new((front[i], back[i], back[(i + 1) % k], front[(i + 1) % k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def section(hw, bot, top, cb, ct, hw_top=None):
    """Chamfered body section, (x, z) loop counter-clockwise from +X."""
    ht = hw if hw_top is None else hw_top
    return [(hw, bot + cb), (ht, top - ct), (ht - ct, top), (-ht + ct, top), (-ht, top - ct),
            (-hw, bot + cb), (-hw + cb, bot), (hw - cb, bot)]


def loft(sections):
    """Bridge equal-length (x, z) loops placed at their y into a closed skin."""
    bm = bmesh.new()
    rings = [[bm.verts.new((x, y, z)) for x, z in loop] for y, loop in sections]
    n = len(rings[0])
    for a, b in zip(rings, rings[1:]):
        for k in range(n):
            bm.faces.new((a[k], a[(k + 1) % n], b[(k + 1) % n], b[k]))
    bm.faces.new(rings[0])
    bm.faces.new(list(reversed(rings[-1])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def mirror_x(bm):
    bm.transform(Matrix.Diagonal((-1, 1, 1, 1)))
    bmesh.ops.reverse_faces(bm, faces=bm.faces)
    return bm


class Part:
    """A bmesh with one material slot per added primitive group."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.materials = []

    def add(self, src, mat=None):
        if mat is not None and mat not in self.materials:
            self.materials.append(mat)
        index = self.materials.index(mat) if mat is not None else 0
        mapping = {v: self.bm.verts.new(v.co) for v in src.verts}
        for f in src.faces:
            try:
                face = self.bm.faces.new([mapping[v] for v in f.verts])
            except ValueError:
                continue
            face.material_index = index
        src.free()
        return self

    def emit(self, origin=None, props=None):
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts, dist=1e-5)
        if origin is not None:
            self.bm.transform(Matrix.Translation(-Vector(origin)))
        mesh = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(mesh)
        self.bm.free()
        parts = self.name.split("__")
        if not self.materials and len(parts) >= 3:
            self.materials.append(material("Cabin_" + parts[2], parts[2], 0.0, 0.55))
        for mat in self.materials:
            mesh.materials.append(mat)
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        if origin is not None:
            obj.location = Vector(origin)
        for key, value in (props or {}).items():
            obj[key] = value
        return obj


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


def object_from_bm(name, bm, mats):
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    for m in mats:
        mesh.materials.append(m)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def apply_modifiers(obj):
    """Bakes the object's modifiers into its mesh (headless-safe)."""
    deps = bpy.context.evaluated_depsgraph_get()
    evaluated = obj.evaluated_get(deps)
    mesh = bpy.data.meshes.new_from_object(evaluated)
    obj.modifiers.clear()
    old = obj.data
    obj.data = mesh
    bpy.data.meshes.remove(old)


# --------------------------------------------------------------------- kit

class Kit:
    """Build context handed to a vehicle's details callback."""

    def __init__(self, spec):
        self.spec = spec
        self.id = spec["id"]
        self.parts = {}
        self.origins = {}
        p = spec.get("paint_finish", (0.35, 0.3))
        self.paint = material(f"{self.id}_Paint", spec["paint"], p[0], p[1])
        self.trim = material(f"{self.id}_Trim", spec.get("trim", "1B1C1F"), 0.0, 0.7)
        self.chrome = material("Remake_Chrome", "C9CED6", 0.95, 0.15)
        self.dark = material("Remake_Dark", "1A1B1F", 0.0, 0.75)
        self.head = material("Lamp_Headlight", "F4F1E6", 0.0, 0.2)
        self.tail = material("Lamp_Taillight", "C8141A", 0.0, 0.3)
        self.reverse = material("Lamp_Reverse", "E9EAEC", 0.0, 0.3)
        self.indicator = material("Indicator_Amber", "F08A1E", 0.0, 0.35)

    def mat(self, name, hex_color, metallic=0.0, roughness=0.6):
        return material(f"{self.id}_{name}", hex_color, metallic, roughness)

    def part(self, name, origin=None):
        if name not in self.parts:
            self.parts[name] = Part(name)
            if origin is not None:
                self.origins[name] = origin
        return self.parts[name]

    def mirrored(self, name, make, mat):
        """Adds make() and its X mirror to part `name` (lamps, mirrors, flares)."""
        part = self.part(name)
        part.add(make(), mat)
        part.add(mirror_x(make()), mat)

    def emit_parts(self):
        for name, part in self.parts.items():
            part.emit(origin=self.origins.get(name))


# ------------------------------------------------------------------- body

def build_body(k):
    s = k.spec
    rows = s["body"]
    bm = loft([(y, section(*row)) for y, *row in rows])
    for f in bm.faces:
        f.smooth = False
    mats = [k.paint]
    tone = s.get("two_tone")
    if tone:
        mats.append(k.mat("Tone", tone["hex"], *s.get("paint_finish", (0.35, 0.3))))
    body = object_from_bm("Body__Shell", bm, mats)
    # Wheel arches: cut a cylinder slightly larger than each tyre.
    w = s["wheels"]
    for y in (w["front_y"], w["rear_y"]):
        cut = object_from_bm("arch_cut", cylinder((0, y, w["radius"]), (1, 0, 0), w["radius"] + w.get("arch_gap", 0.07),
                                                  4.0, 12), [])
        mod = body.modifiers.new("arch", "BOOLEAN")
        mod.operation = "DIFFERENCE"
        mod.solver = "EXACT"
        mod.object = cut
        apply_modifiers(body)
        bpy.data.objects.remove(cut)
    cab = s.get("cabin")
    door = s.get("door")
    bm = bmesh.new()
    bm.from_mesh(body.data)
    if cab:
        # Open the tub over the cabin: the driver shows through the glass.
        y0, y1 = cab["y0"], cab["y1"]
        top = [f for f in bm.faces if y0 < f.calc_center_median().y < y1 and f.normal.z > 0.5
               and f.calc_center_median().z > s["belt"] - 0.25]
        bmesh.ops.delete(bm, geom=top, context="FACES")
    if tone:
        # Second paint on the side faces inside a y/z box (police doors), set
        # after the arch cut so the boolean cannot reset it.
        for f in bm.faces:
            c = f.calc_center_median()
            f.material_index = int(tone["y0"] < c.y < tone["y1"] and tone["z0"] < c.z < tone["z1"]
                                   and f.normal.z < 0.5)
    door_faces = []
    bm.faces.index_update()
    if door:
        sill = door["sill"]
        door_faces = [f for f in bm.faces if f.normal.x > 0.5 and door["y0"] < f.calc_center_median().y < door["y1"]
                      and sill < f.calc_center_median().z < s["belt"] + 0.01]
    door_bm = None
    if door_faces:
        door_bm = bm.copy()
        keep = {f.index for f in door_faces}
        door_bm.faces.ensure_lookup_table()
        bmesh.ops.delete(door_bm, geom=[f for f in door_bm.faces if f.index not in keep], context="FACES")
        bmesh.ops.delete(bm, geom=door_faces, context="FACES")
    bm.to_mesh(body.data)
    bm.free()
    solid = body.modifiers.new("solid", "SOLIDIFY")
    solid.thickness = 0.045
    solid.offset = -1.0
    solid.use_even_offset = True
    apply_modifiers(body)
    if door_bm is not None:
        hinge = door_hinge(k)
        door_bm.transform(Matrix.Translation(-hinge))
        obj = object_from_bm("Door_Driver__Shell", door_bm, mats)
        obj.location = hinge
        solid = obj.modifiers.new("solid", "SOLIDIFY")
        solid.thickness = 0.045
        solid.offset = -1.0
        apply_modifiers(obj)


def body_half_width(k, y, z):
    """Body half width at station y (linear between loft rows), at height z."""
    rows = k.spec["body"]
    for a, b in zip(rows, rows[1:]):
        if a[0] <= y <= b[0]:
            t = (y - a[0]) / max(1e-6, b[0] - a[0])
            return a[1] + (b[1] - a[1]) * t
    return rows[0][1]


def door_hinge(k):
    d = k.spec["door"]
    x = body_half_width(k, d["y0"], k.spec["belt"]) - 0.0225
    return Vector((x, d["y0"] + 0.02, (d["sill"] + k.spec["belt"]) * 0.5))


# -------------------------------------------------------------- greenhouse

def build_greenhouse(k):
    g = k.spec["greenhouse"]
    belt = k.spec["belt"]
    roof, rt = g["roof"], g.get("roof_t", 0.07)
    hb, hr = g["hw_belt"], g["hw_roof"]
    ay, cy, rf, rr = g["a_y"], g["c_y"], g["roof_front"], g["roof_rear"]
    pw = g.get("pillar", 0.08)
    roof_mat = k.mat("Roof", g["roof_hex"], 0.0, 0.5) if g.get("roof_hex") else k.paint
    frame = k.part("Greenhouse")
    # Roof slab.
    frame.add(span_box((-hr - 0.02, rf - 0.02, roof - rt), (hr + 0.02, rr + 0.02, roof), 0.025), roof_mat)

    def edge(y_belt, y_roof, side):
        return Vector((side * hb, y_belt, belt)), Vector((side * hr, y_roof, roof - rt * 0.5))

    def roof_y_at(y):
        # Vertical posts (B/D pillars) meet the roof straight up, clamped to it.
        return min(max(y, rf + pw), rr - pw)

    posts = [ay] + list(g.get("posts", [])) + [cy]
    for side in (1, -1):
        for i, y in enumerate(posts):
            top_y = rf if i == 0 else rr if i == len(posts) - 1 else roof_y_at(y)
            p0, p1 = edge(y, top_y, side)
            frame.add(beam(p0, p1, pw), k.trim if g.get("dark_pillars") else k.paint)
    # Glass: windscreen, rear window, side windows between the posts.
    glass = k.part("Cabin__Glass")
    glass_mat = material("Remake_Glass", "33414D", 0.0, 0.05, alpha=0.45)
    inset = 0.02
    glass.add(pane([(-hb + inset, ay, belt), (hb - inset, ay, belt),
                    (hr - inset, rf, roof - rt), (-hr + inset, rf, roof - rt)]), glass_mat)
    glass.add(pane([(hb - inset, cy, belt), (-hb + inset, cy, belt),
                    (-hr + inset, rr, roof - rt), (hr - inset, rr, roof - rt)]), glass_mat)
    for side in (1, -1):
        for i in range(len(posts) - 1):
            y0, y1 = posts[i], posts[i + 1]
            t0 = rf if i == 0 else roof_y_at(y0)
            t1 = rr if i + 1 == len(posts) - 1 else roof_y_at(y1)
            pts = [(side * hb, y0, belt), (side * hb, y1, belt), (side * hr, t1, roof - rt), (side * hr, t0, roof - rt)]
            if side < 0:
                pts.reverse()
            target = "Door_Driver__Glass" if side > 0 and i == 0 and k.spec.get("door") else "Cabin__Glass"
            if target == "Door_Driver__Glass":
                k.part(target, door_hinge(k)).add(pane(pts), glass_mat)
            else:
                glass.add(pane(pts), glass_mat)


# ---------------------------------------------------------------- interior

def build_interior(k):
    s = k.spec
    pal = s["interior"]
    st = s["seat"]
    g = s["greenhouse"]
    cab = s["cabin"]
    floor_z = st["floor"]
    roof_in = g["roof"] - g.get("roof_t", 0.07)
    # As high as the roof allows a seated 2.5 m bodybuilder (head top about
    # 1.15 m above the cushion); the game lowers the seat per driver.
    # Low roofs get a low cushion (sports-car seat) so the rider is not sunk through the floor.
    cush = max(floor_z + 0.08, min(floor_z + 0.42, roof_in - 1.22))
    k.cushion = cush
    sx, sy = st["x"], st["y"]
    seat_hex, acc_hex = pal["seat"], pal["accent"]

    def seat(group, x, origin=None):
        part = k.part(f"{group}__Seat__{seat_hex}", origin)
        part.add(span_box((x - 0.27, sy - 0.26, cush - 0.14), (x + 0.27, sy + 0.26, cush), 0.03))
        part.add(span_box((x - 0.2, sy - 0.18, floor_z), (x + 0.2, sy + 0.18, cush - 0.12)))
        pivot = Vector((x, sy + 0.26, cush))
        tilt = Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(-13), 4, "X") @ Matrix.Translation(-pivot)
        back = span_box((x - 0.27, sy + 0.20, cush - 0.04), (x + 0.27, sy + 0.32, cush + 0.68), 0.03)
        back.transform(tilt)
        part.add(back)
        head = span_box((x - 0.14, sy + 0.22, cush + 0.70), (x + 0.14, sy + 0.30, cush + 0.88), 0.02)
        head.transform(tilt)
        part.add(head)
        acc = span_box((x - 0.09, sy + 0.185, cush + 0.06), (x + 0.09, sy + 0.2, cush + 0.6))
        acc.transform(tilt)
        k.part(f"{group}__Accent__{acc_hex}", origin).add(acc)

    seat("Driver_Seat", sx, Vector((sx, sy, cush)))
    seat("Cabin", -sx)
    if st.get("rear"):
        bench = k.part(f"Cabin__Bench__{seat_hex}")
        ry = st["rear"]
        bench.add(span_box((-sx - 0.3, ry - 0.25, floor_z), (sx + 0.3, ry + 0.25, cush - 0.02), 0.03))
        bench.add(span_box((-sx - 0.3, ry + 0.2, cush - 0.05), (sx + 0.3, ry + 0.32, cush + 0.6), 0.03))
    hw = body_half_width(k, sy, s["belt"]) - 0.05
    k.part(f"Cabin__Floor__{pal['floor']}").add(span_box((-hw, cab["y0"], floor_z - 0.03), (hw, cab["y1"], floor_z)))
    # Dash under the windscreen base, across the car.
    dy = g["a_y"]
    k.part(f"Cabin__Dash__{pal['dash']}").add(span_box((-hw, dy - 0.05, s["belt"] - 0.32), (hw, dy + 0.16, s["belt"] + 0.04), 0.03))
    k.part(f"Cabin__Screen__{pal.get('screen', '0A0D11')}").add(
        span_box((-0.25, dy + 0.15, s["belt"] - 0.18), (0.05, dy + 0.175, s["belt"] - 0.02)))
    # Steering wheel facing the driver, tilted 24 degrees up (GymVehicleCabin).
    rim_r = 0.19 * s.get("length", 5.0) / s.get("game_length", CAR_GAME_LENGTH)
    centre = Vector((sx, sy - 0.48, cush + 0.40))
    normal = Vector((0.0, math.cos(math.radians(24)), math.sin(math.radians(24))))
    rim = k.part(f"Steering_Wheel__Rim__{pal.get('wheel', '141517')}", centre)
    rim.add(torus(centre, normal, rim_r, 0.016))
    rim.add(cylinder(centre - normal * 0.01, normal, 0.035, 0.03, 8))
    for a in (90, 210, 330):
        u = normal.orthogonal().normalized()
        v = normal.cross(u)
        d = u * math.cos(math.radians(a)) + v * math.sin(math.radians(a))
        rim.add(beam(centre + d * 0.03, centre + d * (rim_r - 0.01), 0.018))
    rim.add(cylinder(centre - normal * 0.17, normal, 0.02, 0.3, 6))
    under = s.get("underbody")
    if under:
        # Footwell skid pan: a 2.5 m driver's feet reach below a low car's floor;
        # this reads as the dark chassis between the wheels (OG cars do the same).
        k.part(f"Cabin__Underbody__{pal.get('trim', '0E0E0F')}").add(
            span_box((-under["hw"], sy - under["front"], under["bottom"]), (under["hw"], sy + under["rear"], floor_z + under.get("top", 0.025)), 0.01))
    marker("Marker__Seat", (sx, sy, cush))
    marker("Marker__Roof", (sx, sy + 0.01, roof_in))
    marker("Marker__Wheel", tuple(centre))
    marker("Marker__Floor", (sx, sy, floor_z))
    marker("Marker__Hinge", tuple(door_hinge(k)))


# ------------------------------------------------------------------ wheels

def build_wheels(k):
    w = k.spec["wheels"]
    r, width, x = w["radius"], w["width"], w["x"]
    rim_mat = material(f"{k.id}_Rim", w.get("rim", "B9BCC2"), 0.3, 0.35)
    tyre = material("Remake_Tyre", "202124", 0.0, 0.85)
    hub = material(f"{k.id}_Hub", w.get("hub", "8E9196"), 0.5, 0.35)
    style = w.get("style", "spoke5")
    for letter, y in (("F", w["front_y"]), ("R", w["rear_y"])):
        for side, lr in ((1, "L"), (-1, "R")):
            centre = Vector((side * x, y, r))
            part = Part(f"Wheel_{letter}{lr}")
            axis = Vector((1, 0, 0))
            part.add(cylinder(centre, axis, r - (0.035 if w.get("lugs") else 0.0), width, w.get("segments", 14)), tyre)
            if w.get("lugs"):
                # Chunky off-road tread: staggered blocks round the tyre.
                n = w.get("segments", 14)
                for s in range(n):
                    t = 2 * math.pi * (s + 0.5) / n
                    for half in (-1, 1):
                        lug = box((0, 0, 0), (width * 0.46, 2 * math.pi * r / n * 0.7, 0.07))
                        lug.transform(Matrix.Translation(centre) @ Matrix.Rotation(t, 4, "X") @
                                      Matrix.Translation((half * width * 0.25, (s % 2) * 0.03, r - 0.035)))
                        part.add(lug, tyre)
            rim_r = r * w.get("rim_frac", 0.6)
            face = centre + Vector((side * (width * 0.5 + 0.004), 0, 0))
            part.add(cylinder(face, axis, rim_r, 0.02, 12), rim_mat)
            if style == "spoke5":
                for s in range(5):
                    t = 2 * math.pi * s / 5
                    d = Vector((0, math.cos(t), math.sin(t)))
                    part.add(beam(face + d * rim_r * 0.15 + axis * side * 0.012,
                                  face + d * rim_r * 0.92 + axis * side * 0.012, 0.07, 0.03), rim_mat)
            elif style == "plain":
                part.add(cylinder(face + axis * side * 0.01, axis, rim_r * 0.9, 0.02, 10), hub)
            elif style == "holes8":
                # Off-road disc: raised ring with a hub, like the reference.
                part.add(cylinder(face + axis * side * 0.012, axis, rim_r * 0.82, 0.02, 12), hub)
            part.add(cylinder(face + axis * side * 0.028, axis, rim_r * 0.28, 0.03, 8), hub)
            palette_wheel(part.emit(origin=centre, props={"wheel_radius": r, "wheel_axis": "X"}), k.id)


def palette_wheel(obj, vehicle_id):
    """One material per wheel: the GLB loader splits a multi-material node
    into one renderer per material, and the game would spin each as a wheel.
    The part colours move into a small palette texture addressed by UV."""
    mesh = obj.data
    colours = [tuple(m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value[:3]) for m in mesh.materials]
    name = f"{vehicle_id}_WheelPalette"
    mat = bpy.data.materials.get(name)
    if mat is None:
        cell = 16
        image = bpy.data.images.new(name, width=cell * len(colours), height=cell, alpha=False)
        pixels = []
        for _y in range(cell):
            for x in range(cell * len(colours)):
                pixels += [*colours[x // cell], 1.0]
        image.pixels = pixels
        image.pack()
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes["Principled BSDF"]
        tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
        tex.image = image
        tex.interpolation = "Closest"
        mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        bsdf.inputs["Metallic"].default_value = 0.2
        bsdf.inputs["Roughness"].default_value = 0.6
    uv = mesh.uv_layers.new(name="UVMap")
    count = len(colours)
    for poly in mesh.polygons:
        u = (poly.material_index + 0.5) / count
        for li in poly.loop_indices:
            uv.data[li].uv = (u, 0.5)
        poly.material_index = 0
    mesh.materials.clear()
    mesh.materials.append(mat)


# ----------------------------------------------------- material collapse

KEEP_MATERIALS = ("Lamp_", "Marker", "Remake_Glass")
# Keep GymVehicleLights' lenses (matched by name) and glass apart; only these
# names stay separate. Everything else opaque and flat becomes one palette per
# finish: SetPass calls scale with materials, not with colours.
FINISHES = (("Matte", 0.0, 0.65), ("Paint", 0.2, 0.4), ("Chrome", 0.9, 0.15))


def _flat_colour(mat):
    nodes = mat.node_tree.nodes
    bsdf = nodes.get("Principled BSDF")
    if bsdf is None or any(n.type == "TEX_IMAGE" for n in nodes) or bsdf.inputs["Alpha"].default_value < 1.0:
        return None
    return tuple(round(c, 4) for c in bsdf.inputs["Base Color"].default_value[:3]), bsdf.inputs["Metallic"].default_value


def collapse_materials(vehicle_id):
    """Bakes flat opaque colours into one palette texture per finish (3 at most),
    remaps UVs, joins plain detail objects, and drops backface duplication."""
    classes = {}
    for mat in bpy.data.materials:
        if mat.name.startswith(KEEP_MATERIALS) or mat.name.endswith("WheelPalette") or mat.users == 0:
            continue
        flat = _flat_colour(mat)
        if flat is None:
            continue
        colour, metallic = flat
        finish = 0 if metallic < 0.05 else 1 if metallic < 0.5 else 2
        classes[mat.name] = (finish, colour)
    palettes = []
    cells = []
    for finish, (label, metallic, roughness) in enumerate(FINISHES):
        colours = sorted({c for f, c in classes.values() if f == finish})
        cells.append({c: i for i, c in enumerate(colours)})
        if not colours:
            palettes.append(None)
            continue
        cell = 8
        image = bpy.data.images.new(f"{vehicle_id}_{label}", width=cell * len(colours), height=cell, alpha=False)
        pixels = []
        for _y in range(cell):
            for x in range(cell * len(colours)):
                pixels += [*colours[x // cell], 1.0]
        image.pixels = pixels
        image.pack()
        mat = bpy.data.materials.new(f"{vehicle_id}_Palette_{label}")
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes["Principled BSDF"]
        tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
        tex.image = image
        tex.interpolation = "Closest"
        mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Roughness"].default_value = roughness
        palettes.append((mat, len(colours)))
    for obj in [o for o in bpy.data.objects if o.type == "MESH" and not o.name.startswith("Marker__")]:
        mesh = obj.data
        if not any(m and m.name in classes for m in mesh.materials):
            continue
        uv = mesh.uv_layers.get("UVMap") or mesh.uv_layers.new(name="UVMap")
        slots = [m for m in mesh.materials]
        new_slots = []

        def slot_of(mat):
            if mat not in new_slots:
                new_slots.append(mat)
            return new_slots.index(mat)

        indices = []
        for poly in mesh.polygons:
            old = slots[poly.material_index] if poly.material_index < len(slots) else None
            if old is not None and old.name in classes:
                finish, colour = classes[old.name]
                pal, count = palettes[finish]
                u = (cells[finish][colour] + 0.5) / count
                for li in poly.loop_indices:
                    uv.data[li].uv = (u, 0.5)
                indices.append(slot_of(pal))
            else:
                indices.append(slot_of(old))
        # materials.clear() resets every polygon's index: assign afterwards.
        mesh.materials.clear()
        for m in new_slots:
            mesh.materials.append(m)
        for poly, index in zip(mesh.polygons, indices):
            poly.material_index = index
    # Join the plain detail objects (no role in the name): one object, one
    # primitive per material. Roles, wheels, doors, seats and the shell stay apart.
    palette_names = {m.name for m in bpy.data.materials if "_Palette_" in m.name}
    plain = [o for o in bpy.data.objects if o.type == "MESH" and "__" not in o.name and not o.name.startswith("Wheel_")
             and o.data.materials and all(m and m.name in palette_names for m in o.data.materials)]
    if len(plain) > 1:
        with bpy.context.temp_override(active_object=plain[0], selected_editable_objects=plain, selected_objects=plain):
            bpy.ops.object.join()
        plain[0].name = "Details"
    for mat in bpy.data.materials:
        mat.use_backface_culling = True


# ------------------------------------------------------------------ output

def triangle_count(objects=None):
    deps = bpy.context.evaluated_depsgraph_get()
    total = 0
    for o in objects or bpy.data.objects:
        if o.type != "MESH" or o.name.startswith(("Marker__", "ground")):
            continue
        mesh = o.evaluated_get(deps).data
        total += sum(len(p.vertices) - 2 for p in mesh.polygons)
    return total


def export(vehicle_id):
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    path = OUT_DIR / f"{vehicle_id}.glb"
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(filepath=str(path), export_format="GLB", use_selection=True,
                              export_extras=True, export_yup=True, export_apply=True)
    return path


def _engine(scene):
    for name in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
        try:
            scene.render.engine = name
            return
        except TypeError:
            continue


def render_sheet(vehicle_id, reference, size, door=True, night=True, ref_view=(1.0, -1.0, 0.55)):
    """Renders views and lays them out next to the reference image."""
    EVIDENCE.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    _engine(scene)
    w, h = 720, 540
    scene.render.resolution_x, scene.render.resolution_y = w, h
    scene.view_settings.view_transform = "Standard"
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    scene.world = world
    for o in bpy.data.objects:
        if o.name.startswith("Marker__"):
            o.hide_render = True
    ground = object_from_bm("ground", span_box((-40, -40, -0.02), (40, 40, 0.0)), [material("Ground", "8C9096", 0.0, 0.9)])
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3.2
    sun.rotation_euler = (math.radians(42), math.radians(10), math.radians(-30))
    scene.collection.objects.link(sun)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    cam.data.lens = 45
    scene.collection.objects.link(cam)
    scene.camera = cam
    L = max(size)
    target = Vector((0, 0, size[2] * 0.45))
    rv = Vector(ref_view).normalized()
    views = [("ref_angle", target + rv * L * 1.55, False, False),
             ("rear34", target + Vector((-1.0, 1.1, 0.55)).normalized() * L * 1.55, False, False),
             ("side", target + Vector((1, 0, 0.08)).normalized() * L * 1.5, False, False),
             ("top", target + Vector((0.001, 0.0, 1.0)) * L * 1.7, False, False)]
    if door:
        views.append(("door_open", target + Vector((1.0, 0.25, 0.6)).normalized() * L * 1.1, True, False))
    if night:
        views.append(("night", target + rv * L * 1.55, False, True))
    doors = [o for o in bpy.data.objects if o.name.startswith("Door_Driver")]
    lamp_mats = [m for m in bpy.data.materials if m.name.startswith("Lamp_")]
    tiles = []
    for name, pos, open_door, dark in views:
        for o in doors:
            o.rotation_euler = (0, 0, math.radians(78) if open_door else 0)
        bg.inputs[0].default_value = (0.03, 0.04, 0.07, 1) if dark else (0.42, 0.62, 0.95, 1)
        bg.inputs[1].default_value = 0.25 if dark else 0.45
        sun.data.energy = 0.05 if dark else 4.2
        for m in lamp_mats:
            bsdf = m.node_tree.nodes.get("Principled BSDF")
            bsdf.inputs["Emission Color"].default_value = bsdf.inputs["Base Color"].default_value
            bsdf.inputs["Emission Strength"].default_value = 6.0 if dark else 0.0
        cam.location = pos
        cam.rotation_euler = (target - pos).to_track_quat("-Z", "Y").to_euler()
        path = EVIDENCE / f"{vehicle_id}_{name}.png"
        scene.render.filepath = str(path)
        bpy.ops.render.render(write_still=True)
        tiles.append(path)
    for o in doors:
        o.rotation_euler = (0, 0, 0)
    bpy.data.objects.remove(ground)
    sheet = EVIDENCE / f"{vehicle_id}_sheet.png"
    compose([REFS / reference] + tiles, sheet, w, h)
    return sheet


def compose(paths, out, w, h, cols=4):
    """Tiles the images into one sheet. Uses the system Python with Pillow:
    Blender's image API re-encodes the 8-bit renders and washes them out."""
    script = "\n".join([
        "import sys",
        "from PIL import Image",
        "w, h, cols, out, *paths = sys.argv[1:]",
        "w, h, cols = int(w), int(h), int(cols)",
        "rows = (len(paths) + cols - 1) // cols",
        "sheet = Image.new('RGB', (cols * w, rows * h), 'white')",
        "for i, p in enumerate(paths):",
        "    im = Image.open(p).convert('RGB')",
        "    im.thumbnail((w, h))",
        "    sheet.paste(im, ((i % cols) * w + (w - im.width) // 2, (i // cols) * h + (h - im.height) // 2))",
        "sheet.save(out)"])
    subprocess.run(["python", "-c", script, str(w), str(h), str(cols), str(out)] + [str(p) for p in paths],
                   check=True)


def bounds_size():
    lo = Vector((1e9, 1e9, 1e9))
    hi = -lo
    for o in bpy.data.objects:
        if o.type != "MESH" or o.name.startswith("Marker__"):
            continue
        for c in o.bound_box:
            p = o.matrix_world @ Vector(c)
            lo = Vector(map(min, lo, p))
            hi = Vector(map(max, hi, p))
    return hi - lo


def run(spec, details=None, builder=None):
    """Builds spec (or calls builder(k) for non-car shapes), exports and renders."""
    args = parse_args()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    k = Kit(spec)
    if builder is not None:
        builder(k)
    else:
        build_body(k)
        build_greenhouse(k)
        if spec.get("seat"):
            build_interior(k)
        build_wheels(k)
    if details is not None:
        details(k)
    k.emit_parts()
    collapse_materials(spec["id"])
    if spec.get("normalize"):
        # Unit-length model: the game scales it to its target length (OG bus GLB contract).
        f = 1.0 / max(bounds_size())
        for o in bpy.data.objects:
            if o.type == "MESH":
                o.data.transform(Matrix.Scale(f, 4))
                o.location *= f
    tris = triangle_count()
    path = export(spec["id"])
    size = bounds_size()
    print(f"REMAKE_VEHICLE_BUILT id={spec['id']} tris={tris} size={size.x:.2f}x{size.y:.2f}x{size.z:.2f} path={path}")
    if not args.no_render:
        sheet = render_sheet(spec["id"], spec["reference"], size, door=bool(spec.get("door")),
                             night=spec.get("lamps", True), ref_view=spec.get("ref_view", (1.0, -1.0, 0.55)))
        print(f"REMAKE_VEHICLE_SHEET id={spec['id']} path={sheet}")
