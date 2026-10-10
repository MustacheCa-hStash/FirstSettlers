# Build collider debugging and clipped-stair plan

## Debug controls

Press **F8** in Play mode: Off → Solids → Solids and clearance → Off.
Press **F9** to freeze/resume the diagnostic pose. When debugging is off, F9
enables the clearance view and freezes it. Walk around or leave building mode
to inspect the captured conflict; gameplay keeps running. The next F8 press
resumes live diagnostics. These keys work with the build picker open too.

`BuildWorld` automatically installs `BuildColliderDebug`; no scene/prefab setup
is required. Its inspector exposes the mode and a default 24 m drawing range.
Debugging defaults off. Drawing does not create colliders, change placement
rules, add support, or change the canonical piece records.

| Colour | Meaning |
| --- | --- |
| Blue | Enabled, streamed physical colliders, read directly from their pooled proxies |
| Grey | Resolved logical solids whose physical proxies are currently unstreamed |
| Green / red | Proposed solids at the accepted / rejected placement pose |
| Orange | Identified blocker, affected piece, or advisory roof/stair conflict partner |
| Yellow | Prospective fitted solids, floor openings, or accepted placement with a warning |
| Purple | Stair passage/headroom reservation; this is not a physical collider |

The panel reports dimensions, origin, orientation, snap hint and failure type.
General build-piece overlap and insufficient headroom are advisory: placement remains
accepted, the HUD shows an amber warning, and the panel identifies the warning
piece. Actual solid overlap takes priority over a headroom-only warning. The
warning applies in both placement orders and clears when the conflict is gone.
Both physics colliders remain enabled, so movement can still be blocked.
The blocking piece gets its display name and session ID both in feedback and
in a floating label. External boxes, meshes and player/capsule shapes are
drawn directly; terrain and unknown collider types use broad bounds.
Lines are visible through surfaces. Mesh diagonals show the walking hull's
actual triangulation. Neither contact offsets nor the player skin are added
to these wire surfaces. Support failures without a particular blocker do not
invent an orange collision shape.

Frozen data is a snapshot: removing a piece while frozen leaves its old wires
visible until diagnostics resume. The current play session owns piece IDs;
these are not permanent asset identifiers.

## Changes made alongside debugging

The previous protected headroom prism extended 0.5 m beyond each side of a
1.25 m stair. That margin belongs to the deliberately generous **floor
opening**, not to wall clearance. Required wall/roof headroom now follows the
full 1.25 m stair width and extends 2 m vertically above its walking plane.
This allows wall-adjacent stairs. Walls crossing the route and overhead roofs
now warn rather than reject, following the requested permissive policy;
duplicate stair solids remain rejected.
Floor cuts remain wider to keep joists/framing away from the opening.

The former automatic 0.25 m starter correction is removed. Validation keeps
the selected snap and manual nudge even when a later flight would intersect
another piece. Diagnostics identify actual overlaps or clearance warnings at
the final preview pose. Current W21 rise/run and the walking hull remain
unchanged; this is not an adjustable-stair implementation.

## Clipped stairs: planned, not implemented

See [permissive building dimensions](PERMISSIVE_BUILDING_DIMENSIONS.md) for the
current eligibility policy and full-span wall/corner proposal.

W21 is a fixed 1.25 m wide, 1.5 m rise, 2 m run module. No resizable stair was
implemented in the previous floor/framing work. That work changes floors
around stairs; it does not shorten their treads or stringers. The earlier
landing-tail example was hypothetical. The approved intersection plan kept
W21's endpoints unchanged.

The requested next design is **clipping at the same slope**, not scaling.
The walking plane follows `y = yToe + 0.75 * (z - zToe)` in the stair's own
frame. A landing at height `h` above the toe therefore needs run `h / 0.75`.
For example:

| Rise | Run | Relation to W21 |
| --- | --- | --- |
| 0.75 m | 1 m | Retain half of the canonical flight |
| 1.5 m | 2 m | Full current flight |
| 3 m | 4 m | Two full flights; clipping cannot shorten this run while preserving slope |

Both endpoints on the 0.25 m local grid constrain clipping lengths to 1 m
run / 0.75 m rise increments: four run ticks for three rise ticks. Arbitrary
0.25 m run cuts produce 0.1875 m rise changes, so independently rounding
height and run would change the slope or break the landing join. A later
non-grid endpoint mode would need explicit metadata and matching attachment
rules rather than hidden rounding.

### Ownership and fitting

1. Preserve a canonical stair record, source mesh, toe pose and original
   extent. Store any player-requested retained interval explicitly; do not
   edit or progressively shorten the source.
2. For a functional short flight, fit the retained interval at placement and
   store it as the player's chosen profile. For example, selecting a 0.75 m
   landing yields a canonical 1 m run, not a temporary hidden second metre
   that grows back when the landing is removed. A supported floor/landing
   may claim the high-end interface only where
   its footprint meets the stair and its walking height equals the ramp
   height. A wall/post/roof collision never becomes a clipping instruction.
3. Derive the endpoint from the landing surface and slope. Keep full tread
   spacing, tread thickness, width and stringer thickness; remove excess
   components and cap exposed cut stringer ends with wood end-grain UVs.
   Prefer complete tread boundaries, with an explicit seated-end allowance
   for any residual partial tread. Reject unsupported/ambiguous joins.
4. Generate a matching shortened smooth ramp, occupied volume and passage
   claim. Floor openings are derived from this retained route, not the old
   full flight. Walking surface, visuals, validation and support must use
   the same endpoints. Merely hiding renderer geometry is insufficient.
5. Resolve a landing's matching seat before its passage/opening claim to
   avoid cutting away the landing that caused the fit. Ordinary floor tiles
   crossing above an ascent still open around the protected route.
6. Recompute temporary seating trims on local neighbour changes. Keep the
   chosen functional extent as the restoration envelope: new walls/posts
   cannot occupy temporarily hidden structural or passage regions. This
   makes removing a landing restore permitted decorative geometry without
   growing steps into another wall. Functional short flights remain short;
   ordinary support loss uses the support graph and existing grace period.
   Do not move a placed stair, retain an unexplained historical trim, or
   carve a wall to make a restoration pass.
7. Preview every affected representation before commit. Support and removal
   checks run against those prospective states. Endpoint sockets must use
   effective dimensions, including downward continuation and landing joins.

For an enclosed room with only 3.5 m clear run, a 3 m rise still needs a new
layout: additional bay space, a landing and turned flight, or a separately
authored steeper stair with its own movement limits. Same-slope clipping
cannot solve that dimensional conflict.

No Blender edits or clipped prefabs are required in this task. Before
implementation, retain editable individual treads/stringers in the source
and establish which ends may be cut and capped. One player-visible W21
option can select derived fitted representations; do not create a menu
entry for every cut length. The runtime resolver and proxy must be extended
together before that option is enabled.

The implementation needs a per-record/per-preview stair profile and effective
bounds, not edits to the shared `BuildDefinition`. `BuildPlacement` sockets,
`BuildSession` indexing, `BuildResolution` floor cuts, support/occupancy checks,
`BuildGameplayProxy`'s ramp, preview rendering and debug dimensions must all
read that profile. Today several of these paths read the definition's fixed
dimensions and the proxy uses its fixed authored collision mesh; adding only
a clipped render mesh would leave the same placement problem.

## Validation

`Tools/ValidateBuildingPrototype.ps1 -ColliderDebugChecks` runs blocker/shape
checks across eight rotations, external solid capture, freeze/resume,
wall-adjacent real CharacterMotor traversal and URP rendering of all modes.
`-InputChecks` exercises real F8/F9 edges and held-key guards alongside the
existing building input checks. `-StairChecks` and `-FramingChecks` retain
the continuous-flight, starter correction and framed opening checks.
All checks use an isolated Unity project, without opening the saved world.

Verified in the implementation run: 820 collider/debug checks; eight near-wall
motor walks; 96 legacy stair walks; 6,195 floor/framing checks; 3,617 base
prototype checks; and native F8/F9 input cycle/freeze/held-key checks. Render
captures are saved in `ArtReferences/ColliderDebug` for Off, Solids and Solids
and clearance. Roof checks also passed (1,058 checks and off-screen shadow
validation). These are controlled fixtures; the debug mode is intended to
expose remaining problems in real building layouts.
