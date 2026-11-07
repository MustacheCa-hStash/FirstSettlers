#ifndef FS_FOREST_CANOPY_LIGHTING_INCLUDED
#define FS_FOREST_CANOPY_LIGHTING_INCLUDED

half4 _FSForestLeaf, _FSForestBark;
half4 _FSForestDarkness;
float4 _FSForestCanopyBounds;
TEXTURE2D(_FSForestCanopyMask);
SAMPLER(sampler_FSForestCanopyMask);

half3 FSForestPalette(half3 authored, half4 palette, half reference)
{
    half tone = dot(authored, half3(.2126h, .7152h, .0722h));
    half detail = clamp(sqrt(max(tone, .001h) / reference), .42h, 1.8h);
    return lerp(authored, palette.rgb * detail, saturate(palette.a));
}

half3 FSForestLeafColor(half3 authored)
{
    return FSForestPalette(authored, _FSForestLeaf, .16h);
}

half3 FSForestBarkColor(half3 authored)
{
    return FSForestPalette(authored, _FSForestBark, .13h);
}

half FSForestCrownFill(half3 normalWS, half interior)
{
    half shade = max(saturate(.55h - .45h * normalWS.y), saturate(interior));
    return 1.0h - saturate(_FSForestDarkness.x) * shade;
}

half FSForestGroundLight(float3 positionWS)
{
    UNITY_BRANCH
    if (_FSForestDarkness.y <= 0.0h) return 1.0h;
    float2 uv = (positionWS.xz - _FSForestCanopyBounds.xy) * _FSForestCanopyBounds.z;
    float2 edge = min(uv, 1.0 - uv);
    half fade = smoothstep(0.0, .08, min(edge.x, edge.y));
    half canopy = SAMPLE_TEXTURE2D(_FSForestCanopyMask, sampler_FSForestCanopyMask, saturate(uv)).r;
    return 1.0h - saturate(_FSForestDarkness.y) * canopy * fade;
}

#endif
