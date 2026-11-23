"""Zyzz's remade full-size SUV (reference remake/zyzz_car.png).

Tall white wagon with dark lower cladding and bumpers, framed glass, plain
hubcaps. Personal detail: roof rails and a sticker on the rear side glass.

Usage: blender -b -noaudio --python-exit-code 1 -P Tools/build_vehicle_zyzz.py -- [--no-render]
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import vehicle_kit as vk  # noqa: E402

BELT = 1.36
ROOF = 2.12
SPEC = {
    "id": "zyzz",
    "reference": "zyzz_car.png",
    "paint": "E9E7E2",
    "paint_finish": (0.05, 0.45),
    "belt": BELT,
    "body": [
        (-2.50, 0.94, 0.4, 1.12, 0.08, 0.08),
        (-2.36, 1.0, 0.36, 1.22, 0.1, 0.12),
        (-0.95, 1.0, 0.36, 1.32, 0.1, 0.1),
        (-0.90, 1.0, 0.36, BELT, 0.1, 0.06),
        (0.25, 1.0, 0.36, BELT, 0.1, 0.06),
        (2.30, 1.0, 0.36, BELT, 0.1, 0.06),
        (2.50, 0.96, 0.4, 1.3, 0.08, 0.06),
    ],
    "cabin": {"y0": -0.93, "y1": 2.3},
    "door": {"y0": -0.90, "y1": 0.25, "sill": 0.5},
    "greenhouse": {"roof": ROOF, "roof_t": 0.07, "hw_belt": 0.96, "hw_roof": 0.9,
                   "a_y": -0.9, "roof_front": -0.35, "roof_rear": 2.38, "c_y": 2.42,
                   "posts": [0.25, 1.15], "pillar": 0.09, "dark_pillars": True},
    "seat": {"x": 0.45, "y": -0.2, "floor": 0.48, "rear": 0.7},
    "interior": {"floor": "26282B", "seat": "34373C", "accent": "5A5F66", "dash": "1B1D20",
                 "screen": "0A0D11", "wheel": "141517"},
    "wheels": {"front_y": -1.62, "rear_y": 1.62, "radius": 0.44, "width": 0.32, "x": 0.88,
               "style": "plain", "rim_frac": 0.7, "rim": "5A5C60", "hub": "A8AAAE", "segments": 10},
    "ref_view": (1.0, -1.15, 0.5),
}


def details(k):
    dark = k.dark
    clad = k.mat("Clad", "2E3033", 0.0, 0.75)
    ext = k.part("Exterior")
    # Grille in two bars with an invented diamond badge; bumpers.
    ext.add(vk.span_box((-0.62, -2.53, 0.78), (0.62, -2.49, 1.08)), dark)
    ext.add(vk.beam((-0.62, -2.54, 0.93), (0.62, -2.54, 0.93), 0.05), clad)
    ext.add(vk.box((0, -2.56, 0.93), (0.12, 0.02, 0.12), 0.0,
                   vk.Matrix.Rotation(vk.math.radians(45), 3, "Y")), k.chrome)
    ext.add(vk.span_box((-1.0, -2.64, 0.36), (1.0, -2.46, 0.72), 0.04), clad)
    ext.add(vk.span_box((-1.0, 2.46, 0.38), (1.0, 2.62, 0.7), 0.04), clad)
    for side in (1, -1):
        ext.add(vk.span_box((min(side * 0.98, side * 1.18), -0.86, 1.4), (max(side * 0.98, side * 1.18), -0.74, 1.56), 0.02), dark)
        ext.add(vk.span_box((side * 1.0 - 0.02, 0.85, 1.18), (side * 1.0 + 0.03, 1.0, 1.23)), dark)
        # Roof rails (personal detail).
        ext.add(vk.beam((side * 0.72, -0.2, ROOF + 0.06), (side * 0.72, 2.2, ROOF + 0.06), 0.06), dark)
        for y in (-0.2, 2.2):
            ext.add(vk.beam((side * 0.72, y, ROOF - 0.02), (side * 0.72, y, ROOF + 0.06), 0.06), dark)
    # Dark lower cladding between the arches; the driver door carries its own piece.
    for side in (1, -1):
        for y0, y1 in ((-2.48, -2.13), (-1.11, -0.9), (-0.9, 0.25), (0.25, 1.11), (2.13, 2.48)):
            piece = vk.span_box((min(side * 0.99, side * 1.025), y0, 0.36), (max(side * 0.99, side * 1.025), y1, 0.72))
            if side > 0 and y0 == -0.9:
                k.part("Door_Driver__Clad__2E3033", vk.door_hinge(k)).add(piece)
            else:
                ext.add(piece, clad)
    # Sticker on the passenger rear quarter glass: a small teal lightning bolt.
    sticker = k.mat("Sticker", "3FB7C2", 0.0, 0.5)
    ext.add(vk.pane([(-0.975, 1.7, 1.55), (-0.975, 1.85, 1.75), (-0.975, 1.82, 1.62),
                     (-0.975, 1.95, 1.66), (-0.975, 1.8, 1.45), (-0.975, 1.83, 1.58)], 0.01), sticker)
    k.part("Door_Driver__Handle__1A1B1F", vk.door_hinge(k)).add(
        vk.span_box((0.98, -0.05, 1.18), (1.03, 0.1, 1.23)))
    lamps = k.part("Lamps")
    for side in (1, -1):
        lamps.add(vk.span_box((min(side * 0.64, side * 0.92), -2.53, 0.96), (max(side * 0.64, side * 0.92), -2.47, 1.08)), k.head)
        lamps.add(vk.span_box((min(side * 0.64, side * 0.92), -2.53, 0.82), (max(side * 0.64, side * 0.92), -2.47, 0.92)), k.indicator)
        lamps.add(vk.span_box((side * 0.92 - 0.06, 2.49, 1.0), (side * 0.92 + 0.06, 2.55, 1.6)), k.tail)
        lamps.add(vk.span_box((side * 0.92 - 0.06, 2.49, 0.86), (side * 0.92 + 0.06, 2.55, 0.98)), k.reverse)


if __name__ == "__main__":
    vk.run(SPEC, details)
