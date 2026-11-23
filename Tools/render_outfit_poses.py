"""Renders the runtime outfit bakes written by GymChaosOutfitPoseExport.

    blender -b --factory-startup -P Tools/render_outfit_poses.py -- <export folder> [look ...]

Each look gets a sheet (rows: poses, columns: front, side, back, hips close-up)
at <export folder>/sheet_<look>.png. Textures come from Resources by name and
are multiplied by the material colour, as URP Lit does.
"""
import bpy, sys, math, json, re
from pathlib import Path
import numpy as np

root = Path(__file__).resolve().parents[1]
args = sys.argv[sys.argv.index('--') + 1:]
folder = Path(args[0]).resolve()
manifest = json.loads((folder / 'manifest.json').read_text())
looks = args[1:] or list(manifest)
resources = root / 'GymChaos/Assets/Resources'
texture_files = {p.stem: p for p in resources.rglob('*.png')}

scene = bpy.context.scene
for o in list(scene.objects):
    bpy.data.objects.remove(o)
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'TEXTURE'
scene.display.shading.show_backface_culling = False
scene.view_settings.view_transform = 'Standard'
scene.render.film_transparent = False
if scene.world is None:
    scene.world = bpy.data.worlds.new('w')
scene.world.color = (0.2, 0.21, 0.23)
TILE_W, TILE_H = 300, 520
cam_data = bpy.data.cameras.new('cam')
cam_data.type = 'ORTHO'
cam = bpy.data.objects.new('cam', cam_data)
scene.collection.objects.link(cam)
scene.camera = cam

images = {}
def tinted(texture, color):
    key = (texture, tuple(round(c, 3) for c in color))
    if key in images:
        return images[key]
    if texture:
        src = bpy.data.images.load(str(texture_files[texture]))
        w, h = src.size
        px = np.array(src.pixels[:], np.float32).reshape(h, w, 4)
    else:
        w = h = 4
        px = np.ones((h, w, 4), np.float32)
    px[..., :3] = np.clip(px[..., :3] * np.array(color, np.float32), 0, 1)
    img = bpy.data.images.new(f'{texture}_{len(images)}', w, h)
    img.pixels[:] = px.ravel()
    img.pack()
    images[key] = img
    return img

materials = {}
def material(texture, color):
    img = tinted(texture, color)
    if img.name in materials:
        return materials[img.name]
    mat = bpy.data.materials.new(img.name)
    mat.use_nodes = True
    nt = mat.node_tree
    t = nt.nodes.new('ShaderNodeTexImage')
    t.image = img
    nt.links.new(t.outputs['Color'], nt.nodes['Principled BSDF'].inputs['Base Color'])
    materials[img.name] = mat
    return mat

def render(path, pos, rot, scale, target):
    cam.location = pos
    cam.rotation_euler = rot
    cam_data.ortho_scale = scale
    cam.location = (pos[0] + target[0], pos[1] + target[1], target[2])
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(str(path))
    px = np.array(img.pixels[:], np.float32).reshape(img.size[1], img.size[0], 4)
    bpy.data.images.remove(img)
    path.unlink()
    return px

files = sorted(folder.glob('*.obj'))
for look in looks:
    parts = manifest[look]
    poses = sorted({re.match(r'.+__(.+)__', f.stem).group(1) for f in files if f.stem.startswith(look + '__')},
                   key=lambda p: ('idle' not in p, 'jump' in p, p))
    rows = []
    for pose in poses:
        objs = []
        for part, mats in parts.items():
            path = folder / f'{look}__{pose}__{part}.obj'
            if not path.exists():
                continue
            before = set(scene.objects)
            bpy.ops.wm.obj_import(filepath=str(path), forward_axis='NEGATIVE_Z', up_axis='Y')
            for o in set(scene.objects) - before:
                sub = int(re.search(r'sub(\d+)', o.name).group(1))
                spec = mats[min(sub, len(mats) - 1)]
                o.data.shade_smooth()
                o.data.materials.clear()
                o.data.materials.append(material(spec['texture'], spec['color']))
                objs.append(o)
        lo = np.min([np.min([o.matrix_world @ v.co for v in o.data.vertices], 0) for o in objs], 0)
        hi = np.max([np.max([o.matrix_world @ v.co for v in o.data.vertices], 0) for o in objs], 0)
        height = hi[2] - lo[2]
        centre = (lo + hi) / 2
        body = (centre[0], centre[1], centre[2])
        hips = (centre[0], centre[1], lo[2] + height * 0.5)
        scene.render.resolution_x, scene.render.resolution_y = TILE_W, TILE_H
        tiles = []
        for pos, rot in (((0, -4, 0), (math.radians(90), 0, 0)),
                         ((4, 0, 0), (math.radians(90), 0, math.radians(90))),
                         ((0, 4, 0), (math.radians(90), 0, math.radians(180)))):
            tiles.append(render(folder / '_tile.png', pos, rot, height * 1.08, body))
        # Close-up of the waist and hips from the front-left (top/bottom join).
        tiles.append(render(folder / '_tile.png', (-2.8, -2.8, 0), (math.radians(90), 0, math.radians(-45)), height * 0.55, hips))
        rows.append(np.concatenate(tiles, 1))
        for o in objs:
            bpy.data.meshes.remove(o.data)
    sheet = np.concatenate(rows[::-1], 0)
    h, w = sheet.shape[:2]
    out = bpy.data.images.new('sheet', w, h)
    out.pixels[:] = sheet.ravel()
    out.filepath_raw = str(folder / f'sheet_{look}.png')
    out.file_format = 'PNG'
    out.save()
    bpy.data.images.remove(out)
    print('SHEET', look, poses)
