#ifndef RED_MAPLE_STYLIZED_LEAF_INCLUDED
#define RED_MAPLE_STYLIZED_LEAF_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#include "Assets/Shaders/TreeSimpleLitCommon.hlsl"

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);
// One layout shared by every pass keeps material values and SRP batching consistent.
CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _SummerLeafColor, _AutumnYellowColor, _AutumnOrangeColor, _AutumnRedColor;
    half4 _TreeLeafTint, _SnowColor;
    half _SeasonAutumnAmount, _AutumnVariationStrength, _TreeTintStrength;
    half _ColorVariationStrength, _LeafDetailStrength, _LeafGradientContrast, _LeafGradientMidpoint;
    half _Cutoff, _AlphaCutoutShadows;
    half _AmbientStrength, _LightWrap, _TranslucencyStrength, _Smoothness, _SpecularStrength;
    half _UseVertexColor, _SnowCoverage;
    float4 _WindDirection;
    half _WindStrength, _WindSpeed, _WindFlutterStrength, _WindFlutterSpeed, _WindGustScale;
    half _WindHeightMin, _WindHeightMax;
CBUFFER_END
float3 _LightDirection, _LightPosition;

struct RedMapleLeafAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    half4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct RedMapleLeafVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    half3 normalWS : TEXCOORD2;
    float3 positionOS : TEXCOORD3;
    float4 shadowCoord : TEXCOORD4;
    half fogFactor : TEXCOORD5;
    half3 vertexLighting : TEXCOORD6;
    half4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

float RedMapleLeafHash(float2 p)
{
    float3 q = frac(float3(p.xyx) * float3(.1031, .1030, .0973));
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}
half RedMapleLeafNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3 - 2 * f);
    return lerp(lerp(RedMapleLeafHash(i), RedMapleLeafHash(i + float2(1,0)), f.x),
        lerp(RedMapleLeafHash(i + float2(0,1)), RedMapleLeafHash(i + 1), f.x), f.y);
}
float3 RedMapleLeafWind(float3 world, float3 positionOS, float2 uv)
{
    float2 dir = normalize(_WindDirection.xz + float2(.0001, 0));
    float height = smoothstep(0, 1, saturate((positionOS.y - _WindHeightMin) /
        max(_WindHeightMax - _WindHeightMin, .0001)));
    // Phase and coloring use undeformed coordinates: wind never makes the palette crawl.
    float phase = dot(world.xz, dir.yx * float2(.73, -.61));
    float gust = sin(dot(world.xz, dir) * _WindGustScale + _Time.y * _WindSpeed + phase * .25);
    float flutter = sin(_Time.y * _WindFlutterSpeed + dot(positionOS, float3(1.7, .4, 2.1)) + uv.x * 4);
    float sway = gust * _WindStrength * height + flutter * _WindFlutterStrength * saturate(uv.y + .25) * height;
    return world + float3(dir.x * sway, flutter * _WindFlutterStrength * .22 * height, dir.y * sway);
}
RedMapleLeafVaryings RedMapleLeafVertex(RedMapleLeafAttributes input)
{
    RedMapleLeafVaryings output = (RedMapleLeafVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    output.positionOS = input.positionOS.xyz;
    output.positionWS = RedMapleLeafWind(TransformObjectToWorld(input.positionOS.xyz), input.positionOS.xyz, input.uv);
    output.positionCS = TransformWorldToHClip(output.positionWS);
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
    output.shadowCoord = TransformWorldToShadowCoord(output.positionWS);
    output.fogFactor = ComputeFogFactor(output.positionCS.z);
    output.vertexLighting = VertexLighting(output.positionWS, output.normalWS);
    output.color = input.color;
    return output;
}
half4 RedMapleLeafSample(RedMapleLeafVaryings input)
{
    half4 atlas = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
    // Opacity comes solely from alpha, never from the grayscale brightness gradient.
    clip(atlas.a - _Cutoff);
    #if defined(LOD_FADE_CROSSFADE)
        LODFadeCrossFade(input.positionCS);
    #endif
    return atlas;
}
half3 RedMapleLeafNormal(RedMapleLeafVaryings input, FRONT_FACE_TYPE facing)
{
    half3 n = NormalizeNormalPerPixel(input.normalWS) * IS_FRONT_VFACE(facing, 1, -1);
    return normalize(lerp(n, half3(0,1,0), _LightWrap * .2));
}
half4 RedMapleLeafFragment(RedMapleLeafVaryings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    half4 atlas = RedMapleLeafSample(input);
    half noise = RedMapleLeafNoise(input.positionOS.xz * .7 + input.uv * 4.7);
    half hue = saturate(.5h + (noise - .5h) * _AutumnVariationStrength * 2);
    // Red maple favors scarlet, with orange and occasional yellow accents.
    half3 autumn = lerp(_AutumnRedColor.rgb, _AutumnOrangeColor.rgb, saturate((hue - .38h) * 2) * .75h);
    autumn = lerp(autumn, _AutumnYellowColor.rgb, smoothstep(.8h, .98h, hue) * .45h);
    half season = TreeSeasonAutumnAmount(_SeasonAutumnAmount);
    half3 leaf = lerp(_SummerLeafColor.rgb, autumn, season);
    // Generic/red maple generation currently supplies white as a neutral tint.
    // Ignore that sentinel (and summer-only alpha-zero tints), preserving autumn
    // reds instead of turning the canopy white when standing-tree instancing runs.
    half4 treeTint = StandingTreeLeafTint(_TreeLeafTint);
    half3 deviation = abs(treeTint.rgb - half3(1,1,1));
    half authoredTint = smoothstep(.015h, .06h, max(deviation.r, max(deviation.g, deviation.b))) * saturate(treeTint.a);
    leaf = lerp(leaf, treeTint.rgb, season * _TreeTintStrength * authoredTint);
    // Center the subtle source gradient around neutral brightness, then amplify
    // its range. Directly multiplying by this pale grayscale atlas darkened every
    // leaf and compressed its shading into a narrow, uniformly green band.
    half luma = dot(atlas.rgb, half3(.299h,.587h,.114h));
    half gradedDetail = clamp(1.0h + (luma - _LeafGradientMidpoint) * _LeafGradientContrast, .25h, 1.5h);
    half detail = lerp(1.0h, gradedDetail, _LeafDetailStrength);
    leaf *= detail * lerp(1 - _ColorVariationStrength, 1 + _ColorVariationStrength, noise);
    leaf *= lerp(half3(1,1,1), input.color.rgb, _UseVertexColor);
    half3 normal = RedMapleLeafNormal(input, facing);
    half snow = StandingTreeSnow(_SnowCoverage) * smoothstep(.05h, .7h, normal.y);
    leaf = lerp(leaf, _SnowColor.rgb * detail, saturate(snow));
    InputData data = InitializeTreeSimpleLitInputData(input.positionWS, normal, input.positionCS,
        input.shadowCoord, _AmbientStrength);
    data.vertexLighting = input.vertexLighting;
    SurfaceData surface = InitializeTreeSimpleLitSurfaceData(leaf, atlas.a, _Smoothness, _SpecularStrength);
    half4 color = ShadeDistantAwareTree(data, surface);
    Light sun = GetMainLight(data.shadowCoord);
    color.rgb += leaf * sun.color * saturate(-dot(normal, sun.direction)) *
        sun.distanceAttenuation * sun.shadowAttenuation * _TranslucencyStrength * (1 - snow);
    if (_DistantTreeEnabled < .5 || _DistantTreeBillboard < .5)
        color.rgb = MixFog(color.rgb, input.fogFactor);
    return half4(color.rgb, atlas.a);
}
RedMapleLeafVaryings RedMapleLeafShadowVertex(RedMapleLeafAttributes input)
{
    RedMapleLeafVaryings output = RedMapleLeafVertex(input);
    #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
        float3 direction = normalize(_LightPosition - output.positionWS);
    #else
        float3 direction = _LightDirection;
    #endif
    output.positionCS = TransformWorldToHClip(ApplyShadowBias(output.positionWS, output.normalWS, direction));
    #if UNITY_REVERSED_Z
        output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE * output.positionCS.w);
    #else
        output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE * output.positionCS.w);
    #endif
    return output;
}
half4 RedMapleLeafShadowFragment(RedMapleLeafVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    ApplyDistantTreeFade(input.positionCS.xy);
    if (StandingTreeAlphaShadows(_AlphaCutoutShadows) > .5)
        RedMapleLeafSample(input);
    else
    {
        #if defined(LOD_FADE_CROSSFADE)
            LODFadeCrossFade(input.positionCS);
        #endif
    }
    return 0;
}
half4 RedMapleLeafDepthFragment(RedMapleLeafVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    half4 atlas = RedMapleLeafSample(input);
    ApplyDistantTreeFade(input.positionCS.xy);
    return half4(0, 0, 0, atlas.a);
}
half4 RedMapleLeafNormalsFragment(RedMapleLeafVaryings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    RedMapleLeafSample(input);
    ApplyDistantTreeFade(input.positionCS.xy);
    half3 normal = RedMapleLeafNormal(input, facing);
    #if defined(_GBUFFER_NORMALS_OCT)
        float2 packed = saturate(PackNormalOctQuadEncode(normal) * .5 + .5);
        return half4(PackFloat2To888(packed), 0);
    #else
        return half4(normal, 0);
    #endif
}
#endif
