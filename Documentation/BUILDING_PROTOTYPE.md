# Building prototype

The enabled SmearScene player has a BuildingController and BuildWorld on LocalControl. Enter Play mode after importing the new assets. Press **B**, choose **Stone foundation**, place it, then reopen the component picker with **Tab** to add floors or walls. Ordinary inspection remains available outside building mode.

| Input | Action |
| --- | --- |
| B | Enter building and open the component picker; exit if already building |
| Esc | Exit building and restore the previous cursor state |
| Tab | Reopen the component picker |
| Left mouse | Place a valid preview |
| Right mouse | Remove the directly aimed-at building piece |
| Q / E, or mouse wheel | Rotate by 45 degrees |
| Arrow keys | Nudge one increment along the active grid's X/Z axes |
| Page Up / Page Down | Nudge one vertical increment |

The picker releases the cursor and blocks movement/look input while gravity still runs. Choosing an item returns to a locked-cursor preview. The click used to select a preset cannot also place it. Green previews can be placed; red previews explain the failed check. Each click places one component. Tool damage can call BuildWorld.Damage(pieceId, amount); the prototype right-click is immediate removal.

## Geometry and authoring

One build unit is **0.25 Unity world units**, independently of terrain worldScale. Logical positions are integer units in a fixed grid frame. Yaw is one of eight 45-degree steps; ordinary components remain upright.

| Preset | X/Y/Z size in build units | Local bounds in Unity coordinates |
| --- | --- | --- |
| Plain wall | 16 / 11 / 1 | (0,0,0) to (4,2.75,0.25) |
| Timber floor | 16 / 1 / 16 | (0,-0.25,0) to (4,0,4) |
| Stone foundation | 16 / 2 / 16 | (0,-0.5,0) to (4,0,4) |

All three are deliberately plain, 12-triangle cuboids with shared URP Lit materials, no texture maps, and exact matching BoxColliders. The wall collider centre is (2,1.375,0.125), size (4,2.75,0.25). The wall origin is its lower start corner; floors/foundations anchor at the top walking surface. A wall plus a floor slab spans a 3-unit storey.

Definitions, meshes, materials, and authoring prefabs live in **Assets/Resources/Building**. The prefabs are authoring references; placed pieces are not instantiated from them. Replace a mesh in its BuildDefinition when importing Blender art, retaining its logical bounds and origin. Blender's Z-up must be converted to Unity's Y-up. Avoid changing root scale or deriving placement bounds from decorative mesh details.

## Placement and support

Independent placements use a world lattice. Foundation bottoms are quantized downward and can embed by at most one build increment at a designated contact probe. Only explicit WorldGroundSurface terrain or TerrainCollider surfaces create anchors; rocks, trees, and other props do not. ChunkRuntime marks its terrain root once when created.

A directly hit building piece supplies its own local grid. Ground hits within 0.10 units of a piece's oriented bounds may acquire that grid; the existing target releases beyond 0.15. Empty corners of rotated world AABBs do not attract snapping. Other obstructing objects do not acquire nearby building frames. Automatic contextual heading is separate from the manually selected ground heading, so leaving a 45-degree structure does not retain its automatic bias.

Wall-on-floor/foundation previews choose the nearest perimeter edge and face inward. Wall-on-wall previews extend an end or stack on top. Floor previews cover foundation tops, extend floor edges, or sit on wall tops. Selecting a foundation and aiming at a wall/floor puts the footing underneath it, allowing missing support to be replaced in the same diagonal frame. Arrow/height offsets are discrete and reset when changing targets or presets. Rotation is discrete both on the ground and within a target frame. A placed piece gets an immutable frame at its own origin/heading, allowing a diagonal wing to be extended accurately without rounding its endpoints back onto the world grid.

Placement checks the session spatial index using oriented-box overlap, then queries world/player colliders. Face contacts are allowed; penetration, player overlap, unsupported placement, buffer saturation, and nudging beyond 8-unit reach are rejected. Dimensions and support tolerances are separate from acquisition padding.

Foundations with valid terrain contact anchor the connection graph. Support flood-fills from all anchors and has no distance, weight, height, or material-strength decay. Connections require a face with positive overlap, rather than a bare edge/corner. Removing one of several anchors preserves alternate paths. A disconnected cycle does not support itself. Detached pieces have three seconds to reconnect before removal. Replacing support during the grace period cancels their removal. Streaming terrain colliders never revoke stored grounding.

## Session state and representations

BuildSession owns stable piece IDs, definition IDs, frame IDs, integer coordinates, rotation steps, health, grounding, and structural adjacency. Spatial cells are 8 world units and index all cells overlapped by each piece; they do not own the piece or its identity. Frames survive removal of their original piece. Session state lasts until its BuildWorld is destroyed, normally on leaving Play mode; no disk persistence is implemented.

Placed geometry uses Graphics.RenderMeshInstanced, grouped by component and spatial cell in batches of up to 500. Transforms are cached until records change; camera frustum/distance checks precede submission. The initial render range is **3,000 units**, with shadows limited to **140 units**. There is no density thinning. The simple cuboid meshes already have minimal geometry; hierarchical distant representations and GPU-resident culling remain future scaling work.

Nearby collision/query proxies activate at **32 units** from the piece's actual oriented bounds and release at **40 units**. At most eight activate per frame, nearest candidates first. Each pooled proxy has a BoxCollider and QueryTarget identity, no renderer, Rigidbody, or per-piece Update. Physical loading is independent of visibility and camera direction. Removing a piece disables its active collider immediately. Far rendering remains while these GameObjects unload.

For a later save format, serialize immutable frames and record fields using stable definition IDs and a schema/dimension-contract version. Resolve meshes/materials and rebuild spatial indexes/connections/proxies after loading. Nonserialized derived transforms and support flags are not save authority. Prune unreferenced frames during snapshot creation. Physics interests for distant NPCs/projectiles, bounded large-edit rebuilding, and multiplayer authority need separate extensions.

## Validation and current limits

**Tools > Building > Validate Prototype (synthetic)** tests the actual definitions, mesh/collider bounds, negative coordinates, 45-degree transforms/nudges, penetration, face contacts, cross-cell lookup, alternate supports, disconnected cycles, replacement support, damage, pooled identity, blockers, and picker lifecycle.

**Tools/ValidateBuildingPrototype.ps1** compiles runtime/editor code and runs a separate Unity project under .utmp/building-prototype. **-Render** renders the real plain wall material and menu at 720p/1080p, checks object-free runtime submission at near/far distances, and verifies deletion removes distant instances. **-InputChecks** uses native player input frames in a new empty synthetic scene to check B/E, idle wheel input, and modal look blocking. The ordinary geometry and render fixtures do not enter Play mode; the input fixture does. None opens or audits the saved world scene. Unity licensing/cache access may require running the command outside a restrictive sandbox.

The first kit has three presets, no resource costs, no save files, no debris animation, and no authored corner/roof/door variants. Plain cuboid wall intersections must avoid penetration; refined corners need matching geometry/pieces. Rebuilding render batches and recomputing support currently process session records after edits; unchanged frames retain cached batches. No measured FPS claim is made.
