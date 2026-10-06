#ifndef RED_MAPLE_STYLIZED_BARK_INCLUDED
#define RED_MAPLE_STYLIZED_BARK_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#include "Assets/Shaders/TreeSimpleLitCommon.hlsl"
TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);
CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor, _TreeBarkTint, _SnowColor;
    half _Brightness, _ColorVariationStrength, _VerticalGradientStrength;
    half _BarkSaturation, _BarkContrast, _BarkMidpoint;
    half _ColorHeightMin, _ColorHeightMax, _AmbientStrength, _Smoothness, _SpecularStrength;
    half _UseVertexColor, _SnowCoverage;
CBUFFER_END
float3 _LightDirection, _LightPosition;
struct RedMapleBarkAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    half4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct RedMapleBarkVaryings
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
RedMapleBarkVaryings RedMapleBarkVertex(RedMapleBarkAttributes input)
{
    RedMapleBarkVaryings output = (RedMapleBarkVaryings)0;
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
void RedMapleBarkFade(RedMapleBarkVaryings input)
{
    ApplyDistantTreeFade(input.positionCS.xy);
    #if defined(LOD_FADE_CROSSFADE)
        LODFadeCrossFade(input.positionCS);
    #endif
}
half4 RedMapleBarkFragment(RedMapleBarkVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    #if defined(LOD_FADE_CROSSFADE)
        LODFadeCrossFade(input.positionCS);
    #endif
    // Reuse the sugar maple texture while making the same painted ridges read
    // softer and less brown. No new sample or UV discontinuity is introduced.
    half3 painted = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
    half luma = dot(painted, half3(.299h, .587h, .114h));
    half3 bark = lerp(luma.xxx, painted, _BarkSaturation);
    bark = max(0.0h, _BarkMidpoint + (bark - _BarkMidpoint) * _BarkContrast);
    bark *= _BaseColor.rgb * _Brightness;
    half variation = sin(dot(input.positionOS, float3(.61, .27, .48))) * _ColorVariationStrength;
    half height = saturate((input.positionOS.y - _ColorHeightMin) / max(_ColorHeightMax - _ColorHeightMin, .0001));
    bark *= (1 + variation) * lerp(1 - _VerticalGradientStrength, 1 + _VerticalGradientStrength, height);
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
RedMapleBarkVaryings RedMapleBarkShadowVertex(RedMapleBarkAttributes input)
{
    RedMapleBarkVaryings output = RedMapleBarkVertex(input);
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
half4 RedMapleBarkDepthFragment(RedMapleBarkVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    RedMapleBarkFade(input);
    return 0;
}
half4 RedMapleBarkNormalsFragment(RedMapleBarkVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    RedMapleBarkFade(input);
    half3 normal = NormalizeNormalPerPixel(input.normalWS);
    #if defined(_GBUFFER_NORMALS_OCT)
        float2 packed = saturate(PackNormalOctQuadEncode(normal) * .5 + .5);
        return half4(PackFloat2To888(packed), 0);
    #else
        return half4(normal, 0);
    #endif
}
#endif

