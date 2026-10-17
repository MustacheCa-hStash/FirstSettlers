# F01–F06 furniture: Blender source and building integration

First-page furnishings use six static single-material FBX meshes, authored compound collision, stable building IDs and actual-mesh catalog thumbnails. F05 uses its baked matte material; the other five use the shared matte wood material. The existing enum values remain unchanged; Furniture is appended as value 7.

## Assets

- Editable source: [FurnitureKit.blend](../ArtReferences/IronAgeFurniture/Models/FurnitureKit.blend), with separate editable construction parts and hidden joined exports.
- [Blender overview](../ArtReferences/IronAgeFurniture/Models/furniture-kit-overview.png) and [model/UV/collision manifest](../ArtReferences/IronAgeFurniture/Models/furniture-models.json).
- FBXs: `Assets/Models/Buildings/Furniture/`.
- Definitions and authoring prefabs: `Assets/Resources/Building/f01-…` through `f06-…`.
- Icons: `Assets/Resources/Building/UI/Thumbnails/build_furniture_f0…png`.

Dimensions below are Unity X width/length × Y height × Z depth, in metres. F01–F05 roots are at base-centre. F06's root is rear-centre at its upper shelf surface; it projects along local +Z, with brackets below Y=0.

| ID | Catalog item | Dimensions | Triangles | Content ID |
| --- | --- | --- | --- | --- |
| F01 | Three-leg stool | 0.50 × 0.50 × 0.50 | 296 | `build.furniture.f01-three-leg-stool` |
| F02 | Plank bench | 1.50 × 0.50 × 0.50 | 276 | `build.furniture.f02-plank-bench` |
| F03 | Pegged table | 1.50 × 0.75 × 0.75 | 628 | `build.furniture.f03-pegged-table` |
| F04 | Low work table | 1.00 × 0.50 × 0.75 | 628 | `build.furniture.f04-low-work-table` |
| F05 | Sleeping platform | 2.00 × 0.50 × 1.00 | 320 | `build.furniture.f05-sleeping-platform` |
| F06 | Wall shelf | 1.00 × 0.25 × 0.25 | 212 | `build.furniture.f06-wall-shelf` |

F01 has a split oval seat and three splayed legs. F02 has split seat boards, slab supports and a pegged stretcher. F03/F04 have four split top boards, end frames, aprons, legs and a stretcher. F05 has separate corner posts/rails and 56 double-faced cord strips with real visible gaps; its usable deck is 1.80 × 0.90 m. F06 has a board, two back brackets, diagonal braces and small wooden pegs. The original 1,472-triangle F05 remains the bake source; see [baked furnishings](BAKED_FURNISHINGS.md) for the installed model and current validation.

These are the bare first-page pieces. Bedding, storage props and interactive sitting/sleeping behavior belong to subsequent work.

## Surface and collision

Every face, including hidden caps and bevels, has non-degenerate UV0. Long faces follow timber grain; caps use end grain. F01–F04 and F06 use the shared `SplitPlankWood` material. F05 uses a 1K colour/AO and normal pair baked from the original wood and lashing atlas. Each mesh has one material submesh and retains the project's fog, shadows, night and cave-light response.

The authored mesh is preserved by resolution; no timber bearing geometry, automatic posts or wall sockets are generated for furniture. Runtime rendering stays in the existing instanced building renderer. Collision uses pooled compound boxes around the actual seat/top, legs, slab supports, stretcher, bed deck/frame or shelf braces. Spaces beneath tables and benches remain open. Rounded/slanted surfaces use conservative box approximations rather than individual visual triangles. The bed has a thin continuous walking collider over its cord deck so the character cannot fall between small visual gaps.

## Placement and support

The Furniture category contains the six pieces, each with a rendered thumbnail, name, dimensions, description and quick-piece entry. Existing staged confirmation, rotation, removal and grid nudge controls apply. Ground and floor placement retain the 0.25 m increment and eight yaw headings.

F01–F05 can ground directly on terrain, with every authored foot checked. They can stand on supported foundations or floor sections; each foot must touch a real resolved top surface. A furniture envelope overhanging an edge is permitted only when all its feet remain supported. Aim at the floor's top, not a neighboring wall or another piece of furniture. Ground aiming does not acquire a nearby structural piece's snap.

F06 aims at the side face of a supported wall or post. Its rear mounting points must all meet a real solid section; window/door apertures do not provide support. The shelf preserves a timber wall's actual 0.125 m face offset, using a persistent surface frame rather than rounding that offset to 0.25 m. The shelf's tangent/elevation still use the quarter-metre grid. Normal-direction nudges or rotations that detach its rear mounts are invalid.

Furniture receives support after the structural graph resolves. It does not enter that graph and cannot carry walls, floors, stairs, roofs, other furniture or a bridge between structural islands. Structural aiming at furniture is rejected. Losing a supporting floor/wall makes the furnishing unsupported; the existing building world's collapse grace period applies. Terrain grounding is latched at placement, following the existing terrain-edit policy.

Build-piece intersections retain the current building system's advisory warnings. Furniture does not introduce a new general prohibition against interpenetrating build pieces. External objects/player obstruction checks use its shaped solids.

## Rebuild

Run from the repository root:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.0/blender.exe' -b -t 4 --python-exit-code 1 --python Tools/BuildFurnitureKit.py
& ./Tools/ValidateFurnitureKit.ps1 -Install
```

The Blender script writes the separate editable source, six joined FBXs, preview and authoring manifest. It does not edit the existing building source file or wood texture. The validation command prepares an isolated Unity project, installs/bakes there, checks imported assets/UI/runtime behavior, and copies only the six new definitions/prefabs/icons, model metadata and updated catalog back after success. It detects a concurrent main-catalog change before overwriting it. Existing scene files and the shared material/texture/shader are hash-checked.

Inside Unity, **Tools → Building → Install F01-F06 Furniture** updates the six definitions/prefabs and bakes their thumbnails without saving the scene. Rebuilding the full building kit also includes the furniture when its FBXs and manifest are available. The importer disables imported materials, keeps authored normals and UVs, bakes axis conversion and verifies identity transforms and the expected bounds.

The catalog's content IDs and prefab/definition metadata remain stable on rebuild. Furniture records use the existing frame/anchor/yaw/health/definition-ID fields; no alternate save representation is introduced.

## Validation

Runtime/editor compilation and the isolated Unity import/UI/runtime suite passed **13,115 checks**, including all imported triangle UVs/normals, six definitions/prefabs/icons, 320 floor/rotation cases across eight headings, terrain grounding, per-foot support, timber wall half-thickness mounting on both faces, window void rejection, duplicate rejection, serialized identity/pose, structural isolation, support removal and actual pooled table colliders. Menu filtering/selection/layout passed at 1280×720, 1920×1080, 3840×2160 and 1024×768. See the [report](../ArtReferences/IronAgeFurniture/Models/unity-validation.json) and [720p Furniture menu](../ArtReferences/IronAgeFurniture/Models/furniture-1280x720.png).

The isolated run required normal desktop access to Unity's local licensing service; the sandboxed batch process failed before asset installation. Scene files and the shared material, wood texture and shader matched their pre-install hashes. The installed catalog retains its nineteen existing entries and adds six furnishing entries.

The existing installed-asset regressions also passed against that 25-piece catalog: [modular wood](../ArtReferences/IronAgeFurniture/Models/modular-regression.json), 33,551 checks and 192 actual-motor stair traversals; [stone](../ArtReferences/IronAgeFurniture/Models/stone-regression.json), 145,252 checks and 16 actual gate traversals, including paving full bearing/removal and all eight headings. These were separate `ModularWoodValidation.RunInstalledBatch` and `StoneKitValidation.RunInstalledBatch` runs in the same isolated project. The furniture installer was run twice successfully to verify that catalog additions and asset identities remain stable.

After Unity imports the new files, restart Play Mode and choose **Build → Furniture**. The six pieces are immediately available through the existing placement controls. The saved world scene was not populated with furniture.
