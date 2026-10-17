# Tree rendering and split-tree integration guide

For prototyping a premade stump plus an upper tree section in FirstSettlers. Based on the repository and SmearScene configuration reviewed on October 16, 2026. **Current behavior** below describes existing code; **proposed integration** describes work still to be implemented.

## 1. The system in one minute

A generated tree has three separate representations:

| Representation | What owns it | What it contains |
| --- | --- | --- |
| Persistent identity and state within the current world session | `TreeRegistry` | ID, placement, species variant, tint, scale, snow, and Standing/Cut/Fallen/Removed state. No GameObject. |
| Standing appearance | `DistantTreeManager` and `StandingTreeRenderer` | Instanced meshes nearby, transitioning to instanced billboards/impostors farther away. No per-tree GameObject, Transform, or Update. |
| Nearby interaction | `TreeGameplayManager` | Pooled GameObjects containing trunk collision and/or canopy query shapes. No visual meshes or Rigidbody. |

**Approaching a tree does not turn its visual into a normal GameObject.** It stays instanced. The nearby collider/query proxy is a separate object that follows the same registry placement.

The tree code is a custom instanced submission system using Unity rendering APIs, including `Graphics.DrawMeshInstanced` and an optional indirect GPU path for distant trees. You do not need to replace the project's render pipeline to add a falling-tree system.

The existing `Cut`, `Fallen`, and `Removed` states all exclude the entire tree from standing mesh **and** billboard rendering. They also cause its nearby gameplay proxy to be released. **They do not currently spawn a stump, falling tree, logs, or replacement colliders.**

## 2. File map: where to start

Links are relative to this document so they work in the repository.

### Core identity, settings, and lifetime

| File | Responsibility / why you would open it |
| --- | --- |
| [TreeRegistry.cs](../Assets/Scripts/FoliageGeneration/Trees/TreeRegistry.cs) | Main-thread tree lookup and chunk registration. Start here for `TryGet`, `TrySetState`, and `ChunkChanged`. Detailed placements take priority over distant placement results. |
| [TreeRecord.cs](../Assets/Scripts/FoliageGeneration/Trees/TreeRecord.cs) | `TreeState` enum and the record's `Id`, `WorldPosition`, `Placement`, and `State`. |
| [TreeId.cs](../Assets/Scripts/FoliageGeneration/Trees/TreeId.cs) | Stable generated ID: world seed + logical chunk + placement source + candidate cell. Use this as the gameplay key. |
| [TreeInstanceData.cs](../Assets/Scripts/FoliageGeneration/Trees/TreeInstanceData.cs) | Placement struct: chunk-local position, rotation, scale, variant, colors, snow, and ID. |
| [TreeSettings.cs](../Assets/Scripts/FoliageGeneration/Trees/TreeSettings.cs) | Near/billboard prefab assignments, species cards, visual distances, streaming, and nested gameplay settings. |
| [TreeSpeciesCatalog.cs](../Assets/Scripts/FoliageGeneration/Trees/TreeSpeciesCatalog.cs) | Resolves each variant's near prefab, billboard prefab, and datacard, including fallback rules. |
| [WorldManager.cs](../Assets/Scripts/WorldManager/WorldManager.cs) | Public world access through `world.Trees` and `world.TreeGameplay`, available after initialization. |
| [ChunkManager.cs](../Assets/Scripts/ChunkManager/ChunkManager.cs) | Constructs and disposes the registry and managers. Updates standing visuals before nearby gameplay proxies. |

### Rendering and generation

| File | Responsibility / integration consequence |
| --- | --- |
| [StandingTreeRenderer.cs](../Assets/Scripts/FoliageGeneration/Trees/StandingTreeRenderer.cs) | Reads the assigned near prefab's mesh renderers, child transforms, submeshes, materials, and LODGroup without instantiating it. Builds shared definitions and submits instanced parts in batches of up to 500 per logical chunk. |
| [DistantTreeManager.cs](../Assets/Scripts/FoliageGeneration/DistantTreeManager.cs) | Despite its name, coordinates **both near and far** tree rendering. Builds standing-only registry snapshots; handles mesh/billboard handoff, far density, terrain seating, cache lifetime, and CPU/GPU submission. |
| [DistantTreeGpuBatch.cs](../Assets/Scripts/FoliageGeneration/DistantTreeGpuBatch.cs), [DistantTreeCompact.compute](../Assets/Shaders/DistantTreeCompact.compute) | Resident distant instance buffers and compute visibility/compaction. Usually unnecessary to touch for a first split-tree prototype. |
| [StandingTreeInstance.hlsl](../Assets/Shaders/StandingTreeInstance.hlsl), [TreeSimpleLitCommon.hlsl](../Assets/Shaders/TreeSimpleLitCommon.hlsl), [TreeShadowCasterCommon.hlsl](../Assets/Shaders/TreeShadowCasterCommon.hlsl) | Per-instance appearance and dither support across visible and shadow passes. Reuse compatible tree materials; a new shader needs this interface for standing rendering. |
| [FoliagePlacementService.cs](../Assets/Scripts/FoliageGeneration/FoliagePlacementService.cs), [FoliageManager.cs](../Assets/Scripts/FoliageGeneration/FoliageManager.cs) | Generate/register detailed placements and schedule work. They do not create the current world runtime's standing visual GameObjects. |
| [DistantTreePlacement.cs](../Assets/Scripts/FoliageGeneration/DistantTreePlacement.cs) | Background placement generation for distant chunks. Returns data, not scene objects. |
| [ChunkFoliageRuntime.cs](../Assets/Scripts/FoliageGeneration/ChunkFoliageRuntime.cs) | Contains older tree GameObject compatibility paths. Do not treat those paths or their pooling settings as the active standing-tree architecture. |

### Collision and selection

| File | Responsibility / integration consequence |
| --- | --- |
| [TreeGameplayAuthoring.cs](../Assets/Scripts/FoliageGeneration/Trees/TreeGameplayAuthoring.cs) | Component on the source near prefab explicitly listing physical trunk colliders and canopy query colliders. It is a template, not a runtime tree controller. |
| [TreeGameplayManager.cs](../Assets/Scripts/FoliageGeneration/Trees/TreeGameplayManager.cs) | Chooses nearby standing trees, copies only authored collider shapes and their transform ancestry, binds proxies, and pools them per source prefab. |
| [TreeGameplayProxy.cs](../Assets/Scripts/FoliageGeneration/Trees/TreeGameplayProxy.cs) | Active proxy's `Id` and `Record`; also a `QueryTarget`. Unbinding clears identity and disables its collider roles. |
| [TreeGameplaySettings.cs](../Assets/Scripts/FoliageGeneration/Trees/TreeGameplaySettings.cs) | Independent physical/query activation and release radii, scan interval, activation budget, and inactive pool size. |
| [PlayerQuery.cs](../Assets/Scripts/PlayerMovement/PlayerQuery.cs), [PlayerQuery.md](PlayerQuery.md) | Existing player selection path. Target data can supply the actual `TreeRecord`; `Breakable` metadata does not itself implement chopping. |
| [GameplayLayers.cs](../Assets/Scripts/WorldManager/GameplayLayers.cs) | Physical trunk copies use `WorldSolid`; canopy copies use `QueryOnly` triggers. |

The fuller existing reference is [TreeRegistry.md](TreeRegistry.md).

## 3. What changes with distance

Visual detail and gameplay activation are independent. Distances below use individual tree-origin XZ distance from the viewer, except prefab mesh LOD selection, which uses screen size.

Let `C = chunkSize * worldScale` and `R = max(0, gameObjectTreeChunkRingRadius)`.

| Transition | Rule | Current SmearScene example |
| --- | --- | --- |
| Mesh remains fully visible before distance handoff | Through `(R + 1) * C` | 153.6 m |
| Mesh blends to billboard/impostor | Across the next chunk width, ending at `(R + 2) * C` | 153.6–192 m |
| Nearby physical trunk activates | `gameplay.activationRadiusChunks * C` | 24.96 m |
| Active trunk releases | `gameplay.releaseRadiusChunks * C`, with an enforced minimum gap | 32.64 m |
| Canopy query activates/releases | Separate canopy settings, same defaults | 24.96 / 32.64 m |

SmearScene uses `chunkSize = 128`, `worldScale = 0.3`, so `C = 38.4 m`, and `R = 3`. These examples are scene settings, not universal constants. The class default for `R` is 1.

Mesh LODGroup levels still change with apparent screen size and `QualitySettings.lodBias`. Adjacent levels dither; the last mesh level remains available until the world distance handoff. Billboards have their own outer fade/thinning. A missing resolved billboard uses instanced meshes as a fallback.

`gameObjectTreeChunkRingRadius` is a retained serialized name; the Inspector calls it **Mesh Detail Radius (chunk widths)**. `treeGameObjectWarmRetainExtraRings` is legacy and does not control the current nearby proxy pool.

Gameplay scans normally refresh every 0.1 seconds, after one metre of focus movement, or after relevant registry changes. Activations are nearest first, defaulting to 8 per frame and an approximate 0.5 ms budget. Active proxies are checked each update for state changes and release distance. A proxy returns to the pool when neither physical nor canopy roles remain active; the default limit of 128 applies to **inactive pooled proxies**, not active trees.

## 4. Authoring stump + upper-section meshes

**Splitting the standing model into multiple MeshRenderers is already supported.** The renderer extracts each eligible mesh part and its transform. That does not give those parts independent runtime behavior: their transforms become shared definition data, and the standing instance still has one placement and one state.

Recommended asset arrangement:

```text
StandingTree prefab                 # assigned as the near prefab
  Stump mesh                        # assembled intact appearance
  Upper section mesh                # assembled intact appearance
  Gameplay                          # existing authoring templates
    Physical Trunk
    Query Canopy

StumpAfterCut prefab                # proposed replacement asset
FallingUpperSection prefab          # proposed replacement asset
```

Keep replacement prefabs separate from the standing prefab. Otherwise active replacement/cap renderers may be extracted into the standing definition too.

Authoring rules that make later integration easier:

1. Use a consistent tree root at ground/trunk origin, with identity root rotation and scale. The extracted definition preserves **child-relative** transforms; do not rely on an authored root offset/scale to correct alignment.
2. Make the two assembled pieces reproduce the original silhouette exactly. Use matching cut height, seam positions, UVs, normals, and materials. Include closed cut faces on replacement assets. Avoid doubled coplanar cap faces in the intact version.
3. Give the falling section a documented cut/hinge pivot, or put it under a parent at that pivot and preserve a mesh-child offset. Store this relationship in prefab-local space so rotation and generated tree scale are applied once.
4. Keep bark/foliage material assignments intact. If the standing prefab uses an LODGroup, include the appropriate stump and upper renderers in **each intended LOD**. Without an LODGroup, enabled mesh renderers on active child paths are collected together.
5. Use simple compound colliders for the falling body. The current standing physical templates support non-trigger Box/Capsule/Sphere colliders; query templates additionally support convex MeshColliders. A future falling Rigidbody belongs to the replacement system.
6. Do not place chopping/falling logic only on the standing visual prefab and expect it to run in the generated world. Neither the renderer nor the proxy manager instantiates those behaviours.

In SmearScene, spruce and the generic near fallback currently use [Spruce_LOD0_v06.prefab](../Assets/Prefabs/Spruce_LOD0_v06.prefab). Use a separate experimental prefab while developing. Species can share a fallback visual while retaining their own identity/datacard.

## 5. Testing with one or a few trees

### Stage A: model and falling-behavior sandbox

Use a separate scene with a ground plane, camera, light, one intact tree, and the two replacement prefabs. Ordinary MeshRenderers are useful here. Keep scene-specific setup in one test driver; expose a cut action/button and a reset action.

Design the reusable cut/fall controller to accept an explicit world pose, scale, asset definition, and fall direction. Keep input, camera lookup, and test-scene object discovery in the driver. Later a registry adapter can supply the same inputs.

Test seam alignment, pivot rotation, collision, and reset at multiple yaw angles and at scale 1 plus the species' generated scale range. Current species datacards start at 1.3–1.5; do not bake that extra multiplier into replacement meshes or collider dimensions. Preserve leaf/bark tint and snow when transferring to normal renderers; the standing renderer's per-instance property blocks are not automatically copied to replacements.

### Stage B: small integration harness using the real registry

Before bringing in terrain generation, make a small MonoBehaviour driver that owns a `TreeRegistry`, `TreeSettings`, `StandingTreeRenderer`, and optionally `TreeGameplayManager`. They are plain C# services, not components to attach from Add Component.

1. Pick a fixed seed and chunk world width. Assign the experimental near prefab, and create one to three explicit placements in one chunk.
2. Give each placement a unique `TreeId.Generated(seed, chunk, TreePlacementSource.Forest, candidateCell)`. Use these synthetic IDs only inside this isolated registry; do not invent IDs for existing production trees.
3. Convert a desired world position to `localPosition` by subtracting `((chunk.x + 0.5) * C, 0, (chunk.z + 0.5) * C)`. Register the complete list through `RegisterChunk(..., TreePlacementDetail.Detailed)`.
4. Each frame, call `BeginFrame(camera)`, submit only registry records in `Standing` state using the placement and `Matrix4x4.TRS(record.WorldPosition, rotation, scale)`, then `EndChunk()`. For this near-only test, pass `billboardFade: 0f` and `loadFade: 1f` to `Submit`.
5. Update the gameplay manager with a movable focus position. Add `TreeGameplayAuthoring` to the source prefab root and assign its colliders explicitly. Do not also instantiate that prefab for visuals/collision in this mode, or the tree will be duplicated.
6. Cut by ID through the same adapter intended for the real world. Keep a separate map of replacement instances by ID. Reset must restore `Standing` **and** remove/reset replacements; the registry does not do the second half.
7. Dispose owned managers/renderers on teardown and unsubscribe events. Increment the registration revision if mutating and re-registering the same placement list in place.

This harness covers instanced extraction, identity, state filtering, and proxy pooling. It does **not** reproduce the automatic distant handoff: `StandingTreeRenderer` receives that fade from its caller. Test actual mesh/billboard transitions later in a world scene using `DistantTreeManager`, which depends on terrain/chunk surfaces.

## 6. Proposed integration into the real world

Keep a dedicated cut/fall manager keyed by `TreeId`, separate from the standing renderer and its collider pool. A proposed asset definition can map each tree type to its stump prefab, upper-section prefab, and local cut pose. This mapping does not exist in `TreeSettings` yet.

Suggested cut sequence:

1. Resolve the ID through `world.Trees.TryGet` and verify `Standing`. Capture `WorldPosition`, rotation, scale, variant, tint, snow, and cut pose **before** changing state.
2. Prepare the replacement stump and upper body, initially inactive, so an asset/setup failure does not leave a missing tree.
3. Set the registry state to `Cut`; activate replacements and start the fall through the new manager. Coordinate with the frame order so standing draws and the old proxy are gone when replacements take over. Registry notification invalidates render data; it does not cancel a draw already submitted this frame. The existing world updates rendering before gameplay proxies.
4. Let the new manager own stump collision, falling-body physics, queries, and any eventual log conversion. Set `Fallen` when appropriate, but remember that state alone has no replacement behavior. Define separately whether a later `Removed` state also removes the stump.

Capture the **ID**, not a proxy Transform/GameObject, when an interaction starts. A proxy can unbind, enter the pool, and later represent another tree. Resolve the record again when completing a delayed action.

Keep cut-state data independent of replacement GameObject lifetime. The registry preserves state across render-cache eviction and deterministic chunk regeneration during the session, but it does not save it to disk. If replacement objects are streamed out, the new manager must recreate the appropriate stump/fallen representation on return. Likewise, settling bodies, pooling replacements, save/load, and replication are new policies to design; they are not supplied by the standing proxy pool.

## 7. Checks before integration

- Cut one tree among several: only that ID changes; no duplicate intact tree, residual canopy query, or old full-height solid trunk remains.
- Check seam, pivot, tint, and collision at different scales and rotations.
- Move the focus outside release distance and return: proxy pooling must not restore a cut tree or change which tree a pending action targets.
- Cross the actual mesh/billboard boundary and revisit the chunk: a cut tree must not reappear as a full-tree billboard.
- Reset repeatedly and leave Play Mode: no replacement bodies, event subscriptions, or runtime materials should leak.

Existing Unity checks are **Tools > Terrain > Validate Tree Registry**, **Validate Tree Gameplay**, **Validate Tree Canopy Queries**, and **Validate Standing Tree Instancing**. Their source files are under `Assets/Editor/` with corresponding names such as `TreeGameplayValidation.cs`. `TreeGameplayValidation` is a useful reference for a tiny manually registered fixture; `StandingTreeValidation` demonstrates multi-part meshes and child transforms. The standing-instancing validation also opens SmearScene, so save scene work before running it. These checks cover existing behavior; add focused cut/fall checks when that implementation exists.
