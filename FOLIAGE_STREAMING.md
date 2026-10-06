# Foliage streaming and reflection ownership

Updated October 6, 2026.

## Runtime owners

- `GrassStream` owns grass discovery, versioned candidates, scheduling and representation transitions. `ResidentGrassRenderer` owns resident buffers and CPU instancing fallback. `GrassRenderUtility` supplies the shared GPU layout, capabilities and bounds.
- `DistantTreeManager` and `StandingTreeRenderer` own tree visuals. `TreeRegistry` owns tree identity/state; `TreeGameplayManager` owns nearby physical/query proxies.
- `FoliageManager` coordinates species ranges and per-chunk readiness. `FoliageRenderAssets`, `FoliagePlacementService`, `FoliageDiscoveryScheduler` and `FoliagePublicationScheduler` own asset binding, registration, discovery and final-publication queues. `ChunkFoliageRuntime` owns ground render batches and bush/rock objects. See [WORLD_ARCHITECTURE.md](WORLD_ARCHITECTURE.md) for composition and ownership.
- `FoliagePublication` owns one staging operation. A shared kind enum covers flowers, lily pads, cattails, clover, dandelions, bushes and rocks.

The old per-chunk grass render component, prefix/scatter compute kernels, grass generation/publication queues, tree billboard caches and visual tree GameObject pools are removed. Historical validator menu names route to resident grass checks. Synchronous grass authoring helpers and the old independent billboard fixture distribution remain inside `UNITY_EDITOR`; they are excluded from player compilation and are not a runtime fallback.

## Publication

One FIFO publication runs at a time, using `renderBatchRebuildBudgetMsPerFrame` and the shared foreground foliage budget. At least one slice advances even when earlier work exhausts the shared budget. `maxRenderBatchRebuildsPerFrame` caps completed publications. Zero time budgets retain the existing count-only behavior.

Ground work counts groups in slices, allocates one final batch of at most 1,023 instances per step, then transforms at most 128 instances per step. There are no schedule-and-immediately-complete transform jobs or final whole-chunk array copies. Completed lists transfer directly to the runtime, retaining old displayed batches until replacement is ready. Empty output is a completed replacement.

Bushes and rocks instantiate one object per step beneath an inactive staging root. Completed roots replace their previous representation; cancelled staging roots are destroyed. A single prefab instantiation or root activation/destruction remains an indivisible Unity operation, so budgets are approximate rather than hard frame-time guarantees. Object pooling is not added in this pass.

Publication rejects changed record/runtime ownership, pooled-runtime resets, source data/list replacement, source count/revision changes, generation clears, changed terrain maps/plans, changed root transforms and changed clover asset counts. Use the data's `Clear*` methods when changing generated lists so revisions invalidate outstanding work. Disposal cancels staging work.

## Rendering

`CullResidentGrassAll` selects distance density and representation once per candidate and routes it into meadow/forest near/far lists in one dispatch per rendered chunk. Each selected channel retains its own mesh and wind bounds. Missing LOD assets force selection to the available representation. Indirect counts remain on the GPU; CPU instancing and cached source data remain available for unsupported/custom compute shaders.

World-owned planar reflections query active normal/far water mesh bounds against the source camera frustum, including retained outgoing handoff meshes. Hidden replacements, pooled objects and camera masks excluding Water cannot enable the reflection. Invisible/below-water periods invalidate the old reflection so re-entry renders immediately. Standalone reflection components without a streaming visibility provider keep their original behavior. Resolution and moving/stationary update rates are unchanged.

## Validation and profiling

Run **Tools > Terrain > Validate Foliage Optimizations (synthetic)**. This tests four-channel compute/reference parity beyond 1,023 candidates, mesh bounds, missing assets, stale counts, partial uploads, publication packing/variation/transforms, empty replacement, invalidation/cancellation, object staging, FIFO deduplication, exhausted-budget progress, water visibility and the existing resident/ground streaming regressions.

The final isolated Unity run passed: `.utmp/foliage-optimization/validation-final.log`. Runtime, editor and player C# compilation and Direct3D compute compilation passed. No live scene/camera audit or FPS benchmark was run.

Profile the same warmed-up walking route after restarting Play mode. Compare `FS.Streaming.Foliage.PublishSlice`, grass dispatch/submission costs and `FS.Water.ReflectionRender`, along with frame-time percentiles and allocations. Discovery input preparation, grass tile uploads and Unity object activation can still exceed a single nominal slice budget.
