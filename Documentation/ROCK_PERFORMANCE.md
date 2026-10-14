# Rock performance investigation

The initial implementation passed correctness checks but introduced avoidable recurring rendering cost and extra sparse terrain sampling. This investigation uses saved SmearScene seed 14566 and viewer (-244, 100, 366), chunk size 128, world scale 0.3, and rock ring radius 6. That covers 113 chunks of 38.4 m width, with a 230.4 m centre-to-centre radius. Boundary chunks extend beyond it. This is the saved spawn range, not the user's current live position.

## Geometry and submissions

| Measurement | Initial implementation | After fixes |
| --- | ---: | ---: |
| Resident rocks in saved spawn range | 497 | 496 |
| Resident full-detail vertex instances | 174,282 | 109,920 |
| Resident full-detail triangles | 229,824 | 217,856 |
| Mean full-detail vertices per chunk | 1,542.3 | 972.7 |
| Submissions before camera rejection | 335 per-chunk draws | Global grouping replaces this |
| Eight-heading comparison with identical updated geometry | 154.1 legacy chunk submissions | 25.5 global submissions, including offscreen shadows |
| Eight-heading visible vertex comparison with identical updated geometry | 36,469 with legacy chunk culling | 11,824 with individual culling and LOD |

Eight headings use a ground-level camera at spawn, 65-degree vertical FOV, 16:9 aspect and 400 m far plane. Optimized visible vertices range from 9,250 to 14,078; mean visible triangles are 23,104. Geometry counts are per submission, before multiplying by depth, normal or shadow-cascade passes. Instances share meshes: these figures do not mean allocating a separate mesh for every rock.

The triplanar material does not need UV seams. Removing them preserves the nearby geometry and reduces imported small-rock vertices from 233/248/250 to 162, and boulder vertices from 574/547/632 to 386. Distant meshes use 58 vertices/112 triangles for small rocks and 136 vertices/268 triangles for boulders, switching at 30 m and 60 m respectively. Hulls and nearby triangle counts remain.

## Native Unity rendering A/B

Identical updated meshes, placements, lighting, camera and 1280 × 720 target were rendered with the old and new submission paths. Each stage uses 24 warm-up frames and 80 measured frames; the order is none → legacy → optimized → optimized → legacy → none. There is exactly one render request per actual editor frame, since instanced submissions persist until a frame ends.

| Path | Median camera-render wall time | p95 | Mean managed/native rock submission time |
| --- | ---: | ---: | ---: |
| No rocks, first/last | 0.410 / 0.423 ms | 0.527 / 0.480 ms | 0 ms |
| Legacy per-chunk, first/repeat | 1.986 / 1.955 ms | 2.272 / 2.247 ms | 0.136 / 0.136 ms |
| Optimized global, first/repeat | 1.019 / 0.976 ms | 1.191 / 1.123 ms | 0.112 / 0.110 ms |

This isolates rock rendering without generated terrain or other vegetation. It demonstrates about 0.97–0.98 ms lower camera-render wall cost, not a GPU timestamp or a full-game FPS measurement. The nearby art, coatings, generation range and density limits are preserved. Small-stone shadows are limited to 45 m; boulder shadows retain 140 m, both capped by pipeline shadow range. Reusing candidate positions changes the snow distribution, explaining the one-rock difference in this window.

Warmed CPU-only culling/grouping measured 0.064 ms/frame with zero managed allocations. Stationary collider updates measured 0.0007 ms/frame with zero repeated scans; the last scan inspected 29/496 records and maintained ten active bodies. Graphics-enabled investigation repeats measured grouping at 0.075–0.091 ms and collision updates at 0.0008–0.0009 ms. Timings vary with hardware and game load.

## Causes and fixes

- **Small batches per chunk:** `RockWorldRenderer` now groups shared meshes/materials across chunks, culls individual transformed rock bounds, uses reusable arrays and selects distant detail. The old terrain frustum bounds are much taller than rocks and admitted many unnecessary submissions. Per-instance light-probe interpolation is disabled; the existing sun/ambient lighting remains. Shadow submission uses the active pipeline's range, and nearby offscreen casters remain independent of camera visibility.
- **Sparse worker sampling:** the independent 6 × 6 snow grid requested height/slope/river/climate data before rejecting warm biomes, also affecting distant-tree workers. Across the 113 neutral-climate Forest/Grassland map fixtures, it added 4,019 distinct positions; Snow added 2,586. The shared forest-boulder grid needs zero additional distinct samples in those fixtures. Mixed chunks where forest placement exits early can still require sampling previously unvisited shared candidates. Full terrain requests already own dense maps; their whole-feature-plan timings measured around 3.7–4.2 ms/chunk. The removed sampling overhead concerns the sparse worker path.
- **Repeated collision scans:** the original pool scanned every resident record ten times per second while stationary. It now rejects irrelevant chunk bounds and rescans on movement, registration changes or setting changes. Convex mode is assigned before the mesh, avoiding an initial non-convex cook.

## Queued versus synchronous work

1. World-feature planning runs inside background terrain requests and background distant-tree placement. Its candidate loop is one worker operation, not a render-frame coroutine.
2. Rock placement conversion is synchronous on the main thread once terrain/features are available, then cached. Initially it averaged 0.0178 ms/chunk, with a 1.0873 ms maximum including a cold call. Repeats averaged 0.0163–0.0183 ms, with a maximum up to 1.3949 ms. It was not the recurring standing-still cost.
3. Visual publication uses the FIFO foliage scheduler, yields per rock and swaps atomically. SmearScene permits at most one completed foliage publication per frame, a 0.35 ms publication budget and a shared 1.35 ms foreground foliage budget. Several cheap slices can complete in one frame; it does not require a separate frame per rock. Other foliage shares the queue. Budgets are checked between slices and are soft limits.
4. Nearby collider activation permits eight bodies per frame with a separate 0.5 ms soft budget. A single activation, cooking operation or physics synchronization must complete once started.
5. Rendering runs every frame independently of those generation/publication budgets. This recurring cost was missed by the original correctness-only validation. It now uses global grouping and culling of existing matrices, without visual GameObjects.

## Diagnostics and reproduction

Restart Play mode after import to replace cached placements/render data. **Tools > Terrain > Log Live Rock Performance** reports actual resident/visible counts, visible vertices/triangles, submissions, offscreen shadow instances, active/free colliders and records visited by the last scan. `TreeSettings.enableRockRendering` and `enableRockColliders` provide independent runtime A/B controls; the rendering toggle preserves physics.

Profiler markers: `FS.Rocks.ConvertPlacement`, `FS.Streaming.Foliage.PublishSlice`, `FS.Rocks.CullAndGroup`, `FS.Rocks.Submit`, `FS.Rocks.CollisionPool`, `FS.Rocks.CollisionScan`, `FS.Rocks.CollisionActivate`. The world-statistics rock bucket now reports culled visible LOD geometry.

`Tools/ValidateRocks.ps1 -Investigate` reproduces saved-range counts and CPU measurements. `-Benchmark` adds the native frame-separated A/B. Original and optimized reports are preserved in `.utmp/rocks/before-investigation.txt` and `after-investigation.txt`; the generated report is under the isolated project's `.utmp/rocks` directory.

Correctness checks cover dense/sparse agreement, global grouping across 113 chunks, the 1,023-instance split, LOD, tight culling, offscreen shadows, zero steady allocations, stationary scan suppression, pool limits, unload, query identity and A/B toggles. Production-renderer pixels and shader passes also pass. The complete live gameplay frame, GPU timestamp and user's recovered FPS remain unmeasured.
