# Transparent hotbar options — October 22, 2026

Generated using the built-in ImageGen tool. This is an exploration only; no Unity runtime code, UI styles, scene objects, item data or inventory contents were changed.

## Compare

- [Meadow comparison](comparison-meadow.png)
- [Forest comparison](comparison-forest.png)

Each option shows a populated illustrative state followed by an empty state. These are labeled close-ups of the hotbar region, not full-screen HUD scaling. The offhand square/marker is separated to the left of nine main slots. No keybinding labels or guidance plaques are added.

| Option | Treatment | Tradeoff |
| --- | --- | --- |
| A · Open stone corners | Small pale corner marks, open interiors, translucent blue selection | Keeps clear slot boundaries while exposing almost all of the world |
| B · Translucent tiles | Separate charcoal-tinted cells with fine ivory borders | Most consistent icon contrast, with more visible framing |
| C · Stone underlines | A short stone dash below each slot, enlarged active underline | Strong material connection with little world coverage |
| D · Almost frameless | Floating icons, active-slot corners, tiny empty-slot dots | Least visual obstruction, quieter empty-slot targeting |

A is the recommended starting point for preserving the stone identity without the old opaque rail. D is useful if minimal framing matters most. C gives a different period-themed direction without filling each slot.

The previous opaque bar's seven item types, order, stack counts and durability overlays are reused as illustrative concept art. The runtime inventory remains empty. The two trailing main slots are empty even in the populated demonstration. Generated icons are mockup art, not newly installed item sprites.

## Full-frame previews

The populated previews preserve the original meadow screenshot at its native 3316×1848 size, with a centered hotbar occupying approximately 42% of screen width:

- [A full frame](a-open-corners-pop-on-original-meadow.png)
- [B full frame](b-smoke-tiles-pop-on-original-meadow.png)
- [C full frame](c-stone-underlines-pop-on-original-meadow.png)
- [D full frame](d-floating-icons-pop-on-original-meadow.png)

The meadow and forest screenshots were not regenerated, dimmed, blurred, recolored or resized when making the native composites. Comparison boards crop and resize those composites for presentation. All 16 combinations (four variants × two states × two original backgrounds) were checked, with zero changed RGB pixels outside the foreground UI, recorded in [pixel-verification.json](pixel-verification.json).

## Motion direction for a future implementation

These PNGs show visual structure only. A future Unity version could use a small icon lift on selection and a short active-outline/tint transition, with genuinely translucent fills that reveal the moving game world. Render consistent canonical item icons with restrained shading and small local shadows. Keep stack counts and durability separate live overlays.

## Generation and reproducibility

[PROMPTS.md](PROMPTS.md) contains the four complete built-in ImageGen prompts and reference paths. The *-raw.png files are the original generated sheets. The *-pop-layer.png and *-empty-layer.png files retain actual alpha for compositing. Very low alpha fringe noise is removed from the presentation layers; no opaque backing is introduced.

[compose.cjs](compose.cjs) crops each foreground row, places it over the original background, verifies pixels outside UI, and builds the comparison boards. Final previews were visually inspected for slot count, offhand separation, selection visibility, item order, readability and background continuity.

