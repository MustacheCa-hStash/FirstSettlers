# Directional tree impostor outlines — 6 November 2026

The six current HQ tree visuals now use **four-vertex rectangles fitted separately to each capture direction**. This preserves the existing four vertices/two triangles per tree while reducing blank rasterized area. Eight-, twelve- and sixteen-vertex options are also available. The saved SmearScene and new `TreeSettings` use `Four`; `Quad` restores the original full capture square. Restart Play Mode after changing the setting.

## Construction and directional safety

`TreeImpostorOutlineBaker` reads the imported BC7 atlases directly on the GPU. For each of the 64 capture frames it finds the full texel-cell support of either nonzero albedo alpha or the semantic leaf/bark IDs that survive the runtime cutoff. It includes compression artifacts and a two-texel guard, then intersects supporting half-planes to create conservative convex polygons with 4, 8, 12 or 16 vertices. Every source row's coverage extrema are checked for enclosure. Degenerate cases keep a full quad rather than risk coverage loss.

The semantic ID shader fetches mip zero. Enclosing its complete support bounds the final visible pixels even when the albedo samples coarser mips. This assumption must be revisited if material-ID sampling changes. This is an outer hull; internal gaps continue to use the original alpha test.

The vertex shader selects the outline with the **same `SelectAtlasFrame` result as the texture image**. Looking from below, above, or around a tree changes both together. It does not reuse one front-view silhouette for other directions or shrink the tree's 3D model. The existing root correction, camera-facing axes, terrain placement, scale, affine wind and texture coordinates are retained. Mesh culling bounds remain the original conservative bounds.

Outline coordinates are shared per species/view in a GPU buffer. A four-vertex variant uses 2 KiB per species (64 × 4 × 8 bytes), approximately 12 KiB across the six visuals. There is no extra per-tree payload or per-frame CPU outline generation. The extra work is one small buffer fetch per existing vertex; the fragment shaders are unchanged. Only enabled outline variants execute that vertex lookup.

The resource contains exact source texture references, capture dimensions, padding and dependency hashes. Missing data, incompatible layouts, unsupported devices, or stale inputs in the Editor fall back to the quad. **Rebake after changing source atlases or import settings before building**; source content hashes are Editor metadata. Runtime ownership disposes the outline buffers with the existing billboard batches.

## Area and GPU results

Mean polygon area as a fraction of the original capture square, averaging all 64 frames equally:

| Visual | Fitted 4 | Fitted 8 | Fitted 12 | Fitted 16 |
| --- | ---: | ---: | ---: | ---: |
| Red maple | 30.1% | 24.0% | 23.6% | 22.8% |
| Sugar maple | 29.4% | 24.0% | 23.2% | 22.6% |
| Oak | 29.0% | 23.6% | 22.9% | 22.4% |
| Birch | 27.8% | 22.8% | 21.0% | 21.0% |
| White pine | 25.4% | 20.9% | 18.9% | 18.4% |
| Spruce | 30.7% | 24.7% | 22.2% | 20.7% |

These are raster-area estimates, not a corresponding reduction in total fragment time. The original shader already rejects empty pixels early, and the views are not weighted to a particular game camera distribution.

Measured **GPU timestamps for the tree draws**, milliseconds, on RTX 5090 / D3D11 / Unity 6000.4.8f1:

| Fixture | Original quad | Fitted 4 | Fitted 8 | Fitted 12 | Fitted 16 |
| --- | ---: | ---: | ---: | ---: | ---: |
| Mixed 1,024 trees, 1440p | 0.0476 | 0.0333 | 0.0325 | 0.0333 | 0.0329 |
| Mixed 8,192 horizon trees, 1440p | 0.0268 | 0.0225 | 0.0195 | 0.0201 | 0.0209 |
| Mixed 1,024 trees, 4K | 0.0709 | 0.0495 | 0.0486 | 0.0484 | 0.0475 |
| Spruce 1,024 trees, 1440p | 0.0160 | 0.0125 | 0.0129 | 0.0133 | 0.0133 |

The fitted rectangle gives most of the measured benefit without adding vertices. Higher budgets sometimes improve the horizon case by a few microseconds, but do not establish a useful general advantage over four vertices. These absolute costs are small on this GPU; this is not a large whole-game FPS recovery or a guarantee on other devices.

## Future vertex and shader pressure

The investigation also draws approximately **198,147 additional submitted mesh vertices** with a synthetic sixteen-iteration vertex shader, a separate full-screen sixteen-iteration fragment workload, and both together. These are controlled GPU pressure tests, not a model of actual character skinning, shadow passes, AI or future gameplay.

For the 1440p mixed 1,024-tree case, total measured GPU scope with both pressures changed from **0.1289 ms** to **0.1176 ms** with fitted four, versus **0.1170 / 0.1174 / 0.1171 ms** with 8 / 12 / 16. The differences between fitted choices are too small to justify multiplying tree vertex work by default.

At 8,192 trees, four vertices submit 32,768 tree vertices. Eight doubles that to 65,536; twelve submits 98,304; sixteen submits 131,072. Tree vertex shaders perform transforms, frame selection, billboard rooting and a lookup, so these counts are not interchangeable with a vertex from a different shader. Four avoids adding **98,304 vertices** relative to sixteen at this population and leaves that budget available to other systems.

The near/horizon tests use real assigned tree materials, stable per-tree transforms, neutral tint, no wind for timing, no fog, and the resident renderer's 96-byte visible-instance format with indirect draws. Six visual types share their existing batches. There is no terrain occlusion, nearby 3D tree mesh work, streaming, culling compute or remaining game frame. Fixtures use 128 sparse trees, 1,024 nearer trees at 200–800 m and 8,192 horizon trees at 1–4 km. Character and other future content still need profiling in a complete frame.

Each stage has eight warm-up and twenty-four measured iterations; a second pass reverses quality order. Tree and total scopes use GPU-enabled `CustomSampler` command-buffer samples. The separate synchronized-completion columns include CPU/driver/readback overhead and must not be presented as isolated GPU time. See [Unity's command-buffer sample API](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/rendering/commandbuffer/beginsample).

## Accuracy and validation

The rendered enclosure test covers all six visuals, all 64 frame centers and 224 views immediately to either side of the internal octahedral frame boundaries. It repeats at 32/128-pixel capture cells, MSAA 1/4 and wind strength 0.15. All fitted budgets had **zero geometrically excluded reference pixels**.

Changing the triangles introduces small sampling differences even when geometry fully encloses the reference. For fitted four, the worst small-cell test lost 0.28% of reference-covered pixels through raster/texture/alpha-to-coverage differences; these are measured separately from geometric clipping. In the lit timing renders, fitted four's worst mean normalized RGB error was **0.000021008**, with at most 827 pixels above the RGB comparison threshold across the tested 1440p/4K images. Results are therefore not described as pixel-identical. The tests enforce an enclosure requirement and bound sampling errors rather than treating any matching polygon as sufficient.

The final integrated validation passed: all six world batches install four vertices/two triangles, source materials and meshes remain unchanged, conservative bounds remain intact, invalid capture layouts fall back, and resident-tree, standing-tree, spatial pixel, generation/registry, terrain-refresh and local-lighting regression suites pass. The older combined regression suite retains its pre-existing four-persistent-allocation shutdown warning, also present before this change.

Raw data: [areas.csv](../ProfilerCaptures/TreeImpostorOutlines/areas.csv), [quality.csv](../ProfilerCaptures/TreeImpostorOutlines/quality.csv), [performance.csv](../ProfilerCaptures/TreeImpostorOutlines/performance.csv), [environment.txt](../ProfilerCaptures/TreeImpostorOutlines/environment.txt).

Reproduce with `Tools/MeasureTreeImpostorOutlines.ps1 -Mode Bake`, `-Mode Validate`, or `-Mode Measure`, with the main Unity project closed. These use a hidden graphics-enabled Editor; `-nographics` cannot run the tests. Results and diagnostic images are saved under `.utmp/tree-outlines/`. Temporary coverage/footprint shaders are removed by the investigation on completion.
