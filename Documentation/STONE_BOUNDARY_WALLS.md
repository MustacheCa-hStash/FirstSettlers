# S07 low rubble wall and S12 empty gate bay

S07 and S12 extend the painted stone kit with directly terrain-bearing boundary walls. They are 4 m wide, 1.25 m high and 0.50 m thick. No foundation is needed when their masonry feet meet terrain.

- [Model overview](../ArtReferences/StoneBuildKit/Boundary/S07-S12-overview.png)
- [Editable Blender source](../ArtReferences/StoneBuildKit/Boundary/StoneBoundaryKit.blend)
- [Blender geometry and UV inspection](../ArtReferences/StoneBuildKit/Boundary/blender-models.json)
- [Earlier stone kit and rebuild pipeline](STONE_BUILD_KIT.md)

## Models and mapping

S07 uses 30 broad irregular stones in four staggered courses over a recessed packing core: **852 triangles**. S12 uses two independent masonry wings, ten stones and one packing core per wing: **584 triangles**. The shallow clipped corners and quiet painted mineral patches keep the established simplified art direction.

S12 deliberately has **no wooden gate, posts, pivots or lintel**. Its empty opening runs from X=1.375 to X=2.625, giving 1.25 m clear width, and is open from the base upward. The whole stone assembly is 1.25 m high. Each wing is 1.375 m wide. The aperture is clear in the visual mesh, placement solids, authoring prefab and pooled runtime collision.

Every surface, including backs, ends, tops, undersides and packing, has planar UV0. The same seeded crop/scale/mirroring approach as the earlier stone kit provides a distinct crop per stone and three deterministic layouts per model. Each projected face uses equal physical scale on both axes. All UV triangles are noncollapsed and stay within the padded sampling area of the approved joint-free painted stone albedo. No labelled trim-preview pixels are used.

`Tools/BuildStoneBoundaryKit.py` rebuilds only this extension's source, six FBXs and previews; it does not rewrite the earlier stone or wooden source files. Assets live beside the earlier stone FBXs under `Assets/Models/Buildings/Stone` and share `PaintedStone.mat`.

## Terrain bearing

The recommended approach is an explicit **canGroundOnTerrain** setting on the two boundary definitions, while keeping their kind as **Wall**. This permits boundary walls to start on soil and propagate building support, without granting every wall, post, cap or paving slab the same grounding behavior. The earlier four stone pieces retain their existing support rules.

Terrain placement snaps the bottom **down** to the quarter-metre grid, allowing shallow seating up to 0.25 m instead of hovering at the next grid height. Terrain contact accepts `TerrainCollider` and explicitly marked `WorldGroundSurface` surfaces. Rock/prop/player obstructions retain their existing checks.

S07 tests its actual base. S12 tests each masonry wing independently: both need foot contact for the assembly to ground itself. Soil beneath the empty gate opening cannot ground its wings, and one grounded wing does not ground the other. Once grounded, the supported wall can carry an S06 cap or connect to other building pieces normally.

The rigid pieces stay level; this is not terrain deformation or automatic slope-following geometry. On markedly uneven ground, step boundary sections using the existing 0.25 m vertical nudges or prepare a level route. A sufficiently high opposite bank can prevent both S12 wings from seating in the same shallow bearing band.

## Unity integration

IDs: `build.stone.s07-low-wall` and `build.stone.s12-gate-bay`. Both are under **Walls**, use a 1.25 m stack rise and retain authored meshes. S12 declares two solid boxes and an aperture; sockets inside that aperture are omitted. Its authoring prefab and runtime proxy use two separate colliders. S07 uses one box collider. Neither creates automatic timber joint posts.

Run **Tools → Building → Rebuild Stone Kit Including S07 S12** to update stone definitions, prefabs and actual-mesh thumbnails. The earlier stone menu command remains available and uses the same importer. The general building rebuild also includes these pieces. Restart Play mode to reload the extended catalog.

## Verification

`Tools/ValidateBuildingPrototype.ps1 -StoneChecks -Workspace stone-boundary` runs import and Play-mode checks in a separate temporary project. Use `-InstalledAssets` as well to verify the main project's installed assets without rebuilding them. The workspace option keeps separate validation runs from sharing a temporary Unity checkout.

The suite inspects every imported UV triangle/normal across all eighteen stone FBXs and exercises direct placement on a real TerrainCollider in eight headings, shallow grid seating, floating rejection, independent gate-wing support, exclusion of ground under the gap, external prop checks, compound collision, wall-cap support and sixteen actual CharacterMotor gate crossings. It also retains the earlier four-piece support and placement checks.

The import and final installed-asset runs both passed **145,246 checks**. The existing installed-building regression suite passed **33,551 checks**, including **192 real CharacterMotor stair walks**. Results are saved beside the source as `unity-validation.json` and `building-regression-validation.json`.
