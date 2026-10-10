# Floor framing and reversible building joints

Current full-span models, varied floor UVs and shared junction visuals are described
in [full-span wood kit](MODULAR_WOOD_KIT.md). This document retains the earlier
floor/framing design and source history.

The current permissive eligibility policy and proposed dimensional migration
are in [permissive building dimensions](PERMISSIVE_BUILDING_DIMENSIONS.md).
General build-piece intersections warn rather than reject. Current compatible
floor fitting remains; unavailable fitting keeps a full solid floor and warns.
New openings and additional render representations are deferred.

The approved intersection plan is implemented with one timber-floor menu option.
The existing eight catalog entries retain their IDs and order. Stairwell and
bearing prefabs are authoring references, not additional placement options.

## Blender models

Source: `C:\Users\samee\OneDrive\Desktop\BlenderModelsFirstSettlers\Building\BuildComponents.blend`.
Backup: `BuildComponents.before-floor-system-9071ad3f.blend` beside the source.
`Floor and framing — editable` contains individual planks, joists, rim spans,
caps and opening trimmers. Construction groups are laid out at Blender Y=12;
joined exports stay hidden at the structural origin. Existing building meshes,
UVs and material assignments were fingerprinted before/after and preserved.

| FBX under Assets/Models/Buildings/Wood/Floors | Nominal Unity X × Y × Z | Triangles |
| --- | --- | ---: |
| TimberFloor_4.00Wx0.25Hx4.00D.fbx | 4 × 0.25 × 4 m | 324 |
| TimberFloorStairwell_4.00Wx0.25Hx4.00D.fbx | 4 × 0.25 × 4 m | 480 |
| RimBeam_3.50Wx0.25Hx0.25D.fbx | 3.5 × 0.25 × 0.25 m | 12 |
| CornerBearingCap_0.25Wx0.25Hx0.25D.fbx | 0.25 m cube | 12 |

All models use one material/submesh. The ordinary wood/end-grain atlas is reused
unchanged. Timber grain follows each member; cut ends have end-grain UVs. Deck
planks are 0.05 m thick, over joists in the remaining band. The clear authored
opening is X=0.75..3, Z=0.5..4 (2.25 × 3.5 m). No threshold crosses its exit.
Opening trimmers replace underlying plank strips, avoiding coplanar top faces.

Unity local Y=-0.25..0, with the walking/bearing top at the origin. Blender axes
are X width, Y depth, Z up. FBX Bake Axis Conversion is enabled. The separate
floor-framing-blueprint.json preserves the authored box components for closed,
UV-mapped runtime cuts. `Tools/BuildFloorFraming.py` regenerates this kit only;
back up subsequent hand edits to its collections before using that script.

## Levels and snapping

Use S for the walking surface of ANY level. Walls/posts occupy S..S+2.75;
stacked walls/posts start at S+3. The 0.25 m band between them contains real
bearing geometry. A floor at S+3 fills that band; absent floors leave the
implicit rim/caps carrying the upper member. Adding/removing a floor never
moves the wall or roof anchors.

Roofs seated on wall/post tops now start at S+3. They can also seat on supported
floors, and a floor aimed at a roof uses its bearing level. Existing roof uphill,
downhill and sideways continuation remains. Low attic headroom is not solved by
adding flooring: raise the eaves with appropriate walls for a habitable storey.

Ground timber finishes snap FLUSH with the foundation walking top S. Their
solid regions replace only the foundation's upper 0.25 m; the lower body and
grounding remain. Removing the finish restores the cap. Basement excavation
through the lower foundation is not an automatic joint.

## Ownership and restoration

`BuildPieceRecord` retains canonical definition, pose, health and identity.
`BuildResolvedState` contains its core mesh, optional auxiliary bearing mesh, solid boxes/convex volumes and
opening claims. `BuildResolution` recomputes those states from original geometry
using spatial neighbours and caches the resulting meshes by local shape.
Nothing is progressively trimmed or permanently replaced with a shorter piece.

Floors subtract the required stair passage and protected wall/post footprints.
Their deck, joists and framing receive closed cut ends. Compatible touching
opening claims merge; multiple remaining stairs can keep an opening after one
is removed. The exact authored opening uses the imported stairwell mesh;
other offsets/interior openings use the same component blueprint and capped
rectangle assembly. Intersections requiring unavailable diagonal fitting retain
the full floor/collider with a warning; rotating an aligned building 45 degrees
still supports the existing fitting rules.

Stair openings are computed from floor elevation, the actual 1.5/2 slope, 2 m
headroom and a 0.5 m body margin. Half-flight clearances therefore differ from
full-storey ones. Full flights produce the planned 2.25 m opening width. Real
CharacterMotor tests validate ascent/descent, including landing transitions.

Walls, posts and foundation bodies retain their full physical shapes. General
build-piece overlap and low headroom are advisory in either placement order.
Both physical colliders remain intact; a buildable arrangement can still obstruct
walking. Duplicates, required support and external obstruction still reject.
They are never auto-carved to excuse a conflicting placement. Doors, basement
openings, gables and arbitrary mid-panel post joints still need explicit pieces.
The W21 model and continuous walking collider are unchanged; its logical
occupancy now uses the ramp hull rather than the full rectangular envelope.

Implicit bearings are real solid shapes attached to a qualifying lower bearing,
not free grounding sources. Floors suppress duplicate bearing regions; stairwell
voids prevent them regenerating across a passage. Roof bearings reuse the span/
cap dimensions and remap ordinary wood UVs into the roof's existing atlas.
These bearings are separate instanced render output sharing their owner's pose
and material. They never copy CPU data from the authored wall/post/roof mesh.
Imported split-plank and infill FBXs retain Read/Write disabled. Floor insertion
suppresses only the auxiliary output, and removal restores it; the core mesh
remains the same asset throughout. Preview rendering follows the same rule.

Support contacts are rebuilt from resolved solids and explicit stair/roof sockets.
Support still traces to grounded footings. Removal preserves the existing grace
period for reconnection/collapse. Preview evaluates the affected existing pieces
and displays their proposed meshes before commit; placement that would remove
an existing supported member's support is rejected.

Pooled collision proxies refresh when resolved geometry changes, so floor holes
are physically open and restored floors become solid again. The instanced
renderer batches by resolved mesh, retains visual bounds, matte/night/fog behavior
and nearby off-screen ShadowsOnly casters. No visible GameObject is spawned per
placed piece.

The stacked-wall regression is verified in actual Play mode, including pixel
captures with the imported wall unreadable, eight rotations, three storeys,
floor insertion/removal, preview changes and removing/restoring the lower wall.
Run `Tools/ValidateBuildingPrototype.ps1 -IntersectionChecks` for these checks
and the permissive roof/stair placement, physical-collider and warning HUD tests.

## Tooling and verification

Prototype rebuild includes the kit and its blueprint. The original wall, wattle,
infill, post, stair and roof assets remain authored sources; the saved scene and
global lighting are not rewritten by isolated validation.

```powershell
./Tools/ValidateBuildingPrototype.ps1 -FramingChecks
./Tools/ValidateBuildingPrototype.ps1
./Tools/ValidateBuildingPrototype.ps1 -StairChecks
./Tools/ValidateBuildingPrototype.ps1 -RoofChecks
./Tools/ValidateBuildingPrototype.ps1 -InputChecks
```

The framing suite covers imported UV area/normals, three-metre wall/post stacking,
floor-before/after bearing replacement, foundation cap restoration, multiple
opening claims, physical hole rays, protected wall appearance, floor/roof ordering,
duplicate rejection and 16 actual-motor stairwell traversals (floor-first and
stairs-first, uphill/downhill, four rotations). Additional layout checks use all
eight yaw steps. Legacy building, 96 stair-motor cases, roof/shadow and native
input suites also pass. See ArtReferences/FloorFraming for images/import reports.

Resolution uses the spatial index for neighbouring joints, cached generated meshes
and cached previews. Support traversal still covers the session graph on structural
changes. This is functional/render validation rather than a large-world FPS benchmark.
