"""Build a remade low-poly character from an MPFB start.

    blender -b -noaudio --python-exit-code 1 -P Tools/remake_character.py -- arnold

Writes Assets/BodyBuilders/remake/<name>.blend and textures/<name>.png. The
result follows .unlazy/assets-202611/ART-CHARACTERS.md; check it with
Tools/verify_remake_asset.py and Tools/render_remake_sheet.py.
"""

from __future__ import annotations

import argparse
import importlib
import math
import sys
from pathlib import Path

import addon_utils
import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import remake_rig  # noqa: E402

PROJECT_ROOT = Path(__file__).resolve().parents[1]
OUT_DIR = PROJECT_ROOT / "Assets/BodyBuilders/remake"
TEXTURE_SIZE = 1024
HEAD_BONE = "DEF-spine.005"

# Flat albedo (sRGB) per part. Detail comes from the baked AO, not paint.
RECIPES = {
    "arnold": {
        "macros": {"gender": 1.0, "age": 0.45, "muscle": 1.0, "weight": 0.6,
                   "proportions": 1.0, "height": 0.72},
        # MPFB detail targets (lr- = both sides; values above 1 overdrive the
        # target). Prime-era mass: huge chest and arms, wide lats, small waist,
        # heavy quads and calves.
        "targets": {"nose-scale-horiz-incr": 0.3, "torso-vshape-incr": 1.2, "torso-muscle-dorsi-incr": 1.4,
                    "torso-muscle-pectoral-incr": 2.0, "measure-shoulder-dist-incr": 0.7,
                    "measure-bust-circ-incr": 0.6, "torso-scale-depth-incr": 0.25,
                    "measure-waist-circ-decr": 1.0, "measure-neck-circ-incr": 0.9,
                    "lr-upperarm-muscle-incr": 1.8, "lr-upperarm-shoulder-muscle-incr": 1.5,
                    "lr-upperarm-scale-horiz-incr": 0.6, "lr-upperarm-scale-depth-incr": 0.6,
                    "measure-upperarm-circ-incr": 0.8, "lr-lowerarm-muscle-incr": 1.2,
                    "lr-upperleg-muscle-incr": 1.0, "lr-upperleg-scale-horiz-incr": 0.25,
                    "measure-thigh-circ-incr": 0.3, "lr-lowerleg-muscle-incr": 1.1,
                    "measure-calf-circ-incr": 0.6, "stomach-pregnant-decr": 1.0,
                    # Face after the reference: square jaw, wide strong chin
                    # with a cleft, high cheekbones, heavy brow, long nose.
                    "head-square": 1.0, "chin-width-incr": 1.0, "chin-prominent-incr": 0.6,
                    "chin-bones-incr": 0.7, "chin-cleft-incr": 0.5, "lr-cheek-bones-incr": 0.8,
                    "eyebrows-trans-forward": 0.8, "forehead-temple-decr": 0.3,
                    "nose-scale-vert-incr": 0.35, "nose-width1-incr": 0.3, "nose-hump-incr": 0.25,
                    "mouth-scale-horiz-incr": 0.5, "mouth-upperlip-volume-decr": 0.3},
        "body_tris": 4300,
        "skin": (0.80, 0.55, 0.40),
        "face": {"brows": (0.22, 0.14, 0.09), "lips": (0.66, 0.40, 0.33), "iris": (0.33, 0.42, 0.42)},
        # Cut from the skin itself (skin_band) so clothing deforms with it in
        # every pose. Bands: low/high as (landmark, offset in metres) where the
        # landmark is a bone height; "hem" places a cut between hip and knee.
        "parts": {
            # Loose 70s gym shorts: waist below the navel, hem high on the
            # thigh and hanging free of it.
            "shorts": {"source": "skin_band", "color": (0.16, 0.22, 0.48),
                       "low": ("hem", 0.56), "high": ("navel", -0.02),
                       "drape": 6, "push": 0.012, "flare": 0.012, "skin_under": True, "tris": 700},
            # 70s canvas trainers: white upper, gum sole.
            "shoes": {"source": "skin_band", "color": (0.86, 0.85, 0.81),
                      "low": ("floor", -1.0), "high": ("ankle", 0.035), "push": 0.014,
                      "hull": True, "flat_bottom": True,
                      "sole": (0.022, (0.55, 0.38, 0.22)), "tris": 500},
            "socks": {"source": "skin_band", "color": (0.92, 0.92, 0.90),
                      "low": ("ankle", 0.035), "high": ("ankle", 0.12), "push": 0.004, "tris": 240},
            # Thick wavy 70s hair: side part, volume on top, over the ears,
            # down to the nape.
            "hair": {"source": "region", "region": "scalp", "color": (0.30, 0.19, 0.11), "front": 0.055,
                     "temple": 0.03, "sides": -0.005, "behind": 0.11, "back": -0.09,
                     "push": 0.015, "volume": 0.028,
                     "wave": 0.011, "part_x": 0.035, "ramp": 0.035, "tris": 900},
        },
    },
    # Classic physique: X-frame, tiny waist, full beard, sleeve tattoos, black
    # tank and shorts, black cap.
    "cbum": {
        "macros": {"gender": 1.0, "age": 0.5, "muscle": 1.0, "weight": 0.5,
                   "proportions": 1.0, "height": 0.76},
        "targets": {"chin-prominent-incr": 0.3, "torso-vshape-incr": 1.7, "torso-muscle-dorsi-incr": 1.4,
                    "torso-muscle-pectoral-incr": 1.2, "measure-shoulder-dist-incr": 1.3,
                    "measure-waist-circ-decr": 1.8, "measure-neck-circ-incr": 0.8,
                    "lr-upperarm-muscle-incr": 1.4, "lr-upperarm-shoulder-muscle-incr": 1.5,
                    "lr-upperarm-scale-horiz-incr": 0.5, "lr-upperarm-scale-depth-incr": 0.5,
                    "measure-upperarm-circ-incr": 0.6, "lr-lowerarm-muscle-incr": 1.1,
                    "lr-upperleg-muscle-incr": 1.4, "lr-upperleg-scale-horiz-incr": 0.4,
                    "measure-thigh-circ-incr": 0.5, "lr-lowerleg-muscle-incr": 1.4,
                    "measure-calf-circ-incr": 0.6, "stomach-pregnant-decr": 0.6,
                    # Long rectangular face, deep-set eyes, straight long nose.
                    "head-rectangular": 0.8, "head-scale-vert-incr": 0.3, "chin-width-incr": 0.6,
                    "chin-bones-incr": 0.6, "chin-height-incr": 0.3, "lr-cheek-bones-incr": 0.4,
                    "eyebrows-trans-down": 0.4, "forehead-scale-vert-decr": 0.3,
                    "nose-scale-vert-incr": 0.4, "nose-width1-decr": 0.3, "nose-point-width-decr": 0.3,
                    "mouth-scale-horiz-decr": 0.2},
        "body_tris": 3600,
        "skin": (0.84, 0.63, 0.50),
        "face": {"brows": (0.16, 0.11, 0.08), "lips": (0.70, 0.45, 0.40), "iris": (0.20, 0.13, 0.08)},
        "parts": {
            "shorts": {"source": "skin_band", "color": (0.07, 0.07, 0.08),
                       "low": ("hem", 0.66), "high": ("navel", -0.03),
                       "drape": 6, "push": 0.012, "flare": 0.006, "skin_under": True, "tris": 600},
            "shoes": {"source": "skin_band", "color": (0.88, 0.87, 0.85),
                      "low": ("floor", -1.0), "high": ("ankle", 0.035), "push": 0.014,
                      "hull": True, "flat_bottom": True,
                      "sole": (0.022, (0.85, 0.45, 0.15)), "tris": 450},
            "socks": {"source": "skin_band", "color": (0.92, 0.92, 0.90),
                      "low": ("ankle", 0.035), "high": ("ankle", 0.13), "push": 0.004, "tris": 200},
            "tank": {"source": "region", "region": "tank", "color": (0.08, 0.08, 0.09),
                     "waist": ("navel", -0.03), "push": 0.012, "drape": 8, "scoop": 0.12, "scoop_x": 0.085,
                     "strap_x": 0.17, "armpit": 0.08, "ramp": 0.015, "tris": 700},
            "cap": {"source": "region", "region": "band_head", "color": (0.08, 0.08, 0.09),
                    "low": 0.05, "tilt": 0.3, "high": 0.4, "push": 0.012, "dome": 0.022, "ramp": 0.012,
                    "tris": 380},
            "brim": {"source": "brim", "color": (0.08, 0.08, 0.09), "brim_width": 0.19,
                     "brim_depth": 0.08, "brim_back": 0.03, "brim_z": 0.052, "tris": 300},
            "beard": {"source": "region", "region": "beard", "color": (0.16, 0.11, 0.08),
                      "under_nose": -0.058, "cheek_line": ((0.026, -0.078), (0.07, -0.025)),
                      "down": 0.19, "depth": 0.1, "push": 0.007, "ramp": 0.008,
                      "edge_smooth": 8,
                      "tris": 300},
            "hair": {"source": "region", "region": "scalp", "color": (0.12, 0.09, 0.07), "front": 0.055,
                     "temple": 0.03, "sides": -0.005, "behind": 0.11, "back": -0.07,
                     "push": 0.005, "ramp": 0.01, "tris": 300},
            "headphones": {"source": "prop", "prop": "headphones", "color": (0.86, 0.86, 0.85),
                           "cup_radius": 0.045, "cup_depth": 0.032, "cup_back": 0.075, "cup_z": -0.005,
                           "band_width": 0.03, "band_clear": 0.012, "tris": 240},
        },
    },
    # Mass monster: widest lats and quads, white headband, grey tank, blue shorts.
    "jaycutler": {
        "macros": {"gender": 1.0, "age": 0.55, "muscle": 1.0, "weight": 0.72,
                   "proportions": 0.9, "height": 0.62},
        "targets": {"lr-cheek-inner-incr": 0.5, "torso-scale-horiz-incr": 0.35, "torso-vshape-incr": 1.2, "torso-muscle-dorsi-incr": 2.2,
                    "torso-muscle-pectoral-incr": 1.5, "measure-shoulder-dist-incr": 1.0,
                    "measure-bust-circ-incr": 0.8, "torso-scale-depth-incr": 0.4,
                    "measure-waist-circ-incr": 0.3, "measure-neck-circ-incr": 1.4,
                    "lr-upperarm-muscle-incr": 1.6, "lr-upperarm-shoulder-muscle-incr": 1.7,
                    "lr-upperarm-scale-horiz-incr": 0.8, "lr-upperarm-scale-depth-incr": 0.8,
                    "measure-upperarm-circ-incr": 1.0, "lr-lowerarm-muscle-incr": 1.3,
                    "lr-upperleg-muscle-incr": 2.0, "lr-upperleg-scale-horiz-incr": 0.9,
                    "measure-thigh-circ-incr": 1.2, "lr-lowerleg-muscle-incr": 1.5,
                    "measure-calf-circ-incr": 1.0,
                    # Broad round face, short forehead, wide flat nose, heavy jaw.
                    "head-round": 0.8, "head-fat-incr": 0.6, "head-scale-horiz-incr": 0.2,
                    "chin-width-incr": 0.7, "chin-height-decr": 0.3, "forehead-scale-vert-decr": 0.5,
                    "nose-width1-incr": 0.5, "nose-width2-incr": 0.4, "nose-flaring-incr": 0.4,
                    "nose-scale-vert-decr": 0.2, "mouth-scale-horiz-incr": 0.2, "eyebrows-trans-down": 0.3},
        "body_tris": 4200,
        "skin": (0.86, 0.62, 0.48),
        "face": {"brows": (0.42, 0.31, 0.20), "lips": (0.72, 0.46, 0.40), "iris": (0.33, 0.45, 0.55)},
        "parts": {
            "shorts": {"source": "skin_band", "color": (0.15, 0.30, 0.65),
                       "low": ("hem", 0.62), "high": ("navel", -0.03),
                       "drape": 6, "push": 0.012, "flare": 0.006, "skin_under": True, "tris": 650},
            "shoes": {"source": "skin_band", "color": (0.90, 0.90, 0.88),
                      "low": ("floor", -1.0), "high": ("ankle", 0.035), "push": 0.014,
                      "hull": True, "flat_bottom": True,
                      "sole": (0.022, (0.30, 0.30, 0.32)), "tris": 450},
            "socks": {"source": "skin_band", "color": (0.92, 0.92, 0.90),
                      "low": ("ankle", 0.035), "high": ("ankle", 0.08), "push": 0.004, "tris": 160},
            "tank": {"source": "region", "region": "tank", "color": (0.78, 0.78, 0.76),
                     "waist": ("navel", -0.03), "push": 0.012, "drape": 8, "scoop": 0.13, "scoop_x": 0.09,
                     "strap_x": 0.17, "armpit": 0.08, "ramp": 0.015, "tris": 700},
            "headband": {"source": "region", "region": "band_head", "color": (0.94, 0.94, 0.92),
                         "low": 0.035, "high": 0.07, "push": 0.012, "ramp": 0.006, "tris": 200},
            "hair": {"source": "region", "region": "scalp", "color": (0.55, 0.42, 0.28), "front": 0.07,
                     "temple": 0.03, "sides": 0.0, "behind": 0.1, "back": -0.07,
                     "push": 0.007, "volume": 0.008, "ramp": 0.01, "tris": 400},
        },
    },
    # Bodyguard in police uniform (same uniform type as the policeman): navy
    # short-sleeve shirt with badge, belt, navy shorts, black boots, bald.
    "ronnie": {
        "macros": {"gender": 1.0, "age": 0.6, "muscle": 1.0, "weight": 0.75, "proportions": 0.9,
                   "height": 0.66, "race": {"african": 1.0, "asian": 0.0, "caucasian": 0.0}},
        "targets": {"head-fat-incr": 0.3, "torso-scale-depth-incr": 0.5, "torso-vshape-incr": 1.3, "torso-muscle-dorsi-incr": 2.3,
                    "torso-muscle-pectoral-incr": 1.7, "measure-shoulder-dist-incr": 1.0,
                    "measure-waist-circ-decr": 0.3, "measure-neck-circ-incr": 1.9,
                    "lr-upperarm-muscle-incr": 2.1, "lr-upperarm-shoulder-muscle-incr": 1.8,
                    "lr-upperarm-scale-horiz-incr": 0.8, "lr-upperarm-scale-depth-incr": 0.8,
                    "measure-upperarm-circ-incr": 1.1, "lr-lowerarm-muscle-incr": 1.4,
                    "lr-upperleg-muscle-incr": 2.1, "lr-upperleg-scale-horiz-incr": 0.8,
                    "measure-thigh-circ-incr": 1.3, "lr-lowerleg-muscle-incr": 1.6,
                    "measure-calf-circ-incr": 0.9,
                    # Round shaved head, broad nose, full lips, strong chin.
                    "head-round": 0.6, "chin-width-incr": 0.5, "chin-prominent-incr": 0.4,
                    "nose-width1-incr": 0.6, "nose-width2-incr": 0.6, "nose-width3-incr": 0.5,
                    "nose-flaring-incr": 0.6, "nose-scale-vert-decr": 0.2,
                    "mouth-lowerlip-volume-incr": 0.8, "mouth-upperlip-volume-incr": 0.7,
                    "mouth-scale-horiz-incr": 0.3},
        "body_tris": 4000,
        "skin": (0.36, 0.22, 0.15),
        "face": {"brows": (0.08, 0.05, 0.04), "lips": (0.30, 0.17, 0.13), "iris": (0.10, 0.06, 0.04)},
        "decals": [{"kind": "goatee", "width": 0.02, "top": -0.095, "bottom": -0.135,
                    "color": (0.06, 0.04, 0.03)}],
        "parts": {
            "shorts": {"source": "skin_band", "color": (0.10, 0.13, 0.24),
                       "low": ("hem", 0.62), "high": ("navel", -0.03),
                       "drape": 6, "push": 0.012, "flare": 0.006, "skin_under": True, "tris": 650},
            "belt": {"source": "skin_band", "color": (0.05, 0.05, 0.05),
                     "low": ("navel", -0.03), "high": ("navel", 0.015), "push": 0.016,
                     "decals": [{"kind": "rect", "x": (-0.03, 0.03), "z": (-0.62, -0.57),
                                 "color": (0.72, 0.72, 0.70)}], "tris": 200},
            "shirt": {"source": "region", "region": "tee", "color": (0.11, 0.14, 0.26),
                      "waist": ("navel", 0.015), "push": 0.013, "drape": 10, "sleeve": 0.45, "scoop": 0.06,
                      "scoop_x": 0.05, "ramp": 0.012,
                      "decals": [{"kind": "rect", "x": (0.085, 0.115), "z": (-0.355, -0.32),
                                  "color": (0.80, 0.66, 0.30)},
                                 {"kind": "rect", "x": (-0.125, -0.075), "z": (-0.345, -0.335),
                                  "color": (0.62, 0.62, 0.62)}], "tris": 900},
            "sunglasses": {"source": "prop", "prop": "sunglasses", "color": (0.05, 0.05, 0.06),
                           "lens_x": 0.034, "lens_w": 0.056, "lens_h": 0.04, "wrap": 4.0,
                           "arm_back": 0.09, "tris": 400},
            "lenses": {"source": "prop", "prop": "sunglass_lenses", "color": (0.03, 0.035, 0.05),
                       "lens_x": 0.034, "lens_w": 0.056, "lens_h": 0.04, "wrap": 4.0,
                       "arm_back": 0.09, "tris": 200},
            "boots": {"source": "skin_band", "color": (0.05, 0.05, 0.05),
                      "low": ("floor", -1.0), "high": ("ankle", 0.13), "push": 0.016,
                      "hull": True, "flat_bottom": True, "sole": (0.03, (0.02, 0.02, 0.02)),
                      "tris": 500},
        },
    },
    # Lean aesthetic physique: shredded, narrow waist, purple shorts, spiky
    # black hair, white trainers.
    "zyzz": {
        "macros": {"gender": 1.0, "age": 0.4, "muscle": 1.0, "weight": 0.25,
                   "proportions": 1.0, "height": 0.72},
        "targets": {"eyebrows-trans-forward": 0.3, "torso-vshape-incr": 1.5, "torso-muscle-dorsi-incr": 1.4,
                    "torso-muscle-pectoral-incr": 1.4, "measure-shoulder-dist-incr": 0.8,
                    "measure-waist-circ-decr": 1.4, "lr-upperarm-muscle-incr": 1.6,
                    "lr-upperarm-shoulder-muscle-incr": 1.7, "lr-lowerarm-muscle-incr": 1.0,
                    "lr-upperleg-muscle-incr": 1.3, "lr-lowerleg-muscle-incr": 1.3,
                    "stomach-pregnant-decr": 1.0,
                    # Narrow oval face, high cheekbones, sharp chin, slim nose.
                    "head-oval": 0.7, "head-scale-horiz-decr": 0.3, "chin-prominent-incr": 0.7,
                    "chin-triangle": 0.4, "lr-cheek-bones-incr": 0.7, "lr-cheek-inner-decr": 0.4,
                    "nose-scale-horiz-decr": 0.35, "nose-point-width-decr": 0.3,
                    "eyebrows-angle-up": 0.3, "mouth-scale-horiz-decr": 0.15},
        "body_tris": 4400,
        "skin": (0.76, 0.52, 0.37),
        "face": {"brows": (0.06, 0.05, 0.05), "lips": (0.68, 0.42, 0.36), "iris": (0.18, 0.11, 0.07)},
        "parts": {
            "shorts": {"source": "skin_band", "color": (0.40, 0.22, 0.62),
                       "low": ("hem", 0.6), "high": ("navel", -0.035),
                       "drape": 6, "push": 0.01, "flare": 0.008, "skin_under": True, "tris": 650},
            "shoes": {"source": "skin_band", "color": (0.92, 0.92, 0.90),
                      "low": ("floor", -1.0), "high": ("ankle", 0.035), "push": 0.013,
                      "hull": True, "flat_bottom": True,
                      "sole": (0.02, (0.75, 0.75, 0.75)), "tris": 450},
            "socks": {"source": "skin_band", "color": (0.94, 0.94, 0.92),
                      "low": ("ankle", 0.035), "high": ("ankle", 0.07), "push": 0.004, "tris": 160},
            # 2010 aesthetic: tall spiky hair swept up and back, short sides.
            "hair": {"source": "region", "region": "scalp", "color": (0.06, 0.05, 0.05), "front": 0.045,
                     "temple": 0.05, "sides": 0.04, "behind": 0.1, "back": -0.04,
                     "push": 0.004, "volume": 0.022, "spikes": 0.012, "sweep": (0.02, 0.012), "quiff": True,
                     "ramp": 0.025, "tris": 900},
        },
    },
    # Rainbow-haired Goku from the reference, anime-ish but in the same world:
    # heroic anime proportions (long legs, broad shoulders, narrow waist,
    # smaller head), modelled anime eyes and brows, light skin shading, tall
    # multicolour spikes, loose pink tee with a rainbow heart over an orange
    # gi hem, purple sash, wide orange gi trousers torn at the knees, rounded
    # blue boots with a red stripe and rope ties, spiky blue wristbands.
    "goku": {
        "macros": {"gender": 1.0, "age": 0.3, "muscle": 1.0, "weight": 0.55,
                   "proportions": 1.0, "height": 0.75},
        "cavity": 0.0,  # flat, cel-style colours
        # Drawn anime finish: 2048 texture, baked cel shadow from a key light
        # front-left-above, ink on part boundaries and on muscle grooves.
        "texture": 2048,
        "ink": {"light": (-0.45, -0.55, 0.7), "threshold": 0.05, "shadow": (0.78, 0.72, 0.86),
                "line": 0.0018, "color": (0.10, 0.06, 0.08), "muscle": 0.08},
        "anime": {"nose_out": 0.03, "lip_out": 0.022, "socket_fill": 0.024, "vjaw": 0.28, "stylize": 2},
        "targets": {"torso-vshape-incr": 1.5, "torso-muscle-dorsi-incr": 1.2,
                    "torso-muscle-pectoral-incr": 1.3, "measure-shoulder-dist-incr": 1.0,
                    "measure-waist-circ-decr": 1.2, "measure-neck-circ-incr": 0.5,
                    "lr-upperarm-muscle-incr": 2.2, "lr-upperarm-shoulder-muscle-incr": 1.8,
                    "lr-upperarm-scale-horiz-incr": 1.4, "lr-upperarm-scale-depth-incr": 1.2,
                    "lr-lowerarm-muscle-incr": 1.8, "lr-lowerarm-scale-horiz-incr": 0.8, "lr-lowerarm-scale-depth-incr": 0.6,
                    "lr-upperleg-muscle-incr": 1.1, "lr-lowerleg-muscle-incr": 1.1,
                    "stomach-pregnant-decr": 1.0,
                    # Anime proportions: longer legs, smaller head.
                    "upperlegs-height-incr": 0.6, "lowerlegs-height-incr": 0.5,
                    "head-scale-vert-decr": 0.3, "head-scale-horiz-decr": 0.25, "head-scale-depth-decr": 0.15,
                    # Anime face: narrow, pointed chin, hollow cheeks, small
                    # nose and mouth; the eyes and brows are modelled on top.
                    "head-oval": 0.7, "chin-triangle": 0.6, "chin-prominent-incr": 0.4,
                    "lr-cheek-inner-decr": 0.5, "lr-cheek-bones-incr": 0.3,
                    "nose-scale-vert-decr": 0.4, "nose-scale-horiz-decr": 0.4, "nose-scale-depth-decr": 0.3,
                    "mouth-scale-horiz-decr": 0.3, "mouth-lowerlip-volume-decr": 0.3},
        "skin": (0.95, 0.78, 0.64),
        # Painted brows hidden (skin colour): the modelled brows replace them.
        "face": {"brows": (0.95, 0.78, 0.64), "lips": (0.92, 0.72, 0.62), "iris": (0.10, 0.60, 0.62)},
        # Anime mouth: one short dark line, set in a stern frown.
        "decals": [{"kind": "rect", "x": (-0.012, 0.012), "z": (-0.088, -0.084), "color": (0.42, 0.24, 0.22)}],
        "parts": {
            "trousers": {"source": "skin_band", "color": (0.94, 0.48, 0.12),
                         "low": ("ankle", 0.2), "high": ("navel", -0.045),
                         "push": 0.016, "flare": 0.0, "bag": 0.07, "skin_under": True, "drape": 12,
                         "decals": [{"kind": "patch", "x": 0.11, "z_mark": ("knee", 0.0), "rx": 0.045,
                                     "rz": 0.06, "color": (0.95, 0.78, 0.64)}], "tris": 800},
            "sash": {"source": "skin_band", "color": (0.36, 0.22, 0.58),
                     "low": ("navel", -0.045), "high": ("navel", 0.0), "push": 0.024, "tris": 200},
            "gi_hem": {"source": "skin_band", "color": (0.94, 0.48, 0.12),
                       "low": ("navel", 0.0), "high": ("navel", 0.035), "push": 0.026, "tris": 200},
            "boots": {"source": "skin_band", "color": (0.22, 0.30, 0.72),
                      "low": ("floor", -1.0), "high": ("ankle", 0.2), "push": 0.018,
                      "hull": True, "round": 6, "flat_bottom": True, "sole": (0.025, (0.12, 0.14, 0.30)),
                      "decals": [{"kind": "zband", "z_mark": (("ankle", 0.09), ("ankle", 0.105)),
                                  "color": (0.94, 0.80, 0.32)},
                                 {"kind": "frontstripe", "width": 0.007, "z_mark": (("floor", 0.02), ("ankle", 0.2)),
                                  "color": (0.84, 0.16, 0.16)}],
                      "tris": 500},
            "tee": {"source": "region", "region": "tee", "color": (0.94, 0.40, 0.60),
                    "waist": ("navel", 0.035), "push": 0.025, "drape": 14, "sleeve": 0.42,
                    "scoop": 0.05, "scoop_x": 0.05, "ramp": 0.012,
                    "decals": [{"kind": "heart", "x": 0.0, "z": -0.36, "size": 0.11,
                                "colors": [(0.90, 0.16, 0.16), (0.96, 0.52, 0.12), (0.97, 0.86, 0.20),
                                           (0.24, 0.70, 0.30), (0.20, 0.42, 0.88), (0.52, 0.26, 0.74)]}],
                    "decimate": 1100, "tris": 800},
            "cuffs": {"source": "region", "region": "cuff", "color": (0.24, 0.28, 0.72),
                      "length": 0.07, "push": 0.012, "ramp": 0.004, "tris": 200},
            "hair": {"source": "region", "region": "scalp", "color": (0.90, 0.90, 0.96), "front": 0.035,
                     "temple": 0.02, "sides": 0.0, "behind": 0.09, "back": -0.06,
                     "push": 0.012, "volume": 0.01, "ramp": 0.015, "decimate": 260, "tris": 300},
            # Flame-shaped locks as in the reference: a silver outline fan
            # behind, coloured flames in front of it, side flames flicking up,
            # a teal bang sweeping over the forehead and a magenta one beside it.
            # (azimuth deg: 0 front, 90 his left; elevation deg; length m;
            #  width m; curl: + flicks up, - falls; palette colour; sweep in x)
            "locks": {"source": "prop", "prop": "anime_hair", "color": (0.92, 0.92, 0.97),
                      "steps": 4, "taper": 0.8, "bulge": 0.4, "thick": 0.25,
                      "palette": {"silver": (0.90, 0.90, 0.96), "magenta": (0.88, 0.12, 0.52),
                                  "pink": (0.96, 0.40, 0.66), "purple": (0.52, 0.24, 0.86),
                                  "lavender": (0.70, 0.56, 0.96), "yellow": (0.99, 0.84, 0.16),
                                  "teal": (0.18, 0.84, 0.74), "blue": (0.24, 0.50, 0.96)},
                      # Three rings, low to high, so the locks overlap into one
                      # flame mass that covers the back and sides of the head.
                      "locks": [(180, 15, 0.16, 0.15, 0.55, "silver", 0.0),
                                (145, 15, 0.16, 0.15, 0.55, "lavender", 0.06),
                                (-145, 15, 0.16, 0.15, 0.55, "silver", -0.06),
                                (110, 18, 0.17, 0.15, 0.60, "blue", 0.08),
                                (-110, 18, 0.17, 0.15, 0.60, "pink", -0.08),
                                (80, 24, 0.22, 0.15, 0.50, "yellow", 0.10),
                                (-80, 24, 0.22, 0.15, 0.50, "silver", -0.10),
                                (160, 45, 0.21, 0.17, 0.40, "silver", 0.05),
                                (-160, 45, 0.21, 0.17, 0.40, "pink", -0.05),
                                (120, 45, 0.22, 0.17, 0.45, "silver", 0.10),
                                (-120, 45, 0.22, 0.17, 0.45, "purple", -0.10),
                                (62, 40, 0.27, 0.17, 0.32, "lavender", 0.16),
                                (95, 42, 0.25, 0.16, 0.30, "blue", 0.14),
                                (-62, 40, 0.27, 0.17, 0.32, "silver", -0.16),
                                (-95, 42, 0.25, 0.16, 0.30, "pink", -0.14),
                                (180, 72, 0.24, 0.17, 0.20, "silver", 0.0),
                                (140, 72, 0.25, 0.17, 0.25, "purple", 0.10),
                                (-140, 72, 0.25, 0.17, 0.25, "silver", -0.10),
                                (30, 68, 0.25, 0.16, 0.22, "purple", 0.14),
                                (-30, 68, 0.25, 0.16, 0.22, "yellow", -0.14),
                                (5, 82, 0.25, 0.16, 0.12, "magenta", -0.06),
                                (-6, 60, 0.17, 0.12, -1.50, "teal", (-0.40, 0.25)),
                                (36, 55, 0.14, 0.10, -1.55, "magenta", (0.20, 0.25)),
                                (-40, 55, 0.11, 0.08, -1.55, "silver", (-0.10, 0.25))],
                      "tris": 900},
            "gi_flaps": {"source": "prop", "prop": "waist_spikes", "on": "gi_hem", "color": (0.94, 0.48, 0.12),
                         "count": 14, "length": 0.05, "width": 0.045, "out": 0.9, "dz": 0.01, "tris": 120},
            "cuff_spikes": {"source": "prop", "prop": "cuff_spikes", "color": (0.24, 0.28, 0.72),
                            "on": "cuffs", "count": 6, "radius": 0.012, "length": 0.05, "back": 0.9, "tris": 100},
            "eye_white": {"source": "prop", "prop": "anime_eyes", "layer": "white",
                           "color": (0.97, 0.97, 0.96), "eye_w": 0.044, "eye_h": 0.026, "eye_gap": 0.034,
                           "eye_z": 0.002, "tilt": 12.0, "lash": 0.005, "brow": 0.007,
                           "frown": 0.009, "tris": 60},
            "eye_iris": {"source": "prop", "prop": "anime_eyes", "layer": "iris",
                           "color": (0.1, 0.6, 0.62), "eye_w": 0.044, "eye_h": 0.026, "eye_gap": 0.034,
                           "eye_z": 0.002, "tilt": 12.0, "lash": 0.005, "brow": 0.007,
                           "frown": 0.009, "tris": 40},
            "eye_pupil": {"source": "prop", "prop": "anime_eyes", "layer": "pupil",
                           "color": (0.03, 0.05, 0.07), "eye_w": 0.044, "eye_h": 0.026, "eye_gap": 0.034,
                           "eye_z": 0.002, "tilt": 12.0, "lash": 0.005, "brow": 0.007,
                           "frown": 0.009, "tris": 40},
            "eye_shine": {"source": "prop", "prop": "anime_eyes", "layer": "shine",
                           "color": (1.0, 1.0, 1.0), "eye_w": 0.044, "eye_h": 0.026, "eye_gap": 0.034,
                           "eye_z": 0.002, "tilt": 12.0, "lash": 0.005, "brow": 0.007,
                           "frown": 0.009, "tris": 40},
            "eye_lash": {"source": "prop", "prop": "anime_eyes", "layer": "lash",
                           "color": (0.05, 0.05, 0.07), "eye_w": 0.044, "eye_h": 0.026, "eye_gap": 0.034,
                           "eye_z": 0.002, "tilt": 12.0, "lash": 0.005, "brow": 0.007,
                           "frown": 0.009, "tris": 60},
            "eye_brow": {"source": "prop", "prop": "anime_eyes", "layer": "brow",
                           "color": (0.22, 0.2, 0.3), "eye_w": 0.044, "eye_h": 0.026, "eye_gap": 0.034,
                           "eye_z": 0.002, "tilt": 12.0, "lash": 0.005, "brow": 0.007,
                           "frown": 0.009, "tris": 40},
        },
    },
    # Patrol officer: same uniform type as Ronnie (navy short-sleeve shirt with
    # badge and name tag, black duty belt) with long trousers and boots; fit,
    # not a bodybuilder; short dark hair.
    "policeman": {
        "macros": {"gender": 1.0, "age": 0.55, "muscle": 0.8, "weight": 0.55,
                   "proportions": 0.9, "height": 0.7},
        "targets": {"torso-vshape-incr": 0.6, "measure-shoulder-dist-incr": 0.4,
                    "lr-upperarm-muscle-incr": 0.7, "torso-muscle-pectoral-incr": 0.5,
                    # Long rectangular face, strong straight jaw, straight nose.
                    "head-rectangular": 0.7, "chin-width-incr": 0.5, "chin-bones-incr": 0.5,
                    "nose-scale-vert-incr": 0.2, "nose-hump-incr": 0.15,
                    "eyebrows-trans-down": 0.2, "mouth-scale-horiz-decr": 0.1},
        "skin": (0.84, 0.64, 0.52),
        "face": {"brows": (0.16, 0.11, 0.08), "lips": (0.66, 0.42, 0.38), "iris": (0.26, 0.20, 0.14)},
        "parts": {
            "trousers": {"source": "skin_band", "color": (0.10, 0.12, 0.20),
                         "low": ("ankle", 0.12), "high": ("navel", -0.03),
                         "push": 0.012, "flare": 0.004, "skin_under": True, "drape": 8, "tris": 900},
            "belt": {"source": "skin_band", "color": (0.05, 0.05, 0.05),
                     "low": ("navel", -0.03), "high": ("navel", 0.02), "push": 0.02,
                     "decals": [{"kind": "rect", "x": (-0.025, 0.025), "z": (-0.66, -0.62),
                                 "color": (0.72, 0.72, 0.70)}], "tris": 200},
            "shirt": {"source": "region", "region": "tee", "color": (0.11, 0.14, 0.26),
                      "waist": ("navel", 0.02), "push": 0.013, "drape": 10, "sleeve": 0.45,
                      "scoop": 0.05, "scoop_x": 0.045, "ramp": 0.012,
                      "decals": [{"kind": "rect", "x": (0.075, 0.105), "z": (-0.37, -0.335),
                                  "color": (0.80, 0.66, 0.30)},
                                 {"kind": "rect", "x": (-0.115, -0.07), "z": (-0.36, -0.35),
                                  "color": (0.62, 0.62, 0.62)},
                                 {"kind": "rect", "x": (0.11, 0.135), "z": (-0.27, -0.235),
                                  "color": (0.04, 0.04, 0.04)}], "tris": 900},
            "boots": {"source": "skin_band", "color": (0.05, 0.05, 0.05),
                      "low": ("floor", -1.0), "high": ("ankle", 0.12), "push": 0.016,
                      "hull": True, "flat_bottom": True, "sole": (0.03, (0.02, 0.02, 0.02)),
                      "tris": 500},
            "hair": {"source": "region", "region": "scalp", "color": (0.20, 0.14, 0.10), "front": 0.06,
                     "temple": 0.035, "sides": 0.01, "behind": 0.1, "back": -0.06,
                     "push": 0.007, "volume": 0.012, "part_x": 0.03, "ramp": 0.015, "tris": 500},
        },
    },
    # Young member in the shared style: dark curly hair, teal hoodie with a
    # white print, black jeans, white trainers.
    "davie": {
        "macros": {"gender": 1.0, "age": 0.4, "muscle": 0.45, "weight": 0.4, "proportions": 0.6,
                   "height": 0.62},
        "targets": {"head-oval": 0.4, "chin-prominent-incr": 0.3, "eyebrows-trans-down": 0.3,
                    "nose-scale-vert-incr": 0.3, "nose-hump-incr": 0.3, "lr-cheek-bones-incr": 0.3,
                    "mouth-lowerlip-volume-incr": 0.3},
        "skin": (0.86, 0.66, 0.54),
        "face": {"brows": (0.10, 0.08, 0.07), "lips": (0.72, 0.46, 0.42), "iris": (0.24, 0.16, 0.10)},
        "parts": {
            "jeans": {"source": "skin_band", "color": (0.17, 0.17, 0.19),
                      "low": ("ankle", 0.035), "high": ("navel", -0.10),
                      # Full-length jeans: no skin under them, or the hips and
                      # knees poke through the denim and the hoodie hem when bent.
                      "push": 0.012, "flare": 0.005, "drape": 8, "tris": 900},
            "hem": {"source": "skin_band", "color": (0.10, 0.44, 0.52),
                    "low": ("navel", -0.10), "high": ("navel", -0.065), "push": 0.026, "tris": 200},
            # full_hips: the glute skin above the hem band is thigh-weighted,
            # so a torso-only cut left it bare between hoodie and hem.
            "hoodie": {"source": "region", "region": "tee", "color": (0.12, 0.50, 0.58),
                       "waist": ("navel", -0.065), "full_hips": True, "push": 0.024, "drape": 12, "sleeve": 1.9,
                       "scoop": 0.03, "scoop_x": 0.05, "ramp": 0.012,
                       "decals": [{"kind": "rect", "x": (-0.024, -0.018), "z": (-0.33, -0.23),
                                   "color": (0.86, 0.88, 0.88)},
                                  {"kind": "rect", "x": (0.018, 0.024), "z": (-0.33, -0.23),
                                   "color": (0.86, 0.88, 0.88)},
                                  {"kind": "rect", "x": (-0.085, 0.085), "z": (-0.385, -0.37),
                                   "color": (0.94, 0.95, 0.95)},
                                  {"kind": "rect", "x": (-0.085, 0.085), "z": (-0.42, -0.395),
                                   "color": (0.94, 0.95, 0.95)},
                                  {"kind": "rect", "x": (-0.04, 0.04), "z": (-0.445, -0.435),
                                   "color": (0.94, 0.95, 0.95)},
                                  {"kind": "rect", "x": (-0.08, 0.08), "z": (-0.58, -0.50),
                                   "color": (0.10, 0.43, 0.51)}], "tris": 1100},
            "cuffs": {"source": "region", "region": "cuff", "color": (0.10, 0.44, 0.52),
                      "length": 0.06, "push": 0.022, "ramp": 0.004, "tris": 200},
            "shoes": {"source": "skin_band", "color": (0.92, 0.92, 0.90),
                      "low": ("floor", -1.0), "high": ("ankle", 0.035), "push": 0.014,
                      "hull": True, "flat_bottom": True, "sole": (0.022, (0.86, 0.86, 0.84)), "tris": 450},
            # Thick dark curls: low on the forehead, over the ears, to the nape.
            "hair": {"source": "region", "region": "scalp", "color": (0.10, 0.08, 0.07), "front": 0.04,
                     "temple": 0.015, "sides": -0.03, "behind": 0.09, "back": -0.10,
                     "push": 0.024, "volume": 0.045, "wave": 0.022, "spikes": 0.01, "ramp": 0.02,
                     "tris": 900},
            # Curls: short blunt tufts all over, so the mass reads curly, not a helmet.
            "curls": {"source": "prop", "prop": "hair_spikes", "color": (0.11, 0.09, 0.08), "lift": 0.15,
                      "spikes": [(az, el, 0.03, 0.03) for el, step in ((10, 40), (35, 45), (60, 60))
                                 for az in range(-180 + step // 2, 180, step)] + [(0, 85, 0.04, 0.03)],
                      "tris": 300},
        },
    },
    # Park resident (Hank): thin older man, open red bomber jacket over a
    # bare chest, white briefs, hip pack, orange cap over spiky grey hair,
    # round glasses, flip-flops.
    "homeless_guy": {
        "macros": {"gender": 1.0, "age": 0.75, "muscle": 0.35, "weight": 0.22,
                   "proportions": 0.5, "height": 0.72},
        "targets": {"lr-cheek-inner-decr": 0.7, "lr-cheek-bones-incr": 0.5, "head-oval": 0.5,
                    "chin-prominent-incr": 0.4, "nose-scale-vert-incr": 0.4, "nose-hump-incr": 0.3,
                    "mouth-scale-horiz-incr": 0.2, "measure-neck-circ-decr": 0.5},
        "skin": (0.82, 0.62, 0.44),
        "face": {"brows": (0.62, 0.60, 0.58), "lips": (0.66, 0.44, 0.36), "iris": (0.30, 0.24, 0.16)},
        "parts": {
            "briefs": {"source": "skin_band", "color": (0.92, 0.92, 0.90),
                       "low": ("hem", 0.22), "high": ("navel", -0.07), "push": 0.006, "tris": 300},
            "strap": {"source": "skin_band", "color": (0.12, 0.11, 0.11),
                      "low": ("navel", -0.07), "high": ("navel", -0.05), "push": 0.008, "tris": 160},
            "hem": {"source": "skin_band", "color": (0.46, 0.13, 0.13),
                    "low": ("navel", -0.05), "high": ("navel", -0.02), "push": 0.03, "tris": 200},
            # Open bomber: a deep narrow front opening shows the bare chest.
            "jacket": {"source": "region", "region": "tee", "color": (0.58, 0.17, 0.17),
                       "waist": ("navel", -0.02), "push": 0.024, "drape": 8, "sleeve": 1.85,
                       "scoop": 0.42, "back_scoop": 0.04, "scoop_x": 0.035, "ramp": 0.012,
                       "neck_free": True, "decimate": 1400, "decimate_beyond_x": 0.24, "tris": 1100},
            "zip": {"source": "zip", "on": "jacket", "color": (0.30, 0.24, 0.18), "max_x": 0.08,
                    "width": 0.004, "tris": 120},
            "collar": {"source": "collar", "color": (0.24, 0.17, 0.13), "open": 30.0, "stand": 0.03,
                       "width": 0.055, "tip": 0.035, "tris": 200},
            "cuffs": {"source": "region", "region": "cuff", "color": (0.46, 0.13, 0.13),
                      "length": 0.06, "push": 0.03, "ramp": 0.004, "tris": 200},
            "pack": {"source": "pack", "color": (0.16, 0.14, 0.14), "at": ("navel", -0.085), "x": 0.12,
                     "size": (0.15, 0.06, 0.085),
                     "palette": {"bag": (0.16, 0.14, 0.14), "zip": (0.44, 0.43, 0.42)}, "tris": 200},
            "sandals": {"source": "skin_band", "color": (0.34, 0.24, 0.17),
                        "low": ("floor", -1.0), "high": ("floor", 0.045), "push": 0.006,
                        "flat_bottom": True, "decimate": 300, "tris": 200},
            "cap": {"source": "region", "region": "band_head", "color": (0.90, 0.46, 0.18),
                    "low": 0.055, "tilt": 0.3, "high": 0.4, "push": 0.016, "dome": 0.02, "ramp": 0.012,
                    "tris": 380},
            "brim": {"source": "brim", "color": (0.90, 0.46, 0.18), "brim_width": 0.18,
                     "brim_depth": 0.075, "brim_back": 0.035, "brim_z": 0.057, "tris": 300},
            "hair": {"source": "region", "region": "scalp", "color": (0.80, 0.80, 0.82), "front": 0.05,
                     "temple": 0.02, "sides": -0.01, "behind": 0.08, "back": -0.08,
                     "push": 0.012, "volume": 0.01, "ramp": 0.015, "decimate": 250, "tris": 400},
            # Grey tufts sticking out under the cap, over the ears and behind.
            "tufts": {"source": "prop", "prop": "hair_spikes", "color": (0.82, 0.82, 0.84), "lift": 0.1,
                      "spikes": [(70, 10, 0.07, 0.022), (-70, 10, 0.07, 0.022), (95, 0, 0.08, 0.024),
                                 (-95, 0, 0.08, 0.024), (120, 5, 0.075, 0.024), (-120, 5, 0.075, 0.024),
                                 (150, 0, 0.065, 0.024), (-150, 0, 0.065, 0.024), (180, 5, 0.06, 0.024),
                                 (85, -15, 0.06, 0.02), (-85, -15, 0.06, 0.02), (135, -15, 0.06, 0.02),
                                 (-135, -15, 0.06, 0.02)], "tris": 200},
            "glasses": {"source": "prop", "prop": "sunglasses", "color": (0.20, 0.18, 0.16),
                        "lens_x": 0.033, "lens_w": 0.044, "lens_h": 0.042, "wrap": 1.0,
                        "arm_back": 0.09, "tris": 400},
            "lenses": {"source": "prop", "prop": "sunglass_lenses", "color": (0.78, 0.80, 0.82),
                       "lens_x": 0.033, "lens_w": 0.044, "lens_h": 0.042, "wrap": 1.0,
                       "arm_back": 0.09, "tris": 200},
        },
    },
    # Mark: tall and lanky, wavy light-brown quiff, thin moustache, plain
    # black tee, grey shorts with a pale leaf print, navy clogs, no socks.
    "mark": {
        "macros": {"gender": 1.0, "age": 0.4, "muscle": 0.4, "weight": 0.22, "proportions": 0.7,
                   "height": 0.92},
        "targets": {"head-oval": 0.5, "chin-prominent-incr": 0.3, "nose-scale-vert-incr": 0.3,
                    "mouth-scale-horiz-incr": 0.3, "measure-neck-circ-decr": 0.4},
        "skin": (0.88, 0.70, 0.58),
        "face": {"brows": (0.36, 0.25, 0.16), "lips": (0.74, 0.48, 0.44), "iris": (0.30, 0.34, 0.30)},
        "parts": {
            "shorts": {"source": "skin_band", "color": (0.27, 0.28, 0.25),
                       "low": ("hem", 0.72), "high": ("navel", -0.03),
                       "drape": 6, "push": 0.014, "flare": 0.016, "skin_under": True,
                       "decals": [{"kind": "pattern", "scale": 110.0, "threshold": 0.2,
                                   "color": (0.68, 0.70, 0.58)}], "tris": 700},
            "clogs": {"source": "skin_band", "color": (0.14, 0.19, 0.38),
                      "low": ("floor", -1.0), "high": ("ankle", 0.02), "push": 0.02,
                      "hull": True, "flat_bottom": True, "sole": (0.025, (0.10, 0.14, 0.28)), "tris": 450},
            "tee": {"source": "region", "region": "tee", "color": (0.07, 0.07, 0.08),
                    "waist": ("navel", -0.03), "push": 0.012, "drape": 8, "sleeve": 0.5, "sleeve_x": 0.15,
                    "scoop": 0.07, "scoop_x": 0.06, "ramp": 0.012, "tris": 900},
            "hair": {"source": "region", "region": "scalp", "color": (0.30, 0.21, 0.13), "front": 0.05,
                     "temple": 0.03, "sides": 0.0, "behind": 0.1, "back": -0.07,
                     "push": 0.01, "volume": 0.03, "wave": 0.008, "sweep": (0.015, 0.018), "quiff": True,
                     "ramp": 0.02, "tris": 700},
            "moustache": {"source": "region", "region": "beard", "color": (0.26, 0.17, 0.10),
                          "under_nose": -0.044, "cheek_line": ((0.03, -0.05), (0.052, -0.078)),
                          "down": 0.08, "depth": 0.06, "push": 0.013, "ramp": 0.004, "edge_smooth": 2,
                          "tris": 60},
        },
    },
    # Receptionist: young and slim, straight brown hair to the jaw, navy
    # two-piece suit with a white shirt, red tie, pocket square, black shoes.
    "manwithsuit1": {
        "macros": {"gender": 1.0, "age": 0.38, "muscle": 0.4, "weight": 0.32, "proportions": 0.7,
                   "height": 0.8},
        "targets": {"head-oval": 0.5, "chin-prominent-incr": 0.2, "nose-scale-vert-incr": 0.2,
                    "mouth-scale-horiz-incr": 0.3},
        "skin": (0.90, 0.72, 0.62),
        "face": {"brows": (0.24, 0.16, 0.11), "lips": (0.76, 0.48, 0.46), "iris": (0.30, 0.24, 0.16)},
        "parts": {
            # Trousers first up to the jacket hem, so both share that cut
            # plane and stay welded; the jacket then takes everything above
            # with an open front, and the shirt is what the opening leaves.
            "trousers": {"source": "skin_band", "color": (0.09, 0.11, 0.21),
                         "low": ("ankle", 0.04), "high": ("hem", 0.3),
                         "push": 0.006, "drape": 8, "tris": 900},
            "jacket": {"source": "region", "region": "tee", "color": (0.09, 0.11, 0.21),
                       "waist": ("hem", 0.3), "full_hips": True, "drape": 10, "sleeve": 1.85,
                       "scoop": 0.3, "back_scoop": 0.03, "scoop_x": 0.075, "vee": True,
                       "push": 0.026, "ramp": 0.002,  # a near-instant step: the hem stands off the trousers
                       "decals": [{"kind": "rect", "x": (0.085, 0.125), "z": (-0.335, -0.32),
                                   "color": (0.95, 0.95, 0.95)},
                                  {"kind": "rect", "x": (-0.008, 0.008), "z": (-0.46, -0.445),
                                   "color": (0.05, 0.06, 0.10)},
                                  {"kind": "rect", "x": (-0.008, 0.008), "z": (-0.54, -0.525),
                                   "color": (0.05, 0.06, 0.10)},
                                  # Hip pocket flaps and a dark hem line, so the
                                  # jacket reads apart from the trousers.
                                  {"kind": "rect", "x": (0.07, 0.17), "z": (-0.80, -0.785),
                                   "color": (0.07, 0.09, 0.18)},
                                  {"kind": "rect", "x": (-0.17, -0.07), "z": (-0.80, -0.785),
                                   "color": (0.07, 0.09, 0.18)},
                                  {"kind": "zband", "z_mark": (("hem", 0.3), ("hem", 0.22)),
                                   "color": (0.03, 0.04, 0.08)}], "tris": 1300},
            "shirt": {"source": "region", "region": "tee", "color": (0.95, 0.95, 0.94),
                      "waist": ("hip", -0.08), "push": 0.006, "sleeve": 0.3,
                      "scoop": 0.02, "scoop_x": 0.035, "ramp": 0.006,
                      "decals": [{"kind": "rect", "x": (-0.022, 0.022), "z": (-0.6, -0.15),
                                  "color": (0.80, 0.08, 0.10)}], "tris": 200},
            "cuffs": {"source": "region", "region": "cuff", "color": (0.95, 0.95, 0.94),
                      "length": 0.03, "push": 0.022, "ramp": 0.004, "tris": 120},
            "shoes": {"source": "skin_band", "color": (0.06, 0.06, 0.07),
                      "low": ("floor", -1.0), "high": ("ankle", 0.04), "push": 0.014,
                      "hull": True, "flat_bottom": True, "sole": (0.02, (0.03, 0.03, 0.03)), "tris": 450},
            # Straight bowl cut: low fringe, over the ears, down to the jaw.
            "hair": {"source": "region", "region": "scalp", "color": (0.30, 0.20, 0.13), "front": 0.045,
                     "temple": 0.02, "sides": -0.06, "behind": 0.06, "back": -0.11,
                     "push": 0.016, "volume": 0.02, "ramp": 0.02, "tris": 800},
        },
    },
    # Lollipop Larry, the sky-dropping fixer: a real cartoon dog standing on
    # two short legs (big head, floppy dark ears, short arms with paws), a
    # propeller cap, yellow tee with an invented bone badge, red shorts and a
    # rainbow spiral lollipop in the right paw. Built by build_dog, not MPFB.
    "jolly_dog": {
        "dog": True,
        "colors": {"fur": (0.90, 0.88, 0.84), "face": (0.95, 0.94, 0.90), "patch": (0.74, 0.64, 0.54),
                   "ear": (0.36, 0.34, 0.34), "ear_inner": (0.86, 0.70, 0.72), "nose": (0.08, 0.07, 0.07),
                   "eye": (0.10, 0.06, 0.05), "mouth": (0.32, 0.10, 0.10), "tongue": (0.94, 0.56, 0.62),
                   "tee": (0.98, 0.84, 0.10), "shorts": (0.88, 0.12, 0.10), "badge": (0.16, 0.36, 0.78),
                   "bone": (0.97, 0.97, 0.95), "cap_blue": (0.16, 0.30, 0.78), "cap_red": (0.88, 0.12, 0.10),
                   "cap_orange": (0.98, 0.52, 0.10), "cap_yellow": (0.98, 0.84, 0.10), "stick": (0.90, 0.78, 0.56),
                   "candy": ((0.88, 0.12, 0.10), (0.98, 0.52, 0.10), (0.98, 0.84, 0.10), (0.18, 0.66, 0.26),
                             (0.16, 0.36, 0.78))},
        # Skeleton landmarks (metres): short legs and arms, a long head bone.
        "rig": {"hip": 0.36, "knee": 0.21, "ankle": 0.08, "leg_x": 0.14, "neck": 0.73, "head": 0.78,
                "crown": 1.40, "arm_z": 0.69, "shoulder_x": 0.17, "elbow_x": 0.27, "wrist_x": 0.36,
                "paw": 0.5, "foot": 0.65, "width": 0.95, "depth": 0.8},
        "eye": (0.0, -0.19, 1.14),
        # The voxel body's stair-steps would bake into the fur: keep the
        # cavity light.
        "cavity": 0.25,
        "body_tris": 5200,
        "face": {},
    },
}


def mpfb():
    package = next(m.__name__ for m in addon_utils.modules() if m.__name__.endswith(".mpfb"))
    load = lambda name: importlib.import_module(f"{package}.services.{name}")  # noqa: E731
    return (load("humanservice").HumanService, load("targetservice").TargetService,
            load("rigservice").RigService)


def srgb_to_linear(c):
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


def select_only(*objects):
    bpy.ops.object.mode_set(mode="OBJECT") if bpy.context.mode != "OBJECT" else None
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]


def group_verts(obj, name):
    group = obj.vertex_groups.get(name)
    if group is None:
        return set()
    return {v.index for v in obj.data.vertices for g in v.groups if g.group == group.index and g.weight > 0.5}


def group_center(obj, names):
    points = [obj.matrix_world @ obj.data.vertices[i].co for n in names for i in group_verts(obj, n)]
    return sum(points, Vector()) / len(points)


def extract(source, group, name):
    """Copy of source keeping only the vertices of one MPFB vertex group."""
    keep = group_verts(source, group)
    obj = source.copy()
    obj.data = source.data.copy()
    obj.name = obj.data.name = name
    bpy.context.scene.collection.objects.link(obj)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.index not in keep], context="VERTS")
    bm.to_mesh(obj.data)
    bm.free()
    return obj


def pose_tpose(meta):
    select_only(meta)
    bpy.ops.object.mode_set(mode="POSE")
    for side, turn in remake_rig.arm_tpose_turns(meta, use_edit_bones=False).items():
        pose_bone = meta.pose.bones[f"upper_arm.{side}"]
        pose_bone.matrix = turn @ pose_bone.matrix
        bpy.context.view_layer.update()
    bpy.ops.object.mode_set(mode="OBJECT")


def bone_axis_t(rig, first, second, point):
    head = rig.data.bones[first].head_local
    tail = rig.data.bones[second].tail_local
    axis = tail - head
    return max(0.0, min(1.0, (point - head).dot(axis) / axis.length_squared))


def to_def_groups(obj, rig):
    """Rename metarig weights to DEF bones, splitting Rigify's twist segments."""
    deform = {b.name for b in rig.data.bones if b.use_deform}

    def targets(name):
        """[(DEF bone, split)] for one source group; split = (first, second) twist pair."""
        if name.startswith("DEF-"):
            # MPFB ships some twist weights already named, with a third
            # segment the contract folds into the second.
            if name in deform:
                return [(name, None)]
            if name.endswith(".002") and name[:-4] + ".001" in deform:
                return [(name[:-4] + ".001", None)]
            return []
        first = f"DEF-{name}"
        if first not in deform:
            return []
        second = f"{first}.001"
        return [(first, (first, second))] if second in deform else [(first, None)]

    plan = {g.index: targets(g.name) for g in obj.vertex_groups}
    totals = {}
    for v in obj.data.vertices:
        for g in v.groups:
            for target, split in plan[g.group]:
                if g.weight <= 0.0:
                    continue
                if split is None:
                    parts = [(target, g.weight)]
                else:
                    t = bone_axis_t(rig, split[0], split[1], v.co)
                    s = max(0.0, min(1.0, (t - 0.3) / 0.4))
                    s = s * s * (3 - 2 * s)
                    parts = [(split[0], g.weight * (1 - s)), (split[1], g.weight * s)]
                for bone, weight in parts:
                    totals[(bone, v.index)] = totals.get((bone, v.index), 0.0) + weight
    obj.vertex_groups.clear()
    for (bone, vertex), weight in totals.items():
        group = obj.vertex_groups.get(bone) or obj.vertex_groups.new(name=bone)
        group.add([vertex], weight, "REPLACE")


def unsubdivide(obj, iterations):
    modifier = obj.modifiers.new("Unsubdivide", "DECIMATE")
    modifier.decimate_type = "UNSUBDIV"
    modifier.iterations = iterations
    select_only(obj)
    bpy.ops.object.modifier_move_to_index(modifier=modifier.name, index=0)
    bpy.ops.object.modifier_apply(modifier=modifier.name)


def drape(bm, fixed, iterations):
    """Relax cloth over the body: smooths muscle detail out of shirts and
    shorts so they read as normal clothes, not spandex. Outline stays put."""
    free = [v for v in bm.verts if v not in fixed]
    bm.normal_update()
    start = {v: (v.co.copy(), v.normal.copy()) for v in free}
    for _ in range(iterations):
        bmesh.ops.smooth_vert(bm, verts=free, factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
        # Cloth bridges the grooves between muscles but never sinks under
        # the skin: anything smoothed inwards is lifted back to the surface.
        for v in free:
            origin, normal = start[v]
            depth = (v.co - origin).dot(normal)
            if depth < 0.0:
                v.co -= normal * depth


def protect_face(body, eye, keep=0.3):
    """Spend more of the body's triangle budget on the face (decimation weight)."""
    seam = body.vertex_groups.get(SEAM) or body.vertex_groups.new(name=SEAM)
    weights = bone_weights(body)
    for v, w in zip(body.data.vertices, weights):
        current = next((g.weight for g in v.groups if g.group == seam.index), 1.0)
        on_face = (w.get("spine.005", 0.0) >= 0.5 and v.co.y < eye.y + 0.07
                   and eye.z - 0.16 <= v.co.z <= eye.z + 0.07)
        seam.add([v.index], current * (keep if on_face else 1.0), "REPLACE")


def decimate(obj, tris):
    current = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    if current <= tris:
        return
    modifier = obj.modifiers.new("Decimate", "DECIMATE")
    modifier.ratio = tris / current
    modifier.use_symmetry = True
    modifier.symmetry_axis = "X"
    if obj.vertex_groups.get(SEAM):
        modifier.vertex_group = SEAM
    select_only(obj)
    bpy.ops.object.modifier_move_to_index(modifier=modifier.name, index=0)
    bpy.ops.object.modifier_apply(modifier=modifier.name)


def fill_unweighted(obj, bone_names=None):
    """Give vertices with no bone weight the weights of the nearest weighted vertex."""
    from mathutils.kdtree import KDTree
    names = {g.index: g.name for g in obj.vertex_groups}
    valid = {i for i, n in names.items() if bone_names is None or n in bone_names}

    def live(v):
        return [g for g in v.groups if g.group in valid and g.weight > 0.0]

    weighted = [v for v in obj.data.vertices if live(v)]
    tree = KDTree(len(weighted))
    for i, v in enumerate(weighted):
        tree.insert(v.co, i)
    tree.balance()
    for v in obj.data.vertices:
        if not live(v):
            for g in live(weighted[tree.find(v.co)[1]]):
                obj.vertex_groups[g.group].add([v.index], g.weight, "REPLACE")


def copy_weights(target, source):
    """Replace target's weights with those of the nearest source vertex."""
    from mathutils.kdtree import KDTree
    tree = KDTree(len(source.data.vertices))
    for v in source.data.vertices:
        tree.insert(v.co, v.index)
    tree.balance()
    names = {g.index: g.name for g in source.vertex_groups}
    target.vertex_groups.clear()
    for v in target.data.vertices:
        nearest = source.data.vertices[tree.find(v.co)[1]]
        for g in nearest.groups:
            name = names[g.group]
            group = target.vertex_groups.get(name) or target.vertex_groups.new(name=name)
            group.add([v.index], g.weight, "REPLACE")


def clean_weights(obj):
    select_only(obj)
    bpy.ops.object.vertex_group_limit_total(group_select_mode="ALL", limit=4)
    bpy.ops.object.vertex_group_clean(group_select_mode="ALL", limit=0.01)
    fill_unweighted(obj)
    bpy.ops.object.vertex_group_normalize_all(group_select_mode="ALL", lock_active=False)


def push(obj, distance):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.normal_update()
    for v in bm.verts:
        v.co += v.normal * distance
    bm.to_mesh(obj.data)
    bm.free()


SEAM = "_seam"  # 0 on cut-line vertices: decimation keeps them so the parts weld back
PART = "part"  # face attribute: which part a face belongs to, for the per-part bake


def cut_skin_band(body, low, high, spec, name):
    """Split the skin between two clean plane cuts into its own clothing part.

    The part keeps the skin's topology and weights, so it bends exactly like
    the body; it is inflated away from the cut lines for cloth volume. With an
    overlap the hem is left free: the skin continues under it and the hem
    flares off the leg instead of being welded to it.
    """
    push, flare, overlap = spec["push"], spec.get("flare", 0.0), spec.get("overlap", 0.0)
    if spec.get("skin_under"):
        # Loose clothing: skin stays under it (same weights, so it never pokes
        # through) and closes the view up the hem; only the welded waistband,
        # where cloth and skin meet, drops it.
        overlap = high - low - 0.03
    cuts = [low, high] + ([low + overlap] if overlap else [])
    bm = bmesh.new()
    bm.from_mesh(body.data)
    for z in cuts:
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0.0, 0.0, z), plane_no=(0.0, 0.0, 1.0))
    bm.to_mesh(body.data)
    bm.free()

    part = body.copy()
    part.data = body.data.copy()
    part.name = part.data.name = name
    bpy.context.scene.collection.objects.link(part)
    for obj, inside in ((body, False), (part, True)):
        bottom = low if inside else low + overlap
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        in_band = lambda f: bottom - 1e-5 <= f.calc_center_median().z <= high + 1e-5  # noqa: E731
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if in_band(f) != inside], context="FACES")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        # Mark cut-line vertices before anything moves them: a flared hem is
        # pushed off its plane but must still keep its outline in decimation.
        on_cut = {v for v in bm.verts if min(abs(v.co.z - z) for z in cuts) < 1e-4}
        if inside:
            floor = min(v.co.z for v in bm.verts)
            if spec.get("hull"):
                # Shoes: each foot becomes its convex hull, so there are no
                # toes at all; the hull's top cap at the ankle cut is removed
                # to leave the opening the sock welds to.
                old_faces = bm.faces[:]
                for side in (1.0, -1.0):
                    foot = [v for v in bm.verts if v.co.x * side > 0.0]
                    bmesh.ops.convex_hull(bm, input=foot, use_existing_faces=False)
                bmesh.ops.delete(bm, geom=[f for f in old_faces if f.is_valid], context="FACES_ONLY")
                bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
                bmesh.ops.delete(bm, geom=[f for f in bm.faces if all(abs(v.co.z - high) < 1e-4 for v in f.verts)],
                                 context="FACES_ONLY")
                bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
            if spec.get("round"):
                top_ring = {v for v in bm.verts if abs(v.co.z - high) < 1e-4}
                free = [v for v in bm.verts if v not in top_ring]
                for _ in range(spec["round"]):
                    bmesh.ops.smooth_vert(bm, verts=free, factor=0.5,
                                          use_axis_x=True, use_axis_y=True, use_axis_z=False)
            if spec.get("drape"):
                drape(bm, on_cut, spec["drape"])
            bm.normal_update()
            for v in bm.verts:
                ramp = min(1.0, abs(v.co.z - high) / 0.025)
                if overlap:
                    hem = 1.0 - max(0.0, min(1.0, (v.co.z - low) / (high - low)))
                    offset = push * ramp + flare * hem * hem
                    if spec.get("bag"):  # gi trousers: wide at the knee, tucked at the boot
                        t = 1.0 - hem  # 0 at the boot, 1 at the waist
                        ease = lambda x: x * x * (3.0 - 2.0 * x)  # noqa: E731
                        offset += spec["bag"] * ease(min(1.0, t / 0.22)) * ease(min(1.0, (1.0 - t) / 0.12))
                else:
                    offset = push * min(ramp, abs(v.co.z - low) / 0.025)
                v.co += v.normal * offset
                if spec.get("flat_bottom"):
                    v.co.z = max(v.co.z, floor)  # soles stay on the ground
        bm.verts.index_update()
        cut_indices = {v.index for v in on_cut if v.is_valid}
        bm.to_mesh(obj.data)
        bm.free()
        # Earlier cuts already marked their seams on the body: keep those zeros.
        seam = obj.vertex_groups.get(SEAM) or obj.vertex_groups.new(name=SEAM)
        for v in obj.data.vertices:
            member = any(g.group == seam.index for g in v.groups)
            if v.index in cut_indices or not member:
                seam.add([v.index], 0.0 if v.index in cut_indices else 1.0, "REPLACE")
    return part


def smooth_region_edge(obj, faces, iterations):
    """Smooth the outline of a face region along itself (no sawtooth hairline).

    Runs on the shared mesh before the split, so both sides keep one outline.
    """
    mesh = obj.data
    inside = [False] * len(mesh.polygons)
    for index in faces:
        inside[index] = True
    edge_faces = {}
    for polygon in mesh.polygons:
        for key in polygon.edge_keys:
            edge_faces.setdefault(key, []).append(inside[polygon.index])
    neighbours = {}
    for (a, b), flags in edge_faces.items():
        if len(flags) == 2 and flags[0] != flags[1]:
            neighbours.setdefault(a, []).append(b)
            neighbours.setdefault(b, []).append(a)
    for _ in range(iterations):
        moved = {v: mesh.vertices[v].co * 0.5 + sum((mesh.vertices[n].co for n in ns), Vector()) * (0.5 / len(ns))
                 for v, ns in neighbours.items() if len(ns) == 2}
        for v, co in moved.items():
            mesh.vertices[v].co = co


BONES = ("spine", "spine.001", "spine.002", "spine.003", "spine.004", "spine.005",
         "breast.L", "breast.R", "shoulder.L", "shoulder.R", "upper_arm.L", "upper_arm.R",
         "forearm.L", "forearm.R", "pelvis.L", "pelvis.R", "thigh.L", "thigh.R")
TORSO = ("spine", "spine.001", "spine.002", "spine.003", "breast.L", "breast.R", "pelvis.L", "pelvis.R")


def bone_weights(obj, fold=False):
    """Per vertex: {metarig bone: weight} for the bones region tests read.

    fold: also count MPFB's ready-named twist groups (DEF-forearm.L.001 ...)
    under their bone, which long sleeves need to reach the wrist."""
    names = {g.index: g.name for g in obj.vertex_groups}

    def bone(name):
        if fold and name.startswith("DEF-"):
            name = name[4:]
            for suffix in (".001", ".002"):
                if name.endswith(suffix) and name[:-4] in BONES:
                    name = name[:-4]
        return name if name in BONES else None

    weights = []
    for v in obj.data.vertices:
        w = {}
        for g in v.groups:
            b = bone(names[g.group])
            if b:
                w[b] = w.get(b, 0.0) + g.weight
        weights.append(w)
    return weights


def cut_region(body, inside, offset, ramp, name, edge_smooth=4, drape_steps=0, fold=False):
    """Cut the faces whose vertices all pass inside(v, weights) into a part.

    Like the clothing bands: the outline is smoothed on the shared mesh, stays
    welded, and the part is pushed out by offset(v) along the normal, easing
    in over `ramp` metres from the outline. No skin is left underneath.
    """
    weights = bone_weights(body, fold)
    mesh = body.data
    # Majority of corners, not all: MPFB's chest and shoulder faces are large,
    # and an all-corners rule drops narrow regions such as tank straps.
    from types import SimpleNamespace
    chosen = set()
    for poly in mesh.polygons:
        merged = {}
        for i in poly.vertices:
            for bone, w in weights[i].items():
                merged[bone] = merged.get(bone, 0.0) + w / len(poly.vertices)
        centre = SimpleNamespace(co=poly.center.copy(), normal=poly.normal.copy())
        if inside(centre, merged):
            chosen.add(poly.index)
    if not chosen:
        raise RuntimeError(f"{name}: region selects no faces")
    print(f"REMAKE_PART {name} faces={len(chosen)}")
    smooth_region_edge(body, chosen, edge_smooth)
    part = body.copy()
    part.data = body.data.copy()
    part.name = part.data.name = name
    bpy.context.scene.collection.objects.link(part)
    from mathutils.kdtree import KDTree
    for obj, keep_inside in ((body, False), (part, True)):
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        bm.faces.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if (f.index in chosen) != keep_inside], context="FACES")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        edge = [v.co.copy() for v in bm.verts if any(e.is_boundary for e in v.link_edges)]
        if keep_inside:
            if drape_steps:
                drape(bm, {v for v in bm.verts if any(e.is_boundary for e in v.link_edges)}, drape_steps)
            tree = KDTree(len(edge))
            for i, co in enumerate(edge):
                tree.insert(co, i)
            tree.balance()
            bm.normal_update()
            for v in bm.verts:
                eased = min(1.0, tree.find(v.co)[2] / ramp) if ramp > 0 else 1.0
                off = offset(v)
                v.co += (off if isinstance(off, Vector) else v.normal * off) * eased
        bm.to_mesh(obj.data)
        bm.free()
        # The outline must survive decimation on both sides to weld back.
        seam = obj.vertex_groups.get(SEAM) or obj.vertex_groups.new(name=SEAM)
        positions = {(round(c.x, 5), round(c.y, 5), round(c.z, 5)) for c in edge}
        for v in obj.data.vertices:
            key = (round(v.co.x, 5), round(v.co.y, 5), round(v.co.z, 5))
            member = any(g.group == seam.index for g in v.groups)
            if key in positions:
                seam.add([v.index], 0.0, "REPLACE")
            elif not member:
                seam.add([v.index], 1.0, "REPLACE")
    return part


def region_part(body, spec, eye, marks, rig, name):
    """Hair, beards, cap crowns, headbands and tops as region cuts (see RECIPES)."""
    kind, front_y = spec["region"], eye.y
    head = lambda w: w.get("spine.005", 0.0)  # noqa: E731
    neck = lambda w: w.get("spine.004", 0.0)  # noqa: E731

    if kind == "scalp":
        def inside(v, w):
            if head(w) >= 0.5 and v.co.z >= eye.z + spec["front"]:
                return True  # crown and forehead hairline
            if head(w) >= 0.5 and v.co.y >= front_y + spec["temple"] and v.co.z >= eye.z + spec["sides"]:
                return True  # sides, down to the ears
            behind = v.co.y >= front_y + spec["behind"]
            return behind and head(w) + neck(w) >= 0.5 and v.co.z >= eye.z + spec["back"]

        def offset(v):
            # Volume on the upward-facing crown only, so the sides fall.
            top = max(0.0, v.normal.z) * max(0.0, min(1.0, (v.co.z - eye.z) / 0.12))
            wave = spec.get("wave", 0.0) * math.sin(v.co.z * 38.0 + v.co.x * 22.0) * math.cos(v.co.y * 30.0)
            spikes = spec.get("spikes", 0.0) * max(0.0, math.sin(v.co.x * 95.0) * math.sin(v.co.y * 85.0 + v.co.z * 40.0))
            part = 0.012 * top * math.exp(-((v.co.x - spec.get("part_x", 9.0)) / 0.008) ** 2)
            front = max(0.0, min(1.0, (front_y + 0.11 - v.co.y) / 0.11)) if spec.get("quiff") else 1.0
            along = spec["push"] + spec.get("volume", 0.0) * top * (0.35 + 0.65 * front) + wave + spikes * top - part
            if "ssj" in spec:
                return v.normal * along + ssj_spike(v.co, spec["ssj"], eye)
            if "sweep" not in spec:
                return along
            # Swept-back styles: the crown and fringe lift up and back.
            sweep_y, sweep_z = spec["sweep"]
            return v.normal * along + Vector((0.0, sweep_y, sweep_z)) * top * front
    elif kind == "band_head":
        # Headbands and cap crowns: head skin between two heights. "tilt"
        # lowers the line towards the back (a cap sits low on the nape) and
        # "dome" raises the crown into a rounded cap shape.
        def inside(v, w):
            low = eye.z + spec["low"] - spec.get("tilt", 0.0) * max(0.0, v.co.y - front_y)
            return head(w) >= 0.5 and low <= v.co.z <= eye.z + spec["high"]

        def offset(v):
            return spec["push"] + spec.get("dome", 0.0) * max(0.0, v.normal.z)
    elif kind == "beard":
        def top(x):
            # Upper edge: under the nose in the middle (moustache), then a
            # cheek line rising at an angle from the mouth corner to the
            # sideburn, instead of one flat line across the face.
            (x0, z0), (x1, z1) = spec["cheek_line"]
            if abs(x) <= x0:
                return eye.z + spec["under_nose"]
            t = min(1.0, (abs(x) - x0) / (x1 - x0))
            return eye.z + z0 + (z1 - z0) * t

        def inside(v, w):
            if head(w) + neck(w) < 0.5 or v.co.y > front_y + spec["depth"]:
                return False
            if not eye.z - spec["down"] <= v.co.z <= top(v.co.x):
                return False
            lips = abs(v.co.x) < 0.028 and eye.z - 0.095 <= v.co.z <= eye.z - 0.07 and v.co.y < front_y + 0.02
            return not lips

        def offset(v):
            # Fuller on the jaw and chin than on the cheeks.
            low = max(0.0, min(1.0, (top(v.co.x) - v.co.z) / 0.05))
            return spec["push"] * (0.5 + 0.5 * low)
    elif kind == "cuff":
        wrist = abs(rig.data.bones["DEF-hand.L"].head_local.x)
        x0, x1 = wrist - spec["length"], wrist - spec.get("gap", 0.005)
        bm = bmesh.new()
        bm.from_mesh(body.data)
        for x in (x0, x1, -x0, -x1):
            faces = [f for f in bm.faces if abs(f.calc_center_median().x) > wrist - 0.25]
            geom = list({e for f in faces for e in f.edges}) + faces + list({v for f in faces for v in f.verts})
            bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(x, 0.0, 0.0), plane_no=(1.0, 0.0, 0.0))
        bm.to_mesh(body.data)
        bm.free()

        shoulder_z = rig.data.bones["DEF-upper_arm.L"].head_local.z

        def inside(v, w):
            # Position only: the arms are level in the T-pose, nothing else
            # reaches this far out (MPFB names its forearm twist weights apart).
            return x0 <= abs(v.co.x) <= x1 and abs(v.co.z - shoulder_z) < 0.15

        def offset(v):
            # Spiky cuff: points around the band, as in the reference.
            around = math.atan2(v.co.z - eye.z, v.co.y)
            return spec["push"] + spec.get("spikes", 0.0) * max(0.0, math.sin(around * 4.0))
    elif kind in ("tank", "tee"):
        waist = height_of(spec["waist"], marks)
        neck_base = rig.data.bones["DEF-spine.004"].head_local.z
        arm = {s: (rig.data.bones[f"DEF-upper_arm.{s}"].head_local, rig.data.bones[f"DEF-forearm.{s}"].head_local)
               for s in ("L", "R")}

        armpit = rig.data.bones["DEF-upper_arm.L"].head_local.z - spec.get("armpit", 0.1)
        wrist = abs(rig.data.bones["DEF-hand.L"].head_local.x)
        bm = bmesh.new()
        bm.from_mesh(body.data)
        upper = lambda f: f.calc_center_median().z > waist - 0.02 and abs(f.calc_center_median().x) < 0.3  # noqa: E731
        planes = [((0.0, 0.0, armpit), (0.0, 0.0, 1.0)), ((0.0, 0.0, neck_base - spec["scoop"]), (0.0, 0.0, 1.0)),
                  ((0.0, 0.0, neck_base - spec.get("back_scoop", spec["scoop"] * 0.4)), (0.0, 0.0, 1.0))]
        # Vertical cuts (neckline, straps) only down to the deepest opening:
        # below it they would slice the belly into slivers that shade in patches.
        opening = min(neck_base - max(spec["scoop"], spec.get("back_scoop", 0.0)), armpit) - 0.03
        for x in (spec["scoop_x"], spec.get("strap_x", spec["scoop_x"])):
            planes += [((x, 0.0, 0.0), (1.0, 0.0, 0.0)), ((-x, 0.0, 0.0), (1.0, 0.0, 0.0))]
        for co, no in planes:
            vertical = no[0] != 0.0
            faces = [f for f in bm.faces if upper(f) and (not vertical or f.calc_center_median().z > opening)]
            geom = list({e for f in faces for e in f.edges}) + faces + list({v for f in faces for v in f.verts})
            bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no)
        bm.to_mesh(body.data)
        bm.free()

        def inside(v, w):
            # MPFB weights the upper chest and traps partly to the neck, so
            # only the neck itself (narrow in x) is excluded by weight.
            if v.co.z < waist - 1e-5 or head(w) >= 0.3:
                return False
            if neck(w) + head(w) >= 0.4 and (abs(v.co.x) < spec["scoop_x"] or spec.get("neck_free")):
                return False  # neck_free: a collar covers the neckline, keep the whole neck skin
            if v.co.y < 0.0 and v.co.z > neck_base - spec["scoop"] and abs(v.co.x) < spec["scoop_x"] * (
                    min(1.0, (v.co.z - neck_base + spec["scoop"]) / spec["scoop"]) if spec.get("vee") else 1.0):
                return False  # front neckline (vee: narrows to a point, a jacket's open front)
            if v.co.y >= 0.0 and abs(v.co.x) < spec["scoop_x"] and v.co.z > neck_base - spec.get("back_scoop", spec["scoop"] * 0.4):
                return False  # back neckline, higher
            torso = sum(w.get(b, 0.0) for b in TORSO)
            arm_w = sum(w.get(b, 0.0) for b in ("upper_arm.L", "upper_arm.R", "forearm.L", "forearm.R"))
            if kind == "tank":
                # Straps over the traps; deep armholes from the armpit up. Above
                # the armpit the strap width alone decides (pec and trap skin
                # is partly weighted to the arm there).
                if abs(v.co.x) <= spec["strap_x"]:
                    return True
                if v.co.z > armpit:
                    return False
                return torso + w.get("shoulder.L", 0.0) + w.get("shoulder.R", 0.0) >= 0.5 and arm_w < 0.3
            if spec.get("full_hips") and v.co.z < armpit and arm_w < 0.3:
                return True
            if "sleeve_x" in spec and v.co.z > armpit - 0.02 and abs(v.co.x) > spec["scoop_x"]:
                # Short sleeves by position on the level T-posed arm, from the
                # shoulder joint out (MPFB's arm weights leave gaps).
                return abs(v.co.x) <= arm["L"][0].x + spec["sleeve_x"]
            if spec["sleeve"] > 1.0 and v.co.z > armpit - 0.02:
                # Long sleeves by position: the T-posed arm is level, so all
                # skin out to the wrist above the armpit is sleeve (weights
                # split around shoulder and elbow and left gaps).
                return abs(v.co.x) <= wrist - 0.005
            side = "L" if v.co.x > 0 else "R"
            start, end = arm[side]
            t = (v.co - start).dot(end - start) / (end - start).length_squared
            return torso + w.get(f"shoulder.{side}", 0.0) >= 0.5 or (arm_w >= 0.5 and t <= spec["sleeve"])

        def offset(v):
            return spec["push"]
    else:
        raise ValueError(kind)
    return cut_region(body, inside, offset, spec.get("ramp", 0.02), name, spec.get("edge_smooth", 4),
                      spec.get("drape", 0), fold=spec.get("fold", spec.get("sleeve", 0.0) > 1.0))


def brim(spec, eye, name):
    """Cap peak: a curved low-poly plate in front of the crown, on the head bone."""
    bm = bmesh.new()
    width, depth, rows, columns = spec["brim_width"], spec["brim_depth"], 4, 10
    grid = []
    for r in range(rows + 1):
        row = []
        for c in range(columns + 1):
            u = c / columns - 0.5
            d = depth * (r / rows) * math.cos(u * math.pi * 0.9)
            x = u * width * (1.0 - 0.25 * r / rows)
            y = eye.y - spec["brim_back"] - d
            z = eye.z + spec["brim_z"] - 0.012 * (r / rows) - 0.01 * (2 * u) ** 2
            row.append(bm.verts.new((x, y, z)))
        grid.append(row)
    for r in range(rows):
        for c in range(columns):
            bm.faces.new((grid[r][c], grid[r][c + 1], grid[r + 1][c + 1], grid[r + 1][c]))
    bmesh.ops.solidify(bm, geom=bm.faces[:], thickness=0.008)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    group = obj.vertex_groups.new(name="spine.005")
    group.add(list(range(len(mesh.vertices))), 1.0, "REPLACE")
    return obj


def cuff_object(bm, name):
    """Cuff spikes weighted to the forearm on their side."""
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    left, right = obj.vertex_groups.new(name="forearm.L"), obj.vertex_groups.new(name="forearm.R")
    for v in mesh.vertices:
        (left if v.co.x > 0 else right).add([v.index], 1.0, "REPLACE")
    return obj


def mesh_object(bm, name, color_group="spine.005"):
    """Finish a generated accessory: object on the head bone."""
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    group = obj.vertex_groups.new(name=color_group)
    group.add(list(range(len(mesh.vertices))), 1.0, "REPLACE")
    return obj


def head_points(objects):
    points = []
    for obj in objects:
        weights = bone_weights(obj)
        points += [v.co.copy() for v, w in zip(obj.data.vertices, weights) if w.get("spine.005", 0.0) >= 0.5]
    return points


def support(points, centre, direction, slab_axis, slab, cone=0.2):
    """Furthest extent of the head points from `centre` along `direction`,
    within a slab across `slab_axis` and a narrow cone around the direction."""
    best = None
    for p in points:
        offset = p - centre
        if abs(offset.dot(slab_axis)) > slab:
            continue
        flat = offset - slab_axis * offset.dot(slab_axis)
        if flat.length < 1e-6 or flat.normalized().dot(direction) < 1.0 - cone:
            continue
        reach = flat.dot(direction)
        best = reach if best is None else max(best, reach)
    return best


def ray_surface(objects, origin, direction):
    best = None
    depsgraph = bpy.context.evaluated_depsgraph_get()
    for obj in objects:
        hit, location, *_ = obj.evaluated_get(depsgraph).ray_cast(origin, direction)
        if hit and (best is None or (location - origin).length < (best - origin).length):
            best = location
    return best


def box_between(bm, start, end, width, height):
    """A thin rectangular bar between two points (temple arms, bridges)."""
    axis = (end - start).normalized()
    side = axis.cross(Vector((0.0, 0.0, 1.0)))
    side = (side if side.length > 1e-6 else Vector((1.0, 0.0, 0.0))).normalized() * (width / 2)
    up = side.cross(axis).normalized() * (height / 2)
    corners = [side + up, -side + up, -side - up, side - up]
    a = [bm.verts.new(start + c) for c in corners]
    b = [bm.verts.new(end + c) for c in corners]
    for k in range(4):
        bm.faces.new((a[k], a[(k + 1) % 4], b[(k + 1) % 4], b[k]))
    bm.faces.new(a[::-1])
    bm.faces.new(b)


def sunglasses(bm, spec, eye, objects, lens_only):
    """Sport sunglasses resting on the nose and hooked over the ears: rounded
    rectangular lenses in a rim, a bridge on the nose, temple arms along the
    head that bend down behind the ears."""
    points = head_points(objects)
    lens_z = eye.z + 0.004
    ahead = Vector((0.0, -1.0, 0.0))
    lens_x, rx, rz = spec["lens_x"], spec["lens_w"] / 2, spec["lens_h"] / 2

    def front_at(x, z):
        hit = ray_surface(objects, Vector((x, eye.y + 0.05, z)), ahead)
        return hit.y if hit else eye.y - 0.03

    for side in (-1.0, 1.0):
        cx = side * lens_x
        # Lens plane just clear of the brow and cheek under it.
        plane_y = min(front_at(cx + side * dx, lens_z + dz) for dx in (-rx, 0.0, rx) for dz in (-rz, rz)) - 0.006
        outline = []
        for k in range(16):
            t = 2 * math.pi * k / 16
            c, s_ = math.cos(t), math.sin(t)
            # Superellipse: rounded rectangle, a little deeper on the outside.
            x = cx + rx * math.copysign(abs(c) ** 0.6, c) * (1.0 + 0.08 * side * c)
            z = lens_z + rz * math.copysign(abs(s_) ** 0.6, s_)
            y = plane_y + spec["wrap"] * (x - cx) * side * max(0.0, (x - cx) * side) * 4.0
            outline.append(Vector((x, y, z)))
        centre = sum(outline, Vector()) / len(outline)
        if lens_only:
            hub = bm.verts.new(centre + Vector((0.0, -0.002, 0.0)))
            ring = [bm.verts.new(co) for co in outline]
            for k in range(16):
                bm.faces.new((hub, ring[k], ring[(k + 1) % 16]))
            continue
        # Rim: a flat ring around the lens, slightly in front of it.
        inner = [bm.verts.new(co + Vector((0.0, -0.003, 0.0))) for co in outline]
        outer = [bm.verts.new(centre + (co - centre) * 1.16 + Vector((0.0, -0.003, 0.0))) for co in outline]
        for k in range(16):
            bm.faces.new((inner[k], outer[k], outer[(k + 1) % 16], inner[(k + 1) % 16]))
        # Temple arm: from the outer rim, along the side of the head, to the
        # ear, then bending down behind it.
        hinge = outline[0] + Vector((side * rx * 0.2, -0.002, rz * 0.4))
        ear_y = eye.y + spec["arm_back"]
        side_x = support(points, Vector((0.0, ear_y, lens_z)), Vector((side, 0.0, 0.0)), Vector((0.0, 1.0, 0.0)), 0.02)
        ear = Vector((side * ((side_x or 0.08) + 0.003), ear_y, lens_z + 0.006))
        mid = Vector((hinge.x * 0.5 + ear.x * 0.5 + side * 0.006, (hinge.y + ear.y) / 2, (hinge.z + ear.z) / 2))
        tip = ear + Vector((-side * 0.004, 0.022, -0.03))
        for a, b in ((hinge, mid), (mid, ear), (ear, tip)):
            box_between(bm, a, b, 0.004, 0.007)
    if not lens_only:
        bridge_z = lens_z + rz * 0.55
        nose = ray_surface(objects, Vector((0.0, eye.y + 0.05, bridge_z)), ahead)
        bridge_y = (nose.y if nose else eye.y - 0.03) - 0.003
        box_between(bm, Vector((-(lens_x - rx * 0.9), bridge_y - 0.004, bridge_z)),
                    Vector((0.0, bridge_y, bridge_z + 0.004)), 0.004, 0.005)
        box_between(bm, Vector((0.0, bridge_y, bridge_z + 0.004)),
                    Vector((lens_x - rx * 0.9, bridge_y - 0.004, bridge_z)), 0.004, 0.005)
    else:
        bmesh.ops.solidify(bm, geom=bm.faces[:], thickness=0.002)


def headphones(bm, spec, eye, objects):
    """Over-ear headphones: cups pressed onto the sides of the head (over hair
    and cap) and a band that follows the head's outline over the crown."""
    points = head_points(objects)
    r, d = spec["cup_radius"], spec["cup_depth"]
    y0 = eye.y + spec["cup_back"]
    cup_z = eye.z + spec["cup_z"]
    slab_axis = Vector((0.0, 1.0, 0.0))
    centre = Vector((0.0, y0, cup_z))
    side_reach = max(support(points, centre, Vector((s, 0.0, 0.0)), slab_axis, 0.03) or 0.08 for s in (-1.0, 1.0))
    reach = side_reach + d * 0.5 - 0.004  # pressed on, not floating
    for side in (-1.0, 1.0):
        cup = Vector((side * reach, y0, cup_z))
        bmesh.ops.create_cone(bm, cap_ends=True, segments=14, radius1=r, radius2=r * 0.88, depth=d,
                              matrix=Matrix.Translation(cup) @ Matrix.Rotation(side * math.pi / 2, 4, "Y"))
        # Cushion ring on the head side, darker in the reference.
        bmesh.ops.create_cone(bm, cap_ends=False, segments=14, radius1=r * 1.04, radius2=r * 1.04, depth=d * 0.35,
                              matrix=Matrix.Translation(cup - Vector((side * d * 0.45, 0.0, 0.0)))
                              @ Matrix.Rotation(side * math.pi / 2, 4, "Y"))
    # Band: for each angle, rest on the outline of head and cap.
    steps, wide, thick = 18, spec["band_width"], 0.009
    rings = []
    for i in range(steps + 1):
        a = math.pi * i / steps
        direction = Vector((math.cos(a), 0.0, math.sin(a)))
        if i in (0, steps):
            radius = reach
        else:
            radius = (support(points, centre, direction, slab_axis, wide) or reach) + 0.004
            radius = max(radius, reach * 0.6)
        base = centre + direction * radius
        ring = [bm.verts.new(base + Vector((0.0, dy, 0.0)) + direction * dz)
                for dy, dz in ((-wide / 2, 0), (wide / 2, 0), (wide / 2, thick), (-wide / 2, thick))]
        rings.append(ring)
    for a, b in zip(rings, rings[1:]):
        for k in range(4):
            bm.faces.new((a[k], a[(k + 1) % 4], b[(k + 1) % 4], b[k]))


def hair_spikes(bm, spec, eye, objects):
    """Anime hair spikes: low-poly cones rooted in the scalp, standing up
    and out from the crown, a few falling forward as bangs."""
    points = head_points(objects)
    centre = Vector((0.0, eye.y + 0.075, eye.z + 0.035))
    up = Vector((0.0, 0.0, 1.0))
    for azimuth, elevation, length, radius in spec["spikes"]:
        a, e = math.radians(azimuth), math.radians(elevation)
        direction = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e))).normalized()
        surface = support(points, centre, direction, direction.cross(up).normalized()
                          if abs(direction.z) < 0.99 else Vector((1.0, 0.0, 0.0)), 0.03, cone=0.08)
        root = centre + direction * ((surface or 0.1) - 0.015)
        # Each spike leans up a little more than its root direction.
        lean = (direction + up * spec.get("lift", 0.4)).normalized()
        rotation = up.rotation_difference(lean).to_matrix().to_4x4()
        bmesh.ops.create_cone(bm, cap_ends=True, segments=5, radius1=radius, radius2=0.0, depth=length,
                              matrix=Matrix.Translation(root + lean * length * 0.5) @ rotation)


def anime_eyes(bm, spec, eye, objects, layer):
    """Large stylised anime eyes laid onto the face, one colour layer per call:
    "white" (almond sclera), "iris", "pupil", "shine" (highlight), "lash"
    (thick upper lash line) and "brow" (angled frown brows)."""
    ahead = Vector((0.0, -1.0, 0.0))
    w, h, gap = spec["eye_w"], spec["eye_h"], spec["eye_gap"]
    tilt = math.radians(spec.get("tilt", 10.0))  # outer corner raised

    def on_face(x, z, lift):
        # Eye sockets dip in: lay the eye on the brow/cheek level in front.
        hit = ray_surface(objects, Vector((x, eye.y + 0.06, z)), ahead)
        y = hit.y if hit else eye.y - 0.02
        return Vector((x, min(y, eye.y - spec.get("eye_front", 0.012)) - lift, z))

    def fan(points, lift):
        centre = sum(points, Vector()) / len(points)
        hub = bm.verts.new(on_face(centre.x, centre.z, lift))
        ring = [bm.verts.new(on_face(p.x, p.z, lift)) for p in points]
        for k in range(len(ring)):
            bm.faces.new((hub, ring[k], ring[(k + 1) % len(ring)]))

    def strip(points, thickness, lift):
        upper = [bm.verts.new(on_face(p.x, p.z + thickness / 2, lift)) for p in points]
        lower = [bm.verts.new(on_face(p.x, p.z - thickness / 2, lift)) for p in points]
        for k in range(len(points) - 1):
            bm.faces.new((lower[k], lower[k + 1], upper[k + 1], upper[k]))

    for side in (-1.0, 1.0):
        cx, cz = side * gap, eye.z + spec.get("eye_z", 0.0)

        def rotate(x, z):  # tilt about the eye centre, mirrored per side
            a = tilt * side
            return Vector((cx + x * math.cos(a) - z * math.sin(a), 0.0, cz + x * math.sin(a) + z * math.cos(a)))

        if layer == "white":
            # Almond: a tall upper arc and a flatter lower arc.
            pts = [rotate(w / 2 * math.cos(t), (h * 0.55 if math.sin(t) > 0 else h * 0.4) * math.sin(t))
                   for t in (2 * math.pi * k / 16 for k in range(16))]
            fan(pts, 0.002)
        elif layer in ("iris", "pupil", "shine"):
            r = {"iris": h * 0.42, "pupil": h * 0.2, "shine": h * 0.09}[layer]
            offset = {"iris": (side * -w * 0.06, 0.0), "pupil": (side * -w * 0.06, 0.0),
                      "shine": (side * -w * 0.06 + w * 0.08, h * 0.15)}[layer]
            lift = {"iris": 0.003, "pupil": 0.0038, "shine": 0.0045}[layer]
            pts = [rotate(offset[0] + r * math.cos(t), offset[1] + r * 1.15 * math.sin(t))
                   for t in (2 * math.pi * k / 12 for k in range(12))]
            fan(pts, lift)
        elif layer == "lash":
            pts = [rotate(w / 2 * math.cos(t) * 1.08, h * 0.55 * math.sin(t))
                   for t in (math.pi * k / 10 for k in range(11))]
            strip(pts, spec["lash"], 0.004)
        elif layer == "brow":
            # From above the inner corner, angled down towards the nose (frown).
            inner, outer = rotate(-side * w * 0.55, h * 1.1), rotate(side * w * 0.6, h * 1.6)
            inner.z -= spec.get("frown", 0.006)
            pts = [inner.lerp(outer, t) for t in (0.0, 0.33, 0.66, 1.0)]
            strip(pts, spec["brow"], 0.004)


def waist_spikes(bm, spec, objects):
    """Torn cloth flaps around a waist band (Goku's gi top over the sash):
    flat blades pointing out and up, uneven in length."""
    band = next(o for o in objects if o.name.endswith("_" + spec["on"]))
    zs = [v.co.z for v in band.data.vertices]
    z = (min(zs) + max(zs)) / 2 + spec.get("dz", 0.0)
    ring = [v.co for v in band.data.vertices if abs(v.co.z - z) < 0.015]
    centre = sum(ring, Vector()) / len(ring)
    up = Vector((0.0, 0.0, 1.0))
    for k in range(spec["count"]):
        a = 2 * math.pi * (k + 0.5 * (k % 2)) / spec["count"]
        radial = Vector((math.sin(a), -math.cos(a), 0.0))
        reach = max((p - centre).dot(radial) for p in ring)
        length = spec["length"] * (0.7 + 0.6 * ((k * 7) % 5) / 4)
        lean = (radial * spec.get("out", 0.6) + up).normalized()
        root = Vector((centre.x, centre.y, z)) + radial * (reach - 0.006)
        side = radial.cross(up).normalized() * spec["width"] / 2
        thin = radial * 0.004
        base = [root + side, root - side, root - thin, root + thin]
        tip = bm.verts.new(root + lean * length)
        verts = [bm.verts.new(co) for co in base]
        for i in range(4):
            bm.faces.new((verts[i], verts[(i + 1) % 4], tip))
        bm.faces.new(verts[::-1])


def cuff_spikes(bm, spec, objects):
    points = [v.co.copy() for obj in objects for v in obj.data.vertices]
    cuffs = next(o for o in objects if o.name.endswith("_" + spec["on"]))
    band_x = sum(abs(v.co.x) for v in cuffs.data.vertices) / len(cuffs.data.vertices)
    for side in (-1.0, 1.0):
        x = side * band_x
        ring = [p for p in points if abs(p.x - x) < 0.012]
        centre = sum(ring, Vector()) / len(ring)
        for k in range(spec["count"]):
            a = 2 * math.pi * k / spec["count"]
            radial = Vector((0.0, math.cos(a), math.sin(a)))
            reach = max((p - centre).dot(radial) for p in ring)
            lean = (radial + Vector((-side * spec["back"], 0.0, 0.0))).normalized()
            root = centre + radial * (reach - 0.004)
            rotation = Vector((0.0, 0.0, 1.0)).rotation_difference(lean).to_matrix().to_4x4()
            bmesh.ops.create_cone(bm, cap_ends=True, segments=4, radius1=spec["radius"], radius2=0.0,
                                  depth=spec["length"],
                                  matrix=Matrix.Translation(root + lean * spec["length"] * 0.5) @ rotation)


def anime_face(body, eye, spec):
    """Reshape the MPFB head into an anime face: flat eye area, tiny nose,
    small flat mouth, V-shaped jaw. Works on the game mesh before any cut."""
    weights = bone_weights(body)
    for v, w in zip(body.data.vertices, weights):
        if w.get("spine.005", 0.0) < 0.5 or v.co.y > eye.y + 0.07:
            continue
        x, z = abs(v.co.x), v.co.z
        if x < 0.026 and eye.z - 0.075 < z < eye.z - 0.005:  # nose: barely there
            v.co.y = max(v.co.y, eye.y - spec["nose_out"])
        if x < 0.032 and eye.z - 0.105 < z < eye.z - 0.062:  # mouth: flat and small
            v.co.y = max(v.co.y, eye.y - spec["lip_out"])
        if 0.008 < x < 0.062 and abs(z - eye.z) < 0.024:  # fill the sockets: flat eye plane
            v.co.y = min(v.co.y, eye.y - spec["socket_fill"])
        if z < eye.z - 0.035 and v.co.y < eye.y + 0.06:  # V-shaped jaw and pointed chin
            t = min(1.0, (eye.z - 0.035 - z) / 0.11)
            v.co.x *= 1.0 - spec["vjaw"] * t


def stylize_body(body, iterations):
    """Soften anatomy into the smooth masses of an anime body: the head,
    hands and feet keep their shape."""
    weights = bone_weights(body)
    keep_shape = {"spine.005"}
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bm.verts.ensure_lookup_table()
    free = [bm.verts[i] for i, w in enumerate(weights)
            if not any(b in keep_shape and x >= 0.3 for b, x in w.items())]
    for _ in range(iterations):
        bmesh.ops.smooth_laplacian_vert(bm, verts=free, lambda_factor=0.5, lambda_border=0.0,
                                        use_x=True, use_y=True, use_z=True, preserve_volume=True)
    bm.to_mesh(body.data)
    bm.free()


def anime_hair(bm, spec, eye, objects):
    """Anime hair as big flat locks laid out like the reference: a tall fan
    of flame-shaped blades over the crown, flames out to the sides and bangs
    falling over the forehead. Each lock is a lens-shaped blade (widest just
    above the root, sharp tip) that rises along its direction, flicks up
    (curl > 0) or falls (curl < 0), and sweeps sideways in x. Each lock keeps
    one palette colour, stored per face for paint_locks."""
    points = head_points(objects)
    centre = Vector((0.0, eye.y + 0.075, eye.z + 0.03))
    up, side = Vector((0.0, 0.0, 1.0)), Vector((1.0, 0.0, 0.0))
    colour_layer = bm.faces.layers.int.new("lock_colour")
    names = list(spec["palette"])
    steps, taper, bulge = spec.get("steps", 5), spec.get("taper", 0.8), spec.get("bulge", 0.35)
    for azimuth, elevation, length, width, curl, colour, sweep in spec["locks"]:
        a, e = math.radians(azimuth), math.radians(elevation)
        direction = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e))).normalized()
        across = direction.cross(up)
        across = (across if across.length > 1e-4 else side).normalized()
        surface = support(points, centre, direction, across.cross(direction).normalized(), 0.03, cone=0.1)
        root = centre + direction * ((surface or 0.1) - 0.025)
        thickness_axis = direction.cross(across).normalized()

        # sweep: sideways bend in x, or (x, y) to also lay bangs forward over the face
        sx, sy = sweep if isinstance(sweep, tuple) else (sweep, 0.0)
        bend = Vector((sx, sy, 0.0))

        def spine(t):
            return root + (direction * t + (up * curl + bend) * t * t) * length

        faces, rings = [], []
        for i in range(steps):
            t = i / steps
            centre_t = spine(t)
            profile = (1.0 - t) ** taper * (1.0 + bulge * math.sin(math.pi * t))
            half, thick = width * 0.5 * profile, width * spec.get("thick", 0.25) * profile
            rings.append([bm.verts.new(centre_t + across * half), bm.verts.new(centre_t + thickness_axis * thick),
                          bm.verts.new(centre_t - across * half), bm.verts.new(centre_t - thickness_axis * thick)])
        tip = bm.verts.new(spine(1.0))
        for r0, r1 in zip(rings, rings[1:]):
            faces += [bm.faces.new((r0[k], r0[(k + 1) % 4], r1[(k + 1) % 4], r1[k])) for k in range(4)]
        faces += [bm.faces.new((rings[-1][k], rings[-1][(k + 1) % 4], tip)) for k in range(4)]
        faces.append(bm.faces.new(rings[0][::-1]))
        for face in faces:
            face[colour_layer] = names.index(colour)


def paint_locks(obj, palette):
    """Repaint each anime hair lock in its own flat palette colour."""
    mesh = obj.data
    colours = [(*srgb_to_linear(c), 1.0) for c in palette.values()]
    index = mesh.attributes["lock_colour"].data
    albedo = mesh.color_attributes["Albedo"].data
    for polygon in mesh.polygons:
        for loop in polygon.loop_indices:
            albedo[loop].color = colours[index[polygon.index].value]


def prop(spec, eye, objects, name):
    """Head accessories built as low-poly meshes."""
    bm = bmesh.new()
    if spec["prop"] == "anime_hair":
        anime_hair(bm, spec, eye, objects)
        return mesh_object(bm, name)
    if spec["prop"] == "waist_spikes":
        waist_spikes(bm, spec, objects)
        return mesh_object(bm, name, "spine.001")
    if spec["prop"] == "cuff_spikes":
        cuff_spikes(bm, spec, objects)
        return cuff_object(bm, name)
    if spec["prop"] == "anime_eyes":
        anime_eyes(bm, spec, eye, objects, spec["layer"])
    elif spec["prop"] == "hair_spikes":
        hair_spikes(bm, spec, eye, objects)
    elif spec["prop"] in ("sunglasses", "sunglass_lenses"):
        sunglasses(bm, spec, eye, objects, spec["prop"] == "sunglass_lenses")
    elif spec["prop"] == "headphones":
        headphones(bm, spec, eye, objects)
    else:
        raise ValueError(spec["prop"])
    return mesh_object(bm, name)


def hip_pack(spec, z, body, name):
    """Belt pouch resting on the front of the hips: a flattened low-poly
    pillow, skinned to the hips."""
    x = spec["x"]
    hit = ray_surface([body], Vector((x, -1.0, z)), Vector((0.0, 1.0, 0.0)))
    w, d, h = spec["size"]
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=10, v_segments=9, radius=1.0)
    for v in bm.verts:
        # Squared-off pillow: push the sphere towards a box, keep round edges.
        co = Vector([math.copysign(abs(c) ** 0.6, c) for c in v.co])
        v.co = Vector((co.x * w / 2, co.y * d / 2, co.z * h / 2))
    if "palette" in spec:  # zip across the front, near the top
        layer = bm.faces.layers.int.new("lock_colour")
        for face in bm.faces:
            c = face.calc_center_median()
            face[layer] = 1 if c.y < -0.15 * d and 0.08 * h < c.z < 0.3 * h and abs(c.x) < 0.42 * w else 0
    # Turn the pouch to follow the curve of the hip, then set it on the skin.
    bmesh.ops.rotate(bm, verts=bm.verts, cent=Vector(), matrix=Matrix.Rotation(math.atan2(x, 0.16), 3, "Z"))
    bmesh.ops.translate(bm, verts=bm.verts, vec=Vector((x, (hit.y if hit else -0.12) - d / 2 + 0.008, z)))
    return mesh_object(bm, name, "spine")


def zip_strips(spec, jacket, name):
    """Zip along both edges of an open jacket: a thin ribbon laid on the
    front opening's boundary edges, skinned like the jacket under it."""
    src = bmesh.new()
    src.from_mesh(jacket.data)
    src.normal_update()
    front = [v for v in src.verts if v.is_boundary and v.co.y < 0.0 and abs(v.co.x) < spec["max_x"]]
    top = max(v.co.z for v in front) - 0.05  # stop below the neckline under the collar
    bm = bmesh.new()
    for e in src.edges:
        a, b = e.verts
        along = b.co - a.co
        if not (e.is_boundary and abs(along.z) > abs(along.x) and
                all(v in front and v.co.z < top for v in e.verts)):
            continue
        quad = []
        for v in (a, b):
            side = v.normal.cross(along).normalized() * spec["width"]
            lift = v.normal * 0.003
            quad.append((v.co + lift + side, v.co + lift - side))
        (a0, a1), (b0, b1) = quad
        face = bm.faces.new([bm.verts.new(co) for co in (a0, b0, b1, a1)])
        face.normal_update()
        if face.normal.dot(a.normal) < 0.0:  # face outwards, like the jacket
            face.normal_flip()
    src.free()
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
    obj = mesh_object(bm, name, "spine.003")
    copy_weights(obj, jacket)
    return obj


def jacket_collar(spec, rig, objects, name):
    """Folded jacket collar: a band that stands up around the neck, folds
    over and lies on the shoulders, open at the front with pointed tips."""
    neck = rig.data.bones["DEF-spine.004"].head_local
    centre = Vector((0.0, neck.y, neck.z))
    opening = math.radians(spec["open"])
    up = Vector((0.0, 0.0, 1.0))
    steps = 10
    rows = []
    for k in range(steps + 1):
        theta = opening + (2 * math.pi - 2 * opening) * k / steps  # 0 is the front
        d = Vector((math.sin(theta), -math.cos(theta), 0.0))
        hit = ray_surface(objects, centre + d * 0.3, -d)
        root = hit if hit else centre + d * 0.06
        front = max(0.0, math.cos(theta))  # tips at the front get longer points
        width = spec["width"] + spec["tip"] * front ** 2
        rows.append([root + d * 0.006 - up * 0.004,
                     root + d * 0.012 + up * spec["stand"],
                     root + d * (0.03 + width * 0.4) + up * (spec["stand"] * 0.6),
                     root + d * (0.02 + width) - up * (0.025 + 0.03 * front)])
    bm = bmesh.new()
    verts = [[bm.verts.new(co) for co in row] for row in rows]
    for a, b in zip(verts, verts[1:]):
        for i in range(3):
            bm.faces.new((a[i], b[i], b[i + 1], a[i + 1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.solidify(bm, geom=bm.faces[:], thickness=0.006)
    return mesh_object(bm, name, "spine.003")


def drop_slivers(bm):
    """Collapse zero-area slivers that cut lines and decimation leave: each
    one loses its shortest edge."""
    for _ in range(4):
        slivers = [f for f in bm.faces if f.calc_area() < 1e-8]
        if not slivers:
            return
        edges = {min(f.edges, key=lambda e: e.calc_length()) for f in slivers}
        bmesh.ops.collapse(bm, edges=list(edges), uvs=False)


def eyeballs(human, centres, name):
    """MPFB's eye helper spheres as low-poly eyeballs on the head bone."""
    eyes = [extract(human, f"helper-{s}-eye", f"{name}_{s}") for s in ("l", "r")]
    select_only(*eyes)
    bpy.ops.object.join()
    return eyes[0]


def ssj_spike(co, ssj, eye):
    """Offset for anime spikes: tall cones around the crown, leaning outwards
    and up, the largest at the top and front."""
    centre = Vector((0.0, eye.y + 0.07, eye.z + 0.03))
    best = Vector()
    for k in range(ssj["count"]):
        azimuth = 2 * math.pi * k / ssj["count"] + 0.3
        elevation = 0.55 + 0.35 * ((k * 7) % 5) / 4
        direction = Vector((math.sin(azimuth) * math.cos(elevation), -math.cos(azimuth) * math.cos(elevation),
                            math.sin(elevation))).normalized()
        root = centre + direction * ssj["radius"]
        reach = math.exp(-((co - root).length / ssj["width"]) ** 2)
        lean = (direction + Vector((0.0, 0.0, 0.8))).normalized()
        offset = lean * ssj["length"] * reach * (0.7 + 0.3 * max(0.0, direction.z))
        if offset.length > best.length:
            best = offset
    return best


def height_of(mark, marks):
    landmark, value = mark
    if landmark == "hem":
        return marks["hip"] - (marks["hip"] - marks["knee"]) * value
    return marks[landmark] + value


def paint(obj, color, regions=()):
    """Flat albedo, then regions [(test(co, normal), colour)] painted over it.

    Tested per corner at its vertex, so region edges blend across a face
    instead of stepping along whole faces.
    """
    attribute = obj.data.color_attributes.new("Albedo", "BYTE_COLOR", "CORNER")
    base = (*srgb_to_linear(color), 1.0)
    painted = [(test, (*srgb_to_linear(c), 1.0)) for test, c in regions]
    mesh = obj.data
    for polygon in mesh.polygons:
        normal = polygon.normal
        for loop in polygon.loop_indices:
            co = mesh.vertices[mesh.loops[loop].vertex_index].co
            attribute.data[loop].color = next((c for test, c in painted if test(co, normal)), base)


def decal_regions(decals, eye, part=None):
    """Flat-colour decals: badges and prints (front rectangles), tattoos, goatees.

    rect:    {"x": (a, b), "z": (a, b) relative to the eyes, "side": "front"|"back"}.
    tattoo:  {"min_x", "max_x": |x| span of the sleeve on the T-posed arms}, an
             irregular pattern so it reads as ink, not a flat sleeve.
    goatee:  {"width", "top", "bottom"} around the chin, relative to the eyes.
    """
    regions = []
    for d in decals:
        if d["kind"] == "rect":
            facing = -1.0 if d.get("side", "front") == "front" else 1.0
            regions.append((lambda co, n, d=d, f=facing: n.y * f > 0.4 and d["x"][0] <= co.x <= d["x"][1]
                            and eye.z + d["z"][0] <= co.z <= eye.z + d["z"][1], d["color"]))
        elif d["kind"] == "tattoo":
            def ink(co, n, d=d):
                if not d["min_x"] <= abs(co.x) <= d.get("max_x", 9.0):
                    return False
                if not eye.z + d.get("min_z", -9.0) <= co.z <= eye.z + d.get("max_z", 9.0):
                    return False  # arms only (they are level with the shoulders in the T-pose)
                # Wavy bands wrapping the arm, broken by a second motif: reads
                # as a dark sleeve with clear shapes, not as smudges.
                around = math.atan2(co.z - (eye.z + (d["min_z"] + d["max_z"]) / 2), co.y)
                band = math.sin(abs(co.x) * 48.0 + math.sin(around * 3.0) * 1.6)
                motif = math.sin(abs(co.x) * 23.0) * math.cos(around * 5.0)
                return band > d.get("threshold", 0.35) or motif > 0.75
            regions.append((ink, d["color"]))
        elif d["kind"] == "heart":
            # Print: a heart in horizontal rainbow stripes on the chest front.
            cx, cz, size, stripes = d["x"], eye.z + d["z"], d["size"], d["colors"]

            def heart(co, n, band, d=d):
                if n.y > -0.3:
                    return False
                x, y = (co.x - cx) / size, (co.z - cz) / size
                if (x * x + y * y - 1.0) ** 3 - x * x * y ** 3 > 0.0:
                    return False
                row = int((1.0 - (y + 1.1) / 2.4) * len(stripes))
                return min(max(row, 0), len(stripes) - 1) == band
            for band, colour in enumerate(stripes):
                regions.append((lambda co, n, band=band: heart(co, n, band), colour))
        elif d["kind"] == "patch":
            # Torn patch (gi knees): a ragged ellipse on the front.
            regions.append((lambda co, n, d=d: n.y < -0.2 and (
                ((abs(co.x) - d["x"]) / d["rx"]) ** 2 + ((co.z - eye.z - d["z"]) / d["rz"]) ** 2
                < 1.0 + 0.35 * math.sin(co.x * 160.0 + co.z * 120.0)), d["color"]))
        elif d["kind"] == "frontstripe":
            # A vertical stripe down the front of each boot (or trouser leg).
            centre = sum(abs(v.co.x) for v in part.data.vertices) / len(part.data.vertices)
            regions.append((lambda co, n, d=d, c=centre: n.y < -0.25 and abs(abs(co.x) - c) <= d["width"]
                            and d["z"][0] <= co.z <= d["z"][1], d["color"]))
        elif d["kind"] == "zband":
            # A horizontal band at absolute heights (boot laces, rope ties).
            regions.append((lambda co, n, d=d: d["z"][0] <= co.z <= d["z"][1], d["color"]))
        elif d["kind"] == "sectors":
            # Multicolour hair: colour by direction around the head.
            centre_y, colours = eye.y + 0.07, d["colors"]
            for k, colour in enumerate(colours):
                regions.append((lambda co, n, k=k, count=len(colours): int(
                    ((math.atan2(co.x, co.y - centre_y) + math.pi) / (2 * math.pi) * count
                     + 0.6 * math.sin(co.z * 40.0)) % count) == k, colour))
        elif d["kind"] == "pattern":
            # Printed fabric (leafy shorts): blotches from crossed sine waves.
            regions.append((lambda co, n, d=d, k=d["scale"]: math.sin(co.x * k + 2.0 * math.sin(co.z * k * 1.3))
                            * math.sin(co.z * k * 0.9 + co.y * k * 0.7 + math.sin(co.x * k * 1.7))
                            > d["threshold"], d["color"]))
        elif d["kind"] == "goatee":
            regions.append((lambda co, n, d=d: abs(co.x) <= d["width"] and eye.z + d["bottom"] <= co.z
                            <= eye.z + d["top"] and n.y < -0.3 and co.y < eye.y + 0.02, d["color"]))
    return regions


def face_regions(eye, spec):
    """Brows and lips as flat colours, placed from the eye midpoint (character faces -Y)."""
    def brow(co, n):
        return (eye.z + 0.014 <= co.z <= eye.z + 0.030 and 0.010 <= abs(co.x) <= 0.052
                and n.y < -0.4 and co.y < eye.y + 0.03)

    def lips(co, n):
        return (eye.z - 0.085 <= co.z <= eye.z - 0.068 and abs(co.x) <= 0.024
                and n.y < -0.5 and co.y < eye.y + 0.01)
    return [(brow, spec["brows"]), (lips, spec["lips"])]


def dilate(rgb, filled, steps):
    """Grow baked islands outwards by up to `steps` pixels, then fill the rest
    with the average colour, so filtering at island edges never reads black."""
    rgb, filled = rgb.copy(), filled.copy()
    for _ in range(steps):
        grown = np.zeros_like(rgb)
        count = np.zeros(filled.shape, dtype=np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            shifted = np.roll(np.roll(filled, dy, 0), dx, 1)
            grown += np.roll(np.roll(rgb, dy, 0), dx, 1) * shifted[..., None]
            count += shifted
        new = (~filled) & (count > 0)
        rgb[new] = grown[new] / count[new][:, None]
        filled = filled | new
    rgb[~filled] = rgb[filled].mean(axis=0)
    return rgb


def texel_maps(lowpoly, emit_material, run):
    """Bake each texel's surface position, normal and part id: (pos, nrm, pid)."""
    mesh = lowpoly.data
    co = [v.co for v in mesh.vertices]
    low = Vector(tuple(min(c[i] for c in co) for i in range(3)))
    span = Vector(tuple(max(c[i] for c in co) - low[i] for i in range(3)))
    ids = mesh.attributes[PART].data
    layers = {}
    for name in ("Pos", "Nrm", "Pid"):
        layers[name] = mesh.color_attributes.new(name, "FLOAT_COLOR", "CORNER")
    corner = mesh.corner_normals
    for poly in mesh.polygons:
        for loop in poly.loop_indices:
            normal = corner[loop].vector
            c = co[mesh.loops[loop].vertex_index]
            layers["Pos"].data[loop].color = ((c.x - low.x) / span.x, (c.y - low.y) / span.y,
                                              (c.z - low.z) / span.z, 1.0)
            layers["Nrm"].data[loop].color = (normal.x * 0.5 + 0.5, normal.y * 0.5 + 0.5,
                                              normal.z * 0.5 + 0.5, 1.0)
            layers["Pid"].data[loop].color = (ids[poly.index].value / 64.0, 0.0, 0.0, 1.0)
    data_image = bpy.data.images.new("texel_data", TEXTURE_SIZE, TEXTURE_SIZE, alpha=True, float_buffer=True)
    data_image.colorspace_settings.name = "Non-Color"
    maps = {}
    for name in ("Pos", "Nrm", "Pid"):
        mesh.materials.clear()
        mesh.materials.append(emit_material(name))
        maps[name] = run("EMIT", lowpoly, into=data_image)
    mesh.materials.clear()
    pos = maps["Pos"][..., :3] * np.array(span) + np.array(low)
    nrm = maps["Nrm"][..., :3] * 2.0 - 1.0
    return pos, nrm, np.rint(maps["Pid"][..., 0] * 64.0).astype(int)


def draw_decals(albedo, inside, pixel_regions, maps):
    """Colour the texels whose surface point passes a decal region test:
    prints, badges and brows get texel-sharp edges."""
    all_pos, all_nrm, pid = maps
    for part_id, regions in pixel_regions.items():
        rows, cols = np.nonzero(inside & (pid == part_id))
        if not len(rows):
            continue
        pos, nrm = all_pos[rows, cols], all_nrm[rows, cols]
        painted = 0
        for k in range(len(rows)):
            point, normal = Vector(pos[k]), Vector(nrm[k])
            for test, colour in regions:
                if test(point, normal):
                    albedo[rows[k], cols[k], :3] = colour
                    painted += 1
                    break
        print(f"REMAKE_DECALS part={part_id} texels={len(rows)} painted={painted}")


def ink_lines(lowpoly, albedo, inside, spec):
    """Drawn-anime ink: a line along every welded boundary between two parts
    (sleeve and trouser hems, waistband, boot tops, hairline), drawn into the
    texture in UV space on both sides, its width given in metres."""
    mesh = lowpoly.data
    size = albedo.shape[0]
    part = mesh.attributes[PART].data
    uv = mesh.uv_layers.active.data
    faces_of = {}
    for poly in mesh.polygons:
        for key in poly.edge_keys:
            faces_of.setdefault(key, []).append(poly)
    ink = np.zeros(inside.shape, dtype=bool)
    for (a, b), polys in faces_of.items():
        if len(polys) != 2 or part[polys[0].index].value == part[polys[1].index].value:
            continue
        length = (mesh.vertices[a].co - mesh.vertices[b].co).length
        for poly in polys:
            ends = {mesh.loops[i].vertex_index: np.array(uv[i].uv) * size for i in poly.loop_indices}
            pa, pb = ends[a], ends[b]
            span = np.linalg.norm(pb - pa)
            radius = max(1.0, spec["line"] * span / max(length, 1e-6))
            steps = max(2, int(span / max(radius * 0.5, 0.5)))
            r = int(np.ceil(radius))
            oy, ox = np.mgrid[-r:r + 1, -r:r + 1]
            disk = (ox * ox + oy * oy) <= radius * radius
            oy, ox = oy[disk], ox[disk]
            for t in np.linspace(0.0, 1.0, steps):
                cx, cy = pa + (pb - pa) * t
                cols = np.clip(np.rint(cx + ox).astype(int), 0, size - 1)
                rows = np.clip(np.rint(cy + oy).astype(int), 0, size - 1)
                ink[rows, cols] = True
    ink &= inside
    albedo[ink, :3] = spec["color"]
    print(f"REMAKE_INK boundary_texels={int(ink.sum())}")


def bake_texture(lowpoly, detail, path: Path, pixel_regions=None, cavity_strength=1.0, ink=None):
    """Colour map (part colours times skin cavity) and a tangent-space normal map.

    Colours come from the game mesh's own painted corners. Cavity and normals
    are baked only onto the skin, from the full MPFB surface: muscle lines and
    face detail show without more triangles, and clothes never inherit the
    body shape under them.
    """
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 16
    size = TEXTURE_SIZE * TEXTURE_SIZE * 4
    # Colour passes bake into an sRGB byte image (as the PNG is stored); the
    # normal pass into a Non-Color one.
    image = bpy.data.images.new(lowpoly.name + "_bake", TEXTURE_SIZE, TEXTURE_SIZE, alpha=True)
    normal_image = bpy.data.images.new(lowpoly.name + "_bake_n", TEXTURE_SIZE, TEXTURE_SIZE, alpha=True)
    normal_image.colorspace_settings.name = "Non-Color"

    def emit_material(layer):
        mat = bpy.data.materials.new(layer)
        mat.use_nodes = True
        m_nodes, m_links = mat.node_tree.nodes, mat.node_tree.links
        m_nodes.clear()
        colour = m_nodes.new("ShaderNodeVertexColor")
        colour.layer_name = layer
        shader = m_nodes.new("ShaderNodeEmission")
        output = m_nodes.new("ShaderNodeOutputMaterial")
        m_links.new(colour.outputs["Color"], shader.inputs["Color"])
        m_links.new(shader.outputs["Emission"], output.inputs["Surface"])
        target = m_nodes.new("ShaderNodeTexImage")
        target.image = image
        m_nodes.active = target
        return mat

    def run(bake_type, target, sources=(), into=image, **settings):
        into.pixels.foreach_set(np.zeros(size, dtype=np.float32))
        for mat in target.data.materials:
            mat.node_tree.nodes.active.image = into
        select_only(target, *sources)
        bpy.context.view_layer.objects.active = target
        scene.render.bake.use_selected_to_active = bool(sources)
        scene.render.bake.cage_extrusion = 0.02
        scene.render.bake.max_ray_distance = 0.06
        bpy.ops.object.bake(type=bake_type, margin=0, **settings)
        values = np.empty(size, dtype=np.float32)
        into.pixels.foreach_get(values)
        return values.reshape(TEXTURE_SIZE, TEXTURE_SIZE, 4)

    # Colours: the painted corners of the game mesh itself.
    lowpoly.data.materials.clear()
    lowpoly.data.materials.append(emit_material("Albedo"))
    albedo = run("EMIT", lowpoly)
    inside = albedo[..., 3] > 0.5
    if pixel_regions or ink:
        maps = texel_maps(lowpoly, emit_material, run)
        draw_decals(albedo, inside, pixel_regions or {}, maps)
        if ink:
            # Cel shading: one hard shadow tone from a fixed key light.
            light = np.array(ink["light"]) / np.linalg.norm(ink["light"])
            shadow = inside & (maps[1] @ light < ink.get("threshold", 0.0))
            albedo[shadow, :3] *= np.array(ink["shadow"])
            ink_lines(lowpoly, albedo, inside, ink)

    # Skin only: cavity and normals from the detailed surface.
    detail.data.color_attributes.new("Cavity", "BYTE_COLOR", "POINT")
    detail.data.color_attributes.active_color = detail.data.color_attributes["Cavity"]
    select_only(detail)
    bpy.ops.object.mode_set(mode="VERTEX_PAINT")
    bpy.ops.paint.vertex_color_dirt(blur_strength=1.0, blur_iterations=1, clean_angle=3.14159,
                                    dirt_angle=0.0, dirt_only=False, normalize=True)
    bpy.ops.object.mode_set(mode="OBJECT")
    detail.data.materials.clear()
    detail.data.materials.append(emit_material("Cavity"))
    skin = lowpoly.copy()
    skin.data = lowpoly.data.copy()
    bpy.context.scene.collection.objects.link(skin)
    ids = skin.data.attributes[PART].data
    bm = bmesh.new()
    bm.from_mesh(skin.data)
    bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if ids[f.index].value != 0], context="FACES")
    bm.to_mesh(skin.data)
    bm.free()
    skin.data.materials.clear()
    skin.data.materials.append(emit_material("Cavity"))
    cavity = run("EMIT", skin, [detail])
    normal = (run("NORMAL", skin, [detail], into=normal_image, normal_space="TANGENT") if cavity_strength > 0
              else np.zeros_like(cavity))
    on_skin = cavity[..., 3] > 0.5
    bpy.data.objects.remove(skin, do_unlink=True)

    dirt = cavity[..., 0]
    low, high = np.percentile(dirt[on_skin], (4, 96))
    lit = np.clip((dirt - low) / max(high - low, 1e-4), 0.0, 1.0)
    shade = np.where(on_skin, 1.0 - cavity_strength * 0.55 * (1.0 - lit), 1.0)
    if ink and ink.get("muscle"):
        # Drawn muscle definition: the deepest grooves of the detailed body
        # become thin ink strokes on the skin.
        lines = on_skin & (lit < ink["muscle"]) & (maps[0][..., 2] < ink["neck_z"])  # not on the face
        albedo[lines, :3] = albedo[lines, :3] * 0.35 + np.array(ink["color"]) * 0.65
        print(f"REMAKE_INK muscle_texels={int(lines.sum())}")
    colour = dilate(albedo[..., :3] * shade[..., None], inside, 8)
    flat = np.array([0.5, 0.5, 1.0])
    use_normal = on_skin if cavity_strength > 0 else np.zeros_like(on_skin)
    normal_rgb = dilate(np.where(use_normal[..., None], normal[..., :3], flat), inside, 8)

    def save(rgb, file_path, colorspace):
        pixels = np.concatenate([rgb, np.ones((TEXTURE_SIZE, TEXTURE_SIZE, 1))], axis=2).astype(np.float32)
        out = bpy.data.images.new(file_path.stem, TEXTURE_SIZE, TEXTURE_SIZE, alpha=True)
        out.colorspace_settings.name = colorspace
        out.pixels.foreach_set(pixels.ravel())
        file_path.parent.mkdir(parents=True, exist_ok=True)
        out.filepath_raw = str(file_path)
        out.file_format = "PNG"
        out.save()

    normal_path = path.with_name(path.stem + "_n.png")
    save(colour, path, "sRGB")
    save(normal_rgb, normal_path, "Non-Color")

    # Final material: colour map as Base Color, normal map, matte.
    material = bpy.data.materials.new(lowpoly.name.split("_")[0] + "_remake")
    material.use_nodes = True
    nodes, links = material.node_tree.nodes, material.node_tree.links
    shader = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
    shader.inputs["Roughness"].default_value = 0.7
    base = nodes.new("ShaderNodeTexImage")
    base.image = bpy.data.images.load(str(path))
    bump = nodes.new("ShaderNodeTexImage")
    bump.image = bpy.data.images.load(str(normal_path))
    bump.image.colorspace_settings.name = "Non-Color"
    normal_map = nodes.new("ShaderNodeNormalMap")
    links.new(base.outputs["Color"], shader.inputs["Base Color"])
    links.new(bump.outputs["Color"], normal_map.inputs["Color"])
    links.new(normal_map.outputs["Normal"], shader.inputs["Normal"])
    return material


def build(name: str) -> Path:
    global TEXTURE_SIZE
    recipe = RECIPES[name]
    TEXTURE_SIZE = recipe.get("texture", 1024)  # Goku's drawn lines need 2048
    HS, TS, RS = mpfb()
    bpy.ops.wm.read_homefile(use_empty=True)
    addon_utils.enable("rigify", default_set=True)

    macros = TS.get_default_macro_info_dict()
    macros.update(recipe["macros"])
    human = HS.create_human(macro_detail_dict=macros)
    for target, weight in recipe.get("targets", {}).items():
        for full in ([f"l-{target[3:]}", f"r-{target[3:]}"] if target.startswith("lr-") else [target]):
            before = {k.name for k in human.data.shape_keys.key_blocks}
            TS.load_target(human, TS.target_full_path(full), weight=min(weight, 1.0))
            for key in human.data.shape_keys.key_blocks:
                if key.name not in before and weight > 1.0:  # overdrive past MPFB's range
                    key.slider_max = weight
                    key.value = weight
    # Bake the shape first: the rig is fitted to the mesh's base coordinates,
    # so unbaked targets (longer legs, bigger shoulders) would leave the
    # joints where the default body had them.
    TS.bake_targets(human)
    # Longer or shorter legs move the feet off the ground: stand on Z = 0.
    lowest = min(v.co.z for v in human.data.vertices)
    human.data.transform(Matrix.Translation((0.0, 0.0, -lowest)))
    meta = HS.add_builtin_rig(human, "rigify.human")
    # Unweighted vertices would stay behind when the arms are raised.
    fill_unweighted(human, {b.name for b in meta.data.bones})
    pose_tpose(meta)
    RS.apply_pose_as_rest_pose(meta)
    eye = group_center(human, ["joint-l-eye", "joint-r-eye"])

    eye_l = group_center(human, ["joint-l-eye"])
    eye_r = group_center(human, ["joint-r-eye"])
    parts = {"body": extract(human, "body", f"{name}_body"),
             "eyes": eyeballs(human, (eye_l, eye_r), f"{name}_eyes")}
    if "anime" in recipe:  # modelled anime eyes replace the eyeballs
        bpy.data.objects.remove(parts.pop("eyes"), do_unlink=True)
    bpy.data.objects.remove(human, do_unlink=True)

    # Unbind first: bone renames would otherwise rename the vertex groups too.
    for obj in parts.values():
        world = obj.matrix_world.copy()
        obj.modifiers.clear()
        obj.parent = None
        obj.matrix_world = world
    mapping = remake_rig.conform_metarig(meta)
    for obj in parts.values():
        remake_rig.conform_groups(obj, mapping)
    rig = remake_rig.generate_rig(meta)
    bpy.data.objects.remove(meta, do_unlink=True)

    # The full MPFB surface stays only as the bake source for detail; the game
    # mesh is MPFB's own animation topology un-subdivided twice: clean edge
    # loops, no collapse-decimation lumps.
    detail = parts["body"].copy()
    detail.data = parts["body"].data.copy()
    bpy.context.scene.collection.objects.link(detail)
    unsubdivide(parts["body"], recipe.get("unsubdivide", 2))
    if "anime" in recipe:
        anime_face(parts["body"], eye, recipe["anime"])
        stylize_body(parts["body"], recipe["anime"].get("stylize", 0))

    marks = {"floor": 0.0, "ankle": rig.data.bones["DEF-foot.L"].head_local.z,
             "navel": rig.data.bones["DEF-spine.001"].head_local.z,
             "knee": rig.data.bones["DEF-shin.L"].head_local.z,
             "hip": rig.data.bones["DEF-thigh.L"].head_local.z}
    for part, spec in recipe["parts"].items():
        part_name = f"{name}_{part}"
        if spec["source"] == "region":
            parts[part] = region_part(parts["body"], spec, eye, marks, rig, part_name)
        elif spec["source"] == "brim":
            parts[part] = brim(spec, eye, part_name)
        elif spec["source"] == "prop":
            parts[part] = prop(spec, eye, [o for k, o in parts.items() if k != "eyes"], part_name)
        elif spec["source"] == "zip":
            parts[part] = zip_strips(spec, parts[spec["on"]], part_name)
        elif spec["source"] == "collar":
            parts[part] = jacket_collar(spec, rig, [o for k, o in parts.items() if k != "eyes"], part_name)
        elif spec["source"] == "pack":
            parts[part] = hip_pack(spec, height_of(spec["at"], marks), parts["body"], part_name)
        else:
            parts[part] = cut_skin_band(parts["body"], height_of(spec["low"], marks),
                                        height_of(spec["high"], marks), spec, part_name)

    # Corners get the flat part colours; decals are drawn per pixel in the
    # texture (a corner copy smeared small prints into blobs across faces).
    region_sets = {"body": face_regions(eye, recipe["face"]) + decal_regions(recipe.get("decals", ()), eye)}
    paint(parts["body"], recipe["skin"])
    for part, spec in recipe["parts"].items():
        if "decimate" in spec:  # trim parts that sit mostly hidden (scalp under locks)
            if "decimate_beyond_x" in spec:
                # Only the sleeves: collapsing the torso leaves big uneven
                # triangles that shade in patches over the chest and belly.
                obj = parts[part]
                group = obj.vertex_groups.get(SEAM) or obj.vertex_groups.new(name=SEAM)
                torso = [v.index for v in obj.data.vertices if abs(v.co.x) < spec["decimate_beyond_x"]]
                group.add(torso, 0.0, "REPLACE")
            decimate(parts[part], spec["decimate"])
        for d in spec.get("decals", ()):
            if "z_mark" in d:  # absolute heights for bands, eye-relative for patches
                mark = d["z_mark"]
                d["z"] = (tuple(height_of(m, marks) for m in mark) if isinstance(mark[0], tuple)
                          else height_of(mark, marks) - eye.z)
        regions = decal_regions(spec.get("decals", ()), eye, parts[part])
        if "sole" in spec:
            sole_height, sole_color = spec["sole"]
            floor = min(v.co.z for v in parts[part].data.vertices)
            regions.append((lambda co, n, h=floor + sole_height: co.z <= h or n.z < -0.6, sole_color))
        paint(parts[part], spec["color"])
        if "palette" in spec:
            paint_locks(parts[part], spec["palette"])
        region_sets[part] = regions
    if "eyes" in parts:
        paint(parts["eyes"], (0.90, 0.88, 0.84), [
            (lambda co, n, c=c, k=k: (co - c).normalized().y < k, col)
            for c in (eye_l, eye_r)
            for k, col in ((-0.97, (0.03, 0.03, 0.03)), (-0.86, recipe["face"].get("iris", (0.30, 0.20, 0.12))))])
    return finish(name, recipe, parts, region_sets, detail, rig, eye)


def finish(name, recipe, parts, region_sets, detail, rig, eye, keep=()) -> Path:
    """Join the painted parts into one skinned body, bake, add LOD1 and the
    EyeAnchor, and save the .blend (shared by the human and dog builders).
    Meshes in keep survive the clean-up (the player's swappable parts)."""
    pixel_regions = {}
    for part_id, (key, obj) in enumerate(parts.items()):  # 0 is the skin
        ids = obj.data.attributes.new(PART, "INT", "FACE")
        ids.data.foreach_set("value", [part_id] * len(obj.data.polygons))
        if region_sets.get(key):
            pixel_regions[part_id] = region_sets[key]
    if "eyes" in parts:
        decimate(parts["eyes"], 240)

    body = parts["body"]
    select_only(body, *[obj for key, obj in parts.items() if key != "body"])
    bpy.ops.object.join()
    body.name = body.data.name = "Body"
    bm = bmesh.new()  # weld the clothing cut lines back onto the skin
    bm.from_mesh(body.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    drop_slivers(bm)
    bm.to_mesh(body.data)
    bm.free()
    for obj in (body, detail):
        obj.modifiers.clear()
        obj.parent = None
    to_def_groups(body, rig)
    clean_weights(body)
    select_only(body)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.006)
    bpy.ops.uv.pack_islands(rotate=True, margin=0.006)
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(55))

    texture = OUT_DIR / "textures" / f"{name}.png"
    ink = dict(recipe["ink"], neck_z=rig.data.bones["DEF-spine.004"].head_local.z) if "ink" in recipe else None
    material = bake_texture(body, detail, texture, pixel_regions, recipe.get("cavity", 1.0), ink)

    lod1 = body.copy()
    lod1.data = body.data.copy()
    lod1.name = lod1.data.name = "Body_LOD1"
    bpy.context.scene.collection.objects.link(lod1)
    decimate(lod1, 2200)
    bm = bmesh.new()
    bm.from_mesh(lod1.data)
    drop_slivers(bm)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(lod1.data)
    bm.free()
    clean_weights(lod1)

    floor = min((body.matrix_world @ v.co).z for v in body.data.vertices)
    for obj in (body, lod1):
        obj.data.transform(Matrix.Translation((0.0, 0.0, -floor)))
        obj.parent = rig
        obj.modifiers.new("Armature", "ARMATURE").object = rig
        obj.data.materials.clear()
        obj.data.materials.append(material)
    select_only(rig)  # bones follow the mesh down onto Z = 0
    bpy.ops.object.mode_set(mode="EDIT")
    # Set from stored positions: translate() per bone would move a connected
    # child's head twice (once with its parent's tail) and bend the limbs.
    shift = Vector((0.0, 0.0, -floor))
    rest = {b.name: (b.head.copy(), b.tail.copy()) for b in rig.data.edit_bones}
    for bone in rig.data.edit_bones:
        head, tail = rest[bone.name]
        bone.head, bone.tail = head + shift, tail + shift
    bpy.ops.object.mode_set(mode="OBJECT")

    anchor = bpy.data.objects.new("EyeAnchor", None)
    bpy.context.scene.collection.objects.link(anchor)
    origin = eye - Vector((0.0, 0.0, floor))
    depsgraph = bpy.context.evaluated_depsgraph_get()
    hit, location, *_ = body.evaluated_get(depsgraph).ray_cast(origin, Vector((0.0, -1.0, 0.0)))
    anchor.location = location if hit else origin
    bpy.context.view_layer.update()
    world = anchor.matrix_world.copy()
    anchor.parent = rig
    anchor.parent_type = "BONE"
    anchor.parent_bone = HEAD_BONE
    bpy.context.view_layer.update()
    anchor.matrix_world = world

    for obj in list(bpy.data.objects):
        if obj.type == "MESH" and obj not in (body, lod1, *keep):
            bpy.data.objects.remove(obj, do_unlink=True)
    output = OUT_DIR / f"{name}.blend"
    bpy.context.preferences.filepaths.save_version = 0  # the script is the source; no .blend1
    bpy.ops.wm.save_as_mainfile(filepath=str(output), compress=True)
    tris = sum(len(p.vertices) - 2 for p in body.data.polygons)
    print(f"REMAKE_BUILD_OK {name} tris={tris} lod1={sum(len(p.vertices) - 2 for p in lod1.data.polygons)} "
          f"floor={floor:.3f} eye={tuple(round(c, 3) for c in anchor.matrix_world.translation)} path={output}")
    return output


def remap(value, xs, ys):
    """Piecewise-linear map through the points (xs, ys), extended past the ends."""
    i = next((k for k in range(len(xs) - 2) if value <= xs[k + 1]), len(xs) - 2)
    return ys[i] + (ys[i + 1] - ys[i]) * (value - xs[i]) / (xs[i + 1] - xs[i])


def dog_metarig(spec):
    """The stock metarig re-proportioned to the dog: short legs and arms,
    small paws, a long head bone. Same bones, so the same 72 DEF bones."""
    meta = remake_rig.build_metarig()
    select_only(meta)
    bpy.ops.object.mode_set(mode="EDIT")
    bones = meta.data.edit_bones
    old = {"hip": bones["thigh.L"].head.z, "knee": bones["shin.L"].head.z, "ankle": bones["foot.L"].head.z,
           "leg_x": bones["thigh.L"].head.x, "spine": bones["spine"].head.z, "neck": bones["spine.004"].head.z,
           "head": bones["spine.005"].head.z, "arm_z": bones["upper_arm.L"].head.z,
           "shoulder_x": bones["upper_arm.L"].head.x, "elbow_x": bones["forearm.L"].head.x,
           "wrist_x": bones["hand.L"].head.x}

    def side(p):
        return 1.0 if p.x > 0 else -1.0

    def torso(p):
        return Vector((p.x * spec["width"], p.y * spec["depth"],
                       remap(p.z, (old["spine"], old["neck"], old["head"]), (spec["hip"], spec["neck"], spec["head"]))))

    def leg(p):
        return Vector((side(p) * spec["leg_x"] + (p.x - side(p) * old["leg_x"]) * 0.6, p.y * spec["depth"],
                       remap(p.z, (old["ankle"], old["knee"], old["hip"]), (spec["ankle"], spec["knee"], spec["hip"]))))

    def arm(p):
        x = remap(abs(p.x), (old["shoulder_x"], old["elbow_x"], old["wrist_x"]),
                  (spec["shoulder_x"], spec["elbow_x"], spec["wrist_x"]))
        return Vector((side(p) * x, p.y * spec["depth"], spec["arm_z"] + (p.z - old["arm_z"]) * 0.6))

    def scaled(base, anchor, factor):  # a paw or foot: shrunk about its root
        moved = base(anchor)
        return lambda p: moved + (p - anchor) * factor

    def crown(p):
        return Vector((0.0, torso(p).y, spec["crown"]))

    targets = {}
    for bone in bones:
        n, s = bone.name, bone.name[-1]
        if n.startswith(("thigh", "shin")):
            head = tail = leg
        elif n.startswith(("foot", "toe", "heel")):
            head = tail = scaled(leg, bones[f"foot.{s}"].head.copy(), spec["foot"])
        elif n.startswith(("upper_arm", "forearm")):
            head = tail = arm
        elif n.startswith(("hand", "palm", "f_", "thumb")):
            head = tail = scaled(arm, bones[f"hand.{s}"].head.copy(), spec["paw"])
        elif n.startswith("shoulder"):
            head, tail = torso, arm
        elif n == "spine.005":
            head, tail = torso, crown
        else:
            head = tail = torso
        targets[n] = (head(bone.head.copy()), tail(bone.tail.copy()))
    for n, (head, tail) in targets.items():
        bones[n].head, bones[n].tail = head, tail
    bpy.ops.object.mode_set(mode="OBJECT")
    return meta


def blob(bm, centre, radii, rotation=None, segments=(20, 12)):
    """Ellipsoid (optionally turned) added to bm; returns its vertices."""
    verts = bmesh.ops.create_uvsphere(bm, u_segments=segments[0], v_segments=segments[1], radius=1.0)["verts"]
    matrix = Matrix.Diagonal((*radii, 1.0))
    if rotation is not None:
        matrix = rotation.to_4x4() @ matrix
    bmesh.ops.transform(bm, matrix=Matrix.Translation(Vector(centre)) @ matrix, verts=verts)
    return verts


def limb(bm, start, end, r0, r1, segments=16):
    """Tapered capsule from start to end."""
    start, end = Vector(start), Vector(end)
    axis = end - start
    verts = bmesh.ops.create_cone(bm, cap_ends=True, segments=segments, radius1=r0, radius2=r1,
                                  depth=axis.length)["verts"]
    turn = Vector((0.0, 0.0, 1.0)).rotation_difference(axis.normalized()).to_matrix().to_4x4()
    bmesh.ops.transform(bm, matrix=Matrix.Translation((start + end) / 2) @ turn, verts=verts)
    blob(bm, start, (r0,) * 3, segments=(segments, 8))
    blob(bm, end, (r1,) * 3, segments=(segments, 8))


def dog_body(spec, name):
    """One watertight fur body: torso, big head with muzzle and jaw, floppy
    ears, short limbs and paws, voxel-remeshed so it skins as one piece. The
    open mouth is carved out afterwards."""
    rig = spec["rig"]
    bm = bmesh.new()
    blob(bm, (0.0, -0.01, 0.47), (0.21, 0.18, 0.17))  # round belly
    blob(bm, (0.0, 0.0, 0.63), (0.19, 0.15, 0.13))  # chest
    blob(bm, (0.0, -0.02, 0.80), (0.16, 0.14, 0.11))  # thick neck: the head sits on the body
    blob(bm, (0.0, 0.03, 1.14), (0.25, 0.23, 0.23))  # cranium
    blob(bm, (0.0, -0.07, 0.98), (0.22, 0.17, 0.14))  # cheeks
    blob(bm, (0.0, -0.20, 1.0), (0.11, 0.12, 0.075))  # muzzle
    blob(bm, (0.0, -0.16, 0.885), (0.09, 0.10, 0.045))  # lower jaw
    for x in (1.0, -1.0):
        tilt = Matrix.Rotation(math.radians(-50.0) * x, 3, "Y")
        blob(bm, (0.21 * x, 0.05, 1.22), (0.07, 0.045, 0.06), tilt)  # ear root
        blob(bm, (0.35 * x, 0.05, 1.12), (0.07, 0.03, 0.15), tilt)  # flap
        shoulder = Vector((rig["shoulder_x"] * x * 0.8, 0.0, rig["arm_z"]))
        wrist = Vector((rig["wrist_x"] * x, 0.0, rig["arm_z"]))
        limb(bm, shoulder, wrist, 0.075, 0.06)
        blob(bm, wrist + Vector((0.06 * x, -0.01, 0.0)), (0.07, 0.07, 0.065))  # paw
        hip = Vector((rig["leg_x"] * x, 0.0, rig["hip"] + 0.02))
        ankle = Vector((rig["leg_x"] * x * 1.08, 0.0, rig["ankle"] + 0.01))
        limb(bm, hip, ankle, 0.09, 0.075)
        blob(bm, (rig["leg_x"] * x * 1.08, -0.06, 0.05), (0.085, 0.13, 0.055))  # foot paw
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    body = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(body)
    select_only(body)
    mesh.remesh_voxel_size = 0.009
    bpy.ops.object.voxel_remesh()
    smooth = body.modifiers.new("Smooth", "SMOOTH")
    smooth.factor, smooth.iterations = 0.5, 10
    bpy.ops.object.modifier_apply(modifier=smooth.name)
    # Open mouth: carve under the muzzle, between the snout and the jaw.
    bm = bmesh.new()
    blob(bm, (0.0, -0.26, 0.925), (0.10, 0.12, 0.045))
    cutter_mesh = bpy.data.meshes.new("mouth_cut")
    bm.to_mesh(cutter_mesh)
    bm.free()
    cutter = bpy.data.objects.new("mouth_cut", cutter_mesh)
    bpy.context.scene.collection.objects.link(cutter)
    carve = body.modifiers.new("Mouth", "BOOLEAN")
    carve.operation, carve.solver, carve.object = "DIFFERENCE", "EXACT", cutter
    select_only(body)
    bpy.ops.object.modifier_apply(modifier=carve.name)
    bpy.data.objects.remove(cutter, do_unlink=True)
    return body


def dog_props(spec, cap_top):
    """Rigid accessories: eyes, nose, mouth inside and tongue, propeller and
    button on the cap (head bone), and the lollipop in the right paw."""
    c, eye, rig = spec["colors"], Vector(spec["eye"]), spec["rig"]
    out = {}

    def solid(name, group, build, colour, regions=()):
        bm = bmesh.new()
        build(bm)
        out[name] = (mesh_object(bm, f"jolly_dog_{name}", group), colour, list(regions))

    def eyeballs(bm):
        for x in (0.105, -0.105):
            blob(bm, (x, eye.y + 0.035, eye.z), (0.043, 0.035, 0.047), segments=(14, 9))
    solid("eyeballs", "spine.005", eyeballs, c["eye"],
          [(lambda co, n: n.y < -0.85 and n.x > 0.25 and n.z > 0.25, (0.95, 0.95, 0.95))])
    solid("nose", "spine.005", lambda bm: blob(bm, (0.0, -0.31, 1.045), (0.055, 0.04, 0.038), segments=(14, 9)),
          c["nose"])
    solid("mouth", "spine.005", lambda bm: blob(bm, (0.0, -0.19, 0.925), (0.09, 0.08, 0.04), segments=(14, 8)),
          c["mouth"])
    solid("tongue", "spine.005", lambda bm: blob(bm, (0.0, -0.27, 0.905), (0.055, 0.07, 0.02),
                                                  segments=(12, 8)), c["tongue"])

    def propeller(bm):
        limb(bm, (0.0, 0.03, cap_top - 0.01), (0.0, 0.03, cap_top + 0.09), 0.012, 0.012, 8)
        for x in (1.0, -1.0):
            blob(bm, (0.085 * x, 0.03, cap_top + 0.095), (0.08, 0.022, 0.008),
                 Matrix.Rotation(math.radians(12.0) * x, 3, "X"), segments=(10, 6))
    solid("propeller", "spine.005", propeller, c["cap_blue"],
          [(lambda co, n: abs(co.x) < 0.02, c["cap_yellow"])])
    solid("button", "spine.005", lambda bm: blob(bm, (0.0, 0.03, cap_top + 0.005), (0.03, 0.03, 0.018),
                                                  segments=(12, 6)), c["cap_yellow"])

    paw = Vector((-rig["wrist_x"] - 0.055, -0.01, rig["arm_z"]))
    candy_centre = paw + Vector((-0.03, -0.12, 0.33))
    spiral = c["candy"]

    def lollipop(bm):
        limb(bm, paw + Vector((0.0, 0.0, -0.05)), candy_centre - Vector((0.0, 0.0, 0.1)), 0.011, 0.011, 8)
        verts = bmesh.ops.create_cone(bm, cap_ends=True, segments=28, radius1=0.12, radius2=0.12,
                                      depth=0.035)["verts"]
        bmesh.ops.transform(bm, matrix=Matrix.Translation(candy_centre) @ Matrix.Rotation(math.radians(90.0), 4, "X"),
                            verts=verts)

    def band(co, k):  # rainbow spiral on the candy faces
        d = co - candy_centre
        r = math.hypot(d.x, d.z)
        turn = math.atan2(d.z, d.x) / (2 * math.pi) * len(spiral) + r * 26.0
        return r < 0.125 and int(turn % len(spiral)) == k
    solid("lollipop", "hand.R", lollipop, c["stick"],
          [(lambda co, n, k=k: abs(co.y - candy_centre.y) < 0.03 and band(co, k), col)
           for k, col in enumerate(spiral)])
    return out


def build_dog(name: str) -> Path:
    """Lollipop Larry: a dog-shaped body on the shared 72-DEF skeleton."""
    global TEXTURE_SIZE
    recipe = RECIPES[name]
    TEXTURE_SIZE = 1024
    bpy.ops.wm.read_homefile(use_empty=True)
    addon_utils.enable("rigify", default_set=True)
    meta = dog_metarig(recipe["rig"])
    rig = remake_rig.generate_rig(meta)
    bpy.data.objects.remove(meta, do_unlink=True)
    c, eye = recipe["colors"], Vector(recipe["eye"])

    body = dog_body(recipe, f"{name}_body")
    detail = body.copy()
    detail.data = body.data.copy()
    bpy.context.scene.collection.objects.link(detail)
    decimate(body, recipe["body_tris"])
    select_only(rig, body)
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    body.modifiers.clear()
    body.parent = None

    # Clothes and cap cut from the fur (they keep its weights), pushed out.
    def tee_inside(v, w):
        return 0.41 <= v.co.z <= 0.79 and abs(v.co.x) <= 0.27

    def cap_inside(v, w):
        return abs(v.co.x) <= 0.25 and v.co.z >= 1.21 - 0.3 * max(0.0, v.co.y - 0.02)
    # Shorts first: their clean top cut is the line the tee then starts from.
    shorts = cut_skin_band(body, 0.2, 0.43, {"push": 0.024, "flare": 0.025, "drape": 6, "skin_under": True},
                           f"{name}_shorts")
    bm = bmesh.new()  # a clean collar line: cut the fur along it before the tee takes the faces below
    bm.from_mesh(body.data)
    bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(0.0, 0.0, 0.79),
                           plane_no=(0.0, 0.0, 1.0))
    bm.to_mesh(body.data)
    bm.free()
    tee = cut_region(body, tee_inside, lambda v: 0.022, 0.015, f"{name}_tee", drape_steps=6)
    cap = cut_region(body, cap_inside, lambda v: 0.02 + 0.02 * max(0.0, v.normal.z), 0.012, f"{name}_cap")
    cap_top = max(v.co.z for v in cap.data.vertices)
    parts = {"body": body, "tee": tee, "shorts": shorts, "cap": cap}
    colours = {"tee": c["tee"], "shorts": c["shorts"], "cap": c["cap_blue"]}

    def ear(co):
        return abs(co.x) > 0.25 and co.z > 0.9

    def toes(co, n):  # three toe lines on the front of each paw
        lines = (abs(abs(co.x) - x0 - k) < 0.004 for x0, k in ((0.15, -0.028), (0.15, 0.0), (0.15, 0.028)))
        return co.z < 0.1 and n.y < -0.3 and any(lines)
    badge = Vector((0.0, 0.0, 0.58))

    def bone_icon(co):  # a white dog bone: a bar with a round knob at each corner
        x, z = abs(co.x - badge.x), co.z - badge.z
        return (x <= 0.04 and abs(z) <= 0.009) or (x - 0.04) ** 2 + (abs(z) - 0.012) ** 2 < 0.012 ** 2
    region_sets = {
        "body": [(lambda co, n: ear(co) and n.x * co.x < -0.3 * abs(co.x), c["ear_inner"]),
                 (lambda co, n: ear(co), c["ear"]),
                 # Tan patches around the eyes and over the brow; a white blaze between them.
                 (lambda co, n: not ear(co) and n.y < -0.1 and abs(co.x) > 0.03 and (
                     ((abs(co.x) - 0.12) / 0.10) ** 2 + ((co.z - 1.16) / 0.085) ** 2 < 1.0 or co.z > 1.2),
                  c["patch"]),
                 (lambda co, n: 0.82 < co.z < 1.08 and co.y < -0.04, c["face"]),
                 (toes, c["patch"])],
        "tee": [(lambda co, n: n.y < -0.4 and bone_icon(co), c["bone"]),
                (lambda co, n: n.y < -0.4 and (co.x - badge.x) ** 2 + (co.z - badge.z) ** 2 < 0.065 ** 2,
                 c["badge"])],
        "cap": [(lambda co, n: co.x > 0.15 and co.y > -0.05, c["cap_orange"]),
                (lambda co, n: co.x >= 0.0, c["cap_red"])],
    }
    paint(body, c["fur"], region_sets["body"])
    for key in ("tee", "shorts", "cap"):
        paint(parts[key], colours[key], region_sets.get(key, ()))
    for key, (obj, colour, regions) in dog_props(recipe, cap_top).items():
        parts[key] = obj
        paint(obj, colour, regions)
        region_sets[key] = regions
    return finish(name, recipe, parts, region_sets, detail, rig, eye)


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("character", choices=sorted(RECIPES))
    character = parser.parse_args(argv).character
    (build_dog if "dog" in RECIPES[character] else build)(character)
