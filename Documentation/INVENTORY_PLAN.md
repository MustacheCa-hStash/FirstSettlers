# Inventory and hotbar plan: Minecraft behavior, stone-and-pillars presentation

October 11, 2026. The user's revised direction follows Minecraft's survival inventory and hotbar functions with stone-and-pillars artwork. The approved A/slender-pillars layout now has an empty UI implementation; item ownership, transactions and persistence remain planned. See [INVENTORY_UI.md](INVENTORY_UI.md) for current controls and integration. This supersedes the earlier 24-slot/shortcut-hotbar proposal.

## Functional baseline

| Area | Model |
| --- | --- |
| Backpack | 27 actual slots, displayed as nine columns × three rows |
| Hotbar | Nine additional actual inventory slots, not bindings to backpack stacks |
| Main hand | The currently selected hotbar slot; no extra main-hand equipment slot |
| Armor | Four dedicated slots: head, chest, legs, feet |
| Offhand | One dedicated item slot, represented beside the gameplay hotbar when occupied |
| Personal crafting | Four actual ingredient slots in a 2×2 grid and one computed output preview |
| Item information | Hover tooltip; no permanent inspector or item-description panel |

The backpack plus hotbar provide **36 carried slots**, with armor and offhand stored separately. Moving an axe from the backpack to the hotbar transfers that item instance rather than leaving a duplicate in its old slot. Equipping armor or moving an item to offhand likewise changes its slot ownership.

Reference: [Minecraft's official personal-crafting guide](https://www.minecraft.net/en-us/article/how-craft) shows the four-cell input and output. [Microsoft's Minecraft EquipmentSlot reference](https://learn.microsoft.com/en-us/minecraft/creator/scriptapi/minecraft/server/equipmentslot?view=minecraft-bedrock-stable) states that player mainhand is the active hotbar slot.

## Visual proposals

[Inventory comparison](../ArtReferences/UIRedesign/MinecraftInventoryStudy/inventory-comparisons.png) and [hotbar close-ups](../ArtReferences/UIRedesign/MinecraftInventoryStudy/hotbar-comparisons.png).

- **Compact inventory:** familiar upper equipment/character/crafting region, with 27 slots and a separate hotbar aligned below. Recommended starting layout because slot relationships are clear and the window leaves useful world space around it.
- **Character-led inventory:** a larger frontal player preview and gear slots on the left; crafting and backpack on the right. Useful if visible equipment changes are a major part of progression.
- **Split panels:** separated equipment/crafting slabs above a lower storage slab, echoing the building UI's floating stone panels.

Gameplay hotbar choices:

- **Carved stone rail:** shared light limestone frame, recessed cells, slate-blue selected fill. Strongest continuity with the building menu.
- **Separate stone tiles:** lighter framing, transparent gaps, outline selection. Keeps the world more visible and makes each cell distinct.
- **Slate inset:** dark cell interiors, ivory stack numbers and a light selection rim. Recommended counterpart to Compact if icon contrast is the priority.

Use pillars at inventory-window edges, with compact rim/corner motifs on the gameplay hotbar. Avoid adding a temple-scale frame to the HUD. Slot sizes and UI scale can be tuned together without changing capacity or transfer rules.

## Icon and tooltip rules

Stack count sits at the item icon's lower-right. Hide a count of one and omit counts for nonstackable tools/armor. Durability is a short bar along the slot's bottom edge, with space reserved so it never collides with the count. Production bars derive from current/max durability and can remain hidden for an undamaged item, following the intended Minecraft-style behavior.

Counts and bars are **live UI overlays**, not permanently painted into the item sprite. Use the same icon/overlay component in backpack, hotbar, armor, offhand and crafting. Keep item icon identity and slot ordering consistent across every view.

A small tooltip shows the name, exact durability and relevant item information on hover. It follows the pointer with a small offset, clamps to the screen and does not cover the hovered slot. Keyboard focus can expose the same information. No persistent item-description column, equip/unequip buttons, hotbar title plaque, I/B guide, or numeric key labels inside the hotbar cells. Number-key and wheel selection can still operate without drawing those bindings on screen. The selected slot uses a rim/fill so selection does not rely on a number or durability color.

## Character preview

The current UI uses a blank Image in the character nook, as requested. Later, render the actual equipped player from the front into a small RenderTexture while inventory is visible. Reuse the actual rig/assets; illustrated mockup people are placeholders. The preview can reflect armor, selected hotbar item and offhand. Keep its camera separate from the world and disable it when the menu closes.

## Inventory operations and state

Create ItemDefinition assets for ID, name, icon/model, item-specific stack limit, equipment eligibility and durability limits. Runtime ItemStack/instance records contain quantity, durability and stable instance identity.

A PlayerInventory owns the 27 backpack slots, nine hotbar slots, four armor slots, offhand and active hotbar index. A carried cursor stack and the four crafting inputs are also owned state; opening or closing UI must not create or delete them. Mainhand reads the active hotbar slot rather than a second equipped-item record.

All moves, swaps, splitting, merging, quick transfers and equipment changes go through one validated service. Views issue commands and observe changes. Preserve quantities and item instance IDs across operations. Save slot positions, active hotbar index, equipped items and relevant instance state by stable IDs rather than Unity object pointers.

Target familiar interactions: left-click pick up/place/merge/swap; right-click split a stack or place a single item; drag distribute; shift-click quick transfer; double-click collect compatible items up to the stack limit; number-key swap between a hovered inventory cell and the corresponding hotbar slot. Armor accepts the appropriate item type. Offhand swapping exchanges it with the selected hotbar slot. Define destination priority consistently for quick transfers and partial-stack merges.

## Integrated 2×2 crafting

Crafting lives in the inventory's upper section. Place actual items in four input cells; match the pattern to a recipe; display the available result in the output cell. Clicking the output consumes one craft and places that result on the carried cursor or into the receiving slot. Shift-taking can craft repeatedly up to ingredient and destination capacity. There is no separate Craft button or quantity selector on this personal grid.

Validate ingredient consumption and output delivery together so insufficient output capacity cannot consume inputs. Return unused crafting ingredients and the cursor-held stack safely when the menu closes, with an explicit world-drop fallback if no inventory capacity remains. Larger crafting grids can belong to future workstations, rather than changing personal crafting behavior.

The mock recipe uses two planks vertically to show four sticks in the output. Values and art are illustrative. The sample separately contains wood 32 in the backpack, wood 20 in the hotbar and one plank in each of two crafting cells; those are separate physical stacks, not repeated labels for one item.

## Input and existing building integration

A single LocalPlayerMenus instance, attached automatically by LocalPlayerInput, owns the active local menu. Tab opens/closes inventory. Inventory releases the cursor and suppresses look and world interactions while keeping locomotion active. Opening inventory exits building and clears the ghost. B or another registered menu replaces inventory. Closing any menu returns to gameplay, with look-delta suppression after cursor capture; there is no previous-menu/build-context restoration.

Number keys 1–9 currently select the empty hotbar cell in both gameplay and inventory views. The selected cell has a blue background; unselected cells are warm stone. No key labels are drawn. Tab now belongs to inventory rather than the old placement catalog shortcut. Wheel selection is deferred; building placement retains Q/E and wheel rotation, and no build commands are submitted while a menu owns input.

Retain the building component catalog and its quick-piece strip as definition selection. Do not treat those ten component definitions as extra carried inventory slots or fabricate building-item stacks to fill the pack.

## Implementation sequence

1. **Core and slot operations:** item content, 36 carried slots, dedicated armor/offhand, active hotbar selection, cursor-held stacks and stable save representation. Verify conservation, stack limits and equipment rules.
2. **Personal inventory and hotbar:** shared slot/tooltip component, selected-item display, cursor/focus handling, armor/offhand transfers and a front-facing character preview. Match the approved layout.
3. **2×2 crafting:** input slots, recipe matching, output preview/take and item-preserving close behavior.
4. **World pickups/use and storage:** connect real interaction actions, item use and container screens using the same slot service and transaction rules.
5. **Persistence and economy:** wire player/container save ownership, introduce workstations and building material costs when gameplay content is ready.

## Deliverables and limits

The [six proposals](../ArtReferences/UIRedesign/MinecraftInventoryStudy/README.md) use the original autumn screenshot unchanged outside the UI, confirmed by pixel checks. Full-size files retain native dimensions; the hotbar comparison board is explicitly a close-up. Slot counts and required groups were visually reviewed. Generated character/icon artwork varies slightly; production must render canonical assets and exact runtime values.

Built-in ImageGen supplied the original mockup artwork. [Prompts](../ArtReferences/UIRedesign/MinecraftInventoryStudy/PROMPTS.md), foreground layers, compositing code and verification are saved with the mockups. That initial design phase did not change runtime code. The subsequently approved empty A/slender-pillars implementation and actual Unity captures are described in [INVENTORY_UI.md](INVENTORY_UI.md). Earlier mockups and their original plan remain in InventoryStudy for provenance.
