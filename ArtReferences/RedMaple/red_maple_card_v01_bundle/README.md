# Red maple leaf-card sample

The sample contains 23 copies of the supplied `leaf_SugarMaple.png`, transformed only by uniform scale, in-plane rotation and translation. A neutral RGB gradient (235 at the tip to 198 at the base) is applied to the source before duplication. The original source alpha is preserved in the graded source. No new leaf silhouettes, veins or stems were drawn.

- `red_maple_card_v01.png`: 1024 x 1024 tintable grayscale RGBA texture; alpha cutoff 0.5 coverage is 66.668% of the square.
- `red_maple_card_v01_opacity.png`: standalone opacity mask. Grayscale RGB brightness is not the opacity mask.
- `red_maple_card_v01.ora`: editable OpenRaster stack for Krita, one layer per transformed leaf.
- `red_maple_leaf_graded.png`: graded source before duplication.
- `red_maple_card_v01_preview.png`: dark-background viewing preview, not the texture to import.
- `red_maple_card_v01_octagon.obj`: eight vertices, six triangles, UVs matching the PNG. Its area is 90.32% of a square; alpha coverage within it is 73.829%.
- `red_maple_card_v01_square.obj`: two-triangle square for comparison.
- `red_maple_card_v01.mtl`: material references for both OBJ files.
- `red_maple_card_v01_layout.json`: placement and coverage records.
- `build_leaf_card.py`: deterministic composition and export script, seed 73129.

Both cards are flat in XY, 0.6 metres wide/high, with bottom-center at the origin and front normal +Z. Scale, rotate and set their attachment orientation in TreeIt as needed. Copy the OBJ, MTL and PNG files together when moving them.

## TreeIt workflow

The developer documents imported OBJ/FBX/DAE meshes and attachment of meshes to leaf layers. The installed English control labels also confirm Add Mesh, Mesh Creation/Create, Mesh File/Load, Mesh Count, Mesh Position, Mesh Attachment and Attachment Layer. This sample has not been imported into a running TreeIt session.

1. Add a Mesh generator with **Add Mesh**.
2. Open its mesh creation/loading controls (**Create**, then **Mesh File > Load**, where present) and load `red_maple_card_v01_octagon.obj`.
3. Assign `red_maple_card_v01.png` in the texture set's Diffuse/base-color slot. Enable **Alpha Test** and **Double-Sided** under Mesh Texture. Use the standalone opacity file only if the application requests a separate mask.
4. In **Mesh Position**, set **Mesh Attachment / Attachment Layer** to the desired branch, twig or leaf generator, depending on the distribution you want.
5. Begin with a small **Mesh Count**. Adjust **Mesh Scale**, pitch/yaw/roll and position distribution; generate the tree and confirm the orientation and attachment pivot before increasing the count.
6. Compare with the supplied square card. The octagon removes 9.68% of rasterized card area before depth rejection while adding four triangles per instance; actual GPU performance must be measured.

Native **Diamond** is a simpler four-sided shape. **Overdraw Reduction** is the installed label for the leaf alpha-width reduction documented by the developer; a specific native automatic octagon workflow was not verified. Imported custom geometry is the confirmed route for this exact outline.

References: https://www.evolved-software.com/treeit/news and `C:/Program Files (x86)/Tree It/Sys/Language/English.txt`.
