# Distant noisy fog

SmearScene has a **Distant Fog Layer** object. Its `DistantFogLayer` component owns
the settings. The **Distant Noisy Fog** feature on `Assets/Settings/PC_Renderer.asset`
draws it using URP 17.4 RenderGraph. Other scenes without an enabled layer do not
receive the effect. No terrain/foliage culling or streaming settings are changed.

## Initial appearance

- Fog starts 600 world units from the camera, feathering in over 200 units.
- The layer occupies world Y -45 to 85, with a 25-unit feather and up to 20 units
  of noisy top-edge variation. These heights fit SmearScene's water Y 14.4 and
  leave its taller mountains above the bank; they are starting values to tune.
- Visible surfaces fade out of the effect between Y 110 and 150. Above Y 150,
  they stay clear even when the view ray crosses the fog. This is deliberate
  artistic control; the upper sky is controlled by the fog volume instead.
- Fog color follows `RenderSettings.fogColor`, which SunCycleController updates.
  Turning off **Follow Day Night Color** exposes an independent fixed color.
- Standard Unity distance fog is disabled in SmearScene to prevent it from
  independently obscuring the protected peaks. The sun-cycle color still updates.

Start with **Top Height**, **Height Feather**, **Billow Height**, **Density**, and
**Noise Tile Size**. A larger tile size gives broader clouds. Noise stays in
world space and drifts with **Wind**; it does not slide along with the camera.
Disabling the component bypasses the effect for comparisons.

Distances use Unity world units, matching **World Pos** in the PCG debugger.
The start distance is the straight-line distance from the camera to each visible
surface, not an absolute X/Z coordinate or a count of terrain-generation samples.
WorldManager's `worldScale` scales generated geometry before placement; it does
not apply another conversion to these fog settings.

## Rendering and quality

Four profiler samples identify the costs:

1. `FS.Fog.SceneDepth`: fresh copy of the completed scene depth attachment.
   URP's SSAO depth/normal prepass omits some custom shaders, so its depth texture
   cannot reliably identify foreground terrain or indirect foliage for this fog.
   This copy includes their normal depth writes without requiring new shader passes.
2. `FS.Fog.WaterCoverage`: R8 coverage of actual visible water meshes.
3. `FS.Fog.Raymarch`: height-bounded, noise-texture raymarch, default half width
   and height with 32 steps. Empty air and foreground are skipped, and sampling
   stops when fog is effectively opaque.
4. `FS.Fog.Composite`: depth/surface-aware upsampling and hardware blending.
   It avoids copying the full-resolution scene color. Rare silhouette mismatches
   use a full-resolution raymarch rather than leaking fog over a foreground edge.

**Resolution** supports full, half and quarter dimensions. **Steps** supports
8–96. A 4K/240 FPS target has only 4.17 ms for the whole frame: use the GPU Profiler
in a representative player build to judge the fog cost. Editor correctness
captures and CPU time spent submitting render requests are not FPS benchmarks.

One generated-world camera viewpoint measured on an RTX 5090 / Direct3D11 at
3840x2160, half-resolution fog and 32 steps, produced a median sum of **0.347 ms**
and P90 **0.465 ms** across 59 editor GPU samples. Mean finished-depth copy, water
coverage, raymarch and composite costs were 0.108, 0.027, 0.081 and 0.110 ms. This excludes
scene rendering, reflection updates, CPU work and total game frame time. It does
not establish 240 FPS gameplay performance or a worst-case fog budget.

The two noise octaves are baked into one repeatable 64-cubed RGBA texture,
`Assets/Settings/DistantFogNoise.asset`. Both channels are read in a single sample
per step. No per-frame texture generation, temporal history, shadow-map sampling,
additional-light scattering, or third-party assets are required. Stable spatial
dithering helps hide bands; it can still reveal grain or thin-edge aliasing at low
quality, so check camera movement as well as still images.

## Sky and water

Sky pixels use the view ray intersected with the finite-height fog layer, rather
than a fictitious surface at the camera far plane. Fog fills horizon gaps while
upward rays outside the layer remain clear.

The river shader has an inexpensive `DistantFogWaterMask` pass. Only actual water
coverage visible through the opaque depth buffer is marked. On those pixels, the
fog uses ray/plane intersection with `WorldManager.GlobalWaterHeight` instead of
the underwater terrain depth. This does not change the water's normal rendering,
refraction, or depth writing. The fog composites after transparents, covering the
finished water color and reflections. Planar reflection cameras in the same scene
also use the feature; their existing 300-unit geometry range remains unchanged.

Other transparent materials that do not write depth use the opaque depth behind
them. They are not covered by a general transparent-depth solution. The shared
water-plane path specifically addresses the project's river/lake shader.

## Verification

`Tools > Atmosphere > Validate Distant Fog Rendering` runs GPU assertions through
the real feature and writes synthetic captures under `.utmp/distant-fog`.
It checks SmearScene's actual terrain material, rock material, and grass shader
near and far, along with the 599/622-unit cutoff, high surfaces, mixed-height
objects, sky, water-plane protection, quality settings and color changes.
`DistantFogValidation.InstallAndValidateBatch`
installs the scene assets first and exits with failure if an assertion fails.
`DistantFogSceneCapture.RunBatch` additionally enters Play mode, captures the
generated SmearScene at 4K with fog on/off, and samples isolated fog GPU timings.
Its temporary camera/profiler settings are restored, and Play-mode edits are not
saved to the scene. Timing results are written to `.utmp/distant-fog/gpu-timings.txt`.

The install menu explicitly saves SmearScene and the PC renderer. It is intended
for first setup or reinstalling the feature, not for resetting artist-tuned values.
