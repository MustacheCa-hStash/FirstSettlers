# Shared streaming scheduling

Updated October 15, 2026.

First Settlers now uses one measured main-thread allowance for completed terrain publication, terrain runtime/content updates, foreground foliage work, and grass completion/upload/dispatch. The default and SmearScene allowance is **2 ms per frame**. Existing per-category time/count limits remain additional limits; they do not add capacity to the shared pool.

## Height sampling concurrency cap

`TerrainHeightSamplingScheduler.MaxConcurrentBatches` is fixed at **six simultaneous sampling tasks process-wide**, shared by detailed terrain height fields, far-terrain height grids, and far control-map generation (which also evaluates terrain heights). Concurrent requests share these six slots. Existing request limits remain separate; SmearScene still admits four terrain-data requests, one far request, six LOD requests, and two collider requests. The previous `Schedule(sampleCount, 64)` set batch size but did not limit sampling to a particular number of workers.

Each admitted wave contains at most six parallel iterations, each evaluating up to 64 consecutive samples. The producer completes that wave before admitting another. Producers take FIFO turns between waves, preserving progress for overlapping requests. Waiting happens on the calling request-producer threads, outside Unity job execution; Unity worker threads do not wait on a semaphore or monitor. Each producer schedules and completes its own job handles, respecting Unity's control-thread ownership. Completion precedes native-output access/disposal, including when setup of a later wave fails. Buffers allocated before admission use `Persistent` with explicit cleanup because waiting can span multiple frames.

This limits height-sampling concurrency, allowing other workers to service lighting and other jobs. It does not pin work to six physical threads, change Unity's global worker count, or cap slope/climate/mesh jobs. All sampling math, seeds, coordinates, and output layouts are preserved. Costs include more schedule/completion boundaries and potentially later terrain arrival under load. The six-task cap does not itself guarantee a particular frame time.

The producer-side profiler path is `FS.Streaming.Worker.HeightSampling.RunCapped`, with `WaitForSlot` separating admission delay, `ScheduleCapped` measuring submission, and `CompleteCapped` covering completion of each wave. On the Job worker lanes, compare overlap of `HeightFieldSampleJob (Burst)`, `FarHeightGridSampleJob (Burst)`, and `FarControlMapBuildJob (Burst)` together: at most six sampling iterations can execute at once. Compare the main camera's `Forward+ Complete` / `TileRangeExpansionJob` duration on the same route to measure whether reduced contention improves the hitch.

Validation: runtime/editor/player C# compilation passed. The isolated Unity fixture observed a peak of exactly six active sampling tasks across four simultaneous producers and 196 batches. It covered empty/partial batches, wave boundaries, failure recovery, and production near/far overlap. A pre-change reference of 66,720 bytes matched exactly for detailed heights/slopes/masks, far heights, and far control pixels. Existing shared streaming, ownership, and two full 128-sample request lifetime checks also passed without native lifetime/leak diagnostics. Run **Tools → Terrain → Validate Height Sampling Concurrency** for the focused fixture; suite log: `.utmp/streaming-budget/height-sampling-cap.log`. Walking-route improvement remains to be measured.

## Terrain texture submission cap

Frame 5160 published three terrain results and submitted nine control maps in about 0.88 ms on the main thread, while its render-thread texture uploads took about 19.89 ms. Main-thread timing alone does not limit the graphics work requested by those submissions.

`TerrainPublicationBudget.ControlMapSetsPerFrame` now permits **one complete control-map set per engine frame** by default, shared across normal and far terrain publication. Current generators produce three maps per set, so the default bounds this publisher to three terrain texture submissions per frame. SmearScene explicitly uses one. Configure **World Manager → Terrain Streaming Budgets → Max Control Map Sets Submitted Per Frame** (`maxControlMapSetsSubmittedPerFrame`); values are clamped to at least one and copied at world construction, so restart Play Mode to apply changes. Existing time and result-count limits continue to apply. Setting the shared time budget to zero does not disable this independent count cap.

Admission peeks at a queued result without transferring ownership. Current results that need another texture set stay in the original completion queue until a later frame; there is no second pending-publication queue, partially attached set, or detached native owner. Near results retain priority over far results. Empty-map results and obsolete/unwanted queue heads do not consume texture slots and can still be drained within their existing CPU/count limits. Collider and LOD consumption do not use the texture quota. Existing dispatch-backpressure thresholds bound further worker accumulation.

The slot is reserved before the first texture is created and includes failed attempts: destruction of partial assets cannot cancel graphics work already submitted. All existing texture/native failure cleanup and atomic record acceptance remain in place. The cap resets using `Time.frameCount`, not once per `Update` call, so repeated publication calls in one frame cannot replenish it. The synthetic fixture injects a frame index for deterministic validation.

`FS.Streaming.Publication.DeferControlMapsForSubmissionLimit` appears under the normal/far result pass when the next wanted set is deferred. Existing `CreateControlMaps` and `UploadOneControlMap` samples keep their names. In a new capture, the combined normal/far `CreateControlMaps` call count should be at most one per main-thread engine frame, with three `UploadOneControlMap` calls for the normal set. A failed set can emit fewer calls. No new Console logging or per-frame diagnostic allocations were added.

Benefit: fewer terrain resource creations are requested together, including when CPU submission is unusually cheap. Tradeoffs: terrain data and distant terrain can arrive later, completed payloads stay queued longer, and faster travel may expose that delay. A whole set still publishes together; lowering the default below three individual textures would require staged publication. This bounds submissions rather than GPU/driver execution time: one texture can still be slow, and an already queued render backlog can span main-thread frame boundaries. It is not a guaranteed render-thread frame-time limit or a throttle for unrelated asset loading.

Validation: runtime/editor/player C# compilation and isolated Unity scheduling/architecture/foliage/grass/Inspector suites passed. Added cases exercise limits of one and two sets, shared near/far admission, same-frame repeated updates, eventual completion, intact deferred native ownership, stale/empty-result handling after cap exhaustion, independent collider/LOD progress, shutdown disposal, partial texture failure cleanup, and next-frame recovery without refunding failed submission slots. Log: `.utmp/streaming-budget/texture-submission-cap.log`. Live render-thread improvement remains to be measured on the walking route.

## Reservations and priority

The allowance reserves 0.5 ms for terrain content, 0.35 ms for foreground foliage, and 0.15 ms for grass. Result publication initially has 1 ms available. These reservations are portions of the same 2 ms, not extra budgets. Ending a phase releases unused capacity to later phases. Oversized reservations are proportionally reduced to at most half the total, leaving room for result consumption with a very small total. Setting the total to zero disables shared limiting while retaining local limits.

Collider results are consumed before terrain data and visual LOD results. Data and LOD passes alternate first service after collision. Near chunk content requests/attaches collision before waiting for a visual LOD, including chunks outside the urgent ring. Coverage, request versions, worker inputs, generation seeds, and terrain handoff ownership retain their existing contracts.

Foreground foliage rotates first service between management, ground discovery, final publication, and tree representation. Grass rotates first service between completion, upload, and dispatch. Grass completion visits only active jobs, with a cursor that also rotates partially completed jobs, rather than scanning every resident grass tile. Ground discovery/publication can still advance one bounded step past a local limit when admitted, but cannot start a pass after the world allowance is exhausted. Deferred work stays queued or represented by unmet versions. An indivisible overrun can temporarily consume another phase's reservation.

## Backpressure and stale results

Terrain, far terrain, LOD, and collider dispatch pauses when that category's completed-result backlog reaches `max(2, 2 * worker limit)`. Existing in-flight workers still finish, so this is a dispatch threshold rather than a strict queue-length cap. Callers cancel the attempted request flag and retry through existing content queues. Ready results are not dropped to enforce this threshold. With one main-thread dispatcher and the existing worker limits, the backlog can exceed the threshold by the already running worker count.

Normal terrain, LOD, and collider publication check request versions before creating Unity assets. Obsolete/missing-record results still consume the per-frame dequeue count, bounding discarded-result scans. Far terrain retains its wanted/version preflight and waits for near result queues to drain.

## Measurement and limits

F3's performance panel reports measured streaming ms for the last update, the configured allowance, the peak since the world started, and overrun-frame count. Existing profiler markers and generation queue logs remain available. F7 resets the FPS sample; restart/regenerate the world to reset streaming peak/overruns. Inspector budget edits take effect after restart/regeneration, as configuration values are copied at world construction.

This is a **soft scheduling allowance**, not a hard full-frame limit:

- Mesh creation, texture upload, prefab creation, physics cooking, and individual publication slices cannot be interrupted midway.
- Coverage reconciliation and terrain handoff cleanup run to completion and are charged to the content phase; they can overrun it. Content attachment and visibility share their existing local budget.
- Drawing, grass range/version reconciliation and blend maintenance, viewer-change queue reconciliation, distant tree updates, ambient life/scatter updates, horizon work, gameplay, rendering/GPU work, and background worker execution are outside the measured pool. A large grass reconciliation scan or GPU upload can still produce a spike.
- Reservations provide admission headroom when earlier operations respect their allowance, not collision-readiness deadlines. Faster movement and cold starts may expose delayed terrain/foliage. Tiny allowances amplify this tradeoff.
- Lower publication throughput can increase latency. Dispatch backpressure limits further accumulation but does not make generation faster or evict retained world data.

## Validation and tuning

### Profiler marker map

Filter CPU Usage Hierarchy by `FS.Streaming.` or locate the same samples in Timeline. Deep Profiling is unnecessary for these explicit scopes. The four `FS.Streaming.Budgeted.*` scopes correspond to the measured allowance phases; add their inclusive durations for an approximate aggregate, rather than adding every nested sample and double-counting work. Bookkeeping/profiler overhead can cause a small difference from the stopwatch total. These samples are separate siblings/branches, not a single continuous interval containing rendering. An empty phase may still have a short sample.

| Marker | Source entry point | What it includes |
| --- | --- | --- |
| `FS.Streaming.Budgeted.Results` | `TerrainResultPublisher.Update` → `WorldStreamingBudget.Measure` | Completed-result consumption/publication |
| `FS.Streaming.Budgeted.Content` | `ChunkManager.UpdateActiveChunks` → `WorldStreamingBudget.Measure` | Coverage reconciliation, runtime creation, terrain/collider attachment, visibility and handoffs |
| `FS.Streaming.Budgeted.Foliage` | `FoliageManager.DrawVisibleFoliageEveryFrame` → `WorldStreamingBudget.Measure` | Pruning and rotating foreground queues; ends before drawing |
| `FS.Streaming.Budgeted.Grass` | `GrassStream.Update` → `WorldStreamingBudget.Measure` | Completion, upload and dispatch; excludes reconciliation, blends and drawing |
| `FS.Streaming.Publication.CreateTerrainMesh` | `TerrainResultPublisher.ApplyLodMeshes` / `ApplyFarTerrain` | One normal/far terrain mesh creation |
| `FS.Streaming.Publication.CreateWaterMesh` | Same publication methods | One water mesh creation |
| `FS.Streaming.Publication.CreateColliderMesh` | `TerrainResultPublisher.ApplyColliders` | Collider mesh creation; actual collider attachment/cooking is under `FS.Streaming.TryApplyCollider` |
| `FS.Streaming.Publication.CreateControlMaps` / `UploadOneControlMap` | `TerrainResultPublisher.CreateControlMapTextures` | Entire map set / one texture's creation and upload |
| `FS.Streaming.Grass.UploadOneTile` | `GrassStream.Publish` → `ResidentGrassRenderer.Upload` | One tile's renderer/GPU upload |
| `FS.Streaming.Worker.ControlMapBuild` | `TerrainControlMapBuilder.BuildRaw` | Padded material weights, blending and snow overlays on a terrain worker |
| `FS.Streaming.Worker.ControlMapScratch` | `TerrainControlMapBuilder.BuildRaw` → `TerrainControlMapScratchPool.Rent` | Scratch borrow/allocation and clearing; nested within the worker build sample |
| `FS.Streaming.Ownership.ReleaseInactiveChunkData` | `ChunkManager.TryReleaseInactiveChunkData` / `TryReleaseInactiveFarTileData` | Explicit coordinated retirement; automatic cache eviction remains deferred |
| `FS.Streaming.Ownership.ReleaseGeneratedAssets` | `TerrainAssetOwner.Dispose` | Record-owned generated asset teardown |

Existing markers such as `FS.Streaming.ApplyLODMeshResult`, `FS.Streaming.TryApplyCollider`, `FS.Streaming.Foliage.PublishSlice`, `FS.Streaming.Grass.Refresh`, `FS.Streaming.Grass.Draw`, and `FS.Streaming.Worker.*` retain their names. The active-job scan remains named `FS.Streaming.Grass.ScanAndCompleteJobs` for capture comparison. Source tracing is always available by searching the exact marker string (`rg -n 'FS.Streaming.Budgeted' Assets/Scripts`) and following the mapped caller; the table does not depend on the Profiler's ability to open a custom sample's call site automatically.

Marker instrumentation is included in profiler-enabled captures; use an Editor capture or a Development Build with profiling enabled. GPU effects can appear later on the render thread/GPU timeline even when the initiating upload has a CPU sample.

### Foliage publication spike diagnosis

The supplied frame 2712 had about 4.87 ms inside one `FS.Streaming.Foliage.PublishSlice`. Publication now exposes the kind and internal stage of that work without changing slice sizes, yield points or budgets. New names are static; no per-slice marker-name formatting or Console logging was added. Markers add profiling overhead, so compare equivalent captures with the same recording settings.

Expand this Main Thread path: `FS.Streaming.Budgeted.Foliage` → `FS.Streaming.Foliage.ProcessBatchQueue` → `FS.Streaming.Foliage.PublishSlice` → `FS.Streaming.Foliage.Publication.<Kind>`. Kinds are `Flower`, `LilyPad`, `Cattail`, `Clover`, `Dandelion`, `Bush` and `Rock`. Stage markers below use the prefix `FS.Streaming.Foliage.Publication.`:

| Stage suffix | What it measures |
| --- | --- |
| `ValidateInputs` | Cached source/revision/readiness and runtime-transform validation; also called by scheduler preflight |
| `PrepareBatches` | Small group counters, offsets and collection initialization |
| `CountGroups` | Classification of up to 128 instances per iterator step |
| `AllocateBatch` | One final matrix/variation batch and its registration in staging lists |
| `TransformInstances` | Sampling/transformation and writes for up to 128 instances |
| `CommitBatches` | Final flower/clover/dandelion/water-plant batch ownership swap |
| `Bush.CreateStaging` | Inactive staging root and object list initialization |
| `Bush.Instantiate` | One bush prefab instantiation |
| `Bush.SetupInstance` | Collider layers, transforms, runtime initialization and renderer configuration |
| `Bush.ClearPrevious` / `Bush.Activate` | Prior representation removal / final staging-root activation |
| `Bush.DiscardStaging` | Cancellation/failure destruction of unpublished bush staging |
| `Rock.CreateStaging` / `Rock.AddInstance` | Staging set creation / one rock's prefab traversal, placement and visual data |
| `Rock.FinishBatch` | One fallback matrix-batch allocation/copy; emitted only when using that builder |
| `Rock.ClearPrevious` | Prior rock representation removal and unregistering |
| `Rock.RegisterColliders` / `Rock.RegisterRenderer` | Final chunk-level registration with collision/render services; scopes can be empty if the service is absent |
| `Dispose` | Iterator disposal, including cancellation cleanup |

`CreateWork` and `Preflight` sit directly beneath `ProcessBatchQueue`, outside `PublishSlice`. They identify constructor or lookup/validation costs that would otherwise remain in that parent's self time. Every iterator stage sample closes before `yield return`; the kind sample covers just one `Advance` call. Add neither nested stage times nor kind durations to their parents when assessing the frame total.

For the next capture, restart Play Mode after import and repeat the route with Deep Profiling and allocation call stacks off. Select a publication spike, expand the path above to its expensive stage, and capture both the expanded hierarchy and matching Timeline interval. Keep worker lanes visible to assess possible contention. The kind/stage samples identify where elapsed time occurs; they alone do not prove whether thread preemption or an internal native wait contributed.

Runtime/editor/player C# compilation and the existing publication/shared-streaming suites passed with this instrumentation. A current-thread ProfilerRecorder observation confirmed completed ground-plant and rock stage samples during the existing fixtures; the bush markers registered successfully, but those fixtures do not exercise bush publication. Log: `.utmp/streaming-budget/foliage-publication-markers.log`. The next live capture is needed to identify the stage behind the reported spike.

### Foliage setup and grass drawing breakdown

Frame 2997 exposed 11.93 ms in foliage `CreateWork` and 10.03 ms in grass `Draw`, mostly self time. Both paths now include a breakdown of their setup, native calls, resource operations and drawing branches. Existing parent names remain available for comparison. This change adds diagnostics only; it does not change scheduling, batch sizes, selection, rendering settings or tree-placement ownership.

Under `FS.Streaming.Foliage.Publication.CreateWork`, expand `Setup.ReadRenderAssets` and `Setup.AllocateAndConstruct`. The latter contains `Setup.CaptureInputs`, `Setup.ReadTransform` (with `Setup.GetRootTransform` and `Setup.ReadTransformMatrix`), and exactly one of `Setup.CreateGroundIterator`, `Setup.CreateBushIterator` or `Setup.CreateRockIterator`. These names all have the prefix `FS.Streaming.Foliage.Publication.`. Object allocation and first-use type initialization occur outside the constructor's child scopes and remain in `AllocateAndConstruct` self time. Iterator creation does not execute the publication loops; their existing stages appear under `PublishSlice` when advanced.

Under `FS.Streaming.Grass.Draw`, expand `VisitEntry` → `Chunk`. The following suffixes all have the prefix `FS.Streaming.Grass.Draw.`; resource operations can also appear under tile upload, retirement, or validation callers.

| Stage suffix | Source / meaning |
| --- | --- |
| `InitializeCounters` | One-time native profiler counter registration if resource events haven't initialized them already; later calls are empty |
| `VisitEntry` / `CheckVisibility` | `GrassStream.Update`: entry traversal and visibility admission |
| `Chunk` / `ReadSettings` | `ResidentGrassRenderer.Draw`: one bounded renderer's full draw path / settings and asset checks |
| `PrepareProperties` | MaterialPropertyBlock clear and common color/fade/shadow properties |
| `CheckGpuSupport` | GPU versus CPU fallback routing; children `Support.CheckMaterial`, `Support.ReadInstancing`, `Support.ReadMaterialTag`, `Support.CheckHardware`, `Support.CheckKernel` measure native material/capability queries in `GrassRenderUtility.IsSupported` |
| `EnsureBuffers` | Existing arena check; actual creation and replay have separate children |
| `Buffers.CreateSources` / `CreateSlots` / `CreateVisibleIndices` / `CreateDrawArguments` / `CreateUnusedIndices` | Individual native ComputeBuffer creations |
| `Buffers.ReuploadResidentSlots` / `UploadMetadata` / `UploadDrawArguments` | Existing CPU slot data replay, changed slot metadata upload, or indirect argument upload |
| `Buffers.Release` | Native arena teardown, including fallback switching, slot growth and disposal |
| `Cull` / `Cull.SelectChannels` | Complete compute-culling preparation / biome and LOD channel selection |
| `Cull.ReadMeshBounds` / `Cull.ReadMeshArguments` | Native mesh bounds and index/base-vertex queries |
| `ReadWindProperties` / `Wind.ReadStrength` / `Wind.ReadFlutter` | Native material wind-property reads in `GrassRenderUtility.WindPadding` |
| `Cull.ResetCounters` / `FindKernel` / `BindValues` / `BindBuffers` / `Dispatch` / `CopyCounts` | Append-counter reset, kernel lookup, compute property and buffer binding, dispatch submission, and indirect count copies |
| `SelectDrawChannels` | Biome/LOD draw traversal; includes the nested draw paths below |
| `Indirect` / `Indirect.PrepareProperties` / `Indirect.Submit` | GPU draw path / per-channel properties and bounds / native indirect draw submission |
| `Fallback.FilterAndBatch` / `Fallback.PrepareProperties` / `Fallback.Submit` | CPU instance filtering and batching / batch variation upload / native instanced draw submission; filtering is the parent self time after subtracting its nested property/submission samples |

Enable **Profiler Modules → First Settlers Grass** before recording. Its default details panel shows nine counters for the selected frame: `FS Grass GPU Chunks`, `Fallback Chunks`, `Buffers Created`, `Buffers Released`, `Buffer Uploads`, `Cull Dispatches`, `Indirect Draw Calls`, `Fallback Draw Calls`, and `Fallback Candidate Visits` (all names begin with `FS Grass `). Counts are main-thread events summed across renderers/streams in that frame, then automatically reset at frame end. GPU/fallback chunks mean renderer draw visits with resident bounds, not unique world coordinates; candidate visits count each CPU filtering pass and can exceed the number of distinct instances. Buffer/upload counters include renderer resource work outside the draw scope, so inspect the CPU hierarchy to locate their caller. They are events, not memory usage or GPU execution times.

The native counter helper is isolated in `Assets/Scripts/Profiling/FirstSettlers.Profiling.asmdef`; only that small diagnostics assembly allows unsafe code to access Unity-owned counter cells. The project-wide unsafe setting and package dependencies are unchanged. Counter update calls compile out when `ENABLE_PROFILER` is absent. Static marker names, chunk/channel/operation-level scopes, and per-slot fallback visit totals avoid Console logging, name formatting, readbacks, or profiling each grass instance. More scopes still add recording overhead; compare captures with the same settings.

For a new capture, restart Play Mode after compilation, enable the grass module, and repeat the same route with **Deep Profile off** and **allocation call stacks off**. On a spike, capture the two expanded CPU branches and the grass module's selected-frame counters. Include the same interval in Timeline with Main Thread, Render Thread, Job and Scripting Threads visible. Save the complete capture with the Profiler save button if possible; it preserves the other frames and threads without further screenshots. A long native-operation sample localizes the call, but does not on its own distinguish CPU execution, internal waiting or OS descheduling.

Validation: runtime/editor/player compilation and isolated Unity assembly import passed. A current-thread observation recorded setup/constructor stages and native grass drawing stages, including both indirect and forced fallback submission, buffer creation/release/replay, metadata and argument uploads. All nine native counter values advanced, and the custom module constructor accepted the descriptors. Existing publication, resident grass selection/bounds parity, and shared scheduling/architecture/Inspector checks passed. Bush iterator setup registered but was not exercised by the existing fixtures. Log: `.utmp/streaming-budget/draw-diagnostics.log`. This verifies instrumentation and behavior preservation; no live-route performance improvement is claimed.

Run **Tools > Terrain > Validate Shared Streaming Scheduling (synthetic)**. The suite covers deterministic accounting/reservation refunds, exception and overrun recovery, collider publication ahead of LOD, stale-result preflight, backlog rejection before acquiring inputs, Inspector transport, existing architecture/foliage/grass/Inspector regressions, shared-budget foliage deferral/resumption, and grass cache/invalidation/empty-result convergence under a 0.1 ms shared allowance.

The isolated test project is under `.utmp/foliage-optimization/unity-project`; logs are under `.utmp/streaming-budget`. Compilation and synthetic checks establish scheduling correctness, not live-world FPS improvement.

Runtime, editor, and player C# compilation passed. The final isolated Unity suite passed all listed checks; log: `.utmp/streaming-budget/validation-complete.log`. Existing obsolete-API warnings remain. No live SmearScene route or FPS benchmark was run.

For tuning, use the same built-player exploration route and compare 1, 2, and 3 ms allowances plus zero (shared limiting disabled). Record frame-time distribution/1% lows, player-proximate collider readiness, terrain/foliage arrival latency, queue counts, and memory during a cold start, steady travel, sharp reversals, and revisiting cached regions. The 2 ms setting is a starting point rather than a measured optimum.
