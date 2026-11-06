# Resident distant-tree submission — 6 November 2026

Compatible distant trees now stay resident without individual CPU submissions each frame. The manager submits one small region record per logical chunk and shared billboard mesh/material. Compute determines each tree's mesh/impostor handoff from viewer distance, blends its terrain seating, and retains the existing distance fade, density thinning, frustum culling and depth ordering.

Nearby meshes still use the standing renderer. Unsupported billboard materials and devices retain the CPU path, including mixed CPU/GPU species within a chunk. Tree population, generation, assets, distances and scene settings are unchanged.

## Data lifetime and work

- Placement and initial appearance upload on registration. Grounding and ecology upload only when changed, using contiguous dirty runs rather than a whole-tree state buffer.
- Region records carry load fade, visibility and an integer frame stamp. An unsubmitted region cannot reuse last frame's visibility. No per-tree state clearing is required in `BeginFrame`.
- Grounding still samples/blends on the CPU when terrain support changes. A revision triggers uploads while heights change; settled terrain requires no tree upload, including when the camera moves.
- Manifests cache separate nearby-mesh and CPU-fallback indices. Compatible far regions skip the per-tree CPU loop completely. Registration and invalidation can still require tree loops.
- A dense list of live source slots drives compute. Evicted/replaced trees leave that list immediately; source-slot holes do not preserve the previous dispatch size. Appending/removing slots updates only the affected index range. Cached, non-evicted sources remain resident and are rejected by region frame stamps when inactive.
- Slot generations keep density history valid across sparse edits and buffer growth, while resetting it for reused slots. Buffers retain peak allocated capacity, but dispatch follows resident count. This is not a registry eviction or asset-residency change.

The GPU source stride grows from 112 to 144 bytes. Removing the old 16-byte state buffer and adding the index list and density generation produces approximately 24 extra GPU bytes per allocated tree, plus 32 bytes per region. This exchanges modest resident memory for fewer CPU operations and uploads. `VisibleInstance` remains 96 bytes and the visible tree shaders are unchanged.

`DistantTreeManager` exposes `CpuTreeSubmissions`, `GpuSourceUploads`, `GpuRegionUploads` and `GpuCandidates` for diagnostics. Candidate/submitted counts are conservative inputs to GPU culling, not GPU-visible counts. Normal rendering never reads GPU results back.

## Matched rendering experiment

Unity 6000.4.8f1, Windows, D3D11, RTX 5090, URP PC asset, 1280 × 720, fog disabled. The original batch and compute shader are taken from commit `29f7c3a7715dfcc8b5280de171f6122ed0f28747`. Both modes render the same deterministic tree transforms, mesh, material, camera, depth bands and full-density settings. The synthetic annulus contains trees at 250–570 m and 32 trees per region. The reference performs the previous per-tree distance/handoff and submission loop; the resident mode submits regions.

Each stage has 16 warm-up and 16 measured renders. A second pass reverses mode order. The following values average the two pass medians:

| Trees | Regions | Original CPU submission | Resident CPU submission | Reduction |
| ---: | ---: | ---: | ---: | ---: |
| 4,096 | 128 | 0.637 ms | 0.049 ms | 92.2% |
| 16,384 | 512 | 2.435 ms | 0.102 ms | 95.8% |
| 65,536 | 2,048 | 9.696 ms | 0.341 ms | 96.5% |

All comparisons were pixel-identical. Every measured submission allocated zero managed bytes. Resident stages uploaded zero source records, and only their region records, on settled frames.

These are controlled **submission measurements**, not whole-game FPS gains or a kilometer-scale world benchmark. They exclude chunk lookup, generation, grounding changes, ecological refreshes, nearby meshes, other species/material batches and the rest of the game. Existing manager-level frustum rejection can also avoid work that this synthetic reference deliberately submits. The CSV's synchronized-render column additionally includes render submission and GPU completion through a one-pixel readback; it is not isolated GPU time.

Raw measurements and device details: [benchmark.csv](../ProfilerCaptures/ResidentTreeSubmission/benchmark.csv), [environment.txt](../ProfilerCaptures/ResidentTreeSubmission/environment.txt). Reproduce with `Tools/MeasureResidentTrees.ps1`; the script imports explicitly named temporary baseline fixtures, waits for hidden Unity to finish, then removes those fixture files. Its outputs and exact baseline sources remain under `.utmp/tree-resident/`.

## Correctness checks

Run Unity with graphics enabled, `-batchmode -projectPath <project> -executeMethod DistantTreeResidentValidation.RunBatch -logFile <log>`. Use `RunAllBatch` to additionally run standing-tree spatial, standing-tree, vegetation-refresh and local-flame regressions. Do not use `-nographics`.

The resident suite covers CPU/GPU handoff and blended-height parity over moving viewpoints, independent region fades, hidden/unsubmitted regions, sparse source uploads, zero steady managed allocations, dense dispatch after removal, region/slot reuse, mixed material fallback, GPU disable/re-enable, cut-state rebuilds, coarse/detailed terrain grounding and approaching near trees. Existing GPU checks also exercise more than 1,023 visible instances, buffer growth, ordering, thinning, source tint and bounds.

`RunAllBatch` completed with exit code 0: 13,597 resident checks, the existing grounding/GPU checks, spatial pixel checks, standing meshes/LOD/snow/wind checks, registry/generation parity, vegetation refresh and local-flame checks. The combined legacy regression suite emits the same four persistent-allocation shutdown warning present in the earlier `.utmp/tree-spatial-validation.log` and `.utmp/vegetation-refresh-validation-final.log`; the standalone resident suite emits no such warning.
