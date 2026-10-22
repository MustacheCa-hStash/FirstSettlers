# Meadow and forest performance investigation — 22 October 2026

Follow-up: [Tree batching and billboard memory](TreeBatchingAndBillboardMemory.md) implements and validates spatial batching, examines atlas memory, and defers 3D tree LOD groups per the user's subsequent direction. The measurements below describe the original investigation.

There are genuine code issues, and a significant near-tree asset/batching cost. Distant billboard overdraw was **not the leading cost in the measured workloads**. Two movement-related refresh mechanisms were corrected. The complete reported 240 → 180 FPS / 200 → 130–140 FPS regression has not been attributed to one commit or reproduced at the user's exact view and resolution.

The reported changes correspond to roughly **1.39 ms extra per meadow frame** and **2.14–2.69 ms extra per forest frame**. The small steady CPU improvements made here do not recover that entire difference.

## Measured rendering costs

Hidden Unity 6000.4.8f1 batch mode, D3D11, RTX 5090, PC URP asset, 2560 × 1440. Each render request was synchronized with a one-pixel GPU readback. Values below are the mean of two pass medians, with 16 warm-up and 48 measured requests per pass, then reversed mode order. These are **CPU + render submission + synchronized GPU completion timings**, not isolated GPU timestamps or game FPS. The empty-camera baseline includes a constant small shadow caster so sun-shadow variants remain active in all modes.

The flat biome fixtures use the production stand generator, species prefabs, scales, shaders and standing/indirect renderers. Meadow has 10 near trees; forest has 121. Near trees occupy a 192 m radius. Far tests use 120 / 742 trees from 154–575 m at full density before culling. Near and far modes are separate experiments; their times must not be summed into a frame estimate. The fixture omits terrain occlusion, grass, buildings, fog and post-processing.

| 1440p test | Meadow total | Forest total |
| --- | ---: | ---: |
| Empty camera | 0.577 ms | 0.575 ms |
| Near trees, current per-chunk batching and shadows | 1.247 ms | 3.113 ms |
| Near trees, shadows disabled | 0.959 ms | 2.155 ms |
| Near trees, experimental global batching and shadows | 1.115 ms | 1.796 ms |
| Far HQ impostors | 0.772 ms | 1.064 ms |
| Far HQ, sun-shadow sampling removed | 0.750 ms | 1.007 ms |
| Far HQ, coverage/material-ID shading only | 0.736 ms | 0.969 ms |

Forest near-tree submission is **172 draws** for 420,768 submitted vertices, including offscreen shadow casters. Flushing the same render definitions once across the fixture instead of once per logical chunk reduces this to **10 draws**. The matched global-batch experiment saved about **1.32 ms** with shadows, while disabling shadows in the normal per-chunk mode saved about **0.96 ms**. These experiments overlap and their savings are not additive.

This makes spatial batching and near-tree shadow/LOD work the strongest measured next targets. Global batching is an experiment, not an enabled production change: its wider bounds change Unity's camera and cascade culling, and can increase offscreen vertex/shadow work. A production implementation should group nearby chunks and validate several headings and denser stands. The measured gain should not be treated as a guaranteed whole-game improvement.

HQ far impostors cost about **0.49 ms above the empty baseline** in the forest stress fixture. Replacing most lighting with exact coverage/material-ID shading saved about **0.09 ms**. Removing sun-shadow sampling saved about **0.06 ms**. Quad overlap still exists, but these measurements do not support blaming distant pixel shading for most of the observed regression. Results at higher resolutions or other views can differ.

Raw repeated measurements and 720p results: [render-ab.csv](../ProfilerCaptures/VegetationPerformanceInvestigation/render-ab.csv). Aggregated values: [render-summary.csv](../ProfilerCaptures/VegetationPerformanceInvestigation/render-summary.csv).

## Asset findings

The six assigned near-tree prefab sources have **no LODGroup**. `StandingTreeRenderer` supports authored LOD groups, but these assignments provide only their full-detail mesh level. The saved scene uses a three-chunk near radius; with 128 samples per chunk and world scale 0.3, its handoff runs from **153.6 to 192 metres**. Full-detail trunks and foliage therefore remain in use until that handoff.

Existing separate RedMaple, SugarMaple and Spruce LOD1/LOD2 prefabs, and WhitePine LOD2, are present but are not connected to those near-prefab definitions. Wiring suitable intermediate levels is an asset/renderer integration opportunity. It was not changed automatically because transition quality and shadow silhouettes need to be checked against the current trees.

Every HQ far tree is only **four vertices / two triangles**. Six distinct species sets each use four 8192² atlases plus an 1088² ambient atlas. Loaded formats include BC7 albedo/surface/material-ID, R8 depth and RGBA ambient. Unity reports **4210.96 MiB of unique tree texture runtime memory** across the audited near and far assets; this profiler number is not a direct measurement of dedicated VRAM usage. No VRAM exhaustion was established on the 32 GB test GPU.

`SampleMaterialIdAtlasPoint` explicitly reads material IDs from mip zero, even for distant proxies. This is a plausible texture-cache/bandwidth cost and an atlas optimization candidate, not evidence that it dominates the frame. Replacing it with filtered sampling without categorical mip handling risks changing leaf/bark classification and silhouettes.

The recent scene changes also raised the meadow tree count guard from 6 to 24 and enabled the new stand distribution. That guard is not itself the density target. The saved seed changed from 14567 to 1456, so an old/new FPS comparison without matched coordinates and world inputs also changes the population being rendered. Grass density settings decreased in those recent scene changes; this alone does not establish grass GPU cost.

Full mesh/shader/texture audit: [assets.csv](../ProfilerCaptures/VegetationPerformanceInvestigation/assets.csv).

## Refresh defects corrected

**Distant trees lost support during terrain handoffs.** `ChunkManager.TryGetDistantTreeSurface` originally queried only the newly desired quadtree leaf. After a chunk-boundary crossing, that leaf can be unavailable while the handoff coordinator deliberately retains the outgoing terrain mesh. Tree submission then skipped the whole affected logical region. Lookup now falls back to a visible, attached, published covering leaf at the existing quadtree sizes. It prefers the requested replacement as soon as it is ready. This uses bounded dictionary lookups rather than a scan of loaded terrain.

**Visual terrain LOD changes extinguished cached torch lights.** Geometry replacement invalidated every affected torch's cached static depth immediately, but only one static shadow refresh is admitted per frame. Four otherwise settled lights therefore fell to one active light until the queue drained. Pure baseline-terrain LOD changes now request a refresh while retaining valid cached shadows until their replacements are admitted. New geometry, same-LOD mesh replacements, removal, excavation and other physical changes retain immediate invalidation. A valid cache can never be restored by a later soft refresh if a real edit has already invalidated it.

**Empty tree publications caused unnecessary lighting invalidation.** Every tree-registry publication formerly invalidated a chunk plus 90 metres of horizontal padding and ten million metres vertically, even when the chunk contained no trees. Standing-tree providers now retain actual canopy bounds and invalidate the union of previous/current geometry. Empty-to-empty publications cause no invalidation; removals still invalidate the old canopy. Registries populated before renderer construction are handled too.

These are deterministic defects consistent with movement-related tree and torch refreshes. The free-running captures did not reproduce a general synchronized camera-turn-only disappearance, so a separate trigger in the user's exact location remains possible. A simple secondary reflection-camera submission test kept both transient instanced and ordinary renderer pixel counts unchanged (620 each); that test did not establish water reflections as the cause.

## Other code improvements and measured limits

- The viewer chunk is passed into tree surface lookup once per update, avoiding repeated Transform reads for every logical chunk.
- Empty tree manifests skip height/culling/drawing work after their ownership/revision checks.
- Camera projection values are cached per tree frame, and single-level prefabs skip unnecessary LOD calculations entirely.
- Farthest-point protection ordering caches each candidate's nearest selected neighbour, reducing manifest preparation from cubic to quadratic work without changing the order. The 128-tree stress manifest changed from 5.30 ms to 0.16 ms. Normal manifests are generally much smaller; this is spike reduction, not a per-frame 5 ms saving.

Two 200-second running-world captures used the saved scene, seed 1456, 1280 × 720, a 65-second settling allowance in each biome, then stationary, turning and ±2.5 m movement phases. Classifier-selected points were meadow (-628, 18.5, 442.8) and forest (-167.2, 18.5, 519.6). These are biome samples, not a claim to match the user's dense forest view. Profiler markers, registration changes and lighting state were recorded; screenshots were inspected. No exceptions were recorded.

Stationary `FS.DistantTrees.Update` medians changed **0.4160 → 0.3742 ms** in meadow and **0.7822 → 0.7319 ms** in forest. Those after-captures include the first surface/empty-manifest/camera-cache/order changes. The final torch LOD-refresh change and single-level LOD bypass were subsequently checked with targeted tests, rather than represented as part of that measured before/after comparison. Scheduling differences mean movement event totals should not be read as strict paired performance measurements.

[world-summary.csv](../ProfilerCaptures/VegetationPerformanceInvestigation/world-summary.csv) retains the capture summaries. Full frame rows and logs are under `.utmp/vegetation-performance/` and `.utmp/*vegetation*.log` in this workspace.

## Validation and reproduction

`VegetationRefreshValidation.RunBatch` passed retained terrain split/merge support, negative coordinates, preferred replacement selection, rejection of invisible/unattached surfaces, empty tree publication, old canopy removal/restoration, prepopulated registry handling and deterministic ordering parity. Its four-torch test verifies all lights stay active during queued LOD updates with one refresh per frame, and real replacements still invalidate immediately. The existing distant-tree GPU suite passed, including >1023 instances, culling, ordering, tint/fade, thinning, capacity growth and slot reuse. The existing local-flame suite passed **68 checks**, including zero light through a thin blocker, exact cached/baseline receiver parity, bounded refresh admission and zero steady managed allocation.

`TreeRenderPerformance.RunBatch` completed all offscreen render stages, including matched per-chunk/global batching and diagnostic shader variants. Generated diagnostic shaders are deleted on completion. `ReflectionSubmissionValidation.RunBatch` completed its secondary-camera probe. `git diff --check` passed.

Run these with the project's Unity version in batch mode, using `-projectPath`, `-executeMethod <method>` and `-logFile`, without `-nographics` for graphics checks. `VegetationPerformanceInvestigation.RunBatch` runs the 200-second world capture. The tools open scenes in memory and do not save authored scene changes. Editor-generated dynamic font cache clearing was restored after the captures.

Recommended next performance work, in order: **spatially combine near-tree draws**, connect suitable **intermediate tree LODs and shadow representations**, then evaluate **HQ atlas memory/material-ID mip handling**. A matched player-build view and resolution are still needed to account for the complete reported FPS loss and separate the remaining terrain, grass, water and post-processing GPU costs.
