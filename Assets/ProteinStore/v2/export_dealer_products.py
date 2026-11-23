"""Homeless Hank's stash for the dealer overlay's 3D inspect view.

    "C:/Program Files/Blender Foundation/Blender 4.5/blender.exe" -b --factory-startup \
        -P Assets/ProteinStore/v2/export_dealer_products.py [-- --no-render]

Five low-poly products after the reference photos, with less on the labels
than the real packs. The "?????" unknowns belong to the stash menu only;
the packs themselves carry plain print:
  dealer_tren    amber 10 ml vial, blue crimp cap, white/blue label
  dealer_anavar  white tub, red banner, grey band
  dealer_mk677   black tub, white stripe, red name
  dealer_rad140  white tub, dark green panel
  dealer_bpc157  black tub, blue lower band
Each model sits at the origin with its label facing Blender -Y, like the
protein.com products, so GymProductInspectView shows the label first.

Outputs:
  GymChaos/Assets/StreamingAssets/BodyBuilders/protein_products/<id>.glb
  Assets/ProteinStore/v2/renders/products/<id>.png (unless --no-render)
"""
import math
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
OUT = REPO / "GymChaos" / "Assets" / "StreamingAssets" / "BodyBuilders" / "protein_products"
RENDERS = HERE / "renders" / "products"
SEGMENTS = 16


def srgb(hex_color):
    value = hex_color.lstrip("#")
    rgb = [int(value[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]
    # Principled base colour is linear.
    return tuple(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in rgb) + (1.0,)


MATERIALS = {}


def material(name, hex_color, roughness=0.5, metallic=0.0):
    key = (name, hex_color)
    if key in MATERIALS:
        return MATERIALS[key]
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = srgb(hex_color)
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    MATERIALS[key] = mat
    return mat


def lathe(name, profile, mat, segments=SEGMENTS, cap_bottom=True, cap_top=True):
    """Revolves (radius, z) points about Z into a closed low-poly shell."""
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    rings = []
    for radius, z in profile:
        ring = []
        for i in range(segments):
            angle = 2 * math.pi * i / segments
            ring.append(bm.verts.new((radius * math.cos(angle), radius * math.sin(angle), z)))
        rings.append(ring)
    for a, b in zip(rings, rings[1:]):
        for i in range(segments):
            j = (i + 1) % segments
            bm.faces.new((a[i], a[j], b[j], b[i]))
    if cap_bottom:
        bm.faces.new(list(reversed(rings[0])))
    if cap_top:
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    for poly in obj.data.polygons:
        poly.use_smooth = True
    return obj


def band(name, radius, z0, z1, mat, arc=None, segments=SEGMENTS * 2):
    """Label band slightly outside the body. arc=(start, end) degrees around
    -Y keeps it to the front; None wraps all the way."""
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    if arc is None:
        angles = [2 * math.pi * i / segments for i in range(segments)]
        closed = True
    else:
        steps = max(2, int(segments * (arc[1] - arc[0]) / 360))
        angles = [math.radians(-90 + arc[0] + (arc[1] - arc[0]) * i / steps) for i in range(steps + 1)]
        closed = False
    low = [bm.verts.new((radius * math.cos(a), radius * math.sin(a), z0)) for a in angles]
    high = [bm.verts.new((radius * math.cos(a), radius * math.sin(a), z1)) for a in angles]
    count = len(angles) if closed else len(angles) - 1
    for i in range(count):
        j = (i + 1) % len(angles)
        bm.faces.new((low[i], low[j], high[j], high[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for face in bm.faces:
        center = face.calc_center_median()
        if face.normal.dot(Vector((center.x, center.y, 0.0))) < 0:
            face.normal_flip()
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    for poly in obj.data.polygons:
        poly.use_smooth = True
    return obj


def text(name, body, size, z, radius, mat, bold=True, x=0.0):
    """Flat text plate on the -Y face, just outside the label radius."""
    curve = bpy.data.curves.new(name, "FONT")
    curve.body = body
    curve.size = size
    curve.align_x = "CENTER"
    curve.align_y = "CENTER"
    curve.resolution_u = 2
    curve.extrude = 0.0
    if bold:
        curve.offset = size * 0.012
    obj = bpy.data.objects.new(name, curve)
    bpy.context.collection.objects.link(obj)
    obj.rotation_euler = (math.radians(90), 0.0, 0.0)
    obj.location = (x, -(radius + 0.0006), z)
    bpy.context.view_layer.objects.active = obj
    for other in bpy.context.selected_objects:
        other.select_set(False)
    obj.select_set(True)
    bpy.ops.object.convert(target="MESH")
    obj = bpy.context.active_object
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    bpy.context.view_layer.update()
    # Bend the plate around the tub so its ends do not float off the label.
    for vertex in obj.data.vertices:
        world = obj.matrix_world @ vertex.co
        angle = world.x / radius
        r = radius + 0.0006
        world.x = r * math.sin(angle)
        world.y = -r * math.cos(angle)
        vertex.co = obj.matrix_world.inverted() @ world
    return obj


def tub(prefix, radius, height, body_hex, cap_hex, cap_height, body_rough=0.35):
    """Supplement tub: rounded shoulder, ribbed screw cap."""
    r, h = radius, height
    profile = [(r * 0.94, 0.0), (r, 0.004), (r, h * 0.9), (r * 0.96, h * 0.95), (r * 0.78, h * 0.98),
               (r * 0.78, h)]
    lathe(prefix + "_body", profile, material(prefix + " body", body_hex, body_rough))
    cap = [(r * 0.84, h), (r * 0.86, h + 0.002), (r * 0.86, h + cap_height - 0.003),
           (r * 0.82, h + cap_height)]
    lathe(prefix + "_cap", cap, material(prefix + " cap", cap_hex, 0.45))
    return h + cap_height


def build_tren():
    # 10 ml injectable vial: amber oil in clear glass, crimped blue cap.
    r, h = 0.0125, 0.042
    # The oil fills the glass to 72%; above it the empty glass shoulder.
    lathe("tren_oil", [(r * 0.96, 0.0), (r, 0.002), (r, h * 0.72)], material("tren oil", "#B8740E", 0.12))
    glass = [(r, h * 0.72), (r, h * 0.78), (r * 0.98, h * 0.8), (r * 0.62, h * 0.86), (r * 0.6, h * 0.92)]
    lathe("tren_glass", glass, material("tren glass top", "#AEB9BE", 0.08), cap_bottom=False)
    lathe("tren_crimp", [(r * 0.72, h * 0.9), (r * 0.74, h * 0.91), (r * 0.74, h * 1.0), (r * 0.7, h * 1.01)],
          material("tren crimp", "#A9B0B6", 0.3, 0.8))
    lathe("tren_flip", [(r * 0.78, h * 1.01), (r * 0.8, h * 1.02), (r * 0.8, h * 1.1), (r * 0.74, h * 1.11)],
          material("tren flip", "#2E64B8", 0.4))
    label_r = r + 0.0004
    band("tren_label", label_r, h * 0.12, h * 0.68, material("tren label", "#F1EDE3", 0.6), arc=(-75, 75))
    band("tren_stripe_top", label_r + 0.0002, h * 0.6, h * 0.66, material("tren blue", "#2753A8", 0.5),
         arc=(-75, 75))
    band("tren_stripe_low", label_r + 0.0002, h * 0.14, h * 0.19, material("tren blue", "#2753A8", 0.5),
         arc=(-75, 75))
    ink = material("tren ink", "#1A2030", 0.6)
    text("tren_name", "TREN", 0.0075, h * 0.46, label_r + 0.0003, ink)
    text("tren_small", "100 mg/ml  10ml", 0.0032, h * 0.29, label_r + 0.0003, ink, bold=False)


def build_anavar():
    r, h = 0.03, 0.095
    tub("anavar", r, h, "#F4F4F2", "#F7F7F5", 0.03)
    label_r = r + 0.0005
    band("anavar_band_top", label_r, h * 0.84, h * 0.88, material("anavar grey", "#B9BBBD", 0.5))
    band("anavar_band_low", label_r, h * 0.1, h * 0.14, material("anavar grey", "#B9BBBD", 0.5))
    red = material("anavar red", "#D7262E", 0.45)
    band("anavar_banner", label_r + 0.0002, h * 0.5, h * 0.68, red, arc=(-70, 70))
    white = material("anavar white ink", "#FFFFFF", 0.5)
    text("anavar_name", "ANAVAR", 0.0125, h * 0.59, label_r + 0.0005, white)
    dark = material("anavar ink", "#2A2A2A", 0.6)
    text("anavar_brand", "pharma", 0.006, h * 0.75, label_r, dark)
    text("anavar_lines", "MUSCLE GROWTH\nPERFORMANCE", 0.0042, h * 0.35, label_r, dark, bold=False)


def build_mk677():
    r, h = 0.03, 0.1
    tub("mk677", r, h, "#141414", "#0E0E0E", 0.032, body_rough=0.3)
    label_r = r + 0.0005
    white = material("mk white", "#F2F2F2", 0.5)
    band("mk_stripe", label_r, h * 0.78, h * 0.8, white, arc=(-120, 120))
    band("mk_bottom", label_r, h * 0.06, h * 0.12, white, arc=(-120, 120))
    red = material("mk red", "#D11F2A", 0.45)
    text("mk_name", "MK-677", 0.014, h * 0.56, label_r + 0.0002, red)
    text("mk_small", "GH SECRETAGOGUE", 0.0055, h * 0.4, label_r + 0.0002, white, bold=False)
    text("mk_brand", "T W P", 0.0065, h * 0.88, label_r + 0.0002, white)


def build_rad140():
    r, h = 0.029, 0.098
    tub("rad140", r, h, "#F5F6F4", "#F2F3F1", 0.03)
    label_r = r + 0.0005
    green = material("rad green", "#18412E", 0.5)
    band("rad_panel", label_r + 0.0002, h * 0.72, h * 0.86, green, arc=(-60, 60))
    band("rad_lower", label_r + 0.0002, h * 0.22, h * 0.32, green, arc=(-60, 60))
    ink = material("rad ink", "#16231C", 0.6)
    text("rad_name", "RAD-140", 0.0125, h * 0.57, label_r + 0.0004, ink)
    text("rad_sub", "(TESTOLONE)", 0.0065, h * 0.46, label_r + 0.0004, ink, bold=False)
    white = material("rad white ink", "#FFFFFF", 0.5)
    text("rad_panel_text", "RESEARCH USE ONLY", 0.0042, h * 0.79, label_r + 0.0005, white, bold=False)
    text("rad_lower_text", "60  10 MG  99%", 0.0045, h * 0.27, label_r + 0.0005, white, bold=False)


def build_bpc157():
    r, h = 0.029, 0.105
    tub("bpc157", r, h, "#1B1E22", "#17191C", 0.032, body_rough=0.3)
    label_r = r + 0.0005
    blue = material("bpc blue", "#1C8FD6", 0.45)
    band("bpc_band", label_r, h * 0.06, h * 0.32, blue)
    white = material("bpc white", "#F4F6F8", 0.5)
    text("bpc_name", "BPC-157", 0.013, h * 0.58, label_r + 0.0002, white)
    text("bpc_brand", "sport", 0.006, h * 0.78, label_r + 0.0002, white)
    text("bpc_sub", "- JOINT & MUSCLE -", 0.0045, h * 0.47, label_r + 0.0002, blue, bold=False)
    text("bpc_band_text", "BLEND OF AMINO ACIDS", 0.0045, h * 0.19, label_r + 0.0004, white, bold=False)


PRODUCTS = {
    "dealer_tren": build_tren,
    "dealer_anavar": build_anavar,
    "dealer_mk677": build_mk677,
    "dealer_rad140": build_rad140,
    "dealer_bpc157": build_bpc157,
}


def clear_scene():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for mesh in list(bpy.data.meshes):
        bpy.data.meshes.remove(mesh)
    for curve in list(bpy.data.curves):
        bpy.data.curves.remove(curve)


def join_all(name):
    objects = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    for o in bpy.context.selected_objects:
        o.select_set(False)
    for o in objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    joined = bpy.context.active_object
    joined.name = name
    return joined


def triangles(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def export(obj, path):
    for o in bpy.context.selected_objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.ops.export_scene.gltf(filepath=str(path), export_format="GLB", use_selection=True,
                              export_apply=True, export_yup=True)


def render(obj, path):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = 512
    scene.render.resolution_y = 512
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "Standard"
    world = bpy.data.worlds.new("Render World")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.18, 0.2, 0.24, 1.0)
    scene.world = world
    bounds = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    center = sum(bounds, Vector()) / 8
    size = max((max(b[i] for b in bounds) - min(b[i] for b in bounds)) for i in range(3))
    cam_data = bpy.data.cameras.new("Cam")
    cam_data.lens = 70
    cam = bpy.data.objects.new("Cam", cam_data)
    bpy.context.collection.objects.link(cam)
    cam.location = center + Vector((size * 0.9, -size * 3.0, size * 0.9))
    cam.rotation_euler = (center - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    light_data = bpy.data.lights.new("Key", "AREA")
    light_data.energy = 14.0 * (size / 0.1) ** 2
    light_data.size = size * 2
    light = bpy.data.objects.new("Key", light_data)
    bpy.context.collection.objects.link(light)
    light.location = center + Vector((size * 1.5, -size * 2.0, size * 2.0))
    light.rotation_euler = (center - light.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    for extra in (cam, light):
        bpy.data.objects.remove(extra, do_unlink=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    do_render = "--no-render" not in argv
    OUT.mkdir(parents=True, exist_ok=True)
    RENDERS.mkdir(parents=True, exist_ok=True)
    for product_id, builder in PRODUCTS.items():
        clear_scene()
        builder()
        obj = join_all(product_id)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
        export(obj, OUT / (product_id + ".glb"))
        print(f"DEALER_PRODUCT {product_id} tris={triangles(obj)} materials={len(obj.data.materials)}")
        if do_render:
            render(obj, RENDERS / (product_id + ".png"))
    print("DEALER_PRODUCTS_OK")


main()
