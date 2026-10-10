# protein.com low-poly store

Generated Blender asset for GymChaos. It is one mall-ready room module with an
open storefront, sliding entrance/exit, `protein.com` sign, racks, cooler,
checkout counter, central display island and abstract low-poly products. The v1.4.3 refinement removes every category sign and the interior circular logo, leaving only the storefront protein.com name; the entrance cooler now holds 24 fitted energy cans and the extra fridge holds 12 fitted RTD/can items.

The durable source is [create_protein_store.py](create_protein_store.py). Use
Blender 4.5 LTS or newer:

```powershell
& "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" --background --factory-startup --python .\create_protein_store.py
```

Outputs:

- `protein_store_lowpoly.blend` — editable authored scene with cameras/lights;
- `protein_store_lowpoly.glb` — selected runtime mesh export;
- `renders\protein_store_beauty.png` — exterior hero view;
- `renders\protein_store_front.png` — facade/sign/door view;
- `renders\protein_store_interior.png` — stock and fixtures view;
- `scene_manifest.txt` — generated object/material summary.

The `.blend` is deliberately kept as one room. `Mall_Module_Join_*` and
`Mall_Module_Top_Rail` are the future adjacency anchors for placing it beside
other mall units.
