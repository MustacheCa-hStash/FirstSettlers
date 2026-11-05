# Terrain ridges, collision and broad uplands

This patch reproduces the four screenshot locations using SmearScene's actual
terrain settings: sample scale 600, world scale 0.3, height multiplier 200,
mountain relief 20, mountain spatial scale 3.3 and mountain coverage 1.3. Scene
settings and the user's current seed are preserved.

## Causes and changes

* **Gills and unsupported walls:** coarse near-terrain LOD stitching made fans
  anchored on a chunk boundary. With a curved height profile, boundary-only
  triangles become vertical fins. Border fans now anchor inside the chunk;
  every triangle has positive XZ area. Managed and production native paths share
  the topology, with complete full-resolution borders at every tested LOD.
* **Narrow elevation spikes:** directional erosion used the sharp gradients of
  newly added cliff thresholds. Rapid gradient rotations steered noise into
  near-vertical cuts. The broad underlying terrain now guides erosion and its
  local-relief target. Added caprock receives the resulting displacement with
  protection, without steering its direction.
* **Collision mismatch:** runtime colliders skipped eight height samples, missing
  narrow crests even at the player's position. They now use every sample and the
  same AC diagonals as LOD0. Tests cook actual PhysX mesh colliders and raycast
  arbitrary points inside triangles, rather than comparing only grid vertices.
* **Excessive highland complexity:** higher terrain blends small rock remnants,
  contour canyons and angular ridges into broad regional benches. Fine erosion
  fades into a lower-amplitude two-octave profile. Soft height terraces add
  gently inclined shelves with regionally varied levels and rounded connecting
  shoulders within raised plateau provinces. Ordinary rounded mountains do not
  receive repeated terrace bands. All of these
  transitions depend on continuous world elevation, not a special-case seed or
  screenshot coordinate. Lower cliffs and dune country remain varied.
* **Dry surface spillover:** basin-floor flattening is restricted to low ground.
  High shelves no longer receive lowland clay/wash channel markings.

The revised canonical height generator feeds detailed/far terrain, habitat,
collision, snow, shadows and excavation baselines. All inputs remain seeded and
independent of chunk order or residency. The generator identity is
`firstsettlers-broad-uplands-v4/triangle-ac-v1`, preventing saved edits made for an
older baseline from being silently applied to these new heights. Cross-platform
bitwise equivalence remains a future networking check for the existing floating
point generator.

## Reported locations

The survey covers a 77.1 m square around each world XZ point. Maximum edge rise
uses the canonical 0.3 m grid; cliff shoulders can still be steep. Gentle ground
means a slope below 15 degrees. These are local samples, not world-area totals.

| Seed | World XZ | Maximum edge rise before → after | Gentle ground before → after |
| --- | --- | --- | --- |
| 67 | -211.09, 704.97 | 14.63 → 1.02 m | 72.1 → 77.4% |
| 67 | -110.05, 780.91 | 11.76 → 1.01 m | 50.6 → 55.9% |
| 67 | 286.72, 1369.66 | 15.38 → 0.52 m | 0.9 → 34.0% |
| 68 | -261.91, 504.08 | 8.73 → 0.92 m | 53.7 → 69.1% |
| 66 control | 286.72, 1369.66 | 0.079 → 0.079 m | 100 → 100% |

The high seed 67 sample is now about 201.4 m high at its center, compared with
216.9 m before. Its maximum second height difference fell from 19.14 m to about
0.006 m. This is smoother, more usable high ground with varied levels; it does
not make every mountain slope suitable for a building. Seed 66's control point
and sampled slope profile remain unchanged; this is not a claim that the entire
seed 66 world is unchanged.

## Validation and inspection

Run `Tools/ValidateWorldLandforms.ps1 -TerrainIssues` for the scene-specific
regression, and `Tools/ValidateWorldLandforms.ps1` for the full terrain suite.
Both use an isolated Unity project and native D3D11 rendering. The historical
`Before` captures were recorded before the patch; `-Baseline` is intended only
when recording a new baseline before changing generation, and will overwrite
that folder with the current generator if run now. `-TerrainIssues -Probe`
prints individual generation-stage values for diagnosis.

The final scene-specific regression passes 1,002,309 assertions. At the five
fixtures, LOD steps 1/2/4/8/16 have no vertical/reversed triangles and
cover each chunk exactly. PhysX reports zero missed rays across 9,245 subcell
samples, with maximum error below 0.05 mm relative to the rendered LOD0 mesh.
Native and managed mesh topology agree. Soft terrace repeats are checked for
continuity and monotonicity, with explicit lowland/water bypass checks.

The full suite passes 143,864 checks plus the existing erosion, sampling-budget,
slope and snow suites. It verifies repeated/reversed generation, signed-coordinate
seams, near/far/macro/collision/excavation agreement, dry-desert water exclusion,
CPU/GPU regional fields, texture bindings and shader compilation.

Runtime collision is now 32,768 triangles per 128-unit chunk, compared with 512
at the former eight-sample spacing. Collider distance, worker limits, queues and
asset lifetime remain bounded by the existing streaming policy. This patch does
not include a live-world FPS benchmark, so collider cooking and memory are the
main performance cost to inspect during play.

Restart Play Mode or regenerate terrain to inspect the new geometry. The paired
native previews below omit foliage and water to expose the actual ground shape;
they use the same camera and settings before and after.

| Site | Before | After |
| --- | --- | --- |
| Seed 67 forest ridge | [Before](Images/TerrainRidges/Before/67_ForestRidge.png) | [After](Images/TerrainRidges/After/67_ForestRidge.png) |
| Seed 67 lakeside collision | [Before](Images/TerrainRidges/Before/67_LakesideCollider.png) | [After](Images/TerrainRidges/After/67_LakesideCollider.png) |
| Seed 67 high upland | [Before](Images/TerrainRidges/Before/67_HighUpland.png) | [After](Images/TerrainRidges/After/67_HighUpland.png) |
| Seed 68 grassland split | [Before](Images/TerrainRidges/Before/68_GrasslandSplit.png) | [After](Images/TerrainRidges/After/68_GrasslandSplit.png) |
| Seed 66 control | [Before](Images/TerrainRidges/Before/66_Control.png) | [After](Images/TerrainRidges/After/66_Control.png) |

[Exact regression output](Images/TerrainRidges/After/validation.txt)
