# Cliff and ice edge gilling

The November 5 screenshot showed two related grid artifacts at steep lips. The
former quadratic cap/toe transition occupied only about 1.6 near-grid intervals
on a default ice front. Its tangent was continuous but its curvature jumped at
the joins. Vertex normals were then interpolated across triangles connecting a
nearly flat top to a steep face, producing repeated bright teeth. Rock/snow/ice
control interpolation could strengthen the visible pattern. Texture repetition
was not the cause; the normal-only GPU captures reproduce it without albedo.

## Changes

* `WorldGlacialLandforms.CliffRamp` now joins the cap, central face and toe with
  continuous tangent and curvature (C2). Fillets use about 8.32 canonical terrain
  units, clamped to 8–42% of the face width. At default settings this is about
  2.5 m of horizontal transition. Width and total rise are preserved; the steep
  middle remains steep. Wider travel openings retain a smaller fillet fraction.
  The rule is independent of render LOD, frame timing and chunk residency.
* The rock side/headwall intersection uses a locally rounded maximum, avoiding
  an inward crease caught by the unchanged single-cell spike check. The rounding
  adds at most 1.8 m at default scale and is zero on the valley floor/open pass.
* `HeightfieldCliffNormal.hlsl` corrects excessive interpolated-normal deflection
  on steep triangles. Gentle ground and small curvature retain smooth normals;
  a blanket flat-normal replacement was rejected because it exposed facets.
  The correction ramps between 8 and 20 degrees of disagreement and leaves
  excavation geometry's separate normal path intact.
* On supported cold ice faces, filtered ice/rock weights are resolved using the
  geometric slope, reducing repeated rocky lobes at the toe. Flat adjacent ground
  retains its material weights. The cold-province result is reused, so this does
  not introduce another geography evaluation or texture lookup.

The canonical height change applies to cold landforms. The material's steep-face
normal correction also helps cliffs in other biomes. Existing desert heights,
dry-desert water exclusion, mesh topology and collider resolution are preserved.
Generator identity is `firstsettlers-glacial-cliffs-v7/triangle-ac-v1`; landform
settings remain schema 4. The scene's current seed 91 and custom tuning were kept.
Restart Play Mode/regenerate terrain to rebuild the lips.

## Native comparisons

The original pre-fillet captures are archived in `Images/TerrainEdges/BeforeLipFix`.
They use the old geometry and ordinary interpolated normals. `SmoothOnly` files
hold the revised geometry fixed and disable just the shader normal correction,
isolating its effect. Captures use Unity D3D11, the runtime material and an
isolated scene, with both shadow-on and shadow-off views. Bounded preview tiles
omit foliage, sky and fog.

| View | Before | After |
| --- | --- | --- |
| Oblique ice edge | [Original](Images/TerrainEdges/BeforeLipFix/Glacial_Before.png) | [Revised](Images/TerrainEdges/Glacial_After.png) |
| Ice edge with shadows | [Original](Images/TerrainEdges/BeforeLipFix/Glacial_Before_Shadows.png) | [Revised](Images/TerrainEdges/Glacial_After_Shadows.png) |
| Rock crest, revised geometry | [Smooth normals only](Images/TerrainEdges/RockLip_SmoothOnly.png) | [Corrected](Images/TerrainEdges/RockLip_After.png) |
| Close glacier terminus | | [Current view](Images/GlacialLandforms/IceFront_Close.png) |

The oblique view uses seed 43, observer world XYZ approximately
`(12304.70, 489.71, -11352.66)`, looking at `(12382.10, 409.71, -11363.10)`.
The existing [ground-view locations](GLACIAL_LANDFORMS.md) remain usable.

## Verification and limits

Run `Tools/ValidateWorldLandforms.ps1 -Edges` for the focused fixture. It captures
production near meshes at step increments 1, 2, 4 and 8, including their stitched
borders, plus the real glacial rock/ice edges. GPU normal-only captures compare
interpolated normals with the triangle plane. On the revised geometry, maximum
steep-face deflections of about 28–83 degrees at coarser steps were reduced to
about 11 degrees. Gentle-pixel changes were below 0.11 degrees (8-bit diagnostic
quantization). Exact measurements are in
[the edge report](Images/TerrainEdges/validation.txt). These are controlled GPU
normal measurements, not a live FPS benchmark or proof that every distant
silhouette is perfectly smooth.

The glacial suite passes 268,237 checks, including monotone C2 profiles,
floor/height/midpoint endpoints, coastal continuity, strict isolated-spike checks,
sustained cliff rise, batch/order repeatability, signed seams, near/far/density
agreement and 21,732 vertical/face-normal PhysX rays. Selected rock faces still
rise 76.6–82.1 m above 60 degrees; ice faces rise 38.6–40.1 m above 70 degrees.
The focused edge suite passes 43 checks. The main terrain suite passes 145,020
checks and the earlier seed 66/67/68 ridge regression passes 1,002,309 checks,
including LOD topology and PhysX alignment. All four suites passed on the final
code and shader state.

No additional runtime meshes, control textures, samplers or render passes were
added. Height evaluation adds a few arithmetic operations; the shader reuses
position derivatives already needed for texture sampling. Coarse LODs retain
finite silhouette resolution, and very fine material boundaries can still show
the control-map grid. Cross-platform bitwise equivalence remains unverified;
the existing seeded canonical sampling and generator-identity checks are retained.
