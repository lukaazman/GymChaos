"""Split the wheels of a scanned single-mesh vehicle into separate objects.

The vehicle scans are one fused mesh. This script finds each tyre from the
geometry (ground contact patches, then the silhouette of the lower half of the
tyre), separates the faces inside each wheel cylinder into its own object with
its origin on the axle, and exports the result in the same format as the input.

Wheel objects are named ``Wheel_<F|R|M><L|R>`` and carry custom properties
``wheel_radius`` and ``wheel_axis`` (exported as glTF extras / FBX user props).
Runtime code (``GymVehicleWheelSpinner``) finds them by the ``Wheel_`` prefix.

Usage:
  blender -b --factory-startup -P Tools/split_vehicle_wheels.py -- \
      --input <in.fbx|in.glb> --output <out.fbx|out.glb> [--report out.json] \
      [--debug-render out_prefix]
"""
import argparse
import json
import math
import sys
from pathlib import Path

import bmesh
import bpy
import mathutils
import numpy as np


def parse_args():
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--report")
    parser.add_argument("--debug-render")
    parser.add_argument("--radius-scale", type=float, default=1.07)
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return parser.parse_args(argv)


def import_any(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if path.lower().endswith(".fbx"):
        bpy.ops.import_scene.fbx(filepath=path)
    else:
        bpy.ops.import_scene.gltf(filepath=path)
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    if len(meshes) != 1:
        raise RuntimeError(f"expected one mesh, found {len(meshes)}")
    return meshes[0]


def cluster_1d(values, gap):
    order = np.argsort(values)
    groups = [[order[0]]]
    for a, b in zip(order[:-1], order[1:]):
        if values[b] - values[a] > gap:
            groups.append([])
        groups[-1].append(b)
    return [np.array(g) for g in groups]


# Hand-measured axle seeds (normalised model units, from orthographic side
# renders) for scans whose low side skirts or mud flaps confuse the automatic
# silhouette fit. "fixed" keeps the seed instead of refining it.
SEEDS = {
    "Davie_Bus": {"fixed": True, "roundify": False, "axles": [
        {"l": -0.272, "r": 0.0445}, {"l": 0.231, "r": 0.0445}]},
    "jaycutler_vehicle": {"fixed": False, "axles": [
        {"l": -0.322, "r": 0.0700}, {"l": 0.245, "r": 0.0685}]},
}


def fit_wheel(V, L, W, zmin, l_c, side_sign, size_l, seed_r=None, fixed=False):
    """Fit centre/radius from the tyre silhouette close to the ground."""
    l = V[:, L]
    z = V[:, 2]
    w = V[:, W] * side_sign
    side = w > 0

    def extents(h, window):
        band = side & (np.abs(z - (zmin + h)) < 0.0015 * size_l) &             (np.abs(l - l_c) < window)
        if band.sum() < 6:
            return None
        return l[band].min(), l[band].max()

    if seed_r is None:
        # First estimate from the bottom few centimetres only: side skirts
        # and bumpers sit higher than this, so only the tyre is measured.
        estimates = []
        for h in np.linspace(0.008, 0.02, 7) * size_l:
            ext = extents(h, 0.15 * size_l)
            if ext is None:
                continue
            a = (ext[1] - ext[0]) * 0.5
            estimates.append((a * a + h * h) / (2.0 * h))
        r0 = float(np.clip(np.median(estimates) if estimates else 0.07 * size_l,
                           0.03 * size_l, 0.13 * size_l))
        spread = 0.15
    else:
        r0 = seed_r
        spread = 0.08
    r = r0
    for _ in range(0 if fixed else 8):
        estimates = []
        centres = []
        for frac in np.linspace(0.18, 0.50, 9):
            h = frac * r
            ext = extents(h, 1.04 * r)
            if ext is None:
                continue
            a = (ext[1] - ext[0]) * 0.5
            estimates.append((a * a + h * h) / (2.0 * h))
            centres.append((ext[0] + ext[1]) * 0.5)
        if not estimates:
            break
        r_new = float(np.clip(np.median(estimates), (1 - spread) * r0,
                              (1 + spread) * r0))
        l_c = float(np.median(centres))
        if abs(r_new - r) < 1e-5:
            r = r_new
            break
        r = r_new
    z_c = zmin + r
    # Lateral extent of the tyre from its ground contact patch; higher up the
    # scanned underbody fills the space between the wheels.
    patch = side & (z < zmin + 0.006 * size_l) & (np.abs(l - l_c) < 0.8 * r)
    if patch.sum() < 3:
        patch = side & (z < zmin + 0.02 * size_l) & (np.abs(l - l_c) < 0.8 * r)
    w_lo = float(np.percentile(w[patch], 1.0))
    w_hi = float(np.percentile(w[patch], 99.0))
    margin = 0.12 * (w_hi - w_lo) + 0.004 * size_l
    return l_c, z_c, r, w_lo - margin, w_hi + margin


def find_wheels(obj, stem):
    me = obj.data
    mw = obj.matrix_world
    V = np.array([tuple(mw @ v.co) for v in me.vertices])
    mn, mx = V.min(0), V.max(0)
    size = mx - mn
    L = 0 if size[0] >= size[1] else 1
    W = 1 - L
    centre = (mn + mx) * 0.5
    zmin = mn[2]
    Vc = V.copy()
    Vc[:, W] -= centre[W]
    seed = SEEDS.get(stem)
    contact = np.where(V[:, 2] < zmin + 0.006 * size[L])[0]
    wheels = []
    for side_sign in (1.0, -1.0):
        if seed:
            axles = [(a["l"], a["r"]) for a in seed["axles"]]
        else:
            side = contact[Vc[contact, W] * side_sign > 0.15 * size[W]]
            axles = [(float(np.median(V[side[g], L])), None)
                     for g in cluster_1d(V[side, L], 0.05 * size[L]) if len(g) >= 3]
        for l_seed, r_seed in axles:
            l_c, z_c, r, w_lo, w_hi = fit_wheel(
                Vc, L, W, zmin, l_seed, side_sign, size[L], r_seed,
                bool(seed and seed.get("fixed")))
            wheels.append({
                "l": l_c, "z": z_c, "r": r, "w_lo": w_lo, "w_hi": w_hi,
                "side": side_sign, "w_centre": float(centre[W]),
                "roundify": not (seed and seed.get("roundify") is False),
            })
    return V, L, W, size, centre, wheels


def name_wheels(wheels, L_mid):
    for wh in wheels:
        lr = "L" if wh["side"] > 0 else "R"
        wh["name"] = f"Wheel_{'P' if wh['l'] >= L_mid else 'N'}{lr}"
    # Disambiguate multiple axles on the same half.
    seen = {}
    for wh in sorted(wheels, key=lambda w: w["l"]):
        base = wh["name"]
        seen[base] = seen.get(base, 0) + 1
        if seen[base] > 1:
            wh["name"] = f"{base}{seen[base]}"


def roundify(mesh, L, W, radius, basis):
    """Make the tread silhouette circular around the axle.

    Several scans have octagonal or decagonal tyres. Spinning a polygon makes
    its corners sweep through the ground line, so the tread is pushed out to
    the fitted circle while the rim keeps its shape. Returns the relative
    silhouette radius spread before and after.
    """
    # Work in world-aligned axes around the pivot; the FBX import can put a
    # rotation on the parent, so local axes need not match L/W/Z.
    M = np.array(basis)
    co = np.array([tuple(v.co) for v in mesh.vertices]) @ M.T
    if len(co) == 0:
        return 0.0, 0.0
    a = np.arctan2(co[:, 2], co[:, L])
    rad = np.hypot(co[:, L], co[:, 2])
    bins = 120
    idx = ((a + math.pi) / (2 * math.pi) * bins).astype(int) % bins
    outer = np.zeros(bins)
    np.maximum.at(outer, idx, rad)
    filled = outer > 0.3 * radius
    if filled.sum() < bins * 0.5:
        return 0.0, 0.0
    # Fill empty bins from neighbours, then take the local max of 3 bins.
    for _ in range(bins):
        if filled.all():
            break
        for i in np.where(~filled)[0]:
            n = [outer[(i - 1) % bins], outer[(i + 1) % bins]]
            n = [x for x in n if x > 0.3 * radius]
            if n:
                outer[i] = max(n)
        filled = outer > 0.3 * radius
    peak = np.maximum(np.maximum(outer, np.roll(outer, 1)), np.roll(outer, -1))
    # The tread is complete only below the axle (the top is trimmed under
    # the fender), so the target radius comes from the lower bins.
    centres = (np.arange(bins) + 0.5) / bins * 2 * math.pi - math.pi
    lower_bins = (centres > -0.8 * math.pi) & (centres < -0.2 * math.pi)
    target = float(np.median(peak[lower_bins]))
    peak = np.clip(peak, target * 0.9, target * 1.1)
    before = float((peak[lower_bins].max() - peak[lower_bins].min()) / target)
    t = rad / np.maximum(peak[idx], 1e-6)
    blend = np.clip((t - 0.55) / 0.30, 0.0, 1.0)
    blend = blend * blend * (3 - 2 * blend)
    # Leave stray pieces (mud flaps, brake parts) outside the tread alone.
    blend[rad > target * 1.12] = 0.0
    scale = 1.0 + blend * (target / np.maximum(peak[idx], 1e-6) - 1.0)
    co[:, L] *= scale
    co[:, 2] *= scale
    local = co @ np.linalg.inv(M).T
    for v, c in zip(mesh.vertices, local):
        v.co = c
    mesh.update()
    rad2 = np.hypot(co[:, L], co[:, 2])
    outer2 = np.zeros(bins)
    np.maximum.at(outer2, idx, rad2)
    o2 = outer2[lower_bins & (outer2 > 0.3 * radius)]
    after = float((o2.max() - o2.min()) / target) if len(o2) else 0.0
    return before, after


def split(obj, V, L, W, wheels, radius_scale):
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    owner = np.full(len(bm.faces), -1, dtype=np.int32)
    for f in bm.faces:
        c = np.mean([V[v.index] for v in f.verts], axis=0)
        for wi, wh in enumerate(wheels):
            w_local = (c[W] - wh["w_centre"]) * wh["side"]
            if w_local < wh["w_lo"] - 0.004 or w_local > wh["w_hi"] + 0.004:
                continue
            # Above the axle the fender lip hugs the tyre; keep its faces on
            # the body by cutting slightly inside the tread there.
            limit = wh["r"] * (radius_scale if c[2] <= wh["z"] + 0.3 * wh["r"]
                               else min(radius_scale, 0.96))
            if math.hypot(c[L] - wh["l"], c[2] - wh["z"]) <= limit:
                owner[f.index] = wi
                break

    def keep_only(source, keep_mask):
        copy = source.copy()
        copy.faces.ensure_lookup_table()
        doomed = [f for f in copy.faces if not keep_mask[f.index]]
        bmesh.ops.delete(copy, geom=doomed, context="FACES_ONLY")
        loose = [v for v in copy.verts if not v.link_faces]
        bmesh.ops.delete(copy, geom=loose, context="VERTS")
        return copy

    created = []
    for wi, wh in enumerate(wheels):
        mask = owner == wi
        wh["faces"] = int(mask.sum())
        if not mask.any():
            continue
        part = keep_only(bm, mask)
        mesh = bpy.data.meshes.new(wh["name"])
        part.to_mesh(mesh)
        part.free()
        for mat in me.materials:
            mesh.materials.append(mat)
        new = bpy.data.objects.new(wh["name"], mesh)
        obj.users_collection[0].objects.link(new)
        new.parent = obj.parent
        new.matrix_world = obj.matrix_world.copy()
        pivot = [0.0, 0.0, 0.0]
        pivot[L] = wh["l"]
        pivot[W] = wh["w_centre"] + wh["side"] * (wh["w_lo"] + wh["w_hi"]) * 0.5
        pivot[2] = wh["z"]
        # Put the origin on the axle so the runtime can spin the node in place.
        local_pivot = new.matrix_world.inverted() @ mathutils.Vector(pivot)
        mesh.transform(mathutils.Matrix.Translation(-local_pivot))
        new.matrix_world = new.matrix_world @ mathutils.Matrix.Translation(local_pivot)
        wh["roundness_before"], wh["roundness_after"] = roundify(
            mesh, L, W, wh["r"], new.matrix_world.to_3x3())             if wh["roundify"] else (0.0, 0.0)
        new["wheel_radius"] = wh["r"]
        new["wheel_axis"] = "XYZ"[W]
        wh["pivot"] = pivot
        created.append(new)

    body = keep_only(bm, owner < 0)
    body.to_mesh(me)
    body.free()
    bm.free()
    me.update()
    return created


def debug_render(obj, wheels_objs, prefix, L, W, size, centre):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "OBJECT"
    scene.world = bpy.data.worlds.new("w")
    obj.color = (0.75, 0.75, 0.75, 1)
    for i, o in enumerate(wheels_objs):
        o.color = (0.95, 0.15 + 0.15 * i, 0.1, 1)
    cam = bpy.data.cameras.new("dbg")
    cam.type = "ORTHO"
    cam_obj = bpy.data.objects.new("dbg", cam)
    scene.collection.objects.link(cam_obj)
    scene.camera = cam_obj
    scene.render.resolution_x = 1600
    scene.render.resolution_y = 900
    ctr = mathutils.Vector(centre)
    views = {
        "side": mathutils.Vector([0, 0, 0]),
        "quarter": mathutils.Vector([0, 0, 0]),
        "under": mathutils.Vector([0, 0, 0]),
    }
    views["side"][W] = 1
    views["quarter"][W] = 1
    views["quarter"][L] = 1
    views["quarter"][2] = 0.35
    views["under"][2] = -1
    views["under"][W] = 0.15
    for name, direction in views.items():
        direction = direction.normalized()
        cam.ortho_scale = size[L] * 1.1
        cam_obj.location = ctr + direction * (size[L] * 4)
        cam_obj.rotation_euler = (-direction).to_track_quat("-Z", "Z" if name == "under" else "Y").to_euler()
        if name == "under":
            cam_obj.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = f"{prefix}_{name}.png"
        bpy.ops.render.render(write_still=True)


def export_any(path):
    bpy.ops.object.select_all(action="SELECT")
    if path.lower().endswith(".fbx"):
        # Same options as Tools/export_vehicle_fbx.py, plus custom properties.
        bpy.ops.export_scene.fbx(
            filepath=path,
            use_selection=True,
            apply_unit_scale=True,
            bake_space_transform=False,
            object_types={"MESH", "EMPTY"},
            path_mode="COPY",
            embed_textures=True,
            add_leaf_bones=False,
            use_custom_props=True,
        )
    else:
        bpy.ops.export_scene.gltf(
            filepath=path,
            export_format="GLB",
            use_selection=True,
            export_extras=True,
            export_yup=True,
        )


def main():
    args = parse_args()
    obj = import_any(str(Path(args.input).resolve()))
    V, L, W, size, centre, wheels = find_wheels(obj, Path(args.input).stem)
    name_wheels(wheels, centre[L])
    created = split(obj, V, L, W, wheels, args.radius_scale)
    report = {
        "input": args.input,
        "length_axis": "XYZ"[L],
        "width_axis": "XYZ"[W],
        "size": size.tolist(),
        "wheels": [{k: v for k, v in wh.items()} for wh in wheels],
    }
    if args.debug_render:
        debug_render(obj, created, args.debug_render, L, W, size, centre)
    out = str(Path(args.output).resolve())
    Path(out).parent.mkdir(parents=True, exist_ok=True)
    export_any(out)
    text = json.dumps(report, indent=1, default=float)
    print("WHEEL_SPLIT_REPORT " + json.dumps(report, default=float))
    if args.report:
        Path(args.report).write_text(text)


if __name__ == "__main__":
    main()
