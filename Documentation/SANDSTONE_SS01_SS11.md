# SS01–SS11 sandstone building kit

Eleven pieces from the [sandstone catalog](../ArtReferences/SandstoneBuildKit/README.md), with an [editable Blender source](../ArtReferences/SandstoneBuildKit/Models/SandstoneBuildKit.blend), twelve metre-scale FBXs, one shared baked colour/AO and normal pair, Unity definitions, prefabs, physical collision and menu thumbnails. SS11 includes both slope representations under one menu choice.

| Part | Dimensions, metres | Category | Runtime triangles |
| --- | --- | --- | ---: |
| SS01 bedded sandstone wall | 4 W × 3 H × 0.50 D | Walls | 12 |
| SS02 dressed sandstone wall | 4 W × 3 H × 0.50 D | Walls | 12 |
| SS03 narrow sandstone wall | 2 W × 3 H × 0.50 D | Walls | 12 |
| SS04 square sandstone pier | 0.50 W × 3 H × 0.50 D | Posts | 12 |
| SS05 flagstone paving | 4 × 4 footprint; 0.25 deep | Floors | 12 |
| SS06 strip footing | 4 L × 0.50 H × 1 D | Foundations | 12 |
| SS07 lintel doorway | 4 W × 3 H × 0.50 D | Walls | 28 |
| SS08 splayed window | 4 W × 3 H × 0.50 D | Walls | 32 |
| SS09 storage niche wall | 4 W × 3 H × 0.50 D | Walls | 28 |
| SS10 upright slab partition | 2 W × 1.50 H × 0.25 D | Walls | 12 |
| SS11 sandstone half-gable | 2 base × 2 rise × 0.50 D | Roofs | 8 |
| **One of each selectable piece** | | | **180** |
| SS11 opposite slope | Same envelope; selected with R | Same menu choice | 8 |

The twelve exported representations total 188 triangles. Every piece has one submesh and shares one instanced material. Detail is baked rather than drawn as individual runtime stones. There are no per-block renderers or duplicate hidden interfaces in the doorway/window/niche render shells.

## Geometry and placement

Wall origins are the lower span endpoint on the thickness centreline. Full walls occupy X=0..4, Y=0..3 and Z=−0.25..+0.25. SS03 uses X=0..2. SS04 is centred in X/Z and starts at Y=0. SS05 occupies X/Z=0..4 and Y=−0.25..0; its root is the top walking surface. SS06 spans X=0..4, Z=−0.50..+0.50, Y=−0.50..0, anchored at its top. SS10 starts at its lower span endpoint, with Z=−0.125..+0.125.

Full-height walls and the pier retain a 3 m stack rise. SS10 has a real 1.50 m height. SS11 is a triangular prism with two mirrored slopes: place a pair at Y=3 for a 4 m span and ridge Y=5. The opposite slope is a linked flip variant; it is not a second catalog tile. Gables use the existing roof attachment path without extra rectangular geometry below the slope.

The catalog's 0.50 m sandstone wall thickness is preserved. Existing limestone-specific dimension adjustments do not apply to sandstone. Mixed-material junctions therefore retain the actual thickness of each family. There are no new automatic corner skins, timber posts or masonry bond operations.

`preserveAuthoredMesh` retains the imported UVs and minimal shells in placed geometry. Quarter-metre sockets follow solid wall bases; SS07 omits sockets across its doorway void. Gables have endpoint sockets. Free placement follows the current building system; bedding and lintel seats communicate construction, without reinstating support or collapse requirements.

## Openings, recesses and collision

**SS07:** a centred 1.25 W × 2.25 H doorway, X=1.375..2.625. One source stone lintel runs X=1.125..2.875 and Y=2.25..2.75, with 0.25 m seats. The runtime shell is a closed extrusion of the U-shaped wall profile: 28 triangles. Three BoxColliders cover two jambs and the header; no collider crosses the aperture. No door leaf or moving interaction is included.

**SS08:** outside is local −Z, inside is +Z. The outer aperture is X=1.75..2.25; the inner aperture widens to X=1.50..2.50. Sill and head stay at Y=1.25 and 2.25. The window is empty through the entire wall. A connected 32-triangle shell models the real flaring jambs; four convex hulls describe the sill, header and two wedge-shaped jambs. The inner width is not simulated with a dark decal or a rectangular box collider. The source has a single 1.50 m lintel above the inner opening.

**SS09:** the pocket opens at local +Z and occupies X=1.50..2.50, Y=1.00..1.75 and Z=0..0.25. The entire rear layer Z=−0.25..0 remains solid. The connected 28-triangle shell contains four recess faces and a back, with five simple collision boxes around/behind the pocket. It has no through-opening metadata. Shelves, pottery or other contents are separate accessories.

**SS10:** the two source slabs bake to one closed 12-triangle panel and one box collider. Their central joint is surface detail rather than two coincident runtime end faces.

**SS11:** each slope uses one 8-triangle convex prism for visual geometry and one small convex collision hull. The unused half of its rectangular logical envelope stays empty in mesh and physical collision.

SS01–SS06 each use one simple solid and one BoxCollider. Prefabs and pooled gameplay proxies use the same decomposition. All pieces remain static and use the existing 100-health placeholder; no new recipes, resource items or durability tier are assigned.

## Colour, baked detail and texture cost

The stone is warm apricot-orange, centred around sRGB `#C88750`, with lighter ochre variations and thin earth-coloured joints near `#997452`. These colours belong to the stone rather than a warm directional light. The source uses procedural broad colour variation and subtle sedimentary grain on independently editable stone meshes. SS01 uses smaller bedded courses; SS02/SS03 use calmer dressed blocks. SS10 uses two broad slabs, without masonry courses.

Cycles selected-to-active baking writes unlit RGB and ambient occlusion in the alpha channel of `Sandstone_ColourAO.png`, plus tangent-space +Y normals in `Sandstone_Normal.png`. Direct/indirect illumination is excluded from the colour bake. The shared 2048-square atlas is packed from the actual runtime surfaces, with UV island spacing and 12 px bake dilation. Surfaces retain their shape and dimensions when packed; no wall is squeezed to become the short-span wall.

`Sandstone.mat` uses the existing `Custom/BakedStoneWallMatte` shader, with directional/night lighting, fog, cave lighting, local flame response and GPU instancing. Import settings are trilinear mipmaps, Clamp, 4× anisotropy, BC7 colour/AO and BC5 normals on Standalone. The two textures have approximately 10.7 MiB of compressed payload including mipmaps, shared across the whole family; platform residency may differ.

The minimal shells have flat external silhouettes. Stone bevels, joints and sedimentary grain are normal/AO detail, so grazing views expose the low-poly surface. The doorway void, splayed reveals, blind recess and triangular gable remain actual geometry. There is one UV layout per representation.

## Rebuild and installation

1. From the repository, run `"C:/Program Files/Blender Foundation/Blender 5.0/blender.exe" --background --factory-startup --python-exit-code 1 --python Tools/BuildSandstoneKit.py`. This regenerates only sandstone source, FBXs, maps, manifest and Blender overview. Preserve manual source edits before regenerating.
2. Run `Tools/ValidateSandstoneKit.ps1 -Install` for isolated compilation, asset installation, native rendering, gameplay checks and selective copying to the main project. Unity must be able to access its installed licensing service.
3. Alternatively use **Tools → Building → Install SS01-SS11 Sandstone** for the scoped editor importer. The full modular rebuild includes sandstone when its source assets are available.
4. Restart Play mode to refresh a previously loaded catalog. Use **B → Walls / Posts / Floors / Foundations / Roofs**; press **R** to flip SS11.

The installer appends missing references to the existing catalog while retaining its other bytes, copies only this family's models/material/maps/definitions/icons, and never opens or saves a world scene. It checks that the native source, FBXs, maps and implementation files match the isolated validation run before installing.

## Verification artifacts

The validation covers repeat-install GUID/catalog stability, preservation of existing definitions, bounds, triangle budgets, UVs, tangent bases, outward-facing triangles, convex closed hulls, shared material, texture imports, category/icons and independent visible-mesh aperture checks. Runtime checks exercise all twelve representations at all eight headings through the actual placement and pooled collider paths. They check both sides of the niche, the window reveal at two depths, wall stacking, paired gable datums and sixteen real CharacterMotor doorway crossings.

Native URP captures include front/reverse views of every representation, an overview and a view submitted through the actual instanced building renderer. The runner then executes existing ordinary-stone, modular-wood and building-interaction regressions. Final results are recorded in [sandstone validation](../ArtReferences/SandstoneBuildKit/Models/unity-validation.json), [stone regression](../ArtReferences/SandstoneBuildKit/Models/stone-regression.json), [wood regression](../ArtReferences/SandstoneBuildKit/Models/wood-regression.json) and [interaction regression](../ArtReferences/SandstoneBuildKit/Models/interaction-regression.json).

Previews: [Blender overview](../ArtReferences/SandstoneBuildKit/Models/sandstone-blender-overview.png), [Unity overview](../ArtReferences/SandstoneBuildKit/Models/sandstone-unity-overview.png), [instanced view](../ArtReferences/SandstoneBuildKit/Models/sandstone-instanced.png), [SS08 exterior](../ArtReferences/SandstoneBuildKit/Models/SS08-unity-front.png), [SS08 interior](../ArtReferences/SandstoneBuildKit/Models/SS08-unity-back.png) and [SS09 recess](../ArtReferences/SandstoneBuildKit/Models/SS09-unity-back.png).

The installed run passed **2,829 sandstone checks**, including sixteen doorway walks, plus **3,608 ordinary-stone**, **28,311 modular-wood** and **8,112 building-interaction checks**. Runtime and editor compilation passed with existing obsolete-API warnings. The Unity overview, window/niche reverse views and instanced capture were visually inspected. The first sandboxed Unity launch could not reach the licensing service; the successful isolated run used the installed editor outside that sandbox. Installation appended eleven sandstone catalog references and retained existing entries and world scenes.
