# Resource design proposal

October 10, 2026. Discussion proposal; no item definitions, balance values, harvesting rules, or runtime mechanics are implemented by this document. Wood, hardwood and frostwood are accepted design inputs from the user; heartwood is a candidate. Suggested properties below are game rules, not measurements of real materials.

## Direction

Build an Iron Age-inspired settlement economy with a layer of woodland and elemental magic. Advancement should expand what players can make and where they can live. Preserve ordinary materials as the bulk of construction and manufacturing throughout the game.

Three independent dimensions define a material: its purpose (structure, cutting edge, binding, protection, fuel or magic), its processing state (gathered or worked), and its specialization (physical or magical properties). Rarity should describe access rather than universally increase every statistic.

The local ordinary-wood concept uses hewn poles, riven boards, pegs, wattle and thatch. Current biome identifiers include forest, grassland, beach, rock, snow, tundra, taiga and desert; proposed riverbank and sacred-grove resources can be placed as local habitats or sites without requiring new full biomes. Existing tree identities can inform yields without making every species a separate inventory item.

Historical inspiration: [Butser's Iron Age area guide](https://static1.squarespace.com/static/5c50c0077c93277e627b7374/t/5de52685e6b0e7013ac813d2/1575298696278/Butser-Ancient-Farm-Iron-Age-Area.pdf) describes timber frames, plank walls, willow/hazel wattle, reed thatch and daub. [The British Museum's Iron Age overview](https://www.britishmuseum.org/collection/galleries/britain-and-europe-800-bc-ad-43) describes iron replacing bronze for tools and weapons. These anchor the material vocabulary; the game can combine traditions and introduce invented resources.

## Wood family

| Resource | Proposed source | Defining role | Tradeoff | Examples |
| --- | --- | --- | --- | --- |
| Wood | Common trees, branches, fallen timber | Cheap, versatile, easy to work | Ordinary durability and no inherent magical affinity | Shelter, floors, poles, tool handles, charcoal |
| Hardwood | Selected mature trees; oak/beech are useful visual cues | Strength and wear resistance | More gathering effort and slower working | Reinforced posts, shields, axe handles, workstations |
| Frostwood | Special trees in taiga, tundra and snowy regions | Holds frost enchantments; resists weathering | Poorer impact toughness; requires specialist working | Frost staves, cold wards, preservation chests |
| Heartwood | Awakened ancient trees and sacred sites | Living affinity; supports restorative magic | Scarce, sensitive to fire; requires ritual preparation | Living bows, healing foci, ward anchors, growing structures |
| Springwood, optional | Flexible woodland trees | Elasticity, lightness | Less useful for heavy load-bearing pieces | Bow limbs, spear shafts, fishing rods |
| Bogwood, optional | Submerged or waterlogged ancient timber | Wet-environment specialization | Heavy and troublesome to extract | Marsh foundations, docks, boat fittings |

Start with the first four. Add springwood only if flexible materials produce meaningful bow/tool choices. Add bogwood only if wetlands or water construction become substantial systems. Ordinary resin-treated wood can cover water protection initially.

Hardwood is a gameplay category rather than a complete botanical classification. Do not make spruce, maple, oak and birch logs occupy separate stacks just for species realism. A tree can yield common wood plus a limited specialty yield. Assign species yields explicitly during balancing.

Real heartwood is the inner wood of a tree; it is not inherently magical. Use **Awakened heartwood** if the game's lore needs to distinguish the magical resource. Keep the short name **Heartwood** if descriptions establish that distinction clearly.

Cold affinity is not automatically thermal insulation. Frostwood preservation chests can channel cooling; protection against cold should come from an explicit ward recipe. Heartwood regeneration should require a planted anchor and energy, not make every carried object regenerate freely.

## Core material families

| Family | Resource | Function and progression value |
| --- | --- | --- |
| Mineral | Stone | Foundations, hearths, early hammerheads, station construction; useful permanently |
| Mineral | Flint | Early cutting edges, knives and arrowheads; remains an inexpensive ammunition option |
| Earth | Clay | Daub, pottery, ovens and smelting structures; links shelter to workshop progression |
| Plant | Plant fiber | Common gathering material for cordage, bindings and basic woven goods |
| Plant | Reeds | Thatch, baskets, mats; makes shore and riverbank exploration useful |
| Plant | Resin | Torches, sealing, adhesive and weather treatments; keeps ordinary wood relevant |
| Animal | Hide | Early coverings, grips, simple protection; feedstock for leather |
| Animal | Bone | Optional early needles, hooks and ritual carvings; add when those recipes exist |
| Processed | Cordage | Reusable binding material for tools, bows and construction |
| Processed | Leather | Worked hide for better equipment, bellows and straps |
| Processed | Charcoal | Specialist metallurgical fuel made from common wood |
| Metal | Iron ore | Main physical equipment progression; exposed deposits or bog deposits can share one item type |
| Processed | Iron stock | Forged bars or billets for cutting tools, weapons and reinforcement |
| Focus | Quartz | Modest mineral focus for introductory magic; shaped rather than inherently a universal power source |
| Magic | Aether crystal | Stores magical charge; used for portable foci and powered wards |
| Magic | Spirit resin | Rare binder for enchanting and living materials; supports preparation and repairs |
| Magic | Frost essence | Optional consumed frost reagent; introduce only if frostwood alone cannot express the recipe distinction |

Berries and meat fit the existing food direction. Add crops, salt, honey, herbs, wool and linen when farming, preservation, brewing, healing or textile equipment needs them. Food should have its own recipe and discovery progression instead of becoming a long list of interchangeable buff ingredients.

## Later candidates and reasons to add them

| Candidate | Reason to exist | Scope condition |
| --- | --- | --- |
| Copper + tin -> bronze | Cast fittings, ornaments, vessels and ritual equipment; alternative metalworking route | Add with enough cast/ritual recipes to justify two ores and an alloy |
| Silver | Conductive inscriptions and protective magic | Add when a distinct ward/enchantment family needs it |
| Gold | Prestige, ceremonial objects and trade | Add when settlement status or trade becomes meaningful |
| Tempered iron or steel | Better smithing and edge retention | Later workshop mastery; not a new mysterious ore |
| Slate | Roof tiles and visual construction variety | Add if mineral roofs differ mechanically or aesthetically enough |
| Limestone | Plaster, masonry or monument construction | Add alongside a coherent masonry process |
| Obsidian | Sharp but fragile weapon specialization | Add only with a believable volcanic source or explicit fantasy source |
| Emberstone | Stores heat; enables heated structures or fire equipment | Add with a fire/heat system; not simply the next damage tier |
| Sky-metal | Rare, light specialty metal | Optional late fantasy branch; avoid making it mandatory for every tool |

Do not launch with all candidates. Each resource needs recognizable sourcing, several useful recipes or one substantial unlock, and a reason it remains desirable after its introduction.

## Progression

Progression belongs to the settlement's capabilities, not a claim that players reenact the whole history of technology.

1. **Establish a camp.** Wood, stone, flint, fiber and food. Hand crafting, campfire, stone/flint tools and a simple shelter. Guaranteed reachable resources prevent dependence on a lucky seed.
2. **Build a homestead.** Reeds, clay, resin and hide. Carpentry bench, drying/tanning frame and pottery hearth or kiln. Roofing, daub, storage and weather protection make the base function better.
3. **Develop a workshop.** Hardwood, charcoal, ore and worked iron. Smelter, forge and anvil. Better gathering, durable tools, reinforcement and stronger physical equipment.
4. **Choose regional expeditions.** Cold regions provide frostwood; awakened groves provide heartwood; mineral sites provide crystals. Practical equipment and minor magic help players enter these areas. They open parallel specialties.
5. **Master combinations.** Composite equipment, specialized wards and upgraded stations. Recipes pair ordinary infrastructure with a small specialist component. Refinement and knowledge remain useful sources of advancement.

Bronze can be an optional casting branch alongside iron. Players should not have to finish a copper age and bronze age before accessing an Iron Age forge.

Small magical effects can appear during the camp/homestead stages through charms or quartz foci. Regional materials deepen magic later; magic need not wait until the endgame.

## Attributes and comparisons

Keep design attributes understandable and connect them to actual systems. Show only relevant attributes for the selected output:

- Structure and defense: strength, durability, mass, fire/weather protection.
- Weapons and tools: cutting performance, toughness, flexibility, weight, harvesting suitability.
- Magic: affinity, charge capacity, charge efficiency and explicit special effects.
- Economy: gathering availability, working effort, fuel and station requirements.

Avoid a universal material-score total. A hardwood shield can be sturdier while a heartwood shield supports a restorative ward. Iron remains the practical axe edge; frostwood improves the specialized handle or focus rather than replacing metal in every part.

Initial building differentiation can use durability, weather protection and ward behavior without requiring structural simulation. The current building support graph has no material-strength or weight decay; heavier loads and longer spans would be separate gameplay work. Do not advertise those attributes as functioning in the current prototype.

## Crafting structure

Use three ingredient roles: **main material**, **binding**, and an optional **specialist addition**. For example, a spear combines a head, shaft and binding; a magical spear adds an inscription or crystal.

Offer constrained substitutions, such as wood/hardwood for a handle or fiber/leather for an appropriate binding. Each permitted substitution has an authored effect. A recipe requiring cold affinity should identify frostwood specifically, not accept any rare wood.

Use intermediates where players reuse or store them: charcoal, cordage, leather and iron stock. Do not create separate mandatory inventory items for every peg, board, twig, shaving and nail. Early construction can spend raw wood and fiber directly. Workstations can later unlock timber kits if batching gives them a clear purpose.

### Illustrative recipes

Ingredient roles only; quantities and times require playtesting.

| Output | Ingredients | Station / action | Reason to craft |
| --- | --- | --- | --- |
| Stone axe | Stone + wood + fiber | Hand crafting | First chopping capability |
| Flint knife | Flint + wood + fiber | Hand crafting | Cutting and simple preparation |
| Wattle wall | Wood + fiber | Build placement | Cheap enclosure |
| Daubed wall upgrade | Existing wattle wall + clay + fiber | Build upgrade | Better shelter protection |
| Thatch roof | Wood + reeds + fiber | Build placement | Early rain protection |
| Charcoal | Wood | Covered charcoal pit | Bootstraps specialist fuel without requiring iron |
| Iron axe | Iron stock + hardwood + leather | Forge | Durable general-purpose chopping |
| Hardwood shield | Hardwood + leather | Carpentry bench | Physical defense specialization |
| Frost staff | Frostwood + shaped quartz + spirit resin | Attunement stone | Efficient frost channeling |
| Preservation chest | Wood + frostwood insert + charged crystal | Carpentry and attunement | Active cooling while powered |
| Heartwood bow | Heartwood + cordage + spirit resin | Carpentry and ritual preparation | Living-magic weapon specialty |
| Living ward | Heartwood anchor + stone + charged crystal | Sacred-site attunement | Maintains a bounded protective effect |

Keep the crafting and harvesting dependency graph acyclic at first access. A frostwood-working upgrade cannot require frostwood before any frostwood can be obtained. Offer initial harvest with an existing tool, fallen specimens, or a clear preparatory ritual.

## Mana and magical supply

Separate energy from physical items:

- **Mana** is replenishable energy held by a character, site or device.
- **Aether crystals** are crafted/found storage or focusing components; equipment can retain its crystal and track remaining charge.
- **Essences** are consumed reagents for enchantment, rituals and exceptional actions, not the mandatory fuel for every ordinary spell.
- **Affinity materials** determine what an item channels well. Frostwood favors frost; heartwood favors living magic.

Recharge should have a clear source and cost: time at a site, transferred player mana, or a limited station rule. A charged crystal is not automatically a second inventory item type. Its charge can belong to the individual item or installed device.

Rare wood should usually be an insert, core or anchor. A ordinary-wood home with one heartwood ward pillar makes a valuable find accessible without demanding a whole enchanted forest per building.

Renewability should fit each material: wood can regrow, plant resources can replenish, magical groves may offer bounded harvest or regrowth rituals. Ore need not regenerate if deposits, salvage and eventual trade provide a sustainable supply. Avoid routinely burning rare crafting materials just to maintain the base.

## UI and item data implications

Suggested resource categories: Timber, Stone & earth, Fibers & hides, Metals, Food & herbs, Magical materials. Processing state should be a secondary filter or label, not another enormous category tree.

Use distinct silhouettes and textures: plain log, dense block, pale frost-grained branch, knotted living core, fibrous bundle, clay lump, angular flint, rough ore and faceted crystal. A small affinity symbol supplements the icon. Preserve selected-state visibility independently of material color; avoid making every rare material share the selection accent or an undifferentiated purple rarity border.

An item detail can say **Frostwood — Timber — Cold affinity — Worked at a carpentry bench**. Equipment comparisons should describe output behavior: **better frost efficiency, lower impact durability**. A recipe explains the required role, station, available/required amounts, source and effect of permitted substitutions.

Retain separate world-object and inventory definitions, following the existing inventory proposal. Tree species is world identity; wood is a harvested item; an equipped axe is an instance with durability. Relevant future data includes stable item ID, family, processing state, recipe tags, supported properties, stack rule, drop/source rules and instance state where needed. No runtime schema change is proposed for immediate implementation here.

The proposed pack has 24 carried slots. Introduce resources gradually, pool ordinary species yields, and balance stack sizes against an expedition's actual needs. Nearby-storage crafting should expose its ingredient source explicitly when implemented.

## Recommended first content pass

Begin with wood, hardwood, stone, flint, fiber, reeds, clay, resin, hide, iron ore, quartz and the existing food choices; add cordage, leather, charcoal and iron stock as reusable products. Develop frostwood, heartwood, aether crystals and spirit resin as the first fantasy expansion. Introduce essence items only after they perform a distinct role.

Validate the loop with a shelter kit, a carpentry station, a forge chain, three contrasting tools/weapons and one powered ward. This covers common gathering, processing, specialist discovery, useful comparison and continued demand for ordinary resources without committing to dozens of speculative systems.
