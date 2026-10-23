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
colliders and stair walking hulls remain. `Supported`, `RecomputeSupport`, and
`BuildSupportState` are compatibility APIs for authoring/tests and report every
piece as eligible; there is no load solver behind them. The removal HUD and
catalog descriptions no longer promise support requirements or collapse.

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
