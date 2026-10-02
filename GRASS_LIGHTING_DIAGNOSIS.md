# Grass face color diagnosis

SmearScene's GrassTuftBroad_LOD0/LOD1 prefabs use M_GrassTuftBroad and the
handwritten Custom/GrassInstancedTerrainTint shader. SG_Grass_Instance is not
the shader assigned to these prefabs.

Both sides of a blade use the same base color at the same height. The shader
uses Cull Off and has no front/back color or normal branch. Texture RGB is
ignored; texture alpha cuts out the blade. The base color combines a smooth
palette sampled at the tuft's origin, a root-to-tip gradient, and material
colors. The palette can vary between tufts but does not vary across a tuft.

Different blade directions have different mesh normals. The prefab builder
recalculates normals from the blade geometry; the shader blends them toward
upward normals before lighting. Direct light contributes
`mainLight.color * (0.35 + 0.65 * saturate(dot(normal, lightDirection)))`.
The current material's Upward Normal Blend is 0.65, with Base/Tip Upward
Normal Blend values of 0.39/0.86, so orientation still affects brightness.
Ambient light and received shadows also affect the final shade.

SunCycleController reduces sunlight intensity with direct sun visibility.
As that direct contribution weakens, the orientation-dependent contrast
decreases, consistent with the reported evening appearance. This explains
the screenshot without requiring a face-color correction. Production grass
shading and material settings were left intact.

GPU validation used the active material and a transient copy of its shader
with only the lighting multiplication removed. Front/back and opposite-normal
base-color differences were zero. Viewing opposite sides of the same lit card
also produced zero difference. Opposite normals produced relative contrast
0.664 at sunlight intensity 1 and 0.181 at intensity 0.1. These are controlled
rendering checks, not measurements of the screenshot or a complete day cycle.

Tools > Foliage > Validate Grass Face Colors reruns this check. To deliberately
make *lit* grass more uniform, increase upward normal blending in
M_GrassTuftBroad. Setting all three upward-blend controls to 1 makes all blade
normals point upward; this also flattens their directional lighting.
