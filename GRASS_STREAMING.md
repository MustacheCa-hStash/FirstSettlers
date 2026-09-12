# Grass streaming

Grass uses deterministic Burst candidate discovery and resident GPU-instanced drawing. Detailed and billboard meshes share the same positions, scales, forest tint and stable selection ranks. The CPU retains canonical candidates for caching and the hardware/material fallback; no GPU readback is used during play.

## Controls

- **Cells Per Axis** sets candidate grid resolution per chunk (125 = at most 15,625 candidates before surface/exclusion filtering). Both representations use it.
- **Sub Chunks Per Chunk** controls independent generation/publication units. The default 10 creates 100 units per chunk, not 100 draw calls.
- **Active Ring Radius** now expresses the detailed-to-billboard transition distance in chunk widths from the actual player, not a circular test of integer chunk coordinates. Diagonal ground is treated by physical distance.
- **Billboard Ring Radius** sets outer grass distance. An outer transition band tapers density; outer distance is kept outside the detailed transition.
- **Transition Width Chunks**, **Representation Transition Seconds**, and **Representation Hysteresis** control the blend. Stable per-candidate representation ranks choose one mesh, without double-drawing the same clump or waiting for another distribution to generate.
- **Density Radius 3 / 6 / 10 / Beyond 10** specify near density versus distance in subchunk widths, interpolated continuously. **Billboard Coverage** (default 0.2) controls far coverage from that same candidate pool. The retired billboard cell grid/spawn-chance and active-subchunk-radius fields are hidden and unused by the new path.
- **Max Concurrent Grass Jobs**, **Max Sub Chunk Generations Per Frame**, and **Sub Chunk Generation Budget** bound discovery work. Completion and uploads have separate budgets and guaranteed individual progress. Pending demand persists in state rather than disappearing when a queue fills.
- **Near Grass Precompute Chunk Padding** adds prefetch distance. At least one subchunk is prefetched. Renderer cache retention adds a transition-width margin; canonical CPU data is reused on re-entry when its inputs still match.
- Density/range changes take effect without rediscovering candidates. Grid, jitter, scale, seed and exclusion changes invalidate discovery; existing displayed slots remain until replacements arrive. Changing prefab meshes/materials requires rebuilding the world so render assets are resolved again.

## Batching

GPU grass has one resident source arena per chunk and separate visibility/argument buffers for the two meshes. Each completed subchunk uploads only its reserved range. There are at most two indirect draw submissions per rendered chunk; indirect draws are not split at 1,023 instances. Camera culling, density and representation selection happen on the GPU. The fallback uses DrawMeshInstanced batches of up to 1,023 and retains the same selection math.

Other foliage remains separate: flower/clover/dandelion and chunk tree-billboard batches have at most 1,023 instances each and are split by chunk/type/material. Near trees, bushes and rocks remain instantiated GameObjects. Whole-scene draw-call totals also include terrain, water, material passes and applicable shadows; they cannot identify grass overhead by themselves.

## Validation and limitations

Tools > Terrain > Validate Resident Grass Streaming runs state and compute correctness checks, not a scene benchmark. It covers bounded progress, cache re-entry, settings replacement, empty terrain, GPU/CPU-reference selection, complementary transitions and partial buffer uploads. Editor-only timing fields support test assertions/logs; no new in-game overlay exists.

Ask the user to validate walking diagonally across chunk boundaries, stopping until work settles, and entering/leaving dense forests. Do not launch automated camera/scene audits: the user explicitly rejected that workflow.

Candidate discovery remains CPU/Burst and currently copies terrain inputs per scheduled subchunk. This is a remaining opportunity for shared immutable input snapshots if profiling warrants it. Resident buffers trade memory for avoiding CPU selection/rebuilding on movement. Material tags are checked case-insensitively so Unity's lowercase true does not silently disable GPU instancing.
