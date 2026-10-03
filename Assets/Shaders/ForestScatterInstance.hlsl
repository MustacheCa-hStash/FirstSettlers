#ifndef FOREST_SCATTER_INSTANCE_INCLUDED
#define FOREST_SCATTER_INSTANCE_INCLUDED
struct ForestScatterSource
{
    float4x4 transform;
    float4 tint, scatter, sphere, selection;
};
#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
StructuredBuffer<ForestScatterSource> _ForestSources;
StructuredBuffer<uint> _ForestVisible;
float4x4 _ForestMeshLocal;
#endif
void SetupForestScatter()
{
#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
    float4x4 transform = mul(_ForestSources[_ForestVisible[unity_InstanceID]].transform, _ForestMeshLocal);
    unity_ObjectToWorld = transform;
    float3 x = transform._m00_m10_m20, y = transform._m01_m11_m21, z = transform._m02_m12_m22;
    x /= max(dot(x,x),1e-12); y /= max(dot(y,y),1e-12); z /= max(dot(z,z),1e-12);
    float3 p = transform._m03_m13_m23;
    unity_WorldToObject = float4x4(float4(x,-dot(x,p)),float4(y,-dot(y,p)),float4(z,-dot(z,p)),float4(0,0,0,1));
#endif
}
half4 ForestScatterTint(half4 fallback)
{
#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
    return _ForestSources[_ForestVisible[unity_InstanceID]].tint;
#else
    return fallback;
#endif
}
float4 ForestScatterParams(float4 fallback)
{
#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
    return _ForestSources[_ForestVisible[unity_InstanceID]].scatter;
#else
    return fallback;
#endif
}
#endif
