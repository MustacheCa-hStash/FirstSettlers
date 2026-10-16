# Local flame lighting and torch

The current PC default uses spatial direct lighting with cached stationary shadows. A functioning surface-illumination cache is available as an opt-in experiment; the benchmark did not justify enabling it universally. The original eight-light experiment and its CPU measurements are preserved in [the historical baseline](LOCAL_FLAME_LIGHTING_BASELINE.md); those measurements do not describe this revision.

## Playing

Restart Play mode after importing the changed scripts and prefabs. **B → Lighting → Mounted torch** places the torch on a supported wall/post; **T** equips or puts away the development carried torch. Mounted emitters and flame bases remain under a stationary scene root even though `BuildWorld` belongs to the moving player.

Torches now have a **12 m illumination cutoff**, warmer amber colour and a broader attenuation curve. Illumination radius is independent of observer distance. Placed effects remain eligible within **96 m** of the camera; the bridge retains up to **128 flame effects**, with at most **64 illuminating sources**. These remain explicit PC prototype budgets, not unlimited settlement lighting.

**F3** reports active/registered lights, shadow refreshes, pending work, render-coverage suppression, ready/resident surface pages, page updates, lighting CPU time, allocations and raw cache texture memory.

Fuel, inventory ownership, recipes, persistence of placed builds, weather extinction, smoke, sound and multiplayer ownership remain outside this implementation.

## Surface illumination

`LocalFlameSurfaceCache` rasterizes resident static geometry into world-aligned surface patches. Each **8 m cell** has six oriented views, each **32 × 32** samples. The normal and surface depth are stored independently of irradiance; material colour is applied by the production receiver shader. Existing build instancing and procedural cave meshes need no new UV charts or per-instance mesh copies.

Up to **192 cells** remain resident. Unchanged patches reuse the combined average direct illumination from all relevant stationary sources. When enabled, the adaptive path bypasses cache sampling when fewer than two stable averaged sources can affect the receiver, or when moving contributions require live evaluation; the forced-cache mode remains available for comparison. The shader checks surface depth and normal before accepting a sample. Other layers, unsupported chart coverage, strong normal detail and unavailable pages use spatial direct lighting. Only one outward surface layer per orientation/cell is cached; this is an explicit limitation, not a claim of complete UV coverage.

Four cache levels (**32, 16, 8, 4**) are selected from the projected surface footprint. Coarse levels reject incompatible planes/normals and large illumination discontinuities. Lookup falls back to finer levels or direct lighting when a coarse result is unsafe. Depth/normal guards prevent borrowing another nearby wall's illumination; finite sampling and filtering still limit sharp shadow-edge fidelity.

Stationary colour/intensity changes, extinction, placement and removal dirty affected illumination. Geometry changes dirty the directly affected surface capture and every affected light's influence region, including receivers beyond the edited object. The default admission is **one complete six-face stationary shadow capture** and **two surface cells per frame**. Dirty cells immediately use current direct lighting; shadow records awaiting replacement suppress their source until blocking geometry is available. This avoids showing stale cached light through newly built walls while work is queued.

## Moving objects and flicker

Moving sources redraw their environment shadows. Moving occluders use separate **64-pixel** radial-depth faces; unchanged stationary blockers retain **256-pixel** faces. The receiver-plane shadow comparison snaps rays to the actual shadow texel centres and accounts for each filter sample's location on the geometric receiver plane, reducing the concentric/grazing self-shadow bands from the previous fixed-distance comparison. Filter samples still clamp at cube-face edges; seam/bias and small-moving-object quality remain visual review areas.

When the experimental cache is enabled, static surface irradiance stores average flame brightness. Nearby stationary flicker applies a live correction, smoothly reducing toward average illumination between **12 and 20 m observer distance**. Flame sprites continue their independent animation. A carried source or moving blocker affecting a receiver switches that pixel to exact spatial direct evaluation while leaving its stored surface data intact. Dynamic receivers explicitly bypass the static surface cache. This avoids double lighting and residual illumination from subtracting approximate combined light when a door closes.

## Spatial selection, readiness and lifecycle

GPU cell lists identify relevant lights in **8 m cells**. An empty region does not loop over all 64 sources. Membership uploads change only when ready-source membership or cell coverage changes. Bounds and memory are fixed; dense overlap still costs more than torches distributed across separate rooms.

`ITerrainLightGuard` checks published render coverage separately from player collision residency. Missing/unpublished edited geometry still suppresses affected lighting; collision-only distance limits do not. Revision checks are deliberately conservative and can temporarily suppress illumination during terrain publication.

Builds, terrain, rocks and authored static props publish mesh/matrix references independently of camera visibility. Trees contribute blockers through their resident provider. Thin/cutout meshes and unsupported cache charts retain the direct path. Dynamic objects must register through `LocalFlameOccluder`; spawners should add it explicitly. Source removal and world teardown release pooled effects and registration; destroying the manager releases GPU resources.

## Memory and supported materials

At full configured capacity, static shadow arrays contain **96 MiB** and dynamic arrays **6 MiB**: **102 MiB raw textures** on the default path. Surface data including four levels/scratch targets allocates lazily when enabled and adds about **24 MiB**, for approximately **126 MiB** after opting in. Experimental textures remain resident until manager destruction after first use. Spatial buffers, command buffers, driver allocation and metadata add overhead. Arrays allocate when lighting is first used. The fixed capacity is substantially more memory than the old 24 MiB experiment and needs representative lower-end-PC profiling before choosing final quality tiers.

The lighting uses shader model **4.5** on the project's PC renderer. Terrain, wood, baked stone, furniture, rocks and the integrated vegetation shaders receive the custom contribution. Arbitrary stock URP shaders do not automatically receive it. Normal-mapped surfaces that cannot safely reuse a patch retain live material response through the direct path.

This is cached **direct diffuse illumination**, not bounced global illumination. It does not create light around cave bends without a valid direct path. A bounce approximation remains a separate experiment; the wider attenuation improves useful direct coverage without raising the global cave ambient floor.

## Validation and comparison

`Tools/ValidateLocalFlame.ps1 -SurfaceChecks` runs GPU pixel checks for actual cache hits, coarse-level use, direct/cache agreement, all six orientations, thin walls, nearby parallel surfaces, moving doors, dynamic receivers, colour/intensity/extinction, render-only readiness, distant observation and 64-source overlap. `-PlayChecks` covers catalog placement, mounted anchoring, pool reuse, carried clearance and teardown. The default command retains the direct-shadow/terrain regressions.

`Tools/ValidateLocalFlame.ps1 -Comparison` builds and runs an isolated standalone 1080p comparison: a synthetic **24 × 48 m hall** and **48 m tunnel**, each with **8, 32 and 64 torches**. It compares an eight-light reference, spatial direct lighting, forced surface sampling, adaptive cached surface lighting, a moving door, native URP lights and lights off. It records frame/CPU timings, valid GPU samples when supplied, surface hit coverage, memory, warm rebuild counts and recovery after wall/torch edits. Native URP has a copied 4096 shadow atlas and requested 256-pixel faces; its intensity is adjusted because its attenuation differs. This is a practical reference, not identical radiometric output.

To try the experiment in Play mode, select the runtime **Local flame lighting** object and enable **Use Surface Cache**. **Adaptive Surface Cache** bypasses cases that cannot amortize sampling; disable it to compare forced sampling. These runtime toggles reset when Play mode stops.

Results and final measured tradeoffs are documented in [the surface-cache report](LOCAL_FLAME_SURFACE_CACHE.md). The historical CPU-only cache-vs-redraw result must not be presented as a benchmark of this surface system.

All validation uses isolated `.utmp` projects. No saved gameplay scene or main pipeline/quality asset is changed. The torch model, editable source and flame atlas remain documented in [the art guide](../ArtReferences/Torch/README.md).
