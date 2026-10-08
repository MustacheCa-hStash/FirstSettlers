# Ordinary wood trim atlas

This sheet is designed for one split-plank layer with three inset rails on one face, and a separate wattle weave made with alpha-clipped visual geometry. Source artwork was generated with the built-in image_gen tool. No game scripts, meshes, prefabs or scene assets are changed.

## Files and status

- [ordinary-wood-trim-albedo.png](ordinary-wood-trim-albedo.png): finished 2048 x 2048 RGBA base-colour atlas with solid wood alpha, genuine wattle holes, continuous repeat boundaries and 16-pixel sampling gutters.
- [trim-labelled-preview.png](trim-labelled-preview.png): labelled visual guide. Labels and checkerboard appear only in this preview, not in the material texture.
- [wattle-tile-albedo.png](wattle-tile-albedo.png): optional standalone 1024 x 1024 weave texture, repeating in both axes. Use the atlas for a wattle definition that combines rails and weave in one material; this standalone version is convenient for separate weave materials or Blender previewing.
- [repeat-check-preview.png](repeat-check-preview.png): wood repeated three times, and wattle repeated twice in both axes.
- [wattle-before-after-preview.png](wattle-before-after-preview.png): previous and corrected wattle masks on dark and light backgrounds. The corrected mask restores the original twig outlines rather than cutting into wood pixels.
- [uv-layout.svg](uv-layout.svg): separate conceptual layout diagram; use the finished sampling coordinates below for the padded export.
- [atlas-layout.json](atlas-layout.json): exact content rectangles, sampling UVs and validation results. [finish_atlas.py](finish_atlas.py) reproduces the authorized cleanup with Pillow and NumPy.
- `ordinary-wood-trim-design.png`: retained generated source, 1254 x 1254 RGBA. The finished atlas is upscaled from this source; 2048 export size does not imply newly captured 2048-resolution detail. Source mask defects are corrected in the finished files.
- `ordinary-wood-trim-original.png`: original artwork used to rebuild the wattle mask in revision 2. It contains a painted checkerboard, so its RGB is used to recover the twig silhouettes; its source alpha is not used.
- `ordinary-wood-trim-albedo-v1.png` and `wattle-tile-albedo-v1.png`: preserved previous exports for comparison. The primary filenames contain the corrected revision 2 textures.
- Only base colour and alpha are being delivered in this pass. Normal/roughness maps should be authored or baked to this same layout later; colour is not a reliable physical height field.

## Layout and UV coordinates

UV origin is bottom-left as in Blender. Region coordinates describe the design rectangles; sample inside their boundaries to avoid filtering into neighbours. The top four full-width strips have grain running along U. Rotate board UV islands so their long dimension follows U even when the board stands vertically in the model.

| Region | U interval | V interval | Intended use |
| --- | --- | --- | --- |
| A: riven grain | 0..1 | 0.875..1 | Main plank faces, restrained splits |
| B: alternate riven grain | 0..1 | 0.750..0.875 | Different plank grain and small knots |
| C: hewn timber | 0..1 | 0.625..0.750 | Three rails, beams, floor edges |
| D: pole/bark | 0..1 | 0.500..0.625 | Posts, wattle stakes and roundwood |
| E: end grain 1 | 0..0.25 | 0.25..0.50 | Exposed plank/beam caps |
| F: end grain 2 | 0.25..0.50 | 0.25..0.50 | Alternate caps |
| G: fibre lashing | 0..0.50 | 0.125..0.25 | Small rope wraps; not entire boards |
| H: tight grain | 0..0.50 | 0..0.125 | Peg sides and small joinery |
| I: wattle weave | 0.50..1 | 0..0.50 | Cutout weave sheet |

Planned repeat scale: wood strip U=0..1 represents 4 m of length; a 0.125-high strip represents about 0.5 m of face width before sampling gutters. Wattle patch represents a nominal 2 x 2 m weave tile. These are authoring scales, not bounds imposed by the build grid. Avoid tiling the end-grain patches.

## Finished sampling coordinates

The table above describes allocation rectangles. The table below gives **content texel-centre bounds after 16-pixel padding**, in Blender bottom-left UV coordinates. Use these to keep UV islands out of the gutters. A through D can use U=0..1 for whole-width horizontal Repeat, while keeping V in the listed content interval. For internal regions, map each UV section into the listed content bounds; global Repeat cannot repeat an internal patch.

| Region | U min | V min | U max | V max |
| --- | --- | --- | --- | --- |
| A | 0.000244 | 0.883057 | 0.999756 | 0.991943 |
| B | 0.000244 | 0.758057 | 0.999756 | 0.866943 |
| C | 0.000244 | 0.633057 | 0.999756 | 0.741943 |
| D | 0.000244 | 0.508057 | 0.999756 | 0.616943 |
| E | 0.008057 | 0.258057 | 0.241943 | 0.491943 |
| F | 0.258057 | 0.258057 | 0.491943 | 0.491943 |
| G | 0.008057 | 0.133057 | 0.491943 | 0.241943 |
| H | 0.008057 | 0.008057 | 0.491943 | 0.116943 |
| I | 0.508057 | 0.008057 | 0.991943 | 0.491943 |

The content, excluding its gutters, represents the nominal face width or weave size. For example, a 0.22 m-wide plank uses about 44% of the chosen wood band's content V height; a 0.15 m rail face uses about 30%. After rotating the UV island, put its long dimension along U.

## How repetition works

The entire atlas is not a vertically seamless texture. Different materials meet at the strip boundaries. A wood strip needs its left/right ends to join; keep its UV V inside the strip and Repeat U. End grain uses isolated UV islands. The half-width lashing strip can repeat only within its own rectangle, not with global Repeat alone.

The wattle region also cannot automatically repeat within its rectangle using Unity's whole-texture Repeat setting. For the current single-material renderer, divide the weave sheet into UV sections and map each to the patch, without generating the individual twigs. A 3.5 x 2.75 m wall at a 2 m tile scale uses four rectangular sections: widths 2 and 1.5 m, heights 2 and 0.75 m. Full sections use the complete patch; partial sections use the matching first 75% of U or 37.5% of V. Align the pattern phase at joins. This adds a handful of triangles, not twig geometry. A dedicated atlas-remapping shader or separate tile texture is a future alternative.

The sampling gutters in a finished atlas reduce neighbouring-region bleed but do not remove atlas mixing at every possible tiny mip level. Check the wall at gameplay distances; very distant building LODs are not implemented in the current renderer.

## First Blender wall

Use metre-scale coordinates. After export to Unity Y-up, logical wall bounds remain X=0..3.5, Y=0..2.75, Z=0..0.25. Keep the root at the lower corner and apply object scale before export.

Suggested starting cross-section, measured across Unity Z: plank layer 0.05..0.13 m; rails 0.10..0.25 m. That embeds 0.03 m of each rail into the planks and leaves 0.12 m exposed on the rail face. The whole piece still fits the 0.25 m envelope. These dimensions are a visual starting point, not an extra collider requirement.

Use around sixteen simple plank prisms with widths summing to the panel width (accounting for any tiny seams), varying top heights between about 2.69 and 2.75 m. Reserve the full 2.75 m height in the BuildDefinition. Start with rails 0.15 m high centred around Y=0.35, 1.35 and 2.35 m; adjust visually. Keep their ends inside X=0..3.5. Add restrained single-segment bevels, grain-aligned UVs and end-grain caps. Avoid reusing one conspicuous knot in every plank.

A 2.75 m-long plank uses about 0.6875 of a 4 m grain repeat. A 3.5 m rail uses about 0.875. Width can be sampled from a sub-band. Use other U offsets for variation; a finalized horizontal repeat allows offsets to wrap.

Export one joined mesh with one material slot. Disconnected islands are fine; welding all plank/rail intersections is unnecessary. Keep an editable Blender copy. Actual plank height and silhouette changes come from geometry; plank alpha remains solid.

## Wattle and Unity material settings

Use actual rails/stakes plus one weave sheet. A lightly bent sheet can improve grazing-angle appearance. Avoid an opaque backing slab behind it, or the alpha holes will reveal that slab rather than the world. Alpha holes remain visual: the current logical wall and gameplay proxy still occupy the full wall box.

For the plank definition: URP Lit, Opaque, Alpha Clipping off, metallic 0, low smoothness (start near 0.15), instancing enabled. Base-map tint should start white so it does not double-tint the brown artwork. The current brown prototype tint would darken it.

For the wattle definition: use the same atlas with a different material, Opaque surface, Alpha Clipping on, cutoff initially 0.5, Render Face Both, instancing enabled. Rails/stakes sample solid wood regions; weave samples region I. This is still one material and submesh per definition, matching the existing renderer. Both-sided rendering does not guarantee ideal back-face normal-map lighting; inspect both sides after introducing a normal map.

Texture importer: Default texture, sRGB enabled for base colour, input texture alpha, Alpha Is Transparency enabled, mipmaps on, trilinear filtering; anisotropy can start at 4. If available, Preserve Coverage with alpha cutoff matching the material helps maintain the weave's alpha coverage through mipmaps. Wood's opaque material ignores this alpha. Separate later normal maps use Normal Map import; scalar data maps use linear/non-sRGB import.

Sources: [Unity default texture import settings](https://docs.unity3d.com/6000.0/Documentation/Manual/texture-type-default.html), [Unity URP Lit material settings](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/lit-shader.html), and [Blender bake margin guidance](https://docs.blender.org/UATEST/manual/en/4.5/render/cycles/baking.html). Padding and mipmaps need testing together; they are not substitutes for periodic texture boundaries.

## Validation

The Python cleanup was explicitly authorized by the user. Revision 2 fixes the overly aggressive wattle mask in revision 1. It rebuilds the twig silhouettes from the original artwork's brown wood against its neutral checkerboard, closes pinholes, and lightly smooths contour pixels. It does not cut into twig highlights using the repaired source's unreliable alpha or chroma thresholds. Trustworthy brown pixels seed RGB dilation, keeping checkerboard colour out of antialiased edges. Seam repair restores solid silhouettes and limits alpha feathering to their contours. Padding is extruded for wood/caps and wraps around the wattle content.

Pixel assertions passed: all wood/utility alpha is 255; the four wood strips have exactly matching left/right edge pixels; the atlas wattle content and standalone wattle tile have exactly matching RGBA pixels on both pairs of opposing edges. At alpha cutoff 0.5, approximately 20.3% of the corrected atlas wattle content is open. Wood and utility pixels are byte-for-byte unchanged from revision 1; atlas layout and sampling coordinates are also unchanged. The corrected weave was visually checked against dark, light and checkerboard backgrounds and in a tiled preview. Results and exact coordinates are recorded in `atlas-layout.json`, with the before/after mask measurements in `wattle-revision-validation.json`.

The output is an upscaled painted base-colour reference texture. Matching edge pixels establish continuity, not the absence of recognizable repeated knots or weave. No Unity material import, live lighting or performance test was run. The atlas retains the low-mip limitations described above.

Exact prompts are saved in `GENERATION_PROMPTS.md`. No normal or roughness map is inferred from a painted albedo image in this pass.

