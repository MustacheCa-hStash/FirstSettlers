# W22 full-storey stairs, W24 handrails and stair attachments

The build menu now has twelve options. W21 remains the half-storey stair; W22 and
W24 are appended without changing existing component IDs or enum values.

| Part | Unity dimensions / datums | Blender dimensions |
| --- | --- | --- |
| W22 | 1.25 m width, 3 m rise, 4 m run; ascends +Z | X=1.25, Y=4, Z=3 |
| W24 | 2 m run, 1.5 m rise, grip 1 m above walking slope | X=0.125 actual timber width, Y=2, Z=2.5 |

W24 reserves a 0.25 m-wide side envelope, but its timber/colliders are 0.125 m wide.
Its grip runs from Y=1 at Z=0 to Y=2.5 at Z=2. The posts snap flush outside the
stair's X=0..1.25 walking width. One rail fits either side of W21; two consecutive
rails fit each side of W22. Aim at the lower or upper 2 m segment of W22 to select
that segment. A side face or the viewer's side chooses left/right. Ordinary turn
controls remain available; an unmatched rotated rail needs a valid stair-side
attachment before it can build. There is one handrail menu option.

W22 has sixteen treads and retains the established 0.75 rise/run slope. Its single
continuous six-vertex/eight-triangle walking wedge has no flat shelf at the
intermediate 1.5 m join. The visual model has 240 triangles; W24 has 36 triangles.
Handrail physics follows its two posts and sloped grip with three convex solids,
leaving the space between them open. Only the stair ramp gets SmoothWalkSurface.

## Underside placement

- Aim at a floor/foundation underside: the stair's **high end** connects to the
  nearest perimeter edge at the top walking elevation. The flight extends outward
  and downward, without a quarter-metre drop below the floor surface.
- Aim at a roof underside: the stair's high end meets the aimed slope, with snapped
  X/Z coordinates projected back onto the actual underside plane. Stair logic runs
  before generic roof placement, so this no longer proposes an ordinary piece
  beneath a nominal roof box.
- Aim beneath an existing stair: the new flight's high end meets the fixed flight's
  low toe. It continues downward with the same slope; W21/W22 can be mixed.
  A side hit from beneath the walking slope also counts as underside aim. If the
  camera ray starts inside the solid walking wedge, an aiming-only slope guide
  recovers the intended stair target. Intervening obstacles keep their priority;
  the physical walking collider is unchanged. Top-face aim continues upward.
- Aim at the stair top/end: continue upward from its high end as before.
- Top-surface floor placement retains its existing footprint fitting.

Exact sockets determine the initial pose. In particular, a centred 1.25 m flight
may have an origin between quarter-metre ticks; rounding that origin would shift
its attachment. Manual nudges still move by 0.25 m in the active frame. A nudge or
rotation which breaks the attachment is rejected unless another valid
endpoint supplies support.

## Terrain and support policy

A stair needs an **endpoint attached to a supported building**. Ground
never grounds a stair. Incidental body/side overlap with another build piece does
not replace that endpoint connection. Declared floor-edge landing sockets remain
available when optional floor/headroom fitting suppresses nearby floor output.

Once the attachment is valid, the stair's body and either endpoint may intersect
a TerrainCollider or an explicitly marked WorldGroundSurface, including complete
burial. P6 permits terrain overlap for all build types. Rock/prop/player obstruction checks remain active, and the
terrain's gameplay collision remains solid. Visible portions of a descending
flight can therefore meet the ground naturally while its lower end is buried.
The renderer keeps the full authored mesh; terrain depth testing hides the buried
portion rather than substituting a clipped prefab.

Examples:

- A W22 attached at an upper floor's 3 m edge may descend to Y=0 even if
  terrain crosses its lower portion at Y=1. This is valid.
- Clicking that terrain directly to start W21/W22 is invalid, even near a building;
  stair aiming no longer substitutes a nearby build target for a terrain hit.
- Extending downward from the buried toe of that flight is valid when the normal
  endpoint connection and other placement checks pass.
- A completely buried new flight is valid with a supported endpoint connection.

Endpoint tests use structural socket/landing geometry. The former terrain-height
and overhead-ray burial tests were removed: a retained cave ceiling is not solid
terrain at the stair, and terrain visibility is not a requirement for attachment.
See [P6 building integration](TERRAIN_SCULPTING_BUILDING_INTEGRATION.md).
Removing a real attachment disconnects the dependent stair/handrail chain under
the existing unsupported-piece grace period. Handrails attach to stairs; they do
not act as independent foundations or grant support to unrelated build types.

This does not introduce resizable/clipped stair models, new stairwell carving or
automatic roof openings. Existing compatible floor fitting and advisory overlap/
headroom warnings remain. Actual intersecting solid colliders can still prevent
movement even when placement is allowed.

## Blender source and exports

Source:
`C:/Users/samee/OneDrive/Desktop/BlenderModelsFirstSettlers/Building/BuildComponents.blend`.
Backup: `BuildComponents.before-w22-w24-2026-10-10.blend` beside it.

Open **Stair extension kit — editable**: W22 at Blender X=0/Y=40 and W24 at X=4/Y=40.
The stair has twenty editable tread/stringer components; the rail has three.
Joined exports are hidden in **Stair extension kit — exports (hidden)**. The script
`Tools/BuildStairExtensionKit.py` replaces only its own collections and preserves
the other 271 source mesh objects. All new pieces use the existing wood atlas
layout with distinct component crops and properly oriented grain, including
diagonal members and end faces.

FBXs are in `Assets/Models/Buildings/Wood/Stairs`. Both imports bake axis conversion,
preserve authored normals and disable runtime visual-mesh readability/material
import. Runtime definitions/prefabs/collision meshes are in `Assets/Resources/Building`.
Visual definitions reference FBX meshes directly; physical hulls remain separate.
Rebuild through **Tools → Building → Rebuild W22 W24 Kit** or the full kit command.

## Verification

The installed-asset suite passed **32,024 checks**, without rebuilding the main
project's linked assets first. Tests include both stair lengths across eight
headings, underside floor/roof snaps, mixed downward chains, true TerrainCollider
and marked streamed-ground embedding, a deeply buried mesh-terrain endpoint,
buried-toe/direct-ground/fully-buried rejection, exposed high-end continuation,
ordinary external blockers, left/right rail segments and open rail collision.

**192 actual CharacterMotor stair traversals** passed: W21/W22, one/two flights,
up/down, three width positions, four headings and 60/120 Hz steps. W22 paths include
rails on both sides, with no stalled entry/exit or airborne incline frames. The
existing sixteen doorway crossings, wall/gable/UV/pool regressions also passed.
Native controls and staged menu selection passed separately with all twelve
options installed. Front/back rendered views were inspected and are saved in
`ArtReferences/StairExtensionKit` with model and validation reports.

Restart Play mode to load the new catalog and attachment behavior. The user's
saved scene was not opened or rewritten.

Underside aiming correction: **32,376 installed-asset checks** passed, including
64 real controller camera rays from beside/beneath and inside the stair wedge,
all W21/W22 source/selected combinations at eight headings. Separate camera checks
preserve upward top-face continuation and prevent aiming through a blocker. The
previous synthetic hit-normal checks did not cover the actual side-face/inside-
collider aiming failure; this regression now runs through BuildingController.
