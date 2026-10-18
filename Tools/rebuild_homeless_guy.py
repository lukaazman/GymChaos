"""Rebuild homeless_guy.glb from the image-to-3D scan.

What it fixes
* Welds the scan (its seams were split), smooths lumps on the body.
* Replaces the fused scan feet and sandals with clean modelled feet, toes,
  flip-flop soles and thong straps (the scan toes were melted blobs).
* Builds a new UV atlas and a clean texture. The source atlas is an exact
  front projection (u = 0.60853 x + 0.60347, v = 0.41461 z + 0.49783), so
  surfaces that face the camera take the exact source pixel. Hidden surfaces
  (back, sides, hair back, cap back) use flat per-part palettes decided by 3D
  rules, so one colour never bleeds over another (hair over cap, skin over
  jacket, jacket over pouch).
* Spiral lenses as in the reference.

Run: blender -b -P Tools/rebuild_homeless_guy.py -- <orig.glb> <out.glb> [workdir]
Needs Pillow importable by Blender's Python (path added below).
"""
import bpy, bmesh, sys, os, math, struct, json, io
sys.path.append(r'C:/Users/Luka/.dcc-mcp/blender-pil')
import numpy as np
from PIL import Image, ImageDraw
from mathutils import Vector

args = sys.argv[sys.argv.index('--') + 1:]
SRC, DST = os.path.abspath(args[0]), os.path.abspath(args[1])
WORK = os.path.abspath(args[2]) if len(args) > 2 else os.path.dirname(DST)
TEX = 2048
CXB = -0.17           # body centre x
CUT_Z = -0.83         # legs are cut here, feet rebuilt below
# source atlas projection: pixel_x = PXA*x + PXB, pixel_y = PYC - PYD*z
PXA, PXB, PYC, PYD = 0.60853 * 1146, 0.60347 * 1146, (1 - 0.49783) * 1682, 0.41461 * 1682


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


# =============================================================== analyse source
d = open(SRC, 'rb').read()
n = struct.unpack('<I', d[12:16])[0]
j = json.loads(d[20:20 + n])
bv = j['bufferViews'][j['images'][0]['bufferView']]
b0 = 20 + n + 8
F_img = Image.open(io.BytesIO(d[b0 + bv['byteOffset']: b0 + bv['byteOffset'] + bv['byteLength']])).convert('RGB')
FW, FH = F_img.size

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
ob0 = [o for o in bpy.data.objects if o.type == 'MESH'][0]
m0 = ob0.data
m0.calc_loop_triangles()
nt0 = len(m0.loop_triangles)
tl = np.zeros(nt0 * 3, np.int32); m0.loop_triangles.foreach_get('loops', tl); tl = tl.reshape(-1, 3)
tv = np.zeros(nt0 * 3, np.int32); m0.loop_triangles.foreach_get('vertices', tv); tv = tv.reshape(-1, 3)
uv0 = np.zeros(len(m0.loops) * 2, np.float32); m0.uv_layers[0].data.foreach_get('uv', uv0); uv0 = uv0.reshape(-1, 2)
co0 = np.zeros(len(m0.vertices) * 3, np.float32); m0.vertices.foreach_get('co', co0); co0 = co0.reshape(-1, 3)
cen0 = co0[tv].mean(1)
F_np = np.array(F_img).astype(np.float32) / 255
# faces whose uv lands on real figure texels: non-white texture
uvc = uv0[tl].mean(1)
ix = np.clip((uvc[:, 0] * FW).astype(int), 0, FW - 1)
iy = np.clip(((1 - uvc[:, 1]) * FH).astype(int), 0, FH - 1)
tcol = F_np[iy, ix]
nonwhite = tcol.min(1) < 0.8
print("SRC nonwhite faces", nonwhite.sum())

# figure mask in source texture
mk = Image.new('L', (FW, FH), 0)
dr = ImageDraw.Draw(mk)
for k in np.nonzero(nonwhite)[0]:
    pts = [(float(uv0[l, 0] * FW), float((1 - uv0[l, 1]) * FH)) for l in tl[k]]
    dr.polygon(pts, fill=255)
mask = np.array(mk) > 0
# grow colours outward from the mask (so silhouette taps never read padding rays)
Fg = F_np.copy()
have = mask.copy()
for _ in range(10):
    acc = np.zeros_like(Fg)
    cnt = np.zeros(have.shape, np.float32)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            if dx == 0 and dy == 0:
                continue
            sh = np.roll(np.roll(Fg * have[..., None], dy, 0), dx, 1)
            hv = np.roll(np.roll(have, dy, 0), dx, 1)
            acc += sh
            cnt += hv
    new = (~have) & (cnt > 0)
    Fg[new] = acc[new] / cnt[new][:, None]
    have = have | new


def paint_spiral(a, cx, cy, r):
    yy, xx = np.mgrid[0:a.shape[0], 0:a.shape[1]]
    dx, dy = xx - cx, yy - cy
    rr = np.hypot(dx, dy)
    ph = (np.arctan2(dy, dx) / (2 * math.pi) * 2 + rr / (r / 3.2)) % 1.0
    col = np.where((ph < 0.5)[..., None], np.array([0.55, 0.72, 0.91], np.float32), np.array([0.97, 0.98, 1.0], np.float32))
    col = np.where((rr > r - 2.2)[..., None], np.array([0.27, 0.27, 0.31], np.float32), col)
    m = rr < r
    a[m] = col[m]




def med(box, pred=None):
    x0, y0, x1, y1 = box
    p = F_np[y0:y1, x0:x1].reshape(-1, 3)
    if pred is not None:
        p = p[pred(p)]
    return np.median(p, axis=0)


lum = lambda p: p.mean(1)
PAL = {
    'jacket': med((400, 480, 540, 620), lambda p: (p[:, 0] > p[:, 1] * 1.6)),
    'skin': med((470, 900, 540, 1100)),
    'briefs': med((548, 800, 610, 870), lambda p: lum(p) > 0.8),
    'hair': med((440, 240, 690, 340), lambda p: (lum(p) > 0.6) & ((p.max(1) - p.min(1)) < 0.12)),
    'cap': med((510, 190, 640, 245), lambda p: (p[:, 0] > p[:, 1] * 1.3)),
    'collar': med((480, 352, 650, 392), lambda p: lum(p) < 0.4),
    'pouch': med((590, 735, 715, 820), lambda p: lum(p) < 0.25),
}
PAL['nape'] = PAL['skin'] * 0.92
SHIN = np.median(np.concatenate([F_np[1340:1400, 455:495].reshape(-1, 3), F_np[1340:1400, 645:685].reshape(-1, 3)]), axis=0)
print("SHIN", (SHIN * 255).astype(int))
sandal_px = F_np[1440:1520, 380:520].reshape(-1, 3)
sandal_px = sandal_px[lum(sandal_px) < 0.42]
STRAP = np.median(sandal_px, axis=0) if len(sandal_px) > 20 else np.array([0.42, 0.30, 0.22])
STRAP = np.array([0.42, 0.29, 0.20])
SOLE_TOP = np.array([0.47, 0.34, 0.24])
SOLE_SIDE = np.array([0.34, 0.24, 0.17])
print("PALETTE", {k: (v * 255).astype(int).tolist() for k, v in PAL.items()}, "strap", (STRAP * 255).astype(int))

# cap geometry (dome radius vs height) from visible orange faces
sat = (tcol.max(1) - tcol.min(1)) / np.maximum(tcol.max(1), 1e-6)
orange = nonwhite & (tcol[:, 0] > tcol[:, 1] * 1.25) & (tcol[:, 0] > tcol[:, 2] * 1.8) & (sat > 0.4) & (cen0[:, 2] > 0.7)
top = orange & (cen0[:, 2] > 0.90)
CAP_CX, CAP_CY = cen0[top, 0].mean(), cen0[top, 1].mean()
rr0 = np.hypot(cen0[:, 0] - CAP_CX, cen0[:, 1] - CAP_CY)
zb = np.array([0.84, 0.86, 0.88, 0.90, 0.92, 0.94, 0.96])
RD = np.array([0.205, 0.205, 0.195, 0.18, 0.16, 0.125, 0.0])
print("CAP axis", CAP_CX, CAP_CY, "RD", RD.round(3))

nk = (cen0[:, 2] > 0.70) & (cen0[:, 2] < 0.73) & (np.abs(cen0[:, 0] - CXB) < 0.05)
NECK = cen0[nk][:, :2].mean(0)
# pouch: visible dark faces around the hip pouch
pouch_f = nonwhite & (tcol.mean(1) < 0.3) & (cen0[:, 2] > 0.02) & (cen0[:, 2] < 0.18) & (cen0[:, 0] > -0.16) & (cen0[:, 0] < 0.05) & (cen0[:, 1] < 0.0)
POUCH_C = cen0[pouch_f]
print("POUCH faces", len(POUCH_C))
PB = np.r_[np.percentile(POUCH_C, 1.5, axis=0) - 0.008, np.percentile(POUCH_C, 98.5, axis=0) + 0.008]
hipf = nonwhite & (tcol.mean(1) > 0.3) & (cen0[:, 0] > PB[0]) & (cen0[:, 0] < PB[3]) & (cen0[:, 2] > PB[2] - 0.05) & (cen0[:, 2] < PB[5] + 0.05) & (cen0[:, 1] < 0.0)
Y_HIP = float(np.median(cen0[hipf, 1])) if hipf.any() else float(POUCH_C[:, 1].max())
COLLAR_R = 0.072
HEAD_RX, HEAD_RY, HEAD_CY = 0.095, 0.125, 0.045
VOX = 0.006
_pv = np.unique(np.floor(POUCH_C / VOX).astype(np.int64), axis=0)


def _dilate(rad):
    n_ = int(math.ceil(rad / VOX))
    offs_ = np.array([(a_, b_, c_) for a_ in range(-n_, n_ + 1) for b_ in range(-n_, n_ + 1) for c_ in range(-n_, n_ + 1)
                      if (a_ * a_ + b_ * b_ + c_ * c_) * VOX * VOX <= rad * rad], np.int64)
    return np.unique((_pv[:, None, :] + offs_[None]).reshape(-1, 3), axis=0)


pk = lambda g: (g[:, 0] * 73856093) ^ (g[:, 1] * 19349663) ^ (g[:, 2] * 83492791)
POUCH_NEAR_FRONT = np.sort(pk(_dilate(0.013)))
POUCH_NEAR_BACK = np.sort(pk(_dilate(0.045)))
GR = 0.004
_gx0, _gz0 = PB[0] - 0.02, PB[2] - 0.02
_nx, _nz = int((PB[3] - PB[0] + 0.04) / GR) + 2, int((PB[5] - PB[2] + 0.04) / GR) + 2
_m = np.zeros((_nz, _nx), bool)
_m[np.clip(((POUCH_C[:, 2] - _gz0) / GR).astype(int), 0, _nz - 1), np.clip(((POUCH_C[:, 0] - _gx0) / GR).astype(int), 0, _nx - 1)] = True


def _shift(m_, dy_, dx_):
    return np.roll(np.roll(m_, dy_, 0), dx_, 1)


for _ in range(4):      # close the gaps between sparse face centroids
    _m = _m | _shift(_m, 1, 0) | _shift(_m, -1, 0) | _shift(_m, 0, 1) | _shift(_m, 0, -1) | _shift(_m, 1, 1) | _shift(_m, -1, -1) | _shift(_m, 1, -1) | _shift(_m, -1, 1)
_e = _m & _shift(_m, 1, 0) & _shift(_m, -1, 0) & _shift(_m, 0, 1) & _shift(_m, 0, -1)
_w = _m.copy()
for _ in range(3):
    _w = _w | _shift(_w, 1, 0) | _shift(_w, -1, 0) | _shift(_w, 0, 1) | _shift(_w, 0, -1)
POUCH_SIL = _e
POUCH_WIDE = _w
print("POUCH bbox", PB.round(3), "hip y", round(Y_HIP, 3))

# sole / leg measurements per foot
sole_pts = co0[co0[:, 2] < -0.935]
Z_MIN = float(co0[:, 2].min())
SOLE_T = 0.021
Z_SOLE_TOP = Z_MIN + SOLE_T
feet = []
for side in ('L', 'R'):
    sel = sole_pts[:, 0] < CXB if side == 'L' else sole_pts[:, 0] >= CXB
    P0 = sole_pts[sel][:, :2]
    mu = P0.mean(0)
    w_, V_ = np.linalg.eigh(np.cov((P0 - mu).T))
    a = V_[:, np.argmax(w_)]
    if a[1] > 0:
        a = -a
    nrm_ = np.array([-a[1], a[0]])
    leg = co0[(np.abs(co0[:, 2] - CUT_Z) < 0.006) & ((co0[:, 0] < CXB) if side == 'L' else (co0[:, 0] >= CXB))]
    lc = (leg[:, :2].min(0) + leg[:, :2].max(0)) / 2
    lr = (leg[:, :2].max(0) - leg[:, :2].min(0)) / 2
    feet.append(dict(side=side, mu=mu, a=a, n=nrm_, pts=P0, leg_c=lc, leg_r=lr))
    print("FOOT", side, "mu", mu.round(3), "axis", a.round(3), "leg c", lc.round(3), "r", lr.round(3))

# =============================================================== build mesh
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
ob = [o for o in bpy.data.objects if o.type == 'MESH'][0]
bpy.context.view_layer.objects.active = ob
ob.select_set(True)
me = ob.data
while me.color_attributes:
    me.color_attributes.remove(me.color_attributes[0])
bpy.ops.mesh.customdata_custom_splitnormals_clear()
bm = bmesh.new()
bm.from_mesh(me)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
bm.to_mesh(me)
bm.free()
print("WELDED verts", len(me.vertices), "polys", len(me.polygons))

co = np.zeros(len(me.vertices) * 3, np.float32); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
vg = ob.vertex_groups.new(name='sm')
for i, c in enumerate(co):
    vg.add([i], 1.0 if c[2] < 0.68 else 0.2, 'REPLACE')
mod = ob.modifiers.new('lap', 'LAPLACIANSMOOTH')
mod.lambda_factor = 0.6
mod.iterations = 14
mod.use_volume_preserve = True
mod.use_normalized = True
mod.vertex_group = 'sm'
bpy.ops.object.modifier_apply(modifier='lap')

# cut legs at CUT_Z and close the cut
bm = bmesh.new()
bm.from_mesh(me)
bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(0, 0, CUT_Z),
                       plane_no=(0, 0, 1), clear_inner=True, use_snap_center=False)
bnd = [e for e in bm.edges if e.is_boundary]
bvs = {v for e in bnd for v in e.verts}
# split boundary edges into loops
_adj = {}
for e in bnd:
    _adj.setdefault(e.verts[0].index, []).append(e.verts[1].index)
    _adj.setdefault(e.verts[1].index, []).append(e.verts[0].index)
_seen, LOOPS = set(), []
for st in list(_adj):
    if st in _seen:
        continue
    stack, comp = [st], []
    while stack:
        q = stack.pop()
        if q in _seen:
            continue
        _seen.add(q); comp.append(q)
        stack.extend(_adj[q])
    LOOPS.append(comp)
bm.verts.ensure_lookup_table()
for comp in LOOPS:
    pp = np.array([bm.verts[i].co[:] for i in comp])
    print("LOOP", len(comp), pp.mean(0).round(3), (pp.max(0) - pp.min(0)).round(3))
for ft in feet:
    pts_ = np.array([[v.co.x, v.co.y] for v in bm.verts if (v.co.x < CXB) == (ft['side'] == 'L') and CUT_Z + 0.015 < v.co.z < CUT_Z + 0.045])
    ft['leg_c'] = pts_.mean(0)
    bp_ = np.array([[v.co.x, v.co.y] for v in bvs if (v.co.x < CXB) == (ft['side'] == 'L')])
    ft['cut_c'] = bp_.mean(0)
    ft['cut_pts'] = bp_
    rel = pts_ - ft['leg_c']
    ft['leg_ru'] = 1.42 * (rel @ ft['a']).std()
    ft['leg_rv'] = 1.42 * (rel @ ft['n']).std()
    print("LEGCUT", ft['side'], ft['leg_c'].round(3), round(ft['leg_ru'], 3), round(ft['leg_rv'], 3), len(pts_), len(bvs))
bmesh.ops.holes_fill(bm, edges=bnd, sides=80)
bmesh.ops.triangulate(bm, faces=bm.faces[:])
bm.to_mesh(me)
bm.free()
print("CUT verts", len(me.vertices))
me.attributes.new('part', 'INT', 'FACE')


def new_obj(name, bmx, part):
    mesh = bpy.data.meshes.new(name)
    bmx.to_mesh(mesh)
    bmx.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    if 'part' not in mesh.attributes:
        mesh.attributes.new('part', 'INT', 'FACE')
    mesh.attributes['part'].data.foreach_set('value', np.full(len(mesh.polygons), part, np.int32))
    return obj


def loft(rings, cap=True):
    bmx = bmesh.new()
    vs = [[bmx.verts.new(p) for p in ring] for ring in rings]
    for r in range(len(vs) - 1):
        k = len(vs[r])
        for i in range(k):
            bmx.faces.new((vs[r][i], vs[r][(i + 1) % k], vs[r + 1][(i + 1) % k], vs[r + 1][i]))
    if cap:
        bmx.faces.new(vs[0][::-1])
        bmx.faces.new(vs[-1])
    bmesh.ops.recalc_face_normals(bmx, faces=bmx.faces[:])
    return bmx


def ellipsoid(center, axes, radii, segs=14):
    bmx = bmesh.new()
    bmesh.ops.create_uvsphere(bmx, u_segments=segs, v_segments=segs // 2 + 2, radius=1.0)
    R = np.array(axes, np.float64)
    for v in bmx.verts:
        p = np.array(v.co) * np.array(radii)
        v.co = Vector(np.array(center) + p @ R)
    return bmx


STATIONS = [  # (fraction along skin length, width, height)
    (0.00, 0.034, 0.044), (0.04, 0.056, 0.060), (0.12, 0.064, 0.072), (0.22, 0.068, 0.074),
    (0.36, 0.078, 0.062), (0.55, 0.088, 0.041), (0.70, 0.098, 0.030), (0.80, 0.096, 0.023), (0.88, 0.086, 0.019)]
TOES = [  # (lateral fraction from medial edge, width, height, length beyond skin end)
    (0.14, 0.027, 0.021, 0.064), (0.34, 0.021, 0.018, 0.060), (0.53, 0.019, 0.017, 0.053),
    (0.71, 0.017, 0.016, 0.047), (0.87, 0.015, 0.014, 0.040)]

for ft in feet:
    a, nn, mu, Pf = ft['a'], ft['n'], ft['mu'], ft['pts']
    u = (Pf - mu) @ a
    v = (Pf - mu) @ nn
    u0, u1 = u.min(), u.max()
    med_sign = 1.0 if (nn[0] * (CXB - mu[0])) > 0 else -1.0
    nb = 28
    edges_ = np.linspace(u0, u1, nb + 1)
    lo, hi, uc = [], [], []
    for k in range(nb):
        s = (u >= edges_[k]) & (u <= edges_[k + 1])
        if s.sum() < 3:
            lo.append(np.nan)
            hi.append(np.nan)
        else:
            lo.append(np.percentile(v[s], 3))
            hi.append(np.percentile(v[s], 97))
        uc.append((edges_[k] + edges_[k + 1]) / 2)
    lo, hi, uc = np.array(lo), np.array(hi), np.array(uc)
    okk = ~np.isnan(lo)
    lo = np.interp(uc, uc[okk], lo[okk])
    hi = np.interp(uc, uc[okk], hi[okk])
    k5 = np.ones(5) / 5
    padf = lambda x: np.concatenate([[x[0]] * 2, x, [x[-1]] * 2])
    lo = np.convolve(padf(lo), k5, 'valid')
    hi = np.convolve(padf(hi), k5, 'valid')
    vmid = (lo + hi) / 2
    half = np.minimum((hi - lo) / 2, 0.056)
    tt = (uc - u0) / (u1 - u0)
    round_ = np.sqrt(np.clip(np.minimum(tt, 1 - tt) / 0.10, 0.0, 1.0)) * 0.92 + 0.08
    uu = np.concatenate([[u0], uc, [u1]])
    hh = np.concatenate([[0.012], half * round_, [0.012]])
    vv = np.concatenate([[vmid[0]], vmid, [vmid[-1]]])
    outline = [(uu[i], vv[i] + hh[i]) for i in range(len(uu))] + [(uu[i], vv[i] - hh[i]) for i in range(len(uu) - 1, -1, -1)]
    W = lambda uu_, vv_, z_: Vector((mu[0] + uu_ * a[0] + vv_ * nn[0], mu[1] + uu_ * a[1] + vv_ * nn[1], z_))
    bmx = bmesh.new()
    vb = [bmx.verts.new(W(p[0], p[1], Z_MIN)) for p in outline]
    vt = [bmx.verts.new(W(p[0], p[1], Z_SOLE_TOP)) for p in outline]
    kk = len(outline)
    for i in range(kk):
        bmx.faces.new((vb[i], vb[(i + 1) % kk], vt[(i + 1) % kk], vt[i]))
    bmx.faces.new(vb[::-1])
    bmx.faces.new(vt)
    bmesh.ops.recalc_face_normals(bmx, faces=bmx.faces[:])
    sole = new_obj('sole_' + ft['side'], bmx, 2)
    bev = sole.modifiers.new('bev', 'BEVEL')
    bev.width = 0.004
    bev.segments = 2
    bev.limit_method = 'ANGLE'
    bpy.context.view_layer.objects.active = sole
    bpy.ops.object.modifier_apply(modifier='bev')

    skin_u0, skin_u1 = u0 + 0.020, u1 - 0.034
    Ls = skin_u1 - skin_u0
    vm_at = lambda uu_: np.interp(uu_, uc, vmid)
    NR = 22
    rings = []
    for (s_, w_, h_) in STATIONS:
        uu_ = skin_u0 + s_ * Ls
        vc_ = vm_at(uu_)
        rings.append([W(uu_, vc_ + (w_ / 2) * math.cos(math.pi * i / NR), Z_SOLE_TOP - 0.003 + h_ * (math.sin(math.pi * i / NR) ** 0.75)) for i in range(NR + 1)])
    foot_bm = loft(rings)
    lc, ru, rv = ft['leg_c'], ft['leg_ru'], ft['leg_rv']
    NRA = 28
    phs = 2 * math.pi * np.arange(NRA) / NRA
    # outline of the scan leg exactly at the cut, in the foot frame, by angular bins
    rel = ft['cut_pts'] - ft['cut_c']
    cu_, cv_ = rel @ a, rel @ nn
    cang = np.arctan2(cv_, cu_) % (2 * math.pi)
    crad = np.hypot(cu_, cv_)
    cut_r = np.zeros(NRA)
    for i in range(NRA):
        dd_ = np.abs(((cang - phs[i] + math.pi) % (2 * math.pi)) - math.pi)
        m_ = dd_ < math.pi / NRA
        cut_r[i] = crad[m_].mean() if m_.any() else np.nan
    okr = ~np.isnan(cut_r)
    cut_r = np.interp(phs, phs[okr], cut_r[okr], period=2 * math.pi)
    cut_uv = np.stack([cut_r * np.cos(phs), cut_r * np.sin(phs)], 1)
    ell_uv = np.stack([ru * 0.90 * np.cos(phs), rv * 0.90 * np.sin(phs)], 1)   # clean shank just above the cut, kept inside it
    cc = ft['cut_c']
    ring_specs = [  # (z, centre, uv outline, forward k, back k, lateral k)
        (CUT_Z + 0.022, lc, ell_uv, 1.0, 1.0, 1.0),
        (CUT_Z, cc, cut_uv * 0.985, 1.0, 1.0, 1.0),
        (CUT_Z - 0.025, cc, cut_uv * 0.985, 1.04, 0.98, 1.0),
        (CUT_Z - 0.055, cc, cut_uv * 0.985, 1.25, 1.0, 1.02),
        (Z_SOLE_TOP + 0.045, cc, cut_uv * 0.985, 1.55, 1.06, 1.08),
        (Z_SOLE_TOP + 0.018, cc, cut_uv * 0.985, 1.9, 1.12, 1.14)]
    arings = []
    for z_, c_, uv_, kf, kb, kl in ring_specs:
        ring_ = []
        for (ur, vr) in uv_:
            ur = ur * (kf if ur > 0 else kb)
            vr = vr * kl
            ring_.append(Vector((c_[0] + a[0] * ur + nn[0] * vr, c_[1] + a[1] * ur + nn[1] * vr, z_)))
        arings.append(ring_)
    ank_bm = loft(arings)
    toe_bms = []
    fore_u = skin_u0 + 0.88 * Ls
    fore_w = 0.086
    for (fr, w_, h_, ln) in TOES:
        vv_ = vm_at(fore_u) + med_sign * (fore_w / 2 - fr * fore_w)
        uu_ = fore_u + ln / 2 - 0.006
        center = (mu[0] + uu_ * a[0] + vv_ * nn[0], mu[1] + uu_ * a[1] + vv_ * nn[1], Z_SOLE_TOP - 0.003 + h_ * 0.62)
        axes = [(a[0], a[1], 0), (nn[0], nn[1], 0), (0, 0, 1)]
        toe_bms.append(ellipsoid(center, axes, (ln / 2 + 0.004, w_ / 2, h_ * 0.62)))
    objs = [new_obj('foot_' + ft['side'], foot_bm, 1), new_obj('ank_' + ft['side'], ank_bm, 1)]
    objs += [new_obj('toe_%d_%s' % (i, ft['side']), b_, 1) for i, b_ in enumerate(toe_bms)]
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    skin = bpy.context.view_layer.objects.active
    skin.data.remesh_voxel_size = 0.0028
    bpy.ops.object.voxel_remesh()
    lap = skin.modifiers.new('lap', 'LAPLACIANSMOOTH')
    lap.lambda_factor = 0.5
    lap.iterations = 4
    lap.use_volume_preserve = True
    bpy.ops.object.modifier_apply(modifier='lap')
    if 'part' not in skin.data.attributes:
        skin.data.attributes.new('part', 'INT', 'FACE')
    skin.data.attributes['part'].data.foreach_set('value', np.full(len(skin.data.polygons), 1, np.int32))

    def surf_h(uu_, vv_):
        s_ = np.clip((uu_ - skin_u0) / Ls, 0, 1)
        sx = [t[0] for t in STATIONS]
        w_ = np.interp(s_, sx, [t[1] for t in STATIONS])
        h_ = np.interp(s_, sx, [t[2] for t in STATIONS])
        q = 1 - (2 * (vv_ - vm_at(uu_)) / w_) ** 2
        return Z_SOLE_TOP - 0.003 + h_ * (max(q, 0.0) ** 0.5) ** 0.75

    post_u = fore_u - 0.004
    post_v = vm_at(post_u) + med_sign * (fore_w / 2 - 0.24 * fore_w)
    side_u = skin_u0 + 0.40 * Ls
    straps = []
    for sgn in (+1, -1):
        sv = vm_at(side_u) + sgn * (STATIONS[4][1] / 2 + 0.014)
        pts = []
        for i in range(16):
            s_ = i / 15
            uu_ = post_u + (side_u - post_u) * s_
            vv_ = post_v + (sv - post_v) * (s_ ** 1.35)
            zz = max(surf_h(uu_, vv_) + 0.005, Z_SOLE_TOP + 0.004 + 0.002 * (1 - s_))
            pts.append((uu_, vv_, zz))
        rings = []
        for i, (uu_, vv_, zz) in enumerate(pts):
            j0, j1 = max(i - 1, 0), min(i + 1, len(pts) - 1)
            tang = np.array([pts[j1][0] - pts[j0][0], pts[j1][1] - pts[j0][1]])
            tang /= max(np.linalg.norm(tang), 1e-9)
            lat = np.array([-tang[1], tang[0]])
            ring = []
            for q_ in range(10):
                th = 2 * math.pi * q_ / 10
                ring.append(W(uu_ + math.cos(th) * 0.010 * lat[0], vv_ + math.cos(th) * 0.010 * lat[1], zz + math.sin(th) * 0.0042))
            rings.append(ring)
        straps.append(new_obj('strap_%s_%d' % (ft['side'], sgn), loft(rings), 3))
    bpy.ops.object.select_all(action='DESELECT')
    for o in [sole, skin] + straps:
        o.select_set(True)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.join()

ob = bpy.context.view_layer.objects.active
me = ob.data
bpy.ops.object.shade_smooth_by_angle(angle=math.radians(55))
print("FEET joined verts", len(me.vertices), "polys", len(me.polygons))

while me.uv_layers:
    me.uv_layers.remove(me.uv_layers[0])
me.uv_layers.new(name='UVMap')
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.0025)
bpy.ops.object.mode_set(mode='OBJECT')

# =============================================================== bake maps
sc = bpy.context.scene
sc.render.engine = 'CYCLES'
sc.cycles.device = 'CPU'
sc.cycles.samples = 1
sc.view_settings.view_transform = 'Raw'
ob.data.materials.clear()


def bake_map(kind):
    img = bpy.data.images.new('b_' + kind, TEX, TEX, float_buffer=True, alpha=False)
    mat = bpy.data.materials.new('bm_' + kind)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    em = nt.nodes.new('ShaderNodeEmission')
    out_ = nt.nodes.new('ShaderNodeOutputMaterial')
    tn = nt.nodes.new('ShaderNodeTexImage')
    tn.image = img
    if kind == 'part':
        at = nt.nodes.new('ShaderNodeAttribute')
        at.attribute_name = 'part'
        at.attribute_type = 'GEOMETRY'
        comb = nt.nodes.new('ShaderNodeCombineXYZ')
        for i_ in range(3):
            nt.links.new(at.outputs['Fac'], comb.inputs[i_])
        nt.links.new(comb.outputs[0], em.inputs['Color'])
    else:
        geo = nt.nodes.new('ShaderNodeNewGeometry')
        vm = nt.nodes.new('ShaderNodeVectorMath')
        if kind == 'pos':
            vm.operation = 'ADD'
            vm.inputs[1].default_value = (1, 1, 1)
            nt.links.new(geo.outputs['Position'], vm.inputs[0])
        else:
            vm.operation = 'MULTIPLY_ADD'
            vm.inputs[1].default_value = (.5, .5, .5)
            vm.inputs[2].default_value = (.5, .5, .5)
            nt.links.new(geo.outputs['Normal'], vm.inputs[0])
        nt.links.new(vm.outputs[0], em.inputs['Color'])
    nt.links.new(em.outputs[0], out_.inputs[0])
    nt.nodes.active = tn
    ob.data.materials.clear()
    ob.data.materials.append(mat)
    bpy.ops.object.bake(type='EMIT', margin=24, margin_type='EXTEND')
    return np.array(img.pixels[:], np.float32).reshape(TEX, TEX, 4)[..., :3]


pos = bake_map('pos') - 1.0
nrm = bake_map('nrm') * 2 - 1
partm = bake_map('part')[..., 0]
nrm /= np.maximum(np.linalg.norm(nrm, axis=-1, keepdims=True), 1e-6)
covered = np.abs(pos).sum(-1) > 1e-4
pr = np.round(partm).astype(np.int32)
for _ in range(2):      # remove speckles at island borders
    cnts = []
    for k_ in range(4):
        e_ = (pr == k_).astype(np.int32)
        c_ = np.zeros_like(e_)
        for dy_ in (-1, 0, 1):
            for dx_ in (-1, 0, 1):
                c_ += np.roll(np.roll(e_, dy_, 0), dx_, 1)
        cnts.append(c_)
    pr = np.argmax(np.stack(cnts), 0).astype(np.int32)
partm = pr.astype(np.float32)
print("COVERAGE", covered.mean(), "part counts", [int(((np.round(partm) == k) & covered).sum()) for k in range(4)])

# front-depth map of the final mesh in source-pixel space
me.calc_loop_triangles()
ntf = len(me.loop_triangles)
ftv = np.zeros(ntf * 3, np.int32); me.loop_triangles.foreach_get('vertices', ftv); ftv = ftv.reshape(-1, 3)
cof = np.zeros(len(me.vertices) * 3, np.float32); me.vertices.foreach_get('co', cof); cof = cof.reshape(-1, 3)
px_v = cof[:, 0] * PXA + PXB
py_v = PYC - cof[:, 2] * PYD
order = np.argsort(-cof[ftv].mean(1)[:, 1])
depth = Image.new('F', (FW, FH), 9.0)
dd = ImageDraw.Draw(depth)
for k in order:
    tri = ftv[k]
    dd.polygon([(float(px_v[i]), float(py_v[i])) for i in tri], fill=float(cof[tri][:, 1].min()))
depth = np.array(depth)

# =============================================================== colour
cov = np.nonzero(covered.reshape(-1))[0]
P = pos.reshape(-1, 3)[cov]
N = nrm.reshape(-1, 3)[cov]
PART = np.round(partm.reshape(-1)[cov]).astype(int)
out = np.zeros((TEX * TEX, 3), np.float32)

px = P[:, 0] * PXA + PXB
py = PYC - P[:, 2] * PYD


def bilin(img, x, y):
    x = np.clip(x, 0, FW - 1.001)
    y = np.clip(y, 0, FH - 1.001)
    x0 = x.astype(int)
    y0 = y.astype(int)
    fx = (x - x0)[:, None]
    fy = (y - y0)[:, None]
    return (img[y0, x0] * (1 - fx) * (1 - fy) + img[y0, x0 + 1] * fx * (1 - fy) +
            img[y0 + 1, x0] * (1 - fx) * fy + img[y0 + 1, x0 + 1] * fx * fy)


front_c = bilin(Fg, px, py)
for lcx in (551.0, 604.0):          # spiral lenses, drawn per texel so they stay sharp
    rr = np.hypot(px - lcx, py - 284.0)
    r_l = 23.0
    inl = rr < r_l + 1.0
    if inl.any():
        ang = np.arctan2(py - 284.0, px - lcx)
        ph = ang / (2 * math.pi) * 2 + rr / (r_l / 3.2)
        t = smooth(-0.3, 0.3, np.sin(2 * math.pi * ph))[:, None]
        sp_c = np.array([0.55, 0.72, 0.91], np.float32) * (1 - t) + np.array([0.97, 0.98, 1.0], np.float32) * t
        rim = smooth(r_l - 2.6, r_l - 1.6, rr)[:, None]
        sp_c = sp_c * (1 - rim) + np.array([0.27, 0.27, 0.31], np.float32) * rim
        a_ = (1 - smooth(r_l - 0.4, r_l + 0.8, rr))[:, None]
        front_c = np.where(inl[:, None], front_c * (1 - a_) + sp_c * a_, front_c)
dep = depth[np.clip(py.astype(int), 0, FH - 1), np.clip(px.astype(int), 0, FW - 1)]
visible = (P[:, 1] <= dep + 0.012)
wf = smooth(-0.30, -0.62, N[:, 1]) * visible

x, y, z = P[:, 0], P[:, 1], P[:, 2]
dxb = np.abs(x - CXB)
# segmentation of the source figure (what colour family each source pixel is)
Fm = Fg
mxv = Fm.max(2)
mnv = Fm.min(2)
satv = (mxv - mnv) / np.maximum(mxv, 1e-6)
lumv = Fm.mean(2)
rr_, gg_, bb_ = Fm[..., 0], Fm[..., 1], Fm[..., 2]
seg = np.zeros((FH, FW), np.uint8)             # 0 other
seg[(rr_ > gg_) & (gg_ > bb_) & (satv > 0.2) & (satv < 0.65) & (lumv > 0.47)] = 2   # skin
seg[(rr_ > gg_ * 1.45) & (gg_ > bb_ * 1.35) & (satv > 0.5) & (lumv > 0.35)] = 5      # cap orange
seg[(rr_ > gg_ * 1.7) & (gg_ < bb_ * 1.35) & (satv > 0.45)] = 1                      # jacket red
seg[(satv < 0.15) & (lumv > 0.55)] = 3                                                # light
seg[lumv < 0.33] = 4                                                                  # dark
sp = seg[np.clip(py.astype(int), 0, FH - 1), np.clip(px.astype(int), 0, FW - 1)]

# ---- part classes from 3D geometry (clean boundaries, no pixel noise)
CL = ['skin', 'face', 'nape', 'jacket', 'briefs', 'collar', 'hair', 'cap', 'pouch', 'glass']
cid = {k: i for i, k in enumerate(CL)}
cls = np.zeros(len(P), np.int32)                 # skin
rneck = np.hypot(x - NECK[0], y - NECK[1])
torso_b = (dxb < 0.20) & (z >= 0.05) & (z < 0.70)
arm_b = (dxb >= 0.20) & (z >= 0.03) & (z < 0.70)
cls[torso_b | arm_b] = cid['jacket']
# white briefs all the way round: leg openings rise toward the hips, band continues across the sides
z_low = np.where(dxb < 0.105, -0.07 + 1.0 * dxb, 0.035)
cls[(z < 0.075) & (dxb < 0.19) & (z > z_low) & (z >= -0.07)] = cid['briefs']
# jacket collar: stands around the neck
neckzone = (z >= 0.625) & (z < 0.705) & (rneck < 0.118)
cls[neckzone & ((rneck >= COLLAR_R) | (y > NECK[1] + 0.01))] = cid['collar']
cls[(z >= 0.705) & (z < 0.76) & (rneck < 0.118)] = cid['nape']      # skin of the neck / nape under the hair
# head: hair spikes, skin core (face / cheeks / nape), cap, glasses
hair_zone = ((z >= 0.745) & ~((z < 0.76) & (rneck < 0.118))) | ((z >= 0.705) & (rneck >= 0.118))
cls[hair_zone] = cid['hair']
E = ((x - CXB) / HEAD_RX) ** 2 + ((y - HEAD_CY) / HEAD_RY) ** 2 < 1
cls[hair_zone & E & (z < 0.80) & (y < 0.03)] = cid['face']
cls[hair_zone & E & (z < 0.755) & (y >= 0.03)] = cid['nape']
cls[(y < -0.07) & (z > 0.74) & (z < 0.835) & (dxb < 0.115)] = cid['glass']
rcap = np.hypot(x - CAP_CX, y - CAP_CY)
rd = np.interp(z, zb, RD, left=RD[0] + 0.02, right=0.0)
iscap = (z >= 0.745) & ((z > 0.935) | ((z > 0.845) & (rcap <= rd + 0.015)) | ((y < -0.09) & (z > 0.80)))
cls[iscap] = cid['cap']
gv = np.floor(P / VOX).astype(np.int64)
hk = pk(gv)
in_bb = (x > PB[0]) & (x < PB[3]) & (z > PB[2]) & (z < PB[5])
# the pouch is whatever sticks out in front of the jacket plane, plus its side / back faces next to it
sx = np.clip(((x - _gx0) / GR).astype(int), 0, _nx - 1)
sz = np.clip(((z - _gz0) / GR).astype(int), 0, _nz - 1)
jacket_plane = (N[:, 1] < -0.6) & (y > Y_HIP - 0.012)          # jacket front around the pouch stays jacket
pouch_in = in_bb & (POUCH_SIL[sz, sx] | (POUCH_WIDE[sz, sx] & ~jacket_plane)) & (y < Y_HIP + 0.07)
# the body-side end cap of the pouch is hidden from the front, so the silhouette misses it
end_cap = (x > PB[3] - 0.025) & (x < PB[3] + 0.045) & (z > PB[2]) & (z < PB[5]) & (y < Y_HIP + 0.05) & ~jacket_plane
pouch_in = pouch_in | end_cap
cls[pouch_in] = cid['pouch']

L = np.array([0.30, 0.55, 0.78])
L /= np.linalg.norm(L)
lam = np.clip(N @ L, -0.2, 1.0)
shade = (0.80 + 0.20 * lam)[:, None]
shade_bright = (0.92 + 0.08 * lam)[:, None]
PALA = np.zeros((len(CL), 3), np.float32)
for k_ in CL:
    PALA[cid[k_]] = {'face': PAL['skin'] * 0.97, 'nape': PAL['skin'] * 0.97, 'glass': np.array([0.30, 0.28, 0.30]), 'collar': np.array([0.15, 0.13, 0.13]),
                     'pouch': np.array([0.12, 0.11, 0.11]), 'briefs': np.array([0.94, 0.93, 0.91]), 'hair': np.minimum(PAL['hair'] * 1.14, 1.0)}.get(k_, PAL.get(k_, PAL['skin']))
hid = PALA[cls] * np.where(np.isin(cls, (cid['briefs'], cid['cap'], cid['hair']))[:, None], shade_bright, shade)

# source pixel allowed for a class? (never let another part's colour through)
REJ = {'skin': (1, 3, 4, 5), 'face': (1, 3, 5), 'nape': (1, 3, 4, 5), 'jacket': (5,), 'briefs': (1, 2, 5), 'collar': (1, 2, 3, 5),
       'hair': (1, 2, 5), 'cap': (1, 2, 3), 'pouch': (1, 2, 5), 'glass': ()}
ok_tab = np.ones((len(CL), 6), bool)
for k_, rj in REJ.items():
    for r_ in rj:
        ok_tab[cid[k_], r_] = False
# front-facing collar texels whose source pixel is skin / jacket belong to the V opening, not the collar
fix = (cls == cid['collar']) & (wf > 0.3) & np.isin(sp, (1, 2, 3, 5))
cls[fix & (sp == 1)] = cid['jacket']
cls[fix & (sp != 1)] = cid['skin']
hid = PALA[cls] * np.where(np.isin(cls, (cid['briefs'], cid['cap'], cid['hair']))[:, None], shade_bright, shade)
accept = ok_tab[cls, sp]
fc = front_c.copy()
fc[cls == cid['collar']] *= 0.55
fc[cls == cid['pouch']] *= 0.8
wfc = wf * accept
col = front_c * 0 + (fc * wfc[:, None] + hid * (1 - wfc)[:, None])
if os.environ.get('HG_DEBUG'):
    dbgc = np.array([[0.9, 0.7, 0.5], [1, .85, .6], [.8, .55, .4], [0.7, 0.1, 0.1], [1, 1, 1], [0, 0, 0], [0.5, 0.6, 1], [1, 0.5, 0], [0.3, 0.3, 0.3], [0, 1, 0]], np.float32)
    col = dbgc[cls]
col = np.where((PART == 0)[:, None], col, 0.0)

skin_c = SHIN * (0.82 + 0.18 * lam)[:, None]
skin_c = skin_c * (0.78 + 0.22 * smooth(Z_SOLE_TOP, Z_SOLE_TOP + 0.03, z))[:, None]
ramp = smooth(CUT_Z - 0.05, CUT_Z, z)[:, None] * (wf > 0.3)[:, None]
skin_c = skin_c * (1 - ramp) + front_c * ramp
col[PART == 1] = skin_c[PART == 1]
topf = smooth(0.3, 0.8, N[:, 2])[:, None]
sole_c = (SOLE_SIDE * (1 - topf) + SOLE_TOP * topf) * shade
col[PART == 2] = sole_c[PART == 2]
col[PART == 3] = (STRAP * 0.95 * (0.82 + 0.18 * lam)[:, None])[PART == 3]
out[cov] = col
flat = out.reshape(TEX, TEX, 3)
flat[~covered] = flat[covered].mean(0)
img8 = np.clip(flat * 255 + 0.5, 0, 255).astype(np.uint8)
png = os.path.join(WORK, 'homeless_guy_albedo.png')
Image.fromarray(img8[::-1]).save(png)  # Blender rows are bottom-up
print("TEXTURE", png)

# =============================================================== material + export
img = bpy.data.images.load(png)
img.pack()
img.name = 'homeless_guy'
mat = bpy.data.materials.new('homeless_guy')
mat.use_nodes = True
nt = mat.node_tree
bsdf = nt.nodes['Principled BSDF']
tn = nt.nodes.new('ShaderNodeTexImage')
tn.image = img
nt.links.new(tn.outputs['Color'], bsdf.inputs['Base Color'])
bsdf.inputs['Roughness'].default_value = 0.85
bsdf.inputs['Metallic'].default_value = 0.0
ob.data.materials.clear()
ob.data.materials.append(mat)
bpy.ops.export_scene.gltf(filepath=DST, export_format='GLB', export_image_format='AUTO',
                          export_yup=True, export_apply=False, export_normals=True,
                          export_attributes=False, use_selection=False)
print("EXPORTED", DST, os.path.getsize(DST))
