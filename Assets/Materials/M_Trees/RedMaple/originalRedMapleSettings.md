# Red Maple source-material reference

Source prefab recommended for the impostor: `Assets/Prefabs/RedMaple_LOD0_v03.prefab`.

The capture setup must use these two source materials exactly:

- Leaves: `M_RedMaple_Leaf`
- Bark: `M_RedMapleBark_Stylized`

## Leaf material (`M_RedMaple_Leaf`)

- Shader: `Custom/RedMapleLeafSimpleLitCutout`
- Base map: assigned Red Maple leaf atlas; its alpha defines leaf-card coverage.
- Cutoff: `0.50`
- Alpha-to-coverage: enabled
- Cull: off
- Smoothness: `0.08`; specular strength: `0.03`
- Summer colour: `(0.18, 0.42, 0.12)`
- Autumn scarlet: `(0.88, 0.06, 0.035)`
- Autumn crimson: `(0.48, 0.025, 0.04)`
- Autumn orange: `(1.0, 0.25, 0.055)`
- Leaf shadow colour: `(0.16, 0.018, 0.018)`
- Autumn amount: `1.0`
- Colour variation: `0.62`; card variation: `0.22`; vertical gradient: `0.30`; leaf contrast: `0.34`
- Ambient strength: `0.30`; light wrap: `0.62`
- Backlighting enabled: true; stable backlighting: true; strength: `0.939`; colour: white
- Wind: direction `(1, 0, 0.35)`, canopy strength `0.11`, speed `1.15`, flutter strength `0.055`, flutter speed `4.8`, gust scale `0.18`, height range `0..7`

## Bark material (`M_RedMapleBark_Stylized`)

- Shader: `Custom/RedMapleStylizedBark`
- Base map: assigned stylized bark texture.
- Base colour: `(0.93, 0.97, 1.0)`
- Brightness: `1.05`; texture saturation: `0.35`; contrast: `0.75`; contrast midpoint: `0.18`
- Colour variation: `0.02`; vertical gradient: `0.025`; gradient height range `0..7`
- Ambient strength: `0.24`; smoothness: `0.10`; specular strength: `0.025`
- Snow coverage: `0`; snow colour `(0.9, 0.94, 0.97)`

## Impostor capture rule

Do **not** capture the displayed summer or autumn colours into the leaf RGB atlas. The Red Maple semantic capture shader writes a neutral leaf-detail/value field to RGB and alpha coverage to A. This allows the runtime impostor to choose summer, early-autumn, scarlet, crimson, orange, or per-instance tint without baking the selected current season twice.

`RedMaple_Octa_MaterialId` stores leaf in R, bark in G, and a stable leaf palette coordinate in B. The current shared impostor runtime shader does not consume B yet; retain it for the Red Maple runtime palette shader rather than discarding it.
