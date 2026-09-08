# BodyBuilders authored sources

Every playable character has its own source blend, deform skeleton, and baked
FBX. No enemy or player shares a runtime skeleton or a hidden animation source.

- `enemies/*_rig.blend` are the six independent enemy rigs.
- `Player/player_rig.blend` is the independent player rig.
- `enemies/anims/` contains the source motion clips used by the exporter.
- `enemies/anims/player_only/` contains the player-only source clips.
- `Tools/export_authored_character_fbx.py` opens each blend separately,
  retargets the source motion onto that blend's own Rigify controls, bakes its
  deform bones, and writes one mesh-plus-animation FBX.

The Unity runtime loads these final assets directly:

- `GymChaos/Assets/Resources/Characters/Enemies/*_authored.fbx`
- `GymChaos/Assets/Resources/Player/player_authored.fbx`

Enemy locomotion, attacks, flight, squat, idle, and celebration clips are
sampled on the same imported enemy hierarchy that is rendered. Player
locomotion, jump, punches, and throws are sampled on the imported player
hierarchy. The old `_mixamo_rigged` playable models, `player_mia_rigged`, and
shared standalone animation folders are no longer runtime dependencies.

The receptionist remains a separate legacy NPC asset because it is not one of
the playable enemy/player rigs.
