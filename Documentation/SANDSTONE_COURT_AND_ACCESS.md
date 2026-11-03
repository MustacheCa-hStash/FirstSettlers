# SS13 and SS15–SS24 sandstone extension

The requested eleven designs from the [sandstone catalog](../ArtReferences/SandstoneBuildKit/README.md) are installed, with a 2 m low wall, 2 m coping and plain coping turn as additional choices. SS12 and SS14 remain concepts. The [SS01–SS11 kit](SANDSTONE_SS01_SS11.md) retains its original source, meshes, material and textures.

- [Editable Blender source](../ArtReferences/SandstoneBuildKit/Extension/SandstoneExtension.blend).
- [Model dimensions, collision and triangle manifest](../ArtReferences/SandstoneBuildKit/Extension/sandstone-extension-models.json).
- [Unity overview](../ArtReferences/SandstoneBuildKit/Extension/extension-unity-overview.png), [actual instanced view](../ArtReferences/SandstoneBuildKit/Extension/extension-instanced.png) and [Blender overview](../ArtReferences/SandstoneBuildKit/Extension/extension-blender-overview.png).
- [Mounted SS23 shelf](../ArtReferences/SandstoneBuildKit/Extension/SS23-mounted-unity.png), [trough interior](../ArtReferences/SandstoneBuildKit/Extension/SS18-unity-back.png) and [drain](../ArtReferences/SandstoneBuildKit/Extension/SS22-unity-front.png).

## Pieces and geometry

All dimensions are metres. Counts describe each authored piece, before any temporary host-wall pocket fitting.

| Part | Dimensions | Category | Triangles |
| --- | --- | --- | ---: |
| SS13 low court wall | 4 W × 1.25 H × 0.50 D | Walls | 12 |
| SS15 alternating coping | 4 L × 0.25 H × 0.50 D | Walls | 68 |
| SS16 court gatepost | 0.75 square × 1.50 H, cap included | Posts | 20 |
| SS17 slab bench | 2 L × 0.50 H × 0.50 D | Furniture | 28 |
| SS18 carved trough | 2 L × 0.50 H × 0.75 D | Furniture | 28 |
| SS19 threshold steps | 1.50 W; 0.50 rise; 1 run | Stairs | 20 |
| SS20 half-storey stair | 1.50 W; 1.50 rise; 2 run | Stairs | 68 |
| SS21 battered terrace wall | 4 W × 1.50 H; 0.75 base / 0.50 top depth | Walls | 12 |
| SS22 open drain | 2 L × 0.25 H × 0.50 D | Furniture | 28 |
| SS23 corbel shelf | 1 W × 0.50 H; 0.50 exposed + 0.25 embedded depth | Furniture | 84 |
| SS24 hearth curb | 1.50 square outside × 0.25 H; 1 square clear centre | Furniture | 32 |
| **Requested eleven designs** | | | **400** |
| SS13 short court wall | 2 W × 1.25 H × 0.50 D | Walls | 12 |
| SS15 short coping | 2 L × 0.25 H × 0.50 D | Walls | 36 |
| SS15 coping turn | 1 × 1 plan envelope; 0.25 H; 0.50-wide L-shaped arms | Walls | 20 |
| **All fourteen choices** | | | **468** |

Runtime solids have one submesh each. Broad blockwork, grain, shallow tool marks and cut edges are baked from independently editable sources. Geometry carries the alternating coping skyline, gatepost cap chamfer, bench gap, trough basin, stair treads, retaining batter, open channel, stepped corbels and open hearth ring. Connected render shells omit hidden interfaces between constituent solids. The shelf's stepped supports account for its larger count.

The source helper reuses the original kit's authoring functions and orange palette; it does not execute the original piece-generation loop or overwrite that kit's atlas. The extension has its own 2048-square `SandstoneExtension_ColourAO.png` and `SandstoneExtension_Normal.png`, sampled by one `SandstoneExtension.mat` using `Custom/BakedStoneWallMatte`. Colour excludes direct/indirect illumination; alpha contains AO. Imports use trilinear mipmaps, Clamp, 4× anisotropy, BC7 colour/AO and BC5 normal compression on Standalone: approximately 10.7 MiB compressed with mipmaps for this pair. The orange albedo and procedural sedimentary detail match SS01–SS11.

## Placement and collision

SS13 and SS15 start at their lower span endpoint, on the depth centreline. SS13 spans Z=−0.25..+0.25. SS15 adds its full 0.25 m above the wall: root Y=1.25 gives a combined 1.50 m top. Taller caps reach 0.25 m, flatter caps 0.1875 m; neither exceeds the nominal envelope. Both lengths have finished end faces. The turn is an explicitly placed L-shaped cap, rather than automatic corner reshaping: plan points X/Z=(0,−0.25), (1,−0.25), (1,0.75), (0.5,0.75), (0.5,0.25), (0,0.25). Join straight runs at its open end planes and reserve its full plan footprint; avoid overlapping straight caps beneath it.

SS16 starts at its base and is centred in X/Z. Its chamfered cap is inside the 1.50 m height and 0.75 m width. It has one convex collider. SS21 is centred about Z=0, with a vertical back at Z=−0.375 and exposed face Z=0.375−Y/6. One convex hull preserves the sloping face. Low court walls and the terrace wall retain terrain-seating hints, without support or soil simulation.

SS17, SS18, SS22 and SS24 start at a lower footprint corner with X/Z extending positively. SS17 uses two 0.25 m-wide feet and a seat occupying Y=0.375..0.50; three boxes preserve the space between them. SS18's 0.125 m rims surround a 1.75 × 0.50 m basin with floor at Y=0.25. Five boxes leave the basin clear. SS22's channel is open along +X, between Z=0.125..0.375, with bottom at Y=0.125. Three boxes leave both ends open. SS24 uses four boxes around an entirely open centre; the mesh has no hidden floor slab.

SS19 and SS20 start at the low walking datum, span X=0..1.50 and ascend along +Z. SS19 has two 0.25 risers / 0.50 goings; SS20 has eight 0.1875 risers / 0.25 goings. Solid stepped occupancy describes the masonry mass. An independent 8-triangle continuous convex ramp and `SmoothWalkSurface` provide walking collision, including endpoint continuation and two half-flights reaching a 3 m storey. No handrails are included.

All pieces use the current free-building rules, 0.25 m placement grid and eight yaw headings. There are no new support or collapse requirements. The hearth is a curb only; trough and drain shapes do not add fluid behavior, and the retaining wall does not edit terrain. Furniture-category entries use the existing surface placement path. No interaction, recipe, resource-processing system or new durability tier is added.

## SS23 embedded tails and reversible fitting

SS23's root is at the bottom-left of its mating wall face, local Z=0. The exposed slab occupies X=0..1, Y=0.375..0.50, Z=0..0.50. Two stepped corbels have 0.25 m-wide embedded tails at X=0.125..0.375 and 0.625..0.875, Y=0..0.375, Z=−0.25..0. The total reserved depth is 0.75 m. Aim at a compatible wall's broad face to mount it at that face's persistent placement frame.

`BuildCorbelPocket` fits both broad faces of aligned, plain solid masonry at least 0.50 m thick, including SS01–SS03 and SS13. Eligibility requires one full logical solid box, the baked-stone material path and enough actual wall height/width for both tails. It excludes shaped hulls, doorways, thin limestone walls, half-gables, thin niche backing and partial seats. Arbitrary free placement remains available; it does not imply that every host receives pockets.

For a compatible mount, the derived wall mesh suppresses only the two filled tail patches, interpolating its original UVs/normals. Host collision is cut in all three dimensions. Tail geometry fills the pockets, so hidden cavity caps are omitted. Canonical wall assets, placement origins and storey sockets remain unchanged. New fitted geometry is cached by source mesh and sorted local cuts, with no per-frame Boolean operations. Authored counts above exclude these local fitted-wall triangles and collision boxes.

Adding either the wall or shelf first gives the same fit. Duplicate tail volumes share one cut; removing one of two overlapping shelves retains the occupied pockets. Removing the final shelf restores the original wall mesh and box collision. Preview fitting is derived separately and does not permanently cut the wall. The shelf stays freely placed if its wall is removed.

## Rebuild, installation and checks

Run Blender from the repository with `--background --factory-startup --python-exit-code 1 --python Tools/BuildSandstoneExtension.py`. This regenerates only the extension source, FBXs, baked maps and manifest. Preserve manual source edits before rebuilding. Then run `Tools/ValidateSandstoneExtension.ps1 -Install`; Unity needs access to its licensing service. For editor installation use **Tools → Building → Install SS13 SS15-SS24 Sandstone**. The full modular rebuild also includes these assets when available. Restart Play mode before opening the updated **B** catalog.

The selective installer verifies source/FBX/texture/code agreement with the isolated validation project, copies only this family's assets and appends fourteen missing catalog references without replacing other catalog bytes or saved scenes. Stable IDs use `build.stone.ss...` for construction and `build.furniture.ss...` for the bench, trough, drain, shelf and hearth. SS12 and SS14 are not introduced by this extension.

The installed run passed **6,152 extension checks**, including all fourteen choices at eight headings, hollow collision profiles, short/full coping heights, 112 real CharacterMotor stair traversals and 32 reversible wall-pocket cases. Compilation, source winding, UV/tangent checks, triangle budgets, repeat-install GUID stability, existing definition preservation, shared material/import settings, prefabs and icons passed. Native URP and instanced captures were reviewed, including the trough interior and mounted shelf.

Existing regressions also passed: **2,848 SS01–SS11 checks**, **3,622 ordinary-stone checks**, **28,311 modular-wood checks** and **8,696 building-interaction checks**. The latter includes the existing 513-piece village benchmark; its measured full-session median was 1.3164 ms in this run, with local-preview p95 0.0079 ms. These measurements describe this validation run, not a game-wide performance guarantee.

Evidence: [extension](../ArtReferences/SandstoneBuildKit/Extension/unity-validation.json), [SS01–SS11](../ArtReferences/SandstoneBuildKit/Extension/ss01-ss11-regression.json), [ordinary stone](../ArtReferences/SandstoneBuildKit/Extension/stone-regression.json), [wood](../ArtReferences/SandstoneBuildKit/Extension/wood-regression.json) and [building interaction](../ArtReferences/SandstoneBuildKit/Extension/interaction-regression.json).
