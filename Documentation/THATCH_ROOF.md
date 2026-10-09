# Single thatched roof

There is one **Thatch roof** menu option, appended after the seven existing
building options. The previous repeat/left/right/single configurations and their
generated Unity assets have been removed. Setup and rebuild create only this roof.

## Blender source

Source: `C:\Users\samee\OneDrive\Desktop\BlenderModelsFirstSettlers\Building\BuildComponents.blend`.
The user's edited source was backed up before repair as
`BuildComponents.before-roof-repair-2026-10-09.blend`, beside that file.

The remaining six construction objects are kept at the user's origin, in
**Thatch Roof — editable construction**. The thatch was replaced in place;
timber geometry/transforms and the woven substrate geometry/UVs were preserved.
The stale hidden joined variants were removed, leaving one hidden export copy.
Other building objects and their UVs were preserved.

The thatch is a closed rhombal prism: **8 vertices, 6 quads, 12 triangles**.
It has straight planar faces, with no subdivisions, waviness, stepped joints or
bevels. All four narrow faces are present, including the previously absent ridge
face visible from the back. Every edge has exactly two incident faces; normals
point outward. Each face has nonzero UV area.

The full joined roof includes four timber members and the existing woven card:
**62 triangles total**, one material/submesh. The previous single roof had 922
triangles. Renderer vertices may be split at UV and normal seams; the editable
thatch itself still has eight geometric vertices.

Use `Tools/BuildThatchRoof.py` inside Blender with this source open to repeat the
repair/export. It edits the remaining named objects and does not recreate variants.
Back up later hand edits first. Reopen the source in Blender to load changes made
by the background repair process.

## Shape, axes and placement

Unity: X along the ridge, Y up, +Z ascending. Editable Blender: X along the ridge,
Z up, +Y ascending. The structural origin is the left eave bearing corner.
Nominal grid size remains 4 m wide × 2 m rise × 2 m run, 45-degree pitch.

Thatch thickness is 0.30 m perpendicular to the slope. Its lower face follows
Y=Z+0.12, and its upper face is 0.424264 m vertically above that face. The 0.25 m
eave overhang remains; its lower edge dips 0.13 m below the bearing reference.
Both ridge-direction sides now end at X=0/4, so the same closed prefab can repeat
every 4 m without overlapping gable overhangs. There are no gable overhang variants.

Actual complete visual bounds:

| Axis | Minimum | Maximum |
| --- | ---: | ---: |
| X | 0 | 4 |
| Y | -0.13 | 2.544264 |
| Z | -0.25 | 2 |

The eave/ridge grid references remain 2.75/4.75 m above the wall base. Covering
reaches approximately 5.294 m. Export naming describes structural dimensions:
`Assets/Models/Buildings/Wood/Roofs/ThatchRoofPanel_4.0Wx2.0Hx2.0D.fbx`.
Unity bakes the FBX hierarchy rotation into runtime vertices/normals without
normalizing visual minimums, preserving the snap origin.

The model importer now enables **Bake Axis Conversion**. The exporter uses
native construction axes, without the earlier compensating half-turn. A
before/after comparison verifies the same runtime vertices, normals and UVs.

For a 4×4 bay, place one copy at (0,2.75,0), yaw 0°, and its opposing copy at
(4,2.75,4), yaw 180°. For a 4×8 building, add the same copies at (4,2.75,0), yaw
0°, and (8,2.75,4), yaw 180°. Wall/post top, opposing ridge and ±4 m repetition
sockets remain. No cap compatibility selection or variants are required.

Aiming at the broad/high face of an existing roof now continues uphill with
the same heading: +2 m rise and +2 m run. Its low edge offers the reverse
continuation with the same heading; the left/right edge offers ±4 m sideways
repetition. A roof hit no longer automatically turns the next piece 180°.
Start an opposing slope from the other wall side, or rotate manually.

The eave overhang is automatically removed from the visual mesh of a piece
with a same-heading roof immediately below it. This prevents a coplanar 0.25 m
overlap at an uphill join. Preview and instanced rendering use the same joined
visual; removing the lower neighbor restores the exposed eave. This is an
internal render choice within the same definition/prefab/menu option. Both
visuals have 62 triangles; the Blender construction geometry is unchanged.

## UV repair

The successful broad-face fibre direction and density are retained. To allow
two texture repeats across a single quad, the existing reeds are packed twice
across the lower half of the same 2048² roof atlas. No new artwork was generated.
The upper half of the atlas, including every wood/end-grain/wattle pixel, is
unchanged. The woven card's UVs are unchanged.

The full thatch UV rectangle is U=8/2048..2040/2048,
V=8/2048..1016/2048, with wrapped eight-pixel gutters. Broad faces use fibre V
along the slope. Gable cut faces use independent slope × thickness coordinates,
and eave/ridge cuts use width × thickness strips. No face projects to a UV line.

Timber side faces were corrected using each beam's own longitudinal/transverse
axes. Grain now runs along the member on all four sides, including the tilted
rafters. End faces use square end-grain islands. Beam geometry is unchanged.

`Tools/PackSingleRoofAtlas.py` repacks the existing standalone reed texture.
`Tools/PrepareThatchAtlas.py` also finishes with that packing when rebuilding
from the saved generated texture source. The original shared wood atlas and all
non-roof building materials/UVs remain unchanged. Roof shading still uses the
existing matte shader, alpha-tested weave, instancing, fog and nighttime lighting.

## Physical shape and verification

Occupied shape and pooled physical collision are one thin sloped convex prism
plus an eave-bearing prism: **two convex colliders**, not a filled attic box.
Logical placement uses those solids as well. Visual overhang remains outside
occupancy. The original stair walking hull and motor behavior are unchanged.

Validation checks include closed faces from all six directions, non-degenerate
UVs on every imported triangle, normals and hierarchy baking, exactly one roof
option, the original seven option order, 4×4/4×8 repetition in eight rotations,
wall/post/gable/beam contacts, attic/player clearance, support loss/reconnection,
near/far instanced views, menu scrolling and actual off-screen roof shadows.
Blender broad/rear/gable/underside renders are in `ArtReferences/ThatchRoof/Repair/`.

```powershell
./Tools/ValidateBuildingPrototype.ps1 -RoofChecks
./Tools/ValidateBuildingPrototype.ps1
./Tools/ValidateBuildingPrototype.ps1 -StairChecks
```

These run in the isolated Unity project, preserving the saved scene and lighting
assets. No live-scene performance benchmark or new gable/beam art is included.
