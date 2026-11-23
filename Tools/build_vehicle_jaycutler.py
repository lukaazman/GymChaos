"""Jay's remade luxury saloon (reference remake/jay_cutler_car.png).

Black four-door with a big chrome grille, chrome window line and lower side
trim. Personal detail: chrome trim and a front and rear plate.

Usage: blender -b -noaudio --python-exit-code 1 -P Tools/build_vehicle_jaycutler.py -- [--no-render]
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import vehicle_kit as vk  # noqa: E402

BELT = 1.12
ROOF = 1.7
SPEC = {
    "id": "jaycutler",
    "reference": "jay_cutler_car.png",
    "paint": "17181B",
    "paint_finish": (0.25, 0.28),
    "belt": BELT,
    "body": [
        (-2.50, 0.9, 0.42, 0.9, 0.08, 0.1),
        (-2.35, 0.97, 0.34, 1.02, 0.1, 0.1),
        (-0.88, 0.98, 0.34, 1.11, 0.1, 0.1),
        (-0.85, 0.98, 0.34, BELT, 0.1, 0.08),
        (0.35, 0.98, 0.34, BELT, 0.1, 0.08),
        (1.25, 0.98, 0.34, BELT, 0.1, 0.08),
        (2.30, 0.96, 0.36, 1.1, 0.1, 0.1),
        (2.50, 0.9, 0.44, 1.02, 0.08, 0.1),
    ],
    "cabin": {"y0": -0.88, "y1": 1.25},
    "door": {"y0": -0.85, "y1": 0.35, "sill": 0.45},
    "greenhouse": {"roof": ROOF, "roof_t": 0.06, "hw_belt": 0.9, "hw_roof": 0.76,
                   "a_y": -0.85, "roof_front": -0.15, "roof_rear": 0.95, "c_y": 1.62,
                   "posts": [0.35], "pillar": 0.07},
    "seat": {"x": 0.43, "y": -0.12, "floor": 0.4, "rear": 0.78},
    "underbody": {"hw": 0.9, "front": 1.15, "rear": 0.4, "bottom": 0.06},
    "interior": {"floor": "3A2F26", "seat": "D6C3A1", "accent": "B59F7E", "dash": "2A1C14",
                 "screen": "0A0D11", "wheel": "1A1410"},
    "wheels": {"front_y": -1.6, "rear_y": 1.6, "radius": 0.41, "width": 0.3, "x": 0.86,
               "style": "spoke5", "rim": "B5B8BE", "hub": "8E9196", "segments": 14},
    "ref_view": (1.0, -1.3, 0.45),
}


def details(k):
    dark, chrome = k.dark, k.chrome
    ext = k.part("Exterior")
    # Big grille: chrome frame, three slats, centre bar; invented round badge.
    ext.add(vk.span_box((-0.42, -2.53, 0.5), (0.42, -2.49, 0.88)), dark)
    for z in (0.49, 0.89):
        ext.add(vk.beam((-0.44, -2.54, z), (0.44, -2.54, z), 0.04), chrome)
    for x in (-0.44, 0.44):
        ext.add(vk.beam((x, -2.54, 0.49), (x, -2.54, 0.89), 0.04), chrome)
    for z in (0.6, 0.69, 0.78):
        ext.add(vk.beam((-0.42, -2.545, z), (0.42, -2.545, z), 0.025), chrome)
    ext.add(vk.beam((0, -2.55, 0.5), (0, -2.55, 0.88), 0.03), chrome)
    ext.add(vk.cylinder((0, -2.57, 0.69), (0, 1, 0), 0.07, 0.02, 10), chrome)
    # Lower intakes and chrome lip, rear bumper.
    ext.add(vk.span_box((-0.9, -2.56, 0.36), (0.9, -2.44, 0.44), 0.03), dark)
    ext.add(vk.beam((-0.75, -2.57, 0.45), (0.75, -2.57, 0.45), 0.02), chrome)
    ext.add(vk.span_box((-0.94, 2.44, 0.4), (0.94, 2.56, 0.52), 0.03), dark)
    ext.add(vk.beam((-0.7, 2.57, 0.55), (0.7, 2.57, 0.55), 0.02), chrome)
    # Chrome window line and lower side trim (personal detail), mirrors, handles.
    g = k.spec["greenhouse"]
    for side in (1, -1):
        x = side * (g["hw_belt"] + 0.01)
        ext.add(vk.beam((x, -0.85, BELT + 0.01), (x, 1.62, BELT + 0.01), 0.025), chrome)
        ext.add(vk.beam((side * 0.98, -1.1, 0.46), (side * 0.98, 1.1, 0.46), 0.025), chrome)
        ext.add(vk.span_box((min(side * 0.95, side * 1.14), -0.75, 1.16), (max(side * 0.95, side * 1.14), -0.62, 1.24), 0.02), dark)
        ext.add(vk.span_box((side * 0.98 - 0.015, 0.95, 0.98), (side * 0.98 + 0.015, 1.1, 1.01)), chrome)
    k.part("Door_Driver__Handle__C9CED6", vk.door_hinge(k)).add(
        vk.span_box((0.965, 0.12, 0.98), (0.995, 0.27, 1.01)))
    # Front and rear plates.
    plate = k.mat("Plate", "F0F0EA", 0.0, 0.5)
    ext.add(vk.span_box((-0.26, -2.6, 0.37), (0.26, -2.57, 0.47)), plate)
    ext.add(vk.span_box((-0.26, 2.5, 0.62), (0.26, 2.53, 0.74)), plate)
    for i in range(4):
        x = -0.17 + 0.11 * i
        ext.add(vk.span_box((x - 0.035, 2.53, 0.65), (x + 0.035, 2.535, 0.71)), dark)
    lamps = k.part("Lamps")
    for side in (1, -1):
        lamps.add(vk.span_box((min(side * 0.5, side * 0.86), -2.54, 0.8), (max(side * 0.5, side * 0.86), -2.47, 0.9), 0.02), k.head)
        lamps.add(vk.span_box((side * 0.76 - 0.18, 2.48, 0.86), (side * 0.76 + 0.18, 2.52, 0.98)), k.tail)
        lamps.add(vk.span_box((side * 0.48 - 0.07, 2.49, 0.86), (side * 0.48 + 0.07, 2.53, 0.94)), k.reverse)


if __name__ == "__main__":
    vk.run(SPEC, details)
