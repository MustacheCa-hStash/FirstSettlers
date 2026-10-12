# Inventory and hotbar: Minecraft structure, stone theme

October 11, 2026. These design proposals supersede the prior 24-slot pack, two equipment references and shortcut-only hotbar proposal. Runtime inventory gameplay is not implemented by this task.

## Inventory layouts

[Inventory comparison board](inventory-comparisons.png)

- [Compact inventory](inventory-compact-on-original-meadow.png)
- [Character-led inventory](inventory-character-on-original-meadow.png)
- [Split-panel inventory](inventory-split-on-original-meadow.png)

Compact is the closest to the familiar Minecraft arrangement and keeps backpack/hotbar columns aligned. Character-led emphasizes the equipment preview; its larger hotbar spans beneath both regions. Split panels echoes the separate stone slabs of the building UI while keeping the storage and hotbar as a clear lower group.

All three include 27 backpack cells (nine columns × three rows), nine separate hotbar cells, four armor slots, one offhand slot, a front-facing character, and four personal-crafting inputs plus an output. The Compact view includes one example hover tooltip; the others show the uncluttered default state. There is no permanent item inspector.

## Gameplay hotbars

[Hotbar detail comparison board](hotbar-comparisons.png)

- [Carved stone rail](hotbar-carved-on-original-meadow.png)
- [Separated stone tiles](hotbar-tiles-on-original-meadow.png)
- [Slate inset hotbar](hotbar-slate-on-original-meadow.png)

Carved rail is the closest material match to the building panels. Separate tiles use lighter framing and more transparent gaps. Slate inset makes pale icons and counts stand out against a darker interior. Every version has nine hotbar slots and a distinct offhand slot. Numeric slot key labels and guidance plaques are removed; the remaining numbers are item stack counts. Exact tool durability appears only in the example tooltip; wear is shown by bars over the item icons. Main hand derives from the active hotbar slot.

These are static design illustrations. Character appearance, clothing and icons are representative proposals, not a render of an existing playable avatar. The three character examples differ slightly in artwork; production will use one actual player model and its equipped objects. Some item artwork depicts wood as logs or planks across variants; the implementation must use one canonical icon for each item definition. Bars will be computed from runtime values rather than baked into the item artwork.

## Exact background and files

All six full-size outputs use the same original autumn meadow screenshot, saved at 3316 × 1848. It is never regenerated, cropped, resized, blurred, recolored or globally dimmed. The UI layers are composited separately. [Pixel verification](pixel-verification.json) reports zero changed pixels outside the overlay for every final composite. Comparison boards are reduced previews; the hotbar board crops the lower scene only for closer inspection.

[compose.cjs](compose.cjs) recreates the composites from saved foreground layers. [PROMPTS.md](PROMPTS.md) preserves the built-in ImageGen prompt set. The current functional plan is [INVENTORY_PLAN.md](../../../Documentation/INVENTORY_PLAN.md).

