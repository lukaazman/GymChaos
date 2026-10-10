Render-driven lint fix for the FBX character textures (2026-10-09).

`run.sh <name> <fbx under Resources/Characters> <texture> <npz>` renders the
character front/back at 1000 px with the runtime UV inset applied
(`views_inset.py`), finds light/off-colour specks on dark cloth
(`find_specks.py`), raycasts each speck to its UV (`probe_uv.py`) and repaints
the hit faces with their 3D neighbour colour (`fix_points.py`). Faces with any
light neighbour or within two rings of a light detail (badges, buckles, skin
borders) are never touched. Paths inside the scripts point at the session
scratch folder; adjust before reuse.
