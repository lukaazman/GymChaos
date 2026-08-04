"""Verify the externally rigged FBX character assets before Unity import."""

from pathlib import Path

import bpy


ROOT = Path("D:/GitHub/GymChaos/GymChaos/Assets/Resources/Characters")
CHARACTERS = (
    ROOT / "Enemies/Arnold.fbx",
    ROOT / "Enemies/Cbum.fbx",
    ROOT / "Enemies/Zyzz.fbx",
    ROOT / "Enemies/Ronnie.fbx",
    ROOT / "Enemies/JayCutler.fbx",
    ROOT / "Enemies/Goku.fbx",
    ROOT / "Reception/manwithsuit1.fbx",
)
CORE_GROUP_NAMES = {
    "mixamorig:Hips",
    "mixamorig:Spine",
    "mixamorig:Spine1",
    "mixamorig:Neck",
    "mixamorig:Head",
    "mixamorig:LeftArm",
    "mixamorig:LeftForeArm",
    "mixamorig:LeftHand",
    "mixamorig:RightArm",
    "mixamorig:RightForeArm",
    "mixamorig:RightHand",
    "mixamorig:LeftUpLeg",
    "mixamorig:LeftLeg",
    "mixamorig:LeftFoot",
    "mixamorig:RightUpLeg",
    "mixamorig:RightLeg",
    "mixamorig:RightFoot",
}


def verify(path: Path) -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path))
    armatures = [obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"]
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if len(armatures) != 1 or not meshes:
        raise RuntimeError(f"{path.name}: expected one armature and at least one mesh")

    armature = armatures[0]
    deform_bones = {bone.name for bone in armature.data.bones if bone.use_deform}
    if len(deform_bones) < 30:
        raise RuntimeError(f"{path.name}: only {len(deform_bones)} deform bones survived export")

    mesh = max(meshes, key=lambda obj: len(obj.data.vertices))
    groups = {group.name for group in mesh.vertex_groups}
    # Terminal Mixamo marker/finger bones are not necessary skinning
    # influences. The core torso, arm, and leg chains are the contract needed
    # by Unity's Humanoid importer and by the downloaded clips.
    missing_groups = CORE_GROUP_NAMES - groups
    if missing_groups:
        raise RuntimeError(f"{path.name}: missing skin groups {sorted(missing_groups)}")

    armature_modifiers = [modifier for modifier in mesh.modifiers if modifier.type == "ARMATURE"]
    if not armature_modifiers or all(modifier.object != armature for modifier in armature_modifiers):
        raise RuntimeError(f"{path.name}: mesh is not bound to the exported armature")

    image_count = len([image for image in bpy.data.images if image.source != "VIEWER"])
    if image_count == 0:
        raise RuntimeError(f"{path.name}: no embedded or linked texture image survived export")

    print(
        f"CHARACTER_EXPORT_OK {path.name} bones={len(deform_bones)} "
        f"groups={len(groups)} vertices={len(mesh.data.vertices)} images={image_count}"
    )


def main() -> None:
    for path in CHARACTERS:
        if not path.is_file():
            raise RuntimeError(f"Missing external character asset: {path}")
        verify(path)
    print(f"CHARACTER_EXPORTS_OK checked={len(CHARACTERS)}")


if __name__ == "__main__":
    main()
