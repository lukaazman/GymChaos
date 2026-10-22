"""Verify the exported player race buggy GLB (Tools/build_player_race_car.py).

Imports GymChaos/Assets/StreamingAssets/BodyBuilders/vehicles/player_buggy.glb
into an empty scene and checks what the game relies on: part names, markers,
top-level nodes, wheel props and pivots, door hinge origin, roll-bar roof,
steering rim radius, overall length and the triangle budget.

Usage:
  blender -b --factory-startup -P Tools/verify_player_race_car.py
"""
import math
import re
import sys
from pathlib import Path

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
GLB = ROOT / "GymChaos/Assets/StreamingAssets/BodyBuilders/vehicles/player_buggy.glb"
LENGTH = 3.4
GAME_LENGTH = 3.4 * 1.25 * 1.12
TRI_MIN, TRI_MAX = 8000, 15000
WHEELS = ("Wheel_PL", "Wheel_PR", "Wheel_NL", "Wheel_NR")
MARKERS = ("Seat", "Roof", "Wheel", "Floor", "Hinge")
HEX = re.compile(r"^[0-9A-Fa-f]{6}$")


def world_bounds(obj):
    pts = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


def main():
    failures = []
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if not GLB.exists():
        print(f"GYMCHAOS_PLAYER_CAR_VERIFY_FAIL missing={GLB}")
        sys.exit(1)
    bpy.ops.import_scene.gltf(filepath=str(GLB))
    meshes = {o.name: o for o in bpy.data.objects if o.type == "MESH"}
    names = set(meshes)

    for required in ("Body__Shell", "Glass__Body", "Door_Driver__Shell", "Door_Driver__Glass", *WHEELS):
        if required not in names:
            failures.append(f"missing {required}")
    for prefix in ("Door_Driver__Card__", "Door_Driver__Trim__", "Driver_Seat__Seat__",
                   "Steering_Wheel__Rim__", "Cabin__"):
        if not any(n.startswith(prefix) for n in names):
            failures.append(f"missing {prefix}*")
    for marker in MARKERS:
        if f"Marker__{marker}" not in names:
            failures.append(f"missing Marker__{marker}")
    for obj in bpy.data.objects:
        if obj.parent is not None:
            failures.append(f"{obj.name} is not top level (parent {obj.parent.name})")
    for name in names:
        parts = name.split("__")
        if len(parts) >= 3 and not HEX.match(parts[2]):
            failures.append(f"{name}: bad colour {parts[2]}")

    def marker(name):
        obj = meshes.get(f"Marker__{name}")
        if obj is None:
            return Vector()
        return sum((obj.matrix_world @ v.co for v in obj.data.vertices), Vector()) / len(obj.data.vertices)

    # Wheels: origin on the axle, one material, props match the mesh.
    for name in WHEELS:
        obj = meshes.get(name)
        if obj is None:
            continue
        lo, hi = world_bounds(obj)
        centre = (lo + hi) * 0.5
        radius = (hi.z - lo.z) * 0.5
        axle = obj.matrix_world.translation
        # On the axle line (the hub cap may sit off-centre along the axle).
        if math.hypot(axle.y - centre.y, axle.z - centre.z) > 0.01 or abs(axle.x - centre.x) > 0.05:
            failures.append(f"{name}: origin {tuple(round(c, 3) for c in axle)} off axle {tuple(round(c, 3) for c in centre)}")
        prop = obj.get("wheel_radius")
        if prop is None or abs(prop - radius) > 0.03 * radius:
            failures.append(f"{name}: wheel_radius {prop} vs mesh {radius:.3f}")
        if obj.get("wheel_axis") != "X":
            failures.append(f"{name}: wheel_axis {obj.get('wheel_axis')}")
        if len(obj.data.materials) != 1:
            failures.append(f"{name}: {len(obj.data.materials)} materials (GLB loader would split it)")
        if abs(lo.z) > 0.01:
            failures.append(f"{name}: tyre bottom at {lo.z:.3f}, not on the ground")
    front = [meshes[n] for n in ("Wheel_NL", "Wheel_NR") if n in meshes]
    rear = [meshes[n] for n in ("Wheel_PL", "Wheel_PR") if n in meshes]
    if front and rear:
        if not all(f.matrix_world.translation.y < r.matrix_world.translation.y for f in front for r in rear):
            failures.append("front (N) wheels are not ahead of the rear (P) wheels")
        if not all(f.get("wheel_radius", 0) < r.get("wheel_radius", 0) for f in front for r in rear):
            failures.append("rear tyres are not bigger than the front ones")

    # Door parts share the hinge origin.
    hinge = marker("Hinge")
    for name in names:
        if name.startswith("Door_Driver"):
            origin = meshes[name].matrix_world.translation
            if (origin - hinge).length > 0.01:
                failures.append(f"{name}: origin {tuple(round(c, 3) for c in origin)} not on the hinge")
    if hinge.x <= 0:
        failures.append("hinge not on the driver (+X) side")

    # Roof = the roll bar top, above the seat.
    seat, roof, floor = marker("Seat"), marker("Roof"), marker("Floor")
    bars = meshes.get("Roll_Bars")
    if bars is None:
        failures.append("missing Roll_Bars")
    else:
        top = world_bounds(bars)[1].z
        if abs(top - roof.z) > 0.02:
            failures.append(f"Roof marker {roof.z:.3f} is not the roll bar top {top:.3f}")
    if not (floor.z < seat.z < roof.z):
        failures.append(f"marker heights floor={floor.z:.2f} seat={seat.z:.2f} roof={roof.z:.2f}")

    # Steering rim: GymVehicleCabin expects 0.19 m once fitted to the game length.
    rim_radius = 0.0
    for name in names:
        if name.startswith("Steering_Wheel__Rim__"):
            lo, hi = world_bounds(meshes[name])
            rim_radius = max(rim_radius, (hi.x - lo.x) * 0.5)
    expected = 0.19 * LENGTH / GAME_LENGTH + 0.016
    if abs(rim_radius - expected) > 0.01:
        failures.append(f"steering rim radius {rim_radius:.3f}, expected {expected:.3f}")

    # Overall size and budget.
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    tris = 0
    for name, obj in meshes.items():
        if name.startswith("Marker__"):
            continue
        a, b = world_bounds(obj)
        lo = Vector((min(lo.x, a.x), min(lo.y, a.y), min(lo.z, a.z)))
        hi = Vector((max(hi.x, b.x), max(hi.y, b.y), max(hi.z, b.z)))
        tris += sum(len(p.vertices) - 2 for p in obj.data.polygons)
    length = hi.y - lo.y
    width = hi.x - lo.x
    if abs(length - LENGTH) > 0.03:
        failures.append(f"length {length:.3f}, expected {LENGTH}")
    if width >= length:
        failures.append(f"width {width:.2f} not below length {length:.2f}")
    if not TRI_MIN <= tris <= TRI_MAX:
        failures.append(f"{tris} triangles, budget {TRI_MIN}-{TRI_MAX}")

    summary = (f"objects={len(meshes)} tris={tris} length={length:.3f} width={width:.3f} "
               f"height={hi.z - lo.z:.3f} rim={rim_radius:.3f} seat={tuple(round(c, 2) for c in seat)} "
               f"roof={roof.z:.2f} hinge={tuple(round(c, 2) for c in hinge)}")
    if failures:
        print("GYMCHAOS_PLAYER_CAR_VERIFY_FAIL " + " | ".join(failures) + " || " + summary)
        sys.exit(1)
    print("GYMCHAOS_PLAYER_CAR_VERIFY_OK " + summary)


if __name__ == "__main__":
    main()
