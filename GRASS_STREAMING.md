# Grass streaming

Grass uses deterministic Burst candidate discovery and resident GPU-instanced drawing. Detailed and billboard meshes share the same positions, scales, forest tint and stable selection ranks. The CPU retains canonical candidates for caching and the hardware/material fallback; no GPU readback is used during play.

## Controls

- **Cells Per Axis** sets candidate grid resolution per chunk (125 = at most 15,625 candidates before surface/exclusion filtering). Both representations use it.
- **Sub Chunks Per Chunk** controls independent generation/publication units. The default 10 creates 100 units per chunk, not 100 draw calls.
- **Active Ring Radius** now expresses the detailed-to-billboard transition distance in chunk widths from the actual player, not a circular test of integer chunk coordinates. Diagonal ground is treated by physical distance.
- **Billboard Ring Radius** sets outer grass distance. An outer transition band tapers density; outer distance is kept outside the detailed transition.
- **Transition Width Chunks**, **Representation Transition Seconds**, and **Representation Hysteresis** control the blend. Stable per-candidate representation ranks choose one mesh, without double-drawing the same clump or waiting for another distribution to generate.
- **Density Radius 3 / 6 / 10 / Beyond 10** specify near density versus distance in subchunk widths, interpolated continuously. **Billboard Coverage** (default 0.2) controls far coverage from that same candidate pool. The retired independent billboard grid/spawn chance, active-subchunk radius and grass queue-cap fields were removed on November 6, 2026.
- **Max Concurrent Grass Jobs**, **Max Sub Chunk Generations Per Frame**, and **Sub Chunk Generation Budget** bound discovery work. Completion and uploads have separate budgets and guaranteed individual progress. Pending demand persists in state rather than disappearing when a queue fills.
- **Near Grass Precompute Chunk Padding** adds prefetch distance. At least one subchunk is prefetched. Renderer cache retention adds a transition-width margin; canonical CPU data is reused on re-entry when its inputs still match.
- Density/range changes take effect without rediscovering candidates. Grid, jitter, scale, seed and exclusion changes invalidate discovery; existing displayed slots remain until replacements arrive. Changing prefab meshes/materials requires rebuilding the world so render assets are resolved again.

## Batching

GPU grass has one resident source arena per chunk and separate visibility/argument buffers per biome and distance mesh. Each completed subchunk uploads only its reserved range. There are at most two indirect submissions for a chunk containing one grass type, or four for a mixed meadow/forest chunk using the forest tuft assets described in [FOREST_GRASS.md](FOREST_GRASS.md). Indirect draws are not split at 1,023 instances. Camera culling, density, biome and representation selection happen on the GPU. The fallback uses DrawMeshInstanced batches of up to 1,023 and retains the same selection math.

Other foliage uses separate renderers. Flowers, clover, dandelions and water plants use instanced batches. `StandingTreeRenderer` batches nearby tree meshes across spatial regions; `DistantTreeManager` renders their billboard representations with GPU compaction or CPU instancing. `RockWorldRenderer` groups rocks across chunks and selects their visible LOD. Berry bushes remain visual GameObjects. Nearby tree and rock physical/query proxies are pooled independently of those visuals. Whole-scene draw-call totals also include terrain, water, material passes and applicable shadows; they cannot identify grass overhead by themselves.

## Validation and limitations

Tools > Terrain > Validate Resident Grass Streaming runs state and compute correctness checks, not a scene benchmark. It covers bounded progress, cache re-entry, settings replacement, empty terrain, GPU/CPU-reference selection, complementary transitions and partial buffer uploads. Editor-only timing fields support test assertions/logs; no new in-game overlay exists.

Ask the user to validate walking diagonally across chunk boundaries, stopping until work settles, and entering/leaving dense forests. Do not launch automated camera/scene audits: the user explicitly rejected that workflow.

Candidate discovery remains CPU/Burst. Jobs borrow matching immutable native terrain maps through a lease; authoring fixtures or replaced maps use the managed-map flattening fallback. Filtered exclusion lists and result storage remain per job. Resident buffers trade memory for avoiding CPU selection/rebuilding on movement. Material tags are checked case-insensitively so Unity's lowercase true does not silently disable GPU instancing.
