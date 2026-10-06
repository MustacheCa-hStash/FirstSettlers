# Tree registry and standing-tree rendering

`WorldManager.Trees` exposes the world-scoped `TreeRegistry` owned by `ChunkManager`.
It is available after world initialization and is replaced on world regeneration.
All access and change notifications run on the main thread. Worker placement jobs return
data with IDs; the main thread registers completed results.

## Species datacards and placement spacing

Tree cards live in `Assets/ScriptableObjects/WorldObjects`: MapleTree (red/generic
maple), SugarMapleTree, BirchAspenTree, BeechTree, SpruceTree, WhitePineTree, OakTree,
and WillowTree. `WorldManager.treeSettings` assigns one card per species under
**Tree Datacards**; forest and grassland variants share the same identity card.
The species card also supplies query name, icon and description, even when a
different species' visual prefab is used as a fallback. Existing prefab
`TreeGameplayAuthoring.queryDefinition` links remain a fallback when no species
card is assigned.

Edit **Forest Tree Exclusion Radius Range** or **Grassland Tree Exclusion Radius
Range** on the card to tune spacing. Forest ranges also apply to Taiga and snow
spruce. Grassland ranges apply only to maple, birch/aspen, white pine, oak and
willow; other species' grassland fields are unused. Each tree deterministically
samples its radius between the two endpoints. Values are terrain sample units;
multiply by `worldScale` for world distance. Placement uses the sum of both
objects' radii and retains existing rock/bush overlap handling. Radii do not
follow visual tree scale and do not change grass clearance or canopy colliders.
Zero radii are valid; reversed endpoints are sorted and negative endpoints
clamped to zero.

The current hardcoded spacing ranges have been copied to the cards. World
initialization snapshots their values into plain worker settings shared by
near and distant placement. Restart Play Mode or regenerate the world after
editing a range. Unconfigured worlds and standalone generation retain legacy
defaults in `WorldFeatureGenerationSettings`.

In Play Mode, enable **Show Tree Exclusion Radius Gizmos** on WorldManager under
**Trees, Bushes and Rocks**, with the view's **Gizmos** button enabled. A plain
horizontal wire circle shows the actual applied radius at every registered
standing tree, whether its placement came from nearby or distant generation.
The radius is stored in world units in `TreeInstanceData.exclusionRadiusWorld`
and exposed by `TreeRecord.ExclusionRadiusWorld`; the display does not resample
the card or multiply by the visual tree scale. Toggling the display is immediate.

## Identity

Generated `TreeId` format v1 contains the world seed, logical chunk coordinates,
placement source and candidate cell index. This full tuple is the identity; its
dictionary hash is not an ID. Forest and Taiga share a candidate grid/source.
Grassland and snow have separate sources. Candidate IDs are assigned in the shared
planner before near/distant conversion, not from the accepted placement list index.

IDs survive reordering/filtering, tint/scale/snow changes, terrain seating, visual LOD
changes, pooling and deterministic chunk regeneration. They are scoped to a seeded
world and the existing candidate layout. Changing chunk size, source numbering or
candidate grid layout requires an explicit world generation/save migration. This
first version does not assign identities to player-planted trees.

## Records and registration

`TreeRecord` exposes a placement copy, world position and state, without references
to renderers, colliders or GameObjects. Consumers must use `TreeId`, not a record's
index in the chunk view. `GetChunk` returns a read-only live view; `TryGet` resolves
an ID. `CollectOriginsInRadiusXZ` appends trunk-origin matches to a caller-owned list
and includes every state. It does not test canopy extents or physical collision.

Detailed placements register from `FoliageManager.EnsureTreesGenerated`, including
already generated data. `ChunkFoliageData.TreeRevision` handles in-place regeneration.
Distant successful manifests register during installation. Failed worker jobs do
not register empty world data. Detailed snapshots take precedence over later
distant snapshots, avoiding changes from asynchronous completion order.

Registration validates IDs before changing a chunk, replaces its placement snapshot,
and preserves matching record objects and state. Removed snapshot entries no longer
appear in ID/spatial lookups. Non-default state overrides remain available if that
placement reappears during the world session. Repeated registration of the same
source/revision is a no-op. Increment the supplied revision if changing that source
list in place.

`ChunkChanged` signals a new placement snapshot or changed state. `TrySetState`
stores Standing/Cut/Fallen/Removed data. The renderer invalidates that chunk's visual
cache and rebuilds from standing records on its next update. Cut, fallen and removed
trees disappear from both mesh and billboard rendering. This does not spawn logs or
perform gameplay transitions; a future falling-tree system will own that representation.

## Rendering ownership

`DistantTreeManager` now coordinates both near and far visual representations. Placement
jobs and detailed foliage generation register records; render manifests copy only
the registry's accepted standing records. Late distant results cannot overwrite detailed
records. Render-cache eviction releases GPU slots without removing registry records.
Revisiting a registered chunk reuses its records instead of repeating placement work.

`StandingTreeRenderer` extracts reusable definitions directly from the assigned near
prefabs, without instantiating them. Each MeshRenderer/MeshFilter contributes its child
transform and mesh submeshes with their shared materials. Bark and foliage remain separate
draw batches. Runtime material and mesh copies belong to the renderer, are shared across
instances, and are disposed with it; source prefab assets stay unchanged. Mesh copies
have padded bounds for authored wind. No per-tree renderer, Transform or Update is created.

The existing serialized `gameObjectTreeChunkRingRadius` is now shown as **Mesh Detail
Radius (chunk widths)**. Full mesh visibility extends to `(radius + 1) * chunkWorldSize`;
the next chunk width blends to the distant representation using individual XZ distances.
With SmearScene's radius 3 and 38.4 m chunks, that is full meshes through 153.6 m and
billboards from 192 m. These are visual distances, unrelated to future collider distances.
The billboard mesh may be an impostor; instancing still applies at every representation.

Authored prefab LODGroup levels use screen-relative size and `QualitySettings.lodBias`.
A narrow dither band blends adjacent mesh levels. The final mesh LOD remains available
until the distance handoff rather than culling trees prematurely. Prefabs with no LODGroup
use their single mesh representation. Near batches are flushed per logical chunk in groups
of at most 500 render parts, keeping draw bounds local. Off-screen near trees are submitted
for shadow casting; Unity performs draw and shadow culling. Far rendering retains the
existing CPU/compute compaction, depth ordering, density control and terrain conformity.
Original terrain seating is used nearby, blending far terrain conformity during handoff.

`StandingTreeInstance.hlsl` carries per-instance leaf/bark colors, snow, alpha-shadow policy
and dither intervals through forward and shadow passes. Existing tree shaders consume it
through the shared tree includes; zero `_StandingTreeEnabled` preserves ordinary prefab
materials and capture behavior. New custom tree shaders must support this interface,
including instancing variants and the shadow pass. Near wind amplitudes are preserved.
The far renderer retains its previous wind and shadow policy. A missing billboard asset
keeps instanced meshes as a fallback with the outer distance fade.

`WorldRenderStatsDebugInfo.TreeMeshes` reports submitted near render-part geometry;
`TreeBillboards` reports the distant path. These counters describe submitted geometry,
not measured frame times or exact GPU visibility.

Standing visual prefab colliders are no longer instantiated. `TreeGameplayManager` now
creates pooled trunk and canopy proxies from explicit prefab authoring. Physical trunks
and query canopies have independent activation. AI navigation, falling bodies and
replication are future registry consumers.
Legacy visual GameObject methods remain for compatibility/old validation;
the world runtime no longer calls them for standing trees. FoliageManager retains detailed
placement scheduling and generation budgets, but does not own standing-tree visuals.

## Nearby trunk and canopy gameplay

`ChunkManager` owns a `TreeGameplayManager`, exposed as `WorldManager.TreeGameplay`.
It runs after tree registration/render updates and is disposed before the registry is
cleared. No manager component needs to be added to the scene. Its **Tree Gameplay (Pooled)**
root lives independently of chunk roots and render visibility.

The manager looks up tree origins in nearby registry chunks, then selects individual
standing trees by XZ distance. Scans run every 0.1 s, after one metre of focus movement,
or after a nearby chunk changes. Active proxies are checked every update for state,
placement changes, missing records and release distance. Thus cutting/removing a tree
releases collision on the next manager update, even while the player stands still.

Defaults under **WorldManager > Tree Settings > Gameplay**:

| Setting | Default | SmearScene equivalent |
| --- | --- | --- |
| Physical activation radius | 0.65 chunk widths | 24.96 m |
| Physical release radius | 0.85 chunk widths | 32.64 m |
| Enable canopy queries | true | requires assigned canopy templates |
| Canopy activation radius | 0.65 chunk widths | 24.96 m |
| Canopy release radius | 0.85 chunk widths | 32.64 m |
| Scan interval | 0.1 s | unchanged |
| Activations per frame | 8 | nearest first |
| Activation budget | 0.5 ms | approximate; at least one candidate can progress |
| Maximum pooled proxies | 128 | inactive proxies across all templates |

Release distance is clamped above activation distance to ensure hysteresis. Trees already
active between those distances remain active. Different distances can be tuned independently
of visual LOD or terrain-collider distance. These distances test trunk origins, not canopy
extents. Set canopy activation far enough out to cover player query reach plus the
largest scaled canopy radius and movement/activation margin. The current focus is the
world viewer/player; multiple players, AI interests and
falling-tree simulation areas are future extensions of this consumer.

Proxies are pooled per source prefab. They contain `TreeGameplayProxy`, required empty
transform ancestors, and only explicitly assigned physical/query colliders. They contain
no renderers, MeshFilters, arbitrary prefab behaviours or Rigidbody. A canopy MeshCollider
references its source sharedMesh; it does not instantiate or duplicate that visual mesh.
The registry placement supplies root
position/rotation/scale, and the authoring supplies child transforms and collider dimensions.
Physical trunk bodies/collider children use WorldSolid; material, contact offset and
layer overrides are copied. Duplicate references are
ignored; source collider enabled state is ignored. Runtime collision is solid, not a trigger.
Transform changes are synchronized with physics once per changed update, rather than on
settled frames. [Unity Physics.SyncTransforms documentation](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/physics/synctransforms).

An active proxy identifies its tree with `TreeGameplayProxy.Id`; `TryGetProxy(TreeId, out
proxy)` resolves active bodies. Released bodies clear their ID and are disabled before
pooling. ActiveCount, PooledCount and LastActivationCount expose runtime diagnostics.

### Preparing a tree prefab

1. Open the assigned near-tree prefab. SmearScene currently uses
   `Assets/Prefabs/Spruce_LOD0_v06.prefab` for spruce and its generic fallback.
2. Add **TreeGameplayAuthoring** to the prefab root.
3. Create empty children named **Gameplay / Physical Trunk**. Add a **CapsuleCollider**
   to Physical Trunk, with direction **Y** and **Is Trigger off**. Fit its radius, height
   and center to the solid trunk in prefab-local coordinates. No mesh or Rigidbody is needed.
4. Assign that CapsuleCollider to **Physical Trunk Colliders** on the authoring component.
   Multiple Box/Capsule/Sphere colliders may be assigned for a compound trunk. Physical
   trunk MeshColliders and all unassigned shapes are excluded.
5. Save the prefab and restart Play Mode. The generated scale is already applied by the
   proxy, so do not manually double collider dimensions to match SmearScene's 2x instances.
   Runtime physical trunk colliders are assigned WorldSolid automatically; the source
   collider can remain on Default.

### Preparing a query canopy

1. Under Gameplay, add a separate child named Query Canopy. Fit a low-poly closed cone
   mesh to the spruce foliage (12 sides, about 22–24 triangles, is a suitable start).
2. Assign that imported mesh to a MeshCollider on the child. Enable Convex and Is Trigger,
   and set the child layer to QueryOnly. Remove its MeshRenderer/MeshFilter; the collider
   retains its own mesh reference. No Rigidbody is needed for this view query.
3. Assign the collider to **Query Canopy Colliders** on the root TreeGameplayAuthoring.
   Box/Capsule/Sphere query shapes are also supported. Multiple shapes may share a target.
4. Keep physical and query colliders on different GameObjects because a GameObject has
   only one layer. Duplicate references are ignored; nonconvex/missing meshes, foreign
   colliders and query shapes sharing a physical collider's GameObject are rejected.
5. Save the prefab and restart Play Mode. Runtime copies assigned shape dimensions,
   hierarchy and sharedMesh/cooking options, forces canopy copies to QueryOnly triggers,
   and controls their enabled state independently of the physical trunk. Source enabled
   and trigger states do not control the runtime canopy role; source authoring is unchanged.

Canopy-only templates are supported. A proxy remains allocated while either role is
active and returns to the shared pool when both are off. State changes and missing
records release both roles. The manager scans to the larger enabled activation radius
and checks role hysteresis per active tree each update. Settled proxies do not toggle
colliders or call SyncTransforms.

Readable convex query meshes are pre-baked once per mesh/cooking-options combination
when a source template is first requested. This moves cooking out of repeated role
toggles. Non-readable imported meshes use Unity's normal MeshCollider cooking path;
enable Read/Write on a small collider asset to allow explicit runtime pre-baking. Use
positive, unskewed transforms for cooked-mesh sharing. Existing pooled colliders retain
their mesh reference and cooking options when enabled/disabled, without another explicit
BakeMesh call. See [Unity Physics.BakeMesh](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics.BakeMesh.html).

The manager does not invent trunk dimensions from leaf bounds. Missing/invalid authoring
logs a warning once per requested prefab; those trees continue to render without physical
proxies. Templates are cached for a world session, so restart after editing authoring.

### Testing in SmearScene

Walk into a trunk: the existing CharacterController should stop against it. Leaves and
branches without assigned trunk collision remain passable. In Play Mode, inspect **Tree
Gameplay (Pooled)** with gizmos enabled: nearby tree bodies are active, distant pooled
bodies are inactive, and each active root name contains its variant and stable TreeId.
Walk beyond the release radius, then return, to observe pool reuse. For an easy visual
activation test, temporarily reduce the two gameplay radii while keeping release larger.
The tree's instanced appearance should remain unchanged throughout.

## Player query identity

TreeGameplayProxy is now a QueryTarget provider on the existing pooled gameplay root.
Child physical colliders and assigned QueryOnly shapes resolve that provider through their
ancestors. Its metadata contains a species name, optional icon, Breakable capability,
and the actual registry TreeRecord with its stable TreeId. No extra target GameObject
or per-tree Update is added. The source TreeGameplayAuthoring can override Query Display
Name and Query Icon; those values are copied when the manager caches the source template.

Target snapshots are invalidated when a proxy unbinds/rebinds, becomes inactive, or its
record leaves Standing state. Consumers can use PlayerQuery.Current.HasTarget and
Target.TryGetData<TreeRecord> rather than treating the pooled GameObject as persistent
identity. See [PlayerQuery.md](PlayerQuery.md) for result fields and Play Mode inspection.

## Lifetime and scope

Records survive render visibility changes and distant render
cache eviction. Only nearby gameplay trees get pooled GameObjects; no per-tree Update
methods are added. Registered
chunks are retained for the world session and cleared on world disposal. Memory
therefore grows with explored/generated tree chunks; future bounded residency can
evict unchanged placements while retaining state overrides. No disk save system,
network replication or navigation is added.

## Validation

Run **Tools > Terrain > Validate Tree Registry**, or use Unity batch mode with
`-executeMethod TreeRegistryValidation.RunBatch`. Checks cover ID serialization,
all three candidate sources, scale/order stability, full/sparse planner and real
worker parity, source priority, cached/in-place registration, invalid snapshots,
state retention, reset and negative-coordinate XZ queries.

Run **Tools > Terrain > Validate Standing Tree Instancing**, or batch mode with
`-executeMethod StandingTreeValidation.RunBatch`, for registry-to-renderer filtering,
mesh/billboard mask partition, prefab bark/foliage extraction, multi-submesh child
transforms, both sides of LOD transitions, instanced snow isolation, animated wind,
1001-tree batch boundaries, SmearScene tree forward/shadow compilation and existing
distant compute correctness. The controlled mixed-snow render is written to
`Logs/StandingTreeValidation/mixed-snow.png`. This is correctness validation, not a
performance benchmark or a full gameplay playthrough.

Run **Tools > Terrain > Validate Tree Gameplay**, or batch mode with
`-executeMethod TreeGameplayValidation.RunBatch`, for nearest activation budgets,
hysteresis, pool limits/reuse, negative-coordinate XZ selection, transformed collider
hierarchies, registry placement/state changes, disposal, settled allocation/sync checks
and CharacterController collision/release against an authored trunk. Fixtures are temporary
and do not modify source prefabs or scenes.

Run **Tools > Terrain > Validate Tree Canopy Queries**, or batch mode with
`-executeMethod TreeCanopyValidation.RunBatch`, for shared convex cone meshes, independent
trunk/canopy activation and hysteresis, query identity/solid obstruction, character
pass-through, query-only templates, invalid authoring, pool reuse and settled allocations.
It also runs the existing tree gameplay and player query/target validations. The cone is
an in-memory test fixture, not an asset or a modification to the spruce prefab.
