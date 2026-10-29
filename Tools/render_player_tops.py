"""Front/back renders of the player in every top, for visual review.

    blender -b --factory-startup -P Tools/render_player_tops.py -- [top_color ...]

Uses the textures written by build_player_tops.py. The Oversized shell is
rebuilt here the same way PlayerTopShell builds it in Unity (faces whose
corners all sit in the shell mask, pushed out along welded normals by the
mask's inflation). Arms are lowered to a relaxed pose. Workbench renders go
to .tools/tops/render_<top>_<color>.png (front | back).
"""
import bpy, bmesh, sys, math
from pathlib import Path
import numpy as np
from mathutils import Vector, Euler

root = Path(__file__).resolve().parents[1]
outfits = root / 'GymChaos/Assets/Resources/Player/Outfits'
out_dir = root / '.tools/tops'
out_dir.mkdir(parents=True, exist_ok=True)
args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
looks = args or ['tank_black', 'oversized_black', 'oversized_white', 'oversized_red', 'onyx_black', 'onyx_blue', 'onyx_gold']

bpy.ops.wm.open_mainfile(filepath=str(root / 'Assets/BodyBuilders/Player/player_rig.blend'))
scene = bpy.context.scene
body = max((o for o in scene.objects if o.type == 'MESH'), key=lambda o: len(o.data.vertices))
arm = scene.objects.get('rig') or next((o for o in scene.objects if o.type == 'ARMATURE'), None)
for o in scene.objects:
    if o.type == 'MESH' and o != body:
        o.hide_render = True

# Relaxed arms: rotate the upper arms down toward the body.
if arm is not None:
    for side, sign in (('L', -1), ('R', 1)):
        parent = arm.pose.bones.get(f'upper_arm_parent.{side}')
        if parent is not None and 'IK_FK' in parent.keys():
            parent['IK_FK'] = 1.0
        for name in (f'upper_arm_fk.{side}',):
            pb = arm.pose.bones.get(name)
            if pb is not None:
                pb.rotation_mode = 'XYZ'
                pb.rotation_euler = Euler((0, 0, 0))
                pb.rotation_euler.rotate_axis('Z', math.radians(sign * 62))
                break
    bpy.context.view_layer.update()

img_cache = {}
def image(path):
    if path not in img_cache:
        img_cache[path] = bpy.data.images.load(str(path))
    return img_cache[path]

def make_material(name, tex_path, double=False):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes.get('Principled BSDF')
    t = nt.nodes.new('ShaderNodeTexImage')
    t.image = image(tex_path)
    nt.links.new(t.outputs['Color'], bsdf.inputs['Base Color'])
    mat.use_backface_culling = not double
    return mat


def smooth_and_inflate(bm, vert_inflate, iterations=40, lam=0.5, mu=-0.53, normal_passes=6):
    """Same steps as PlayerTopShell.cs: weld by position, Taubin-smooth the
    surface (cloth hangs smooth over the muscles), push out along smoothed
    normals by the mask inflation, never less than half of it outside."""
    verts = list(bm.verts)
    key_index = {}
    weld = np.zeros(len(verts), np.int64)
    for i, v in enumerate(verts):
        k = tuple(round(c, 5) for c in v.co)
        weld[i] = key_index.setdefault(k, len(key_index))
    n = len(key_index)
    pos = np.zeros((n, 3)); amount = np.zeros(n)
    for i, v in enumerate(verts):
        pos[weld[i]] = tuple(v.co)
        amount[weld[i]] = max(amount[weld[i]], vert_inflate.get(v, 0))
    tris = []
    for f in bm.faces:
        idx = [weld[v.index] for v in f.verts]
        for j in range(1, len(idx) - 1):
            tris.append((idx[0], idx[j], idx[j + 1]))
    tris = np.array(tris)
    edges = np.concatenate([tris[:, [0, 1]], tris[:, [1, 2]], tris[:, [2, 0]]])
    edges = np.concatenate([edges, edges[:, ::-1]])
    deg = np.bincount(edges[:, 0], minlength=n).astype(float)
    def laplace(p):
        acc = np.zeros_like(p)
        np.add.at(acc, edges[:, 0], p[edges[:, 1]])
        return acc / np.maximum(deg, 1)[:, None] - p
    original = pos.copy()
    # Open edges (hem, cuffs, collar): smooth along the edge loop only, so
    # the jagged scan triangles become a straight hem.
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
        lp = laplace(pos); lb = laplace_boundary(pos)
        pos = pos + lam * np.where(boundary[:, None], lb, lp)
        lp = laplace(pos); lb = laplace_boundary(pos)
        pos = pos + mu * np.where(boundary[:, None], lb, lp)
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

def build_shell(tex, mask='oversized_shell.bytes'):
    data = np.frombuffer((outfits / mask).read_bytes(), np.uint8)
    import tempfile, os
    tmp = Path(tempfile.gettempdir()) / (mask + '_tmp.png')
    tmp.write_bytes(data.tobytes())
    mimg = bpy.data.images.load(str(tmp))
    w, h = mimg.size
    px = np.array(mimg.pixels[:]).reshape(h, w, 4)
    shell = body.copy()
    shell.data = body.data.copy()
    shell.name = 'Oversized Shell'
    scene.collection.objects.link(shell)
    bm = bmesh.new()
    bm.from_mesh(shell.data)
    uv_layer = bm.loops.layers.uv.active
    def sample(uv):
        x = min(w - 1, max(0, int(round(uv.x * (w - 1)))))
        y = min(h - 1, max(0, int(round(uv.y * (h - 1)))))
        return px[y, x]
    vert_inflate = {}
    keep = set()
    for f in bm.faces:
        ok = True
        for l in f.loops:
            s = sample(l[uv_layer].uv)
            if s[0] < 0.5:
                ok = False
                break
            vert_inflate[l.vert] = max(vert_inflate.get(l.vert, 0), s[1] * 0.04)
        if ok:
            keep.add(f.index)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.index not in keep], context='FACES')
    bm.verts.index_update()
    smooth_and_inflate(bm, vert_inflate)
    bm.to_mesh(shell.data)
    bm.free()
    shell.data.materials.clear()
    shell.data.materials.append(make_material('shell', tex, double=True))
    return shell

scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'TEXTURE'
scene.display.shading.show_backface_culling = False
scene.render.resolution_x = 520
scene.render.resolution_y = 760
scene.render.film_transparent = False
scene.world.color = (0.2, 0.2, 0.22) if scene.world else None
cam_data = bpy.data.cameras.new('cam')
cam_data.type = 'ORTHO'
cam_data.ortho_scale = 0.62
cam = bpy.data.objects.new('cam', cam_data)
scene.collection.objects.link(cam)
scene.camera = cam

# Bottoms (Tools/build_player_bottoms.py): grey fabric tinted like
# PlayerTopShell does at runtime (colour / BottomsFabricLevel).
BOTTOM_COLORS = {'black': (0.025, 0.03, 0.04), 'white': (0.9, 0.92, 0.9), 'red': (0.72, 0.045, 0.025),
                 'blue': (0.035, 0.22, 0.78), 'gold': (0.92, 0.58, 0.08)}

def tinted_fabric(color):
    import tempfile
    img = bpy.data.images.load(str(outfits / 'bottoms_fabric.png'))
    w, h = img.size
    px = np.array(img.pixels[:]).reshape(h, w, 4)
    px[..., :3] = np.clip(px[..., :3] * np.array(BOTTOM_COLORS[color]) / 0.8, 0, 1)
    out = bpy.data.images.new('fabric_' + color, w, h)
    out.pixels[:] = px.ravel()
    path = Path(tempfile.gettempdir()) / f'bottoms_{color}.png'
    out.filepath_raw = str(path)
    out.file_format = 'PNG'
    out.save()
    return path

for look in looks:
    top, color = look.split('_', 1)
    if top in ('shorts', 'pants'):
        body.data.materials.clear()
        body.data.materials.append(make_material(look, root / 'GymChaos/Assets/Resources/Characters/Textures/player_authored.png'))
        shell = build_shell(tinted_fabric(color), f'{top}_shell.bytes')
        cam_data.ortho_scale = 1.05
        frames = []
        for view, (pos, rot) in {'front': ((0, -3, 0.5), (math.radians(90), 0, 0)),
                                 'back': ((0, 3, 0.5), (math.radians(90), 0, math.radians(180)))}.items():
            cam.location = pos
            cam.rotation_euler = rot
            path = out_dir / f'_{look}_{view}.png'
            scene.render.filepath = str(path)
            bpy.ops.render.render(write_still=True)
            frames.append(path)
        a = bpy.data.images.load(str(frames[0])); b = bpy.data.images.load(str(frames[1]))
        W, H = a.size
        combo = bpy.data.images.new('combo', W * 2, H)
        combo.pixels[:] = np.concatenate([np.array(a.pixels[:]).reshape(H, W, 4), np.array(b.pixels[:]).reshape(H, W, 4)], 1).ravel()
        combo.filepath_raw = str(out_dir / f'render_{look}.png')
        combo.file_format = 'PNG'
        combo.save()
        bpy.data.objects.remove(shell)
        print('RENDERED', look)
        continue
    tex = (root / 'GymChaos/Assets/Resources/Characters/Textures/player_authored.png'
           if look == 'tank_black' else
           outfits / (f'shirt_{color}.png' if top == 'tank' else f'{top}_{color}.png'))
    mat = make_material(look, tex)
    body.data.materials.clear()
    body.data.materials.append(mat)
    shell = build_shell(tex) if top == 'oversized' else None
    frames = []
    for view, (pos, rot) in {'front': ((0, -3, 0.62), (math.radians(90), 0, 0)),
                             'back': ((0, 3, 0.62), (math.radians(90), 0, math.radians(180)))}.items():
        cam.location = pos
        cam.rotation_euler = rot
        path = out_dir / f'_{look}_{view}.png'
        scene.render.filepath = str(path)
        bpy.ops.render.render(write_still=True)
        frames.append(path)
    a = bpy.data.images.load(str(frames[0])); b = bpy.data.images.load(str(frames[1]))
    W, H = a.size
    pa = np.array(a.pixels[:]).reshape(H, W, 4); pb = np.array(b.pixels[:]).reshape(H, W, 4)
    combo = bpy.data.images.new('combo', W * 2, H)
    combo.pixels[:] = np.concatenate([pa, pb], 1).ravel()
    combo.filepath_raw = str(out_dir / f'render_{look}.png')
    combo.file_format = 'PNG'
    combo.save()
    if shell is not None:
        bpy.data.objects.remove(shell)
    print('RENDERED', look)
