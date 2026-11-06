# Distant rock and cliff textures

Near chunks and far quadtree patches share `Custom/StylizedTerrainURP` and the terrain base material. Rock/cliff color now survives beyond the old 100–450 world-unit fade. It smoothly changes scale before fading to the existing regional average color at the horizon. Near texture color, regional geology tints, surface coverage, and terrain geometry are preserved.

| Control | Base material value | Behavior |
| --- | ---: | --- |
| Rock tiling | 0.01 | Existing near scale: one repeat per 100 world units |
| Far tiling | 0.0025 | Broad color patterns: one repeat per 400 world units |
| Far scale blend | 180–650 | Smooth crossfade between the two world-space scales |
| Far minimum mip | 3 | A 1024² source uses at most 128² for distant color |
| Far mip bias | 1.5 | Selects another 1.5 mip levels beyond the projected pixel footprint, subject to the minimum mip |
| Color fade | 2500–6000 | Gradual return to the regional average color at the horizon |
| Normal fade | 35–110 | Existing near-only normal mapping |

All distances are Unity world units after world scaling. Controls are in the terrain material's **Rock and Cliff** section. Generated chunks clone the base material: restart Play mode or regenerate chunks to propagate material-value changes to already loaded terrain.

## Sampling cost

- Far color reuses the existing BC7-compressed 1024² rock albedo and its mip chain. No additional textures, samplers, shader variants, control maps, or meshes are allocated.
- The far path computes a mip from explicit world-position derivatives taken before divergent surface branches. A positive bias and minimum mip prevent broad tiling from requesting fine source levels. Trilinear interpolation smooths mip changes; explicit-LOD sampling uses isotropic filtering at distance.
- Near rendering keeps derivative-based anisotropic sampling. Both scales skip triplanar axes whose eased weight is zero. Normal texture reads stop after their independent fade.
- Only the scale transition samples both color layers. Fully distant rock skips near color/normal sampling; beyond the final color fade it skips all rock detail reads.

Smaller mip reads reduce the working texture footprint and bandwidth, not the total allocation of the shared texture. The full mip chain remains available for nearby rock. This change adds color reads where the old shader used a constant; actual frame cost depends on visible terrain coverage, hardware, and other rendering work.

## Validation

Run `./Tools/ValidateDistantTerrain.ps1`. It builds an isolated Unity project from local package sources and uses D3D11 to warm 48 terrain shader variants and render production-shader fixtures. It checks compressed/mipmapped import settings, material-inspector coverage, distant color against the former fallback, normal-map isolation, and the final horizon fallback. A diagnostic texture encodes different colors in each mip to verify that the minimum mip, positive bias, and pixel footprint select smaller levels on the GPU.

Captures and `validation.txt` are written to `Documentation/Images/DistantTerrain`; the full log is `.utmp/distant-terrain/validation.log`. These are functional rendering checks, not a gameplay FPS benchmark.

The validated D3D11 run passed all 48 variants and 165 inspector property checks. Diagnostic center-pixel red values were 60 with unrestricted mips, 141 with minimum mip 3, 135 with bias 3, and 141 with a larger pixel footprint. A real generated dry-mountain patch retained rock variation against the old flat fallback, while changing the normal texture had no effect on fully distant rock.

- [Generated terrain with distant color](Images/DistantTerrain/Terrain_DistantTextured.png)
- [Same terrain with the previous distance fade](Images/DistantTerrain/Terrain_PreviousFallback.png)
