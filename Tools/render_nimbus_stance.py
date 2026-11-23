"""Render Goku's exported nimbus_stance clip standing on his cloud.

Game proportions: Goku 2.84 m tall, cloud 3.5 m long. The cloud flies
broadside; Goku faces the travel direction (-Y here) and stands over the
middle of the cloud body. The game bends each leg so the soles sit on the
cloud; this preview only lowers the body to the average cloud height under
the feet, so a foot may show a few centimetres off.

Usage: blender -b --factory-startup -P Tools/render_nimbus_stance.py
Writes Logs/agent/nimbus/*.png
"""
import math
from pathlib import Path

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree
import bmesh

ROOT = Path(__file__).resolve().parents[1]
GOKU = ROOT / "GymChaos/Assets/Resources/Characters/Enemies/goku_authored.fbx"
CLOUD = ROOT / "GymChaos/Assets/Resources/Vehicles/goku_vehicle.fbx"
OUT = ROOT / "Logs/agent/nimbus"
GOKU_HEIGHT = 2.30 * 1.075 * 1.15
CLOUD_LENGTH = 2.8 * 1.25


def bounds(objects):
    pts = [o.matrix_world @ Vector(c) for o in objects for c in o.bound_box]
    return (Vector([min(p[i] for p in pts) for i in range(3)]),
            Vector([max(p[i] for p in pts) for i in range(3)]))


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(CLOUD))
    cloud = [o for o in bpy.data.objects if o.type == "MESH"]
    root = bpy.data.objects.new("cloud_root", None)
    bpy.context.scene.collection.objects.link(root)
    for o in bpy.data.objects:
        if o.parent is None and o is not root:
            o.parent = root
    lo, hi = bounds(cloud)
    root.scale = (CLOUD_LENGTH / (hi.x - lo.x),) * 3
    bpy.context.view_layer.update()
    lo, hi = bounds(cloud)
    root.location = -Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
    bpy.context.view_layer.update()
    lo, hi = bounds(cloud)
    bm = bmesh.new()
    for o in cloud:
        tmp = o.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh()
        tmp.transform(o.matrix_world)
        bm.from_mesh(tmp)
    bvh = BVHTree.FromBMesh(bm)

    def top(x, y):
        hit = bvh.ray_cast(Vector((x, y, 10)), Vector((0, 0, -1)))
        return hit[0].z if hit[0] else float("nan")

    half = (hi.x - lo.x) / 2
    # Body (puffy) end is the higher one.
    body_side = 1.0 if top(half * 0.5, 0) > top(-half * 0.5, 0) else -1.0
    stand_x = body_side * half * 0.45

    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(GOKU), use_anim=True, automatic_bone_orientation=False)
    goku = [o for o in bpy.data.objects if o not in before]
    arm = next(o for o in goku if o.type == "ARMATURE")
    meshes = [o for o in goku if o.type == "MESH"]
    import os
    clip = os.environ.get("NIMBUS_CLIP", "nimbus_stance")
    action = next(a for a in bpy.data.actions if a.name.endswith(clip))
    arm.animation_data_create()
    arm.animation_data.action = action
    if getattr(action, "slots", None):
        arm.animation_data.action_slot = action.slots[0]
    scene = bpy.context.scene
    scene.frame_set(0)
    scene.frame_set(15)
    bpy.context.view_layer.update()

    def posed_points():
        graph = bpy.context.evaluated_depsgraph_get()
        pts = []
        for o in meshes:
            ev = o.evaluated_get(graph)
            m = ev.to_mesh()
            pts += [ev.matrix_world @ v.co for v in m.vertices]
            ev.to_mesh_clear()
        return pts

    pts = posed_points()
    height = max(p.z for p in pts) - min(p.z for p in pts)
    rest_height = 1.083  # authored rest height of goku_authored
    # The clip animates the armature object itself: move a parent instead.
    holder = bpy.data.objects.new("goku_holder", None)
    scene.collection.objects.link(holder)
    arm.parent = holder
    holder.scale = (GOKU_HEIGHT / rest_height,) * 3
    bpy.context.view_layer.update()
    pts = posed_points()
    sole = min(p.z for p in pts)
    cx = sum(p.x for p in pts) / len(pts)
    cy = sum(p.y for p in pts) / len(pts)
    feet_x = [stand_x - 0.56, stand_x + 0.56]
    surface = sum(top(x, 0.0) for x in feet_x) / 2
    holder.location += Vector((stand_x - cx, -cy, surface - sole))
    bpy.context.view_layer.update()
    print(f"NIMBUS_DEBUG posedHeight={height:.3f} scaledTo={GOKU_HEIGHT:.2f}")

    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = 900
    scene.render.resolution_y = 1100
    world = bpy.data.worlds.new("w")
    world.color = (0.92, 0.92, 0.92)
    scene.world = world
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(45), 0, math.radians(-30))
    scene.collection.objects.link(sun)
    for o in cloud:
        mat = bpy.data.materials.new("cloud")
        mat.diffuse_color = (0.93, 0.8, 0.55, 1)
        o.data.materials.clear()
        o.data.materials.append(mat)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.lens = 45
    target = Vector((stand_x * 0.6, 0, 1.4))
    views = {"front": Vector((stand_x - 2.5, -7.5, 2.6)), "side": Vector((-9.0, -1.0, 2.0)),
             "back": Vector((stand_x + 2.0, 7.5, 2.4))}
    for name, pos in views.items():
        cam.location = pos
        cam.rotation_euler = (target - pos).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = str(OUT / f"nimbus_{name}.png")
        bpy.ops.render.render(write_still=True)
    print(f"GYMCHAOS_NIMBUS_RENDER_OK bodySide={body_side} standX={stand_x:.2f} surface={surface:.2f} out={OUT}")


if __name__ == "__main__":
    main()
