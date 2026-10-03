# Forest ferns

## Third LOD and grass-aligned transitions

`Assets/Resources/Foliage/ForestFern_LOD2.prefab` adds a **52-triangle / 156-vertex** coarse mesh, compared with near **273/819** and mid **100/300**. Four arching fronds keep four broad pairs each, using one solid triangle per leaflet instead of a folded diamond. Broader lobes preserve distant green mass; the existing plain material, size multiplier, pivot and wind remain. It adds no texture, alpha cards, veins, collider or shadow caster. LOD2 uses 48% fewer triangles than LOD1.

WorldManager > **Forest Ferns > Fern Distant LOD** exposes **Coarse Prefab**, **Match Grass Lod Distances**, **Coarse Lod Start** and **Coarse Lod End**. Matching defaults on. Near→mid follows grass density tiers 3–6 subchunks; mid→coarse follows the detailed-grass/billboard boundary, using grass's active-ring radius and transition width. With current SmearScene settings, the transitions are **11.52–23.04** and **24.96–51.84 world units**. Each plant chooses exactly one mesh using independent stable ranks and smooth complementary selection. Thresholds update when grass settings change.

Disable matching to use the existing manual near/mid controls (22–38 by default) and new coarse controls (40–55). Coarse Start is clamped beyond the first transition to preserve ordered LODs. Missing coarse assets retain the first two LODs. The GPU and CPU fallback both implement all three tiers. Fern render distance remains **65**, with fade **55–65**; it is independent of matching LOD thresholds. Fern distance-density thinning defaults off, preserving placement frequency.

Actual near/mid/coarse CPU/GPU pixel and fade parity, compute selection, ring density selection and existing habitat/generation regressions passed (`.utmp/forest-floor/forest-distance.log`). [Fern_LOD_Comparison.png](ArtReferences/ForestFloor/Fern_LOD_Comparison.png) shows LOD0/1/2 left to right at the same 2x scale using actual meshes/materials and Linear URP lighting. It is an authoring comparison, not a live-world/FPS benchmark. **Tools > Foliage > Render Fern LOD Comparison** regenerates it; **Build Coarse Forest Fern** authors only LOD2 while preserving existing near/mid assets and material settings. The full fern builder now authors all three. Earlier sections describe earlier versions.

## Resident GPU rendering

Ferns share the resident GPU renderer and compute distance/frustum/LOD selection described in [LEAF_CLUSTERS.md](LEAF_CLUSTERS.md). **GPU Indirect Rendering** defaults on, including SmearScene; the three-LOD system submits at most three indirect draws. Stable transforms/tints upload only as generation adds instances or storage grows/relocates. Unsupported hardware/materials or disabling the toggle retains CPU instancing with cached transforms and LOD ranks. The fern shader supports procedural instancing in forward/depth/normal passes; its dynamic wind remains. Colors, frequency, size multiplier, habitat rules and 65-unit range remain unchanged. Resident buffers add memory and retain peak capacity; this does not remove frond overlap or tree shading costs. Explicit correctness checks are under **Tools > Foliage > Validate Forest Scatter GPU Rendering**.

## Broader foliage and higher frequency

The second pass roughly doubles leaflet width and widens stems/tips without adding near triangles. Both halves of each distant leaflet now remain visible: the far mesh is **100 triangles / 300 vertices**, while near remains **273 / 819**. The plain green fills and texture-free shader remain.

WorldManager > Forest Ferns now exposes **Fern Size Multiplier**, default **2**, also explicitly stored in SmearScene. It multiplies the randomized Scale Range (0.75–1.2), so default instances are 1.5–2.4 times the authored mesh. Generation blocker footprints, terrain placement signatures and render/culling matrices use this scale. Changing it invalidates cached fern candidates through the existing bounded stream.

Default candidate spacing is now **1.5** rather than 2.5 units, density **0.65** rather than 0.30, and minimum moisture **0.35** rather than 0.45. This raises nominal placement opportunities approximately 6x while maintaining moss, forest, slope, river and feature exclusions. Candidates also use irregular multi-point occupancy and world-space drift rather than constrained single-point cells. The controlled preview increased from **18 to 138 ferns**; the suitable no-moss fixture produced 253. Actual world counts depend on habitat and blocker footprints, especially at the larger plant size.

The rebuilt authoring captures are `Forest_Understory.png` and `Fern_Floor_Detail.png`. Tests verified deterministic generation, increased fern frequency, exact 2x unobstructed instance scaling, inherited forest/moss/water rules and leaf seam ownership. Log: `.utmp/forest-floor/variation-unity.log`. Larger silhouettes and more plants add rasterization/geometry/culling work; no live performance benchmark was run. Earlier sections document the first pass's settings and mesh budgets.

Ready assets: `Assets/Resources/Foliage/ForestFern_LOD0.prefab` and `ForestFern_LOD1.prefab`, sharing `Assets/Materials/M_Grass/M_ForestFern.mat` and `ForestFernInstanced.shader`.

The plant has seven uneven arching fronds, paired pointed leaflets, and a roughly 1.2-unit spread. Leaflets attach to the actual stem segments; the base sits at the mesh pivot and runtime roots sink 6 mm into the sampled terrain. The material uses almost uniform green (0.31, 0.47, 0.16), modest whole-frond/instance tone differences, a subdued stem, lighting, and gentle sway. It samples no bitmap and adds no veins or surface grain. **Tools > Foliage > Build Forest Fern** rebuilds its assets and restores authored material defaults.

WorldManager exposes Forest Ferns settings. Defaults favor moist shaded Forest/Grass terrain, irregular colonies, and sparse floor coverage. Candidates use a world-aligned 2.5-unit jittered grid with density 0.30, a moisture threshold of 0.45, a 26-degree slope ceiling, and 0.75–1.2 instance scale. Clearings, dry soil and river proximity reduce placement; moss above the visible-cover threshold blocks fern roots. Existing tree/bush/boulder footprints and actual terrain triangle normals/heights also apply. Grassland terrain receives no ferns.

Ferns reuse the bounded forest-scatter cache and streaming kernel used by fallen leaves, with their own settings, seed, resource paths and profiler markers. Runtime placement creates no per-plant GameObjects. The resident indirect renderer selects complementary near/far LODs over 22–38 units. CPU fallback batches up to 1,023 plants across chunks. Their default range is 65 units, fading over the final 10; it can optionally follow grass distance.

| Mesh | Fronds | Triangles | Vertices |
|---|---:|---:|---:|
| Near | 7 | 273 | 819 |
| Far | 4 | 60 | 180 |

Opaque double-sided geometry avoids alpha-card empty regions, but overlapping fronds and foliage still cost rasterization and shading. Ferns receive shadows but add no shadow casters or colliders. Their instanced shader includes forward, depth and depth-normal passes and current URP Forward+/fog support. CPU generation/culling, retained instances, extra LOD batches and geometry all add cost; no frame-time improvement or benchmark is claimed.

**Tools > Foliage > Validate Forest Understory** checks fern habitat/asset settings, strict moss grass exclusion (including a mixed meadow/forest boundary), near/far/owned-buffer parity, 25-read cached biome ownership, leaf frequency increase, forest rock transition math and the existing moss/tree/forest/ambient-life regressions. It does not author assets. The delivery batch additionally builds and captures actual Unity D3D11 authoring fixtures in Linear color space. Images: `ArtReferences/ForestFloor/Fern_Floor_Detail.png`, `Forest_Understory.png`, and `Forest_Rock_Edge.png`. Controlled daylight/fog-free previews do not reproduce the complete live world. Restart Play mode to regenerate cached ground foliage and insect habitat.
