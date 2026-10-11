# Production building UI skin prompts

Built-in ImageGen, October 10, 2026. The approved stone-and-pillars concept was used as a style reference. No world/background artwork was generated for the implemented UI. Source images are retained as `stone-panel-source.png` and `stone-pillar-source.png` in the parent directory. Production assets are `Assets/Resources/Building/UI/StonePanel.png` and `StonePillar.png`.

The panel was exported at 512 × 512. The pillar's faint surrounding glow was removed through alpha cleanup; transparent bounds were trimmed and the result exported at 1024 pixels high for nine-slice stretching. Component thumbnails were rendered in Unity from the actual catalog meshes/materials, not ImageGen.

## Panel

Use case: ui-mockup. Production game UI skin asset for the stone-and-pillars interface in the reference. Reference image role STYLE ONLY. Generate ONE square full-canvas opaque warm pale limestone/plaster PANEL with a calm subtly mottled center and a narrow shallow stone bevel/frame around all four sides. Nine-slice-ready texture: each corner and all border detail contained within outer 10 percent; center is quiet nearly uniform ivory stone with subtle broad texture, suitable behind dark UI text. Four corners each have one tiny restrained incised square spiral motif. Flat frontal orthographic elevation, aligned straight rectangle, no perspective, no world scene. Match broad painted stylized materials of reference; modest diffuse shading, no shiny polished marble, no heavy grit. Entire canvas is panel, no empty margins. No letters, no text, no glyphs, no category icons, no item models, no pillars, no transparent holes. Clean reusable UI panel artwork, not a screenshot.

## Pillar

Use case: ui-mockup. Production game UI decoration asset, ONE narrow stylized warm limestone DORIC PILLAR, matching the reference stone-and-pillars menu. Style reference only; generate isolated decoration with GENUINE transparent alpha surroundings. Tall portrait image with height approximately six times width; front orthographic elevation, straight vertical shaft, modest plain Doric capital at top, subtle simple base at bottom. Pillar fills almost all image height and 75 percent width; capital/base slightly wider than shaft. Very restrained shallow fluting, warm ivory/limestone color, broad painted diffuse lighting, no realistic marble veining. A narrow UI edge decoration, readable scaled to 40-70 pixels wide, not monument concept art. No world, scene, ground, cast shadow extending outside pillar, letters, panels, text, models, laurels, imperial symbols or statuary. Keep top and bottom architectural detail compact, middle shaft long and uniform for vertical nine-slice stretching. Transparent background.

