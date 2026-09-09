#ifndef GRASS_INDIRECT_INSTANCE_INCLUDED
#define GRASS_INDIRECT_INSTANCE_INCLUDED

// Exactly the existing matrix + forest blend / rank-derived wind phase payload.
struct GrassIndirectInstance
{
    float4x4 objectToWorld;
    float4 data;
};

#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
StructuredBuffer<GrassIndirectInstance> _GrassInstances;
StructuredBuffer<uint> _GrassVisibleIndices;
#endif

void SetupGrassIndirect()
{
#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
    float4x4 transform = _GrassInstances[_GrassVisibleIndices[unity_InstanceID]].objectToWorld;
    unity_ObjectToWorld = transform;
    float3 x = transform._m00_m10_m20;
    float3 y = transform._m01_m11_m21;
    float3 z = transform._m02_m12_m22;
    // Inverse TRS preserves normals under non-uniform instance scale.
    x /= max(dot(x, x), 1e-12);
    y /= max(dot(y, y), 1e-12);
    z /= max(dot(z, z), 1e-12);
    float3 p = transform._m03_m13_m23;
    unity_WorldToObject = float4x4(float4(x, -dot(x, p)), float4(y, -dot(y, p)),
        float4(z, -dot(z, p)), float4(0, 0, 0, 1));
#endif
}

float4 GetGrassInstanceData(float4 fallbackData)
{
#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
    return _GrassInstances[_GrassVisibleIndices[unity_InstanceID]].data;
#else
    return fallbackData;
#endif
}
#endif
