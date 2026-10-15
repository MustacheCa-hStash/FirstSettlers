# Square wall infill

The **Square wall infill** entry is in the Walls category. It measures **2 m wide,
2 m high, 0.25 m thick**, with origin at the lower-left structural line and
thickness centred at local Z = 0. It shares the installed split-plank material
and ordinary wood atlas. No additional texture or material is needed.

Eight individually UV-mapped vertical boards and three rear rails use varied,
atlas-safe grain crops; end faces use end grain. All eleven timber prisms are
closed, totaling 132 triangles. The gameplay shape is one box, independent of
the board gaps. Import uses baked axis conversion, imported normals, no imported
materials, and no CPU mesh copy.

Aim at the left or right half of a 4 m wall top to place a square in that 2 m
half bay. Squares continue sideways, stack at 2 m intervals, and attach beneath
rectangular walls or gable bases. Aim near a gable's vertical end to fill beside
it at its base level. Normal quarter-metre nudging and rotation remain available.
Existing W15 half-gables sit directly on the squares' 2 m tops. Panels do not
require a symmetric counterpart. Normal support, duplicate and external-object
checks still apply; build-piece overlap retains the existing permissive policy.

For the pictured 8 m wide gable, place a rising triangle, two squares, and a
falling triangle across the bottom roof level. Two half-gables sit on the central
squares to close the peak. This produces a 4 m rise above the 3 m wall line.

An auxiliary post uses a dedicated **2.003 m** visual mesh, including the same
3 mm seating relief as the existing posts. It appears through the shared junction
resolver; it is not a separate menu piece or collider. Its width/depth are 0.26 m
to cover the 0.25 m junction without coplanar faces. Gable contacts retain the
existing suppression of rectangular posts at sloping ends.

Blender source: `BuildComponents.blend`, collection **Square infill kit — editable**,
panel at Blender (0, 46, 0), post at (3, 46, 0). Joined exports are in its hidden
exports collection. `Tools/BuildSquareInfill.py` recreates only these collections
and checks that unrelated mesh geometry, transforms, UVs and materials remain
unchanged. The source was backed up before authoring.

Unity references:

- `Assets/Models/Buildings/Wood/Modular/SquareInfill_2x2x0.25.fbx`
- `Assets/Models/Buildings/Wood/Modular/SquareInfill_JunctionPost_2m.fbx`
- `Assets/Resources/Building/square-infill.asset` and `.prefab`
- `Assets/Resources/Building/UI/Thumbnails/build_wood_square-infill.png`

The normal asset rebuild includes this definition. For a targeted installation
that preserves existing catalog entries and material choices, run
`Tools/ValidateBuildingPrototype.ps1 -SquareInfillAssets` and copy only the new
assets and catalog entry from the isolated project. `-InstalledAssets` validates
the actual installed references without regenerating them. Regressions cover all
eight headings, asymmetric placement, half-bay snapping, rectangular stacks,
under-gable attachment, real physics rays from both sides, duplicate rejection,
independent removal/restoration, UVs/normals, and short corner posts. Front/back
Unity captures and validation results are in `ArtReferences/SquareInfill`.
