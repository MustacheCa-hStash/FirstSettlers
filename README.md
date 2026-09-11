# FirstSettlers Work Notes

## September 11 correction: broad sparse mountains, no terraces

User rejected the bench/terrace approach after seeing repeated horizontal bands. Removed all bench settings and height remapping. MountainSpatialScale defaults to 2.4, stretching both profile and region noise in XZ while keeping mountainRelief unchanged. MountainSparsity defaults to 0.12, raising only the lower mask threshold to reduce coverage while retaining a maximum mask of 1. Layout changes on regeneration. Existing rolling base land noise supplies hills at varying elevations inside larger mountain regions. GentleMountainErosion defaults to 0.45 on shallow base slopes, smoothly restoring full erosion on steeper faces; no altitude bands.

Retained lowland half-height and shoreline smoothing. LowlandHillVariation 0.8 and LowlandHillScale 900 add spatially varied positive relief boosts on dry land, with river carving last. Settings version 4 migrates the new fields while retaining prior lowland/erosion choices. Scene edits belong to user and remain untouched. CPU/GPU refactoring explicitly prohibited for this task.

Runtime/editor compilation passed (existing BerryBushManager obsolete API warning). Isolated Unity with synchronous Burst passed all checks: spatial scale preserves the isolated mountain height profile and divides gradients by 2.4; sparsity eliminated mountain mask at 2,608 sampled locations; 4,851 samples exhibited larger lowland hills; shoreline continuity and absence of mountain height remapping passed. Existing full near/far/macro/collider/seam suite passed across three seeds and custom settings. Log: .utmp/erosion-check/spatial-unity-validation.log. Previous bench slope statistics are obsolete. Visual appearance still needs live camera review; regenerate terrain.

## Current terrain system: whole-world erosion overhaul

User scope: erosion across the entire world, adjustable Inspector parameters and spatial scale, remove legacy fake mountain ruggedness, investigate circled stipple/striping artifacts, preserve flatter river-shaped broad valleys and rounded gully floors. Heightmap quality takes priority over old biome/slope placement rules. User deleted MountainDetailValidation and MountainErosionValidation during development to compile. They remain removed; WorldErosionValidation is the replacement.

Implemented:

- WorldErosionSettings: serializable snapshot on WorldManager; base elevation/relief/roughness, global erosion amplitude/wavelength/octaves, stretch/rotation/offset, gully/rounding/fade controls, minimum wavelength, maximum terrain vertex spacing. Regenerate Terrain Inspector button recreates the world with a consistent snapshot.
- WorldTerrainHeight: smooth rotated gradient-noise base with analytical derivatives, one world-coordinate erosion pass after base composition. Mountain width now expands the smooth broad mask; old summit stretching and max-unioned eroded copies are retired. Legacy sampling-array/anchor arguments remain compatibility plumbing but are not used to form terrain.
- Erosion settings are threaded through near requests, far/macro jobs, snow resampling and distant-tree placement. Existing final-height normals/slopes/collision stay coherent. Far meshes now respect maxMeshSpacing; near LODs cap spacing to a divisor of chunk size.
- Reproduced a real artifact source: the upstream cubic float hash collapsed 1,024 cells to ONE offset at seed 568317; integer replacement yields 1,024 unique offsets. Also smooth internal direction changes at crests and remove finite-difference input slopes.
- Fixed outgoing macro/incoming chunk overlap by suppressing incoming terrain/water until complete macro handoff, retaining coverage without simultaneous overlapping draws.
- Added riverValleyWidth / riverValleyFlattening; broad dry river valley shaping is applied after global erosion, while valleyRounding controls erosion gully floors.

Verification / current handoff:

- Runtime compilation currently passes (existing BerryBushManager obsolete API warning).
- Offline current-base test: 12,675 positions, eroded lowlands 7,531 / mountains 1,910 / seabed 1,275. Analytic-gradient comparison maximum error 1.85e-5; bounded global displacement and disabled/zero bypass passed. Temporary harness and preview in .utmp/erosion-check.
- WorldErosionValidation is added and passed in isolated headless Unity 6000.4.8f1 with synchronous Burst compilation. Covers 5,043 base/erosion samples (3,001 lowland / 763 mountain / 514 seabed changed; max derivative error 1.08e-5), off/zero bypass, real sampler valley shaping, 3 large hash seeds, handoff visibility, default/custom rotation/stretch/amplitude/spacing settings, near/far/macro/collider agreement and X/Z height/slope seams across three seeds. Logs: .utmp/erosion-check/world-unity-validation.log. Runtime/editor compilation passes.
- User opened/saved SmearScene with settings during work; their choices were preserved. Settings version 2 migrates only the newly added river valley controls in version-1 snapshots. Old mountain validation files remain deleted; use Tools > Terrain > Validate World Erosion.
- Generated hillshade inspected. The exact user screenshot has not been reproduced in the live scene. Remaining user-facing review: appearance at their camera and streaming/GPU frame cost with their chosen mesh spacing; do not claim measured performance improvement. See MOUNTAIN_DETAIL.md for the full current controls and behavior. No compute migration has been attempted.

## Historical notes: shared slope angles and mountain meadows

- `SlopeMap` now stores degrees: atan(raw height gradient * meshHeightMultiplier). The terrain request passes the active multiplier; far mesh/control samples and sparse distant-tree placement use the same conversion. Uniform worldScale cancels. Raw gradient maps remain derivatives for normals and MountainSnow.
- `TerrainSlopePolicy` centralizes biome decisions for managed, Burst near-map and far-control classification. The coarse far mesh uses the same policy with neutral climate because it has no climate grid.
- Forest canopy fades from 25 to 45 degrees; forest/tree eligibility ends at 45. Grass remains eligible through 63 degrees (old 0.01 at multiplier 200 is 63.43 degrees). Warm strong-mountain benches below normalized height 3 can become Grassland; moderate mountain shoulders can become meadows. Cold snow and high alpine terrain remain protected. Existing grass generation/density/ranking consumes the new Grass surface normally.
- Loose sand/mud limits are 40 degrees; cliffs begin at 70. Flower/clover/dandelion defaults and all three SmearScene values are 40 degrees. Forest floor exposure begins at 35 degrees. Debug overlay explicitly displays degrees. Existing slope-based ecology checks now use degree thresholds.
- Runtime and editor C# compile checks passed (existing BerryBushManager warning only). Twelve executable checks passed against the actual shared policy and Unity.Mathematics, covering conversion, multiplier changes, forest/grass boundaries, cold/alpine/cliff/water exclusions. Also available via Tools > Validation > Terrain Slope Policy. Updated old validation fixtures to degrees. Unity scene visuals and Burst runtime execution still require validation; restart Play mode to regenerate cached terrain/ecology.


## Terrain handoff hole fix

- Found an asynchronous coverage gap in `RebuildActiveChunkSet`: outgoing macro tiles and individual chunks were destroyed before budgeted replacement mesh work finished. This can recur at particular macro-tile boundaries at any travel speed and affects terrain and water together.
- Keep outgoing runtimes until all currently wanted replacement chunks have attached terrain meshes, or the replacement macro tile has attached its mesh. Generated-but-unapplied data does not count as ready. Outgoing individual chunks stop foliage and collider work. Runtimes outside the desired coverage are released; reversing direction reuses retained runtimes.
- Runtime C# compilation passed against Unity 6000.4.8f1 references (existing BerryBushManager obsolete-API warning only). Scene reproduction/benchmarking remains for Unity. The whole-world erosion overhaul above supersedes the temporary-overlap behavior by suppressing incoming terrain/water until the macro handoff completes.

## Earlier Task: Compute-Driven Grass

User authorized extending the successful tree approach to grass while preserving existing generation, density settings and selection-rank clumps.

### Implemented

- Added resident indirect rendering for near grass and billboard grass. Enabled in SmearScene through `GrassSettings.gpuIndirectRendering` and the assigned `grassCompactShader`.
- Generation, exclusion rules, biome data, seeds, scales, bucket sorting and density selection remain authoritative on CPU. Near grass retains each subchunk's sorted prefix, including the minimum-one rule at positive density. Billboard grass retains the same per-cell prefixes with deterministic fractional rounding. No new random ranking or density distribution was introduced.
- Factored the existing prefix-count calculations into shared helpers for correctness checks. Near-density edits now schedule reselection through the existing budgeted rebuild queue, including chunks returning onscreen after an edit.
- `GrassIndirectRenderer` uploads the existing selected matrices and instance data only when render data/mesh bounds change. It merges the old 1,023-sized batches into one indirect draw per chunk and representation.
- `GrassCompact.compute` performs wind-aware per-clump frustum culling and stable prefix/scatter compaction. It emits source indices rather than copying whole records; source order and rank-derived wind phase remain stable.
- The grass shader adds a procedural variant while preserving CPU instancing, forest tint, wind, fade/dither, normals and shadow reception. Culling uses the configured viewer camera; a missing camera disables per-clump culling.
- GPU counts stay on GPU during normal rendering. Buffer ownership follows the foliage root; clearing, disabling/destroying the root, switching off indirect rendering, or replacing assets releases/rebuilds resources.
- Unsupported shaders/devices, an unassigned compute shader, or a custom instance-data property use the existing CPU fallback.
- Trees, flowers, clover and dandelions retain their existing render paths.

### How this differs from trees

Grass has many more overlapping alpha-tested blades. Reduced CPU draw submission and GPU visibility work can help, but on-screen overdraw and wind/shading cost can still dominate. This implementation preserves CPU generation and rank-prefix selection; it does not move grass generation or the density selector to compute. It batches per chunk rather than globally across the world, retaining chunk streaming ownership and billboard fade behavior.

### Verification and Unity handoff

- Runtime and editor C# compilation passed against Unity 6000.4.8f1's generated references (the existing BerryBushManager obsolete-API warning remains).
- Offline Direct3D compilation passed for all three grass compute kernels and 12 grass forward shader stages: ordinary, CPU-instanced and procedural vertex/fragment variants, with fade off/on.
- The actual extracted prefix-count helpers passed 18,018 density/count combinations for monotonicity, zero/full density and bounded counts using equivalent managed Mathf operations.
- `git diff --check` passed.
- Run **Tools > Terrain > Validate Grass Compute (correctness only)** in Unity. This checks GPU ordering, nested density prefixes, forest/wind instance data, more than 1,023 clumps, growth/replacement/disposal, frustum rejection and wind bounds. GPU readback occurs only in this explicit validation command.
- Unity import, GPU correctness execution, scene visuals and benchmarks remain for the user. Compare `gpuIndirectRendering` on/off using the same warmed-up route and density settings. Inspect near/billboard transitions, clump identity while changing density, wind at screen edges and returning to streamed chunks.
- CPU profiler markers: `FS.Grass.IndirectUpload` and `FS.Grass.IndirectCull`. Upload work should occur on rebuilds, not every frame.
- Existing render geometry statistics report selected candidates, not post-culling indirect counts. Use the Frame Debugger/GPU profiler for actual draws and GPU timing.
- CPU fallback arrays are retained to allow switching paths. GPU capacity is retained until clear/dispose; very small batches may not gain from compute dispatch overhead.
- Other scenes must assign `Assets/Shaders/GrassCompact.compute` to the new grass setting to enable this path.

## Previous Task: Compute-Driven Distant Trees

Previous task: complete/fix the tree compute path. The user subsequently reported a noticeable latency/performance improvement and authorized extending the approach to grass.

### Current implementation

- `DistantTreeManager` retains authoritative chunk manifests, background placement, neighbor-aware ecology, terrain seating and near GameObject/collision handoff.
- `DistantTreeGpuBatch` keeps transforms, tints, selection priorities, ecology and conservative bounds in resident GPU slots. Static records upload only on registration, ecology changes or buffer growth. Each frame uploads a 16-byte height/load/handoff state per resident slot.
- `DistantTreeCompact.compute` performs per-tree range/frustum culling, density smoothing, stable thinning, coverage calculation and depth-band selection. Prefix/scatter passes compact in stable front-to-back bands without CPU sorting or unordered append behavior.
- Indirect argument counts stay on GPU. Each supported mesh/material uses one indirect draw, without the 1,023-instance limit.
- Buffer growth copies existing GPU density state. Manifest replacement/eviction recycles slots; settings/shader changes rebuild GPU resources; disposal releases all buffers.
- CPU instanced drawing remains available when disabled, unsupported, unassigned or using a material without the `DistantTreeIndirect=True` tag.

### Shader and correctness fixes

- Removed the reserved HLSL field name `matrix` and duplicate `instanceID` declarations that caused shader errors.
- Uses Unity procedural instancing (`SetupDistantTree`) and a conditional shader-model-4.5 requirement for indirect variants. Ordinary/CPU-instanced variants retain their original shader target.
- Shared `DistantTreeInstance.hlsl` supplies transforms (including inverse TRS for normals), tint access and per-instance fade data through Unity's instance ID.
- Removed the manually enabled indirect keyword that could break CPU fallback. Unity selects procedural variants for indirect draws.
- Added indirect support to the common billboard, birch, red maple, sugar maple and spruce variation shaders, plus the red maple/sugar maple/spruce/white pine LOD2 billboard shaders. Species-specific coloration is preserved; shaders with per-tree tint now read it from the indirect buffer. The common billboard now consumes its tint on both paths.
- Draw and culling bounds include scaled, rotated and offset geometry as well as upright camera-facing cards.
- Grass shaders, grass rendering and near-tree collision behavior were not changed.

### Local verification completed

- Runtime C# and editor C# compilation passed using Unity 6000.4.8f1's Roslyn compiler and this project's generated response-file references. One unrelated existing obsolete-API warning remains in `BerryBushManager`.
- Offline Direct3D HLSL compilation passed for all four compute kernels and 60 forward shader stages: ordinary, CPU-instanced and procedural vertex/fragment variants across all nine supported billboard shaders, including spruce's far-simple variants.
- These HLSL checks use the project's actual package includes and an offline Unity-style preamble; they do not replace Unity shader import, build-variant validation or scene rendering.
- `git diff --check` passed.
- No Unity scene visual checks or performance benchmarks were run in this pass.

### Unity validation handoff

1. Let Unity reimport and compile, and check the Console.
2. Run **Tools > Terrain > Validate Distant Tree Compute (correctness only)**. This explicitly checks shader variants, buffer strides, scaled/offset bounds, more than 1,023 instances, frustum rejection, ordering, tint/fade transport, thinning, zero visibility, density preservation during growth and slot reuse. The check intentionally reads GPU buffers; it does not run during normal rendering.
3. The existing **Validate Distant Trees (correctness only)** command remains available for placement, terrain sampling and stable identity checks.
4. In SmearScene compare compute enabled/disabled: every species, terrain seating, screen edges, near/far dither handoff, streaming, rapid turns and returning to previously visited chunks.
5. Benchmark the same route/camera, density, draw distance and warmed-up terrain in both modes. Record main/render thread time, GPU frame time, draw calls and memory. Allow transitions to settle after switching modes.

### Remaining limitations / benchmark considerations

- CPU still visits active trees for terrain seating and dynamic state submission; placement, ecology and near-tree lifecycle stay on CPU. This is not fully GPU-generated foliage.
- Resident buffers retain their peak capacity until disabled/recreated/disposed. Eviction reuses slots, but holes still incur inexpensive inactive compute work.
- Depth ordering is approximate per mesh/material, not a global sort across species.
- Runtime render statistics report submitted candidates, not the GPU-visible count. Use the Frame Debugger/profiler or the explicit correctness check when inspecting actual indirect counts.
- CPU and GPU maintain separate density histories. Switching modes rebuilds GPU buffers; allow the configured transition interval to settle before comparing screenshots or timings.
- Distant tree shadows remain disabled, as before.
- Grass now has its own implementation described above; the tree path is unchanged in this pass.
