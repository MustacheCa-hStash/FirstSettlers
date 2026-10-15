# Sparse density terrain sculpting implementation plan

Status: proposed implementation contract, awaiting user review. Written October 15, 2026. No runtime implementation is included in this change.

## Outcome and scope

Introduce persistent, three-dimensional excavation with smooth/faceted reconstructed terrain, including player-dug tunnels and retained ceilings. The initial world remains the existing generated landscape with solid stone beneath it. No procedural underground cavities, ore placement, fluid simulation, or AI navigation implementation is part of this first delivery.

The authoritative editable terrain is a procedural baseline plus sparse density overrides. Existing heightfield meshes remain an optimized representation of unchanged terrain. Only nearby representations reflect edits; distant terrain generation, macro meshes, maps, and silhouette data continue to show the baseline.

This document is the reference for subsequent execution. Complete phases in order, recording measured results and material deviations here. Do not treat proposed performance targets as measured results. Implementation starts only after the user has reviewed this plan's assumptions.

## Assumptions for user review

| ID | Decision | Status / implication |
| --- | --- | --- |
| A01 | Excavation only. | **Confirmed by user.** Raising, filling, flattening, and general smoothing tools are deferred. |
| A02 | Development sculpt mode with adjustable brushes. | **Confirmed by user.** No inventory item, durability, stamina, loot, or crafting integration. |
| A03 | Existing landscape and solid stone below it. | **User scope.** Manually excavated cavities are supported; generated caves and ore are deferred. |
| A04 | Density spacing is **0.3 m in world space**, on all three axes. | Proposed. Matches SmearScene's current horizontal terrain spacing. Gives roughly 3.3 sampling intervals across a 1 m cut and avoids a second horizontal lattice. This deliberately selects more detail than the earlier 0.5 m example. |
| A05 | A density brick owns **16 x 16 x 16 samples**; a mesh brick spans **16 cells = 4.8 m** per side. | Proposed. Shared boundary samples are read from neighbouring owners, not independently persisted. A source terrain chunk is 128 cells / 38.4 m, or 8 bricks across. |
| A06 | One signed 16-bit density per sample, clamped to **[-1.2, +1.2] m**. | Proposed. Negative is stone; nonnegative is air. Values approximate signed distance; they are not material percentages. Core detailed brick payload is 8 KiB. |
| A07 | Full visible excavation through **192 m horizontal distance** and **64 m vertical separation**; render-only horizontal transition **192-256 m** and vertical transition **64-96 m**. | **Revised proposal after distance discussion; not yet confirmed.** Horizontal distance is to the local player's XZ focus. This replaces the initial conservative 64-96 m horizontal range. The independent vertical transition closes distant shaft boundaries instead of exposing an open streaming cut. Data and near collision never use either visual fade. Large edits/tunnel entrances can close or disappear at distance as an intentional first-version limitation. |
| A08 | Begin preparing relevant edited representations through **288 m horizontally / 112 m vertically**; retire eligible caches outside **320 m horizontally or 128 m vertically**, subject to pins and save safety. | Revised proposal. Use tile/brick AABB distance and account for the coverage anchor margin. Only relevant sparse edited data, replacement exterior skin, and dependencies are prepared; this does not load a dense cylinder of underground stone. |
| A09 | Edited collision is required within **32 m in 3D** of the player, requested through **48 m**, retained through **64 m**. | Proposed. Include overlapping support-critical geometry and pin geometry occupied by the player. Existing ordinary terrain collider coverage remains in place. Teleports must wait for destination edited collision. |
| A10 | Sphere and axis-aligned box excavation brushes. | Proposed. Default sphere radius 0.5 m (1 m diameter), adjustable radius/box half-extent 0.5-2 m; maximum reach 6 m. Shape is continuous, not grid-snapped. A box brush is an approximate box-shaped cut at this resolution, not a promise of sharp corners or exact removed volume. |
| A11 | First delivery supports **dry excavation only**. | **Confirmed by user.** Reject a brush whose removal bounds extend below `GlobalWaterHeight + 0.3 m`. SmearScene currently has global water Y = 14.4 m. No flooding, draining, or water mesh updates. This restriction can be relaxed separately. |
| A12 | Persist terrain edits in a dedicated local world sidecar. | Proposed. Reloading/restarting restores edits. No general building/inventory save framework is implied. World/profile fingerprint mismatch disables that edit store rather than applying incompatible data. |
| A13 | Grass and small ground decoration are suppressed over edited surface footprints, including a 0.6 m margin. | **Confirmed by user**, including acceptance of floating larger props. No new grass is generated on excavation surfaces. Existing trees and large rocks retain their existing registry/placement states; undermining does not add falling physics or automatically destroy them. They may remain visibly unsupported in this development version. |
| A14 | Terrain changes can remove building support. | **Confirmed by user.** Revalidate affected logical grounding after an edit commits, then use the existing support/collapse rules and grace time. Streaming/unloading alone must never remove grounding. |
| A15 | Preserve existing landscape appearance outside changed patches; newly exposed cut surfaces use a stone material. | **Confirmed by user.** Use world-space texturing for walls/ceilings. Add local terrain shadow casting needed for ceilings; no cave GI, ambient occlusion bake, or guaranteed dark interiors in this delivery. |
| A16 | Local single-player/editor prototype. | Proposed. Replication, multiplayer authority, concurrent remote edits, consoles, and production save migration are deferred. Stable IDs, revisions, and a versioned file format leave room for them. |
| A17 | Do not modify terrain generation settings or existing world content to accommodate the prototype. | Proposed. New density profile is immutable for a saved world. Feature is opt-in and enabled in SmearScene only during the implementation/integration phase. |

Confirmed choices: A01/A02 during plan preparation and A11/A13/A14/A15 during subsequent review. All other proposed choices remain reviewable before code changes. The larger horizontal edit range is a revised recommendation, not an inference that all assumptions or implementation have been approved.

## Current repository integration facts

- Unity version: `6000.4.8f1` (`ProjectSettings/ProjectVersion.txt`). AI Navigation 2.0.12 is installed, but no custom navigation integration was found under `Assets/Scripts`.
- `WorldManager` constructs `WorldConfiguration` and `ChunkManager`. SmearScene uses `chunkSize = 128`, `worldScale = 0.3`, `meshHeightMultiplier = 200`, `colliderDistance = 5`, and `farTerrainStartRing = 8`.
- Chunk width is **128 x 0.3 = 38.4 world metres**, not 128 m. The initial 64 m edit radius was about 1.67 chunk widths; the revised 192 m full-detail radius is five chunk widths. Ranges are radii from the coverage anchor, not total widths. The new full-detail disk has nine times the area of the original disk, but actual additional work depends on edited footprints and cache policy; untouched terrain does not acquire a dense volume. Retain memory/worker admission caps and benchmark a heavily edited area at the larger range.
- `MeshGenerator` generates heightfield terrain with LODs and border topology. `ChunkRuntime` currently owns one terrain mesh and one terrain MeshCollider, plus separate water geometry.
- `TerrainRequestManager`, `TerrainResultPublisher`, `TerrainAssetOwner`, and `TerrainHandoffCoordinator` already provide bounded workers, request identities, resource ownership, staged publication, and representation handoffs. Preserve these contracts.
- Collider requests currently use `colliderStep = 8`, or **2.4 m** in this scene. That mesh cannot remain authoritative over edited openings.
- Grass streaming retains the original `HeightMap` and native map leases. Changing a height map in place would not be a correct way to represent multi-level terrain or invalidate those readers.
- `BuildWorld.IsGround` recognizes `WorldGroundSurface` on the actual collider GameObject. Building grounding is persisted as logical `BuildPieceRecord.Grounded`, and `BuildSession.RecomputeSupport()` uses that flag. Merely swapping colliders does not update existing building support.
- `TerrainHorizonShadowSystem` uses a baseline height cache; it cannot describe cave ceilings or all excavation shadows.
- No general runtime world-save service was found in the inspected world/building scripts. Terrain persistence needs a small explicitly scoped service.

## Architecture and proposed source ownership

New code lives under `Assets/Scripts/TerrainSculpting/`. Names below describe intended responsibilities, not a requirement to create one file per trivial type.

| Component | Responsibility |
| --- | --- |
| `TerrainDensityCoordinates` / `DensityBrickCoord` | Integer global sample coordinates, correct signed floor division, local indexing, world conversion. |
| `TerrainBaselineSampler` / `ITerrainDensityGenerator` | Reproduce existing terrain as a signed scalar field; later allow procedural cave subtraction without changing brush/storage APIs. |
| `TerrainDensityBrick` / `TerrainDensityWorld` | Implicit/uniform/detailed states, canonical sample ownership, revisions, leases/snapshots, resident cache. |
| `TerrainDensityBrush` / `TerrainSculptCommand` | Shape bounds and deterministic excavation operation, independent of hardware input and rendering. |
| `TerrainSculptTransaction` | Stage a multi-brick change, build all required replacements, commit an internally consistent revision. |
| `TerrainDensityMesher` | Fixed-resolution deterministic surface extraction, normals, material classification, bounds, and shared-edge handling. |
| `TerrainSculptCoveragePolicy` | Near/fade/prefetch/release ranges, vertical selection, visual-only field, and coverage revision. |
| `TerrainSculptRequestManager` / publisher | Integrate bounded sampling/meshing/cooking and publication with existing streaming budgets and ownership. |
| `TerrainSculptRuntime` / surface adapter | Own replacement patch renderers/colliders and filtered baseline meshes; prevent duplicate or missing coverage. |
| `TerrainEditStore` | Versioned compressed brick snapshots and atomic committed manifests. |
| `TerrainSculptController` | Preview and development UI; consumes commands from `LocalPlayerInput`. |

Add `TerrainSculptConfiguration` to `WorldConfiguration` and corresponding serialized fields to `WorldManager`. `ChunkManager` composes the service and forwards lifecycle/coverage changes. Extend `ChunkRuntime`, coverage handoffs, diagnostics, grass invalidation, and building grounding only at explicit boundaries. Add an appropriate volume terrain shader under `Assets/Shaders/S_Terrain/`; reuse existing textures and material inputs without overwriting authored terrain assets.

No extra external voxel package is required for the initial implementation. Reuse Unity Collections/Burst/Jobs already in the project where appropriate. No sub-agent delegation is required by this plan.

## Density and coordinate contract

1. Use one global world-space lattice with spacing 0.3 m. Persistent coordinates are integers, not rounded floating-point positions. Convert negative positions with mathematical floor division. Avoid per-chunk floating-point origins that generate different boundary samples. Validate the source-grid alignment when the feature is enabled. This first profile targets SmearScene's 0.3 m source grid; a scene with another world scale must use a deliberately compatible profile rather than silently rescaling saved edits.
2. A brick owns samples with local indices `[0, 15]` on each axis. A 16-cell mesh needs corners `[0, 16]`, read through canonical owners. Central-difference normals require a transient halo `[-1, 17]`: **19^3 samples = about 13.4 KiB at two bytes each**. Do not serialize those duplicated halos.
3. Obtain baseline heights from the existing full-resolution generator, including existing shaping/river/water settings and world conversion. Do not use a visual LOD mesh as the source. Where interpolation is needed, use the same triangle interpolation as the original heightfield, not an unrelated bilinear approximation.
4. Baseline field is `clamp(worldY - H(worldX, worldZ), -B, B)`, where `B = 1.2 m`. It is a sign-correct height-based field, not exact Euclidean distance on slopes. Cache horizontal height work rather than evaluating terrain noise separately at every Y sample.
5. Encode with a defined round/clamp rule into signed 16-bit values using symmetric range `[-32767, 32767]`. Reserve `-32768` for invalid/debug handling. The theoretical encoding step is about 0.0000366 m; shape fidelity is governed by the 0.3 m grid, not that number.
6. Define one global zero tie rule (`value < 0` is stone). Resolve degenerate edge/face cases consistently from integer sample addresses; remove zero-area triangles. Test exact-zero cuts explicitly.
7. An absent brick means **generator baseline**, not automatically stone. A modified uniform-air or uniform-stone brick remains a saved override. Only omit a saved brick if its content actually matches baseline; never discard a modified all-air brick on unload.
8. Derive uniform states conservatively. Eight equal-sign corners do not prove the absence of a cavity. Future generator bounds must account for cave features lying entirely inside a brick.

## Excavation and transactional publication

Given cutting-shape signed distance `s` (negative inside the brush), use `cut = clamp(-s, -B, B)` and `new = max(old, cut)`. This makes excavation monotonic in the authoritative field and preserves existing air. Sphere and box formulas are evaluated in metres.

Expand removal bounds by B for density updates, plus the necessary meshing/normal halo for dependency invalidation. Updating negative samples in this band describes nearby stone; it does not excavate additional space. No whole-world distance recomputation or repeated global blur is required.

Transaction sequence:

1. Validate mode, menu/focus state, terrain-only target, reach, dry bounds, pending work, and allocation/staging admission. Non-terrain solids obstruct aiming; do not carve buildings/rocks through the terrain tool.
2. Acquire immutable snapshots of affected samples and baseline dependencies. Stage sample modifications under one edit transaction revision; do not expose half of a cross-brick edit.
3. Enumerate every dependent render/collider patch, source heightfield mask, foliage footprint, and grounding footprint. Coalesce overlapping dirty work.
4. Build sample/mesh outputs on workers. Create Unity Mesh objects on the supported thread, prepare collision, and keep outputs detached until current and complete. `Physics.BakeMesh` may be used with supported worker/job execution; never bake the same Mesh concurrently or modify it while cooking.
5. Compare world epoch, edit revision, coverage revision, profile fingerprint, and neighbour dependencies before allocating/publishing Unity resources. Dispose obsolete results without replacing live terrain.
6. Once replacements are ready, commit density, terrain coverage/masks, render geometry, and collision as one main-thread swap before the next physics simulation. Mesh creation/cooking can be spread across frames; the final reference swap cannot expose an intermediate hole or original invisible collider.
7. Emit `TerrainEditCommitted(revision, changedBounds)` for grounding/foliage. Queue persistence for that committed revision. A pending preview is explicitly pending, not a successful completed cut.
8. An edit failure leaves the previous committed field and physical/rendered coverage intact. Apply backpressure rather than allowing unlimited pending brushes.

One transaction may require multiple replacement objects. The existing 2 ms shared allowance remains **soft**: the final indivisible swap or Unity operation may overrun. Measure that cost; do not claim a hard frame-time guarantee. Default input accepts at most four actions per second, with one transaction active and no unbounded queued strokes. The preview reports busy rather than accumulating delayed excavations.

## Meshing, heightfield replacement, and seams

Use a consistent six-tetrahedra decomposition per grid cell for the first delivery, with a globally consistent face diagonal and shared-edge vertex indexing. This avoids introducing a large external Marching Cubes lookup package and matches the explanatory visual's general extraction approach. It can produce more triangles than a later optimized mesher; measure before considering replacement. Use 32-bit indices when necessary.

Compute smooth normals from neighbouring scalar samples. Keep geometry and collision consistent; a rounded brush does not require blurring the whole terrain. Assign cut faces stone based on their relation to the baseline and edit field. Preserve the original terrain control-map appearance on retained surface skin. Use world-space/triplanar stone texturing rather than trying to reuse top-down UVs for ceilings.

Only edited footprints and necessary guard columns switch to volume-derived representation. A footprint column is 4.8 x 4.8 m in XZ. **A buried edit must not simply hide the heightfield above it.** For each owned column, retain/reconstruct its baseline exterior surface as well as the edited underground boundaries. Discover the exterior vertical range from min/max baseline heights and bounds, not by allocating all intervening solid bricks.

Specific coverage requirements:

- Untouched source chunks retain their existing LOD pipeline and collider meshes.
- Source chunks with active replacement footprints use a filtered full-resolution baseline render mesh in the nearby affected area. The 16-cell footprint edges align with the original 0.3 m lattice; filtering must not rely on shader holes alone.
- Suppress original terrain triangles only where the replacement representation supplies the exterior surface. Do not leave double surfaces, hidden blocking triangles, or a shaft through an intact roof.
- Build explicit boundary collars against the original triangle-interpolated surface. Compatible tetrahedral decomposition alone is not assumed sufficient. Match boundary vertex positions and ordered edge segments; no open skirts extending through tunnels.
- The collider adapter must actually remove coarse baseline collision from replacement footprints, reconstruct nearby ground at suitable detail, and stitch to the surviving original collider. Original 2.4 m-spaced collider geometry cannot simply coexist over a hole. The 4.8 m footprints align with two original collider cells, simplifying clipping; fine/coarse boundary heights still require a tested collar.
- Every patch collider GameObject is on `GameplayLayers.WorldSolid` and carries `WorldGroundSurface` itself.
- Existing normal/far handoffs cannot reset masks or retire replacement coverage until the incoming representation is ready. Readiness includes matching collision where required.

Seam correctness is an early implementation gate. If a verified collar cannot be produced within the chosen mesh adapter, revise this plan and present the concrete alternative before broad integration; do not hide cracks with arbitrary overlapping meshes.

## Near-only visual representation

The authoritative field is never faded. It is used for persistence, editing, local solid queries, and collision. A separate sampled **visual field** transitions back to baseline at distance:

```text
r = horizontal distance from sample to coverage anchor
w = 1                              for r <= 192 m
w = 1 - smoothstep(192, 256, r)     for 192 < r < 256 m
w = 0                              for r >= 256 m
v = 1 - smoothstep(64, 96, abs(sampleY - coverageAnchorY))
renderField = lerp(baselineField, committedEditedField, w * v)
```

Quantize all three coverage anchor coordinates to the 4.8 m lattice with a deadband, so moving every centimetre does not remesh the transition regions. The specified distances are measured from that anchor; its offset from the player can be a few metres and is shown in debug diagnostics. All bricks in a coverage batch use the same anchor/revision. Shared samples use the same horizontal and vertical weights. The horizontal 288/320 m and vertical 112/128 m preparation/release margins include this offset.

This fade changes topology: an entrance may shrink and close in the annulus. A simple material alpha fade cannot solve that, so the annulus is rebuilt as necessary. Keep the old complete coverage revision until its replacement is ready, and do not allow the player to enter a region still using the wrong local field. Profile the annulus rebuild workload separately from editing.

Beyond the annulus, the representation matches baseline before handing back to ordinary LOD/far terrain. Keep a zero-edit exterior guard collar for exact baseline joins. Reclaim inactive render bricks after the release threshold, retaining saved overrides independently.

Vertical selection is sparse: include baseline surface-intersecting bricks for owned columns, modified boundary bricks whose AABBs intersect the vertical transition window, and their required dependency halos. Exterior skin beyond the vertical edit window can use the unchanged baseline shortcut. Retain/pin occupied and support-critical collision. Whole solid spans between a deep tunnel and the exterior surface are not allocated or meshed. The first version does not promise visibility down shafts farther than this window; the vertical visual field returns to baseline before handing back coverage. Include the zero-edit guard layer and reject impossible resource admission rather than publishing an open incomplete boundary.

No density LOD levels or Transvoxel implementation are required initially. All active volumetric mesh bricks use 0.3 m spacing. Multi-resolution volume meshing is a separate future performance project.

## Development controls

- Proposed toggle: **F6**, routed through `LocalPlayerInput`, which remains the sole hardware reader. Entering sculpt mode exits building mode; opening menus or losing focus suspends interaction.
- LMB performs one cut. Hold repeats at up to 4 Hz only when the previous transaction can accept work. Escape exits mode. Existing inventory/build controls keep their normal behaviour outside this mode.
- Mouse wheel adjusts radius/half-extent in 0.1 m increments within 0.5-2 m. A compact development panel selects sphere/box and shows size, reach, pending state, and rejection reason. No hidden destructive reset key.
- Aim using a terrain-only identity check after the first obstructing solid hit. Use a preview centred 0.25 m inside the hit surface so the default brush takes a useful scoop. No aiming through buildings, props, or an unavailable collision frontier.
- Box orientation is world-axis aligned in this version. Sphere/box results are sampled approximations, not conserved resource volumes.
- An explicit development save/flush action is available. Resetting saved edits requires an explicit separate user action in that UI and is not part of ordinary digging or feature disable.

## Persistence and recovery contract

Use a scoped path such as `Application.persistentDataPath/Worlds/<configured-slot>/TerrainEdits/`. Initial development slot is `development-default`; the manifest records a stable world ID and a fingerprint of seed, complete relevant generation settings/version, world scale, density profile, baseline interpolation, and format version. Do not key only by seed.

Persist immutable compressed full-brick snapshots of the committed **authoritative** density, not the render fade. Use built-in lossless compression and a documented codec tag. Include brick coordinates, revision, lengths, checksum, and uniform/detailed encoding. Never serialize Unity objects, transient halos, meshes, colliders, or a mandatory history of brush strokes.

Save a batch by writing new immutable brick records first, then atomically replace a small manifest naming the complete committed set. A crash during a multi-brick write must load either the previous manifest or the complete new one, not half a tunnel. Keep a last-good manifest until its successor is validated. Do not overwrite the only good brick record before the manifest commits. Orphan cleanup can be deferred.

Debounce background saves for two seconds after committed edits; flush on an explicit save and orderly world shutdown. The development UI distinguishes committed-in-memory from saved-on-disk. A crash can lose edits newer than the last completed save; this is an explicit limitation, not a claim of synchronous durability per click. Normal cache eviction must retain unsaved authoritative data or finish its save first.

Load flow: read/validate manifest and profile, read/decompress required overrides, obtain absent neighbours from baseline, reconstruct halos, build meshes/collision, then publish. A checksum failure, unknown format, or fingerprint mismatch is reported; it must not silently convert saved excavation to original stone or overwrite the save. Keep existing valid live geometry and deny entry into unavailable modified collision.

## Existing-system behaviour

**Building:** after an authoritative edit commits, re-evaluate grounding only for affected pieces and propagate support through the existing session solver. Invalidate preview support caches with a terrain revision. Use committed density-derived grounding or guaranteed-current collision; never infer ungrounding from a retired streaming collider. The existing unsupported grace/collapse rules remain authoritative. Revalidate a building when its edited terrain becomes available on reload.

**Foliage:** retain original biome/climate/ecology maps. Add persistent edit-footprint suppression to grass and small surface decoration, including already resident GPU instances and pending worker outputs. Apply it again after streaming/reload. A roof retained over a tunnel may lose small foliage under the conservative footprint rule; precise surface-contact preservation is deferred. Do not regenerate the biome map or break current map leases. Trees/large rocks keep their current states; unsupported props are a documented development limitation.

**Materials and shadows:** original control maps classify retained exterior skin; exposed geometry uses stone and world-space mapping. Keep original horizon data as the distant baseline. Do not bind a top-down baseline horizon as if it described a ceiling. Local volume surfaces participate in ordinary directional shadows in the nearby range. Existing ambient lighting may keep interiors brighter than physically correct; proper underground lighting and edit-aware distant horizon shadows are deferred.

**Player queries/movement:** physical patch colliders are ordinary solid surfaces. Add committed density/segment-query support for grounding and collision-readiness guards. Prevent walking or teleporting into an unready modified region; do not fall back to a flat original collider over its entrance. No new underground locomotion modes.

**Water:** preserve the global plane and existing water generation. Enforce A11 before staging a cut; neither clipping nor the density band is a water simulation.

**AI/future generation:** expose `TerrainEditCommitted` and stable changed bounds/revisions for future local navmesh updates. Provide generator interfaces for cave subtraction before edit overrides are applied. No runtime navmesh rebuilds, ore fields, new cave layouts, or structure generation now. Changing generators for a saved world requires an explicit migration/version policy.

## Scheduling, memory, and instrumentation

Initial limits, configurable but fixed for a running world:

- At most **two** active sculpt sample/mesh jobs, admitted within the existing LOD/mesh worker allowance rather than added without a global bound. At most one sculpt collision-cooking task concurrently, sharing admission with existing collider work.
- One active edit transaction; at most four completed ordinary sculpt mesh results waiting for publication, plus the bounded results explicitly reserved for the admitted transaction/coverage batch. Bound bytes as well as item count.
- Reserve 0.5 ms **inside the existing 2 ms shared streaming allowance** for sculpt staging/publication; unused allowance returns to other phases. Do not add an independent unmeasured 2 ms loop. Existing collision-first service and foliage reservations must remain viable.
- Resident authoritative/cached density and density snapshot staging budget: **64 MiB**, tracked separately from existing terrain maps. Estimate and admit all brick/snapshot/halo buffers before a transaction starts. Evict reconstructible clean caches first; never lose unsaved or occupied data.
- Derived CPU mesh plus pending output/staging target cap: **128 MiB**. Track estimated GPU mesh bytes and collision allocations separately; engine-owned memory still needs profiler measurement. These limits are admission controls, not a promised total-process delta.
- If mandatory local terrain cannot fit, keep the previous complete coverage, report a development limit, and deny the edit/movement requiring missing geometry. Never silently drop a saved edit or hide required collision.

Suggested markers: `FS.Streaming.Sculpt.Sample`, `.Edit`, `.Mesh`, `.Cook`, `.Stage`, `.Commit`, `.Coverage`, `.Save`, `.Load`, `.FoliageInvalidate`, `.Grounding`. Counters: detailed/uniform/implicit bricks, dirty/saved revisions, resident/staging bytes, mesh triangles, active/pending jobs, edit latency, coverage backlog, stale discards, and publication overruns. Extend F3 diagnostics rather than logging every frame.

Development targets on the user's machine: ordinary default-brush edit p95 visible/collision commit latency <=150 ms after warmup; no default-brush edit >400 ms on the fixed test route; bounded memory after repeated leave/return cycles; no uncontrolled worker growth. Record p95/p99 streaming time and worst indivisible commits against an equivalent unedited route. Targets are subject to measurement, and missed targets require a documented adjustment before expanding scope.

## Phased implementation and acceptance gates

| Phase | Work | Required evidence before proceeding |
| --- | --- | --- |
| P0: review and fixture | Review assumptions; capture clean walking/streaming baseline; add isolated terrain fixtures using the actual scene profile. | User review completed; baseline captures and no unrelated asset changes. |
| P1: authoritative field | Coordinates, baseline generator adapter, encoding, sparse bricks, brush union, bounded snapshots; no main-scene visual switch. | Negative coordinates; exact zero; 1 m box and sphere; repeated/overlapping cuts; cross-face/edge/corner edits; no lost air; bounded band; baseline height agreement. |
| P2: mesh and collider adapter | Deterministic mesher, normals, patch columns, baseline filtering, seam collars, explicit colliders. | Watertight adjacent bricks; intact roof above buried cavity; successful tunnel breakthrough; no source collider blocking openings; actual motor traverses floor, ramp, entrance and patch/chunk boundaries. |
| P3: transactions and streaming | Shared scheduling, stale-result rejection, resource lifecycle, atomic swaps, horizontal 192/256 m and vertical 64/96 m visual fields, prefetch/release, destination guards. | Rapid edits plus movement; coverage-anchor changes; no visible/physical partial commits; no holes or double geometry; unchanged far/macro outputs; range sweeps and low-budget eventual progress; heavily edited-area workload at the enlarged horizontal radius. |
| P4: terrain persistence | Versioned compressed snapshots, committed manifests, restart reconstruction, failures and shutdown. | Byte-equivalent density after reload; same boundary geometry; cross-brick atomic saves; interrupted write recovery; corrupt/profile-mismatch handling; unsaved eviction protection. |
| P5: development interaction | F6 mode, brush controls, preview, menu/focus arbitration, limits and save status. | Useful default scoop; no double input with building; blocked targets respected; no hidden queue; water restriction accurately reported. |
| P6: world consumers | Grass/decoration suppression, building grounding/cache invalidation, materials and local ceiling shadows. | No hovering grass over an opening after edit or reload; actual support removal and grace behaviour; no collapse from unload; cut walls/ceilings render and collide on both relevant approaches. |
| P7: validation and tuning | Runtime/editor/player compilation; isolated regressions; visual inspection and profiler route; final operating guide. | Existing terrain ownership/scheduling/height/water/foliage/building/movement checks pass; targets reported honestly; no leaks after repeated reload/retirement; UI/settings documented. |

Use the existing isolated Unity validation patterns in `Tools/` to avoid opening a second editor on the active project. Proposed additions: `Assets/Editor/TerrainSculptingValidation.cs`, `Assets/Editor/TerrainSculptingIntegrationValidation.cs`, and `Tools/ValidateTerrainSculpting.ps1`. These tests protect topology, persistence, and physical behaviour; do not substitute a large count of implementation-mirroring assertions for actual traversal/rendering evidence.

Required visual/manual cases: unedited hill comparison; 1 m scoop at several sub-grid offsets; level cut into slope; retained ceiling; two tunnels joining; brick and source-chunk corner cuts; negative world coordinates; actor standing on edited ground during commits; building undermining; grass already uploaded before cut; reload while inside a tunnel; walking through all range thresholds; water-bound rejection; feature disabled with an existing save.

## Explicit first-version limitations and later extensions

- Distant terrain and maps show the original landscape; visual fade is deliberate. No distant excavation silhouette, distant cave interiors, or edit-aware macro heightfield updates.
- No addition/filling, resource economy, ore, procedural caves, collapse physics, fluid changes, or AI implementation.
- Finite sample spacing can round away thin walls or small holes. Test and document practical feature sizes; do not equate encoding precision with geometric precision.
- Trees/large rocks may remain unsupported, small foliage suppression is conservative, and interiors use existing ambient lighting.
- Tetrahedral extraction and annulus rebuilds are intentionally simple first choices with measurable costs. Potential later work: optimized extraction, density LOD/Transvoxel, larger mesh batching, tighter narrow-band storage, precise foliage attachment, and cheaper surface-following collision.
- Generator/spacing/profile changes require explicit save migration or a new slot. No automatic destructive reset.

## References

- [Existing streaming budgets and ownership](STREAMING_SCHEDULING.md), [terrain ownership](TERRAIN_OWNERSHIP.md), [building prototype](BUILDING_PROTOTYPE.md), and [player queries](PlayerQuery.md).
- [Voxel Tools: smooth terrain, field clamping and encoding](https://voxel-tools.readthedocs.io/en/latest/smooth_terrain/). Reference for established techniques; this plan does not adopt its Godot implementation or performance claims.
- [Unity Physics.BakeMesh](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics.BakeMesh.html). Confirm execution constraints against the project's actual Unity version during implementation.
- [Transvoxel](https://transvoxel.org/). Future multi-resolution surface stitching, explicitly outside the initial implementation.
