# Spruce datacard and query HUD

Open `Assets/Scenes/SmearScene.unity` and enter Play mode. Look at a spruce trunk or its query canopy within the existing query's reach (currently 8 world units in this scene). The compact near-reticle panel displays **Spruce tree** in amber and **Wood source** beneath it. It clears on a miss, obstruction, target invalidation, or when the local query/presenter is disabled.

This stage implements identification only. It does not add a hotbar, discovery masking, harvesting, input bindings, or action prompts. Other existing query targets retain their authored/fallback names; absent descriptions are hidden rather than invented. The query panel is text-only; the optional icon remains part of the shared datacard/query metadata for other consumers.

## Project locations

| Purpose | Project path |
| --- | --- |
| Spruce datacard asset | `Assets/ScriptableObjects/WorldObjects/SpruceTree.asset` |
| Datacard schema | `Assets/Scripts/Content/WorldObjectDefinition.cs` |
| Optional datacard sprite (256 x 256, transparent; not drawn by the query HUD) | `Assets/UI/Icons/SpruceTree.png` |
| Woodland style asset | `Assets/UI/QueryHud/WoodlandOriginal.asset` |
| Style schema | `Assets/Scripts/UI/QueryHudStyle.cs` |
| UI Toolkit layout and spacing | `Assets/UI/QueryHud/QueryHud.uxml`, `QueryHud.uss` |
| Runtime panel/resolution settings | `Assets/UI/QueryHud/QueryHudPanelSettings.asset` |
| Segoe UI font assets | `Assets/UI/QueryHud/Fonts/SegoeUI-Regular.asset`, `SegoeUI-Semibold.asset` |
| Reusable query HUD prefab | `Assets/Prefabs/UI/QueryHud.prefab` |
| Presentation data, presenter and view | `Assets/Scripts/UI/QueryHudData.cs`, `QueryHudPresenter.cs`, `QueryHudView.cs` |
| Scene instance | `PlayerCharacter/UI/QueryHud` in SmearScene |
| Explicit setup and validation menus | `Assets/Editor/QueryHudSetup.cs`, `QueryHudValidation.cs`, `QueryHudRenderValidation.cs` |

Select `SpruceTree.asset` in Unity's Project window to change its ID (`tree.spruce`), name, sprite, or short description. Create another with **Assets > Create > First Settlers > Content > World Object Datacard**. Assign it to **Query Definition** on the relevant tree source prefab's `TreeGameplayAuthoring`, or to **Definition** on a `RockQueryTarget`.

Select `WoodlandOriginal.asset` to change appearance. The chosen settings are Segoe UI Regular with Semibold names; text scale 0.95; background opacity 0.75; corner radius 14; charcoal green `#263530`; description text `#C6CFBE`; amber accent/name text `#D9BF7B`. Background alpha is applied to the panel color, preserving opaque text. The name uses the accent directly, so changing the style's accent updates its color.

The panel reference resolution is 1920 x 1080 with **Scale With Screen Size**, matching width/height equally. At 3840 x 2160, geometry scales by two. The text-only panel sizes to its content between 160 and 260 logical pixels wide, with 14-pixel horizontal and 12-pixel vertical padding; longer text wraps at the maximum width. The base name/description sizes of 17/13 become 16.15/12.35 logical pixels with the chosen text scale. The panel sits 32 logical pixels right of screen center, at 48% screen height.

## Data flow

`SpruceTree.asset` is referenced by `TreeGameplayAuthoring` on `Spruce_LOD0_v06.prefab`. `TreeGameplayManager` copies that reference into its cached template and configures each pooled `TreeGameplayProxy` from it. Both trunk and canopy colliders resolve the same proxy.

`QueryTargetInfo` now contains a `Definition` reference and a `Description`, alongside its existing `DisplayName`, `Icon`, capabilities, typed world data and lifetime checks. A populated definition name/icon takes precedence; legacy metadata remains the fallback for providers without a datacard. The original tree record/ID stays unchanged. The datacard contains no quantity, durability, damage, inventory state or interactions.

`PlayerQuery.Current` remains the obstruction-filtered, nearest valid result. It samples at execution order 100. `QueryHudPresenter` runs at 200 and converts only a valid target into `QueryHudData`, containing just name and description. `QueryHudView` renders those two display fields through the shared UXML/style. It never casts rays, inspects equipment or executes actions, and every visual element ignores pointer picking. Unchanged values avoid text reassignment and rebuilding the visual tree.

Edit datacard values during Play mode to see the next query sample pick them up. Restart Play mode after changing a source prefab's definition assignment, since tree templates are cached, or after editing the HUD style settings.

## Fonts

The Segoe UI assets use **Dynamic OS** population and resolve the installed Windows font. The source Microsoft font files are not copied into the project. This matches the current Windows playtest setup; a future platform without Segoe UI will need its own font choice. The font asset reference in the style is the replacement point.

## Verification and setup

The scene is already wired. The prefab's `PlayerQuery` reference is intentionally unset because it is reusable; its SmearScene instance overrides that reference with the local player's existing `Interaction/PlayerQuery`.

- **Tools > UI > Validate Query HUD** exercises actual spruce template/proxy propagation, physical and canopy query paths, world identity, label/style/sprite/font setup, obstruction, misses, tree-state changes, pooled reuse, missing-field fallbacks, document disable/re-enable and settled allocations. It also runs the existing QueryTargetValidation suite. Temporary fixtures are destroyed; the scene is not saved by validation.
- **Tools > UI > Render Query HUD Checks** exports controlled actual UI Toolkit render checks at 1080p and 4K under `.utmp/query-hud/`. This is a panel-render fixture, not a full-world gameplay benchmark.
- **Tools > UI > Install Spruce Query HUD** recreates/rewires the named assets and scene if needed. It is explicit and resets the datacard name/description and selected style settings, so ordinary content edits should use the Inspector rather than rerunning installation.

## Icon generation

Generated with the built-in ImageGen tool, then copied into the project and reduced to a 256 x 256 PNG using Unity's texture importer and PNG encoder. The original generated source remains in Codex's generated-images folder. The final sprite uses full-rectangle import, alpha transparency, bilinear filtering, clamp wrapping, no mipmaps and no retained readable CPU copy.

Final generation prompt:

> Use case: stylized-concept. Asset type: small transparent game HUD sprite, a spruce tree identity icon for a woodland settlement game. Generate one isolated full spruce tree, centered, upright, recognizable conical evergreen silhouette with layered broad green boughs and a small visible warm brown trunk. Style: restrained stylized low-poly painted 3D game inventory thumbnail, natural muted forest greens with soft warm highlights, clean bold shapes, very little tiny detail, appropriate next to cream Segoe UI text on a charcoal green panel and muted amber accent. Composition: square with about 12 percent transparent padding around the whole tree, tree fills most of the frame, tree visible from front with slight three-quarter dimensional shading. Background must be genuinely transparent alpha. Designed to remain readable when reduced to a 32–80 pixel HUD icon; not a scene. No text, no letters, no border, no badge, no ground, no grass, no cast shadow outside the object, no ornaments, no snow, no other objects. Generate a crisp square source image.
