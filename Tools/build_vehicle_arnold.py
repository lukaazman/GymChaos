"""Arnold's remade off-roader (reference remake/arnold_car.png).

Boxy red four-door with a short pickup bed, black roof, flared arches, big
off-road tyres, bull bar. Personal detail: front winch and a roof rack.

Usage: blender -b -noaudio --python-exit-code 1 -P Tools/build_vehicle_arnold.py -- [--no-render]
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import vehicle_kit as vk  # noqa: E402

BELT = 1.42
SPEC = {
    "id": "arnold",
    "reference": "arnold_car.png",
    "paint": "D9201E",
    "paint_finish": (0.1, 0.45),
    "trim": "1B1C1F",
    "belt": BELT,
    # y, half width, bottom, top, bottom chamfer, top chamfer
    "body": [
        (-2.50, 0.96, 0.62, 1.22, 0.06, 0.06),
        (-2.38, 1.00, 0.52, 1.34, 0.08, 0.1),
        (-1.00, 1.00, 0.50, 1.40, 0.08, 0.08),
        (-0.95, 1.00, 0.50, BELT, 0.08, 0.06),
        (0.35, 1.00, 0.50, BELT, 0.08, 0.06),
        (1.00, 1.00, 0.50, BELT, 0.08, 0.06),
        (2.40, 1.00, 0.52, BELT, 0.08, 0.06),
        (2.50, 0.96, 0.60, 1.36, 0.06, 0.06),
    ],
    "cabin": {"y0": -0.98, "y1": 1.0},
    "door": {"y0": -0.95, "y1": 0.35, "sill": 0.52},
    "greenhouse": {"belt": BELT, "roof": 2.08, "roof_t": 0.08, "hw_belt": 0.98, "hw_roof": 0.92,
                   "a_y": -0.95, "roof_front": -0.7, "roof_rear": 1.0, "c_y": 1.05,
                   "posts": [0.35], "pillar": 0.1, "roof_hex": "1E1F22"},
    "seat": {"x": 0.45, "y": -0.22, "floor": 0.62, "rear": 0.58},
    "underbody": {"hw": 0.9, "front": 1.3, "rear": 0.35, "bottom": 0.4, "top": 0.1},
    "interior": {"floor": "24251D", "seat": "7A6A4F", "accent": "5C5240", "dash": "2C3024",
                 "screen": "0A0D11", "wheel": "161712"},
    "wheels": {"front_y": -1.62, "rear_y": 1.62, "radius": 0.56, "width": 0.44, "x": 0.94,
               "style": "holes8", "rim_frac": 0.52, "rim": "D5D7DB", "hub": "B9BCC2", "segments": 14, "lugs": True, "arch_gap": 0.06},
    "ref_view": (1.0, -1.15, 0.5),
}


def details(k):
    dark, red = k.dark, k.paint
    body = k.part("Exterior")
    # Fender flares over every arch.
    for y in (-1.62, 1.62):
        for side in (1, -1):
            x0 = side * 0.98
            body.add(vk.span_box((min(x0, x0 + side * 0.14), y - 0.74, 1.12), (max(x0, x0 + side * 0.14), y + 0.74, 1.24), 0.03), red)
            for dy in (-0.72, 0.72):
                body.add(vk.beam((x0 + side * 0.07, y + dy, 1.16), (x0 + side * 0.07, y + dy * 0.98, 0.62), 0.12), red)
    # Hood vent, grille and badge.
    body.add(vk.span_box((-0.42, -2.1, 1.4), (0.42, -1.4, 1.44), 0.01), dark)
    body.add(vk.span_box((-0.5, -2.53, 0.72), (0.5, -2.49, 1.18)), dark)
    for i in range(7):
        x = -0.36 + 0.12 * i
        body.add(vk.span_box((x - 0.035, -2.545, 0.78), (x + 0.035, -2.52, 1.14)), red)
    body.add(vk.box((0, -2.56, 1.08), (0.16, 0.02, 0.06), 0.01), k.chrome)
    # Bed walls, dark bed floor and tailgate.
    body.add(vk.span_box((-0.94, 1.12, BELT - 0.02), (0.94, 2.46, BELT + 0.0)), dark)
    # Front and rear bumpers, step bars.
    body.add(vk.span_box((-1.0, -2.68, 0.5), (1.0, -2.5, 0.68), 0.03), dark)
    body.add(vk.span_box((-1.0, 2.5, 0.52), (1.0, 2.66, 0.7), 0.03), dark)
    for side in (1, -1):
        body.add(vk.span_box((min(side * 0.98, side * 1.12), -0.9, 0.42), (max(side * 0.98, side * 1.12), 0.95, 0.5)), dark)
        # Mirrors.
        body.add(vk.span_box((min(side * 1.0, side * 1.22), -0.92, 1.5), (max(side * 1.0, side * 1.22), -0.82, 1.7), 0.02), dark)
        # Rear door handle (the front one is on the driver door).
        body.add(vk.span_box((side * 1.0 - 0.02, 0.6, 1.24), (side * 1.0 + 0.03, 0.75, 1.29)), dark)
    k.part("Door_Driver__Handle__1A1B1F", vk.door_hinge(k)).add(
        vk.span_box((0.98, -0.42, 1.24), (1.03, -0.27, 1.29)))
    # Bull bar with round headlights inside it, and the winch (personal detail).
    bar = k.part("Bull_Bar")
    for side in (1, -1):
        bar.add(vk.beam((side * 0.95, -2.7, 0.62), (side * 0.95, -2.7, 1.24), 0.07), dark)
        bar.add(vk.beam((side * 0.3, -2.7, 0.62), (side * 0.3, -2.7, 1.24), 0.07), dark)
    bar.add(vk.beam((-0.98, -2.7, 1.24), (0.98, -2.7, 1.24), 0.07), dark)
    bar.add(vk.beam((-0.98, -2.7, 0.9), (0.98, -2.7, 0.9), 0.06), dark)
    bar.add(vk.cylinder((0, -2.76, 0.6), (1, 0, 0), 0.09, 0.5, 8), dark)
    bar.add(vk.span_box((-0.06, -2.86, 0.5), (0.06, -2.8, 0.6)), k.chrome)
    lamps = k.part("Lamps")
    for side in (1, -1):
        lamps.add(vk.cylinder((side * 0.64, -2.53, 1.02), (0, 1, 0), 0.13, 0.06, 8), k.head)
        lamps.add(vk.span_box((side * 0.82 - 0.06, -2.53, 1.08), (side * 0.82 + 0.06, -2.49, 1.16)), k.indicator)
        lamps.add(vk.span_box((side * 0.82 - 0.1, 2.49, 1.08), (side * 0.82 + 0.1, 2.53, 1.3)), k.tail)
        lamps.add(vk.span_box((side * 0.82 - 0.1, 2.49, 0.9), (side * 0.82 + 0.1, 2.53, 0.98)), k.reverse)
    # Roof rack (personal detail).
    rack = k.part("Roof_Rack")
    for x in (-0.82, 0.82):
        rack.add(vk.beam((x, -0.5, 2.16), (x, 0.9, 2.16), 0.05), dark)
    for y in (-0.45, 0.0, 0.45, 0.85):
        rack.add(vk.beam((-0.84, y, 2.16), (0.84, y, 2.16), 0.04), dark)
        for x in (-0.82, 0.82):
            rack.add(vk.beam((x, y, 2.08), (x, y, 2.16), 0.04), dark)


if __name__ == "__main__":
    vk.run(SPEC, details)
