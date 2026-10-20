# L01–L03: limestone walls

The additional pier, paving, footing, limewashed wall, plinth and doorway are documented in [L04–L09 limestone components](LIMESTONE_L04_L09.md).

Three modeled walls from the [limestone catalog](../ArtReferences/LimestoneBuildKit/README.md), with an editable [Blender source](../ArtReferences/LimestoneBuildKit/Models/LimestoneBuildKit.blend), nine FBX layouts, one shared colour/AO and normal pair, authoring prefabs, build definitions and actual-mesh icons.

| Component | Size, metres | Construction | Runtime mesh |
| --- | --- | --- | --- |
| L01 coursed limestone wall | 4 W × 3 H × 0.50 D | Varied rough-squared blocks in nine slightly uneven courses | 12 triangles |
| L02 dressed limestone wall | 4 W × 3 H × 0.50 D | Six regular half-metre courses, staggered 1 m blocks and fitted end stones | 12 triangles |
| L03 narrow limestone wall | 2 W × 3 H × 0.50 D | Same physical block sizes/course heights as L02, independently authored short-span ends | 12 triangles |

Unity roots are the lower span endpoint on the thickness centreline: X=0..4 (or 0..2), Y=0..3, Z=−0.25..+0.25. Blender uses X across, Z up and Y depth; FBXs bake the axis conversion with metre scale and identity transforms. Every wall has three deterministic UV layouts with identical geometry. The two alternative mappings reflect the whole wall while preserving course heights; MikkTSpace tangents account for the mapping changes.

## Surface authoring

The source consists of editable, closed, shallow-bevelled limestone blocks over recessed warm beige packing. L01 varies block widths, course levels, face setbacks and bevel sizes. L02/L03 use a more regular bond. Broad painted colour comes from the joint-free **F limestone-face region** of the [unlabelled trim source](../ArtReferences/LimestoneBuildKit/Textures/limestone-timber-trim-source.png), sampled through Blender shader coordinates and lightly varied per stone. Neither the guide's labels nor painted masonry joints are applied across individual blocks.

Cycles selected-to-active baking produces `Assets/Textures/Buildings/Limestone/Limestone_ColourAO.png` (unlit RGB, ambient occlusion alpha) and `Limestone_Normal.png` (tangent-space +Y). Directional illumination and cast shadows are excluded from colour. The shared 2048-square pair has eighteen independent face islands, **160 pixels/metre** on all three walls, 16 px dilation/gutters and a 72 px outer packing border. L03 is not a horizontally squeezed full wall. The illustrative combined trim atlas is a source palette; these exact baked maps and UV islands are the production wall textures.

`Limestone.mat` reuses `Custom/BakedStoneWallMatte`: opaque, matte, instanced, with the existing directional, night, fog, cave and local-flame lighting paths. AO affects ambient response; mapped normals respond to light. Imports use trilinear mipmaps, 4× anisotropy, Clamp, BC7 colour/AO and BC5 normals on Standalone. The two maps require approximately 10.7 MiB including mips at those formats, shared across all instances of all three walls. Actual platform residency can differ.

Placed walls have flat rectangular silhouettes and continuous box collision. Individual stone relief and joint recesses are shading detail, matching the existing [baked ordinary-stone approach](BAKED_STONE_WALL.md); close grazing views expose the flat surface. No displacement, transparency or per-stone runtime draw calls are added.

## Building integration

Stable IDs are `build.stone.l01-coursed-limestone`, `build.stone.l02-dressed-limestone` and `build.stone.l03-narrow-limestone`. They appear under **B → Walls**. Existing names/order are retained, and limestone receives explicit descriptions and icons. Definitions, prefabs and their metadata live under `Assets/Resources/Building/l01-...`, `l02-...` and `l03-...`; imported models/material/maps have their own Limestone subfolders.

Full walls have seventeen quarter-metre endpoint/interior sockets; L03 has nine. There are no extra end-post cells. Stack rise is exactly 3 m. `preserveAuthoredMesh` keeps the baked wall geometry/UVs and suppresses generated timber fitting. No automatic wooden corner posts are added. Existing permissive building intersection rules and ordinary-stone support behavior remain in use. Like S02, these full-height walls begin on a foundation/supported construction rather than acquiring direct-terrain grounding. Removing the foundation removes their support through the existing support graph.

The three definitions use the existing placeholder 100 health, without a new durability tier, resource recipe or limestone inventory system. They can join existing wood/stone construction through existing socket/support logic; specialized bonded masonry corner dressing, timber bearing seats and new physical stability simulation remain future work.

## Rebuild and validation

1. Run `"C:/Program Files/Blender Foundation/Blender 5.0/blender.exe" --background --factory-startup --python-exit-code 1 --python Tools/BuildLimestoneKit.py` from the repository. This regenerates this kit's source, maps, FBXs and Blender renders. Preserve manual source edits before regenerating.
2. Use **Tools → Building → Install L01-L03 Limestone Walls** in Unity for the scoped importer. It creates/updates only these walls, their shared material and icons, then appends missing catalog entries. It is safe to repeat and does not open a world scene. General **Rebuild Full-Span Modular Kit** also includes limestone when its source assets are available.
3. Restart Play mode if the catalog was already loaded.

`Tools/ValidateLimestoneKit.ps1 -Install` prepares an isolated project, compiles runtime/editor source, installs twice, validates the new assets, captures actual URP views and enters Play mode for all three walls at eight headings. It then runs the existing stone regression suite and selectively copies validated limestone assets into the main project. The installer appends only missing definition references to the current catalog, preserving its other properties and entries. No saved world scene is opened or rewritten.

Checks include imported dimensions, positive closed solid volume, every UV triangle, tangent bases, all nine layouts, texture imports, prefabs/icons, shared material, repeat-install GUID stability, preservation of existing definitions, 3 m stacking, 2/4 m endpoints, retained authored meshes, pooled collision raycasts and loss of support after foundation removal. Renders include front/reverse views, normal-disabled comparisons, an overview, and joined/stacked walls through the actual instanced building renderer at a 45° heading.

Evidence is saved beside the source: [asset/runtime result](../ArtReferences/LimestoneBuildKit/Models/unity-validation.json), [ordinary-stone regression](../ArtReferences/LimestoneBuildKit/Models/stone-regression.json), [Blender bake report](../ArtReferences/LimestoneBuildKit/Models/bake-report.json), [Unity overview](../ArtReferences/LimestoneBuildKit/Models/limestone-unity-overview.png) and [joined/stacked capture](../ArtReferences/LimestoneBuildKit/Models/limestone-joined-stacked-instanced.png).

The installed pass completed **1,424 limestone checks** and **3,538 existing stone regression checks**, including all eight headings and sixteen existing gate traversals. Front/reverse views and the joined/stacked instanced capture were visually inspected. Runtime and editor compilation passed with existing obsolete-API warnings. The main catalog gained only three reference lines; existing definitions were verified unchanged during the scoped importer test.
