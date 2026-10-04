# Snowy spruce

Spruce grows in snowy boreal and subalpine forests. True tundra generally lies
beyond the treeline: https://www.nps.gov/subjects/nnlandmarks/boreal.htm and
https://www.nps.gov/romo/learn/nature/alpine_tundra_ecosystem.htm.

The enabled build scene, `Assets/Scenes/SmearScene.unity`, uses
`Spruce_LOD0_v06.prefab` as its fallback instance tree. The spruce species slot
now explicitly references that same prefab. Its mesh, UVs, transforms and bark
material are unchanged. Its leaf material is `SpruceTreeLeaf_M.mat`, whose active
`_BaseMap` is `leafCard_Spruce_03.png` (not the legacy `_MainTex` entry).

`leafCard_Spruce_03_Snow.png` is the snowy sibling created with the built-in
imagegen tool from that active leaf card. It supplies snow color/coverage through
`_SnowMap`; the original `_BaseMap` remains authoritative for exposed needles.
Broad, smooth snow pillows bridge spaces between needles and nearby twigs within
each existing leaf card. Large gaps between the snow-covered fans and dark green
needle fringes remain open. Snow is identified by pale RGB regions and its alpha is unioned with
the original needle alpha in both the forward and shadow passes. It cannot remove
original needles and has no effect at zero coverage. No mesh, UV or prefab geometry
is changed. Snow-only pixels normalize their color against the union alpha to
avoid black edges. Snow color is blended after needle tinting and suppresses green
needle transmission; its lighting normal tilts upward to suggest a settled cap.
Uncovered needles receive a modest cool blue-green tint. Snowy instances enable
alpha-clipped shadows, including the cap shapes.

The generation pass produces at most eight spruce per logical chunk, in sparse
stands on Snow surfaces. Moisture must exceed .35, temperature must exceed .10,
slopes must be below 35 degrees, and river masks below .32. Trees stay above water
and below the existing normalized mountain treeline (.8), with density fading
above .62. Tundra and extreme cold remain treeless. Both the dense and sparse
planners use the same candidate order, habitat rules, spacing, scale and snow
coverage. Near foliage accepts the planned spruce on Snow surfaces. Coverage is
carried into `TreeInstanceData` and reset on every pooled-instance rebuild.
Impostor materials and rendering are unchanged and ignore this coverage.

Taiga has a separate bare-spruce placement pass. Previously this cool, wet band
between Snow and Forest was classified correctly but was excluded from every
tree pass, leaving broad treeless areas. Taiga now shares the forest stand noise,
18-tree chunk budget, slope limit (45 degrees), river exclusion (.64), and spruce
spacing. Its terrain classification and Forest/Grassland groundcover ownership
remain unchanged. Both near and distant planners run the same pass.
`TaigaTreeValidation.RunBatch` checks suitable/excluded habitats, placement parity,
the saved scene's Taiga chunks, and existing forest, transition and snow regressions.

Restart Play Mode or use World Manager's Regenerate Terrain to see regenerated
placements. `Tools > Terrain > Validate Snowy Spruce` checks habitat exclusions,
dense/sparse agreement, near-instance creation, pool resets, shader compilation,
and rendered cap coverage, white snow color, and retention of original needles. Batch entry point:
`SpruceSnowValidation.RunBatch` (also runs existing distant-tree correctness checks).

## Image generation prompts

Generated with the built-in imagegen tool. First, the original leaf card was the
edit target and the user's two LEGO Fortnite images were cap style references:

Use case: lighting-weather. Asset type: a flat square RGBA spruce branch leaf-card texture for a stylized 3D game. Image 1 is the EDIT TARGET, the existing white silhouette atlas. Images 2 and 3 are ONLY style/shape references for accumulated snow: broad smooth pillows bridging foliage with a ragged dark-green fringe underneath. Do not reproduce the game scene, people, ice, icicles, background or text. Keep EXACT UV alignment: the main stem starts at bottom-left and runs diagonally up/right; each twig and needle remains in exactly the target image's position. The tree mesh stays unchanged. Paint exposed needles deep cool green. Replace thin streaks with LARGE CONNECTED MATTE WHITE SNOW CAPS lying across each twig's upper fan of needles. Snow is allowed and REQUIRED to BRIDGE small transparent spaces BETWEEN NEIGHBORING NEEDLES on each individual twig, forming smooth chunky scalloped white sheets with a gently rounded upper surface and soft pale blue undersides. On the long upright and rightward twig fans, cover about the middle/upper 65 percent of each fan with a broad continuous cushion: large areas of WHITE MASS, NOT individual needle stripes or a thin central stem stripe. Many snow cap boundaries should form a continuous rounded edge spanning ten or more adjacent needles; leave irregular sharp green needle tips and lower fringes poking from underneath around the outer bottom edge. Keep major empty spaces BETWEEN separate twigs transparent, keep the lower main stalk partially exposed green/brown, don't flood-fill the whole rectangular card, do not make a solid triangular tree. A few small green windows in snow cushions, not lots of holes. Very restrained powder microtexture, no noisy flecks, no narrow white needle tracing, no sparkles, no distinct per-needle snow dashes. Style inspired by the smooth snow-laden evergreen branches in the two reference images, with simple clean shapes and broad white caps, almost no fine grain. Shape and layout must fit existing branch positions and square crop. Transparent background must include the new broad snow cap shapes; preserve original needle positions and alpha at exposed green fringes, while snow alpha fills the small inter-needle spaces beneath the snow cushions. Do not rotate or shift target branch, no new branch stems, no new geometry, no cast shadows, no text.

A final edit widened these caps using the user's close-up snow reference:

Use case: lighting-weather. EDIT TARGET Image 1 is the current square spruce leaf-card texture with snow. Image 2 is only a STYLE REFERENCE for thick broad evergreen snow blankets. Retain exact image-1 twig/needle positions, square crop and UV alignment. This is a texture on a dense 3D spruce: narrow snow caps down individual stems still look like thin white streaks when mapped, so this edit must drastically INCREASE THE WIDTH OF SOLID SNOW MASSES. Change snow shapes only. Turn narrow spinal snow into a FEW VERY BROAD, SMOOTH CONNECTED BLANKETS OVER WHOLE TWIG FANS. Let neighboring twigs merge under snow. Required: opaque snow must FILL MOST SMALL AND MEDIUM TRANSPARENT HOLES BETWEEN ADJACENT TWIGS within each frond fan. In the upper-right branched crown, create one broad soft triangular scalloped snow pillow covering most of the canopy fan, with irregular dark green needle fringes projecting from its lower edge. Across the middle-right horizontal fans form a thick wide blanket spanning the space between the two neighboring rows of twigs. Across the lower-right fans form another broad white pillow spanning the entire gap between nearby twigs. On the tall upright twig left of center, form a broad continuous rounded snow mantle much wider than the stem, filling the spaces between opposite needles. Snow masses should occupy about 55 percent of the whole square image area, and total opaque foliage plus snow about 70 percent; substantial newly opaque inter-twig spaces are essential. Keep a few large transparent gaps separating these blankets, keeping the overall branched radial frond shape, not a solid rectangle. Main stem from bottom-left still exposed on lower-left. Exposed dark green leaf tips and fringes must be retained at the snow blanket's LOWER OUTER EDGE. Smooth chunky white silhouettes, very slight pale-blue underside shading, almost no granular noise or narrow zigzag ridges, no individual needle white lines, no individual skinny stem stripes, no spotty dots, no ice or icicles. Snow alpha should be fully opaque throughout the broad smooth blankets and transparent in the remaining large gaps. Match the reference's thick pillowy snow layered across branches. Flat stylized game RGBA texture; no 3D scene, no whole tree, no characters, no text. The model geometry stays fixed, so this texture's larger smooth alpha shapes create the caps. Do not shrink or rotate the original plant layout.

The tool returned a 1254 by 1254 square image. Unity imports it with the original
texture's import settings and normalized UVs. Needle alpha remains intact and
snow alpha bridges the gaps beneath the new caps.
