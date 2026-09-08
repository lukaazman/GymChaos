"""Read-only structural audit for authored GymChaos Blender rigs."""

from __future__ import annotations

import sys
from pathlib import Path

import bpy

sys.path.insert(0, str(Path(__file__).resolve().parent))
import export_authored_character_fbx as exporter


for character, config in exporter.CHARACTERS.items():
    bpy.ops.wm.open_mainfile(filepath=str(config["blend"]))
    armature = exporter.find_armature()
    mesh = exporter.find_mesh(armature)
    deform_names = {bone.name for bone in armature.data.bones if bone.use_deform}
    unweighted = 0
    non_normalized = 0
    for vertex in mesh.data.vertices:
        influences = [item for item in vertex.groups if item.weight > 1e-6]
        total = sum(item.weight for item in influences)
        if not influences:
            unweighted += 1
        elif abs(total - 1.0) > 0.001:
            non_normalized += 1
    modifiers = [
        (modifier.type, modifier.object.name if getattr(modifier, "object", None) else None)
        for modifier in mesh.modifiers
    ]
    print(
        "GYMCHAOS_AUTHORED_RIG_AUDIT "
        f"character={character} armature={armature.name} "
        f"bones={len(armature.data.bones)} deform={len(deform_names)} "
        f"mesh={mesh.name} vertices={len(mesh.data.vertices)} "
        f"unweighted={unweighted} nonNormalized={non_normalized} "
        f"meshParent={mesh.parent.name if mesh.parent else 'none'} "
        f"armatureScale={tuple(round(value, 6) for value in armature.scale)} "
        f"meshScale={tuple(round(value, 6) for value in mesh.scale)} "
        f"modifiers={modifiers}"
    )
