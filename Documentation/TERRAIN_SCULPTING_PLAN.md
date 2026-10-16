# Sparse density terrain sculpting implementation plan

Status: implementation authorized October 15, 2026. P1–P5 and the playtest shutdown/latency fixes are implemented. P6's stone/local-shadow, vegetation and reviewed building policies are implemented. Building placement permits terrain overlap, checks fresh initial footing and retains placement-time grounding after excavation. The user subsequently authorized the 2 m cached sky-access field; terrain/building ambient and cave brightness controls are implemented. Point-light dynamics remain deferred. See [checkpoint 6 A/B](TERRAIN_SCULPTING_CHECKPOINT_6_AB.md), [building integration](TERRAIN_SCULPTING_BUILDING_INTEGRATION.md), [sky access](TERRAIN_SKY_ACCESS.md) and earlier checkpoint reports for scope/evidence. Sculpting remains opt-in; the user's playtest scene settings are preserved. Live walking/frame-time and heavy-edit p95 gates remain open for P7; this checkpoint does not start P7.

## Outcome and scope

**Current host policy, superseding the original P4/P5 save integration:** terrain edits are session-only at the user's request. WorldManager never loads or saves the previous development slot. Edits and foliage suppression survive streaming within one world, then reset when it restarts, including on seed changes. The P4 backend remains available only to explicit persistent hosts. A bounded memory-pressure recovery prioritizes collision and 16 m/pinned edited visuals if a normal successor batch cannot fit. See [session lifecycle and spawn recovery](TERRAIN_SCULPTING_SESSION_FIXES.md).

Introduce three-dimensional excavation with smooth/faceted reconstructed terrain, including player-dug tunnels and retained ceilings. The initial world remains the existing generated landscape with solid stone beneath it. No procedural underground cavities, ore placement, fluid simulation, or AI navigation implementation is part of this first delivery.

The authoritative editable terrain is a procedural baseline plus sparse density overrides. Existing heightfield meshes remain an optimized representation of unchanged terrain. Only nearby representations reflect edits; distant terrain generation, macro meshes, maps, and silhouette data continue to show the baseline.

This document is the reference for execution. Complete phases in order, recording measured results and material deviations here. Do not treat performance targets as measured results. The user approved the reviewed plan and authorized implementation in phases, with checkpoints available for review.

**P5 playtest follow-up:** the user authorized shutdown/latency fixes before P6. The view now tolerates scene destruction order, packed seam-edge hashes avoid pathological collisions, extracted 4.8 m mesh bricks are cached inside a 16 MiB conservative share of the existing derived budget, and only affected source-chunk windows are assembled/published. Source-chunk geometry/publication can still grow with local complexity; live frame-time and heavy-edit p95 gates remain open. Save format, density profile, scene settings and P6 consumers are unchanged. See [playtest fixes and measured before/after results](TERRAIN_SCULPTING_PLAYTEST_FIXES.md).

**P6 A/B checkpoint:** exposed surfaces now use independently replaceable stone settings with triplanar sampling, local two-sided ceiling shadows and per-fragment cascade selection. Grass/small-decoration consumers apply an immutable, committed density-derived suppression footprint to resident and pending representations, restoring it from existing saves. The persistent mask shares the derived budget with a 16 MiB conservative cap and survives render retirement; F3 reports its charge. Original biome/ecology maps, trees/large rocks and building support remain unchanged. See [checkpoint 6 A/B](TERRAIN_SCULPTING_CHECKPOINT_6_AB.md) for source settings, actual renders, limits and validation.

## Assumptions for user review

| ID | Decision | Status / implication |
| --- | --- | --- |
| A01 | Excavation only. | **Confirmed by user.** Raising, filling, flattening, and general smoothing tools are deferred. |
| A02 | Development sculpt mode with adjustable brushes. | **Confirmed by user.** No inventory item, durability, stamina, loot, or crafting integration. |
| A03 | Existing landscape and solid stone below it. | **User scope.** Manually excavated cavities are supported; generated caves and ore are deferred. |
| A04 | Density spacing is **0.3 m in world space**, on all three axes. | Approved. Matches SmearScene's current horizontal terrain spacing. Gives roughly 3.3 sampling intervals across a 1 m cut and avoids a second horizontal lattice. This deliberately selects more detail than the earlier 0.5 m example. |
| A05 | A density brick owns **16 x 16 x 16 samples**; a mesh brick spans **16 cells = 4.8 m** per side. | Approved. Shared boundary samples are read from neighbouring owners, not independently persisted. A source terrain chunk is 128 cells / 38.4 m, or 8 bricks across. |
| A06 | One signed 16-bit density per sample, clamped to **[-1.2, +1.2] m**. | Approved. Negative is stone; nonnegative is air. Values approximate signed distance; they are not material percentages. Core detailed brick payload is 8 KiB. |
| A07 | Full visible excavation through **192 m horizontal distance** and **64 m vertical separation**; render-only horizontal transition **192-256 m** and vertical transition **64-96 m**. | **Approved with the reviewed plan.** Horizontal distance is to the local player's XZ focus. This replaces the initial conservative 64-96 m horizontal range. The independent vertical transition closes distant shaft boundaries instead of exposing an open streaming cut. Data and near collision never use either visual fade. Large edits/tunnel entrances can close or disappear at distance as an intentional first-version limitation. |
| A08 | Begin preparing relevant edited representations through **288 m horizontally / 112 m vertically**; retire eligible caches outside **320 m horizontally or 128 m vertically**, subject to pins and save safety. | Approved. Use tile/brick AABB distance and account for the coverage anchor margin. Only relevant sparse edited data, replacement exterior skin, and dependencies are prepared; this does not load a dense cylinder of underground stone. |
| A09 | Edited collision is required within **32 m in 3D** of the player, requested through **48 m**, retained through **64 m**. | Approved. Pin geometry occupied by the player. Existing ordinary terrain collider coverage remains in place. Teleports and new building placement must wait for current destination edited collision. Under revised A14, stored building anchors do not require terrain support pins. |
| A10 | Sphere and axis-aligned box excavation brushes. | Approved. Default sphere radius 0.5 m (1 m diameter), adjustable radius/box half-extent 0.5-2 m; maximum reach 6 m. Shape is continuous, not grid-snapped. A box brush is an approximate box-shaped cut at this resolution, not a promise of sharp corners or exact removed volume. |
| A11 | First delivery supports **dry excavation only**. | **Confirmed by user.** Reject a brush whose removal bounds extend below `GlobalWaterHeight + 0.3 m`. SmearScene currently has global water Y = 14.4 m. No flooding, draining, or water mesh updates. This restriction can be relaxed separately. |
| A12 | **Session-only terrain until whole-world saving is introduced.** | **Updated at the user's request after P6 A/B.** Existing disk saves are ignored. Committed edits and suppression survive streaming during a session but reset on world restart/seed change. The P4 backend is retained for explicit future hosts; normal play has no store or save operation. |
| A13 | Suppress grass/small decoration only where original surface support is removed, with **0.15 m circular padding on a 0.15 m mask**. | **Refined after the user's P6 playtest.** Intact cave roofs retain vegetation; the support probe is 0.05 m below the original landscape and follows the mesher's field. This replaces broad XZ projection and 0.6 m padding. No new grass is generated underground. Existing trees/large rocks keep their registry/placement states and may remain unsupported. See [foliage refinements](TERRAIN_SCULPTING_FOLIAGE_REFINEMENTS.md). |
| A14 | **Initial footing is checked; placed grounding survives excavation. Terrain overlap is permitted for all build types.** | **Revised by the user during the P6 building review.** Foundations and existing terrain-capable definitions must obtain valid initial contact with current published terrain, or attach to supported construction. Other parts require structural support; stairs retain endpoint attachment. Buried pieces keep their complete geometry/collision. Excavation and streaming never revoke stored grounding; structural removal/destruction still uses the existing solver and grace/collapse rules. This supersedes the original terrain-reactive grounding proposal. |
| A15 | Preserve existing landscape appearance outside changed patches; newly exposed cut surfaces use a stone material. | **Confirmed by user.** Use world-space texturing for walls/ceilings. Add local terrain shadow casting needed for ceilings; no cave GI, ambient occlusion bake, or guaranteed dark interiors in this delivery. |
| A16 | Local single-player/editor prototype. | Approved. Replication, multiplayer authority, concurrent remote edits, consoles, and production save migration are deferred. Stable IDs, revisions, and a versioned file format leave room for them. |
| A17 | Do not modify terrain generation settings or existing world content to accommodate the prototype. | Approved. New density profile is immutable for a saved world. Feature is opt-in and enabled in SmearScene only during the implementation/integration phase. |
| A18 | **Cached sky access at 2 m spacing.** | **Authorized after the P6 building review.** Sparse 32 m tiles, nearest 128 resident tiles within a 512 x 256 x 512 m lookup window. Reserve 24 MiB inside the existing 128 MiB derived cap; keep the existing 16 MiB extraction/mask maximum shares inside the remaining geometry budget. Terrain and wood/stone building ambient/fog consume the field. Live default cave ambient is 2.5% of ordinary ambient, adjustable independently of visibility. Point-light dynamics, constructed-roof occlusion and arbitrary material integration remain deferred. See [controls and limits](TERRAIN_SKY_ACCESS.md). |

The user confirmed A01/A02 during preparation and A11/A13/A14/A15 during review, then approved the reviewed plan (including the enlarged horizontal range) and authorized phased implementation. Entries originally labelled proposed are now the approved implementation baseline; material changes must be documented at a checkpoint.

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

**P2 result:** this gate passed, including actual SmearScene native heights, fine/coarse filtered-baseline joins and real player-motor traversals. The adapter uses zipper collars one source cell wide for rendering and eight cells wide for collision. It rejects an edited collar or an incomplete vertical envelope. P3 must merge/expand rectangular footprints until the outer collar is unchanged; a single 4.8 m column is too narrow for the two 2.4 m collision collars, so collision footprints require grouped columns (at least 9.6 m on the column grid). See checkpoint 2 for the precise adapter constraints. Conservative generator bounds and override metadata skip proven uniform stone/air spans; unknown bounds or detailed overrides fall back to sampling. Per-build caps exist; global derived-data memory admission and asynchronous publication still belong to P3.

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

- Implemented toggle: **F6**, routed through `LocalPlayerInput`, which remains the sole hardware reader for sculpt commands. Entering sculpt mode exits building mode; opening menus or losing focus suspends interaction.
- LMB performs one cut. Hold repeats at up to 4 Hz only when the previous transaction can accept work. Escape exits mode. Existing inventory/build controls keep their normal behaviour outside this mode.
- Mouse wheel adjusts radius/half-extent in 0.1 m increments within 0.5-2 m. A compact development panel selects sphere/box and shows size, reach, pending state, and rejection reason. No hidden destructive reset key.
- Aim using a terrain-only identity check after the first obstructing solid hit. Use a preview centred 0.25 m inside the hit surface so the default brush takes a useful scoop. No aiming through buildings, props, or an unavailable collision frontier.
- Box orientation is world-axis aligned in this version. Sphere/box results are sampled approximations, not conserved resource volumes.
- An explicit development save/flush action is available. Resetting saved edits requires an explicit separate user action in that UI and is not part of ordinary digging or feature disable.

**P5 checkpoint decisions:** R switches sphere/box through the passive HUD; F10 requests a nonblocking save/retry (F7 is already used by performance sampling). One wheel-event direction changes size by 0.1 m. Reach is measured from the camera eye to the nearest solid hit, with the continuous brush extending around its inset centre. Fresh mouse presses are required after mode/menu/focus/capture transitions. Busy holds do not queue positions, and failed backend work requires an explicit new click. The controller is bound automatically to the local input only for an enabled sculpting world. No reset/delete action is introduced. [Checkpoint 5](TERRAIN_SCULPTING_CHECKPOINT_5.md) contains manual testing steps and real Unity captures; the saved scene remains off.

## Persistence and recovery contract

Use a scoped path such as `Application.persistentDataPath/Worlds/<configured-slot>/TerrainEdits/`. Initial development slot is `development-default`; the manifest records a stable world ID and a fingerprint of seed, complete relevant generation settings/version, world scale, density profile, baseline interpolation, and format version. Do not key only by seed.

Persist immutable compressed full-brick snapshots of the committed **authoritative** density, not the render fade. Use built-in lossless compression and a documented codec tag. Include brick coordinates, revision, lengths, checksum, and uniform/detailed encoding. Never serialize Unity objects, transient halos, meshes, colliders, or a mandatory history of brush strokes.

Save a batch by writing new immutable brick records first, then atomically replace a small manifest naming the complete committed set. A crash during a multi-brick write must load either the previous manifest or the complete new one, not half a tunnel. Keep a last-good manifest until its successor is validated. Do not overwrite the only good brick record before the manifest commits. Orphan cleanup can be deferred.

Debounce background saves for two seconds after committed edits; flush on an explicit save and orderly world shutdown. The development UI distinguishes committed-in-memory from saved-on-disk. A crash can lose edits newer than the last completed save; this is an explicit limitation, not a claim of synchronous durability per click. Normal cache eviction must retain unsaved authoritative data or finish its save first.

Load flow: read/validate manifest and profile, read/decompress required overrides, obtain absent neighbours from baseline, reconstruct halos, build meshes/collision, then publish. A checksum failure, unknown format, or fingerprint mismatch is reported; it must not silently convert saved excavation to original stone or overwrite the save. Keep existing valid live geometry and deny entry into unavailable modified collision.

**P4 checkpoint refinements:** the initial store eagerly restores the sparse owner set and retains it within the existing payload budget; it does not page density out to disk. Persistent hosts cap accepted committed payload at `(64 MiB - 64 KiB) / 2 = 31.96875 MiB`, preserving room for the conservatively charged full snapshot plus codec scratch. Saved/unsaved overrides cannot be lost through derived-cache retirement. A validated predecessor can recover a corrupt latest generation with an explicit warning and read-only excavation/save policy. Unknown formats or incompatible fingerprints never fall back. Startup movement waits for validation and restored collision. Atomic manifests, compressed immutable records, exclusive ownership, debounce/flush/retry and these limits are documented in [checkpoint 4](TERRAIN_SCULPTING_CHECKPOINT_4.md). Disk paging, orphan cleanup and migrations remain future work.

## Existing-system behaviour

**Building (revised P6 policy):** permit all terrain intersections, including fully concealed pieces, without cutting terrain or build geometry. Preserve suggested snaps and manual nudges. Remove terrain-above-point burial tests from stair eligibility and structural endpoint connections; a cave roof must not imply that the space beneath it is solid. New previews/commits recompute initial footing against current published collision. The local host binds the sculpt runtime's coverage guard and rejects placement while relevant edited collision is unavailable/stale. Preview structural caches include the freshly computed grounding result; terrain no longer participates in structural stair connections. Once committed, the record's grounding is retained across excavation, later structural rebuilds and streaming. No edit-triggered terrain re-grounding scan, terrain support pins or physics load simulation is added. Existing duplicate, prop/player, reach, structural support, full-bearing and grace/collapse rules remain. Hidden-piece selection/X-ray is deferred. See [building integration and validation](TERRAIN_SCULPTING_BUILDING_INTEGRATION.md).

**Foliage:** retain original biome/climate/ecology maps. Apply immutable surface-support suppression to resident GPU instances and pending small-decoration outputs. Reapply after streaming; rebuild from density for explicit persistent hosts. Intact tunnel roofs preserve vegetation. Surface breakthrough clears a narrow 0.15 m border on a finer 0.15 m mask. Trees/large rocks keep their current states; unsupported props are a documented development limitation. See [surface foliage refinements](TERRAIN_SCULPTING_FOLIAGE_REFINEMENTS.md).

**Materials and shadows:** original control maps classify retained exterior skin; exposed geometry uses stone and world-space mapping. Keep original horizon data as the distant baseline. Do not bind a top-down baseline horizon as if it described a ceiling. Local volume surfaces participate in ordinary directional shadows in the nearby range. The separately authorized cached sky-access field now attenuates terrain/building ambient floors and fog, with live cave brightness controls. This is approximate sky visibility, not bounced GI; point-light dynamics and edit-aware distant horizon shadows remain deferred.

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
- Cached sky access reserves **24 MiB within that derived cap**, conservatively including its managed/native-readable/GPU texture copies and bounded worker scratch. Terrain geometry retains 104 MiB; the existing extraction and foliage mask maximum shares remain 16 MiB each. Optional sky workers yield and release snapshots before a new sculpt job dispatches; lighting never delays geometry publication.
- If mandatory local terrain cannot fit, keep the previous complete coverage, report a development limit, and deny the edit/movement requiring missing geometry. Never silently drop a saved edit or hide required collision.

Suggested markers: `FS.Streaming.Sculpt.Sample`, `.Edit`, `.Mesh`, `.Cook`, `.Stage`, `.Commit`, `.Coverage`, `.Save`, `.Load`, `.FoliageInvalidate`, `.Grounding`. Counters: detailed/uniform/implicit bricks, dirty/saved revisions, resident/staging bytes, mesh triangles, active/pending jobs, edit latency, coverage backlog, stale discards, and publication overruns. Extend F3 diagnostics rather than logging every frame.

Development targets on the user's machine: ordinary default-brush edit p95 visible/collision commit latency <=150 ms after warmup; no default-brush edit >400 ms on the fixed test route; bounded memory after repeated leave/return cycles; no uncontrolled worker growth. Record p95/p99 streaming time and worst indivisible commits against an equivalent unedited route. Targets are subject to measurement, and missed targets require a documented adjustment before expanding scope.

**P3 checkpoint:** functional transaction/coverage/ownership gates pass, including the actual native generator/`ChunkManager`, published source-chunk seams, delayed work, standing/traversing actors and nine separated edits. The runtime currently uses one shared-admission producer, complete rectangular patch batches and a 64-source-chunk cap for exceptional full far-tile/relocation handoffs. Scalar/bulk sampling agrees, immutable streaming height maps are reused, and unchanged chunk representations are reused. Native managed-worker edit latency still misses the provisional target in the isolated fixture. Scene activation remains disabled; capture the live baseline and optimize/retest extraction before claiming that gate. Persistence can follow as a separate functional checkpoint without enabling gameplay. See checkpoint 3 for precise limitations, measurements and reproduction.

## Phased implementation and acceptance gates

| Phase | Work | Required evidence before proceeding |
| --- | --- | --- |
| P0: review and fixture | Review assumptions; capture clean walking/streaming baseline; add isolated terrain fixtures using the actual scene profile. | User review completed; baseline captures and no unrelated asset changes. |
| P1: authoritative field | Coordinates, baseline generator adapter, encoding, sparse bricks, brush union, bounded snapshots; no main-scene visual switch. | Negative coordinates; exact zero; 1 m box and sphere; repeated/overlapping cuts; cross-face/edge/corner edits; no lost air; bounded band; baseline height agreement. |
| P2: mesh and collider adapter | Deterministic mesher, normals, patch columns, baseline filtering, seam collars, explicit colliders. | Watertight adjacent bricks; intact roof above buried cavity; successful tunnel breakthrough; no source collider blocking openings; actual motor traverses floor, ramp, entrance and patch/chunk boundaries. |
| P3: transactions and streaming | Shared scheduling, stale-result rejection, resource lifecycle, atomic swaps, horizontal 192/256 m and vertical 64/96 m visual fields, prefetch/release, destination guards. | Rapid edits plus movement; coverage-anchor changes; no visible/physical partial commits; no holes or double geometry; unchanged far/macro outputs; range sweeps and low-budget eventual progress; heavily edited-area workload at the enlarged horizontal radius. |
| P4: terrain persistence | Versioned compressed snapshots, committed manifests, restart reconstruction, failures and shutdown. | Byte-equivalent density after reload; same boundary geometry; cross-brick atomic saves; interrupted write recovery; corrupt/profile-mismatch handling; unsaved eviction protection. |
| P5: development interaction | F6 mode, brush controls, preview, menu/focus arbitration, limits and save status. | Useful default scoop; no double input with building; blocked targets respected; no hidden queue; water restriction accurately reported. |
| P6: world consumers | Grass/decoration suppression, permissive terrain/build overlap, fresh initial footing with retained anchors, materials and local ceiling shadows. Cached sky access was subsequently authorized separately. | No hovering grass over an opening after edit or reload; cave stairs and buried supported builds accepted; new footing follows excavation while placed anchors survive it and unload; structural destruction still disconnects support and applies grace; cut walls/ceilings render and collide on both relevant approaches. Cached ambient has separate density/GPU checks; point-light dynamics and hidden-build selection remain deferred. |
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
