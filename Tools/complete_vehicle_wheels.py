"""Make split vehicle wheels whole so they can spin without artefacts.

Runs on the output of Tools/split_vehicle_wheels.py (body mesh + ``Wheel_*``
objects with their origin on the axle). The split kept the tread above the
axle on the body, because the fender hugs it there, and the scans never
captured the inner sidewall. A spinning wheel therefore carried a missing
band round with it while a static strip of tyre stayed on the body.

For every wheel this script:
  1. rebuilds the tyre band above the axle from the complete lower band
     turned 180 degrees (the rim keeps its own spokes), and drops stray
     fender/brake faces that orbit outside the tread;
  2. removes the leftover static tyre faces from the body inside the wheel
     cylinder above the axle;
  3. closes the inner side with the tyre's own full inner sidewall disc, so
     the hollow scan interior is never visible.

Usage:
  blender -b --factory-startup -P Tools/complete_vehicle_wheels.py -- \
      --input <split.fbx|glb> --output <out.fbx|glb> [--render out_prefix] \
      [--render-only]
"""
import argparse
import math
import sys
from pathlib import Path

import bmesh
import bpy
import mathutils
import numpy as np

AXES = "XYZ"


def parse_args():
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--output")
    parser.add_argument("--render")
    parser.add_argument("--render-only", action="store_true")
    parser.add_argument("--keep-body-tyre", action="store_true")
    parser.add_argument("--hide-body", action="store_true")
    parser.add_argument("--reference",
                        help="full-detail file whose wheel radius/width a LOD reuses")
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return parser.parse_args(argv)


def import_any(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if path.lower().endswith(".fbx"):
        bpy.ops.import_scene.fbx(filepath=path, use_custom_props=True)
    else:
        bpy.ops.import_scene.gltf(filepath=path)
    wheels = [o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("Wheel_")]
    bodies = [o for o in bpy.data.objects if o.type == "MESH" and not o.name.startswith("Wheel_")]
    if not wheels or len(bodies) != 1:
        raise RuntimeError(f"expected one body and wheels, found {len(bodies)} / {len(wheels)}")
    return bodies[0], wheels


def wheel_frame(wheel, body):
    """Axle origin, axle axis (world), outward sign and radius."""
    origin = wheel.matrix_world.translation.copy()
    axis_name = str(wheel.get("wheel_axis", ""))
    if axis_name not in ("X", "Y", "Z"):
        # Fall back to the thinnest world extent of the wheel.
        co = np.array([tuple(wheel.matrix_world @ v.co) for v in wheel.data.vertices])
        axis_name = AXES[int(np.argmin(co.max(0) - co.min(0)))]
    axis = mathutils.Vector((0, 0, 0))
    axis[AXES.index(axis_name)] = 1.0
    body_centre = sum((body.matrix_world @ mathutils.Vector(c) for c in body.bound_box),
                      mathutils.Vector()) / 8.0
    outward = 1.0 if (origin - body_centre).dot(axis) > 0 else -1.0
    radius = float(wheel.get("wheel_radius", 0.0))
    return origin, axis, outward, radius


RIM_KEEP = 0.68     # scanned faces fully inside this radius are the rim
TYRE_INNER = 0.58   # procedural tyre lip radius, in front of the rim cut
SEGMENTS = 64


def measure_wheel(wheel, origin, axis, outward, radius):
    mw = wheel.matrix_world
    co = np.array([tuple(mw @ v.co - origin) for v in wheel.data.vertices])
    ax = np.array(tuple(axis))
    along = co @ ax * outward
    radial = np.linalg.norm(co - np.outer(co @ ax, ax), axis=1)
    if radius <= 0.0:
        radius = float(np.percentile(radial, 99.0))
    # Width from the scanned tyre band only: axle and brake parts reach
    # further inwards and would make the tyre far too wide.
    band = along[radial > 0.8 * radius]
    return radius, float(np.percentile(band, 2.0)), float(np.percentile(band, 98.0))


def scan_image(obj):
    for mat in obj.data.materials:
        if mat and mat.use_nodes:
            for node in mat.node_tree.nodes:
                if node.type == "TEX_IMAGE" and node.image is not None:
                    return node.image
    return None


def pick_rubber_uv(wheel, bm, uv_layer, faces, radial, radius):
    """UV of a dark, even tread texel among the scanned tyre faces."""
    image = scan_image(wheel)
    if image is None or uv_layer is None or image.size[0] == 0:
        return None
    w, h = image.size
    pixels = np.array(image.pixels[:], dtype=np.float32).reshape(h, w, image.channels)
    candidates = []
    for f in faces:
        if min(radial[v] for v in f.verts) < 0.85 * radius:
            continue
        for loop in f.loops:
            u, v = loop[uv_layer].uv
            x = int((u % 1.0) * (w - 1))
            y = int((v % 1.0) * (h - 1))
            # Even neighbourhood only, so a UV inset or mip blend stays dark.
            patch = pixels[max(0, y - 2):y + 3, max(0, x - 2):x + 3, :3]
            lum = patch.mean(axis=2)
            candidates.append((float(lum.mean()), float(lum.std()), (u, v)))
    if not candidates:
        return None
    lums = np.array([c[0] for c in candidates])
    target = float(np.percentile(lums, 25.0))
    best = min(candidates, key=lambda c: abs(c[0] - target) + 4.0 * c[1])
    print(f"WHEEL_RUBBER_TEXEL {wheel.name} lum={best[0]:.3f} std={best[1]:.3f}")
    return mathutils.Vector(best[2])


def complete_wheel(wheel, origin, axis, outward, radius, a_in, a_out):
    """Keep the scanned rim, replace the noisy scanned tyre with a clean one.

    The scanned tread was trimmed under the fender and ragged at the arch,
    so any part of it orbiting into view looked torn. A lathed tyre with the
    fitted radius and width is identical at every spin angle; its inner
    sidewall is a full disc that closes the hollow scan interior.
    """
    mw = wheel.matrix_world
    inv = mw.inverted()
    bm = bmesh.new()
    bm.from_mesh(wheel.data)
    world = {v: mw @ v.co for v in bm.verts}
    rel = {v: world[v] - origin for v in bm.verts}
    along = {v: rel[v].dot(axis) * outward for v in bm.verts}
    radial = {v: (rel[v] - rel[v].project(axis)).length for v in bm.verts}
    doomed = [f for f in bm.faces
              if max(radial[v] for v in f.verts) > RIM_KEEP * radius]
    uv_layer = bm.loops.layers.uv.active
    rubber_uv = pick_rubber_uv(wheel, bm, uv_layer, doomed, radial, radius)
    bmesh.ops.delete(bm, geom=doomed, context="FACES")
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")

    if rubber_uv is not None:
        # Same material and texture as the scan, sampled at a dark tread
        # texel: the wheel stays one mesh primitive with one material.
        slot = 0
    else:
        mat = rubber_material()
        if mat.name not in [m.name for m in wheel.data.materials if m]:
            wheel.data.materials.append(mat)
        slot = [m.name if m else "" for m in wheel.data.materials].index(mat.name)

    width = a_out - a_in
    # The lip sits just in front of the outermost remaining rim faces near
    # the cut, so their ragged edge is hidden behind clean rubber.
    rim_front = [along[v] for v in bm.verts
                 if v.is_valid and radial[v] > 0.5 * radius]
    lip_face = min(a_out + 0.15 * width,
                   max([a_out - 0.04 * width] + [x + 0.01 * width for x in rim_front]))
    ri = TYRE_INNER * radius
    ro = radius
    shoulder = min(0.18 * width, 0.12 * radius)
    bulge = 0.06 * width
    # Cross-section (radius, along) from the outer sidewall over the tread to
    # the inner sidewall; the inner side closes to the hub as a full disc.
    outer_face = a_out - 0.04 * width
    inner_face = a_in + 0.02 * width
    profile = [(ri, lip_face), (ri + 0.12 * radius, lip_face)]
    ri = ri + 0.12 * radius
    for t in (0.35, 0.7):
        profile.append((ri + (ro - shoulder - ri) * t, outer_face + bulge * math.sin(math.pi * t)))
    for k in range(5):
        ang = (math.pi / 2) * k / 4
        profile.append((ro - shoulder + shoulder * math.sin(ang),
                        outer_face - shoulder + shoulder * math.cos(ang)))
    for k in range(5):
        ang = (math.pi / 2) * k / 4
        profile.append((ro - shoulder + shoulder * math.cos(ang),
                        inner_face + shoulder - shoulder * math.sin(ang)))
    for t in (0.3, 0.65, 1.0):
        profile.append((ro - shoulder - (ro - shoulder) * t, inner_face))

    e1 = axis.orthogonal().normalized()
    e2 = axis.cross(e1).normalized()
    rings = []
    for r_p, a_p in profile:
        if r_p <= 1e-9:
            hub = bm.verts.new(inv @ (origin + axis * outward * a_p))
            rings.append([hub] * SEGMENTS)
            continue
        ring = []
        for i in range(SEGMENTS):
            t = 2 * math.pi * i / SEGMENTS
            p = origin + axis * outward * a_p + (e1 * math.cos(t) + e2 * math.sin(t)) * r_p
            ring.append(bm.verts.new(inv @ p))
        rings.append(ring)
    tyre_faces = []
    for ra, rb in zip(rings, rings[1:]):
        for i in range(SEGMENTS):
            j = (i + 1) % SEGMENTS
            quad = [ra[i], ra[j], rb[j], rb[i]]
            verts = [v for k, v in enumerate(quad) if v not in quad[:k]]
            if len(verts) < 3:
                continue
            try:
                face = bm.faces.new(verts)
            except ValueError:
                continue
            face.material_index = slot
            face.smooth = True
            if rubber_uv is not None and uv_layer is not None:
                for loop in face.loops:
                    loop[uv_layer].uv = rubber_uv
            tyre_faces.append(face)
    # Outward-facing normals: away from the tyre's own centre line.
    tyre_faces = [f for f in tyre_faces if f.is_valid]
    centre_local = inv @ (origin + axis * outward * (inner_face + outer_face) * 0.5)
    for f in tyre_faces:
        c = f.calc_center_median()
        n = f.normal
        r_vec = c - centre_local
        if n.length > 0 and n.dot(r_vec) < 0:
            f.normal_flip()
    bm.to_mesh(wheel.data)
    bm.free()
    wheel.data.update()
    return len(doomed), len(tyre_faces), radius, a_in, a_out


def strip_body(body, wheels_info):
    mw = body.matrix_world
    bm = bmesh.new()
    bm.from_mesh(body.data)
    world = {v: mw @ v.co for v in bm.verts}
    doomed = []
    for f in bm.faces:
        c = sum((world[v] for v in f.verts), mathutils.Vector()) / len(f.verts)
        for origin, axis, outward, radius, w_lo, w_hi in wheels_info:
            rel = c - origin
            along = rel.dot(axis) * outward
            if along < w_lo or along > w_hi:
                continue
            radial = (rel - rel.project(axis)).length
            if radial <= radius * 0.99:
                doomed.append(f)
                break
    bmesh.ops.delete(bm, geom=doomed, context="FACES")
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm.to_mesh(body.data)
    bm.free()
    body.data.update()
    return len(doomed)


def rubber_material():
    mat = bpy.data.materials.get("Wheel inner rubber")
    if mat is None:
        mat = bpy.data.materials.new("Wheel inner rubber")
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes["Principled BSDF"]
        bsdf.inputs["Base Color"].default_value = (0.045, 0.045, 0.05, 1.0)
        bsdf.inputs["Roughness"].default_value = 0.85
    return mat


def render(prefix, body, wheels, spin_degrees):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.render.resolution_x = 1200
    scene.render.resolution_y = 700
    scene.world = bpy.data.worlds.new("w")
    cam_data = bpy.data.cameras.new("cam")
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    rests = {w: w.matrix_world.copy() for w in wheels}
    target = wheels[0]
    origin, axis, outward, radius = wheel_frame(target, body)
    for deg in spin_degrees:
        for w in wheels:
            o, ax, _, _ = wheel_frame_cached[w]
            rot = mathutils.Matrix.Translation(o) @ mathutils.Matrix.Rotation(
                math.radians(deg), 4, ax) @ mathutils.Matrix.Translation(-o)
            w.matrix_world = rot @ rests[w]
        for view, offset in (("side", axis * outward * 6.0),
                             ("low", axis * outward * 4.5 + mathutils.Vector((0, 0, -0.6)) * radius * 4)):
            direction = offset.normalized()
            cam.location = origin + direction * radius * 5.0
            cam.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
            cam_data.lens = 50
            scene.render.filepath = f"{prefix}_{view}_{deg:03d}.png"
            bpy.ops.render.render(write_still=True)
    for w in wheels:
        w.matrix_world = rests[w]


def export_any(path):
    bpy.ops.object.select_all(action="SELECT")
    for o in bpy.data.objects:
        if o.type == "CAMERA":
            o.select_set(False)
    if path.lower().endswith(".fbx"):
        bpy.ops.export_scene.fbx(
            filepath=path, use_selection=True, apply_unit_scale=True,
            bake_space_transform=False, object_types={"MESH", "EMPTY"},
            path_mode="COPY", embed_textures=True, add_leaf_bones=False,
            use_custom_props=True)
    else:
        bpy.ops.export_scene.gltf(
            filepath=path, export_format="GLB", use_selection=True,
            export_extras=True, export_yup=True)


wheel_frame_cached = {}


def main():
    args = parse_args()
    reference = {}
    if args.reference:
        ref_body, ref_wheels = import_any(str(Path(args.reference).resolve()))
        for w in ref_wheels:
            reference[w.name] = measure_wheel(w, *wheel_frame(w, ref_body))
    body, wheels = import_any(str(Path(args.input).resolve()))
    for w in wheels:
        wheel_frame_cached[w] = wheel_frame(w, body)
    if not args.render_only:
        info = []
        for w in wheels:
            origin, axis, outward, radius = wheel_frame_cached[w]
            co = np.array([tuple(w.matrix_world @ v.co - origin) for v in w.data.vertices])
            along = co @ np.array(tuple(axis)) * outward
            # Only the tyre band: the hub/brake bits further inside stay put.
            w_lo, w_hi = float(along.min()), float(along.max())
            radius, a_in, a_out = reference.get(
                w.name, measure_wheel(w, origin, axis, outward, radius))
            removed, tyre, radius, a_in, a_out = complete_wheel(
                w, origin, axis, outward, radius, a_in, a_out)
            wheel_frame_cached[w] = (origin, axis, outward, radius)
            w["wheel_radius"] = radius
            w["wheel_axis"] = AXES[max(range(3), key=lambda i: abs(axis[i]))]
            info.append((origin, axis, outward, radius, w_lo - 0.01 * radius, w_hi + 0.01 * radius))
            print(f"WHEEL_COMPLETE {w.name} radius={radius:.4f} scanTyreFacesRemoved={removed} "
                  f"tyreFaces={tyre} width={a_out - a_in:.4f}")
        if not args.keep_body_tyre:
            stripped = strip_body(body, info)
            print(f"WHEEL_BODY_STRIPPED faces={stripped}")
    if args.render:
        body.hide_render = args.hide_body
        render(args.render, body, wheels, (0, 90, 180))
    if args.output and not args.render_only:
        out = str(Path(args.output).resolve())
        export_any(out)
        print("WHEEL_COMPLETE_EXPORTED " + out)


if __name__ == "__main__":
    main()
