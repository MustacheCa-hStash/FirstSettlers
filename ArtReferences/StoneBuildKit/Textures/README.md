# Painted stone trim-sheet proposal

Implementation update: S02 now uses a **12-triangle box with baked colour/AO and normal atlases** derived from its original detailed masonry. See the [baked wall guide](../../../Documentation/BAKED_STONE_WALL.md). S03/S04/S06/S07/S12 now also use minimal solids with one shared colour/AO + normal atlas; see the [baked kit guide](../../../Documentation/BAKED_STONE_KIT.md). The labelled board below remains a proposal; its masonry seams and labels are not applied to those meshes. See the [model and import guide](../../../Documentation/STONE_BUILD_KIT.md).

[View the labelled proposal](painted-stone-trim-proposal.png).

Soft matte warm grey, taupe and restrained slate, with broad painted patches and minimal surface noise. This follows the project's selected [painted rock surface](../../Rocks/README.md) and pairs with its active Meadow Timber wood atlas. The catalog's ink/gouache construction drawings are presentation references; this trim proposal follows the quieter in-game surface style.

| Region | Purpose | Typical S## parts |
| --- | --- | --- |
| A — Irregular rubble | Broad staggered masonry faces | S01, S07–S09, S11–S12, S23 |
| B — Coursed rubble | More orderly masonry variant | S02; optional coursed faces on other walls |
| C — Cap / tread top | Continuous stone surface for geometry-defined tops | S06, S17–S21 |
| D — Cut / edge face | Continuous surface for sides, sills and risers | S04, S06, S11, S17–S20 |
| E — Large stone face | Joint-free surface for individual modeled stones | S03, S05, S15, S21, S24 |
| F — Flagstone paving | Large, quiet paving pattern | S04, S20 |
| G — Packed earth joint | Narrow recessed packing accents | Masonry joints and footing packing |
| H — Pale stone variant | Controlled alternative mineral colour | Selected stones across the kit |

Use A/B/F on relatively simple surfaces when a painted masonry pattern is useful. Use C/D/E/H on individually modeled stones so painted joints do not cross their geometry. Mixing the two approaches should retain similar apparent stone sizes. Wooden lintels, leaves, plates, floors and rails reuse the existing Meadow Timber atlas; roofs reuse the existing timber/thatch material vocabulary.

The intended atlas allocation has four full-width strips in the upper half; one large stone patch in the lower-left quadrant; paving, joint and pale-stone patches stacked in the lower-right quadrant. This labelled image communicates the design, not final pixel/UV coordinates. The generated strip heights are illustrative.

Generated with the built-in image_gen tool; [the exact prompt](GENERATION_PROMPTS.md) is saved here. This is a design preview with labels, a legend and a surrounding board. It is not a production albedo: artwork isolation, exact packing, gutters, repeat-edge checks and mip inspection remain future texture-authoring work. No materials, UVs, models or scene assets were changed.
