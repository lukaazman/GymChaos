import bpy, json, struct
import numpy as np
from mathutils import Vector
from pathlib import Path

root = Path(__file__).resolve().parents[1]
data = (root / "Assets/BodyBuilders/wearables/headband.glb").read_bytes()
length = struct.unpack_from("<I", data, 12)[0]
doc = json.loads(data[20:20+length])
accessor = doc["accessors"][doc["meshes"][0]["primitives"][0]["attributes"]["POSITION"]]
view = doc["bufferViews"][accessor["bufferView"]]
offset = 28 + length + view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
v = np.frombuffer(data, dtype="<f4", count=accessor["count"]*3, offset=offset).reshape(-1,3).copy()
v[:,0] *= -1
w, axes = np.linalg.eigh(np.cov(v.T))
normal = axes[:,0]
if normal[1] < 0: normal *= -1
print("HEADBAND_UNITY_NORMAL",normal.tolist(), "EIGENVALUES",w.tolist())
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(root / "GymChaos/Assets/Resources/Player/player_authored.fbx"))
for obj in bpy.context.scene.objects:
    if obj.type == "ARMATURE":
        for bone in obj.data.bones:
            if "spine" in bone.name or "head" in bone.name.lower():
                print("BONE", bone.name, tuple(obj.matrix_world @ bone.head_local), tuple(obj.matrix_world @ bone.tail_local))
    if obj.type == "MESH":
        verts=[obj.matrix_world @ v.co for v in obj.data.vertices]
        print("BODY",obj.name,tuple(obj.dimensions),tuple(min(v[i] for v in verts) for i in range(3)),tuple(max(v[i] for v in verts) for i in range(3)))
        for group in obj.vertex_groups:
            if "spine.005" in group.name or "head" in group.name.lower():
                selected=[obj.matrix_world @ v.co for v in obj.data.vertices if any(g.group==group.index and g.weight>=0.5 for g in v.groups)]
                print("HEAD_WEIGHT",group.name,len(selected),tuple(min(v[i] for v in selected) for i in range(3)) if selected else None,tuple(max(v[i] for v in selected) for i in range(3)) if selected else None)
