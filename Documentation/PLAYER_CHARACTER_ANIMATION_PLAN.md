# Player character, modular appearance, and animation plan

Date: November 2, 2026. Status: proposed architecture; no runtime implementation or scene changes in this task. Target editor: Unity 6000.4.8f1, URP 17.4.0. No root motion.

## 1. Decisions

1. Keep the existing character root, movement controller, camera ownership, and query system. A cosmetic/model change must not replace the player or its gameplay state.
2. Promote the successful v02 Mixamo skeleton to the first canonical human rig after a Unity import/deformation check. Compatible body, clothing, and cosmetic variants use that rig.
3. One Animator and one skeleton evaluate the character pose. Clothing does not get its own Animator. Rigid attachments follow bone sockets.
4. Keep an Animator Controller for locomotion. Use a small, manually evaluated Playables graph to mix that controller with timed action clips. The action runtime supplies action clip time and blend weight explicitly.
5. Gameplay action state authorizes actions and owns their clock, cancellation, cooldown, and hit bookkeeping. Animation Events never directly apply tree damage or inventory changes.
6. Clip-authored contact markers open a hit opportunity. A fresh, obstruction-aware tool contact query must still validate the hit. A marker is not proof that a tree was touched.
7. Implement one complete vertical slice first: v02 + one axe + in-place locomotion + one chop + physical trunk validation + a second interchangeable appearance.

The Playables choice is deliberate: ordinary Animator transitions are good for locomotion, but independently running a gameplay timer and a freely transitioning action state would introduce two clocks for the same impact. We will use one clock and explicitly sample the action pose from it. This is a small animation driver, not a new general-purpose animation framework. Unity supports mixing an Animator Controller with clips in a PlayableGraph ([Unity example](https://docs.unity3d.com/6000.0/Documentation/Manual/Playables-Examples.html)).

## 2. What exists today

| Existing piece | Observed behavior | Integration decision |
| --- | --- | --- |
| `CharacterMotor` | Owns CharacterController movement, gravity, grounding, stance/headroom, terrain movement guard; publishes ActualVelocity, IsGrounded, IsCrouched and GroundNormal | Remains the movement authority; expose a small locomotion snapshot and accept optional action movement restrictions |
| `LocalPlayerInput` | Sole reader of hardware; applies look and calls motor; routes building/sculpting/menu controls | Add semantic action command submission and apply action movement policy; do not put hit/damage code here |
| `FirstPersonLook` | Yaw rotates the character root; pitch rotates ViewPivot | Keep this ownership; animation must not move the camera or rotate the gameplay root |
| `PlayerQuery` | Two casts in LateUpdate order 100; Current is obstruction-filtered for HUD; SolidHit and QueryOnlyHit are separately available | Use it to discover intent after the current frame's view update; perform independent short-range contact validation at impact |
| `LocalPlayerMenus` | Owns menu/cursor state and an empty hotbar's SelectedHotbarIndex | Keep menu gating; move real selected-slot ownership into PlayerInventory when inventory state exists |
| `TreeGameplayProxy` | Pooled QueryTarget with TreeId, TreeRecord, explicit physical trunk and canopy collider lists | Resolve physical trunk contacts through this provider and retain stable TreeId, not a pooled GameObject reference |
| `TreeRegistry` / `TreeRecord` | World data with stable IDs and Standing/Cut/Fallen/Removed; TrySetState updates state | Add a world-owned harvest/damage service; HP, yields, durability transactions and falling-tree gameplay do not exist yet |
| `LocalFlameHeldTorch` | Development offhand fixture; its LateUpdate order 300 positions a prop relative to the camera and handles wall obstruction | Replace camera-follow placement with an equipment socket; preserve light/occlusion behavior and avoid a second independent equip state |
| Old Vincent player | Separate script reads input, moves a CharacterController and writes move X/Y Animator parameters | Treat as a reference for existing clips, not the player architecture to extend |

The saved SmearScene already has these placeholders:

```text
PlayerCharacter                         CharacterController + CharacterMotor
├── LocalControl                        LocalPlayerInput, menus/build/sculpt owners
├── ViewPivot                           FirstPersonLook
│   └── Main Camera
├── Visuals
│   ├── RigAndAnimator                  currently empty
│   └── EquipmentSockets                currently empty
├── Audio                               currently empty
├── Interaction                         PlayerQuery
└── UI
```

The saved scene's query reach is **8 m**, despite the query script's 5 m default and the older documentation's default description. Neither is axe reach. The motor's scene settings are 5 m/s walk, 8 m/s sprint, 2.25 m/s crouch and 1.8 m standing height.

The repository contains old idle/walk/run/sprint/strafe/jump/landing FBXs and PlayerAnimator.controller. They are candidates for retargeting and cleanup, not verified v02-ready animations. The successful Mixamo result has not been validated as a Unity rig by this planning task.

Relevant current files: [motor](../Assets/Scripts/PlayerMovement/CharacterMotor.cs), [input](../Assets/Scripts/PlayerMovement/LocalPlayerInput.cs), [look](../Assets/Scripts/PlayerMovement/FirstPersonLook.cs), [query](../Assets/Scripts/PlayerMovement/PlayerQuery.cs), [target identities](../Assets/Scripts/PlayerMovement/QueryTarget.cs), [query design](PlayerQuery.md), [inventory plan](INVENTORY_PLAN.md), [tree registry](TreeRegistry.md).

## 3. Runtime ownership and hierarchy

```text
PlayerCharacter                         stable gameplay actor; never swapped for clothing
│  CharacterMotor
│  CharacterInventory / CharacterEquipment
│  CharacterAppearance
│  CharacterFrameCoordinator
├── LocalControl                        hardware/menu adapters only
├── ViewPivot / Main Camera             camera remains independent of animated head
├── Visuals                             CharacterVisualController + CharacterAnimationDriver
│   ├── RigAndAnimator
│   │   └── HumanRig instance            ONE Animator, Avatar and bone hierarchy
│   │       ├── modular body renderers
│   │       ├── clothing renderers
│   │       └── bones / real socket transforms / rigid attachments
│   └── EquipmentSockets                socket registry/binding component
├── Interaction                         PlayerQuery, CharacterActionController, ToolHitResolver
├── Audio                               cosmetic audio/VFX presentation
└── UI                                  views of existing gameplay/appearance state
```

Names are proposed component responsibilities; they need not all become separate files before the first slice works. Use explicit serialized/bootstrap references, not scene searches or bone-name searches every frame.

| Owner | Owns | Does not own |
| --- | --- | --- |
| CharacterMotor | Root translation, capsule, stance and physical movement | Clip time, clothing, tool damage |
| FirstPersonLook | Root yaw and ViewPivot pitch | Head-bone transforms or root motion |
| PlayerInventory | Actual carried slots, item instances, armor/offhand and active hotbar slot | Instantiated hand models |
| CharacterEquipment | Derived equipped-item snapshot and revision | A duplicate item store |
| CharacterAppearance | Body/skin/hair/beard choices and cosmetic overrides | Tool power, durability, reach or damage |
| CharacterVisualController | Rig/part instances, materials, sockets and visibility | Inventory transactions and damage |
| CharacterActionController | Accepted action instance, source-time progress, restrictions, cancellation, marker consumption and recovery | Physics movement and tree state mutation |
| CharacterAnimationDriver | Pose evaluation and clip blending from snapshots/time | Starting actions from raw input or committing hits |
| ToolHitResolver | Validated physical contact result | Selecting cosmetics or playing a second action clock |
| TreeHarvestService | HP/hit progress and tree outcomes by TreeId | Character animation state or per-tree Update methods |

Disabling/replacing a visual must not destroy inventory, the motor, target identity, or cooldown state. The equipment/appearance data outlives its current renderers.

## 4. Modular meshes and rig compatibility

### First human rig family

Create `HumanSettlerRigV1` from the approved Mixamo rig. Freeze hierarchy, rest transforms, unit scale, joint orientation and reference mesh space after testing. Canonical local coordinates are Unity Y-up, feet at y=0, forward +Z, scale 1, with a 1.8 m reference body. Correct FBX axis offsets once in the authored visual prefab, not separately on each outfit.

Compatible variants may have different vertices, topology and materials. They still need weights/bind poses authored for this skeleton and garment fit. Changes to skin tone, hair or a shirt must not reconstruct the skeleton. Do not auto-rig every outfit independently in Mixamo.

Humanoid retargeting lets a different valid Avatar use the animation library; it does **not** make an arbitrary shirt's skinning compatible with that Avatar ([retargeting](https://docs.unity.com/en-us/engine/6000.3/manual/animation-section/animation-mecanim/avatar-creationand-setup/retargeting)).

### Part assignment

Each `ModularPartDefinition` records slot, rig family, fit family, renderer prefab/mesh, materials, ordered bone bindings, bounds and covered body regions. Bone references are resolved once when equipping. Preserve the mesh's bone-index order when assigning SkinnedMeshRenderer.bones; match its authored bind poses and coordinate space. Setting sharedMesh alone is insufficient. Unity's bind pose at an index corresponds to the bone at that same index ([bind poses](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/mesh/bindposes)).

An editor validator compares a rig signature containing bone paths/rest transforms and mesh-space information. Bone names alone are insufficient. A garment FBX can include its authoring armature, but only its validated renderer/binding data is installed onto the live canonical rig. Missing or incompatible bones reject that part and keep the previous valid appearance.

| Part | Initial representation |
| --- | --- |
| Body/head/hand/foot variants | Skinned renderers on the canonical rig |
| Shirts, trousers, boots, deforming armor | Skinned renderers on the same rig; author fit variants where necessary |
| Short hair, short beard, rigid helmets, eyes | Rigid bone attachments initially; jaw/neck skinning when the design requires deformation |
| Long hair, long beards, skirts or capes | Skinned parts with an explicitly compatible rig extension; cloth/secondary simulation later |
| Axe, torch, shield, belt pouch | Rigid socket attachments; their gameplay definitions remain separate |

Use body-region visibility masks: Head, Torso, UpperArms, Forearms, Hands, Pelvis, UpperLegs, LowerLegs, Feet. Equipped parts declare which regions they cover, combined as a union. Initially author regions as separate meshes or validated index-buffer variants. Do not delete arbitrary vertices or scale bones to hide skin at runtime. A beard and helmet also declare compatibility/hide rules for hair; arbitrary combinations are not assumed to fit.

Small shape variants can share this rig. Different bone proportions/creatures use a complete visual-rig prefab with their own Avatar and fit family through the same visual interface. Such a swap occurs at idle, or cancels the active action before the old rig is released. The controller/capsule does not silently change because a cosmetic skin is taller.

Save stable appearance/part IDs, palette values and supported blendshape values. Do not save prefab instances, bone Transform references, or Animator state hashes as character identity. Start with a few renderers and shared materials; runtime mesh merging is a later measured optimization.

## 5. Equipment and sockets

Use semantic socket IDs such as MainHand, OffHand, Head, Back and Belt. The EquipmentSockets placeholder holds the registry; actual socket transforms are children of the appropriate bones. A prop under an unbound sibling object will not follow a hand.

`EquipmentVisualDefinition` contains the prefab, socket, local grip position/rotation, optional support-hand grip target, contact-shape descriptor and animation family such as OneHandAxe or TwoHandAxe. A rigid prop is instantiated once and remains parented while held. The animation moves the hand; the socket and grip offset place the tool. Two-handed clips must be fitted to the authored support grip; add IK later if needed, before contact sampling, rather than moving the tool independently of the hands.

The future PlayerInventory follows the existing **36 carried slots, four armor slots and one offhand slot** plan. Main hand is a view of its active hotbar item. LocalPlayerMenus should delegate selection to that inventory and observe the result; do not leave two independent selected indices. Visuals consume an EquipmentSnapshot containing slot/item-instance IDs and a revision.

For the first animation slice, a small development equipment source can supply one real test axe through the same snapshot contract. Mark it explicitly as a fixture; it is not a production inventory implementation.

Equipment changes rebuild only affected attachments/parts. Apply player/self-exclusion roles to any new colliders explicitly; CharacterMotor.Awake only handles colliders present at startup. Held tools initially have no physical collision response or OnTrigger damage path. Drops use a different world representation.

Migrate LocalFlameHeldTorch's placement to the offhand socket. Its lighting source and obstruction checks remain owned presentation behavior. T can temporarily call the development equipment source, but must not toggle a second hidden equip flag. Distinguish hiding/extinguishing a blocked rendered flame from unequipping the inventory item.

## 6. Locomotion without root motion

CharacterMotor remains the only normal movement writer. Set Animator.applyRootMotion=false; no OnAnimatorMove translation/rotation; no second CharacterController or Rigidbody locomotion on the imported character.

Publish `CharacterLocomotionSnapshot` after motor simulation: actual planar/local velocity, vertical velocity, grounded state, actual crouch state, ground normal, accepted-jump sequence, landing sequence and teleport/discontinuity flag. Accepted jump is a motor result, not Space being pressed. Stance animation uses IsCrouched so a blocked stand-up remains crouched.

Locomotion controller inputs are MoveX/MoveZ in consistent m/s, Grounded, Crouched, VerticalSpeed and accepted jump/landing signals. Use actual displacement velocity, not WASD intent, so walking into a wall does not run in place. Ground-stability smoothing may affect presentation only, not the motor's physical grounding.

Start with idle; directional walk/jog/run; crouch locomotion; jump start; airborne/fall; and landing. Use suitable forward, backward and strafe clips. The scene's 5 m/s default movement may call for a jog/run visual rather than speeding a slow walk clip several times over. Measure each clip's authored gait speed, use sensible playback-rate limits, and leave gameplay speed tuning as a separate explicit decision.

Download/use in-place clips and validate root drift in Unity. Import root rotation and XZ treatment consistently. Check vertical jump clips separately: disabling applyRootMotion does not remove an authored trajectory baked into skeletal pose, and blindly baking a jumping body's full vertical trajectory into pose can stack it on motor-driven jumping. Use jump poses/extracted travel compatible with the motor, not a second visual leap.

The camera stays under ViewPivot at motor-owned stance height, never under the animated head. View bob/shake is optional camera presentation and must not change action reach.

## 7. One action clock and clip-authored contact

### Data

`CharacterActionDefinition` contains stable ActionId, target/contact policy, movement restrictions, repeat/cancel policy, recovery/cooldown rules and allowed equipment family. `CharacterAnimationSet` binds that action/family to an `ActionMotion` entry containing the actual clip, trim range, blend envelope, full/upper-body mask and ordered markers. Keep the clip and its marker data together when selecting a variant.

Store marker time in imported-clip seconds. An editor view displays source frames/normalized phase for authoring. Changing a clip, trim range or duration flags its markers for review. Gameplay must not compare an Animator normalizedTime to one exact float, wait an unrelated hard-coded duration, or look for a particular bone keyframe at runtime.

Each accepted runtime action captures actor/run ID, resolved motion, tool instance/revision, intended target handle, source time, marker-consumption mask and recovery deadline. Lock its speed multiplier for that run initially. If speed is later changed mid-action, integrate the shared source-time clock rather than recomputing elapsed time from a new start speed.

### Playback graph

```text
AnimatorControllerPlayable (locomotion) ──┐
                                        ├── AnimationLayerMixerPlayable ── ONE Animator
AnimationClipPlayable (active action) ───┘
```

CharacterAnimationDriver owns this graph and its sole output. It explicitly evaluates it after query/input processing. Locomotion advances once per frame. The active action playable is sampled at the source time supplied by CharacterActionController; it does not independently free-run. Blend weight also comes from that same source time. Do not additionally drive this action through Animator triggers/exit-time transitions or another automatic controller output.

First chop is a full-body override with horizontal movement stopped/braked by the motor and gravity still simulated. No repeated looping clip: each chop is a distinct action instance. Upper-body use-while-moving is added only for clips tested with an appropriate mask. Do not treat a full-body Mixamo chop as automatically safe to mask above the hips.

Keep contact inside a fully weighted section: blend-in has finished before the impact, and blend-out starts afterward. Missing motion/rig/tool-contact data rejects an action; it does not silently apply timed damage without an animation.

### Illustrative chop

These timings are an example, not measurements of an imported clip:

| Source time in a 1.0 s / 30 fps clip | Meaning |
| --- | --- |
| 0.00–0.12 s | Blend in / windup |
| Frame 13 / 0.433 s | Begin the short blade-path sample preceding impact |
| Frame 14 / 0.467 s | Impact opportunity; one hit maximum |
| 0.47–0.85 s | Recovery; optionally emit cosmetic follow-through |
| 0.85–1.00 s | Blend out, then allow the next action |

The artist/designer scrubs the actual axe animation and places Impact at its intended contact pose. If the wood is not physically touched, this marker produces a miss. A later multi-contact action can author a contact window, but one axe chop initially has one gameplay impact marker.

Use crossing tests, not equality:

```text
previousSourceTime < markerTime <= currentSourceTime
```

Consume each marker once for `(actor, actionRunId, markerIndex)` even on a miss. If a hitch jumps from 0.40 s to 0.65 s, frame 14 still gets one opportunity. A held button starts another run only after recovery; it does not replay multiple historical swings to catch up after a hitch or focus loss.

### Contact-pose sampling

At a crossed impact, do not query the weapon solely at the end-of-frame follow-through pose. That pose may already be beyond the tree.

1. Advance the locomotion controller once for this frame.
2. Sample the action at its authored pre-impact/impact times, with the blend weight appropriate to each sampled time. Use zero-delta evaluations for these extra poses so locomotion is not advanced repeatedly. Unity exposes explicit graph evaluation ([Evaluate](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Playables.PlayableGraph.Evaluate.html)).
3. Read the actual rig/socket/tool contact shape at those poses. Sweep a small blade-contact sphere, or the authored set of volumes, through the short interval. Include initial overlap and bounded intermediate samples for curved movement. Do not sweep an entire windup or count the handle as an axe edge.
4. Validate and queue a contact result. No audio, trigger callbacks, IK side effects or inventory observers execute from temporary contact-pose evaluation.
5. Restore the final current-time pose and blend weight with zero extra clock advancement before rendering or attaching cosmetic effects.
6. Commit the still-valid result once, then publish feedback after pose restoration. If no hit is found, emit only a miss/whoosh as appropriate.

Initial local gameplay uses the current authoritative root/world pose for these short sub-samples; this is not historical physics rewind. The current reach/obstruction check takes priority after a long hitch. An active local action must keep its rig evaluable even when head/body renderers are hidden. AlwaysAnimate is appropriate for the local/actively interacting actor; do not turn it on indiscriminately for a future village population ([culling modes](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AnimatorCullingMode.html)).

The early graph proof must demonstrate that sampling/restoring action time leaves locomotion state time and the rendered final pose unchanged. A future dedicated-server path would need rig-specific baked contact trajectories or another validated headless contact model; this local graph is not, by itself, deterministic network simulation.

## 8. A tree hit from input to world state

```text
Primary-use intent
    → mode/menu/equipment gates
    → accept a Chop action with tool snapshot and intended TreeId
    → shared action time drives axe pose
    → Impact marker crossed
    → sample blade contact, query WorldSolid, ignore Player / QueryOnly
    → confirm same standing tree and current range/obstruction/tool validity
    → TreeHarvestService + item wear transaction
    → one accepted result → wood impact effects, sound and tree reaction
```

Discovery uses PlayerQuery.SolidHit and requires a collider in the resolved TreeGameplayProxy.PhysicalTrunkColliders. QueryOnly canopy identification is not choppable contact. The HUD's long reach does not authorize harvesting at that distance.

This gathering action captures an intended TreeId. Looking at another tree during windup must not silently transfer that run's damage to it. Revalidate actual physical contact at the marker; looking away, moving out of reach, a wall intervening, a cut tree, a pooled proxy rebind, or an unequipped/broken tool produces a miss/cancellation. Future free-swing combat can have a different target policy; do not hide that difference inside tree code.

At impact:

- Use nearest-obstacle WorldSolid casts/overlap checks with triggers ignored. A wall, terrain, building or different solid blocks the axe; do not cast against only a breakables layer.
- Exclude the character/held tools and canopy QueryOnly shapes. Use the existing gameplay layer roles.
- Resolve the contacted provider and verify its physical collider role, identity version, TreeId and Standing state again. Re-resolve the registry record by stable ID. Collider references are short-lived query data.
- Enforce a tool-authored range relative to the actor/tool, a permitted aim/arc, and line of sight from the hand/shoulder toward contact. The exact initial reach must be fitted to the rig and axe; do not inherit the camera's 8 m reach.
- Reject initial blade overlaps through a wall using the hand/shoulder obstruction check. Being inside a trunk volume is not permission to bypass intervening geometry.
- Deduplicate both the action marker and the accepted gameplay request. Decide which valid hit to use before changing HP or durability.

TreeHarvestService is world-scoped, keyed by TreeId, and creates per-tree harvest progress lazily on first hit. It owns HP/hit progress, tool suitability and terminal outcomes, without adding per-tree Update components. A valid nonterminal hit changes harvest progress; only a terminal hit requests a transition such as Standing → Cut through the registry. The registry currently removes non-standing trees from standing rendering; a falling tree/log representation and drops require their own implementation.

Use authored harvest definitions for tree toughness/yields; do not put runtime HP into WorldObjectDefinition assets. TreeId-scoped progress must survive proxy pooling and visual/chunk cache eviction; connect its save lifecycle explicitly when persistence is added. Rocks currently lack a persistent registry identity, so rock persistence/network support needs separate work rather than reusing a GameObject ID.

For the first axe, durability is charged once for an accepted valid hit, not a missed swing. Validate the item instance and tree together before committing either update. Publish VFX/audio after the commit so listeners cannot cause a duplicate hit or partial inventory transaction. Stamina costs and resource drops are separate explicit game rules when those systems exist.

## 9. Frame order, interruption, and modes

Preserve the project's current Update-driven CharacterController loop. Proposed scheduling:

| Stage | Owner / order | Work |
| --- | --- | --- |
| Update | LocalPlayerInput and motor | Read hardware, process menus/modes, apply look, submit semantic use command, apply existing action movement restrictions and simulate motor |
| Update | Existing world managers | Update gameplay proxies; their owners synchronize changed physics transforms |
| LateUpdate 100 | Existing PlayerQuery | Sample the final camera pose |
| LateUpdate 150 | New CharacterFrameCoordinator | Resolve equipment changes and cancellations; accept intent from this frame's query; advance action clock; evaluate locomotion/action poses; resolve crossed markers; restore final pose; commit validated results |
| LateUpdate 175 | Existing TerrainSculptController | Its existing mutually exclusive mode processing |
| LateUpdate 200 | Existing QueryHudPresenter | Display still-valid query identity after action effects |
| LateUpdate 300 | Held-light/cosmetic updates | Read final socket poses; no gameplay hit generation |

An action accepted in LateUpdate begins at source time zero that frame; its movement policy is applied before the next motor step. It never consumes a previous-frame PlayerQuery.Current in Update. New action restrictions do not teleport the root or skip gravity.

Priority for primary-use routing: disabled/unfocused/dead or blocking menu → no use; sculpt mode → sculpt command; building mode → build command; ordinary gameplay → equipped use action. One mouse edge/held state cannot simultaneously place a wall, excavate terrain and chop a tree. Retain existing input-edge consumption while menus are open and on cursor recapture.

| Situation | Policy |
| --- | --- |
| Menu opens, focus lost, death/disable, scene/world replaced | Cancel pending/active use and clear buffered use; do not resume a half-finished hit on return |
| Tool/hotbar/required offhand changes before impact | Cancel old run before contact; update visuals from actual equipment |
| Tool changes after impact | Preserve the old run's recovery/cooldown deadline so swapping cannot reset attack speed |
| Jump or sprint during stationary chop | Explicitly cancel that action before applying the requested motion; no delayed old hit |
| Intended tree disappears or is cut by another actor | Cancel/miss without applying damage to a recycled proxy |
| Same-rig hair/clothing/skin changes | Can swap safely without resetting locomotion or the action clock |
| Full rig-family change | Defer until idle, or cancel and release pending contacts before rig replacement |
| Time scale zero / gameplay pause | Pause the shared scaled clock and pose; no impact while paused |
| Teleport/discontinuity | Clear contact history and cancel the run; never sweep a tool across the teleport path |

Requests carry a current action generation/run ID. Late callbacks from cancelled or replaced visuals cannot affect the next run. Limit buffering to one explicitly permitted next-use request, and clear it on context changes. No frame backlog.

## 10. First-person presentation, cosmetics, and preview

The initial goal is one world rig whose visible arms/tool match the collision pose. Hide the local head/eyes/headwear from the local first-person view while preserving other characters' appearance. V02 is currently one body mesh plus eyes, so head/body-region separation is an asset preparation step; do not scale the head bone to zero or disable the entire body to solve camera clipping. A local-only ShadowsOnly head is an acceptable first implementation, with preview/remote renderers normal; camera-specific reflection behavior can be expanded later.

Test the chop from the actual 1.6 m eye height before accepting a clip: tool visibility, near-plane clipping, head/hair visibility, two-hand grip and contact height all matter. Cosmetic head/spine look or grip IK must be evaluated consistently before contact sampling. Start with a fitted/baked action clip; the Animation Rigging package is not currently installed and is not required for the first slice.

If dedicated first-person arms are needed later, they are a cosmetic view driven by the same accepted action/time. They never own a second hit path. World-rig tool contact remains authoritative and the viewmodel must be fitted to that result, not allowed to reach through walls independently.

Inventory preview uses a separate render-only appearance instance and camera, sharing definitions and appearance/equipment snapshot. It has no CharacterMotor, PlayerQuery, input, action controller, light registration, or damage receiver. Disable preview rendering while the menu is closed. Never clone the complete player prefab to make the inventory portrait.

Skin palettes/materials, hair, clothing and rigid accessories are presentation data. Armor/tool items may have gameplay stats through inventory; a visual override alone must not add armor, longer reach or harvesting power.

## 11. Events, bounds, and performance

Animation Events are suitable for optional footsteps or presentation callbacks. Their receiver belongs on the Animator GameObject and forwards to the character presentation owner, as Unity requires a matching receiver on the animated object ([events](https://docs.unity.com/en-us/engine/6000.3/manual/animation-section/animation-mecanim/animation-clips/animation-events-on-imported-clips)). Imported action clips must not contain direct damage events. Action whooshes/impact effects can use the shared timeline instead.

Ignore callbacks during temporary contact-pose samples. Footsteps should be grounded/movement-gated and deduplicated across blended locomotion clips, so two blend-tree contributors do not make two footstep sounds. Cosmetic callbacks cannot finish actions, equip items, or apply damage.

Validate skinned mesh bounds through crouch, jump, raised arms and tool use. Renderer visibility and Animator evaluation are different concerns. Culling a hat or changing mesh LOD must not cancel a hit, pause a cooldown, destroy a socket, or make the rig disappear. Local interaction evaluation is not dependent on the camera seeing the body.

Cache bone/socket bindings and resolved action data on setup/equip. No per-frame scene discovery, string-based bone searches, garment instantiation, or rebuilding bone arrays. Contact queries occur only at active contact intervals and use bounded reusable storage with an explicit overflow policy. Deduplication history is bounded, not an ever-growing list of every swing.

Profile graph evaluation, extra marker-pose samples, hit queries, skinned renderer count and allocations separately. Start with one actor and ordinary separate renderers. Do not introduce DOTS crowds, runtime mesh atlases/combination, a networking stack or a generic combat framework for the first chop.

## 12. Concrete implementation sequence and acceptance gates

### A. Rig/import and animation proof

Import v02's successful Mixamo rig into an isolated test prefab. Verify Humanoid Avatar mapping, fingers, scale, bind pose, elbows/knees/shoulders and eyes. Freeze HumanSettlerRigV1. Import in-place idle, directional movement and one fitted axe chop. Preserve the original FBX and use prefab/import metadata for Unity adaptations.

Prove the manual graph on Unity 6000.4.8f1: same clip/source time gives the expected pose; zero-delta impact samples do not advance locomotion or emit effects; restore gives the original final pose; root/camera do not drift. This proof precedes gameplay wiring.

### B. Modular visual slice

Install the rig under the existing RigAndAnimator placeholder. Add one shirt, hair/beard choice and axe socket. Show two compatible body/appearance selections while walking/crouching. Check bindings, body-region coverage, local head visibility, garment clipping and preserved action/locomotion state. No independent outfit Animators.

### C. Locomotion integration

Feed motor snapshots into the locomotion controller. Exercise wall blocking, slopes/stairs, crouch with obstructed headroom, accepted/rejected jump and airborne landing. Keep the camera stable and all gameplay movement motor-driven. Old VincentPlayerMovement must not be enabled on this rig.

### D. One reliable tree action

Add the development equipment source, semantic use command, action controller/clock, one clip's authored impact and ToolHitResolver. Use actual WorldSolid trunk proxies and stable TreeId. Begin with a recording/fake hit sink to prove timing, then add TreeHarvestService progress and item-wear transaction. Do not fake a complete tree-felling/drop system by destroying pooled proxy objects.

### E. Inventory/equipment and additional actions

Replace the development source with PlayerInventory-derived main hand/armor/offhand. Wire hotbar selection once, migrate held torch ownership, and add a render-only inventory preview. Add mining/melee/pickup/eat/equip actions through their own target/commit policies; they do not all inherit axe contact rules. Add upper-body masks and IK only after compatible action clips exist.

### Validation matrix

| Test | Required result |
| --- | --- |
| 15/30/60/120 fps plus a frame skipping past Impact | One opportunity per run; no missing/double damage; contact samples use authored impact pose |
| 0.75x / 1x / 1.5x run speed | Pose, whoosh/contact and recovery remain synchronized by one clock |
| Impact skipped over together with action finish | Process the marker once before completing the run; no immediate multi-swing catch-up |
| Contact while blending in/out or wrong action generation | Invalid authoring rejected; cancelled/old runs cannot commit |
| Canopy-only target, out-of-reach trunk, wall in front, axe initial overlap | No invalid harvest or wall penetration |
| Two trees near the axe, aim changes during windup | Only the intended physically contacted tree can receive that gathering run's hit |
| Proxy pooled/rebound, chunk/proxy released, tree state changes | Old target snapshot rejected; no damage to a new proxy occupant |
| Menu/focus/pause/death/disable before impact | No later ghost hit; no resumed held-input backlog |
| Equip/remove/break tool; rapid hotbar switching | Correct cancellation, one wear transaction and recovery preserved |
| Turn rapidly, crouch/jump, collide with a wall during action | Predictable motor policy; no animation movement or stale contact path |
| Body/head renderer hidden or offscreen; mesh LOD changes | Active action still samples/finishes correctly |
| Swap compatible body/hair/clothes; reject bad bind poses | Pose/state preserved; no extra skeletons or broken garment skinning |
| Inventory preview active | No duplicate action, item instance, torch light, or gameplay callbacks |
| Repeated warmed movement/contact updates | No steady managed allocation in core character/action/contact paths; measure actual frame cost |

Add focused editor/Play Mode validations following the existing PlayerQueryValidation and gameplay-layer fixture style. Unit-test marker crossing/cancellation with an injected clock; test real pose evaluation and physics contacts in Play Mode. Acceptance requires visual recordings/captures as well as geometry and state assertions.

## 13. First deliverable

A scene/test prefab using the existing player breakdown, one canonical v02 rig, two visible appearance combinations, one axe and one in-place chop. Movement/camera remain unchanged in ownership; contact is tied to an authored clip marker, physically validated against the existing tree trunk system, and cannot duplicate or survive cancellation. That is the foundation to establish before expanding the wardrobe, animation library or harvesting economy.
