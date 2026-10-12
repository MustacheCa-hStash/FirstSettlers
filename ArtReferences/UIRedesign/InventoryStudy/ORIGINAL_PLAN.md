# Inventory proposal and stone-theme mockups

October 10, 2026. Proposed design, not an implemented inventory system. The existing stone-and-pillars building menu is the visual reference. The next implementation should start with the inventory core and Pack + hotbar, followed by containers and crafting.

## Screens

[Overview](../ArtReferences/UIRedesign/InventoryStudy/inventory-overview.png)

| Screen | Layout and behavior |
| --- | --- |
| [Pack and equipment](../ArtReferences/UIRedesign/InventoryStudy/pack-on-original-meadow.png) | Equipment/navigation left, 6 × 4 carried-item grid middle, selected-item information and actions right, nine quick slots below |
| [Storage transfer](../ArtReferences/UIRedesign/InventoryStudy/storage-on-original-meadow.png) | Carried items and chest side by side, explicit transfer/split controls, the same hotbar below |
| [Crafting](../ArtReferences/UIRedesign/InventoryStudy/crafting-on-original-meadow.png) | Categories left, recipes middle, selected output/ingredients/station/quantity right |
| [Hotbar during play](../ArtReferences/UIRedesign/InventoryStudy/hotbar-on-original-meadow.png) | Compact nine-slot limestone shelf, selected-item name, counts and separate tool durability; I/B hints |

Reuse the existing StonePanel and StonePillar artwork, Georgia headings, Segoe UI body type, slate-blue selection and fine geometric corner motifs. Pillars belong at modal outer edges. Compact gameplay elements use a shallow stone rim, keeping the world visible. Inventory cells use a quiet outline to distinguish occupied and empty slots; the selected cell is slate blue. Item artwork should eventually be rendered from authored item meshes/materials using the building thumbnail workflow.

Start with **24 carried slots**, **two equipment references** (main hand/off hand), and **nine hotbar bindings** as a testable first capacity. Capacity and item limits belong in game data so they can change after playtesting. Avoid introducing weight/hunger/armor statistics before the corresponding game rules exist.

## Delivery order

1. **Inventory core and item content.** Item definitions, carried instances/stacks, add/remove/move/swap/split/merge, capacity checks, change events and save-ready stable IDs.
2. **Pack and hotbar.** The first usable screen, selected-item detail, drag/drop plus click controls, sorting, two equipment references and quick-slot assignment. A temporary pickup fixture can exercise the loop before harvesting is connected.
3. **Storage.** A container owns the same inventory model; the transfer screen binds two owners to the same slot component. Add explicit container interaction and capacity-aware transfer.
4. **Crafting.** Recipe definitions and transactional spending/output, quantity, shortages and station conditions. Begin with backpack ingredients; add nearby storage later with an explicit ingredient-source rule.
5. **World integration and persistence.** Connect actual pickups/harvesting, item use, container persistence and player saves. Introduce building material costs only after this economy is usable and balanced.

## Item model

Create a separate ItemDefinition ScriptableObject with stable ID, display name, icon, category, stack limit and optional equipment/tool metadata. Runtime carried data owns quantity and item-instance ID; tools also own durability. Static definitions never store a player's quantity or current durability.

WorldObjectDefinition currently describes world identity and tree-generation authoring data. Keep it distinct from inventory definitions. A world tree can resolve a harvesting result to a wood item definition through a drop/interaction rule, rather than putting inventory stacks into the tree's datacard.

An inventory container owns ordered slots and the item instances in them. A service performs validated moves, swaps, splitting and merging, then emits state changes. The views render that state and submit commands; they never alter quantities directly. Preserve total quantities across container transfer. A failed operation leaves both owners unchanged and reports a useful reason.

The mock equipment panel displays the same axe instance already carried in the pack. Equipping and assigning a hotbar slot must not clone it. For tools, equipment/hotbar binding should use the stable item-instance ID. For stackable materials and consumables, a hotbar binding can use the item type and resolve the carried total, remaining stable when stacks move or merge. Transferring an item away updates or clears affected equipment/quick-slot references according to the ownership rule. Counts in the gameplay hotbar include carried items, not everything stored in nearby chests.

Persist item definition IDs, quantities, tool-instance IDs/durability, slot positions and equipment/quick-slot references in a versioned player/container record. Establish that serialization shape in the core; wire it to the game's eventual save owner once persistence is introduced. Never save raw Unity object pointers as item identity.

## Interactions and input

**I** opens the pack, **Esc/I** closes it. This avoids E, which already turns build previews. **1–9** selects quick slots during normal gameplay. The wheel can cycle quick slots outside building; during placement it continues rotating pieces. Inside a menu it scrolls the appropriate list. A single local input/menu coordinator owns these mode decisions.

Opening inventory pauses placement input and clears its ghost. Closing it restores the prior building selection/context and cursor state. Switching between Pack and Crafting keeps one modal focus owner. Returning to a build catalog preserves its free cursor; returning to gameplay recaptures the hidden cursor and suppresses recentering deltas, as the building menu already does.

Use drag/drop for moving and assigning, with equivalent click selection and explicit actions. Shift-click transfers a full stack when another destination container is visible. Right-click offers a quantity-aware split. In a single pack view, quick move can mean assigning to a quick slot or moving to the available equipment target; the label should be context-specific in implementation. Sort is explicit and preserves stable item bindings. Ordinary closing/reopening preserves selection and manual slot ordering.

The query HUD should offer pickup/container/harvest prompts only when an implemented interaction resolver supplies those actions. The existing identification result or a breakability flag alone does not execute harvesting. Add actual world actions as part of world integration, with input routes consistent with the selected tool and active menu.

## Crafting rules

Recipe definitions supply output, ingredients and station requirement. The service computes availability, output capacity and maximum quantity from current inventory state. The panel displays available/required counts, the exact shortfall, station conditions and ingredient source. The main button explains its disabled state through adjacent text: missing resources, missing station, or no output capacity.

Validate the complete craft before spending ingredients, and commit ingredient removal and output addition together. Quantity changes recompute all requirements. A stale selection or a container change triggers another availability check before commit. When nearby storage is introduced, label it and define its reach; the interface should never conceal where materials are being consumed.

## Sample data and mockup limits

The illustrated pack contains 12 occupied slots: wood 20, stone 16, fiber 6, rope 4, berries 8, meat 2, stone axe 1, wooden hammer 1, torch 1, flint 7, hide 3 and reeds 12. The chest contains wood 32, stone 40, fiber 18 and rope 6. A sample hammer costs 4 wood + 2 fiber, making three the ingredient-limited maximum with that carried supply. The torch example is short one resin; cooked meat needs a campfire. These costs, items and stations are illustrative design content, not balanced or implemented game features.

ImageGen illustrates appearance and density. Item placements/artwork differ slightly between the generated screens, and durability tracks are illustrative rather than exact proportional renderings. Real screens must bind one canonical model, preserve slot positions across views and derive every number and bar from state. A normal production inventory view should not pin an unrelated recipe requirement unless the player explicitly pins it.

All four full-resolution composites use the exact same original autumn screenshot at 3316 × 1848. The background was not regenerated, resized, cropped, dimmed or recolored. Pixel verification found zero changes wherever overlay alpha is zero; the overview uses reduced previews. Transparent layers, [prompts](../ArtReferences/UIRedesign/InventoryStudy/PROMPTS.md), [compositing script](../ArtReferences/UIRedesign/InventoryStudy/compose.cjs) and [verification](../ArtReferences/UIRedesign/InventoryStudy/pixel-verification.json) are saved with the mockups. No runtime game files were edited for this design request.
