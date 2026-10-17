# Baked stone kit: minimal solids with shared masonry textures

The five installed stone pieces following S02 now bake their visible stones, joints and bevels onto simple closed solids. They use the existing baked-wall shader and a single shared colour/AO + normal texture pair.

| Piece | Original triangles | Runtime triangles | Geometry retained |
| --- | ---: | ---: | --- |
| S03 pier | 716 | 12 | One square pier |
| S04 flagstone ground floor | 2,168 | 12 | One shallow slab |
| S06 wall cap | 264 | 72 | Six separate capstones, with their original gaps |
| S07 low rubble wall | 852 | 12 | One full-span wall |
| S12 empty gate bay | 584 | 24 | Two independent wings; 1.25 m opening stays empty |
| **These five pieces** | **4,584** | **132** | **97.12% fewer triangles** |

With the previously baked [S02 wall](BAKED_STONE_WALL.md), the six-piece kit totals **144 triangles instead of 7,544** (98.09% fewer). The prototype stone foundation was already a 12-triangle box and retains its existing finish and geometry.

## Shared surface maps

`BakedStoneKit_ColourAO.png` stores unlit painted colour in RGB and baked ambient occlusion in alpha. `BakedStoneKit_Normal.png` stores tangent-space +Y normals. Source directional lighting and cast shadows are excluded from the colour bake. S07/S12 receive an 8 mm bevel on their temporary bake-source copies so their otherwise perfectly sharp joint edges can contribute readable normal-map detail; no bevel geometry is exported and neither source file is overwritten. AO affects ambient lighting; normal maps respond to the existing directional, additional and local flame lighting.

All five pieces share `BakedStoneKit.mat`, using `Custom/BakedStoneWallMatte`. The atlas is 2048 square, with 66 independently padded face islands and 16-pixel dilation. The floor top receives 224 pixels/metre; its concealed underside receives 64; the other faces receive approximately 192. Both atlases use mipmaps, trilinear filtering and 4× anisotropy. Standalone imports are BC7 colour/AO and BC5 normals: approximately **10.7 MiB including mipmaps for all five pieces combined**, shared across placed instances.

Each piece retains three deterministic UV variants with identical geometry. Mapping reflections operate coherently around each solid, so caps keep their individual widths and gate wings keep their openings. Normal tangents are calculated with MikkTSpace. No transparency, parallax, displacement, extra rendering passes or per-stone materials are introduced.

The original source geometry remains in `StoneBuildKit.blend` and `StoneBoundaryKit.blend` for rebaking. Their detailed FBXs remain available as visual references. Placed definitions and prefabs reference only the simplified baked exports. Logical bounds, content IDs, saved prefab/definition GUIDs, collision, sockets, terrain grounding, support and floor-bearing rules retain their existing contracts.

## Rebuild and verification

1. Run `blender --background --python Tools/BakeStoneKit.py` from the repository root. This reads the original sources without overwriting them, packs the shared atlas, bakes three maps and exports three variants per piece.
2. In Unity, choose **Tools → Building → Rebuild Baked Stone Kit** to update these five definitions, prefab meshes/materials and thumbnails. General stone and modular rebuilds also select the baked models. S02 retains its separate wall bake/rebuild command.
3. Restart Play mode if the catalog is already loaded.

Run `Tools/ValidateBuildingPrototype.ps1 -BakedStoneKitChecks` to install and verify in an isolated temporary project. Add `-InstalledAssets` to validate installed assets without rebuilding. The suite checks closed topology, exact triangle budgets, all tangent bases, three variants per piece, a shared atlas, six separate capstone solids, and an unobstructed gate mesh. URP captures compare original/revised meshes, normal-disabled lighting, opposite light, reverse/underside surfaces and the actual instanced renderer. It also runs the S02 render checks and existing stone gameplay regression suite across eight headings.

The outer faces of the pier, floor and low wall are now flat. Individual stone silhouettes, physical relief and per-stone cast shadows are texture shading. The capstone gaps and gate aperture remain actual geometry. This is the same geometry-versus-surface tradeoff approved for S02.

The final run passed **1,537 kit mesh/render checks**, **96 S02 render checks** and **3,535 stone gameplay checks**, including eight headings and sixteen actual gate crossings. It also exercised the full stone-kit rebuild path. Saved [kit checks](../ArtReferences/StoneBuildKit/BakedKit/unity-validation.json), [gameplay checks](../ArtReferences/StoneBuildKit/BakedKit/stone-regression.json), [instanced preview](../ArtReferences/StoneBuildKit/BakedKit/kit-instanced.png), [floor preview](../ArtReferences/StoneBuildKit/BakedKit/S04-baked.png), and [gate preview](../ArtReferences/StoneBuildKit/BakedKit/S12-baked.png).
