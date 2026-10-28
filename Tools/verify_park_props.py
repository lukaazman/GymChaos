"""Check the park bench and lamp GLBs from Tools/build_park_props.py.

blender -b --factory-startup -P Tools/verify_park_props.py

Re-imports each GLB and checks its size against the frame GymOutdoorFoliage,
GymParkAccess and the sitting code rely on, its material names and its
triangle budget. Prints GYMCHAOS_PARK_PROPS_VERIFY_OK or exits 1.
"""
import os
import sys

import bpy
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
DIR = os.path.join(ROOT, "GymChaos", "Assets", "StreamingAssets", "BodyBuilders", "outside", "foliage")
errors = []


def load(name):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=os.path.join(DIR, name))
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    tris = 0
    mats = set()
    for obj in meshes:
        for v in obj.data.vertices:
            w = obj.matrix_world @ v.co
            lo = Vector(map(min, lo, w))
            hi = Vector(map(max, hi, w))
        tris += sum(len(p.vertices) - 2 for p in obj.data.polygons)
        mats.update(m.name for m in obj.data.materials if m)
    return lo, hi, tris, mats


def near(label, value, want, tol):
    if abs(value - want) > tol:
        errors.append(f"{label}={value:.3f} want {want}+-{tol}")


lo, hi, tris, mats = load("park_bench.glb")
size = hi - lo
print(f"bench size={size.x:.3f}x{size.y:.3f}x{size.z:.3f} tris={tris} mats={sorted(mats)}")
near("bench width", size.x, 1.74, 0.08)
near("bench depth incl. backrest", size.y, 0.56, 0.08)
near("bench height", hi.z, 0.86, 0.04)
near("bench floor", lo.z, 0.0, 0.01)
near("bench centre x", (lo.x + hi.x) * 0.5, 0.0, 0.01)
if not {"Park bench wood", "Park cast iron"} <= mats:
    errors.append(f"bench materials {sorted(mats)}")
if not 200 <= tris <= 6000:
    errors.append(f"bench tris {tris}")

# Seat top: highest wood vertex in front of the backrest.
seat_top = max((o.matrix_world @ o.data.vertices[i].co).z
               for o in bpy.context.scene.objects if o.type == "MESH"
               for p in o.data.polygons if o.data.materials[p.material_index].name == "Park bench wood"
               for i in p.vertices if (o.matrix_world @ o.data.vertices[i].co).y < 0.15)
near("bench seat top", seat_top, 0.49, 0.03)

lo, hi, tris, mats = load("park_lamp.glb")
size = hi - lo
print(f"lamp size={size.x:.3f}x{size.y:.3f}x{size.z:.3f} tris={tris} mats={sorted(mats)}")
near("lamp height", hi.z, 3.98, 0.05)
near("lamp floor", lo.z, 0.0, 0.01)
if size.x > 0.45 or size.y > 0.45:
    errors.append(f"lamp footprint {size.x:.2f}x{size.y:.2f}")
if not {"Park lamp glow", "Park cast iron"} <= mats:
    errors.append(f"lamp materials {sorted(mats)}")
if not 100 <= tris <= 4000:
    errors.append(f"lamp tris {tris}")

if errors:
    print("GYMCHAOS_PARK_PROPS_VERIFY_FAIL " + "; ".join(errors))
    sys.exit(1)
print("GYMCHAOS_PARK_PROPS_VERIFY_OK")
