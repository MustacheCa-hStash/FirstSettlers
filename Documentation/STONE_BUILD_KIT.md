# S02, S03, S04 and S06: Blender source and building integration

**S02 update:** the installed coursed wall now uses a **12-triangle box with baked colour/AO and normal maps**, replacing its 2,960-triangle stone assembly. See [baked wall implementation, rebuild and verification](BAKED_STONE_WALL.md). The original detailed S02 source is retained for baking; the remaining stone pieces retain their existing geometry.

This page describes the initial four stone components. [S07 and S12](STONE_BOUNDARY_WALLS.md) extend the installed stone kit to six options, with directly terrain-bearing boundary walls and an empty gate opening. The saved world scene is not changed. The dedicated importer preserves existing wooden models/materials and appends or updates the stone definitions.

## Source and dimensions

Editable source: [StoneBuildKit.blend](../ArtReferences/StoneBuildKit/Models/StoneBuildKit.blend). Individual stones are in **Stone kit — editable individual stones**. Joined exports and UV layouts are in **Stone kit — exports and UV variants**, hidden from rendering. This is a separate source file from the existing wooden `BuildComponents.blend`; no earlier Blender source is overwritten.

[Overview render](../ArtReferences/StoneBuildKit/Models/stone-kit-overview.png), per-component front/back/underside renders and [Blender inspection report](../ArtReferences/StoneBuildKit/Models/blender-models.json) are saved beside the source.

| Component | Unity bounds / anchor | Visible triangles | Construction |
| --- | --- | --- | --- |
| S02 coursed stone wall | X=0..4, Y=0..3, Z=−0.25..+0.25 | 12 | Closed box; nine masonry courses baked from the original 67-stone source |
| S03 stone pier | X/Z=−0.25..+0.25, Y=0..3 | 716 | Eight courses of paired stones with alternating bond, plus recessed core |
| S04 flagstone ground floor | X/Z=0..4, Y=−0.25..0; top-surface anchor | 2,168 | 36 irregular convex paving stones over packed bedding; level walking tops |
| S06 stone wall cap | X=0..4, Y=0..0.25, Z=−0.25..+0.25 | 264 | Six independent broad capstones; not a monolithic stone lintel |

Blender uses X across, Z up and Y depth. FBXs bake axis conversion into native Unity XYZ with identity hierarchy transforms and metre scale. Bounds/colliders follow the catalog's full spans. Wall and pier stack rise is exactly 3 m; the cap adds 0.25 m. Stones have simple shallow bevels and flat faces, keeping the approved broad painted look. Recessed packing prevents through-gaps without coplanar exposed faces.

## Surface and UV mapping

The source uses an exact copy of the approved `Assets/Textures/Rock/Presets/PaintedStone_Albedo.png`, saved as `Assets/Textures/Buildings/Stone/PaintedStone_Albedo.png` so building imports are self-contained. Its source is 1254 × 1254 pixels. This is the joint-free stone-face approach proposed for individually modeled stones. The earlier labelled trim design board remains a design reference, not an applied texture; its printed joints, labels and legend are not used on models.

S03/S04/S06 retain one material/submesh, `Assets/Materials/Buildings/Stone/PaintedStone.mat`, using `Custom/BuildingWoodMatte`. S02 now uses its own `BakedStoneWall.mat` with `Custom/BakedStoneWallMatte`, supporting tangent-space normal maps and ambient occlusion while retaining the established diffuse, day/night, shadow, local flame, cave and fog response. Both material paths remain matte and instanced.

Each stone face, bevel, underside, recessed core face and bedding face has UV0. Planar projection chooses a nondegenerate face plane and uses the same physical UV scale along both projected axes. Deterministic seeds vary crop offset, scale, axis swap and mirroring per stone and face. UVs stay inside the tile with a minimum 3.5% border; texture wrapping is Clamp. No triangle has collapsed UV area. Each component has three exported UV layouts with identical geometry and bounds. Existing position/heading hashing selects a stable layout; removing and replacing neighbours does not reshuffle its appearance.

## Placement and collision

- Stable IDs: `build.stone.s02-coursed-wall`, `build.stone.s03-pier`, `build.stone.s04-flagstone-floor`, `build.stone.s06-wall-cap`.
- S02/S06 use the existing full-span wall sockets; S03 is a centred post; S04 is a floor. No new enum values or automatic wooden junction covers are introduced for the stone walls.
- Stone definitions set `preserveAuthoredMesh`. Fitting retains their FBX visuals and UV layouts, rather than replacing paving or wall bearing with timber-generated geometry. Stone surfaces retain their complete logical box colliders; building intersections follow the existing permissive warning policy.
- S04 sets `requiresFullBearing`: its complete footprint needs supported foundation coverage immediately beneath its underside. Multiple aligned foundation boxes may collectively supply coverage. Side contact, half-supported cantilevers and timber-only framing cannot support this paving. Put it over the existing foundation/bedding; it cannot begin directly on terrain or become a suspended upper floor.
- Full-bearing rules are applied to preview and committed support graphs. Removing the supporting foundation disconnects its paving. Existing wooden-only structures retain their original support traversal.
- Pier adjacency uses the actual two post widths, fixing the old hard-coded 0.25 m spacing for a 0.50 m stone pier. Wooden post spacing remains 0.25 m.
- The four authoring prefabs and runtime proxies use simple continuous BoxColliders independent of the visual joints and bevels. S04 has no stairwell cutting; use the existing timber floors for upper access.

S03 is optional freestanding masonry bearing. Its three-metre body is not automatically inserted at wall junctions. Detailed timber beam seats, stone junction dressing and architectural stability simulation are not added in this pass.

## Rebuild and import

1. Run Blender with `--background --factory-startup --python Tools/BuildStoneKit.py` from this repository. The script regenerates its own stone source, reports, twelve FBXs and render previews. Preserve later manual edits before rerunning this generator.
2. Run Blender with `--background --python Tools/BakeStoneWall.py` to regenerate the separate baked S02 exports and atlases. In Unity, choose **Tools → Building → Rebuild S02 S03 S04 S06 Stone Kit**. This selects the baked S02, links imported FBX subassets directly, preserves stone definition/prefab GUIDs on rebuild and bakes four actual-mesh thumbnails. Use **Rebuild Baked S02 Stone Wall** for a wall-only update.
3. The general **Rebuild Full-Span Modular Kit** pipeline also discovers and includes the stone models. The dedicated stone command is preferable when only these assets need rebuilding because it does not rebuild wooden assets.
4. Restart Play mode to load the extended catalog. Stone pieces appear under Walls, Posts and Floors; the total option count also includes the installed wooden extensions.

Importer settings retain authored normals, one submesh, identity transforms, no embedded material import and disabled runtime mesh readability. All new assets and their Unity metadata are saved in the main project; importing does not require Blender to be running.

## Validation

`Tools/ValidateBuildingPrototype.ps1 -StoneChecks` imports and checks the models in an isolated Unity project, then exercises actual Play-mode placements/support/proxies. The final `-StoneChecks -InstalledAssets` run passed **118,355 checks** against the main project's installed assets without rebuilding them: every imported UV triangle and face normal, all three variants per part, bounds, prefab references, thumbnails, eight headings, wall/cap stacking, stone pier adjacency, paving support/removal, four-footing coverage and rejection of unsupported paving. [Saved result](../ArtReferences/StoneBuildKit/Models/unity-validation.json).

`Tools/ValidateBuildingPrototype.ps1 -InstalledAssets` passed **32,376 checks**, including **192 real CharacterMotor stair walks**, against the installed assets without rebuilding them first. [Saved regression result](../ArtReferences/StoneBuildKit/Models/building-regression-validation.json). Validation uses temporary synthetic scenes; it does not open or rewrite `SmearScene.unity`. The validation harness now respects nested assembly-definition source boundaries, including the separate profiling assembly.
