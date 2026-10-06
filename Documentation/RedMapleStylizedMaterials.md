# Reclassified stylized red maple leaf card

The initial leaf card has three dominant lobes and reads more clearly as stylized red maple beside the new five-lobed sugar maple art. Its smooth margins mean this is an artistic species distinction, not a botanically exact red maple identification.

- Texture: `Assets/Textures/Trees/RedMaple/T_RedMapleLeafClump_Stylized.png`.
- Material: `Assets/Materials/M_Trees/RedMaple/M_RedMapleLeaf_Stylized.mat`.
- Shader: `Custom/RedMapleStylizedLeaf`, in `Assets/Shaders/RedMapleStylizedLeaf.shader`.
- Authoring texture/layers/meshes: `ArtReferences/RedMaple/red_maple_card_v01*`.

These assets retain the original texture pixels, tuning, and Unity GUIDs. The existing sugar maple prefab was relinked to a new sugar maple material/texture, so it no longer uses the reclassified card. Existing legacy red maple prefabs and their shaders are not automatically replaced.

The leaf shader starts with a slightly cooler summer green and favors scarlet/red with orange and occasional yellow accents in autumn. It retains per-tree tinting, wind, alpha/depth/shadow passes and the same gradient contrast controls. Neutral white tints from generic/red maple generation and summer-only alpha-zero tints do not override its autumn palette. Explicit colored autumn tints remain supported.

## Bark using the shared texture

- Shader: `Custom/RedMapleStylizedBark`, in `Assets/Shaders/RedMapleStylizedBark.shader`.
- Material: `Assets/Materials/M_Trees/RedMaple/M_RedMapleBark_Stylized.mat`.
- Shared texture: `Assets/Textures/Trees/Bark/T_SugarMapleBark_Stylized.png`; no duplicate bark image is required.

The shader reduces the warm texture's saturation to `0.35`, softens its ridge/furrow contrast to `0.75` around a linear midpoint of `0.18`, applies a subtle cool tint `(0.93, 0.97, 1)`, and lifts brightness to `1.05`. The painted pattern and tiling remain the same. These controls change color and apparent ridge contrast; they do not create new physical bark geometry. Tree bark tint, snow, instancing, fades, lighting and four rendering passes are retained.

Both red maple materials are ready to assign to a new tree. Existing legacy red maple prefabs are not changed by this shader/material setup. `SugarMapleStylizedAssets.CreateAssets` configures all four maple materials. Its validation includes both red maple shaders, shared bark texture identity, cooler bark rendering, and the neutral-white autumn tint case.
