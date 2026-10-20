# White pine materials

The forest and grassland World Manager source is `Assets/Prefabs/WhitePine_LOD0_v01.prefab` (GUID `272060f130eda5545b5b78e8a9ef33d1`). Its existing material references are preserved.

- `M_WhitePineBark`: `T_WhitePineBark_Stylized.png`, opaque sRGB, repeat wrapping, trilinear filtering, mipmaps and desktop BC7. The existing 4 × 4 UV tiling is retained. Neutral material tint preserves the painted cool gray-brown color. Subtle brightness/height variation, low gloss, shared per-tree bark tint, snow, shadows, depth and normal passes use the same painted-bark implementation as maple and oak.
- `M_WhitePineLeaves`: existing white pine needle atlas and alpha cutoff 0.38. Needle RGB `(0.22, 0.39, 0.33)`, shadow `(0.09, 0.22, 0.18)`, tips `(0.43, 0.56, 0.45)`. Tip blend 0.22, strand contrast 0.22, card variation 0.035, ambient floor 0.30, stable backlight 0.32 with a near-neutral cool tint. Location tint uses the standing tree appearance block. Needles remain evergreen when the global autumn amount changes. Shared snow and night lighting remain active; wind settings are retained.

The bark shader keeps its original GUID and name `Custom/WhitePineBarkSimpleLit`. It now samples painted RGB directly instead of interpreting the image as a crack mask. The needle shader supports Forward+ and additional lights, fog, location tint, snow and soft two-sided transmission.

Generated with the built-in image generator. Original texture and complete prompt are in `ArtReferences/WhitePine/white_pine_bark_v01.png` and `white_pine_bark_v01_prompt.txt`. Sugar maple bark was the style reference. White pine morphology and needle color references: [Missouri Department of Conservation](https://mdc.mo.gov/discover-nature/field-guide/eastern-white-pine), [NC State Extension](https://plants.ces.ncsu.edu/plants/pinus-strobus/common-name/northern-white-pine/).

`WhitePineMaterialValidation.RunBatch` runs in the existing isolated material-validation project. It checks assigned prefab materials, opaque repeating sRGB bark import, shader variants, evergreen stability and standing-tree tint, and writes whole-tree/trunk previews. The approved materials are used by the [white pine billboard bake](WhitePineImpostors.md).
