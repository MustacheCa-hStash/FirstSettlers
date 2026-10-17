# S02: baked masonry on a 12-triangle wall

S02 now uses one closed 4 × 3 × 0.5 m box: **12 triangles instead of 2,960** (99.59% fewer). Individual stones are retained only in the original Blender bake source. The placed wall has six flat faces, one opaque material and one submesh. Its definition ID, prefab GUID, dimensions, collision, sockets, support rules and 3 m stacking datum are retained.

## Texture and lighting contract

`Tools/BakeStoneWall.py` opens the existing `ArtReferences/StoneBuildKit/Models/StoneBuildKit.blend` without saving over it. It projects the 67 beveled source stones and packing core onto the box using Cycles selected-to-active baking:

- `S02_BakedWall_ColourAO.png`: unlit painted colour in RGB, ambient occlusion in alpha.
- `S02_BakedWall_Normal.png`: tangent-space bevel/joint normals, +Y convention.

The two shared 2048-square atlases map front, back, ends, top and bottom at 232 pixels/metre. Islands have 16-pixel dilation and padding. Both textures use mipmaps, trilinear filtering and 4× anisotropy. Standalone import uses BC7 colour/AO and BC5 normals, approximately 10.7 MiB combined including mipmaps, shared by every S02 wall.

`Custom/BakedStoneWallMatte` retains the current building shader's diffuse, fog, day/night, cave and local-flame lighting. Baked AO affects ambient light only. Tangent normals affect directional and additional lights, local flames, and the depth-normal pass. Shadow casting uses the box silhouette. No directional illumination, specular highlights, parallax or displacement are baked into the colour.

Three mesh variants use the same atlas: original mapping, whole-wall X reflection, and a 180-degree mapping around the vertical axis. Course heights remain aligned. Ends and horizontal surfaces transform with the wall pattern; normal-map tangents are recalculated with MikkTSpace. Position/heading hashing continues to choose a stable variant.

## Rebuild

1. Run `blender --background --python Tools/BakeStoneWall.py` from the repository root.
2. In Unity, use **Tools → Building → Rebuild Baked S02 Stone Wall**. This updates S02 and its thumbnail only. General stone/modular rebuilds also select the baked S02 assets.
3. Restart Play mode if the catalog was already loaded.

The old S02 FBXs and original `.blend` remain as authoring/reference data. The runtime definition and prefab reference only the new `S02_BakedCoursedWall` exports and `BakedStoneWall.mat`. S03/S04/S06/S07/S12 subsequently received their own [minimal geometry and shared bake](BAKED_STONE_KIT.md).

## Verification and appearance

`Tools/ValidateBuildingPrototype.ps1 -BakedStoneWallChecks` installs into a temporary isolated Unity project and checks the 12-triangle variants, tangent bases, bounds, prefab collision, texture imports and shader compilation. Actual URP captures include original/revised walls under the same light, a normal-disabled comparison, opposite light, close view, reverse/end view, and six joined/stacked walls through the real instanced renderer at a 45-degree heading.

Use `-BakedStoneWallChecks -InstalledAssets` to inspect the installed assets without rebuilding. `-StoneChecks -InstalledAssets` additionally exercises existing stone placement, support and gate collision fixtures. Results and render previews are saved under `ArtReferences/StoneBuildKit/BakedWall` in the validation project.

At the initial S02-only stage, the installed-assets run passed **96 asset/render checks** plus **88,178 stone regression checks**, including eight headings, wall/cap stacking, support removal, paving bearing, and sixteen real gate crossings. Saved evidence: [render checks](../ArtReferences/StoneBuildKit/BakedWall/unity-validation.json), [stone regression](../ArtReferences/StoneBuildKit/BakedWall/stone-regression.json), [revised wall](../ArtReferences/StoneBuildKit/BakedWall/wall-front.png), [original wall](../ArtReferences/StoneBuildKit/BakedWall/wall-original-2960-tris.png), and [joined/stacked instanced walls](../ArtReferences/StoneBuildKit/BakedWall/wall-joined-stacked-instanced.png).

The wall outline is rectangular. Mortar and individual stone bevels are shading detail; they do not change the silhouette, physical collision or individual-stone shadow geometry. Very close grazing views reveal the flat surface, which is the intentional tradeoff for eliminating runtime stone geometry.
