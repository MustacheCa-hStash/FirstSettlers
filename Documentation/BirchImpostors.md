# Birch materials and octahedral impostor

Source: `Assets/Prefabs/Birch_LOD0_v04.prefab`, GUID `fe4a12ed830904b42abf5a9d5b7572d0`, assigned to both forest and grassland birch/aspen categories in `SmearScene`. Existing source prefab, material and shader GUIDs are preserved.

## Materials

Both birch shaders now use instancing and four consistent passes: forward, shadow, depth and depth normals. They support Forward+, additional lights, LOD/distant fades, fog, the shared night lighting, location-aware standing-tree appearance and snow.

Leaves use the existing birch atlas with alpha-only clipping at 0.5, coverage-preserving mipmaps at that same cutoff, clamp wrapping and trilinear filtering. Summer remains green `(0.18, 0.46, 0.14)`. Autumn uses gold `(1, 0.68, 0.16)`, ochre `(0.78, 0.46, 0.10)` and pale gold `(1, 0.84, 0.38)`. The authored season defaults to summer; the global season simulation overrides it. Placement colors are deterministic absolute autumn golds, decoded consistently in source and impostor shaders. Soft brightness/detail variation and stable two-sided transmission use the completed deciduous-tree conventions. Ambient floor is 0.30 and backlight strength is 0.65.

Bark retains the existing black-mark texture, cream-white base and dark scars. `BirchBarkSurface.hlsl` supplies the identical unlit bark formula to the source shader and semantic capture. Paper warmth and height variation are subtle. The original source textures are retained.

## Bake and assignment

- Runtime prefab: `Assets/Prefabs/Birch_OctaImpostor_Runtime.prefab`
- Runtime material: `Assets/Materials/M_Trees/Birch/Birch_OctaImpostor_M.mat`
- Mesh: `Assets/Models/Trees/Birch_OctaImpostor_Runtime.mesh`
- Five atlases and provenance/validation records: `Assets/Textures/Trees/Impostors/Birch`

The primary atlases contain 8 × 8 full-sphere octahedral views, 504-pixel tiles, two-pixel padding and 2× supersampling (4064²). Ambient tiles are 128 pixels (1056² atlas), using a 128³ density volume, eight AO rays, twelve sky rays and sixteen steps. Capture excludes lighting and wind, uses actual source bounds and forces mesh LOD0. Foliage contains neutral half-range brightness, restored at runtime and colored by the shared seasonal palette; bark stores full authored color. Semantic outputs share coverage's supersample winner and one-pixel color bleed without coverage expansion.

Runtime uses `Custom/SpruceOctaImpostor`, palette mode 3 (Birch). The gold palette uses the same interpolation as oak with birch colors. Existing oak, maple, spruce and white pine palette behavior is unchanged. The material supports CPU/indirect instancing, depth parallax, AO/sky visibility, fog, wind and stable two-sided transmission.

Run `Tools/BakeBirchImpostor.ps1` to prepare an isolated Unity project from cached packages, bake, validate, verify input/output SHA256 hashes, install, and assign both `birchAspenTreeBillboardPrefab` and `grasslandBirchAspenTreeBillboardPrefab` in `SmearScene`. The installer checks both source slots still reference the baked prefab. Options: `-PrepareOnly`, `-ValidateOnly`, `-InstallOnly`. Unity menu: `Tools > Impostors > Birch > Bake and Assign Birch Billboards`.

Validation covers all 64 atlas views, source material references and alpha settings, deterministic location colors, 108 source/impostor seasonal color comparisons, oak/maple regressions, CPU/indirect routing and pixels, culling bounds, fades, 720 root-placement cases and source/proxy previews in summer and autumn at two angles.
