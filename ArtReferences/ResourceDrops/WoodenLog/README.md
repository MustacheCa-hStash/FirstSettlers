# Ordinary wood item drop

Simple horizontal log for First Settlers, authored October 12, 2026. The silhouette is a slightly tapered twelve-sided cylinder, with softened outer cut edges, a narrow bark rim and painted end rings. No branch stubs or small decorative geometry.

## Deliverables

- [Editable Blender source](WoodenLogDrop.blend), with packed texture and separate preview camera, lighting and floor.
- [Portable GLB](WoodenLogDrop.glb), with embedded material/texture and just the log.
- [Unity FBX](../../../Assets/Models/Resources/WoodenLogDrop.fbx), with just the log mesh.
- [Unity material](../../../Assets/Materials/Resources/WoodenLog_MeadowTimber.mat), using the existing matte wood shader and meadow-timber atlas. The FBX importer remaps its material to this asset.
- [Three-quarter preview](wooden-log-preview.png), [reverse](wooden-log-reverse.png), [side](wooden-log-side.png), and [transparent 512-pixel icon](wooden-log-icon.png). These are renders of the actual model.
- [Geometry/export checks](model-report.json).

168 triangles, 86 geometric vertices, one mesh, one material. UV seams and split normals can increase the imported render vertex count. Bounds are approximately **0.76 m long × 0.291 m high × 0.315 m deep** in Unity. Unity Y up; length along X; unit scale; pivot at ground center for a resting drop. A bobbing or spinning drop should rotate a parent pivot lifted to the visual center, approximately Y = 0.146 m.

## Project references consulted

- [Ordinary wood direction](../../IronAgeWood/README.md): simple natural roundwood and hewn timber in the Iron Age inspired settlement kit.
- [Wood finish comparisons](../../IronAgeWood/Textures/StylizedSamples/README.md) and the actual current `SplitPlankWood.mat`: the active shared material references meadow timber. Reuses that exact atlas unchanged, with padded UVs in D (bark), E/F (cut ends).
- [Painted rock direction](../../Rocks/README.md): broad muted color patches, matte surfaces and minimal fine detail.
- [Implemented inventory](../../UIRedesign/ImplementedInventory/README.md) and its gameplay capture: readability against the bright meadow and stone inventory artwork.

The bark uses a short longitudinal crop of D to keep its painted plates broad. End rings use the quiet E/F meadow-timber artwork. No normal maps, photographic grain or newly generated textures.

## Validation and integration

Checked closed manifold topology, outward winding, padded atlas sampling, triangle budget, and one-material FBX round trip at metre scale. Visually inspected three angles and the transparent icon. Unity runtime/import validation has not been run; previews use Blender studio lighting, so the game's matte shader and lighting will affect the final appearance. No gameplay scripts, item definitions, pickup behavior, colliders, prefabs or existing scenes were changed.

Rebuild from the project root with Blender 5.0:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.0/blender.exe' --background --python Tools/BuildWoodenLogDrop.py
```

The script rebuilds mesh exports, packed Blender source, renders and the validation report. The supplied Unity material/importer metadata are retained independently.
