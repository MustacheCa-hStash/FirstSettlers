# Baked split-plank wall

The 4 × 3 × 0.25 m split-plank wall now uses **136 triangles instead of 944** (85.59% fewer). Sixteen detailed board solids are merged into one thin closed panel that retains the twelve major top-height segments. Three rear rails remain separate simple boxes. Fine chips, board grooves, bevels and the selected painted wood grain are baked into the surface.

The original detailed FBX remains as `SplitPlankWallSetup.SourceModelPath`. Runtime `ModelPath` points to `SplitPlankWall_Baked.fbx`. The existing wall definition ID, prefab GUID, logical dimensions, sockets, BoxCollider and generated junction/bearing behaviour are retained.

## Baking and materials

`Tools/BakeSplitPlankWall.py` reads the original FBX and resolves the active trim texture from `SplitPlankWood.mat`. It applies the imported coordinate transform, extracts board heights and rail bounds, builds four simple solids, and projects the detailed source using Cycles selected-to-active baking. Original inputs are hashed and remain unchanged.

The shared 2048-square colour/AO and normal maps use approximately 192 pixels/metre. Colour RGB contains no directional illumination; alpha stores ambient occlusion. Normal mapping supplies dynamic edge/groove lighting through the same opaque baked-surface shader as the stone kit. The maps use mipmaps, trilinear filtering, 4× anisotropy, BC7 colour/AO and BC5 normals, approximately 10.7 MiB including mipmaps shared across all wall instances.

Generated bearing timber continues to use its existing trim UVs. To keep it correct, the baked atlas retains the original grain region (U=0..1, V=.75..1) and end-grain region (U=0...5, V=.25...5), with flat normals and full ambient visibility in those regions. Source RGB is retained within one 8-bit level after PNG round-trip. The baked shader falls back to the geometric normal for generated meshes that lack tangents.

`BakedSplitPlankWall.mat` is specific to this wall. Posts, wattle framing, floors, aperture walls, gables, infill, furniture and generated junction covers retain the original `SplitPlankWood.mat` and trim texture. General rebuild helpers explicitly distinguish the wall surface from shared trim material. Changing the selected wood texture requires rebaking the wall to capture its new appearance.

## Rebuild and verification

1. Run Blender with `--background --python Tools/BakeSplitPlankWall.py` from the repository root.
2. Use **Tools → Building → Rebuild Baked Split-Plank Wall** in Unity to update the wall and thumbnail. The existing split-plank and full modular rebuild paths also select the baked mesh/material.
3. Restart Play mode if the catalog was already loaded.

`Tools/ValidateBuildingPrototype.ps1 -BakedSplitPlankChecks` verifies the new wall in an isolated temporary project, then exercises a complete modular rebuild and the existing gameplay/render regression suite. Add `-InstalledAssets` to validate installed assets without rebuilding.

Captures compare original/revised geometry under identical light, the back and rail silhouettes, a close view, opposite light, normal-disabled rendering, and six rotated instanced walls with three procedural bearing bands. Tests also inspect tangent bases, logical bounds, prefab references and collider geometry. The modular suite covers post suppression/restoration, eight headings, wall/floor/roof fitting and actual character stair traversal.

The panel closes the shallow spaces between boards. Individual board sidewalls and tiny bevel silhouettes are baked shading; the stepped upper edge and rail outline remain geometry. The result stays opaque and does not add alpha cutouts or parallax.

The final run passed **252 wall asset/render checks** and **28,311 modular regression checks**, including **192 actual character stair traversals** and the full rebuild path. Saved [wall checks](../ArtReferences/ModularWoodKit/BakedWall/unity-validation.json), [modular regression](../ArtReferences/ModularWoodKit/BakedWall/modular-regression.json), [trim preservation](../ArtReferences/ModularWoodKit/BakedWall/trim-validation.json), [front preview](../ArtReferences/ModularWoodKit/BakedWall/wall-front.png), and [rear framing preview](../ArtReferences/ModularWoodKit/BakedWall/wall-back-and-end.png).
