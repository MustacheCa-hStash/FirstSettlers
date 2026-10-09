# Ordinary wood: Iron Age inspired building concepts

Five drawn concept sheets for First Settlers. No meshes, prefabs, scripts, colliders or scene assets were created or changed by the concept work.

- [01: Core building parts](01-core-building-parts.png)
- [02: Enclosure additions](02-enclosure-additions.png)
- [03: Roof and assembly proposals](03-roof-and-assembly.png)
- [04: Ladders and low access](04-ladders-and-low-access.png)
- [05: Stairs and landings](05-stairs-and-landings.png)
- [Access dimensions, starter parts and implementation notes](ACCESS_COMPONENTS.md)
- [Ordinary wood trim atlas, UV coordinates and Blender guide](Textures/README.md)

The images communicate construction, silhouettes and proportions. The dimensions below are the intended design specification; painted grain, joinery, assembly openings and perspective are illustrative, not measured manufacturing drawings.

## Visual direction

Use ordinary wood as one resource tier: axe-hewn poles, riven boards, woven branches, wooden pegs and restrained fibre lashings. Vary grain and tool marks, keeping mating faces and overall envelopes consistent. Separate bay posts from infill panels. Dense wattle is a useful lighter-looking alternative to plank infill. Roof structure stays wood, with an optional reed/thatch covering.

This is a British Iron Age inspired direction adapted to the game's square grid, not a reconstruction of a specific ancient house. Butser Ancient Farm's [Iron Age area guide](https://static1.squarespace.com/static/5c50c0077c93277e627b7374/t/5de52685e6b0e7013ac813d2/1575298696278/Butser-Ancient-Farm-Iron-Age-Area.pdf) and [reconstructed buildings](https://www.butserancientfarm.co.uk/buildings-and-experiments) provide period reference for roundhouses, timber, wattle and thatch. The illustrated square gabled shelter is a gameplay adaptation. A roundhouse kit would be a later design task, including relative-45-degree junctions and radial roof parts.

## Existing dimensional contract

Checked against `Assets/Scripts/Building/BuildGeometry.cs`, `BuildDefinition.cs`, the four current definition assets, `BuildPlacement.cs`, `BuildGameplayProxy.cs` and `Documentation/BUILDING_PROTOTYPE.md`. The assets/code take precedence over the older README sentence that calls the wall 4 m wide.

- Position increments: 0.25 Unity world units, treated here as metres; independent of terrain worldScale.
- Eight yaw headings in 45-degree steps. This is horizontal rotation, not roof pitch.
- Full bay: 4 x 4 m. Wall infill: 3.50 W x 2.75 H x 0.25 D m.
- Separate corner/post envelope: 0.25 W x 2.75 H x 0.25 D m.
- Bay accounting: 0.25 + 3.50 + 0.25 = 4.00 m on a foundation perimeter. Wall-to-wall extension currently touches panels directly; it does not automatically insert posts.
- Floor: 4 x 4 m, occupying 0.25 m below its top surface. Foundation: 4 x 4 m, occupying 0.50 m below its top.
- Wall/post origins are lower corners; floor/foundation origins reference their tops. A floor-bearing storey is 3 m, comprising 2.75 m wall plus 0.25 m floor. Directly stacked walls advance 2.75 m.
- Logical bounds stay independent of decorative mesh detail. Post contact faces should occupy their 0.25 m cells; a thin round pole could look disconnected despite logical support.

## Planned parts

All sizes are metres. W/H/D refer to wall width, height and thickness; floor/footing dimensions are footprint and total depth. A door leaf is a fitted moving element rather than an independently quantized grid piece.

| ID | Part | Planned size | Description and fit |
| --- | --- | --- | --- |
| W01 | Split-plank wall | 3.50 W x 2.75 H x 0.25 D | Riven vertical boards on pegged rails. Matches current wall envelope: 14/11/1 ticks. |
| W02 | Wattle wall | 3.50 W x 2.75 H x 0.25 D | Woven wood rods on thin stakes/rails. Alternate wall appearance with the same envelope. |
| W03 | Bay post | 0.25 W x 2.75 H x 0.25 D | Separate lightly squared pole filling a corner or wall end. Now uses the Bay post option (retained Corner placement kind): 1/11/1 ticks. |
| W04 | Timber floor | 4.00 x 4.00 footprint; 0.25 deep | Split boards over shallow joists, all within the current 16/1/16-tick floor envelope. |
| W05 | Sleeper footing | 4.00 x 4.00 footprint; 0.50 deep | Proposed ordinary-wood foundation variant using low cross-laid sleepers; current foundation dimensions: 16/2/16 ticks. Foundation remains the grounding role. |
| W06 | Horizontal beam | 4.00 L x 0.25 x 0.25 | Exposed lintel/tie member. Proposed beam placement rules; optional 2 m variant shares the same section. |
| W07 | Low plank wall | 3.50 W x 1.25 H x 0.25 D | Pen, edge or partition infill; 14/5/1 ticks. Deliberately grid-aligned low height, not an exact half of 2.75 m. |
| W08 | Narrow wall | 1.50 W x 2.75 H x 0.25 D | Short infill: 6/11/1 ticks. With two 0.25 m post cells, a nominal 2 m bay. |
| W09 | Doorway wall | 3.50 W x 2.75 H x 0.25 D | Same full wall envelope; centred 1.00 W x 2.25 H clear opening, measured inside jambs and below lintel. |
| W10 | Pegged door | 0.95 W x 2.20 H x 0.08 D | Fitted plank leaf; nominal 0.025 m clearance per opening side. Pivot/batten protrusions require detailed fit planning. Not an independent 0.25 m grid definition. |
| W11 | Shutter wall | 3.50 W x 2.75 H x 0.25 D | 1.00 W x 0.75 H opening with sill 1.25 m above wall base. Proposed fitted wooden shutter; no glass. |
| W12 | Wattle fence | 3.50 W x 1.25 H x 0.25 D | Coarser open-weave boundary panel; 14/5/1 ticks. Separate end posts can use a shorter future post variant. |
| W13 | Rafter module | 2.00 horizontal run x 4.00 ridge length; 2.00 rise | One half of a 4 m-wide 45-degree roof. Slope line is sqrt(8), approximately 2.83 m. 0.25 m structural member envelope. |
| W14 | Woven roof deck | Same projection/rise as W13 | Close branch laths over the timber frame. Roof substrate, with optional thatch above it; not a horizontal floor. |
| W15 | Gable infill | 3.50 W x 2.00 peak H x 0.25 D | Ends rise 0.25 m, peak rises 2 m; slope matches the central 3.5 m of a 4 m bay. Needs shaped end fillers in the remaining 0.25 m strips. |
| W16 | Ridge beam | 4.00 L x 0.25 x 0.25 | Reuse W06 geometry with ridge-specific attachment, rather than authoring a duplicate fundamental mesh. |
| W17 | Upright rung ladder | 0.75 W x 3.50 H x 0.25 D | Reaches a 3 m floor with 0.50 m handholds; 0.25 m rung pitch. |
| W18 | Leaning loft ladder | 0.75 W; 3.00 rise; 1.00 run | Steep rung ladder, with 0.75 x 3.50 x 1.25 m reserved envelope including handholds. |
| W19 | Notched log ladder | 0.50 W; 3.00 rise; 1.00 run | Optional carved-footstep variant; 0.50 x 3.25 x 1.25 m envelope. |
| W20 | Threshold steps | 1.00 W; 0.50 rise; 1.00 run | Two broad 0.25 m risers and 0.50 m goings. |
| W21 | Half-storey stair | 1.25 W; 1.50 rise; 2.00 run | Eight 0.1875 m risers with 0.25 m goings, on pegged stringers. |
| W22 | Full-storey stair | 1.25 W; 3.00 rise; 4.00 run | Sixteen matching risers/goings; optional long variant of two W21 modules. |
| W23 | Small landing | 1.25 x 1.25 footprint; 0.25 deep | Top-surface anchor; intermediate turn at 1.50 m elevation between half flights. |
| W24 | Stair handrail | 2.00 run; 1.50 rise; 1.00 above tread line | 2.00 L x 2.50 overall H x 0.25 side-depth envelope. |

## Roof relationships

A 4 m-wide roof has two 2 m horizontal runs. At 45-degree pitch, each rises 2 m: eave line at 2.75 m above the wall base and ridge line at 4.75 m. These are logical reference lines; member and thatch thickness can extend beyond them. For an 8 m-long building, repeat the 4 m-long roof modules along the ridge. Thatch overhang does not enlarge the nominal floor bay. Exact roof bounds, joints, cover depth and overhang need an authoring pass before implementation.

The illustration's large cutaway entrance exposes construction; W09's 1 x 2.25 m opening is the doorway specification. Likewise the assembly study is not a counted bill of materials.

## Implementation boundaries and useful starting order

W01-W04 are visual designs around today's envelopes. W05 is a proposed wood variant of the existing grounding component. Simple rectangular additions such as W07/W08/W12 could use additional definitions, but have not been implemented or validated. W06/W16 need beam attachment design. The current enum contains only Wall, Floor, Foundation and Corner.

The current gameplay proxy uses one solid BoxCollider per logical piece. Doorway/shutter openings need compound or shaped collision, and moving leaves need separate interaction/physics. Open beams and pitched roofs need appropriate occupied-volume and support rules as well as physical colliders; collision changes alone will not fix the logical box overlap/contact checks. Roof pitch must be authored into the part geometry or supported by new placement rules; the existing 45-degree yaw control does not tilt a roof. No existing runtime behavior was changed.

For a small first art pass, start with post, plank wall, floor, doorway/door and one covered roof module; add wattle, low/narrow walls and fitted shutters as silhouette variants. Keep the sleeper footing simple and low. Reuse beam geometry for ridge/tie roles. Earth floors are appropriate visual context for huts; the plank floor remains useful for platforms and raised storage.

## Generation

Made with the built-in image_gen tool. [Initial building generation and correction prompts](GENERATION_PROMPTS.md) and [access component prompts](ACCESS_GENERATION_PROMPTS.md) are saved alongside the five final PNGs. Sheets were visually checked, with targeted annotation corrections. [The access guide](ACCESS_COMPONENTS.md) records the authoritative modeling dimensions and runtime requirements. Runtime tests were unnecessary because these additions change concept references only.
