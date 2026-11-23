"""CBum's remade muscle coupe (reference remake/cbum_car.png).

Low black two-door with a long hood, fastback roof, chrome bumper and grille
frame with round lamps. Personal detail: twin racing stripes.

Usage: blender -b -noaudio --python-exit-code 1 -P Tools/build_vehicle_cbum.py -- [--no-render]
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import vehicle_kit as vk  # noqa: E402

BELT = 1.08
ROOF = 1.62
SPEC = {
    "id": "cbum",
    "reference": "cbum_car.png",
    "paint": "1C1D20",
    "paint_finish": (0.2, 0.32),
    "belt": BELT,
    "body": [
        (-2.50, 0.92, 0.38, 0.86, 0.06, 0.08),
        (-2.38, 0.97, 0.30, 0.96, 0.08, 0.1),
        (-0.76, 0.98, 0.30, 1.06, 0.08, 0.1),
        (-0.70, 0.98, 0.30, BELT, 0.08, 0.08),
        (0.55, 0.98, 0.30, BELT, 0.08, 0.08),
        (1.10, 0.98, 0.30, BELT, 0.08, 0.08),
        (2.30, 0.97, 0.34, 1.08, 0.08, 0.1),
        (2.50, 0.92, 0.42, 1.04, 0.06, 0.08),
    ],
    "cabin": {"y0": -0.73, "y1": 1.1},
    "door": {"y0": -0.70, "y1": 0.55, "sill": 0.4},
    "greenhouse": {"roof": ROOF, "roof_t": 0.06, "hw_belt": 0.9, "hw_roof": 0.74,
                   "a_y": -0.70, "roof_front": -0.05, "roof_rear": 0.85, "c_y": 1.62,
                   "posts": [0.55], "pillar": 0.07},
    "seat": {"x": 0.42, "y": -0.05, "floor": 0.38, "rear": 0.75},
    "underbody": {"hw": 0.9, "front": 1.15, "rear": 0.4, "bottom": 0.05},
    "interior": {"floor": "121214", "seat": "1E1E21", "accent": "A3141C", "dash": "141416",
                 "screen": "0A0D11", "wheel": "121213"},
    "wheels": {"front_y": -1.62, "rear_y": 1.6, "radius": 0.4, "width": 0.32, "x": 0.86,
               "style": "spoke5", "rim": "A9ACB2", "hub": "8E9196", "segments": 14},
    "ref_view": (1.0, -1.25, 0.45),
}


def details(k):
    dark, chrome = k.dark, k.chrome
    ext = k.part("Exterior")
    # Grille in a chrome frame with round lamps at its ends, chrome bumpers.
    ext.add(vk.span_box((-0.78, -2.53, 0.54), (0.78, -2.49, 0.84)), dark)
    for z in (0.53, 0.85):
        ext.add(vk.beam((-0.8, -2.54, z), (0.8, -2.54, z), 0.035), chrome)
    for x in (-0.8, 0.8):
        ext.add(vk.beam((x, -2.54, 0.53), (x, -2.54, 0.85), 0.035), chrome)
    for z in (0.62, 0.7, 0.78):
        ext.add(vk.beam((-0.5, -2.545, z), (0.5, -2.545, z), 0.018), chrome)
    ext.add(vk.beam((0, -2.55, 0.56), (0, -2.55, 0.82), 0.02), chrome)
    ext.add(vk.span_box((-0.98, -2.64, 0.4), (0.98, -2.5, 0.5), 0.03), chrome)
    ext.add(vk.span_box((-0.96, 2.5, 0.42), (0.96, 2.62, 0.52), 0.03), chrome)
    ext.add(vk.span_box((-0.7, -2.6, 0.3), (0.7, -2.42, 0.36)), dark)  # front lip
    # Ducktail spoiler lip on the trunk.
    ext.add(vk.span_box((-0.9, 2.3, 1.08), (0.9, 2.5, 1.13), 0.02), k.paint)
    # Rear quarter vents, mirrors, rear-side badge.
    for side in (1, -1):
        for i in range(3):
            y = 1.25 + 0.07 * i
            ext.add(vk.span_box((side * 0.98 - 0.01, y, 0.7), (side * 0.98 + 0.01, y + 0.03, 0.86)), chrome)
        ext.add(vk.span_box((min(side * 0.96, side * 1.12), -0.6, 1.12), (max(side * 0.96, side * 1.12), -0.5, 1.2), 0.02), chrome)
    ext.add(vk.box((0, -2.55, 0.7), (0.14, 0.02, 0.05), 0.01), chrome)
    k.part("Door_Driver__Handle__C9CED6", vk.door_hinge(k)).add(
        vk.span_box((0.97, 0.3, 0.96), (1.0, 0.44, 0.99)))
    lamps = k.part("Lamps")
    for side in (1, -1):
        lamps.add(vk.cylinder((side * 0.66, -2.53, 0.69), (0, 1, 0), 0.11, 0.06, 8), k.head)
        lamps.add(vk.span_box((side * 0.72 - 0.2, 2.5, 0.74), (side * 0.72 + 0.2, 2.54, 0.88)), k.tail)
        lamps.add(vk.span_box((side * 0.28 - 0.06, 2.5, 0.76), (side * 0.28 + 0.06, 2.54, 0.86)), k.reverse)
    # Twin racing stripes over hood, roof and trunk (personal detail).
    stripe = k.mat("Stripe", "E6E6E8", 0.0, 0.45)
    s = k.part("Stripes")
    for x in (-0.18, 0.18):
        w = 0.11
        s.add(vk.pane([(x - w, -2.4, 0.975), (x + w, -2.4, 0.975), (x + w, -0.78, 1.07), (x - w, -0.78, 1.07)], 0.01), stripe)
        s.add(vk.pane([(x - w, -0.05, ROOF + 0.005), (x + w, -0.05, ROOF + 0.005), (x + w, 0.85, ROOF + 0.005),
                       (x - w, 0.85, ROOF + 0.005)], 0.01), stripe)
        s.add(vk.pane([(x - w, 1.66, BELT + 0.005), (x + w, 1.66, BELT + 0.005), (x + w, 2.28, 1.085),
                       (x - w, 2.28, 1.085)], 0.01), stripe)


if __name__ == "__main__":
    vk.run(SPEC, details)
