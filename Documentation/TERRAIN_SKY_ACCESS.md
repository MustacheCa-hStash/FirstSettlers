# Cached cave sky access

Implemented October 16, 2026 after the P6 building checkpoint. This is derived lighting data; terrain remains session-only. Point/spot-light dynamics, torch assets, procedural cave generation and P7 remain separate work.

## Brightness controls

Select `Assets/Resources/TerrainSculpting/CaveLighting.asset` in Unity's Project window. Its values are read live during Play mode; editing brightness does not retrace the field.

| Setting | Default | Effect |
| --- | --- | --- |
| Enabled | On | Turns cached cave ambient on/off. Off restores ordinary ambient. Existing allocated cache storage is retained until world teardown. |
| Cave Ambient Multiplier | **0.025** | Deep sealed caves retain 2.5% of ordinary ambient. Try 0.01 for darker caves, 0.05–0.10 for easier visibility, or 0 to remove ambient entirely. A value of 1 restores ordinary ambient everywhere. |
| Sky Response | **1.5** | Shapes partial exposure near entrances/shafts. Higher values darken partial exposure; lower values brighten it. Fully exposed outdoor surfaces remain unchanged. |
| Blend Seconds | **0.35** | Blends replacement visibility values over this period. Set 0 for immediate publication when diagnosing updates. Brightness sliders themselves apply immediately. |

The approximate ambient multiplier is `lerp(CaveAmbientMultiplier, 1, pow(SkyAccess, SkyResponse))`. It scales both sky ambient and the material's minimum ambient floor. For example, a terrain floor of 0.26 becomes about 0.0065 at sky access 0 with the default multiplier. Existing time-of-day ambient colors and building ambient-floor dimming still apply.

`WorldManager > Terrain Sculpting > Cave Lighting` can override the default asset. An empty reference uses the resource above. Restart Play mode after changing which asset is assigned; changes to the already selected asset's fields are live. Probe spacing is fixed at **2 m**, independent of the 0.3 m terrain density lattice.

The terrain and shared wood/stone building shaders consume the field. Their fog color is attenuated too, to avoid bright outdoor fog washing out the cave. Direct sunlight/shadows and existing building direct-light contributions remain separate. Multiplier 0 removes ambient, rather than guaranteeing black pixels in the presence of direct light. Arbitrary shaders on other props/characters need this shared include integrated explicitly; Unity's stock materials do not automatically read it.

## Cache and reconstruction

- A tile spans **32 m**, owning 16 lighting cells on each axis with **17 cubed probes** including its boundary. Shared world coordinates reconstruct matching border probes.
- Allocate tiles around committed density overrides with a 2 m guard margin, within a rolling **512 x 256 x 512 m** lookup window around the player (32 m alignment). Sparse residency is capped at the **nearest 128 tiles**. This does not allocate all underground stone or change terrain render/collision ranges.
- Each below-baseline air probe tests nine upward directions: vertical has weight two, and eight upper-hemisphere directions each have weight one. Visibility uses the same six-tetrahedron interpolation as the terrain mesh, with 0.15 m trace steps and a 128 m maximum. Reaching the original landscape's exterior certifies outdoor access for the present solid-below-heightfield generator; exhausting the trace counts as occluded.
- Six additional short axis traces store free-space distances up to 4 m. Shading rejects interpolation from probes across nearby walls/floors/ceilings using these distances. Natural-air probes above the original height surface cannot illuminate a cavity through a retained thin roof. No eligible interior probes means conservative zero sky access.
- Any committed density revision invalidates all selected tiles. This deliberately includes lighting dependencies beyond a brush's bounds: a skylight can affect the chamber below it. Unchanged tiles remain reusable across focus changes. Distant tiles retire and reconstruct when revisited; none are saved to disk.
- Work uses `TerrainRequestManager.TryRunSculpt`, sharing existing mesh-worker admission. It runs after terrain work is ready and is cancelled when terrain needs the worker. There is no second terrain worker pool. Main-thread publication observes the shared frame allowance, with one tile upload as an indivisible operation.
- A captured density revision and cancellation token protect publication. Cancel/join precedes world disposal. Old complete lighting remains until replacements publish; new tiles blend from ordinary lighting. Geometry/collision publication never waits for lighting.

## Memory and performance boundaries

The cache reserves **24 MiB within the existing 128 MiB derived terrain cap**, leaving 104 MiB for terrain meshes, suppression and extraction cache in the normal host. The reservation covers the two RGBA8 probe atlases, surface-height atlas, page table, managed arrays, Unity-readable copies, GPU storage, bounded worker scratch and conservative overhead. Textures allocate only once there are nearby edited tiles. Tiny synthetic hosts with less than 32 MiB derived allowance omit this optional consumer.

The probe atlases are each 272 x 2312 texels, about 2.4 MiB raw; the baseline-height atlas is 272 x 136 texels. No density values are persisted again. Density snapshots retain their existing separate 64 MiB admission rules, so a cache refresh can wait for snapshot headroom rather than exceed the limit.

Rendering uses a small page-table/height lookup outside caves and eight visibility-weighted probe lookups inside. It performs no fragment density ray marching. A completed tile uploads its rectangles to readable Unity textures; actual driver upload and fragment cost still require a representative live-world GPU profile. F3 reports status, resident/pending/omitted tiles and latest trace/upload durations. Profiler markers are `FS.Streaming.Sculpt.SkyTrace` and `FS.Streaming.Sculpt.SkyUpload`.

## Limits

This estimates direct sky access, without bounced/colored global illumination. Small passages with no eligible 2 m air probe can remain dark. Six axis distances reduce thin-wall leaks but cannot represent every diagonal/complex visibility boundary; dense irregular caves still need visual testing. Terrain is the occluder; wooden roofs do not yet alter this field.

Uncached or capacity-omitted tiles use ordinary ambient. Updates are asynchronous, so a newly created cave can initially show ordinary lighting while its first tiles are traced. The nearest-player priority and F3 omitted count make this explicit. A later larger cache or finer boundary representation should be guided by measured playtests.

## Validation

`Tools/ValidateTerrainSculpting.ps1 -WorldChecks` includes dedicated sky tests for sealed/open chambers, dependencies beyond excavation bounds, sub-spacing roofs/walls/internal ceilings, negative coordinates, live controls without retracing, retirement/reconstruction, stale worker rejection, 128-tile capacity, shared worker admission, bounded derived storage and in-flight teardown.

`Tools/ValidateTerrainSculptingWorld.ps1` exercises real URP terrain/building images, independent cave brightness, exterior preservation and a newly excavated skylight, alongside the existing 98 shader variants, roof shadows and GPU vegetation checks. Measured results and captures are recorded after successful validation; these isolated checks do not establish a production frame-time target.

Runtime/editor/player compilation, **232 dedicated cache assertions**, **295,357 P6 consumer checks**, **8,565 P1 checks** and shared height-sampling regressions passed. The final URP run passed **34 graphics checks** and all **98 shader variants**, including a direct GPU page lookup matching the sealed-cave CPU query. Mean image brightness in the ambient-only floor fixture changed from **0.2439 to 0.0161** with the default cave multiplier; opening a skylight raised it to **0.0499**. The brightness-1 image matched ordinary lighting, and exterior brightness remained unchanged. These are image measurements in the isolated fixture, not exposure-independent physical light units.

The latest boundary tile in the CPU fixture traced in about **2 ms** with a **0.05 ms** upload in the headless backend; this mostly solid tile does not represent a large open chamber or a real GPU upload. Use the live F3/profiler values for representative costs. Visibility lookup follows the geometric surface normal, so texture-detail normals do not move the sample across terrain boundaries.

Captures: [ordinary ambient](Images/CaveSkyAccessDisabled.png), [default cave ambient](Images/CaveSkyAccessEnabled.png), [opened shaft](Images/CaveSkyAccessOpenedShaft.png), [quarter ambient](Images/CaveSkyAccessQuarterAmbient.png), [building ambient](Images/CaveSkyAccessBuildingDark.png). Logs: `.utmp/terrain-sculpting/validation.log` and `.utmp/terrain-sculpting/p6-graphics.log`.
