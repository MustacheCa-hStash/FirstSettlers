# Current Terrain CPU/GPU Pipeline

This describes the terrain system as it is implemented today. It covers the
runtime order of work, where it executes, and what crosses from CPU work to GPU
rendering. It does not describe a proposed quadtree or GPU-instanced terrain
replacement.

## Current representation

- **Near terrain:** independent `128 x 128` terrain chunks. The scene renders a
  circular active region (`viewDistance = 100`), while collision is limited to a
  smaller circular radius (`colliderDistance = 2`).
- **Near mesh LOD:** the requested mesh LOD is selected by Chebyshev chunk ring:
  `0` at rings 0-1, `1` at 2-3, `2` at 4-5, `3` at 6-7, and `4` at 8+.
  Its mesh grid step is `1 << lod`.
- **Far terrain:** beginning beyond ring 8, world-aligned quadtree leaves replace
  fully-far chunks. Leaves are `4 x 4`, `8 x 8`, `16 x 16`, or `32 x 32`
  chunks. A boundary strip remains normal chunks where a larger leaf would cross
  a LOD band. Far leaves have no collider or detailed chunk foliage runtime.
  Leaves through `16 x 16` use a `33 x 33` height grid; `32 x 32` leaves use
  `65 x 65`. Each leaf uses a `61 x 61` control-map grid.
- **Render objects:** every normal chunk and every far macro tile owns a
  `GameObject`, `MeshFilter`, and `MeshRenderer`. Far tiles have an individually
  generated mesh and a cloned material with tile-specific control-map textures.
  They are not GPU-instanced.

## Per-frame execution order

`WorldManager.Update()` calls `ChunkManager.UpdateActiveChunks()` every frame.
The major terrain stages occur in this order:

1. **[Main CPU] Apply completed asynchronous results.**
   `ProcessCompletedRequests()` removes completed terrain-data, LOD-mesh,
   collider, and far-terrain results from thread-safe queues. It creates Unity
   `Mesh` and `Texture2D` objects on the main thread, validates that each result
   is still wanted, stores accepted results in a chunk/tile record, and queues
   its visual attachment.

2. **[Main CPU, only on chunk-boundary crossing] Rebuild desired coverage.**
   The manager converts the viewer position to a chunk coordinate. If it changed
   since the last update, it rebuilds the circular active set, sorts it by
   proximity and viewing direction, and decides whether each location is a
   normal chunk or a fully-far quadtree leaf. It retains outgoing meshes until a
   replacement mesh is attached, avoiding an empty handoff gap.

3. **[Main CPU] Create/reuse a bounded number of runtime objects.**
   At most four normal chunk runtimes are created/reused per frame, subject to a
   small time budget. Far-tile runtimes are also pooled. Object creation does
   not generate terrain geometry; it creates the Unity renderer containers that
   later receive generated assets.

4. **[Main CPU] Build camera visibility work.**
   The manager calculates the camera frustum planes, prioritizes urgent nearby
   chunks, processes a budgeted visual-work queue, and performs budgeted render
   visibility checks. This controls requests and renderer visibility; Unity also
   performs its normal renderer culling during rendering.

5. **[Main CPU] Request or attach normal chunk content.**
   For a normal chunk, the manager requests terrain data if missing. Once data
   exists, it chooses the current ring LOD, requests that LOD mesh if not
   cached, and attaches it when ready. Within the collider radius, it likewise
   requests/attaches a separate collider mesh. Meshes for previously requested
   LODs remain cached in the chunk record.

6. **[Main CPU] Request or attach far content.**
   For a fully-far quadtree leaf, the manager requests its far-terrain data or
   attaches its completed mesh/control maps. Far work is deliberately deferred
   while normal terrain, normal LOD mesh, or collider work is active or waiting,
   so player-proximate content wins scheduling priority.

7. **[Main CPU] Complete near/far handoffs.**
   Old normal chunks remain visible until their macro replacement has an attached
   terrain mesh. Old macro tiles remain visible until their replacement normal
   chunks have attached terrain meshes. The manager hides overlaps and returns
   no-longer-needed runtimes to pools.

8. **[Main CPU + GPU] Update foliage, then render.**
   Foliage streaming/drawing is updated after terrain management. Unity then
   culls and renders the enabled terrain and water `MeshRenderer`s. This is
   where the terrain shader executes on the GPU.

## Background CPU/Burst work

Requests are queued by `TerrainRequestManager` to worker threads. The heavy
terrain generation is CPU/Burst work; it does not execute in a compute shader.

### Normal chunk terrain-data request

The worker generates a padded height field and supporting maps: gradients,
slope, climate/moisture, biome, surface type, water state, ground cover,
river/mountain inputs, and feature-plan data. It then builds three raw
`Color32` control maps. These encode material *weights*, not final shaded RGB
pixels.

### Normal chunk LOD-mesh request

After normal terrain data is ready, a separate worker request uses it to build:

- the terrain mesh topology and vertices for the requested step increment;
- a water mesh at that same LOD.

The mesh generator preserves dense border samples to stitch chunk LODs without
cracks. This means coarser near LODs reduce interior geometry more strongly than
their border geometry.

### Collider request

Collider mesh data is generated separately, only for chunks inside the collider
radius. Far macro tiles do not request colliders.

### Far quadtree-leaf request

`FarTerrainGenerator.Generate()` runs on a worker thread. It:

1. samples the coarse height grid from the same world terrain evaluator;
2. derives slope and surface classification;
3. builds coarse terrain and water mesh data, including a skirt;
4. evaluates climate/surface/snow/ground-cover rules into three raw control
   maps.

The raw mesh data and `Color32` control-map arrays return to the main-thread
completion queue. Worker code cannot create or assign Unity `Mesh`,
`Texture2D`, or renderer objects.

## Main-thread upload and attachment

When a worker result is accepted, the main thread creates its Unity assets:

```text
CPU/Burst MeshData + ControlMapPixelData
        |
        v
main thread: MeshData.CreateMesh() + Texture2D.SetPixels32()/Apply()
        |
        v
chunk/tile record cache
        |
        v
MeshFilter.sharedMesh + cloned material _ControlMap0/1/2 assignments
        |
        v
Unity render submission
```

Each far tile receives its own control-map textures on its cloned terrain
material. The distinct textures, as well as the unique displaced mesh, prevent
the current far tiles from being combined by ordinary GPU instancing.

## GPU terrain rendering

The terrain uses `Custom/StylizedTerrain` (`CustomStylizedTerrain.shader`).
The vertex shader transforms already-generated mesh vertices and normals; it
does **not** sample a heightmap to displace a shared grid.

For every rasterized terrain pixel, the fragment shader:

1. samples `_ControlMap0`, `_ControlMap1`, and `_ControlMap2`;
2. sharpens and normalizes their material weights;
3. selects/blends sand, mud, grass, rock, cliff, snow, riverbed, and ground
   cover appearance;
4. samples world-space detail textures and normal maps where applicable;
5. applies procedural grass tint variation and distance-based detail fading;
6. applies main-light/shadow, ambient, fog, and final color output.

Therefore, **surface classification is CPU/Burst-generated, while final color,
texture detail, normals, lighting, shadows, and fog are GPU fragment-shader
work.**

## Important performance implications

- The quadtree reduces far renderer/request count and deliberately relaxes mesh
  spacing by level. It uses 16-unit spacing at the first far leaf, then 32 or
  64-unit spacing farther from the player.
- CPU work is bounded by worker-count and main-thread apply budgets. Far work
  intentionally yields to near terrain/collider work.
- GPU cost is driven by visible tile draw calls, terrain triangles, pixel
  shading, and texture sampling. The system does not currently use indirect
  draws, GPU terrain generation, GPU culling, or GPU instancing for terrain.
- A future instanced far-terrain implementation would need a shared grid mesh,
  GPU height displacement, and texture-array/buffer-based per-tile control
  data. A far quadtree, by contrast, can reduce patch count and triangle count
  while retaining the present CPU-generated mesh/control-map model.

## Primary implementation references

- `Assets/Scripts/WorldManager/WorldManager.cs`
- `Assets/Scripts/ChunkManager/ChunkManager.cs`
- `Assets/Scripts/ChunkManager/ChunkRuntime.cs`
- `Assets/Scripts/ChunkManager/FarTerrainTileRuntime.cs`
- `Assets/Scripts/TerrainGeneration/TerrainRequestManager.cs`
- `Assets/Scripts/TerrainGeneration/MeshGeneration/FarTerrainGenerator.cs`
- `Assets/Scripts/TerrainGeneration/MeshGeneration/MeshGenerator.cs`
- `Assets/Scripts/TerrainGeneration/BiomeGeneration/Surface/Color/TerrainControlMapBuilder.cs`
- `Assets/Shaders/S_Terrain/CustomStylizedTerrain.shader`
