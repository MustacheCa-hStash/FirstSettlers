# Full-span wood kit: Blender source and Unity integration

Implemented standard: 4 m wall spans, 3 m storeys and centred 0.25 m wall/post
colliders. The build menu now has seven options; the two-plank infill is archived
outside the menu. Existing component IDs/GUIDs and the ordinary wood atlas remain.

## Blender

Source: `C:/Users/samee/OneDrive/Desktop/BlenderModelsFirstSettlers/Building/BuildComponents.blend`.
Backup: `BuildComponents.before-modular-standard-2026-10-10.blend` in the same folder.

Open **Modular kit — editable**. The new pieces are on the row at Blender Y=22:
split-plank wall X=0, wattle X=6, post X=11, full floor X=15, stairwell floor X=21.
They are editable individual components; joined exports and UV-only layouts are
hidden in **Modular kit — exports and UV variants**, at the export origin.
Legacy objects keep their geometry, UVs, transforms and material assignments.

| Model | Geometry | Unity local origin/extent |
| --- | --- | --- |
| Split-plank wall | 944 triangles, 16 boards and 3 rails; original detail retained | X=0..4, Y=0..3, Z=−0.125..+0.125 |
| Wattle wall | 32 triangles; original woven-sheet UVs retained | Same full-span envelope |
| Post visual | 12 triangles | X/Z=−0.13..+0.13, Y=0..3.003 |
| Full framed floor | 324 triangles, 27 components | X/Z=0..4, Y=−0.25..0 |
| Edge stairwell floor | 480 triangles, 40 components | Same outer extent; existing 2.25 × 3.5 m opening |

The post collider is **0.25 × 3 × 0.25 m**. Its visual extends 5 mm per face in X/Z
and has 3 mm top-cap relief. This prevents coplanar side and horizontal cap faces
against wall tops/floors. Storey height and collision remain exactly 3 m.

New Blender materials use the atlas through the active UV layer directly, with
roughness 0.95 and low specular response. Wattle alpha is connected explicitly;
Unity retains its existing double-sided cutout shader/material.

## Texture variation

Each timber component uses a deterministic crop inside the same padded ordinary
wood strip. Longitudinal grain follows the board/beam axis. Crop position, extent,
mirroring and narrow transverse sampling vary by component. End faces use the
two end-grain blocks with physical projection, including newly exposed cut caps.
No wood UVs are moved into the woven alpha region or across trim-row borders.

All 14 floorboard top UV rectangles are distinct. Three full-floor and three
matching stairwell UV layouts add stable variation between tiles, without adding
player-selected prefab options or changing collision. Three post UV layouts are
also chosen from canonical position/orientation. Removing/readding neighbours
does not randomize the piece's texture pattern.

The floor blueprint stores `uvSeed`. Runtime cut pieces preserve the original
board's UV reference bounds and seed, so cutting crops its existing grain rather
than stretching a full texture strip onto each fragment. Blender and Unity share
the same mapping algorithm in `Tools/BuildingTrimUV.py` and `BuildTrimUV.cs`.

## Junction rendering

Walls declare endpoint and interior sockets at 0.25 m intervals on their boundary
line. Matching sockets identify butt joints, corners, T junctions and crossings.
There is one derived post visual per junction/storey, owned by canonical pose,
independent of placement order. Covers use the ordinary wood material even when
their owning wall uses the wattle material. Parallel overlapping interiors do not
generate a post at every interval.

An explicit centred post supplying the required height suppresses the generated
cover. Removing that post restores the cover; removing walls removes or
reclassifies the junction. Covers are instanced render attachments, with no new
player-selected record, health source, GameObject or collider. Original wall meshes
remain unchanged and may stay unreadable at runtime. Wall-end boolean trimming
is unnecessary for this first implementation.

Preview evaluates both the incoming attachments and changes to existing pieces.
The renderer cache compares attachment meshes/materials/transforms as well as the
core mesh, so suppression/restoration updates even when a wall's core stays the same.

## Snapping and colliders

Full-span walls/posts/floors snap to boundary lines and declared endpoints rather
than half-thickness bounds. Rotated end sockets retain exact poses even when the
origin lies between ticks in the parent's frame. Manual nudges still move 0.25 m
in the active grid. Main wall stacking, roof bearing and floor walking levels use
the 3 m storey datum. Roof and W21 geometry/UVs remain as before.

Build-piece overlap and stair headroom remain advisory; full wall and ramp
colliders remain solid. Duplicate pieces, missing support, external obstruction
and reach checks remain active. Existing compatible floor fitting is retained;
additional stair clipping/opening features were not introduced here.

Collision pools now deactivate immediately on disposal before Unity's deferred
Destroy, preventing one retired pool from colliding during the rest of the frame.

Restart Play mode after adopting these assets: this is a session prototype and
existing active-session poses from the old dimensions are not migrated in place.
The saved scene was not opened or rewritten.

## Authoring and validation

`Tools/BuildModularWoodKit.py` authors and exports the source models. Re-running it
replaces only its own collections. `Tools/BuildingTrimUV.py` defines atlas-safe
projection. Unity's **Tools → Building → Rebuild Full-Span Modular Kit** links the
models, variants, colliders and seven-option catalog while preserving asset GUIDs.

Run `Tools/ValidateBuildingPrototype.ps1 -ModularChecks`: asset/face UV and normal
inspection, then actual Play-mode snapping, junction lifecycle, collider and motor
checks. Full-span geometry supersedes the legacy 3.5 m dimensional fixtures;
geometry flags now route to this suite. `-InputChecks` remains the native input
suite. Original validation source is retained for historical reference.

Verified: **21,445 checks**, including 14 unique floorboard mappings in each layout,
all authored triangle UVs/normals, eight rotations, both construction orders,
butt/corner/T/cross covers, explicit-post overrides, exact rotated endpoints,
three-storey placement, retained colliders and **96 real CharacterMotor walks**.
Blender inspection also checked all triangles of 90 editable components, with no
collapsed UVs. Rendered front/back/top/underside and Unity case views are saved in
`ArtReferences/ModularWoodKit`.
