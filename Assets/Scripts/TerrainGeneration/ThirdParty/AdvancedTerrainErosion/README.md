# Advanced Terrain Erosion: FirstSettlers integration

Source: https://github.com/lpmitchell/AdvancedTerrainErosion
Upstream file: package/Runtime/LPMGames.Terrain.Erosion/AdvancedTerrainErosion.cs
Retrieved 2026-09-10 from main. Original copyright/license notices are retained.

Project changes to the upstream file:
- Expose ErosionFilter so WorldTerrainHeight can supply the composed world height and analytical gradient.
- Replace the cubic fractional float cell hash with an integer hash. The old hash collapsed 1,024 sampled cells to one offset at a representative project seed (568317); the replacement produces 1,024 distinct offsets.
- Add optional directionSmoothing to transition smoothly through gully-slope sign changes. The default zero value preserves the upstream sign behavior for its other entrypoints.

WorldTerrainHeight.cs and WorldErosionSettings.cs contain project-specific base generation, integration and tuning. This project uses the float API; the upstream double-precision compilation symbol is unsupported by this integration.

See LICENSE.txt and THIRD_PARTY_NOTICES.md for licensing and attribution.
