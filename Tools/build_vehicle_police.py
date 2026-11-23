"""Remade police cruiser (reference remake/policecar.png).

Boxy black sedan with white doors and roof, push bar, plain hubcaps.
Personal detail: the light bar (red and blue strobe lenses).

Usage: blender -b -noaudio --python-exit-code 1 -P Tools/build_vehicle_police.py -- [--no-render]
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import vehicle_kit as vk  # noqa: E402

BELT = 1.12
ROOF = 1.74
SPEC = {
    "id": "police",
    "reference": "policecar.png",
    "paint": "1D1E21",
    "paint_finish": (0.1, 0.4),
    "two_tone": {"hex": "ECECEA", "y0": -0.78, "y1": 1.3, "z0": 0.42, "z1": 2.0},
    "belt": BELT,
    "body": [
        (-2.50, 0.92, 0.42, 0.92, 0.08, 0.08),
        (-2.36, 0.98, 0.34, 1.04, 0.1, 0.1),
        (-0.78, 0.98, 0.34, 1.1, 0.1, 0.08),
        (-0.75, 0.98, 0.34, BELT, 0.1, 0.06),
        (0.35, 0.98, 0.34, BELT, 0.1, 0.06),
        (1.30, 0.98, 0.34, BELT, 0.1, 0.06),
        (2.36, 0.98, 0.36, 1.1, 0.1, 0.1),
        (2.50, 0.92, 0.42, 1.02, 0.08, 0.08),
    ],
    "cabin": {"y0": -0.78, "y1": 1.3},
    "door": {"y0": -0.75, "y1": 0.35, "sill": 0.45},
    "greenhouse": {"roof": ROOF, "roof_t": 0.07, "hw_belt": 0.9, "hw_roof": 0.78,
                   "a_y": -0.75, "roof_front": -0.12, "roof_rear": 0.98, "c_y": 1.5,
                   "posts": [0.35], "pillar": 0.08, "roof_hex": "ECECEA", "dark_pillars": True},
    "seat": {"x": 0.43, "y": -0.22, "floor": 0.4, "rear": 0.82},
    "interior": {"floor": "1C1D20", "seat": "26282B", "accent": "1F3A6E", "dash": "141517",
                 "screen": "0A0D11", "wheel": "121314"},
    "wheels": {"front_y": -1.6, "rear_y": 1.6, "radius": 0.4, "width": 0.3, "x": 0.86,
               "style": "plain", "rim_frac": 0.66, "rim": "4A4C50", "hub": "B4B6BA", "segments": 10},
    "ref_view": (1.0, -1.2, 0.55),
}


def details(k):
    dark = k.dark
    ext = k.part("Exterior")
    ext.add(vk.span_box((-0.55, -2.53, 0.56), (0.55, -2.49, 0.86)), dark)
    # Push bar.
    for x in (-0.4, 0.4):
        ext.add(vk.beam((x, -2.62, 0.42), (x, -2.62, 0.92), 0.08), dark)
    ext.add(vk.beam((-0.48, -2.64, 0.62), (0.48, -2.64, 0.62), 0.1), dark)
    ext.add(vk.span_box((-0.98, -2.6, 0.36), (0.98, -2.48, 0.5), 0.03), dark)
    ext.add(vk.span_box((-0.98, 2.48, 0.38), (0.98, 2.6, 0.52), 0.03), dark)
    for side in (1, -1):
        ext.add(vk.span_box((min(side * 0.95, side * 1.14), -0.7, 1.16), (max(side * 0.95, side * 1.14), -0.58, 1.28), 0.02), dark)
        ext.add(vk.span_box((side * 0.98 - 0.02, 0.95, 0.98), (side * 0.98 + 0.03, 1.1, 1.02)), dark)
    k.part("Door_Driver__Handle__1A1B1F", vk.door_hinge(k)).add(
        vk.span_box((0.97, 0.12, 0.98), (1.01, 0.27, 1.02)))
    lamps = k.part("Lamps")
    for side in (1, -1):
        lamps.add(vk.span_box((min(side * 0.6, side * 0.88), -2.53, 0.7), (max(side * 0.6, side * 0.88), -2.48, 0.84), 0.01), k.head)
        lamps.add(vk.span_box((side * 0.74 - 0.18, 2.48, 0.78), (side * 0.74 + 0.18, 2.53, 0.94)), k.tail)
        lamps.add(vk.span_box((side * 0.42 - 0.07, 2.49, 0.8), (side * 0.42 + 0.07, 2.53, 0.9)), k.reverse)
    # Light bar (personal detail): blue on the driver side, red on the other.
    bar = k.part("Light_Bar")
    for y in (0.25, 0.55):
        bar.add(vk.span_box((-0.5, y - 0.04, ROOF), (0.5, y + 0.04, ROOF + 0.06)), dark)
    bar.add(vk.span_box((0.22, 0.2, ROOF + 0.06), (0.62, 0.6, ROOF + 0.2), 0.02), vk.material("Lamp_StrobeBlue", "2F5BD8", 0.0, 0.3))
    bar.add(vk.span_box((-0.22, 0.2, ROOF + 0.06), (0.22, 0.6, ROOF + 0.2), 0.02), vk.material("Lightbar_White", "F0F0F0", 0.0, 0.3))
    bar.add(vk.span_box((-0.62, 0.2, ROOF + 0.06), (-0.22, 0.6, ROOF + 0.2), 0.02), vk.material("Lamp_StrobeRed", "C8262A", 0.0, 0.3))


if __name__ == "__main__":
    vk.run(SPEC, details)
