# Ordinary stone: S01–S24 build catalog

The remaining ordinary-stone designs now have an [implementation guide](../../Documentation/STONE_COMPLETION_KIT.md)
and [independent editable source](Completion/StoneCompletionKit.blend). This includes static door/gate leaves;
doorway and window openings are fixed, with no open/close interactions. S13/S14/S22 reuse the installed roof,
floor and handrail. The original concept dimensions and historical implementation notes below are retained.

Concept catalog for First Settlers, matching the W## wooden component sheets. The catalog originated as illustrated design sheets and a dimension specification. **S02, S03, S04, S06, S07 and S12 now have Blender models, complete varied UVs, FBX exports and Unity building integration**; see the [initial implementation guide](../../Documentation/STONE_BUILD_KIT.md) and [S07/S12 boundary wall guide](../../Documentation/STONE_BOUNDARY_WALLS.md). S12's wooden gate is deferred; its opening is empty. Other S## entries remain proposals. The saved world scene has not been changed by this work.

- [01: Core masonry](01-core-masonry.png) — S01–S06
- [02: Walls and openings](02-walls-and-openings.png) — S07–S12
- [03: Timber integration and shelter study](03-timber-and-assembly-v2.png) — S13–S16
- [04: Stone stairs and landings](04-stairs-and-landings.png) — S17–S20
- [05: Guards and landscape supports](05-guards-and-supports-v2.png) — S21–S24
- [Exact generation prompts](GENERATION_PROMPTS.md)
- [Painted stone trim-sheet proposal](Textures/README.md)
- [Implemented S02/S03/S04/S06 model preview](Models/stone-kit-overview.png)
- [S07 low wall and S12 empty gate preview](Boundary/S07-S12-overview.png)

## Visual direction

Fieldstone and roughly dressed rubble with broad, readable stone faces, irregular horizontal courses, small packing stones and muted earth-filled joints. Warm grey, charcoal and occasional buff stones; weathering is restrained. S01 is irregular rubble and S02 is more carefully coursed rubble within the same ordinary-stone resource tier. Their silhouettes and stone pattern differ without adding a new progression tier. Avoid polished ashlar, ornamental castle masonry, battlements, glass and conspicuous modern hardware.

Timber is recommended for short opening lintels, fitted doors/gates/shutters, wall plates, upper floors and the roof. These remain ordinary wood, matching W## hewn members and split boards. A stone building does not need a stone roof or a suspended stone floor. Stone gables and exterior stairs provide the heavier silhouette; lighter timber upper structures prevent every component becoming a massive block.

Historic Environment Scotland's [Arnol Blackhouse material guide](https://www.historicenvironment.scot/media/6311/blackhouse-paper-model.pdf) and [roof repair account](https://blog.historicenvironment.scot/2016/10/new-thatch-arnol-blackhouse/) provide visual references for stone walls, earth mortar, timber roof structure and thatch. These later vernacular references inform the material combination, not an Iron Age dating claim. The square bays, dimensions and multi-storey options below are game design adaptations.

## Dimensional contract

The current [modular wood standard](../../Documentation/MODULAR_WOOD_KIT.md), [opening kit](../../Documentation/OPENING_WALL_KIT.md) and [stair extensions](../../Documentation/W22_W24_STAIRS.md) supersede the earlier wooden concept sheets' 3.50 × 2.75 m panels.

- World units are treated as metres. Placement increment: 0.25 m. Eight yaw headings at 45° intervals; inclined geometry has its own authored slope.
- Full wall span: 4.00 m; short span: 2.00 m; storey rise: 3.00 m. Stone wall thickness: 0.50 m, twice the timber wall thickness.
- Wall length is measured along its boundary line; no extra end-post cells are added. A wall root is its lower span endpoint on the thickness centreline. A full wall occupies X=0..4, Y=0..3, Z=−0.25..+0.25 in its local wall frame.
- Structural walls stack by exactly 3 m. A 0.25 m floor depth is below its walking datum, bearing in the upper 0.25 m wall band; it does not add another 0.25 m to the storey rise.
- A nominal 4 × 4 m shelter has perimeter wall centrelines 4 m apart: outer footprint is 4.50 × 4.50 m and clear interior is 3.50 × 3.50 m. Floor/footing parts keep their own 4 × 4 m footprints. The footing needs appropriate coverage beyond the nominal perimeter; see assembly notes below.
- Floors, landings and footings use top-surface anchors and extend downward by their stated depth. Walls/posts start at their base. Stairs start at their low walking datum.
- Doors/shutters/gates are fitted elements; leaf dimensions need not be quarter-metre multiples. Clear aperture dimensions are measured inside jambs and below lintels.
- Tables govern future modeling. Perspective drawings, visible stone counts and tiny annotations are illustrative.

## Component specification

Dimensions are metres. Wall sizes are width × height × thickness. Stairs use width, rise and run. Resource column expresses design intent, not implemented recipes or quantities.

| ID | Part | Planned size | Material and construction intent |
| --- | --- | --- | --- |
| S01 | Rubble wall | 4.00 W × 3.00 H × 0.50 D | Stone/earth joints; irregular courses, packed core, large visible faces. Primary wall. |
| S02 | Coursed rubble wall | 4.00 W × 3.00 H × 0.50 D | Stone; flatter selected faces, staggered joints. Visual alternative to S01, same tier/envelope. |
| S03 | Stone pier | 0.50 × 3.00 × 0.50 | Stone; square rubble pier for exposed ends and local bearing. Optional explicit piece, not an automatic requirement at every wall end. |
| S04 | Flagstone ground floor | 4.00 × 4.00; 0.25 deep | Stone slabs on packed bedding. Requires continuous ground/solid support; not a suspended upper-storey slab. |
| S05 | Rubble footing | 4.00 × 4.00; 0.50 deep | Stone; rough low foundation mass. Tiled/offset footing rows provide coverage for perimeter walls. |
| S06 | Stone wall cap | 4.00 L × 0.25 H × 0.50 D | Stone; large rough capstones on exposed low walls. Discontinuous individual stones, not a monolithic 4 m lintel. |
| S07 | Low rubble wall | 4.00 W × 1.25 H × 0.50 D | Stone; yard enclosure/edge. S06 optionally adds 0.25 m, making total height 1.50 m. |
| S08 | Narrow rubble wall | 2.00 W × 3.00 H × 0.50 D | Stone; half-bay closure/partition, full storey. |
| S09 | Timber-lintel doorway | 4.00 W × 3.00 H × 0.50 D | Stone + wood; centred 1.25 W × 2.25 H clear aperture, with rough timber lintel seated into masonry. |
| S10 | Pegged plank door | 1.20 W × 2.20 H × 0.08 D | Wood; 0.025 m nominal clearance on each side/top/bottom inside S09. Fitted pivot hardware requires later detailing. |
| S11 | Shutter opening wall | 4.00 W × 3.00 H × 0.50 D | Stone + wood; centred 1.00 W × 0.75 H clear opening, sill 1.25 m above base. Timber lintel, stone sill, fitted wooden shutter; no glass. |
| S12 | Yard gate bay | 4.00 W × 1.25 H × 0.50 D | Implemented as two low masonry wings around a centred 1.25 m clear opening, with no gate or wooden components. A future fitted gate may use the original proposed 1.20 W × 1.15 H × 0.08 D leaf, bottom 0.05 m above base. |
| S13 | Timber-and-thatch roof half | 2.00 run × 4.00 ridge length; 2.00 rise | Wood + thatch; reuse W roof vocabulary. 45° structural slope, 2.83 m slope line. Two halves cover a 4 m bay. Cover thickness/overhang are separate visual extents. |
| S14 | Timber upper floor | 4.00 × 4.00; 0.25 deep | Wood; reuse full framed W04 floor and its stairwell variant. Beam ends bear into wall-top band. Proposed stone-aware fitting keeps the interior clear and supported. |
| S15 | Stone half-gable | 2.00 base × 2.00 rise × 0.50 D | Stone; rising/falling triangular halves form 4 m-wide, 2 m-high gable. One catalog option with mirrored orientation, like current W15. |
| S16 | Timber wall plate | 4.00 L × 0.25 H × 0.50 D | Wood; bearing member seated in upper 0.25 m of the wall, flush at wall-top datum. Optional 2 m length uses same section. Does not add height above 3 m. |
| S17 | Stone threshold steps | 1.25 W; 0.50 rise; 1.00 run | Stone; two 0.25 m risers and two 0.50 m goings on solid packing. Low entry access. |
| S18 | Stone half-storey stair | 1.25 W; 1.50 rise; 2.00 run | Stone; eight 0.1875 m risers and eight 0.25 m goings, supported by filled stepped masonry. |
| S19 | Stone full-storey stair | 1.25 W; 3.00 rise; 4.00 run | Stone; sixteen matching risers/goings, solid filled base. Separate long-flight convenience, not a new stair pitch. |
| S20 | Stone landing | 1.25 × 1.25; 0.25 deep | Stone; cap slabs over solid bearing, at top-surface datum. Elevated examples require a pier/platform beneath the full landing. |
| S21 | Stepped stone stair guard | 2.00 run; 1.50 rise; 0.50 thick | Stone; rises with S18, top edges at least 1.00 m above each corresponding tread. Reserved envelope 0.50 W × 2.50 H × 2.00 run; separate side strip outside the stair walking width. |
| S22 | Timber stair handrail | 2.00 run; 1.50 rise; 1.00 grip height | Wood; reuse W24. Reserved side strip 0.25 W × 2.50 H × 2.00 run; optional lighter alternative to S21. |
| S23 | Retaining wall | 4.00 W × 1.50 H × 0.75 D | Stone; broad base, slightly battered exposed face within the envelope. Terrain edge component, with drainage as visual/context intent. |
| S24 | Rubble buttress | 1.00 W × 3.00 H × 1.00 projection | Stone; tapering mass within rectangular envelope, flush vertical rear mating face. Optional external support silhouette; not required on every small shelter. |

## Assemblies and mixed-material fit

**Starter shelter:** S01 or S02 walls, S09 doorway, S10 door, optional S11 shutter wall, S04 ground paving, S05 footings, S16 wall plates, two S13 roof halves, and paired S15 half-gables at each gabled end. This is an assembly illustration rather than a counted bill of materials. Gate, stairs, buttresses and upper floors are extensions.

Use a 4 m centreline bay with wall tops at Y=3 m. The wall plates occupy Y=2.75..3.00 rather than sitting on top. Structural roof eaves are Y=3 m and ridge Y=5 m; thatch/member thickness can extend above those lines. Pair the triangular gables on the same eave datum. Roof covering/end seams need a later fit pass for the 0.50 m stone thickness, as existing wooden gables are 0.25 m thick.

Centred wall profiles mean a single nominal 4 × 4 m footing tile does not fully cover all four outer wall faces. The concept illustration may show a continuous 4.50 × 4.50 m base, assembled from overlapping/offset S05 coverage; it must not be labeled as one S05 tile. Future foundation fitting should resolve merged/trimmed coverage and avoid visible overlaps. S04 paving can fit the 3.50 × 3.50 m clear interior, with its nominal 4 m envelope extending into the wall's seating area until fitted.

Timber floors at Y=3 occupy Y=2.75..3.00. A second wall storey starts at Y=3, reaching Y=6. S14's 4 m nominal floor fits against masonry and its ends need bearing seats. Its old wood-only fit behavior is a reference, not proof of stone compatibility. A stairwell opening remains mandatory for upper access; an uncut slab must not visually seal the stairs.

Timber lintels in S09/S11 have a proposed 0.25 m height and at least 0.25 m seat at each side, within the 0.50 m wall thickness. S09 lintel underside is Y=2.25 and top Y=2.50; S11 lintel underside is Y=2.00 and top Y=2.25. These are game-art proportions for later authoring review. Do not depict a row of loose stones floating unsupported across an opening.

Doors and shutters reuse wood construction language; S10 has a new fitted width because the old W10 concept was sized for a 1 m opening. S12's two low wall wings leave a real opening and its gate is a separately fitted element. Future costs should charge wood for wooden fitted parts and stone for masonry; no recipe numbers or strength values are decided here.

## Access geometry

- S18: 8 × 0.1875 = 1.50 m rise; 8 × 0.25 = 2.00 m run.
- S19: 16 × 0.1875 = 3.00 m rise; 16 × 0.25 = 4.00 m run. Both use about 36.9° overall inclination, matching the wooden stairs.
- Top arrival tread is included in the nominal going count. Draw solid packed/masonry support beneath steps, not thin stone treads spanning freely between timber stringers.
- S20 may turn two half flights at Y=1.50 m; it needs continuous solid bearing to that level. S03 can be stacked/arranged as landing bearing in a later fitting plan, or a future solid platform can supply it. No unsupported quarter-metre flagstone platform is implied.
- Guard widths are outside the stair's 1.25 m walking width. Two S21 guards require 2.25 m total width; two S22 reserved strips require 1.75 m. A one-stone/one-timber arrangement reserves 2.00 m. Stairwell, guard, turning and headroom clearance need later character validation.
- S21 is a stepped-top solid side wall: its high point is Y=2.50 m above flight base, and each step segment retains 1.00 m guard height above that tread. Two guard segments accompany a full flight.

## Future implementation notes

Stable S01–S24 IDs describe catalog designs; they are not runtime content IDs yet. S02 is a visual wall variant. S13, S14 and S22 refer to existing W assets where feasible, rather than requesting duplicate underlying models. S16 is a new proposed timber bearing section, and S10 needs a fitted leaf. Exact export/material organization is a later task.

Stone junctions should show interlocking masonry or a restrained quoin cover, rather than automatically gaining the wood kit's timber posts. Explicit S03 piers should suppress duplicate masonry cover where appropriate. Intersections between 0.50 m stone and 0.25 m wood need designed endpoint, corner, T and 45° seams; no runtime behavior is claimed here.

Openings need collision that preserves the clear aperture. Gables, stairs, stepped guards and tapered buttresses need suitable occupied shapes; bounding boxes alone can occupy empty space. Fitted moving leaves require pivots, interaction, collision and attachment behavior. Flagstones/landings require continuous bearing checks. Retaining walls are visual terrain-edge designs; they do not imply terrain cutting or soil simulation.

Prioritize S01, S05, S09/S10 and the reused roof for the first future modeling pass, then S04/S11/S15/S16 for a coherent shelter. Add low walls/gate and S17 threshold steps; defer upper-storey floors, long stairs, guards, retaining walls and buttresses until the basic seams and support rules are settled.

## Generation and review

Generated with the built-in image_gen tool in the original ivory paper, ink and gouache catalog style. Prompts are saved in [GENERATION_PROMPTS.md](GENERATION_PROMPTS.md). The dimension table is authoritative; illustrations communicate appearance and construction. The shelter's cutaway includes an optional timber loft to show floor bearing. Sheet 03's roof-half silhouette and sheet 05's guard-height labels received targeted corrections; their v2 images are the final selections. Initial drawings are retained alongside them for provenance. Painted tread/stone counts and proportions are illustrative; use the table's eight/sixteen risers and specified landing depth for later modeling. No runtime tests apply to these reference-only additions.
