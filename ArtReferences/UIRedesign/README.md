# First Settlers UI design study

October 10, 2026. Design exploration; no runtime UI, scenes, input bindings, or gameplay systems were modified.

## Recommended direction

**Art-direction update:** the user approved this catalog's information design but requested artwork closer to the actual game. See [the revised painted-panel, timber-tab and sand-panel concepts](ART_DIRECTION_V2.md). That revision supersedes the original visual treatment below; the interaction and system analysis remains applicable.

Use **Woodland** as the shared visual language: charcoal green surfaces, cream text, amber selection, restrained corners, and small rendered item thumbnails. Combine Minecraft's quick-slot clarity, Valheim's compact building workflow, Satisfactory's browsable component catalog, and Grounded's resource handling conveniences. Use the field-journal treatment for discovery and reference pages rather than making every gameplay panel look like a book.

The professional game examples below are references for specific interactions. The proposed adaptations are design judgments, not claims that First Settlers already implements those games' features. Historical screenshots demonstrate patterns, and may differ from current versions.

## What exists today

The current source and catalog take precedence over older screenshots and notes. The current catalog contains **ten choices**. The two-plank infill still exists as an asset but is outside the active catalog; the opposite half-gable slope is a linked variant, not an eleventh choice.

| Component | Width × height × depth | Presentation notes |
| --- | --- | --- |
| Stone foundation | 4 × 0.5 × 4 m | Starting support anchor |
| Split-plank wall | 4 × 3 × 0.25 m | Full-span timber wall |
| Timber floor | 4 × 0.25 × 4 m | Framing and stairwell geometry can be derived |
| Bay post | 0.25 × 3 × 0.25 m | Explicit support; junction cover visuals can be automatic |
| Wattle wall | 4 × 3 × 0.25 m | Two-sided woven preview |
| W21 stair | 1.25 × 1.5 × 2 m | Half-storey flight |
| Thatch roof | 4 × 2 × 2 m | Structural width/rise/run; cosmetic bounds differ |
| W09 doorway wall | 4 × 3 × 0.25 m | 1.25 × 2.25 m clear opening; no moving door leaf |
| W11 shutter wall | 4 × 3 × 0.25 m | 1 × 0.75 m aperture; no moving shutter hardware |
| W15 half-gable | 2 × 2 × 0.25 m | Right triangle; R flips rising/falling slopes |

Implemented: UI Toolkit component picker and passive building HUD; contextual snap/placement messages; quarter-metre nudging; eight 45° headings; rotation, height adjustment, slope flipping, removal, support reconnection grace; object identification through PlayerQuery and datacards; a charcoal-green spruce query panel.

Proposed extensions: player inventory, nine-slot hotbar, equipment/durability display, vitals, crafting recipes and quantities, harvesting actions, build favorites, search, nearby-storage crafting, blueprints, undo and controller navigation. These need gameplay/data support. Identification metadata and capability flags alone do not authorize an action prompt. Building currently has no resource costs or disk persistence.

Evidence: `Assets/Resources/Building/PrototypeCatalog.asset`, the referenced definition assets, `BuildMenuView.cs`, `BuildingController.cs`, `LocalPlayerInput.cs`, and `Documentation/QueryHUD.md`, `MODULAR_WOOD_KIT.md`, `OPENING_WALL_KIT.md`, `THATCH_ROOF.md`, `GameplayLayers.md`. The current house sample is `ArtReferences/OpeningWallKit/opening-kit-house.png`, an actual controlled Unity render, not a saved-world screenshot.

## Professional game reference map

| Game and source | Pattern to study | Adaptation for First Settlers |
| --- | --- | --- |
| [Minecraft — official controls](https://www.minecraft.net/article/minecraft-controls) | Nine numbered quick slots, wheel selection, inventory overflow | Nine consistent item slots; number, count, and durability have separate positions. Keep the same slot ordering inside the pack. Use First Settlers' own materials and typography. |
| [Valheim — illustrated build-menu reference](https://www.4netplayers.com/en/blog/valheim/valheim-house-building-buildingsystem/) | Compact tabbed building palette, visible tool context and placement hints | A small building palette and short contextual support messages. Avoid a permanent full list of every control. This linked screenshot is a third-party visual reference. |
| [Grounded — official inventory/HotPouch update](https://grounded.obsidian.net/news/grounded/tease-1-1) | Expanded quick-access sets and backpack organization | Begin with one readable hotbar; add saved loadouts only when players actually need them. |
| [Grounded — official nearby-storage crafting](https://grounded.obsidian.net/news/grounded/february-0-7-0-update-is-released) | Ingredient access from nearby storage | Later, label ingredient sources explicitly: Backpack / Nearby storage. Show available and required counts beside each other. |
| [Satisfactory — build-menu visual reference](https://questions.satisfactorygame.com/post/66bcd125902bf447ed77d3d6) | Category rail, thumbnail grid, selected-piece preview and description | A three-column component catalog. Put dimensions, variant controls, and attachment guidance in the detail column. This is a community screenshot on the game's Q&A site. |
| [No Man's Sky — Frontiers](https://www.nomanssky.com/frontiers-update/) | Grid-based catalog and contextual placement/editing tools | Clear groups for Walls, Floors, Posts, Stairs and Roofs; show shape/variant choices next to the selected part. Editing and duplication would be later gameplay extensions. |
| [Conan Exiles — official building overhaul](https://www.conanexiles.com/blog/a-helping-hammer-building-overhaul-in-age-of-sorcery/) | Construction mode accesses categorized pieces directly | Building favorites should reference definitions rather than fill the player's backpack with fabricated component items. Current B/Tab controls can already express this separation. |
| [Enshrouded — official game overview](https://enshrouded.com/en-US) | Creative building with a large variety of materials and shapes | Group future pieces by function, then material. Preserve the current modular geometry and snapping rather than implying a voxel terrain editor. |
| [Subnautica — official Seabases preview](https://unknownworlds.com/en/news/coming-next-seabases) | A dedicated builder tool and connected modular habitat pieces | Teach assembly with a simple house example: foundation → walls/openings → floors/stairs → gables/roof. Use small contextual guidance as players aim. |
| [Factorio — developer blueprint-library redesign](https://www.factorio.com/blog/post/fff-356) | Consistent library and item representations | Later assembly blueprints should have one identity across library, preview and favorites, with labels and thumbnails. Persistence is a prerequisite. |
| [LEGO Fortnite — official introduction](https://www.fortnite.com/news/welcome-to-lego-fortnite) | Progression from shelter and campfire to crafting stations and unlocked builds | A later guided first-shelter plan could introduce actual kit pieces and prerequisites gradually. Do not expose unimplemented stations today. |
| [Anno 1800 — developer blueprint discussion](https://www.anno-union.com/x-marks-the-spot/) | Planning a build before construction | A future plan mode could visualize a timber cabin and its total resources. Treat plans as distinct from committed world records. |
| [The Last of Us Part II — official accessibility details](https://blog.playstation.com/2020/06/09/the-last-of-us-part-ii-accessibility-features-detailed/) | HUD scaling, contrast and configurable assistance | UI scale, high-contrast selections, readable focus outlines, and status icons plus words. Never use green/red alone to explain placement validity. |

Screenshots useful for a reference board: [No Man's Sky category grid](https://www.nomanssky.com/media/legacy/uploads/2021/09/base-building-grid-menu-bg-1440w.jpg), [Satisfactory category/detail layout](https://i.imgur.com/DtJYLDi.jpeg), [Valheim hammer palette](https://www.4netplayers.com/images/blog/valheim-haus-bauen-bausystem/1.webp), and [Grounded inventory screenshot/source](https://www.shacknews.com/article/128290/can-you-upgrade-backpack-size). Study those images' layouts; inventory-capacity claims in old articles are not design requirements.

## Screens and common components

**Exploration.** The world dominates. A reticle, current target's name/description, and proposed hotbar are enough. The existing query card clears on a miss or obstruction. A later action prompt appears only when the interaction resolver supplies a real available action; don't infer "Chop" merely from "Wood source." Vitals appear once corresponding player systems exist. Avoid a mandatory minimap, currencies or progress trackers.

**Build catalog.** Replace the narrow text list with rendered thumbnails, functional categories and a selected-part detail panel. Keep all ten choices easy to discover. Display dimensions in one consistent W × H × D order; roof dimensions explicitly describe structural width/rise/run. Half-gable flipping is a single selected-piece control, not a duplicate entry. Stairwell fitting, texture crops, junction posts and roof continuation meshes remain automatic derived behavior rather than extra catalog clutter. Search becomes useful as the kit grows; a ten-item catalog does not need it to function.

**Placement.** Show the selected piece, inferred attachment action and placement reason near the preview. A compact lower panel holds heading, nudge step and vertical offset. Distinguish three states: accepted, accepted with warning, rejected. Read the current message/warning from BuildPreview; never synthesize a new structural rule in the UI. The detached-state example uses the existing three-second reconnection grace, but the sample is a static state illustration, not a running support simulation. Preserve permissive intersections where current placement policy permits them. Do not replace that policy with a blanket collision rejection.

**Inventory.** The sample uses a 6 × 4 backpack and nine quick slots as a density experiment, not final gameplay capacity. Counts sit bottom-right, bindings top-left, durability on a separate thin track. Tooltips can expose details without permanent stat panels. Shift-transfer, split stacks, equip, consume and drop need implemented commands. A future storage view should show backpack and container side by side, retain quick slots, and label the destination. Favor explicit selection and buttons for controller support; drag/drop can supplement them.

**Crafting.** A recipe list and output detail view share the same slot components. State the station requirement, ingredient source and exact shortfall. Disable craft when requirements fail, but explain why. Quantity selection recomputes every ingredient and output. The mock hammer costs are placeholders: 4 wood + 3 fiber, with 12 wood and 2 fiber available. They are not balanced game content. Nearby-storage crafting should show where materials come from and how far the rule reaches once supported.

**Field journal.** Use the parchment treatment for discovered trees, rock types, materials and assembly instructions. Reuse datacard names, icons and descriptions. Discovery masking and recipes should not be invented by the presenter. A selected model, its dimensions and a small assembled-house example give building instructions a useful spatial reference.

## Mode and input contract

| Input | Current behavior | Proposed UI behavior |
| --- | --- | --- |
| B / Esc | Enter/exit building | Preserve; close the build overlay cleanly |
| Tab while building | Open component picker | Open catalog; do not also use it for pack navigation |
| Q / E or wheel | Turn 45° while placing | Preserve and update heading display |
| Arrow keys | View-relative 0.25 m nudges, hold repeat | Show brief hint; update offsets from actual resolved preview |
| Page Up / Down | Vertical nudge | Preserve; display height offset |
| R | Flip the linked half-gable slope | Show only for pieces with a flip variant |
| Left / right click | Place / remove in building | Preserve existing commands; menus consume clicks |
| I | No inventory command today | Proposed pack binding, avoiding E's existing rotate-right role |
| 1–9 | No hotbar today | Proposed slot selection; catalog hover-to-bind could be added later |
| Wheel outside building | No proposed hotbar implementation yet | Select quick slot once a hotbar exists; mode ownership prevents rotation conflicts |

Keep LocalPlayerInput as the single local hardware owner. Proposed menu interactions issue commands through presenters, rather than each panel reading keyboard/mouse separately. One modal focus owner should block look/movement, consume opening/closing inputs, and restore the gameplay cursor. Controller mappings and remapping are future work; the examples do not imply they exist today.

## Design tokens and Unity integration

Recommended tokens: panel `#263530`, text `#EFE8D5`, secondary text `#C6CFBE`, selection `#D9BF7B`; a darker backdrop for contrast. Use amber for selection, not rarity. Pair support colors with icons and plain-language reasons. Extend the current Segoe UI typography, with a small range of text sizes and a shared UI-scale setting. Validate at 720p, 1080p and 4K; choose a portable font before supporting systems without Segoe UI.

Build a shared USS token sheet and components for panel, tab, item slot, key hint, status row and selected-part preview. QueryHudStyle is the existing styling seam; BuildMenuView is currently a separate presentation. Their common visual tokens can converge without coupling query logic to inventory or building.

Keep content metadata distinct from state: WorldObjectDefinition supplies identification; BuildDefinition supplies building identity and geometry; future item definitions would supply stackability, thumbnails and recipes. Inventory stacks, equipped slots, durability, build costs and discovery records need separate runtime owners. A hotbar slot should refer to an item/tool or build favorite, not silently duplicate inventory stacks.

Prioritize: (1) unify query/build typography and tokens; (2) categorized catalog using the current ten definitions, plus accurate thumbnails; (3) compact contextual placement HUD and supported/warning/rejected presentations; (4) item/inventory backend and hotbar; (5) crafting/storage integration; (6) assembly plans and blueprint persistence. UI polish in the first three steps can ship against current systems; later steps depend on game features.

## Deliverables and review

- [Woodland exploration concept](woodland-exploration.png): proposed hotbar/vitals surrounding the current query style.
- [Builder catalog concept](builder-catalog.png): current ten choices, corrected half-gable and roof-panel shapes.
- [Field-journal inventory concept](field-journal-pack.png): proposed inventory/crafting and illustrative item counts.
- `builder-catalog-exploration.png`: superseded geometry exploration, retained for provenance only.
- [Generation prompts](GENERATION_PROMPTS.md): three original prompts and targeted geometry correction; built-in ImageGen used.
- Interactive study: Woodland, Builder workspace and Field journal alternatives; each offers catalog, placement and pack views. It includes the actual Unity house sample. Component thumbnails are schematic CSS illustrations, not Unity asset renders. The study demonstrates navigation, filtering, variant flipping, readouts and shortages; it does not perform world placement, physics, harvesting or crafting.

ImageGen results are visual concepts. The field-journal illustration has inconsistent decorative backpack/recipe counts; the interactive study makes its ingredient counts internally consistent. Use the catalog assets and the table above for dimensions and the interactive study for behavior; use the images for art direction. Runtime menus should use thumbnails rendered from their actual BuildDefinition meshes.
