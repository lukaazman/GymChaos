"""Multi-angle interior renders of every cabin vehicle (closed + door open).

Usage:
  blender -b --factory-startup -P Tools/render_vehicle_interiors.py -- OUTDIR [tag] [name ...]
Writes OUTDIR/<tag>_<name>_<view>.png. Camera clip planes cut the roof for the
top view, so the original shell is untouched.
"""
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
A = ROOT / "GymChaos/Assets"
CARS = {
    "cbum": A / "Resources/Vehicles/cbum_vehicle.fbx",
    "zyzz": A / "Resources/Vehicles/zyzz_vehicle.fbx",
    "jaycutler": A / "Resources/Vehicles/jaycutler_vehicle.fbx",
    "ronnie": A / "Resources/Vehicles/ronnie_vehicle.fbx",
    "police": A / "StreamingAssets/BodyBuilders/vehicles/Policecar_lod.glb",
    "arnold": A / "StreamingAssets/BodyBuilders/vehicles/Arnold_Hummer_lod.glb",
}
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = Path(argv[0]) if argv else ROOT / "Logs/agent/vehicle-interiors"
TAG = argv[1] if len(argv) > 1 else "now"
NAMES = argv[2:] or list(CARS)
OUT.mkdir(parents=True, exist_ok=True)


def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def load(path):
    if path.suffix == ".fbx":
        bpy.ops.import_scene.fbx(filepath=str(path))
    else:
        bpy.ops.import_scene.gltf(filepath=str(path))
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    for o in meshes:
        if o.name.startswith("Marker__"):
            o.hide_render = True
        for slot in o.material_slots:
            m = slot.material
            if m and ("Glass" in m.name or "Glass" in o.name):
                m.blend_method = "BLEND"
                if m.use_nodes:
                    for n in m.node_tree.nodes:
                        if n.type == "BSDF_PRINCIPLED":
                            n.inputs["Alpha"].default_value = 0.25
    return meshes


def world_bbox(objs):
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


def center(objs):
    lo, hi = world_bbox(objs)
    return (lo + hi) / 2


def setup_scene():
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE_NEXT"
    sc.render.resolution_x, sc.render.resolution_y = 800, 520
    w = bpy.data.worlds.new("w")
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.6, 0.65, 0.7, 1)
    w.node_tree.nodes["Background"].inputs[1].default_value = 1.4
    sc.world = w
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(40), math.radians(15), math.radians(35))
    sc.collection.objects.link(sun)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    return sc, cam


hi_L = [1.0]


def shoot(sc, cam, name, view, pos, target, lens=32, clip_start=0.05):
    cam.data.clip_end = 1000
    cam.location = pos
    cam.data.type = "ORTHO" if view.endswith("top_cut") or view.endswith("open_top") else "PERSP"
    cam.data.ortho_scale = 1.1 * (hi_L[0])
    cam.data.lens = lens
    cam.data.clip_start = clip_start
    cam.rotation_euler = (target - pos).to_track_quat("-Z", "Y").to_euler()
    sc.render.filepath = str(OUT / f"{TAG}_{name}_{view}.png")
    bpy.ops.render.render(write_still=True)


def run(name):
    clear()
    meshes = load(CARS[name])
    sc, cam = setup_scene()
    lo, hi = world_bbox(meshes)
    c = (lo + hi) / 2
    size = hi - lo
    long_axis = 0 if size.x > size.y else 1
    side_axis = 1 - long_axis
    doors = [o for o in meshes if o.name.startswith("Door_Driver")]
    door_c = center(doors) if doors else c
    side = 1.0 if door_c[side_axis] > c[side_axis] else -1.0
    L, W, H = size[long_axis], size[side_axis], size.z
    hi_L[0] = L
    # Cabin centre guess: door centre along the car, mid height of the car.
    cab = Vector(c)
    cab[long_axis] = door_c[long_axis]
    cab.z = lo.z + H * 0.55

    def at(a, s, z):
        v = Vector((0, 0, lo.z + H * z))
        v[long_axis] = c[long_axis] + a * L
        v[side_axis] = c[side_axis] + s * W
        return v

    # Which way is the nose? Door sits behind the nose, so the nose is the
    # longer remainder; fall back to -axis.
    nose = -1.0 if (door_c[long_axis] - lo[long_axis]) > (hi[long_axis] - door_c[long_axis]) else 1.0
    nose = -nose  # door nearer to nose end side means nose direction toward door
    views = {
        "side_in": (at(0.0, side * 1.6, 0.62) + Vector((0, 0, 0)), cab, 30),
        "front_in": (at(nose * 1.1, side * 0.35, 0.75), cab, 34),
        "rear_in": (at(-nose * 1.1, side * 0.35, 0.75), cab, 34),
        "seat_pov": (at((door_c[long_axis] - c[long_axis]) / L, side * 0.12, 0.5), at(nose * 1.2, side * 0.0, 0.42), 24),
    }
    # Through the windscreen from above and in front, as someone beside the car sees it.
    views["ws_above"] = (at(nose * 0.75, side * 0.35, 1.25), at(nose * 0.22, 0.0, 0.5), 30)
    views["ws_down"] = (at(nose * 0.3, side * 0.15, 1.9), at(nose * 0.18, side * 0.05, 0.4), 34)
    views["ws_above2"] = (at(nose * 0.55, -side * 0.2, 1.45), at(nose * 0.2, side * 0.1, 0.45), 30)
    views["dash_close"] = (at(nose * 0.75, side * 0.1, 0.7), at(nose * 0.1, side * 0.1, 0.6), 40)
    for v, (p, t, lens) in views.items():
        shoot(sc, cam, name, v, p, t, lens)
    # Top view with the roof clipped away by the near plane.
    top = Vector((c.x, c.y, hi.z + 1.0 * L))
    clip = top.z - (lo.z + H * 0.68)
    shoot(sc, cam, name, "top_cut", top, Vector((c.x, c.y, cab.z)), 22, clip_start=clip)
    # Door open: try both signs, keep the one that moves the door away.
    for d in doors:
        d.rotation_mode = "XYZ"
    if doors:
        base = door_c.copy()
        best = None
        for sign in (1, -1):
            for d in doors:
                d.rotation_euler[2] += math.radians(62) * sign
            bpy.context.view_layer.update()
            d = abs(center(doors)[side_axis] - c[side_axis])
            for dd in doors:
                dd.rotation_euler[2] -= math.radians(62) * sign
            if best is None or d > best[0]:
                best = (d, sign)
        for d in doors:
            d.rotation_euler[2] += math.radians(62) * best[1]
        bpy.context.view_layer.update()
        shoot(sc, cam, name, "door_open", at(-nose * 0.35, side * 1.6, 0.8), cab, 32)
        fwd = at(nose * 0.12, side * 0.05, 0.5)
        shoot(sc, cam, name, "door_fwd", at(-nose * 0.2, side * 1.1, 0.75), fwd, 40)
        shoot(sc, cam, name, "door_fwd2", at(nose * 0.55, side * 1.0, 0.8), at(nose * 0.1, side * 0.0, 0.55), 40)
        shoot(sc, cam, name, "door_open_top", top, Vector((c.x, c.y, cab.z)), 22, clip_start=clip)


for n in NAMES:
    run(n)
print("GYMCHAOS_VEHICLE_INTERIOR_RENDER_DONE")
