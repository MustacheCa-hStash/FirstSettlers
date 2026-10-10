# Proposed intersection and mesh-ownership plan

See [implemented full-span wood kit](MODULAR_WOOD_KIT.md) for the latest models,
shared post covers and UV variation.

Current placement policy and proposed dimensional redesign are described in
[permissive building dimensions](PERMISSIVE_BUILDING_DIMENSIONS.md). Ordinary
build-piece intersections/headroom are now advisory. The historical ownership
rules below remain useful fitting targets, rather than current rejection rules.

Approved design, now implemented. See [floor/framing implementation](FLOOR_FRAMING.md)
for the current models, runtime rules and validation. The sections below preserve
the design rationale and identify future functional pieces outside this change.
See [collider debugging and the clipped-stair follow-up](BUILD_COLLIDER_DEBUG.md)
for the current headroom-width correction and the requested same-slope clipping
design. Clipped stairs are planned, not implemented.

## Dimensional contract

Keep the 0.25 m placement grid, 4 m bays and 3 m regular storey spacing. Use S
for the walking surface of the particular level, wherever that is in the world.

| Completed option | Current nominal dimensions, Unity X × Y × Z |
| --- | --- |
| Split-plank wall | 3.5 × 2.75 × 0.25 m |
| Wattle wall | 3.5 × 2.75 × 0.25 m |
| Two-plank infill | 0.5 × 2.75 × 0.25 m |
| Bay post | 0.25 × 2.75 × 0.25 m |
| Timber floor | 4 × 0.25 × 4 m; top at its origin |
| Stone foundation | 4 × 0.5 × 4 m; top at its origin |
| W21 stair | 1.25 × 1.5 × 2 m |
| Thatch half-roof | 4 m width, 2 m rise, 2 m run; 45° |

The wall's 3.5 m panel plus two 0.25 m post/end reservations completes a 4 m
bay. Keep those reservations; do not replace the bay grid with 3.5 m tiles.

For regular storeys: wall/post base S, top S+2.75; deck/rim band S+2.75..S+3;
next walking surface S+3. The next wall or terminal roof starts at S+3. Two
W21 flights reach that next walking surface, with 4 m total horizontal run.

An absent floor must not leave a 0.25 m unsupported gap. A shared rim assembly
fills the band beneath upper walls/posts or an eave when there is no floor.
When a floor supplies the same framing, suppress the duplicate rim output.
This is shared auxiliary geometry, not an extra player-selected wall variant.

Proposed ground finish: a floor may replace the foundation's upper 0.25 m at
the SAME walking elevation S. The foundation retains its lower 0.25 m. The
finish band is the only permitted subtraction from a foundation. This avoids
raising an already established wall level when a ground floor is added. The
current foundation-to-floor snap instead puts the floor above the foundation;
adopting the proposed convention will require a later placement change.

Every floor/roof/stair rule uses its own level and connections, not global
ground height. Only grounding/support ultimately traces to terrain/footings.

## Ownership rules

There is no single priority ordering for whole pieces. Declare ownership for
specific interface regions, and reject intersections outside those regions.

| Pair/interface | Owner | What may yield |
| --- | --- | --- |
| Wall/post at a designed bay corner | Post occupies reserved corner | No wall cut is needed with existing dimensions |
| Wall/post through the middle of a panel | Full bodies retained until a declared joint exists | Allow with warning; future fitting can supply a framed junction |
| Wall or post versus stairs | Both physical bodies retained | Allow overlap/low clearance with warning; no automatic wall carving |
| Roof versus stairs (current permissive policy) | Both keep their full physical shapes | Allow solid overlap and low headroom with an advisory warning in either placement order; movement may still be obstructed |
| Wall/post versus floor at the same height | Wall/post | Floor edge/planks/joists fit to the interior face or notch around a post |
| Foundation versus flush floor finish | Floor finish, within upper 0.25 m cap only | Foundation cap; restore it when the finish is removed |
| Foundation versus stairs below its top | Foundation body retained | Allow with warning; walkable access still needs an opening |
| Stair passage through an upper floor | Stairwell void | Floor deck and obstructing joists; provide framed edges around the opening |
| Stair high end seated on landing | Landing surface and fixed stair endpoint | Optional decorative stair tail within a declared seating pocket only |
| Floor versus shared rim/bearing output | Floor's own framing | Suppress duplicate auxiliary rim where the floor supplies it |
| Roof eave versus floor in deck/rim band | Floor framing | Optional lower roof bearing; never the thatch or rafters |
| Roof versus roof uphill | Continuing roof's shared interface | Upper roof's cosmetic eave; already handled by joined visual |
| Roof versus roof sideways | Exact 4 m butt boundary | No extra mesh state; no gable overhang at module boundaries |
| Opposing roofs at ridge | Matching ridge socket | Keep the existing closed geometry unless a visible problem requires a change |
| Ordinary full wall versus roof slope | Both structural bodies retained | Allow with warning; a shaped gable remains an optional visual refinement |
| Adjacent floors/foundations | Exact matching boundary | No artist-created variant necessary |
| Coincident duplicate walls/floors/roofs | Neither | Reject duplicates; do not silently delete one |
| Split-plank/wattle/infill replacing each other | Deliberate replacement action | No automatic hiding of the other wall |

Walls retain their exterior and interior appearance. A stair cannot trigger an
automatic doorway, shave a bay post, or remove exposed wall rails. Mid-panel
T/cross junctions are likewise explicit architectural choices. Bay-boundary T
junctions can use existing post reservations and panel ends.

The two stair/floor rules refer to DIFFERENT regions: the stairwell passage
cuts the floor, while a landing may conceal a decorative stair end. This is not
a circular whole-piece priority relationship.

## Shared rim geometry

Use two reusable artist-authored parts, with the same ordinary wood material:

| Part | Dimensions | Local bounds |
| --- | --- | --- |
| Rim span | 3.5 × 0.25 × 0.25 m | X=0..3.5, Y=-0.25..0, Z=0..0.25 |
| Corner bearing cap | 0.25 × 0.25 × 0.25 m | X/Z=0..0.25, Y=-0.25..0 |

The span sits between reserved corner columns. Caps finish those columns in
the same horizontal band, so four full-length beams do not double-fill the
corners. Four spans and four caps define the perimeter of a 4 m square bay.
Floor framing can reuse this geometry. A roof eave uses only the relevant
bearing line. Upper walls/posts demand the same shared layer, so stacking
without a floor remains structurally connected.

The assembly must have real occupied geometry and a valid bearing below; it
cannot create a grounded support source by itself. Partial floor coverage
suppresses only covered rim intervals, not an entire four-metre bearing.
Reserved stairwell voids prevent auxiliary beams being regenerated across the
passage. Missing bearing/support remains a structural problem, not a mesh fix.

Roof geometry uses a different packed atlas from ordinary timber. Reusing a
rim mesh in a joined roof will require the corresponding wood-region UV remap
or a separately batched auxiliary mesh. Keep one material per final joined mesh.

## Stairwell dimensions and movement

W21 remains 1.25 m wide, rises 1.5 m and runs 2 m. One flight's high endpoint
is (Y=1.5, Z=2) relative to its toe. A normal landing starts at Z=2 with walking
surface Y=1.5 and thickness occupying Y=1.25..1.5. This is already an exact
interface. The existing stair has no 10 cm tail beyond Z=2; the earlier tail
example was hypothetical. A shorter stair mesh is not mandatory.

For a full three-metre ascent, the two endpoints are (Y=1.5,Z=2) and
(Y=3,Z=4). The current player stands 1.8 m tall with a 0.4 m controller radius
and 0.08 m skin width. Plan roughly 2 m standing clearance along the path.

Use a STARTER clear opening around 2.25 m wide × 3.5 m along the run for that
full ascent. The width leaves 0.5 m each side of a 1.25 m stair. Length includes
headroom before the upper flight, not only a hole immediately above the last
tread. This is a modelling target, not a verified gameplay minimum.

For a simple edge-opening example: floor bounds X/Z=0..4, Y=-0.25..0;
opening X=0.75..3.0, Z=0.5..4.0. The two-flight stair centre aligns with that
opening; its high endpoint meets the open floor edge/next landing at Z=4.
Do not put an extra header or floor lip across that exit before Z=4.

An interior-opening representation can use a hole with the same clear size
surrounded by framing. Its offset must suit the stair endpoint and landing:
do not force a four-metre flight into a shorter hole or a closed four-metre
room. A stair may start in an adjoining bay; cuts can affect more than one
floor tile. Multiple stairs produce the union of valid opening claims.

Before implementation settles these templates, test the swept character body
at stair edges, landing transitions, rotations and the chosen opening offsets.
Half-storey landings need their own clearance evaluation; a 1.5 m-high ceiling
cannot be treated as a normal standing room.

Actual walking geometry remains solid. Low clearance and intersections with
walls, posts or roofs now follow the permissive policy: allow placement with
an advisory warning, retaining both physical colliders. A walkable route through
a solid obstruction still needs an explicit opening or a different layout.
The current bounding box is not proof that the visible stair occupies every
point within it; improve occupancy separately from render trimming.

## Blender work for completed options

| Existing option | Additional representation needed? | Artist work |
| --- | --- | --- |
| Timber floor | Yes, highest priority | Author full framed floor and edge stairwell floor; interior stairwell can follow. Keep deck, joists, rim and opening trimmers separable in source. |
| Thatch roof | Additional bearing state, using shared parts | Reuse existing full/joined roof bodies. Supply the rim span/cap kit; do not remodel the eight-vertex thatch. |
| Stone foundation | Full/finish-covered cap states | Current primitive can be generated in code. If authoring stone art, separate lower body Y=-0.5..-0.25 from the upper cap Y=-0.25..0. |
| W21 stair | No mandatory additional mesh yet | Keep rise, run, treads and stringers. A seated decorative end is conditional on later measured overlap, not required by the current endpoint. |
| Split-plank wall | No | Keep existing geometry; no stair cutout variant. |
| Wattle wall | No | Keep existing frame/weave and UVs. |
| Two-plank infill | No | Keep as a deliberate gap-filling component. |
| Bay post | No | Keep the full structural post. Shared caps bridge floor/rim bands, rather than shortening the post. |

The smallest immediate authoring list is: full framed floor, edge stairwell
floor, rim span, corner bearing cap. The full floor replaces the current
placeholder; the edge stairwell is an additional representation of the same
floor option. Existing whole-mesh roof join handling can stay as it is.

Do not model every combination of post notch, wall-edge inset and stair-hole
offset. Expose predictable deck/beam regions in the source so a later resolver
can assemble/cut only those regions and cap exposed ends. Begin with quarter-
metre edge cuts and axis-aligned openings in the building frame. Arbitrary
diagonal intersections are rejected in the first version; a whole building
frame rotated 45 degrees still uses the same local interfaces.

Explicit gables, knee walls, doors and smaller landings are future functional
build pieces. They are not automatically produced by carving a completed wall.

## Reversible update contract

Keep canonical piece identity, pose and dimensions independent of its resolved
appearance. Recompute from canonical geometry when neighbours change; never
progressively trim a previously trimmed result. Resolve passage voids and
protected structures before optional trim/bearing geometry. Same-priority
duplicate solid claims are rejected.

Placement order must not change the resolved result. Removing one stair only
removes its opening claim; other stairs can keep the opening. Removing a floor
restores foundation cap/rim geometry where appropriate, while structural
support is recomputed separately. Collision/occupancy must match the resolved
solid shapes whenever an opening or cap actually changes physical space.

Use a local dependency update for render/shape changes and a support update for
the affected structure. Preview the resolved result of all affected pieces
before accepting placement. Avoid silently moving existing stairs or letting
restored geometry block their passage.
