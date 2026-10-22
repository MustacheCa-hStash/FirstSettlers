# FirstSettlers Work Notes

## October 21: limestone L18�L24

Added the fitted eaves cornice, threshold steps, half- and full-storey masonry stairs, bedded landing, battered retaining wall and stepped buttress. The cornice replaces the upper 0.25 m of compatible walls without moving the 3 m storey datum; its mesh and collision restore when removed. Landing and buttress support requires complete solid bedding, with terrain grounding available for the buttress and retaining wall. Seven meshes total 292 triangles with shared colour/AO and normal maps. Validation includes 160 character stair traversals and all installed building regressions. See the [guide, source and Unity component sheet](Documentation/LIMESTONE_L18_L24.md). Restart Play mode after import.

## October 21: limestone L10–L17

Added the arched doorway, narrow and arched windows, two courtyard wall lengths, chamfered coping, capped courtyard pier and limestone half-gable. Openings stay clear in mesh and collision. L13 plus L15 matches L16's 1.50 m height, and L17 flips between triangular slopes with R. Nine FBXs share baked colour/AO and normal maps; the eight selectable parts total 528 triangles. Validation covers eight headings, sixteen character crossings, twenty-four terrain placements and both roof types, followed by the installed building regressions. See the [L10–L17 guide and Unity preview](Documentation/LIMESTONE_L10_L17.md). Restart Play mode after import.

## October 21: layered tree stands

Tree distribution now uses world-space stands, dominant species palettes and
bounded neighbour competition across chunk edges. Authored tree sizes and
exclusion radii are preserved. Controlled mature-tree tuning produces about
19 trees/ha in forest cores, 4/ha in openings, and 1.5/ha across grassland. Edit
`Assets/Resources/Foliage/TreeDistribution.asset` and regenerate/restart Play Mode.
See [density, size checks, implementation and validation](Documentation/TREE_STANDS.md).

## October 17: timber and infill L25/L27/L28/L31/L33/L35

Added six catalog pieces with editable Blender sources, native-scale FBXs, one shared trim material, physical collision hulls and menu thumbnails. L28 keeps a clear 1 × 1 m window without shutters; L31 flips between two triangular slopes. L33 has solid overlapping shingle courses, deep eaves and an exposed timber underside. L35 mounts above a doorway, checking every rear contact against supported wall or gable solids. The final isolated run passed 41,146 new checks and 36,641 existing building regression checks, including 192 stair walks. Use **B → Walls/Roofs** and restart Play mode after import. See the [kit guide, Unity previews and trim sheet](Documentation/TIMBER_INFILL_KIT.md).

## October 17: L04–L09 limestone components

Added the limestone pier, paving, strip footing, limewashed wall, plinth and square doorway, with editable Blender sources, eighteen UV-varied FBXs, shared baked maps, prefabs and menu icons. Paving needs full underside bearing; authored footings retain their geometry and seat paving above their top. The 1.25 × 2.25 m doorway remains clear in mesh and compound collision. Validation passed 3,404 new checks plus 1,400 L01–L03 and 3,544 ordinary-stone regression checks, including sixteen doorway walks and eight terrain-footing cases. Restart Play mode after import. See the [L04–L09 guide and preview](Documentation/LIMESTONE_L04_L09.md).

## October 17: L01–L03 limestone walls

Added coursed, dressed and narrow limestone walls under **B → Walls**, with editable Blender stones, nine UV-varied FBXs, shared baked colour/AO and normal maps, prefabs and rendered icons. Full walls are 4 × 3 × 0.5 m; the narrow wall is 2 × 3 × 0.5 m with matching block scale. Each placed wall uses 12 triangles. The isolated run passed 1,424 limestone checks and 3,538 existing stone checks. Restart Play mode after import. See the [implementation guide and rendered preview](Documentation/LIMESTONE_BUILD_KIT.md).

## October 17: layered thatch roof

The detailed roof is now baked to **1,180 triangles**, a 72.5% reduction, retaining
the course shapes and fringe. A unique UV1 normal/AO bake supplies the lost relief;
the detailed Blender source remains available. Matched Unity silhouette agreement
is 99.3% in the comparison view. See [baking and validation](ArtReferences/ThatchRoof/Redesign/Baked/README.md).

Replaced the flat covering with a detailed first pass: four overlapping courses,
64 rounded bundles, 192 modeled straw tips and lifted cutout fringe, with warm
new straw artwork. The 4,286-triangle panel keeps its existing structural module,
material batching and two thin colliders; uphill visuals now clip at the seam.
An independent editable Blender source and renders are in the
[roof redesign guide](ArtReferences/ThatchRoof/Redesign/README.md), including direct
measurements of Valheim's locally installed roof mesh and a later baking plan.

## October 16: adaptive surface lighting and mounted torch

Added bounded local flame lighting with cached surface irradiance, spatial light lists, separate moving-occluder shadows, local invalidation and queued refreshes. The revised torch has a 12 m cutoff, wider useful illumination, up to 64 illuminating sources and effects eligible within 96 m. Surface caching was implemented and benchmarked, and remains opt-in because it did not consistently outperform spatial direct lighting. Moving/cheap cases retain live lighting. The grazing shadow-band comparison now uses actual texel centres. **B → Lighting → Mounted torch** places the new fibre-bound torch/wooden holder; **T** equips the development carried variant. A shared 36-frame flame atlas supplies visible fire and mild matching flicker. Restart Play mode after import.

The original eight-light CPU baseline is historical. The new standalone comparison covers 8/32/64 torches in a hall and cave, including forced/adaptive surface caching, spatial direct lighting and native URP. The default raw texture payload is 102 MiB; enabling the experimental surface cache adds approximately 24 MiB. See [the surface-cache trial and comparison](Documentation/LOCAL_FLAME_SURFACE_CACHE.md). See [implementation, controls, evidence and limits](Documentation/LOCAL_FLAME_LIGHTING.md) and [the editable torch model](ArtReferences/Torch/README.md).

## October 16: F01–F06 furniture in the building catalog

Added the first six household pieces: three-leg stool, plank bench, pegged table, low work table, sleeping platform and wall shelf. Editable Blender source, complete wood/cord UVs, six FBXs, prefabs and rendered icons are installed under a new Furniture category. Furniture receives per-foot or rear-wall support without carrying structural loads; compound collision keeps space beneath tables/seats open. Compilation and 13,115 isolated asset/UI/runtime checks passed, including 320 floor/rotation cases and four menu resolutions. Existing modular-wood and stone regressions also passed. See the [furniture implementation guide](Documentation/FURNITURE_KIT.md). Restart Play Mode after import to load the updated catalog.

## October 16: directional light under cave roofs

Fixed residual sun/moon light on underground floors and sun-facing building faces. The day/night controller's deliberately weak outdoor shadows left direct light in fully occluded caves after ambient was dimmed. Terrain and wood/stone shaders now recover full geometric shadow occlusion underground, preserving outdoor artistic shadows and actual direct light through entrances. This reuses the existing field and shadow map, with no new tracing or memory allocation. See [cause, limits and regression coverage](Documentation/TERRAIN_SKY_ACCESS.md#directional-cave-lighting-fix).

## October 16: cached cave sky access

Added a sparse **2 m** sky-access field for terrain and shared wood/stone building ambient, with live cave-brightness and entrance-response controls in `Assets/Resources/TerrainSculpting/CaveLighting.asset`. Lighting uses shared worker admission, revision-safe background tracing, bounded nearest-tile residency and a 24 MiB reservation inside the existing 128 MiB derived cap. Day/night lighting reuses visibility; fog follows cave attenuation. Point-light dynamics remain deferred. See [settings, cache behavior and validation](Documentation/TERRAIN_SKY_ACCESS.md).

## October 16: building in excavated terrain

P6 building integration permits partial/complete terrain overlap, including underground stairs and buried endpoints, while retaining initial foundation/terrain-capable footing checks and ordinary structural attachments. New placement waits for current edited collision and rechecks footing; placed anchors survive excavation and streaming. Removing/destroying structural support still uses the existing grace/collapse rules. Full build meshes/colliders and existing snaps/nudges remain. Lighting, cached sky access, point lights, hidden-piece selection and P7 remain deferred. See [integration policy, validation and future lighting design](Documentation/TERRAIN_SCULPTING_BUILDING_INTEGRATION.md).

## Surface foliage around excavations

Grass/small-decoration clearing now follows lost support at the original surface. Sealed caves retain vegetation above them; breakthrough removes it locally. Circular padding is reduced from 0.6 m to 0.15 m on a finer foliage mask, while terrain density stays at 0.3 m. See [foliage refinements](Documentation/TERRAIN_SCULPTING_FOLIAGE_REFINEMENTS.md) for support tolerance, CPU/GPU validation and memory accounting. Sculpting remains session-only.

## October 15: session-only terrain and spawn recovery

Terrain sculpting now keeps edits only for the current world session. Restarting starts with the procedural baseline; existing disk saves are ignored and no terrain files are written. The elevated-spawn freeze was traced to successor coverage exceeding its derived-memory allowance. A bounded recovery releases optional extraction cache and prioritizes nearby edited visuals/collision while retaining accepted density and published collision. The HUD documents the session lifecycle. See [session and spawn fixes](Documentation/TERRAIN_SCULPTING_SESSION_FIXES.md).

## October 15: excavation stone, local shadows and vegetation

P6 A/B adds world-space stone on cut floors/walls/ceilings, two-sided local roof shadow casting and persistent grass/small-decoration suppression for resident and pending representations. The independent [ExcavatedStone appearance asset](Assets/Resources/TerrainSculpting/ExcavatedStone.asset) initially uses the existing stone texture and supports later albedo/normal/tiling replacement. Existing saves rebuild both appearance and suppression without a reset. CPU/save/performance regressions, 98 shader variants, native URP renders and GPU grass/fern checks passed. See [checkpoint 6 A/B](Documentation/TERRAIN_SCULPTING_CHECKPOINT_6_AB.md). Building support is deferred for review; cave ambient lighting remains after P6.

## October 15: sculpt playtest shutdown and latency fixes

Fixed sculpt HUD/outline teardown after Unity objects have already been destroyed and ensured world cleanup still reaches backend disposal. Corrected pathological seam-edge hash collisions, cached unchanged 4.8 m mesh-brick extraction, and localized assembly/publication to affected source-chunk windows. Connected-room benchmarks that previously took 2–46 seconds now complete in a few hundred milliseconds; heavy edited-scene p95/frame-time gates remain open. The existing density/save format and scene settings are retained. See [playtest fixes, budgets and validation](Documentation/TERRAIN_SCULPTING_PLAYTEST_FIXES.md). Foliage, exposed-stone materials and ceiling shadows remain P6.

## October 15: development terrain sculpt controls

P5 adds opt-in **F6** sculpt mode with a depth-tested sphere/box preview, **R** shape selection, **0.1 m wheel** size steps, LMB tap/hold excavation (maximum 4/s), and **F10** save/retry. Closest-solid terrain targeting, dry bounds, collision readiness, building/menu/focus coordination and fresh-click suppression protect interaction. The passive HUD distinguishes pending, rejected, unsaved and saved states. See [checkpoint 5](Documentation/TERRAIN_SCULPTING_CHECKPOINT_5.md) for Unity testing steps, native-input evidence and rendered captures. The saved scene remains off; P6 foliage/building/material integration and the existing live performance gate remain outstanding.

## October 15: persistent terrain excavation

P4 adds compressed immutable density records, checksummed world/profile identity, atomic manifests with a recoverable predecessor, background saving after two quiet seconds, explicit flush/retry APIs and orderly shutdown saves. Restart reconstructs terrain meshes and collision from the saved owners; startup and failure guards prevent unavailable edited terrain from becoming traversable. Persistent hosts reserve save-snapshot headroom within the existing 64 MiB payload budget. See [checkpoint 4](Documentation/TERRAIN_SCULPTING_CHECKPOINT_4.md) for the format, recovery policy, limits and validation. The scene toggle remains off; development controls remain P5 and P3's live performance gate remains open.

## October 15: sculpt transactions and streaming

P3 now stages complete density/render/collision swaps through the shared worker and frame budgets, rejects stale results, reuses unchanged surfaces, and enforces the approved 192/256 m horizontal and 64/96 m vertical visual transitions. Collision readiness, occupied/support pins, source/far handoffs and destination preparation protect movement. Native `ChunkManager`, source-chunk topology, guarded motor, delayed-work, nine-edit coverage and resource regressions are documented in [checkpoint 3](Documentation/TERRAIN_SCULPTING_CHECKPOINT_3.md). The feature remains opt-in/off in the scene; native edit latency still misses the provisional target, and the live walking/performance gate remains open. Persistence and development controls follow separately.

## October 15: sparse terrain meshing and collision adapter

P2 of the [terrain sculpting plan](Documentation/TERRAIN_SCULPTING_PLAN.md) now reconstructs exterior surfaces, tunnel walls and retained ceilings from the density field. Explicit collars join the original 0.3 m render grid and 2.4 m collider grid; filtered baseline meshes remove old blocking geometry. Compilation, topology/native-height seam checks, 16 actual player-motor traversals and a deep-cavity sparse-reconstruction fixture passed. Main-scene integration remains disabled; live streaming, transactions and performance measurement follow in P3. See [checkpoint 2 and the tested geometry preview](Documentation/TERRAIN_SCULPTING_CHECKPOINT_2.md).

## October 15: sparse terrain excavation foundation

Added the first authoritative density layer for the approved [terrain sculpting plan](Documentation/TERRAIN_SCULPTING_PLAN.md): 0.3 m samples, sparse immutable bricks, staged sphere/box excavation, stable snapshots, shared mesh halos and bounded payload admission. Canonical baseline sampling uses the existing native height pipeline. Runtime/editor/player compilation, 8,565 P1 assertions and the shared height-sampling regression passed; 108 native baseline samples matched exactly. This checkpoint does not enable sculpting in the scene; meshing, collision replacement, development controls and file persistence follow in later phases. See [checkpoint scope and validation](Documentation/TERRAIN_SCULPTING_CHECKPOINT_1.md).

## October 15: household furnishings concept catalog

Added the [F01–F24 furniture and household catalog](ArtReferences/IronAgeFurniture/README.md), matching the illustrated stone and wood sheets: seating, tables, sleeping platform, baskets/chest, textiles, pottery, tableware and small craft/decorative props, plus a furnished shelter study. The guide records proposed modeling sizes, fit with current 4 m bays, period references and future placement needs. These are concept references; runtime furniture assets are not implemented.

## October 15: shared height sampling cap of six

Detailed terrain, far heights, and the height sampling inside far control-map generation now share a fixed cap of **six simultaneous sampling tasks**. Short waves take turns across requests so those requests cannot multiply the limit. Request limits stay unchanged. Waiting occurs on producer threads; the Unity job workers remain available for other work such as Forward+ lighting. Sampling outputs matched the pre-change reference byte for byte, concurrency peaked at six across four producers, and compilation plus streaming/ownership/lifetime checks passed. Terrain may arrive later under load; route frame-time improvement still needs measurement. See [implementation, profiler labels and validation](Documentation/STREAMING_SCHEDULING.md#height-sampling-concurrency-cap).

## October 14: terrain texture submission cap

Terrain publication now submits at most **one complete control-map set per frame** by default (currently three textures), shared by near and far terrain. This limits bursts of graphics work even when main-thread submission fits the time budget. Extra current results remain queued with their native ownership intact; obsolete results can still be discarded, and collider/LOD publication continues. Adjust **Max Control Map Sets Submitted Per Frame** under World Manager's Terrain Streaming Budgets and restart Play Mode to apply. SmearScene is set to one. See [behavior, tradeoffs and validation](Documentation/STREAMING_SCHEDULING.md#terrain-texture-submission-cap).

## October 14: foliage setup and grass draw diagnostics

Foliage `CreateWork` and grass `Draw` now expose their internal setup and native operations: transform reads, object/iterator construction, material capability checks, buffer creation/replay/uploads, culling setup/dispatch/count copies, and GPU or CPU fallback draw submission. **Profiler Modules → First Settlers Grass** adds nine per-frame path/resource counters. This is instrumentation only. Runtime/editor/player compilation and isolated production-marker/counter observations plus existing foliage, grass-culling and shared-streaming checks passed. See [marker map and capture instructions](Documentation/STREAMING_SCHEDULING.md#foliage-setup-and-grass-drawing-breakdown).

## October 14: foliage publication stage markers

A captured 4.87 ms main-thread publication slice now has explicit kind and stage markers beneath `FS.Streaming.Foliage.PublishSlice`. `FS.Streaming.Foliage.Publication.*` separates input validation, grouping, batch allocation, transforms, commit/disposal, bush instantiation/setup/activation and rock construction/registration. Samples close before iterator yields; budgets and scheduling behavior are unchanged. See [capture instructions and marker map](Documentation/STREAMING_SCHEDULING.md#foliage-publication-spike-diagnosis).

## October 14: terrain resource ownership cleanup

Normal and macro terrain records now release generated meshes/textures, native ownership and managed inputs on disposal; replacements release obsolete assets safely across shared slots. Explicit inactive-chunk retirement protects coverage/handoffs and active foliage/grass readers, removes consumer references/results and keeps tree registry placements/states intact. Session-wide request tokens reject results for recreated records, worker failures clear matching request flags, and shutdown drains every result category while rejecting late completions. Publication cleans up partially created resources on failure. Automatic cache eviction and tree placement unloading remain deferred. See [terrain ownership and validation](Documentation/TERRAIN_OWNERSHIP.md).

## October 14: bounded control-map scratch reuse

Normal terrain-data workers now reuse the three padded control-map pixel buffers through a per-request-manager pool capped by the terrain worker limit. Buffers are cleared before reuse and returned after blending, including failures; output arrays remain independently owned. This avoids about 201 KiB of temporary pixel payload per normal chunk after warmup, at a maximum retained scratch cost of about 0.79 MiB with four workers. Different dimensions and excess borrowers use fresh scratch without growing the pool. Added `FS.Streaming.Worker.ControlMapBuild` and `ControlMapScratch` profiler markers. Compilation, byte-for-byte reuse/concurrency/lifecycle checks, streaming regressions and real terrain requests passed; warmed allocation events fell from 10 to 5 per build. GC-pause improvement remains unmeasured. See [GC pressure investigation](Documentation/GC_PRESSURE_INVESTIGATION.md).

## October 14: terrain-worker allocation lifetime correction

A supplied profiler/Console trace identified a TempJob mountain-mask buffer surviving seven frames in a terrain-data worker. Normal terrain-request scratch buffers now allow multi-frame lifetimes with explicit cleanup; mountain-mask and ecology inputs are released after their last consuming job. Height/climate retained-map scratch and snow scratch follow the same worker-safe ownership. Runtime/editor/player compilation, real-worker lifetime checks and streaming regressions passed. A separate far-geometry spacing assertion also failed with original allocation code; details are in the report. No automatic/incremental GC policy was changed. See [GC pressure investigation and capture plan](Documentation/GC_PRESSURE_INVESTIGATION.md) for allocation/retention findings and the tradeoffs of manual collection scheduling.

## October 14: shared conservative streaming allowance

Added a 2 ms shared main-thread allowance for terrain result publication, runtime/content work, foliage scheduling, and grass completion/uploads/dispatch. Content, foliage and grass reserve portions of that allowance; unused capacity returns to later phases. Collision results and attachment precede visual LOD work. Rotating first service prevents fixed-order queue starvation, completed-result backpressure slows dispatch when publication falls behind, and obsolete results are rejected before allocating Unity assets. Grass completion now visits active jobs rather than every resident tile. F3 reports measured streaming time, peak and overruns.

The limit remains soft: indivisible Unity operations and required reconciliation can overrun, while rendering, grass reconciliation, distant content, ambient/gameplay updates and worker execution remain outside the measured pool. Tighter budgets trade faster content arrival for more restrained main-thread work. Runtime/editor/player C# compilation and the isolated Unity scheduling, architecture, foliage, grass and Inspector regressions passed; live-world FPS remains unmeasured. See [scheduling, validation and tuning](Documentation/STREAMING_SCHEDULING.md). Restart Play Mode after import to apply the new configuration.

## October 9: player-relative building arrows and hold repeat

Arrows now map to the closest forward/right axes of the active build grid relative to the player's horizontal view: Down brings the preview closer, Up moves it away, and Left/Right follow the player's sides. Each individual arrow moves one 0.25 m grid step. Holds repeat after 0.3 seconds at 0.1-second intervals; LocalPlayerInput exposes a repeat toggle and timing settings. Menu/focus/disabled-input transitions clear holds, and opposing arrows cancel. Page Up/Down remain vertical tap controls. Runtime/editor compilation, direction/repeat correctness checks and native input regressions passed; see [building guide](Documentation/BUILDING_PROTOTYPE.md).

## October 9: continuous stair flights and wall-clearance snaps

Removed the repeated 0.25 m flat exit from W21's walking hull. It is now a continuous 1.5 m / 2 m ramp (6 vertices/8 triangles), so consecutive flights keep rising without horizontal shelves. Landing-to-ramp adhesion handles downhill entry without contact loss. A floor-top starter can shift backward one grid unit if its next flight would otherwise overlap a far border wall; the shift must preserve support and clear both pieces. Nudges remain one unit relative to the shown snap. Fully enclosed 4 m bays have only 3.5 m of clear run and still cannot contain two full flights without more space.

96 single/chained actual-motor traversal cases and rotated wall-clearance samples passed. The previous 0.25 m overlap was reproduced and cleared; tight-room, nudge and stable-preview cases passed. No existing records are moved, and the visual mesh, FBX, catalog, scene, material, shader and lighting asset were preserved. See [stair approach and samples](Documentation/W21_STAIR.md). Restart Play mode to refresh pooled colliders.

## October 9: W21 half-storey wood stair

Linked the uploaded stair as the seventh build option. Blender X/Y/Z dimensions 1.25/2/1.5 m become Unity width/height/run 1.25/1.5/2 m, with a lower-front-left origin and ascent along +Z. The 240-vertex/120-triangle visual mesh uses the existing wood material. A separate convex ramp has a zero-height toe and 0.25 m flat exit; stair-marked downhill adhesion eliminates contact loss without changing ordinary terrain behavior. Pooled bodies switch cleanly between ramp and box colliders. Straight stair continuations and high-end landings transmit support.

Compilation, 3,615 prototype checks, 7,276 stair checks and 48 actual-player up/down traversals passed, including near-side paths, rotated frames and 60/120 Hz simulation. Front/back/instanced rendering and the seven-option menu passed. The source FBX/importer, saved scene, wood material, shader and lighting asset were preserved. See [stair guide and previews](Documentation/W21_STAIR.md). Restart Play mode after Unity imports changes.

## October 9: two-sided wattle placement preview

Reproduced the missing weave in the green/red ghost: preview materials culled the thin sheets' back faces while solid rails stayed visible. BuildingController now uses cached two-sided preview copies when the authored component material is two-sided. Placed wattle cutouts/shadows and other component previews retain their existing behavior. Runtime/editor compilation and the GPU preview regression cover both colors, both sides, all eight headings, material reuse and cleanup. Legacy weave coverage failed in 16/32 views; the corrected preview passes all 32. See [wattle guide](Documentation/WATTLE_WALL.md).

## October 8: imported bay post replaces the plain corner

The fourth building option is now **Bay post**, using the imported quarter-metre post FBX and shared matte wood atlas material. Its 0.25 x 2.75 x 0.25 m bounds, origin, collider, corner/pillar snapping and support are preserved. Definition, mesh and prefab were renamed to bay-post assets with their original GUIDs and stable content ID, so existing references remain valid. The uploaded post has 24 vertices/12 triangles, retaining the room's geometry budget. Existing nearby/off-screen building shadow casting applies to it.

## October 8: enclosed-corner guidance, wattle surfaces and underside attachment

Corner stacking now resolves the real post at connected wall joints and uses a bounded guide volume above it. Adjacent wall tops can mask that guide without stealing the post target; unrelated obstacles remain blocking, and ordinary collision checks remain authoritative. Wattle now uses Surface wall placement across foundation/floor tops, retaining its verified lower-corner origin, mesh and collider. Bottom-face hits attach selected pieces below the target, including walls/pillars beneath roofs. Foundation repair and manual grid adjustments remain available.

## October 8: wattle wall and two-sided matte wood cutouts

Added the uploaded 3.5 x 2.75 x 0.25 m wattle wall as the sixth building option, preserving the prior five options and their order. Its one-material, 64-vertex/32-triangle visual mesh uses the shared wood atlas with a dedicated WattleWood material. The matte shader clips alpha consistently in forward, shadow, depth and depth-normal passes and supports both faces with corrected lighting normals. Existing wood remains opaque/back-face culled; the physical wall remains a full BoxCollider.

Runtime/editor compilation, 2,828 synthetic checks, the six-option menu, actual instanced rendering and GPU pass/shadow comparisons passed. The previous off-screen roof-shadow regression also passed. Source FBX/importer, opaque wood material, saved scene and lighting settings were preserved. The FBX's upper UV islands have a small authored offset relative to the request notes; the imported UVs were retained. See [wattle integration, previews and rebuild instructions](Documentation/WATTLE_WALL.md). Restart Play mode after Unity imports the assets.

## October 8: filler defaults and corner sky stacking

Two-plank infill now has an authored Infill placement mode: it faces the viewer on open surfaces, follows aimed wall ends at all side-face heights, and defaults across a pillar's viewed face rather than making a perpendicular corner. Top/above-top aim still stacks, and manual turns/nudges remain. Its length can straddle adjoining floor bays to fill their seam. Logical dimensions and colliders are unchanged.

Corner pieces now acquire bounded sky guidance from all four sides or their top. Looking above a previously hit corner/pillar previews a direct stack, with ordinary reach, collision, support and obstruction checks. No physical guide objects are created. See [building prototype guide](Documentation/BUILDING_PROTOTYPE.md).

## October 8: stable building shadows from off-screen roofs

Building batches outside the camera frustum now remain submitted as shadow-only casters while inside the existing 140 m shadow range. Visible geometry retains its frustum/3,000 m distance checks, and shadow range zero disables casting. This fixes camera rotation/movement removing the roof from the shadow map while the floor remains visible. No sun, cascade, material, collision or saved-scene settings changed.

A real-pixel regression reproduced the bug with four foundations and roof slabs, then verified that a fixed world point stays identically shadowed when looking down, turning forward and moving. Removing the roof or disabling shadows restores direct sunlight. Runtime/editor compilation and existing building render checks passed. See [shadow regression details and captures](Documentation/BUILDING_PROTOTYPE.md).

## October 8: two-plank infill building option

Added the imported half-metre two-plank wall as a fifth picker option, sharing the split-plank wall's matte shader/material and wood trim atlas. Its logical/collider dimensions are 0.5 x 2.75 x 0.25 m, with no end-post reservation, so it fills straight seams occupied by two quarter-metre corner plugs. The full wall and corner options remain. Import bounds, 2,110 placement/collider checks (including rotated seam snapping and bridge support), front/back/instanced rendering and the five-option menu passed. See [wood wall and infill guide](Documentation/SPLIT_PLANK_WALL.md). Restart Play mode after import.

## October 8: split-plank wood building wall

The building picker now uses the authored split-plank FBX in place of the plain wall, retaining the existing wall asset/content ID and 3.5 x 2.75 x 0.25 m logical box. The wood trim atlas is assigned through `SplitPlankWood.mat` and the new `Custom/BuildingWoodMatte` shader: diffuse sun/ambient lighting, shadows, additional lights, fog and the existing nighttime ambient-floor dimming, with no specular or reflection contribution.

The import is already aligned to the build mesh coordinates, with one submesh, 2,040 vertices and 944 triangles. Runtime/editor compilation, 2,072 synthetic placement/collider checks, all four shader passes, front/back captures and actual instanced pixels passed. The nine-piece room now has 3,836 triangles per geometry pass and retains three grouped submissions. No live-world FPS measurement was made. See [split-plank wall guide](Documentation/SPLIT_PLANK_WALL.md). Restart Play mode after Unity imports the changes.

## October 7: bounded sky guidance and precise wall attachments

Floor/roof previews now follow every quarter-metre height across a wall's full face. Aim above a previously hit wall to suggest a seated ceiling through a bounded sky guide; walls retain their end-extension and upper-half stacking directions, including stacking through sky guidance. Actual solid hits take priority, and leaving the guide region or changing context releases it. Wall extensions now touch directly without a forced two-plug gap; foundation corner slots remain.

Direct wall stacks advance 2.75 m; floors between wall courses add their 0.25 m thickness for 3 m storeys. A seated floor overlapping an existing upper wall is rejected with a specific explanation. No interior floor panel or relative-45-degree junction change was added. See [building prototype guide](Documentation/BUILDING_PROTOTYPE.md). Restart Play mode after compilation.

## October 7: aim-aware building placement and interior pillars

Upper-half wall hits now stack walls or centre a flat ceiling on the wall's full span, extending toward the viewed face with the whole quarter-metre edge seated on the wall. Top-face ceiling hits use the viewer's side; lower hits retain wall extensions or side platforms. The four inward faces of a closed room select the same ceiling footprint.

Corner pieces now follow the ordinary quarter-metre grid across floors/foundations for interior pillars. Lower wall ends retain a small corner snap; wall middles allow side pillars, and upper hits allow stacking. The HUD names the inferred placement action. See [building prototype guide](Documentation/BUILDING_PROTOTYPE.md). Restart Play mode after compilation.

## October 7: flush building corners and gameplay cursor capture

The wall now spans 3.5 units inside a four-unit bay, reserving quarter-unit corners. Added a plain 0.25 × 2.75 × 0.25 corner mesh/prefab with exact collision and a fourth picker option. Four walls plus four plugs close a foundation without overlaps or protrusions, including rotated local grids. Corners snap to foundation vertices and wall ends; paired plugs fill straight junctions. Returning from the picker or exiting building explicitly recaptures the hidden gameplay cursor and suppresses centering mouse deltas.

Added named building Profiler samples and documented CPU/GPU costs in [the guide](Documentation/BUILDING_PROTOTYPE.md). Synthetic room fixture: nine components, 108 triangles per geometry pass, three grouped instance submissions. Live-world FPS remains unmeasured. Restart Play mode after import for the new component dimensions and menu.

## October 7: building prototype

SmearScene now has a B-toggle component picker and build preview. Start with a foundation, then use Tab to select the plain wall or floor. Placement uses 0.25-unit grid increments, eight 45-degree headings, and contextual local frames with a small oriented-bound acquisition margin. Q/E or the wheel turns; arrows nudge; Page Up/Down changes height; left click places and right click removes.

The wall is 4 × 2.75 × 0.25 units with exact mesh and box-collider bounds. Pieces remain session records with instanced visuals to 3,000 units and pooled collision/query bodies at 32/40-unit activation/release ranges. Foundation-connected support has a three-second reconnection grace period. No disk saves or resource costs yet. See [building prototype guide](Documentation/BUILDING_PROTOTYPE.md) for asset locations, architecture, controls, validation, and limits.

## October 6: grouped world configuration and extracted coordinator services

`ChunkManager` now takes one configuration with named generation, coverage, worker, publication, content, rendering, scene and foliage groups. Existing WorldManager/TreeSettings serialized fields and inspector paths remain. `TreeSpeciesCatalog` centralizes all current near/billboard/datacard/habitat/name bindings; rendering, gameplay, query identity and worker snapshots share it.

Extracted terrain coverage/handoffs, completed-result publication and runtime pooling, plus foliage asset resolution, placement conversion, discovery and publication queues into state-owning services. Also corrected outgoing-runtime cleanup when no far replacement applies. See [WORLD_ARCHITECTURE.md](WORLD_ARCHITECTURE.md).

Runtime/editor/player compilation and isolated synthetic architecture plus foliage regressions passed (`.utmp/foliage-optimization/architecture-validation-final.log`). Menu: **Tools > Terrain > Validate World Architecture (synthetic)**. No saved scene, camera audit, Play mode or FPS benchmark was used. Restart Play mode for the new composition.

## October 6: shared grass culling, visible-water reflections and incremental foliage publication

Resident grass now selects all meadow/forest near/far lists in one compute dispatch per rendered chunk, preserving density, transitions, per-mesh/wind bounds and CPU fallback. World-owned water reflections skip frames without water meshes in the camera frustum and refresh immediately on re-entry; existing resolution/update rates remain.

Removed the bypassed grass queues/renderer/compute kernels and legacy tree billboard/GameObject rendering paths. Ground batches now build final arrays in bounded slices and publish by ownership transfer; bushes/rocks stage one object per step. Revision/ownership checks discard stale or cancelled work. Editor authoring adapters remain outside player builds. See [FOLIAGE_STREAMING.md](FOLIAGE_STREAMING.md) for ownership, budgets and remaining indivisible operations.

Runtime/editor/player C# compilation, Direct3D compute compilation and the isolated synthetic Unity suite passed (`.utmp/foliage-optimization/validation-final.log`). Menu: **Tools > Terrain > Validate Foliage Optimizations (synthetic)**. No live scene/camera audit or FPS benchmark was run. Restart Play mode before inspecting/profile comparison.

## October 2: extended, continuous clover range

Clover now renders independently of detailed grass. SmearScene and new settings use a three-chunk horizontal player radius (115.2 world units), with an outer half-chunk fade (96–115.2), replacing the grass-radius clamp and prefab material's 58-unit cutoff. Chunk gates use continuous player-to-chunk bounds with a mesh margin, so chunk crossings no longer toggle nearby diagonal colonies. A one-chunk prewarm prepares both placements and cached instanced batches ahead of visibility.

Tune **Clover Settings > Render Range > Active Ring Radius**, **Render Fade Width Chunks**, and **Pre Generation Ring Padding**. Radius/padding edits schedule bounded refresh work even while stationary; fade settings apply at draw time. Both current and legacy clover shaders receive runtime distance overrides without changing shared art materials. Placement density, forest habitat and shadow settings remain. More distant clover increases draw work and retained cache memory; no live FPS benchmark was run. Restart Play mode for initial scene/settings uptake.

**Tools > Foliage > Validate Clover Render Range** passed independent range, chunk seam/diagonal/negative-coordinate routing, prewarm batch eligibility, live setting refresh, actual instanced pixels beyond two chunks, partial/zero fading, height independence and material preservation for all four prefab variants, plus ground streaming and forest habitat regressions. Log: `.utmp/forest-floor/clover-range.log`. Runtime/editor compilation and 32 ordinary/instanced shader-stage checks passed.

## October 2: third fern LOD and leaf distance-density rings

Ferns now have a 52-triangle coarse LOD, with grass-aligned near/mid and mid/coarse transitions (11.52–23.04 and 24.96–51.84 units in SmearScene) and optional manual distances. Compute and CPU fallback select exactly one of three representations; placement frequency, plain art and 65-unit range remain. The checked-in [fern comparison](ArtReferences/ForestFloor/Fern_LOD_Comparison.png) shows actual LOD0/1/2 meshes at identical scale.

Leaves expose grass-style 3/6/10/14-subchunk density controls, defaulting to 1/.7/.4/.2. Candidate frequency increases from 6x to 12x for fuller nearby ground; stable density ranks thin distant rendering without reseeding/re-uploading placements as the player moves. Grass distance matching now uses an adjustable 0.8 multiplier: 115.2→92.16 units, with fade 81.41–92.16. GPU/CPU selection/pixel/fade parity, streaming, increased near population, seams and forest habitat regressions passed in isolated Unity (`.utmp/forest-floor/forest-distance.log`); shader stages and compute compiled. No live FPS benchmark was run. Restart Play mode. See [LEAF_CLUSTERS.md](LEAF_CLUSTERS.md) and [FOREST_FERNS.md](FOREST_FERNS.md).

## October 2: resident GPU leaves and ferns

Leaves and ferns now default to resident compute/indirect rendering. Stable per-instance transforms/tints/scatter data upload only when chunks add instances or storage changes; compute selects distance, frustum and near/far LODs. CPU fallback caches transforms and variation parameters. Leaf static variation is baked into a shared lookup texture, and hidden leaves collapse before rasterization. Habitat, art, geometry and render distances remain. Leaf depth/normal passes also now avoid alpha-to-coverage suppressing their outputs.

Actual near/far CPU/indirect pixel and fade parity, default routing/fallback, more than 1,023 instances, culling/LOD parity, chunk growth/eviction/slot reuse and unchanged-frame source uploads passed in isolated Unity (`.utmp/forest-floor/forest-gpu.log`). Fifty-four ordinary/instanced/procedural shader-stage checks and the compute kernel compiled. See [LEAF_CLUSTERS.md](LEAF_CLUSTERS.md) and [FOREST_FERNS.md](FOREST_FERNS.md) for settings, memory tradeoffs and the explicit **Validate Forest Scatter GPU Rendering** command. No live FPS benchmark was run. Restart Play mode.

## October 2: spruce octa impostor GPU rendering

The current `Spruce_OctaImpostor_Runtime` now supports the existing distant-tree compute/indirect path, including resident transforms/tints, GPU visibility/thinning/depth ordering, and load/near-handoff dither fades. The capability tag check now ignores case: Unity returned `true`, which the previous `True` comparison rejected. Camera-facing square corners have conservative bounds; authored crown footprints still determine ecological thinning. CPU instancing and standalone material use remain supported.

Actual-prefab routing, CPU/indirect pixel parity across three angles with varied TRS/tints, fades, bounds and the existing >1023-instance compute suite passed in the isolated Unity project (`.utmp/tree-check/spruce-gpu-final.log`). Eighteen ordinary/instanced/procedural shader compile checks passed. No live scene or FPS benchmark was run. The five-atlas lighting cost remains; CPU height/transition bookkeeping and small state uploads also remain. Restart Play mode. See [FAR_TREES_HANDOFF.md](FAR_TREES_HANDOFF.md).

## October 2: irregular leaf groups and broader, more visible ferns

Leaf candidates now use variable cell occupancy, full-cell offsets and world-space drift, with a halo for deterministic chunk ownership. Per-leaf pivots/IDs let the instanced shader vary leaf quantities, heading, scale and arrangement; the same nine-leaf stamp no longer repeats. Instance scale ranges from 0.55–1.5. The sixfold candidate budget remains, and the updated fixture achieved 161->949 placements (5.89x). The atlas artwork is unchanged.

Fern leaflets are roughly twice as broad, distant leaflets preserve both halves, and the fern range/settings expose an explicit **Fern Size Multiplier**, set to 2 in SmearScene. Spacing/density/moisture tuning substantially increases opportunities; the same authoring fixture increased from 18 to 138 plants. Mesh budgets are now 273/100 triangles. Runtime instancing and existing moss/forest exclusions remain. Tests and actual Linear render previews passed (`variation-unity.log`); no live frame-time benchmark was run. Restart Play mode for rebuilt mesh/settings uptake. See [LEAF_CLUSTERS.md](LEAF_CLUSTERS.md) and [FOREST_FERNS.md](FOREST_FERNS.md).

## October 2: moss grass exclusion, meadow insects, ferns and denser fallen leaves

Moss now blocks grass completely, with a margin for terrain control-map interpolation and matching near/billboard/fallback rules. Leaves can still accumulate on moss. Bees/butterflies require a cached grassland majority from a 5x5 chunk sample grid and grassland at their actual terrain probes; forest and mixed unowned chunks do not host them. The terrain shader now preserves litter substrate at rock transitions, removing the residual green rim from double-blended cover weights.

Added simple geometry-only forest ferns with instanced near/far meshes (273/60 triangles), uniform leaflet fills, moist shaded forest placement and bounded streaming. Leaf scatter candidate frequency is now 6x (+500%), confirmed at 155->938 placements in the same fixture. More instances/geometry add cost; performance remains unmeasured. Restart Play mode to regenerate. See [FOREST_FERNS.md](FOREST_FERNS.md) and [FOREST_FLOOR.md](FOREST_FLOOR.md) for settings, authoring renders and validation.

## October 2: moss preview/game color correction

The authoring clone used Gamma while the game uses Linear. Replaced the terrain shader's literal moss fill with a Color property so Unity converts the authored dark green correctly. Preview preparation now matches the game's color space, and the moss renderer requires Linear. Actual Unity before/after captures under identical lighting are in `ArtReferences/ForestFloor/Moss_Linear_Before.png` and `Moss_Floor.png`; the former reproduces the pale pre-fix result. Earlier Gamma captures are not exact live color references. All 24 terrain shader stages and the isolated Unity rendering passed. Restart Play mode to refresh chunk material copies. Global lighting, grass rims and generation are unchanged. See [FOREST_FLOOR.md](FOREST_FLOOR.md).

## October 2: moss integration and disconnected tree scale fix

The inspector's Tree Uniform Scale Range was unused by the world-feature planner: near and distant trees used hard-coded species sizes. It now passes through ChunkManager into shared worker settings and deterministically sets uniform scales for forest and meadow trees. The scene's existing 4–5 setting is preserved and now takes effect after restarting Play mode. Species, positions, counts, rotations, and placement clearances are unaffected; near GameObjects and distant CPU/GPU matrices consume the same planned scale. Reversed endpoints are normalized and invalid scales sanitized. Already-cached trees are not resized live; the inspector tooltip explains regeneration.

Moss now forms stronger continuous patches, dominates blended terrain through a shared coverage curve, uses the existing mixed/dense textures with reduced fine contrast/saturation, and suppresses grass/clover while retaining some fallen leaves. Moss blends last over eligible terrain, including overlapping rock at patch edges; steep vertical faces and snow/water surfaces remain protected. No new moss geometry or foliage renderer. The user explicitly asked to hold the grass-rim fix: the existing control-map smoothing and grass/litter boundary behavior remain unchanged. See [FOREST_FLOOR.md](FOREST_FLOOR.md) for costs, tuning, and the isolated authoring preview. No live scene audit, Play mode, or computer control.

Validation passed: 72 synthetic trees with inspector-to-near/sparse size transport, actual real-terrain near/distant placement parity, moss habitat/dominance and native grass/clover/leaf suppression, forest seams/far terrain/streaming regressions, runtime/editor C# and 24 terrain shader stages. The legacy distant-tree real-terrain test used a 200x height multiplier against 10x in the sparse path and omitted local moisture inputs; its fixture now matches production inputs. Actual Unity terrain/material preview saved to `ArtReferences/ForestFloor/Moss_Floor.png`. Log: `.utmp/forest-floor/moss-tree-unity2.log`.

## October 2: forest scatter, opening clover, and requested Unity previews

Replaced the five-leaf rosette with nine asymmetrically scattered, nearly solid-color leaves. The preserved atlas supplies alpha silhouettes only; no texture veins or grain appear. Near/far meshes use 72/16 triangles and complementary 18–30-unit LOD selection. Leaves now match grass's horizontal range automatically: 115.2 units in SmearScene, fading over 101.76–115.2. The extended radius increases candidate/cache/draw work; no performance improvement is claimed. GPU instancing remains DrawMeshInstanced with CPU culling, while grass retains its resident indirect path.

Forest grass blades are wider (1.6–2.8 cm) and lighter olive, at the same 42/7-triangle budgets. Fixed current URP Forward+/fragment-fog handling. Clover uses its existing instanced system in forest DarkGrass openings only, at reduced colony frequency and a 22-degree slope ceiling; deep litter/moss remains excluded. Restart Play mode to regenerate.

The user explicitly requested actual forest asset renders in this turn. Controlled offscreen Unity URP authoring renders are saved in [ArtReferences/ForestFloor](ArtReferences/ForestFloor), with matching before/after images, generated forest floor close-up/context, and arranged asset detail. They use actual asset meshes/shaders and generated foliage on a representative flat fixture; they do not reproduce the live world's complete lighting/terrain shader or measure performance. No Play mode or computer-control automation was used. This authorization applies to these authoring previews, not renewed automated live-scene audits. Generation/range/LOD/clover checks and runtime/editor compilation passed. See [LEAF_CLUSTERS.md](LEAF_CLUSTERS.md) and [FOREST_GRASS.md](FOREST_GRASS.md).

## October 2: separate simple forest grass tuft

Implemented the authorized step 1: a short, irregular forest-only tuft with separated blade roots, nearly uniform olive/dry-brown fills, and no detailed bitmap texture. New Resources prefabs supply 42-triangle near and 7-triangle distant meshes automatically; optional GrassSettings overrides are available. Forest and meadow draw lists share the existing resident GPU candidate arena and distance/density policy. Homogeneous chunks retain at most two grass draws; mixed chunks can use four. Leaf assets, ground texture, placement density, and grassland assets are unchanged. See [FOREST_GRASS.md](FOREST_GRASS.md) for assets, costs, and tuning. Restart Play mode; the user should inspect their actual views. No automated scene/camera tests.

## October 1: instanced forest leaf clusters

Added a ready-to-use five-leaf prefab, a painted four-leaf atlas, and a URP instanced shader. WorldManager now exposes Forest Leaf Clusters settings and automatically loads the default prefab. Placement favors quiet litter in forests, excludes steep/rock/wet terrain and object footprints, and renders within 28 units with a 20–28 fade. GPU instancing aggregates visible clusters across nearby chunks; no per-leaf GameObjects or new shadow casters. See [LEAF_CLUSTERS.md](LEAF_CLUSTERS.md) for asset paths, costs, tuning, and validation. Restart Play mode and inspect the user's actual forest views.

## October 1: forest floor redesign

Forest grass now follows a continuous, irregular density field with sparse interiors and fuller clearings. Litter remains beneath vegetation, with gradual soil/moss/mixed-litter blends in both detailed and distant terrain. The 45–50 degree wet slope band retains forest treatment, removing the meadow-density rims around rock faces while keeping the tree placement limit at 45 degrees. Step 3 adds the existing litter normal map with a distance fade and subtle world-space tone variation. See [FOREST_FLOOR.md](FOREST_FLOOR.md) for tuning, costs, and validation. Restart Play mode to regenerate; the user should check their actual views.

## September 12: streaming priorities 1 and 2

User authorized allocation/GC cleanup and ground foliage generation first; remaining priorities and profiler evidence are tracked in [todo.md](todo.md). Do not run scene/camera tests; the user will profile their own walking route. No terrain CPU/GPU refactor or broader tree rendering rewrite in this pass.

FoliageGenerator now exposes incremental flower/clover/dandelion discovery enumerators. Persistent native buffers survive frame boundaries; jobs are scheduled then polled without a main-thread wait, followed by 256-candidate collection slices. Native allocations are protected by finally (including setup failures); shutdown/cancellation completes before disposing. Original synchronous entry points remain for compatibility. Per-species revisions plus map/data identity checks reject cleared or replaced inputs. Generated flags only become true after collection finishes.

FoliageManager runs one active ground discovery request with FIFO pending work, bounded by the existing groundFoliageGenerationBudgetMsPerFrame and maxGroundFoliageGenerationsPerFrame controls. One step always progresses even if earlier foreground work exhausted its shared budget. Active work participates in deduplication/pruning and queue counters. Grass preparation queues clover instead of generating it synchronously; GrassStream waits for clover readiness. Clover requests needed for grass are allowed beyond clover's render range, and completion invalidates older grass candidates. Batch publication continues through the existing queue; rendering remains instanced.

Allocation reductions: transfer ownership of completed LOD triangle lists instead of list -> array -> list; reuse manager-owned flower/dandelion matrix/data scratch lists and per-prefab clover scratch lists; cache the grass priority comparison delegate. Final runtime render batches still own their arrays; these changes remove redundant intermediates rather than all generation allocations. Native-map setup, tree/bush/rock dependencies and existing ground render-batch construction can still exceed a slice budget. Profile before extending the work; do not claim that the screenshots establish every GC allocation source or that measured FPS has improved.

Validation: runtime/editor Roslyn compilation passed (existing BerryBushManager obsolete API warning). Final synthetic Unity suite passed (.utmp/erosion-check/ground-foliage-integration.log): all three discovery types defer scheduling, collect deterministic sliced output, reject clear/map invalidation, and support cancellation/retry. Queue integration passed repeated grass requests without duplicate clover, clover beyond its own render range, grass invalidation on completion, and progress with an exhausted shared budget. Test menu: Tools > Terrain > Validate Ground Foliage Streaming. No Play mode or camera audit was run.

## September 12: resident grass streaming completed

Implemented user-approved items 1-6: continuous player-distance/subchunk coverage; one deterministic candidate distribution for both meshes; persistent desired/data/display versions; aging priorities and independent completion/publication/scheduling budgets; complementary smooth representation transitions retaining old resident data during replacement; subchunk range uploads into chunk-wide GPU arenas. No new in-game debug overlay. Terrain pipeline unchanged.

Production path: ChunkManager calls FoliageManager.UpdateGrassStreaming with ALL active coordinates and actual viewer position. GrassStream owns candidate state/cache and async Burst discovery; ResidentGrassRenderer owns a shared source arena and separate near/far visibility/indirect-argument buffers. GrassCompact.CullResidentGrass performs per-candidate distance density, representation selection, edge taper and frustum culling. GPU draws are at most two submissions per rendered chunk, not per subchunk or per 1,023 candidates. CPU instanced fallback remains. Legacy grass helpers in FoliageManager/old validators are retained but production no longer routes grass through those queues.

Fixed the checkpoint's severe regression: Unity returned the material tag as lowercase true, while IsSupported required True. Use ordinal case-insensitive matching. The old check forced CPU fallback even on the RTX 5090. Also stopped per-frame CPU writes to GPU-owned argument buffers, retained separate representation buffers, and changed stationary membership reconciliation to 10 Hz (movement immediate). Cache survives renderer eviction/re-entry; settings/terrain/external ClearNearGrass invalidate versions and stale jobs cannot overwrite the desired revision. Empty discovery is a completed state.

Validation: final Unity GPU/state suite passed in .utmp/erosion-check/grass-final-validation2.log (bounded jobs and uploads, diagonal distance, eventual convergence, external invalidation, cache/re-entry, settings and in-flight replacement, empty results, GPU/reference selection, complementary transitions, partial slots, buffer recreation). Runtime compile passed with the existing BerryBushManager obsolete API warning. Earlier scene audits confirmed GPU usage and no pending grass work, but their camera/view/performance numbers are not authoritative for the user's view.

USER STEERING: Do not run automated scene/camera tests again; ask the user to test their actual views. GrassSceneAudit was removed. User reports about 200 FPS / 1,000 draw calls normally, 150 FPS / 1,500 in forests. Those are whole-scene figures; grass has two indirect submissions per rendered chunk, other foliage still has per-chunk/per-type batches and nearby tree/bush/rock GameObjects. Do not attribute the forest draw-call count to grass without a user-provided frame breakdown. See GRASS_STREAMING.md for controls and batch sizes.

## September 11 correction: broad sparse mountains, no terraces

User rejected the bench/terrace approach after seeing repeated horizontal bands. Removed all bench settings and height remapping. MountainSpatialScale defaults to 2.4, stretching both profile and region noise in XZ while keeping mountainRelief unchanged. MountainSparsity defaults to 0.12, raising only the lower mask threshold to reduce coverage while retaining a maximum mask of 1. Layout changes on regeneration. Existing rolling base land noise supplies hills at varying elevations inside larger mountain regions. GentleMountainErosion defaults to 0.45 on shallow base slopes, smoothly restoring full erosion on steeper faces; no altitude bands.

Retained lowland half-height and shoreline smoothing. LowlandHillVariation 0.8 and LowlandHillScale 900 add spatially varied positive relief boosts on dry land, with river carving last. Settings version 4 migrates the new fields while retaining prior lowland/erosion choices. Scene edits belong to user and remain untouched. CPU/GPU refactoring explicitly prohibited for this task.

Runtime/editor compilation passed (existing BerryBushManager obsolete API warning). Isolated Unity with synchronous Burst passed all checks: spatial scale preserves the isolated mountain height profile and divides gradients by 2.4; sparsity eliminated mountain mask at 2,608 sampled locations; 4,851 samples exhibited larger lowland hills; shoreline continuity and absence of mountain height remapping passed. Existing full near/far/macro/collider/seam suite passed across three seeds and custom settings. Log: .utmp/erosion-check/spatial-unity-validation.log. Previous bench slope statistics are obsolete. Visual appearance still needs live camera review; regenerate terrain.

## Current terrain system: whole-world erosion overhaul

User scope: erosion across the entire world, adjustable Inspector parameters and spatial scale, remove legacy fake mountain ruggedness, investigate circled stipple/striping artifacts, preserve flatter river-shaped broad valleys and rounded gully floors. Heightmap quality takes priority over old biome/slope placement rules. User deleted MountainDetailValidation and MountainErosionValidation during development to compile. They remain removed; WorldErosionValidation is the replacement.

Implemented:

- WorldErosionSettings: serializable snapshot on WorldManager; base elevation/relief/roughness, global erosion amplitude/wavelength/octaves, stretch/rotation/offset, gully/rounding/fade controls, minimum wavelength, maximum terrain vertex spacing. Regenerate Terrain Inspector button recreates the world with a consistent snapshot.
- WorldTerrainHeight: smooth rotated gradient-noise base with analytical derivatives, one world-coordinate erosion pass after base composition. Mountain width now expands the smooth broad mask; old summit stretching and max-unioned eroded copies are retired. Legacy sampling-array/anchor arguments remain compatibility plumbing but are not used to form terrain.
- Erosion settings are threaded through near requests, far/macro jobs, snow resampling and distant-tree placement. Existing final-height normals/slopes/collision stay coherent. Far meshes now respect maxMeshSpacing; near LODs cap spacing to a divisor of chunk size.
- Reproduced a real artifact source: the upstream cubic float hash collapsed 1,024 cells to ONE offset at seed 568317; integer replacement yields 1,024 unique offsets. Also smooth internal direction changes at crests and remove finite-difference input slopes.
- Fixed outgoing macro/incoming chunk overlap by suppressing incoming terrain/water until complete macro handoff, retaining coverage without simultaneous overlapping draws.
- Added riverValleyWidth / riverValleyFlattening; broad dry river valley shaping is applied after global erosion, while valleyRounding controls erosion gully floors.

Verification / current handoff:

- Runtime compilation currently passes (existing BerryBushManager obsolete API warning).
- Offline current-base test: 12,675 positions, eroded lowlands 7,531 / mountains 1,910 / seabed 1,275. Analytic-gradient comparison maximum error 1.85e-5; bounded global displacement and disabled/zero bypass passed. Temporary harness and preview in .utmp/erosion-check.
- WorldErosionValidation is added and passed in isolated headless Unity 6000.4.8f1 with synchronous Burst compilation. Covers 5,043 base/erosion samples (3,001 lowland / 763 mountain / 514 seabed changed; max derivative error 1.08e-5), off/zero bypass, real sampler valley shaping, 3 large hash seeds, handoff visibility, default/custom rotation/stretch/amplitude/spacing settings, near/far/macro/collider agreement and X/Z height/slope seams across three seeds. Logs: .utmp/erosion-check/world-unity-validation.log. Runtime/editor compilation passes.
- User opened/saved SmearScene with settings during work; their choices were preserved. Settings version 2 migrates only the newly added river valley controls in version-1 snapshots. Old mountain validation files remain deleted; use Tools > Terrain > Validate World Erosion.
- Generated hillshade inspected. The exact user screenshot has not been reproduced in the live scene. Remaining user-facing review: appearance at their camera and streaming/GPU frame cost with their chosen mesh spacing; do not claim measured performance improvement. See MOUNTAIN_DETAIL.md for the full current controls and behavior. No compute migration has been attempted.

## Historical notes: shared slope angles and mountain meadows

- `SlopeMap` now stores degrees: atan(raw height gradient * meshHeightMultiplier). The terrain request passes the active multiplier; far mesh/control samples and sparse distant-tree placement use the same conversion. Uniform worldScale cancels. Raw gradient maps remain derivatives for normals and MountainSnow.
- `TerrainSlopePolicy` centralizes biome decisions for managed, Burst near-map and far-control classification. The coarse far mesh uses the same policy with neutral climate because it has no climate grid.
- Forest canopy fades from 25 to 45 degrees; forest/tree eligibility ends at 45. Grass remains eligible through 63 degrees (old 0.01 at multiplier 200 is 63.43 degrees). Warm strong-mountain benches below normalized height 3 can become Grassland; moderate mountain shoulders can become meadows. Cold snow and high alpine terrain remain protected. Existing grass generation/density/ranking consumes the new Grass surface normally.
- Loose sand/mud limits are 40 degrees; cliffs begin at 70. Flower/clover/dandelion defaults and all three SmearScene values are 40 degrees. Forest floor exposure begins at 35 degrees. Debug overlay explicitly displays degrees. Existing slope-based ecology checks now use degree thresholds.
- Runtime and editor C# compile checks passed (existing BerryBushManager warning only). Twelve executable checks passed against the actual shared policy and Unity.Mathematics, covering conversion, multiplier changes, forest/grass boundaries, cold/alpine/cliff/water exclusions. Also available via Tools > Validation > Terrain Slope Policy. Updated old validation fixtures to degrees. Unity scene visuals and Burst runtime execution still require validation; restart Play mode to regenerate cached terrain/ecology.


## Terrain handoff hole fix

- Found an asynchronous coverage gap in `RebuildActiveChunkSet`: outgoing macro tiles and individual chunks were destroyed before budgeted replacement mesh work finished. This can recur at particular macro-tile boundaries at any travel speed and affects terrain and water together.
- Keep outgoing runtimes until all currently wanted replacement chunks have attached terrain meshes, or the replacement macro tile has attached its mesh. Generated-but-unapplied data does not count as ready. Outgoing individual chunks stop foliage and collider work. Runtimes outside the desired coverage are released; reversing direction reuses retained runtimes.
- Runtime C# compilation passed against Unity 6000.4.8f1 references (existing BerryBushManager obsolete-API warning only). Scene reproduction/benchmarking remains for Unity. The whole-world erosion overhaul above supersedes the temporary-overlap behavior by suppressing incoming terrain/water until the macro handoff completes.

## Earlier Task: Compute-Driven Grass

User authorized extending the successful tree approach to grass while preserving existing generation, density settings and selection-rank clumps.

### Implemented

- Added resident indirect rendering for near grass and billboard grass. Enabled in SmearScene through `GrassSettings.gpuIndirectRendering` and the assigned `grassCompactShader`.
- Generation, exclusion rules, biome data, seeds, scales, bucket sorting and density selection remain authoritative on CPU. Near grass retains each subchunk's sorted prefix, including the minimum-one rule at positive density. Billboard grass retains the same per-cell prefixes with deterministic fractional rounding. No new random ranking or density distribution was introduced.
- Factored the existing prefix-count calculations into shared helpers for correctness checks. Near-density edits now schedule reselection through the existing budgeted rebuild queue, including chunks returning onscreen after an edit.
- `GrassIndirectRenderer` uploads the existing selected matrices and instance data only when render data/mesh bounds change. It merges the old 1,023-sized batches into one indirect draw per chunk and representation.
- `GrassCompact.compute` performs wind-aware per-clump frustum culling and stable prefix/scatter compaction. It emits source indices rather than copying whole records; source order and rank-derived wind phase remain stable.
- The grass shader adds a procedural variant while preserving CPU instancing, forest tint, wind, fade/dither, normals and shadow reception. Culling uses the configured viewer camera; a missing camera disables per-clump culling.
- GPU counts stay on GPU during normal rendering. Buffer ownership follows the foliage root; clearing, disabling/destroying the root, switching off indirect rendering, or replacing assets releases/rebuilds resources.
- Unsupported shaders/devices, an unassigned compute shader, or a custom instance-data property use the existing CPU fallback.
- Trees, flowers, clover and dandelions retain their existing render paths.

### How this differs from trees

Grass has many more overlapping alpha-tested blades. Reduced CPU draw submission and GPU visibility work can help, but on-screen overdraw and wind/shading cost can still dominate. This implementation preserves CPU generation and rank-prefix selection; it does not move grass generation or the density selector to compute. It batches per chunk rather than globally across the world, retaining chunk streaming ownership and billboard fade behavior.

### Verification and Unity handoff

- Runtime and editor C# compilation passed against Unity 6000.4.8f1's generated references (the existing BerryBushManager obsolete-API warning remains).
- Offline Direct3D compilation passed for all three grass compute kernels and 12 grass forward shader stages: ordinary, CPU-instanced and procedural vertex/fragment variants, with fade off/on.
- The actual extracted prefix-count helpers passed 18,018 density/count combinations for monotonicity, zero/full density and bounded counts using equivalent managed Mathf operations.
- `git diff --check` passed.
- Run **Tools > Terrain > Validate Grass Compute (correctness only)** in Unity. This checks GPU ordering, nested density prefixes, forest/wind instance data, more than 1,023 clumps, growth/replacement/disposal, frustum rejection and wind bounds. GPU readback occurs only in this explicit validation command.
- Unity import, GPU correctness execution, scene visuals and benchmarks remain for the user. Compare `gpuIndirectRendering` on/off using the same warmed-up route and density settings. Inspect near/billboard transitions, clump identity while changing density, wind at screen edges and returning to streamed chunks.
- CPU profiler markers: `FS.Grass.IndirectUpload` and `FS.Grass.IndirectCull`. Upload work should occur on rebuilds, not every frame.
- Existing render geometry statistics report selected candidates, not post-culling indirect counts. Use the Frame Debugger/GPU profiler for actual draws and GPU timing.
- CPU fallback arrays are retained to allow switching paths. GPU capacity is retained until clear/dispose; very small batches may not gain from compute dispatch overhead.
- Other scenes must assign `Assets/Shaders/GrassCompact.compute` to the new grass setting to enable this path.

## Previous Task: Compute-Driven Distant Trees

Previous task: complete/fix the tree compute path. The user subsequently reported a noticeable latency/performance improvement and authorized extending the approach to grass.

### Current implementation

- `DistantTreeManager` retains authoritative chunk manifests, background placement, neighbor-aware ecology, terrain seating and near GameObject/collision handoff.
- `DistantTreeGpuBatch` keeps transforms, tints, selection priorities, ecology and conservative bounds in resident GPU slots. Static records upload only on registration, ecology changes or buffer growth. Each frame uploads a 16-byte height/load/handoff state per resident slot.
- `DistantTreeCompact.compute` performs per-tree range/frustum culling, density smoothing, stable thinning, coverage calculation and depth-band selection. Prefix/scatter passes compact in stable front-to-back bands without CPU sorting or unordered append behavior.
- Indirect argument counts stay on GPU. Each supported mesh/material uses one indirect draw, without the 1,023-instance limit.
- Buffer growth copies existing GPU density state. Manifest replacement/eviction recycles slots; settings/shader changes rebuild GPU resources; disposal releases all buffers.
- CPU instanced drawing remains available when disabled, unsupported, unassigned or using a material without the `DistantTreeIndirect=True` tag.

### Shader and correctness fixes

- Removed the reserved HLSL field name `matrix` and duplicate `instanceID` declarations that caused shader errors.
- Uses Unity procedural instancing (`SetupDistantTree`) and a conditional shader-model-4.5 requirement for indirect variants. Ordinary/CPU-instanced variants retain their original shader target.
- Shared `DistantTreeInstance.hlsl` supplies transforms (including inverse TRS for normals), tint access and per-instance fade data through Unity's instance ID.
- Removed the manually enabled indirect keyword that could break CPU fallback. Unity selects procedural variants for indirect draws.
- Added indirect support to the common billboard, birch, red maple, sugar maple and spruce variation shaders, plus the red maple/sugar maple/spruce/white pine LOD2 billboard shaders. Species-specific coloration is preserved; shaders with per-tree tint now read it from the indirect buffer. The common billboard now consumes its tint on both paths.
- Draw and culling bounds include scaled, rotated and offset geometry as well as upright camera-facing cards.
- Grass shaders, grass rendering and near-tree collision behavior were not changed.

### Local verification completed

- Runtime C# and editor C# compilation passed using Unity 6000.4.8f1's Roslyn compiler and this project's generated response-file references. One unrelated existing obsolete-API warning remains in `BerryBushManager`.
- Offline Direct3D HLSL compilation passed for all four compute kernels and 60 forward shader stages: ordinary, CPU-instanced and procedural vertex/fragment variants across all nine supported billboard shaders, including spruce's far-simple variants.
- These HLSL checks use the project's actual package includes and an offline Unity-style preamble; they do not replace Unity shader import, build-variant validation or scene rendering.
- `git diff --check` passed.
- No Unity scene visual checks or performance benchmarks were run in this pass.

### Unity validation handoff

1. Let Unity reimport and compile, and check the Console.
2. Run **Tools > Terrain > Validate Distant Tree Compute (correctness only)**. This explicitly checks shader variants, buffer strides, scaled/offset bounds, more than 1,023 instances, frustum rejection, ordering, tint/fade transport, thinning, zero visibility, density preservation during growth and slot reuse. The check intentionally reads GPU buffers; it does not run during normal rendering.
3. The existing **Validate Distant Trees (correctness only)** command remains available for placement, terrain sampling and stable identity checks.
4. In SmearScene compare compute enabled/disabled: every species, terrain seating, screen edges, near/far dither handoff, streaming, rapid turns and returning to previously visited chunks.
5. Benchmark the same route/camera, density, draw distance and warmed-up terrain in both modes. Record main/render thread time, GPU frame time, draw calls and memory. Allow transitions to settle after switching modes.

### Remaining limitations / benchmark considerations

- CPU still visits active trees for terrain seating and dynamic state submission; placement, ecology and near-tree lifecycle stay on CPU. This is not fully GPU-generated foliage.
- Resident buffers retain their peak capacity until disabled/recreated/disposed. Eviction reuses slots, but holes still incur inexpensive inactive compute work.
- Depth ordering is approximate per mesh/material, not a global sort across species.
- Runtime render statistics report submitted candidates, not the GPU-visible count. Use the Frame Debugger/profiler or the explicit correctness check when inspecting actual indirect counts.
- CPU and GPU maintain separate density histories. Switching modes rebuilds GPU buffers; allow the configured transition interval to settle before comparing screenshots or timings.
- Distant tree shadows remain disabled, as before.
- Grass now has its own implementation described above; the tree path is unchanged in this pass.
