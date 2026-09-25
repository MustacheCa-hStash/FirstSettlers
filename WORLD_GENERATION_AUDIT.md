# World generation and placement audit

Reviewed 2026-09-24. Scope: map construction, placement decisions, data flow, and determinism. Rendering and mesh optimization are excluded. The initial audit made no generation changes; implementation checkpoints appear below.

The most valuable immediate work is to fix the sparse planner's dependency ordering, eliminate repeated patch decisions, and bound influence-map work to each feature's footprint. Chunk boundaries are the largest distribution-quality problem. Broader ecological realism can mostly reuse existing terrain samples and placement passes.

Evidence is from source inspection and an isolated C# harness running the repository's actual feature planner and analytic noise implementation. The harness substitutes basic Unity math/value types; these are algorithm checks, not Unity/Burst performance measurements or a complete engine validation. No millisecond speedups are claimed.

## Actual pipeline

The orchestration is in [TerrainRequestManager.cs](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scripts/TerrainGeneration/TerrainRequestManager.cs:117).

| Order | Data or decision | How it is formed |
|---|---|---|
| 1 | Base height and mountain mask | Seeded gradient-noise fBm for lowland relief, a broader mountain-region mask, and a shaped mountain profile. `WorldTerrainHeight.Base` combines them and supplies analytic base derivatives. |
| 2 | Erosion and playable terrain shaping | Point-evaluable erosion uses the base gradient. Local-relief mode also evaluates base height at four offset positions. Lowlands and shores are softened afterward. |
| 3 | River mask and carved height | A warped, jittered Voronoi-like site field evaluates pair boundaries. Mountain eligibility suppresses channels. Broad basins lower toward a dry shoulder; narrow channels lower below the single global water plane. This is procedural channel geometry, not a downhill drainage simulation. |
| 4 | Gradients and gameplay slope | Height samples cover `chunkSize + 11` on each axis. One-step derivatives and a separate slope from a +/-4-sample stencil are calculated. The public maps retain `chunkSize + 3` samples, including a one-sample halo. Gameplay slope is in degrees. |
| 5 | Moisture and temperature | Independent seeded value-noise fBm, at `sampleScale * 10` and `sampleScale * 12`. Effective octave count is `max(1, octaves - 2)`. Neither map currently uses height, drainage, slope aspect, or canopy. |
| 6 | Biome | Ordered thresholds over water height, slope, mountain mask, temperature, and moisture. Water/cliffs/alpine rules precede lowland climate rules. |
| 7 | Surface and water state | Surface derives from height, biome, river mask, and slope. Water state is a height comparison against the water plane, shallow-depth threshold, and wet band; its river-mask parameter does not affect classification. |
| 8 | Forest and grassland intent fields | World-coordinate noise combined with slope, moisture, river mask, and biome eligibility. Forest: canopy intent, clusters, clearings, rockiness, damp shade, understory. Grassland: openness, meadow moisture, riparian intent, groves, rockiness; grove intent also reads eight neighboring biomes. |
| 9 | Rocks | Forest boulders first; grassland large boulder, then smaller rocks. Jittered chunk-local candidate grids, environmental probability, per-chunk limits, and exclusion against earlier accepted placements. Small grassland rocks may cluster around the large boulder. |
| 10 | Rock influence | Grassland rocks stamp radial influence. Forest rocks reserve placement space but deliberately do not stamp circular floor patches; forest rock influence comes from rockiness noise. |
| 11 | Trees | Forest trees, then grassland trees. Jittered chunk-local candidates; environmental chance; weighted species selection; exclusion against earlier rocks/trees. Forest uses 81 candidates and a cap of 18; grassland uses 49 candidates and a configurable cap, default 8. |
| 12 | Berry bushes | Global 24-sample patch cells, then radial child candidates. Habitat, noise, and exclusions determine acceptance; cap is 30 bushes per chunk. These decisions use understory/canopy intent, before actual canopy density exists. |
| 13 | Canopy and organic floor | Canopy starts at 0.26 times forest canopy intent, then takes the maximum of radial tree influences, followed by noise and clearing modulation. Organic floor combines noise and ecological fields, then adds species-dependent local tree contributions. |
| 14 | Ground cover | Grass-surface classification uses biome, climate, slope, river mask, canopy, clearings, rock influence, damp shade, and organic floor. Forest cover includes litter, moss, dirt and dark grass; taiga uses needle litter/dark grass; tundra uses snow dusting. |
| 15 | Smaller foliage | Scheduled when needed by `FoliageManager`. Trees, bushes and rocks are materialized from the plan. Flowers use global patches; tall flowers can remove earlier ordinary flowers, then daisy weeds are added. Clover and dandelions share a patch-discovery implementation. Grass waits for applicable clover influence. Lily pads and cattails separately derive shore-distance maps. |

The broad dependency order is sound: large features reserve space before small plants, and ground cover responds to tree placement. Small-foliage generation is a dependency graph rather than one universal total order: clover/dandelions ensure trees, bushes and rocks; flowers currently ensure/exclude trees only; aquatic vegetation uses terrain/water independently.

Near grass already reads persistent native terrain maps and applies cheap habitat checks before exclusions and bilinear height sampling. Per-candidate hashes and indexed job outputs avoid a shared mutable random stream. Keep these properties.

### Scale and configuration matter

The saved [SmearScene](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scenes/SmearScene.unity:1211) uses 128-sample chunks, sample scale 600, world scale 0.3, height multiplier 200, and global water Y 14.4. Thus chunks span 38.4 world units; water height is 0.24 in normalized terrain units. Slope conversion correctly cancels uniform world scale.

Its climate setting `octaves = 2` produces **one actual climate octave**. Persistence and lacunarity therefore do not change climate in that configuration. Climate correlation scales are approximately 1,800/2,160 world units before interpreting the noise shape. This explains broad biome regions; it is not an erosion-octave setting.

The old `BaseLandGenerator`, `MountainMaskGenerator` and `MountainTerrainGenerator` files are not the active height path. The compatibility octave-offset arrays passed through the height API are ignored by the current `SampleWorldHeight` implementation, and mountain anchors are now always empty.

## Determinism and correctness findings

### 1. Sparse tree generation reads neighbors before preparing them

**Highest priority; reproduced.** In [WorldFeaturePlanGenerator.cs](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scripts/TerrainGeneration/BiomeGeneration/WorldFeaturePlanGenerator.cs:149), `Prepare` samples the center, builds grassland fields, and marks it prepared. `BuildGrasslandStructureFields` immediately calls `GetAdjacentForestBlend`. Only afterward does `PrepareGrasslandTree` sample the neighboring biomes. The grove/open-grass fields are not rebuilt.

[DistantTreePlacement](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scripts/FoliageGeneration/DistantTreePlacement.cs:39) pools the arrays and clears validity flags, not all array contents. Consequently those neighbor reads can see default values or biomes left by another chunk. Which scratch workspace a worker receives can change placement.

Reproduction: seed 1, chunk (0,0), size 128, uniform current grassland/grass surface, moisture 0.75, temperature 0.5, slope 20, river mask 0.1, default feature settings. Fresh scratch produced two trees and matched the full planner. Priming the same scratch with alternating forest/grassland cells, then regenerating the identical current input, produced three trees with different positions/species. Twenty-six prepared grove samples differed.

**Fix:** ensure each grassland field's biome neighbors are sampled before constructing it, even when a rock candidate prepares that location first. Alternatively separate neighbor-dependent grove preparation from the other fields. Clearing pooled arrays alone removes the history dependence but does not fix full/sparse disagreement caused by unprepared neighbors.

### 2. Chunk-local influence and exclusions do not describe one continuous world

**Reproduced.** [IntersectsExistingPlacement](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scripts/TerrainGeneration/BiomeGeneration/WorldFeaturePlanGenerator.cs:1303) only sees the current plan. Canopy, rock and organic-floor stamping likewise see only local placements; radial sampling also clamps halo positions to the chunk interior.

For synthetic uniform forest at seed 1, adjacent chunks (0,0)/(1,0) disagreed at **60 of 129 shared canopy samples**, with a maximum absolute difference of **0.5755826**. The same case accepted neighboring trees at global (123,5), radius 8.317957, and (137.0773,5.7968626), radius 7.412: their exclusion disks overlap.

This is spatial inconsistency, distinct from random nondeterminism. Generating each chunk repeatedly can still reproduce the same seam.

**Fix direction:** evaluate influence from a world-coordinate halo of neighboring features. For spacing, use globally addressed candidates and a deterministic conflict rule with a stable rank/ID; do not use whichever neighboring chunks happen to be loaded. A bounded rule that compares eligible candidates against higher-priority nearby candidates avoids an unbounded cross-chunk greedy dependency, but changes the distribution and needs tuning. This is a larger improvement, not a free micro-optimization.

### 3. Patch boundary assumptions need correction

- **Berry patches:** [center habitat is clamped to the receiving chunk](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scripts/TerrainGeneration/BiomeGeneration/WorldFeaturePlanGenerator.cs:940). Two chunks evaluating the same global patch can therefore disagree about its chance, species, count and radius. Sample habitat at the true global center, or use world-coordinate patch decisions with child-local habitat acceptance. Avoid using neighbor load state.
- **Clover and dandelions:** [patch culling uses the unscaled radius](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scripts/FoliageGeneration/FoliageGenerator.cs:2968), then child placement stretches that radius by up to 1.28 and rotates it. A radius-5 patch centered at x=133.5 is rejected for a chunk ending at x=128, although an allowed stretched child can reach x=127.1. Use a conservative `1.28 * radius`, or the rotated ellipse's exact X/Z extents, for overlap and candidate-cell enumeration.
- **Ownership:** berry and basic flower/clover children use inclusive upper chunk edges. Lily pads, cattails and daisy weeds use half-open edges. Standardize on `[min, max)` so an exact-edge candidate has one owner, including at negative coordinates.

### 4. Shoreline distance has insufficient neighborhood support

[Lily pads](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scripts/FoliageGeneration/LilyPadGenerator.cs:25) and [cattails](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scripts/FoliageGeneration/CattailGenerator.cs:31) run chamfer transforms over the one-sample-padded chunk. A shore two samples outside that boundary is invisible, even if a nearby candidate is well within its allowed shore band. Entirely water/land chunks can exit early despite a qualifying shore just outside their halo.

At the saved world scale, lily pads' 8-world-unit reach requires about **27 terrain samples**, plus a sampling guard, rather than one. Compute distance on a sufficiently expanded domain and crop, or on deterministic shared tiles. Share the to-dry field between both systems. Unify the dry test too: lily pads use `>= waterLevel`, cattails use `> waterLevel`.

### 5. Stable IDs are tied to list order

[CreateStableBushId](C:/Users/samee/GitProjects/FirstSettlers/Assets/Scripts/FoliageGeneration/FoliageGenerator.cs:3729) includes `placementIndex`. Reordering rocks/trees or changing list assembly changes a surviving bush's ID even when its position/species is identical. Before changing placement order or parallelizing list assembly, give each candidate an ID derived from world seed, feature channel, global cell and child index. Decide generation-version/save migration explicitly.

Rare edge case: `Mathf.Abs(Hash(...)) % count` does not produce a nonnegative value for `int.MinValue`. Use a normalization that handles that case. Preserve existing offsets for ordinary hash values if maintaining old seeds is required; blindly switching all inputs to unsigned modulo changes many existing worlds.

## A. Performance improvements

The ordering below reflects visible repeated work and implementation risk, not measured runtime proportions.

| Priority | Change | Evidence and expected benefit | Preservation requirements |
|---|---|---|---|
| 1 | Compute patch descriptors once | `FlowerDiscoveryJob.Execute` recomputes center, patch noise, chance, radius and count for every flower slot. The saved scene allows **350 slots per patch**, including rejected patches. Clover/dandelions repeat the same pattern. | One descriptor per global patch, then child jobs. Keep the original patch/child hashes and output order. Use fixed output slices initially; deterministic compaction can follow. |
| 2 | Bound radial map updates | `ApplyRadialInfluence` and `AddOrganicFloorTreeInfluence` scan all 17,161 map cells for every feature, even outside its small radius. | Iterate a conservative clipped bounding rectangle, retain squared-distance rejection and existing arithmetic, and preserve the current duplicate/clamped halo behavior until the seam fix is introduced separately. A radius-16 footprint needs roughly 33-35 cells per axis instead of 131. |
| 3 | Remove unused derivative maps and compatibility inputs | One-step X/Z gradients are allocated, calculated, copied and returned, but have no consumer anywhere under `Assets`. The gameplay slope uses its own wider stencil. Four seeded offset arrays and empty mountain anchors are also prepared despite being ignored by the active height sampler. | Keep the wide-stencil slope. At size 128 the two unused managed plus two native gradient buffers alone account for about **285 KiB per terrain request**, excluding object overhead. Do not confuse these with `WorldTerrainHeight.Base` derivatives required by erosion. |
| 4 | Index dense-foliage exclusions | Every grass candidate scans all clover influences and computes a square root for each; tree/bush/rock lists are also scanned. Each subchunk copies whole-chunk influence/exclusion lists. | First filter lists to the subchunk AABB expanded by influence radius. Then use bins if counts warrant it. Check squared distance before sqrt; a clover influence of 1 can return immediately because aggregation is `max`. Avoid a discretized mask if exact placement preservation is required. |
| 5 | Keep map data native across stages | Height/climate jobs write native arrays, copy them to managed maps, dispose native storage, then the request manager copies maps back to native. `ChunkRecord` later makes persistent copies; flowers/clover/dandelions flatten them again. | Transfer ownership or retain immutable buffers with job lifetime tracking. Near grass already demonstrates native-map reuse. Async readers must keep buffers alive through unload/regeneration/cancellation. |
| 6 | Cheap river rejection before expensive work | Mountain eligibility is calculated after the entire river field. Each point checks 36 site pairs; each pair scans the seven other sites and takes a separation sqrt before adjacency rejection. | Calculate zero eligibility first and bypass rivers there. Find the lowest three site distances once; for any excluded pair, the closest remaining site is among those three. Preserve tie handling, pair accumulation and the existing adjacency fade. Move sqrt after adjacency rejection. Do not replace the field with only the nearest pair: that can change junctions. |
| 7 | Hoist invariant height setup | Settings sanitization, erosion octave count/weight normalization, and rotation/domain constants appear in the point-sampling path. Local-relief erosion evaluates `Base` five times per point overall. | Sanitize once at the public boundary and pass a prepared context. Preserve public validation and arithmetic. Inspect Burst output/profile first: inlining may already remove some repeated calculations. Skipping the mountain profile where the mask and its gradient are zero is another candidate. |
| 8 | Schedule independent work together | Height, moisture and temperature are independent but each helper schedules and immediately completes. Water state depends on height only. Biome then surface can be calculated in one per-sample job. | Expose job handles and build dependencies, completing at the actual managed-planner boundary. This reduces synchronization; throughput gains depend on worker saturation. Retain fixed candidate ordering. |

Additional small wins:

- Replace managed `Hash(params int[])` calls with fixed-arity overloads preserving the exact mix sequence. They allocate arrays in the feature planner and managed flower passes; the Burst candidate paths already use fixed-arity variants.
- Skip canopy breakup noise when density is exactly zero. The forest/grassland field loops already skip irrelevant biomes, so do not assume every ecological noise runs everywhere.
- Several fields, especially grassland intent and forest tree-cluster/understory fields, are only read inside the planner. Treat them as scratch or evaluate them on candidates; do not retain all 15 feature-plan float maps for the life of a chunk without a consumer. Those 15 maps total about 0.98 MiB at size 128 before overhead.
- If tall-flower clearing is material in profiles, spatially restrict its scan of existing flowers and compact survivors once. Repeated `List.RemoveAt` shifts the remaining list. Preserve survivor order and current patch precedence.
- A value-only ecological noise sampler can avoid calculating unused derivatives, but measure compiler behavior before assigning it a large expected benefit.

Do not start by adding more worker threads, replacing noise algorithms, reducing map resolution, or changing erosion octave counts. Those are scheduling or visual-quality tradeoffs rather than clearly equivalent optimizations.

## B. Low-cost generation-quality improvements

| Improvement | Existing inputs to reuse | Cost and implications |
|---|---|---|
| Populate taiga | Existing spruce/white-pine variants, temperature/moisture, slope and clearing logic | The planner only accepts Forest or Grassland; a synthetic Taiga/Grass map produced **zero placements**. Add an explicit conifer-dominated taiga path using the existing bounded tree candidate budget. Reusing the entire forest species table would not be appropriate. |
| Make understory respond to actual shade | Actual canopy stamps, moisture and clearings | Build canopy immediately after trees, before berry bushes; let bush suitability distinguish canopy gaps from deep shade. Bushes do not contribute to the current canopy map, so this ordering needs no additional canopy pass. Revisit damp shade too: it currently follows canopy intent rather than realized canopy. Changes accepted plants, so version the generation rules. |
| Match forest litter to actual species | Tree variant and existing tree-influence pass | Conifers already exist inside Forest, but forest cover does not select NeedleLitter; that category is reserved for Taiga. Accumulate a conifer/deciduous contribution or dominant-litter signal while stamping existing influences, and use it in ground-cover choice. No extra noise field is necessary. |
| Add local soil wetness/exposure | Height, the existing +/-4 slope samples, river/shore proximity and gradient direction | Separate broad climate moisture from local habitat wetness. A small concavity term, water proximity and aspect term can bias moss, willow, berries and meadow plants. This is a cheap habitat proxy, not flow accumulation or a physical hydrology model. Keep climate biome thresholds separately tunable. |
| Consistent plant clearance | Existing planned rock/bush/tree footprints and a shared local spatial index | Basic and tall flowers currently exclude trees but not bushes/rocks. Use the same planned blockers for all relevant small plants; allow species-specific tolerances. Use the plan rather than render-asset availability to define physical occupancy. |
| Tie tree footprint to size | Existing scale sample and species ranges | Scale, exclusion radius and canopy radius are independently hashed. Relating radii to species and realized size, with bounded jitter, gives large trees appropriate space/shade. Cheap arithmetic; changes spacing and acceptance. |
| Remove chunk-shaped distribution artifacts | World-coordinate candidates, ecological density and deterministic local conflict ranks | Current trees/rocks clamp positions inward by 3-6 samples and use chunk-local caps. This creates empty boundary strips and concentrates candidates exactly on inset lines. Global cells and continuous density avoid this. Combine with the boundary/exclusion redesign; do not simply remove the clamp first. |
| Seed aquatic colony shapes | Existing noise helper and world seed | Lily/cattail broad Perlin colony patterns use species offsets but not `worldSeed`; the seed currently changes their candidate acceptance/jitter. Add seed to the colony field if distinct broad patterns between worlds are desired. Same order of per-candidate noise cost. |

The first five are particularly useful. Truly downhill rivers, erosion-driven sediment transport, and regional rain shadows are larger systems. The present global water plane and pointwise channel mask limit what can be achieved with a small placement-only change.

## Recommended implementation sequence and validation

1. Fix sparse field preparation and add a scratch-reuse/full-versus-sparse regression. Correct stretched patch bounds and half-open ownership separately.
2. Apply output-preserving optimizations: patch descriptors, bounded stamps, unused buffers/setup removal, and cheap influence rejection. Compare complete ordered outputs against the original implementation before and after.
3. Consolidate native ownership and job dependencies once lifetimes and cancellation are covered. Add counters around map copies and actual asynchronous discovery execution; current synchronous generation timers do not fully represent the incremental paths.
4. Introduce taiga trees, actual-canopy understory, species litter and local wetness as explicit generation-rule changes. Evaluate distributions over multiple seeds, not only one attractive chunk.
5. Redesign shared feature halos/cross-chunk conflicts and shore-distance support. These have the biggest spatial-quality benefit but require more than a local loop optimization.

Regression coverage should include positive and negative neighboring chunks; reversed/random chunk request order; cold and reused scratch; varying worker counts; cancellation/retry; full versus sparse trees using identical height multipliers/settings; patches centered outside their receiving chunk; water/land just outside the one-sample halo; exact-boundary candidate ownership; and unchanged IDs for unchanged candidates.

For performance, measure height samples, patch decisions, child candidates, accepted counts, influence-distance checks, allocations and copy volume, plus warmed-up per-stage timing across representative habitats. The existing [DistantTreeValidation](C:/Users/samee/GitProjects/FirstSettlers/Assets/Editor/DistantTreeValidation.cs:150) and [GroundFoliageStreamingValidation](C:/Users/samee/GitProjects/FirstSettlers/Assets/Editor/GroundFoliageStreamingValidation.cs:38) provide starting points, but fresh-array repeatability alone does not establish scheduling independence or cross-chunk consistency.

Exact preservation means more than retaining the seed: keep candidate identity, hash salts, conflict ordering, floating-point evaluation order and final list ordering where they are observable. Cross-platform/Burst-versus-managed bitwise equivalence should be tested explicitly rather than assumed.

## Implementation checkpoint: A1 only

Implemented patch-level discovery for ordinary flowers, clover and dandelions. Each scheduled job iteration computes one patch's shared decisions and writes its children into the original fixed output slice. Rejected/unused slots are zero-initialized. Hashes, candidate indices, result ordering and placement rules are preserved. Batch size adapts to the maximum children per patch so dense patches still distribute across workers.

Validation in Unity 6000.4.8f1, with synchronous Burst compilation for both the original and optimized implementations:

- Compared **68,116 ordered instances across 24 cases**, including positive/negative seeds and sparse/dense patches. All instance fields matched exactly.
- In 80 warmed, paired flower-generation measurements on synthetic 128-sample grassland chunks, median total generation time was **3.125 ms before / 2.823 ms after** (about **9.7% lower**). This includes map preparation and result conversion; it is not a whole-world or frame-rate measurement.
- Ground foliage streaming validation passed: deferred discovery, incremental output, deterministic repetition, cancellation, replaced-map rejection, retry and grass/clover dependencies. Added rejected-patch regression coverage and supplied the existing test fixture's missing flower settings.
- A2 and later recommendations remain unimplemented at this checkpoint, as requested. The temporary original-versus-optimized comparison harness was removed from Assets after validation.

Re-run the retained checks with **Tools > Terrain > Validate Ground Foliage Streaming** in the Unity editor. Changes are in `FoliageGenerator.cs` and `GroundFoliageStreamingValidation.cs`.

## Implementation checkpoint: A2 only

Bounded the radial update loops for rock influence, canopy density, and tree-driven organic floor to the cells their radius can reach. The existing squared-distance test, influence arithmetic, placement order, and duplicate edge samples are unchanged.

Unity 6000.4.8f1 comparison against the original planner passed for 18 full plans and 230 ordered placements across forest, grassland, and mixed synthetic habitats, three seeds, and positive/negative chunk coordinates. Canopy, organic-floor, and rock-influence maps matched bit-for-bit. Direct stamp comparisons also covered near and beyond all chunk edges and radii from 0.1 to 200 samples.

For one warmed mixed-habitat 128-sample chunk, the median of 20 paired full-planner runs was **14.278 ms before / 12.487 ms after** (about **12.5% lower**). This is a synthetic case, not a whole-world or frame-rate estimate. The temporary original-versus-optimized comparison code was removed after validation. A3 and later recommendations remain unimplemented.

## Implementation checkpoint: A3 only

Removed the unused one-step X/Z gradient maps from the near-terrain job, managed result, and request handoff. The gameplay slope still uses its original four-sample stencil. Also removed creation and carriage of the four legacy octave-offset arrays and empty mountain-anchor buffers from near/far height jobs, mountain-snow coverage, and distant-tree terrain sampling. Climate octave offsets remain, since climate sampling uses them. The compatibility `GetMountainAnchors` method remains available but has no active generation call sites.

Unity 6000.4.8f1 compared the original and optimized near-height implementations across 12 cases (three seeds, positive/negative chunk coordinates, 32/128-sample chunks, default/customized erosion): height, slope, mountain mask, and river mask were bit-exact. A warmed 128-sample height-field case had median **6.419 ms before / 6.152 ms after** over 12 paired runs (about **4.2% lower**). The two removed managed gradient maps and two native gradient buffers save about **285 KiB** of transient storage per size-128 request, excluding object overhead.

Existing slope-policy, mountain-snow, and global-water validations passed. Two broader validations did not pass: `WorldErosionValidation` failed its far-mesh maximum-spacing assertion, and `DistantTreeValidation` failed its real-terrain full-versus-sparse tree-count assertion. The spacing rule is outside A3's scope. The sparse-tree mismatch is a known issue in the initial audit, but these broad checks were not rerun on an untouched baseline; the direct near-height-map comparison did pass. The temporary comparison harness was removed. A4 and later recommendations remain unimplemented.

## Implementation checkpoint: A4 only

Near-grass subchunk jobs now receive only tree, bush, rock, and clover influences whose radius can reach that subchunk's local-coordinate bounds. Filtering preserves source order and uses conservative padding at boundaries. Both near and billboard grass skip the square root for clover influences clearly beyond their radius, and stop scanning when an influence reaches 1. The candidate hashes, exclusion radii, and placement order are unchanged. A separate bin index was not added; the filtered per-subchunk lists are already small in the tested density range.

In Unity 6000.4.8f1, a temporary copy of the pre-A4 foliage generator was compared side by side with the optimized version. Across 12 synthetic 128-sample chunk cases with trees, bushes, rocks, 160 clover clumps, three seeds, positive/negative chunk coordinates, and clover both on and off, **149,624 ordered near-grass instances matched exactly**. Three billboard cases with blockers and clover matched another **33,149 ordered instances**. The existing billboard-grass streaming validation also passed.

In 32 warmed paired full-chunk near-grass measurements of the dense-clover synthetic case, median generation time was **5.788 ms before / 5.492 ms after** (about **5.1% lower**). This is not a whole-world or frame-rate measurement. The temporary comparison code was removed. A5 and later recommendations remain unimplemented.

## Implementation checkpoint: A5 only

The terrain request now retains native height, slope, river, moisture, and temperature job outputs for downstream map jobs. It transfers the persistent height, slope, river, biome, surface, water, and ground-cover buffers to the accepted chunk record instead of copying managed maps back into new native arrays. Billboard grass, flowers, clover, and dandelions borrow matching immutable native maps; fixture or replaced maps still use the managed-map flattening path. Each asynchronous reader holds a lease so unloading or replacing a record defers buffer disposal until that reader completes. Rejected and queued results release their buffers, including during manager shutdown.

The last Unity 6000.4.8f1 batch run passed bitwise native/managed map comparisons, full-request ownership handoff, native-versus-fallback foliage instance comparisons, record replacement and in-flight mesh lifetimes, and queued-result shutdown cleanup. The user then requested no further testing or validation, so no later checks were run. Static copy accounting suggests roughly **2 MiB** less repeated map copying for a size-128 chunk when all four affected foliage passes run; this is not a measured timing result. The temporary A5 validation harness was removed. A6 and later recommendations remain unimplemented.

## Implementation checkpoint: B2 only (B1 skipped)

The full feature planner now builds the realized canopy density after placing forest and grassland trees and before placing forest berry bushes. Forest damp shade is updated from this canopy density; understory density retains its existing fine-breakup pattern but responds to the shade change and is reduced under dense canopy. Bush patch acceptance therefore favors viable gaps and partial cover, while species weights favor blueberries in shade and raspberries, blackberries, and strawberries in openings. Organic-floor generation uses the updated shade field. Tree placements and the sparse tree-only planner are unchanged. This intentionally changes bush distributions for the same seed; existing bush IDs include placement index, variant, and position, so changed placements receive different IDs. There is no persistent world-generation rule-version facility in the current codebase.

No testing or validation was run for B2, per the user's request. B1 remains skipped; B3 and later quality recommendations remain unimplemented.

## Implementation checkpoint: B3 only

Tree canopy stamping now also accumulates a signed litter contribution over the same footprint: spruce and pines add needle influence, while deciduous trees add leaf influence. The ground-cover classifier uses the resulting balance only when its existing habitat rules choose leaf litter or mixed forest floor. Strong conifer dominance yields `NeedleLitter`, transition areas yield `MixedForestFloor`, and strong deciduous dominance yields `LeafLitter`. Moss, dirt, grass, river, rock, and exposure decisions retain precedence. This adds one float map and its native transfer, but no noise sampling or separate tree-footprint traversal. It intentionally changes forest ground-cover distribution for the same seed. Far-terrain ground cover uses a separate approximation and does not carry the near planner's tree species signal.

No testing or validation was run for B3, per the user's request. B1 remains skipped; B4 and later quality recommendations remain unimplemented.

## Implementation checkpoint: B4 only

Added a bounded local moisture adjustment derived from the existing height stencil, river mask, water level, and slope. Shallow basins, river and shore proximity, and slopes facing +Z add wetness; exposed steep ground and local ridges reduce it. The adjustment is clamped to -0.18..+0.22 and is calculated only for forest or grassland grass surfaces. It is combined with climate moisture for forest damp shade, berry suitability, grassland meadow moisture intent, tree-species weights (including willow), and forest ground-cover classification. Biome classification and mountain-snow climate inputs still use the unchanged climate map. The full terrain request builds one adjustment map; the sparse distant-tree path evaluates the same formula at prepared candidates from its cached height sampler. Direct planner callers without height input retain zero adjustment. This is a local habitat proxy, not flow accumulation or physical hydrology.

The added map is about 67 KiB at size 128 and is copied once for the ground-cover job. No additional noise field is sampled. Boundary samples use the same clamped four-sample stencil in the full and sparse paths; cross-chunk halo consistency remains part of the broader boundary redesign. No testing or validation was run for B4, per the user's request. B1 remains skipped; B5 and later quality recommendations remain unimplemented.

## Implementation checkpoint: B5 only

Ordinary flowers, tall lupines/meadows, daisies, clover, and dandelions now check planned tree, bush, and boulder footprints through the same exact-distance uniform-grid lookup. The feature plan defines occupancy even when a render prefab or instance is unavailable; only fixtures without a plan fall back to generated instance lists. Each plant pass retains its own minimum clearances, and planned footprint fractions distinguish trees, bushes, and rocks. Flower settings add bush/rock minimum distances and separate tolerance scales for tall flowers and daisies. The index uses sample-space positions and places blockers into every cell their maximum possible clearance can reach, avoiding a quantized placement mask. Candidate hashes and enumeration order remain unchanged, while accepted plants intentionally change around planned features. Async discovery holds its own native grid until its job completes or is canceled and rejects a replaced feature plan during result conversion.

No testing or validation was run for B5, per the user's request. B1 remains skipped; B6 and later quality recommendations remain unimplemented.

## Implementation checkpoint: A6 only

The production height sampler now calculates river eligibility before sampling the river field, so points with zero eligibility bypass its warp, site generation, and pair evaluation. It still runs the existing basin carve with zero influence to preserve the height operation sequence. The river sampler records the three nearest site distances while constructing its nine sites. For each of the original 36 pairs, it uses the nearest site outside that pair for the adjacency fade instead of rescanning the other seven sites. Strict distance comparisons retain site-index ordering on ties. Site separation and its square root are calculated only after a pair survives adjacency rejection. All pair accumulation, channel and basin calculations, and fade formulas remain in place.

This removes up to 252 third-site visits per river sample and avoids up to 36 square roots when every pair is rejected; these are static operation counts, not measured timing results. No testing or validation was run for A6, per the user's request. A7 and later performance recommendations remain unimplemented.
