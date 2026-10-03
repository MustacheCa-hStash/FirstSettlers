# Forest–grassland transitions

Implemented scope: the moisture-driven forest–grassland border. Categorical biome and gameplay ground-cover labels remain owned by the existing classifiers. Terrain appearance and population decisions consume a shared membership field.

## Membership and shape

`BiomeTransitionPolicy` dispatches the registered biome pair and rejects water, riverbed/bank surfaces, cold grassland and mountain grassland. It uses the existing climate moisture threshold of **0.65**, with a starting half-width of **0.015 climate units**, modulated by the existing river mask. A smoothstep produces forest weight `w`; meadow weight is `1 − w`. Edge intensity is `4w(1 − w)`.

This is a range in a climate field, not a distance from a biome boundary. Its physical width depends on the local climate gradient. A shallow gradient produces a broad transition, a steep gradient a narrow one; river modulation adds habitat variation. A perfectly uniform gradient can still produce an even band locally. Existing grove, canopy, clearing and colony fields then break up the visible vegetation within that membership envelope.

Membership is stored in `WorldFeaturePlan.ForestMembershipMap`: **0** means outside this registered pair; **1…255** encode pure meadow through pure forest. Consumers preserve their categorical behavior for the sentinel. Quantization error is at most approximately **0.002** in weight.

## Completed population and rendering paths

| Path | Implemented behavior |
|---|---|
| Ecological fields | Evaluate the existing forest profile where forest weight is positive, and the meadow profile where meadow weight is positive. Reuse the existing maps. Continuous membership replaces the one-cell categorical forest-neighbor boost for this pair. |
| Terrain substrate | Blend existing floor density toward meadow density 1, and attenuate forest soil, moss and mixed litter. Existing three control textures and their padded blur carry the result. Meadow/default samples in the reused floor field initialize to density 1. |
| Far terrain | Its actual control-map job evaluates the same policy, including byte quantization, and the same floor blend. No new control texture or terrain mesh channel. |
| Grass | Keep one existing candidate lattice. Select exactly one forest or meadow profile per candidate using a stable hash, then thin by that profile's density. Recover the forest density from the existing blended field; no second density map is needed. Near and billboard generation use identical decisions. |
| Trees | Keep the existing forest and meadow candidate orders and species profiles. Weight acceptance by `w` and `1 − w`, retaining shared exclusions. Registered-pair trees share a ceiling of `max(18, configured meadow cap)`, instead of adding the two maxima. |
| Rocks | Weight the existing forest/meadow passes, including large meadow boulders and their satellites. Registered-pair rocks share `max(configured forest cap, configured meadow cap)`. Existing regional rockiness, assets, scale ranges and exclusions remain in use. |
| Berry bushes | Extend the existing forest patch pass into eligible mixed habitat and weight child acceptance by forest membership. Keep the existing 30-bush ceiling and patch/species logic. |
| Flowers | Choose the effective biome profile within the existing patch candidates. Honor the configured allowed-biome mask and palette. Meadow-only flowers taper out instead of stopping at the categorical line. |
| Lupines and daisy weeds | Existing drifts retain their patch geometry, blockers and candidate budgets; meadow membership selects surviving children. |
| Clover and dandelions | Use complementary profile selection within the existing clump candidates. Forest-clover enablement, patch frequency, slope rules and graded moss retention remain active. Margin clover on litter diminishes toward the existing pure-forest opening rule. Dandelions use the meadow share. |
| Ferns and leaf scatter | Extend their existing single generation pass to positive forest membership and weight retention by that membership. Retain existing habitat, moss, blocker, asset and LOD rules. |
| Streaming | Retain one persistent native byte copy with the terrain buffers. Grass and plant jobs acquire the existing terrain lease; the owned-buffer fallback also carries membership. Disposal waits for outstanding readers. |
| Distant trees | Sparse preparation samples its dependencies before building fields and clears each reused profile sample. Mountain masks come from cached height samples. Its slope stencil now uses the same world halo as full terrain; signed cache keys prevent collisions outside the chunk grid. |
| Inspection | F3 includes forest/meadow weights. `Tools > Terrain > Preview Forest–Grassland Transitions` reads the current scene inputs, finds eligible borders, previews and exports membership. |

“Substitution” means complementary acceptance or asset selection inside the existing budgets. It does not run an additional complete population over the transition. Expected pre-exclusion density follows the weighted profiles; exclusions, habitat filters and caps can reduce the realized density. Grass family and LOD selection still use the existing resident renderer.

## Computation and memory

For a 128-sample chunk, including its existing halo, there are **131 × 131 = 17,161** samples.

* Membership adds one small arithmetic/classification pass over those samples, using already available inputs. It adds **no climate noise, height sampling or boundary-distance transform** to full terrain generation.
* The retained membership costs **16.76 KiB managed + 16.76 KiB native = 33.52 KiB per full chunk**, excluding array headers. A sparse workspace adds only the managed byte field and reuses it.
* Existing ecological arrays are reused. A mixed point needs both existing profiles: that adds five forest noise evaluations on the meadow side, or five meadow evaluations on the forest side. Meadow-side mixed points also need the existing forest-floor evaluator, which was previously skipped there. That evaluator currently uses seven noise samples; it receives cached canopy/clearing inputs in the full planner. Pure meadow and pure forest avoid evaluating the opposing profile.
* A previously entirely meadow chunk which now contains forest membership can allocate the existing floor field and native density field: about **268.14 + 67.04 KiB**. Chunks already carrying those fields reuse them. Forest scatter can also become eligible in such chunks; its retained instances are still thinned by membership.
* Grass adds a byte lookup, decode and deterministic family decision per candidate. It does not double the grass candidate count or upload a second instance population. Existing tree/rock candidate ceilings and lattices remain bounded; the shared cap counts the previous pass once, then updates a local counter.
* Render cost depends on which existing meshes survive. Substituting assets can change vertex work and mixed regions can use both existing material families. No new shader, mesh, texture or GPU instance field was introduced.

The existing `TerrainWorldFeaturePlan` and foliage profiler stages include this work. Correctness results below are not a gameplay frame-rate benchmark.

## Validation and inspection

Run `Tools > Terrain > Validate Forest–Grassland Transitions`, or batch entry point `BiomeTransitionValidation.RunBatch`. `RunGraphicsBatch` additionally checks the existing resident grass GPU family/LOD lists.

The suite exercises eligibility, exact family endpoints, byte accuracy, variable physical width, reused full/sparse workspaces, shared tree/rock budgets, berry habitat, near/billboard/fallback grass identities, native reader lifetime, plant profile/toggle/moss rules, unrelated biome controls, negative-coordinate seams, real border chunks and the actual far-terrain control job. It also runs the existing distant-tree and forest-floor regressions.

Validated in Unity 6000.4.8f1: all checks passed, including 7,738 identical near/billboard/fallback grass candidates; 48 matching near/distant trees across five real border chunks; 289 mixed far-terrain control samples; and the 2,048-candidate GPU family/LOD regression. The controlled density test produced 2,586 pure-forest tufts, 7,569 mixed grasses (1,268 forest tufts forming a subset of the forest profile), and 12,544 pure-meadow grasses. Optional lupine/daisy drift tapering and the closed-forest clover limit also passed. No live gameplay frame-rate benchmark was run.

Generated inspection files are in `ArtReferences/BiomeTransitions`. `Membership.png` is a controlled variable-gradient illustration. `WorldMembership.png` uses terrain/climate sampling with its inputs recorded in `WorldMembership.txt`: yellow meadow, green forest, cyan mixed habitat, other colors unchanged biome classifications. The editor map samples fields directly; loaded chunks interpolate their sampled fields.

Restart Play Mode to regenerate existing chunk populations. Existing seeds can now yield different placements within eligible transition habitat.

## Expansion contract

Keep pair eligibility and membership math in `BiomeTransitionPolicy`, separate from categorical classification and rendering. `BiomeBorder` identifies the pair; sampling is stateless and usable by managed and Burst paths. `PrimaryWeight`/`SecondaryWeight`, `Membership`, `EncodeWeight`/`DecodeWeight`, `Population` and `SelectPrimary` provide reusable pair math and compact storage.

For another shared border, add its pair dispatch and compact membership storage, then connect that pair's existing habitat profiles to the same candidate/budget/lease pattern. The current byte field deliberately describes forest–grassland only; do not reinterpret it for unrelated pairs or allocate a full float field for every biome. Supply the same inputs to full, sparse and far sampling, and add pair eligibility, seam and representation-parity cases before enabling the new consumers.
