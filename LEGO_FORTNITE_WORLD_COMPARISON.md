# FirstSettlers and LEGO Fortnite: world generation and rendering

Reviewed October 3, 2026 against the current working tree, including uncommitted changes. The enabled build scene is SmearScene. This is a source and settings review, with a small executable geometry calculation; it is not a Unity Player or LEGO Fortnite GPU capture. Runtime code and settings were not changed.

The most useful lesson is to maintain one authoritative world description, then progressively reduce the information needed to generate, simulate, and render it. FirstSettlers already does much of this. The remaining opportunities concern consistency between those descriptions, authored composition, cache lifetime, and cost per visible pixel.

## 1. What is documented about LEGO Fortnite

Epic explicitly identifies World Partition streaming and procedural environment population through PCG, and described a 95 km² playable world at launch. That is a historical launch figure, not a verified total for every island and mode in October 2026. World size is not render distance. [Epic's launch announcement](https://www.epicgames.com/site/news/the-adventure-is-building-lego-fortnite-is-live)

A level designer who worked on the game describes authored LEGO points of interest spawning in the procedural open world, with natural terrain/foliage assets around LEGO structures. This supports a hybrid approach to composition; it does not establish that every terrain landform is assembled from prefabricated tiles. [Josiah Frizzell's production portfolio](https://josiahfrizzell.com/)

Unreal's public PCG system supports generation at different spatial scales, with larger-grid outputs cached for smaller-grid consumers, runtime generation sources, cleanup radii, and scheduling. Those are documented engine capabilities and useful reference designs, not proof of LEGO Fortnite's exact graph or private settings. [Epic's PCG generation modes](https://dev.epicgames.com/documentation/en-us/unreal-engine/using-pcg-generation-modes-in-unreal-engine)

World Partition streams spatial cells; HLOD provides simplified distant representations. These solve different problems from procedural placement and Nanite geometry selection. FirstSettlers should preserve that distinction when designing its Unity equivalents. [World Partition](https://dev.epicgames.com/documentation/en-us/unreal-engine/world-partition-in-unreal-engine), [World Partition HLOD](https://dev.epicgames.com/documentation/en-us/unreal-engine/world-partition---hierarchical-level-of-detail-in-unreal-engine)

Epic documents Nanite, Lumen, Virtual Shadow Maps and TSR for Fortnite Battle Royale's high-end rendering path. That source concerns Battle Royale, so it cannot establish which LEGO assets use each feature on each platform. A Switch/low-quality comparison and a high-end PC comparison should be treated separately. [Fortnite rendering announcement](https://www.fortnite.com/news/drop-into-the-next-generation-of-fortnite-battle-royale-powered-by-unreal-engine-5-1)

I did not find a public authoritative LEGO Fortnite table of terrain cell size, loading radius, vegetation culling distance, vertex counts, PCG generation time, or per-pass GPU timings. Claims that LEGO uses an exact noise algorithm, Poisson sampler, terrain template scheme, or Unreal default loading radius would exceed the evidence.

## 2. Distances and geometry: verified project values versus estimates

For FirstSettlers, the numbers below come from [SmearScene](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scenes/SmearScene.unity:889) and the policy code. One chunk is 128 samples × 0.3 = **38.4 Unity world units**. I use metres below only under the usual project convention of one Unity unit per metre. Character and asset scale still affect how large that feels.

For LEGO Fortnite, the following are deliberately broad, low-confidence engineering guesses for an outdoor high-quality PC view. They are not measured settings, calibrated footage measurements, or reliable platform limits. They express plausible orders of magnitude to compare representation layers. The true distances can lie outside these ranges. LEGO character scale further complicates visual estimates.

| Layer | FirstSettlers current behavior | LEGO Fortnite rough estimate | Confidence in LEGO estimate |
|---|---|---|---|
| Terrain coverage / large horizon | Nominal circular coverage radius 3,840 units; macro tiles can extend outside its edge | Roughly 2–5 km for substantial distant landforms | Low |
| Camera clipping | Far plane 6,500; vertical FOV 60° | Not established | Unknown |
| Detailed trees | GameObject representation through chunk ring 3; about 115 units to outer chunk centres along axes, with chunk extent beyond that | Detailed meshes perhaps 100–300 m, asset and quality dependent | Very low |
| Individual distant trees / substantial vegetation | Distant tree outer radius 576; fade starts at 499.2 | Perhaps 300–1,500 m for recognizable large vegetation | Very low |
| Grass | Radial outer distance 115.2; mesh/card transition around 25–52 | Perhaps 20–100 m for individual ground vegetation | Very low |
| Leaf scatter | Effective radius 92.16, because it follows 0.8 × grass distance | Not established | Unknown |
| Ferns | Radius 65; grass-linked LOD policy overrides some inspector thresholds | Not established | Unknown |
| Physics terrain | Circular radius of five chunk centres, approximately 192 plus boundary extent | Not established; should not be equated with visibility | Unknown |
| Realtime directional shadows | PC URP asset: 140 units, four cascades, 2,048 map resolution | No authoritative LEGO value found | Unknown |

Your terrain horizon already sits in the same broad distance class as the LEGO estimate. Its existence alone is unlikely to be the visual gap. The density, silhouette, lighting and continuity of the intermediate environment are more promising targets.

### Exact terrain mesh topology

I executed the actual `BuildTerrainTopology` method in a small C# harness, substituting only ordinary math functions and counting distinct grid coordinates and triangles. [Reproducible PowerShell calculation](C:/Users/samee/GitProjects/FirstSettlers/Tools/WorldComparisonMetrics.ps1)

| Near LOD | Chunk rings | Grid step | World interior spacing | Actual vertices per chunk | Actual triangles per chunk |
|---|---|---:|---:|---:|---:|
| 0 | 0–1 | 1 | 0.3 | 16,641 | 32,768 |
| 1 | 2–3 | 2 | 0.6 | 4,477 | 8,444 |
| 2 | 4–5 | 4 | 1.2 | 1,461 | 2,420 |
| 3 | 6–7 | 8 | 2.4 | 709 | 932 |
| 4 | 8+ in the normal mesh policy | 16 | 4.8 | 501 | 548 |

Ring 8+ normally enters the separate far path in the saved scene, so the last row is a topology capability, not a claim that all outer normal records draw LOD4. Some alignment boundaries retain single-chunk far meshes.

Dense seam borders explain why coarse meshes exceed a simple square-grid calculation. At step 16 a plain 9×9 grid would have 81 vertices; the actual stitched topology has 501. This is a deliberate correctness cost. It is worth optimizing only if it is material in a capture. [MeshGenerator](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scripts/TerrainGeneration/MeshGeneration/MeshGenerator.cs:217), [LOD policy](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scripts/TerrainGeneration/MeshGeneration/ChunkRingLODPolicy.cs:5)

### Effective macro-tile geometry

The inspector's far height resolution of 9 is not the effective macro resolution. Current code chooses **65, 65, 33, 65** for 4-, 8-, 16- and 32-chunk patches. Control-map resolution is 61 for all four. This differs from the older pipeline document. [Effective policy](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scripts/ChunkManager/ChunkManager.cs:1726)

| Patch width | World width | Height grid | Vertex spacing | Vertices including skirts | Triangles including skirts | Control texel spacing |
|---|---:|---:|---:|---:|---:|---:|
| 4 chunks | 153.6 | 65×65 | 2.4 | 4,737 | 8,704 | 2.56 |
| 8 chunks | 307.2 | 65×65 | 4.8 | 4,737 | 8,704 | 5.12 |
| 16 chunks | 614.4 | 33×33 | 19.2 | 1,345 | 2,304 | 10.24 |
| 32 chunks | 1,228.8 | 65×65 | 19.2 | 4,737 | 8,704 | 20.48 |

Patch-size eligibility begins at Chebyshev rings 8, 16, 32 and 64, but a world-aligned patch must have its entire footprint outside the preceding band. This produces position-dependent boundary strips.

The coverage calculation enumerates the same circular logical chunk set and patch selection methods as ChunkManager. It models fully populated requested terrain in every direction, including full boundary patches:

| Viewer chunk | Logical coordinates tested | Normal records | Of those, single-chunk far records | Macro records | Terrain vertices | Terrain triangles |
|---|---:|---:|---:|---:|---:|---:|
| (0,0) | 31,417 | 256 | 31 | 172 | 1,164,220 | 2,099,680 |
| (7,13) | 31,417 | 320 | 95 | 232 | 1,366,136 | 2,461,408 |

These totals exclude foliage, water, previous cached LODs, transitional overlap, reflection and shadow passes. They are not a rendered-frame count: frustum, occlusion, residency and attachment readiness change actual submissions. The rings 0–7 alone contain 225 chunks and total 507,777 vertices / 903,840 triangles if every chunk in that square is counted.

### LEGO vertex and triangle estimates

If forced to put a number on a high-quality PC outdoor view, I would use **millions of triangles**, with a very uncertain envelope around **1–10 million main-view triangles**. A conventional indexed-mesh equivalent might contain roughly **0.5–5 million vertices**, but that conversion is especially unreliable for disconnected foliage, split normals/UVs and Nanite's cluster representation. These are scale guesses, not recovered LEGO counters. Source assets and all loaded geometry could be far larger; shadows and other passes can process geometry again.

For your project, the directly computed terrain totals are more useful than this speculative LEGO vertex number. Do not set a Unity triangle target from it. Compare frame times, material work, shaded pixels, bandwidth and memory as well as geometry.

## 3. Generation architecture and distribution

### You already have a substantial ecological pipeline

Your active path is a world-coordinate height evaluator with analytic base gradients, point-evaluable erosion, river carving, climate, biome/surface/water classification, ecological fields, large features, ground cover and small foliage. It is substantially more developed than independent random prefab scattering. In the current planner, trees contribute canopy before berry-bush placement; local moisture adjustment and forest-floor ecology have also been added. [Terrain orchestration](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scripts/TerrainGeneration/TerrainRequestManager.cs:117), [Feature planner](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scripts/TerrainGeneration/BiomeGeneration/WorldFeaturePlanGenerator.cs:114)

The broad design maps well to hierarchical PCG. Your Unity code is effectively a procedural dependency graph even though it is expressed as C# rather than editor nodes. The useful next step is explicit ownership, caching and dependency contracts for each stage.

| Concern | FirstSettlers today | Useful evolution |
|---|---|---|
| Broad world identity | Seeded height and climate fields | Region descriptions with recognizable landscape composition, landmarks and resource intent |
| Biomes | Climate thresholds with water/slope/mountain precedence | Continuous membership weights where useful, plus explicit transition ecotones |
| Forests | Canopy intent, clustered trees, clearings, damp shade, litter and moss | Shared world-scale forest descriptors across near terrain and distant tree planning |
| Trees | 9×9 forest candidate lattice, up to 18 accepted trees per chunk; 7×7 grassland lattice, scene cap 6 | Globally addressed candidates and spacing constraints independent of chunk ownership |
| Flowers | World patch cells, child candidates, environmental rejection, larger meadows | Cache patch decisions across consuming tiles; retain current deterministic child hashes |
| Boulders | Reserve space before trees and small plants | Add authored compositions such as outcrops, scree, exposed roots and bank clusters |
| Water vegetation | Shore-distance fields and habitat rules | Shared padded shore tiles large enough for the maximum influence radius |
| Interactive state | Bush IDs/state plus near collidable representations | Persistent sparse changes keyed by world candidate ID, independent of visual residency |

The LEGO-specific composition lesson is the combination of procedural surroundings with authored encounters. For FirstSettlers, useful equivalents are a stream crossing, abandoned camp, fallen-tree clearing, rock overhang, meadow bowl or resource grove. Give each composition a footprint, slope/elevation constraints, approach direction, ground adjustment limits and smaller-feature exclusion masks. Author the relationships among its parts and procedurally choose where it fits. The portfolio evidence supports this lesson for POIs, not a wholesale replacement of your terrain with tiles.

### Three spatial scales deserve explicit separation

1. **Regional intent:** broad climate, valleys, forest mass, large clearings, landmarks and traversal corridors.
2. **Feature descriptors:** trees, boulders, bushes, flower colonies and authored compositions; include IDs and influence extents.
3. **Local realization:** terrain textures, detailed foliage, colliders and gameplay objects near the player.

Near rendering and far rendering should read the same regional/feature descriptors at different resolutions. A cheaper far pass may omit details; it should preserve the large-scale identity of the place.

### Distribution issues still visible in the current code

**Chunk-local exclusions.** `IntersectsExistingPlacement` checks only the current plan's placements. Tree and canopy influences similarly originate from that local plan. Shared world noise does not by itself make feature spacing or stamped maps continuous. Adjacent chunks can accept overlapping exclusion disks or calculate different canopy influence at the shared edge. [Placement exclusion](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scripts/TerrainGeneration/BiomeGeneration/WorldFeaturePlanGenerator.cs:1385)

Use a global candidate lattice with a stable candidate rank/ID and bounded neighbourhood conflict rules. Enumerate neighbouring candidates through an expanded footprint, then emit only candidates owned by the chunk using half-open bounds. A bounded rank rule changes the distribution and requires tuning; it is not automatically identical to the existing greedy acceptance order. Evaluate canopy/litter influence from accepted neighbouring descriptors through a sufficient halo.

**Sparse tree dependency ordering.** `Prepare` builds grassland fields, including an adjacent-biome blend, before `PrepareGrasslandTree` samples those neighbouring biomes. The fields are not rebuilt after that sampling. Pooled scratch buffers make this particularly risky. Sample dependencies before evaluating the field, and verify full/sparse equality under repeated pooled-buffer reuse. This ordering is still present; the earlier audit contains a historical reproduction, which I did not rerun here. [Sparse path](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scripts/TerrainGeneration/BiomeGeneration/WorldFeaturePlanGenerator.cs:182)

**Patch habitat at a clamped centre.** Berry patches use globally addressed centres but read receiving-chunk habitat through clamped local indices. Two chunks can make different decisions for the same global patch. Decide the patch once from true world-centre habitat, then perform child-local acceptance. [Berry patch evaluation](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scripts/TerrainGeneration/BiomeGeneration/WorldFeaturePlanGenerator.cs:1033)

**Shore support.** Lily/cattail transforms operate on the existing padded maps, with early exits when that map contains only water or only dry land. A one-sample halo is 0.3 units; lily pads can need a shore up to 8 units away, approximately 27 samples plus a guard. Use an expanded shared shore tile and crop the result. [LilyPadGenerator](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scripts/FoliageGeneration/LilyPadGenerator.cs:25), [CattailGenerator](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scripts/FoliageGeneration/CattailGenerator.cs:31)

### Scale matters more than adding another noise field

Scene climate `octaves=2` becomes one effective climate octave. Climate persistence 0.05 and lacunarity 10 therefore do not add climate detail in this configuration. Approximate noise coordinate scales are 1,800 and 2,160 world units for moisture and temperature. These are correlation scales, not promised biome diameters. Broad regions are appropriate; habitat variation should usually come from local ecological fields rather than fragmenting every biome. [ClimateGenerator](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scripts/TerrainGeneration/BiomeGeneration/ClimateGenerator.cs:10)

Rivers currently derive from warped site boundaries and carving toward one water level, rather than a downhill drainage network. This is efficient and streamable, but does not guarantee hydrological connectivity or natural upstream/downstream structure. For a future improvement, generate a sparse regional valley/drainage graph and use the current analytic functions for local channel shape. Cache that graph; avoid a full hydraulic simulation per newly loaded fine chunk.

## 4. Computation cost and streaming

### Work you have already improved

The current tree/foliage implementation and terrain request path incorporate several earlier audit recommendations:

- Flower and clover discovery execute once per patch, then loop its children, rather than recalculating a descriptor per child slot.
- Radial canopy/organic-floor influence loops use clipped bounds.
- Generated height/slope/climate native buffers are transferred into later stages; retained terrain maps support native readers with lifetime tracking.
- The active river sampler performs mountain eligibility rejection before sampling and tracks nearest-site distances to avoid repeated third-site searches.
- HeightFieldResult no longer carries the old unused derivative-map pair.

Do not schedule these as entirely new optimizations based on the older audit. Some managed/native duplication and flattening still exist, but the remaining work should be identified per stage. [Current request pipeline](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scripts/TerrainGeneration/TerrainRequestManager.cs:183), [Patch job](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scripts/FoliageGeneration/FoliageGenerator.cs:3016), [Active river path](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scripts/TerrainGeneration/InputsToBiomeGen/HeightMapGenerator.cs:265)

### Cost models worth tracking

| Stage | Approximate work model | Implication |
|---|---|---|
| Near height request | 139² = 19,321 padded point evaluations | Small visible chunks still require the full supporting field |
| Fine ecology/maps | Mostly 131² = 17,161 cells per map | Several passes and retained arrays can outweigh coarse mesh topology |
| Local-relief erosion | Centre base sample plus four offset base evaluations where active | A height query is considerably more expensive than one noise lookup |
| Far control maps | 61² = 3,721 texels, each with centre height and slope-neighbour sampling | A coarse macro mesh is not necessarily cheap to generate |
| Large-feature exclusion | Candidate count × prior placements | Small current lists are reasonable; indexing pays off as populations grow |
| Grass candidates | 144² / 38.4² ≈ 14.06 potential candidates per square unit | Dense-foliage footprint and rejection cost deserve measurement |
| Active-set rebuild | (2×100+1)² = 40,401 coordinate tests; 31,417 enter the disk | Boundary crossings can do much more CPU bookkeeping than final renderer count suggests |
| Foliage draw work | Surviving instances × mesh complexity × applicable passes | Instancing reduces submission overhead, not vertex/pixel cost |

These are operation counts, not measured millisecond costs. CPU hardware, Burst compilation, worker contention, habitat rejection and rendering settings matter.

For example, a 65×65 macro tile performs 4,225 height-grid samples plus approximately 5 × 3,721 = 18,605 centre/slope samples for the control maps: about **22,830 height evaluations** across those two stages. That is slightly more than the near request's 19,321 padded height samples, although a macro covers much more area and omits many fine ecology stages. Coarse shading fields or cached derivatives could reduce this work, with an explicit quality tradeoff; reducing mesh triangles alone leaves it untouched.

The full potential grass disk at radius 115.2 would contain roughly 586,000 candidate sites at the scene lattice density before habitat rejection and distance thinning. That is an area-density thought experiment, not a statement about the actual resident arena size. Its rendering policy shares persistent candidates and selects representation on the GPU; preserve that design.

Scene terrain concurrency caps are four data requests, one far request, six mesh requests and two collider requests. Grass has its own concurrency cap of eight. Some worker requests schedule Burst jobs and wait for them. These caps are not independent core budgets: raising all of them can increase contention and memory pressure. Measure throughput and latency together.

Result-application budgets are checks around units of work, not hard preemption. A mesh upload or texture creation can overrun a nominal time allowance. Track p95/p99 application spikes as well as averages. Prefer measured total foreground budget and urgent collision readiness over blindly increasing result limits.

### Cache lifetime is a substantial remaining issue

`loadedChunks` and far runtimes are pooled/released, but `chunkRecords` and `farTerrainTileRecords` have no ordinary exploration eviction path visible in ChunkManager. Near records retain map data and cached meshes for requested LODs. Far records retain meshes, textures and height grids. Releasing render objects therefore does not bound generated-resource memory. `FarTerrainTileRecord` also has no disposal method; whole-world cleanup should explicitly destroy the Unity assets it owns. [ChunkManager lifetime](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scripts/ChunkManager/ChunkManager.cs:292), [FarTerrainTileRecord](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scripts/ChunkManager/FarTerrainTileRecord.cs:3)

The seven core retained 32-bit maps at 131² occupy about 0.46 MiB per copy, or about 0.92 MiB when both managed and native copies are retained. This excludes feature-plan fields, ecology, foliage, meshes, textures and allocator overhead. One thousand previously generated near records can therefore retain nearly a GiB in those core map copies alone. A bounded renderer count is insufficient evidence of bounded memory.

Introduce byte-accounted caches for near terrain data, LOD meshes and macro assets. Pin active, incoming/outgoing handoff and in-flight reader dependencies; evict least-recently-used unpinned records and destroy owned Unity resources. Preserve changed gameplay state separately. Your existing native reader lifetime mechanism is a useful foundation.

Enumerating quadtree leaves directly instead of visiting every fine coordinate is another potential CPU improvement. It requires preserving the circle boundary, world alignment and readiness-based handoffs. At present, inspect the boundary-crossing profiler marker before undertaking that change.

## 5. Rendering techniques transferable to Unity

### Representation continuity

Your terrain macros are analogous to an HLOD idea for the ground. Your individual tree impostors are LOD substitutes. There is still room for a farther **forest-group representation**: stand-level canopy meshes/impostors or very cheap clustered crowns that preserve forest mass beyond the 576-unit individual-tree range. Thin trees by stand coverage and silhouette, not just independently by random chance. Keep silhouette/edge trees and major landmarks longer.

Your current distant-tree settings already protect edges and selected trees and use crowding-aware thinning. Extend that rather than replacing it with uniform density reduction. Raising the individual-tree radius from 15 to 30 chunks increases the disk area approximately fourfold. A separate coarse stand tier is likely the better first experiment.

Far terrain and near terrain use different levels of ecological information. Far control maps classify forest ground cover from climate/noise without all actual local tree influences. This can change the apparent forest-floor character during handoff. Share broad descriptors where feasible and blend omitted fine effects out gradually.

### Screen-space terrain error

Ring LOD is predictable and cheap, but treats a flat meadow and a steep distant ridge equally. Screen-space error should determine where extra geometry matters. An illustrative projected error is:

`errorPixels ≈ heightErrorWorld × viewportHeight / (2 × distance × tan(verticalFOV / 2))`

At 1080p, 60° vertical FOV and 1 km distance, a 2-unit height error projects to about 1.87 pixels. Use an error bound or conservative measured residual from finer height samples, then retain more geometry for silhouettes. Include grazing views, camera altitude, hysteresis and seam constraints; the formula alone is not a complete selection algorithm.

Your far spacing reaches 19.2 units while the shortest configured four-octave erosion wavelength is also approximately 19.2 units. That is inadequate sampling for reliably reconstructing that detail. Filter attenuated high frequencies at coarse levels, and separately preserve broad ridge extrema. Repeatedly sampling detailed noise on a coarse grid can both waste CPU and alias the terrain.

### Occlusion versus frustum culling

The grass, distant-tree and forest-scatter compute paths primarily perform distance, density/LOD and frustum selection. I did not find a depth-pyramid visibility test in those paths. Objects behind a mountain can still survive frustum selection. Terrain horizon shadows calculate lighting, not camera visibility.

Both GPU Resident Drawer and its occlusion option are off in the PC URP asset. Unity 6.4 documents GPU occlusion for compatible GPU Resident Drawer renderers. Benchmark it for eligible ordinary MeshRenderers, with custom shader and material-property restrictions checked. It does not automatically insert visibility tests into your custom indirect foliage pipelines. [Unity 6.4 occlusion documentation](https://docs.unity3d.com/6000.4/Documentation/Manual/urp/gpu-culling.html)

For custom draws, a conservative hierarchical-depth approach is a possible later step. Test solid terrain/rocks as occluders before vegetation; add temporal hysteresis and safe handling for camera cuts and fast motion. Occlusion overhead can lose in an open meadow, so compare meadow, forest and valley separately.

### Geometry versus alpha overdraw

Tree/plant cutout cards can spend substantial time shading fragments that ultimately fail alpha or sit behind other cards. A card with few vertices can cost more than a larger opaque silhouette mesh. Unreal's own documentation identifies aggregate foliage and layered geometry as difficult cases for Nanite too. [Epic's Nanite content guidance](https://dev.epicgames.com/documentation/unreal-engine/working-with-naniteenabled-content)

For Unity, trim empty atlas margins, reduce repeated internal card layers, preserve crown coverage in LODs, and make wind/material work cheaper at distance. Your coarse fern asset is 156 vertices / 52 triangles; that is a useful known mesh budget. It still needs a pixel-cost check when thousands of instances overlap. [Coarse fern mesh](C:/Users/samee/GitProjects/FirstSettlers/Assets/Models/Foliage/ForestFern_LOD2.asset:14)

Do not attempt to reproduce Nanite by submitting all high-detail Unity meshes through one giant indirect draw. Hierarchical clusters, visibility, streaming and error-based geometry reduction are the properties that matter.

### Shared terrain rendering resources

Normal chunks and macros own separate render objects and tile textures/materials. SRP Batcher is on, which reduces compatible state-update overhead; it does not merge these meshes into a single draw. Terrain shares the expensive custom material shader even far away. Consider explicit far shader variants with reduced layer/parallax/normal/triplanar work and a controlled appearance transition. [Terrain shader](C:/Users/samee/GitProjects/FirstSettlers/Assets/Shaders/S_Terrain/CustomStylizedTerrain.shader:650)

If terrain submission is measurably expensive, texture arrays plus per-tile indices can reduce unique binding/state churn. Shared topology and GPU height displacement are a larger option, but change normals, bounds, seam handling, shadow/depth passes and collider integration. With hundreds of terrain objects rather than tens of thousands, measure before rebuilding the whole renderer.

## 6. Project settings with likely impact

The project uses Unity 6000.4.8f1 and URP 17.4.0. PC is the default Standalone quality tier. The renderer enum in the installed package confirms `m_RenderingMode=2` means **Forward+**.

| Setting | Current state | Practical implication / experiment |
|---|---|---|
| PC shadow distance | URP 140, four cascades, 2,048 main-light map | This is the relevant setting; QualitySettings' 40 is not the URP distance |
| Mobile shadow distance | 90, one cascade | Different visual/performance tier; do not compare it to LEGO's highest PC setting |
| PC render scale | 1.0 | Resolution changes can expose fragment/bandwidth bottlenecks |
| Mobile render scale | 0.8 | About 64% of full-resolution pixel count before fixed-cost passes |
| Camera AA | MSAA asset value 1 (disabled), camera AA 0, post-processing off | Thin leaves, distant grass and dither fades can shimmer; AA experiments may improve perceived density without more geometry |
| HDR | Enabled | Format/bandwidth cost; assess together with lighting and post-processing |
| Depth and opaque textures | Required in PC pipeline | Retain where custom shaders need them; inspect actual copy/pass cost |
| SSAO | Active, full resolution (`Downsample=0`), radius 0.3, intensity 0.4 | Test downsampling or a lower tier against canopy and grass readability |
| GPU Resident Drawer / occlusion | Off | Targeted experiment for compatible ordinary renderers |
| Texture mip streaming | Off in both quality tiers | Large tree/ground atlases can retain unnecessary mips; audit import settings and verify custom indirect bounds/texture use |
| PC LOD bias | 2 | Influences Unity LOD selection, but does not automatically change your custom ring/compute policies |
| Incremental GC | On | Useful, but does not eliminate allocation traffic or Unity asset lifetime problems |

Sources: [PC pipeline](C:/Users/samee/GitProjects/FirstSettlers/Assets/Settings/PC_RPAsset.asset:26), [PC renderer](C:/Users/samee/GitProjects/FirstSettlers/Assets/Settings/PC_Renderer.asset:72), [Quality settings](C:/Users/samee/GitProjects/FirstSettlers/ProjectSettings/QualitySettings.asset:64), [Camera](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scenes/SmearScene.unity:180). Unity confirms that URP shadow distance belongs in the pipeline asset. [Shadow distance documentation](https://docs.unity.com/en-us/engine/6000.3/manual/lighting-overview/shadows/shadow-realtime/shadow-distance)

### Water reflections deserve an early GPU experiment

Your planar reflection uses a texture at 0.7 of each screen dimension, approximately **49% of full-screen pixels**, with a 300-unit clip range. It requests 30 updates/s when stationary and 120 when moving, effectively potentially every frame at a 60 Hz moving-camera frame rate. Shadows and post-processing are disabled for that camera, which helps. It remains an additional scene render, with fixed geometry/culling costs as well as pixels. It currently skips below-water views but does not establish that water occupies a useful amount of the current main view before updating. [Reflection code](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scripts/WorldManager/PlanarWaterReflection.cs:76)

Compare 0.5 resolution and 15–30 updates/s, then add a visibility/usefulness gate. Do not assume a 49% total-frame cost: clip range, scene contents and pass settings matter. Also verify that custom camera-specific indirect foliage draws appear in the reflection as intended before treating missing reflected vegetation as an optimization.

### Lighting and atmospheric depth

Terrain's current ambient model uses spherical harmonics plus an ambient floor and a directional-light/horizon-shadow term. It is not equivalent to scene-dependent dynamic GI. Your skyline shadows are a useful cheap solution for distant mountain shading, but do not darken tree-covered ground through full indirect transport.

For this stylized procedural world, investigate canopy-aware ambient attenuation, consistent palette/exposure, distance haze, and contact grounding first. Baked probes/APV require a practical strategy for newly generated worlds and runtime changes; enabling a checkbox will not provide correct arbitrary-seed lighting. A measured dynamic lighting solution is a separate project from terrain generation. Do not switch pipelines merely to imitate the name Lumen.

## 7. Prioritized work

| Priority | Change | Why | Verification |
|---|---|---|---|
| 1 | Fix sparse tree dependency ordering and agree on world candidate IDs/ownership | Near/far correspondence and deterministic worlds underpin every representation | Full/sparse equality, pooled scratch reuse, generation order permutations |
| 2 | Bound terrain and macro resource caches; explicitly destroy owned assets | Prevent exploration memory growth despite runtime pooling | Long traversal, return trip, unload/regenerate; native and GPU memory plateau |
| 3 | Resolve cross-chunk feature influence and patch habitat; expand shore support | Better distribution continuity without simply adding more objects | Boundary heatmaps, neighbour exclusion violations, shoreline cases |
| 4 | Profile reflection, foliage overdraw, SSAO and distant terrain material variants | These can dominate before terrain vertex count becomes the limiting factor | One-setting A/B GPU captures in meadow, forest and water views |
| 5 | Add authored regional compositions and shared descriptors | Improves readable identity and relationships between features | Many-seed visual review with traversal/resource constraints |
| 6 | Add a coarse forest-stand horizon tier | Preserve forest mass without multiplying individual tree work | Match canopy coverage, silhouettes and handoff under movement |
| 7 | Adopt error-aware far terrain and filtered coarse sampling | Spend geometry on mountains that visibly need it | Projected residual/error maps, high-altitude and grazing views |
| 8 | Add suitable occlusion and shared terrain resources if captures justify them | Reduce hidden work / submission overhead | Net frame-time improvement including visibility overhead |

For a 60 fps desktop target, a reasonable initial engineering allocation is roughly 1–2 ms average foreground streaming work, with occasional spikes controlled below about 3–4 ms, plus separate measured GPU budgets for ground, vegetation, lighting and water. These are suggested targets, not measured FirstSettlers performance or LEGO budgets. Tune them for the actual minimum-spec device.

## 8. How to get comparable measurements

Use a standalone Development Player after Burst/shader warmup. Record static meadow, dense forest, water-facing, ridge/valley and fast traversal camera paths. Capture at fixed resolution and settings; separate initial load from warmed traversal. Report median/p95/p99 main-thread and GPU time, memory growth, work-queue latency, collider readiness, draw counts, pass geometry and shaded-pixel cost. Avoid conflating worker elapsed time with frame time.

The existing `WorldRenderStatsDebugInfo` is useful for tracking representation budgets, but it omits dedicated leaf/fern categories and does not enumerate all shadow/reflection/depth passes. Distant-tree GPU stats use `SubmittedCount`, which is populated before compute selection, not a hardware visible-triangle counter. Treat the overlay as a source-side estimate and reconcile with a frame capture. [Stats schema](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scripts/WorldManager/WorldRenderStatsDebugInfo.cs:1), [Distant tree stats](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scripts/FoliageGeneration/DistantTreeManager.cs:320)

For LEGO Fortnite, the defensible next measurement is a chosen platform/preset/resolution, a specific world location and a reproducible camera position, using known in-game distance markers to record when each object class disappears or changes representation. Screenshots alone cannot recover triangle/vertex counts. Such a comparison should record a build/version because world content and platform rendering can change.

No runtime optimization or settings experiment was applied as part of this review. The geometry harness is source-driven and was run at viewer chunks (0,0) and (7,13); its counts are reproducible, while the LEGO geometry and distance ranges remain explicitly speculative.
