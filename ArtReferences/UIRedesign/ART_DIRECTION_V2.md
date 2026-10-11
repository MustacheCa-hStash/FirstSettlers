# Builder catalog: art direction revision

October 10, 2026. The user approves the original catalog's layout, names, descriptions and functional grouping, but finds its UI artwork and type too polished and formal. These three revisions preserve that information design and change its visual treatment. They supersede the first study's formal green/brass styling recommendation.

## Reading the actual game

The five supplied gameplay screenshots show clear blue skies, strong seasonal foliage colors, simple polygonal rocks, broad terrain forms, cutout leaf shapes, and ordinary daylight. Bark uses broad painted shapes with understated depth. The scene's visual character is direct, bright and materially simple. The first mockup's cinematic scenery and fine gold interface treatment made the UI feel like it belonged to a different rendering style.

Local assets consulted:

- `Assets/Textures/Trees/Bark/T_SugarMapleBark_Stylized.png`: broad, low-frequency painted bark forms.
- `Assets/Textures/Buildings/Wood/ordinary-wood-trim-albedo.png` and its art-reference copy: the existing timber/wattle material vocabulary.
- `Assets/Shaders/BuildingWoodMatte.shader`: diffuse-only building shading; its final lighting deliberately omits metallic, view-dependent specular and reflection-probe contributions.
- `Assets/Shaders/S_Terrain/CustomStylizedTerrain.shader`: explicit grass, sand, mud, rock and snow color families, useful anchors for UI pigment.
- `Assets/Resources/Building/PrototypeCatalog.asset`: the same ten current choices and linked half-gable slope variant.

## Three samples

| Direction | Treatment | Use |
| --- | --- | --- |
| [Painted panels](builder-catalog-painted-panels-v2.png) | Matte slate/bark panels; chalk text; flat leaf-green selection; broad painted separators and corner marks; filled symbols | Recommended primary gameplay UI |
| [Timber tabs](builder-catalog-timber-tabs-v2.png) | Calm dark panels with a painted heading plank and modest wood tabs/favorites rim | Optional material accents for building and journal screens |
| [Sand panels](builder-catalog-sand-panels-v2.png) | Sandy opaque surfaces, dark bark-colored text and forest-green selection | A lighter alternative that relates to shoreline and stone colors |

Each retains the category rail, three-column ten-piece grid, selected half-gable preview, dimensions, placement hints, Choose piece action and bottom favorites strip. The gable remains a right triangle and the roof remains an individual pitched panel. No categories or gameplay capabilities were added.

Recommended approach: use Painted panels as the base. Make the heading slightly hand-painted and broad, with a friendly humanist body font and readable numerals. Keep texture quiet behind text. The revised font appearance is conceptual; no production typeface or font asset was selected or installed.

## Rules for carrying this into real UI

- Share the world's material simplicity: broad matte color areas, shallow painted variation, blunt symbols and a few soft angular corners.
- Reserve leaf-green fill for the selected category and primary action; use chalk/leaf corner marks for selected item cards. Keep selection visible through shape and text as well as color.
- Use a small amount of timber where it communicates construction; let calm panel surfaces carry the catalog and descriptions.
- Preserve the approved spacing and hierarchy. Slight edge irregularity is visual artwork, not an excuse to shift hit targets, baselines or item alignment.
- Use a reusable static edge/surface texture or nine-slice panel artwork. Avoid random edges that change as a panel resizes or animates.
- Render production component thumbnails from the actual BuildDefinition meshes and materials under simple diffuse lighting. The generated thumbnails are style references, not exact geometry or UV exports.
- Maintain text contrast on opaque panels against both the bright meadow and dark forest. Use a readable body face, larger slightly irregular headings, and consistent dimension/key-hint typography.

The generated scenes are based on the user screenshots and reinterpret them; they do not preserve every source pixel. Names, arrangement and controls were visually reviewed. These are mockup images only. No Unity scene, shader, material, font, runtime UI or input file was edited. The previously existing SmearScene working-tree change was left in place. The earlier interactive study still illustrates the original art direction and was not silently restyled.

Full prompts: [RESTYLE_PROMPTS.md](RESTYLE_PROMPTS.md). Generated using built-in ImageGen, with one separate edit call per direction. The original builder-catalog.png is retained for comparison.
