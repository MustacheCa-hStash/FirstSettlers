# Mountain Detail and Art Setup

## Geometry

The existing mountain mask, primary four-octave height field, frequency, and height multiplier are unchanged. The old positive-only ruggedness layer is replaced by three rounded ridge octaves, a shared two-sample domain warp, and shallow channels derived from the first ridge sample. Everything samples absolute terrain coordinates through the same helper in managed and Burst/native generation.

Detail is bounded to 8% of broad mountain relief, with an additional cap of 1.5 normalized height units. It fades across the lower flanks and cannot enter regions where the broad mountain signal still permits rivers. A water-clearance limit also prevents this layer from moving terrain across the global water plane. These limits preserve the broad profile, not every individual peak height.

At the current sampleScale of 600, the nominal octave scales are 360, 180, and 90 terrain units (108, 54, and 27 world units at worldScale 0.3). These are noise scales, not guaranteed feature widths. Crests are rounded to reduce sharp features missed by coarse meshes. Collision and distant terrain still sample the same height function; coarse triangles inevitably approximate the intervening terrain.

The tuning constants are grouped in HeightMapGenerator.SampleMountainDetail. Biome, surface, snow, and placement slope thresholds remain unchanged and continue to use generated terrain slopes. Rivers still use the original broad mountain signal, never the detailed height.

## Assign Your Assets

Open Assets/Materials/M_Terrain/M_TerrainBase.mat (the terrain material assigned in SmearScene).

1. Assign a seamless color texture to **Rock / Cliff Albedo**.
2. Assign its matching tangent-space normal map to **Rock / Cliff Normal**. Import it as **Normal map**, not a regular color texture.
3. Enable **Enable Rock / Cliff Detail**. It is off by default, so unassigned assets add no rock-detail texture sampling to the shader.
4. Use Repeat wrapping and mipmaps on both assets. Albedo uses sRGB; normal-map import handles its data encoding.

Start with one restrained, pale neutral-gray fractured stone set: irregular cracks, broad chipped facets, and fine grain, without strong baked lighting or deep black crevices. An initial 1K or 2K pair is sufficient for evaluation. The existing Rock Color and Cliff Color tint the albedo, so a dark source texture can make the result too dark.

The same pair covers classified **Rock** and **Cliff** surfaces, blended by the existing surface weights. Cliffs retain their darker tint and have a separate, slightly stronger normal setting. Triplanar projection avoids vertical stretching and uses consistent world-space coordinates across chunks. Use irregular, mostly nondirectional fractures; strongly horizontal strata can reveal projection blending.

## Regions and Desired Appearance

| Region | Desired detail | Assignment |
| --- | --- | --- |
| Exposed mountain ridges and flanks | Chipped slabs, restrained cracks, occasional coarse grain | New shared Rock / Cliff slots |
| Steep cliff faces | Same geology, stronger relief and darker existing tint | Cliff Normal Strength; shared texture pair |
| Snow-covered upper slopes | Subtle wind-packed ripples or fine granular snow, not stone cracks | Existing Snow Albedo / Snow Normal slots |
| Grassy foothills | Existing grass and ground-cover detail fading into exposed stone | Existing grass/ground-cover slots; no new assets required |
| Riverbeds, mud, sand | Keep existing appearance | New rock layer does not target these surface types |

There is no separate scree or altitude-specific rock slot in this version. Keep the shared stone set suitable for both mountain outcrops and other exposed rock. No displacement, metallic, roughness, or occlusion slots were added; normal maps affect lighting, not silhouettes or collision.

## Starting Controls

- **Rock / Cliff Tiling:** 0.12 repeats per world unit, about one repeat every 8.3 units. Increase for smaller-looking features.
- **Rock / Cliff Albedo Strength:** 0.5. Lower it if the texture overwhelms the existing stylized palette.
- **Rock Normal Strength:** 0.45; **Cliff Normal Strength:** 0.65. Lower both if highlights look noisy.
- **Rock Detail Fade Start / End:** 100 / 450 world units. Color detail and normal perturbation fade to the existing plain material at distance; geometric mountain detail is unaffected.

Regenerate terrain to see geometry changes. Edit the base material outside Play mode so assignments persist; restart generation if existing chunk material instances retain old settings.

## Validation

Run **Tools > Terrain > Validate Mountain Detail**. It compares the new field with the original broad field and legacy ruggedness formula over three seeds, checks signed/bounded detail and water protection, verifies managed/Burst and neighboring chunk heights, compares near/far shared vertices, compiles both rock shader variants, and runs existing water/surface-blending regressions.
