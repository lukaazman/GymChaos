"""Export equipment material maps from Blender without changing model geometry.

Run with Blender in background mode and pass the source .blend files after ``--``.
The generated files are loaded by EquipmentMaterialRestorer in Unity.
"""

from __future__ import annotations

from array import array
import json
from pathlib import Path
import re
import sys

import bpy


PROJECT_ROOT = Path(__file__).resolve().parents[1]
OUTPUT_ROOT = PROJECT_ROOT / "GymChaos" / "Assets" / "Resources" / "EquipmentMaterials"
MANIFEST_PATH = OUTPUT_ROOT / "equipment-materials.json"
MAX_TEXTURE_SIZE = 1024


def normalize_material_name(name: str) -> str:
    normalized = re.sub(r"\s*\(Instance\)\s*$", "", name, flags=re.IGNORECASE)
    normalized = re.sub(r"\.\d{3}$", "", normalized)
    return " ".join(normalized.strip().lower().split())


def safe_name(name: str) -> str:
    value = re.sub(r"[^a-z0-9]+", "-", normalize_material_name(name)).strip("-")
    return value or "material"


def principled_node(material):
    if material is None or not material.use_nodes or material.node_tree is None:
        return None
    nodes = [node for node in material.node_tree.nodes if node.type == "BSDF_PRINCIPLED"]
    return nodes[0] if nodes else None


def socket(node, *names):
    if node is None:
        return None
    for name in names:
        candidate = node.inputs.get(name)
        if candidate is not None:
            return candidate
    return None


def linked_image(input_socket, visited=None, inverted=False):
    if input_socket is None or not input_socket.is_linked:
        return None, inverted

    visited = visited or set()
    link = input_socket.links[0]
    node = link.from_node
    if node is None or node.as_pointer() in visited:
        return None, inverted
    visited.add(node.as_pointer())

    if node.type == "TEX_IMAGE" and node.image is not None:
        return node.image, inverted

    if node.type == "INVERT":
        return linked_image(socket(node, "Color"), visited, not inverted)

    preferred_inputs = {
        "NORMAL_MAP": ("Color",),
        "BUMP": ("Height", "Normal"),
        "RGBTOBW": ("Color",),
        "VALTORGB": ("Fac",),
        "GAMMA": ("Color",),
        "HUE_SAT": ("Color",),
        "MIX_RGB": ("Color1", "Color2", "Fac"),
        "MIX": ("A", "B", "Factor"),
        "MATH": ("Value", "Value_001"),
        "MAP_RANGE": ("Value",),
    }
    names = preferred_inputs.get(node.type, tuple(item.name for item in node.inputs))
    for name in names:
        candidate = node.inputs.get(name)
        image, child_inverted = linked_image(candidate, visited, inverted)
        if image is not None:
            return image, child_inverted
    return None, inverted


def fallback_image(material, role: str):
    if material is None or material.node_tree is None:
        return None
    patterns = {
        "albedo": ("basecolor", "base_color", "base color", "diffuse", "albedo", "color"),
        "normal": ("normal", "nor_gl"),
        "metallic": ("metallic", "metalness", "metal"),
        "roughness": ("roughness", "rough"),
        "glossiness": ("glossiness", "gloss", "smoothness"),
    }
    for node in material.node_tree.nodes:
        if node.type != "TEX_IMAGE" or node.image is None:
            continue
        name = node.image.name.lower()
        if any(token in name for token in patterns[role]):
            return node.image
    return None


def image_for_role(material, role: str):
    node = principled_node(material)
    target_socket = {
        "albedo": socket(node, "Base Color"),
        "normal": socket(node, "Normal"),
        "metallic": socket(node, "Metallic"),
        "roughness": socket(node, "Roughness"),
    }[role]
    image, inverted = linked_image(target_socket)
    if image is not None:
        return image, inverted

    image = fallback_image(material, role)
    if image is not None:
        return image, False
    if role == "roughness":
        image = fallback_image(material, "glossiness")
        if image is not None:
            return image, True
    return None, False


def socket_default(material, name: str, fallback):
    target = socket(principled_node(material), name)
    if target is None:
        return fallback
    value = target.default_value
    if hasattr(value, "__len__"):
        return [float(component) for component in value]
    return float(value)


def capped_dimensions(image):
    width = max(1, int(image.size[0]))
    height = max(1, int(image.size[1]))
    scale = min(1.0, MAX_TEXTURE_SIZE / max(width, height))
    return max(1, round(width * scale)), max(1, round(height * scale))


def scaled_copy(image, width=None, height=None):
    duplicate = image.copy()
    target_width, target_height = (width, height) if width and height else capped_dimensions(image)
    if duplicate.size[0] != target_width or duplicate.size[1] != target_height:
        duplicate.scale(target_width, target_height)
    return duplicate


def save_image(image, path: Path):
    path.parent.mkdir(parents=True, exist_ok=True)
    duplicate = scaled_copy(image)
    duplicate.filepath_raw = str(path)
    duplicate.file_format = "PNG"
    duplicate.save()
    bpy.data.images.remove(duplicate)


def pixels(image, width, height):
    duplicate = scaled_copy(image, width, height)
    values = array("f", [0.0]) * (width * height * 4)
    duplicate.pixels.foreach_get(values)
    bpy.data.images.remove(duplicate)
    return values


def save_metallic_smoothness(material, metallic_image, roughness_image, roughness_inverted, path: Path):
    source = metallic_image or roughness_image
    if source is None:
        return False

    width, height = capped_dimensions(source)
    metallic_values = pixels(metallic_image, width, height) if metallic_image is not None else None
    roughness_values = pixels(roughness_image, width, height) if roughness_image is not None else None
    metallic_scalar = float(socket_default(material, "Metallic", 0.0))
    roughness_scalar = float(socket_default(material, "Roughness", 0.5))

    packed = array("f", [0.0]) * (width * height * 4)
    for offset in range(0, len(packed), 4):
        metallic = metallic_values[offset] if metallic_values is not None else metallic_scalar
        if roughness_values is not None:
            source_value = roughness_values[offset]
            smoothness = source_value if roughness_inverted else 1.0 - source_value
        else:
            smoothness = 1.0 - roughness_scalar
        packed[offset] = max(0.0, min(1.0, metallic))
        packed[offset + 1] = 0.0
        packed[offset + 2] = 0.0
        packed[offset + 3] = max(0.0, min(1.0, smoothness))

    generated = bpy.data.images.new(path.stem, width=width, height=height, alpha=True, float_buffer=False)
    generated.pixels.foreach_set(packed)
    generated.filepath_raw = str(path)
    generated.file_format = "PNG"
    generated.save()
    bpy.data.images.remove(generated)
    return True


def export_material(material, aliases):
    normalized = normalize_material_name(material.name)
    folder_name = safe_name(normalized)
    folder = OUTPUT_ROOT / folder_name
    resource_root = f"EquipmentMaterials/{folder_name}"

    albedo, _ = image_for_role(material, "albedo")
    normal, _ = image_for_role(material, "normal")
    metallic, _ = image_for_role(material, "metallic")
    roughness, roughness_inverted = image_for_role(material, "roughness")

    entry = {
        "name": normalized,
        "aliases": sorted(aliases, key=str.lower),
        "baseColor": list(socket_default(material, "Base Color", [0.8, 0.8, 0.8, 1.0]))[:4],
        "metallic": float(socket_default(material, "Metallic", 0.0)),
        "smoothness": 1.0 - float(socket_default(material, "Roughness", 0.5)),
        "albedo": "",
        "normal": "",
        "metallicSmoothness": "",
    }

    if albedo is not None:
        save_image(albedo, folder / "albedo.png")
        entry["albedo"] = f"{resource_root}/albedo"
    if normal is not None:
        save_image(normal, folder / "normal.png")
        entry["normal"] = f"{resource_root}/normal"
    if save_metallic_smoothness(material, metallic, roughness, roughness_inverted, folder / "metallic-smoothness.png"):
        entry["metallicSmoothness"] = f"{resource_root}/metallic-smoothness"
    return entry


def main():
    paths = [Path(arg).resolve() for arg in sys.argv[sys.argv.index("--") + 1 :]] if "--" in sys.argv else []
    if not paths:
        raise SystemExit("Pass one or more equipment .blend paths after --")

    collected = {}
    for path in paths:
        bpy.ops.wm.open_mainfile(filepath=str(path))
        for material in bpy.data.materials:
            normalized = normalize_material_name(material.name)
            if not normalized:
                continue
            record = collected.setdefault(normalized, {"source": path, "material": material.name, "aliases": set()})
            record["aliases"].add(material.name)

    # Materials belong to their loaded Blender file, so reopen that file immediately before export.
    entries = []
    by_source = {}
    for normalized, record in collected.items():
        by_source.setdefault(record["source"], []).append((normalized, record))

    for source, records in by_source.items():
        bpy.ops.wm.open_mainfile(filepath=str(source))
        for normalized, record in records:
            material = bpy.data.materials.get(record["material"])
            if material is None:
                continue
            entries.append(export_material(material, record["aliases"]))
            print(f"EXPORTED {normalized} from {source.name}")

    OUTPUT_ROOT.mkdir(parents=True, exist_ok=True)
    manifest = {"materials": sorted(entries, key=lambda item: item["name"])}
    MANIFEST_PATH.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(f"WROTE {MANIFEST_PATH} materials={len(entries)}")


if __name__ == "__main__":
    main()
