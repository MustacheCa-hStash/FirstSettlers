# P6: building in excavated terrain

Implemented October 16, 2026 following the user's building/support review. This checkpoint supersedes the original A14 proposal to remove grounding after excavation. It does not begin P7 or implement lighting changes.

## Approved behavior

| Case | Result |
| --- | --- |
| Initial foundation, or a definition already allowed to ground on terrain | Must find valid footing on current published terrain, or obtain ordinary structural support. Existing shallow seating and per-section/gate-foot rules remain. Burial alone does not establish footing. |
| Supported wall/floor/roof/post intersecting terrain | Allowed, including complete burial. Existing snaps are suggestions; manual nudges/rotation remain. |
| Stair under a cave ceiling, or with a buried endpoint/body | Allowed when its structural endpoint meets a supported building/flight. Ground alone never grounds a stair. |
| Fully buried build piece | Entire mesh and physical collider remain. Placement does not remove terrain, clip the piece, or expose an X-ray selection mode. |
| Dig beneath an existing grounded piece | Its stored grounding and dependent structural support remain. The anchor can float and can support subsequent extensions. |
| Terrain collision streams out | No effect on existing grounding or stair connections. |
| New placement while edited collision is stale/unavailable | Rejected with `Waiting for current terrain collision`; commit repeats validation. |
| Remove/destroy a building anchor | Existing support graph, alternate support paths, full-bearing requirements and three-second unsupported grace/collapse still apply. |
| Duplicate footprint, unsupported piece, player/prop obstruction or reach failure | Existing rejection remains. Build-to-build overlap/headroom remain advisory under the existing permissive policy. |

Terrain remains solid for the character. Permission to place a buried stair does not make that stair traversable until sufficient rock is excavated. Trees, rocks and other props are not terrain merely because they stand on it. Hidden-piece access/removal, changing stored anchors, saves and building simulation are separate future work.

## Implementation boundaries

- `BuildStairAttachment` now uses sockets/landing geometry only. Removed both the Unity Terrain height comparison and overhead casts against marked terrain. The latter classified a clear cave as buried whenever its roof was above the stair; it also made structural connection rebuilding depend on streamed collider availability.
- `BuildWorld` skips recognized terrain in external overlap rejection for every build type. It retains the complete existing resolved geometry/collider; ordinary build-to-build joint fitting remains unchanged.
- `WorldManager` binds the local player's `BuildWorld.TerrainGuard` to the current sculpt runtime, rebinds it during world regeneration and clears its own binding on teardown. The guard checks readiness/coverage, not solid occupancy. No standalone density occupancy query was added for a rule that now permits occupancy.
- Every preview/commit still recomputes eligible initial footing from current collision. Structural preview caches include that freshly computed grounding result; they cannot retain a previous `Grounded=true` after excavation removes the candidate's footing. Terrain-independent stair contacts remove the need to invalidate structural geometry solely because terrain changed.
- `BuildPieceRecord.Grounded` remains placement-time authority. Excavation does not scan/re-ground buildings, pin terrain for their support, or start collapse timers. Structural edits retain the existing graph/bearing solver and grace behavior.

This adds a sparse terrain-readiness check during placement and removes terrain burial queries from stair validation/graph rebuilding. It adds no continuous load simulation or per-frame re-grounding of settlements. Existing structural rebuild and overlap costs still scale with building complexity; no live settlement performance claim is made.

## Validation

All checks ran in isolated Unity projects against Unity 6000.4.8f1; no saved scene, lighting asset or authored building asset was changed.

- Runtime, editor and player C# compilation passed. Existing obsolete-API warnings remain unrelated to this change.
- `Tools/ValidateTerrainSculpting.ps1 -WorldChecks`: **295,357 P6 consumer checks**, **8,565 P1 checks**, and shared height-sampling regressions passed. The new integration fixture carves a retained-roof chamber using the real asynchronous terrain/collider pipeline. It validates cave-floor footing, fully buried supported walls, full physical geometry, duplicate/unsupported rejection, unpublished-collision guarding, changed footing at the same cached preview pose, newly lowered footing, retained anchors after excavation/structural rebuild/streaming, and structural destruction/grace/collapse. Existing foliage checks also pass.
- `Tools/ValidateBuildingPrototype.ps1 -InstalledAssets`: **33,551 checks** and **192 actual CharacterMotor stair traversals** passed against installed wood assets. Stair regressions cover all eight headings, W21/W22 mixed downward continuations, retained cave ceilings, buried toes/entire flights, marked terrain and Unity TerrainCollider, handrails, unchanged ground-start rejection, and prop obstruction.
- `Tools/ValidateBuildingPrototype.ps1 -InstalledAssets -StoneChecks`: **145,246 checks**, including **16 actual gate traversals**, terrain-capable wall/gate footing, complete paving bearing and removal, all headings and installed asset geometry passed.

Logs: `.utmp/terrain-sculpting/validation.log`, `.utmp/building-prototype/modular-wood.log`, `.utmp/building-prototype/stone-kit.log`. Check counts include fixture work and are correctness results, not frame-rate measurements.

Suggested playtest: place a foundation on a carved cave floor; attach/nudge stairs and a wall into rock; dig away the foundation's footing; verify the construction remains; then remove the structural foundation and verify dependent pieces lose support normally. Current controls/reach and the session-only terrain lifecycle remain in effect.

## Lighting follow-up

The user subsequently authorized the cached field at 2 m spacing. See [the implemented sky-access system and live brightness controls](TERRAIN_SKY_ACCESS.md). The following records the earlier design discussion; point-light work remains deferred.

### Original lighting proposal

No lighting implementation or ambient setting changes were made in this checkpoint. The user prefers a cached sky-access field later, with ordinary local lights considered afterward. Current terrain shading takes the larger of sky ambient and a minimum ambient floor, so reducing only sky ambient would leave caves bright. The eventual change must apply cave attenuation to that floor as well, and make terrain/building/prop shaders agree.

### What the field would represent

Store a continuous estimate of exposure to the outdoor sky at coarse points in nearby air space: open ground approaches 1, deep enclosed cave approaches 0, and entrances/shafts occupy intermediate values. This is illumination data, independent of the 0.3 m authoritative density lattice. A starting experiment would use **2 m probe spacing**, with closer sampling or wall-aware reconstruction near entrances and thin roofs. These are provisional lighting choices, not new approved terrain settings.

Several directional visibility queries through the committed density field would estimate each probe's sky exposure. They run when an affected area is prepared or its geometry changes, on a bounded background queue. Rendering samples the published field at the shaded world position; it does not trace a new set of visibility rays for every pixel/frame. No hardware ray tracing is needed. The density representation supplies underground occlusion even when a distant terrain collider is not present; visibility queries must distinguish actual sky escape from an unavailable region or a trace-distance limit.

An artistic ambient model could be:

`ambient = outdoorSkyAmbient * skyAccess + caveAmbient * (1 - skyAccess)`

The cave term can be very low to make artificial lighting valuable. It remains an adjustable art/gameplay decision. Apply this to environmental ambient; direct sun with terrain shadows and eventual torch/lantern light are handled separately. In particular, a dark cave must not multiply away a nearby torch's direct contribution. A spatial field lets a dark cave and bright visible entrance coexist in the same camera view. A camera-wide brightness multiplier would not supply this behavior.

### Storage, work and updates

- Allocate sparse local air/boundary regions, not a dense underground world. At 2 m spacing a 64 m cube has 32 cubed probes: **32 KiB for one byte per probe**, before border samples, page metadata, validity, CPU/GPU copies and staging. Actual runtime residency and GPU format must be measured; this is only raw payload arithmetic.
- The initial visibility calculation and geometry invalidation are likely more significant than that payload. Limit work per frame, prioritize the player/entrance, publish complete versioned regions and blend updates to avoid flashes. Geometry revisions must reject obsolete results.
- An opening can change exposure farther into a chamber than its brush bounds. Invalidation must account for visibility dependencies or conservatively refresh the connected nearby region; updating just probes inside the brush is insufficient.
- Changing time of day changes the outdoor ambient color/intensity, not cave topology. Reuse sky access and multiply by the new sky lighting without rebuilding geometry visibility solely for day/night.
- Prevent interpolation through stone. A coarse grid can otherwise blend a bright exterior probe into a cave across a thin wall. Occupancy-aware interpolation, denser boundary probes and an adjustable entrance falloff need deliberate tests.
- Start with terrain as the occluder. If constructed roofs must also darken ambient, add their logical occluders and build/remove invalidation explicitly; a terrain-only field cannot infer a wooden roof.

### What remains separate

Sky access alone is not colored light bounce, smoke/fog lighting or torch illumination. Direct-sky visibility can also make a bend darker than a physically bounced-light solution would; a small cave ambient term or later limited propagation can provide art control. This approximation fits the desired dark caves without committing to full real-time global illumination.

Later torch/lantern work would add the terrain shader's local-light contribution and coordinate it with existing building shading. Light range, active light counts and selectively enabled shadows need their own measured budget; they are not part of this implementation. Cached sky access should remain independent so adding those lights later does not require rebuilding cave visibility every time a torch moves or flickers.
