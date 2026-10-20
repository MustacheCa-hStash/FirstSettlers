# Limestone + timber material trim proposal

Implementation update: L01–L09 now use exact baked colour/AO and normal atlases derived from editable stone sources. See the [L01–L03 guide](../../../Documentation/LIMESTONE_BUILD_KIT.md) and [L04–L09 guide](../../../Documentation/LIMESTONE_L04_L09.md). The joint-free F region below supplies stone colour during baking; the labelled guide and approximate proposal packing are not applied directly to runtime meshes.

L25, L27, L28, L31, L33 and L35 now sample the clean trim source directly, using measured interior UV regions and a shallow Blender-baked grain normal map. Their timber, daub, end grain and solid shingle geometry share one material; see the [installed timber/infill guide](../../../Documentation/TIMBER_INFILL_KIT.md). L28 is open, without the earlier proposed shutters.

A flat hand-painted material atlas study for [L01–L36](../README.md), including the [timber, infill and roofing extension](../TIMBER_EXTENSION.md). The pale stone, cream daub and warm timber are intended to work together while retaining the quieter broad-painted surfaces of the existing Meadow Timber atlas. The paper-and-gouache component sheets communicate construction; this atlas communicates the in-game surface treatment.

- [Unlabelled flat atlas source](limestone-timber-trim-source.png).
- [Labelled trim-sheet guide](limestone-timber-trim-guide.png).
- [Exact source and guide prompts](../EXTENSION_PROMPTS.md).

The saved unlabelled source is **1254 × 1254 RGB**. It has not been upscaled to imply a higher native detail level. The later 2048 master mentioned below is an authoring target, not the current file resolution.

The labelled guide and allocation below remain **design references**. The source contains no intended text or presentation border; the guide adds region tags and an external legend. The installed timber/infill kit samples interior regions with verified UVs and its own normal companion. Full-strip repeat edges and the remaining proposed materials are not implemented tileable surfaces.

## Proposed allocation

The following rectangles specify the requested design allocation, with normalized coordinates measured from the **top left**. They are not measurements of texel-perfect generated boundaries. The target for a later authored master is 2048 × 2048; use the actual saved image dimensions when inspecting this concept source. The finished authored allocation needs exact cuts and padding before UV work.

| Zone | Material | X interval | Y interval | Typical use |
| --- | --- | --- | --- | --- |
| A | Coursed limestone | 0..1 | 0..0.125 | L01, low walls and retaining faces; rough-squared pale courses. |
| B | Dressed limestone | 0..1 | 0.125..0.25 | L02/L03 and simplified planar dressed masonry. |
| C | Hewn timber | 0..1 | 0.25..0.375 | Frame posts, rails, braces, L29/L30/L32/L34/L36. Continuous face, no plank seams. |
| D | Riven board face | 0..1 | 0.375..0.50 | L26 boards, fitted shutters and simple timber furniture/door faces. Geometry supplies board edges. |
| E | Plain daub/render | 0..0.50 | 0.50..0.75 | L25/L27/L28/L31 infill; no wooden framing painted into the patch. |
| F | Plain limestone face | 0..0.25 | 0.75..1 | Individually modeled lintels, voussoirs, coping, steps and piers; no extra masonry joints crossing stones. |
| G | End grain | 0.25..0.50 | 0.75..1 | Exposed beam, board and peg ends. Isolated islands, not repeating bands. |
| H | Cleft wood shingles | 0.50..1 | 0.50..0.75 | Broad roof face study for L33/L35. Grain runs down the roof slope. |
| I | Reed/straw thatch | 0.50..1 | 0.75..0.875 | Colour coordination with reused earlier roof option; existing installed thatch remains authoritative. |
| J | Limestone paving | 0.50..0.875 | 0.875..1 | Broad ground paving pattern for L05/L22 or simplified distant surfaces. |
| K | Beige joint fill | 0.875..1 | 0.875..1 | Recessed mortar/joint accents and unobtrusive utility colour. |

Horizontal strips A–D are intended to repeat **along U** after proper edge authoring. Their V bands must stay isolated. C/D grain runs horizontally on the image: rotate UV islands for vertical posts or boards so their long dimension still samples along the grain. Do not put the top of every post in the end-grain region by stretching its long side there.

H shingles have grain and water-shedding direction down the image/roof fall. Rows need phase continuity at adjoining roof modules. I has down-slope reed direction and should not be stretched crosswise into a woven wall. Render E is intentionally plain so geometry can carry different frame layouts without repeating a painted framework. F is deliberately free of joints: use it on geometry-defined single stones instead of mapping a miniature masonry wall across each lintel or arch wedge.

## Scale and repetition plan

A/B initially represent about 4 m along the wall; stone-course heights should be reviewed against the component sheets rather than stretching the same block texture arbitrarily across every surface. C/D initially represent 4 m of member length and approximately 0.50 m of face width per strip. Use a V subrange for a 0.15 or 0.25 m timber face. E can start around a 2 × 1 m surface area, F around 1 × 1 m of plain mineral colour and H around 2 × 1 m of roof surface; actual row size should be settled during shingle modeling. End grain has no repeating physical length contract.

These are authoring starting scales, not claims that generated texels already encode those dimensions. Roof overlap and silhouette should come from modeled or baked geometry. A planar shingle pattern alone cannot produce thick shingle edges or an authentic close-up roof profile. Likewise A/B are appropriate for simplified/baked faces; individually modeled stones should use F to avoid seams crossing their physical boundaries.

Internal patches E–K cannot repeat inside their own rectangle with ordinary whole-texture Repeat. Split larger surfaces into mapped sections, use a dedicated remapping shader, or export separate repeat tiles as a later authoring task. Full-width A–D can only use whole-texture U wrapping once their left/right edges actually match. No seamlessness claim is made for this generated source.

## Next authoring pass

Retain the existing Meadow Timber material wherever reuse looks consistent; the new C/D/G regions show a coordinated option, not a requirement to repaint installed wood. Reuse the existing roof material for thatch; do not replace the active thatch atlas with this small colour-study region. The combined atlas is useful for assessing a shared material palette, but a separate roof texture may be better when roof texel density, opacity or detail requirements differ.

Before import, create a precisely packed master with content rectangles and appropriate duplicated-edge gutters (a 16 px starting allowance at 2048), author and inspect repeat seams and low mips, and then record actual sampling coordinates. Pixel density must be checked on both a 4 m wall and a close-up trim member. Normal, roughness and AO should be authored or baked from controlled sources to the same final layout; painted albedo is not a reliable height field. The guide's letters and legend must never be sampled by meshes.

No frame-rate, import, seamlessness or runtime validation is claimed for these image-only proposals. The built-in image tool was used; exact prompts are saved alongside the component prompts.

The source and guide were visually reviewed for all eleven material regions, horizontal wood grain, down-slope shingle/thatch direction, clean render without painted framing and readable guide labels. The source is opaque RGB. The guide is a generated presentation with illustrative legend swatches; use the separate source for colour/packing inspection rather than extracting texture pixels or UV coordinates from the resized guide. Requested band heights are approximate in the generated source, reinforcing the need for an exact packing pass before modeling UVs.
