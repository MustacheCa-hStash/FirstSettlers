# Forest leaf clusters

## Irregular scatter revision

The sixfold candidate rate remains, but cells now contain **0/1/2/3 opportunities** with mean one, fully random positions across each cell and a smoothly varying world-space drift. A search halo includes displaced cells, and half-open ownership uses final positions, preserving deterministic negative-coordinate seams. Leaf instance Scale Range is now **0.55–1.5**, including SmearScene.

Each leaf mesh now carries its own pivot and identity in UV channel 1. The instanced shader varies individual leaf heading, size, offset, color tone, spread and visibility from stable instance parameters. Patches therefore have different silhouettes and leaf quantities while retaining nearly uniform color within each leaf. The first leaf remains present in both LODs; other identities thin consistently. The alpha atlas image and its artwork are unchanged.

Near/far meshes remain 72/16 triangles, with one material and the same batching/LOD architecture. Additional shader arithmetic and a 16-byte variation vector per submitted instance have a cost; hiding individual leaves does not remove their vertex work. Culling and blocker footprints use a conservative bound for the procedural spread. Actual preview renderers now receive the same scatter/tint parameters as runtime batches, avoiding another authoring/runtime discrepancy.

The new forest density fixture produced **161->949 placements (5.89x)** versus its 1x candidate rate, still above the requested +400% minimum. Validation passed for chunk seams, deterministic slices, placement exclusions, UV identity data, instanced/Forward+ shader variants and actual Linear Unity renders. Log: `.utmp/forest-floor/variation-unity.log`. Earlier sections describe previous scatter layouts and settings.

October 2 frequency update: `placementMultiplier` defaults to **6**, also set on SmearScene's leaf settings. Effective candidate spacing is `cellSize / sqrt(placementMultiplier)`; at 1.5 units this is approximately 0.612 units. Candidate opportunities therefore increase 6x (+500%), spreading more scatters through eligible forest floor instead of only making existing colonies pass a capped probability. The unchanged habitat/blocker rules yielded 155->938 actual placements in the validation fixture (6.05x). Existing moss retention, 72/16-triangle meshes and grass-matched render distance remain. Generation/cache/culling and instanced geometry costs rise with the additional placements; the per-frame streaming budgets stay bounded. Ferns share the bounded scatter kernel with their own settings and resources; see [FOREST_FERNS.md](FOREST_FERNS.md).

The ready-to-use prefab is `Assets/Resources/Foliage/LeafCluster.prefab`. The October 2 revision replaces the compact five-leaf rosette with nine asymmetrically scattered leaves, approximately 1.10 × 0.74 world units across. Leaves form loose pairs and isolated shapes with millimeter-scale folds, rather than radiating from a shared center. The near mesh has 72 triangles and 81 vertices. `Assets/Resources/Foliage/LeafScatter_LOD1.prefab` retains four of the same leaves with simpler folds: 16 triangles and 24 vertices. Both have one submesh/material, no colliders, and no shadow casting. They receive main-light shadows.

`Assets/Textures/Foliage/T_LeafCluster_Atlas.png` supplies four transparent silhouettes: maple, oak, beech, and birch. The bitmap is unchanged, but the material now sets **Use Painted Atlas Color = 0**, sampling only its alpha outline. Each leaf has a nearly uniform brown, ochre, tan, or taupe fill; texture veins, grain, and painted highlights do not appear. Lighting can shade the actual folds. Unity imports the atlas at maximum 1024 resolution with compressed color/alpha, coverage-preserving mipmaps, clamp wrapping, and no retained CPU pixel copy. Geometry follows each silhouette's envelope rather than using a cluster-sized quad.

`Assets/Shaders/S_Grass/LeafClusterInstanced.shader` uses a single texture sample, mesh normals, per-instance tint, ambient SH, main-light shadows, fog, and a dithered distance fade. Forward, depth, and depth-normal passes use the same cutout/fade. It includes the current URP Forward+ and fragment-fog variants. There is no wind animation or shadow-caster pass.

WorldManager exposes **Forest Leaf Clusters** settings. Existing scenes automatically use the Resources prefab when no override is assigned; no manual scene wiring or prefab rebuilding is needed. Restart Play mode to load the new code/assets. An optional prefab override must have a MeshFilter and MeshRenderer on the same object and an instancing-enabled material compatible with `_LeafInstanceTint`, `_FadeStart`, and `_FadeEnd`.

Default placement/render controls:

- **Match Grass Render Distance** is enabled by default. With SmearScene's 128-sample chunks, worldScale 0.3, and grass billboard radius 3, leaves reach **115.2 world units**, fading from **101.76–115.2**. Distance is horizontal and follows the same viewer as grass. Changes to grass range update leaves automatically; terrain residency and foliage visibility still gate rendering.
- Disable matching to use the manual 28-unit range and 8-unit fade width. Prewarm margin is 8 units.
- Near/far selection transitions over 18–30 units with a stable rank per scatter. Each instance draws exactly one representation; there is no duplicate transition layer.
- Candidate cell size 1.5 world units, independent of terrain worldScale.
- Base keep probability 0.38, reduced by colony noise, existing grass, soil, moss, slope, and river influence. Dominant moss carpets retain 25% of otherwise eligible leaf scatters, leaving occasional fallen leaves rather than clearing every instance. Existing serialized settings can retain their previous density value.
- Only Forest + Grass terrain surfaces with packed forest-floor ecology are eligible. Ground-cover gameplay labels do not control distribution.
- Maximum slope 32 degrees, checked against both the slope field and the actual terrain triangle. Density tapers before that cutoff.
- Scale 0.8–1.25 with deterministic rotation/tint variation.
- Tree trunk cores, bush/boulder/structure exclusion footprints, and the cluster's bounding radius exclude clutter; litter can still approach a tree's surrounding floor.
- Up to 256 candidate evaluations per frame within a soft 0.35 ms generation budget. One new chunk's blocker index can be initialized per frame. No jobs or synchronous job completions are introduced.

Generation uses a world-aligned jittered grid with half-open chunk ownership, triangle-interpolated terrain height, and terrain-aligned rotations. The prefab pivot sits at its lowest leaf, and placement adds only a 0.006-unit separation to avoid surface fighting. Cached candidates are discarded when their terrain maps, ecology/plan, placement settings, or prefab change, and evicted outside the prewarm area. A spatial blocker index avoids scanning every tree for every candidate. The system is disposed with ChunkManager.

Rendering uses **Graphics.DrawMeshInstanced**, the same GPU instancing API used by clover/dandelions. It does not instantiate a GameObject per cluster. CPU distance/frustum tests select visible candidates into reusable arrays; batches aggregate across nearby chunks separately for each LOD, with up to 1023 instances per submission. This path does not use grass's compute/indirect renderer. Enabled depth/normal prepasses add work. Tight cutout geometry limits alpha overdraw, but does not eliminate it.

A synthetic flat 64 × 64 forest fixture generated 406 scatters before feature exclusions after the moss-retention revision: approximately 0.099 scatters per square world unit. This is a validation fixture, not measured world coverage or a frame-time result. Every visible scatter adds 72 near or 16 distant triangles per relevant pass. The 115.2-unit radius covers about 16.9 times the area of the previous 28-unit radius before frustum/terrain exclusions; CPU visits, cache memory, and submissions can therefore rise despite the cheaper distant mesh. No FPS improvement is claimed.

Authoring: **Tools > Foliage > Build Forest Leaf Cluster** regenerates mesh/material/prefab from the atlas while preserving existing asset GUIDs. It runs only in Edit mode. The checked-in assets are already built.

Validation: **Tools > Terrain > Validate Forest Leaf Clusters** checks deterministic slicing, negative-coordinate seam ownership, forest/surface/slope/water exclusions, vegetation suppression, blocker footprints, map/settings invalidation, terrain triangle height/normal alignment, world scale, frustum sphere clipping, and prefab/material/texture/depth-pass settings. **Tools > Foliage > Validate Forest Floor Redesign** additionally checks matched/dynamic/manual range, complementary LODs, plain-color assets, and forest clover opening/toggle/slope exclusions. Actual isolated Unity/D3D11 renders and generation checks passed; see `.utmp/forest-grass/forest-preview-delivery.log`.

## Forest previews and clover

The user requested offscreen forest renders on October 2. [After_Close.png](ArtReferences/ForestFloor/After_Close.png) and [After_Forest.png](ArtReferences/ForestFloor/After_Forest.png) use actual generated grass, leaves, and clover with the game's litter albedo and tree prefabs. [Forest_Foliage_Close.png](ArtReferences/ForestFloor/Forest_Foliage_Close.png) is an arranged asset composition; [LeafScatter_Detail.png](ArtReferences/ForestFloor/LeafScatter_Detail.png) shows the scatter by itself. Matching before images preserve the earlier meshes/material fills.

These are Unity URP authoring renders on a flat representative forest fixture, with controlled daylight, no fog/postprocessing, and a simple lit ground material. They do not reproduce the live world's terrain shader, canopy lighting, slopes, or performance. **Tools > Foliage > Render Forest Floor Previews** renders currently saved assets and then rebuilds them from authored defaults; it updates assets and images. Re-running after delivery does not recover the historical before geometry. No computer-control automation or Play mode was used.

Clover now participates in Forest **DarkGrass** openings through the existing instanced clover system. Deep LeafLitter/Moss cover stays excluded. Forest colony frequency defaults to 55% of the normal patch opportunity, with a 22-degree slope ceiling. The existing patch-scale grass suppression remains active. `CloverSettings.enableForestClover`, `forestPatchChance`, and `forestMaxSlope` control this behavior. Restart Play mode to regenerate existing chunks after changing placement settings. White clover's preference for light/woodland openings is supported by [USFS FEIS](https://research.fs.usda.gov/feis/species-reviews/trirep).

## Texture provenance

The preserved atlas was generated in the earlier task with the built-in imagegen tool, using the existing litter albedo and stylized bush atlas as references. The current shader uses only its silhouette alpha. No bitmap edits or image generation were performed in the October 2 redesign. The historical prompts were:

1. “Production game foliage albedo/alpha atlas on true transparency; exactly four isolated, upward-pointing leaves in a 2×2 grid: ochre/tan maple, desaturated russet oak, cocoa beech, grey-taupe birch. Earthy colors informed by forest litter; hand-painted low-poly style, broad angular tonal facets, clear central/branching veins, restrained wear. Flat diffuse lighting; no scene, shadows, grid, text, or watermark.”
2. “Preserve the four silhouettes, quadrant layout, colors, stems, and true alpha. Simplify photographic grain into broad angular painted facets with 3–5 coherent value regions, a clear center fold, restrained veins, and a few large wear patches. Matte stylized game albedo; no cast shadows, specular, scene, grid, text, or crop.”

Exact prompts are saved alongside the asset in `Assets/Textures/Foliage/LeafClusterTexturePrompts.txt`.
