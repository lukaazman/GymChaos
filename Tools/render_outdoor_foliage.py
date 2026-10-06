"""Renders the exported foliage set side by side for a visual check.

    blender -b -P Tools/render_outdoor_foliage.py -- <output.png>
"""
import math
import os
import sys

import bpy
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
FOLIAGE = os.path.join(ROOT, "GymChaos", "Assets", "StreamingAssets",
                       "BodyBuilders", "outside", "foliage")
OUTPUT = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else os.path.join(
    ROOT, "Logs", "agent", "foliage_render.png")
# name, uniform scale, x position
LAYOUT = [("bush1_lod", 1.8, -9), ("bush2_lod", 2.0, -6), ("bush3_custom", 1.3, -3),
          ("tree1_lod", 5.5, 1), ("tree2_lod", 6.5, 5), ("tree3_custom", 2.6, 9)]

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
for name, scale, x in LAYOUT:
    before = set(scene.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(FOLIAGE, name + ".glb"))
    for obj in set(scene.objects) - before:
        if obj.parent is None:
            obj.scale = (scale, scale, scale)
            obj.location = (x, 0, 0)
bpy.ops.mesh.primitive_plane_add(size=40)
ground = bpy.context.active_object
mat = bpy.data.materials.new("ground")
mat.use_nodes = True
mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.05, 0.06, 0.08, 1)
ground.data.materials.append(mat)
bpy.ops.object.light_add(type="SUN", rotation=(math.radians(50), 0, math.radians(30)))
bpy.context.active_object.data.energy = 4
world = bpy.data.worlds.new("w")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.35, 0.5, 0.75, 1)
world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.8
scene.world = world
bpy.ops.object.camera_add(location=(0, -22, 6))
cam = bpy.context.active_object
cam.rotation_euler = (Vector((0, 0, 2.5)) - cam.location).to_track_quat("-Z", "Y").to_euler()
cam.data.lens = 32
scene.camera = cam
scene.render.engine = "BLENDER_EEVEE_NEXT"
scene.render.resolution_x, scene.render.resolution_y = 1400, 600
scene.render.filepath = OUTPUT
bpy.ops.render.render(write_still=True)
print("FOLIAGE_RENDER", OUTPUT)
