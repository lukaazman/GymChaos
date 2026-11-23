"""Davie's remade city bus (reference remake/davie_bus.png).

Teal low-floor bus, dark window band, two double folding doors on the curb
side (-X), seats and poles inside. Personal detail: LED route sign over the
windscreen. No driver cabin contract: the game never seats a driver here.

Usage: blender -b -noaudio --python-exit-code 1 -P Tools/build_vehicle_bus.py -- [--no-render]
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import vehicle_kit as vk  # noqa: E402

HALF = 5.2
BELT = 1.3
ROOF = 2.95
DOORS = [(-4.75, -3.85), (0.0, 0.9)]
SPEC = {
    "id": "bus",
    "reference": "davie_bus.png",
    "length": 2 * HALF,
    "paint": "1FA9AE",
    "paint_finish": (0.05, 0.45),
    "trim": "23262B",
    "belt": BELT,
    "body": [
        (-HALF, 1.2, 0.36, 1.25, 0.1, 0.06),
        (-HALF + 0.12, 1.25, 0.32, BELT, 0.08, 0.04),
        (HALF - 0.12, 1.25, 0.32, BELT, 0.08, 0.04),
        (HALF, 1.22, 0.36, BELT, 0.1, 0.04),
    ],
    "cabin": {"y0": -HALF + 0.1, "y1": HALF - 0.1},
    "greenhouse": {"roof": ROOF, "roof_t": 0.12, "hw_belt": 1.23, "hw_roof": 1.2,
                   "a_y": -HALF + 0.08, "roof_front": -HALF + 0.18, "roof_rear": HALF - 0.06, "c_y": HALF - 0.04,
                   "posts": [-3.85, -2.6, -1.3, 0.0, 0.9, 2.1, 3.3, 4.3], "pillar": 0.14, "dark_pillars": True},
    "wheels": {"front_y": -3.05, "rear_y": 2.95, "radius": 0.52, "width": 0.36, "x": 1.02,
               "style": "plain", "rim_frac": 0.62, "rim": "3A3C40", "hub": "9EA1A6", "segments": 10},
    "lamps": True,
    "normalize": True,
    "ref_view": (-1.0, -1.1, 0.45),
}


def details(k):
    dark, teal = k.trim, k.paint
    ext = k.part("Exterior")
    # Roof slab in body colour with the AC pod, dark front mask round the screen.
    ext.add(vk.span_box((-0.8, 0.5, ROOF + 0.04), (0.8, 2.6, ROOF + 0.26), 0.06), teal)
    ext.add(vk.span_box((-1.22, -HALF - 0.04, ROOF - 0.5), (1.22, -HALF + 0.02, ROOF - 0.05)), dark)
    # Mirrors on stalks.
    for side in (1, -1):
        ext.add(vk.beam((side * 1.2, -HALF + 0.1, 2.6), (side * 1.42, -HALF - 0.25, 2.55), 0.06), dark)
        ext.add(vk.span_box((side * 1.42 - 0.07, -HALF - 0.32, 2.0), (side * 1.42 + 0.07, -HALF - 0.18, 2.55), 0.02), dark)
    # Rear grille.
    for i in range(5):
        z = 0.7 + 0.08 * i
        ext.add(vk.span_box((-1.255, 3.9, z), (-1.24, 4.4, z + 0.04)), dark)
    # Folding doors on the curb side: dark frames, glass leaves, yellow grab rails.
    rail = k.mat("Rail", "D9A72B", 0.2, 0.4)
    glass = vk.material("Remake_Glass", "33414D", 0.0, 0.05, alpha=0.45)
    door = k.part("Bus_Door")
    for y0, y1 in DOORS:
        mid = (y0 + y1) * 0.5
        door.add(vk.span_box((-1.27, y0, 0.36), (-1.24, y1, 2.55)), dark)
        for a, b in ((y0 + 0.06, mid - 0.02), (mid + 0.02, y1 - 0.06)):
            door.add(vk.pane([(-1.275, a, 0.5), (-1.275, b, 0.5), (-1.275, b, 2.45), (-1.275, a, 2.45)][::-1], 0.012), glass)
        door.add(vk.beam((-1.2, mid - 0.15, 1.0), (-1.2, mid - 0.3, 1.5), 0.04), rail)
        door.add(vk.beam((-1.2, mid + 0.15, 1.0), (-1.2, mid + 0.3, 1.5), 0.04), rail)
        door.add(vk.span_box((-1.28, y0, 0.36), (-1.24, y1, 0.42)), k.chrome)
    # Interior: floor, rows of seats, poles, driver seat and wheel.
    k.part("Cabin__Floor__2B2E33").add(vk.span_box((-1.18, -HALF + 0.15, 0.36), (1.18, HALF - 0.15, 0.42)))
    seats = k.part("Cabin__Seat__2D4A6A")
    for y in (-2.5, -1.8, 1.6, 2.3, 3.0, 3.7, 4.4):
        for x in (-0.75, 0.75):
            if x < 0 and y < 1.0:
                continue  # standing room by the doors
            seats.add(vk.span_box((x - 0.36, y - 0.22, 0.85), (x + 0.36, y + 0.22, 0.95), 0.02))
            seats.add(vk.span_box((x - 0.36, y + 0.16, 0.95), (x + 0.36, y + 0.24, 1.55), 0.02))
            seats.add(vk.span_box((x - 0.15, y - 0.1, 0.42), (x + 0.15, y + 0.1, 0.85)))
    poles = k.part("Poles")
    for y in (-3.4, -1.0, 1.2, 3.3):
        poles.add(vk.beam((-0.3, y, 0.42), (-0.3, y, ROOF - 0.12), 0.04), rail)
    k.part("Cabin__Driver__1E2024").add(vk.span_box((0.45, -4.3, 0.85), (1.0, -3.9, 0.98), 0.02))
    k.part("Cabin__Driver__1E2024").add(vk.span_box((0.45, -3.95, 0.95), (1.0, -3.85, 1.7), 0.02))
    k.part("Cabin__Dash__1B1D22").add(vk.span_box((-1.15, -HALF + 0.1, 0.42), (1.15, -HALF + 0.5, 1.3), 0.03))
    k.part("Cabin__Wheel__141517").add(vk.torus((0.72, -4.7, 1.45), (0, 0.6, 0.8), 0.22, 0.025))
    # LED route sign (personal detail).
    sign = vk.material("Sign_Led", "F2A93B", 0.0, 0.4)
    ext.add(vk.span_box((-0.9, -HALF - 0.06, ROOF - 0.42), (0.9, -HALF - 0.04, ROOF - 0.14)), sign)
    lamps = k.part("Lamps")
    for side in (1, -1):
        lamps.add(vk.span_box((min(side * 0.85, side * 1.15), -HALF - 0.04, 0.62), (max(side * 0.85, side * 1.15), -HALF + 0.02, 0.74), 0.01), k.head)
        lamps.add(vk.span_box((min(side * 0.95, side * 1.18), HALF - 0.02, 0.9), (max(side * 0.95, side * 1.18), HALF + 0.04, 1.2)), k.tail)
        lamps.add(vk.span_box((min(side * 0.95, side * 1.18), HALF - 0.02, 0.74), (max(side * 0.95, side * 1.18), HALF + 0.04, 0.86)), k.reverse)


if __name__ == "__main__":
    vk.run(SPEC, details)
