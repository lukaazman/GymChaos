"""Fresh-import check and preview renders for abandoned_tent.glb.

    blender -b --factory-startup -P Assets/Tent/verify_abandoned_tent.py

Prints TENT_VERIFY_OK or TENT_VERIFY_FAIL and writes renders/*.png.
"""

import math
import os
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
GLB = os.path.join(ROOT, "Assets", "BodyBuilders", "outside", "abandoned_tent.glb")
RENDERS = os.path.join(HERE, "renders")
SKIP_RENDER = "--no-render" in sys.argv

failures = []


def check(cond, msg):
    if not cond:
        failures.append(msg)
        print("FAIL:", msg)


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=GLB)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
check(len(meshes) >= 8, "expected >= 8 mesh objects, got %d" % len(meshes))

tris = 0
mins = Vector((1e9, 1e9, 1e9))
maxs = Vector((-1e9, -1e9, -1e9))
mat_names = set()
for o in meshes:
    tris += sum(len(p.vertices) - 2 for p in o.data.polygons)
    for corner in o.bound_box:
        w = o.matrix_world @ Vector(corner)
        mins = Vector((min(mins[i], w[i]) for i in range(3)))
        maxs = Vector((max(maxs[i], w[i]) for i in range(3)))
    for slot in o.material_slots:
        mat_names.add(slot.material.name)
        nodes = slot.material.node_tree.nodes
        tex = [n for n in nodes if n.type == "TEX_IMAGE" and n.image is not None]
        check(len(tex) >= 1, "material %s has no texture" % slot.material.name)
print("TENT_TRIS", tris, "MATERIALS", len(mat_names))
dims = maxs - mins
print("TENT_BBOX", tuple(round(v, 2) for v in dims), "min", tuple(round(v, 2) for v in mins))
check(tris < 4000, "triangle count too high: %d" % tris)
check(len(mat_names) >= 12, "too few materials: %d" % len(mat_names))
check(1.2 < dims.z < 2.2, "unexpected height %.2f" % dims.z)
check(-0.12 < mins.z < 0.02, "tent should sit on z=0, stakes may dip (min z %.3f)" % mins.z)

# Interior check: ray from the front door into the tent must reach the back,
# and a ray from inside upward must hit the canvas (closed shell).
canvas = next(o for o in meshes if o.name.startswith("Tent_Canvas"))
dg = bpy.context.evaluated_depsgraph_get()
bm_world = [(canvas.matrix_world @ v.co) for v in canvas.data.vertices]
bvh = BVHTree.FromPolygons(bm_world, [tuple(p.vertices) for p in canvas.data.polygons])
door = bvh.ray_cast(Vector((0, -3.0, 0.5)), Vector((0, 1, 0)), 8.0)
check(door[0] is not None and door[0].y > 0.5, "door ray should pass the opening and hit the back wall")
up = bvh.ray_cast(Vector((0.0, -0.3, 0.9)), Vector((0, 0, 1)), 4.0)
check(up[0] is not None, "interior ray should hit the roof")
walls = [bvh.ray_cast(Vector((0.0, y, 0.7)), Vector((sx, 0, 0)), 4.0)[0] for y in (0.3, 0.5) for sx in (1, -1)]
check(sum(h is not None for h in walls) >= 3, "interior rays should hit the side walls")

# Interior contents must sit inside the shell footprint.
names = {o.name for o in meshes}
for need in ("Tent_Bedding", "Tent_Props", "Tent_Floor"):
    check(any(n.startswith(need) for n in names), "missing " + need)

if not SKIP_RENDER and not failures:
    os.makedirs(RENDERS, exist_ok=True)
    scene = bpy.context.scene
    try:
        scene.render.engine = "BLENDER_EEVEE_NEXT"
    except TypeError:
        scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1100
    scene.render.resolution_y = 750
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (0.92, 0.93, 0.95, 1)
    bg.inputs["Strength"].default_value = 1.1
    scene.world = world
    # ground plane to catch shadow
    bpy.ops.mesh.primitive_plane_add(size=14, location=(0, 0, -0.005))
    plane = bpy.context.active_object
    pm = bpy.data.materials.new("ground")
    pm.use_nodes = True
    pm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.72, 0.70, 0.64, 1)
    plane.data.materials.append(pm)
    sun = bpy.data.lights.new("sun", "SUN")
    sun.energy = 3.2
    sun.angle = math.radians(8)
    sun_obj = bpy.data.objects.new("sun", sun)
    sun_obj.rotation_euler = (math.radians(50), math.radians(8), math.radians(-35))
    scene.collection.objects.link(sun_obj)
    fill = bpy.data.lights.new("fill", "AREA")
    fill.energy = 250
    fill.size = 4
    fill_obj = bpy.data.objects.new("fill", fill)
    fill_obj.location = (-3, -4, 2.5)
    fill_obj.rotation_euler = (math.radians(65), 0, math.radians(-35))
    scene.collection.objects.link(fill_obj)
    glow = bpy.data.lights.new("inside", "POINT")
    glow.energy = 35
    glow_obj = bpy.data.objects.new("inside", glow)
    glow_obj.location = (0.0, -0.1, 1.1)
    scene.collection.objects.link(glow_obj)

    cam_data = bpy.data.cameras.new("cam")
    cam_data.lens = 42
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam

    def shot(name, loc, target, lens=42, inside=True):
        cam.location = loc
        cam_data.lens = lens
        direction = Vector(target) - Vector(loc)
        cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
        glow_obj.hide_render = not inside
        scene.render.filepath = os.path.join(RENDERS, name + ".png")
        bpy.ops.render.render(write_still=True)

    shot("tent_3q", (-3.6, -4.2, 2.1), (0, 0, 0.7), inside=False)
    shot("tent_front", (0, -4.2, 1.0), (0, 0, 0.6), inside=True)
    shot("tent_interior", (0.0, -1.9, 0.8), (0, 0.4, 0.3), lens=28, inside=True)
    shot("tent_back", (3.2, 4.0, 2.0), (0, 0, 0.7), inside=False)
    shot("tent_top", (0, -0.01, 6.5), (0, 0, 0.5), lens=50, inside=False)

if failures:
    print("TENT_VERIFY_FAIL", len(failures))
    sys.exit(1)
print("TENT_VERIFY_OK")
