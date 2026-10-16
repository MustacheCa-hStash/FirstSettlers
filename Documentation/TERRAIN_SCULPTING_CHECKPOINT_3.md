# Terrain sculpting checkpoint 3: transactions and streaming

October 15, 2026. P3's transaction, streaming and ownership integration is implemented. The [approved plan](TERRAIN_SCULPTING_PLAN.md) remains the scope reference. Sculpting is **opt-in and disabled in the saved scene**. The live walking/frame-time baseline and the edit-latency target remain gates before enabling it for normal play.

## What happens during an excavation

1. `TerrainSculptRuntime.TryExcavate` admits one command. Further commands return busy; there is no stroke queue. Sampling/staging and mesh construction run on a producer using an existing `TerrainRequestManager` mesh slot.
2. The producer stages density without publishing it, captures a stable snapshot, expands the affected XZ columns by an unchanged guard, and merges intersecting footprints. A merged rectangle reconstructs both the exterior skin and any interior excavation. This preserves roofs and avoids placing a collar across another cut.
3. The builder selects render/collision coverage, generates the required patches and filters their original baseline coverage. Output is split at source-chunk boundaries with canonical clipping. A patch wholly inside one chunk uses a direct copy. Unchanged chunk surfaces are reused when their content, collision coverage and visual-field state still match.
4. The main thread prepares detached Unity meshes, then prepares collision under the existing collider admission. It checks that all source runtimes and any complete normal/far handoff are ready. Old terrain remains attached while any part is missing.
5. One publication call attaches the complete new coverage and commits authoritative density. Attachment failure restores the old surfaces before yielding a frame. Stale results fail their identity checks before publication. Consumer callbacks run only after the committed geometry and density agree.
6. Replaced assets and CPU arrays are released. Shutdown cancels and joins the producer before retiring density/source resources. No late result can attach to a regenerated world.

The request identity includes the world epoch, request, authoritative revision and coverage revision. A world fixes its generator/profile for its lifetime; regeneration creates another epoch and drains the old service. File fingerprints belong to P4.

## Coverage and collision

| Representation | Implemented policy |
| --- | --- |
| Visible edits | Full weight through 192 m horizontally, returning to baseline over 192–256 m. |
| Vertical visual window | Full weight through 64 m separation, returning to baseline over 64–96 m. |
| Preparation | Relevant sparse groups/dependencies through 288 m horizontally / 112 m vertically. Groups outside the visible window warm canonical height dependencies without creating a zero-weight excavation mesh. Overrides are already resident in P3. |
| Retention | Eligible edited representations/group state retire outside 320 m horizontally or 128 m vertically. Pins and authoritative data are independent. |
| Collision | Authoritative, unfaded density. Prepare through 48 m, require within 32 m, retain through 64 m in 3D. Selection uses conservative merged edited bounds. |
| Occupied/support-critical terrain | The occupied region is pinned. `PinCollision` provides disposable bounds pins for future building/support consumers. P6 still has to subscribe those consumers. |

Coverage anchors sit on the 4.8 m lattice and use a deadband. All surfaces in a batch use one anchor/revision. The distance field changes topology, so this is an actual reconstruction toward baseline, not material transparency. A completed worker result still counts as busy until its publication/disposal is handled.

`CharacterMotor.TerrainGuard` checks the swept actor bounds before movement. It denies entry near edited terrain without current collision or full local visual coverage. Pending edits leave the old coherent terrain usable. `RequestTeleport` prepares destination coverage and its ordinary 3×3 source neighbourhood before moving the viewer in the final swap; gameplay must use that API rather than writing the transform directly.

If incoming coverage overlaps an attached far tile, the host prepares the tile's **complete source footprint** before retiring it. Ordinary LOD/visibility refreshes cannot reveal original collision over a patch. Claimed render coverage keeps the corresponding far footprint represented by normal chunks until release. Far mesh, control-map and height-generation outputs themselves remain unchanged.

A handoff that needs more than **64 source chunks** is rejected with a status message while keeping the previous terrain. This bounds exceptional relocation/large-footprint preparation; it does not make every excavation a 64-chunk operation. The existing normal terrain workers, publication quotas and ownership still apply to these source chunks.

## Memory and compute

- The density/snapshot/halo admission remains 64 MiB. Unsaved authoritative overrides are retained; P3 does not evict them or pretend that disk persistence exists.
- Derived build/staging admission is 128 MiB by default, accounting for retained surfaces plus an in-flight batch. Growable arrays/index dictionaries, output copies and persistent per-batch height caches are charged before growth. Builder scratch is released from live ownership accounting on return; final arrays remain charged. This is conservative **estimated live ownership**, not a promise about process memory, garbage awaiting collection, driver allocations or PhysX storage.
- P3 dispatches at most one sculpt batch at a time, within the approved maximum of two. It shares ordinary mesh admission, keeps at most one completed/staged batch, and uses the existing collider admission for one main-thread cooking operation at a time.
- The sculpt phase reserves 0.5 ms **inside** the shared 2 ms allowance. Later work stops when the allowance is spent. Unity mesh creation, collision preparation and the final swap are indivisible and can overrun; the shared budget reports those overruns. Very small publication allowances still permit bounded eventual progress.
- The baseline adapter shares immutable canonical maps already produced by streaming through a thread-safe lookup, with bounded sampler retention and native generation on a miss. Workers do not read the mutable chunk-record dictionary. Bulk 19³ halo reads share one baseline height lookup per vertical column; scalar-field generators that are not heightfields retain the general sampling path. Bulk and individual samples are checked for equality.
- Uniform stone/air skipping from P2 remains active. No dense underground cylinder is allocated. Rectangular groups can still become expensive when many nearby edits merge; cell/output/admission limits reject an oversized replacement rather than publishing a partial one.

F3 now reports sculpt status, published revisions, active jobs, chunk count, density/derived reservations, stale discards, render/collider triangles and the last edit/worker/mesh/cooking/commit durations. Profiler markers include `FS.Streaming.Sculpt.Edit`, `.Mesh`, `.Coverage`, `.Stage`, `.Cook` and `.Commit`, under `FS.Streaming.Budgeted.Sculpt` for measured main-thread work.

## Validation

Run the complete sculpt suite from the repository root:

```powershell
./Tools/ValidateTerrainSculpting.ps1 -MeshChecks -IntegrationChecks
```

The script compiles runtime/editor/player targets and runs the isolated Unity project. It uses a simulated frame index for the synchronous `ChunkManager` fixture so the real per-engine-frame texture quota can advance; production continues to use Unity's frame index.

Verified cases include:

- P1 density/baseline and P2 mesh/collider regressions, including exact native heights, roof retention and existing motor routes.
- Delayed worker and collision admission, rejected second commands, delayed source readiness, density remaining uncommitted until complete geometry is ready, an actor standing through the swap, and four guarded entrance/tunnel motor routes.
- A changing coverage anchor during a pending edit, retry without losing/duplicating that command, failure cleanup, and cancellation/join during shutdown.
- Horizontal/vertical threshold sweeps, dependency prefetch, return reconstruction, collision pins/release, unchanged-surface reuse and progress with a 0.001 ms local publication allowance.
- Published Unity render/collision topology and three further actual motor routes at source-chunk corners 0, +128 and -128, including negative coordinates. Ordinary handoff cannot retire owned coverage and can retire it after release.
- Nine separated excavations within the enlarged near radius, followed by a coverage sweep/return under the default memory cap.
- The actual `ChunkManager`, native SmearScene generator/profile and shared streaming loop publishing a dry scoop. Original cached LOD assets remain intact; raycasts hit the excavated floor rather than old baseline collision.
- An outgoing far tile held through relocation preparation, then retired in the publication frame without changing its mesh data.
- Derived admission failure preserving the old field/coverage, destination readiness, and resource/reservation cleanup.

Evidence paths under ignored `.utmp/terrain-sculpting/`:

- `p3-command.log` and `validation.log`: compilation and P1/P2/P3 outcome, topology/physics and native timing observations.
- `p3-streaming-regression.log`: existing shared-streaming, architecture, collider priority, texture quota, foliage, grass and Inspector regressions.
- `p3-ownership-regression.log`: existing ownership, native reader lifetime, publication failure/shutdown and related regressions.

### Performance gate

The functional fixtures pass, but this checkpoint does **not** establish the plan's ≤150 ms warm p95 or ≤400 ms maximum default-brush targets. Native first/subsequent edit observations and stage breakdowns are recorded in the Unity log; several runs have exceeded one or both targets. Worker extraction is the dominant cost in the native fixture. Synthetic flat terrain is substantially cheaper. The small native fixture omits normal scene content and rendering, so neither result is live-world FPS or a complete gameplay benchmark.

Latest complete run (observations, not guarantees):

| Observation | Result |
| --- | --- |
| Runtime/editor/player compilation; P1/P2 | Passed; 8,565 P1 checks, 108 exact native height comparisons and 613,404 P2 checks, including 16 P2 motor traversals. |
| P3 | Passed, about 1.93 million vertex/edge/state checks, seven additional motor routes, a standing actor during publication, nine-edit range sweeps, and native/far-handoff fixtures. Async polling makes the precise assertion count variable. |
| Native first default scoop | 251.329 ms. |
| Five subsequent native default scoops | 219.921–376.892 ms; too few samples to claim a representative p95, and all exceed the provisional 150 ms threshold. |
| Last native worker / mesh creation / cooking / commit | 331.295 / 1.685 / 0.046 / 2.481 ms. Cooking may share work with Unity activation; the full swap is measured separately. |
| Last native retained render / collision geometry | 52,720 / 14,632 triangles, including the affected chunk's surviving baseline. |
| Nine separated edits | 73,728 authoritative payload bytes; 18,299,040 bytes retained derived estimate after the return sweep. |
| Nine-edit mixed cold/warm observation | Nine samples, p95 equals the maximum at 171.396 ms; maximum measured fixture publication slice 39.560 ms. This synthetic host also constructs original source terrain lazily inside its lookup, so that slice is not an isolated engine upload/cooking measurement. |
| Existing shared-streaming/ownership/native lifetime | Passed, including collider priority, texture quotas, foliage, grass, Inspector, retirement and retained native outputs. |

The source chunks prepared for a handoff use the existing terrain cache and its existing retention policy. Their maps/materials are separate from the new density/derived admission figures; the 64-chunk preparation limit is per exceptional handoff, not a total world-memory limit.

Keep the feature disabled in SmearScene until the clean walking baseline and a representative edited route have been captured, and optimize/retest the native extraction workload before claiming the latency gate. P4's persistence work does not make this performance gate disappear. P7 remains responsible for the final operating/performance report.

## Integration/API and remaining phases

`WorldManager` exposes **Enable Terrain Sculpting**, copied into `WorldConfiguration.Sculpting`, and exposes the running `Sculpting` service. Restart Play Mode after changing the toggle. The initial adapter requires the approved 0.3 m source grid and source chunks divisible by 16. The saved scene toggle is untouched/off.

The main-thread service API is `TryExcavate`, `Busy`/`Status`, `PinCollision`, `RequestTeleport`/`CancelTeleport`, and `TerrainEditCommitted`. These are integration hooks, not finished gameplay controls. `Density` exposes the authoritative field for scoped future persistence/queries; callers must not bypass transactions to commit gameplay edits. No F6 interaction, save sidecar, stone surface material, grass suppression or building support subscription is delivered here. Those remain P4, P5 and P6. There are still no generated caves, ore or AI-navigation changes.
