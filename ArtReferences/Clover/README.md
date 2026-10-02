# Clover v02

Open [the preview gallery](PreviewGallery.html) for all rendered views.

Individual three-leaf heads are authored first, then combined into irregular
single-mesh clumps. Each leaflet has its own shallow cup and intact UV island.
The texture contains one leaflet, rather than many overlapping clovers baked
onto a large terrain card. This preserves silhouettes and gives individual
leaves real parallax, elevation and lighting.

The design follows the first supplied photograph's white-clover growth and
occasional white flower heads, with the second reference's readable game leaf
shapes. The shared texture has broad color shading and a softened pale V. Its
Unity import is limited to 256 pixels, with alpha-coverage mipmaps, clamp,
trilinear filtering and 4x anisotropy. No normal map is needed.

## Assets

| Prefab | Heads | White blooms | Triangles | Purpose |
|---|---:|---:|---:|---|
| CloverClump_v02_A | 34 | 0 | 1,122 | Main green clump |
| CloverClump_v02_B_Flowering | 38 | 3 | 1,812 | Occasional flowering clump |
| CloverClump_v02_Edge | 19 | 0 | 627 | Loose edges and gaps |
| CloverSingle_v02 | 1 | 0 | 33 | Hand placement |

Prefabs: `Assets/Prefabs/`. Material:
`Assets/Materials/M_Flowers/M_Clover/M_CloverLeaf_v02.mat`. Texture:
`Assets/Textures/Flowers/T_CloverLeaf_v02.png`. Shader:
`Assets/Shaders/S_Grass/CloverLeafInstancedLit.shader`.

The prefab root has a MeshFilter and MeshRenderer, identity transforms, one
submesh and one material. The runtime renderer extracts only the mesh/material;
it does not apply prefab child transforms. Baked Unity Y-up mesh assets therefore
come from the accompanying JSON authoring data. FBX files are editable exchange
copies; the `_Runtime.asset` meshes are the prefab's actual geometry.

## Placement and scale

Use the three clump prefabs in `CloverSettings.cloverClumpPrefabs`; retain the
single-head asset for hand placement. Selection is uniform across array entries,
so repeat A entries if flowering clumps should be less frequent. A reasonable
starting array is A, A, A, B_Flowering, Edge. The single fallback field can use A.

Current SmearScene uniform scales are .4-.6. Geometry is authored at twice the
physical source size, so the preview at .5 shows the intended size. A is about
0.81 x 0.64 m and 0.11 m high at .5. At .4-.6, its width is about 0.65-0.97 m.
`worldScale` affects placement coordinates; it does not scale clover geometry.
The existing grass influence radius .7 multiplied by .5 is .35 m, close to A's
footprint. Existing patch settings will yield loose groups; overlap clumps for
a continuous mat. The coverage previews deliberately overlap 28 clumps to show
the resulting edge and canopy, rather than reproduce the world's spawn density.

The original clover prefab and SmearScene assignments are retained for visual
review. These are new v02 assets, ready to assign after choosing the design.

## Rendering and cost

The shader uses the texture's RGB and alpha, per-head vertex tint, instanced
`_CloverInstanceData` tone variation, two-sided lighting, main-light shadows,
depth/depth-normal passes, fog and the original 42-58 m dithered distance fade.
Opaque stems and white florets share the material using vertex alpha 0; leaflets
use vertex alpha 1. This is intentional material metadata, not vertex opacity.

The standalone prefabs and preview renderers cast shadows. The existing world's
`DrawClover` instanced draw explicitly uses `ShadowCastingMode.Off`; using these
prefabs there retains that runtime policy. The preview's clover shadows therefore
demonstrate the asset's capability rather than the current world draw settings.

The old clump contains 26 triangles spread across broad alpha cards. The new
clumps have more geometry and smaller transparent regions. Each remains one
instanced mesh/material, but the vertex/fragment tradeoff needs profiling in the
actual scene. No FPS improvement or live-world appearance is established here.
The existing clover renderer does not select LODGroup children, so these prefabs
do not contain a nonfunctional LODGroup.

## Rebuild and previews

1. Run Blender with `--background --python ArtReferences/Clover/GenerateClover.py`.
2. In Unity, run **Tools > Foliage > Clover v02 > Build and Render Previews**.

The Blender script regenerates deterministic geometry, FBX, JSON, a `.blend`
source, mesh statistics and two Cycles renders. Unity builds serialized meshes,
material and prefabs, validates geometry/UVs/instancing, then renders seven URP
views in an isolated preview scene without opening the world or entering Play.
Shader warmup and green-pixel checks prevent shadow-only captures. All previews
are actual asset renders. The woodland view uses the project's existing leaf
litter texture on a temporary plane; no environment material is modified.

## Texture provenance

Generated with the built-in imagegen tool, then simplified in one imagegen edit.
The selected output was copied into the project. Runtime import reduces it to
256 pixels; the source PNG is retained at generated resolution.

Initial prompt: one transparent, top-down white-clover leaflet with a rounded
obovate silhouette, shallow tip notch, pale V, gentle meadow-green albedo and
generous transparent padding; no stem, extra leaves, environment, shadows or text.

Final edit prompt: preserve the same silhouette, location, padding and alpha;
remove fine veins, speckle and photo noise; retain a faint central vein, soften
the V, and use a few broad, matte, natural meadow-green tones.

The complete prompt text is recorded in [TexturePrompts.md](TexturePrompts.md).

## Verification

Unity 6000.4.8f1 compiled the editor authoring code and shader, validated all four
prefabs, and rendered seven URP previews successfully. Visual checks covered all
views, including the individual clover and the final woodland view. Blender 5.0.1
regenerated the editable source and both Cycles previews. Log:
`.utmp/clover-validation/unity-final.log`. No live world test or performance
benchmark was run. Source and prefab assignments in the existing scene are
unchanged by this asset authoring workflow.
