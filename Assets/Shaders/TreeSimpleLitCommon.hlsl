#ifndef TREE_SIMPLE_LIT_COMMON_INCLUDED
#define TREE_SIMPLE_LIT_COMMON_INCLUDED

half _TreeNightAmbientFloorDimAmount;
half _TreeNightAmbientFloorScaleAtMidnight;

InputData InitializeTreeSimpleLitInputData(
    float3 positionWS,
    half3 normalWS,
    float4 positionCS,
    float4 shadowCoord,
    half ambientStrength)
{
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

#endif
