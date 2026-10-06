#ifndef TREE_SIMPLE_LIT_COMMON_INCLUDED
#define TREE_SIMPLE_LIT_COMMON_INCLUDED

#include "Assets/Shaders/DistantTreeFade.hlsl"
#include "Assets/Shaders/TreeSeasonSimulation.hlsl"

half _TreeNightAmbientFloorDimAmount;
half _TreeNightAmbientFloorScaleAtMidnight;

InputData InitializeTreeSimpleLitInputData(
    float3 positionWS,
    half3 normalWS,
    float4 positionCS,
    float4 shadowCoord,
    half ambientStrength)
{
    ApplyDistantTreeFade(positionCS.xy);
    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    inputData.normalWS = NormalizeNormalPerPixel(normalWS);
    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(positionWS);
    inputData.shadowCoord = shadowCoord;
    half floorScaleAtMidnight = saturate(_TreeNightAmbientFloorScaleAtMidnight);
    half nightDimAmount = saturate(_TreeNightAmbientFloorDimAmount);
    half scaledAmbientStrength = ambientStrength * lerp(1.0h, floorScaleAtMidnight, nightDimAmount);
    inputData.bakedGI = max(SampleSH(inputData.normalWS), scaledAmbientStrength.xxx);
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
    inputData.shadowMask = half4(1.0h, 1.0h, 1.0h, 1.0h);
    return inputData;
}

SurfaceData InitializeTreeSimpleLitSurfaceData(
    half3 albedo,
    half alpha,
    half smoothness,
    half specularStrength)
{
    SurfaceData surfaceData = (SurfaceData)0;
    surfaceData.albedo = saturate(albedo);
    surfaceData.alpha = alpha;
    surfaceData.specular = specularStrength.xxx;
    surfaceData.smoothness = smoothness;
    surfaceData.normalTS = half3(0.0h, 0.0h, 1.0h);
    surfaceData.emission = half3(0.0h, 0.0h, 0.0h);
    surfaceData.occlusion = 1.0h;
    return surfaceData;
}

half3 EvaluateTreeLeafBacklighting(
    InputData inputData,
    half3 albedo,
    half3 normalWS,
    half strength,
    half3 tint)
{
    if (strength <= 0.0h)
        return half3(0.0h, 0.0h, 0.0h);

    // Match the distant billboard path, which deliberately skips shadow sampling.
    Light sun;
    if (_DistantTreeEnabled > 0.5 && _DistantTreeBillboard > 0.5)
        sun = GetMainLight();
    else
        sun = GetMainLight(inputData.shadowCoord);

    half transmission = saturate(-dot(normalWS, sun.direction));
    return albedo * tint * sun.color * transmission *
        sun.distanceAttenuation * sun.shadowAttenuation * strength;
}

half4 ShadeDistantAwareTree(InputData inputData, SurfaceData surfaceData)
{
    if (_DistantTreeEnabled > 0.5 && _DistantTreeBillboard > 0.5)
    {
        Light light = GetMainLight();
        half diffuse = saturate(dot(inputData.normalWS, light.direction));
        half3 color = surfaceData.albedo * (inputData.bakedGI + light.color * diffuse) + surfaceData.emission;
        float fog = ComputeFogFactor(TransformWorldToHClip(inputData.positionWS).z);
        return half4(MixFog(color, fog), surfaceData.alpha);
    }
    return UniversalFragmentBlinnPhong(inputData, surfaceData);
}

#endif
