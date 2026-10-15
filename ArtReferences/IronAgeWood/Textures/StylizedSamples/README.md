# Stylized wood texture-swap samples

Four base-color samples made with the built-in image_gen tool, informed by the supplied gameplay screenshots and the existing ordinary wood atlas. **Weathered woodland** is the quieter match for the grey-brown tree trunks; **soft hewn oak** gives buildings a warmer silhouette with more visible grain. **Golden oak** uses pale wheat-tan color inspired by Minecraft oak planks with the game's painted surface style. **Meadow timber** uses muted oat-brown tones and sparse gentle grain for a calmer match to the green hills and beige shores. All remove photographic pores, scratches and dense fibers from the wood regions.

## Preview and deliverables

- [Existing wall comparison](existing-wall-texture-swap.png): current texture, soft hewn oak, weathered woodland, applied to three copies of the existing `Modular/SplitPlankWall_4.00Wx3.00Hx0.25D.fbx`. Geometry and UV0 are unchanged. This is a Blender preview; game lighting and the Unity building shader can change the final appearance.
- [Editable preview scene](texture-swap-preview.blend).
- [Soft hewn oak atlas](../../../../Assets/Textures/Buildings/Wood/ordinary-wood-trim-soft-hewn-oak.png).
- [Weathered woodland atlas](../../../../Assets/Textures/Buildings/Wood/ordinary-wood-trim-weathered-woodland.png).
- [Additional wall comparison](additional-wall-texture-swap.png): soft hewn oak, golden oak, meadow timber on the same imported wall mesh, with identical UVs, materials and preview lighting except the base-color texture.
- [Golden oak atlas](../../../../Assets/Textures/Buildings/Wood/ordinary-wood-trim-golden-oak.png).
- [Meadow timber atlas](../../../../Assets/Textures/Buildings/Wood/ordinary-wood-trim-meadow-timber.png).
- [Additional editable preview scene](additional-texture-swap-preview.blend).
- New material siblings: `Assets/Materials/Buildings/Wood/SplitPlankWood-GoldenOak.mat` and `SplitPlankWood-MeadowTimber.mat`.
- Optional Unity material siblings: `Assets/Materials/Buildings/Wood/SplitPlankWood-SoftHewnOak.mat` and `SplitPlankWood-WeatheredWoodland.mat`. These copy the current shader, white tint, instancing and shading properties, changing only the name and base-map reference.

The initial sample work added sibling textures and materials. On request, an earlier pass assigned soft hewn oak to the existing shared `SplitPlankWood` and `WattleWood` materials for in-game inspection. Only their base-map references changed; their shaders, settings, material identity, UVs, meshes, definitions and scenes were retained. The active texture may subsequently be changed in the Unity Inspector. Wattle's weave and alpha pixels remain identical between these samples; its timber framing uses the selected wood.

For quick comparison in Unity, select `Assets/Materials/Buildings/Wood/SplitPlankWood.mat` and change the Inspector's **Ordinary Wood Trim (UV0)** texture field. Choose `ordinary-wood-trim-soft-hewn-oak`, `ordinary-wood-trim-weathered-woodland`, or the original `ordinary-wood-trim-albedo` from `Assets/Textures/Buildings/Wood`. This updates all building parts sharing that material. Use the matching command below to synchronize wattle and roof timber. Inspect the result in Scene view or Play mode; setup menus now preserve the selected wood texture.

The additional options in that same texture field are `ordinary-wood-trim-golden-oak` and `ordinary-wood-trim-meadow-timber`. Generating additional samples leaves the user's active material selection intact; this pass adds sibling assets rather than switching the shared material.

The building setup now preserves the active `SplitPlankWood` base map on rebuild. Wattle rebuilds use that same selected atlas, and roof rebuilds refresh only the timber regions inside the combined roof atlas. Use **Tools → Building → Match Wattle and Roof to Active Wood Texture** after choosing another wood texture to update both immediately and refresh the affected thumbnails. The operation verifies that wattle weave, roof weave/thatch and alpha masks remain unchanged. Meadow Timber is the selected atlas for the current matched materials.

Matched results: [wattle wall](matched-wattle-wall.png), [actual instanced roof underside](matched-roof-underside.png), and [pixel-preservation report](matched-wood-surfaces.json). The roof's 786,432 timber-colour texels were refreshed from the selected atlas; weave/thatch RGBA and the entire roof alpha mask were preserved.

## Texture swapping contract

The finished PNGs preserve the original **2048 x 2048** RGBA canvas and all nine A-I allocation and sampling rectangles in [the original atlas layout](../atlas-layout.json). Each region's generated artwork is packed into its existing content rectangle; the same 16-pixel gutters are extruded around it. A-D retain horizontal grain, E/F retain end grain, G retains lashing and H retains fine grain. No painted plank seams or joints are baked into A-C: the mesh provides those divisions, so arbitrary UV crops work.

The full bottom-right wattle cell **including its gutters and RGBA pixels is copied exactly from the existing texture**. Its appearance is unchanged in this pass. This preserves the original twig silhouettes, transparency, cutout coverage and pattern phase. Every alpha pixel in the whole atlas is identical to the current atlas.

A-D opposing U-edge pixels match exactly; a narrow symmetric seam blend is confined to 48 texels at the repeat ends. G/H have matching local U edges too. These are periodic strips, not a texture that should repeat vertically as a whole. Preserve `_BaseMap` scale `(1,1)` and offset `(0,0)`; use the existing UV0. Unity importer settings are copied from the current atlas into new metadata with unique GUIDs.

To try a sample on the current kit, swap the current wood material's base map to either finished PNG, or use its sibling material on a test copy. Wattle uses its own alpha-clipped material, so swap that material's base map rather than assigning the opaque sample material. The thatch roof has a separate combined atlas and is outside this texture swap's scope.

## Verification and limits

[Pixel checks](atlas-validation.json) passed for both exports: unchanged full alpha mask, unchanged wattle RGBA, solid wood and utility alpha, matching A-D horizontal edge pixels, fixed resolution and padding. Generated sources were 1254 x 1254; packing into a 2048 atlas does not create native 2048 detail.

The new [golden oak pixel checks](golden-oak-atlas-validation.json) and [meadow timber pixel checks](meadow-timber-atlas-validation.json) pass the same compatibility assertions. These sources are also 1254 x 1254, packed to 2048 with the existing sampling bounds and 16-pixel gutters. Wattle is retained from the original, not the generated source. Additional exact prompts are recorded in [ADDITIONAL_GENERATION_PROMPTS.md](ADDITIONAL_GENERATION_PROMPTS.md). Run `render_samples.py -- --additional-samples` through Blender to reproduce their separate comparison without rerunning the unchanged FBX audit.

[FBX UV audit](uv-validation.json) inspects 26 existing wood source exports, including the available stair extension pieces. Twenty-three meshes have every face within one documented content region, allowing unrestricted U Repeat on A-D. These include the current modular wall, wattle wall, all post variants, floor variants, rim and caps, doorway, shutter wall, gables, two-plank infill and stair extension pieces.

Three older source meshes have pre-existing exceptions: legacy bay post (6 faces), legacy split-plank wall (6), and the W21 half-story stair (16). The report records their exact UV bounds. An exception means a polygon does not fit one **padded content** rectangle after allowing full-width U Repeat; it can include gutters or more than one region. The samples preserve the existing allocations and do not repair these source UVs. Thus this is a confirmed direct atlas-layout swap, with a clean check for the current modular kit, rather than a claim that every older source UV was already correctly authored.

Unity runtime/import validation was not run. Preview lighting is uniform studio lighting. At very small mip levels the existing atlas can still mix adjacent regions; the samples preserve its padding contract.

## Reproduction

[Exact prompts](GENERATION_PROMPTS.md) and the two `*-source.png` artworks are retained. [Pack-Samples.ps1](Pack-Samples.ps1) packs the generated artwork, retains the wattle cell and asserts pixel compatibility using System.Drawing. Run it from PowerShell 7 with `-Source` and `-Output` absolute paths. [render_samples.py](render_samples.py) imports existing FBXs read-only, audits UV0 and renders the comparison using Blender 5.0; pass `-- --audit-only` to skip rendering. Neither script alters the original model files.
