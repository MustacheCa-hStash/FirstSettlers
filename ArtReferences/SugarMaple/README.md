# Five-lobed sugar maple card

`sugar_maple_card_v02.png` is a 1024-square RGBA card built from 31 copies of the supplied Kenney foliage sprite `sprite_0086.png`. Copies use uniform scale, rotation and translation only. The sprite's original grayscale shading is retained and multiplied by a subtle neutral tip-to-base gradient before duplication. Source alpha and silhouettes are preserved; no new leaves or veins were drawn.

- Square alpha coverage at cutoff 0.5: 64.900%.
- Coverage within the eight-sided card: 71.871%.
- `sugar_maple_card_v02.ora`: Krita-compatible editable layer stack.
- `sugar_maple_card_v02_opacity.png`: independent alpha mask, not the brightness gradient.
- `sugar_maple_card_v02_octagon.obj`: 8 vertices / 6 triangles, matching UVs.
- `sugar_maple_card_v02_square.obj`: 2-triangle square for comparison.
- `sugar_maple_card_v02.mtl`: matching material references; keep beside the OBJ and PNG.
- `sugar_maple_card_v02_layout.json`: source hash, placements and measured coverage.
- `sugar_maple_leaf_graded.png`: the shaded source before copying.
- `build_leaf_card.py`: deterministic reconstruction script.

The older three-dominant-lobe card was moved to `ArtReferences/RedMaple` and renamed `red_maple_card_v01*`. The original source file outside this project retains its historical filename; it was not renamed or modified.

The sugar maple bark files in this folder remain unchanged. Runtime sugar maple leaf material: `Assets/Materials/M_Trees/SugarMaple/M_SugarMapleLeaf_Stylized.mat`. The updated `SugarMaple_LOD0_v02.prefab` references that material.
