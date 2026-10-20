# White pine octahedral billboards

Source: `Assets/Prefabs/WhitePine_LOD0_v01.prefab`, the forest and grassland white pine source assigned in `SmearScene`. Uses the approved `M_WhitePineLeaves` and `M_WhitePineBark` materials with the painted bark texture.

Outputs:

- `Assets/Prefabs/WhitePine_OctaImpostor_Runtime.prefab`
- `Assets/Materials/M_Trees/WhitePine/WhitePine_OctaImpostor_M.mat`
- `Assets/Models/Trees/WhitePine_OctaImpostor_Runtime.mesh`
- Five atlases and capture/validation metadata in `Assets/Textures/Trees/Impostors/WhitePine`.

The primary atlases use 8 × 8 full-sphere octahedral views, 504-pixel tiles, two pixels of padding and 2× supersampling (4064²). The ambient atlas uses 128-pixel tiles (1056²), a 128³ density volume, eight AO rays, twelve sky rays and sixteen steps. Geometry is captured at mesh LOD0 with wind and scene lighting excluded. Bounds come from the actual source prefab.

`WhitePineImpostorSemanticCapture.shader` captures the source's blue-green needle palette, tips, strand contrast and variation in linear space, then the capture tool stores sRGB albedo. Painted bark retains its full color. Normals, roughness, transmission, depth and material IDs share the same supersample winner as coverage. Color bleed uses one pixel without expanding coverage.

The shared runtime impostor shader uses full captured evergreen RGB, no deciduous palette, per-tree needle multipliers, depth parallax, AO/sky visibility, wind, scene fog and stable two-sided needle transmission. The stable-transmission option defaults off for existing materials; oak retains its existing behavior. Quad root anchoring, source ecological bounds and expanded camera-facing render bounds follow the existing species pipeline.

Rebake from PowerShell with `Tools/BakeWhitePineImpostor.ps1`. The script prepares an isolated Unity project using local cached packages, bakes and validates, checks SHA256 input/output signatures, then installs the generated assets and updates both `whitePineTreeBillboardPrefab` and `grasslandWhitePineTreeBillboardPrefab` in `SmearScene`. It verifies both source slots still reference the expected prefab. `-PrepareOnly`, `-ValidateOnly` and `-InstallOnly` are also available. The Unity menu is `Tools > Impostors > WhitePine > Bake and Assign WhitePine Billboards`.

Validation checks all 64 atlas views and foliage/bark classifications, actual CPU/indirect rendering, culling bounds, fades, 720 root-registration cases, and source/proxy previews at two angles in summer and autumn. Evergreen previews must match between seasons. Existing oak and maple color regressions also run.
