# Permissive placement and proposed dimensional contract

The dimensional migration and shared post visuals are now implemented. See
[full-span wood kit](MODULAR_WOOD_KIT.md) for the current models, UV variation,
colliders, snapping and validation. The proposal below preserves the rationale.

## Implemented code preparation

`BuildPlacementPolicy` centralizes build-piece eligibility. Ordinary intersections
and stair headroom are advisory for every build-part pair, in either placement
order. Both pieces keep their physical shapes. Duplicate same-kind footprints,
missing support, removal of existing support, external player/object penetration,
and reach limits still reject placement. Coincident wall/infill material variants
are duplicates when their footprints match; replacement remains a separate action.

Accepted solid intersections transmit support. Headroom regions never transmit
support. The logical support graph therefore agrees with accepted physical contact
instead of requiring an exact butt-face alignment. This is a simple prototype
support rule; it is not an engineering/load simulation.
Declared wall/post stacking and authored endpoint connections also transmit
support independently of optional fitted bearing output. The legacy stack rule
bridges at most the existing 0.25 m band; it does not grant arbitrary floating support.

Stair validation retains the chosen snap and manual nudge. The previous automatic
0.25 m starter shift for a hypothetical second-flight clash is removed. An actual
second-flight collision now reports a warning and can be committed.

Existing compatible floor fitting remains available. Fitting is secondary to
eligibility: an unsupported diagonal cut or completely suppressed floor retains
its full authored mesh and full collider, with a fitting warning. No new floor
opening, roof cutting, or additional artist representation was implemented here.

`boundsOffset` can describe half-thickness offsets independently of the 0.25 m
anchor grid. `stackRiseUnits` can specify a 3 m storey independently of body height;
zero preserves current 2.75 m bodies plus a 0.25 m bearing band. Wall stacking,
floor seating and roof seating read this explicit rise. Assets retain their current
dimensions and defaults until a coherent mesh/definition/snap migration is done.

Optional `jointSockets` declare local endpoint positions and outward directions.
Resolved state records matching butt/corner/angled contacts and a primary owner
chosen by canonical pose, independent of placement order. Current assets do not
declare sockets. These contacts are preparation for future shared outputs: no
automatic corner post, collider carving, or wall cropping is generated today.

## Recommended next standard

Dimensions below are a proposal, not new dimensions applied to current models.

| Component | Proposed physical size | Anchor/interface |
| --- | --- | --- |
| Main split-plank and wattle wall | 4 m span × 3 m height × 0.25 m depth | Endpoints on bay boundary lines; no 0.25 m end reservations |
| Corner/bay post | 0.25 × 3 × 0.25 m | Centre on the wall junction |
| Timber floor | 4 × 0.25 × 4 m | Walking surface at storey level S; occupies S−0.25..S |
| Stone foundation | 4 × 0.5 × 4 m | Same walking-level convention; top at S |
| Thatch roof module | Keep 4 m width, 2 m run, 2 m rise | Bearing at S+3; overhang stays cosmetic |
| W21 stairs | Keep 1.25 m width, 1.5 m rise, 2 m run initially | Toe and high-end sockets; same-slope clipping remains a separate future feature |
| Short walls/landings | Multiples of the 0.25 m grid | Preserve the same endpoint and walking-height conventions |

The recommended wall has local span X=0..4, height Y=0..3, thickness
Z=−0.125..+0.125. Its endpoint sockets are (0,0,0) and (4,0,0).
The future profile is `sizeUnits=(16,12,1)`, `boundsOffset=(0,0,−0.125)`,
`wallEndInsetUnits=0`, `stackRiseUnits=12`. A centred post has
X/Z=−0.125..+0.125. Half-thickness coordinates belong to the shapes; player
anchors remain on the 0.25 m grid. A wall on either side of a bay then has
0.125 m thickness inward and outward, yielding about 3.75 m clear interior
between opposite walls whose boundary lines are 4 m apart.

Every story uses its own datum. A full-height wall spans S..S+3. The upper floor
walks at S+3 and extends down to S+2.75, overlapping the wall's upper band at the
perimeter. The upper wall starts at S+3. With a floor present, ceiling clearance
is approximately 2.75 m. With no floor, full-height stacked walls touch directly;
they need no automatically added 0.25 m bearing band. The foundation is not used
as the universal reference for elevated construction.

Alternative: retain 4 × 2.75 × 0.25 m wall bodies and the existing 0.25 m shared
band. This preserves the current vertical artwork and framing, but retains the
extra generation/suppression/restoration dependency. The 3 m full-height option
is preferred for the requested simpler and more permissive system.

Current main-wall art is still 3.5 × 2.75 × 0.25 m. A genuine 4 × 3 m source
model is the clean migration. Temporary render scaling could bridge the change,
but would stretch plank proportions and UV density; it is not enabled here.
Changing only the logical dimensions would leave a mismatch between visual and
physical extent. New centred-post/wall snapping must use declared sockets rather
than rounding bounding-box minima to the grid.

## Corner options

### Preferred first step: shared post cover

Allow both full-span walls to meet/overlap. Derive one corner-cover output from
their common endpoint, rather than spawning a player-selected post for each wall.
The cover shares the junction's support; it does not create a new grounded source.
An explicitly placed post takes visual precedence over the generated cover.
Straight butt joints can remain plain or use a seam cover; corners and T junctions
can receive a post. T junctions need interior wall sockets in a later extension.

Use a physical post width/depth of 0.25 m and a render width/depth around 0.26 m:
5 mm extra per face, with height kept exactly 3 m. This avoids standard coplanar
surfaces at the wall/post joint. Do not enlarge the collider or change endpoints
to achieve this effect. Test the actual irregular planks/rails; 0.27 m is an
alternative if their surfaces extend further. A larger post is a stylistic choice,
not a change to the bay grid.

Start with a visual cover where the wall colliders already close the corner. Add
a 0.25 m joint collider only if it fills a required physical gap. Keep one logical
cover per junction/storey; derive it from current neighbours and suppress it when
an authored post supplies it. Removing a wall removes/reclassifies the joint output
without shrinking or moving the other wall.

### Later optional refinement: wall-end fitting

Declare a small end region, let the joint/post own that region, and shorten the
designated wall end and matching collider there. Deterministic ownership prevents
different outcomes when walls are placed in a different order. A cut needs a closed
end and suitable end-grain UVs. It must use the future artist-part data or a known
parametric end representation, not arbitrary boolean cuts of the unreadable FBX.
The canonical full-span wall remains intact for restoration.

Buried boards/stringers do not require trimming merely because they overlap.
This refinement should follow a visible need; the shared cover is sufficient for
many ordinary corners. Cosmetic fitting never automatically opens a wall for stairs.

## Stair planning limit

A 4 m bay is not 4 m of clear walking space. At the proposed centred thickness it
has about 3.75 m clear interior. Two current W21 flights still need 4 m of run for
a 3 m rise, before providing approach/landing space. Permissive eligibility lets
the player build the arrangement, but does not make intersecting colliders passable.
Same-slope clipping cannot shorten the horizontal run while retaining the same rise.

A practical future compact layout uses two parallel flights with a turn landing:
two 1.25 m widths plus a 0.25 m middle gap total 2.75 m; a 2 m flight run plus a
1.25 m deep turn landing totals 3.25 m. That can fit within a 3.75 m interior,
subject to player-body, railing and headroom checks. A roughly 2.75 × 1.25 m
landing can bridge both flight widths. This is a planning target; no landing art,
new stair dimensions or traversal guarantees for this layout were created here.

## Verification

`-IntersectionChecks` exercises real Play mode: same-plane wall stacking beside
two stairs, advisory wall/headroom/solid overlap, retained wall/ramp colliders,
duplicate rejection, full-solid fallback and future centred 4m/3m socket profiles.
Corner ownership and story-level wall/floor/roof sockets are checked in both
placement orders across eight rotations. Existing stack rendering tests still
use unreadable imported wall art.
