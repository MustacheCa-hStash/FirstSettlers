# Building prototype

The enabled SmearScene player has a BuildingController and BuildWorld on LocalControl. Enter Play mode after importing the new assets. Press **B**, choose **Stone foundation**, place it, then reopen the component picker with **Tab** to add floors or walls. Ordinary inspection remains available outside building mode.

| Input | Action |
| --- | --- |
| B | Enter building and open the component picker; exit if already building |
| Esc | Exit building and recapture the gameplay cursor |
| Tab | Reopen the component picker |
| Left mouse | Place a valid preview |
| Right mouse | Remove the directly aimed-at building piece |
| Q / E, or mouse wheel | Rotate by 45 degrees |
| Arrow keys | Nudge one increment along the active grid's X/Z axes |
| Page Up / Page Down | Nudge one vertical increment |

The picker releases the cursor and blocks movement/look input while gravity still runs. Choosing an item or exiting with B/Escape explicitly returns to a locked, hidden gameplay cursor. Capture is reapplied briefly after the UI transition and when application focus returns, rather than restoring a previously unlocked snapshot. The first two look samples after capture are suppressed to prevent a cursor-centering camera jump. The click used to select a preset cannot also place it. Green previews can be placed; red previews explain the failed check. Each click places one component. Tool damage can call BuildWorld.Damage(pieceId, amount); the prototype right-click is immediate removal.

## Geometry and authoring

One build unit is **0.25 Unity world units**, independently of terrain worldScale. Logical positions are integer units in a fixed grid frame. Yaw is one of eight 45-degree steps; ordinary components remain upright.

| Preset | X/Y/Z size in build units | Local bounds in Unity coordinates |
| --- | --- | --- |
| Plain wall | 14 / 11 / 1 | (0,0,0) to (3.5,2.75,0.25) |
| Timber floor | 16 / 1 / 16 | (0,-0.25,0) to (4,0,4) |
| Stone foundation | 16 / 2 / 16 | (0,-0.5,0) to (4,0,4) |
| Corner piece | 1 / 11 / 1 | (0,0,0) to (0.25,2.75,0.25) |

All four are deliberately plain, 12-triangle cuboids with shared URP Lit materials, no texture maps, and exact matching BoxColliders. The wall collider centre is (1.75,1.375,0.125), size (3.5,2.75,0.25). The corner collider centre is (0.125,1.375,0.125), size (0.25,2.75,0.25). Walls and corners use lower-corner origins; floors/foundations anchor at the top walking surface. A wall plus a floor slab spans a 3-unit storey. Corner pieces share the wall material, with no added bevels or intentional seams.

Each four-metre wall bay reserves 0.25 m at both ends: **0.25 + 3.5 + 0.25 = 4 m**. All four wall panels can be placed independently on the foundation's inward border. Quarter-metre corner plugs then fill the holes entirely within its footprint, in either placement order. The result is flush inside and outside, including when the whole foundation is rotated 45 degrees.

Definitions, meshes, materials, and authoring prefabs live in **Assets/Resources/Building**. The prefabs are authoring references; placed pieces are not instantiated from them. Replace a mesh in its BuildDefinition when importing Blender art, retaining its logical bounds and origin. Blender's Z-up must be converted to Unity's Y-up. Avoid changing root scale or deriving placement bounds from decorative mesh details.

## Placement and support

Independent placements use a world lattice. Foundation bottoms are quantized downward and can embed by at most one build increment at a designated contact probe. Only explicit WorldGroundSurface terrain or TerrainCollider surfaces create anchors; rocks, trees, and other props do not. ChunkRuntime marks its terrain root once when created.

A directly hit building piece supplies its own local grid. Ground hits within 0.10 units of a piece's oriented bounds may acquire that grid; the existing target releases beyond 0.15. Empty corners of rotated world AABBs do not attract snapping. Other obstructing objects do not acquire nearby building frames. Automatic contextual heading is separate from the manually selected ground heading, so leaving a 45-degree structure does not retain its automatic bias.

Wall-on-floor/foundation previews choose the nearest perimeter edge and face inward, with a one-unit end inset. Wall-to-wall extensions touch directly at either end, without automatically reserving space for two plugs. Foundation perimeter bays still reserve the flush corner cells. On an existing wall, aiming at its upper half or top face stacks a new wall directly above; lower-half hits extend its ends. These directions remain independent of the viewer's broad face.

Floor/roof previews on a wall face follow every quarter-metre elevation over the entire height, including the upper half. They centre on the whole wall span and end against the viewed face without penetrating it. A top-face hit or aim above the actual wall top selects a ceiling: its underside rests on the wall top, its edge covers the full quarter-metre wall thickness, and it extends toward the viewer. Top-face hits use the camera position to choose that side. In a four-wall enclosure, all four inward faces select the same roof footprint. Top attachment has 0.03 m hysteresis to prevent flickering while preserving the last side-platform height tick.

A real wall hit seeds a bounded sky guide for the selected wall or floor preset. If the ray subsequently misses all solid surfaces within reach, it can intersect an invisible extension of that viewed wall face. Floor/roof guidance extends 1 m above the wall; wall guidance extends by the selected wall's height. Both allow 0.15 m beyond the wall ends. These margins are adjustable on BuildingController. Looking higher inside this region keeps a roof seated or a wall directly stacked; it does not raise them into empty sky. Real surface hits take priority. Leaving the region, aiming away/parallel, exceeding reach, changing presets, opening the picker, exiting, or losing the source piece releases the guide. Guidance does not instantiate objects and cannot select a piece for right-click removal.

Directly stacked walls advance by 2.75 m. A floor-bearing storey advances by 3 m: 2.75 m wall plus 0.25 m slab. Place the slab first, then aim at its top to place the next wall. A seated slab cannot be inserted into a joint already occupied by a directly stacked wall; the preview explains that the floor edge is occupied. Side platforms remain available beside continuous walls. Existing pieces are never silently shifted and collision exceptions are not introduced. An interior-sized floor panel and relative-45-degree junction components remain future kit additions.

Corner pieces also work as pillars. Across foundations/floors they follow the ordinary quarter-metre X/Z grid over the entire surface, clamped only to its usable bounds; aiming near the surface corners still fills their flush cells. Lower wall hits within 0.15 m of an end preserve the corner-plug snap, while middle hits put a pillar alongside the viewed face. Upper wall hits place pillars along the top surface. Upper-half/top hits on existing pillars stack them; lower side hits allow adjacent plugs. Walls can attach to pillar faces or stack on them. The placement HUD reports the inferred action. Collision/support checks and arrow nudges still apply.

Selecting a foundation and aiming at a wall/floor puts the footing underneath it, accounting for the wall's bay inset so the repaired foundation retains its original footprint. Arrow/height offsets are discrete and reset when changing targets or presets. Rotation is discrete both on the ground and within a target frame. A placed piece gets an immutable frame at its own origin/heading, allowing a diagonal wing to be extended accurately without rounding its endpoints back onto the world grid.

Placement checks the session spatial index using oriented-box overlap, then queries world/player colliders. Face contacts are allowed; penetration, player overlap, unsupported placement, buffer saturation, and nudging beyond 8-unit reach are rejected. Dimensions and support tolerances are separate from acquisition padding.

Foundations with valid terrain contact anchor the connection graph. Support flood-fills from all anchors and has no distance, weight, height, or material-strength decay. Connections require a face with positive overlap, rather than a bare edge/corner. Removing one of several anchors preserves alternate paths. A disconnected cycle does not support itself. Detached pieces have three seconds to reconnect before removal. Replacing support during the grace period cancels their removal. Streaming terrain colliders never revoke stored grounding.

## Session state and representations

BuildSession owns stable piece IDs, definition IDs, frame IDs, integer coordinates, rotation steps, health, grounding, and structural adjacency. Spatial cells are 8 world units and index all cells overlapped by each piece; they do not own the piece or its identity. Frames survive removal of their original piece. Session state lasts until its BuildWorld is destroyed, normally on leaving Play mode; no disk persistence is implemented.

Placed geometry uses Graphics.RenderMeshInstanced, grouped by component and spatial cell in batches of up to 500. Transforms are cached until records change; camera frustum/distance checks precede submission. The initial render range is **3,000 units**, with shadows limited to **140 units**. There is no density thinning. The simple cuboid meshes already have minimal geometry; hierarchical distant representations and GPU-resident culling remain future scaling work.

Nearby collision/query proxies activate at **32 units** from the piece's actual oriented bounds and release at **40 units**. At most eight activate per frame, nearest candidates first. Each pooled proxy has a BoxCollider and QueryTarget identity, no renderer, Rigidbody, or per-piece Update. Physical loading is independent of visibility and camera direction. Removing a piece disables its active collider immediately. Far rendering remains while these GameObjects unload.

For a later save format, serialize immutable frames and record fields using stable definition IDs and a schema/dimension-contract version. Resolve meshes/materials and rebuild spatial indexes/connections/proxies after loading. Nonserialized derived transforms and support flags are not save authority. Prune unreferenced frames during snapshot creation. Physics interests for distant NPCs/projectiles, bounded large-edit rebuilding, and multiplayer authority need separate extensions.

## Validation and current limits

**Tools > Building > Validate Prototype (synthetic)** tests the actual definitions, mesh/collider bounds, negative coordinates, rotated transforms/nudges, penetration, face contacts, cross-cell lookup, alternate supports, disconnected cycles, replacement support, damage, pooled identity, blockers, and picker lifecycle. Placement checks cover every wall height tick on both faces, direct end contact, bounded sky rays, real obstruction priority, guide reset on preset/menu/target changes, seated roofs, and direct versus floor-bearing storeys. These use isolated fixtures; they do not move or inspect the saved world camera.

**Tools/ValidateBuildingPrototype.ps1** compiles runtime/editor code and runs a separate Unity project under .utmp/building-prototype. **-Render** renders the real plain wall material and menu at 720p/1080p, checks object-free runtime submission at near/far distances, and verifies deletion removes distant instances. **-InputChecks** uses native player input frames in a new empty synthetic scene to check B/E, idle wheel input, and modal look blocking. The ordinary geometry and render fixtures do not enter Play mode; the input fixture does. None opens or audits the saved world scene. Unity licensing/cache access may require running the command outside a restrictive sandbox.

The kit has four presets, no resource costs, no save files, no debris animation, and no authored roof/door or relative-45-degree junction variants. Rebuilding render batches and recomputing support currently process session records after edits; unchanged frames retain cached batches. No measured FPS claim is made.

The bounded sky-guidance/full-height placement pass passed runtime/editor compilation and **2,068 synthetic checks** in the isolated Unity project. Log: `.utmp/building-prototype/validation.log`. Aim transitions still need player review in the normal play view; no saved-scene camera audit or performance benchmark was run.

## CPU and GPU computation

On steady frames the CPU checks cached batch bounds against six frustum planes and the range limit, then submits surviving instance groups. It also checks active collider distances and candidate activation. The nearby spatial query and distance sort refresh approximately every 0.1 seconds, after one metre of movement, or when records change. While actively placing, one raycast, nearby oriented-bound tests, and a bounded nonallocating physics overlap query validate the preview. UI labels change only when their contents differ, although constructing the preview heading still allocates a small string per frame.

Structural support is an event-driven graph traversal, approximately O(N + E) for pieces and connections, without weight or joint simulation. The current prototype scans the session graph after edits and rebuilds all render batches after a revision, including health changes. Those edit-time costs and allocations can grow with large settlements; they are not paid continuously while an unchanged structure stands.

The GPU renders one shared indexed cuboid mesh per component definition at many transforms, with opaque depth testing and a plain Lit material. Each preset has 24 vertices and 12 triangles. A foundation, four walls and four corners contain **108 triangles per geometry pass**. In the isolated same-cell room fixture this produces **three instanced submissions** (foundation, walls, corners), rather than nine individual submissions. Shadow/depth/other pipeline passes add work. Shadows stop at 140 units while geometry remains visible to 3,000.

Instancing reduces CPU submission overhead; it does not remove per-instance vertex processing or pixel shading. The current path uploads visible instance matrices through RenderMeshInstanced each frame; it is not a GPU-resident compute-culling system. Opaque depth testing helps reject hidden fragments, but there is no building-specific occlusion culling, buried-face removal, or HLOD yet. CPU matrix arrays allocate 500 slots per batch, including unused capacity. Detailed custom meshes, extra materials, large edit bursts, and dense nearby colliders are the main scaling concerns to profile next.

Unity Profiler samples: **FS.Building.Rendering**, **FS.Building.RenderBatchRebuild**, **FS.Building.PlacementValidation**, **FS.Building.CollisionStreaming**, **FS.Building.Support**. The test counts and renders establish geometry, batching and behavior; they do not establish milliseconds or live-world FPS.
