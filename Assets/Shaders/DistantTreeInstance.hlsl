#ifndef DISTANT_TREE_INSTANCE_INCLUDED
#define DISTANT_TREE_INSTANCE_INCLUDED

// Matches DistantTreeGpuBatch.VisibleInstance (96 bytes).
struct DistantTreeInstance
{
    float4x4 objectToWorld;
    float4 tint;
    float4 fade;
};

#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
StructuredBuffer<DistantTreeInstance> _DistantTreeInstances;
#endif

void SetupDistantTree()
{
#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
    float4x4 transform = _DistantTreeInstances[unity_InstanceID].objectToWorld;
    unity_ObjectToWorld = transform;
    // The manifest contains TRS matrices. Invert their orthogonal, possibly
    // non-uniformly scaled columns for the fixed-plane species' normal transforms.
    float3 x = transform._m00_m10_m20;
    float3 y = transform._m01_m11_m21;
    float3 z = transform._m02_m12_m22;
    x /= max(dot(x, x), 1e-12);
    y /= max(dot(y, y), 1e-12);
    z /= max(dot(z, z), 1e-12);
    float3 p = transform._m03_m13_m23;
    unity_WorldToObject = float4x4(
        float4(x, -dot(x, p)), float4(y, -dot(y, p)),
        float4(z, -dot(z, p)), float4(0, 0, 0, 1));
#endif
}

half4 GetDistantTreeTint(half4 fallbackTint)
{
#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
    return _DistantTreeInstances[unity_InstanceID].tint;
#else
    return fallbackTint;
#endif
}
#endif
