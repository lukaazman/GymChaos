"""Give each driven visitor car a see-through cabin and an opening driver door.

The cars are single-shell scans with the windows painted into the texture.
For every car this script, working on the runtime asset (wheels already split
by Tools/split_vehicle_wheels.py + Tools/complete_vehicle_wheels.py):

  1. cuts the windscreen, both front side windows and, where the car has
     them, the rear side windows and the rear window out of the body along
     authored outlines and turns them into transparent ``Glass`` parts;
  2. cuts the driver door out along its seam into its own parts, with its
     origin on the front hinge, an inner door card and edge strips so the open
     door is a solid slab;
  3. closes the cabin with an inner liner (a decimated, inset copy of the
     shell turned inside out) and edge strips round every opening, so nothing
     behind the glass is see-through;
  4. builds a small interior in the car's own colours: seats, dashboard,
     console, floor and a steering wheel;
  5. adds tiny marker meshes (seat, roof, hinge, wheel, floor) that the game
     reads to seat the driver and swing the door.

Part names follow ``Group__Role__RRGGBB``; the game gives each part its
material from that name (``Glass`` roles become glass, ``Shell`` keeps the
scan texture). Everything is top level: the runtime GLB loader ignores node
hierarchy and empty nodes.

The original assets are backed up once under Logs/agent/cabins/backup and the
script always rebuilds from that backup, so it can run repeatedly.

Usage:
  blender -b --factory-startup -P Tools/build_vehicle_cabins.py -- [cbum ...] [--no-render]
"""
import argparse
import math
import shutil
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "GymChaos/Assets"
BACKUP = ROOT / "Logs/agent/cabins/backup"
EVIDENCE = ROOT / "Logs/agent/cabins"

# Every car is fitted to 5 m in the game: one source unit is 5 m.
M = 0.2

# Local car frame: a = along the car (nose at -a), c = across (driver side
# +c), z = up. Polygons are in source units, measured on orthographic renders.
VEHICLES = {
    "cbum": {
        "path": ASSETS / "Resources/Vehicles/cbum_vehicle.fbx",
        "axis": "y",
        # 1969 Camaro coupe: frameless door glass, long door.
        "windshield": {"view": "top", "z_min": 0.03, "poly": [
            (-0.125, -0.165), (-0.072, -0.128), (-0.072, 0.128), (-0.125, 0.165)]},
        "side_window": [(-0.117, 0.051), (-0.048, 0.118), (0.102, 0.118), (0.102, 0.051)],
        "door": [(-0.17, -0.092), (-0.17, 0.045), (-0.125, 0.047), (-0.05, 0.122),
                 (0.104, 0.122), (0.104, 0.05), (0.125, 0.045), (0.125, -0.092)],
        "seat_a": 0.005,
        "cabin": (-0.16, 0.30),
        "rear_bench": 0.15,
        # Rear quarter window and the fastback rear window.
        "rear_side_windows": [[(0.108, 0.056), (0.108, 0.118), (0.124, 0.118), (0.178, 0.071), (0.170, 0.060)]],
        "rear_window": {"view": "top", "z_min": 0.085, "poly": [
            (0.24, -0.125), (0.315, -0.14), (0.315, 0.14), (0.24, 0.125)]},
        "palette": {"liner": "1C1C1F", "floor": "121214", "seat": "1E1E21",
                    "accent": "A3141C", "dash": "141416", "trim": "0E0E0F",
                    "console": "17171A", "screen": "0A0D11", "wheel": "121213"},
    },
    "zyzz": {
        "path": ASSETS / "Resources/Vehicles/zyzz_vehicle.fbx",
        "axis": "y",
        # Full-size SUV: framed door, tall greenhouse.
        "windshield": {"view": "top", "z_min": 0.04, "poly": [
            (-0.222, -0.152), (-0.135, -0.13), (-0.135, 0.13), (-0.222, 0.152)]},
        "side_window": [(-0.204, 0.053), (-0.118, 0.143), (-0.027, 0.143), (-0.027, 0.053)],
        "door": [(-0.225, -0.055), (-0.225, 0.05), (-0.12, 0.15), (-0.016, 0.15), (-0.016, -0.055)],
        "seat_a": -0.06,
        "cabin": (-0.25, 0.46),
        "rear_bench": 0.17,
        # Rear door and cargo windows, tailgate glass.
        "rear_side_windows": [
            [(0.010, 0.061), (0.010, 0.140), (0.168, 0.141), (0.176, 0.062)],
            [(0.240, 0.061), (0.212, 0.141), (0.444, 0.141), (0.448, 0.061)]],
        "rear_window": {"view": "rear", "a_min": 0.40, "boxes": [
            [(-0.156, 0.063), (-0.129, 0.144), (0.129, 0.144), (0.156, 0.063)]]},
        "palette": {"liner": "6E7177", "floor": "26282B", "seat": "34373C",
                    "accent": "5A5F66", "dash": "1B1D20", "trim": "111214",
                    "console": "222428", "screen": "0A0D11", "wheel": "141517"},
    },
    "jaycutler": {
        "path": ASSETS / "Resources/Vehicles/jaycutler_vehicle.fbx",
        "axis": "y",
        # Mercedes saloon: cream leather, dark wood.
        "windshield": {"view": "top", "z_min": 0.02, "poly": [
            (-0.198, -0.142), (-0.08, -0.125), (-0.08, 0.125), (-0.198, 0.142)]},
        "side_window": [(-0.14, 0.054), (-0.072, 0.117), (0.04, 0.123), (0.04, 0.054)],
        "door": [(-0.175, -0.085), (-0.175, 0.045), (-0.145, 0.05), (-0.07, 0.126),
                 (0.045, 0.128), (0.045, -0.085)],
        "seat_a": -0.005,
        "cabin": (-0.21, 0.32),
        "rear_bench": 0.17,
        # Rear door window with its quarter light, sloped rear window.
        "rear_side_windows": [[(0.048, 0.057), (0.048, 0.123), (0.12, 0.120), (0.16, 0.109),
                               (0.20, 0.090), (0.240, 0.061), (0.236, 0.058)]],
        "rear_window": {"view": "top", "z_min": 0.072, "poly": [
            (0.215, -0.12), (0.343, -0.13), (0.343, 0.13), (0.215, 0.12)]},
        "palette": {"liner": "CBBFA9", "floor": "3A2F26", "seat": "D6C3A1",
                    "accent": "B59F7E", "dash": "2A1C14", "trim": "16110D",
                    "console": "3B2A1F", "screen": "0A0D11", "wheel": "1A1410"},
    },
    "ronnie": {
        "path": ASSETS / "Resources/Vehicles/ronnie_vehicle.fbx",
        "axis": "y",
        # Boxy cruiser: grey cloth, black dash.
        "windshield": {"view": "top", "z_min": 0.03, "poly": [
            (-0.17, -0.165), (-0.077, -0.15), (-0.077, 0.15), (-0.17, 0.165)]},
        "side_window": [(-0.094, 0.047), (-0.048, 0.118), (0.051, 0.118), (0.051, 0.047)],
        "door": [(-0.149, -0.13), (-0.149, 0.04), (-0.075, 0.128), (0.059, 0.128), (0.059, -0.13)],
        "seat_a": -0.002,
        "cabin": (-0.18, 0.30),
        "rear_bench": 0.17,
        "rear_side_windows": [[(0.084, 0.044), (0.084, 0.119), (0.185, 0.119), (0.195, 0.111), (0.230, 0.046)]],
        "rear_window": {"view": "top", "z_min": 0.058, "poly": [
            (0.247, -0.145), (0.298, -0.152), (0.298, 0.152), (0.247, 0.145)]},
        "palette": {"liner": "55585D", "floor": "1F2023", "seat": "2C2E31",
                    "accent": "45484D", "dash": "161719", "trim": "0F1011",
                    "console": "1D1E21", "screen": "0A0D11", "wheel": "141416"},
    },
    "police": {
        "path": ASSETS / "StreamingAssets/BodyBuilders/vehicles/Policecar_lod.glb",
        "axis": "y",
        "windshield": {"view": "top", "z_min": 0.03, "poly": [
            (-0.17, -0.165), (-0.077, -0.15), (-0.077, 0.15), (-0.17, 0.165)]},
        "side_window": [(-0.094, 0.047), (-0.048, 0.118), (0.051, 0.118), (0.051, 0.047)],
        "door": [(-0.149, -0.13), (-0.149, 0.04), (-0.075, 0.128), (0.059, 0.128), (0.059, -0.13)],
        "seat_a": -0.002,
        "cabin": (-0.18, 0.30),
        "rear_bench": 0.17,
        "rear_side_windows": [[(0.084, 0.044), (0.084, 0.119), (0.185, 0.119), (0.195, 0.111), (0.230, 0.046)]],
        "rear_window": {"view": "top", "z_min": 0.058, "poly": [
            (0.247, -0.145), (0.298, -0.152), (0.298, 0.152), (0.247, 0.145)]},
        "palette": {"liner": "4A4D52", "floor": "1C1D20", "seat": "26282B",
                    "accent": "1F3A6E", "dash": "141517", "trim": "0E0F10",
                    "console": "1A1B1E", "screen": "0A0D11", "wheel": "121314"},
    },
    "arnold": {
        "path": ASSETS / "StreamingAssets/BodyBuilders/vehicles/Arnold_Hummer_lod.glb",
        "axis": "x",
        # Hummer H1: split upright windscreen, square framed doors.
        "windshield": {"view": "front", "a_range": (-0.24, -0.16), "boxes": [
            [(0.018, 0.103), (0.19, 0.103), (0.19, 0.165), (0.018, 0.165)],
            [(-0.19, 0.103), (-0.016, 0.103), (-0.016, 0.165), (-0.19, 0.165)]]},
        "side_window": [(-0.166, 0.085), (-0.166, 0.162), (-0.054, 0.162), (-0.054, 0.085)],
        "door": [(-0.195, -0.07), (-0.195, 0.185), (-0.045, 0.185), (-0.045, -0.07)],
        "seat_a": -0.085,
        "cabin": (-0.215, 0.17),
        "rear_bench": 0.16,
        # Rear door windows only: the cab's back wall hides behind the spare tyre.
        "rear_side_windows": [[(0.004, 0.087), (0.004, 0.165), (0.093, 0.165), (0.112, 0.087)]],
        "palette": {"liner": "4A4C3C", "floor": "24251D", "seat": "7A6A4F",
                    "accent": "5C5240", "dash": "2C3024", "trim": "141510",
                    "console": "33362A", "screen": "0A0D11", "wheel": "161712"},
    },
}

GLASS = "1A242C"
BODY_EDGE = 0.008   # window/door aperture strip depth (4 cm)
DOOR_THICKNESS = 0.009
LINER_INSET = 0.006
LINER_FACES = 9000

BODY, DOOR, DOOR_GLASS, BODY_GLASS, BODY_JAMB, DOOR_JAMB, DOOR_CARD = range(7)


def parse_args():
    parser = argparse.ArgumentParser()
    parser.add_argument("names", nargs="*")
    parser.add_argument("--no-render", action="store_true")
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return parser.parse_args(argv)


class Frame:
    """Maps the car frame (a, c, z) to Blender world space and back."""

    def __init__(self, axis):
        if axis == "y":
            self.A, self.C = Vector((0, 1, 0)), Vector((1, 0, 0))
        else:
            self.A, self.C = Vector((1, 0, 0)), Vector((0, -1, 0))
        self.Z = Vector((0, 0, 1))

    def local(self, v):
        return Vector((v.dot(self.A), v.dot(self.C), v.dot(self.Z)))

    def world(self, a, c, z):
        return self.A * a + self.C * c + self.Z * z

    def direction(self, a, c, z):
        return self.A * a + self.C * c + self.Z * z


def point_in_poly(x, y, poly):
    inside = False
    j = len(poly) - 1
    for i in range(len(poly)):
        xi, yi = poly[i]
        xj, yj = poly[j]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi + 1e-12) + xi:
            inside = not inside
        j = i
    return inside


def poly_bbox(poly, margin):
    xs = [p[0] for p in poly]
    ys = [p[1] for p in poly]
    return min(xs) - margin, max(xs) + margin, min(ys) - margin, max(ys) + margin


def import_asset(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if path.suffix.lower() == ".fbx":
        bpy.ops.import_scene.fbx(filepath=str(path))
    else:
        bpy.ops.import_scene.gltf(filepath=str(path))
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    bodies = [o for o in meshes if not o.name.startswith("Wheel_")]
    if len(bodies) != 1:
        raise RuntimeError(f"expected one body mesh, found {[o.name for o in bodies]}")
    body = bodies[0]
    if body.parent is not None and any(
            abs(body.parent.matrix_world[i][j] - (1.0 if i == j else 0.0)) > 1e-5
            for i in range(4) for j in range(4)):
        raise RuntimeError(f"body parent {body.parent.name} is not identity")
    return body, [o for o in meshes if o.name.startswith("Wheel_")]


class Regions:
    """Face classification by the authored outlines."""

    def __init__(self, cfg, frame, half_width):
        self.cfg = cfg
        self.f = frame
        self.w = half_width

    def windshield(self, p, n):
        ws = self.cfg["windshield"]
        a, c, z = p
        if ws["view"] == "top":
            return z > ws["z_min"] and n.x < -0.12 and point_in_poly(a, c, ws["poly"])
        a0, a1 = ws["a_range"]
        return a0 <= a <= a1 and n.x < -0.4 and any(
            point_in_poly(c, z, box) for box in ws["boxes"])

    def side_window(self, p, n, side):
        a, c, z = p
        return (side * c > 0.45 * self.w and side * n.y > 0.2 and
                point_in_poly(a, z, self.cfg["side_window"]))

    def door(self, p):
        a, c, z = p
        return c > 0.45 * self.w and point_in_poly(a, z, self.cfg["door"])

    def rear_side_window(self, p, n):
        a, c, z = p
        return (abs(c) > 0.45 * self.w and n.y * c > 0 and abs(n.y) > 0.2 and
                any(point_in_poly(a, z, poly) for poly in self.cfg.get("rear_side_windows", ())))

    def rear_window(self, p, n):
        rw = self.cfg.get("rear_window")
        if rw is None:
            return False
        a, c, z = p
        if rw["view"] == "top":
            return z > rw["z_min"] and n.x > 0.12 and point_in_poly(a, c, rw["poly"])
        return a >= rw["a_min"] and n.x > 0.4 and any(point_in_poly(c, z, box) for box in rw["boxes"])

    def classify(self, p, n):
        if (self.windshield(p, n) or self.side_window(p, n, -1) or
                self.rear_side_window(p, n) or self.rear_window(p, n)):
            return BODY_GLASS
        if self.side_window(p, n, 1):
            return DOOR_GLASS if self.door(p) else BODY_GLASS
        if self.door(p):
            return DOOR
        return BODY

    def opening(self, p, n, margin):
        """True on or within `margin` of an opening (used to clear the liner;
        p and n are the outward shell point and normal in the car frame)."""
        a, c, z = p
        ws = self.cfg["windshield"]
        if ws["view"] == "top":
            if z > ws["z_min"] - margin and n.x < -0.05 and near_poly(a, c, ws["poly"], margin):
                return True
        else:
            a0, a1 = ws["a_range"]
            if a0 - margin <= a <= a1 + margin and n.x < -0.2 and any(
                    near_poly(c, z, box, margin) for box in ws["boxes"]):
                return True
        if abs(c) > 0.4 * self.w and abs(n.y) > 0.15 and n.y * c > 0 and                 near_poly(a, z, self.cfg["side_window"], margin):
            return True
        if abs(c) > 0.4 * self.w and abs(n.y) > 0.15 and n.y * c > 0 and any(
                near_poly(a, z, poly, margin) for poly in self.cfg.get("rear_side_windows", ())):
            return True
        rw = self.cfg.get("rear_window")
        if rw is not None:
            if rw["view"] == "top":
                if z > rw["z_min"] - margin and n.x > 0.05 and near_poly(a, c, rw["poly"], margin):
                    return True
            elif a >= rw["a_min"] - margin and n.x > 0.2 and any(
                    near_poly(c, z, box, margin) for box in rw["boxes"]):
                return True
        return c > 0.4 * self.w and n.y > 0.15 and near_poly(a, z, self.cfg["door"], margin)


def near_poly(x, y, poly, margin):
    if point_in_poly(x, y, poly):
        return True
    for i in range(len(poly)):
        (x0, y0), (x1, y1) = poly[i], poly[(i + 1) % len(poly)]
        dx, dy = x1 - x0, y1 - y0
        t = max(0.0, min(1.0, ((x - x0) * dx + (y - y0) * dy) / (dx * dx + dy * dy + 1e-12)))
        if (x - x0 - t * dx) ** 2 + (y - y0 - t * dy) ** 2 < margin * margin:
            return True
    return False


def bisect_outline(bm, frame, poly, plane_axis, select):
    """Cut the faces chosen by `select` along every edge of a 2D outline.

    plane_axis names the car axis the outline is extruded along ("c" for a
    side outline in (a, z), "z" for a top outline in (a, c), "a" for a
    front outline in (c, z))."""
    for i in range(len(poly)):
        p0, p1 = poly[i], poly[(i + 1) % len(poly)]
        if plane_axis == "c":
            w0 = frame.world(p0[0], 0, p0[1])
            d = frame.direction(p1[0] - p0[0], 0, p1[1] - p0[1])
            extrude = frame.C
        elif plane_axis == "z":
            w0 = frame.world(p0[0], p0[1], 0)
            d = frame.direction(p1[0] - p0[0], p1[1] - p0[1], 0)
            extrude = frame.Z
        else:
            w0 = frame.world(0, p0[0], p0[1])
            d = frame.direction(0, p1[0] - p0[0], p1[1] - p0[1])
            extrude = frame.A
        normal = d.cross(extrude)
        if normal.length < 1e-9:
            continue
        faces = [f for f in bm.faces if select(frame.local(f.calc_center_median()))]
        if not faces:
            continue
        geom = set(faces)
        for f in faces:
            geom.update(f.edges)
            geom.update(f.verts)
        bmesh.ops.bisect_plane(bm, geom=list(geom), dist=1e-7,
                               plane_co=w0, plane_no=normal.normalized())


def cut_openings(bm, cfg, frame, half_width):
    margin = 0.02
    side = cfg["side_window"]
    sx0, sx1, sz0, sz1 = poly_bbox(side, margin)
    for s in (1, -1):
        bisect_outline(bm, frame, side, "c", lambda p, s=s: (
            s * p.y > 0.4 * half_width and sx0 <= p.x <= sx1 and sz0 <= p.z <= sz1))
    door = cfg["door"]
    dx0, dx1, dz0, dz1 = poly_bbox(door, margin)
    bisect_outline(bm, frame, door, "c", lambda p: (
        p.y > 0.4 * half_width and dx0 <= p.x <= dx1 and dz0 <= p.z <= dz1))
    for poly in cfg.get("rear_side_windows", ()):
        rx0, rx1, rz0, rz1 = poly_bbox(poly, margin)
        for s in (1, -1):
            bisect_outline(bm, frame, poly, "c", lambda p, s=s, b=(rx0, rx1, rz0, rz1): (
                s * p.y > 0.4 * half_width and b[0] <= p.x <= b[1] and b[2] <= p.z <= b[3]))
    rw = cfg.get("rear_window")
    if rw is not None and rw["view"] == "top":
        x0, x1, y0, y1 = poly_bbox(rw["poly"], margin)
        bisect_outline(bm, frame, rw["poly"], "z", lambda p: (
            p.z > rw["z_min"] - margin and x0 <= p.x <= x1 and y0 <= p.y <= y1))
    elif rw is not None:
        for box in rw["boxes"]:
            x0, x1, y0, y1 = poly_bbox(box, margin)
            bisect_outline(bm, frame, box, "a", lambda p, b=(x0, x1, y0, y1): (
                p.x >= rw["a_min"] - margin and b[0] <= p.y <= b[1] and b[2] <= p.z <= b[3]))
    ws = cfg["windshield"]
    if ws["view"] == "top":
        x0, x1, y0, y1 = poly_bbox(ws["poly"], margin)
        bisect_outline(bm, frame, ws["poly"], "z", lambda p: (
            p.z > ws["z_min"] - margin and x0 <= p.x <= x1 and y0 <= p.y <= y1))
    else:
        a0, a1 = ws["a_range"]
        for box in ws["boxes"]:
            x0, x1, y0, y1 = poly_bbox(box, margin)
            bisect_outline(bm, frame, box, "a", lambda p: (
                a0 - margin <= p.x <= a1 + margin and x0 <= p.y <= x1 and y0 <= p.z <= y1))


def tag_faces(bm, regions, frame):
    layer = bm.faces.layers.int.get("part") or bm.faces.layers.int.new("part")
    counts = [0] * 7
    for f in bm.faces:
        tag = regions.classify(frame.local(f.calc_center_median()), frame.local(f.normal))
        f[layer] = tag
        counts[tag] += 1
    return layer, counts


class Inward:
    """Fixed inward directions for the aperture strips and the door card.

    Scan normals are noisy (worst on the decimated LOD cars); offsetting
    along them pushed strips and door-card corners out through the skin.
    Door and side apertures go straight in across the car; the windscreen
    aperture goes back along the glass's mean inward normal."""

    def __init__(self, bm, layer, frame, half_width):
        self.f = frame
        self.w = half_width
        front, rear = Vector(), Vector()
        for f in bm.faces:
            center = frame.local(f.calc_center_median())
            if f[layer] == BODY_GLASS and abs(center.y) < 0.5 * half_width:
                if center.x < 0:
                    front += f.normal * f.calc_area()
                else:
                    rear += f.normal * f.calc_area()
        self.windshield = -front.normalized() if front.length > 1e-9 else frame.A.copy()
        self.rear = -rear.normalized() if rear.length > 1e-9 else -frame.A

    def __call__(self, co):
        local = self.f.local(co)
        c = local.y
        if abs(c) > 0.6 * self.w:
            return -self.f.C if c > 0 else self.f.C.copy()
        return self.windshield if local.x < 0 else self.rear


def add_strip(bm, layer, edge_pairs, inward, depth, strip_tag, offsets):
    """Quads from boundary edges of owner faces, pushed `depth` inward."""
    made = 0
    for edge, owner, other in edge_pairs:
        verts = []
        for v in edge.verts:
            if v not in offsets:
                offsets[v] = bm.verts.new(v.co + inward(v.co) * depth)
            verts.append(offsets[v])
        v0, v1 = edge.verts
        try:
            face = bm.faces.new((v0, v1, verts[1], verts[0]))
        except ValueError:
            continue
        face.normal_update()
        toward = other.calc_center_median() - (v0.co + v1.co) * 0.5
        if face.normal.dot(toward) < 0:
            face.normal_flip()
        face[layer] = strip_tag
        made += 1
    return made


def build_edges_and_door_card(bm, layer, frame, half_width):
    inward = Inward(bm, layer, frame, half_width)
    door_inward = lambda co: -frame.C
    body_edges, door_edges = [], []
    for e in bm.edges:
        if len(e.link_faces) != 2:
            continue
        f0, f1 = e.link_faces
        t0, t1 = f0[layer], f1[layer]
        if t0 == t1:
            continue
        if t0 == BODY and t1 != BODY:
            body_edges.append((e, f0, f1))
        elif t1 == BODY and t0 != BODY:
            body_edges.append((e, f1, f0))
        if t0 == DOOR and t1 != DOOR:
            door_edges.append((e, f0, f1))
        elif t1 == DOOR and t0 != DOOR:
            door_edges.append((e, f1, f0))
    body_offsets, door_offsets = {}, {}
    made_body = add_strip(bm, layer, body_edges, inward, BODY_EDGE, BODY_JAMB, body_offsets)
    made_door = add_strip(bm, layer, door_edges, door_inward, DOOR_THICKNESS, DOOR_JAMB, door_offsets)
    door_faces = [f for f in bm.faces if f[layer] == DOOR]
    for f in door_faces:
        verts = []
        for v in f.verts:
            if v not in door_offsets:
                door_offsets[v] = bm.verts.new(v.co - frame.C * DOOR_THICKNESS)
            verts.append(door_offsets[v])
        try:
            card = bm.faces.new(list(reversed(verts)))
        except ValueError:
            continue
        card[layer] = DOOR_CARD
    return made_body, made_door, len(door_faces)


def material(name, hex_color, alpha=1.0):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    rgb = tuple(int(hex_color[i:i + 2], 16) / 255.0 for i in (0, 2, 4))
    mat.diffuse_color = (*rgb, alpha)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf is not None:
        bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
        bsdf.inputs["Roughness"].default_value = 0.05 if alpha < 1 else 0.7
        bsdf.inputs["Alpha"].default_value = alpha
    if alpha < 1:
        mat.surface_render_method = "BLENDED"
    return mat


def object_from_tags(src_bm, layer, tags, name, mat, origin=None, keep_uv=False):
    bm = src_bm.copy()
    lay = bm.faces.layers.int.get("part")
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f[lay] not in tags], context="FACES")
    if not keep_uv:
        for uv in list(bm.loops.layers.uv):
            bm.loops.layers.uv.remove(uv)
    if origin is not None:
        bm.transform(Matrix.Translation(-origin))
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    if origin is not None:
        obj.location = origin
    mesh.materials.clear()
    mesh.materials.append(mat)
    return obj, len(mesh.polygons)


class Builder:
    """Collects interior primitives per named part."""

    def __init__(self, frame):
        self.f = frame
        self.parts = {}

    def bm(self, name):
        if name not in self.parts:
            self.parts[name] = bmesh.new()
        return self.parts[name]

    def basis(self, size_m, pitch_deg=0.0):
        """Matrix mapping a unit cube to a box of size (a, c, z) in metres,
        pitched about the car's c axis (positive tips the top toward +a)."""
        sa, sc, sz = (s * M for s in size_m)
        p = math.radians(pitch_deg)
        a_axis = self.f.A * math.cos(p) - self.f.Z * math.sin(p)
        z_axis = self.f.Z * math.cos(p) + self.f.A * math.sin(p)
        return Matrix((a_axis * sa, self.f.C * sc, z_axis * sz)).transposed().to_4x4()

    def box(self, name, center, size_m, pitch_deg=0.0, bevel_m=0.02):
        bm = self.bm(name)
        tmp = bmesh.new()
        bmesh.ops.create_cube(tmp, size=1.0)
        if bevel_m > 0:
            try:
                bmesh.ops.bevel(tmp, geom=list(tmp.edges) + list(tmp.verts),
                                offset=min(0.45, bevel_m / max(min(size_m), 1e-3)),
                                segments=2, profile=0.5, affect="EDGES",
                                clamp_overlap=True)
            except Exception:
                pass
        tmp.transform(Matrix.Translation(center) @ self.basis(size_m, pitch_deg))
        bmesh.ops.recalc_face_normals(tmp, faces=tmp.faces)
        self._merge(bm, tmp)

    def cylinder(self, name, center, axis, radius_m, length_m, segments=16):
        bm = self.bm(name)
        tmp = bmesh.new()
        bmesh.ops.create_cone(tmp, cap_ends=True, segments=segments,
                              radius1=radius_m * M, radius2=radius_m * M,
                              depth=length_m * M)
        rot = Vector((0, 0, 1)).rotation_difference(axis.normalized()).to_matrix().to_4x4()
        tmp.transform(Matrix.Translation(center) @ rot)
        bmesh.ops.recalc_face_normals(tmp, faces=tmp.faces)
        self._merge(bm, tmp)

    def torus(self, name, center, normal, major_m, minor_m, ring=28, tube=8):
        tmp = bmesh.new()
        n = normal.normalized()
        u = n.orthogonal().normalized()
        v = n.cross(u)
        rings = []
        for i in range(ring):
            t = 2 * math.pi * i / ring
            radial = u * math.cos(t) + v * math.sin(t)
            rings.append([tmp.verts.new(
                center + radial * (major_m + minor_m * math.cos(2 * math.pi * j / tube)) * M +
                n * (minor_m * M * math.sin(2 * math.pi * j / tube))) for j in range(tube)])
        for i in range(ring):
            for j in range(tube):
                tmp.faces.new((rings[i][j], rings[(i + 1) % ring][j],
                               rings[(i + 1) % ring][(j + 1) % tube], rings[i][(j + 1) % tube]))
        bmesh.ops.recalc_face_normals(tmp, faces=tmp.faces)
        self._merge(self.bm(name), tmp)

    def marker(self, name, point):
        bm = self.bm(name)
        e = 0.0005
        bm.faces.new((bm.verts.new(point), bm.verts.new(point + Vector((e, 0, 0))),
                      bm.verts.new(point + Vector((0, e, 0)))))

    @staticmethod
    def _merge(dst, src):
        mapping = {v: dst.verts.new(v.co) for v in src.verts}
        for f in src.faces:
            try:
                dst.faces.new([mapping[v] for v in f.verts])
            except ValueError:
                pass
        src.free()

    def emit(self, origins):
        made = []
        for name, bm in self.parts.items():
            origin = origins.get(name.split("__")[0])
            if name.startswith("Marker__"):
                bm.verts.ensure_lookup_table()
                origin = bm.verts[0].co.copy()
            if origin is not None:
                bm.transform(Matrix.Translation(-origin))
            mesh = bpy.data.meshes.new(name)
            bm.to_mesh(mesh)
            bm.free()
            obj = bpy.data.objects.new(name, mesh)
            bpy.context.scene.collection.objects.link(obj)
            if origin is not None:
                obj.location = origin
            parts = name.split("__")
            color = parts[2] if len(parts) > 2 else "FF00FF"
            mesh.materials.append(material("Cabin_" + color, color))
            made.append(obj)
        return made


def ray_hit(bvh, origin, direction):
    hit = bvh.ray_cast(origin, direction.normalized(), 10.0)
    return hit[0]


def build_interior(cfg, frame, bvh, ground_z, half_width):
    pal = cfg["palette"]
    b = Builder(frame)
    belt_z = min(p[1] for p in cfg["side_window"])
    a_seat = cfg["seat_a"]
    # Inner half width at shoulder height beside the seat.
    probe = frame.world(a_seat, 0.0, belt_z - 0.01)
    hit = ray_hit(bvh, probe, frame.C)
    inner = (frame.local(hit).y if hit else half_width) - LINER_INSET - 0.004
    c_seat = 0.47 * inner
    roof = ray_hit(bvh, frame.world(a_seat + 0.01, c_seat, 1.0), -frame.Z)
    roof_z = (frame.local(roof).z if roof else belt_z + 0.08) - LINER_INSET
    # Floor just above the shell's underside (or the door sill when the scan
    # has no underside), never below the body where it would show.
    under = ray_hit(bvh, frame.world(a_seat, 0.5 * c_seat, ground_z - 0.05), frame.Z)
    sill_z = min(p[1] for p in cfg["door"]) + 0.012
    under_z = frame.local(under).z + 0.006 if under and frame.local(under).z < belt_z else sill_z
    floor_z = max(ground_z + 0.20 * M, min(under_z, sill_z))
    # As high as the roof allows a seated 2.5 m bodybuilder (head top about
    # 1.15 m above the cushion), so the shoulders clear the beltline; the
    # game lowers the seat per driver when the head would touch the roof.
    cush_z = max(ground_z + 0.36 * M, min(ground_z + 0.58 * M, roof_z - 1.22 * M))
    ws = cfg["windshield"]
    cowl_a = (min(p[0] for p in ws["poly"]) if ws["view"] == "top" else ws["a_range"][0] + 0.03)
    a0, a1 = cfg["cabin"]

    def seat(group, c, accent=True):
        b.box(f"{group}__Seat__{pal['seat']}",
              frame.world(a_seat + 0.02 * M, c, cush_z - 0.07 * M), (0.54, 0.54, 0.14))
        back_center = frame.world(a_seat + 0.30 * M, c, cush_z + 0.32 * M)
        b.box(f"{group}__Seat__{pal['seat']}", back_center, (0.13, 0.54, 0.66), pitch_deg=-13)
        b.box(f"{group}__Seat__{pal['seat']}",
              frame.world(a_seat + 0.38 * M, c, cush_z + 0.76 * M), (0.10, 0.30, 0.20), pitch_deg=-13)
        if accent:
            b.box(f"{group}__Accent__{pal['accent']}",
                  frame.world(a_seat + 0.235 * M, c, cush_z + 0.33 * M), (0.02, 0.18, 0.56),
                  pitch_deg=-13, bevel_m=0.005)

    seat("Driver_Seat", c_seat)
    seat("Cabin", -c_seat)
    rear_a = a_seat + cfg["rear_bench"]
    if rear_a + 0.04 < a1:
        width = 2 * (inner - 0.04 * M) / M
        b.box(f"Cabin__Seat__{pal['seat']}",
              frame.world(rear_a, 0.0, cush_z - 0.06 * M), (0.52, width, 0.14))
        b.box(f"Cabin__Seat__{pal['seat']}",
              frame.world(rear_a + 0.30 * M, 0.0, cush_z + 0.30 * M), (0.13, width, 0.62), pitch_deg=-15)
        # Rear headrests and a centre armrest: the rear bench now shows
        # through the rear side windows and the rear window.
        for side in (-1, 1):
            b.box(f"Cabin__Seat__{pal['seat']}",
                  frame.world(rear_a + 0.38 * M, side * c_seat, cush_z + 0.72 * M), (0.10, 0.28, 0.18),
                  pitch_deg=-15)
        b.box(f"Cabin__Accent__{pal['accent']}",
              frame.world(rear_a + 0.02 * M, 0.0, cush_z + 0.10 * M), (0.40, 0.18, 0.08), bevel_m=0.01)
        behind = rear_a + 0.40 * M
        if a1 - behind > 0.18 * M:
            rw = cfg.get("rear_window")
            if rw is not None and rw["view"] == "top":
                # Saloon / coupe: parcel shelf under the rear window.
                shelf_z = min(belt_z - 0.01 * M, cush_z + 0.70 * M)
                b.box(f"Cabin__Trim__{pal['trim']}",
                      frame.world((behind + a1) * 0.5, 0.0, shelf_z), ((a1 - behind) / M, width, 0.04),
                      bevel_m=0.005)
            else:
                # Wagon / SUV: a carpeted load floor with a low cargo cover lip.
                b.box(f"Cabin__Floor__{pal['floor']}",
                      frame.world((behind + a1) * 0.5, 0.0, cush_z - 0.10 * M), ((a1 - behind) / M, width, 0.05),
                      bevel_m=0.0)
                b.box(f"Cabin__Trim__{pal['trim']}",
                      frame.world(behind + 0.03 * M, 0.0, cush_z + 0.62 * M), (0.06, width, 0.05),
                      bevel_m=0.005)
    # Floor and console.
    cabin_len = (a1 - a0) / M
    low = ray_hit(bvh, frame.world(a_seat, 0.0, floor_z + 0.06 * M), frame.C)
    floor_half = min(inner, (frame.local(low).y if low else inner) - 0.006)
    b.box(f"Cabin__Floor__{pal['floor']}",
          frame.world((a0 + a1) * 0.5, 0.0, floor_z - 0.03 * M),
          (cabin_len, 2 * floor_half / M, 0.06), bevel_m=0.0)
    # Footwell tub down to just above the road: a 2.5 m driver's feet sit
    # below the cabin floor of the tall cars (the Hummer) and would show
    # under the body; this reads as the dark chassis between the wheels.
    well_top = floor_z - 0.02 * M
    well_bottom = ground_z + 0.14 * M
    if well_top - well_bottom > 0.04 * M:
        well_front = a_seat - 1.05 * M
        well_rear = a_seat + 0.15 * M
        b.box(f"Cabin__Underbody__{pal['trim']}",
              frame.world((well_front + well_rear) * 0.5, 0.0, (well_top + well_bottom) * 0.5),
              ((well_rear - well_front) / M, 1.8 * floor_half / M, (well_top - well_bottom) / M),
              bevel_m=0.02)
    b.box(f"Cabin__Console__{pal['console']}",
          frame.world(a_seat - 0.10 * M, 0.0, (floor_z + cush_z) * 0.5 + 0.02 * M),
          (0.62, 0.22, (cush_z - floor_z) / M + 0.08))
    b.box(f"Cabin__Trim__{pal['trim']}",
          frame.world(a_seat - 0.30 * M, 0.0, cush_z + 0.09 * M), (0.05, 0.05, 0.10), bevel_m=0.015)
    # Dashboard from the windscreen base back to the driver's knees.
    dash_rear = a_seat - 0.60 * M
    dash_front = cowl_a + 0.03
    dash_len = max(0.2, (dash_rear - dash_front) / M)
    dash_top = min(belt_z - 0.02 * M, cush_z + 0.58 * M)
    b.box(f"Cabin__Dash__{pal['dash']}",
          frame.world((dash_front + dash_rear) * 0.5, 0.0, dash_top - 0.17 * M),
          (dash_len, 1.8 * inner / M, 0.34), bevel_m=0.05)
    b.box(f"Cabin__Trim__{pal['trim']}",
          frame.world(dash_rear - 0.10 * M, c_seat, dash_top + 0.03 * M), (0.18, 0.40, 0.07),
          bevel_m=0.02)
    b.box(f"Cabin__Screen__{pal['screen']}",
          frame.world(dash_rear - 0.01 * M, 0.0, dash_top - 0.06 * M), (0.02, 0.26, 0.15),
          pitch_deg=12, bevel_m=0.004)
    # Steering wheel, column into the dash; the game moves it to the hands.
    wheel_center = frame.world(a_seat - 0.44 * M, c_seat, cush_z + 0.37 * M)
    tilt = math.radians(24)
    normal = frame.direction(math.cos(tilt), 0.0, math.sin(tilt))
    b.torus(f"Steering_Wheel__Rim__{pal['wheel']}", wheel_center, normal, 0.19, 0.022)
    b.cylinder(f"Steering_Wheel__Rim__{pal['wheel']}", wheel_center - normal * 0.01 * M,
               normal, 0.06, 0.05)
    side = normal.orthogonal().normalized()
    up = normal.cross(frame.C).normalized()
    for direction in (frame.C, -frame.C, -up):
        d = direction - normal * direction.dot(normal)
        b.cylinder(f"Steering_Wheel__Rim__{pal['wheel']}",
                   wheel_center + d.normalized() * 0.10 * M, d, 0.012, 0.17, segments=8)
    b.cylinder(f"Steering_Wheel__Rim__{pal['wheel']}", wheel_center - normal * 0.22 * M,
               normal, 0.035, 0.42, segments=12)
    hinge = door_hinge(cfg, frame, bvh)
    b.marker("Marker__Seat", frame.world(a_seat, c_seat, cush_z))
    b.marker("Marker__Roof", frame.world(a_seat + 0.01, c_seat, roof_z))
    b.marker("Marker__Wheel", wheel_center)
    b.marker("Marker__Floor", frame.world(a_seat, c_seat, floor_z))
    b.marker("Marker__Hinge", hinge)
    origins = {"Driver_Seat": frame.world(a_seat, c_seat, cush_z), "Steering_Wheel": wheel_center}
    objects = b.emit(origins)
    return objects, {"a_seat": a_seat, "c_seat": c_seat, "inner": inner,
                     "roof_above_cushion_m": (roof_z - cush_z) / M,
                     "cushion_above_ground_m": (cush_z - ground_z) / M, "hinge": hinge}


def door_hinge(cfg, frame, bvh):
    door = cfg["door"]
    hinge_a = min(p[0] for p in door) + 0.004
    belt_z = min(p[1] for p in cfg["side_window"])
    z = belt_z - 0.03
    hit = ray_hit(bvh, frame.world(hinge_a, 2.0, z), -frame.C)
    c = frame.local(hit).y if hit else 0.2
    return frame.world(hinge_a, c - DOOR_THICKNESS * 0.5, z)


def build_liner(body_obj, regions, frame, cfg, ground_z, pal):
    liner = body_obj.copy()
    liner.data = body_obj.data.copy()
    liner.name = f"Cabin__Liner__{pal['liner']}"
    bpy.context.scene.collection.objects.link(liner)
    faces = len(liner.data.polygons)
    mod = liner.modifiers.new("decimate", "DECIMATE")
    mod.ratio = min(1.0, LINER_FACES / max(1, faces))
    bpy.context.view_layer.objects.active = liner
    for o in bpy.context.selected_objects:
        o.select_set(False)
    liner.select_set(True)
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bm = bmesh.new()
    bm.from_mesh(liner.data)
    bm.transform(liner.matrix_world)
    bm.normal_update()
    # The whole shell is lined (not only the cabin): any shell face seen
    # from inside through the glass must be closed, including the rear
    # window and the parts of the roof and trunk behind it.
    floor_z = ground_z + 0.15 * M
    drop = []
    for f in bm.faces:
        p = frame.local(f.calc_center_median())
        if p.z < floor_z or regions.opening(p, frame.local(f.normal), 0.008):
            drop.append(f)
    bmesh.ops.delete(bm, geom=drop, context="FACES")
    for v in bm.verts:
        v.co -= v.normal * LINER_INSET
    for f in bm.faces:
        f.normal_flip()
    for uv in list(bm.loops.layers.uv):
        bm.loops.layers.uv.remove(uv)
    bm.transform(liner.matrix_world.inverted())
    bm.to_mesh(liner.data)
    count = len(bm.faces)
    bm.free()
    liner.data.materials.clear()
    liner.data.materials.append(material("Cabin_" + pal["liner"], pal["liner"]))
    return liner, count


def process(name, render):
    cfg = VEHICLES[name]
    target = cfg["path"]
    BACKUP.mkdir(parents=True, exist_ok=True)
    backup = BACKUP / target.name
    if not backup.exists():
        shutil.copy2(target, backup)
    body, wheels = import_asset(backup)
    frame = Frame(cfg["axis"])
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bm.transform(body.matrix_world)
    bm.normal_update()
    half_width = max(abs(frame.local(v.co).y) for v in bm.verts)
    ground_z = min(min((o.matrix_world @ Vector(c)).z for c in o.bound_box) for o in [body] + wheels)
    bvh = BVHTree.FromBMesh(bm)
    regions = Regions(cfg, frame, half_width)

    cut_openings(bm, cfg, frame, half_width)
    bm.normal_update()
    layer, counts = tag_faces(bm, regions, frame)
    if min(counts[DOOR], counts[DOOR_GLASS], counts[BODY_GLASS]) < 20:
        raise RuntimeError(f"{name}: an opening selected too few faces: {counts}")
    jambs, door_edges, door_faces = build_edges_and_door_card(bm, layer, frame, half_width)
    pal = cfg["palette"]
    hinge = door_hinge(cfg, frame, bvh)
    body_mat = body.data.materials[0]
    glass = material("Glass_" + GLASS, GLASS, alpha=0.3)
    made = {}
    made["door"] = object_from_tags(bm, layer, {DOOR}, "Door_Driver__Shell", body_mat, hinge, True)
    made["door_glass"] = object_from_tags(bm, layer, {DOOR_GLASS}, "Door_Driver__Glass", glass, hinge)
    made["door_card"] = object_from_tags(bm, layer, {DOOR_CARD}, f"Door_Driver__Card__{pal['liner']}",
                                         material("Cabin_" + pal["liner"], pal["liner"]), hinge)
    made["door_edge"] = object_from_tags(bm, layer, {DOOR_JAMB}, f"Door_Driver__Trim__{pal['trim']}",
                                         material("Cabin_" + pal["trim"], pal["trim"]), hinge)
    made["glass"] = object_from_tags(bm, layer, {BODY_GLASS}, "Glass__Body", glass)
    made["jamb"] = object_from_tags(bm, layer, {BODY_JAMB}, f"Cabin__Trim__{pal['trim']}",
                                    material("Cabin_" + pal["trim"], pal["trim"]))
    # The body keeps its own object, name, material and UVs.
    keep = bm.copy()
    klay = keep.faces.layers.int.get("part")
    bmesh.ops.delete(keep, geom=[f for f in keep.faces if f[klay] != BODY], context="FACES")
    keep.faces.layers.int.remove(klay)
    keep.transform(body.matrix_world.inverted())
    original = body.data.copy()
    keep.to_mesh(body.data)
    keep.free()
    # Liner from the uncut shell.
    shell = bpy.data.objects.new("shell_tmp", original)
    bpy.context.scene.collection.objects.link(shell)
    shell.matrix_world = body.matrix_world
    liner, liner_faces = build_liner(shell, regions, frame, cfg, ground_z, pal)
    bpy.data.objects.remove(shell)
    interior, info = build_interior(cfg, frame, bvh, ground_z, half_width)
    bm.free()

    export(target)
    summary = " ".join(f"{k}={v[1]}" for k, v in made.items())
    print(f"GYMCHAOS_VEHICLE_CABIN_OK name={name} {summary} liner={liner_faces} "
          f"doorEdges={door_edges} bodyEdges={jambs} interiorParts={len(interior)} "
          f"seatC={info['c_seat']:.3f} roofAboveCushion={info['roof_above_cushion_m']:.2f}m "
          f"cushion={info['cushion_above_ground_m']:.2f}m output={target}")
    if render:
        render_evidence(name, frame, hinge)


def export(target):
    for o in bpy.data.objects:
        o.select_set(o.type in {"MESH", "EMPTY"})
    if target.suffix.lower() == ".fbx":
        bpy.ops.export_scene.fbx(
            filepath=str(target), use_selection=True, apply_unit_scale=True,
            bake_space_transform=False, object_types={"MESH", "EMPTY"},
            path_mode="COPY", embed_textures=True, add_leaf_bones=False,
            use_custom_props=True)
    else:
        bpy.ops.export_scene.gltf(
            filepath=str(target), export_format="GLB", use_selection=True,
            export_extras=True, export_yup=True)


def render_evidence(name, frame, hinge):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = 960
    scene.render.resolution_y = 640
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("w")
    world.color = (0.55, 0.6, 0.65)
    scene.world = world
    for o in bpy.data.objects:
        if o.name.startswith("Marker__"):
            o.hide_render = True
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 4.0
    sun.rotation_euler = (math.radians(50), 0, math.radians(30))
    scene.collection.objects.link(sun)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.lens = 38
    door_parts = [o for o in bpy.data.objects if o.name.startswith("Door_Driver")]
    views = {
        "front": frame.world(-0.75, 0.38, 0.22),
        "side": frame.world(-0.05, 0.85, 0.16),
        "rear": frame.world(0.85, 0.42, 0.3),
    }
    target = frame.world(-0.02, 0.0, 0.02)
    for open_door in (False, True):
        for o in door_parts:
            o.rotation_euler = (0, 0, 0)
            if open_door:
                # Swing outward: the rear edge moves toward +c.
                sign = 1.0 if frame.A.cross(frame.C).z > 0 else -1.0
                o.rotation_euler[2] = math.radians(62) * sign
        for view, pos in views.items():
            if open_door and view in ("front", "rear"):
                continue
            cam.location = pos
            cam.rotation_euler = (target - pos).to_track_quat("-Z", "Y").to_euler()
            scene.render.filepath = str(EVIDENCE / f"{name}_{view}{'_open' if open_door else ''}.png")
            bpy.ops.render.render(write_still=True)


def main():
    args = parse_args()
    names = args.names or list(VEHICLES)
    for name in names:
        process(name, not args.no_render)
    print(f"GYMCHAOS_VEHICLE_CABINS_DONE count={len(names)}")


if __name__ == "__main__":
    main()
