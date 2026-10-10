# Product layout notes — Proteini.si pass v1.8.1

Product forms and colors reference the current listings below. Each package face now carries a readable PROTEINI.SI brand cue and a family cue (`100% NATURAL WHEY`, `100% PURE CREATINE`, `PRE-WORKOUT`, `SPORT SHAKER`, `PROTEIN BAR`, `ENERGY DRINK`, or `PROTEIN SHAKE`). The package forms remain original low-poly geometry; detailed label photography and 1:1 artwork are not copied.

## References and display zones
- [Shakers](https://www.proteini.si/sl/dodatki/sejkerji): tall bottles; left-front rack.
- [Pre-workout](https://www.proteini.si/sl/pre-workout): canisters/bottles; left-rear rack.
- [Pure creatine 200 g](https://www.proteini.si/sl/kreatin/monohidrat/proteini-si-100-pure-creatine-200g): white resealable pouch; back-wall fixture.
- [Natural whey](https://www.proteini.si/sl/beljakovine/sirotka/proteini-si-100-natural-whey-protein): pouches and tubs; right rack.
- [Energy drinks](https://www.proteini.si/sl/energijska-hrana/energijski-napitki): slim cans; front-right cooler.
- [Protein bars](https://www.proteini.si/sl/beljakovine/beljakovinske-ploscice): wrapped bars; island and checkout.
- [RTD shake](https://www.proteini.si/sl/beljakovine/pripravljeni-napitki/proteini-si-protein-shake-rtd-8x330ml-vanilla): chilled bottles; rear-left fridge.

Whey and creatine pouches have gussets, tapered shoulders, seals, and reseal strips. Both fridge fronts are transparent and show stock. Packaging uses restrained white/ivory faces with blue, brown, violet, orange, and black accents; the store shell stays black, signal yellow, and warm white.

## v1.8.1 verification contract

The source script owns a seven-family contract with official reference URLs,
prefixes, silhouette tokens, and front-cue tokens. `Tools/verify_protein_store_art.py`
checks that contract against the exported GLB node names and the byte-identical
Unity StreamingAssets copy. The runtime art verifier separately checks the
same cues after the GLB loader has instantiated them and captures close-ups.
