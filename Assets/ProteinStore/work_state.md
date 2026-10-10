# Work state

- Stage: final sign/emblem cleanup, entrance energy-cooler fit correction and beverage-shape refinement complete.
- Current durable source: create_protein_store.py.
- Current source version: 1.8.1.
- Reference limitation: supplied attachment PNGs were inaccessible to the
  sandbox; official Proteini.si category/product pages and the user's
  constraints informed the shape and palette decisions. See
  reference_notes.md and product_layout_notes.md.
- Completed: clean Blender regeneration, removal of all category signs and the interior emblem/light, fitted 24-can entrance energy cooler and 12-item extra fridge with zero product overlap, both-fridge shelf/content correction, beverage can/bottle shape refinement, fresh authored/GLB metrics, standardized v10 multiview evidence, and visual review of the exported result.
- Final report: final_report.md.
- Final evidence: validation/evidence_authored_v10 and validation/evidence_glb_v10; numeric metrics are validation/authored_metrics_v10.json and validation/glb_metrics_v10.json.
- Product fronts now use readable PROTEINI.SI/category cues with original low-poly label geometry; copied 1:1 artwork remains out of scope.
- The seven-family source/export/runtime contract is checked by `Tools/verify_protein_store_art.py` and the Unity Direct3D12 art verifier.
