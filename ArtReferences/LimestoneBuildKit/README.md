# Limestone: L01–L36 build catalog

An illustrated concept family for First Settlers, accompanying the [ordinary stone](../StoneBuildKit/README.md), [wood](../IronAgeWood/README.md) and [furniture](../IronAgeFurniture/README.md) catalogs. Limestone adds pale coursed walls, dressed openings, modest cornices, broad paving and enclosed courtyards. The architectural language stays practical: a mason's homestead and workshop with timber roofs, rather than an ornamental palace.

**L01–L17, L25, L27, L28, L31, L33 and L35 now have editable Blender models and Unity building integration.** See the [L01–L03 wall guide](../../Documentation/LIMESTONE_BUILD_KIT.md), [original source](Models/LimestoneBuildKit.blend) and [rendered walls](Models/limestone-unity-overview.png). The additional pier, paving, footing, limewashed wall, plinth and doorway have their own [L04–L09 guide](../../Documentation/LIMESTONE_L04_L09.md), [editable source](Extension/LimestoneExtension.blend) and [Unity preview](Extension/limestone-extension-unity-overview.png). Arched openings, windows and courtyard pieces are covered in the [L10–L17 guide](../../Documentation/LIMESTONE_L10_L17.md), [native source](Courtyard/LimestoneCourtyard.blend) and [Unity preview](Courtyard/courtyard-unity-overview.png). The cornice, three stair lengths, solid-bedded landing, retaining wall and buttress have an [L18�L24 guide](../../Documentation/LIMESTONE_L18_L24.md), [editable source](Access/LimestoneAccess.blend) and [Unity component sheet](Access/access-catalog.png). The six timber/infill additions have an [installed kit guide](../../Documentation/TIMBER_INFILL_KIT.md) and [native source](TimberInfill/TimberInfillKit.blend). Other numbered entries remain proposals. The dimensions below govern authoring; painted proportions, block counts and dimension arrows are illustrative.

The additional [L25–L36 timber extension](TIMBER_EXTENSION.md) develops the supplied house reference into framed daub walls, boarded accents, simple wooden trim, fitted shutters and optional shingle roofing. It includes period/context notes, two component sheets, a mixed-material assembly and a [combined material trim proposal](Textures/README.md). L01–L24 retain their original specifications below.

- [01: Core masonry](01-core-masonry.png) — L01–L06.
- [02: Wall finishes and openings](02-walls-and-openings.png) — L07–L12.
- [03: Courtyard and roofline](03-courtyard-and-roofline.png) — L13–L18.
- [04: Access and landscape](04-access-and-landscape-v2.png) — L19–L24.
- [05: Homestead assembly study](05-homestead-assembly.png) — examples using this family and existing timber construction.
- [06: Timber and infill](06-timber-and-infill.png) — L25–L30.
- [07: Gables, roofs and bearing](07-gables-roofs-and-bearing.png) — L31–L36.
- [08: Mixed-material homestead](08-mixed-material-homestead.png) — early-material and optional vernacular directions.
- [Material trim sheet and guide](Textures/README.md).
- [Exact image generation prompts](GENERATION_PROMPTS.md).
- [Extension and trim prompts](EXTENSION_PROMPTS.md).

## Material and silhouette

Use chalky ivory, warm cream and muted honey-grey limestone, with broad readable planes and shallow tool marks. Rough-squared L01 has varied rectangular blocks in level courses; dressed L02 has more regular blocks and finer joints. Their silhouettes remain interchangeable. Thin warm-beige recessed joints keep walls light at gameplay distance. Avoid black mortar grids, tiny busy rubble, polished marble, heavy damage, battlements and elaborate classical ornament.

L07 adds restrained warm off-white limewash over masonry, with occasional worn patches revealing the pale blocks. It is a finish alternative, not another structural thickness. Keep a little tonal separation between wall, coping and paving. Weathering belongs near ground contact and water paths, rather than coating every face in dark grime.

Reuse golden-brown hewn timber, pegged boards and reed/thatch roofing from the wood family. Pale stone frames make these warmer elements stand out. Square and semicircular openings provide the main silhouette change; plain chamfers and a shallow roofline course supply the limited ornament. No historical dating or structural certification is intended.

## Dimensional contract

Follow the current [modular wood](../../Documentation/MODULAR_WOOD_KIT.md) and [stone](../StoneBuildKit/README.md) contracts: metres, 0.25 m placement increments, eight yaw headings at 45° intervals, 4 m full spans, 2 m short spans and 3 m storey rise. Wall thickness is 0.50 m. A wall root is its lower span endpoint on its thickness centreline: a full wall occupies local X=0..4, Y=0..3, Z=−0.25..+0.25. Surface relief stays inside the occupied envelope at mating faces.

Walls and piers start at their base; paving, footing and landing anchors sit at their upper datum, with their full depth below it. Stairs start at the low walking datum. Local X runs across a wall or across the stair width; stair run proceeds forward. List the explicit dimensions below when authoring instead of inferring orientation from a drawing.

A nominal 4 × 4 m room has wall centrelines 4 m apart, 4.50 × 4.50 m outer dimensions and 3.50 × 3.50 m clear interior. Ground paving retains a 4 m nominal envelope with edges seated into masonry. Upper timber floors are 0.25 m deep below their walking datum: floor top and the next storey's wall base both occur at Y=3 m. Limestone paving and landings are not suspended stone floors.

## Component specification

All sizes are metres. Width × height × depth applies to wall-like pieces. Material columns describe appearance and construction intent, not costs, strength ratings or implemented resource processing.

| ID | Part | Planned dimensions | Construction and fit |
| --- | --- | --- | --- |
| L01 | Coursed limestone wall | 4.00 W × 3.00 H × 0.50 D | Rough-squared pale limestone, varied block lengths, horizontal staggered courses. Primary economical appearance. |
| L02 | Dressed block wall | 4.00 W × 3.00 H × 0.50 D | More regular blocks with shallow tooling and small edge chamfers. Same placement and support envelope as L01. |
| L03 | Narrow block wall | 2.00 W × 3.00 H × 0.50 D | Half-span L02 for closures and partitions. No extra end-post cells. |
| L04 | Square limestone pier | 0.50 × 3.00 × 0.50 | Plain stacked square masonry; no capital outside the envelope. Optional exposed-end/local bearing piece. |
| L05 | Limestone paving | 4.00 × 4.00 footprint; 0.25 deep | Broad rectangular slabs on packed bedding. Continuous solid support required. |
| L06 | Strip footing | 4.00 L × 0.50 H × 1.00 D | Broad low masonry strip centred below a wall; top anchor and level bearing face. Corners require a merged/trimmed junction. |
| L07 | Limewashed wall | 4.00 W × 3.00 H × 0.50 D | L01 masonry with thin warm off-white finish; subtle exposed patches. A material/mesh variant, not a stacked overlay. |
| L08 | Plinth course | 4.00 L × 0.50 H × 0.50 D | Two low dressed courses with a level top. Can form a low edge or raise a wall's base; does not secretly fit inside a 3 m wall. |
| L09 | Square doorway wall | 4.00 W × 3.00 H × 0.50 D | Centred 1.25 W × 2.25 H clear opening. One visibly solid limestone lintel, 1.75 L × 0.50 H × 0.50 D, with 0.25 seats; lintel Y=2.25..2.75. |
| L10 | Arched doorway wall | 4.00 W × 3.00 H × 0.50 D | Centred 1.50 m clear opening. Vertical jambs to Y=1.50; semicircle radius 0.75 reaches crown Y=2.25. Radial voussoirs form a 0.25 m ring, outer crown Y=2.50. Empty aperture. |
| L11 | Narrow window wall | 4.00 W × 3.00 H × 0.50 D | Centred 0.50 W × 1.00 H clear window, sill Y=1.25, head Y=2.25. Single 1.00 L × 0.25 H lintel seated 0.25 each side. Empty aperture; no glass. |
| L12 | Arched window wall | 4.00 W × 3.00 H × 0.50 D | Centred 1.50 m opening; sill Y=1.00, spring Y=1.75, inner crown Y=2.50. Radius 0.75; ring 0.25; outer crown Y=2.75. Empty aperture. |
| L13 | Low courtyard wall | 4.00 W × 1.25 H × 0.50 D | Neatly coursed pale stone, flat uncapped top. Takes L15 for 1.50 m total height. |
| L14 | Short courtyard wall | 2.00 W × 1.25 H × 0.50 D | Half-span boundary wall, matching L13. Optional short coping is a length variant of L15. |
| L15 | Chamfered coping | 4.00 L × 0.25 H × 0.50 D | Several fitted capstones with a shallow sloping/chamfered top. Flat continuous bearing below; no width overhang. |
| L16 | Courtyard pier | 0.75 W × 1.50 H × 0.75 D | Broader short pier with its plain bevelled cap included in the total height. Terminates a low wall or frames an empty entrance. |
| L17 | Limestone half-gable | 2.00 base × 2.00 rise × 0.50 D | Triangular masonry, rising/falling orientations form a 4 m-wide, 2 m-high gable. One design with mirrored geometry. |
| L18 | Eaves cornice | 4.00 L × 0.25 H × 0.75 D | Restrained chamfered bearing course, no carved brackets. Centred over 0.50 m wall; 0.125 projection each side. Replaces the top 0.25 m wall band through reversible mesh and collision fitting. |
| L19 | Threshold steps | 1.50 W; 0.50 rise; 1.00 run | Two 0.25 risers and two 0.50 goings, solid supported masonry below. |
| L20 | Half-storey stair | 1.50 W; 1.50 rise; 2.00 run | Eight 0.1875 risers and eight 0.25 goings. Broad pale treads over a filled stepped mass. |
| L21 | Full-storey stair | 1.50 W; 3.00 rise; 4.00 run | Sixteen matching risers/goings; convenience long-flight model, same pitch as L20. |
| L22 | Limestone landing | 1.50 × 1.50 footprint; 0.25 deep | Slab cap at top-surface datum; continuous bearing beneath the entire area. Not a free-spanning balcony. |
| L23 | Retaining wall | 4.00 W × 1.50 H × 0.75 D | Broad base and slightly battered exposed face within its envelope. Plain back plane; visual terrain-edge component. |
| L24 | Plain buttress | 1.00 W × 3.00 H × 1.00 projection | Tapered or stepped pale masonry mass with flush vertical mating back. Optional external silhouette, with no numerical strength bonus assigned. |

## Openings and roofline

L10 and L12 have distinct opening profiles: the door spring line is 1.50 m above its base and the window spring line is 1.75 m. Their semicircles have the same 0.75 m radius. Model wedge-shaped arch stones pointing toward the circle centre, with real jamb support; a horizontal row of loose blocks must not bridge the aperture. The 0.25 m ring is a game-art proportion for later modeling and traversal review.

L09's top is a single substantial lintel, not several unsupported short stones. Its proposed proportions are a visual design assumption. Detailed structural review and resource/processing design are future work. The square doorway could accept a separately fitted 1.20 W × 2.20 H wooden leaf with nominal 0.025 m clearance per edge. Arched leaves and shutters need their own fitted profiles, pivots and collision plans; they are not additional numbered L## parts and are not included in these sheets.

L17 sits on the eave datum Y=3; two mirrored halves produce ridge Y=5 and a 45° roof. Use the existing timber/thatch roof family. Roof covering and overhang must be fitted for a 0.50 m masonry end wall. The wall plate seats in the top wall band instead of adding storey height.

L18 occupies Y=2.75..3.00. The installed fitting rule clips a compatible full wall's visible mesh and collision to 2.75 m and restores both when the cornice is removed. It does not overlap the old course or raise the 3 m eave. It requires a matching supported full bay; short walls and triangular gables do not accept it. L36's timber bearing plate remains unbuilt and needs a coordinated bearing detail. L08, by contrast, genuinely adds 0.50 m when placed above the ground datum; doorway, floors, roof and steps must all follow that raised base.

## Footings, access and junctions

For a 4 × 8 m nominal shell, run 1 m-wide L06 strips along all perimeter centrelines, splitting the long sides into two 4 m lengths. Their union covers the wall footprints; perpendicular strips overlap at corners and must become one resolved junction or use trimmed end variants. Separate intersecting visible meshes are not a finished solution. For a ground datum Y=0, footing occupies −0.50..0 and paving occupies −0.25..0.

Paving occupies nominal X=0..4 and Z=0..8, with its edges under the wall's inner bearing band; the clear room is X=0.25..3.75, Z=0.25..7.75. Mixed stone/wood joints retain each material's actual thickness. Bonded or quoined limestone corner covers should remain pale stone, with suppression where an explicit pier occupies the same space. Author endpoint, corner, T and 45° treatments before asserting snap compatibility.

L20 and L21 match the wooden stair pitch: rise/run=0.75, approximately 36.9°. Top arrival tread is included in the nominal going count. L22 can turn two half flights at Y=1.50, but needs a solid masonry base reaching its underside; a 0.25 m slab alone provides no elevated support. These access pieces widen the ordinary-stone stair proposal from 1.25 to 1.50 m. Check attachment and character clearance at that width rather than silently reusing the old occupied volume.

Guards are not included in the L## stair envelope. A future timber rail can sit outside the 1.50 m clear width with its own reserved strip and support. The existing S21 guard is only a potential mixed-material accessory; its shape and fit are not established for this kit. Headroom, handrails, turns and empty aperture collision need actual character validation. Retaining walls do not imply soil pressure simulation or automatic terrain excavation.

## Assembly studies

**Mason's homestead:** a 4 × 8 m nominal, single-storey building, L01/L02 walls on L06 footing, L05 ground paving, L09 square front doorway and L11 side windows. Paired L17 gables meet the reused timber/thatch roof at 3 m eaves and 5 m ridge. A partial roof cutaway shows timber bearing and the grounded floor. Use existing wooden doors and F## furniture as future fitted accessories. L08 and L18 are omitted from this baseline to keep one unambiguous ground/eave datum. The 4.50 × 8.50 m outer shell and roof overhang are larger than the nominal centreline footprint.

**Walled court:** L13/L14 boundaries on real footing/ground support, L15 coping and two L16 piers framing a 1.50 m clear empty entrance. A 1.50 m gate opening between independent piers is a layout distance, not another 0.25 m-thick infill part. Decorative paving follows the same slab vocabulary. A separate L10 wall can mark a covered workshop entrance; it is a full-height 4 m bay, not a 1.50 m-high garden wall.

**Raised approach:** L19 reaches a 0.50 m raised entry datum. For larger changes, use L20 or L21 with solid mass below and L22 supported on a filled platform. L23 edges the terrace and L24 adds an optional buttress silhouette. Assembly illustrations show design intent rather than counted bills of materials or certified stair layouts.

## Resource and implementation proposal

The [resource proposal](../../Documentation/RESOURCE_DESIGN_PROPOSAL.md) already lists limestone as a later candidate for masonry, plaster or monuments. This catalog gives that candidate a coherent visual use. A possible later loop is quarry limestone → prepare rough/dressed blocks at a masonry station → build walls and paving; a lime-processing branch could supply joints and limewash. Station requirements, fuel, yields, inventories, mortar ingredients and unlock costs remain undecided. Limestone is not automatically a higher durability tier than every ordinary-stone piece.

Start modeling L01/L02/L03/L04/L05/L06 and L09/L11, plus L17 to close a shelter using the existing roof vocabulary. Follow with arched profiles L10/L12, courtyard pieces L13–L16 and threshold L19. Defer fitted cornice, long access and landscape parts until masonry seams, support and roof bearing have been demonstrated.

Share pale stone materials across the family, with a finish variant for L07. Broad face variation should break obvious tile repetition without changing contact planes. Openings require compound/convex geometry that leaves their real profiles clear; gables, steps and buttresses need occupied shapes beyond a full bounding box. Paving and landing support must cover the full underside. No recipe, structural balance, lime-production system or save-format work is part of this concept catalog.

## Original L01–L24 generation and review

Illustrations use the built-in image generation tool and the project's ivory-paper catalog style. The [prompt set](GENERATION_PROMPTS.md) records the exact requests. Written dimensions and fit notes take precedence over painted stone counts and small annotations. These are reference artifacts; Unity compilation and runtime tests do not apply.

All five sheets were visually inspected for component labels, the pale-stone material family, opening silhouettes and solid stair support. Sheet 04 received a targeted annotation correction so its retaining-wall inset agrees with the 0.75 m maximum depth; the reviewed v2 is selected above. Stair riser counts remain governed by the table, rather than counting painted tread edges. The homestead's cutaway, gable apex treatment, vegetation, furniture and fitted wooden leaves are illustrative accessories; they do not add numbered limestone products or override the measured shell and roof datums.
