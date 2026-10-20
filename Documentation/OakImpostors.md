# Oak impostor bake

The finished `Assets/Prefabs/Oak_OctaImpostor_Runtime.prefab` is assigned to both `oakTreeBillboardPrefab` and `grasslandOakTreeBillboardPrefab` in SmearScene. Both source mesh categories use `Assets/Prefabs/Oak_LOD0_v03.prefab`.

## Rebuild

- **Unity:** `Tools > Impostors > Oak > Bake and Assign Oak Billboards`. Uses the oak prefab assigned on a loaded World Manager, captures it in a temporary isolated scene, builds the runtime assets, and assigns both billboard slots on loaded World Managers. Save the edited scene normally afterward.
- **PowerShell:** `Tools/BakeOakImpostor.ps1`. Prepares an isolated project with cached local packages, runs the complete Unity bake and GPU validation, installs the products, and updates only the two oak billboard references in SmearScene. The source slots must still reference Oak_LOD0_v03 before installation.
- `-PrepareOnly` prepares the isolated project; `-ValidateOnly` checks the installed bake there; `-InstallOnly` installs a previously validated bake. Installation checks SHA-256 signatures of source dependencies and products to reject stale or changed output.
- **Rebuild the proxy from existing atlases:** `Tools > Impostors > Oak > Create or Update Oak Runtime Proxy`. Re-reads capture metadata and the source leaf palette. This reapplies oak material defaults.
- **Validate in Unity:** `Tools > Impostors > Oak > Validate Baked Oak`.

## Products

| Asset | Path |
| --- | --- |
| Runtime prefab | `Assets/Prefabs/Oak_OctaImpostor_Runtime.prefab` |
| Canonical quad | `Assets/Models/Trees/Oak_OctaImpostor_Runtime.mesh` |
| Runtime material | `Assets/Materials/M_Trees/Oak/Oak_OctaImpostor_M.mat` |
| Five atlases and bake records | `Assets/Textures/Trees/Impostors/Oak/` |

The full-sphere capture uses 8×8 views, 504px content tiles, 2px padding and 2× supersampling. The four primary atlases are 4064×4064. The 128px ambient tiles produce a 1056×1056 atlas. Import preserves NPOT dimensions, uses clamp and uncompressed mipmaps, and enables sRGB only for albedo/coverage. Surface, depth, material ID and ambient remain linear data.

Ambient baking uses a 128³ alpha-aware density volume, 8 AO rays, 12 sky rays and 16 trace steps. Capture center and radius come from the current prefab's renderer bounds and are read from metadata when rebuilding the proxy. No species bounds are hardcoded.

## Coverage, color and lighting

The capture stores neutral leaf brightness and actual painted bark color. RGB is correctly encoded for an sRGB albedo importer; alpha remains linear coverage. Leaf brightness is stored at half range and restored with runtime foliage brightness 2, preserving the source gradient's highlights in RGBA8. Edge colors and semantic data bleed one pixel into empty space, with **no alpha coverage expansion** for oak. The runtime cutoff is 0.04, retaining fractional coverage above the raster fringe.

Albedo picks the supersample with greatest coverage. Surface, depth and material ID reuse that exact sample index and the same dilation donors. This prevents a bark-colored texel being classified as a leaf at branch/foliage boundaries. The shared capture tool now uses aligned samples for subsequent captures of every species; existing spruce and maple atlas files are untouched.

The shared octa shader uses **Oak palette mode 2**. `OakLeafSeasonPalette.hlsl` is shared with the 3D oak shader, including its neutral tint sentinel, location tint, summer brightness variation and seasonal blending. Raw instance colors are decoded consistently in linear projects. Ochre/russet/wine colors and season parameters are copied from `M_Oak_Leaf`; the atlas carries no fixed seasonal hue. World Manager's season preview controls meshes and impostors together.

Oak retains its painted bark instead of using the maple grayscale bark remap. Backlighting uses the captured source strength and the source's warm transmission tint. The shared shader retains its existing maple and spruce branches, fog, night ambient dimming, root registration and CPU/procedural rendering support.

The source mesh bounds remain the ecological crown bounds. The rendering path expands its owned bounds for the root-anchored camera-facing quad, preventing culling errors without inflating tree-thinning estimates.

## Validation evidence

`OakImpostorValidation` checks all 64 frames, correct map dimensions/import settings, leaf/bark classification and neutral leaf color. It performs 108 oak mesh/impostor palette comparisons and 216 maple regression comparisons across authored/global seasons and ordinary/instanced/indirect rendering.

The shared GPU validator checks the actual oak prefab's compute routing and CPU fallback, rendered CPU/indirect agreement at three angles with varied transforms/tints, load/handoff fades and bounds. The root-marker test performs 720 oak checks across camera angles, scale, perspective/orthographic views and ordinary/instanced/indirect draws. The existing shared validator was updated for the current tree registry/API and given valid placement IDs.

Source/impostor summer and autumn previews at two view angles are written to `.utmp/oak-materials/render-results`. The installed `Oak_Octa_Source.json` records the source prefab and bake settings; `Oak_Octa_Validation.json` records validated input and product signatures.
