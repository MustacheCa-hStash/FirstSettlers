# Mountain snow distribution

Implemented November 3, 2026 in `Assets/Scripts/TerrainGeneration/BiomeGeneration/MountainSnow.cs`.

The selected approach is a static terrain-aware retention mask, baked into the existing control maps. Near terrain and Burst far/macro terrain already call the same evaluator, so the change applies to both. The comparison favored this over adjusting the existing snow coverage gamma alone.

The previous model retained snow almost fully through 55 degrees and tapered as far as 85 degrees. Its square-root retention curve and render gamma favored a pale coating over much of the mountain. The revised model:

- Tapers nominal retention from 40 to 75 degrees, exposing steeper headwalls while retaining continuous snow on gentler benches. These are artistic limits for this project's exaggerated relief, not an avalanche simulation.
- Lets local concavity increase slope retention and lowers the snowline in sheltered terrain. Convex crests lose more snow.
- Strengthens the existing fixed north-facing and leeward biases. These depend on climatic directions (+Z north, wind toward +X), independently of the moving sun.
- Moves fine noise into the snowline elevation, so it breaks up melt boundaries without mottling full snowfields.
- Narrows the altitude transition and resolves accumulation through a smooth coverage curve, reducing broad intermediate snow/rock mixtures.

The climate snowline function, water/river protection, foothill influence, lowland tundra dusting and material normalization remain in use. Snow is a visual surface treatment; this change does not simulate snow depth, seasonal melting, transport, or avalanches.

## Tuning

The existing **Mountain Snow Blend Gamma** remains available in WorldManager's terrain rendering settings. Lower values increase partial coverage; 1 removes the coverage boost. The saved scene's 0.633 and the code default of 0.72 were retained. Gamma alone cannot place snow according to terrain shape.

The subsequent [snow/rock material separation](SNOW_ROCK_SEPARATION.md) adds a dedicated shader threshold and boundary width after map filtering. It preserves these generation maps while resolving their weights into clearer snowfields and exposed rock. The distribution comparisons below retain continuous material mixing to isolate the earlier generation change.

The selected implementation parameters are grouped in `MountainSnow.Evaluate`:

| Parameter | Selected value |
| --- | --- |
| North aspect snowline bias | 0.55 height units |
| Leeward snowline bias | 0.28 height units |
| Shelter snowline bias | 0.75 height units |
| Broad / boundary noise amplitudes | 0.4 / 0.28 height units |
| Altitude transition relative to local snowline | -0.35 to +1.35 height units |
| Slope retention normal Y limits | 0.2588 to 0.7660 (75 to 40 degrees) |
| Shelter adjustment to normal Y | 0.12 |
| Maximum exposed-crest coverage reduction | 30% |
| Final accumulation transition | 0.12 to 0.82 |

The existing `SampleRadius = 4` is unchanged. All scales are terrain coordinates; World Scale converts them to world coordinates. Regenerate terrain / restart Play mode to see the new baked distribution.

## Controlled visual comparison

The checked-in [renders](../ArtReferences/MountainSnow) use generated, eroded mountains, the real URP terrain shader/material/textures, Linear color space, identical cameras and lighting, synchronous shader compilation and warmed capture frames. They omit vegetation, water rendering, the saved scene's sky/fog and runtime streaming. They are distribution comparisons, not screenshots of the running world.

Seed 46 was tested with both default erosion settings and the relevant saved `SmearScene` landform settings (mountain relief 20, spatial scale 3.3, sparsity 0.01, lowland height ratio 0.3, hill variation 0.2). Both fixtures use sample scale 600, mountain coverage 1.3, height multiplier 200 and world scale 0.3. Each is a 4096-unit terrain patch sampled every 16 units. The snow slope/shelter stencil remains 4 units, independently of this display mesh spacing. Both fixture sets use gamma 0.633 for before/after; the gamma-only alternative uses the original distribution with gamma 1.15.

| Terrain | Original | Gamma-only alternative | Selected distribution |
| --- | --- | --- | --- |
| Default landforms | [Before](../ArtReferences/MountainSnow/default-before.png) | [Gamma only](../ArtReferences/MountainSnow/default-gamma-only.png) | [After](../ArtReferences/MountainSnow/default-after.png) |
| Saved-scene landforms | [Before](../ArtReferences/MountainSnow/scene-before.png) | [Gamma only](../ArtReferences/MountainSnow/scene-gamma-only.png) | [After](../ArtReferences/MountainSnow/scene-after.png) |

Corresponding `*-top.png` files show the same patches from above. In the default fixture, intermediate mountain snow weights (between 10% and 90%) decreased from 16.8% to 11.2% of mountain samples, while solid snow increased from 76.0% to 77.5%. Mean snow weight changed from 85.1% to 83.8%. The saved-scene fixture remains heavily snow-covered at high elevations, with more exposed steep areas and a clearer lower boundary.

## Performance and verification

Runtime evaluation still uses four neighboring height samples and two coherent noise evaluations. There are no additional buffers, textures, mesh vertices, draw calls, or shader passes in production. All changes occur during terrain generation. The comparison renderer and frozen original evaluator are editor-only.

The warmed Burst comparison alternates original/revised execution order, discards two warmup rounds, and reports the median of 15 runs over 66,049 identical samples. Measured added evaluation work was approximately 0.01–0.02 ms per patch on this machine. This measures only snow evaluation, including job scheduling/completion; it is not a whole-chunk or live FPS benchmark. Height generation, the four-point stencil and control-map upload costs are unchanged.

Validation passed:

- Full runtime, editor and player C# compilation (existing unrelated obsolete-API warnings).
- Climate/altitude response; north/south and windward/leeward ordering; sheltered versus exposed retention; high gentle snowfields versus steep bare faces; render-gamma endpoints.
- Water/river exclusions, normalized material weights and unchanged lowland snow dusting.
- X/Z chunk seams and near/far agreement across seeds 7, 42 and 12345, plus regional mountain coverage.
- All 49 surface pairs, far climate surfaces, shared control-map edges and terrain shader compilation.
- Two sets of actual Unity URP before/gamma-only/after captures and warmed Burst timing comparisons.

Run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tools/ValidateMountainSnow.ps1` from the repository root. It prepares a separate project under `.utmp/mountain-snow/unity-project`, writes the comparison images to `ArtReferences/MountainSnow`, and logs to `.utmp/mountain-snow/validation.log`. It uses the Unity version in `ProjectSettings/ProjectVersion.txt`. The existing **Tools > Terrain > Validate Mountain Snow** menu runs the snow correctness checks without rendering the comparison.
