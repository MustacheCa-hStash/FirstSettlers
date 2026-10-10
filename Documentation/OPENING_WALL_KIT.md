# W09, W11 and W15 implementation

The catalog has ten options: the original seven plus W09 doorway wall, W11 shutter
wall and W15 half-gable. W15 has one menu entry; **R** flips its slope using a
linked definition/mesh with the same footprint. Q/E, wheel rotation and nudges
continue to work. The mirrored definition is also discoverable through the
catalog's `Find` method without appearing as another menu option.

| Piece | Structural dimensions, W × H × D | Clear opening / collision |
| --- | --- | --- |
| W09 | 4 × 3 × 0.25 m | 1.25 × 2.25 m centred doorway; three solid boxes |
| W11 | 4 × 3 × 0.25 m | 1 × 0.75 m centred aperture, sill at 1.25 m; four solid boxes |
| W15 | 2 × 2 × 0.25 m per half | 45° rising/falling triangular prism; six vertices, eight triangles |

No door leaf, shutter, window glass or moving hardware was authored. Dimensions
for openings measure clear space inside the timber framing. W09's doorway runs
X=1.375..2.625, Y=0..2.25. W11's opening runs X=1.5..2.5, Y=1.25..2.0. Both wall
profiles occupy X=0..4, Y=0..3, Z=-0.125..0.125; they keep zero end reservation,
the 3 m stacking datum, quarter-metre junction sockets and the existing permissive
build-piece overlap policy. Their physical colliders are permanently open,
independent of cosmetic mesh fitting.

## Blender and materials

Saved source:
`C:/Users/samee/OneDrive/Desktop/BlenderModelsFirstSettlers/Building/BuildComponents.blend`.
Backup: `BuildComponents.before-opening-walls-2026-10-10.blend` beside the source.

Open **Opening wall kit — editable**. Individual editable parts are on Blender
Y=30: W09 at X=0, W11 at X=6, rising W15 at X=12, falling W15 at X=16. Separate
roof seam strips are at Y=34, X=12/16. Joined exports are hidden in **Opening wall
kit — exports (hidden)**. Core models are 4/4/2/2 m wide, with Blender Z as height
and Y as thickness. Existing 187 mesh objects retained their geometry, transforms,
UVs and material assignments. Re-running `Tools/BuildOpeningWallKit.py` replaces
only this script's own collections.

All pieces reuse the existing ordinary wood trim atlas and matte material. Each
board/frame samples a deterministic varied crop; grain follows upright boards,
horizontal rails and diagonal slope framing. End/cut faces are unwrapped too.
Mirrored gable meshes retain valid UVs/normals without negative transform scaling.
The source components are closed solids with flat faces and no subtle deformation.

| Export | Visible triangles |
| --- | --- |
| W09 | 288 |
| W11 | 384 |
| Each W15 core | 116 |
| Each contextual roof seam strip | 12 |

Visual meshes reference their FBX subassets directly. Imports bake axis conversion,
preserve authored normals and disable runtime mesh readability/material import.
Triangle collision assets remain separate tiny meshes. Rebuild with **Tools →
Building → Rebuild W09 W11 W15 Kit** or the full modular-kit rebuild command.

## Connections and fitting

W09/W11 stack and seat floors/roof eaves like split plank and wattle. Normal end
corners generate shared post visuals. Interior junctions touching an aperture
suppress generated posts that would intrude into that aperture. Manually added
intersecting pieces remain allowed; suppression does not automatically open those
pieces' colliders. Removing neighbours restores the appropriate derived visuals.

Floor fitting uses the actual solid wall sections rather than cutting the entire
rectangular envelope across a clear aperture. A gable's floor intersection uses
its occupied slope at the floor's underside elevation.

W15 starts on the selected 2 m half of a wall's top. Place the rising half on the
left and falling half on the right to form a 4 m-wide gable. A wall ending at 3 m
therefore produces a structural ridge at 5 m; the same datums work on later
storeys. Continuing from a gable uses its base level instead of treating its empty
bounding-box top as another flat wall storey. Gables do not generate 3 m corner
posts; their base, peak and slope framing are authored in the models.

A roof-end snap follows the aimed roof's actual origin/heading, selecting either
the eave-to-ridge half or the opposite half from its ridge. Conversely, snapping a
roof to a gable uses the gable's low eave datum and selects the roof direction from
the slope/viewer side. Face contact between convex pieces transmits support, and
removing the supporting wall disconnects unsupported gable/roof assemblies.

The existing roof's visible underside is 0.12 m above its structural slope. An
aligned roof end therefore supplies a contextual **0.125 m vertical seam strip**
on the gable, with 5 mm seating overlap. It is an instanced render attachment;
its removal/restoration follows the roof neighbour. It does not change the 2 m
gable rise, triangular collider, health or menu options. Its highest visual point
is 2.125 m above the gable base, within the roof covering. Unattached gables show
only their 2 m core. The authoring prefab holds that core and its physical collider;
the contextual trim is added by the build renderer.

## Verification

`Tools/ValidateBuildingPrototype.ps1 -InstalledAssets` passed **29,891 checks** on
the main project's actual installed assets without rebuilding them first. Checks
include imported face UVs/normals, physical ray clearance from both sides,
external-object eligibility through openings, section-aware floor fitting, all
eight headings, 3 m stacks, roof-first/gable-first support, contextual seam removal,
existing junction/pool lifecycle regressions and 96 stair motor traversals.
There were also **16 real CharacterMotor doorway crossings**, in both directions
at every heading, using the saved player's 1.8 m height and 0.4 m radius.

`-InputChecks` passed native R-key flipping, fresh/held presses and menu-held
suppression, alongside existing B/E/Escape/F8/F9 and arrow-repeat checks. Blender
verified every source component's closed faces and noncollapsed UVs. Rendered
front/back models and an assembled house were inspected; images and reports are
in `ArtReferences/OpeningWallKit`. These checks use isolated synthetic Unity
scenes; the user's saved scene was not opened or rewritten.

Restart Play mode before testing the new catalog and assets in the game.
