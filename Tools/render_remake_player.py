"""Review sheet for the remade player: body shape keys and every hair and
facial hair option, in an A-pose, front 3/4.

    blender -b -noaudio --python-exit-code 1 -P Tools/render_remake_player.py

Writes Logs/agent/customize/player_sheet.png and prints REMAKE_PLAYER_SHEET_OK.
"""

import math
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Logs/agent/customize"
SKIN = {"light": (0.95, 0.80, 0.69), "mid": (0.78, 0.56, 0.42), "dark": (0.42, 0.27, 0.19)}
TILES = [  # label, keys, visible parts, skin, hair colour
    ("base", {}, ("Tank", "Shorts", "Hair_2"), "mid", (0.10, 0.07, 0.05)),
    ("bodybuilding", {"Upper": 0.77, "Lower": 0.77}, ("Tank", "Shorts", "Hair_1"), "light", (0.35, 0.22, 0.12)),
    ("strongman", {"Upper": 1, "Lower": 1, "Soft": 1}, ("Tank", "Shorts", "Hair_3", "Beard_3"), "dark",
     (0.06, 0.05, 0.05)),
    ("female", {"Female": 1}, ("Tank", "Shorts", "Hair_5"), "mid", (0.55, 0.36, 0.18)),
    ("female bulk", {"Female": 1, "Upper": 0.77, "Lower": 0.77}, ("Tank", "Shorts", "Hair_4"), "light",
     (0.80, 0.66, 0.40)),
    ("cardio", {}, ("Tank", "Shorts", "Hair_4", "Beard_2"), "dark", (0.12, 0.08, 0.06)),
    # The OG locker tops and pants, authored parts wearing the remake atlases.
    ("onyx", {"Upper": 0.77, "Lower": 0.77}, ("Onyx", "Pants", "Hair_2"), "mid", (0.10, 0.07, 0.05)),
    ("oversized", {"Female": 1}, ("Oversized", "Shorts", "Hair_5"), "light", (0.10, 0.07, 0.05)),
    ("oversized male", {"Upper": 0.77, "Lower": 0.77}, ("Oversized", "Pants", "Hair_1"), "dark", (0.10, 0.07, 0.05)),
]
# --close: torso close-ups (straps, neckline, waist) of the tank tiles.
CLOSE = "--close" in sys.argv
if CLOSE:  # base, bodybuilding, female: front and side
    TILES = [TILES[1], TILES[3], TILES[6]]
    TILES = [t + (yaw,) for t in TILES for yaw in (-8, -60, 172)]
ATLAS = ROOT / "GymChaos/Assets/Resources/Player/Outfits/Remake"


def srgb(c):
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


def tint(material, colour):
    bsdf = next(n for n in material.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    tex = next((n for n in material.node_tree.nodes if n.type == "TEX_IMAGE"), None)
    if tex is None:
        bsdf.inputs["Base Color"].default_value = (*srgb(colour), 1)
        return
    mix = material.node_tree.nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    mix.blend_type = "MULTIPLY"
    mix.inputs["Factor"].default_value = 1.0
    material.node_tree.links.new(tex.outputs["Color"], mix.inputs["A"])
    mix.inputs["B"].default_value = (*srgb(colour), 1)
    material.node_tree.links.new(mix.outputs["Result"], bsdf.inputs["Base Color"])


def render_tile(label, keys, visible, skin, hair, path, view_yaw=None):
    bpy.ops.wm.open_mainfile(filepath=str(ROOT / "Assets/BodyBuilders/remake/player.blend"))
    scene = bpy.context.scene
    meshes = [o for o in scene.objects if o.type == "MESH"]
    rig = next(o for o in scene.objects if o.type == "ARMATURE")
    for obj in meshes:
        obj.hide_render = obj.name != "Body" and obj.name not in visible
        for block in obj.data.shape_keys.key_blocks[1:]:
            block.value = keys.get(block.name, 0.0)
    body = scene.objects["Body"]
    for part, atlas in (("Onyx", "onyx_red"), ("Oversized", "oversized_blue")):
        if part in visible:
            material = scene.objects[part].data.materials[0]
            tex = material.node_tree.nodes.new("ShaderNodeTexImage")
            tex.image = bpy.data.images.load(str(ATLAS / f"{atlas}.png"))
            bsdf = next(n for n in material.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
            material.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    tint(body.data.materials["Skin"], SKIN[skin])
    for obj in meshes:
        if obj.name in visible:
            if obj.name in ("Onyx", "Oversized"):
                continue
            colour = hair if obj.name.startswith(("Hair", "Beard")) else (
                (0.86, 0.86, 0.84) if obj.name == "Tank" else (0.08, 0.08, 0.09))
            tint(obj.data.materials[0], colour)
    for pb in rig.pose.bones:
        for c in list(pb.constraints):
            pb.constraints.remove(c)
    bpy.context.view_layer.update()
    for side, sign in (("L", 1), ("R", -1)):
        pb = rig.pose.bones[f"DEF-upper_arm.{side}"]
        head = pb.head.copy()
        pb.matrix = (Matrix.Translation(head) @ Matrix.Rotation(math.radians(68 * sign), 4, "Y")
                     @ Matrix.Translation(-head)) @ pb.matrix
        bpy.context.view_layer.update()
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = 1.3 if CLOSE else 2.1
    scene.collection.objects.link(cam)
    centre = Vector((0, 0, 1.0 if CLOSE else 0.92))
    yaw = math.radians(view_yaw if view_yaw is not None else -30)
    cam.location = centre + Vector((math.sin(yaw) * -6, -math.cos(yaw) * 6, 0.25))
    cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    for rot, energy in (((0.87, 0, -0.6), 5.0), ((1.2, 0, 2.4), 2.2)):
        light = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
        light.data.energy = energy
        light.rotation_euler = rot
        scene.collection.objects.link(light)
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.03, 0.04, 0.07, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 1.0
    scene.world = world
    scene.render.engine = "BLENDER_EEVEE"
    scene.view_settings.view_transform = "Standard"
    scene.render.resolution_x, scene.render.resolution_y = (420, 620) if CLOSE else (420, 760)
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    paths = []
    for i, tile in enumerate(TILES):
        path = OUT / f"_tile{i}.png"
        render_tile(*tile[:5], path, *tile[5:])
        paths.append(path)
    images = [bpy.data.images.load(str(p)) for p in paths]
    w, h = images[0].size
    sheet = np.zeros((h, w * len(images), 4), np.float32)
    for i, image in enumerate(images):
        sheet[:, i * w:(i + 1) * w] = np.array(image.pixels[:], np.float32).reshape(h, w, 4)
    out = bpy.data.images.new("sheet", w * len(images), h, alpha=True)
    out.pixels[:] = sheet.ravel()
    out.filepath_raw = str(OUT / ("player_close.png" if CLOSE else "player_sheet.png"))
    out.file_format = "PNG"
    out.save()
    for p in paths:
        p.unlink()
    print("REMAKE_PLAYER_SHEET_OK", OUT / "player_sheet.png")


main()
