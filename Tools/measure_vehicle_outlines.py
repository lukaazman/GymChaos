"""Orthographic measuring renders of the backed-up vehicle shells.

Renders each car from the driver side (+c), from the rear (+a) and from the
top in the car frame used by Tools/build_vehicle_cabins.py, and writes a JSON
sidecar with the pixel -> car-frame mapping. Tools/grid_vehicle_outlines.py
then draws a labelled grid so window outlines can be read in source units.

Usage:
  blender -b --factory-startup -P Tools/measure_vehicle_outlines.py -- [cbum ...]
"""
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_vehicle_cabins as cabins  # noqa: E402

OUT = cabins.EVIDENCE / "measure"
RES = 1400


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    names = argv or list(cabins.VEHICLES)
    OUT.mkdir(parents=True, exist_ok=True)
    for name in names:
        cfg = cabins.VEHICLES[name]
        body, wheels = cabins.import_asset(cabins.BACKUP / cfg["path"].name)
        frame = cabins.Frame(cfg["axis"])
        pts = [frame.local(body.matrix_world @ v.co) for v in body.data.vertices]
        lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
        hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
        scene = bpy.context.scene
        scene.render.engine = "BLENDER_EEVEE_NEXT"
        scene.render.resolution_x = RES
        scene.render.resolution_y = RES
        world = bpy.data.worlds.new("w")
        world.color = (0.8, 0.8, 0.8)
        scene.world = world
        for rot in ((math.radians(50), 0, math.radians(30)), (math.radians(-50), 0, math.radians(210))):
            sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
            sun.data.energy = 3.0
            sun.rotation_euler = rot
            scene.collection.objects.link(sun)
        cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
        scene.collection.objects.link(cam)
        scene.camera = cam
        cam.data.type = "ORTHO"
        center = (lo + hi) * 0.5
        span = max(hi.x - lo.x, hi.y - lo.y, hi.z - lo.z) * 1.08
        cam.data.ortho_scale = span
        cam.data.clip_end = 100
        meta = {"span": span, "res": RES, "lo": list(lo), "hi": list(hi)}
        # view: (camera position, up vector, horizontal car axis, vertical car axis)
        views = {
            "side": (frame.world(center.x, hi.y + 2, center.z), frame.Z, "a", "z"),
            "rear": (frame.world(hi.x + 2, center.y, center.z), frame.Z, "c", "z"),
            "top": (frame.world(center.x, center.y, hi.z + 2), frame.C, "a", "c"),
        }
        for view, (pos, up, haxis, vaxis) in views.items():
            cam.location = pos
            if view == "top":
                # Image right = +a, image up = -c (camera looks down).
                from mathutils import Matrix
                right = frame.A
                upv = -frame.C
                back = right.cross(upv)
                cam.matrix_world = Matrix((
                    (right.x, upv.x, back.x, pos.x),
                    (right.y, upv.y, back.y, pos.y),
                    (right.z, upv.z, back.z, pos.z),
                    (0, 0, 0, 1)))
            else:
                from mathutils import Matrix
                if view == "side":
                    right = frame.A    # nose (-a) on the left
                else:
                    right = -frame.C   # looking forward from behind: driver (+c) on the left
                upv = frame.Z
                back = right.cross(upv)
                cam.matrix_world = Matrix((
                    (right.x, upv.x, back.x, pos.x),
                    (right.y, upv.y, back.y, pos.y),
                    (right.z, upv.z, back.z, pos.z),
                    (0, 0, 0, 1)))
            h_center = {"a": center.x, "c": center.y}[haxis]
            v_center = {"z": center.z, "c": center.y}[vaxis]
            h_sign = -1 if (view == "rear") else 1
            v_sign = -1 if (view == "top") else 1
            meta[view] = {"haxis": haxis, "vaxis": vaxis, "h_center": h_center,
                          "v_center": v_center, "h_sign": h_sign, "v_sign": v_sign}
            scene.render.filepath = str(OUT / f"{name}_{view}.png")
            bpy.ops.render.render(write_still=True)
        (OUT / f"{name}.json").write_text(json.dumps(meta, indent=1))
        print(f"GYMCHAOS_VEHICLE_MEASURE_OK name={name} lo={tuple(round(v, 3) for v in lo)} "
              f"hi={tuple(round(v, 3) for v in hi)}")


if __name__ == "__main__":
    main()
