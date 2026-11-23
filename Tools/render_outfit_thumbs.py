"""Transparent PNG thumbnails for the in-game outfit screen.

    blender -b --factory-startup -P Tools/render_outfit_thumbs.py -- [garments] [heads] [members] [vehicles] [name_filter=<substring>]

No argument renders everything into GymChaos/Assets/Resources/UI/Outfit/ (Cycles CPU, no window):
  top_*/bottom_*  256x256 white garments (shells rebuilt from the mask like Tools/render_player_tops.py)
  head_*          256x256 hat GLBs
  member_<key>_<og|slop>  256x320 head-to-waist portraits (rest pose, game textures)
  vehicle_<key>_<og|slop> 384x256 3/4 front views
"""
import bpy, bmesh, sys, math, tempfile
from pathlib import Path
import numpy as np
from mathutils import Vector, Euler

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'GymChaos/Assets'
RES = ASSETS / 'Resources'
STREAM = ASSETS / 'StreamingAssets'
OUTFITS = RES / 'Player/Outfits'
OUT = RES / 'UI/Outfit'
OUT.mkdir(parents=True, exist_ok=True)
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
FILTER = next((a.split('=', 1)[1] for a in argv if a.startswith('name_filter=')), '')
GROUPS = [a for a in argv if '=' not in a] or ['garments', 'heads', 'members', 'vehicles']
SAMPLES = 64


# ---------------------------------------------------------------- scene helpers
def reset(keep_file=False):
    if not keep_file:
        bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = SAMPLES
    sc.cycles.use_denoising = True
    sc.render.film_transparent = True
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGBA'
    sc.view_settings.view_transform = 'Standard'
    sc.view_settings.look = 'None'
    sc.world = bpy.data.worlds.new('w')
    sc.world.use_nodes = True
    bg = sc.world.node_tree.nodes['Background']
    bg.inputs[0].default_value = (0.8, 0.8, 0.82, 1)
    bg.inputs[1].default_value = 0.55
    return sc


def pbr(name, tex=None, color=(1, 1, 1, 1), rough=0.65, double=False, vcol=None):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    b = mat.node_tree.nodes['Principled BSDF']
    b.inputs['Roughness'].default_value = rough
    if tex is not None:
        t = mat.node_tree.nodes.new('ShaderNodeTexImage')
        t.image = bpy.data.images.load(str(tex))
        src = t.outputs['Color']
        if vcol:  # the game multiplies the atlas by the mesh's vertex colours (GymFixerAgent, _UseVertexColor)
            va = mat.node_tree.nodes.new('ShaderNodeVertexColor'); va.layer_name = vcol
            mx = mat.node_tree.nodes.new('ShaderNodeMix'); mx.data_type = 'RGBA'; mx.blend_type = 'MULTIPLY'
            mx.inputs[0].default_value = 1.0
            ins = [i for i in mx.inputs if i.type == 'RGBA']
            mat.node_tree.links.new(src, ins[0]); mat.node_tree.links.new(va.outputs['Color'], ins[1])
            src = [o for o in mx.outputs if o.type == 'RGBA'][0]
        mat.node_tree.links.new(src, b.inputs['Base Color'])
    else:
        b.inputs['Base Color'].default_value = color
    mat.use_backface_culling = not double
    return mat


def import_file(path):
    before = set(bpy.context.scene.objects)
    if path.suffix == '.fbx':
        bpy.ops.import_scene.fbx(filepath=str(path))
    else:
        bpy.ops.import_scene.gltf(filepath=str(path))
    new = [o for o in bpy.context.scene.objects if o not in before]
    for o in new:
        if o.type == 'ARMATURE':
            o.data.pose_position = 'REST'
            if o.animation_data:
                o.animation_data.action = None
        if o.name.startswith('Marker__'):
            o.hide_render = True
    bpy.context.view_layer.update()
    return [o for o in new if o.type == 'MESH' and not o.name.startswith('Marker__')]


def evaluated_points(objs):
    dg = bpy.context.evaluated_depsgraph_get()
    pts = []
    for o in objs:
        e = o.evaluated_get(dg)
        me = e.to_mesh()
        n = len(me.vertices)
        if n:
            a = np.empty(n * 3)
            me.vertices.foreach_get('co', a)
            m = np.array(e.matrix_world)
            a = a.reshape(n, 3)
            pts.append(a @ m[:3, :3].T + m[:3, 3])
        e.to_mesh_clear()
    return np.concatenate(pts)


def shoot(sc, objs, path, w, h, az=0.0, el=8.0, margin=0.08, region=None, pts=None, spin=0.0):
    """Orthographic camera on az/el (az 0 = from -Y, +az swings toward +X), subject centred
    with `margin` on every side. `region(pts)->mask` limits the framed points (torso crops)."""
    if pts is None:
        pts = evaluated_points(objs)
    if region is not None:
        pts = pts[region(pts)]
    a, e = math.radians(az), math.radians(el)
    d = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
    quat = (-d).to_track_quat('-Z', 'Y')
    basis = quat.to_matrix()
    right, up = basis.col[0], basis.col[1]
    ctr = (pts.min(0) + pts.max(0)) / 2
    pr, pu = pts @ np.array(right), pts @ np.array(up)
    cw, ch = pr.max() - pr.min(), pu.max() - pu.min()
    cr, cu = (pr.max() + pr.min()) / 2, (pu.max() + pu.min()) / 2
    centre = right * cr + up * cu + d * float(ctr @ np.array(d))
    size = max(cw / (w / max(w, h)), ch / (h / max(w, h))) / (1 - 2 * margin)
    cam_d = bpy.data.cameras.new('cam')
    cam_d.type = 'ORTHO'
    cam_d.ortho_scale = size
    cam_d.clip_start, cam_d.clip_end = 0.01, 100
    cam = bpy.data.objects.new('cam', cam_d)
    sc.collection.objects.link(cam)
    cam.rotation_euler = quat.to_euler()
    cam.location = centre + d * 20
    sc.camera = cam
    # key / fill / rim suns built from the camera basis (soft: wide angle, no hard shadow)
    back = basis.col[2]
    for name, vec, strength, angle in (
            ('key', back * 0.75 - right * 0.55 + up * 0.7, 2.2, 40),
            ('fill', back * 0.8 + right * 0.7 + up * 0.15, 0.9, 60),
            ('rim', -back * 0.8 + right * 0.5 + up * 0.5, 1.3, 40)):
        ld = bpy.data.lights.new(name, 'SUN')
        ld.energy = strength
        ld.angle = math.radians(angle)
        lo = bpy.data.objects.new(name, ld)
        sc.collection.objects.link(lo)
        lo.rotation_euler = Vector(vec).normalized().to_track_quat('Z', 'Y').to_euler()
    sc.render.resolution_x, sc.render.resolution_y = w, h
    sc.render.resolution_percentage = 100
    sc.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    print('RENDERED', path.name)


def wanted(name):
    return FILTER in name


# ---------------------------------------------------------------- garments
BODY_TEX = RES / 'Characters/Textures/player_authored.png'
GARMENTS = [  # name, shell mask, texture (bottoms: fabric name, whitened)
    ('top_tanktop', None, OUTFITS / 'shirt_white.png'),
    ('top_oversized', 'oversized_shell.bytes', OUTFITS / 'oversized_white.png'),
    ('top_onyx', 'onyx_shell.bytes', OUTFITS / 'onyx_white.png'),
    ('bottom_shorts', 'shorts_shell.bytes', 'bottoms_fabric'),
    ('bottom_pants', 'pants_shell.bytes', 'pants_fabric'),
]


def white_fabric(name):
    img = bpy.data.images.load(str(OUTFITS / f'{name}.png'))
    w, h = img.size
    px = np.array(img.pixels[:]).reshape(h, w, 4)
    px[..., :3] = np.clip(px[..., :3] / 0.8, 0, 1)  # PlayerTopShell: cloth / BottomsFabricLevel
    out = bpy.data.images.new('fabric_white', w, h)
    out.pixels[:] = px.ravel()
    path = Path(tempfile.gettempdir()) / 'outfit_fabric_white.png'
    out.filepath_raw = str(path)
    out.file_format = 'PNG'
    out.save()
    return path


def smooth_and_inflate(bm, vert_inflate, iterations=40, lam=0.5, mu=-0.53, normal_passes=6):
    """Same steps as Tools/render_player_tops.py (PlayerTopShell.cs): weld, Taubin-smooth, inflate."""
    verts = list(bm.verts)
    key_index = {}
    weld = np.zeros(len(verts), np.int64)
    for i, v in enumerate(verts):
        weld[i] = key_index.setdefault(tuple(round(c, 5) for c in v.co), len(key_index))
    n = len(key_index)
    pos = np.zeros((n, 3)); amount = np.zeros(n)
    for i, v in enumerate(verts):
        pos[weld[i]] = tuple(v.co)
        amount[weld[i]] = max(amount[weld[i]], vert_inflate.get(v, 0))
    tris = np.array([(weld[f.verts[0].index], weld[f.verts[j].index], weld[f.verts[j + 1].index])
                     for f in bm.faces for j in range(1, len(f.verts) - 1)])
    edges = np.concatenate([tris[:, [0, 1]], tris[:, [1, 2]], tris[:, [2, 0]]])
    edges = np.concatenate([edges, edges[:, ::-1]])
    deg = np.bincount(edges[:, 0], minlength=n).astype(float)

    def laplace(p):
        acc = np.zeros_like(p)
        np.add.at(acc, edges[:, 0], p[edges[:, 1]])
        return acc / np.maximum(deg, 1)[:, None] - p
    original = pos.copy()
    e_sorted = np.sort(np.concatenate([tris[:, [0, 1]], tris[:, [1, 2]], tris[:, [2, 0]]]), 1)
    uniq, counts = np.unique(e_sorted, axis=0, return_counts=True)
    bedges = uniq[counts == 1]
    boundary = np.zeros(n, bool); boundary[bedges.ravel()] = True
    bed = np.concatenate([bedges, bedges[:, ::-1]])
    bdeg = np.bincount(bed[:, 0], minlength=n).astype(float)

    def laplace_boundary(p):
        acc = np.zeros_like(p)
        np.add.at(acc, bed[:, 0], p[bed[:, 1]])
        return np.where(boundary[:, None], acc / np.maximum(bdeg, 1)[:, None] - p, 0)
    for _ in range(iterations):
        for f in (lam, mu):
            pos = pos + f * np.where(boundary[:, None], laplace_boundary(pos), laplace(pos))

    def normals(p):
        fn = np.cross(p[tris[:, 1]] - p[tris[:, 0]], p[tris[:, 2]] - p[tris[:, 0]])
        nv = np.zeros_like(p)
        for c in range(3):
            np.add.at(nv, tris[:, c], fn)
        return nv / np.maximum(np.linalg.norm(nv, axis=1, keepdims=True), 1e-9)
    nrm = normals(pos)
    for _ in range(normal_passes):
        nrm = nrm + laplace(nrm)
        nrm /= np.maximum(np.linalg.norm(nrm, axis=1, keepdims=True), 1e-9)
    out = pos + nrm * amount[:, None]
    gap = ((out - original) * nrm).sum(1)
    out += nrm * np.maximum(0, 0.5 * amount - gap)[:, None]
    for i, v in enumerate(verts):
        v.co = Vector(out[weld[i]])


def build_shell(sc, body, mask, mat):
    tmp = Path(tempfile.gettempdir()) / (mask + '_tmp.png')
    tmp.write_bytes((OUTFITS / mask).read_bytes())
    mimg = bpy.data.images.load(str(tmp))
    w, h = mimg.size
    px = np.array(mimg.pixels[:]).reshape(h, w, 4)
    shell = body.copy()
    shell.data = body.data.copy()
    sc.collection.objects.link(shell)
    bm = bmesh.new(); bm.from_mesh(shell.data)
    uvl = bm.loops.layers.uv.active
    infl, keep = {}, set()
    for f in bm.faces:
        ok = True
        for l in f.loops:
            uv = l[uvl].uv
            s = px[min(h - 1, max(0, int(round(uv.y * (h - 1))))), min(w - 1, max(0, int(round(uv.x * (w - 1)))))]
            if s[0] < 0.5:
                ok = False
                break
            infl[l.vert] = max(infl.get(l.vert, 0), s[1] * 0.04)
        if ok:
            keep.add(f.index)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.index not in keep], context='FACES')
    bm.verts.index_update()
    smooth_and_inflate(bm, infl)
    bm.to_mesh(shell.data); bm.free()
    shell.data.materials.clear()
    shell.data.materials.append(mat)
    return shell


def render_garments():
    for name, mask, tex in GARMENTS:
        if not wanted(name):
            continue
        bpy.ops.wm.open_mainfile(filepath=str(ROOT / 'Assets/BodyBuilders/Player/player_rig.blend'))
        sc = reset(keep_file=True)
        body = max((o for o in sc.objects if o.type == 'MESH'), key=lambda o: len(o.data.vertices))
        arm = sc.objects.get('rig')
        for o in sc.objects:
            if o.type == 'MESH' and o != body:
                o.hide_render = True
        if arm is not None:  # relaxed arms, as render_player_tops.py
            for side, sign in (('L', -1), ('R', 1)):
                parent = arm.pose.bones.get(f'upper_arm_parent.{side}')
                if parent is not None and 'IK_FK' in parent.keys():
                    parent['IK_FK'] = 1.0
                pb = arm.pose.bones.get(f'upper_arm_fk.{side}')
                if pb is not None:
                    pb.rotation_mode = 'XYZ'
                    pb.rotation_euler = Euler((0, 0, 0))
                    pb.rotation_euler.rotate_axis('Z', math.radians(sign * 62))
            bpy.context.view_layer.update()
        if mask is None:  # tank top is painted on the scan: torso from shoulders to waist
            body.data.materials.clear()
            body.data.materials.append(pbr('body', tex))
            pts = evaluated_points([body])
            z0, z1 = pts[:, 2].min(), pts[:, 2].max()
            H = z1 - z0
            shoot(sc, [body], OUT / f'{name}.png', 256, 256, 0, 0, 0.08, pts=pts,
                  region=lambda p: (p[:, 2] > z0 + .53 * H) & (p[:, 2] < z0 + .735 * H) & (abs(p[:, 0]) < .15 * H))
            continue
        fabric = tex if isinstance(tex, Path) else white_fabric(tex)
        shell = build_shell(sc, body, mask, pbr(name, fabric, double=True, rough=0.8))
        body.hide_render = True
        shoot(sc, [shell], OUT / f'{name}.png', 256, 256, 0, 0)


# ---------------------------------------------------------------- headwear
HATS = {'head_cap': 'baseball_cap', 'head_beanie': 'beanie', 'head_headband': 'headband',
        'head_buckethat': 'bucket_hat', 'head_jollycap': 'jolly_cap'}


def render_heads():
    for name, glb in HATS.items():
        if not wanted(name):
            continue
        sc = reset()
        objs = import_file(STREAM / f'BodyBuilders/wearables/{glb}.glb')
        shoot(sc, objs, OUT / f'{name}.png', 256, 256, -35, 22)


# ---------------------------------------------------------------- members
# key: (og fbx + texture, slop file stem)
MEMBERS = {k: (f'Characters/Enemies/{k}_authored', f'Characters/Textures/{"jay" if k == "jaycutler" else k}', k)
           for k in ('arnold', 'cbum', 'zyzz', 'ronnie', 'jaycutler', 'goku', 'davie', 'policeman', 'mark')}
MEMBERS['manwithsuit1'] = ('Characters/Reception/manwithsuit1_mixamo_rigged', 'Characters/Textures/manwithsuit1', 'manwithsuit1')
MEMBERS['hank'] = ('Characters/Homeless/homeless_guy_authored', 'Characters/Textures/homeless_guy', 'homeless_guy')
MEMBERS['larry'] = ('Characters/Fixer/jolly_dog_authored', 'Characters/Textures/jolly_dog', 'jolly_dog')
MEMBER_FACING = {}  # per-key azimuth override if a model faces another way
MEMBER_CROP = {'larry': (.62, .40)}
REST_POSE = {'manwithsuit1_og', 'larry_og', 'larry_slop'}  # authored pose kept (held item / lollipop)  # (height fraction from the top, half width / H) for big-headed models


def arms_down(objs_all, outward=9.0):
    """Idle pose: rotate each upper arm (world space) to hang along the torso, `outward` deg off vertical."""
    for arm in (o for o in objs_all if o.type == 'ARMATURE'):
        arm.data.pose_position = 'POSE'
        M = arm.matrix_world
        A = M.to_3x3().normalized()
        for pb in arm.pose.bones:  # importers leave the file's pose on the bones: start from the bind pose
            pb.rotation_mode = 'QUATERNION'
            pb.location, pb.rotation_quaternion, pb.scale = (0, 0, 0), (1, 0, 0, 0), (1, 1, 1)
        bpy.context.view_layer.update()
        names = ('DEF-upper_arm.L', 'DEF-upper_arm.R', 'mixamorig:LeftArm', 'mixamorig:RightArm',
                 'DEF-forearm.L', 'DEF-forearm.R', 'mixamorig:LeftForeArm', 'mixamorig:RightForeArm')
        for n in names:  # parents before children
            pb = arm.pose.bones.get(n)
            if pb is None:
                continue
            head, tail = M @ pb.head, M @ pb.tail
            side = 1 if (M @ pb.bone.head_local).x >= 0 else -1
            t = Vector((side * math.sin(math.radians(outward)), 0, -math.cos(math.radians(outward))))
            delta = A.inverted() @ (tail - head).rotation_difference(t).to_matrix() @ A  # armature space
            rp = pb.matrix.to_3x3().normalized()
            rbc = pb.rotation_quaternion.to_matrix()
            p = rp @ rbc.inverted()
            pb.rotation_quaternion = (p.inverted() @ delta @ p @ rbc).to_quaternion()
            bpy.context.view_layer.update()
        bpy.context.view_layer.update()


def render_members():
    for key, (og_model, og_tex, stem) in MEMBERS.items():
        for ver, model, tex in (('og', og_model, og_tex),
                                ('slop', f'Characters/Remake/{stem}_authored', f'Characters/Remake/Textures/{stem}')):
            name = f'member_{key}_{ver}'
            if not wanted(name):
                continue
            sc = reset()
            objs = import_file(RES / f'{model}.fbx')
            for o in list(objs):  # the glow shell is a spell effect, not part of the look
                if o.name.startswith('lollipop_glow'):
                    o.hide_render = True
                    objs.remove(o)
            if f'{key}_{ver}' not in REST_POSE:
                arms_down(list(sc.objects))
            mat = pbr('skin', RES / f'{tex}.png', rough=0.7,
                      vcol='Color' if (key == 'larry' and ver == 'og') else None)
            for o in objs:
                o.data.materials.clear()
                o.data.materials.append(mat)
            pts = evaluated_points(objs)
            ztop = pts[:, 2].max(); H = ztop - pts[:, 2].min()
            frac, half = MEMBER_CROP.get(f'{key}_{ver}', MEMBER_CROP.get(key, (.40, .30)))
            shoot(sc, objs, OUT / f'{name}.png', 256, 320, MEMBER_FACING.get(key, 0), 3, 0.06, pts=pts,
                  region=lambda p: (p[:, 2] > ztop - frac * H) & (abs(p[:, 0]) < half * H))


# ---------------------------------------------------------------- vehicles
# key: (og asset, og body texture for FBX vehicles or None)
VEHICLES = {
    'bus': ('BodyBuilders/vehicles/Davie_Bus_lod.glb', None),
    'police': ('BodyBuilders/vehicles/Policecar_lod.glb', None),
    'arnold': ('BodyBuilders/vehicles/Arnold_Hummer_lod.glb', None),
    'cbum': ('Vehicles/cbum_vehicle.fbx', 'Vehicles/Textures/cbum_vehicle.png'),
    'jaycutler': ('Vehicles/jaycutler_vehicle.fbx', 'Vehicles/Textures/jaycutler_vehicle.png'),
    'zyzz': ('Vehicles/zyzz_vehicle.fbx', 'Vehicles/Textures/zyzz_vehicle.png'),
    'cloud': ('Vehicles/goku_vehicle.fbx', 'Vehicles/Textures/goku_vehicle.png'),
}
VEHICLE_FRONT = {('arnold', 'og'): 180}  # (key, ver) -> rotation when the model's front is not toward -Y; every shot ends up front lower-left


DROP = ('Cabin__', 'Driver_Seat__', 'Steering_Wheel', 'Marker__', 'Door_Driver__Card', 'Door_Driver__Trim')


def front_angle(pts):
    """Cloud: the thick puff is the front, the thin trail the back. Returns psi (deg) with front = (sin psi, -cos psi)."""
    ax = 0 if np.ptp(pts[:, 0]) > np.ptp(pts[:, 1]) else 1
    lo, hi = pts[:, ax].min(), pts[:, ax].max()
    cut = (hi - lo) * .25
    thick = lambda m: np.ptp(pts[m][:, 2]) + np.ptp(pts[m][:, 1 - ax])
    front_hi = thick(pts[:, ax] > hi - cut) > thick(pts[:, ax] < lo + cut)
    f = np.zeros(2); f[ax] = 1 if front_hi else -1
    return math.degrees(math.atan2(f[0], -f[1]))


def render_vehicles():
    for key, (og, og_tex) in VEHICLES.items():
        for ver in ('og', 'slop'):
            name = f'vehicle_{key}_{ver}'
            if not wanted(name):
                continue
            sc = reset()
            path = (STREAM if og.startswith('BodyBuilders') else RES) / og if ver == 'og' \
                else STREAM / f'BodyBuilders/vehicles/remake/{key}.glb'
            objs = import_file(path)
            for o in list(objs):  # only body, wheels and glass: no interior, door-card or helper meshes
                if o.name.startswith(DROP) and not o.name.startswith('Cabin__Glass'):
                    o.hide_render = True
                    objs.remove(o)
            if ver == 'og' and og_tex:  # game binds the body texture on the untextured-by-name materials
                for o in objs:
                    for i, m in enumerate(o.data.materials):
                        if m and m.name.startswith('Material'):
                            o.data.materials[i] = pbr('body', RES / og_tex, rough=0.45)
            psi = front_angle(evaluated_points(objs)) if key == 'cloud' else VEHICLE_FRONT.get((key, ver), 0)
            shoot(sc, objs, OUT / f'{name}.png', 384, 256, 35 + psi, 14, 0.08)


for g in GROUPS:
    {'garments': render_garments, 'heads': render_heads,
     'members': render_members, 'vehicles': render_vehicles}[g]()
