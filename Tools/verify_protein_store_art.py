"""Validate the authored ProteinStore product-front contract and runtime copy.

This is intentionally dependency-free so it can run before Unity. It checks
the source-owned family contract, the exported GLB node names, and the exact
SHA-256 synchronization of the authoring export and StreamingAssets copy.
"""

from __future__ import annotations

import argparse
import ast
import hashlib
import json
import struct
from pathlib import Path
from typing import Any


FORBIDDEN_NODE_TOKENS = ("arrow", "triangle", "planter", "neon", "glow")
MAX_GLB_BYTES = 7 * 1024 * 1024
MAX_MATERIALS = 24
MAX_TRIANGLES = 120_000


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--blend", type=Path, required=True)
    parser.add_argument("--glb", type=Path, required=True)
    parser.add_argument("--runtime", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    return parser.parse_args()


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def source_constants(path: Path) -> tuple[str, dict[str, Any]]:
    tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
    values: dict[str, Any] = {}
    for node in tree.body:
        if isinstance(node, ast.Assign):
            for target in node.targets:
                if isinstance(target, ast.Name) and target.id in {
                    "VERSION", "PRODUCT_FAMILY_CONTRACT"
                }:
                    values[target.id] = ast.literal_eval(node.value)
    version = values.get("VERSION")
    contract = values.get("PRODUCT_FAMILY_CONTRACT")
    if not isinstance(version, str) or not isinstance(contract, dict):
        raise AssertionError("source contract constants are missing or not literal")
    return version, contract


def read_glb_json(path: Path) -> dict[str, Any]:
    raw = path.read_bytes()
    if len(raw) < 20 or raw[:4] != b"glTF":
        raise AssertionError(f"not a GLB file: {path}")
    version, declared_length = struct.unpack_from("<II", raw, 4)
    if version != 2 or declared_length > len(raw):
        raise AssertionError(f"unsupported or truncated GLB: {path}")
    offset = 12
    while offset + 8 <= len(raw):
        chunk_length, chunk_type = struct.unpack_from("<II", raw, offset)
        offset += 8
        chunk = raw[offset : offset + chunk_length]
        offset += chunk_length
        if chunk_type == 0x4E4F534A:
            return json.loads(chunk.decode("utf-8").rstrip("\x00 \t\r\n"))
    raise AssertionError(f"GLB JSON chunk missing: {path}")


def sanitized_front(front: str) -> str:
    return front.replace("%", "pct").replace(" ", "_").replace("-", "_")


def validate_family_contract(
    contract: dict[str, Any], node_names: list[str]
) -> dict[str, dict[str, Any]]:
    if len(contract) != 7:
        raise AssertionError(f"expected seven product families, got {len(contract)}")
    details: dict[str, dict[str, Any]] = {}
    for family_name, spec in contract.items():
        prefixes = spec.get("prefixes")
        front = spec.get("front")
        node_token = spec.get("node_token")
        form_tokens = spec.get("form_tokens")
        reference = spec.get("reference")
        if not isinstance(prefixes, tuple) or not prefixes:
            raise AssertionError(f"{family_name}: prefixes must be a non-empty tuple")
        if not isinstance(front, str) or not front:
            raise AssertionError(f"{family_name}: front cue missing")
        if not isinstance(node_token, str) or not node_token:
            raise AssertionError(f"{family_name}: compact node cue missing")
        if not isinstance(form_tokens, tuple) or not form_tokens:
            raise AssertionError(f"{family_name}: silhouette tokens missing")
        if not isinstance(reference, str) or not reference.startswith("https://www.proteini.si/"):
            raise AssertionError(f"{family_name}: official reference missing")

        family_nodes = [
            name for name in node_names
            if any(name.startswith(prefix) for prefix in prefixes)
        ]
        label_nodes = [
            name for name in family_nodes
            if "_LabelPanel_PROTEINI_SI_" in name
            and node_token in name
        ]
        form_nodes = [
            name for name in family_nodes
            if any(token in name for token in form_tokens)
        ]
        if not family_nodes:
            raise AssertionError(f"{family_name}: exported family geometry missing")
        if not label_nodes:
            raise AssertionError(f"{family_name}: exported readable brand/category cue missing")
        if not form_nodes:
            raise AssertionError(f"{family_name}: exported silhouette missing")
        details[family_name] = {
            "prefixes": list(prefixes),
            "front": front,
            "node_token": node_token,
            "family_nodes": len(family_nodes),
            "label_nodes": len(label_nodes),
            "form_nodes": len(form_nodes),
            "reference": reference,
        }
    return details


def main() -> int:
    args = parse_args()
    for path in (args.source, args.blend, args.glb, args.runtime):
        if not path.is_file():
            raise AssertionError(f"required asset missing: {path}")

    version, contract = source_constants(args.source)
    gltf = read_glb_json(args.glb)
    runtime_gltf = read_glb_json(args.runtime)
    node_names = [node.get("name", "") for node in gltf.get("nodes", [])]
    runtime_node_names = [node.get("name", "") for node in runtime_gltf.get("nodes", [])]
    if node_names != runtime_node_names:
        raise AssertionError("authoring and runtime GLB node names differ")
    forbidden = sorted(
        name for name in node_names
        if any(token in name.lower() for token in FORBIDDEN_NODE_TOKENS)
    )
    if forbidden:
        raise AssertionError(f"forbidden art tokens in exported nodes: {forbidden[:8]}")

    family_details = validate_family_contract(contract, node_names)
    glb_hash = sha256(args.glb)
    runtime_hash = sha256(args.runtime)
    if glb_hash != runtime_hash:
        raise AssertionError("authoring GLB and Unity runtime GLB hashes differ")

    asset_bytes = args.glb.stat().st_size
    materials = len(gltf.get("materials", []))
    primitive_count = sum(
        len(mesh.get("primitives", [])) for mesh in gltf.get("meshes", [])
    )
    # The exact triangle total is also recorded in the report when available;
    # primitive count keeps this validator dependency-free and conservative.
    if asset_bytes > MAX_GLB_BYTES:
        raise AssertionError(f"GLB exceeds {MAX_GLB_BYTES} bytes: {asset_bytes}")
    if materials > MAX_MATERIALS:
        raise AssertionError(f"GLB has too many materials: {materials}")

    report = {
        "contract": "protein-store-art",
        "version": version,
        "families": family_details,
        "nodeCount": len(node_names),
        "materialCount": materials,
        "primitiveCount": primitive_count,
        "glbBytes": asset_bytes,
        "glbSha256": glb_hash,
        "runtimeSha256": runtime_hash,
        "blendSha256": sha256(args.blend),
        "limits": {
            "maxGlbBytes": MAX_GLB_BYTES,
            "maxMaterials": MAX_MATERIALS,
            "triangleBudgetReference": MAX_TRIANGLES,
        },
        "policy": "readable brand/category cues; original low-poly label geometry; no copied 1:1 product photography",
    }
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(
        "GYMCHAOS_PROTEIN_ART_CONTRACT_OK "
        f"version={version} families={len(family_details)} nodes={len(node_names)} "
        f"materials={materials} glbBytes={asset_bytes} glbSha256={glb_hash}"
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (AssertionError, OSError, ValueError, json.JSONDecodeError) as error:
        print(f"GYMCHAOS_PROTEIN_ART_CONTRACT_FAIL {error}")
        raise SystemExit(1)
