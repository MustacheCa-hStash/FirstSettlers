# Terrain surface slope calibration

Updated November 3, 2026. All values below are **degrees**, measured from the terrain's height gradient with the active height multiplier. They are visual/ecological calibration values, not a slope-stability simulation or universal limits on where plants can grow.

Different materials need different rules. Loose granular material, rooted soil, saturated banks, organic litter, and adherent moss do not share one angle of stability. USGS describes talus repose angles of roughly 30–40 degrees in its [surficial-deposits study](https://pubs.usgs.gov/pp/0268/report.pdf). NRCS explains that steeper slopes tend to lose topsoil and have thinner soils, while topography and aspect affect moisture and temperature ([Soil Facts](https://www.nrcs.usda.gov/resources/education-and-teaching-materials/soil-facts)); litter and living roots protect forest soil ([Soil Health on Forestland](https://www.nrcs.usda.gov/conservation-basics/soil/soil-health/soil-health-on-forestland)). These observations informed the distinction between loose surface and rooted-cover thresholds. The exact game cutoffs, especially the wet-bank and cliff boundaries, are chosen defaults for this terrain and its existing assets.

## Selected rules

| Surface / population | Previous | Selected behavior |
| --- | --- | --- |
| Grass and rooted forest-floor substrate | Through 50° | Through 45°; rock above 45° |
| Trees on grass/forest/taiga | Fade 25–45° | Fade 25–40°; independent of the ground substrate |
| Exposed sand / desert / shore sand | Through 40° | Through 34°; rock above 34° |
| Exposed riverbank mud | Below 40° | Through 25°; rock above 25° |
| Rock versus cliff material | Cliff from 70° | Rock below 60°; cliff from 60° |
| Forest exposed-ground classification | Above 35° | Above 30° |
| Continuous forest litter thinning | 32% slope-driven soil blend over 24–45° | Up to 65% slope-driven soil blend over 28–42° |
| Taiga needles / dark grass | Patch selection independent of slope | Existing patch field progressively yields to soil over 28–42° |
| Loose leaf scatter | Fade to existing 32° limit | Retained |
| Ferns | Existing 26° limit | Retained |
| Flowers / meadow clover | Existing configurable limits, generally 32–40° | Retained, additionally excluded by the earlier rock surface boundary |
| Moss | Damp/shaded colonies; existing slope attenuation over 38–60° | Retained; it can persist on steeper remaining substrate than loose leaves |
| Mountain snow | Terrain-aware retention from the preceding snow change | Retained, including snow that can coat the underlying cliff surface |

Soil remains part of the existing forest-ground shader blend. Litter is retained underneath it, preventing a strip of meadow grass between forest and rock. Taiga now remains a taiga biome through the ground-cover band even after tree suitability fades out; the previous tree-limit biome switch could turn that band into a meadow. Above the vegetated-surface limit it becomes rock as before. Water classification still takes priority over slope, so submerged banks remain riverbed.

The surface labels still pass through the existing three-sample control-map blur. This change does not add another procedural surface system, geometry, noise field, texture sample or shader pass. The taiga adjustment reuses its existing patch-noise value.

## Implementation

- `Assets/Scripts/TerrainGeneration/BiomeGeneration/TerrainSlopePolicy.cs`: shared thresholds, litter-exposure curve and forest/taiga ownership rules.
- `.../Surface/SurfaceTypeClassifier.cs`: separate wet mud and dry loose-sand limits, with existing water/snow priorities.
- `.../ForestFloorPolicy.cs`: earlier, stronger loss of the loose litter mat into soil near exposed slopes.
- `.../GroundCoverMapGenerator.cs` and `.../MeshGeneration/FarTerrainGenerator.cs`: matching taiga ground-cover thinning in detailed and far/macro generation.

No serialized scene settings, material assets, sculpted terrain, shaders, or rendering budgets were changed. Near and distant tree generators already consume the shared tree-suitability threshold. Restart Play mode / regenerate chunks to apply the new generation rules. Configurable per-species placement limits remain available.

## Verification and previews

Runtime, editor and player compilation passed, with existing unrelated obsolete-API warnings. The isolated Unity run passed 1,103 additional surface/cover boundary checks, including wet/dry banks, cliffs, snow, all-biome underwater protection, continuous bounded litter thinning and steep taiga soil. Existing forest-floor validation passed: opening/slope/moisture response, X/Z seams, detailed/far ecology policy, near/billboard placement identity, zero/full density and preserved meadow density. Existing surface blending and mountain snow checks also passed, including normalized weights, shared edges and near/far snow agreement for three seeds.

The controlled [Unity material previews](../ArtReferences/TerrainSurfaces) compare identical 0–80° slope ramps using the actual terrain material/textures and Linear URP lighting. Slope increases from left to right; the displayed geometry rises accordingly. Altitude/climate inputs to the classifier are held fixed to isolate slope. These are material studies, without vegetation, scene sky/fog or streaming. The snow ramp shows the base snow/cliff classification without the separate mountain snow coating.

| Profile | Before | After |
| --- | --- | --- |
| Meadow | [Before](../ArtReferences/TerrainSurfaces/grassland-before.png) | [After](../ArtReferences/TerrainSurfaces/grassland-after.png) |
| Forest floor | [Before](../ArtReferences/TerrainSurfaces/forest-before.png) | [After](../ArtReferences/TerrainSurfaces/forest-after.png) |
| Taiga floor | [Before](../ArtReferences/TerrainSurfaces/taiga-before.png) | [After](../ArtReferences/TerrainSurfaces/taiga-after.png) |
| Sand | [Before](../ArtReferences/TerrainSurfaces/desert-before.png) | [After](../ArtReferences/TerrainSurfaces/desert-after.png) |
| Wet bank | [Before](../ArtReferences/TerrainSurfaces/wet-bank-before.png) | [After](../ArtReferences/TerrainSurfaces/wet-bank-after.png) |
| Snow substrate | [Before](../ArtReferences/TerrainSurfaces/snow-before.png) | [After](../ArtReferences/TerrainSurfaces/snow-after.png) |

Run from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tools/ValidateMountainSnow.ps1 -SurfaceChecks
```

The runner reuses the isolated graphics project under `.utmp/mountain-snow/unity-project`. Log: `.utmp/mountain-snow/surface-validation.log`. There is no live FPS benchmark; the runtime changes are shared constants, a small smoothstep calculation for litter exposure, and existing control-map generation.
