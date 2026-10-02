# Forest grass tuft

Step 1 replaces the meadow-shaped grass in forests with a separate low, open tuft. The October 2 preview revision widens and lightens it after inspecting actual Unity forest renders. Grassland assets remain unchanged; clover locally suppresses grass in forest openings, and the subsequent moss integration reduces grass in carpet cores through the same forest density field. The accompanying leaf scatter revision is documented in [LEAF_CLUSTERS.md](LEAF_CLUSTERS.md), and moss in [FOREST_FLOOR.md](FOREST_FLOOR.md).

## Assets and appearance

- Near prefab: `Assets/Resources/Foliage/ForestGrassTuft_LOD0.prefab`.
- Distant prefab: `Assets/Resources/Foliage/ForestGrassTuft_LOD1.prefab`.
- Material: `Assets/Materials/M_Grass/M_ForestGrassTuft.mat`.
- Meshes: `Assets/Models/Foliage/ForestGrassTuft_LOD0.asset` and `ForestGrassTuft_LOD1.asset`.
- Shader: `Assets/Shaders/S_Grass/ForestGrassInstanced.shader`.

The tuft uses 14 tapered blades with separated roots, uneven heights of roughly 9–24 cm before instance scaling, and modest leaning. Blades are now 1.6–2.8 cm wide rather than 0.9–1.7 cm, increasing silhouette coverage without adding triangles or instances. There is no filled strip between the roots. Roots sit 9 mm below the sampled surface. The far mesh retains seven of the same blades, including one dry blade.

Each blade has a nearly uniform olive or muted dry-brown fill. The authored green is now RGB (0.29, 0.40, 0.17), brighter than the original (0.25, 0.31, 0.12), but still subdued compared with meadow grass. There are no veins, grain, capillaries, or sampled bitmap textures. Vertex colors select the dry accent and a small whole-blade value difference; the shader adds a small whole-tuft variation, an 8% brown root transition, lighting, shadows, fog, and restrained wind. This follows the simple tapered silhouettes and plain fills of the grassland asset at a smaller scale. Current URP Forward+ and fragment-fog variants prevent incorrect indirect-light attenuation and near-ground fogging.

GrassSettings has optional Forest Grass Assets overrides. Unassigned fields automatically load these two Resources prefabs. The usual grassland prefabs remain assigned as before. Restart Play mode to resolve the new assets. Edit the new material for palette, root blend, and wind changes; use **Tools > Foliage > Build Forest Grass Tuft** to regenerate geometry (this also restores the authored material defaults).

## Rendering and cost

The production resident grass renderer uses the existing forest-blend marker to choose a separate mesh/material at both distances. Candidates, rank selection, density, exclusions, streaming, and complementary LOD transitions remain shared. CPU instancing fallback makes the same biome split. Missing forest assets fall back to the standard asset at the affected distance.

| Mesh | Vertices | Triangles per tuft |
|---|---:|---:|
| Forest near | 70 | 42 |
| Forest distant | 21 | 7 |
| Existing GrassTuftBroad_LOD0 | 24 | 18 |

Near geometry is more expensive than the broad meadow cards because each blade has its own silhouette. Opaque, double-sided blades avoid rasterizing and alpha-testing the empty portions of large cards, but overlapping blades still have a cost. No new shadow casters or colliders are added. This is not a measured frame-time improvement.

A chunk containing only one grass type keeps at most two indirect submissions. A mixed forest/meadow chunk can use four. All types share one 80-byte-per-candidate source arena; only visibility indices and indirect arguments are separate. Visibility buffers allocate lazily by draw channel, so a mixed chunk can retain another 8 bytes per arena slot for its second pair of lists. Existing source uploads remain per changed subchunk, with no per-frame CPU matrix rebuilding or GPU readback in production.

## Validation

`ForestGrassValidation` checks prefab/Resources resolution, instancing support, geometry budgets, separate root topology, uniform blade colors, both LODs, and shader import. Its compute-only check compares four biome/LOD lists against the CPU selection policy using 2,048 mixed candidates, including transitions, zero density, source growth, partial updates, and empty replacement. The synthetic resident-streaming regression uses native terrain maps as production does. These checks do not load a game scene or create a camera.

Test menu: **Tools > Foliage > Validate Forest Grass (assets and compute only)**. See [forest context](ArtReferences/ForestFloor/After_Forest.png), [generated floor close-up](ArtReferences/ForestFloor/After_Close.png), and [combined asset composition](ArtReferences/ForestFloor/Forest_Foliage_Close.png) for user-requested actual Unity authoring renders. These use controlled daylight and the game's ground albedo on a flat fixture, not the complete live terrain/lighting setup. Slopes, live canopy lighting, and performance still need the user's normal walking view.

Original validation: runtime/editor C# compilation; offline shader variants and resident compute kernel; isolated Unity prefab import and synthetic streaming regression; actual D3D11 compute selection on the RTX 5090. Logs are in `.utmp/forest-grass/asset-validation2.log` and `gpu-validation.log`. October 2 adds actual D3D11 offscreen authoring captures and range/LOD/clover validation (`forest-preview-delivery.log`), plus refreshed runtime/editor and shader compilation. Existing obsolete-API warnings remain unrelated. No Play mode, live-scene benchmark, or computer-control automation was used.
