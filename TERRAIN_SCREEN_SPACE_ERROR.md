# Terrain screen error diagnostic and retired experiment

The target is 3840 x 2160 at 200 FPS (5 ms per frame). Runtime terrain refinement and fixed-view simplification were removed on October 3, 2026 after player testing showed no performance benefit. Their inspector settings, F6 switching, baked resource, runtime support and experiment-only tooling are removed. Terrain uses the original regular macro meshes.

The F3 terrain readout and separate average FPS / 1% low panel remain. F7 resets its rolling 30-second sample; terrain regeneration and focus changes reset it automatically. Distant-tree ground targets remain cached for the original support grid, preserving height blending and avoiding repeated work once settled. The read-only screen-error diagnostic remains available for future quality analysis.

## Experiment outcome

Local refinement raised the meadow surface triangle count from 368,640 to 1,741,930 (4.73 times), and the player observed over 60 FPS average loss and worse stutters. Disabling it restored performance.

The later frozen simplification removed 7.6% of meadow and 34.7% of hill far-terrain surface triangles, subject to a sampled 1-pixel additional shape difference and a 5-degree interpolated-normal guard. These reductions did not establish a rendering speedup. After cleaning recurring tree-grounding overhead, the final hill movement/turning test yielded:

| Mode | Average FPS | 1% low FPS |
| --- | ---: | ---: |
| Original | 305 | 165 |
| Simplified | 286 | 155 |
| Original repeat | 290 | 175 |

There was run-to-run variation, but simplification was below both original runs. The runtime experiments were retired; no further performance work is currently required. Historical CSVs, offline selection metadata and validation reports remain in `ProfilerCaptures/TerrainGridComparison` and `ProfilerCaptures/TerrainSimplificationComparison`. Those are experiment records, not active runtime settings. No cause for the remaining frame-time difference was established by CPU/GPU profiling.

## Using the diagnostic

1. Enter Play Mode in SmearScene and wait for terrain streaming to settle.
2. Open **Tools > Terrain > Far Terrain Screen Error**. It automatically finds WorldManager and its gameplay camera on the first measurement; assign them explicitly when inspecting another world/camera.
3. Leave the target viewport height at **2160**, even if the editor Game view is smaller. Use a 16:9 gameplay camera/Game view for the 3840 x 2160 target. Measure the nearest 16 macro patches in the camera frustum to start. Camera position, rotation and projection are captured when measurement starts; stand still for the baseline.
4. Read max/RMS shape error in world units, **Projected px** for the captured view, and **Proxy px** for the current view. Green is at or below the projected target, yellow is up to twice the target, and red exceeds twice the target. Gray means the patch had no on-screen sampled endpoints; it does not prove that the patch is invisible. Scene view outlines use the assigned gameplay camera's captured projection, rather than the Scene view camera. The vertical line marks the largest sampled height residual.
5. Changing target pixel height/error rescales/recolors the cached projected measurements. After moving or rotating the camera, remeasure to obtain angle-aware errors for the new view. The distance proxy updates live, while projected values explicitly retain the original camera snapshot. Results whose meshes were replaced, pooled or moved are ignored.
6. Export CSV for each view. Compare a meadow, a mountain skyline, and an elevated valley view. Repeat with dense sampling for rough patches; a higher measured maximum means the quick sample pattern missed detail.
7. Close the window before performance captures. Sampling runs in one asynchronous Burst job at a time, but it competes with streaming jobs; the outlines/window also add editor work. Exported diagnostic elapsed time includes editor scheduling and is not a terrain generation or rendered-frame timing.

Regenerate terrain after editing WorldManager generation settings and remeasure. The diagnostic takes an inspector settings snapshot at the start of a measurement run. Use matching settings and generated meshes.

## What is measured

The tool reads actual generated macro mesh triangles. Each triangle is sampled at its centroid and three edge midpoints. Dense mode adds its vertices and three interior points. Samples interpolate the actual triangle plane, not a bilinear height surface. Vertical skirts are excluded because they have zero area in the terrain's X/Z plane.

The exact height evaluator used by far terrain generation is evaluated at those world positions, including seed, mountain coverage, erosion, river/water settings and height/world scales. The largest absolute vertical residual and RMS residual are retained per patch. This is a sampled estimate: narrow extrema between the probes can be missed. No safety factor or formal error bound is claimed.

**Projected pixels:** each sampled point on the mesh and its corresponding intended terrain point are transformed through the captured camera's view and projection matrices. Their two-dimensional screen displacement is measured at the target viewport size, and the maximum is retained. This accounts for camera elevation, viewing angle, FOV and perspective depth changes. A height displacement directly along the centre viewing ray can have zero projected displacement; off-axis points can still shift through perspective. Samples with neither endpoint on screen are omitted. Visible near/far clipping crossings are flagged as infinite error. CSV camera fields refer to this captured view.

**Distance proxy:** perspective projection uses `maxError * viewportHeight / (2 * nearestBoundsDistance * tan(verticalFOV / 2))`. The mesh bounds are expanded vertically by the sampled maximum before computing distance. Orthographic projection uses `maxError * viewportHeight / (2 * orthographicSize)`. The proxy is useful for a simple future LOD policy, but does not fully account for view direction. The exported proxy uses the captured view; the displayed proxy uses the current camera.

Both metrics describe sampled geometric displacement. Neither measures complete image differences, visible silhouettes, or whether a patch is hidden behind terrain. Textures, normals, water and lighting error are not measured. Only macro patches are included in this initial tool; single-chunk far meshes and near terrain are excluded. Low projected displacement in an overhead view does not by itself justify removing geometry: changes in visibility, silhouette and shading need separate checks.

## Validation

Run `./Tools/ValidateTerrainDebug.ps1` for isolated Unity compilation and checks of FPS statistics, cached tree grounding, original terrain screen-error sampling, and inspector field/dependency coverage. The log is `Logs/terrain-debug-validation.log`. These checks verify behavior and geometry; they do not measure in-game frame times.
