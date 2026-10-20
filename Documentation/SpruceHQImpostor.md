# Spruce HQ impostor

Source: `Assets/Prefabs/Spruce_LOD0_v06.prefab`, the currently assigned spruce source. This bake creates separate assets and assigns only `spruceTreeBillboardPrefab` on World Manager in `SmearScene`.

## Preserved original

The spruce-specific billboard slot was empty, so spruce used the general `treeBillboardPrefab` fallback. That original remains at:

- `Assets/Prefabs/Spruce_OctaImpostor_Runtime.prefab`
- `Assets/Materials/M_Trees/Spruce/Spruce_OctaImpostor_M.mat`
- `Assets/Models/Trees/Spruce_OctaImpostor_Runtime.mesh`
- `Assets/Textures/Trees/Impostors/Spruce/`

The general fallback assignment is retained. `Logs/SpruceHQ/OldAssetHashes.json` records original file hashes, including their metadata and the shared runtime shader, for an explicit preservation check.

## New assets

- `Assets/Prefabs/SpruceHQ_OctaImpostor_Runtime.prefab`
- `Assets/Materials/M_Trees/SpruceHQ/SpruceHQ_OctaImpostor_M.mat`
- `Assets/Models/Trees/SpruceHQ_OctaImpostor_Runtime.mesh`
- `Assets/Textures/Trees/Impostors/SpruceHQ/`

The 64 full-sphere views use 1,016-pixel tiles, four pixels of padding and 2× supersampling, yielding 8192² primary atlases. The old bake used 504-pixel tiles. The new bake preserves narrow needles with maximum-coverage supersamples, expands a two-pixel neighborhood at 0.75 strength, and copies the matching normal/depth/material semantics. This is approximately one old-resolution texel of support. Large branch gaps remain part of the source silhouette. The ambient atlas uses 128-pixel tiles with matching four-pixel padding (1088²).

Runtime textures use trilinear filtering, mipmaps, matching 0.012 alpha-cutoff coverage preservation and explicit desktop size/format overrides. BC7 stores albedo, normals/roughness/transmission, material IDs and ambient data; depth uses R8 after ambient baking. This increases per-view detail while controlling GPU memory. CPU-readable texture copies are disabled after baking. The original shader and original materials are retained.

The semantic capture follows the current spruce source's needle color/detail and bark formulas without scene lighting or wind. The source's assigned needle texture is `Assets/Textures/Trees/leafCard_Spruce_03.png`; the bake explicitly verifies that it exists and is assigned before capturing. The new evergreen material uses captured colors with neutral season tint, source ambient/light-wrap settings, restrained brightness restoration and AO. Source materials and geometry are unchanged.

## Reproduction and checks

`Tools/BakeSpruceHQImpostor.ps1` prepares the isolated project, bakes, applies runtime imports, validates, verifies input/output hashes, installs the new assets and updates the spruce-specific slot. `-PrepareOnly`, `-ValidateOnly` and `-InstallOnly` are supported. Unity menu: `Tools > Impostors > SpruceHQ > Bake and Assign SpruceHQ Billboards`.

Validation checks all 64 atlas frames, imported resolutions, coverage mip settings, storage formats, compute routing and CPU/indirect pixels, culling bounds, fades and 720 root-placement cases. It renders the source, original and HQ impostor at three angles and 512/256/128/64-pixel viewports. Distance coverage and interior-gap counts are saved alongside comparison images in `Logs/SpruceHQ`.

Final interior-gap counts summed across three tested angles:

| Viewport | Original | HQ |
|---|---:|---:|
| 256 px | 1,048 | 271 |
| 128 px | 85 | 49 |
| 64 px | 13 | 11 |

These count empty pixels between each crown row's occupied edges in the comparison fixture, not missing branches in the actual model. Source-shaped branch openings are retained. All 22 recorded original files matched their initial hashes after installation; the new spruce-specific slot resolves to the HQ prefab and the general fallback still resolves to the original.
