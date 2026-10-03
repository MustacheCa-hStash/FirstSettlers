# Terrain base material inspector cleanup

Scope: `Assets/Materials/M_Terrain/M_TerrainBase.mat` and its `Custom/StylizedTerrainURP` shader inspector. Other material shaders are unchanged.

## Organization and inactive controls

The custom TerrainMaterialInspector groups all 128 current shader properties by surface and shared function. Every current property has a consumer, including the two floats that select shader keywords; working fallback modes are retained. Unlisted future public properties still appear automatically.

- Matching Blade Ground and Traditional Grass Textures show which mode consumes each control. Traditional grass albedo/normal/tiling settings are inactive while matching blade ground is selected. Grass Normal Strength is shared by both modes.
- Rock/Cliff Detail controls are inactive when that feature is off; Rock Color and Cliff Color remain editable because the shader still uses them.
- Optional AO/height strengths become editable when the corresponding map is assigned. Map slots remain editable so the feature can be configured.
- Normal/parallax fade controls explain when their corresponding strengths are zero. Other zero-strength dependencies cover tone contrast, forest macro frequency and distance-noise frequency.
- Generated control maps and horizon-shadow inputs appear read-only in the collapsed Preview and Runtime Inputs section. They are still shader properties for runtime bindings.
- Preview Receive Shadows remains a working standalone-material control. World Manager overrides it on generated near/far terrain. Removed the generic Toggle drawer that generated an unused `_RECEIVESHADOWS_ON` keyword; the inspector edits the actual shader float directly.
- RGB color pickers omit unused alpha controls and preserve each material's stored alpha when editing colors.
- Labels explain grass noise bias, shared normal/tiling settings, moss texture/fill blending, distance fallback color, and height-map sampling offsets. Height maps do not displace the mesh.

Generated chunks copy the base material. Use World Manager's Regenerate Terrain button or restart Play mode to apply base-material edits to loaded chunks.

## Tiling that actually works

Higher world tiling values produce smaller texture tiles. Frequencies are repeats per world unit.

| Surface | Shared frequency controls | Per-map Tiling / Offset |
| --- | --- | --- |
| Rock / cliff | Rock / Cliff Tiling | Unsupported; hidden for albedo and normal. Triplanar projections share the same frequency. |
| Matching blade ground | Blade Ground Near/Far Repeats Per Meter | Unsupported; hidden on the packed surface map. |
| Traditional grass | Grass Tiling Near/Far | Unsupported; hidden on albedo and normal. |
| Snow | Snow Tiling Near/Far | Unsupported; hidden on albedo and normal. |
| Sand | Sand Tiling | Unsupported; hidden on albedo and normal. |
| Bare dirt | Bare Dirt Tiling | Unsupported; hidden on all maps. |
| Ordinary moss | Moss / Dense Moss World Tiling | Unsupported; hidden on ordinary moss maps. |
| Leaf litter | Litter / Mixed Floor World Tiling | Supported on albedo, normal, AO and height. |
| Mixed forest floor | Litter / Mixed Floor World Tiling | Supported on albedo, normal and height. |
| Dense moss | Moss / Dense Moss World Tiling | Supported on albedo, AO and height. |

The ten supported map transforms appear in per-map foldouts. Their Tiling multiplies the shared world frequency, and their Offset shifts that map independently. Existing values are preserved. `[NoScaleOffset]` flags also prevent ignored transforms from appearing in Unity's fallback shader inspector.

Mixed forest floor shares litter normal strength/fades. Dense moss shares ordinary moss's world frequency, fill/saturation and macro variation. Their distribution comes from generated control maps, not material sliders.

## Obsolete saved data removed

Removed 54 saved entries absent from the current shader: former URP/Lit texture, lighting, blending, transparency and color properties, old `_GrassTiling` / `_SnowTiling`, and retired forest-grass properties. Removed the unused saved shadow keyword. Unity-managed lightmap texture entries and all current shader properties remain.

This does not change the rendering program: the HLSL program and shader variants are byte-for-byte unchanged. No extra runtime texture samples or rendering features are introduced.

## Validation

`Assets/Editor/TerrainMaterialInspectorValidation.cs` exposes **Tools > Terrain > Validate Terrain Material Inspector**. Batch validation runs in the disposable Unity project and additionally compares against a pre-cleanup material fixture and runs the existing cliff rendering validation.

Unity 6000.4.8f1 validation passed on 2026-10-03: 128 properties grouped once, ten supported map transforms retained, and runtime diagnostics, feature keyword synchronization, optional maps, zero-strength dependencies and mixed selections checked. Preservation checks passed for all live shader values, texture references, texture transforms, feature keywords and render queue against the original material. Shader/render checks passed for 48 Direct3D11 variants spanning rock/grass modes, Forward/Forward+, fog and shadows, plus offscreen cliff previews and distance fallback independence. Surviving saved YAML entries are unchanged apart from line endings, the HLSL program is unchanged, and `git diff --check` passed. The final inspector compiles using current Unity APIs without compiler warnings. Live inspector appearance remains for user review.
