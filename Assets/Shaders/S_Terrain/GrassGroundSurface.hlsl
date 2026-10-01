#ifndef FIRST_SETTLERS_GRASS_GROUND_INCLUDED
#define FIRST_SETTLERS_GRASS_GROUND_INCLUDED

// Three stable, world-anchored tile samples on a triangular lattice. The input
// is periodic; random quarter-turns and offsets hide the repeating tile itself.
// All four channels share the transforms, including inverse-rotated normals.
float2 GrassGroundRotate(float2 value, int turns)
{
    if (turns == 1) return float2(-value.y, value.x);
    if (turns == 2) return -value;
    if (turns == 3) return float2(value.y, -value.x);
    return value;
}

half4 GrassGroundTile(float2 uv, float2 vertex, float2 dx, float2 dy, float2 parallax, bool useHeight)
{
    float2 offset = float2(Hash21(vertex + 19.37), Hash21(vertex + 83.11));
    int turns = (int)(Hash21(vertex + 41.73) * 4.0);
    float2 tileUV = GrassGroundRotate(uv, turns) + offset;
    float2 tileDx = GrassGroundRotate(dx, turns);
    float2 tileDy = GrassGroundRotate(dy, turns);
    half4 surface = SAMPLE_TEXTURE2D_GRAD(_GrassSurfaceMap, sampler_GrassSurfaceMap, tileUV, tileDx, tileDy);
    [branch] if (useHeight)
    {
        tileUV -= GrassGroundRotate(parallax, turns) * (surface.a - 0.4h);
        surface = SAMPLE_TEXTURE2D_GRAD(_GrassSurfaceMap, sampler_GrassSurfaceMap, tileUV, tileDx, tileDy);
    }
    float2 normalXZ = GrassGroundRotate(surface.gb * 2.0h - 1.0h, turns == 0 ? 0 : 4 - turns);
    surface.gb = normalXZ * 0.5h + 0.5h;
    return surface;
}

half4 GrassGroundLattice(float2 uv, float2 dx, float2 dy, float2 parallax, bool useHeight)
{
    float2 skew = mul(float2x2(1.0, 0.0, -0.57735027, 1.15470054), uv * _GrassGroundGridScale);
    float2 cell = floor(skew);
    float2 f = frac(skew);
    float2 a, b, c;
    float3 weights;
    if (f.x + f.y < 1.0)
    {
        a = cell; b = cell + float2(1, 0); c = cell + float2(0, 1);
        weights = float3(1.0 - f.x - f.y, f.x, f.y);
    }
    else
    {
        a = cell + 1.0; b = cell + float2(0, 1); c = cell + float2(1, 0);
        weights = float3(f.x + f.y - 1.0, 1.0 - f.x, 1.0 - f.y);
    }
    // Smooth blends with zero derivative when a tile leaves the triangle.
    weights = weights * weights * weights;
    weights /= max(dot(weights, 1.0.xxx), 0.00001);
    return GrassGroundTile(uv, a, dx, dy, parallax, useHeight) * weights.x
         + GrassGroundTile(uv, b, dx, dy, parallax, useHeight) * weights.y
         + GrassGroundTile(uv, c, dx, dy, parallax, useHeight) * weights.z;
}

void SampleGrassGround(float3 positionWS, float3 baseNormalWS, float cameraDistance, float grassAmount,
    float2 dx, float2 dy, out half tone, out float3 normalWS)
{
    tone = 1.0h;
    normalWS = baseNormalWS;
    float detailFade = 1.0 - smoothstep(_GrassGroundDetailFadeStart,
        max(_GrassGroundDetailFadeEnd, _GrassGroundDetailFadeStart + 0.01), cameraDistance);
    // At range, or under another forest-floor layer, no grass texture work.
    [branch] if (detailFade * grassAmount < 0.001) return;

    float normalFade = 1.0 - smoothstep(_GrassGroundNormalFadeStart,
        max(_GrassGroundNormalFadeEnd, _GrassGroundNormalFadeStart + 0.01), cameraDistance);
    float heightFade = 1.0 - smoothstep(_GrassGroundHeightFadeStart,
        max(_GrassGroundHeightFadeEnd, _GrassGroundHeightFadeStart + 0.01), cameraDistance);
    bool useHeight = heightFade * _GrassGroundHeightDepth > 0.00001;
    float2 parallax = 0.0;
    [branch] if (useHeight)
    {
        float3 view = normalize(_WorldSpaceCameraPos.xyz - positionWS);
        parallax = view.xz / max(abs(dot(view, baseNormalWS)), 0.45) *
            (_GrassGroundHeightDepth * _GrassGroundTiling * heightFade);
    }

    // Crossfade two fixed world-space patterns. Scaling the UVs continuously
    // with camera distance would make the pattern slide as the camera moves.
    float scaleBlend = smoothstep(_GrassGroundScaleFadeStart,
        max(_GrassGroundScaleFadeEnd, _GrassGroundScaleFadeStart + 0.01), cameraDistance);
    half4 surface;
    [branch] if (scaleBlend >= 0.999)
    {
        surface = GrassGroundLattice(positionWS.xz * _GrassGroundFarTiling,
            dx * _GrassGroundFarTiling, dy * _GrassGroundFarTiling, 0.0, false);
    }
    else
    {
        surface = GrassGroundLattice(positionWS.xz * _GrassGroundTiling,
            dx * _GrassGroundTiling, dy * _GrassGroundTiling, parallax, useHeight);
        [branch] if (scaleBlend > 0.001)
        {
            half4 farSurface = GrassGroundLattice(positionWS.xz * _GrassGroundFarTiling,
                dx * _GrassGroundFarTiling, dy * _GrassGroundFarTiling, 0.0, false);
            surface = lerp(surface, farSurface, scaleBlend);
        }
    }
    // Expand blade/background separation around the neutral terrain tone.
    half bladeTone = max(0.25h, 1.0h + (surface.r * 2.0h - 1.0h) * _GrassGroundDetailContrast);
    tone = lerp(1.0h, bladeTone, _GrassGroundDetailStrength * detailFade);
    [branch] if (normalFade * _GrassNormalStrength > 0.001)
    {
        float2 normalXZ = surface.gb * 2.0h - 1.0h;
        float3 perturbation = float3(normalXZ.x, 0.0, normalXZ.y);
        perturbation -= baseNormalWS * dot(perturbation, baseNormalWS);
        normalWS = normalize(baseNormalWS + perturbation * (_GrassNormalStrength * normalFade));
    }
}
#endif
