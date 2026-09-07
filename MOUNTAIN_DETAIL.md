# Mountain Detail and Art Setup

## Mountain Coverage (Inspector)

On WorldManager, adjust **Mountain Coverage** (next to Sample Scale). The default **1** preserves the original mountain footprint. Try **1.5** to expand mountain shoulders into neighboring land; higher values increase the overall mountainous XZ area. The supported range is 0.5 to 3. This is a coverage-shaping control, not a literal distance or area multiplier.

Noise coordinates and frequencies remain unchanged. The control remaps the lower part of the mountain mask, smoothly joining the unchanged strong mountain cores at mask 0.45. This preserves those cores and their heights while increasing the surrounding mountain footprint. River exclusion and biome classification use the expanded mask. Near terrain, far terrain (including macro tiles), and the height data used by colliders and placement all receive the same setting. Base-land noise, climate coordinates, river path coordinates, and global water Y remain unchanged.

Restart Play Mode/regenerate the world after changing it. This replaces the earlier horizontal-rescaling behavior: the same serialized setting now controls coverage, so reset to 1 before comparing. Increasing it does not move the noise pattern or spread ranges farther apart; nearby foothills can join at higher values. Minor shoulder peaks can rise, but strong cores remain unchanged. Do not use the general Sample Scale for this adjustment.

## Geometry

At coverage 1, the existing mountain mask, primary four-octave height field, frequency, and height multiplier are unchanged. The old positive-only ruggedness layer is replaced by three rounded ridge octaves, a shared two-sample domain warp, and shallow channels derived from the first ridge sample. Everything samples absolute terrain coordinates through the same helper in managed and Burst/native generation.

Detail is bounded to 8% of broad mountain relief, with an additional cap of 1.5 normalized height units. It fades across the lower flanks and cannot enter regions where the broad mountain signal still permits rivers. A water-clearance limit also prevents this layer from moving terrain across the global water plane. These limits preserve the broad profile, not every individual peak height.

At the current sampleScale of 600, the nominal octave scales are 360, 180, and 90 terrain units (108, 54, and 27 world units at worldScale 0.3). These are noise scales, not guaranteed feature widths. Crests are rounded to reduce sharp features missed by coarse meshes. Collision and distant terrain still sample the same height function; coarse triangles inevitably approximate the intervening terrain.

The geometry tuning constants are grouped in HeightMapGenerator.SampleMountainDetail. Biome, surface, and placement labels continue to use generated terrain slopes. Mountain snow rendering now uses the separate continuous coverage described below. Rivers still use the original broad mountain signal, never the detailed height.

## Mountain Snow Coverage

MountainSnow computes a static snow coating when control maps are generated. Temperature continuously raises the snowline from cold to hot regions; moisture lowers it modestly. At moisture 0.5, the snowline centers are generated heights 0.7, 2, 4, and 7 at temperatures 0, 0.3, 0.65, and 1. Coverage starts 0.55 below that line and reaches full climatic coverage 1.9 above it, before slope/exposure adjustments. Generated heights convert to world Y with meshHeightMultiplier * worldScale (60 in SmearScene). These are artistic thresholds for the compressed world, not real-world elevations.

The fixed climatic shaded direction is +Z, with prevailing wind toward +X. Sheltered hollows lower the snowline; exposed ridges retain somewhat less snow. The normal uses the actual vertical height multiplier. Retention tapers between 55 and 85 degrees to accommodate the steep, vertically exaggerated mountains. Broad world-coordinate noise varies the snowline, and smaller breakup fades away in upper snowfields. Tune these values in MountainSnow.cs.

Coverage fades into the existing lowlands between mountain masks 0.25 and 0.45, and between heights 0.6 and 1.5 above the water level. Existing lowland snow and tundra dusting remain intact even where the mountain mask overlaps them. Mountain snow can coat rock or grass without changing gameplay biome/surface labels; legacy mountain Snow materials recover rock underneath where snow retreats. Shorelines and river banks are excluded. No geometry, collision, dynamic weather simulation, extra shader textures, or per-frame snow work is added.

Near and far terrain call the same evaluator in Burst jobs. Near terrain reuses its height map and samples beyond the halo where necessary; distant terrain reuses the four height samples already needed for its slope. Both use a fixed four-terrain-unit radius, independent of mesh LOD. Coarse distant control maps still approximate small snow boundaries through filtering.

Restart Play Mode/regenerate the world to update existing chunks. Run **Tools > Terrain > Validate Mountain Snow** to check climate ordering, slope and shelter response, weight conservation, protected water, adjacent X/Z chunk borders, and near/far snow agreement across three seeds.

### Snow Texture Setup

Assign the prepared textures to **Snow Albedo** and **Snow Normal** on Assets/Materials/M_Terrain/M_TerrainBase.mat. Import the normal texture as **Normal map**, and use Repeat wrapping with mipmaps. These slots are currently unassigned; their white/flat defaults render the snow color without surface detail. Snow projection is already active and needs no enable keyword or toggle.

Snow uses world-space XZ mapping on gentle terrain and blends into triplanar mapping between slope values 0.35 and 0.75 (approximately 49 to 76 degrees). Albedo and normals share the same projection coordinates; each normal-map projection is oriented into world space before blending. Near/far snow tiling and transition distances are shared material settings, not separate settings for far terrain.

Near chunks, far chunks, and macro tiles all clone the same terrain material and bind the same three control-map slots. Snow is the red channel of Control Map 1 in every path. Far control maps use the same snow evaluator, with lower-resolution filtering approximating fine boundaries. Assign textures before entering Play Mode, or regenerate runtime material instances afterward.

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
