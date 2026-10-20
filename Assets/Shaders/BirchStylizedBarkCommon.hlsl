#ifndef BIRCH_STYLIZED_BARK_INCLUDED
#define BIRCH_STYLIZED_BARK_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#include "Assets/Shaders/TreeSimpleLitCommon.hlsl"
#include "Assets/Shaders/BirchBarkSurface.hlsl"
TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);
CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor, _TreeBarkTint, _SnowColor, _WarmBarkColor, _ScarColor;
    half _ScarThreshold, _ScarSoftness, _ScarStrength;
    half _Brightness, _ColorVariationStrength, _VerticalGradientStrength;
    half _ColorHeightMin, _ColorHeightMax, _AmbientStrength, _Smoothness, _SpecularStrength;
    half _UseVertexColor, _SnowCoverage;
CBUFFER_END
float3 _LightDirection, _LightPosition;
struct BirchBarkAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    half4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct BirchBarkVaryings
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
BirchBarkVaryings BirchBarkVertex(BirchBarkAttributes input)
{
    BirchBarkVaryings output = (BirchBarkVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    output.positionOS = input.positionOS.xyz;
    output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
    output.positionCS = TransformWorldToHClip(output.positionWS);
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
    output.shadowCoord = TransformWorldToShadowCoord(output.positionWS);
    output.fogFactor = ComputeFogFactor(output.positionCS.z);
    output.vertexLighting = VertexLighting(output.positionWS, output.normalWS);
    output.color = input.color;
    return output;
}
void BirchBarkFade(BirchBarkVaryings input)
{
    ApplyDistantTreeFade(input.positionCS.xy);
    #if defined(LOD_FADE_CROSSFADE)
        LODFadeCrossFade(input.positionCS);
    #endif
}
half4 BirchBarkFragment(BirchBarkVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    #if defined(LOD_FADE_CROSSFADE)
        LODFadeCrossFade(input.positionCS);
    #endif
    half3 bark = BirchBarkAlbedo(SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).rgb,
        input.positionOS,_BaseColor.rgb,_WarmBarkColor.rgb,_ScarColor.rgb,_ScarThreshold,_ScarSoftness,
        _ScarStrength,_ColorVariationStrength,_VerticalGradientStrength,_ColorHeightMin,_ColorHeightMax,_Brightness);
    bark *= StandingTreeBarkTint(_TreeBarkTint).rgb;
    bark *= lerp(half3(1,1,1), input.color.rgb, _UseVertexColor);
    half3 normal = NormalizeNormalPerPixel(input.normalWS);
    bark = lerp(bark, _SnowColor.rgb, saturate(StandingTreeSnow(_SnowCoverage) * smoothstep(.2h,.8h,normal.y)));
    InputData data = InitializeTreeSimpleLitInputData(input.positionWS, normal, input.positionCS,
        input.shadowCoord, _AmbientStrength);
    data.vertexLighting = input.vertexLighting;
    SurfaceData surface = InitializeTreeSimpleLitSurfaceData(bark, 1, _Smoothness, _SpecularStrength);
    half4 color = ShadeDistantAwareTree(data, surface);
    if (_DistantTreeEnabled < .5 || _DistantTreeBillboard < .5)
        color.rgb = MixFog(color.rgb, input.fogFactor);
    return half4(color.rgb, 1);
}
BirchBarkVaryings BirchBarkShadowVertex(BirchBarkAttributes input)
{
    BirchBarkVaryings output = BirchBarkVertex(input);
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
half4 BirchBarkDepthFragment(BirchBarkVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    BirchBarkFade(input);
    return 0;
}
half4 BirchBarkNormalsFragment(BirchBarkVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    BirchBarkFade(input);
    half3 normal = NormalizeNormalPerPixel(input.normalWS);
    #if defined(_GBUFFER_NORMALS_OCT)
        float2 packed = saturate(PackNormalOctQuadEncode(normal) * .5 + .5);
        return half4(PackFloat2To888(packed), 0);
    #else
        return half4(normal, 0);
    #endif
}
#endif
