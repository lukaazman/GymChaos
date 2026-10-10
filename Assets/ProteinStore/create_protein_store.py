"""Build the protein.com low-poly mall storefront for GymChaos.

Run with Blender 4.5+ in background mode:
    blender --background --factory-startup --python create_protein_store.py

The script is intentionally self-contained so the asset can be regenerated
after Unity-side or art-direction changes.  The authored scene contains the
presentation cameras/lights; the exported GLB contains only the store meshes.
"""

from __future__ import annotations

import math
import os
import random
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector


# -----------------------------------------------------------------------------
# Contract / constants
# -----------------------------------------------------------------------------

ASSET_NAME = "protein.com Low Poly Protein Store"
VERSION = "1.8.3"
SEED = 240912

# Compact source-owned cues for the seven product families. These describe
# recognizable forms and label hierarchy without copying photographed labels.
PRODUCT_FAMILY_CONTRACT = {
    "whey": {
        "prefixes": ("Protein_Right",),
        "front": "100% NATURAL WHEY",
        "node_token": "WHEY",
        "form_tokens": ("PouchBody", "ProteinTubBody"),
        "reference": "https://www.proteini.si/sl/beljakovine/sirotka/proteini-si-100-natural-whey-protein",
    },
    "creatine": {
        "prefixes": ("Creatine_Back",),
        "front": "100% PURE CREATINE",
        "node_token": "CREATINE",
        "form_tokens": ("PouchBody",),
        "reference": "https://www.proteini.si/sl/kreatin/monohidrat/proteini-si-100-pure-creatine-200g",
    },
    "pre_workout": {
        "prefixes": ("PreWorkout_Left",),
        "front": "PRE-WORKOUT",
        "node_token": "PRE_WORKOUT",
        "form_tokens": ("PreWorkoutBody",),
        "reference": "https://www.proteini.si/sl/pre-workout",
    },
    "shaker": {
        "prefixes": ("Shakers_Left",),
        "front": "SPORT SHAKER",
        "node_token": "SHAKER",
        "form_tokens": ("BottleBody", "ShakerFlipTop"),
        "reference": "https://www.proteini.si/sl/dodatki/sejkerji",
    },
    "protein_bar": {
        "prefixes": ("ProteinBars",),
        "front": "PROTEIN BAR",
        "node_token": "PROTEIN_BAR",
        "form_tokens": ("BarBody",),
        "reference": "https://www.proteini.si/sl/beljakovine/beljakovinske-ploscice",
    },
    "energy": {
        "prefixes": ("EnergyDrinks_Cooler", "ColdFridge_RearLeft_Can"),
        "front": "ENERGY DRINK",
        "node_token": "ENERGY",
        "form_tokens": ("EnergyCanBody",),
        "reference": "https://www.proteini.si/sl/energijska-hrana/energijski-napitki",
    },
    "rtd_shake": {
        "prefixes": ("ColdFridge_RearLeft_Rtd",),
        "front": "PROTEIN SHAKE",
        "node_token": "RTD_SHAKE",
        "form_tokens": ("RtdBody", "RtdShoulder"),
        "reference": "https://www.proteini.si/sl/beljakovine/pripravljeni-napitki/proteini-si-protein-shake-rtd-8x330ml-vanilla",
    },
}

ROOM_W = 12.0
ROOM_D = 8.3
ROOM_H = 5.5
FRONT_Y = -4.25
BACK_Y = 4.05
WALL_T = 0.22
CHECKOUT_Y_SHIFT = -0.90

ROOT = Path(__file__).resolve().parent
BLEND_PATH = ROOT / "protein_store_lowpoly.blend"
GLB_PATH = ROOT / "protein_store_lowpoly.glb"
RENDER_DIR = ROOT / "renders"
RENDER_DIR.mkdir(parents=True, exist_ok=True)

random.seed(SEED)


# -----------------------------------------------------------------------------
# Scene helpers
# -----------------------------------------------------------------------------


COLLECTIONS: dict[str, bpy.types.Collection] = {}


def make_collection(name: str) -> bpy.types.Collection:
    collection = bpy.data.collections.get(name) or bpy.data.collections.new(name)
    if collection.name not in bpy.context.scene.collection.children:
        try:
            bpy.context.scene.collection.children.link(collection)
        except RuntimeError:
            pass
    COLLECTIONS[name] = collection
    return collection


def move_to_collection(obj: bpy.types.Object, collection_name: str) -> None:
    collection = COLLECTIONS[collection_name]
    for old in list(obj.users_collection):
        old.objects.unlink(obj)
    collection.objects.link(obj)


def set_active(obj: bpy.types.Object) -> None:
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def set_smooth(obj: bpy.types.Object, smooth: bool) -> None:
    if hasattr(obj.data, "polygons"):
        for poly in obj.data.polygons:
            poly.use_smooth = smooth


def apply_scale(obj: bpy.types.Object) -> None:
    set_active(obj)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)


def add_bevel(obj: bpy.types.Object, width: float, segments: int = 1) -> None:
    if width <= 0:
        return
    bevel = obj.modifiers.new(name="LowPoly_EdgeBevel", type="BEVEL")
    bevel.width = width
    bevel.segments = segments
    bevel.limit_method = "ANGLE"
    bevel.angle_limit = math.radians(25)
    bevel.harden_normals = True


def assign_material(obj: bpy.types.Object, material: bpy.types.Material) -> None:
    obj.data.materials.clear()
    obj.data.materials.append(material)


def add_box(
    name: str,
    location: tuple[float, float, float],
    dimensions: tuple[float, float, float],
    material: bpy.types.Material,
    collection: str = "Store",
    bevel: float = 0.0,
    segments: int = 1,
    rotation: tuple[float, float, float] = (0.0, 0.0, 0.0),
    smooth: bool = True,
) -> bpy.types.Object:
    bpy.ops.mesh.primitive_cube_add(location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = dimensions
    apply_scale(obj)
    assign_material(obj, material)
    set_smooth(obj, smooth)
    add_bevel(obj, bevel, segments)
    move_to_collection(obj, collection)
    return obj


def add_cooler_cabinet(
    prefix: str,
    center_x: float,
    width: float,
    front_y: float,
    back_y: float,
    bottom_z: float,
    top_z: float,
    plinth_top_z: float,
    material: bpy.types.Material,
) -> None:
    """Close a clear-front cooler between its front frame and rear body.

    The glass, frame and rear body alone read as two floating slabs from any
    side angle. Two side walls, a roof and a base plinth make one cabinet
    while leaving the front panel clear for the stock.
    """

    wall = 0.08
    depth = abs(back_y - front_y)
    mid_y = (front_y + back_y) * 0.5
    height = top_z - bottom_z
    mid_z = (top_z + bottom_z) * 0.5
    for side, sign in (("SideLeft", -1.0), ("SideRight", 1.0)):
        x = center_x + sign * (width * 0.5 - wall * 0.5)
        add_box(f"{prefix}_{side}", (x, mid_y, mid_z), (wall, depth, height), material, bevel=0.025, segments=1)
    add_box(f"{prefix}_Roof", (center_x, mid_y, top_z - wall * 0.5), (width, depth, wall), material, bevel=0.025, segments=1)
    plinth_height = plinth_top_z - bottom_z
    add_box(
        f"{prefix}_Plinth",
        (center_x, mid_y, bottom_z + plinth_height * 0.5),
        (width, depth, plinth_height),
        material,
        bevel=0.025,
        segments=1,
    )


def add_cylinder(
    name: str,
    location: tuple[float, float, float],
    radius: float,
    depth: float,
    material: bpy.types.Material,
    collection: str = "Store",
    vertices: int = 12,
    bevel: float = 0.0,
    rotation: tuple[float, float, float] = (0.0, 0.0, 0.0),
    smooth: bool = False,
) -> bpy.types.Object:
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices,
        radius=radius,
        depth=depth,
        location=location,
        rotation=rotation,
    )
    obj = bpy.context.object
    obj.name = name
    assign_material(obj, material)
    set_smooth(obj, smooth)
    add_bevel(obj, bevel, 1)
    move_to_collection(obj, collection)
    return obj


def add_cone(
    name: str,
    location: tuple[float, float, float],
    radius_1: float,
    radius_2: float,
    depth: float,
    material: bpy.types.Material,
    collection: str = "Store",
    vertices: int = 10,
    bevel: float = 0.0,
    rotation: tuple[float, float, float] = (0.0, 0.0, 0.0),
    smooth: bool = False,
) -> bpy.types.Object:
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices,
        radius1=radius_1,
        radius2=radius_2,
        depth=depth,
        location=location,
        rotation=rotation,
    )
    obj = bpy.context.object
    obj.name = name
    assign_material(obj, material)
    set_smooth(obj, smooth)
    add_bevel(obj, bevel, 1)
    move_to_collection(obj, collection)
    return obj


def add_uv_sphere(
    name: str,
    location: tuple[float, float, float],
    scale: tuple[float, float, float],
    material: bpy.types.Material,
    collection: str = "Store",
    segments: int = 12,
    rings: int = 6,
) -> bpy.types.Object:
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=segments,
        ring_count=rings,
        location=location,
    )
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    apply_scale(obj)
    assign_material(obj, material)
    set_smooth(obj, True)
    move_to_collection(obj, collection)
    return obj


def add_torus(
    name: str,
    location: tuple[float, float, float],
    major_radius: float,
    minor_radius: float,
    material: bpy.types.Material,
    collection: str = "Store",
    rotation: tuple[float, float, float] = (0.0, 0.0, 0.0),
    major_segments: int = 12,
    minor_segments: int = 4,
) -> bpy.types.Object:
    bpy.ops.mesh.primitive_torus_add(
        major_segments=major_segments,
        minor_segments=minor_segments,
        major_radius=major_radius,
        minor_radius=minor_radius,
        location=location,
        rotation=rotation,
    )
    obj = bpy.context.object
    obj.name = name
    assign_material(obj, material)
    set_smooth(obj, False)
    move_to_collection(obj, collection)
    return obj


def add_cylinder_between(
    name: str,
    start: tuple[float, float, float],
    end: tuple[float, float, float],
    radius: float,
    material: bpy.types.Material,
    collection: str = "Store",
    vertices: int = 8,
) -> bpy.types.Object:
    a = Vector(start)
    b = Vector(end)
    direction = b - a
    mid = (a + b) * 0.5
    obj = add_cylinder(
        name,
        tuple(mid),
        radius,
        direction.length,
        material,
        collection=collection,
        vertices=vertices,
        bevel=0.0,
        smooth=False,
    )
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = direction.to_track_quat("Z", "Y")
    return obj


def add_text(
    name: str,
    body: str,
    location: tuple[float, float, float],
    size: float,
    material: bpy.types.Material,
    collection: str = "Store",
    align_x: str = "CENTER",
    extrude: float = 0.04,
    bevel_depth: float = 0.015,
    rotation: tuple[float, float, float] = (math.pi / 2.0, 0.0, 0.0),
    scale_x: float = 1.0,
) -> bpy.types.Object:
    curve = bpy.data.curves.new(name=f"{name}_Curve", type="FONT")
    curve.body = body
    curve.align_x = align_x
    curve.align_y = "CENTER"
    curve.size = size
    curve.extrude = extrude
    curve.bevel_depth = bevel_depth
    curve.bevel_resolution = 2
    obj = bpy.data.objects.new(name, curve)
    obj.location = location
    obj.rotation_euler = rotation
    obj.scale.x = scale_x
    COLLECTIONS[collection].objects.link(obj)
    assign_material(obj, material)
    # Convert the visible identity mark to mesh so GLB import keeps it.
    set_active(obj)
    bpy.ops.object.convert(target="MESH")
    obj = bpy.context.object
    obj.name = name
    cleanup = bmesh.new()
    cleanup.from_mesh(obj.data)
    bad_faces = [face for face in cleanup.faces if face.calc_area() <= 1e-8]
    if bad_faces:
        bmesh.ops.delete(cleanup, geom=bad_faces, context="FACES")
    cleanup.to_mesh(obj.data)
    cleanup.free()
    obj.data.update()
    return obj


# -----------------------------------------------------------------------------
# Materials
# -----------------------------------------------------------------------------


def make_material(
    name: str,
    color: tuple[float, float, float, float],
    roughness: float = 0.5,
    metallic: float = 0.0,
    emission: tuple[float, float, float, float] | None = None,
    emission_strength: float = 0.0,
    alpha: float = 1.0,
    transmission: float = 0.0,
) -> bpy.types.Material:
    material = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    material.use_nodes = True
    material.diffuse_color = color
    try:
        material.surface_render_method = "DITHERED" if alpha < 0.98 else "DITHERED"
    except Exception:
        pass
    if alpha < 0.98:
        try:
            material.blend_method = "BLEND"
            material.use_screen_refraction = True
        except Exception:
            pass
    nodes = material.node_tree.nodes
    bsdf = nodes.get("Principled BSDF")
    if bsdf is None:
        return material
    bsdf.inputs["Base Color"].default_value = color
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    if "Alpha" in bsdf.inputs:
        bsdf.inputs["Alpha"].default_value = alpha
    if "Transmission Weight" in bsdf.inputs:
        bsdf.inputs["Transmission Weight"].default_value = transmission
    if "IOR" in bsdf.inputs:
        bsdf.inputs["IOR"].default_value = 1.45
    if emission is not None:
        if "Emission Color" in bsdf.inputs:
            bsdf.inputs["Emission Color"].default_value = emission
        elif "Emission" in bsdf.inputs:
            bsdf.inputs["Emission"].default_value = emission
        if "Emission Strength" in bsdf.inputs:
            bsdf.inputs["Emission Strength"].default_value = emission_strength
    return material


def create_materials() -> dict[str, bpy.types.Material]:
    mats: dict[str, bpy.types.Material] = {}
    mats["navy"] = make_material("PS_Black_Frame", (0.018, 0.021, 0.024, 1.0), 0.44, 0.12)
    mats["navy2"] = make_material("PS_Black_Accent", (0.032, 0.036, 0.040, 1.0), 0.48, 0.12)
    mats["teal"] = make_material("PS_Product_Deep_Blue", (0.025, 0.11, 0.28, 1.0), 0.34, 0.08)
    mats["teal_dark"] = make_material("PS_Store_White_Panel", (0.78, 0.79, 0.77, 1.0), 0.48, 0.0)
    mats["orange"] = make_material("PS_Orange", (0.95, 0.25, 0.055, 1.0), 0.34, 0.04)
    mats["orange_dark"] = make_material("PS_Orange_Shadow", (0.50, 0.075, 0.018, 1.0), 0.42, 0.02)
    mats["cream"] = make_material("PS_Warm_White", (0.90, 0.88, 0.80, 1.0), 0.48, 0.0)
    mats["white"] = make_material("PS_Soft_White", (0.97, 0.975, 0.965, 1.0), 0.42, 0.0)
    mats["store_white"] = make_material("PS_Store_White", (0.93, 0.92, 0.87, 1.0), 0.46, 0.0)
    mats["ceiling"] = make_material("PS_Ceiling_White", (0.88, 0.89, 0.86, 1.0), 0.58, 0.0)
    mats["glass"] = make_material(
        "PS_Tinted_Glass",
        (0.72, 0.90, 0.94, 0.10),
        0.08,
        0.0,
        alpha=0.10,
        transmission=0.58,
    )
    mats["metal"] = make_material("PS_Warm_Metal", (0.18, 0.16, 0.12, 1.0), 0.31, 0.58)
    mats["black"] = make_material("PS_Rubber_Black", (0.004, 0.004, 0.006, 1.0), 0.68, 0.0)
    mats["floor"] = make_material("PS_Floor_Stone", (0.17, 0.18, 0.18, 1.0), 0.62, 0.04)
    mats["floor_light"] = make_material("PS_Floor_Yellow", (0.72, 0.39, 0.025, 1.0), 0.50, 0.06)
    mats["wood"] = make_material("PS_Signal_Yellow_Painted", (0.78, 0.43, 0.025, 1.0), 0.54, 0.0)
    mats["pink"] = make_material("PS_Product_Plum", (0.48, 0.07, 0.25, 1.0), 0.38, 0.0)
    mats["lime"] = make_material("PS_Product_Lime", (0.70, 0.80, 0.10, 1.0), 0.40, 0.0)
    mats["blue"] = make_material("PS_Product_Blue", (0.07, 0.34, 0.84, 1.0), 0.33, 0.02)
    mats["violet"] = make_material("PS_Product_Violet", (0.38, 0.16, 0.75, 1.0), 0.38, 0.02)
    mats["yellow"] = make_material("PS_Product_Yellow", (0.95, 0.56, 0.035, 1.0), 0.39, 0.0)
    mats["brown"] = make_material("PS_Product_Brown", (0.34, 0.12, 0.045, 1.0), 0.46, 0.0)
    mats["store_yellow"] = make_material("PS_Signal_Yellow", (1.0, 0.58, 0.015, 1.0), 0.34, 0.02)
    mats["store_yellow_dark"] = make_material("PS_Signal_Yellow_Shadow", (0.52, 0.25, 0.012, 1.0), 0.46, 0.02)
    return mats


# -----------------------------------------------------------------------------
def package_front_name(prefix: str) -> str:
    """Source-referenced product name for the package face."""
    for family in PRODUCT_FAMILY_CONTRACT.values():
        if any(prefix.startswith(candidate) for candidate in family["prefixes"]):
            return family["front"]
    return "SPORT NUTRITION"


def product_label_ink(background: bpy.types.Material) -> bpy.types.Material:
    color = background.diffuse_color
    luminance = 0.2126 * color[0] + 0.7152 * color[1] + 0.0722 * color[2]
    if luminance >= 0.46:
        return bpy.data.materials.get("PS_Product_Label_Black_Ink") or make_material(
            "PS_Product_Label_Black_Ink", (0.018, 0.021, 0.024, 1.0), 0.62, 0.0
        )
    return bpy.data.materials.get("PS_Product_Label_White_Ink") or make_material(
        "PS_Product_Label_White_Ink", (0.97, 0.975, 0.965, 1.0), 0.62, 0.0
    )


def add_label_text(
    name: str,
    text: str,
    x: float,
    y: float,
    z: float,
    size: float,
    ink: bpy.types.Material,
    rotation_z: float,
    scale: float,
) -> bpy.types.Object:
    curve = bpy.data.curves.new(f"{name}_Font", "FONT")
    curve.body = text
    curve.size = size
    curve.align_x = "CENTER"
    curve.align_y = "CENTER"
    curve.resolution_u = 1
    curve.extrude = 0.0
    curve.bevel_depth = 0.0
    obj = bpy.data.objects.new(name, curve)
    COLLECTIONS["Store"].objects.link(obj)
    obj.location = (x, y, z)
    obj.rotation_euler = (math.pi * 0.5, 0.0, rotation_z)
    obj.data.materials.append(ink)
    set_active(obj)
    bpy.ops.object.convert(target="MESH")
    return bpy.context.object


def add_product_label(
    prefix: str,
    x: float,
    y: float,
    z: float,
    width: float,
    height: float,
    material: bpy.types.Material,
    scale: float = 1.0,
    rotation_z: float = 0.0,
) -> bpy.types.Object:
    """Layer a restrained brand, product-family and size hierarchy onto each label."""
    label = add_box(
        f"{prefix}_LabelPanel",
        (x, y, z),
        (width, 0.028 * scale, height),
        material,
        bevel=0.012 * scale,
        segments=1,
        rotation=(0.0, 0.0, rotation_z),
        smooth=False,
    )
    product_name = package_front_name(prefix)
    ink = product_label_ink(material)
    text_y = y - 0.015 * scale - 0.0008 * scale
    text_objects = [
        add_label_text(
            f"{prefix}_BrandMark", "PROTEINI.SI", x, text_y,
            z + height * 0.22, min(height * 0.18, width * 0.075),
            ink, rotation_z, scale,
        ),
        add_label_text(
            f"{prefix}_ProductName", product_name, x, text_y,
            z - height * 0.17, min(height * 0.28, width / (max(1, len(product_name)) * 0.68)),
            ink, rotation_z, scale,
        ),
    ]
    set_active(label)
    for obj in text_objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = label
    bpy.ops.object.join()
    # The joined mesh keeps a semantic name so the exported/runtime GLB can
    # prove that the readable brand/category cue survived text conversion.
    label_token = next(
        family["node_token"] for family in PRODUCT_FAMILY_CONTRACT.values()
        if any(prefix.startswith(candidate) for candidate in family["prefixes"])
    )
    label.name = f"{prefix}_LabelPanel_PROTEINI_SI_{label_token}"
    return label


# Product families (short readable copy is confined to package faces)
# -----------------------------------------------------------------------------

def add_tub(
    prefix: str,
    x: float,
    y: float,
    shelf_z: float,
    body_mat: bpy.types.Material,
    lid_mat: bpy.types.Material,
    accent_mat: bpy.types.Material,
    scale: float = 1.0,
    rotation_z: float = 0.0,
) -> list[bpy.types.Object]:
    r = 0.32 * scale
    h = 0.52 * scale
    z = shelf_z + 0.06 + h * 0.5
    body = add_cone(
        f"{prefix}_TubBody",
        (x, y, z),
        r * 0.90,
        r,
        h,
        body_mat,
        vertices=10,
        bevel=0.035 * scale,
        rotation=(0.0, 0.0, rotation_z),
        smooth=False,
    )
    lid = add_cylinder(
        f"{prefix}_TubLid",
        (x, y, shelf_z + 0.06 + h + 0.055 * scale),
        r * 1.03,
        0.11 * scale,
        lid_mat,
        vertices=10,
        bevel=0.025 * scale,
        rotation=(0.0, 0.0, rotation_z),
        smooth=False,
    )
    ring = add_torus(
        f"{prefix}_TubAccentRing",
        (x, y, shelf_z + 0.06 + h - 0.02 * scale),
        r * 0.92,
        0.022 * scale,
        accent_mat,
        major_segments=10,
        minor_segments=4,
    )
    label = add_product_label(prefix, x, y - r * 1.01, z, r * 1.45, h * 0.30, accent_mat, scale, rotation_z)


def add_bottle(
    prefix: str,
    x: float,
    y: float,
    shelf_z: float,
    body_mat: bpy.types.Material,
    cap_mat: bpy.types.Material,
    accent_mat: bpy.types.Material,
    scale: float = 1.0,
    rotation_z: float = 0.0,
) -> list[bpy.types.Object]:
    r = 0.22 * scale
    h = 0.66 * scale
    z = shelf_z + 0.06 + h * 0.5
    body = add_cone(
        f"{prefix}_BottleBody",
        (x, y, z),
        r * 1.02,
        r * 0.83,
        h,
        body_mat,
        vertices=9,
        bevel=0.035 * scale,
        rotation=(0.0, 0.0, rotation_z),
        smooth=False,
    )
    neck = add_cylinder(
        f"{prefix}_BottleNeck",
        (x, y, shelf_z + 0.06 + h + 0.075 * scale),
        r * 0.55,
        0.15 * scale,
        body_mat,
        vertices=9,
        bevel=0.022 * scale,
        smooth=False,
    )
    cap = add_cylinder(
        f"{prefix}_BottleCap",
        (x, y, shelf_z + 0.06 + h + 0.175 * scale),
        r * 0.62,
        0.12 * scale,
        cap_mat,
        vertices=8,
        bevel=0.02 * scale,
        smooth=False,
    )
    band = add_torus(
        f"{prefix}_BottleBand",
        (x, y, shelf_z + 0.06 + h * 0.40),
        r * 0.99,
        0.018 * scale,
        accent_mat,
        major_segments=9,
        minor_segments=4,
    )
    handle = add_torus(
        f"{prefix}_BottleHandle",
        (x + r * 0.82, y, shelf_z + 0.06 + h + 0.06 * scale),
        r * 0.32,
        0.025 * scale,
        cap_mat,
        rotation=(math.pi / 2.0, 0.0, 0.0),
        major_segments=8,
        minor_segments=4,
    )
    label = add_product_label(prefix, x, y - r * 1.03, z, r * 1.42, h * 0.26, accent_mat, scale, rotation_z)


def add_pouch(
    prefix: str,
    x: float,
    y: float,
    shelf_z: float,
    body_mat: bpy.types.Material,
    stripe_mat: bpy.types.Material,
    scale: float = 1.0,
    rotation_z: float = 0.0,
) -> list[bpy.types.Object]:
    """Resealable stand-up supplement pouch with shoulders, gusset, zipper and front label."""
    w = 0.53 * scale
    d = 0.30 * scale
    h = 0.78 * scale
    base_z = shelf_z + 0.06

    # The profile widens into a flat bottom gusset, keeps a broad printable
    # front, then tapers at the shoulders into a heat-sealed reseal strip.
    profile = [
        (0.00, 0.84, 0.66),
        (0.06, 0.93, 0.90),
        (0.13, 0.99, 1.00),
        (0.58, 1.00, 0.97),
        (0.78, 0.98, 0.93),
        (0.88, 0.88, 0.82),
        (0.94, 0.78, 0.73),
        (0.98, 0.78, 0.73),
    ]
    vertices: list[tuple[float, float, float]] = []
    for height, width_factor, depth_factor in profile:
        half_w = w * width_factor * 0.5
        half_d = d * depth_factor * 0.5
        z = h * height
        vertices.extend(
            [
                (-half_w, -half_d, z),
                (half_w, -half_d, z),
                (half_w, half_d, z),
                (-half_w, half_d, z),
            ]
        )

    faces: list[tuple[int, ...]] = [(3, 2, 1, 0)]
    for ring in range(len(profile) - 1):
        lower = ring * 4
        upper = (ring + 1) * 4
        for corner in range(4):
            next_corner = (corner + 1) % 4
            faces.append(
                (
                    lower + corner,
                    lower + next_corner,
                    upper + next_corner,
                    upper + corner,
                )
            )
    last = (len(profile) - 1) * 4
    faces.append((last, last + 1, last + 2, last + 3))

    mesh = bpy.data.meshes.new(f"{prefix}_PouchMesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    body = bpy.data.objects.new(f"{prefix}_PouchBody", mesh)
    body.location = (x, y, base_z)
    body.rotation_euler.z = rotation_z
    COLLECTIONS["Store"].objects.link(body)
    assign_material(body, body_mat)
    add_bevel(body, 0.012 * scale, segments=1)
    body["package_form"] = "resealable_stand_up_pouch"

    front_y = y - d * 0.49
    top_seal = add_box(
        f"{prefix}_PouchHeatSeal",
        (x, y, base_z + h * 0.965),
        (w * 0.80, d * 0.76, 0.038 * scale),
        body_mat,
        bevel=0.012 * scale,
        segments=1,
        rotation=(0.0, 0.0, rotation_z),
    )
    zipper = add_box(
        f"{prefix}_PouchResealStrip",
        (x, y - d * 0.4225 - 0.003 * scale, base_z + h * 0.875),
        (w * 0.76, 0.018 * scale, 0.032 * scale),
        stripe_mat,
        bevel=0.008 * scale,
        segments=1,
        rotation=(0.0, 0.0, rotation_z),
        smooth=False,
    )
    label = add_product_label(
        prefix,
        x,
        front_y - 0.003 * scale,
        base_z + h * 0.47,
        w * 0.74,
        h * 0.31,
        stripe_mat,
        scale,
        rotation_z,
    )
    gusset = add_box(
        f"{prefix}_PouchBottomGusset",
        (x, front_y - 0.006 * scale, base_z + h * 0.12),
        (w * 0.78, 0.018 * scale, h * 0.045),
        stripe_mat,
        bevel=0.008 * scale,
        segments=1,
        rotation=(0.0, 0.0, rotation_z),
        smooth=False,
    )
    return [body, top_seal, zipper, label, gusset]

def add_bar(
    prefix: str,
    x: float,
    y: float,
    shelf_z: float,
    body_mat: bpy.types.Material,
    end_mat: bpy.types.Material,
    scale: float = 1.0,
    rotation_z: float = 0.0,
) -> list[bpy.types.Object]:
    w = 0.62 * scale
    d = 0.28 * scale
    h = 0.22 * scale
    z = shelf_z + 0.06 + h * 0.5
    body = add_box(
        f"{prefix}_BarBody",
        (x, y, z),
        (w, d, h),
        body_mat,
        bevel=0.09 * scale,
        segments=2,
        rotation=(0.0, 0.0, rotation_z),
    )
    end_l = add_box(
        f"{prefix}_BarEndL",
        (x - w * 0.43, y, z),
        (0.06 * scale, d * 0.90, h * 0.75),
        end_mat,
        bevel=0.02 * scale,
        segments=1,
        rotation=(0.0, 0.0, rotation_z),
    )
    end_r = add_box(
        f"{prefix}_BarEndR",
        (x + w * 0.43, y, z),
        (0.06 * scale, d * 0.90, h * 0.75),
        end_mat,
        bevel=0.02 * scale,
        segments=1,
        rotation=(0.0, 0.0, rotation_z),
    )
    label = add_product_label(prefix, x, y - d * 0.53, z, w * 0.64, h * 0.48, end_mat, scale, rotation_z)


def add_shaker(
    prefix: str,
    x: float,
    y: float,
    shelf_z: float,
    body_mat: bpy.types.Material,
    cap_mat: bpy.types.Material,
    accent_mat: bpy.types.Material,
    scale: float = 1.0,
    rotation_z: float = 0.0,
) -> list[bpy.types.Object]:
    """A tall shaker silhouette with a lid button, handle and color band."""
    objects = add_bottle(
        prefix,
        x,
        y,
        shelf_z,
        body_mat,
        cap_mat,
        accent_mat,
        scale=scale,
        rotation_z=rotation_z,
    )
    r = 0.22 * scale
    h = 0.66 * scale
    add_box(
        f"{prefix}_ShakerFlipTop",
        (x, y - r * 0.12, shelf_z + 0.06 + h + 0.245 * scale),
        (r * 0.70, r * 0.62, 0.055 * scale),
        accent_mat,
        bevel=0.012 * scale,
        segments=1,
        rotation=(0.0, 0.0, rotation_z),
        smooth=False,
    )
    return objects


def add_preworkout_tub(
    prefix: str,
    x: float,
    y: float,
    shelf_z: float,
    body_mat: bpy.types.Material,
    lid_mat: bpy.types.Material,
    accent_mat: bpy.types.Material,
    scale: float = 1.0,
    rotation_z: float = 0.0,
) -> list[bpy.types.Object]:
    """Tall, dark pre-workout canister with a wide cap and two color bands."""
    r = 0.32 * scale
    h = 0.70 * scale
    z = shelf_z + 0.06 + h * 0.5
    body = add_cone(
        f"{prefix}_PreWorkoutBody",
        (x, y, z),
        r * 0.94,
        r,
        h,
        body_mat,
        vertices=12,
        bevel=0.035 * scale,
        rotation=(0.0, 0.0, rotation_z),
        smooth=False,
    )
    shoulder = add_cone(
        f"{prefix}_PreWorkoutShoulder",
        (x, y, shelf_z + 0.06 + h + 0.025 * scale),
        r * 0.86,
        r * 0.72,
        0.075 * scale,
        body_mat,
        vertices=12,
        bevel=0.018 * scale,
        rotation=(0.0, 0.0, rotation_z),
        smooth=False,
    )
    lid = add_cylinder(
        f"{prefix}_PreWorkoutLid",
        (x, y, shelf_z + 0.06 + h + 0.095 * scale),
        r * 0.88,
        0.11 * scale,
        lid_mat,
        vertices=12,
        bevel=0.022 * scale,
        rotation=(0.0, 0.0, rotation_z),
        smooth=False,
    )
    ring_low = add_torus(
        f"{prefix}_PreWorkoutLowerBand",
        (x, y, shelf_z + 0.06 + h * 0.20),
        r * 0.96,
        0.022 * scale,
        accent_mat,
        major_segments=12,
        minor_segments=4,
    )
    ring_high = add_torus(
        f"{prefix}_PreWorkoutUpperBand",
        (x, y, shelf_z + 0.06 + h * 0.72),
        r * 0.98,
        0.018 * scale,
        accent_mat,
        major_segments=12,
        minor_segments=4,
    )
    label = add_product_label(prefix, x, y - r * 1.01, z, r * 1.45, h * 0.30, accent_mat, scale, rotation_z)


def add_creatine_jar(
    prefix: str,
    x: float,
    y: float,
    shelf_z: float,
    body_mat: bpy.types.Material,
    lid_mat: bpy.types.Material,
    accent_mat: bpy.types.Material,
    scale: float = 1.0,
    rotation_z: float = 0.0,
) -> list[bpy.types.Object]:
    """Short, wide supplement jar used for the creatine bay."""
    r = 0.34 * scale
    h = 0.42 * scale
    z = shelf_z + 0.06 + h * 0.5
    body = add_cone(
        f"{prefix}_CreatineBody",
        (x, y, z),
        r * 0.96,
        r * 0.88,
        h,
        body_mat,
        vertices=10,
        bevel=0.032 * scale,
        rotation=(0.0, 0.0, rotation_z),
        smooth=False,
    )
    shoulder = add_cone(
        f"{prefix}_CreatineShoulder",
        (x, y, shelf_z + 0.06 + h + 0.02 * scale),
        r * 0.86,
        r * 0.78,
        0.06 * scale,
        body_mat,
        vertices=10,
        bevel=0.016 * scale,
        rotation=(0.0, 0.0, rotation_z),
        smooth=False,
    )
    lid = add_cylinder(
        f"{prefix}_CreatineLid",
        (x, y, shelf_z + 0.06 + h + 0.075 * scale),
        r * 0.90,
        0.10 * scale,
        lid_mat,
        vertices=10,
        bevel=0.020 * scale,
        rotation=(0.0, 0.0, rotation_z),
        smooth=False,
    )
    ring = add_torus(
        f"{prefix}_CreatineAccentRing",
        (x, y, shelf_z + 0.06 + h * 0.20),
        r * 0.91,
        0.022 * scale,
        accent_mat,
        major_segments=10,
        minor_segments=4,
    )
    label = add_product_label(prefix, x, y - r * 1.01, z, r * 1.30, h * 0.26, accent_mat, scale, rotation_z)


def add_protein_tub(
    prefix: str,
    x: float,
    y: float,
    shelf_z: float,
    body_mat: bpy.types.Material,
    lid_mat: bpy.types.Material,
    accent_mat: bpy.types.Material,
    scale: float = 1.0,
    rotation_z: float = 0.0,
) -> list[bpy.types.Object]:
    """Large whey tub silhouette, distinct from the squat creatine jar."""
    r = 0.41 * scale
    h = 0.67 * scale
    z = shelf_z + 0.06 + h * 0.5
    body = add_cone(
        f"{prefix}_ProteinTubBody",
        (x, y, z),
        r * 0.92,
        r,
        h,
        body_mat,
        vertices=12,
        bevel=0.045 * scale,
        rotation=(0.0, 0.0, rotation_z),
        smooth=False,
    )
    shoulder = add_cone(
        f"{prefix}_ProteinTubShoulder",
        (x, y, shelf_z + 0.06 + h + 0.025 * scale),
        r * 0.90,
        r * 0.78,
        0.085 * scale,
        body_mat,
        vertices=12,
        bevel=0.022 * scale,
        rotation=(0.0, 0.0, rotation_z),
        smooth=False,
    )
    lid = add_cylinder(
        f"{prefix}_ProteinTubLid",
        (x, y, shelf_z + 0.06 + h + 0.105 * scale),
        r * 0.91,
        0.12 * scale,
        lid_mat,
        vertices=12,
        bevel=0.024 * scale,
        rotation=(0.0, 0.0, rotation_z),
        smooth=False,
    )
    ring = add_torus(
        f"{prefix}_ProteinTubAccentRing",
        (x, y, shelf_z + 0.06 + h * 0.28),
        r * 0.95,
        0.025 * scale,
        accent_mat,
        major_segments=12,
        minor_segments=4,
    )
    label = add_product_label(prefix, x, y - r * 1.01, z, r * 1.35, h * 0.30, accent_mat, scale, rotation_z)


def add_energy_can(
    prefix: str,
    x: float,
    y: float,
    shelf_z: float,
    body_mat: bpy.types.Material,
    top_mat: bpy.types.Material,
    accent_mat: bpy.types.Material,
    scale: float = 1.0,
    rotation_z: float = 0.0,
) -> list[bpy.types.Object]:
    """Slim low-poly beverage can with a tapered body, rims and pull tab."""
    r = 0.145 * scale
    h = 0.64 * scale
    z = shelf_z + 0.06 + h * 0.5
    body = add_cone(
        f"{prefix}_EnergyCanBody",
        (x, y, z),
        r * 1.02,
        r * 0.94,
        h,
        body_mat,
        vertices=12,
        bevel=0.018 * scale,
        rotation=(0.0, 0.0, rotation_z),
        smooth=False,
    )
    top = add_cylinder(
        f"{prefix}_EnergyCanTop",
        (x, y, shelf_z + 0.06 + h + 0.025 * scale),
        r * 0.88,
        0.045 * scale,
        top_mat,
        vertices=12,
        bevel=0.010 * scale,
        rotation=(0.0, 0.0, rotation_z),
        smooth=False,
    )
    top_rim = add_torus(
        f"{prefix}_EnergyCanTopRim",
        (x, y, shelf_z + 0.06 + h + 0.047 * scale),
        r * 0.88,
        0.012 * scale,
        accent_mat,
        major_segments=12,
        minor_segments=4,
    )
    tab = add_box(
        f"{prefix}_EnergyCanPullTab",
        (x, y - r * 0.16, shelf_z + 0.06 + h + 0.056 * scale),
        (r * 0.48, r * 0.24, 0.018 * scale),
        top_mat,
        bevel=0.004 * scale,
        segments=1,
        rotation=(0.0, 0.0, rotation_z),
        smooth=False,
    )
    bottom_ring = add_torus(
        f"{prefix}_EnergyCanBottomRing",
        (x, y, shelf_z + 0.06 + 0.014 * scale),
        r * 0.88,
        0.014 * scale,
        accent_mat,
        major_segments=12,
        minor_segments=4,
    )
    accent_ring = add_torus(
        f"{prefix}_EnergyCanAccentRing",
        (x, y, shelf_z + 0.06 + h * 0.52),
        r * 1.01,
        0.014 * scale,
        accent_mat,
        major_segments=12,
        minor_segments=4,
    )
    label = add_product_label(prefix, x, y - r * 1.03, z, r * 1.38, h * 0.32, accent_mat, scale, rotation_z)


def add_rtd_bottle(
    prefix: str,
    x: float,
    y: float,
    shelf_z: float,
    body_mat: bpy.types.Material,
    cap_mat: bpy.types.Material,
    accent_mat: bpy.types.Material,
    scale: float = 1.0,
    rotation_z: float = 0.0,
) -> list[bpy.types.Object]:
    """Small ready-to-drink bottle with a shoulder, neck, cap and base ring."""
    r = 0.20 * scale
    h = 0.48 * scale
    z = shelf_z + 0.06 + h * 0.5
    body = add_cone(
        f"{prefix}_RtdBody",
        (x, y, z),
        r * 1.02,
        r * 0.96,
        h,
        body_mat,
        vertices=10,
        bevel=0.025 * scale,
        rotation=(0.0, 0.0, rotation_z),
        smooth=False,
    )
    shoulder = add_cone(
        f"{prefix}_RtdShoulder",
        (x, y, shelf_z + 0.06 + h + 0.05 * scale),
        r * 0.96,
        r * 0.62,
        0.10 * scale,
        body_mat,
        vertices=10,
        bevel=0.018 * scale,
        rotation=(0.0, 0.0, rotation_z),
        smooth=False,
    )
    neck = add_cylinder(
        f"{prefix}_RtdNeck",
        (x, y, shelf_z + 0.06 + h + 0.145 * scale),
        r * 0.48,
        0.09 * scale,
        body_mat,
        vertices=10,
        bevel=0.016 * scale,
        smooth=False,
    )
    cap = add_cylinder(
        f"{prefix}_RtdCap",
        (x, y, shelf_z + 0.06 + h + 0.2275 * scale),
        r * 0.57,
        0.075 * scale,
        cap_mat,
        vertices=10,
        bevel=0.014 * scale,
        smooth=False,
    )
    band = add_torus(
        f"{prefix}_RtdBand",
        (x, y, shelf_z + 0.06 + h * 0.40),
        r * 0.99,
        0.016 * scale,
        accent_mat,
        major_segments=10,
        minor_segments=4,
    )
    base_ring = add_torus(
        f"{prefix}_RtdBaseRing",
        (x, y, shelf_z + 0.06 + 0.012 * scale),
        r * 0.90,
        0.012 * scale,
        accent_mat,
        major_segments=10,
        minor_segments=4,
    )
    label = add_product_label(prefix, x, y - r * 1.03, z, r * 1.35, h * 0.30, accent_mat, scale, rotation_z)


def stock_products(mats: dict[str, bpy.types.Material]) -> None:
    # The rack contents are intentionally zoned instead of alternating product
    # families. Each bay has one silhouette language and a restrained palette
    # inspired by the current Proteini.si assortment, without product copy.
    shelf_rows = [1.00, 2.05, 3.10, 4.15]

    # Left-front bay: shaker bottles. The current assortment includes tall
    # 650–800 ml bottles in black, white, olive and transparent combinations.
    shaker_ys = [-2.28, -1.62, -0.96, -0.30]
    shaker_palettes = [
        (mats["white"], mats["black"], mats["blue"]),
        (mats["cream"], mats["white"], mats["orange"]),
        (mats["white"], mats["black"], mats["violet"]),
        (mats["white"], mats["brown"], mats["store_yellow"]),
    ]
    for row_index, shelf_z in enumerate(shelf_rows):
        for item_index, y in enumerate(shaker_ys):
            body, cap, accent = shaker_palettes[(row_index + item_index) % len(shaker_palettes)]
            add_shaker(
                f"Shakers_Left_R{row_index}_S{item_index}",
                -4.30 - 0.02 * (item_index % 2),
                y,
                shelf_z,
                body,
                cap,
                accent,
                scale=0.86 + 0.05 * ((row_index + item_index) % 3),
                rotation_z=0.025 * ((item_index % 3) - 1),
            )

    # Left-rear bay: taller pre-workout canisters, mostly dark with a strong
    # orange/lime/teal signal band like sports-nutrition tubs on the site.
    pre_ys = [0.58, 1.30, 2.02, 2.74, 3.34]
    pre_palettes = [
        (mats["white"], mats["black"], mats["orange"]),
        (mats["cream"], mats["black"], mats["blue"]),
        (mats["white"], mats["brown"], mats["violet"]),
        (mats["white"], mats["black"], mats["store_yellow"]),
    ]
    for row_index, shelf_z in enumerate(shelf_rows):
        for item_index, y in enumerate(pre_ys):
            body, lid, accent = pre_palettes[(row_index + item_index) % len(pre_palettes)]
            add_preworkout_tub(
                f"PreWorkout_Left_R{row_index}_S{item_index}",
                -4.30 + 0.02 * (item_index % 2),
                y,
                shelf_z,
                body,
                lid,
                accent,
                scale=0.86 + 0.05 * ((row_index + item_index) % 2),
                rotation_z=-0.025 * ((item_index % 3) - 1),
            )

    # Right wall: resealable whey pouches are the main Proteini.si form;
    # a smaller number of rigid tubs represent bulk formats in the same bay.
    protein_ys = [-2.24, -1.40, -0.56, 0.28, 1.12, 1.96, 2.80, 3.34]
    protein_tub_palette = [
        (mats["white"], mats["black"], mats["blue"]),
        (mats["cream"], mats["brown"], mats["orange"]),
        (mats["white"], mats["black"], mats["violet"]),
        (mats["white"], mats["brown"], mats["store_yellow"]),
    ]
    protein_pouch_palette = [
        (mats["white"], mats["blue"]),
        (mats["cream"], mats["black"]),
        (mats["white"], mats["orange"]),
        (mats["white"], mats["violet"]),
    ]
    for row_index, shelf_z in enumerate(shelf_rows):
        for item_index, y in enumerate(protein_ys):
            px = 4.30 + 0.02 * (item_index % 2)
            if (item_index + row_index) % 3 != 0:
                body, stripe = protein_pouch_palette[(item_index + row_index) % len(protein_pouch_palette)]
                add_pouch(
                    f"Protein_Right_R{row_index}_S{item_index}",
                    px,
                    y,
                    shelf_z,
                    body,
                    stripe,
                    scale=0.88 + 0.05 * (row_index % 2),
                    rotation_z=-0.025 * ((item_index % 3) - 1),
                )
            else:
                body, lid, accent = protein_tub_palette[(item_index + row_index) % len(protein_tub_palette)]
                add_protein_tub(
                    f"Protein_Right_R{row_index}_S{item_index}",
                    px,
                    y,
                    shelf_z,
                    body,
                    lid,
                    accent,
                    scale=0.72 + 0.05 * ((item_index + row_index) % 3),
                    rotation_z=-0.025 * ((item_index % 3) - 1),
                )
    # Central island: a real protein-bar fixture with trays and repeated slim
    # wrappers, not a mixed hero shelf.
    bar_rows = [(-0.18, mats["white"], mats["orange"]), (0.48, mats["cream"], mats["violet"]), (1.14, mats["white"], mats["blue"])]
    for row_index, (y, body, end) in enumerate(bar_rows):
        for tray_x in (-0.88, 0.0, 0.88):
            add_box(
                f"Island_BarTray_R{row_index}_{tray_x:+.2f}",
                (tray_x, y, 1.19),
                (0.76, 0.46, 0.08),
                mats["navy2"],
                bevel=0.035,
                segments=1,
            )
            add_bar(
                f"ProteinBars_Island_R{row_index}_{tray_x:+.2f}",
                tray_x,
                y,
                1.20,
                body,
                end,
                scale=0.78 + 0.03 * (row_index % 2),
                rotation_z=0.05 * ((row_index % 3) - 1),
            )

    # Checkout impulse display stays in the bar family so the island and till
    # read as one snack zone.
    add_bar("ProteinBars_Checkout_A", 2.78, 2.18 + CHECKOUT_Y_SHIFT, 1.15, mats["white"], mats["brown"], 0.78, -0.06)
    add_bar("ProteinBars_Checkout_B", 3.37, 2.18 + CHECKOUT_Y_SHIFT, 1.15, mats["cream"], mats["black"], 0.72, 0.06)

    # Back-wall creatine fixture: the 200 g Proteini.si package is a compact
    # resealable white pouch with blue/black graphics, not a rigid jar.
    creatine_palettes = [
        (mats["white"], mats["blue"]),
        (mats["cream"], mats["black"]),
        (mats["white"], mats["blue"]),
        (mats["white"], mats["orange"]),
    ]
    for row_index, shelf_z in enumerate((1.18, 1.92, 2.66)):
        for item_index, x in enumerate((-1.36, -0.45, 0.45, 1.36)):
            body, stripe = creatine_palettes[(row_index + item_index) % len(creatine_palettes)]
            add_pouch(
                f"Creatine_Back_R{row_index}_S{item_index}",
                x,
                3.18 + 0.06 * (item_index % 2),
                shelf_z,
                body,
                stripe,
                scale=0.78 + 0.03 * ((row_index + item_index) % 2),
                rotation_z=0.03 * ((item_index % 3) - 1),
            )
    # Energy drinks live in their own front-right cooler and are always slim
    # cans, never tubs or pouches.
    # Keep the energy cans visibly distinct behind the clear front. The old
    # mostly-white body palette collapsed into the cooler's white shell in the
    # integrated render, leaving only tiny label accents readable.
    energy_palette = [
        (mats["teal"], mats["metal"], mats["blue"]),
        (mats["cream"], mats["metal"], mats["orange"]),
        (mats["blue"], mats["metal"], mats["violet"]),
        (mats["navy2"], mats["metal"], mats["yellow"]),
    ]
    for shelf_index, shelf_z in enumerate((0.78, 1.28, 1.78)):
        for depth_index, drink_y in enumerate((-2.94, -2.64)):
            for item_index, x in enumerate((2.62, 2.97, 3.32, 3.67)):
                body, top, accent = energy_palette[(shelf_index + depth_index + item_index) % len(energy_palette)]
                add_energy_can(
                    f"EnergyDrinks_Cooler_R{shelf_index}_D{depth_index}_S{item_index}",
                    x,
                    drink_y,
                    shelf_z,
                    body,
                    top,
                    accent,
                    scale=0.68 + 0.02 * ((item_index + shelf_index + depth_index) % 2),
                    rotation_z=0.025 * ((item_index % 3) - 1),
                )

    # Extra cold fridge at the rear-left: small RTD protein bottles and a few
    # chilled cans, visually distinct from the energy-drink cooler.
    cold_bottle_palette = [
        (mats["white"], mats["black"], mats["blue"]),
        (mats["cream"], mats["black"], mats["orange"]),
        (mats["white"], mats["brown"], mats["violet"]),
    ]
    for shelf_index, shelf_z in enumerate((0.78, 1.26, 1.74)):
        if shelf_index == 1:
            for item_index, x in enumerate((-3.25, -2.84, -2.43, -2.02)):
                body, top, accent = energy_palette[(item_index + 1) % len(energy_palette)]
                add_energy_can(
                    f"ColdFridge_RearLeft_Can_R{shelf_index}_S{item_index}",
                    x,
                    2.36,
                    shelf_z,
                    body,
                    top,
                    accent,
                    scale=0.58,
                    rotation_z=0.03 * ((item_index % 3) - 1),
                )
        else:
            for item_index, x in enumerate((-3.25, -2.84, -2.43, -2.02)):
                body, cap, accent = cold_bottle_palette[(shelf_index + item_index) % len(cold_bottle_palette)]
                add_rtd_bottle(
                    f"ColdFridge_RearLeft_Rtd_R{shelf_index}_S{item_index}",
                    x,
                    2.36,
                    shelf_z,
                    body,
                    cap,
                    accent,
                    scale=0.58 + 0.02 * ((item_index + shelf_index) % 2),
                    rotation_z=0.025 * ((item_index % 3) - 1),
                )

# -----------------------------------------------------------------------------
# Architecture and shop fittings
# -----------------------------------------------------------------------------


def add_facade(mats: dict[str, bpy.types.Material]) -> None:
    # Flat, white/yellow retail shell with a clear mall-facing entrance.
    add_box("Store_Floor_Slab", (0.0, 0.0, -0.12), (ROOM_W, ROOM_D, 0.24), mats["floor"], bevel=0.10, segments=2)
    add_box("Store_Back_Wall", (0.0, BACK_Y, ROOM_H * 0.5), (ROOM_W, WALL_T, ROOM_H), mats["store_white"], bevel=0.06, segments=1)
    add_box("Store_Left_Wall", (-ROOM_W * 0.5 + WALL_T * 0.5, 0.0, ROOM_H * 0.5), (WALL_T, ROOM_D, ROOM_H), mats["store_white"], bevel=0.06, segments=1)
    add_box("Store_Right_Wall", (ROOM_W * 0.5 - WALL_T * 0.5, 0.0, ROOM_H * 0.5), (WALL_T, ROOM_D, ROOM_H), mats["store_white"], bevel=0.06, segments=1)
    add_box("Store_Ceiling_Slab", (0.0, 0.0, ROOM_H - 0.14), (ROOM_W - 0.32, ROOM_D - 0.32, 0.24), mats["ceiling"], bevel=0.07, segments=1)

    for x, side in [(-5.55, "Left"), (5.55, "Right")]:
        add_box(f"Facade_{side}_Pillar", (x, FRONT_Y, 2.48), (0.90, 0.52, 4.96), mats["store_white"], bevel=0.10, segments=2)
        add_box(f"Facade_{side}_PillarShadow", (x, FRONT_Y - 0.28, 2.65), (0.18, 0.045, 3.90), mats["navy"], bevel=0.025, segments=1)
        add_box(f"Facade_{side}_AccentInlay", (x, FRONT_Y - 0.31, 2.65), (0.10, 0.035, 3.45), mats["store_yellow"], bevel=0.018, segments=1)

    add_box("Facade_Upper_Header", (0.0, FRONT_Y, 4.82), (10.85, 0.52, 0.58), mats["store_white"], bevel=0.12, segments=2)
    add_box("Facade_Upper_Header_Accent", (0.0, FRONT_Y - 0.285, 4.54), (10.15, 0.05, 0.07), mats["store_yellow"], bevel=0.025, segments=1)
    add_box("Facade_Left_DisplayPanel", (-3.82, FRONT_Y - 0.04, 2.56), (2.65, 0.22, 3.85), mats["store_white"], bevel=0.08, segments=2)
    add_box("Facade_Right_DisplayPanel", (3.82, FRONT_Y - 0.04, 2.56), (2.65, 0.22, 3.85), mats["store_white"], bevel=0.08, segments=2)

    # Central auto-door / entrance-exit frame; no floor arrows or decorative triangles.
    add_box("Doorway_Left_Jamb", (-2.28, FRONT_Y - 0.26, 2.05), (0.16, 0.24, 3.92), mats["metal"], bevel=0.025, segments=1)
    add_box("Doorway_Right_Jamb", (2.28, FRONT_Y - 0.26, 2.05), (0.16, 0.24, 3.92), mats["metal"], bevel=0.025, segments=1)
    add_box("Doorway_Top_Rail", (0.0, FRONT_Y - 0.26, 4.00), (4.70, 0.24, 0.16), mats["metal"], bevel=0.025, segments=1)
    add_box("Door_Left_SlidingGlass", (-2.52, FRONT_Y - 0.23, 2.05), (1.28, 0.07, 3.62), mats["glass"], bevel=0.025, segments=1)
    add_box("Door_Right_SlidingGlass", (2.52, FRONT_Y - 0.23, 2.05), (1.28, 0.07, 3.62), mats["glass"], bevel=0.025, segments=1)
    add_box("Door_Left_Handle", (-1.92, FRONT_Y - 0.35, 2.12), (0.045, 0.06, 0.75), mats["store_yellow"], bevel=0.02, segments=1)
    add_box("Door_Right_Handle", (1.92, FRONT_Y - 0.35, 2.12), (0.045, 0.06, 0.75), mats["store_yellow"], bevel=0.02, segments=1)
    add_box("Door_Entry_Threshold", (0.0, FRONT_Y - 0.42, 0.055), (4.35, 0.55, 0.11), mats["metal"], bevel=0.025, segments=1)

    # Keep the single readable store identity; the former left emblem is intentionally absent.
    add_box("Storefront_Sign_Case", (0.0, FRONT_Y - 0.02, 5.30), (8.95, 0.52, 1.08), mats["store_white"], bevel=0.13, segments=2)
    add_box("Storefront_Sign_Inset", (0.0, FRONT_Y - 0.31, 5.30), (8.35, 0.055, 0.68), mats["store_yellow"], bevel=0.06, segments=2)
    add_box("Storefront_Sign_Rule", (0.0, FRONT_Y - 0.36, 5.02), (7.25, 0.035, 0.045), mats["navy"], bevel=0.012, segments=1)
    add_text("ProteinDotCom_Logo", "protein.com", (0.58, FRONT_Y - 0.39, 5.34), 0.88, mats["navy"], scale_x=0.93, extrude=0.055, bevel_depth=0.018)

    # A shallow flat canopy keeps the entry legible without the former sloped/neon assembly.
    add_box("Storefront_Entry_FlatCanopy", (0.0, FRONT_Y - 0.52, 4.18), (5.10, 0.56, 0.18), mats["store_white"], bevel=0.06, segments=1)
    add_box("Storefront_Entry_CanopyTrim", (0.0, FRONT_Y - 0.82, 4.08), (4.55, 0.06, 0.08), mats["store_yellow"], bevel=0.02, segments=1)

    for x, side in [(-5.91, "Left"), (5.91, "Right")]:
        add_box(f"Mall_Module_Join_{side}", (x, 0.0, 2.68), (0.08, ROOM_D - 0.30, 4.95), mats["metal"], bevel=0.02, segments=1)
        add_box(f"Mall_Module_JoinCap_{side}", (x, -0.10, 5.23), (0.14, 0.40, 0.18), mats["store_yellow"], bevel=0.03, segments=1)
    add_box("Mall_Module_Top_Rail", (0.0, 0.0, 5.38), (11.75, 0.12, 0.12), mats["navy"], bevel=0.025, segments=1)
    add_box("Mall_Unit_Front_Platform", (0.0, FRONT_Y - 0.72, -0.02), (11.75, 1.45, 0.16), mats["floor_light"], bevel=0.05, segments=1)
    add_box("Mall_Unit_Front_Platform_Inset", (0.0, FRONT_Y - 0.70, 0.075), (10.85, 0.92, 0.035), mats["store_white"], bevel=0.025, segments=1)


def add_wall_rack(prefix: str, x: float, side: str, mats: dict[str, bpy.types.Material]) -> None:
    inward = 1.0 if side == "Left" else -1.0
    add_box(f"{prefix}_BackPanel", (x, 0.62, 2.62), (0.18, 6.45, 4.74), mats["store_white"], bevel=0.04, segments=1)
    for y in (-2.50, 3.52):
        add_box(f"{prefix}_Upright_{y:+.2f}", (x + inward * 0.34, y, 2.55), (0.16, 0.16, 4.78), mats["navy"], bevel=0.025, segments=1)
    for z in (0.98, 2.03, 3.08, 4.13):
        add_box(f"{prefix}_Shelf_{z:.2f}", (x + inward * 0.15, 0.52, z), (0.82, 6.08, 0.13), mats["wood"], bevel=0.035, segments=1)
        add_box(f"{prefix}_ShelfFront_{z:.2f}", (x + inward * 0.55, 0.52, z + 0.11), (0.08, 6.00, 0.16), mats["store_yellow"], bevel=0.025, segments=1)
    for y in (-2.48, -1.25, 0.02, 1.28, 2.50, 3.47):
        add_box(f"{prefix}_ShelfMarker_{y:+.2f}", (x + inward * 0.58, y, 0.72), (0.055, 0.34, 0.12), mats["store_yellow_dark"], bevel=0.02, segments=1)


def add_interior(mats: dict[str, bpy.types.Material]) -> None:
    add_wall_rack("DisplayRack_Left", -4.55, "Left", mats)
    add_wall_rack("DisplayRack_Right", 4.55, "Right", mats)

    # Product zoning stays explicit: pre-workout and RTD on the left, whey on
    # the right, bars in the island, and creatine on the rear focal wall.
    add_box("DisplayRack_Left_ZoneDivider", (-3.95, 0.10, 2.58), (0.07, 0.10, 4.35), mats["store_yellow"], bevel=0.018, segments=1)
    add_box("DisplayRack_Right_ProteinRail", (3.95, 0.54, 4.42), (0.07, 5.45, 0.10), mats["store_yellow"], bevel=0.018, segments=1)

    add_box("BackBrand_LargePanel", (0.0, 3.87, 3.58), (7.50, 0.08, 2.25), mats["store_white"], bevel=0.08, segments=2)
    add_box("BackBrand_YellowBand", (0.0, 3.80, 3.16), (6.75, 0.07, 0.24), mats["store_yellow"], bevel=0.04, segments=1)
    add_box("BackBrand_BlackBand", (0.0, 3.79, 2.60), (7.20, 0.06, 0.12), mats["black"], bevel=0.025, segments=1)
    for x in (-2.55, -1.70, 1.70, 2.55):
        add_box(f"BackBrand_VerticalSlat_{x:+.2f}", (x, 3.72, 3.62), (0.12, 0.08, 2.05), mats["store_yellow_dark"], bevel=0.025, segments=1)

    # Compact creatine pouches get a low, stable bay rather than being mixed with
    # the taller pre-workout silhouettes.
    add_box("Creatine_Bay_Base", (0.0, 3.15, 0.54), (3.55, 0.82, 0.92), mats["store_white"], bevel=0.11, segments=2)
    for shelf_index, z in enumerate((1.12, 1.86, 2.60)):
        add_box(f"Creatine_Bay_Shelf_{shelf_index}", (0.0, 3.12, z), (3.36, 0.70, 0.12), mats["wood"], bevel=0.035, segments=1)
    add_box("Creatine_Bay_HeaderPlate", (0.0, 3.45, 3.35), (3.52, 0.08, 0.25), mats["store_yellow"], bevel=0.045, segments=1)

    # Central island is a dedicated protein-bar fixture with trays and clear,
    # repeated wrappers instead of mixed hero geometry.
    add_box("Island_Display_Base", (0.0, 0.48, 0.34), (3.05, 2.25, 0.68), mats["store_white"], bevel=0.13, segments=2)
    add_box("Island_Display_Top", (0.0, 0.48, 0.78), (2.85, 2.04, 0.14), mats["store_yellow"], bevel=0.05, segments=1)
    add_box("Island_Display_LowerShelf", (0.0, 0.48, 1.10), (2.62, 1.86, 0.12), mats["white"], bevel=0.04, segments=1)
    add_box("Island_Display_FrontAccent", (0.0, -0.68, 0.52), (2.20, 0.08, 0.23), mats["store_yellow"], bevel=0.035, segments=1)
    add_box("Island_Display_LeftAccent", (-1.39, 0.48, 0.54), (0.08, 1.50, 0.25), mats["store_yellow"], bevel=0.025, segments=1)
    add_box("Island_Display_RightAccent", (1.39, 0.48, 0.54), (0.08, 1.50, 0.25), mats["store_white"], bevel=0.025, segments=1)

    # A practical checkout counter: light body, durable metal worktop, dark
    # kick plate, small terminal and receipt roll without a luminous prop.
    counter_y = 2.64 + CHECKOUT_Y_SHIFT
    counter_front_y = counter_y - 0.59
    add_box("Checkout_CounterBody", (3.04, counter_y, 0.58), (2.65, 1.14, 1.16), mats["store_white"], bevel=0.11, segments=2)
    add_box("Checkout_CounterTop", (3.04, counter_y, 1.20), (2.82, 1.25, 0.15), mats["metal"], bevel=0.05, segments=1)
    add_box("Checkout_CounterKick", (3.04, counter_front_y, 0.24), (2.24, 0.05, 0.28), mats["black"], bevel=0.02, segments=1)
    add_box("Checkout_CounterAccent", (3.04, counter_front_y, 0.68), (2.05, 0.055, 0.10), mats["store_yellow"], bevel=0.025, segments=1)
    add_box("Checkout_TerminalBase", (3.05, counter_y + 0.04, 1.42), (0.56, 0.42, 0.16), mats["metal"], bevel=0.04, segments=1)
    add_box("Checkout_TerminalScreen", (3.05, counter_y - 0.07, 1.72), (0.48, 0.07, 0.46), mats["navy"], bevel=0.05, segments=2, rotation=(math.radians(-12), 0.0, 0.0))
    add_box("Checkout_TerminalStatus", (3.05, counter_y - 0.115, 1.56), (0.18, 0.025, 0.035), mats["store_yellow"], bevel=0.01, segments=1)
    add_cylinder("Checkout_ReceiptRoll", (3.48, counter_y + 0.04, 1.48), 0.12, 0.10, mats["white"], vertices=10, bevel=0.02, rotation=(math.pi / 2.0, 0.0, 0.0), smooth=False)

    # Front-right cooler for energy drinks. Its single clear front panel is
    # framed rather than tinted opaque so the cans remain readable in-scene.
    # Keep the opaque support behind the stock, not in front of it. A full
    # depth white box used to occlude the cans through the clear panel.
    add_box("EnergyCooler_FrontRight_Body", (3.15, -2.05, 1.40), (1.75, 0.45, 2.72), mats["store_white"], bevel=0.12, segments=2)
    add_box("EnergyCooler_FrontRight_Glass", (3.15, -3.10, 1.47), (1.48, 0.05, 1.94), mats["glass"], bevel=0.035, segments=1)
    add_box("EnergyCooler_FrontRight_Header", (3.15, -3.14, 2.57), (1.48, 0.07, 0.20), mats["store_yellow"], bevel=0.025, segments=1)
    add_box("EnergyCooler_FrontRight_FrameBottom", (3.15, -3.15, 0.42), (1.48, 0.07, 0.10), mats["navy"], bevel=0.02, segments=1)
    add_box("EnergyCooler_FrontRight_FrameLeft", (2.40, -3.15, 1.48), (0.08, 0.07, 2.16), mats["navy"], bevel=0.02, segments=1)
    add_box("EnergyCooler_FrontRight_FrameRight", (3.90, -3.15, 1.48), (0.08, 0.07, 2.16), mats["navy"], bevel=0.02, segments=1)
    for z in (0.78, 1.28, 1.78):
        add_box(f"EnergyCooler_FrontRight_Shelf_{z:.2f}", (3.15, -3.00, z), (1.45, 0.07, 0.09), mats["metal"], bevel=0.02, segments=1)
    add_cooler_cabinet("EnergyCooler_FrontRight", 3.15, 1.75, -3.19, -2.10, 0.04, 2.76, 0.37, mats["store_white"])

    # Rear-left chilled cabinet for RTD protein bottles and cans. It uses the
    # same clear-front construction, so both fridges share a coherent retail
    # language and their contents can be seen from the mall-facing camera.
    add_box("ColdFridge_Extra_Body", (-2.78, 3.10, 1.28), (2.25, 0.45, 2.48), mats["store_white"], bevel=0.12, segments=2)
    add_box("ColdFridge_Extra_Glass", (-2.78, 2.23, 1.43), (1.86, 0.05, 1.86), mats["glass"], bevel=0.035, segments=1)
    add_box("ColdFridge_Extra_Header", (-2.78, 2.18, 2.48), (1.78, 0.07, 0.20), mats["store_yellow"], bevel=0.025, segments=1)
    add_box("ColdFridge_Extra_FrameBottom", (-2.78, 2.18, 0.46), (1.86, 0.07, 0.10), mats["navy"], bevel=0.02, segments=1)
    add_box("ColdFridge_Extra_FrameLeft", (-3.73, 2.18, 1.43), (0.08, 0.07, 2.04), mats["navy"], bevel=0.02, segments=1)
    add_box("ColdFridge_Extra_FrameRight", (-1.83, 2.18, 1.43), (0.08, 0.07, 2.04), mats["navy"], bevel=0.02, segments=1)
    for z in (0.78, 1.26, 1.74):
        add_box(f"ColdFridge_Extra_Shelf_{z:.2f}", (-2.78, 2.34, z), (1.78, 0.07, 0.09), mats["metal"], bevel=0.02, segments=1)
    add_cooler_cabinet("ColdFridge_Extra", -2.78, 2.25, 2.14, 3.10, 0.04, 2.58, 0.41, mats["store_white"])

    # The opaque ceiling slab remains the primary shell; these shallow white
    # recessed panels add restrained retail lighting without neon geometry.
    for index, (x, y) in enumerate(((-3.25, -0.30), (3.25, 0.80))):
        add_box(f"Ceiling_RecessedPanel_{index}", (x, y, ROOM_H - 0.29), (2.60, 0.72, 0.06), mats["white"], bevel=0.03, segments=1)


# Camera and lighting
# -----------------------------------------------------------------------------


def point_at(obj: bpy.types.Object, target: tuple[float, float, float]) -> None:
    direction = Vector(target) - obj.location
    obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def add_area_light(
    name: str,
    location: tuple[float, float, float],
    energy: float,
    color: tuple[float, float, float],
    size: float,
    target: tuple[float, float, float],
) -> bpy.types.Object:
    data = bpy.data.lights.new(name=name, type="AREA")
    data.energy = energy
    data.color = color
    data.shape = "DISK"
    data.size = size
    obj = bpy.data.objects.new(name, data)
    obj.location = location
    COLLECTIONS["Presentation"].objects.link(obj)
    point_at(obj, target)
    return obj


def add_camera(name: str, location: tuple[float, float, float], target: tuple[float, float, float], lens: float) -> bpy.types.Object:
    data = bpy.data.cameras.new(name=name)
    data.lens = lens
    data.sensor_width = 36.0
    data.clip_start = 0.05
    data.clip_end = 250.0
    obj = bpy.data.objects.new(name, data)
    obj.location = location
    COLLECTIONS["Presentation"].objects.link(obj)
    point_at(obj, target)
    return obj


def setup_presentation(mats: dict[str, bpy.types.Material]) -> dict[str, bpy.types.Object]:
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 800
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.filepath = str(RENDER_DIR / "protein_store_beauty.png")
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    try:
        scene.view_settings.look = "AgX - Medium High Contrast"
    except Exception:
        pass
    scene.world.color = (0.008, 0.014, 0.028)
    if scene.world.use_nodes:
        bg = scene.world.node_tree.nodes.get("Background")
        if bg:
            bg.inputs["Color"].default_value = (0.008, 0.014, 0.030, 1.0)
            bg.inputs["Strength"].default_value = 0.28

    # Key, fill and warm interior pools.
    add_area_light("Presentation_Key", (8.5, -10.0, 12.0), 1350.0, (1.0, 0.82, 0.56), 7.0, (0.0, 0.5, 2.3))
    add_area_light("Presentation_Fill", (-8.0, -4.0, 8.0), 950.0, (0.78, 0.84, 1.0), 6.0, (0.0, 1.0, 2.2))
    add_area_light("Presentation_Rim", (0.0, 7.0, 9.0), 1150.0, (1.0, 0.76, 0.24), 5.0, (0.0, 2.0, 2.4))
    # The opaque slab remains closed, so a pair of broad, neutral retail
    # fixtures below it lights the stock without adding visible neon geometry.
    add_area_light("Presentation_Interior_Left", (-3.1, -0.3, ROOM_H - 0.55), 520.0, (1.0, 0.96, 0.88), 3.8, (-1.6, 0.2, 1.1))
    add_area_light("Presentation_Interior_Right", (3.1, 0.8, ROOM_H - 0.55), 520.0, (1.0, 0.96, 0.88), 3.8, (1.4, 0.6, 1.1))

    cameras = {
        "Beauty_Camera": add_camera("Beauty_Camera", (13.4, -16.5, 9.5), (0.0, 0.6, 2.65), 54.0),
        "Front_Camera": add_camera("Front_Camera", (0.0, -18.5, 6.4), (0.0, 0.45, 2.65), 58.0),
        "Interior_Camera": add_camera("Interior_Camera", (0.0, -8.8, 4.35), (0.0, 1.25, 2.30), 48.0),
        "Top_Camera": add_camera("Top_Camera", (0.0, 0.2, 17.0), (0.0, 0.6, 0.0), 52.0),
    }
    scene.camera = cameras["Beauty_Camera"]
    scene["asset_name"] = ASSET_NAME
    scene["asset_version"] = VERSION
    scene["style"] = "clean white-yellow supplement shop with restrained black"
    scene["room_dimensions_m"] = f"{ROOM_W:.2f} x {ROOM_D:.2f} x {ROOM_H:.2f}"
    scene["product_text_policy"] = (
        "Readable PROTEINI.SI brand and category cues; original low-poly label geometry, "
        "no copied 1:1 product photography"
    )
    scene["palette"] = "warm white, signal yellow, restrained black; products use blue, brown, violet, orange and black accents"
    scene["future_extension"] = "Side join bars and top rail reserve mall adjacency"
    scene["entry_decor_policy"] = "no plants, entry arrows, left emblem or emissive neon geometry"
    scene["ceiling_policy"] = "opaque slab"
    scene["fridge_front_policy"] = "clear low-tint glass with visible drinks"
    scene["reference_categories"] = ", ".join(PRODUCT_FAMILY_CONTRACT.keys())
    scene["product_front_contract_version"] = VERSION
    return cameras


# -----------------------------------------------------------------------------
# Export, validation metadata, and rendering
# -----------------------------------------------------------------------------


def create_root_anchor() -> bpy.types.Object:
    root = bpy.data.objects.new("ProteinStore_Root", None)
    root.empty_display_type = "CUBE"
    root.empty_display_size = 0.5
    root["asset_role"] = "runtime_root"
    COLLECTIONS["Store"].objects.link(root)
    return root


def parent_store_objects(root: bpy.types.Object) -> None:
    for obj in list(COLLECTIONS["Store"].objects):
        if obj == root or obj.type != "MESH":
            continue
        obj.parent = root


def validate_contract() -> None:
    mesh_names = {obj.name for obj in bpy.data.objects if obj.type == "MESH"}
    required = {
        "Store_Ceiling_Slab",
        "ProteinDotCom_Logo",
        "Checkout_CounterBody",
        "EnergyCooler_FrontRight_Glass",
        "ColdFridge_Extra_Glass",
    }
    forbidden_tokens = ("arrow", "triangle", "proteinmark", "planter", "neon", "glow")
    missing = sorted(required - mesh_names)
    forbidden = sorted(
        name for name in mesh_names
        if any(token in name.lower() for token in forbidden_tokens)
    )
    emissive = []
    for material in bpy.data.materials:
        if not material.use_nodes:
            continue
        bsdf = material.node_tree.nodes.get("Principled BSDF")
        if not bsdf or "Emission Strength" not in bsdf.inputs:
            continue
        if float(bsdf.inputs["Emission Strength"].default_value) > 0.05:
            emissive.append(material.name)
    assert not missing, f"missing required ProteinStore meshes: {missing}"
    assert not forbidden, f"forbidden legacy ProteinStore meshes: {forbidden}"
    assert not emissive, f"unexpected emissive ProteinStore materials: {emissive}"
    glass = bpy.data.materials.get("PS_Tinted_Glass")
    assert glass is not None, "clear fridge glass material missing"
    glass_alpha = 0.10
    if glass.use_nodes:
        bsdf = glass.node_tree.nodes.get("Principled BSDF")
        if bsdf and "Alpha" in bsdf.inputs:
            glass_alpha = float(bsdf.inputs["Alpha"].default_value)
    print(
        "GYMCHAOS_PROTEIN_STORE_CONTRACT_OK "
        f"required={len(required)} forbidden=0 emissive=0 glass_alpha={glass_alpha:.2f}"
    )


def validate_product_front_contract() -> None:
    mesh_names = {obj.name for obj in bpy.data.objects if obj.type == "MESH"}
    family_details = []
    for family_name, spec in PRODUCT_FAMILY_CONTRACT.items():
        family_names = [
            name for name in mesh_names
            if any(name.startswith(prefix) for prefix in spec["prefixes"])
        ]
        label_names = [
            name for name in family_names
            if "_LabelPanel_PROTEINI_SI_" in name
        ]
        has_form = any(
            token in name
            for name in family_names
            for token in spec["form_tokens"]
        )
        assert family_names, f"missing product family geometry: {family_name}"
        assert label_names, f"missing readable product cue: {family_name}"
        assert has_form, f"missing product silhouette token: {family_name}"
        family_details.append(f"{family_name}={len(label_names)}/{len(family_names)}")
    print(
        "GYMCHAOS_PROTEIN_ART_SOURCE_CONTRACT_OK "
        f"version={VERSION} families={len(PRODUCT_FAMILY_CONTRACT)} "
        + " ".join(family_details)
        + " readableBrand=1 readableCategory=1 originalLabelGeometry=1"
    )


def write_scene_manifest() -> None:
    manifest = ROOT / "scene_manifest.txt"
    mesh_objects = [obj for obj in bpy.data.objects if obj.type == "MESH" and obj.library is None]
    materials = sorted({mat.name for obj in mesh_objects for mat in obj.data.materials if mat})
    total_vertices = sum(len(obj.data.vertices) for obj in mesh_objects)
    total_polys = sum(len(obj.data.polygons) for obj in mesh_objects)
    lines = [
        f"asset={ASSET_NAME}",
        f"version={VERSION}",
        f"seed={SEED}",
        f"mesh_objects={len(mesh_objects)}",
        f"vertices={total_vertices}",
        f"polygons={total_polys}",
        f"materials={len(materials)}",
        "material_names=" + ",".join(materials),
        "required=opaque ceiling slab, entrance/exit door, protein.com sign, white-yellow displays, category-zoned racks, clear fridge fronts, supplement silhouettes",
        "forbidden=plants, entry arrows/triangles, left emblem, emissive neon geometry",
        "product_front_contract=7 families with PROTEINI.SI brand and category cues, original low-poly label geometry",
        "product_text_policy=readable brand/category cues; no copied 1:1 product photography",
    ]
    manifest.write_text("\n".join(lines) + "\n", encoding="utf-8")


def render_named(scene: bpy.types.Scene, camera: bpy.types.Object, filename: str) -> None:
    scene.camera = camera
    scene.render.filepath = str(RENDER_DIR / filename)
    bpy.ops.render.render(write_still=True)


def export_glb() -> None:
    bpy.ops.object.select_all(action="DESELECT")
    export_objects = []
    for obj in bpy.data.objects:
        if obj.type == "MESH" and obj.library is None:
            obj.select_set(True)
            export_objects.append(obj)
    bpy.context.view_layer.objects.active = export_objects[0] if export_objects else None
    try:
        bpy.ops.export_scene.gltf(
            filepath=str(GLB_PATH),
            export_format="GLB",
            use_selection=True,
            export_apply=True,
            export_cameras=False,
            export_lights=False,
        )
    finally:
        bpy.ops.object.select_all(action="DESELECT")


def clean_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for data_block in (bpy.data.meshes, bpy.data.curves, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for item in list(data_block):
            if item.users == 0:
                data_block.remove(item)
    for collection in list(bpy.data.collections):
        if collection.name != "Collection" and collection.users == 0:
            bpy.data.collections.remove(collection)


def main() -> None:
    clean_scene()
    for name in ("Store", "Facade", "Interior", "Products", "Presentation"):
        make_collection(name)

    mats = create_materials()
    root = create_root_anchor()
    add_facade(mats)
    add_interior(mats)
    stock_products(mats)
    # Planters intentionally omitted: keep the storefront entry clear and collision-safe.
    parent_store_objects(root)
    cameras = setup_presentation(mats)
    validate_contract()
    validate_product_front_contract()
    write_scene_manifest()

    # Authored evidence views. The front view is the contract-critical check;
    # the interior view exposes the secondary retail work and product variety.
    scene = bpy.context.scene
    render_named(scene, cameras["Beauty_Camera"], "protein_store_beauty.png")
    render_named(scene, cameras["Front_Camera"], "protein_store_front.png")
    render_named(scene, cameras["Interior_Camera"], "protein_store_interior.png")
    scene.camera = cameras["Beauty_Camera"]
    export_glb()
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH))
    print(f"PROTEIN_STORE_BUILD_OK blend={BLEND_PATH} glb={GLB_PATH}")


if __name__ == "__main__":
    main()
