# Final report — protein.com store v1.8.1

Source: Assets/ProteinStore/create_protein_store.py. Outputs: Assets/ProteinStore/protein_store_lowpoly.blend and GymChaos/Assets/StreamingAssets/BodyBuilders/outside/protein_store_lowpoly.glb.

The v1.8.1 store retains its flat black/yellow/warm-white shell, opaque roof,
organized displays, practical counter, and two transparent fridges with visible
stock. Product faces carry readable PROTEINI.SI brand/category cues, and the
seven family silhouettes are checked through export and runtime. This remains
an original stylized interpretation; detailed package photography is not 1:1.

Validation:
- Blender 4.5.10 build: PROTEIN_STORE_BUILD_OK.
- Blender/source contract: required=5, forbidden=0, emissive=0, glass_alpha=0.16, seven product families, readable brand/category cues.
- Fresh GLB metrics and exact source/runtime hash are recorded in `.unlazy/gymchaos-legacy-goal-20260924/protein-store-art-contract.json` after regeneration.
- Unity store contract: loaded, 1,002 renderers/materials, 154 colliders, two transparent fridge panels, clear entrance.
- Unity integrated contract: GYMCHAOS_PROTEIN_STORE_FULL_CONTRACT_OK; rareChance=0.08; storeReached/returnedToGym/Mark dialogue/two fridges/drinks/direct obstacle block/detour all passed; max detour 1.28 m.
- GLB source/runtime hash: C3D575652EC4F3EA54B19FE6633BB3B69FC915712584E79D9CDC51E5DD60412E. Blend hash: 16BAB12E3D9337926537762AE362FF7E0D9AD4466097FBBDC1E3BC9433027EE2.

Route blocker injection now requires a legal 1.55 m lateral clearance; the verifier still checks direct blockage and completed detour. See product_layout_notes.md, modeling_contract.md, and reference_notes.md for the stylized-fidelity boundary.
