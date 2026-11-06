# Cold valleys, snow and glacier ice

Implemented November 5, 2026, then revised after the first site review. Cold
catchments now pair broad valley interiors and rocky benches with tall, steep
rock headwalls and substantial blue ice faces. Floor smoothing, rock relief and
ice thickness have separate profiles. Upper bowls, asymmetric shoulders, local
openings and occasional passes retain different travel opportunities.

Restart Play Mode or regenerate terrain after Unity imports the assets. Current
scene seed 91 and existing custom mountain, erosion, material and streaming
settings are preserved. The generator identity changed, so existing saved edit
baselines must not be silently reused against the new heights.

## Terrain and traversal

The catchments use seeded world-space anchors, curved axes and varying widths.
Broad, piecewise planar changes of rim direction give rock faces irregular
facets. Curvature-continuous toes and lips join nearly planar steep faces, with
a transition wide enough for the canonical near grid. Side walls and headwalls
use a locally rounded maximum at their intersection. Selected partial openings widen and lower one side; upper bowls retain
stronger headwalls, while pass variants omit the enclosing headwall and carry
little ice. These are incomplete travel opportunities, not guaranteed staircases.
Upper terrain outside these regions keeps the previous broad-upland behavior.

At the saved scene scale, a typical catchment is several hundred metres long,
with floor and shoulder widths sufficient for travel and future settlement
layouts. The four selected centre chunks have 87.1–100% of samples below 15
degrees. This describes selected interiors, not every mountain or the entire
glacier footprint. Dedicated cross-sections through three catchments show
sustained rock rises of **76.6–82.1 m above 60 degrees** and ice rises of
**38.6–40.1 m above 70 degrees**. Their mean steep-face angles are approximately
82–84 degrees. These are sustained face measurements, not isolated maximum slopes.

Floor cutting/uplift is smoothly bounded. Wall and ice rise are added separately,
so the floor limit cannot suppress a cliff into a rounded mound. Catchment
envelopes reach exactly zero before their planning-cell boundaries. The taller
profiles use a broader, continuous shoreline protection fade, including where
erosion has brought otherwise high ground close to water. The formations remain
heightfields with finite face widths, without overhangs or floating ice meshes.

The main glacier body is nominally 39 m thick at the saved scene scale, with
regional modulation and smaller hanging ice skins. Its side margins and downhill
terminus use the same solid heightfield and collision as the rest of the terrain.
Exposed blue coverage follows this new footprint, including its steep faces;
quiet snow patches can remain on the top. Rocky benches remain beside the ice.
Shallow seams remain traversable; this pass does not add
deep crevasse mazes, ice caves, glacier flow, seasonal melting, lake freezing,
sliding physics or an ice resource-volume simulation.

## Snow distribution and coverage

The existing climate, altitude, aspect, wind and shelter rules remain active.
Already cool climates receive a small cooling adjustment, with a stronger bias
inside selected cold provinces. Temperatures at or above 0.65 retain their
existing warm values, and glacial province strength is zero in the arid field
above 0.15. Existing hot/dry climate eligibility and dry-desert water exclusion
pass their regressions.

A matched four-seed survey using actual canonical heights, slopes and climate
increased Snow biome labels from **4,194 to 5,321 out of 58,564 samples**:
**7.16% to 9.09%**. This is a sampled coverage comparison, not a whole-world area
percentage or a claim that every seed gains the same amount of snow.

Slope retention now tapers nominally from 35 to 70 degrees, preserving snowfields
on gentle ground while exposing more steep headwalls. Low-mask Snow terrain also
exposes rock above 45 degrees. Seeded blue ice windows and scoured moraine patches
are explicitly preserved through the snow overlay, so accumulation cannot turn
every visible glacier or rocky shoulder into a uniform white coating.
The terrain material also sheds interpolated snow on geometric faces between
approximately 53 and 70 degrees when glaciers are enabled, above the waterline.
This limits repeated snow teeth where a coarse distant control pixel spans a cap
and headwall. It redistributes snow to rock without new textures or height reads;
the baked biome and gameplay surface fields retain their canonical policies.

## Generation and determinism

1. Evaluate the same early aridity/cold potential and seeded catchment plan from
   absolute coordinates, independently of final biome labels.
2. Generate the existing broad mountains, regional rock formations, erosion,
   river carving, dry surfaces and uplands.
3. Finish suitable cold catchments, their rock shoulders, solid ice and small
   sheltered drifts in the canonical height sampler.
4. Derive final slopes, temperature/moisture, biomes, ice/moraine surfaces, snow
   cover and vegetation eligibility.

Near/far/macro terrain, colliders, habitats, horizon reads and excavation density
use that same sampler. Surface jobs receive the actual temperature map; distant
and point habitat paths use the same temperature-aware surface evaluator. No
clock, frame number, mutable random stream, chunk loading order or neighboring
chunk residency participates in generation.

The identity is `firstsettlers-glacial-cliffs-v7/triangle-ac-v1`. New settings
are fingerprinted. Version-2 landform settings migrate through version 3;
version-3 settings migrate to version 4 with the three new cliff controls while
preserving previous customization. Production sampling remains strict Burst math.
Repeated/reversed native sampling is verified on this Unity/Burst build;
cross-platform bitwise equality remains a future networking check for the
floating-point generator.

The new controls are under World Manager's regional landform settings:

| Control | Default | Meaning |
| --- | --- | --- |
| Glaciers | on | Enables the cold/glacial additions; off restores the earlier climate and terrain path. |
| Glacial Strength | 1 | Regional glacier/catchment strength, from 0 to 1. |
| Cold Expansion | 0.025 | Small cooling of already cool climates, independent of the stronger local cold-province bias. |
| Glacial Relief | 1.2 | Nominal rock-wall rise in normalized height units; 72 m at the saved scene scale. |
| Ice Front Height | 0.65 | Nominal solid glacier thickness; 39 m at the saved scene scale. |
| Glacial Wall Width | 44 | Rock transition width in terrain units; 13.2 m at scene scale. Ice uses 45% of this width (5.94 m). |

## Materials and textures

Ice is opaque, muted blue-grey and milky, with gentle internal variation and
restrained normal detail. The cliff revision reuses the [ice albedo, normal and authoring height maps](../ArtReferences/GlacialIce/README.md),
which complement the existing snow and stone textures. Four overlapping texture windows
avoid visible source-edge seams; triplanar projection covers floors and fronts.
Normal detail fades from 35–110 m; albedo detail fades from 90–260 m to its
linear-light texture average. No periodic horizontal strata tint was reintroduced.
The revised ice tint is `(0.84, 0.92, 1)` and multiplies both detailed and distant
ice consistently. Existing material customization is preserved by the installer;
the deeper tint is the default for a first install. No new bitmap was needed for
this revision.

Ice reuses the riverbed control channel only above water in cold provinces.
Submerged beds and arid clay retain their original meanings. Moraine reuses the
rock channel and the existing regional stone texture. Ice and Moraine are appended
surface labels, preserving previous ordinal values. There are still three
control maps, with no additional render pass or sampler register.

The two bound 1024-square ice maps use BC7 albedo and BC5 normal compression,
about **2.67 MiB including mipmaps**. The authoring height map is not bound at
runtime and does not drive gameplay geometry. Nearby ice can require up to 12
albedo and 12 normal reads across all three projections, with fewer on aligned
surfaces and none after the detail fade. Height generation has additional work
in glacial catchments; the warmed fixture medians are recorded in the validation
report. No live-world FPS/GPU benchmark was performed.
The final warmed cold-centre fixture measured **13.176 ms without glaciers versus
15.861 ms with them** for a 128-unit heightfield. This includes the whole glacial
pass, not just this revision relative to the previous soft profile. The existing
runtime terrain LOD/collider resolution, control-map count and texture allocations
are unchanged by the cliff revision.

## Validation and previews

Run `Tools/ValidateWorldLandforms.ps1 -Glacial` for the glacial fixture,
`Tools/ValidateWorldLandforms.ps1` for the full terrain suite, and
`Tools/ValidateWorldLandforms.ps1 -TerrainIssues` for the earlier seed 67/68 ridge
and collision regression. These run in an isolated Unity project and do not
alter the open scene.

The glacial suite covers the four-seed Snow survey, full catchment continuity,
coastal protection, warm/arid exclusions, settings migration, identity changes,
reversed native batches, signed-coordinate seams, near/far surface weights,
excavation baselines, shader variants, texture imports and native renders.
Steep-face checks measure contiguous rise above a slope threshold, and spike
checks detect single-cell slope reversals without rejecting genuine cliffs.
PhysX checks include vertical rays against triangle-interpolated ground and rays
cast toward steep faces along their surface normals. The exact count and maximum
errors are recorded in the report. CPU/GPU cold fields also
pass the full suite's 0.0003 agreement bound, including extreme signed seeds.

The final glacial pass has **268,237 checks** and **21,732 actual PhysX rays**
across the four interiors and seven steep-face chunks. The largest recorded
vertical error is 0.000092 m; face-normal rays use a 0.003 m tolerance. The earlier
seed 66/67/68 ridge regression passes 1,002,309 checks, and the main terrain suite
passes 145,020 checks, including erosion, scheduling, slope and snow regressions.

The exact pass counts, sampled slopes, wall locations, seed/terrain coordinates
and warmed height-field timings are in [validation.txt](Images/GlacialLandforms/validation.txt).
Multiply terrain XZ by 0.3 for world XZ at the saved scene scale. The native
previews omit foliage, sky/fog and water, using the runtime terrain material.
Bare previews show the same geometry without snow/ice material coverage.
Cliff close-ups use a neighbourhood of native terrain tiles with 0.3 m geometry
spacing; overviews use 1.2 m spacing. Preview resolution changes do not change
runtime LOD settings. Coarser distant meshes still approximate sharp lips.
The original four catchment anchors remain unchanged for direct site comparison.
The later [edge-gilling fix](TERRAIN_EDGES.md) documents the C2 lips, restrained
normal correction, material transitions, focused LOD diagnostics and paired views.

| Formation | Snow/ice | Bare geometry | Close view |
| --- | --- | --- | --- |
| Glacier valley | [View](Images/GlacialLandforms/GlacierValley.png) | [View](Images/GlacialLandforms/GlacierValley_Bare.png) | [View](Images/GlacialLandforms/GlacierValley_Close.png) |
| Shoulder benches | [View](Images/GlacialLandforms/ShoulderBenches.png) | [View](Images/GlacialLandforms/ShoulderBenches_Bare.png) | [View](Images/GlacialLandforms/ShoulderBenches_Close.png) |
| Upper bowl | [View](Images/GlacialLandforms/UpperBowl.png) | [View](Images/GlacialLandforms/UpperBowl_Bare.png) | [View](Images/GlacialLandforms/UpperBowl_Close.png) |
| Mountain pass | [View](Images/GlacialLandforms/MountainPass.png) | [View](Images/GlacialLandforms/MountainPass_Bare.png) | [View](Images/GlacialLandforms/MountainPass_Close.png) |
| Exposed ice front | [View](Images/GlacialLandforms/IceFront.png) | [View](Images/GlacialLandforms/IceFront_Bare.png) | [View](Images/GlacialLandforms/IceFront_Close.png) |
| Rock headwall | [View](Images/GlacialLandforms/RockWall.png) | [View](Images/GlacialLandforms/RockWall_Bare.png) | [View](Images/GlacialLandforms/RockWall_Close.png) |

The previous soft profiles and their exact original renders are preserved in
[Images/GlacialCliffs/Before](Images/GlacialCliffs/Before). Compare
[the original valley](Images/GlacialCliffs/Before/GlacierValley.png) with
[the revised valley](Images/GlacialLandforms/GlacierValley.png). Ground-view
observer and look-at XYZ coordinates are written into the validation report.

Both of these ground views use **seed 43** and the saved scene's scales. Observer
positions are 5 m above canonical ground, so a player can settle onto the surface.

| View | Observer world XYZ | Look toward world XYZ |
| --- | --- | --- |
| Blue glacier terminus | `(12296.24, 390.97, -11186.40)` | `(12310.42, 401.26, -11206.99)` |
| Rock wall beside ice | `(11408.79, 227.34, -11459.92)` | `(11378.17, 227.10, -11522.87)` |
