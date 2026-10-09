# Split-plank wood wall

The existing wall picker entry is now **Split-plank wood wall**, using the authored FBX and ordinary-wood trim atlas. Its `wall.asset` GUID, `build.prototype.wall` content ID and original catalog position are retained. The catalog also includes **Two-plank infill wall** as a separate fifth option. The previous cuboid mesh/material files remain available for the other prototype assets and historical references; the wall definition and authoring prefab now reference the imported model and matte material.

## Assets

| Purpose | Path |
| --- | --- |
| Authored model | `Assets/Models/Buildings/Wood/SplitPlankWall_3.5Wx2.75Hx0.25D.fbx` |
| Atlas | `Assets/Textures/Buildings/Wood/ordinary-wood-trim-albedo.png` |
| Material | `Assets/Materials/Buildings/Wood/SplitPlankWood.mat` |
| Custom shader | `Assets/Shaders/BuildingWoodMatte.shader` (`Custom/BuildingWoodMatte`) |
| Build definition | `Assets/Resources/Building/wall.asset` |
| Authoring prefab | `Assets/Resources/Building/wall.prefab` |
| Rebuild tool | `Assets/Editor/SplitPlankWallSetup.cs` |

The collider remains a BoxCollider with **size (3.5, 2.75, 0.25)** and **centre (1.75, 1.375, 0.125)**. The logical definition is still 14/11/1 quarter-metre ticks, minimum (0,0,0), with one tick of wall-bay inset at each end. The same definition dimensions supply pooled gameplay colliders, overlap checks, support connections and snapping. Fine plank bevels, height changes and rails are visual mesh detail.

## Two-plank infill

The new **Two-plank infill wall** uses `Assets/Models/Buildings/Wood/TwoPlankInfill_0.5Wx2.75Hx0.fbx`, the same `SplitPlankWood.mat` and the same atlas. Its definition is `Assets/Resources/Building/two-plank-infill.asset`, with stable ID `build.wood.two-plank-infill`; its authoring prefab is `two-plank-infill.prefab` in that folder.

Its logical dimensions are **0.5 x 2.75 x 0.25 m** (2/11/1 quarter-metre ticks), lower-corner minimum zero, collider centre **(0.25, 1.375, 0.125)**. `wallEndInsetUnits` is **0**: the entire 0.5 m length fills a straight seam, without reserving additional post slots. Its authored `wallPlacementMode` is **Infill**, with ordinary Wall collision/support and filler-specific default positioning. The imported mesh has **528 vertices, 244 triangles and one submesh**, already aligned to its logical box.

For the seam between adjacent full wall bays, remove the two existing 0.25 m corner plugs and aim near a full wall's end at any side-face height to extend the infill directly against it. Top/above-top aim still stacks. On open floors/foundations, the infill faces the viewer along the target's wall axes and follows the grid, allowing its length to straddle a bay seam. Beside a post, it defaults parallel to the viewed face rather than making a perpendicular corner. Selecting it for ground placement initializes its broad face toward the camera. Manual rotation and nudging remain available. Corner posts remain useful where walls turn; infill can connect both full wall ends and transmit support through the half-metre gap.

After re-exporting, **Tools > Building > Rebuild Two-Plank Infill Wall** refreshes just this definition/prefab and registers it once in the existing catalog. The full prototype installer includes it too. [Front/instanced preview](../ArtReferences/BuildingPrototype/TwoPlankInfill.png) and [back preview](../ArtReferences/BuildingPrototype/TwoPlankInfillBack.png) were checked. **2,110 placement/collider checks passed**, including both-direction seam snapping, rotated frames, no neighbouring overlap, bridge support and support loss after removal. Textured front/back and actual instanced render checks passed, along with the five-option menu at 720p/1080p. Logs: `.utmp/building-prototype/infill-import.log`, `infill-validation.log`, `infill-render.log`.

## Rendering and material

The shader uses textured diffuse lighting, a small diffuse wrap, subtle cool shadow tint, SH ambient lighting, additional lights (including Forward+), sun shadows and URP fog. It reuses the existing sun-cycle nighttime ambient-floor dimming from `TreeNightLighting.hlsl`. It deliberately has no specular lobe, metallic term, smoothness control or reflection-probe contribution, so moving the camera does not create a shiny surface sheen. Forward, shadow-caster, depth and depth-normal passes support GPU instancing; the material enables instancing for `BuildRenderer`.

The atlas is sampled using the authored UV0. Material tint starts at white; saturation and contrast default to 0.9 to soften the photographic grain slightly while keeping the actual texture. Brightness defaults to 1, daytime ambient floor to 0.18, diffuse strength to 0.85 and wrap to 0.08. These are starting art settings and can be adjusted in the material Inspector. Rebuilding preserves existing material tuning. The wall is fully opaque; alpha in the atlas's wattle region does not cut holes in this plank wall. The [Wattle wall](WATTLE_WALL.md) uses the shared shader and atlas with a dedicated two-sided cutout material; this plank/infill material stays opaque and back-face culled.

The atlas importer uses trilinear filtering and anisotropy 4, with its existing 2048 maximum size, sRGB, repeat addressing and mipmaps. No normal map or generated physical height map is added.

## Re-exporting from Blender

Keep one mesh/submesh, the lower-corner origin, metre scale and the existing FBX filename. On import the mesh itself must span **(0,0,0) to (3.5,2.75,0.25)** in Unity coordinates, with identity model transforms. The renderer draws the raw mesh; rotating a prefab to compensate for a bad FBX does not correct runtime placement.

After re-exporting/reimporting, use **Tools > Building > Rebuild Split-Plank Wood Wall** if the definition/prefab references need refreshing. The tool validates the import and updates only this wall's mesh/material/prefab references and fixed logical dimensions. The full prototype installer also reapplies the wood wall and imported bay post after creating their logical definitions and the plain floor/foundation assets. Bay posts share this wall's wood atlas/material while retaining their authored FBX UVs. Restart Play mode after importing changes to refresh existing render batches.

## Verification and previews

The current imported mesh has **2,040 vertices, 944 triangles and one submesh**. Its raw bounds and transforms were checked in isolated Unity before wiring the definition. Runtime/editor compilation passed with existing unrelated obsolete-API warnings. **2,072 synthetic checks passed**, including dimensions, physical/logical boxes, rotated frames, wall bays, support, collision and UI. All four shader passes compiled; front/back material captures and actual instanced rendering passed. Menu render checks passed at 720p and 1080p; near/far rendering and deletion checks passed.

- [Front](../ArtReferences/BuildingPrototype/SplitPlankWallFront.png)
- [Back with rails](../ArtReferences/BuildingPrototype/SplitPlankWallBack.png)
- [Instanced wall at 45 degrees](../ArtReferences/BuildingPrototype/SplitPlankWallInstanced.png)

The nine-piece room fixture contains 3,836 triangles per geometry pass and still uses three spatially grouped submissions. Shadow/depth passes add work; this is not an FPS benchmark. No saved scene or live camera was opened or changed. Logs: `.utmp/building-prototype/wood-import.log`, `wood-validation.log` and `wood-render.log`. Run `Tools/ValidateBuildingPrototype.ps1` for placement checks or add `-Render` for graphics checks; Unity licensing access may require execution outside a restricted sandbox.
