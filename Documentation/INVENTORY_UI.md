# Empty stone inventory and hotbar

The approved A/slender-pillars design is implemented with Unity UI Toolkit. The window contains 27 backpack slots, nine assignment hotbar slots, four armor slots, offhand, 2×2 crafting and output. All slots are empty. The character view is a blank Image. The green recipe-book button is centered under the 2×2 grid and has no action. No sample items, transfers, recipes, tooltip content or save records are created.

## Controls and input

- Tab opens inventory from gameplay or building, and Tab again closes it. Escape also closes it.
- WASD, sprint, crouch and jump remain active with inventory open. Mouse motion moves the free cursor, without rotating the player or camera. Clicks, wheel and building controls do not issue world interaction commands.
- B replaces inventory with the building catalog. Opening another menu through LocalPlayerMenus.Open likewise replaces inventory. Closing that menu returns to gameplay; no menu history is retained.
- Keys 1–9 select the hotbar cell. Selection is synchronized between the gameplay HUD and inventory row. A selected cell has a slate-blue fill and ivory outline; unselected cells retain warm stone. Clicking a hotbar cell in inventory also selects it. No hotkey numerals or guidance plaques are shown.
- Returning to gameplay recaptures the cursor and suppresses two mouse-look frames to avoid a camera jump. Placement is exited when inventory or another menu opens.

The gameplay hotbar includes a blank offhand square and ignores pointer picking. It is hidden while any menu is open. The building placement readout sits above it to avoid overlap.

## Runtime and assets

LocalPlayerInput automatically attaches LocalPlayerMenus to its own object; existing player scenes do not need edits. Restart Play mode after Unity imports the files. Each local menu owner creates one InventoryView and a cloned PanelSettings, and disposes them with the owner. The shared building panel resource is not modified.

| Purpose | File |
| --- | --- |
| Active-menu ownership, cursor and selected index | Assets/Scripts/UI/LocalPlayerMenus.cs |
| Visual tree, shared empty slot construction and responsive scale | Assets/Scripts/UI/InventoryView.cs |
| Layout and stone styles | Assets/Resources/Inventory/Inventory.uxml, Inventory.uss |
| Hardware edges, movement and interaction gating | Assets/Scripts/PlayerMovement/LocalPlayerInput.cs |
| Building-menu integration | Assets/Scripts/Building/BuildingController.cs |
| Query-card suppression while menus own input | Assets/Scripts/UI/QueryHudPresenter.cs |
| Reused artwork and fonts | Assets/Resources/Building/UI/StonePanel.png, StonePillar.png, Fonts/StoneHeading.asset |
| Synthetic rendering/native input validation | Assets/Editor/InventoryUiValidation.cs |

The 704×648 logical window scales uniformly with the viewport and remains centered. At 1920×1080 it occupies about 37% width and 60% height. Backpack, gear, crafting and hotbar use the same 68px square cell. Pillars are narrow and the heading is limited to Crafting. The world is not dimmed or blurred.

Future menus register with `menus.Open(owner, onClosed, movementAllowed)` and close with `menus.Close(owner)`. The callback hides/cleans their view when replaced. A menu that exits to its own gameplay mode can use `menus.Close(owner, false)` to release ownership without firing its replacement callback. Do not manage a separate menu history or cursor lock in new screens. World interaction systems should consult `LocalPlayerInput.WorldInteractionAllowed` before dispatching actions; passive view queries may still sample the world.

## Future inventory ownership

Keep actual inventory state independent of this UI and menu lifetime. A player-owned inventory model should own the 27 backpack slots, nine hotbar slots, four armor slots, offhand, selected index, crafting inputs and any cursor-held stack. UI views should subscribe to state changes and submit validated transfer commands to one service. Main hand should derive from the selected hotbar slot, avoiding duplicate ownership.

ItemDefinition assets can hold stable IDs, icons, stack limits, durability limits and equipment eligibility. Runtime item records hold quantity, current durability and stable instance identity. Save/load belongs to the player's gameplay/save service, storing IDs and slot positions rather than VisualElements or scene-object pointers. Containers can reuse the slot service while owning their own state. Do not store item stacks in LocalPlayerMenus or InventoryView. Move the temporary selected-index property into the player model when that model is introduced.

Crafting consumption/result delivery and stack moves need atomic validation and conservation checks. Closing a view must not delete or duplicate items. The detailed behavior is recorded in [INVENTORY_PLAN.md](INVENTORY_PLAN.md); this implementation deliberately stops at the requested empty UI.

## Validation

Run `Tools/ValidateBuildingPrototype.ps1 -InventoryChecks` for real UI rendering at 1280×720, 1920×1080, 3840×2160 and 1024×768, checking empty slots, blank character, centered book, square/aligned cells, contained layout, passive HUD and synchronized palette/selection. Captures are written under ArtReferences/UIRedesign/ImplementedInventory in the isolated validation project.

Run `Tools/ValidateBuildingPrototype.ps1 -InventoryInputChecks` for native Input System Tab/B/Escape/1–9 frames, held-Tab suppression, movement with the menu open, mouse-look suppression, placement/interaction blocking, cursor recapture and replacement without return history. Both suites use an empty synthetic scene and add no item data. Runtime/editor compilation is included in the shell harness. Native editor graphics/licensing may require normal Windows access outside the restricted sandbox.

October 11 verification: runtime/editor compilation, four-resolution rendering/layout validation, native inventory-input checks and the existing building-input regression suite passed. Logs are .utmp/building-prototype/inventory-ui.log, inventory-input.log and input.log. Actual Unity renders and original-meadow composites are saved in [ImplementedInventory](../ArtReferences/UIRedesign/ImplementedInventory/). The movement check holds W through normal acceleration instead of relying on a tiny first-frame displacement below the CharacterController threshold; no movement rules were changed.
