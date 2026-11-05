# Dry basin clay and wash gravel

Two original albedos were created with the **built-in ImageGen tool** for the
FirstSettlers desert terrain. Their muted pale buff, clay-taupe and sandstone
colors follow the existing painted rock and sandy-ground references. Clay uses
quiet irregular plates and narrow shrinkage cracks; wash gravel uses small
embedded chips and subdued sandy silt. Both avoid strong baked lighting, wet gloss
and dark graphic outlines.

## Sources and exact prompts

- [Cracked clay source](CrackedClay_Source.png)
- [Dry wash gravel source](DryWash_Source.png)
- [Complete generation prompt set](Prompts.json)

The selected albedos in `Assets` are byte-for-byte copies of these source images;
their color artwork has not been repainted or procedurally replaced. The original
generated copies also remain in the Codex image library.

| Asset | Saved project path |
| --- | --- |
| Clay color | [T_CrackedClay_Albedo.png](../../Assets/Textures/Ground/Desert/T_CrackedClay_Albedo.png) |
| Clay normal | [T_CrackedClay_NormalGL.png](../../Assets/Textures/Ground/Desert/T_CrackedClay_NormalGL.png) |
| Clay relief authoring export | [T_CrackedClay_Height.png](../../Assets/Textures/Ground/Desert/T_CrackedClay_Height.png) |
| Wash color | [T_DryWash_Albedo.png](../../Assets/Textures/Ground/Desert/T_DryWash_Albedo.png) |
| Wash normal | [T_DryWash_NormalGL.png](../../Assets/Textures/Ground/Desert/T_DryWash_NormalGL.png) |
| Wash relief authoring export | [T_DryWash_Height.png](../../Assets/Textures/Ground/Desert/T_DryWash_Height.png) |

Source/albedo SHA-256:

```text
CrackedClay: 8d37e37c0cf4aa11c30e64c606e221f43a90c9055e9c6f507b44dc0badf45c66
DryWash:     b5c575b39d78519e01cda5774de0fd6f868038f7ac65d911ea51e173a90930d2
```

## Import and technical bake

`Assets/Editor/DesertSurfaceSetup.cs` performs the initial technical bake directly
in Unity. Use **Tools → Terrain → Install Dry Desert Surfaces**. It imports the
albedo as an explicit 2D texture, estimates shallow relief from local luminance
contrast, and creates aligned OpenGL-style normal and height PNGs when the
derived files are missing. This relief is estimated artwork, not scanned geometry.
The color image remains untouched.

Albedo and normals import at 1024 square with mipmaps, repeat wrapping and 4x
anisotropy. Albedo is sRGB BC7; normal maps are linear BC5 with green flip off.
Height exports are linear authoring assets and are not sampled by the runtime
terrain shader. The four bound textures occupy approximately 5.33 MiB including
mips in their desktop formats.

The terrain material binds both families under **Dry Desert Sediments**. Clay
tiling defaults to 0.75 and wash tiling to 0.80; normal strengths are 0.28 and
0.22. Material tint starts white so the generated palettes remain visible.

## Repetition and distance

The shader uses four overlapping texture windows with smooth weights. Each
window fades out before reaching a source-image edge; this makes the projected
material continuous across repeating cells without altering the source pixels.
Explicit texture gradients keep mip selection valid across those windows. Normal
and albedo use the existing sand repeat sampler, adding no sampler registers.

Normal detail fades out by 65 m. Albedo detail fades from 70 to 220 m into a
linear-light texture average, with subtle world-space mineral variation remaining
at distance. A single active material family uses up to four albedo and four
normal reads nearby; distant detail bypasses those reads. Blended boundaries can
evaluate both families. No extra terrain control-map textures are added.

`DryWash` and `CrackedClay` are explicit gameplay surface labels. In desert
provinces they share the existing mud and riverbed control channels, respectively;
the shader interprets those channels as dry sediments using the same regional
field. Outside the regional transition the existing wet-ground appearance remains.

## Native previews and checks

- [Clay close-up](../../Documentation/Images/WorldLandforms/CrackedClay_Close.png)
- [Gravel close-up](../../Documentation/Images/WorldLandforms/DryWash_Close.png)
- [Actual dry wash terrain](../../Documentation/Images/WorldLandforms/DryWashes.png)
- [Dry basin terrain](../../Documentation/Images/WorldLandforms/DryBasin.png)
- [Generation, water policy and validation](../../Documentation/WORLD_LANDFORMS.md)

`Tools/ValidateWorldLandforms.ps1` bakes and checks an isolated project before
copying the requested texture assets/import settings and material bindings back
to the workspace. It checks imports, native rendering, detailed/distant material
agreement, terrain determinism and genuinely empty desert water meshes. Preview
images are isolated patches and material studies, not full-scene frame-time tests.
