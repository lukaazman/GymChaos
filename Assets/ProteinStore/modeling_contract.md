# protein.com low-poly store — modeling contract

- Flat, open-front retail shell in warm white and signal yellow, with black structure, restrained lights, and an opaque ceiling.
- Keep the existing product zones, practical checkout, and two transparent stocked fridges.
- Represent whey, creatine, pre-workout, shakers, bars, energy cans, and chilled drinks with distinct silhouettes and compact raised brand/category cues.
- Keep meshes low-poly, products clear of shelves/glass, and authored Blender source synchronized with the runtime GLB.
- Package branding is a stylized cue, not copied 1:1 artwork.

## v1.8.1 validation contract

The source contract requires seven product families with a readable
PROTEINI.SI cue, a family-specific front cue, and a family-specific silhouette
token. Blender validates those meshes before export; the dependency-free
verification script checks exported node names, official-reference metadata,
asset limits, and the byte-identical Unity runtime copy. Unity then validates
the same cues after runtime GLB instantiation and captures direct product and
environment evidence under Direct3D12.

The existing shell contract remains: opaque ceiling, open entrance, useful
category zones, practical counter, two transparent stocked fridges, no
emissive neon geometry, and low-poly materials. Detailed package photography
is out of scope; recognizable original forms and hierarchy are required.
