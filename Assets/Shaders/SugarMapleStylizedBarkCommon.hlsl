#ifndef SUGAR_MAPLE_STYLIZED_BARK_INCLUDED
#define SUGAR_MAPLE_STYLIZED_BARK_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#include "Assets/Shaders/TreeSimpleLitCommon.hlsl"
TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);
#if defined(_BARK_NORMALMAP)
    TEXTURE2D(_BumpMap);
    SAMPLER(sampler_BumpMap);
#endif
CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half _BarkDistanceTiling, _BarkCanopyShelter;
    half4 _BaseColor, _TreeBarkTint, _SnowColor;
    half _Brightness, _ColorVariationStrength, _VerticalGradientStrength;
    half _ColorHeightMin, _ColorHeightMax, _AmbientStrength, _Smoothness, _SpecularStrength;
    half _UseVertexColor, _SnowCoverage;
    half _BumpScale;
CBUFFER_END
float3 _LightDirection, _LightPosition;
struct SugarMapleBarkAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    #if defined(_BARK_NORMALMAP)
        float4 tangentOS : TANGENT;
    #endif
    float2 uv : TEXCOORD0;
    half4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct SugarMapleBarkVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    half3 normalWS : TEXCOORD2;
    float3 positionOS : TEXCOORD3;
    float4 shadowCoord : TEXCOORD4;
    half fogFactor : TEXCOORD5;
    half3 vertexLighting : TEXCOORD6;
    #if defined(_BARK_NORMALMAP)
        half4 tangentWS : TEXCOORD7;
    #endif
    half4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};
SugarMapleBarkVaryings SugarMapleBarkVertex(SugarMapleBarkAttributes input)
{
    SugarMapleBarkVaryings output = (SugarMapleBarkVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    output.positionOS = input.positionOS.xyz;
    output.positionWS = ApplyStandingTreeSway(TransformObjectToWorld(input.positionOS.xyz));
    output.positionCS = TransformWorldToHClip(output.positionWS);
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    #if defined(_BARK_NORMALMAP)
        output.tangentWS = half4(TransformObjectToWorldDir(input.tangentOS.xyz),
                                 input.tangentOS.w * GetOddNegativeScale());
    #endif
    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
    #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
        output.shadowCoord = TransformWorldToShadowCoord(output.positionWS);
    #endif
    output.fogFactor = ComputeFogFactor(output.positionCS.z);
    output.vertexLighting = VertexLighting(output.positionWS, output.normalWS);
    output.color = input.color;
    return output;
}
void SugarMapleBarkFade(SugarMapleBarkVaryings input)
{
    ApplyDistantTreeFade(input.positionCS.xy);
    #if defined(LOD_FADE_CROSSFADE)
        LODFadeCrossFade(input.positionCS);
    #endif
}
half3 SugarMapleBarkNormal(SugarMapleBarkVaryings input)
{
    #if defined(_BARK_NORMALMAP)
        half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
        half3 bitangent = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
        return NormalizeNormalPerPixel(TransformTangentToWorld(normalTS,
            half3x3(input.tangentWS.xyz, bitangent, input.normalWS)));
    #else
        return NormalizeNormalPerPixel(input.normalWS);
    #endif
}
half3 SampleBarkAlbedo(float2 uv, float3 positionWS)
{
    float2 uvDx = ddx(uv);
    float2 uvDy = ddy(uv);
    half blend = _BarkDistanceTiling > 0.0h
        ? smoothstep(8.0, 24.0, distance(positionWS, _WorldSpaceCameraPos))
        : 0.0h;

    UNITY_BRANCH if (blend <= 0.0h)
        return SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, uv, uvDx, uvDy).rgb;

    float scale = _BarkDistanceTiling;
    float2 coarseUV = (uv - _BaseMap_ST.zw) * scale + _BaseMap_ST.zw;
    half3 coarse = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, coarseUV, uvDx * scale, uvDy * scale).rgb;
    UNITY_BRANCH if (blend >= 1.0h)
        return coarse;

    half3 fine = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, uv, uvDx, uvDy).rgb;
    return lerp(fine, coarse, blend);
}
half4 SugarMapleBarkFragment(SugarMapleBarkVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    #if defined(_BARK_NORMALMAP)
        SugarMapleBarkFade(input);
    #elif defined(LOD_FADE_CROSSFADE)
        LODFadeCrossFade(input.positionCS);
    #endif
    // Preserve the painted base color; it is not a high-contrast crack mask.
    half3 bark = SampleBarkAlbedo(input.uv, input.positionWS) * _BaseColor.rgb * _Brightness;
    half variation = sin(dot(input.positionOS, float3(.61, .27, .48))) * _ColorVariationStrength;
    half height = saturate((input.positionOS.y - _ColorHeightMin) / max(_ColorHeightMax - _ColorHeightMin, .0001));
    bark *= (1 + variation) * lerp(1 - _VerticalGradientStrength, 1 + _VerticalGradientStrength, height);
    bark *= StandingTreeBarkTint(_TreeBarkTint).rgb;
    bark *= lerp(half3(1,1,1), input.color.rgb, _UseVertexColor);
    half3 normal = SugarMapleBarkNormal(input);
    bark = FSForestBarkColor(bark);
    bark = lerp(bark, _SnowColor.rgb, saturate(StandingTreeSnow(_SnowCoverage) * smoothstep(.2h,.8h,normal.y)));
    InputData data = InitializeTreeSimpleLitInputData(input.positionWS, normal, input.positionCS,
        ResolveTreeShadowCoord(input.positionWS, input.shadowCoord), _AmbientStrength);
    UNITY_BRANCH if (_BarkCanopyShelter > 0.0h)
        data.bakedGI *= lerp(1.0h, FSForestCrownAmbient(input.positionWS,
            NormalizeNormalPerPixel(input.normalWS), 1.0h), _BarkCanopyShelter);
    data.vertexLighting = input.vertexLighting;
    SurfaceData surface = InitializeTreeSimpleLitSurfaceData(bark, 1, _Smoothness, _SpecularStrength);
    half4 color = ShadeDistantAwareTree(data, surface);
    if (_DistantTreeEnabled < .5 || _DistantTreeBillboard < .5)
        color.rgb = MixFog(color.rgb, input.fogFactor);
    return half4(color.rgb, 1);
}
SugarMapleBarkVaryings SugarMapleBarkShadowVertex(SugarMapleBarkAttributes input)
{
    SugarMapleBarkVaryings output = SugarMapleBarkVertex(input);
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
half4 SugarMapleBarkDepthFragment(SugarMapleBarkVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    SugarMapleBarkFade(input);
    return 0;
}
half4 SugarMapleBarkNormalsFragment(SugarMapleBarkVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    SugarMapleBarkFade(input);
    half3 normal = SugarMapleBarkNormal(input);
    #if defined(_GBUFFER_NORMALS_OCT)
        float2 packed = saturate(PackNormalOctQuadEncode(normal) * .5 + .5);
        return half4(PackFloat2To888(packed), 0);
    #else
        return half4(normal, 0);
    #endif
}
#endif
