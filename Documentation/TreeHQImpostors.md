# HQ tree impostors

The five older-resolution tree bakes have separate HQ replacements. The sources are the prefabs assigned in World Manager:

| Species | Source | Replacement |
|---|---|---|
| Red maple | `RedMaple_LOD0_v03` | `RedMapleHQ_OctaImpostor_Runtime` |
| Sugar maple | `SugarMaple_LOD0_v04` | `SugarMapleHQ_OctaImpostor_Runtime` |
| Oak | `Oak_LOD0_v03` | `OakHQ_OctaImpostor_Runtime` |
| Birch | `Birch_LOD0_v04` | `BirchHQ_OctaImpostor_Runtime` |
| White pine | `WhitePine_LOD0_v01` | `WhitePineHQ_OctaImpostor_Runtime` |

Prefabs live under `Assets/Prefabs`; the matching material, atlas and mesh assets use the same species-plus-`HQ` prefix. Original prefabs, textures, materials, meshes and metadata remain at their original paths. `Logs/TreeHQ/OriginalAssetHashes.json` verifies preservation, including the previously approved SpruceHQ assets and shared spruce runtime shader.

Each replacement uses the approved spruce quality tier: 64 full-sphere views, 1,016-pixel tiles, four-pixel padding, 2× supersampling, 8192² primary atlases, matching donor samples for all semantics, two-pixel coverage support, coverage-preserving mipmaps, trilinear filtering and 0.012 alpha cutoff. Broadleaf support uses 0.5 strength; white pine uses the spruce setting of 0.75. Ambient visibility uses 128-pixel tiles, a 128³ voxel volume, eight AO rays and twelve sky rays. Runtime desktop formats are BC7 and R8 depth, with CPU readability disabled after baking.

Maple capture follows the leaf materials actually assigned to the current source prefabs, `M_RedMaple_Leaf` and `M_SugarMapleLeaf`. Their original leaf shaders use different palette and tint rules from the separate `_Stylized` materials used by older baking tools. `TreeHQOctaImpostor.shader` reproduces the assigned materials' red/crimson/orange or red/orange/yellow seasonal mixtures, colored card-bottom shade, per-tree placement tints and stable transmission. Its atlas stores leaf/fine palette coordinates in material-ID B/A and the card shade in surface B. Normal/coverage/detail and bark color remain captured. The original shared impostor shader is retained; GPU bounds recognize both shaders. Near-tree materials are preserved.

Oak, birch and white pine use the same HQ runtime shader with the original species palette behavior selected. The HQ shader discards coverage without a semantic material identity, preventing faint alpha introduced by block compression from displaying empty border texels. Validation checks raw capture alignment separately and renders an empty-identity fixture to verify that discard. Lighting is calibrated against the assigned source under the same light and placement tint. The seasonal preview globals continue to affect the new deciduous materials.

World Manager assignments in `Assets/Scenes/SmearScene.unity` include all nine relevant species and grassland billboard slots. The two general fallback slots use the approved SpruceHQ, matching their existing spruce source fallback for categories without a dedicated source. SpruceHQ is reused without rebaking. The earlier spruce documentation describes its original installation; this batch subsequently upgrades the general fallback assignments.

Run `Tools/BakeTreeHQImpostors.ps1` to prepare the isolated project, resume missing or changed bakes, validate, verify hashes, install and assign. It supports `-PrepareOnly`, `-ValidateOnly` and `-InstallOnly`. Do not run two isolated-project Unity jobs simultaneously. Unity menu: `Tools > Impostors > Bake and Assign All HQ Trees` updates the currently loaded managers; the script handles the saved scene and fallback slots.

Checks cover source texture/material bindings, all 64 frames, import formats and sizes, compute routing, CPU/indirect pixel parity, fades, culling bounds and root registration. Comparison images and coverage/gap metrics render the source, old and HQ impostors in summer and autumn at three angles and 512/256/128/64-pixel viewports. Palette fixtures compare source and HQ shaders across seasonal preview, placement tints and rendering modes. Validation records include input/output hashes, so stale results cannot be installed.
