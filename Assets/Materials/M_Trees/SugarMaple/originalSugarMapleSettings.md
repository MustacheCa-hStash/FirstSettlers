# Sugar Maple source-material reference

Capture source: `Assets/Prefabs/SugarMaple_LOD0_v04.prefab`.

- Leaf Material: `M_SugarMapleLeaf`
- Bark Material: `M_SugarMapleBark_Stylized`

## Leaf

- Shader: `Custom/SugarMapleLeafSimpleLitCutout`
- Cutoff: `0.50`
- Summer: `(0.16, 0.45, 0.12)`
- Autumn red: `(0.72, 0.08, 0.055)`
- Autumn orange: `(0.95, 0.32, 0.06)`
- Autumn yellow: `(1.0, 0.62, 0.12)`
- Autumn variation: `0.28`; per-tree tint strength: `0.88`
- Seasonal amount: `1`
- Leaf card contrast: `0.32`; card variation: `0.12`; vertical gradient: `0.28`
- Backlighting: `0.907`; smoothness `0.08`; specular `0.03`

## Bark

- Shader: `Custom/SugarMapleStylizedBark`
- Base colour: white; brightness `1`
- Colour variation: `0.025`; vertical gradient: `0.04`
- Smoothness: `0.10`; specular `0.025`

## Capture rule

Leaf RGB is captured as neutral value/detail; summer and autumn hue are applied by the runtime season palette. Bark RGB is captured from the painted stylized bark texture without scene lighting.
