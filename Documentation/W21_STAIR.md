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

A **convex 8-vertex, 12-triangle hull** fills the wedge underneath the flight. It is not cooked from the detailed visual model and does not collide against individual tread risers.

- Toe: **Y=0 at Z=0**, with no vertical leading lip.
- Incline: rises continuously to **Y=1.5 at Z=1.75**.
- Exit: a **0.25 m flat section** from Z=1.75 to 2, at exactly Y=1.5.
- Width: 1.25 m; bottom: Y=0; maximum envelope: 1.25 x 1.5 x 2 m.

The walking incline is approximately **40.6 degrees**, slightly steeper than the visual flight's 36.9-degree overall rise/run, because the final quarter metre is flat. It is below the saved player controller's 50-degree slope limit. Entry and exit meet adjoining walking surfaces without a blocking riser. Placement still reserves the full rectangular envelope; the solid wedge also blocks crawl-through underneath the open visual treads. A future open-under-stair collision design would be a separate change.

Pooled gameplay bodies use this convex MeshCollider for Stair parts. Their old BoxCollider is disabled; switching back to a wall disables the ramp and re-enables the box. Collider release disables both and clears query identity. No visual MeshRenderer is added to gameplay proxies.

The actual movement tests initially exposed brief downhill ground-contact loss despite successful traversal. A `SmoothWalkSurface` marker now opts these ramps into slope-aware downward adhesion in CharacterMotor. It follows the needed downhill drop rather than relying only on the fixed 2 m/s ground-stick velocity. This applies only to marked stair ramps; ordinary terrain/walls retain their existing behavior, and jumping retains its normal takeoff logic. The authoring prefab carries the same marker.

The collider approximates the rendered treads to suppress step bobbing. Exact third-person foot planting would need animation/IK rather than a per-tread movement collider.

## Placement and support

- Aim at a foundation/floor top to place the stair foot on that walking elevation, with the ordinary quarter-metre grid and manual turns/nudges.
- Aim at a floor's side to connect the stair's high end to the floor's walking elevation.
- Aim at an existing stair to continue a straight flight **2 m forward and 1.5 m up**. An explicit stair high-end/toe socket transmits support; ordinary unrelated edge/corner touching still does not.
- Select a floor and aim at a stair to put a landing beyond its high end at the same upper elevation.
- Underside aim retains the existing attach-underneath behavior.

Two straight W21 flights reach a 3 m storey over a 4 m run. A turn still needs a supported landing and suitable clear space. These changes do not create floor openings: a full floor slab in the stairwell will block the player. Plan headroom and an opening above the incline before enclosing it.

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

The linked model passed the existing **3,615 prototype checks** and **7,276 stair checks**. The 48 traversal cases all maintained ramp contact. The separate render run passed front/back captures, actual instanced near/far draws and deletion, and the seven-option menu at 720p/1080p. Log: `.utmp/building-prototype/render.log`.

- [Front preview](../ArtReferences/BuildingPrototype/W21StairFront.png)
- [Back preview](../ArtReferences/BuildingPrototype/W21StairBack.png)
- [Instanced preview](../ArtReferences/BuildingPrototype/W21StairInstanced.png)

