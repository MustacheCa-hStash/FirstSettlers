# Terrain sculpting checkpoint 1: authoritative density foundation

October 15, 2026. Scope: P1 and the P0 fixed-profile data fixture from the [implementation plan](TERRAIN_SCULPTING_PLAN.md). The main scene is not connected to these new services yet.

## Delivered behaviour

- Immutable 0.3 m / 16-cells-per-brick profile with signed 16-bit values and a 1.2 m band.
- Canonical sample ownership across positive and negative brick/source-chunk coordinates.
- Continuous sphere and box excavation. Outside samples in the boundary band change while remaining stone; repeated or overlapping cuts cannot refill existing air.
- Staged copy-on-write edits. Pending values can be read into a mesh input without becoming visible to committed solid queries. Cancelling a staged edit leaves the field unchanged. Commit swaps the brick dictionary root rather than publishing a sequence of individual brick changes.
- Implicit baseline and immutable uniform/detailed overrides. Fully excavated uniform-air bricks remain explicit overrides and survive snapshot reconstruction.
- Disposable stable snapshots and 19^3 mesh inputs with shared corners and gradient halos. Readers keep the captured revision while later edits commit.
- Conservative payload admission, including retained snapshots, staged arrays and halo buffers, with a default 64 MiB limit. Reported payload bytes exclude dictionary/object headers and caller-owned exported copies. Persistence must budget its own exported copies in P4.
- Configurable minimum removal Y for the approved dry-excavation restriction. Gameplay radius/reach/input limits arrive with the controller, not this storage layer.

## Baseline source and an integration decision

`TerrainBaselineSampler` reads the same native/Burst-generated full-resolution height maps as the current terrain. It reproduces the full-resolution triangle diagonal when interpolating between lattice positions and preserves the production float height-multiplier/world-scale order.

This is intentional: the existing managed point evaluator has previously documented small rounding differences from native generation. It must not be substituted at seams. The sampler has a bounded height-map cache and an injectable provider for canonical existing chunk maps; cold production generation still participates in `TerrainHeightSamplingScheduler`'s shared cap. Cold calls belong on producer threads, not player/render callbacks.

The fixture reads SmearScene's actual seed, scale, water settings and complete serialized erosion profile directly from its file without opening or modifying the scene. It compares 108 native height samples across positive/negative chunk boundaries, checks sign/world conversion, and measures a warmed cache-read loop. The isolated timing includes reference generation and first-use work; it is not a live walking or edit-latency benchmark.

## Validation and reproduction

Run from the repository root:

```powershell
./Tools/ValidateTerrainSculpting.ps1
```

Optional `-CompileOnly` performs runtime/editor/player C# compilation; `-PrepareOnly` additionally prepares the isolated Unity project without launching it. The script uses the project's installed Unity version and cached dependencies rather than opening a second editor on the main project.

Evidence paths:

- `.utmp/terrain-sculpting/command.log`: complete-target compilation and harness outcome.
- `.utmp/terrain-sculpting/validation.log`: isolated Unity foundation tests and shared height-sampling regression.
- `.utmp/terrain-sculpting/baseline-observation.txt`: actual scene-profile baseline comparison and CPU observation.

The foundation fixtures cover one-metre sphere/box cuts at sub-grid offsets, recovered box-face interpolation, exact zeros, eight-owner corner cuts, negative coordinates, repeated/overlapping cuts, staged mesh inputs, cancellation, independent snapshot branches, reconstructed sample equality, preserved uniform air, shared halos, dry-bound rejection, failed generation recovery, and budget admission. A pinned snapshot is included in admission accounting; the tests verify it prevents an over-budget edit.

Final validation passed:

| Check | Result |
| --- | --- |
| Complete runtime/editor/player C# compilation | Passed on all three reference sets; existing deprecation warnings only. |
| Density foundation | 8,347 assertions passed. |
| Production baseline and complete P1 suite | 8,565 assertions total passed; all 108 sampled native heights matched exactly. |
| Existing height-sampling regression | Passed, including partial scheduling failure recovery and production near/far overlap. Four producers submitted 196 batches; observed concurrency peaked at six, preserving the shared limit. |
| Latest CPU observation | Multi-chunk reference/cache fixture 656.367 ms; 10,000 warm cached height reads 1.586 ms; ten sampler-generated chunks with eight retained. First-use/cache state affects this observation. It is not live-world FPS or brush latency. |
| Repository whitespace check | `git diff --check` passed. |

The first sandboxed Unity launch could not reach the local licensing service. The final validation ran outside that sandbox against the isolated project and exited successfully. The validation harness also accounts for cached player reference files that predate the separate profiling assembly; it compiles that dependency against player references. No engine/runtime implementation failure was hidden by these harness corrections.

## Deliberately not delivered at this checkpoint

- No F6 sculpt tool or change to SmearScene's render/physics behaviour yet.
- No terrain mesher, baseline masking, collision replacement, seam collars, distance-transition implementation, material changes, or grass/building invalidation yet.
- Snapshots are **in memory**. Versioned files, compression, atomic on-disk manifests and restart loading belong to P4. Snapshot reconstruction is not a claim that a world save already exists.
- The live unedited walking/frame-time baseline is still required before scene activation. P0's data fixture is ready; its live route capture is deferred to the integration checkpoint rather than being claimed complete from a CPU test.

## Next checkpoint

P2 adds the deterministic mesher and isolated render/physics fixtures, then proves the baseline replacement/collar strategy. Required examples include a retained roof over a buried cavity, tunnel breakthrough, matching brick/source-chunk edges, and actual player-motor traversal without the old heightfield collider blocking an entrance. Native sample equality in P1 does not by itself prove a clamped field's reconstructed mesh or collider seams are correct; those are P2 gates.
