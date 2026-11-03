# Sandstone: SS01–SS24 build catalog

An illustrated concept family for First Settlers, alongside [ordinary stone](../StoneBuildKit/README.md), [limestone](../LimestoneBuildKit/README.md) and [wood](../IronAgeWood/README.md). Sandstone adds warm apricot-orange masonry, cleft slabs and simple worked-stone domestic pieces. The distinguishing features are sedimentary bedding, broad slab forms, recessed storage, small splayed openings and useful court fittings.

**SS01–SS11, SS13 and SS15–SS24 are modeled and integrated into the Unity building catalog.** The [initial kit guide](../../Documentation/SANDSTONE_SS01_SS11.md), [editable source](Models/SandstoneBuildKit.blend) and [Unity overview](Models/sandstone-unity-overview.png) cover the first eleven pieces, totaling 180 runtime triangles with a linked SS11 gable flip. The [court/access guide](../../Documentation/SANDSTONE_COURT_AND_ACCESS.md), [extension source](Extension/SandstoneExtension.blend) and [Unity extension overview](Extension/extension-unity-overview.png) cover the eleven new designs: 400 authored triangles, plus 68 for short wall/coping and a coping turn. SS23 has reversible embedded-tail fitting on compatible solid masonry. Each group shares its own baked colour/AO and normal pair. SS12 and SS14 remain concepts; no sandstone recipes or resource-processing systems are added. The measured specification below governs authoring; painted proportions, stone counts, step counts and assembly callouts are illustrative.

- [01: Core masonry](01-core-masonry.png) — SS01–SS06.
- [02: Openings and interiors](02-openings-and-interiors.png) — SS07–SS12.
- [03: Court and household](03-court-and-household.png) — SS13–SS18.
- [04: Access and working stone](04-access-and-working-stone.png) — SS19–SS24.
- [05: Homestead assembly](05-homestead-assembly-v2.png) — shelter, working court and terrace studies.
- [Exact generation prompts](GENERATION_PROMPTS.md).

## Period and setting

Sandstone is reasonable for the project's [British Iron Age inspired direction](../../Documentation/RESOURCE_DESIGN_PROPOSAL.md), where suitable stone occurs locally. The Scottish Archaeological Research Framework's [Old Scatness case study](https://scarf.scot/regional/sirfa/sirfa-case-studies/old-scatness/) describes an Iron Age village and the local Old Red Sandstone sequence supplying building stone. Forestry and Land Scotland's [broch learning resource](https://forestryandland.gov.scot/living-and-working/learning-and-education/outdoor-learning/to-build-a-broch) places substantial stone and timber roundhouse traditions in the Iron Age.

Those sources support the material and general construction vocabulary. They do not establish this square, modular homestead or every SS## design as an excavated Iron Age type. The gabled house, storage wall, standardized fittings and dimensions are gameplay adaptations. Keep simple lintels and slabwork as the baseline; this family does not need a new palace, temple or medieval architectural style to justify its material.

The orange hue also has a real geological basis. The British Geological Survey's [Mauchline Sandstone record](https://webservices.bgs.ac.uk/buildingstone/buildingstones/10016?mapType=recordMap&recordId=10016&recordType=buildingStone) describes a rich orange colour caused by a small amount of oxidised iron. This is a colour reference, not evidence that that particular quarry supplied Iron Age buildings.

Sandstone need not be restricted to desert settlements. Place suitable outcrops where the world's geology makes sense, including temperate settings. The art direction uses neutral daylight so the orange comes from the stone itself.

## Palette and surface

The requested shift from Minecraft-like pale yellow sandstone is **a restrained orange/apricot bias**. Use this as a family of naturally iron-stained stone, with enough variation to remain masonry rather than a uniform orange painted wall.

| Role | sRGB colour | Use |
| --- | --- | --- |
| Main stone | `#C88750` | Warm ochre-orange midtone; dominant body colour. |
| Lighter stone | `#DBA46D` | Apricot variation across selected broad faces. |
| Fresh cut edge | `#E9BB86` | Small warm peach highlights and newly worked surfaces. |
| Rust variation | `#B46F43` | Occasional subdued iron-rich block or band. |
| Recessed joint | `#997452` | Thin warm earth joints; lighting supplies deeper occlusion. |

These are albedo/art-direction targets, not exact samples of the generated illustrations. Check future materials under neutral light beside ordinary grey stone, pale limestone, brown wood and yellow thatch. The sandstone should remain recognizably orange in shade without becoming saturated red brick. Keep the majority around the main/apricot tones; rust is an accent.

Use long bedded courses, broad granular chisel marks, subtle sediment lines and narrow softened arrises. Dressed SS02 calms the joints and tooling; it does not become polished ashlar. Cleft slab faces can reveal gentle unevenness. Avoid black mortar outlines, fine noisy speckle, identical edge wear on every block, baked sunset tint, oversized cracks, hieroglyphs or unrelated ornamental carving.

For a later atlas, reserve rough wall, dressed wall, cleft slab, cut end and coping/trim regions. Keep grain orientation meaningful on slabs and horizontal bedding coherent on walls. Provide broad UV variation without changing contact planes or increasing stone count just to hide tiling.

## Modular contract

Use the current masonry family: metres, 0.25 m placement increments, eight yaw headings in 45° steps, 4 m full spans, 2 m short spans, 3 m storey rise and 0.50 m wall thickness. Internal details such as a tread or trough rim may use smaller measurements; they do not change the placement grid.

Wall roots lie at the lower span endpoint on the thickness centreline. A full wall occupies local X=0..4, Y=0..3, Z=−0.25..+0.25. Full walls include their masonry ends; do not insert timber post cells. Relief must stay within contact envelopes. SS03 uses exactly half the span with the same block scale.

Paving and footings anchor at their upper surface with depth below it. Stairs anchor at the low walking datum; local X is width and local +Z is run. Other standalone pieces anchor at their base unless explicitly stated. SS14 anchors at the ground line, with its burial tail below it. The SS23 shelf anchors on its rear mating wall face, at the bottom of its visible envelope.

A 4 × 8 m nominal room has 4.50 × 8.50 m outside wall dimensions and 3.50 × 7.50 m clear space. Timber floors, roof bearing, doorway leaves and masonry junctions require the existing mixed-material thickness rules. No sandstone suspended floor or sandstone roof is proposed here.

The current project allows free placement and has removed structural support/collapse requirements; see the [building placement and foliage guide](../../Documentation/BUILDING_FOLIAGE.md). References below to bedding, masonry seats and embedded tails describe construction intent and required visual/geometry fit for later authoring. They do not reinstate runtime support rules or claim new simulation.

## Component specification

All sizes are metres. W/H/D means width, height and depth. Stairs list width, rise and run. Decorative tooling stays inside mating faces. Numbered parts are selectable concepts; mirrored or short-length variants do not create extra IDs.

| ID | Part | Planned size | Construction and fit |
| --- | --- | --- | --- |
| SS01 | Bedded sandstone wall | 4 W × 3 H × 0.50 D | Rough-squared elongated orange blocks; primary everyday wall. Strong horizontal bedding with staggered joints. |
| SS02 | Dressed sandstone wall | 4 W × 3 H × 0.50 D | Calmer block faces and narrower joints, restrained broad tooling. Same occupied envelope as SS01. |
| SS03 | Narrow wall | 2 W × 3 H × 0.50 D | Half-span SS02 for short closures; maintain full-size blocks. |
| SS04 | Square pier | 0.50 W × 3 H × 0.50 D | Plain stacked stone. Explicit corner/end alternative; suppress coincident corner geometry. |
| SS05 | Flagstone paving | 4 × 4 footprint; 0.25 deep | Broad cleft slabs with ground bedding as construction intent. Top datum; not a thin elevated structural floor. |
| SS06 | Strip footing | 4 L × 0.50 H × 1 D | Low bedded stone strip centred beneath a wall. Top datum. Union/trim overlapping corners. |
| SS07 | Lintel doorway | 4 W × 3 H × 0.50 D | Centred 1.25 W × 2.25 H clear aperture. Single 1.75 L × 0.50 H × 0.50 D lintel, 0.25 seating each side, Y=2.25..2.75. Empty opening. |
| SS08 | Splayed window | 4 W × 3 H × 0.50 D | Outside opening 0.50 W × 1 H, sill Y=1.25, head Y=2.25; widens to 1 W at inside face. Straight-sided reveal through full wall thickness; single lintel 1.50 L × 0.25 H above inner opening, with 0.25 seats. No glass. |
| SS09 | Storage niche wall | 4 W × 3 H × 0.50 D | Centred inside recess 1 W × 0.75 H × 0.25 deep, sill Y=1.00. Retains 0.25 solid exterior backing. One 1.50 L × 0.25 H lintel over pocket at Y=1.75..2.00. A blind recess, never a through aperture. |
| SS10 | Upright slab partition | 2 W × 1.50 H × 0.25 D | Two broad vertical slabs form a low indoor divider. Square bedded bottom, subtly cleft faces, no coping. |
| SS11 | Half-gable | 2 base × 2 rise × 0.50 D | Right triangular bedded masonry. Mirrored halves produce 4 m span and 2 m rise, with eaves Y=3 and ridge Y=5 relative to wall base. |
| SS12 | Beam-seat wall | 4 W × 3 H × 0.50 D | One centred top notch 0.50 W × 0.25 H, across full thickness: X=1.75..2.25, Y=2.75..3.00. Takes a separate 0.50-wide, 0.25-deep timber crossing the wall with top at Y=3.00. |
| SS13 | Low court wall | 4 W × 1.25 H × 0.50 D | Uncapped bedded wall. A 2 m length variant shares the ID. SS15 adds 0.25 height. |
| SS14 | Upright slab fence | 2 W × 1.25 exposed H × 0.25 D | Simple abutting cleft slabs, with another 0.25 buried below ground: total authored height 1.50. Ground-line root, actual buried geometry retained. No simulated soil resistance. |
| SS15 | Alternating coping | 4 L × 0.25 H × 0.50 D | Rough taller/shorter upright capstones, all within the 0.25 height and 0.50 width. SS13 + SS15 = 1.50 H. Provide 2 m and clean end/corner variants; no projected ledge. |
| SS16 | Court gatepost | 0.75 W × 1.50 H × 0.75 D | Plain broad post with weather-shedding cap included in envelope. Two independent posts can frame a 1.50 clear entrance. Gate/hinges separate. |
| SS17 | Slab bench | 2 L × 0.50 H × 0.50 D | One 0.125 thick seat on two 0.375 high solid stone feet, with real open space between. Separate seat/feet construction within one placement. |
| SS18 | Carved trough | 2 L × 0.50 H × 0.75 D | Hollow stone vessel. Rim/walls 0.125 thick, basin 1.75 L × 0.50 W × 0.25 deep; base 0.25 thick. No water mechanics included. |
| SS19 | Threshold steps | 1.50 W; 0.50 rise; 1 run | Two 0.25 risers and two 0.50 goings over solid masonry. Upper walking surface meets raised entry datum. |
| SS20 | Half-storey stair | 1.50 W; 1.50 rise; 2 run | Eight 0.1875 risers and eight 0.25 goings. Solid filled mass beneath flight; matches the current wood/limestone stair pitch. |
| SS21 | Battered terrace wall | 4 W × 1.50 H × 0.75 D | Vertical back; 0.75 base depth tapering to 0.50 top depth. Exposed face slopes inward. 2 m variant possible; no terrain edit or soil-pressure behavior implied. |
| SS22 | Open drain channel | 2 L × 0.25 H × 0.50 D | Open U-shaped channel, 0.25 clear width and 0.125 depth, with 0.125 base and sidewalls. Ends remain open. Grounded construction intent; no automatic grade or fluid simulation. |
| SS23 | Corbel shelf | 1 W × 0.50 H × 0.50 exposed projection | One 0.125 thick slab on two short stepped stone corbels, all inside visible envelope. Corbel tails extend another 0.25 into the host wall: total reserved depth 0.75. Built-in fitting, not a surface sticker. |
| SS24 | Hearth curb | 1.50 × 1.50 outside footprint; 0.25 H | Square ring of 0.25 wide stones around a 1 × 1 open centre. Earth/hearth bedding separate. No solid slab across centre, flame, chimney, cooking or heat-resistance mechanic implied. |

## Distinctive construction details

**Storage and light:** SS08 gives a small external opening a wider interior reveal; SS09 turns wall thickness into useful storage without puncturing the outside face. Keep their collision profiles visibly different. SS09's 0.25 backing is part of the mesh, not a dark decal masking a through-hole.

**Slab vocabulary:** SS10 is a low indoor partition; SS14 is an outdoor boundary with burial tails. They share cleft sandstone character but have different height datums and uses. SS15 uses a rough alternating cap silhouette rather than a dressed limestone chamfer. Its rhythm is deliberately compressed to stay inside the modular 0.25 m height.

**Useful worked stone:** SS17, SS18, SS22 and SS24 expand furnishing and workyard choices without becoming another architectural age. Hollow interiors and openings must remain real. SS18/SS22 hold no simulated water by virtue of their shape. SS24 is a curb component; a later hearth system would independently decide fuel, ignition and heat behavior.

**Embedded joinery:** SS12 remains a concept receiving a separate timber crossing at the existing roof/eave datum. SS23 now has reversible host-wall pockets and collision fitting for its embedded corbel tails; see the [supported wall profiles and fit rules](../../Documentation/SANDSTONE_COURT_AND_ACCESS.md). It does not cut shaped/open walls or the thin backing of SS09. Arbitrary free placement remains available, without promising fitting on every host.

SS11 takes the existing timber/thatch roof family, with proper 0.50 m end-wall fit. A timber plate should sit in a defined seat, not add unexpected storey height. Keep external masonry protected by a modest roof overhang. The drawings illustrate this connection without supplying a new timber definition.

## Junctions and authoring constraints

Author clean straight endpoints, 90° corners, T junctions and 45° contacts. Corners need resolved bonds or intentional pier substitution, not two overlapping skins. Mixed sandstone/limestone/ordinary-stone junctions retain each material's thickness and texture identity. No automatic blending system is introduced.

Preserve real openings with compound collision: SS07/SS08 need clear apertures, SS09 needs a solid back, SS17 needs space between feet, SS18 needs an empty basin, SS22 needs an open channel and SS24 needs an open centre. Triangle gables, stairs, corbels and battered walls need geometry appropriate to their occupied shapes rather than a full bounding box.

SS20 includes the top arrival tread in its nominal going count. Two flights reach a 3 m storey, but any turning landing is a separately designed fitted piece; SS05 does not imply a suspended stone landing. Rails/headroom and character clearance need later validation at actual player dimensions.

SS22 can form a visually continuous straight run on a level surface. Its carved channel does not supply a downhill gradient. A later corner, fall transition or outlet would be an explicitly authored variant; it must not silently bend or excavate terrain.

## Assembly studies

The [homestead sheet](05-homestead-assembly-v2.png) demonstrates three uses:

1. **Warm-stone homestead:** a one-storey 4 × 8 m nominal shell, rough orange walls with restrained dressed details, square lintel doorway, small splayed windows and timber/thatch roof. Ground paving, storage niche and slab partition make the interior useful. This is a square-grid game adaptation.
2. **Working court:** low court walls and alternating coping meet an upright-slab boundary; gateposts frame an empty entrance. A slab bench and trough furnish the court.
3. **Terrace and drain:** a battered wall edges a raised terrace; an open stone channel at its foot suggests surface drainage toward gravel. This is an illustrative landscape arrangement, without new terrain/water behavior.

The illustrated raised house starts at global Y=0.50, so its 3 m local eaves and 5 m local ridge sit at global Y=3.50 and 5.50. SS19 reaches its entry from global Y=0. Paving/footing top is the raised ground datum. A nominal 0.50 m strip footing is not the complete fill for that platform: earth/stone bedding below the floor must be part of the assembly. Painted cutaways are explanatory omissions, not proposed missing walls or floors.

Wooden leaves, roof rafters, textiles, plants, packed earth and loose pottery are contextual accessories, not extra sandstone IDs. A niche can face the interior even when it is revealed through an illustrative wall/roof cutaway.

## Resource and rollout proposal

Quarry suitable sandstone bedrock or collect useful slabs → prepare bedded blocks, cleft slabs or dressed fittings at a masonry work area. Sandstone is rock; loose beach sand is not directly compacted into building blocks. Block, slab and dressed states can remain processing distinctions within one sandstone resource family.

SS01–SS11 provide the core shelter, storage niche and indoor slab partition. SS13 and SS15–SS24 now add low court walls, coping, gateposts, hollow furnishings, steps, terrace edges, drainage, a fitted shelf and hearth curb. SS12's beam seat and SS14's upright slab fence remain later candidates.

Material quality varies with the deposit; this design assigns no universal sandstone strength, weathering penalty, fire rating or superior durability tier. Mortar recipes, station costs, harvesting, unlocks and balancing remain separate decisions. Thin earth-toned joints are an art direction, not evidence for a particular historical mortar recipe.

## Generation and review

Five new sheets were created with the built-in image generation tool, using the existing limestone core sheet and then the sandstone core sheet as style references. [The full prompt set](GENERATION_PROMPTS.md) records the shared direction and each sheet's exact content, plus the assembly correction. No existing artwork was replaced.

Visual review checks the orange palette, SS01–SS24 labels, component roles, opening/recess silhouettes, distinct slab pieces, solid-filled stair intent and coherent mixed-material shelter. Read the dimension table for precise thicknesses, tread counts, buried tails and corbel embedment; painted details are not manufacturing drawings. The sheets are concept art, so Unity compilation and runtime tests are not applicable.

The selected assembly v2 corrects SS10 from small-block masonry to two upright slabs and makes SS24 a complete square curb. The original remains available for provenance. SS12's illustrative dotted timber is a placement cue: the authored beam crosses the wall through its notch. SS15's alternating high/low cap profile and SS23's extra 0.25 m embedded tails follow the specification even where small painted insets simplify them.

