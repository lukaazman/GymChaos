# protein.com store, v2

Built from scratch on 2026-10-08. The earlier `../create_protein_store.py` model
is kept only as a reference for the product categories and the staff space
behind the counter.

## Build

```powershell
python Assets/ProteinStore/v2/make_store_textures.py
& "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" -b --factory-startup -P Assets/ProteinStore/v2/create_protein_store_v2.py
python Assets/ProteinStore/v2/verify_protein_store_v2.py
Copy-Item Assets/ProteinStore/v2/protein_store_v2.glb GymChaos/Assets/StreamingAssets/BodyBuilders/outside/protein_store.glb
```

Pass `-- --no-render` to the Blender step to skip the preview renders in
`renders/`.

## Contents

- 23.4 m x 20.1 m single-storey shop, plain boxes and low-segment cylinders, no
  bevels. White walls and shelves, black shelving frames and storefront, yellow
  brand accents (proteini.si colours). `protein.com` letters on the fascia.
- Door 2.6 m wide on the storefront (authored Y = -2.76); Unity lines the gym
  path's wall opening up with it, at the south end of the path.
- Categories: PROTEIN (whey pouches and tubs), ISOLATE, AMINO ACIDS, SNACKS
  (peanut butter, shakers) on the north wall; PRE-WORKOUT and CREATINE on the
  south wall; PROTEIN BARS and VITAMINS & MINERALS on the back wall and the
  island gondolas; six glass-door COLD DRINKS fridges (shakes, energy cans).
- Checkout counter by the door with till, card terminal, bar impulse box and a
  staff zone behind it (Mark stands there).
- Packaging follows the proteini.si look (white pouches and tubs with a
  flavour-coloured lightning bolt, blue protein bar boxes) under the store's own
  `protein.com` name; textures come from `make_store_textures.py`.

## Checks

`verify_protein_store_v2.py` reads `layout_boxes.json` (every fixture part and
product recorded during the build) and fails on any overlap deeper than 1 mm,
any product that does not stand on a surface, or missing Unity-facing objects
(`Solid_*` colliders, fridge glass, sign letters). Unity-side placement is
checked by `GymChaosProteinStoreVerifier` (`GYMCHAOS_PROTEIN_STORE_LAYOUT_OK`).

Colour factors are written as sRGB values because `RuntimeGlbSceneLoader`
passes glTF factors straight to `Material.color`.
