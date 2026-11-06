# Water waves and flow

Near water now uses actual GPU Gerstner displacement, matching analytic normals, smaller advected normal ripples, and a direct sun highlight. CPU vertices remain at the mean water level. The shader moves them vertically and horizontally each draw; it does not rebuild meshes every frame.

## Tuning

Controls live on Assets/Materials/WaterMeshBaseMaterial.mat. Shader defaults match this material.

| Control | Default | Effect |
| --- | --- | --- |
| Maximum combined wave height | 0.16 metres | Sum of amplitudes from the mean plane: a theoretical -16 to +16 cm envelope. Clamped to 30 cm to match mesh bounds. Zero disables geometric waves. |
| Gerstner crest steepness | 0.6 | Horizontal motion relative to vertical amplitude. Zero gives vertical-only sine waves. |
| Geometric wavelength scale | 1 | Scales wavelengths of 7.5, 5.2, 4.1, and 10.8 metres, with amplitude weights 0.40, 0.30, 0.18, and 0.12. |
| Geometric wave speed | 1 | Multiplies wave phase time. Phase velocity uses gravity and wavelength plus the current. Zero freezes geometric waves. |
| Full geometric waves distance | 60 metres | Start of the smooth geometric fade, measured horizontally from the view camera. |
| Flat water distance | 120 metres | End of displacement; the streaming owner may shorten this. |
| Sun highlight strength / sharpness | 0.65 / 160 | Main directional light highlight, driven by the combined geometric and small-ripple normal. |
| Current direction (world XZ) | (0.8, 0.6) | Vector X/Y fields represent world X/Z. Normalized; world +X is the zero-vector fallback. Geometric waves spread around this direction. |
| Current speed | 0.9 world units/second | Advects small ripples downstream and contributes to geometric phase velocity. |
| Ripple tiling / small ripple strength | 0.08 / 0.65 | Scale and finer detail of normal ripples. |
| Surface normal movement | 0.34 | Strength of texture normals, independent of geometric amplitude. |
| Reflection / refraction distortion | 0.04 / 0.014 | Additional small-wave distortion in camera screen axes. |

To freeze the entire surface, set both current speed and geometric wave speed to zero. For a geometry-only review, set surface normal movement to zero while retaining geometric amplitude and sun highlights.

## Mesh and streaming

WaterMeshGenerator.GenerateWaveMesh creates shared grid vertices only in wet cells, using padded native terrain heights and water states. Grid spacing is at most 0.6 metres. Topology and shore samples are independent of terrain LOD, so adjacent detailed chunks have matching edge vertices and wave data. Each vertex carries depth and world-space X/Z depth gradients in UV channel 1. Larger world scales subdivide source cells; smaller scales use an integer source-grid stride. Partial last cells are supported.

Terrain LODs 0-2 (rings 0-5) use this grid; LOD 3 onward and macro tiles retain greedy flat water. For the scene's 128-sample chunk at world scale 0.3, a fully wet detailed chunk has **4,225 vertices and 8,192 triangles**. Partially wet chunks allocate their occupied grid region. Dry chunks avoid full-grid vertex allocation.

WorldManager limits waves to a conservative radius inside the first flat representation: (min(firstMacroRing, 6) - 1) * chunkWorldSize - 1.2 metres, bounded above by the material fade end. With macro tiles disabled, ring 6 is the boundary. Horizontal camera distance also gives the mirrored reflection camera the same envelope. Waves reach exactly zero before detailed/flat boundaries, including small-chunk configurations, without edge skirts or periodic flat strips at detailed chunk seams.

Mesh bounds expand by 0.3 metres on every axis to cover the maximum allowed displacement. Existing water visibility and reflection culling consume those expanded renderer bounds.

## Shore, reflection, and fog

Displacement is zero at terrain depths up to 8 cm and reaches full amplitude at 90 cm. Both the shore and distance fades contribute derivatives to the wave normal, keeping highlights consistent with the damped shape. Small normal detail fades between 35 and 120 units of eye depth; the combined texture normal retains its 70-200-unit fade.

The reflection camera mirrors around the mean plane. Sampling projects the displaced point through that mirror using a screen-UV correction that becomes exactly zero on flat water. This remains a small-wave approximation. Additional distortion fades near shore; refraction rejects above-surface scene samples. Tint depth is reconstructed from actual bed and displaced surface positions.

Color and fog passes call the same EvaluateWaterWaves function in WaterWaves.hlsl and share one material constant-buffer layout. The fog water buffer stores actual positive linear eye depth in R32 float, cleared to zero outside water. Fog reconstructs water distance and height from this depth, supporting perspective, orthographic, and mirrored views. This replaces flat-plane inference. The buffer uses four bytes per pixel instead of one, approximately 25 MB extra at 3840 x 2160, excluding driver overhead.

## Validation

Run powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tools/ValidateWaterFlow.ps1 from the repository root. It prepares an isolated project under .utmp/water-flow, uses the installed Unity editor and cached packages, and writes results to Documentation/Images/WaterFlow. It does not open or save the gameplay scene.

The sloping-bank fixture includes a checked bed, trees, a sun light, and an intersecting post. Timed variants inline the production wave include and substitute only the clock. A diagnostic variant retains the production vertex function but outputs interpolated geometric height instead of shading. The actual shipping shader is separately compiled and rendered.

The GPU diagnostic measured troughs of **-0.15564 m** and crests of **+0.15869 m**. Zero amplitude restores flat geometry. Other checks cover frozen animation, matching split-mesh output, distant stability, zero direction, reflection fallback, and geometric animation without a normal texture. CPU checks cover shared vertices, spacing, winding, bounds, padded depths/gradients, chunk edges, dry chunks, and small-pond coverage. Existing water coverage/classification, reflection visibility, and distant-fog GPU suites also run.

See the [animated preview](Images/WaterFlow/WaterFlow.gif), [before](Images/WaterFlow/Near-Before-t0.png), [after](Images/WaterFlow/Near-After-t0.png), and [validation report](Images/WaterFlow/validation.txt). The 24-frame preview samples four seconds at six frames per second. Validation uses Unity 6000.4.8f1 / D3D11 / NVIDIA GeForce RTX 5090. These are synthetic correctness and visual checks, not gameplay performance measurements.

## Limits

This is visual water motion, not a fluid or buoyancy simulation. Gameplay water level remains the mean plane. Currents do not yet follow river bends, and there are no interaction wakes. Shore data comes from the retained terrain heightfield; live sculpt edits do not refresh it independently. For an already-running world, restart Play mode to regenerate cached water meshes and cloned materials.
