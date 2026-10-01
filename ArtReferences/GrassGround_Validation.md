# Matching grass ground validation

Validated on October 1, 2026 in Unity 6000.4.8f1, URP 17.4.0, Direct3D 11.
Command-line Unity ran an isolated copy of the project assets and PC render
pipeline. No computer use or Play mode was used.

## Fix

The new ground layer exceeded D3D11's 16 sampler-register limit when rock detail
and shadows were enabled together. Control maps now share a clamp sampler, and
each repeating layer's normal map shares its albedo sampler. Sand has its own
albedo/normal sampler so it does not depend on the old grass texture, which is
compiled out when matching ground is enabled. The terrain declares nine sampler
states, with room remaining for the render pipeline's shadow samplers.

## Checks passed

The average-tint revision adds Blade Ground Average Tint, set to sRGB
(0.90, 0.90, 0.78). It darkens grass terrain and reduces blue slightly to match
the darker, warmer grass/clutter appearance. Sampling bands around the user's
marked line gave median linear luminances of 0.3962 above and 0.3181 below
(a ratio of 0.803). These are a visual reference, not a calibrated lighting
measurement. The same close-up preview before/after this change measured mean
linear luminances of 0.13080 and 0.10253 (a ratio of 0.784, about 22% darker).

The tint multiplies grass color after palette selection, before other
forest-floor layers. It applies uniformly at every camera distance, including
beyond the texture cutoff, and adds no texture fetches or sampler states.
All shader/range checks below were rerun, and close, distant and live-blade
renders were reviewed. Contrast, tiling and distance fade settings were retained.

The distance revision preserves the user's contrast of 3 and tone strength of
0.76. Texture detail now stays fully active through 180 m and fades out by
400 m, replacing the former 30-70 m fade. From 30 to 100 m, it crossfades between
two fixed world-space patterns: 0.45 repeats/m nearby and 0.06 repeats/m farther
away (7.5 times larger shapes). Explicit derivatives use each pattern's scale,
preserving mip filtering without camera-dependent UV sliding. The periodic
map, randomized tile blending and sampler states are unchanged.

The normal and height cutoffs remain 30 m and 12 m. Outside the scale transition,
the ground uses three texture fetches, or six while close parallax is active.
The 30-100 m transition uses six fetches; distant terrain uses only the three
coarse samples, then skips the map entirely beyond the detail cutoff. This
extends the area doing texture work; no gameplay frame-time benchmark was run.
All checks below were rerun for this revision, and the previews were refreshed.

- Compiled and warmed 256 terrain variants: matching ground on/off, rock on/off,
  all four fog modes, all four main-light shadow modes, and supported soft-shadow
  qualities. No terrain shader compilation errors.
- Rendered close-up, wide-area/two-adjacent-mesh, 360 m sloped terrain, and live-grass compatibility
  previews. Automatic checks reject blank renders and shader-error magenta.
- At roughly 140 m, swapping the map between extreme dark/light values changed
  the rendered pixels, confirming the texture is active beyond the old cutoff.
- At the same distance, changing far tiling changed the rendered pixels, while
  changing near tiling produced identical images: only the far pattern is used.
- Beyond the 400 m detail cutoff, swapping the packed map between extreme dark
  and light values produced identical rendered images.
- Beyond the 12 m height cutoff, parallax depths of 0 and 0.02 m produced
  identical rendered images.
- Beyond the 30 m normal cutoff, normal strengths of 0 and 1 produced identical
  rendered images.
- Visually inspected close, wide and distant previews: no magenta, visible chunk seam, or
  obvious repeating tile grid in the rendered patch.

Runtime surface map: 1024 x 1024, linear BC7, 11 mip levels. Unity's Editor
runtime-memory measurement was 2,797,160 bytes; this is not a standalone GPU
memory or gameplay frame-time measurement.

The distance fades use camera distance. Existing forest-floor texture and
strength settings were preserved. The Edit-mode validation menu now validates
current material settings without applying authoring defaults.

Batch log: `.utmp/GrassGroundValidation.batch.log`.
Previews: `GrassGround_ClosePreview.png`, `GrassGround_RepeatPreview.png`,
`GrassGround_DistancePreview.png`, and `GrassGround_WithBladesPreview.png` in this directory.
