# L04–L09 limestone components

Six additional parts from the [limestone catalog](../ArtReferences/LimestoneBuildKit/README.md), accompanying the installed [L01–L03 walls](LIMESTONE_BUILD_KIT.md). Editable source, bake metadata and previews live in [ArtReferences/LimestoneBuildKit/Extension](../ArtReferences/LimestoneBuildKit/Extension/LimestoneExtension.blend).

| Part | Dimensions, metres | Menu category | Runtime triangles |
| --- | --- | --- | ---: |
| L04 square limestone pier | 0.50 × 3.00 × 0.50 | Posts | 12 |
| L05 limestone paving | 4.00 × 4.00 footprint; 0.25 deep | Floors | 12 |
| L06 limestone strip footing | 4.00 long × 1.00 wide; 0.50 deep | Foundations | 12 |
| L07 limewashed wall | 4.00 wide × 3.00 high × 0.50 thick | Walls | 12 |
| L08 limestone plinth course | 4.00 long × 0.50 high × 0.50 thick | Walls | 12 |
| L09 square limestone doorway | 4.00 wide × 3.00 high × 0.50 thick | Walls | 36 |
| **These six parts** | | | **96** |

L04 starts at its base and is centred in X/Z. L05 is anchored at its walking surface, with its body below Y=0; its X/Z footprint is 0..4. L06 is anchored at its top, spans X=0..4, Y=−0.50..0 and Z=−0.50..+0.50. Wall-like pieces start at their lower span endpoint on the thickness centreline. L07/L09 stack by 3 m. L08 adds a real 0.50 m rise: placing a full wall over it puts that wall's base at Y=0.50 and top at Y=3.50.

## Source geometry and surfaces

`Tools/BuildLimestoneExtension.py` creates editable closed stones, bedding and recessed packing, then bakes their unlit colour, ambient occlusion and bevel/joint normals to minimal runtime solids. L04 has eight square courses. L05 uses broad staggered slabs over packed bedding. L06 is a rough pale masonry strip. L07 uses a warmer, much lighter limewash material with a few exposed block faces and quieter joints. L08 has two low dressed courses.

L09 has one 1.75 × 0.50 × 0.50 m stone lintel at Y=2.25..2.75, with 0.25 m seats each side. Its **1.25 m-wide, 2.25 m-high aperture is real geometry**. Two short jamb solids and one full-width upper solid define both the runtime mesh and its three BoxColliders. The top quarter-metre course sits above the lintel. No door leaf, hinge, opening animation or wooden frame is included. Aperture-bottom wall sockets are omitted, so the void cannot act as a wall attachment line.

Each part has three coherent UV variants with unchanged dimensions and occupied geometry. Doorway reflection swaps the two jamb mappings as well as the header, leaving the opening unchanged. All eighteen FBXs use one submesh, metre scale, applied axis conversion and imported normals with Unity MikkTSpace tangents. The original individual stones remain in `LimestoneExtension.blend`; runtime relief is shading, with flat outer silhouettes as in L01–L03.

The six pieces share `LimestoneExtension.mat` and a separate 2048-square pair: `LimestoneExtension_ColourAO.png` and `LimestoneExtension_Normal.png`. This leaves L01–L03's existing maps/material untouched. Colour is unlit RGB, with AO in alpha; normals are tangent-space +Y. The atlas has 48 face islands with 16 px gutters/dilation and a 72 px outer packing border. Most faces receive 128 px/m, paving tops 192 px/m and concealed paving undersides 64 px/m. The same joint-free limestone trim region supplies the broad painted stone variation; no labelled guide art is sampled.

The existing matte stone shader supplies instancing, directional/night/fog/cave and local-flame lighting. Imports use trilinear mipmaps, Clamp, 4× anisotropy, BC7 colour/AO and BC5 normals on Standalone. Their combined texture payload is approximately 10.7 MiB with mips at those formats, shared by all six parts. No new shader or per-stone runtime draws are introduced.

## Footings and paving

L06 uses the existing terrain-bearing Foundation role. Like the prototype foundation, initial terrain placement seats its bottom on the hit surface: for terrain Y=0, its top/anchor is Y=0.50. It can also connect to supported construction. It is a solid strip rather than a 4 × 4 m tile.

L05 requires supported foundation or solid-paving coverage beneath its **entire** underside. One 1 m-wide strip cannot support the full 4 × 4 m tile. Four parallel L06 rows can collectively supply coverage, with centres at Z=0.50, 1.50, 2.50 and 3.50 and spans X=0..4. For an L05 walking datum of Y=0, strip tops must be Y=−0.25, matching its underside. Removing one row breaks the tile's full bearing. Existing timber floors do not count as solid paving support; L05 is not a suspended upper floor.

Authored foundations now honour `preserveAuthoredMesh` in foundation resolution. Paving snaps with its **underside** seated on an authored footing's top. The original prototype foundation retains its existing shallow finish-cap recess and original floor-seating behavior. This avoids replacing L06's baked limestone with a generated timber mesh or burying its receiving paving inside the footing.

Use non-overlapping butt/offset layouts for strip corners. General building overlaps remain permissive; intersecting strip meshes do not automatically become a bonded or merged masonry corner. A dedicated corner/foundation fitting pass remains separate from these straight catalog parts.

## Rebuild and verification

Run Blender with `--background --factory-startup --python-exit-code 1 --python Tools/BuildLimestoneExtension.py`. This regenerates only L04–L09's source, eighteen FBXs, shared maps and previews. Preserve later hand edits before regenerating. In Unity, use **Tools → Building → Install L04-L09 Limestone Components** for the scoped importer, or the existing full modular rebuild when intentionally rebuilding the whole catalog. Restart Play mode after import.

`Tools/ValidateLimestoneKit.ps1 -Extension -Install` compiles source and uses the isolated limestone validation project. It installs L04–L09 twice to verify stable GUIDs and catalog counts, checks every imported UV triangle/tangent/bound, checks closed solids and doorway mesh clearance, captures actual URP views, and exercises all six parts at eight headings. Play checks include solid/paving support and removal, rejected partial paving, the 0.50 m plinth rise, full authored footing retention, automatic paving height, eight actual TerrainCollider footings and sixteen CharacterMotor doorway crossings. L01–L03 and the ordinary-stone gameplay suite run afterward before the selective installer appends the six new catalog references.

Evidence: [extension checks](../ArtReferences/LimestoneBuildKit/Extension/unity-validation.json), [L01–L03 regression](../ArtReferences/LimestoneBuildKit/Extension/l01-l03-regression.json), [ordinary-stone regression](../ArtReferences/LimestoneBuildKit/Extension/stone-regression.json), [bake report](../ArtReferences/LimestoneBuildKit/Extension/bake-report.json), [Unity overview](../ArtReferences/LimestoneBuildKit/Extension/limestone-extension-unity-overview.png) and [instanced capture](../ArtReferences/LimestoneBuildKit/Extension/limestone-extension-instanced.png).

The final isolated run passed **3,404 L04–L09 checks**, **1,400 L01–L03 regression checks** and **3,544 ordinary-stone checks**. These include eighteen new imported UV layouts, forty-eight component/support cases, sixteen actual doorway walks, eight TerrainCollider footing cases and the existing sixteen gate walks. Front/reverse renders, the doorway and overview were visually inspected. Runtime/editor compilation passed with existing obsolete-API warnings. Installation appended six catalog references and retained the original pieces and maps; no saved world scene was changed.
