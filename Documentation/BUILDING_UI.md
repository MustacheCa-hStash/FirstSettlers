# Stone building catalog

The building picker now uses the approved limestone-and-pillars style: three separate panels, serif headings, clear sans-serif body text, slate-blue selection, functional categories, actual-mesh thumbnails and a selected-piece detail view. The world stays visible between and around the panels. The placement HUD shares the limestone artwork and retains the existing placement messages and valid/warning/error states.

October 12 compact layout: the normal catalog footprint is about 30% smaller through narrower bounds, tighter panel padding/category rows, shorter thumbnail tiles and a slimmer quick-piece strip. All text, content, font sizes and existing responsive font rules are retained. Catalog scrolling keeps every component reachable; the placement HUD is unchanged.

The catalog is centered horizontally and vertically using symmetric viewport margins. The normal and compact responsive layouts both retain that center when the viewport or PanelSettings GUI scale changes; the normal layout keeps its compact 66% × 68% footprint.

Centering was validated at all four resolutions with PanelSettings GUI scales of 0.75, 1.0 and 1.25. [Centered Unity preview](../ArtReferences/UIRedesign/ImplementedBuilding/catalog-centered-on-original-meadow.png) shows the current placement over the original meadow screenshot.

The compact layout passed the existing render/interaction suite at 1280×720, 1920×1080, 3840×2160 and 1024×768. All twelve current catalog tiles fit in the 1080p default view. [Compact Unity preview](../ArtReferences/UIRedesign/ImplementedBuilding/catalog-compact-on-original-meadow.png) uses the original meadow background and a real Unity UI capture; earlier previews are retained.

## Controls

- **B** opens building; **B / Esc** exits.
- Click a category to filter the catalog, then click a component to inspect its name, dimensions and description.
- **Enter** confirms the highlighted definition and returns to placement. Keyboard focus highlights a component too; **double-click** a catalog tile to select it immediately. The Choose piece button remains available.
- For half-gables, **Flip slope / R** changes the staged variant and its thumbnail. It does not commit a world piece or change the active placement definition until confirmed.
- The **Quick pieces** strip selects a catalog component and enters placement immediately. It is a shortcut strip for the current kit, not inventory or persisted custom favorites.
- During placement, existing left-click place, right-click remove, Q/E/wheel rotation, arrow nudges, Page Up/Down height and R slope flip are preserved. **Tab** now exits building and opens inventory. Use B to exit building and B again to open the catalog. Menus replace one another without restoring a previous menu when closed.

Catalog order/grouping is presentation-only. It does not change the preset array, stable content IDs, component geometry, meshes, materials, snapping, collision, support or persistence. The half-gable remains one catalog entry with two linked representations. Its category is Roofs even though its physical definition kind is Wall. Dimensions consistently read **width × height × depth** from LocalBounds; the roof numbers describe its structural width/rise/run.

## Source and assets

| Purpose | Location |
| --- | --- |
| Layout and style | `Assets/Resources/Building/BuildMenu.uxml`, `BuildMenu.uss` |
| View and UI state | `Assets/Scripts/Building/BuildMenuView.cs` |
| Presentation names, categories and descriptions | `BuildUiContent.cs` |
| Vector category symbols | `BuildCategoryIcon.cs` |
| Controller and hardware routing | `BuildingController.cs`, `Assets/Scripts/PlayerMovement/LocalPlayerInput.cs` |
| Nine-slice limestone and pillar artwork | `Assets/Resources/Building/UI/StonePanel.png`, `StonePillar.png` |
| Heading/body font assets | `Assets/Resources/Building/UI/Fonts/StoneHeading.asset`, `StoneBody.asset` |
| Thirteen actual-mesh thumbnails (twelve tiles plus mirrored gable) | `Assets/Resources/Building/UI/Thumbnails/` |
| Asset baker and render/interaction validation | `Assets/Editor/BuildStoneUiAuthoring.cs` |

Thumbnails are 512 × 384 transparent PNGs rendered from each installed BuildDefinition.mesh and material under simple diffuse lighting. Both half-gable slopes are baked; only the canonical slope occupies a catalog tile. The foundation preview is the actual plain foundation geometry, not the decorative masonry block drawn in the concept. Rebuild thumbnails after changing catalog geometry/materials using **Tools > Building > Rebuild Stone UI Assets**. This explicit action imports the skin as 2D textures, updates thumbnails and creates missing fonts; it never loads or saves a game scene or rebuilds the building catalog.

Fonts use Dynamic OS Georgia Bold for headings and Segoe UI Regular for body text, following the project's existing Windows font approach. Font files are not copied from Windows. A target without these installed fonts needs suitable replacement font assets at the two resource paths before shipping.

The catalog creates its visual tree once per view, caches its tiles and filters them in place. The UI does not render preview cameras or inspect meshes in player builds. Switching the detail view loads an already-baked texture; hidden menus have no per-frame UI refresh loop. Category symbols are cached UI Toolkit vector geometry. The passive placement HUD ignores pointer picking throughout its subtree. LocalPlayerInput remains the local hardware reader; UI submit handling preserves the staged slope when a canonical tile has focus and avoids duplicate confirmation.

## Verification

W22/W24 thumbnails are now installed in the tile grid, detail panel and quick
strip; both belong to the Stairs category alongside W21. The targeted
`-StairUiAssets` authoring switch bakes only those two images and then validates
the picker. Installed-asset checks cover keyboard focus followed by Enter,
double-click confirmation, preserved staged gable slopes and layouts at all four
resolutions. Native Enter/held-key/menu guards passed separately.

**Tools > Building > Validate Stone UI (synthetic)** tests the installed UI skin bindings, texture dimensions, all current thumbnails, category membership, staged versus committed selection, both gable variants, quick-strip clicks, keyboard submit with a focused canonical tile, passive HUD picking and contained controls. It renders real UI Toolkit output at 1280 × 720, 1920 × 1080, 3840 × 2160 and 1024 × 768, including placement and warning views. It uses temporary objects and a cloned PanelSettings; it does not inspect the saved world camera.

Shell harness: `Tools/ValidateBuildingPrototype.ps1 -UiChecks`. To bake and validate assets in the isolated validation project, use `-UiAssets`; its generated assets live under `.utmp/building-prototype/unity-project/Assets/Resources/Building/UI`, so copy reviewed output back explicitly if using that authoring route. The Unity menu action bakes directly into the open project instead.

`Tools/ValidateBuildingPrototype.ps1 -InputChecks` additionally runs native B/E/Escape/R/Enter/F8/F9 input frames in an empty synthetic scene, including held-key guards, staged menu flip/confirmation, cursor recapture, suppressed look and arrow repeat. These checks require normal Windows Unity graphics/licensing access if the restricted sandbox crashes the editor's services.

Actual rendered previews are saved in [ImplementedBuilding](../ArtReferences/UIRedesign/ImplementedBuilding/). `catalog-on-original-meadow.png` and `placement-on-original-meadow.png` composite the actual Unity UI captures over the user's original screenshot for comparison; they are not live-world gameplay captures. [Artwork prompts](../ArtReferences/UIRedesign/ImplementedBuilding/ARTWORK_PROMPTS.md) record the two ImageGen skin assets. No saved scene changes were made by this implementation; the user's existing SmearScene edits were preserved.

October 10 validation: runtime/editor compilation passed, the installed stone UI bindings and four-resolution render/interaction checks passed, and the extended native input suite passed. Logs: `.utmp/building-prototype/stone-ui.log` and `input.log`. The first sandboxed editor launch crashed before validation; the successful checks used the same isolated project with normal Windows graphics/licensing access. Existing unrelated obsolete-API compiler warnings remain. Restart Play mode after Unity imports the new UI assets, then press B.
