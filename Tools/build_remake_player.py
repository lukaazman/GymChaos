"""Build the remade, customizable player.

    blender -b -noaudio --python-exit-code 1 -P Tools/build_remake_player.py

Writes Assets/BodyBuilders/remake/player.blend and textures/player.png, then
Tools/export_authored_character_fbx.py --remake player exports it.

One lean male MPFB base in the remake style. Every mesh carries four shape
keys mapped from full-resolution MPFB variants (closest point on the base
surface, same triangle and barycentrics on the variant):

- Female: the female body at the base height (the game scales height).
- Upper / Lower: the full-muscle variant, split at the hips by leg weights.
- Soft: the heavy variant (soft tissue over the muscle).

Weights 0..1; the game maps class bulk from catalog.json onto them and
resolves the keys into a plain mesh once (cloth shells copy body faces).

Meshes: Body (skin, painted near white and tinted per skin tone in game; eyes,
shoes and socks on a second "Gear" material), Tank, Shorts, Hair_1..5 and
Beard_1..3 as separate skinned parts, all on the 72 DEF bone rig.
"""

from __future__ import annotations

import sys
from pathlib import Path

import addon_utils
import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform

sys.path.insert(0, str(Path(__file__).resolve().parent))
import remake_character as rc  # noqa: E402
import remake_rig  # noqa: E402

NAME = "player"
BASE = {"gender": 1.0, "age": 0.4, "muscle": 0.68, "weight": 0.3, "proportions": 0.7, "height": 0.6}
# Even the lean base is athletic: flat pecs with a clear lower edge, no soft chest.
BASE_TARGETS = {"torso-muscle-pectoral-incr": 1.0, "measure-bust-circ-decr": 0.5, "torso-vshape-incr": 0.35,
                "stomach-pregnant-decr": 0.6, "lr-upperarm-muscle-incr": 0.3}
# The female variant keeps her own chest: only the athletic tone carries over.
FEMALE_TARGETS = {"torso-vshape-incr": 0.2, "stomach-pregnant-decr": 0.6, "lr-upperarm-muscle-incr": 0.3}
MUSCLE_TARGETS = {"torso-vshape-incr": 0.9, "torso-muscle-dorsi-incr": 1.0, "torso-muscle-pectoral-incr": 1.0,
                  "measure-shoulder-dist-incr": 0.5, "measure-waist-circ-decr": 0.6,
                  "lr-upperarm-muscle-incr": 1.2, "lr-upperarm-shoulder-muscle-incr": 1.2,
                  "lr-lowerarm-muscle-incr": 0.9, "lr-upperleg-muscle-incr": 1.1,
                  "lr-lowerleg-muscle-incr": 1.1, "measure-calf-circ-incr": 0.4}
VARIANTS = {
    "Female": ({"gender": 0.0}, {}),
    "Bulk": ({"muscle": 1.0, "weight": 0.5}, MUSCLE_TARGETS),
    "Soft": ({"weight": 1.0}, {"stomach-pregnant-incr": 0.35}),
}
LEG_BONES = ("thigh", "shin", "foot", "toe")
SKIN = (0.94, 0.94, 0.94)  # tinted per skin tone in game
FACE = {"brows": (0.16, 0.13, 0.12), "lips": (0.82, 0.64, 0.62), "iris": (0.24, 0.17, 0.12)}
# The player's own tank top: narrow straps that run straight down from the
# traps beside the neck (not out on the shoulders), a U neckline in front,
# a higher one behind, deep armholes, and a hem that hangs over the shorts'
# waistband so no skin or band shows between top and bottom.
TANK = {"waist": ("navel", -0.045), "neck_x": 0.088, "strap_x": 0.125, "settle": 40, "clearance": 0.006, "scoop": 0.13, "back_scoop": 0.055,
        "armpit": 0.09, "side_in": 0.01, "armhole_drop": 0.07, "push": 0.018, "hem_flare": 0.016, "drape": 10, "ramp": 0.004}
SHORTS = {"source": "skin_band", "low": ("hem", 0.6), "high": ("navel", -0.03), "drape": 10, "push": 0.013,
          "flare": 0.006, "overlap": 0.0, "settle": 6, "clearance": 0.012}
# The OG locker tops and pants, authored for the remade body (runtime cloth
# shells copied from the coarse low-poly mesh came out holed and ballooned).
# They take the body's UVs, so the OG designs from build_player_tops.py
# --remake (Onyx honeycomb, Oversized KING print) map straight onto them.
ONYX = {"source": "region", "region": "tee", "waist": ("navel", -0.085), "sleeve": 0.0, "sleeve_x": 0.15,
        "scoop": 0.035,
        "back_scoop": 0.02, "scoop_x": 0.058, "push": 0.005, "drape": 4, "ramp": 0.004,
        "settle": 16, "clearance": 0.011, "hem_flare": 0.012, "hem_zone": 0.09}
# Loose, but cut for this body: hangs a hand off the chest, sleeves to above the elbow, hem at the hip.
OVERSIZED = {"source": "region", "region": "tee", "waist": ("navel", -0.15), "full_hips": True, "sleeve": 0.0,
             "sleeve_x": 0.23, "scoop": 0.03, "back_scoop": 0.015, "scoop_x": 0.062, "push": 0.03, "drape": 20, "ramp": 0.004,
             "settle": 60, "clearance": 0.034, "hem_flare": 0.008, "loose": True}
PANTS = {"source": "skin_band", "low": ("ankle", 0.03), "high": ("navel", -0.03), "drape": 10, "push": 0.012,
         "flare": 0.008, "overlap": 0.0, "settle": 10, "clearance": 0.013, "hem_flare": 0.0}
CLOTH = ("Tank", "Shorts", "Onyx", "Oversized", "Pants")
LOOSE = ("Oversized",)
SHOES = {"source": "skin_band", "color": (0.12, 0.12, 0.13), "low": ("floor", -1.0), "high": ("ankle", 0.035),
         "push": 0.014, "hull": True, "flat_bottom": True, "sole": (0.022, (0.88, 0.88, 0.86)), "tris": 450}
SOCKS = {"source": "skin_band", "color": (0.94, 0.94, 0.92), "low": ("ankle", 0.035), "high": ("ankle", 0.09),
         "push": 0.004, "tris": 160}
SCALP = {"source": "region", "region": "scalp"}
# Order is the save index (0 = none). Names live in the game (PlayerAppearance).
HAIR = [
    dict(SCALP, front=0.06, temple=0.035, sides=0.0, behind=0.1, back=-0.06, push=0.004, ramp=0.008),  # buzz
    dict(SCALP, front=0.05, temple=0.03, sides=0.0, behind=0.1, back=-0.07, push=0.008, volume=0.012,
         ramp=0.012),  # crop
    dict(SCALP, front=0.04, temple=0.015, sides=-0.03, behind=0.09, back=-0.10, push=0.024, volume=0.045,
         wave=0.022, spikes=0.01, ramp=0.02),  # curls
    dict(SCALP, front=0.045, temple=0.05, sides=0.04, behind=0.1, back=-0.04, push=0.004, volume=0.022,
         spikes=0.012, sweep=(0.02, 0.012), quiff=True, ramp=0.025),  # quiff
    dict(SCALP, front=0.045, temple=0.02, sides=-0.05, behind=0.1, back=-0.16, push=0.012, volume=0.03,
         wave=0.01, ramp=0.02),  # long
]
BEARD = {"source": "region", "region": "beard"}
FACIAL = [
    dict(BEARD, under_nose=-0.058, cheek_line=((0.026, -0.078), (0.07, -0.025)), down=0.19, depth=0.1,
         push=0.003, ramp=0.004, edge_smooth=8),  # stubble
    dict(BEARD, under_nose=-0.044, cheek_line=((0.03, -0.05), (0.052, -0.078)), down=0.08, depth=0.06,
         push=0.013, ramp=0.004, edge_smooth=2),  # moustache
    dict(BEARD, under_nose=-0.058, cheek_line=((0.026, -0.078), (0.07, -0.025)), down=0.19, depth=0.1,
         push=0.007, ramp=0.008, edge_smooth=8),  # full beard
]


def human(overrides, targets):
    """Full MPFB surface in the rig's T-pose rest, standing on Z = 0."""
    HS, TS, RS = rc.mpfb()
    macros = TS.get_default_macro_info_dict()
    macros.update(BASE)
    macros.update(overrides)
    obj = HS.create_human(macro_detail_dict=macros)
    targets = {**(FEMALE_TARGETS if overrides.get("gender") == 0.0 else BASE_TARGETS), **targets}
    for target, weight in targets.items():
        for full in ([f"l-{target[3:]}", f"r-{target[3:]}"] if target.startswith("lr-") else [target]):
            before = {k.name for k in obj.data.shape_keys.key_blocks}
            TS.load_target(obj, TS.target_full_path(full), weight=min(weight, 1.0))
            for key in obj.data.shape_keys.key_blocks:
                if key.name not in before and weight > 1.0:
                    key.slider_max = weight
                    key.value = weight
    TS.bake_targets(obj)
    lowest = min(v.co.z for v in obj.data.vertices)
    obj.data.transform(Matrix.Translation((0.0, 0.0, -lowest)))
    meta = HS.add_builtin_rig(obj, "rigify.human")
    rc.fill_unweighted(obj, {b.name for b in meta.data.bones})
    rc.pose_tpose(meta)
    RS.apply_pose_as_rest_pose(meta)
    return obj, meta


def surface(obj, meta, name):
    """The body surface alone, unbound, as a plain mesh in world space."""
    body = rc.extract(obj, "body", name)
    world = body.matrix_world.copy()
    body.modifiers.clear()
    body.parent = None
    body.matrix_world = world
    for v in body.vertex_groups[:]:
        body.vertex_groups.remove(v)
    bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.objects.remove(meta, do_unlink=True)
    return body


def copy_of(obj, name):
    dup = obj.copy()
    dup.data = obj.data.copy()
    dup.name = dup.data.name = name
    bpy.context.scene.collection.objects.link(dup)
    return dup


def tank_part(body, spec, marks, rig, name, skin):
    """The tank top region on a copy of the body (see TANK)."""
    import bmesh
    import math
    waist = rc.height_of(spec["waist"], marks)
    neck = rig.data.bones["DEF-spine.004"].head_local.z
    armpit = rig.data.bones["DEF-upper_arm.L"].head_local.z - spec["armpit"]
    # Side seam: inside the arm root, so the armhole never leaves a flap out past the ribs.
    side = abs(rig.data.bones["DEF-upper_arm.L"].head_local.x) - spec["side_in"]
    inner, outer = spec["neck_x"], spec["strap_x"]
    front_top, back_top = neck - spec["scoop"], neck - spec["back_scoop"]
    # Clean edges: bisect along the strap lines (above the armholes only) and the openings.
    bm = bmesh.new()
    bm.from_mesh(body.data)
    for co, no in (((0, 0, armpit), (0, 0, 1)), ((0, 0, front_top), (0, 0, 1)), ((0, 0, back_top), (0, 0, 1)),
                   ((0, 0, waist), (0, 0, 1)), ((inner, 0, 0), (1, 0, 0)), ((-inner, 0, 0), (1, 0, 0)),
                   ((outer, 0, 0), (1, 0, 0)), ((-outer, 0, 0), (1, 0, 0))):
        vertical = no[0] != 0
        faces = [f for f in bm.faces if abs(f.calc_center_median().x) < 0.3 and f.calc_center_median().z > waist - 0.03
                 and (not vertical or f.calc_center_median().z > min(front_top, armpit) - 0.03)]
        geom = list({e for f in faces for e in f.edges}) + faces + list({v for f in faces for v in f.verts})
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no)
    bm.to_mesh(body.data)
    bm.free()

    def inside(v, w):
        co = v.co
        if co.z < waist or w.get("spine.005", 0.0) >= 0.3:
            return False
        ax = abs(co.x)
        arm = sum(w.get(b, 0.0) for b in ("upper_arm.L", "upper_arm.R", "forearm.L", "forearm.R"))
        if ax < inner:  # centre panel up to the neckline
            return co.z <= (front_top if co.y < 0.0 else back_top)
        if co.z > armpit:  # only the straps above the armholes, ending on the traps (never up the neck)
            return ax <= outer and arm < 0.5 and w.get("spine.004", 0.0) < 0.35
        torso = sum(w.get(b, 0.0) for b in rc.TORSO) + w.get("shoulder.L", 0.0) + w.get("shoulder.R", 0.0)
        # The side seam limit is for the ribs under the armhole; the hips are wider than it.
        # Armhole: the strap's outer edge keeps going straight down past the armpit line, then
        # curves out to the side seam (no step between strap and chest panel).
        t = max(0.0, min(1.0, (armpit - co.z) / spec["armhole_drop"]))
        edge = outer + (side - outer) * math.sqrt(t)
        if ax <= outer:  # straight under the strap: always cloth, whatever MPFB's arm weights say
            return True
        return torso >= 0.4 and arm < 0.45 and (ax <= edge or co.z < marks["navel"] + 0.08)

    def offset(v):
        # The hem hangs a little off the body, over the shorts' waistband.
        hem = max(0.0, min(1.0, (waist + 0.07 - v.co.z) / 0.07))
        strap = 0.3 if v.co.z > armpit and abs(v.co.x) >= inner else 1.0  # straps lie flat on the shoulders
        return spec["push"] * strap + spec["hem_flare"] * hem

    return settle(rc.cut_region(body, inside, offset, spec["ramp"], name, 4, spec["drape"]), skin, spec,
                  waist)


def settle(part, skin, spec, waist):
    """Knit lies over the body instead of tracing it: rc.drape never lets cloth sink, so nipples
    stayed as bumps. Here the cloth relaxes freely but never comes closer to the skin than the
    clearance (more toward the hem, so a top hangs over the bottoms' waistband)."""
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(part.data)
    bm.normal_update()
    free = [v for v in bm.verts if not any(e.is_boundary for e in v.link_edges)]
    skin.data.calc_loop_triangles()
    tree = BVHTree.FromPolygons([v.co for v in skin.data.vertices], [t.vertices for t in skin.data.loop_triangles])
    for _ in range(spec["settle"]):
        bmesh.ops.smooth_vert(bm, verts=free, factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
        for v in bm.verts:  # never closer to the skin than the clearance, open edges included
            location, normal, _, _ = tree.find_nearest(v.co)
            gap = (v.co - location).dot(normal)
            if "hem_zone" in spec:  # full flare over the bottoms' waistband, then a short ramp
                flare = max(0.0, min(1.0, (waist + spec["hem_zone"] - v.co.z) / 0.03))
            else:
                flare = max(0.0, min(1.0, (waist + 0.07 - v.co.z) / 0.07))
            want = spec["clearance"] + spec.get("hem_flare", 0.0) * flare
            if gap < want:
                v.co += normal * (want - gap)
    bm.to_mesh(part.data)
    bm.free()
    return part


def hang(part, chest_z, shoulder_x, sectors=48):
    """A loose tee falls straight down from the chest: below it, every direction around the
    torso keeps at least the widest reach above it (no belly or waist tracing)."""
    import bmesh
    import math
    bm = bmesh.new()
    bm.from_mesh(part.data)
    torso = [v for v in bm.verts if abs(v.co.x) < shoulder_x]
    cy = sum(v.co.y for v in torso) / max(1, len(torso))
    reach = [0.0] * sectors
    for v in sorted(torso, key=lambda v: -v.co.z):
        dx, dy = v.co.x, v.co.y - cy
        r = math.hypot(dx, dy)
        k = int((math.atan2(dy, dx) + math.pi) / (2 * math.pi) * sectors) % sectors
        if v.co.z < chest_z and r < reach[k] and r > 1e-6:
            scale = reach[k] / r
            v.co.x, v.co.y = dx * scale, cy + dy * scale
            r = reach[k]
        reach[k] = max(reach[k], r)
    bmesh.ops.smooth_vert(bm, verts=[v for v in torso if v.co.z < chest_z], factor=0.5,
                          use_axis_x=True, use_axis_y=True, use_axis_z=False)
    bm.to_mesh(part.data)
    bm.free()


# Oversized print layout (kept in step with build_player_tops.py --remake): front half
# u 0..0.5, back half 0.5..1 (mirrored so it reads from behind), x in body heights
# -0.5..0.5, v from height 0.35 (bottom) to 0.85 (top) of the body height.
PLANAR_Z0, PLANAR_Z1 = 0.35, 0.85


def planar_uv(piece, height):
    """Front/back projection by face normal: room for a chest crest and a back print."""
    for old in list(piece.data.uv_layers):
        piece.data.uv_layers.remove(old)
    layer = piece.data.uv_layers.new(name="UVMap")
    piece.data.uv_layers.active = layer
    for polygon in piece.data.polygons:
        back = polygon.normal.y > 0.0  # the body faces -Y
        for loop_index in polygon.loop_indices:
            co = piece.data.vertices[piece.data.loops[loop_index].vertex_index].co
            x, z = co.x / height, co.z / height
            u = 0.75 - x * 0.5 if back else 0.25 + x * 0.5
            layer.data[loop_index].uv = (u, (z - PLANAR_Z0) / (PLANAR_Z1 - PLANAR_Z0))


def cut_from_copy(body, spec, eye, marks, rig, name):
    """A swappable part cut from a copy, so the skin stays whole under it."""
    scratch = copy_of(body, name + "_scratch")
    if name == "Tank":
        part = tank_part(scratch, spec, marks, rig, name, body)
    elif spec["source"] == "region":
        import bmesh
        bm = bmesh.new()
        bm.from_mesh(scratch.data)
        planes = []
        if "waist" in spec:  # a straight hem
            planes.append(((0, 0, rc.height_of(spec["waist"], marks)), (0, 0, 1)))
        if spec.get("sleeve_x"):  # straight sleeve ends, square to the level T-posed arm
            end = abs(rig.data.bones["DEF-upper_arm.L"].head_local.x) + spec["sleeve_x"]
            planes += [((end, 0, 0), (1, 0, 0)), ((-end, 0, 0), (1, 0, 0))]
        for co, no in planes:
            geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
            bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no)
        bm.to_mesh(scratch.data)
        bm.free()
        part = rc.region_part(scratch, spec, eye, marks, rig, name)
    else:
        part = rc.cut_skin_band(scratch, rc.height_of(spec["low"], marks), rc.height_of(spec["high"], marks),
                                spec, name)
    if "settle" in spec and name != "Tank":
        top = rc.height_of(spec["waist"], marks) if "waist" in spec else -10.0
        part = settle(part, body, spec, top)
    if spec.get("loose"):
        hang(part, rig.data.bones["DEF-spine.003"].head_local.z,
             abs(rig.data.bones["DEF-upper_arm.L"].head_local.x))
        part = settle(part, body, dict(spec, settle=4), rc.height_of(spec["waist"], marks))
    bpy.data.objects.remove(scratch, do_unlink=True)
    return part


def flat_material(name):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    return material


def smooth_deltas(obj, moved, iterations):
    """Laplacian-smooth the per-vertex offsets (target - rest) over the mesh edges."""
    rest = [v.co.copy() for v in obj.data.vertices]
    delta = [m - r for m, r in zip(moved, rest)]
    links = [[] for _ in rest]
    for e in obj.data.edges:
        a, b = e.vertices
        links[a].append(b)
        links[b].append(a)
    for _ in range(iterations):
        delta = [(d + sum((delta[j] for j in n), Vector()) / len(n)) * 0.5 if n else d
                 for d, n in zip(delta, links)]
    return [r + d for r, d in zip(rest, delta)]


def add_keys(obj, base_hi, variants, triangles, tree, chest_z):
    """Basis plus one key per variant, for every vertex of obj."""
    if obj.data.shape_keys is None:
        obj.shape_key_add(name="Basis")
    verts = base_hi.data.vertices
    legs = [sum(g.weight for g in v.groups
                if any(obj.vertex_groups[g.group].name.startswith(f"DEF-{b}") for b in LEG_BONES))
            for v in obj.data.vertices]
    moved = {}
    for name, variant in variants.items():
        target = variant.data.vertices
        out = []
        for v in obj.data.vertices:
            _, _, index, _ = tree.find_nearest(v.co)
            a, b, c = triangles[index]
            out.append(barycentric_transform(v.co, verts[a].co, verts[b].co, verts[c].co,
                                             target[a].co, target[b].co, target[c].co))
        if obj.name in CLOTH:  # cloth drapes: no nipples or muscle grooves through it
            smooth = smooth_deltas(obj, out, 14 if obj.name in LOOSE else 5)
            # The cloth never lags behind the skin (bulkier thighs, shoulders, hips and the
            # female chest showed through); it only skips the skin's inward detail.
            # Loose cloth hangs well off the skin: it takes only the smoothed shape (no lumps).
            floor = smooth if obj.name in LOOSE else out
            for i, v in enumerate(obj.data.vertices):
                lag = (floor[i] - smooth[i]).dot(v.normal)
                if lag > 0.0:
                    smooth[i] = smooth[i] + v.normal * lag
            out = smooth
        moved[name] = out
    for key_name, source, mask in (("Female", "Female", None), ("Upper", "Bulk", False),
                                   ("Lower", "Bulk", True), ("Soft", "Soft", None)):
        key = obj.shape_key_add(name=key_name, from_mix=False)
        for i, v in enumerate(obj.data.vertices):
            w = 1.0 if mask is None else (min(1.0, legs[i]) if mask else 1.0 - min(1.0, legs[i]))
            key.data[i].co = v.co + (moved[source][i] - v.co) * w


def main():
    rc.TEXTURE_SIZE = 1024
    bpy.ops.wm.read_homefile(use_empty=True)
    addon_utils.enable("rigify", default_set=True)

    # Variants first; the base build below reuses the same rest pose steps.
    variants = {}
    heights = {}
    for name, (overrides, targets) in VARIANTS.items():
        obj, meta = human(overrides, targets)
        variants[name] = surface(obj, meta, "variant_" + name)
        heights[name] = max(v.co.z for v in variants[name].data.vertices)

    human_obj, meta = human({}, {})
    eye = rc.group_center(human_obj, ["joint-l-eye", "joint-r-eye"])
    eye_l = rc.group_center(human_obj, ["joint-l-eye"])
    eye_r = rc.group_center(human_obj, ["joint-r-eye"])
    parts = {"body": rc.extract(human_obj, "body", f"{NAME}_body"),
             "eyes": rc.eyeballs(human_obj, (eye_l, eye_r), f"{NAME}_eyes")}
    bpy.data.objects.remove(human_obj, do_unlink=True)
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

    detail = copy_of(parts["body"], "detail")
    base_hi = copy_of(parts["body"], "base_hi")
    for v in base_hi.vertex_groups[:]:
        base_hi.vertex_groups.remove(v)
    # Same height for every variant: the skeleton is the base's; the game scales height.
    top = max(v.co.z for v in base_hi.data.vertices)
    for name, variant in variants.items():
        variant.data.transform(Matrix.Scale(top / heights[name], 4))
    rc.unsubdivide(parts["body"], 2)

    marks = {"floor": 0.0, "ankle": rig.data.bones["DEF-foot.L"].head_local.z,
             "navel": rig.data.bones["DEF-spine.001"].head_local.z,
             "knee": rig.data.bones["DEF-shin.L"].head_local.z,
             "hip": rig.data.bones["DEF-thigh.L"].head_local.z}
    pieces = {"Tank": cut_from_copy(parts["body"], TANK, eye, marks, rig, "Tank"),
              "Shorts": cut_from_copy(parts["body"], SHORTS, eye, marks, rig, "Shorts"),
              "Onyx": cut_from_copy(parts["body"], ONYX, eye, marks, rig, "Onyx"),
              "Oversized": cut_from_copy(parts["body"], OVERSIZED, eye, marks, rig, "Oversized"),
              "Pants": cut_from_copy(parts["body"], PANTS, eye, marks, rig, "Pants")}
    for i, spec in enumerate(HAIR, 1):
        pieces[f"Hair_{i}"] = cut_from_copy(parts["body"], spec, eye, marks, rig, f"Hair_{i}")
    for i, spec in enumerate(FACIAL, 1):
        pieces[f"Beard_{i}"] = cut_from_copy(parts["body"], spec, eye, marks, rig, f"Beard_{i}")
    for key, spec in (("shoes", SHOES), ("socks", SOCKS)):
        parts[key] = rc.cut_skin_band(parts["body"], rc.height_of(spec["low"], marks),
                                      rc.height_of(spec["high"], marks), spec, f"{NAME}_{key}")

    recipe = {"skin": SKIN, "face": FACE, "parts": {"shoes": SHOES, "socks": SOCKS}}
    region_sets = {"body": rc.face_regions(eye, FACE)}
    rc.paint(parts["body"], SKIN)
    for key, spec in (("shoes", SHOES), ("socks", SOCKS)):
        regions = []
        if "sole" in spec:
            sole_height, sole_color = spec["sole"]
            floor = min(v.co.z for v in parts[key].data.vertices)
            regions.append((lambda co, n, h=floor + sole_height: co.z <= h or n.z < -0.6, sole_color))
        rc.paint(parts[key], spec["color"])
        region_sets[key] = regions
    rc.paint(parts["eyes"], (0.90, 0.88, 0.84), [
        (lambda co, n, c=c, k=k: (co - c).normalized().y < k, col)
        for c in (eye_l, eye_r)
        for k, col in ((-0.97, (0.03, 0.03, 0.03)), (-0.86, FACE["iris"]))])

    floor = min((o.matrix_world @ v.co).z for o in parts.values() for v in o.data.vertices)
    keep = (*pieces.values(), *variants.values(), base_hi)
    rc.finish(NAME, recipe, parts, region_sets, detail, rig, eye, keep=keep)

    body = bpy.data.objects["Body"]
    bpy.data.objects.remove(bpy.data.objects["Body_LOD1"], do_unlink=True)
    shift = Matrix.Translation((0.0, 0.0, -floor))
    for obj in (*variants.values(), base_hi):
        obj.data.transform(shift)
    # Eyes, shoes and socks keep their colour: a second slot the game leaves untinted.
    part_ids = body.data.attributes[rc.PART].data
    gear = body.data.materials[0].copy()
    gear.name = "Gear"
    body.data.materials[0].name = "Skin"
    body.data.materials.append(gear)
    for polygon in body.data.polygons:
        polygon.material_index = 0 if part_ids[polygon.index].value == 0 else 1
    for name, piece in pieces.items():
        piece.modifiers.clear()
        piece.parent = None
        piece.data.transform(shift)
        rc.to_def_groups(piece, rig)
        rc.clean_weights(piece)
        piece.parent = rig
        piece.modifiers.new("Armature", "ARMATURE").object = rig
        piece.data.materials.clear()
        piece.data.materials.append(flat_material("Hair" if name.startswith(("Hair", "Beard")) else name))
        rc.select_only(piece)
        bpy.ops.object.shade_smooth_by_angle(angle=1.0)

    # Cloth takes the body's UVs from the closest skin point, so the OG atlases fit it.
    body.data.calc_loop_triangles()
    skin_uv = body.data.uv_layers.active.data
    skin_tris = list(body.data.loop_triangles)
    skin_tree = BVHTree.FromPolygons([v.co for v in body.data.vertices], [t.vertices for t in skin_tris])
    top = max(v.co.z for v in body.data.vertices)
    planar_uv(pieces["Oversized"], top)
    planar_uv(pieces["Onyx"], top)
    for name in ("Pants",):
        piece = pieces[name]
        for old in list(piece.data.uv_layers):  # the copy carries MPFB's own UVs
            piece.data.uv_layers.remove(old)
        layer = piece.data.uv_layers.new(name="UVMap")
        piece.data.uv_layers.active = layer
        # One skin triangle per cloth face: per-corner lookups jumped UV islands at the seams.
        for polygon in piece.data.polygons:
            _, _, index, _ = skin_tree.find_nearest(polygon.center)
            tri = skin_tris[index]
            a, b, c = (body.data.vertices[i].co for i in tri.vertices)
            ua, ub, uc = (Vector((*skin_uv[l].uv, 0.0)) for l in tri.loops)
            for loop_index in polygon.loop_indices:
                co = piece.data.vertices[piece.data.loops[loop_index].vertex_index].co
                uv = barycentric_transform(co, a, b, c, ua, ub, uc)
                layer.data[loop_index].uv = (uv.x, uv.y)

    base_hi.data.calc_loop_triangles()
    triangles = [tuple(t.vertices) for t in base_hi.data.loop_triangles]
    tree = BVHTree.FromPolygons([v.co for v in base_hi.data.vertices], triangles)
    for obj in (body, *pieces.values()):
        add_keys(obj, base_hi, variants, triangles, tree, rig.data.bones["DEF-spine.002"].head_local.z)
    for obj in (*variants.values(), base_hi):
        bpy.data.objects.remove(obj, do_unlink=True)

    output = rc.OUT_DIR / f"{NAME}.blend"
    bpy.ops.wm.save_as_mainfile(filepath=str(output), compress=True)
    tris = {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons)
            for o in bpy.data.objects if o.type == "MESH"}
    keys = {o.name: len(o.data.shape_keys.key_blocks) - 1 for o in bpy.data.objects if o.type == "MESH"}
    assert all(k == 4 for k in keys.values()), keys
    print(f"REMAKE_PLAYER_OK meshes={len(tris)} body_tris={tris['Body']} "
          f"pieces={','.join(sorted(n for n in tris if n != 'Body'))} path={output}")


main()
