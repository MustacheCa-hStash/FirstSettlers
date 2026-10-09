# W21 half-storey wood stair

W21 is linked as the **seventh** build option, after the original six. The uploaded visual model and the smooth walking collider are separate assets.

## Dimensions and orientation

| Space | X | Y | Z |
| --- | --- | --- | --- |
| Blender | 1.25 m width | 2.00 m run | 1.50 m height |
| Unity | 1.25 m width | 1.50 m height | 2.00 m run |

Unity lower-front-left origin is **(0,0,0)**; bounds are **(0,0,0)..(1.25,1.5,2)**. The flight ascends along Unity **+Z**. Its grid dimensions are **(5,6,8)** quarter-metre units, minimum zero, with no wall-end inset.

Source: [HalfStoryStair_1.25Wx1.5Hx2.0D.fbx](../Assets/Models/Buildings/Wood/Stairs/HalfStoryStair_1.25Wx1.5Hx2.0D.fbx). Unity inspection found **240 vertices, 120 triangles, one mesh/submesh**, exact intended bounds and identity hierarchy transforms. The high tread is at the rear as required. The source FBX and importer were preserved; the derived runtime mesh retains authored UV0/normals. The existing opaque SplitPlankWood material/atlas is shared.

## Walking collider

[Side-profile diagram](../ArtReferences/IronAgeWood/W21-walking-profile.svg).

A **convex 6-vertex, 8-triangle hull** fills the wedge underneath the flight. It is not cooked from the detailed visual model and does not collide against individual tread risers.

- Toe: **Y=0 at Z=0**, with no vertical leading lip.
- Incline: rises continuously to **Y=1.5 at Z=2**.
- Exit: no built-in flat shelf; the next stair's toe meets this end with the same slope. A separate landing provides a flat area when wanted.
- Width: 1.25 m; bottom: Y=0; maximum envelope: 1.25 x 1.5 x 2 m.

The walking incline is approximately **36.9 degrees**, matching the visual flight's overall rise/run. The earlier 0.25 m flat exit was removed: it caused repeated vertical pauses during continuous flights and was not needed to join collinear ramps. Entry and exit still meet adjoining walking surfaces without a blocking riser. Placement reserves the full rectangular envelope; the solid wedge also blocks crawl-through underneath the open visual treads. A future open-under-stair collision design would be a separate change.

Pooled gameplay bodies use this convex MeshCollider for Stair parts. Their old BoxCollider is disabled; switching back to a wall disables the ramp and re-enables the box. Collider release disables both and clears query identity. No visual MeshRenderer is added to gameplay proxies.

The actual movement tests initially exposed brief downhill ground-contact loss despite successful traversal. A `SmoothWalkSurface` marker opts these ramps into slope-aware downward adhesion in CharacterMotor. It follows the downhill drop rather than relying only on the fixed 2 m/s ground-stick velocity. A short prospective probe handles the transition from a flat landing onto a descending marked ramp. The altered adhesion applies only when a marked ramp is detected; ordinary terrain/walls retain their existing behavior, and jumping retains its normal takeoff logic. The authoring prefab carries the same marker.

The collider approximates the rendered treads to suppress step bobbing. Exact third-person foot planting would need animation/IK rather than a per-tread movement collider.

## Placement and support

- Aim at a foundation/floor top to place the stair foot on that walking elevation. If the next identical flight would clip a border wall, the starter can move backward by one 0.25 m unit, provided the starter remains supported/unblocked and the continuation clears existing pieces. This aligns its end to the wall's inner face instead of the floor's outer edge. Manual nudges are applied after this fitted base snap, so each remains exactly one grid unit. Existing placed pieces are never moved.
- Aim at a floor's side to connect the stair's high end to the floor's walking elevation.
- Aim at an existing stair to continue a straight flight **2 m forward and 1.5 m up**. An explicit stair high-end/toe socket transmits support; ordinary unrelated edge/corner touching still does not.
- Select a floor and aim at a stair to put a landing beyond its high end at the same upper elevation.
- Underside aim retains the existing attach-underneath behavior.

Two straight W21 flights reach a 3 m storey over a 4 m run. A turn still needs a supported landing and suitable clear space. These changes do not create floor openings: a full floor slab in the stairwell will block the player. Plan headroom and an opening above the incline before enclosing it.

The wall-clearance reproduction used a 4 m bay with a far wall occupying Z=3.75..4. The previous starter at Z=0 sent the next flight to Z=4, overlapping the wall by 0.25 m. The corrected starter at Z=-0.25 ends the second flight at Z=3.75. This correction is not applied if the backward move collides with a near wall or loses support. Walls at both ends of a 4 m bay leave only 3.5 m of clear run: two full W21 flights cannot fit entirely inside that space. The placement hint reports that the next flight needs more clear run; an actual overlapping continuation is still rejected.

## Assets and rebuilding

- Definition: `Assets/Resources/Building/w21-stair.asset`, ID `build.wood.w21-half-storey-stair`, appended enum value `BuildPartKind.Stair` (existing values remain unchanged).
- Visual: `w21-stair-mesh.asset`, derived from the uploaded FBX.
- Walking hull: `w21-stair-collision.asset`, generated independently.
- Authoring prefab: `w21-stair.prefab`, one visual material and the convex walking collider.
- Import tooling: `Assets/Editor/W21StairSetup.cs`.

**Tools > Building > Prepare W21 Stair Import** prepares the definition/hull/prefab independently of a visual model. **Tools > Building > Link or Rebuild W21 Stair** verifies dimensions and ascending direction, bakes any hierarchy transforms, retains UV0, updates the derived visual mesh and registers the option once. The prototype installer includes the stair only when its source FBX exists. It never exposes an unlinked/null-mesh option in the picker.

The model belongs in `Assets/Models/Buildings/Wood/Stairs/`. Keep its current filename, metre scale, one wood material and authored UVs when re-exporting. Exported geometry must ascend along Unity +Z with the rear tread at Y=1.5. The importer validates that direction in addition to the bounding box; compensating only by rotating a prefab would not fix the raw instanced mesh. Restart Play mode after Unity imports changes.

The three existing BayPost, TwoPlankInfill and WattleWall source filenames were renamed to include their complete 0.25D suffix during this work. Their setup constants now follow the actual filenames; no source FBX is copied over or renamed by the stair setup.

## Validation

Run `Tools/ValidateBuildingPrototype.ps1 -StairChecks` in the existing isolated workflow. Runtime/editor compilation and the standard prototype checks passed, along with **48 actual CharacterMotor traversals**: up/down, three width positions including near-side paths, headings 0/45/90/315 degrees, and 60/120 Hz simulation steps. All traversals completed, with **zero airborne frames on the tested incline** and no stalled entry/exit. Tests also verify ray heights, bounds, lower/high-end placement, chain support/reconnection, landing attachment and pooled box/ramp switching. The saved player's controller settings are used: height 1.8, radius 0.4, slope limit 50, step offset 0.3 and skin width 0.08.

Log: `.utmp/building-prototype/stairs.log`. Rendering is checked separately with `-Render`, including the seven-option picker and actual instanced source mesh. This is synthetic validation, not a live-world FPS benchmark. Source model/importer, saved scene, opaque material, shader and lighting asset hashes were checked for preservation.

The continuous-flight/wall-clearance update passed **3,615 prototype checks** and **18,496 stair checks**, including **96 single/chained up/down traversals** at 60/120 Hz across rotated frames and near-side paths. The measured rise/run through the two-flight join stayed near 0.75, without a horizontal pause, and ramp-contact loss was zero. Snap tests reproduce the old 0.25 m wall overlap, verify the corrected position, repeated-validation stability and one-unit nudges, and reject a move into the near wall of a tight room. The visual mesh/material were not changed; the earlier render run passed front/back/instanced captures and the seven-option menu. Logs: `.utmp/building-prototype/stairs.log`, `render.log`.

- [Front preview](../ArtReferences/BuildingPrototype/W21StairFront.png)
- [Back preview](../ArtReferences/BuildingPrototype/W21StairBack.png)
- [Instanced preview](../ArtReferences/BuildingPrototype/W21StairInstanced.png)

