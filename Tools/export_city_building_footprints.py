"""Export per-building footprints of the city backdrop for the race minigame.

The runtime city (`city_dystopia_backdrop.glb`) is one joined mesh, so Unity
cannot hide a single tower.  This script reads the same source objects that
`Assets/CityRecreation/make_city_dystopia_backdrop.py` joins and writes one
axis-aligned footprint per building assembly.  The race carver assigns each
runtime triangle to the footprint that contains its centroid and drops the
triangles of buildings that stand on the race corridor.

Coordinates are written in the runtime mesh space of the GLB loader:
Blender (x, y, z) -> glTF (x, z, -y) -> Unity mesh (-x, z, -y).

Usage:
    blender -b Assets/city_dystopia.blend -P Tools/export_city_building_footprints.py
"""

from __future__ import annotations

import json
import os
import sys

import bpy

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
OUTPUT = os.path.join(
    REPO, "GymChaos", "Assets", "Resources", "Race", "city_dystopia_buildings.json")

# One joined window mesh spans every facade; its triangles are assigned to
# buildings by centroid at runtime, so it gets no footprint of its own.
SHARED_GROUPS = {"Facade_Window_Module_Grid"}

sys.path.insert(0, os.path.join(REPO, "Assets", "CityRecreation"))
from make_city_dystopia_backdrop import should_export  # noqa: E402


def group_key(obj):
    """Top-most ancestor name: one Building_NNN_Assembly per building."""
    root = obj
    while root.parent is not None:
        root = root.parent
    return root.name


def main():
    depsgraph = bpy.context.evaluated_depsgraph_get()
    groups = {}
    for obj in bpy.data.objects:
        if not should_export(obj):
            continue
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        if mesh is None or len(mesh.vertices) == 0:
            evaluated.to_mesh_clear()
            continue
        matrix = obj.matrix_world
        box = groups.setdefault(group_key(obj), [1e9, 1e9, 1e9, -1e9, -1e9, -1e9, 0])
        for vertex in mesh.vertices:
            world = matrix @ vertex.co
            # Unity mesh space of the runtime loader.
            point = (-world.x, world.z, -world.y)
            for axis in range(3):
                box[axis] = min(box[axis], point[axis])
                box[axis + 3] = max(box[axis + 3], point[axis])
        box[6] += len(mesh.polygons)
        evaluated.to_mesh_clear()

    buildings = []
    for name in sorted(groups):
        if name in SHARED_GROUPS:
            continue
        box = groups[name]
        buildings.append({
            "name": name,
            "min": [round(box[0], 3), round(box[1], 3), round(box[2], 3)],
            "max": [round(box[3], 3), round(box[4], 3), round(box[5], 3)],
            "polygons": box[6],
        })

    os.makedirs(os.path.dirname(OUTPUT), exist_ok=True)
    with open(OUTPUT, "w", encoding="utf-8") as handle:
        json.dump({"space": "unity_mesh", "buildings": buildings}, handle, indent=1)
    print("CITY_BUILDING_FOOTPRINTS_OK", len(buildings), OUTPUT)


if __name__ == "__main__":
    main()
