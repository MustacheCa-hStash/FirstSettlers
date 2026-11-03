# Masonry roofs, floors, arches and infill

Eleven components extend the installed material families. Existing S04 flagstone
and L05 limestone full floors remain available, along with L10/L12 limestone
arched doorway/window walls. Restart Play Mode to reload the catalog.

| New piece | Dimensions, metres | Menu |
| --- | --- | --- |
| Stone / limestone square wall infill | 2 wide × 2 high; 0.5 / 0.25 thick | Walls |
| Stone / limestone half floor | 4 × 2 footprint; 0.25 deep below top datum | Floors |
| Stone / limestone flat roof | 4 × 4 footprint; 0.25 deep below top datum | Roofs |
| Stone arched doorway wall | 4 × 3 × 0.5; 1.5 clear width, 2.25 crown | Walls |
| Stone arched window wall | 4 × 3 × 0.5; 1.5 clear width, sill 1, crown 2.5 | Walls |
| Stone / limestone broad arch wall | 4 × 3; 3.5 clear width, spring 2.25, crown 2.75 | Walls |
| Open braced timber frame | 4 × 3 × 0.25; diagonal knee braces between posts/header | Walls |

Arches use stone rings and actual convex solids around their curved
apertures. Broad arch walls have 0.25 m side piers and shallow curved
bands. Their clear height is at least
2.25 m across the full 3.5 m opening and reaches 2.75 m at the centre.
Solid masonry fills every portion above the broad arch, giving a continuous,
level top at the standard 3 m wall height. These pieces and the narrower
doorway/window walls all retain the complete rectangular wall envelope.
All apertures remain fixed and empty, with no interactive doors, shutters or glass.

Flat roofs are walkable stone decks with level flagstone surfaces and continuous
box collision. They use floor placement and repeat at the same height, including
on wall tops; the menu lists them under Roofs. These replace the two pitched
masonry slab options, preserving their asset GUIDs and content IDs. Existing
timber roof pieces are unchanged. Half floors are fixed slabs: they do
not automatically cut stairwells. Arrange panels around the desired opening.
Placement continues to follow the project's free-building policy.

`Tools/BuildMasonryExpansion.py` creates editable individual source stones, slabs
and framing, low runtime exports, eleven FBXs, an overview and the manifest in
`ArtReferences/MasonryExpansion`. Ten masonry pieces share a 2048-square baked
colour/AO and normal atlas derived from the existing approved stone/limestone
sources. Timber retains the existing Meadow Timber trim and material. Position
increments remain 0.25 m with eight yaw headings.

Run Blender with `--background --factory-startup --python-exit-code 1 --python
Tools/BuildMasonryExpansion.py` to regenerate the native source. Preserve manual
source edits before rebuilding. The Unity importer is **Tools → Building → Install
Masonry Roofs Floors Arches and Infill**. General full-span rebuilding includes
the expansion when its source files are present.

`Tools/ValidateMasonryExpansion.ps1 -Install` compiles source, imports twice in an
isolated project, validates stable GUIDs and unchanged existing definitions,
checks UVs/normals/hulls and actual URP/instanced rendering, then exercises
placement, flat roof joins and wall-top seating, square infill, collision apertures and real motor
traversal at all eight headings. Existing wood, stone and interaction regressions
must pass before the scoped installer copies owned assets and appends catalog
references. Saved world scenes are not edited.

Opening traversal covers the centre and both side lanes of each broad arch,
in both directions at all eight headings. Unity captures and machine-readable
verification reports are saved beside the native source in
`ArtReferences/MasonryExpansion`.

Validation checks that the broad arch tops match ordinary wall height and that
the space above each curve is solid, while preserving the 128 character
traversals. Reimport refreshes live mesh buffers before rebuilding
the menu thumbnails and Unity captures.
