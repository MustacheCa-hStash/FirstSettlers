# Generation prompts

Mode: built-in image_gen; two independent style-transfer calls. Source art generated at 1254 x 1254; packed exports are 2048 x 2048 and do not add native source detail.

## Soft hewn oak

```text
Use case: style-transfer.
Asset type: unlit base-color wood trim-sheet artwork for First Settlers, square 2048 by 2048.
Input images: Image 1 is the EXISTING texture atlas and strict layout reference, edit target. Image 2 and Image 3 are gameplay screenshots, STYLE references only.
Primary request: Make a new "Soft hewn oak" version of Image 1 that belongs with the game's simplified, low-poly trees and rocks. Remove ALL photographic pore detail, tiny scratches and micro-noise. Broad matte warm muted oak brown color masses, only a few long slightly angular grain grooves per band, quiet midtones, understated highlights, small sparse knots. Almost flat hand-painted color, not lavish fantasy painterly texture.
Composition/UV contract: Entire canvas is the texture, no frame or margins or text. Preserve the EXACT normalized allocation of Image 1. Top half is exactly FOUR full-width horizontal bands of height 1/8 canvas each: A riven timber at y=0..1/8, B alternate riven timber at y=1/8..1/4, C hewn timber at y=1/4..3/8, D bark at y=3/8..1/2. Wood grain flows horizontally, no vertical plank seams in any band, no perspective, NO pre-painted board divisions, nails or joints. B especially should have calm readable low-contrast grain across its full height, because mesh UVs crop arbitrary subrectangles here. C restrained broad axe facets, D simplified angular bark.
Bottom-left quarter: E and F each occupy a 1/4 by 1/4 square side by side at y=1/2..3/4, two endgrain patches with only a few broad rings and no drawn circular outer silhouette. G fiber lashing occupies left half at y=3/4..7/8, H fine wood occupies left half at y=7/8..1.
Bottom-right quarter I at x=1/2..1,y=1/2..1: keep existing wattle silhouettes and holes exactly unchanged; don't invent backgrounds or alter the region.
Keep all material boundaries exactly at these positions; no gaps between regions. Do not copy the original's smeared gutters: artwork should run to each region boundary. Exact sampling gutters will be packed externally. Make horizontal strip edges join seamlessly left to right. Diffuse albedo only, uniform neutral illumination, no cast shadows, no baked shine, no vignettes, no labels, no watermark. Wood regions must be opaque.
```

## Weathered woodland

```text
Use case: style-transfer.
Asset type: unlit base-color wood trim-sheet artwork for First Settlers, square 2048 by 2048.
Input images: Image 1 is the EXISTING texture atlas and strict layout reference, edit target. Image 2 and Image 3 are gameplay screenshots, STYLE references only.
Primary request: Make a new "Weathered woodland timber" version of Image 1 that belongs with the game's simplified, low-poly trees and rocks. Remove ALL photographic pore detail, tiny scratches and micro-noise. Matte desaturated medium taupe-brown with cool grey undertones, matching the simplified grey-brown bark in the forest screenshot. Very quiet broad color planes, few subtle long grain grooves, low contrast, almost no knots. Grain and toolmarks should be TWO TIMES QUIETER than a usual stylized game texture. No orange or honey-yellow palette. Sparse angular grain; flat color masses, not lavish fantasy painterly texture. Endgrain should have only 5 to 7 broad subdued rings, no radial cracks. Hewn and bark regions likewise broad and quiet.
Composition/UV contract: Entire canvas is the texture, no frame or margins or text. Preserve the EXACT normalized allocation of Image 1. Top half is exactly FOUR full-width horizontal bands of height 1/8 canvas each: A riven timber at y=0..1/8, B alternate riven timber at y=1/8..1/4, C hewn timber at y=1/4..3/8, D bark at y=3/8..1/2. Wood grain flows horizontally, no vertical plank seams in any band, no perspective, NO pre-painted board divisions, nails or joints. B especially should have calm readable low-contrast grain across its full height, because mesh UVs crop arbitrary subrectangles here. C restrained broad axe facets, D simplified angular bark.
Bottom-left quarter: E and F each occupy a 1/4 by 1/4 square side by side at y=1/2..3/4, two endgrain patches with only a few broad rings and no drawn circular outer silhouette. G fiber lashing occupies left half at y=3/4..7/8, H fine wood occupies left half at y=7/8..1.
Bottom-right quarter I at x=1/2..1,y=1/2..1: keep existing wattle silhouettes and holes exactly unchanged; don't invent backgrounds or alter the region.
Keep all material boundaries exactly at these positions; no gaps between regions. Do not copy the original's smeared gutters: artwork should run to each region boundary. Exact sampling gutters will be packed externally. Make horizontal strip edges join seamlessly left to right. Diffuse albedo only, uniform neutral illumination, no cast shadows, no baked shine, no vignettes, no labels, no watermark. Wood regions must be opaque.
```

