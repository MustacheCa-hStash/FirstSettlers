# Original Spruce Settings

Snapshot of the materials currently assigned by `Assets/Prefabs/Spruce_LOD0_v06.prefab`. This document records the source state for impostor capture. The only intentional post-snapshot material change is disabling leaf wind amplitude for capture.

## Capture source

- Capture prefab: `Assets/Prefabs/Spruce_LOD0_v06.prefab`
- Source model instance: `Spruce_LOD0_P3310_B2930_L380_v06`
- Leaf material: `Assets/Materials/M_Trees/Spruce/SpruceTreeLeaf_M.mat` (`Custom/SpruceLeafTintCutout`)
- Bark material: `Assets/Materials/M_Trees/Spruce/SpruceTreeBark_M.mat` (`Custom/SpruceBarkSimpleLit`)
- The capture center and radius have not yet been measured; they will be derived from this prefab's combined renderer bounds in the capture scene.

## Leaf material

### Textures

| Property | Value | Scale / offset |
|---|---|---|
| `_BaseMap` | `Assets/Textures/Trees/leafCard_Spruce_03.png` | `(1, 1)` / `(0, 0)` |
| `_MainTex` | `Assets/Textures/Trees/leafClumpAtlas_Spruce.png` | `(1, 1)` / `(0, 0)` |
| `_BumpMap`, `_DetailAlbedoMap`, `_DetailMask`, `_DetailNormalMap`, `_EmissionMap`, `_MetallicGlossMap`, `_OcclusionMap`, `_ParallaxMap`, `_SpecGlossMap`, Unity lightmap/shadow-mask slots | None | `(1, 1)` / `(0, 0)` |

### Numeric properties

| Property | Value |
|---|---:|
| `_AlphaClip`, `_AlphaCutoutShadows`, `_AlphaToMask` | `1`, `1`, `1` |
| `_Cutoff` | `0.008` |
| `_Cull`, `_Surface`, `_ZWrite` | `0`, `0`, `1` |
| `_AmbientStrength`, `_InteriorShadeStrength`, `_LowerShadeStrength`, `_FauxShadeStrength`, `_LightWrap` | `0.3`, `0.2`, `0.22`, `0.36`, `0.45` |
| `_BacklightPower`, `_BacklightStrength` | `3.5`, `0.16` |
| `_ColorVariationStrength`, `_FineVariationStrength`, `_HeightColorVariation` | `0.38`, `0.18`, `0.24` |
| `_ColorHeightMin`, `_ColorHeightMax` | `0`, `8` |
| `_MacroVariationScale`, `_MacroVariationStrength` | `0.16`, `0.7` |
| `_NeedleContrast`, `_TipStrength`, `_LeafTintStrength` | `0.251`, `0.449`, `0.85` |
| `_LeafMaskThreshold`, `_LeafMaskSoftness` | `0`, `0.08` |
| `_Smoothness`, `_SpecularStrength`, `_Metallic`, `_Glossiness`, `_GlossMapScale` | `0.08`, `0.03`, `0`, `0`, `0` |
| `_ReceiveShadows`, `_SpecularHighlights`, `_EnvironmentReflections`, `_GlossyReflections` | `1`, `1`, `1`, `0` |
| `_UseVertexColor`, `_ForceBillboardFacing`, `_SampleGI` | `0`, `0`, `0` |
| `_WindStrength`, `_WindFlutterStrength` — original | `0.08`, `0.035` |
| `_WindStrength`, `_WindFlutterStrength` — capture state | `0`, `0` |
| `_WindSpeed`, `_WindFlutterSpeed`, `_WindGustScale` | `1.35`, `5.5`, `0.22` |
| `_WindHeightMin`, `_WindHeightMax` | `0`, `8` |
| `_BumpScale`, `_Parallax`, `_OcclusionStrength` | `1`, `0.005`, `1` |
| `_Blend`, `_BlendOp`, `_SrcBlend`, `_DstBlend`, `_SrcBlendAlpha`, `_DstBlendAlpha`, `_QueueOffset` | `0`, `0`, `1`, `0`, `1`, `0`, `0` |
| `_WorkflowMode`, `_SmoothnessTextureChannel`, `_BlendModePreserveSpecular` | `1`, `0`, `1` |
| `_ClearCoatMask`, `_ClearCoatSmoothness`, `_DetailAlbedoMapScale`, `_DetailNormalMapScale` | `0`, `0`, `1`, `1` |
| `_AddPrecomputedVelocity`, `_XRMotionVectorsPass` | `0`, `1` |
| `_Brightness` | `1` |

### Colors

| Property | RGBA |
|---|---|
| `_BaseColor` | `(0.8666667, 0.9098039, 0.8627451, 1)` |
| `_BaseNeedleColor`, `_LeafColor` | `(0.18431368, 0.35294116, 0.21176466, 1)` / `(0.18431373, 0.3529412, 0.21176471, 1)` |
| `_CoolNeedleColor` | `(0.13, 0.25, 0.22, 1)` |
| `_DeepNeedleColor` | `(0.055, 0.16, 0.09, 1)` |
| `_SunNeedleColor`, `_TipColor` | `(0.29999998, 0.45, 0.21999997, 1)` / `(0.3, 0.45, 0.22, 1)` |
| `_BacklightColor` | `(0.45, 0.72, 0.38, 1)` |
| `_WindDirection` | `(1, 0, 0.35, 0)` |
| `_Color` | `(1, 1, 1, 1)` |
| `_SpecColor` | `(0.19999996, 0.19999996, 0.19999996, 1)` |
| `_EmissionColor` | `(0, 0, 0, 1)` |

Other material state: instancing enabled; double-sided GI enabled; alpha-cutout keywords listed as invalid; `MOTIONVECTORS` pass disabled.

## Bark material

### Textures

| Property | Value | Scale / offset |
|---|---|---|
| `_BaseMap` | `Assets/Textures/Trees/Bark/SpruceBark.png` | `(1, 1)` / `(0, 0)` |
| `_MainTex` | `Assets/Textures/Trees/Bark/SpruceBarkTextureSeamless.png` | `(1, 1)` / `(0, 0)` |
| `_BumpMap`, `_DetailAlbedoMap`, `_DetailMask`, `_DetailNormalMap`, `_EmissionMap`, `_MetallicGlossMap`, `_OcclusionMap`, `_ParallaxMap`, `_SpecGlossMap`, Unity lightmap/shadow-mask slots | None | `(1, 1)` / `(0, 0)` |

### Numeric properties

| Property | Value |
|---|---:|
| `_AlphaClip`, `_AlphaToMask`, `_Surface`, `_ZWrite`, `_Cull` | `0`, `0`, `0`, `1`, `2` |
| `_AmbientStrength`, `_LightWrap`, `_FauxSideShadeStrength` | `0.18`, `0.22`, `0.258` |
| `_Brightness` | `2` |
| `_CrackThreshold`, `_CrackSoftness`, `_CrackDarkness` | `0.42`, `0.045`, `0.68` |
| `_RidgeStrength`, `_ColorVariationStrength`, `_VerticalGradientStrength` | `0.18`, `0.215`, `0.316` |
| `_Smoothness`, `_SpecularStrength`, `_Metallic`, `_Glossiness`, `_GlossMapScale` | `0.12`, `0.06`, `0`, `0`, `0` |
| `_ReceiveShadows`, `_SpecularHighlights`, `_EnvironmentReflections`, `_GlossyReflections` | `1`, `1`, `1`, `0` |
| `_UseVertexColor`, `_SampleGI` | `0`, `0` |
| `_BumpScale`, `_Parallax`, `_OcclusionStrength` | `1`, `0.005`, `1` |
| `_Cutoff` | `0.5` |
| `_Blend`, `_BlendOp`, `_SrcBlend`, `_DstBlend`, `_SrcBlendAlpha`, `_DstBlendAlpha`, `_QueueOffset` | `0`, `0`, `1`, `0`, `1`, `0`, `0` |
| `_WorkflowMode`, `_SmoothnessTextureChannel`, `_BlendModePreserveSpecular` | `1`, `0`, `1` |
| `_ClearCoatMask`, `_ClearCoatSmoothness`, `_DetailAlbedoMapScale`, `_DetailNormalMapScale` | `0`, `0`, `1`, `1` |
| `_AddPrecomputedVelocity`, `_XRMotionVectorsPass` | `0`, `1` |

### Colors

| Property | RGBA |
|---|---|
| `_BaseColor` | `(0.408805, 0.3377018, 0.2789644, 1)` |
| `_RidgeColor` | `(0.42, 0.27, 0.13, 1)` |
| `_CreviceColor` | `(0.028, 0.018, 0.01, 1)` |
| `_CoolShadowColor` | `(0.13, 0.095, 0.06, 1)` |
| `_Color` | `(0.24, 0.15, 0.075, 1)` |
| `_SpecColor` | `(0.19999996, 0.19999996, 0.19999996, 1)` |
| `_EmissionColor` | `(0, 0, 0, 1)` |

Other material state: instancing enabled; opaque render type; double-sided GI disabled; `MOTIONVECTORS` pass disabled.
