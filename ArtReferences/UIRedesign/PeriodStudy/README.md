# Quiet overlay on original screenshots; period-inspired alternatives

October 10, 2026. The user selected Quiet overlay as the strongest direction, required their backgrounds to remain unchanged, and requested additional visual ideas informed by an Iron Age to pre-Roman-Empire setting.

## Comparison images

[Quiet overlay on all five backgrounds](quiet-five-backgrounds.png)

Native-resolution composites:

1. [Autumn meadow](quiet-background-01.png)
2. [Green forest](quiet-background-02.png)
3. [Summer lakeside](quiet-background-03.png)
4. [Autumn lakeside](quiet-background-04.png)
5. [Mountain meadow](quiet-background-05.png)

The same clean Quiet overlay layer is used for all five. Its interface is a faithful generated recreation of the approved concept, rather than a pixel-identical extraction of its contaminated panels. The backgrounds are the exact user-provided screenshots, not regenerated scenes.

[Four period-inspired alternatives on the same autumn meadow](period-directions.png)

- [Papyrus & ink](papyrus-background-01.png): plant-fiber writing surfaces, dark ink and red marginal accents.
- [Wax tablets](wax-tablets-background-01.png): shallow framed tablet surfaces, dark wax and modest hinge details.
- [Hide & bronze](hide-bronze-background-01.png): muted hide surfaces with restrained metal edging and geometric accents.
- [Stone & pillars](stone-pillars-background-01.png): limestone/plaster faces and narrow Greek-inspired pillar edges.

## Exact-background workflow

Generated only transparent UI layers using built-in ImageGen. The original screenshot backgrounds were not submitted for regeneration. Removed stray alpha pixels beyond the intended panel regions. Composited with Sharp in JavaScript, as required to preserve the user's backgrounds exactly.

The base image is never resized, cropped, recolored, globally dimmed or blurred. Only the UI layer scales uniformly to fit each source screenshot; the source image remains at its native dimensions. The full-size output PNGs preserve every pixel where the overlay alpha is zero. The two comparison boards use scaled previews for convenient inspection; use the individual files for full-resolution comparison.

[Pixel verification](pixel-verification.json) records **zero changed background pixels outside the overlay** for all nine full-size outputs, with approximately 2 million outside-overlay pixels checked per image. Source copies in Backgrounds are byte-identical to the five attached PNGs and are retained for future exact comparisons.

[compose.cjs](compose.cjs) reproduces the composites and verification using the five saved original backgrounds and the five UI layers. [PROMPTS.md](PROMPTS.md) contains the full ImageGen prompt set.

## Historical basis and limits

These are alternative material traditions across the ancient Mediterranean, Europe and Near East, not a reconstruction of one culture or a claim that every Iron Age settlement used all of them. No exact region or century has been selected in this task.

- [The Met: Papyrus-making in Egypt](https://www.metmuseum.org/essays/papyrus-making-in-egypt) explains papyrus as a plant-derived ancient writing support. This motivates papyrus fiber/ink rather than modern paper or a medieval bound book.
- [Berlin Papyrus Database: Writing materials](https://berlpap.smb.museum/schreibmaterialien/?lang=en) discusses papyrus, wood, wax, skins and parchment in antiquity.
- [British Museum: waxed ivory writing tablet, 720–710 BC](https://www.britishmuseum.org/collection/object/W_1954-1115-1) establishes a pre-imperial example of waxed writing surfaces. The timber frames in this concept are an artistic adaptation, not a copy of that ivory object.
- [The Met: Architecture in Ancient Greece](https://www.metmuseum.org/it/essays/architecture-in-ancient-greece) documents Archaic/Classical column-and-lintel architecture before the Roman Empire. The two narrow pillar accents use that broader design vocabulary.
- [British Museum: Britain and Europe 800 BC–AD 43](https://www.britishmuseum.org/collection/galleries/britain-and-europe-800-bc-ad-43) covers distinct Iron Age material cultures and decorated objects. The bronze/hide option is a restrained visual interpretation, not an exact motif reproduction.

Use period references as material and shape cues while retaining legible current-language labels, consistent controls and clear selection. Quiet overlay remains the user's preferred base. Papyrus could supply occasional reference/recipe surfaces; a small bronze/stone detail can add period character without framing every panel as a monument.

No Unity scene, shader, material, font, gameplay, input or runtime UI file was changed.
