# Household furnishings: F01–F24 concept catalog

Five illustrated sheets for First Settlers, continuing the ivory paper, ink and gouache treatment of the [wood](../IronAgeWood/README.md) and [stone](../StoneBuildKit/README.md) catalogs. Twenty-four proposed furniture and dressing assets cover seating, sleeping, storage, tableware, textiles and small household details.

- [01: Seating, tables and bed](01-seating-tables-and-bed.png) — F01–F06
- [02: Storage and textiles](02-storage-and-textiles.png) — F07–F12
- [03: Pottery and tableware](03-pottery-and-tableware.png) — F13–F18
- [04: Hearth, craft and decoration](04-hearth-craft-and-decoration.png) — F19–F24
- [05: Furnished shelter study](05-furnished-shelter-study-v2.png) — reuse within a two-bay house
- [Exact image generation prompts](GENERATION_PROMPTS.md)
- [Household trim atlas, material proof and UV guide](Textures/README.md) — one new sheet, with existing wood/stone reuse.

These are concept references and a future modeling specification. F## identifiers are design IDs, not registered runtime content IDs. No models, recipes, prefabs, interactions or placement support are implemented by this catalog. The dimensions in this document govern future modeling; painted perspective, tiny arrows, weave counts and assembly arrangements are illustrative.

## Period and material direction

Use the existing kits' British Iron Age inspired direction, with later Iron Age domestic equipment as a reference rather than a tightly dated regional reconstruction. Simple stools, backless benches, pegged tables, a cord-deck sleeping platform and removable-lid storage offer useful game furnishing silhouettes. Their exact forms are proposals. Avalon Archaeology explicitly notes the limited evidence for furniture while discussing the woodworking skills needed to make seats, tables and beds. Its roundhouse account also supports querns and textile production as household context. See the [Avalon roundhouse reference](https://swheritage.org.uk/avalon-archaeology/our-story/iron-age-roundhouse/).

The [Butser buildings and experiments](https://www.butserancientfarm.co.uk/buildings-and-experiments) establish the same reconstructed roundhouse context used by the wood catalog. The game's square house, 4 m bays, shelf brackets, chest details, hanging baskets, wall-cloth display and tied herb display are design interpretations. They are not claims that a matching set of furniture survives from a particular site.

More direct object references inform selected small props:

- [British Museum rotary-quern upper stone, Little Woodbury](https://www.britishmuseum.org/collection/object/H_1939-1011-142): an Iron Age stone runner. F20 is a complete proposed two-stone assembly with a turning handle, not a replica of this fragment or its exact dimensions.
- [British Museum pottery spindle whorl](https://www.britishmuseum.org/collection/object/H_1976-0501-876): Iron Age ceramic textile equipment. F21 adds a proposed wooden spindle and wool grouping for scene dressing.
- [Museum Wales Iron Age bone weaving comb](https://museum.wales/collections/online/object/842bc752-820e-3774-95d7-5a2df2442061/Iron-Age-bone-weaving-comb/): supports F23's textile-tool category. F23 reconstructs a complete silhouette with a short row of teeth; it does not copy the surviving broken object's measurements or original tooth count.
- [Cambridge Museum of Archaeology and Anthropology, Glastonbury decorated pottery sherd](https://collections.maa.cam.ac.uk/objects/570523/): a reference for restrained incised ceramic decoration. F13's complete jar profile is a design proposal.

Wood matches the W## kit: broad hewn faces, riven boards, pegs, visible sockets and restrained tool marks. Wicker uses readable broad woven bands, not dense individual modeled strands at distance. Clay is matte, unglazed, buff/brown/charcoal with modest firing variation and soot on cooking vessels. Stone reuses the warm fieldstone palette. Rush matting and wool bring oatmeal, straw, muted rust and restrained blue/charcoal stripes. Bone is cream-brown and subtly incised. Ornament comes from material, weave, shallow geometric bands and everyday possessions.

Avoid modern upholstered furniture, tall backed dining chairs, cabinets with drawers, metal-bound pirate chests, glass bottles, glazed ceramic sets, candles/candelabra, heraldic tapestries, conspicuous iron hardware and fantasy runes. Do not add a chimney to the hearth design. Metal could be a separately researched later extension; this ordinary household set does not require metal fittings.

## Scale and authoring contract

Use metres and the current [modular wood standard](../../Documentation/MODULAR_WOOD_KIT.md) plus the [stone catalog](../StoneBuildKit/README.md): 4 m boundary-centreline bays and 3 m storeys. Older wooden concept sheets' 3.50 m infill / 2.75 m wall measurements are superseded for shell construction. Timber walls are 0.25 m thick; stone walls are 0.50 m thick. Thus a one-bay room is 3.75 m clear between timber inner faces or 3.50 m between stone inner faces. A 4 × 8 m nominal continuous stone shell is 3.50 × 7.50 m clear, before any internal partition.

Sizes below are full visual envelopes, including splayed feet, basket handles, cords, shelf brackets and quern handles. W is local X, D is local Z and H is local Y. For items described by length, author the long axis along local X. Diameter applies to the maximum body envelope, not only the opening. Decorative irregularity stays inside the size. Thin cloth, a ladle and a comb retain their natural size; they are not enlarged to 0.25 m structural blocks.

Proposed anchors for future authoring:

- Freestanding furniture, containers and tabletop props: base-centre, Y=0 at the receiving surface, geometry upward. F05's 0.50 m includes frame and bare cord deck; bedding is separate.
- F11 mat and F12 bedding: bottom-centre with their thin thickness above the surface. F12 fits on F05's proposed 1.80 × 0.90 m usable cord deck; frame layout must preserve that clear area.
- F06 shelf: rear-centre at the upper shelf surface; bracket geometry extends 0.25 m below the anchor, and shelf projection extends 0.25 m into the room. The complete height remains 0.25 m.
- F10 hanging basket and F24 herbs: top-centre at the suspension loop, geometry downward. F10's total 0.75 m includes its cords. F22 cloth: rear top-centre, geometry downward and up to 0.04 m into the room; the suspension rod is included.
- F19 hearth: base-centre on a solid receiving ground surface; stone ring and clay/ash fill are contained in its 0.15 m height.

The existing 0.25 m building increment and eight 45-degree yaw headings describe structural placement. A future furnishing mode should retain those coarse alignment options for large pieces while supporting surface-relative fine offsets for small props, without changing the structural grid. This is proposed behavior, not current functionality.

## Component specification

All measurements are proposed game sizes in metres. Materials express visual intent, not implemented recipes or costs. A grouped dressing asset such as F21 is one catalog entry.

| ID | Part | Full visual envelope | Construction and intended use |
| --- | --- | --- | --- |
| F01 | Three-leg stool | 0.50 W × 0.50 D × 0.50 H | Thick hewn seat, three splayed socketed legs; simple compact seating. |
| F02 | Plank bench | 1.50 W × 0.50 D × 0.50 H | Backless split-plank seat on two pegged slab supports. |
| F03 | Pegged table | 1.50 W × 0.75 D × 0.75 H | Split-board top, timber end frames and stretcher; main dining/work surface. |
| F04 | Low work table | 1.00 W × 0.75 D × 0.50 H | Broad empty working board on short legs; floor-level craft staging. |
| F05 | Sleeping platform | 2.00 L × 1.00 W × 0.50 H | Low wood frame and woven cord deck, four short feet; no headboard or bundled bedding. Reserve 1.80 × 0.90 m clear deck for F12. |
| F06 | Wall shelf | 1.00 W × 0.25 D × 0.25 H | Single board and two wooden brackets; fixed to timber or designed masonry seats, with no floating mount. |
| F07 | Pegged storage chest | 1.00 W × 0.50 D × 0.50 H | Straight-sided plank box and flat lift-off lid; no hinge, lock or metal bands. |
| F08 | Wicker hamper | 0.50 W × 0.50 D × 0.75 H | Tall woven container with removable lid and integral handles. |
| F09 | Shallow basket | 0.50 W × 0.50 D × 0.25 H | Open low gathering/storage basket; empty base version. |
| F10 | Hanging basket | 0.50 W × 0.50 D × 0.75 H | Small basket with three suspension cords and top loop; total suspended height. |
| F11 | Rush floor mat | 1.50 L × 1.00 W × 0.04 H | Flat woven rush rectangle with modest bands; thin dressing overlay. |
| F12 | Wool bedding | 1.75 L × 0.90 W × 0.12 H | Unfolded low-loft cover; muted striped edge, separate from F05. |
| F13 | Storage jar | 0.50 diameter × 0.65 H | Unglazed shouldered clay vessel, open narrow rim, flat base, shallow incised band. |
| F14 | Cooking pot | 0.30 diameter × 0.30 H | Squat open clay pot, simple rim, soot on lower body; no handles. |
| F15 | Clay bowl | 0.25 diameter × 0.12 H | Plain shallow hand-formed eating bowl. |
| F16 | Wooden platter | 0.35 diameter × 0.04 H | Carved shallow round dish, low raised lip. |
| F17 | Wooden cup | 0.10 diameter × 0.12 H | Small handleless carved cup; real hollow rim. |
| F18 | Wooden ladle | 0.35 L × 0.10 W × 0.06 H | One-piece handle and shallow carved bowl, lying down. |
| F19 | Hearth surround | 1.00 W × 1.00 D × 0.15 H | Low fieldstone ring and packed clay/ash centre; solid ground support. Cold dressing proposal. |
| F20 | Rotary quern | 0.50 W × 0.50 D × 0.40 H | Separate stone bed and domed runner; grain opening and short wood handle within envelope. |
| F21 | Spindle and wool | 0.25 W × 0.25 D × 0.35 H | Grouped wooden drop spindle, ceramic whorl and small wool bundle. |
| F22 | Woven wall cloth | 0.75 W × 1.00 H × 0.04 D | Simple wool rectangle hung from loops on a narrow rod; display use is proposed. |
| F23 | Bone weaving comb | 0.15 L × 0.05 W × 0.015 H | Narrow textile beater with short teeth at one end; restrained incised handle. |
| F24 | Dried herb bundle | 0.25 W × 0.20 D × 0.50 H | Small leafy stems tied with fibre, hanging loop included; proposed domestic dressing. |

## Furnishing a two-bay shelter

Sheet 05 places the family in a 4 × 8 m nominal stone shell with a cutaway roof. It shows a working/eating area near the entrance and a sleeping/storage area toward the back. The pictured arrangement is illustrative rather than a fitted plan or counted bill of materials. The three side vignettes are close-up uses of the same pieces, not additional products.

For an initial measurable layout, take wall centrelines X=0 and 4, Z=0 and 8. Inner stone faces are X=0.25/3.75 and Z=0.25/7.75. Put the entrance in the Z=0 wall, clear of the front aisle. These floor envelopes demonstrate a possible non-overlapping arrangement before chair-use, interaction, lid-removal and character validation:

| Piece or reserved area | X extent | Z extent | Comment |
| --- | --- | --- | --- |
| Central passage | 1.25–2.25 | 0.25–7.75 | 1 m nominal clear spine; keep dressing and hanging props out of it. |
| F19 cold hearth | 0.25–1.25 | 1.50–2.50 | Packed earth/clay below; shown cold. This is not an operating-fire clearance specification. |
| F20 quern | 0.50–1.00 | 3.25–3.75 | Ground surface; working reach needs later validation. |
| F03 table | 2.25–3.00 | 2.50–4.00 | Rotate long local X axis along room Z. |
| F02 bench | 3.25–3.75 | 2.50–4.00 | Same long-axis rotation; table-to-bench spacing is provisional. |
| F05 platform | 0.25–1.25 | 5.25–7.25 | Long axis along Z; 1 m access strip at right. |
| F07 chest | 2.75–3.75 | 6.50–7.00 | Reach from central passage; allow lift-off-lid handling above. |
| F08 hamper | 3.00–3.50 | 5.50–6.00 | Separate from chest and passage. |

F06 can mount above the table or another prepared wall surface; resolve its actual anchor, protrusion and reach before export. Small tableware and textile tools sit on a receiving surface. F12 stays on the bed deck, wall cloth stays behind furniture, and suspended items require a real beam or peg connection. Not every F## piece needs to appear in every room. A one-bay starter can use a bed, stool, small basket, bowl and wool cover, adding the large table/storage zone in the second bay.

## Future implementation and modeling order

Start with F01/F02/F03/F05/F07 and one wicker mesh, then F13/F15/F17 for a furnished room with distinct large and small silhouettes. Add F11/F12/F22 textile color, then the smaller craft and hearth dressing. Reuse materials across the catalog; F21's wool can share the bedding's fiber treatment without making it a rigid striped object.

Keep visible cavities in baskets, cups, bowls and pots. Use broad wicker/cord geometry or suitable cutout/normal treatment by viewing distance rather than thousands of separate strands. Reserve opaque simple silhouettes for distant LODs. Share modest geometric patterns through textures; do not sculpt every thread. Exact triangle budgets, UV packing and export grouping belong to the modeling pass.

Furniture needs separate placement rules from walls: it must not join structural wall sockets, carry upper storeys, require wall-style corner covers or be silently treated as load-bearing construction. Shelves, wall cloth and hanging props need explicit attachment ownership and removal behavior. Tabletop props and bedding need parent/receiving-surface persistence. Furniture collisions must leave usable space under tables where appropriate, while tiny decorative pieces and cloth should not create player-blocking boxes. Validate true occupied shapes and support, not only visual bounding boxes.

Chest storage, sitting, sleeping, spinning, grinding, cooking and animated lid/runner interactions are separate future systems. F19 is a cold visual surround; operating fire would require its own effects, support/clearance rules and gameplay design. No smoke/ventilation, heat simulation, storage capacity, crafting recipe or stat is promised here.

## Generation and review

Generated using the built-in image_gen tool, with [the complete prompt set](GENERATION_PROMPTS.md) retained alongside final PNGs. All four component sheets were visually reviewed for labels, material consistency, recognizable silhouettes and the absence of modern fittings. The shelter study received a targeted correction to its weaving tool and quern support; the reviewed v2 is the final selection, with the initial image retained for provenance. The written table remains authoritative for dimensions and authoring anchors, including total hanging heights and full handle envelopes; the cutaway is illustrative and its small quern silhouette is simplified. Use F20's individual sheet for the separate bed/runner construction. Runtime tests do not apply to these reference-only additions.
