# Gameplay collision layers

Layers are assigned in `ProjectSettings/TagManager.asset` and mirrored by
`Assets/Scripts/WorldManager/GameplayLayers.cs`.

| Layer | Index | Current role |
| --- | --- | --- |
| WorldSolid | 8 | Generated terrain, physical tree trunks, all rocks and solid bush colliders. Use for placed floors, walls and other solid surfaces. |
| Player | 9 | CharacterController and physical child colliders owned by CharacterMotor. |
| QueryOnly | 10 | Identification/query shapes. No physical contacts with any layer. Author as triggers. |

`SolidSurfaceMask` includes WorldSolid. The motor's grounding and
crouch headroom probes use this explicit serialized mask and ignore triggers. They
exclude Player and QueryOnly. CharacterController.Move continues to use the physics
collision matrix, which permits Player contacts with WorldSolid. The collision
matrix disables only QueryOnly's contacts; other existing layer pairs remain unchanged.

Runtime ownership:

- `ChunkRuntime` assigns its terrain root WorldSolid.
- `TreeGameplayManager` assigns physical trunk proxy roots and collider-bearing children
  WorldSolid, independently of source-prefab layers. Collider dimensions, materials and
  authored layer overrides still come from the tree's collider templates.
- `ChunkFoliageRuntime` assigns spawned rock roots and non-trigger collider children
  WorldSolid. This covers every current rock placement variant and fallback.
- Solid spawned bush colliders use WorldSolid.
- `CharacterMotor.Awake` assigns its root and physical child colliders Player.

Runtime assignments do not alter source prefab assets. Trigger children retain their
explicitly authored layers. Rendering culling masks must include any layer carrying both
a collider and renderer; SmearScene's camera masks already include all layers.

## Authoring objects placed directly in a scene

Use **WorldSolid** on physical collider-bearing objects, including rocks, tree trunks,
floors, walls and terrain. For a future query-only
shape, use **QueryOnly** and **Is Trigger on**. Unity layers belong to individual
GameObjects; changing the parent alone does not necessarily update a collider on a child.

The existing tree-prefab setup can stay on Default because pooled trunk proxies receive
WorldSolid automatically. Generated rock prefabs can also stay on their current layers;
spawned instances receive WorldSolid automatically.

## Target identity and capabilities

Layers describe collision and query filtering roles. Object type, display name, surface
audio and capabilities such as breakability belong to components or data. Trees and rocks
share WorldSolid because their physical colliders have the same role. A breakable tree
does not require a new layer. Fixed and breakable rocks can also share this role.

PlayerQuery resolves a hit collider (or its parent/proxy) to its QueryTarget provider
and data before treating it as an unnamed obstruction. A WorldSolid hit may be
a named, breakable rock or tree. For trees, the gameplay proxy provides the stable TreeId
used to resolve registry data. Additional canopy identification shapes can use QueryOnly
and resolve to that same tree record. The query layer itself does not supply target identity.

Generated rocks now receive RockQueryTarget with breakability enabled by default.
Their data and physical layer are independent. Damage and destruction remain future
stages. A separate shared breakable layer would be justified only by a concrete filtering
requirement; none is needed currently.

The player geometry query now samples WorldSolid and QueryOnly independently; see
[PlayerQuery.md](PlayerQuery.md). TreeGameplayManager copies assigned canopy templates
to QueryOnly triggers and activates them independently from physical trunks. An explicit ray layer mask with
`QueryTriggerInteraction.Collide` can hit
QueryOnly triggers even though physical contacts for that layer are disabled; validation
checks this in the project's actual physics configuration.

## Validation

Run **Tools > Terrain > Validate Gameplay Layers**, or Unity batch mode with
`-executeMethod GameplayLayerValidation.RunBatch`. It validates the saved layer indices
and collision matrix, WorldSolid slope detection and headroom, Player and
QueryOnly exclusion, query trigger ray access, player collision against a breakable rock,
spawned rock variants/terrain assignments, existing tree gameplay tests, and SmearScene's
serialized player mask/camera visibility. Fixtures do not save scene or prefab changes.
