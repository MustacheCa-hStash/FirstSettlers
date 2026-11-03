# Distinct snow and rock materials

Implemented November 3, 2026. The terrain shader now resolves the existing filtered snow coverage into distinct snowfields and exposed rock, with a small soft boundary. The generated distribution maps and their slope/climate/shelter rules are preserved.

## Selected settings

The terrain material's **Snow** section exposes:

| Control | Default | Effect |
| --- | --- | --- |
| Snow / Rock Separation | 1 | Full coverage separation. Set to 0 to restore the previous continuous mixing, or use an intermediate value for a weaker effect. |
| Snow Coverage Threshold | 0.5 | Center of the snow/rock boundary. Increasing it exposes more rock. |
| Snow Boundary Width | 0.2 | At the default threshold, the nominal blend runs from 0.4 to 0.6 coverage. Smaller values make firmer boundaries. |

Both the shader defaults and `Assets/Materials/M_Terrain/M_TerrainBase.mat` use these values. The global Surface Blend Sharpness and WorldManager snow gamma retain their existing values. The threshold acts on the already filtered/render-boosted map weights.

The 0.2 width was selected after comparing the previous continuous mixing and widths of 0.2 and 0.06. It removes most of the broad faded coating while preserving a softer margin than 0.06. No additional boundary-noise field was needed in this pass; the existing generated distribution supplies the contour variation.

## Shader behavior

`Assets/Shaders/S_Terrain/CustomStylizedTerrain.shader` performs the remap after sampling and normalizing the control maps, before either color or normal blending. It computes snow's share of **snow + rock + cliff**, resolves that share through a narrow smoothstep, and returns the complementary amount to rock/cliff in their original proportions. The combined material weight stays constant.

This affects both albedo and normal weights. A resolved snow interior uses its own snow material and normal rather than retaining an intermediate rock contribution. Snow normals still perturb the geometric terrain normal; lighting and mountain shape remain visible.

Grass, sand, mud and riverbed weights are untouched. Forest cover support is untouched. Snow-on-grass dusting with no rock/cliff support retains its existing weight. This isolates the change to the requested snow-versus-rock/cliff distinction rather than sharpening every surface pair.

A screen-space derivative sets a minimum transition margin for subpixel edges. The margin is bounded so pure rock and pure snow stay pure, including extreme exposed threshold/width settings. This reduces edge aliasing at distance without adding animated or screen-door dithering.

No runtime terrain-generation changes, new texture fetches, geometry, shader keywords, or rendering passes are required. The fragment shader adds weight-remapping arithmetic and a derivative. A live FPS/GPU timing comparison was not performed. Restart Play mode to refresh any cached terrain-material copies.

## Controlled comparisons

The following captures use identical generated terrain, control-map values, material textures, cameras and lighting. The fixtures use seed 46 and both default and saved-scene landform settings. They are isolated Linear URP authoring renders, without the saved scene's vegetation, sky or fog.

| Fixture | Previous continuous mixing | Selected width 0.2 | Narrow width 0.06 |
| --- | --- | --- | --- |
| Default landforms | [Blended](../ArtReferences/SnowRockSeparation/default-blended.png) | [Selected](../ArtReferences/SnowRockSeparation/default-selected.png) | [Narrow](../ArtReferences/SnowRockSeparation/default-narrow.png) |
| Scene landforms | [Blended](../ArtReferences/SnowRockSeparation/scene-blended.png) | [Selected](../ArtReferences/SnowRockSeparation/scene-selected.png) | [Narrow](../ArtReferences/SnowRockSeparation/scene-narrow.png) |

Corresponding `*-top.png` files show the same terrain from above. The diagnostic [continuous](../ArtReferences/SnowRockSeparation/coverage-blended.png) and [selected](../ArtReferences/SnowRockSeparation/coverage-selected.png) gradients use red for rock and green for snow to make material mixing directly measurable.

## Verification

The isolated Unity run passed 536 production GPU pixel/normal/variant checks. The controlled coverage gradient contained 462 intermediate pixels before and 76 after out of 504 tested interior pixels, retaining a finite soft margin. Checks also covered:

- Conservation of snow/rock weight and preservation of rock-to-cliff proportions.
- Pure rock/snow endpoints, threshold movement and extreme exposed parameter combinations.
- Unchanged snow-on-grass dusting, snow-free surfaces and grass contribution in mixed pixels.
- Equal-albedo normal tests: low-coverage pixels match pure-rock shading, and high-coverage pixels match pure-snow shading.
- Thirteen warmed shader variants across rock/grass modes, main-light shadow modes and the shadow pass.
- All 136 terrain material inspector properties grouped correctly and existing inspector behavior preserved.
- Existing surface blending, water/dusting, X/Z seams and near/far snow tests across three seeds.

Full runtime, editor and player C# compilation also passed, with existing unrelated obsolete-API warnings.

Run from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tools/ValidateMountainSnow.ps1 -SeparationChecks
```

Log: `.utmp/mountain-snow/separation-validation.log`. The comparison renderer runs in the separate project at `.utmp/mountain-snow/unity-project`. Production controls are grouped by `TerrainMaterialInspector.cs`, with tooltips in `TerrainMaterialInspectorRules.cs`.
