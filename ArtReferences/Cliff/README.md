# Fractured cliff texture

Original natural grey/taupe stone with broad vertical slab fractures, inspired by the supplied cliff screenshots. The restrained mineral detail fits the photographic forest-floor layers and natural skybox. Built-in imagegen was used for generation and central-cross seam repair; no external image API was used.

## Files and setup

- `Assets/Textures/Rock/T_CliffFractured_Albedo.png`: 1024 square, sRGB, BC7.
- `Assets/Textures/Rock/T_CliffFractured_NormalGL.png`: aligned +Y tangent normal, linear NormalMap import, BC5, green flip off.
- `Assets/Textures/Rock/T_CliffFractured_Height.png`: linear BC4 authoring export. Height is estimated from crevices/local luminance, not measured geometry. It is used to bake the normal and is not sampled at runtime.

The terrain material `Assets/Materials/M_Terrain/M_TerrainBase.mat`, referenced by SmearScene and cloned for near/far chunks, uses the new albedo and normal through the existing `_ROCK_DETAIL` path. Existing source rock textures remain available. Restart Play mode so runtime material clones pick up the new maps/settings.

`Rock Tiling` is repeats per world unit (0.12 = 8.33 m per tile); the texture-slot scale/offset are not used by this world-space projection. Texture strength is 1, rock normal strength 0.45, cliff normal strength 0.65. Both side projections keep vertical fractures upright. Signed projection frames rotate normal slopes consistently on positive and negative faces. World coordinates keep projection placement consistent across chunks. The normals affect lighting; they do not create protruding geometry or change the cliff silhouette.

Normal sampling fades between 35 and 110 m. Albedo detail fades between 100 and 450 m. At `Rock / Cliff Texture Strength = 1`, nearby rock uses the sampled albedo directly, without rock/cliff tint multiplication or flat-color dilution. Lighting, shadowing and fog still apply normally. During the distance fade it blends to the previous fallback color, which is preserved exactly:

`distantColor = rockOrCliffColor * lerp(white, averageAlbedo, distantAverageColorStrength)`

`Rock Color (Distance Fallback)` and `Cliff Color (Distance Fallback)` are (0.65, 0.64, 0.62). `Rock / Cliff Distant Average Color Strength` remains 0.65. These values affect the distant fallback only; texture strength is independent. Reducing texture strength blends the distant color into the nearby texture. If replacing the albedo, update `Rock / Cliff Average Albedo` to its average color (sRGB encoded linear mean). Turning off `Enable Rock / Cliff Detail` restores the color-only path.

## Seam repair

The source is offset by half its width and height (four-quadrant swap). Imagegen repairs the central seam cross. `BakeCliff.py` composites that repair only around the cross, preserving the original continuous outer perimeter, then eases residual edge error over a narrow border. Height smoothing and normal derivatives wrap periodically. All three exported PNGs have identical opposite edge pixels. The 3x3 repeat preview provides a visual check; identical edge pixels alone do not guarantee invisible content discontinuities. Trilinear filtering, repeat wrapping and anisotropy 4 are configured. Block compression/mip filtering can introduce small deviations, so the Unity preview is also relevant.

Rebake with Pillow and numpy:

```powershell
python ArtReferences/Cliff/BakeCliff.py
```

It preserves asset GUIDs. `BakeReport.json` records edge errors, normal convention and average color.

## Runtime cost

The previous rock path always sampled three albedos and three normals when active. This path eases negligible projection weights to zero and uses explicit UV gradients so branching does not invalidate mip selection:

| Visible rock pixel | Texture reads in rock path |
| --- | --- |
| Axis-aligned near face | 1 albedo + 1 normal |
| Near face blending two projections | 2 albedos + 2 normals |
| All three projections contribute | 3 albedos + 3 normals maximum |
| Beyond normal fade | 1–3 albedo reads |
| Beyond albedo fade / no rock coverage / keyword disabled | 0 |

These are source-level counts, not measured frame-time savings. Branch divergence can make actual GPU savings smaller, and branches/gradients add arithmetic. No extra sampler registers, runtime height fetches, parallax iterations, tessellation or displacement are added. BC7+BC5 1024 maps occupy approximately 2.67 MiB including mipmaps; the unbound height map does not need runtime residency. A live-world GPU benchmark would be required to quantify FPS. Normal detail and triplanar albedo were already enabled before this change, so the new texture introduces no increase in worst-case rock fetch count.

## Verification

Use **Tools > Terrain > Validate and Preview Cliff Texture** in Edit mode. The utility checks imports and material assignments, warms 48 terrain shader variants covering rock/grass keywords, Forward/Forward+, cascade/screen shadows and fog, and renders actual terrain material captures on positive/negative faces and at distance without editing the open scene. The batch entry point is `CliffTextureValidation.RunBatch`.

Executed successfully in the isolated `.utmp/grass-ground-validation` project with Unity 6000.4.8f1, Linear color space and Direct3D11. Initial log: `.utmp/grass-ground-validation/cliff-final-validation.log`; untinted-color follow-up: `.utmp/grass-ground-validation/cliff-untinted-validation.log`. The captures and `UnityValidationReport.json` are saved here. The normal-versus-albedo-only comparison confirms that the generated normal changes rendered lighting. The distant render is checked for exact pixel equality after changing nearby texture strength and replacing the albedo. PNG opposite-edge equality passed for all three maps; repeat and Unity captures were visually reviewed. These are isolated material fixtures, not a live-world performance test or a full terrain-generation regression.

## Exact generation prompt

Use case: photorealistic-natural. Asset type: square seamless tileable PBR base-color texture for Unity terrain rock and vertical cliff faces, 2048x2048. Primary request: an original natural grey cliff rock surface with the broad long vertical fractures and large irregular angular slab faces seen in Fortnite cliffs, but with more realistic subdued weathered stone detail to fit photographic forest-floor textures and a natural HDRI skybox. Only the flat texture fills the image edge to edge, straight-on orthographic view. Large elongated irregular slabs running predominantly vertically, occasional short diagonal or horizontal fractures interrupting them, shallow chipped bevels, broad quiet rock faces with subtle mineral grain. Natural cool-neutral slate grey and slightly warm grey/taupe variation, moderate contrast, diffuse even illumination, no directional light, no strong baked shadows, no glossy highlights. Avoid masonry, regular bricks, loose pebbles, scattered boulders, rounded cobblestones, cartoon outlines, deep black cracks, grass, moss, sky, perspective, labels, border, text. Seamless periodic continuity across all four edges, use wrapped composition so vertical fractures and mineral detail continue across opposite boundaries. Fracture detail should come from surface pattern; do not depict a freestanding cliff or landscape. Useful as a neutral grey albedo multiplied by terrain tint. Large forms occupy about one quarter to one eighth of tile width, with varied length. Fine realistic surface texture is restrained.

## Exact offset seam-repair prompt

Edit target: supplied offset cliff texture. It has been shifted half a tile in both directions so its old opposite edges now form an artificial straight central horizontal and vertical cross seam. Repair that central cross: reconnect the irregular stone slabs, fractured edges, mineral grain and colors naturally through the middle. Keep the surrounding texture, grey/taupe palette, realistic yet broad slab forms and vertical geological orientation unchanged. Keep the outermost borders exactly unchanged; this is a technical seam cleanup for a repeating game texture. Do not add light sources, shadows, vegetation, masonry, text or new composition. Return the entire square texture edge to edge. The central cross must disappear into continuous natural rock.
