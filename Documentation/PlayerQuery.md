# Player crosshair query and target resolution

`PlayerQuery` is attached to `PlayerCharacter/Interaction` in SmearScene. Its explicit
camera reference is the existing Main Camera under ViewPivot. Reach defaults to 5 world
units and is adjustable in the Inspector.

## Sampling and filtering

The component samples in LateUpdate with execution order 100, after the existing input,
look and movement Update. It uses the center of the camera viewport, independent of the
mouse cursor, screen resolution and field of view. The ray begins on the camera near
plane; reach and hit distance are measured from there. Camera projection changes,
including an orthographic view, use that camera's viewport ray.

Each sample performs two closest-hit casts:

- WorldSolid with triggers ignored, for physical surfaces and obstructions.
- QueryOnly with triggers included, for additional identification shapes.

The nearest result wins; a solid wins an exact tie. A wall in front of a query shape
blocks it. Ordinary WorldSolid trigger volumes do not obstruct the view query. Player,
Default, Water and other layers are excluded. All colliders owned by the local character
must keep the Player role; future held-item colliders also need appropriate self exclusion.

The same two casts also expose `SolidHit` and `QueryOnlyHit`. `Current` applies their
nearest-hit/obstruction rule for display. These separate role results are raw: QueryOnlyHit
can be behind a wall, so use Current for identification UI. Future tool hit checks can
inspect SolidHit to require contact with a physical trunk, without adding a third cast
or treating a canopy hit as a valid chopping contact. Breakable describes the target's
capability; the hit geometry still determines which parts a particular action may affect.

This query does not move the camera, character or crosshair. It samples every rendered
frame without smoothing or timed delays. It does not call Physics.SyncTransforms; the
systems that change gameplay collider transforms own that synchronization, as the tree
gameplay manager already does. Future camera motion in LateUpdate must run before this
component, or its owner can schedule Sample() after applying the final view pose.

The two casts return single RaycastHit values without allocating hit arrays. They avoid
the unsorted/truncated results of a bounded multi-hit buffer. There are no per-object
Update methods on target providers or scene/chunk scans. Target resolution walks only
the hit collider's ancestors. Profile the sample under `FS.PlayerQuery.Sample`.

## Result and scope

`PlayerQuery.Current` is a value containing HasHit, Collider, Point, Normal, Distance and
Kind (Solid or QueryOnly; None on a miss). It refreshes every frame, including misses.
Disabling the component, disabling/unassigning its camera or setting reach to zero clears
the result. HasHit also checks whether its collider still exists, is enabled and is active.

The closest hit now resolves the nearest `QueryTarget` provider on the collider object
or an ancestor. `Current.HasTarget` reports whether that identity is still valid;
`Current.Target` contains Source, DisplayName, optional Sprite Icon, Capabilities and Data.
Use `Target.TryGetData<T>(out T data)` to access typed data and
`Target.HasCapability(QueryTargetCapabilities.Breakable)` for capability checks.

Existing tree trunk hits resolve `TreeGameplayProxy`, which is itself a QueryTarget.
Its Data is the actual TreeRecord from the world registry, including its stable TreeId,
placement and current state. Species names come from placement data. Trees remain
represented by one pooled gameplay object, with no extra target GameObject or Update.
TreeGameplayAuthoring on the visual source prefab optionally overrides Query Display Name
and Query Icon; the manager copies those references into its cached gameplay template.
Restart Play mode after changing those template settings.

Generated rocks receive RockQueryTarget automatically on the instance root. It returns
RockQueryData, containing that instance's RockInstanceData placement (variant, prefab
index and local transform). Default display name is Rock and Breakable is enabled. This
is per-instance metadata, not a persistent rock registry or a fabricated network ID.

An unnamed solid such as terrain still returns HasHit but no target. It blocks the
query behind it. Disabled target providers do not resolve, and the nearest provider owns
its descendant shapes even when disabled. A Solid result can be a named, breakable target.
Providers carry an identity version; rebinding/unbinding a pooled tree or reinitializing
a rock invalidates older snapshots. Tree snapshots also become unavailable when their
record leaves Standing state, even before the manager releases the collider. Read current
results rather than keeping a GameObject reference as a persistent identity.

The query also returns an optional shared `WorldObjectDefinition` datacard and its
short description. The spruce source prefab is linked to `SpruceTree.asset`, and the
local player's query HUD shows its icon, name and description. Providers without a
datacard retain their existing names/icons and return an empty description. See
[QueryHUD.md](QueryHUD.md) for asset paths, data flow and appearance settings.
Action input and damage remain future stages. Trees can be hit through their active
physical trunks or assigned canopy query colliders. TreeGameplayAuthoring.QueryCanopyColliders
and the manager's canopy distance settings configure those shapes; see
[TreeRegistry.md](TreeRegistry.md#preparing-a-query-canopy). Ray queries detect collider surfaces, so this is not
pixel-accurate mesh picking; a ray starting inside a collider also does not detect that
collider. Large foliage volumes may eventually need a separate inside-volume policy.

Only the local player should have this component enabled in multiplayer. View detection
is local presentation data. Future interaction requests need their own authoritative
validation; these transient collider references are not network identities.

## Testing in SmearScene

1. Enter Play mode and select PlayerCharacter/Interaction.
2. Look at terrain, a rock or an active tree trunk within reach. The Inspector shows
   Current Geometry Hit, including the collider reference and hit distance. The Resolved
   Target section shows name, icon, capabilities, provider, and tree ID/state or rock variant.
   Solid Collider and Query-only Collider show the independent results from the two casts.
3. Turn away or put an object out of reach. Has Hit clears immediately on the next sample.
4. In Scene view with Gizmos enabled, selection shows a green ray ending at a hit, or a
   yellow ray extending to the maximum distance on a miss.
5. To test a separate shape, place a trigger collider on QueryOnly within reach. It can
   be detected even though QueryOnly produces no physical contacts. Put a WorldSolid wall
   between it and the camera to check obstruction. A query shape under a target root
   resolves that same target; a standalone shape only reports geometry.

Objects placed directly in the scene need WorldSolid on each physical collider object.
Generated terrain, rocks and tree proxies already receive that assignment automatically.

For a directly placed rock, add RockQueryTarget to its root, set Display Name and optional
Icon, and keep Breakable enabled for the current design. Its descendant colliders resolve
that root without explicit per-collider links. It is also possible to disable Breakable
later while preserving naming and physical collision. Generated rock prefabs may author
the same component to override their name/icon/capabilities; the spawn code preserves it
and binds the generated placement. Source prefab assets are never modified by spawning.

For future object types, implement QueryTarget.TryGetInfo on their own provider. Return
their identity/data through the same QueryTargetInfo contract. Physics layers do not
define object type or breakability. Explicit external collider links can be added when
a future hierarchy requires them; current physical and query children share a target root.

Run Tools > Terrain > Validate Player Query, or batch mode with
`-executeMethod PlayerQueryValidation.RunBatch`. Validation covers closest-hit selection,
solid obstruction in dense query geometry, ignored physical triggers/player colliders,
explicit query triggers, camera pose/projection changes, range and lifecycle clearing,
zero managed allocations across 1,000 warmed samples, and SmearScene references. It
creates temporary fixtures and does not save scene changes. This is a correctness and
allocation check, not a representative forest frame-time benchmark.

The query validation also runs QueryTargetValidation: tree registry identity, optional
icons, child query shapes sharing a target, solid obstruction, pooled proxy reuse,
generated rock variants, authored scene metadata, capability overrides, and zero managed
allocations across 1,000 warmed samples with actual tree and rock targets. The focused
menu is Tools > Terrain > Validate Query Targets.

Unity API references: [ViewportPointToRay](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Camera.ViewportPointToRay.html),
[Physics.Raycast](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics.Raycast.html),
[LateUpdate](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.LateUpdate.html).
