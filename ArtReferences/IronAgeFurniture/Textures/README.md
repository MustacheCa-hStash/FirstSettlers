# Household furnishings — one supplementary trim atlas

One new opaque base-color atlas complements the existing Meadow Timber and Painted Stone textures. It supplies the materials missing from the building kit without duplicating its wood, grain caps, lashings or stone. The design covers all F01–F24 furnishing materials using **one additional texture**, with shared existing wood/stone where appropriate.

- [Clean atlas](household-trim-albedo.png) — native 1254 × 1254 PNG, no labels or borders.
- [Labelled material and region guide](household-trim-guide.svg) — self-contained SVG with the texture embedded.
- [Material proof render](material-proof.png) — actual atlas applied to temporary basket, pottery, textile, mat, bone and leaf forms alongside the existing wood and stone.
- [Editable proof scene](material-proof.blend) and [reproduction script](render_material_proof.py).
- [Exact source-pixel rectangles and inset UV coordinates](atlas-layout.json).
- [Source/UV verification report](validation.json).
- [Exact built-in imagegen prompt and references](GENERATION_PROMPTS.md).

The PNG is a generated **design source**, retained without pixel editing or enlargement. It has inset sampling guards rather than extruded padding, and its opposing edges differ. Use the supplied bounded UV spans for initial modeling. A production packing/seam pass and Unity mip/compression review remain before treating it as a seamless shipping atlas. The preview proves material coverage and bounded UV use; it does not establish seamless repetition.

## Match to the current art

The active `SplitPlankWood` material references [Meadow Timber](../../../Assets/Textures/Buildings/Wood/ordinary-wood-trim-meadow-timber.png), confirmed by its texture GUID. That atlas and the actual [painted stone](../../../Assets/Textures/Buildings/Stone/PaintedStone_Albedo.png) were inspected and supplied to imagegen as style references. The matched wattle render, existing thatch, painted foliage and the furnishing sheets were also consulted.

The surface treatment follows the game assets rather than the catalog's more detailed ink drawings: soft painted color, simplified broad marks, warm muted browns, matte clay, oatmeal cloth and restrained rust/blue-grey accents. The basket and mat retain enough weave to read at a distance; tiny photographic fibers, pores, sharp cracks and heavy carved ornament are avoided. Clay hue is deliberately warmer than the stone to keep the material families distinguishable.

The proof uses roughness 1, zero specular contribution and no normal/bump map. Its shadows come from Blender studio lighting. It is a style/UV proof, not the Unity lighting result.

## Allocation

A–D receive the upper half for surfaces used over substantial visible areas. E–G use horizontal strips for repeated construction details; H–K share the last quarter for small props and utility surfaces. Giving the large reusable fields most of the sheet avoids spending a separate texture on each pottery vessel or fabric color.

| Region | Material | Use |
| --- | --- | --- |
| A | Closed willow weave | Basket bodies, lids and shallow interiors; F08–F10. |
| B | Bound rush | F11 floor mat; broad parallel rushes differ from basket weave. |
| C | Plain oatmeal wool | F12 bedding, F22 cloth and close crops on F21's soft wool geometry. |
| D | Buff clay | F13 jar, F14 upper body, F15 bowl and F21 whorl. |
| E | Striped wool border | F12 edge band and F22 textile accent; continuous rust, charcoal and blue-grey stripes. |
| F | Bound willow rim | Basket lips and wrapped handles; also small cord details where this binding suits. |
| G | Incised clay band | F13 jar shoulder; optional restrained F15 rim decoration. |
| H | Warm bone | F23 weaving comb, with shape/teeth provided by geometry. |
| I | Smoke-darkened clay | F14 lower body and cavities; use D for an unsooted alternative. |
| J | Cold ash / packed clay | F19 hearth centre, inside its separately modeled stone surround. |
| K | Dusty sage leaf surface | F24 small opaque leaf meshes. |

F is a surface band for an actual rim mesh, not a billboard rope silhouette. A is opaque close weave; it does not provide holes for a cord bed deck. K is an opaque surface fill, not a plant cutout. Herb silhouettes and bed-deck openings come from geometry or a separately suitable existing cutout material.

## Reuse instead of another sheet

| Furnishing IDs | Surface assignment |
| --- | --- |
| F01–F04 | Existing wood A/B/C faces, E/F caps and H pegs. |
| F05 | Existing wood frame; existing G lashing on modeled cord strands. Keep real gaps between strands. |
| F06/F07 | Existing wood; chest lid remains a separate lift-off part. |
| F08–F10 | New A body/lid, F rim; existing wood G or modeled fibre for F10 suspension. |
| F11 | New B; geometric thin edge, no floating fringe alpha required. |
| F12 | New C body and E border. |
| F13 | New D body and G shoulder. |
| F14 | New D upper body, I lower/cavity; transition needs a deliberate mesh/UV seam or later painted blend. |
| F15 | New D; G is optional and should stay subtle. |
| F16–F18 | Existing wood tight grain and suitable end-grain crops. Grain follows carved form. |
| F19 | Existing Painted Stone ring; new J ground centre. |
| F20 | Existing Painted Stone bed/runner and existing wood handle. |
| F21 | Existing wood spindle, new D ceramic whorl and C on modeled wool volume. |
| F22 | New C and E; existing wood suspension rod. |
| F23 | New H; incised handle marks can be geometry or a later small decal if needed. |
| F24 | New K on small leaf geometry, existing wood/fibre crops on stems and binding. |

This is one **new atlas**, not one material/draw call for every furnished object. A wood table can keep its existing material; a basket can use only the new material. Mixed bed, hearth or quern meshes can require two shared materials/submeshes. Keep those shared rather than creating a separate material for each object. No new metal, transparent-glass or glossy pottery material is needed.

## UV guide

UV origin is bottom-left, as in Blender. The JSON also records pixel rectangles from the top-left of the 1254-pixel source. Material transitions are hand-generated and a few pixels wide; the documented safe content bounds sit **12 source pixels inside** each allocation, avoiding those transitions.

The coordinates below address the actual saved PNG, not the originally requested 2048 canvas. They are inclusive texel-centre bounds. The outer guard is still that region's artwork; it has not been repaired into an extruded gutter.

| ID | Material | U min | V min | U max | V max |
| --- | --- | --- | --- | --- | --- |
| A | Closed willow weave | 0.009968 | 0.759569 | 0.490032 | 0.990032 |
| B | Bound rush | 0.509968 | 0.759569 | 0.990032 | 0.990032 |
| C | Oatmeal wool | 0.009968 | 0.509171 | 0.490032 | 0.739633 |
| D | Buff clay | 0.509968 | 0.509171 | 0.990032 | 0.739633 |
| E | Striped wool border | 0.009968 | 0.399123 | 0.990032 | 0.489234 |
| F | Bound willow rim | 0.009968 | 0.340909 | 0.990032 | 0.379187 |
| G | Incised clay band | 0.009968 | 0.259569 | 0.990032 | 0.320973 |
| H | Bone | 0.009968 | 0.009968 | 0.239633 | 0.239633 |
| I | Smoke-darkened clay | 0.259569 | 0.009968 | 0.490032 | 0.239633 |
| J | Cold ash / packed clay | 0.509968 | 0.009968 | 0.740431 | 0.239633 |
| K | Dusty sage | 0.760367 | 0.009968 | 0.990032 | 0.239633 |

Map a local 0–1 UV section using `U = uMin + localU × (uMax-uMin)` and the corresponding V equation. Every polygon must remain in a single intended region. Separate a jar shoulder from its undecorated body and a cloth border from its body; do not stretch one polygon through multiple atlas bands.

For trim strips, U follows the circumference or long edge; V crosses the band. Put a jar's UV seam at the rear. Leave all three wool colors in E rather than using a separate texture per stripe. C's neutral body can appear above/below the border without filling the whole cloth with stripes. On basket rims, avoid putting the diagonal wraps along the rim's cross-section rather than around its circumference.

The nominal sample sizes in JSON are starting art scales, not a single enforced texel density. A/B can start around 0.75 × 0.375 m per field; C around 0.375 × 0.1875 m; D around 0.50 × 0.25 m. E spans about 2 m of length and 0.22 m width. Adjust to the object's silhouette and viewing distance; simplify the pattern before enlarging weave into giant ropes.

The entire atlas must not tile as one texture. Whole-image Repeat cannot repeat an internal cell. For bounded initial mapping, fit a whole section once, or split a mesh into bounded UV sections. The proof splits the cloth into a few repeats to assess scale and exposes any joins; those repeated sections are **not** a claim of seamless source art. For final repeated cloth/basket surfaces, periodic cell edges must be finished and checked at the chosen sample interval. A local atlas-remap shader would need matching periodic art too, and is not part of this design.

## Production handoff

Keep the native source intact. For a final 2048 × 2048 production pack, resize/repack each chosen region into an exact documented cell, finish the periodic boundaries, extrude or wrap cell padding and publish updated UV bounds. Upscaling this source does not create native 2048 detail. The current guard of 12 source pixels corresponds to about 20 pixels at 2048, but an inset is not the same as a generated gutter.

Use sRGB base color, a white base tint, an opaque matte material, metallic 0 and low smoothness. Keep texture scale/offset at 1/0. Start with clamp sampling for the complete design atlas and keep UVs inside their bounds. Inspect mipmaps and compression at normal play distance, especially C's weave, narrow F and G, and colored E beside clay. Padding cannot prevent all cross-cell mixing at the smallest mips; a distant LOD can use a quiet crop or simplified material.

Use the existing `Custom/BuildingWoodMatte` as a visual starting point if desired; it already accepts a UV0 base-color texture and matte lighting controls. The design does not change its code or add shader features. Do not infer a physically valid normal/height map from painted albedo. Any later normal/roughness maps must use the final atlas layout.

No shared material, texture, shader, game mesh, building definition, importer or scene was changed for this proposal. Keep the new source in ArtReferences until the production pack and first furnishing models are ready.

## Verification and reproduction

The source is 1254 × 1254 and fully opaque. The report records its SHA-256 and both-axis edge differences for all eleven content regions: the generated edges are **not pixel-identical**. All 2,264 checked atlas-mapped proof faces stayed inside their intended safe regions. Existing textures were loaded read-only.

The proof was rendered and visually reviewed: separate wicker/rush/wool patterns remain readable, the clay decoration follows a jar shoulder, the basket has a genuine cavity, and the new palette pairs with Meadow Timber and Painted Stone. The preview forms are disposable material studies rather than production furnishing models. Unity import, mip/compression behavior and gameplay-distance lighting have not been validated.

Reproduce with Blender 5.0:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.0/blender.exe' -b -t 4 --python ArtReferences/IronAgeFurniture/Textures/render_material_proof.py
```

The SVG is a separate vector annotation layer around an embedded copy of the source; it does not modify texture pixels. Its small template is retained as `household-trim-guide.template.svg`. Exact generation instructions are preserved in GENERATION_PROMPTS.md; the built-in imagegen tool produced the sole new texture.

