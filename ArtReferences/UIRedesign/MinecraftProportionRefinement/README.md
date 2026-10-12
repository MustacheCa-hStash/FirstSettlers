# Inventory and hotbar proportion refinement

Created 2026-10-11 using the built-in ImageGen tool. Visual proposals only. No inventory or hotbar game code was changed.

## Previews

- [Inventory comparison](inventory-comparison.png): A, slender pillars; B, quiet stone relief.
- [Hotbar close-up](hotbar-closeup-on-original-meadow.png).
- [A at full resolution](inventory-thin-pillars-on-original-meadow.png).
- [B at full resolution](inventory-quiet-stone-on-original-meadow.png).
- [Hotbar at actual HUD scale](hotbar-slate-warm-stone-on-original-meadow.png).

The inventory panels occupy approximately 36% of screen width and 59–60% of screen height, centered. The supplied Minecraft reference occupies approximately 36% width and 61% height. Both proposals reduce the oversized prior heading/character area and include the assignment hotbar within one panel.

Each proposal depicts 27 backpack slots (9×3), 9 hotbar slots, 4 armor slots, an offhand slot, a front-facing character, and 2×2 crafting with an output slot. A small recipe-book icon is a concept placeholder. Item and character renders are illustrative ImageGen art, not captures from the game.

The hotbar retains Slate inset's slim rail, shallow recessed cells, corner blocks, and separate offhand square. All slot interiors, including the selected slot and offhand, use Carved stone rail's warm sandstone palette. Selection uses an ivory outline with a dark inner stroke; stack counts and wear bars remain visible. No slot hotkey numerals or guidance plaques.

## Background preservation

All full-resolution previews use the original source-01.png meadow screenshot at 3316×1848. Only the generated transparent UI foreground was scaled and composited. The source background was not regenerated, resized, cropped, dimmed, blurred, or recolored. The comparison board and hotbar close-up are explicitly presentation previews derived from those full-resolution composites.

[pixel-verification.json](pixel-verification.json) records zero changed RGB pixels outside each UI overlay.

## Generation records

[PROMPTS.md](PROMPTS.md) contains the three complete prompts, input reference paths, and the targeted correction that removed the remaining blue fill in variant A.

The *-raw.png files retain the generated outputs. The *-ui-layer.png files are cropped transparent foreground layers used to make the previews. No generated mockup is connected to the game's runtime UI.

