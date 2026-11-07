# Phase I cave shaping handoff

**Paused.** Procedural cave generation is disabled in SmearScene and defaults to off in other scenes. The playtest found the planned paths extremely unnatural. If the work resumes, start with standalone caverns and optional natural openings. Fully enclosed caves should still be discoverable by mining. The design does not require planned corridors between cave parts or a cavern connection system.

## Implemented

- Planner schema 2 builds underground networks with varied junctions, curved passages, loops, branches and asymmetric chamber lobes. Hillside connections are optional. Entrance search uses explicit surface entrance metadata.
- Enclosed systems retain procedural air beneath intact terrain. They prepare meshes within 96 m of their bounds. Excavation can open their walls through the ordinary edit transaction.
- Passages have coherent width, strata and roof variation. Walkable bands retain clearance and restrained gradients. Geology influences room size and roof profile. Limestone deposits and boulders are solid, excavatable density features placed outside the walking bands.
- Terrain local lights use camera-relative receiver-plane derivatives. Exposed stone receives up to 25 mm of normal-space tolerance, projected onto each shadow ray. Other receivers use the 6 mm radial default.

## Verified

- Final CPU suite passed with the varied layouts and tapered formation tips. It covers deterministic planning, cache bounds, sealed cave exclusion from search, deferred enclosed mesh preparation, physical mining breakthrough, formation collision and removal, seams, route clearance, and native camera/controller excavation.
- Native seed 1 entrance is at (958.330, 388.530, 274.486). The fixture has 1,210 m of routes, 72 spans, 190 volume tiles and 4 source chunks. Final derived use is 154.1 MiB including the sky reservation, with 1,832,322 triangles. Search took 0.10 s. See .utmp/terrain-sculpting/cave-natural-cpu-final.log.
- Existing terrain regressions passed 8,565 density assertions and 3,609,242 transaction/streaming assertions. See .utmp/terrain-sculpting/cave-natural-integration-final.log.
- The distant planar receiver fixture reported zero unexpectedly shadowed samples out of 23,047 with either derivative path. A follow-up convex terrain fixture compared a clear-light reference against actual shadow casting. It measured zero of 26,744 significant light losses with the slope-aware tolerance and two with the original radial tolerance.
- Cave graphics passed 47 checks. The convex test recorded zero of 26,736 significant light losses in the full run. Three seeds produced passage, room and formation renders. Thin stepped shadows remain in some corners, and the native mouth still has a regular arch. See .utmp/terrain-sculpting/cave-graphics.log.
- Shared terrain URP passed 52 checks and compiled 98 shader variants. See .utmp/terrain-sculpting/cave-phase-i-world-graphics/validation.log.
- Final loaded-terrain travel passed in 117.22 s with one previous excavation and peak source preparation of 64 out of 64 chunks. Graphics ran concurrently. See .utmp/terrain-sculpting/cave-natural-travel-final.log.
- The focused receiver rerun passed with zero of 26,744 significant light losses and exited without the persistent allocation warning after explicit edit-mode flame teardown was added. The full 47-check run predates this fixture-only change. See .utmp/terrain-sculpting/cave-receiver-validation.log and cave-receiver-before-fixture-cleanup.log.

## Resume

Tools/ValidateProceduralCaves.ps1 runs the isolated CPU project. Use -TravelChecks for cold travel and -SkipPrepare when that project already has the current sources. Tools/ValidateProceduralCaveGraphics.ps1 runs the separate graphics project. Its -ReceiverChecks switch runs the focused convex terrain case. Unity 6000.4.8f1 needs escalated local licensing and worker IPC for batch runs. Keep the hidden editor launch.

The prototype's cave memory cap is 256 MiB and the far-terrain handoff limit is 64 source chunks. Preserve the seam, collision and excavation fixes if reusing the mesh and streaming code. Preview images and NaturalNetworkPlans.svg under Documentation/Images/ProceduralCaves document the previous design.

