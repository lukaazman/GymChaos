"""Contract check for the remade vehicle GLBs (Tools/vehicle_kit.py).

Imports each GLB and checks what the game reads: four Wheel_* nodes with
their axle origin, the cabin markers, a driver door with its hinge marker,
glass, lamp materials (police strobes) and the triangle budget.

Usage: blender -b -noaudio --python-exit-code 1 -P Tools/verify_remake_vehicle.py -- all|<id> [...]
Prints REMAKE_VEHICLE_OK <id> per vehicle and REMAKE_VEHICLE_ALL_OK count=<n>.
"""
import sys
from pathlib import Path

import bpy

ROOT = Path(__file__).resolve().parents[1]
GLB_DIR = ROOT / "GymChaos/Assets/StreamingAssets/BodyBuilders/vehicles/remake"
CARS = ("arnold", "cbum", "jaycutler", "zyzz", "police")
ALL = CARS + ("bus", "cloud")
MARKERS = ("Seat", "Roof", "Wheel", "Floor", "Hinge")
MIN_TRIS, MAX_TRIS = 1500, 8000


def check(vehicle_id):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(GLB_DIR / f"{vehicle_id}.glb"))
    objs = [o for o in bpy.data.objects if o.type == "MESH"]
    names = [o.name for o in objs]
    mats = {m.name for o in objs for m in o.data.materials if m}
    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs if not o.name.startswith("Marker__"))
    errors = []
    real = [m for m in mats if not m.startswith("Marker")]
    if len(real) > 14:
        errors.append(f"materials={len(real)} (max 14, SetPass budget)")
    if not MIN_TRIS <= tris <= MAX_TRIS:
        errors.append(f"tris={tris} outside {MIN_TRIS}-{MAX_TRIS}")
    wheels = [o for o in objs if o.name.startswith("Wheel_")]
    if vehicle_id != "cloud":
        if len(wheels) != 4:
            errors.append(f"wheels={len(wheels)}")
        for w in wheels:
            # One material: the game's GLB loader splits materials into separate wheel nodes.
            if len(w.data.materials) != 1:
                errors.append(f"{w.name} materials={len(w.data.materials)}")
            # Origin on the axle: the tyre's bounds are centred on its node in
            # the rolling plane (the rim face makes it uneven across the axle).
            ys = [v.co.y for v in w.data.vertices]
            zs = [v.co.z for v in w.data.vertices]
            off = max(abs(max(ys) + min(ys)), abs(max(zs) + min(zs))) * 0.5
            if off > 0.02:
                errors.append(f"{w.name} origin off axle by {off:.3f}")
        if not {"Lamp_Headlight", "Lamp_Taillight", "Lamp_Reverse"} <= mats:
            errors.append("lamp materials missing")
        if not any("Glass" in n for n in names):
            errors.append("no glass")
    if vehicle_id in CARS:
        for m in MARKERS:
            if f"Marker__{m}" not in names:
                errors.append(f"Marker__{m} missing")
        door = [o for o in objs if o.name.startswith("Door_Driver__Shell")]
        hinge = next((o for o in objs if o.name == "Marker__Hinge"), None)
        if not door or hinge is None:
            errors.append("driver door or hinge missing")
        elif (door[0].matrix_world.translation - hinge.data.vertices[0].co).length > 0.05:
            errors.append("door origin is not on the hinge")
        for group in ("Driver_Seat__", "Steering_Wheel__", "Door_Driver__Glass"):
            if not any(n.startswith(group) for n in names):
                errors.append(f"{group} missing")
    if vehicle_id == "police" and not {"Lamp_StrobeRed", "Lamp_StrobeBlue"} <= mats:
        errors.append("strobe lenses missing")
    if vehicle_id == "bus" and not any(n.startswith("Bus_Door") for n in names):
        errors.append("bus doors missing")
    if errors:
        print(f"REMAKE_VEHICLE_FAIL {vehicle_id} " + "; ".join(errors))
        return False
    print(f"REMAKE_VEHICLE_OK {vehicle_id} tris={tris} wheels={len(wheels)} materials={len(real)} meshes={len(objs)}")
    return True


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ["all"]
    ids = ALL if argv == ["all"] else argv
    results = [check(i) for i in ids]
    if not all(results):
        sys.exit(1)
    if argv == ["all"]:
        print(f"REMAKE_VEHICLE_ALL_OK count={len(results)}")


if __name__ == "__main__":
    main()
