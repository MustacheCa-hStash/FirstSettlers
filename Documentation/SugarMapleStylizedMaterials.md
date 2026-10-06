# Stylized sugar maple materials

New materials with the generated art are available in `Assets/Materials/M_Trees/SugarMaple`:

- `M_SugarMapleLeaf_Stylized.mat` uses `Custom/SugarMapleStylizedLeaf` and `Assets/Textures/Trees/SugarMaple/T_SugarMapleLeafClump_Stylized.png`.
- `M_SugarMapleBark_Stylized.mat` uses `Custom/SugarMapleStylizedBark` and `Assets/Textures/Trees/Bark/T_SugarMapleBark_Stylized.png`.

`SugarMaple_LOD0_v02.prefab` uses these materials. Its leaf texture now contains 31 affine copies of the supplied Kenney `sprite_0086.png`, preserving the sprite's five-lobed outline and existing shading. The first three-dominant-lobe card was reclassified as stylized red maple; its texture, material, shader and authoring deliverables are now under RedMaple names. Existing legacy maple shaders remain available. Baked impostors have not been replaced by this leaf-card change.

## Seasons and variation

The leaf material starts green with **Season Autumn Amount** (`_SeasonAutumnAmount`) at `0`. Increase it toward `1` to blend into the yellow/orange/red palette. The material exposes summer and autumn colors independently. **Soft Brightness Variation** is deliberately restrained, while **Autumn Palette Variation** allows more variation during fall.

**Leaf Gradient Contrast** amplifies the grayscale shading already painted into each leaf. **Leaf Gradient Midpoint (Linear)** centers it around neutral brightness so the texture does not simply darken all foliage. The tuned summer material uses RGB `(0.30, 0.57, 0.17)`, texture gradient strength `1`, gradient contrast `1.7`, and midpoint `0.68`. This provides brighter tips and darker bases without changing the alpha mask or increasing the ambient lighting floor.

`_TreeLeafTint` and `_TreeBarkTint` retain the existing property names used by tree property blocks. `StandingTreeLeafTint`/`StandingTreeBarkTint` read the project's `_StandingTreeLeaves`/`_StandingTreeBark` instance data. Existing generated sugar maple leaf tints are warm colors, so the new shader applies them only in proportion to `_SeasonAutumnAmount`; they do not turn summer trees orange. `_TreeTintStrength` controls their contribution. Bark tints remain multipliers.

For live testing, World Manager > **Tree Season Preview** overrides the authored season on existing cached material copies: `0` is summer and `1` is autumn. Turn it off to restore each material's own `_SeasonAutumnAmount`. This is a color preview, not a calendar/season manager. If integrating a new tree into the existing distant renderer, recapture or supply matching impostors rather than assuming the old baked atlas matches the new leaves.

## Rendering and import

Both shaders use URP Simple Lit lighting, the project's nighttime ambient floor, instancing and distant/standing-tree fade helpers. Leaves are two-sided and use alpha clipping with a `0.5` cutoff. RGB brightness is used for soft leaf shading; only the alpha channel determines visibility. Shared leaf deformation is used in forward, shadow, depth and depth-normal passes. Snow coverage uses the existing standing-tree instance snow value, with a material fallback. Bark uses its painted base color directly rather than interpreting it as a crack mask.

The leaf texture is sRGB, clamped, trilinear, mipmapped, with alpha transparency and coverage-preserving mipmaps at the same `0.5` cutoff. Bark is sRGB, opaque, repeating and mipmapped; its non-power-of-two source is resized to the nearest power of two at import. Both use 1K import limits, anisotropy 4, and high-quality BC7 compression on Standalone. Source PNG files remain intact.

## Validation

`Tools > Art > Sugar Maple > Create Stylized Materials` configures importers and creates missing materials without resetting existing artist edits. `Tools > Art > Sugar Maple > Validate Stylized Materials` compiles all four shader passes across instanced, crossfade, packed-normal and shadow/light variants, and renders GPU checks for alpha clipping, green summer, warm autumn, stable seasonal opacity, per-tree leaf/bark tinting, backfaces and wind.

Batch entry point: `SugarMapleStylizedAssets.RunBatch`. The same validation also compiles/renders the reclassified red maple leaf material and checks its texture/shader references. Render previews and validation logs are written under `Logs/SugarMapleStylized` and `Logs/SugarMapleStylizedValidationActual.log`.
