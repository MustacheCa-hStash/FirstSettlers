# SmearScene Architecture and Performance Audit

Scope: only systems referenced by `Assets/Scenes/SmearScene.unity` or reached from those systems at runtime. I did not inspect unrelated editor validation tools except where they clarified runtime code paths.

## Scene Configuration Context

`SmearScene` is configured as a very large streaming test:

- `viewDistance: 100`, `chunkSize: 128`, `worldScale: 0.3`.
- Far terrain begins at ring `9`, uses `4x4` macro tiles, and keeps broad distant trees enabled.
- Grass uses GPU indirect rendering, `cellsPerAxis: 125`, `subChunksPerChunk: 10`, `maxConcurrentGrassJobs: 8`, and up to `8` grass uploads per frame.
- Terrain result application can spend up to `5 ms` total per frame, with mesh creation, collider creation, texture creation, runtime GameObject creation, foliage, and distant tree work all happening around the same update loop.

This means the architecture needs to be less about average throughput and more about avoiding indivisible main-thread spikes. Many current systems have budgets, but several individual operations are still too large to be safely hidden inside those budgets.

## Highest Impact Improvements

### 1. Store chunk data in native, flat, reusable layouts instead of repeatedly flattening managed 2D arrays

Relevant files:

- `Assets/Scripts/TerrainGeneration/TerrainRequestManager.cs`
- `Assets/Scripts/FoliageGeneration/FoliageGenerator.cs`
- `Assets/Scripts/TerrainGeneration/MeshGeneration/MeshGenerator.cs`
- `Assets/Scripts/TerrainGeneration/MeshGeneration/Collider/ColliderMeshGenerator.cs`
- `Assets/Scripts/ChunkManager/ChunkRecord.cs`

The terrain pipeline currently generates and stores core maps as managed multidimensional arrays such as `float[,]`, `SurfaceType[,]`, `BiomeType[,]`, and `GroundCoverType[,]`. Later systems repeatedly copy those maps into `NativeArray`s before Burst jobs can use them.

The clearest example is grass subchunk generation. Each scheduled grass subchunk flattens the entire height, surface, biome, and ground-cover maps, plus tree/bush/rock/clover exclusion lists, before scheduling a job. With `subChunksPerChunk: 10`, that can mean up to 100 repeated full-map copies per chunk over time. The Burst work itself is sensible; the data boundary is the expensive part.

Recommended architecture:

- Make `ChunkRecord` own a generated `ChunkNativeData` cache: flat native height, slope, biome, surface, water, ground-cover, and river maps, plus dimensions and version stamps.
- Produce that layout once when terrain data completes, then share read-only slices with mesh, collider, foliage, grass, and debug sampling.
- Dispose native data when the `ChunkRecord` is evicted, not when each subchunk job completes.
- Keep managed arrays only if editor/debug inspection needs them, or migrate debug sampling to flat accessors.

Expected effect:

- Less main-thread and worker-side copying.
- Lower allocation pressure.
- Cleaner ownership between terrain generation and foliage generation.
- Grass jobs become truly small subchunk jobs rather than small jobs wrapped in full-chunk data marshaling.

### 2. Replace per-chunk terrain GameObject churn with pooled chunk runtimes and shared materials

Relevant files:

- `Assets/Scripts/ChunkManager/ChunkRuntime.cs`
- `Assets/Scripts/ChunkManager/FarTerrainTileRuntime.cs`
- `Assets/Scripts/ChunkManager/ChunkManager.cs`
- `Assets/Scripts/FoliageGeneration/FoliageManager.cs`

Normal terrain chunks create a root GameObject, water child, `MeshFilter`, `MeshRenderer`, runtime terrain material, runtime water material, and sometimes a `MeshCollider`. Foliage creates another root object. Far terrain does a similar smaller setup. Destruction tears those objects and materials down.

Recent code already budgets runtime creation, but one constructor/destructor is still indivisible. The profiler frames where `RebuildActiveChunkSet`, `NormalCleanup`, `GameObject.AddComponent`, and physics simulation showed up fit this pattern: even budgeted object lifecycle work can trigger Unity internals, transform hierarchy updates, renderer registration, material ownership, collider cooking/registration, and later physics costs.

Recommended architecture:

- Use a pool for `ChunkRuntime` and `FarTerrainTileRuntime`.
- Reuse GameObjects, renderers, water child objects, and material/property blocks.
- Prefer shared terrain/water materials plus `MaterialPropertyBlock` for per-chunk control maps and shadow flags.
- Avoid destroying runtime GameObjects during normal streaming; deactivate and recycle them.
- Keep collider ownership separate from visual runtime ownership, so collider attach/detach can be delayed and budgeted independently.

Expected effect:

- Fewer spikes from `GameObject`, `AddComponent`, `Destroy`, material instantiation, and transform hierarchy changes.
- Less chance of a cleanup frame causing a later physics spike.
- Easier handoff logic, because hidden outgoing visuals can become pooled inactive visuals after the replacement is confirmed.

### 3. Treat collider streaming as its own system with hysteresis and stricter throttling

Relevant files:

- `Assets/Scripts/ChunkManager/ChunkManager.cs`
- `Assets/Scripts/ChunkManager/ChunkRuntime.cs`
- `Assets/Scripts/TerrainGeneration/TerrainRequestManager.cs`
- `Assets/Scripts/TerrainGeneration/MeshGeneration/Collider/ColliderMeshGenerator.cs`
- `Assets/Scripts/PlayerMovement/PlayerMovement.cs`

The player uses `CharacterController`, so movement is not Rigidbody gravity, but it still queries Unity physics/colliders through `controller.Move`. The profiler frame that spent most of its time under `Physics.Simulate` was not necessarily caused by player code directly; it may be the physics world reacting to terrain collider changes, fixed-step timing, broadphase updates, or accumulated physics work.

Current collider streaming is tied to chunk range and runtime lifecycle. Outgoing chunks remove colliders when they leave the active set; incoming chunks request/apply collider meshes near the viewer.

Recommended architecture:

- Maintain a dedicated collider ring with hysteresis: keep colliders for an extra ring or two after leaving range, and retire only a few per frame.
- Apply at most one terrain collider mesh per frame until proven safe.
- Pool `MeshCollider` components where possible, or keep components alive and set `enabled`/`sharedMesh` carefully.
- Consider a lower-resolution analytic height query for player grounding and movement, using collision only for objects that truly need physics.
- Add profiler markers specifically around collider attach, detach, `sharedMesh` assignment, and `CharacterController.Move`.

Expected effect:

- Fewer physics broadphase spikes.
- More direct proof of whether stutters are physics-world updates or visual/content streaming.
- A cleaner test path for disabling player collision without disabling terrain generation.

### 4. Move terrain mesh creation onto Unity's MeshData API path

Relevant files:

- `Assets/Scripts/TerrainGeneration/MeshGeneration/MeshGenerator.cs`
- `Assets/Scripts/TerrainGeneration/MeshGeneration/WaterMeshGenerator.cs`
- `Assets/Scripts/TerrainGeneration/MeshGeneration/FarTerrainGenerator.cs`
- `Assets/Scripts/ChunkManager/ChunkManager.cs`

Worker threads currently produce custom `MeshData` objects that hold managed arrays/lists, then the main thread calls `CreateMesh()`. That main-thread step still allocates and uploads Unity mesh data. Water mesh creation uses `ToArray()` for vertices, UVs, triangles, and colors.

Recommended architecture:

- Use Unity's writable `Mesh.MeshDataArray` API for terrain, water, far terrain, and colliders.
- Build vertex/index buffers in native memory and apply them on the main thread with minimal conversion.
- Reuse mesh objects where possible instead of creating new `Mesh` instances for every LOD result.
- For water, avoid list-to-array copies when creating meshes.

Expected effect:

- Lower main-thread cost when completed mesh results arrive.
- Fewer managed allocations from mesh arrays/lists.
- Better alignment with the rest of the jobified generation pipeline.

### 5. Split `ChunkManager` into explicit streaming subsystems

Relevant files:

- `Assets/Scripts/ChunkManager/ChunkManager.cs`
- `Assets/Scripts/WorldManager/WorldManager.cs`

`ChunkManager` currently owns active-set calculation, terrain records, normal runtimes, far tile runtimes, result application, collider decisions, visibility, far/near handoff, foliage management, grass streaming, distant trees, and debug info. It works, but it makes budget interactions hard to reason about. Several independent systems share the same frame and call each other in one long update.

Recommended architecture:

- Keep `WorldManager` as the scene-facing coordinator.
- Split `ChunkManager` into smaller services:
  - `ActiveSetService`
  - `TerrainDataStreamer`
  - `TerrainRuntimePool`
  - `ColliderStreamer`
  - `FarTerrainStreamer`
  - `TerrainHandoffController`
  - `ChunkVisibilityService`
  - `FoliageStreamingCoordinator`
- Give each service an explicit `Tick(budget)` and a small output state, rather than letting one monolithic update own all side effects.

Expected effect:

- Easier performance tuning.
- Fewer accidental budget fights.
- Cleaner profiler markers that map to ownership boundaries.
- Simpler future changes, especially around macro-tile handoff and collider behavior.

## Medium Impact Improvements

### 6. Avoid sorting large active lists from scratch on each chunk transition

Relevant files:

- `Assets/Scripts/ChunkManager/ChunkManager.cs`
- `Assets/Scripts/FoliageGeneration/GrassStream.cs`
- `Assets/Scripts/FoliageGeneration/DistantTreeManager.cs`

With `viewDistance: 100`, the logical visible world contains thousands of potential chunk coordinates even after macro far terrain removes many far chunks. `RebuildActiveChunkSet` scans a full circle and sorts active coords. Grass then scans active coords again. Distant trees build and sort their own wanted list by radius.

Recommended architecture:

- Precompute ring offsets once per view-distance and far-tile size.
- Maintain incremental active-set deltas when the viewer moves one chunk.
- Store priority order as ring buckets instead of sorting all coordinates each time.
- Let grass/distant trees consume the same active/ring data instead of independently rebuilding broad sets.

Expected effect:

- Less work at chunk boundaries.
- Less latency when sprinting straight through new tiles.
- Easier to reason about what actually changed between frames.

### 7. Make grass publishing data-oriented and persistent

Relevant files:

- `Assets/Scripts/FoliageGeneration/GrassStream.cs`
- `Assets/Scripts/FoliageGeneration/FoliageGenerator.cs`
- `Assets/Scripts/FoliageGeneration/ResidentGrassRenderer.cs`

The resident grass renderer is already a good direction: fixed slots and indirect draws. The remaining issue is that completed job results are converted into managed `List<FoliageInstanceData>`, sorted, then uploaded by creating a fresh `GrassIndirectRenderer.Instance[]` for each tile. That keeps CPU-side representation convenient but adds conversion and allocation at publication time.

Recommended architecture:

- Have grass jobs write the final GPU instance layout, or a compact native intermediate layout, directly.
- Keep one persistent native buffer per chunk or per grass entry.
- Upload from native data into the resident buffer without creating a managed array per tile.
- Replace per-subchunk sort with deterministic rank selection during generation where possible.

Expected effect:

- Less variance in `ConvertJobResults`, `SortInstances`, and `PrepareUploadInstances`.
- More predictable grass streaming frames.
- Lower GC/allocation noise in development builds.

### 8. Reduce duplicate foliage systems

Relevant files:

- `Assets/Scripts/FoliageGeneration/FoliageManager.cs`
- `Assets/Scripts/FoliageGeneration/GrassStream.cs`
- `Assets/Scripts/FoliageGeneration/ResidentGrassRenderer.cs`
- `Assets/Scripts/FoliageGeneration/GrassIndirectRenderer.cs`

There are two grass paths: the older chunk foliage runtime/batch path and the newer `GrassStream`/resident renderer path. `FoliageManager` still contains broad queueing, near grass batch rebuilding, billboard grass batching, and render paths that overlap conceptually with the new streaming grass path.

Recommended architecture:

- Choose `GrassStream` as the authoritative near/billboard grass path if it is the intended replacement.
- Remove or fully disable the older near grass rebuild path in active scenes.
- Keep flowers/clover/dandelions/trees in `FoliageManager`, but move grass-specific management out.
- Use a shared render-batch abstraction for non-grass foliage so the manager is not a single huge file with many parallel code paths.

Expected effect:

- Fewer queues to prune and update.
- Less risk that old grass settings cause invalidation or batch rebuilds.
- Easier profiling because grass has one owner.

### 9. Convert near tree/bush/rock GameObject representations to pooled impostor/collider tiers

Relevant files:

- `Assets/Scripts/FoliageGeneration/FoliageManager.cs`
- `Assets/Scripts/FoliageGeneration/ChunkFoliageRuntime.cs`
- `Assets/Scripts/FoliageGeneration/DistantTreeManager.cs`

The system already has distant tree GPU/billboard logic, but near representations still involve GameObject trees/bushes/rocks. The handoff between GameObjects, billboards, and distant trees is complex and can become expensive when many chunks change representation together.

Recommended architecture:

- Use pooled GameObjects only for the small interaction/collision set.
- Use GPU instancing or indirect rendering for visual-only nearby trees/rocks wherever possible.
- Separate "can collide/interact" from "visible as high detail."
- Keep a warm pool sized to the maximum expected interaction ring rather than instantiating/destroying with chunks.

Expected effect:

- Reduced transform hierarchy churn.
- Smoother tree representation transitions.
- Lower cost when sprinting through terrain.

### 10. Make far terrain and near terrain use one LOD/tile model

Relevant files:

- `Assets/Scripts/ChunkManager/ChunkManager.cs`
- `Assets/Scripts/TerrainGeneration/MeshGeneration/MeshGenerator.cs`
- `Assets/Scripts/TerrainGeneration/MeshGeneration/FarTerrainGenerator.cs`

Near chunks and far macro tiles are separate runtime concepts with separate generation paths and handoff rules. This works, but it creates edge cases: normal-to-far handoff, far-to-normal handoff, visibility hiding, and duplicated control-map generation logic.

Recommended architecture:

- Represent all terrain as tiles in one clipmap/quadtree-like terrain streamer.
- Let a tile have an LOD, resolution, collider policy, and foliage policy.
- Treat current `4x4` far macro tiles as just lower-resolution terrain tiles.
- Keep skirts or border stitching as a tile-rendering detail, not a separate far-terrain ownership model.

Expected effect:

- Cleaner handoffs.
- Fewer special cases around `activeLastUpdate` vs `activeFarTilesLastUpdate`.
- Easier future terrain stitching between arbitrary LOD boundaries.

## Lower Impact / Cleanup Improvements

### 11. Disable expensive debug/profile logging by default in performance builds

Relevant files:

- `Assets/Scripts/WorldManager/WorldManager.cs`
- `Assets/Scripts/TerrainGeneration/TerrainGenerationProfiler.cs`
- `Assets/Scripts/WorldManager/PcgDebugOverlay.cs`
- `Assets/Scripts/WorldManager/GameTimeDebugOverlay.cs`

`SmearScene` has `logTerrainGenerationProfile: 1`. Logging every five seconds is useful while diagnosing, but performance comparison builds should have a clear "profile build" toggle so logging and overlays are not mixed into normal measurements.

Recommended architecture:

- Add a single scene-level profiling mode asset or enum.
- Gate debug overlays, terrain profile logs, and verbose markers behind that mode.
- Keep Unity Profiler markers compiled in, but avoid periodic log formatting unless explicitly enabled.

### 12. Replace scene-heavy serialized settings with versioned ScriptableObject profiles

Relevant files:

- `Assets/Scripts/WorldManager/WorldManager.cs`
- `Assets/Scenes/SmearScene.unity`

The scene serializes a large block of world, terrain, grass, foliage, tree, erosion, water, and budget settings. That makes experiments easy but also makes it hard to compare profiles and avoid accidental scene churn.

Recommended architecture:

- Create `WorldStreamingProfile`, `TerrainGenerationProfile`, `FoliageStreamingProfile`, and `RenderBudgetProfile` assets.
- Let `SmearScene` reference profiles.
- Keep a "Performance Investigation" profile separate from gameplay defaults.

Expected effect:

- Cleaner diffs.
- Easier A/B testing.
- Less chance of tuning one scene into a unique snowflake.

### 13. Use a dedicated player profiling mode

Relevant files:

- `Assets/Scripts/PlayerMovement/PlayerMovement.cs`

The current player uses `CharacterController.Move`, gravity, crouch height changes, and camera look. That is fine for gameplay, but it is not ideal for terrain streaming profiling because it can involve physics while terrain colliders are changing.

Recommended architecture:

- Add an explicit no-clip profiling mode that moves the transform directly and disables the `CharacterController`.
- Keep the same camera, height, and speed presets so the terrain route remains comparable.
- Add a marker around normal `CharacterController.Move`.

Expected effect:

- Better isolation between terrain streaming stutters and player/physics stutters.
- Faster diagnosis when a spike appears under `Physics.Simulate`.

## Notes On Terrain Stitching

Current near terrain mesh generation keeps full-resolution borders while allowing coarser interiors. That means all normal chunks carry high-detail edges, which helps avoid cracks even when neighboring interiors use different LODs. Far terrain uses skirts on macro-tile edges rather than true neighbor-aware stitching.

Architecturally, this is workable for now. If the terrain system is unified later, stitching should become an LOD-boundary policy:

- Same-LOD neighbors can share the same edge resolution.
- Different-LOD neighbors need explicit edge constraints or skirts.
- Macro far tiles should not need a separate stitching story from near tiles.

## Suggested Implementation Order

1. Add a profiling/no-clip player mode and collider attach/detach markers, because this will make future tests cleaner.
2. Pool chunk and far-tile runtimes, keeping renderers/material bindings alive.
3. Split collider streaming from visual streaming and add hysteresis.
4. Add persistent native terrain data to `ChunkRecord`.
5. Convert grass generation/upload to consume persistent native chunk data.
6. Move mesh creation toward Unity `MeshData` APIs.
7. Collapse old and new grass paths into one authoritative system.
8. Consider a unified terrain tile/LOD model once the stutter sources are under control.

## Files Reviewed

Primary scene and configuration:

- `Assets/Scenes/SmearScene.unity`
- `Assets/Scripts/WorldManager/WorldManager.cs`

Terrain streaming/runtime:

- `Assets/Scripts/ChunkManager/ChunkManager.cs`
- `Assets/Scripts/ChunkManager/ChunkRuntime.cs`
- `Assets/Scripts/ChunkManager/FarTerrainTileRuntime.cs`
- `Assets/Scripts/ChunkManager/ChunkRecord.cs`
- `Assets/Scripts/ChunkManager/FarTerrainTileRecord.cs`
- `Assets/Scripts/ChunkManager/ChunkCoord.cs`
- `Assets/Scripts/ChunkManager/SubChunkCoord.cs`

Terrain generation:

- `Assets/Scripts/TerrainGeneration/TerrainRequestManager.cs`
- `Assets/Scripts/TerrainGeneration/TerrainDataRequestResult.cs`
- `Assets/Scripts/TerrainGeneration/FarTerrainRequestResult.cs`
- `Assets/Scripts/TerrainGeneration/MeshGeneration/MeshGenerator.cs`
- `Assets/Scripts/TerrainGeneration/MeshGeneration/WaterMeshGenerator.cs`
- `Assets/Scripts/TerrainGeneration/MeshGeneration/FarTerrainGenerator.cs`
- `Assets/Scripts/TerrainGeneration/MeshGeneration/Collider/ColliderMeshGenerator.cs`
- `Assets/Scripts/TerrainGeneration/InputsToBiomeGen/HeightMapGenerator.cs`
- `Assets/Scripts/TerrainGeneration/InputsToBiomeGen/WorldTerrainHeight.cs`
- `Assets/Scripts/TerrainGeneration/BiomeGeneration/*`

Foliage and grass:

- `Assets/Scripts/FoliageGeneration/FoliageManager.cs`
- `Assets/Scripts/FoliageGeneration/ChunkFoliageRuntime.cs`
- `Assets/Scripts/FoliageGeneration/ChunkFoliageData.cs`
- `Assets/Scripts/FoliageGeneration/FoliageGenerator.cs`
- `Assets/Scripts/FoliageGeneration/GrassStream.cs`
- `Assets/Scripts/FoliageGeneration/ResidentGrassRenderer.cs`
- `Assets/Scripts/FoliageGeneration/GrassIndirectRenderer.cs`
- `Assets/Scripts/FoliageGeneration/DistantTreeManager.cs`
- `Assets/Scripts/FoliageGeneration/DistantTreeGpuBatch.cs`
- `Assets/Scripts/FoliageGeneration/DistantTreePlacement.cs`
- `Assets/Scripts/FoliageGeneration/BerryBushManager.cs`

Player/time/debug:

- `Assets/Scripts/PlayerMovement/PlayerMovement.cs`
- `Assets/Scripts/TimeSystem/GameTimeManager.cs`
- `Assets/Scripts/TimeSystem/SunCycleController.cs`
- `Assets/Scripts/WorldManager/PcgDebugOverlay.cs`
- `Assets/Scripts/WorldManager/GameTimeDebugOverlay.cs`
