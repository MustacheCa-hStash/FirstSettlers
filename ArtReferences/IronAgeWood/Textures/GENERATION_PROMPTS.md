# Trim atlas generation prompts

Built-in image_gen generation and two targeted corrections. The user subsequently authorized the Python cleanup in finish_atlas.py; the finished export and validation are described in README.md. The retained source artwork below precedes that cleanup.

## Prompt 1

```text
Use case: stylized-concept.
Asset type: directly usable game BASE COLOR RGBA TRIM ATLAS, not a concept sheet, for the ordinary-wood modular building set of First Settlers. Produce a square 2048 x 2048 PNG texture if possible. Flat orthographic texture pixels filling entire image, no perspective, no margins, no text, no numbers, no arrows, no dividers, no page background, no objects displayed in a scene. Pixel artwork is high quality naturalistically painted game texture with restrained brushwork, muted warm medium brown ordinary timber, consistent neutral diffuse illumination, no sun highlights, no cast shadows, no dark vignette. No metal, stone, snow, fantasy wood tiers. Grain and fine axe tool marks only: do not draw plank joints or a collection of planks because actual geometry will form individual boards. Keep albedo variation subtle and avoid oversized black knots.
EXACT NORMALIZED RECTANGULAR LAYOUT, origin at image TOP LEFT:
- y=0.000 to 0.125, x=0 to1: one full-width strip of long horizontally flowing riven wood grain, warm weathered tan, small natural splits.
- y=0.125 to0.250, x=0 to1: one full-width strip of slightly darker riven grain with a different subtle pattern and sparse small knots.
- y=0.250 to0.375, x=0 to1: one full-width strip of axe-hewn beam wood, horizontal long grain and restrained flat tool marks, no strong shadows or bevel outlines.
- y=0.375 to0.500, x=0 to1: one full-width strip of thin fine bark / rough pole surface, its length and fibres flowing horizontally, medium brown to match the planks, not thick cracked old tree bark.
These FOUR TOP STRIPS each are exactly one-eighth of canvas height, 256 pixels tall at2048. They occupy top half and are fully OPAQUE, no separators. Each strip must independently be SEAMLESS LEFT TO RIGHT: at its left and right boundaries, colour, grain position, thickness and direction match so that repeating along U causes NO visible join. No requirement to tile vertically across different trim strips. Nominal each strip represents4m timber length and0.5m face width.
LOWER LEFT QUADRANT x=0 to0.5,y=0.5 to1 is entirely OPAQUE wood/joinery utility textures:
- x=0 to0.25,y=0.5 to0.75: square subdued end-grain field with fine age rings, one lightly off-centre core, as wood surface not a floating log disk, ALL corners filled with end-grain wood, no black or transparent circular surroundings.
- x=0.25 to0.5,y=0.5 to0.75: second subdued square end-grain field, slightly different rings, same colour family, all corners filled.
- x=0 to0.5,y=0.75 to0.875: continuous fine bast-fibre rope/lashing texture flowing horizontally, soft brown, no deep baked shadows, repeatable within this half-width band.
- x=0 to0.5,y=0.875 to1: continuous fine straight tight wood grain for peg sides and small joinery, opaque, same timber palette.
LOWER RIGHT QUADRANT x=0.5 to1,y=0.5 to1:
One full square seamless WATTLE WEAVE TILE of many slim ordinary brown wooden rods alternating over and under, mainly horizontal twig rows with slender woven vertical stakes, natural handwoven slight irregularities, NOT wicker furniture and not thick planks. Rods about1 to3cm nominal diameter in a2x2m tile, vertical stakes about4 to6cm, rows spaced about5 to8cm, horizontal and vertical continuity at all four edges of THIS QUADRANT. Need an airy but substantial weave, roughly20 to30percent genuinely empty open holes between rods. All actual holes in weave must have TRUE TRANSPARENT ALPHA, NOT white, grey, black or a checkerboard painted in. Rod surfaces opaque with clean finely antialiased edges and neutral natural colour. Top/bottom and left/right patterns match for this quadrant's repeat. This is the ONLY transparent region; all wood and utility rectangles must remain fully opaque. Wattle should use same wood colour palette as trim strips. No perimeter border around wattle.
Entire texture must respect exact rectangle boundaries and consistent density. No silhouette-cut plank tips: plank outline and height variation will come from 3D geometry, wood alpha stays solid. No material lighting gradients. Seamlessness important at each specified trim's horizontal boundaries and at four boundaries of wattle tile. No labels whatsoever in the texture.
```

## Prompt 2

```text
Edit this image as a GAME TEXTURE, not a display preview. Keep the exact four full-width horizontal strips in top half and lower-left end grain / rope / peg layout. Keep square proportions, all existing material colours and fine wood grain. Correct these two technical problems:
1. REAL ALPHA TRANSPARENCY. The checkerboard visible between wattle twigs in the lower right quadrant is mistakenly PAINTED into pixels. REMOVE ALL grey and white checker squares physically from that wattle region. Every empty opening between twigs must have RGBA alpha exactly0; only the wooden twigs themselves remain, with alpha255 and a small antialiased fringe. Everywhere in top half and lower left quadrant must be completely opaque alpha255. Do not make opaque wood faintly translucent. Do not paint any checkerboard, white background, grey background or dark background. Empty holes are transparent pixels. This is a production cutout PNG, with genuine transparent holes.
2. SEAMLESS REPEAT. In each of the four top strips separately, LEFT EDGE grain must join RIGHT EDGE grain precisely without a jump in grain paths or colour. In the wattle lower-right square, LEFT and RIGHT boundaries must meet the same rods at matching heights, thicknesses and directions, and TOP and BOTTOM must meet the same stakes at matching positions. Extend the weave through tile boundaries, NO perimeter stakes at tile edges, NO frame around tile. A periodic weave of slim twigs around slender stakes, naturally irregular within the period, about20percent empty holes. Preserve the quadrant size and overall layout.
Output an actual2048x2048 RGBA PNG if possible, no text or labels, NO checkerboard graphic anywhere. This is a texture image, not an explanatory diagram. Actual transparency only where wattle has empty holes; wood is solid.
```

## Prompt 3

```text
Edit this RGBA texture atlas to remove ONLY technical artifacts. Preserve all existing wood artwork, grain, rectangle layout, weave artwork and positions exactly. A previous transparent-background operation left bright red/yellow/green junk RGB visible in lower-right wattle gaps. Erase that coloured junk to genuine transparency: set ALL empty spaces between the brown wooden twigs to alpha0; no coloured speckles or checkerboards. Twig surfaces brown and alpha255. Clean cutout edges. Also force ALL pixels in upper half and lower-left quadrant opaque alpha255, since those are SOLID WOOD texture regions with no holes at all. No labels or lighting changes. Keep four top bands, two end grain squares, rope and peg bands, and lower-right weave quadrant. No new frame. The output must be a usable clean game texture with real alpha holes in wattle ONLY. Gaps should contain invisible natural brown RGB dilation at alpha0, never bright chroma key colours. This is the last targeted transparency cleanup. Please make the LEFT and RIGHT ends of each top strip naturally continuous, and weave tile boundaries periodic. Square PNG, true transparency.
```

