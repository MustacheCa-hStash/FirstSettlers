# Split-plank wood wall

The existing wall picker entry is now **Split-plank wood wall**, using the authored FBX and ordinary-wood trim atlas. Its `wall.asset` GUID, `build.prototype.wall` content ID and position in the four-entry catalog are retained. The previous cuboid mesh/material files remain available for the other prototype assets and historical references; the wall definition and authoring prefab now reference the imported model and matte material.

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

## Rendering and material

The shader uses textured diffuse lighting, a small diffuse wrap, subtle cool shadow tint, SH ambient lighting, additional lights (including Forward+), sun shadows and URP fog. It reuses the existing sun-cycle nighttime ambient-floor dimming from `TreeNightLighting.hlsl`. It deliberately has no specular lobe, metallic term, smoothness control or reflection-probe contribution, so moving the camera does not create a shiny surface sheen. Forward, shadow-caster, depth and depth-normal passes support GPU instancing; the material enables instancing for `BuildRenderer`.

The atlas is sampled using the authored UV0. Material tint starts at white; saturation and contrast default to 0.9 to soften the photographic grain slightly while keeping the actual texture. Brightness defaults to 1, daytime ambient floor to 0.18, diffuse strength to 0.85 and wrap to 0.08. These are starting art settings and can be adjusted in the material Inspector. Rebuilding preserves existing material tuning. The wall is fully opaque; alpha in the atlas's wattle region does not cut holes in this plank wall. A future wattle component needs its own cutout material/shader support.

The atlas importer uses trilinear filtering and anisotropy 4, with its existing 2048 maximum size, sRGB, repeat addressing and mipmaps. No normal map or generated physical height map is added.

## Re-exporting from Blender

Keep one mesh/submesh, the lower-corner origin, metre scale and the existing FBX filename. On import the mesh itself must span **(0,0,0) to (3.5,2.75,0.25)** in Unity coordinates, with identity model transforms. The renderer draws the raw mesh; rotating a prefab to compensate for a bad FBX does not correct runtime placement.

After re-exporting/reimporting, use **Tools > Building > Rebuild Split-Plank Wood Wall** if the definition/prefab references need refreshing. The tool validates the import and updates only this wall's mesh/material/prefab references and fixed logical dimensions. The full prototype installer also reapplies the wood wall after creating the plain floor/foundation/corner assets. Restart Play mode after importing changes to refresh existing render batches.

## Verification and previews

The current imported mesh has **2,040 vertices, 944 triangles and one submesh**. Its raw bounds and transforms were checked in isolated Unity before wiring the definition. Runtime/editor compilation passed with existing unrelated obsolete-API warnings. **2,072 synthetic checks passed**, including dimensions, physical/logical boxes, rotated frames, wall bays, support, collision and UI. All four shader passes compiled; front/back material captures and actual instanced rendering passed. Menu render checks passed at 720p and 1080p; near/far rendering and deletion checks passed.

- [Front](../ArtReferences/BuildingPrototype/SplitPlankWallFront.png)
- [Back with rails](../ArtReferences/BuildingPrototype/SplitPlankWallBack.png)
- [Instanced wall at 45 degrees](../ArtReferences/BuildingPrototype/SplitPlankWallInstanced.png)

The nine-piece room fixture contains 3,836 triangles per geometry pass and still uses three spatially grouped submissions. Shadow/depth passes add work; this is not an FPS benchmark. No saved scene or live camera was opened or changed. Logs: `.utmp/building-prototype/wood-import.log`, `wood-validation.log` and `wood-render.log`. Run `Tools/ValidateBuildingPrototype.ps1` for placement checks or add `-Render` for graphics checks; Unity licensing access may require execution outside a restricted sandbox.
