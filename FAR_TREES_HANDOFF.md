# Distant tree implementation handoff

## Request and constraints
- Implement consistent GPU-instanced trees over far terrain, inspector controls, stable thinning, and transitions without a deliberately empty ring.
- User will perform performance tests. Do not run performance benchmarks.
- Preserve existing world distribution and species where possible. No subagents requested.

## Implemented design
- Existing near placement uses WorldFeaturePlanGenerator: rocks precede trees and influence acceptance. Reuse these exact placement functions rather than inventing another distribution.
- Sparse candidate sampling skips whole-chunk ecological raster calculations and ground-cover/bush work, preserving candidate order and exclusions. Scratch maps are pooled, capped at four idle workspaces.
- Compact manifests are cached independently of terrain runtime/macro tile lifetime and generated on a bounded worker queue. Existing near lists take precedence when available.
- All 12 existing forest/grassland species variants are supported. Missing assignments use existing fallback prefabs; no new atlas artwork. Identical mesh/material sources share a batch.
- No distant shadow casting/receiving or wind animation. Species tint/texture logic is retained with simpler ambient/main-light shading and fog. Alpha clipping and depth writes remain enabled.
- Stable thinning keeps a nested subset of real trees. Farthest-point ordering protects a configurable number of spatial representatives per logical chunk, including all trees in very sparse chunks.
- Near/billboard handoff is a temporal complementary dither crossfade, default .65 seconds. This first version retains the existing circular 3D-tree radius; it does not introduce a new per-tree 3D LOD system. Legacy billboard start/end rings are ignored while enabled, so there is no deliberate empty band.
- The outgoing 3D representation is retained until its manifest is available; billboards remain until near objects exist. Steady-state near fades do not rewrite renderer property blocks every frame.
- Thinning and the outer cutoff are clamped beyond the circular 3D-tree region, preserving full density during handoff.
- Height conformity samples the actual far mesh triangle split, blends to detailed terrain height, and never changes placement identity. Owned billboard mesh copies have rotation-safe bounds to prevent screen-edge culling pops.

## Repository observations
- Initial working tree was clean this turn; previous far-water implementation is already present.
- The legacy FoliageManager draws only logical near runtimes. DistantTreeManager is called independently from ChunkManager and queries both near runtimes and loaded far macro tiles.
- TreeSettings is serializable on WorldManager and is the inspector control location.
- Common tree billboard shader is Assets/Shaders/TreeBillboardInstancedUnlit.shader (actual name Custom/TreeBillboardInstancedSimpleLit).
- Previous dotnet build failed in Unity package RenderGraph code; alternative build hit SDK directory permissions. Use meaningful compilation/correctness checks, no performance tests.

## Status: implementation complete
- Implemented DistantTreePlacement and shared sparse planner callbacks. Original near rules and candidate ordering remain intact; pooled scratch grids avoid per-chunk large allocations.
- Implemented DistantTreeManager with 12 existing variants/fallbacks, bounded worker jobs, compact cached manifests, spatial culling, species batches, stable density thinning and farthest-point protected representatives.
- Added inspector settings on TreeSettings (default distance 18 chunk widths, outer fade 2, density .45, protected trees 2, temporal transition .65 seconds, worker count 1, install count 1, cache 2048).
- Added far height-grid retention and triangle interpolation for terrain seating. New renderer covers both logical far chunks and macro tiles.
- Added shared dither helper, near renderer property-block fades, billboard instance fades, and simplified distant lighting/fog. Legacy billboard ring gap is bypassed when enabled.
- Unity compilation and first correctness run PASSED. Sparse planner matched 54 trees in 12 synthetic ecological cases; four real-terrain cases also passed. No performance tests.
- Pooled scratch reuse and custom spruce shader fade coverage passed subsequent compilation/correctness runs.
- Shader compilation and isolated GPU fade-mask tests passed: full=4096 pixels, hidden=0, half=2045. Complementary near/billboard masks cover the full target without holes. This is a controlled helper test, not a full-scene visual or performance test.
- Final validation passed after rotation-safe bounds, overlap statistics, pooled workspace reuse, and moving real-terrain comparison calls onto background workers. A final targeted rerun also passed after restricting neighbor sampling to valid grassland tree candidates.
- Unity batch validation needs sandbox escalation for licensing IPC. Successful command launches Editor/Unity.exe 6000.4.8f1 with -batchmode -nographics -projectPath and -executeMethod DistantTreeValidation.RunBatch. Log: Library/DistantTreeValidation.log. No user scene was opened or saved.
- User said "continue. usage reset"; continue the same task, do not consume a usage credit or create a scheduled task.

## Inspector controls
Location: WorldManager > Tree Settings > Distant Tree Coverage / Distant Tree Streaming.

| Control | Default | Behavior |
| --- | --- | --- |
| Enable Distant Trees | On | Restart Play Mode after changing; off restores legacy billboard rings. |
| Distant Tree Distance Chunks | 18 | Radial world distance in chunk widths; limited to loaded terrain. Automatically enlarged if needed to surround the circular 3D-tree radius plus its fade. |
| Fade Width Chunks | 2 | Outer cutoff fade width. |
| Thinning Start Chunks | 8 | Automatically raised beyond the circular 3D handoff. |
| Density | .45 | Approximate retained coverage of non-protected trees at outer range. |
| Protected Count | 2 | Spatial representatives retained per logical chunk. |
| Transition Seconds | .65 | Temporal 3D/billboard and initial-load fade. |
| Terrain Conform | 1 | Strength of seating on coarse terrain. |
| Height Blend Speed | 20 | World units/sec; zero snaps immediately. |
| Worker Count | 1 | Maximum concurrent placement jobs, clamped 1–4. |
| Results Per Frame | 1 | Completed manifests installed per frame, clamped 1–8. |
| Cache Chunks | 2048 | Soft limit; active manifests are protected from eviction. |

Distance, density, fade, conformity and budget controls update while playing. Restart for enable/disable, prefab, world-generation or placement changes. Existing GameObject Tree Chunk Ring Radius still controls where 3D trees are requested. For the current scene it is 7; the old billboard start ring 18 is bypassed.

## Validation / resuming
- Correctness menu: Tools > Terrain > Validate Distant Trees (correctness only).
- Batch correctness: DistantTreeValidation.RunBatch.
- Batch shader + GPU mask correctness: DistantTreeValidation.RunGraphicsBatch (omit -nographics). Log: Library/DistantTreeGraphicsValidation.log.
- No benchmarks or frame-time measurements were run, per user instruction.
- Full-world appearance and performance tuning remain for the user's manual test. No scene settings or source prefab/material assets were edited.
- Deliberately deferred: forest-cluster impostors, new billboard artwork, indirect GPU culling, and a full spatial 3D LOD rewrite.
- All implementation work requested for this first version is complete. Resume from user feedback or manual test findings; do not restart implementation or rerun completed checks without a new reason.

## Follow-up: circular foliage coverage
- Changed tree representation, warm retention, bushes, rocks, grass, flowers, clover, dandelions, and their chunk pre-generation ranges to circular chunk-distance membership.
- Billboard grass now excludes the same circular near-grass region, preventing a missing corner band. Grass subchunk generation coverage is circular too; its traversal order can still scan square shells before filtering.
- Boundaries remain quantized to whole chunks and centered on the player chunk. Terrain LOD/stitching code is unchanged.
- Distant-tree minimum distance/thinning safeguards now use the circular inner radius plus two chunks of handoff padding, rather than the old square diagonal multiplier.
- Added radius-zero, diagonal rejection, 3-4-5 boundary, negative-coordinate, and tree/ground-cover consistency checks.
- Follow-up validation: whitespace check passed. Unity batch correctness launch exited before tests because this project is open in another Unity editor session; the new circular checks remain available through the correctness menu. No performance tests were run.

## Spruce LOD3 color and simpler far shader
- Spruce_LOD3_v01 uses Spruce_Billboard_Tree_Mat and Custom/SpruceBillboardVariationSimpleLitCutout.
- Connected matching Spruce_LOD2_Mask_0d red-channel needle mask; linear import. Albedo alpha still controls holes, mask only separates needles from bark.
- Matched palette mixing rules and current 3D material variation .52, contrast .34, tip .1, ambient .3. Billboard uses multiplicative captured texture detail; exact appearance still depends on capture lighting and flat normals and needs user visual comparison.
- Runtime-owned distant material enables SPRUCE_FAR_SIMPLE (multi_compile_local prevents build stripping). Vertex palette variation, early dither rejection, no extra procedural canopy darkening, simple main light + ambient + fog. Source material retains full lighting outside distant manager.
- No performance tests. Static diff check passed; shader compilation/visual validation not yet confirmed in the open Unity session. Restart play mode to rebuild runtime material clones and enable new variant/mask.

## Camera-depth band ordering
- Distant Tree Depth Ordering defaults on; Distant Tree Depth Bands defaults 32 (4-64). Live controls; no near LOD or density changes.
- Visible instances are stably bucketed by camera-forward depth each frame, separately per shared mesh/material. Bands span the visible batch's minimum/maximum base depth. Whole-tree bases approximate extent; within-band order remains the original chunk traversal order.
- Stable counting sort uses reusable pending records, index storage and 64 counters. Linear CPU work and extra retained memory; buffers can allocate when a new peak instance count is reached. No steady-state sorting allocations.
- Draws still pack 1023 instances across band boundaries, preserving draw counts for identical visibility. Unity can reorder separate draw groups; different species/materials are not globally depth sorted. This is depth-efficiency ordering, not occlusion culling.
- Static diff check passed. External dotnet build was attempted but stopped in Unity RenderPipelines.Core package (CS8168/CS8347 in PassesData.cs), so compilation of these changes remains unconfirmed in Unity. No performance tests run.

## Density-aware distant stands
- Enabled by default: Distant Tree Density Aware. Existing Distant Tree Density (.45) is the dense-interior outer target, not a second multiplicative reduction. Disable awareness for uniform distance thinning.
- Cached 4x4 cells per logical chunk store tree count and estimated circular crown area from billboard bounds. Each tree samples a 3x3 cell neighborhood across adjacent chunk manifests. Three or fewer neighborhood trees are protected. Edge exposure is the fraction of empty surrounding cells.
- Distant Tree Crowding Threshold defaults .35 summed crown area / sampled ground area; thinning ramps from this threshold to twice it. Distant Tree Edge Protection defaults .8. Existing thinning start, transition duration and protected representatives remain effective.
- Neighbor insertion/replacement/eviction invalidates only adjacent cached crowding. Missing manifests count as open space (conservative retention). Density approaches updated targets temporally; the inner handoff remains full density. Surviving positions and stable priorities never change.
- Limitations: approximate crown footprints, grid-scale edge detection, no skyline/view-dependent protection, extra per-tree cached arrays; recently streamed stands can initially retain more trees. This is not occlusion culling and improvements require user benchmarking.
- Whitespace/static review passed. External compilation could not complete because Unity dependency DLLs are absent from Temp/bin/Debug; Unity compilation and scene appearance remain to verify. No performance tests run.

## Uniform Spruce billboard color (supersedes masked color above)
- User requested uniform dark green over every surviving pixel, including trunk. Spruce billboard forward shader now uses Uniform Dark Green (_BaseNeedleColor) times Base Tint, retaining lighting/fog and fades.
- Albedo texture is used only for alpha; separate needle-mask binding, sample, color-variation function and vertex interpolation removed. Obsolete variation/mask controls removed from shader inspector. Original texture assets remain available.
- Static diff check passed; no performance tests or Unity visual/compilation validation this turn. Restart play mode to refresh cloned materials.
- Correction: restored the prior texture-modulated Spruce leaf palette, contrast and far vertex variation; applies to all opaque pixels including bark, with no needle-mask sample. Supersedes uniform flat color above. Static checks passed; no performance tests or visual verification.
