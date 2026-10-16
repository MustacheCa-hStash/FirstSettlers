# Local flame lighting investigation

Implementation follow-up: [installed system, torch assets and measured baseline](LOCAL_FLAME_LIGHTING.md). The recommendations below record the earlier investigation and are superseded where the implementation document differs.

October 16, 2026. Research and proposed implementation sequence; no runtime lighting, assets, inputs or scene settings changed by this investigation. All numeric budgets below are starting experiments, not measured performance claims.

## Recommended first content

Start with one wooden, fibre-bound torch, available as a carried source and in a simple wall holder. Both use the same flame/light definition. This is an art proposal consistent with the project's British Iron Age inspired construction direction, not a reconstruction of an excavated British torch. Model and texture references for the particular head, binding and fuel still need selection before final asset production.

A small wick lamp or protected lantern can follow using the same runtime. Avoid committing to a familiar modern fuel lantern. The British Museum's [first-century Roman bronze lantern](https://www.britishmuseum.org/collection/object/G_1856-1226-670) has protective horn sheets, now lost, and a lamp holder. It is a useful ancient reference but does not establish that this form belongs to pre-Roman British domestic equipment. Using it would be an explicit extension of the game's existing flexible period direction.

Deliver the lighting proof before producing several finished props. The important initial experience is a moving warm pool of light on cave rock, building wood/stone and outdoor ground, with a clearly burning source.

## Existing integration points

Inspected against Unity **6000.4.8f1**, installed URP **17.4.0** and the current working tree, including ongoing furniture/cave changes.

| Area | Current evidence | Work needed |
| --- | --- | --- |
| PC renderer | `Assets/Settings/PC_Renderer.asset` selects Forward+ (`m_RenderingMode: 2`). `PC_RPAsset.asset` supports additional-light shadows with a 2048 atlas and 256/512/1024 tiers. | Reuse this path; inspect the actual quality/camera configuration during validation. The serialized four-lights-per-object value does not cap Forward+ illumination. |
| Terrain | `CustomStylizedTerrain.shader` accumulates main directional light and cave-scaled ambient only. | Add per-pixel clustered local diffuse lighting, distance attenuation and additional-light shadows, for both original terrain and excavated surfaces. |
| Buildings | `BuildingWoodMatte.shader` already has `InputData`, `LIGHT_LOOP_BEGIN`, clustered variants and additional-shadow sampling. | Verify actual pixels for installed wood/stone/furniture materials. Preserve two-sided weave normals. Keep local contribution separate from directional shadow tint during review. |
| Loose rocks | `WeatheredRock.shader` has an additional-light loop. | Render actual rock materials under the prototype and check occlusion. |
| Vegetation | Examples including `ForestGrassInstanced.shader` and `CloverInstancedSimpleLit.shader` use main-light shading without a local-light loop. | Audit active near grass, fern, shrub and tree shaders, then extend the ones needed around the player. Far impostors need no torch illumination outside its range. |
| Cave darkness | `TerrainSkyAccess` controls environmental ambient and underground directional shadow correction. | Retain its cached visibility. A moving/flickering torch must not retrace this field or have its light multiplied by cave ambient. |
| Shadow geometry | Excavated terrain has two-sided punctual shadow casting. `BuildRenderer` retains nearby off-screen batches as shadow-only casters. | Validate point-light coverage, bias and camera turns using actual terrain/build meshes. Caster submission/residency must cover active lights as well as camera-visible receivers. |
| Equipment | `InventoryView` explicitly presents an empty inventory; it has no item ownership/equipment implementation. | Use a small development equip fixture first. Do not imply that a torch is already a functional inventory item. Add real equip/placement ownership as a separate integration step. |

Unity's [Forward/Forward+ custom light-loop guide](https://docs.unity3d.com/6000.4/Documentation/Manual/urp/use-built-in-shader-methods-additional-lights-fplus.html) matches the installed package: `GetAdditionalLightsCount()` returns zero in clustered mode, while `LIGHT_LOOP_BEGIN` traverses clusters using `inputData.positionWS` and normalized screen UV. A plain count-based loop would therefore miss the lights. Keep shadow shader variants in player builds and check the terrain shader's existing D3D11 sampler limit when adding shadow/cookie resources.

## Illumination and containment

Use **one real URP point light per selected torch**, with warm color, smooth attenuation and a finite range. Begin around a 6 m cutoff and tune illumination in a 4 m building bay and a larger carved chamber. Readability, exposure and material response determine final intensity/range; no physical unit or exact visibility radius is claimed yet. The light should reach nearby floors, walls and ceilings in every direction. A spotlight would impose an inappropriate flashlight cone on an exposed flame.

Evaluate shading as `albedo * (environmentAmbient * caveMultiplier + directionalDiffuse + localDiffuse)`, retaining existing normal mapping and material style. Directional horizon/shadow tint must not artificially extinguish warm local light. Do not raise a global ambient floor to simulate the torch. With the source extinguished, sealed caves should return to their existing darkness.

**Occlusion is required for nearby gameplay lights.** Short range alone cannot prevent light crossing a thin cave wall, wooden floor or roof. Start with full-strength punctual shadows and tune near-plane/depth/normal bias against thin geometry, including cubemap seams. Existing weakened sun/moon shadow strengths must not be copied to flame lights. Use the actual light-emitting position outside the prop's opaque head; exclude only inappropriate self-shadow geometry, retaining terrain and building occluders.

An off-screen flame can illuminate an on-screen wall, and an off-screen wall can block it. Select lights using their influence bounds, not just visibility of the flame sprite. Retain the necessary local shadow casters when turning the camera. At streaming boundaries, do not claim containment from missing meshes/colliders: gate or fade affected illumination until required occluder coverage is ready. A physics ray to the camera is not sufficient proof of receiver visibility.

For carried use, attach the prop to a world-space hand/socket fixture and keep the emitter clear of nearby walls using the existing collision/readiness rules. Test first-person near clipping and retraction. A separately rendered foreground prop must not place its real world light through rock. Mount placement must keep the emitter on the room side of its supporting wall. Removal, extinguishing, unequipping and world teardown must release registration and pooled resources.

## Flame appearance

Use a tightly cropped, seamless flame flipbook with alpha, a bright yellow core, orange tongues and fine broken edges. Begin with a 6 x 6 atlas of 128-pixel cells (768 x 768), frame blending around 24 fps, a shared emissive material and per-instance phase. Verify texture compression and alpha edges in the target build. The precise atlas dimensions/frame count are art experiments.

Render a small upright camera-facing card; compare a second crossed card for close/overhead views, keeping the base anchored to the fuel. Add modest UV distortion or tip bending so the motion includes upward flow, changing silhouette and irregular licks instead of uniform whole-sprite scaling. Avoid a visible rectangular halo and flat overhead disappearance. Use depth-tested premultiplied transparency with depth writes off; check ordering when flames overlap. A small additive core is optional only if the first layer lacks brightness. Bloom is optional polish, not the flame's silhouette.

Drive light intensity and flame size/emission from related smooth, seeded noise. Try roughly 8% intensity variation, small size changes and independent phases per source. Avoid abrupt random values every frame, synchronized torches or constant fast strobing. Keep the light's transform/range stable unless the holder moves: visible flame tips can move without making all shadows jump. Color changes should be subtle. Flicker must not trigger terrain, cave-cache or material reconstruction. Standard URP shadow rendering can still cost work each frame even when only intensity changes; do not assume automatic static shadow caching.

No lights on individual flame particles. Pool an optional sparse ember/smoke effect, reduce it with distance, and defer dense smoke/volumetrics. Keep flame bounds tight because transparent fill and overlap can dominate even a low-polygon effect. The luminous texture shows the fire; the point light illuminates its surroundings. Each must remain convincing when bloom is disabled.

## Bounded work and provisional budgets

| Resource | First experiment | Constraint |
| --- | --- | --- |
| Occluded local lights | At most **2 shadowed point lights** active near the player | Reserve one for the carried source; prioritize the other by relevant influence and stability. Compare a one-light mode. |
| Shadow resolution | **256 pixels per face**, compare 512 for carried light | Measure seams, leaks, atlas packing and shadow draw cost; do not increase the whole atlas by default. |
| Flame-only visuals | At most **32 nearby animated sources**, with distance/screen-size LOD | Independent of active illumination; distant sources can remain visible without allocating lights. |
| Selection | Central registry, bounded nearby candidates, reusable buffers; refresh about **10 Hz** | Add spatial buckets for settlement scale. Follow the selected carried source every frame; registration must not scan the scene each frame. |
| Stability | Hysteresis plus short fades on illumination reassignment | Brief handoffs must still respect the hard light/shadow cap; avoid duplicate overlapping lights during fades. |
| Allocation | **0 recurring managed allocations** after warm-up | Shared materials/atlas, pooled light objects and effects; no per-source material instances or per-frame arrays. |

A point light needs six shadow faces; two at 256 per face request twelve atlas tiles before other users. Unity documents the [shared atlas and six-face point-shadow cost](https://docs.unity3d.com/6000.4/Documentation/Manual/urp/shadow-resolution-urp.html). Atlas space is only one budget: geometry submission and GPU raster/filter cost must also be measured. The cap applies across this flame system, with atlas headroom checked against other game lights.

When over budget, fade lower-priority illumination while retaining its flame visual. **Do not silently turn a shadowed cave/building light into an unshadowed light**, which would reintroduce wall leaks. Two active sources are a deliberately limited prototype, not an adequate final promise for a furnished settlement. If playtests require many simultaneously illuminating mounted lights, compare more shadowed lights against an explicitly designed static local-visibility/shadow cache. That extension needs build/remove/excavation invalidation and moving-occluder handling; it is not supplied by the existing sky-access cache.

Provisional profiling targets on an agreed PC/resolution: under **0.2 ms p95 CPU** for source selection/flicker/follow and under **1 ms p95 added GPU time** for the initial carried-plus-mounted case. These are acceptance goals to test, not expected results. Record hardware, resolution, quality, baseline and sample duration. Measure total frame time and the Forward+ culling workload as well as custom script markers. Do not infer world FPS from a small validation fixture.

## Implementation checkpoints and evidence

1. **Lighting proof:** a development point-light fixture, terrain local-light support and real URP comparisons on terrain, carved stone, installed building materials and loose rocks. Verify darkness with the light off and day/night ambient regressions. Compile affected variants for editor and player, including Forward+ and any supported fallback renderer.
2. **Containment and cost:** moving carried source plus mounted source, full shadow strength, thin walls/floors/ceilings, camera turns, range boundaries, cubemap seams and streamed occluders. Instrument registered/selected/shadowed/suppressed counts, selection CPU time, allocations, atlas warnings and shadow draw/GPU cost. Run 1, 2, 8 and 32 registered-source scenes, including deliberate budget overflow.
3. **Flame asset and prop:** author the torch head/binding, atlas and shared material. Capture moving video from normal, close and overhead views, with/without bloom and against dark/bright backgrounds. Inspect loop continuity, lighting/flame timing, transparency overlap and held-item clipping. Include nearby active foliage in outdoor validation; document any remaining shader exclusions.
4. **Gameplay integration:** carry/equip, mounted placement, light/extinguish and cleanup behavior. Reuse current building placement/support and menu/focus policies. Inventory ownership, recipes, fuel duration, rain/water extinction, persistence and multiplayer replication need their own explicit scope; they are not prerequisites for proving local illumination.

Required visual cases: sealed cave and adjacent sealed chamber; a bend that blocks the source; thin roof with outdoor ground above; wood and stone rooms with doors open/closed; two storeys; source behind the camera; floor partly outside the light range; overlap between two sources; daylight and midnight surface vegetation; excavation/build removal that opens or closes a light path. Compare the same receiver with source off/on and with an occluder added/removed. A sealed adjacent room must not brighten merely because the source is close across its wall.

This investigation performed source/configuration review and checked installed URP implementation against Unity documentation. It did not run Unity, render a flame, compile changed shaders or benchmark lighting. Those results belong to the checkpoints above.
