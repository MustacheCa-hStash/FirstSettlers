# Forest showcase technology review for FirstSettlers

Research date: November 6, 2026. Recommendation: retain Unity and adopt selected
rendering and environment concepts incrementally. A full engine replacement is
not justified by the evidence available for this game.

This assessment combines the supplied screenshot, the public YouTube description
and creator replies, original developer posts retrieved through X's public embed
service, sampled video storyboard frames, official engine documentation, and the
current FirstSettlers source and existing benchmark reports. It is a research and
source audit; no new gameplay performance measurements were taken. Storyboard
samples establish visible content, not temporal stability or measured frame time.

**The showcase is primarily an achievement in foliage rendering, lighting and
environment composition.** Its visible landscape does not establish a new
runtime terrain-generation, excavation, collision or world-simulation solution.
The author is Toni Macaroni, whose current custom engine followed earlier Godot
experiments. The current engine's programming language and graphics API were not
verified in the primary material examined; claims that it is specifically a
C++/Vulkan implementation should not be treated as established facts. The
description's mention of virtual textures concerns the earlier Godot work and
does not independently confirm a particular texture-streaming implementation in
this engine. [Showcase and description](https://www.youtube.com/watch?v=uR5EvBWxN14).

**What is confirmed, and what it means for us.** The explanations below distinguish
the developer's statements from proposed FirstSettlers implementations.

| Technology | Confirmed information | Engineering interpretation and limits |
| --- | --- | --- |
| Clustered LOD / virtual geometry | The description names CLODs. The developer separately confirms GPU-driven meshlets and use of mesh shaders. | Small mesh clusters permit finer visibility and detail decisions than one LOD for an entire tree. The exact hierarchy, error metric, compression and residency implementation remain undisclosed. |
| Far geometry as voxels | Tiny triangles become voxel representations; ordinary image impostors are excluded from this demonstration. | This addresses the difficulty of simplifying disconnected leaves and needles while retaining canopy volume. It does not imply rendering every source polygon or that voxels are automatically cheaper in our workloads. |
| Very distant proxies | The description says the extreme-distance representations omit instance data. | Aggregating a forest region is one possible approach for us. The author's exact proxy construction and retained data are unknown. Rendering without individual tree records does not remove our need to retain tree identity and edits elsewhere. |
| Hybrid GI | Screen-space and voxel information provide global illumination. Hardware RT is not used. | The two representations can complement each other: screen detail and world information beyond the camera view. A specific cone-tracing, octree or probe algorithm is not confirmed. |

The geometry and hardware statements are supported by the developer's
[meshlet/voxel explanation](https://x.com/ToniMacaroniy/status/2113574482107646296)
and [mesh-shader confirmation](https://x.com/ToniMacaroniy/status/2113395585399337153).
The interpretation of why these techniques help is an engineering assessment,
not a reconstruction of unpublished source code.

**Several comparatively accessible choices contribute to the appearance.** The
developer says terrain and scattering are generated in Houdini with placement
rules. He calls out tree-size gradients at forest margins and different canopy
forms inside and outside stands. His clouds are described as 2.5D rather than
fully volumetric. He also leaves small ground-cover grass out of shadow maps and
uses contact shadows for it. These are useful examples of concentrating effort
on the visible result while limiting expensive simulation and rendering work.
[Terrain and scattering](https://x.com/ToniMacaroniy/status/2117374994715938925),
[cloud representation](https://x.com/ToniMacaroniy/status/2117402460902043823),
[grass shadows](https://x.com/ToniMacaroniy/status/2116137664923705681).

A creator reply additionally describes grass alpha-test mip tuning, a mask
derived from movement and selective sharpening to reduce temporal smearing.
These are image-quality measures, separate from virtual geometry. Their success
in our game would depend on our anti-aliasing, motion vectors, leaf textures,
depth coverage and camera movement. They are not evidence that all temporal
reconstruction or upscaling has been eliminated.
[Developer's image-quality reply](https://www.youtube.com/watch?v=uR5EvBWxN14&lc=UgwCRwX2wfkfN2B16254AaABAg.AclGpi9_HpfAclJPhGa9OI).

**The performance claims need careful interpretation.** The opening cards show
three million trees and approximately four million polygons per tree. Those are
scene/source-detail claims; they do not establish the number of triangles
rasterized each frame. Cluster selection, visibility rejection and far
representations are precisely how a renderer avoids processing all source detail.

The developer reports approximately 130 FPS average on an RTX 3080, around
200 FPS in aerial views and 80 FPS in dense forest, with a reply identifying
1440p rendering. The uploaded video is 4K; upload resolution and rendering
resolution are separate. The opening statement about DLSS 5 does not establish
native rendering without other reconstruction. These results were not
independently benchmarked here and do not establish a complete game's budget.
[Performance thread](https://www.youtube.com/watch?v=uR5EvBWxN14&lc=UgweB79yG-NXkYF1arR4AaABAg).

I found no identifiable public repository or downloadable release for this engine
in the developer's public repository inventory. There is therefore no verified
drop-in source package or licensing offer to integrate. The eight-month period
mentioned in the description also follows the author's stated two to three years
of intermittent renderer development; it should not be used as an estimate for
rebuilding all of FirstSettlers.
[Public repositories](https://github.com/ToniMacaroni?tab=repositories),
[development history](https://x.com/ToniMacaroniy/status/2113547315461894488).

**FirstSettlers already has a substantial foundation.** The checked workspace
uses Unity 6000.4.8f1 and URP 17.4.0, according to
[ProjectVersion.txt](../ProjectSettings/ProjectVersion.txt) and
[manifest.json](../Packages/manifest.json).

| Existing system | Current evidence | Implication |
| --- | --- | --- |
| Resident GPU foliage | Distant trees retain source records, accept region submissions and perform GPU handoff, thinning, frustum culling and depth ordering. Grass, leaves and ferns also have custom GPU paths. | We already avoid much ordinary per-object rendering overhead. Recommending GPU instancing as an entirely new direction would miss the current architecture. |
| Near-tree batching | Four-by-four chunk regions are implemented and enabled in the documented scene. | The earlier per-chunk submission bottleneck has already received a measured improvement. It is not an untouched optimization. |
| Terrain coverage | World-aligned quadtree-sized far patches, near/far handoff and shared baseline sampling already exist. | Preserve this terrain system while testing foliage or lighting changes independently. |
| Excavation and caves | Sparse authoritative terrain density and derived meshes support edits and caves. | These density samples represent gameplay terrain; they are not a ready-made voxel radiance field or a tree renderer. |
| Lighting and atmosphere | SSAO is enabled in the PC renderer; custom cave sky access, local flame shadows, RenderGraph fog and a skybox cycle exist. | Extend the current lighting architecture deliberately. Cached direct illumination and SSAO do not provide general indirect light bounce. |
| Grass shadow policy | Resident and fallback grass draws already use `ShadowCastingMode.Off`. | The developer's grass shadow-map exclusion is already present. Directional contact-shadow quality would be a separate experiment. |

Relevant implementation references:
[resident tree design](ResidentTreeSubmission.md),
[tree spatial batching](TreeBatchingAndBillboardMemory.md),
[terrain coverage](../Assets/Scripts/ChunkManager/TerrainCoveragePolicy.cs),
[far terrain generation](../Assets/Scripts/TerrainGeneration/MeshGeneration/FarTerrainGenerator.cs),
[procedural caves](PROCEDURAL_CAVES.md),
[grass renderer](../Assets/Scripts/FoliageGeneration/ResidentGrassRenderer.cs),
[PC renderer](../Assets/Settings/PC_Renderer.asset),
[fog feature](../Assets/Scripts/WorldManager/DistantFogRendererFeature.cs).

The existing vegetation audit found that the six assigned nearby tree sources
had no intermediate LODGroup levels, although the standing renderer supports
them. The subsequent batching work deliberately preserved the LOD setup and
handoff distances. Intermediate tree and shadow representations are consequently
a more direct candidate than a complete geometry pipeline replacement.
[Vegetation investigation](VegetationPerformanceInvestigation.md).

The later atlas audit measures approximately 2,084 MiB of HQ billboard texture
payload. A mixed-resolution prototype reduced this to 932 MiB, a 55% saving, and
was pixel-identical in its 18 sampled comparisons. That preset is experimental:
production needs per-map padding changes and import/baker validation. Smaller
atlases did not demonstrate a consistent frame-time win on the test RTX 5090.
This is a concrete memory opportunity, not a promised FPS recovery or a claim
that our impostors are the leading rendering bottleneck.
[Atlas results and limits](TreeBatchingAndBillboardMemory.md).

**Unity can host the concepts without replacing the engine.** Unity exposes
GPU-controlled indirect drawing, and URP RenderGraph accepts custom raster and
compute passes. Those provide practical integration points for regional proxies,
depth hierarchies, new lighting fields and custom foliage drawing. Full CLOD
geometry still needs a builder, cluster hierarchy, streaming/residency policy,
culling, material handling, shadows and animation. Reproducing the author's
specific mesh-shader route would require a separate graphics-API and native
integration investigation; ordinary indirect instancing is not that system.
[Indirect drawing](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Graphics.RenderMeshIndirect.html),
[URP RenderGraph](https://docs.unity3d.com/6000.4/Documentation/Manual/urp/render-graph-introduction.html).

Unity's built-in GPU occlusion culling works through the GPU Resident Drawer and
its compatibility restrictions. Our custom indirect foliage does not
automatically acquire per-instance depth occlusion by enabling that setting.
The examined tree compute path performs frustum/range/density selection without
a hierarchical-depth occlusion test. An added depth-culling pass must justify
its setup cost and preserve wind bounds, camera-turn visibility, reflections
and offscreen shadow casters.
[Unity occlusion-culling documentation](https://docs.unity3d.com/6000.4/Documentation/Manual/urp/gpu-culling.html),
[current tree compute](../Assets/Shaders/DistantTreeCompact.compute).

URP 6.4 has no built-in screen-space GI according to Unity's feature table.
Adaptive Probe Volumes support changing lighting using baked scenarios and sky
occlusion; those features do not by themselves solve arbitrary new terrain,
excavations and player-built roofs. A GI experiment needs custom or third-party
rendering plus explicit support in our receiver shaders and edit lifecycle.
SSGI alone also cannot reliably account for blockers outside the camera view.
[Pipeline feature comparison](https://docs.unity3d.com/6000.4/Documentation/Manual/render-pipelines-feature-comparison.html),
[APV runtime lighting](https://docs.unity3d.com/6000.4/Documentation/Manual/urp/probevolumes-understand-changing-lighting-at-runtime.html).

**The three uses of voxels must remain separate.** Our terrain density is
authoritative solid/air data. A foliage voxel asset describes filtered geometry,
coverage and materials. A GI field describes lighting and visibility. They can
share bounds, change notifications and some occupancy information, but differ
in resolution, contents, update frequency and correctness requirements. A cave
density field alone contains neither the trees and buildings above it nor their
material/radiance data.

For extreme-distance forest proxies, retain deterministic tree identities and
cut-state edits in the gameplay model, even when the renderer stores only a
regional aggregate. Rebuild affected aggregates after chopping or construction.
Likewise, GI changes need bounded dirty-region processing for terrain cuts,
new walls, roofs, doors and streamed geometry. Thin roofs, cave entrances and
offscreen blockers are critical tests: approximate lighting must not reintroduce
sunlight leaks that the current cave and torch systems explicitly prevent.

**Recommended investment order.** The effort ranges below are rough planning
judgments for an experienced graphics engineer using existing project assets,
with art support where needed. They are not measured delivery commitments and
exclude large new asset libraries and broad platform certification.

| Work | Value for FirstSettlers | Indicative effort and decision |
| --- | --- | --- |
| Forest composition and coherent weather | High visual benefit: canopy/understory transitions, matched wind, cloud cover, sun/ambient balance, rain, fog and wetness. Current stands already provide species palettes, clearings and age variation. | A focused visual slice: 1–3 weeks. Complete weather behavior and art take longer. Prioritize this. Houdini is optional; preserve deterministic runtime rules. |
| Intermediate tree LODs, shadow cost and atlas preset | Addresses existing project evidence while retaining present gameplay and rendering ownership. | A bounded first production pass: roughly 1–3 weeks, depending on mesh authoring and visual review. Prioritize this; evaluate memory and frame time separately. |
| Conservative depth occlusion | Can help forest interiors, hills and built settlements; may add overhead in open meadow/aerial views. | First prototype: roughly 1–3 weeks. Continue only after matched GPU measurements show a net gain. |
| Regional forest HLOD proxies | Potentially valuable when long views contain many individually represented trees. | Prototype: 2–4 weeks; production integration approximately 1–3 months. Gate on measured long-view costs and edit/handoff quality. |
| Hybrid dynamic GI | Potentially strong improvement to shaded vegetation, interiors and daylight transitions. Carries leakage, temporal and update-budget risks. | A limited experiment: 4–8 weeks; broad production quality can take 3–6 months or more. Gate on receiver coverage, roof correctness and target GPU cost. |
| Voxel foliage replacing impostors | Can improve distant canopy volume and avoid some card artifacts. Not a demonstrated need in our current workload. | One-species prototype: 4–8 weeks; robust multi-species, wind, season, material and shadow integration can take several months. Defer pending comparison with optimized impostors. |
| Complete virtual geometry | Attractive for a future content pipeline with very high geometric detail. Current assets do not establish that need. | A renderer program of 6–12 engineer-months or more is a reasonable risk allowance, not a guaranteed schedule. Defer. |
| Engine replacement | Would also require replacing or integrating tooling, physics, navigation, animation, UI, asset import, debugging and platform support around the existing game. | A year-scale, open-ended program. Do not start to reproduce this visual demonstration. |

A custom engine becomes a serious option only after a representative Unity
prototype demonstrates a specific architectural limitation that custom passes,
indirect rendering, native integration or a focused custom render pipeline
cannot meet economically. The developer's preference for specialization is not
evidence that Unity must prevent our visual goals.

Unreal is another existing-engine route to investigate if virtualized foliage
becomes a central future requirement. Epic's current Nanite Foliage documentation
describes assemblies, voxel representations and skinning, and labels the feature
Experimental. That demonstrates that the broad ideas are not exclusive to a
bespoke engine; it does not establish performance parity or justify porting this
game. [Epic Nanite Foliage documentation](https://dev.epicgames.com/documentation/en-us/unreal-engine/nanite-foliage).

**The next concrete experiment should be a controlled forest slice in Unity.**
Use an existing meadow-to-forest route plus a settlement and a cave entrance.
Keep seed, camera path, density and display resolution fixed. First establish a
player-build baseline on representative target hardware, because the existing
RTX 5090 fixtures cannot predict an RTX 3080 result. Record CPU and GPU frame
time separately, frame-time percentiles, resident memory and streaming spikes.

Compare intermediate LOD/shadow changes, the production-ready atlas preset and
coordinated weather/lighting one at a time. Review slow walks, fast turns,
aerial views, day/night and clear/rain transitions. Include tree removal,
building placement, a moving door and excavation during streaming. Keep changes
that improve the chosen quality/performance target; do not add their isolated
benchmark savings together as a whole-game prediction.

Only then compare a regional proxy or limited GI prototype against that improved
baseline. Maintain the seeded gameplay world and current terrain/collision
authority throughout. This lets us obtain useful visual improvements early and
gives any larger rendering investment a concrete benefit to prove.
