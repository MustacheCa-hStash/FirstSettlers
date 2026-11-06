# Village state, procedural generation and NPC facilities

October 30, 2026. Proposed architecture for First Settlers; no runtime behavior or save policy is changed by this document.

The village should be a persistent logical part of the active world. Procedural generation produces its initial records. Construction, destruction and NPC activities modify those records. Rendering, collision, navigation and UI consume them through derived indexes and streamed representations.

## Current project fit

- [BuildSession](../Assets/Scripts/Building/BuildSession.cs) already owns data-only BuildPieceRecord and BuildGridFrame objects, with separate derived geometry and spatial indexing. This is the starting point for piece authority.
- BuildPieceRecord retains a definition content ID and grid pose, but current piece/frame IDs are session counters starting at one. They are not regeneration-safe world identities.
- Add/Remove/Damage are natural mutation boundaries. Damage can remove a piece through the session directly, so changes must be recorded at the authoritative mutation layer, not solely in BuildWorld's input-facing methods.
- Each current Add resolves a local neighbourhood and queues only affected render groups. PCG still needs a batch insertion path so overlapping neighbourhoods in a whole village are resolved once per batch.
- [BuildWorld](../Assets/Scripts/Building/BuildWorld.cs) streams physical proxies around one Focus. Detailed NPC simulation needs collision/terrain/navigation coverage independent of the player's proximity.
- [WorldFeaturePlanGenerator](../Assets/Scripts/TerrainGeneration/BiomeGeneration/WorldFeaturePlanGenerator.cs) currently prepares chunk-scale landscape/foliage plans. A village requires a higher-level plan owning the entire settlement across terrain chunks.
- AI Navigation 2.0.12 is installed in Packages/manifest.json. No village/NPC navigation implementation was found in the runtime scripts inspected.
- The [active world policy](TERRAIN_SCULPTING_SESSION_FIXES.md) starts fresh on Play restart and retains terrain edits within a session. Preserve that policy; design storage interfaces now without quietly enabling disk persistence.

The earlier [Village Fixtures notes](../ArtReferences/VillageFixtures/README.md) already outline record-owned facilities, capabilities and reservation cleanup. This plan makes their ownership and lifecycle explicit.

## 1. One authority, several record types

Use ordinary typed C# records/classes and dictionaries first. ScriptableObjects contain reusable definitions and generation templates; runtime records contain mutable instance state. No database server or new ECS dependency is required.

| Record | Canonical responsibilities |
| --- | --- |
| World identity | World ID/seed, schema versions, pinned generator/template versions, simulation time and mutation sequence. |
| VillageRecord | Stable ID, plan origin/territory, explicit membership and claim policy, household IDs, structure/POI references and settlement metadata. |
| StructureRecord | Identity of a meaningful assembly such as a house or grain store; template, construction state, ownership, piece membership and facility references. |
| PieceRecord | Existing build definition ID, durable frame/grid pose, health, lifecycle, optional structure link and revision. Reuse the building record model. |
| PoiRecord | A named semantic place or area such as a grain court, pasture, water source or shared court; geometry/anchors, village links and facility references. A POI need not be a building. |
| FacilityRecord | Available capabilities, equipment/resource/dependency references, interaction slot definitions, operational state and revision. |
| NpcRecord | Home/work assignments, needs, inventory references, current job/travel state and logical position. Reservation leases belong to a scheduler, not a scene component. |

Do not maintain one mutable copy of a piece in WorldState and a second independently editable copy in BuildSession. Expand/refactor the existing piece authority behind a world-owned store: BuildSession's dictionaries can index the same canonical records while continuing to resolve building geometry. Alternatively keep BuildSession as the first piece-store implementation behind the store interface. Health and pose must have one writer and one authoritative owner.

Keep derived fields out of saved DTOs: Unity Mesh/Material references, GameObjects, physical proxies, path corner arrays, capability indexes and resolved fitting geometry can be rebuilt. Definition content IDs and durable poses must resolve without a loaded prefab.

Suggested service boundaries:

- WorldStateStore: canonical records, logical region pages and generation/mutation state.
- VillagePlanner: pure generation of proposed records from immutable terrain/template inputs.
- WorldCommands: validated, idempotent mutation batches and committed change sets.
- VillageIndex: derived village/capability/ownership lookup.
- FacilitySystem: dependency evaluation, interaction slots and operational revisions.
- ReservationService: slot leases, work/resource holds and cancellation.
- NavigationService: route queries, dirty-region scheduling and readiness.
- RepresentationSystem: bindings to optional loaded visuals, colliders and detailed actors.

## 2. Stable identity and deterministic generation

Generated IDs must not depend on GameObject instance IDs, load order, randomized string hashes, float positions or the order of accepted candidates.

Use a stable world namespace and deterministic hashes of authored identities, for example:

- VillageId = StableHash(WorldId, planning-cell key, stable site-slot key).
- StructureId = StableHash(VillageId, stable lot/structure-slot key).
- PieceId = StableHash(StructureId, template-node GUID).
- FacilityId = StableHash(owner entity ID, authored facility-slot GUID).

Explicit stable template-node IDs survive array reorderings. Reject/cull a candidate without renumbering later candidates. Validate hash uniqueness. Runtime-built entities receive IDs in a separate namespace through a world allocator whose state will be persisted with a save; they must never collide with generated IDs.

Persist grid-frame identity and transforms as well as piece identity. Retain a frame while any other piece references it, even if the piece that originally introduced it is removed. Avoid converting framed placements through repeated float round trips. Use region-relative world positions for large-world records where appropriate.

Have a planning region own a whole village even when it crosses many terrain chunks. Neighbouring planning regions use a deterministic candidate/conflict rule and halo sampling so border villages are neither duplicated nor clipped. Chunk arrival should request an existing village plan through a coalesced in-flight/cache entry, not independently roll another settlement.

Generation stages:

1. Choose a site against stable baseline terrain inputs: ground, water, slope, resources and reachable surrounding land.
2. Lay out boundary, access routes, plots, work yards and reserved clearance envelopes.
3. Select structure templates and emit stable piece records, household references, POIs and facilities.
4. Validate footprints, access routes, interaction clearances and cross-chunk ownership before publishing.
5. Apply the current world's overrides, additions and removals.
6. Commit the result as one logical batch, then queue visual/collision/navigation materialization.

The same seed and pinned generation inputs should produce the same baseline regardless of chunk arrival order. A later terrain edit should not cause a demolished house to be rerolled onto a new site. Use original/pinned generation inputs or retain the accepted plan snapshot.

## 3. Loading without losing changes

Separate baseline generation from the mutable overlay.

- Generated baseline: reproducible initial village/structure/piece/facility records.
- Overrides: changed health, ownership, construction, inventories, water/fuel and other mutable state.
- Additions: player/NPC-created entities and new village/structure/POI memberships.
- Tombstones: stable IDs of removed generated entities, plus any surviving explicit assembly state.

A destroyed generated trough must remain absent when its terrain chunk unloads and returns. Dropping its loaded GameObject is never a tombstone; actual destruction is.

Within a session, logical state pages or their in-memory deltas survive representation unload. If logical pages also leave memory, a world-owned state store must retain a lossless snapshot/delta page for them. There must be an explicit retained/archived state, not an accidental return to an untouched procedural baseline.

For future saved games, load the save's world identity and pinned generator versions, reconstruct the baseline and apply the saved overlay before publishing entities or NPC assignments. For a new world/session, start with the fresh baseline according to the current game policy. Use a storage backend interface: in-memory now, disk snapshot/delta storage when the whole-world save system is deliberately introduced.

Version templates, schemas and generation inputs. Do not silently run a different generator against an old save. Either retain the old generator/template interpretation, migrate the accepted plan/IDs, or save the accepted village layout snapshot. Do not regenerate from newly sculpted ground and assume old tombstones will still identify the same objects.

Transient nav caches and interaction leases need not be persisted as authoritative state. On save load, rebuild paths and reconcile job ownership/progress with fresh leases, so completed work cannot be granted twice.

## 4. One mutation pipeline for PCG and gameplay

Use commands such as MaterializeVillage, PlacePiece, RemovePiece, DamagePiece, CompleteBlueprint, RegisterFacility, MoveFacility, AssignStructureToVillage and TransferInventory.

Each committed command/batch has a world sequence, stable entity IDs and expected revisions where races matter. Keep event notifications separate from authoritative storage; losing or rebuilding an index should not lose world state.

A transaction should:

1. Validate IDs, ownership, inputs and dependency rules against a consistent state.
2. Update the canonical records and piece store once.
3. Record the resulting overlay/tombstone or dirty state page.
4. Update immediate facility availability and invalidate affected reservations/jobs.
5. Produce a committed change set containing IDs, old/new bounds, revisions and change categories.
6. Publish it to indexes, building resolution, UI, representation and navigation schedulers.

Queue change sets until the transaction is fully committed; do not let event callbacks observe half a building. Derived expensive work may be asynchronous, with explicit pending/readiness state. Pure damage that leaves geometry and capabilities unchanged does not require a navigation rebuild.

Dependency indexes such as piece ID -> affected structures/facilities avoid rescanning every POI on every change. Frame/pose changes propagate to dependent interaction anchors and affected navigation bounds. Include nearby pieces whose resolved geometry changed through fitting, not only the directly added/removed piece.

Example: destroy a quern's required runner stone. The piece is removed/tombstoned, its GrindGrain facility becomes unavailable, outstanding work/slot leases are cancelled, waiting workers receive an invalidation, and the changed geometric area is queued for navigation if necessary. The entire house and village do not disappear.

Keep dependency rules separate from structural-support simulation. The game currently permits free placement. A required quern part is a functional dependency; it does not introduce a new load-bearing rule for all buildings.

## 5. Register POIs and facilities explicitly

A structure is an assembly; a POI is a meaningful place; a facility provides an action. They are related but not interchangeable.

Examples:

| Place or object | Authored capability examples |
| --- | --- |
| Household hearth | Cook, WarmUp when its actual runtime systems exist |
| Complete hand quern | GrindGrain |
| Raised grain store/container | StoreGrain or WithdrawFood according to access/inventory state |
| Water source/collection facility | DrawWater |
| Workstation with tools | RepairTools |
| Bench with seat definitions | Sit or Rest |
| Livestock trough with supplied water | AnimalDrink |
| Shared court | Gather/Meet interaction areas |

The installed hollow sandstone trough is currently a modeled object. Its shape alone does not create water or a drink capability. Capabilities are attached through authored facility definitions and required runtime components/state.

PCG templates explicitly create facility records and link their relevant pieces. Player-built standalone authored fixtures can register automatically on placement/completion. Multi-piece player buildings should initially use an explicit blueprint or player designation, such as a house plot or grain store, rather than trying to infer a house from arbitrary nearby walls.

Village membership should follow an explicit claim/construction context and deterministic assignment rule, with an override for player designation. Proximity to the village centre is a useful suggestion, not authority; it should not steal a neighbouring village's building or continually reassign objects when a boundary moves. A shared natural source can have one authoritative facility with access from multiple village indexes.

Maintain by-ID dictionaries and short per-capability/per-village candidate lists. Reuse the existing piece spatial index for geometric queries. Add new spatial structures only when measured query counts require them.

## 6. NPC interaction and reservations

Store authored interaction slots as local offsets/areas relative to their owning entity, including facing, agent restrictions, use radius, required clearance and capacity. Store entrance and approach anchors for structures. A building's centre is not necessarily a reachable work position.

An NPC routine should:

1. Request a capability, for example GrindGrain.
2. Filter candidates by alive/construction/operational state, access rights, resources and agent compatibility.
3. Rank a short candidate list using estimated route cost and queue/occupancy.
4. Request a real route to a valid interaction approach for the correct agent profile.
5. Reserve the selected slot with an expiring token bound to entity/slot revision.
6. Travel, revalidate at arrival, then commit the actual operation/resource transfer atomically.

A cheap coarse route estimate is only a ranking hint. Reachability must be validated; nearest straight-line distance and nearest nav sample are insufficient, especially for stacked floors. Bind anchors to their intended elevation/surface.

Do not conflate three states:

- Logically usable: equipment, ownership, inputs and construction requirements are satisfied.
- Reachable by this actor: a complete route exists for its agent type and permissions.
- Ready for detailed execution: local terrain, navigation and physical representation are available.

A blocked route may be specific to one actor and does not globally destroy a water facility. Unloaded/pending navigation is not proof of permanent unreachability.

Release/renew leases on cancellation, death, removal, path failure and timeout. Use job/operation IDs so interrupted or retried work grants output once. Recheck resource availability at the commit boundary; a slot reservation is not proof that ingredients are still present. Implement queues/fairness where contention actually occurs.

Use change-driven invalidation and bounded retry/backoff. Villagers should not search the entire village or request a new path every frame.

## 7. Navigation from logical geometry

Use the installed Unity AI Navigation package as the initial local pathfinding backend behind NavigationService. Unity documents [runtime navigation support](https://docs.unity.com/en-us/engine/6000.3/manual/packages-list/packages-all/pack-safe/com-unity-ai-navigation) and [incremental asynchronous NavMeshData updates](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/ai/navmeshbuilder/updatenavmeshdataasync).

Build navigation sources from authoritative terrain snapshots and resolved building occupancy/walk surfaces, not from whichever player-near colliders happen to be instantiated. Preserve doorway holes, floor/storey separation, stair walking ramps and relevant fences/solid furnishings. Treat large logical structure bounds as a broad phase only.

Start with bounded local navigation regions for active villages and adjoining approaches. Region sizing and source density require a measured prototype; avoid assuming terrain render chunks are automatically the right nav partitions. Maintain portal/connection records across region borders. A later higher-level graph can connect village entrances and regional routes.

Construction, destruction, movement, doors/gates and terrain edits dirty the union of old/new affected geometry, with a clearance halo and neighbouring region edges. Coalesce edits and limit concurrent rebuilds through the shared world-work admission/budget system. Rebuild each region from its complete current source snapshot in stable source order. Unity's API requires a complete source list for the NavMeshData being updated, not just changed pieces.

Use revisioned, leased geometry snapshots. A source mesh/terrain buffer must remain alive for an outstanding async build. Tag work with world epoch and terrain/build revisions. Build into staging data and publish only if those inputs still match; discard/requeue stale results. Do not mutate live navigation and then pretend a stale result can be discarded after agents have used it.

Until a changed area is ready, invalidate affected corridors/links and prevent detailed agents from entering unavailable or now-invalid geometry. A removed floor or newly placed wall must affect movement immediately, even while a nav job is pending. Use conservative dirty-area gates and current collision/movement readiness; unaffected routes can continue.

Obstacle carving can help represent suitable temporary blockers, but it cuts existing walk space. It cannot create a newly constructed elevated floor or stair. Structural walk surfaces need their own updated navigation sources. See Unity's [obstacle reference](https://docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/manual/NavMeshObstacle.html).

Ordinary NPC movement should not assume currently static door/gate leaves can open. A traversal link requiring an action is only usable when that action exists and the actor is permitted to perform it.

## 8. Streaming and simulation levels

Logical existence, detailed simulation and visible representation are independent lifetimes.

| Layer | What survives representation unload |
| --- | --- |
| Village/structure/POI/facility records | Identity, membership, state, inventory/dependencies and removal history |
| NPC logical state | Needs, assignments, job progress and coarse travel |
| Derived indexes | Rebuildable from records; may remain for active logical regions |
| Local nav/collision/visuals | May unload when no detailed actor needs them; readiness must be explicit |

Near NPCs use detailed navigation/animation and physical interactions. Distant villages can use bounded coarse activity/travel updates, preserving the same jobs and inventories. Materialization reconciles progress and obtains fresh valid interaction leases; it must not spawn duplicate NPCs, reset needs or grant products again.

Detailed actors need terrain/collision/nav residency pins around themselves and their active corridors. Extend the existing single-focus building proxy policy to multiple bounded focus/pin requests. If the coverage budget cannot admit an actor, keep it in coarse simulation or wait safely before entering the region. Do not allow a visually distant villager to walk through a house because only the player's 32 m collider bubble exists.

## 9. Implementation sequence and acceptance gates

1. **Stable IDs, authoritative records and committed changes.** Extend the existing building record model, keep one writer, retain frames correctly, add batch insertion and exercise add/damage/remove notifications.
2. **Deterministic VillagePlanner and in-memory overlay.** Generate one small village spanning chunk boundaries; preserve it through repeated unload/reload, reversed chunk order and tombstones.
3. **POI/facility indexes and explicit registration.** Implement a few examples: water collection, rest seat, storage and a test work fixture. Verify construction completion, required-equipment destruction, resource depletion and rights changes.
4. **Navigation and one detailed villager routine.** Walk from a home anchor to a reserved water/work slot; handle a wall placed in the route, a removed floor, blocked gate, deleted target and pending nav region.
5. **Multiple villagers and simulation streaming.** Add contention, cancellation, multi-actor residency and near/far handoff. Benchmark before enlarging the population or simulation radius.
6. **Whole-world save integration.** Add a deliberate disk backend, snapshot/delta compaction and migration once the game's overall save policy is established. Keep normal Play restarts fresh until then.

Core automated acceptance cases:

- Same baseline IDs/layout across generation order, region overlap and repeat hydration; no duplicate village/house/NPC records.
- Generated destruction survives chunk unload/return; runtime additions, membership, health, inventories and jobs remain.
- Every facility reference resolves or has a defined missing dependency state; player-built facilities enter indexes once.
- Removal invalidates reservations/activities before representation teardown; pooling and OnDisable do not count as destruction.
- Frame-owner removal does not move surviving framed pieces.
- Blocked/destroyed route and pending terrain/nav produce bounded replanning, not wall traversal or endless retries.
- Different storeys and agent types do not share invalid approach points.
- Near/far handoff and failed/retried work produce no duplicate inventory transfer.
- Async jobs after world reset/unload cannot publish stale state or access disposed geometry.
- No scene-wide per-frame searches or one full nav rebuild per placed construction piece.

This proposal changes only documentation. It does not enable persistence, generate villages, add POI gameplay or implement NPC pathfinding.

