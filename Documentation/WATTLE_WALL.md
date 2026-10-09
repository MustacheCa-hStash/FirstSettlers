# Wattle wall

## Placement preview visibility

The weave sheets disappeared from the back in the placement ghost because the shared green/red URP Lit preview materials culled back faces, while the placed wattle material is two-sided. Solid rail geometry stayed visible, matching the reported screenshot. BuildingController now chooses cached two-sided preview material copies for components whose authored material disables culling. Ordinary one-sided component previews retain their shared materials. The copies are reused and released when the controller is destroyed; source preview assets, wattle geometry, UVs, placed cutout lighting/shadows and placement rules are unchanged.

`Tools/ValidateBuildingPrototype.ps1 -PreviewChecks` runs an isolated GPU regression with the actual wattle mesh and the controller's preview material selection. Both green/red previews are checked from both sides at all eight headings. The legacy preview lost all four sampled weave patches in 16/32 views; the fixed preview retains all 484 sampled weave pixels in every view. The test also checks material reuse, unchanged shared culling and cleanup. Log: `.utmp/building-prototype/preview.log`; this does not open the saved scene or reproduce the user's live camera.

**Wattle wall** is the sixth option, appended after the existing split-plank wall, floor, foundation, corner and two-plank infill. The weave renders from both sides with cutout holes; the logical/physical wall box stays solid.

## Model and assets

Source: `Assets/Models/Buildings/Wood/WattleWall_3.5Wx2.75Hx0.fbx`. Isolated Unity inspection found **64 vertices, 32 triangles, one mesh/submesh**, identity hierarchy transforms and bounds **(0,0,0)..(3.5,2.75,0.25)**. The four weave faces contribute eight triangles; the two timber rails contribute the other 24. No twig meshes or added poles are generated.

- Definition: `Assets/Resources/Building/wattle-wall.asset`, ID `build.wood.wattle-wall`.
- Runtime mesh: `wattle-wall-mesh.asset` in the same folder.
- Authoring prefab: `wattle-wall.prefab` in the same folder.
- Material: `Assets/Materials/Buildings/Wood/WattleWood.mat`.
- Shader: `Assets/Shaders/BuildingWoodMatte.shader`, `Custom/BuildingWoodMatte`.
- Shared atlas: `Assets/Textures/Buildings/Wood/ordinary-wood-trim-albedo.png`.

The definition is a **Surface-mode Wall**, with size ticks **(14,11,1)**, minimum ticks zero and one wall-end inset tick. On a foundation/floor it follows the quarter-metre surface grid rather than forcing perimeter bays; the wall's bottom rests on the target's top face. Its long axis may overhang while the thin axis remains on the supporting face. Heading initially inherits that surface's grid; manual rotation/nudging remains available. Wall-to-wall extension/stacking keeps the ordinary wall rules. A bottom-face hit attaches beneath the target instead. BoxCollider centre is **(1.75,1.375,0.125)** and size is **(3.5,2.75,0.25)**. Holes do not admit characters or physics rays; there is no twig MeshCollider.

`WattleWallSetup` reads mesh data through editor MeshUtility, preserving the source FBX/import settings. It bakes hierarchy transforms, inverse-transpose normals and mirrored winding into one submesh, preserves UV0 and normalizes the lower-corner origin. Dimension mismatches are rejected rather than stretched. The supplied FBX already needs no transform/origin correction; its derived mesh asset is updated in place on rebuild.

Use **Tools > Building > Rebuild Wattle Wall** after updating the FBX. It refreshes this mesh, definition, prefab and material and registers the picker entry once without changing the existing five. The full prototype installer also includes it as option six. Restart Play mode after asset updates to refresh existing render batches.

## Material and rendering

The dedicated material copies the opaque wood material's matte art settings when first created, then uses:

| Setting | Wattle | Existing split-plank/infill |
| --- | --- | --- |
| Alpha clipping / keyword | On / `_ALPHATEST_ON` | Off |
| Cutoff | Initially 0.5 | Unused |
| Face culling | Off (both faces) | Back |
| Render type / queue | TransparentCutout / 2450 | Opaque / Geometry |
| Depth writes | On | On |
| Instancing | On | On |

No alpha blending is used. Forward, shadow-caster, depth-only and depth-normal passes apply the same atlas alpha threshold, including base tint alpha. Shadow vertices carry transformed UVs. Two-sided forward/depth-normal normals face the viewed side; shadow bias faces the light. The vertex additional-light path also uses the corrected back-face normal. Existing matte diffuse appearance, Forward+ additional lights, fog, ambient SH, sun shadows and sun-cycle nighttime ambient-floor dimming are retained, without specular or reflection sheen.

Visible two-sided batches request TwoSided shadow casting. Nearby off-screen batches retain **ShadowsOnly**, with Cull Off in the wattle shadow pass. The recent off-screen roof-caster fix remains intact.

## UV inspection note

UV0, including intentionally overlapping islands, is preserved exactly from the FBX. Lower 1.75 x 1.75 m faces use the stated full patch: **U 0.50805664..0.99194336**, **V 0.00805664..0.49194336**. The two upper 1.75 x 1.00 m faces retain the intended 4/7 V span, but their authored offset differs from the pasted notes:

- Actual imported upper V: **0.02123334..0.29774000**.
- Upper V in the notes: **0.00805664..0.28456334**.

The existing upward offset is approximately **0.0131767**. Integration introduces no random or corrective UV offset/rotation. If the lower/upper join should use the exact written phase, change those upper islands in Blender and rebuild. Horizontal U still repeats twice per panel and restarts consistently across identical neighbouring panels.

Rail faces/caps stay in opaque hewn-wood/end-grain regions. A combined atlas does not turn into a repeating wattle texture through global Repeat; the four mesh UV faces handle local repeats. The texture/import settings and opaque wood material were preserved.

## Verification

`Tools/ValidateBuildingPrototype.ps1 -WattleChecks` passed runtime/editor compilation, **2,828 synthetic checks**, six-option menu rendering at 720p/1080p, authored front/back and actual instanced rendering, near/far submission and deletion. Checks include bounds/origin, source UV preservation, weave regions, opaque rail samples, rotated snapping, overlap rejection, pooled full BoxCollider, support loss/reconnection and idempotent rebuild/catalog order.

GPU comparisons found **zero mask mismatches** between forward, shadow-caster, depth-only and depth-normal passes, from both sides. The depth-normal output confirms opposite camera-facing normals. A validation-only depth-reveal shader makes actual depth-write holes visible instead of inferring them from forward colour.

A real instanced weave canopy outside the camera view casts onto a receiver. Mean receiver brightness was **1.0000** with no caster, **0.0801** with an opaque caster, and **0.2779** with the cutout caster. Reversed winding/normals produced the same **0.2779**, verifying two-sided cutout shadows through the ShadowsOnly submission path. This is an isolated diagnostic fixture, not a scene brightness recommendation.

- [Front](../ArtReferences/BuildingPrototype/WattleWallFront.png)
- [Back](../ArtReferences/BuildingPrototype/WattleWallBack.png)
- [Instanced](../ArtReferences/BuildingPrototype/WattleWallInstanced.png)
- [Cutout shadow fixture](../ArtReferences/BuildingPrototype/WattleWallCutoutShadow.png)

Logs: `.utmp/building-prototype/wattle-import.log`, `wattle.log`. Captures and the import report also remain in the isolated project's `.utmp/building-prototype` directory. These checks use an empty separate Unity project; the saved scene, source FBX/importer, opaque material, project lighting settings and URP asset were verified unchanged by hashes. Existing unrelated obsolete-API warnings remain. No live-world FPS claim is made.

The existing `-ShadowChecks` roof regression also passed: the sampled floor point stayed at luminance **0.0076** while looking down, ahead and after moving, and returned to **0.6509** after removing the roof. Log: `.utmp/building-prototype/shadows.log`.

