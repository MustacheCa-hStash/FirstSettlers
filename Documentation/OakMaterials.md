# Oak foliage and bark materials

`Assets/Materials/M_Trees/Oak/M_Oak_Leaf.mat` uses `Custom/OakLeafSimpleLitCutout` and `Assets/Textures/Trees/Oak/T_OakLeafCard.png`. The 1024×1024 RGBA source is copied unchanged from `C:/Users/samee/OneDrive/Desktop/TreeModels/krita/OakLeafCard.png.png`. Opacity comes from its alpha channel; the pale RGB gradient supplies leaf shading detail. Import uses sRGB, clamp, trilinear filtering, coverage-preserving mipmaps at cutoff 0.5, and desktop BC7 compression.

`M_Oak_Bark.mat` uses `Custom/OakBarkSimpleLit` and `Assets/Textures/Trees/Bark/T_WhiteOakBark_Stylized.png`, copied unchanged from `ArtReferences/Oak/white_oak_bark_v01.png`. The native source is 1254×1254; Unity imports a 1024px repeating, opaque sRGB texture. This is a painted base-color map, not a thresholded crevice mask. The bark shader shares the maple stylized bark implementation, including per-tree tint, lighting, night dimming, fog and snow support. Bark has no leaf-style transmission or autumn hue shift.

Both existing material GUIDs and shader names are preserved. `Tree_Blender_012_Oak.prefab` already references them; its leaf renderer now receives shadows. The newer `Oak_LOD0_P2557_B1800_L600_v02.fbx` explicitly remaps Blender `Material.001` to oak foliage and `Material.002` to oak bark, so direct instances of that imported model use the configured materials too.

## Color and season

`FoliageGenerator.GetDeterministicTreeColors` supplies forest and grassland oaks with placement-seeded autumn colors: ochre, russet/copper and muted wine. Bark gets a restrained ashy multiplier. Both near and distant placement use this function. Existing generated placements must be regenerated to receive the new species palette.

The leaf shader blends green summer foliage into its autumn palette and then blends toward the placement's intended autumn color. Summer retains a subtle per-tree brightness shift. Neutral white tint means no autumn override. Material colors are sRGB-authored; the leaf shader converts absolute instance palette vectors to linear in linear projects so standalone and instanced colors agree. Bark uses the same multiplicative tint convention as the maple bark shader.

The material defaults to autumn (`_SeasonAutumnAmount = 1`), matching the current legacy maple materials. Use **World Manager → Tree Season Preview → Simulate Tree Season → Summer to Autumn** for a live global override. The existing globals update cached materials immediately; turning preview off restores the material's authored value. Seasonal changes affect color, not the alpha mask or leaf geometry.

## Lighting

Oak foliage uses the maples' shared stable backlighting helper and supports Forward+ lighting. Backlighting is enabled at 0.72, with a nearly neutral warm tint; ambient floor is 0.30 and light wrap is 0.55. These are quieter than the current maple transmission settings. Wind sway is 0.10 and flutter is 0.025. Every leaf pass uses the same wind deformation and alpha threshold. Both shaders provide forward, shadow, depth and depth-normal passes, instancing, LOD fades and the shared tree lighting path.

## Validation

Run `Tools/ValidateOakMaterials.ps1`. It prepares an isolated Unity project under `.utmp/oak-materials/unity-project`, using cached local packages and copies of the relevant assets and runtime scripts. `OakMaterialValidation.RunBatch` checks texture import/assignment, both oak model bindings, deterministic forest/grassland colors, shader variants, summer/midpoint/autumn rendering, alpha coverage, instanced tinting, preview cleanup, two-sided backlighting and bark rendering. GPU previews are written to `.utmp/oak-materials/render-results`.

SmearScene now assigns `Oak_LOD0_v03` to both oak mesh categories and `Oak_OctaImpostor_Runtime` to both oak billboard categories. See [OakImpostors.md](OakImpostors.md) for the complete bake and validation workflow.
