# World Terrain Generation and Art Setup

## Whole-world erosion

The current generator replaces the previous mountain-only detail integration. It uses a smooth, rotated gradient-noise base with analytical derivatives and applies the advanced erosion filter once in global terrain coordinates. Lowlands, mountains and seabed all receive erosion. Legacy warped ridges, fake ruggedness, summit stretching and max-unioned mountain copies are no longer used to form the heightmap. Mountain Width now broadens the smooth mountain mask. Existing world seeds produce a new layout.

Select WorldManager and expand **Erosion** under **Heightfield Overhaul**. Settings are copied into each world's generation requests. In Play mode, use **Regenerate Terrain** at the bottom of the Inspector to apply edits consistently to near chunks, distant/macro tiles, collision, snow resampling and placement. Alternatively restart Play mode. Editing fields alone intentionally does not mix old/new terrain across already loaded chunks.

## Controls and units

- **Mountain Spatial Scale** defaults to 2.4. Both mountain shapes and their distribution stretch in XZ, giving wider, less frequent mountains with the same vertical relief range. Seeded mountain locations change. The ordinary rolling land noise is not stretched, providing hills within the broader mountain slopes.
- **Mountain Sparsity** defaults to 0.12. Higher values leave more open lowlands by raising the region-mask onset, while the fully mountainous mask remains 1. This is independent of horizontal scale; the existing Mountain Coverage setting still applies.
- **Gentle Mountain Erosion** defaults to 0.45. Gentle base slopes retain 45% of erosion, transitioning smoothly to full erosion on steep slopes. Selection is based on local slope, never repeated elevation bands. No terrace or bench remapping remains.
- **Lowland Hill Variation / Scale** default to 0.8 / 900 terrain units. Occasional smooth dry-land patches increase the usual half-height ratio up to 0.9, without adding noise octaves or changing the shoreline. River carving remains last so channel beds and broad valley floors stay controlled. Set variation to zero for the previous uniform lowland relief.

- **Lowland Height Ratio** defaults to 0.5: compresses final eroded lowland height around global water, preserving XZ footprint and shore crossings. The effect fades out as mountain contribution rises. It also compresses shallow seabed relief.
- **Shore Height Band / Shore Slope Ratio** default to 0.25 / 0.2: progressively flatten heights near water. The band applies to the already compressed surface; river carving runs afterward to preserve channels and dry valley floors.
- Settings version 4 removes terraces and adds the spatial controls below, preserving existing erosion and shoreline choices.

- **Base Elevation / Lowland Relief / Mountain Relief** control broad landforms. **Base Octaves / Base Roughness / Mountain Shape** control the smooth input; two gentle octaves are the default so erosion provides the surface structure.
- **Amplitude** is the maximum accumulated erosion displacement in normalized height units. It works as an actual amplitude control; zero and Enabled=false bypass erosion. World vertical distance equals normalized height * Mesh Height Multiplier * World Scale (60 in SmearScene).
- **Wavelength** controls the largest gully scale, independently of Sample Scale. All XZ scales are terrain units: multiply by World Scale (0.3 in SmearScene) for world distance. Default 512 corresponds to 153.6 world units. Individual features can be narrower than their nominal wavelength.
- **Octaves / Lacunarity / Persistence** control the count, scale ratio and amplitude falloff of gully layers. **Minimum Wavelength** excludes finer octaves everywhere; it does not change with mesh LOD. Defaults yield nominal wavelengths 512, 256, 128, 64.
- **Stretch / Rotation / Offset / Seed Offset** control the erosion pattern's spatial layout. The input gradient transforms with the domain so directions remain consistent. Stretch 1,1 is isotropic.
- **Gully Weight / Branching / Cell Size / Normalization** adjust the branching pattern. **Ridge Rounding / Valley Rounding** separately soften crests and gully floors; increase Valley Rounding for gentler drainage bottoms.
- **Direction Smoothing** reduces abrupt internal direction flips at crests. **Slope Response / Height Scale** control how the base slope influences the filter and its branching.
- **Fade Target=Local Relief** compares the base height with its neighborhood so a high-altitude valley can still receive a valley target. **Relief Radius / Relief Contrast** define that neighborhood and response. **Altitude** is an alternate artistic mode with explicit Valley And Peak Heights.
- **Max Mesh Spacing** limits near/far/macro terrain vertex spacing. Smaller values preserve gully silhouettes at increased CPU/memory/render cost. Existing resolution settings can request denser meshes. Use roughly four or more samples across the smallest nominal wavelength. A heightmap cannot make a coarse triangle represent a narrow gully.

## Flatter river valleys and gully floors

Erosion shapes the entire base first. With **Carve Rivers** enabled, the existing river field then creates broad dry valley shoulders and a narrower submerged channel. **River Valley Width** expands/contracts the broad corridor; **River Valley Flattening** blends it toward its flat dry floor (1 is the full original flattening behavior). Channel paths are independent of those two controls. Mountain exclusion still keeps this lowland river system from slicing level channels through high ridges.

The erosion filter itself supplies smaller gullies with rounded floors via Valley Rounding. Those gullies are an erosion-like procedural pattern, not a hydrologically connected water simulation. Global erosion is no longer bounded by the old mountain-only 8% relief or shoreline protection, so changing its settings can change coastlines and lakes. Biomes/placement consume the new heights without retaining the old world distribution.

## Artifact fixes and verification

The earlier float cell hash could collapse to repeated offsets at project-sized seeds. It is replaced with integer hashing. Analytical input gradients replace subtraction of nearly equal float heights; smooth internal slope-direction transitions reduce crest artifacts. Erosion is evaluated after composing one base field, rather than on overlapping stretched mountain copies. Incoming chunk terrain/water are suppressed while an outgoing macro tile still covers them, avoiding overlapping draws during handoff.

Run **Tools > Terrain > Validate World Erosion**. Runtime/editor compilation and headless Unity 6000.4.8f1 checks passed for analytical gradients, world-wide coverage, zero/off bypass, valley controls, hash diversity, handoff visibility, customized settings, near/far/macro/collider agreement and X/Z seams across three seeds. A generated hillshade was inspected; the user's exact in-scene camera view has not been reproduced. Scene appearance and streaming performance still need assessment with chosen settings.

This remains CPU/Burst generation. Larger erosion/mesh detail settings add work. Source and attribution are under `Assets/Scripts/TerrainGeneration/ThirdParty/AdvancedTerrainErosion`.

## Mountain Snow Coverage

MountainSnow computes a static snow coating when control maps are generated. Temperature continuously raises the snowline from cold to hot regions; moisture lowers it modestly. At moisture 0.5, the snowline centers are generated heights 0.7, 2, 4, and 7 at temperatures 0, 0.3, 0.65, and 1. Coverage starts 0.55 below that line and reaches full climatic coverage 1.9 above it, before slope/exposure adjustments. Generated heights convert to world Y with meshHeightMultiplier * worldScale (60 in SmearScene). These are artistic thresholds for the compressed world, not real-world elevations.

The fixed climatic shaded direction is +Z, with prevailing wind toward +X. Sheltered hollows lower the snowline; exposed ridges retain somewhat less snow. The normal uses the actual vertical height multiplier. Retention tapers between 55 and 85 degrees to accommodate the steep, vertically exaggerated mountains. Broad world-coordinate noise varies the snowline, and smaller breakup fades away in upper snowfields. Tune these values in MountainSnow.cs.

Coverage fades into the existing lowlands between mountain masks 0.25 and 0.45, and between heights 0.6 and 1.5 above the water level. Existing lowland snow and tundra dusting remain intact even where the mountain mask overlaps them. Mountain snow can coat rock or grass without changing gameplay biome/surface labels; legacy mountain Snow materials recover rock underneath where snow retreats. Shorelines and river banks are excluded. No geometry, collision, dynamic weather simulation, extra shader textures, or per-frame snow work is added.

Rendered mountain snow applies a configurable coverage gamma boost in MountainSnow.Apply before writing the snow control-map weight. This keeps the same generated distribution while making partially blended mountain snow read brighter against rock. Tune **Mountain Snow Blend Gamma** on WorldManager; 1 disables the boost, and lower values make blended snow more prominent.

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
