# Baked mounted torch and sleeping platform

The mounted torch is **280 triangles**, reduced from 1,996 (86%). Its two 840-triangle spiral bindings are eight-sided sleeves. The five separate fuel splints, locating pegs and diagonal brace remain geometry; the holder uses two plain boxes. Rope turns, wood grain, bevel lighting and contact shading come from a 512×512 colour/AO and tangent normal pair.

The sleeping platform is **320 triangles**, reduced from 1,472 (78%). Eight plain frame boxes cost 96 triangles. Its 19 long and 37 cross cords are narrow double-faced strips costing 224 triangles. Both cord courses, the gaps between them, and the open space below the bed remain physical geometry. A 1,024×1,024 colour/AO and tangent normal pair captures the original frame and rounded cord shading. The strips become flat when viewed exactly along their edges; normal maps cannot restore the original cylindrical silhouette there.

Both pieces use the existing baked matte shader: one submesh and one instanced material per piece, with no cutout, transparency or parallax. Colour alpha stores AO. The normal map adds one sample per shaded pixel using the same lighting path as the simplified walls. Standalone BC7 colour/AO and BC5 normals use about 3.33 MiB together including mipmaps. Original shared timber and hand-torch materials remain available to other pieces.

Only mesh and material references change in the installed definitions and prefabs. Original prefab IDs, collision boxes, placement bounds, support points, health, flame prefab, emitter and flame anchors are retained. The six-piece furniture installer and mounted torch installer both route these two models back to their baked assets during future rebuilds.

## Reproduce and validate

1. Run Blender with `--background --python Tools/BakeTorchAndBed.py`.
2. Run `Tools/ValidateBakedFurnishings.ps1 -Install` from PowerShell. It prepares an isolated Unity project, checks meshes and renders, runs the furniture Play-mode suite and the local flame Play-mode suite, then copies the validated visual references back.

The original Blender sources are retained and hash-checked by the baker. Evidence is in `ArtReferences/BakedFurnishings`: before/after renders, underside views, normal-disabled comparisons, validation reports, and the complete installed catalog triangle audit. Counts are triangles, not Blender quads. Catalog counts cover base meshes and listed variants; optional procedural junction/bearing meshes and animated flame cards are additional.

## Remaining catalog pieces above 50 triangles

| Build piece | Triangles |
|---|---:|
| Pegged table | 628 |
| Low work table | 628 |
| Timber floor with stairwell | 480 |
| W11 shutter wall | 384 |
| Timber floor | 324 |
| Sleeping platform | 320 |
| Three-leg stool | 296 |
| W09 doorway wall | 288 |
| Mounted torch | 280 |
| Plank bench | 276 |
| W22 full-storey stair | 240 |
| Wall shelf | 212 |
| Split-plank wall | 136 |
| Square wall infill | 132 |
| W21 half-storey stair | 120 |
| W15 half-gable, rising or falling | 116 each |
| S06 stone wall cap | 72 |
| Thatch roof, including continuation variant | 62 each |

The best next candidates are the tables, stool, bench and shelf: bevels and small wooden details can move into bakes. The opening walls and square infill can use the same panel-and-frame approach as the plank wall while keeping real openings. Floors can bake board detail while retaining the perimeter, stairwell and underside supports. The roof, cap and stairs are already modest; most remaining triangles describe useful edges, separate solids or step profiles.
