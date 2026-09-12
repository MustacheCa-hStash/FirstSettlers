# Streaming performance priorities

Updated September 12, 2026. Use the user's walking route and camera for performance checks. Do not run automated scene/camera tests. Synthetic correctness tests are permitted. CPU/GPU terrain refactoring remains deferred.

## Current pass: priorities 1 and 2

- [x] Remove redundant LOD triangle list -> array -> list copies; transfer ownership of the completed builder list.
- [x] Reuse flower/dandelion matrix and instance-data scratch lists and per-prefab clover scratch lists between chunks. Final runtime render batches still own their arrays.
- [x] Cache the grass scheduling comparison delegate instead of allocating a capturing lambda every scheduling pass.
- [x] Defer flower, clover and dandelion Burst discovery instead of scheduling and immediately waiting on the main thread.
- [x] Collect discovery results in 256-candidate slices under the existing ground foliage time budget. Use a single resident discovery job to bound memory and worker pressure; FIFO requests prevent repeated nearest-first starvation.
- [x] Queue clover exclusions from grass preparation and wait for readiness; avoid synchronous clover generation in the grass path.
- [x] Reject clear/replaced-map/replaced-data results and dispose native allocations on completion/shutdown. Keep GPU-instanced grass and existing foliage rendering paths.
- [x] Runtime/editor compile and synthetic Unity job/queue correctness tests passed; log: `.utmp/erosion-check/ground-foliage-integration.log`.
- [ ] User performance check: same warmed-up walking route; compare ProcessGroundGenerationQueue, GC.Alloc, GC.Collect/CollectIncremental, and CPU frame spikes. Compilation/correctness checks are not performance evidence.
- [ ] Capture allocation call stacks if significant collection spikes remain. Identify terrain, foliage and Editor allocations separately before expanding pooling. This pass removes confirmed redundant allocations; it does not establish that all GC sources are eliminated.
- [ ] If remaining ground foliage spikes sit in batch building or setup, split native-map preparation, transform job completion and render-batch publication further. Current budget cannot preempt one setup or batch operation.

## Next priorities (not implemented in this pass)

3. **High: chunk boundary and visible-content work.** RebuildActiveChunkSet was 4.003 ms; ProcessVisibleChunkContentQueue was 4.580 ms. Review repeated scans/rebuilds and divide large publication/activation operations into bounded stages. Preserve near/far handoff coverage.
4. **Medium-high: LOD requests and caching.** Worker.LODMeshRequest was 12.416–15.395 ms. Inspect duplicate requests, discarded results, cache lifetime, worker contention and main-thread waits in Timeline. A worker duration is not itself a main-thread stall. Index-copy removal above addresses allocations only.
5. **Then: far terrain publication and runtime reuse.** Profile actual FarTerrain markers on the main thread before changing its generator. Review mesh/texture upload, runtime construction/destruction and pooling; budgets checked between whole results cannot limit the cost of one result.
6. **Later: remaining foliage rendering.** Inspect the user's forest Frame Debugger capture; separate trees, flowers/clover/dandelions, shadows and terrain. Consider resident/hybrid foliage rendering and cross-chunk batches only after streaming spikes are addressed.

## Evidence from supplied captures

- CPU active frames: 18.877, 13.697 and 20.625 ms; reported GPU times below 0.7 ms.
- Ground generation: 6.523 ms; FlowerDiscoveryJob (Burst): 3.258 ms on another frame.
- GC.Collect: 3.061 and 5.451 ms; incremental collection: 3.011 ms.
- Highlights includes worker-thread markers and nested scopes; do not add those times together or attribute all of them to the main thread. Next useful evidence is CPU Timeline plus allocation call stacks.
