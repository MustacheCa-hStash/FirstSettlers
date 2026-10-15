# Terrain ownership cleanup

Updated October 14, 2026. Ownership cleanup is implemented; automatic cache eviction and tree placement unloading remain deferred.

## Generated asset ownership

`ChunkRecord` owns accepted normal/far control textures, terrain/water LOD meshes, collider meshes, and its per-chunk far terrain/water meshes. `FarTerrainTileRecord` owns accepted macro-tile meshes and textures. `TerrainAssetOwner` tracks references per record slot, so an asset shared by multiple slots survives until its last slot is released and is destroyed only once. Paired mesh swaps retain incoming references before releasing outgoing references. Authored materials, meshes and prefabs are not transferred through these generated-asset APIs.

Clearing a collider or LOD mesh slot now releases that ownership. Replacing accepted assets releases obsolete resources. Runtime materials/GameObjects remain owned by the runtime pool; runtimes detach their mesh/texture/collider bindings before record retirement or world shutdown. A displayed LOD replacement is rebound after publication transfers ownership, so a wakeup/rebinding exception cannot destroy already accepted assets. Runtime `Object.Destroy` is deferred by Unity; isolated Edit-mode validation uses immediate destruction of generated objects.

Record disposal is idempotent. It invalidates all requests, disposes native ownership, releases generated assets, and clears managed maps, feature plans, foliage/ambient data, cached biome ownership aliases and readiness state. Native terrain readers retain their existing leases: owner disposal rejects new leases and waits for the final existing reader to release its storage. At final release, native fields are reset and managed source aliases are cleared. Copy-constructor failure also releases partial native allocations; transferred job outputs remain caller-owned until construction succeeds.

## Explicit retirement

`ChunkManager.TryReleaseInactiveChunkData(coord)` and `TryReleaseInactiveFarTileData(patch)` are explicit preparation APIs. No update loop invokes them automatically and no cache-size setting was added.

Normal retirement refuses chunks in desired coverage, chunks with attached runtimes (including outgoing handoffs), and chunks still read by active foliage discovery or grass jobs. Once eligible, it invalidates requests, removes pending foliage/grass work and resident leaf/fern/ambient references, discards queued results, disposes the record and removes coordinator queue entries. Far retirement protects desired/attached macro tiles and removes their generated resources/results. Retiring a chunk does not forcibly complete a running grass/discovery job; callers retry after normal completion/retirement makes it eligible.

Tree registry placement snapshots, tree records and changed states are deliberately retained. No per-chunk tree unload operation was added. Thus this is not a bound on all explored-world memory. Whole-world shutdown retains its existing tree registry clear behavior.

Call retirement only on the main thread. Future automatic eviction should invoke bounded retirement steps within the shared streaming allowance. These explicit calls are not time-sliced: native disposal and Unity asset destruction can take variable time. They provide ownership safety, not a hard frame-time bound. Cached regions outside explicit retirement still retain their data.

## Results, failures and shutdown

All record request categories use session-wide tokens rather than restarting at version one when a coordinate is recreated. Late data/mesh/collider/far results fail preflight against the replacement record. Dropped terrain results dispose their retained native outputs. Already running workers may finish; LOD workers retain native leases and collider workers retain their managed inputs until then.

Worker exceptions produce lightweight failure notifications. Publication handles up to eight per update, respecting the shared allowance when present, cancels only the matching current request and wakes its content queue for a retry. Stale failures cannot cancel newer requests. Dispatch failure/throw rolls back active counters and LOD leases.

`TerrainRequestManager.Dispose` closes admission, drops all four result queues plus failure notifications, disposes queued terrain native data and shuts down scratch pooling. Every worker category rejects late completion after closure. World shutdown closes the request manager before releasing consumers/runtimes, then disposes both normal and macro records and clears coordinator references. The existing bounded worker wait remains; leases and late-result rejection protect workers that outlast it.

Publication holds generated meshes/textures locally until acceptance. Rejection or failure destroys staging resources; partial texture-set construction and exceptions inside mesh construction release their allocated prefixes. Failed publication clears the matching request flag. After acceptance, the record owns assets even if a later wakeup throws.

## Validation and profiling

Run **Tools > Terrain > Validate Terrain Ownership Cleanup** with Play Mode stopped. The fixture checks repeated asset replacement/disposal, duplicate slot ownership and paired swaps, native-reader lifetime and alias release, coverage/reader retirement guards, unchanged registered tree placement/cut state, late data rejection after record recreation, partial normal/far publication failure, post-transfer wakeup failure, worker failure notifications, all-category queue teardown and shutdown with real terrain/far/LOD/collider workers active.

The batch entry point additionally runs the control-map scratch, streaming/architecture/foliage/grass/Inspector and real terrain-worker lifetime suites. Logs are under `.utmp/streaming-budget`. These checks validate ownership and lifecycle behavior; live traversal, GC pauses and memory plateaus still need measurement after a cache policy is enabled.

Runtime/editor/player compilation and the final isolated batch passed in Unity 6000.4.8f1. Log: `.utmp/streaming-budget/ownership-validation-final.log`. The worker integration checks emitted no request-failure or TempJob lifetime diagnostics; existing obsolete-API compiler warnings remain.

Profiler markers: `FS.Streaming.Ownership.ReleaseInactiveChunkData` covers coordinated explicit retirement, and `FS.Streaming.Ownership.ReleaseGeneratedAssets` covers a record's generated-asset teardown. Search the exact marker string to find its source.
