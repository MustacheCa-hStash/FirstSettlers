# Building foliage and free placement

Building pieces own exclusions derived from their resolved boxes and convex solids.
The registry belongs to `BuildSession`; it follows placement, removal, destruction,
and changes to fitted geometry. It is independent of foliage residency. Unchanged
cached convex geometry does not invalidate foliage during unrelated building edits.

## CPU work and bounds

`BuildFoliageExclusion` indexes owners and foliage consumers in 32 m XZ cells.
Changes enqueue affected cells. Notification fan-out, consumer preparation,
intersection work, and publication all advance incrementally. A second edit
restarts notification for its cells and invalidates old worker results.

- Main-thread target: **0.75 ms per frame**, checked between operations.
- At most **64 scheduler operations** and **4 publications per frame**.
- Each preparation/publication slice visits at most **128 instances**.
- At most **2 managed worker tasks**; each packet tests at most **128 plants
  against 16 numeric convex shapes**.
- Normal gameplay never waits for an unfinished worker, including cancellation
  and disposal. Retired jobs retain their admission slot until completion.
- A settled consumer performs no repeated building intersection work. Empty spatial cells are released when their owners and resident consumers leave.

The time budget is soft: one bounded operation, a driver call, OS scheduling, or
garbage collection can exceed it. Larger edits take more frames rather than
increasing per-frame work. This is not a guarantee about total game frame time.

Checks clip a vertical plant segment against the actual local convex planes,
including a 3.5 cm contact margin. This retains vegetation below raised floors
or above underground construction when the plant does not intersect a solid.
Grass and small plants use mesh-derived heights; forest scatter also uses conservative horizontal bounds across its LODs. This is not triangle-by-triangle testing
of animated blades or individual fern fronds.

## Representation and restoration

- Grass retains every candidate and its original density rank. CPU results set
  the displayed rank to 2 for hidden instances, which the existing GPU culling
  and CPU fallback reject. Restoration reinstates the exact original rank.
  GPU updates contain at most 128 existing 80-byte source records (10 KiB).
- Flowers, clover, dandelions, and water plants retain their original matrices
  and attributes. Compaction proceeds in slices, then swaps the completed batch.
  Publishing another plant type does not discard existing exclusion results.
- Bushes retain their objects and harvesting state. Exclusion toggles their
  activity and colliders; demolition restores them. Building placement permits
  overlapping bushes so clearing can occur after commit.
- Ferns and leaf scatter retain their procedural instances. Cached flags drive
  CPU rendering and bounded updates to resident GPU density ranks across LODs.
- Terrain excavation suppression remains independent. Demolishing a building
  cannot restore vegetation whose terrain support was excavated.

Existing and newly streamed representations join the same queue. Clearing is
eventual over bounded updates; a newly arrived representation can briefly appear
before its result publishes. Preview movement does not clear vegetation.
Trees and rocks retain their existing gameplay removal/collision policies.

No separate foliage-deletion save is written. Session building records define
the exclusions, and generated candidates define what can return. Current world
and building-session persistence behavior is unchanged.

## Free building

Terrain footing, structural support, full-bearing requirements, furnishing mount
support, and timed collapse no longer determine placement or lifetime. Roofs,
stairs, walls, furnishings, and foundations may float. Removing a piece leaves
other pieces in place. Explicit removal and damage still work.

Snapping, nudging, resolved joint fitting, duplicate prevention, terrain collision
readiness, and independent player/world blockers remain. Physical building
colliders and stair walking hulls remain. The retired support/grounding APIs and
bearing/mount metadata were removed on November 6, 2026. Authoring checks now
assert actual session membership, fitted geometry and collision rather than an
always-true support flag. The removal HUD and catalog descriptions use free-placement rules.

Health-only mutations advance `BuildSession.Revision` without changing
`GeometryRevision`, so they retain render batches and cached preview fitting.
Collision revisions change only when a piece's resolved solids change; unrelated
construction does not rebind an unchanged proxy. Preview fitting reuses its
candidate, neighbours and visual changes at an unchanged pose/revision, while
every validation still checks current terrain readiness and external blockers.
Add/remove fitting resolves two spatial neighbour rings, using committed floor
states and scratch joint ownership at the boundary. It updates connection edges
on both ends and republishes foliage/collision only when solids change. Visual
notifications coalesce per renderer: only changed pieces and their old/new
material/mesh/cell groups rebuild, reusing existing 500-instance batch storage.
Preview overlays own separate groups and filter only affected committed groups.
Village batch insertion remains future work.

## Validation

Run `Tools/ValidateBuildingFoliage.ps1` for native Unity CPU/GPU coverage,
restoration, overlapping owners, rotated convex geometry, height separation,
streaming arrival, terrain composition, worker retirement, rapid notification
edits, free placement, and a 131,072-instance GPU-upload stress workload. It also
runs the existing four-channel grass-culling regression.

Run `Tools/ValidateBuildingFoliage.ps1 -IntersectionChecks` for the real Play Mode
building geometry, retained-collider, player-blocker, duplicate, and rendering
regressions. Historical support/footing assertions in the building validation
fixtures now expect free placement; geometry and collision assertions remain.

Final native Direct3D11 upload stress on the development machine measured p99 **0.209 ms**
and maximum **0.474 ms** per scheduler update in the recorded run. These are
isolated foliage-update measurements, not whole-game FPS measurements. Earlier native runs measured maxima between 0.540 and 1.320 ms. The final suite passed 269,509 foliage checks, the existing grass-culling regression, and 937 Play Mode intersection/render checks. The migrated modular-building suite passed 28,311 checks, including 192 real-motor stair walks. Profiler scopes are
`FS.Building.Foliage.BudgetedUpdate` and `FS.Building.Foliage.Intersections`.

### November 6 cleanup verification

Removed the retired support service, flags, placement arguments and bearing/mount
metadata, including 283 obsolete fields across 105 definition assets. Asset
comparison confirmed that geometry, materials, identities and other serialized
references were preserved. The furniture generator no longer exports unused
support samples; its six geometry/export definitions are unchanged.

Runtime, editor and player compilation passed with existing obsolete-API warnings.
The isolated native Unity suites passed **365,707 checks**:

| Suite | Checks |
| --- | ---: |
| Interaction, revision and preview caching | 8,360 |
| Intersection, retained collision and rendering | 937 |
| Modular wood, including 192 motor stair traversals | 28,311 |
| F01–F06 furniture and catalog UI at four resolutions | 9,747 |
| Limestone access, including 160 motor stair traversals | 4,049 |
| Stone, including 16 gate traversals | 3,613 |
| Building foliage, CPU/GPU and 131,072-instance stress | 269,506 |
| Timber/infill, roof joins and floating canopy | 41,184 |

The new interaction cases cover nonfatal versus lethal damage, unchanged collision
after unrelated edits, refreshed collision after fitting changes, cached preview
reuse, live moving blockers and changing terrain readiness. Older support-only
assertions now check actual session membership and retained geometry. Furniture
validation is scoped to F01–F06; catalog category checks accept later kits and
respect the currently hidden placement HUD. Logs are under
`.utmp/building-cleanup/*-native.log` and `compile-final.log`.

These are isolated correctness checks. The saved world scene was not opened or
changed, and no whole-game performance improvement is claimed. Planned building
sockets, bounds offsets, storey-rise overrides, roof attachments and village/save
architecture remain in place.

### Local building mutation and render groups

`BuildSession` now resolves only the edited piece's two spatial neighbour rings.
Boundary contacts use scratch states for complete corner-cover ownership, and
unaffected boundary floors supply committed solids. Both ends of connection
edges update together. Collision/foliage publications retain their existing
solid-change checks; local-flame geometry publishes only changed visuals.

`BuildRenderer` coalesces changed IDs independently per renderer, removes their
old group membership, and rebuilds only touched material/mesh/cell groups.
Existing 500-instance arrays survive edits, group bounds shrink on removal,
and empty groups retire. Preview overlays have their own collection; this also
fixes preview parts entering committed batches. Renderer disposal releases the
session subscription.

Prepare the isolated fixture with
`Tools/ValidateBuildingPrototype.ps1 -PrepareOnly -UiAssets -Workspace building-incremental`,
then run Unity with `-batchmode -force-d3d11 -projectPath .utmp/building-incremental/unity-project -executeMethod BuildIncrementalValidation.RunBatch`.
The validator compares each mutation with full resolution, including imported
mesh positions, normals, UVs, tangents, collision hulls, joints, attachments,
connection edges and local-flame parts. It exercises connected construction,
1,024 distant pieces, group reuse, split batches, preview removal/restoration,
delayed synchronization and multiple renderer lifetimes.

Native Unity Direct3D11 verification passed **682,083 checks across 11 suites**:

| Suite | Checks |
| --- | ---: |
| Incremental/full-resolution parity and render lifecycle | 310,064 |
| Existing interaction, intersection, wood, furniture, access, stone, infill and foliage suites | 365,797 |
| Local-flame rendering, cache and shadow regression | 70 |
| Sandstone extension, including 112 stair traversals and 32 reversible pocket cases | 6,152 |

An edit in 64 connected wall/floor records resolved **9 records**; a local addition
with 1,024 detached distant pieces resolved **3 records**, the same as before
adding those distant pieces. The renderer fixture rebuilt one group for a
one-piece edit while preserving both existing groups' batch objects. Geometry,
UVs, normals, tangents, collision, contacts, covers and flame registration matched
full resolution through additions, removals and lethal damage.

Runtime, editor and player compilation passed with existing obsolete-API warnings.
Logs are `.utmp/building-incremental/*-native.log` (the final incremental run is
`incremental-final-native.log`) and `compile-final.log`. The foliage stress run
measured p95 0.345 ms, p99 0.584 ms and maximum 4.238 ms per update; its 0.75 ms
budget is not a hard wall-clock cap. These isolated correctness/scaling checks
do not establish whole-game FPS. No saved scene or planned village/save framework
was changed. Future batch insertion and distant rendering remain separate work.
