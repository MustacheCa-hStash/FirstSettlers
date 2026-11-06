# Regional desert and rock landforms

Larger connected desert provinces now contain dune fields, sedimentary plateaus,
mesas and detached buttes, stepped escarpments, canyon cuts, dry washes, dry
mountain ridges, small badland shoulders, stony plains and pale dry basins.
Plateau provinces also extend into temperate terrain, with weaker uplift there.
The current stone and sand textures are reused, with new generated cracked clay
and dry-wash gravel for exposed basin floors and drainage beds.

Restart Play Mode, or use **World Manager → Regenerate Terrain**, to generate the
new world. The scene's seed is preserved. Controls are under **Terrain Shape and
Climate → Erosion → Regional Desert and Rock Landforms**.

**Dry Deserts** is enabled. Desert cores have no wet-river carving or standing
water: low terrain is smoothly lifted to a dry datum, preserving gentle relief,
then receives local basin and wash shaping. The water-state, surface, collision
and near/far mesh paths all consume that same terrain. This changes the ground
geometry and wet-channel mask rather than merely hiding a water renderer.

## Formation order

1. Sample stateless world-space aridity, cold/glacial potential, geology, plateau
   province, sand supply and basin fields. These require the seed and coordinates, not a
   finished heightmap or biome label.
2. Generate the existing broad land/mountain surface. Add regional plateau
   relief, cliff transitions, detached remnants, secondary ledges, connected
   canyon cuts and dry mountain ridge shaping. The shaped base retains analytic
   derivatives, while erosion direction and its relief target use the original
   broad surface. Sharp cliff gradients must not steer the directional filter.
3. Add erosion displacement to the shaped surface, reducing erosion on protected
   plateau formations and high uplands. Access shaping also recognizes those
   formations, preserving their relief.
4. Carve wet rivers outside desert cores, reducing their influence within raised
   plateau formations and the broad arid transition. In dry provinces, preserve
   positive relief above a local dry datum, carve independent ephemeral washes
   and flatten suitable basin interiors. Dry beds stay above the water plane.
5. Accumulate dunes on suitable dry, gently sloped, sand-bearing lowlands. Dunes
   taper across broad wash aprons and avoid active rivers, plateau outcrops and
   mountain slopes. Their windward rise is longer than their slip face, with
   rounded crests and slowly warped spacing. Small badland ribs add detail to
   exposed dry sedimentary shoulders. Basin-floor flattening is confined to low
   ground; elevated rock shelves do not become sea-level clay pans.
6. At high elevations, replace small plateau remnants and contour cuts with broad
   provincial benches and fade fine erosion detail. Raised plateau provinces
   receive gently inclined shelves and rounded shoulders with regionally varied
   levels; ordinary high mountains retain their rounded profile.
7. Finish suitable [cold catchments and solid glacier bodies](GLACIAL_LANDFORMS.md),
   with broad valley floors, shoulders, upper bowls, passes and selected ice fronts.
8. Calculate final slopes, then final climate, biomes, surfaces and ground cover.
   Final climate uses the same early aridity field, biasing its desert cores
   toward warm/dry conditions. Existing hot/dry patches remain eligible.
9. Place foliage and sparse desert boulders after surfaces are known.

This keeps landforms in the canonical height generator. They are not height edits
applied to chunks after biome assignment.

## Forms and surfaces

| Form | Behavior |
| --- | --- |
| Dunes | Connected sand-bearing areas, seeded wind direction, curved asymmetric crests; current validation samples reach about 6 m of accumulation. |
| Mesas and buttes | Raised sedimentary interiors with steep perimeter transitions and smaller detached remnants in the same province. |
| Escarpments | Secondary ledges and stepped shoulders along the plateau field. |
| Canyons | Connected warped contour cuts through raised plateau terrain; the floor follows underlying relief. |
| Dry mountains | Angular lower shoulders transition to broad, softly terraced uplands; the highest peaks still support the existing snow system. |
| Badland shoulders | Smaller exposed ribs and gullies where sedimentary dry ground slopes away from flatter land. |
| Dry washes | Separate from wet rivers; gravel/silt beds and exposed rock, with broad dune exclusion aprons. |
| Stony plains | Desert surfaces with little sand supply use the existing rock layer for exposed bedrock/gravel. |
| Dry basins | Suitable lowlands flatten toward dry cracked-clay pans, retaining gentle relief and quieter interiors. Sand-rich dune troughs retain sand. |

Hot/dry mountain benches can now classify as desert. Steeper faces retain the
existing Rock/Cliff surface classifications. Their geology remains available
through the independent regional field, so an exposed desert cliff keeps its
stone tint even when its biome label is Rock.

Desert boulders reuse the existing bare grassland rock prefabs and size ranges.
They favor exposed rocky terrain, avoid dune cores, basin cores, wet channels and
steep slopes, and have a maximum of three per chunk, subject to the existing rock
budget. Placement has a boundary margin and deterministic local exclusions.

## Appearance

Terrain, excavated stone and the instanced WeatheredRock shader share regional
geology tints: warm ochre sandstone, pale limestone, cooler granite and brown
desert bedrock. Transitions are continuous in world space. Rock tint has no
repeating height bands. Both nearby texture detail and distant rock fallback colors
receive the tint. Sand also receives warm-desert variation. The new dry-wash and
cracked-clay labels use [generated sediment textures](../ArtReferences/DesertSurfaces/README.md),
including subtle estimated normal maps and texture-average distance colors.

`WorldLandformAppearance` binds integer seed and coordinate scales when a
`ChunkManager` is created, and clears them when that world is disposed. There is
one active rendered world, matching the existing shared shader-global model.
Standalone material previews retain the base material appearance until bound.
Full signed seed bits are sent with `Shader.SetGlobalInteger`.

No additional terrain control-map textures or per-instance materials are
allocated. Dry sediments reuse the mud/riverbed control channels regionally and
share the sand repeat sampler. Their two albedo/normal pairs add about 5.33 MiB
with desktop mipmaps; distant sediment detail fades to the texture average.

## Controls and scales

`WorldLandformSettings` is stored inside the existing immutable erosion settings
snapshot. Version-4 scenes migrate to version 5 with regional defaults; the main
SmearScene has the new settings explicitly serialized. Regional settings migrate
from version 1 to 2 with Dry Deserts enabled.
Version 2 migrates to version 3 with glaciers enabled, strength 1 and cold
expansion 0.025, preserving all previous regional settings.
Version 3 migrates to version 4 with independent glacial rock rise, ice height
and wall width, preserving existing customization.

| Control | Default | Meaning |
| --- | --- | --- |
| Enabled | true | Enables regional shaping, climate bias, surface selection and appearance. |
| Dry Deserts | true | Excludes wet-river carving and keeps arid core ground above the water plane. |
| Desert Region Scale | 18 | Broad climate-province scale relative to Sample Scale. |
| Plateau Relief | 1.8 | Nominal normalized uplift, modulated by province, original terrain and formation shape. |
| Plateau Scale | 1.4 | Shape scale relative to Sample Scale. |
| Cliff Width | 0.10 | Transition width in the plateau field; smaller is steeper. |
| Dune Height | 0.12 | Maximum normalized sand accumulation before regional envelopes. |
| Dune Wavelength | 200 | Crest spacing in terrain XZ units. |
| Dry Wash Depth | 0.18 | Maximum normalized cut, limited by dry-bed clearance. |
| Glaciers | true | Enables cold catchments, solid ice and the cold-climate additions. |
| Glacial Strength | 1 | Strength of regional cold/glacial provinces. |
| Cold Expansion | 0.025 | Small cooling of already cool climates. |
| Glacial Relief | 1.2 | Local cold rock-wall rise, independent of floor smoothing. |
| Ice Front Height | 0.65 | Solid glacier thickness and exposed-front rise. |
| Glacial Wall Width | 44 | Rock face width in terrain units; ice uses 45% of this width. |

At the scene's World Scale 0.3 and Height Multiplier 200, one terrain XZ unit is
0.3 m and one normalized height unit is 60 m. The default dune wavelength is
therefore about 60 m. These are generation parameters; change them before
regenerating. Changing render LOD does not change the field wavelength.

Upland shaping is elevation-dependent rather than seed-specific. Small rock
formations blend into broad province-scale benches between 0.9 and 1.8 normalized
height units above the water datum. Fine erosion and dry surface detail fade
between 1.4 and 3 units above water. Raised plateau formations receive soft
shelves with a 0.75-unit height spacing, regionally varied levels, a gentle
positive bench slope and smooth
connections. At the scene scale these bands correspond to 54–108 m and 84–180 m
above water, with about 45 m between shelf levels. Lower terrain keeps its local
landforms. Ordinary high mountains are not terraced. High shelves retain bedrock
instead of lowland wash/clay markings.

## Determinism and compatibility

New fields are pure functions of integer seed, immutable settings and absolute
terrain coordinates. Hashing uses fixed unsigned integer arithmetic; no mutable
random stream, load order, clock, frame number or neighbour residency enters
landform generation. Production height/climate/surface jobs explicitly use strict
Burst floating-point math.

Near terrain, far/macro terrain, collision, distant tree habitats and cold
excavation reads all consume the same generation functions. Surface and climate
sampling use the same regional settings on each path. Shader geography mirrors
the CPU integer hash and field equations, but gameplay never depends on GPU
field results.

The baseline identity is now
`firstsettlers-glacial-cliffs-v7/triangle-ac-v1`; all landform settings are
included in the existing fingerprint. Future multiplayer peers should exchange
the seed, sanitized settings and generator identity, and reject incompatible
generators. Bitwise scheduling repeatability is verified on this Unity/Burst
build; cross-platform bitwise equivalence remains a networking integration check,
since the complete existing generator uses floating-point noise and erosion.

## Validation and previews

Run `Tools/ValidateWorldLandforms.ps1`. It prepares an isolated Unity project using
installed package sources, then runs native Burst and D3D11 checks without editing
the open world. The latest run passed **145,020 checks**, plus the existing world
erosion, six-batch admission/concurrency, terrain slope and mountain snow suites.

Coverage includes analytic base gradients, signed coordinates, settings migration,
fingerprint changes, reversed sampling order with bitwise results, repeated
chunks, X/Z height/slope seams, detailed/far/macro height agreement, collider and
excavation baselines, detailed/distant ecological surfaces, desert materials and
snow overlays. GPU/CPU geography agrees within 0.000006 across normal, negative
and extreme signed 32-bit seeds. The tested default dune-only gradients remain
below the 34-degree loose-sand boundary.

The dry-desert survey checks 3,899 native samples across three seeds, including
1,064 formerly submerged locations. Core samples have no wet river mask and
classify as dry. Detailed water meshes at three LOD steps and distant desert
water meshes are empty. Both generated texture families pass 2D/sRGB/normal,
compression, mip and material-binding checks.

Across the three-seed climate survey, hot/dry samples increased from 508 to 5,021
out of 11,163. This describes sampled climate eligibility, not a measured whole
world land-area percentage. The original hot/dry samples remain eligible.

The dense previews also exposed a pre-existing 16-bit mesh index limit.
`MeshData.CreateMesh` now selects 32-bit indices above 65,535 vertices, with a
regression check that the final mesh vertex remains referenced correctly.

Native previews use the runtime terrain material. These are isolated terrain
patches with foliage omitted; separate geometry tests verify the dry-desert water
meshes are empty.

- [Plateaus, buttes and cliff cuts](Images/WorldLandforms/Plateaus.png)
- [Actual dune accumulation after the complete terrain pipeline](Images/WorldLandforms/Dunes.png)
- [Dry basin](Images/WorldLandforms/DryBasin.png)
- [Actual dry wash](Images/WorldLandforms/DryWashes.png)
- [Cracked clay close-up](Images/WorldLandforms/CrackedClay_Close.png)
- [Wash gravel close-up](Images/WorldLandforms/DryWash_Close.png)
- [Dry mountains with high-elevation snow](Images/WorldLandforms/DryMountains.png)
- [Validation marker and reproducible coordinates](Images/WorldLandforms/validation.txt)

This is heightfield terrain: steep walls and open cuts are supported, while
overhangs, arches and undercut ledges still require separate mesh/density work.
Canyon/wash patterns are procedural formation fields, not a water-flow simulation.
The validation is not a live-world frame-time benchmark.

## Ridge, collision and upland regression

See [the seed 67/68 regression notes](TERRAIN_RIDGES.md) for the reported screenshot
locations and before/after renders. Near LOD stitching now anchors border fans
inside each chunk, removing vertical boundary-only triangles. Native and managed
meshes share that topology. Runtime colliders use the full-resolution grid and
the same AC triangle diagonals as LOD0, instead of skipping eight samples.
This increases collider geometry within the existing bounded collision radius;
worker, streaming and lifecycle budgets remain active.
