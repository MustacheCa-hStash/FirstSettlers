# Forest floor redesign

Forest grass now uses a continuous density field rather than the `DarkGrass` ground-cover category. Domain-warped colony noise, canopy intent, clearing strength, moisture, slope, and river proximity determine a deterministic keep probability. Grass jobs interpolate that probability at each jittered candidate position. Existing tree/bush/rock exclusions, clover suppression, distance thinning, and resident GPU selection still apply.

Forest ground remains predominantly litter underneath plants. Soil, moss, and mixed litter vary continuously through the existing control-map channels; discrete ground-cover labels remain available for gameplay and other foliage. The near and far terrain paths use the same `ForestFloorPolicy`. The field deliberately uses global canopy intent, rather than isolated circular tree footprints, to avoid rings and per-chunk tree-influence seams.

This pass adds no meshes, materials, shader variants, or draw submissions. The packed managed field costs 16 bytes per map sample in chunks containing forest land; foliage jobs retain only its 4-byte density channel. For a 128-sample chunk with its halo, the combined additional field storage is approximately 335 KiB. Non-forest chunks do not allocate the managed field. Existing terrain material branches can run more often when multiple floor layers contribute; GPU timing still needs measurement in the user's actual views.

Tuning is in `Assets/Scripts/TerrainGeneration/BiomeGeneration/ForestFloorPolicy.cs`:

- Colony warp and noise scales control patch shape and size, in terrain sample coordinates.
- Interior keep probability ranges from 0.025 to 0.26 before canopy and habitat suppression.
- Strong clearings approach 0.45–0.80 before habitat suppression.
- The green ground contribution is limited to 18% of the local grass keep probability. Litter therefore persists even beneath a dense opening.
- Soil and moss values are sequential shader blend weights, not exclusive area percentages.

Before the moss revision, the synthetic closed-canopy fixture averaged 8.8% of meadow candidates, and its fully open fixture averaged 65.1%, before object exclusions. Moss now further reduces the closed-canopy density. These are fixture results, not measured world coverage or frame-time gains.

The grass rims at forest rock faces came from different slope limits: wet land became grassland at 45 degrees, but remained eligible for grass through 50 degrees. That band received meadow density and tint. Wet forest now retains its forest floor through 50 degrees, with forest grass density tapering to zero there. Rock starts above 50 degrees as before; tree placement still stops at 45 degrees. Genuine dry meadow slopes retain their existing behavior.

Step 3 assigns the existing ordinary litter normal map (`leaf-fall3-normal-unity.png`) to the terrain material. Its leaf/twig layout matches the current litter albedo. Ordinary and mixed-litter normal strength fades between 12 and 45 world units, skipping those samples beyond the fade. Broad world-space tone variation reduces the uniform tiled appearance without changing ground-cover boundaries. It does not remove all recognizable repeated leaves or add silhouette depth.

Tune these values on `Assets/Materials/M_Terrain/M_TerrainBase.mat`:

- Litter normal strength: 0.45.
- Litter normal fade: 12–45 world units.
- Forest floor macro scale: 0.025, approximately a 40-unit noise grid with a smaller secondary scale.
- Forest floor macro tone strength: 0.12.

This surface pass adds two arithmetic noise evaluations on litter pixels, activates the ordinary litter normal texture sample nearby, and fades both litter normal samples at distance. It adds no geometry, alpha overdraw, materials, shader variants, or draw submissions. GPU timing remains unmeasured.

Restart Play mode to regenerate cached terrain and grass. Inspect the same forest from close range, the rock boundary, the forest edge, and the distant hill view. Leaf meshes, fern assets, and stronger texture anti-tiling remain subsequent work.

Validation passed: runtime/editor Roslyn compilation, 24 D3D11 terrain shader stages covering grass-ground/rock/shadow combinations, and isolated Unity 6000.4.8f1 checks. `Tools > Terrain > Validate Forest Floor` checks density response, rock-edge classification and taper, X/Z seams, detailed/far policy agreement, control-map independence from category islands, near/far candidate parity, deterministic selection, zero/full density, owned-array fallback, grassland preservation, and litter material/import settings. Batch validation also runs the actual far/macro terrain jobs, the 14 slope-policy checks, and the existing billboard/ground-foliage streaming regressions. No scene or camera automation is used. Logs are in `.utmp/forest-floor/`.

## October 2: dominant moss patches

Moss now uses its own domain-warped colony field, biased toward moist shaded floor and away from dry ground, clearings, open water, and extreme slopes. Coverage can reach carpet strength rather than the old 0.32 cap. `ForestFloorPolicy.MossDominance` maps coverage through smoothstep(0.03, 0.58); the terrain shader mirrors that curve. At coverage 0.4, moss contributes roughly 75% of the surface color on eligible floor. Cores fully cover the underlying litter/soil/grass.

The shader applies moss after assembling the underlying terrain, so it can cover grass and overlapping rock/cliff at selected patch edges. Snow, mud, sand, and riverbed remain protected by the eligible-surface factor. Near-vertical faces taper out; there is no new broad rock-face moss generation or moss treatment for separate boulder/tree prefab materials. The surface control-map blur, grass/litter interpolation, slope boundaries, and grass-rim behavior are unchanged as requested.

`mixedmoss-albedo2.png` supplies debris-rich patch edges, and `mossy-ground1-albedo.png` supplies dense carpet interiors. Dense texture selection follows moss dominance instead of the mixed-litter variant channel. Source images are unchanged. Fine contrast 0.35 and saturation 0.7 favor a broad olive fill; tune **Moss Fine Detail Contrast** and **Moss Saturation** on the terrain material. Dense moss tint is (0.85, 0.9, 0.7). Height/normal relief fades over 12–45 units, while color coverage persists into distant terrain. The dense source has no matching normal map, so its interiors use the geometric normal.

The same coverage reduces grass density to 2% in full cores, excludes clover in full cores, and retains 25% of otherwise eligible leaf scatters. Trees and shrubs retain their placement rules. Grass's existing native density channel carries the suppressed value; clover samples the managed moss field during bounded result collection, adding no native moss allocation or job wait. Foliage masks are sampled before the visual control-map blur, so a narrow edge transition can differ slightly.

This adds no moss instances, geometry, colliders, materials, draw calls, or shader variants. Moss terrain pixels pay texture/blend costs, including both edge/core samples in transition areas. Frame-time impact remains unmeasured. Restart Play mode to regenerate terrain and cached foliage.

**Tools > Terrain > Validate Forest Moss and Tree Scale** checks moss habitat/dominance, grass/clover/leaf suppression, tree range transport, near/sparse tree size agreement, and the existing seam/far/placement regressions. **Tools > Foliage > Render Forest Moss Preview** captures actual terrain shader/control maps and generated foliage in an isolated flat authoring fixture, without rebuilding assets or loading a live world scene. Output: [Moss_Floor.png](ArtReferences/ForestFloor/Moss_Floor.png).

Completed validation: runtime/editor C# compilation, 24 offline terrain shader stages, actual Unity D3D11 shader rendering, and the complete moss/tree/forest-floor suite. The scale fixture compared 72 forest/meadow trees at 1x and 4–5x without changing their positions. A controlled carpet fixture reduced grass from 5,189 to 100, leaves from 356 to 108, and excluded clover cores. These counts are validation fixtures, not expected world percentages. Existing distant-tree real-terrain checks were corrected to use matching height multipliers and local moisture inputs, then passed. Log: `.utmp/forest-floor/moss-tree-unity2.log`. No performance benchmark was run.

## October 2: moss color-space correction

The original isolated preview project used Gamma while the game uses Linear. The shader's literal broad moss fill (0.12, 0.18, 0.035) consequently appeared pale in the game. It is now the **Moss Broad Fill Color** material Color property, with the same authored RGB values; Unity converts that color for Linear rendering. Fine contrast, saturation, source textures, moss coverage and foliage placement are unchanged. This correction adds no texture samples, geometry or shader variants.

The isolated preview preparation now copies the game's active color-space setting, and the moss preview refuses Gamma rendering. `Moss_Floor.png` has been replaced with an actual Linear Unity D3D11 capture; `Moss_Linear_Before.png` reproduces the former literal in identical Linear lighting for comparison. The old Gamma reference is retained at `.utmp/forest-floor/moss-gamma-reference.png`. These controlled authoring captures disable fog/post-processing; live sun, shadow and time-of-day lighting still affect the result. Earlier forest authoring images were captured in Gamma and should not be treated as exact live color references.

Validation: 24 terrain shader stages passed, and the isolated Unity editor compiled the updated preview and rendered both comparisons successfully (`.utmp/forest-floor/moss-color-unity.log`). Restart Play mode to refresh copied chunk materials. No game color-space, global lighting, scene, grass-rim or generation changes were made in this correction.
