# World composition and service ownership

Updated October 6, 2026.

## Configuration

`ChunkManager` takes one `WorldConfiguration`. `WorldManager.BuildConfiguration` maps its existing serialized fields to named groups; saved scenes, prefabs, datacards and inspector property paths are preserved.

| Group | Inputs |
| --- | --- |
| Generation | Seed, chunk dimensions, scale, climate, erosion, mountain/snow settings and water |
| Coverage | Near/collider ranges, far patch hierarchy and far sampling settings |
| Workers | Terrain, far terrain, LOD and collider concurrency limits |
| Publication | Per-category completion counts/time budgets and total apply budget |
| Content | Visible-content, visibility-check and far-content budgets |
| Rendering | Terrain/water materials, shadow flags and horizon settings |
| Scene | Viewer, viewer camera and chunk parent |
| Foliage | Authored grass, ground plants, trees, ambient life and forest scatter settings |

Value groups are copied at construction. Changing the configuration container afterward does not modify a running world. Foliage settings and Unity object references retain their existing live-reference behavior; generation and asset edits still require regeneration/restart where their inspector tooltips specify it. `FoliageManager` accepts the generation/foliage groups plus the world tree registry.

## Species bindings

`TreeSpeciesCatalog` is the single table for all twelve current tree variants. Each entry binds its display name, habitat, near prefab, billboard prefab and shared species datacard. Constant-time indexed lookup avoids per-frame catalog construction or enumeration for gameplay queries.

`TreeSettings` preserves the existing serialized authoring fields and delegates resolution to the catalog. Standing/distant rendering enumerate the catalog; gameplay resolves through the same bindings; query labels use its display names. Species-specific prefabs override habitat fallbacks. Grassland uses its own fallback when assigned, then the global fallback. Datacards use the authored species card first, then the resolved near prefab's authoring card.

`TreeGenerationSnapshot` translates these bindings into plain scale/radius arrays on the main thread. Workers receive values, not Unity assets. Array capacity follows the catalog's variant IDs. Ecological species selection, placement seeds and habitat-specific rules remain explicit in generation policies.

When adding a species, add the authoring fields and catalog entry, then its ecological selection rules. Preserve existing enum numeric values and tree identity rules.

## Responsibilities

| Owner | Responsibility |
| --- | --- |
| ChunkManager | Desired coverage membership, content/visibility scheduling and world update orchestration |
| TerrainCoveragePolicy | World-aligned patch selection, ring boundaries and overlap geometry |
| TerrainHandoffCoordinator | Outgoing-runtime readiness, terrain/water handoffs and hidden replacements |
| TerrainResultPublisher | Completed request consumption, mesh/texture construction, acceptance and content wakeups |
| TerrainRuntimePool | Inactive normal/far runtime reuse and the hidden pool root |
| FoliageManager | Species ranges, per-chunk readiness, placement registration scheduling and drawing coordination |
| FoliageRenderAssets | Mesh/material resolution, capabilities and runtime binding |
| FoliagePlacementService | Plan-to-instance conversion and detailed tree registration |
| FoliageDiscoveryScheduler | One active ground discovery job and its bounded FIFO/deduplication state |
| FoliagePublicationScheduler | One active final publication and its bounded FIFO/deduplication/cancellation state |
| FoliagePublication | Sliced final batches or object staging for one request |

Foliage queues depend on `IChunkLookup`, exposing only record/runtime lookup. The extracted classes own their state and dispose their active work; these are composition services, not partial-file splits of the original coordinators. Existing profiler marker names and publication ordering are retained.

Outgoing normal chunks without an applicable far replacement now release their runtime instead of being dropped from the pending cleanup list while remaining loaded. Readiness-gated replacements and reversal reuse remain intact.

## Validation

Run **Tools > Terrain > Validate World Architecture (synthetic)**. It checks species overrides/fallbacks, worker snapshot independence, inspector-to-configuration transport, count/time budgets, copied group values, negative-coordinate patch ownership, normal/far handoffs and reversals, stale LOD results, far-publication priority, runtime pooling and the foliage optimization regressions.

Runtime/editor/player C# compilation and the isolated synthetic Unity suite passed. Log: `.utmp/foliage-optimization/architecture-validation-final.log`. No saved scene, camera audit, Play mode or live performance benchmark was used. Existing obsolete-API warnings remain.

World-data eviction, complete generated-resource disposal and worker failure/cancellation improvements remain separate work from this composition refactor.
