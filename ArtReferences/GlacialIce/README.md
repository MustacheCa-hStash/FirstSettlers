# Glacier ice material

Created with the built-in imagegen tool for FirstSettlers' muted, painterly terrain
style. The chosen artwork is opaque pale blue-grey ice with milky inclusions,
subtle irregular veins and restrained contrast. Existing snow and stone artwork
are reused alongside it.

* [Selected source](GlacierIce_Source.png)
* [Exact prompt and generation tool](Prompts.json)
* [Runtime albedo](../../Assets/Textures/Ground/Glacial/T_GlacierIce_Albedo.png)
* [Estimated OpenGL normal](../../Assets/Textures/Ground/Glacial/T_GlacierIce_NormalGL.png)
* [Authoring height](../../Assets/Textures/Ground/Glacial/T_GlacierIce_Height.png)

The selected source and project albedo are byte-identical. SHA-256:
`a007e04fa12628bfac7c283af43043f8a0c5c59f9f7f6c3896a4f530cace9849`.
The original generated image is retained at
`C:/Users/samee/.codex/generated_images/01a1a960-327c-7520-bc84-fa768e479cdf/exec-917fb7ef-cfce-4e7e-8be6-b82a1febf863.png`.

`GlacialSurfaceSetup` uses the existing Unity-native technical bake in
`DesertSurfaceSetup`: local luminance relief, wrap-aware smoothing and gradient
normals. These maps estimate fine material relief rather than physical ice
thickness; the original albedo is not edited. No Python or CLI image-generation
fallback was used.

Albedo imports as sRGB BC7, normal as linear BC5, both repeat/trilinear with
mipmaps and a 1024 maximum. The height map is authoring-only. The bound pair costs
approximately 2.67 MiB including mipmaps. The terrain material binds these maps
in its Glacier Ice section; the installation menu is Tools / Terrain / Install
Glacier Ice. Subsequent installs preserve authored scalar tuning.

The shader's overlapping interior texture windows remove source-edge seams
without changing source pixels. Triplanar projection supports floors and ice
fronts, with normals fading before albedo detail and a matching distant average.
The ice remains an opaque terrain surface with canonical collision.

The November 5 cliff revision keeps these source pixels and technical maps. Its
slightly deeper blue comes from terrain-material tint `(0.84, 0.92, 1)`, applied
to both detailed ice and its distant average. First installs use that tint;
subsequent installs preserve authored material tuning.

See [the landform guide and native previews](../../Documentation/GLACIAL_LANDFORMS.md).
