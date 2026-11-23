"""Render one review sheet for an exported character FBX.

Front, side, back and 3/4 views in the idle pose, then one tile per gameplay
pose the FBX has a clip for, all with the real texture on a neutral
background, stitched into one PNG under Logs/agent/remake/.

    blender -b -noaudio --python-exit-code 1 -P Tools/render_remake_sheet.py -- <character.fbx> [--texture <png>]

Prints REMAKE_SHEET_OK <name> tiles=<n> path=<png>.
"""

from __future__ import annotations

import argparse
import math
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector

PROJECT_ROOT = Path(__file__).resolve().parents[1]
TEXTURES = PROJECT_ROOT / "GymChaos/Assets/Resources/Characters/Textures"
OUTPUT = PROJECT_ROOT / "Logs/agent/remake"
TILE_W, TILE_H, COLUMNS = 360, 480, 4
VIEWS = (("front", 0.0), ("side", 90.0), ("back", 180.0), ("three_quarter", 35.0))
POSES = (("idle1", 0.5), ("walking", 0.25), ("running", 0.25), ("punch_combo", 0.4),
         ("squat", 0.5), ("deadlift", 0.5), ("vehicle_driving", 0.5), ("sitting_idle", 0.5),
         ("gun_shooting", 0.5), ("nimbus_stance", 0.5), ("magic_sign", 0.5), ("sleeping", 0.5))


def find_action(name):
    name = name.lower()
    return next((a for a in bpy.data.actions
                 if a.name.lower() == name or a.name.lower().endswith(("|" + name, "_" + name))), None)


def set_pose(armature, action, normalized):
    armature.animation_data_create()
    armature.animation_data.action = action
    if hasattr(armature.animation_data, "action_slot") and action.slots:
        armature.animation_data.action_slot = action.slots[0]
    start, end = action.frame_range
    bpy.context.scene.frame_set(int(round(start + (end - start) * normalized)))
    bpy.context.view_layer.update()


def bounds(meshes):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    points = []
    for mesh in meshes:
        evaluated = mesh.evaluated_get(depsgraph)
        data = evaluated.to_mesh()
        points += [evaluated.matrix_world @ v.co for v in data.vertices]
        evaluated.to_mesh_clear()
    low = Vector(tuple(min(p[i] for p in points) for i in range(3)))
    high = Vector(tuple(max(p[i] for p in points) for i in range(3)))
    return low, high


def apply_texture(meshes, texture: Path):
    image = bpy.data.images.load(str(texture))
    for mesh in meshes:
        for slot in mesh.material_slots:
            material = slot.material
            if material is None:
                continue
            material.use_nodes = True
            nodes = material.node_tree.nodes
            shader = next((n for n in nodes if n.type == "BSDF_PRINCIPLED"), None)
            if shader is None:
                continue
            # The game binds the texture at runtime; the FBX path may not resolve.
            node = next((n for n in nodes if n.type == "TEX_IMAGE"), None) or nodes.new("ShaderNodeTexImage")
            node.image = image
            material.node_tree.links.new(node.outputs["Color"], shader.inputs["Base Color"])
            normal = texture.with_name(texture.stem + "_n.png")
            if normal.exists():
                bump = nodes.new("ShaderNodeTexImage")
                bump.image = bpy.data.images.load(str(normal))
                bump.image.colorspace_settings.name = "Non-Color"
                normal_map = nodes.new("ShaderNodeNormalMap")
                material.node_tree.links.new(bump.outputs["Color"], normal_map.inputs["Color"])
                material.node_tree.links.new(normal_map.outputs["Normal"], shader.inputs["Normal"])


def toon(meshes):
    outline = bpy.data.materials.new("outline")
    outline.use_nodes = True
    outline.use_backface_culling = True
    o_nodes = outline.node_tree.nodes
    o_nodes.clear()
    black = o_nodes.new("ShaderNodeEmission")
    black.inputs["Color"].default_value = (0.02, 0.02, 0.03, 1.0)
    o_out = o_nodes.new("ShaderNodeOutputMaterial")
    outline.node_tree.links.new(black.outputs["Emission"], o_out.inputs["Surface"])
    for mesh in meshes:
        for slot in mesh.material_slots:
            mat = slot.material
            nodes, links = mat.node_tree.nodes, mat.node_tree.links
            base = next(n for n in nodes if n.type == "TEX_IMAGE" and n.image and not n.image.name.endswith("_n.png"))
            out = next(n for n in nodes if n.type == "OUTPUT_MATERIAL")
            diffuse = nodes.new("ShaderNodeBsdfDiffuse")
            to_rgb = nodes.new("ShaderNodeShaderToRGB")
            ramp = nodes.new("ShaderNodeValToRGB")
            ramp.color_ramp.interpolation = "CONSTANT"
            ramp.color_ramp.elements[0].color = (0.62, 0.6, 0.66, 1.0)
            ramp.color_ramp.elements[1].position = 0.35
            ramp.color_ramp.elements[1].color = (1.0, 1.0, 1.0, 1.0)
            multiply = nodes.new("ShaderNodeMix")
            multiply.data_type = "RGBA"
            multiply.blend_type = "MULTIPLY"
            multiply.inputs["Factor"].default_value = 1.0
            emit = nodes.new("ShaderNodeEmission")
            links.new(diffuse.outputs["BSDF"], to_rgb.inputs["Shader"])
            links.new(to_rgb.outputs["Color"], ramp.inputs["Fac"])
            links.new(base.outputs["Color"], multiply.inputs[6])
            links.new(ramp.outputs["Color"], multiply.inputs[7])
            links.new(multiply.outputs[2], emit.inputs["Color"])
            links.new(emit.outputs["Emission"], out.inputs["Surface"])
        mesh.data.materials.append(outline)
        hull = mesh.modifiers.new("Outline", "SOLIDIFY")
        hull.thickness = 0.006
        hull.offset = 1.0
        hull.use_flip_normals = True
        hull.use_rim = False
        hull.material_offset = len(mesh.data.materials) - 1


def setup_scene():
    scene = bpy.context.scene
    engines = {item.identifier for item in
               bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items}
    scene.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in engines else "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = TILE_W, TILE_H
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.world = scene.world or bpy.data.worlds.new("Sheet World")
    scene.world.use_nodes = True
    background = scene.world.node_tree.nodes.get("Background")
    background.inputs["Color"].default_value = (0.16, 0.16, 0.17, 1.0)
    background.inputs["Strength"].default_value = 0.9
    camera = bpy.data.objects.new("Sheet Camera", bpy.data.cameras.new("Sheet Camera"))
    camera.data.type = "ORTHO"
    scene.collection.objects.link(camera)
    scene.camera = camera
    lights = []
    for name, energy, size in (("Key", 700.0, 3.0), ("Fill", 300.0, 5.0), ("Rim", 400.0, 2.0)):
        light = bpy.data.objects.new(name, bpy.data.lights.new(name, "AREA"))
        light.data.energy, light.data.size = energy, size
        scene.collection.objects.link(light)
        lights.append(light)
    return scene, camera, lights


def aim(obj, target):
    obj.rotation_euler = (target - obj.location).to_track_quat("-Z", "Y").to_euler()


def render_tile(scene, camera, lights, meshes, yaw_degrees, path: Path):
    low, high = bounds(meshes)
    center, size = (low + high) * 0.5, high - low
    distance = max(size.x, size.y, size.z) * 3.0
    yaw = math.radians(yaw_degrees)
    # The character faces -Y: yaw 0 looks at its front.
    direction = Vector((math.sin(yaw), -math.cos(yaw), 0.0))
    camera.location = center + direction * distance + Vector((0.0, 0.0, size.z * 0.05))
    aim(camera, center)
    camera.data.ortho_scale = max(size.z, size.x, size.y) * 1.15
    side = Vector((-direction.y, direction.x, 0.0))
    offsets = (direction * 3 - side * 2 + Vector((0, 0, 3)),
               direction * 2 + side * 3 + Vector((0, 0, 1)),
               direction * -3 + Vector((0, 0, 2.5)))
    for light, offset in zip(lights, offsets):
        light.location = center + offset
        aim(light, center)
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


def stitch(tiles, output: Path):
    rows = math.ceil(len(tiles) / COLUMNS)
    sheet = np.full((rows * TILE_H, COLUMNS * TILE_W, 4), 0.12, dtype=np.float32)
    sheet[..., 3] = 1.0
    for index, tile in enumerate(tiles):
        image = bpy.data.images.load(str(tile))
        pixels = np.empty(TILE_W * TILE_H * 4, dtype=np.float32)
        image.pixels.foreach_get(pixels)
        row = rows - 1 - index // COLUMNS  # Blender images start at the bottom row.
        column = index % COLUMNS
        sheet[row * TILE_H:(row + 1) * TILE_H, column * TILE_W:(column + 1) * TILE_W] = \
            pixels.reshape(TILE_H, TILE_W, 4)
        bpy.data.images.remove(image)
    result = bpy.data.images.new("sheet", COLUMNS * TILE_W, rows * TILE_H, alpha=True)
    result.pixels.foreach_set(sheet.ravel())
    result.filepath_raw = str(output)
    result.file_format = "PNG"
    result.save()


def main() -> int:
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("fbx")
    parser.add_argument("--texture")
    # Anime characters: two-tone cel shading and an inverted-hull outline,
    # a preview of the toon material they get in the game.
    parser.add_argument("--toon", action="store_true")
    args = parser.parse_args(argv)
    fbx = Path(args.fbx).resolve()
    name = fbx.stem

    bpy.ops.wm.read_homefile(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(fbx), use_anim=True, automatic_bone_orientation=False)
    armature = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    meshes = [o for o in bpy.context.scene.objects
              if o.type == "MESH" and not o.name.endswith("_LOD1")]
    texture = Path(args.texture).resolve() if args.texture else TEXTURES / (name.removesuffix("_authored") + ".png")
    if texture.exists():
        apply_texture(meshes, texture)
    if args.toon:
        toon(meshes)

    scene, camera, lights = setup_scene()
    work = OUTPUT / name
    work.mkdir(parents=True, exist_ok=True)
    tiles = []
    idle = find_action("idle1")
    if idle:
        set_pose(armature, idle, 0.5)
    for view, yaw in VIEWS:
        tiles.append(work / f"view_{view}.png")
        render_tile(scene, camera, lights, meshes, yaw, tiles[-1])
    for clip, normalized in POSES:
        action = find_action(clip)
        if action is None:
            continue
        set_pose(armature, action, normalized)
        tiles.append(work / f"pose_{clip}.png")
        render_tile(scene, camera, lights, meshes, 30.0, tiles[-1])

    output = OUTPUT / f"{name}_sheet.png"
    stitch(tiles, output)
    print(f"REMAKE_SHEET_OK {name} tiles={len(tiles)} path={output} "
          f"texture={'yes' if texture.exists() else 'no'} order={[t.stem for t in tiles]}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
