"""Recover the original police UV atlas without rebuilding the rig or animation."""
import hashlib
import json
import struct
from pathlib import Path

root = Path(__file__).resolve().parents[1]
source = root / "Assets/BodyBuilders/policeman.glb"
data = source.read_bytes()
magic, version, length = struct.unpack_from("<III", data)
assert magic == 0x46546C67 and version == 2 and length == len(data)
chunks = {}
offset = 12
while offset < length:
    size, kind = struct.unpack_from("<II", data, offset)
    chunks[kind] = data[offset + 8:offset + 8 + size]
    offset += 8 + size
gltf = json.loads(chunks[0x4E4F534A])
indices = {
    gltf["textures"][material["pbrMetallicRoughness"]["baseColorTexture"]["index"]]["source"]
    for material in gltf["materials"]
    if "baseColorTexture" in material.get("pbrMetallicRoughness", {})
}
assert len(indices) == 1, f"Expected one original UV atlas, got {indices}"
image = gltf["images"][indices.pop()]
assert image["mimeType"] == "image/png"
view = gltf["bufferViews"][image["bufferView"]]
start = view.get("byteOffset", 0)
atlas = chunks[0x004E4942][start:start + view["byteLength"]]
assert atlas.startswith(b"\x89PNG\r\n\x1a\n")
destination = root / "GymChaos/Assets/Resources/Characters/Textures/policeman.png"
if destination.exists() and destination.read_bytes() != atlas:
    raise RuntimeError("Refusing to replace an existing different atlas")
destination.write_bytes(atlas)
print(f"Police original atlas: {len(atlas)} bytes SHA256={hashlib.sha256(atlas).hexdigest()}")
