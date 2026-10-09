# Ladders, stairs and landings

Companion drawn concept sheets for the ordinary-wood kit:

- [04: Ladders and low access](04-ladders-and-low-access.png)
- [05: Stairs and landings](05-stairs-and-landings.png)

Eight proposals extend the earlier W01-W16 set. They use the same axe-hewn poles, split boards, wooden pegs, modest fibre lashings and ivory gouache/ink presentation. The existing [wood trim atlas](Textures/README.md) supplies their surfaces. No meshes, prefab definitions, game scripts, movement code or colliders were created or modified.

## Design basis

The access pieces target the existing 4 m bay and 0.25 m placement grid. A floor-bearing storey rises 3 m: 2.75 m wall plus 0.25 m slab. Ladder grip extensions are above that walking surface. Internal rung/tread dimensions are mesh detail and need not match placement ticks.

These are gameplay adaptations in the earlier British Iron Age inspired direction, rather than replicas of excavated British domestic access structures. Timber stairs are consistent with a pre-Roman material vocabulary: the [Natural History Museum Vienna's Hallstatt staircase reference](https://www.nhm.at/hallstatt/en/salt_mine/bronze_age/transport_paths) documents timber stair access in Bronze Age mining, earlier than the intended Iron Age setting. That reference establishes an early timber construction precedent; it does not establish the illustrated dimensions or a specific roundhouse staircase. The [Butser reconstruction references](https://www.butserancientfarm.co.uk/buildings-and-experiments) remain the broader building context.

## Component specification

Dimensions are metres. Rise means change in walking elevation, run means horizontal travel to that elevation, and member length is different from either. Width is measured across the access path. Drawing captions and the following specification govern modeling; painted perspectives, assembly insets and minor scale-marker text are illustrative.

| ID | Part | Access dimensions | Planned envelope and purpose |
| --- | --- | --- | --- |
| W17 | Upright rung ladder | 0.75 wide; reaches 3.00 m | 0.75 W x 3.50 H x 0.25 D. Two poles and 12 rungs at elevations 0.25 through 3.00 m. Rail tips continue 0.50 m above the floor. Compact vertical loft access. |
| W18 | Leaning loft ladder | 0.75 wide; 3.00 rise; 1.00 run | 0.75 W x 3.50 H x 1.25 D. Approximately 71.6 degrees to horizontal, 0.50 m grip extension above floor. Preferred everyday loft ladder; rung positions follow 0.25 m vertical pitch. |
| W19 | Notched log ladder | 0.50 wide; 3.00 rise; 1.00 run | 0.50 W x 3.25 H x 1.25 D. Carved recessed footholds in one broad timber, about 0.25 m vertical pitch, with 0.25 m top grip extension. Optional utility variant, rather than another resource tier. |
| W20 | Threshold steps | 1.00 wide; 0.50 rise; 1.00 run | 1.00 W x 0.50 H x 1.00 D. Two 0.25 m risers and two 0.50 m goings. Low access to raised entrances/platforms. |
| W21 | Half-storey stair | 1.25 overall wide; 1.50 rise; 2.00 run | 1.25 W x 1.50 H x 2.00 D. Eight 0.1875 m risers and nominal 0.25 m goings, about 36.9-degree pitch. Two pegged stringers carry split-plank treads. |
| W22 | Full-storey stair | 1.25 overall wide; 3.00 rise; 4.00 run | 1.25 W x 3.00 H x 4.00 D. Sixteen matching risers/goings. One straight 3 m flight; optional long prefab that can reuse two W21 modules. |
| W23 | Small landing | 1.25 x 1.25 walking surface | 0.25 m total depth below its top-surface anchor, like the current floor convention. Use at 1.50 m elevation between two W21 flights for a turn. Requires support. |
| W24 | Stair handrail | Follows W21's 2.00 run / 1.50 rise | 2.00 L x 2.50 overall H x 0.25 side depth. Grip line is nominally 1 m above the tread reference line, so its high end reaches 2.50 m above the flight base. Plain sloping grip on three vertical posts. |

Envelopes reserve space for attachments, not decorative overhangs. In a flight's local coordinates, width can be X, height Y and run Z; W24 is a narrow side strip, not a 2 m-wide wall. W17-W22 envelope dimensions fall on whole 0.25 m ticks. A leaning member's actual rail length and grip extension must be modeled from the rise/run lines, rather than confusing 3 m rise with 3 m timber length.

## Stair geometry

- W21: 8 x 0.1875 = 1.50 m rise; 8 x 0.25 = 2.00 m run.
- W22: 16 x 0.1875 = 3.00 m rise; 16 x 0.25 = 4.00 m run.
- Both have rise/run = 0.75, yielding about 36.9 degrees. They are shallower than a 45-degree roof frame.
- A top tread at the arrival level is included in these nominal eight/sixteen goings. Decide the top-seat joint with the upper floor before detailing stringer ends.
- Tread thickness can start near 0.08 m, contained below each specified tread top. Use actual geometry for treads, stringers, pegs and notch shapes; alpha is unnecessary for these solid timber pieces.
- Overall 1.25 m stair width includes two stringers; target around 1 m clear tread width. Handrails need attachment clearance. If their reserved side strips sit outside the flight envelope, allow another 0.25 m per guarded side in stairwell planning.
- Two W21 modules can form a straight W22-equivalent flight. A turn adds W23 between flights: landing top at 1.50 m; second flight arrives at 3.00 m. Landings and upper floors need support and an opening with adequate headroom.

The upright ladder and log ladder are alternatives; a small starter kit does not need every variant. Prioritize W18, W20, W21 and W23. Add W24 for exposed access, W17 for vertical climbing, W19 for a different silhouette, and W22 as an optional long authoring convenience.

## Blender and trim reuse

Use ordinary solid geometry and one material slot per exported component for the current renderer. Poles map to the bark strip or hewn grain; riven treads/landing boards to A/B; stringers and rails to C; peg sides to H; exposed timber ends to E/F. Small optional lashings use G. Align UV grain with each member's length. Retain an editable Blender copy before joining export meshes. Keep roots and bounds planned independently from visual detail.

Ladder rungs are roundwood with subtly flattened contact surfaces, through-jointed into rails and secured by wooden pegs. Stair treads seat on/in timber stringers with pegged joints. W19 steps are actual carved recesses in one log, not a collection of floating boards. W24 stays a simple grip rail with plain posts; avoid ornate balusters, modern hardware or carved medieval finials.

## Runtime work still needed

W21 now has an implemented build option, independent walking hull, stair placement/support rules and ramp-specific downhill adhesion. See [the W21 implementation and rebuild guide](../../Documentation/W21_STAIR.md). The remaining access concepts below are still proposals.

The current BuildPartKind enum contains Wall, Floor, Foundation and Corner, and the checked CharacterMotor has no ladder-climbing state. These drawings add no runtime features.

Ladders need attachment/entry/exit rules and a climbing movement state. Stairs need suitable physical collision and logical occupied-volume/support rules; the existing solid box proxy cannot represent an open inclined stair. A simple ramp-like walking collider is a useful candidate to test while the renderer shows individual treads, with appropriate side/underside handling. The existing logical box overlap checks also need design, not only a different physical collider.

Upper access needs floor-opening pieces or another supported way to reserve a stairwell. The existing 4 x 4 m floor slab does not gain a hole from a painted or visually cut mesh. Check character headroom, lower/upper joins, handrail side clearance and turn landings during implementation. The low W20 steps likewise need a working physical proxy and movement check. The 45-degree rotation control is yaw, not stair pitch; an inclined part's pitch is authored or handled by future placement rules.

## Generation and review

Created with the built-in image_gen tool, using the core component sheet as a style reference. [Exact generation and correction prompts](ACCESS_GENERATION_PROMPTS.md) are saved alongside these drawings. The sheets were visually reviewed and the ladder-height captions received targeted corrections. The geometry equations above are the modeling specification; illustrated counts and perspective are not a manufacturing drawing. No runtime tests were appropriate for drawing-only additions.

