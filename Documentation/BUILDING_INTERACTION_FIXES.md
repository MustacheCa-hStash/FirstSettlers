# Building interaction and preview updates — October 22, 2026

These rules supersede the older midpoint-stack, wall-centred floor, 0.5 m ordinary
limestone wall, and 1.5 m limestone stair descriptions in the historical kit guides.

## Placement

- Manual yaw is a world heading for the selected component. Target changes, aim
  misses, movement, repeated placement, and reselecting that component preserve it.
  Selecting a different component releases the manual heading. Gable slope flips
  preserve heading. Attachment rules may reject a pose but cannot silently turn it.
- Initial stairs on surfaces follow the approach heading. Wall-face stairs attach
  their high end to the aimed face and extend toward the approached side.
- Actual wall end faces continue horizontally at every height. Top faces stack;
  broad-face stacking uses a 0.15 m strip near the top rather than half the wall.
- Floors follow the aim position along a wall in 0.25 m steps. Adjacent wall/gable
  pieces use the same grid phase. Side platforms use the physical face; storey
  seating retains the boundary datum and reversible fitting.
- Foundation/floor extensions align rotated occupied bounds rather than assuming
  a zero minimum. Centred L06 footings now meet both end-to-end and side-by-side.
  Off-grid exact seams use a dedicated frame; nudges remain 0.25 m.
- Stair side faces offer aligned left/right flights. Matching pitch and a shared
  elevation along a positive run overlap establish an explicit side
  connection. Different pitch or vertical separation does not establish that connection.
  A real side hit takes priority over the stair underside aim guide.
- Canopies and other furnishings snap to aimed faces but may also be placed freely.
  Rear mounting samples, full bedding and foot contact are not required. Removing
  a neighbouring wall or floor leaves each placed piece in place, following
  [free building](BUILDING_FOLIAGE.md#free-building).

## Dimensions and authoring

| Parts | Installed dimensions |
| --- | --- |
| Ordinary limestone walls/courses, openings and L17 half-gables | 0.25 m centred thickness; spans, heights and openings retained |
| L18 cornice | 0.5 m depth, including 0.125 m projection each side of the wall |
| L19 threshold steps | 2 m width, 0.5 m rise, 1 m run |
| L20 half-storey stair | 2 m width, 1.5 m rise, 2 m run |
| L21 full-storey stair | 2 m width, 3 m rise, 4 m run |
| L22 landing | 2 × 2 m footprint, 0.25 m depth; floating placement permitted |

L06 footings, piers, heavy retaining walls and buttresses keep their existing sizes.
No extra narrow stair catalog entry is added. Two standard limestone stairs now
cover a four-metre floor edge. Stair run and room interior clearance remain distinct.

`BuildKitDimensions` derives installed meshes from retained source art. It preserves
UV coordinates, transforms normals with the inverse transpose, rebuilds tangents,
and updates bounds, compound solids, walking hulls, authoring prefabs and thumbnails
together. Generated assets end in `-modular-*`. Their GUIDs are stable on rebuilding.
Small derived wall meshes retain CPU data for reversible cornice fitting.

The limestone installers apply the same conversion after reading original source
models/manifests, so rebuilding cannot silently restore old dimensions. Existing
Blender sources and imported source models remain intact. Definitions/prefab IDs
and catalog entries are retained. No scene is saved and no existing record is moved.
Restart Play mode to adopt changed dimensional definitions; existing bad placements
are not repositioned automatically.

## Preview performance

The old path resolved all session geometry, rebuilt all support contacts and often
rebuilt all rendering batches for a new cursor pose. Collider range was not the
primary scaling issue identified in that path.

Preview resolution now uses direct spatial neighbours plus a dependency ring for
floor fitting and complete junction ownership. Unchanged dependency floors use
their committed resolved states. Only directly affected pieces become overrides.

Unchanged pose and geometry revision reuse the preview candidate, neighbour list,
fitting states and visual overrides. Health-only damage retains this cache and
render batches. Per-piece collision revisions change only when resolved solids
change. Every preview and commit still checks live terrain readiness and
external/player collisions. There is no support graph or collapse solver.

Stable instanced batches survive cursor movement. Preview overrides filter only
affected batches and render a small replacement overlay. Clearing a preview
restores those batches without rebuilding the village. Movement collision ranges
and physical streaming remain unchanged.

Committed add/remove mutations now resolve two spatial neighbour rings as well.
Boundary floor states and scratch joint contacts retain full-resolution fitting
and corner ownership. Only changed visuals notify renderers; each renderer
coalesces pending IDs and rebuilds their old/new spatial groups with reusable
batch arrays. Removing a piece retires its attachments and preview overrides.
`BuildWorld` disposes its renderer subscription during teardown.

## Verification and reproduction

Run `Tools/ValidateBuildingInteractions.ps1`; add `-Install` to selectively copy the
validated dimensions and derived assets back from the isolated project. The install
checks asset identity and copies only 20 owned definitions, their prefabs, derived
meshes/solids and thumbnails. Scenes, catalog, fonts and unrelated art are excluded.

`BuildInteractionValidation` tests cursor sweeps across wall/gable seams, all eight
rotations, centred/rotated footing seams, real controller target changes and aim
misses, manual headings across the catalog, side stairs, relaxed canopy support,
cornice/landing support removal, incremental/full-resolution parity, and stable
render batches. It benchmarks changing uncached ordinary and fitted poses against
the former full geometry/support work in a synthetic 513-piece session.

The harness also runs existing modular timber (192 character stair traversals),
limestone access (160 masonry stair traversals), courtyard, extension, limestone,
timber-infill and native input suites. Evidence lives under
`.utmp/building-interactions/`; the interaction report is
`unity-project/.utmp/interaction/validation.json`.

Earlier measured runs: ordinary preview validation approximately 0.008 ms median,
0.013 ms p95; fitted preview approximately 0.11 ms median; full-session reference
approximately 1.5 ms median. These are isolated editor CPU measurements, not a
live-world FPS or jitter guarantee for the pictured village.
